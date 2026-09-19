using System.Collections.Immutable;
using MpFoundation.Game.Round;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>ROUND-1's engine-free tier.</b> Every transition, every named refusal, both timer-expiry
/// paths, the grace extension and what happens after it, a role holder disconnecting mid-round,
/// the role swap surviving a reset, score accumulation, and the wire's late-join fold.
///
/// <para><b>The loop is a pure function, so these are the real tests</b> — not a proxy for a scene
/// suite. The scene smoke that follows proves the DRIVER carries this into a live session
/// (teleports, broadcast, two peers agreeing); it cannot practically reach a 180 s timeout or a
/// grace extension, and it should not have to.</para>
///
/// <para><b>Everything is driven at a fixed 1/60 s tick</b>, the rate the driver actually steps
/// at, rather than with one convenient giant <c>dt</c>. A loop that only works when a phase is
/// crossed in a single step is a loop that has never met a frame.</para>
/// </summary>
public class HideSeekLoopTests
{
    private const float Dt = 1f / 60f;

    private const int Host = 11;
    private const int Joiner = 22;

    private static HideSeekTuning Tuning => HideSeekTuning.Default;

    /// <summary>Both humans present, nothing pressed, nothing held, nothing built. The resting
    /// input every test starts from and mutates with <c>with</c>.</summary>
    private static HideSeekInput Idle => new()
    {
        HumanPeerIds = ImmutableArray.Create(Host, Joiner),
    };

    private static HideSeekState Fresh() => HideSeekLoop.Restart(Tuning);

    /// <summary>One tick.</summary>
    private static HideSeekState Tick(HideSeekState s, HideSeekInput input) =>
        HideSeekLoop.Step(s, input, Dt, Tuning);

    /// <summary>Idle ticks until the phase changes or the budget runs out. Returns the state at
    /// the FIRST tick the phase differs, so a test can assert on the transition tick itself.</summary>
    private static HideSeekState RunUntilPhaseLeaves(HideSeekState s, HideSeekInput input,
        int maxTicks = 60 * 400)
    {
        HideSeekPhase from = s.Phase;
        for (int i = 0; i < maxTicks; i++)
        {
            s = Tick(s, input);
            if (s.Phase != from)
                return s;
        }
        Assert.Fail($"phase {from} never left after {maxTicks} ticks");
        return s;
    }

