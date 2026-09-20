using Godot;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PROBE-1 (2026-09-20): the at-rest gate's decision, without an engine.
///
/// <para><b>What a wrong answer here costs, in both directions.</b> Too EAGER and a prop stops
/// animating mid-pop, or stops chasing a hand, or keeps an outline lit after the highlight moved
/// on — a permanent visual bug on one object in a room of two thousand, invisible to every suite
/// in the repo. Too SHY and the fix does nothing: the four writes through the engine boundary go
/// on running sixty times a second per prop, which is the bug this packet exists to remove. So
/// each gate condition gets its own fact, and each one is a case where the answer must be false.</para>
/// </summary>
public class CarryIdleTests
{
    /// <summary>A prop at rest on a shelf: frozen, nobody holding it, nothing highlighting it,
    /// the pop spring finished, the emission lerp arrived.</summary>
    private static bool Settled(
        bool homeSet = true,
        bool frozen = true,
        bool held = false,
        bool highlighted = false,
        bool outlineVisible = false,
        double thunkCooldown = 0.0,
        Vector3? visualScale = null,
        Vector3? visualScaleVel = null,
        float glow = 2.5f,
        float baseGlow = 2.5f)
        => CarryIdle.IsSettled(homeSet, frozen, held, highlighted, outlineVisible, thunkCooldown,
            visualScale ?? Vector3.One, visualScaleVel ?? Vector3.Zero, glow, baseGlow);

    [Fact]
    public void APropRestingOnAShelfIsSettled() => Assert.True(Settled());

    /// <summary>A code-built prop's authored emission is 0, not 2.5 — the gate must compare
    /// against the prop's OWN resting value and not against zero. Carryable's _baseGlow comment
    /// records the session this cost: the pulse used to lerp toward absolute 0 and erased every
    /// authored prop's bloom.</summary>
    [Fact]
    public void AZeroBaseGlowPropIsSettledAtZero() => Assert.True(Settled(glow: 0f, baseGlow: 0f));

    [Fact]
    public void AnUnfrozenPropIsNeverSettled()
    {
        // A loose prop on the server: the solver moves it under us, so ApproachSpeedMps has to
        // keep being read and the body's own position keeps changing.
        Assert.False(Settled(frozen: false));
    }

    [Fact]
    public void AHeldPropIsNeverSettled()
    {
        // The anchor chase lives in the body below the gate. A held prop that stopped ticking
        // would hang in the air where it was picked up.
        Assert.False(Settled(held: true));
    }

    [Fact]
    public void AHighlightedPropIsNeverSettled()
    {
        // The highlight BREATHES — 0.35 + 0.2 sin(6t) — so it is never at a resting value.
        Assert.False(Settled(highlighted: true));
    }

    [Fact]
    public void APropWhoseOutlineIsStillShowingIsNeverSettled()
    {
        // Highlighted goes false a frame before the outline is cleared (the clear happens in the
        // body). Gating only on Highlighted would strand the shell lit forever.
        Assert.False(Settled(outlineVisible: true));
    }

    [Fact]
    public void APropWithAThunkCooldownStillRunningIsNeverSettled()
    {
        Assert.False(Settled(thunkCooldown: 0.12));
        Assert.True(Settled(thunkCooldown: 0.0));
        Assert.True(Settled(thunkCooldown: -0.4));
    }

    [Fact]
    public void APropThatHasNotYetCapturedItsHomePositionIsNeverSettled()
    {
        // The first tick of a prop's life is the one that records the home it is recovered to.
        Assert.False(Settled(homeSet: false));
    }

    [Fact]
    public void APopSpringStillOpeningIsNotSettled()
    {
        Assert.False(Settled(visualScale: new Vector3(1.3f, 1.3f, 1.3f)));
        Assert.False(Settled(visualScale: new Vector3(0.7f, 0.7f, 0.7f)));
    }

    /// <summary><b>A spring passing THROUGH its target at speed is not finished</b>, and this is
    /// the case a scale-offset-only gate gets wrong: at the instant of the zero crossing the
    /// offset is zero and the prop is moving fastest. Freezing it there is a prop stuck at
    /// exactly the wrong size.</summary>
    [Fact]
    public void APopSpringAtItsTargetButStillMovingIsNotSettled()
    {
        Assert.False(Settled(visualScale: Vector3.One,
            visualScaleVel: new Vector3(0.9f, 0.9f, 0.9f)));
    }

    [Fact]
    public void AnEmissionLerpStillTravellingIsNotSettled()
    {
        // The lerp is 10/s toward the target and never arrives exactly, which is why the gate
        // has an epsilon at all; a tenth of an energy unit is not "arrived".
        Assert.False(Settled(glow: 2.6f, baseGlow: 2.5f));
        Assert.False(Settled(glow: 0.1f, baseGlow: 0f));
    }

    /// <summary>The epsilons admit a residual and reject anything bigger, in both directions.
    /// Pinned so a later tightening is a deliberate change and not a drift.</summary>
    [Fact]
    public void TheEpsilonsAdmitAResidualAndNoMore()
    {
        float halfScale = CarryIdle.ScaleEpsilon * 0.5f;
        Assert.True(Settled(visualScale: new Vector3(1f + halfScale, 1f, 1f)));
        Assert.False(Settled(visualScale: new Vector3(1f + CarryIdle.ScaleEpsilon * 4f, 1f, 1f)));

        float halfVel = CarryIdle.ScaleVelEpsilon * 0.5f;
        Assert.True(Settled(visualScaleVel: new Vector3(halfVel, 0f, 0f)));
        Assert.False(Settled(visualScaleVel: new Vector3(CarryIdle.ScaleVelEpsilon * 4f, 0f, 0f)));

        Assert.True(Settled(glow: 2.5f + CarryIdle.GlowEpsilon * 0.5f, baseGlow: 2.5f));
        Assert.False(Settled(glow: 2.5f + CarryIdle.GlowEpsilon * 4f, baseGlow: 2.5f));
    }

    /// <summary><b>Every single condition, alone, is enough to keep a prop ticking.</b> Stated as
    /// one fact as well as separately, because the gate is an AND chain and an AND chain that
    /// lost a term would still pass most of the tests above.</summary>
    [Fact]
    public void EveryDisturbanceAloneKeepsThePropTicking()
    {
        Assert.False(Settled(homeSet: false));
        Assert.False(Settled(frozen: false));
        Assert.False(Settled(held: true));
        Assert.False(Settled(highlighted: true));
        Assert.False(Settled(outlineVisible: true));
        Assert.False(Settled(thunkCooldown: 0.01));
        Assert.False(Settled(visualScale: new Vector3(1.2f, 1f, 1f)));
        Assert.False(Settled(visualScaleVel: new Vector3(0.5f, 0f, 0f)));
        Assert.False(Settled(glow: 3f, baseGlow: 2.5f));
        // ...and with none of them, it settles. Otherwise the nine above prove nothing.
        Assert.True(Settled());
    }
}
