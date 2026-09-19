using System;
using System.Collections.Generic;
using Godot;

namespace MpFoundation.Net;

/// <summary>One evaluated invariant: what it is called, what it measures right now, what it must
/// stay inside, and whether it currently holds.</summary>
/// <param name="Name">Short label, as it appears in the printed readout.</param>
/// <param name="Value">The measured quantity at the tuning being evaluated.</param>
/// <param name="Bound">The bound, rendered — <c>"1.200 s"</c>, <c>"(1.45 - 1.75)"</c>.</param>
/// <param name="Unit">Unit for the measured value.</param>
/// <param name="Holds">False when the inequality is breached.</param>
/// <param name="BoundSpeaksForItself">True when <paramref name="Bound"/> already contains the
/// measured quantity — the gear ordering renders as <c>2.43 &lt; 5.40 &lt; 8.64</c>, and printing a
/// separate value beside it would say the same number twice.</param>
public readonly record struct MotorInvariant(string Name, float Value, string Bound, string Unit,
    bool Holds, bool BoundSpeaksForItself = false);

/// <summary>
/// <b>The coupled constraints, evaluated live against a candidate tuning</b> — MOVE-4a §3.4's
/// "live invariant readout" and §6.4's shape rule 7.
///
/// <para><b>Why this exists rather than a per-slider bound:</b> the bounds are coupled. The
/// <c>Gravity</c> / <c>JumpVelocity</c> / <c>FallGravityMultiplier</c> / <c>ApexHangStrength</c>
/// window is one four-dimensional constraint, and lowering two of them a little each breaches an
/// inequality neither breaches alone. No number beside a slider can say that.</para>
///
/// <para><b>Everything here is pure arithmetic over a <see cref="MotorTuning"/> value</b>, never
/// over <see cref="MotorTuning.Current"/>, so a panel can evaluate a working copy before applying
/// it and a printed block can record the state of the world at the moment a tuning was chosen.</para>
/// </summary>
public static class MotorTuningInvariants
{
    /// <summary>The tick the whole discrete model runs on (§2.1). <c>AvatarMotor.TickDelta</c>
    /// stays a <c>const</c> and is not a knob (§2.4).</summary>
    private const float Dt = AvatarMotor.TickDelta;

    /// <summary>The literal <c>AirborneControlTests.LongestAirtimeSec</c> uses for the air-brake
    /// bound. A hand-typed number in the test, so it is a hand-typed number here — if it were
    /// re-derived the readout would stop agreeing with the assertion it is reporting on.
    /// <b>MOVE-8: 0.710 → 0.683 s with the ruled gravity rows.</b></summary>
    private const float LongestAirtimeSec = 0.683f;

    /// <summary><c>LocomotionProfile.WalkFraction</c>. Not a knob; the walk gear is derived.</summary>
    private const float WalkFraction = 0.45f;

    // The JumpApex window, transcribed from AirborneControlTests.JumpApex_BracketsTheSpecRange_
    // HeldVersusReleasedImmediately (tests/unit/AirborneControlTests.cs:336-348). All FIVE of its
    // assertions are here, not just the two the readout prints: the ceiling below has to know what
    // the test actually asserts, and the two tap windows and the 2x ratio are three of the five.
    // MOVE-8: re-centred on Talon's ruled arc, tolerances untouched. Mirrors the same four
    // literals in AirborneControlTests.JumpApex_BracketsTheSpecRange; if the two ever disagree the
    // readout is reporting on a test that is not the one running.
    private const float ApexHeldLo = 1.55f - 0.15f, ApexHeldHi = 1.55f + 0.15f;
    private const float AirtimeHeldLo = 0.683f - 0.06f, AirtimeHeldHi = 0.683f + 0.06f;
    private const float ApexTapLo = 0.54f - 0.15f, ApexTapHi = 0.54f + 0.15f;
    private const float AirtimeTapLo = 0.283f - 0.06f, AirtimeTapHi = 0.283f + 0.06f;
    private const float CarriableRatioMin = 2.0f;

    /// <summary>The tap the test simulates: released on the first airborne tick
    /// (<c>holdForTicks: 1</c>).</summary>
    private const int TapHoldTicks = 1;

    /// <summary>The hard ceiling on <c>ApexHangStrength</c> (§2.3): at 1.0 the effective gravity at
    /// the apex is zero and the body never comes down.</summary>
    private const float ApexHangStrengthHardMax = 0.90f;

    /// <summary>The knob-table max for <c>ApexHangWindowMps</c>, used when the window is
    /// unconstrained because the hang is switched off.</summary>
    private const float ApexHangWindowHardMax = 6.00f;

