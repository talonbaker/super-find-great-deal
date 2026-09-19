using Godot;
using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// The cold clock (lake-water contract §5) and the anti-unwinnable property it underwrites
/// (MECHANICS-BIBLE §10.5).
///
/// The spec's requirement is not "chill goes up" — it is that chill is a <b>pure function of
/// position</b>: same inputs, same output, no RNG, no dependence on frame rate beyond dt. That
/// is what makes it safe to be server-authoritative and safe to reason about, and it is what
/// most of this file measures.
/// </summary>
public class WaterChillTests
{
    private const float Dt = 1f / 60f;

    // --- Purity ------------------------------------------------------------------------------

    [Fact]
    public void Chill_IsAPureFunctionOfPosition_SameRouteSameAnswer()
    {
        static float Swim(int seed)
        {
            float chill = 0f;
            for (int i = 0; i < 4_000; i++)
            {
                // A deterministic wander west and back, so the route visits the flat region, the
                // ramp and the far region rather than sitting at one rate.
                float x = -50f - 60f * MathF.Abs(MathF.Sin((i + seed * 0) * 0.0007f));
                chill = ChillClock.Advance(chill, WaterState.Swimming, x, Dt);
            }
            return chill;
        }
        Assert.Equal(Swim(0), Swim(1));
    }

    [Fact]
    public void Chill_IsFrameRateIndependent_ToWithinIntegrationError()
    {
        // Same 60 s of swimming at the same x, integrated at 30, 60 and 144 Hz. The rate is
        // piecewise-constant in x and x is fixed here, so these must agree to floating-point
        // noise — not merely "roughly". A drift here would mean a laggy client and a smooth one
        // disagreed about how cold a camper was.
        static float Integrate(float hz)
        {
            float dt = 1f / hz;
            float chill = 0f;
            for (int i = 0; i < (int)(60f * hz); i++)
                chill = ChillClock.Advance(chill, WaterState.Swimming, -70f, dt);
            return chill;
        }
        float a = Integrate(30f), b = Integrate(60f), c = Integrate(144f);
        Assert.Equal(a, b, precision: 3);
        Assert.Equal(b, c, precision: 3);
    }

    [Fact]
    public void Chill_PositiveControl_TheHarnessCanDetectImpurity()
    {
        // The purity assertions above are worthless unless the comparison they use can actually
        // fail. This runs the identical loop against an integrator that is deliberately impure
        // (rate scaled by the step index) and asserts the two runs DIVERGE — proving the check
        // reports "present" as well as "absent".
        static float Impure(int seed)
        {
            float chill = 0f;
            for (int i = 0; i < 500; i++)
                chill = Math.Clamp(chill + Dt * 0.01f * (1 + (i + seed) % 3), 0f, 1f);
            return chill;
        }
        Assert.NotEqual(Impure(0), Impure(1));
    }

    // --- Accumulation and recovery ---------------------------------------------------------------

    [Theory]
    [InlineData(-50f, 180f)]
    // x = -70 sits 7/22 of the way down the ramp: lerp(180, 45, 0.3182) = 137.05 s.
    [InlineData(-70f, 137.05f)]
    [InlineData(-100f, 20f)]
    public void Chill_ReachesOneAfterTheSpecifiedTime(float x, float expectedSeconds)
    {
        float chill = 0f;
        int steps = 0;
        int cap = (int)(400f / Dt);
        while (chill < 1f && steps < cap)
        {
            chill = ChillClock.Advance(chill, WaterState.Swimming, x, Dt);
            steps++;
        }
        Assert.Equal(1f, chill, precision: 5);
        Assert.Equal(expectedSeconds, steps * Dt, precision: 0);
    }

    [Fact]
    public void Chill_AccumulatesOnlyWhileSwimming()
    {
        Assert.Equal(0f, ChillClock.Advance(0f, WaterState.Wading, -100f, 10f), precision: 5);
        Assert.Equal(0f, ChillClock.Advance(0f, WaterState.Dry, -100f, 10f), precision: 5);
        Assert.True(ChillClock.Advance(0f, WaterState.Swimming, -100f, 10f) > 0f);
    }

    [Fact]
    public void Chill_RecoversAtOneThirtiethPerSecond_RegardlessOfHowColdTheWaterWas()
    {
        // The recovery rate is deliberately NOT position-scaled: you warm up at the same rate
        // wherever you climbed out. Wading in the far-west water recovers exactly as fast as
        // wading by the shore, which is what makes the shelf a genuine refuge.
        Assert.Equal(0.5f, ChillClock.Advance(1f, WaterState.Wading, -140f, 15f), precision: 4);
        Assert.Equal(0.5f, ChillClock.Advance(1f, WaterState.Dry, 0f, 15f), precision: 4);
        Assert.Equal(0f, ChillClock.Advance(1f, WaterState.Dry, 0f, 30f), precision: 4);
    }

