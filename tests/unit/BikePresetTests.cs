using System.Collections.Generic;
using System.Linq;
using MpFoundation.Dev.Playground;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The ALT-row bike presets, pinned</b> (BIKE-1b, 2026-09-01). A bike preset is a rewrite rule
/// over a foot tuning, and it is pressed over WHATEVER foot preset is loaded, so the claim to pin
/// is the cross product: every bike preset, over every foot preset in every bank, at five points
/// of the mount blend, with and without the slope bonus — the derived ride is inside every knob's
/// range and survives <c>MotorTuning.Validate</c> unchanged. Same law as
/// <c>MovementPresetTests</c> and <c>BikeRigTests</c>: what the banner names is what the motor
/// runs.
/// </summary>
public sealed class BikePresetTests
{
    public static IEnumerable<object[]> Pairs()
    {
        var feet = new List<(string, MotorTuning)> { ("shipped default", MotorTuning.Default) };
        feet.AddRange(MovementPresets.All.Select(p => ($"{MovementPresets.KeyLabel(p)} {p.Name}", p.Tuning)));
        foreach (var bp in BikePresets.All)
            foreach (var (name, foot) in feet)
                yield return new object[] { BikePresets.KeyLabel(bp) + " " + bp.Name, bp.Tuning, name, foot };
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryBikePresetOverEveryFootPreset_IsInsideEveryKnobsRange_AndValidatesUnchanged(
        string bikeName, BikeTuning bike, string footName, MotorTuning foot)
    {
        foreach (float blend in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        foreach (float bonus in new[] { 0f, bike.SlopeBonusMaxMps })
        {
            MotorTuning ride = BikeRig.RideWithBonus(foot, bike, blend, bonus);
            foreach (MotorKnob knob in MotorTuningKnobs.All)
            {
                float v = knob.Get(ride);
                Assert.True(v >= knob.Min && v <= knob.Max,
                    $"{bikeName} over {footName} at blend {blend}, bonus {bonus}: {knob.Name} = {v} "
                  + $"is outside [{knob.Min}, {knob.Max}]");
            }
            MotorTuning seated = MotorTuning.Validate(ride, out _);
            Assert.True(seated.Equals(ride),
                $"{bikeName} over {footName} at blend {blend}, bonus {bonus}: Validate changed a row");
        }
    }

    [Fact]
    public void TenPresets_OnDistinctDigits_AndAltZeroIsTheDefault()
    {
        Assert.Equal(10, BikePresets.All.Count);
        Assert.Equal(Enumerable.Range(0, 10), BikePresets.All.Select(p => p.Key).OrderBy(k => k));
        Assert.Equal(BikeTuning.Default, BikePresets.ForKey(0)!.Value.Tuning);
        Assert.Null(BikePresets.ForKey(10));
    }

    [Fact]
    public void EveryPresetSaysWhatToFeelAndWhereToFeelIt()
    {
        foreach (var p in BikePresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Feel), $"{p.Name} has no feel sentence");
            Assert.False(string.IsNullOrWhiteSpace(p.Try), $"{p.Name} has no try sentence");
            Assert.True(p.Try.Length > 30, $"{p.Name}'s try sentence is too short to act on");
        }
    }

    [Fact]
    public void ThePresetsAreALadder_NotTenCopiesOfTheDefault()
    {
        var distinct = BikePresets.All.Select(p => p.Tuning).Distinct().Count();
        Assert.Equal(10, distinct);
        // The brackets this file promises: 1 slower than 0 slower than 2; 6 costs more than 0
        // costs more than 5; 7 mounts faster than 0 mounts faster than 8.
        BikeTuning T(int k) => BikePresets.ForKey(k)!.Value.Tuning;
        Assert.True(T(1).RideSpeedMul < T(0).RideSpeedMul && T(0).RideSpeedMul < T(2).RideSpeedMul);
        Assert.False(T(5).StumbleEnabled);
        Assert.True(T(6).StumbleEnabled && T(6).StumbleSpeedFraction < T(0).StumbleSpeedFraction);
        Assert.True(T(7).MountBlendSec < T(0).MountBlendSec && T(0).MountBlendSec < T(8).MountBlendSec);
        Assert.True(T(9).RideSlopeGain > T(0).RideSlopeGain);
        Assert.True(T(3).RideAirJumpMul > 1f && T(3).KickOffUpMps > T(0).KickOffUpMps);
        Assert.True(T(4).DriftTurnDegPerSec > T(0).DriftTurnDegPerSec);
    }

    // --- the new arithmetic (BIKE-1b) ----------------------------------------------------------

    [Fact]
    public void TheKickOffSetsVerticalAndNeverBelowARiseAlreadyHad()
    {
        var b = BikeTuning.Default;
        var falling = BikeRig.KickOffBurst(new Godot.Vector3(3f, -5f, 4f), new Godot.Vector3(0.6f, 0f, 0.8f), b);
        Assert.Equal(b.KickOffUpMps, falling.Y, 1e-5f);
        Assert.Equal(3f + 0.6f * b.KickOffForwardMps, falling.X, 1e-5f);
        var rising = BikeRig.KickOffBurst(new Godot.Vector3(0f, 9f, 0f), Godot.Vector3.Forward, b);
        Assert.Equal(9f, rising.Y, 1e-5f);
    }

