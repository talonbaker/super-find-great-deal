using System;
using System.Collections.Generic;
using MpFoundation.Game.World;

namespace Sail.Game.Run;

/// <summary>What a machine decision asks the driver to do. One commit = one wire message
/// (spec §3.2); the driver broadcasts it and the CallLocal handler applies it identically
/// everywhere — the RunDriver detect→Rpc→apply-in-handler shape, one layer up.</summary>
public enum PlaythroughCommitKind : byte
{
    RoundIntro = 0,
    RoundLive = 1,
    RoundEnd = 2,
    UpgradeLobby = 3,
    Loss = 4,
}

/// <summary>One decided transition. <paramref name="IsNewPlaythrough"/> marks BOTH commits
/// into RoundIntro(1) — Boot exit and Play Again alike — because the playthrough boundary is
/// an unconditional entry guard (spec §5.4), not a Play-Again special case.
/// <paramref name="ViaPlayAgain"/> additionally drives T10's RunDriver.ResetRun();
/// <paramref name="ReanchorClock"/> drives T9's forward re-anchor (spec §1.5).</summary>
public readonly record struct PlaythroughCommit(
    PlaythroughCommitKind Kind,
    int Round,
    bool IsNewPlaythrough,
    bool ViaPlayAgain,
    bool ReanchorClock,
    RunOutcome? Outcome);

/// <summary>
/// The playthrough state machine's pure core (spec §1.2–§1.7, CORE-PROG-A1) — every guard,
/// timer, the verdict chain and the re-anchor arithmetic, with no scene tree, no Multiplayer
/// API and no Godot types, so the whole transition table is walkable by <c>dotnet test</c>
/// (the RunPhaseTracker/CyclePhase seam, one layer up). <see cref="PlaythroughDriver"/> is
/// the wire: it exists only on the server (clients apply broadcasts without guards), asks
/// this class what to do, broadcasts the answer, and calls <see cref="Apply"/> from the
/// CallLocal handler — so the authority's machine state advances synchronously inside its own
/// broadcast (probe 1, Run-NetProbeTest.ps1) and any guard evaluated after a commit reads
/// post-commit state.
///
/// Transition ownership (spec §1.7): T3 <see cref="TickBoot"/>; T4/T8/T9
/// <see cref="TickTimers"/> (plus the all-ready halves of T8/T9 via
/// <see cref="OnReadyAdvance"/>); T5 is not a machine transition (bands are time, states are
/// game); T6/T7 <see cref="OnPhaseCrossed"/> — the verdict instant, the only point predicates
/// are ever evaluated (spec §1.4); T10 <see cref="OnPlayAgain"/>. T1/T2/T11 are AppFlow
/// (local scene switches) and have no machine row at all.
/// </summary>
public sealed class PlaythroughMachine
{
    /// <summary>Spec §1.7: every timer clamps to >= 1 s; a non-positive configured value
    /// clamps (and the driver logs <see cref="TimersClamped"/>), never zero-fires.</summary>
    public const double MinTimerSec = 1.0;

    // Spec §8 placeholder values — a balance/feel pass may retune freely.
    public const double DefaultIntroSec = 4.0;
    public const double DefaultTallySec = 15.0;
    public const double DefaultLobbySec = 30.0;

    public double IntroSec { get; }
    public double TallySec { get; }
    public double LobbySec { get; }

    /// <summary>True if any configured timer was invalid (non-finite or &lt; 1 s) and got
    /// clamped — surfaced so the driver can log it (this class cannot).</summary>
    public bool TimersClamped { get; }

    /// <summary>Whether this world plays a scored playthrough at all — <see cref="WorldRunFlow"/>
    /// resolves it per world id and the driver passes the answer in. FALSE means the verdict
    /// instant (T6/T7) is never reached: <see cref="OnPhaseCrossed"/> returns null before any
    /// predicate is consulted, so the machine settles in InRound after the boot intro and stays
    /// there for the whole session — no round end, no upgrade lobby, no loss.
    ///
    /// <para>It gates the VERDICT rather than the boot commit deliberately. Boot still commits
    /// RoundIntro(1), so the playthrough-boundary entry guard (spec §5.4's store reset) and the
    /// ledger's ApplyRoundBegin still run exactly once at session start — a world with no quota
    /// still deserves a clean world state. What it must never do is be told it lost.</para>
    ///
    /// <para>Band-liveness is the other half of why this is the right cut: Loss, RoundEnd and
    /// UpgradeLobby are all NOT band-live (<see cref="PlaythroughStates.IsBandLive"/>), so a
    /// world parked in one of them silently loses payouts, cutoffs and the store's dawn fan.
    /// Staying InRound keeps every one of those live for a level whose entire subject is being
    /// in it.</para></summary>
    public bool RunsPlaythrough { get; }

    public PlaythroughState State { get; private set; } = PlaythroughState.Boot;

