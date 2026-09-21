using Godot;
using MpFoundation.Game.Props;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The wheel moves what you are holding forward and back in space</b> (FEEL-1, 2026-09-20).
/// Talon's ask, in his own words: <i>"I'd like a scroll wheel to move the object forward and back
/// in space."</i>
///
/// <para><b>One node, on the local human player only</b>, for the same reason
/// <see cref="HeldPropRotator"/> is one: this reads a mouse, and <see cref="SandboxAvatar"/> is
/// fed exclusively by <see cref="IIntentSource"/> so that a bot, a replay and a person are
/// indistinguishable to it. A verb that reaches for <c>Input</c> directly does not belong in the
/// body.</para>
///
/// <para><b>Not on the wire, and that is the same call CARRY-1 made for the hold ROTATION.</b> The
/// distance is a fact about how the holder is looking at their own object; it reaches everybody
/// else exactly once, in the transform a PLACE sends. A peer watching a hider carry a can sees it
/// at the distance it was grabbed at — the documented mismatch, beside the spring's lag, and
/// cheaper than a field per tick for a cosmetic difference of a few centimetres.</para>
///
/// <para><b>Unconditional, unlike the rotate modifier.</b> The wheel used to give
/// <see cref="HeldPropRotator"/> coarse yaw steps while the right button was held. One input with
/// two meanings depending on a modifier is a rule a player has to be told rather than discover,
/// and the rotator's own gesture (right button + mouse) is unaffected by giving the wheel away.
/// The bindings are <c>hold_closer</c> / <c>hold_farther</c> in <c>project.godot</c> (wheel down /
/// wheel up, plus the d-pad) so a rebind works, and so the How-to-Play panel can render them.</para>
/// </summary>
public partial class HoldDistanceController : Node
{
    /// <summary>Move the held object one notch further from the eye.</summary>
    public const string FartherAction = "hold_farther";

    /// <summary>...and one notch closer. Closer than <c>CarryHold.HoldMinM</c> is refused by the
    /// clamp, never by this node: the band is a fact about the prop's bulk and the holder's
    /// capsule, and it is derived where those are known.</summary>
    public const string CloserAction = "hold_closer";

    private SandboxAvatar _avatar = null!;

    /// <summary>Builds one for a locally-controlled human player. No-op for anything else — a bot,
    /// a remote proxy and the server's own simulation all have no mouse and no screen.</summary>
    public static HoldDistanceController Attach(SandboxAvatar avatar)
    {
        var node = new HoldDistanceController { Name = nameof(HoldDistanceController), _avatar = avatar };
        avatar.AddChild(node);
        return node;
    }

    public override void _Input(InputEvent @event)
    {
        // Edges only. A wheel event is a press and a release a frame apart, and acting on both
        // would move the object two notches for one flick of the finger.
        int notches = 0;
        if (@event.IsActionPressed(FartherAction))
            notches = 1;
        else if (@event.IsActionPressed(CloserAction))
            notches = -1;
        if (notches == 0)
            return;

        // What is in the hand is server-authoritative, so it is asked rather than remembered —
        // the same discipline HeldPropRotator's poll follows, and for the same reasons (a
        // disconnect release, a round reset, a hold broken against a shelf).
        NetworkedProp? held = _avatar.Props?.FindHeldBy(_avatar.OwnerPeerId);
        if (held == null || !held.SpringActive)
            return;
        held.ScrollHold(notches);
        GetViewport().SetInputAsHandled();
    }
}
