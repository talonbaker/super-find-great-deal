using Godot;
using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// <b>WATER-3: the lake is a finite volume, and this is the proof.</b>
///
/// <para>Talon, 2026-08-29: <i>"the water outside the map, around the green zone. If the player
/// falls off the map this will allow the player to continue swimming as if there is nothing there
/// like floating in air invisible water."</i> The cause was that
/// <see cref="WaterGeometry.DepthAt(Vector3)"/> tested a half-plane — <c>x &lt;= ShoreX</c> at any
/// z, any distance west and any depth — so "the lake" had two edges and needed four plus a floor.
/// </para>
///
/// <para>Every test here uses the <b>named-lake overloads</b>, never
/// <see cref="WaterGeometry.ActiveLake"/>: xUnit runs classes in parallel, and a test that mutated
/// a static world constant would make every other water test in the run depend on scheduling.</para>
/// </summary>
public class LakeFootprintTests
{
    private static readonly WaterGeometry.LakeFootprint Camp = WaterGeometry.CampLake;
    private static readonly WaterGeometry.LakeFootprint Green = WaterGeometry.BubbleTestLake;

    private static Vector3 At(float x, float y, float z) => new(x, y, z);

    // --- Talon's defect, named ------------------------------------------------------------

    /// <summary><b>The bug, as a test.</b> Off the western end of the bubble test (its green
    /// section stops at x = −140) and well under the water plane: the half-plane called this
    /// swimming, forever, in nothing rendered. It must be dry, so the body falls and
    /// <c>RespawnService</c>'s boundary scan claims it as OffTheEdge or Void.</summary>
    [Theory]
    [InlineData(-160f, -20f, 0f)]     // straight west, 20 m down
    [InlineData(-141f, -0.6f, 0f)]    // one metre past the section edge, one centimetre under
    [InlineData(-200f, -60f, 0f)]     // most of the way to the off-map radius
    [InlineData(-95f, -20f, 60f)]     // due north of the lake, off the section's z edge
    [InlineData(-95f, -20f, -60f)]    // due south, same
    [InlineData(-95f, -800f, 0f)]     // straight down the lake's own column, into the void
    public void OffTheMapIsDryAndFalling_NotSwimming(float x, float y, float z)
    {
        Assert.True(WaterGeometry.DepthAt(At(x, y, z), Green) <= 0f);
        Assert.Equal(WaterState.Dry, WaterGeometry.ResolveAt(WaterState.Swimming, At(x, y, z), Green));
        Assert.False(WaterGeometry.IsSubmerged(At(x, y, z), Green));
    }

    /// <summary>The other half of the same claim: inside the bubble test's lake, the answers are
    /// the ones the water contract has always given.</summary>
    [Fact]
    public void InsideTheBubbleTestLake_TheContractIsUnchanged()
    {
        var centre = At(WaterGeometry.BubbleTestLakeCentreX, WaterGeometry.SwimLineY,
                        WaterGeometry.BubbleTestLakeCentreZ);
        Assert.Equal(WaterGeometry.SwimSubmersionM, WaterGeometry.DepthAt(centre, Green), precision: 4);
        Assert.Equal(WaterState.Swimming, WaterGeometry.ResolveAt(WaterState.Dry, centre, Green));
        Assert.True(WaterGeometry.IsSubmerged(centre, Green));
    }

    // --- The disc's boundary (MECHANICS-BIBLE §1: state the bound) --------------------------

    /// <summary>The radial bound is INCLUSIVE: a body exactly on the rim is in the lake. Checked on
    /// all four cardinal rays so a sign error in one axis cannot hide behind another.</summary>
    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    public void TheDiscRimIsInside_AndOneMillimetrePastItIsNot(float ux, float uz)
    {
        float r = WaterGeometry.BubbleTestLakeRadiusM;
        float cx = WaterGeometry.BubbleTestLakeCentreX, cz = WaterGeometry.BubbleTestLakeCentreZ;
        float y = WaterGeometry.SwimLineY;

        Assert.True(WaterGeometry.DepthAt(At(cx + ux * r, y, cz + uz * r), Green) > 0f);
        Assert.True(WaterGeometry.DepthAt(At(cx + ux * (r + 0.001f), y, cz + uz * (r + 0.001f)),
                                          Green) <= 0f);
    }

