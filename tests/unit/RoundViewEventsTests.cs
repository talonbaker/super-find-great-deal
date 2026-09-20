using System.Collections.Immutable;
using MpFoundation.Game.Round;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// REVIEW-1 C1. <b>Which events one round message raises</b>, as a pure function of the view
/// before it and the view after it — the decision <c>HideSeekDriver.ApplyRound</c> used to make
/// inline, where it was unreachable from here.
///
/// <para><b>The defect these tests are written against.</b> The driver returned early whenever
/// the phase had not moved, so a message carrying a refusal and nothing else raised nothing at
/// all. The only remaining delivery was <c>RoundStripWidget</c> reading <c>View.Refusal</c> as a
/// LEVEL on <c>GameHud</c>'s 10 Hz poll — and the refusal is true for exactly one sim tick
/// (~16 ms at 60 Hz), so the sentence was lost about five times in six. The Hiding buzzer's
/// refusal has no other channel at all (<c>RoundButton.PlayPressResult</c> answers the PRESSER
/// only, and the buzzer has no press behind it), so the hider was silently handed ten extra
/// seconds with no explanation — the exact outcome <c>HideSeekTuning.HidingGraceSec</c>'s own doc
/// promises will not happen.</para>
///
/// <para><b>The positive control is the phase-change half</b>: if these tests only asserted that
/// a refusal is reported, a function that reported everything on every message would pass them
/// all. So every case also pins what must NOT be raised.</para>
/// </summary>
public class RoundViewEventsTests
{
    private static HideSeekView View(HideSeekPhase phase, HideSeekRefusal refusal = HideSeekRefusal.None,
        int foundTick = -1) =>
        new(phase, 1, 0f, 10, 20, ImmutableDictionary<int, int>.Empty, refusal, 0, foundTick, null);

    // --- the defect itself --------------------------------------------------------------------

    [Fact]
    public void ARefusalOnAMessageThatDoesNotMoveThePhaseIsStillReported()
    {
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Hiding),
            View(HideSeekPhase.Hiding, HideSeekRefusal.PutTheObjectDownFirst));

        Assert.Equal(HideSeekRefusal.PutTheObjectDownFirst, edges.Refusal);
        Assert.False(edges.PhaseChanged);
        Assert.False(edges.Found);
        Assert.False(edges.ResetRequested);
    }

    [Fact]
    public void TheHidingBuzzersRefusalIsReportedAlthoughTheClockOnlyExtended()
    {
        // HideSeekLoop.Step's Hiding branch: the buzzer fires with the target still in the
        // hider's hands, the clock is extended by HidingGraceSec and the phase stays Hiding.
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Hiding),
            View(HideSeekPhase.Hiding, HideSeekRefusal.NobodyCouldReachThat));

        Assert.Equal(HideSeekRefusal.NobodyCouldReachThat, edges.Refusal);
        Assert.False(edges.PhaseChanged);
    }

    [Fact]
    public void ARefusalThatArrivesWithAPhaseChangeIsReportedToo()
    {
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Tally),
            View(HideSeekPhase.Holding, HideSeekRefusal.NeedTwoPlayers));

        Assert.Equal(HideSeekRefusal.NeedTwoPlayers, edges.Refusal);
        Assert.True(edges.PhaseChanged);
        Assert.True(edges.ResetRequested);
    }

    // --- the positive controls ----------------------------------------------------------------

    [Fact]
    public void AMessageWithNoRefusalReportsNone()
    {
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Hiding, HideSeekRefusal.PutTheObjectDownFirst),
            View(HideSeekPhase.Hiding));

        Assert.Equal(HideSeekRefusal.None, edges.Refusal);
        Assert.False(edges.PhaseChanged);
    }

    [Fact]
    public void AStandingStillMessageRaisesNothingAtAll()
    {
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Seeking), View(HideSeekPhase.Seeking));

        Assert.False(edges.PhaseChanged);
        Assert.False(edges.Found);
        Assert.False(edges.ResetRequested);
        Assert.Equal(HideSeekRefusal.None, edges.Refusal);
    }

    // --- the phase events, unchanged by the fix -----------------------------------------------

    [Fact]
    public void TogetherWithARealFoundTickRaisesFound()
    {
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Seeking),
            View(HideSeekPhase.Together, foundTick: 1234));

        Assert.True(edges.PhaseChanged);
        Assert.True(edges.Found);
        Assert.False(edges.ResetRequested);
    }

    [Fact]
    public void TogetherWithoutAFoundTickDoesNotRaiseFound()
    {
        // HideSeekWire.NoFoundTick rides as -1: the seek timed out rather than ending on a find.
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Seeking),
            View(HideSeekPhase.Together, foundTick: -1));

        Assert.True(edges.PhaseChanged);
        Assert.False(edges.Found);
    }

    [Fact]
    public void OnlyTallyToHoldingIsTheResetEdge()
    {
        Assert.True(RoundViewEvents.Between(View(HideSeekPhase.Tally), View(HideSeekPhase.Holding))
            .ResetRequested);
        Assert.False(RoundViewEvents.Between(View(HideSeekPhase.Together), View(HideSeekPhase.Holding))
            .ResetRequested);
        Assert.False(RoundViewEvents.Between(View(HideSeekPhase.Tally), View(HideSeekPhase.Tally))
            .ResetRequested);
    }

    [Fact]
    public void StayingInOnePhaseNeverRaisesFoundEvenWithAFoundTickOnBothSides()
    {
        // The tick a find lands, the phase moves; every message after it in Together carries the
        // same FoundTick. Raising Found on each would burst DOOR-1's door ten times a second.
        RoundViewEvents.Edges edges = RoundViewEvents.Between(
            View(HideSeekPhase.Together, foundTick: 1234),
            View(HideSeekPhase.Together, foundTick: 1234));

        Assert.False(edges.PhaseChanged);
        Assert.False(edges.Found);
    }
}
