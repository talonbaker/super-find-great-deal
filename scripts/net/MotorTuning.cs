using System;
using Godot;

namespace MpFoundation.Net;

/// <summary>
/// <b>Every movement feel number, in one value.</b> One field per row of the MOVE-4a knob table
/// (<c>docs/design/2026-08-26-movement-tuning-surface.md</c> §2.2), carrying the same name as the
/// constant it replaces so the printed C# (<see cref="MotorTuningPrint"/>) is a straight
/// substitution and a reader grepping for <c>Deceleration</c> finds both ends of the round trip.
///
/// <para><b>A value type: copied, never aliased</b>, so a panel editing a working copy can never
/// half-apply it. <see cref="Current"/> is what the motor reads; <see cref="TryApply"/> is the
/// only thing that writes it (§7.2's single writer), and it refuses while a network session is
/// live in every build configuration.</para>
///
/// <para><b>MOVE-4b shipped the seam, not new feel</b> — §8's identity contract. MOVE-4c wired the
/// apex hang and the camera dip and MOVE-4f wired the dip's ramp, and every one of those shipped at
/// a value that changed nothing until a slider moved. Four waves of instrument, no change to the
/// body Talon had approved.</para>
///
/// <para><b>MOVE-8 (2026-08-28) is where the seam was SPENT, which is what it was built for.</b>
/// Talon sat at the keyboard, ruled thirteen rows against controls, and
/// <see cref="Default"/> is now that ruling — <c>MovementPresets.RollBase</c>, field for field,
/// asserted by <c>MovementPresetTests.TheShippedDefaultIsRollBase_FieldForField</c>. So "reproduces
/// today's shipped behaviour" is no longer what <see cref="Default"/> means; it IS today's shipped
/// behaviour, and <c>MotorTuningDefaultIdentityTests</c> still transcribes every row against a
/// literal so nothing moves without a test saying so. <b>A red in that file with no named ruling
/// behind it is still a bug, not a value to update.</b></para>
/// </summary>
public readonly record struct MotorTuning
{
    // --- Ground ------------------------------------------------------------------------------
    /// <summary>Knob 1. Jog-gear top speed, m/s. <see cref="AvatarMotor.MoveSpeed"/>.</summary>
    public float MoveSpeed { get; init; }

    /// <summary>Knob 2. Sprint multiple of <see cref="MoveSpeed"/>.</summary>
    public float SprintMultiplier { get; init; }

    /// <summary>Knob 3. Build rate along the current heading, m/s².</summary>
    public float Acceleration { get; init; }

    /// <summary>Knob 4. Shed rate with no input, m/s².</summary>
    public float Deceleration { get; init; }

    /// <summary>Knob 5. Redirect rate, m/s².</summary>
    public float TurnAcceleration { get; init; }

    /// <summary>Knob 6. Travel-facing lerp rate, s⁻¹.</summary>
    public float TurnLerp { get; init; }

    /// <summary>Knob 7. Aimed-facing lerp rate, s⁻¹.</summary>
    public float AimTurnLerp { get; init; }

    /// <summary>Knob 58 (FP-1, 2026-09-19). <b>Does the body face where the player LOOKS, or where
    /// it is GOING?</b> 1 = the look (this game's default), 0 = travel direction (the foundation's
    /// third-person rule, and what every prior packet shipped).
    ///
    /// <para><b>A mode, not a fork.</b> <c>AvatarMotor.Step</c> already took an optional
    /// <c>faceYaw</c>; at 1 this row is what supplies it, from the same sanitized
    /// <c>MoveIntent.AimYaw</c> that already rides the wire and is already replayed in
    /// reconciliation. So the authority, the owner's prediction and the replay all derive the same
    /// facing from the same byte — nothing new crosses the wire and no side computes it a second
    /// way. <c>ResolveYaw</c> is untouched and its tests still pass an explicit angle.</para>
    ///
    /// <para><b>Why it defaults ON here.</b> The game is first person: a player's body is the only
    /// thing the OTHER player can read, and a body that faces its travel direction tells them where
    /// you are walking while you stare at the shelf you are about to hide something in. Facing the
    /// look is also the only way the carry anchor, the aim ray and the rendered view can agree
    /// about which way "in front of me" is.</para>
    ///
    /// <para><b>Its cost, stated.</b> An intent that carries no look — every scripted bot — reports
    /// <c>AimYaw = 0</c>, so at 1 a bot faces world-zero instead of its heading. The bot brains
    /// therefore fill <c>AimYaw</c> from their own travel direction (see
    /// <c>DeterministicWalkIntentSource</c>), which keeps a fixture facing the way it did before
    /// this row existed and makes its replicated aim ray honest at the same time.</para></summary>
    public float BodyYawFollowsAim { get; init; }

    // --- Gravity -----------------------------------------------------------------------------
    /// <summary>Knob 8. Base gravity, m/s².</summary>
    public float Gravity { get; init; }

    /// <summary>Knob 9. Extra gravity while descending, ×.</summary>
    public float FallGravityMultiplier { get; init; }

    /// <summary>Knob 10. Fraction of gravity removed at the exact apex. <c>0.00</c> is still the
    /// exact no-op (§8 AC-D4), but <b>MOVE-8 is where it stopped being what ships</b>: Talon ruled
    /// 0.30 on 2026-08-28 as part of FORGIVING, so the hang is live and every branch of
    /// <c>AvatarMotor.GravityFor</c> is scaled near the apex. Its ceiling is a live function of the
    /// window and both gravities — see
    /// <c>MotorTuningInvariants.ApexHangStrengthCeiling</c>, which reads 0.600 at the shipped
    /// 2.00 m/s window.</summary>
    public float ApexHangStrength { get; init; }

    /// <summary>Knob 11. Half-width of the apex window, m/s — the speed at which the hang has
    /// faded to nothing. Inert while <see cref="ApexHangStrength"/> is zero, which it no longer is:
    /// <b>MOVE-8 made this row live too</b>, and its own ceiling with it (5.90 at the ruled
    /// strength, where it used to be an unconditional 6.00 because a term that does not run cannot
    /// constrain anything).</summary>
    public float ApexHangWindowMps { get; init; }

    // --- Jump --------------------------------------------------------------------------------
    /// <summary>Knob 12. Launch vertical speed, m/s.</summary>
    public float JumpVelocity { get; init; }

    /// <summary>Knob 13. Gravity multiple while rising with the key released, ×.</summary>
    public float JumpReleaseGravityMultiplier { get; init; }

    /// <summary>Knob 14. Coyote window, s.</summary>
    public float CoyoteTimeSec { get; init; }

    /// <summary>Knob 15. Jump-buffer window, s.</summary>
    public float JumpBufferSec { get; init; }

    // --- Air ---------------------------------------------------------------------------------
    /// <summary>Knob 16. Airborne share of <see cref="Acceleration"/>.</summary>
    public float AirControlBuild { get; init; }

    /// <summary>Knob 17. Airborne share of <see cref="TurnAcceleration"/>.</summary>
    public float AirControlTurn { get; init; }

    /// <summary>Knob 18. Airborne share of <see cref="Deceleration"/>.</summary>
    public float AirControlBrake { get; init; }

    // --- Skid --------------------------------------------------------------------------------
    /// <summary>Knob 19. Skid entry threshold as a <i>fraction</i> of <see cref="MoveSpeed"/>
    /// (§2.5): the derivation is load-bearing, so the field is the fraction and
    /// <c>AvatarMotor.SkidEnterSpeedMps</c> stays the product.</summary>
    public float SkidEnterSpeedFraction { get; init; }

    /// <summary>Knob 20. Most-aligned dot that still counts as a reversal.</summary>
    public float SkidAlignmentMax { get; init; }

    /// <summary>Knob 21. Skidding shed rate, m/s².</summary>
    public float SkidDeceleration { get; init; }

    /// <summary>Knob 22. Speed at which a skid ends, m/s.</summary>
    public float SkidExitSpeedMps { get; init; }

    /// <summary>Knob 23. Hard ceiling on one skid, s.</summary>
    public float SkidMaxSec { get; init; }

    // --- Landing (AvatarVisual — carried by the seam, wired by MOVE-4c) ----------------------
    /// <summary>Knob 24. Fall speed below which a landing does not register, m/s.</summary>
    public float LandMinFallMps { get; init; }

    /// <summary>Knob 25. Fall speed at which the landing reads full intensity, m/s.</summary>
    public float LandFullFallMps { get; init; }

    /// <summary>Knob 26. Takeoff kick duration, s.</summary>
    public float TakeoffKickSec { get; init; }

    /// <summary>Knob 27. Air drive above which the takeoff kick fires, fraction.</summary>
    public float TakeoffKickMinDriveFraction { get; init; }

    // --- Camera (SandboxCamera — MOVE-4c's, wired by MOVE-4f) --------------------------------
    /// <summary>Knob 28. Landing camera dip depth at a full-intensity landing, m. <c>0.00</c> is
    /// the shipped value and an exact no-op (§8 AC-D3): the dip stays off until Talon turns it
    /// up.</summary>
    public float CameraDipStrengthM { get; init; }

    /// <summary>Knob 29. Dip attack, s. Inert at strength 0.</summary>
    public float CameraDipAttackSec { get; init; }

    /// <summary>Knob 30. Dip recovery, s. Inert at strength 0.
    /// 0.26 agrees with <c>AvatarVisual.LandAbsorbSec</c>.</summary>
    public float CameraDipRecoverSec { get; init; }

    /// <summary>Knob 31. <b>MOVE-4f's.</b> Exponent on the normalised landing fall speed — how fast
    /// the dip ramps up from zero, and what replaced the dip's fall-speed gate (Talon's choice C,
    /// 2026-08-27). See <c>SandboxCamera.CameraDipRampPower</c> for why 6. Inert at strength 0.</summary>
    public float CameraDipRampPower { get; init; }

    // --- Slide (MOVE-5, spec §11.1 rows 31-41) -------------------------------------------------
    // The whole crouch family ships LIVE (§11.2): none of it can fire without a deliberate held
    // button through a landing, so a player who does not make that input sees a bit-identical body.
    // That is a per-row judgement, not a blanket one — the two rows that fire with NO new input
    // (ChainBonusMps, AirJumpMode) shipped at their exact no-ops instead. MOVE-8: AirJumpMode has
    // since LEFT its no-op on Talon's ruling, and the caution was right — a double jump does change
    // what every gap means, which is why MOVE-8 owes a changed-class list. ChainBonusMps stays at
    // 0.00, now by ruling rather than by caution.

    /// <summary>Knob 31. How long the jump button must stay down on the ground before a crouch verb
    /// enters, in seconds. 0.20 s = 12 ticks.</summary>
    public float JumpHoldWindowSec { get; init; }

    /// <summary>Knob 32. <b>Fork O1's knob, not a decision</b> (§4.3, MOVE-5b ruling 3): 1 starts
    /// the verb on the touchdown tick itself, 0 makes it wait out the hold window on the ground.
    /// Both behaviours are built; Talon decides with his hands.</summary>
    public float TouchdownSlideImmediate { get; init; }

    /// <summary>Knob 33. Slide entry threshold as a <i>fraction</i> of <see cref="MoveSpeed"/> —
    /// the same shape knob 19 uses and for the same reason (§2.5: the derivation is load-bearing).
    /// 1.22 is <c>LocomotionProfile.SprintEnterMps</c> exactly, so the rule has a name:
    /// <b>you can only slide out of a sprint.</b></summary>
    public float SlideEnterSpeedFraction { get; init; }

    /// <summary>Knob 34. Speed at which a slide settles, m/s. Must stay strictly below
    /// <see cref="SlideEnterSpeedMps"/> — the gap IS the anti-chatter mechanism (§4.3).</summary>
    public float SlideExitSpeedMps { get; init; }

    /// <summary>Knob 35. Slide shed rate, m/s^2. Below ground <c>Deceleration</c> (21) and below
    /// <c>SkidDeceleration</c> (13) — a slide is <i>slippery</i>, and that is the whole
    /// verb.</summary>
    public float SlideDeceleration { get; init; }

    /// <summary>Knob 36. How fast the slide's heading carves toward the stick, deg/s. A
    /// <i>rotation</i> of the velocity vector, so the carve preserves speed exactly and cannot
    /// fight the deceleration.</summary>
    public float SlideTurnRateDeg { get; init; }

    /// <summary>Knob 37. Floor on the slide's SELF-termination, s. <b>It never blocks the release
    /// exit</b> — that is what makes it legal under the weight principle (§4.5).</summary>
    public float SlideMinSec { get; init; }

    /// <summary>Knob 38. Hard ceiling on one slide, s. Inert at the defaults (the speed exit fires
    /// at 1.107 s); it earns its keep at the edge of the knob range, where a low
    /// <see cref="SlideDeceleration"/> would otherwise make an exitless state reachable by a
    /// slider.</summary>
    public float SlideMaxSec { get; init; }

    /// <summary>Knob 39. Duck-walk and tuck wish cap as a fraction of <see cref="MoveSpeed"/>.
    /// 0.45 is <c>LocomotionProfile.WalkSpeedMps</c> exactly — the duck walk costs no speed
    /// relative to a walk, because "weight is skin, not friction" (§5.3).</summary>
    public float DuckWalkSpeedFraction { get; init; }

    /// <summary>Knob 40. 1 = a settling slide always latches into a duck walk; 0 = only if the
    /// stick is inside <see cref="DuckWalkEntryConeDeg"/> of travel. Ships 0, the brief's literal
    /// reading ("hold <i>forward</i> through the slide" — forward is a condition).</summary>
    public float DuckWalkGuaranteed { get; init; }

    /// <summary>Knob 41. Largest stick-vs-travel deviation that still settles, degrees. Inert at
    /// <see cref="DuckWalkGuaranteed"/> = 1.</summary>
    public float DuckWalkEntryConeDeg { get; init; }

    // --- Chain (MOVE-5, spec §11.1 rows 42-45) -------------------------------------------------

    /// <summary>Knob 42. Ground dwell a chain survives before it starts decaying, s. 0.35 s is
    /// 3.02 m of running between hops at sprint — "generous", as a number (§6.4).</summary>
    public float ChainGraceSec { get; init; }

    /// <summary>Knob 43. Seconds per lost chain level once the grace has run out. Linear in depth:
    /// from the cap, full loss takes 1.85 s. <b>Decays gradually, never resets to zero.</b></summary>
    public float ChainDecayIntervalSec { get; init; }

    /// <summary>Knob 44. Wish-speed gained per chain level, m/s. <b><c>0.00</c> is the exact no-op
    /// and it is what ships</b> (§11.2): the chain is the one thing in this wave that fires with no
    /// new input at all, so it would change the ordinary hop chains the playground's flow course
    /// was built to compare against. A slider drag away; §6.2's ladder is at 0.60.</summary>
    public float ChainBonusMps { get; init; }

    /// <summary>Knob 45. Cap on <c>MoveState.ChainDepth</c>. <b>Max 7 is a WIRE WIDTH</b>, not
    /// taste — three bits. Clamped in the validator, not merely in the widget (§11.3).</summary>
    public float ChainMaxDepth { get; init; }

    // --- Air, continued (MOVE-5, spec §11.1 rows 46-52) ----------------------------------------

    /// <summary>Knob 46. <b>0 = off (the exact no-op), 1 = traditional — <i>and 1 is what ships
    /// since MOVE-8</i> — 2 = the Kick.</b>
    ///
    /// <para>It shipped at 0 because a double jump changes what every gap in the playground means,
    /// and the baseline Talon approved was the only baseline the lab had. He then ruled it on
    /// 2026-08-28 against that baseline — <i>"the jump plus double jump works a little bit more
    /// than the jump plus kick"</i>, SHIFT+1 against SHIFT+2, one variable apart — and the caution
    /// turned out to be exactly right: MOVE-8's <c>bt3_blue_verify.gd</c> census found 58 jumps in
    /// the blue tower alone that changed reachability class. The four <c>Kick*</c> rows below are
    /// dead code at mode 1.</para></summary>
    public float AirJumpMode { get; init; }

    /// <summary>Knob 47. Air jumps allowed per flight. <b>Max 3 is a WIRE WIDTH</b> — two bits.
    /// Same validator clamp, same reason as <see cref="ChainMaxDepth"/>.</summary>
    public float AirJumpCountMax { get; init; }

    /// <summary>Knob 48. Mode 1's launch as a fraction of <see cref="JumpVelocity"/>. Inert unless
    /// <see cref="AirJumpMode"/> is 1.</summary>
    public float AirJumpVelocityFraction { get; init; }

    /// <summary>Knob 49. The Kick's share of the fall it converts into forward speed. <b>Max 1.00
    /// is hard:</b> above 1 the Kick returns more speed than the fall carried — energy from
    /// nothing, and precisely the floaty read the variant exists to avoid.</summary>
    public float KickConversionFraction { get; init; }

    /// <summary>Knob 50. Flat term added to the Kick's gain, m/s. Inert unless
    /// <see cref="AirJumpMode"/> is 2.</summary>
    public float KickHorizontalGainMps { get; init; }

    /// <summary>Knob 51. What the Kick does to vertical speed, m/s — it <i>arrests</i> the fall, it
    /// does not climb. <b>Max 6.00 is hard:</b> at 8.4 it would equal <see cref="JumpVelocity"/>
    /// and mode 2 would become mode 1 wearing mode 2's name.</summary>
    public float KickVerticalMps { get; init; }

    /// <summary>Knob 52. The committed-run floor the Kick needs, m/s. 4.05 at the shipped tuning is
    /// <c>SkidEnterSpeedMps</c> — deliberately the same "committed run" line MOVE-3a §2.4 already
    /// made a rule. A separate knob rather than a derived one because MOVE-4's table is one float
    /// per row.</summary>
    public float KickMinSpeedMps { get; init; }

    // --- Anticipation (MOVE-5f, the last unbuilt item in Talon's lab brief) ---------------------
    //
    // Talon asked for BOTH candidate approaches behind a toggle so he could feel the difference
    // rather than argue it, plus a knob for coil depth against hold duration. Five rows: the mode,
    // then a pair per approach. See AnticipationCoil for approach 1 and AvatarMotor.LaunchCoilFactor
    // for approach 2, and both for why "hold longer -> deeper coil" is resolved AFTER the launch.

    /// <summary>Knob 53. <b>0 = off (the EXACT no-op, and what ships), 1 = the real coil pose,
    /// 2 = the coil baked into the rise curve.</b>
    ///
    /// <para><b>Mode 1 costs the arc nothing</b> — it is pose only, and whatever the arc is, it is
    /// unchanged to the bit at mode 1. (MOVE-8: that arc is now 1.407 / 0.667 / 0.300 / 0.233; the
    /// IDENTITY between mode 0 and mode 1 is the claim, not the four numbers, and
    /// <c>AnticipationCoilTests</c> asserts it with no tolerance at all.) It still ships OFF, for
    /// the reason every MOVE-4/5 row shipped at its no-op: it changes the body Talon approved, and
    /// the lab exists so he picks with his hands. <b>Mode 2 moves the arc by construction</b>, because
    /// it is a gravity term; <c>MotorTuningInvariants.LaunchCoilStrengthCeiling</c> is what says how
    /// far it may move before the approved window breaks.</para></summary>
    public float AnticipationMode { get; init; }

    /// <summary>Knob 54. <b>The coil-depth knob the brief asks for.</b> How much of
    /// <c>AnticipationCoil</c>'s authored amplitude a fully-committed hold reaches, 0-1. Inert
    /// unless <see cref="AnticipationMode"/> is 1.</summary>
    public float AnticipationDepth { get; init; }

    /// <summary>Knob 55. <b>The depth-versus-hold-duration relationship, in one number:</b> the
    /// seconds of RISE at which the coil reaches <see cref="AnticipationDepth"/>. A held ascent is
    /// 0.382 s and a tapped one 0.127 s at the shipped gravity, so at 0.22 s a held jump coils to
    /// 1.000 and a tap to 0.617 (<c>AnticipationCoil.Depth</c>). Inert unless
    /// <see cref="AnticipationMode"/> is 1.</summary>
    public float AnticipationCoilSec { get; init; }

    /// <summary>Knob 56. Mode 2's extra gravity at the instant of launch, as a fraction — 0.35
    /// means the opening frames of the rise run at 1.35x. Inert unless
    /// <see cref="AnticipationMode"/> is 2.</summary>
    public float AnticipationBakedStrength { get; init; }

    /// <summary>Knob 57. The share of the launch speed over which mode 2's term decays to nothing.
    /// 0.35 means it is gone once the body has shed 35% of <see cref="JumpVelocity"/> — "the first
    /// few frames", in the brief's words, expressed in the only currency
    /// <c>AvatarMotor.GravityFor</c> has. Inert unless <see cref="AnticipationMode"/> is 2.</summary>
    public float AnticipationBakedWindow { get; init; }

    /// <summary>
    /// <b>The shipped body — Talon's ruling of 2026-08-28, landed by MOVE-8.</b>
    ///
    /// <para><b>What changed and why it is not a tuning tweak.</b> This property used to be the §8
    /// identity contract: "today's shipped behaviour, exactly", every row transcribed from the
    /// constant it replaced. That contract did its job — it carried the motor through MOVE-4 and
    /// MOVE-5 without a single silent behaviour change — and MOVE-7 spent it deliberately. Talon
    /// sat at the keyboard against controls and ruled thirteen of these rows, and
    /// <c>MovementPresets.RollBase</c> is that ruling assembled. <b>This is now that preset,
    /// field for field</b>, and <c>MovementPresetTests.ShippedDefaultIsRollBase</c> is the guard
    /// that says so — the dev preset and the shipped default cannot drift apart again.</para>
    ///
    /// <para><b>The thirteen ruled rows</b>, each against the value it replaced:
    /// <c>MoveSpeed</c> 5.4 → <b>3.8</b> (the ladder 3.8/4.4/5.4/6.4, every other row pinned —
    /// <i>"SHIFT+5 feels the best"</i>); <c>Gravity</c> 22 → <b>24</b>,
    /// <c>FallGravityMultiplier</c> 1.35 → <b>1.50</b>, <c>ApexHangStrength</c> 0.00 → <b>0.30</b>,
    /// <c>JumpReleaseGravityMultiplier</c> 3.0 → <b>4.00</b>, <c>CoyoteTimeSec</c> and
    /// <c>JumpBufferSec</c> 0.12 → <b>0.30</b>, <c>AirControlBuild/Turn/Brake</c>
    /// 0.45/0.35/0.30 → <b>0.70/0.60/0.55</b> (all eight are FORGIVING, <i>"my favourite so far in
    /// every regard"</i>); <c>AirJumpMode</c> 0 → <b>1</b>, the traditional double jump, with
    /// <c>AirJumpCountMax</c> 1 and <c>AirJumpVelocityFraction</c> 0.80 already at the values it
    /// wants. <c>SprintMultiplier</c> stays 1.6 (<i>"let's keep the sprint button"</i>) and
    /// <c>ChainBonusMps</c> stays at its exact no-op 0.00 (<i>"I would rather have it more
    /// streamlined"</i>) — both rulings that a row NOT moving is the evidence for.</para>
    ///
    /// <para><b>Two rows that were derived from the old speed and are now stale by construction,
    /// stated rather than silently moved</b> (MOVE-8 leaves both, and the report carries them as
    /// forks): <see cref="SkidEnterSpeedFraction"/> is a fraction so the skid entry follows the
    /// speed down to <c>0.75 × 3.8 = 2.85 m/s</c> — correct and intended — but
    /// <see cref="KickMinSpeedMps"/> is the LITERAL <c>4.05</c> that used to BE that product, and
    /// nothing moved it — so the Kick's minimum entry speed now sits at 1.07× the jog rather than
    /// at the skid entry it was copied from. It is <b>inert</b> at <c>AirJumpMode 1</c> (the Kick
    /// never runs), which is why MOVE-8 reports it rather than changing it: re-deriving it would
    /// be a second, unruled opinion smuggled in beside Talon's.</para>
    ///
    /// <para><b>The arc this produces is measured, not assumed</b> — see
    /// <c>MotorArc</c>, which derives it from these rows, and MOVE-8's report for the in-engine
    /// capture that confirms it. <c>MotorTuningDefaultIdentityTests</c> still asserts every row
    /// against a literal, so no value here moves without a test saying so.</para>
    /// </summary>
    public static MotorTuning Default { get; } = new()
    {
        // MOVE-8 / MOVE-7 ruling: the speed ladder's winning rung. Four gears are declared as
        // FRACTIONS of this row (sprint, skid entry, slide entry, duck walk), so it moves the whole
        // ladder together and their relationships are unchanged.
        MoveSpeed = 3.8f,
        SprintMultiplier = 1.6f,
        Acceleration = 9f,
        Deceleration = 21f,
        TurnAcceleration = 34f,
        TurnLerp = 12f,
        AimTurnLerp = 22f,
        // FP-1: this game is first person, so the body faces the look. See the field's doc for
        // why this is a mode on the existing faceYaw seam rather than a second facing rule.
        BodyYawFollowsAim = 1f,

        // MOVE-8 / FORGIVING: heavier gravity with a real hang at the top and a sharp release cut.
        // The apex barely moves (1.534 → 1.407 m in the playground's convention) because the extra
        // gravity and the hang pull against each other; what changes is the SHAPE.
        Gravity = 24f,
        FallGravityMultiplier = 1.50f,
        ApexHangStrength = 0.30f,
        ApexHangWindowMps = 2.00f,

        JumpVelocity = 8.4f,
        JumpReleaseGravityMultiplier = 4.00f,
        // MOVE-8 / FORGIVING: both forgiveness timers at their generous end. 0.30 s is 18 ticks.
        CoyoteTimeSec = 0.30f,
        JumpBufferSec = 0.30f,

        // MOVE-8 / FORGIVING: real air authority. These leave the "30-60% of ground control"
        // window MOVE-3 was specified against — deliberately, on Talon's ruling; see
        // AirborneControlTests.ThreeAirFractions_AreInsideTheDesignWindow_AndOrdered.
        AirControlBuild = 0.70f,
        AirControlTurn = 0.60f,
        AirControlBrake = 0.55f,

        SkidEnterSpeedFraction = 0.75f,
        SkidAlignmentMax = -0.5f,
        SkidDeceleration = 13f,
        SkidExitSpeedMps = 1.2f,
        SkidMaxSec = 0.75f,

        LandMinFallMps = 2.5f,
        LandFullFallMps = 14f,
        TakeoffKickSec = 0.16f,
        TakeoffKickMinDriveFraction = 0.4f,

        CameraDipStrengthM = 0.00f,
        CameraDipAttackSec = 0.05f,
        CameraDipRecoverSec = 0.26f,
        CameraDipRampPower = 6f,

        // MOVE-5, spec §11.1. Twenty-two rows with no constant to transcribe — the verbs did not
        // exist. ChainBonusMps still ships at its EXACT no-op 0.00 — now by Talon's ruling rather
        // than by caution — and AirJumpMode has LEFT its no-op for 1, the traditional double jump.
        JumpHoldWindowSec = 0.20f,
        TouchdownSlideImmediate = 0f,
        SlideEnterSpeedFraction = 1.22f,
        SlideExitSpeedMps = 2.00f,
        SlideDeceleration = 6.0f,
        SlideTurnRateDeg = 90f,
        SlideMinSec = 0.12f,
        SlideMaxSec = 1.40f,
        DuckWalkSpeedFraction = 0.45f,
        DuckWalkGuaranteed = 0f,
        DuckWalkEntryConeDeg = 60f,

        ChainGraceSec = 0.35f,
        ChainDecayIntervalSec = 0.50f,
        ChainBonusMps = 0.00f,
        ChainMaxDepth = 4f,

        // MOVE-8 / MOVE-7 ruling: the traditional double jump, one of them, at 0.80 of the ground
        // jump's launch speed. "I think the jump plus double jump works a little bit more than the
        // jump plus kick" — SHIFT+1 against SHIFT+2, one variable apart on an identical base.
        // The four Kick rows below are now DEAD CODE at this mode; they are left at their MOVE-5
        // values so the lab's Kick presets keep meaning what they meant when Talon judged them.
        AirJumpMode = 1f,
        AirJumpCountMax = 1f,
        AirJumpVelocityFraction = 0.80f,
        KickConversionFraction = 0.35f,
        KickHorizontalGainMps = 1.20f,
        KickVerticalMps = 1.60f,
        KickMinSpeedMps = 4.05f,   // MOVE-8: stale-by-construction, see the remark above. Inert.

        // MOVE-5f, the anticipation. FIVE rows, and the FIRST of them is the exact no-op: at
        // AnticipationMode 0 neither approach runs, GravityFor is byte-identical to its pre-MOVE-5f
        // self, and AvatarVisual writes the same pose it wrote yesterday. The other four are slider
        // START POSITIONS, not behaviours — every one of them is inert until the mode leaves 0.
        AnticipationMode = 0f,
        AnticipationDepth = 0.60f,
        AnticipationCoilSec = 0.22f,
        AnticipationBakedStrength = 0.35f,
        AnticipationBakedWindow = 0.35f,
    };

    /// <summary>
    /// <b>What the motor reads, this instant.</b> Ambient mutable state read by
    /// server-authoritative, client-predicted code — which is exactly right in a one-process lab
    /// and a silent desync generator in a session, which is why <see cref="TryApply"/> exists and
    /// why this setter is private (§7).
    /// </summary>
    public static MotorTuning Current { get; private set; } = Default;

    /// <summary>Effective skid entry speed, m/s — <see cref="SkidEnterSpeedFraction"/> against
    /// <see cref="MoveSpeed"/>. The quantity the pins and the anti-chatter bound are stated
    /// in.</summary>
    public float SkidEnterSpeedMps => SkidEnterSpeedFraction * MoveSpeed;

    /// <summary>Effective slide entry speed, m/s — <see cref="SlideEnterSpeedFraction"/> against
    /// <see cref="MoveSpeed"/>, exactly as <see cref="SkidEnterSpeedMps"/> is. 6.588 at the shipped
    /// tuning, which is <c>LocomotionProfile.SprintEnterMps</c>: <b>you can only slide out of a
    /// sprint</b> (§4.3). The quantity the anti-chatter bound is stated in.</summary>
    public float SlideEnterSpeedMps => SlideEnterSpeedFraction * MoveSpeed;

    /// <summary>Effective duck-walk / tuck wish cap, m/s. 2.43 at the shipped tuning, which is
    /// <c>LocomotionProfile.WalkSpeedMps</c> exactly (§5.3).</summary>
    public float DuckWalkSpeedMps => DuckWalkSpeedFraction * MoveSpeed;

    /// <summary>The ground hold window in whole ticks — <c>round(sec x 60)</c>, 12 at the shipped
    /// 0.20 s. Rounded once, here, so the motor, the tests and any readout cannot disagree about
    /// the integer (§10.3: these counters are integers by construction).</summary>
    public int JumpHoldWindowTicks => TicksOf(JumpHoldWindowSec);

    /// <summary>The slide's self-termination floor in whole ticks — 7 at the shipped 0.12 s.</summary>
    public int SlideMinTicks => TicksOf(SlideMinSec);

    /// <summary>The slide's hard ceiling in whole ticks — 84 at the shipped 1.40 s.</summary>
    public int SlideMaxTicks => TicksOf(SlideMaxSec);

    /// <summary>The chain grace in whole ticks — 21 at the shipped 0.35 s.</summary>
    public int ChainGraceTicks => TicksOf(ChainGraceSec);

    /// <summary>One chain decay level in whole ticks — 30 at the shipped 0.50 s.</summary>
    public int ChainDecayTicks => TicksOf(ChainDecayIntervalSec);

    /// <summary>Seconds to whole ticks, the one rounding (§10.3). Clamped into
    /// <c>[1, AvatarMotor.VerbClockAirborne - 1]</c>: a zero-tick window would make an integer
    /// comparison meaningless, and the ceiling keeps a counted clock from ever reaching the
    /// airborne sentinel by counting. Both ends are unreachable from the knob ranges — the widest
    /// row is <c>SlideMaxSec</c>'s 3.00 s = 180 ticks — so this clamp is a second line of defence
    /// against a hand-edited file, exactly like <c>StepSkid</c>'s own cap.</summary>
    private static int TicksOf(float seconds)
    {
        if (!float.IsFinite(seconds))
            return 1;
        return Math.Clamp((int)MathF.Round(seconds * AvatarMotor.TickRate), 1,
            AvatarMotor.VerbClockAirborne - 1);
    }

    // --- The single writer, and its guard -----------------------------------------------------

    private static Func<bool>? _sessionProbe;

    /// <summary>
    /// <b>The ONLY writer of <see cref="Current"/></b> (§7.2). Refuses while any multiplayer
    /// session exists, in <b>every</b> build configuration — a guard compiled out of release is
    /// absent from exactly the build where the failure is silent.
    ///
    /// <para><b>Refuses rather than throws</b> because the caller is a slider callback: a throw
    /// there kills the panel mid-drag and leaves the tuning half-applied across fields, which is a
    /// worse state than the one being prevented. <paramref name="refusal"/> is what the panel
    /// shows.</para>
    ///
    /// <para>What it prevents, stated so it is recognisable if the guard is ever defeated: not an
    /// exception and not a desync error, but <b>rubber-banding proportional to acceleration,
    /// absent while standing still, visible only to the peer whose tuning differs</b> — i.e.
    /// something every observer would call a network problem (§7.4).</para>
    /// </summary>
    /// <param name="next">The tuning to apply. It is run through <see cref="Validate"/> first, so
    /// a hand-edited file can never put a NaN or an exitless state into the motor.</param>
    /// <param name="refusal">Empty on success; the reason to display on refusal.</param>
    /// <returns><c>true</c> if <see cref="Current"/> now equals the validated
    /// <paramref name="next"/>; <c>false</c> if it is unchanged.</returns>
    public static bool TryApply(in MotorTuning next, out string refusal)
    {
        if (SessionLive())
        {
            refusal = "MotorTuning cannot be changed while a network session is live: the server "
                    + "and every predicting client must simulate identical constants.";
            return false;
        }

        refusal = "";
        Current = Validate(next, out _);
        return true;
    }

    /// <summary>Restores <see cref="Current"/> to <see cref="Default"/> through the same guard.
    /// Same refusal rules; the lab's "reset all" and the test teardown both go through it.</summary>
    public static bool TryReset(out string refusal) => TryApply(Default, out refusal);

    /// <summary>
    /// <b>Is a multiplayer session live?</b> Asks the engine rather than a bookkeeping flag
    /// (§7.2): a future join path cannot fail to set a flag it never has to set.
    ///
    /// <para>Godot installs an <c>OfflineMultiplayerPeer</c> by default in some configurations, so
    /// "a peer exists" is not the test — the peer must be a real one and not disconnected.</para>
    ///
    /// <para><b>If the engine cannot be asked at all, the answer is "live".</b> A guard that
    /// cannot verify safety must refuse, not permit; the failure mode of a wrong <c>true</c> is a
    /// slider that will not move, and of a wrong <c>false</c> is silent rubber-banding nobody
    /// diagnoses for a week.</para>
    /// </summary>
    public static bool SessionLive()
    {
        Func<bool>? probe = _sessionProbe;
        if (probe is not null)
            return probe();

        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
                return false;
            MultiplayerApi? mp = tree.GetMultiplayer();
            MultiplayerPeer? peer = mp?.MultiplayerPeer;
            if (peer is null || peer is OfflineMultiplayerPeer)
                return false;
            return peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected;
        }
        catch (Exception)
        {
            return true;   // cannot verify safety -> refuse. See the remark above.
        }
    }

    /// <summary><b>Test seam</b>, the same shape <c>TelemetryPaths.ResetForTests</c> uses: hands
    /// <see cref="SessionLive"/> an answer so the parity guard can be exercised with no engine.
    /// Pass <c>null</c> to hand the question back to the engine.</summary>
    public static void SetSessionProbeForTests(Func<bool>? probe) => _sessionProbe = probe;

    /// <summary><b>Test seam.</b> Restores <see cref="Current"/> without consulting the guard, so a
    /// test that applied a tuning cannot leak it into the next test.</summary>
    public static void ResetForTests()
    {
        _sessionProbe = null;
        Current = Default;
    }

    // --- Validation -----------------------------------------------------------------------------

    /// <summary>
    /// <b>Clamps a tuning into the legal world</b> — §6.3's rules, applied to a whole tuning
    /// whatever its provenance (a slider, a hand-edited file, a paste). Per field: a non-finite
    /// value is replaced by its shipped default, and anything outside the knob's <c>[min, max]</c>
    /// is <i>clamped</i> rather than defaulted, because a hand-typed 25 for <c>Deceleration</c>
    /// clearly means "high" and defaulting to 21 would silently contradict an explicit intent.
    /// Then the two <b>ordering</b> constraints (§2.3) are enforced, which no per-field bound can
    /// express:
    /// <list type="bullet">
    /// <item><c>LandFullFallMps ≥ max(3.0, LandMinFallMps + 0.5)</c> — it is the divisor in the
    /// landing intensity, and below <c>LandMinFallMps</c> every qualifying landing clamps to full,
    /// collapsing a two-constant gate into one.</item>
    /// <item><c>SkidExitSpeedMps &lt; SkidEnterSpeedFraction × MoveSpeed</c> — the gap between
    /// entry and exit <i>is</i> the anti-chatter mechanism. Collapse it and the skid re-enters the
    /// tick it exits: MECHANICS §2's flicker case, produced by a slider.</item>
    /// </list>
    /// <b>Every clamp is reported</b>, because a value silently moved is a lab lying to the person
    /// using it.
    /// </summary>
    public static MotorTuning Validate(in MotorTuning candidate, out IReadOnlyList<string> warnings)
    {
        var notes = new List<string>();
        MotorTuning t = candidate;

        foreach (MotorKnob knob in MotorTuningKnobs.All)
        {
            float value = knob.Get(t);

            if (!float.IsFinite(value))
            {
                notes.Add($"{knob.Name} was {value} (not a finite number) — replaced by the shipped "
                        + $"default {MotorTuningPrint.Literal(knob.Default, knob.Step)}. A NaN "
                        + "reaching the motor propagates into position and the body is gone for good.");
                t = knob.Set(t, knob.Default);
                continue;
            }

            float clamped = Math.Clamp(value, knob.Min, knob.Max);
            if (clamped != value)
            {
                notes.Add($"{knob.Name} {MotorTuningPrint.Literal(value, knob.Step)} is outside "
                        + $"[{MotorTuningPrint.Literal(knob.Min, knob.Step)}, "
                        + $"{MotorTuningPrint.Literal(knob.Max, knob.Step)}] — clamped to "
                        + $"{MotorTuningPrint.Literal(clamped, knob.Step)}. {knob.BoundReason}");
                t = knob.Set(t, clamped);
            }
        }

        // Ordering constraint 1 — the landing gate must stay a two-constant gate.
        float fullFloor = Math.Max(3.0f, t.LandMinFallMps + 0.5f);
        if (t.LandFullFallMps < fullFloor)
        {
            notes.Add($"LandFullFallMps {t.LandFullFallMps:0.###} is below "
                    + $"max(3.0, LandMinFallMps + 0.5) = {fullFloor:0.###} — raised to it. Below "
                    + "LandMinFallMps every qualifying landing clamps to full intensity and the "
                    + "two-constant gate collapses into one (spec §2.3).");
            t = t with { LandFullFallMps = fullFloor };
        }

        // Ordering constraint 2 — the skid's anti-chatter gap.
        float entry = t.SkidEnterSpeedMps;
        if (t.SkidExitSpeedMps >= entry)
        {
            float capped = Math.Max(MotorTuningKnobs.SkidExitSpeedMps.Min, entry - 0.05f);
            notes.Add($"SkidExitSpeedMps {t.SkidExitSpeedMps:0.###} is not below the skid entry "
                    + $"speed {entry:0.###} m/s — lowered to {capped:0.###}. The gap between entry "
                    + "and exit IS the anti-chatter mechanism; collapse it and the skid re-enters "
                    + "the tick it exits (MECHANICS §2's flicker case, spec §2.3).");
            t = t with { SkidExitSpeedMps = capped };
        }

        // Ordering constraint 3 (MOVE-5 §11.3) — the SLIDE's anti-chatter gap. The same law as
        // constraint 2, on the pair that carries it for the crouch verb: collapse the gap and the
        // slide re-enters the tick it exits. It is a separate constraint rather than a shared one
        // because the two verbs answer different questions and coupling their thresholds would mean
        // tuning one silently retunes the other (§4.3).
        float slideEntry = t.SlideEnterSpeedMps;
        if (t.SlideExitSpeedMps >= slideEntry)
        {
            float capped = Math.Max(MotorTuningKnobs.SlideExitSpeedMps.Min, slideEntry - 0.05f);
            notes.Add($"SlideExitSpeedMps {t.SlideExitSpeedMps:0.###} is not below the slide entry "
                    + $"speed {slideEntry:0.###} m/s — lowered to {capped:0.###}. The gap between "
                    + "entry and exit IS the anti-chatter mechanism; collapse it and the slide "
                    + "re-enters the tick it exits (MECHANICS §2's flicker case, spec §11.3).");
            t = t with { SlideExitSpeedMps = capped };
        }

        // Ordering constraint 4 (MOVE-5 §11.3) — the slide's two duration exits must not overlap,
        // or §3.4's "cannot overlap" row becomes a lie and the min-duration floor would gate the
        // max-duration ceiling it is supposed to precede.
        if (t.SlideMinSec >= t.SlideMaxSec)
        {
            float raised = Math.Min(MotorTuningKnobs.SlideMaxSec.Max, t.SlideMinSec + 0.05f);
            notes.Add($"SlideMaxSec {t.SlideMaxSec:0.###} is not above SlideMinSec "
                    + $"{t.SlideMinSec:0.###} — raised to {raised:0.###}. The two duration exits "
                    + "would otherwise overlap, and spec §3.4's \"cannot overlap\" row is enforced "
                    + "here rather than assumed (spec §11.3).");
            t = t with { SlideMaxSec = raised };
        }

        warnings = notes;
        return t;
    }
}
