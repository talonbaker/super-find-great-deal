using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.World;

/// <summary>
/// The four typed phase-crossing events (design §1's day→dusk→night→dawn→day loop), a
/// replicated run-length counter, and the server-authoritative run-end/reset the whole
/// lean-MVP loop hangs off (L1, Issue #104). A LAYER ABOVE <see cref="CycleDriver"/>, never a
/// change to it — this file adds nothing to CycleDriver.cs and reuses its existing public
/// surface exactly (<see cref="CycleDriver.Phase"/>, <see cref="CycleDriver.CyclesElapsed"/>,
/// <see cref="CycleDriver.Synced"/>, and — for reset — <see cref="CycleDriver.Setup"/> and
/// <see cref="CycleDriver.SendPhaseTo"/>, the very same entry points Gameplay's own session
/// start and late-join delivery already call). Wired into Gameplay exactly like CycleDriver —
/// present in every world, server and client alike — so its own late-join delivery rides the
/// identical peer-connect funnel (see <see cref="SendRunStateTo"/>, called from
/// Gameplay.OnPeerConnected the same place CycleDriver.SendPhaseTo and
/// PropManager.SendDumpTo already are).
///
/// <b>CyclesElapsed IS the counter (Issue #104's decision point).</b> CycleDriver.CyclesElapsed
/// was already replicated (2 Hz broadcast + reliable late-join/reconnect sync) and read by
/// nothing (audit §4.1). Rather than add a second replicated int that means the same thing —
/// exactly the two-copies-of-one-fact drift <see cref="CycleBands"/>'s own doc warns about —
/// this driver reads it directly every tick and a reset rewinds it by re-invoking
/// CycleDriver.Setup, so there is exactly one number anywhere that answers "which cycle is
/// this," never two that could disagree.
///
/// <b>Exactly-once, not latest-wins.</b> CycleDriver's own phase broadcast is deliberately
/// unreliable (a dropped 0.5s sample costs nothing — the next one supersedes it). A phase-
/// CROSSING is not like that: missing one is missing a fact ("the sunset payout should have
/// fired") that no later sample recovers. Every RunDriver event below is therefore a RELIABLE,
/// ordered RPC on its own channel (<see cref="NetCodec.RunChannel"/>) with <c>CallLocal = true</c>
/// — the exact idiom <see cref="MpFoundation.Game.Props.PropManager"/>'s ApplyPropState already uses for "authoritative
/// discrete state transition, applied identically on the server and every peer" — rather than
/// CycleDriver's snapshot style.
/// </summary>
public partial class RunDriver : Node
{
    public const string NodeName = "RunDriver";

    /// <summary>Single instance per running game, same convention as <see cref="CycleDriver.Instance"/>.</summary>
    public static RunDriver? Instance { get; private set; }

    /// <summary>Design §1: "Session = 2 day/night cycles." --run-cycles &lt;= 0 (unset or
    /// malformed) falls back here — MECHANICS-BIBLE §6, define the extremes explicitly.</summary>
    public const int DefaultRunCycles = 2;

    /// <summary>Design §1: "cycle period default 720s (≈7 min day + 5 min night)." Requested
    /// via <see cref="LaunchOptions.RequestDefaultCyclePeriod"/> — the exact "0 = unset, first
    /// requester wins" sentinel a world's own default (e.g. hoodlab's 300s) or an explicit
    /// --cycle-period test hook already uses to override CycleDriver's shared 120s default; see
    /// the call site in Gameplay._Ready for why this always loses to either of those.</summary>
    public const double DefaultCyclePeriodSec = 720.0;

    private bool _isServer;
    private int _runCycles;
    private double _periodSec;
    private double _resetAtSec = -1;
    private double _elapsedSinceSetupSec;
    private bool _resetTestHookFired;
    private uint _seq;

    // RunPhaseTracker's ordinal for the most recently applied band (see that class's doc for
    // why an integer ordinal, not the raw CycleBands.Band enum, is what gets compared tick to
    // tick). Seeded (not "crossed into") on the first synced sample after Setup/reset — see
    // DetectAndEmitCrossings's _trackerInitialized branch.
    private int _lastOrdinal;
    private bool _trackerInitialized;

