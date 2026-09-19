using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-4c — the apex hang and the landing dip</b>, against
/// <c>docs/design/2026-08-26-movement-tuning-surface.md</c> §4 and §5.
///
/// <para><b>Nothing here parks a non-default <see cref="MotorTuning.Current"/>, and that shaped the
/// whole file</b> (MOVE-4b's standing rule). Seven test classes now read <c>AvatarMotor</c>'s
/// properties live and xUnit runs classes in parallel, so a test that tuned the hang even briefly
/// would surface as an intermittent red in a file nobody touched. The way out is that the two new
/// shapes are <b>pure functions of their arguments</b> — <see cref="AvatarMotor.ApexHangFactor"/>
/// and <see cref="SandboxCamera.DipDepthAt"/> — so every non-default value in this file is passed
/// as a parameter, and the arc arithmetic runs on <see cref="MotorTuningInvariants"/>, which
/// evaluates a candidate tuning it was handed rather than the one that is applied.</para>
/// </summary>
public class ApexHangAndLandingDipTests
{
    private const float Dt = AvatarMotor.TickDelta;

    private static void Near(float expected, float actual, float tol, string what)
        => Assert.True(Math.Abs(expected - actual) <= tol,
            $"{what}: expected {expected:F4} +/- {tol:F4}, got {actual:F4}");

    // =============================================================================================
    // 1. THE NO-OP. ApexHangStrength = 0.00 must reproduce the jump Talon played and approved.
    // =============================================================================================

    /// <summary><c>GravityFor</c> exactly as it was before MOVE-4c — the three-way branch, no hang.
    /// Written out rather than referenced so the comparison below is against an independent
    /// statement of the old rule and not against the new code with a flag flipped.</summary>
    private static float PreMove4cGravityFor(float velocityY, bool jumpHeld, bool locked)
    {
        if (velocityY < 0f)
            return AvatarMotor.Gravity * AvatarMotor.FallGravityMultiplier;
        if (velocityY > 0f && !jumpHeld && !locked)
            return AvatarMotor.Gravity * AvatarMotor.JumpReleaseGravityMultiplier;
        return AvatarMotor.Gravity;
    }

    /// <summary>Every input the sweep below visits: the whole velocity range a jump passes through,
    /// finely around zero and the 2.00 m/s window edge where the hang lives.</summary>
    private static IEnumerable<float> VerticalVelocitySweep()
    {
        for (int i = -1200; i <= 1200; i++)
            yield return i * 0.01f;          // -12 .. +12 m/s in 1 cm/s steps
    }

    /// <summary>
    /// <b>The hang is exactly the pre-MOVE-4c branch, SCALED</b> — every velocity, every
    /// held/locked/ballistic combination, <c>Assert.Equal</c> on the raw float rather than a
    /// tolerance.
    ///
    /// <para><b>MOVE-8 changed what this test can claim, and the change is worth reading.</b> It
    /// used to assert that <c>GravityFor</c> was <i>bit-identical</i> to its pre-MOVE-4c self,
    /// which was true only because <c>ApexHangStrength</c> shipped at its exact no-op. Talon ruled
    /// the hang to 0.30 on 2026-08-28, so identity is gone by construction and asserting it would
    /// only be asserting that the ruling had not landed. <b>What replaces it is stronger, not
    /// weaker:</b> the shipped function must equal the old branch multiplied by
    /// <see cref="AvatarMotor.ApexHangFactor"/> and by nothing else, over the same 19,000-point
    /// sweep — which pins the SHAPE of MOVE-4c's change rather than its absence, and would still
    /// catch a hang that had leaked into the locked or ballistic paths (it must not: both return
    /// before the term, and the two rows below say so).</para>
    ///
    /// <para><b>The no-op itself is still asserted</b>, one test down, on a candidate tuning — a
    /// term that claims to be inert at zero has to be checked at zero, and there is no longer a
    /// shipped zero to check it at.</para>
    ///
    /// <para><b>With its positive control</b>, which is the half that makes the check worth
    /// anything: the same sweep run against a reference carrying a DIFFERENT hang finds thousands
    /// of disagreements. A method that cannot detect a changed arc cannot be trusted when it
    /// reports an unchanged one.</para>
    /// </summary>
    [Fact]
    public void AtTheShippedStrength_GravityForIsThePreMove4cBranchScaledByTheHangAndNothingElse()
    {
        Assert.Equal(0.30f, AvatarMotor.ApexHangStrength);

        int compared = 0;
        foreach (float vy in VerticalVelocitySweep())
        foreach (bool held in new[] { false, true })
        foreach (bool locked in new[] { false, true })
        {
            // Locked and ballistic bodies return BEFORE the hang term, so their expectation is the
            // bare branch — which is the assertion that the term did not leak into the two paths
            // MOVE-4c promised to leave alone.
            float expected = PreMove4cGravityFor(vy, held, locked);
            if (!locked)
                expected *= AvatarMotor.ApexHangFactor(vy, AvatarMotor.ApexHangStrength,
                    AvatarMotor.ApexHangWindowMps);
            Assert.Equal(expected, AvatarMotor.GravityFor(vy, held, locked));
            compared++;
        }
        Assert.True(compared > 9000, $"the sweep only compared {compared} points");

        // POSITIVE CONTROL. Same sweep, same comparison, against a reference carrying the
        // recommended first experiment's hang. If this does not disagree, the sweep above proves
        // nothing.
        int disagreements = 0;
        foreach (float vy in VerticalVelocitySweep())
        {
            float otherHang = PreMove4cGravityFor(vy, true, false)
                             * AvatarMotor.ApexHangFactor(vy, 0.60f, 2.00f);
            if (otherHang != AvatarMotor.GravityFor(vy, true, false))
                disagreements++;
        }
        Assert.True(disagreements > 200,
            $"the positive control found only {disagreements} disagreements — the sweep is blind");
    }

    /// <summary><b>The term is still the exact no-op at strength zero</b> — MOVE-4c's acceptance
    /// criterion 2, moved onto a candidate tuning by MOVE-8 because the shipped strength is no
    /// longer zero. Run through <see cref="MotorTuningInvariants.GravityFor"/>, which is the
    /// independently-authored mirror of the shipped cut, so this is still two authorships being
    /// compared rather than one function with itself.</summary>
    [Fact]
    public void AtStrengthZero_TheHangTermIsStillTheExactNoOp()
    {
        MotorTuning off = MotorTuning.Default with { ApexHangStrength = 0f };
        int compared = 0;
        foreach (float vy in VerticalVelocitySweep())
        foreach (bool held in new[] { false, true })
        {
            float bare = vy < 0f ? off.Gravity * off.FallGravityMultiplier
                : vy > 0f && !held ? off.Gravity * off.JumpReleaseGravityMultiplier
                : off.Gravity;
            Assert.Equal(bare, MotorTuningInvariants.GravityFor(off, vy, held));
            compared++;
        }
        Assert.True(compared > 4000, $"the sweep only compared {compared} points");
    }

    /// <summary>One jump on the discrete 60 Hz model, evaluated against a candidate tuning. Byte for
    /// byte the model <c>AirborneControlTests.SimulateJump</c> (<c>:555</c>) runs, including the
    /// launch tick whose free <c>JumpVelocity x dt</c> is worth 0.14 m.</summary>
    private static (float ApexM, float AirtimeSec) Sim(in MotorTuning t, int holdForTicks)
        => MotorTuningInvariants.SimulateJump(t, holdForTicks);

