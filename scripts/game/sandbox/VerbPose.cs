using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>MOVE-5c — the crouch verbs and the chain jump, as pose arithmetic.</b> Spec
/// <c>docs/design/2026-08-27-movement-verbs-state-machine.md</c> §12 is the contract; this is §12
/// with nothing engine-shaped in it, so every number §12 states is an assertion in
/// <c>dotnet test</c> rather than a caption under a screenshot.
///
/// <para><b>Pure and static for <see cref="LocomotionProfile"/>'s reason.</b> The gait's arithmetic
/// was pulled out of <see cref="AvatarVisual"/> so it could be tested without a viewport; the verb
/// poses are the same kind of thing. <see cref="AvatarVisual"/> keeps the rig half — which node,
/// which blend, which solver — and this keeps the numbers. Nothing here reads or writes simulation
/// state: it is handed a <see cref="MoveVerb"/> and a chain depth that
/// <c>AvatarMotor</c> already resolved and replicated, and it answers with angles.</para>
///
/// <para><b>THE CHAIN HAS NO UI, IN ANY BUILD, INCLUDING THE LAB</b> (spec §6.6, quoted at
/// <c>SandboxAvatar.ChainDepthNow</c>). This class is the entire channel by which a co-op partner
/// learns that a teammate's chain is deepening. That is stated here because it is the reason the
/// numbers below are chosen for <i>silhouette</i> — an outline against sky at thirty metres —
/// rather than for how they look in a close-up.</para>
///
/// <para><b>The weight principle.</b> Nothing in this class gates an input or delays a state
/// change. Every value is a function of a state the motor has already committed to; the body
/// catches up to the state and the state never waits for the body. The blend rates below are how
/// fast it catches up, not how long anything is held open.</para>
/// </summary>
public static class VerbPose
{
    // --- THE BLEND RATES ---------------------------------------------------------------------

    /// <summary>How fast the body eases between verb poses, per second. 18 — the same rate
    /// <c>AvatarVisual.SkidBlendRate</c> uses, deliberately rather than by coincidence: the skid is
    /// the one shipped pose that also has to appear and vanish on a replicated boolean, and it is
    /// the rate Talon has already accepted for that.
    ///
    /// <para>0.056 s of full travel — three and a third frames at 60 Hz. Spec §12.3 requires the
    /// pop-up on release to be visible <i>on the tick the button comes up</i>; an eased blend
    /// satisfies that because it begins moving on that tick. What it must not be is a hold, and it
    /// is not: the state has already changed, and this is only the body arriving.</para></summary>
    public const float VerbBlendRate = 18f;

    /// <summary>How fast the READ of the chain's depth follows the counter, in depths per second.
    /// 6 — one whole depth in 0.167 s, against a jump's ~0.7 s of airtime and the chain's 0.35 s
    /// ground grace, so the pose visibly <i>deepens</i> after each take-off instead of snapping
    /// between four costumes. Spec §12.1's own requirement, in one constant: a partner has to read
    /// deepening, not a wardrobe change.
    ///
    /// <para>It eases DOWN at the same rate, which is what makes the chain's decay legible too — a
    /// body coming off the boil unwinds rather than popping upright.</para></summary>
    public const float ChainBlendRate = 6f;

