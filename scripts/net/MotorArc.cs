using Godot;

namespace MpFoundation.Net;

/// <summary>One jump's shape: how high, how long, and how far the body travels while it is in the
/// air. Metres and seconds.</summary>
/// <param name="ApexM">Height of the highest point above the takeoff plane.</param>
/// <param name="AirtimeSec">Takeoff to touchdown, on flat ground.</param>
/// <param name="RangeM">Horizontal distance covered over <paramref name="AirtimeSec"/> at the
/// gear the jump was taken in — a sprint for the held jump, a jog for the tap.</param>
public readonly record struct JumpArc(float ApexM, float AirtimeSec, float RangeM);

/// <summary>
/// <b>The jump arc, DERIVED from a tuning instead of remembered from a playtest</b> — MOVE-8
/// scope item 4.
///
/// <para><b>The problem this exists to end.</b> Four numbers — 1.534 / 6.192 / 0.467 / 1.710 —
/// were measured in-engine at MOVE-3e and then typed, as literals, into
/// <c>BubbleTestLayout</c>, <c>CalibrationCourse</c>, <c>FlowCourse</c>, <c>bt3_blue_verify.gd</c>,
/// <c>bubbletest_bake_green.gd</c>, two test files and half a dozen doc comments. Every one of
/// those copies is a claim about <c>MotorTuning.Default</c> that nothing checked, and MOVE-8 is
/// the packet that had to move the tuning: at that moment all of them became wrong at once, in
/// silence, and a level whose gaps were sized against them became a level sized against a motor
/// that no longer exists. <b>Deriving the arc is what makes the level follow the body.</b></para>
///
/// <para><b>Why a simulation and not a closed form.</b> The packet asks for "closed-form from
/// JumpVelocity / Gravity / FallGravityMultiplier / MoveSpeed × SprintMultiplier". The continuous
/// closed form <c>v²/2g</c> gives <b>1.604 m</b> where the shipped, measured, approved apex is
/// <b>1.534 m</b> — a 4.5% error, because the body is integrated in whole 60 Hz Euler ticks and
/// because <see cref="MotorTuning.ApexHangStrength"/> makes gravity a function of <c>v_y</c>
/// rather than a constant. No closed form can carry the hang term at all. So the arc is taken
/// from <see cref="MotorTuningInvariants.SimulateJump"/> — the same tick-by-tick model
/// <c>AirborneControlTests.SimulateJump</c> runs, over the same shipped
/// <see cref="AvatarMotor.GravityFor"/> cut — and it reproduces <b>all four</b> of MOVE-3e's
/// measured literals to the digit at the pre-MOVE-8 tuning. <c>MotorArcTests</c> asserts exactly
/// that, which is the positive control: a derivation that could not reproduce the measurement it
/// replaces would be a guess with a formula in front of it.</para>
///
/// <para><b>The reporting convention is MOVE-3d's ("as played"), not the unit suite's.</b>
/// <c>MovementPlayground</c> latches the takeoff position on the first tick <c>Grounded</c> goes
/// false, which is AFTER the launch tick has already advanced the body by
/// <c>JumpVelocity × dt</c> = 0.14 m, and it starts its airtime clock on that same tick. So its
/// apex is one launch-step lower and its airtime one tick shorter than the simulation's. Both
/// offsets are exact rather than fitted, and this class subtracts them, because these numbers'
/// job is to be compared against level geometry that was authored from the playground's readout.
/// <c>ApexHangAndLandingDipTests.AsPlayed</c> is the same two subtractions.</para>
///
/// <para><b>Flat ground.</b> <see cref="JumpArc.RangeM"/> is speed × airtime over a level plane.
/// The playground's capture run reports 7.200 m for the held sprint rather than this class's
/// 6.192 m at the pre-MOVE-8 tuning, and neither is wrong: that run launches from the Ridge Run
/// flow course and lands lower than it left, so it is a different jump. Level constants want the
/// flat number, so the flat number is what is derived here.</para>
/// </summary>
public static class MotorArc
{
    /// <summary>The tick the discrete model runs on. <c>AvatarMotor.TickDelta</c> is a
    /// <c>const</c> and not a knob.</summary>
    private const float Dt = AvatarMotor.TickDelta;

    /// <summary>The tap the playground's capture run takes: <c>JumpHeld</c> is released on the
    /// tick after the press, and the press tick IS the launch tick, whose gravity branch is
    /// skipped entirely — so the tap is never held for a single AIRBORNE tick. This is
    /// deliberately NOT the unit suite's <c>holdForTicks: 1</c>; both are right, they are two
    /// conventions for the same press.</summary>
    private const int TapHoldTicks = 0;

    /// <summary>MOVE-3d's reporting convention: the launch step out of the apex, one tick out of
    /// the airtime. See the remark on the class.</summary>
    private static (float ApexM, float AirtimeSec) AsPlayed(in MotorTuning t, int holdForTicks)
    {
        (float apex, float airtime) = MotorTuningInvariants.SimulateJump(t, holdForTicks);
        return (apex - t.JumpVelocity * Dt, airtime - Dt);
    }