    /// <summary>A disc, not its bounding square. The corner of the bounding box is
    /// <c>r * sqrt(2)</c> ≈ 24 m from the centre — dry land in GreenHills — and a box-only test
    /// would call it lake.</summary>
    [Fact]
    public void TheDiscIsRound_NotItsBoundingBox()
    {
        float r = WaterGeometry.BubbleTestLakeRadiusM;
        var corner = At(WaterGeometry.BubbleTestLakeCentreX + r, WaterGeometry.SwimLineY,
                        WaterGeometry.BubbleTestLakeCentreZ + r);
        Assert.True(WaterGeometry.DepthAt(corner, Green) <= 0f);
        Assert.False(WaterGeometry.InLakeRegion(corner, Green));
    }

    /// <summary>The disc is centred on GreenHills' section anchor. The centre is mirrored in
    /// <c>WaterGeometry</c> because the water contract must not depend on a level; this is the
    /// guard that makes the mirror safe.</summary>
    [Fact]
    public void TheDiscIsCentredOnTheGreenSectionAnchor()
    {
        Assert.Equal(-95f, WaterGeometry.BubbleTestLakeCentreX);
        Assert.Equal(0f, WaterGeometry.BubbleTestLakeCentreZ);
    }

    // --- The floor: a lake has a bottom ------------------------------------------------------

    /// <summary>The floor bound is INCLUSIVE at the floor and excludes everything below it. A body
    /// under the lakebed has clipped through the world; it falls, it does not swim.</summary>
    [Fact]
    public void BelowTheFloorIsNotTheLake()
    {
        float cx = WaterGeometry.BubbleTestLakeCentreX, cz = WaterGeometry.BubbleTestLakeCentreZ;
        Assert.True(WaterGeometry.DepthAt(At(cx, Green.FloorY, cz), Green) > 0f);
        Assert.True(WaterGeometry.DepthAt(At(cx, Green.FloorY - 0.001f, cz), Green) <= 0f);
        Assert.True(WaterGeometry.DepthAt(At(-70f, Camp.FloorY, 0f), Camp) > 0f);
        Assert.True(WaterGeometry.DepthAt(At(-70f, Camp.FloorY - 0.001f, 0f), Camp) <= 0f);
    }

    /// <summary>Both floors clear the deepest bed their world authors, with room for the
    /// sputter-out's 1.8 m sink from the swim line — otherwise a capsizing camper would drop out of
    /// the water mid-sequence and the sink would resolve as a fall.</summary>
    [Fact]
    public void EachFloorClearsItsOwnLakebedAndTheSputterSink()
    {
        float sunk = WaterGeometry.SwimLineY - WaterGeometry.GoUnderSec * WaterGeometry.SinkRate;
        Assert.True(sunk > Green.FloorY, $"the sputter sinks to {sunk}, under green's floor.");
        Assert.True(sunk > Camp.FloorY, $"the sputter sinks to {sunk}, under camp's floor.");

        // camp_sites.BED_DEEP is −6.50; the bubble test bake's BASIN_FLOOR is −3.85.
        Assert.True(Camp.FloorY < -6.5f);
        Assert.True(Green.FloorY < -3.85f);
    }

    // --- The camp lake keeps the behaviour it had --------------------------------------------

