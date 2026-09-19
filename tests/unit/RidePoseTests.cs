using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>BIKE-4A — the ride channel's arithmetic.</b> <see cref="RidePose"/> exists so the rider's
/// numbers are assertions here rather than captions under a screenshot, exactly as
/// <c>AnticipationCoilTests</c> and <c>VerbPoseTests</c> do for the coil and the crouch verbs.
///
/// <para>What this file can and cannot prove. It proves the cadence rule, the PEDAL/COAST trigger
/// and its hysteresis, the crank phase's advance and its settle, the pose targets' shape, and the
/// two identities the packet's safety rests on (weight 0 is the exact no-op; the airborne scale is
/// exactly 1 unmounted). It proves nothing about the rig — whether the hip angle it returns reaches
/// a node — which is the engine half and is checked by <c>--bike-selftest</c>'s
/// <c>ride_*</c> checks instead.</para>
/// </summary>
public class RidePoseTests
{
    private const float Dt = 1f / 60f;

    /// <summary>The ride cap on the shipped bike tuning: foot 3.8 × ride 1.55 × sprint 1.6.
    /// Typed here rather than computed through <c>BikeRig</c> so this file stays independent of the
    /// lab's tuning struct; the self-test asserts the live one.</summary>
    private const float RideCapMps = 9.424f;

    // --- THE CADENCE RULE (acceptance criterion 2) ------------------------------------------------

    [Fact]
    public void CrankMetersPerRev_IsTheThreeCRule_TwoPiRadiusTimesGearRatio()
    {
        Assert.Equal( (double)(0.34f), RidePose.WheelRadiusM, 5);
        Assert.Equal( (double)(2.75f), RidePose.WheelRevsPerCrankRev, 5);
        Assert.Equal( (double)(2f * MathF.PI * 0.34f * 2.75f), RidePose.CrankMetersPerRev, 4);
    }

    [Fact]
    public void CrankRate_IsZeroAtRest()
    {
        Assert.Equal(0f, RidePose.CrankHzAt(0f));
    }

    [Fact]
    public void CrankRate_IsProportionalToSpeed()
    {
        float a = RidePose.CrankHzAt(2f);
        float b = RidePose.CrankHzAt(4f);
        float c = RidePose.CrankHzAt(8f);
        Assert.True(a > 0f, $"crank rate at 2 m/s was {a}");
        Assert.Equal( (double)(2f * a), b, 5);
        Assert.Equal( (double)(4f * a), c, 5);
    }

    /// <summary>The number the packet asks to be stated: at the 9.42 m/s ride cap the cranks turn
    /// 1.60 rev/s (96 rpm) under 3C's rule with the greybox's own 0.34 m wheel. 3B's provisional
    /// 3.4 m/rev would have said 2.77 rev/s (166 rpm), which is a track sprint rather than a
    /// ride.</summary>
    [Fact]
    public void CrankRate_AtTheRideCap_IsABriskHumanCadence()
    {
        float hz = RidePose.CrankHzAt(RideCapMps);
        Assert.InRange(hz, 1.5f, 1.7f);
        Assert.InRange(hz * 60f, 90f, 102f);
    }

    [Fact]
    public void CrankRate_IsFiniteOnGarbage()
    {
        Assert.Equal(0f, RidePose.CrankHzAt(float.NaN));
        Assert.Equal(0f, RidePose.CrankHzAt(float.PositiveInfinity));
    }

    /// <summary>The coast threshold is the on-foot body's own idle exit, so a mounted body and an
    /// unmounted one go still at the same ground speed.</summary>
    [Fact]
    public void CoastThreshold_IsTheOnFootIdleExit()
    {
        Assert.Equal(LocomotionProfile.IdleExitMps, RidePose.CrankStallMps);
        Assert.False(RidePose.CranksTurn(RideMode.Pedal, onFloor: true, 0.10f));
        Assert.True(RidePose.CranksTurn(RideMode.Pedal, onFloor: true, 4.0f));
    }

