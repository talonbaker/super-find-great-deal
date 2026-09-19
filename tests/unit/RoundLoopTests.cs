using System.Collections.Immutable;
using MpFoundation.Game.Round;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The round loop's transition table and the five races SESSION-2 names</b> (D3,
/// <c>REVIEW-2026-09-15-BOTTLE-FREEWAY-STATE.md</c> §4; design doc
/// <c>docs/design/2026-09-15-round-loop.md</c>).
///
/// <para><b>Five planted controls, run and reverted — evidence quoted in the SESSION-2 phase-A
/// report.</b> Each was built by commenting out or rewriting one piece of <c>RoundLoop.Step</c>,
/// rebuilding, and filtering <c>dotnet test</c> to the affected test(s); the exact failure line
/// is quoted for each, and every mutation was reverted before the gate run below.</para>
/// <list type="bullet">
/// <item><b>Skipping <c>FoldFacts</c> entirely</b> (the ordering race, item 1) turned 9 of the 23
/// tests red, including <see cref="AShatterOnTheHornTick_TallyReadsCoinsAfterTheSpill"/> (it read
/// the PRE-spill coin, 20, instead of 3) and both join races.</item>
/// <item><b>Removing the <c>if (!input.HumansPresent) return s;</c> guard</b> (Gathering waits for
/// nobody) turned <see cref="Gathering_WithNoHumans_NeverStartsTheClock"/> red: <c>Expected:
/// Gathering  Actual: Tally</c> — ten seconds of ticks with nobody present raced all the way to
/// the first tally.</item>
/// <item><b>Deleting the <c>ResetRequested = false</c> clear at the top of <c>Step</c></b> turned
/// <see cref="ResetRequested_IsTrueForExactlyOneTick"/> red: <c>Assert.False() … Actual: True</c>
/// — the edge stayed set a second tick.</item>
/// <item><b>Making the Countdown branch read <c>HostPressedStart</c> and re-arm itself</b> turned
/// <see cref="StartPressedTwice_TheSecondPressDuringCountdownIsIgnored"/> red: expected ordinary
/// decay to <c>4.9833…</c>, actual <c>5</c> (re-armed to the full <c>CountdownSec</c> instead of
/// ticking down).</item>
/// <item><b>Removing the floor from <c>EnterRound</c></b> (<c>RemainingSec = t.RoundSec</c>, no
/// <c>Math.Max</c>) turned both <see cref="RoundSec_AtZeroOrNegative_ClampsToTheFloor"/> cases red:
/// <c>Expected: 1  Actual: -5</c> and <c>Expected: 1  Actual: 0</c>.</item>
/// </list>
/// </summary>
public class RoundLoopTests
{
    private static RoundLoopTuning T => RoundLoopTuning.Default;

    /// <summary>The tuning with every wait cut to the 1 s floor, so a transition table can be
    /// walked without simulating three real minutes. Nothing a race depends on moves.</summary>
    private static RoundLoopTuning Brisk => RoundLoopTuning.Default with
    {
        GatherSec = 1f,
        CountdownSec = 1f,
        RoundSec = 1f,
        TallySec = 1f,
    };

    private const float Dt = 1f / 60f;

    private static RoundLoopInput None => default;

    private static RoundLoopInput Coins(params (int rider, int amount)[] entries) => new()
    {
        CarriedCoinsPerRider = entries.ToImmutableDictionary(e => e.rider, e => e.amount),
    };

    // =======================================================================================
    // The transition table.
    // =======================================================================================

