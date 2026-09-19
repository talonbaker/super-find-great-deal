using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>MOVE-5f — the anticipation crouch, as pose arithmetic.</b> The last unbuilt item in Talon's
/// Movement Feel Lab brief:
///
/// <blockquote>"The crouch must never delay the actual launch — the jump physically starts the
/// frame the button is pressed. Two candidate approaches, test both as a toggle in the lab: 1. A
/// real, visible crouch pose plays before liftoff. 2. The crouch is faked — baked into the first
/// few (already-in-motion) frames of the non-uniform rise curve. Holding the jump button longer
/// should produce a longer/deeper coil (bigger anticipation). Coil depth vs. hold duration should
/// be its own tunable knob."</blockquote>
///
/// <para><b>This class is approach 1 — the real coil — and nothing else.</b> Approach 2 is a
/// gravity term and lives where gravity lives (<c>AvatarMotor.LaunchCoilFactor</c>), for the reason
/// MOVE-4c's apex hang lives there: a curve that shapes the arc is not a pose.</para>
///
/// <para><b>Pure and static for <see cref="VerbPose"/>'s reason</b>, and modelled on it directly:
/// <see cref="AvatarVisual"/> keeps the rig half — which node, which blend, which solver — and this
/// keeps the numbers, so every figure below is an assertion in <c>dotnet test</c> rather than a
/// caption under a screenshot. Nothing here reads or writes simulation state.</para>
///
/// <para><b>THE CORRECTION THE BRIEF COULD NOT KNOW IT NEEDED, and it is the whole design.</b> The
/// brief was written believing the jump might fire on release. <b>It does not: the jump fires at
/// full <c>JumpVelocity</c> on the PRESS</b>, and height is carried afterwards by a release cut
/// that triples gravity while the body is rising (<c>AvatarMotor.GravityFor</c>). So "hold longer →
/// deeper coil" cannot be resolved before the launch, because <i>at the launch the hold duration is
/// zero and unknowable</i>. Sampling it would mean holding the impulse back for at least one tick,
/// and that is the deferred launch RIG-1 already rejected on Talon's behalf:</para>
///
/// <blockquote>"I don't want to put something into the development that is 'expensive' by
/// default... how this player character informs the player they have jumped, AFTER they have
/// jumped, is that one leg shoots up into the air as a clearly 'launching' action motion."</blockquote>
///
/// <para><b>So the coil is resolved the way the shipped jump already resolves height: after the
/// launch, continuously, out of a fact that is already true.</b> It deepens for as long as the body
/// is still <i>rising</i> — which is exactly as long as the player is still holding, because
/// releasing is what triples gravity and ends the rise. A tap's coil is shallow because the jump
/// was cut short; a held jump's coil is deep because it was not. <b>Not one tick of the launch is
/// delayed, and nothing here needs to know the future.</b></para>
///
/// <para><b>The weight principle, stated as a property of this file rather than as an intention:</b>
/// nothing in this class is consulted by anything that decides. It is handed an elapsed rise time
/// the body has already spent and answers with angles. There is no input it can gate and no state
/// change it can hold open.</para>
/// </summary>
public static class AnticipationCoil
{
    /// <summary>Which anticipation is running. <b>Knob 53</b>, and it ships at
    /// <see cref="Off"/> — see <c>MotorTuning.AnticipationMode</c> for why the no-op is the shipped
    /// setting even though approach 1 costs the arc nothing.</summary>
    public const int Off = 0;

    /// <summary>Approach 1: a real, visible coil pose, played into an already-rising body. <b>Pose
    /// only.</b> It writes no velocity, no gravity and no wire byte, so the arc at this mode is the
    /// approved arc to the bit.</summary>
    public const int RealCoil = 1;

    /// <summary>Approach 2: the coil faked into the first frames of the rise curve — a gravity term
    /// (<c>AvatarMotor.LaunchCoilFactor</c>), no pose at all. <b>It moves the arc by construction</b>,
    /// which is what makes it a different candidate rather than a second spelling of mode 1.</summary>
    public const int BakedCoil = 2;

    // --- THE ENVELOPE ---------------------------------------------------------------------------