    // --- THE VERB POSES (spec §12.2) -----------------------------------------------------------
    //
    // §12.2 gives three words and one requirement:
    //
    //   Slide     "tucked ball, low, facing travel"
    //   Tuck      "BRACED - compressed, arms in, weight low and still"
    //   DuckWalk  "SETTLED - extended, head lowered, a walking gait"
    //
    // and: Tuck and DuckWalk share their locomotion exactly (§5.1), so "distinguishable from each
    // other" is a real requirement rather than a formality. Braced versus settled is the same
    // difference the INPUT has - the button held versus released - which is the cheapest legibility
    // available and the one a partner can learn once.
    //
    // WHAT ACTUALLY CARRIES AT DISTANCE, and it is why these numbers are shaped the way they are.
    // At 30 m a 1.20 m body is about 60 px tall in a 1000 px frame. Six pixels of height difference
    // is not a read. What IS a read at that size is the OUTLINE: whether the crown sits over the
    // feet or half a body-length in front of them, and whether the legs are striding or planted. So
    // the three poses are separated primarily by TORSO PITCH and by whether a gait is running, and
    // only secondarily by how low they sit.
    //
    //   Slide  deepest fold, hardest forward pitch, NO gait      - a body moving fast without a
    //                                                              stride is unmistakable
    //   Tuck   deep fold, essentially UPRIGHT, arms folded in    - short and square
    //   Duck   shallow fold, forward pitch, gait running         - short and striding
    //
    // Every amplitude is a fraction of the LIMB's own length or a typed angle inside MaxBodyTilt,
    // per the discipline MOVE-1 set: the day the real cast lands with different legs, the poses
    // scale with them.

    /// <summary>How far both feet pull in toward their hips in a crouch-SLIDE, as a fraction of leg
    /// length. 0.46 — <b>measured in engine at 114.6 degrees of knee and a 25.2 cm hip drop</b> on
    /// the greybox's 0.548 m leg, which is the "tucked ball, low" §12.2 asks for and the deepest
    /// pose the body has. It sums past <c>MaxLimbFold</c> when a landing absorb lands on top of it;
    /// that is the clamp's job and nothing here needs to know.
    ///
    /// <para>The knee ANGLE is a function of the fraction alone on equal segments, so it holds on
    /// any rig; the DROP scales with whatever leg the body measured for itself.</para></summary>
    public const float SlideKneeFold = 0.46f;

    /// <summary>The crouch-slide's forward torso pitch, radians. 0.42 (24 degrees) — well inside
    /// <c>AvatarVisual.MaxBodyTilt</c> (0.60) so it composes with the lean and the landing fold
    /// rather than fighting the clamp for room. <b>This is the slide's load-bearing tell:</b>
    /// measured in engine, it throws the silhouette's leading edge 23.6 cm further forward than a
    /// standing body's and drops the crown 21.5 cm — a change to the OUTLINE, which is the thing
    /// that survives thirty metres and a dark field.</summary>
    public const float SlideTiltRad = 0.42f;

    /// <summary>How far both elbows fold in a slide, as a fraction of arm length. 0.35 — arms in,
    /// forearms across the body: part of "ball" is that the limbs stop being separate lines in the
    /// silhouette.</summary>
    public const float SlideArmFold = 0.35f;

    /// <summary>The slide's shoulder pitch, radians, positive forward. −0.35 — swept back and down
    /// along the body, which is where arms go when the rest of you is tucked over your knees.</summary>
    public const float SlideArmSweepRad = -0.35f;

    /// <summary>How much of the gait a slide expresses. <b>Zero, and it is the second half of the
    /// slide's tell.</b> §4.4: the speed decays along the heading and the stick contributes no
    /// acceleration — the feet are not taking steps, they are being dragged. A body translating at
    /// six metres a second with no stride under it is legible at any distance, and it is also the
    /// honest pose: expressing a stride while the feet slide is exactly the foot-skate MOVE-1
    /// removed.</summary>
    public const float SlideGaitScale = 0f;

    /// <summary>Where the slide puts the LEAD leg, as a fraction of
    /// <c>LocomotionProfile.MaxLegSwingRad</c>, positive forward. 0.55 — the leg that goes out in
    /// front of the slide.</summary>
    public const float SlideLeadLegFraction = 0.55f;

    /// <summary>Where the slide puts the TRAIL leg, same units. −0.20 — folded back under the hips.
    /// The two legs pointing opposite ways is what stops a deep symmetric crouch reading as a body
    /// that simply got shorter, which is <c>SkidTrailLegFraction</c>'s argument applied to a second
    /// pose.</summary>
    public const float SlideTrailLegFraction = -0.20f;

