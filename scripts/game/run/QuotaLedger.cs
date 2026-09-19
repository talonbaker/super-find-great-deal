using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;
using MpFoundation.Net;

namespace Sail.Game.Run;

/// <summary>
/// The winter-cache quota ledger (core-spine spec §2, CORE-PROG-A1) — a plain Node child of
/// Gameplay, added identically on every peer, holding every [Rpc] the quota system owns (the
/// every-peer law). Server-authoritative and CUMULATIVE (spec §2.1): within a
/// playthrough <see cref="CumulativeBanked"/> only ever grows; the round-N verdict checks it
/// against cumulative demand, so surplus banked in round N automatically counts toward round
/// N+1 — the winter cache is a stockpile, not a nightly bucket. The schedule's
/// <c>CarrySurplus = false</c> flips the verdict to a per-round bucket with no code change
/// (see <see cref="QuotaMath.QuotaMissed"/>; SD-1 decision D3).
///
/// <b>Replication.</b> Banked changes are discrete transitions ⇒ reliable + ordered +
/// CallLocal on <see cref="NetCodec.RunChannel"/> — deliberately the SAME channel as the
/// crossings and the verdict, so no client can ever display a banked count the verdict
/// contradicts (spec §2.3). Broadcasts carry absolute values, never increments, so a
/// duplicate application is idempotent by construction. Round-begin values (demand, cumulative
/// demand) ride <see cref="PlaythroughDriver"/>'s own RoundIntro broadcast (one wire message
/// per transition, spec §3.2) via <see cref="ApplyRoundBegin"/> — a client NEVER computes a
/// demand from its own copy of the .tres (only the server's numbers are authoritative,
/// version-skew immunity, spec §2.2). Late join: <see cref="SendQuotaTo"/> in the
/// OnPeerConnected funnel, after SendFlowStateTo (spec §3.4).
///
/// <b>Persistence scope</b>: run-scoped (spec §5.2 — the cache is the playthrough's score).
/// A registered <see cref="IWorldStateSlice"/> (CORE-PROG-A2): the store's boundary fan is the
/// only reset path, reached from PlaythroughDriver's entry guard on the server and the RunReset
/// broadcast on every peer — reset is idempotent, so the documented T10 double pass is harmless
/// (spec §5.4). The `_seq` rule: the wire sequence is never rewound by
/// a reset.
///
/// <b>Banking</b> (spec §2.4): no drop-off interactable exists yet — <see cref="ServerBank"/>
/// is the declared slot the future camp drop-off verb calls (its prompt/range/animation are
/// future /spec-interaction work). Guard: accepted while play is live (RoundIntro/InRound);
/// otherwise denied with feedback to the requester only (the typed-denial idiom, ordinals
/// append-only) and the haul is NEVER consumed on a denial — this method never touches props
/// at all; the caller keeps what it was carrying (spec §6 case 6).
/// </summary>
public partial class QuotaLedger : Node, IWorldStateSlice
{
    public const string NodeName = "QuotaLedger";

    public static QuotaLedger? Instance { get; private set; }

    /// <summary>Why a bank request was refused. Ordinals cross the wire — append only
    /// (the typed-denial precedent).</summary>
    public enum QuotaDenial
    {
        None = 0,
        /// <summary>Play is not live — RoundEnd, UpgradeLobby, Loss or Boot (spec §5.6's
        /// in-flight-request rule: a deposit racing the verdict lands here a tick later and
        /// keeps its haul; the same items bank five seconds into the next day).</summary>
        NotAcceptingNow = 1,
    }

    private bool _isServer;
    private int[] _early = QuotaMath.FallbackEarlyRounds;
    private float _factor = 1.35f;
    private bool _carrySurplus = true;
    private uint _seq; // never rewound (the _seq rule).

    /// <summary>False until this peer has authoritative quota state — server: from Setup;
    /// client: once SyncQuotaTo lands. Same contract as RunDriver.Synced.</summary>
    public bool Synced { get; private set; }

    public int CumulativeBanked { get; private set; }
    public int BankedThisRound { get; private set; }
    public int CurrentRound { get; private set; } = 1;

    /// <summary>This round's own demand — replicated; on a client this is whatever the last
    /// RoundIntro broadcast / quota sync carried, never locally derived.</summary>
    public int DemandCurrentRound { get; private set; }

    /// <summary>Σ demand(1..CurrentRound) — the number the carry-surplus verdict compares
    /// against. Replicated like <see cref="DemandCurrentRound"/>.</summary>
    public int CumulativeDemand { get; private set; }