    [Fact]
    public void TheRampLaunchConvertsSpeedToRise_OnlyOffAnUphillLip()
    {
        var b = BikeTuning.Default with { RampLaunchGain = 1f };
        var v = new Godot.Vector3(0f, 0f, 9.4f);
        float sin20 = Godot.Mathf.Sin(Godot.Mathf.DegToRad(20f));
        Assert.Equal(9.4f * sin20, BikeRig.RampLaunch(v, sin20, b).Y, 1e-4f);
        Assert.Equal(0f, BikeRig.RampLaunch(v, -sin20, b).Y);                   // a downhill lip
        Assert.Equal(0f, BikeRig.RampLaunch(v, 0f, b).Y);                       // a flat edge
        Assert.Equal(0f, BikeRig.RampLaunch(v, sin20, b with { RampLaunchGain = 0f }).Y);
        Assert.Equal(8.4f, BikeRig.RampLaunch(new Godot.Vector3(0f, 8.4f, 9.4f), sin20, b).Y); // a jump keeps its own
    }

    [Fact]
    public void TimeToFloor_IsTheFallSolvedForwards()
    {
        // From rest, 1.47 m under 24 m/s^2: t = sqrt(2h/g).
        Assert.Equal(Godot.Mathf.Sqrt(2f * 1.47f / 24f), BikeRig.TimeToFloor(0f, 1.47f, 24f), 1e-5f);
        // Falling at 6 m/s toward a floor 0.5 m down: h = 6t + 12t^2 -> t = 0.0747.
        float t = BikeRig.TimeToFloor(-6f, 0.5f, 24f);
        Assert.Equal(0.5f, 6f * t + 12f * t * t, 1e-4f);
        Assert.Equal(0f, BikeRig.TimeToFloor(-6f, 0f, 24f));
        // Rising takes longer than falling from the same height.
        Assert.True(BikeRig.TimeToFloor(3f, 1f, 24f) > BikeRig.TimeToFloor(-3f, 1f, 24f));
    }

    [Fact]
    public void TheRideCarriesTheJumpRows_ScaledByTheirMultipliers()
    {
        var foot = MotorTuning.Default;
        var b = BikeTuning.Default with { RideJumpMul = 1.2f, RideAirJumpMul = 1.3f };
        var ride = BikeRig.Ride(foot, b, 1f);
        Assert.Equal(foot.JumpVelocity * 1.2f, ride.JumpVelocity, 1e-5f);
        Assert.Equal(foot.AirJumpVelocityFraction * 1.3f, ride.AirJumpVelocityFraction, 1e-5f);
        Assert.Equal(foot.JumpVelocity, BikeRig.Ride(foot, b, 0f).JumpVelocity);
    }

    // --- the swing (BIKE-1c) ----------------------------------------------------------------------

    [Fact]
    public void TheSwingLunge_AddsForwardAndLiftsToAtLeastTheTunedRise()
    {
        var b = BikeTuning.Default;
        var v = BikeRig.SwingLunge(new Godot.Vector3(0f, -4f, 5f), Godot.Vector3.Back, b);
        Assert.Equal(5f + b.SwingLungeForwardMps, v.Z, 1e-5f);
        Assert.Equal(b.SwingLungeUpMps, v.Y, 1e-5f);
        Assert.Equal(6f, BikeRig.SwingLunge(new Godot.Vector3(0f, 6f, 0f), Godot.Vector3.Back, b).Y, 1e-5f);
    }

    [Fact]
    public void TheSlingshot_IsTheAirMountBurstPlusTheSwingsMomentum()
    {
        var b = BikeTuning.Default;
        var v0 = new Godot.Vector3(0f, -3f, 6f);
        var plain = BikeRig.AirMountBurst(v0, Godot.Vector3.Back, b);
        var sling = BikeRig.Slingshot(v0, Godot.Vector3.Back, b);
        Assert.Equal(plain.Y, sling.Y, 1e-5f);
        Assert.Equal(plain.Z + b.SlingshotForwardMps, sling.Z, 1e-5f);
    }

    [Fact]
    public void TheSwingOffset_StartsAndEndsOnTheBack_AndPeaksInFrontAtFullReach()
    {
        var b = BikeTuning.Default;
        Godot.Vector2 start = BikeRig.SwingOffset(0f, b), end = BikeRig.SwingOffset(1f, b), mid = BikeRig.SwingOffset(0.5f, b);
        Assert.Equal(0f, start.X, 1e-5f); Assert.True(start.Y < 0f);                    // behind
        Assert.Equal(start.X, end.X, 1e-4f); Assert.Equal(start.Y, end.Y, 1e-4f);      // back where it began
        Assert.Equal(b.SwingReachM, mid.Y, 1e-4f); Assert.Equal(0f, mid.X, 1e-4f);      // in front, at reach
        Assert.True(BikeRig.SwingOffset(0.25f, b).X > 0f);                              // past the right hip first
    }

    [Fact]
    public void TheSlopeBonusNeverPushesTheWishPastTheKnobCeiling()
    {
        var fast = MotorTuning.Default with { MoveSpeed = 11f };
        var b = BikeTuning.Default with { RideSpeedMul = 1.9f, SlopeBonusMaxMps = 8f };
        Assert.Equal(MotorTuningKnobs.MoveSpeed.Max, BikeRig.RideWithBonus(fast, b, 1f, 8f).MoveSpeed);
    }
}