    private readonly List<PhaseEventSample> _history = new();

    /// <summary>The run's configured length in cycles. Authoritative on the server from Setup
    /// onward; on a client this is meaningless until <see cref="Synced"/> — same contract as
    /// <see cref="CycleDriver.Synced"/> guards Phase/CyclesElapsed.</summary>
    public int RunCycles => _runCycles;

    /// <summary>True once the final configured cycle has closed (its dawn→day crossing) and
    /// stays true until <see cref="ResetRun"/> clears it. Edge-triggered, fires
    /// <see cref="RunEndedSignal"/> exactly once per run.</summary>
    public bool RunEnded { get; private set; }

    /// <summary>False until this peer has its first authoritative run state — server: true
    /// immediately from <see cref="Setup"/> (it is its own authority); client: true once
    /// <see cref="SyncRunStateTo"/> lands. Same contract, same reasoning as
    /// <see cref="CycleDriver.Synced"/>.</summary>
    public bool Synced { get; private set; }

    /// <summary>Every phase-crossing event this peer has applied so far, in order, since its own
    /// Setup/last reset — NOT replayed to a late joiner (a late joiner only needs the CURRENT
    /// phase/counter, which CycleDriver's own late-join sync already guarantees; replaying
    /// already-resolved one-shot events like a paid-out sunset to a joiner who missed it is a
    /// consumer decision, not this driver's). Read-only view over the backing list so a test
    /// harness (BotHarness) can poll it directly, the same style CycleDriver.Phase is polled.</summary>
    public IReadOnlyList<PhaseEventSample> History => _history;

    /// <summary>Fires on every peer (server included) exactly once per crossing, in order —
    /// (kind, cyclesElapsed AFTER the crossing). The contract L7 (wallets/payout) and L10
    /// (night/dawn) code against.</summary>
    public event Action<PhaseEventKind, int>? PhaseCrossed;

    /// <summary>Fires on every peer exactly once when the run's final cycle closes. The
    /// contract L11 (summary screen) codes against.</summary>
    public event Action? RunEndedSignal;

    /// <summary>Fires on every peer exactly once per <see cref="ResetRun"/> — after the counter
    /// and RunEnded have already been rewound locally, so a subscriber reading either inside its
    /// handler sees the POST-reset value. The contract L7 (zero wallets) and L11 (dismiss
    /// summary) code against.</summary>
    public event Action? RunReset;

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>runCycles &lt;= 0 falls back to <see cref="DefaultRunCycles"/>; periodSec &lt;= 0
    /// falls back to <see cref="DefaultCyclePeriodSec"/> (both defensive last resorts — every
    /// real launch path has already resolved a positive value by the time this runs; see
    /// LaunchOptions.RequestDefaultCyclePeriod). resetAtSec &lt; 0 disables the test-only
    /// self-reset hook (--run-reset-at); every real launch path leaves it disabled.</summary>
    public void Setup(bool isServer, int runCycles, double periodSec, double resetAtSec = -1)
    {
        _isServer = isServer;
        _runCycles = runCycles > 0 ? runCycles : DefaultRunCycles;
        _periodSec = periodSec > 0 ? periodSec : DefaultCyclePeriodSec;
        _resetAtSec = resetAtSec;
        _elapsedSinceSetupSec = 0;
        _resetTestHookFired = false;
        _lastOrdinal = 0;
        _trackerInitialized = false;
        _history.Clear();
        RunEnded = false;
        // The server is always its own authority (CycleDriver.Setup's exact wording) — nothing
        // to wait for. A client stays unsynced until SyncRunStateTo lands (see Synced's doc).
        Synced = isServer;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer)
            return; // only the server ever detects/decides crossings; see the class doc.

        _elapsedSinceSetupSec += delta;
        if (_resetAtSec >= 0 && !_resetTestHookFired && _elapsedSinceSetupSec >= _resetAtSec)
        {
            _resetTestHookFired = true; // one-shot, same idiom as --force-reconnect-at.
            ResetRun();
        }

