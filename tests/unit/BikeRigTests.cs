using System.Collections.Generic;
using System.Linq;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The bike prototype's arithmetic, pinned</b> (BIKE-0, 2026-09-01). <c>BikeRig</c> is pure,
/// so every claim the layer makes about a burst, a hop, a clamp, a drift, the ramp or the spring
/// is a claim about a function of values, and those are asserted here without an engine.
///
/// <para><b>The one that matters most is the first.</b> The ride tuning is applied through
/// <c>MotorTuning.TryApply</c>, which validates and CLAMPS: a ride tuning that only becomes legal
/// by being clamped would put a bike in the lab that is not the one this file describes, and the
/// notes Talon writes would be filed against numbers the motor never ran. Same law as
/// <c>MovementPresetTests</c>, same sweep — and it runs at every point of the blend, because the
/// layer applies the blend's intermediate tunings too.</para>
///
/// <para>Parks no tuning: every assertion takes values and asks pure functions about them.</para>
/// </summary>
public sealed class BikeRigTests
{
    private static readonly BikeTuning B = BikeTuning.Default;

    public static IEnumerable<object[]> FootTunings => new[]
    {
        new object[] { "shipped default", MotorTuning.Default },
        new object[] { "roll base (CTRL+0)", MovementPresets.ForKey(0, PresetBank.Ctrl)!.Value.Tuning },
        new object[] { "surf (CTRL+1)", MovementPresets.ForKey(1, PresetBank.Ctrl)!.Value.Tuning },
    };

    [Theory]
    [MemberData(nameof(FootTunings))]
    public void TheRideTuningIsInsideEveryKnobsRange_AndSurvivesValidationUnchanged_AtEveryBlend(
        string name, MotorTuning foot)
    {
        foreach (float blend in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            MotorTuning ride = BikeRig.Ride(foot, B, blend);

            foreach (MotorKnob knob in MotorTuningKnobs.All)
            {
                float v = knob.Get(ride);
                Assert.True(v >= knob.Min && v <= knob.Max,
                    $"ride of {name} at blend {blend}: {knob.Name} = {v} is outside [{knob.Min}, {knob.Max}]");
            }

            MotorTuning seated = MotorTuning.Validate(ride, out IReadOnlyList<string> warnings);
            Assert.True(seated.Equals(ride),
                $"ride of {name} at blend {blend} does not survive validation unchanged - the lab "
              + "would run a bike this file does not describe");
            Assert.True(warnings.Count == 0,
                $"ride of {name} at blend {blend} validated with warnings: {string.Join(" | ", warnings)}");
        }
    }

    [Fact]
    public void BlendZeroIsTheFootTuningExactly_AndOneIsTheFullRide()
    {
        MotorTuning foot = MotorTuning.Default;
        Assert.Equal(foot, BikeRig.Ride(foot, B, 0f));
        Assert.Equal(BikeRig.Ride(foot, B), BikeRig.Ride(foot, B, 1f));
        Assert.Equal(BikeRig.Ride(foot, B), BikeRig.Ride(foot, B, 7f));   // clamped
        MotorTuning mid = BikeRig.Ride(foot, B, 0.5f);
        Assert.Equal((foot.MoveSpeed + foot.MoveSpeed * B.RideSpeedMul) * 0.5f, mid.MoveSpeed, 1e-4f);
        Assert.Equal((foot.Deceleration + B.RideDeceleration) * 0.5f, mid.Deceleration, 1e-4f);
    }

    [Fact]
    public void RidingIsFasterAndSlipperier_TurnsAsTightlyAsTheBody_AndLeavesTheJumpAlone()
    {
        MotorTuning foot = MotorTuning.Default;
        MotorTuning ride = BikeRig.Ride(foot, B);

        Assert.True(ride.MoveSpeed > foot.MoveSpeed);
        Assert.True(ride.Deceleration < foot.Deceleration);
        Assert.True(ride.AirControlBuild < foot.AirControlBuild);

        // Talon, 2026-09-01: tight turning. The ride inherits the body's turn rows at RideTurnMul.
        Assert.Equal(foot.TurnLerp * B.RideTurnMul, ride.TurnLerp, 1e-4f);
        Assert.Equal(foot.TurnAcceleration * B.RideTurnAccelMul, ride.TurnAcceleration, 1e-4f);

        // The arc rides across untouched: a jump is the same height on and off the bike.
        Assert.Equal(foot.JumpVelocity, ride.JumpVelocity);
        Assert.Equal(foot.Gravity, ride.Gravity);
        Assert.Equal(foot.SprintMultiplier, ride.SprintMultiplier);
        Assert.Equal(foot.CoyoteTimeSec, ride.CoyoteTimeSec);
    }

