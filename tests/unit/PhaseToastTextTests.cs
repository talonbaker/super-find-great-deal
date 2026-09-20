using MpFoundation.Game.World;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// L11 (Issue #114) spec §1 steps 4/5: "Phase toasts on L1's events (sunset/night/dawn — one
/// line each, no art)." Exactly three of the four <see cref="PhaseEventKind"/> values should
/// produce a toast; <see cref="PhaseEventKind.DawnToDay"/> deliberately should not (see
/// <see cref="PhaseToastText"/>'s own doc for why — it would collide with the summary panel on
/// the run's final cycle).
/// </summary>
public class PhaseToastTextTests
{
    [Theory]
    [InlineData(PhaseEventKind.DayToDusk)]
    [InlineData(PhaseEventKind.DuskToNight)]
    [InlineData(PhaseEventKind.NightToDawn)]
    public void TheThreeSpecifiedCrossings_EachProduceANonEmptyLine(PhaseEventKind kind)
    {
        string? text = PhaseToastText.TextFor(kind);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void DawnToDay_ProducesNoToast()
    {
        // Per scope: only sunset/night/dawn get a toast. The run's final DawnToDay crossing
        // also fires RunEndedSignal in the same tick — a toast here would flash and be
        // immediately covered by the summary panel.
        Assert.Null(PhaseToastText.TextFor(PhaseEventKind.DawnToDay));
    }

    [Fact]
    public void EveryToastLine_IsDistinct()
    {
        // A toast that couldn't tell the player which phase just happened would defeat the
        // point of having three of them.
        string? sunset = PhaseToastText.TextFor(PhaseEventKind.DayToDusk);
        string? night = PhaseToastText.TextFor(PhaseEventKind.DuskToNight);
        string? dawn = PhaseToastText.TextFor(PhaseEventKind.NightToDawn);
        Assert.NotEqual(sunset, night);
        Assert.NotEqual(night, dawn);
        Assert.NotEqual(sunset, dawn);
    }
}