        // A one-physics-frame lag between CycleDriver's true phase and this driver noticing it
        // (if node processing order ever put this before CycleDriver's own _PhysicsProcess in a
        // given frame) is the entire cost of NOT depending on tree child-order — at most ~1/60s
        // against a >=12s test period or the real 720s one, the same "imperceptible against a
        // much longer period" reasoning CycleDriver's own 0.5s broadcast interval already relies
        // on. Correctness (exactly-once, in order) never depends on this ordering either way.
        if (CycleDriver.Instance is not { Synced: true } cycle)
            return; // hold until the server's own CycleDriver has a real phase to read.
        DetectAndEmitCrossings(cycle.Phase, cycle.CyclesElapsed);
    }

    private void DetectAndEmitCrossings(float phase, int cyclesElapsed)
    {
        CycleBands.Band band = CycleBands.GetBand(phase, cyclesElapsed, out _);
        int ordinal = RunPhaseTracker.Ordinal(band, cyclesElapsed);

        if (!_trackerInitialized)
        {
            // The run genuinely STARTS inside the Day band (design §1) — that is not a
            // crossing, so the very first sample only seeds the tracker. Firing a synthetic
            // "entered day" event here would violate exactly-once (day 1 was never entered
            // FROM anything).
            _lastOrdinal = ordinal;
            _trackerInitialized = true;
            return;
        }
        if (ordinal <= _lastOrdinal)
            return; // no forward progress this tick (still inside the same band).

        foreach ((PhaseEventKind kind, int cyclesAfter) in RunPhaseTracker.CrossingsBetween(_lastOrdinal, ordinal))
        {
            Rpc(MethodName.BroadcastPhaseCrossed, (byte)kind, cyclesAfter, ++_seq);
            if (!RunEnded && RunPhaseTracker.IsRunEndCrossing(kind, cyclesAfter, _runCycles))
                Rpc(MethodName.BroadcastRunEnded, ++_seq);
        }
        _lastOrdinal = ordinal;
    }

    /// <summary>Server -> everyone (CallLocal): one phase-crossing event, applied identically
    /// wherever it runs — the exact idiom <see cref="MpFoundation.Game.Props.PropManager"/>'s ApplyPropState uses. Reliable +
    /// ordered on <see cref="NetCodec.RunChannel"/>, so no staleness/seq guard is needed the way
    /// CycleDriver.Apply needs one for its unreliable snapshot (see the class doc).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastPhaseCrossed(byte kind, int cyclesElapsedAfter, uint seq)
    {
        var sample = new PhaseEventSample((PhaseEventKind)kind, cyclesElapsedAfter);
        _history.Add(sample);
        PhaseCrossed?.Invoke(sample.Kind, sample.CyclesElapsedAfter);
    }

    /// <summary>Server -> everyone (CallLocal): the run's final cycle has closed. Idempotent —
    /// re-applying is a harmless no-op (see the RunEnded guard), so a duplicate delivery (which
    /// reliable+ordered should never actually produce) could never double-fire the subscriber
    /// contract either way — MECHANICS-BIBLE §4.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastRunEnded(uint seq)
    {
        if (RunEnded)
            return;
        RunEnded = true;
        RunEndedSignal?.Invoke();
    }

    /// <summary>Server-authoritative run-state reset (L11's "Play Again"): returns the SAME
    /// session to cycle 1 without rehosting. Safe to call more than once in a row (idempotent —
    /// each call just re-anchors to the same post-reset state). No-op on a client (authority
    /// only).</summary>
    public void ResetRun()
    {
        if (!_isServer)
            return;

        // Re-anchor the shared clock through CycleDriver's OWN public Setup() — the exact same
        // entry point Gameplay._Ready already calls at session start, never a new hook into
        // CycleDriver's internals. periodSec is this driver's own captured value (never
        // re-derived), so a reset changes WHERE in the run we are, never its pacing.
        CycleDriver.Instance?.Setup(isServer: true, _periodSec, startPhase: 0f);

        // Re-deliver reliably to every currently-connected peer immediately, rather than
        // waiting up to CycleDriver's 0.5s unreliable broadcast interval to (maybe) carry a
        // reset that large out. Same call, same urgency as a late joiner's very first sync —
        // CycleDriver.Apply's isSync path always wins outright, never blended against an
        // in-flight periodic sample.
        if (CycleDriver.Instance is { } cycle)
            foreach (long peerId in Multiplayer.GetPeers())
                cycle.SendPhaseTo((int)peerId);

        Rpc(MethodName.BroadcastRunReset, _runCycles, ++_seq);
    }

    /// <summary>Server -> everyone (CallLocal): applies the reset locally wherever it runs —
    /// same idiom as <see cref="BroadcastPhaseCrossed"/>.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void BroadcastRunReset(int runCycles, uint seq)
    {
        _runCycles = runCycles; // defensive re-affirmation; a reset never changes the configured
                                 // run length in practice, but this keeps every peer pinned to
                                 // the server's value regardless.
        _lastOrdinal = 0; // Day, cyclesElapsed 0 — the run's genuine restarted ordinal.
        _trackerInitialized = true; // NOT re-seeded via the first-sample branch: we already know
                                     // the true post-reset state, so there is nothing to wait for.
        _history.Clear();
        RunEnded = false;
        RunReset?.Invoke();
    }

    /// <summary>Server-only: called by Gameplay.OnPeerConnected for every joining/resuming peer,
    /// the exact call site CycleDriver.SendPhaseTo and PropManager.SendDumpTo already use.
    /// Carries only (RunCycles, RunEnded) — a late joiner needs no crossing-event replay (see
    /// the class doc's History note); CycleDriver's own SendPhaseTo (called alongside this, same
    /// site) already guarantees the correct current phase/counter.</summary>
    public void SendRunStateTo(int peerId)
    {
        if (!_isServer)
            return;
        RpcId(peerId, MethodName.SyncRunStateTo, _runCycles, RunEnded, ++_seq);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncRunStateTo(int runCycles, bool runEnded, uint seq)
    {
        _runCycles = runCycles;
        RunEnded = runEnded;
        Synced = true;
    }
}