    /// <summary>
    /// The same jump in <b>MOVE-3d's reporting convention</b> — the one the playground's readout
    /// uses and the one Talon's approved figures are quoted in. <c>MovementPlayground</c> latches
    /// <c>_takeoffPos</c> on the first tick <c>Grounded</c> goes false, which is AFTER the launch
    /// tick has already advanced the body, and starts its airtime clock on that same tick. So its
    /// apex is measured from the post-launch height and its airtime is one tick shorter than the
    /// simulation's. Both offsets are exact, not fitted.
    /// </summary>
    private static (float ApexM, float AirtimeSec) AsPlayed(in MotorTuning t, int holdForTicks)
    {
        (float apex, float airtime) = Sim(t, holdForTicks);
        return (apex - t.JumpVelocity * Dt, airtime - Dt);
    }

    /// <summary>
    /// <b>The shipped arc, in MOVE-3d's convention</b> (MOVE-4c's acceptance criterion 2, re-taken
    /// at MOVE-8). Held sprint <c>1.407 m / 0.667 s</c>, jog tap <c>0.300 m / 0.233 s</c>, to three
    /// decimals, at <see cref="MotorTuning.Default"/> — Talon's ruled tuning. It read
    /// <c>1.534 / 0.717</c> and <c>0.467 / 0.317</c> until 2026-08-28, and the criterion this test
    /// carries has changed with it: it was "the approved arc is UNMOVED", and it is now "the arc is
    /// exactly what MOVE-8 measured, and moves only when a ruling moves it".
    ///
    /// <para>The tap is <c>holdForTicks: 0</c> here and <c>1</c> in
    /// <c>AirborneControlTests.JumpApex_BracketsTheSpecRange</c>, and both are right: the capture
    /// run releases <c>JumpHeld</c> on the tick after the press, and the press tick is the launch
    /// tick, whose gravity branch is skipped entirely. So the playground's tap is never held for a
    /// single AIRBORNE tick, while the unit test's is held for one.</para>
    /// </summary>
    [Fact]
    public void TheApprovedArcIsUnchanged_InMove3dsOwnReportingConvention()
    {
        (float apexHeld, float airtimeHeld) = AsPlayed(MotorTuning.Default, int.MaxValue);
        (float apexTap, float airtimeTap) = AsPlayed(MotorTuning.Default, 0);

        // MOVE-8: re-measured at Talon's ruled tuning. These four ARE the packet's headline arc
        // numbers and MotorArcTests derives the same four independently, so a disagreement between
        // the two files is a real finding rather than a stale literal.
        Near(1.407f, apexHeld, 0.0005f, "held sprint apex");
        Near(0.667f, airtimeHeld, 0.0005f, "held sprint airtime");
        Near(0.300f, apexTap, 0.0005f, "jog tap apex");
        Near(0.233f, airtimeTap, 0.0005f, "jog tap airtime");
    }

    /// <summary>The same arc in the unit suite's own convention — spec §3.7's <c>1.6739 /
    /// 0.7333</c> and <c>0.6978 / 0.350</c>. Asserted here so that if the two conventions ever stop
    /// differing by exactly the launch tick, this file says so rather than quietly agreeing.</summary>
    [Fact]
    public void TheJumpApexTestsOwnModel_StillProducesSpec37sFigures()
    {
        (float apexHeld, float airtimeHeld) = Sim(MotorTuning.Default, int.MaxValue);
        (float apexTap, float airtimeTap) = Sim(MotorTuning.Default, 1);

        // MOVE-8: the same jump in the unit suite's convention, re-measured at the ruled tuning.
        // The pair still differs from the block above by EXACTLY one launch tick, which is the
        // property this test exists to hold: 1.5470 - 8.4/60 = 1.4070, 0.6833 - 1/60 = 0.6667.
        Near(1.5470f, apexHeld, 0.0005f, "apexHeld, launch tick included");
        Near(0.6833f, airtimeHeld, 0.0005f, "airtimeHeld, launch tick included");
        Near(0.5408f, apexTap, 0.0005f, "apexTap, launch tick included");
        Near(0.2833f, airtimeTap, 0.0005f, "airtimeTap, launch tick included");
        Assert.True(MotorTuningInvariants.JumpApexAssertionsHold(MotorTuning.Default),
            "the shipped tuning must satisfy every assertion JumpApex_BracketsTheSpecRange makes");
    }

    // =============================================================================================
    // 2. THE SHAPE (spec §4.1-4.2) — continuous, and continuously differentiable.
    // =============================================================================================

    [Fact]
    public void ApexHangFactor_IsFullAtTheApexAndGoneAtTheWindowEdge()
    {
        Assert.Equal(1f - 0.35f, AvatarMotor.ApexHangFactor(0f, 0.35f, 2.00f), 1e-6f);
        Assert.Equal(1f, AvatarMotor.ApexHangFactor(2.00f, 0.35f, 2.00f), 1e-6f);
        Assert.Equal(1f, AvatarMotor.ApexHangFactor(9f, 0.90f, 2.00f), 1e-6f);
        Assert.Equal(1f, AvatarMotor.ApexHangFactor(-9f, 0.90f, 2.00f), 1e-6f);
        // Symmetric in the sign of v_y: the hang is a function of |v_y|, so the rise and the fall
        // are attenuated identically at the same speed. That symmetry is what makes the existing
        // 22 -> 29.7 step at zero SCALED rather than reshaped.
        for (float v = 0f; v <= 3f; v += 0.05f)
            Assert.Equal(AvatarMotor.ApexHangFactor(v, 0.6f, 2.00f),
                AvatarMotor.ApexHangFactor(-v, 0.6f, 2.00f), 1e-6f);
    }

    /// <summary>
    /// <b>Acceleration is continuous entering and leaving the hang — demonstrated, not asserted</b>
    /// (acceptance criterion 3). The value AND its slope are swept across the window edge; the
    /// second difference stays at the smooth-function scale.
    ///
    /// <para><b>The positive control is a linear ramp</b>, <c>w = 1 - u</c>: continuous in the value
    /// and NOT continuous in the slope. It is the shape spec §4.2 rejects as "a hitch rather than a
    /// float", and this test shows the measurement can tell the two apart — which is the only
    /// reason the smoothstep's result means anything.</para>
    /// </summary>
    [Fact]
    public void TheHangIsC1AtBothEnds_AndALinearRampWouldNotBe()
    {
        const float S = 0.90f, W = 2.00f, H = 0.001f;

        float SmoothSlopeJump = MaxSecondDifference(v => AvatarMotor.ApexHangFactor(v, S, W), H);
        float LinearSlopeJump = MaxSecondDifference(v =>
        {
            float u = Math.Min(1f, Math.Abs(v) / W);
            return 1f - S * (1f - u);                       // the rejected linear ramp
        }, H);

        // The smoothstep's curvature is bounded; the ramp has a genuine corner at the window edge
        // and at zero, which shows up as a second difference an order of magnitude larger.
        Assert.True(SmoothSlopeJump < 0.02f,
            $"smoothstep second difference {SmoothSlopeJump:F5} — that is a kink, not a curve");
        Assert.True(LinearSlopeJump > 10f * SmoothSlopeJump,
            $"positive control failed: the linear ramp's second difference was {LinearSlopeJump:F5} "
            + $"against the smoothstep's {SmoothSlopeJump:F5}, so this measurement cannot tell a "
            + "corner from a curve and its verdict on the smoothstep is worthless");
    }

    /// <summary>Largest |f(v+h) - 2f(v) + f(v-h)| / h over the sweep — a discrete second difference,
    /// which is bounded for a C1 function and blows up at a slope discontinuity.</summary>
    private static float MaxSecondDifference(Func<float, float> f, float h)
    {
        float worst = 0f;
        for (float v = -3f; v <= 3f; v += h)
        {
            float d2 = Math.Abs(f(v + h) - 2f * f(v) + f(v - h)) / h;
            if (d2 > worst)
                worst = d2;
        }
        return worst;
    }