    [Fact]
    public void TheFootCapIsTheSprintWish_AndTheRideCapIsAboveIt()
    {
        MotorTuning foot = MotorTuning.Default;
        Assert.Equal(foot.MoveSpeed * foot.SprintMultiplier, BikeRig.FootCapMps(foot), 1e-4f);
        Assert.Equal(BikeRig.FootCapMps(BikeRig.Ride(foot, B)), BikeRig.RideCapMps(foot, B), 1e-4f);
        Assert.True(BikeRig.RideCapMps(foot, B) > BikeRig.FootCapMps(foot),
            "the bike must be faster than running or the stumble prices nothing");
    }

    [Fact]
    public void TheSlideJumpIsTallerAndChangesNothingElse()
    {
        MotorTuning foot = MotorTuning.Default;
        MotorTuning slide = BikeRig.SlideJump(foot, B);
        Assert.Equal(foot.JumpVelocity * B.SlideJumpMul, slide.JumpVelocity, 1e-4f);
        Assert.Equal(foot, slide with { JumpVelocity = foot.JumpVelocity });
        MotorTuning seated = MotorTuning.Validate(slide, out _);
        Assert.Equal(slide.JumpVelocity, seated.JumpVelocity, 1e-4f);
    }

    [Fact]
    public void AHopNeverTakesAwayRiseTheBodyAlreadyHas()
    {
        Assert.Equal(B.MountHopMps, BikeRig.Hop(new Vector3(3f, -2f, 0f), B.MountHopMps).Y, 1e-4f);
        Assert.Equal(5f, BikeRig.Hop(new Vector3(3f, 5f, 0f), B.MountHopMps).Y, 1e-4f);
        Assert.Equal(3f, BikeRig.Hop(new Vector3(3f, -2f, 0f), B.MountHopMps).X, 1e-4f);
    }

    [Theory]
    [InlineData(-6f)]   // falling
    [InlineData(0f)]    // apex
    [InlineData(2.5f)]  // still rising
    public void TheAirMountBurstNeverCostsHeightAlreadyBought(float vy)
    {
        var v = new Vector3(4f, vy, 0f);
        Vector3 after = BikeRig.AirMountBurst(v, new Vector3(1f, 0f, 0f), B);
        Assert.Equal(Mathf.Max(vy, 0f) + B.AirMountUpMps, after.Y, 1e-4f);
        Assert.Equal(4f + B.AirMountForwardMps, after.X, 1e-4f);
        Assert.Equal(0f, after.Z, 1e-4f);
    }

    [Fact]
    public void TheBurstsThrowAlongTheHeadingAndNeverUpTheHeading()
    {
        var heading = new Vector3(0f, 0.8f, 0.6f);
        Vector3 a = BikeRig.AirMountBurst(Vector3.Zero, heading, B);
        Assert.Equal(B.AirMountUpMps, a.Y, 1e-4f);
        Assert.Equal(B.AirMountForwardMps, a.Z, 1e-4f);
        Vector3 l = BikeRig.LandDismountBurst(new Vector3(0f, -3f, 5f), heading, B);
        Assert.Equal(B.LandDismountUpMps, l.Y, 1e-4f);      // SET, the floor's -3 is gone
        Assert.Equal(5f + B.LandDismountForwardMps, l.Z, 1e-4f);
    }

    [Fact]
    public void TheStumbleClampsOnlyAboveTheCap_AndOnlyHorizontally()
    {
        float cap = BikeRig.FootCapMps(MotorTuning.Default);

        Vector3 slow = new(3f, -1f, 0f);
        Vector3 s = BikeRig.StumbleClamp(slow, cap, B, out bool clampedSlow);
        Assert.False(clampedSlow);
        Assert.Equal(slow, s);

        Vector3 fast = new(0f, -2f, cap * 1.5f);
        Vector3 f = BikeRig.StumbleClamp(fast, cap, B, out bool clampedFast);
        Assert.True(clampedFast);
        Assert.Equal(cap * B.StumbleSpeedFraction, new Vector2(f.X, f.Z).Length(), 1e-3f);
        Assert.Equal(-2f, f.Y, 1e-4f);
        Assert.True(f.Z > 0f, "direction preserved");
    }