    /// <summary>How far both feet pull in toward their hips in a stationary crouch-TUCK, as a
    /// fraction of leg length. 0.30 — about 91 degrees of knee, a real squat, deeper than the duck
    /// walk and shallower than the slide.</summary>
    public const float TuckKneeFold = 0.30f;

    /// <summary>The tuck's forward torso pitch, radians. 0.02 — <b>essentially upright, and that is
    /// the point.</b> "Braced" is a body gathering itself under its own weight, not a body reaching
    /// forward. Against the duck walk's 0.30 this is the whole distinction §12.2 needs, and it is
    /// the one that survives distance: at 30 m the pitch is legible when 4 cm of crown height is
    /// not.</summary>
    public const float TuckTiltRad = 0.02f;

    /// <summary>How far both elbows fold in a tuck, as a fraction of arm length. 0.45 — the most
    /// folded pose the body has. §12.2's "arms in", literally: a braced body's elbows come to its
    /// ribs, and a compact arm silhouette is a large part of what makes a small figure read as
    /// <i>compressed</i> rather than merely far away.</summary>
    public const float TuckArmFold = 0.45f;

    /// <summary>The tuck's shoulder pitch, radians, positive forward. 0.14 — hands up and slightly
    /// in front, the way a braced body holds them. Opposite in sign to the slide's, so the two deep
    /// crouches do not converge on one arm pose.</summary>
    public const float TuckArmSweepRad = 0.14f;

    /// <summary>How much of the gait a tuck expresses. <b>One — full.</b> §5.1 gives the tuck and
    /// the duck walk identical locomotion, so a tuck that moves is really walking and its feet must
    /// really step. Suppressing the gait here would manufacture foot-skate in a state the player can
    /// hold indefinitely.</summary>
    public const float TuckGaitScale = 1f;

    /// <summary>How far both feet pull in toward their hips in a DUCK WALK, as a fraction of leg
    /// length. 0.16 — about 66 degrees of knee. §12.2's "extended": the shallowest of the three, so
    /// the settled walk stands taller than the braced hold it came from.</summary>
    public const float DuckKneeFold = 0.16f;

    /// <summary>The duck walk's forward torso pitch, radians. 0.30 (17 degrees) — §12.2's "head
    /// lowered". Between the slide's 0.42 and the tuck's 0.02, and the ordering is the read: pitched
    /// and striding is a duck walk, pitched and planted is a slide, square and low is a
    /// tuck.</summary>
    public const float DuckTiltRad = 0.30f;

    /// <summary>How far both elbows fold in a duck walk, as a fraction of arm length. 0.10 — barely
    /// any. "Extended", and it leaves the arms free to carry the gait's own counter-swing, which is
    /// the motion cue that separates this pose from the still one.</summary>
    public const float DuckArmFold = 0.10f;

    /// <summary>The duck walk's shoulder pitch offset, radians. Zero — the arms are the gait's here,
    /// deliberately. A settled walk that also posed its arms would have two authors on one
    /// channel.</summary>
    public const float DuckArmSweepRad = 0f;

    /// <summary>How much of the gait a duck walk expresses. One — see <see cref="TuckGaitScale"/>;
    /// the duck walk is the state the gait matters most in.</summary>
    public const float DuckGaitScale = 1f;

    // --- THE CHAIN (spec §12.1) ----------------------------------------------------------------
    //
    // §12.1's ladder, and its own priority order for how a partner reads it:
    //
    //   depth 0  nothing - the baseline run and jump
    //   depth 1  arms sweep back, torso pitches forward ~8 deg          the ACTOR's confirmation
    //   depth 2  pitch deepens to ~15 deg, TRAILING LEG EXTENDS         the SILHOUETTE, at 30 m
    //   depth 3  ground-contact tell; the take-off kick at full drive   from behind
    //   depth 4  a foot trail, and the 15 deg pitch HOLDS THROUGH THE LANDING
    //
    // "What must ship for the verb to be readable at all: depth 2's silhouette change." Everything
    // here is continuous in a SMOOTHED depth rather than switched on integers, because §12.1's
    // requirement is that a partner reads deepening rather than a series of discrete costume
    // changes. The functions below all hit §12.1's stated numbers exactly at integer depths and
    // interpolate between them.

