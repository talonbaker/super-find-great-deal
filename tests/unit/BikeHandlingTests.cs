using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The bike's handling model, pinned</b> (BIKE-2x, 2026-09-02). <see cref="BikeHandling"/> is
/// pure, so every claim the harness makes about the lean, the turn curve or the drift's charge
/// ladder is a claim about a function of values — and those are asserted here without an engine,
/// the same way <c>BikeRigTests</c> asserts the layer's arithmetic.
///
/// <para><b>The four the packet named by hand are the four that carry the most weight</b>, and each
/// is asserted against the tuned row rather than a figure typed here, so a moved row moves the
/// assertion with it:</para>
/// <list type="number">
/// <item>lean is monotone in yaw rate,</item>
/// <item>the charge tiers are reached at the tuned durations,</item>
/// <item>the grip recovery curve reaches 99 % inside the tuned time,</item>
/// <item>and there is no drift entry below the tuned entry speed.</item>
/// </list>
///
/// <para>Parks no tuning and touches no node: every assertion takes values and asks a static
/// function about them.</para>
/// </summary>
public sealed class BikeHandlingTests
{
    private static readonly BikeHandlingTuning H = BikeHandlingTuning.Default;

    /// <summary>The physics tick the lab actually runs at. Every stepped assertion below uses it,
    /// because a charge ladder that only lands on its thresholds at some convenient dt is a ladder
    /// that will miss them in the engine.</summary>
    private const float Tick = 1f / 60f;

    // --- Lean ------------------------------------------------------------------------------------

    /// <summary><b>The packet's first named pin.</b> More yaw rate, more lean — never less. Asserted
    /// as non-decreasing across the whole sweep (the clamp makes the top of it flat) and as
    /// STRICTLY increasing everywhere the clamp is not yet reached, which is the half that would
    /// still pass if the function had quietly become a constant.</summary>
    [Fact]
    public void LeanIsMonotoneInYawRate_AndStrictlyIncreasingBelowTheClamp()
    {
        const float speed = 8f;
        float previous = 0f;
        int strictlyIncreasingSamples = 0;

        for (float yaw = 0f; yaw <= 6f; yaw += 0.05f)
        {
            float lean = BikeHandling.LeanSteadyDeg(speed, yaw, H);
            Assert.True(lean >= previous - 1e-5f,
                $"lean fell from {previous:F4} to {lean:F4} at yaw {yaw:F2} rad/s");
            if (lean < H.LeanMaxDeg - 1e-3f && yaw > 0f)
            {
                Assert.True(lean > previous,
                    $"lean did not rise at yaw {yaw:F2} rad/s (still {lean:F4}, below the {H.LeanMaxDeg} clamp)");
                strictlyIncreasingSamples++;
            }
            previous = lean;
        }

        // If the clamp were reached on the first sample the loop above would assert nothing at all.
        Assert.True(strictlyIncreasingSamples >= 10,
            $"only {strictlyIncreasingSamples} samples were below the clamp — the sweep proves nothing");
    }

    /// <summary>The same monotonicity in the other argument: at a fixed yaw rate, a faster body
    /// leans further. Both arguments enter as their product, so this is the other half of the same
    /// claim and cheap to hold.</summary>
    [Fact]
    public void LeanIsMonotoneInSpeed()
    {
        const float yaw = 1.2f;
        float previous = 0f;
        for (float speed = 0f; speed <= 12f; speed += 0.25f)
        {
            float lean = BikeHandling.LeanSteadyDeg(speed, yaw, H);
            Assert.True(lean >= previous - 1e-5f,
                $"lean fell from {previous:F4} to {lean:F4} at {speed:F2} m/s");
            previous = lean;
        }
    }

    /// <summary>A body going nowhere, or going straight, is upright. Both are the same arithmetic
    /// (the product is zero) and both are worth naming: a lean that persists at a standstill is the
    /// first thing anyone would notice in a headed frame.</summary>
    [Theory]
    [InlineData(0f, 3f)]
    [InlineData(9f, 0f)]
    [InlineData(0f, 0f)]
    public void AStandstillOrAStraightLineIsUpright(float speed, float yaw)
        => Assert.Equal(0f, BikeHandling.LeanSteadyDeg(speed, yaw, H), 1e-5f);

    /// <summary><b>The sign convention, pinned so a headed run can be read against it.</b> Godot's
    /// yaw is about +Y, so a positive yaw rate is a turn to the rider's left and the return value is
    /// positive — "leaning left". The magnitudes match exactly; only the sign differs.</summary>
    [Fact]
    public void ALeftTurnLeansLeftAndARightTurnLeansRightBySameAmount()
    {
        float left = BikeHandling.LeanSteadyDeg(7f, 1.5f, H);
        float right = BikeHandling.LeanSteadyDeg(7f, -1.5f, H);
        Assert.True(left > 0f, $"a positive yaw rate must lean positive, got {left:F4}");
        Assert.Equal(-left, right, 1e-5f);
    }

    /// <summary>The clamp holds against anything. A yaw rate and a speed no body in this lab can
    /// reach must still produce an angle the greybox can draw.</summary>
    [Fact]
    public void LeanNeverPassesItsMax()
    {
        foreach (float speed in new[] { 0f, 5f, 20f, 200f })
        foreach (float yaw in new[] { -50f, -3f, 0f, 3f, 50f })
            Assert.True(Mathf.Abs(BikeHandling.LeanSteadyDeg(speed, yaw, H)) <= H.LeanMaxDeg + 1e-4f,
                $"lean passed the {H.LeanMaxDeg} deg clamp at {speed} m/s, {yaw} rad/s");
    }

    /// <summary>A zeroed max (the HANDLING OFF preset) means no lean at all, not a lean of some
    /// residual size — the control has to be a real control.</summary>
    [Fact]
    public void AZeroedMaxProducesNoLeanAtAll()
    {
        BikeHandlingTuning off = H with { LeanMaxDeg = 0f };
        Assert.Equal(0f, BikeHandling.LeanSteadyDeg(9f, 4f, off), 1e-6f);
    }

    /// <summary>
    /// <b>The lag is frame-rate independent</b>, which is the whole reason it is exponential rather
    /// than <c>lerp(a, b, rate * dt)</c>. Ten steps of a millisecond and one step of ten land in the
    /// same place; the linear form does not, and this value is stepped on the render clock.
    /// </summary>
    [Fact]
    public void LeanStepIsFrameRateIndependent()
    {
        const float target = 30f;
        float coarse = BikeHandling.LeanStep(0f, target, 0.1f, H);

        float fine = 0f;
        for (int i = 0; i < 10; i++)
            fine = BikeHandling.LeanStep(fine, target, 0.01f, H);

        Assert.Equal(coarse, fine, 1e-4f);
    }