    /// <summary>
    /// <b>The regression guard for every world that is not the bubble test.</b> Inside the camp map
    /// the new footprint must agree with the old half-plane at every point, or WATER-3 has quietly
    /// retuned camp's shoreline while claiming to bound it. Swept on a 5 m grid over the whole map
    /// and over the full authored depth range.
    /// </summary>
    [Fact]
    public void InsideTheCampMap_TheFootprintAgreesWithTheOldHalfPlane()
    {
        int compared = 0;
        for (float x = -WaterGeometry.MapHalf; x <= WaterGeometry.MapHalf; x += 5f)
        for (float z = -WaterGeometry.MapHalf; z <= WaterGeometry.MapHalf; z += 5f)
        for (float y = -6.5f; y <= 4f; y += 0.5f)
        {
            var p = At(x, y, z);
            float old = x > WaterGeometry.ShoreX ? -1f : WaterGeometry.WaterY - y;
            float now = WaterGeometry.DepthAt(p, Camp);
            Assert.Equal(old, now, precision: 4);
            compared++;
        }
        // 61 x 61 x 22 = 81,862. Asserted so a loop bound that silently collapses to nothing
        // cannot pass this as a clean sweep.
        Assert.Equal(81_862, compared);
    }

    /// <summary>What the camp footprint DOES change: the infinite part. Past the map's own edge
    /// there is no camp and no lake.</summary>
    [Theory]
    [InlineData(-151f, -2f, 0f)]
    [InlineData(-400f, -2f, 0f)]
    [InlineData(-70f, -2f, 151f)]
    [InlineData(-70f, -2f, -151f)]
    public void PastTheCampMapEdge_ThereIsNoLake(float x, float y, float z)
    {
        Assert.True(WaterGeometry.DepthAt(At(x, y, z), Camp) <= 0f);
    }

    /// <summary>The shoreline itself is unmoved: exactly ShoreX is still lake, one millimetre east
    /// is not. This is the boundary <c>WaterGeometryTests</c> pins at 0.02 m of depth.</summary>
    [Fact]
    public void TheCampShorelineIsStillInclusive()
    {
        Assert.True(WaterGeometry.DepthAt(At(WaterGeometry.ShoreX, -0.60f, 0f), Camp) > 0f);
        Assert.True(WaterGeometry.DepthAt(At(WaterGeometry.ShoreX + 0.001f, -0.60f, 0f), Camp) <= 0f);
    }

    // --- Totality: no NaN escapes -------------------------------------------------------------

    /// <summary>Every non-finite coordinate is outside every lake, on both predicates and on all
    /// three axes — z included, which the half-plane never looked at and so could never have
    /// rejected.</summary>
    [Theory]
    [InlineData(float.NaN, -2f, 0f)]
    [InlineData(-70f, float.NaN, 0f)]
    [InlineData(-70f, -2f, float.NaN)]
    [InlineData(float.PositiveInfinity, -2f, 0f)]
    [InlineData(float.NegativeInfinity, -2f, 0f)]
    [InlineData(-70f, float.NegativeInfinity, 0f)]
    [InlineData(-70f, -2f, float.PositiveInfinity)]
    public void NonFiniteIsAlwaysOutside(float x, float y, float z)
    {
        var p = At(x, y, z);
        foreach (WaterGeometry.LakeFootprint lake in new[] { Camp, Green })
        {
            Assert.True(WaterGeometry.DepthAt(p, lake) <= 0f);
            Assert.False(WaterGeometry.IsSubmerged(p, lake));
            Assert.Equal(WaterState.Dry, WaterGeometry.ResolveAt(WaterState.Swimming, p, lake));
        }
    }

    // --- Which world gets which lake -----------------------------------------------------------

    [Theory]
    [InlineData("bubbletest")]
    public void TheBubbleTestWorldGetsTheDisc(string world)
    {
        Assert.True(WaterGeometry.LakeForWorld(world).RadiusM > 0f);
        Assert.Equal(WaterGeometry.BubbleTestLakeRadiusM, WaterGeometry.LakeForWorld(world).RadiusM);
    }