    /// <summary>Holding -> Hiding, the ordinary way, with both roles assigned.</summary>
    private static HideSeekState StartedRound()
    {
        HideSeekState s = Tick(Fresh(), Idle);          // fold assigns the roles
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true });
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        return s;
    }

    /// <summary>The card, asserted present and unwrapped. <c>Assert.NotNull</c> on a nullable
    /// STRUCT returns void in this xUnit, so it cannot stand in an expression; this keeps the
    /// assertion and the read in one place rather than scattering <c>!.Value</c>.</summary>
    private static HideSeekTally Card(in HideSeekState s)
    {
        Assert.True(s.LastTally.HasValue, "no card was computed");
        return s.LastTally!.Value;
    }

    /// <inheritdoc cref="Card"/>
    private static HideSeekTally CardOf(in HideSeekView v)
    {
        Assert.True(v.LastTally.HasValue, "the folded view carries no card");
        return v.LastTally!.Value;
    }

    // --- roles ---------------------------------------------------------------------------------

    [Fact]
    public void FirstRound_TheHostHides_AndTheSecondJoinerSeeks()
    {
        HideSeekState s = Tick(Fresh(), Idle);
        Assert.Equal(Host, s.HiderPeerId);
        Assert.Equal(Joiner, s.SeekerPeerId);
    }

    [Fact]
    public void ARosterOfOne_LeavesTheSeekerSlotEmpty()
    {
        HideSeekState s = Tick(Fresh(), new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Host) });
        Assert.Equal(Host, s.HiderPeerId);
        Assert.Equal(0, s.SeekerPeerId);
    }

    // --- Holding -> Hiding, and both of its refusals --------------------------------------------

    [Fact]
    public void Start_WithTwoHumansAndAHeldRackObject_EntersHiding()
    {
        HideSeekState s = StartedRound();
        Assert.Equal(Tuning.HidingSec, s.RemainingSec);
        Assert.Equal(HideSeekRefusal.None, s.Refusal);
    }

    [Fact]
    public void Start_WithOneHuman_IsRefused_NeedTwoPlayers()
    {
        var solo = new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Host) };
        HideSeekState s = Tick(Fresh(), solo);
        s = Tick(s, solo with { HostPressedStart = true, HiderHeldRackProp = true });

        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.Equal(HideSeekRefusal.NeedTwoPlayers, s.Refusal);
    }

    [Fact]
    public void Start_WithThreeHumans_IsAlsoRefused_NeedTwoPlayers()
    {
        // "Exactly two", not "at least two" — the transport still accepts six.
        var three = new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Host, Joiner, 33) };
        HideSeekState s = Tick(Fresh(), three);
        s = Tick(s, three with { HostPressedStart = true, HiderHeldRackProp = true });

        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.Equal(HideSeekRefusal.NeedTwoPlayers, s.Refusal);
    }

    [Fact]
    public void Start_WithEmptyHands_IsRefused_HiderMustHoldAnObject()
    {
        HideSeekState s = Tick(Fresh(), Idle);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = false });

        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.Equal(HideSeekRefusal.HiderMustHoldAnObject, s.Refusal);
    }

    [Fact]
    public void ARefusal_LastsExactlyOneTick()
    {
        HideSeekState s = Tick(Fresh(), Idle);
        s = Tick(s, Idle with { HostPressedStart = true });
        Assert.Equal(HideSeekRefusal.HiderMustHoldAnObject, s.Refusal);

        s = Tick(s, Idle);
        Assert.Equal(HideSeekRefusal.None, s.Refusal);
    }

    // --- Hiding -> Seeking, and both of its refusals ---------------------------------------------

    [Fact]
    public void Confirm_WithTheObjectDown_EntersSeeking()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });

        Assert.Equal(HideSeekPhase.Seeking, s.Phase);
        Assert.Equal(Tuning.SeekingSec, s.RemainingSec);
    }

    [Fact]
    public void Confirm_WhileStillHoldingTheTarget_IsRefused_PutTheObjectDownFirst()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true, HiderHoldsTarget = true });

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(HideSeekRefusal.PutTheObjectDownFirst, s.Refusal);
    }

    [Fact]
    public void Confirm_WithAnUnreachableHide_IsRefused_NobodyCouldReachThat()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true, TargetRetrievable = false });

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(HideSeekRefusal.NobodyCouldReachThat, s.Refusal);
    }

    [Fact]
    public void ARefusedConfirm_DoesNotStallTheHidingClock()
    {
        // The refusal and the countdown are independent: a hider mashing Confirm must not buy
        // themselves extra seconds.
        HideSeekState s = StartedRound();
        float before = s.RemainingSec;
        s = Tick(s, Idle with { HiderPressedConfirm = true, HiderHoldsTarget = true });

        Assert.Equal(HideSeekRefusal.PutTheObjectDownFirst, s.Refusal);
        Assert.True(s.RemainingSec < before, $"clock stalled at {s.RemainingSec}");
    }

    /// <summary><b>The default REACH-1 has not supplied yet.</b> An unmeasured retrievability must
    /// read as retrievable — a plain <c>bool</c> would default to false and refuse every Confirm
    /// in the game until REACH-1 landed, on a loop that looks correct in review.</summary>
    [Fact]
    public void AnUnmeasuredRetrievability_ReadsAsRetrievable()
    {
        Assert.True(new HideSeekInput().TargetRetrievableOrDefault);

        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true, TargetRetrievable = null });
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);
    }

    // --- the Hiding buzzer, the grace, and the broken hide ---------------------------------------

    [Fact]
    public void TheHidingBuzzer_WithAGoodHide_StartsSeeking()
    {
        HideSeekState s = RunUntilPhaseLeaves(StartedRound(), Idle);
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);
    }

    [Fact]
    public void TheHidingBuzzer_WithTheTargetStillHeld_ExtendsHidingOnce_AndSaysWhy()
    {
        HideSeekInput holding = Idle with { HiderHoldsTarget = true };
        HideSeekState s = StartedRound();

        // Run the 30 s out. The phase does not change, so RunUntilPhaseLeaves is not the tool.
        for (int i = 0; i < 60 * 31 && !s.HidingExtended; i++)
            s = Tick(s, holding);

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.True(s.HidingExtended);
        Assert.Equal(Tuning.HidingGraceSec, s.RemainingSec);
        Assert.Equal(HideSeekRefusal.PutTheObjectDownFirst, s.Refusal);
    }

    [Fact]
    public void TheHidingBuzzer_WithAnUnreachableHide_ExtendsAndSays_NobodyCouldReachThat()
    {
        HideSeekInput unreachable = Idle with { TargetRetrievable = false };
        HideSeekState s = StartedRound();
        for (int i = 0; i < 60 * 31 && !s.HidingExtended; i++)
            s = Tick(s, unreachable);

        Assert.True(s.HidingExtended);
        Assert.Equal(HideSeekRefusal.NobodyCouldReachThat, s.Refusal);
    }

    [Fact]
    public void AHideFixedDuringTheGrace_StartsSeekingNormally()
    {
        HideSeekInput unreachable = Idle with { TargetRetrievable = false };
        HideSeekState s = StartedRound();
        for (int i = 0; i < 60 * 31 && !s.HidingExtended; i++)
            s = Tick(s, unreachable);

        // The hider moves it somewhere findable and confirms.
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);
    }

    [Fact]
    public void AHideStillBrokenAfterTheGrace_GoesToTallyWithTheHiderScoringNothing()
    {
        HideSeekInput unreachable = Idle with { TargetRetrievable = false, TowersCompleted = 4 };
        HideSeekState s = StartedRound();
        for (int i = 0; i < 60 * 60 && s.Phase == HideSeekPhase.Hiding; i++)
            s = Tick(s, unreachable);

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        HideSeekTally card = Card(s);
        Assert.Equal(0, card.HiderGained);   // the hide failed; the towers are not a consolation
        Assert.Equal(0, card.SeekerGained);  // and the seeker was never asked to find it
        Assert.Equal(HideSeekRefusal.NobodyCouldReachThat, s.Refusal);
        Assert.Equal(0, s.ScoreOf(Host));
        Assert.Equal(0, s.ScoreOf(Joiner));
    }

    [Fact]
    public void TheGraceIsSpentOnce_NotOncePerBuzzer()
    {
        HideSeekInput unreachable = Idle with { TargetRetrievable = false };
        HideSeekState s = StartedRound();
        int extensions = 0;
        bool wasExtended = false;
        for (int i = 0; i < 60 * 60 && s.Phase == HideSeekPhase.Hiding; i++)
        {
            s = Tick(s, unreachable);
            if (s.HidingExtended && !wasExtended)
                extensions++;
            wasExtended = s.HidingExtended;
        }
        Assert.Equal(1, extensions);
        Assert.Equal(HideSeekPhase.Tally, s.Phase);
    }

    // --- Seeking -> Together, and the seek timeout ------------------------------------------------

    [Fact]
    public void TheFind_FreezesTheTowersAndTheClock_AndStampsTheFoundTick()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });

        // 20 s of seeking with the hider stacking.
        HideSeekInput stacking = Idle with { TowersCompleted = 3 };
        for (int i = 0; i < 60 * 20; i++)
            s = Tick(s, stacking);

        s = Tick(s, stacking with { TargetInDropOff = true });

        Assert.Equal(HideSeekPhase.Together, s.Phase);
        Assert.Equal(3, s.TowersAtFound);
        Assert.NotNull(s.FoundTick);
        Assert.InRange(s.RemainingAtFoundSec, 159, 160);
    }

    /// <summary>A tower finished on the exact tick the object lands in the bin IS counted — the
    /// fact is folded before the transition is evaluated. This is the fold-first ordering the loop
    /// inherited, stated as a test rather than as a comment.</summary>
    [Fact]
    public void ATowerFinishedOnTheFindTick_IsCounted()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        for (int i = 0; i < 60; i++)
            s = Tick(s, Idle with { TowersCompleted = 2 });

        s = Tick(s, Idle with { TowersCompleted = 3, TargetInDropOff = true });
        Assert.Equal(3, s.TowersAtFound);
    }

    [Fact]
    public void TheSeekTimeout_GoesStraightToTally_HiderKeepsTheTowers_SeekerScoresNothing()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });

        HideSeekInput stacking = Idle with { TowersCompleted = 5 };
        s = RunUntilPhaseLeaves(s, stacking);

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        HideSeekTally card = Card(s);
        Assert.Equal(5, card.HiderGained);
        Assert.Equal(0, card.SeekerGained);
        Assert.Equal(5, s.ScoreOf(Host));
        Assert.Equal(0, s.ScoreOf(Joiner));
    }

    // --- Together -> Tally -------------------------------------------------------------------------

    [Fact]
    public void Together_HasNoClock_AndEndsOnlyOnTheEndPress()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TargetInDropOff = true });
        Assert.Equal(HideSeekPhase.Together, s.Phase);

        for (int i = 0; i < 60 * 60; i++)
            s = Tick(s, Idle with { TargetInDropOff = true });
        Assert.Equal(HideSeekPhase.Together, s.Phase);

        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });
        Assert.Equal(HideSeekPhase.Tally, s.Phase);
    }

    [Fact]
    public void TheCard_IsComputedOnce_AndReadBackEveryTickOfTally()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        for (int i = 0; i < 60 * 10; i++)
            s = Tick(s, Idle with { TowersCompleted = 2 });
        s = Tick(s, Idle with { TowersCompleted = 2, TargetInDropOff = true });
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });

        HideSeekTally first = Card(s);
        // Facts keep arriving during Tally; the card must not move.
        for (int i = 0; i < 60 * 3; i++)
            s = Tick(s, Idle with { TowersCompleted = 99 });
        Assert.Equal(first, Card(s));
    }

    [Fact]
    public void TheCardCarriesTheRoundItIsAbout_EvenThoughTheIndexHasAlreadyAdvanced()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TargetInDropOff = true });
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });

        Assert.Equal(2, s.RoundIndex);
        Assert.Equal(1, Card(s).RoundIndex);
    }

    // --- the reset edge and the role swap -----------------------------------------------------------

    [Fact]
    public void Tally_LeadsBackToHolding_WithResetRequestedForExactlyOneTick_AndTheRolesSwapped()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TargetInDropOff = true });
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });
        Assert.Equal(HideSeekPhase.Tally, s.Phase);

        s = RunUntilPhaseLeaves(s, Idle);

        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.True(s.ResetRequested);
        Assert.Equal(Joiner, s.HiderPeerId);
        Assert.Equal(Host, s.SeekerPeerId);

        s = Tick(s, Idle);
        Assert.False(s.ResetRequested);
        // And the swap survives the next Holding fold — the role repair must not undo it.
        Assert.Equal(Joiner, s.HiderPeerId);
        Assert.Equal(Host, s.SeekerPeerId);
    }

    [Fact]
    public void ScoresAccumulateAcrossRounds()
    {
        HideSeekState s = StartedRound();

        // Round 1: host hides, 4 towers, found.
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        for (int i = 0; i < 60 * 5; i++)
            s = Tick(s, Idle with { TowersCompleted = 4 });
        s = Tick(s, Idle with { TowersCompleted = 4, TargetInDropOff = true });
        int seekerGain1 = s.RemainingAtFoundSec;
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });
        s = RunUntilPhaseLeaves(s, Idle);   // back to Holding, roles swapped

        Assert.Equal(4, s.ScoreOf(Host));
        Assert.Equal(seekerGain1, s.ScoreOf(Joiner));

        // Round 2: the joiner hides now, 2 towers, seek times out.
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true });
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = RunUntilPhaseLeaves(s, Idle with { TowersCompleted = 2 });

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        Assert.Equal(4, s.ScoreOf(Host));                    // the host sought and timed out
        Assert.Equal(seekerGain1 + 2, s.ScoreOf(Joiner));    // the joiner hid and kept 2 towers
    }

    // --- a role holder leaving mid-round --------------------------------------------------------

    [Theory]
    [InlineData(Host)]
    [InlineData(Joiner)]
    public void ARoleHolderLeavingDuringHiding_EndsTheRoundImmediately(int leaver)
    {
        HideSeekState s = StartedRound();
        int stayed = leaver == Host ? Joiner : Host;
        s = Tick(s, new HideSeekInput { HumanPeerIds = ImmutableArray.Create(stayed) });

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        Assert.True(Card(s).EndedByDisconnect);
    }

    [Fact]
    public void ARoleHolderLeavingDuringSeeking_KeepsTheOthersScore()
    {
        // Round 1 banks something for the joiner, then round 2's hider drops out mid-seek.
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TargetInDropOff = true });
        int banked = s.RemainingAtFoundSec;
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });
        s = RunUntilPhaseLeaves(s, Idle);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true });
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);

        s = Tick(s, new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Host) });

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        Assert.Equal(banked, s.ScoreOf(Joiner));   // kept, not zeroed
        Assert.Equal(0, Card(s).HiderGained);
        Assert.Equal(0, Card(s).SeekerGained);
    }

    [Fact]
    public void ARoleHolderLeavingDuringTally_DoesNotReEndTheRound()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TargetInDropOff = true });
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });
        HideSeekTally card = Card(s);

        var solo = new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Host) };
        for (int i = 0; i < 60 * 3; i++)
            s = Tick(s, solo);

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        Assert.Equal(card, Card(s));   // the card is not recomputed
    }

    [Fact]
    public void AfterADisconnectEndedRound_TheLoopReturnsToHoldingAndReassignsRoles()
    {
        HideSeekState s = StartedRound();
        var solo = new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Joiner) };
        s = Tick(s, solo);
        Assert.Equal(HideSeekPhase.Tally, s.Phase);

        s = RunUntilPhaseLeaves(s, solo);
        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.True(s.ResetRequested);

        s = Tick(s, solo);
        Assert.Equal(Joiner, s.HiderPeerId);
        Assert.Equal(0, s.SeekerPeerId);
    }

    // --- timer hygiene -----------------------------------------------------------------------------

    [Fact]
    public void ANegativeOrNaNDt_IsTreatedAsZero()
    {
        HideSeekState s = StartedRound();
        float before = s.RemainingSec;
        s = HideSeekLoop.Step(s, Idle, -5f, Tuning);
        Assert.Equal(before, s.RemainingSec);
        s = HideSeekLoop.Step(s, Idle, float.NaN, Tuning);
        Assert.Equal(before, s.RemainingSec);
    }

    [Fact]
    public void EveryArmedTimer_FloorsAtOneSecond()
    {
        var silly = new HideSeekTuning
        {
            HidingSec = 0f, HidingGraceSec = -3f, SeekingSec = 0f, TallySec = 0f,
        };

        HideSeekState s = HideSeekLoop.Step(HideSeekLoop.Restart(silly), Idle, Dt, silly);
        s = HideSeekLoop.Step(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true },
            Dt, silly);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(HideSeekTuning.MinTimerSec, s.RemainingSec);

        s = HideSeekLoop.Step(s, Idle with { HiderPressedConfirm = true }, Dt, silly);
        Assert.Equal(HideSeekTuning.MinTimerSec, s.RemainingSec);
    }

    // --- the wire -----------------------------------------------------------------------------------

    /// <summary><b>A late joiner is complete from one message.</b> The property the whole absolute
    /// wire exists for: folding onto nothing and folding onto a stale view give the same
    /// answer.</summary>
    [Fact]
    public void Fold_IsIndependentOfWhateverTheClientAlreadyHad()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TowersCompleted = 3, TargetInDropOff = true });
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });

        HideSeekWire wire = HideSeekWire.Encode(s, new[] { Host, Joiner });

        HideSeekView fromNothing = HideSeekWire.Fold(null, wire);
        HideSeekView stale = HideSeekWire.Fold(null,
            HideSeekWire.Encode(HideSeekLoop.Restart(Tuning), new[] { 999 }));
        HideSeekView fromStale = HideSeekWire.Fold(stale, wire);

        Assert.Equal(fromNothing, fromStale);
        Assert.Equal(HideSeekPhase.Tally, fromNothing.Phase);
        Assert.Equal(Host, fromNothing.HiderPeerId);
        Assert.Equal(3, CardOf(fromNothing).HiderGained);
    }

    /// <summary>
    /// <b>The positive control for the hand-written equality above.</b> A record struct's
    /// synthesized <c>Equals</c> compares an <c>ImmutableArray</c> member by REFERENCE, so two
    /// messages encoded from identical state came out unequal — which is exactly what the driver
    /// uses to decide whether to broadcast, so it would have sent a reliable RPC to every peer 60
    /// times a second. This is the test that found it; if someone deletes
    /// <c>HideSeekWire.Equals</c>, this is what goes red.
    /// </summary>
    [Fact]
    public void TwoMessagesEncodedFromTheSameState_AreEqual()
    {
        HideSeekState s = StartedRound();
        HideSeekWire a = HideSeekWire.Encode(s, new[] { Host, Joiner });
        HideSeekWire b = HideSeekWire.Encode(s, new[] { Host, Joiner });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(HideSeekWire.Fold(null, a), HideSeekWire.Fold(null, b));

        // And a message that really did move is still unequal. Seven ticks, not one: the fastest
        // field on this message is RemainingTenths, so at a 1/60 s tick the wire is BYTE-IDENTICAL
        // for five or six consecutive ticks. That is not a rounding accident — it is what makes
        // "broadcast when the wire changes" self-throttle to 10 Hz on the one field that moves
        // every frame, without the driver owning a timer of its own.
        HideSeekState later = s;
        for (int i = 0; i < 7; i++)
            later = Tick(later, Idle);
        Assert.NotEqual(a, HideSeekWire.Encode(later, new[] { Host, Joiner }));
    }

    /// <summary>The self-throttle stated on its own: a whole second of 60 Hz ticks produces about
    /// ten distinct messages, not sixty. If someone adds a raw-seconds float to this message, this
    /// is what goes red — and the symptom in a live session would be a reliable RPC per peer per
    /// frame.</summary>
    [Fact]
    public void TheWireChangesAboutTenTimesASecond_NotSixty()
    {
        HideSeekState s = StartedRound();
        HideSeekWire last = HideSeekWire.Encode(s, new[] { Host, Joiner });
        int changes = 0;
        for (int i = 0; i < 60; i++)
        {
            s = Tick(s, Idle);
            HideSeekWire now = HideSeekWire.Encode(s, new[] { Host, Joiner });
            if (!now.Equals(last))
                changes++;
            last = now;
        }
        Assert.InRange(changes, 8, 12);
    }

    [Fact]
    public void Fold_IsIdempotent()
    {
        HideSeekState s = StartedRound();
        HideSeekWire wire = HideSeekWire.Encode(s, new[] { Host, Joiner });
        HideSeekView once = HideSeekWire.Fold(null, wire);
        Assert.Equal(once, HideSeekWire.Fold(once, wire));
    }

    /// <summary><b>Identity is not a byte.</b> The measured bug from the loop this replaces: two
    /// real ENet peer ids above 255 clamped to the same byte, and the fold threw on the duplicate
    /// key. Both survive here, distinct.</summary>
    [Fact]
    public void PeerIdsAboveAByte_SurviveTheWireDistinctly()
    {
        int a = 1_234_567;
        int b = 7_654_321;
        var roster = ImmutableArray.Create(a, b);

        HideSeekState s = HideSeekLoop.Step(HideSeekLoop.Restart(Tuning),
            new HideSeekInput { HumanPeerIds = roster }, Dt, Tuning);

        HideSeekView view = HideSeekWire.Fold(null, HideSeekWire.Encode(s, roster));

        Assert.Equal(2, view.Scores.Count);
        Assert.True(view.Scores.ContainsKey(a));
        Assert.True(view.Scores.ContainsKey(b));
        Assert.Equal(a, view.HiderPeerId);
        Assert.Equal(b, view.SeekerPeerId);
    }

    /// <summary>Scores clamp at 255 rather than wrapping — an honest ceiling for a display value,
    /// where a wrapped byte reading 4 while the real score is 260 is the dishonest one.</summary>
    [Fact]
    public void AnAbsurdScore_ClampsRatherThanWraps()
    {
        HideSeekState s = Tick(Fresh(), Idle) with
        {
            Scores = ImmutableDictionary<int, int>.Empty.SetItem(Host, 260).SetItem(Joiner, -4),
        };
        HideSeekView view = HideSeekWire.Fold(null, HideSeekWire.Encode(s, new[] { Host, Joiner }));

        Assert.Equal(255, view.ScoreOf(Host));
        Assert.Equal(0, view.ScoreOf(Joiner));
    }

    [Fact]
    public void PackAndUnpack_RoundTripEveryField()
    {
        HideSeekState s = StartedRound();
        s = Tick(s, Idle with { HiderPressedConfirm = true });
        s = Tick(s, Idle with { TowersCompleted = 7, TargetInDropOff = true });
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true });

        HideSeekWire wire = HideSeekWire.Encode(s, new[] { Host, Joiner });
        var p = wire.Pack();
        HideSeekWire back = HideSeekWire.Unpack(p.Phase, p.Round, p.RemainingTenths, p.Hider,
            p.Seeker, p.ScorePeers, p.ScoreValues, p.Refusal, p.Towers, p.TallyRound, p.TallyHider,
            p.TallyHiderGain, p.TallySeeker, p.TallySeekerGain, p.TallyByDisconnect);

        Assert.Equal(wire, back);
    }

    [Fact]
    public void AMessageWithNoCard_RoundTripsAsNoCard()
    {
        HideSeekWire wire = HideSeekWire.Encode(Fresh(), new[] { Host });
        Assert.Null(wire.Tally);

        var p = wire.Pack();
        Assert.Equal(HideSeekWire.NoTallyRound, p.TallyRound);
        HideSeekWire back = HideSeekWire.Unpack(p.Phase, p.Round, p.RemainingTenths, p.Hider,
            p.Seeker, p.ScorePeers, p.ScoreValues, p.Refusal, p.Towers, p.TallyRound, p.TallyHider,
            p.TallyHiderGain, p.TallySeeker, p.TallySeekerGain, p.TallyByDisconnect);
        Assert.Null(back.Tally);
    }

    /// <summary>A malformed pair of score arrays must produce a short roster, not an exception on
    /// a receive path. The server can be handed anything.</summary>
    [Fact]
    public void MismatchedScoreArrays_FoldToTheShorterRoster()
    {
        HideSeekWire wire = HideSeekWire.Unpack(0, 1, 0, Host, Joiner,
            new[] { Host, Joiner, 33 }, new[] { 5 }, 0, 0,
            HideSeekWire.NoTallyRound, 0, 0, 0, 0, false);
        HideSeekView view = HideSeekWire.Fold(null, wire);

        Assert.Single(view.Scores);
        Assert.Equal(5, view.ScoreOf(Host));
    }

    [Fact]
    public void ARefusalRidesTheWire()
    {
        HideSeekState s = Tick(Fresh(), Idle);
        s = Tick(s, Idle with { HostPressedStart = true });
        HideSeekView view = HideSeekWire.Fold(null, HideSeekWire.Encode(s, new[] { Host, Joiner }));
        Assert.Equal(HideSeekRefusal.HiderMustHoldAnObject, view.Refusal);
    }

    // --- the copy ------------------------------------------------------------------------------------

    [Fact]
    public void EveryRefusal_HasASentence()
    {
        foreach (HideSeekRefusal r in System.Enum.GetValues<HideSeekRefusal>())
        {
            string sentence = HideSeekText.RefusalSentence(r);
            if (r == HideSeekRefusal.None)
                Assert.Equal(string.Empty, sentence);
            else
                Assert.False(string.IsNullOrWhiteSpace(sentence),
                    $"{r} has no sentence — a silent refusal is the defect this enum exists to prevent");
        }
    }

    [Fact]
    public void EveryPhase_HasAName()
    {
        foreach (HideSeekPhase p in System.Enum.GetValues<HideSeekPhase>())
            Assert.False(string.IsNullOrWhiteSpace(HideSeekText.PhaseName(p)), $"{p} has no name");
    }

    [Theory]
    [InlineData(0f, "0:00")]
    [InlineData(0.9f, "0:00")]
    [InlineData(29.9f, "0:29")]   // floored: never tell a player they have time that has gone
    [InlineData(30f, "0:30")]
    [InlineData(180f, "3:00")]
    [InlineData(-4f, "0:00")]
    public void TheClockReadsMmSs_AndFloors(float sec, string expected) =>
        Assert.Equal(expected, HideSeekText.TimerText(sec));

    [Fact]
    public void TheStripLine_DropsTheClockInThePhasesThatHaveNone()
    {
        Assert.DoesNotContain(":", HideSeekText.StripLine(HideSeekPhase.Holding, 0f, "YOU HIDE", 1));
        Assert.DoesNotContain(":", HideSeekText.StripLine(HideSeekPhase.Together, 0f, "YOU SEEK", 2));
        Assert.Contains("0:24", HideSeekText.StripLine(HideSeekPhase.Hiding, 24.4f, "YOU HIDE", 1));
    }

    [Fact]
    public void TheStripLine_LeavesNoStraySeparatorForASpectator()
    {
        string line = HideSeekText.StripLine(HideSeekPhase.Seeking, 100f, string.Empty, 3);
        Assert.DoesNotContain("·  ·", line);
        Assert.Contains("ROUND 3", line);
    }

    [Fact]
    public void RoleText_NamesTheRoleAndNothingElse()
    {
        HideSeekView view = HideSeekWire.Fold(null,
            HideSeekWire.Encode(Tick(Fresh(), Idle), new[] { Host, Joiner }));

        Assert.Equal("YOU HIDE", view.RoleTextFor(Host));
        Assert.Equal("YOU SEEK", view.RoleTextFor(Joiner));
        Assert.Equal(string.Empty, view.RoleTextFor(999));
        Assert.Equal(string.Empty, view.RoleTextFor(0));
    }
}