    /// <summary>
    /// <b>The hang introduces no new discontinuity in gravity</b> (spec §4.2 property 4). Each side
    /// of <c>v_y = 0</c> is swept separately, because the motor already HAS a step there — rising
    /// uses 22, falling 29.7 — and this term neither removes nor widens it: the same factor
    /// multiplies both sides, so the pre-existing step is scaled, never reshaped.
    /// </summary>
    [Fact]
    public void GravityHasNoNewStep_OnEitherSideOfZero_EvenAtTheStrongestHang()
    {
        MotorTuning t = MotorTuning.Default with { ApexHangStrength = 0.90f, ApexHangWindowMps = 2f };
        const float H = 0.002f;

        // Falling side, and the rising-and-held side. (Rising-and-released is the same shape times
        // a constant, so it cannot introduce a step the held branch does not have.)
        foreach (bool rising in new[] { true, false })
        {
            float sign = rising ? 1f : -1f;
            float prev = float.NaN;
            for (float m = H; m <= 4f; m += H)
            {
                float v = sign * m;
                float g = MotorTuningInvariants.GravityFor(t, v, jumpHeld: true);
                if (!float.IsNaN(prev))
                    Assert.True(Math.Abs(g - prev) < 0.25f,
                        $"gravity stepped {Math.Abs(g - prev):F4} m/s^2 between v_y={v - sign * H:F3} "
                        + $"and {v:F3} — a step that size is a hitch");
                prev = g;
            }
        }

        // And the step AT zero is the shipped 22 -> 29.7 ratio, scaled by one common factor.
        float up = MotorTuningInvariants.GravityFor(t, 0f, jumpHeld: true);
        float down = MotorTuningInvariants.GravityFor(t, -1e-6f, jumpHeld: true);
        Near(t.FallGravityMultiplier, down / up, 1e-3f,
            "the ratio across zero — unchanged by the hang, because one factor multiplies both");
    }

    /// <summary>
    /// <b>Releasing the key always ends the ascent sooner than holding it</b> (spec §4.2 property 5
    /// and §4.5) — at every velocity, strength and window, not merely at today's numbers. This is
    /// what stops the apex being raised by mashing the key, and it is structural: the same factor
    /// multiplies both branches.
    /// </summary>
    [Fact]
    public void TheReleaseCutOutranksTheHeldBranch_AtEveryStrengthAndWindow()
    {
        foreach (float s in new[] { 0f, 0.10f, 0.35f, 0.60f, 0.90f })
        foreach (float w in new[] { 0.25f, 1f, 2f, 4f, 6f })
        {
            MotorTuning t = MotorTuning.Default with { ApexHangStrength = s, ApexHangWindowMps = w };
            for (float v = 0.01f; v <= 9f; v += 0.01f)
            {
                float gHeld = MotorTuningInvariants.GravityFor(t, v, jumpHeld: true);
                float gReleased = MotorTuningInvariants.GravityFor(t, v, jumpHeld: false);
                Assert.True(gReleased > gHeld,
                    $"at S={s}, W={w}, v_y={v:F2} the release cut ({gReleased:F3}) did not outrank "
                    + $"the held branch ({gHeld:F3}) — energy can be gained by mashing the key");
            }
        }
    }

    /// <summary>
    /// <b>Locked and ballistic bodies get exactly today's gravity, bit for bit, at any hang</b>
    /// (spec §4.2). This is what keeps <c>EelProfile</c>'s <c>RiseSec</c> / <c>PeakHeightM</c> /
    /// <c>FallSec</c> arithmetic — all computed from bare <c>Gravity</c> — true from the other side.
    /// </summary>
    [Fact]
    public void ALockedOrBallisticBodyNeverFeelsTheHang()
    {
        MotorTuning t = MotorTuning.Default with { ApexHangStrength = 0.90f, ApexHangWindowMps = 6f };
        for (float v = -6f; v <= 6f; v += 0.05f)
        foreach (bool held in new[] { false, true })
        {
            float bare = v < 0f ? t.Gravity * t.FallGravityMultiplier : t.Gravity;
            Assert.Equal(bare, MotorTuningInvariants.GravityFor(t, v, held, locked: true), 1e-4f);
        }
    }

    /// <summary>
    /// <b>The mirror and the motor agree about the hang at NON-default values</b>, which the
    /// existing <c>TheInvariantMirrorAgreesWithTheMotor_AcrossEveryBranch</c> cannot check because
    /// it runs at <see cref="MotorTuning.Default"/>, where the early return fires and there is no
    /// hang to compare. Two independent authorships of one shape, swept over a grid — and no test
    /// had to park a tuned <c>MotorTuning.Current</c> to get it.
    /// </summary>
    [Fact]
    public void TheInvariantMirrorsTheApexHangShape_AtEveryStrengthAndWindow()
    {
        foreach (float s in new[] { 0.01f, 0.10f, 0.35f, 0.55f, 0.90f })
        foreach (float w in new[] { 0.25f, 0.5f, 1f, 2f, 3.5f, 6f })
        {
            MotorTuning t = MotorTuning.Default with { ApexHangStrength = s, ApexHangWindowMps = w };
            for (float v = -8f; v <= 8f; v += 0.02f)
            {
                // The mirror's attenuation, recovered by dividing out the branch it multiplies.
                float withHang = MotorTuningInvariants.GravityFor(t, v, jumpHeld: true);
                float withoutHang = MotorTuningInvariants.GravityFor(
                    t with { ApexHangStrength = 0f }, v, jumpHeld: true);
                float mirrorFactor = withHang / withoutHang;
                Near(AvatarMotor.ApexHangFactor(v, s, w), mirrorFactor, 2e-4f,
                    $"mirror vs motor at S={s}, W={w}, v_y={v:F2}");
            }
        }
    }

    // =============================================================================================
    // 3. THE CEILING (acceptance criterion 4, spec §3.7).
    // =============================================================================================

    /// <summary>The largest strength, to the nearest 0.001, at which every assertion
    /// <c>JumpApex_BracketsTheSpecRange</c> makes still holds — found by walking the discrete
    /// simulation rather than by any closed form.</summary>
    private static float MeasuredCeiling(float windowMps)
    {
        MotorTuning t = MotorTuning.Default with { ApexHangWindowMps = windowMps };
        float last = 0f;
        for (int i = 0; i <= 900; i++)
        {
            float s = i / 1000f;
            if (!MotorTuningInvariants.JumpApexAssertionsHold(t with { ApexHangStrength = s }))
                break;
            last = s;
        }
        return last;
    }

    /// <summary>
    /// <b>The ceiling the knob carries is the ceiling the test actually imposes</b> (acceptance
    /// criterion 4). Measured by stepping the same simulation
    /// <c>AirborneControlTests.SimulateJump</c> runs, then compared with what
    /// <c>MotorTuningKnobs.ApexHangStrength.PinMax</c> reports.
    ///
    /// <para><b>MOVE-8: the measured ceiling at a 2.00 m/s window is now 0.600</b>, up from 0.350,
    /// and the shipped strength sits at 0.300 — half the ceiling, which is real margin where 0.35
    /// used to have a thousandth. The headroom grew because the window's binding assertion is the
    /// held airtime and the ruled gravity rows shortened the baseline flight, so there is more room
    /// under the same tolerance. <b>At a 6.00 m/s window the ceiling is 0.296, i.e. BELOW the
    /// shipped strength</b> — which is exactly why the panel evaluates this live instead of
    /// printing a number beside the slider.</para>
    /// </summary>
    [Fact]
    public void TheApexHangCeiling_IsWhereTheJumpApexTestActuallyBreaks()
    {
        foreach (float w in new[] { 1.00f, 2.00f, 3.00f, 4.00f, 6.00f })
        {
            MotorTuning t = MotorTuning.Default with { ApexHangWindowMps = w };
            float measured = MeasuredCeiling(w);
            float reported = MotorTuningInvariants.ApexHangStrengthCeiling(t);

            Near(measured, reported, 0.0015f, $"ceiling at window {w:F2}");
            Assert.True(MotorTuningInvariants.JumpApexAssertionsHold(
                    t with { ApexHangStrength = reported }),
                $"the reported ceiling {reported:F4} at window {w:F2} does not itself pass");
            Assert.False(MotorTuningInvariants.JumpApexAssertionsHold(
                    t with { ApexHangStrength = reported + 0.002f }),
                $"one step past the reported ceiling {reported:F4} at window {w:F2} still passes — "
                + "the ceiling is not tight, so the panel would be warning at the wrong place");

            // The knob table's own live pin must be reading the same function.
            Assert.Equal(reported, MotorTuningKnobs.ApexHangStrength.PinMax!(t), 1e-4f);
        }

        Near(0.600f, MeasuredCeiling(2.00f), 0.0015f, "the headline ceiling at a 2.00 m/s window");
        // MOVE-8: the shipped strength is inside its own ceiling at the shipped window, with room.
        Assert.True(MotorTuning.Default.ApexHangStrength < MeasuredCeiling(MotorTuning.Default.ApexHangWindowMps));
    }

