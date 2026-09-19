using System;
using System.Collections.Generic;
using MpFoundation.Game.Presentation;
using MpFoundation.Ui.Flow;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-B1's headless proof (packet scope 6), Godot-free half: the scripted
/// playthrough walked against <see cref="FakePlaythroughDriver"/> with the SAME pure
/// routing decision the real screens poll (<see cref="ScreenRouter"/>), the §3.2 ordering
/// contract, the never-strand late-join landing for every state, and the §3.3 request
/// semantics. The in-engine twin (<c>--screenflow-selftest</c>) proves the same walk on
/// real CanvasLayers; this file proves the decision tables in full.
/// </summary>
public class FlowScreensTests
{
    // ------------------------------------------------------------------ the scripted walk

    [Fact]
    public void ScriptedWalk_RoutesEveryScreenInOrder()
    {
        var driver = new FakePlaythroughDriver();
        var routed = new List<ScreenId>();
        foreach (PlaythroughStep step in ScriptedPlaythrough.Steps)
        {
            step.Apply?.Invoke(driver);
            routed.Add(ScreenRouter.ScreenFor(driver.Synced, driver.State));
        }

        ScreenId[] expected =
        {
            ScreenId.Connecting, ScreenId.Connecting, ScreenId.RoundIntro, ScreenId.None,
            ScreenId.None, ScreenId.None, ScreenId.None, ScreenId.None, ScreenId.RoundEnd,
            ScreenId.UpgradeLobby, ScreenId.RoundIntro, ScreenId.None, ScreenId.None,
            ScreenId.Loss,
        };
        Assert.Equal(expected, routed);
    }

    [Fact]
    public void ScriptedWalk_ShowsEverySurfaceAtLeastOnce()
    {
        var driver = new FakePlaythroughDriver();
        var seen = new HashSet<ScreenId>();
        bool stripSeen = false, nightfallCued = false, duskCued = false;
        foreach (PlaythroughStep step in ScriptedPlaythrough.Steps)
        {
            step.Apply?.Invoke(driver);
            seen.Add(ScreenRouter.ScreenFor(driver.Synced, driver.State));
            stripSeen |= ScreenRouter.QuotaStripVisible(driver.Synced, driver.State);
            nightfallCued |= step.Cue == StepCue.Nightfall;
            duskCued |= step.Cue == StepCue.Dusk;
        }
        // All six §3.5 surfaces: 1 telegraph (dusk + nightfall cues), 2 tally, 3 lobby,
        // 4 loss, 5 quota strip, 6 connecting gate.
        Assert.Contains(ScreenId.Connecting, seen);
        Assert.Contains(ScreenId.RoundIntro, seen);
        Assert.Contains(ScreenId.RoundEnd, seen);
        Assert.Contains(ScreenId.UpgradeLobby, seen);
        Assert.Contains(ScreenId.Loss, seen);
        Assert.True(stripSeen, "quota strip never became visible during the walk");
        Assert.True(duskCued && nightfallCued, "the telegraph cues are missing from the script");
    }

    // ---------------------------------------------------------- late join into every state

    public static IEnumerable<object[]> EveryState()
    {
        yield return new object[] { (Action<FakePlaythroughDriver>)(_ => { }), ScreenId.Connecting };
        yield return new object[] { (Action<FakePlaythroughDriver>)(d => d.CommitSynced()), ScreenId.Connecting };
        yield return new object[] { (Action<FakePlaythroughDriver>)(d => { d.CommitSynced(); d.CommitRoundIntro(1, 3); }), ScreenId.RoundIntro };
        yield return new object[] { (Action<FakePlaythroughDriver>)(d => { d.CommitSynced(); d.CommitRoundIntro(1, 3); d.CommitRoundLive(); }), ScreenId.None };
        yield return new object[] { (Action<FakePlaythroughDriver>)(d => { d.CommitSynced(); d.CommitRoundIntro(1, 3); d.CommitRoundLive(); d.CommitRoundEnd(new RoundSummary(1, 3, 5, 8)); }), ScreenId.RoundEnd };
        yield return new object[] { (Action<FakePlaythroughDriver>)(d => { d.CommitSynced(); d.CommitRoundIntro(1, 3); d.CommitRoundLive(); d.CommitRoundEnd(new RoundSummary(1, 3, 5, 8)); d.CommitUpgradeLobby(); }), ScreenId.UpgradeLobby };
        yield return new object[] { (Action<FakePlaythroughDriver>)(d => { d.CommitSynced(); d.CommitRoundIntro(1, 3); d.CommitRoundLive(); d.CommitLoss(new RunOutcome(RunOutcomeKind.QuotaMissed, 1, 3, 0)); }), ScreenId.Loss };
    }

    /// <summary>The never-strand rule: a subscriber that attaches AFTER the state committed
    /// (so it witnessed zero events) lands on the correct screen from one poll of the
    /// queryable state — subscribe-and-poll proven against the fake driver (AC4).</summary>
    [Theory]
    [MemberData(nameof(EveryState))]
    public void LateJoin_LandsOnCorrectScreen_FromPollAlone(Action<FakePlaythroughDriver> arrangeBeforeJoining, ScreenId expected)
    {
        var driver = new FakePlaythroughDriver();
        arrangeBeforeJoining(driver); // everything happens before "we" exist.

        // The late joiner: no event subscriptions at all — the poll is the whole landing.
        Assert.Equal(expected, ScreenRouter.ScreenFor(driver.Synced, driver.State));

        // And the latched payloads the screen needs are pollable too (spec §3.4).
        if (expected == ScreenId.RoundEnd)
            Assert.NotNull(driver.LastRoundSummary);
        if (expected == ScreenId.Loss)
            Assert.NotNull(driver.LastOutcome);
    }

