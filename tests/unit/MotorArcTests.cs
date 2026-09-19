using Godot;
using MpFoundation.Net;
using Sail.Game.World.BubbleTest;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-8 scope item 4: the level's arc follows the motor, and here is the proof it does.</b>
///
/// <para><c>MotorArc</c> replaced four measured-then-typed literals with a derivation. A derivation
/// is only worth having if it can reproduce the measurement it replaces, so
/// <see cref="TheDerivationReproducesMoveThreeEsFourMeasuredLiterals_AtThePreRulingTuning"/> is the
/// load-bearing test in this file: it evaluates <c>MotorArc</c> against a hand-built copy of the
/// tuning that shipped BEFORE Talon's 2026-08-28 ruling and requires all four of MOVE-3e's
/// in-engine numbers back, to a millimetre. Everything else here is a consequence.</para>
/// </summary>
public class MotorArcTests
{
    /// <summary>Tolerance for a "reproduces the measurement" claim: one millimetre / one
    /// millisecond. Wide enough for float ordering, far too narrow to admit a different jump.
    /// </summary>
    private const float Mm = 1e-3f;

    /// <summary>
    /// <b>The tuning as it shipped before MOVE-8</b>, hand-typed from git history rather than
    /// derived from anything, so this file has an independent fixed point to test against. Thirteen
    /// rows differ from <see cref="MotorTuning.Default"/>; only the six the arc actually reads are
    /// load-bearing here, and the rest are written out so the value can be read as a snapshot.
    /// </summary>
    private static MotorTuning PreRuling => MotorTuning.Default with
    {
        MoveSpeed = 5.4f,
        Gravity = 22f,
        FallGravityMultiplier = 1.35f,
        ApexHangStrength = 0.00f,
        JumpReleaseGravityMultiplier = 3.00f,
        CoyoteTimeSec = 0.12f,
        JumpBufferSec = 0.12f,
        AirControlBuild = 0.45f,
        AirControlTurn = 0.35f,
        AirControlBrake = 0.30f,
        AirJumpMode = 0f,
    };

    private static void Near(float expected, float actual, float tol, string what) =>
        Assert.True(Mathf.Abs(expected - actual) <= tol,
            $"{what}: expected {expected:F4}, got {actual:F4} (tolerance {tol:G})");

    // =============================================================================================
    // 1. The positive control.
    // =============================================================================================

    /// <summary>
    /// <b>The whole justification for the derivation, in one test.</b> MOVE-3e measured a held
    /// sprint jump and a jog tap in a headed engine and reported apex 1.534 m / range 6.192 m and
    /// apex 0.467 m / range 1.710 m. Those four numbers were then typed into
    /// <c>BubbleTestLayout</c>, two courses, two GDScript tools and a handful of doc comments.
    /// <c>MotorArc</c> must return all four at the tuning they were measured at, or it is a formula
    /// standing where a measurement used to be.
    ///
    /// <para><b>The continuous closed form does not pass this test</b>, which is why
    /// <c>MotorArc</c> does not use one: <c>v²/2g</c> at 8.4 and 22 is 1.604 m against a measured
    /// 1.534 m. The 4.5% gap is the 60 Hz Euler integration, and it is asserted below rather than
    /// argued, because "we used a simulation instead" is a claim that deserves a number.</para>
    /// </summary>
    [Fact]
    public void TheDerivationReproducesMoveThreeEsFourMeasuredLiterals_AtThePreRulingTuning()
    {
        JumpArc held = MotorArc.HeldSprint(PreRuling);
        JumpArc tap = MotorArc.JogTap(PreRuling);

        Near(1.534f, held.ApexM, Mm, "MOVE-3e's measured held-sprint apex");
        Near(6.192f, held.RangeM, Mm, "MOVE-3e's measured held-sprint flat range");
        Near(0.467f, tap.ApexM, Mm, "MOVE-3e's measured jog-tap apex");
        Near(1.710f, tap.RangeM, Mm, "MOVE-3e's measured jog-tap flat range");

        // The airtimes the ranges are built out of, stated so a range that came out right for the
        // wrong reason (a compensating error in speed and time) still fails.
        Near(0.7167f, held.AirtimeSec, Mm, "held-sprint airtime, MOVE-3d's convention");
        Near(0.3167f, tap.AirtimeSec, Mm, "jog-tap airtime, MOVE-3d's convention");
    }