    /// <summary>The lag never overshoots and never reverses, at any dt — including a dt far larger
    /// than any frame this lab will ever draw, which is where the linear form fails outright.</summary>
    [Theory]
    [InlineData(0.001f)]
    [InlineData(Tick)]
    [InlineData(0.25f)]
    [InlineData(5f)]
    public void LeanStepNeverOvershootsItsTarget(float dt)
    {
        const float target = 20f;
        float lean = -12f;
        // Long enough for the lag to be done at THIS dt, not a fixed step count: twelve time
        // constants is 0.999994 of the way, and a fixed 200 steps at a 1 ms dt would only be a
        // fifth of a second and would fail the convergence check on a correct curve.
        int steps = Mathf.CeilToInt(12f / (H.LeanRatePerSec * dt));
        for (int i = 0; i < steps; i++)
        {
            float next = BikeHandling.LeanStep(lean, target, dt, H);
            Assert.True(next >= lean - 1e-5f, $"lean reversed: {lean:F5} -> {next:F5}");
            Assert.True(next <= target + 1e-5f, $"lean overshot {target}: {next:F5}");
            lean = next;
        }
        Assert.True(Mathf.Abs(lean - target) < 1e-3f, $"lean never converged: {lean:F5}");
    }

    /// <summary>A zero or negative dt is a no-op rather than a NaN. The render clock hands out a
    /// zero dt on the first frame after a pause, and a lean that becomes NaN there stays NaN
    /// forever.</summary>
    [Fact]
    public void LeanStepAtZeroDtIsANoOp()
        => Assert.Equal(7f, BikeHandling.LeanStep(7f, 30f, 0f, H), 1e-6f);

    /// <summary>The yaw-rate helper takes the short way round the circle. Without the wrap, a body
    /// crossing the +/-pi seam reports about 2 pi / dt — 377 rad/s at a 60 Hz tick — and slams the
    /// lean into its clamp for exactly one frame.</summary>
    [Fact]
    public void YawRateTakesTheShortWayRoundTheSeam()
    {
        float rate = BikeHandling.YawRatePerSec(-3.10f, 3.10f, Tick);
        // 3.10 -> -3.10 the short way is +0.0832 rad (through pi), not -6.20.
        Assert.True(rate > 0f, $"the seam crossing came out negative: {rate:F3} rad/s");
        Assert.True(Mathf.Abs(rate) < 10f, $"the seam crossing produced {rate:F1} rad/s — it wrapped");
    }

    // --- The low-speed wobble (BIKE-4B, 2026-09-02) -------------------------------------------------
    //
    // The wobble is presentation, so none of these assertions is about where the body goes. They are
    // about the properties the feature's acceptance rests on: it is EXACTLY zero at and above the
    // fade speed, it is CONTINUOUS across that boundary, and at amplitude zero it is bit-exactly
    // nothing — so the drawn roll with the knob off is bit-identical to the corner lean alone.

    /// <summary>Amplitude zero is not "a very small wobble", it is <b>no</b> wobble: exactly
    /// <c>0f</c>, at every speed and every phase. This is acceptance criterion 3's arithmetic half —
    /// the drawn roll is <c>lean + WobbleDeg(...)</c>, and adding a bit-exact <c>0f</c> to a float
    /// returns that float unchanged, so the composed roll is bit-identical to today's.</summary>
    [Fact]
    public void AmplitudeZero_IsBitExactlyZero_AtEverySpeedAndPhase()
    {
        BikeHandlingTuning off = H with { WobbleAmplitudeDeg = 0f };
        for (float speed = 0f; speed <= 12f; speed += 0.25f)
        {
            for (float t = 0f; t < 4f; t += 0.017f)
            {
                float w = BikeHandling.WobbleDeg(speed, t, off);
                Assert.True(w.Equals(0f),
                    $"amplitude 0 produced {w} at {speed} m/s, phase {t} s — must be exactly 0f");
                // And the composition it feeds is therefore an identity on any lean angle.
                float lean = BikeHandling.LeanSteadyDeg(speed, 2.5f, off);
                Assert.True((lean + w).Equals(lean), "lean + wobble must be bit-identical to lean");
            }
        }
    }

    /// <summary>At and above the fade speed the wobble is exactly zero, and it stays zero however
    /// far above — no wrap, no sign flip, no revival at high speed. Acceptance criterion 2's first
    /// half.</summary>
    [Fact]
    public void AtAndAboveTheFadeSpeed_TheWobbleIsExactlyZero()
    {
        float fade = H.WobbleFadeSpeedMps;
        Assert.True(fade > 0f, "the default fade speed must be positive or the feature is off");
        foreach (float speed in new[] { fade, fade + 1e-4f, fade + 0.5f, 6.1f, 9.4f, 25f, 1000f })
        {
            for (float t = 0f; t < 3f; t += 0.013f)
            {
                float w = BikeHandling.WobbleDeg(speed, t, H);
                Assert.True(w.Equals(0f),
                    $"wobble {w} at {speed} m/s (fade {fade}) — must be exactly 0f at and above");
            }
        }
    }

    /// <summary>The function is continuous ACROSS the fade boundary, not merely zero on one side of
    /// it: the envelope's value and its slope both reach zero there, so the wobble dies away rather
    /// than clipping off. Asserted by walking up to the boundary and requiring the swing to fall
    /// monotonically to under a thousandth of the amplitude row before the step across.
    /// Acceptance criterion 2's second half.</summary>
    [Fact]
    public void TheWobbleIsContinuousAcrossTheFadeBoundary()
    {
        float fade = H.WobbleFadeSpeedMps;
        // The phase where the two sines sum closest to their peak, found by scan so the envelope is
        // measured at close to full swing rather than at an accidental zero crossing.
        float peakPhase = 0f, peakAbs = 0f;
        for (float t = 0f; t < 5f; t += 0.001f)
        {
            float a = Mathf.Abs(BikeHandling.WobbleDeg(0f, t, H));
            if (a > peakAbs) { peakAbs = a; peakPhase = t; }
        }
        Assert.True(peakAbs > 0f, "the wobble never left zero at a standstill");

        float prev = float.MaxValue;
        for (float speed = fade - 0.5f; speed < fade; speed += 0.005f)
        {
            float w = Mathf.Abs(BikeHandling.WobbleDeg(speed, peakPhase, H));
            Assert.True(w <= prev + 1e-6f,
                $"the envelope rose approaching the boundary: {prev} -> {w} at {speed} m/s");
            prev = w;
        }
        // The last sample below the boundary, and the first at it. A discontinuity would show as a
        // finite jump here; the smoothstep envelope makes it vanishingly small.
        float justBelow = Mathf.Abs(BikeHandling.WobbleDeg(fade - 1e-3f, peakPhase, H));
        float atBoundary = Mathf.Abs(BikeHandling.WobbleDeg(fade, peakPhase, H));
        Assert.Equal(0f, atBoundary);
        Assert.True(justBelow < 1e-3f * H.WobbleAmplitudeDeg,
            $"the step across the boundary was {justBelow:E3} deg — not continuous");
    }