    /// <summary>
    /// <b>Gravity this tick, as a pure function of a candidate tuning</b> — the exact three-way cut
    /// <see cref="AvatarMotor.GravityFor"/> runs, evaluated against <paramref name="t"/> instead of
    /// <see cref="MotorTuning.Current"/> so an unapplied tuning can be measured.
    ///
    /// <para><b>It is a mirror, and mirrors drift.</b>
    /// <c>MotorTuningTests.TheInvariantMirrorAgreesWithTheMotor_AcrossEveryBranch</c> is the
    /// positive control that says so — it sweeps both functions over the same inputs at
    /// <see cref="MotorTuning.Default"/> and fails the moment they disagree. <b>MOVE-4c, adding the
    /// apex-hang term: add it to both, or that test goes red and tells you which one you
    /// forgot.</b></para>
    ///
    /// <para><b>MOVE-4c added the apex hang here as an independent re-implementation</b>, not as a
    /// call into <c>AvatarMotor</c>, so the drift test still compares two authorships rather than
    /// one function with itself. <c>MotorTuningTests.TheInvariantMirrorsTheApexHangShape_AtEveryStrengthAndWindow</c>
    /// sweeps this against <see cref="AvatarMotor.ApexHangFactor"/> across a grid of strengths and
    /// windows — which is a drift guard at NON-default values, and it needs no test to park a
    /// non-default <see cref="MotorTuning.Current"/> to get one.</para>
    /// </summary>
    public static float GravityFor(in MotorTuning t, float velocityY, bool jumpHeld,
        bool locked = false)
    {
        float g;
        if (velocityY < 0f)
            g = t.Gravity * t.FallGravityMultiplier;
        else if (velocityY > 0f && !jumpHeld && !locked)
            g = t.Gravity * t.JumpReleaseGravityMultiplier;
        else
            g = t.Gravity;

        if (locked)
            return g;

        float s = t.ApexHangStrength;
        if (s > 0f && t.ApexHangWindowMps > 0f)
        {
            float u = Math.Min(1f, Math.Abs(velocityY) / t.ApexHangWindowMps);
            float w = 1f - (u * u * (3f - 2f * u));
            g *= 1f - s * w;
        }

        // MOVE-5f's baked coil, mirrored here for the reason the remark above gives — and the
        // mirror is load-bearing rather than tidy: SimulateJump below runs THIS function, so
        // JumpApexAssertionsHold and both LaunchCoil ceilings would report on a jump the motor is
        // not simulating if the term lived only in AvatarMotor. Re-implemented rather than
        // delegated, so MotorTuningTests still compares two authorships.
        if (Mathf.RoundToInt(t.AnticipationMode) == 2 && t.AnticipationBakedStrength > 0f
            && t.AnticipationBakedWindow > 0f && t.JumpVelocity > 0f && velocityY > 0f)
        {
            float span = t.JumpVelocity * t.AnticipationBakedWindow;
            float u = Mathf.Clamp((t.JumpVelocity - velocityY) / span, 0f, 1f);
            float w = 1f - (u * u * (3f - 2f * u));
            g *= 1f + t.AnticipationBakedStrength * w;
        }
        return g;
    }

    /// <summary>
    /// One jump, tick by tick — <b>the same discrete model
    /// <c>AirborneControlTests.SimulateJump</c> (<c>:531</c>) runs</b>, including the launch tick
    /// whose free <c>JumpVelocity × dt</c> is worth 0.14 m. Evaluated against a candidate tuning so
    /// the readout reports the numbers the test would measure after a paste.
    /// </summary>
    /// <param name="holdForTicks">How many airborne ticks the jump key stays down.</param>
    public static (float ApexM, float AirtimeSec) SimulateJump(in MotorTuning t, int holdForTicks)
    {
        float vy = t.JumpVelocity;
        float y = vy * Dt;
        float apex = y;
        int ticks = 1;

        while (y > 0f && ticks < 600)
        {
            bool held = ticks <= holdForTicks;
            vy -= GravityFor(t, vy, held) * Dt;
            y += vy * Dt;
            if (y > apex)
                apex = y;
            ticks++;
        }
        return (apex, ticks * Dt);
    }

    /// <summary>The most the air brake can shed over the longest flight — the quantity
    /// <c>AirborneControlTests.cs:217</c> asserts at <c>4.47 ± 0.02</c> and <c>:218</c> requires to
    /// stay above the skid entry speed.</summary>
    public static float MaxAirBrakeShedMps(in MotorTuning t)
        => t.Deceleration * t.AirControlBrake * LongestAirtimeSec;

