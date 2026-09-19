using Godot;
using MpFoundation.Game.Aim;
using MpFoundation.Net;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Third-person orbit camera on the collision-safe rig — a SpringArm3D shape-casts a
/// camera-sized sphere so the view can never enter geometry or dip under the floor,
/// and pitch is hard-clamped.
///
/// <para><b>"The camera's job is to never be noticed" is retired (Talon, 2026-08-16).</b> That
/// sentence stood here from iteration 1 and it is what he was rejecting: <i>"the same camera has
/// been there since day one, you noticed that and you saw it, and now we're changing it so that I
/// can feel something different."</i> An action camera that communicates speed does the opposite of
/// going unnoticed, and the conventional vocabulary for it — a lens that widens as you accelerate,
/// an orbit that eases out at a sprint and pulls in at a walk, a look-ahead that frames where you
/// are going — was all deliberately excluded from this rig. It is in now, all four channels driven
/// off one eased speed value (<see cref="_speed01"/>) so they move as one thing rather than four.
/// What is still excluded: trauma shake, and any effect that moves the lens without the player
/// having done something. This is a co-op game with a slapstick register, not a racing sim, and a
/// camera that makes people motion-sick is a failure of a different kind.</para>
///
/// <para><b>The landing dip (MOVE-4c, spec §5) is inside that rule, not an exception to it.</b> It
/// fires on a landing the player's own body just took, and it ships at <c>0.00 m</c> — an exact
/// no-op — so the shipped camera is the one that was played and approved.</para>
///
/// <para><b>It has NO threshold, and that is Talon's ruling of 2026-08-27 (MOVE-4f, choice C).</b>
/// This class used to say the dip fired "at the same fall-speed threshold the knees and the
/// landing sound already use", and that the threshold was what kept a hop chain from putting a
/// periodic vertical oscillation at 2-4 Hz into the view — the band motion sickness lives in.
/// MOVE-4e measured the premise and falsified it: <c>LandMinFallMps</c> of 2.5 m/s is a 0.105 m
/// fall, the measured jog tap lands at <b>5.27 m/s</b> and a held jump at about <b>9.5</b>, so the
/// gate separated nothing — every hop this motor can produce is on its far side. The harm was real
/// and the mechanism was imaginary. So the dip's amplitude is now a <b>continuous ramp from
/// zero</b> in the landing fall speed (<see cref="DipIntensityFor(float)"/>): a tap's dip is a
/// fraction of a millimetre <i>by construction rather than by tuning</i>, and there is no value of
/// the fall speed at which the response steps. The knees and the sound keep their own gate,
/// untouched, at 2.5. See <see cref="NotifyLanding"/>.</para>
///
/// Rig: [this: lagged focus, clamped into free space] -> [_pivot: yaw+pitch]
///      -> [SpringArm3D: pure length sensor] + [Camera3D: placed by this script].
/// The camera is deliberately NOT the arm's child: SpringArm3D force-sets its
/// children's global position every physics frame, which forbids the graded arm
/// return below. The arm just measures; this script places the lens.
/// </summary>
public partial class SandboxCamera : Node3D, ILookAngles
{
    /// <summary>Radians of look per pixel of mouse travel. <b>Public since FP-1</b> — the
    /// first-person rig reads this one rather than declaring a second copy, so the day a
    /// sensitivity slider lands there is one number for it to move.</summary>
    public const float MouseSensitivity = 0.0025f;
    // A stick reports a HELD position, not a delta like the mouse, so it cannot share the
    // mouse's event-driven _UnhandledInput path — it is polled in _PhysicsProcess instead
    // (see ApplyStickLook). 2.6 rad/s at full deflection (~150 deg/s): deliberate rather than
    // whippy, both the standard third-person default and the right register for a horror game,
    // where a camera that snaps undermines the dread it is supposed to be building.
    private const float LookSpeed = 2.6f;
    // Negative pitch = camera raised, looking down.
    //
    // CATCH-1 (2026-08-16): the up-look side used to stop at +0.30 rad — 17.2 degrees — and its
    // comment said why, and the reason was not a design one: "clamped well short of horizontal so
    // the orbit can never dive under the floor". That was a rig workaround for a fixed 3.6 m orbit
    // arm, inherited from the mp-foundation era, and because SanitizeAimPitch clamps the replicated
    // aim into this same range it capped EVERY aimed verb in the game. Talon could not swing the
    // net at anything above his own eyeline.
    //
    // The floor-dive is now solved on its own terms instead — see ArmLimitForPitch, which shortens
    // the orbit as the look rises so the lens rides ABOVE the ground by construction rather than by
    // being forbidden to try. With that in place the clamp is free to be symmetric with the
    // look-down side: 1.05 rad, 60.2 degrees, up as well as down. Symmetric on purpose — an
    // asymmetric look range is a thing that has to be justified every time somebody reads it, and
    // there is no longer anything to justify.
    //
    // Public (WP-L3, aim substrate): AvatarMotor.SanitizeAimPitch clamps the replicated aim
    // pitch into this SAME range, so a legitimate aim ray can never claim an angle the camera
    // rig itself would refuse to look at — single source of truth, referenced, not duplicated.
    public const float PitchMin = -1.05f;
    public const float PitchMax = 1.05f;
    /// <summary>The longest the orbit is ever asked to be, metres — the ceiling
    /// <see cref="ArmLimitForPitch"/> hands back when nothing is limiting it, and the reach of the
    /// sprint end of <see cref="ArmAtSprintM"/>. Raised from the shipped 3.6 to cover the new sprint
    /// orbit; ordinary standing now sits at <see cref="ArmAtRestM"/>, well inside it.</summary>
    private const float ArmLength = 4.6f;

    // --- The speed cue (MOVE-1, 2026-08-16) -----------------------------------------------------
    //
    // Four channels, one input: LocomotionProfile.SpeedCue01 of the body's ground speed, eased. They
    // are deliberately not four independent effects — a camera whose FOV, orbit and framing each ran
    // on their own curve reads as instability rather than as speed.

    /// <summary>Orbit length at a stand, metres. Tighter than the shipped 3.6, so the difference
    /// between standing and sprinting is a real change of framing rather than a nuance.</summary>
    private const float ArmAtRestM = 3.15f;

    /// <summary>Orbit length at a dead sprint, metres. The camera falls back and lets the world
    /// come at you; <see cref="ArmLength"/> is its ceiling.</summary>
    private const float ArmAtSprintM = 4.5f;

    /// <summary>How fast the speed-driven orbit length eases, per second. Lazy on purpose — the arm
    /// should trail the gear change rather than track it, and it is also what keeps the ordinary
    /// "contraction is instant" rule below from snapping the view in the moment a player stops:
    /// this base moves smoothly, so the measured length it feeds moves smoothly too.</summary>
    private const float ArmSpeedRate = 2.2f;

    /// <summary>Degrees of extra field of view at a dead sprint. <b>The single cheapest, strongest
    /// speed cue there is</b> — the world stretches at the edges and closes back in when you
    /// stop.</summary>
    private const float SpeedFovGainDeg = 14f;

    /// <summary>How fast the speed FOV moves, degrees/second. Fast enough to track the speed ramp
    /// closely (this is a speed cue, not a second acceleration cue) without being able to snap.</summary>
    private const float SpeedFovDegPerSec = 30f;