    /// <summary>At walking pace the wobble is genuinely oscillating: it changes over time, crosses
    /// zero in both directions, and does so several times a second. This is the feature — a bike
    /// that is not a statue at 1 m/s (BIKE-3C section 3, gap 1).</summary>
    [Fact]
    public void AtWalkingPace_TheWobbleOscillatesThroughBothSigns()
    {
        int positives = 0, negatives = 0, crossings = 0;
        float prev = BikeHandling.WobbleDeg(1f, 0f, H);
        float peak = 0f;
        for (int i = 1; i <= 300; i++)   // five seconds at the lab's 60 Hz tick
        {
            float w = BikeHandling.WobbleDeg(1f, i * Tick, H);
            if (w > 0f) positives++;
            if (w < 0f) negatives++;
            if (prev * w < 0f) crossings++;
            peak = Mathf.Max(peak, Mathf.Abs(w));
            prev = w;
        }
        Assert.True(positives > 50 && negatives > 50,
            $"the wobble spent {positives} samples left and {negatives} right of upright");
        Assert.True(crossings >= 10,
            $"only {crossings} zero crossings in 5 s — that is not an oscillation");
        Assert.True(peak > 0.5f * H.WobbleAmplitudeDeg,
            $"peak swing {peak:F3} deg against a {H.WobbleAmplitudeDeg} deg amplitude row");
    }

    /// <summary>The amplitude row IS the peak degrees: the two sine shares sum to one, so the
    /// wobble never exceeds the number on the knob, at any speed or phase. A knob whose value is
    /// not the thing it names is a knob nobody can tune.</summary>
    [Fact]
    public void TheWobbleNeverExceedsItsAmplitudeRow_AndReachesMostOfIt()
    {
        float peak = 0f;
        for (float speed = 0f; speed <= 12f; speed += 0.1f)
        {
            for (float t = 0f; t < 12f; t += 0.005f)
            {
                float w = Mathf.Abs(BikeHandling.WobbleDeg(speed, t, H));
                Assert.True(w <= H.WobbleAmplitudeDeg + 1e-4f,
                    $"wobble {w} exceeded the {H.WobbleAmplitudeDeg} deg row at {speed} m/s");
                peak = Mathf.Max(peak, w);
            }
        }
        Assert.True(peak > 0.9f * H.WobbleAmplitudeDeg,
            $"the wobble only ever reached {peak:F3} of its {H.WobbleAmplitudeDeg} deg row");
    }