    /// <summary>The torso's forward pitch at chain depth 1, radians. 0.140 — §12.1's "~8 degrees"
    /// (8.02). The depth-1 tell is explicitly <i>the actor's own confirmation that a chain started</i>,
    /// not a partner's, so it is small on purpose.</summary>
    public const float ChainPitchDepth1Rad = 0.140f;

    /// <summary>The torso's forward pitch at chain depth 2 and beyond, radians. 0.262 — §12.1's
    /// "~15 degrees" (15.01). This is the pitch that holds through the landing at depth 4.</summary>
    public const float ChainPitchDepth2Rad = 0.262f;

    /// <summary>How far BOTH arms sweep back once a chain is running, radians, positive forward.
    /// −0.45 (26 degrees behind the body) at depth 1 and held. §12.1 depth 1, "arms sweep
    /// back".</summary>
    public const float ChainArmSweepRad = -0.45f;

    /// <summary>How far the LEADING arm reaches forward at depth 2, radians. 0.55. §12.1 depth 2
    /// wants "a straight silhouette line from trailing toe to leading hand" — one hand has to be out
    /// in front for there to be a line, so at depth 2 the lead-side arm crosses from the swept-back
    /// pose to this one while the trailing arm stays back. The result is a diagonal across the whole
    /// figure, which is the outline change that survives thirty metres.</summary>
    public const float ChainArmReachRad = 0.55f;

    /// <summary>How far the TRAILING leg extends behind the body at depth 2, as a fraction of
    /// <c>LocomotionProfile.MaxLegSwingRad</c>, positive forward. −1.0 — the full anatomical swing,
    /// backwards. §12.1's "long-jump stride": the trailing toe is one end of the diagonal and it
    /// only reads if the leg is genuinely straight and genuinely behind.</summary>
    public const float ChainTrailLegFraction = -1.0f;

    /// <summary>Where the LEAD leg goes at depth 2, same units. 0.95 — forward and nearly at the
    /// limit. Against the trail leg's −1.0 that is a 75-degree split, which is a long-jump stride
    /// and not a jog.</summary>
    public const float ChainLeadLegFraction = 0.95f;

    /// <summary>The chain depth at and beyond which the take-off kick fires regardless of how much
    /// upward drive the jump had. 3 — §12.1 depth 3, "the take-off kick fires at full drive". It is
    /// a GATE bypass, never a retune: the kick's amplitude, duration and shape are
    /// <c>TakeoffKickFraction</c>'s, <c>TakeoffKickSec</c>'s and <c>LaunchSnapFraction</c>'s,
    /// unchanged and still Talon's.</summary>
    public const int ChainForcedKickDepth = 3;

    // --- THE ARITHMETIC ------------------------------------------------------------------------

    /// <summary>A clamped 0..1 ramp across <paramref name="lo"/>..<paramref name="hi"/>. The one
    /// shape every chain function below is built out of, so "continuous, and exact at the integers"
    /// is a property of one function rather than of five.</summary>
    public static float Ramp(float value, float lo, float hi)
    {
        if (hi <= lo)
            return value >= hi ? 1f : 0f;
        return Mathf.Clamp((value - lo) / (hi - lo), 0f, 1f);
    }

