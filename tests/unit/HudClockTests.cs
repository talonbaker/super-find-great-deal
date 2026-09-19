using MpFoundation.Game.World;
using MpFoundation.Ui.Hud;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The top-centre day/phase readout's phase -> band mapping (<see cref="HudClock"/>).
///
/// The property that matters: the readout must agree with the SKY on every day of the run.
/// <see cref="CycleBands"/> moves the run's phase boundaries as the nights get longer (dusk from
/// phase 0.550 on day 1 to 0.350 on day 5), so the readout names the band the table reports
/// rather than deriving anything from raw phase. These tests pin the band names at the table's
/// own boundaries, which is exactly where a re-derivation would drift.
/// </summary>
public class HudClockTests
{
    /// <summary>The day number is 1-based and keeps counting past the escalation table's clamp.
    /// A group that survives to day 7 must not be told it is day 5.</summary>
    [Fact]
    public void DayPhaseText_IsOneBased_AndNotClampedAtTheTable()
    {
        Assert.StartsWith("DAY 1 ", HudClock.DayPhaseText(0f, 0));
        Assert.StartsWith("DAY 5 ", HudClock.DayPhaseText(0f, 4));
        Assert.StartsWith("DAY 8 ", HudClock.DayPhaseText(0f, 7));
    }

    [Fact]
    public void DayPhaseText_NamesTheBandTheSkyIsIn()
    {
        (float duskStart, float nightStart, float dawnStart) = CycleBands.Boundaries(0);
        Assert.Equal("DAY 1 · DAY", HudClock.DayPhaseText(0f, 0));
        Assert.Equal("DAY 1 · DUSK", HudClock.DayPhaseText(duskStart, 0));
        Assert.Equal("DAY 1 · NIGHT", HudClock.DayPhaseText(nightStart, 0));
        Assert.Equal("DAY 1 · DAWN", HudClock.DayPhaseText(dawnStart, 0));
    }

    /// <summary>Dusk counts as night-side: the accent turns when the light does, not a band
    /// later. Dawn does not — by then the HUD should have handed the day back.</summary>
    [Theory]
    [InlineData(CycleBands.Band.Day, false)]
    [InlineData(CycleBands.Band.DuskSweep, true)]
    [InlineData(CycleBands.Band.Night, true)]
    [InlineData(CycleBands.Band.DawnSweep, false)]
    public void IsNightSide(CycleBands.Band band, bool expected)
    {
        Assert.Equal(expected, HudClock.IsNightSide(band));
    }
}
