using MpFoundation;
using MpFoundation.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// STYLE-4's engine-free tier: the named-phase lookup and the launch-argument plumbing that turns
/// "--cycle-start-phase noon --cycle-freeze" into two settled values.
///
/// <para>What is deliberately NOT here: the freeze's behaviour over time. <c>CycleDriver</c> is a
/// <c>Node</c> whose freeze lives in its per-tick <c>_PhysicsProcess</c> and its RPC-fed
/// <c>Apply</c>, so proving "the phase is identical 60 s later" needs a live tree and is done where
/// it can be done honestly — tests/Run-StyleRockTest.ps1, with its unfrozen controls. Asserting it
/// here against a re-implementation of the arithmetic would prove the re-implementation.</para>
/// </summary>
public class CycleFreezeTests
{
    // --- the name ---------------------------------------------------------------------------------

    [Fact]
    public void Noon_IsTheDayBandsCentre_OnDayOne()
    {
        Assert.True(CycleBands.TryNamedPhase("noon", 0, out float noon));
        (float duskStart, _, _) = CycleBands.Boundaries(0);
        Assert.Equal((double)(duskStart * 0.5f), (double)(noon), 6);
    }

    /// <summary>The whole reason a NAME exists rather than a number: the day band shortens as the
    /// run escalates, so a literal that is right on day 1 is wrong on day 5 while still looking
    /// perfectly plausible on the command line.</summary>
    [Fact]
    public void Noon_FollowsTheEscalationTable_AcrossTheRun()
    {
        var phases = new float[CycleBands.MaxDayIndex + 1];
        for (int day = 0; day <= CycleBands.MaxDayIndex; day++)
        {
            Assert.True(CycleBands.TryNamedPhase("noon", day, out phases[day]));
            (float duskStart, _, _) = CycleBands.Boundaries(day);
            Assert.Equal((double)(duskStart * 0.5f), (double)(phases[day]), 6);
        }
        for (int day = 1; day <= CycleBands.MaxDayIndex; day++)
            Assert.True(phases[day] < phases[day - 1], $"noon on day {day + 1} is not earlier than day {day}");
    }

    /// <summary>Every name is a lookup INTO <see cref="CycleBands.AtmosphereBreakpoints"/>, never a
    /// second copy of a boundary. If a name ever stops matching a breakpoint, the lookup has grown
    /// its own numbers, which is the duplication CycleBands exists to prevent.</summary>
    [Theory]
    [InlineData("day-start", 0)]
    [InlineData("noon", 1)]
    [InlineData("midday", 1)]
    [InlineData("dusk", 2)]
    [InlineData("night", 5)]
    [InlineData("midnight", 6)]
    [InlineData("deep-night", 6)]
    [InlineData("dawn", 7)]
    public void EveryName_IsABreakpoint_OnEveryDay(string name, int breakpointIndex)
    {
        for (int day = 0; day <= CycleBands.MaxDayIndex; day++)
        {
            Assert.True(CycleBands.TryNamedPhase(name, day, out float phase));
            Assert.Equal((double)(CycleBands.AtmosphereBreakpoints(day)[breakpointIndex]), (double)(phase), 6);
        }
    }

    [Fact]
    public void PhaseNames_ListsExactlyWhatIsAccepted()
    {
        foreach (string name in CycleBands.PhaseNames)
            Assert.True(CycleBands.TryNamedPhase(name, 0, out _), $"advertised name '{name}' is not accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nooon")]
    [InlineData("half past four")]
    public void UnknownName_IsRefused_RatherThanResolvingToZero(string name)
    {
        Assert.False(CycleBands.TryNamedPhase(name, 0, out float phase));
        Assert.Equal(0f, phase);
    }

    [Fact]
    public void NameLookup_IsCaseInsensitive()
    {
        Assert.True(CycleBands.TryNamedPhase("NOON", 0, out float upper));
        Assert.True(CycleBands.TryNamedPhase("noon", 0, out float lower));
        Assert.Equal((double)(lower), (double)(upper), 6);
    }

    // --- the launch arguments ---------------------------------------------------------------------

    [Fact]
    public void CycleFreeze_DefaultsOff_AndParses()
    {
        Assert.False(LaunchOptions.Parse(new[] { "--server" }).CycleFreeze);
        Assert.True(LaunchOptions.Parse(new[] { "--cycle-freeze" }).CycleFreeze);
    }

    [Fact]
    public void NamedStartPhase_Resolves_AndRecordsTheName()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--cycle-start-phase", "noon" });
        CycleBands.TryNamedPhase("noon", 0, out float expected);
        Assert.Equal((double)(expected), (double)(o.CycleStartPhase), 6);
        Assert.Equal("noon", o.CycleStartPhaseName);
    }

    /// <summary>The reason resolution is a post-parse pass rather than inline: otherwise the answer
    /// would depend on which side of <c>--cycle-start-day</c> the name was written on, which is a
    /// bug that would look exactly like a working flag until somebody reordered a launch line.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamedStartPhase_IsIndependentOfArgumentOrder(bool dayFirst)
    {
        string[] args = dayFirst
            ? new[] { "--cycle-start-day", "4", "--cycle-start-phase", "noon" }
            : new[] { "--cycle-start-phase", "noon", "--cycle-start-day", "4" };
        LaunchOptions o = LaunchOptions.Parse(args);
        CycleBands.TryNamedPhase("noon", 4, out float expected);
        Assert.Equal((double)(expected), (double)(o.CycleStartPhase), 6);
        Assert.Equal(4, o.CycleStartDay);
    }

    [Fact]
    public void NumericStartPhase_StillWins_AndCarriesNoName()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--cycle-start-phase", "0.78" });
        Assert.Equal((double)(0.78f), (double)(o.CycleStartPhase), 6);
        Assert.Equal("", o.CycleStartPhaseName);
    }

    /// <summary>An unknown name must leave the phase at its default rather than half-applying: a
    /// review harness silently reviewing at day-start when it was asked for noon is the worst
    /// available outcome, because it looks like it worked.</summary>
    [Fact]
    public void UnknownStartPhaseName_LeavesTheDefault_AndDoesNotClaimAName()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--cycle-start-phase", "nooon" });
        Assert.Equal(0f, o.CycleStartPhase);
        Assert.Equal("", o.CycleStartPhaseName);
    }

    /// <summary>The review launch line as a whole, parsed once — the thing
    /// deploy/Play-StyleReview.cmd actually passes.</summary>
    [Fact]
    public void TheReviewLaunchLine_ParsesAsOneCoherentSet()
    {
        LaunchOptions o = LaunchOptions.Parse(new[]
        {
            "--cycle-start-phase", "noon", "--cycle-freeze",
        });
        CycleBands.TryNamedPhase("noon", 0, out float noon);
        Assert.True(o.CycleFreeze);
        Assert.Equal((double)(noon), (double)(o.CycleStartPhase), 6);
    }
}
