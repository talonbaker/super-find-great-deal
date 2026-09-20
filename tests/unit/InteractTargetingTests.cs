using System.Collections.Generic;
using Godot;
using MpFoundation.Game;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The one rule for "what does E act on", via the engine-free core overload (the Camera3D
/// overload only resolves the camera transform and delegates). Pins the contract its class
/// doc promises: reach gates everything; under an aim, the aimed candidate beats a nearer
/// unaimed one (the door-steals-the-press playtest bug); with no aim — or nothing in reach
/// under the aim — nearest-in-reach wins; and the pick is deterministic so the thing that
/// shimmers IS the thing the key acts on.
/// </summary>
public class InteractTargetingTests
{
    private static InteractTargeting.Candidate C(string name, Vector3 focus, float range = 5f) =>
        new(name, focus, range);

    private static readonly Vector3 Avatar = Vector3.Zero;
    private static readonly Vector3 Cam = new(0, 1.5f, 0);
    private static readonly Vector3 Forward = new(0, 0, -1); // camera looks toward -Z

    private static object? PickAimed(params InteractTargeting.Candidate[] cs) =>
        InteractTargeting.Pick(cs, Avatar, Cam, Forward, aimed: true);

    private static object? PickBlind(params InteractTargeting.Candidate[] cs) =>
        InteractTargeting.Pick(cs, Avatar, Cam, Forward, aimed: false);

    [Fact]
    public void NoCandidates_ReturnsNull()
    {
        Assert.Null(PickBlind());
        Assert.Null(PickAimed());
    }

    [Fact]
    public void OutOfRange_IsNeverPicked_EvenDeadUnderTheAim()
    {
        // Dead ahead of the camera, but 10 m away with a 5 m reach.
        Assert.Null(PickAimed(C("far", new Vector3(0, 1.5f, -10f))));
    }

    [Fact]
    public void NoAim_NearestInReachWins()
    {
        object? winner = PickBlind(
            C("near", new Vector3(1f, 0, 0)),
            C("farther", new Vector3(3f, 0, 0)));
        Assert.Equal("near", winner);
    }

    [Fact]
    public void UnderAim_AimedCandidateBeatsNearerUnaimedOne()
    {
        // The playtest bug this rule killed: the door behind you must not steal the press
        // from the box you are looking at.
        object? winner = PickAimed(
            C("door-behind", new Vector3(0, 1.5f, 2f)),   // nearer, but behind the camera
            C("box-ahead", new Vector3(0, 1.5f, -3f)));   // dead ahead
        Assert.Equal("box-ahead", winner);
    }

    [Fact]
    public void BehindTheCamera_CanNeverWinTheAim_FallsBackToNearest()
    {
        // Only one candidate, in reach but behind the camera: the aim finds nothing and
        // the nearest-in-reach fallback still returns it.
        object? winner = PickAimed(C("behind", new Vector3(0, 1.5f, 2f)));
        Assert.Equal("behind", winner);
    }

    [Fact]
    public void OutsideTheAimCone_DoesNotCountAsAimed()
    {
        // ~90° off the camera forward: inside reach, far outside the ~55° half-angle cone.
        // The nearer on-axis candidate is also present, so the cone loser must not win.
        object? winner = PickAimed(
            C("side", new Vector3(3f, 1.5f, 0)),
            C("ahead", new Vector3(0, 1.5f, -4f)));
        Assert.Equal("ahead", winner);
    }

    [Fact]
    public void TwoAimedCandidates_TheOneClosestToTheCrosshairWins()
    {
        object? winner = PickAimed(
            C("slightly-off", new Vector3(1f, 1.5f, -4f)),
            C("dead-center", new Vector3(0, 1.5f, -4f)));
        Assert.Equal("dead-center", winner);
    }

    [Fact]
    public void DegenerateFocusAtTheCamera_IsSkippedByTheAim_NotACrash()
    {
        object? winner = PickAimed(
            C("at-camera", Cam),
            C("ahead", new Vector3(0, 1.5f, -3f)));
        Assert.Equal("ahead", winner);
    }

    [Fact]
    public void SameInputsTwice_SameWinner_HighlightAndPressAgree()
    {
        var cs = new List<InteractTargeting.Candidate>
        {
            C("a", new Vector3(1f, 0, -1f)),
            C("b", new Vector3(0, 1.5f, -2f)),
            C("c", new Vector3(-2f, 0, 1f)),
        };
        object? first = InteractTargeting.Pick(cs, Avatar, Cam, Forward, aimed: true);
        object? second = InteractTargeting.Pick(cs, Avatar, Cam, Forward, aimed: true);
        Assert.NotNull(first);
        Assert.Same(first, second);
    }
}
