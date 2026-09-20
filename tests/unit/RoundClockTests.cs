using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MpFoundation.Game.Round;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>CLOCK-1 (2026-09-19): the wall clock's copy, the round's cue derivation, and the seven new
/// SFX recipes — all without an engine.</b>
///
/// <para>The split between this file and <c>tests/Run-RoundClockTest.ps1</c> is the same split
/// ROUND-1 drew and it is worth restating: everything here is a RULE and is therefore a pure
/// function, and nothing here is repeated in the scene suite. What the smoke exists for is the
/// half a pure function cannot reach — that three separate processes, each holding its own folded
/// view, paint the same second on three walls at the same instant.</para>
/// </summary>
public class RoundClockTests
{
    // =====================================================================================
    // 1. ClockLine — the second line on the wall, every phase
    // =====================================================================================

    private static HideSeekTally Card(int hid = 3, int seek = 48, bool disconnect = false) =>
        new(RoundIndex: 1, HiderPeerId: 11, HiderGained: hid, SeekerPeerId: 22,
            SeekerGained: seek, EndedByDisconnect: disconnect);

    [Fact]
    public void ClockLine_HasAnAnswerForEveryPhase()
    {
        // Not "does not throw": an unhandled phase would fall through to the default arm and
        // return empty, which is a legitimate answer for Holding and a blank wall for the rest.
        // So the phases that MUST say something are named.
        foreach (HideSeekPhase phase in Enum.GetValues<HideSeekPhase>())
        {
            string line = HideSeekText.ClockLine(phase, 30f, 3, Card());
            if (phase == HideSeekPhase.Holding)
                Assert.Equal(string.Empty, line);
            else
                Assert.False(string.IsNullOrWhiteSpace(line), $"{phase} leaves the clock blank");
        }
    }

    [Fact]
    public void ClockLine_IsBlankInHolding_BecauseAFrozenZeroReadsAsExpired()
    {
        Assert.Equal(string.Empty, HideSeekText.ClockLine(HideSeekPhase.Holding, 0f, 0, null));
        // ...and stays blank even if the wire somehow carried a clock for a phase with none.
        Assert.Equal(string.Empty, HideSeekText.ClockLine(HideSeekPhase.Holding, 12f, 0, Card()));
    }

    [Theory]
    [InlineData(HideSeekPhase.Hiding)]
    [InlineData(HideSeekPhase.Seeking)]
    public void ClockLine_IsExactlyTheStripsTimer_InTheTwoPhasesThatHaveAClock(HideSeekPhase phase)
    {
        // THE POINT OF THE WHOLE PACKET. Two readouts of one number: if this ever stops being
        // literally TimerText, the clock and the strip can disagree by a second and a player
        // watching both sees the game glitch.
        foreach (float sec in new[] { 0f, 0.99f, 1f, 9.4f, 59.9f, 60f, 180f })
            Assert.Equal(HideSeekText.TimerText(sec), HideSeekText.ClockLine(phase, sec, 4, Card()));
    }

    [Fact]
    public void ClockLine_ShowsWhatTheHiderGotDone_InTogether()
    {
        Assert.Equal("3 SORTED", HideSeekText.ClockLine(HideSeekPhase.Together, 0f, 3, null));
        Assert.Equal("0 SORTED", HideSeekText.ClockLine(HideSeekPhase.Together, 0f, 0, null));
        // Negative is not reachable from the loop (it clamps) but the wire is a ushort and this
        // is a display: clamping here too costs nothing and "-1 SORTED" on a wall is a bug report.
        Assert.Equal("0 SORTED", HideSeekText.ClockLine(HideSeekPhase.Together, 0f, -4, null));
    }

    [Fact]
    public void ClockLine_ShowsTheCard_InTally()
    {
        Assert.Equal("HID 3 · SEEK 48",
            HideSeekText.ClockLine(HideSeekPhase.Tally, 6f, 3, Card()));
    }

    [Fact]
    public void ClockLine_SaysAWordRatherThanTwoZeroes_WhenTheRoundEndedOnADisconnect()
    {
        // Two zeroes look like two people who tried. HideSeekTally carries the distinction
        // precisely so a readout does not have to guess at it.
        Assert.Equal("ENDED EARLY",
            HideSeekText.ClockLine(HideSeekPhase.Tally, 6f, 0, Card(0, 0, disconnect: true)));
    }

