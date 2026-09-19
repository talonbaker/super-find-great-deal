using System.Linq;
using System.Reflection;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The guard that replaces the one the compiler used to give for free</b> — MOVE-4a §8's
/// default-identity contract, MOVE-4b scope item 6.
///
/// <para>Until MOVE-4b, <c>AvatarMotor</c>'s feel numbers were <c>public const</c> and <c>const</c>
/// propagates: half a dozen downstream files declared their own constants <i>from</i> them, so an
/// accidental edit to a movement number was caught by the compiler and by every arithmetic
/// assertion built on it. Converting them to properties reading <see cref="MotorTuning.Current"/>
/// is what makes a live slider possible, and it is also what takes that guard away. <b>This file is
/// the replacement:</b> every one of the thirty-one defaults, asserted against the literal the spec
/// documents, so an accidental default change is still loud.</para>
///
/// <para><b>Read it as a list, not as code.</b> Each line below is a transcription. If one of them
/// disagrees with the constant it mirrors, the disagreement is the bug — do not "fix" the test to
/// match a number somebody moved.</para>
///
/// <para><b>MOVE-8 moved thirteen of them, and this is what that costs.</b> Talon ruled the tuning
/// at the keyboard on 2026-08-28 (<c>MovementPresets.RollBase</c>), so thirteen literals below are
/// re-pinned to the ruled values and every one carries a <c>// MOVE-8</c> marker. <b>That is the
/// ONLY circumstance in which a line here moves</b> — a named ruling, landed in the same session,
/// with the arc re-measured. A red in this file with no such ruling behind it is still the bug,
/// and the paragraph above still governs it.</para>
///
/// <para><b>This suite never leaves <see cref="MotorTuning.Current"/> at a non-default value</b>, so
/// nothing here can perturb the movement tests running beside it. The one test that does exercise
/// the writer moves <c>CameraDipStrengthM</c>, which no code and no other test reads.</para>
/// </summary>
public class MotorTuningDefaultIdentityTests
{
    private static readonly MotorTuning D = MotorTuning.Default;

    /// <summary><b>AC-D1.</b> Thirty-one settable fields, one per knob-table row — no more, no
    /// fewer. A field with no row is a knob no panel will ever show and no print will ever emit;
    /// MOVE-4f's <c>CameraDipRampPower</c> is the thirty-first, and this assertion is what
    /// stopped it being added in one place and not the other.
    ///
    /// <para><b>58 → 59 (FP-1, 2026-09-19), with a reason.</b> <c>BodyYawFollowsAim</c> is the
    /// fifty-ninth: the first-person facing mode, added as a row on the existing seam rather than
    /// as a branch inside <c>AvatarMotor.Step</c>. The count moved because a row was deliberately
    /// added, which is the only circumstance in which this number is allowed to move.</para></summary>
    [Fact]
    public void ThereAreExactlyFiftyNineFields_OnePerKnobTableRow()
    {
        PropertyInfo[] fields = typeof(MotorTuning)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

        Assert.Equal(59, fields.Length);
        Assert.Equal(59, MotorTuningKnobs.All.Count);
        Assert.Equal(
            MotorTuningKnobs.All.Select(k => k.Name).OrderBy(n => n, System.StringComparer.Ordinal),
            fields.Select(p => p.Name).OrderBy(n => n, System.StringComparer.Ordinal));
    }

