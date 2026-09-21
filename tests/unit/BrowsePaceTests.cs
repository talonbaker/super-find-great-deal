using MpFoundation.Game;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// FEEL-1 (2026-09-20): <b>this game's own walking speed</b>, and the fact that it is applied
/// through <see cref="MotorTuning.TryApply"/> rather than by moving the foundation's
/// <see cref="MotorTuning.Default"/>.
///
/// <para>Talon, after his first ride: <i>"slow down the players' movement overall; no sprint
/// button; they won't be moving far enough for it to matter; keep jumping."</i></para>
///
/// <para><b>Why the seam and not the default.</b> <c>MotorTuning.Default</c> is MOVE-8's ruling —
/// Talon at a keyboard on 2026-08-28, thirteen rows, with roughly thirty tests pinning the arc,
/// the skid distances, the gear ladder and the wind curve that were MEASURED at it. Editing it
/// would not slow this game down so much as delete that record. <c>Current</c> is what the motor
/// reads and <c>TryApply</c> is its single writer — the seam MOVE-4b shipped for exactly this —
/// so the supermarket states its own body and the foundation keeps its.</para>
/// </summary>
public class BrowsePaceTests
{
    [Fact]
    public void TheBrowsePace_IsTheFoundationsBody_WithThreeRowsMoved()
    {
        MotorTuning browse = BrowsePace.Tuning(BrowsePace.DefaultWalkSpeedMps);
        MotorTuning baseline = MotorTuning.Default;

        Assert.Equal(BrowsePace.DefaultWalkSpeedMps, browse.MoveSpeed);
        Assert.Equal(1f, browse.SprintMultiplier);
        Assert.Equal(BrowsePace.BrowseAccelerationMps2, browse.Acceleration);
        // Everything else is the foundation's, untouched — asserted as the whole record rather
        // than row by row, so a future edit that quietly moves a fourth row is a red.
        Assert.Equal(
            baseline with
            {
                MoveSpeed = browse.MoveSpeed,
                SprintMultiplier = 1f,
                Acceleration = BrowsePace.BrowseAccelerationMps2,
            },
            browse);
    }

    /// <summary>
    /// INT-2 (2026-09-20), ruling 4, on SICK-1's finding 2: <b>the ramp to speed must not be
    /// slower than the ramp to a stop.</b> SICK-1 measured 0.42 s to reach speed against 0.18 s to
    /// shed it and called it "a body that is heavier than the player expects"; the packet's bar is
    /// 0.15 s.
    ///
    /// <para>Asserted as the ORDERING as well as the bar, because the bar alone is satisfied by a
    /// constant tuned for one top speed and <c>--walk-speed</c> moves the top speed. Both ends of
    /// the override window are checked for that reason.</para>
    /// </summary>
    [Theory]
    [InlineData(2.0f)]
    [InlineData(2.4f)]
    [InlineData(2.5f)]
    public void TheRampToSpeed_IsUnderTheBar_AndNeverSlowerThanTheStop(float walkSpeedMps)
    {
        MotorTuning browse = BrowsePace.Tuning(walkSpeedMps);
        float rampSec = walkSpeedMps / browse.Acceleration;
        float stopSec = walkSpeedMps / browse.Deceleration;

        Assert.True(rampSec <= 0.15f, $"ramp to speed is {rampSec:F3} s, bar 0.15 s");
        Assert.True(rampSec <= stopSec,
            $"ramp {rampSec:F3} s must not be slower than the stop {stopSec:F3} s");
    }

    /// <summary>The ramp is raised on THIS GAME's tuning and the foundation's signed-off row is
    /// untouched — MOVE-8's ruling, pinned in the knob table to a sprint-ramp bracket measured at
    /// 9. A red here means somebody moved the default instead of the game's body.</summary>
    [Fact]
    public void TheFoundationsOwnRamp_IsUnmoved()
    {
        Assert.Equal(9f, MotorTuning.Default.Acceleration);
        Assert.NotEqual(MotorTuning.Default.Acceleration, BrowsePace.BrowseAccelerationMps2);
    }

    [Fact]
    public void TheDefaultWalkSpeed_IsInsideTheBandTalonNamed()
    {
        Assert.InRange(BrowsePace.DefaultWalkSpeedMps, 2.0f, 2.5f);
    }

    [Fact]
    public void TheBrowsePace_IsSlowerThanTheFoundationsJog()
    {
        Assert.True(BrowsePace.DefaultWalkSpeedMps < MotorTuning.Default.MoveSpeed,
            "the whole point is that it is slower");
    }

    [Fact]
    public void SprintIsANoOp_SoNoSourceCanAskForOne()
    {
        MotorTuning browse = BrowsePace.Tuning(BrowsePace.DefaultWalkSpeedMps);
        Assert.Equal(browse.MoveSpeed, browse.MoveSpeed * browse.SprintMultiplier);
    }

    [Fact]
    public void TheWalkGear_FollowsTheBrowsePaceDown()
    {
        // The walk modifier is a FRACTION of the top speed, so it moves with it rather than
        // needing its own ruling.
        float walkGear = BrowsePace.DefaultWalkSpeedMps * MpFoundation.Game.Sandbox.LocomotionProfile.WalkFraction;
        Assert.InRange(walkGear, 0.9f, 1.3f);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(1000f)]
    [InlineData(float.NaN)]
    public void AnAbsurdOverride_IsRefusedRatherThanApplied(float asked)
    {
        // --walk-speed is a ride-session knob; a value the knob table would refuse must not reach
        // the motor, because a motor constant that differs between two peers is a desync.
        Assert.Equal(BrowsePace.DefaultWalkSpeedMps, BrowsePace.SanitizeWalkSpeed(asked));
    }

    [Fact]
    public void AReasonableOverride_IsKept()
    {
        Assert.Equal(2.0f, BrowsePace.SanitizeWalkSpeed(2.0f));
    }
}