    /// <summary>
    /// <b>How deep the coil is, 0..1, after <paramref name="riseSec"/> seconds of rising</b> —
    /// the coil-depth-versus-hold-duration relationship, and the one function the whole approach
    /// reduces to.
    ///
    /// <para><c>u = clamp(riseSec / coilSec, 0, 1)</c>, then <c>smoothstep(u) = u²(3 − 2u)</c>.
    /// Smoothstep rather than a linear ramp for <c>AvatarMotor.ApexHangFactor</c>'s reason, which is
    /// the same reason here: <c>w'(0) = w'(1) = 0</c>, so the coil neither starts nor stops with a
    /// corner. A linear ramp would be continuous in the pose and discontinuous in its rate — a
    /// twitch on the take-off frame and a second one at full depth.</para>
    ///
    /// <para><b>The arithmetic, at the shipped ascent lengths.</b> A held jump rises for
    /// <c>JumpVelocity / Gravity = 8.4 / 22 = 0.3818 s</c>; a jump released on the first airborne
    /// tick rises for <c>8.4 / 66 = 0.1273 s</c>, because the cut triples gravity. At the shipped
    /// <c>AnticipationCoilSec</c> of 0.22 s that is <c>u = 1</c> against <c>u = 0.5786</c>, so the
    /// held coil reaches <b>1.000</b> and the tap's reaches <b>0.617</b>. The tap's is 62% as deep
    /// and, because it releases from a lower value at the same rate, also briefer. That is the
    /// entire read.</para>
    /// </summary>
    /// <param name="riseSec">Seconds this flight has spent with a positive vertical velocity.</param>
    /// <param name="coilSec">Knob 55 — the rise time at which the coil reaches full depth.</param>
    public static float Depth(float riseSec, float coilSec)
    {
        if (!(coilSec > 0f) || !(riseSec > 0f))
            return 0f;
        float u = Mathf.Min(1f, riseSec / coilSec);
        return u * u * (3f - (2f * u));
    }

    /// <summary>The deepest a coil can get on an ascent of <paramref name="ascentSec"/> seconds —
    /// <see cref="Depth"/> evaluated at the end of the rise. A convenience for stating the
    /// relationship in a test and in the lab's readout; the body itself never calls it, because the
    /// body has no idea how long its own ascent is going to be and must not.</summary>
    public static float AchievedDepth(float ascentSec, float coilSec) => Depth(ascentSec, coilSec);

    /// <summary>How fast the coil unwinds once the body stops rising, per second. <b>6 — exactly
    /// <c>AvatarVisual.AirborneFoldRate</c></b>, deliberately rather than by coincidence: that is
    /// the rate the airborne fold already hands the gait over at, and 0.167 s of unwind against a
    /// held jump's 0.335 s of descent means the coil is spent into the apex tuck rather than
    /// overlapping it at full strength.
    ///
    /// <para>Linear, like every other envelope in <see cref="AvatarVisual"/> — the take-off kick,
    /// the landing absorb and the lean's anticipation are all linear decays, and the file's own
    /// argument is that a release has an end and an exponential tail would smear a fraction of it
    /// across the whole flight.</para></summary>
    public const float ReleaseRate = 6f;

    // --- THE POSE (the amplitudes, at depth 1) ---------------------------------------------------
    //
    // WHAT A COIL HAS TO LOOK LIKE, and what it must not collide with. The body already does five
    // things around a jump, all of them ratified:
    //
    //   the push-off kick     0.16 s AFTER launch, a SCISSOR - lead thigh up and folded, trail leg
    //                         driving back and straight. FOLLOW-THROUGH, not anticipation.
    //   the apex tuck         both knees up across the top of the arc, keyed to airborne weight
    //   the downward reach    legs extending for the floor as the fall builds
    //   the landing absorb    both knees, GROUNDED, and the only fold that drops the hips
    //   the rise/fall tilts   the chest opening on the drive up, bracing through the fall
    //
    // The coil is a GATHER: the body drawing itself in around its own centre. Three channels, and
    // each is chosen because it is legible at silhouette distance and because nothing above already
    // owns it as the same reading:
    //
    //   BOTH KNEES fold, symmetrically. The scissor is asymmetric and lasts 0.16 s; the coil is
    //              symmetric and lasts the rise. They are additive on one term and clamped by
    //              MaxLimbFold with everything else, which is what the clamp is for.
    //   THE TORSO  pitches FORWARD. The rise tilt pitches BACK, and they are not two readings of
    //              one event: the rise tilt reads HOW FAST the body is going up (it is keyed to
    //              _airDrive and is spent by the apex), the coil reads HOW LONG the player has
    //              committed. A tap has high initial drive and no commitment; the two quantities
    //              genuinely differ, and having them move in opposite directions is what makes the
    //              difference visible instead of merely present.
    //   THE ARMS   fold in at the elbow and sweep back at the shoulder. VerbPose's own finding:
    //              at thirty metres a folded arm stops being a separate line beside the torso and
    //              a straight one does not.
    //
    // NO VERTICAL SCALE TERM, and it is not an oversight. RIG-1 deleted LandCompressY and left the
    // argument in AvatarVisual.cs beside it: "a squashed mesh and a bent knee are two different
    // readings of the same landing, and a body that does both is a body doing it twice." A coil is
    // the same event. The knee is the reading; there is no second one.
    //
    // NO HIP DROP EITHER, and here the reason is geometry rather than taste. CrouchDropM exists
    // because a knee bent WITH THE FEET ON THE FLOOR lowers the hips by exactly what it shortened
    // the legs by. The coil is airborne by construction, so there is no floor to push against and
    // the fold simply lifts the feet - which is what a tuck is. That is also why MOVE-5c's HasKnees
    // exposure does not arise here: the drop this coil contributes to CrouchDropM is identically
    // zero on every rig, so a knee-less body can neither fold nor sink. AnticipationCoilTests pins
    // it rather than leaving it to this paragraph.
    //
    // Every amplitude is a fraction of the LIMB's own length or a typed angle well inside
    // MaxBodyTilt, per the discipline MOVE-1 set: the day the real cast lands with different legs,
    // the pose scales with them.