    /// <summary><b>AC-D2 and AC-D3, the whole contract, one assertion per row.</b> Twenty-six are
    /// straight transcriptions from <c>AvatarMotor.cs</c> and <c>AvatarVisual.cs</c>; the five
    /// exceptions are the reshaped skid fraction and the four camera-dip terms that have no
    /// constant to transcribe, all of them stated in §8 (and MOVE-4f scope item 4 for the fourth),
    /// and none of them a behaviour change.</summary>
    [Fact]
    public void EveryDefault_IsTheLiteralTheSpecDocuments()
    {
        // Ground
        Assert.Equal(3.8f, D.MoveSpeed);      // MOVE-8: Talon's speed ruling
        Assert.Equal(1.6f, D.SprintMultiplier);
        Assert.Equal(9f, D.Acceleration);
        Assert.Equal(21f, D.Deceleration);
        Assert.Equal(34f, D.TurnAcceleration);
        Assert.Equal(12f, D.TurnLerp);
        Assert.Equal(22f, D.AimTurnLerp);
        // FP-1: no constant to transcribe — the row was born as a mode, and 1 is "the body faces
        // the look", which is what a first-person game means by facing.
        Assert.Equal(1f, D.BodyYawFollowsAim);

        // Gravity
        Assert.Equal(24f, D.Gravity);         // MOVE-8: FORGIVING
        Assert.Equal(1.50f, D.FallGravityMultiplier);   // MOVE-8: FORGIVING
        Assert.Equal(0.30f, D.ApexHangStrength);      // MOVE-8: LEFT the no-op, on the ruling
        Assert.Equal(2.00f, D.ApexHangWindowMps);

        // Jump
        Assert.Equal(8.4f, D.JumpVelocity);
        Assert.Equal(4.00f, D.JumpReleaseGravityMultiplier);   // MOVE-8
        Assert.Equal(0.30f, D.CoyoteTimeSec);   // MOVE-8: both forgiveness timers at their
        Assert.Equal(0.30f, D.JumpBufferSec);   // generous end, 18 ticks each

        // Air
        Assert.Equal(0.70f, D.AirControlBuild);   // MOVE-8: real air authority. These three
        Assert.Equal(0.60f, D.AirControlTurn);    // leave MOVE-3's 30-60% window deliberately;
        Assert.Equal(0.55f, D.AirControlBrake);   // see AirborneControlTests for the consequence.

        // Skid
        Assert.Equal(0.75f, D.SkidEnterSpeedFraction);   // AC-D3: the FRACTION, not the product
        Assert.Equal(-0.50f, D.SkidAlignmentMax);
        Assert.Equal(13f, D.SkidDeceleration);
        Assert.Equal(1.20f, D.SkidExitSpeedMps);
        Assert.Equal(0.75f, D.SkidMaxSec);

        // Landing
        Assert.Equal(2.5f, D.LandMinFallMps);
        Assert.Equal(14f, D.LandFullFallMps);
        Assert.Equal(0.16f, D.TakeoffKickSec);
        Assert.Equal(0.40f, D.TakeoffKickMinDriveFraction);

        // Camera
        Assert.Equal(0.00f, D.CameraDipStrengthM);       // AC-D3: the exact behavioural no-op
        Assert.Equal(0.05f, D.CameraDipAttackSec);
        Assert.Equal(0.26f, D.CameraDipRecoverSec);
        // MOVE-4f scope item 4. The ramp ships at 6 and the STRENGTH still ships at 0.00, so the
        // dip is an exact no-op at every fall speed — the ramp changes nothing until Talon turns
        // the strength up, which is the whole wave's promise.
        Assert.Equal(6f, D.CameraDipRampPower);
    }

    /// <summary><b>AC-D3, the reshaped row's identity, and MOVE-8's vindication of it.</b>
    /// <c>SkidEnterSpeedFraction</c> is not a transcription of <c>SkidEnterSpeedMps</c> — it is the
    /// fraction, and MOVE-4b's argument for reshaping it was that a fraction follows the speed
    /// while a product strands. MOVE-8 is the first time that mattered: <c>0.75 × 5.4 = 4.05</c>
    /// became <c>0.75 × 3.8 = 2.85</c> with nothing to edit, and the gear ladder kept its shape.
    /// (<c>KickMinSpeedMps</c>, the one row that was left as the PRODUCT, is the counterexample —
    /// it is still 4.05 and it no longer means anything. It is inert at the shipped
    /// <c>AirJumpMode</c>.)</summary>
    [Fact]
    public void TheReshapedSkidRow_DerivesTheExactSpeedTheConstantHad()
    {
        Assert.Equal(2.85f, D.SkidEnterSpeedMps, 1e-5f);   // MOVE-8: 0.75 x 3.8, the fraction did its job
        Assert.Equal(AvatarMotor.SkidEnterSpeedMps, D.SkidEnterSpeedMps);
    }

    /// <summary>The knob table and the tuning must not be able to disagree about what "shipped"
    /// means: the ranges, the clamps, the print's <c>// was</c> comments and the sparse save format
    /// all read <c>MotorKnob.Default</c>, while the motor reads the struct.</summary>
    [Fact]
    public void TheKnobTablesDefault_IsTheTuningsDefault_ForEveryRow()
    {
        foreach (MotorKnob knob in MotorTuningKnobs.All)
            Assert.Equal(knob.Default, knob.Get(D));
    }

    /// <summary>Every default sits inside its own knob range. A shipped value outside the slider
    /// that is meant to explore it would be clamped away the first time anything validated.</summary>
    [Fact]
    public void EveryDefault_IsInsideItsOwnRange()
    {
        foreach (MotorKnob knob in MotorTuningKnobs.All)
            Assert.InRange(knob.Default, knob.Min, knob.Max);
    }