/// <summary>The four typed phase-crossing events (design §1), in the fixed cyclic order a real
/// run visits them: Day -> DuskSweep -> Night -> DawnSweep -> Day(next cycle) -> ... Named for
/// the transition itself (what just ended -> what just began), matching the dispatch's own
/// wording exactly. Wire-encoded as a byte (see RunDriver's Rpc signatures).</summary>
public enum PhaseEventKind : byte
{
    DayToDusk = 0,
    DuskToNight = 1,
    NightToDawn = 2,
    DawnToDay = 3,
}

/// <summary>One applied phase-crossing event: which one, and CyclesElapsed AFTER it (i.e. which
/// cycle the run is in as of this crossing — see RunPhaseTracker.CrossingsBetween's doc for why
/// "after" is the value carried, not "before").</summary>
public readonly record struct PhaseEventSample(PhaseEventKind Kind, int CyclesElapsedAfter);

/// <summary>
/// Pure ordinal arithmetic over <see cref="CycleBands.Band"/>, pulled out of <see cref="RunDriver"/>
/// so it is directly testable without a running scene tree or Multiplayer API — the same seam
/// <see cref="CyclePhase"/> gives <see cref="CycleDriver"/> and <see cref="CycleBands"/> gives
/// <see cref="OutdoorAtmosphere"/>/<c>NightDome</c> (see RunDriverSelfTest.cs, run via
/// --run-driver-selftest / Run-RunDriverTest.ps1).
///
/// <b>Why an ordinal, not a direct enum compare.</b> Band alone can't tell "no progress" from "a
/// full cycle actually just happened" (Day at cyclesElapsed=0 and Day at cyclesElapsed=1 are the
/// same enum value), and a same-day comparison can't detect a defensive multi-band skip within
/// one server physics tick (a hitch, not the steady state, but MECHANICS-BIBLE's boundary-
/// condition discipline says define it anyway rather than let it silently drop events — the
/// exact failure mode CycleDriver's own snap-correct rule exists to avoid for the raw phase
/// float). Encoding (cyclesElapsed, band) as a single monotonically increasing integer —
/// <c>ordinal = cyclesElapsed * 4 + bandIndex</c>, band cycling Day(0) -> DuskSweep(1) ->
/// Night(2) -> DawnSweep(3) -> Day of the next cycle(4) -> ... — turns "how many boundaries were
/// crossed going from A to B, and which ones, in order" into integer subtraction and a range,
/// with no special-casing for a wrap or a multi-band jump.
/// </summary>
public static class RunPhaseTracker
{
    private const int BandsPerCycle = 4;

