using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-3's airborne layer, asserted rather than described</b> —
/// <c>docs/design/2026-08-26-airborne-control-and-jump-shape.md</c>.
///
/// <para>Three things were added inside <c>AvatarMotor.Step</c>: airborne acceleration runs at a
/// fraction of ground acceleration, an airborne body's wish speed is capped by the speed it
/// already has, and releasing the jump key while still rising triples gravity. Every number below
/// is read off a named constant in <c>AvatarMotor</c>; not one is a literal.</para>
///
/// <para><b>The load-bearing test is
/// <see cref="AirborneSprint_WithInputReleased_CannotBeStoppedInsideTheLongestJump"/>.</b> It is
/// the one that catches a regression back to the shipped defect — <c>RateFor</c>'s output applied
/// identically in the air and on the ground, so a sprint could be braked to a dead stop in 0.41 s,
/// comfortably inside a 0.710 s jump. It was verified to go red by deleting the airborne branch and
/// re-running, then restored.</para>
///
/// <para><b>Why the vertical tests simulate rather than call <c>Step</c>.</b> <c>Step</c> needs a
/// live <c>CharacterBody3D</c> (MoveAndSlide is how Godot resolves collision), so it cannot run in
/// this tier at all. Every rule <c>Step</c> makes a decision with has therefore been split out as a
/// pure function — <see cref="AvatarMotor.RateFor"/>, <see cref="AvatarMotor.GravityFor"/>,
/// <see cref="AvatarMotor.StepJump"/>, <see cref="AvatarMotor.AirborneWishSpeed"/> — and the
/// integrations below drive <b>those shipped functions</b> over a flat, collision-free world. What
/// is re-stated here is only the Euler integration (<c>v -= g·dt; p += v·dt</c>) and the two
/// <c>MoveToward</c> calls, which is exactly what <c>MoveAndSlide</c> reduces to on flat
/// ground.</para>
/// </summary>
public class AirborneControlTests
{
    private const float Dt = AvatarMotor.TickDelta;

    /// <summary>The longest airtime a SINGLE jump can have (spec §3.3): a full-hold jump at 60 Hz.
    /// Every "cannot be done in the air" claim is measured against this window and no other.
    ///
    /// <para><b>MOVE-8: 0.710 → 0.683 s.</b> Talon's ruling raised <c>Gravity</c> to 24 and
    /// <c>FallGravityMultiplier</c> to 1.50 while adding a 0.30 apex hang, and the three together
    /// net out 0.027 s shorter. <c>MotorTuningInvariants.LongestAirtimeSec</c> is the hand-typed
    /// mirror of this number and moved with it.</para>
    ///
    /// <para><b>It is no longer the longest FLIGHT, and that distinction is new at MOVE-8.</b>
    /// <c>AirJumpMode</c> now ships at 1, so a player who spends the air jump at the apex is
    /// airborne for <b>1.067 s</b> — 56% longer than this. Every claim below is therefore scoped to
    /// "inside one jump", and the double jump's window is the reason three of them no longer hold
    /// at all: see the MOVE-8 fork block in section 2.</para></summary>
    private const float LongestAirtimeSec = 0.683f;

    /// <summary>Airtime of a held sprint jump with the air jump spent at the apex — the real
    /// longest flight since MOVE-8. Derived rather than typed, from the same simulation the level
    /// constants use.</summary>
    private static float LongestFlightSec => MotorArc.DoubleJumpAtApex(MotorTuning.Default).AirtimeSec;