    /// <summary>
    /// <b>Spec §3.7's closed form overstates the ceiling, which is why MOVE-4c stopped using it.</b>
    /// <c>Delta_t = W (1/G_rise + 1/G_fall) (1/(1 - S/2) - 1)</c> is continuous-math applied to a
    /// quantity the test measures in whole 60 Hz ticks. At the shipped tuning and a 2.00 m/s window
    /// it said 0.376 where the simulation broke at 0.351. <b>MOVE-8 re-measured it at the ruled
    /// tuning and the correction got LARGER, not smaller</b>: 0.709 against a measured 0.600, so
    /// the closed form now overstates by a sixth rather than by a fourteenth. This test is the
    /// demonstration.
    /// </summary>
    [Fact]
    public void TheClosedFormWouldHaveReportedAStrengthThatRedensTheSuite()
    {
        MotorTuning t = MotorTuning.Default with { ApexHangWindowMps = 2.00f };
        (_, float baseline) = Sim(t with { ApexHangStrength = 0f }, int.MaxValue);

        float a = t.ApexHangWindowMps * (1f / t.Gravity + 1f / (t.Gravity * t.FallGravityMultiplier));
        // The upper edge of MotorTuningInvariants' held-airtime window, hand-typed there and
        // hand-typed here for the same reason: this reconstruction has to use the number the test
        // it is reconstructing actually uses. MOVE-8: 0.770 -> 0.743 with the re-centred window.
        float b = (0.743f - baseline) / a;
        float closedForm = 2f * b / (1f + b);

        Near(0.7093f, closedForm, 0.002f, "spec §3.7's closed form at a 2.00 m/s window");
        float measured = MeasuredCeiling(2.00f);
        Assert.True(closedForm > measured + 0.02f,
            $"the closed form ({closedForm:F4}) is supposed to overstate the measured ceiling "
            + $"({measured:F4}) — if it no longer does, this correction can be retired");
        Assert.False(MotorTuningInvariants.JumpApexAssertionsHold(
                t with { ApexHangStrength = closedForm }),
            "the closed form's answer must be a value the JumpApex test rejects, or there was "
            + "nothing to correct");
    }

    /// <summary>The same constraint read the other way round: at a strength, how wide may the
    /// window get?
    ///
    /// <para><b>MOVE-8 made this row live, and that is the whole edit.</b> The window ceiling was
    /// an unconditional 6.00 — the hard max — because the hang shipped OFF and a term that does not
    /// run cannot constrain anything. At the ruled strength of 0.30 the ceiling is <b>5.90</b>: the
    /// shipped 2.00 m/s window has plenty of room, but the row is no longer inert, so the
    /// unbounded case is asserted where it still exists (strength 0) rather than at the shipped
    /// tuning.</para></summary>
    [Fact]
    public void TheWindowCeilingIsTheSameConstraintReadBackwards()
    {
        Assert.Equal(6.00f, MotorTuningInvariants.ApexHangWindowCeiling(
            MotorTuning.Default with { ApexHangStrength = 0f }), 1e-3f);
        Near(5.90f, MotorTuningInvariants.ApexHangWindowCeiling(MotorTuning.Default), 0.01f,
            "the window ceiling at the ruled strength");
        Assert.True(MotorTuning.Default.ApexHangWindowMps
            < MotorTuningInvariants.ApexHangWindowCeiling(MotorTuning.Default));

        MotorTuning t = MotorTuning.Default with { ApexHangStrength = 0.35f };
        float w = MotorTuningInvariants.ApexHangWindowCeiling(t);
        Assert.True(MotorTuningInvariants.JumpApexAssertionsHold(t with { ApexHangWindowMps = w }),
            $"the reported window ceiling {w:F4} does not itself pass");
        Assert.False(MotorTuningInvariants.JumpApexAssertionsHold(
                t with { ApexHangWindowMps = w + 0.02f }),
            $"one step past the reported window ceiling {w:F4} still passes");
    }

    /// <summary>
    /// <b>Spec §4.4's recommended first experiment, measured</b> — <c>0.35 / 2.00</c> buys airtime,
    /// not height: <c>+4.7%</c> of airtime against <c>+0.57%</c> of apex. A hang is time at the top.
    ///
    /// <para><b>MOVE-8 re-measured all four at the ruled tuning</b>, and footnote (a) below has
    /// been overtaken: 0.35 sat one thousandth under a 0.350 ceiling at the pre-ruling tuning, and
    /// the ceiling is now 0.600, so §4.4's experiment has real room. The DIRECTION of the effect —
    /// airtime rather than height — is unchanged and is what the ratio assertion still guards.</para>
    ///
    /// <para>Footnote (b) survives verbatim: §4.4's table gives the jog tap's airtime at this
    /// setting as one tick less than the tick-by-tick answer, while the apex column of the same row
    /// reproduces exactly — so the model is the same one and the tap airtime is a slip.</para>
    /// </summary>
    [Fact]
    public void TheRecommendedFirstExperiment_BuysAirtimeRatherThanHeight()
    {
        MotorTuning t = MotorTuning.Default with { ApexHangStrength = 0.35f, ApexHangWindowMps = 2f };

        (float apexHeld, float airtimeHeld) = AsPlayed(t, int.MaxValue);
        (float apexTap, float airtimeTap) = AsPlayed(t, 0);

        Near(1.4081f, apexHeld, 0.0005f, "held sprint apex at 0.35 / 2.00");
        Near(0.6833f, airtimeHeld, 0.0005f, "held sprint airtime at 0.35 / 2.00");

        // The comparison is against the SAME tuning with the hang switched off, not against the
        // shipped tuning: MOVE-8 ships a 0.30 hang, so "shipped" is no longer a hang-free baseline
        // and the old comparison would have been measuring 0.05 of strength rather than 0.35.
        MotorTuning off = t with { ApexHangStrength = 0f };
        (float baseApex, float baseAir) = AsPlayed(off, int.MaxValue);
        Assert.True((airtimeHeld - baseAir) / baseAir > 7f * ((apexHeld - baseApex) / baseApex),
            "the hang is supposed to move airtime far more than apex; it did not");

        Near(0.600f, MotorTuningInvariants.ApexHangStrengthCeiling(t), 0.0015f,
            "the ceiling 0.35 now sits comfortably under");
    }

    // =============================================================================================
    // 4. THE LANDING — one gate, one curve, three channels (spec §5.1-5.2).
    // =============================================================================================