    /// <summary>The monotonically increasing position of (band, cyclesElapsed) in run time. See
    /// the class doc for the encoding.</summary>
    public static int Ordinal(CycleBands.Band band, int cyclesElapsed) => cyclesElapsed * BandsPerCycle + BandIndex(band);

    private static int BandIndex(CycleBands.Band band) => band switch
    {
        CycleBands.Band.Day => 0,
        CycleBands.Band.DuskSweep => 1,
        CycleBands.Band.Night => 2,
        CycleBands.Band.DawnSweep => 3,
        _ => 0,
    };

    /// <summary>The event fired by ENTERING the band at the given ordinal — i.e. what just
    /// started. Ordinal's band-index component (mod 4) alone determines it; the cycle count
    /// component only ever affects <see cref="CrossingsBetween"/>'s CyclesElapsedAfter, never
    /// which kind fires.</summary>
    public static PhaseEventKind EventFor(int ordinal)
    {
        int bandIndex = ((ordinal % BandsPerCycle) + BandsPerCycle) % BandsPerCycle;
        return bandIndex switch
        {
            0 => PhaseEventKind.DawnToDay,   // entering Day = dawn's sweep just ended.
            1 => PhaseEventKind.DayToDusk,   // entering DuskSweep = day just ended.
            2 => PhaseEventKind.DuskToNight, // entering Night = the dusk sweep just closed.
            _ => PhaseEventKind.NightToDawn, // entering DawnSweep = night just ended.
        };
    }

    /// <summary>Every ordinal boundary strictly between <paramref name="fromOrdinal"/> (exclusive)
    /// and <paramref name="toOrdinal"/> (inclusive), in order — the crossings that happened
    /// between two samples. Empty if there is no forward progress (toOrdinal &lt;= fromOrdinal).
    /// CyclesElapsedAfter for a returned crossing is <c>ordinal / 4</c> — the cycle count that is
    /// TRUE the instant that boundary is crossed (e.g. the DawnToDay crossing that closes cycle 0
    /// reports CyclesElapsedAfter=1: cycle 0 just finished, cycle 1 has just begun — the same
    /// value CycleDriver.CyclesElapsed itself reads at that same instant).</summary>
    public static IEnumerable<(PhaseEventKind Kind, int CyclesElapsedAfter)> CrossingsBetween(int fromOrdinal, int toOrdinal)
    {
        for (int o = fromOrdinal + 1; o <= toOrdinal; o++)
            yield return (EventFor(o), o / BandsPerCycle);
    }

    /// <summary>Design §1 / L1 scope item 3: "emit a run-end signal when the final cycle
    /// closes." A crossing closes the run iff it is the dawn->day transition (a cycle only
    /// ever completes at dawn's end) AND the cycle count it reports has reached the configured
    /// run length — e.g. runCycles=2 ends at the DawnToDay crossing reporting
    /// CyclesElapsedAfter=2 (cycle 1, the second cycle, just closed), not at CyclesElapsedAfter=1
    /// (cycle 0 closing — cycle 1 is still owed).</summary>
    public static bool IsRunEndCrossing(PhaseEventKind kind, int cyclesElapsedAfter, int runCycles) =>
        kind == PhaseEventKind.DawnToDay && cyclesElapsedAfter >= runCycles;
}
