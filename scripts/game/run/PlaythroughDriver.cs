using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;
using MpFoundation.Net;

namespace Sail.Game.Run;

/// <summary>
/// The Tier-2 playthrough machine's wire (core-spine spec §1, CORE-PROG-A1): a plain Node
/// child of Gameplay, added identically on every peer, holding every [Rpc] the playthrough
/// system owns (the identical-tree law). Layered ABOVE the locked <see cref="RunDriver"/>
/// exactly as RunDriver layers above <see cref="CycleDriver"/>: it subscribes to
/// PhaseCrossed and never modifies either file's contract. Server-detected, client-applied —
/// every transition is ONE reliable + ordered + CallLocal RPC on
/// <see cref="NetCodec.RunChannel"/>, the same channel as the crossings, so
/// crossing-before-verdict arrival order is structural on every peer (spec §1.6; measured by
/// tests/Run-NetProbeTest.ps1 before this file existed — the probe-first obligation).
///
/// The decisions live in <see cref="PlaythroughMachine"/> (pure, server-only, walked by
/// dotnet test); this node detects triggers, broadcasts commits, and applies them in the
/// CallLocal handlers — so the authority's own machine state advances synchronously inside
/// its broadcast (probe 1) and every guard evaluated after a commit reads post-commit state.
/// Each handler sets the queryable state FIRST, then fires <see cref="StateChanged"/>, then
/// the typed event — a handler for either always reads post-transition state (the RunReset
/// ordering contract, generalized — spec §3.2).
///
/// <b>The gating rule</b> (spec §1.2, binding downstream): bands are time, states are game.
/// During RoundEnd/UpgradeLobby/Loss the clock free-runs and the sky does whatever it says —
/// dawn rising behind the tally is a feature — but any system whose behavior keys on the
/// band for GAMEPLAY effect must additionally gate on this driver's State being band-live.
/// This packet gates no existing consumer (under real timing those states live entirely in
/// DawnSweep/early Day, where the night systems are already inert); the consumer list is in
/// the A1 report and CORE-VER-1 checks it against a grep.
///
/// <b>Run-end</b>: <see cref="RunConcluded"/> is THE only run-end under the open-ended model.
/// RunDriver.RunEndedSignal is neutralized (uncapped default run length, spec §1.5 / D5) and
/// no new subscriber may attach to it.
/// </summary>
public partial class PlaythroughDriver : Node
{
    public const string NodeName = "PlaythroughDriver";

    public static PlaythroughDriver? Instance { get; private set; }

    /// <summary>False until this peer has authoritative flow state — server: from Setup;
    /// client: once SyncFlowStateTo lands (the "Connecting…" gate, B1 surface 6). Same
    /// contract as RunDriver.Synced.</summary>
    public bool Synced { get; private set; }

    public PlaythroughState State { get; private set; } = PlaythroughState.Boot;

    /// <summary>1-based; valid from RoundIntro(1) onward.</summary>
    public int Round { get; private set; }

    /// <summary>Countdown for RoundIntro/RoundEnd/UpgradeLobby; -1 where no timer runs. On
    /// the server this mirrors the machine's authoritative timer; on a client it is local
    /// extrapolation between syncs — cosmetic, the server's own timer is the authority.</summary>
    public double StateRemainingSec { get; private set; } = -1;

    /// <summary>The one liveness query the gating rule's consumers ask (spec §1.2,
    /// CORE-PROG-A2 scope 6): is the band currently GAMEPLAY-live? False in Boot, RoundEnd,
    /// UpgradeLobby and Loss — the clock free-runs there and the sky does what it says, but
    /// nothing mechanical (payouts, cutoffs, freeze pressure) may key on it. Consumers guard
    /// as <c>PlaythroughDriver.Instance is { IsBandLive: false }</c> → skip, so a world with
    /// no driver (labs, self-tests) keeps today's always-live behavior.</summary>
    public bool IsBandLive => PlaythroughStates.IsBandLive(State);

    /// <summary>Latched at each RoundEnded; null before the first (and again after a new
    /// playthrough begins).</summary>
    public RoundSummary? LastRoundSummary { get; private set; }

    /// <summary>Latched at RunConcluded; null unless State == Loss.</summary>
    public RunOutcome? LastOutcome { get; private set; }