    /// <summary>How far ahead of the player the focus is thrown at a dead sprint, metres. The
    /// shipped rig had 0.7 m saturating at <b>1 m/s</b> — i.e. fully on before the character had
    /// finished its first step, which is a constant offset wearing a look-ahead's clothes. Scaled
    /// properly it is the channel that actually frames a run.</summary>
    private const float LookAheadAtSprintM = 2.4f;

    /// <summary>How far ahead of the player the focus is thrown at a walk, metres.</summary>
    private const float LookAheadAtRestM = 0.25f;

    /// <summary>Follow stiffness at a dead sprint. Lower than the standing
    /// <see cref="FollowStiffness"/>, so the focus trails further behind the faster you go — the
    /// camera visibly catching up under acceleration, and settling as you brake.</summary>
    private const float FollowStiffnessAtSprint = 6f;

    /// <summary>Vertical clearance the lens keeps above the plane its focus point stands on, in
    /// metres, when the look-up angle would otherwise swing the orbit under the ground. The cast
    /// sphere is 0.25 m; the extra 0.20 m is the same order as <see cref="ArmMargin"/> and keeps
    /// the near-clip plane out of the dirt on the slope cases the analytic bound cannot see.
    /// <para>This is what replaced the old +0.30 rad pitch clamp — see <see cref="PitchMax"/>.
    /// The SpringArm3D's shape cast is still the real safety net for geometry; this bound exists
    /// so the cast is never ASKED for a position deep under the terrain in the first place, which
    /// is the state the old comment was defending against.</para></summary>
    private const float LensGroundClearanceM = 0.45f;

    // --- THE LANDING DIP (MOVE-4c, spec §5) -------------------------------------------------------
    //
    // Three knobs and one safety floor. The knobs read MotorTuning.Current exactly the way
    // AvatarMotor's twenty-one converted constants do, so MOVE-4d's slider moves them live; the
    // floor is NOT a knob, for the reason a panel must never be able to author a camera below the
    // floor plane.
    //
    // Everything about this term lives in this class, which is client-local presentation. It
    // touches no MoveIntent, no MoveState, no AvatarMotor call and nothing that crosses the wire,
    // so "weight is skin, not friction" holds structurally rather than carefully: the dip cannot
    // delay, gate or damp an input because it never sees one. It is not replicated, and each
    // client computes its own from the fall speed its own body reported.

    /// <summary>How far the focus point drops at a full-intensity landing, metres. <b>0.00 is the
    /// shipped value and an exact no-op</b> — <see cref="SandboxCamera"/> had no dip of any kind
    /// before MOVE-4c, so today's shipped value for this knob is zero and the default-identity
    /// contract (spec §8) requires it to stay zero. Range 0.00–0.50; spec §5.5 names 0.30 as where
    /// it stops reading as impact and 0.45 as the discomfort band, and the range deliberately
    /// reaches past both because a slider that cannot produce a bad value teaches nothing.</summary>
    public static float CameraDipStrengthM => MotorTuning.Current.CameraDipStrengthM;

    /// <summary>How long the dip takes to reach full depth, seconds. 0.05 — three ticks: short
    /// enough to read as impact, long enough not to be a one-frame jump cut.</summary>
    public static float CameraDipAttackSec => MotorTuning.Current.CameraDipAttackSec;

    /// <summary>How long the dip takes to unwind, seconds. 0.26 — deliberately the same number as
    /// <c>AvatarVisual.LandAbsorbSec</c>, because the knees and the camera are recovering from the
    /// same event and should finish together. <b>They are separate knobs anyway</b>: the brief is
    /// explicit that the dip is "a separate, independent tunable knob… not tied to the squash or
    /// gravity curve", so the defaults agreeing is the design and the coupling is not.</summary>
    public static float CameraDipRecoverSec => MotorTuning.Current.CameraDipRecoverSec;

    /// <summary>
    /// <b>How fast the dip ramps with landing fall speed</b> — the exponent on the normalised fall
    /// speed, and the one knob MOVE-4f's choice C costs. Dimensionless; 6 is shipped.
    ///
    /// <para><b>What it buys.</b> The dip is
    /// <c>strength × (fall / LandFullFallMps)^CameraDipRampPower</c>, saturating at full strength
    /// once the fall reaches <see cref="AvatarVisual.LandFullFallMps"/>. That is continuous from
    /// zero and strictly increasing, so there is no fall speed at which it steps — and the
    /// exponent is what separates a hop from a drop <i>without</i> a threshold. At the shipped 6,
    /// and at the <b>maximum</b> <see cref="CameraDipStrengthM"/> of 0.50 m, the measured 5.27 m/s
    /// jog-tap landing dips <b>0.00142 m</b>; a held jump's 9.5 m/s lands at 0.0488 m, 34x deeper,
    /// and a 3.3 m drop reaches the full 0.50.</para>
    ///
    /// <para><b>Measured in engine, MOVE-4f</b>, by running the same two landings at strength 0.00
    /// and again at 0.50 and aligning the paired frames: the drop moved the rendered view
    /// <b>43.97 px</b> and the tap <b>0.33 px</b>, two runs each, against an instrument whose
    /// run-to-run floor on a static frame is 0.002 px. The tap is real and sub-pixel; the drop is
    /// 133x deeper. That ratio is the knob's whole job.</para>
    ///
    /// <para><b>Why 6 and not 5 or 4.</b> It is the smallest integer exponent at which the tap's
    /// dip at the knob's maximum falls to <see cref="PerceptibleDipM"/>: 5 leaves 0.0038 m and 4
    /// leaves 0.0100 m, both of them above it, and 0.0100 m is inside what MOVE-4e's own tracker
    /// resolved. "Imperceptible by construction" has to mean something measurable or it means
    /// nothing.</para>
    ///
    /// <para><b>The range reaches down to 1.0, which is linear and bad</b>, for
    /// <see cref="CameraDipStrengthM"/>'s stated reason — a slider that cannot produce a bad value
    /// teaches nothing. The row is pinned on the coupled quantity rather than on itself, so the
    /// panel paints it red the moment strength and ramp together put a jog tap above the
    /// perception floor.</para>
    /// </summary>
    public static float CameraDipRampPower => MotorTuning.Current.CameraDipRampPower;

    /// <summary>
    /// <b>How deep this landing dips, as a fraction of <see cref="CameraDipStrengthM"/></b> — the
    /// dip's own curve, and the whole of MOVE-4f's choice C.
    ///
    /// <para>Deliberately NOT <c>AvatarVisual.LandIntensityFor</c>. That curve has a legibility
    /// floor of 0.25 because it is only ever asked about a landing that already passed the knee
    /// absorb's gate — "a landing that qualified must be visible". The dip has no gate to qualify
    /// for, so a floor there would be exactly the step this design exists to delete: every
    /// touchdown, however soft, would dip at least a quarter of the strength. The two curves share
    /// their full-intensity speed (<see cref="AvatarVisual.LandFullFallMps"/>) and differ only in
    /// shape, which keeps one definition of "a landing at full weight" in the tree.</para>
    ///
    /// <para>Pure and static so the shape is provable without a Node, a physics tick or a running
    /// engine — <see cref="DipDepthAt"/>'s reason.</para>
    /// </summary>
    /// <param name="fallMps">The deepest fall speed of the flight, m/s. Never negative; a
    /// touchdown with no fall in it is 0 and dips exactly nothing.</param>
    /// <param name="fullFallMps">The fall speed at which the dip reaches full strength.</param>
    /// <param name="rampPower">The exponent — <see cref="CameraDipRampPower"/>.</param>
    public static float DipIntensityFor(float fallMps, float fullFallMps, float rampPower)
    {
        if (!float.IsFinite(fallMps) || fallMps <= 0f)
            return 0f;
        float full = Mathf.Max(fullFallMps, MinFullFallMps);
        // Saturation at the TOP, not a gate at the bottom: below `full` the response is a strictly
        // increasing power of the fall speed with no floor, no epsilon and no branch.
        float x = Mathf.Min(fallMps / full, 1f);
        return Mathf.Pow(x, Mathf.Max(rampPower, MinRampPower));
    }

