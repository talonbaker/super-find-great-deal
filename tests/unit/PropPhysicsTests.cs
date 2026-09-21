using Godot;
using MpFoundation.Game.Props;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PHYS-1 (2026-09-20). The arithmetic behind ruling P1 (wake on contact) and P2 (bounded
/// energy), taken branch by branch with no engine present.
///
/// <para>The bars these pin are Talon's two sentences in numbers: <i>"those boxes should fall
/// over like dominoes"</i> — a held crate at the browse pace must hand a cereal box enough speed
/// to tip it — and <i>"they won't freak out and make other objects jump around randomly"</i> —
/// nothing this build hands a prop may exceed 3 m/s.</para>
/// </summary>
public class PropPhysicsTests
{

    /// <summary>xunit 2.4's <c>Assert.Equal(float, float, int)</c> is ambiguous against its
    /// (double, double, int) overload, so every comparison in this file goes through one
    /// widening helper rather than a cast at each call site.</summary>
    private static void Near(float expected, float actual, int digits) =>
        Assert.Equal((double)expected, (double)actual, digits);
    // --- P2: the clamp ------------------------------------------------------------------------

    [Fact]
    public void AVelocityInsideTheBar_IsNotTouched()
    {
        var v = new Vector3(1.5f, -0.8f, 1.5f);
        Assert.False(PropPhysics.ClampVelocity(v, PropPhysics.MaxPropSpeedMps, out Vector3 outV));
        Assert.Equal(v, outV);
    }

    [Fact]
    public void HorizontalSpeedAboveTheBar_IsClampedAndReported()
    {
        var v = new Vector3(10f, 0f, 0f);
        Assert.True(PropPhysics.ClampVelocity(v, PropPhysics.MaxPropSpeedMps, out Vector3 outV));
        Near(PropPhysics.MaxPropSpeedMps, outV.Length(), 3);
    }

    [Fact]
    public void ClampingHorizontalKeepsTheDirection()
    {
        var v = new Vector3(6f, 0f, 8f); // length 10, heading 0.6/0.8
        PropPhysics.ClampVelocity(v, PropPhysics.MaxPropSpeedMps, out Vector3 outV);
        Near(0.6f, outV.X / outV.Length(), 3);
        Near(0.8f, outV.Z / outV.Length(), 3);
    }

    /// <summary><b>The one that keeps the clamp honest.</b> A prop knocked off a 1.5 m shelf is
    /// doing 5.4 m/s by the time it lands. Clamping that to 3 would play every fall in slow
    /// motion — a "fix" the player would read as the physics being broken, which is the defect
    /// this packet exists to remove rather than to add.</summary>
    [Fact]
    public void GravityIsNotAnInteraction_AFallingPropIsNotSlowedToTheBar()
    {
        var falling = new Vector3(0f, -5.4f, 0f);
        Assert.False(PropPhysics.ClampVelocity(falling, PropPhysics.MaxPropSpeedMps,
            out Vector3 outV));
        Near(-5.4f, outV.Y, 3);
    }

    [Fact]
    public void ADownwardSpeedPastTerminal_IsStillCaught()
    {
        var v = new Vector3(0f, -40f, 0f);
        Assert.True(PropPhysics.ClampVelocity(v, PropPhysics.MaxPropSpeedMps, out Vector3 outV));
        Near(-PropPhysics.MaxPropFallSpeedMps, outV.Y, 3);
    }

    /// <summary>UP is an interaction — nothing in a supermarket launches itself — so the bar
    /// applies to it even though the opposite sign gets the terminal allowance.</summary>
    [Fact]
    public void UpwardSpeedAboveTheBar_IsClamped()
    {
        var v = new Vector3(0f, 9f, 0f);
        Assert.True(PropPhysics.ClampVelocity(v, PropPhysics.MaxPropSpeedMps, out Vector3 outV));
        Near(PropPhysics.MaxPropSpeedMps, outV.Y, 3);
    }

    [Fact]
    public void AThrowRidesItsOwnCap()
    {
        var v = new Vector3(7.5f, 3.2f, 0f);
        Assert.False(PropPhysics.ClampVelocity(v, PropPhysics.ThrowSpeedCapMps, out _));
        Assert.True(PropPhysics.ClampVelocity(v, PropPhysics.MaxPropSpeedMps, out _));
    }

    [Fact]
    public void AThrowsEnergyIsSpentWhenItsHorizontalSpeedIsBackInsideTheBar()
    {
        Assert.False(PropPhysics.ThrowEnergySpent(new Vector3(7.5f, 0f, 0f)));
        Assert.True(PropPhysics.ThrowEnergySpent(new Vector3(2.0f, 0f, 0f)));
    }

    /// <summary>A throw that is mostly FALLING has not spent its launch energy — the arm of the
    /// arc where the horizontal component is still 6 m/s and the vertical is -5. Reading the
    /// whole speed would latch this correctly, but reading the whole speed on the way UP
    /// (2 m/s forward, 3.2 m/s up) would refuse to latch forever; the horizontal component is
    /// the one the launch actually wrote.</summary>
    [Fact]
    public void AThrowMidArc_IsJudgedOnItsHorizontalComponent()
    {
        Assert.False(PropPhysics.ThrowEnergySpent(new Vector3(6f, -5f, 0f)));
        Assert.True(PropPhysics.ThrowEnergySpent(new Vector3(2f, -5f, 0f)));
    }

    [Fact]
    public void SpinInsideTheCeiling_IsNotTouched()
    {
        var w = new Vector3(0f, 28f, 0f); // a can rolling at 1 m/s on a 35 mm radius
        Assert.False(PropPhysics.ClampSpin(w, out Vector3 outW));
        Assert.Equal(w, outW);
    }

    [Fact]
    public void ASolverExplosionsSpin_IsCaught()
    {
        Assert.True(PropPhysics.ClampSpin(new Vector3(0f, 400f, 0f), out Vector3 outW));
        Near(PropPhysics.MaxPropSpinRadPerSec, outW.Length(), 3);
    }

    // --- P1: the wake -------------------------------------------------------------------------

    [Fact]
    public void ABrushBelowTheThreshold_WakesNothing()
    {
        Assert.False(PropPhysics.ShouldWake(0.1f));
        Assert.False(PropPhysics.ShouldWake(PropPhysics.WakeSpeedThresholdMps - 0.01f));
        Assert.True(PropPhysics.ShouldWake(PropPhysics.WakeSpeedThresholdMps));
    }

    /// <summary><b>The domino bar as arithmetic.</b> A 1 kg crate carried at the browse pace
    /// (2.4 m/s) into a 0.4 kg cereal box: the box must leave with enough speed to tip, and with
    /// the bar left unspent.</summary>
    [Fact]
    public void AHeldCrateAtTheBrowsePace_HandsACerealBoxARealShove()
    {
        float v = PropPhysics.WakeSpeed(moverMassKg: 1.0f, targetMassKg: 0.4f,
            approachSpeedMps: 2.4f);
        Assert.InRange(v, 1.5f, PropPhysics.MaxPropSpeedMps);
        Near(2.057f, v, 2);
    }

    [Fact]
    public void TwoEqualCans_SplitTheShoveTheWayAnInelasticHitDoes()
    {
        // (1 + 0.2) * 0.35 / 0.70 * 1.0 = 0.6
        Near(0.6f, PropPhysics.WakeSpeed(0.35f, 0.35f, 1.0f), 3);
    }

    [Fact]
    public void AHeavyTargetTakesLessThanALightOne()
    {
        float lightTarget = PropPhysics.WakeSpeed(1.0f, 0.25f, 2.4f);
        float heavyTarget = PropPhysics.WakeSpeed(1.0f, 1.0f, 2.4f);
        Assert.True(lightTarget > heavyTarget);
    }

    /// <summary>P2's real guarantee: the clamp is at the IMPULSE, so nothing this build does to
    /// a prop can hand it more than the bar even before the per-tick backstop runs.</summary>
    [Fact]
    public void NoContactCanEverHandAPropMoreThanTheBar()
    {
        Near(PropPhysics.MaxPropSpeedMps, PropPhysics.WakeSpeed(1000f, 0.01f, 100f), 3);
    }

    [Fact]
    public void SeparatingBodiesWakeNothing()
    {
        Assert.Equal(0f, PropPhysics.WakeSpeed(1.0f, 0.4f, -2.4f));
        Assert.Equal(0f, PropPhysics.WakeSpeed(1.0f, 0.4f, 0f));
    }

    [Fact]
    public void AMasslessMoverStillPushes()
    {
        // A kinematic body a level author never weighed reads 0 kg; treat it as the target's
        // equal rather than as a ghost that cannot move anything.
        Assert.True(PropPhysics.WakeSpeed(0f, 0.4f, 2.4f) > 0f);
    }

    [Fact]
    public void TheImpulseIsMomentum_MassTimesTheWakeSpeed()
    {
        Vector3 j = PropPhysics.ContactImpulse(new Vector3(1f, 0f, 0f), 1.0f, 0.4f, 2.4f);
        Near(PropPhysics.WakeSpeed(1.0f, 0.4f, 2.4f) * 0.4f, j.Length(), 3);
        Near(1f, j.Normalized().X, 3);
    }

    [Fact]
    public void TheImpulseNormalisesWhateverDirectionItIsHanded()
    {
        Vector3 j = PropPhysics.ContactImpulse(new Vector3(37f, 0f, 0f), 1.0f, 0.4f, 2.4f);
        Near(PropPhysics.WakeSpeed(1.0f, 0.4f, 2.4f) * 0.4f, j.Length(), 3);
    }

    [Fact]
    public void ADegenerateDirectionOrASeparatingContact_IsNoImpulseAtAll()
    {
        Assert.Equal(Vector3.Zero, PropPhysics.ContactImpulse(Vector3.Zero, 1f, 0.4f, 2.4f));
        Assert.Equal(Vector3.Zero, PropPhysics.ContactImpulse(Vector3.Right, 1f, 0.4f, -1f));
    }

    // --- the approach speed, which is what makes a graze different from a shove ----------------

    [Fact]
    public void HeadOnApproach_IsTheWholeSpeed()
    {
        float v = PropPhysics.ApproachSpeed(new Vector3(2.4f, 0f, 0f), Vector3.Zero,
            new Vector3(1f, 0f, 0f));
        Near(2.4f, v, 3);
    }

    /// <summary><b>Walking ALONG a shelf face must not scatter it.</b> This is the property the
    /// threshold leans on: a graze contributes almost nothing however fast the mover is
    /// going.</summary>
    [Fact]
    public void AGrazeAlongASurface_IsNotAShove()
    {
        // Travelling down the aisle at the browse pace, shelf normal pointing sideways.
        float v = PropPhysics.ApproachSpeed(new Vector3(0f, 0f, 2.4f), Vector3.Zero,
            new Vector3(1f, 0f, 0f));
        Near(0f, v, 3);
        Assert.False(PropPhysics.ShouldWake(v));
    }

    [Fact]
    public void ApproachIsRelative_APropThatIsRunningAwayIsNotHit()
    {
        float v = PropPhysics.ApproachSpeed(new Vector3(2.4f, 0f, 0f), new Vector3(2.4f, 0f, 0f),
            new Vector3(1f, 0f, 0f));
        Near(0f, v, 3);
    }

    [Fact]
    public void ADegenerateNormalIsNoApproachAtAll() =>
        Assert.Equal(0f, PropPhysics.ApproachSpeed(Vector3.One, Vector3.Zero, Vector3.Zero));
}
