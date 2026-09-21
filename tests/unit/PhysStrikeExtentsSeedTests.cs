using Godot;
using MpFoundation;
using MpFoundation.Game.Props;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>PHYS-2 (2026-09-21): three pieces of arithmetic that three live defects turned out to be.</b>
///
/// <para><b>1. Where a prop-on-prop wake lands.</b> The funnel applied every such impulse at the
/// struck prop's CENTRE, and a row of cereal boxes shoved at 2.8 m/s shuffled 3 cm each and
/// stood — <c>wake prop=2 at=2.77 -> 1.66</c>, <c>prop=3 1.65 -> 0.99</c>, <c>prop=4 0.57 ->
/// 0.34</c>, not one past 60 degrees. A domino is knocked over by its neighbour's top edge.
/// <see cref="PropPhysics.StrikePoint"/> is the mover's support point along the line of centres,
/// and these pin the two cases that matter: a toppling box leads HIGH, a sliding box leads with
/// the centre of its face.</para>
///
/// <para><b>2. What "inside the room" means for something round.</b> REACH-1's bounds test
/// swept the eight corners of the shape's local box, so a can lying on the floor at its measured
/// rest (1.2 cm into it) was inside the 2 cm slack when its box was axis-aligned and 2.6 cm
/// OUTSIDE it when the can had rolled 45 degrees — and the audit teleported it home. The same
/// run restored an authored orange 0.4 m for the same reason. <see cref="PropPhysics.RoundExtents"/>
/// does not change as a round thing rotates about its own axis, which is the whole property.</para>
///
/// <para><b>3. A can seeded on its side.</b> Laying an upright can down with a spin was measured
/// not to work (friction 0.25 skids the base out; tilt peaked at 1 degree over three runs), so
/// <c>--seed-test-props</c> grew a fifth field. The drop-on-malformed rule every other field of
/// that flag follows is pinned for it too.</para>
/// </summary>
public class PhysStrikeExtentsSeedTests
{
    private static void Near(float expected, float actual, int digits) =>
        Assert.Equal((double)expected, (double)actual, digits);

    // A cereal box: 0.19 wide (X), 0.28 tall (Y), 0.06 deep (Z) — CerealBox.tscn's numbers.
    private static readonly Vector3 BoxHalf = new(0.095f, 0.14f, 0.03f);

    // --- 1. the strike point ------------------------------------------------------------------

    [Fact]
    public void ASlidingBox_StrikesWithTheCentreOfItsLeadingFace()
    {
        // Upright, at the origin, moving +X into a neighbour at +X: the four +X corners tie and
        // their mean is the face centre, at the box's own height — a flat contact, no torque.
        var at = new Transform3D(Basis.Identity, new Vector3(0f, 0.14f, 0f));
        Vector3 strike = PropPhysics.StrikePoint(at, BoxHalf, Vector3.Right);

        Near(0.095f, strike.X, 4);
        Near(0.14f, strike.Y, 4);
        Near(0f, strike.Z, 4);
    }

    [Fact]
    public void ATopplingBox_StrikesWithItsTopFrontEdge()
    {
        // Pitched 30 degrees forward about Z (top toward +X): the leading points are the two
        // top-front corners, which sit well above the box's centre.
        var basis = Basis.FromEuler(new Vector3(0f, 0f, Mathf.DegToRad(-30f)));
        var at = new Transform3D(basis, new Vector3(0f, 0.14f, 0f));
        Vector3 strike = PropPhysics.StrikePoint(at, BoxHalf, Vector3.Right);

        // Only the two top-front corners tie, so Z averages to zero and Y is ABOVE the centre.
        Near(0f, strike.Z, 4);
        Assert.True(strike.Y > 0.14f + 0.05f,
            $"a toppling box must lead with its top edge; strike.Y={strike.Y:F3} against centre 0.140");
        Assert.True(strike.X > 0.095f, "the tipped top edge reaches past where the upright face was");
    }

    [Fact]
    public void ADegenerateLineOfCentres_FallsBackToTheMoverCentre()
    {
        var at = new Transform3D(Basis.Identity, new Vector3(1f, 2f, 3f));
        Assert.Equal(at.Origin, PropPhysics.StrikePoint(at, BoxHalf, Vector3.Zero));
    }