    /// <summary>
    /// <b>Does <c>AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately</c>
    /// pass at this tuning?</b> All five of its assertions, evaluated on the same discrete model the
    /// test itself runs.
    /// </summary>
    public static bool JumpApexAssertionsHold(in MotorTuning t)
    {
        (float apexHeld, float airtimeHeld) = SimulateJump(t, int.MaxValue);
        (float apexTap, float airtimeTap) = SimulateJump(t, TapHoldTicks);
        if (!float.IsFinite(apexHeld) || !float.IsFinite(apexTap) || !(apexTap > 0f))
            return false;
        return apexHeld >= ApexHeldLo && apexHeld <= ApexHeldHi
            && airtimeHeld >= AirtimeHeldLo && airtimeHeld <= AirtimeHeldHi
            && apexTap >= ApexTapLo && apexTap <= ApexTapHi
            && airtimeTap >= AirtimeTapLo && airtimeTap <= AirtimeTapHi
            && apexHeld / apexTap >= CarriableRatioMin;
    }

    /// <summary>
    /// <b>The test-asserted ceiling on <c>ApexHangStrength</c></b> (§3.7), live in
    /// <c>ApexHangWindowMps</c>, <c>Gravity</c>, <c>FallGravityMultiplier</c> and
    /// <c>JumpVelocity</c> — which is why it is a function and not a number.
    ///
    /// <para><b>MOVE-4c replaced §3.7's closed form with a bisection on the discrete simulation,
    /// and the correction is not cosmetic.</b> The closed form
    /// <c>Δt = W·(1/G_rise + 1/G_fall)·(1/(1 − S/2) − 1)</c> is a continuous-math estimate of a
    /// quantity the test measures in whole 60 Hz ticks, and it OVERSTATES the ceiling by about 7%:
    /// at the shipped tuning with a 2.00 m/s window it says <b>0.376</b> where the simulation the
    /// test actually runs breaks at <b>0.351</b>. Every value in <c>(0.350, 0.376]</c> would have
    /// been reported as "inside its ceiling" by the panel and by the printed block while in fact
    /// reddening <c>JumpApex_BracketsTheSpecRange</c> on paste — which is precisely the failure the
    /// ceiling exists to prevent. §4.4's recommended first experiment of 0.35 does not have "about
    /// 7% of margin"; it has <b>0.2%</b>, one thousandth below the true ceiling, and one slider step
    /// past it is red.</para>
    ///
    /// <para><b>Why a bisection is sound here:</b> every one of the five assertions is monotone in
    /// the strength — more hang means strictly more airtime and strictly more apex, on both the held
    /// and the tapped arc, and the held apex grows faster than the tapped one so the 2x carriable
    /// ratio only improves. The passing set is therefore an interval <c>[0, S*]</c> and bisection
    /// finds <c>S*</c> exactly, tick quantisation included. Cost is ~40 doublings of a ~50-tick
    /// integration; it runs on a panel redraw and on a print, never per frame.</para>
    /// </summary>
    public static float ApexHangStrengthCeiling(MotorTuning t)
    {
        MotorTuning off = t with { ApexHangStrength = 0f };
        if (!JumpApexAssertionsHold(off))
            return 0f;                                  // already breached without any hang at all
        if (!(t.ApexHangWindowMps > 0f) || !float.IsFinite(t.ApexHangWindowMps))
            return ApexHangStrengthHardMax;             // an inert window cannot constrain anything
        if (JumpApexAssertionsHold(t with { ApexHangStrength = ApexHangStrengthHardMax }))
            return ApexHangStrengthHardMax;

        return Bisect(0f, ApexHangStrengthHardMax,
            s => JumpApexAssertionsHold(t with { ApexHangStrength = s }));
    }

    /// <summary>The same §3.7 constraint read the other way: the ceiling on
    /// <c>ApexHangWindowMps</c> given the strength. Unbounded while the hang is switched off, which
    /// is the shipped state. Bisected on the same simulation and for the same reason as
    /// <see cref="ApexHangStrengthCeiling"/>.</summary>
    public static float ApexHangWindowCeiling(MotorTuning t)
    {
        if (!(t.ApexHangStrength > 0f))
            return ApexHangWindowHardMax;               // inert: the window has no effect at all
        if (!JumpApexAssertionsHold(t with { ApexHangStrength = 0f }))
            return 0f;
        if (JumpApexAssertionsHold(t with { ApexHangWindowMps = ApexHangWindowHardMax }))
            return ApexHangWindowHardMax;

        return Bisect(0f, ApexHangWindowHardMax,
            w => JumpApexAssertionsHold(t with { ApexHangWindowMps = w }));
    }