    /// <summary>
    /// <b>There is exactly one landing-intensity curve now</b> (acceptance criterion 7). Before
    /// MOVE-4c, <c>AvatarVisual</c> clamped to <c>[0.25, 1]</c> and <c>SandboxAvatar</c>'s
    /// <c>ActorEvent.Land</c> fan-out clamped the same quantity off the same gate to <c>[0, 1]</c>.
    /// The floor is 0.25 and the argument is <c>LandMinIntensity</c>'s own, generalised: a landing
    /// that qualified must be perceptible in every channel it drives, or the gate reads as a bug on
    /// the frames just past it.
    /// </summary>
    [Fact]
    public void TheLandingIntensityIsOneCurveWithTheLegibilityFloor()
    {
        Assert.Equal(AvatarVisual.LandMinIntensity,
            AvatarVisual.LandIntensityFor(AvatarVisual.LandMinFallMps), 1e-5f);
        Assert.Equal(1f, AvatarVisual.LandIntensityFor(AvatarVisual.LandFullFallMps), 1e-5f);
        Assert.Equal(1f, AvatarVisual.LandIntensityFor(100f), 1e-5f);
        Assert.Equal(AvatarVisual.LandMinIntensity, AvatarVisual.LandIntensityFor(0f), 1e-5f);

        // Monotone in between, and never below the floor.
        float prev = 0f;
        for (float fall = AvatarVisual.LandMinFallMps; fall <= 20f; fall += 0.05f)
        {
            float got = AvatarVisual.LandIntensityFor(fall);
            Assert.True(got >= AvatarVisual.LandMinIntensity - 1e-6f, $"floor breached at {fall:F2}");
            Assert.True(got >= prev - 1e-6f, $"intensity fell between {fall - 0.05f:F2} and {fall:F2}");
            prev = got;
        }
    }

    /// <summary>
    /// <b>What unifying the two curves actually moves, measured</b> — the argument a third party can
    /// check, which is what the packet asked for rather than a preference.
    ///
    /// <para>The floor only bites below <c>0.25 x LandFullFallMps = 3.5 m/s</c>, so the affected
    /// band is <c>[2.5, 3.5)</c>. In it the intensity rises by at most <c>0.0714</c>. The puffling
    /// profile's <c>land_dust</c> response gates at <c>MinIntensity = 0.428</c>, which is a 5.99 m/s
    /// fall — far outside the band — so <b>no response changes whether it fires</b>. The only reader
    /// that scales with intensity is <c>land_pad</c>, at <c>IntensityVolumeBoostDb = 4</c>: at most
    /// <b>0.286 dB</b>, well under the ~1 dB a listener can hear.</para>
    /// </summary>
    [Fact]
    public void UnifyingTheCurvesFlipsNoGateAndMovesAtMostAQuarterOfADecibel()
    {
        const float DustMinIntensity = 0.428f;      // puffling_presentation.tres, land_dust
        const float LandPadBoostDb = 4f;            // puffling_presentation.tres, land_pad

        float worstDelta = 0f;
        for (float fall = AvatarVisual.LandMinFallMps; fall <= 30f; fall += 0.01f)
        {
            float before = Mathf.Clamp(fall / AvatarVisual.LandFullFallMps, 0f, 1f);  // the old curve
            float after = AvatarVisual.LandIntensityFor(fall);
            worstDelta = Math.Max(worstDelta, Math.Abs(after - before));

            Assert.True((before >= DustMinIntensity) == (after >= DustMinIntensity),
                $"the dust gate flipped at fall {fall:F2} ({before:F4} -> {after:F4})");
        }

        Near(0.25f - AvatarVisual.LandMinFallMps / AvatarVisual.LandFullFallMps, worstDelta, 1e-4f,
            "the largest intensity change the unification causes");
        Assert.True(worstDelta * LandPadBoostDb < 0.30f,
            $"land_pad would move {worstDelta * LandPadBoostDb:F4} dB, which is audible");
    }

    // =============================================================================================
    // 5. THE CAMERA DIP (spec §5.3-5.5).
    // =============================================================================================

    [Fact]
    public void TheDipShipsAsAnExactNoOp()
    {
        Assert.Equal(0.00f, SandboxCamera.CameraDipStrengthM);
        Assert.Equal(0.05f, SandboxCamera.CameraDipAttackSec);
        Assert.Equal(0.26f, SandboxCamera.CameraDipRecoverSec);
        Assert.Equal(6f, SandboxCamera.CameraDipRampPower);          // MOVE-4f's ramp

        // Whatever the envelope's clock says, a zero peak is zero depth, and a zero depth leaves the
        // focus height exactly where it was.
        for (float t = 0f; t <= 1f; t += 0.005f)
            Assert.Equal(0f, SandboxCamera.DipDepthAt(t, 0f, 0.05f, 0.26f));
        Assert.Equal(0.9339f, SandboxCamera.DippedFocusHeight(0.9339f, 0f), 1e-5f);

        // MOVE-4f: the dip is UNGATED now, so "no-op" has to be claimed over every fall speed a
        // touchdown can carry rather than only over the ones that used to qualify. At the shipped
        // strength of zero the armed peak is zero from a feather to a terminal drop.
        for (float fall = 0f; fall <= 60f; fall += 0.25f)
            Assert.Equal(0f, SandboxCamera.CameraDipStrengthM * SandboxCamera.DipIntensityFor(fall));
    }

    /// <summary>
    /// <b>Attack, no hold, recover — linear both ways</b> (spec §5.4). A spring would overshoot on
    /// the way back and lift the camera ABOVE its rest height, which reads as a bounce rather than a
    /// landing; <c>AvatarVisual</c>'s own absorb decays linearly for the same stated reason.
    /// </summary>
    [Fact]
    public void TheDipEnvelopeIsALinearAttackAndALinearRelease_AndEndsAtExactlyZero()
    {
        const float Peak = 0.12f, A = 0.05f, R = 0.26f;

        Assert.Equal(0f, SandboxCamera.DipDepthAt(0f, Peak, A, R), 1e-5f);
        Assert.Equal(Peak * 0.5f, SandboxCamera.DipDepthAt(A * 0.5f, Peak, A, R), 1e-5f);
        Assert.Equal(Peak, SandboxCamera.DipDepthAt(A, Peak, A, R), 1e-5f);
        Assert.Equal(Peak * 0.5f, SandboxCamera.DipDepthAt(A + R * 0.5f, Peak, A, R), 1e-5f);
        Assert.Equal(0f, SandboxCamera.DipDepthAt(A + R, Peak, A, R), 1e-5f);
        Assert.Equal(0f, SandboxCamera.DipDepthAt(A + R + 5f, Peak, A, R), 1e-5f);
        Assert.Equal(0f, SandboxCamera.DipDepthAt(-1f, Peak, A, R), 1e-5f);

        // It never overshoots: the depth stays inside [0, peak] for the whole envelope, so the
        // camera never rises above its rest height. That is the anti-bounce property.
        for (float t = 0f; t <= A + R + 0.2f; t += 0.001f)
        {
            float d = SandboxCamera.DipDepthAt(t, Peak, A, R);
            Assert.True(d >= -1e-6f && d <= Peak + 1e-6f, $"depth {d:F5} left [0, {Peak}] at t={t:F3}");
        }
    }

    /// <summary>
    /// <b>Re-trigger restarts at the higher depth and never sums</b> (spec §5.4, MECHANICS §4).
    /// Summing is how two landings 40 ms apart produce a dip deeper than any single landing can
    /// reach. The restart point is also chosen so the depth is CONTINUOUS across the re-trigger: a
    /// step upward would read as the bounce §5.4 rejects a spring for.
    /// </summary>
    [Fact]
    public void TheDipRestartsAtTheHigherDepth_ContinuouslyAndWithoutSumming()
    {
        const float A = 0.05f, R = 0.26f;

        // Mid-recovery at 0.20 m, showing 0.10 m, when a deeper landing arrives.
        float showing = SandboxCamera.DipDepthAt(A + R * 0.5f, 0.20f, A, R);
        Assert.Equal(0.10f, showing, 1e-5f);

        float newPeak = Math.Max(0.30f, showing);
        float restart = SandboxCamera.RestartElapsedFor(showing, newPeak, A);
        Assert.Equal(showing, SandboxCamera.DipDepthAt(restart, newPeak, A, R), 1e-5f);   // continuous
        Assert.True(newPeak <= 0.30f + 1e-6f, "the depths summed instead of taking the higher");

        // A shallower landing mid-envelope cannot make the dip shallower either: the peak is the
        // higher of the two, so the envelope carries on toward the depth already showing.
        float shallowPeak = Math.Max(0.02f, showing);
        Assert.Equal(showing, shallowPeak, 1e-5f);
        float restart2 = SandboxCamera.RestartElapsedFor(showing, shallowPeak, A);
        Assert.Equal(showing, SandboxCamera.DipDepthAt(restart2, shallowPeak, A, R), 1e-5f);

        // A fresh landing with nothing showing starts at the top of the attack ramp.
        Assert.Equal(0f, SandboxCamera.RestartElapsedFor(0f, 0.12f, A), 1e-6f);
    }

