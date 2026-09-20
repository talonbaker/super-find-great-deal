using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MpFoundation.Game.Round;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>TASK-1's gate</b>: the rule of the round, the once-only count and the settle timer, all
/// without an engine.
///
/// <para>Everything the hider's score depends on is in here, and that is deliberate — the scene
/// side of TASK-1 is bins, plates and a poll, and the only arithmetic in it is
/// "is this prop resting inside that box". The decision about whether a red ball in bin 1 is
/// worth a point is a pure function, so it is tested as one.</para>
/// </summary>
public class SortRuleTests
{
    private static readonly SortColour[] Colours =
        { SortColour.Red, SortColour.Blue, SortColour.Yellow };

    private static readonly SortShape[] Shapes =
        { SortShape.Cube, SortShape.Ball, SortShape.Can };

    // ==========================================================================================
    // SortRule.For -- the rule is derived from the round index and from nothing else
    // ==========================================================================================

    [Theory]
    [InlineData(1, SortBy.Colour)]
    [InlineData(2, SortBy.Shape)]
    [InlineData(3, SortBy.Colour)]
    [InlineData(4, SortBy.Shape)]
    [InlineData(99, SortBy.Colour)]
    [InlineData(100, SortBy.Shape)]
    public void For_AlternatesColourThenShape_StartingAtColour(int round, SortBy want) =>
        Assert.Equal(want, SortRule.For(round));

    /// <summary>A defaulted view reads round 0, and a wire that clamped could in principle hand
    /// over a negative. Both are the not-a-round case and both answer as round 1 does, because a
    /// plate that said SHAPE for half a frame before the first message landed would be the one
    /// readout in the room that lied.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void For_BelowOne_ReadsAsRoundOne(int round) =>
        Assert.Equal(SortBy.Colour, SortRule.For(round));

    /// <summary><b>Each player does the job once each way inside one match.</b> MatchRounds is 2
    /// and the roles swap at the reset edge, so this is the property the alternation exists for
    /// — and it is asserted against the tuning rather than against the literal 2, so raising
    /// MatchRounds turns this red instead of quietly making it false.</summary>
    [Fact]
    public void AcrossOneMatch_BothRulesAreUsed()
    {
        var t = HideSeekTuning.Current;
        var used = new HashSet<SortBy>();
        for (int round = 1; round <= t.MatchRoundsOrFloor; round++)
            used.Add(SortRule.For(round));
        Assert.Equal(2, used.Count);
    }

    // ==========================================================================================
    // SortRule.Matches -- every (rule, item, bin) combination
    // ==========================================================================================

    /// <summary>
    /// <b>All 54 combinations</b>: two rules x three colours x three shapes x three bins. Written
    /// as a sweep rather than as 54 InlineData rows because the ASSERTION is the interesting part
    /// — under Colour the shape must not affect the answer at all, and under Shape the colour
    /// must not, which is exactly the property a hand-written table of expected values would
    /// smuggle past.
    /// </summary>
    [Fact]
    public void Matches_IsTheOneAxisTheRuleNames_AndIgnoresTheOther()
    {
        int checkedCombinations = 0;
        foreach (SortColour colour in Colours)
        foreach (SortShape shape in Shapes)
        {
            var item = new SortItemFacts(colour, shape);
            for (int bin = 0; bin < SortRule.BinCount; bin++)
            {
                Assert.Equal((int)colour == bin, SortRule.Matches(SortBy.Colour, item, bin));
                Assert.Equal((int)shape == bin, SortRule.Matches(SortBy.Shape, item, bin));
                checkedCombinations += 2;
            }
        }

        Assert.Equal(54, checkedCombinations);
    }

    /// <summary>Exactly one bin takes each object under each rule — the property that makes
    /// "three bins" a complete answer rather than merely a plausible one.</summary>
    [Fact]
    public void Matches_ExactlyOneBinPerObjectPerRule()
    {
        foreach (SortColour colour in Colours)
        foreach (SortShape shape in Shapes)
        foreach (SortBy rule in new[] { SortBy.Colour, SortBy.Shape })
        {
            var item = new SortItemFacts(colour, shape);
            int hits = 0;
            for (int bin = 0; bin < SortRule.BinCount; bin++)
                if (SortRule.Matches(rule, item, bin))
                    hits++;
            Assert.Equal(1, hits);
        }
    }