    /// <summary>The pose one verb asks for. <see cref="MoveVerb.Normal"/> answers with the exact
    /// zero pose — every field 0f, and <c>GaitScale</c> 1f — so a body that is not in a verb is
    /// bit-identical to what shipped before this class existed. That identity is the whole safety
    /// argument for touching a ratified body at all, and it is asserted in
    /// <c>VerbPoseTests</c>.</summary>
    public static Targets For(MoveVerb verb) => verb switch
    {
        MoveVerb.Slide => new Targets(
            SlideKneeFold, SlideTiltRad, SlideArmFold, SlideArmSweepRad, SlideGaitScale,
            SlideLeadLegFraction, SlideTrailLegFraction, LegsPlanted: true),
        MoveVerb.Tuck => new Targets(
            TuckKneeFold, TuckTiltRad, TuckArmFold, TuckArmSweepRad, TuckGaitScale,
            0f, 0f, LegsPlanted: false),
        MoveVerb.DuckWalk => new Targets(
            DuckKneeFold, DuckTiltRad, DuckArmFold, DuckArmSweepRad, DuckGaitScale,
            0f, 0f, LegsPlanted: false),
        _ => Normal,
    };

    /// <summary>The exact zero pose. Named rather than inlined because "Normal changes nothing" is a
    /// claim several tests make and one constant is easier to keep true than five literals.</summary>
    public static readonly Targets Normal = new(
        KneeFold: 0f, ForwardTiltRad: 0f, ArmFold: 0f, ArmSweepRad: 0f, GaitScale: 1f,
        LeadLegFraction: 0f, TrailLegFraction: 0f, LegsPlanted: false);

    /// <summary>
    /// Combine the three eased verb weights into one pose.
    ///
    /// <para><b>A weighted sum is safe here because the weights are one-hot at every instant.</b>
    /// All three ease toward 0 or 1 at the same <see cref="VerbBlendRate"/> and at most one target
    /// is ever 1, so during a Slide→DuckWalk handover the two move at equal and opposite rates and
    /// their sum is exactly 1 throughout; entering or leaving a verb runs the sum between 0 and 1.
    /// It can never exceed 1, which is asserted rather than assumed
    /// (<c>VerbPoseTests.BlendWeights_NeverSumPastOne_AcrossEveryHandover</c>).</para>
    ///
    /// <para><c>GaitScale</c> is the exception and it interpolates from 1 rather than summing from
    /// 0 — it is a multiplier, and a multiplier whose neutral value is 1 cannot be added.</para>
    /// </summary>
    public static Targets Blend(float tuck, float slide, float duck)
    {
        Targets t = For(MoveVerb.Tuck);
        Targets s = For(MoveVerb.Slide);
        Targets d = For(MoveVerb.DuckWalk);
        float w = tuck + slide + duck;
        return new Targets(
            KneeFold: (t.KneeFold * tuck) + (s.KneeFold * slide) + (d.KneeFold * duck),
            ForwardTiltRad: (t.ForwardTiltRad * tuck) + (s.ForwardTiltRad * slide)
                + (d.ForwardTiltRad * duck),
            ArmFold: (t.ArmFold * tuck) + (s.ArmFold * slide) + (d.ArmFold * duck),
            ArmSweepRad: (t.ArmSweepRad * tuck) + (s.ArmSweepRad * slide) + (d.ArmSweepRad * duck),
            // 1 at w = 0, and each verb's own scale at its own full weight.
            GaitScale: 1f + (((t.GaitScale - 1f) * tuck) + ((s.GaitScale - 1f) * slide)
                + ((d.GaitScale - 1f) * duck)),
            LeadLegFraction: (s.LeadLegFraction * slide),
            TrailLegFraction: (s.TrailLegFraction * slide),
            LegsPlanted: w > 0f && slide > 0f);
    }

    /// <summary>The chain's torso pitch at a smoothed depth, radians, positive FORWARD. 0 at depth
    /// 0, §12.1's 8 degrees at depth 1, its 15 degrees at depth 2, and held there — the ladder does
    /// not keep pitching, because §12.1 gives depths 3 and 4 different channels rather than more of
    /// this one.</summary>
    public static float ChainPitchRad(float depth) =>
        (ChainPitchDepth1Rad * Ramp(depth, 0f, 1f))
        + ((ChainPitchDepth2Rad - ChainPitchDepth1Rad) * Ramp(depth, 1f, 2f));