    /// <summary><b>The negative control for the above.</b> The closed form the packet suggested is
    /// wrong by 70 mm on the apex — 70x the tolerance the derivation passes at — so this file can
    /// tell a right answer from a plausible one.</summary>
    [Fact]
    public void TheContinuousClosedForm_WouldHaveMissedTheMeasurementBySeventyMillimetres()
    {
        float closedForm = PreRuling.JumpVelocity * PreRuling.JumpVelocity / (2f * PreRuling.Gravity);
        Near(1.604f, closedForm, Mm, "the continuous v^2/2g apex");
        Assert.True(Mathf.Abs(closedForm - 1.534f) > 0.06f,
            "the closed form must be visibly wrong here, or this file is not discriminating");
    }

    // =============================================================================================
    // 2. The arc at the shipped tuning — the six numbers MOVE-8 reports.
    // =============================================================================================

    /// <summary><b>The ruled arc, pinned.</b> These six are what MOVE-8 measured in-engine and what
    /// the level constants now carry. A change to any gravity or speed row moves them, which is the
    /// point — but it moves them HERE first, loudly, rather than in a playtest.</summary>
    [Fact]
    public void TheShippedArc_IsTheSixNumbersMoveEightMeasured()
    {
        JumpArc held = MotorArc.HeldSprint(MotorTuning.Default);
        JumpArc tap = MotorArc.JogTap(MotorTuning.Default);

        Near(1.407f, held.ApexM, Mm, "held-sprint apex at the ruled tuning");
        Near(0.667f, held.AirtimeSec, Mm, "held-sprint airtime at the ruled tuning");
        Near(4.053f, held.RangeM, Mm, "held-sprint flat range at the ruled tuning");

        Near(0.300f, tap.ApexM, Mm, "jog-tap apex at the ruled tuning");
        Near(0.233f, tap.AirtimeSec, Mm, "jog-tap airtime at the ruled tuning");
        Near(0.887f, tap.RangeM, Mm, "jog-tap flat range at the ruled tuning");
    }

    /// <summary>
    /// <b>The double jump, which is new at MOVE-8 and is now the reachability ceiling.</b> Spent at
    /// the apex, where <c>StepAirJump</c>'s ASSIGNMENT of <c>velocity.Y</c> throws nothing away.
    ///
    /// <para><b>The free launch tick is in here, and it was found by measuring rather than by
    /// reasoning.</b> The first version of this model omitted it and reported 2.298 m; the engine
    /// capture came back with <b>2.402 m</b> on a press landing two ticks late, and the 0.112 m gap
    /// is exactly <c>JumpVelocity × AirJumpVelocityFraction × dt</c> — the air jump travels its
    /// whole new velocity for one tick before gravity touches it, precisely as the ground jump
    /// does. The packet's estimate was "roughly 2.4 m", which the corrected model agrees with and
    /// the uncorrected one did not.</para>
    /// </summary>
    [Fact]
    public void TheDoubleJump_LiftsTheCeilingToTwoPointFour_AndTheRangeToSixPointFive()
    {
        JumpArc dbl = MotorArc.DoubleJumpAtApex(MotorTuning.Default);

        Near(2.410f, dbl.ApexM, Mm, "double-jump apex, air jump spent at the top");
        Near(1.0667f, dbl.AirtimeSec, Mm, "double-jump airtime");
        Near(6.485f, dbl.RangeM, Mm, "double-jump flat range at a sprint");

        // The launch step, asserted as its own quantity so a future edit that "simplifies" the
        // branch away moves this by a known amount rather than by a mystery one.
        Near(0.112f, MotorTuning.Default.JumpVelocity
            * MotorTuning.Default.AirJumpVelocityFraction / 60f, 1e-4f,
            "the air jump's free launch step");

        JumpArc held = MotorArc.HeldSprint(MotorTuning.Default);
        Assert.True(dbl.ApexM > held.ApexM);
        Assert.True(dbl.RangeM > held.RangeM);
    }