    [Fact]
    public void CranksNeverTurn_WhileCoastingOrAirborne()
    {
        Assert.False(RidePose.CranksTurn(RideMode.Coast, onFloor: true, 8f));
        Assert.False(RidePose.CranksTurn(RideMode.Pedal, onFloor: false, 8f));
    }

    // --- PEDAL vs COAST (acceptance criterion 3) --------------------------------------------------

    [Fact]
    public void Wants_Pedal_OnlyWithForwardInputAtOrBelowTheWish()
    {
        Assert.Equal(RideMode.Pedal, RidePose.Wants(1f, 5f, 9.4f));
        Assert.Equal(RideMode.Pedal, RidePose.Wants(1f, 9.4f, 9.4f));
    }

    [Fact]
    public void Wants_Coast_WithNoForwardInput()
    {
        Assert.Equal(RideMode.Coast, RidePose.Wants(0f, 5f, 9.4f));
        Assert.Equal(RideMode.Coast, RidePose.Wants(RidePose.ForwardInputDeadzone, 5f, 9.4f));
    }

    [Fact]
    public void Wants_Coast_WhenMomentumCarriesTheBodyPastTheWish()
    {
        // The slope carrying: over the wish by more than the margin, stick still held.
        Assert.Equal(RideMode.Coast, RidePose.Wants(1f, 9.4f + RidePose.CoastOverWishMps + 0.1f, 9.4f));
        // Inside the margin is still driving — a millimetre of motor overshoot is not a coast.
        Assert.Equal(RideMode.Pedal, RidePose.Wants(1f, 9.4f + (RidePose.CoastOverWishMps * 0.5f), 9.4f));
    }

    [Fact]
    public void Wants_Coast_WhenNoWishIsStated()
    {
        Assert.Equal(RideMode.Coast, RidePose.Wants(1f, 5f, 0f));
    }

    [Fact]
    public void StepMode_HoldsForTheHysteresisBeforeSwitching()
    {
        RideMode mode = RideMode.Coast;
        float hold = 0f;
        int ticks = 0;
        while (mode == RideMode.Coast && ticks < 200)
        {
            (mode, hold) = RidePose.StepMode(mode, hold, RideMode.Pedal, Dt);
            ticks++;
        }
        Assert.Equal(RideMode.Pedal, mode);
        // It waited the stated hold and no longer: 0.3 s at 60 Hz is 18 ticks.
        Assert.Equal(Mathf.CeilToInt(RidePose.ModeHoldSec / Dt), ticks);
        Assert.Equal(0f, hold);
    }

    /// <summary>The gear-flicker cure, and the difference between a hysteresis and a delay: a wish
    /// that keeps changing its mind never accumulates toward a switch.</summary>
    [Fact]
    public void StepMode_AFlickerNeverSwitches()
    {
        RideMode mode = RideMode.Coast;
        float hold = 0f;
        for (int i = 0; i < 600; i++)
        {
            RideMode wants = i % 2 == 0 ? RideMode.Pedal : RideMode.Coast;
            (mode, hold) = RidePose.StepMode(mode, hold, wants, Dt);
        }
        Assert.Equal(RideMode.Coast, mode);
    }

    [Fact]
    public void StepMode_AgreementResetsTheClock()
    {
        (RideMode mode, float hold) = RidePose.StepMode(RideMode.Pedal, 0.29f, RideMode.Pedal, Dt);
        Assert.Equal(RideMode.Pedal, mode);
        Assert.Equal(0f, hold);
    }

    // --- THE CRANK PHASE --------------------------------------------------------------------------

    [Fact]
    public void AdvancePhase_TurnsAtTheCadenceRuleAndWraps()
    {
        float p = 0f;
        float speed = 6f;
        for (int i = 0; i < 60; i++)
            p = RidePose.AdvancePhase(p, speed, Dt, cranksTurn: true);
        // One second of turning is exactly CrankHzAt(speed) revolutions; the phase is the fraction.
        float revs = RidePose.CrankHzAt(speed);
        Assert.Equal( (double)(revs - MathF.Floor(revs)), p, 3);
        Assert.InRange(p, 0f, 1f);
    }