    /// <summary>The live-tuning overload — the one <see cref="NotifyLanding"/> calls.</summary>
    public static float DipIntensityFor(float fallMps)
        => DipIntensityFor(fallMps, AvatarVisual.LandFullFallMps, CameraDipRampPower);

    /// <summary>
    /// <b>The landing fall speed of a jog tap jump, m/s — measured, not derived.</b> MOVE-4e read
    /// the tap's apex at 0.467 m in a headed engine, three runs of three on each of two trees, and
    /// <c>sqrt(2 × 22 × 1.35 × 0.467)</c> was 5.27. It is the softest landing this motor can
    /// actually produce from a jump, which is what makes it the right probe for "can a hop be
    /// felt": every other landing dips more.
    ///
    /// <para><b>MOVE-8: 5.27 → 4.65.</b> Talon's ruling took the tap's apex to 0.300 m and fall
    /// gravity to 36, and <c>sqrt(2 × 24 × 1.50 × 0.300)</c> is 4.65 — SOFTER, so the probe still
    /// probes the softest case. Re-measured against <c>MotorArc.JogTap</c>, which reproduces
    /// MOVE-4e's own 0.467 m at MOVE-4e's own tuning, so this is the same observation re-taken
    /// rather than a different quantity.</para>
    ///
    /// <para>Still a hand-typed number rather than a live re-derivation, for
    /// <c>MotorTuningInvariants.LongestAirtimeSec</c>'s reason — it is the number a person
    /// observed, and a probe that silently followed the sliders would stop being able to say the
    /// arc had moved.</para>
    /// </summary>
    public const float JogTapLandingMps = 4.65f;

    /// <summary>
    /// <b>How deep a landing dip has to be before it is worth calling visible</b>, metres — the
    /// ceiling the ramp row's pin is stated against.
    ///
    /// <para>Derived from the only measurements anyone has of this camera. MOVE-4e's
    /// horizontal-edge tracker resolved <b>56 px/m</b> of camera vertical translation and repeated
    /// its settled row to 0.08 px; MOVE-4f's paired-frame alignment measured <b>96 px/m</b> on the
    /// same rig. 0.0018 m is 0.10 px at the first scale and 0.17 px at the second — sub-pixel on a
    /// 720-line frame either way, and about 0.029 degrees at the shipped orbit. It is deliberately
    /// stated in <i>metres</i> rather than pixels: a pixel is a property of the capture, and this
    /// has to mean the same thing at any resolution.</para>
    ///
    /// <para><b>Not a knob</b>, for <see cref="MinFocusHeightM"/>'s reason: it is a fact about
    /// perception and measurement, not a taste. It is the ceiling the ramp row's pin is stated
    /// against, so the panel turns red the moment strength and ramp together put a jog tap above
    /// it.</para>
    /// </summary>
    public const float PerceptibleDipM = 0.0018f;

    /// <summary>Floor on the normalising fall speed, m/s. A divisor; the knob table's own minimum
    /// (3.0) is far above it, so this only ever catches a value that reached the field without
    /// passing <c>MotorTuning.Validate</c>.</summary>
    private const float MinFullFallMps = 1e-3f;

    /// <summary>Floor on the ramp exponent. The knob's own minimum is 1.0; below zero the curve
    /// would invert and a feather touchdown would dip hardest, which is the one shape that would
    /// make the no-gate design worse than the gate it replaced.</summary>
    private const float MinRampPower = 0.01f;

    /// <summary>Hard floor on the dipped focus height, metres. <b>Not a knob</b> (spec §5.3): the
    /// dip subtracts from the focus height, and a focus height at or below zero is a camera
    /// orbiting the dirt. 0.25 m is the arm's own cast-sphere radius, which is the smallest height
    /// at which the rig's geometry still means anything.</summary>
    private const float MinFocusHeightM = 0.25f;

    /// <summary>The focus height after this frame's dip, metres — the whole of what the dip changes,
    /// as pure arithmetic so the floor is provable rather than observed.
    ///
    /// <para><b>The safety argument in one line:</b> <see cref="ArmLimitForPitch"/> is monotone
    /// non-decreasing in the focus height, so lowering the focus can only ever SHORTEN the arm. The
    /// dip therefore cannot authorise a lens position the undipped camera would not already have
    /// taken, and the SpringArm3D's 0.25 m sphere cast — which is what actually keeps the lens out
    /// of geometry — is untouched and still sweeps.</para></summary>
    public static float DippedFocusHeight(float focusHeightM, float dipM)
        => Mathf.Max(focusHeightM - dipM, MinFocusHeightM);

    /// <summary>Hard floor on the orbit length, metres. At full up-look the analytic bound below
    /// wants a very short arm (that is simply what "3.6 m behind a 1.2 m child while looking 60
    /// degrees up" costs geometrically), and this stops it collapsing into the near-clip plane.
    /// The own-body cull already drops the avatar's mesh once the arm dips inside it
    /// (<see cref="BodyHideBufferM"/>), so a fully-raised look reads as a tight over-the-head view
    /// rather than as the inside of a skull.</summary>
    private const float MinArmM = 0.35f;
    private const float ArmMargin = 0.30f;
    private const float FollowStiffness = 9f;    // iteration-1 lag at a stand: lazy but never floaty
    // The spring-arm cast ORIGIN must always sit in free space: pushed against a wall,
    // the raw focus point (avatar + up + look-ahead) lands on/inside the wall plane and
    // the sphere-cast degenerates — the arm then flickers between zero and full length
    // every tick, holding the camera alternately at the pivot and OUTSIDE the room
    // (the "blocks of texture" defect). Clearance must exceed the cast sphere's 0.25m.
    private const float FocusClearance = 0.35f;
    // Occluders leaving the arm's path used to snap the camera outward multiple metres
    // in one tick. Extension is graded instead; CONTRACTION stays instant — the camera
    // must never spend a frame inside whatever just crossed the arm.
    private const float ArmExtendRate = 6f;
    // A focus gap this large in one tick can only be a commanded teleport (TV portal,
    // reset-to-spawn — tens of metres); legit gaps top out around 3.5m (a max
    // reconciliation pop + follow lag + look-ahead). Teleports CUT: lerping the focus
    // across the map drags the arm's cast origin through every wall in between.
    private const float TeleportSnapM = 6f;

    // P4b (2026-08-08 playtest): the spring arm's shape-cast excludes the target's OWN body
    // (SyncPlayerExclusions), so nothing stops it contracting past the character's own
    // silhouette when an obstruction sits close to the pivot — "the arm shortens against the
    // obstruction until the near plane is inside the character." Below AvatarHalfWidthM plus
    // this buffer (covering the camera's own near-clip plane), the standard third-person fix
    // is applied: the camera keeps respecting real geometry (never pushed THROUGH a wall to
    // hold a minimum length) and instead the avatar's own body drops off this camera's
    // CullMask for the frames it would otherwise render from inside its own head — see
    // AvatarVisual.OwnBodyRenderLayer for why that is local-only and multiplayer-safe.
    private const float BodyHideBufferM = 0.12f;

