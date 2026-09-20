using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>Which side of the pedal ⇄ coast pair the rider is on (BIKE-3B §S1).</summary>
public enum RideMode
{
    /// <summary>Momentum is doing the work: the cranks are level at 3-and-9, the knees are soft
    /// and the torso is more upright. The freewheel read — see <see cref="RidePose"/>'s note on
    /// fork F-A3.</summary>
    Coast = 0,

    /// <summary>The player is driving: the knees turn the crank circle at
    /// <see cref="RidePose.CrankHzAt"/> and the torso is pitched down over the bars.</summary>
    Pedal = 1,
}

/// <summary>
/// <b>BIKE-4A — the ride channel's numbers.</b> The rider's body while the bike is under it,
/// expressed as pose targets over the existing rig, exactly as <see cref="VerbPose"/> and
/// <see cref="AnticipationCoil"/> express theirs. <see cref="AvatarVisual"/> keeps the rig half —
/// which node, which blend, which solver — and this keeps the arithmetic, so every figure below is
/// an assertion in <c>dotnet test</c> rather than a caption under a screenshot.
///
/// <para><b>The design source is <c>docs/design/2026-09-02-bike-rider-animation-states.md</c>
/// (BIKE-3B) §S1 and its airborne neighbour</b>, and the cadence rule is BIKE-3C's row 3, lifted
/// from Wobble's <c>BikePedalAnimator</c>. Nothing here is redesigned; the two places this file
/// makes a call of its own are marked VALUE and stated in BIKE-4A's report.</para>
///
/// <para><b>Presentation only, and it decides nothing.</b> Nothing in this class is read back by
/// anything that resolves a mount, a speed or a collision — it is handed a speed the body has
/// already travelled at and a mode the input has already chosen, and it answers with angles. There
/// is no input it can gate and no state change it can hold open. It is also, deliberately, the
/// whole of BIKE-4A's arithmetic: this packet adds <b>no</b> write to avatar velocity or position
/// anywhere, and a class that only returns a struct cannot acquire one by accident.</para>
///
/// <para><b>What is NOT here, and why.</b> The no-hands flourish (3B's S1 arms row) is gated on
/// affect fork F-A2 and is not built. The drift pose (S3), the mount/dismount choreographies
/// (T1–T6) and the stumble pose (T5) are out of BIKE-4A's scope — they either depend on Talon's
/// open jump ruling or belong to a later packet. Fork <b>F-A3</b> (freewheel vs fixed-gear) is
/// still open; 3B's S1 table as written is the freewheel, so <see cref="RideMode.Coast"/> parks the
/// cranks level. If Talon picks fixed-gear it is one rule swap — <see cref="AdvancePhase"/>'s
/// <c>cranksTurn</c> argument becomes constant true — and nothing else in this file moves.</para>
/// </summary>
public static class RidePose
{
    // --- THE CADENCE RULE (BIKE-3C row 3) --------------------------------------------------------

    /// <summary>The greybox bike's wheel radius, metres. <b>0.34 — <c>BikeGreybox.WheelRadiusM</c>
    /// itself</b>, duplicated as a constant rather than referenced because this file lives in
    /// <c>scripts/game/</c> and the greybox lives in <c>scripts/dev/</c>: the shipped body may not
    /// take a dependency on a lab prop, and <c>BikeGreybox.WheelRadiusM</c> is private besides.
    /// <b>The duplication is a real one and it is stated rather than hidden</b> — if the greybox's
    /// wheel changes size, this constant has to change with it or the cranks stop agreeing with the
    /// wheels. <c>RidePoseTests</c> pins the value and this sentence names the file that would have
    /// to move with it; there is no mechanism that would catch it automatically.</summary>
    public const float WheelRadiusM = 0.34f;

    /// <summary>How many wheel revolutions one crank revolution buys. <b>2.75</b> — Wobble's
    /// <c>BikePedalAnimator._wheelRevsPerCrankRev</c>, named by BIKE-3C row 13 as one of the
    /// dimensionless constants that genuinely transfers between the two games.</summary>
    public const float WheelRevsPerCrankRev = 2.75f;