    [Theory]
    [InlineData(0.10f, 0f)]
    [InlineData(0.40f, 0.5f)]
    [InlineData(0.60f, 0.5f)]
    [InlineData(0.90f, 0f)]
    public void AdvancePhase_SettlesToTheNearestLevelCrank(float start, float expected)
    {
        float p = start;
        for (int i = 0; i < 120; i++)
            p = RidePose.AdvancePhase(p, 6f, Dt, cranksTurn: false);
        Assert.Equal( (double)(expected), p, 3);
    }

    [Fact]
    public void AdvancePhase_SurvivesGarbage()
    {
        float p = RidePose.AdvancePhase(float.NaN, 6f, Dt, cranksTurn: true);
        Assert.True(float.IsFinite(p));
        Assert.InRange(p, 0f, 1f);
    }

    // --- THE POSE ---------------------------------------------------------------------------------

    [Fact]
    public void For_TheTwoLegsAreHalfARevolutionApart()
    {
        RidePose.Targets a = RidePose.For(0.13f, RidePose.PedalTiltRad);
        RidePose.Targets b = RidePose.For(0.13f + 0.5f, RidePose.PedalTiltRad);
        Assert.Equal( (double)(a.HipPitchL), b.HipPitchR, 5);
        Assert.Equal( (double)(a.HipPitchR), b.HipPitchL, 5);
        Assert.Equal( (double)(a.KneeFoldL), b.KneeFoldR, 5);
    }

    /// <summary>3B's COAST row: "cranks level (3-and-9)". Both level phases put one foot forward and
    /// one back with the knees equal, which is what "level" means on a crank.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    public void For_AtALevelPhase_TheKneesAreEqualAndTheFeetOppose(float phase)
    {
        RidePose.Targets t = RidePose.For(phase, RidePose.CoastTiltRad);
        Assert.Equal( (double)(t.KneeFoldL), t.KneeFoldR, 4);
        Assert.Equal( (double)(RidePose.CrankKneeBaseFold), t.KneeFoldL, 4);
        Assert.Equal( (double)(RidePose.CrankHipBiasRad), (t.HipPitchL + t.HipPitchR) * 0.5f, 4);
        Assert.True(MathF.Abs(t.HipPitchL - t.HipPitchR) > 0.4f,
            $"the feet should oppose across the crank: {t.HipPitchL} vs {t.HipPitchR}");
    }

    [Fact]
    public void For_KneeFoldStaysInsideTheRigsStructuralCap()
    {
        for (int i = 0; i <= 100; i++)
        {
            RidePose.Targets t = RidePose.For(i / 100f, RidePose.PedalTiltRad);
            Assert.InRange(t.KneeFoldL, 0f, 0.50f);
            Assert.InRange(t.KneeFoldR, 0f, 0.50f);
            Assert.InRange(MathF.Abs(t.HipPitchL), 0f, LocomotionProfile.MaxLegSwingRad);
            Assert.InRange(MathF.Abs(t.HipPitchR), 0f, LocomotionProfile.MaxLegSwingRad);
        }
    }

    /// <summary>PEDAL and COAST have to be distinguishable above the waist too — the legs are behind
    /// the frame from most angles.</summary>
    [Fact]
    public void PedalAndCoastAreDistinguishablePitches_BothInsideTheTiltClamp()
    {
        Assert.True(RidePose.TiltFor(RideMode.Pedal) > RidePose.TiltFor(RideMode.Coast));
        Assert.True(RidePose.TiltFor(RideMode.Pedal) - RidePose.TiltFor(RideMode.Coast) > 0.08f);
        Assert.InRange(RidePose.TiltFor(RideMode.Pedal), 0f, AvatarVisual.MaxBodyTilt);
    }

    [Fact]
    public void None_IsTheExactZeroPose()
    {
        Assert.Equal(0f, RidePose.None.HipPitchL);
        Assert.Equal(0f, RidePose.None.HipPitchR);
        Assert.Equal(0f, RidePose.None.KneeFoldL);
        Assert.Equal(0f, RidePose.None.KneeFoldR);
        Assert.Equal(0f, RidePose.None.ForwardTiltRad);
    }

