using System.Linq;
using MpFoundation.Game.World;
using MpFoundation.Ui;
using MpFoundation.Ui.Flow;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-B1's per-screen display-decision statics: content extremes (AC6 — 0 banked,
/// 6-player roster, longest names), the worded-stages register check with its positive
/// control (AC5), countdown clamping, and the telegraph content seam.
/// </summary>
public class FlowCopyTests
{
    // -------------------------------------------------------------------- round intro (AC6)

    [Theory]
    [InlineData(1, "NIGHT 1")]
    [InlineData(214, "NIGHT 214")]
    [InlineData(0, "NIGHT 1")]  // pre-sync round clamps up — never "NIGHT 0".
    [InlineData(-3, "NIGHT 1")]
    public void IntroTitle_ClampsRound(int round, string expected) =>
        Assert.Equal(expected, RoundIntroCard.Title(round));

    [Fact]
    public void IntroDemand_Extremes()
    {
        Assert.Equal("The winter cache needs 3 more by dawn.", RoundIntroCard.DemandLine(3, 0));
        Assert.Equal("The winter cache needs 1 more by dawn.", RoundIntroCard.DemandLine(8, 7));
        // Met (and over-met) reads as a statement, not "0 more".
        Assert.DoesNotContain("0 more", RoundIntroCard.DemandLine(3, 3));
        Assert.DoesNotContain("-", RoundIntroCard.DemandLine(3, 10));
    }

    // ---------------------------------------------------------------------- round-end tally

    [Fact]
    public void Tally_HonestNumbers_AndCarryoverOnlyWhenPositive()
    {
        var surplus = new RoundSummary(Round: 1, Demand: 3, Banked: 5, NextDemand: 8);
        Assert.Equal("NIGHT 1 - SURVIVED", RoundEndTallyPanel.Heading(1));
        Assert.Equal("Cache banked: 5 of 3 needed", RoundEndTallyPanel.BankedLine(surplus));
        Assert.Equal("Surplus carries forward: +2", RoundEndTallyPanel.CarryoverLine(surplus));

        var exact = new RoundSummary(1, 3, 3, 8);
        Assert.Null(RoundEndTallyPanel.CarryoverLine(exact)); // shown only when nonzero (packet 3c).

        Assert.Contains("8", RoundEndTallyPanel.NextDemandLine(surplus));
    }

    /// <summary>AC6's roster extremes ride the existing formatter seam the tally composes —
    /// re-asserted here against this packet's actual content: six kids, a deliberately long
    /// name, and an empty roster all format without truncation surprises.</summary>
    [Fact]
    public void Tally_RosterExtremes_ThroughExistingFormatter()
    {
        var longest = new KidQuarters(7, "Maximiliana-Wilhelmina Konstantinopoulos", 0);
        string line = SessionSummaryFormatter.KidLine(longest);
        Assert.Contains("Maximiliana-Wilhelmina Konstantinopoulos", line);
        Assert.Contains("0q", line); // the 0-earned kid renders honestly, not blank.

        for (int i = 1; i <= 6; i++) // six-player roster: every line distinct and well-formed.
            Assert.Contains($"Player {i}", SessionSummaryFormatter.KidLine(new KidQuarters(i, "", i)));
    }

    // -------------------------------------------------------------------------- loss (AC6)

    [Fact]
    public void Loss_ZeroBanked_ReadsAsStatement()
    {
        var outcome = new RunOutcome(RunOutcomeKind.QuotaMissed, Round: 1, Demand: 3, Banked: 0);
        Assert.Equal("THE CACHE RAN DRY", LossScreen.VerdictHeading(outcome));
        Assert.Equal("Night 1: the cache held 0 of the 3 winter needed.", LossScreen.NumbersLine(outcome));
    }

    [Fact]
    public void Loss_RegisterLaw_NobodyTakenOrHurt()
    {
        // The register law survives even placeholder copy: no taken child, no harm words.
        string all = LossScreen.BodyLine + LossScreen.VerdictHeading(
            new RunOutcome(RunOutcomeKind.QuotaMissed, 1, 3, 0));
        foreach (string banned in new[] { "taken", "dead", "died", "kill", "blood", "gone missing" })
            Assert.DoesNotContain(banned, all.ToLowerInvariant());
    }