    /// <summary>1-based; valid from RoundIntro(1) onward.</summary>
    public int Round { get; private set; }

    /// <summary>Server-authoritative countdown for RoundIntro/RoundEnd/UpgradeLobby; -1
    /// where no timer runs (Boot, InRound, Loss — Loss has no auto-timeout by design).</summary>
    public double StateRemainingSec { get; private set; } = -1;

    private readonly List<ILossPredicate> _predicates = new();
    private readonly HashSet<int> _ready = new();

    /// <param name="runsPlaythrough">Defaults TRUE so every existing caller — the live driver on
    /// every other world, the demo, the self-tests and the xUnit walk — is unchanged; only a
    /// world that explicitly declares it has no playthrough passes false.</param>
    public PlaythroughMachine(double introSec = DefaultIntroSec,
        double tallySec = DefaultTallySec, double lobbySec = DefaultLobbySec,
        bool runsPlaythrough = true)
    {
        IntroSec = ClampTimer(introSec, out bool c1);
        TallySec = ClampTimer(tallySec, out bool c2);
        LobbySec = ClampTimer(lobbySec, out bool c3);
        TimersClamped = c1 || c2 || c3;
        RunsPlaythrough = runsPlaythrough;
    }

    private static double ClampTimer(double sec, out bool clamped)
    {
        clamped = !double.IsFinite(sec) || sec < MinTimerSec;
        return clamped ? MinTimerSec : sec;
    }

    /// <summary>Registration order is evaluation order is priority order (MECHANICS-BIBLE §7).
    /// Exactly one predicate ships (the quota); the list exists for extension, and no second
    /// predicate is invented in this packet.</summary>
    public void RegisterLossPredicate(ILossPredicate predicate) => _predicates.Add(predicate);

    public IReadOnlyList<ILossPredicate> Predicates => _predicates;

    /// <summary>T3 — Boot exit, server only. World seeding completes synchronously inside
    /// Gameplay._Ready (StartAsServer runs before the first physics tick), so the two clock
    /// syncs are the observable gates. Guard rejection: null while either is false.</summary>
    public PlaythroughCommit? TickBoot(bool cycleSynced, bool runSynced) =>
        State == PlaythroughState.Boot && cycleSynced && runSynced
            ? NewPlaythroughCommit(viaPlayAgain: false)
            : null;

    /// <summary>T4/T8/T9's timer halves, plus T8/T9's disconnect-shrink rule: the all-ready
    /// check re-runs here every tick against the CURRENT connected set, so a disconnect
    /// mid-tally can complete an all-ready skip without any new request (spec §1.7 T8).</summary>
    public PlaythroughCommit? TickTimers(double dt, IReadOnlyCollection<int> connectedPeers)
    {
        if (dt <= 0)
            return null;
        switch (State)
        {
            case PlaythroughState.RoundIntro:
                StateRemainingSec -= dt;
                return StateRemainingSec <= 0
                    ? new PlaythroughCommit(PlaythroughCommitKind.RoundLive, Round, false, false, false, null)
                    : null;
            case PlaythroughState.RoundEnd:
                StateRemainingSec -= dt;
                return StateRemainingSec <= 0 || AllReady(connectedPeers) ? AdvanceFromRoundEnd() : null;
            case PlaythroughState.UpgradeLobby:
                StateRemainingSec -= dt;
                return StateRemainingSec <= 0 || AllReady(connectedPeers) ? AdvanceFromLobby() : null;
            default:
                return null; // Boot advances via TickBoot; InRound via the verdict; Loss never times out.
        }
    }