    [Fact]
    public void ClockLine_IsBlankInTally_BeforeThereIsACardAtAll()
    {
        Assert.Equal(string.Empty, HideSeekText.ClockLine(HideSeekPhase.Tally, 6f, 0, null));
    }

    [Fact]
    public void ClockLine_NeverExceedsWhatTheFitCanRescue()
    {
        // The panel is sized for ReferenceChars and FitPixelSize refuses to shrink past
        // MinScale, so a line longer than this is drawn OUTSIDE the panel no matter what. This
        // is the bound that makes the copy choices above load-bearing rather than taste.
        //
        // INT-0B (2026-09-19, packet ruling 3): the match-aware overload's arms are bounded
        // HERE too, and that is the whole reason the clock renders its own short forms instead
        // of MATCH-1's sentence. The names below are the WORST case the resolver can produce --
        // no display name at all, so PlayerName falls back to "PLAYER <peerId>" -- because a
        // name that has not replicated yet is exactly when a readout is most wanted.
        int worst = (int)(RoundClockLayout.ReferenceChars / RoundClockLayout.MinScale);
        var lines = new List<string>
        {
            HideSeekText.ClockLine(HideSeekPhase.Hiding, 3599f, 0, null),
            HideSeekText.ClockLine(HideSeekPhase.Together, 0f, 255, null),
            HideSeekText.ClockLine(HideSeekPhase.Tally, 6f, 0, Card(255, 255)),
            HideSeekText.ClockLine(HideSeekPhase.Tally, 6f, 0, Card(0, 0, disconnect: true)),

            // Every arm of the match-aware overload, with unresolved names.
            Clock(HideSeekPhase.Tally, MatchCard(winner: Seeker), NoNames, Both),
            Clock(HideSeekPhase.Tally, MatchCard(winner: 0), NoNames, Both),
            Clock(HideSeekPhase.Tally, MatchCard(disconnect: true), NoNames, HiderOnly),
            Clock(HideSeekPhase.Tally, RoundCard(255, 255), NoNames, Both),
            Clock(HideSeekPhase.Tally, RoundCard(disconnect: true), NoNames, HiderOnly),
            Clock(HideSeekPhase.Holding, MatchCard(winner: Seeker), NoNames, Both),
            Clock(HideSeekPhase.Holding, MatchCard(winner: 0), NoNames, Both),
            Clock(HideSeekPhase.Together, MatchCard(winner: Seeker), NoNames, Both),
            Clock(HideSeekPhase.Seeking, MatchCard(winner: Seeker), NoNames, Both),
        };
        foreach (string line in lines)
            Assert.True(line.Length <= worst,
                $"\"{line}\" is {line.Length} chars; the panel can only rescue {worst}");
    }

    // =====================================================================================
    // 1b. ClockLine(view, nameOf, tuning) — the match-aware overload (INT-0B, ruling 3)
    //
    // The KIND of sentence is MATCH-1's chooser's, so the wall and the strip cannot disagree
    // about whether a MATCH ended. The WORDS are the clock's own, because MATCH-1's sentence is
    // 57 characters against a panel that holds seven — see the overload's doc.
    // =====================================================================================

    private const int Hider = 11;
    private const int Seeker = 22;
    private static readonly int[] Both = { Hider, Seeker };
    private static readonly int[] HiderOnly = { Hider };

    /// <summary>The worst case the name resolver can give: nothing, so PlayerName falls back.</summary>
    private static readonly Func<int, string>? NoNames = null;

    private static string Names(int id) =>
        id == Hider ? "ADA" : id == Seeker ? "BEN" : string.Empty;

    private static HideSeekTally RoundCard(int hid = 3, int seek = 168, bool disconnect = false) =>
        new(RoundIndex: 1, HiderPeerId: Hider, HiderGained: hid, SeekerPeerId: Seeker,
            SeekerGained: seek, EndedByDisconnect: disconnect, MatchOver: false, MatchIndex: 1,
            WinnerPeerId: 0, HiderTotal: hid, SeekerTotal: seek);

    private static HideSeekTally MatchCard(int winner = Seeker, bool disconnect = false) =>
        new(RoundIndex: 2, HiderPeerId: Seeker, HiderGained: 1, SeekerPeerId: Hider,
            SeekerGained: 168, EndedByDisconnect: disconnect, MatchOver: true, MatchIndex: 1,
            WinnerPeerId: winner, HiderTotal: 173, SeekerTotal: 171);