    // ------------------------------------------------------------------------ upgrade lobby

    [Fact]
    public void Lobby_FramingNamesTheNextNight_AndSlotsAreLabeledEmpty()
    {
        Assert.Contains("Night 2", UpgradeLobbyPanel.FramingLine(1));
        Assert.Contains("Night 2", UpgradeLobbyPanel.FramingLine(0)); // degenerate 0 clamps to 1-survived.
        Assert.NotEmpty(UpgradeLobbyPanel.UpgradeSlotLabels);
        foreach (string slot in UpgradeLobbyPanel.UpgradeSlotLabels)
            Assert.Contains("UPGRADE SLOT", slot); // content-agnostic by ruling — labeled, never faked.
    }

    // ------------------------------------------------- quota strip: worded stages (AC5)

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(-5, 3, 0)]   // negative banked clamps to empty.
    [InlineData(1, 3, 1)]
    [InlineData(2, 4, 2)]    // exactly half -> stage 2.
    [InlineData(2, 3, 2)]
    [InlineData(3, 3, 3)]
    [InlineData(9, 3, 3)]
    [InlineData(0, 0, 3)]    // nothing owed = met.
    [InlineData(0, -1, 3)]
    [InlineData(600000000, 1000000000, 2)] // the 1e9 demand clamp tail — no overflow.
    public void QuotaStages_Boundaries(int banked, int demand, int expected) =>
        Assert.Equal(expected, QuotaStripWidget.StageOf(banked, demand));

    /// <summary>AC5's ABSENCE check with its positive control: no stage line may carry a
    /// digit (worded stages, never a number — the register ban), and the detector proves it
    /// can find a digit before its all-clear means anything.</summary>
    [Fact]
    public void QuotaStages_WordedOnly_NoDigits_WithPositiveControl()
    {
        static bool HasDigit(string s) => s.Any(char.IsDigit);
        Assert.True(HasDigit("3 of 8 banked"), "positive control failed — the detector is blind");

        for (int stage = 0; stage <= 3; stage++)
        {
            string line = QuotaStripWidget.TextFor(stage);
            Assert.False(string.IsNullOrWhiteSpace(line), $"stage {stage} has no wording");
            Assert.False(HasDigit(line), $"stage {stage} leaks a number: \"{line}\"");
        }
        Assert.False(HasDigit(QuotaStripWidget.CaptionText));
    }

    [Fact]
    public void QuotaStages_EscalateInWordingOnly_DistinctLines()
    {
        var lines = new[] { 0, 1, 2, 3 }.Select(QuotaStripWidget.TextFor).ToArray();
        Assert.Equal(4, lines.Distinct().Count());
    }

    // ------------------------------------------------------------------- countdown + telegraph

    [Theory]
    [InlineData(30.0, "Next day in 30s")]
    [InlineData(0.2, "Next day in 1s")]
    [InlineData(0.0, "Next day in 0s")]
    [InlineData(-5.0, "Next day in 0s")] // stale extrapolation never shows negative.
    public void Countdown_Clamps(double sec, string expected) =>
        Assert.Equal(expected, FlowFormat.Countdown(sec, "Next day"));

    [Fact]
    public void Nightfall_ExactlyOneCrossingGetsTheFullTreatment()
    {
        Assert.True(PhaseToastText.FullTreatmentFor(PhaseEventKind.DuskToNight));
        Assert.False(PhaseToastText.FullTreatmentFor(PhaseEventKind.DayToDusk));
        Assert.False(PhaseToastText.FullTreatmentFor(PhaseEventKind.NightToDawn));
        Assert.False(PhaseToastText.FullTreatmentFor(PhaseEventKind.DawnToDay));
    }

    [Fact]
    public void Nightfall_SpellsOutTheMechanics()
    {
        // The brief's requirement: what changes at night is TOLD. The lines must name the
        // three live mechanics (creatures/light, cold, quota) — asserted by keyword so a
        // /direct tone rewrite can't silently drop a mechanic.
        string all = string.Join(" ", PhaseToastText.NightfallLines).ToLowerInvariant();
        Assert.Contains("creature", all);
        Assert.Contains("light", all);
        Assert.Contains("freeze", all);
        Assert.Contains("dawn", all);
        Assert.NotEmpty(PhaseToastText.NightfallHeading);
    }
}