    /// <summary>Metres of ground per crank revolution: <c>2π · r · gearRatio</c> = <b>5.875 m</b>.
    /// Derived rather than typed so moving either constant above moves the cadence with it.
    ///
    /// <para><b>Why this and not 3B's provisional 3.4 m.</b> 3B proposed <c>CrankMetersPerRev ≈
    /// 3.4</c> and flagged it provisional; BIKE-4A's acceptance criterion 2 names 3C's rule
    /// instead — <c>speed / (wheelRadius × gearRatio)</c> at ratio 2.75 — and 3.4 m implies a
    /// 0.197 m wheel, which is not the wheel the greybox has. Taking the real radius gives
    /// <b>1.60 rev/s at the 9.42 m/s ride cap</b> (96 rpm — a brisk but human cadence) against
    /// 3B's 2.8 rev/s (168 rpm, which is a track sprint). The rule wins over the worked example,
    /// as the packet directs.</para></summary>
    public static readonly float CrankMetersPerRev = Mathf.Tau * WheelRadiusM * WheelRevsPerCrankRev;

    /// <summary>
    /// <b>Crank revolutions per second at a given horizontal ground speed.</b> The whole cadence
    /// rule, and the one function the leg's rate reduces to: <c>speed / CrankMetersPerRev</c>, which
    /// is BIKE-3C's <c>speed / (wheelRadius × gearRatio)</c> converted from radians to revolutions.
    ///
    /// <para><b>Zero at rest, and linear everywhere</b> — "the pedal spin IS the momentum readout"
    /// (3C row 3). A body that is not moving has cranks that are not turning, with no threshold, no
    /// curve and no floor in the way of it.</para>
    /// </summary>
    public static float CrankHzAt(float flatSpeedMps)
    {
        if (!float.IsFinite(flatSpeedMps))
            return 0f;
        return Mathf.Abs(flatSpeedMps) / CrankMetersPerRev;
    }

    /// <summary><b>The coast threshold, VALUE fork, taken here.</b> Below this ground speed the
    /// cranks hold still even in <see cref="RideMode.Pedal"/> — 3C row 3 asks for "a coast threshold
    /// below which legs hold still and the wheels alone roll" and hands the number to 3B; 3B states
    /// the mode <i>trigger</i> (forward input, at-or-below wish) and a 0.3 s hysteresis but never a
    /// speed, so the speed is picked here.
    ///
    /// <para><b>0.22 m/s — <see cref="LocomotionProfile.IdleExitMps"/>, deliberately rather than a
    /// fresh literal</b>: it is the speed at which the on-foot body itself stops taking steps, so a
    /// mounted body and an unmounted one go still at exactly the same ground speed and a
    /// dismount at a crawl cannot produce a leg that stops twice. At 0.22 m/s the rule above asks
    /// for 0.037 rev/s — one crank revolution every 27 seconds, which is a stalled pedal a frame at
    /// a time rather than a slow one.</para></summary>
    public const float CrankStallMps = LocomotionProfile.IdleExitMps;

    // --- PEDAL ⇄ COAST (BIKE-3B §S1) -------------------------------------------------------------

    /// <summary>How far over the current wish speed the body may be before the mode wants COAST,
    /// m/s. <b>0.35, VALUE</b> — a shade under 4% of the 9.42 m/s ride cap, so genuine slope carry
    /// (3B names <c>SlopeBonusMps &gt; 0.5</c> as the case) reads as coasting while the ordinary
    /// millimetre of overshoot the motor leaves after an acceleration does not.</summary>
    public const float CoastOverWishMps = 0.35f;

    /// <summary>How much forward stick counts as "forward input held". <b>0.2</b> — the same order
    /// as the analogue deadzones elsewhere, and well under the 1.0 a keyboard always produces, so
    /// this is a real threshold on a stick and a no-op on a keyboard.</summary>
    public const float ForwardInputDeadzone = 0.2f;

