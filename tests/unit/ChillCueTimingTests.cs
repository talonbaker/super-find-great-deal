using Godot;
using Sail.Game.Water;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// R1, 2026-08-09: <b>when</b> the cold's urgency cue arrives, as an executable guarantee.
///
/// <see cref="ChillReadoutTests"/> pins the wording ladder and <c>WaterChillTests</c> pins the
/// curve's shape; between them they assert that the cue escalates correctly and say nothing at all
/// about whether it escalates in TIME. That gap is not hypothetical — it is exactly the shipped
/// defect. With <c>CueOnsetChill</c> at 0.35 every one of those tests was green while a camper
/// floating inside the rope got no signal on any channel for the first 63 seconds, and the field
/// report was "I did not realize that anything was happening... with or without audio."
///
/// The dispatch decision default for this packet is "cue the ONSET, not just the crisis — a cue
/// that only fires near the sputter-out satisfies the letter and fails the intent." These are that
/// sentence, made falsifiable. They are deliberately written against the WORST case in the lake
/// (the safest, slowest water, where the warning takes longest to arrive) and swept across every
/// position, because a guarantee that holds only in the cold deep is not a guarantee.
///
/// <b>These are perception tests, not state-machine tests.</b> Nothing here touches hysteresis, the
/// sputter-out, or the position sweep — those are <c>WaterChillTests</c>' and are unchanged.
/// </summary>
public class ChillCueTimingTests
{
    /// <summary>Positions spanning the whole lake: east of the rope where the water is safest and
    /// the warning is slowest, through the cold ramp, out past its end into the fastest water.</summary>
    public static TheoryData<float> LakeXs()
    {
        var data = new TheoryData<float>();
        for (float x = WaterGeometry.ShelfX; x >= -120f; x -= 2.5f)
            data.Add(x);
        return data;
    }

    /// <summary>
    /// The headline guarantee: wherever you are in the lake, a camper who simply floats is told
    /// the cold exists within this many seconds. 9.0 s is the true worst case
    /// (<c>CueOnsetChill</c> 0.05 x the 180 s safest water); the bound is set a little above it so
    /// an intentional retune has room to breathe, and far below the 63 s the shipped build
    /// actually delivered.
    /// </summary>
    private const float OnsetMustArriveWithinSec = 12f;

    /// <summary>The onset must also be early as a FRACTION of the swim, not merely early in
    /// absolute seconds — in the fastest water the whole swim is 20 s, and a cue that arrived 12 s
    /// into it would be a crisis alarm wearing an onset's name.</summary>
    private const float OnsetMustArriveWithinFraction = 0.10f;

    [Theory]
    [MemberData(nameof(LakeXs))]
    public void CueOnset_ArrivesWithinTheGuaranteedSeconds_Everywhere(float x)
    {
        float seconds = SecondsToCueOnset(x);

        Assert.True(
            seconds <= OnsetMustArriveWithinSec,
            $"at x={x} the first perceptible cue takes {seconds:F1}s of floating; " +
            $"the guarantee is {OnsetMustArriveWithinSec}s");
    }

    [Theory]
    [MemberData(nameof(LakeXs))]
    public void CueOnset_ArrivesInTheFirstTenthOfTheSwim_Everywhere(float x)
    {
        float total = WaterGeometry.TimeToFullChillSec(x);
        float fraction = SecondsToCueOnset(x) / total;

        Assert.True(
            fraction <= OnsetMustArriveWithinFraction,
            $"at x={x} the cue starts {fraction:P0} into a {total:F0}s swim; " +
            $"the guarantee is the first {OnsetMustArriveWithinFraction:P0}");
    }

    /// <summary>
    /// The onset must leave usable time to act. A cue is a fairness channel: it exists so the
    /// player can still choose to swim back, so the gap between "you are told" and "you are taken"
    /// has to be most of the swim, not a sliver at the end.
    /// </summary>
    [Theory]
    [MemberData(nameof(LakeXs))]
    public void AfterTheOnset_MostOfTheSwimRemains(float x)
    {
        float total = WaterGeometry.TimeToFullChillSec(x);
        float remaining = total - SecondsToCueOnset(x);

        Assert.True(
            remaining >= total * 0.85f,
            $"at x={x} only {remaining:F1}s of a {total:F0}s swim remain after the first cue");
    }

    /// <summary>
    /// The three worded stages must not all pile up at the end. Each escalation step needs to be
    /// separated by real swimming time, or "escalation" is a single event with three labels.
    /// </summary>
    [Theory]
    [MemberData(nameof(LakeXs))]
    public void TheThreeReadoutStages_AreSeparatedInTime(float x)
    {
        float toStage1 = SecondsToIntensity(x, 0.0001f);
        float toStage2 = SecondsToIntensity(x, 0.50f);
        float toStage3 = SecondsToIntensity(x, 0.85f);
        float total = WaterGeometry.TimeToFullChillSec(x);

        Assert.True(toStage1 < toStage2, $"at x={x} stage 2 does not come after stage 1");
        Assert.True(toStage2 < toStage3, $"at x={x} stage 3 does not come after stage 2");
        // Each step is worth at least a tenth of the swim, so no two stages read as simultaneous.
        Assert.True(toStage2 - toStage1 >= total * 0.10f, $"at x={x} stages 1 and 2 are too close");
        Assert.True(toStage3 - toStage2 >= total * 0.10f, $"at x={x} stages 2 and 3 are too close");
    }

    /// <summary>
    /// The onset threshold itself, stated as intent rather than as a number: whatever
    /// <c>CueOnsetChill</c> is retuned to, it has to stay in the first tenth of the chill range.
    /// This is the single line that would have gone red on the shipped 0.35.
    /// </summary>
    [Fact]
    public void CueOnsetChill_SitsInTheFirstTenthOfTheRange()
    {
        Assert.InRange(ChillClock.CueOnsetChill, 0f, 0.10f);
    }

    /// <summary>Seconds of continuous swimming at <paramref name="x"/>, from bone dry, before the
    /// cue becomes perceptible at all. Derived from the shipped constants rather than restated, so
    /// a retune moves the assertion with it.</summary>
    private static float SecondsToCueOnset(float x) => SecondsToIntensity(x, 0.0001f);

    /// <summary>Seconds of continuous swimming at <paramref name="x"/> before
    /// <see cref="ChillClock.CueIntensity"/> reaches <paramref name="intensity"/>. Inverts the cue
    /// curve rather than stepping the integrator: the arithmetic is exact and cannot drift with a
    /// step size.</summary>
    private static float SecondsToIntensity(float x, float intensity)
    {
        // intensity = (chill - onset) / (1 - onset)  =>  chill = onset + intensity * (1 - onset)
        float chill = ChillClock.CueOnsetChill + intensity * (1f - ChillClock.CueOnsetChill);
        return Mathf.Clamp(chill, 0f, 1f) * WaterGeometry.TimeToFullChillSec(x);
    }
}