    /// <summary>How much of §12.1's depth-2 long-jump stride is expressed at a smoothed depth. 0 at
    /// depth 1 and below, 1 at depth 2 and above. <b>This is the one tell §12.1 marks as required</b>
    /// — "what must ship for the verb to be readable at all" — because it is the first depth that
    /// changes the outline rather than the shading.</summary>
    public static float ChainStrideWeight(float depth) => Ramp(depth, 1f, 2f);

    /// <summary>How much of §12.1's depth-4 grounded hold is expressed. 0 at depth 3 and below, 1 at
    /// depth 4. It is what keeps the 15-degree pitch on the body <i>after</i> the landing instead of
    /// recovering — §12.1: "during the 0.35 s grace it tells a partner he is still hot, which is the
    /// only moment a partner could act on it".
    ///
    /// <para>No timer is needed for the grace and none is kept: <c>ChainDepth</c> itself decays when
    /// the grace expires (§6.4), so a non-zero depth on the ground <i>is</i> "still hot".</para></summary>
    public static float ChainGroundHoldWeight(float depth) => Ramp(depth, 3f, 4f);

    /// <summary>Both arms' swept-back angle at a smoothed depth, radians, positive forward. Ramps in
    /// across depth 0→1 and holds. §12.1 depth 1.</summary>
    public static float ChainArmSweepAt(float depth) => ChainArmSweepRad * Ramp(depth, 0f, 1f);

    /// <summary>The LEADING arm's angle at a smoothed depth, radians. Sweeps back with the other one
    /// through depth 1, then crosses to <see cref="ChainArmReachRad"/> across depth 1→2 so that
    /// depth 2 has a hand out in front for the diagonal to end at.</summary>
    public static float ChainLeadArmAt(float depth) =>
        Mathf.Lerp(ChainArmSweepAt(depth), ChainArmReachRad, ChainStrideWeight(depth));

    /// <summary>True when §12.1 depth 3's "the take-off kick fires at full drive" applies — i.e. the
    /// kick should fire on this take-off even though the upward drive is under
    /// <c>TakeoffKickMinDriveFraction</c>. Takes the raw replicated depth, not the smoothed read: a
    /// take-off is an instant and the counter is exact at it.</summary>
    public static bool ForcesTakeoffKick(int depth) => depth >= ChainForcedKickDepth;

    /// <summary>
    /// One verb's pose, in the units the rig actually consumes.
    /// </summary>
    /// <param name="KneeFold">Both feet pulled toward their hips, as a fraction of leg length. Also
    /// what the rig drops by: with the feet planted, shortening both legs by <c>L × fold</c> lowers
    /// everything above them by exactly that (see <c>AvatarVisual.CrouchDropM</c>).</param>
    /// <param name="ForwardTiltRad">Torso pitch, radians, <b>positive forward</b>. The rig's own sign
    /// for forward is negative; the conversion happens once, at the one place the tilt is
    /// assembled.</param>
    /// <param name="ArmFold">Both elbows, as a fraction of arm length.</param>
    /// <param name="ArmSweepRad">Both shoulders, radians, positive forward.</param>
    /// <param name="GaitScale">Multiplier on the gait's expressed weight. 1 leaves it
    /// alone.</param>
    /// <param name="LeadLegFraction">Where the lead leg's hip goes, as a fraction of
    /// <c>LocomotionProfile.MaxLegSwingRad</c>, positive forward. Only meaningful when
    /// <paramref name="LegsPlanted"/>.</param>
    /// <param name="TrailLegFraction">The same for the trail leg.</param>
    /// <param name="LegsPlanted">True when the legs are POSED rather than left to the gait or the
    /// clip — the slide only. Needed because a clip-driven body's legs are not scaled by the gait
    /// weight, so <paramref name="GaitScale"/> alone cannot stop a stride the clip is
    /// authoring.</param>
    public readonly record struct Targets(
        float KneeFold,
        float ForwardTiltRad,
        float ArmFold,
        float ArmSweepRad,
        float GaitScale,
        float LeadLegFraction,
        float TrailLegFraction,
        bool LegsPlanted);
}
