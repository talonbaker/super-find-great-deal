using Godot;
using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// The depth-to-state machine and the geometry it reads (lake-water contract §3, §4).
///
/// These are engine-free by construction: <see cref="WaterGeometry"/> takes floats and
/// <c>Vector3</c>s and returns enums, with no node, no scene and no physics query anywhere, so
/// the whole state machine is provable here rather than only observable in a running game.
/// </summary>
public class WaterGeometryTests
{
    private static Vector3 InLake(float x, float y) => new(x, y, 0f);

    // --- Depth ---------------------------------------------------------------------------

    [Fact]
    public void Depth_IsWaterSurfaceMinusFeet()
    {
        Assert.Equal(1.42f, WaterGeometry.DepthAt(InLake(-70f, -2f)), precision: 4);
    }

    [Fact]
    public void Depth_EastOfShore_IsAlwaysDry_EvenDeepUnderground()
    {
        // The water plane is a LAKE fact, not a global sea level. A cellar, a pit or a ravine
        // anywhere else on the map sits far below WaterY and must never read as submerged —
        // this is the check that stops the whole contract from turning into "the map floods
        // below y = -0.58".
        Assert.True(WaterGeometry.DepthAt(new Vector3(WaterGeometry.ShoreX + 0.01f, -40f, 0f)) <= 0f);
        Assert.Equal(WaterState.Dry,
            WaterGeometry.ResolveAt(WaterState.Dry, new Vector3(0f, -40f, 0f)));
    }

    [Fact]
    public void Depth_AtExactlyTheShoreLine_IsInsideTheLakeAndTwoCentimetresDeep()
    {
        // Spec §3.1: bed height at SHORE_X is -0.60 against a surface at -0.58. That 0.02 m is
        // what makes the lateral cut at the shoreline continuous in STATE even though it is a
        // hard edge in geometry.
        float depth = WaterGeometry.DepthAt(InLake(WaterGeometry.ShoreX, -0.60f));
        Assert.Equal(0.02f, depth, precision: 4);
        Assert.Equal(WaterState.Dry, WaterGeometry.Resolve(WaterState.Dry, depth));
    }

    [Fact]
    public void Depth_NonFinitePosition_IsDryRatherThanNaN()
    {
        Assert.True(WaterGeometry.DepthAt(new Vector3(float.NaN, -3f, 0f)) <= 0f);
        Assert.True(WaterGeometry.DepthAt(new Vector3(-70f, float.NaN, 0f)) <= 0f);
    }

    // --- The state machine, including every boundary (MECHANICS-BIBLE §1) --------------------

    [Theory]
    // From Dry: needs threshold + band to deepen.
    [InlineData(WaterState.Dry, 0.00f, WaterState.Dry)]
    [InlineData(WaterState.Dry, 0.20f, WaterState.Dry)]   // exactly the threshold: still Dry
    [InlineData(WaterState.Dry, 0.24f, WaterState.Dry)]   // inside the band: still Dry
    [InlineData(WaterState.Dry, 0.25f, WaterState.Wading)] // threshold + band: crosses
    [InlineData(WaterState.Dry, 1.15f, WaterState.Swimming)] // teleported into deep water
    // From Wading: both directions.
    [InlineData(WaterState.Wading, 0.16f, WaterState.Wading)]
    [InlineData(WaterState.Wading, 0.15f, WaterState.Dry)]
    [InlineData(WaterState.Wading, 1.10f, WaterState.Wading)] // exactly the threshold: still Wading
    [InlineData(WaterState.Wading, 1.14f, WaterState.Wading)]
    [InlineData(WaterState.Wading, 1.15f, WaterState.Swimming)]
    // From Swimming: both directions, including the lifted-clean-out case the shore recovery uses.
    [InlineData(WaterState.Swimming, 1.06f, WaterState.Swimming)]
    [InlineData(WaterState.Swimming, 1.05f, WaterState.Wading)]
    [InlineData(WaterState.Swimming, 0.15f, WaterState.Dry)]
    [InlineData(WaterState.Swimming, -5.0f, WaterState.Dry)]
    public void Resolve_EveryBoundary(WaterState previous, float depth, WaterState expected)
    {
        Assert.Equal(expected, WaterGeometry.Resolve(previous, depth));
    }