    private static string Clock(HideSeekPhase phase, HideSeekTally? card,
        Func<int, string>? nameOf, int[] present, float remainingSec = 0f, int towers = 3)
    {
        ImmutableDictionary<int, int> scores = ImmutableDictionary<int, int>.Empty;
        foreach (int p in present)
            scores = scores.SetItem(p, 0);
        var view = new HideSeekView(phase, 2, remainingSec, Hider, Seeker, scores,
            HideSeekRefusal.None, towers, HideSeekWire.NoFoundTick, card);
        return HideSeekText.ClockLine(view, nameOf, HideSeekTuning.Default);
    }

    [Fact]
    public void ClockLine_SaysWhoWonTheMatch_WhereItUsedToSayARoundCard()
    {
        // THE DEFECT THIS CLOSES, in one assertion: before the match-aware overload the strip
        // read "MATCH 1 · BEN WINS 173–171" and the wall read "HID 1 · SEEK 168" at the same
        // instant — two readouts of one card disagreeing about whether a MATCH had ended.
        Assert.Equal("BEN WINS", Clock(HideSeekPhase.Tally, MatchCard(), Names, Both));
        Assert.Equal("DRAW", Clock(HideSeekPhase.Tally, MatchCard(winner: 0), Names, Both));
    }

    [Fact]
    public void ClockLine_LeavesAMidMatchTallyExactlyAsClock1ShippedIt()
    {
        // The other half of the chooser: a round that is NOT a match end still says the card's
        // two numbers. CLOCK-1's copy, unchanged, and the packet is explicit that it stays.
        Assert.Equal("HID 3 · SEEK 168", Clock(HideSeekPhase.Tally, RoundCard(), Names, Both));
    }

    [Fact]
    public void ClockLine_CarriesTheResultIntoHolding_AndIsBlankWhenThereIsNoneToCarry()
    {
        Assert.Equal("BEN WON", Clock(HideSeekPhase.Holding, MatchCard(), Names, Both));
        Assert.Equal("DRAW", Clock(HideSeekPhase.Holding, MatchCard(winner: 0), Names, Both));

        // Holding after an ORDINARY round, and Holding with no card at all: blank, as before.
        Assert.Equal(string.Empty, Clock(HideSeekPhase.Holding, RoundCard(), Names, Both));
        Assert.Equal(string.Empty, Clock(HideSeekPhase.Holding, null, Names, Both));
    }

    [Fact]
    public void ClockLine_NamesTheLeaver_AndFallsBackToClock1sWordWhenItCannot()
    {
        // The leaver is recovered off the score map exactly as MatchLine recovers it: the card's
        // role holder with no row is the one who is gone. Only when exactly one is missing.
        Assert.Equal("ADA LEFT",
            Clock(HideSeekPhase.Tally, RoundCard(disconnect: true), Names, new[] { Seeker }));
        Assert.Equal("BEN LEFT",
            Clock(HideSeekPhase.Tally, MatchCard(disconnect: true), Names, new[] { Hider }));

        // Both still present, or both gone: naming nobody is worse than a word.
        Assert.Equal("ENDED EARLY",
            Clock(HideSeekPhase.Tally, RoundCard(disconnect: true), Names, Both));
        Assert.Equal("ENDED EARLY",
            Clock(HideSeekPhase.Tally, MatchCard(disconnect: true), Names, Array.Empty<int>()));
    }

    [Fact]
    public void ClockLine_HoldingKeepsTheResultThroughADisconnect_AsMatchLineDoes()
    {
        // MATCH-1's own rule for this phase (MatchHoldingLine): the match is over with the
        // totals as they stand, and "someone left" tells the survivor nothing they do not know
        // while hiding the score still on the board. The wall must not disagree with the strip
        // about that either.
        Assert.Equal("BEN WON",
            Clock(HideSeekPhase.Holding, MatchCard(disconnect: true), Names, new[] { Hider }));
    }

    [Theory]
    [InlineData(HideSeekPhase.Hiding)]
    [InlineData(HideSeekPhase.Seeking)]
    [InlineData(HideSeekPhase.Together)]
    public void ClockLine_IsTheOldOverloadEverywhereTheChooserHasNothingToSay(HideSeekPhase phase)
    {
        // Byte for byte, even with a match-over card sitting in the view — the chooser speaks in
        // Tally and in Holding and nowhere else, and a clock that showed a result mid-round
        // would be announcing the LAST match over the top of this one.
        string expected = HideSeekText.ClockLine(phase, 31f, 3, MatchCard());
        Assert.Equal(expected, Clock(phase, MatchCard(), Names, Both, remainingSec: 31f));
    }