    /// <summary><b>Hysteresis: how long a mode must be wanted before it is taken, seconds.</b> 0.3
    /// each way — 3B §S1, provisional there, taken as-is here. It is the gear-flicker cure
    /// (MECHANICS-BIBLE §2, and the same cure <see cref="LocomotionProfile"/>'s gear bands use): a
    /// rolling-resistance flicker either side of the wish must never machine-gun the legs between
    /// turning and level.</summary>
    public const float ModeHoldSec = 0.3f;

    /// <summary>
    /// <b>Which mode the inputs are asking for, before hysteresis.</b> 3B's trigger, term for term:
    /// PEDAL while the player is driving — forward input held <i>and</i> the body at or below its
    /// current wish; COAST when momentum is doing the work — no forward input, <b>or</b> speed above
    /// the wish.
    /// </summary>
    /// <param name="forwardInput">How much forward stick, 0..1. A keyboard gives 1 or 0.</param>
    /// <param name="flatSpeedMps">Horizontal ground speed.</param>
    /// <param name="wishMps">The speed the motor is currently being asked for — the ride wish,
    /// slope bonus included. A non-positive wish is treated as "no wish stated", which can only
    /// mean COAST.</param>
    public static RideMode Wants(float forwardInput, float flatSpeedMps, float wishMps)
    {
        if (!(forwardInput > ForwardInputDeadzone) || !(wishMps > 0f))
            return RideMode.Coast;
        return flatSpeedMps <= wishMps + CoastOverWishMps ? RideMode.Pedal : RideMode.Coast;
    }

    /// <summary>
    /// <b>One tick of the mode, with the hysteresis.</b> Pure: it is handed the mode it is in, how
    /// long the other one has been wanted, and what <see cref="Wants"/> says now, and it answers
    /// with both. Nothing is stored here.
    ///
    /// <para>The clock counts time spent wanting the OTHER mode and resets the instant the wish
    /// agrees with the state, so a flicker never accumulates toward a switch — which is the whole
    /// of what a hysteresis has to do that a simple delay does not.</para>
    /// </summary>
    public static (RideMode Mode, float HoldSec) StepMode(
        RideMode current, float holdSec, RideMode wants, float dt)
    {
        if (wants == current)
            return (current, 0f);
        float held = holdSec + Mathf.Max(dt, 0f);
        return held >= ModeHoldSec ? (wants, 0f) : (current, held);
    }

    // --- THE CRANK PHASE -------------------------------------------------------------------------

    /// <summary>How fast a parked crank settles to level, crank revolutions per second. <b>1.2,
    /// VALUE</b> — a little under one revolution in a second, so a rider who stops pedalling reaches
    /// 3-and-9 within a quarter turn (~0.21 s at worst) rather than snapping there. Anything faster
    /// reads as the legs being yanked; anything slower leaves the cranks drifting after the body has
    /// visibly stopped driving.</summary>
    public const float CoastSettleRevPerSec = 1.2f;