    /// <summary>Gathering -&gt; Countdown -&gt; Round -&gt; Tally -&gt; Countdown (round 2), walked
    /// once, with the exact input that causes each arrow named at the arrow.</summary>
    [Fact]
    public void TheTransitionTable_GatheringToCountdownToRoundToTallyToCountdown()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);
        Assert.Equal(RoundPhase.Gathering, s.Phase);
        Assert.Equal(1, s.RoundIndex);

        // GATHERING, no one home: waits, does not arm.
        s = RoundLoop.Step(s, None, Dt, t);
        Assert.Equal(RoundPhase.Gathering, s.Phase);
        Assert.False(s.GatherArmed);

        // A human arrives: arms THIS tick, does not also decrement.
        s = RoundLoop.Step(s, new RoundLoopInput { HumansPresent = true }, Dt, t);
        Assert.Equal(RoundPhase.Gathering, s.Phase);
        Assert.True(s.GatherArmed);
        Assert.Equal(t.GatherSec, s.RemainingSec, 0.0001f);

        // GATHERING --GatherSec elapses--> COUNTDOWN.
        s = RoundLoop.Step(s, None, 1f, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
        Assert.Equal(t.CountdownSec, s.RemainingSec, 0.0001f);

        // COUNTDOWN --countdown 0--> ROUND.
        s = RoundLoop.Step(s, None, 1f, t);
        Assert.Equal(RoundPhase.Round, s.Phase);
        Assert.Equal(t.RoundSec, s.RemainingSec, 0.0001f);

        // ROUND: coins accrue, the loop just stores what it's told.
        s = RoundLoop.Step(s, Coins((1, 12)), Dt, t);
        Assert.Equal(RoundPhase.Round, s.Phase);
        Assert.Equal(12, s.CarriedCoinsPerRider[1]);

        // ROUND --the horn--> TALLY, and the verdict is decided on the way in.
        s = RoundLoop.Step(s, None, 1f, t);
        Assert.Equal(RoundPhase.Tally, s.Phase);
        Assert.NotNull(s.LastTally);
        Assert.Equal(new[] { 1 }, s.LastTally!.Value.WinnerRiderIds.ToArray());
        Assert.Equal(12, s.LastTally!.Value.Lines.Single(l => l.RiderId == 1).Coins);
        Assert.False(s.ResetRequested); // not yet — the commit that raises it hasn't landed

        // TALLY --hold expires--> COUNTDOWN, round 2, the reset commit.
        s = RoundLoop.Step(s, None, 1f, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
        Assert.Equal(2, s.RoundIndex);
        Assert.True(s.ResetRequested);
        Assert.Equal(0, s.CarriedCoinsPerRider[1]); // the world reset: coins back to zero
    }

    /// <summary>Pressing start in Gathering skips the rest of the gather clock outright, with or
    /// without a human ever having been recorded present.</summary>
    [Fact]
    public void HostPressedStart_InGathering_SkipsTheRestOfTheGatherClock()
    {
        RoundLoopTuning t = T; // the real 20s default — proves the skip, not just a brisk clock
        RoundLoopState s = RoundLoop.Restart(t);

        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
        Assert.Equal(t.CountdownSec, s.RemainingSec, 0.0001f);
    }

    // =======================================================================================
    // Race 1: a shatter on the horn tick.
    // =======================================================================================

    /// <summary>
    /// <b>The packet's own example.</b> A bottle shatters on the exact tick the round ends: the
    /// shatter is counted, the spill (a lower carried-coin report on the SAME tick) is applied,
    /// and the frozen tally reads the coin count AFTER the spill — "a bottle that breaks on the
    /// horn loses them, that is the joke."
    /// </summary>
    [Fact]
    public void AShatterOnTheHornTick_TallyReadsCoinsAfterTheSpill()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t); // -> Countdown
        s = RoundLoop.Step(s, None, 1f, t);                                          // -> Round

        s = RoundLoop.Step(s, Coins((1, 20)), Dt, t);
        Assert.Equal(20, s.CarriedCoinsPerRider[1]);

        // The horn tick: the same tick that ends the round ALSO carries the shatter and the
        // post-spill coin report (a lower number — the coins are gone).
        var hornInput = new RoundLoopInput
        {
            CarriedCoinsPerRider = ImmutableDictionary<int, int>.Empty.Add(1, 3), // spilled down to 3
            Shatters = ImmutableArray.Create(new RoundShatterEvent(1, "car", null)),
        };
        s = RoundLoop.Step(s, hornInput, 1f, t); // dt=1f forces RemainingSec <= 0 -> Tally

        Assert.Equal(RoundPhase.Tally, s.Phase);
        RoundTallyLine line = s.LastTally!.Value.Lines.Single(l => l.RiderId == 1);
        Assert.Equal(3, line.Coins);   // AFTER the spill, not the 20 carried a moment before
        Assert.Equal(1, line.Shatters);
    }

    /// <summary>A shatter with an unknown witness state (null, phase A's only value) counts the
    /// shatter but never the witnessed count — WITNESS-1 owns turning null into yes or no.</summary>
    [Fact]
    public void AnUnwitnessedShatter_CountsTheShatter_NeverTheWitnessedCount()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);

        s = RoundLoop.Step(s, new RoundLoopInput
        {
            Shatters = ImmutableArray.Create(new RoundShatterEvent(1, "car", null)),
        }, Dt, t);
        Assert.Equal(1, s.ShattersPerRider[1]);
        Assert.Equal(0, s.WitnessedShattersPerRider.GetValueOrDefault(1));

        s = RoundLoop.Step(s, new RoundLoopInput
        {
            Shatters = ImmutableArray.Create(new RoundShatterEvent(1, "car", true)),
        }, Dt, t);
        Assert.Equal(2, s.ShattersPerRider[1]);
        Assert.Equal(1, s.WitnessedShattersPerRider[1]);
    }

    // =======================================================================================
    // Race 2 & 3: joins mid-loop.
    // =======================================================================================

    /// <summary>A rider whose id first appears during Countdown is held at a marker (an engine
    /// concern, not this model's) but IS in the round that is about to start — they show up in
    /// that round's own tally.</summary>
    [Fact]
    public void AJoinDuringCountdown_IsInTheRound()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t); // -> Countdown

        // Rider 2 joins mid-countdown.
        s = RoundLoop.Step(s, Coins((2, 0)), Dt, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);

        s = RoundLoop.Step(s, None, 1f, t); // -> Round
        s = RoundLoop.Step(s, Coins((2, 7)), Dt, t);
        s = RoundLoop.Step(s, None, 1f, t); // -> Tally

        Assert.True(s.LastTally!.Value.Lines.Any(l => l.RiderId == 2 && l.Coins == 7));
    }

    /// <summary>A rider whose id first appears during Tally sees the card that is already up (it
    /// does not retroactively include them) and is folded into the NEXT round instead.</summary>
    [Fact]
    public void AJoinDuringTally_SeesTheCurrentCard_AndIsInTheNextRound()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t); // -> Countdown
        s = RoundLoop.Step(s, None, 1f, t);                                          // -> Round
        s = RoundLoop.Step(s, Coins((1, 5)), Dt, t);
        s = RoundLoop.Step(s, None, 1f, t);                                          // -> Tally

        Assert.Equal(RoundPhase.Tally, s.Phase);
        Assert.DoesNotContain(s.LastTally!.Value.Lines, l => l.RiderId == 2);

        // Rider 2 joins mid-tally.
        s = RoundLoop.Step(s, Coins((2, 0)), Dt, t);
        Assert.Equal(RoundPhase.Tally, s.Phase);
        // The card already up still does not show them — it was frozen before they existed.
        Assert.DoesNotContain(s.LastTally!.Value.Lines, l => l.RiderId == 2);

        s = RoundLoop.Step(s, None, 1f, t); // -> Countdown, round 2, the reset commit
        Assert.Equal(2, s.RoundIndex);
        s = RoundLoop.Step(s, None, 1f, t); // -> Round 2
        s = RoundLoop.Step(s, Coins((2, 9)), Dt, t);
        s = RoundLoop.Step(s, None, 1f, t); // -> Tally 2

        Assert.True(s.LastTally!.Value.Lines.Any(l => l.RiderId == 2 && l.Coins == 9));
    }

    // =======================================================================================
    // Race 4: start pressed twice.
    // =======================================================================================

    /// <summary>The second start press — during Countdown, one tick after the first already
    /// fired — changes nothing. Countdown never reads <c>HostPressedStart</c> at all, which is
    /// the same "dropped silently, not errored" posture <c>ShiftLoop</c>'s LAUNCHING takes on a
    /// second ready pull.</summary>
    [Fact]
    public void StartPressedTwice_TheSecondPressDuringCountdownIsIgnored()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);

        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
        float remainingAfterFirstPress = s.RemainingSec;

        // A second press lands one tick later, still well inside Countdown.
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
        // Ordinary ticking only — not re-armed to the full CountdownSec a second time.
        Assert.Equal(remainingAfterFirstPress - Dt, s.RemainingSec, 0.0001f);
    }

    /// <summary>Pressing start twice in the SAME tick's sense — i.e. the flag stays true on a
    /// later tick that is still Gathering because nothing consumed it — is likewise a no-op past
    /// the first transition, since Gathering is simply not the phase anymore.</summary>
    [Fact]
    public void StartPressedTwice_OnceAlreadyPastGathering_ChangesNothing()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t); // -> Countdown
        s = RoundLoop.Step(s, None, 1f, t);                                          // -> Round

        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        Assert.Equal(RoundPhase.Round, s.Phase); // did not jump back to Countdown
    }

    // =======================================================================================
    // Race 5: every timer at exactly 0 and at a negative configured value.
    // =======================================================================================

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void GatherSec_AtZeroOrNegative_ClampsToTheFloor(float configured)
    {
        RoundLoopTuning t = Brisk with { GatherSec = configured };
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HumansPresent = true }, Dt, t); // arms
        Assert.Equal(RoundLoopTuning.MinTimerSec, s.RemainingSec, 0.0001f);

        // Exactly the floor's worth of dt fires it (inclusive <= 0 bound).
        s = RoundLoop.Step(s, None, RoundLoopTuning.MinTimerSec, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void CountdownSec_AtZeroOrNegative_ClampsToTheFloor(float configured)
    {
        RoundLoopTuning t = Brisk with { CountdownSec = configured };
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        Assert.Equal(RoundLoopTuning.MinTimerSec, s.RemainingSec, 0.0001f);

        s = RoundLoop.Step(s, None, RoundLoopTuning.MinTimerSec, t);
        Assert.Equal(RoundPhase.Round, s.Phase);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void RoundSec_AtZeroOrNegative_ClampsToTheFloor(float configured)
    {
        RoundLoopTuning t = Brisk with { RoundSec = configured };
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t); // -> Countdown
        s = RoundLoop.Step(s, None, 1f, t);                                          // -> Round
        Assert.Equal(RoundLoopTuning.MinTimerSec, s.RemainingSec, 0.0001f);

        s = RoundLoop.Step(s, None, RoundLoopTuning.MinTimerSec, t);
        Assert.Equal(RoundPhase.Tally, s.Phase);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void TallySec_AtZeroOrNegative_ClampsToTheFloor(float configured)
    {
        RoundLoopTuning t = Brisk with { TallySec = configured };
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, None, 1f, t); // -> Tally
        Assert.Equal(RoundLoopTuning.MinTimerSec, s.RemainingSec, 0.0001f);

        s = RoundLoop.Step(s, None, RoundLoopTuning.MinTimerSec, t);
        Assert.Equal(RoundPhase.Countdown, s.Phase);
    }

    /// <summary>Landing at exactly zero (not a hair under) still fires, same tick — the "fires at
    /// <c>&lt;= 0</c> after the decrement" rule read literally at its own boundary.</summary>
    [Fact]
    public void ATimerLandingAtExactlyZero_FiresOnThatTick()
    {
        RoundLoopTuning t = Brisk; // CountdownSec = 1f
        RoundLoopState s = RoundLoop.Restart(t);
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        Assert.Equal(1f, s.RemainingSec, 0.0001f);

        s = RoundLoop.Step(s, None, 1f, t); // 1 - 1 = 0 exactly
        Assert.Equal(RoundPhase.Round, s.Phase);
    }

    // =======================================================================================
    // Gathering's own rule: the clock does not run with nobody there.
    // =======================================================================================

    [Fact]
    public void Gathering_WithNoHumans_NeverStartsTheClock()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = RoundLoop.Restart(t);

        for (int i = 0; i < 600; i++) // ten seconds of ticks at 60 Hz — far past GatherSec
            s = RoundLoop.Step(s, None, Dt, t);

        Assert.Equal(RoundPhase.Gathering, s.Phase);
        Assert.False(s.GatherArmed);
    }

    // =======================================================================================
    // The reset edge.
    // =======================================================================================

    [Fact]
    public void ResetRequested_IsTrueForExactlyOneTick()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, None, 1f, t); // -> Tally
        Assert.False(s.ResetRequested);

        s = RoundLoop.Step(s, None, 1f, t); // -> Countdown, round 2: the commit
        Assert.True(s.ResetRequested);

        s = RoundLoop.Step(s, None, Dt, t); // one more tick, still Countdown
        Assert.Equal(RoundPhase.Countdown, s.Phase);
        Assert.False(s.ResetRequested);
    }

    // =======================================================================================
    // Ties.
    // =======================================================================================

    [Fact]
    public void ATie_SharesTheWin()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, Coins((1, 10), (2, 10), (3, 4)), Dt, t);
        s = RoundLoop.Step(s, None, 1f, t); // -> Tally

        Assert.Equal(new[] { 1, 2 }, s.LastTally!.Value.WinnerRiderIds.OrderBy(x => x).ToArray());
    }

    // =======================================================================================
    // RoundWire: byte arithmetic and late-join completeness.
    // =======================================================================================

    [Fact]
    public void RoundWire_EncodeThenFold_RoundTripsExactly()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, Coins((1, 40), (2, 17)), Dt, t);
        s = RoundLoop.Step(s, new RoundLoopInput
        {
            Shatters = ImmutableArray.Create(new RoundShatterEvent(2, "car", true)),
        }, 1f, t); // -> Tally

        RoundWire wire = RoundWire.Encode(s, new[] { 1, 2 });
        RoundWireView view = RoundWire.Fold(null, wire);

        Assert.Equal(RoundPhase.Tally, view.Phase);
        Assert.Equal(40, view.CarriedCoinsPerRider[1]);
        Assert.Equal(17, view.CarriedCoinsPerRider[2]);
        Assert.Equal(1, view.ShattersPerRider[2]);
        RoundTallyLine line2 = view.LastTally!.Value.Lines.Single(l => l.RiderId == 2);
        Assert.Equal(1, line2.WitnessedShatters);
    }

    /// <summary>The whole late-join property, made explicit: folding with no prior view and
    /// folding on top of an unrelated stale one produce the same result, because every field is
    /// absolute.</summary>
    [Fact]
    public void RoundWire_Fold_IsCompleteFromOneMessage_RegardlessOfPriorView()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, Coins((1, 55)), Dt, t);
        RoundWire wire = RoundWire.Encode(s, new[] { 1 });

        RoundWireView freshJoiner = RoundWire.Fold(null, wire);
        RoundWireView staleReconnector = RoundWire.Fold(
            new RoundWireView(RoundPhase.Tally, 99, 0f,
                ImmutableDictionary<int, int>.Empty.Add(1, 0).Add(9, 250),
                ImmutableDictionary<int, int>.Empty, null),
            wire);

        // ImmutableDictionary has no value equality of its own, so compare field-by-field rather
        // than struct-equal the two views (their dictionaries are equal in content, not in
        // reference, and a plain Assert.Equal on the record would spuriously fail on that alone).
        Assert.Equal(freshJoiner.Phase, staleReconnector.Phase);
        Assert.Equal(freshJoiner.Round, staleReconnector.Round);
        Assert.Equal(freshJoiner.RemainingSec, staleReconnector.RemainingSec, 0.0001f);
        Assert.Equal(freshJoiner.CarriedCoinsPerRider, staleReconnector.CarriedCoinsPerRider);
        Assert.Equal(freshJoiner.ShattersPerRider, staleReconnector.ShattersPerRider);
        Assert.Equal(55, freshJoiner.CarriedCoinsPerRider[1]);
        Assert.False(freshJoiner.CarriedCoinsPerRider.ContainsKey(9)); // the stale peer's ghost rider is gone
    }

    [Fact]
    public void RoundWire_ByteFields_ClampAtTheTop_RatherThanWrap()
    {
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, Coins((1, 9000)), Dt, t); // far past a byte

        RoundWire wire = RoundWire.Encode(s, new[] { 1 });
        Assert.Equal(255, wire.Riders.Single().CarriedCoins); // clamped, not 9000 % 256 = 40
    }

    /// <summary>
    /// <b>Phase B's own regression, pinned here.</b> Real Godot multiplayer peer ids are arbitrary
    /// 32-bit values, not small connect-order indices — <c>Run-RoundLoopTest.ps1</c>'s first live
    /// run threw inside <c>Fold</c>'s <c>ToImmutableDictionary</c> the moment a fourth rider joined,
    /// because phase A's <c>RoundWireRider</c> clamped a rider's IDENTITY to a byte the same way it
    /// clamps a score, and two real peer ids both over 255 clamp to the identical 255. Two riders
    /// whose ids are nowhere near each other, both realistically large, must round-trip as two
    /// distinct entries rather than collide.
    /// </summary>
    [Fact]
    public void RoundWire_TwoLargeRealPeerIds_DoNotCollideOnTheWire()
    {
        const int riderA = 662618210; // a real peer id quoted elsewhere in this codebase's own logs
        const int riderB = 1298351102;
        RoundLoopTuning t = Brisk;
        RoundLoopState s = OnRound(RoundLoop.Restart(t), t);
        s = RoundLoop.Step(s, Coins((riderA, 12), (riderB, 34)), Dt, t);

        RoundWire wire = RoundWire.Encode(s, new[] { riderA, riderB });
        RoundWireView view = RoundWire.Fold(null, wire); // must not throw on a duplicate key

        Assert.Equal(2, wire.Riders.Length);
        Assert.Equal(12, view.CarriedCoinsPerRider[riderA]);
        Assert.Equal(34, view.CarriedCoinsPerRider[riderB]);
    }

    // =======================================================================================
    // Helpers.
    // =======================================================================================

    /// <summary>Start, count down, and land on ROUND.</summary>
    private static RoundLoopState OnRound(RoundLoopState s, in RoundLoopTuning t)
    {
        s = RoundLoop.Step(s, new RoundLoopInput { HostPressedStart = true }, Dt, t);
        s = RoundLoop.Step(s, None, 1f, t);
        Assert.Equal(RoundPhase.Round, s.Phase);
        return s;
    }
}
