using System;
using System.Collections.Generic;
using System.Linq;
using MpFoundation.Game.Props;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>Every refusal says why, and every ordinal stays where it is</b> (CARRY-1).
///
/// <para>Two invariants that are cheap to state and expensive to lose. The first is this repo's
/// oldest interaction scar: a refused action that produces no explanation reads as an
/// unresponsive game, the player presses again, and the one in-world control this foundation ever
/// shipped failed a playtest for exactly that (INTERACTION-BIBLE §2/§3/§7). The second is a wire
/// contract: both denial enums ride their ORDINALS across the network, so a reorder silently
/// turns "somebody else got there first" into "your hands are full" on any peer built from a
/// different commit.</para>
/// </summary>
public class CarryRefusalTests
{
    private static IEnumerable<PropManager.GrabDenial> GrabReasons() =>
        Enum.GetValues<PropManager.GrabDenial>().Where(r => r != PropManager.GrabDenial.None);

    private static IEnumerable<PropManager.PlaceDenial> PlaceReasons() =>
        Enum.GetValues<PropManager.PlaceDenial>().Where(r => r != PropManager.PlaceDenial.None);

    [Fact]
    public void EveryGrabRefusal_HasItsOwnSentence()
    {
        var seen = new Dictionary<string, PropManager.GrabDenial>();
        foreach (PropManager.GrabDenial reason in GrabReasons())
        {
            string text = CarryRefusalText.For(reason);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{reason} refuses the player in silence");
            Assert.NotEqual(CarryRefusalText.Unknown, text);
            Assert.False(seen.ContainsKey(text),
                $"{reason} and {(seen.TryGetValue(text, out var other) ? other : default)} say the same thing, "
                + "so the player cannot tell which happened");
            seen[text] = reason;
        }
    }

    [Fact]
    public void EveryPlaceRefusal_HasItsOwnSentence()
    {
        var seen = new Dictionary<string, PropManager.PlaceDenial>();
        foreach (PropManager.PlaceDenial reason in PlaceReasons())
        {
            string text = CarryRefusalText.For(reason);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{reason} refuses the player in silence");
            Assert.NotEqual(CarryRefusalText.Unknown, text);
            // OutOfRange means the same thing to both verbs and deliberately shares its wording
            // with grab's; within ONE enum, two reasons sharing a sentence would be the defect.
            Assert.False(seen.ContainsKey(text),
                $"{reason} and {(seen.TryGetValue(text, out var other) ? other : default)} say the same thing");
            seen[text] = reason;
        }
    }

    [Fact]
    public void UnknownOrdinal_StillSaysSomething()
    {
        // An older client hearing a newer server's reason. It must not fall through to an empty
        // string: a bump with no words is the defect this whole file exists about.
        Assert.False(string.IsNullOrWhiteSpace(CarryRefusalText.For((PropManager.GrabDenial)99)));
        Assert.False(string.IsNullOrWhiteSpace(CarryRefusalText.For((PropManager.PlaceDenial)99)));
    }

    [Fact]
    public void GrabDenialOrdinals_ArePinned()
    {
        Assert.Equal(0, (int)PropManager.GrabDenial.None);
        Assert.Equal(1, (int)PropManager.GrabDenial.HandsFull);
        Assert.Equal(2, (int)PropManager.GrabDenial.OutOfRange);
        Assert.Equal(3, (int)PropManager.GrabDenial.Taken);
        Assert.Equal(4, (int)PropManager.GrabDenial.Gone);
        Assert.Equal(5, (int)PropManager.GrabDenial.AlreadyHeld);
    }

    [Fact]
    public void PlaceDenialOrdinals_ArePinned()
    {
        // tests/Run-PlaceTest.ps1 asserts on these exact numbers out of the bot JSONL, so a
        // reorder would turn that suite green-for-the-wrong-reason rather than red.
        Assert.Equal(0, (int)PropManager.PlaceDenial.None);
        Assert.Equal(1, (int)PropManager.PlaceDenial.NotHolding);
        Assert.Equal(2, (int)PropManager.PlaceDenial.OutOfRange);
        Assert.Equal(3, (int)PropManager.PlaceDenial.TooFarToPlace);
        Assert.Equal(4, (int)PropManager.PlaceDenial.DoesNotFitThere);
        Assert.Equal(5, (int)PropManager.PlaceDenial.OutsideRoom);
        Assert.Equal(6, (int)PropManager.PlaceDenial.Gone);
        Assert.Equal(7, (int)PropManager.PlaceDenial.NotAllowedHere);
    }

    [Fact]
    public void PlaceReach_IsSmallerThanTheAimProbe_AndSmallerThanGrabReach()
    {
        // Three numbers that are easy to conflate and mean different things:
        //   PlaceReachM      — how far the OBJECT may end up from the hand. Small: the verb is
        //                      "put it where I am holding it", and a generous value here is
        //                      telekinesis and, worse, a way to post the target through a shelf.
        //   PlaceAimProbeM   — how far ahead the eye looks for something to set it down ON.
        //   PickupRadius     — how far the BODY may be from the prop (plus the server's own
        //                      GrabRangeTolerance, which is latency slack, not extra reach).
        Assert.True(PropManager.PlaceReachM
            < MpFoundation.Game.Sandbox.SandboxAvatar.PlaceAimProbeM);
        Assert.True(PropManager.PlaceReachM < MpFoundation.Game.Sandbox.SandboxAvatar.PickupRadius);
    }

    [Fact]
    public void PlacementDecision_AllowIsTheIdentityCase()
    {
        var at = new Godot.Transform3D(Godot.Basis.Identity, new Godot.Vector3(1f, 2f, 3f));
        PlacementDecision allow = PlacementDecision.Allow(at);
        Assert.True(allow.Allowed);
        Assert.Equal(at, allow.Transform);
        Assert.Equal(PropManager.PlaceDenial.None, allow.Reason);

        PlacementDecision refuse = PlacementDecision.Refuse(PropManager.PlaceDenial.NotAllowedHere);
        Assert.False(refuse.Allowed);
        Assert.Equal(PropManager.PlaceDenial.NotAllowedHere, refuse.Reason);
    }

    [Fact]
    public void IntegrityVerdict_AllowedOnlyWhenNothingFaulted()
    {
        Assert.True(PlacementIntegrity.Verdict.Ok.Allowed);
        Assert.False(new PlacementIntegrity.Verdict(
            PlacementIntegrity.PlacementFault.Overlapping, 0.4f, "x").Allowed);
        Assert.False(new PlacementIntegrity.Verdict(
            PlacementIntegrity.PlacementFault.OutsideRoomBounds, 0f, "x").Allowed);
        // A verdict that cannot describe itself is a server log line nobody can act on.
        Assert.Contains("0.400", new PlacementIntegrity.Verdict(
            PlacementIntegrity.PlacementFault.Overlapping, 0.4f, "the wall").ToString());
    }
}