    /// <summary>
    /// <b>One tick of the crank phase</b>, 0..1 around one crank revolution. It advances at
    /// <see cref="CrankHzAt"/> while the cranks are turning and otherwise settles to the nearest
    /// LEVEL position — phase 0 or 0.5, the 3-and-9 of 3B's COAST row.
    ///
    /// <para><b>Presentation-local, never on the wire</b> (3B §1's accumulator note): it is
    /// integrated from a replicated velocity exactly as <c>AvatarVisual._gaitPhase</c> is, so every
    /// client resolves the same cranks for the same body to within interpolation error, and nothing
    /// has to be sent.</para>
    /// </summary>
    /// <param name="phase01">The phase so far.</param>
    /// <param name="flatSpeedMps">Horizontal ground speed.</param>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="cranksTurn">True only when the mode is <see cref="RideMode.Pedal"/>, the body
    /// is on the floor, and the speed is at or above <see cref="CrankStallMps"/>.</param>
    public static float AdvancePhase(float phase01, float flatSpeedMps, float dt, bool cranksTurn)
    {
        float p = float.IsFinite(phase01) ? phase01 - Mathf.Floor(phase01) : 0f;
        float step = Mathf.Max(dt, 0f);
        if (cranksTurn)
        {
            p += CrankHzAt(flatSpeedMps) * step;
            return p - Mathf.Floor(p);
        }
        // Settle to whichever of the two LEVEL phases is nearer, the short way round the circle.
        float target = p < 0.25f ? 0f : p < 0.75f ? 0.5f : 1f;
        float settled = Mathf.MoveToward(p, target, CoastSettleRevPerSec * step);
        return settled - Mathf.Floor(settled);
    }

    /// <summary>True when the cranks should be turning this tick: pedalling, on the floor, and
    /// actually moving. Named rather than inlined because it is the exact predicate the coast
    /// threshold exists to state.</summary>
    public static bool CranksTurn(RideMode mode, bool onFloor, float flatSpeedMps)
        => mode == RideMode.Pedal && onFloor && Mathf.Abs(flatSpeedMps) >= CrankStallMps;

    // --- THE POSE (amplitudes, at weight 1) ------------------------------------------------------
    //
    // WHAT A RIDER HAS TO LOOK LIKE, and what it must not collide with. The on-foot body already
    // owns every channel below; the ride channel takes them over in proportion to its weight and
    // gives them straight back on the dismount. Each amplitude is a fraction of the LIMB's own
    // length or a typed angle well inside MaxBodyTilt, per the discipline MOVE-1 set: the day the
    // real cast lands with different legs, the pose scales with them.
    //
    //   THE LEGS   sit on a crank CIRCLE rather than on a stride. The hip leads the circle (the
    //              foot is furthest forward at top-of-stroke-minus-a-quarter) and the knee folds
    //              most where the pedal is highest, which is the whole difference between a leg
    //              turning a crank and a leg taking a step. The two legs are half a revolution
    //              apart — 3B's "phase offset π".
    //   THE TORSO  pitches forward, further while driving than while coasting. That difference is
    //              the mode's readable half above the waist and the reason PEDAL and COAST are
    //              distinguishable at silhouette distance with the legs hidden behind the frame.
    //   THE ARMS   go to the bars: a fixed hand target either side of the stem, yawing with the
    //              steer. Elbows are whatever that target implies — the solver's answer, never a
    //              typed angle — which is the same thing that makes a carry read as a carry.

    /// <summary>The middle of the hip's swing on the crank, radians, positive = foot forward. 0.30
    /// — the feet sit forward of the hips on a bicycle, which is most of what separates a seated
    /// silhouette from a standing one.</summary>
    public const float CrankHipBiasRad = 0.30f;

    /// <summary>Half the hip's travel around the crank circle, radians. 0.26 (15°) — a
    /// 30° peak-to-peak sweep against the gait's 77°, because a crank is a small circle and a
    /// stride is not. Comfortably inside <c>LocomotionProfile.MaxLegSwingRad</c>.</summary>
    public const float CrankHipAmplitudeRad = 0.26f;

    /// <summary>How far both feet sit pulled in toward their hips on the pedals, as a fraction of
    /// leg length — the seated knee that is there at every crank angle. 0.20, between the apex
    /// tuck's 0.18 and the coil's 0.24.</summary>
    public const float CrankKneeBaseFold = 0.20f;

    /// <summary>How much more the knee folds at the top of the stroke than at the bottom, as a
    /// fraction of leg length. 0.12, so one leg swings between 0.08 and 0.32 while the other does
    /// the opposite — the scissor that reads as pedalling.</summary>
    public const float CrankKneeAmplitudeFold = 0.12f;