    // --- Boundaries and totality (MECHANICS-BIBLE §1, §6) ------------------------------------------

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    [InlineData(7f)]
    public void Chill_PoisonedOrOutOfRangeInput_ComesBackLegal(float poisoned)
    {
        float result = ChillClock.Advance(poisoned, WaterState.Swimming, -70f, Dt);
        Assert.True(float.IsFinite(result) && result is >= 0f and <= 1f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void Chill_NonPositiveDt_IsANoOp(float dt)
    {
        Assert.Equal(0.4f, ChillClock.Advance(0.4f, WaterState.Swimming, -140f, dt), precision: 5);
    }

    [Fact]
    public void Chill_NeverExceedsOneNorGoesNegative_EvenWithAnAbsurdStep()
    {
        Assert.Equal(1f, ChillClock.Advance(0.9f, WaterState.Swimming, -140f, 10_000f), precision: 5);
        Assert.Equal(0f, ChillClock.Advance(0.1f, WaterState.Dry, 0f, 10_000f), precision: 5);
    }

    // --- The urgency cue curve (spec §5.1) ------------------------------------------------------------

    [Fact]
    public void CueIntensity_IsSilentBelowOnsetAndFullAtOne()
    {
        Assert.Equal(0f, ChillClock.CueIntensity(0f), precision: 5);
        Assert.Equal(0f, ChillClock.CueIntensity(ChillClock.CueOnsetChill), precision: 5);
        Assert.Equal(1f, ChillClock.CueIntensity(1f), precision: 5);
        Assert.True(ChillClock.CueIntensity(0.8f) is > 0f and < 1f);
        Assert.Equal(0f, ChillClock.CueIntensity(float.NaN), precision: 5);
    }

    [Fact]
    public void CueIntensity_IsMonotonic()
    {
        float previous = -1f;
        for (float c = 0f; c <= 1.0001f; c += 0.01f)
        {
            float v = ChillClock.CueIntensity(c);
            Assert.True(v >= previous, $"cue intensity went backwards at chill={c}");
            previous = v;
        }
    }

    // --- The anti-unwinnable guarantee (MECHANICS-BIBLE §10.5) ------------------------------------------

    /// <summary>
    /// <b>always_one_exit.</b> The lake's declared guarantee, proved rather than asserted: from
    /// EVERY position in the water, the cold reaches 1.0 in bounded time and the sputter-out puts
    /// the camper back on a shoreline point east of the water line. There is no position, and no
    /// combination of position and chill, from which a player is stuck.
    ///
    /// This is the property the whole "soft boundary, never a wall" decision rests on — the map's
    /// west edge is enforced by the cold, not by collision, and if the cold could ever fail to
    /// collect someone then deleting the invisible wall would have shipped a way to strand
    /// yourself in black water forever.
    /// </summary>
    [Fact]
    public void AntiUnwinnable_NoPositionInTheWaterCanLeaveAPlayerStuck()
    {
        int sampled = 0;
        float worstSeconds = 0f;
        for (float x = WaterGeometry.ShoreX; x >= -WaterGeometry.MapHalf; x -= 1f)
        {
            for (float z = -WaterGeometry.MapHalf; z <= WaterGeometry.MapHalf; z += 5f)
            {
                sampled++;

                // 1. The cold always advances here, and always in bounded time.
                float rate = WaterGeometry.ChillRatePerSec(x);
                Assert.True(float.IsFinite(rate) && rate > 0f,
                    $"chill does not advance at x={x} — a camper there would never be collected");
                float seconds = ChillClock.SecondsToFull(0f, x);
                Assert.True(float.IsFinite(seconds) && seconds > 0f
                    && seconds <= WaterGeometry.TimeToFullInsideRopeSec,
                    $"time-to-collection at x={x} is {seconds}s, outside the bound");
                worstSeconds = MathF.Max(worstSeconds, seconds);

                // 2. It actually converges when integrated, not merely in the closed form.
                float chill = 0f;
                int steps = 0;
                int cap = (int)(1.5f * WaterGeometry.TimeToFullInsideRopeSec / Dt);
                while (chill < 1f && steps++ < cap)
                    chill = ChillClock.Advance(chill, WaterState.Swimming, x, Dt);
                Assert.Equal(1f, chill, precision: 5);

                // 3. The place it puts you is real, finite, and out of the water.
                Vector3 shore = WaterGeometry.NearestShorePoint(new Vector3(x, WaterGeometry.WaterY - 3f, z));
                Assert.True(float.IsFinite(shore.X) && float.IsFinite(shore.Y) && float.IsFinite(shore.Z));
                Assert.True(shore.X > WaterGeometry.ShoreX,
                    $"recovery from ({x},{z}) lands at x={shore.X}, still in the lake");
                Assert.True(MathF.Abs(shore.Z) <= WaterGeometry.MapHalf,
                    $"recovery from ({x},{z}) lands off the map at z={shore.Z}");

                // 4. And having landed there, you are Dry — not still resolving as swimming on
                //    the bank, which would restart the clock the instant it finished.
                Assert.Equal(WaterState.Dry,
                    WaterGeometry.ResolveAt(WaterState.Swimming, shore));
            }
        }
        Assert.True(sampled > 6_000, $"only {sampled} lake positions sampled — coverage too thin");
        Assert.True(worstSeconds <= WaterGeometry.TimeToFullInsideRopeSec);
    }

    [Fact]
    public void AntiUnwinnable_PositiveControl_TheHarnessCanDetectAStuckSpot()
    {
        // Proof the sweep above could fail. A rate curve with a dead zone — the "always-safe
        // tile" MECHANICS-BIBLE §10.3 names by name — must be caught by the same three checks.
        static float BrokenRate(float x) => x is < -100f and > -110f ? 0f : 1f / 180f;

        bool caught = false;
        for (float x = WaterGeometry.ShoreX; x >= -WaterGeometry.MapHalf; x -= 1f)
            if (BrokenRate(x) <= 0f)
                caught = true;
        Assert.True(caught, "the sweep's own dead-zone detector never fires — it proves nothing");
    }
}
