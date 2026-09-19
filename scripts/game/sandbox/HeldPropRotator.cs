using Godot;
using MpFoundation.Game.Props;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>Turn the thing in your hands</b> — hold the rotate modifier (right mouse button by default)
/// and move the mouse to yaw and pitch it, or roll the wheel for coarse fixed steps. CARRY-1
/// packet item 2.
///
/// <para><b>Why a node of its own, attached only to the local human player.</b> This is the only
/// thing in the game that reads a mouse for something other than the camera, and it has to be able
/// to TAKE the mouse away from the camera while it is active. Putting that in
/// <see cref="SandboxAvatar"/> would mean the avatar — which is fed exclusively by
/// <see cref="IIntentSource"/>, deliberately, so that a bot, a replay and a person are
/// indistinguishable to it — suddenly reaching for <c>Input</c> directly. Putting it in the camera
/// would mean the camera knowing what a held prop is, and there is a camera being replaced this
/// week (FP-1). So: one small node, built beside the local player's camera, that reads input and
/// writes one basis.</para>
///
/// <para><b>It suppresses the look while it is turning, and that is not a nicety.</b> Right mouse
/// plus mouse motion would otherwise spin the object AND the camera, which reads as the object
/// being stuck to the view — the exact opposite of the thing the verb is for. The suppression is
/// done by consuming the event in <see cref="_Input"/>, which runs before
/// <c>_UnhandledInput</c> where <c>SandboxCamera</c> reads its mouse. Any camera that replaces it
/// keeps working as long as it also reads the mouse as UNHANDLED input, which is the Godot idiom
/// and what the current one does — flagged for INT-1 rather than assumed.</para>
///
/// <para><b>No new wire field.</b> The rotation lives on the holder's own machine and reaches
/// everybody else exactly once, in the transform a PLACE sends (see
/// <see cref="SandboxAvatar.HeldPropLocalRotation"/>). Drop and throw do not carry it, which is
/// the honest difference between setting a thing down and getting rid of it.</para>
/// </summary>
public partial class HeldPropRotator : Node
{
    /// <summary>The input action that means "I am turning what I am holding, not looking around".
    /// Bound to the right mouse button in <c>project.godot</c>, on the same physical button as
    /// <c>aim</c> — see the class doc in <c>ControlGlyphs</c> for why that is not a clash in this
    /// game.</summary>
    public const string RotateAction = "rotate_held";

    /// <summary>Radians of held-object rotation per pixel of mouse travel. Independent of the
    /// camera's own sensitivity on purpose: turning an object in your hands and turning your head
    /// are different gestures at different scales, and one slider driving both is how "the camera
    /// is too fast" becomes "I can't line the crate up".</summary>
    public const float MouseRadiansPerPixel = 0.010f;

    /// <summary>One wheel notch, degrees. A coarse, repeatable step for lining an object up
    /// squarely — the thing the analogue mouse is bad at. 15° divides 90° and 360° exactly, so
    /// six notches is a quarter turn and nobody has to eyeball it.</summary>
    public const float WheelStepDegrees = 15f;

    private SandboxAvatar _avatar = null!;
    private int _lastHeldPropId = -1;

    /// <summary>Builds one for a locally-controlled human player. No-op for anything else —
    /// a bot, a remote proxy and the server's own simulation all have no mouse and no screen.</summary>
    public static HeldPropRotator Attach(SandboxAvatar avatar)
    {
        var rotator = new HeldPropRotator { Name = nameof(HeldPropRotator), _avatar = avatar };
        avatar.AddChild(rotator);
        return rotator;
    }

    /// <summary>True while the player is holding the modifier AND actually has something to turn.
    /// Both halves matter: the modifier shares its button with <c>aim</c>, so grabbing the mouse
    /// away from the camera whenever it is down — including when the hands are empty — would
    /// silently break looking around.</summary>
    public bool Active => _avatar.Props?.FindHeldBy(_avatar.OwnerPeerId) != null
                          && Input.IsActionPressed(RotateAction);

    public override void _Process(double _)
    {
        // What is in the hand is server-authoritative and can change without this node being told
        // — a disconnect release, a consumed prop, a round reset. Polling the id is how the reset
        // below stays correct through every one of those without subscribing to any of them.
        int held = _avatar.Props?.FindHeldBy(_avatar.OwnerPeerId)?.PropId ?? -1;
        if (held == _lastHeldPropId)
            return;
        _lastHeldPropId = held;
        // A fresh thing in the hand starts the way it was authored, never at the angle the last
        // thing came out at.
        _avatar.HeldPropLocalRotation = Basis.Identity;
    }

    public override void _Input(InputEvent @event)
    {
        if (!Active)
            return;

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Yaw about the holder's own up, pitch about the holder's own right, and in THAT
            // order so a yawed object still pitches "toward me" rather than about some axis it
            // inherited three turns ago. Composing on the left keeps every step in the holder's
            // frame, which is the frame the player is thinking in.
            var yaw = new Basis(Vector3.Up, -motion.Relative.X * MouseRadiansPerPixel);
            var pitch = new Basis(Vector3.Right, -motion.Relative.Y * MouseRadiansPerPixel);
            _avatar.HeldPropLocalRotation =
                (yaw * pitch * _avatar.HeldPropLocalRotation).Orthonormalized();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true } button
            && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            float sign = button.ButtonIndex == MouseButton.WheelUp ? 1f : -1f;
            var step = new Basis(Vector3.Up, Mathf.DegToRad(WheelStepDegrees) * sign);
            _avatar.HeldPropLocalRotation = (step * _avatar.HeldPropLocalRotation).Orthonormalized();
            GetViewport().SetInputAsHandled();
        }
    }
}
