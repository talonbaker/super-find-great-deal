using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Light;

/// <summary>
/// The flashlight's local-input seam: one <c>F</c> press becomes one
/// <see cref="FlashlightManager.ClientRequestToggle"/>. Nothing else. It holds no state, decides
/// nothing, and cannot light anything — it asks, and the server answers. Deliberately the same
/// shape as every other one-key world verb here, because a second shape would be a second set of
/// edge cases (the captured-mouse rule, the once-per-press rule) to keep in sync.
///
/// <para><b>The key is declared into the live <c>InputMap</c> at runtime, not into
/// <c>project.godot</c></b>, and that is a deliberate call rather than a scope dodge. The
/// precedent and its reasoning are <see cref="Sandbox.LocalInputIntentSource.EnsureWalkAction"/>'s,
/// verbatim: <see cref="EnsureAction"/> defers to the file, so if <c>project.godot</c> ever
/// declares <c>flashlight_toggle</c> this touches nothing and a player's rebind survives. It also
/// keeps this packet out of a file two other packets are editing this wave, and — the reason that
/// matters most here — the standing repo rule against writing <c>ui_*</c> actions into
/// <c>project.godot</c> is easiest to keep by not opening the <c>[input]</c> block at all.</para>
///
/// <para><b>Why <c>F</c> is free.</b> Measured, not assumed:
/// <c>LocalInputIntentSource.WalkFallbackKey</c>'s own doc keeps the taken-key list and records
/// that SWING-2 moved the swing onto the primary button and deleted the <c>net_swing</c> action —
/// *"F is no longer in that list … F is free again"*. The rest of the letters were spoken for
/// at the time (<c>E</c> interact plus the since-removed item verbs), Shift is sprint and Left
/// Ctrl is walk. <c>F</c> is also what Talon named in note 10, so
/// it is the binding and the toast agree by construction — the hint the player reads and the key
/// that works are the same fact.</para>
///
/// <para><b>Local only.</b> This is a client-side input reader; a headless bot drives the same verb
/// by calling <see cref="FlashlightManager.ServerToggle"/> server-side, which is what a scene test
/// does. That split is why nothing here needs a peer id.</para>
/// </summary>
public partial class FlashlightController : Node
{
    public const string NodeName = "FlashlightController";

    /// <summary>The action name. Public so a test and a future rebind screen name the same
    /// string.</summary>
    public const string ActionName = "flashlight_toggle";

    /// <summary><c>F</c>. Physical, not a keycode: the same choice every action in
    /// <c>project.godot</c> makes, so an AZERTY or Dvorak player presses the key in the position
    /// the toast is talking about.</summary>
    public const Key FallbackKey = Key.F;

    /// <summary>Gamepad: the right stick click. The conventional "personal utility toggle" slot on
    /// a pad and the only face/stick button in this project's map that is not already spoken for
    /// (<c>LocalInputIntentSource.WalkFallbackButton</c> holds the LEFT stick).</summary>
    public const JoyButton FallbackButton = JoyButton.RightStick;

    /// <summary>Declare <see cref="ActionName"/> if nothing already has. Idempotent, and it never
    /// overwrites an existing binding — see the class doc for why the action is declared here
    /// rather than in <c>project.godot</c>.</summary>
    public static void EnsureAction()
    {
        if (InputMap.HasAction(ActionName))
            return;
        InputMap.AddAction(ActionName);
        InputMap.ActionAddEvent(ActionName, new InputEventKey { PhysicalKeycode = FallbackKey });
        InputMap.ActionAddEvent(ActionName, new InputEventJoypadButton { ButtonIndex = FallbackButton });
    }

    /// <summary>Attach to a scene root. No-op headless (a dedicated server reads no keyboard) and
    /// no-op if one already exists.</summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance is { IsHeadless: true })
            return;
        if (sceneRoot.GetNodeOrNull(NodeName) != null)
            return;
        EnsureAction();
        sceneRoot.AddChild(new FlashlightController { Name = NodeName });
    }

    /// <summary>Polled in <c>_Process</c> and deliberately NOT in <c>_PhysicsProcess</c>:
    /// <c>IsActionJustPressed</c> stays true for the whole frame it fired in, so reading it from
    /// both would send two requests for one press — and this verb is a <i>toggle</i>, so a double
    /// read does not merely waste a packet, it lands back where it started and the light appears
    /// not to work at all. That is the whole reason it is read in exactly one place.
    ///
    /// The captured-mouse guard: a released mouse means UI focus, and a menu keystroke must never
    /// reach a world verb.</summary>
    public override void _Process(double delta)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        if (!InputMap.HasAction(ActionName) || !Input.IsActionJustPressed(ActionName))
            return;
        FlashlightManager.Instance?.ClientRequestToggle();
    }
}
