using Godot;
using MpFoundation;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>PHYS-2 (2026-09-20): the fixture's shove is a NUMBER the suite chooses.</b>
///
/// <para>PHYS-1 staged <c>Run-PhysicsFeelTest</c>'s domino row by walking a bot into it and then
/// measured the bot: peak approach speeds of <b>0.66, 0.70, 0.89, 1.18, 2.88 and 3.30 m/s</b> into
/// the same fixture across six runs of ONE build, with the chain coming out 2/5, 3/5, 4/5 and 5/5.
/// Every other bar of that suite was stable to two decimal places over the same runs, which is
/// what identifies the shove as the variable. <c>--phys-shove</c> is the fix: the suite says how
/// fast, and <c>PropManager.ServerNudgeLoose</c> — REACH-1's existing hook — does it.</para>
///
/// <para><b>What these tests are for.</b> The flag's whole value is that the number in the suite
/// is the number the server uses; a parse that silently dropped the sign of a component, or
/// swallowed the second entry of a list, would hand the fixture a different experiment while
/// every log line still read as if it had worked. The drop-on-malformed rule is pinned for the
/// same reason it is pinned on <c>--seed-test-props</c>: a shove defaulted to zero stages nothing
/// and the suite then reports a physics failure about its own command line.</para>
/// </summary>
public class PhysShoveOptionTests
{
    /// <summary>The ordinary case: one entry, read back component for component.</summary>
    [Fact]
    public void OneShove_ParsesIdVelocityAndTime()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--phys-shove", "6,1.0,0,-0.5,12.5" });

        (int propId, Vector3 velocity, double atSec) = Assert.Single(o.PhysShoves);
        Assert.Equal(6, propId);
        Assert.Equal(1.0f, velocity.X, 1e-4f);
        Assert.Equal(0f, velocity.Y, 1e-4f);
        Assert.Equal(-0.5f, velocity.Z, 1e-4f);
        Assert.Equal(12.5, atSec, 4);
    }

    /// <summary>Several shoves on one line, in order. The row and the can are two separate events
    /// in one run, so a parse that kept only the first would stage half the suite.</summary>
    [Fact]
    public void SeveralShoves_AllParseInOrder()
    {
        LaunchOptions o = LaunchOptions.Parse(new[]
        {
            "--phys-shove", "1,2.5,0,0,30.0;6,1.0,0,0,38.0"
        });

        Assert.Equal(2, o.PhysShoves.Count);
        Assert.Equal(1, o.PhysShoves[0].PropId);
        Assert.Equal(2.5f, o.PhysShoves[0].Velocity.X, 1e-4f);
        Assert.Equal(30.0, o.PhysShoves[0].AtSec, 4);
        Assert.Equal(6, o.PhysShoves[1].PropId);
        Assert.Equal(1.0f, o.PhysShoves[1].Velocity.X, 1e-4f);
        Assert.Equal(38.0, o.PhysShoves[1].AtSec, 4);
    }

    /// <summary>The same prop, shoved twice on one run, survives the parse. <c>StepPhysShove</c>
    /// keys its fired-once set on the ENTRY INDEX rather than on the prop id precisely so this
    /// stays possible; the parse has to hand it two entries for that to mean anything.</summary>
    [Fact]
    public void TheSameProp_MayBeShovedTwice()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--phys-shove", "3,1,0,0,10;3,-1,0,0,20" });

        Assert.Equal(2, o.PhysShoves.Count);
        Assert.Equal(3, o.PhysShoves[0].PropId);
        Assert.Equal(3, o.PhysShoves[1].PropId);
        Assert.Equal(1f, o.PhysShoves[0].Velocity.X, 1e-4f);
        Assert.Equal(-1f, o.PhysShoves[1].Velocity.X, 1e-4f);
    }

    /// <summary>A malformed entry is DROPPED and its neighbours are not — the
    /// <c>--seed-test-props</c> rule, restated here because the consequence is worse: a shove
    /// silently defaulted to (0,0,0) at t=0 is a fixture that stages nothing at all.</summary>
    [Theory]
    [InlineData("6,1,0,0")]              // four fields: no time
    [InlineData("six,1,0,0,12")]         // a name where the id goes
    [InlineData("6,fast,0,0,12")]        // a word where a component goes
    [InlineData("6,1,0,0,soon")]         // a word where the time goes
    [InlineData("")]                     // nothing at all
    public void AMalformedEntry_IsDroppedRatherThanDefaulted(string bad)
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--phys-shove", bad });
        Assert.Empty(o.PhysShoves);

        // ...and it does not take the well-formed entry beside it with it.
        LaunchOptions mixed = LaunchOptions.Parse(new[] { "--phys-shove", bad + ";6,1,0,0,12" });
        (int propId, Vector3 velocity, double atSec) = Assert.Single(mixed.PhysShoves);
        Assert.Equal(6, propId);
        Assert.Equal(1f, velocity.X, 1e-4f);
        Assert.Equal(12.0, atSec, 4);
    }

    /// <summary>Absent by default, which is what makes this flag free for every launch that is
    /// not a suite: <c>StepPhysShove</c> returns on an empty list before it touches its clock.
    /// </summary>
    [Fact]
    public void ALaunchThatNamesNoShove_HasNone()
    {
        Assert.Empty(LaunchOptions.Parse(new string[0]).PhysShoves);
        Assert.Empty(LaunchOptions.Parse(new[] { "--server", "--port", "7916" }).PhysShoves);
    }
}