    /// <summary>Everything else keeps camp's lake — the same shoreline it had before WATER-3, so a
    /// world that never named a lake is not silently retuned.</summary>
    [Theory]
    [InlineData("camp")]
    [InlineData("playtest1")]
    [InlineData("playground")]
    [InlineData("hoodlab")]
    [InlineData("")]
    [InlineData(null)]
    public void EveryOtherWorldGetsTheCampLake(string? world)
    {
        WaterGeometry.LakeFootprint lake = WaterGeometry.LakeForWorld(world);
        Assert.Equal(0f, lake.RadiusM);
        Assert.Equal(WaterGeometry.ShoreX, lake.MaxX);
        Assert.Equal(WaterGeometry.CampLake.FloorY, lake.FloorY);
    }

    /// <summary>The default is camp's lake, so a headless path that never builds a world (a lab, a
    /// unit test, a self-test fixture) reads exactly what it read before.</summary>
    [Fact]
    public void TheDefaultActiveLakeIsCamp()
    {
        Assert.Equal(WaterGeometry.CampLake.MaxX, WaterGeometry.ActiveLake.MaxX);
        Assert.Equal(WaterGeometry.CampLake.FloorY, WaterGeometry.ActiveLake.FloorY);
        Assert.Equal(0f, WaterGeometry.ActiveLake.RadiusM);
    }

    // --- The footprint primitive itself ----------------------------------------------------------

    /// <summary>A box's four plan bounds are all inclusive, and each is tested on its own axis so a
    /// transposed comparison cannot pass by symmetry.</summary>
    [Fact]
    public void BoxBoundsAreInclusiveOnAllFourEdges()
    {
        WaterGeometry.LakeFootprint b =
            WaterGeometry.LakeFootprint.Box(-10f, 10f, -20f, 20f, -5f);
        Assert.True(b.ContainsPlan(At(-10f, 0f, 0f)));
        Assert.True(b.ContainsPlan(At(10f, 0f, 0f)));
        Assert.True(b.ContainsPlan(At(0f, 0f, -20f)));
        Assert.True(b.ContainsPlan(At(0f, 0f, 20f)));
        Assert.False(b.ContainsPlan(At(-10.001f, 0f, 0f)));
        Assert.False(b.ContainsPlan(At(10.001f, 0f, 0f)));
        Assert.False(b.ContainsPlan(At(0f, 0f, -20.001f)));
        Assert.False(b.ContainsPlan(At(0f, 0f, 20.001f)));
    }

    /// <summary><see cref="WaterGeometry.LakeFootprint.ContainsPlan"/> ignores height and
    /// <see cref="WaterGeometry.LakeFootprint.Contains"/> does not — the two answer different
    /// questions and the split is what lets <c>InLakeRegion</c> stay a lateral test.</summary>
    [Fact]
    public void PlanContainmentIgnoresHeight_VolumeContainmentDoesNot()
    {
        WaterGeometry.LakeFootprint b =
            WaterGeometry.LakeFootprint.Box(-10f, 10f, -10f, 10f, -5f);
        Assert.True(b.ContainsPlan(At(0f, -900f, 0f)));
        Assert.False(b.Contains(At(0f, -900f, 0f)));
        Assert.True(b.Contains(At(0f, 900f, 0f)));   // high above the water is still "the lake",
                                                     // and DepthAt turns that into a negative depth
    }

    /// <summary>A disc's own plan box is its bounding square, so the cheap rejection cannot be
    /// tighter than the radius it is meant to bracket.</summary>
    [Fact]
    public void DiscDerivesItsPlanBoxFromTheRadius()
    {
        WaterGeometry.LakeFootprint d = WaterGeometry.LakeFootprint.Disc(-95f, 0f, 17f, -6f);
        Assert.Equal(-112f, d.MinX);
        Assert.Equal(-78f, d.MaxX);
        Assert.Equal(-17f, d.MinZ);
        Assert.Equal(17f, d.MaxZ);
    }
}