    // Aim-rig FOV ease-in (WP-L3, item 4): client-local presentation ONLY — narrows the
    // rendered lens while raised so aiming reads as a deliberate zoom-in, exactly the way
    // feat/tier0-camcorder's viewfinder crop does for the camcorder. This NEVER touches
    // validity: AimQuery.QueryGroup takes its own fovDegrees parameter from the calling verb
    // and never reads Camera3D.Fov (there is no Camera3D parameter for it to read) — a verb's
    // shot can be judged "in frame" by the true, un-eased frustum while the player's own screen
    // is still mid-zoom. See the WP-L3 PR's consumption contract for the worked proof.
    private const float DefaultFov = 75f;
    private const float AimFov = 55f;
    // Sweeps the whole range in exactly AimController.RaiseDurationSec, same "locked to the
    // stance's own commit duration" technique the camcorder branch's FovDegPerSec uses — the
    // zoom completes precisely when Raising/Lowering itself commits, not approximately.
    private const float AimFovDegPerSec = (DefaultFov - AimFov) / AimController.RaiseDurationSec;

    public float Yaw { get; private set; }

    /// <summary>Current look pitch, radians, always within [PitchMin, PitchMax] — the WP-L3 aim
    /// substrate's LocalInputIntentSource reads this every tick alongside Yaw to build
    /// MoveIntent.AimPitch (see that field's doc comment for why the body's own facing isn't
    /// enough).</summary>
    public float Pitch => _pitch;

    /// <summary>Offline sandbox toggles mouse capture on Esc itself (no pause overlay). In the
    /// networked game the PauseOverlay owns Esc, so it sets this false to avoid double-handling.</summary>
    public bool HandlesPauseToggle { get; set; } = true;

    /// <summary>The actual Camera3D (valid after Attach) — exposed for test invariants.</summary>
    public Camera3D? CameraNode => _camera;

    /// <summary>The graded arm length actually placing the camera this frame — exposed for the
    /// P4b camera lab's diagnostic logging and test invariants.</summary>
    public float RenderedArmM => _renderedArm;

    private Node3D _pivot = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private Node3D _target = null!;

    /// <summary>
    /// <b>The node this camera is currently following</b>, or <c>null</c> once
    /// <see cref="Detach"/> has run. Read-only on purpose: it is the fact
    /// <c>SandboxAvatar.AdoptFollowCamera</c> checks before it will believe a camera claiming to
    /// be its own (MOVE-4f scope item 1), so a settable one would defeat the guard it feeds.
    /// </summary>
    public Node3D? FollowTarget => _target;

    private Rid _targetRid;
    private bool _hasTargetRid;
    // The spring arm (and the focus-clamp ray) must ignore EVERY player body, not just
    // this camera's own: avatars sit on collision layer 1, so two players face-to-face
    // otherwise collapse each other's cameras. Kept in sync with the live-avatar
    // registry via its version stamp so joins and leaves re-sync the exclusion set.
    private int _exclusionVersion = -1;
    private readonly Godot.Collections.Array<Rid> _excludeRids = new();

    /// <summary>The focus-clamp ray query, built once instead of per physics tick (perf audit
    /// 2026-08-07). CollisionMask=1 reproduces the <c>collisionMask: 1</c> argument the old
    /// per-tick <c>PhysicsRayQueryParameters3D.Create</c> passed; every other field keeps the
    /// class default, which is what Create left them at. Only From/To are rewritten per tick.
    /// Exclude is re-assigned from <see cref="SyncPlayerExclusions"/> rather than set once,
    /// because the engine snapshots the exclusion set at assignment time — a join or leave must
    /// push the rebuilt set through again, not merely mutate the array behind its back.</summary>
    private readonly PhysicsRayQueryParameters3D _focusQuery = new() { CollisionMask = 1 };

    /// <summary>IntersectRay's result key as a String Variant, built once — the same lookup
    /// <c>hit["position"]</c> performed, without re-marshalling the key every tick.</summary>
    private static readonly Variant PositionKey = "position";
    private float _pitch = -0.28f;
    private Vector3 _focus;
    private float _renderedArm = ArmAtRestM; // graded arm length actually shown

    /// <summary>Eased 0..1 speed cue — the single input every speed channel reads. Eased here rather
    /// than inside each channel so the FOV, the orbit, the look-ahead and the follow stiffness can
    /// never disagree about how fast the body is going.</summary>
    private float _speed01;

    /// <summary>The speed-driven orbit length, before pitch and occlusion get their say, metres.</summary>
    private float _speedArm = ArmAtRestM;

    /// <summary>Current normalised speed cue, 0 at a walk and 1 at a dead sprint. Exposed so a
    /// capture harness can log what the camera thought the speed was beside what it drew.</summary>
    public float SpeedCue01 => _speed01;

    /// <summary>Seconds since the qualifying touchdown that armed the current dip envelope, or a
    /// negative value when no envelope is running. One float and one peak: no latched "in a dip"
    /// state to enter, leave or chatter.</summary>
    private float _dipElapsedSec = -1f;

    /// <summary>The depth this envelope is heading for, metres — <c>strength × intensity</c>,
    /// captured at the moment of the landing so a slider dragged mid-dip cannot rewrite an
    /// envelope that is already unwinding.</summary>
    private float _dipPeakM;

    /// <summary>The dip applied to the focus height this frame, metres — exposed so a capture
    /// harness and the unit tier can read what the camera actually did rather than infer it.</summary>
    public float DipNowM => CurrentDipM();

    /// <summary>
    /// <b>A landing happened; dip the focus.</b> MOVE-4c, spec §5, rewired by MOVE-4f — called
    /// from <c>SandboxAvatar</c>'s touchdown branch on <b>every</b> landing, beside (and no longer
    /// inside) the <c>fall &gt;= AvatarVisual.LandMinFallMps</c> gate that fires the landing sound
    /// and the knee absorb.
    ///
    /// <para><b>NOT threshold-gated any more</b> — MOVE-4f, Talon's choice C, 2026-08-27. It takes
    /// the raw fall speed rather than a pre-computed intensity precisely so no caller can hand it
    /// a gated number: the curve is <see cref="DipIntensityFor(float)"/>'s and lives here, which is
    /// what makes "continuous from zero" a property of the class rather than of its call site. A
    /// touchdown with no fall in it arrives as <c>0</c> and dips exactly zero; nothing about the
    /// knee absorb's own 2.5 m/s gate, the landing sound's fan-out or the intensity they share
    /// changed.</para>
    ///
    /// <para><b>Idempotent under re-trigger (MECHANICS §4).</b> A second qualifying landing inside a
    /// running envelope RESTARTS it at the higher of (the new depth, the depth showing right now)
    /// and never sums — summing is how two landings 40 ms apart produce a dip deeper than any single
    /// landing can reach. The restart is placed at the point on the new attack where the displayed
    /// depth already sits, so the focus continues downward instead of stepping UP to re-attack;
    /// a step up would read as a bounce, which is the exact reading §5.4 rejects a spring for.</para>
    /// </summary>
    /// <param name="fallMps">The deepest fall speed of the flight that just ended, m/s — the same
    /// quantity <c>AvatarVisual</c>'s gate is tested against, handed over ungated.</param>
    public void NotifyLanding(float fallMps)
    {
        if (!float.IsFinite(fallMps))
            return;
        float peak = CameraDipStrengthM * Mathf.Clamp(DipIntensityFor(fallMps), 0f, 1f);
        float showing = CurrentDipM();
        peak = Mathf.Max(peak, showing);
        if (!(peak > 0f))
        {
            // The shipped case: strength 0 (or a touchdown with no fall in it), nothing showing.
            // Stay idle rather than arming an envelope that would compute zero every tick for a
            // third of a second — which is also what keeps the ungated call cheap on a hop chain.
            _dipElapsedSec = -1f;
            _dipPeakM = 0f;
            return;
        }
        _dipPeakM = peak;
        _dipElapsedSec = RestartElapsedFor(showing, peak, CameraDipAttackSec);
    }