    /// <summary>
    /// <b>The dip can only ever pull the camera IN</b> (acceptance criterion 6), which is the
    /// structural half of "it cannot defeat the sphere sweep": <c>ArmLimitForPitch</c> is monotone
    /// non-decreasing in the focus height, so a dipped focus can never authorise an arm the undipped
    /// camera would not already have taken. Swept over the whole pitch range and the whole knob
    /// range, at the knob's maximum and not merely at its default.
    /// </summary>
    [Fact]
    public void TheDipCanOnlyShortenTheArm_AtEveryPitchAndEveryDip()
    {
        const float ShippedFocusM = 1.241f * 0.7526f;      // AvatarProportions: crown x focus share

        for (float pitch = SandboxCamera.PitchMin; pitch <= SandboxCamera.PitchMax; pitch += 0.01f)
        {
            float undipped = SandboxCamera.ArmLimitForPitch(pitch, ShippedFocusM);
            float previous = undipped;
            for (float dip = 0f; dip <= 0.50f; dip += 0.01f)
            {
                float limit = SandboxCamera.ArmLimitForPitch(pitch,
                    SandboxCamera.DippedFocusHeight(ShippedFocusM, dip));
                Assert.True(limit <= previous + 1e-5f,
                    $"a deeper dip LENGTHENED the arm at pitch {pitch:F2}: {previous:F4} -> {limit:F4}");
                Assert.True(limit <= undipped + 1e-5f,
                    $"the dipped arm {limit:F4} exceeded the undipped {undipped:F4} at pitch {pitch:F2}");
                previous = limit;
            }
        }
    }

    /// <summary>
    /// <b>The lens still clears the plane the character stands on at the knob's maximum</b>
    /// (acceptance criterion 6), for the bodies that can actually land: the shipped camper (crown
    /// 1.241 m) and the greybox classic (1.20 m). Computed through the public rig arithmetic —
    /// the lens sits <c>L sin(pitch)</c> below the focus — rather than observed in an engine.
    ///
    /// <para><b>The honest boundary, stated rather than discovered.</b> The analytic bound bottoms
    /// out at the rig's own <c>MinArmM</c>, so it gives positive clearance only while
    /// <c>max(crown x 0.7526 - dip, 0.25) &gt; MinArmM sin(PitchMax)</c> — a crown of about
    /// <b>1.07 m</b> at the knob's 0.50 m maximum. Below that the SpringArm3D's 0.25 m sphere cast
    /// is the remaining guard, which is exactly the role <c>ArmLimitForPitch</c>'s own doc already
    /// gives it for the steep-upslope case. No shipped player body is below it.</para>
    /// </summary>
    [Fact]
    public void TheLensClearsTheFootPlane_AtTheKnobsMaximum_ForEveryBodyThatCanLand()
    {
        // MinArmM is private; the rig hands it back for any focus below the lens clearance.
        float minArm = SandboxCamera.ArmLimitForPitch(SandboxCamera.PitchMax, 0f);

        foreach (float crown in new[] { 1.241f, 1.20f })
        {
            float focus = crown * 0.7526f;
            for (float dip = 0f; dip <= 0.50f; dip += 0.005f)
            {
                float dipped = SandboxCamera.DippedFocusHeight(focus, dip);
                for (float pitch = 0f; pitch <= SandboxCamera.PitchMax; pitch += 0.01f)
                {
                    float arm = SandboxCamera.ArmLimitForPitch(pitch, dipped);
                    float lensY = dipped - arm * Mathf.Sin(pitch);
                    Assert.True(lensY > 0f,
                        $"crown {crown:F3} at dip {dip:F3} and pitch {pitch:F2}: the lens sat "
                        + $"{lensY:F4} m relative to the foot plane");
                }
            }
        }

        // And the boundary itself, so the number in the doc comment above is measured rather than
        // asserted: the clearance at the worst case is the floor minus the shortest arm's rise.
        float worst = 1.20f * 0.7526f - 0.50f - minArm * Mathf.Sin(SandboxCamera.PitchMax);
        Assert.True(worst > 0.05f,
            $"the greybox classic's worst-case clearance fell to {worst:F4} m");
    }
    // =============================================================================================
    // 6. MOVE-4f - THE DIP HAS NO GATE (Talon's choice C, 2026-08-27).
    //
    // MOVE-4e measured the premise the gated design rested on and falsified it, then Talon chose
    // the option with no gate at all. Everything below is about the property that replaced it:
    // amplitude that ramps CONTINUOUSLY FROM ZERO with landing fall speed, so a tap's dip is
    // imperceptible by construction rather than by a threshold somebody tuned.
    // =============================================================================================

    /// <summary>Fall gravity, m/s^2 - the acceleration a landing is reached under.</summary>
    private static float FallG =>
        MotorTuning.Default.Gravity * MotorTuning.Default.FallGravityMultiplier;

    /// <summary>Landing speed after a free fall of <paramref name="heightM"/>.</summary>
    private static float LandingSpeedAfter(float heightM) => MathF.Sqrt(2f * FallG * heightM);

    /// <summary>
    /// <b>MOVE-4c's stated reason for gating the dip is arithmetically false, and this is the
    /// record of it.</b> Its UNVERIFIED list said the hop chain "is supposed to produce no dip at
    /// all, because hops land well under 2.5 m/s". MOVE-4e measured otherwise; here is the same
    /// falsification as arithmetic, so it cannot quietly come back.
    ///
    /// <para><b>Why this test exists after the gate is gone.</b> The gate's premise is the argument
    /// somebody will reach for the next time a hop chain feels bad - "just put the threshold back".
    /// A test is the cheapest way to say: that threshold never separated a hop from a drop, and it
    /// still would not.</para>
    /// </summary>
    [Fact]
    public void TheOldGatesPremiseIsFalse_EveryJumpThisMotorProducesLandsPastIt()
    {
        float gate = MotorTuning.Default.LandMinFallMps;
        Assert.Equal(2.5f, gate);

        // MOVE-8: the ruled fall gravity is 36 rather than 29.7, so every height below shrank
        // while every landing SPEED grew. The finding is unchanged and got stronger.
        // The gate is a 0.087 m fall - a step down off a kerb, not a hop.
        float gateHeight = gate * gate / (2f * FallG);
        Near(0.0868f, gateHeight, 5e-4f, "the fall height LandMinFallMps corresponds to");

        // The ruled jog-tap apex, landing at nearly twice the gate.
        float tapApex = MotorArc.JogTap(MotorTuning.Default).ApexM;
        Near(4.648f, LandingSpeedAfter(tapApex), 5e-3f, "the jog tap's landing speed");
        Assert.True(LandingSpeedAfter(tapApex) > 1.8f * gate);

        // Even the playground's spawn lift qualifies, and nobody would call that a landing.
        Assert.True(LandingSpeedAfter(0.40f) > gate);

        // And the held jump.
        Near(10.07f, LandingSpeedAfter(MotorArc.HeldSprint(MotorTuning.Default).ApexM), 0.05f,
            "the held jump's landing speed");

        // So the gate separates nothing this motor can produce: every jump is on its far side, and
        // a dip behind it would have fired on EVERY hop - the 2-4 Hz case it was invented against.
        Assert.True(SandboxCamera.JogTapLandingMps > gate,
            "the softest jump landing must be past the gate, or MOVE-4e's finding has evaporated");
    }