    /// <summary>Fires for EVERY transition: (from, to, round).</summary>
    public event Action<PlaythroughState, PlaythroughState, int>? StateChanged;

    public event Action<int /*round*/, int /*demandAuthoritative*/>? RoundIntroStarted;
    public event Action<int /*round*/>? RoundLive;
    public event Action<RoundSummary>? RoundEnded;
    public event Action<int /*roundJustSurvived*/, double /*durationSec*/>? UpgradeLobbyStarted;
    public event Action<RunOutcome>? RunConcluded;

    private bool _isServer;
    private bool _runsPlaythrough = true; // resolved in Setup; see WorldRunFlow.
    private double _periodSec;
    private PlaythroughMachine? _machine; // server only — clients apply broadcasts, no guards.
    private IWorldStateStore? _store;
    private QuotaLedger? _ledger;
    private uint _seq; // never rewound.

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        if (_isServer && RunDriver.Instance is { } driver)
            driver.PhaseCrossed -= OnRunPhaseCrossed;
    }

    /// <summary>store is CORE-PROG-A2's WorldStateStore — null until that packet lands, and
    /// every playthrough start says so loudly (see RunPlaythroughBoundary). periodSec is the
    /// same resolved value RunDriver captured (never re-derived at re-anchor time, the
    /// ResetRun reasoning). Timer args are the --flow-timers test hook; &lt;= 0 means "spec §8
    /// placeholder".
    ///
    /// <para>runsPlaythrough is resolved per world (<see cref="WorldRunFlow"/>) rather than
    /// passed down from Gameplay, for the same reason <c>HudProfile.Current</c> reads the world
    /// itself: the decision is a property of the world, one switch owns it, and no call site can
    /// answer it differently. Null (the default) means "ask the world"; the explicit overload is
    /// for tests that have no session.</para></summary>
    public void Setup(bool isServer, double periodSec, IWorldStateStore? store,
        double introSec = 0, double tallySec = 0, double lobbySec = 0,
        bool? runsPlaythrough = null)
    {
        _isServer = isServer;
        _periodSec = periodSec > 0 ? periodSec : RunDriver.DefaultCyclePeriodSec;
        _store = store;
        _runsPlaythrough = runsPlaythrough ?? WorldRunFlow.Current;
        if (isServer)
        {
            _machine = new PlaythroughMachine(
                introSec > 0 ? introSec : PlaythroughMachine.DefaultIntroSec,
                tallySec > 0 ? tallySec : PlaythroughMachine.DefaultTallySec,
                lobbySec > 0 ? lobbySec : PlaythroughMachine.DefaultLobbySec,
                _runsPlaythrough);
            if (_machine.TimersClamped)
                GD.PushWarning("[playthrough] a configured state timer was < 1s or invalid and was clamped (spec §1.7)");
            // Server-side verdict detection rides the locked crossing layer. RunDriver is
            // constructed before this driver (Gameplay._Ready — a correctness dependency,
            // documented at the construction site); its CallLocal broadcast invokes this
            // handler synchronously on the server, so the verdict broadcast is queued on
            // RunChannel immediately behind the crossing that triggered it (probe 2).
            if (RunDriver.Instance is { } driver)
                driver.PhaseCrossed += OnRunPhaseCrossed;
        }
        Synced = isServer;
    }

    /// <summary>Wired by Gameplay after QuotaLedger exists (constructed after this driver —
    /// it answers to the driver, order documented at the site). Also registers the one
    /// shipped predicate; chain order = registration order (spec §1.6).
    ///
    /// <para>LOSS-1: a world that runs no playthrough registers no predicate. The machine's own
    /// <see cref="PlaythroughMachine.RunsPlaythrough"/> gate already makes the verdict instant
    /// unreachable, so this is belt and braces — but it is also the honest statement: there is
    /// no quota to miss, so there is nothing to ask about it. The ledger itself is still wired
    /// (it replicates, it answers late joiners, and it costs nothing when nobody banks).</para></summary>
    public void SetQuotaLedger(QuotaLedger ledger)
    {
        _ledger = ledger;
        if (_runsPlaythrough)
            _machine?.RegisterLossPredicate(new QuotaMissedPredicate(ledger));
    }

    /// <summary>Extension point for future predicates (none shipped beyond the quota).</summary>
    public void RegisterLossPredicate(ILossPredicate predicate) => _machine?.RegisterLossPredicate(predicate);

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer || _machine == null)
            return;
        if (_machine.State == PlaythroughState.Boot)
        {
            // T3: world seeding completed synchronously inside Gameplay._Ready (StartAsServer
            // runs before the first physics tick), so the clock syncs are the gates.
            PlaythroughCommit? boot = _machine.TickBoot(
                CycleDriver.Instance is { Synced: true }, RunDriver.Instance is { Synced: true });
            if (boot is { } b)
                ExecuteCommit(b);
        }
        else
        {
            // The connected-peer set only ever matters to the all-ready skip, which only
            // exists in RoundEnd/UpgradeLobby (machine contract) — so the marshal + convert
            // is skipped everywhere else rather than allocating per physics tick for states
            // that ignore it. Suites like L12 run five processes on one machine; the server
            // tick stays as close to its pre-A1 cost as possible.
            int[] peers = _machine.State is PlaythroughState.RoundEnd or PlaythroughState.UpgradeLobby
                ? ConnectedPeers()
                : Array.Empty<int>();
            PlaythroughCommit? timed = _machine.TickTimers(delta, peers);
            if (timed is { } t)
                ExecuteCommit(t);
        }
        StateRemainingSec = _machine.StateRemainingSec;
    }

    public override void _Process(double delta)
    {
        // Client-local cosmetic countdown between syncs (spec §3.2's StateRemainingSec note).
        if (!_isServer && StateRemainingSec > 0)
            StateRemainingSec = Math.Max(0, StateRemainingSec - delta);
    }

    private int[] ConnectedPeers() => Multiplayer.GetPeers();

    private void OnRunPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        if (!_isServer || _machine == null)
            return;
        PlaythroughCommit? verdict = _machine.OnPhaseCrossed(kind, cyclesElapsedAfter);
        if (verdict is { } v)
            ExecuteCommit(v);
    }

    /// <summary>Spec §1.4's one-tick order, made code: (crossing already applied and its
    /// broadcast queued by RunDriver) → predicates already ran (in the machine) → exactly one
    /// commit broadcast here, on the same channel → local application inside the Rpc call.</summary>
    private void ExecuteCommit(PlaythroughCommit commit)
    {
        switch (commit.Kind)
        {
            case PlaythroughCommitKind.RoundIntro:
            {
                if (commit.IsNewPlaythrough)
                    RunPlaythroughBoundary(commit.ViaPlayAgain);
                if (commit.ReanchorClock)
                    ReanchorClock(roundJustSurvived: commit.Round - 1);
                int demand = _ledger?.DemandFor(commit.Round) ?? 0;
                int cumDemand = _ledger?.CumulativeDemandThrough(commit.Round) ?? 0;
                Rpc(MethodName.BroadcastRoundIntro, commit.Round, demand, cumDemand, _machine!.IntroSec, ++_seq);
                break;
            }
            case PlaythroughCommitKind.RoundLive:
                Rpc(MethodName.BroadcastRoundLive, commit.Round, ++_seq);
                break;
            case PlaythroughCommitKind.RoundEnd:
            {
                int nextDemand = _ledger?.DemandFor(commit.Round + 1) ?? 0;
                Rpc(MethodName.BroadcastRoundEnded, commit.Round,
                    _ledger?.CumulativeDemand ?? 0, _ledger?.CumulativeBanked ?? 0,
                    nextDemand, _machine!.TallySec, ++_seq);
                break;
            }
            case PlaythroughCommitKind.UpgradeLobby:
                Rpc(MethodName.BroadcastUpgradeLobby, commit.Round, _machine!.LobbySec, ++_seq);
                break;
            case PlaythroughCommitKind.Loss:
            {
                RunOutcome o = commit.Outcome!.Value;
                Rpc(MethodName.BroadcastRunConcluded, (byte)o.Kind, o.Round, o.Demand, o.Banked, ++_seq);
                break;
            }
        }
    }

    /// <summary>Spec §5.4: the playthrough boundary is an ENTRY guard — run unconditionally
    /// inside every commit into RoundIntro(1), session start and Play Again alike (slices are
    /// idempotent, so the pristine first pass is a cheap no-op that buys an unconditional
    /// invariant). T10's sequence: store reset → ResetRun() → RoundIntro broadcast; ResetRun
    /// is CallLocal, so its RunReset fan-out and clock rewind complete synchronously before
    /// the RoundIntro broadcast is queued (probe 1), and channel ordering delivers
    /// reset-before-intro on every client (probe 2).</summary>
    private void RunPlaythroughBoundary(bool viaPlayAgain)
    {
        if (_store != null)
        {
            _store.ResetForNewPlaythrough();
        }
        else
        {
            // Loud by design (packet scope 7): a missing store must never be silent. Until
            // CORE-PROG-A2 wires WorldStateStore, cross-playthrough cleanliness rides
            // RunReset's existing ad-hoc subscribers only — which is exactly today's shipped
            // behavior, so play continues rather than bricking the branch mid-program.
            GD.PushError("[playthrough] no WorldStateStore at playthrough start - the boundary " +
                         "reset fanned over NOTHING (CORE-PROG-A2 wires the store; spec §5.4)");
        }
        if (viaPlayAgain)
            RunDriver.Instance?.ResetRun();
    }

    /// <summary>Spec §1.5: re-anchor FORWARD through CycleDriver's own Setup (the one
    /// additive parameter), then the ResetRun fan-out verbatim — an immediate reliable
    /// SendPhaseTo to every peer rather than waiting on the 0.5 s unreliable broadcast.
    /// Forward-only invariant: <see cref="PlaythroughMachine.ReanchorStartCycles"/>.</summary>
    private void ReanchorClock(int roundJustSurvived)
    {
        if (CycleDriver.Instance is not { } cycle)
            return;
        CycleBands.Band band = CycleBands.GetBand(cycle.Phase, cycle.CyclesElapsed, out _);
        int ordinalNow = RunPhaseTracker.Ordinal(band, cycle.CyclesElapsed);
        int startCycles = PlaythroughMachine.ReanchorStartCycles(roundJustSurvived, ordinalNow);
        cycle.Setup(isServer: true, _periodSec, startPhase: 0f, startCycles: startCycles);
        foreach (long peerId in Multiplayer.GetPeers())
            cycle.SendPhaseTo((int)peerId);
    }

    // --- broadcasts: one per transition, R+O+CL on RunChannel (spec §3.2) -------------------

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastRoundIntro(int round, int demand, int cumulativeDemand, double durationSec, uint seq)
    {
        PlaythroughState from = State;
        _machine?.Apply(PlaythroughState.RoundIntro, round, durationSec);
        State = PlaythroughState.RoundIntro;
        Round = round;
        StateRemainingSec = durationSec;
        if (round == 1)
        {
            // Every RoundIntro(1) is a new playthrough (T3 or T10) — the latches read fresh.
            LastRoundSummary = null;
            LastOutcome = null;
        }
        // The one wire message that carries a round's demand numbers (spec §2.2's
        // server-numbers-only rule): the ledger applies before any event fires, so a
        // subscriber to either event below reads post-round-begin quota state.
        QuotaLedger.Instance?.ApplyRoundBegin(round, demand, cumulativeDemand);
        StateChanged?.Invoke(from, State, round);
        RoundIntroStarted?.Invoke(round, demand);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastRoundLive(int round, uint seq)
    {
        PlaythroughState from = State;
        _machine?.Apply(PlaythroughState.InRound, round, -1);
        State = PlaythroughState.InRound;
        Round = round;
        StateRemainingSec = -1;
        StateChanged?.Invoke(from, State, round);
        RoundLive?.Invoke(round);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastRoundEnded(int round, int demand, int banked, int nextDemand, double durationSec, uint seq)
    {
        PlaythroughState from = State;
        _machine?.Apply(PlaythroughState.RoundEnd, round, durationSec);
        State = PlaythroughState.RoundEnd;
        Round = round;
        StateRemainingSec = durationSec;
        var summary = new RoundSummary(round, demand, banked, nextDemand);
        LastRoundSummary = summary;
        StateChanged?.Invoke(from, State, round);
        RoundEnded?.Invoke(summary);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastUpgradeLobby(int roundJustSurvived, double durationSec, uint seq)
    {
        PlaythroughState from = State;
        _machine?.Apply(PlaythroughState.UpgradeLobby, roundJustSurvived, durationSec);
        State = PlaythroughState.UpgradeLobby;
        Round = roundJustSurvived;
        StateRemainingSec = durationSec;
        StateChanged?.Invoke(from, State, roundJustSurvived);
        UpgradeLobbyStarted?.Invoke(roundJustSurvived, durationSec);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastRunConcluded(byte kind, int round, int demand, int banked, uint seq)
    {
        PlaythroughState from = State;
        _machine?.Apply(PlaythroughState.Loss, round, -1);
        State = PlaythroughState.Loss;
        Round = round;
        StateRemainingSec = -1;
        var outcome = new RunOutcome((RunOutcomeKind)kind, round, demand, banked);
        LastOutcome = outcome;
        StateChanged?.Invoke(from, State, round);
        RunConcluded?.Invoke(outcome);
    }

    // --- client → server requests (spec §3.3) ----------------------------------------------

    /// <summary>Client-side entry: B1's screens (and BotHarness's --flow-ready-at) call this;
    /// the server validates sender and state and silently drops stale echoes.</summary>
    public void SendReadyAdvance() => RpcId(1, MethodName.RequestReadyAdvance);

    /// <summary>Client-side entry for the Loss screen's Play Again (any peer may request —
    /// the RunDriverResetRequest precedent — but the driver, not the UI, sequences
    /// reset-then-intro).</summary>
    public void SendPlayAgain() => RpcId(1, MethodName.RequestPlayAgain);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel)]
    private void RequestReadyAdvance()
    {
        if (!_isServer || _machine == null)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0)
            return; // no trustworthy sender — the PropManager.RequestGrab posture.
        if (_machine.OnReadyAdvance(peer, ConnectedPeers(), out PlaythroughCommit? commit)
            && commit is { } c)
        {
            ExecuteCommit(c);
        }
        // Out-of-state requests return false above and are dropped silently — stale echoes
        // of a screen the server already left, not errors (spec §3.3).
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel)]
    private void RequestPlayAgain()
    {
        if (!_isServer || _machine == null)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0)
            return;
        if (_machine.OnPlayAgain(out PlaythroughCommit? commit) && commit is { } c)
            ExecuteCommit(c);
    }

    // --- late join / never-strand (spec §3.4) -----------------------------------------------

    /// <summary>Server-only: Gameplay.OnPeerConnected, immediately after
    /// RunDriver.SendRunStateTo. Carries state, round, the live countdown, and both latched
    /// payloads — a joiner into RoundEnd/Loss shows the tally/loss screen from the poll; no
    /// surface ever depends on having witnessed a transition.</summary>
    public void SendFlowStateTo(int peerId)
    {
        if (!_isServer)
            return;
        bool hasSummary = LastRoundSummary.HasValue;
        RoundSummary s = LastRoundSummary ?? default;
        bool hasOutcome = LastOutcome.HasValue;
        RunOutcome o = LastOutcome ?? default;
        RpcId(peerId, MethodName.SyncFlowStateTo, (byte)State, Round, StateRemainingSec,
            hasSummary, s.Round, s.Demand, s.Banked, s.NextDemand,
            hasOutcome, (byte)o.Kind, o.Round, o.Demand, o.Banked, ++_seq);
    }

    /// <summary>Fires no events on purpose (the RunDriver.SyncRunStateTo posture): surfaces
    /// initialize by POLLING after Synced, then react to events — so a duplicate application
    /// around a connect (broadcast + targeted sync) can never double-fire anything, and it is
    /// idempotent by construction (same fields, same values).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncFlowStateTo(byte state, int round, double remainingSec,
        bool hasSummary, int sRound, int sDemand, int sBanked, int sNextDemand,
        bool hasOutcome, byte oKind, int oRound, int oDemand, int oBanked, uint seq)
    {
        State = (PlaythroughState)state;
        Round = round;
        StateRemainingSec = remainingSec;
        LastRoundSummary = hasSummary ? new RoundSummary(sRound, sDemand, sBanked, sNextDemand) : null;
        LastOutcome = hasOutcome ? new RunOutcome((RunOutcomeKind)oKind, oRound, oDemand, oBanked) : null;
        Synced = true;
    }
}