    /// <summary>Where on a restarted attack ramp a dip of <paramref name="showingM"/> already sits,
    /// seconds — the re-trigger rule's arithmetic, pure so it is provable rather than observed.
    /// Zero for a fresh landing; the full attack for a re-trigger that cannot go any deeper.</summary>
    public static float RestartElapsedFor(float showingM, float peakM, float attackSec)
    {
        if (!(peakM > 0f))
            return 0f;
        return Mathf.Clamp(showingM / peakM, 0f, 1f) * Mathf.Max(attackSec, MinEnvelopeSec);
    }

    /// <summary>Floor on either envelope leg, seconds. Both are divisors in a normalised ramp; the
    /// knob table's own minima (0.01 attack, 0.02 recover) are above it, so this only ever catches a
    /// value that reached the field without passing <c>MotorTuning.Validate</c>.</summary>
    private const float MinEnvelopeSec = 1e-4f;

    /// <summary>
    /// <b>The dip showing right now, metres</b> — a linear attack and a linear release, no spring
    /// and no hold (spec §5.4). A spring overshoots on the way back and lifts the camera ABOVE its
    /// rest height, which reads as a bounce rather than a landing; <c>AvatarVisual</c>'s own absorb
    /// decays linearly for the same stated reason, "the recovery IS the settle". One event, one
    /// shape.
    /// </summary>
    private float CurrentDipM()
        => DipDepthAt(_dipElapsedSec, _dipPeakM, CameraDipAttackSec, CameraDipRecoverSec);

    /// <summary>The envelope itself, as pure arithmetic — <c>ArmLimitForPitch</c>'s reason: a shape
    /// this load-bearing should be provable without a Node, a physics tick or a running engine.
    /// A negative <paramref name="elapsedSec"/> means no envelope is armed.</summary>
    public static float DipDepthAt(float elapsedSec, float peakM, float attackSec, float recoverSec)
    {
        if (elapsedSec < 0f || !(peakM > 0f) || !float.IsFinite(elapsedSec))
            return 0f;
        float attack = Mathf.Max(attackSec, MinEnvelopeSec);
        if (elapsedSec < attack)
            return peakM * (elapsedSec / attack);
        float recover = Mathf.Max(recoverSec, MinEnvelopeSec);
        float since = elapsedSec - attack;
        return since < recover ? peakM * (1f - since / recover) : 0f;
    }

    /// <summary>Advance the dip envelope one tick and retire it once it has unwound. Separate from
    /// <see cref="CurrentDipM"/> so the depth can be read as many times as a frame needs without the
    /// clock moving under it.</summary>
    private void AdvanceDip(float dt)
    {
        if (_dipElapsedSec < 0f)
            return;
        _dipElapsedSec += dt;
        if (_dipElapsedSec >= Mathf.Max(CameraDipAttackSec, MinEnvelopeSec)
                            + Mathf.Max(CameraDipRecoverSec, MinEnvelopeSec))
        {
            _dipElapsedSec = -1f;
            _dipPeakM = 0f;
        }
    }

    /// <summary>
    /// <b>The single writer of <see cref="_target"/>, and the one place an avatar learns which
    /// camera is following it</b> (MOVE-4f scope item 1).
    ///
    /// <para><b>Why the registration lives here and not on a public setter.</b> Before MOVE-4f,
    /// <c>SandboxAvatar._followCamera</c> was assigned on exactly two lines, both inside
    /// <c>ConfigureNetworkedInstance</c>'s owned-client branch — so every dev harness in the repo
    /// (the since-removed dev labs, <c>SandboxSelfTest</c>, <c>SandboxWorld</c>) built a real
    /// follow camera the avatar never
    /// heard about, and MOVE-4e measured the consequence: the landing dip was a permanent no-op in
    /// the one scene whose whole job is tuning it. A public <c>FollowCamera</c> setter would fix
    /// that and hand any caller anywhere the ability to point a networked session's camera field
    /// at a camera that is not following that body. <b>Attaching IS the registration</b> instead:
    /// the field can only ever hold a camera whose <see cref="FollowTarget"/> is that same avatar,
    /// and the avatar re-checks that before believing the claim.</para>
    ///
    /// <para>Idempotent, and it releases the previous target first, so a
    /// <see cref="Reattach"/> from body A to body B cannot leave A still naming this camera as
    /// its own.</para>
    /// </summary>
    private void BindFollowTarget(Node3D? next)
    {
        Node3D? previous = _target;
        if (!ReferenceEquals(previous, next) && previous is SandboxAvatar was
            && GodotObject.IsInstanceValid(was))
            was.ReleaseFollowCamera(this);

        _target = next!;
        if (next is SandboxAvatar now)
            now.AdoptFollowCamera(this);
    }