    /// <summary>
    /// <b>THE ABSENCE CHECK, with its positive control</b> (MOVE-4f acceptance criterion 4): no
    /// threshold, gate or step function exists anywhere in the dip's path.
    ///
    /// <para><b>An absence claim is worthless until the method can prove a presence</b>, so the
    /// first half of this test measures the largest single-sample jump the response makes across a
    /// fine sweep of fall speeds and shows it is tiny; the second half puts a real step into the
    /// same sweep - the OLD gated shape, reconstructed - and shows the same measurement catches it
    /// instantly. Same sweep, same statistic, one shape it must pass and one it must fail.</para>
    /// </summary>
    [Fact]
    public void TheDipResponseIsContinuousFromZero_AndTheSameSweepWouldCatchAStep()
    {
        const float Full = 14f, Ramp = 6f;

        // The old shape, reconstructed exactly, as the positive control: AvatarVisual's gate at
        // 2.5 m/s with its own 0.25 legibility floor above it.
        static float Gated(float fall)
            => fall >= AvatarVisual.LandMinFallMps ? AvatarVisual.LandIntensityFor(fall) : 0f;
        static float Continuous(float fall) => SandboxCamera.DipIntensityFor(fall, Full, Ramp);

        // Monotone, starting at exactly zero, inside [0, 1], saturating at the top and staying.
        float previous = Continuous(0f);
        Assert.Equal(0f, previous);
        for (float fall = 0.005f; fall <= 30f; fall += 0.005f)
        {
            float here = Continuous(fall);
            Assert.True(here >= previous - 1e-7f, $"the response fell at {fall:F3} m/s");
            Assert.InRange(here, 0f, 1f);
            previous = here;
        }
        Assert.Equal(1f, previous, 1e-6f);

        // THE DISCRIMINATOR, and it is the one that cannot be argued with: refine the sample
        // spacing and a CONTINUOUS response's largest sample-to-sample change shrinks with it,
        // while a STEP's does not shrink at all - a step's height is a property of the function,
        // not of how finely you looked. Same statistic, same sweep, two spacings.
        float coarse = WorstJump(Continuous, 0.005f);
        float fine = WorstJump(Continuous, 0.00125f);      // four times finer
        float gatedCoarse = WorstJump(Gated, 0.005f);
        float gatedFine = WorstJump(Gated, 0.00125f);

        Assert.True(gatedCoarse > 0.24f,
            $"the positive control did not step: worst jump {gatedCoarse:F5}");
        Assert.True(gatedFine > 0.99f * gatedCoarse,
            $"the step shrank under refinement ({gatedCoarse:F5} -> {gatedFine:F5}), so this "
            + "measurement is not actually detecting a step");
        Assert.True(fine < 0.35f * coarse,
            $"the dip's worst jump did not shrink with the sampling ({coarse:F7} -> {fine:F7}), "
            + "which is what a hidden step would look like");

        // And the absolute size, bounded analytically rather than merely observed: the steepest
        // the ramp ever gets is at saturation, where d/dfall = Ramp / Full per m/s.
        Assert.True(coarse <= Ramp / Full * 0.005f * 1.01f,
            $"worst jump {coarse:F7} exceeded the curve's own maximum slope over one sample");
    }

    /// <summary>The largest change between two adjacent samples of <paramref name="f"/> over the
    /// whole landing-speed range, at a given sample spacing. A continuous function's answer scales
    /// with the spacing; a step function's does not.</summary>
    private static float WorstJump(Func<float, float> f, float spacing)
    {
        float previous = f(0f), worst = 0f;
        for (float fall = spacing; fall <= 30f; fall += spacing)
        {
            float here = f(fall);
            worst = MathF.Max(worst, MathF.Abs(here - previous));
            previous = here;
        }
        return worst;
    }

    /// <summary>
    /// <b>THE PIN on knob 31</b> - <c>MotorTuningKnobs.CameraDipRampPower</c> names this method,
    /// and this is the assertion that fails first if the shipped ramp moves down.
    ///
    /// <para><b>A jog tap's dip is below perception at the STRENGTH KNOB'S MAXIMUM</b>, which is the
    /// worst case the panel can ever be dragged to and therefore the only bar worth asserting. At
    /// the shipped default strength of 0.00 the dip is exactly zero and the claim is trivial; the
    /// interesting claim is that Talon cannot make a hop visible with the strength slider alone.
    /// The floor is <c>SandboxCamera.PerceptibleDipM</c>: 0.10 px at the 56 px/m MOVE-4e measured
    /// its horizon tracker resolving, against that tracker's own 0.08 px repeatability.</para>
    /// </summary>
    [Fact]
    public void AJogTapStaysBelowThePerceptionFloor_AtTheShippedRampAndTheStrengthKnobsMaximum()
    {
        float maxStrength = MotorTuningKnobs.CameraDipStrengthM.Max;
        Assert.Equal(0.50f, maxStrength);

        float tapAtMax = maxStrength * SandboxCamera.DipIntensityFor(
            SandboxCamera.JogTapLandingMps, MotorTuning.Default.LandFullFallMps,
            MotorTuning.Default.CameraDipRampPower);

        // MOVE-8: 0.001422 -> 0.000671. The tap lands SOFTER at the ruled tuning (4.65 m/s
        // against 5.27), so it is further below the perception floor than it was — the claim this
        // test makes is unchanged and its margin doubled.
        Near(0.000671f, tapAtMax, 2e-6f, "the jog tap's dip at the strength knob's maximum");
        Assert.True(tapAtMax < SandboxCamera.PerceptibleDipM,
            $"a jog tap dips {tapAtMax:F6} m, at or above the {SandboxCamera.PerceptibleDipM:F6} m "
            + "perception floor");

        // At the SHIPPED strength it is not merely below the floor, it is exactly nothing.
        Assert.Equal(0f, MotorTuning.Default.CameraDipStrengthM
                       * SandboxCamera.DipIntensityFor(SandboxCamera.JogTapLandingMps,
                           MotorTuning.Default.LandFullFallMps,
                           MotorTuning.Default.CameraDipRampPower));

        // Why 6 and not less, stated as arithmetic rather than as taste: 5 and 4 both put the tap
        // back above the floor at the same maximum strength. This is the "smallest integer that
        // works" claim in SandboxCamera.CameraDipRampPower's doc, checked.
        foreach (float weaker in new[] { 5f, 4f, 3f, 2f, 1f })
            Assert.True(maxStrength * SandboxCamera.DipIntensityFor(
                    SandboxCamera.JogTapLandingMps, 14f, weaker) > SandboxCamera.PerceptibleDipM,
                $"ramp {weaker:F1} was expected to put a jog tap above the perception floor");
    }

