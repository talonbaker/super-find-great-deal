using Godot;

namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>
/// <b>Playback rate driven from ground speed, so <c>stride × cadence = ground speed</c> survives the
/// move to authored clips.</b> This is the trap the packet names in full and the one MOVE-1 already
/// paid for once: <i>"the shipped waddle drove a fixed 3.9 Hz churn with no arithmetic relationship
/// to the sliding at all — which is exactly what 'vibrating while skating' is, and why no amount of
/// amplitude tuning ever helped."</i>
///
/// <para><b>A clip has a fixed stride; the game has a continuous speed.</b> Root motion measures the
/// clip's stride into the locomotion constants (the brief's point 1) and never produces a runtime
/// position delta — but that only fixes the clip's OWN speed. Played at any other speed the same
/// clip skates, in exact proportion to the ratio. The fix is the identity rearranged:</para>
///
/// <code>
///     ground speed = clip nominal speed × playback rate
///     ⇒  playback rate = ground speed ÷ clip nominal speed
/// </code>
///
/// <para><b>The rate is never clamped on the normal path</b>, and that is deliberate. A clamp is
/// arithmetically identical to reintroducing the skate: the moment the rate stops tracking the
/// speed, the planted foot stops being world-stationary. The only guard here is against a
/// degenerate nominal (zero, negative, non-finite), which is a broken export rather than a fast
/// player.</para>
///
/// <para>Engine-free apart from <c>Godot.Mathf</c>; every claim is an assertion in
/// <c>ClipTimeWarpTests</c>, several of them measured off the shipped <c>Greybox.glb</c> bytes.</para>
/// </summary>
public static class ClipTimeWarp
{
    // --- The nominal speeds, measured by ANIM-M2 out of the exported bytes -----------------------
    //
    // A clip's nominal ground speed is the rate its PLANTED foot travels backwards, which is by
    // definition the speed the body travels forwards:
    //
    //     nominal = 2 × reach ÷ (duty × cycle)
    //
    // Walk: reach 0.27345 m, duty 0.2812, cycle 0.8000 s  ->  2.4308 m/s
    // Run:  reach 0.27075 m, duty 0.1094, cycle 0.5333 s  ->  9.2824 m/s
    //
    // Both clips share the SAME reach and it is forced rather than maintained: above ~1.16 m/s the
    // wanted reach exceeds what a 0.440 m leg can physically reach, so both sit on the same
    // leg × sin(MaxLegSwingRad) cap. What differs between them is cadence and duty, not amplitude.
    // ClipTimeWarpTests re-derives both numbers from the shipped BoxKid.glb, so a re-export that
    // changes a cadence turns a test red instead of quietly moving the skate back in.
    //
    // --- BODY-3 (2026-08-29) MOVED EVERY NUMBER IN THIS BLOCK, AND THE REASON IS COUNTER-INTUITIVE
    //     ENOUGH TO BE WORTH THE PARAGRAPH -------------------------------------------------------
    //
    // The box kid was conformed to the classic greybox and its hip fell 0.548 m -> 0.440 m. The
    // naive expectation is that a shorter leg covers less ground per stride and therefore that
    // these speeds should have come DOWN. They did not move at all, and the two halves of why are
    // both worth knowing:
    //
    //   THE EXPORTED HIP ANGLES DID NOT CHANGE. Both libraries saturate the same leg-swing cap,
    //   GAIT_MAX_LEG_SWING_RAD = acos(1 - 0.22) = 0.6747 rad, which is a pure ANGLE with no leg
    //   length in it. Peak |theta| measures 0.8491 rad on the new Walk and 0.8488 on the new Run,
    //   the same as before. What changed is what those angles MEAN in metres: the same swing on a
    //   0.440 m thigh moves the ankle 0.273 m instead of 0.343 m. So the AMPLITUDE fell 20% -- see
    //   SharedFootSweepM, which is the number that really moved.
    //
    //   THE RATE IS AMPLITUDE-INVARIANT, because the duty factor falls with the reach. The clip
    //   author sets duty = reach x cadence / v whenever the leg cap binds, so
    //   nominal = 2 x reach / (duty x cycle) collapses to v -- the speed the clip was authored at --
    //   with the leg cancelling out of both sides. A shorter leg therefore takes SHORTER, MORE
    //   FREQUENT steps at the same ground speed, which is exactly what a shorter leg does.
    //
    // The residual movement in the two constants (Walk +0.4%, Run +1.5%) is not the leg. It is the
    // exporter re-baking the stance onto whole 120 fps frames at a new duty factor, and it is the
    // same order as the -0.4% the old Walk already carried against its own authoring speed.
    //
    // NONE OF THIS WAS VISIBLE FROM THE TEST FAILURE, which reported these speeds 24.5% HIGH. That
    // was ClipTimeWarpTests' own reader holding a hardcoded `LegM = 0.548f` -- a copy of a body
    // dimension, in a test that reads the body, with nothing to keep it honest. It converts real
    // angles with a stale leg. It reads the leg off the shipped node graph now, which is why these
    // constants could be re-derived from the file rather than fitted to make a red go away.