    /// <summary>T6/T7 — THE verdict instant (spec §1.4). Predicates are evaluated here and
    /// nowhere else: no polling path can produce a verdict mid-round, and only the
    /// NightToDawn crossing while InRound triggers one. First registered predicate returning
    /// non-null wins; a hitch batch delivering NightToDawn mid-sequence verdicts at exactly
    /// that point (the state advances synchronously, so a second NightToDawn in the same
    /// batch finds State != InRound and is guard-rejected — one verdict per round, always).</summary>
    public PlaythroughCommit? OnPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        if (kind != PhaseEventKind.NightToDawn || State != PlaythroughState.InRound)
            return null;
        // LOSS-1 (Talon note 4, 2026-08-29): a world with no quota has no verdict to pass. The
        // gate is HERE, ahead of the predicate chain, so the answer is the same whatever anyone
        // registers later — and so the machine stays in band-live InRound instead of parking in
        // RoundEnd/Loss with the screens quietly not built. See RunsPlaythrough.
        if (!RunsPlaythrough)
            return null;
        foreach (ILossPredicate predicate in _predicates)
        {
            if (predicate.Evaluate(Round) is { } outcome)
                return new PlaythroughCommit(PlaythroughCommitKind.Loss, Round, false, false, false, outcome);
        }
        return new PlaythroughCommit(PlaythroughCommitKind.RoundEnd, Round, false, false, false, null);
    }

    /// <summary>T8/T9's ready halves. Valid in RoundEnd and UpgradeLobby; anything else is a
    /// stale echo of a screen the server already left — silently dropped (returns false),
    /// per spec §3.3. Never triggers vacuously: an empty room falls back to the timer
    /// (value call — see <see cref="AllReady"/>).</summary>
    public bool OnReadyAdvance(int peerId, IReadOnlyCollection<int> connectedPeers, out PlaythroughCommit? commit)
    {
        commit = null;
        if (State is not (PlaythroughState.RoundEnd or PlaythroughState.UpgradeLobby))
            return false;
        _ready.Add(peerId);
        if (AllReady(connectedPeers))
            commit = State == PlaythroughState.RoundEnd ? AdvanceFromRoundEnd() : AdvanceFromLobby();
        return true;
    }

    /// <summary>T10. Valid only in Loss; a duplicate request arriving after the commit finds
    /// State == RoundIntro and is dropped by this guard (spec §6 case 5/7).</summary>
    public bool OnPlayAgain(out PlaythroughCommit? commit)
    {
        commit = null;
        if (State != PlaythroughState.Loss)
            return false;
        commit = NewPlaythroughCommit(viaPlayAgain: true);
        return true;
    }

    /// <summary>The local application — called from the driver's CallLocal broadcast handler
    /// (server and, mirrored field-wise, every client). Sets the queryable state FIRST; the
    /// driver invokes events after (the RunReset ordering contract, generalized — spec §3.2).
    /// Idempotent: re-applying the same values is a no-op by construction. The ready set
    /// clears on EVERY transition — each window's ready set is its own, and a reconnector
    /// (new peer id) is not-ready again automatically.</summary>
    public void Apply(PlaythroughState to, int round, double remainingSec)
    {
        State = to;
        Round = round;
        StateRemainingSec = remainingSec;
        _ready.Clear();
    }

    /// <summary>The duration the driver stamps on a commit's broadcast (and re-arms locally
    /// via <see cref="Apply"/>): -1 where no timer runs.</summary>
    public double DurationFor(PlaythroughCommitKind kind) => kind switch
    {
        PlaythroughCommitKind.RoundIntro => IntroSec,
        PlaythroughCommitKind.RoundEnd => TallySec,
        PlaythroughCommitKind.UpgradeLobby => LobbySec,
        _ => -1,
    };

    /// <summary>Spec §1.5's forward-only re-anchor, as arithmetic: entering round N+1 after
    /// surviving round N re-anchors the clock to (startCycles = N, phase 0) — ordinal 4N —
    /// UNLESS the free-running clock already passed that ordinal (a lobby outlasting a short
    /// test period's whole dawn+day), in which case the target is the next Day-band start at
    /// or after the clock. THE INVARIANT (asserted by test, relied on by RunPhaseTracker):
    /// the returned target ordinal (result * 4) is always >= <paramref name="clockOrdinalNow"/>,
    /// so this path can never rewind the tracker — backward jumps stay the exclusive property
    /// of ResetRun, which resets the tracker explicitly. Under real timing (tally+lobby ~45 s
    /// vs a 720 s period's ~100+ s dawn sweep + day) the overrun branch never runs and the
    /// nominal Round N+1 &lt;-&gt; CyclesElapsed N mapping holds exactly.</summary>
    public static int ReanchorStartCycles(int roundJustSurvived, int clockOrdinalNow)
    {
        int target = Math.Max(roundJustSurvived, 0);
        if (clockOrdinalNow > target * 4)
            target = (clockOrdinalNow + 3) / 4; // next Day-band start at/after the clock — forward, never back
        return target;
    }

    private static PlaythroughCommit NewPlaythroughCommit(bool viaPlayAgain) =>
        new(PlaythroughCommitKind.RoundIntro, 1, true, viaPlayAgain, false, null);

    private PlaythroughCommit AdvanceFromRoundEnd() =>
        new(PlaythroughCommitKind.UpgradeLobby, Round, false, false, false, null);

    private PlaythroughCommit AdvanceFromLobby() =>
        new(PlaythroughCommitKind.RoundIntro, Round + 1, false, false, true, null);

    /// <summary>All CONNECTED peers ready, and at least one connected — the vacuous-truth arm
    /// (zero peers) deliberately does NOT skip (value call, CORE-PROG-A1): an empty room rides
    /// the timer, which spec §6 case 10 already accepts, rather than instant-flickering
    /// through tally/lobby screens nobody sees; membership is checked against the CURRENT
    /// connected set so departed peers can never strand the skip.</summary>
    private bool AllReady(IReadOnlyCollection<int> connectedPeers)
    {
        if (connectedPeers.Count == 0)
            return false;
        foreach (int peer in connectedPeers)
        {
            if (!_ready.Contains(peer))
                return false;
        }
        return true;
    }
}
