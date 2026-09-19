using System.Collections.Generic;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// The player's visible body. The rig was first built around a soft round creature (a body
/// that is widest at the bottom, stubby feet, nub arms, springy head-sprout, tail puff, big
/// blinking eyes, blush) — deliberately NOT the Among Us bean or the R.E.P.O. pill: the
/// bottom-heavy egg silhouette plus the sprout is its own read. All expressiveness is body
/// language: squash-and-stretch, waddle roll, lean, sprout lag, tumble.
///
/// The visible mesh comes from a roster row (see docs/ART-BIBLE.md) — this class no longer
/// builds procedural sphere geometry. A harvested-parts row instances its model once per
/// avatar and reparents its named parts onto the same
/// animated Node3D rig the procedural version used (<see cref="_body"/>,
/// <see cref="_leftArm"/>/<see cref="_rightArm"/>, <see cref="_leftThigh"/>/
/// <see cref="_rightThigh"/>, <see cref="_leftEye"/>/<see cref="_rightEye"/>,
/// <see cref="_sprout"/>), so every animation below is untouched — only the geometry
/// hanging off each node changed.
///
/// Purely cosmetic — owns no gameplay state. The avatar feeds it velocity/floor/
/// stumble each frame and fires the trigger methods on events, so replication never
/// has to know this class exists.
///
/// AS OF 2026-08-08 IT BUILDS TWO KINDS OF CHARACTER, not one. The above describes the
/// harvested-part family — a part rig. The other is a WHOLE FIGURE: one authored figure
/// kept whole, which is what a player actually is (see <see cref="Build"/> and
/// <see cref="PreferredAvatarKey"/>). Both drive the same animation verbs and the same
/// character face contract; they differ only in how their geometry reaches the rig, and no
/// part rig is ever a whole figure.
/// </summary>
public partial class AvatarVisual : Node3D
{
    /// <summary>The player garment family. The server deals one per joining player
    /// (spawn-index round-robin, sent in the spawn data) — the personalization baseline
    /// until the cosmetics UI packet ships real player choice. Six colors, six players:
    /// no two match.
    ///
    /// <para><b>These are clothes, not a colour wheel</b> (Talon, 2026-08-08 — replacing the
    /// original §4.1 pastel family). Sixteen colours across every hue family, on a deliberate
    /// value ladder. Three rules govern the set, and all three are checkable by
    /// the palette tests rather than left as prose:</para>
    ///
    /// <para><b>1. THE RESERVED ZONE — the only colours barred, and it is narrow.</b> What is
    /// off-limits to clothing is the <i>alarm</i> register: highlighter yellow and warning red,
    /// meaning high chroma AND high value in the 352–12° and 46–70° bands. Those belong to
    /// designed elements that signal something is wrong, and a player must never be mistakable
    /// for one. Everything else is fair game — rich ochre, deep brick, saturated teal all pass,
    /// and the set runs to 0.78 saturation. This is deliberately looser than the first version
    /// of this palette, which banned purity outright and came out muted and small.</para>
    ///
    /// <para><b>2. Separated by VALUE first, not hue</b> — ART-BIBLE §3, and the defect the
    /// fabric pass measured in the palette this replaces. The pastels spanned only 0.132
    /// Rec.601 luma and clipped to near-white under the shipped daylight, crushing hue and
    /// fabric detail together; hue is also the first channel the eye loses at night, which is
    /// exactly when telling players apart matters most. This ladder spans <b>0.663</b>
    /// (0.160 aubergine → 0.823 bone).</para>
    ///
    /// <para><b>3. ORDER IS LOAD-BEARING — the first six entries are the ones players get.</b>
    /// Dealing is <c>palette[spawnIndex % Length]</c> and <c>Protocol.MaxPlayers</c> is 6, so
    /// with a sixteen-colour array only indices 0–5 are ever dealt. Those six are therefore
    /// chosen, not incidental: one from each of six DIFFERENT hue families (neutral, gold,
    /// orange, green, blue, purple), the widest value ladder available, and the best
    /// worst-case separation under normal, deuteranope and protanope simulation. <b>Reordering
    /// this array silently changes what six colours the game actually uses.</b> The remaining
    /// ten are the pool the cosmetics UI packet draws on when it ships real player choice —
    /// they are held to the same rules, just not dealt by default.</para>
    ///
    /// <para>PER-SESSION VARIETY IS NOT BUILT: every session deals the same first six, because
    /// varying the subset needs a seeded offset chosen by the server in the spawn path
    /// (<c>Gameplay.SpawnPlayer</c>) — a different file and another agent's lane during this
    /// wave. The colour index already travels in spawn data and survives reconnect, so nothing
    /// downstream would need to change; it is one server-side pick away.</para>
    ///
    /// <para>NAME IS DEBT: still <c>PastelPalette</c> because ~12 call sites across three
    /// in-flight branches reference it, and renaming mid-wave would collide with other agents.
    /// Rename in one deliberate pass — the same discipline as Issue #62's Camp Quarter
    /// rename.</para>
    /// </summary>
    public static readonly Color[] PastelPalette =
    {
        // ---- The dealt six. One per hue family; reordering changes the game. ----
        new(0.86f, 0.82f, 0.74f), // bone       — canvas tee            luma 0.823  neutral
        new(0.80f, 0.58f, 0.18f), // ochre      — rich broken gold      luma 0.600  gold
        new(0.68f, 0.38f, 0.24f), // terracotta — clay orange           luma 0.454  orange
        new(0.30f, 0.44f, 0.22f), // fern       — camp green            luma 0.373  green
        new(0.14f, 0.20f, 0.42f), // navy       — deep blue             luma 0.207  blue
        new(0.20f, 0.12f, 0.26f), // aubergine  — near-black purple     luma 0.160  purple

        // ---- The choice pool: same rules, awaiting the cosmetics UI packet. ----
        new(0.74f, 0.78f, 0.80f), // chalk      — cool off-white        luma 0.770  neutral
        new(0.83f, 0.70f, 0.42f), // wheat      — pale gold             luma 0.707  gold
        new(0.56f, 0.63f, 0.47f), // sage       — grey-green            luma 0.591  green
        new(0.75f, 0.46f, 0.31f), // clay       — warm mid orange       luma 0.530  orange
        new(0.52f, 0.46f, 0.60f), // heather    — dusty violet          luma 0.494  purple
        new(0.40f, 0.46f, 0.52f), // slate      — grey-blue             luma 0.449  blue
        new(0.61f, 0.35f, 0.43f), // rosewood   — dusty red-mauve       luma 0.437  pink
        new(0.13f, 0.45f, 0.48f), // teal       — saturated deep teal    luma 0.358  teal
        new(0.62f, 0.22f, 0.16f), // brick      — deep red, not alarm   luma 0.333  red
        new(0.34f, 0.12f, 0.13f), // oxblood    — near-black red        luma 0.187  red
    };

    // Squash-and-stretch spring (underdamped on purpose: the overshoot IS the jelly).
    private const float ScaleStiffness = 140f;
    private const float ScaleDamping = 10f;

    // --- The gait (MOVE-1, 2026-08-16) --------------------------------------------------------
    //
    // WHAT WAS HERE, and why none of it survived. A fixed 3.9 Hz churn (6.24 steps/s at sprint,
    // against a real 3.5-4), a 4.5 cm vertical hop, a 6-degree roll, and a permanent ~30-degree
    // forward pitch. No limb travelled through an arc, no foot planted, nothing pushed off, and
    // CRUCIALLY the vibration had no arithmetic relationship at all to the sliding underneath it.
    // That mismatch IS Talon's "tiny little stuttering step", and it is why every previous amplitude
    // tune failed: there was no locomotion system here to tune.
    //
    // WHAT IS HERE NOW. Every number below is derived from ground speed through LocomotionProfile,
    // against the identity stride x cadence = speed. The stance foot is world-stationary by
    // construction; the swing amplitudes, the bob and the arm counter-swing all fall out of the
    // measured leg rather than being authored against one body's proportions.
    //
    // The four constants left here are the ones that are genuinely cosmetic taste — how much the
    // body leans into the gait on top of the physics, not how the gait works.

    /// <summary>Sideways body roll at the peak of each step, radians (~4.6 deg), scaled by gait
    /// amplitude. Smaller than the waddle's 6 degrees because the legs now carry the read.</summary>
    private const float GaitRollRad = 0.08f;

    /// <summary>Torso counter-rotation against the arms, radians (~5.2 deg). The thing that makes a
    /// run look like a run rather than a body being slid along: shoulders and hips oppose.</summary>
    private const float GaitTorsoTwistRad = 0.09f;

    /// <summary>Arm swing as a fraction of the opposite leg's hip angle. Under 1 because arms swing
    /// through a smaller arc than legs, and because a nub arm at a leg's amplitude reads as
    /// flailing.</summary>
    private const float ArmSwingRatio = 0.70f;

    /// <summary>How much of the leg-split-driven body drop is expressed as a bob. 1.0 would be the
    /// exact inverted-pendulum height; a shade under reads better because the rig has no knee to
    /// absorb the rest.</summary>
    private const float BobFollow = 0.85f;

    /// <summary>How fast the gait amplitude eases in and out with gear changes, per second. Slow
    /// enough that a gear change is a transition rather than a pop.</summary>
    private const float GaitAmpRate = 5f;

    /// <summary>How fast the legs fold back to rest once airborne, per second.</summary>
    private const float AirborneFoldRate = 6f;

    // --- Rotational silhouette (MOVE-1, Talon's second amendment, note C) -----------------------
    //
    // In 2D the artist owns the camera, so a strong pose is strong because it is drawn from the one
    // angle that exists. In 3D the player swings the camera wherever they like, and a pose built out
    // of single-axis OFFSETS reads from the side and vanishes head-on. So the swing's weight is put
    // into things that change the silhouette by ROTATING: the torso whips one way while the hips and
    // feet hold the other, and the arm travels through an arc rather than to a coordinate.

    /// <summary>How far the swinging arm pitches through the stroke, radians (~29 deg at the
    /// extremes) — back and up on the coil, through and down on the strike. On the rotation channel,
    /// which the carry and aim poses do not use, so it composes with the existing hand offset
    /// instead of fighting it.</summary>
    private const float SwingArmPitchRad = 0.50f;

    /// <summary>How much the feet resist the torso's swing twist, as a fraction of it. The hips stay
    /// planted while the shoulders whip — the counter-rotation that makes a swing look like it came
    /// from the ground rather than from the waist. The feet are children of this rig rather than of
    /// the pose, so they already do not inherit the twist; this makes the opposition deliberate and
    /// visible rather than merely absent.</summary>
    private const float FootCounterTwist = 0.35f;

    private const float RunEaseIn = 6f;   // sprint weight blends up (~0.17 s)
    private const float RunEaseOut = 4f;  // and back down when it ends

    // --- Anticipation (MOVE-1, Talon's second amendment) ---------------------------------------
    //
    // "Anticipation must move OPPOSITE the action before it moves with it." A lean that is a
    // function of the current acceleration cannot do that - it is already pointing the right way by
    // the time there is any acceleration to point at. What CAN do it is reacting to the ONSET: the
    // instant the acceleration starts changing, the body throws its weight the other way for a
    // moment and then commits. That is a sprinter coiling before a start and a body rocking back
    // before it sets off, and it is the one term in this file that is predictive rather than
    // reactive.

    /// <summary>How far the body throws its weight the WRONG way at the onset of a hard
    /// acceleration or a hard brake, in radians (~6 deg). Deliberately large relative to the
    /// steady-state lean it precedes (~10 deg): an anticipation smaller than the action it
    /// anticipates does not read as anticipation, it reads as noise.</summary>
    private const float AnticipationRad = 0.105f;

    /// <summary>How long the anticipation holds before the real lean takes over, seconds. Short
    /// enough to be a flinch rather than a stagger.</summary>
    private const float AnticipationSec = 0.14f;

    /// <summary>The change in acceleration, per second, that counts as an onset. Tuned against the
    /// real thing: pressing sprint from a stand steps the acceleration from 0 to
    /// AvatarMotor.Acceleration inside one tick, which is ~540 m/s^3 - an order of magnitude over
    /// this - while the noise the yaw-rotating body frame injects sits well under it.</summary>
    private const float AnticipationJerkThreshold = 60f;

    // A carried item rides the same step phase as the waddle, so it bobs with the gait
    // instead of sitting dead-still. Kept deliberately small — a gentle sway, not a swing.
    //
    // HALVED 2026-08-16 (ANIM-1), and the reason is the whole of Gap 0. These numbers used to be
    // the ONLY vertical motion a held item had, because the carry anchor was a child of the avatar
    // ROOT and inherited none of the body's own waddle — so this offset was quietly standing in for
    // the body's hop rather than adding to it. The anchor now hangs inside the pose (see _pose), so
    // it inherits the body's own bob for free and this is the small extra hand sway on
    // top that it always claimed to be. Left non-zero rather than deleted because the hands do move
    // relative to the torso, and because a held item that is rigidly welded to a bobbing chest
    // reads as a prop glued on. MOVE-1 re-based the body's half of that on the derived gait: the
    // bob is now L(1 - cos(split)) off the real legs (0.049 m at full stride on the greybox) rather
    // than a 0.045 m |sin|, so this stays the same 0.022 m of extra hand sway on top of it.
    private const float CarryBobHeight = 0.022f;  // vertical bob, peaking on each footfall
    private const float CarryBobSway = 0.010f;    // side-to-side sway, in phase with the roll
    private const float CarryIdleBreath = 0.012f; // faint rise/fall at a stand, matched to breathing

    // --- The swing (ANIM-1) ------------------------------------------------------------------
    //
    // A WHOLE-BODY swing: the torso already twists, leans, rolls and squashes, and a body whipping
    // its entire trunk round to swing something is both funnier and more on-register than a careful
    // arm animation would be. It satisfies the slapstick register law by construction; where arms
    // exist they ride SwingHandOffset on top.
    //
    // Every one of these is a VALUE call, picked and stated rather than escalated. The arc they ride
    // is the stroke's own drive, so the body and the held thing cannot disagree about which way the
    // stroke goes.

    /// <summary>Body twist through the stroke, in radians (~49 deg at the extremes of the drive).
    /// The coil-and-whip: the torso counter-rotates into the wind-up and unwinds through the
    /// stroke.
    ///
    /// <para><b>0.55 → 0.85 (MOVE-1, Talon's second amendment).</b> This is the one channel that
    /// changes the SILHOUETTE rotationally rather than on a single axis, which is what makes the
    /// pose survive a third-person camera the player can swing anywhere — an offset that reads from
    /// the side reads as nothing head-on. Pushed hard for that reason, and because a keyframe artist
    /// hits extremes deliberately past comfortable while an interpolated rig averages toward
    /// mush.</para></summary>
    private const float SwingYawRad = 0.85f;

    /// <summary>Extra forward pitch at the peak of the FORWARD stroke only, in radians (~15 deg).
    /// Deliberately one-sided: leaning back into a wind-up and forward through a stroke are the same
    /// gesture, and pitching back as well would read as a stumble. 0.18 → 0.26 (MOVE-1, second
    /// amendment: extremes, not averages).</summary>
    private const float SwingLeanRad = 0.26f;

    /// <summary>Sideways body roll through the stroke, in radians (~17 deg) — leaning into the
    /// swing rather than standing square to it. 0.20 → 0.30 (MOVE-1, second amendment). Rotational,
    /// so it reads from any camera angle.</summary>
    private const float SwingRollRad = 0.30f;

    /// <summary>Squash on the coil, stretch on the follow-through, as a fraction of body height.
    /// Applied on top of the existing spring rather than through it, so an interrupted swing
    /// unwinds with the blend instead of ringing the spring.</summary>
    private const float SwingSquash = 0.16f;

    /// <summary>How fast the swing pose eases in and out. Fast — a swing is 0.61 s end to end
    /// and a blend slower than that would never reach full amplitude. Tied to <c>_carryBlend</c>'s 6/s discipline, four times over.</summary>
    private const float SwingBlendRate = 24f;

    // --- The turnaround skid's pose (SKID-1) ------------------------------------------------------
    // ONE branch, driven off the motor's replicated skid flag, and nothing else. The physics already
    // supplies half the read for free: a body shedding 13 m/s² is decelerating hard, so the
    // acceleration lean (see LeanForAccel) pitches it back on its own. What the lean cannot supply is
    // the legs, because the gait keeps striding through the slide as though the feet were still
    // doing the work — and a brake is exactly the pose where they are not.

    /// <summary>Extra backward pitch on top of the acceleration lean while skidding, radians —
    /// 0.14, about 8 degrees. Additive and INSIDE the MaxBodyTilt clamp, for the reason the swing
    /// lean states: a pose term added after the clamp is a pose term that can breach the floor
    /// invariant.</summary>
    private const float SkidLeanRad = 0.14f;

    /// <summary>How far the lead leg is thrown out in front during the slide, as a fraction of the
    /// leg's own maximum swing. 0.92 — near the anatomical limit, because the whole point of the
    /// pose is that the feet are out ahead of the body rather than under it.</summary>
    private const float SkidLegPlantFraction = 0.92f;

    /// <summary>The trailing leg's share of that angle. Both legs go forward — this is a two-footed
    /// brake, not a lunge — but staggered, because two legs at identical angles read as a mannequin
    /// being dragged.</summary>
    private const float SkidTrailLegFraction = 0.55f;

    /// <summary>How fast the brake pose eases in and out. 18/s — under a tenth of a second, because
    /// the shortest skid this can be handed (a jog reversal) is only 0.32 s long and a slower blend
    /// would never reach full amplitude before the state exited. Same discipline as
    /// <see cref="SwingBlendRate"/>, one notch gentler.</summary>
    private const float SkidBlendRate = 18f;

    // --- The jump's pose (JUMP-1, 2026-08-16) -----------------------------------------------------
    //
    // WHAT WAS HERE, and why none of it survived. Two event-driven writes to the body's SCALE and
    // nothing else. TriggerJumpStretch snapped Scale to (1.14, 0.84, 1.14) and kicked the spring at
    // +10/s, which the spring (zeta = 0.42) threw to a MEASURED Y of 1.412 at t = 0.121 s — the body
    // 41% taller than itself, a fifth of a second after take-off — while X and Z dipped to 0.878 at
    // t = 0.150 s. TriggerLandSquash then snapped Y to 0.707 and X/Z to 1.244 on an ordinary jump
    // (the arc lands at 9.77 m/s, intensity 0.698), and rang back through 1.066. The take-off ring
    // does not even finish before the landing fires: it settles to within 1% at t = 0.808 s against
    // an airtime of 0.711 s.
    //
    // Between those two events the body did NOTHING. The legs folded to rest (see _airborne), the
    // gait weight went to zero, and what remained was a rigid figure translating along a parabola
    // with a rubber-ball scale laid over it. That is the whole of Talon's "arcade jump", and it is
    // the same shape MOVE-1 found in the old gait: an exaggeration standing in for a system that
    // was not there. The exaggeration was authored for a body that did not really run; the body
    // runs now, and against it the rubber reads as a cartoon effect rather than as weight.
    //
    // WHAT IS HERE NOW. The jump has a POSE, and every part of it is derived from two things the
    // wire already carries on every peer — Grounded and Velocity.Y (NetCodec bytes 26..30). So a
    // teammate's jump is animated on every client, which the two triggers never managed: they were
    // fired from PlayStepCosmetics, which a RemoteProxy does not run, so until today a remote
    // player's jump had no body reaction at all.
    //
    // Amplitudes are fractions of the LEG (LocomotionProfile.MaxLegSwingRad = 0.675 rad, and
    // _legLengthM), not typed angles, so they scale with whatever body the cast lands with — the
    // discipline MOVE-1 set. Nothing here touches velocity: see the class doc, and JUMP-1's report.

    /// <summary>How far the TRAIL leg sweeps BACK at the instant of take-off, as a fraction of the
    /// leg's own maximum swing (0.55 → 0.371 rad, 21 deg). This is the push-off: the leg finishes
    /// driving down and behind the body before it tucks. Without it the tuck eases in from rest and
    /// the take-off reads as the legs simply going limp.
    ///
    /// <para><b>TRAIL leg only since RIG-1.</b> It used to be both legs, which is why the take-off
    /// read as a symmetric limp rather than as a launch — see
    /// <see cref="LaunchSnapFraction"/>.</para></summary>
    private const float TakeoffKickFraction = 0.55f;

    // --- The launch snap (RIG-1, 2026-08-17) ------------------------------------------------------
    //
    // Talon, on being told the pre-takeoff crouch was the one expensive item (it needs AvatarMotor to
    // hold the impulse back, which adds input latency to a jump he has already signed off):
    //
    //   "I don't want to put something into the development that is 'expensive' by default... I take
    //    a lot of inspiration from Super Mario 64 on the N64 and how this player character informs
    //    the player they have jumped, AFTER they have jumped, is that one leg shoots up into the air
    //    as a clearly 'launching' action motion."
    //
    // So the crouch is OUT and the launch read is delivered AFTER take-off, on the take-off edge that
    // already exists. Cost: one latched bool and a sign flip inside TakeoffKickSec's envelope. No new
    // state machine, no new signal, no wire byte, no AvatarMotor change, and — the whole point — no
    // latency, because nothing is held back.

    /// <summary>How far the LEAD leg's thigh drives FORWARD and up through the take-off window, as a
    /// fraction of the leg's maximum swing (0.90 → 0.608 rad, 35 deg). Deliberately near the
    /// anatomical limit and deliberately the opposite sign to the trail leg's push-off: the two legs
    /// pointing opposite ways at the launch instant is the entire read. A symmetric take-off is what
    /// shipped and it is what Talon could not see.</summary>
    private const float LaunchSnapFraction = 0.90f;

    /// <summary>How far the LEAD leg's foot target pulls in toward its own hip at the peak of the
    /// snap, as a fraction of leg length. 0.38 — a deep fold (the knee comes back about 103 degrees
    /// on the greybox's equal 0.22 m segments), which is what makes a raised thigh read as a leg
    /// shooting up rather than as a leg being pointed. <b>This is the term that needs the knee</b>:
    /// on a rig without one it has nowhere to go.</summary>
    private const float LaunchSnapKneeFold = 0.38f;

    /// <summary>How far both feet pull in toward their hips across the apex tuck, as a fraction of
    /// leg length. 0.18 — knees up, and the reason <see cref="JumpTuckLift"/> can stay small: the
    /// tuck is now mostly a fold rather than a hip node being winched upward.</summary>
    private const float JumpTuckKneeFold = 0.18f;

    /// <summary>How far both feet pull in at full landing impact, as a fraction of leg length. 0.10 —
    /// which on the greybox's leg is a 4.4 cm hip drop and about 43 degrees of knee. <b>This is what
    /// replaced <c>LandCompressY</c></b>: the hip drops because the knees bent, not because the mesh
    /// was squashed. See <see cref="CrouchDropM"/> for how the feet stay on the floor while it
    /// happens.</summary>
    private const float LandAbsorbKneeFold = 0.10f;

    /// <summary>How far each elbow folds through the apex tuck, as a fraction of arm length. Small —
    /// 0.10 — because the arms' airborne read is carried by <see cref="JumpArmRad"/>'s rotation about
    /// the shoulder and the elbow is there to stop the arm reading as a plank, not to take over.
    /// One beat behind the legs, on the same drive.</summary>
    private const float JumpArmElbowFold = 0.10f;

    /// <summary>Hard cap on how far any limb may be folded, as a fraction of its own length. 0.50 —
    /// at which point the tip has come half way to its own root and the joint is at about 120
    /// degrees. ADDITIVE fold terms can otherwise sum past anything sensible (a landing out of a
    /// launch snap stacks two of them), and a limb folded past this reads as broken rather than as
    /// bent. <see cref="LimbIk"/> would clamp it safely regardless; this clamps it <i>legibly</i>.</summary>
    private const float MaxLimbFold = 0.50f;

    /// <summary>The same constant, readable from a test — exposed as a derived property rather than
    /// by widening the field, exactly as <see cref="WaistPitchShareForTools"/> is, so the field stays
    /// the single writer. MOVE-5f asserts that the coil, the launch snap and the apex tuck really do
    /// stack past this cap and that the clamp is what bounds them; a test that retyped 0.50 would go
    /// quiet on the one day that mattered.</summary>
    public static float MaxLimbFoldForTools => MaxLimbFold;

    /// <summary>How much of the body's total forward/back pitch is expressed at the WAIST rather than
    /// at the hip. 0.35 — a single tunable, exposed so Talon can dial it to zero after playing
    /// without touching a line of structure.
    ///
    /// <para><b>The total is clamped BEFORE it is distributed</b>, never after: <c>MaxBodyTilt</c> is
    /// the floor-clamp invariant (<c>SandboxSelfTest.phys_fall_mesh_clamped</c> pins it) and a pose
    /// term applied outside the clamp is a pose term that can breach it. <see cref="BodyTiltX"/>
    /// therefore reports the clamped TOTAL, so that test still measures the quantity it always
    /// measured.</para>
    ///
    /// <para><b>This is the one place RIG-1 changes ratified feel, and it does so visibly.</b> Two
    /// joints in a chain do not put the crown where one does, even at an identical total angle: at
    /// full tilt the waist's share drops the crown about 1.4 cm further than the hip alone would.
    /// Talon has been told and accepted it.</para></summary>
    private const float WaistPitchShare = 0.35f;

    /// <summary>The same constant, readable from a lab. ANIM-M1's flat-seam bend budget has to state
    /// the angle the SHIPPED layer already reaches at this joint, and a lab that retyped 0.35 would
    /// go quiet on the one day that mattered — the day this number moved. Exposed as a derived
    /// property rather than by widening the field, so the field stays the single writer.</summary>
    public static float WaistPitchShareForTools => WaistPitchShare;

    /// <summary>How long the push-off kick lasts, seconds. 0.16 — a shade under the 0.167 s
    /// <see cref="AirborneFoldRate"/> takes to fold the gait out, so the kick is still expressing
    /// while the gait is leaving and the two hand over rather than overlapping at full strength.
    /// Decays linearly for <c>anticipationNow</c>'s reason: a push-off has an end.
    ///
    /// <para><b>MOVE-4d: a live knob.</b> Widened to <c>public</c> per spec §2.2 and reading
    /// <see cref="MotorTuning.Current"/> — the same conversion MOVE-4b made for
    /// <c>AvatarMotor</c>'s twenty-one. Before this it was a <c>const</c> that
    /// <see cref="MotorTuning"/> carried a field for and nothing read, which made its panel slider
    /// a knob that changed the world not at all while the printed block reported it had moved. A
    /// slider that appears to work is worse than no slider.</para></summary>
    public static float TakeoffKickSec => MotorTuning.Current.TakeoffKickSec;

    /// <summary>Knees up on the way up: how far both legs swing FORWARD at full upward drive, as a
    /// fraction of the leg's maximum swing (0.62 → 0.419 rad, 24 deg). Paired with
    /// <see cref="JumpTuckLift"/> — a hip angle alone is a leg thrown forward, and it is the lift
    /// that turns that into a tuck.</summary>
    private const float JumpTuckFraction = 0.62f;

    /// <summary>How far the tucked leg's hip node RISES, as a fraction of leg length. 0.16 — the
    /// same channel the gait's swing-phase lift uses, so it can never open a gap under the torso
    /// (the lift is a rise, never a drop).</summary>
    private const float JumpTuckLift = 0.16f;

    /// <summary><b>How far the ankle may lift the toe toward the shin, radians</b> (ANIM-M2b).
    /// 45 degrees, which comfortably covers the ratified gait: the leg reaches 38.74 degrees at the
    /// stance extremes, so the levelling term never saturates during a gait and only stops in the
    /// deepest part of a knock-out — which is where a foot at its stop is the RIGHT read. Past the
    /// limit the foot simply rides the leg.
    /// <para><b>This clamp bounds the levelling, and since W7-3 the levelling is no longer the whole
    /// of the ankle</b> — <see cref="AnkleLevelFraction"/> scales what comes out of here, so "the
    /// sole stays flat through the whole of stance" (which this note used to claim, and which was
    /// Talon's note 11) is no longer true and is no longer wanted.</para></summary>
    private const float AnkleDorsiLimitRad = 0.7854f;

    /// <summary><b>How far the ankle may point the toe away from the shin, radians</b> (ANIM-M2b).
    /// Deliberately larger than <see cref="AnkleDorsiLimitRad"/> — an ankle plantar-flexes a great
    /// deal further than it dorsiflexes, and this is the direction the trailing leg needs at the
    /// back of a stride and on a jump launch.</summary>
    private const float AnklePlantarLimitRad = 1.0472f;

    /// <summary>
    /// <b>How much of the ankle's levelling is actually applied</b> (W7-3, 2026-08-30, Talon's
    /// playtest note 11). The remainder is given back to the leg, so the foot ROLLS through the
    /// stride instead of being held permanently parallel to the ground.
    ///
    /// <para><b>This exists because the levelling was doing its job perfectly and its job was the
    /// bug.</b> Talon, having played the WAVE-5 build: <i>"Please address the player character's
    /// feet. They don't move along with the body, they stay flat to the ground."</i> That is not an
    /// approximation of the defect, it is a description of
    /// <see cref="LimbIk.LevelAnkle"/>'s contract: it returns exactly <c>-(hip + knee)</c>, clamped,
    /// which cancels the entire leg and pins the sole in the rig's rest plane on every frame the
    /// clamps are not saturated. At the ratified stance extremes the leg reaches 38.74 degrees and
    /// both clamps are 45 degrees or more, so they never saturate during a gait and the sole is
    /// world-flat for the whole cycle. The measured before-capture
    /// (<c>docs/qa/W7-3/before/gait/03-mid-jog-close.png</c>) shows both feet as horizontal tabs on
    /// steeply angled shins — a paddle at the end of a stick.</para>
    ///
    /// <para><b>Why a fraction rather than a phase-driven roll, which was the obvious alternative
    /// and is wrong here.</b> A real cycle rolls heel-strike → flat → toe-off, and those extremes
    /// happen at the swing/stance BOUNDARIES. <c>LocomotionProfile.FootLiftAt</c> is a
    /// <c>sin(s·pi)</c> arch that is zero at both boundaries and peaks mid-swing — i.e. it is at its
    /// maximum exactly where the leg is most vertical and the foot has least to say, and zero
    /// exactly where the foot has most. Driving the roll off lift would therefore move the ankle
    /// where nobody can see it and leave it flat where Talon is looking. Scaling the levelling
    /// instead makes the roll a function of the LEG ANGLE, which is the quantity that actually goes
    /// to its extremes at toe-off and heel-strike — the correct anatomy, from the term that was
    /// already there, with no new phase input and nothing that can fall out of phase with a
    /// time-warped clip.</para>
    ///
    /// <para><b>0.55 keeps the majority of the levelling.</b> At the stance extreme the sole ends up
    /// pitched about 0.45 of (hip + knee) — heel up and toe down behind the body, toe up and heel
    /// down in front — which is a walk cycle's read rather than a stylisation of one. It is
    /// deliberately NOT 0: a foot that fully rides the leg is a leg with a lump on the end, and the
    /// planted-foot read that ANIM-M2b bought would be thrown away to fix a swing-phase complaint.
    /// </para>
    ///
    /// <para><b>The cost, measured rather than waved at.</b> The boxkid's foot reaches 0.055 m
    /// forward of the ankle (<c>build_boxkid.py</c>'s <c>SHIN_HALF_Z</c>, which the foot inherits so
    /// it stays inside the classic greybox's leg outline). At a 20 degree roll its leading bottom
    /// corner passes about 19 mm through the floor plane at the stride extremes. That is accepted,
    /// not overlooked: <see cref="CrouchDropM"/>'s own note already records the silhouette 27 mm
    /// below the floor in a slide for the opposite reason (a sole that CANNOT finish levelling), so
    /// this is the same order of penetration as a case that has already shipped, on a 1.20 m body,
    /// on flat ground. Lifting the ankle to compensate would put the leg on stilts through stance,
    /// which is a worse artifact and a visible one.</para>
    ///
    /// <para><b>Presentation only</b> (canon fact 4, the parity law). It moves one rotation channel
    /// on two meshes. No collision, no server state, nothing on the wire — two peers disagreeing
    /// about it would still agree about every outcome.</para>
    /// </summary>
    private const float AnkleLevelFraction = 0.55f;

    /// <summary>Reaching for the ground on the way down: how far the legs swing forward at full
    /// downward drive, as a fraction of maximum swing (0.30 → 0.203 rad, 12 deg), with zero lift.
    /// Deliberately much smaller than the tuck — the legs are EXTENDING to meet the floor, and this
    /// asymmetry between rising and falling is most of what separates a jump from an arc.</summary>
    private const float JumpReachFraction = 0.30f;

    /// <summary>The other leg's share of the airborne angles. 0.62 — a scissor, not a mannequin:
    /// two legs at identical angles is the read <see cref="SkidTrailLegFraction"/> exists to avoid,
    /// and it is worse in the air where nothing else is moving.</summary>
    private const float JumpTrailLegFraction = 0.62f;

    /// <summary>Backward pitch at full upward drive, radians (~6 deg). The chest opening through
    /// the rise. Additive and INSIDE the MaxBodyTilt clamp, for <see cref="SkidLeanRad"/>'s
    /// reason.</summary>
    private const float JumpRiseTiltRad = 0.10f;

    /// <summary>Forward pitch at full downward drive, radians (~9 deg) — bracing, looking at where
    /// the feet are about to be. Larger than the rise tilt because a body that is falling is doing
    /// something about it; a body that is rising has already done it.</summary>
    private const float JumpFallTiltRad = 0.16f;

    /// <summary>The arms' airborne pitch about their shoulders at full drive, radians (~31 deg).
    /// ROTATIONAL, never an offset — MOVE-1's second amendment, note C: an offset on one axis
    /// detaches a nub arm from the shoulder it hangs off, and vanishes from a head-on camera.</summary>
    private const float JumpArmRad = 0.55f;

    /// <summary>Landing absorb: how far the legs splay forward at full impact, as a fraction of
    /// maximum swing (0.34 → 0.230 rad, 13 deg). A third of the skid's plant — catching your weight
    /// is not braking with it.</summary>
    private const float LandPlantFraction = 0.34f;

    /// <summary>How long the absorb takes to unwind, seconds. 0.26 — long enough to read as the
    /// body taking the weight, short enough that a player who lands running is running again before
    /// they notice. Decays linearly; the recovery IS the settle.</summary>
    private const float LandAbsorbSec = 0.26f;

    /// <summary>Forward fold at the deepest point of the absorb, radians (~10 deg). Inside the
    /// MaxBodyTilt clamp with everything else.</summary>
    private const float LandTiltRad = 0.18f;

    // LandCompressY LIVED HERE and was DELETED by RIG-1. It was a 0.12 vertical body compression at
    // full impact, applied as a multiplier on the squash spring's result, and its own comment named
    // its own replacement: "it survives because knee flex is a real thing a landing body does and
    // this rig has no knee to do it with." This rig has a knee. The absorb is now
    // LandAbsorbKneeFold — a real fold of both knees plus the hip drop that geometrically follows
    // from it — and NO vertical scale term survives the jump at all. Do not reintroduce it: a
    // squashed mesh and a bent knee are two different readings of the same landing, and a body that
    // does both is a body doing it twice.

    /// <summary>Fall speed below which a landing gets no absorb at all, m/s. 2.5 — about a 0.28 m
    /// drop. <b>Shared with SandboxAvatar's ActorEvent.Land gate</b> so the pose and the sound agree
    /// on what counts as a landing; it was two copies of the same literal before JUMP-1.
    ///
    /// <para><b>MOVE-4d: a live knob</b> — see <see cref="TakeoffKickSec"/> for the argument. The
    /// sharing still holds: <c>SandboxAvatar</c> reads this symbol, so the pose and the sound move
    /// together when the slider does.</para></summary>
    public static float LandMinFallMps => MotorTuning.Current.LandMinFallMps;

    /// <summary>Fall speed at which the absorb is at full amplitude, m/s. 14 — an ordinary jump off
    /// flat ground lands at 9.77 (v=8.4, g=22 up / 29.7 down), so a plain jump absorbs at ~0.70 and
    /// a real drop still has somewhere to go. Shared with SandboxAvatar for
    /// <see cref="LandMinFallMps"/>'s reason.
    ///
    /// <para><b>MOVE-4d: a live knob.</b> <c>MotorTuning.Validate</c> holds the ordering constraint
    /// that keeps this above <c>max(3.0, LandMinFallMps + 0.5)</c>, so no slider and no hand-edited
    /// file can collapse the two-constant gate into one (spec §2.3).</para></summary>
    public static float LandFullFallMps => MotorTuning.Current.LandFullFallMps;

    /// <summary>Floor on the absorb once one has been triggered at all. 0.25 — a landing that
    /// qualified must be visible, or the gate reads as a bug on the frames just past it.
    /// <para><b>Not a knob</b>, deliberately (spec §5.2): it is the gate's own legibility floor, and
    /// a slider on it would let the panel author the exact bug the constant exists to prevent.</para></summary>
    public const float LandMinIntensity = 0.25f;

    /// <summary>
    /// <b>How hard a landing was, 0.25–1 — the ONE curve, for every channel</b> (MOVE-4c, spec
    /// §5.2). The knee absorb, the <c>ActorEvent.Land</c> sound fan-out and the camera dip all read
    /// this; before MOVE-4c the first two were two different clamps of the same quantity off the
    /// same gate — <c>[LandMinIntensity, 1]</c> here and <c>[0, 1]</c> in <c>SandboxAvatar</c>.
    ///
    /// <para><b>The floor is 0.25 and the argument is the constant's own, generalised.</b>
    /// <see cref="LandMinIntensity"/> exists because "a landing that qualified must be visible, or
    /// the gate reads as a bug on the frames just past it" — and that reasoning is about the GATE,
    /// not about the pose. It is equally true of a landing that qualified but is inaudible.
    /// Unifying downward instead (dropping the pose to a 0 floor) is the option that is actually
    /// forbidden: it would change how a landing LOOKS at the shipped tuning, and the whole of
    /// MOVE-4 ships as a no-op against the arc Talon approved.</para>
    ///
    /// <para><b>What the unification actually moves, measured rather than waved at.</b> Only fall
    /// speeds in <c>[2.5, 3.5) m/s</c> are affected — above 3.5, <c>fall / 14</c> is already past
    /// 0.25. In that band the intensity rises by at most <c>0.25 − 2.5/14 = 0.0714</c>. The avatar
    /// footstep profile's three <c>Land</c> responses are the only readers: <c>land_pad</c> scales volume by
    /// <c>IntensityVolumeBoostDb = 4</c>, so it gets <b>at most 0.286 dB louder</b> — well under the
    /// ~1 dB a listener can hear; <c>land_squeak</c> has no intensity boost and is bit-identical;
    /// and <c>land_dust</c>'s <c>MinIntensity = 0.428</c> corresponds to a 5.99 m/s fall, far above
    /// the 3.5 m/s where the floor stops biting, so its gating is bit-identical too. <b>No response
    /// changes whether it fires. Nothing visual changes at all.</b></para>
    /// </summary>
    /// <param name="fallMps">The deepest fall speed of the flight — the same quantity the gate
    /// <see cref="LandMinFallMps"/> is tested against. Callers own the gate; this owns the curve.</param>
    public static float LandIntensityFor(float fallMps)
        => Mathf.Clamp(fallMps / LandFullFallMps, LandMinIntensity, 1f);

    /// <summary>Upward speed a take-off must have before the push-off kick fires, as a fraction of
    /// <c>AvatarMotor.JumpVelocity</c>. 0.4 — so walking off a ledge (Y at or below zero) leaves the
    /// ground without kicking off it, which is the honest read of what happened.
    ///
    /// <para><b>MOVE-4d: a live knob</b>, widened to <c>public</c> per spec §2.2 — see
    /// <see cref="TakeoffKickSec"/>.</para></summary>
    public static float TakeoffKickMinDriveFraction => MotorTuning.Current.TakeoffKickMinDriveFraction;

    // --- THE ANTICIPATION COIL (MOVE-5f) ---------------------------------------------------------
    //
    // The last unbuilt item in Talon's lab brief. The numbers are AnticipationCoil's — pure, and
    // testable without a viewport — and the three knobs below are the two of the five rows this
    // class reads plus the mode both readers take from one place. See AnticipationCoil for why the
    // coil is played INTO an already-rising body rather than before the launch, and why that is the
    // only reading of "hold longer -> deeper coil" that does not require knowing the future.

    /// <summary>Knob 53, read through <c>AvatarMotor</c> so the pose and the gravity term cannot
    /// disagree about which approach is running. Only <see cref="AnticipationCoil.RealCoil"/> makes
    /// this class do anything at all; mode 2 is entirely <c>AvatarMotor.GravityFor</c>'s and puts no
    /// pose on the body, which is the difference between the two candidates.</summary>
    public static int AnticipationMode => AvatarMotor.AnticipationMode;

    /// <summary>Knob 54 — the brief's own "coil depth". Multiplies every amplitude in
    /// <see cref="AnticipationCoil"/>; at 0 mode 1 is an exact no-op.</summary>
    public static float AnticipationDepth => MotorTuning.Current.AnticipationDepth;

    /// <summary>Knob 55 — the brief's "coil depth vs. hold duration", as the seconds of RISE at
    /// which the coil reaches <see cref="AnticipationDepth"/>.</summary>
    public static float AnticipationCoilSec => MotorTuning.Current.AnticipationCoilSec;

    /// <summary>Hard cap on total body tilt; keeps every blob above the floor plane
    /// (the tallest part is ~0.6 m from the body origin, so even at max tilt nothing
    /// can reach y=0). Tests assert this invariant through falls and landings.</summary>
    public const float MaxBodyTilt = 0.60f;

    private Vector3 _scale = Vector3.One;
    private Vector3 _scaleVel = Vector3.Zero;

    /// <summary>The left foot's position through its own cycle, 0..1. INTEGRATED, never recomputed
    /// from a clock: the cadence changes every frame as the body speeds up, and a phase derived from
    /// <c>time × cadence</c> would jump backwards the instant the cadence dropped — a foot
    /// teleporting mid-stride. The right foot is this plus a half cycle.</summary>
    private float _gaitPhase;

    private float _gaitAmp;        // eased 0..1; scales the cosmetic half of the gait, never the reach
    private float _runBlend;       // eased 0..1 sprint weight, kept for the pose/HUD read
    private Gear _gear = Gear.Idle;
    private int _lastPlantIndex;   // which foot's stance was last entered, for the SFX edge
    private bool _footPlanted;     // set when a foot plants; consumed by the SFX layer to squeak in sync
    private float _leanRad;        // low-passed acceleration lean
    private float _turnRollRad;    // low-passed lateral-acceleration roll
    private float _prevForwardSpeed;
    private float _prevLateralSpeed;
    private float _prevAccelFwd;   // for the onset detector
    private float _anticipation;   // signed radians, decaying
    private float _anticipationLeft; // seconds remaining
    private float _airborne;       // eased 0..1; folds the legs while off the floor

    // --- The jump (JUMP-1). All derived; nothing here is set by an event. ---
    /// <summary>Signed airborne drive, -1..+1: <c>velocity.Y / AvatarMotor.JumpVelocity</c> while
    /// off the floor, 0 on it. +1 the tick a full-power jump leaves the ground, 0 at the apex,
    /// down to -1 through the fall. This one number is what makes the rise and the fall two
    /// different poses instead of two halves of one arc.</summary>
    private float _airDrive;
    private bool _wasOnFloor = true;   // the take-off / touchdown edge detector
    private float _maxAirFallMps;      // deepest fall speed seen this flight, consumed on touchdown
    private float _takeoffLeft;        // seconds of push-off kick remaining
    private float _landPeak;           // this landing's absorb amplitude, 0..1
    private float _landLeft;           // seconds of absorb remaining

    /// <summary>Seconds this flight has spent RISING (MOVE-5f). Zeroed on the take-off edge — which
    /// is every flight's first frame, so a touchdown reset would be a second writer saying the same
    /// thing — and it stops accumulating the moment the body stops going up, which is the moment
    /// the player let go, because letting go is what triples gravity. <b>This is the entire channel
    /// by which "hold longer" reaches the pose</b>, and it is a fact the body already has rather
    /// than a button state it would have to be told about: <see cref="Animate"/> is handed a
    /// velocity, and every peer animating a remote proxy is handed the same one.</summary>
    private float _coilRiseSec;

    /// <summary>The coil weight the body is posing this frame, 0..1 — <c>AnticipationCoil.Depth</c>
    /// scaled by knob 54 while rising, unwinding linearly at
    /// <c>AnticipationCoil.ReleaseRate</c> once the rise ends. Presentation smoothing over a fact
    /// the motor already committed to, exactly the kind <c>_skidBlend</c> and <c>_airborne</c>
    /// already are; nothing outside this class reads it and nothing that decides consults it.</summary>
    private float _coilBlend;

    /// <summary>The coil weight this body is posing, for the capture harness and for tests — "the
    /// coil deepened with the hold" is a measurement before it is a photograph. Zero at every frame
    /// of every jump while <see cref="AnticipationMode"/> is not
    /// <see cref="AnticipationCoil.RealCoil"/>.</summary>
    public float CoilReadForTools => _coilBlend;

    /// <summary>Seconds of rise accumulated this flight, for the same reason
    /// <see cref="CoilReadForTools"/> exists: it is the input the relationship is stated over, so a
    /// test can assert the relationship rather than the pose it happened to produce.</summary>
    public float CoilRiseSecForTools => _coilRiseSec;

    /// <summary>Which leg shoots up on the launch snap. <b>Latched on the take-off edge and held for
    /// the whole flight</b>, so it cannot flicker mid-air — a lead leg re-derived per frame from
    /// <c>_gaitPhase</c> would swap sides at the phase wrap and read as the legs arguing. A standing
    /// jump gets whichever leg the idle phase happened to park on, which is fine and reads as a
    /// natural lead foot. The lead is the leg that was NOT taking the weight: the planted one is what
    /// pushes off.</summary>
    private bool _leadLegLeft;

    /// <summary>The clamped TOTAL body pitch this frame, radians — before it is split between the hip
    /// and the waist. Stored rather than read back off a node because it is now spread across two,
    /// and <see cref="BodyTiltX"/>'s contract is the total.</summary>
    private float _bodyTiltX;

    private float _blinkTimer = 2f;
    private float _blinking;
    /// <summary>Eased weight of the HANDLE carry — one hand on a long-handled tool (CARRY-1).</summary>
    private float _handleBlend;

    /// <summary>Eased weight of the ARMFUL carry — both arms around a bulky load (CARRY-1).</summary>
    private float _armfulBlend;

    private CarryPose _carry = CarryPose.None;
    private float _aimBlend;
    private bool _aiming;
    private float _swingBlend;
    private bool _swinging;
    private float _swingDrive;
    private float _skidBlend;
    private bool _skidding;
    private float _animTime;
    private float _fidgetTimer = 4f;
    private float _fidgeting;

    /// <summary>Rest-pose bounds of this character's <b>body</b>, in the rig's own local space
    /// (y = 0 is the ground it stands on), <b>measured off the instanced model</b> right after
    /// it is built — not declared anywhere, and not a number anyone typed.
    ///
    /// <para>Every <see cref="MeshInstance3D"/> under this rig is unioned EXCEPT two kinds of
    /// node. The <see cref="_sprout"/> subtree, because it is springy secondary motion that lags
    /// and sways behind the body: a collider drawn round it would give a sprouted body a head of
    /// air (the original rig's sprout added 0.30 m over a 0.93 m body), and a cosmetic that lags must never
    /// decide where a solid is. And anything already queued for deletion — the harvest path
    /// frees the leftover glTF scaffolding with <c>QueueFree</c>, which is DEFERRED, so on the
    /// frame the model is built those discarded parts are still in the tree and would silently
    /// widen the measurement for exactly one frame.</para>
    ///
    /// <para>Consumers derive from this through <see cref="AvatarProportions"/> rather than
    /// reading it raw; see that type for what each dimension turns into and why.</para></summary>
    public Aabb BodyBounds { get; private set; }

    /// <summary>Rest-pose centre of this character's eyes, rig-local, or null when the model has
    /// no eye geometry at all. Read off the very eye nodes the rig already resolved for the
    /// blink, so this adds no new part-name knowledge to engine code.</summary>
    public Vector3? EyeCentre { get; private set; }

    /// <summary>Render layer every mesh under <see cref="_body"/> lives on EXCLUSIVELY (never
    /// layer 1). P4b (2026-08-08 playtest, docs/superpowers/plans/2026-08-08-four-packet-dispatch.md):
    /// SandboxCamera's spring arm excludes its own target's collider, so nothing stops the arm
    /// contracting past the character's own silhouette when a close obstruction sits near the
    /// pivot — the camera then renders from inside the avatar's own head. A dedicated layer lets
    /// ONLY that avatar's own camera drop out of it for the frames the arm is that short (see
    /// SandboxCamera._PhysicsProcess), while every other camera's default CullMask (all layers)
    /// keeps rendering the body normally — a local fix, not a network-visible one.</summary>
    public const int OwnBodyRenderLayer = 20;

    /// <summary>Current TOTAL body tilt (X), radians — exposed for the floor-clamp test invariant.
    ///
    /// <para><b>The total, not one node's share</b> (RIG-1). The pitch is now distributed between the
    /// hip pivot and the waist (see <see cref="WaistPitchShare"/>), so reading it back off
    /// <c>_pose.Rotation.X</c> would report 65% of the body's actual pitch and
    /// <c>SandboxSelfTest.phys_fall_mesh_clamped</c> would silently stop measuring the invariant it
    /// exists for. This is the quantity <c>MaxBodyTilt</c> clamps, and it is clamped before it is
    /// split.</para></summary>
    public float BodyTiltX => _bodyTiltX;

    /// <summary>The waist's own share of <see cref="BodyTiltX"/> this frame, radians. Zero on any rig
    /// that did not declare a waist (see <see cref="HasWaist"/>). Exposed so a capture harness can
    /// state the bend it photographed rather than assert it.</summary>
    public float WaistPitchRad { get; private set; }

    /// <summary>
    /// <b>How far the whole rig has sunk on its own knees this frame, metres — always &gt;= 0.</b>
    ///
    /// <para><b>Why it is a node of its own, and not part of the pose.</b> A landing absorb is a real
    /// knee flex, and a real knee flex lowers the hips: geometry gives no other answer once the feet
    /// are on the floor. But <see cref="BodyOffsetY"/> — the hip pivot's lift — is pinned
    /// <c>&gt;= 0</c> by <c>SandboxSelfTest.phys_fall_mesh_clamped</c>, because it is a POSE term and
    /// a pose that lowers the body is how a mesh ends up through the floor. Both are right. So the
    /// crouch is a separate, Y-only node that carries the whole rig — pose, mounts and both leg roots
    /// together — down by exactly the amount the legs were shortened, which is what keeps the feet on
    /// the ground while the hips drop. The pose keeps meaning what it meant, and the drop is a
    /// quantity with one writer and one name.</para>
    ///
    /// <para><b>Bounded by <c>MaxLimbFold × legLength</c></b> — 27.4 cm on the greybox's leg, which
    /// this lineage measures at <b>0.548 m</b> (in engine, 2026-08-27, <c>-Verbs</c> capture; the
    /// 0.44 m several comments in this file work their examples from is stale). It was
    /// <c>LandAbsorbKneeFold × legLength</c> until MOVE-5c put the crouch VERBS on this same term,
    /// which is where they belong: a slide and a landing absorb are both a knee bent with the feet
    /// planted, and there is one number for how far that lowers the hips.</para>
    ///
    /// <para>The deepest verb is the slide at <c>VerbPose.SlideKneeFold</c> = 0.46, <b>a measured
    /// 25.2 cm drop</b>; the greybox's belly sits at y 0.42, so 16.8 cm of torso clearance remains.
    /// <b>The foot is the tighter constraint and it is not this term's:</b> the same capture measured
    /// the silhouette 27 mm below the floor plane, because <c>LimbIk.LevelAnkle</c> pins at
    /// <c>AnkleDorsiLimitRad</c> (45°, measured pinned) and a sole that cannot finish levelling
    /// points its toe down. A real deep squat lifts the heel instead; the solver has no term for
    /// that. A body with a longer leg under a lower belly is the case to re-measure — the lab prints
    /// both the drop and the live silhouette's floor clearance per shot.</para>
    /// </summary>
    public float CrouchDropM { get; private set; }

    /// <summary>How far the named knee is bent this frame, radians — 0 straight, positive is bent.
    /// Zero on a rig with no shin. Exposed for the capture harness: "the knee is visible doing the
    /// work" is a measurement before it is a photograph.</summary>
    public float KneeBendRad(bool left) => left ? _kneeBendL : _kneeBendR;

    /// <summary>The ankle angle written this frame, radians, relative to the shin — the amount by
    /// which the foot is countering the leg to keep its sole flat (ANIM-M2b). Zero on every rig with
    /// no foot node, which is every row but the greybox.
    ///
    /// <para><b>A READOUT, and it is the honest one to assert on.</b> "The foot planted" is a claim
    /// about an ANGLE, and a headed capture can only ever show it at the frames somebody chose to
    /// photograph; this is the same claim, every frame, in a number. <c>hip + knee + this</c> is the
    /// sole's own pitch.</para>
    ///
    /// <para><b>That pitch is no longer zero off-clamp, and the change is deliberate</b> (W7-3,
    /// Talon's note 11). This value is now the levelling scaled by
    /// <see cref="AnkleLevelFraction"/>, so the sole's pitch is
    /// <c>(1 - AnkleLevelFraction) x (hip + knee)</c> everywhere the clamps are not saturated —
    /// the foot rolls with the stride instead of being held parallel to the ground. The previous
    /// sentence here ("it is zero wherever the ankle is not at its clamp") described the behaviour
    /// Talon asked to have removed, and is corrected rather than deleted so a reader of an older
    /// assertion knows why it moved.</para></summary>
    public float AnkleLevelRad(bool left) => left ? _ankleLevelL : _ankleLevelR;

    /// <summary>The hip pitch actually driven into the leg solver this frame, radians, positive
    /// forward. A READOUT of whichever source owns the channel — the derived gait or, under the clip
    /// layer, the authored clip — so a test can ask "did the body move" without having to know which
    /// one is driving. Nothing branches on it.</summary>
    public float HipPitchRad(bool left) => left ? _hipPitchL : _hipPitchR;

    /// <summary>The shoulder pitch actually driven into the arm solver this frame, radians. Same
    /// shape and same purpose as <see cref="HipPitchRad"/>. Distinct from
    /// <see cref="RightArmPitchRad"/>, which reports the SWING's own arc and nothing else.</summary>
    public float ShoulderPitchRad(bool left) => left ? _shoulderPitchL : _shoulderPitchR;

    private float _hipPitchL;
    private float _hipPitchR;
    private float _shoulderPitchL;
    private float _shoulderPitchR;

    /// <summary>How far the named elbow is bent this frame, radians. Zero on a rig with no
    /// forearm.</summary>
    public float ElbowBendRad(bool left) => left ? _elbowBendL : _elbowBendR;

    /// <summary>True when this character's rig declared real shin geometry, so the legs are solved
    /// with two bones. False on every <c>.glb</c> on the roster today, which falls back to the
    /// single-segment rotation that shipped — see <see cref="TakeOptionalPart"/>.</summary>
    public bool HasKnees { get; private set; }

    /// <summary>True when this character's rig declared real forearm geometry.</summary>
    public bool HasElbows { get; private set; }

    /// <summary>True when this character's rig declared BOTH a real head and a real lower torso —
    /// i.e. an authored upper/lower split with something above it to lean. Only then is the waist
    /// driven; see <see cref="WaistPitchShare"/>.</summary>
    public bool HasWaist { get; private set; }

    /// <summary>Which leg is the launch snap's lead this flight. Latched on take-off; see
    /// <see cref="_leadLegLeft"/>.</summary>
    public bool LeadLegIsLeft => _leadLegLeft;

    /// <summary>Metres of thigh and shin this rig measured for itself. They sum to
    /// <see cref="LegLengthM"/> by construction — the shin's length IS the knee's rest height above
    /// the ground and the thigh's is what is left — which is the arithmetic that makes the gait
    /// invariance property hold exactly rather than approximately.</summary>
    public float ThighLengthM => _thighLengthM;

    /// <inheritdoc cref="ThighLengthM"/>
    public float ShinLengthM => _legLengthM - _thighLengthM;

    /// <summary>Metres from shoulder to elbow, and elbow to wrist, measured off this rig.</summary>
    public float UpperArmLengthM => _upperArmLengthM;

    /// <inheritdoc cref="UpperArmLengthM"/>
    public float ForearmLengthM => _armLengthM - _upperArmLengthM;

    /// <summary>Total shoulder-to-wrist length, metres, measured off this rig.</summary>
    public float ArmLengthM => _armLengthM;

    /// <summary>
    /// Rest-pose bounds of ONE named harvested part, in the rig's own local space, or null when this
    /// character has no real geometry under that name.
    ///
    /// <para><b>Exposed for the capture lab, and it is the difference between a measurement and a
    /// caption.</b> RIG-1's acceptance criteria are numbers taken off emitted geometry — the hips'
    /// half-X against the legs' outer half-X, the visible leg as a fraction of the crown — and taking
    /// them off the source constants instead would prove only that the constants say what they say.
    /// Reads the same vertex path <see cref="BodyBounds"/> does, so the two cannot disagree.</para>
    /// </summary>
    public Aabb? PartBoundsRigLocal(string name)
    {
        if (FindChild(name, recursive: true, owned: false) is not MeshInstance3D mesh
            || mesh.Mesh == null)
        {
            return null;
        }
        var bounds = new Aabb();
        bool any = false;
        ExpandOverVertices(mesh, GlobalTransform.AffineInverse(), ref bounds, ref any);
        return any ? bounds : null;
    }

    /// <summary>Eased 0..1 sprint weight. Since MOVE-1 it no longer drives cadence or amplitude
    /// (both derive from ground speed now) — it is the sprint READ, for the pose layer and for a
    /// test that wants to know the body knows it is sprinting.</summary>
    public float RunBlend => _runBlend;

    /// <summary><b>Which gear this body is in</b> — the named, readable state. Derived from ground
    /// speed with hysteresis; see <see cref="LocomotionProfile.GearFor"/>.</summary>
    public Gear Gear => _gear;

    /// <summary>Where the left foot is through its own gait cycle, 0..1. <c>[0, duty)</c> is stance.
    /// Exposed so a capture harness can sample a foot at a known point in its stride rather than
    /// guessing.</summary>
    public float GaitPhase => _gaitPhase;

    /// <summary>Metres of hip-to-foot length this rig measured for itself — what every derived gait
    /// dimension is scaled against.</summary>
    public float LegLengthM => _legLengthM;

    /// <summary>Steps per second the gait is currently turning over. Derived from ground speed, and
    /// the number the whole "stuttering step" complaint was about.</summary>
    public float CadenceHz { get; private set; }

    /// <summary>How far in front of (and behind) its hip a planted foot is reaching, metres. This
    /// times the cadence is the ground speed, by construction.</summary>
    public float StanceReachM { get; private set; }

    /// <summary>Fraction of a foot's cycle currently spent planted.</summary>
    public float DutyFactor { get; private set; }

    /// <summary>
    /// <b>World position of one foot's ground contact point.</b> The measurement "planted" actually
    /// means: sampled across consecutive frames while that foot is in stance, its horizontal
    /// movement is the foot slip, and the whole gait rewrite is judged on that number.
    ///
    /// <para>Not simply the node's origin — the node is the <i>hip</i>, and the contact point is down
    /// the leg from it, which is the end that is supposed to be stationary.</para>
    ///
    /// <para><b>Measured through the KNEE when there is one</b> (RIG-1). On a two-bone leg the ankle
    /// is not <c>legLength</c> along the thigh's axis — that is only true while the knee is straight,
    /// which is exactly the case a foot-slip test would never catch going wrong. Walking the real
    /// shin node is the honest answer at any knee angle and is identical to the old arithmetic
    /// whenever the leg is straight, which the ratified gait always keeps it.</para>
    /// </summary>
    public Vector3 FootContactGlobal(bool left)
    {
        Node3D? shin = left ? _shinL : _shinR;
        if (shin != null && IsInstanceValid(shin))
            return shin.GlobalTransform * new Vector3(0f, -ShinLengthM, 0f);

        Node3D foot = left ? _leftThigh : _rightThigh;
        if (foot == null || !IsInstanceValid(foot))
            return Vector3.Zero;
        return foot.GlobalTransform * new Vector3(0f, -_legLengthM, 0f);
    }

    /// <summary>True while the named foot is in its stance (planted) phase — the window over which
    /// <see cref="FootContactGlobal"/> must not move.</summary>
    public bool FootInStance(bool left)
    {
        float p = left ? _gaitPhase : _gaitPhase + 0.5f;
        p -= Mathf.Floor(p);
        return _gaitAmp > 0.95f && p < DutyFactor;
    }

    /// <summary>Current body lift — exposed for the floor-clamp test invariant.</summary>
    public float BodyOffsetY => _pose.Position.Y;

    /// <summary>Current body twist (Y), rig-local — the swing's coil-and-whip plus the idle fidget.
    /// Local rather than global on purpose: the avatar root's own facing yaw would swamp a 31-degree
    /// body twist, and a test comparing the two would be measuring which way the character is
    /// walking.</summary>
    public float BodyYaw => _pose.Rotation.Y;

    /// <summary>Small local-space offset a held item's carry anchor should ride so the carried prop
    /// bobs in sync with the waddle instead of sitting frozen. ~Zero at a stand (a faint breath),
    /// grows with gait speed, always small. Purely cosmetic — SandboxAvatar applies it to the anchor,
    /// and both offline and networked held props inherit it through their existing anchor-follow.</summary>
    public Vector3 CarryBobOffset { get; private set; }

    /// <summary>Eased 0..1 carry-pose weight — the union of both carries, so "am I posed as carrying
    /// something" is one question with one answer. Exposed for the pose test, which predates the
    /// handle/armful split and is about the union.</summary>
    public float CarryWeight => Mathf.Max(_handleBlend, _armfulBlend);

    /// <summary>Eased 0..1 weight of the HANDLE carry — one hand gripping a long-handled tool
    /// (<see cref="CarryPose.Handle"/>). Exposed so a capture harness can state which of the two
    /// carries it photographed rather than assert it.</summary>
    public float HandleCarryWeight => _handleBlend;

    /// <summary>Eased 0..1 weight of the ARMFUL carry — both arms around a bulky load
    /// (<see cref="CarryPose.Armful"/>).</summary>
    public float ArmfulCarryWeight => _armfulBlend;

    /// <summary>Each arm node's position this frame minus its authored rest, metres. <b>Must be
    /// exactly zero</b> — CARRY-1's acceptance criterion 1, and the reason it is a property rather
    /// than a comment: the defect this packet fixed was a pose translating the shoulder joint out of
    /// the torso, and "the shoulder never moves" is only an invariant if something measures it.</summary>
    public Vector3 ArmRestDeviation(bool left) =>
        left ? _leftArm.Position - _leftArmRest : _rightArm.Position - _rightArmRest;

    /// <summary>Where each hand was ASKED to go this frame, relative to its own shoulder, in the
    /// arm's rest-local frame — and how far short the arm fell of it. A two-bone arm cannot reach
    /// past its own length, so a target beyond <see cref="ArmLengthM"/> resolves to a fully extended
    /// arm pointing at it; this reports that shortfall instead of hiding it.</summary>
    public float HandReachShortfallM(bool left) => left ? _handShortfallL : _handShortfallR;

    /// <summary>
    /// The extra local-space offset the HAND takes while swinging — a single vector, applied both to
    /// the arms (where arms exist) and to the carry mount, so the net can never leave the hand that
    /// is swinging it.
    ///
    /// <para><b>One source, two consumers, deliberately.</b> If the arm's sweep and the mount's
    /// sweep were computed separately they would be two numbers for one motion, which is exactly the
    /// fault the carry bob had before ANIM-1 (see <see cref="CarryBobHeight"/>). Zero unless a swing
    /// is being expressed.</para>
    /// </summary>
    public Vector3 SwingHandOffset { get; private set; }

    /// <summary>The swinging arm's pitch about its shoulder this frame, radians — the rotational
    /// half of the swing pose (second amendment, note C). Exposed so a capture harness can state the
    /// pose it photographed rather than assert it.</summary>
    public float RightArmPitchRad { get; private set; }

    /// <summary>How far the feet are turned AGAINST the torso's swing twist this frame, radians.
    /// The hips holding while the shoulders whip; see <see cref="FootCounterTwist"/>.</summary>
    public float FootYawRad { get; private set; }

    /// <summary>Eased 0..1 swing-pose weight. Exposed so a test can prove the pose <b>unwinds</b>
    /// when a swing is abandoned rather than sticking — the failure mode a blend exists to
    /// prevent.</summary>
    public float SwingWeight => _swingBlend;

    /// <summary>
    /// World transform of the posed body, <b>without</b> the squash-and-stretch scale — i.e. exactly
    /// the frame a mounted hand lives in.
    ///
    /// <para><b>Exposed for the Gap 0 positive control</b> (<c>SandboxSelfTest</c>): "the held item
    /// is in the hand" is only measurable against where the body actually put the hand, and that
    /// point is <c>PoseGlobalTransform * Proportions.CarryAnchorRestLocal</c>. Without it, a test can
    /// only compare the anchor against the avatar's root transform, which is precisely the
    /// measurement that stayed green through a year of the anchor ignoring the body's rotation.</para>
    /// </summary>
    public Transform3D PoseGlobalTransform
    {
        get
        {
            EnsurePoseRig();
            return _pose.GlobalTransform;
        }
    }

    /// <summary>Returns true once per animated footfall, so the SFX layer can squeak in
    /// sync with the waddle instead of on distance walked. Consumes the flag.</summary>
    public bool ConsumeFootPlant()
    {
        bool planted = _footPlanted;
        _footPlanted = false;
        return planted;
    }

    /// <summary>The roster key every unknown or invalid choice clamps to — on every peer,
    /// including a malicious or stale client's synced value (see
    /// <see cref="NormalizeAvatarKey"/>). This is a containment target, not a preference; what
    /// a player gets when nobody chose is <see cref="PreferredAvatarKey"/>.</summary>
    public const string DefaultAvatarKey = PrimitiveFallbackAvatarKey;

    /// <summary>How a roster model hands its geometry to the animated rig.</summary>
    private enum Build
    {
        /// <summary>The harvested-part contract: sixteen separately named parts get harvested onto
        /// the rig's own nodes (body, arms, feet, eyes, sprout) and player-tinted. A missing
        /// part is a loud error, because for these models it means a broken export.</summary>
        HarvestedParts,

        /// <summary>The same sixteen-part contract as <see cref="HarvestedParts"/>, except the
        /// parts are built in code by <see cref="GreyboxAvatarBody"/> instead of parsed out of a
        /// <c>.glb</c> — no file, no import, nothing for the asset pipeline to validate. The
        /// harvest, the tint, the rest-pose measurement and every verb in <see cref="Animate"/>
        /// are shared with <c>HarvestedParts</c> and untouched; only the source of the geometry
        /// differs. Parts this body does not have are emitted as explicitly empty meshes, so
        /// absence is declared by the builder rather than discovered (and error-logged) by the
        /// harvest — see that class for why the loud error is right for a model and wrong
        /// here.</summary>
        PrimitiveParts,

        /// <summary>
        /// <b>The imported scene IS the rig</b> (ANIM-M3, executing ANIM-M0 §3.2's option (a)). No
        /// harvest, no reparent, no <c>QueueFree</c> of the model: the <c>.glb</c> ships
        /// <c>Rig / Pose / Body / Waist</c> as authored empties with the legs outside the pose, so
        /// this build caches named references instead of rebuilding the hierarchy out of parts.
        ///
        /// <para><b>Why it is a third mode and not a replacement.</b> The harvested and code-built
        /// rows keep <see cref="HarvestedParts"/> and <see cref="PrimitiveParts"/> untouched, so the
        /// shipped, replicated, test-pinned bodies carry zero risk from this change — the same
        /// containment argument the roster table already makes for keeping families apart.</para>
        ///
        /// <para><b>What it buys, and it is the entire point of the migration.</b> The rest
        /// positions, the parent chain and the clip channels all come out of one file, so a clip
        /// keyed in <c>greybox.blend</c> binds to the runtime hierarchy verbatim. Under the harvest
        /// they cannot: the original harvested creature shipped five authored animations that never
        /// played once, because the harvest reparents the meshes out and frees the animated wrappers
        /// those clips target (ANIM-M0 §1.4). That is the hypothesis proven on a shipped asset rather
        /// than argued.</para>
        ///
        /// <para><b>No <see cref="MirrorAboutY"/>.</b> ANIM-M2 re-authored the asset
        /// <c>-Z</c>-forward in <c>build_greybox.py</c>, so the runtime correction is <i>gone</i>
        /// rather than doubled. See that method's remarks for the 81.001 mm/frame foot-slip
        /// measurement the mirror existed to fix, and ANIM-M3's report for the re-measurement after
        /// its removal.</para>
        ///
        /// <para><b>The one genuinely subtle obligation:</b> <see cref="_mounts"/> must survive a
        /// rebuild. <see cref="BuildAppearance"/> destroys and rebuilds the body on an avatar-key
        /// change, and under this mode <c>Pose</c> arrives inside the <c>.glb</c> and is freed with
        /// it. <see cref="RestoreBuiltPose"/> and the re-parent at the end of
        /// <see cref="BuildAuthoredRig"/> are what keep every held item bound; without them they drop
        /// silently, with no error.</para>
        /// </summary>
        AuthoredRig,
    }

    /// <summary>One roster row. The face profile travels WITH the model rather than being
    /// derived from a path convention: part-rig bodies share one (they share a part contract)
    /// and each whole figure has its own, and a rule that produced both would be more clever than
    /// the two-column table it replaced.
    ///
    /// <para><paramref name="AbsentParts"/> is that same idea applied to absence (INTEG-1): the
    /// contract names sixteen parts and a given FILE may legitimately ship without some of them,
    /// so the row states which — per row, never globally. See <see cref="BoxKidAbsentParts"/>
    /// for why a global switch would be the wrong shape.</para>
    ///
    /// <para><paramref name="HeadIsDistinctVolume"/> is the same idea applied to SHAPE, and it
    /// exists because "this model has a part named Head" and "this model's head is a separate
    /// volume" stopped being the same claim (AVATAR-5). See the head block in
    /// <see cref="BuildAppearance"/> for what turns on it and why it defaults true.</para>
    ///
    /// <para><paramref name="PrimitiveBuilder"/> is which code-built body a
    /// <see cref="Build.PrimitiveParts"/> row emits (MOVE-3g). Null means the shipped
    /// <see cref="GreyboxAvatarBody"/>, so every existing row keeps its behaviour without stating
    /// anything; it is a field rather than a fourth <see cref="Build"/> member on purpose —
    /// <c>greybox_classic</c> wants EVERYTHING else <c>PrimitiveParts</c> already decides (no file
    /// to load, no glTF yaw, no warm-cache entry, <see cref="IsCodeBuilt"/> true,
    /// <see cref="ResolvedFromFile"/> false), and a new enum member would have meant auditing every
    /// one of those comparisons for a difference that is purely which method returns the
    /// meshes.</para></summary>
    private readonly record struct RosterRow(
        string ModelPath,
        Build Build,
        System.Collections.Generic.IReadOnlySet<string> AbsentParts,
        bool AxisAlignedParts = false,
        bool HeadIsDistinctVolume = true,
        System.Func<Node3D>? PrimitiveBuilder = null);

    /// <summary>A row that has every part the contract names. The default, and the only honest
    /// default: a declaration of absence is a claim about a specific file, and inheriting one is
    /// how a broken export goes quiet.</summary>
    private static readonly System.Collections.Generic.IReadOnlySet<string> NoAbsentParts =
        new System.Collections.Generic.HashSet<string>();

    /// <summary><b>The eight contract parts <c>BoxKid.glb</c> does not have</b> (BT-7). The same
    /// eight the greybox lacks, and deliberately a SEPARATE set rather than a shared instance —
    /// a declaration of absence is a claim about ONE file, and a global switch would be the wrong
    /// shape. Two bodies happening to lack the same limbs today is not a reason to let one file's
    /// export failure hide behind the other file's declaration.</summary>
    private static readonly System.Collections.Generic.IReadOnlySet<string> BoxKidAbsentParts =
        new System.Collections.Generic.HashSet<string>
        {
            "Belly", "Tail", "BackFiller", "BlushL", "BlushR", "Stem", "LeafL", "LeafR",
        };

    // The avatar roster. A row says which build a body is and BuildAppearance branches once.
    // The MVP extraction (2026-09-02) removed the harvested creature family and the whole-figure blockouts along
    // with their assets; the harvested-parts build path they used stays for the next harvested
    // body, and the two code-built greybox rows still exercise its part contract.
    //
    // Replicated as of 2026-07-23 (see docs/creatures/avatar-roster-description.md §8):
    // SandboxAvatar.AvatarKey rides the same client-authority, spawn-replicated
    // MultiplayerSynchronizer field DisplayName already uses, so every peer renders the
    // SAME model for a given player. SAIL_AVATAR remains the fallback/default source for
    // the local choice (see ResolveEnvAvatarKey) — unset by a picker, headless bots and
    // CI behave exactly as before.
    private static readonly System.Collections.Generic.Dictionary<string, RosterRow> Roster =
        BuildRoster();

    private static System.Collections.Generic.Dictionary<string, RosterRow> BuildRoster()
    {
        var roster = new System.Collections.Generic.Dictionary<string, RosterRow>
        {
            // THE FALLBACK, and the engine-free test fixture (Talon, 2026-08-17: "I would like you
            // to keep the 'generated graybox' model as a fallback in case something doesn't load").
            // No model path, because there is no model: the string is a label that shows up in a
            // missing-part error, never a resource to load. Two live jobs, both real — it is what
            // BuildAppearance falls back to when ANY roster model fails to load (see
            // PrimitiveFallbackAvatarKey), and its constants are what the xUnit suite reads in a
            // host with no engine in it.
            // HeadIsDistinctVolume: FALSE for the same reason the file's row says so — AVATAR-5
            // rebuilt GreyboxAvatarBody around the same revolved profile, so its `Head` is the
            // same top slice of the same continuous surface. Declared here rather than inherited
            // from the row above: the two rows are two bodies, and the day one of them grows a
            // neck the other must not silently follow it.
            ["greybox_primitive"] = new RosterRow(
                GreyboxAvatarBody.SourceLabel,
                Build.PrimitiveParts, NoAbsentParts,
                HeadIsDistinctVolume: false),
            // THE PRE-GUMDROP BODY (MOVE-3g, 2026-08-26). Talon: "bring back the original graybox
            // player character. The one that was before the gumdrop." Recovered verbatim from
            // GreyboxAvatarBody.cs at ea812e03 — the last revision before 1be54a07 rebuilt that
            // file around the approved revolved profile — and it lives in its own type,
            // ClassicGreyboxAvatarBody, so that the row above keeps serving the fallback it has
            // always served. Two rows, two bodies; nothing here changes either of the other two.
            //
            // Build.PrimitiveParts with a PrimitiveBuilder, NOT a new Build member: everything
            // else this build decides is exactly what this body wants, and only the source of the
            // meshes differs. NoAbsentParts for the same reason the row above carries it — this
            // body declares its own eight absences IN THE GEOMETRY, as empty MeshInstance3Ds, so
            // the harvest's loud missing-part error stays fully armed for anything it did not
            // declare.
            //
            // HeadIsDistinctVolume: TRUE, and unlike the two rows above that is not a default
            // being inherited — it is a measured claim about this body. Its head is a separate
            // frustum spanning 0.88 -> 1.20 on a torso whose top is 0.158 half-X against the
            // head's 0.175, so there is a real step at the neck for ART-BIBLE 3's 10% tonal band
            // to land on. That band is exactly what AVATAR-5 had to switch off for a surface with
            // no crease in it; on this body it is back to doing its job.
            ["greybox_classic"] = new RosterRow(
                ClassicGreyboxAvatarBody.SourceLabel,
                Build.PrimitiveParts, NoAbsentParts,
                HeadIsDistinctVolume: true,
                PrimitiveBuilder: ClassicGreyboxAvatarBody.Build),

            // THE BOX BODY (BT-7, 2026-08-27). Talon: "Simple boxy figure: box body, two box
            // arms, two box legs, box head." Authored as a FILE, not as code geometry, because
            // the bubble-test program's Q3 ruled it so: "a code-generated greybox body risks
            // quietly violating the same 'physically present, editor-openable' spirit ... BoxKid
            // is trivial to make (it's just boxes) and gives you a real, versioned, reusable
            // asset instead of a throwaway." Built by assets/creatures/boxkid/build_boxkid.py,
            // which IMPORTS build_greybox.py's joint heights rather than copying them — so this
            // body's pivots are the greybox's pivots to the millimetre and the same clip library
            // binds to it verbatim.
            //
            // Build.AuthoredRig, exactly as the greybox row: the .glb carries Rig/Pose/Body/Waist
            // and the fourteen NLA-track clips, so BuildAuthoredRig caches the imported hierarchy
            // and AvatarClipDirector resolves its six keyed joints by name.
            //
            // HeadIsDistinctVolume: TRUE, and on this body that is a measured claim rather than a
            // default being inherited. The head is a 0.34 m cube standing 0.28 m proud of a trunk
            // 0.24 m deep — a real step at the neck for ART-BIBLE 3's 10% tonal band to land on.
            // The two greybox rows say FALSE because the gumdrop is one continuous revolved
            // surface with no crease for that band to sit on; this body is nothing but creases.
            //
            // Its own BoxKidAbsentParts rather than a shared set, even though the greybox lacked the
            // same eight — for the same reason greybox_primitive does not share
            // another row's: a declaration of absence is a claim about ONE file, and a shared list is
            // what would swallow the loud missing-part error the day this export drops a limb.
            ["boxkid"] = new RosterRow(
                BoxKidModelPath,
                Build.AuthoredRig, BoxKidAbsentParts, AxisAlignedParts: false,
                HeadIsDistinctVolume: true),
        };
        return roster;
    }

    /// <summary>The roster in a fixed, explicit display order (independent of
    /// <c>Dictionary</c> enumeration, which the runtime never contractually guarantees) —
    /// the source both the in-game picker's cycle order and any future roster listing
    /// should read from, so nobody has to duplicate this ordering a second time.
    ///
    /// The greybox leads (GREY-1, 2026-08-16): it is <see cref="PreferredAvatarKey"/>, and index 0
    /// is what the menu picker starts on, so the two must not disagree. Then any
    /// placeholder cast members, and the code-built primitive LAST
    /// (INTEG-1) — it is on the picker so the authored body and its fallback can be put side by
    /// side by hand, but it is a diagnostic row and it does not belong where a cast member goes.
    ///
    /// <para><b>BODY-1 (2026-08-28): index 0 is now <see cref="ClassicGreyboxAvatarKey"/>, and
    /// that is the fix, not a cosmetic reorder.</b> The pickers (<c>HostMenu</c>,
    /// <c>JoinMenu</c>) initialise <c>_avatarIndex = 0</c> and write
    /// <c>RosterEntries[_avatarIndex].Key</c> into <c>NetworkManager.LocalAvatarKey</c>
    /// UNCONDITIONALLY — a player who never touched the picker still ships index 0's key, and a
    /// non-empty <c>LocalAvatarKey</c> beats <see cref="PreferredAvatarKeyFor"/> in
    /// <c>SandboxAvatar</c>. So index 0 is not "where the picker starts"; it is what a player IS
    /// whenever the menus are in the path. Talon played a whole session on the gumdrop after the
    /// world's preferred key had been repointed, because only the fallback moved. The two places
    /// must agree, and they now do: index 0 and <see cref="PreferredAvatarKey"/> are the same
    /// constant.</para>
    ///
    /// <para><b>BODY-2 (2026-08-28): index 0 is now <see cref="BoxKidAvatarKey"/>.</b> Talon, on
    /// the plan's Q1: <i>"The BoxKid.glb is the one I want."</i> BODY-1's MECHANISM is what carried
    /// over — index 0 and <see cref="PreferredAvatarKey"/> written as one symbol, with
    /// <c>ThePickersDefaultRowIsTheDefaultBody</c> failing the moment they diverge — and only its
    /// CHOICE of body was overturned. <see cref="ClassicGreyboxAvatarKey"/> keeps its row at index
    /// 1; it is not deleted, because it is the last code-built player-shaped body and the control
    /// the suite measures the authored one against.</para>
    ///
    /// <para><b>AVATAR-1 (2026-09-05): there is no in-game picker any more, and this array is
    /// unchanged.</b> Talon, on the last change before the Steam playtest upload: <i>"I need this
    /// to only be using the main model ... I don't want players to be able to change for this
    /// initial build because of the movements don't line up right."</i> What was removed is the
    /// player-facing CHOICE — the two menus' prev/next controls and their unconditional write into
    /// <c>NetworkManager.LocalAvatarKey</c> — and nothing else. Every row here is still live: the
    /// primitive is still the load-failure fallback and the engine-free fixture, the classic is
    /// still the code-built control, and both are still reachable through <c>SAIL_AVATAR</c>,
    /// which is how <c>GreyboxPlayerLab</c>, the movement playground and
    /// <c>Run-AvatarIdentityTest</c> select a body. The ORDERING contract above also survives
    /// intact and is deliberately still tested, because it is what a restored picker will need.
    /// This is a FIRST-BUILD restriction taken for an animation-fit reason; see
    /// <c>DECISION-LOG.md</c> for what has to be true before the picker returns.</para></summary>
    public static readonly (string Key, string DisplayName)[] RosterEntries = BuildRosterEntries();

    private static (string Key, string DisplayName)[] BuildRosterEntries()
    {
        var list = new System.Collections.Generic.List<(string, string)>();
        // BODY-2 (2026-08-28): the BOX KID leads. Talon, ruling on the plan's Q1: "The BoxKid.glb
        // is the one I want." Index 0 is PreferredAvatarKey — the SAME SYMBOL, which is BODY-1's
        // mechanism and the whole reason ThePickersDefaultRowIsTheDefaultBody exists — so the
        // pickers' unconditional `RosterEntries[0].Key` write and the world fallback cannot
        // disagree about what a player is.
        list.Add((BoxKidAvatarKey, "Box Kid"));
        // BODY-1: the classic greybox IS "Greybox" now — the gumdrop row it displaced is gone, so
        // the display name moves with the job rather than acquiring a parenthetical nobody needs.
        // BODY-2 moved it off index 0 and KEPT the row: it is the engine-free xUnit fixture's
        // sibling, the only surviving code-built player-shaped body, and the control the suite
        // measures the authored one against. See the report's picker recommendation.
        list.Add((ClassicGreyboxAvatarKey, "Greybox"));
        list.Add((PrimitiveFallbackAvatarKey, "Greybox (primitive)"));
        return list.ToArray();
    }

    /// <summary>
    /// <b>The load-failure fallback: the code-built primitive, as its own roster row.</b> Talon,
    /// 2026-08-17: <i>"I would like you to keep the 'generated graybox' model as a fallback in case
    /// something doesn't load, you're right."</i>
    ///
    /// <para><see cref="BuildAppearance"/> substitutes this row's geometry for ANY roster model
    /// whose <c>.glb</c> fails to load — a missing file, a failed import, a corrupt export — rather
    /// than only for the greybox, because "the model did not load" is not a greybox-specific
    /// failure and the primitive satisfies the same sixteen-part contract the harvest path drives.
    /// Before this existed, a failed load reached <c>Instantiate</c> on a null scene and took the
    /// spawn down with it.</para>
    /// </summary>
    public const string PrimitiveFallbackAvatarKey = "greybox_primitive";

    /// <summary>
    /// <b>The pre-gumdrop greybox, recovered (MOVE-3g, 2026-08-26).</b> Talon: <i>"bring back the
    /// original graybox player character. The one that was before the gumdrop."</i> The geometry
    /// is <see cref="ClassicGreyboxAvatarBody"/> — <c>GreyboxAvatarBody.cs</c> as it stood at
    /// <c>ea812e03</c>, the last revision before the revolved profile landed.
    ///
    /// <para><b>BODY-1 (2026-08-28): this is now the played body everywhere.</b> Talon:
    /// <i>"I want the one that looks like a gumdrop to be removed completely and the only one
    /// that's used for this playtest is the one that looks like boxes."</i> It is
    /// <see cref="PreferredAvatarKey"/> and <see cref="RosterEntries"/> index 0. It is still NOT
    /// <see cref="PrimitiveFallbackAvatarKey"/> — a load failure falls back to
    /// <see cref="GreyboxAvatarBody"/>, which survives untouched as the engine-free xUnit fixture
    /// and the substitute for ANY row whose <c>.glb</c> fails to load. (This body cannot itself
    /// fail to load: it has no file.)</para>
    ///
    /// <para><b>The string rides the wire</b> (<c>SandboxAvatar.AvatarKey</c>), which is why this is
    /// a NEW value rather than a repurposed one: no existing key changed meaning, so an old client
    /// and a new one still agree about every body either of them can name.</para>
    /// </summary>
    public const string ClassicGreyboxAvatarKey = "greybox_classic";

    /// <summary>What a player is by default when nothing chose for them — the greybox
    /// (Talon, 2026-08-16), because the running and everything else feels wrong and he cannot
    /// read the animation through a body that has no limbs to move. This is a FEEL instrument, not a cast decision — the real cast (pendling, banneret) is
    /// specced and unbuilt, and superseding this key is one line when it lands.
    ///
    /// <para><b>INTEG-1 (2026-08-17) made this key a FILE — <c>Greybox.glb</c> — and BODY-1
    /// (2026-08-28) took the file away again.</b> That is a reversal, so the reason INTEG-1 gave is
    /// recorded rather than deleted. Talon then: <i>"I don't like assets of things that I can't
    /// open or see... I want to be able to open this asset model player character and open the
    /// file in blender or godot and look and see and manipulate and change things."</i> Talon now:
    /// <i>"I want the one that looks like a gumdrop to be removed completely."</i> The body he
    /// wants predates that file, so the file goes. <see cref="ResolvedModelPath"/> is still where a
    /// running process states which geometry it actually got.</para>
    ///
    /// Deliberately NOT the same constant as <see cref="DefaultAvatarKey"/>. That one is the
    /// clamp target for a value that arrived WRONG — a hostile client, a stale build, a
    /// dropped roster entry — and Run-AvatarIdentityTest pins the clamp target on the wire.
    /// Conflating "what we fall back to when someone lied to us" with "what you are if you
    /// never picked" is how a security clamp quietly becomes a design decision.
    ///
    /// <para><b>BODY-1 (2026-08-28) repointed it at <see cref="ClassicGreyboxAvatarKey"/> and
    /// deleted the file it used to name.</b> Talon asked for the OLDEST greybox — the one made of
    /// boxes, not the gumdrop — and the gumdrop's roster row and <c>Greybox.glb</c> are gone with
    /// it. Written as the constant rather than the literal <c>"greybox_classic"</c> so the two
    /// cannot drift: <see cref="RosterEntries"/> index 0 reads the same symbol, and the whole bug
    /// this packet fixed was those two disagreeing.</para>
    ///
    /// <para><b>The tension this leaves open, deliberately unresolved.</b> Talon's standing ruling
    /// is that code-only geometry is not a real asset — the reason INTEG-1 made this key a file at
    /// all, and the reason BT-7 authored <c>BoxKid.glb</c> rather than more code. This body is
    /// built in code and has no <c>.glb</c>. His live word outranks the ruling and this key
    /// follows his live word; the tension is noted, not settled here.</para>
    ///
    /// <para><b>BODY-2 (2026-08-28) repointed it at <see cref="BoxKidAvatarKey"/>, and that
    /// RESOLVES the tension the paragraph above left open.</b> Talon, on the plan's Q1:
    /// <i>"The BoxKid.glb is the one I want."</i> The played body is a file again —
    /// <c>assets/creatures/boxkid/BoxKid.glb</c>, openable in Blender and in Godot — which is what
    /// his standing "no assets I cannot open" ruling always asked for, so INTEG-1's reasoning and
    /// his live word now point the same way for the first time since BODY-1. BODY-1's mechanism is
    /// untouched and is the reason this is one line: <see cref="RosterEntries"/> index 0 reads this
    /// same symbol, and <c>ThePickersDefaultRowIsTheDefaultBody</c> is red the moment they
    /// disagree.</para></summary>
    public const string PreferredAvatarKey = BoxKidAvatarKey;

    /// <summary><b>The box body (BT-7, 2026-08-27).</b> Six volumes — head, body, two arms, two
    /// legs — authored in <c>assets/creatures/boxkid/build_boxkid.py</c> and exported as
    /// <c>BoxKid.glb</c>. BT-7 added it as a pure ADDITION that displaced nothing.
    ///
    /// <para><b>BODY-2 (2026-08-28): it is now the played body.</b> Talon: <i>"The BoxKid.glb is
    /// the one I want."</i> It is <see cref="PreferredAvatarKey"/> and <see cref="RosterEntries"/>
    /// index 0. It is still NOT <see cref="PrimitiveFallbackAvatarKey"/> and still not
    /// <see cref="DefaultAvatarKey"/>: a failed load of this <c>.glb</c> falls back to
    /// <see cref="GreyboxAvatarBody"/> exactly as any other file-backed row does, and a key that
    /// arrived WRONG still clamps to <see cref="DefaultAvatarKey"/>. Those are three different jobs and conflating
    /// any two of them is how a security clamp becomes a design decision.</para>
    ///
    /// <para>The string rides the wire (<c>SandboxAvatar.AvatarKey</c>), which is why BT-7 made it
    /// a NEW value rather than repurposing one — no existing key changed meaning then, and none
    /// changes meaning now: BODY-2 moves which key is the DEFAULT, not what any key names.</para>
    /// </summary>
    public const string BoxKidAvatarKey = "boxkid";

    /// <summary>Where <see cref="BoxKidAvatarKey"/>'s geometry lives. A constant rather than a
    /// literal in the roster because <c>BoxKidAssetContractTests</c> reads the shipped bytes at
    /// this path and a second spelling is how the two would drift.</summary>
    public const string BoxKidModelPath = "res://assets/creatures/boxkid/BoxKid.glb";

    /// <summary>The launch world whose players are <see cref="BoxKidAvatarKey"/> — the bubble
    /// test (<c>--world bubbletest</c>, BT-0). Named here rather than at the call site so the
    /// world→body table is one table.</summary>
    public const string BoxKidWorldId = "bubbletest";

    /// <summary>
    /// <b>What a player is in <paramref name="worldId"/> when nobody chose for them.</b>
    /// <see cref="PreferredAvatarKey"/> everywhere except the bubble test, which is
    /// <see cref="BoxKidAvatarKey"/>.
    ///
    /// <para><b>It replaces the fallback, never the choice</b>, and that ordering is the whole
    /// design of this function. A picker selection still wins, and so does an explicit
    /// <c>SAIL_AVATAR</c> — see <see cref="ResolveEnvAvatarKey(string)"/>. A level deciding what
    /// body you get is a sensible default for a level built around one; a level overriding what
    /// you deliberately picked is a bug report, and the two are one line apart.</para>
    ///
    /// <para>A null or unknown world id is <see cref="PreferredAvatarKey"/>, because "I do not
    /// recognise this world" and "this world has no opinion" are the same answer and inventing a
    /// third would only be a way to fail loudly at something harmless.</para>
    /// </summary>
    /// <para><b>BODY-1 (2026-08-28): every world now answers the same, and the table is
    /// deliberately kept rather than inlined.</b> Talon: <i>"I want the oldest gray box model...
    /// the one that looks like a gumdrop [is] removed completely and the only one that's used for
    /// this playtest is the one that looks like boxes."</i> That is
    /// <see cref="ClassicGreyboxAvatarKey"/>, and it is now <see cref="PreferredAvatarKey"/>, so
    /// the bubble test's special case and the global default resolved to the same value and the
    /// branch became a lie about there being a choice. This supersedes BT-7's D7/Q3 default of
    /// <see cref="BoxKidAvatarKey"/> for that world; the box kid stays a roster row and
    /// <c>SAIL_AVATAR=boxkid</c> still reaches it anywhere. The function survives the collapse
    /// because a per-world body is a design the level roster is expected to want again, and
    /// re-deriving the seam at every call site later is the expensive half.</para>
    ///
    /// <para><b>BODY-2 (2026-08-28): still collapsed, and now collapsed onto the box kid.</b>
    /// Talon: <i>"The BoxKid.glb is the one I want"</i>, and <i>"the only one that's used for this
    /// playtest"</i>. The bubble test and every other world answer alike again — which means BT-7's
    /// per-world branch has now been the same value under two different bodies, and the function is
    /// STILL kept for the reason above rather than because it currently does anything.</para>
    public static string PreferredAvatarKeyFor(string? worldId) => PreferredAvatarKey;

    /// <summary>True when <paramref name="key"/> (already normalized — see
    /// <see cref="NormalizeAvatarKey"/>) names a real roster entry.</summary>
    public static bool IsValidAvatarKey(string key) => Roster.ContainsKey(key);

    /// <summary>Where a roster key's geometry is DECLARED to come from — a <c>res://</c> path for a
    /// file-backed row, or <see cref="GreyboxAvatarBody.SourceLabel"/> for the code-built one. The
    /// declaration, not the outcome: what a given instance actually got is
    /// <see cref="ResolvedModelPath"/>, and the two differ exactly when a load failed.</summary>
    public static string DeclaredModelPathFor(string avatarKey) => Roster[avatarKey].ModelPath;

    /// <summary>True when a roster key's geometry is built in code rather than loaded from a
    /// file.</summary>
    public static bool IsCodeBuilt(string avatarKey) => Roster[avatarKey].Build == Build.PrimitiveParts;

    /// <summary>True when <see cref="BuildAppearance"/> applies <see cref="MirrorAboutY"/> to this
    /// row's instanced model. Exposed for <c>GreyboxPlayerLab</c>'s asset-vs-runtime pivot gate,
    /// which must mirror its hidden reference copy exactly when — and only when — the runtime does,
    /// or the comparison measures the correction rather than the joints. See
    /// <see cref="Build.AuthoredRig"/> for why the played body no longer takes one.</summary>
    public static bool AppliesFacingMirrorFor(string avatarKey) =>
        Roster[avatarKey] is { Build: not Build.AuthoredRig, AxisAlignedParts: true };

    /// <summary>True when a roster key's geometry IS the imported scene rather than a harvest of it
    /// — <see cref="Build.AuthoredRig"/>. Read by the labs and by the contract tests; nothing in
    /// shipping code branches on it.</summary>
    public static bool IsAuthoredRig(string avatarKey) => Roster[avatarKey].Build == Build.AuthoredRig;

    /// <summary>The parts <paramref name="avatarKey"/>'s row declares it does not have. See
    /// <see cref="BoxKidAbsentParts"/>.</summary>
    internal static System.Collections.Generic.IReadOnlySet<string> DeclaredAbsentPartsFor(string avatarKey) =>
        Roster[avatarKey].AbsentParts;

    /// <summary>Replaces one row's declared-absent set. The test seam behind criterion 7's positive
    /// control: point a row at its real model while declaring NOTHING absent and every genuinely
    /// missing part must show up in <see cref="UndeclaredMissingParts"/> — which is what proves the
    /// declaration silences exactly the parts that are really gone, rather than everything. Read the
    /// original out of <see cref="DeclaredAbsentPartsFor"/> first and put it back afterwards.</summary>
    internal static void OverrideDeclaredAbsentPartsForTest(
        string avatarKey, System.Collections.Generic.IReadOnlySet<string> parts)
    {
        Roster[avatarKey] = Roster[avatarKey] with { AbsentParts = parts };
        _restPoseCache.Remove(avatarKey);
    }

    /// <summary>Whether a row declares its head a distinct volume — see
    /// <see cref="RosterRow.HeadIsDistinctVolume"/> and the head block in
    /// <see cref="BuildAppearance"/>.</summary>
    internal static bool HeadIsDistinctVolumeFor(string avatarKey) =>
        Roster[avatarKey].HeadIsDistinctVolume;

    /// <summary>Flips one row's head-value-break declaration. The test seam behind the positive
    /// control for it: asserting only that the greybox's head matches its torso would pass exactly
    /// as well if the value break had been DELETED rather than made conditional — and a deleted
    /// rule is a silent regression waiting for the first model that grows a real head. Forcing this
    /// true has to make the two colours diverge again. Put the original back afterwards.</summary>
    internal static void OverrideHeadIsDistinctVolumeForTest(string avatarKey, bool distinct)
    {
        Roster[avatarKey] = Roster[avatarKey] with { HeadIsDistinctVolume = distinct };
        _restPoseCache.Remove(avatarKey);
    }

    /// <summary>The albedo actually on a harvested part right now, or null if that part is missing
    /// or carries no material override. Read off the BUILT body rather than recomputed from the
    /// palette: what is worth asserting is what the renderer was handed.</summary>
    internal Color? PartAlbedo(string name) =>
        FindChild(name, recursive: true, owned: false) is MeshInstance3D mesh
        && mesh.MaterialOverride is StandardMaterial3D mat
            ? mat.AlbedoColor
            : null;

    /// <summary>
    /// <b>Every part name the harvest found missing WITHOUT the row having declared it absent,
    /// since the last reset.</b> The observable side of the <see cref="GD.PushError"/> in
    /// <see cref="TakePart"/>, so a test can assert on it — an error that can only be eyeballed in
    /// a log is an error no suite defends.
    /// </summary>
    internal static System.Collections.Generic.IReadOnlyList<string> UndeclaredMissingParts =>
        _undeclaredMissingParts;

    internal static void ResetUndeclaredMissingParts() => _undeclaredMissingParts.Clear();

    private static readonly System.Collections.Generic.List<string> _undeclaredMissingParts = new();

    /// <summary>One line per distinct (key, resolved source) pair, so a running process states in
    /// its log which geometry it is actually rendering. Deduped because this would otherwise print
    /// once per spawn.</summary>
    private static readonly System.Collections.Generic.HashSet<string> _loggedResolutions = new();

    /// <summary>
    /// <b>Where THIS instance's geometry actually came from</b> — the resolved <c>res://</c> path,
    /// or <see cref="GreyboxAvatarBody.SourceLabel"/> when the primitive was built (either because
    /// the row asked for it or because the row's model failed to load).
    ///
    /// <para>Exists because "the game loads the file" is a claim about a running process, and a
    /// source constant cannot settle it. This is the field to assert on and the field the lab
    /// prints.</para>
    /// </summary>
    public string ResolvedModelPath { get; private set; } = "";

    /// <summary>True when this instance's geometry was parsed out of a file, false when it was
    /// built in code. False on a fallback, which is the point.</summary>
    public bool ResolvedFromFile { get; private set; }

    /// <summary>Validate-on-receipt: trims/lowercases like the roster's own keys, then
    /// clamps anything unrecognized to <see cref="DefaultAvatarKey"/>. This is the single
    /// choke point every avatar-key write passes through (local default, picker choice,
    /// and a synced value arriving from another peer alike — see
    /// <c>SandboxAvatar.AvatarKey</c>'s setter), so an out-of-range or malformed value —
    /// whether a stale build's dropped roster entry or a deliberately hostile client — can
    /// never make any peer resolve an unknown model.</summary>
    public static string NormalizeAvatarKey(string? key)
    {
        string normalized = (key ?? "").Trim().ToLowerInvariant();
        if (normalized == RetiredGumdropAvatarKey)
            return ClassicGreyboxAvatarKey;
        return IsValidAvatarKey(normalized) ? normalized : DefaultAvatarKey;
    }

    /// <summary>
    /// <b>The retired gumdrop's wire value: <c>"greybox"</c>, aliased forward to
    /// <see cref="ClassicGreyboxAvatarKey"/> rather than clamped</b> (BODY-1, 2026-08-28).
    ///
    /// <para><b>Why an alias and not a rejection.</b> The choice is not "honest error vs. silent
    /// substitution" — it is between two silent substitutions, because <see cref="NormalizeAvatarKey"/>
    /// has exactly one containment path and it lands on <see cref="DefaultAvatarKey"/>. So the
    /// question is only <i>which</i> wrong body a peer who says <c>"greybox"</c> gets, and the two
    /// candidates are the classic greybox or the <b>primitive fallback</b>. A peer sending this string is not
    /// lying: it is an older build, a stale launcher line, a saved picker index, or a
    /// <c>SAIL_AVATAR=greybox</c> in a script — and it means <i>"I am the greybox player body"</i>,
    /// which the classic now is. Turning that into an unrelated creature is the larger lie of the
    /// two, and it would land silently in a shipped session rather than at a build boundary.</para>
    ///
    /// <para><b>What it does NOT weaken.</b> The clamp is untouched for everything else: a value
    /// that is set and is not a roster key still becomes <see cref="DefaultAvatarKey"/>, which is
    /// what <c>Run-AvatarIdentityTest</c>'s deliberately-bogus bot pins on the wire. This is one
    /// named retired identity mapped to its successor, not a general "close enough" rule — and it
    /// is a value that <see cref="IsValidAvatarKey"/> deliberately still answers <c>false</c> for,
    /// so nothing downstream can index the roster with it.</para>
    ///
    /// <para><b>BODY-2 (2026-08-28) re-examined this alias and DELIBERATELY LEFT IT ALONE. The
    /// full wire table, because the packet asks for it stated rather than inferred:</b></para>
    /// <list type="table">
    ///   <item><description><c>"greybox"</c> (an old build, a stale launcher line, a saved picker
    ///     index) → <see cref="ClassicGreyboxAvatarKey"/>. <b>Not</b> the new default. The sender
    ///     named a BODY that still exists on the roster, and it still gets that body. Re-pointing
    ///     this alias at <see cref="PreferredAvatarKey"/> would silently convert it from "the
    ///     greybox body" into "whatever is default this week", so a peer's stated choice would
    ///     change meaning every time Talon changes his mind about the default — which is exactly
    ///     the class of silent substitution the alias exists to bound.</description></item>
    ///   <item><description><c>"greybox_classic"</c> → itself. A live roster row, unchanged by
    ///     BODY-2; it merely stopped being index 0.</description></item>
    ///   <item><description><c>"boxkid"</c> → itself, and it is now also what an UNSET local choice
    ///     resolves to (<see cref="ResolveEnvAvatarKey()"/> → <see cref="PreferredAvatarKey"/>).
    ///     </description></item>
    ///   <item><description>anything else that is set and is not a roster key →
    ///     <see cref="DefaultAvatarKey"/>, the containment clamp, untouched.</description></item>
    /// </list>
    /// <para>Covered by <c>TheRetiredGumdropKeyNormalisesToTheClassicAndNotToTheClamp</c> and
    /// <c>TheWireKeyTableIsWhatTheReportSays</c>.</para>
    /// </summary>
    public const string RetiredGumdropAvatarKey = "greybox";

    /// <summary>The local-choice fallback: <c>SAIL_AVATAR</c>, normalized/clamped exactly
    /// like a network-received key. Read fresh (not cached) — cheap, and it lets a test
    /// harness change the env var between runs without a stale value lingering.
    ///
    /// UNSET is a different case from INVALID and now resolves differently: nobody chose, so
    /// you get <see cref="PreferredAvatarKey"/> — the played body. A value
    /// that was SET and is not a roster key still clamps to <see cref="DefaultAvatarKey"/>,
    /// because that path exists to contain a stale or hostile client and must keep behaving
    /// exactly as it did (Run-AvatarIdentityTest launches a bot with a deliberately bogus key
    /// and pins the clamp target on the wire).</summary>
    public static string ResolveEnvAvatarKey() => ResolveEnvAvatarKey(null);

    /// <summary>The same fallback, told which world it is falling back IN (BT-7). An unset
    /// <c>SAIL_AVATAR</c> resolves to <see cref="PreferredAvatarKeyFor"/> rather than to
    /// <see cref="PreferredAvatarKey"/>; a SET one still wins, unchanged, which is what keeps
    /// <c>SAIL_AVATAR=boxkid</c> a way to see the box body in any world and
    /// <c>SAIL_AVATAR=greybox</c> a way to opt out of it inside the bubble test.</summary>
    public static string ResolveEnvAvatarKey(string? worldId)
    {
        string raw = OS.GetEnvironment("SAIL_AVATAR");
        return string.IsNullOrWhiteSpace(raw)
            ? PreferredAvatarKeyFor(worldId)
            : NormalizeAvatarKey(raw);
    }

    // One PackedScene per roster key, loaded on first use and shared by every avatar
    // instancing that key (Godot shares Mesh/Material sub-resources across instances by
    // default, so this costs one glTF parse per DISTINCT model in play, not one per
    // spawn). Keyed by roster key rather than a single process-wide slot — replication
    // means two avatars in the same process can now legitimately render different
    // models at once, so the old "one model for the whole process" cache would be wrong.
    private static readonly System.Collections.Generic.Dictionary<string, PackedScene?> _modelScenes = new();

    /// <summary>Roster keys whose model load is FORCED to fail, for the life of the process. The
    /// test seam behind criterion 6 of INTEG-1: an untested fallback is not a fallback, and the
    /// only honest way to test this one is to make a load actually fail. Empty in every real
    /// session — nothing but <c>SandboxSelfTest</c> and <c>GreyboxPlayerLab</c> writes it.</summary>
    private static readonly System.Collections.Generic.HashSet<string> _forcedLoadFailures = new();

    /// <summary>Makes (or stops making) <paramref name="avatarKey"/>'s model load fail. Drops that
    /// key's cached scene AND its cached rest-pose measurement, because both are keyed by the model
    /// and the point of the exercise is that the geometry changes underneath them.</summary>
    internal static void ForceModelLoadFailure(string avatarKey, bool forced)
    {
        if (forced)
            _forcedLoadFailures.Add(avatarKey);
        else
            _forcedLoadFailures.Remove(avatarKey);
        _modelScenes.Remove(avatarKey);
        _restPoseCache.Remove(avatarKey);
    }

    /// <summary>Null when the model could not be loaded — a missing file, a failed import, or the
    /// forced-failure seam above. Callers fall back to the primitive; see
    /// <see cref="PrimitiveFallbackAvatarKey"/>. The null is cached like a hit would be, so a
    /// broken asset costs one failed load per process rather than one per spawn.</summary>
    private static PackedScene? ModelSceneFor(string avatarKey)
    {
        if (_forcedLoadFailures.Contains(avatarKey))
            return null;
        if (!_modelScenes.TryGetValue(avatarKey, out PackedScene? scene))
        {
            string path = Roster[avatarKey].ModelPath;
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            _modelScenes[avatarKey] = scene;
        }
        return scene;
    }

    /// <summary>Forces every roster model's one-time glTF parse at scene setup instead of
    /// on first spawn (see FlashlightProp.WarmModelCache — same hitch, same reason). All
    /// six, not just the local player's pick, because a remote peer can now spawn wearing
    /// any of them.</summary>
    public static void WarmModelCache()
    {
        foreach (System.Collections.Generic.KeyValuePair<string, RosterRow> entry in Roster)
        {
            // A code-built body has no glTF parse to hoist and no path to load — its RosterRow
            // carries a human-readable label where a model path would be. Warming it would send
            // that label to GD.Load and error on every scene setup.
            if (entry.Value.Build != Build.PrimitiveParts)
                _ = ModelSceneFor(entry.Key);
        }
    }

    /// <summary>
    /// <b>The node that carries the body's POSE, and the reason held items finally follow the
    /// hands (ANIM-1, Gap 0).</b>
    ///
    /// <para><b>What was wrong.</b> The lean, the waddle roll, the hop and the idle fidget were all
    /// written to <see cref="_body"/>, which lives inside this visual — while the carry, stow and
    /// aim anchors were children of the avatar ROOT, one level up, and had their <i>position</i>
    /// copied to them each frame with no rotation at all. So the body pitched 13 degrees forward at
    /// full sprint and rolled 6 degrees each step, and the hand a log was supposedly in did not
    /// move: Talon's "it's still in the same spot, it's just that their body has angled forward to
    /// run", exactly.</para>
    ///
    /// <para><b>The fix is parentage, not a better copy.</b> Anything mounted under
    /// <see cref="Mounts"/> inherits the lean, the roll, the hop, the fidget yaw, the reconciliation
    /// offset AND the knocked-out/frozen root pose for free, and keeps inheriting whatever is added
    /// next. <c>CarryAnchorGlobalTransform</c> and every existing caller of it —
    /// <c>NetworkedProp</c>, <c>CarryController</c> — are unchanged, which is what makes this
    /// contained.</para>
    ///
    /// <para><b>Why the pose moved OFF <see cref="_body"/> rather than the anchors moving ONTO it.</b>
    /// Two reasons, both load-bearing. <see cref="_body"/> carries the squash-and-stretch scale, and
    /// a mount under it would hand every held log a non-uniform scale (harmless for
    /// <c>Carryable</c>, which orthonormalises, and silently wrong for anything that does not). And
    /// <see cref="BuildAppearance"/> destroys and rebuilds <see cref="_body"/> whenever a synced
    /// avatar key catches up — a mount parented under it would be freed with it, taking the
    /// anchors and every held item's binding with them. This node survives a rebuild for that
    /// reason.</para>
    /// </summary>
    private Node3D _pose = null!;

    /// <summary>The whole-rig vertical crouch node (RIG-1) — the sole writer of
    /// <see cref="CrouchDropM"/>. Carries <see cref="_pose"/> and both leg roots together, so a
    /// landing absorb lowers the hips and the held prop while the feet stay on the floor. See
    /// <see cref="CrouchDropM"/> for why this is a node rather than a term in the pose. Survives a
    /// rebuild for the same reason <see cref="_pose"/> does — the pose hangs inside it.</summary>
    private Node3D _crouch = null!;

    /// <summary>Where the avatar's anchors hang. Its own node rather than the anchors going straight
    /// onto <see cref="_pose"/> so <see cref="AccumulateBodyBounds"/> can exclude the whole subtree
    /// in one line — a net or a held log measured into <see cref="BodyBounds"/> would silently grow
    /// the character's collision capsule, and that failure looks exactly like a tuning mistake.</summary>
    private Node3D _mounts = null!;

    /// <summary>The node a caller parents a hand/back attachment to. See <see cref="_pose"/> for
    /// what it inherits and <see cref="_mounts"/> for why it is not <see cref="_pose"/> itself.</summary>
    public Node3D Mounts
    {
        get
        {
            EnsurePoseRig();
            return _mounts;
        }
    }

    private Node3D _body = null!;
    private Node3D _sprout = null!;
    private Node3D _leftArm = null!;
    private Node3D _rightArm = null!;
    private Node3D _leftThigh = null!;
    private Node3D _rightThigh = null!;
    private Node3D _leftEye = null!;
    private Node3D _rightEye = null!;
    private Vector2 _sproutSway;
    private Vector2 _sproutSwayVel;

    // --- RIG-1's new joints. All nullable, all optional, all degraded-to-absent by default ------
    //
    // NOT `null!`, unlike everything above, and the difference is the whole graceful-degradation
    // contract. Every .glb on the roster at the time (a harvested-part family and a whole-figure
    // family, since removed) had no Head, no ShinL/R and no ForearmL/R, and RIG-1 edits no .glb and does not touch docs/BLENDER-EXPORT.md.
    // A null here means "this rig genuinely has no such joint", and every read below falls back to
    // the single-segment rotation that shipped rather than synthesising a joint out of nothing.

    /// <summary>The waist pivot: a Node3D placed AT the split in the trunk geometry, carrying the
    /// upper torso, the head and both arms. Built by this class rather than harvested — there is no
    /// mesh at a joint — but placed off the harvested lower torso's own measured top, so it cannot
    /// drift from the seam it is supposed to bend at. Null on a rig with no authored split.</summary>
    private Node3D? _waist;

    private MeshInstance3D? _head;
    private MeshInstance3D? _shinL;
    private MeshInstance3D? _shinR;

    /// <summary>The FOOT nodes (ANIM-M2b). Optional exactly as the shins are — the greybox is
    /// the only body on the roster that has them — and the ankle is the one channel they carry.
    /// See <see cref="LimbIk.LevelAnkle"/> for why that channel is the solver's rather than a
    /// clip's.</summary>
    private MeshInstance3D? _footL;
    private MeshInstance3D? _footR;
    private MeshInstance3D? _forearmL;
    private MeshInstance3D? _forearmR;

    private Vector3 _shinLRestRot;
    private Vector3 _shinRRestRot;
    private Vector3 _footLRestRot;
    private Vector3 _footRRestRot;
    private Vector3 _forearmLRestRot;
    private Vector3 _forearmRRestRot;

    /// <summary>Measured segment lengths — see <see cref="ThighLengthM"/> for why they are measured
    /// rather than declared, and why the pair summing exactly to the whole limb is load-bearing.
    /// Defaulted to half of the fallback limb so a degraded rig still solves something sane.</summary>
    private float _thighLengthM = 0.175f;
    private float _upperArmLengthM = 0.20f;
    private float _armLengthM = 0.38f;

    private float _kneeBendL;
    private float _kneeBendR;
    private float _ankleLevelL;
    private float _ankleLevelR;
    private float _elbowBendL;
    private float _elbowBendR;

    // Rest transforms captured off the authored model right after it's harvested, so
    // Animate() blends/offsets relative to the artist's real geometry instead of the
    // old procedural blob coordinates.
    private Vector3 _leftArmRest;
    private Vector3 _rightArmRest;
    private float _leftThighRestY;
    private float _rightThighRestY;

    /// <summary>The authored rest ROTATION of each limb, captured at harvest alongside its rest
    /// position (MOVE-1). The gait swings limbs about their own hips, which is a rotation channel
    /// nothing wrote before — and a harvest reparents with <c>keepGlobalTransform</c>, so a part the
    /// artist posed at an angle arrives carrying it. Writing the gait angle absolutely would have
    /// silently straightened every such part on every roster key that has one; the greybox has none
    /// (its legs are authored axis-aligned) which is exactly why it would never have shown up
    /// here.</summary>
    private Vector3 _leftArmRestRot;
    private Vector3 _rightArmRestRot;
    // MERGE NOTE (PLAYTEST-1 trunk, 2026-08-22). ANIM-M2b renamed the leg's top bone Foot -> Thigh
    // and gave the rig a real foot below it; CARRY-1 predates that rename, so its side of this hunk
    // still carried master's `_leftFootRestRot` / `_rightFootRestRot`. Those are the SAME two fields
    // this file now calls `_leftThighRestRot` / `_rightThighRestRot`, not new ones — keeping both
    // would have declared the thigh's rest pose twice and let the leg solver read whichever copy was
    // written last. CARRY-1 does not touch leg code at all (verified against master), so the rename
    // simply wins and only CARRY-1's genuinely new arm fields are taken.
    private Vector3 _leftThighRestRot;
    private Vector3 _rightThighRestRot;

    /// <summary>The authored rest SCALE of each arm, captured for the same reason its rest rotation
    /// is: CARRY-1 writes the arm's whole <see cref="Basis"/> rather than its Euler
    /// <c>Rotation</c> (a hand target is a direction, and directions do not add as Euler triples), and
    /// a basis write would otherwise silently reset a scale the artist authored.</summary>
    private Vector3 _leftArmRestScale = Vector3.One;
    private Vector3 _rightArmRestScale = Vector3.One;

    /// <summary>Where each hand goes for each carry, as an offset from that arm's OWN shoulder in the
    /// arm's rest-local frame — measured off this body at build time, never typed. See
    /// <see cref="MeasureCarryTargets"/> for where each number comes from.</summary>
    private Vector3 _handleHandR;
    private Vector3 _armfulHandL;
    private Vector3 _armfulHandR;

    /// <summary>How far short of its asked-for hand target each arm fell this frame, metres.</summary>
    private float _handShortfallL;
    private float _handShortfallR;
    private Vector3 _leftEyeRestScale;
    private Vector3 _rightEyeRestScale;

    /// <summary>Hip-to-foot length, metres, measured off this character rather than typed. The rig's
    /// foot node IS its hip joint (the leg mesh hangs below it), so its rest height above the ground
    /// is the leg length — which is why the greybox's 0.36 m node yields exactly 0.36 m. A model
    /// whose "feet" are stubs sitting on the floor has no leg to measure, so it falls back to a
    /// fraction of its own measured height; see <see cref="MeasureRestPose"/>.</summary>
    private float _legLengthM = 0.35f;

    /// <summary>The roster key this instance was last built from — exposed so a caller
    /// (SandboxAvatar) can skip a redundant rebuild when a synced value turns out to
    /// match what's already showing.</summary>
    public string AvatarKey { get; private set; } = DefaultAvatarKey;

    /// <summary>Builds the creature's visual by instancing the given roster entry's glb
    /// and reparenting its named parts onto the animated rig. Safe to call more than
    /// once on the same instance — a later call (the avatar-key synchronizer catching up
    /// to the authority's real pick a beat after spawn, exactly like DisplayName's own
    /// catch-up window) tears down whatever was already built and reinstances fresh,
    /// rather than piling a second model on top of the first.</summary>
    public void BuildAppearance(Color bodyColor, string avatarKey)
    {
        avatarKey = NormalizeAvatarKey(avatarKey);
        AvatarKey = avatarKey;

        EnsurePoseRig();
        // A previous build may have handed _pose to the .glb (Build.AuthoredRig). Take it back
        // BEFORE the teardown below frees that model, or the anchors go with it. See
        // RestoreBuiltPose.
        RestoreBuiltPose();
        // Back to rest BEFORE anything is measured. MeasureRestPose reads global vertex positions,
        // and on a REBUILD this node is already carrying a frame of live waddle — measuring the
        // artist's rest pose through 13 degrees of run lean would bake that lean into the collision
        // capsule and the eyeline for the rest of the process (the measurement is cached per key).
        // The crouch is reset with it, and for the same reason: measuring a rest pose through a
        // landing absorb would bake a 4 cm hip drop into every derived dimension.
        _pose.Transform = Transform3D.Identity;
        _crouch.Transform = Transform3D.Identity;
        CrouchDropM = 0f;
        // The verb blends go with it (MOVE-5c) and for the identical reason: a rebuild that landed
        // mid-slide would measure the rest pose through 46% of a knee fold and bake a 20 cm crouch
        // into the capsule and the eyeline for the rest of the process.
        _tuckBlend = 0f;
        _slideBlend = 0f;
        _duckBlend = 0f;
        _chainRead = 0f;

        // Every joint is re-resolved by the harvest below. Cleared here rather than trusted, because
        // a rebuild can land on a DIFFERENT character — a rig with knees rebuilt as one without them
        // would otherwise keep solving through a disposed shin node.
        _waist = null;
        _head = null;
        _shinL = _shinR = null;
        _footL = _footR = null;
        _forearmL = _forearmR = null;
        HasKnees = HasElbows = HasWaist = false;

        if (_body != null)
        {
            foreach (Node child in GetChildren())
            {
                // The pose rig outlives a rebuild: the avatar's carry anchor hangs inside it, and
                // freeing it would leave every held prop bound to a disposed node. See _pose. The
                // crouch is the pose's parent, so keeping the pose means keeping it.
                if (child == _crouch)
                    continue;
                RemoveChild(child);
                child.QueueFree();
            }

            // The feet are children of the CROUCH node, not of the body (so squash does not lift
            // them and so a landing absorb does lower them), so the loop above did not reach them.
            foreach (Node child in _crouch.GetChildren())
            {
                if (child == _pose)
                    continue;
                _crouch.RemoveChild(child);
                child.QueueFree();
            }

            foreach (Node child in _pose.GetChildren())
            {
                if (child == _mounts)
                    continue;
                _pose.RemoveChild(child);
                child.QueueFree();
            }
        }

        Color belly = bodyColor.Lightened(0.45f);
        Color chin = bodyColor.Lightened(0.15f);   // lower-front bulge reads a touch lighter than the body
        Color footColor = bodyColor.Darkened(0.12f);
        Color stemColor = bodyColor.Darkened(0.15f);

        RosterRow row = Roster[avatarKey];

        // Where the geometry comes from, and the ONLY thing the greybox changes about this
        // method. Everything below it — the harvest, the tint, the rest-pose measurement, the
        // face binding — is shared, which is the entire bet of GREY-1: satisfy the part contract
        // and the whole procedural animation layer drives the primitive with no new pose code.
        //
        // A glTF model's own export convention faces +Z with left/right mirrored versus this
        // rig's -Z-forward convention (a Blender/glTF forward-axis artifact, verified with the
        // AvatarGlbProbe tool against the raw glTF node transforms). A single 180 deg yaw on the
        // instantiated root corrects every part at once — round blob shapes are unaffected by the
        // flip, only their placement is. Whole-figure exports share it: their eyes and mouth sit at
        // +Z in the exported glTF too. The greybox has no exporter behind it and is authored directly
        // in this rig's space, so it must NOT be flipped.
        //
        // THE LOAD-FAILURE FALLBACK (INTEG-1, Talon's ruling). A row that asks for a file and does
        // not get one is served the code-built primitive instead, and the ROW is swapped with it —
        // not just the geometry. That matters: the substituted body has the primitive's part
        // contract, the primitive's declared absences and the part-rig face profile, and it is
        // authored in this rig's space so it must not take the glTF yaw flip. Serving the
        // primitive's meshes through the failed row's declarations would have been a body wearing
        // another model's paperwork.
        Node3D model;
        if (row.Build == Build.PrimitiveParts)
        {
            // Which primitive is the ROW's to say (MOVE-3g). Null is the shipped fallback body,
            // so the greybox_primitive row reads exactly as it did before greybox_classic existed.
            model = (row.PrimitiveBuilder ?? GreyboxAvatarBody.Build)();
        }
        else if (ModelSceneFor(avatarKey) is PackedScene scene)
        {
            model = scene.Instantiate<Node3D>();
            // Build.AuthoredRig takes NEITHER correction (ANIM-M3). ANIM-M2 baked the -Z facing into
            // build_greybox.py's to_blender(), so the file already faces the way this rig does; a
            // mirror on top of it is a second correction for one facing and renders the body
            // backwards, and a 180 deg yaw would additionally fold itself into every node's rest
            // basis, which is the defect MirrorAboutY exists to avoid.
            if (row.Build != Build.AuthoredRig)
            {
                if (row.AxisAlignedParts)
                    MirrorAboutY(model);
                else
                    model.RotationDegrees = new Vector3(0, 180, 0);
            }
        }
        else
        {
            GD.PushError(
                $"AvatarVisual: '{row.ModelPath}' failed to load for avatar key '{avatarKey}'; " +
                $"falling back to the code-built primitive ({GreyboxAvatarBody.SourceLabel}).");
            row = Roster[PrimitiveFallbackAvatarKey];
            model = GreyboxAvatarBody.Build();
        }

        // Everything hangs off _body so squash/stretch scales around the feet, and _body hangs off
        // _pose so the lean/roll/hop reach the mounted anchors too. See _pose.
        //
        // AFTER the model resolves, not before, and that ordering is load-bearing: the load-failure
        // fallback above swaps the ROW as well as the geometry (INTEG-1), so a Build.AuthoredRig row
        // whose file did not load is served the PRIMITIVE's row — and the primitive needs this node.
        // Keying the decision on the row as it stood before the load would have left the fallback
        // body with no _body to harvest onto, which renders as four stray meshes standing at the hip
        // height and looks exactly like a broken export.
        //
        // Skipped for Build.AuthoredRig, where Pose and Body arrive inside the .glb: building a
        // second pair here and then abandoning them would leave two nodes named "Pose" under one
        // parent, which is a silent FindChild ambiguity rather than an error.
        if (row.Build != Build.AuthoredRig)
        {
            _body = new Node3D { Name = "Body" };
            _pose.AddChild(_body);
        }

        string modelPath = row.ModelPath;
        ResolvedModelPath = modelPath;
        ResolvedFromFile = row.Build != Build.PrimitiveParts;
        _declaredAbsentParts = row.AbsentParts;

        // The runtime proof that the game loads the file. A source constant cannot settle this and
        // neither can a roster table: what a process renders is what it managed to resolve, so the
        // process says so out loud, once per distinct answer.
        if (_loggedResolutions.Add($"{avatarKey}|{modelPath}"))
        {
            GD.Print($"[avatar] key '{avatarKey}' resolved to {row.Build} <- {modelPath}" +
                     (row.AbsentParts.Count > 0
                         ? $"   (declared absent: {string.Join(", ", row.AbsentParts)})"
                         : ""));
        }

        // Build.AuthoredRig parents the model under the CROUCH node rather than under this one,
        // because the authored `Rig` root carries the legs as well as the pose and it is the crouch
        // that must lower the hips while the feet stay planted (RIG-1). Every other build adds it
        // here and then discards the scaffolding.
        if (row.Build == Build.AuthoredRig)
            _crouch.AddChild(model);
        else
            AddChild(model);

        if (row.Build == Build.AuthoredRig)
        {
            // chin/footColor are deliberately NOT passed: BODY-2 replaced this build's per-part
            // deltas with a height-derived ramp, and handing it two precomputed shades it no longer
            // reads is how a parameter goes on looking load-bearing after it stopped being so.
            // The harvest path below still uses both.
            BuildAuthoredRig(model, row, bodyColor, modelPath);
            MarkOwnBodyLayer(_body);
            MeasureRestPose();
            return;
        }

        var parts = new Dictionary<string, MeshInstance3D>();
        CollectMeshInstances(model, parts);

        // Body-color parts: reparent under _body (inherits squash/lean/breathe) and
        // tint per-player. BackFiller is the single authored butt-cheek filler (Art
        // Bible §6.3) — same body color so the volume reads flush, not two-balls-stuck-on.
        // (The original harvested creature shipped exactly one BackFiller mesh; a second "_002"
        // was never exported, and requesting it error-logged on every avatar spawn.)
        // --- THE LOWER TORSO comes first, because the waist is placed off its measured top edge.
        //     Hips used to be a declared-absent empty mesh on the greybox; RIG-1 promoted it to real
        //     lower-torso geometry, which is what let the trunk split in two without a new part name.
        MeshInstance3D hips = ReparentTinted(parts, "Hips", _body, chin, modelPath);

        // --- THE WAIST. A joint has no mesh, so this node is built rather than harvested — but it is
        //     PLACED off the harvested geometry (the lower torso's own top edge) rather than off a
        //     constant, so the pivot cannot drift from the seam it bends at on a rig whose hips sit
        //     somewhere else. Only driven when the rig declared both a real lower torso and a real
        //     head: bending the top half of a single round blob body about a mid-height pivot
        //     would tear a seam that character does not have. See HasWaist.
        _waist = new Node3D { Name = "Waist" };
        _body.AddChild(_waist);
        if (hips.Mesh != null)
        {
            Aabb hipsLocal = hips.GetAabb();
            _waist.GlobalPosition = hips.GlobalTransform * new Vector3(0f, hipsLocal.End.Y, 0f);
        }

        // Everything above the waist hangs off it: the upper torso, the head, the arms, the face.
        ReparentTinted(parts, "Torso", _waist, bodyColor, modelPath);
        ReparentTinted(parts, "Belly", _waist, belly, modelPath);
        ReparentTinted(parts, "Tail", _body, belly, modelPath);
        ReparentTinted(parts, "BackFiller", _waist, bodyColor, modelPath);

        // --- THE HEAD, and THE VALUE BREAK THAT IS NOT UNCONDITIONAL (AVATAR-5). An optional part:
        //     a blob body's head is its one round blob and a whole figure is one authored mesh, so
        //     absence falls back to a face that rides the upper torso exactly as it did.
        //
        //     The tint used to be `Lightened(0.10f)` for every model that had the part, reasoning
        //     from ART-BIBLE §3: separate by VALUE first, and a head that is a distinct volume
        //     should read as one. That reasoning is intact — but it was written when having a part
        //     named `Head` and having a distinct head volume were the same claim, and AVATAR-1C/1D
        //     ended that: the greybox's head is the top slice of ONE revolved profile with no
        //     crease and no step anywhere on it. Painted a value lighter, that slice's node seam
        //     renders as a hard horizontal band across a smooth dome — a manufactured edge the
        //     silhouette does not have. §3's actual mandate is that things separate from the floor
        //     and from EACH OTHER in value; nothing in it asks one continuous surface to be split
        //     into two, and doing so costs legibility rather than buying it.
        //
        //     So the value break is DECLARED per row, next to the path it is a claim about, exactly
        //     as absence is (see RosterRow.HeadIsDistinctVolume). The default is TRUE — a model
        //     that bothers to ship a separate `Head` node normally has a separate head, and the
        //     honest default is the one that keeps §3 rather than the one that quietly drops it for
        //     everything. No row on the roster is affected today except the two greybox rows:
        //     MEASURED off the shipped bytes, no harvested .glb this repo has carried has had a
        //     `Head` node at all, so the break has never fired anywhere but here.
        //
        //     Either way the head is TINTED — never left on its authored material, which would
        //     leave one player's head grey while the rest of them took the palette colour. Sharing
        //     Torso's exact colour also shares Torso's exact material instance (Mat caches per
        //     colour), which is ART-BIBLE §4.2 gaining an entry, not losing one.
        _head = TakeOptionalPart(parts, "Head", _waist);
        if (_head != null)
            _head.MaterialOverride = Mat(row.HeadIsDistinctVolume ? bodyColor.Lightened(0.10f) : bodyColor);
        Node3D faceParent = _head ?? _waist;

        _leftArm = ReparentTinted(parts, "ArmL", _waist, bodyColor, modelPath);
        _rightArm = ReparentTinted(parts, "ArmR", _waist, bodyColor, modelPath);
        _leftArmRest = _leftArm.Position;
        _rightArmRest = _rightArm.Position;
        _leftArmRestRot = _leftArm.Rotation;
        _rightArmRestRot = _rightArm.Rotation;
        _leftArmRestScale = _leftArm.Scale;
        _rightArmRestScale = _rightArm.Scale;

        // --- THE FOREARMS, hanging off their own upper arms so an elbow rotation pivots at the elbow.
        //     Reparented AFTER the upper arms are in place, because keepGlobalTransform resolves
        //     against the parent's live transform.
        _forearmL = TakeOptionalPart(parts, "ForearmL", _leftArm);
        _forearmR = TakeOptionalPart(parts, "ForearmR", _rightArm);
        if (_forearmL != null)
            _forearmL.MaterialOverride = Mat(bodyColor.Darkened(0.08f));
        if (_forearmR != null)
            _forearmR.MaterialOverride = Mat(bodyColor.Darkened(0.08f));
        _forearmLRestRot = _forearmL?.Rotation ?? Vector3.Zero;
        _forearmRRestRot = _forearmR?.Rotation ?? Vector3.Zero;

        // Face details keep the artist's authored colors — never player-tinted.
        _leftEye = ReparentAsIs(parts, "EyeL", faceParent, modelPath);
        _rightEye = ReparentAsIs(parts, "EyeR", faceParent, modelPath);
        _leftEyeRestScale = _leftEye.Scale;
        _rightEyeRestScale = _rightEye.Scale;
        ReparentAsIs(parts, "BlushL", faceParent, modelPath);
        ReparentAsIs(parts, "BlushR", faceParent, modelPath);

        // Legs — the THIGH under the FootL/FootR names. THE HARVEST DIALECT, deliberately left
        // alone by ANIM-M2b's rename: the harvested part rigs still speak it,
        // and only the AUTHORED path below moved to ThighL/ThighR (see GreyboxAvatarBody for the
        // compromise). Children of the CROUCH node rather than of the body, so squash does not lift
        // them and the landing absorb does lower them with the hips.
        _leftThigh = ReparentTinted(parts, "FootL", _crouch, footColor, modelPath);
        _rightThigh = ReparentTinted(parts, "FootR", _crouch, footColor, modelPath);
        _leftThighRestY = _leftThigh.Position.Y;
        _rightThighRestY = _rightThigh.Position.Y;
        _leftThighRestRot = _leftThigh.Rotation;
        _rightThighRestRot = _rightThigh.Rotation;

        // --- THE SHINS, hanging off their own thighs so a knee rotation pivots at the knee. Darker
        //     than the thigh by a deliberate margin: the knee is the joint this whole packet exists
        //     to make visible, and a value break at the joint is what makes a bend legible at print
        //     size (ART-BIBLE §3 — value before hue).
        _shinL = TakeOptionalPart(parts, "ShinL", _leftThigh);
        _shinR = TakeOptionalPart(parts, "ShinR", _rightThigh);
        if (_shinL != null)
            _shinL.MaterialOverride = Mat(bodyColor.Darkened(0.24f));
        if (_shinR != null)
            _shinR.MaterialOverride = Mat(bodyColor.Darkened(0.24f));
        _shinLRestRot = _shinL?.Rotation ?? Vector3.Zero;
        _shinRRestRot = _shinR?.Rotation ?? Vector3.Zero;

        HasKnees = _shinL != null && _shinR != null;
        HasElbows = _forearmL != null && _forearmR != null;
        HasWaist = _head != null && hips.Mesh != null;

        // Springy sprout on the crown: a stem topped with two little seedling leaves.
        // Pure secondary-motion joy. Pivot sits at the stem's authored base (its mesh
        // AABB bottom) so it swings from where it actually meets the head, not an
        // arbitrary guess. The leaves keep their authored leaf-green (never player-tinted),
        // so every sprouted body reads as the same little sprout regardless of body color.
        _sprout = new Node3D { Name = "Sprout" };
        _body.AddChild(_sprout);
        if (parts.TryGetValue("Stem", out MeshInstance3D? stem))
        {
            Aabb localAabb = stem.GetAabb();
            _sprout.GlobalPosition = stem.GlobalTransform * new Vector3(0, localAabb.Position.Y, 0);
        }
        ReparentTinted(parts, "Stem", _sprout, stemColor, modelPath);
        ReparentAsIs(parts, "LeafL", _sprout, modelPath);
        ReparentAsIs(parts, "LeafR", _sprout, modelPath);

        // Whatever's left is empty glTF scaffolding (export-root/group nodes) — discard it.
        model.QueueFree();

        MarkOwnBodyLayer(_body);
        MeasureRestPose();
    }

    /// <summary>Builds the pose rig once per instance and never again. Idempotent and safe to call
    /// from a property getter, because a caller can legitimately ask for
    /// <see cref="Mounts"/> before the first <see cref="BuildAppearance"/> — <c>SandboxAvatar</c>
    /// does exactly that ordering deliberately (the visual is built first so everything else can be
    /// measured off it, but the anchors are created after).</summary>
    private void EnsurePoseRig()
    {
        if (_pose != null)
            return;

        // Crouch → Pose → Mounts. The crouch is outermost because it must carry the legs as well as
        // the body: it is the node that lets the hips drop while the feet stay planted.
        _crouch = new Node3D { Name = "Crouch" };
        AddChild(_crouch);
        _pose = new Node3D { Name = "Pose" };
        _crouch.AddChild(_pose);
        _mounts = new Node3D { Name = "Mounts" };
        _pose.AddChild(_mounts);
    }

    /// <summary>True while <see cref="_pose"/> is a node that arrived inside the model rather than
    /// one this class built — see <see cref="Build.AuthoredRig"/>.</summary>
    private bool _poseIsAuthored;

    /// <summary>The clip layer for this body, or null when the flag is off, the row is not
    /// <see cref="Build.AuthoredRig"/>, or the library failed to build. Null is the shipped default
    /// and it means the procedural gait owns the base pose exactly as before.</summary>
    private Anim.AvatarClipDirector? _clips;

    /// <summary>What the clip library proposed this frame. Zero when there is no clip layer, which is
    /// what makes every read of it below a no-op on the procedural path.</summary>
    private Anim.AvatarClipPose _clipPose;

    private Anim.AvatarActionState _clipAction = Anim.AvatarActionState.Locomotion;
    private Anim.AvatarHoldState _clipHold = Anim.AvatarHoldState.Empty;
    private Anim.AnimationLodTier _clipTier = Anim.AnimationLodTier.Full;

    /// <summary>How long <see cref="_clipAction"/> has been the answer, seconds. Local, and it
    /// carries no information a peer could disagree about — every transition into the state it times
    /// is a pure function of replicated facts.</summary>
    private float _clipStateElapsedSec;

    /// <summary><b>The replicated tick this client is RENDERING</b>, written each frame by
    /// <c>SandboxAvatar.SetNetworkTick</c>. On a remote proxy this is the interpolated render tick,
    /// not the newest received one — the body on screen is <c>SnapshotBuffer.InterpDelayTicks</c>
    /// behind, and seeking a clip to the newest tick would put the pose ahead of the position it
    /// belongs to. Negative means "this build has no network clock", which is every non-networked
    /// lab and every test fixture; the seek then falls back to local elapsed time.</summary>
    private double _networkTick = -1.0;

    /// <summary>The render tick at which <see cref="_clipAction"/> became true. Stamped locally from
    /// the replicated clock rather than sent — see <c>ClipTimeAnchor</c> for why that is still a
    /// replicated anchor and not animation state on the wire.</summary>
    private double _clipEntryTick = -1.0;

    /// <summary>A replicated stagger. No shipping cause raises one yet — <c>IncapacityState</c> has
    /// Active / KnockedOut / Frozen and a reserved 3 — so this seam exists, is driven by
    /// <c>SandboxSelfTest</c>, and is the single line a stagger cause would set. Declared rather
    /// than omitted so the state machine's rows are complete (BEHAVIOR-BIBLE §1: every state
    /// declares entry, behaviour and exit) instead of one row being invisible.</summary>
    private bool _staggering;

    /// <summary>The stroke's replicated normalised progress, forwarded through
    /// <see cref="SetSwing"/>. This is the swing's clip-time anchor and it is strictly better than
    /// a locally-stamped entry tick: it is correct on a joining client's very FIRST frame rather
    /// than on its first observed transition.</summary>
    private float _swingProgress01;

    /// <summary>True when the authored clip library is driving this body's base pose this frame.
    /// Read by <see cref="Animate"/> at each of the four channels the clips own.</summary>
    public bool ClipsDriving => _clips != null;

    /// <summary>Which authored clip state this body is in — a readout for the capture harness and
    /// the labs, never an input to anything.</summary>
    public Anim.AvatarActionState ClipAction => _clipAction;

    /// <summary>Which upper-body override is layered this frame. Readout only.</summary>
    public Anim.AvatarHoldState ClipHold => _clipHold;

    /// <summary>This body's animation LOD tier this frame. Readout only.</summary>
    public Anim.AnimationLodTier ClipLodTier => _clipTier;

    /// <summary>
    /// <b>The replicated clock this body's clip time is anchored to.</b> Written every rendered frame
    /// by <c>SandboxAvatar</c> from whichever tick its own role actually renders — the server's own
    /// counter on the authority, the reconciled-plus-predicted count on the owner, the interpolated
    /// render tick on a remote proxy. Presentation only: nothing downstream of it decides anything.
    /// </summary>
    public void SetNetworkTick(double renderTick) => _networkTick = renderTick;

    /// <summary>Raise or clear a replicated stagger — see <see cref="_staggering"/>. Idempotent.</summary>
    public void SetStagger(bool staggering) => _staggering = staggering;

    /// <summary>
    /// <b>This frame's animation LOD tier, from the camera distance.</b> Called by
    /// <c>SandboxAvatar</c>; a body nobody tells stays at <see cref="Anim.AnimationLodTier.Full"/>,
    /// which is the safe default — a lab or a test fixture that never sets a distance gets every
    /// frame rather than a frozen body it cannot explain.
    /// </summary>
    public void SetAnimationLod(float cameraDistanceM, bool isLocalPlayer)
    {
        Anim.AnimationLodTier next =
            Anim.AvatarAnimationLod.TierFor(cameraDistanceM, isLocalPlayer, _clipTier);
        // A tier change SAYS SO, once per change per body. A still frame cannot show that a distant
        // body dropped to half rate — that is the whole difficulty of photographing an optimisation —
        // so the evidence is a measured log line naming the tier and the distance that produced it,
        // which is the same discipline the [avatar] resolution line already uses.
        if (next != _clipTier && _clips != null)
        {
            GD.Print($"[avatar-lod] {Name}: {_clipTier} -> {next} at {cameraDistanceM:F1} m " +
                     $"({Anim.AvatarAnimationLod.FramesPerAdvance(next)} frame(s) per tree advance)");
        }
        _clipTier = next;
    }

    /// <summary>
    /// <b>Where in the current action clip this body should be.</b> Negative means "no replicated
    /// opinion" — locomotion, whose phase is integrated locally and is not a replicated fact.
    ///
    /// <para>Stamps the entry tick on the frame the state changes, then answers
    /// <c>(now − entryTick)</c> on every frame after, not only on the edge. Answering every frame is
    /// what makes a client that received the change LATE converge rather than merely start in the
    /// right place, and it is what lands a client that JOINED mid-action on the right frame — its
    /// first observed frame of the state stamps an entry tick in the past, because the tick clock is
    /// the server's and not this process's uptime.</para>
    /// </summary>
    private float ClipSeekSecondsFor(Anim.AvatarActionState state)
    {
        if (_clips == null || state == Anim.AvatarActionState.Locomotion)
            return -1f;
        string? clip = Anim.AvatarClipNames.FullBodyClipFor(state);
        if (clip == null)
            return -1f;
        if (_networkTick < 0.0)
            return -1f;   // no network clock in this build: the transition's own reset is the answer
        return Anim.ClipTimeAnchor.SecondsFromTicks(
            _clipEntryTick, _networkTick, MpFoundation.Net.NetProfile.TickDelta,
            _clips.LengthOf(clip), !Anim.AvatarClipNames.IsOneShot(clip));
    }

    /// <summary>
    /// <b>The upper-body override, derived from the same replicated signals the procedural arm poses
    /// already key off.</b> Ordered by which gesture wins when two are true at once: a stroke in
    /// flight outranks a ready pose, a ready pose outranks a carry, and empty hands are what is
    /// left.
    ///
    /// <para><b>The two carries are not distinguished yet, and that is honest rather than lazy.</b>
    /// CARRY-1 names <c>Carry_Handle</c> and <c>Carry_Armful</c> and both are authored, but
    /// the carry flag was a single bool — the carry SHAPE is not a replicated fact on this
    /// branch. Everything carried takes the armful until it is, and the seam is this one line.</para>
    /// </summary>
    /// <summary>
    /// <b>Adopt an action state and stamp when it began.</b> The entry tick is taken from the
    /// REPLICATED clock rather than from local elapsed time, which is the whole trick: a client that
    /// joins mid-swing observes the transition on its first frame and stamps a tick that is already
    /// in the past, so <see cref="ClipSeekSecondsFor"/> answers with the frame the action really is
    /// at instead of with zero.
    /// </summary>
    private void EnterClipState(Anim.AvatarActionState next, float delta)
    {
        if (next == _clipAction && _clipSeen)
        {
            _clipStateElapsedSec += delta;
            return;
        }

        // FIRST SIGHT IS NOT AN ENTRY. A client that spawns a body which is ALREADY knocked out did
        // not watch it fall; stamping the entry tick at "now" would play the fall from frame 0 and
        // render a body standing up in order to collapse again. A state observed as already-true on
        // the first frame is entered at its END — which for a one-shot that holds its last frame is
        // exactly the pose the body has been in since before this client existed.
        //
        // This is the general rule the tick pair needs and the wire does not carry: no shipping
        // MoveState field says WHEN an incapacity began, and adding one is a codec change this
        // packet is not authorised to make. Where a replicated anchor DOES exist it is used instead
        // and it is strictly better — the net stroke's Progress01 (see _swingProgress01) and the
        // skid's own replicated SkidRemaining (see _skidRemainingSec).
        bool firstSight = !_clipSeen;
        _clipSeen = true;
        _clipAction = next;
        _clipStateElapsedSec = 0f;

        float backdateSec = 0f;
        if (firstSight && next != Anim.AvatarActionState.Locomotion
            && Anim.AvatarClipNames.FullBodyClipFor(next) is { } clip && _clips != null)
        {
            backdateSec = _clips.LengthOf(clip);
        }
        if (next == Anim.AvatarActionState.Skid && _skidRemainingSec > 0f)
        {
            // The one action state whose elapsed time IS on the wire. SkidRemaining is a MoveState
            // field the motor simulates identically on the server, in owner prediction and in replay,
            // so every peer can compute how far into the brake this body is without being told.
            backdateSec = Mathf.Max(0f, SkidDurationSec - _skidRemainingSec);
        }

        _clipEntryTick = _networkTick - (backdateSec / MpFoundation.Net.NetProfile.TickDelta);
    }

    /// <summary>True once this body has advanced at least one clip frame — see
    /// <see cref="EnterClipState"/> for why "first sight" is treated differently from "entry".</summary>
    private bool _clipSeen;

    /// <summary>The replicated <c>MoveState.SkidRemaining</c>, forwarded by
    /// <see cref="SetSkidding(bool, float)"/>.</summary>
    private float _skidRemainingSec;

    /// <summary>The brake's hard cap, seconds — <c>AvatarMotor.SkidMaxSec</c> read back through the
    /// one place this class needs it, never re-typed. It is a CAP rather than a duration (a skid also
    /// exits on speed, on re-alignment and on leaving the ground), so backdating against it is exact
    /// only for a brake that runs its full length and is an over-estimate otherwise — bounded by the
    /// clip's own 0.367 s, which is half the cap, so the seek clamps to the clip's end either way and
    /// the error is invisible.</summary>
    private static float SkidDurationSec => MpFoundation.Net.AvatarMotor.SkidMaxSec;

    private Anim.AvatarHoldState DeriveHoldState()
    {
        if (_swinging || _swingBlend > 0.001f)
            return Anim.AvatarHoldState.NetSwing;
        if (_aiming)
            return Anim.AvatarHoldState.NetReady;
        // MERGE NOTE (PLAYTEST-1 trunk, 2026-08-22). This read a carrying bool that CARRY-1
        // deleted, replacing it with the CarryPose enum. git did NOT flag this: ANIM-M3 added this
        // method on its own branch and CARRY-1 removed the field on its own, so the two edits never
        // touched the same lines and only the compiler caught it.
        //
        // Widening it is not a repair, it is the completion ANIM-M3 was already written for:
        // AvatarHoldState has declared CarryHandle = 3 since that branch, with the doc comment
        // "(CARRY-1)" on it, and a single boolean could never reach it — every carry, handle or
        // armful, mapped to CarryArmful. With the enum in hand the handle carry finally selects its
        // own clip, which is what both branches independently assumed would happen.
        if (_carry == CarryPose.Handle)
            return Anim.AvatarHoldState.CarryHandle;
        if (_carry == CarryPose.Armful)
            return Anim.AvatarHoldState.CarryArmful;
        return Anim.AvatarHoldState.Empty;
    }

    /// <summary>
    /// <b>Takes <see cref="_pose"/> back from a model that is about to be freed.</b>
    ///
    /// <para>Under <see cref="Build.AuthoredRig"/> the pose node lives inside the <c>.glb</c>, and
    /// <see cref="BuildAppearance"/>'s teardown frees the model whenever a synced avatar key catches
    /// up. <see cref="_mounts"/> — and therefore every held prop's binding, every carry anchor and
    /// the stow anchor — hangs off it. Without this, an avatar-key change drops every held item on
    /// the floor with no error, which is the exact failure ANIM-M0 §3.1 singled out as the one
    /// genuinely subtle break in option (a).</para>
    ///
    /// <para>Idempotent and cheap: on every build that is not coming out of an authored rig it does
    /// nothing at all.</para>
    /// </summary>
    private void RestoreBuiltPose()
    {
        // The clip layer belongs to the model that is about to go. Dropped first so nothing holds a
        // reference to a disposed AnimationTree.
        _clips = null;
        _clipPose = default;

        if (!_poseIsAuthored)
            return;

        // Rebuild the C#-owned pose FIRST, then move the mounts onto it, then let the caller's
        // teardown free the model. Local transform preserved rather than global: the authored pose
        // was reset to identity at the top of BuildAppearance, so the two spaces agree, and
        // preserving the LOCAL transform is what keeps a carry anchor's authored rest offset exact
        // instead of round-tripping it through a global-space matrix.
        Transform3D mountsLocal = _mounts.Transform;
        var rebuilt = new Node3D { Name = "Pose" };
        _crouch.AddChild(rebuilt);
        _mounts.Reparent(rebuilt, keepGlobalTransform: false);
        _mounts.Transform = mountsLocal;
        _pose = rebuilt;
        _poseIsAuthored = false;
    }

    /// <summary>
    /// <b>Build.AuthoredRig: cache the imported hierarchy instead of rebuilding it.</b>
    ///
    /// <para>Every <c>Reparent(keepGlobalTransform: true)</c> the harvest performed is gone, and so
    /// is the <c>model.QueueFree()</c> that ended it — the model IS the rig now. What is left is a
    /// name lookup per joint, a material override per tinted mesh, and the two things the asset
    /// deliberately does not carry: <see cref="_mounts"/> (re-parented onto the authored pose, see
    /// <see cref="RestoreBuiltPose"/>) and the sprout stub the harvest path animates.</para>
    ///
    /// <para><b>A missing part is loud, exactly as it is under the harvest</b>, and for the same
    /// reason: a part missing from a model almost always means a broken export, and this error is
    /// the only thing that catches it. Declared absences (see <c>RosterRow.AbsentParts</c>) stay
    /// silent.</para>
    /// </summary>
    private void BuildAuthoredRig(
        Node3D model, RosterRow row, Color bodyColor, string modelPath)
    {
        // --- The three authored empties that replace the C#-built pose rig ------------------------
        if (model.FindChild("Pose", recursive: true, owned: false) is not Node3D authoredPose
            || model.FindChild("Body", recursive: true, owned: false) is not Node3D authoredBody)
        {
            GD.PushError(
                $"AvatarVisual: '{modelPath}' is a Build.AuthoredRig row but carries no Pose/Body " +
                "empties; the .glb hierarchy is not the rig. Falling back to the built pose.");
            return;
        }

        Transform3D mountsLocal = _mounts.Transform;
        Node3D builtPose = _pose;
        _mounts.Reparent(authoredPose, keepGlobalTransform: false);
        _mounts.Transform = mountsLocal;
        _pose = authoredPose;
        _poseIsAuthored = true;
        // The C#-built Pose is now empty and would otherwise sit beside the authored one under the
        // crouch, where a later FindChild("Pose") could resolve to either.
        _crouch.RemoveChild(builtPose);
        builtPose.QueueFree();

        _body = authoredBody;
        _waist = model.FindChild("Waist", recursive: true, owned: false) as Node3D;

        // --- The tint, part by part ----------------------------------------------------------------
        //
        // BODY-2 (2026-08-28): the per-part deltas that used to be written inline here are now
        // DERIVED FROM EACH PART'S HEIGHT — see ApplyValueRamp. Every part is still looked up in
        // exactly the same order and every field is still assigned in exactly the same place; the
        // only change is that the colour arrives in a second pass, once every part's height is
        // known. The parts are collected into `ramped` as they are found, which is why the
        // MaterialOverride assignments have moved off these lines rather than changing value.
        var ramped = new System.Collections.Generic.List<MeshInstance3D>(18);

        MeshInstance3D? hips = AuthoredMesh(model, "Hips", modelPath);
        Ramped(ramped, hips);
        MeshInstance3D? torso = AuthoredMesh(model, "Torso", modelPath);
        Ramped(ramped, torso);

        _head = AuthoredMesh(model, "Head", modelPath);
        // The head is on the ramp only when the row declares a distinct volume. On a body that is
        // one continuous revolved surface there is no crease for ART-BIBLE 3's tonal band to land
        // on, and lifting the top of it just reads as a lighting bug — which is the case AVATAR-5
        // had to switch the band off for. boxkid, the only AuthoredRig row today, declares TRUE.
        //
        // A head that is NOT a distinct volume takes THE TORSO'S colour, and after ApplyValueRamp
        // rather than here. That is not a nicety: the claim is "one continuous surface reads as one
        // surface", and the torso is now ramped, so tinting the head with the raw bodyColor would
        // leave it a few points off its own neck — which is precisely what a continuous surface
        // must never do, and precisely what the suite's forced-false control caught when this code
        // first shipped doing it.
        if (row.HeadIsDistinctVolume)
            Ramped(ramped, _head);

        _leftArm = AuthoredMesh(model, "ArmL", modelPath) ?? Inert("ArmL", _waist ?? _body);
        _rightArm = AuthoredMesh(model, "ArmR", modelPath) ?? Inert("ArmR", _waist ?? _body);
        Ramped(ramped, _leftArm as MeshInstance3D);
        Ramped(ramped, _rightArm as MeshInstance3D);
        _leftArmRest = _leftArm.Position;
        _rightArmRest = _rightArm.Position;
        _leftArmRestRot = _leftArm.Rotation;
        _rightArmRestRot = _rightArm.Rotation;
        // THE SCALE, and this line was MISSING until W7-3 (2026-08-30, found while diagnosing
        // Talon's note 4). The harvest path has captured it since CARRY-1 (see the sibling
        // assignments in BuildAppearance); this path — the one the PLAYED body takes, boxkid being
        // PreferredAvatarKey — captured Position and Rotation and stopped. SolveArmToTarget writes
        // `Basis.FromEuler(rest) * s.Root` SCALED BY this field, so on the authored rig every carry
        // and every aim silently re-scaled both arms to 1 the instant the pose engaged. It is a
        // latent bug rather than a live one only while BoxKid.glb happens to export unit arm scale,
        // which is a property of today's asset and not of this code; a re-export with an unapplied
        // scale would have made it a visible pop with nothing in the file to explain it.
        _leftArmRestScale = _leftArm.Scale;
        _rightArmRestScale = _rightArm.Scale;

        _forearmL = AuthoredMesh(model, "ForearmL", modelPath);
        _forearmR = AuthoredMesh(model, "ForearmR", modelPath);
        Ramped(ramped, _forearmL);
        Ramped(ramped, _forearmR);
        _forearmLRestRot = _forearmL?.Rotation ?? Vector3.Zero;
        _forearmRRestRot = _forearmR?.Rotation ?? Vector3.Zero;

        // THE LEG, AND ITS NAMES CHANGED ON 2026-08-21. Until ANIM-M2b the greybox's THIGHS were
        // called FootL/FootR — a grandfathered harvest-contract name on a body that had no feet.
        // Talon's ruling gave the thighs a name of their own and Foot* back to a real foot, so the
        // authored chain is ThighL -> ShinL -> FootL. The HARVEST path is deliberately NOT renamed:
        // the harvested part rigs still speak the old dialect, and the two
        // dialects already live on two different code paths — which is exactly what makes a
        // greybox-only rename cheap. See docs/BLENDER-EXPORT.md's migration note.
        _leftThigh = AuthoredMesh(model, "ThighL", modelPath) ?? Inert("ThighL", _crouch);
        _rightThigh = AuthoredMesh(model, "ThighR", modelPath) ?? Inert("ThighR", _crouch);
        Ramped(ramped, _leftThigh as MeshInstance3D);
        Ramped(ramped, _rightThigh as MeshInstance3D);
        _leftThighRestY = _leftThigh.Position.Y;
        _rightThighRestY = _rightThigh.Position.Y;
        _leftThighRestRot = _leftThigh.Rotation;
        _rightThighRestRot = _rightThigh.Rotation;

        _shinL = AuthoredMesh(model, "ShinL", modelPath);
        _shinR = AuthoredMesh(model, "ShinR", modelPath);
        Ramped(ramped, _shinL);
        Ramped(ramped, _shinR);
        _shinLRestRot = _shinL?.Rotation ?? Vector3.Zero;
        _shinRRestRot = _shinR?.Rotation ?? Vector3.Zero;

        // THE FEET (ANIM-M2b). Optional in exactly the way the shins are: no other row on the
        // roster carries one, and a body without them degrades to the leg that shipped rather than
        // to an exception. Tinted WITH the shins rather than against them — the joint this packet
        // makes visible is the ankle, and what makes it legible is the sole's ANGLE against the
        // ground, not a third value step. Under BODY-2's ramp they still get no step of their own —
        // they simply sit at the bottom of the ramp, which is the same statement made by height.
        _footL = AuthoredMesh(model, "FootL", modelPath);
        _footR = AuthoredMesh(model, "FootR", modelPath);
        Ramped(ramped, _footL);
        Ramped(ramped, _footR);
        _footLRestRot = _footL?.Rotation ?? Vector3.Zero;
        _footRRestRot = _footR?.Rotation ?? Vector3.Zero;

        // Face details keep the artist's authored colours — never player-tinted.
        _leftEye = AuthoredMesh(model, "EyeL", modelPath) ?? Inert("EyeL", _head ?? _body);
        _rightEye = AuthoredMesh(model, "EyeR", modelPath) ?? Inert("EyeR", _head ?? _body);
        _leftEyeRestScale = _leftEye.Scale;
        _rightEyeRestScale = _rightEye.Scale;

        HasKnees = _shinL != null && _shinR != null;
        HasElbows = _forearmL != null && _forearmR != null;
        HasWaist = _head != null && hips?.Mesh != null && _waist != null;

        // The sprout is a harvested-family part and this body declares it absent, but Animate writes
        // to the node unconditionally — so it gets an inert stub. Under _body so it is torn down
        // with everything else.
        _sprout = Inert("Sprout", _body);

        // --- THE PARTS THIS BUILD DOES NOT POSE, PROBED ANYWAY ------------------------------------
        //
        // The harvest asks for all sixteen contract parts because it reparents all sixteen; this
        // build only looks up the ones it holds a reference to, and eight of the contract — Belly,
        // Tail, BackFiller, BlushL, BlushR, Stem, LeafL, LeafR — would therefore never be ASKED
        // about at all. That is not the same as declaring them absent, and the difference is a real
        // check rather than bookkeeping: SandboxSelfTest's positive control clears the row's
        // declared-absent set and asserts that every part the file genuinely lacks becomes loud
        // again, which is what proves the declaration silences exactly the parts that are really
        // gone rather than everything. A build that never asks silences everything by omission and
        // passes that control having measured nothing.
        //
        // Tinted where present, for the same reason the harvest tints them: a part that exists must
        // not be left on its authored grey while the rest of the body takes the palette colour.
        Tint(AuthoredMesh(model, "Belly", modelPath), bodyColor.Lightened(0.45f));
        Tint(AuthoredMesh(model, "Tail", modelPath), bodyColor.Lightened(0.45f));
        Ramped(ramped, AuthoredMesh(model, "BackFiller", modelPath));
        AuthoredMesh(model, "BlushL", modelPath);
        AuthoredMesh(model, "BlushR", modelPath);
        Tint(AuthoredMesh(model, "Stem", modelPath), bodyColor.Darkened(0.15f));
        AuthoredMesh(model, "LeafL", modelPath);
        AuthoredMesh(model, "LeafR", modelPath);
        AuthoredMesh(model, "Mouth", modelPath);
        // BELLY, TAIL and STEM keep their authored deltas rather than joining the ramp, and that is
        // a decision rather than an omission: all three are part-rig parts this row declares absent
        // (they are null on boxkid, so nothing here runs today), and their deltas are SHAPE
        // statements — a pale belly, a pale tail, a darker stem — not height statements. The day an
        // AuthoredRig row grows one, a ramp would flatten the very thing the delta is for.
        // BlushL/R, EyeL/R and Mouth are the FACE and take no player tint at all, ramp or otherwise.

        // --- THE VALUE RAMP, applied once every part's height is known (BODY-2, scope item 4) -----
        ApplyValueRamp(ramped, model, bodyColor);

        // The continuous-surface head, resolved AFTER the ramp because it is defined in terms of the
        // torso's final colour rather than of the palette. The material INSTANCE is shared, not a
        // second Mat() of the same value: Mat caches per colour so the two would be the same object
        // anyway, and taking the torso's own reference states the relationship instead of relying on
        // the cache to keep re-deriving it.
        if (!row.HeadIsDistinctVolume && _head != null)
            _head.MaterialOverride = torso != null ? torso.MaterialOverride : Mat(bodyColor);

        // --- The clip layer -------------------------------------------------------------------------
        if (Anim.AvatarClipFlag.AuthoredClips)
        {
            _clips = Anim.AvatarClipDirector.TryBuild(model, modelPath);
            if (_clips != null && _loggedClipLayers.Add(modelPath))
                GD.Print($"[avatar-clips] authored clip library ON for {modelPath}");
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> _loggedClipLayers = new();

    /// <summary>One named mesh out of an authored rig. Loud when it is missing and not declared
    /// absent — the same asymmetry <see cref="TakePart"/> draws, for the same reason.</summary>
    private MeshInstance3D? AuthoredMesh(Node3D model, string name, string modelPath)
    {
        if (model.FindChild(name, recursive: true, owned: false) is MeshInstance3D found)
            return found;
        if (!_declaredAbsentParts.Contains(name))
        {
            _undeclaredMissingParts.Add(name);
            GD.PushError($"AvatarVisual: '{modelPath}' is missing expected part '{name}'.");
        }
        return null;
    }

    private void Tint(MeshInstance3D? mesh, Color color)
    {
        if (mesh != null)
            mesh.MaterialOverride = Mat(color);
    }

    // =============================================================================================
    // THE VERTICAL VALUE RAMP (BODY-2, 2026-08-28, scope item 4)
    // =============================================================================================
    //
    // Talon, on the classic greybox: "I love the darker green of the limbs where it's dark green
    // and then going to lighter green going up to the head. Can we push this a little more?"
    //
    // WHAT WAS THERE BEFORE, measured rather than characterised. BuildAuthoredRig did NOT tint the
    // box kid flat (the packet says it did — see the report's Corrections). It carried a per-part
    // delta list: Head +0.10, Hips +0.15, Torso/Arms 0, Forearms -0.08, Thighs -0.12, Shins/Feet
    // -0.24. That list has the RANGE Talon likes and is NOT MONOTONIC IN HEIGHT — the hips, which
    // sit below the torso, were the second LIGHTEST thing on the body, because that delta was
    // authored for a gumdrop's "lower-front bulge" and inherited by a body that has no bulge. So
    // the thing Talon described (dark at the bottom, light at the top) was never quite what the
    // code did, and no amount of scaling the old list would have made it so.
    //
    // WHAT IT IS NOW. One delta per part, derived from that part's own height in the rig, times one
    // knob. At BodyValueRampStrength = 1 the ENDPOINTS are exactly the old list's endpoints
    // (RampBottomDelta -0.24 at the lowest part, RampTopDelta +0.10 at the highest) — so "1.0" is
    // the current-classic-equivalent setting the packet asks to capture — and everything between
    // them is now monotonic instead of ad hoc.
    //
    // MEASURED OFF THE ASSET, NOT DECLARED. A table of part heights would be a second copy of the
    // .glb's pivots, and this repo has already learned what a second copy of build_greybox.py's
    // joint heights costs (BT-7 imports them rather than cloning them, for exactly this reason).
    // Each part's height is read from its own mesh AABB through its own transform chain, so if
    // Talon moves a joint in Blender the ramp moves with it and nothing here needs editing.

    /// <summary>The value delta applied to the LOWEST part on the body at
    /// <see cref="BodyValueRampStrength"/> = 1. Negative is darker. -0.24 is exactly what the
    /// shins and feet carried before BODY-2, so strength 1 preserves the bottom of the range that
    /// was already there rather than inventing a new one.</summary>
    public const float RampBottomDelta = -0.24f;

    /// <summary>The value delta applied to the HIGHEST part on the body at
    /// <see cref="BodyValueRampStrength"/> = 1. Positive is lighter. +0.10 is exactly what a
    /// distinct-volume head carried before BODY-2 (ART-BIBLE 3's tonal band), for the same
    /// reason.</summary>
    public const float RampTopDelta = 0.10f;

    /// <summary>
    /// What <see cref="BodyValueRampStrength"/> is when nobody has said otherwise.
    ///
    /// <para><b>1.8 since W7-3 (2026-08-30). It was 1.0, and 1.0 is why Talon asked twice.</b>
    /// This is the middle rung of the three BODY-2 bracketed and captured for him
    /// (<c>docs/qa/body-2/ramp/ramp-{1p0,1p8,2p8}</c>) — a value that was already photographed
    /// and offered, not a new one invented here.</para>
    ///
    /// <para><b>The two asks, in his words.</b> 2026-08-28, on the classic greybox:
    /// <i>"I love the darker green of the limbs where it's dark green and then going to lighter
    /// green going up to the head. Can we push this a little more?"</i> BODY-2 built the ramp for
    /// exactly that ask and then, correctly under its own packet, declined to pick the value —
    /// leaving the default at 1.0, the rung whose endpoints merely reproduce the range the body
    /// already had. So the thing he asked to have pushed was never pushed. 2026-08-30, having
    /// played it: <i>"Please make the player character 'ombre' like the other graybox was, where
    /// the feet are darker and go up toward the head which becomes lighter. This was a really
    /// cool look."</i> Same ask, second time, now with a name.</para>
    ///
    /// <para><b>Measured, not asserted.</b> At 1.0 the shipped body's own front plane runs
    /// head 113.1 luma → foot 86.9 in a headed capture
    /// (<c>docs/qa/W7-3/before/gait/01-standing-front.png</c>): a 26-luma spread that is real,
    /// monotonic, and — on the evidence of him asking again — not readable as a look. The whole
    /// ramp is a linear lerp times this number, so 1.8 is 1.8x that separation.</para>
    ///
    /// <para><b>Why not 2.8, the top rung.</b> 2.8 puts the lowest part at
    /// <c>Darkened(0.672)</c> — barely a third of the player's own colour — and canon fact 4
    /// makes darkness the game's central pressure with silhouette the read. A body whose legs go
    /// nearly black at night stops carrying the player's tint, which is the one thing the
    /// two-bot colour check exists to prove it still does. 1.8's <c>Darkened(0.432)</c> is
    /// plainly darker and still plainly the player's colour. <b>The knob is unchanged and 2.8 is
    /// one word away</b> — <c>--body-ramp 2.8</c> or <c>SAIL_BODY_RAMP=2.8</c>, no rebuild — and
    /// W7-3's report ships the A/B so that word can be said against a picture.</para>
    /// </summary>
    public const float DefaultBodyValueRampStrength = 1.8f;

    /// <summary>Strength 0 is the identity — a flat body-coloured figure — and is kept reachable
    /// deliberately: it is the control that proves any silhouette or legibility claim about the
    /// ramp is about the ramp.</summary>
    public const float MinBodyValueRampStrength = 0f;

    /// <summary>The ceiling. 4 puts the lowest part at <c>Darkened(0.96)</c>, i.e. very nearly
    /// black, and the highest at <c>Lightened(0.40)</c> — past this the knob stops describing a
    /// body and starts describing a gradient, and the deltas clamp anyway.</summary>
    public const float MaxBodyValueRampStrength = 4f;

    /// <summary>The environment variable that moves the ramp with no rebuild —
    /// <c>SAIL_BODY_RAMP=1.8</c>. Read FRESH on every appearance build (the same discipline
    /// <see cref="ResolveEnvAvatarKey()"/> uses and for the same reason: a harness that changes it
    /// between runs must not be fighting a cached value).</summary>
    public const string BodyValueRampEnvVar = "SAIL_BODY_RAMP";

    private static float? _bodyValueRampOverride;

    /// <summary>
    /// <b>THE KNOB. One number, two live sources, no rebuild needed for either.</b>
    ///
    /// <para>An explicit set (<c>--body-ramp</c>, applied once in <c>Boot</c> — the single-writer
    /// shape <c>NightBrightnessMul</c> already uses) wins; otherwise
    /// <see cref="BodyValueRampEnvVar"/> is read fresh; otherwise
    /// <see cref="DefaultBodyValueRampStrength"/>. Clamped to
    /// [<see cref="MinBodyValueRampStrength"/>, <see cref="MaxBodyValueRampStrength"/>] on the way
    /// in and again on the way out, so no path can hand the ramp a value the captures did not
    /// bracket.</para>
    ///
    /// <para><b>PRESENTATION ONLY, and that is a canon fact (4, the parity law), not a preference.</b>
    /// It moves the albedo of a player's own meshes and nothing else: no sight range, no collision,
    /// no server state, nothing on the wire. Two peers running different values still agree about
    /// every outcome — they disagree only about how dark somebody's shins look, which is the whole
    /// point of handing Talon a dial rather than a decision.</para>
    ///
    /// <para><b>BODY-2 does NOT pick the shipped value.</b> The packet says so and the report
    /// captures 1.0 / 1.8 / 2.8 for Talon to choose from. The default stays at the value that
    /// reproduces the range that was already on the body.</para>
    /// </summary>
    public static float BodyValueRampStrength
    {
        get
        {
            if (_bodyValueRampOverride is float set)
                return set;
            string? env = System.Environment.GetEnvironmentVariable(BodyValueRampEnvVar);
            if (!string.IsNullOrWhiteSpace(env)
                && float.TryParse(env, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                return Mathf.Clamp(parsed, MinBodyValueRampStrength, MaxBodyValueRampStrength);
            }
            return DefaultBodyValueRampStrength;
        }
        set => _bodyValueRampOverride =
            Mathf.Clamp(value, MinBodyValueRampStrength, MaxBodyValueRampStrength);
    }

    /// <summary>Drops the explicit override so the env var (or the default) is in charge again.
    /// For tests, which must not leak a strength into the next one — the xUnit suite runs in
    /// parallel and a leaked static is the classic source of a test that only fails in company.
    /// </summary>
    public static void ResetBodyValueRampStrength() => _bodyValueRampOverride = null;

    /// <summary>
    /// <b>The ramp curve itself: pure, engine-free enough to unit-test, and the only place the
    /// shape is decided.</b> <paramref name="normalizedHeight"/> is 0 at the lowest ramped part and
    /// 1 at the highest.
    ///
    /// <para>Linear in the normalized height, because the thing being described is a linear
    /// statement ("dark at the bottom, light at the top") and a curve here would be a second,
    /// unasked-for design decision hiding inside a tuning knob.</para>
    ///
    /// <para>Clamped to ±0.95 rather than ±1: <c>Darkened(1)</c> is pure black and
    /// <c>Lightened(1)</c> is pure white, and a part that has lost its hue entirely has stopped
    /// carrying the player's colour — which is the one thing the two-bot colour check exists to
    /// prove it still does.</para>
    /// </summary>
    public static float BodyValueRampDelta(float normalizedHeight, float strength)
    {
        float t = Mathf.Clamp(normalizedHeight, 0f, 1f);
        float s = Mathf.Clamp(strength, MinBodyValueRampStrength, MaxBodyValueRampStrength);
        return Mathf.Clamp(Mathf.Lerp(RampBottomDelta, RampTopDelta, t) * s, -0.95f, 0.95f);
    }

    /// <summary>Applies <paramref name="delta"/> as a value shift on <paramref name="bodyColor"/>:
    /// negative darkens, positive lightens, zero is the identity. One function so the sign
    /// convention is stated once.</summary>
    public static Color BodyColorAtRampDelta(Color bodyColor, float delta) =>
        delta < 0f ? bodyColor.Darkened(-delta)
        : delta > 0f ? bodyColor.Lightened(delta)
        : bodyColor;

    /// <summary>Adds a part to the ramp set. Null-tolerant (a declared-absent part is simply not on
    /// the ramp) and duplicate-tolerant (a body that names one mesh twice would otherwise skew its
    /// own normalization).</summary>
    private static void Ramped(System.Collections.Generic.List<MeshInstance3D> set, MeshInstance3D? mesh)
    {
        if (mesh != null && !set.Contains(mesh))
            set.Add(mesh);
    }

    /// <summary>Tints every collected part by its own height. Two passes, because the normalization
    /// cannot be known until every part has been measured — which is the whole reason the tint had
    /// to move out of the lookup calls.</summary>
    private void ApplyValueRamp(
        System.Collections.Generic.List<MeshInstance3D> ramped, Node3D rigRoot, Color bodyColor)
    {
        if (ramped.Count == 0)
            return;

        float strength = BodyValueRampStrength;
        var heights = new float[ramped.Count];
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < ramped.Count; i++)
        {
            heights[i] = RigLocalCentreY(ramped[i], rigRoot);
            // A declared-absent stub carries no Mesh and therefore no real height; it must not be
            // allowed to define an endpoint, because an empty node sitting at the rig origin would
            // silently anchor the bottom of the ramp at y=0 on a body whose feet are higher.
            if (ramped[i].Mesh == null)
                continue;
            min = Mathf.Min(min, heights[i]);
            max = Mathf.Max(max, heights[i]);
        }

        // Degenerate: nothing had geometry, or every part sits at one height. Both mean the ramp has
        // nothing to say, and the honest answer is the flat body colour rather than an arbitrary t.
        float span = max - min;
        if (!(span > 0.0001f))
        {
            foreach (MeshInstance3D mesh in ramped)
                mesh.MaterialOverride = Mat(bodyColor);
            return;
        }

        for (int i = 0; i < ramped.Count; i++)
        {
            float t = (heights[i] - min) / span;
            ramped[i].MaterialOverride =
                Mat(BodyColorAtRampDelta(bodyColor, BodyValueRampDelta(t, strength)));
        }
    }

    /// <summary>Where a part's centre sits, in <paramref name="rigRoot"/>'s own space.
    ///
    /// <para>Walks the transform chain by hand rather than reading <c>GlobalPosition</c>, and that
    /// is required, not stylistic: <see cref="BuildAppearance"/> runs during a build that may not be
    /// inside the scene tree yet, and a global transform read outside the tree is either zero or
    /// stale. The chain is a handful of multiplies on a sixteen-part body.</para>
    ///
    /// <para>The AABB CENTRE, not the origin: a box kid's <c>Head</c> node sits at the neck and its
    /// geometry is a cube above it, so the origin would put the head at the shoulders and hand the
    /// top of the ramp to nothing.</para></summary>
    private static float RigLocalCentreY(MeshInstance3D mesh, Node3D rigRoot)
    {
        Vector3 point = mesh.Mesh?.GetAabb().GetCenter() ?? Vector3.Zero;
        Node? node = mesh;
        while (node is Node3D node3d && node != rigRoot)
        {
            point = node3d.Transform * point;
            node = node3d.GetParent();
        }
        return point.Y;
    }

    /// <summary>Moves every <see cref="MeshInstance3D"/> under <see cref="_body"/> onto
    /// <see cref="OwnBodyRenderLayer"/> exclusively (off layer 1). See that constant's doc for
    /// why. A direct recursive walk rather than <see cref="CollectMeshInstances"/>'s
    /// name-keyed dictionary — that helper drops same-named siblings (multiple garment meshes
    /// share names on a fully-dressed whole figure), which would leave some meshes stuck on layer 1
    /// and never hideable. Feet (children of this rig, not of <c>_body</c>) are deliberately
    /// left on layer 1 — they sit near the ground, nowhere close to the pivot height that
    /// produces the clip.</summary>
    private static void MarkOwnBodyLayer(Node node)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.Layers = 0;
            mesh.SetLayerMaskValue(OwnBodyRenderLayer, true);
        }
        foreach (Node child in node.GetChildren())
            MarkOwnBodyLayer(child);
    }

    /// <summary>One measurement per roster key, not per spawn. The result is a property of the
    /// MODEL — both build paths place every part at its authored world placement, so two avatars
    /// wearing the same key measure identically — and the walk below reads real vertex data,
    /// which is worth doing exactly once per character per process.</summary>
    private static readonly Dictionary<string, (Aabb Body, Vector3? Eye)> _restPoseCache = new();

    /// <summary>Fills <see cref="BodyBounds"/> and <see cref="EyeCentre"/> from the geometry
    /// that was just built. Runs at the end of BOTH build paths, before anything animates, so
    /// what it records is the artist's rest pose rather than a frame of waddle.</summary>
    private void MeasureRestPose()
    {
        if (!_restPoseCache.TryGetValue(AvatarKey, out (Aabb Body, Vector3? Eye) measured))
        {
            Transform3D toRig = GlobalTransform.AffineInverse();
            var bounds = new Aabb();
            bool any = false;
            AccumulateBodyBounds(this, toRig, ref bounds, ref any);
            measured = (any ? bounds : new Aabb(), MeasureEyeCentre(toRig));
            _restPoseCache[AvatarKey] = measured;
        }
        BodyBounds = measured.Body;
        EyeCentre = measured.Eye;

        // The leg the whole gait is scaled against (MOVE-1). Measured, never declared — the same
        // discipline AvatarProportions applies to every other dimension, and the reason the day the
        // real cast lands nothing in LocomotionProfile is retyped.
        //
        // The 0.06 m floor separates "this rig has a leg" from "this rig's feet are stubs on the
        // floor" (a stub-footed blob body) or "this figure has no limbs at all" (a whole figure whose
        // feet are Inert nodes at the origin). Those get a third of their measured height, which is roughly
        // where a hip sits and keeps the derived stride sane instead of collapsing it to zero.
        float measuredLeg = Mathf.Max(_leftThighRestY, _rightThighRestY);
        _legLengthM = measuredLeg > 0.06f
            ? measuredLeg
            : Mathf.Max(0.10f, BodyBounds.Size.Y * 0.30f);

        MeasureLimbSegments();
        MeasureCarryTargets();
    }

    /// <summary>
    /// <b>Where each hand goes for each of the two carries, measured off this body</b> (CARRY-1).
    ///
    /// <para><b>The anchor point is not invented: it is where the carried thing actually is.</b>
    /// Every held object — the net, a log, a camera — hangs off the carry mount at
    /// <c>AvatarProportions.CarryAnchorRestLocal</c>, which is itself derived from this body's own
    /// measured crown and half-width. So "the hand grips the handle" is expressible as one
    /// subtraction rather than as a tuned offset, and it retargets to any body on the roster for
    /// free. A typed offset would be exactly the mistake this packet exists to undo: the carry
    /// offsets it replaced were absolute metres calibrated to the original rig's nub arms, and on a
    /// human-proportioned greybox they asked for a hand 0.469 m from a 0.38 m shoulder.</para>
    ///
    /// <para><b>Two frames, converted once.</b> The mount lives under the POSE node; the arms hang
    /// off the waist (or, on a whole figure, straight off the body). Both are at rest at the
    /// moment this runs — it is called from <c>BuildAppearance</c>, before a single
    /// <see cref="Animate"/> — so one transform converts between them and the answer is a constant
    /// for the life of the rig. At run time the two frames diverge slightly under lean and squash;
    /// that residual is a centimetre-scale drift of the grip, not a joint leaving its socket, and it
    /// is measured rather than assumed (<c>HandReachShortfallM</c>).</para>
    /// </summary>
    private void MeasureCarryTargets()
    {
        AvatarProportions proportions = AvatarProportions.For(BodyBounds, EyeCentre?.Y);
        Vector3 anchorRig = proportions.CarryAnchorRestLocal;

        Vector3 anchorInArmSpace = anchorRig;
        if (IsInsideTree() && _leftArm.GetParent() is Node3D armParent && armParent.IsInsideTree())
            anchorInArmSpace =
                (GlobalTransform.AffineInverse() * armParent.GlobalTransform).AffineInverse()
                * anchorRig;

        // THE HANDLE. One hand, on the grip, and the grip is the mount's own origin — a handled
        // prop runs forward from there, so the mount IS the butt of the handle. The RIGHT arm,
        // because the right arm is already the tool hand everywhere else in this file
        // (SwingHandOffset has always been the right arm's).
        _handleHandR = anchorInArmSpace - _rightArmRest;

        // THE ARMFUL. Both hands straddle the same load and sit UNDER it, which is what "supporting a
        // bulky load from underneath" is. Scaled off the body's own half-width rather than typed, for
        // the reason the anchor is. VALUE CALL (CARRY-1, mine): 0.62 of half-width apart and 0.26 of
        // half-width below the mount — on the greybox that is hands 12.2 cm either side of the load
        // and 5.1 cm under it, which clears the torso and still reads as two hands on one object.
        float half = Mathf.Max(0.05f, proportions.HalfWidthM);
        var spread = new Vector3(half * 0.62f, -half * 0.26f, 0f);
        _armfulHandL = anchorInArmSpace + new Vector3(-spread.X, spread.Y, 0f) - _leftArmRest;
        _armfulHandR = anchorInArmSpace + new Vector3(+spread.X, spread.Y, 0f) - _rightArmRest;
    }

    /// <summary>
    /// <b>Where each limb's middle joint is, measured off the rig rather than typed</b> (RIG-1) —
    /// the same discipline <see cref="AvatarProportions"/> applies to every other dimension.
    ///
    /// <para><b>The pair MUST sum to the whole limb, exactly.</b> That is the arithmetic the gait
    /// invariance property rests on: today's gait asks for a foot at exactly <c>legLength</c> from the
    /// hip, and a two-bone solve returns a straight knee for that target only if
    /// <c>thigh + shin == legLength</c>. So the shin's length is taken as the sub-segment node's own
    /// drop below the hip — a rest offset, not a mesh extent, because a mesh deliberately overhangs
    /// its joint so the bend does not open a gap — and the thigh is defined as whatever is left. Two
    /// independent measurements would leave a residual, and a residual is a knee that is very slightly
    /// bent at rest on every frame of every walk.</para>
    /// </summary>
    private void MeasureLimbSegments()
    {
        // The shin node sits AT the knee, so its rest offset below its own hip is the thigh's length.
        float thigh = _shinL != null ? -_shinL.Position.Y
            : (_shinR != null ? -_shinR.Position.Y : _legLengthM * 0.5f);
        _thighLengthM = Mathf.Clamp(thigh, _legLengthM * 0.1f, _legLengthM * 0.9f);

        // The arm's own length is not otherwise measured anywhere, so it is taken here. With a forearm
        // it is the elbow's drop below the shoulder plus the forearm mesh's drop below the elbow —
        // summed for the same reason the leg's pair is, so shoulder-to-wrist is one quantity and not
        // two that can disagree. Without one it is the single arm mesh's own drop, falling back to a
        // fraction of the body's height on a rig whose arms are inert stubs (a whole figure), which
        // keeps the elbow maths finite on a figure that will never show one.
        MeshInstance3D? forearm = _forearmL ?? _forearmR;
        if (forearm?.Mesh != null)
        {
            _upperArmLengthM = Mathf.Max(0.01f, -forearm.Position.Y);
            _armLengthM = _upperArmLengthM + Mathf.Max(0.01f, -forearm.GetAabb().Position.Y);
        }
        else
        {
            float arm = _leftArm is MeshInstance3D { Mesh: not null } armMesh
                ? -armMesh.GetAabb().Position.Y
                : 0f;
            _armLengthM = arm > 0.04f ? arm : Mathf.Max(0.10f, BodyBounds.Size.Y * 0.32f);
            // A single-segment arm: there is no elbow, so "upper" is the whole limb. Nothing solves
            // through it (HasElbows is false), and this keeps ForearmLengthM at zero rather than
            // inventing a bone the model does not have.
            _upperArmLengthM = _armLengthM;
        }
    }

    private void AccumulateBodyBounds(Node node, Transform3D toRig, ref Aabb bounds, ref bool any)
    {
        // The sprout is cosmetic secondary motion and the scaffolding is already dead — see
        // BodyBounds' doc comment for what each exclusion is protecting against.
        //
        // The mounts subtree is the third exclusion and it is the newest (ANIM-1). Everything hanging
        // there is a thing the character is HOLDING — a net, a log, a camera — and a held object is
        // not part of the body. Without this, picking up a crate would grow the avatar's own
        // collision capsule and eyeline the next time a synced avatar key triggered a rebuild, which
        // would present as a mysterious tuning drift rather than as this line being missing.
        if (node == _sprout || node == _mounts || node.IsQueuedForDeletion())
            return;
        if (node is MeshInstance3D mesh)
            ExpandOverVertices(mesh, toRig, ref bounds, ref any);
        foreach (Node child in node.GetChildren())
            AccumulateBodyBounds(child, toRig, ref bounds, ref any);
    }

    /// <summary>Grows <paramref name="bounds"/> over one mesh's ACTUAL vertices, in rig space.
    ///
    /// <para>Vertices, not <c>GetAabb()</c>, and this is not fussiness. <c>GetAabb()</c> is
    /// axis-aligned in the MESH's own space; transforming its eight corners and re-bounding them
    /// inflates any part whose node carries an off-axis rotation. Several of the original
    /// harvested torsos were pitched about 17 degrees, and measuring them that way put the crown
    /// 10 cm above the highest vertex in the file — which would have grown the shipped creature's
    /// collider by that much while leaving the bodies whose parts happened to be axis-aligned untouched.
    /// The first version of this method did exactly that and the roster-wide suite caught it.</para>
    ///
    /// <para>EVERY SURFACE, not <c>surface 0</c>. glTF ships one primitive per material, so
    /// surface 0 is one material's chunk of the character and never the character. Reading only
    /// the first surface is a mistake this repo has made more than once, and it fails quietly:
    /// the numbers look plausible, they are just a fraction of the model.</para>
    ///
    /// <para>Rest pose only. Nothing on the roster is skinned or morph-driven today (the face
    /// contract's Part backend moves whole nodes), so rest vertices are the whole silhouette. A
    /// future skinned character would need its extremes measured across its pose range instead;
    /// this would under-measure it.</para></summary>
    private static void ExpandOverVertices(MeshInstance3D instance, Transform3D toRig,
        ref Aabb bounds, ref bool any)
    {
        Mesh? mesh = instance.Mesh;
        if (mesh == null || !instance.IsInsideTree())
            return;
        Transform3D toRigLocal = toRig * instance.GlobalTransform;
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
            if (arrays.Count <= (int)Mesh.ArrayType.Vertex)
                continue;
            foreach (Vector3 vertex in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                Vector3 point = toRigLocal * vertex;
                if (!any)
                {
                    bounds = new Aabb(point, Vector3.Zero);
                    any = true;
                }
                else
                {
                    bounds = bounds.Expand(point);
                }
            }
        }
    }

    private Vector3? MeasureEyeCentre(Transform3D toRig)
    {
        Vector3? left = EyePartCentre(_leftEye, toRig);
        Vector3? right = EyePartCentre(_rightEye, toRig);
        if (left == null)
            return right;
        if (right == null)
            return left;
        return (left.Value + right.Value) * 0.5f;
    }

    /// <summary>Centre of one eye part, rig-local. Deliberately returns null for anything that
    /// is not real geometry: both build paths substitute an EMPTY node when a model is missing
    /// an eye, and an empty node sits at the rig origin — so trusting it would put a character's
    /// eyeline on the floor, which looks exactly like a tuning mistake rather than a broken
    /// export.</summary>
    private static Vector3? EyePartCentre(Node3D? part, Transform3D toRig)
    {
        if (part is not MeshInstance3D mesh || mesh.Mesh == null || !IsInstanceValid(mesh))
            return null;
        var bounds = new Aabb();
        bool any = false;
        ExpandOverVertices(mesh, toRig, ref bounds, ref any);
        return any ? bounds.GetCenter() : null;
    }

    /// <summary>
    /// <b>The glTF forward-axis correction applied as a POSITION-ONLY mirror: every node's local
    /// translation is negated in X and Z and every basis is left identity.</b> The alternative — a
    /// 180-degree yaw on the instanced root, which is what every other file-backed row still gets —
    /// lands the parts in exactly the same places and is what shipped before INTEG-1.
    ///
    /// <para><b>Why this exists, measured.</b> A yaw on the root does not stop at placement: it
    /// becomes part of every harvested node's REST BASIS, because <c>Reparent(keepGlobalTransform)</c>
    /// preserves the basis along with the position. The pose layer then adds its angles to that rest
    /// rotation (<c>SolveLeg</c>: <c>hipNode.Rotation = hipRestRot + new Vector3(hipAngle, ...)</c>),
    /// so a "swing the thigh forward" delta is applied about an X axis that the yaw has already
    /// pointed backwards — and the leg swings the wrong way. Measured on the authored greybox before
    /// this method existed: <c>SandboxSelfTest</c>'s foot-slip check read <b>81.001 mm/frame at
    /// 2.43 m/s against a per-frame travel of 40.5 mm</b> — exactly 2x, the signature of a contact
    /// point moving backwards over the ground at the same speed the body moves forwards. With the
    /// mirror it reads 0.000 mm/frame.
    ///
    /// <para><b>RE-MEASURED 2026-08-21 (ANIM-M3), because the greybox row no longer takes this
    /// correction at all.</b> ANIM-M2 authored the asset <c>-Z</c>-forward and the row moved to
    /// <see cref="Build.AuthoredRig"/>, so the question is whether removing the mirror re-opened the
    /// slip. It did not, and the two configurations were run back to back on one machine off one
    /// import to say so rather than reasoned about: <b>walk 0.001, jog 0.004, sprint 0.015 mm/frame,
    /// identical to three decimals with the harvest-plus-mirror row restored and with the authored
    /// row in place.</b> The "0.000" above is an older print of the same walk case and is kept as the
    /// historical figure; 0.001 mm/frame against 40.5 mm of per-frame travel is 0.0025%, which is the
    /// same claim.</para>
    ///
    /// <para>The reason it is unaffected is the one ANIM-M2 gave and this method's own body shows:
    /// the mirror writes POSITIONS and leaves every basis identity, while the defect it was built
    /// for is a folded rest BASIS. Removing a correction that never touched the bases cannot
    /// re-introduce a basis fault.</para>
    ///
    /// <para><b>This is a pre-existing defect in the harvest path, not one INTEG-1 introduced, and it
    /// is why it is opted into per row rather than fixed for everyone.</b> Every file-backed row has
    /// carried the yawed rest basis since the harvest was written; it was never measurable because no
    /// authored model had legs long enough to see it (the original creature's foot node sat 0.08 m
    /// off the ground, and RIG-1 recorded that its walk collected zero usable stance samples). The authored
    /// greybox is the first harvested body with a real 0.44 m leg, which is what made it visible.
    /// Turning the mirror on globally is NOT the same change: a mirror moves a part's origin without
    /// mirroring its geometry, so it is only equivalent to the yaw for parts whose mesh is symmetric
    /// about its own X and Z — true of every axis-aligned box, frustum and sphere in the authored
    /// greybox, and NOT safe to assume of a tailed body. Flipping a harvested creature needs
    /// a look at each one and belongs in its own packet.</para>
    /// </summary>
    private static void MirrorAboutY(Node node)
    {
        if (node is Node3D spatial)
        {
            Vector3 p = spatial.Position;
            spatial.Position = new Vector3(-p.X, p.Y, -p.Z);
        }
        foreach (Node child in node.GetChildren())
            MirrorAboutY(child);
    }

    private static Node3D Inert(string name, Node3D parent)
    {
        var node = new Node3D { Name = name };
        parent.AddChild(node);
        return node;
    }

    private static void CollectMeshInstances(Node node, Dictionary<string, MeshInstance3D> into)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh)
                into[mesh.Name.ToString()] = mesh;
            CollectMeshInstances(child, into);
        }
    }

    /// <summary>The parts the row being built declares it does not have — set from the resolved row
    /// at the top of <see cref="BuildAppearance"/>, read by <see cref="TakePart"/>. An instance
    /// field rather than a parameter threaded through a dozen call sites, and set from the RESOLVED
    /// row, so a fallback body is judged against the fallback's declarations.</summary>
    private System.Collections.Generic.IReadOnlySet<string> _declaredAbsentParts = NoAbsentParts;

    /// <summary>The absences the body THIS instance actually built declares — the resolved row's set,
    /// which on a fallback is the primitive's and not the row that failed. Paired with
    /// <see cref="ResolvedModelPath"/>: together they say what was built and what it was allowed to
    /// be missing.</summary>
    internal System.Collections.Generic.IReadOnlySet<string> ResolvedAbsentParts => _declaredAbsentParts;

    private MeshInstance3D ReparentTinted(
        Dictionary<string, MeshInstance3D> parts, string name, Node3D newParent, Color color, string modelPath)
    {
        MeshInstance3D mesh = TakePart(parts, name, newParent, modelPath);
        mesh.MaterialOverride = Mat(color);
        return mesh;
    }

    private MeshInstance3D ReparentAsIs(
        Dictionary<string, MeshInstance3D> parts, string name, Node3D newParent, string modelPath) =>
        TakePart(parts, name, newParent, modelPath);

    // Moves a harvested GLB mesh part under newParent, preserving its visual placement —
    // Node.Reparent(keepGlobalTransform: true) keeps it exactly where the artist authored
    // it, however deep it was nested in the imported hierarchy. Falls back to an empty
    // placeholder if the model is ever missing a named part, so a stale asset degrades
    // instead of crashing the rig.
    private MeshInstance3D TakePart(
        Dictionary<string, MeshInstance3D> parts, string name, Node3D newParent, string modelPath)
    {
        // The harvested re-rig wraps each moving part in a Node3D that takes the plain
        // part name (ArmL, ArmR, FootL, FootR, Tail) and nests the actual mesh one level
        // down as "<name>_mesh". Non-moving parts stay flat MeshInstance3Ds under their own
        // name. Try the flat name first, then the wrapper-nested "<name>_mesh" — either way
        // Reparent(keepGlobalTransform) preserves the authored world placement, however deep
        // the mesh sat, so the harvested rig lands exactly where the artist posed it.
        if ((parts.Remove(name, out MeshInstance3D? mesh) || parts.Remove(name + "_mesh", out mesh))
            && mesh != null)
        {
            mesh.Reparent(newParent, true);
            return mesh;
        }

        // DECLARED absent (see RosterRow.AbsentParts) is a different fact from DISCOVERED absent,
        // and only the second one is an error. A row that says "this file has no tail" gets the same
        // empty placeholder the code-built body emits for itself, silently; anything the row did NOT
        // name is still loud, because a part missing from a model almost always means a broken
        // export and this error is the only thing that catches it.
        if (!_declaredAbsentParts.Contains(name))
        {
            _undeclaredMissingParts.Add(name);
            GD.PushError($"AvatarVisual: '{modelPath}' is missing expected part '{name}'.");
        }
        var placeholder = new MeshInstance3D { Name = name };
        newParent.AddChild(placeholder);
        return placeholder;
    }

    /// <summary>
    /// Harvests a part that a model is <b>allowed not to have</b> — the head, the shins, the forearms
    /// (RIG-1). Returns null when it is absent, and the caller falls back to the single-segment pose
    /// that shipped rather than synthesising a joint.
    ///
    /// <para><b>Silent, unlike <see cref="TakePart"/>, and that asymmetry is the point.</b> The loud
    /// error is right for the sixteen-part contract, where a missing part means a broken export. It is
    /// wrong here: every <c>.glb</c> on the roster when RIG-1 landed legitimately
    /// had none of these, RIG-1 edits no <c>.glb</c>, and pushing an error would fire on every avatar
    /// spawn in the game.</para>
    ///
    /// <para><b>A declared-absent empty mesh reads as ABSENT, not as a zero-length bone.</b> Both
    /// build paths substitute a <see cref="MeshInstance3D"/> carrying no mesh for a part a body does
    /// not have, and a zero-length bone fed to <see cref="LimbIk"/> would be a limb that solves to
    /// nothing. This is the same test <see cref="EyePartCentre"/> already applies for exactly the same
    /// reason — it returns null for anything that is not real geometry so an aim ray can never
    /// silently be placed on the floor. The empty node is left in <paramref name="parts"/> and freed
    /// with the rest of the leftover scaffolding.</para>
    /// </summary>
    private static MeshInstance3D? TakeOptionalPart(
        Dictionary<string, MeshInstance3D> parts, string name, Node3D newParent)
    {
        if (!parts.TryGetValue(name, out MeshInstance3D? mesh))
            parts.TryGetValue(name + "_mesh", out mesh);
        if (mesh == null || mesh.Mesh == null)
            return null;
        parts.Remove(name);
        parts.Remove(name + "_mesh");
        mesh.Reparent(newParent, true);
        return mesh;
    }

    // TriggerJumpStretch() and TriggerLandSquash(float) LIVED HERE and were deleted by JUMP-1.
    // They were the only two writers of a jump-specific vertical scale, they were called from
    // PlayStepCosmetics (so they never ran on a remote proxy at all), and what they wrote is
    // measured in the "The jump's pose" constant block above. Both are now DERIVED inside
    // Animate from the replicated Grounded flag and Velocity.Y, which is why the jump is a pose
    // rather than a scale and why every peer sees it. Do not reintroduce them as an event: the
    // event path is precisely what left a teammate's jump unanimated.

    /// <summary>Quick sideways jolt for bumps (small squash toward the hit).</summary>
    public void TriggerBumpSquish()
    {
        _scale = new Vector3(0.88f, 1.1f, 0.88f);
        _scaleVel = Vector3.Zero;
    }

    /// <summary>
    /// <b>Which of the two carries this body is in</b> (CARRY-1). Replaces the single
    /// <c>SetCarrying(bool)</c>, which could not express a distinction the data model has made since
    /// 2026-08-08: a thing in the HAND and an armful in the ARMS are different registers and they
    /// are held differently.
    ///
    /// <para><b>Costs no wire bytes.</b> Both registers already replicate through
    /// <c>PropManager</c>'s holder state, so every peer derives the same pose from state it was
    /// already handed.</para>
    /// </summary>
    public void SetCarry(CarryPose pose) => _carry = pose;

    /// <summary>
    /// The Soaked wet look (W2, lake-water contract §7). A darkening, glossy
    /// <see cref="GeometryInstance3D.MaterialOverlay"/> laid over every mesh part rather than an
    /// edit to any part's own material — which is what makes it reversible with one null
    /// assignment and keeps it from touching the palette work <c>BuildAppearance</c> owns. One
    /// shared static material for every soaked player in the session: it carries no per-instance
    /// data, so N players cost one material, not N.
    ///
    /// <b>Scope note.</b> This is a wet <i>sheen</i>, not the dripping and flat hair spec §7
    /// describes — those want particles and a hair rig, and this trunk has neither. The state is
    /// legible at a glance (MECHANICS-BIBLE §2.5), which is the part that is load-bearing now;
    /// the rest is art debt with a named home.
    /// </summary>
    public void SetSoaked(bool soaked)
    {
        if (_soaked == soaked)
            return;
        _soaked = soaked;
        ApplyOverlay(this, soaked ? WetOverlay : null);
    }

    private bool _soaked;

    private static StandardMaterial3D? _wetOverlay;

    /// <summary>
    /// One shared wet-sheen material for every soaked player in the session — it carries no
    /// per-instance data, so N players cost one material rather than N.
    ///
    /// <b>Built lazily, and that is not a micro-optimisation.</b> A <c>static readonly</c>
    /// initializer would run this class's static constructor the first time ANY member is
    /// touched — including from the engine-free xUnit tier, where constructing a Godot
    /// <c>Resource</c> has no engine behind it and takes the whole test host down with it rather
    /// than failing one test. <c>FabricCatalogTests</c> touches this class, so that is not a
    /// hypothetical. Deferring construction to the first actual soaking keeps it in-engine, where
    /// it belongs.
    /// </summary>
    private static StandardMaterial3D WetOverlay => _wetOverlay ??= new StandardMaterial3D
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0.10f, 0.16f, 0.24f, 0.34f),
        Metallic = 0.55f,
        MetallicSpecular = 0.85f,
        Roughness = 0.14f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
    };

    private static void ApplyOverlay(Node node, Material? overlay)
    {
        if (node is MeshInstance3D mesh)
            mesh.MaterialOverlay = overlay;
        foreach (Node child in node.GetChildren())
            ApplyOverlay(child, overlay);
    }

    // --- Failure-state skins (phase 1c) ----------------------------------------------------------

    private Sail.Game.Failure.IncapacityState _incapacity = Sail.Game.Failure.IncapacityState.Active;
    private Node3D? _birds;
    private MeshInstance3D? _iceBlock;
    private float _birdSpin;
    private static StandardMaterial3D? _frostOverlay;

    // --- The root transform, and its ONE writer (W7-4) -------------------------------------------
    //
    // THREE separate systems want to move this node and every one of them is presentation:
    //   * the incapacity skin  - a knocked-out body lies flat and is lifted clear of the floor;
    //   * the render offset    - a predicted owner hides a reconciliation pop by sliding the MESH
    //                            while the simulated body stays where the server put it;
    //   * the death beat       - RespawnService's beat between the kill and the respawn.
    //
    // Until W7-4 they wrote Position and Rotation directly, from three different files, on three
    // different callbacks. SetIncapacity's own comment already names the hazard in the case it
    // guarded against ("two writers on one node is the exact shape of bug this whole packet is
    // about") and then the other two walked straight into it: SandboxAvatar.OwnerTick zeroed the
    // death beat's pose on every physics tick and the beat wrote it back on every render frame, so
    // the body Talon watched drown was posed by whichever callback happened to run last.
    // BEHAVIOR-BIBLE 10.2 / STATE-CASCADE-TABLE row 2: exactly one writer per tick.
    //
    // So each contributor now DECLARES its term and this class composes them. The three are
    // additive by construction and none of them can silently discard another.
    private Vector3 _incapacityPos, _incapacityRot;
    private Vector3 _renderOffset;
    private Vector3 _deathPos, _deathRot;

    /// <summary>The render-space correction offset, in this node's local space - a predicted
    /// owner's undrained reconciliation error (see <c>SandboxAvatar.RenderGlobalPosition</c>).
    /// Zero on every other role. Idempotent; safe to call every frame.</summary>
    public void SetRenderOffset(Vector3 localOffset)
    {
        _renderOffset = localOffset;
        ApplyRootPose();
    }

    /// <summary>The death beat's pose (<see cref="Sail.Game.Run.DeathBeatPose"/>), additive over
    /// whatever the other two contributors are doing. Presentation only and never replayed - the
    /// body's authoritative transform is untouched by this, which is what lets a client play a
    /// beat without ever disagreeing with the server about where anyone is standing.
    ///
    /// <para>Call with <see cref="Sail.Game.Run.DeathBeatPose.Pose.Rest"/> to clear it; the beat
    /// does exactly that when it ends, so a body that comes back is posed by nothing.</para></summary>
    public void SetDeathPose(in Sail.Game.Run.DeathBeatPose.Pose pose)
    {
        _deathPos = pose.Offset;
        _deathRot = pose.Rotation;
        ApplyRootPose();
    }

    /// <summary>The sole writer of this node's own Position and Rotation.</summary>
    private void ApplyRootPose()
    {
        Position = _incapacityPos + _renderOffset + _deathPos;
        Rotation = _incapacityRot + _deathRot;
    }

    /// <summary>Whether the animation loop should be posing this body at all. False while
    /// incapacitated: the gait, the lean, the fidget and the idle breathing all say "this player
    /// is alive and doing something", and every one of them fights a body that is supposed to be
    /// flat on its back or frozen solid.</summary>
    public bool AnimationSuspended => _incapacity != Sail.Game.Failure.IncapacityState.Active;

    /// <summary>
    /// <b>The legibility cascade row</b> (STATE-CASCADE-TABLE row 20, BEHAVIOR-BIBLE §10.3,
    /// MECHANICS-BIBLE §2.5). Within about a second and without any text, a player must know they
    /// changed state and <i>which</i> state they are in — and the two must be distinguishable
    /// from <b>each other</b>, not merely from normal play, which is the bar §10.3 actually sets.
    ///
    /// <para>They are built to be told apart at a glance and from across camp, on three separate
    /// channels at once: <b>silhouette</b> (a body lying flat versus a body standing rigid inside
    /// a box), <b>colour</b> (nothing added versus a pale blue that appears nowhere else in the
    /// world palette), and <b>motion</b> (birds orbiting versus total stillness — a frozen player
    /// is the only thing in this game that does not move at all). Any one of the three carries
    /// the read alone, which is what lets it survive night, fog and distance.</para>
    ///
    /// <para>Both skins are comic by construction, per the register law (beta plan §0): cartoon
    /// birds and a solid block of ice. Nothing here is or may become gore. <b>The affect
    /// direction — what being frozen should FEEL like — is not decided here and was not decided
    /// by this packet</b>; it routes through <c>/direct</c>, which owns the THRILL-BIBLE ledger.
    /// This method implements the register the plan already ratified, and no more.</para>
    /// </summary>
    public void SetIncapacity(Sail.Game.Failure.IncapacityState state, float crownM, float halfWidthM)
    {
        if (_incapacity == state)
            return;
        _incapacity = state;

        bool ko = state == Sail.Game.Failure.IncapacityState.KnockedOut;
        bool frozen = state == Sail.Game.Failure.IncapacityState.Frozen;

        // Flat on the back: pitch the whole rig back 90 degrees and lift it so the body lies ON
        // the ground rather than pivoting through it. Applied to THIS node, never to _body —
        // Animate owns _body's transform, and two writers on one node is the exact shape of bug
        // this whole packet is about.
        // W7-4: declares its term instead of writing the node, so the render offset and the death
        // beat survive a knock-out landing on the same body. ApplyRootPose is the only writer.
        _incapacityRot = ko ? new Vector3(-Mathf.Pi * 0.5f, 0f, 0f) : Vector3.Zero;
        _incapacityPos = ko ? new Vector3(0f, Mathf.Max(0.05f, halfWidthM * 0.5f), 0f) : Vector3.Zero;
        ApplyRootPose();

        // Restores the wet sheen rather than clearing to null when thawing: the overlay slot has
        // one occupant, and a player who froze while Soaked must not come out of it dry.
        ApplyOverlay(this, frozen ? FrostOverlay : (_soaked ? WetOverlay : null));
        EnsureBirds(crownM).Visible = ko;
        EnsureIceBlock(crownM, halfWidthM).Visible = frozen;
    }

    /// <summary>Spins the birds. Called from <see cref="Animate"/> so it rides whatever frame loop
    /// already drives this visual (owner tick, offline tick, or a remote proxy's render frame)
    /// rather than needing a <c>_Process</c> of its own on every player in the session.</summary>
    private void AnimateFailureSkin(float delta)
    {
        if (_birds is not Node3D birds || !birds.Visible)
            return;
        _birdSpin += delta * BirdOrbitRadPerSec;
        // Counter-rotates the rig's own -90 degree pitch, so the birds orbit a horizontal circle
        // above the fallen player instead of a vertical one beside them. They are parented to the
        // rig so they follow the body when it is dragged; that convenience is what costs this.
        birds.Rotation = new Vector3(Mathf.Pi * 0.5f, _birdSpin, 0f);
    }

    private const float BirdOrbitRadPerSec = 2.4f;

    private Node3D EnsureBirds(float crownM)
    {
        if (_birds is Node3D existing)
            return existing;
        var pivot = new Node3D { Name = "KoBirds", Position = new Vector3(0f, crownM + 0.28f, 0f) };
        var body = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 6, Rings = 3 };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.09f, 0.13f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        // Three, evenly spaced, at a readable orbit radius. Deliberately crude spheres: this is a
        // cartoon convention, not a bird, and it reads harder as a shape than as a model.
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Tau / 3f;
            pivot.AddChild(new MeshInstance3D
            {
                Name = $"Bird{i}",
                Mesh = body,
                MaterialOverride = mat,
                Position = new Vector3(Mathf.Cos(a) * 0.26f, Mathf.Sin(a) * 0.05f, Mathf.Sin(a) * 0.26f),
            });
        }
        AddChild(pivot);
        _birds = pivot;
        return pivot;
    }

    private MeshInstance3D EnsureIceBlock(float crownM, float halfWidthM)
    {
        if (_iceBlock is MeshInstance3D existing)
            return existing;
        // Sized off the measured body with a margin, never as a literal — the dimensions
        // cascade's standing rule (derive, do not retype). A block cut to one roster entry's
        // numbers is wrong for the next one, which is precisely how one body's head ended up
        // outside its own collider.
        float w = Mathf.Max(0.5f, halfWidthM * 2f + 0.22f);
        float h = Mathf.Max(0.8f, crownM + 0.16f);
        var block = new MeshInstance3D
        {
            Name = "FrozenBlock",
            Mesh = new BoxMesh { Size = new Vector3(w, h, w) },
            Position = new Vector3(0f, h * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.62f, 0.84f, 0.95f, 0.42f),
                // Emission, modestly: this has to read across a nearly black camp (beta plan §5),
                // and an unlit ice block at night is an invisible ice block. Visibility across
                // camp is the state's whole promise — "2-second legibility for free".
                EmissionEnabled = true,
                Emission = new Color(0.36f, 0.62f, 0.78f),
                EmissionEnergyMultiplier = 0.5f,
                Metallic = 0.2f,
                Roughness = 0.08f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(block);
        _iceBlock = block;
        return block;
    }

    /// <summary>The frozen sheen laid over the body inside the block. Lazy for exactly the reason
    /// <see cref="WetOverlay"/> is lazy — a static initializer constructing a Godot
    /// <c>Resource</c> takes the engine-free xUnit host down with it rather than failing one
    /// test. Read that property's doc; this is the same trap.</summary>
    private static StandardMaterial3D FrostOverlay => _frostOverlay ??= new StandardMaterial3D
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0.70f, 0.86f, 0.95f, 0.55f),
        Metallic = 0.3f,
        Roughness = 0.10f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
    };

    /// <summary>Aim-rig raised pose (WP-L3, Issue #106) — independent of the carry pose:
    /// aimed equipment is per-player EQUIPMENT, not a CarryController-slotted prop (hand slot
    /// stays free, per the lean spec), so this is never driven by the same "am I holding
    /// something" signal carrying is. Both blends can in principle be active together (nothing
    /// stops a future verb from co-existing with a genuinely carried item); see Animate's
    /// additive combination below for why that composes safely instead of fighting.</summary>
    public void SetAiming(bool aiming) => _aiming = aiming;

    /// <summary>
    /// Drive the swing pose for one frame (ANIM-1). <c>engaged</c> is eased into a weight exactly as
    /// <see cref="SetCarry"/> and <see cref="SetAiming"/> are, so a swing that is abandoned
    /// mid-stroke — the peer left, the avatar despawned, the run reset — unwinds instead of leaving
    /// the body frozen mid-whip. <c>drive</c> is the stroke's body drive: roughly -1..+1, zero at
    /// rest.
    ///
    /// <para><b>Presentation only.</b> Nothing set here is ever read back by anything that decides
    /// what a swing did; an authority resolves that from its own aim transform. No caller exists
    /// in the MVP build (the swing verb left with its package); the rig stays inert at zero
    /// weight until the next held tool drives it.</para>
    /// </summary>
    /// <param name="progress01">The stroke's replicated normalised progress, forwarded so the
    /// clip layer can seek
    /// <c>Hold_NetSwing</c> to where the swing REALLY is rather than restarting it. This is the
    /// mid-action-join case the brief names by name, and it is the one action state where the anchor
    /// is a genuine wire value rather than a locally-stamped tick — see <c>ClipTimeAnchor</c>.
    /// Ignored entirely when the clip layer is off.</param>
    public void SetSwing(bool engaged, float drive, float progress01 = 0f)
    {
        _swinging = engaged;
        // HELD rather than zeroed on disengage, for exactly the reason _swingDrive is held two lines
        // below: DeriveHoldState keeps reporting NetSwing while _swingBlend unwinds, so zeroing this
        // would seek the stroke clip back to frame 0 and pin it at the coil for the whole unwind —
        // the arm snapping back to the start of a swing it just finished. Held, the clip stays where
        // the stroke left it and the transition fades out from there.
        if (engaged)
            _swingProgress01 = Mathf.Clamp(progress01, 0f, 1f);
        // Held rather than zeroed on disengage, so the unwinding blend eases the body back along the
        // stroke it was on instead of snapping the drive to zero under a non-zero weight.
        if (engaged)
            _swingDrive = Mathf.Clamp(drive, -1f, 1f);
    }

    /// <summary>
    /// The turnaround skid's pose flag (SKID-1). <b>Presentation only, and it never decides
    /// anything</b> — the state itself is <c>MoveState.SkidRemaining</c>, simulated by
    /// <c>AvatarMotor.Step</c> on the server and in owner prediction alike. This is the same
    /// signal arriving at the body that already moved.
    /// </summary>
    /// <param name="remainingSec">The replicated <c>MoveState.SkidRemaining</c>. The clip layer
    /// backdates the <c>Skid</c> clip's entry against it, which is what lets a client that arrived
    /// mid-brake render the brake it is actually in. Optional so every existing caller — the labs,
    /// the self-tests — keeps compiling and simply gets the un-backdated behaviour.</param>
    public void SetSkidding(bool skidding, float remainingSec = 0f)
    {
        _skidding = skidding;
        _skidRemainingSec = skidding ? remainingSec : 0f;
    }

    /// <summary>
    /// <b>The crouch verbs and the chain jump, arriving at the body</b> (MOVE-5c, spec §12).
    /// Presentation only, on exactly the contract <see cref="SetSkidding"/> has: the state itself is
    /// <c>MoveState.Verb</c> and <c>MoveState.ChainDepth</c>, simulated by <c>AvatarMotor.Step</c> on
    /// the server and in owner prediction alike, replicated to every peer, and read here by a class
    /// that decides nothing. <b>Nothing in this file computes a verb for itself</b> — a pose that
    /// recomputed one is a pose that can quietly disagree with the motor it is reporting on.
    ///
    /// <para><b>This is the entire readable channel for the chain.</b> Spec §6.6 forbids a chain
    /// readout of any kind, in any build, <i>including the lab</i>; §12 is what a co-op partner is
    /// given instead. If the pose below does not carry it, the mechanic is invisible to everyone
    /// including the player who earned it.</para>
    ///
    /// <para><b>Both parameters are optional</b> so every existing caller — the labs, the
    /// self-tests, the capture harnesses — keeps compiling and keeps getting exactly the body it had
    /// before this method existed. <see cref="MoveVerb.Normal"/> at depth 0 is the exact no-op; see
    /// <see cref="VerbPose.Normal"/>.</para>
    /// </summary>
    /// <param name="verb">The replicated <c>MoveState.Verb</c>.</param>
    /// <param name="chainDepth">The replicated <c>MoveState.ChainDepth</c>. <b>Read by the pose and
    /// by tests, and by nothing else</b> — §6.6.</param>
    public void SetVerb(MoveVerb verb, int chainDepth = 0)
    {
        _verb = verb;
        _chainDepth = Mathf.Max(0, chainDepth);
    }

    private MoveVerb _verb;
    private int _chainDepth;

    /// <summary>
    /// <b>The ride channel (BIKE-4A) — the mounted body arriving at the rig.</b> Presentation only,
    /// on exactly the contract <see cref="SetSwing"/> and <see cref="SetSkidding"/> have: nothing
    /// set here is ever read back by anything that decides whether a body is mounted, how fast it
    /// goes or what it collides with. <b>This packet writes no velocity and no position anywhere</b>
    /// — the whole of it is this method, a crank phase integrated locally, and pose targets from
    /// <see cref="RidePose"/>.
    ///
    /// <para><b>Nothing in this file computes a ride for itself</b>, for <see cref="SetVerb"/>'s
    /// reason: a pose that recomputed the mode is a pose that can quietly disagree with the layer
    /// it is reporting on. The mode comes in resolved, with its hysteresis already spent by
    /// <see cref="RidePose.StepMode"/> in the caller.</para>
    ///
    /// <para><b>The weight IS the layer's blend</b> (BIKE-3B §1), not a second eased copy of it, so
    /// the tuning blend and the body blend cannot disagree and a reversed blend reverses the body
    /// for free. It is applied as a lerp toward the ride's targets everywhere it lands, so an
    /// interrupted mount unwinds instead of leaving the body half-straddled.</para>
    ///
    /// <para><b>Every parameter is optional and the defaults are the exact no-op</b>, so every
    /// existing caller — the labs, the self-tests, the capture harnesses — compiles unchanged and
    /// gets exactly the body it had before this method existed. Weight 0 multiplies out every term
    /// below it, bit for bit.</para>
    ///
    /// <para><b>Where this differs from 3B's proposed signature, and why.</b> 3B wrote
    /// <c>SetRide(weight, mode, crankPhase01, leanDeg, stumble01)</c>. <c>stumble01</c> is dropped:
    /// the stumble pose is choreography T5 and is outside BIKE-4A's scope, and a parameter this
    /// packet does not implement would read as a channel that exists. <c>crankPhase01</c> becomes an
    /// optional SEEK rather than a required input, because 3B's own §1 calls the phase a
    /// "presentation-local accumulator [that] advances client-side from wheel speed the way
    /// <c>_gaitPhase</c> already advances from ground speed" — and <c>_gaitPhase</c> lives here. A
    /// negative value (the default) means "advance it yourself"; a value in 0..1 seeks it, which is
    /// the same mid-join affordance <see cref="SetSwing"/>'s <c>progress01</c> is. <c>steer01</c> is
    /// added because 3B's S1 arms row asks the bars to follow the steer and its input table names
    /// the steer as an existing input.</para>
    /// </summary>
    /// <param name="weight"><c>BikeLayer.Blend</c>: 0 on foot, 1 fully riding.</param>
    /// <param name="mode">PEDAL or COAST, already resolved — see <see cref="RidePose.Wants"/> and
    /// <see cref="RidePose.StepMode"/>.</param>
    /// <param name="steer01">Steer, −1..+1, positive turning toward +X. Yaws the bars.</param>
    /// <param name="leanRad">The machine's roll, radians — BIKE-2x's <c>BikeHandling</c> output.
    /// Added to the body's roll so the rider banks with the bike. <b>Not wired by BIKE-4A</b>: the
    /// handling lean is the sibling packet's channel and this is the one argument that waits for
    /// it; the default 0 is the exact no-op until it is passed.</param>
    /// <param name="crankPhase01">Negative (the default) advances the local accumulator; 0..1 seeks
    /// it.</param>
    public void SetRide(float weight, RideMode mode, float steer01 = 0f, float leanRad = 0f,
        float crankPhase01 = -1f)
    {
        _rideWeight = Mathf.Clamp(weight, 0f, 1f);
        _rideMode = mode;
        _rideSteer01 = Mathf.Clamp(steer01, -1f, 1f);
        _rideLeanRad = float.IsFinite(leanRad) ? leanRad : 0f;
        if (crankPhase01 >= 0f)
            _crankPhase01 = crankPhase01 - Mathf.Floor(crankPhase01);
    }

    private float _rideWeight;
    private RideMode _rideMode = RideMode.Coast;
    private float _rideSteer01;
    private float _rideLeanRad;

    /// <summary>Where the cranks are, 0..1 around one revolution. Integrated here from the
    /// replicated ground speed exactly as <c>_gaitPhase</c> is, and never sent — see
    /// <see cref="SetRide"/>.</summary>
    private float _crankPhase01;

    /// <summary>The eased torso pitch the mode asks for, radians positive forward. Eased rather
    /// than switched so the 6° between PEDAL and COAST is crossed rather than stepped over.</summary>
    private float _rideTiltRad = RidePose.CoastTiltRad;

    /// <summary>The ride weight the last <see cref="Animate"/> actually POSED — which is not always
    /// the weight <see cref="SetRide"/> was told. <b>A READOUT for the self-test and the capture
    /// harness</b>, exactly as <see cref="ChainReadForTools"/> is: "the channel parked" is a
    /// measurement before it is a photograph. Nothing decides on it.
    ///
    /// <para><b>Why it is a separate field from <c>_rideWeight</c>, and it is not tidiness.</b> The
    /// incapacity early return parks the channel for the frame it renders, and then the driver calls
    /// <c>SetRide</c> again on the very next tick — so a readout of the INPUT reports a pedalling
    /// rider all the way through a knock-out even though not one pose term ran. It reported exactly
    /// that in the first self-test run of this packet, and the check that caught it is the reason
    /// this field exists. What a body is posing is the only honest thing to report.</para></summary>
    public float RideWeightForTools => _ridePosedWeight;

    /// <inheritdoc cref="RideWeightForTools"/>
    private float _ridePosedWeight;

    /// <inheritdoc cref="RideWeightForTools"/>
    public float CrankPhase01ForTools => _crankPhase01;

    /// <inheritdoc cref="RideWeightForTools"/>
    public RideMode RideModeForTools => _rideMode;

    /// <summary>Crank revolutions per second at the speed this body was last animated with — the
    /// cadence rule's own answer (<see cref="RidePose.CrankHzAt"/>), zeroed while the cranks are
    /// parked. The pedal spin IS the momentum readout (BIKE-3C row 3), so this is the number a test
    /// asserts proportionality on.</summary>
    public float CrankHz { get; private set; }

    /// <summary>The three eased verb weights and the smoothed chain read.
    ///
    /// <para><b>Yes, this is local state, and it is the same kind <c>_skidBlend</c>, <c>_gaitAmp</c>
    /// and <c>_airborne</c> already are</b> — presentation smoothing over a replicated fact, written
    /// once per rendered frame by one method, read by nothing outside this class, and never fed back
    /// into control or physics. It carries no information a peer could disagree about: every peer
    /// runs this same easing over the same replicated verb and depth, exactly as they already do for
    /// the skid. MECHANICS' state rules govern simulation state, and none of this is any.</para>
    ///
    /// <para>They are also why a pose can never gate an input: the state has already changed by the
    /// time these move, and they only ever describe how far the body has got in catching
    /// up.</para></summary>
    private float _tuckBlend;

    /// <inheritdoc cref="_tuckBlend"/>
    private float _slideBlend;

    /// <inheritdoc cref="_tuckBlend"/>
    private float _duckBlend;

    /// <inheritdoc cref="_tuckBlend"/>
    private float _chainRead;

    /// <summary>The eased chain depth this body is currently posing. A READOUT for the capture
    /// harness and for tests — "the pose deepened" is a measurement before it is a photograph.
    /// <b>It is not a readout for a player</b>: §6.6 forbids showing the chain, and nothing in a
    /// shipped or lab UI may print this.</summary>
    public float ChainReadForTools => _chainRead;

    /// <summary>The three eased verb weights, for the same reason <see cref="ChainReadForTools"/>
    /// exists. They sum to at most 1 by construction — see <see cref="VerbPose.Blend"/>.</summary>
    public (float Tuck, float Slide, float Duck) VerbBlendForTools
        => (_tuckBlend, _slideBlend, _duckBlend);

    /// <summary>
    /// Per-frame animation. localVelocity is the avatar's velocity rotated into its
    /// own space (so leaning works regardless of facing). Purely cosmetic — nothing
    /// here ever feeds back into control or physics.
    /// </summary>
    public void Animate(double dt, Vector3 localVelocity, bool onFloor, bool sprinting = false)
    {
        float delta = (float)dt;

        // Incapacitated: the failure skin is the only thing animating. Returning here rather than
        // letting the gait run underneath the pose is deliberate — a knocked-out player whose
        // legs keep waddling and whose chest keeps breathing is the "some system was never told"
        // shape, and it would also quietly fight the root pose SetIncapacity just set.
        AnimateFailureSkin(delta);
        if (AnimationSuspended)
        {
            // Park the swing rather than freezing it mid-stroke. A knocked-out player whose net arm
            // is still cocked is the same "some system was never told" shape the early return above
            // exists to prevent, and the offset would keep a held net floating off the hand for as
            // long as the body was down.
            _swinging = false;
            _swingBlend = 0f;
            SwingHandOffset = Vector3.Zero;
            // Park the crouch for the same reason (RIG-1). SetIncapacity poses the rig root; a rig
            // left sunk 4 cm on a landing absorb it will never finish would sit that far into the
            // ground it is lying on, and nothing would ever unwind it because this early return is
            // above every writer.
            CrouchDropM = 0f;
            if (_crouch != null)
                _crouch.Position = Vector3.Zero;
            // Park the verb and the chain with it (MOVE-5c), for the crouch's own reason. §3.2's
            // control-lock states dominate and already force Verb = Normal, ChainDepth = 0 in the
            // motor, so this is not a second opinion about the state — it is the eased READ of it
            // being released rather than frozen mid-blend above every writer. Without it, a body
            // knocked out of a slide would come back up still 46% folded for three frames.
            _tuckBlend = 0f;
            _slideBlend = 0f;
            _duckBlend = 0f;
            _chainRead = 0f;
            // Park the coil with them (MOVE-5f), for the crouch's own reason: this early return is
            // above every writer, so a body knocked out mid-rise would hold its coil — a folded knee
            // and a gathered torso — for as long as it was down, and nothing below would ever unwind
            // it. The clock goes with the weight so the flight it was measuring cannot resume.
            _coilBlend = 0f;
            _coilRiseSec = 0f;
            // Park the RIDE with them (BIKE-4A), for the coil's own reason and 3B §6.2's
            // instruction: this early return is above every writer, so a knocked-out rider would
            // hold a pedalling pose — legs on a crank circle, torso hunched over bars that are no
            // longer under it — for as long as the body was down, and nothing below would ever
            // unwind it. The WEIGHT goes to zero and the mode goes back to its rest side; the CRANK
            // PHASE is deliberately HELD, exactly as 3B specifies, so a body that comes back up
            // still riding resumes the stroke it was on instead of snapping the legs to 3-and-9.
            //
            // Zeroed here rather than trusted to the caller for _swinging's reason: SetRide is
            // called every frame by whatever is driving the bike, and this park is what THIS frame
            // renders. The layer is not being told anything; the eased READ of it is being released.
            _rideWeight = 0f;
            _rideMode = RideMode.Coast;
            _rideSteer01 = 0f;
            _rideLeanRad = 0f;
            _rideTiltRad = RidePose.CoastTiltRad;
            _ridePosedWeight = 0f;
            CrankHz = 0f;
            // THE EQUIVALENT HARD BRANCH IN THE TREE (ANIM-M3). The early return above is above every
            // procedural writer; the clip layer needs the same treatment or a knocked-out body would
            // hold whatever frame of Walk it was on. It goes to KnockOut — a real authored pose, at
            // zero crossfade, because a hit that eases in over 0.15 s reads as a body falling over
            // gracefully — and both parks above still happen, which is the whole point of doing it
            // here rather than one branch earlier.
            if (_clips != null)
            {
                EnterClipState(Anim.AvatarActionState.KnockOut, delta);
                _clipHold = Anim.AvatarHoldState.Empty;
                _clipPose = _clips.Advance(
                    delta, Gear.Idle, 0f, _clipAction, _clipHold,
                    ClipSeekSecondsFor(_clipAction), -1f, _clipTier);
            }
            return;
        }

        _animTime += delta;
        float flatSpeed = new Vector2(localVelocity.X, localVelocity.Z).Length();
        // THE GEAR — a named state with hysteresis, derived from replicated ground speed so every
        // peer resolves the same one for the same body (LocomotionProfile.GearFor).
        _gear = LocomotionProfile.GearFor(_gear, flatSpeed);

        // --- THE RIDE CHANNEL (BIKE-4A) -----------------------------------------------------------
        //
        // THE ONE LINE THE FIELD NOTE ASKED FOR. Talon, 2026-09-02: "riding reuses the on-foot
        // running system visually (legs run while mounted)". It does, because the gear is derived
        // from ground speed and a mounted body's ground speed is the BIKE's — at the 9.42 m/s ride
        // cap LocomotionProfile.GearFor answers Sprint and the body runs a full sprint cadence
        // through the frame. So the gear is short-circuited to Idle for as long as anything of the
        // ride weight is on the body: a mounted body can never reach Sprint gait, whatever the
        // speedometer says.
        //
        // AT ANY NON-ZERO WEIGHT, not past a threshold, and the reason is that a half-weight is a
        // MOUNT IN PROGRESS: the legs are already leaving the ground for the pedals, and a gait that
        // kept running until the blend crossed 0.5 would be exactly the "some system was never told"
        // shape. Idle is not a claim that the body is standing still — _gaitAmp, which is what
        // actually expresses the gait, drains from it at its own rate, and the crank pose below is
        // lerped in over the same blend.
        //
        // EXACT IDENTITY AT WEIGHT 0. _rideWeight is 0 on every body nothing has called SetRide on,
        // which is every body in the shipped game today, so this branch never runs for them and the
        // gear, the idle read and the cadence are the ones that shipped, bit for bit.
        float rideW = Mathf.Clamp(_rideWeight, 0f, 1f);
        bool riding = rideW > 0f;
        _ridePosedWeight = rideW;
        if (riding)
            _gear = Gear.Idle;
        bool idle = onFloor && _gear == Gear.Idle && !riding;

        // Sprint READ. No longer drives cadence or amplitude — both come off the ground now — so
        // this exists for the pose layer and for anything that wants to know the body knows. The
        // speed fallback keeps remote proxies (which render from interpolated velocity and never see
        // the sprint flag) in the same gear as the owner.
        bool running = onFloor && (_gear == Gear.Sprint || (sprinting && _gear >= Gear.Jog));
        _runBlend = Mathf.MoveToward(_runBlend, running ? 1f : 0f,
            delta * (running ? RunEaseIn : RunEaseOut));

        // How much of the gait is expressed. It scales the COSMETIC half only (roll, twist, bob,
        // arm swing) plus the blend in and out of a stand — never the stance reach, because scaling
        // the reach is scaling the foot's speed, and that is precisely the skate this rewrite
        // removes. The one place it does touch the legs is the 0.2 s blend out of Idle, where the
        // body is under 0.5 m/s and the residual slip is millimetres.
        _gaitAmp = Mathf.MoveToward(_gaitAmp, onFloor && _gear != Gear.Idle ? 1f : 0f,
            delta * GaitAmpRate);
        _airborne = Mathf.MoveToward(_airborne, onFloor ? 0f : 1f, delta * AirborneFoldRate);

        // --- THE CRANK (BIKE-4A / BIKE-3C row 3) --------------------------------------------------
        //
        // crankRate = speed / (wheelRadius x gearRatio) — Wobble's fixed-gear cadence rule, the one
        // line of that game's arithmetic that ports. INTEGRATED, never derived from a clock, for
        // _gaitPhase's own reason: a phase read off _animTime would slip against a speed that
        // changes, and the whole point of the pedal spin is that it IS the momentum readout.
        //
        // The cranks turn only while PEDALLING, ON THE FLOOR and actually moving (RidePose.CranksTurn
        // — the coast threshold). Otherwise they SETTLE TO LEVEL, which is 3B's COAST row ("cranks
        // level, 3-and-9") and its airborne row ("cranks freeze level") in one rule rather than two:
        // an airborne rider is coasting by construction, because there is no ground for the wheel to
        // be geared to.
        //
        // The mode's torso pitch is EASED, not switched — 12 degrees driving against 6 coasting, so
        // the mode is legible above the waist even when the frame hides the legs.
        bool cranksTurn = riding && RidePose.CranksTurn(_rideMode, onFloor, flatSpeed);
        CrankHz = cranksTurn ? RidePose.CrankHzAt(flatSpeed) : 0f;
        if (riding)
        {
            _crankPhase01 = RidePose.AdvancePhase(_crankPhase01, flatSpeed, delta, cranksTurn);
            _rideTiltRad = Mathf.MoveToward(
                _rideTiltRad, RidePose.TiltFor(_rideMode), delta * RidePose.TiltEaseRate);
        }
        RidePose.Targets ridePose = riding ? RidePose.For(_crankPhase01, _rideTiltRad) : RidePose.None;

        // --- THE CROUCH VERBS AND THE CHAIN (MOVE-5c, spec §12) -----------------------------------
        //
        // Four eased reads over two replicated facts, and that is the whole of the local state this
        // packet adds — see _tuckBlend for why it is the same kind _skidBlend already is.
        //
        // ONE-HOT AND EQUAL-RATE, which is what makes the weighted sum in VerbPose.Blend safe: at
        // most one target is 1, all three move at VerbBlendRate, so a Slide→DuckWalk handover has
        // them moving at equal and opposite rates with a sum of exactly 1 throughout. Asserted in
        // VerbPoseTests rather than assumed.
        //
        // THE BLEND IS NOT A DELAY. The state changed on the tick the motor changed it; these begin
        // moving on that same tick, which is what §12.3 asks for ("the pop-up on release must be
        // visible on the tick the button comes up"). Nothing here is consulted by anything that
        // decides — the body catches up to the state and the state never waits for the body.
        _tuckBlend = Mathf.MoveToward(
            _tuckBlend, _verb == MoveVerb.Tuck ? 1f : 0f, delta * VerbPose.VerbBlendRate);
        _slideBlend = Mathf.MoveToward(
            _slideBlend, _verb == MoveVerb.Slide ? 1f : 0f, delta * VerbPose.VerbBlendRate);
        _duckBlend = Mathf.MoveToward(
            _duckBlend, _verb == MoveVerb.DuckWalk ? 1f : 0f, delta * VerbPose.VerbBlendRate);
        VerbPose.Targets verbPose = VerbPose.Blend(_tuckBlend, _slideBlend, _duckBlend);
        // HOW FAR THE VERB ACTUALLY SHORTENS THIS RIG'S LEG, which is not the same as how far it
        // asked to. SolveLeg degrades to a single-segment rotation on any rig with no shin — every
        // .glb on the roster but the greybox — and on that path a fold is simply not expressible, so
        // the leg does not shorten at all. Everything downstream that follows FROM the shortening —
        // the hip drop, and the gait's own reach — has to take this number rather than the request,
        // or a knee-less body would sink 20 cm through the floor on legs that never bent.
        float verbDropFold = HasKnees ? verbPose.KneeFold : 0f;
        // The chain read eases SLOWER than the verbs, and deliberately: §12.1 wants a partner to see
        // the chain DEEPEN rather than to see four costumes. See VerbPose.ChainBlendRate.
        _chainRead = Mathf.MoveToward(_chainRead, _chainDepth, delta * VerbPose.ChainBlendRate);

        float gaitWeight = _gaitAmp * (1f - _airborne) * verbPose.GaitScale;

        // --- THE JUMP, DERIVED FROM THE ARC (JUMP-1) ----------------------------------------------
        //
        // Two edges and one signed drive, all read off the two facts every peer already has. The
        // motor is not consulted and not touched: this reads a velocity it was handed by value and
        // writes nothing but child transforms. See the constant block for what this replaced.
        //
        // THE DRIVE IS LATCHED ON TOUCHDOWN, not zeroed, and the sim says why. The motor resolves the collision
        // before this sees the velocity, so Y is already 0 on the landing frame while _airborne
        // still has 0.167 s of fold-out left. Zeroing the drive there flipped reach to tuck for
        // that whole fold — the legs snapping from "extended for the floor" back up into a tuck
        // 18 degrees the wrong way, one frame after landing on them. Latching holds the last
        // airborne pose and lets _airborne alone fade it, which is what the fold is for. On the
        // ground the latched value is multiplied by _airborne = 0 everywhere it is read.
        if (!onFloor)
            _airDrive = Mathf.Clamp(localVelocity.Y / AvatarMotor.JumpVelocity, -1f, 1f);

        // TAKE-OFF. Fires on the grounded->airborne edge, and only with real upward speed, so
        // walking off a ledge does not push off it.
        if (_wasOnFloor && !onFloor)
        {
            _maxAirFallMps = 0f;
            // THE COIL'S CLOCK STARTS HERE AND NOWHERE ELSE (MOVE-5f). Zeroed on the take-off edge
            // rather than accumulated across flights: the coil is a reading of THIS jump's hold, and
            // a clock that carried over would deepen the second hop of a chain for a hold the player
            // spent on the first. The blend is NOT zeroed — a body that leaves the ground still
            // unwinding a previous coil unwinds it, rather than snapping.
            _coilRiseSec = 0f;
            // §12.1 DEPTH 3: "the take-off kick fires at full drive". A GATE BYPASS, never a retune —
            // the kick's amplitude (TakeoffKickFraction), its duration (TakeoffKickSec) and its
            // launch snap (LaunchSnapFraction) are untouched and still Talon's. All that changes at
            // depth 3 is that a small hop no longer skips it, so a body deep in a chain pushes off
            // every contact instead of only the committed ones. At depth 0 the predicate is the
            // shipped one, term for term.
            if (_airDrive > TakeoffKickMinDriveFraction || VerbPose.ForcesTakeoffKick(_chainDepth))
                _takeoffLeft = TakeoffKickSec;
            // THE LEAD LEG (RIG-1's launch snap). Latched here, once, and held for the whole flight —
            // see _leadLegLeft. The LEFT foot is in its stance (taking the weight, therefore pushing
            // off) for the first half of the cycle, so the lead — the leg that shoots up — is the
            // right one then, and the left one otherwise.
            _leadLegLeft = _gaitPhase >= 0.5f;
        }
        if (!onFloor)
            _maxAirFallMps = Mathf.Max(_maxAirFallMps, -localVelocity.Y);

        // TOUCHDOWN. The impact speed is the deepest fall speed of the flight, not this frame's —
        // by the time onFloor is true the motor has already resolved the collision and Y is ~0.
        if (!_wasOnFloor && onFloor)
        {
            _takeoffLeft = 0f;
            if (_maxAirFallMps >= LandMinFallMps)
            {
                _landPeak = LandIntensityFor(_maxAirFallMps);
                _landLeft = LandAbsorbSec;
            }
            _maxAirFallMps = 0f;
        }
        _wasOnFloor = onFloor;

        // Both envelopes decay LINEARLY, for the reason anticipationNow's comment gives: a push-off
        // and an absorb each have an end, and an exponential tail would smear a fraction of them
        // across the whole flight.
        float takeoffKick = 0f;
        if (_takeoffLeft > 0f)
        {
            _takeoffLeft -= delta;
            takeoffKick = Mathf.Max(0f, _takeoffLeft / TakeoffKickSec);
        }
        float landDrive = 0f;
        if (_landLeft > 0f)
        {
            _landLeft -= delta;
            landDrive = _landPeak * Mathf.Max(0f, _landLeft / LandAbsorbSec);
        }
        // Rising and falling are two different poses, not two signs of one, and they PARTITION the
        // airborne weight: airTuck + airReach == _airborne, always.
        //
        // The tuck is not keyed to upward speed, and the first pass had it wrong. AirborneFoldRate
        // takes 0.167 s to fold the gait out, and the whole ascent of the ratified arc is only
        // 0.382 s (v = 8.4, g = 22), so by the time the fold finished the upward drive was already
        // down to 0.563 and a velocity-keyed tuck peaked at 13 degrees and then unwound before the
        // apex — the legs at their limpest exactly where a jumping body is at its most folded. So
        // the tuck is the whole airborne weight MINUS whatever the descent has claimed: knees up
        // through the ascent and across the apex, and the legs extending for the floor as the fall
        // builds, fully extended at contact. The take-off kick below is what eases the tuck in.
        float airReach = Mathf.Max(0f, -_airDrive) * _airborne;
        float airTuck = _airborne - airReach;

        // --- THE MOUNTED AIRBORNE POSE (BIKE-4A, 3B §S2) ------------------------------------------
        //
        // S2 IS S1 WITH THE FLOOR TAKEN AWAY, and 3B says so: no choreography, no second partition,
        // just "the existing airborne partition at reduced amplitude (~40%) because the feet stay on
        // the pedals". Reusing the partition rather than authoring a mounted one is the packet's
        // instruction and it is also the only version that cannot drift: rise-tuck and fall-reach
        // stay the two shapes they already are, and a rider simply expresses less of them.
        //
        // SCALED AT THE SOURCE, in one place, and that is the whole reason it is here and not in
        // five. airTuck, airReach and landDrive between them feed the legs, the lift, the knee fold,
        // the arms, the elbow and the torso pitch; scaling them here reduces all six by one number
        // and none of them can end up disagreeing with the others about how airborne a rider is.
        //
        // EXACTLY 1.0 AT WEIGHT 0 (see RidePose.AirAmplitudeAt) — 1f - (0f * x) is 1f, bit for bit —
        // so every jump, fall and landing on every unmounted body is untouched to the bit.
        float rideAirAmp = RidePose.AirAmplitudeAt(rideW);
        airReach *= rideAirAmp;
        airTuck *= rideAirAmp;
        landDrive *= rideAirAmp;

        // --- THE ANTICIPATION COIL (MOVE-5f, approach 1) ------------------------------------------
        //
        // ONE clock and ONE eased weight, over a fact the body already has. See AnticipationCoil for
        // the whole argument; the two lines it comes down to are:
        //
        //   RISING is the read of HELD, because releasing is what triples gravity and ends the rise.
        //          So the coil deepens for exactly as long as the player is still committed, and it
        //          needs no button state, no wire byte and no knowledge of the future.
        //   NOTHING HERE DELAYS ANYTHING. The launch already happened - AvatarMotor set velocity.Y
        //          to JumpVelocity on the press tick, and this class is handed the result. There is
        //          no input it can gate: it writes child transforms and nothing else, which is the
        //          same reason SetVerb's blends cannot gate one.
        //
        // MODE-GATED AT THE SOURCE. At AnticipationMode 0 - the shipped setting - and at mode 2,
        // where the coil is a gravity term with no pose at all, the clock never runs and the weight
        // is driven to zero, so every downstream term below is multiplied by an exact 0f and the
        // body is bit-identical to the one that shipped.
        bool coilOn = AnticipationMode == AnticipationCoil.RealCoil;
        if (coilOn && !onFloor && localVelocity.Y > 0f)
            _coilRiseSec += delta;
        // The ramp IN is taken directly rather than eased, and that is deliberate: Depth is a
        // smoothstep, so it already leaves 0 with zero slope and arrives at 1 with zero slope, and a
        // MoveToward laid over it would clip the middle of the curve for no gain. The release IS
        // eased, linearly, for the reason every other envelope in this file is - the take-off kick
        // and the landing absorb both decay linearly because a release has an end.
        float coilTarget = coilOn && !onFloor && localVelocity.Y > 0f
            ? AnticipationCoil.Depth(_coilRiseSec, AnticipationCoilSec) * AnticipationDepth
            : 0f;
        _coilBlend = coilTarget > _coilBlend
            ? coilTarget
            : Mathf.MoveToward(_coilBlend, coilTarget, delta * AnticipationCoil.ReleaseRate);
        // AIRBORNE-WEIGHTED, and it is what keeps the coil out of CrouchDropM's world entirely: the
        // fold below can only ever be an AIRBORNE fold, which lifts a foot rather than dropping a
        // hip, so a knee-less rig has nothing to sink through the floor on. See AnticipationCoil.
        //
        // STATED RATHER THAN GLOSSED: this is a SECOND ease on the way in, because _airborne is
        // itself climbing at AirborneFoldRate for the first 0.167 s. The product peaks a little
        // later than Depth alone would and is a little gentler over the first three frames — which
        // is the right way round for a coil that must not pop on the take-off frame, and it is why
        // the knob is spelled as the rise time at which DEPTH saturates rather than as the frame the
        // pose peaks on. It also means the coil cannot survive a landing: _airborne goes to zero.
        float coilWeight = _coilBlend * _airborne;
        AnticipationCoil.Targets coilPose = AnticipationCoil.For(coilWeight);

        // --- THE LEAN: ACCELERATION, NEVER SPEED ------------------------------------------------
        //
        // Differentiated here rather than plumbed in, because this class already receives the exact
        // quantity it needs — the velocity in the BODY's own frame — every frame from all three call
        // paths (offline, owner prediction, remote proxy). Forward acceleration becomes pitch and
        // lateral acceleration becomes roll, so the body leans into a launch, stands up at speed,
        // pitches back under braking, and banks into a turn.
        //
        // Low-passed because the body yaws under AvatarMotor.TurnLerp, and a rotating frame injects
        // apparent acceleration that would otherwise twitch the pose on every corner.
        float forwardSpeed = -localVelocity.Z;
        float lateralSpeed = localVelocity.X;
        if (delta > 1e-5f)
        {
            float accelFwd = (forwardSpeed - _prevForwardSpeed) / delta;
            float accelLat = (lateralSpeed - _prevLateralSpeed) / delta;

            // THE ANTICIPATION (second amendment). The ONSET of an acceleration - not the
            // acceleration itself - throws the body the opposite way for a moment. Detected on the
            // jerk, which steps by two orders of magnitude the tick a player commits and is
            // otherwise small, so this fires on a decision and not on a wobble. Re-armed only after
            // the previous flinch has run out, so a sustained ramp flinches once rather than
            // buzzing.
            float jerk = (accelFwd - _prevAccelFwd) / delta;
            if (_anticipationLeft <= 0f && Mathf.Abs(jerk) > AnticipationJerkThreshold)
            {
                // OPPOSITE the action: a launch (jerk forward) rocks the body BACK, a brake rocks
                // it forward. Sign here is the rig's, where a negative pitch is forward.
                _anticipation = Mathf.Sign(jerk) * AnticipationRad;
                _anticipationLeft = AnticipationSec;
            }
            _prevAccelFwd = accelFwd;

            float k = 1f - Mathf.Exp(-delta / LocomotionProfile.AccelSmoothingSec);
            _leanRad = Mathf.Lerp(_leanRad, LocomotionProfile.LeanForAccel(accelFwd), k);
            _turnRollRad = Mathf.Lerp(_turnRollRad, LocomotionProfile.RollForAccel(accelLat), k);
        }
        _prevForwardSpeed = forwardSpeed;
        _prevLateralSpeed = lateralSpeed;

        // Decays linearly rather than exponentially: a flinch has an end, and an exponential tail
        // would leave a fraction of it smeared across the whole launch, which is the averaging this
        // amendment exists to remove.
        float anticipationNow = 0f;
        if (_anticipationLeft > 0f)
        {
            _anticipationLeft -= delta;
            anticipationNow = _anticipation * Mathf.Max(0f, _anticipationLeft / AnticipationSec);
        }

        // The swing weight. Eased both ways, and it is the thing that makes an interrupted swing
        // safe: the pose below is entirely weight-multiplied, so a swing that never reaches its
        // Recover phase still returns the body to rest within SwingBlendRate.
        _swingBlend = Mathf.MoveToward(_swingBlend, _swinging ? 1f : 0f, delta * SwingBlendRate);
        float swing = _swingDrive * _swingBlend;
        // The forward half of the stroke only — see SwingLeanRad for why the coil does not lean back.
        float swingForward = Mathf.Max(0f, swing);

        // Squash/stretch spring toward rest.
        Vector3 accel = (Vector3.One - _scale) * ScaleStiffness - _scaleVel * ScaleDamping;
        _scaleVel += accel * delta;
        _scale += _scaleVel * delta;
        // Idle breathing: a slow, tiny vertical swell layered onto the spring.
        float breathe = idle ? 1f + 0.02f * Mathf.Sin(_animTime * 2.2f) : 1f;
        // Coil on the wind-up, stretch on the follow-through. Multiplied onto the spring's result
        // rather than injected into the spring, so it is bounded by the blend and cannot ring.
        // Volume is roughly preserved (half the vertical change, opposite sign, on X and Z), which is
        // what keeps a squat read as a squat instead of as a shrink.
        float swingSquash = SwingSquash * swing;
        // NO JUMP TERM HERE ANY MORE (RIG-1). JUMP-1 left one vertical scale on the landing — a 0.12
        // compression standing in for the knee flex a knee-less rig could not do — and this rig has
        // knees. The absorb is LandAbsorbKneeFold, in the legs, with the hip drop that follows from
        // it (see CrouchDropM). What is left on this node is the idle breath, the swing coil and the
        // bump spring, exactly as it was before the jump ever wrote here.
        _body.Scale = new Vector3(
            _scale.X * (1f - (swingSquash * 0.5f)),
            Mathf.Max(0.4f, _scale.Y * breathe * (1f + swingSquash)),
            _scale.Z * (1f - (swingSquash * 0.5f)));

        // --- THE GAIT, DERIVED FROM THE GROUND ---------------------------------------------------
        //
        // stride x cadence = ground speed. Cadence is a shallow function of speed (so most of the
        // extra speed becomes STRIDE, which is what "a stride, not a stuttering step" means
        // arithmetically), the stance reach is what that identity leaves over, and the duty factor
        // is whatever the leg can actually reach in the time available. Every one of these is
        // LocomotionProfile's, tested without an engine.
        //
        // THE LEG THE GAIT IS WALKING ON, NOT THE ONE THE RIG WAS BUILT WITH (MOVE-5c). A crouch
        // genuinely shortens the leg — that is the whole of CrouchDropM — and deriving a gait from
        // the FULL leg while the body stands on a folded one is foot-skate, precisely the defect
        // MOVE-1 exists to have removed: the hip opens to the angle a 0.44 m leg needs to cover a
        // 0.20 m track, and the foot, hanging 16% closer to that hip, covers 0.17 m of it. The
        // identity survives the substitution rather than being patched around it — the reach cap
        // falls with the leg, and DutyFactorAt absorbs the difference as a longer stance, which is
        // exactly what a crouched walk does.
        //
        // EXACTLY _legLengthM AT verbDropFold = 0 (x * (1f - 0f) is x, bit for bit), so every gait
        // MOVE-1 ratified is untouched. The LANDING absorb is deliberately not in this term, for the
        // same reason it is not in the drop gate: its 0.10 over 0.26 s is a shipped arc and the
        // packet's fourth rule says approved things do not move.
        float gaitLegM = _legLengthM * (1f - verbDropFold);
        CadenceHz = LocomotionProfile.CadenceAt(flatSpeed);
        StanceReachM = LocomotionProfile.StanceReachAt(flatSpeed, gaitLegM);
        DutyFactor = LocomotionProfile.DutyFactorAt(flatSpeed, gaitLegM);

        // --- THE CLIP LAYER (ANIM-M3) ---------------------------------------------------------------
        //
        // Advanced HERE — after the gear and the ground-derived constants, before anything is written
        // to a joint — so "the tree proposes, the procedural layer disposes" is an ordering in code
        // rather than a hope about node callbacks. The tree runs in Manual callback mode for exactly
        // that reason.
        //
        // The three readouts above are RE-SOURCED from the clip while it drives, and that is not a
        // cosmetic tidy: CadenceHz is what _gaitPhase integrates, _gaitPhase is what the footstep
        // latch and the swing-leg lift read, and a phase advancing at the PROCEDURAL cadence under a
        // clip playing at the WARPED cadence is two gaits at two speeds on one body. DutyFactor is
        // the clip's authored duty (invariant under time-warping, since warping scales the cycle and
        // the stance together). StanceReachM is left as-is: both gait clips sit on the same leg cap
        // ANIM-M2 measured, so the reach the ground asks for and the reach the clip has agree by
        // construction above ~1.44 m/s.
        if (_clips != null)
        {
            EnterClipState(Anim.AvatarActionStates.Derive(
                incapacitated: false, staggering: _staggering, grounded: onFloor,
                skidding: _skidding, verticalVelocity: localVelocity.Y,
                previous: _clipAction, stateElapsedSec: _clipStateElapsedSec,
                launchSec: _clips.LengthOf(Anim.AvatarClipNames.JumpLaunch),
                landSec: _clips.LengthOf(Anim.AvatarClipNames.JumpLand)), delta);
            _clipHold = DeriveHoldState();
            CadenceHz = Anim.ClipTimeWarp.CadenceHzFor(_gear, flatSpeed);
            DutyFactor = Anim.ClipTimeWarp.DutyFactorFor(_gear) > 0f
                ? Anim.ClipTimeWarp.DutyFactorFor(_gear)
                : DutyFactor;
            _clipPose = _clips.Advance(
                delta, _gear, flatSpeed, _clipAction, _clipHold,
                ClipSeekSecondsFor(_clipAction),
                _clipHold == Anim.AvatarHoldState.NetSwing
                    ? Anim.ClipTimeAnchor.SecondsFromProgress(
                        _swingProgress01, _clips.LengthOf(Anim.AvatarClipNames.HoldNetSwing))
                    : -1f,
                _clipTier);
        }

        // THE RIDE DISPLACES THE STEP RATE (BIKE-4A). Forcing the gear to Idle above already stops
        // _gaitPhase advancing and drains _gaitAmp, but CadenceHz is derived from SPEED, not from
        // the gear — LocomotionProfile.CadenceAt(9.42) is the sprint cadence whatever gear the body
        // is in, and this is a PUBLIC readout that the footstep director and the self-test both read.
        // Left alone it would report a sprinting body under a pedalling one, which is the field
        // note's complaint surviving in the one place a test would look for it. A rider takes no
        // steps; the crank rate is on CrankHz instead.
        //
        // Placed after the clip block so it also displaces ClipTimeWarp's answer, and multiplicative
        // so a partial mount reports a partial cadence: exactly CadenceHz at ride weight 0.
        if (riding)
            CadenceHz *= 1f - rideW;

        // INTEGRATED, never derived from a clock — see _gaitPhase. One foot's cycle is two steps,
        // hence the half.
        if (onFloor && _gear != Gear.Idle)
        {
            _gaitPhase += delta * CadenceHz * 0.5f;
            _gaitPhase -= Mathf.Floor(_gaitPhase);
        }

        // A foot enters stance twice per cycle — at phase 0 (left) and 0.5 (right). That IS the
        // footfall, so the SFX layer squeaks on the real plant rather than on distance walked.
        int plantIndex = _gaitPhase < 0.5f ? 0 : 1;
        if (plantIndex != _lastPlantIndex)
        {
            _lastPlantIndex = plantIndex;
            if (onFloor && _gaitAmp > 0.2f)
                _footPlanted = true;
        }

        float phaseL = _gaitPhase;
        float phaseR = _gaitPhase + 0.5f >= 1f ? _gaitPhase - 0.5f : _gaitPhase + 0.5f;
        float trackL = LocomotionProfile.FootTrackAt(phaseL, DutyFactor, StanceReachM);
        float trackR = LocomotionProfile.FootTrackAt(phaseR, DutyFactor, StanceReachM);
        // --- WHERE THE HIP ANGLE COMES FROM (ANIM-M3) ---------------------------------------------
        //
        // With the clip layer on, the base hip angle is the CLIP's, read back off ThighL/ThighR after
        // the tree evaluated. It arrives UNSCALED by gaitWeight, deliberately: the blend space
        // already fades to Idle below walking speed and the transition node already fades between
        // action clips, so multiplying by gaitWeight would be a second fade over the first — and it
        // would zero the jump and knock-out poses outright, since gaitWeight is _gaitAmp × (1 −
        // _airborne).
        //
        // It goes through the SAME SolveLeg call the derived gait uses. That is the whole reason the
        // director hands back an ANGLE rather than writing ThighL.Rotation itself: LimbIk stays the
        // sole writer of the knee, and LimbIkTests.RigidGaitFootPositions_AreReproducedExactly stays
        // a test instead of a deleted one.
        bool clipLegs = _clips != null;
        // gaitLegM, not _legLengthM — see its own note. Identical outside a crouch verb; inside one
        // it is what stops the hip asking for a track the folded leg cannot reach.
        float legAngleL = clipLegs
            ? _clipPose.LegAngleL
            : LocomotionProfile.LegAngleFor(trackL, gaitLegM) * gaitWeight;
        float legAngleR = clipLegs
            ? _clipPose.LegAngleR
            : LocomotionProfile.LegAngleFor(trackR, gaitLegM) * gaitWeight;
        // THE LIFT STAYS PROCEDURAL, and this is the answer to ANIM-M2's open item 3 (FootLiftAt has
        // no home in the clips). Three reasons, any one sufficient. It is a TRANSLATION, and the clip
        // library's first authoring rule is rotation-only — a translation track on ThighL is also what
        // synthesised a 74.8 mm phantom rest offset on Body during ANIM-M2's export. It derives from
        // DutyFactor and StanceReachM, which are outputs of the very identity the time-warp
        // preserves, so it stays in phase with a warped clip for free. And ThighL is the node the
        // solver owns; a keyed translation there would be a second writer on it.
        float liftL = LocomotionProfile.FootLiftAt(phaseL, DutyFactor, StanceReachM) * gaitWeight;
        float liftR = LocomotionProfile.FootLiftAt(phaseR, DutyFactor, StanceReachM) * gaitWeight;

        // --- THE JUMP'S LEGS (JUMP-1) --------------------------------------------------------------
        // ADDITIVE on top of the gait rather than a lerp toward a target, and deliberately so: the
        // gait's own contribution is already multiplied to zero by gaitWeight the moment the feet
        // leave the ground, so there is nothing here to blend against, and an additive term keeps
        // the landing absorb layering over a gait that is coming BACK rather than fighting it.
        //
        // Positive angle swings a foot forward (LegAngleFor negates against the track), so the
        // push-off kick is negative — the trail leg driving down and behind — and the tuck and the
        // reach are both forward, at very different amplitudes and with only the tuck lifting.
        //
        // THE LAUNCH SNAP (RIG-1). The kick used to be one shared term with the same sign on both
        // legs, which is why take-off read as a symmetric limp: both legs swept back together and then
        // both tucked together. It is now split by LEAD and TRAIL and the two point OPPOSITE ways at
        // the launch instant — the lead thigh driving forward and up with a deep knee fold, the trail
        // leg keeping JUMP-1's push-off exactly as ratified. That is a sign flip inside an envelope
        // that already exists: no new state, no new signal, no latency, nothing on the wire.
        float legAirCommon =
            LocomotionProfile.MaxLegSwingRad
            * ((JumpTuckFraction * airTuck)
                + (JumpReachFraction * airReach)
                + (LandPlantFraction * landDrive));
        float kickLead = LocomotionProfile.MaxLegSwingRad * LaunchSnapFraction * takeoffKick;
        float kickTrail = -LocomotionProfile.MaxLegSwingRad * TakeoffKickFraction * takeoffKick;
        float liftAir = _legLengthM * JumpTuckLift * airTuck;
        //
        // ALL FOUR SKIPPED UNDER THE CLIP LAYER (ANIM-M3). Jump_Launch, Jump_Air and Jump_Land are
        // authored clips that key exactly these channels, and the state machine already routes to
        // them off the same replicated Grounded/Velocity.Y this envelope is derived from. Adding the
        // procedural terms on top would be two jumps on one body. The LIFT is skipped with them for
        // the same reason it is skipped nowhere else: liftAir is the tuck's, and the tuck is in the
        // clip.
        if (!clipLegs)
        {
            legAngleL += legAirCommon + (_leadLegLeft ? kickLead : kickTrail);
            legAngleR += (legAirCommon * JumpTrailLegFraction) + (_leadLegLeft ? kickTrail : kickLead);
            liftL += liftAir;
            liftR += liftAir * JumpTrailLegFraction;
        }

        // --- WHAT THE KNEE DOES (RIG-1) -----------------------------------------------------------
        // A fold is a fraction of the leg's own length that the foot target is pulled IN toward its
        // hip, and it is the only channel that bends a knee at all: a target at exactly leg length
        // solves to a straight limb, which is the whole reason MOVE-1's gait cannot drift (see
        // LimbIk). So the knee appears in precisely three places and nowhere else.
        //
        //   THE LAUNCH SNAP    lead leg only, deep — the foot comes up toward the hip while the thigh
        //                      drives forward. Together those two are "one leg shoots up".
        //   THE APEX TUCK      both legs, moderate, scissored — knees up across the top of the arc.
        //   THE LANDING ABSORB both legs, and the ONE fold that also lowers the rig (see below):
        //                      the feet are on the floor, so a bent knee means dropped hips.
        //
        // The downward reach folds NOTHING. The legs are extending to meet the floor, and JUMP-1's
        // asymmetry between rising and falling is most of what separates a jump from an arc.
        float landFold = LandAbsorbKneeFold * landDrive;
        float tuckFold = JumpTuckKneeFold * airTuck;
        float snapFold = LaunchSnapKneeFold * takeoffKick;
        // THE CROUCH VERBS JOIN THE LANDING ABSORB ON THIS EXACT TERM (MOVE-5c), and they belong
        // there for the landing absorb's own reason: both are a knee bent while the feet are on the
        // floor, so both lower the hips by exactly what they shortened the legs by. A verb crouch
        // that lowered the rig without folding the knee would float the feet; one that folded the
        // knee without lowering the rig would sink them. There is one number, and this is it.
        //
        // MIN, NOT CLAMP, and the identity is the point: verbPose.KneeFold is exactly 0 outside a
        // verb and landFold never exceeds LandAbsorbKneeFold (0.10), so at Verb = Normal this
        // returns landFold bit-for-bit and the approved landing is untouched. MaxLimbFold is the
        // same structural cap the knees already carry.
        //
        // THE REQUEST, not what the rig can do: a knee-less rig is handed the fold anyway and
        // SolveLeg discards it there (nothing is synthesised). Only the DROP below has to know the
        // difference, because only the drop is a claim about geometry that did or did not happen.
        float groundFold = Mathf.Min(landFold + verbPose.KneeFold, MaxLimbFold);
        // THE DROP TAKES verbDropFold, NOT THE REQUEST, and that is the difference between a crouch
        // and a body sunk through the floor — see verbDropFold at the top of this method. On a
        // knee-less rig the leg never shortened, so there is nothing for the hips to drop by, and
        // what that body gets instead is the torso pitch, the arm sweep and the planted legs: all
        // rotations, all expressible on any rig. It stands at full height and still reads as a slide
        // rather than as a walk — honest degradation, nothing synthesised.
        //
        // landFold is NOT gated with it. The landing absorb has always dropped by its own 4.4 cm on
        // every rig, knees or not, and that body is ratified — see the packet's fourth rule. The same
        // mismatch is present there in miniature and is recorded as a finding rather than fixed here,
        // because fixing it would move an approved arc.
        float groundDropFold = Mathf.Min(landFold + verbDropFold, MaxLimbFold);
        // THE COIL'S FOLD (MOVE-5f) — BOTH legs, symmetrically, and that symmetry is half of what
        // separates it from the launch snap it overlaps. The snap is a SCISSOR that lasts 0.16 s
        // (lead thigh up and deeply folded, trail leg straight and driving back); the coil is
        // symmetric and lasts the rise. Additive with both, and clamped by MaxLimbFold with them,
        // which is precisely the case the clamp's own comment names: "a landing out of a launch
        // snap stacks two of them".
        //
        // AIRBORNE BY CONSTRUCTION (coilWeight carries _airborne), so it never reaches
        // groundDropFold and can never lower the rig. That is why this needs no HasKnees gate where
        // MOVE-5c's verb fold did: the gate exists to stop a knee-less body dropping its hips for a
        // fold that never happened, and there is no drop here to gate. A knee-less rig is handed the
        // request, SolveLeg discards it, and what that body gets instead is the torso gather and the
        // arm fold below - all rotations, expressible on any rig. Honest degradation, nothing
        // synthesised, and AnticipationCoilTests pins the drop at exactly zero rather than trusting
        // this paragraph.
        float coilFold = coilPose.KneeFold;
        float kneeFoldL = Mathf.Clamp(
            groundFold + tuckFold + coilFold + (_leadLegLeft ? snapFold : 0f), 0f, MaxLimbFold);
        float kneeFoldR = Mathf.Clamp(
            groundFold + (tuckFold * JumpTrailLegFraction) + coilFold
                + (_leadLegLeft ? 0f : snapFold),
            0f, MaxLimbFold);
        // §12.1 DEPTH 2: the trailing leg has to be STRAIGHT for "a straight silhouette line from
        // trailing toe to leading hand" to be a line at all. Airborne-weighted, so it never argues
        // with the grounded folds above.
        float chainStride = VerbPose.ChainStrideWeight(_chainRead) * _airborne;
        if (chainStride > 0f)
        {
            if (_leadLegLeft)
                kneeFoldR = Mathf.Lerp(kneeFoldR, 0f, chainStride);
            else
                kneeFoldL = Mathf.Lerp(kneeFoldL, 0f, chainStride);
        }

        // THE HIP DROP, and it is geometry rather than taste: with the feet planted, shortening both
        // legs by L*fold lowers everything above them by exactly that. Written to the crouch node so
        // the pose's own >= 0 lift invariant is untouched — see CrouchDropM for the full argument.
        // Only the GROUNDED folds contribute: an airborne fold has no floor to push against, so its
        // knee simply lifts the foot, which is what a tuck is. groundDropFold is the landing absorb
        // plus whatever crouch verb the rig can actually express — see its own note — and is exactly
        // landFold at Verb = Normal, so the shipped landing drop is unchanged to the bit.
        // THE RIDER'S HIPS DO NOT DROP (BIKE-4A). Every gram of this term is the geometry of a knee
        // bent WITH THE FEET ON THE FLOOR — that is CrouchDropM's whole argument — and a rider's
        // weight is on the saddle. Left in, a mounted landing would sink the body through the bike
        // it is sitting on by the absorb's own 4.4 cm, and 3B says the same thing from the other
        // side: "the hop's landing on the saddle needs no landing absorb — the bike's LandSquash IS
        // the absorb, and doubling it on the body reads as two impacts from one hop."
        //
        // Lerped rather than switched, so a dismount hands the drop back over the same blend it took
        // it away on; exactly groundDropFold at weight 0.
        CrouchDropM = _legLengthM * Mathf.Lerp(groundDropFold, 0f, rideW);
        // Bounded at the leg's own anatomical limit, because ADDITIVE means the sum can exceed it:
        // landing out of a full sprint puts the gait's own 38.7 degrees and the absorb's 13 on the
        // same hip, and 52 degrees is not a landing, it is a split. The clamp sits BEFORE the skid
        // block on purpose — SKID-1's pose is a lerp to 0.92 of this same limit and is already
        // inside it, so nothing ratified changes shape here.
        //
        // NOT APPLIED UNDER THE CLIP LAYER, and the numbers say why rather than a preference.
        // MaxLegSwingRad is 38.74 deg and it is the STANCE limit — the widest half-swing whose foot
        // the leg can still reach the ground with. The authored clips honour it exactly through
        // stance (both Walk and Run enter at +38.7 deg and leave at -38.7 deg, measured off the
        // shipped bytes) and then deliberately overshoot to -48.6 deg through the SWING, where there
        // is no ground to reach. Clamping a clip here would flatten the swing-through arc of every
        // stride to a stance limit that does not apply to it. The clamp exists because the
        // procedural terms above are ADDITIVE and can sum past the limit; nothing above is additive
        // once the clip owns the channel.
        if (!clipLegs)
        {
            legAngleL = Mathf.Clamp(legAngleL,
                -LocomotionProfile.MaxLegSwingRad, LocomotionProfile.MaxLegSwingRad);
            legAngleR = Mathf.Clamp(legAngleR,
                -LocomotionProfile.MaxLegSwingRad, LocomotionProfile.MaxLegSwingRad);
        }

        // --- THE BRAKE (SKID-1) — the one pose branch this packet is allowed -----------------------
        // Both feet are thrown out in front of the body and stop lifting, which is what a brake is:
        // the legs have gone from carrying the body forward to arguing with it. Blended rather than
        // switched, so a skid that ends early (the stick released, a wall) unwinds instead of
        // snapping the legs back under the hips. The backward pitch is added into tiltX below,
        // inside its clamp.
        // The BLEND still runs under the clip layer — SkidLeanRad reads it below, and the backward
        // pitch is a modifier the clips do not carry — but the leg override does not: `Skid` is an
        // authored clip and the state machine is already in it, with the same 0.15 s crossfade this
        // lerp was standing in for.
        _skidBlend = Mathf.MoveToward(_skidBlend, _skidding ? 1f : 0f, delta * SkidBlendRate);
        if (!clipLegs && _skidBlend > 0.001f)
        {
            // Positive hip angle swings a foot FORWARD (LegAngleFor negates against the track), and
            // the amplitude is a fraction of the leg's own anatomical limit rather than a typed
            // angle — so the day the real cast lands with different legs, the brake scales with them.
            float plant = LocomotionProfile.MaxLegSwingRad * SkidLegPlantFraction;
            legAngleL = Mathf.Lerp(legAngleL, plant, _skidBlend);
            legAngleR = Mathf.Lerp(legAngleR, plant * SkidTrailLegFraction, _skidBlend);
            liftL = Mathf.Lerp(liftL, 0f, _skidBlend);
            liftR = Mathf.Lerp(liftR, 0f, _skidBlend);
        }

        // --- THE VERB LEGS AND THE CHAIN'S STRIDE (MOVE-5c) ---------------------------------------
        //
        // BOTH BRANCHES, unlike the skid block above, and the reason is a fact about the clip library
        // rather than a preference: `Skid` is an authored clip and the state machine is already
        // playing it, so SKID-1's leg override is redundant under clips. There is no Slide clip, no
        // Tuck clip and no chain clip — AvatarClipNames.All carries fourteen names and none of them
        // is a crouch — so if this block skipped the clip path, the verbs would be invisible on every
        // body that has a clip layer, which is every body Talon actually plays.
        //
        // LERPS TOWARD A BOUNDED TARGET, never an additive term, for the reason the clamp above
        // states: the clip's own swing deliberately overshoots MaxLegSwingRad through the swing
        // phase, so an additive term here would stack on top of an already-exceeded limit. A lerp
        // toward a bounded target cannot exceed the bound whatever it starts from.
        //
        // THE SLIDE'S LEGS ARE PLANTED. §4.4: the speed decays along the heading and the stick
        // contributes no acceleration — the feet are being dragged, not taking steps. The two legs
        // point OPPOSITE ways (lead out in front, trail folded under) because a deep symmetric crouch
        // reads as a body that merely got shorter, which is SkidTrailLegFraction's own argument.
        if (verbPose.LegsPlanted && _slideBlend > 0.001f)
        {
            float lead = LocomotionProfile.MaxLegSwingRad * VerbPose.SlideLeadLegFraction;
            float trail = LocomotionProfile.MaxLegSwingRad * VerbPose.SlideTrailLegFraction;
            legAngleL = Mathf.Lerp(legAngleL, _leadLegLeft ? lead : trail, _slideBlend);
            legAngleR = Mathf.Lerp(legAngleR, _leadLegLeft ? trail : lead, _slideBlend);
            liftL = Mathf.Lerp(liftL, 0f, _slideBlend);
            liftR = Mathf.Lerp(liftR, 0f, _slideBlend);
        }

        // §12.1 DEPTH 2 — THE ONE TELL THE SPEC MARKS AS REQUIRED. "The first depth that changes the
        // outline rather than the shading, so a partner 30 m away in the dark sees a diagonal where
        // there was an upright." The trailing leg goes to the full anatomical swing BACKWARD with a
        // straight knee (see the fold block above), the lead leg forward, and the resulting 75-degree
        // split is a long-jump stride rather than a jog. Airborne-weighted through chainStride, so it
        // arrives as the body leaves the ground and is gone before the feet are back on it — except
        // at depth 4, whose hold is a TORSO term and stays out of the legs.
        if (chainStride > 0f)
        {
            float chainLead = LocomotionProfile.MaxLegSwingRad * VerbPose.ChainLeadLegFraction;
            float chainTrail = LocomotionProfile.MaxLegSwingRad * VerbPose.ChainTrailLegFraction;
            legAngleL = Mathf.Lerp(legAngleL, _leadLegLeft ? chainLead : chainTrail, chainStride);
            legAngleR = Mathf.Lerp(legAngleR, _leadLegLeft ? chainTrail : chainLead, chainStride);
        }

        // --- THE RIDE'S LEGS (BIKE-4A) ------------------------------------------------------------
        //
        // LAST OF THE LEG BLOCKS, and the order is the priority ladder 3B §2 states: the ride
        // channel sits above the verbs, the gait and the jump poses and displaces them in proportion
        // to its weight. (Incapacity and water sit above IT, and both are handled higher up — the
        // early return parks this channel outright.)
        //
        // A LERP TOWARD A BOUNDED TARGET, never an additive term, for the verb block's own reason:
        // the clip layer's swing deliberately overshoots MaxLegSwingRad, so an additive term would
        // stack on top of an already-exceeded limit. A lerp cannot exceed its target whatever it
        // starts from — and it is also what makes an interrupted mount safe, since a blend that
        // reverses simply retraces the same path (3B §7: the channel is a SCRUB, not a timeline).
        //
        // BOTH BRANCHES, clip and procedural alike, for the verb block's other reason: there is no
        // ride clip and there never can be one (BIKE-3B's opening constraint — the body is
        // procedural and no ride clips can be authored). A term that lived only in the !clipLegs
        // branch would leave every clip-bearing body running a stride on a bicycle, which is the
        // field note verbatim.
        //
        // THE AIRBORNE PARTITION SURVIVES UNDERNEATH IT, which is the whole of S2. The target is the
        // crank pose PLUS the air term — already scaled to 40% at its source — rather than the crank
        // pose alone, so a mounted jump still tucks through the rise and reaches through the fall
        // instead of holding a seated pose through the arc. The take-off kick is deliberately NOT in
        // the target: a push-off kick is a runner's leg leaving the ground, and a rider's is
        // choreography T3, which is out of this packet's scope and waits on Talon's jump ruling.
        //
        // SYMMETRIC, unlike every leg block above it. The gait's asymmetries — lead and trail,
        // JumpTrailLegFraction — describe a body with one foot doing something different from the
        // other. Both of a rider's feet are on pedals; the only asymmetry a rider has is the half
        // revolution between the cranks, and RidePose.For already carries it.
        if (riding)
        {
            legAngleL = Mathf.Lerp(legAngleL, ridePose.HipPitchL + legAirCommon, rideW);
            legAngleR = Mathf.Lerp(legAngleR, ridePose.HipPitchR + legAirCommon, rideW);
            liftL = Mathf.Lerp(liftL, liftAir, rideW);
            liftR = Mathf.Lerp(liftR, liftAir, rideW);
            // The knee joins the hip, on the same weight and inside the same structural cap. The
            // GROUNDED folds go with it: a rider's weight is on the saddle, not on its feet, so
            // there is nothing here for CrouchDropM to lower — see RidePose.Targets.KneeFoldL.
            kneeFoldL = Mathf.Clamp(
                Mathf.Lerp(kneeFoldL, ridePose.KneeFoldL + tuckFold, rideW), 0f, MaxLimbFold);
            kneeFoldR = Mathf.Clamp(
                Mathf.Lerp(kneeFoldR, ridePose.KneeFoldR + tuckFold, rideW), 0f, MaxLimbFold);
        }

        // The body rides its own legs. A leg rotated theta off vertical is L(1 - cos theta) shorter
        // than an upright one, so the body genuinely drops as the stride opens and rises as the feet
        // pass under it — the inverted pendulum, for free, in phase with the actual legs rather than
        // an authored |sin| that hoped to line up with them. Referenced from the LOWEST point so it
        // is never negative, which keeps the floor-clamp invariant structural.
        //
        // The MINIMUM of the two, not the maximum, and the first pass had it wrong. The body's
        // height is set by the leg closest to underneath it — the one taking the weight — and the
        // other one is in the air where it supports nothing. Taking the max let the SWINGING leg
        // pin the term at its extreme, and the lab measured the consequence: a bob of 0.0012 m at a
        // jog against 0.0476 m at a walk, purely because the faster gait spends more of its cycle
        // with one leg thrown out. Measured, not reasoned.
        float legSplit = Mathf.Min(Mathf.Abs(legAngleL), Mathf.Abs(legAngleR));
        float maxDrop = _legLengthM * LocomotionProfile.LegShorteningFraction;
        float bob = Mathf.Max(0f,
            (maxDrop - (_legLengthM * (1f - Mathf.Cos(legSplit)))) * BobFollow * gaitWeight);

        float gaitCycle = _gaitPhase * Mathf.Tau;
        // Roll toward the planted side, once per cycle; plus the bank from a turn (a lateral
        // acceleration, see above), which is a second, independent reason a body leans over.
        // THE RIDE'S ROLL joins them (BIKE-4A): the rider banks with the machine. The angle is the
        // BIKE-2x handling harness's, handed in through SetRide rather than recomputed here — the
        // machine's roll is the sibling packet's channel and a body that derived its own would be a
        // second opinion about which way the bike is leaning.
        //
        // THE TURN BANK CROSSFADES OUT AS THE RIDE COMES IN, and that is not a tidy-up — it is the
        // whole reason wiring the lean was safe (orchestrator, 2026-09-02, at wave-4 integration).
        // `_turnRollRad` is a low-passed LATERAL ACCELERATION and it is computed unconditionally,
        // mounted or not. A cornering rider therefore has two live descriptions of one physical
        // fact: their own sideways acceleration, and the machine's lean. Adding both banks the body
        // TWICE through every corner — the exact "second opinion" the paragraph above refuses,
        // arriving through the back door because nobody suppressed the first one. So the two are a
        // crossfade on ride weight, not a sum: on foot you bank from your own acceleration, mounted
        // you bank with the machine, and mid-blend you get one bank's worth of each.
        //
        // At rideW = 0 this is byte-for-byte the on-foot expression it replaced, so nothing about an
        // unmounted body moves.
        float roll = Mathf.Sin(gaitCycle) * GaitRollRad * gaitWeight
            + _turnRollRad * (1f - rideW)
            + SwingRollRad * swing
            + (_rideLeanRad * rideW);
        // Shoulders against hips, opposite the roll: the counter-rotation that separates a run from
        // a body being slid along the ground.
        float torsoTwist = -Mathf.Sin(gaitCycle) * GaitTorsoTwistRad * gaitWeight;

        // Held-item bob: rides the same phase, so a carried prop moves with the gait rather than
        // sitting dead-still. SandboxAvatar adds this to the carry anchor each frame.
        float carryBobY = Mathf.Abs(Mathf.Sin(gaitCycle)) * CarryBobHeight * gaitWeight
            + (1f - gaitWeight) * CarryIdleBreath * Mathf.Sin(_animTime * 2.2f);
        float carryBobX = Mathf.Sin(gaitCycle) * CarryBobSway * gaitWeight;
        CarryBobOffset = new Vector3(carryBobX, carryBobY, 0f);

        // Idle fidget: every few seconds, a quick curious look-around wiggle.
        float fidgetYaw = 0f;
        if (idle)
        {
            _fidgetTimer -= delta;
            if (_fidgetTimer <= 0f)
            {
                _fidgeting = 0.6f;
                _fidgetTimer = 3.5f + (float)GD.RandRange(0.0, 3.0);
            }
        }
        if (_fidgeting > 0f)
        {
            _fidgeting -= delta;
            fidgetYaw = Mathf.Sin((0.6f - _fidgeting) * Mathf.Tau * 1.7f) * 0.09f * Mathf.Min(1f, _fidgeting * 4f);
        }

        // THE LEAN. _leanRad is the low-passed forward ACCELERATION (see above), not the speed and
        // not a constant run term — so the body pitches in while it is building speed, stands up
        // once it is there, and pitches back while it brakes. What shipped was
        // `velocity.Z * scale - RunLeanRad * runBlend`, which GREY-1 measured at -29.8 degrees held
        // constantly at every sprint, identical on every roster body of the day. That is a
        // start-block posture, it said nothing about what the body was doing, and Talon hates it.
        //
        // The swing lean goes INSIDE the clamp, not on top of it. MaxBodyTilt is the floor-clamp
        // invariant (SandboxSelfTest.phys_fall_mesh_clamped pins it), and a pose term added after the
        // clamp would be a pose term that can breach it — the exact shape of "some system was never
        // told" this file's own comments keep warning about.
        // The anticipation goes INSIDE the clamp with everything else, for MaxBodyTilt's reason —
        // and so does the skid's backward pitch (SKID-1). Positive is back (see LeanForAccel's sign
        // note), so the brake adds to whatever the deceleration lean is already doing.
        //
        // The jump's three pitches (JUMP-1) go in the same place for the same reason, and
        // phys_fall_mesh_clamped is the test that proves it: the chest opening on the drive up
        // (back, keyed to upward speed so it is gone by the apex), the brace through the fall
        // (forward, growing with the descent), and the fold of the landing absorb (forward).
        float jumpTilt =
            (JumpRiseTiltRad * Mathf.Max(0f, _airDrive) * _airborne)
            - (JumpFallTiltRad * airReach)
            - (LandTiltRad * landDrive);
        // THE VERB AND THE CHAIN, INSIDE THE SAME CLAMP, for the same reason every other term is:
        // MaxBodyTilt is the floor-clamp invariant phys_fall_mesh_clamped pins, and a pose term added
        // outside it is a pose term that can breach it. NEGATED because VerbPose states its pitches
        // positive-forward and the rig's forward is negative — the conversion happens once, here.
        //
        // §12.1's DEPTH-4 HOLD is the Mathf.Max: depths 1-3 pitch the body only while it is in the
        // air, and depth 4 keeps that same 15 degrees on it through the landing as well. That held
        // GROUNDED pose is the one §12.1 says matters most ("during the 0.35 s grace it tells a
        // partner he is still hot, which is the only moment a partner could act on it"), and it needs
        // no grace timer of its own: ChainDepth decays when the grace expires (§6.4), so a non-zero
        // depth on the ground already means still hot.
        // MAX, NOT SUM, and it is this file's own squash-versus-knee argument in a second place. A
        // body cannot be pitched forward twice: the verb's pitch and the chain's held pitch are two
        // readings of one channel, and adding them puts a slide landed at depth 4 at 0.68 rad, past
        // MaxBodyTilt, where it would spend its whole existence pinned at the clamp reading as
        // neither tell. The max keeps each at full strength alone — which is every case except the
        // one overlap — and lets the deeper one own the torso where they meet. Airborne the question
        // does not arise: law V0 forces Verb = Normal off the floor.
        float chainTiltWeight = Mathf.Max(_airborne, VerbPose.ChainGroundHoldWeight(_chainRead));
        float verbTilt = -Mathf.Max(
            verbPose.ForwardTiltRad, VerbPose.ChainPitchRad(_chainRead) * chainTiltWeight);
        // THE COIL'S GATHER (MOVE-5f), INSIDE THE SAME CLAMP as everything else, for MaxBodyTilt's
        // reason. NEGATED because AnticipationCoil states its pitch positive-forward and the rig's
        // forward is negative — the same conversion verbTilt makes, one line up.
        //
        // ADDED to jumpTilt rather than MAX'd with it, and the distinction is the one this file
        // already draws for the verb and the chain. Those two are MAX'd because they are two
        // readings of ONE channel — a body cannot be pitched forward twice. The coil and the rise
        // tilt are not: JumpRiseTiltRad reads HOW FAST the body is going up (keyed to _airDrive, and
        // spent by the apex), the coil reads HOW LONG the player committed. A tap has the first in
        // full and almost none of the second. They point opposite ways on purpose, and the sum is
        // the honest composite — a fully coiled body nets ~0.12 rad forward where an uncoiled one is
        // 0.10 rad back, which is the 19-degree swing in the outline that carries at distance.
        float coilTilt = -coilPose.ForwardTiltRad;
        // THE RIDE'S PITCH (BIKE-4A), INSIDE THE SAME CLAMP as everything else, for MaxBodyTilt's
        // reason — a pose term added outside it is a pose term that can breach the floor-clamp
        // invariant phys_fall_mesh_clamped pins. NEGATED because RidePose states its pitch
        // positive-forward and the rig's forward is negative, the same conversion verbTilt and
        // coilTilt make one and two lines up.
        //
        // ADDED rather than MAX'd, and the file's own distinction says why: verbTilt and the chain
        // are MAX'd because they are two readings of ONE channel. The ride is a different channel
        // entirely — it is where the body is SITTING, not what it is doing — and it composes with
        // the acceleration lean and the slope the way a real rider's back does. At full ride the
        // gait's own terms are already zero (gaitWeight is 0), so what is left to compose with is
        // exactly the lean, the anticipation and the airborne partition, all of which should still
        // be visible on a rider.
        float rideTilt = -ridePose.ForwardTiltRad * rideW;
        float tiltX = Mathf.Clamp(
            _leanRad + anticipationNow - SwingLeanRad * swingForward + SkidLeanRad * _skidBlend
                + jumpTilt + verbTilt + coilTilt + rideTilt,
            -MaxBodyTilt, MaxBodyTilt);

        // WRITTEN TO _pose, NOT _body (ANIM-1). This is the whole of the Gap 0 fix: the anchors hang
        // inside this node, so a held item inherits the lean, the roll, the bob and the fidget by
        // parentage instead of by a per-frame copy that only ever carried the position. See _pose.
        // The squash-and-stretch scale stays on _body, deliberately, so it does NOT reach them.
        //
        // PIVOTED AT THE HIP, NOT AT THE FEET (MOVE-1, closing GREY-1's open question 1). The pose
        // node sits at the rig origin, which is the ground, so every rotation written here used to
        // swing the whole body about its own feet — on a 1.20 m body that threw the crown 0.60 m
        // forward and dropped it 0.16 m, and GREY-1 photographed one body rolled onto its face
        // and another's feet leaving the ground. Rotating about a point and then translating back
        // is the standard fix, and here the point is measured: the hip is where the legs meet the
        // body, which is exactly _legLengthM up. The feet are children of THIS node rather than of
        // the pose, so they never inherited the lean and never should — a leg that tilts with the
        // torso is a leg that is not standing on anything.
        // THE WAIST (RIG-1). The total pitch above is what MaxBodyTilt clamped; it is DISTRIBUTED
        // here, after the clamp, between the hip pivot and the waist. Clamp-then-distribute, never
        // distribute-then-clamp: two shares each individually under the cap can sum past it, and the
        // cap is the floor-clamp invariant phys_fall_mesh_clamped pins. BodyTiltX reports the total
        // for exactly that reason.
        //
        // Only a rig that declared an authored upper/lower split takes a waist share (see HasWaist);
        // every other body puts the whole pitch on the hip, as it always did, so
        // nothing ratified moves on them.
        _bodyTiltX = tiltX;
        float waistPitch = HasWaist && _waist != null ? tiltX * WaistPitchShare : 0f;
        WaistPitchRad = waistPitch;
        if (_waist != null)
            _waist.Rotation = _waist.Rotation with { X = waistPitch };

        _pose.Rotation = new Vector3(tiltX - waistPitch,
            fidgetYaw + torsoTwist + SwingYawRad * swing, roll);
        var hipPivot = new Vector3(0f, _legLengthM, 0f);
        // The Y term is L(1 - cos(tilt)cos(roll)), which is >= 0 for every angle — so BodyOffsetY's
        // >= 0 invariant survives structurally rather than by a clamp somebody could delete.
        _pose.Position = hipPivot - (_pose.Basis * hipPivot) + new Vector3(0f, bob, 0f);

        // THE CROUCH. One writer, one axis, never positive — the knees can sink the rig but never
        // lift it, so this can no more put a mesh through a ceiling than the pose can put one through
        // the floor. Bounded by LandAbsorbKneeFold x legLength (4.4 cm on the greybox) against a
        // lowest body vertex at y 0.42.
        _crouch.Position = new Vector3(0f, -CrouchDropM, 0f);

        // THE LEGS. Each swings about its own hip through the angle the derived track asks for, and
        // the node lifts only during swing — zero lift throughout stance, because a planted foot is
        // planted. The lift is a RISE of the hip node, never a drop, so a leg can never open a gap
        // under the torso it overlaps.
        // The hips holding against the shoulders. SwingYawRad twists the torso; this turns the feet
        // the other way by a fraction of it, so the body reads as wound between two ends rather than
        // as a single piece rotating.
        float footCounterYaw = -SwingYawRad * swing * FootCounterTwist;
        FootYawRad = footCounterYaw;

        // --- THROUGH THE SOLVER (RIG-1) -----------------------------------------------------------
        // The hip angle above is not written to the node any more; it is turned into a FOOT TARGET —
        // where the ankle should be, relative to its own hip — and the two-bone solve answers with a
        // thigh angle and a knee angle. The target is the rigid leg's own foot position scaled in by
        // the fold, so with no fold the target sits at exactly leg length, LimbIk returns a straight
        // knee, and the pose is bit-identical to what shipped. That identity is the packet's whole
        // safety argument and it is asserted in LimbIkTests.RigidGaitFootPositions_AreReproducedExactly.
        _hipPitchL = legAngleL;
        _hipPitchR = legAngleR;
        SolveLeg(_leftThigh, _shinL, _footL, _leftThighRestRot, _shinLRestRot, _footLRestRot,
            legAngleL, kneeFoldL, footCounterYaw, _leftThighRestY + liftL, out _kneeBendL,
            out _ankleLevelL);
        SolveLeg(_rightThigh, _shinR, _footR, _rightThighRestRot, _shinRRestRot, _footRRestRot,
            legAngleR, kneeFoldR, footCounterYaw, _rightThighRestY + liftR, out _kneeBendR,
            out _ankleLevelR);

        // Sprout: springy lag opposite horizontal velocity — free secondary motion.
        Vector2 sproutTarget = new(-localVelocity.Z * 0.06f, localVelocity.X * 0.06f);
        Vector2 sproutAccel = (sproutTarget - _sproutSway) * 60f - _sproutSwayVel * 6f;
        _sproutSwayVel += sproutAccel * delta;
        _sproutSway += _sproutSwayVel * delta;
        _sprout.Rotation = new Vector3(
            Mathf.Clamp(_sproutSway.X, -0.9f, 0.9f), 0, Mathf.Clamp(_sproutSway.Y, -0.9f, 0.9f));

        // --- THE TWO CARRIES (CARRY-1) ------------------------------------------------------------
        // Talon, 2026-08-17: "they should have two distinct means of carrying something… like how a
        // person holds a bat or a sword… [and] holding a big pot like their arms are out". Those are
        // the two registers the carry model has separated since 2026-08-08 — the HAND and the ARMS
        // — finally reaching the body. Same 6/s ease the single carry blend always had, kept
        // rather than re-picked: nothing about the blend RATE was wrong.
        _handleBlend = Mathf.MoveToward(
            _handleBlend, _carry == CarryPose.Handle ? 1f : 0f, delta * 6f);
        _armfulBlend = Mathf.MoveToward(
            _armfulBlend, _carry == CarryPose.Armful ? 1f : 0f, delta * 6f);

        // Aim rig (WP-L3): hands up near eye level, deliberately distinct from the carry
        // gesture above (higher Y, less forward Z) so a raised aim reads as a different pose
        // than merely holding something, even though both are "arms forward" in silhouette.
        // Blend RATE (not just duration) is locked to AimController.RaiseDurationSec so the
        // pose finishes exactly when the stance itself commits — same technique
        // feat/tier0-camcorder's FOV ease uses (FovDegPerSec, tuned off the same constant).
        const float aimBlendRate = 1f / MpFoundation.Game.Aim.AimController.RaiseDurationSec;
        _aimBlend = Mathf.MoveToward(_aimBlend, _aiming ? 1f : 0f, delta * aimBlendRate);
        // THE AIM POSE'S HAND POSITIONS ARE THE SHIPPED ONES, TO THE MILLIMETRE — and that is the
        // whole safety argument for touching a ratified pose at all. The old code reached them by
        // translating the SHOULDER by these offsets and letting the hand ride along; this reaches the
        // same hand by leaving the shoulder alone and asking the arm. Hand = rest hand + the offset,
        // so the numbers below are ANIM-1's own, unchanged and still the only place they appear.
        Vector3 restHand = new(0f, -(_upperArmLengthM + ForearmLengthM), 0f);
        Vector3 aimHandL = restHand + new Vector3(0.09f, 0.30f, -0.28f);
        Vector3 aimHandR = restHand + new Vector3(-0.09f, 0.30f, -0.28f);

        // THE BARS (BIKE-4A). Hand targets in the same frame the carry and the aim use, so the
        // shoulder is never written and cannot leave its socket — the structural fix CARRY-1 made,
        // inherited rather than re-argued. Unlike those two, the offsets are FRACTIONS OF THE ARM's
        // own length rather than absolute metres: GREY-1's finding 3 is that the shipped literals
        // were calibrated to an -uffling's nub arms and land somewhere else on every other rig, and
        // a pair of hands that misses the bars by a third of a metre is a rider holding nothing.
        // The yaw is the steer, so the bars turn with the front wheel.
        (Vector3 barL, Vector3 barR) = RidePose.BarHands(
            _upperArmLengthM + ForearmLengthM, _rideSteer01);
        Vector3 rideHandL = restHand + barL;
        Vector3 rideHandR = restHand + barR;

        // Swing rig (ANIM-1). ONE vector, and both the arm and the hand mount ride it — see
        // SwingHandOffset for why that is not a convenience. It sweeps laterally with the stroke,
        // lifts, and reaches forward, which on a body with arms is a visible arm sweep and on a
        // body that declares them absent writes to an Inert stub nobody renders. Both are correct:
        // absent parts are a fact about the model, and an armless body's swing is carried entirely
        // by the whole-body twist above.
        // The whole vector is weight-multiplied so it is EXACTLY zero at rest — a constant term left
        // outside the weight would park a held net six centimetres off the hand forever.
        Vector3 swingHand = new Vector3(-0.06f + 0.34f * _swingDrive, 0.20f, -0.26f) * _swingBlend;
        SwingHandOffset = swingHand;
        // The trailing arm follows at less than half amplitude and mirrored in X — a counterweight,
        // not a second net hand.
        //
        // ITS VERTICAL TERM IS GONE (MOVE-1, second amendment note C), and the captures are why. It
        // used to lift the trailing arm 0.10 m, which on a body whose arm hangs from a shoulder node
        // simply raises the whole bar — the top pokes above the shoulder and a gap opens under it.
        // That is GREY-1's finding 3 (these offsets are absolute metres calibrated to the original
        // rig's nub arms, and the greybox does not get that luck) showing up as a visibly detached limb in
        // a head-on shot. The amendment's own instruction is the fix: prefer motion that changes the
        // silhouette ROTATIONALLY over an offset on one axis. So the lift is dropped and the arm's
        // share of SwingArmPitchRad is raised to carry it instead — a rotation about the shoulder
        // cannot detach the arm from the shoulder it rotates about.
        //
        // SwingHandOffset itself is untouched: it also places the NET, and that is ANIM-1's tuning.
        Vector3 swingTrail = new(-swingHand.X * 0.45f, 0f, swingHand.Z * 0.3f);

        // --- NOTHING BELOW WRITES _leftArm.Position OR _rightArm.Position, AND THAT IS THE PACKET ---
        //
        // It used to, and that was the defect Talon diagnosed from a moving build: "the player
        // character model's shoulders pop out of their sockets… this is physically impossible".
        // GreyboxAvatarBody authors each arm with ITS NODE ORIGIN AT THE SHOULDER (its own class doc:
        // a part centred on its own mesh "would swing from its middle and read as a floating stick"),
        // so writing Position on that node translates the JOINT. The three poses that did it were
        // absolute metres calibrated to the original rig's nub arms:
        //
        //   carry  (0.16, 0.10, -0.34)  -> the shoulder 16 cm inboard, 10 cm up, 34 cm forward. On the
        //                                  greybox that lands it at x = 0.015 — 1.5 cm off the body's
        //                                  own midline, i.e. inside the middle of the chest, a third
        //                                  of a metre in front of it. GreyboxAvatarBody's ShoulderX
        //                                  comment already recorded this arithmetic as the reason it
        //                                  could not use 0.155.
        //   aim    (0.09, 0.30, -0.28)  -> the shoulder 30 cm UP: "shoulders pop out of their sockets
        //                                  and hold the net up", literally.
        //   swing  up to (0.28, 0.20, -0.26) on the right arm alone, every stroke.
        //
        // MOVE-1's second amendment, note C, had already found this class of bug and fixed it for the
        // swing's TRAILING arm by switching that term to a rotation. The other five stayed. The fix
        // here is the structural one rather than another tuned offset: every pose is now a HAND
        // TARGET, solved through the two-bone IK RIG-1 shipped, and the shoulder node is never
        // written at all. A shoulder that is never written cannot leave its socket — which is why
        // acceptance criterion 1 is an equality against the authored rest and not a threshold.
        //
        // Each target below is an offset from that arm's OWN shoulder, in the arm's rest-local frame.

        // ARM COUNTER-SWING (MOVE-1). Each arm swings with the OPPOSITE leg — that is the whole of
        // why a walk reads as a walk — through the same derived hip angle at a smaller amplitude.
        //
        // SUPPRESSED PER ARM, NOT GLOBALLY (CARRY-1, scope item 4). It used to fade under
        // max(swing, carry, aim) for both arms at once. That is right for an ARMFUL — both arms are
        // genuinely occupied — and wrong for a HANDLE: a person carrying a bat still swings the other
        // arm, and that asymmetry is most of what makes the walk read as natural. So the free arm
        // keeps its gait through a handle carry and only the tool hand gives it up.
        //
        // THE RIDE JOINS THE GUARD (BIKE-4A) on both arms: hands on the bars are hands that are not
        // counter-swinging with the legs, not folding into the ribs for a coil, and not sweeping
        // back for a verb. Exactly 0 at ride weight 0, so nothing unmounted moves.
        float armGuardL = Mathf.Max(rideW, Mathf.Max(_swingBlend, Mathf.Max(_armfulBlend, _aimBlend)));
        float armGuardR = Mathf.Max(armGuardL, _handleBlend);
        float armGaitL = gaitWeight * ArmSwingRatio * (1f - armGuardL);
        float armGaitR = gaitWeight * ArmSwingRatio * (1f - armGuardR);
        // The arm's own arc through the stroke — rotational, so it survives a camera the player can
        // put anywhere (second amendment, note C). The trailing arm takes a fraction of it, mirrored,
        // as a counterweight rather than a second net hand.
        float armSwingPitch = swing * SwingArmPitchRad;
        RightArmPitchRad = armSwingPitch;
        // THE AIRBORNE ARMS (JUMP-1). Rotational about the shoulder, never an offset — an offset on
        // one axis raises the whole bar and opens a gap under it on a nub arm, which is GREY-1's
        // finding 3 and the reason the swing's trailing arm lost its vertical term. Up through the
        // ascent and the apex, coming down and forward as the fall builds — the arms doing what the
        // legs are doing, one beat behind. Guarded per arm now, for the reason armGait is.
        float armAirL = JumpArmRad * (airTuck - (airReach * 0.6f)) * (1f - armGuardL);
        float armAirR = JumpArmRad * (airTuck - (airReach * 0.6f)) * (1f - armGuardR);

        // --- THE ELBOW (RIG-1) --------------------------------------------------------------------
        // The airborne tuck is still the only thing that SHORTENS the arm. Everything else is a hand
        // target at whatever distance it happens to sit, and the solver answers with whatever elbow
        // that implies — which is the point: a bent elbow is what a carry looks like, and before this
        // packet the rig had no way to express one without moving the shoulder to fake it.
        //
        // THE VERB'S FOLD JOINS IT (MOVE-5c). §12.2 separates the tuck from the duck walk as "arms
        // in" against "extended", and on a silhouette that IS the elbow: a folded arm stops being a
        // separate line beside the torso and a straight one does not. Guarded per arm exactly as the
        // airborne tuck is — a body carrying a net does not get to fold that arm into its ribs —
        // and clamped by MaxLimbFold with it, since the two are additive.
        // THE COIL'S ELBOW JOINS THEM (MOVE-5f), on the same term and guarded the same way — a body
        // carrying an armful does not get that arm folded into its ribs by a pose. VerbPose's own
        // finding is why the coil bothers with the arms at all: at thirty metres a folded arm stops
        // being a separate line beside the torso and a straight one does not, so the elbow is a
        // large part of what makes a small figure read as GATHERED rather than merely far away.
        float verbArmFold = verbPose.ArmFold + coilPose.ArmFold;
        float elbowFoldL = Mathf.Clamp(
            (JumpArmElbowFold * airTuck + verbArmFold) * (1f - armGuardL), 0f, MaxLimbFold);
        float elbowFoldR = Mathf.Clamp(
            (JumpArmElbowFold * airTuck + verbArmFold) * (1f - armGuardR), 0f, MaxLimbFold);

        // --- WHERE THE SHOULDER ANGLE COMES FROM (ANIM-M3) -----------------------------------------
        //
        // With the clip layer on, the arm's ROTATION is entirely the clip's. Every one of the three
        // procedural terms it replaces is now an authored channel: Walk and Run key the gait
        // counter-swing, Hold_NetSwing keys the stroke's arc, and Jump_Air keys the airborne tuck.
        // Keeping any of them would be the same pose written twice.
        //
        // What does NOT move to the clip, and it is the split ANIM-M0 4.2 draws: the arm's
        // POSITION offsets (carry, aim, and SwingHandOffset) stay procedural, because
        // SwingHandOffset also places the held NET and that is ANIM-1's ratified tuning. And the
        // ELBOW stays LimbIk's, for the same reason the knee does.
        //
        // --- MERGE NOTE (PLAYTEST-1 trunk, 2026-08-22) ----------------------------------------------
        //
        // ANIM-M3 and CARRY-1 both rewrote this block and conflicted head-on, but they are not
        // actually competing: ANIM-M3 changes where the shoulder ANGLE comes from, CARRY-1 changes
        // what the arm is SOLVED AGAINST (a hand target rather than an angle). ANIM-M3's own comment
        // above draws exactly that line — rotation is the clip's, position offsets stay procedural —
        // and a carry is a position offset. So the two compose in series rather than either winning:
        // the clip-or-gait choice below produces the angle, and that angle is what CARRY-1's
        // GaitHandTarget converts into the base hand position every pose then lerps away from.
        //
        // Consequence worth stating: with clips ON and no pose active, the arm follows the authored
        // channel exactly as ANIM-M3 intended; with a carry active, the clip's arm angle becomes the
        // REST the carry departs from instead of being overwritten by the procedural gait. Neither
        // branch's ratified look is altered when the other's feature is off.
        //
        // Per-side variables (armGaitL/R, armAirL/R, elbowFoldL/R) are CARRY-1's; ANIM-M3's side of
        // the hunk still used the older single-sided armGait/armAir/elbowFold, which no longer exist
        // above this point.
        float shoulderAngleL = clipLegs
            ? _clipPose.ArmPitchL
            : (legAngleR * armGaitL) - (armSwingPitch * 0.55f) + armAirL;
        float shoulderAngleR = clipLegs
            ? _clipPose.ArmPitchR
            : (legAngleL * armGaitR) + armSwingPitch + (armAirR * JumpTrailLegFraction);

        // --- THE VERB'S ARMS AND THE CHAIN'S (MOVE-5c) ---------------------------------------------
        //
        // ADDED AFTER the clip-or-gait choice rather than inside either branch, and for the leg
        // block's reason: no crouch clip and no chain clip exists, so a term that lived only in the
        // procedural branch would be invisible on every body with a clip layer. Positive is FORWARD
        // (see GaitHandTarget: the hand's Z is -sin(angle) and the rig faces -Z).
        //
        // §12.1 DEPTH 1: both arms sweep back — "the actor's own confirmation that a chain started".
        // §12.1 DEPTH 2: the LEADING hand crosses forward while the trailing one stays back, because
        // "a straight silhouette line from trailing toe to leading hand" needs a hand at the far end
        // of it. The leading hand is the one OPPOSITE the lead leg, which is what a stride does and
        // what makes the line cross the whole figure rather than run down one side.
        //
        // Airborne-weighted: depth 4's hold is a torso term (§12.1) and stays out of the arms.
        // Guarded per arm exactly as the gait counter-swing and the airborne tuck are — a body
        // mid-net-stroke or holding an armful does not get its arms taken away by a pose.
        float chainArmBack = VerbPose.ChainArmSweepAt(_chainRead) * _airborne;
        float chainArmLead = VerbPose.ChainLeadArmAt(_chainRead) * _airborne;
        // THE COIL'S SHOULDER RIDES THE SAME LINE (MOVE-5f), for the reason the block above gives:
        // no coil clip exists either, so a term that lived only in the procedural branch would be
        // invisible on every body with a clip layer. Swept BACK (negative is behind the body), the
        // smaller sibling of the slide's own sweep — a coil brings the arms IN to the line of the
        // body, and an arm reaching forward is a different pose called a stride.
        float coilArmSweep = coilPose.ArmSweepRad;
        shoulderAngleL +=
            (verbPose.ArmSweepRad + coilArmSweep + (_leadLegLeft ? chainArmBack : chainArmLead))
            * (1f - armGuardL);
        shoulderAngleR +=
            (verbPose.ArmSweepRad + coilArmSweep + (_leadLegLeft ? chainArmLead : chainArmBack))
            * (1f - armGuardR);

        _shoulderPitchL = shoulderAngleL;
        _shoulderPitchR = shoulderAngleR;
        float foldR = elbowFoldR * JumpTrailLegFraction;

        // THE POSE WEIGHTS. A swing outranks a carry and a raised aim outranks a carry, so the two
        // ratified poses keep their shipped hand positions and only the CARRY — this packet's whole
        // subject — is new. That ordering is also what keeps the boundary the packet drew: the net's
        // stroke is untouched, and a handle carry simply stands down while one is in flight.
        float poseOut = 1f - Mathf.Max(_swingBlend, _aimBlend);
        float handleW = _handleBlend * poseOut;
        float armfulW = _armfulBlend * poseOut;
        float aimW = _aimBlend * (1f - _swingBlend);
        // THE BARS' OWN WEIGHT (BIKE-4A), on the same shape as the carry's: a swing and a raised aim
        // still outrank it, so the two ratified poses keep their shipped hand positions. Where the
        // ride sits relative to the CARRY is 3B §6.3's decision and it is stated there: carry wins
        // the arms, so a carrying rider pedals with the load in their arms — degenerate but safe,
        // never wrong-looking, never a lockout. That ordering is expressed below by applying this
        // lerp BEFORE the carry's, so the carry's own weight lerps away from the bars.
        float rideHandW = rideW * poseOut;

        // THE HAND TARGETS. Base is the gait's own implied hand — exactly where the rigid arm put it,
        // which is what makes a zero-weight frame identical to what shipped — then each pose lerps
        // the hand toward its own place, and the swing's sweep is ADDED to whatever that is.
        //
        // The swing term is the same vector, applied to the same hand, at the same amplitude it always
        // was: SwingHandOffset moved the shoulder and the hand rode along, so adding it to the hand
        // instead reproduces the shipped hand position to the millimetre wherever the arm can reach.
        // Where it cannot — a 0.38 m arm asked for a hand 0.42-0.51 m out at the extremes of the
        // stroke — the solver returns a fully extended arm pointing at the net, and the shortfall is
        // reported (HandReachShortfallM) rather than papered over by moving the joint.
        Vector3 targetL = GaitHandTarget(shoulderAngleL, elbowFoldL);
        Vector3 targetR = GaitHandTarget(shoulderAngleR, foldR);
        targetL = targetL.Lerp(rideHandL, rideHandW).Lerp(_armfulHandL, armfulW)
            .Lerp(aimHandL, aimW) + swingTrail;
        targetR = targetR.Lerp(rideHandR, rideHandW).Lerp(_handleHandR, handleW)
            .Lerp(_armfulHandR, armfulW).Lerp(aimHandR, aimW) + swingHand;

        // The legacy scalar path when NO pose is asking for anything, so a plain walk, run, jump and
        // skid are bit-identical to what MOVE-1 and JUMP-1 ratified rather than merely equal to
        // within float noise. The two paths agree analytically (a straight arm pointed at its own
        // rigid hand is the same arm); this branch means the ratified gait never has to rely on that.
        float poseWeightL = Mathf.Max(rideHandW, Mathf.Max(armfulW, Mathf.Max(aimW, _swingBlend)));
        float poseWeightR = Mathf.Max(handleW, poseWeightL);

        if (poseWeightL <= 0f)
        {
            SolveArm(_leftArm, _forearmL, _leftArmRestRot, _forearmLRestRot,
                shoulderAngleL, elbowFoldL, out _elbowBendL);
            _handShortfallL = 0f;
        }
        else
        {
            SolveArmToTarget(_leftArm, _forearmL, _leftArmRestRot, _forearmLRestRot,
                _leftArmRestScale, targetL, out _elbowBendL, out _handShortfallL);
        }

        if (poseWeightR <= 0f)
        {
            SolveArm(_rightArm, _forearmR, _rightArmRestRot, _forearmRRestRot,
                shoulderAngleR, foldR, out _elbowBendR);
            _handShortfallR = 0f;
        }
        else
        {
            SolveArmToTarget(_rightArm, _forearmR, _rightArmRestRot, _forearmRRestRot,
                _rightArmRestScale, targetR, out _elbowBendR, out _handShortfallR);
        }

        // Blink: quick lid-drop every few seconds (scale the eye meshes flat). Same
        // 0.12/1.35 open-vs-closed ratio as before, applied relative to each eye's
        // authored rest scale (the model already bakes the same oval proportions).
        //
        // THIS IS THE OWNER OF BLINK (decision, 2026-08-08). Blink is idle behaviour that belongs
        // next to breath and fidget, it needs no network event, and its five cadence parameters
        // are the literals below (they lived on BodyLanguageProfile, deleted by ANIM-M2b under
        // ruling 9; the OWNERSHIP decision is unchanged). Any future face layer must leave the
        // blink to this code or the two sum into double-blinks at the wrong amplitude.
        _blinkTimer -= delta;
        if (_blinkTimer <= 0f)
        {
            _blinking = 0.12f;
            _blinkTimer = 2.2f + (float)GD.RandRange(0.0, 2.6);
        }
        if (_blinking > 0f)
            _blinking -= delta;
        float eyeYFrac = _blinking > 0f ? 0.12f / 1.35f : 1f;
        _leftEye.Scale = _leftEyeRestScale with { Y = _leftEyeRestScale.Y * eyeYFrac };
        _rightEye.Scale = _rightEyeRestScale with { Y = _rightEyeRestScale.Y * eyeYFrac };
    }

    /// <summary>
    /// Writes one leg's pose — thigh angle and knee angle — from the hip angle the gait asked for and
    /// however far the jump wants the leg shortened.
    ///
    /// <para><b>The target is the rigid leg's own answer, scaled in by the fold.</b> That is the
    /// single line that makes the whole knee free: at <paramref name="fold"/> = 0 the target sits at
    /// exactly <c>legLength</c> from the hip, <see cref="LimbIk"/> returns a straight knee at the same
    /// hip angle, and the ratified walk, run and skid are reproduced to the last bit. A fold is the
    /// only thing that can bend it.</para>
    ///
    /// <para><b>Degrades to the single-segment rotation that shipped</b> when the rig has no shin —
    /// every <c>.glb</c> on the roster. The hip angle is written straight to the node exactly as
    /// before, and the fold is simply not expressible on a limb with one bone; nothing is
    /// synthesised.</para>
    /// </summary>
    private void SolveLeg(Node3D hipNode, MeshInstance3D? shin, MeshInstance3D? foot,
        Vector3 hipRestRot, Vector3 shinRestRot, Vector3 footRestRot,
        float hipAngle, float fold, float counterYaw, float hipY,
        out float kneeBend, out float ankleLevel)
    {
        hipNode.Position = hipNode.Position with { Y = hipY };

        if (shin == null)
        {
            hipNode.Rotation = hipRestRot + new Vector3(hipAngle, counterYaw, 0f);
            kneeBend = 0f;
            ankleLevel = 0f;
            return;
        }

        float thigh = _thighLengthM;
        float shinLen = ShinLengthM;
        float reach = (thigh + shinLen) * (1f - fold);
        var target = new Vector2(Mathf.Sin(hipAngle) * reach, -Mathf.Cos(hipAngle) * reach);

        LimbIk.Solution s = LimbIk.Solve(thigh, shinLen, target, LimbIk.Bend.KneeBackward);
        hipNode.Rotation = hipRestRot + new Vector3(s.RootAngleRad, counterYaw, 0f);
        shin.Rotation = shinRestRot with { X = shinRestRot.X + s.JointAngleRad };
        kneeBend = -s.JointAngleRad;   // knees bend negative; report the magnitude as a bend

        // THE ANKLE (ANIM-M2b). Written LAST and derived from the two angles above, because that is
        // the only order in which it can be right: levelling the sole means cancelling hip + knee,
        // and the knee is a number the solver has only just produced. Nothing else writes this node
        // - the clip library's own self-check forbids keying FootL/FootR for exactly this reason.
        //
        // W7-3 (Talon note 11) SCALES IT, and the scale is the whole fix. See AnkleLevelFraction:
        // LevelAnkle answers "what holds the sole flat", and holding the sole flat on EVERY frame is
        // literally the defect he reported. Keeping most of the levelling and giving the rest back
        // to the leg is what turns a paddle into an ankle.
        ankleLevel = LimbIk.LevelAnkle(s.RootAngleRad, s.JointAngleRad,
            AnkleDorsiLimitRad, AnklePlantarLimitRad) * AnkleLevelFraction;
        if (foot != null)
            foot.Rotation = footRestRot with { X = footRestRot.X + ankleLevel };
    }

    /// <summary>Where the gait alone would put this hand, relative to its own shoulder, in the arm's
    /// rest-local frame — the 3D form of the 2D target <see cref="SolveArm"/> builds internally
    /// (<c>X</c> forward is the rig's <c>-Z</c>). Exists so a pose can be expressed as a LERP between
    /// two hand positions rather than as an offset bolted onto a joint.</summary>
    private Vector3 GaitHandTarget(float shoulderAngle, float fold)
    {
        float reach = (_upperArmLengthM + ForearmLengthM) * (1f - fold);
        return new Vector3(0f, -Mathf.Cos(shoulderAngle), -Mathf.Sin(shoulderAngle)) * reach;
    }

    /// <summary>
    /// <b>One arm posed by its HAND rather than by its shoulder</b> (CARRY-1). Given where the hand
    /// should be — an offset from the shoulder, in the arm's rest-local frame — this writes the
    /// shoulder's orientation and the elbow's bend and <b>never the shoulder's position</b>. That is
    /// the whole point of the method: the joint cannot leave its socket because nothing here can move
    /// it, which is a stronger guarantee than any offset small enough to look right.
    ///
    /// <para><b>The maths is <see cref="LimbIk.SolveSpatial"/>'s and lives there</b>, next to the
    /// planar solve it generalises and inside <c>dotnet test</c>'s reach — this method is the rig's
    /// half: which lengths, which pole, and which nodes get written. <see cref="LimbIk.Solve"/> is
    /// deliberately planar and a carry is not, because the net hangs on the body's own midline, 17.5 cm
    /// inboard of the shoulder that has to reach it, so the plane itself has to tilt.</para>
    ///
    /// <para><b>Degradation, with the shoulder still fixed</b> (acceptance criterion 7). On a rig
    /// with no forearm — every stub-armed body — there is no elbow to bend, so the whole arm
    /// simply points at the target. That is a real pose rather than an unposed limb, and unlike the
    /// behaviour it replaces it cannot dislocate a nub arm it was never calibrated for.</para>
    /// </summary>
    /// <param name="shortfallM">How far short of <paramref name="handTarget"/> the hand landed — zero
    /// whenever the target was inside the arm's reach. Reported rather than swallowed: a carry that
    /// asks for a hand the arm cannot reach is a fact about the pose, and the previous code hid
    /// exactly that fact by moving the shoulder until the sum worked out.</param>
    private void SolveArmToTarget(Node3D shoulderNode, MeshInstance3D? forearm,
        Vector3 shoulderRestRot, Vector3 forearmRestRot, Vector3 shoulderRestScale,
        Vector3 handTarget, out float elbowBend, out float shortfallM)
    {
        // WHERE THE ELBOW GOES, and it is the whole difference between a pose and a mess. A
        // straight-back elbow is the textbook answer and it is wrong for a cross-body reach: on the
        // greybox, reaching the tool hand to a grip on the body's own midline solves the elbow to
        // x = 0.007 m, dead centre of the chest, and the arm disappears inside the torso — a different
        // way of failing the same criterion the shoulder translation failed. Swung outboard it solves
        // to x = 0.160 m, a centimetre clear of a torso 0.158 m wide. LimbIk.SolveSpatial scales the
        // swing by the fold, which is what keeps a straight arm identical to SolveArm's own pose.
        //
        // VALUE CALL (CARRY-1): outboard 1, down 0.55, forward 0.25, reached at a right-angle fold.
        // The side is read off the arm's own rest POSITION, which is authoritative precisely because
        // nothing writes it any more.
        var outward = new Vector3(Mathf.Sign(shoulderNode.Position.X), -0.55f, -0.25f);

        LimbIk.SpatialSolution s = LimbIk.SolveSpatial(
            _upperArmLengthM, forearm != null ? ForearmLengthM : 0f, handTarget, outward);

        shoulderNode.Basis = (Basis.FromEuler(shoulderRestRot) * s.Root).Scaled(shoulderRestScale);
        if (forearm != null)
            forearm.Rotation = forearmRestRot with { X = forearmRestRot.X + s.JointAngleRad };
        elbowBend = s.JointAngleRad;   // elbows bend positive, same sign convention SolveArm reports
        shortfallM = s.ShortfallM;
    }

    /// <summary>One arm's pose from a shoulder ANGLE — the gait, the jump and the swing's rotational
    /// arc, on the same contract as <see cref="SolveLeg"/>. See that method for why a zero fold is
    /// bit-identical to the rigid arm.
    ///
    /// <para><b>The arm node's POSITION is not touched here, and as of CARRY-1 it is not touched
    /// anywhere</b> — see the long note in <see cref="Animate"/>. When a carry, aim or swing pose is
    /// asking for a hand somewhere, <see cref="SolveArmToTarget"/> runs instead of this.</para></summary>
    private void SolveArm(Node3D shoulderNode, MeshInstance3D? forearm, Vector3 shoulderRestRot,
        Vector3 forearmRestRot, float shoulderAngle, float fold, out float elbowBend)
    {
        if (forearm == null)
        {
            shoulderNode.Rotation = shoulderRestRot + new Vector3(shoulderAngle, 0f, 0f);
            elbowBend = 0f;
            return;
        }

        float upper = _upperArmLengthM;
        float lower = ForearmLengthM;
        float reach = (upper + lower) * (1f - fold);
        var target = new Vector2(Mathf.Sin(shoulderAngle) * reach, -Mathf.Cos(shoulderAngle) * reach);

        LimbIk.Solution s = LimbIk.Solve(upper, lower, target, LimbIk.Bend.ElbowForward);
        shoulderNode.Rotation = shoulderRestRot + new Vector3(s.RootAngleRad, 0f, 0f);
        forearm.Rotation = forearmRestRot with { X = forearmRestRot.X + s.JointAngleRad };
        elbowBend = s.JointAngleRad;   // elbows bend positive
    }

    // One material per unique COLOR, shared across every avatar in the scene (Bible §4:
    // unique materials ≤25; the old new-material-per-blob pattern put ~15 on every
    // avatar — ~90 with a full lobby — and broke draw-call batching). The GLB's own
    // face-detail materials (eyes/blush) are left untouched and get the same free
    // sharing from Godot's PackedScene resource cache. Colors come from a small pastel
    // palette, so this dictionary stays tiny.
    private static readonly Dictionary<Color, StandardMaterial3D> MatCache = new();

    private static StandardMaterial3D Mat(Color color)
    {
        if (MatCache.TryGetValue(color, out StandardMaterial3D? cached))
            return cached;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.85f,
            Metallic = 0f,
        };
        MatCache[color] = mat;
        return mat;
    }
}
