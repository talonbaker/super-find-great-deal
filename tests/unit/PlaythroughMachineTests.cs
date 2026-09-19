using System;
using System.Collections.Generic;
using MpFoundation.Game.World;
using Sail.Game.Run;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-A1 acceptance criterion 2: the headless transition-table walk over the spec's
/// §1.7 rows, INCLUDING guard rejections, against the pure machine every server decision
/// routes through. T1/T2/T11 are AppFlow (local scene switches — no machine row, per spec
/// §1.1) and are deliberately absent here; T5 (band crossings) is asserted as NOT a machine
/// transition. Also: verdict priority + single-evaluation-point (criterion 3), the
/// re-anchor forward-only invariant for both named ordinal cases plus the overrun case
/// (criterion 4), hitch catch-up, and timer extremes.
///
/// ApplyCommit below mirrors PlaythroughDriver's broadcast-handler mapping (commit kind →
/// applied state/duration): in production the application happens inside the CallLocal
/// handler, synchronously on the authority — measured by Run-NetProbeTest.ps1 before any of
/// this code existed.
/// </summary>
public class PlaythroughMachineTests
{
    private static readonly int[] TwoPeers = { 10, 20 };

    private sealed class FakePredicate : ILossPredicate
    {
        private readonly Func<int, RunOutcome?> _evaluate;
        public int Invocations { get; private set; }
        public FakePredicate(string id, Func<int, RunOutcome?> evaluate) { Id = id; _evaluate = evaluate; }
        public string Id { get; }
        public RunOutcome? Evaluate(int round) { Invocations++; return _evaluate(round); }
    }

    private static PlaythroughState StateFor(PlaythroughCommitKind kind) => kind switch
    {
        PlaythroughCommitKind.RoundIntro => PlaythroughState.RoundIntro,
        PlaythroughCommitKind.RoundLive => PlaythroughState.InRound,
        PlaythroughCommitKind.RoundEnd => PlaythroughState.RoundEnd,
        PlaythroughCommitKind.UpgradeLobby => PlaythroughState.UpgradeLobby,
        PlaythroughCommitKind.Loss => PlaythroughState.Loss,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void ApplyCommit(PlaythroughMachine machine, PlaythroughCommit commit) =>
        machine.Apply(StateFor(commit.Kind), commit.Round, machine.DurationFor(commit.Kind));

    /// <summary>Boot → InRound(1) via the real commits, 1 s timers.</summary>
    private static PlaythroughMachine AtInRound(params ILossPredicate[] predicates)
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        foreach (ILossPredicate p in predicates)
            machine.RegisterLossPredicate(p);
        ApplyCommit(machine, machine.TickBoot(true, true)!.Value);          // T3
        ApplyCommit(machine, machine.TickTimers(1.1, TwoPeers)!.Value);     // T4
        Assert.Equal(PlaythroughState.InRound, machine.State);
        return machine;
    }

    // --- T3: Boot → RoundIntro(1) -----------------------------------------------------------