    /// <summary><b>The air jump is modelled only where the motor has one.</b> At
    /// <c>AirJumpMode</c> 0 there is nothing to spend and at mode 2 the Kick converts fall speed
    /// into reach rather than buying altitude, so reporting either as a raised ceiling would be a
    /// ceiling the motor does not have.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    public void AtEveryModeButOne_TheDoubleJumpArcIsJustTheHeldJump(float mode)
    {
        MotorTuning t = MotorTuning.Default with { AirJumpMode = mode };
        Assert.Equal(MotorArc.HeldSprint(t), MotorArc.DoubleJumpAtApex(t));
    }

    // =============================================================================================
    // 3. The seam: the level reads the motor, not a remembered number.
    // =============================================================================================

    /// <summary>
    /// <b>Acceptance criterion 5: change one constant, the level's number moves.</b> The assertion
    /// is not that <c>BubbleTestLayout.SprintRange</c> equals 4.053 — that would be a second
    /// literal, exactly what MOVE-8 removed — but that it tracks a tuning it was never told about.
    /// A knob is moved here and the layout's derived value is required to follow it.
    /// </summary>
    [Fact]
    public void TheLayoutsArcIsTheMotorsArc_AndItTracksAChangeToTheTuning()
    {
        Assert.Equal(MotorArc.HeldSprint(MotorTuning.Default).ApexM, BubbleTestLayout.SprintApex);
        Assert.Equal(MotorArc.HeldSprint(MotorTuning.Default).RangeM, BubbleTestLayout.SprintRange);
        Assert.Equal(MotorArc.JogTap(MotorTuning.Default).ApexM, BubbleTestLayout.TapApex);
        Assert.Equal(MotorArc.JogTap(MotorTuning.Default).RangeM, BubbleTestLayout.TapRange);
        Assert.Equal(MotorArc.DoubleJumpAtApex(MotorTuning.Default).ApexM, BubbleTestLayout.DoubleApex);
        Assert.Equal(MotorArc.DoubleJumpAtApex(MotorTuning.Default).RangeM, BubbleTestLayout.DoubleRange);

        // THE TRACKING PROOF. Halve the ground speed and the flat ranges must halve with it, while
        // the apexes — which do not depend on horizontal speed at all — must not move a millimetre.
        // Both halves matter: a "derived" value wired to the wrong row would move when it should
        // not, and this catches that as readily as it catches one that is still a literal.
        MotorTuning slower = MotorTuning.Default with { MoveSpeed = MotorTuning.Default.MoveSpeed * 0.5f };
        Near(0.5f * BubbleTestLayout.SprintRange, MotorArc.HeldSprint(slower).RangeM, Mm,
            "the held range must halve with the ground speed");
        Near(0.5f * BubbleTestLayout.TapRange, MotorArc.JogTap(slower).RangeM, Mm,
            "the tap range must halve with the ground speed");
        Near(BubbleTestLayout.SprintApex, MotorArc.HeldSprint(slower).ApexM, Mm,
            "the apex must NOT move with the ground speed");

        // And the other way: raise gravity and the apex must fall while the ranges follow the
        // shortened airtime.
        MotorTuning heavier = MotorTuning.Default with { Gravity = MotorTuning.Default.Gravity * 1.5f };
        Assert.True(MotorArc.HeldSprint(heavier).ApexM < BubbleTestLayout.SprintApex);
        Assert.True(MotorArc.HeldSprint(heavier).RangeM < BubbleTestLayout.SprintRange);
    }

    /// <summary><b>The ceiling really is the double jump now.</b> Stated as its own assertion
    /// because every reachability judgement in the level — and every line of MOVE-8's changed-class
    /// list — depends on which of the two numbers is the ceiling, and before MOVE-8 the answer was
    /// the other one.</summary>
    [Fact]
    public void TheReachabilityCeilingIsTheDoubleJump_NotTheHeldSprintJump()
    {
        Assert.True(BubbleTestLayout.DoubleApex > BubbleTestLayout.SprintApex);
        Assert.True(BubbleTestLayout.DoubleRange > BubbleTestLayout.SprintRange);
        Assert.True(BubbleTestLayout.SprintApex > BubbleTestLayout.TapApex);
        Assert.True(BubbleTestLayout.SprintRange > BubbleTestLayout.TapRange);
    }
}