    /// <summary>Top ground gear: <see cref="MotorTuning.MoveSpeed"/> × the sprint multiplier.
    /// The chain is deliberately excluded — it ships at its exact no-op and a level constant that
    /// silently grew with a chain depth would be unusable.</summary>
    public static float SprintSpeedMps(in MotorTuning t) => t.MoveSpeed * t.SprintMultiplier;

    /// <summary><b>A fully-held jump taken at a sprint.</b> The tallest and longest single jump the
    /// body has, and — until MOVE-8 shipped the double jump — the reachability ceiling every
    /// authored gap and ledge in the repo was sized against.</summary>
    public static JumpArc HeldSprint(in MotorTuning t)
    {
        (float apex, float airtime) = AsPlayed(t, int.MaxValue);
        return new JumpArc(apex, airtime, SprintSpeedMps(t) * airtime);
    }

    /// <summary><b>A fully-held jump taken at a jog</b> — LD-1. The same rise and the same airtime as
    /// <see cref="HeldSprint"/>, because neither depends on ground speed, over
    /// <see cref="MotorTuning.MoveSpeed"/> instead of the sprint gear. It is the envelope the
    /// horizontal Jog band is a fraction of: a gap a player clears without pressing sprint, which is
    /// what "free" means on a metric card. A derivation, not a new tuning row — every number in it
    /// was already here.</summary>
    public static JumpArc HeldJog(in MotorTuning t)
    {
        (float apex, float airtime) = AsPlayed(t, int.MaxValue);
        return new JumpArc(apex, airtime, t.MoveSpeed * airtime);
    }

    /// <summary><b>A tapped jump taken at a jog.</b> The floor of the carriable range: the smallest
    /// hop the body can make on purpose, and the one stepping stones and low kerbs are sized
    /// against so a crossing reads as walkable-with-care rather than as a jump sequence.</summary>
    public static JumpArc JogTap(in MotorTuning t)
    {
        (float apex, float airtime) = AsPlayed(t, TapHoldTicks);
        return new JumpArc(apex, airtime, t.MoveSpeed * airtime);
    }

    /// <summary>
    /// <b>A held sprint jump with the air jump spent at the apex</b> — the real reachability
    /// ceiling from MOVE-8 onward, because <see cref="MotorTuning.AirJumpMode"/> now ships at 1.
    ///
    /// <para><b>At the apex, and not earlier, because the apex is where it is worth most.</b>
    /// <c>AvatarMotor.StepAirJump</c> mode 1 ASSIGNS <c>velocity.Y = JumpVelocity ×
    /// AirJumpVelocityFraction</c> rather than adding to it, so a press taken while still rising
    /// throws away the rise it overwrites. The apex is the first instant at which nothing is
    /// discarded, which makes this the envelope rather than a typical jump — exactly the reading a
    /// "can this ledge be reached" question wants.</para>
    ///
    /// <para><b>It returns nothing at <c>AirJumpMode</c> 0 or 2.</b> Mode 0 has no air jump and
    /// mode 2 is the Kick, which converts fall speed into reach on a body that is already falling
    /// and is not an altitude gain at all — modelling either here would report a ceiling the motor
    /// does not have. At those modes this is <see cref="HeldSprint"/>, unchanged.</para>
    /// </summary>
    public static JumpArc DoubleJumpAtApex(in MotorTuning t)
    {
        if (Mathf.RoundToInt(t.AirJumpMode) != 1 || Mathf.RoundToInt(t.AirJumpCountMax) < 1)
            return HeldSprint(t);

        float vy = t.JumpVelocity;
        float y = vy * Dt;
        float apex = y;
        int ticks = 1;
        bool spent = false;

        while (y > 0f && ticks < 1200)
        {
            // The press lands on the first tick the body is no longer rising. One air jump, because
            // AirJumpCountMax ships at 1; a second would need the counter this loop deliberately
            // does not model, and no shipped tuning grants one.
            //
            // THE AIR JUMP GETS ITS OWN FREE LAUNCH TICK, exactly as the ground jump does, and
            // leaving it out is a 0.112 m error that an in-engine capture catches instantly (MOVE-8
            // measured 2.402 m against a model that said 2.298 m before this branch existed).
            // AvatarMotor.Step assigns velocity.Y and then hands it to MoveAndSlide, so the body
            // travels the whole new velocity for one tick before any gravity is subtracted from it
            // — which is the same asymmetry AsPlayed's `apex - JumpVelocity * Dt` exists to undo at
            // the other end.
            if (!spent && vy <= 0f)
            {
                vy = t.JumpVelocity * t.AirJumpVelocityFraction;
                spent = true;
                y += vy * Dt;
                if (y > apex)
                    apex = y;
                ticks++;
                continue;
            }
            vy -= MotorTuningInvariants.GravityFor(t, vy, jumpHeld: true) * Dt;
            y += vy * Dt;
            if (y > apex)
                apex = y;
            ticks++;
        }

        float apexM = apex - t.JumpVelocity * Dt;
        float airtime = ticks * Dt - Dt;
        return new JumpArc(apexM, airtime, SprintSpeedMps(t) * airtime);
    }
}