    // --- THE AIRBORNE PARTITION -------------------------------------------------------------------

    /// <summary>The identity the whole packet's safety argument rests on: an unmounted body's
    /// airborne partition is scaled by exactly 1, bit for bit, so every jump, fall and landing that
    /// shipped is untouched.</summary>
    [Fact]
    public void AirAmplitude_IsExactlyOneAtWeightZero()
    {
        Assert.Equal(
            BitConverter.SingleToInt32Bits(1f),
            BitConverter.SingleToInt32Bits(RidePose.AirAmplitudeAt(0f)));
    }

    [Fact]
    public void AirAmplitude_IsTheStatedFractionAtFullRide()
    {
        Assert.Equal( (double)(RidePose.AirAmplitudeFraction), RidePose.AirAmplitudeAt(1f), 5);
        Assert.InRange(RidePose.AirAmplitudeAt(0.5f), 0.69f, 0.71f);
    }

    // --- THE BARS ---------------------------------------------------------------------------------

    [Fact]
    public void BarHands_AreSymmetricAndForwardAtZeroSteer()
    {
        (Vector3 l, Vector3 r) = RidePose.BarHands(0.38f, 0f);
        Assert.Equal( (double)(-l.X), r.X, 5);
        Assert.Equal( (double)(l.Y), r.Y, 5);
        Assert.Equal( (double)(l.Z), r.Z, 5);
        Assert.True(l.Z < 0f, $"the bars are in FRONT of the shoulders (rig faces -Z): {l.Z}");
        Assert.True(l.Y > 0f, $"the bars are ABOVE the hanging hand: {l.Y}");
    }

    /// <summary>Both grips have to be inside the arm's reach or the solver returns a straight arm
    /// pointing at nothing and the hands miss the bars.</summary>
    [Theory]
    [InlineData(0.28f)]
    [InlineData(0.38f)]
    [InlineData(0.55f)]
    public void BarHands_AreReachableOnEveryRigLength(float armLengthM)
    {
        (Vector3 l, Vector3 r) = RidePose.BarHands(armLengthM, 0f);
        var rest = new Vector3(0f, -armLengthM, 0f);
        Assert.True((rest + l).Length() < armLengthM,
            $"left grip {(rest + l).Length()} vs arm {armLengthM}");
        Assert.True((rest + r).Length() < armLengthM,
            $"right grip {(rest + r).Length()} vs arm {armLengthM}");
    }

    [Fact]
    public void BarHands_YawWithTheSteer()
    {
        (Vector3 l0, Vector3 r0) = RidePose.BarHands(0.38f, 0f);
        (Vector3 l1, Vector3 r1) = RidePose.BarHands(0.38f, 1f);
        Assert.True(MathF.Abs(l1.Z - l0.Z) > 0.01f, "the outside hand should move fore/aft");
        Assert.True(MathF.Abs(r1.Z - r0.Z) > 0.01f, "the inside hand should move fore/aft");
        // Opposite ways: the bar turns about its centre rather than translating.
        Assert.True((l1.Z - l0.Z) * (r1.Z - r0.Z) < 0f,
            $"the two grips moved the same way ({l1.Z - l0.Z} and {r1.Z - r0.Z}) — that is a slide, not a yaw");
    }

    [Fact]
    public void BarHands_ClampGarbageSteer()
    {
        (Vector3 l, _) = RidePose.BarHands(0.38f, float.NaN);
        Assert.True(l.IsFinite());
    }

    // --- ACCEPTANCE CRITERION 5: THE ABSENCE CHECK, WITH ITS POSITIVE CONTROL ----------------------