    [Fact]
    public void Resolve_NonFiniteDepth_IsDry()
    {
        Assert.Equal(WaterState.Dry, WaterGeometry.Resolve(WaterState.Swimming, float.NaN));
        Assert.Equal(WaterState.Dry, WaterGeometry.Resolve(WaterState.Swimming, float.PositiveInfinity));
    }

    [Fact]
    public void Resolve_CorruptPreviousState_FoldsToDryRatherThanFallingThrough()
    {
        // Reachable only from a doctored wire byte. NetCodec already folds those, but the state
        // machine must be total on its own rather than trusting a caller to have sanitised.
        Assert.Equal(WaterState.Dry, WaterGeometry.Resolve((WaterState)99, 3f));
    }

    // --- Hysteresis: the defect it exists to prevent, measured ---------------------------------

    [Fact]
    public void Hysteresis_ParkedExactlyOnAThreshold_NeverStrobes()
    {
        // The failure mode without a deadband: a camper settled at exactly the wade depth
        // alternates Dry/Wading every physics tick, which would restart the entry splash 60
        // times a second and flicker the speed multiplier.
        foreach (float threshold in new[] { WaterGeometry.WadeDepthM, WaterGeometry.SwimDepthM })
        {
            WaterState state = WaterState.Dry;
            for (int i = 0; i < 600; i++)
                state = WaterGeometry.Resolve(state, threshold);
            WaterState settled = state;
            for (int i = 0; i < 600; i++)
            {
                state = WaterGeometry.Resolve(state, threshold);
                Assert.Equal(settled, state);
            }
        }
    }

    [Fact]
    public void Hysteresis_PositiveControl_TheSameLoopWithoutABandWouldStrobe()
    {
        // A test that only ever asserts "absent" proves nothing until it is shown able to report
        // "present". This reproduces the un-banded machine and asserts it DOES strobe, which is
        // what makes the assertion above meaningful rather than vacuous.
        static WaterState Unbanded(float depth) => depth >= WaterGeometry.WadeDepthM
            ? WaterState.Wading
            : WaterState.Dry;

        // Physically, a body settling onto the bed jitters by a hair either side of the contact
        // point. Un-banded, an epsilon of jitter flips the state; banded, it cannot.
        const float jitter = 0.01f;
        var unbanded = new HashSet<WaterState>();
        var banded = new HashSet<WaterState>();
        WaterState carried = WaterState.Dry;
        for (int i = 0; i < 40; i++)
        {
            float depth = WaterGeometry.WadeDepthM + (i % 2 == 0 ? jitter : -jitter);
            unbanded.Add(Unbanded(depth));
            carried = WaterGeometry.Resolve(carried, depth);
            banded.Add(carried);
        }
        Assert.Equal(2, unbanded.Count);  // the diagnostic CAN report "present"
        Assert.Single(banded);            // ...and reports "absent" only because it is absent
    }

    // --- The cold curve (spec §5) ----------------------------------------------------------------

    [Theory]
    [InlineData(0f, 180f)]
    [InlineData(-45f, 180f)]
    [InlineData(-62.9f, 180f)]
    [InlineData(-63f, 180f)]      // the rope: ramp start
    [InlineData(-85f, 45f)]       // ramp end
    [InlineData(-85.001f, 20f)]   // past the ramp: the documented step
    [InlineData(-150f, 20f)]
    public void TimeToFullChill_MatchesTheSpecTable(float x, float expected)
    {
        Assert.Equal(expected, WaterGeometry.TimeToFullChillSec(x), precision: 2);
    }