    /// <summary>The knob-table max for <c>AnticipationBakedStrength</c> (MOVE-5f row 56).</summary>
    private const float LaunchCoilStrengthHardMax = 2.00f;

    /// <summary>The knob-table max for <c>AnticipationBakedWindow</c> (row 57).</summary>
    private const float LaunchCoilWindowHardMax = 1.00f;

    /// <summary>
    /// <b>The test-asserted ceiling on <c>AnticipationBakedStrength</c></b> — the same shape and the
    /// same bisection as <see cref="ApexHangStrengthCeiling"/>, and it exists for the same reason:
    /// the constraint is live in <c>AnticipationBakedWindow</c>, <c>JumpVelocity</c>,
    /// <c>Gravity</c> and <c>FallGravityMultiplier</c>, so no number printed beside the slider could
    /// say it.
    ///
    /// <para><b>The inequality runs the other way from the hang's, and that is the whole difference.</b>
    /// The apex hang ADDS airtime and apex, so it is bounded by
    /// <c>JumpApex_BracketsTheSpecRange</c>'s ceilings; the baked coil TAKES them, so it is bounded
    /// by the same test's FLOORS (held apex 1.45 m, held airtime 0.650 s, and the 2x carriable
    /// ratio). Bisection is sound either way, because every one of the five assertions is monotone
    /// in the strength — more baked gravity is strictly less height on both the held and the tapped
    /// arc — so the passing set is again an interval <c>[0, S*]</c>.</para>
    ///
    /// <para><b>Unbounded while the mode is not 2</b>, which is the shipped state: a term that does
    /// not run cannot constrain anything, exactly as <see cref="ApexHangWindowCeiling"/> is
    /// unbounded while the hang is off.</para>
    /// </summary>
    public static float LaunchCoilStrengthCeiling(MotorTuning t)
    {
        if (Mathf.RoundToInt(t.AnticipationMode) != 2)
            return LaunchCoilStrengthHardMax;           // inert: the term never runs
        MotorTuning off = t with { AnticipationBakedStrength = 0f };
        if (!JumpApexAssertionsHold(off))
            return 0f;                                  // already breached without any coil at all
        if (!(t.AnticipationBakedWindow > 0f) || !float.IsFinite(t.AnticipationBakedWindow))
            return LaunchCoilStrengthHardMax;           // an inert window cannot constrain anything
        if (JumpApexAssertionsHold(t with { AnticipationBakedStrength = LaunchCoilStrengthHardMax }))
            return LaunchCoilStrengthHardMax;

        return Bisect(0f, LaunchCoilStrengthHardMax,
            s => JumpApexAssertionsHold(t with { AnticipationBakedStrength = s }));
    }

    /// <summary>The same constraint read the other way: the ceiling on
    /// <c>AnticipationBakedWindow</c> given the strength. A wider window spends the extra gravity
    /// over more of the rise, so it too is monotone in the direction that costs height. Unbounded
    /// while the mode is not 2 or the strength is zero.</summary>
    public static float LaunchCoilWindowCeiling(MotorTuning t)
    {
        if (Mathf.RoundToInt(t.AnticipationMode) != 2 || !(t.AnticipationBakedStrength > 0f))
            return LaunchCoilWindowHardMax;
        if (!JumpApexAssertionsHold(t with { AnticipationBakedStrength = 0f }))
            return 0f;
        if (JumpApexAssertionsHold(t with { AnticipationBakedWindow = LaunchCoilWindowHardMax }))
            return LaunchCoilWindowHardMax;

        return Bisect(0f, LaunchCoilWindowHardMax,
            w => JumpApexAssertionsHold(t with { AnticipationBakedWindow = w }));
    }