    /// <summary>Ground speed the <c>Walk</c> clip is authored at, m/s. Measured off the shipped
    /// bytes, which is what actually plays -- and robust to the reader's stance quantisation, because
    /// a rate measured over any sub-window of a constant-rate stance is the same rate.
    ///
    /// <para><b>Against <c>LocomotionProfile.WalkSpeedMps</c> (1.7100) this is +42.2%, and that gap
    /// is MOVE-8's open fork, not a defect BODY-3 introduced.</b> The clip library still mirrors a
    /// 5.4 m/s <c>MOVE_SPEED_MPS</c> in <c>author_greybox_clips.py</c> while the motor has moved to
    /// 3.8; closing it means re-authoring the clips at the new speed, which changes what the player
    /// feels and is therefore Talon's ruling. See
    /// <c>ClipTimeWarpTests.MOVE8_TheWalkClipNoLongerAnchorsTheWalkGear_AndIsAwaitingARuling</c>.</para>
    ///
    /// <para><b>DECIDED 2026-08-30 — deliberately NOT closed, for sequencing rather than caution.</b>
    /// Talon was told about the gap and answered <i>"I'm not sure what you mean but this is no
    /// problem just do what you want is best."</i> The judgement made on his behalf was to leave
    /// it, on two grounds.</para>
    ///
    /// <para><b>1. It is not a correctness defect.</b> The warp is what stops feet skating and it
    /// compensates — <c>RateFor</c> divides by the nominal above, so a clip authored fast is played
    /// slow rather than dragged across the ground. BODY-3's conform run put
    /// <c>Run-SandboxTest.ps1</c> at 166/166 in engine, including
    /// <c>gait_stance_foot_is_planted</c> at <b>walk, jog AND sprint</b> — the direct anti-skate
    /// checks, on the conformed body. What remains is an <i>authoring</i> question: the stride's
    /// SHAPE was designed for a faster gait, so it reads as a longer, more loping step than a
    /// 1.71 m/s walk naturally would. That is a look, and a look is judged with eyes.</para>
    ///
    /// <para><b>2. Changing it now would destroy the ability to attribute.</b> BODY-3 has just
    /// re-proportioned the whole body and re-authored this very clip library against the shorter
    /// leg, and <b>Talon has not yet played that build</b>. Stacking a second gait change on the
    /// first, before he has seen the first, means that if the walk feels wrong he cannot tell which
    /// change caused it — and the entire substance of his note 7 was that he could feel the
    /// difference between two bodies. One variable at a time.</para>
    ///
    /// <para><b>So: play it, then rule.</b> If the walk reads as loping or over-strided next time
    /// he plays, closing this fork is the fix and it is bounded — set <c>MOVE_SPEED_MPS</c> in
    /// <c>author_greybox_clips.py</c> to match <c>MotorTuning.Current.MoveSpeed</c>, re-author, and
    /// re-stamp the two nominal constants here from the measured bytes. Never by hand: see the
    /// block above for what a hand-fitted constant cost the last time.</para></summary>
    public const float WalkNominalMps = 2.4308f;

    /// <summary>Ground speed the <c>Run</c> clip is authored at, m/s. Against
    /// <c>LocomotionProfile.SprintSpeedMps</c> (8.6400) this is +5.8%, deliberately: a clip slightly
    /// faster than anything the game asks for is always warped DOWN, never extrapolated past its own
    /// authored extremes.</summary>
    public const float RunNominalMps = 9.2824f;

    /// <summary>The foot sweep both gait clips share, metres — the leg cap, not a tuning value,
    /// and therefore the one number here that BODY-3 genuinely moved: 0.6859 -> 0.5507, which is
    /// 2 x 0.440 x sin(0.6747 rad). The angles are unchanged; the radius they swing on is not.
    ///
    /// <para>This is the AUTHORED cap rather than a measured value, as it always was. The reader in
    /// <c>ClipTimeWarpTests</c> lands a few millimetres under it — 0.5469 at Walk and 0.5415 at Run
    /// today — because it resamples onto 512 uniform phase steps and the stance window it finds
    /// stops short of the authored handover. That is the same signature the old value carried
    /// (0.6809 and 0.6768 against 0.6859), and it is why that assertion runs on a 1 cm bar while the
    /// tight claim is the two clips agreeing with EACH OTHER, which the quantisation cannot
    /// manufacture.</para></summary>
    public const float SharedFootSweepM = 0.5507f;

    /// <summary>Duty factor authored into <c>Walk</c> (fraction of the cycle a foot is planted).
    /// Invariant under time-warping: warping scales the cycle and the stance together.
    ///
    /// <para><b>NOT cosmetic, and not covered by a test that measures it off the file.</b>
    /// <c>AvatarVisual.Animate</c> publishes this value as its own <c>DutyFactor</c> whenever the
    /// clip layer is driving, and <c>LocomotionProfile.FootTrackAt</c> / <c>FootLiftAt</c> place the
    /// procedural foot from it — so a stale duty puts the foot down at the wrong moment even
    /// though the clip is correct. BODY-3 moved it 0.3542 -> 0.2812 because a shorter leg reaches
    /// less and therefore stands for a smaller fraction of a faster cadence.</para></summary>
    public const float WalkDutyFactor = 0.2812f;