    /// <summary><b>The mechanic, as an assertion.</b> A red ball and a red cube go together under
    /// Colour and apart under Shape; a red ball and a blue ball do the opposite. If this ever
    /// passes for both rules the game has stopped being the thing Talon asked for.</summary>
    [Fact]
    public void TheSameTwoObjects_GroupTogetherUnderOneRuleAndApartUnderTheOther()
    {
        var redBall = new SortItemFacts(SortColour.Red, SortShape.Ball);
        var redCube = new SortItemFacts(SortColour.Red, SortShape.Cube);
        var blueBall = new SortItemFacts(SortColour.Blue, SortShape.Ball);

        Assert.Equal(SortRule.BinFor(SortBy.Colour, redBall), SortRule.BinFor(SortBy.Colour, redCube));
        Assert.NotEqual(SortRule.BinFor(SortBy.Shape, redBall), SortRule.BinFor(SortBy.Shape, redCube));

        Assert.Equal(SortRule.BinFor(SortBy.Shape, redBall), SortRule.BinFor(SortBy.Shape, blueBall));
        Assert.NotEqual(SortRule.BinFor(SortBy.Colour, redBall), SortRule.BinFor(SortBy.Colour, blueBall));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void Matches_OutOfRangeBin_IsFalseRatherThanAThrow(int bin)
    {
        var item = new SortItemFacts(SortColour.Red, SortShape.Cube);
        Assert.False(SortRule.Matches(SortBy.Colour, item, bin));
        Assert.False(SortRule.Matches(SortBy.Shape, item, bin));
    }

    /// <summary><see cref="SortRule.BinFor"/> is the inverse of <see cref="SortRule.Matches"/>,
    /// which is the property that lets a fixture use it without becoming a second rule.</summary>
    [Fact]
    public void BinFor_AgreesWithMatches_Everywhere()
    {
        foreach (SortColour colour in Colours)
        foreach (SortShape shape in Shapes)
        foreach (SortBy rule in new[] { SortBy.Colour, SortBy.Shape })
        {
            var item = new SortItemFacts(colour, shape);
            Assert.True(SortRule.Matches(rule, item, SortRule.BinFor(rule, item)));
        }
    }

    /// <summary>The ordinals ARE the bin indices, and the bins are authored against them. A
    /// renumbering here silently repoints every bin in the task room, so it argues with a test
    /// rather than with a comment.</summary>
    [Fact]
    public void TheOrdinalsArePinnedBecauseTheyAreTheBinIndices()
    {
        Assert.Equal(0, (int)SortColour.Red);
        Assert.Equal(1, (int)SortColour.Blue);
        Assert.Equal(2, (int)SortColour.Yellow);
        Assert.Equal(0, (int)SortShape.Cube);
        Assert.Equal(1, (int)SortShape.Ball);
        Assert.Equal(2, (int)SortShape.Can);
        Assert.Equal(3, SortRule.BinCount);
        Assert.Equal(SortRule.BinCount, Enum.GetValues<SortColour>().Length);
        Assert.Equal(SortRule.BinCount, Enum.GetValues<SortShape>().Length);
    }

    // ==========================================================================================
    // SortTally -- the settle timer and the once-only count
    // ==========================================================================================

    private const float Tick = 1f / 60f;

    private static SortSighting Resting(int id, int bin, SortColour c, SortShape s) =>
        new(id, bin, new SortItemFacts(c, s), AtRest: true);

    private static SortSighting Moving(int id, int bin, SortColour c, SortShape s) =>
        new(id, bin, new SortItemFacts(c, s), AtRest: false);

    private static List<SortEvent> StepFor(SortTally tally, SortBy rule,
        IReadOnlyList<SortSighting> sightings, float seconds)
    {
        var all = new List<SortEvent>();
        int ticks = (int)MathF.Round(seconds / Tick);
        for (int i = 0; i < ticks; i++)
            all.AddRange(tally.Step(rule, sightings, Tick));
        return all;
    }

    [Fact]
    public void NothingCounts_BeforeTheSettleTimeHasElapsed()
    {
        var tally = new SortTally();
        var here = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };

        List<SortEvent> events = StepFor(tally, SortBy.Colour, here, SortTally.SettleSec - 0.1f);

        Assert.Empty(events);
        Assert.Equal(0, tally.Completed);
        Assert.Equal(1, tally.Settling);
    }