    // --- 2. round extents ----------------------------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(137f)]
    public void ACanRollingAboutItsOwnAxis_KeepsTheSameVerticalExtent(float rollDeg)
    {
        // A can (r 0.035, h 0.12) lying with its axis along Z, then spun about Z by any angle:
        // its extent along Y is its radius every time. This is the case the corner sweep got
        // wrong by up to sqrt(2).
        var lying = Basis.FromEuler(new Vector3(Mathf.DegToRad(90f), 0f, 0f));
        var spun = Basis.FromEuler(new Vector3(0f, 0f, Mathf.DegToRad(rollDeg))) * lying;
        Vector3 axis = spun.Y.Normalized();

        Vector3 ext = PropPhysics.RoundExtents(axis, 0.06f, 0.035f, roundEnds: false);

        Near(0.035f, ext.Y, 4);
        // And the corner sweep, for contrast, is what the old test used: at 45 degrees it says
        // 0.049 — 1.4 cm deeper than the can's real bottom, which was the whole teleport.
        Vector3 swept = PropPhysics.BoxExtents(spun, new Vector3(0.035f, 0.06f, 0.035f));
        if (rollDeg == 45f)
            Assert.True(swept.Y > 0.045f, $"the corner sweep overstates a rolled can: {swept.Y:F3}");
    }

    [Fact]
    public void AnUprightCan_ExtendsHalfItsHeightVertically()
    {
        Vector3 ext = PropPhysics.RoundExtents(Vector3.Up, 0.06f, 0.035f, roundEnds: false);
        Near(0.06f, ext.Y, 4);
        Near(0.035f, ext.X, 4);
        Near(0.035f, ext.Z, 4);
    }

    [Fact]
    public void ASphere_IsItsRadiusOnEveryAxis_HoweverItIsTurned()
    {
        // A sphere is a capsule of zero length; the axis is irrelevant and must not leak in.
        Vector3 ext = PropPhysics.RoundExtents(new Vector3(0.577f, 0.577f, 0.577f).Normalized(), 0f, 0.08f, roundEnds: true);
        Near(0.08f, ext.X, 4);
        Near(0.08f, ext.Y, 4);
        Near(0.08f, ext.Z, 4);
    }

    [Fact]
    public void BoxExtents_MatchTheCornerSweep()
    {
        var basis = Basis.FromEuler(new Vector3(0.3f, -0.7f, 1.1f));
        Vector3 ext = PropPhysics.BoxExtents(basis, BoxHalf);

        // The slow way, for the record.
        Vector3 swept = Vector3.Zero;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? -BoxHalf.X : BoxHalf.X,
                (i & 2) == 0 ? -BoxHalf.Y : BoxHalf.Y,
                (i & 4) == 0 ? -BoxHalf.Z : BoxHalf.Z);
            Vector3 p = basis * corner;
            swept = new Vector3(Mathf.Max(swept.X, Mathf.Abs(p.X)), Mathf.Max(swept.Y, Mathf.Abs(p.Y)),
                Mathf.Max(swept.Z, Mathf.Abs(p.Z)));
        }
        Near(swept.X, ext.X, 4);
        Near(swept.Y, ext.Y, 4);
        Near(swept.Z, ext.Z, 4);
    }

    // --- 3. the seed's fifth field -------------------------------------------------------------

    [Fact]
    public void ASeedWithNoFifthField_IsUpright()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--seed-test-props", "44.6,0.035,-2,can" });
        (Vector3 at, MpFoundation.Net.PropKind kind, float rollXDeg, float yawYDeg) = Assert.Single(o.SeedTestProps);
        Assert.Equal(MpFoundation.Net.PropKind.Can, kind);
        Near(44.6f, at.X, 4);
        Assert.Equal(0f, rollXDeg);
        Assert.Equal(0f, yawYDeg);
    }

    [Fact]
    public void ASeedWithASixthField_CarriesItsYaw()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--seed-test-props", "42.3,0.14,-2,box,0,90" });
        var seed = Assert.Single(o.SeedTestProps);
        Assert.Equal(0f, seed.RollXDeg);
        Assert.Equal(90f, seed.YawYDeg);
    }

    [Fact]
    public void ASeedWithAFifthField_CarriesItsRoll()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--seed-test-props", "44.6,0.035,-2,can,90;1,2,3,box" });
        Assert.Equal(2, o.SeedTestProps.Count);
        Assert.Equal(90f, o.SeedTestProps[0].RollXDeg);
        Assert.Equal(0f, o.SeedTestProps[1].RollXDeg);
    }

    [Fact]
    public void AMalformedFifthField_DropsTheEntry_NotTheRoll()
    {
        // A can "meant to lie down and quietly standing" is a fixture that lies — so the entry is
        // dropped, exactly as a malformed coordinate is, and the well-formed neighbour survives.
        LaunchOptions o = LaunchOptions.Parse(new[] { "--seed-test-props", "44.6,0.035,-2,can,ninety;1,2,3,box,0" });
        (Vector3 at, MpFoundation.Net.PropKind kind, float rollXDeg, float _) = Assert.Single(o.SeedTestProps);
        Assert.Equal(MpFoundation.Net.PropKind.Box, kind);
        Near(1f, at.X, 4);
        Assert.Equal(0f, rollXDeg);
    }
}