    [Fact]
    public void TimeToFullChill_RampIsMonotonicAndHitsTheMidpoint()
    {
        float previous = float.MaxValue;
        for (float x = WaterGeometry.RopeX; x >= WaterGeometry.ColdRampEndX; x -= 0.25f)
        {
            float t = WaterGeometry.TimeToFullChillSec(x);
            Assert.True(t <= previous, $"cold curve went backwards at x={x}");
            previous = t;
        }
        float mid = (WaterGeometry.RopeX + WaterGeometry.ColdRampEndX) * 0.5f;
        Assert.Equal(112.5f, WaterGeometry.TimeToFullChillSec(mid), precision: 2);
    }

    [Fact]
    public void TimeToFullChill_NonFiniteX_FallsBackToTheGentlestRate()
    {
        Assert.Equal(WaterGeometry.TimeToFullInsideRopeSec,
            WaterGeometry.TimeToFullChillSec(float.NaN), precision: 2);
    }

    // --- The shoreline ----------------------------------------------------------------------------

    [Fact]
    public void NearestShorePoint_IsDueEastAtTheSameZ_AndOnTheBank()
    {
        Vector3 shore = WaterGeometry.NearestShorePoint(new Vector3(-120f, -6f, 37f));
        Assert.Equal(37f, shore.Z, precision: 3);
        Assert.True(shore.X > WaterGeometry.ShoreX,
            "a recovery must land east of the shoreline, not on it");
    }

    [Fact]
    public void NearestShorePoint_ClampsZIntoTheWorld()
    {
        Vector3 shore = WaterGeometry.NearestShorePoint(new Vector3(-100f, -6f, 9_999f));
        Assert.True(Math.Abs(shore.Z) < WaterGeometry.MapHalf);
    }

    [Fact]
    public void NearestShorePoint_NonFiniteInput_IsStillAValidPoint()
    {
        Vector3 shore = WaterGeometry.NearestShorePoint(new Vector3(float.NaN, float.NaN, float.NaN));
        Assert.True(float.IsFinite(shore.X) && float.IsFinite(shore.Y) && float.IsFinite(shore.Z));
    }

    // --- Movement gating (spec §4) -------------------------------------------------------------------

    [Fact]
    public void SpeedMultipliers_MatchTheSpecTable()
    {
        Assert.Equal(1f, WaterGeometry.SpeedMultiplierFor(WaterState.Dry), precision: 4);
        Assert.Equal(0.55f, WaterGeometry.SpeedMultiplierFor(WaterState.Wading), precision: 4);
        Assert.Equal(0.40f, WaterGeometry.SpeedMultiplierFor(WaterState.Swimming), precision: 4);
    }

    [Fact]
    public void SprintIsDeniedInAnyWater_JumpOnlyWhileSwimming()
    {
        Assert.True(WaterGeometry.SprintAllowed(WaterState.Dry));
        Assert.False(WaterGeometry.SprintAllowed(WaterState.Wading));
        Assert.False(WaterGeometry.SprintAllowed(WaterState.Swimming));

        Assert.True(WaterGeometry.JumpAllowed(WaterState.Dry));
        Assert.True(WaterGeometry.JumpAllowed(WaterState.Wading));
        Assert.False(WaterGeometry.JumpAllowed(WaterState.Swimming));
    }

    [Fact]
    public void SwimLine_IsDeepEnoughToStaySwimming()
    {
        // If the held swim depth were inside the hysteresis band, holding it would immediately
        // resolve back to Wading and the camper would strobe between swimming and standing —
        // the exact bug the band exists to prevent, reintroduced through the back door.
        float depthAtSwimLine = WaterGeometry.WaterY - WaterGeometry.SwimLineY;
        Assert.True(depthAtSwimLine > WaterGeometry.SwimDepthM + WaterGeometry.HysteresisM,
            $"swim line holds at depth {depthAtSwimLine}, inside the hysteresis band");
        Assert.Equal(WaterState.Swimming,
            WaterGeometry.Resolve(WaterState.Swimming, depthAtSwimLine));
    }
}