    /// <summary>
    /// <b>The composed ceiling: the fastest a body can be moving horizontally with EVERYTHING
    /// switched on</b> — chain at its cap, plus a Kick taken at the hardest fall the shipped arc
    /// produces. MOVE-5 §7.2 asks for this as an invariant readout row <i>rather than a fourth
    /// knob</i>, "so it cannot drift out of step": it is a consequence of five sliders
    /// (<c>MoveSpeed</c>, <c>SprintMultiplier</c>, <c>ChainMaxDepth</c>, <c>ChainBonusMps</c> and
    /// the three Kick rows), and no number printed beside any one of them could say what they
    /// compose to.
    ///
    /// <para><b>The fall speed is derived, not typed.</b> A held jump's touchdown speed is
    /// <c>sqrt(2 x g_fall x apex)</c> off this tuning's own simulated apex — 9.55 m/s at the shipped
    /// numbers, which is where §7.2's <c>1.20 + 0.35 x 9.55 = 4.54</c> comes from. Typing 9.55 here
    /// would have stranded the row the first time Talon moved gravity.</para>
    ///
    /// <para>At the shipped defaults it reads <b>8.64 m/s</b>, because <c>ChainBonusMps</c> is 0.00
    /// and <c>AirJumpMode</c> is 0: the row is live from the moment either leaves its no-op, which
    /// is exactly when Talon needs to see it. §13's open question O4 is about this number, so the
    /// row exists to put it in front of him rather than to fail him.</para>
    /// </summary>
    public static float ComposedCeilingMps(in MotorTuning t)
    {
        float sprintWish = t.MoveSpeed * t.SprintMultiplier + t.ChainMaxDepth * t.ChainBonusMps;
        if (t.AirJumpMode < 1.5f)
            return sprintWish;                          // no Kick: the chain's own top is the top

        (float apex, _) = SimulateJump(t, int.MaxValue);
        float gFall = t.Gravity * t.FallGravityMultiplier;
        float fall = apex > 0f && gFall > 0f ? Mathf.Sqrt(2f * gFall * apex) : 0f;
        return sprintWish + t.KickHorizontalGainMps + fall * t.KickConversionFraction;
    }

    /// <summary>The largest value in <c>[lo, hi]</c> for which <paramref name="holds"/> is true,
    /// given that it is true at <paramref name="lo"/> and false at <paramref name="hi"/>. Forty
    /// halvings take a 6.0-wide bracket to under 1e-17, so the answer is exact to float.</summary>
    private static float Bisect(float lo, float hi, Func<float, bool> holds)
    {
        for (int i = 0; i < 40; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (mid <= lo || mid >= hi)
                break;
            if (holds(mid))
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// <b>Every coupled invariant, evaluated</b> — the readout §6.4 rule 7 requires in the printed
    /// block, breached or not, so a paste records what the state of the world was when the tuning
    /// was chosen.
    /// </summary>
    public static IReadOnlyList<MotorInvariant> Evaluate(in MotorTuning t)
    {
        float shed = MaxAirBrakeShedMps(t);
        float entry = t.SkidEnterSpeedMps;
        (float apexHeld, float airtimeHeld) = SimulateJump(t, int.MaxValue);
        float walk = t.MoveSpeed * WalkFraction;
        float sprint = t.MoveSpeed * t.SprintMultiplier;

        float slideEntry = t.SlideEnterSpeedMps;
        float ceiling = ComposedCeilingMps(t);

        return new[]
        {
            new MotorInvariant("air-brake vs skid entry", shed,
                $"{entry:0.000} m/s", "m/s", shed > entry),
            new MotorInvariant("JumpApex window, apex", apexHeld,
                $"({ApexHeldLo:0.00} - {ApexHeldHi:0.00})", "m",
                apexHeld >= ApexHeldLo && apexHeld <= ApexHeldHi),
            new MotorInvariant("JumpApex window, airtime", airtimeHeld,
                $"({AirtimeHeldLo:0.000} - {AirtimeHeldHi:0.000})", "s",
                airtimeHeld >= AirtimeHeldLo && airtimeHeld <= AirtimeHeldHi),
            new MotorInvariant("gear ordering", t.MoveSpeed,
                $"{walk:0.00} < {t.MoveSpeed:0.00} < {sprint:0.00}", "m/s",
                walk < t.MoveSpeed && t.MoveSpeed < sprint, BoundSpeaksForItself: true),

            // MOVE-5 §11.3: "MotorTuning.Validate must clamp these, and MotorTuningInvariants must
            // report the coupled ones." Both of the slide's coupled pairs, reported here and
            // clamped there — the readout is what tells Talon a slider MOVED something else,
            // which a silent clamp cannot.
            new MotorInvariant("slide exit vs slide entry", t.SlideExitSpeedMps,
                $"< {slideEntry:0.000} m/s", "m/s", t.SlideExitSpeedMps < slideEntry),
            new MotorInvariant("slide min vs max", t.SlideMinSec,
                $"< {t.SlideMaxSec:0.000} s", "s", t.SlideMinSec < t.SlideMaxSec),

            // MOVE-5 §7.2's composed ceiling, and §13's open question O4 made visible. It goes red
            // only on the one thing §11.3 calls hard rather than taste — a Kick returning more
            // speed than the fall carried, which is energy from nothing.
            new MotorInvariant("composed ceiling (chain cap + Kick)", ceiling,
                $"{(sprint > 0f ? ceiling / sprint : 0f):0.00}x sprint", "m/s",
                float.IsFinite(ceiling) && t.KickConversionFraction <= 1.0f),
        };
    }
}