    [Fact]
    public void T3_BootExit_BothClocksSynced_CommitsRoundIntro1AsNewPlaythrough()
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        PlaythroughCommit commit = machine.TickBoot(true, true)!.Value;
        Assert.Equal(PlaythroughCommitKind.RoundIntro, commit.Kind);
        Assert.Equal(1, commit.Round);
        Assert.True(commit.IsNewPlaythrough);   // the boundary reset is unconditional at entry (spec §5.4)
        Assert.False(commit.ViaPlayAgain);      // no ResetRun at session start
        Assert.False(commit.ReanchorClock);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void T3_Guard_UnsyncedClock_Rejects(bool cycleSynced, bool runSynced)
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        Assert.Null(machine.TickBoot(cycleSynced, runSynced));
        Assert.Equal(PlaythroughState.Boot, machine.State);
    }

    [Fact]
    public void T3_Guard_AlreadyPastBoot_Rejects()
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        ApplyCommit(machine, machine.TickBoot(true, true)!.Value);
        Assert.Null(machine.TickBoot(true, true));
    }

    // --- T4: RoundIntro → InRound -------------------------------------------------------------

    [Fact]
    public void T4_IntroTimer_ElapsesToRoundLive_NeverEarly()
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        ApplyCommit(machine, machine.TickBoot(true, true)!.Value);
        Assert.Null(machine.TickTimers(0.5, TwoPeers));
        PlaythroughCommit commit = machine.TickTimers(0.6, TwoPeers)!.Value;
        Assert.Equal(PlaythroughCommitKind.RoundLive, commit.Kind);
        Assert.Equal(1, commit.Round);
    }

    // --- T5: band crossings are NOT machine transitions ---------------------------------------

    [Theory]
    [InlineData(PhaseEventKind.DayToDusk)]
    [InlineData(PhaseEventKind.DuskToNight)]
    [InlineData(PhaseEventKind.DawnToDay)]
    public void T5_NonDawnCrossings_AreNotMachineTransitions(PhaseEventKind kind)
    {
        var machine = AtInRound();
        Assert.Null(machine.OnPhaseCrossed(kind, 0));
        Assert.Equal(PlaythroughState.InRound, machine.State);
    }

    // --- T6/T7: the verdict instant ------------------------------------------------------------

    [Fact]
    public void T6_Verdict_NoPredicateFires_CommitsRoundEnd()
    {
        var machine = AtInRound(new FakePredicate("never", _ => null));
        PlaythroughCommit commit = machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 1)!.Value;
        Assert.Equal(PlaythroughCommitKind.RoundEnd, commit.Kind);
        Assert.Equal(1, commit.Round);
        Assert.Null(commit.Outcome);
    }

    [Fact]
    public void T7_Verdict_PredicateOutcome_CommitsLossCarryingIt()
    {
        var outcome = new RunOutcome(RunOutcomeKind.QuotaMissed, 1, 3, 1);
        var machine = AtInRound(new FakePredicate("quota", _ => outcome));
        PlaythroughCommit commit = machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 1)!.Value;
        Assert.Equal(PlaythroughCommitKind.Loss, commit.Kind);
        Assert.Equal(outcome, commit.Outcome);
    }

    [Fact]
    public void Verdict_PriorityIsRegistrationOrder_FirstNonNullWins_LaterNeverConsulted()
    {
        var first = new FakePredicate("first-null", _ => null);
        var second = new FakePredicate("second-wins", r => new RunOutcome(RunOutcomeKind.QuotaMissed, r, 9, 0));
        var third = new FakePredicate("third-shadowed", r => new RunOutcome(RunOutcomeKind.QuotaMissed, r, 111, 111));
        var machine = AtInRound(first, second, third);
        PlaythroughCommit commit = machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 1)!.Value;
        Assert.Equal(9, commit.Outcome!.Value.Demand); // second's, never third's
        Assert.Equal(1, first.Invocations);
        Assert.Equal(1, second.Invocations);
        Assert.Equal(0, third.Invocations); // MECHANICS-BIBLE §7: explicit priority, first non-null wins
    }

    [Fact]
    public void Verdict_EvaluatedAtVerdictInstantOnly_NoPollingPathMidRound()
    {
        var wouldFail = new FakePredicate("always-fails", r => new RunOutcome(RunOutcomeKind.QuotaMissed, r, 99, 0));
        var machine = AtInRound(wouldFail);
        for (int i = 0; i < 100; i++)
            Assert.Null(machine.TickTimers(0.5, TwoPeers)); // a whole night of ticking, quota failing throughout
        Assert.Equal(0, wouldFail.Invocations);              // never consulted outside the crossing
        Assert.Equal(PlaythroughState.InRound, machine.State);
    }

    [Theory]
    [InlineData(PlaythroughState.Boot)]
    [InlineData(PlaythroughState.RoundIntro)]
    [InlineData(PlaythroughState.RoundEnd)]
    [InlineData(PlaythroughState.UpgradeLobby)]
    [InlineData(PlaythroughState.Loss)]
    public void Verdict_Guard_NightToDawnOutsideInRound_Rejected(PlaythroughState state)
    {
        var predicate = new FakePredicate("guarded", r => new RunOutcome(RunOutcomeKind.QuotaMissed, r, 1, 0));
        var machine = new PlaythroughMachine(1, 1, 1);
        machine.RegisterLossPredicate(predicate);
        machine.Apply(state, 1, -1);
        Assert.Null(machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 1));
        Assert.Equal(0, predicate.Invocations);
    }

    [Fact]
    public void Hitch_BatchDeliveredInOrder_ExactlyOneVerdict()
    {
        // Spec §6 case 11: RunDriver's ordinal walk emits every missed crossing in order in
        // one tick; the verdict runs at the NightToDawn's position in the sequence exactly as
        // it would alone, and the state having advanced guards everything after it.
        var machine = AtInRound();
        var commits = new List<PlaythroughCommit>();
        foreach (PhaseEventKind kind in new[]
                 { PhaseEventKind.DuskToNight, PhaseEventKind.NightToDawn, PhaseEventKind.DawnToDay })
        {
            if (machine.OnPhaseCrossed(kind, 1) is { } commit)
            {
                commits.Add(commit);
                ApplyCommit(machine, commit); // the CallLocal handler applies synchronously (probe 1)
            }
        }
        Assert.Single(commits);
        Assert.Equal(PlaythroughCommitKind.RoundEnd, commits[0].Kind);
        // A pathological batch containing a SECOND night's crossing verdicts once, not twice.
        Assert.Null(machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 2));
    }

    // --- T8: RoundEnd → UpgradeLobby ------------------------------------------------------------

    private static PlaythroughMachine AtRoundEnd()
    {
        var machine = AtInRound();
        ApplyCommit(machine, machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 1)!.Value);
        Assert.Equal(PlaythroughState.RoundEnd, machine.State);
        return machine;
    }

    [Fact]
    public void T8_TallyTimer_CommitsUpgradeLobby()
    {
        var machine = AtRoundEnd();
        Assert.Null(machine.TickTimers(0.5, TwoPeers));
        PlaythroughCommit commit = machine.TickTimers(0.6, TwoPeers)!.Value;
        Assert.Equal(PlaythroughCommitKind.UpgradeLobby, commit.Kind);
        Assert.Equal(1, commit.Round); // roundJustSurvived
    }

    [Fact]
    public void T8_AllReadySkip_EveryConnectedPeerReady()
    {
        var machine = AtRoundEnd();
        Assert.True(machine.OnReadyAdvance(10, TwoPeers, out PlaythroughCommit? none));
        Assert.Null(none); // half the room is not a skip
        Assert.True(machine.OnReadyAdvance(20, TwoPeers, out PlaythroughCommit? commit));
        Assert.Equal(PlaythroughCommitKind.UpgradeLobby, commit!.Value.Kind);
    }

    [Fact]
    public void T8_DisconnectShrinksTheSet_SkipCompletesOnTick_NeverStrands()
    {
        var machine = AtRoundEnd();
        machine.OnReadyAdvance(10, TwoPeers, out _);
        // Peer 20 disconnects; the next tick re-checks all-ready against the CURRENT set.
        PlaythroughCommit? commit = machine.TickTimers(0.01, new[] { 10 });
        Assert.Equal(PlaythroughCommitKind.UpgradeLobby, commit!.Value.Kind);
    }

    [Fact]
    public void T8_EmptyRoom_NoVacuousSkip_TimerStillGuarantees()
    {
        var machine = AtRoundEnd();
        int[] nobody = Array.Empty<int>();
        Assert.Null(machine.TickTimers(0.5, nobody));       // no instant vacuous advance
        PlaythroughCommit? commit = machine.TickTimers(0.6, nobody); // the AFK-guarantee timer still fires
        Assert.Equal(PlaythroughCommitKind.UpgradeLobby, commit!.Value.Kind);
    }

    [Theory]
    [InlineData(PlaythroughState.Boot)]
    [InlineData(PlaythroughState.RoundIntro)]
    [InlineData(PlaythroughState.InRound)]
    [InlineData(PlaythroughState.Loss)]
    public void ReadyAdvance_OutOfState_SilentlyDropped(PlaythroughState state)
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        machine.Apply(state, 1, -1);
        Assert.False(machine.OnReadyAdvance(10, TwoPeers, out PlaythroughCommit? commit)); // stale echo, not an error
        Assert.Null(commit);
        Assert.Equal(state, machine.State);
    }

    [Fact]
    public void ReadySet_ClearsBetweenWindows_LobbyNeedsItsOwnReadies()
    {
        var machine = AtRoundEnd();
        machine.OnReadyAdvance(10, TwoPeers, out _);
        machine.OnReadyAdvance(20, TwoPeers, out PlaythroughCommit? toLobby);
        ApplyCommit(machine, toLobby!.Value);
        Assert.Equal(PlaythroughState.UpgradeLobby, machine.State);
        // RoundEnd's readies must not carry into the lobby's window.
        Assert.Null(machine.TickTimers(0.01, TwoPeers));
        machine.OnReadyAdvance(10, TwoPeers, out PlaythroughCommit? still);
        Assert.Null(still);
        machine.OnReadyAdvance(20, TwoPeers, out PlaythroughCommit? advance);
        Assert.Equal(PlaythroughCommitKind.RoundIntro, advance!.Value.Kind);
    }

    // --- T9: UpgradeLobby → RoundIntro(N+1) ----------------------------------------------------

    [Fact]
    public void T9_LobbyTimer_CommitsNextRoundIntro_WithReanchor_NotANewPlaythrough()
    {
        var machine = AtRoundEnd();
        ApplyCommit(machine, machine.TickTimers(1.1, TwoPeers)!.Value); // → lobby
        PlaythroughCommit commit = machine.TickTimers(1.1, TwoPeers)!.Value;
        Assert.Equal(PlaythroughCommitKind.RoundIntro, commit.Kind);
        Assert.Equal(2, commit.Round);
        Assert.True(commit.ReanchorClock);
        Assert.False(commit.IsNewPlaythrough); // the boundary reset must NOT run between rounds — the map remembers
        Assert.False(commit.ViaPlayAgain);
    }

    // --- T10: Loss → RoundIntro(1) ---------------------------------------------------------------

    [Fact]
    public void T10_PlayAgain_OnlyInLoss_SecondRequestDroppedByGuard()
    {
        var outcome = new RunOutcome(RunOutcomeKind.QuotaMissed, 1, 3, 0);
        var machine = AtInRound(new FakePredicate("quota", _ => outcome));
        ApplyCommit(machine, machine.OnPhaseCrossed(PhaseEventKind.NightToDawn, 1)!.Value);
        Assert.Equal(PlaythroughState.Loss, machine.State);

        Assert.True(machine.OnPlayAgain(out PlaythroughCommit? commit));
        Assert.Equal(PlaythroughCommitKind.RoundIntro, commit!.Value.Kind);
        Assert.Equal(1, commit.Value.Round);
        Assert.True(commit.Value.IsNewPlaythrough);
        Assert.True(commit.Value.ViaPlayAgain); // T10 additionally drives RunDriver.ResetRun()

        ApplyCommit(machine, commit.Value);
        // Spec §6 case 5/7: the duplicate request finds State == RoundIntro and is dropped.
        Assert.False(machine.OnPlayAgain(out PlaythroughCommit? duplicate));
        Assert.Null(duplicate);
    }

    [Fact]
    public void Loss_HasNoTimeout_PlayersSitWithItAsLongAsTheyLike()
    {
        var machine = new PlaythroughMachine(1, 1, 1);
        machine.Apply(PlaythroughState.Loss, 3, -1);
        for (int i = 0; i < 1000; i++)
            Assert.Null(machine.TickTimers(1.0, TwoPeers));
        Assert.Equal(PlaythroughState.Loss, machine.State);
    }

    // --- Timers: extremes (spec §1.7 / MECHANICS-BIBLE §6) ---------------------------------------

    [Fact]
    public void Timers_NonPositiveOrInvalid_ClampToFloor_NeverZeroFire()
    {
        var machine = new PlaythroughMachine(0, -5, double.NaN);
        Assert.True(machine.TimersClamped);
        Assert.Equal(PlaythroughMachine.MinTimerSec, machine.IntroSec);
        Assert.Equal(PlaythroughMachine.MinTimerSec, machine.TallySec);
        Assert.Equal(PlaythroughMachine.MinTimerSec, machine.LobbySec);

        ApplyCommit(machine, machine.TickBoot(true, true)!.Value);
        Assert.Null(machine.TickTimers(0, TwoPeers));    // dt 0 can never fire anything
        Assert.Null(machine.TickTimers(0.5, TwoPeers));  // a clamped timer is a REAL 1 s window
        Assert.NotNull(machine.TickTimers(0.6, TwoPeers));
    }

    [Fact]
    public void Timers_DefaultsAreTheSpecPlaceholders()
    {
        var machine = new PlaythroughMachine();
        Assert.False(machine.TimersClamped);
        Assert.Equal(PlaythroughMachine.DefaultIntroSec, machine.IntroSec);
        Assert.Equal(PlaythroughMachine.DefaultTallySec, machine.TallySec);
        Assert.Equal(PlaythroughMachine.DefaultLobbySec, machine.LobbySec);
        Assert.Equal(-1, machine.DurationFor(PlaythroughCommitKind.RoundLive));
        Assert.Equal(-1, machine.DurationFor(PlaythroughCommitKind.Loss));
    }

    // --- The re-anchor's forward-only invariant (spec §1.5, criterion 4) -------------------------

    [Fact]
    public void Reanchor_MidSweep_TargetsSurvivedRound_TrackerEmitsExactlyTheOneDawnCrossing()
    {
        // Named case 1: re-anchor happens mid-DawnSweep of cycle N-1 (ordinal 4N-1).
        // Surviving round 1: clock at ordinal 3 (DawnSweep, cycle 0) → startCycles 1, target
        // ordinal 4 — and RunDriver's own catch-up walk emits exactly one DawnToDay(1).
        Assert.Equal(1, PlaythroughMachine.ReanchorStartCycles(1, 3));
        var crossings = new List<(PhaseEventKind Kind, int After)>();
        foreach ((PhaseEventKind kind, int after) in RunPhaseTracker.CrossingsBetween(3, 4))
            crossings.Add((kind, after));
        Assert.Single(crossings);
        Assert.Equal((PhaseEventKind.DawnToDay, 1), crossings[0]);
    }

    [Fact]
    public void Reanchor_PostDawnToDay_EqualOrdinal_NoCrossingNoStall()
    {
        // Named case 2: the lobby outlasted the sweep — DawnToDay(N) already fired naturally
        // and the clock sits in Day of cycle N (ordinal 4N). Target equals current: the
        // tracker walks nothing and nothing stalls.
        Assert.Equal(1, PlaythroughMachine.ReanchorStartCycles(1, 4));
        Assert.Empty(RunPhaseTracker.CrossingsBetween(4, 4));
    }

    [Fact]
    public void Reanchor_ClockOverranTheTarget_NextDayStart_NeverBackward()
    {
        // Degenerate short-period case: tally+lobby outlasted the whole dawn+day and the
        // clock is already in DuskSweep of cycle 1 (ordinal 5). A backward re-anchor to
        // ordinal 4 would stall the tracker (spec §1.5); the machine rides forward to the
        // next Day-band start instead.
        Assert.Equal(2, PlaythroughMachine.ReanchorStartCycles(1, 5));  // ordinal 8 >= 5
        Assert.Equal(2, PlaythroughMachine.ReanchorStartCycles(1, 8));  // exactly at a Day start: equal, no jump
    }

    [Fact]
    public void Reanchor_ForwardOnlyInvariant_HoldsEverywhere()
    {
        // THE invariant, stated for VER (spec §1.5): the target ordinal is never below the
        // clock's, so this path can never rewind RunPhaseTracker — backward jumps stay the
        // exclusive property of ResetRun.
        for (int round = 0; round <= 8; round++)
        {
            for (int ordinal = 0; ordinal <= 40; ordinal++)
            {
                int target = PlaythroughMachine.ReanchorStartCycles(round, ordinal) * 4;
                Assert.True(target >= ordinal,
                    $"ReanchorStartCycles({round}, {ordinal}) targets ordinal {target} — BACKWARD");
                Assert.True(target >= round * 4,
                    $"ReanchorStartCycles({round}, {ordinal}) targets ordinal {target}, before round {round}'s day");
            }
        }
    }
}