    /// <summary>Duty factor authored into <c>Run</c>. The 2.5× mismatch against
    /// <see cref="WalkDutyFactor"/> is why the two may not be blended on a weight — see
    /// <see cref="AnimationCrossfade.LocomotionBlendSec"/>.</summary>
    public const float RunDutyFactor = 0.1094f;

    /// <summary>
    /// <b>Which gait clip a gear plays.</b>
    ///
    /// <para><b>Keyed on <see cref="Gear"/> rather than on a speed of its own, and that is the whole
    /// point of doing it this way.</b> <c>LocomotionProfile</c>'s own comment states the rule — <i>"a
    /// second copy of a speed is how a mirror goes stale"</i> — and a clip-selection threshold in
    /// metres per second would be exactly that: a second boundary, with its own hysteresis, free to
    /// drift out of agreement with the gear the rest of the body is already in. <see cref="Gear"/> is
    /// derived from replicated ground speed, already hysteretic, and already identical on every peer,
    /// so a clip chosen from it cannot chatter and cannot disagree between clients.</para>
    ///
    /// <para><b>The honest cost, and it is a real gap.</b> The library has two gait clips 3.8× apart
    /// and the game's usable band is 0.45–8.64 m/s, so Jog — <i>the default gear, what you are when
    /// you just hold a direction</i> — is served by the Run clip warped down to 0.30–0.72×. That is
    /// arithmetically correct and it will read slow. The fix is a third authored clip at Jog
    /// (5.4 m/s), which is <c>assets/**</c> and therefore ANIM-M2's lane, not this one. Recorded
    /// rather than papered over with a clamp.</para>
    /// </summary>
    public static string LocomotionClipFor(Gear gear) => gear switch
    {
        Gear.Idle => AvatarClipNames.Idle,
        Gear.Walk => AvatarClipNames.Walk,
        _ => AvatarClipNames.Run,
    };

    /// <summary>The nominal ground speed of the clip <paramref name="gear"/> plays. Idle has none —
    /// it is a stand, not a gait — and returns 0, which <see cref="RateFor"/> reads as "do not
    /// warp".</summary>
    public static float NominalMpsFor(Gear gear) => gear switch
    {
        Gear.Idle => 0f,
        Gear.Walk => WalkNominalMps,
        _ => RunNominalMps,
    };

    /// <summary>The duty factor of the clip <paramref name="gear"/> plays.</summary>
    public static float DutyFactorFor(Gear gear) => gear switch
    {
        Gear.Idle => 0f,
        Gear.Walk => WalkDutyFactor,
        _ => RunDutyFactor,
    };

    /// <summary>
    /// <b>Playback rate for one rendered frame.</b> <c>groundSpeed ÷ nominal</c>, and nothing else.
    /// </summary>
    /// <param name="gear">The replicated-derived gear.</param>
    /// <param name="groundSpeedMps">Replicated horizontal speed.</param>
    /// <returns>1.0 for Idle (a stand plays at its authored rate); otherwise the exact ratio.</returns>
    public static float RateFor(Gear gear, float groundSpeedMps)
    {
        float nominal = NominalMpsFor(gear);
        if (nominal <= 0f || !float.IsFinite(nominal))
            return 1f;
        float v = float.IsFinite(groundSpeedMps) ? Mathf.Abs(groundSpeedMps) : 0f;
        return v / nominal;
    }

    /// <summary>
    /// <b>The identity, stated as a function so it can be asserted rather than believed.</b> The
    /// ground speed a clip played at <paramref name="rate"/> actually expresses. For any speed,
    /// <c>GroundSpeedExpressed(gear, RateFor(gear, v)) == v</c> — which is the whole contract, and
    /// it is one line of test.
    /// </summary>
    public static float GroundSpeedExpressed(Gear gear, float rate) => NominalMpsFor(gear) * rate;

    /// <summary>
    /// <b>Cadence the warped clip produces, steps per second.</b> The authored cadence scaled by the
    /// rate. Exposed because <c>AvatarVisual</c> publishes <c>CadenceHz</c> as a readout other systems
    /// consume, and a clip-driven gait must keep writing a cadence that means what the procedural one
    /// meant. (<c>Run-BodyLanguageTest</c> asserted it until ANIM-M2b deleted that family, ruling 9.)
    /// </summary>
    public static float CadenceHzFor(Gear gear, float groundSpeedMps)
    {
        float nominal = NominalMpsFor(gear);
        if (nominal <= 0f)
            return 0f;
        // Two steps per cycle. cycle = 2 × reach ÷ (duty × nominal) at rate 1, so cadence scales with
        // the rate exactly.
        float authoredCadence = gear == Gear.Walk ? 2.5f : 3.75f;
        return authoredCadence * RateFor(gear, groundSpeedMps);
    }
}