    /// <summary>The rider's forward torso pitch while pedalling, radians. <b>0.21 (12°)</b> —
    /// 3B's S1 table, provisional there. Inside <c>AvatarVisual.MaxBodyTilt</c> (0.60) with room
    /// for the lean and the slope to compose rather than fight the clamp.</summary>
    public const float PedalTiltRad = 0.21f;

    /// <summary>The rider's forward torso pitch while coasting, radians. <b>0.105 (6°)</b> — 3B's
    /// S1 table: "more upright". Exactly half the pedalling pitch, so the mode change is a visible
    /// 6° of chest at any distance the body itself is visible at.</summary>
    public const float CoastTiltRad = 0.105f;

    /// <summary>How fast the torso moves between the two mode pitches, radians per second. 1.1 —
    /// the 6° gap crosses in ~0.1 s, inside the 0.3 s the hysteresis already spent deciding, so the
    /// pitch arrives with the mode rather than trailing it.</summary>
    public const float TiltEaseRate = 1.1f;

    /// <summary>How far forward of the shoulder each hand sits on the bars, as a fraction of arm
    /// length. 0.72.</summary>
    public const float BarReachFraction = 0.72f;

    /// <summary>How far ABOVE the hanging hand each hand sits on the bars, as a fraction of arm
    /// length. 0.55 — hands at roughly chest height on a rig whose arm hangs to the hip.</summary>
    public const float BarRiseFraction = 0.55f;

    /// <summary>Half the bar's width, as a fraction of arm length. 0.28 — hands a little outside
    /// the shoulders, which is where a bar puts them.</summary>
    public const float BarHalfWidthFraction = 0.28f;

    /// <summary>How far the bars yaw at full steer, radians. 0.35 (20°) — enough that a hard turn
    /// visibly turns the hands, small enough that the arms never cross the body.</summary>
    public const float BarYawRad = 0.35f;

    /// <summary>
    /// <b>Where the two hands sit on the bars</b>, as offsets from each arm's own hanging rest
    /// hand, in the arm's rest-local frame — the exact shape <c>AvatarVisual</c>'s carry and aim
    /// targets already use, so the shoulder is never written and cannot leave its socket.
    /// </summary>
    /// <param name="armLengthM">Shoulder to hand on this rig.</param>
    /// <param name="steer01">Steer, −1..+1, positive turning toward +X. Yaws the bar about its
    /// centre: the inside hand comes back, the outside hand goes forward.</param>
    public static (Vector3 Left, Vector3 Right) BarHands(float armLengthM, float steer01)
    {
        float a = Mathf.Max(armLengthM, 0.01f);
        float s = Mathf.Clamp(float.IsFinite(steer01) ? steer01 : 0f, -1f, 1f);
        float yaw = s * BarYawRad;
        float halfW = BarHalfWidthFraction * a;
        float reach = BarReachFraction * a;
        float rise = BarRiseFraction * a;
        // Rotate each grip about the bar's centre in the XZ plane. The rig faces -Z.
        float cos = Mathf.Cos(yaw);
        float sin = Mathf.Sin(yaw);
        Vector3 Grip(float x) => new(
            (x * cos) + (0f * sin),
            rise,
            -reach - (x * sin));
        return (Grip(halfW), Grip(-halfW));
    }