    [Fact]
    public void TheDriftKeepsMomentum_ShedsGentlyToTheFloor_AndNeverBelowIt()
    {
        float floor = BikeRig.RideCapMps(MotorTuning.Default, B) * B.DriftSpeedFloorFraction;
        var v = new Vector3(0f, 0f, 9f);
        float dt = 1f / 60f;
        float last = 9f;
        for (int i = 0; i < 240; i++)
        {
            v = BikeRig.Drift(v, Vector3.Zero, dt, floor, B);
            float speed = new Vector2(v.X, v.Z).Length();
            Assert.True(speed <= last + 1e-5f, $"tick {i}: speed rose {last} -> {speed}");
            Assert.True(speed >= floor - 1e-4f, $"tick {i}: fell under the floor {floor}");
            last = speed;
        }
        Assert.Equal(floor, last, 1e-3f);
        // Under the floor already: not slowed further.
        Vector3 slow = BikeRig.Drift(new Vector3(0f, 0f, floor * 0.5f), Vector3.Zero, dt, floor, B);
        Assert.Equal(floor * 0.5f, slow.Z, 1e-4f);
    }

    [Fact]
    public void TheDriftSwingsTheNoseTowardTheStickAtTheTunedRate()
    {
        var v = new Vector3(0f, 0f, 8f);            // heading +Z
        var wish = new Vector3(1f, 0f, 0f);           // stick hard right
        float dt = 0.05f;
        Vector3 after = BikeRig.Drift(v, wish, dt, 1f, B);
        float turned = Mathf.RadToDeg(new Vector2(0f, 1f).AngleTo(new Vector2(after.X, after.Z).Normalized()));
        Assert.Equal(B.DriftTurnDegPerSec * dt, Mathf.Abs(turned), 5e-2f);
        Assert.True(after.X > 0f, "swung toward the stick, not away");
        // And it is a tighter corner than the steering lerp: more than 90 degrees inside half a second.
        Assert.True(B.DriftTurnDegPerSec * 0.5f > 90f);
    }

    [Fact]
    public void TheHoldRampRunsJogToSprintAndClampsAtBothEnds()
    {
        Assert.Equal(B.HoldRampStartFraction, BikeRig.HoldRamp(0f, B), 1e-4f);
        Assert.Equal(B.HoldRampStartFraction, BikeRig.HoldRamp(-1f, B), 1e-4f);
        Assert.Equal(1f, BikeRig.HoldRamp(B.HoldRampSec, B), 1e-4f);
        Assert.Equal(1f, BikeRig.HoldRamp(B.HoldRampSec * 4f, B), 1e-4f);
        float prev = 0f;
        for (float t = 0f; t <= B.HoldRampSec; t += 0.05f)
        {
            float now = BikeRig.HoldRamp(t, B);
            Assert.True(now >= prev, $"ramp fell at {t}");
            prev = now;
        }
    }

    [Fact]
    public void TheJogIsWhereTheRampStarts()
    {
        MotorTuning foot = MotorTuning.Default;
        float sprint = foot.MoveSpeed * foot.SprintMultiplier;
        Assert.Equal(foot.MoveSpeed, sprint * B.HoldRampStartFraction, 5e-2f);
    }

    [Fact]
    public void TheUnfoldSpringStartsShut_OvershootsOnce_AndSettlesOpen()
    {
        Assert.Equal(0f, BikeRig.UnfoldSpring(0f, B), 1e-5f);
        Assert.Equal(1f, BikeRig.UnfoldSpring(1f, B), 1e-5f);
        Assert.Equal(1f, BikeRig.UnfoldSpring(3f, B), 1e-5f);
        float peak = 0f;
        for (float t = 0f; t <= 1f; t += 0.01f)
            peak = Mathf.Max(peak, BikeRig.UnfoldSpring(t, B));
        Assert.True(peak > 1.05f, $"no overshoot: peak {peak}");
        Assert.Equal(B.UnfoldOvershoot, peak, 2e-2f);
    }