    /// <summary>
    /// <b>The separation the ramp buys, in metres, at the knob's maximum</b> (MOVE-4f acceptance
    /// criterion 3's numbers). A tap is a fraction of a millimetre; a held jump is thirty-four
    /// times deeper; a real drop reaches the full knob. That spread is what a threshold was
    /// supposed to produce and could not, because 5.27 and 9.55 m/s sit on the same side of any
    /// threshold that does not also switch the knee absorb off.
    /// </summary>
    [Fact]
    public void TheRampSeparatesATapFromADrop_ByAFactorNoThresholdCouldHaveReached()
    {
        const float Max = 0.50f, Full = 14f, Ramp = 6f;

        float tap = Max * SandboxCamera.DipIntensityFor(SandboxCamera.JogTapLandingMps, Full, Ramp);
        float jump = Max * SandboxCamera.DipIntensityFor(10.07f, Full, Ramp);
        float drop = Max * SandboxCamera.DipIntensityFor(Full, Full, Ramp);

        // MOVE-8: re-measured on the ruled arc. The tap lands softer (4.65 m/s, was 5.27) and the
        // held jump harder (10.07 m/s, was 9.5), so BOTH separations widened — the finding this
        // test carries got stronger rather than weaker.
        Near(0.00067f, tap, 5e-6f, "jog tap at max strength");
        Near(0.06924f, jump, 5e-5f, "held jump at max strength");
        Near(0.50000f, drop, 1e-5f, "a 2.7 m drop at max strength");

        // MOVE-8: tap-to-jump WIDENED from 34x to 103x (the tap got softer and the held jump got
        // harder), while jump-to-drop NARROWED from 10.2x to 7.2x — the held jump now lands at
        // 10.07 m/s against a 14 m/s saturation, so it is much further up the same ramp. Both
        // separations are still far past anything a threshold could have produced, which is the
        // claim; the second bound is restated at 7x rather than left at a 10x that the ruling
        // spent.
        Assert.True(jump / tap > 30f, $"tap-to-jump separation was only {jump / tap:F1}x");
        Assert.True(drop / jump > 7f, $"jump-to-drop separation was only {drop / jump:F1}x");

        // The drop that saturates the knob is still a real one: 2.7 m, not a hop off a step.
        Near(2.722f, Full * Full / (2f * FallG), 5e-3f, "the fall height that saturates the dip");
    }

    /// <summary>
    /// <b>The curve is defined everywhere a touchdown can reach it</b>, including the arguments a
    /// gate used to make unreachable. MECHANICS 6: exercise the extremes rather than the
    /// comfortable middle, because the dip is now called on EVERY landing rather than on the ones
    /// that already passed a filter.
    /// </summary>
    [Fact]
    public void TheDipCurveIsTotal_AtEveryFallSpeedAndEveryTuningTheValidatorPermits()
    {
        foreach (float fall in new[] { 0f, 1e-6f, 0.001f, 1f, 5.27f, 14f, 100f, 1e6f })
        foreach (float full in new[] { MotorTuningKnobs.LandFullFallMps.Min, 14f,
                                       MotorTuningKnobs.LandFullFallMps.Max })
        foreach (float ramp in new[] { MotorTuningKnobs.CameraDipRampPower.Min, 6f,
                                       MotorTuningKnobs.CameraDipRampPower.Max })
        {
            float v = SandboxCamera.DipIntensityFor(fall, full, ramp);
            Assert.True(float.IsFinite(v), $"fall {fall} full {full} ramp {ramp} gave {v}");
            Assert.InRange(v, 0f, 1f);
        }

        // The values a gate used to make unreachable, and which now arrive as ordinary arguments.
        Assert.Equal(0f, SandboxCamera.DipIntensityFor(0f, 14f, 6f));
        Assert.Equal(0f, SandboxCamera.DipIntensityFor(-3f, 14f, 6f));          // never negative
        Assert.Equal(0f, SandboxCamera.DipIntensityFor(float.NaN, 14f, 6f));
        Assert.Equal(0f, SandboxCamera.DipIntensityFor(float.NegativeInfinity, 14f, 6f));
        // A non-finite fall speed is a bug somewhere upstream, and the right answer to it is no
        // dip rather than a full one: a camera that lurches to its deepest dip is a far louder
        // way to report an arithmetic fault than a camera that does nothing.
        Assert.Equal(0f, SandboxCamera.DipIntensityFor(float.PositiveInfinity, 14f, 6f));

        // A validator-defeating zero divisor cannot produce a NaN focus height.
        Assert.True(float.IsFinite(SandboxCamera.DipIntensityFor(5f, 0f, 6f)));
        Assert.True(float.IsFinite(SandboxCamera.DipIntensityFor(5f, 14f, 0f)));
    }

    /// <summary>
    /// <b>The ramp row's pin is drawn on the coupled quantity, and it moves</b> - the panel's half
    /// of the guarantee. MOVE-4d's finding was four sliders that reported a change they had not
    /// made; the counterpart failure here would be a badge that reports a window it does not
    /// actually evaluate. Checked in both directions at the shipped tuning and past it.
    /// </summary>
    [Fact]
    public void TheRampRowsPinBreachesExactlyWhenAJogTapBecomesVisible()
    {
        MotorKnob row = MotorTuningKnobs.CameraDipRampPower;

        Assert.True(row.IsPinned);
        Assert.Equal(31, row.Index);
        Assert.Equal("Camera", row.Group);
        Assert.Equal(6f, row.Default);
        Assert.Equal(MotorTuning.Default.CameraDipRampPower, row.Default);
        Assert.Equal("CameraDipStrengthM", row.LiveWhenMoved);

        // Shipped: holds, and the badge's quantity is the max-strength tap depth, not the exponent.
        Assert.False(row.Breaches(MotorTuning.Default, out float shipped, out _, out float? hi));
        Near(0.000671f, shipped, 2e-6f, "the pinned quantity at the shipped tuning");
        Assert.Equal(SandboxCamera.PerceptibleDipM, hi!.Value);

        // Drop the ramp far enough and the row goes red. MOVE-8: the breach point moved from
        // just under 5.5 to just under 5.27, because the ruled tap lands SOFTER (4.65 m/s against
        // 5.27) and therefore has further to climb before it crosses the 0.0018 m floor. The
        // shipped 6 has more margin than it had, not less, and the tightness claim is restated at
        // the value that now bounds it rather than deleted.
        Assert.True(row.Breaches(MotorTuning.Default with { CameraDipRampPower = 5.0f },
            out _, out _, out _));
        Assert.False(row.Breaches(MotorTuning.Default with { CameraDipRampPower = 5.5f },
            out _, out _, out _));

        // Raise it and it holds harder.
        Assert.False(row.Breaches(MotorTuning.Default with { CameraDipRampPower = 12f },
            out _, out _, out _));

        // COUPLED, which is the reason the window is a function of the tuning at all: halving the
        // shared full-intensity fall speed makes a jog tap visible at the same ramp.
        Assert.True(row.Breaches(MotorTuning.Default with { LandFullFallMps = 7f },
            out _, out _, out _));
    }

    /// <summary>
    /// <b>The knee absorb and its gate are untouched</b> (MOVE-4f acceptance criteria 5 and 6, and
    /// its STOP condition). Choice C was picked over raising the shared gate precisely so the body
    /// Talon has already played does not move, so this is the assertion that would fire if a later
    /// session "tidied" the two definitions back together.
    /// </summary>
    [Fact]
    public void TheKneeAbsorbAndItsGateAreExactlyWhereTalonPlayedThem()
    {
        Assert.Equal(2.5f, MotorTuning.Default.LandMinFallMps);
        Assert.Equal(2.5f, AvatarVisual.LandMinFallMps);
        Assert.Equal(14f, MotorTuning.Default.LandFullFallMps);
        Assert.Equal(0.25f, AvatarVisual.LandMinIntensity);

        // The absorb's curve, unchanged: floored at 0.25 the moment it qualifies, full at 14.
        Assert.Equal(0.25f, AvatarVisual.LandIntensityFor(2.5f));
        Assert.Equal(0.25f, AvatarVisual.LandIntensityFor(3.4f));
        Near(0.6786f, AvatarVisual.LandIntensityFor(9.5f), 1e-4f, "the absorb at a held landing");
        Assert.Equal(1f, AvatarVisual.LandIntensityFor(20f));

        // And the dip does NOT read that curve. Same landing, two different numbers, on purpose:
        // the absorb's floor exists because its gate exists, and the dip has no gate to justify one.
        Assert.True(AvatarVisual.LandIntensityFor(SandboxCamera.JogTapLandingMps)
                  > 100f * SandboxCamera.DipIntensityFor(SandboxCamera.JogTapLandingMps, 14f, 6f));
    }
}