    /// <summary>
    /// <b>The pose one ride tick asks for, in the units the rig consumes.</b> Weight is applied by
    /// the caller (it lerps toward these targets by <c>BikeLayer.Blend</c>) — this returns the
    /// FULL-weight pose, the way <see cref="VerbPose.Blend"/> returns a blended one and
    /// <see cref="AnticipationCoil.For"/> returns a scaled one. Splitting it this way is what lets
    /// the airborne partition survive underneath the crank pose instead of being multiplied away.
    /// </summary>
    /// <param name="crankPhase01">Where the cranks are, 0..1 around one revolution.</param>
    /// <param name="tiltRad">The eased torso pitch, positive forward — see
    /// <see cref="PedalTiltRad"/> / <see cref="CoastTiltRad"/> and <see cref="TiltEaseRate"/>. Eased
    /// by the caller so a mode change crosses the gap rather than stepping over it.</param>
    public static Targets For(float crankPhase01, float tiltRad)
    {
        float p = float.IsFinite(crankPhase01) ? crankPhase01 - Mathf.Floor(crankPhase01) : 0f;
        float theta = p * Mathf.Tau;
        // The right leg is half a revolution behind the left — 3B's "phase offset π".
        float thetaR = theta + Mathf.Pi;
        return new Targets(
            HipPitchL: CrankHipBiasRad + (CrankHipAmplitudeRad * Mathf.Cos(theta)),
            HipPitchR: CrankHipBiasRad + (CrankHipAmplitudeRad * Mathf.Cos(thetaR)),
            KneeFoldL: CrankKneeBaseFold + (CrankKneeAmplitudeFold * Mathf.Sin(theta)),
            KneeFoldR: CrankKneeBaseFold + (CrankKneeAmplitudeFold * Mathf.Sin(thetaR)),
            ForwardTiltRad: tiltRad);
    }

    /// <summary>The torso pitch a mode asks for, radians, positive forward.</summary>
    public static float TiltFor(RideMode mode) => mode == RideMode.Pedal ? PedalTiltRad : CoastTiltRad;

    /// <summary>
    /// <b>How much of the airborne partition a mounted body keeps.</b> 0.40 — 3B's S2, provisional
    /// there, taken as-is: "the existing airborne partition applies at reduced amplitude (~40%)
    /// because the feet stay on the pedals". Applied at the SOURCE of <c>airTuck</c>,
    /// <c>airReach</c> and the landing absorb, so every downstream consumer — legs, lift, knee fold,
    /// arms, elbow, torso pitch — is reduced by one number in one place and none of them can drift
    /// out of agreement with the others.
    /// </summary>
    public const float AirAmplitudeFraction = 0.40f;

    /// <summary>How far the airborne partition is scaled down at a given ride weight. <b>Exactly 1
    /// at weight 0</b> (<c>1 − 0 × x</c> is <c>1</c>, bit for bit), which is the identity that keeps
    /// every unmounted body this packet did not touch bit-identical to what shipped.</summary>
    public static float AirAmplitudeAt(float rideWeight)
        => 1f - (Mathf.Clamp(rideWeight, 0f, 1f) * (1f - AirAmplitudeFraction));

    /// <summary>The exact no-op pose — the cranks level at 3-and-9 with no pitch. Named rather than
    /// inlined because "a body that is not riding changes nothing" is a claim several tests
    /// make.</summary>
    public static readonly Targets None = new(0f, 0f, 0f, 0f, 0f);

    /// <summary>One ride tick's pose.</summary>
    /// <param name="HipPitchL">Left hip angle, radians, <b>positive swings the foot forward</b> —
    /// the same sign convention <c>LocomotionProfile.LegAngleFor</c> answers in.</param>
    /// <param name="HipPitchR">Right hip angle, radians, positive forward.</param>
    /// <param name="KneeFoldL">Left foot pulled in toward its hip, as a fraction of leg length.
    /// <b>The rider is seated on the bike, not standing on the floor</b>, so this never reaches
    /// <c>AvatarVisual.CrouchDropM</c>: dropping the hips for a fold the saddle is carrying would
    /// sink the whole body through the bike.</param>
    /// <param name="KneeFoldR">Right foot pulled in toward its hip, as a fraction of leg length.</param>
    /// <param name="ForwardTiltRad">Torso pitch, radians, <b>positive forward</b>. The rig's own
    /// sign for forward is negative; the conversion happens once, where the tilt is assembled.</param>
    public readonly record struct Targets(
        float HipPitchL,
        float HipPitchR,
        float KneeFoldL,
        float KneeFoldR,
        float ForwardTiltRad);
}
