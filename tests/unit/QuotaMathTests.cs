using Sail.Game.Run;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-A1 acceptance criterion 6's extremes half (spec §2.2): the demand curve's
/// arithmetic, its strict-increase floor, and every named extreme — empty array, factor
/// &lt;= 1, overflow clamp — plus the banking-window and both quota-verdict models. The
/// RESOURCE half (the .tres actually loading and driving this math) is --flow-selftest's,
/// which needs the engine.
/// </summary>
public class QuotaMathTests
{
    private static readonly int[] Placeholder = { 3, 5, 8, 12 };
    private const float PlaceholderFactor = 1.35f;

    // --- the curve -----------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 8)]
    [InlineData(4, 12)]
    [InlineData(5, 17)] // ceil(12 * 1.35) = ceil(16.2)
    [InlineData(6, 23)] // ceil(17 * 1.35) = ceil(22.95)
    public void PlaceholderCurve_AuthoredThenGeometricTail(int round, int expected) =>
        Assert.Equal(expected, QuotaMath.Demand(round, Placeholder, PlaceholderFactor));

    [Fact]
    public void StrictIncreaseFloor_AFlatAuthoredArrayCannotFlattenTheRamp()
    {
        int[] flat = { 5, 5, 5 };
        Assert.Equal(5, QuotaMath.Demand(1, flat, PlaceholderFactor));
        Assert.Equal(6, QuotaMath.Demand(2, flat, PlaceholderFactor));
        Assert.Equal(7, QuotaMath.Demand(3, flat, PlaceholderFactor));
    }

    [Fact]
    public void StrictIncreaseFloor_ADecreasingAuthoredArrayIsRaisedNotHonored()
    {
        int[] decreasing = { 10, 4 };
        Assert.Equal(10, QuotaMath.Demand(1, decreasing, PlaceholderFactor));
        Assert.Equal(11, QuotaMath.Demand(2, decreasing, PlaceholderFactor));
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.5f)]
    [InlineData(0f)]
    [InlineData(-2f)]
    public void FactorAtOrBelowOne_TailDegradesToLinear_NeverDividesOrFlatlines(float factor)
    {
        int[] single = { 3 };
        Assert.Equal(3, QuotaMath.Demand(1, single, factor));
        Assert.Equal(4, QuotaMath.Demand(2, single, factor));
        Assert.Equal(5, QuotaMath.Demand(3, single, factor));
        Assert.Equal(6, QuotaMath.Demand(4, single, factor));
    }

    [Fact]
    public void EmptyOrNullArray_FallsBackToThree()
    {
        Assert.Equal(3, QuotaMath.Demand(1, System.Array.Empty<int>(), PlaceholderFactor));
        Assert.Equal(3, QuotaMath.Demand(1, null, PlaceholderFactor));
        Assert.Equal(5, QuotaMath.Demand(2, null, PlaceholderFactor)); // ceil(3*1.35)=ceil(4.05)
    }

    [Fact]
    public void NonPositiveAuthoredEntries_FlooredToOne()
    {
        Assert.Equal(1, QuotaMath.Demand(1, new[] { 0 }, PlaceholderFactor));
        Assert.Equal(1, QuotaMath.Demand(1, new[] { -7 }, PlaceholderFactor));
        Assert.Equal(5, QuotaMath.Demand(2, new[] { 0, 5 }, PlaceholderFactor));
    }

    [Fact]
    public void RoundBelowOne_ReadsAsRoundOne() =>
        Assert.Equal(3, QuotaMath.Demand(-4, Placeholder, PlaceholderFactor));

    [Fact]
    public void OverflowClamp_TheOpenEndedTailCapsAndStaysCapped()
    {
        int[] huge = { 500_000_000 };
        Assert.Equal(500_000_000, QuotaMath.Demand(1, huge, 3f));
        Assert.Equal(QuotaMath.DemandClamp, QuotaMath.Demand(2, huge, 3f));  // 1.5e9 → clamp
        Assert.Equal(QuotaMath.DemandClamp, QuotaMath.Demand(3, huge, 3f));  // capped stays capped
        Assert.Equal(QuotaMath.DemandClamp, QuotaMath.Demand(50, huge, 3f)); // no wraparound ever
    }

    // --- cumulative demand ----------------------------------------------------------------------

    [Fact]
    public void CumulativeDemand_SumsTheCurve()
    {
        Assert.Equal(3, QuotaMath.CumulativeDemand(1, Placeholder, PlaceholderFactor));
        Assert.Equal(8, QuotaMath.CumulativeDemand(2, Placeholder, PlaceholderFactor));
        Assert.Equal(16, QuotaMath.CumulativeDemand(3, Placeholder, PlaceholderFactor));
        Assert.Equal(28, QuotaMath.CumulativeDemand(4, Placeholder, PlaceholderFactor));
    }

    [Fact]
    public void CumulativeDemand_SharesTheOverflowClamp()
    {
        int[] huge = { 900_000_000 };
        Assert.Equal(QuotaMath.DemandClamp, QuotaMath.CumulativeDemand(2, huge, 1.0f));
        Assert.Equal(QuotaMath.DemandClamp, QuotaMath.CumulativeDemand(100, huge, 3f));
    }

    // --- the banking window (spec §2.4 / §5.6) ---------------------------------------------------

    [Theory]
    [InlineData(PlaythroughState.RoundIntro, true)]
    [InlineData(PlaythroughState.InRound, true)]
    [InlineData(PlaythroughState.Boot, false)]
    [InlineData(PlaythroughState.RoundEnd, false)]
    [InlineData(PlaythroughState.UpgradeLobby, false)]
    [InlineData(PlaythroughState.Loss, false)]
    public void BankingOpen_ExactlyWhilePlayIsLive(PlaythroughState state, bool open) =>
        Assert.Equal(open, QuotaMath.BankingOpen(state));

    // --- the verdict question, both carry models (spec §2.1 / D3) --------------------------------

    [Fact]
    public void QuotaMissed_CarrySurplus_ComparesCumulatives_SurplusRollsForward()
    {
        // Round 2 of the placeholder curve: cumulative demand 8. Banked 9 in round 1, zero
        // in round 2 — the stockpile still covers it.
        Assert.False(QuotaMath.QuotaMissed(true, cumulativeBanked: 9, cumulativeDemand: 8,
            bankedThisRound: 0, demandThisRound: 5));
        Assert.True(QuotaMath.QuotaMissed(true, cumulativeBanked: 7, cumulativeDemand: 8,
            bankedThisRound: 7, demandThisRound: 5));
        Assert.False(QuotaMath.QuotaMissed(true, cumulativeBanked: 8, cumulativeDemand: 8,
            bankedThisRound: 0, demandThisRound: 5)); // exactly met is met
    }

    [Fact]
    public void QuotaMissed_PerRoundBucket_IgnoresTheStockpile()
    {
        // The one-flag flip (CarrySurplus=false): last round's surplus no longer helps.
        Assert.True(QuotaMath.QuotaMissed(false, cumulativeBanked: 9, cumulativeDemand: 8,
            bankedThisRound: 0, demandThisRound: 5));
        Assert.False(QuotaMath.QuotaMissed(false, cumulativeBanked: 5, cumulativeDemand: 8,
            bankedThisRound: 5, demandThisRound: 5));
    }
}