    /// <summary><b>The default is a fixed point of validation.</b> If validating the shipped tuning
    /// changed anything, the shipped game would already be out of bounds.</summary>
    [Fact]
    public void ValidatingTheDefault_ChangesNothing_AndWarnsAboutNothing()
    {
        MotorTuning validated = MotorTuning.Validate(D, out var warnings);
        Assert.Equal(D, validated);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// <b>The motor's own reading of the seam.</b> Every <c>AvatarMotor</c> property now resolves
    /// through <see cref="MotorTuning.Current"/>; at the shipped tuning it must produce exactly the
    /// literal it produced before MOVE-4b. This is the assertion that would catch a property wired
    /// to the wrong field — a typo the compiler cannot see because every field is a <c>float</c>.
    /// </summary>
    [Fact]
    public void EveryMotorProperty_ReadsTheDefault_AtTheShippedTuning()
    {
        Assert.Equal(3.8f, AvatarMotor.MoveSpeed);
        Assert.Equal(1.6f, AvatarMotor.SprintMultiplier);
        Assert.Equal(9f, AvatarMotor.Acceleration);
        Assert.Equal(21f, AvatarMotor.Deceleration);
        Assert.Equal(34f, AvatarMotor.TurnAcceleration);
        Assert.Equal(12f, AvatarMotor.TurnLerp);
        Assert.Equal(22f, AvatarMotor.AimTurnLerp);
        Assert.Equal(24f, AvatarMotor.Gravity);
        Assert.Equal(1.50f, AvatarMotor.FallGravityMultiplier);
        Assert.Equal(8.4f, AvatarMotor.JumpVelocity);
        Assert.Equal(4.00f, AvatarMotor.JumpReleaseGravityMultiplier);
        Assert.Equal(0.30f, AvatarMotor.CoyoteTimeSec);
        Assert.Equal(0.30f, AvatarMotor.JumpBufferSec);
        Assert.Equal(0.70f, AvatarMotor.AirControlBuild);
        Assert.Equal(0.60f, AvatarMotor.AirControlTurn);
        Assert.Equal(0.55f, AvatarMotor.AirControlBrake);
        Assert.Equal(2.85f, AvatarMotor.SkidEnterSpeedMps, 1e-5f);
        Assert.Equal(-0.50f, AvatarMotor.SkidAlignmentMax);
        Assert.Equal(13f, AvatarMotor.SkidDeceleration);
        Assert.Equal(1.20f, AvatarMotor.SkidExitSpeedMps);
        Assert.Equal(0.75f, AvatarMotor.SkidMaxSec);
        Assert.Equal(3.8f, AvatarMotor.AirWishSpeedFloorMps);

        // MOVE-4d: the four AvatarVisual landing/takeoff rows. MOVE-4b carried fields for them and
        // left the constants alone, so their sliders moved nothing while the printed block reported
        // that they had — a knob that appears to work, which is worse than an absent one. They are
        // properties on the same seam now, with the same identity obligation as the twenty-one.
        Assert.Equal(2.5f, AvatarVisual.LandMinFallMps);
        Assert.Equal(14f, AvatarVisual.LandFullFallMps);
        Assert.Equal(0.16f, AvatarVisual.TakeoffKickSec);
        Assert.Equal(0.40f, AvatarVisual.TakeoffKickMinDriveFraction);
    }

    /// <summary>
    /// <b>The downstream derivations track the tuning — and MOVE-8 is the proof they do.</b>
    /// Spec §9.2's widening was chosen deliberately: <c>LocomotionProfile</c>'s gears,
    /// <c>ToolStance</c>'s run hysteresis and <c>EelProfile</c>'s flight arithmetic read the
    /// sliders in real time, and the price is that nothing downstream is compiler-checked any more.
    /// <b>Talon's speed ruling moved all five gear numbers at once and not one line of
    /// <c>LocomotionProfile</c> or <c>ToolStanceState</c> had to be edited</b> — walk 2.43 → 1.71,
    /// jog 5.40 → 3.80, sprint 8.64 → 6.08, run enter/exit with them. The two <c>EelProfile</c>
    /// rows are unmoved because <c>JumpVelocity</c> and <c>Deceleration</c> are unmoved.
    /// </summary>
    [Fact]
    public void TheDownstreamDerivations_TrackTheTuning_AndLandOnTheRuledNumbers()
    {
        Assert.Equal(1.71f, LocomotionProfile.WalkSpeedMps, 1e-5f);
        Assert.Equal(3.80f, LocomotionProfile.JogSpeedMps, 1e-5f);
        Assert.Equal(6.08f, LocomotionProfile.SprintSpeedMps, 1e-5f);
    }
}