    /// <summary>Remaining need for the diegetic display (spec §3.5 row 5): integers only,
    /// no meter — the register law's meter ban holds one layer down.</summary>
    public int RemainingNeed => Math.Max(0, (_carrySurplus ? CumulativeDemand : DemandCurrentRound)
        - (_carrySurplus ? CumulativeBanked : BankedThisRound));

    public bool CarrySurplus => _carrySurplus;

    /// <summary>Raised on every peer (CallLocal) per applied bank: (cumulativeBanked, delta).</summary>
    public event Action<int, int>? BankedChanged;

    /// <summary>Raised on the requesting peer when the server refuses a deposit — the
    /// typed-denied shape; UI consumption is B1's.</summary>
    public event Action<QuotaDenial>? QuotaDenied;

    /// <summary>Test hook: the most recent denial delivered to THIS peer (BotHarness samples
    /// it — the "denial reaches only the requester" live assertion, Run-FlowTest.ps1).</summary>
    internal QuotaDenial LastDenial { get; private set; } = QuotaDenial.None;

    // --- test-only scheduled banking (--quota-bank-at): the drop-off stand-in ---------------
    private readonly List<(double AtSec, int Units, int ConnectIndex, bool Fired)> _testBanks = new();
    private readonly Dictionary<int, int> _peerByConnectIndex = new(); // connect order -> peer id
    private double _elapsedSec;

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>schedule null (missing/corrupt .tres) falls back to QuotaMath's defensive
    /// defaults rather than crashing a session over a data file — the driver logs it.
    /// earlyOverride is the --quota-early test hook (server-side only, like every seed hook).</summary>
    public void Setup(bool isServer, QuotaSchedule? schedule, int[]? earlyOverride = null,
        List<(double AtSec, int Units, int ConnectIndex)>? testBankSchedule = null)
    {
        _isServer = isServer;
        if (schedule != null)
        {
            _early = schedule.EarlyRoundsDemand is { Length: > 0 }
                ? schedule.EarlyRoundsDemand
                : QuotaMath.FallbackEarlyRounds;
            _factor = schedule.TailGrowthFactor;
            _carrySurplus = schedule.CarrySurplus;
        }
        if (isServer && earlyOverride is { Length: > 0 })
            _early = earlyOverride;
        if (isServer && testBankSchedule != null)
            foreach ((double at, int units, int idx) in testBankSchedule)
                _testBanks.Add((at, units, idx, false));

        // CORE-PROG-A2: migrated from a direct RunReset subscription to a registered store
        // slice — the store is now the single world-state RunReset subscriber and fans this
        // slice in construction order (spec §5.3). Null-safe: unit/self-test contexts with no
        // store simply never reset, exactly as they never had a RunDriver to subscribe to.
        Sail.Game.Run.WorldStateStore.Instance?.Register(this);

        Synced = isServer;
    }

    public int DemandFor(int round) => QuotaMath.Demand(round, _early, _factor);

    public int CumulativeDemandThrough(int round) => QuotaMath.CumulativeDemand(round, _early, _factor);

    /// <summary>Applied from PlaythroughDriver's RoundIntro broadcast handler on EVERY peer
    /// (server included, via CallLocal) — the one wire message that carries a round's demand
    /// numbers. Idempotent: absolute values.</summary>
    public void ApplyRoundBegin(int round, int demandRound, int cumulativeDemand)
    {
        CurrentRound = round;
        DemandCurrentRound = demandRound;
        CumulativeDemand = cumulativeDemand;
        BankedThisRound = 0;
    }

    /// <summary>The verdict-instant question (spec §1.6's one shipped predicate reads this).</summary>
    public bool IsQuotaMissed() => QuotaMath.QuotaMissed(_carrySurplus,
        CumulativeBanked, CumulativeDemand, BankedThisRound, DemandCurrentRound);

    /// <summary>Server-side only — the camp drop-off's deposit verb, and the test harness's
    /// (spec §2.4's declared slot). See the class doc for the guard and the never-consume
    /// rule. cacheUnits &lt;= 0 is a no-op false with no denial (nothing was offered).</summary>
    public bool ServerBank(int peerId, int cacheUnits, out QuotaDenial denial)
    {
        denial = QuotaDenial.None;
        if (!_isServer || cacheUnits <= 0)
            return false;
        // The state guard reads the driver's post-commit state — safe against in-flight
        // requests by the probe-1 mechanism: a boundary that committed earlier this tick has
        // already applied locally, so a request arriving after it sees the closed window.
        PlaythroughState state = PlaythroughDriver.Instance?.State ?? PlaythroughState.Boot;
        if (!QuotaMath.BankingOpen(state))
        {
            denial = QuotaDenial.NotAcceptingNow;
            DeliverDenial(peerId, denial);
            return false;
        }
        int cum = (int)Math.Min((long)CumulativeBanked + cacheUnits, QuotaMath.DemandClamp);
        int round = (int)Math.Min((long)BankedThisRound + cacheUnits, QuotaMath.DemandClamp);
        int delta = cum - CumulativeBanked;
        Rpc(MethodName.BroadcastBank, cum, round, delta, ++_seq);
        return true;
    }