    [Fact]
    public void TheRightBin_CountsOnceTheSettleTimeHasElapsed()
    {
        var tally = new SortTally();
        var here = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };

        List<SortEvent> events = StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 0.1f);

        SortEvent e = Assert.Single(events);
        Assert.Equal(SortVerdict.Right, e.Verdict);
        Assert.Equal(1, e.PropId);
        Assert.Equal(0, e.BinSlot);
        Assert.Equal(1, tally.Completed);
        Assert.True(tally.HasCounted(1));
    }

    [Fact]
    public void TheWrongBin_BuzzesOnce_AndSubtractsNothing()
    {
        var tally = new SortTally();
        // A red cube in bin 1 (BLUE) on a colour round.
        var here = new[] { Resting(1, 1, SortColour.Red, SortShape.Cube) };

        List<SortEvent> events = StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 2.0f);

        SortEvent e = Assert.Single(events);
        Assert.Equal(SortVerdict.Wrong, e.Verdict);
        Assert.Equal(0, tally.Completed);
        Assert.False(tally.HasCounted(1));
    }

    /// <summary>The latch, and it is the difference between a buzz and an alarm: a misplaced
    /// object left where it is must not buzz sixty times a second for the rest of the round.
    /// Measured as a count over two whole seconds of sitting there.</summary>
    [Fact]
    public void AMisplacedObjectLeftWhereItIs_BuzzesExactlyOnce()
    {
        var tally = new SortTally();
        var here = new[] { Resting(7, 2, SortColour.Red, SortShape.Cube) };

        List<SortEvent> events = StepFor(tally, SortBy.Colour, here, 2.0f);

        Assert.Equal(1, events.Count(e => e.Verdict == SortVerdict.Wrong));
    }

    /// <summary>...and it RE-ARMS when the object is picked up again, so the player's second
    /// attempt gets an answer. The re-arm is the half that a latch usually forgets.</summary>
    [Fact]
    public void PickingItUpAndPuttingItBackWrong_BuzzesAgain()
    {
        var tally = new SortTally();
        var wrong = new[] { Resting(7, 2, SortColour.Red, SortShape.Cube) };
        var inHand = new[] { Moving(7, 2, SortColour.Red, SortShape.Cube) };

        List<SortEvent> first = StepFor(tally, SortBy.Colour, wrong, SortTally.SettleSec + 0.1f);
        StepFor(tally, SortBy.Colour, inHand, 0.5f);
        List<SortEvent> second = StepFor(tally, SortBy.Colour, wrong, SortTally.SettleSec + 0.1f);

        Assert.Equal(SortVerdict.Wrong, Assert.Single(first).Verdict);
        Assert.Equal(SortVerdict.Wrong, Assert.Single(second).Verdict);
        Assert.Equal(0, tally.Completed);
    }

    /// <summary><b>The once-only rule</b>, and the packet's own smoke assertion in miniature:
    /// taking a counted object out and putting it back in the same right bin adds nothing.</summary>
    [Fact]
    public void ACountedObject_PutBackInTheSameRightBin_DoesNotCountAgain()
    {
        var tally = new SortTally();
        var here = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };
        var inHand = new[] { Moving(1, 0, SortColour.Red, SortShape.Cube) };

        StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 0.1f);
        Assert.Equal(1, tally.Completed);

        StepFor(tally, SortBy.Colour, inHand, 0.5f);
        List<SortEvent> again = StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 0.5f);

        Assert.Empty(again);
        Assert.Equal(1, tally.Completed);
    }

    /// <summary>A counted object that is later moved to a WRONG bin says nothing either. It is
    /// done for the round: commenting on a decision that can no longer cost or earn the player
    /// anything is noise.</summary>
    [Fact]
    public void ACountedObject_MovedToAWrongBin_IsSilentAndCostsNothing()
    {
        var tally = new SortTally();
        var right = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };
        var gone = Array.Empty<SortSighting>();
        var wrong = new[] { Resting(1, 2, SortColour.Red, SortShape.Cube) };

        StepFor(tally, SortBy.Colour, right, SortTally.SettleSec + 0.1f);
        StepFor(tally, SortBy.Colour, gone, 0.3f);
        List<SortEvent> after = StepFor(tally, SortBy.Colour, wrong, SortTally.SettleSec + 0.5f);

        Assert.Empty(after);
        Assert.Equal(1, tally.Completed);
    }

    /// <summary>Taking a counted object OUT subtracts nothing. The count is a record of work
    /// done, not an inventory of what is currently in the bins.</summary>
    [Fact]
    public void RemovingACountedObject_SubtractsNothing()
    {
        var tally = new SortTally();
        var here = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };

        StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 0.1f);
        StepFor(tally, SortBy.Colour, Array.Empty<SortSighting>(), 3.0f);

        Assert.Equal(1, tally.Completed);
    }

    /// <summary>Held over the bin, or still falling into it, is not sorted. Without this the
    /// burst's shove would score the props it throws into a bin.</summary>
    [Fact]
    public void AnObjectThatIsNotAtRest_NeverCounts_HoweverLongItHovers()
    {
        var tally = new SortTally();
        var hovering = new[] { Moving(1, 0, SortColour.Red, SortShape.Cube) };

        List<SortEvent> events = StepFor(tally, SortBy.Colour, hovering, 5.0f);

        Assert.Empty(events);
        Assert.Equal(0, tally.Completed);
        Assert.Equal(0, tally.Settling);
    }

    /// <summary>Half a second in one bin plus half in another is not half a second anywhere.
    /// A player dragging an object along the row must not bank the bin they passed over.</summary>
    [Fact]
    public void MovingBinsMidSettle_RestartsTheClock()
    {
        var tally = new SortTally();
        var binOne = new[] { Resting(1, 1, SortColour.Red, SortShape.Cube) };
        var binZero = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };

        // 0.4 s in the wrong bin, then across to the right one: neither latch should have fired
        // at the moment of the move, and the right bin then needs its own full settle.
        List<SortEvent> drag = StepFor(tally, SortBy.Colour, binOne, 0.4f);
        Assert.Empty(drag);

        List<SortEvent> tooSoon = StepFor(tally, SortBy.Colour, binZero, 0.4f);
        Assert.Empty(tooSoon);

        List<SortEvent> landed = StepFor(tally, SortBy.Colour, binZero, 0.2f);
        Assert.Equal(SortVerdict.Right, Assert.Single(landed).Verdict);
    }

    /// <summary>Eighteen objects, all correct, all at once: the count is the count and the
    /// events are one apiece. The real room has exactly this many.</summary>
    [Fact]
    public void AWholeCrateSortedCorrectly_CountsEveryObjectOnce()
    {
        var tally = new SortTally();
        var all = new List<SortSighting>();
        int id = 0;
        foreach (SortColour colour in Colours)
        foreach (SortShape shape in Shapes)
        for (int copy = 0; copy < 2; copy++)
            all.Add(Resting(id++, (int)colour, colour, shape));

        List<SortEvent> events = StepFor(tally, SortBy.Colour, all, SortTally.SettleSec + 0.1f);

        Assert.Equal(18, all.Count);
        Assert.Equal(18, events.Count);
        Assert.All(events, e => Assert.Equal(SortVerdict.Right, e.Verdict));
        Assert.Equal(18, tally.Completed);
    }

    /// <summary>The same eighteen objects, in the same eighteen bins, on the SHAPE round: six
    /// are right and twelve are wrong. This is what the second round of a match looks like to a
    /// player running on the first round's habit, and it is the whole reason the rule
    /// alternates.</summary>
    [Fact]
    public void TheSameLayoutOnTheOtherRule_IsMostlyWrong()
    {
        var tally = new SortTally();
        var all = new List<SortSighting>();
        int id = 0;
        foreach (SortColour colour in Colours)
        foreach (SortShape shape in Shapes)
        for (int copy = 0; copy < 2; copy++)
            all.Add(Resting(id++, (int)colour, colour, shape));

        List<SortEvent> events = StepFor(tally, SortBy.Shape, all, SortTally.SettleSec + 0.1f);

        Assert.Equal(6, events.Count(e => e.Verdict == SortVerdict.Right));
        Assert.Equal(12, events.Count(e => e.Verdict == SortVerdict.Wrong));
        Assert.Equal(6, tally.Completed);
    }

    [Fact]
    public void Reset_ClearsTheCountAndTheSettleClocks()
    {
        var tally = new SortTally();
        var here = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };
        StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 0.1f);
        Assert.Equal(1, tally.Completed);

        tally.Reset();

        Assert.Equal(0, tally.Completed);
        Assert.False(tally.HasCounted(1));
        Assert.Equal(0, tally.Settling);

        // ...and the same object in the same bin scores again in the new round, which is what a
        // reset MEANS. A latch that survived the edge would make round 2 start at zero with
        // nothing scoreable in it.
        List<SortEvent> events = StepFor(tally, SortBy.Colour, here, SortTally.SettleSec + 0.1f);
        Assert.Equal(SortVerdict.Right, Assert.Single(events).Verdict);
    }

    /// <summary>dt hygiene, the same three cases <c>HideSeekLoop</c> pins: a paused window and a
    /// resumed one both produce these, and neither should hand somebody a free sort.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1f)]
    [InlineData(-0.0001f)]
    public void ANonsenseDelta_AdvancesNothing(float dt)
    {
        var tally = new SortTally();
        var here = new[] { Resting(1, 0, SortColour.Red, SortShape.Cube) };
        for (int i = 0; i < 1000; i++)
            Assert.Empty(tally.Step(SortBy.Colour, here, dt));
        Assert.Equal(0, tally.Completed);
    }

    [Fact]
    public void NoSightingsAtAll_IsAQuietNoOp()
    {
        var tally = new SortTally();
        Assert.Empty(tally.Step(SortBy.Colour, null, Tick));
        Assert.Empty(tally.Step(SortBy.Colour, Array.Empty<SortSighting>(), Tick));
        Assert.Equal(0, tally.Completed);
    }

    [Fact]
    public void ASightingInANonexistentBin_IsIgnored()
    {
        var tally = new SortTally();
        var bogus = new[] { Resting(1, 9, SortColour.Red, SortShape.Cube) };
        Assert.Empty(StepFor(tally, SortBy.Colour, bogus, 3.0f));
        Assert.Equal(0, tally.Completed);
    }

    // ==========================================================================================
    // The copy
    // ==========================================================================================

    [Theory]
    [InlineData(SortBy.Colour, "COLOUR")]
    [InlineData(SortBy.Shape, "SHAPE")]
    public void SortRuleWord_IsOneWord(SortBy rule, string want) =>
        Assert.Equal(want, HideSeekText.SortRuleWord(rule));

    [Fact]
    public void SortRuleLine_IsThePhraseTheStripUses()
    {
        Assert.Equal("BY COLOUR", HideSeekText.SortRuleLine(SortBy.Colour));
        Assert.Equal("BY SHAPE", HideSeekText.SortRuleLine(SortBy.Shape));
    }

    [Theory]
    [InlineData(SortBy.Colour, 0, "RED")]
    [InlineData(SortBy.Colour, 1, "BLUE")]
    [InlineData(SortBy.Colour, 2, "YELLOW")]
    [InlineData(SortBy.Shape, 0, "CUBE")]
    [InlineData(SortBy.Shape, 1, "BALL")]
    [InlineData(SortBy.Shape, 2, "CAN")]
    public void BinPlateWord_NamesWhatTheBinWantsThisRound(SortBy rule, int bin, string want) =>
        Assert.Equal(want, HideSeekText.BinPlateWord(rule, bin));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void BinPlateWord_OutOfRange_IsVisiblyWrongRatherThanAThrow(int bin)
    {
        Assert.Equal("?", HideSeekText.BinPlateWord(SortBy.Colour, bin));
        Assert.Equal("?", HideSeekText.BinPlateWord(SortBy.Shape, bin));
    }

    /// <summary>Every plate word is short enough for the plate. The bin's plate is narrower than
    /// the clock's panel and YELLOW is the longest word either rule can produce; this pins that
    /// the copy cannot grow past what the plate was sized for without arguing with a test
    /// first — CLOCK-1's §3.3 lesson, applied one wall over.</summary>
    [Fact]
    public void EveryPlateWord_FitsThePlate()
    {
        foreach (SortBy rule in new[] { SortBy.Colour, SortBy.Shape })
        for (int bin = 0; bin < SortRule.BinCount; bin++)
            Assert.True(HideSeekText.BinPlateWord(rule, bin).Length <= 6,
                $"{HideSeekText.BinPlateWord(rule, bin)} is wider than the plate was sized for");
    }

    // --- the strip suffix ---------------------------------------------------------------------

    private static HideSeekView ViewFor(HideSeekPhase phase, int round, int hider, int seeker,
        int sorts) =>
        new(phase, round, RemainingSec: 100f, HiderPeerId: hider, SeekerPeerId: seeker,
            Scores: ImmutableDictionary<int, int>.Empty, Refusal: HideSeekRefusal.None,
            SortsCompleted: sorts, FoundTick: HideSeekWire.NoFoundTick, LastTally: null);

    [Fact]
    public void SortLine_TellsTheHiderTheCountAndTheRule()
    {
        HideSeekView v = ViewFor(HideSeekPhase.Seeking, round: 1, hider: 11, seeker: 22, sorts: 7);
        Assert.Equal("SORTED 7 · BY COLOUR", HideSeekText.SortLine(v, 11));
    }

    [Fact]
    public void SortLine_FollowsTheRuleOfTheRound()
    {
        HideSeekView v = ViewFor(HideSeekPhase.Seeking, round: 2, hider: 11, seeker: 22, sorts: 3);
        Assert.Equal("SORTED 3 · BY SHAPE", HideSeekText.SortLine(v, 11));
    }

    /// <summary>The seeker never sees it. Program §2: neither player can see the other's
    /// progress, and the seeker knowing how the hider's job is going is exactly that.</summary>
    [Fact]
    public void SortLine_IsEmptyForTheSeekerAndForASpectator()
    {
        HideSeekView v = ViewFor(HideSeekPhase.Seeking, round: 1, hider: 11, seeker: 22, sorts: 7);
        Assert.Equal(string.Empty, HideSeekText.SortLine(v, 22));
        Assert.Equal(string.Empty, HideSeekText.SortLine(v, 33));
        Assert.Equal(string.Empty, HideSeekText.SortLine(v, 0));
    }

    [Theory]
    [InlineData(HideSeekPhase.Holding)]
    [InlineData(HideSeekPhase.Hiding)]
    [InlineData(HideSeekPhase.Tally)]
    public void SortLine_IsEmptyOutsideTheJob(HideSeekPhase phase)
    {
        HideSeekView v = ViewFor(phase, round: 1, hider: 11, seeker: 22, sorts: 7);
        Assert.Equal(string.Empty, HideSeekText.SortLine(v, 11));
    }

    /// <summary>Together keeps it: the number is frozen and it is what the round was worth, read
    /// in the three seconds after the door while the seeker is walking in.</summary>
    [Fact]
    public void SortLine_SurvivesIntoTogether()
    {
        HideSeekView v = ViewFor(HideSeekPhase.Together, round: 1, hider: 11, seeker: 22, sorts: 5);
        Assert.Equal("SORTED 5 · BY COLOUR", HideSeekText.SortLine(v, 11));
    }

    [Fact]
    public void SortLine_NeverPrintsANegativeCount()
    {
        HideSeekView v = ViewFor(HideSeekPhase.Seeking, round: 1, hider: 11, seeker: 22, sorts: -4);
        Assert.Equal("SORTED 0 · BY COLOUR", HideSeekText.SortLine(v, 11));
    }

    /// <summary><b>It goes through MATCH-1's chooser</b>, which is the whole reason it is in
    /// <c>HideSeekText</c> and not in the widget: the strip is assembled in exactly one
    /// place.</summary>
    [Fact]
    public void TheStrip_CarriesTheSortLineForTheHider()
    {
        HideSeekView v = ViewFor(HideSeekPhase.Seeking, round: 1, hider: 11, seeker: 22, sorts: 7);
        Assert.Equal("SEEKING · 1:40 · YOU HIDE · ROUND 1 · SORTED 7 · BY COLOUR",
            HideSeekText.StripLine(v, 11, null, HideSeekTuning.Current));
        Assert.Equal("SEEKING · 1:40 · YOU SEEK · ROUND 1",
            HideSeekText.StripLine(v, 22, null, HideSeekTuning.Current));
    }

    // ==========================================================================================
    // The two new Sfx members
    // ==========================================================================================

    public static IEnumerable<object[]> NewSfx() => new[]
    {
        new object[] { Sfx.SortGood },
        new object[] { Sfx.SortBad },
    };

    private static float[] Pcm(Sfx kind) => kind switch
    {
        Sfx.SortGood => SfxLab.SortGoodPcm(),
        Sfx.SortBad => SfxLab.SortBadPcm(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_IsFinite(Sfx kind) =>
        Assert.All(Pcm(kind), s => Assert.True(float.IsFinite(s),
            $"{kind} rendered a non-finite sample"));

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_IsAudible(Sfx kind)
    {
        float[] pcm = Pcm(kind);
        Assert.True(pcm.Length > 0, $"{kind} rendered nothing at all");
        float peak = pcm.Max(MathF.Abs);
        Assert.True(peak > 0.05f, $"{kind} peaks at {peak:0.####} — that is silence, not a sound");
        double rms = Math.Sqrt(pcm.Select(s => (double)s * s).Sum() / pcm.Length);
        Assert.True(rms > 0.01, $"{kind} has RMS {rms:0.####} — a click, not the sound described");
    }

    /// <summary><c>SfxLab.Render</c> CLAMPS, so a recipe that overshot flattens rather than blows
    /// up — audible as a crunch and invisible to every other check here. SFX-1's own first run
    /// found three recipes hard against the rail this way.</summary>
    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_StaysUnderZeroDbfs(Sfx kind)
    {
        float peak = Pcm(kind).Max(MathF.Abs);
        Assert.True(peak < 1.0f, $"{kind} peaks at {peak:0.####} — the clamp is flattening it");
    }

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_StartsAndEndsNearSilence(Sfx kind)
    {
        float[] pcm = Pcm(kind);
        Assert.True(MathF.Abs(pcm[0]) < 0.05f, $"{kind} starts at {pcm[0]:0.###} — that clicks");
        Assert.True(MathF.Abs(pcm[^1]) < 0.05f, $"{kind} ends at {pcm[^1]:0.###} — that clicks");
    }

    /// <summary>Both fire per OBJECT rather than per round, so they have to be short: a hider
    /// sorting well hears the first of these every few seconds for three minutes.</summary>
    [Fact]
    public void BothCuesAreShorterThanTheRoundCuesTheySitBeside()
    {
        double good = SfxLab.SortGoodPcm().Length / (double)SfxLab.SampleRateHz;
        double bad = SfxLab.SortBadPcm().Length / (double)SfxLab.SampleRateHz;
        double note = SfxLab.NotePcm().Length / (double)SfxLab.SampleRateHz;
        double buzz = SfxLab.RoundBuzzPcm().Length / (double)SfxLab.SampleRateHz;
        Assert.True(good < note, $"SortGood {good:0.###}s is not shorter than Note {note:0.###}s");
        Assert.True(bad < buzz, $"SortBad {bad:0.###}s is not shorter than RoundBuzz {buzz:0.###}s");
    }

    /// <summary>The two are a PAIR and must be distinguishable by ear, not by attention. The
    /// cheapest measurable half of that is the spectral centre: the good tick lives around a
    /// kilohertz and the correction around two hundred hertz, so a player facing away still
    /// knows which one fired.</summary>
    [Fact]
    public void TheGoodTickIsBrightAndTheCorrectionIsDull()
    {
        Assert.True(ZeroCrossingRate(SfxLab.SortGoodPcm())
                    > 3.0 * ZeroCrossingRate(SfxLab.SortBadPcm()),
            "SortGood and SortBad are too close in pitch to tell apart without looking");
    }

    private static double ZeroCrossingRate(float[] pcm)
    {
        int crossings = 0;
        for (int i = 1; i < pcm.Length; i++)
            if ((pcm[i - 1] < 0f) != (pcm[i] < 0f))
                crossings++;
        return crossings / (pcm.Length / (double)SfxLab.SampleRateHz);
    }

    /// <summary>
    /// <b>The ordinals, pinned, and the reserved gap left alone.</b> <c>EventResponse.Sound</c>
    /// serialises this enum as an int in every <c>prop_presentation.tres</c>, so a member that
    /// moved would silently re-point a profile at a different sound.
    ///
    /// <para>43–45 were BTN-1's reservation: TASK-1 was branched off INT-0B's tip and could not
    /// see BTN-1's edit, so it started at 46 and both merges were appends. That is the same
    /// arrangement that made DOOR-1's, SFX-1's and CLOCK-1's three blind enum edits merge for
    /// free at INT-0B, and it worked: nothing was renumbered.</para>
    ///
    /// <para><b>AMENDED AT INT-1 (2026-09-19), and this is the point of the amendment.</b>
    /// TASK-1 asserted 43, 44 and 45 were ABSENT, which was a statement that BTN-1 had not landed
    /// yet — a correct assertion whose subject has now ARRIVED through HOLD-1's branch. Each is
    /// replaced by the fact its absence was standing in for: <b>43 IS <c>Sfx.Buzzer</c></b>, and
    /// 44–45 are the free remainder. Nothing was changed to make a test pass; what changed is
    /// that the reservation was honoured, which is exactly what this test was watching for.
    /// (INT-0B §5 repaired two assertions of precisely this shape at the previous merge.)</para>
    /// </summary>
    [Fact]
    public void TheNewOrdinalsStartAt46_AndBtn1sReservationWasHonoured()
    {
        Assert.Equal(46, (int)Sfx.SortGood);
        Assert.Equal(47, (int)Sfx.SortBad);

        int[] taken = Enum.GetValues<Sfx>().Select(v => (int)v).ToArray();

        // 43 is what the gap was FOR, and it is BTN-1's bin-rejection buzzer.
        Assert.Equal(43, (int)Sfx.Buzzer);
        // 44 and 45 are still free. Asserted so that a lane appending at 48 while 44 sits empty
        // is a deliberate choice rather than an oversight, and so that anything that DOES take
        // one has to come through this test and say so.
        Assert.DoesNotContain(44, taken);
        Assert.DoesNotContain(45, taken);

        // One list, every ordinal once -- the assertion INT-0B added after the three-lane merge.
        Assert.Equal(taken.Length, taken.Distinct().Count());

        // And the round's own cues did not move underneath this append.
        Assert.Equal(38, (int)Sfx.Note);
        Assert.Equal(39, (int)Sfx.RoundBuzz);
        Assert.Equal(42, (int)Sfx.ResetWhoosh);
    }

    /// <summary>A match sentence still wins outright. MATCH-1's line already carries "3 sorted"
    /// and appending a second count to it would be the strip saying the same number twice in two
    /// different ways.</summary>
    [Fact]
    public void AMatchSentence_IsNotSuffixed()
    {
        var card = new HideSeekTally(RoundIndex: 1, HiderPeerId: 11, HiderGained: 3,
            SeekerPeerId: 22, SeekerGained: 40, EndedByDisconnect: false,
            MatchOver: true, MatchIndex: 1, WinnerPeerId: 11, HiderTotal: 3, SeekerTotal: 2);
        var v = new HideSeekView(HideSeekPhase.Tally, 1, 5f, 11, 22,
            ImmutableDictionary<int, int>.Empty, HideSeekRefusal.None, 3,
            HideSeekWire.NoFoundTick, card);

        string line = HideSeekText.StripLine(v, 11, null, HideSeekTuning.Current);
        Assert.StartsWith("MATCH 1", line);
        Assert.DoesNotContain("SORTED", line);
    }
}
