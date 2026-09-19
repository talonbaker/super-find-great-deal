using Sail.Game.Water;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// 2026-08-08 playtest fallout, P2: the pure intensity -&gt; stage -&gt; line mapping behind
/// <see cref="ChillReadout"/>, the new diegetic-readout channel. Same seam
/// <see cref="PhaseToastText"/> uses — a scene-tree-free class so the wording ladder is directly
/// assertable.
/// </summary>
public class ChillReadoutTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void AtOrBelowZero_IsHiddenAndSilent(float intensity)
    {
        Assert.Equal(0, ChillReadout.StageOf(intensity));
        Assert.Equal("", ChillReadout.TextFor(ChillReadout.StageOf(intensity)));
    }

    [Fact]
    public void JustAboveZero_IsTheOnsetStage()
    {
        // The whole point of this packet: SOMETHING shows the instant chill leaves zero, not
        // only once it is nearly too late.
        Assert.Equal(1, ChillReadout.StageOf(0.001f));
    }

    [Fact]
    public void StageBoundariesAreOrderedAndEachHasANonEmptyLine()
    {
        int onset = ChillReadout.StageOf(0.2f);
        int mid = ChillReadout.StageOf(0.6f);
        int crisis = ChillReadout.StageOf(0.95f);
        Assert.True(onset < mid);
        Assert.True(mid < crisis);
        foreach (int stage in new[] { onset, mid, crisis })
            Assert.False(string.IsNullOrWhiteSpace(ChillReadout.TextFor(stage)));
    }

    [Fact]
    public void AtFullIntensity_IsTheCrisisStage()
    {
        Assert.Equal(3, ChillReadout.StageOf(1f));
    }

    [Fact]
    public void StageOf_IsMonotonicNondecreasing()
    {
        int previous = -1;
        for (float i = 0f; i <= 1.0001f; i += 0.01f)
        {
            int stage = ChillReadout.StageOf(i);
            Assert.True(stage >= previous, $"stage went backwards at intensity={i}");
            previous = stage;
        }
    }

    [Fact]
    public void EveryStageLine_IsDistinct()
    {
        // A readout whose stages could not be told apart would defeat the point of having them.
        string onset = ChillReadout.TextFor(1);
        string mid = ChillReadout.TextFor(2);
        string crisis = ChillReadout.TextFor(3);
        Assert.NotEqual(onset, mid);
        Assert.NotEqual(mid, crisis);
        Assert.NotEqual(onset, crisis);
    }

    [Fact]
    public void NoStageLine_ContainsANumber()
    {
        // Spec §5.1's "no HUD bar" ban is on a meter, not on diegetic text — but the guarantee
        // only holds if nothing here ever prints the chill value itself.
        foreach (int stage in new[] { 1, 2, 3 })
        {
            string text = ChillReadout.TextFor(stage);
            foreach (char c in text)
                Assert.False(char.IsDigit(c), $"stage {stage} line contains a digit: \"{text}\"");
        }
    }
}