    private static void Near(float expected, float actual, float eps, string what) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"{what}: expected {expected:F4}, got {actual:F4} (tolerance {eps:G})");

    /// <summary>Ticks that fit inside a window, rounded up so a claim about "the whole flight" is
    /// never accidentally short.</summary>
    private static int TicksIn(float sec) => Mathf.CeilToInt(sec / Dt);

    // =============================================================================================
    // 1. The three fractions are real fractions, inside Talon's window.
    // =============================================================================================

    /// <summary>
    /// <b>The three fractions, inside the window Talon last ruled, and ordered.</b>
    ///
    /// <para><b>MOVE-8 moved the window, and that is the whole content of this edit.</b> MOVE-3's
    /// ask was "30%-60% of ground control" and it was Talon's, in 2026-08-26's words. MOVE-7 is
    /// Talon again, at the keyboard, against a control, ruling FORGIVING — <i>"my favourite so far
    /// in every regard"</i> — which carries 0.70 / 0.60 / 0.55. A later ruling by the same person
    /// supersedes an earlier one; the window is re-pinned to <b>[0.55, 0.70]</b> so this test still
    /// catches a drift, and the ORDERING assertion below is untouched because it is the design
    /// claim rather than the taste one, and it still holds.</para>
    ///
    /// <para><b>What the wider window costs is NOT hidden here</b> — it is asserted three tests
    /// down, in the MOVE-8 fork block: at 0.55 the air brake can stop a sprint jump dead in
    /// mid-air, which is the exact defect MOVE-3 was written to fix.</para>
    /// </summary>
    [Fact]
    public void ThreeAirFractions_AreInsideTheDesignWindow_AndOrdered()
    {
        foreach (float f in new[]
                 { AvatarMotor.AirControlBrake, AvatarMotor.AirControlTurn, AvatarMotor.AirControlBuild })
            Assert.InRange(f, 0.55f, 0.70f);

        Assert.True(AvatarMotor.AirControlBrake < AvatarMotor.AirControlTurn,
            "brake must have the LEAST authority of the three — stopping in mid-air is the defect");
        Assert.True(AvatarMotor.AirControlTurn < AvatarMotor.AirControlBuild,
            "redirect must not out-rank build; see spec §2.1");
    }

    /// <summary>
    /// <b>Air control is inside the spec's fraction of ground control, measured as the actual
    /// velocity delta over one tick — grounded vs airborne, identical intent.</b> Not the constants
    /// compared to each other: the number a player feels is how far the velocity actually moved.
    /// </summary>
    [Theory]
    // wish, velocity, the fraction that branch should show
    // MOVE-8: the speeds are the ruled gears (jog 3.8, sprint 6.08) and the fractions are the
    // ruled air rows. Only the numbers moved — every row still tests the branch it always did.
    [InlineData(0f, 0f, 6.08f, 0f, 0.55f)]      // no input at sprint      -> brake
    [InlineData(-3.8f, 0f, 0f, 0f, 0.70f)]      // standing start          -> build
    [InlineData(-3.8f, 0f, -3.0f, 0f, 0.70f)]   // aligned, below wish     -> build
    [InlineData(3.8f, 0f, -3.8f, 0f, 0.60f)]    // fully opposed at jog    -> turn
    [InlineData(0f, -3.8f, -3.8f, 0f, 0.60f)]   // 90 degrees at jog       -> turn
    public void AirControl_IsTheSpecFractionOfGroundControl_MeasuredAsAOneTickVelocityDelta(
        float wishX, float wishZ, float velX, float velZ, float expectedFraction)
    {
        var wish = new Vector3(wishX, 0f, wishZ);
        var velocity = new Vector3(velX, 0f, velZ);

        float groundDelta = OneTickDelta(velocity, wish, grounded: true);
        float airDelta = OneTickDelta(velocity, wish, grounded: false);

        Assert.True(groundDelta > 1e-4f, "the ground case must actually move, or the ratio is noise");
        float measured = airDelta / groundDelta;
        Near(expectedFraction, measured, 1e-3f, "measured air/ground velocity delta over one tick");
        // The window's own edges, with a float epsilon: the products do not round exactly, so the
        // measured brake ratio comes back a few ULPs off and a bare InRange would fail on the
        // arithmetic rather than on the design. MOVE-8 moved the window with the ruling.
        Assert.InRange(measured, 0.55f - 1e-4f, 0.70f + 1e-4f);
    }

    /// <summary>How far the horizontal velocity actually moves in one tick, the exact pair of
    /// <c>MoveToward</c> calls <c>Step</c> makes.</summary>
    private static float OneTickDelta(Vector3 velocity, Vector3 wish, bool grounded)
    {
        float rate = AvatarMotor.RateFor(velocity, wish, grounded);
        var next = new Vector3(
            Mathf.MoveToward(velocity.X, wish.X, rate * Dt), 0f,
            Mathf.MoveToward(velocity.Z, wish.Z, rate * Dt));
        return (next - velocity).Length();
    }

    /// <summary><c>grounded</c> defaults to true, so every pre-existing call site and every
    /// pre-existing test is byte-identical (spec §2.2). Stated as an assertion because "additive"
    /// is a claim, not a comment.</summary>
    [Fact]
    public void RateFor_DefaultsToGrounded_SoEveryPreExistingCallSiteIsUnchanged()
    {
        var v = new Vector3(0f, 0f, -5.4f);
        var wish = new Vector3(0f, 0f, -5.4f);
        Assert.Equal(AvatarMotor.RateFor(v, wish, grounded: true), AvatarMotor.RateFor(v, wish));
        Assert.Equal(AvatarMotor.Deceleration, AvatarMotor.RateFor(v, Vector3.Zero));
    }

    // =============================================================================================
    // 2. THE ANTI-DRIFT ASSERTIONS. These are the ones that catch a regression to today's body.
    // =============================================================================================

    // -----------------------------------------------------------------------------------------
    // MOVE-8 FORK -- the three tests below WERE the anti-drift block, and the ruling broke all
    // three at once. THIS IS NOT A WEAKENING; it is the same three measurements, re-stated as the
    // facts they now report, so the loss is ASSERTED rather than only written down in a report.
    //
    // WHAT BROKE, IN ONE LINE. MOVE-3 spec §2.4's promise -- "a committed run cannot be cancelled
    // in mid-air", the whole reason the airborne branch exists -- was bought with an air brake of
    // 21 x 0.30 = 6.30 m/s^2 against a sprint of 8.64 m/s: 1.371 s to stop, and no jump lasted
    // 0.710 s. Talon's FORGIVING ruling takes the brake to 21 x 0.55 = 11.55 m/s^2 and his speed
    // ruling takes the sprint to 6.08 m/s, so a stop now costs 0.526 s -- INSIDE a 0.683 s jump,
    // and less than half the 1.067 s a double jump buys. Both halves of the ruling push the same
    // way, which is why neither could have surfaced it alone.
    //
    // WHY IT IS PINNED RATHER THAN FIXED. The numbers are Talon's, taken at the keyboard against
    // controls, and the repair is a design call between at least three options (drop
    // AirControlBrake toward 0.35, restore ground speed, or accept that a jump is steerable now)
    // that only he can make. MOVE-8's scope is to LAND the ruling and report what it costs.
    //
    // WHAT THESE STILL CATCH. Each keeps its positive control and each pins a MEASURED number, so
    // an unrelated regression in RateFor, AirborneWishSpeed or the branch structure still reddens
    // them -- and if Talon rules the brake back down, all three go red and get restored, which is
    // exactly the signal that the fork has closed.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>MOVE-8 FORK: a released sprint jump now stops dead in mid-air.</b> Spec §2.4's
    /// momentum-keep promise, measured at the ruled tuning and found spent. See the fork block
    /// above for what broke and why it is not repaired here.
    /// </summary>
    [Fact]
    public void MOVE8_AReleasedSprintJump_NowStopsInsideOneJump_AndSpecTwoFourIsSpent()
    {
        float sprint = AvatarMotor.MoveSpeed * AvatarMotor.SprintMultiplier;
        float brake = AvatarMotor.Deceleration * AvatarMotor.AirControlBrake;
        float stopSec = sprint / brake;

        // The measurement, to three decimals, so a later drift in EITHER factor moves it.
        Near(11.55f, brake, 1e-3f, "the airborne brake rate the ruling produces");
        Near(0.526f, stopSec, 1e-3f, "seconds to cancel a sprint in mid-air");
        Assert.True(stopSec < LongestAirtimeSec,
            "if this is false the fork has CLOSED: restore the >1 m/s assertion this test replaced");

        float airSpeed = SimulateHorizontal(sprint, Vector3.Zero, grounded: false, LongestAirtimeSec);
        Assert.Equal(0f, airSpeed);

        // POSITIVE CONTROL, unchanged and still load-bearing: the ground case must also reach zero,
        // or the simulation is not measuring braking at all.
        float groundSpeed = SimulateHorizontal(sprint, Vector3.Zero, grounded: true, LongestAirtimeSec);
        Assert.Equal(0f, groundSpeed);
    }

    /// <summary>
    /// <b>MOVE-8 FORK: a released jog jump stops too</b>, and it is the case a player meets
    /// constantly -- the default gear. It needed 0.857 s at the pre-ruling tuning and needs
    /// 0.329 s now. See the fork block above.
    /// </summary>
    [Fact]
    public void MOVE8_AReleasedJogJump_NowStopsInsideOneJump()
    {
        float jog = AvatarMotor.MoveSpeed;
        float stopSec = jog / (AvatarMotor.Deceleration * AvatarMotor.AirControlBrake);
        Near(0.329f, stopSec, 1e-3f, "seconds to cancel a jog in mid-air");

        float atStop = SimulateHorizontal(jog, Vector3.Zero, grounded: false, LongestAirtimeSec);
        Assert.Equal(0f, atStop);

        // The half of the old claim that SURVIVES, and it is worth keeping: the stop is not
        // instant. A tenth of a second in, a released jog jump still carries most of its speed, so
        // the body still reads as committed over the opening of a flight.
        float early = SimulateHorizontal(jog, Vector3.Zero, grounded: false, 0.10f);
        Assert.True(early > 0.5f * jog,
            $"a released jog jump had already shed half its speed 0.10 s in ({early:F3} m/s) -- " +
            "that is not a brake any more, and the fork has got WORSE rather than staying where " +
            "MOVE-8 measured it");
    }

    /// <summary>
    /// <b>MOVE-8 FORK: a full 180 now fits inside one jump.</b> Reversing at a sprint needed
    /// 1.452 s of redirect against a 0.710 s jump before the ruling; it needs about 0.5 s now,
    /// against 0.683 s. This was the sharpest of the three anti-drift tests. See the fork block.
    /// </summary>
    [Fact]
    public void MOVE8_AFullMidAirReversal_NowFitsInsideOneJump()
    {
        float sprint = AvatarMotor.MoveSpeed * AvatarMotor.SprintMultiplier;
        // The stick is hard over the other way. The wish magnitude is what the ceiling allows: the
        // body already carries sprint speed, so it may ask for sprint speed backwards.
        float wishSpeed = AvatarMotor.AirborneWishSpeed(sprint, AvatarMotor.MoveSpeed,
            new Vector3(0f, 0f, -sprint));
        var opposed = new Vector3(0f, 0f, wishSpeed);

        // SIGNED, not a magnitude: a magnitude cannot tell a body that stopped from one that has
        // already reversed hard, and "reversed hard" is exactly what is being reported.
        float vz = SimulateHorizontalSigned(-sprint, opposed.Z, grounded: false, LongestAirtimeSec);
        Assert.True(vz > 0f,
            $"the reversal did NOT complete inside one jump (vz {vz:F3} m/s) -- the fork has " +
            "CLOSED, so restore the vz < 0 assertion this test replaced");

        // POSITIVE CONTROL: on the ground the same reversal blows through zero harder still, which
        // is what keeps this a comparison rather than a single reading.
        float ground = SimulateHorizontalSigned(-sprint, opposed.Z, grounded: true, LongestAirtimeSec);
        Assert.True(ground > vz, "the ground case must reverse at least as hard as the air case");
    }

    /// <summary>
    /// <b>A walk hop CAN be stopped in the air, deliberately</b> (spec §2.4). Stated as a test
    /// rather than left implicit, because "airborne braking is weak" read without this line looks
    /// like a bug when a hop in place refuses to stop.
    /// </summary>
    [Fact]
    public void AirborneWalkHop_CanStillBeStoppedInTheAir()
    {
        float walk = LocomotionProfile.WalkSpeedMps;
        float speed = SimulateHorizontal(walk, Vector3.Zero, grounded: false, LongestAirtimeSec);
        Assert.Equal(0f, speed);
    }

    /// <summary>The coherence spec §2.4 names as a rule in its own right: the most the air brake
    /// can shed over the longest flight sits just above <see cref="AvatarMotor.SkidEnterSpeedMps"/>,
    /// so <b>any run committed enough to skid on the ground is committed enough that it cannot be
    /// cancelled in the air.</b> One threshold, two systems.</summary>
    [Fact]
    public void MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold()
    {
        float shed = AvatarMotor.Deceleration * AvatarMotor.AirControlBrake * LongestAirtimeSec;
        // MOVE-8: 4.47 -> 7.89. The coherence claim this number used to carry -- "anything
        // committed enough to skid is committed enough that it cannot be cancelled in the air" --
        // is precisely what the fork block above records as spent: 7.89 m/s is now more than a
        // WHOLE SPRINT, not merely more than the skid entry. Kept as a live measurement.
        Near(7.89f, shed, 0.02f, "maximum speed the air brake can shed in one flight");
        Assert.True(shed > AvatarMotor.SkidEnterSpeedMps);
    }

    // =============================================================================================
    // 3. The wish-speed ceiling.
    // =============================================================================================

    [Theory]
    // requested, groundWish, carried speed, expected
    [InlineData(8.64f, 5.4f, 8.64f, 8.64f)]  // sprint takeoff, holding sprint: holds, cannot gain
    [InlineData(8.64f, 5.4f, 8.0f, 8.0f)]    // decayed to 8.0: ratchets down only
    [InlineData(8.64f, 5.4f, 5.4f, 5.4f)]    // jog takeoff, presses sprint: sprint does NOTHING
    [InlineData(5.4f, 5.4f, 0f, 5.4f)]       // standing jump: may still build to the ground wish
    [InlineData(5.4f, 2.16f, 0f, 2.16f)]     // wading (waterMul): a hop cannot exceed wading speed
    public void AirborneWishSpeed_CapsTheRequestAtWhatIsAlreadyCarried(
        float requested, float groundWish, float carried, float expected)
    {
        float got = AvatarMotor.AirborneWishSpeed(requested, groundWish, new Vector3(0f, 0f, -carried));
        Near(expected, got, 1e-4f, "airborne wish speed");
    }

    [Fact]
    public void AirborneWishSpeed_NonFiniteCarriedVelocity_FallsBackToTheRequest()
    {
        float got = AvatarMotor.AirborneWishSpeed(5.4f, 5.4f, new Vector3(float.NaN, 0f, 0f));
        Assert.Equal(5.4f, got);
    }

    // =============================================================================================
    // 4. Variable jump height.
    // =============================================================================================

    /// <summary><b>The gravity branch, including its boundary at exactly zero</b> (MECHANICS §1,
    /// spec §3.2). The cut is <c>velocityY &gt; 0</c> strictly, so the apex tick gets ordinary
    /// gravity.</summary>
    /// <summary>
    /// <b>The rows, as <c>MemberData</c> rather than <c>[InlineData]</c> — and the reason is the
    /// test's meaning, not the compiler</b> (MOVE-4b, spec §9.1).
    ///
    /// <para>MOVE-4b converted <c>AvatarMotor</c>'s feel constants to properties reading
    /// <c>MotorTuning.Current</c>, and an attribute argument must be a compile-time constant, so
    /// these five rows could no longer be attributes. There were two ways out: <b>hard-code the
    /// literals</b> (39.6, 22, 66) or <b>keep computing the expectation from the live
    /// constants</b>. They are not equivalent. This test's stated purpose is that
    /// <see cref="AvatarMotor.GravityFor"/> resolves its three branches <i>and</i> the boundary at
    /// exactly zero — a claim about the shape of the cut, true at any gravity. Hard-coded literals
    /// would quietly convert it into a snapshot of today's numbers, so it would start failing for
    /// a retune it was never meant to guard and stop failing for a broken branch at a tuned
    /// gravity. <c>MemberData</c> keeps the original meaning exactly: the expectation is still
    /// <c>Gravity × FallGravityMultiplier</c>, evaluated at whatever those are.</para>
    ///
    /// <para>The branch structure is spelled out here rather than delegated to a helper, so this is
    /// still an independent statement of the rule and not a tautology against the implementation.</para>
    /// </summary>
    /// <summary>
    /// <b>MOVE-8: the apex hang, written out rather than delegated.</b> Talon's ruling took
    /// <c>ApexHangStrength</c> off its exact no-op, so every branch above is now SCALED near the
    /// apex — and a row that just said <c>AvatarMotor.Gravity</c> stopped being true at
    /// <c>|v_y| &lt; 2 m/s</c>, which is where four of the five rows live.
    ///
    /// <para>The smoothstep is re-typed here instead of calling
    /// <see cref="AvatarMotor.ApexHangFactor"/>, for the same reason the branch structure below is
    /// re-typed instead of calling a helper: a test that computes its expectation with the function
    /// under test asserts nothing. This is a third authorship of the same shape — the motor's, the
    /// invariant mirror's, and this one — and <c>MotorTuningTests</c> is where the first two are
    /// swept against each other.</para>
    /// </summary>
    private static float Hang(float velocityY)
    {
        float s = AvatarMotor.ApexHangStrength;
        float window = AvatarMotor.ApexHangWindowMps;
        if (!(s > 0f) || !(window > 0f))
            return 1f;
        float u = Mathf.Min(1f, Mathf.Abs(velocityY) / window);
        return 1f - s * (1f - (u * u * (3f - 2f * u)));
    }

    public static TheoryData<float, bool, bool, float> GravityBranchRows() => new()
    {
        { -1f, false, false, AvatarMotor.Gravity * AvatarMotor.FallGravityMultiplier * Hang(-1f) },
        { -1f, true, false, AvatarMotor.Gravity * AvatarMotor.FallGravityMultiplier * Hang(-1f) },
        { 0f, false, false, AvatarMotor.Gravity * Hang(0f) },   // the apex tick: NOT cut
        { 1f, true, false, AvatarMotor.Gravity * Hang(1f) },    // rising, held
        { 1f, false, false, AvatarMotor.Gravity * AvatarMotor.JumpReleaseGravityMultiplier * Hang(1f) },
        // The branch structure is still the claim, so it is also asserted OUTSIDE the hang window,
        // where Hang is exactly 1 and the three cuts stand bare. Without these two rows the ruling
        // would have left every row above multiplied by the same factor and the test would no
        // longer be able to see a broken cut at speed.
        { -6f, false, false, AvatarMotor.Gravity * AvatarMotor.FallGravityMultiplier },
        { 6f, false, false, AvatarMotor.Gravity * AvatarMotor.JumpReleaseGravityMultiplier },
    };

    [Theory]
    [MemberData(nameof(GravityBranchRows))]
    public void GravityFor_ResolvesTheThreeBranchesAndTheExactlyZeroBoundary(
        float velocityY, bool jumpHeld, bool locked, float expected)
        => Near(expected, AvatarMotor.GravityFor(velocityY, jumpHeld, locked), 1e-4f,
            "gravity this tick");

    /// <summary>
    /// <b>A control-locked body's arc is bit-identical to today's</b> (spec §7). The lock already
    /// forces <c>JumpHeld</c> false, so without the explicit guard every control-locked rising body
    /// would silently get 3x gravity.
    /// </summary>
    [Fact]
    public void ControlLockedRisingBody_GetsOrdinaryGravity_NoMatterWhatTheHeldBitSays()
    {
        foreach (bool held in new[] { false, true })
            Near(AvatarMotor.Gravity,
                AvatarMotor.GravityFor(1f, held, locked: true), 1e-4f,
                "a locked rising body's gravity");
    }

    /// <summary>
    /// <b>The carriable range: min apex and max apex, simulated tick by tick</b> (spec §3.3).
    ///
    /// <para><b>On the tolerance.</b> The spec's figures are continuous-math and its own §3.3 is
    /// internally inconsistent about whether the one-tick launch offset (0.14 m) is inside the
    /// table value or outside it — its closed-form runs to 1.74 m where the table says 1.60 m. A
    /// discrete 60 Hz Euler integration lands between the two. 0.15 m is wide enough to cover both
    /// readings and far too narrow to admit a broken cut: the two ends of the range are 1 m
    /// apart.</para>
    /// </summary>
    [Fact]
    public void JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately()
    {
        (float apexHeld, float airtimeHeld) = SimulateJump(holdForTicks: int.MaxValue);
        (float apexTap, float airtimeTap) = SimulateJump(holdForTicks: 1);

        // MOVE-8: the four centres are re-pinned to the ruled arc. The TOLERANCES are unchanged,
        // deliberately — 0.15 m and 0.06 s are what makes this a bracket rather than a snapshot,
        // and narrowing them while re-centring would have quietly turned it into one.
        //   held  1.60 -> 1.55 m, 0.710 -> 0.683 s
        //   tap   0.67 -> 0.54 m, 0.357 -> 0.283 s
        // The held apex barely moved (gravity 22 -> 24 pushes down, the 0.30 apex hang pushes
        // back) while the tap collapsed, because the tap is cut by JumpReleaseGravityMultiplier
        // and that went 3.0 -> 4.0. That is the ruling's shape in two numbers: the tall jump is
        // the jump you had, and the short one got much shorter.
        Near(1.55f, apexHeld, 0.15f, "apex of a fully held jump");
        Near(0.54f, apexTap, 0.15f, "apex of a jump released on the first airborne tick");
        Near(0.683f, airtimeHeld, 0.06f, "airtime of a fully held jump");
        Near(0.283f, airtimeTap, 0.06f, "airtime of a minimum hop");

        Assert.True(apexHeld / apexTap >= 2.0f,
            $"the carriable range collapsed to {apexHeld / apexTap:F2}x — the jump is not variable");
    }

    /// <summary>
    /// <b>The range is continuous and monotonic in release time</b> (spec §3.3): every extra tick
    /// of hold buys height, and none loses it. This is what makes the middle of the range usable
    /// rather than a two-position switch.
    /// </summary>
    [Fact]
    public void JumpApex_IsMonotonicInHoldDuration()
    {
        float previous = -1f;
        for (int hold = 1; hold <= 30; hold++)
        {
            (float apex, _) = SimulateJump(hold);
            Assert.True(apex >= previous - 1e-4f,
                $"holding for {hold} ticks gave a LOWER apex ({apex:F4}) than {hold - 1} ({previous:F4})");
            previous = apex;
        }
    }

    /// <summary>
    /// <b>Mashing the key mid-ascent can never gain height</b> (spec §3.2, MECHANICS §2's flicker
    /// rule). The released branch is always the harsher one, so the apex is bounded at the
    /// held-throughout value no matter what the input does — which is why the cut needed no latch
    /// and no state to enter or leave.
    /// </summary>
    [Fact]
    public void MashingTheJumpKeyMidAscent_NeverExceedsTheHeldThroughoutApex()
    {
        (float ceiling, _) = SimulateJump(holdForTicks: int.MaxValue);
        for (int period = 1; period <= 6; period++)
        {
            (float apex, _) = SimulateJump(holdForTicks: int.MaxValue, mashPeriodTicks: period);
            Assert.True(apex <= ceiling + 1e-4f,
                $"mashing every {period} ticks reached {apex:F4} m against a bound of {ceiling:F4} m");
        }
    }

    // =============================================================================================
    // 5. Coyote time and the jump buffer — no coverage at all before MOVE-3 extracted StepJump.
    // =============================================================================================

    /// <summary>Coyote still fires at the last tick of its window and refuses on the next one.
    /// <b>MOVE-8: the window is <c>CoyoteTimeSec</c> = 0.30 s, which is 18 whole ticks at 60 Hz</b>
    /// — Talon ruled both forgiveness timers to their generous end, up from 0.12 s / 7 ticks. The
    /// EDGE behaviour is what this test guards and it is unchanged; only where the edge sits
    /// moved.</summary>
    [Fact]
    public void CoyoteTime_FiresAtTheWindowEdge_AndNotOneTickLater()
    {
        int lastFiring = -1;
        for (int ticksAirborne = 1; ticksAirborne <= 30; ticksAirborne++)
        {
            float coyote = AvatarMotor.CoyoteTimeSec - ticksAirborne * Dt;
            var r = AvatarMotor.StepJump(grounded: false, coyote + Dt, 0f,
                jumpEdge: true, jumpAllowed: true, locked: false, Dt);
            if (r.Jumped)
                lastFiring = ticksAirborne;
            else
                break;
        }
        // 17, not 18, and the one tick is a float boundary rather than a bug: 0.30 s is an EXACT
        // multiple of the 60 Hz tick, so at the 18th airborne tick the remaining window computes to
        // a hair below zero (0.3f - 18 * (1/60f) is -1e-8, not 0) and the window has closed. 0.12 s
        // was not an exact multiple, which is why the old edge was a clean 7 of 7.2. The BEHAVIOUR
        // this test guards -- fires at the last tick inside, refuses on the next -- is unchanged.
        Assert.Equal(17, lastFiring);
        Near(AvatarMotor.CoyoteTimeSec, 0.30f, 1e-5f, "the coyote window itself");
    }

    /// <summary>A successful jump zeroes coyote, so a second press inside what would have been the
    /// same window gets nothing. Without this the forgiveness becomes a free double jump.</summary>
    [Fact]
    public void CoyoteTime_CannotDoubleServe_AfterASuccessfulJump()
    {
        var first = AvatarMotor.StepJump(grounded: true, 0f, 0f, jumpEdge: true, jumpAllowed: true, locked: false, Dt);
        Assert.True(first.Jumped);
        Assert.Equal(0f, first.CoyoteRemaining);

        var second = AvatarMotor.StepJump(grounded: false, first.CoyoteRemaining,
            first.JumpBufferRemaining, jumpEdge: true, jumpAllowed: true, locked: false, Dt);
        Assert.False(second.Jumped);
    }

    /// <summary>
    /// <b>A jump pressed before landing fires on the landing tick</b> (spec §6.2) — the whole point
    /// of the buffer, and the thing that makes a hop chain a chain. The press happens with coyote
    /// long expired; nothing fires until the tick that reports <c>grounded</c>, and then it fires
    /// immediately.
    /// </summary>
    [Fact]
    public void BufferedJump_PressedBeforeLanding_FiresOnTheLandingTick()
    {
        // Airborne, coyote long gone, and the key goes down.
        var armed = AvatarMotor.StepJump(grounded: false, prevCoyote: -1f, prevJumpBuffer: 0f,
            jumpEdge: true, jumpAllowed: true, locked: false, Dt);
        Assert.False(armed.Jumped);
        Assert.Equal(AvatarMotor.JumpBufferSec, armed.JumpBufferRemaining);

        // Still falling, key released. The buffer runs down but survives.
        float coyote = armed.CoyoteRemaining;
        float buffer = armed.JumpBufferRemaining;
        for (int i = 0; i < 5; i++)
        {
            var mid = AvatarMotor.StepJump(grounded: false, coyote, buffer,
                jumpEdge: false, jumpAllowed: true, locked: false, Dt);
            Assert.False(mid.Jumped);
            coyote = mid.CoyoteRemaining;
            buffer = mid.JumpBufferRemaining;
        }

        // Touchdown.
        var landed = AvatarMotor.StepJump(grounded: true, coyote, buffer,
            jumpEdge: false, jumpAllowed: true, locked: false, Dt);
        Assert.True(landed.Jumped, "the buffered press did not chain off the landing tick");
    }

    /// <summary>Holding jump through a landing does NOT auto-bounce (spec §6.2): the buffer is
    /// armed by the press edge only, so a chain needs a fresh press each time. Stated as a test
    /// because "holding jump should keep bouncing" is a plausible thing to add unasked.</summary>
    [Fact]
    public void HoldingJumpThroughALanding_DoesNotAutoBounce()
    {
        float coyote = -1f, buffer = -1f;
        for (int i = 0; i < 20; i++)
        {
            var r = AvatarMotor.StepJump(grounded: i >= 10, coyote, buffer,
                jumpEdge: false, jumpAllowed: true, locked: false, Dt);
            Assert.False(r.Jumped, $"an unpressed key fired a jump at tick {i}");
            coyote = r.CoyoteRemaining;
            buffer = r.JumpBufferRemaining;
        }
    }

    /// <summary>The water contract still denies a jump outright (spec §7): a swimming body neither
    /// arms the buffer nor fires.</summary>
    [Fact]
    public void JumpDenied_ByTheWaterContract_NeitherFiresNorArms()
    {
        var r = AvatarMotor.StepJump(grounded: true, 0f, 0f, jumpEdge: true, jumpAllowed: false, locked: false, Dt);
        Assert.False(r.Jumped);
        Assert.True(r.JumpBufferRemaining <= 0f);
    }

    // =============================================================================================
    // 5b. W6-5 — one press, at most one jump. The COMPOSITION of the two jumps, which is where the
    //     defect lived: both halves were individually covered and correct, and the tick that ran
    //     them back to back was covered nowhere, because it lived inside Step and Step needs a
    //     live CharacterBody3D. AvatarMotor.StepJumps is that tick, extracted; these are its tests.
    //
    //     Not one number below is a literal. JumpVelocity, AirJumpVelocityFraction, CoyoteTimeSec
    //     and AirJumpCountMax are all read off the live tuning, so a ruling that moves any of them
    //     moves these expectations with it and cannot turn a correct motor red.
    // =============================================================================================

    /// <summary>The tuning these tests resolve against. <c>Current</c> rather than <c>Default</c>
    /// deliberately: <see cref="AvatarMotor.StepJump"/> reads the forgiveness timers off
    /// <c>Current</c>, so anything else would compose two different tunings. Nothing in this suite
    /// writes it (see <c>MotorTuningSessionTests</c>' standing rule), so it is
    /// <c>Default</c>.</summary>
    private static MotorTuning Tuning => MotorTuning.Current;

    /// <summary>Any finite sprint wish. Mode 1 ignores it entirely; it exists for the Kick.</summary>
    private static float SprintWish => AvatarMotor.MoveSpeed * Tuning.SprintMultiplier;

    /// <summary>
    /// <b>The regression (W6-5, REVIEW-1 finding 1): a press one tick after walking off a ledge is
    /// an ordinary jump and nothing else.</b>
    ///
    /// <para>Before the fix this tick launched at <c>JumpVelocity x AirJumpVelocityFraction</c> —
    /// 20% under an ordinary jump — and returned <c>AirJumpsUsed == 1</c>, because
    /// <c>Step</c> handed <c>StepAirJump</c> the same press edge the ground jump had just spent
    /// and <c>prev.Grounded</c> is false throughout a coyote window, so the grounded early-out did
    /// not save it either. Both effects, from the window that exists to be forgiving.</para>
    /// </summary>
    [Fact]
    public void CoyotePress_IsAnOrdinaryJump_AndDoesNotAlsoSpendTheAirJump()
    {
        MotorTuning t = Tuning;
        // Without this the test would pass vacuously at AirJumpMode 0, the exact no-op this defect
        // hid behind until MOVE-8 ruled mode 1 on 2026-08-28.
        Assert.Equal(1f, t.AirJumpMode);
        Assert.True(t.AirJumpCountMax >= 1f, "there is no air jump to be wrongly spent");

        AvatarMotor.JumpsResolution r = AvatarMotor.StepJumps(t, Vector3.Zero, grounded: false,
            prevCoyote: AvatarMotor.CoyoteTimeSec - Dt, prevJumpBuffer: 0f, prevAirJumpsUsed: 0,
            jumpEdge: true, jumpAllowed: true, locked: false,
            sprintWish: SprintWish, dt: Dt);

        Assert.True(r.Jumped, "a live coyote window did not turn the press into an ordinary jump");
        // The two MEASUREMENTS first and the verdict last, deliberately: when this test fails it
        // must say what the launch speed and the counter actually were, not merely that a bool was
        // wrong. Before the fix this line read "expected 8.4000, got 6.7200".
        Near(t.JumpVelocity, r.Velocity.Y, 1e-5f, "the coyote jump's launch speed");
        Assert.Equal(0, r.AirJumpsUsed);
        Assert.False(r.AirJumped, "the same press ALSO reached the air jump");
    }

    /// <summary><b>The other half of the criterion: a genuine air jump is untouched.</b> Coyote
    /// expired, so no ground jump fires and the edge reaches the air jump unspent — it still
    /// launches at exactly <c>JumpVelocity x AirJumpVelocityFraction</c> and still costs one. The
    /// buffer it armed is zeroed by the second spend rule, so this press cannot also buy a
    /// buffered ground jump on the touchdown.</summary>
    [Fact]
    public void GenuineAirJump_CoyoteExpired_StillFiresAtTheFraction_AndStillCostsOne()
    {
        MotorTuning t = Tuning;
        AvatarMotor.JumpsResolution r = AvatarMotor.StepJumps(t, new Vector3(0f, -6f, 0f),
            grounded: false, prevCoyote: -1f, prevJumpBuffer: 0f, prevAirJumpsUsed: 0,
            jumpEdge: true, jumpAllowed: true, locked: false,
            sprintWish: SprintWish, dt: Dt);

        Assert.False(r.Jumped, "an expired coyote window fired a ground jump");
        Assert.True(r.AirJumped, "the air jump did not fire when nothing else could");
        Near(t.JumpVelocity * t.AirJumpVelocityFraction, r.Velocity.Y, 1e-5f,
            "the air jump's launch speed");
        Assert.Equal(1, r.AirJumpsUsed);
        Assert.Equal(0f, r.JumpBufferRemaining);
    }

    /// <summary><b>The double jump the coyote press did not spend is still there later in the same
    /// flight.</b> The two tests above measure one tick each; this one carries the counter across
    /// the gap, which is the shape the player actually feels — step off a ledge, jump, and then
    /// still have the second jump when the gap turns out to be wider than it looked.</summary>
    [Fact]
    public void TheCoyotePress_LeavesTheDoubleJump_ForLaterInTheSameFlight()
    {
        MotorTuning t = Tuning;
        AvatarMotor.JumpsResolution off = AvatarMotor.StepJumps(t, Vector3.Zero, grounded: false,
            prevCoyote: AvatarMotor.CoyoteTimeSec - Dt, prevJumpBuffer: 0f, prevAirJumpsUsed: 0,
            jumpEdge: true, jumpAllowed: true, locked: false,
            sprintWish: SprintWish, dt: Dt);
        Assert.True(off.Jumped);
        // The measurement this test is named for: the coyote press must leave the budget alone.
        // Before the fix this read "expected 0, got 1".
        Assert.Equal(0, off.AirJumpsUsed);

        // Rising, then falling, key released. Coyote is zero from the moment the jump fired.
        float coyote = off.CoyoteRemaining;
        float buffer = off.JumpBufferRemaining;
        byte used = off.AirJumpsUsed;
        Vector3 v = off.Velocity;
        for (int i = 0; i < 12; i++)
        {
            v.Y -= AvatarMotor.GravityFor(v.Y, jumpHeld: false, locked: false) * Dt;
            AvatarMotor.JumpsResolution mid = AvatarMotor.StepJumps(t, v, grounded: false, coyote,
                buffer, used, jumpEdge: false, jumpAllowed: true, locked: false,
                SprintWish, Dt);
            Assert.False(mid.AnyJump, $"an unpressed key fired a jump at tick {i}");
            coyote = mid.CoyoteRemaining;
            buffer = mid.JumpBufferRemaining;
            used = mid.AirJumpsUsed;
            v = mid.Velocity;
        }

        AvatarMotor.JumpsResolution second = AvatarMotor.StepJumps(t, v, grounded: false, coyote,
            buffer, used, jumpEdge: true, jumpAllowed: true, locked: false,
            SprintWish, Dt);
        Near(t.JumpVelocity * t.AirJumpVelocityFraction, second.Velocity.Y, 1e-5f,
            "the double jump taken later in the flight");
        Assert.Equal(1, second.AirJumpsUsed);
        Assert.True(second.AirJumped, "the double jump had already been spent by the coyote press");
    }

    /// <summary><b>The buffered-jump-on-landing chain still works through the composition.</b> The
    /// air-jump budget is already spent, so the press buys nothing in the air and the buffer it
    /// arms survives to the touchdown tick — the behaviour
    /// <see cref="BufferedJump_PressedBeforeLanding_FiresOnTheLandingTick"/> guards on
    /// <c>StepJump</c> alone, re-asserted on the function <c>Step</c> now actually calls.</summary>
    [Fact]
    public void BufferedJumpOnLanding_StillChains_ThroughTheComposition()
    {
        MotorTuning t = Tuning;
        var spent = (byte)Mathf.RoundToInt(t.AirJumpCountMax);

        AvatarMotor.JumpsResolution armed = AvatarMotor.StepJumps(t, new Vector3(0f, -6f, 0f),
            grounded: false, prevCoyote: -1f, prevJumpBuffer: 0f, prevAirJumpsUsed: spent,
            jumpEdge: true, jumpAllowed: true, locked: false, SprintWish, Dt);
        Assert.False(armed.AnyJump, "a spent budget still bought a jump in the air");
        Near(AvatarMotor.JumpBufferSec, armed.JumpBufferRemaining, 1e-5f, "the armed buffer");

        float coyote = armed.CoyoteRemaining;
        float buffer = armed.JumpBufferRemaining;
        byte used = armed.AirJumpsUsed;
        for (int i = 0; i < 5; i++)
        {
            AvatarMotor.JumpsResolution mid = AvatarMotor.StepJumps(t, new Vector3(0f, -8f, 0f),
                grounded: false, coyote, buffer, used, jumpEdge: false, jumpAllowed: true,
                locked: false, SprintWish, Dt);
            Assert.False(mid.AnyJump);
            coyote = mid.CoyoteRemaining;
            buffer = mid.JumpBufferRemaining;
            used = mid.AirJumpsUsed;
        }

        AvatarMotor.JumpsResolution landed = AvatarMotor.StepJumps(t, new Vector3(0f, -9f, 0f),
            grounded: true, coyote, buffer, used, jumpEdge: false, jumpAllowed: true,
            locked: false, SprintWish, Dt);
        Assert.True(landed.Jumped, "the buffered press did not chain off the landing tick");
        Assert.False(landed.AirJumped);
        Near(t.JumpVelocity, landed.Velocity.Y, 1e-5f, "the buffered jump's launch speed");
        Assert.Equal(0, landed.AirJumpsUsed);
    }

    /// <summary>
    /// <b>The guarantee lives in the composition, not in <c>StepAirJump</c> — do not re-split the
    /// call.</b> This is the defect, preserved as a measurement: hand <c>StepAirJump</c> the raw
    /// press edge after a coyote ground jump, exactly as <c>Step</c> did before W6-5, and it still
    /// overwrites the launch speed and still spends the counter. It cannot do otherwise — nothing
    /// in its parameter list says the edge was spent.
    ///
    /// <para>So this is not a bug report; it is the reason <see cref="AvatarMotor.StepJumps"/>
    /// exists. If somebody ever inlines those two calls back into <c>Step</c>, this test goes on
    /// passing and <see cref="CoyotePress_IsAnOrdinaryJump_AndDoesNotAlsoSpendTheAirJump"/> is the
    /// one that catches them.</para>
    /// </summary>
    [Fact]
    public void StepAirJump_HandedTheRawEdge_StillEatsTheGroundJump_WhichIsWhyStepJumpsExists()
    {
        MotorTuning t = Tuning;
        AvatarMotor.JumpResolution ground = AvatarMotor.StepJump(grounded: false,
            AvatarMotor.CoyoteTimeSec - Dt, 0f, jumpEdge: true, jumpAllowed: true, locked: false, Dt);
        Assert.True(ground.Jumped);

        var v = new Vector3(0f, t.JumpVelocity, 0f);
        AvatarMotor.AirJumpResolution air = AvatarMotor.StepAirJump(t, v, grounded: false,
            prevAirJumpsUsed: 0, jumpEdge: true, jumpAllowed: true, locked: false,
            SprintWish);

        Assert.True(air.Fired);
        Near(t.JumpVelocity * t.AirJumpVelocityFraction, air.Velocity.Y, 1e-5f,
            "the raw-edge composition's launch speed");
        Assert.Equal(1, air.AirJumpsUsed);
    }

    // =============================================================================================
    // 5c. MRF-C — a control lock spends the buffer. The 2026-08-30 master review's F9.
    // =============================================================================================

    /// <summary>
    /// <b>A jump buffered before a control lock does not fire out of the lock</b> (MECHANICS §2:
    /// the lock is total, there is no partial-control state).
    ///
    /// <para>The defect, stated as the stream that reproduced it: press jump while airborne — the
    /// buffer arms for <see cref="AvatarMotor.JumpBufferSec"/>, 0.30 s, 18 ticks at 60 Hz — then
    /// take a freeze/KO within that window and land. <c>Step</c> zeroes <c>effective.Jump</c> under
    /// a lock, so no NEW press gets through, but the already-armed buffer was untouched: the
    /// landing tick refilled coyote, the buffer was still positive, and the incapacitated body
    /// launched at <c>JumpVelocity</c> (8.4 m/s at the shipped tuning). <c>locked</c> and
    /// <c>ballistic</c> reached <see cref="AvatarMotor.StepJumps"/> and were spent only on the air
    /// half.</para>
    ///
    /// <para><b>Its own positive control is the third block:</b> the identical tick stream with
    /// <c>locked: false</c> DOES fire on the landing tick. Without that, an assertion that "no jump
    /// fired" would pass just as well against a stream that could never have jumped at all.</para>
    /// </summary>
    [Fact]
    public void BufferedJump_DoesNotFireOutOfAControlLock()
    {
        MotorTuning t = Tuning;

        // The press, in the air, unlocked: this is what arms the buffer. The air-jump budget is
        // handed in already spent, for the reason
        // BufferedJumpOnLanding_StillChains_ThroughTheComposition gives — at the shipped
        // AirJumpMode 1 an available air jump would consume this press and spend rule 2 would zero
        // the very buffer this test is about.
        var spent = (byte)Mathf.RoundToInt(t.AirJumpCountMax);
        AvatarMotor.JumpsResolution armed = AvatarMotor.StepJumps(t, new Vector3(0f, -6f, 0f),
            grounded: false, prevCoyote: -1f, prevJumpBuffer: 0f, prevAirJumpsUsed: spent,
            jumpEdge: true, jumpAllowed: true, locked: false, SprintWish, Dt);
        Assert.False(armed.AnyJump, "the arming press must buy nothing in the air");
        Near(AvatarMotor.JumpBufferSec, armed.JumpBufferRemaining, 1e-5f,
            "the buffer the press arms");

        // The lock lands 9 ticks later — half the buffer window, well inside it — and the body
        // touches down on the tick after that.
        AvatarMotor.JumpsResolution held = RunToTouchdown(t, armed, ticksBeforeTouchdown: 9,
            locked: true);
        Assert.False(held.Jumped,
            "a jump buffered before the lock fired out of it — the incapacitated body launched");
        Assert.False(held.AnyJump, "the air half fired instead");
        Near(-9f, held.Velocity.Y, 1e-5f, "the locked body's vertical velocity (must be untouched)");
        Assert.Equal(0f, held.JumpBufferRemaining);

        // POSITIVE CONTROL. Identical stream, lock never applied: the buffered press must fire, or
        // the assertion above is measuring an unreachable jump rather than a suppressed one.
        AvatarMotor.JumpsResolution free = RunToTouchdown(t, armed, ticksBeforeTouchdown: 9,
            locked: false);
        Assert.True(free.Jumped, "positive control: the unlocked stream did not fire the buffer");
        Near(t.JumpVelocity, free.Velocity.Y, 1e-5f, "the unlocked buffered jump's launch speed");
    }

    /// <summary>Drives <paramref name="from"/> forward <paramref name="ticksBeforeTouchdown"/>
    /// airborne ticks under <paramref name="locked"/> and returns the touchdown tick's resolution.
    /// No press after the first: the buffer is the only thing that can fire here.</summary>
    private static AvatarMotor.JumpsResolution RunToTouchdown(in MotorTuning t,
        AvatarMotor.JumpsResolution from, int ticksBeforeTouchdown, bool locked)
    {
        float coyote = from.CoyoteRemaining;
        float buffer = from.JumpBufferRemaining;
        byte used = from.AirJumpsUsed;
        for (int i = 0; i < ticksBeforeTouchdown; i++)
        {
            AvatarMotor.JumpsResolution mid = AvatarMotor.StepJumps(t, new Vector3(0f, -8f, 0f),
                grounded: false, coyote, buffer, used, jumpEdge: false, jumpAllowed: true,
                locked, SprintWish, Dt);
            Assert.False(mid.AnyJump, $"an unpressed airborne tick {i} fired a jump");
            coyote = mid.CoyoteRemaining;
            buffer = mid.JumpBufferRemaining;
            used = mid.AirJumpsUsed;
        }
        return AvatarMotor.StepJumps(t, new Vector3(0f, -9f, 0f), grounded: true, coyote, buffer,
            used, jumpEdge: false, jumpAllowed: true, locked, SprintWish, Dt);
    }

    // =============================================================================================
    // 6. The landing rule, and the held bit's wire contract.
    // =============================================================================================

    /// <summary><b>A landing costs nothing</b> (spec §6.1). A rule implemented as the absence of a
    /// line needs an assertion or it is unprovable; this is that assertion, and the playground's
    /// hop chains are its visible half.</summary>
    [Fact]
    public void ALanding_PreservesHorizontalSpeedExactly()
    {
        Assert.Equal(1f, AvatarMotor.LandingSpeedMultiplier);
        foreach (float v in new[] { 0f, 2.43f, 5.4f, 8.64f })
            Assert.Equal(v, AvatarMotor.LandingHorizontalSpeed(v));
    }

    /// <summary><c>JumpHeld</c> defaults false, so every scripted and bot intent source hops at the
    /// minimum height, deterministically (spec §4.1).</summary>
    [Fact]
    public void JumpHeld_DefaultsFalse_SoEveryBotHopIsAMinimumHop()
    {
        Assert.False(default(MoveIntent).JumpHeld);
        Assert.False(MoveIntent.None.JumpHeld);
        Assert.False(new MoveIntent { Jump = true }.JumpHeld);
    }

    // =============================================================================================
    // The simulators. Euler on flat ground, driving the shipped pure functions.
    // =============================================================================================

    /// <summary>Horizontal speed after <paramref name="sec"/> of holding <paramref name="wish"/>,
    /// starting at <paramref name="startSpeed"/> travelling along -Z. Returns the magnitude, so
    /// zero means a dead stop whether or not the body went on to reverse.</summary>
    private static float SimulateHorizontal(float startSpeed, Vector3 wish, bool grounded, float sec)
    {
        var v = new Vector3(0f, 0f, -startSpeed);
        for (int i = 0; i < TicksIn(sec); i++)
        {
            float rate = AvatarMotor.RateFor(v, wish, grounded);
            v = new Vector3(
                Mathf.MoveToward(v.X, wish.X, rate * Dt), 0f,
                Mathf.MoveToward(v.Z, wish.Z, rate * Dt));
        }
        return new Vector2(v.X, v.Z).Length();
    }

    /// <summary>As above but signed on Z, so a reversal is visible as a sign flip rather than
    /// collapsing into a magnitude.</summary>
    private static float SimulateHorizontalSigned(float startZ, float wishZ, bool grounded, float sec)
    {
        var v = new Vector3(0f, 0f, startZ);
        var wish = new Vector3(0f, 0f, wishZ);
        for (int i = 0; i < TicksIn(sec); i++)
        {
            float rate = AvatarMotor.RateFor(v, wish, grounded);
            v = new Vector3(v.X, 0f, Mathf.MoveToward(v.Z, wishZ, rate * Dt));
        }
        return v.Z;
    }

    /// <summary>
    /// One jump, tick by tick, from a standing launch on flat ground: apex above the launch
    /// position (metres) and total airtime (seconds).
    ///
    /// <para>The launch tick is modelled the way <c>Step</c> runs it — <c>prev.Grounded</c> is true
    /// so the gravity branch is skipped entirely and the body advances a full
    /// <c>JumpVelocity × dt</c> before any gravity applies. That one tick is worth 0.14 m and the
    /// spec calls it out; leaving it out here would put every number 0.14 m low.</para>
    /// </summary>
    /// <param name="holdForTicks">How many airborne ticks the key stays down.</param>
    /// <param name="mashPeriodTicks">If positive, the key is instead toggled with this period —
    /// the flicker-rule case.</param>
    private static (float ApexM, float AirtimeSec) SimulateJump(int holdForTicks,
        int mashPeriodTicks = 0)
    {
        float vy = AvatarMotor.JumpVelocity;
        float y = vy * Dt;          // the launch tick, gravity branch skipped
        float apex = y;
        int ticks = 1;

        while (y > 0f && ticks < 600)
        {
            bool held = mashPeriodTicks > 0
                ? (ticks / mashPeriodTicks) % 2 == 0
                : ticks <= holdForTicks;
            vy -= AvatarMotor.GravityFor(vy, held, locked: false) * Dt;
            y += vy * Dt;
            if (y > apex)
                apex = y;
            ticks++;
        }
        return (apex, ticks * Dt);
    }
}