    /// <summary>How far both feet pull in toward their hips at full coil, as a fraction of leg
    /// length. 0.24 — <b>81.1 degrees of knee</b> on equal segments (<c>2·acos(1 − 0.24)</c>),
    /// which sits between the apex tuck's 0.18 and the launch snap's 0.38 and reads as a body
    /// gathering rather than as a body already tucked. On the greybox's measured 0.548 m leg it
    /// pulls the foot 13.2 cm toward its hip.
    ///
    /// <para>Additive with the tuck and clamped by <c>MaxLimbFold</c> with it, which is the case the
    /// clamp was written for: "a landing out of a launch snap stacks two of them".</para></summary>
    public const float CoilKneeFold = 0.24f;

    /// <summary>The coil's forward torso pitch at full depth, radians. 0.22 (12.6 degrees) —
    /// comfortably inside <c>AvatarVisual.MaxBodyTilt</c> (0.60) so it composes with the lean, the
    /// skid and the landing fold rather than fighting the clamp for room. It is a little over twice
    /// <c>JumpRiseTiltRad</c> (0.10, backward), so a fully coiled body nets about 0.12 rad forward
    /// at the top of a held rise where an uncoiled one is 0.10 rad back: a 19-degree swing in the
    /// outline, which is what survives thirty metres.</summary>
    public const float CoilTiltRad = 0.22f;

    /// <summary>How far both elbows fold at full coil, as a fraction of arm length. 0.22 — between
    /// the duck walk's "extended" 0.10 and the tuck's "arms in" 0.45. Guarded per arm exactly as
    /// the airborne tuck and the verb fold are: a body carrying an armful does not get that arm
    /// folded into its ribs by a pose.</summary>
    public const float CoilArmFold = 0.22f;

    /// <summary>The coil's shoulder pitch at full depth, radians, positive forward. −0.28 — swept
    /// back and down along the body, the smaller sibling of <c>VerbPose.SlideArmSweepRad</c>
    /// (−0.35). Back rather than forward because the coil is a gather: arms come <i>in</i> to the
    /// line of the body, and an arm reaching forward is a different pose called a stride.</summary>
    public const float CoilArmSweepRad = -0.28f;

    /// <summary>
    /// The pose one coil asks for, in the units the rig consumes. <b>Depth 0 answers with the exact
    /// zero pose</b> — every field <c>0f</c> — so a body outside a coil, and every body at all when
    /// <c>AnticipationMode</c> is <see cref="Off"/>, is bit-identical to what shipped before this
    /// class existed. That identity is the whole safety argument for touching a ratified body, and
    /// it is asserted in <c>AnticipationCoilTests</c> rather than claimed here.
    /// </summary>
    /// <param name="coil">The eased coil weight, 0..1 — <see cref="Depth"/> scaled by knob 54 and
    /// by however far the release has unwound it.</param>
    public static Targets For(float coil)
    {
        float c = Mathf.Clamp(coil, 0f, 1f);
        return new Targets(
            KneeFold: CoilKneeFold * c,
            ForwardTiltRad: CoilTiltRad * c,
            ArmFold: CoilArmFold * c,
            ArmSweepRad: CoilArmSweepRad * c);
    }

    /// <summary>The exact zero pose. Named rather than inlined because "an uncoiled body changes
    /// nothing" is a claim several tests make, and one constant is easier to keep true than four
    /// literals.</summary>
    public static readonly Targets None = new(0f, 0f, 0f, 0f);

    /// <summary>One coil's pose.</summary>
    /// <param name="KneeFold">Both feet pulled toward their hips, as a fraction of leg length.
    /// <b>Airborne only, so it never reaches <c>AvatarVisual.CrouchDropM</c></b> — see the block
    /// comment above.</param>
    /// <param name="ForwardTiltRad">Torso pitch, radians, <b>positive forward</b>. The rig's own
    /// sign for forward is negative; the conversion happens once, where the tilt is assembled.</param>
    /// <param name="ArmFold">Both elbows, as a fraction of arm length.</param>
    /// <param name="ArmSweepRad">Both shoulders, radians, positive forward.</param>
    public readonly record struct Targets(
        float KneeFold,
        float ForwardTiltRad,
        float ArmFold,
        float ArmSweepRad);
}