    /// <summary>
    /// <b>BIKE-4A adds no write to avatar velocity or position, and no second velocity writer.</b>
    /// The ride channel is a POSE: it reads a velocity the motor already resolved and answers with
    /// angles. <c>BikeLayer.PostStep</c> stays the sole writer of the body's motion in this lab, as
    /// BIKE-0's own isolation rule requires.
    ///
    /// <para><b>The positive control is real code, not a planted string</b>, which is the packet's
    /// own instruction: the same matcher is pointed at <c>BikeLayer.cs</c>, whose <c>PostStep</c>
    /// genuinely writes <c>_avatar.Velocity</c> a dozen times. If it finds those and finds nothing
    /// in this packet's files, "absent" means absent rather than "the matcher never worked". A
    /// planted literal is added on top for the specific shape a ride channel would most plausibly
    /// grow — a pose nudging the body — because that string does not occur anywhere real.</para>
    /// </summary>
    [Fact]
    public void TheRideChannelWritesNoVelocityOrPosition_AndTheMatcherWouldSeeItIfItDid()
    {
        string root = FindRepoRoot();

        // THE POSITIVE CONTROL, FIRST. An instrument that has never read a positive is not known to
        // work — so this fires before anything is trusted to be empty.
        string bikeLayer = File.ReadAllText(
            Path.Combine(root, "scripts", "dev", "playground", "BikeLayer.cs"));
        string[] realWrites = MotionWrites(bikeLayer).ToArray();
        Assert.True(realWrites.Length >= 8,
            $"the positive control found only {realWrites.Length} of BikeLayer's velocity writes — "
          + "the matcher is broken, and every 'absent' below is worthless");

        // THE TWO FILES THIS PACKET ADDED. Neither may write motion of any kind.
        foreach (string rel in new[]
        {
            Path.Combine("scripts", "game", "sandbox", "RidePose.cs"),
            Path.Combine("scripts", "dev", "MovementPlayground.BikeRide.cs"),
        })
        {
            string src = File.ReadAllText(Path.Combine(root, rel));
            Assert.True(MotionWrites(src).Count() == 0,
                $"{rel} writes motion: {string.Join(" | ", MotionWrites(src))}");
        }

        // THE RIDE'S OWN LINES IN THE SHIPPED FILE. AvatarVisual writes rig-node transforms on
        // nearly every line, so the whole file cannot be scanned — the scan is over the lines the
        // ride channel touches, identified by name.
        string visual = File.ReadAllText(
            Path.Combine(root, "scripts", "game", "sandbox", "AvatarVisual.cs"));
        string[] rideMotion = visual.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal))
            .Where(l => l.Contains("ride", StringComparison.OrdinalIgnoreCase)
                     || l.Contains("crank", StringComparison.OrdinalIgnoreCase))
            .Where(l => MotionWrites(l).Any())
            .ToArray();
        Assert.True(rideMotion.Length == 0,
            $"the ride channel writes motion in AvatarVisual: {string.Join(" | ", rideMotion)}");

        // AND THE PLANTED SHAPE the matcher must also catch — the nudge a pose layer grows when
        // somebody decides the body should "help" the bike along.
        Assert.NotEmpty(MotionWrites("_avatar.Velocity += ridePose.Forward * rideW;"));
        Assert.NotEmpty(MotionWrites("avatar.GlobalPosition = seatPoint;"));
    }

    /// <summary><b>BikeLayer gained getters and nothing else</b> (the packet's seam: read-only
    /// accessors only). Asserted off the type rather than the source, so a setter added later fails
    /// here even if it is spelled differently.</summary>
    [Fact]
    public void TheAccessorsBikeFourAAddedToBikeLayerAreReadOnly()
    {
        foreach (string name in new[] { "RideWishMps", "LastMoveDir" })
        {
            var prop = typeof(MpFoundation.Dev.Playground.BikeLayer)
                .GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            Assert.True(prop!.CanRead, $"{name} must be readable");
            Assert.False(prop.CanWrite, $"{name} must be read-only — the seam allows a getter only");
        }
    }

    /// <summary>Lines that assign to a body's motion — velocity, world position or transform.
    /// Deliberately broad on the left-hand side (anything ending in one of those names) and narrow
    /// on the operator (a real assignment, never a comparison).</summary>
    private static IEnumerable<string> MotionWrites(string source)
        => source.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal)
                     && !l.StartsWith("///", StringComparison.Ordinal)
                     && !l.StartsWith("*", StringComparison.Ordinal))
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(
                l,
                @"\.(Velocity|GlobalPosition|GlobalTransform)\s*(=[^=]|\+=|-=|\*=)"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
