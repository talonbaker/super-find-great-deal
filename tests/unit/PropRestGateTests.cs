using Godot;
using MpFoundation.Game.Props;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PHYS-1 (2026-09-20), ruling P3: <b>the rest audit never teleports a prop the player is looking
/// at moving.</b>
///
/// <para>The audit is one of the things that can produce the freakout Talon described, and the
/// two halves of the gate are different failures. The CLOCK stops the audit undoing a separation
/// the solver was about to finish; the DISTANCE stops it doing anything visible in front of the
/// person who caused it. Either alone leaves the other case shipping.</para>
/// </summary>
public class PropRestGateTests
{

    /// <summary>xunit 2.4's <c>Assert.Equal(float, float, int)</c> is ambiguous against its
    /// (double, double, int) overload, so every comparison in this file goes through one
    /// widening helper rather than a cast at each call site.</summary>
    private static void Near(float expected, float actual, int digits) =>
        Assert.Equal((double)expected, (double)actual, digits);
    private const float Far = 10f;

    [Fact]
    public void FreshlyStuck_WaitsEvenInAnEmptyRoom()
    {
        Assert.False(PropRestGate.MayRestoreLastGood(stuckSec: 0f, nearestGrabRayM: Far));
        Assert.False(PropRestGate.MayRestoreLastGood(
            PropRestGate.StuckHoldSec - 0.01f, Far));
    }

    [Fact]
    public void StuckLongEnoughAndNobodyNear_Restores()
    {
        Assert.True(PropRestGate.MayRestoreLastGood(PropRestGate.StuckHoldSec, Far));
        Assert.True(PropRestGate.MayRestoreLastGood(5f, Far));
    }

    /// <summary>The ruling's own sentence: a snap-back in front of your eyes is the freakout, so
    /// however long it has been stuck, a prop a player is aimed at is logged and left.</summary>
    [Fact]
    public void StuckForever_ButAPlayerIsAimedAtIt_StillWaits()
    {
        Assert.False(PropRestGate.MayRestoreLastGood(30f, 0.5f));
        Assert.False(PropRestGate.MayRestoreLastGood(30f, PropRestGate.PlayerAttentionM));
    }

    [Fact]
    public void JustPastTheAttentionRadius_Restores() =>
        Assert.True(PropRestGate.MayRestoreLastGood(
            PropRestGate.StuckHoldSec, PropRestGate.PlayerAttentionM + 0.01f));

    /// <summary><b>An empty room must read as FAR, never as zero.</b> The seeker's whole round
    /// happens in a room the hider has left; if "no avatars" collapsed to distance 0 the audit
    /// would never correct anything there, which is precisely where correcting is safe.</summary>
    [Fact]
    public void NoAvatarsAtAll_IsFarAway() =>
        Assert.True(PropRestGate.MayRestoreLastGood(
            PropRestGate.StuckHoldSec, float.PositiveInfinity));

    // --- the grab ray ------------------------------------------------------------------------

    [Fact]
    public void APropStraightAheadIsOnTheRay()
    {
        float d = PropRestGate.DistanceToGrabRay(
            point: new Vector3(0f, 0f, -1.5f), eye: Vector3.Zero,
            aimDirection: new Vector3(0f, 0f, -1f), rangeM: 2.25f);
        Near(0f, d, 3);
    }

    /// <summary><b>A SEGMENT, not an infinite line.</b> A prop directly BEHIND the player is on
    /// the line through their eye and is not something they are looking at.</summary>
    [Fact]
    public void APropBehindThePlayerIsNotOnTheRay()
    {
        float d = PropRestGate.DistanceToGrabRay(
            new Vector3(0f, 0f, 4f), Vector3.Zero, new Vector3(0f, 0f, -1f), 2.25f);
        Near(4f, d, 3);
    }

    [Fact]
    public void APropPastTheEndOfTheRayIsMeasuredFromTheEnd()
    {
        float d = PropRestGate.DistanceToGrabRay(
            new Vector3(0f, 0f, -10f), Vector3.Zero, new Vector3(0f, 0f, -1f), rangeM: 2.25f);
        Near(7.75f, d, 3);
    }

    [Fact]
    public void TheDirectionNeedNotBeNormalised()
    {
        float d = PropRestGate.DistanceToGrabRay(
            new Vector3(1f, 0f, -1.5f), Vector3.Zero, new Vector3(0f, 0f, -17f), 2.25f);
        Near(1f, d, 3);
    }

    [Fact]
    public void ADegenerateAimFallsBackToTheEyeDistance()
    {
        float d = PropRestGate.DistanceToGrabRay(
            new Vector3(0f, 0f, -3f), Vector3.Zero, Vector3.Zero, 2.25f);
        Near(3f, d, 3);
    }

    /// <summary>The two constants are a pair and their relationship is the ruling: the attention
    /// radius has to sit inside the reach a player actually has, or the gate would be refusing
    /// corrections to props no player could possibly be manipulating. The grab range is
    /// <c>PickupRadius</c> + <c>PropManager.GrabRangeTolerance</c> (0.75 m).</summary>
    [Fact]
    public void TheAttentionRadiusIsInsideTheGrabRange() =>
        Assert.True(PropRestGate.PlayerAttentionM
            <= MpFoundation.Game.Sandbox.SandboxAvatar.PickupRadius + 0.75f);
}