    [Fact]
    public void ClockLine_TruncatesALongNameRatherThanOverflowingThePanel()
    {
        // Six characters, and the strip beside the player still carries the full name. The
        // fallback is the worst case and is what an unreplicated name produces.
        Assert.Equal("LONGER WINS",
            Clock(HideSeekPhase.Tally, MatchCard(), _ => "LONGERTHANTHEPANEL", Both));
        Assert.Equal($"PLAYER WINS", Clock(HideSeekPhase.Tally, MatchCard(), NoNames, Both));
    }

    [Fact]
    public void ClockLine_AndTheStrip_AlwaysAgreeAboutWhetherAMatchEnded()
    {
        // The property the whole overload exists for, stated as one: wherever MATCH-1's strip
        // says a MATCH ended, the wall says so too, and wherever it does not, the wall does not.
        // Asserted over every phase and both card kinds rather than at the two points above.
        foreach (HideSeekPhase phase in Enum.GetValues<HideSeekPhase>())
        {
            foreach (HideSeekTally card in new[] { RoundCard(), MatchCard() })
            {
                ImmutableDictionary<int, int> scores = ImmutableDictionary<int, int>.Empty
                    .SetItem(Hider, 0).SetItem(Seeker, 0);
                var view = new HideSeekView(phase, 2, 0f, Hider, Seeker, scores,
                    HideSeekRefusal.None, 3, HideSeekWire.NoFoundTick, card);

                string strip = HideSeekText.StripLine(view, Hider, Names, HideSeekTuning.Default);
                string wall = HideSeekText.ClockLine(view, Names, HideSeekTuning.Default);

                bool stripSaysMatchEnded = strip.Contains("WINS") || strip.Contains("WON")
                    || strip.Contains("DRAW");
                bool wallSaysMatchEnded = wall.Contains("WINS") || wall.Contains("WON")
                    || wall.Contains("DRAW");
                Assert.True(stripSaysMatchEnded == wallSaysMatchEnded,
                    $"{phase}, matchOver={card.MatchOver}: strip \"{strip}\" and wall \"{wall}\" "
                    + "disagree about whether a match ended");
            }
        }
    }

    // =====================================================================================
    // 2. RoundClockLayout — the arithmetic that keeps a long line inside the panel
    // =====================================================================================

    [Fact]
    public void FitPixelSize_LeavesAShortLineAtTheAuthoredSize()
    {
        Assert.Equal(0.005f, RoundClockLayout.FitPixelSize(0.005f, 4));
        Assert.Equal(0.005f, RoundClockLayout.FitPixelSize(0.005f, RoundClockLayout.ReferenceChars));
        // And never GROWS one: the panel is fixed and the two labels must stay in proportion.
        Assert.Equal(0.005f, RoundClockLayout.FitPixelSize(0.005f, 1));
        Assert.Equal(0.005f, RoundClockLayout.FitPixelSize(0.005f, 0));
    }

    [Fact]
    public void FitPixelSize_ShrinksInProportion_AndStopsAtTheFloor()
    {
        // Derived from the constant, not from a copy of it: the reference width is a fact about
        // the authored panel and moves when the panel does, and a test that hard-coded the
        // resulting ratio would go red for the wrong reason the day somebody widened the clock.
        const int longer = RoundClockLayout.ReferenceChars * 2;
        Assert.True(MathF.Abs(0.005f * 0.5f - RoundClockLayout.FitPixelSize(0.005f, longer)) < 1e-7f);
        Assert.True(RoundClockLayout.FitPixelSize(0.005f, RoundClockLayout.ReferenceChars + 1)
                    < RoundClockLayout.FitPixelSize(0.005f, RoundClockLayout.ReferenceChars));
        // Past the floor it stops rather than vanishing: an unreadable line that looks fine is
        // worse than an overhang, because nothing about it looks broken.
        Assert.True(MathF.Abs(0.005f * RoundClockLayout.MinScale
            - RoundClockLayout.FitPixelSize(0.005f, 400)) < 1e-7f);
    }

    // =====================================================================================
    // 3. RoundAudioCues.ForEdge — every row of the packet's table, and the no-ops
    // =====================================================================================