    /// <summary>BIKE-3C row 12's standing rule, made a test: could <c>DriftStep</c> replay it from a
    /// snapshot? The check is that the function is a pure map from <c>(speed, phase, tuning)</c> —
    /// the same three values give the same bits, in any order, however many times, with unrelated
    /// model calls interleaved. A hidden clock, a static, or an accumulator would fail this.</summary>
    [Fact]
    public void TheWobbleIsAPureReplayableFunctionOfSpeedPhaseAndTuning()
    {
        var samples = new List<(float speed, float phase, float value)>();
        for (float speed = 0f; speed <= 5f; speed += 0.37f)
            for (float t = 0f; t < 3f; t += 0.19f)
                samples.Add((speed, t, BikeHandling.WobbleDeg(speed, t, H)));

        // Replayed backwards, with other model calls interleaved, long after the fact.
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            BikeHandling.DriftStep(BikeHandling.DriftState.Rest, true, true, true, 9f, 0.4f, Tick, H);
            BikeHandling.LeanSteadyDeg(samples[i].speed, 1.7f, H);
            float again = BikeHandling.WobbleDeg(samples[i].speed, samples[i].phase, H);
            Assert.True(again.Equals(samples[i].value),
                $"replay differed at {samples[i].speed} m/s, phase {samples[i].phase}: "
              + $"{samples[i].value} then {again}");
        }
    }

    /// <summary>Phase zero is exactly upright, so the harness's "hold the phase at zero off the
    /// bike" gives a mount that begins level instead of popping into a roll.</summary>
    [Fact]
    public void PhaseZeroIsExactlyUpright()
    {
        for (float speed = 0f; speed <= 4f; speed += 0.1f)
            Assert.True(BikeHandling.WobbleDeg(speed, 0f, H).Equals(0f),
                $"phase 0 was not upright at {speed} m/s");
    }

    /// <summary>Speed's sign is ignored and a garbage row cannot poison the roll: a NaN or negative
    /// amplitude, a NaN, zero or negative fade speed, and a NaN speed all return a clean zero rather
    /// than propagating. The knob panel clamps its sliders; a preset typed wrong does not.</summary>
    [Fact]
    public void GarbageRowsAndBackwardsRolling_ProduceAFiniteRoll()
    {
        Assert.Equal(BikeHandling.WobbleDeg(1.4f, 0.31f, H), BikeHandling.WobbleDeg(-1.4f, 0.31f, H));
        foreach (BikeHandlingTuning bad in new[]
        {
            H with { WobbleAmplitudeDeg = float.NaN },
            H with { WobbleAmplitudeDeg = -3f },
            H with { WobbleFadeSpeedMps = 0f },
            H with { WobbleFadeSpeedMps = float.NaN },
            H with { WobbleFadeSpeedMps = -2f },
        })
            for (float speed = 0f; speed <= 6f; speed += 0.5f)
                Assert.True(BikeHandling.WobbleDeg(speed, 0.4f, bad).Equals(0f),
                    $"a garbage row produced a non-zero wobble at {speed} m/s");
        Assert.True(BikeHandling.WobbleDeg(float.NaN, 0.4f, H).Equals(0f));
        for (float t = 0f; t < 2f; t += 0.05f)
            Assert.True(float.IsFinite(BikeHandling.WobbleDeg(0.5f, t, H)));
    }

    /// <summary>The wobble is <b>speed-stability coupling</b>, so its envelope must actually fall
    /// with speed rather than merely switch off at the end: sampled at a fixed phase, the swing at
    /// a creep strictly exceeds the swing at a jog.</summary>
    [Fact]
    public void TheEnvelopeFallsMonotonicallyWithSpeed()
    {
        float phase = 0.42f;   // a phase where both sines are well off zero
        float prev = float.MaxValue;
        for (float speed = 0f; speed < H.WobbleFadeSpeedMps; speed += 0.05f)
        {
            float w = Mathf.Abs(BikeHandling.WobbleDeg(speed, phase, H));
            Assert.True(w <= prev + 1e-6f, $"the envelope rose from {prev} to {w} at {speed} m/s");
            prev = w;
        }
        Assert.True(Mathf.Abs(BikeHandling.WobbleDeg(0.5f, phase, H))
                  > Mathf.Abs(BikeHandling.WobbleDeg(3.5f, phase, H)),
            "a creep must rock more than a jog");
    }

    /// <summary>The HANDLING OFF preset really is off: it zeroes the wobble as well as the lean and
    /// the curve, so CTRL+ALT+6 remains the one-press control for everything the handling model
    /// adds.</summary>
    [Fact]
    public void TheHandlingOffPreset_ZeroesTheWobbleToo()
    {
        BikeHandlingTuning offPreset = BikeHandlingTuning.ForKey(6)!.Value.Tuning;
        for (float speed = 0f; speed <= 6f; speed += 0.25f)
            for (float t = 0f; t < 2f; t += 0.05f)
                Assert.True(BikeHandling.WobbleDeg(speed, t, offPreset).Equals(0f),
                    $"HANDLING OFF still wobbled at {speed} m/s");
    }

    /// <summary>The wobble amplitude default sits under the 3-degree floor BIKE-3A's drawn-lean
    /// check samples above, so a wobble added to a corner lean can never flip the sign that check
    /// pins. Acceptance criterion 4, as arithmetic rather than as a belief.</summary>
    [Fact]
    public void TheDefaultAmplitudeCannotFlipTheSignOfALeanAboveThreeDegrees()
    {
        Assert.True(H.WobbleAmplitudeDeg < 3f,
            $"amplitude {H.WobbleAmplitudeDeg} is at or above BIKE-3A's 3 deg sample floor");
        for (float lean = 3.0001f; lean <= 38f; lean += 0.25f)
            for (float t = 0f; t < 3f; t += 0.02f)
                foreach (float speed in new[] { 0f, 0.5f, 1f, 2f, 3.9f })
                {
                    float w = BikeHandling.WobbleDeg(speed, t, H);
                    Assert.True(lean + w > 0f && -lean + w < 0f,
                        $"a {w} deg wobble flipped a {lean} deg lean at {speed} m/s");
                }
    }

    // --- Turn shaping ------------------------------------------------------------------------------

    /// <summary>The curve's two ends are exactly the two rows that name them, and everything above
    /// the cap stays at the high end rather than extrapolating past it.</summary>
    [Fact]
    public void TheTurnCurveHitsItsRowsAtBothEndsAndClampsAboveTheCap()
    {
        const float cap = 9.4f;
        Assert.Equal(H.TurnLowSpeedMul, BikeHandling.TurnMultiplier(0f, cap, H), 1e-5f);
        Assert.Equal(H.TurnHighSpeedMul, BikeHandling.TurnMultiplier(cap, cap, H), 1e-5f);
        Assert.Equal(H.TurnHighSpeedMul, BikeHandling.TurnMultiplier(cap * 3f, cap, H), 1e-5f);
    }

    /// <summary><b>Tight at low speed, wider at high</b> — the packet's whole ask for this curve,
    /// asserted as a strict monotone decrease across the range with the default rows.</summary>
    [Fact]
    public void TheTurnCurveWidensMonotonicallyWithSpeed()
    {
        const float cap = 9.4f;
        float previous = float.MaxValue;
        for (float speed = 0f; speed <= cap; speed += 0.1f)
        {
            float mul = BikeHandling.TurnMultiplier(speed, cap, H);
            Assert.True(mul <= previous + 1e-6f,
                $"the turn curve tightened at {speed:F2} m/s: {previous:F5} -> {mul:F5}");
            previous = mul;
        }
        Assert.True(BikeHandling.TurnMultiplier(0f, cap, H) > BikeHandling.TurnMultiplier(cap, cap, H),
            "the default curve does not widen at all");
    }

    /// <summary>The NO CURVE preset really is a control: a flat 1.0 at every speed, so an A/B
    /// against it isolates the curve rather than the curve plus a residue of itself.</summary>
    [Fact]
    public void TheNoCurvePresetIsFlatAtOne()
    {
        BikeHandlingTuning flat = BikeHandlingTuning.ForKey(1)!.Value.Tuning;
        for (float speed = 0f; speed <= 12f; speed += 0.5f)
            Assert.Equal(1f, BikeHandling.TurnMultiplier(speed, 9.4f, flat), 1e-5f);
    }

    /// <summary>A zero or negative cap is a division this lab must survive: the ride cap is derived
    /// from a tuning a slider can drag, and a body asked about a zero cap must return the tight end
    /// rather than an infinity.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-4f)]
    public void ADegenerateCapDoesNotProduceInfinity(float cap)
    {
        float mul = BikeHandling.TurnMultiplier(5f, cap, H);
        Assert.True(float.IsFinite(mul), $"a cap of {cap} produced {mul}");
        Assert.Equal(H.TurnHighSpeedMul, mul, 1e-5f);
    }

    /// <summary>
    /// <b>The shaped turn survives the validator at every speed.</b> This is the one assertion here
    /// that reaches outside the handling model, and it is the important one: the harness multiplies
    /// <c>RideTurnMul</c> by this curve and hands the whole record to <c>BikeLayer.SetTuning</c>, so
    /// a curve that produced a <c>TurnLerp</c> outside the knob's range would seat a ride the panel
    /// considers illegal and then display it as legal. <c>BikeRig.Ride</c> clamps, and this proves
    /// the clamp is never reached with the shipped rows rather than merely relying on it.
    ///
    /// <para><b>It goes through <c>MotorTuning.Validate</c>, not <c>TryApply</c>, and that is not a
    /// style choice.</b> <c>TryApply</c> calls <c>SessionLive()</c>, which asks the <i>engine</i>
    /// whether a multiplayer peer exists — and in this Godot-free test host that native call
    /// <b>segfaults the whole process</b> (measured 2026-09-02: SIGSEGV, a 447 MB core dump, and
    /// eighteen of this file's thirty-six tests silently never ran while the runner still printed
    /// a green line for the ones that had). <c>BikeRigTests</c> already validates this way for the
    /// same reason. <b>Never call <c>TryApply</c> from an xUnit test.</b></para>
    /// </summary>
    [Fact]
    public void TheShapedRideTuningIsLegalAtEverySpeed_AndNeverNeedsTheClamp()
    {
        MotorTuning foot = MotorTuning.Default;
        BikeTuning b = BikeTuning.Default;
        float cap = BikeRig.RideCapMps(foot, b);

        for (float speed = 0f; speed <= cap * 1.5f; speed += 0.25f)
        {
            float mul = BikeHandling.TurnMultiplier(speed, cap, H);
            BikeTuning shaped = b with { RideTurnMul = b.RideTurnMul * mul };
            MotorTuning ride = BikeRig.Ride(foot, shaped);

            Assert.True(ride.TurnLerp >= MotorTuningKnobs.TurnLerp.Min
                     && ride.TurnLerp <= MotorTuningKnobs.TurnLerp.Max,
                $"TurnLerp {ride.TurnLerp:F4} at {speed:F2} m/s is outside the knob's range");
            // The unclamped product and the clamped one agree: the clamp is a guard, not a
            // silent correction the readout would misreport.
            Assert.Equal(foot.TurnLerp * shaped.RideTurnMul, ride.TurnLerp, 1e-4f);

            MotorTuning seated = MotorTuning.Validate(ride, out IReadOnlyList<string> warnings);
            Assert.True(seated.Equals(ride),
                $"the shaped ride at {speed:F2} m/s does not survive validation unchanged — the lab "
              + "would run a bike this file does not describe");
            Assert.True(warnings.Count == 0,
                $"the shaped ride at {speed:F2} m/s validated with warnings: {string.Join(" | ", warnings)}");
        }
    }

    // --- Grip ------------------------------------------------------------------------------------

    /// <summary><b>The packet's third named pin.</b> The recovery curve reaches 99 % inside the
    /// tuned time — and, so the assertion is not trivially satisfiable by a curve that jumps
    /// straight to 1, it is still SHORT of 99 % at nine tenths of that time.</summary>
    [Fact]
    public void GripReaches99PercentInsideTheTunedTime_AndNotBefore()
    {
        float t99 = H.DriftGripRecoverSec;
        Assert.True(BikeHandling.GripAfterRelease(t99, H) >= 0.99f,
            $"grip was only {BikeHandling.GripAfterRelease(t99, H):F5} at the tuned {t99:F2} s");
        Assert.True(BikeHandling.GripAfterRelease(t99 * 0.9f, H) < 0.99f,
            "grip already passed 99 % before the tuned time — the row does not mean what it says");
        Assert.Equal(0f, BikeHandling.GripAfterRelease(0f, H), 1e-6f);
    }

    /// <summary>The stepped form and the closed form agree. Two spellings of one curve that could
    /// drift apart is exactly the sort of thing a lab discovers a week late.</summary>
    [Fact]
    public void TheSteppedGripAgreesWithTheClosedForm()
    {
        float grip = 0f;
        for (int i = 1; i <= 60; i++)
        {
            grip = BikeHandling.GripStep(grip, Tick, drifting: false, H);
            Assert.Equal(BikeHandling.GripAfterRelease(i * Tick, H), grip, 1e-4f);
        }
    }

    /// <summary>Grip is zero while the drift is live — a locked rear wheel has none and does not
    /// lose it gradually — and it is bounded at both ends whatever it is handed.</summary>
    [Fact]
    public void GripIsZeroWhileDriftingAndAlwaysInsideZeroToOne()
    {
        Assert.Equal(0f, BikeHandling.GripStep(1f, Tick, drifting: true, H));
        foreach (float start in new[] { -5f, 0f, 0.5f, 1f, 4f })
        {
            float grip = BikeHandling.GripStep(start, Tick, drifting: false, H);
            Assert.InRange(grip, 0f, 1f);
        }
    }

    // --- The drift: entry ---------------------------------------------------------------------------

    /// <summary>
    /// <b>The packet's fourth named pin, and the rule it states hardest:</b> the drift must never
    /// enter from ordinary cornering. Swept from a standstill to the entry speed, holding the
    /// button on the floor on the bike the whole way — nothing enters, nothing charges, and grip is
    /// never spent.
    /// </summary>
    [Fact]
    public void NothingEntersBelowTheEntrySpeed()
    {
        for (float speed = 0f; speed < H.DriftEntrySpeedMps - 1e-4f; speed += 0.05f)
        {
            BikeHandling.DriftResult r = BikeHandling.DriftStep(BikeHandling.DriftState.Rest,
                mounted: true, grounded: true, held: true, speed, steer: 0.9f, Tick, H);
            Assert.False(r.Next.Active, $"the drift entered at {speed:F2} m/s");
            Assert.False(r.Entered, $"an entry was reported at {speed:F2} m/s");
            Assert.Equal(0f, r.Next.ChargeSec, 1e-6f);
            Assert.Equal(1f, r.Next.Grip, 1e-5f);
        }

        BikeHandling.DriftResult at = BikeHandling.DriftStep(BikeHandling.DriftState.Rest,
            mounted: true, grounded: true, held: true, H.DriftEntrySpeedMps, steer: 0f, Tick, H);
        Assert.True(at.Entered, "the drift did not enter AT the entry speed");
        Assert.True(at.Next.Active);
    }

    /// <summary>The other four halves of the entry gate, one at a time, all at a speed that would
    /// otherwise qualify. A drift that could start in the air, or off the bike, is not a drift.</summary>
    [Theory]
    [InlineData(false, true, true)]   // not mounted
    [InlineData(true, false, true)]   // airborne
    [InlineData(true, true, false)]   // button up
    public void EveryOtherHalfOfTheEntryGateAlsoRefuses(bool mounted, bool grounded, bool held)
    {
        BikeHandling.DriftResult r = BikeHandling.DriftStep(BikeHandling.DriftState.Rest,
            mounted, grounded, held, speedMps: 9f, steer: 0f, Tick, H);
        Assert.False(r.Next.Active);
        Assert.False(r.Entered);
    }

    // --- The drift: charge, tiers, payout -------------------------------------------------------------

    /// <summary>
    /// <b>The packet's second named pin.</b> Held at speed on the floor, the charge ladder reaches
    /// each tier at the tuned duration — asserted against the ROWS, within one physics tick, so a
    /// moved threshold moves the assertion.
    /// </summary>
    [Fact]
    public void TheChargeTiersAreReachedAtTheTunedDurations()
    {
        var reachedAt = new Dictionary<int, float>();
        BikeHandling.DriftState s = BikeHandling.DriftState.Rest;
        int tier = 0;

        for (int i = 0; i < 60 * 4; i++)
        {
            s = BikeHandling.DriftStep(s, mounted: true, grounded: true, held: true,
                speedMps: 9f, steer: 0f, Tick, H).Next;
            if (!s.Active)
                break;
            int now = BikeHandling.DriftTier(s.ChargeSec, H);
            if (now > tier)
            {
                reachedAt[now] = s.ChargeSec;
                tier = now;
            }
        }

        foreach ((int t, float row) in new[]
                 { (1, H.DriftTier1Sec), (2, H.DriftTier2Sec), (3, H.DriftTier3Sec) })
        {
            Assert.True(reachedAt.ContainsKey(t), $"tier {t} was never reached inside DriftMaxSec");
            Assert.True(Mathf.Abs(reachedAt[t] - row) <= Tick + 1e-5f,
                $"tier {t} arrived at {reachedAt[t]:F4} s of charge, not the tuned {row:F4} s");
        }
    }

    /// <summary>The tier a charge names, read directly. Just under a threshold is the tier below;
    /// exactly on it is the tier itself.</summary>
    [Fact]
    public void TheTierBoundariesAreInclusiveFromBelow()
    {
        Assert.Equal(0, BikeHandling.DriftTier(H.DriftTier1Sec - 1e-3f, H));
        Assert.Equal(1, BikeHandling.DriftTier(H.DriftTier1Sec, H));
        Assert.Equal(1, BikeHandling.DriftTier(H.DriftTier2Sec - 1e-3f, H));
        Assert.Equal(2, BikeHandling.DriftTier(H.DriftTier2Sec, H));
        Assert.Equal(2, BikeHandling.DriftTier(H.DriftTier3Sec - 1e-3f, H));
        Assert.Equal(3, BikeHandling.DriftTier(H.DriftTier3Sec, H));
        Assert.Equal(3, BikeHandling.DriftTier(H.DriftTier3Sec * 10f, H));
    }

    /// <summary>Release pays exactly the tier's own row, on exactly one tick, and the state that
    /// comes back is a clean one — the boost must not be payable twice.</summary>
    [Fact]
    public void ReleasePaysTheTiersRowExactlyOnceAndResetsTheCharge()
    {
        BikeHandling.DriftState s = HeldForSeconds(H.DriftTier2Sec + 0.05f, out _);
        Assert.Equal(2, BikeHandling.DriftTier(s.ChargeSec, H));

        BikeHandling.DriftResult release = BikeHandling.DriftStep(s, mounted: true, grounded: true,
            held: false, speedMps: 9f, steer: 0f, Tick, H);
        Assert.Equal(H.DriftTier2BoostMps, release.ExitBoostMps, 1e-5f);
        Assert.Equal(2, release.ExitTier);
        Assert.False(release.Next.Active);
        Assert.Equal(0f, release.Next.ChargeSec, 1e-6f);

        BikeHandling.DriftResult after = BikeHandling.DriftStep(release.Next, mounted: true,
            grounded: true, held: false, speedMps: 9f, steer: 0f, Tick, H);
        Assert.Equal(0f, after.ExitBoostMps, 1e-6f);
        Assert.Equal(0, after.ExitTier);
    }

    /// <summary>A drift released below tier 1 pays nothing at all. The ladder has to have a bottom
    /// rung that costs something to reach, or every twitch of the button is a boost.</summary>
    [Fact]
    public void ADriftReleasedBelowTierOnePaysNothing()
    {
        BikeHandling.DriftState s = HeldForSeconds(H.DriftTier1Sec * 0.5f, out _);
        BikeHandling.DriftResult release = BikeHandling.DriftStep(s, mounted: true, grounded: true,
            held: false, speedMps: 9f, steer: 0f, Tick, H);
        Assert.Equal(0, release.ExitTier);
        Assert.Equal(0f, release.ExitBoostMps, 1e-6f);
    }

    /// <summary><b>Leaving the ground pays out.</b> A jump out of a loaded corner is the good
    /// version of this move; charging a player for taking it would teach them not to.</summary>
    [Fact]
    public void JumpingOutOfADriftStillPaysTheCharge()
    {
        BikeHandling.DriftState s = HeldForSeconds(H.DriftTier1Sec + 0.05f, out _);
        BikeHandling.DriftResult air = BikeHandling.DriftStep(s, mounted: true, grounded: false,
            held: true, speedMps: 9f, steer: 0f, Tick, H);
        Assert.Equal(H.DriftTier1BoostMps, air.ExitBoostMps, 1e-5f);
        Assert.False(air.Next.Active);
        Assert.False(air.Next.NeedsRelease);
    }

    /// <summary>Dismounting mid-drift pays out too, and leaves nothing live behind it.</summary>
    [Fact]
    public void DismountingMidDriftPaysOutAndLeavesNothingLive()
    {
        BikeHandling.DriftState s = HeldForSeconds(H.DriftTier1Sec + 0.05f, out _);
        BikeHandling.DriftResult off = BikeHandling.DriftStep(s, mounted: false, grounded: true,
            held: true, speedMps: 9f, steer: 0f, Tick, H);
        Assert.True(off.ExitBoostMps > 0f);
        Assert.False(off.Next.Active);
    }

    /// <summary>
    /// <b>The duration cap ends the drift, and it latches.</b> Holding the button through the cap
    /// must not restart a fresh drift on the next tick — that would pay an exit boost every
    /// <c>DriftMaxSec</c> for doing nothing. The latch clears only when the button comes up.
    /// </summary>
    [Fact]
    public void TheDurationCapEndsTheDriftAndLatchesUntilTheButtonComesUp()
    {
        BikeHandling.DriftState s = BikeHandling.DriftState.Rest;
        float paid = 0f;
        bool capped = false;

        for (int i = 0; i < 60 * 8 && !capped; i++)
        {
            BikeHandling.DriftResult r = BikeHandling.DriftStep(s, mounted: true, grounded: true,
                held: true, speedMps: 9f, steer: 0f, Tick, H);
            s = r.Next;
            if (r.ExitBoostMps > 0f)
            {
                paid = r.ExitBoostMps;
                capped = true;
            }
        }

        Assert.True(capped, $"the drift never hit its {H.DriftMaxSec} s cap");
        Assert.Equal(H.DriftTier3BoostMps, paid, 1e-5f);
        Assert.True(s.NeedsRelease, "the cap did not latch");

        // Still held: nothing may restart, however long we wait.
        for (int i = 0; i < 240; i++)
        {
            BikeHandling.DriftResult r = BikeHandling.DriftStep(s, mounted: true, grounded: true,
                held: true, speedMps: 9f, steer: 0f, Tick, H);
            Assert.False(r.Entered, $"the latch let a drift restart {i} ticks after the cap");
            Assert.Equal(0f, r.ExitBoostMps, 1e-6f);
            s = r.Next;
        }

        // Button up for one tick, then down again: a fresh drift is allowed.
        s = BikeHandling.DriftStep(s, mounted: true, grounded: true, held: false,
            speedMps: 9f, steer: 0f, Tick, H).Next;
        Assert.False(s.NeedsRelease, "the latch did not clear on release");
        Assert.True(BikeHandling.DriftStep(s, mounted: true, grounded: true, held: true,
            speedMps: 9f, steer: 0f, Tick, H).Entered, "a fresh press was refused after a clean release");
    }

    /// <summary>A drift that sheds below the entry speed mid-corner keeps running. The entry gate is
    /// about how a drift may START; cancelling one underneath a player because a hill slowed them
    /// reads as the game taking the controls away, which Talon ruled out.</summary>
    [Fact]
    public void ADriftThatSlowsBelowTheEntrySpeedIsNotCancelled()
    {
        BikeHandling.DriftState s = HeldForSeconds(0.3f, out _);
        Assert.True(s.Active);
        for (int i = 0; i < 30; i++)
        {
            BikeHandling.DriftResult r = BikeHandling.DriftStep(s, mounted: true, grounded: true,
                held: true, speedMps: H.DriftEntrySpeedMps * 0.2f, steer: 0f, Tick, H);
            Assert.True(r.Next.Active, $"the drift was cancelled by speed on tick {i}");
            s = r.Next;
        }
    }

    // --- The drift: the wiggle charge model ------------------------------------------------------

    /// <summary>
    /// <b>Under the wiggle model, holding a line earns nothing</b> — that is the whole difference
    /// between the two models, and it is the assertion that would fail if the wiggle toggle were
    /// silently falling through to the duration model.
    /// </summary>
    [Fact]
    public void TheWiggleModelEarnsNothingFromASteadyStick()
    {
        BikeHandlingTuning w = H with { DriftWiggleCharge = true };
        BikeHandling.DriftState s = BikeHandling.DriftState.Rest;
        for (int i = 0; i < 60 * 3; i++)
        {
            BikeHandling.DriftResult r = BikeHandling.DriftStep(s, mounted: true, grounded: true,
                held: true, speedMps: 9f, steer: 1f, Tick, w);
            s = r.Next;
            if (!s.Active)
                break;
        }
        Assert.Equal(0, BikeHandling.DriftTier(s.ChargeSec, w));
    }

    /// <summary>Working the stick fills the same ladder: each qualifying reversal is worth
    /// <c>DriftWiggleFlickSec</c>, so tier 1 arrives after exactly the number of flicks that row
    /// implies — asserted against the rows, not a typed count.</summary>
    [Fact]
    public void WorkingTheStickFillsTheSameLadder()
    {
        BikeHandlingTuning w = H with { DriftWiggleCharge = true };
        int flicksForTier1 = Mathf.CeilToInt(w.DriftTier1Sec / w.DriftWiggleFlickSec);

        BikeHandling.DriftState s = BikeHandling.DriftStep(BikeHandling.DriftState.Rest,
            mounted: true, grounded: true, held: true, speedMps: 9f, steer: 0f, Tick, w).Next;
        Assert.True(s.Active);

        float side = 1f;
        int flicks = 0;
        while (flicks < flicksForTier1)
        {
            s = BikeHandling.DriftStep(s, mounted: true, grounded: true, held: true,
                speedMps: 9f, steer: side, Tick, w).Next;
            side = -side;
            flicks++;
        }

        Assert.Equal(1, BikeHandling.DriftTier(s.ChargeSec, w));
        Assert.Equal(flicks * w.DriftWiggleFlickSec, s.ChargeSec, 1e-4f);
    }

    /// <summary>A stick that never leaves the dead zone registers no flick however hard it is
    /// waggled. Without the throw floor, the wiggle ladder fills itself on controller noise.</summary>
    [Fact]
    public void ATinyWaggleInsideTheDeadZoneIsNotAFlick()
    {
        float tiny = H.DriftWiggleFlickMin * 0.5f;
        Assert.False(BikeHandling.IsFlick(tiny, -tiny, H));
        Assert.False(BikeHandling.IsFlick(-tiny, tiny, H));
        Assert.True(BikeHandling.IsFlick(H.DriftWiggleFlickMin, -1f, H));
    }

    /// <summary>Two throws to the same side are one flick, not two: the charge is for reversing,
    /// not for pushing.</summary>
    [Fact]
    public void PushingTheSameWayTwiceIsOneFlick()
    {
        Assert.True(BikeHandling.IsFlick(1f, 0f, H));
        Assert.False(BikeHandling.IsFlick(1f, 1f, H));
        Assert.True(BikeHandling.IsFlick(-1f, 1f, H));
    }

    // --- Tier feedback ----------------------------------------------------------------------------

    /// <summary>Progress through a tier stays inside 0..1 across the whole charge range, and the top
    /// tier is full. It is the only input the diegetic spark has, so a value outside the range is a
    /// colour nobody chose.</summary>
    [Fact]
    public void TierProgressStaysInsideZeroToOne()
    {
        for (float charge = 0f; charge <= H.DriftTier3Sec * 1.5f; charge += 0.01f)
            Assert.InRange(BikeHandling.TierProgress01(charge, H), 0f, 1f);
        Assert.Equal(1f, BikeHandling.TierProgress01(H.DriftTier3Sec, H), 1e-5f);
    }

    /// <summary>The four tier colours are visually distinct. With no counter anywhere — the packet
    /// forbids one — the colour IS the readout, and two tiers that look alike are two tiers a
    /// player cannot tell apart.</summary>
    [Fact]
    public void TheFourTierColoursAreDistinct()
    {
        var seen = new List<Color>();
        for (int tier = 0; tier <= 3; tier++)
        {
            Color c = BikeHandling.TierColour(tier);
            foreach (Color other in seen)
            {
                float distance = Mathf.Abs(c.R - other.R) + Mathf.Abs(c.G - other.G)
                               + Mathf.Abs(c.B - other.B);
                Assert.True(distance > 0.4f,
                    $"tier {tier} is within {distance:F2} of another tier's colour");
            }
            seen.Add(c);
        }
    }

    // --- The IsHumanInput trap -----------------------------------------------------------------------

    /// <summary>
    /// <b><c>BikeLayer</c> does not declare <c>IsHumanInput</c>, so it is the interface's
    /// <c>=&gt; false</c> default — and NOTHING in the lab may gate a player-facing feature on it.</b>
    ///
    /// <para><b>This is a pin on a defect that shipped and was found by telemetry rather than by a
    /// test.</b> The harness's drift used to read
    /// <c>!avatar.IntentSource.IsHumanInput ? scripted : pollTheMouse</c>. In a hands-on session the
    /// avatar's intent source IS the <c>BikeLayer</c> — it is the outermost decorator — so that
    /// predicate answered "scripted" for a human at a keyboard and the mouse was never polled.
    /// <b>The drift was unreachable by hand</b>, and with it every charge tier, both charge models,
    /// the spark and the exit boost.</para>
    ///
    /// <para>The engine self-test passed 12/12 throughout, because it drives its own scripted flag
    /// and never took the dead branch. What caught it was Talon's first session logging <b>zero
    /// drift entries across 104 seconds above the 3.5 m/s gate</b> — not a preference, an
    /// impossibility.</para>
    ///
    /// <para><c>BikeLayer</c> withholding the property is CORRECT and must not be "fixed": its own
    /// comment records why (the repo's achievement guard lets only the real keyboard source claim a
    /// person, and a lab body earns no achievements). This test exists so the next reader learns
    /// that from a red rather than from a playtest.</para>
    /// </summary>
    [Fact]
    public void BikeLayerDoesNotClaimHumanInput_SoNoLabFeatureMayBeGatedOnIt()
    {
        System.Reflection.PropertyInfo? declared = typeof(BikeLayer).GetProperty(
            nameof(IIntentSource.IsHumanInput),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
          | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.True(declared is null,
            "BikeLayer now declares IsHumanInput. That may be right, but the harness's drift "
          + "predicate was rewritten on the assumption that it does NOT — re-read the driftHeld "
          + "paragraph in MovementPlayground.BikeHandling.cs before changing either.");

        // And the consequence, as an assertion rather than as prose: a source that declares the
        // property reports true, one that does not reports the interface default — so a DECORATOR
        // that omits it reports "not human" however human the source it wraps is. That asymmetry
        // is the whole defect, in two lines.
        Assert.True(((IIntentSource)new HumanClaimingSource()).IsHumanInput);
        Assert.False(((IIntentSource)new SilentDecorator()).IsHumanInput,
            "a source that does not declare IsHumanInput must fall through to the false default — "
          + "which is exactly why a lab feature may not be gated on it");
    }

    /// <summary>A stand-in for the real keyboard source, which cannot be constructed without an
    /// engine. Only its <c>IsHumanInput</c> is exercised.</summary>
    private sealed class HumanClaimingSource : IIntentSource
    {
        public MoveIntent NextIntent(double delta) => MoveIntent.None;
        public bool IsHumanInput => true;
    }

    /// <summary>A decorator shaped exactly like <c>BikeLayer</c>: it wraps a source and does not
    /// declare <c>IsHumanInput</c>. It reports false even wrapping a human.</summary>
    private sealed class SilentDecorator : IIntentSource
    {
        public MoveIntent NextIntent(double delta) => MoveIntent.None;
    }

    // --- The presets -------------------------------------------------------------------------------

    /// <summary>Every preset is reachable by the digit it names, and every one of them is a whole
    /// record rather than a patch — so loading one can never leave half of a previous preset in
    /// force.</summary>
    [Fact]
    public void EveryPresetIsReachableByItsDigit()
    {
        foreach ((string key, string name, BikeHandlingTuning tuning) in BikeHandlingTuning.Presets)
        {
            int digit = int.Parse(key, System.Globalization.CultureInfo.InvariantCulture);
            var found = BikeHandlingTuning.ForKey(digit);
            Assert.True(found.HasValue, $"preset {key} ({name}) is not reachable by its own digit");
            Assert.Equal(tuning, found!.Value.Tuning);
        }
        Assert.Null(BikeHandlingTuning.ForKey(9));
    }

    /// <summary>Every preset's rows are sane enough that the model cannot produce a NaN, an
    /// infinity, or a negative lean out of them. A preset row is a number Talon may drag to an
    /// extreme in one keystroke; none of them may take the model with it.</summary>
    [Fact]
    public void EveryPresetProducesFiniteHandlingAtEverySpeed()
    {
        foreach ((string key, string name, BikeHandlingTuning t) in BikeHandlingTuning.Presets)
        {
            for (float speed = 0f; speed <= 14f; speed += 0.5f)
            {
                float lean = BikeHandling.LeanSteadyDeg(speed, 2.5f, t);
                float mul = BikeHandling.TurnMultiplier(speed, 9.4f, t);
                Assert.True(float.IsFinite(lean) && float.IsFinite(mul),
                    $"preset {key} ({name}) produced lean {lean} / mul {mul} at {speed} m/s");
                Assert.True(Mathf.Abs(lean) <= Mathf.Max(t.LeanMaxDeg, 0f) + 1e-4f);
                Assert.True(mul > 0f, $"preset {key} produced a non-positive turn multiplier");
            }
            Assert.True(t.DriftEntrySpeedMps > 0f, $"preset {key} would let the drift enter at rest");
            Assert.True(t.DriftTier1Sec < t.DriftTier2Sec && t.DriftTier2Sec < t.DriftTier3Sec,
                $"preset {key}'s tier thresholds are not in order");
        }
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>Enter a drift and hold it for <paramref name="seconds"/> at a qualifying speed,
    /// returning the live state. The duration model, so the charge is the wall clock.</summary>
    private static BikeHandling.DriftState HeldForSeconds(float seconds, out int ticks)
    {
        BikeHandling.DriftState s = BikeHandling.DriftState.Rest;
        ticks = 0;
        while (s.ChargeSec < seconds)
        {
            BikeHandling.DriftResult r = BikeHandling.DriftStep(s, mounted: true, grounded: true,
                held: true, speedMps: 9f, steer: 0f, Tick, H);
            s = r.Next;
            ticks++;
            if (!s.Active && ticks > 1)
                throw new InvalidOperationException(
                    $"the drift ended after {ticks} ticks, before reaching {seconds:F2} s of charge");
            if (ticks > 60 * 60)
                throw new InvalidOperationException("the drift never charged");
        }
        return s;
    }
}
