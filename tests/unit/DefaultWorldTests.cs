using MpFoundation;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>LAUNCH-1 (2026-08-28): a launch that names no world builds the bubble test.</b> The
/// movement-and-feel MVP asks players three questions about the bubble test; a Steam tester who
/// installs the build and clicks Host must land there. Before LAUNCH-1 they landed in camp — the
/// default was <c>"camp"</c> and <c>HostMenu.HostAsync</c> additionally forced <c>"camp"</c>
/// whenever <c>--world</c> was absent, so flipping either site alone would have left the other
/// one quietly overriding it (MVP integration plan §2.4).
///
/// <para><b>Why these two tests and not one.</b> The default and the override are a pair, and the
/// pair is the contract: the default has to move, and moving it must not cost the dev labs. Every
/// lab world (houselab, hoodlab, playground, camp) is reachable only through an explicit
/// <c>--world</c>, because the Practice button is gone from MainMenu and Host is the only way in.
/// So a change that pinned the new default while breaking <c>WorldExplicit</c> would pass one
/// test and make four worlds unreachable by a human — which is the exact defect the comment block
/// at <c>HostMenu.cs</c> was written to prevent, and it went unnoticed once already.</para>
///
/// <para><b>Scope, stated honestly.</b> These run without the engine, so they cover
/// <see cref="LaunchOptions"/>: the default value and the flag HostMenu's forcing reads. They do
/// not execute <c>HostMenu.HostAsync</c> itself (a Godot <c>Control</c> needs a running engine).
/// What removes the drift risk there is not this file but the shared
/// <see cref="LaunchOptions.DefaultWorld"/> symbol both sites now use; that the menu path really
/// reaches the supermarket is evidenced by a headed no-flag launch capture.</para>
/// </summary>
public class DefaultWorldTests
{
    /// <summary>A LaunchOptions nobody configured builds the supermarket — the game's world,
    /// not the CI scaffolding.</summary>
    [Fact]
    public void ANamelessLaunchBuildsTheSupermarket()
    {
        Assert.Equal("supermarket", LaunchOptions.DefaultWorld);

        // The zero-argument parse is the real Steam path: no --world anywhere on the line.
        var parsed = LaunchOptions.Parse(new string[0]);
        Assert.Equal(LaunchOptions.DefaultWorld, parsed.World);

        // False here is what makes HostMenu force the default; true would make it stand aside.
        Assert.False(parsed.WorldExplicit);
    }

    /// <summary>An explicit --world still wins, so every dev lab stays reachable.</summary>
    [Theory]
    [InlineData("camp")]
    [InlineData("playground")]
    [InlineData("houselab")]
    [InlineData("hoodlab")]
    [InlineData("open")]
    public void AnExplicitWorldStillWins(string world)
    {
        var parsed = LaunchOptions.Parse(new[] { "--world", world });

        Assert.Equal(world, parsed.World);

        // WorldExplicit is the whole gate: HostMenu overwrites World only when this is false, so
        // a regression that stopped setting it would silently drop the player into the bubble
        // test no matter what they asked for.
        Assert.True(parsed.WorldExplicit);
        Assert.NotEqual(LaunchOptions.DefaultWorld, parsed.World);
    }
}