    private static HideSeekView View(HideSeekPhase phase, float remaining = 0f, int round = 1,
        int sorts = 0, HideSeekTally? tally = null) =>
        new(phase, round, remaining, HiderPeerId: 11, SeekerPeerId: 22,
            Scores: ImmutableDictionary<int, int>.Empty, Refusal: HideSeekRefusal.None,
            SortsCompleted: sorts, FoundTick: HideSeekWire.NoFoundTick, LastTally: tally);

    private static RoundCue Only(IReadOnlyList<RoundCue> cues)
    {
        Assert.Single(cues);
        return cues[0];
    }

    [Fact]
    public void NoPreviousView_IsSilent_BecauseALateJoinerWitnessedNothing()
    {
        // The rule the driver's own PhaseChanged event follows. A client arriving mid-seek must
        // not be told the round just started, and this is the assertion that says so.
        foreach (HideSeekPhase phase in Enum.GetValues<HideSeekPhase>())
            Assert.Empty(RoundAudioCues.ForEdge(null, View(phase, 12f)));
    }

    [Fact]
    public void AnIdenticalMessage_IsSilent()
    {
        HideSeekView v = View(HideSeekPhase.Seeking, 100f);
        Assert.Empty(RoundAudioCues.ForEdge(v, v));
    }