    // ------------------------------------------------------------- the §3.2 ordering contract

    [Fact]
    public void Events_FireAfterStateIsSet_StateChangedBeforeTypedEvent()
    {
        var driver = new FakePlaythroughDriver();
        driver.CommitSynced();
        var order = new List<string>();
        driver.StateChanged += (_, to, _) =>
        {
            order.Add("StateChanged");
            Assert.Equal(to, driver.State); // handler reads post-transition state.
        };
        driver.RoundIntroStarted += (round, _) =>
        {
            order.Add("RoundIntroStarted");
            Assert.Equal(PlaythroughState.RoundIntro, driver.State);
            Assert.Equal(round, driver.Round);
        };
        driver.CommitRoundIntro(1, 3);
        Assert.Equal(new[] { "StateChanged", "RoundIntroStarted" }, order);
    }

    // ------------------------------------------------------------------ the §3.3 requests

    [Fact]
    public void ReadyAdvance_AdvancesTally_ThenLobby_ThenNextIntro()
    {
        var driver = new FakePlaythroughDriver();
        driver.CommitSynced();
        driver.CommitRoundIntro(1, 3);
        driver.CommitRoundLive();
        driver.RequestReadyAdvance(); // stale — InRound, must be dropped.
        Assert.Equal(PlaythroughState.InRound, driver.State);
        Assert.Equal(0, driver.ReadyAdvanceRequests);

        driver.CommitRoundEnd(new RoundSummary(1, 3, 5, 8));
        driver.RequestReadyAdvance();
        Assert.Equal(PlaythroughState.UpgradeLobby, driver.State);
        driver.RequestReadyAdvance();
        Assert.Equal(PlaythroughState.RoundIntro, driver.State);
        Assert.Equal(2, driver.Round);
    }

    [Fact]
    public void PlayAgain_OnlyFromLoss_ResetsLedgerAndLatches()
    {
        var driver = new FakePlaythroughDriver();
        driver.CommitSynced();
        driver.CommitRoundIntro(1, 3);
        driver.RequestPlayAgain(); // stale — not Loss, dropped.
        Assert.Equal(PlaythroughState.RoundIntro, driver.State);

        driver.CommitRoundLive();
        driver.CommitBank(5);
        driver.CommitRoundEnd(new RoundSummary(1, 3, 5, 8));
        driver.CommitUpgradeLobby();
        driver.CommitRoundIntro(2, 8);
        driver.CommitRoundLive();
        driver.CommitLoss(new RunOutcome(RunOutcomeKind.QuotaMissed, 2, 8, 5));

        driver.RequestPlayAgain();
        Assert.Equal(PlaythroughState.RoundIntro, driver.State);
        Assert.Equal(1, driver.Round);
        Assert.Equal(0, driver.CumulativeBanked); // the boundary reset (spec §5.4).
        Assert.Null(driver.LastRoundSummary);
        Assert.Null(driver.LastOutcome);

        int requestsAfterFirst = driver.PlayAgainRequests;
        driver.RequestPlayAgain(); // the double press finds RoundIntro and drops (§6 case 5).
        Assert.Equal(requestsAfterFirst, driver.PlayAgainRequests);
    }

    // ----------------------------------------------------------------------------- timers

    [Fact]
    public void Tick_ClampsAtZero_AndNeverAdvancesState()
    {
        var driver = new FakePlaythroughDriver();
        driver.CommitSynced();
        driver.CommitRoundIntro(1, 3);
        driver.Tick(9999);
        Assert.Equal(0, driver.StateRemainingSec);
        Assert.Equal(PlaythroughState.RoundIntro, driver.State); // display clamps; commits advance.
    }

    // ------------------------------------------------------------------- router totality

    [Fact]
    public void Router_IsTotal_OverEveryState()
    {
        foreach (PlaythroughState state in Enum.GetValues<PlaythroughState>())
        {
            ScreenId synced = ScreenRouter.ScreenFor(true, state);
            Assert.Equal(ScreenId.Connecting, ScreenRouter.ScreenFor(false, state)); // unsynced always gates.
            if (state == PlaythroughState.InRound)
                Assert.Equal(ScreenId.None, synced);
            else
                Assert.NotEqual(ScreenId.None, synced); // every non-InRound state owns a screen.
        }
    }

    [Fact]
    public void Telegraph_AllowedWithoutView_GatedWithOne()
    {
        Assert.True(ScreenRouter.TelegraphAllowed(null)); // pre-integration: today's behavior.
        var driver = new FakePlaythroughDriver();
        driver.CommitSynced();
        driver.CommitRoundIntro(1, 3);
        driver.CommitRoundLive();
        Assert.True(ScreenRouter.TelegraphAllowed(driver));
        driver.CommitRoundEnd(new RoundSummary(1, 3, 5, 8));
        Assert.False(ScreenRouter.TelegraphAllowed(driver)); // no dusk fanfare over a tally.
    }
}