    [Fact]
    public void TheHeadingIsTheVelocityWhileThereIsOne_OtherwiseTheFacing()
    {
        Vector3 facing = new Vector3(0f, 0f, -1f);
        Assert.Equal(new Vector3(1f, 0f, 0f), BikeRig.Heading(new Vector3(5f, -3f, 0f), facing));
        Assert.Equal(facing, BikeRig.Heading(new Vector3(0.1f, -3f, 0f), facing));
        Vector3 h = BikeRig.Heading(Vector3.Zero, new Vector3(0f, 0.7f, -0.7f));
        Assert.Equal(0f, h.Y, 1e-5f);
        Assert.Equal(1f, h.Length(), 1e-5f);
    }

    [Fact]
    public void TheSlopeBonusClimbsDownhill_BleedsOnTheFlat_AndIsCappedAndNeverNegative()
    {
        float dt = 1f / 60f;
        float sin12 = Mathf.Sin(Mathf.DegToRad(12f));
        float bonus = 0f;
        for (int i = 0; i < 60; i++)
            bonus = BikeRig.SlopeBonusStep(bonus, sin12, dt, B);
        float expected = (AvatarMotor.Gravity * sin12 * B.RideSlopeGain - B.RideDeceleration) * 1f;
        Assert.Equal(expected, bonus, 5e-2f);
        Assert.True(bonus > 0f);

        float flat = bonus;
        for (int i = 0; i < 60; i++)
            flat = BikeRig.SlopeBonusStep(flat, 0f, dt, B);
        Assert.Equal(Mathf.Max(bonus - B.RideDeceleration, 0f), flat, 5e-2f);

        Assert.Equal(0f, BikeRig.SlopeBonusStep(0f, -0.5f, dt, B), 1e-5f);        // uphill from zero stays zero
        Assert.Equal(B.SlopeBonusMaxMps, BikeRig.SlopeBonusStep(B.SlopeBonusMaxMps, 1f, dt, B), 1e-5f);
    }

    [Fact]
    public void TheSlopeBonusLandsInTheWish_ScaledByTheBlend()
    {
        MotorTuning foot = MotorTuning.Default;
        MotorTuning full = BikeRig.RideWithBonus(foot, B, 1f, 3.2f);
        Assert.Equal(BikeRig.Ride(foot, B).MoveSpeed + 3.2f / foot.SprintMultiplier, full.MoveSpeed, 1e-4f);
        Assert.Equal(foot, BikeRig.RideWithBonus(foot, B, 0f, 3.2f));
        MotorTuning seated = MotorTuning.Validate(BikeRig.RideWithBonus(foot, B, 1f, B.SlopeBonusMaxMps), out _);
        Assert.Equal(BikeRig.RideWithBonus(foot, B, 1f, B.SlopeBonusMaxMps).MoveSpeed, seated.MoveSpeed, 1e-4f);
    }

    /// <summary>The bike layer restores the foot tuning through <c>TryApply</c>, i.e. through
    /// <c>Validate</c> a second time. If validating a validated tuning moved a bit, the lab's
    /// "restored exactly" check could never pass — so this pins whether it does, per row, with
    /// the exact delta named.</summary>
    [Fact]
    public void ValidatingAValidatedTuningMovesNoBit()
    {
        MotorTuning once = MotorTuning.Validate(MotorTuning.Default, out _);
        MotorTuning twice = MotorTuning.Validate(once, out _);
        var moved = MotorTuningKnobs.All
            .Where(k => k.Get(once) != k.Get(twice))
            .Select(k => $"{k.Name}: {k.Get(once):R} -> {k.Get(twice):R}")
            .ToList();
        Assert.True(moved.Count == 0, "Validate is not idempotent on: " + string.Join("; ", moved));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void TheDefaultsAreSane()
    {
        Assert.True(B.LandDismountWindowSec > 0f && B.LandDismountWindowSec < 0.5f);
        Assert.True(B.StumbleEnabled, "the stumble opens ON so it can be felt, then switched off");
        Assert.True(B.StumbleSteerFraction > 0f, "a stumble is not a lockout");
        Assert.True(B.StumbleSpeedFraction < 1f, "the stumble must cost something");
        Assert.True(B.DriftDecel > B.RideDeceleration && B.DriftDecel < 13f, "a drift is a corner, not the foot skid");
        Assert.True(B.DriftSpeedFloorFraction > 0f && B.DriftSpeedFloorFraction < 1f);
        Assert.True(B.SlideJumpMul > 1f);
        Assert.True(B.AirMountUpMps > 0f && B.LandDismountUpMps > 0f && B.MountHopMps > 0f);
        Assert.True(B.MountBlendSec > 0f && B.UnfoldSec > 0f && B.FoldSec > 0f);
    }
}