    [Fact]
    public void TheClockMovingWithinASecond_IsSilent()
    {
        // The wire's fastest field is tenths, so this is the overwhelmingly common edge: ten
        // times a second, every second of a round, and it must cost nothing.
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 100.4f),
            View(HideSeekPhase.Seeking, 100.1f)));
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 25.9f),
            View(HideSeekPhase.Hiding, 25.1f)));
    }

    [Fact]
    public void Start_IsARisingChime_Flat()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(
            View(HideSeekPhase.Holding), View(HideSeekPhase.Hiding, 30f)));
        Assert.Equal(Sfx.ChimeUp, cue.Sound);
        Assert.True(cue.Flat);
        Assert.False(cue.Positional);
        Assert.Equal(0f, cue.PitchBias);
    }

    [Theory]
    [InlineData(HideSeekPhase.Hiding)]
    [InlineData(HideSeekPhase.Seeking)]
    public void TheLastTenSeconds_TickOnceEach_Positionally(HideSeekPhase phase)
    {
        var heard = new List<int>();
        float prev = 11.4f;
        // Walk the clock down in tenths, exactly as the wire does.
        for (float t = 11.3f; t >= -0.05f; t -= 0.1f)
        {
            IReadOnlyList<RoundCue> cues = RoundAudioCues.ForEdge(View(phase, prev), View(phase, t));
            foreach (RoundCue cue in cues)
            {
                Assert.Equal(Sfx.Tick, cue.Sound);
                Assert.True(cue.Positional, "the tick is the building counting; it has a direction");
                Assert.False(cue.Flat, "a flat tick would put the countdown inside the player's head");
                heard.Add((int)MathF.Floor(t < 0 ? 0 : t));
            }
            prev = t;
        }
        Assert.Equal(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, heard);
    }

    [Fact]
    public void NothingTicksAboveTen()
    {
        // 12 -> 11 is a second boundary and is deliberately SILENT; 12 -> 10 is not, because
        // ten is where the countdown starts being audible. Both directions are asserted, or this
        // would pass on a rule that never ticks at all.
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 12.05f),
            View(HideSeekPhase.Seeking, 11.05f)));
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 30.05f),
            View(HideSeekPhase.Hiding, 28.95f)));
        Assert.Equal(Sfx.Tick, Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 11.05f),
            View(HideSeekPhase.Seeking, 10.95f))).Sound);
    }

    [Fact]
    public void TheLastThreeSecondsRise_ExactlyAsThePacketLists()
    {
        Assert.Equal(0f, RoundAudioCues.TickPitchBias(3));
        Assert.Equal(0.05f, RoundAudioCues.TickPitchBias(2));
        Assert.Equal(0.10f, RoundAudioCues.TickPitchBias(1));
        Assert.Equal(0f, RoundAudioCues.TickPitchBias(10));
        // And the bias reaches the cue, not just the helper.
        RoundCue one = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 2.05f),
            View(HideSeekPhase.Hiding, 1.95f)));
        Assert.Equal(0.10f, one.PitchBias);
    }

    [Fact]
    public void AStalledClientHearsTheSecondItIsLookingAt_NotABurstOfTheOnesItSleptThrough()
    {
        // One candidate per edge, deliberately. A peer that missed eight messages gets ONE tick
        // for the second now on the wall, not eight ticks for seconds already gone.
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 9.9f),
            View(HideSeekPhase.Seeking, 2.4f)));
        Assert.Equal(Sfx.Tick, cue.Sound);
        Assert.Equal(0.05f, cue.PitchBias);   // the second it landed on, 2, not the ones it passed
    }

    [Fact]
    public void TheGrace_IsReadOffTheClockGoingUp_AndBuzzesShortOnBothLayers()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 0f),
            View(HideSeekPhase.Hiding, 10f)));
        Assert.Equal(Sfx.BuzzShort, cue.Sound);
        Assert.True(cue.Flat);
        Assert.True(cue.Positional);
    }

    [Fact]
    public void ARefusedConfirmDoesNotBuzz_WhichIsWhyTheGraceIsNotReadOffTheRefusal()
    {
        // The loop sets the SAME refusal reason on a refused Confirm as it does when it grants
        // the grace. Keying the buzzer on the reason would sound it every time the hider pressed
        // the button with the object still in their hands; the clock only ever goes up once.
        HideSeekView prev = View(HideSeekPhase.Hiding, 18.0f);
        var refused = new HideSeekView(HideSeekPhase.Hiding, 1, 17.9f, 11, 22,
            ImmutableDictionary<int, int>.Empty, HideSeekRefusal.PutTheObjectDownFirst, 0,
            HideSeekWire.NoFoundTick, null);
        Assert.Empty(RoundAudioCues.ForEdge(prev, refused));
    }

    [Fact]
    public void Confirm_IsOneNote_Flat()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 12.3f),
            View(HideSeekPhase.Seeking, 180f)));
        Assert.Equal(Sfx.Note, cue.Sound);
        Assert.True(cue.Flat);
        Assert.False(cue.Positional);
    }

    [Fact]
    public void TheHideBuzzer_IsTheSameEdgeWithAnExpiredClock_AndUsesBothLayers()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 0f),
            View(HideSeekPhase.Seeking, 180f)));
        Assert.Equal(Sfx.RoundBuzz, cue.Sound);
        Assert.True(cue.Flat);
        Assert.True(cue.Positional);
    }

    [Fact]
    public void TheBuzzerWindowIsOneWireTickWide_AndTheBoundaryIsStated()
    {
        // Documenting the ambiguity rather than pretending it is not there: at 10 Hz the last
        // Hiding message before the buzzer reads 0.0, so anything at or under one tick is the
        // buzzer and a Confirm inside that same tenth is misread. Both mean "the hide is over".
        Assert.Equal(Sfx.RoundBuzz, Only(RoundAudioCues.ForEdge(
            View(HideSeekPhase.Hiding, RoundAudioCues.BuzzerWindowSec),
            View(HideSeekPhase.Seeking, 180f))).Sound);
        Assert.Equal(Sfx.Note, Only(RoundAudioCues.ForEdge(
            View(HideSeekPhase.Hiding, RoundAudioCues.BuzzerWindowSec + 0.1f),
            View(HideSeekPhase.Seeking, 180f))).Sound);
    }

    [Fact]
    public void TheFind_IsSilent_BecauseDoor1OwnsTheBang()
    {
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 96f),
            View(HideSeekPhase.Together)));
    }

    [Fact]
    public void TheSeekTimeout_IsADoubleBuzz_OnBothLayers()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 0f),
            View(HideSeekPhase.Tally, 6f, tally: Card(2, 0))));
        Assert.Equal(Sfx.BuzzDouble, cue.Sound);
        Assert.True(cue.Flat);
        Assert.True(cue.Positional);
    }

    [Fact]
    public void AFailedHideAfterTheGrace_UsesTheSameTimeoutCue_RatherThanEndingInSilence()
    {
        // NOT a row in the packet's table. It is the same event as the seek timeout — a clock ran
        // out into a tally — and the alternative is the one ending in this game nobody hears.
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 0f),
            View(HideSeekPhase.Tally, 6f, tally: Card(0, 0))));
        Assert.Equal(Sfx.BuzzDouble, cue.Sound);
    }

    [Fact]
    public void TheEndPress_IsTheExistingTriumph_Flat()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Together),
            View(HideSeekPhase.Tally, 6f, tally: Card())));
        Assert.Equal(Sfx.Triumph, cue.Sound);
        Assert.True(cue.Flat);
        Assert.False(cue.Positional);
    }

    [Fact]
    public void ARoundThatEndedOnADisconnect_IsSilentFromEveryPhaseItCanEndIn()
    {
        // A triumph sting for a player staring at an empty room is the worst of the three, but
        // none of them is right, so the rule is on the CARD rather than on the edge.
        HideSeekView tally = View(HideSeekPhase.Tally, 6f, tally: Card(0, 0, disconnect: true));
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Hiding, 12f), tally));
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Seeking, 96f), tally));
        Assert.Empty(RoundAudioCues.ForEdge(View(HideSeekPhase.Together), tally));
    }

    [Fact]
    public void TheResetEdge_IsAWhoosh_Flat()
    {
        RoundCue cue = Only(RoundAudioCues.ForEdge(View(HideSeekPhase.Tally, 0f, tally: Card()),
            View(HideSeekPhase.Holding, 0f, round: 2, tally: Card())));
        Assert.Equal(Sfx.ResetWhoosh, cue.Sound);
        Assert.True(cue.Flat);
        Assert.False(cue.Positional);
    }

    [Fact]
    public void AWholeRoundProducesThePacketsSequence_AndNothingElse()
    {
        // The integration of every rule above, walked as one round the way the smoke walks it.
        var heard = new List<Sfx>();
        HideSeekView v = View(HideSeekPhase.Holding);

        void Step(HideSeekView next)
        {
            foreach (RoundCue cue in RoundAudioCues.ForEdge(v, next))
                heard.Add(cue.Sound);
            v = next;
        }

        // Stepped in exact tenths off an int, not by repeatedly subtracting 0.1f: the wire
        // carries tenths, and a float accumulation would make the tick COUNT an artefact of
        // rounding rather than a property of the rule.
        Step(View(HideSeekPhase.Hiding, 30f));                    // Start
        for (int tenths = 299; tenths >= 50; tenths--)            // the hide, ticking from 10
            Step(View(HideSeekPhase.Hiding, tenths / 10f));
        Step(View(HideSeekPhase.Seeking, 180f));                  // Confirm with 5.0 s left
        for (int tenths = 1799; tenths >= 1680; tenths--)         // some seeking, no ticks
            Step(View(HideSeekPhase.Seeking, tenths / 10f));
        Step(View(HideSeekPhase.Together));                       // the find — DOOR-1's
        Step(View(HideSeekPhase.Tally, 6f, tally: Card()));       // End
        Step(View(HideSeekPhase.Holding, 0f, round: 2, tally: Card()));  // the reset

        Assert.Equal(
            new[]
            {
                Sfx.ChimeUp,
                Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick,   // 10..5
                Sfx.Note, Sfx.Triumph, Sfx.ResetWhoosh,
            },
            heard);
    }

    // =====================================================================================
    // 4. The seven new recipes: non-silent, finite, under 0 dBFS
    // =====================================================================================

    public static IEnumerable<object[]> NewSfx() => new[]
    {
        new object[] { Sfx.ChimeUp },
        new object[] { Sfx.Tick },
        new object[] { Sfx.Note },
        new object[] { Sfx.RoundBuzz },
        new object[] { Sfx.BuzzShort },
        new object[] { Sfx.BuzzDouble },
        new object[] { Sfx.ResetWhoosh },
    };

    private static float[] Pcm(Sfx kind) => kind switch
    {
        Sfx.ChimeUp => SfxLab.ChimeUpPcm(),
        Sfx.Tick => SfxLab.TickPcm(),
        Sfx.Note => SfxLab.NotePcm(),
        Sfx.RoundBuzz => SfxLab.RoundBuzzPcm(),
        Sfx.BuzzShort => SfxLab.BuzzShortPcm(),
        Sfx.BuzzDouble => SfxLab.BuzzDoublePcm(),
        Sfx.ResetWhoosh => SfxLab.ResetWhooshPcm(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_IsFinite(Sfx kind)
    {
        float[] pcm = Pcm(kind);
        Assert.All(pcm, s => Assert.True(float.IsFinite(s), $"{kind} rendered a non-finite sample"));
    }

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_IsAudible(Sfx kind)
    {
        float[] pcm = Pcm(kind);
        Assert.True(pcm.Length > 0, $"{kind} rendered nothing at all");
        float peak = pcm.Max(MathF.Abs);
        // A recipe whose gain constant was fat-fingered to zero renders a buffer of the right
        // LENGTH full of silence, and every other check here passes on it.
        Assert.True(peak > 0.05f, $"{kind} peaks at {peak:0.####} — that is silence, not a sound");
        // And it has to be audible for most of its length, not just at one sample: a recipe whose
        // envelope collapsed would peak fine and be inaudible.
        double rms = Math.Sqrt(pcm.Select(s => (double)s * s).Sum() / pcm.Length);
        Assert.True(rms > 0.01, $"{kind} has RMS {rms:0.####} — a click, not the sound described");
    }

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_StaysUnderZeroDbfs(Sfx kind)
    {
        // SfxLab.Render CLAMPS to [-1, 1], so a recipe that overshot does not blow up — it
        // flattens, which is audible as a crunch and invisible to every other check in this file.
        // Strictly under 1.0 is therefore the assertion: touching the rail at all means the clamp
        // did work that a gain constant should have done.
        float peak = Pcm(kind).Max(MathF.Abs);
        Assert.True(peak < 1.0f, $"{kind} peaks at {peak:0.####} — the clamp is flattening it");
    }

    [Theory]
    [MemberData(nameof(NewSfx))]
    public void EveryNewRecipe_StartsAndEndsNearSilence(Sfx kind)
    {
        // A buffer that begins or ends at full deflection clicks, every time it plays, on top of
        // whatever it was supposed to sound like.
        float[] pcm = Pcm(kind);
        Assert.True(MathF.Abs(pcm[0]) < 0.05f, $"{kind} starts at {pcm[0]:0.###} — that clicks");
        Assert.True(MathF.Abs(pcm[^1]) < 0.05f, $"{kind} ends at {pcm[^1]:0.###} — that clicks");
    }

    [Fact]
    public void TheNewOrdinalsAreTheOnesTheOrchestratorHandedOut_AndTheGapIsLeftAlone()
    {
        // The .tres serialization contract (SfxLab's own header) plus a cross-lane one: DOOR-1
        // and SFX-1 branched from this same base and hold 24-35. A lane that compacted the gap
        // would silently re-point every presentation profile that referenced one of theirs.
        //
        // INT-0B (2026-09-19): those two lanes have now LANDED, so the assertion that 24-35 are
        // absent has done its job and is replaced by the assertion it was standing in for -- that
        // CLOCK-1's seven did not move when they arrived. The names are checked too, because the
        // other way this lane could have been damaged at the merge is a collapse onto SFX-1's
        // Whoosh (35) or the pre-fork Buzz (17), which the packet's ruling 1 forbids.
        Assert.Equal(36, (int)Sfx.ChimeUp);
        Assert.Equal(37, (int)Sfx.Tick);
        Assert.Equal(38, (int)Sfx.Note);
        Assert.Equal(39, (int)Sfx.RoundBuzz);
        Assert.Equal(40, (int)Sfx.BuzzShort);
        Assert.Equal(41, (int)Sfx.BuzzDouble);
        Assert.Equal(42, (int)Sfx.ResetWhoosh);

        // 24-35 are now DOOR-1's and SFX-1's, exactly as the gap reserved them for.
        Assert.Equal(24, (int)Sfx.Bang);
        Assert.Equal(25, (int)Sfx.TinPick);
        Assert.Equal(35, (int)Sfx.Whoosh);

        // One list, every ordinal once. This is what a collapsed or renumbered merge breaks.
        int[] taken = Enum.GetValues<Sfx>().Select(v => (int)v).ToArray();
        Assert.Equal(taken.Length, taken.Distinct().Count());

        // RoundBuzz and ResetWhoosh are NOT Buzz and Whoosh, and must not become them: they are
        // different sounds on different events (CLOCK-1's handoff, packet INT-0B ruling 1).
        Assert.NotEqual((int)Sfx.Buzz, (int)Sfx.RoundBuzz);
        Assert.NotEqual((int)Sfx.Whoosh, (int)Sfx.ResetWhoosh);

        // The pinned ordinals below are untouched — the whole reason the gap existed.
        Assert.Equal(17, (int)Sfx.Buzz);
        Assert.Equal(23, (int)Sfx.Triumph);
    }

    // =====================================================================================
    // 5. The cadence is the HUD's cadence
    // =====================================================================================

    [Fact]
    public void TheClockPollsAtTheHudsCadence()
    {
        // "The strip and the clock show the same second in the same frame" is only true if the
        // two are READ at the same rate. Two files that happen to say 0.1 would drift the day
        // somebody retuned one; this is the assertion that makes them one number.
        Assert.Equal(MpFoundation.Ui.Hud.GameHud.PollIntervalSec, RoundAudio.PollIntervalSec);
    }
}