    public void Attach(Node3D target)
    {
        BindFollowTarget(target);
        _focus = target.GlobalPosition;

        _pivot = new Node3D { Name = "Pivot" };
        AddChild(_pivot);
        _arm = new SpringArm3D
        {
            Name = "Arm",
            SpringLength = ArmLength,
            Margin = ArmMargin,
            // Shape-cast a camera-sized sphere (not the default ray): the arm then
            // keeps the whole lens outside geometry, not just the ray's hit point —
            // a bare ray leaves the camera grazing the surface it stopped at.
            Shape = new SphereShape3D { Radius = 0.25f },
            CollisionMask = 1, // world geometry; carried/thrown props are layer 1 too — fine
        };
        _pivot.AddChild(_arm);
        SyncArmCastRadius();
        _camera = new Camera3D
        {
            Current = true,
            Fov = DefaultFov,
            Attributes = new CameraAttributesPractical
            {
                DofBlurFarEnabled = true,
                DofBlurFarDistance = 9f,
                DofBlurFarTransition = 5f,
                DofBlurNearEnabled = true,
                DofBlurNearDistance = 0.35f,
                DofBlurNearTransition = 0.25f,
                DofBlurAmount = 0.012f,
            },
        };
        _pivot.AddChild(_camera); // sibling of the arm, NOT its child — see the class doc
        _camera.Position = new Vector3(0, 0, _renderedArm);
        // The avatar's own collider must not push its camera arm around (nor block
        // the focus-clamp ray below) — and neither may any OTHER player's body.
        if (target is CollisionObject3D body)
        {
            _targetRid = body.GetRid();
            _hasTargetRid = true;
        }
        SyncPlayerExclusions();

        // Unconditional again, since 2026-09-04. This used to be gated on
        // Ui.HowToPlayPanel.FirstRunGateActive, because the level's first-run How-to-Play door was
        // the ONE surface that could open over a live avatar: the spawn confirmation is a network
        // round-trip and could land at any point while the player was still reading, yanking the
        // mouse out from under the panel's X mid-read (a trap Talon hit live). That door is gone —
        // How-to-Play is now only ever asked for, from a pause menu that has already suppressed
        // world UI and freed the mouse itself — so there is no longer a panel for this to race.
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Rebuilds the spring-arm (and focus-ray) exclusion set from the live-avatar
    /// registry: this camera's own body plus every other player body, so no player collider
    /// can ever push this camera's arm. Re-run whenever <see cref="SandboxAvatar.LiveVersion"/>
    /// changes (a join or a leave).</summary>
    private const float ArmCastRadiusMaxM = 0.25f;
    private const float ArmCastRadiusMinM = 0.06f;

    /// <summary>The arm's sphere cast must fit inside the followed body's own collision capsule.
    /// A shape cast that STARTS overlapping a wall reports no hit at all (Godot ignores initial
    /// overlaps), so a body pressed against a wall with the arm pointing into it would let the
    /// camera walk straight through. The original reference body's capsule (0.36 m) was wider
    /// than the 0.25 m cast and hid this; the box-kid's (about 0.15 m) is not — measured by
    /// SandboxSelfTest's camera_never_crosses_wall on the extraction. Re-synced every physics
    /// tick because the body's proportions are measured after the model loads, which can be
    /// after the camera attaches.</summary>
    private void SyncArmCastRadius()
    {
        float capsule = _target is SandboxAvatar avatar
            ? avatar.Proportions.CapsuleRadiusM
            : AvatarProportions.Fallback.CapsuleRadiusM;
        float radius = Mathf.Clamp(capsule * 0.9f, ArmCastRadiusMinM, ArmCastRadiusMaxM);
        if (_arm.Shape is SphereShape3D sphere && !Mathf.IsEqualApprox(sphere.Radius, radius))
            sphere.Radius = radius;
    }

    private void SyncPlayerExclusions()
    {
        _arm.ClearExcludedObjects();
        _excludeRids.Clear();
        if (_hasTargetRid)
        {
            _arm.AddExcludedObject(_targetRid);
            _excludeRids.Add(_targetRid);
        }
        foreach (SandboxAvatar avatar in SandboxAvatar.Live)
        {
            Rid rid = avatar.GetRid();
            if (_hasTargetRid && rid == _targetRid)
                continue;
            _arm.AddExcludedObject(rid);
            _excludeRids.Add(rid);
        }
        _exclusionVersion = SandboxAvatar.LiveVersion;
        // Push the rebuilt set into the hoisted query (see _focusQuery's doc comment). An empty
        // array is the same "exclude nothing" the old code expressed by skipping the assignment.
        _focusQuery.Exclude = _excludeRids;
    }

    /// <summary>Height above the target's origin that this camera frames.
    ///
    /// <para>Derived from the character's own measured body (<see cref="AvatarProportions"/>)
    /// rather than the absolute 0.70 m this used to be. That number was three-quarters of the
    /// way up the original squat body and is navel height on a 1.24 m figure — the follow camera
    /// would have been framing the player's belt from the day a taller figure became the player
    /// character. Falls
    /// back to the shipped constant for any target that is not an avatar (the spectate rigs and
    /// labs point this at plain Node3Ds).</para>
    ///
    /// <para>Read live rather than cached at <see cref="Attach"/>: an avatar re-measures itself
    /// when its appearance rebuilds, which for a networked spawn happens a beat AFTER the camera
    /// attaches (the avatar-key synchronizer catching up to the authority's pick).</para></summary>
    public float FocusHeight => _target is SandboxAvatar avatar
        ? avatar.Proportions.CameraFocusHeightM
        : AvatarProportions.Fallback.CameraFocusHeightM;

    // --- Detach (phase 1c) ---------------------------------------------------------------------
    // STATE-CASCADE-TABLE hard constraint 3 and BEHAVIOR-BIBLE §10.2's third missing API, in as
    // many words: "SandboxCamera has no detach — it must be built". This is it. Two levels,
    // because the failure states need one of them and a future spectate mode needs the other, and
    // conflating them would have shipped the wrong one for both.

    /// <summary>Fully detached: no target, no follow, no input. <see cref="_PhysicsProcess"/> is
    /// inert and the lens holds wherever it was left.</summary>
    public bool Detached { get; private set; }

    /// <summary>Following normally, but deaf to the mouse and the stick. What incapacitation
    /// uses.</summary>
    public bool InputDetached { get; private set; }

    /// <summary>
    /// Sever this camera from its target entirely. Idempotent, and safe to call on a camera whose
    /// target has already been freed — which is the case that motivated it: <see cref="_PhysicsProcess"/>
    /// carries a defensive null/validity guard added after a live playtest crash, and a real
    /// detach makes that guard structural instead of a net under a known hazard.
    ///
    /// <para>Not what the failure states use — see <see cref="SetInputDetached"/> for why a
    /// knocked-out player keeps watching.</para>
    /// </summary>
    public void Detach()
    {
        Detached = true;
        InputDetached = true;
        BindFollowTarget(null);
        _hasTargetRid = false;
        _arm?.ClearExcludedObjects();
        _excludeRids.Clear();
        _focusQuery.Exclude = _excludeRids;
    }

    /// <summary>Point a detached (or attached) camera at a new target. The counterpart to
    /// <see cref="Detach"/>; reuses the existing rig rather than rebuilding it when there is
    /// already one, so re-attaching never spawns a second <c>Camera3D</c> fighting over
    /// <c>Current</c>.</summary>
    public void Reattach(Node3D target)
    {
        if (_pivot == null)
        {
            Attach(target);
            Detached = false;
            InputDetached = false;
            return;
        }
        BindFollowTarget(target);
        _focus = target.GlobalPosition;
        if (target is CollisionObject3D body)
        {
            _targetRid = body.GetRid();
            _hasTargetRid = true;
        }
        SyncPlayerExclusions();
        Detached = false;
        InputDetached = false;
    }

    /// <summary>
    /// Keep following, stop listening. The mouse and the stick move nothing while this is set,
    /// and the orbit holds its last angles.
    ///
    /// <para>This is what a Knocked Out or Frozen player's camera does, and the split from
    /// <see cref="Detach"/> is a register decision as much as a technical one: the player must
    /// stop <i>driving</i> the camera (the half the shipped controllable-ragdoll defect got
    /// wrong), but they should keep <i>seeing</i>. Watching yourself slide across camp as an ice
    /// block is the comedy beta plan §0's register law asks for; a black screen throws it away,
    /// and a camera the player can still orbit says "you are fine" while they are not.</para>
    ///
    /// <para>Idempotent, and no different from normal play in every other respect — the follow,
    /// the spring arm, the occlusion clamp and the focus logic are untouched, so nothing about
    /// this state can regress the camera work P4b landed.</para>
    /// </summary>
    public void SetInputDetached(bool detached) => InputDetached = detached;

    /// <summary>Test/replay hook: set orbit angles directly (pitch still clamped).</summary>
    public void SetOrbit(float yaw, float pitch)
    {
        Yaw = yaw;
        _pitch = Mathf.Clamp(pitch, PitchMin, PitchMax);
    }

    /// <summary>Pure stick-look arithmetic — no Node, no Input, no device attached — so the
    /// response curve and the pitch clamp are provable without a physical pad in hand.
    /// <paramref name="stick"/> is the raw <c>Input.GetVector</c> result; the clamps are passed
    /// in rather than read off the private consts so this stays callable from outside the
    /// instance. Mirrors the mouse path's sign convention exactly (stick down == mouse moved
    /// down == pitch decreases), so an inverted axis is a test failure, not a matter of taste.</summary>
    internal static (float Yaw, float Pitch) ApplyStickLook(
        Vector2 stick, float yaw, float pitch, float dt, float pitchMin, float pitchMax)
    {
        // Squared response: fine control near centre, full speed at the edge. Standard for a
        // stick, and the reason a pad can aim at all despite far less precision than a mouse.
        stick *= stick.Length();
        yaw -= stick.X * LookSpeed * dt;
        pitch = Mathf.Clamp(pitch - stick.Y * LookSpeed * dt, pitchMin, pitchMax);
        return (yaw, pitch);
    }

    /// <summary>
    /// <b>The longest orbit that keeps the lens off the floor at this look angle.</b> Pure
    /// arithmetic, no Node and no physics, so the floor guarantee is provable rather than
    /// observed.
    ///
    /// <para>The rig places the lens at pivot-local <c>(0, 0, L)</c> and rotates the pivot by
    /// <c>pitch</c> about X, so the lens sits <c>L·sin(pitch)</c> BELOW the focus point when the
    /// look is up. The focus point is <paramref name="focusHeightM"/> above the character's own
    /// feet, so the lens clears the ground exactly while
    /// <c>L ≤ (focusHeight − LensGroundClearanceM) / sin(pitch)</c>. Below the horizon
    /// (<c>pitch ≤ 0</c>) the orbit rises away from the ground and nothing is limited — the
    /// shipped 3.6 m arm and its shape cast are untouched there, which is the whole
    /// look-down half of the range and every frame of ordinary play.</para>
    ///
    /// <para><b>An upper bound, never a minimum.</b> The caller takes the smaller of this and the
    /// arm's measured clearance, so this can only ever pull the camera IN — it can never push it
    /// through the wall the shape cast just found. That ordering is the reason adding this cannot
    /// regress the P4b occlusion work.</para>
    ///
    /// <para>Honest scope: this bounds the lens against the plane the CHARACTER stands on, which
    /// is what the old clamp was really protecting. On a steep upslope directly behind the player
    /// the ground can still rise into the bound — the SpringArm3D's sphere cast is what handles
    /// that, exactly as it always has, and it is now being handed a reachable request instead of
    /// one several metres underground.</para>
    /// </summary>
    /// <param name="maxArmM">The orbit length being asked for — the speed-driven length since
    /// MOVE-1. Defaults to <see cref="ArmLength"/>, the rig's ceiling, so every existing caller and
    /// every assertion in <c>CatchInstrumentTests</c> is byte-identical.</param>
    public static float ArmLimitForPitch(float pitch, float focusHeightM, float maxArmM = ArmLength)
    {
        float ceiling = Mathf.Clamp(maxArmM, MinArmM, ArmLength);
        float rise = Mathf.Sin(pitch);
        if (rise <= 1e-3f)
            return ceiling; // level or looking down: the orbit climbs, nothing to bound.
        return Mathf.Clamp((focusHeightM - LensGroundClearanceM) / rise, MinArmM, ceiling);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Esc toggles mouse capture so the sandbox is usable without the pause overlay.
        if (HandlesPauseToggle && @event.IsActionPressed("pause"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
            return;
        }

        // Detached in either sense: the player is not driving this camera. The pause toggle above
        // is deliberately still live — a knocked-out player must still be able to reach the menu.
        if (Detached || InputDetached)
            return;

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, PitchMin, PitchMax);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_arm != null) SyncArmCastRadius();
        // Defense-in-depth (audit P1 + a live playtest crash): a reconnect that leaves this
        // camera's target stale must never crash every physics tick. The structural fix is
        // Gameplay's teardown freeing this camera (a _players sibling of its target - see
        // Attach's call site in SandboxAvatar) in the same pass as the target itself, but this
        // guard holds regardless of how a target reference goes stale.
        if (Detached || _target == null || !GodotObject.IsInstanceValid(_target))
            return;
        // A player joined or left since the last sync: rebuild the arm's exclusion set.
        if (_exclusionVersion != SandboxAvatar.LiveVersion)
            SyncPlayerExclusions();
        float dt = (float)delta;

        // Stick look: obeys the same capture gate the mouse path obeys in _UnhandledInput, so
        // the camera can never spin behind an open pause overlay. Polled here rather than
        // event-driven because a stick reports a held position every frame, not a one-shot
        // delta. Costs one Input.GetVector on an idle pad — the actions' own 0.25 deadzone
        // returns Vector2.Zero at rest, so a resting stick moves the camera by exactly nothing.
        // InputDetached gates the stick exactly as it gates the mouse in _UnhandledInput. Both
        // channels, one flag — a look input the pad could still drive while the mouse could not
        // is the "three of four systems updated" shape in miniature.
        if (!InputDetached && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Vector2 stick = Input.GetVector("look_left", "look_right", "look_up", "look_down");
            (Yaw, _pitch) = ApplyStickLook(stick, Yaw, _pitch, dt, PitchMin, PitchMax);
        }

        // Lagged focus point: exponential chase toward the avatar plus a nudge in its
        // travel direction, so sprints read fast without losing the character. Track where
        // the avatar RENDERS (body + reconciliation offset), not the raw body — the netcode
        // hides server corrections on the render layer, and a camera reading the raw body
        // visibly detaches from the mesh every time a correction fires.
        Vector3 basePos = _target is SandboxAvatar avatar
            ? avatar.RenderGlobalPosition
            : _target.GlobalPosition;
        // THE LANDING DIP (MOVE-4c, spec §5.3). The dip lowers the focus HEIGHT and nothing else —
        // it never places the camera. Everything downstream then works unchanged and unaware:
        // ClampFocusToFreeSpace runs on the dipped focus, ArmLimitForPitch is handed the DIPPED
        // height (handed the stale one it would authorise an arm the dipped focus cannot afford),
        // the SpringArm3D's 0.25 m sphere cast is untouched and is still the real safety net, and
        // the _focus.Lerp below softens even a step in the envelope before it reaches the eye.
        // ArmLimitForPitch is monotone non-decreasing in focusHeight, so a dip can only ever pull
        // the camera IN — it can never authorise a position the undipped camera would not have
        // taken, which is what makes "the dip cannot defeat the sweep" a structural claim.
        AdvanceDip(dt);
        float focusHeight = DippedFocusHeight(FocusHeight, CurrentDipM());
        Vector3 targetFocus = basePos + Vector3.Up * focusHeight;

        // THE SPEED CUE (MOVE-1). One eased number, four channels. Read off the body's own velocity
        // — which is replicated, so a remote peer's camera and its owner's agree about it — and
        // normalised through LocomotionProfile so the camera's idea of "fast" is the same one the
        // gait and the gear use.
        float followStiffness = FollowStiffness;
        if (_target is CharacterBody3D body)
        {
            Vector3 flatVel = body.Velocity with { Y = 0 };
            float speed = flatVel.Length();
            float cue = LocomotionProfile.SpeedCue01(speed);
            // Eased, and asymmetrically: the cue rises with the acceleration ramp and falls a shade
            // faster, so pulling up out of a sprint settles the view rather than leaving it wide.
            float rate = cue > _speed01 ? 2.6f : 3.4f;
            _speed01 = Mathf.Lerp(_speed01, cue, 1f - Mathf.Exp(-rate * dt));

            // 1. Look-ahead. Scaled by the cue instead of saturating at 1 m/s (see
            //    LookAheadAtSprintM), and taken from the DIRECTION rather than the raw velocity so
            //    the throw is the value below and not whatever the speed happens to be.
            if (speed > 0.05f)
            {
                targetFocus += flatVel / speed
                    * Mathf.Lerp(LookAheadAtRestM, LookAheadAtSprintM, _speed01);
            }

            // 2. Follow stiffness. Lower at speed = the focus trails further = the camera visibly
            //    catches up under acceleration and settles under braking.
            followStiffness = Mathf.Lerp(FollowStiffness, FollowStiffnessAtSprint, _speed01);
        }
        else
        {
            _speed01 = Mathf.Lerp(_speed01, 0f, 1f - Mathf.Exp(-3.4f * dt));
        }

        // 3. Orbit length. Eased separately and slowly (ArmSpeedRate) so it trails the gear change
        //    — and, critically, so it moves SMOOTHLY: the instant-contraction rule below exists to
        //    keep the lens out of geometry, and a base that stepped down on a stop would make it
        //    snap the whole view in one frame.
        _speedArm = Mathf.Lerp(_speedArm, Mathf.Lerp(ArmAtRestM, ArmAtSprintM, _speed01),
            1f - Mathf.Exp(-ArmSpeedRate * dt));
        // Water (W2, lake-water contract §4): swimming drops the eye to the waterline. From the
        // water you cannot see over the water — free prospect denial, and the cheapest half of
        // what makes the night lake frightening.
        //
        // Applied to the FOCUS point rather than to the lens, deliberately: the orbit rig, the
        // occlusion clamp below and the spring arm all keep working unchanged, and the eye height
        // is a world fact (a plane) rather than an offset from a body that is itself bobbing.
        // Keyed off the avatar's replicated water state, so a remote player watching a swimmer
        // never has to guess.
        if (_target is SandboxAvatar swimmer
            && swimmer.WaterStateNow == Sail.Game.Water.WaterState.Swimming)
        {
            targetFocus.Y = Sail.Game.Water.WaterGeometry.WaterY
                + Sail.Game.Water.WaterGeometry.SwimEyeAboveWaterM;
        }

        // The clamp ray must START in guaranteed-free space: the raw simulated body
        // (server-validated, never inside geometry) — NOT the render position, whose
        // correction offset can transiently sit inside a wall, and a ray started inside
        // a solid reports no hit and silently skips the clamp.
        targetFocus = ClampFocusToFreeSpace(_target.GlobalPosition, targetFocus, focusHeight);
        _focus = _focus.DistanceTo(targetFocus) > TeleportSnapM
            ? targetFocus // commanded teleport: cut, never swoop through the world
            : _focus.Lerp(targetFocus, 1f - Mathf.Exp(-followStiffness * dt));

        GlobalPosition = _focus;
        _pivot.Rotation = new Vector3(_pitch, Yaw, 0);

        // Shorten the orbit as the look rises (CATCH-1) — this is what lets PitchMax be 60 degrees
        // up instead of 17. Written into SpringLength so the engine's own cast is asked for a
        // reachable length, AND applied again as a Min below: SpringArm3D computes its hit length
        // in its own _physics_process, so whether this tick's SpringLength is already reflected in
        // this tick's GetHitLength is a node-order question, and the bound must not depend on the
        // answer. Taking the smaller of the two is correct under either ordering.
        float armLimit = ArmLimitForPitch(_pitch, focusHeight, _speedArm);
        if (!Mathf.IsEqualApprox(_arm.SpringLength, armLimit))
            _arm.SpringLength = armLimit;

        // Place the lens on the pivot ray at the arm's measured clearance: contraction
        // lands the same tick it is measured (never a frame inside geometry), extension
        // is graded so an occluder leaving the arm's path can't teleport the view.
        float measured = Mathf.Min(armLimit, Mathf.Max(0f, _arm.GetHitLength()));
        _renderedArm = measured <= _renderedArm
            ? measured
            : Mathf.Lerp(_renderedArm, measured, 1f - Mathf.Exp(-ArmExtendRate * dt));
        _camera.Position = new Vector3(0, 0, _renderedArm);

        // P4b: hide the target's own body from THIS camera only once the rendered arm dips
        // inside it, so a close obstruction never reads as "the camera is inside your skull".
        // Gated on the actual target type (lab/spectator rigs attach plain Node3Ds with no
        // AvatarVisual to hide, and no Proportions to size the clearance from).
        if (_target is SandboxAvatar selfAvatar)
        {
            float minClearance = selfAvatar.Proportions.HalfWidthM + BodyHideBufferM;
            _camera.SetCullMaskValue(AvatarVisual.OwnBodyRenderLayer, _renderedArm >= minClearance);
        }

        // Third-person body stays visible to everyone else (raised is a stance, not a camera
        // detach) — this only ever affects what the target's OWN camera renders.
        //
        // Keyed on DIRECTION (Raising/Raised -> zoomed, Lowering/Lowered -> wide), not on
        // "!= Lowered" — same fix feat/tier0-camcorder's Task 4 made for its own FOV keying:
        // recovery begins the instant Lowering starts, symmetric with engagement beginning the
        // instant Raising starts, rather than holding the zoom through the whole Lowering
        // transition (see AvatarVisual.Animate's SetAiming call site for the identical rule
        // applied to the arm pose).
        bool aiming = _target is SandboxAvatar avatarForFov
            && avatarForFov.AimStance is AimStance.Raising or AimStance.Raised;
        // 4. The lens. Aiming still wins outright — a zoom that fought a sprint would make the one
        //    verb that needs a steady frame the one that never gets one — and otherwise the FOV
        //    widens with the speed cue. Each branch keeps its own rate: the aim zoom is locked to
        //    AimController's commit duration and must stay so.
        float targetFov = aiming
            ? AimFov
            : DefaultFov + (SpeedFovGainDeg * _speed01);
        float fovRate = aiming || _camera.Fov < DefaultFov ? AimFovDegPerSec : SpeedFovDegPerSec;
        _camera.Fov = Mathf.MoveToward(_camera.Fov, targetFov, fovRate * dt);
    }