    /// <summary>Server -> everyone (CallLocal): absolute post-bank values, applied
    /// identically wherever it runs — the RunDriver.BroadcastPhaseCrossed idiom. Reliable +
    /// ordered on RunChannel so banked state and the verdict share one arrival order.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastBank(int cumulativeBanked, int bankedThisRound, int delta, uint seq)
    {
        CumulativeBanked = cumulativeBanked;
        BankedThisRound = bankedThisRound;
        BankedChanged?.Invoke(cumulativeBanked, delta);
    }

    /// <summary>Server -> the one requester whose deposit was refused (the typed-denial
    /// exact shape, host-as-player short-circuit included). Reliable: a dropped rejection is
    /// a silent failure — INTERACTION-BIBLE §2's defect class.</summary>
    private void DeliverDenial(int peerId, QuotaDenial denial)
    {
        if (peerId <= 0)
            return; // server-initiated test call with no real requester — nothing to notify.
        if (peerId == Multiplayer.GetUniqueId())
            OnQuotaDenied((int)denial);
        else
            RpcId(peerId, MethodName.OnQuotaDenied, (int)denial);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel)]
    private void OnQuotaDenied(int denial)
    {
        LastDenial = (QuotaDenial)denial;
        QuotaDenied?.Invoke((QuotaDenial)denial);
    }

    /// <summary>Server-only: Gameplay.OnPeerConnected funnel, after SendFlowStateTo (spec
    /// §3.4). A joiner's ledger must match the server's before its world reads as playing.</summary>
    public void SendQuotaTo(int peerId)
    {
        if (!_isServer)
            return;
        RpcId(peerId, MethodName.SyncQuotaTo,
            CumulativeBanked, BankedThisRound, CurrentRound, DemandCurrentRound, CumulativeDemand, ++_seq);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncQuotaTo(int cumulativeBanked, int bankedThisRound, int round,
        int demandRound, int cumulativeDemand, uint seq)
    {
        CumulativeBanked = cumulativeBanked;
        BankedThisRound = bankedThisRound;
        CurrentRound = round;
        DemandCurrentRound = demandRound;
        CumulativeDemand = cumulativeDemand;
        Synced = true;
    }

    // --- the playthrough boundary ----------------------------------------------------------

    /// <summary>IWorldStateSlice (A2 registers it; idempotent by construction). Zeroes the
    /// cache; demand fields for the new playthrough arrive with the next RoundIntro(1)
    /// broadcast, ordered after the reset on the same channel (spec §5.4's T10 sequence).
    /// The wire _seq is deliberately NOT rewound (class doc).</summary>
    public string SliceId => "quota-ledger";

    public void ResetForNewPlaythrough()
    {
        CumulativeBanked = 0;
        BankedThisRound = 0;
        CurrentRound = 1;
    }

    // --- test-only scheduled banking ---------------------------------------------------------

    /// <summary>Test hook, from Gameplay.OnPeerConnected: connect-order-index -> peer id, the
    /// same convention an earlier test-seeding flag used (peer ids are unpredictable; connect order
    /// is not).</summary>
    public void TestRegisterConnectIndex(int connectIndex, int peerId) =>
        _peerByConnectIndex[connectIndex] = peerId;

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer || _testBanks.Count == 0)
            return;
        _elapsedSec += delta;
        for (int i = 0; i < _testBanks.Count; i++)
        {
            (double at, int units, int idx, bool fired) = _testBanks[i];
            if (fired || _elapsedSec < at || !_peerByConnectIndex.TryGetValue(idx, out int peer))
                continue;
            _testBanks[i] = (at, units, idx, true);
            bool ok = ServerBank(peer, units, out QuotaDenial denial);
            GD.Print($"[quota] test-bank at={at}s units={units} peer={peer} " +
                     $"accepted={ok} denial={denial} cumBanked={CumulativeBanked}");
        }
    }
}