    /// <summary>Pulls the focus target back into free space. The ray starts inside the
    /// avatar's own collider (a guaranteed-free point, the avatar itself excluded) and
    /// stops <see cref="FocusClearance"/> short of the first world surface toward the
    /// desired focus — so the spring-arm's cast origin can never start embedded, which
    /// is the root cause of the through-the-wall camera flicker.</summary>
    private Vector3 ClampFocusToFreeSpace(Vector3 basePos, Vector3 targetFocus, float focusHeight)
    {
        // Half the focus height, which on the original body is exactly the 0.35 m this used to be
        // written as. Kept proportional so the ray's origin stays comfortably inside whatever
        // body it belongs to: it must start in guaranteed-free space, and "inside the avatar's
        // own collider" is only guaranteed if it scales with the collider.
        Vector3 safeStart = basePos + Vector3.Up * (focusHeight * 0.5f);
        Vector3 toFocus = targetFocus - safeStart;
        float wanted = toFocus.Length();
        if (wanted < 1e-4f)
            return targetFocus;

        _focusQuery.From = safeStart;
        _focusQuery.To = targetFocus;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(_focusQuery);
        if (hit.Count == 0)
            return targetFocus;

        float clear = Mathf.Max(0f, safeStart.DistanceTo((Vector3)hit[PositionKey]) - FocusClearance);
        return safeStart + toFocus / wanted * clear;
    }
}
