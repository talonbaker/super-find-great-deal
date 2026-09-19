using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Honk;

/// <summary>
/// <b>The honk's local-input seam: one <c>H</c> press becomes one
/// <see cref="HonkManager.ClientRequestHonk"/>.</b> HONK-1, 2026-09-04. It holds no state, decides
/// nothing, and cannot make a sound — it asks, and the server answers. Deliberately the same shape
/// as <c>FlashlightController</c>, because a second shape would be a second set of edge cases (the
/// captured-mouse rule, the once-per-press rule) to keep in sync.
///
/// <para><b>The key is declared into the live <c>InputMap</c> at runtime, not into
/// <c>project.godot</c></b>, following <c>FlashlightController.EnsureAction</c> and, before it,
/// <c>LocalInputIntentSource.EnsureWalkAction</c>. <see cref="EnsureAction"/> defers to the file:
/// if <c>project.godot</c> ever declares <c>honk</c>, this touches nothing, and a player who has
/// rebound the key keeps their binding.</para>
///
/// <para><b>Why <c>H</c> is free — measured on this tip, not assumed.</b> Talon's shipped foreword
/// names <c>H</c>, so this had to be checked rather than chosen. Three places can claim a key and
/// all three were read:
/// <list type="number">
/// <item><c>project.godot</c>'s <c>[input]</c> block declares eleven keyboard actions, by PHYSICAL
/// keycode: <c>move_forward</c> 87 <b>W</b>, <c>move_back</c> 83 <b>S</b>, <c>move_left</c> 65
/// <b>A</b>, <c>move_right</c> 68 <b>D</b>, <c>pause</c> 4194305 <b>Escape</b>, <c>jump</c> 32
/// <b>Space</b>, <c>interact</c> 69 <b>E</b>, <c>throw</c> 81 <b>Q</b>, <c>voice_ptt</c> 86
/// <b>V</b>, <c>sprint</c> 4194325 <b>Shift</b>, plus the mouse/stick-only look/aim/fire actions.
/// <b>72 (H) appears nowhere in it.</b></item>
/// <item>The two actions declared at RUNTIME — the only other way an action exists here — are
/// <c>flashlight_toggle</c> on <b>F</b> and <c>walk</c> on <b>Left Ctrl</b>. A repo-wide search
/// for <c>InputMap.AddAction</c> returns those two call sites and no others.</item>
/// <item>Raw key polling, which bypasses the action map entirely: the only
/// <c>Input.IsPhysicalKeyPressed</c> calls in <c>scripts/</c> are <c>BikeLayer</c>'s three, and
/// one of them <b>is <c>Key.H</c></b> (<c>RampKey</c>, the hold-to-sprint toggle). <b>That is not
/// a collision and the packet's STOP condition is not met</b>: <c>BikeLayer</c> is constructed
/// only by <c>MovementPlayground</c>, which is <c>scenes/dev/MovementPlayground.tscn</c> — a
/// separate scene launched on its own, never a child of <c>Gameplay.tscn</c>, and one this node is
/// never attached to. The two verbs cannot be alive in the same process at the same time. It is
/// recorded here anyway because a future packet that networks the bike WOULD collide, and the
/// cheapest place to find that out is this paragraph.</item>
/// </list>
/// The doc comment on <c>LocalInputIntentSource.WalkFallbackKey</c> is the repo's designated home
/// for this list; on this tip it has been trimmed to a sentence ("Shift is <c>sprint</c>, Alt is
/// the window manager's on Windows, and the letter keys are the verbs (E interact)") and no longer
/// enumerates. The enumeration above is the measurement it used to carry.</para>
///
/// <para><b>Local only, and never a bot.</b> This is a client-side keyboard reader: it is not
/// attached at all on a headless peer, and a scripted bot drives the verb through
/// <c>HonkManager.ClientRequestHonk</c> directly (<c>--honk-at</c>) — the SAME method this calls,
/// so a test is evidence about the shipped verb rather than about a test hook.</para>
/// </summary>
public partial class HonkController : Node
{
    public const string NodeName = "HonkController";

    /// <summary><c>H</c>. Physical, not a keycode: every action in <c>project.godot</c> makes the
    /// same choice, so an AZERTY or Dvorak player presses the key in the position the foreword is
    /// talking about.</summary>
    public const Key FallbackKey = Key.H;

    /// <summary>Gamepad: the D-pad's up. A pad has no free face or stick button left
    /// (<c>LocalInputIntentSource.WalkFallbackButton</c> holds the left stick,
    /// <c>FlashlightController.FallbackButton</c> the right), and a social "shout" verb on the
    /// D-pad is the convention every squad shooter already trained players on.</summary>
    public const JoyButton FallbackButton = JoyButton.DpadUp;

    /// <summary>Declare <see cref="HonkConfig.ActionName"/> if nothing already has. Idempotent, and
    /// it never overwrites an existing binding — see the class doc for why the action is declared
    /// here rather than in <c>project.godot</c>.</summary>
    public static void EnsureAction()
    {
        if (InputMap.HasAction(HonkConfig.ActionName))
            return;
        InputMap.AddAction(HonkConfig.ActionName);
        InputMap.ActionAddEvent(HonkConfig.ActionName, new InputEventKey { PhysicalKeycode = FallbackKey });
        InputMap.ActionAddEvent(HonkConfig.ActionName, new InputEventJoypadButton { ButtonIndex = FallbackButton });
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
        sceneRoot.AddChild(new HonkController { Name = NodeName });
    }

    /// <summary>Polled in <c>_Process</c> and deliberately NOT in <c>_PhysicsProcess</c>:
    /// <c>IsActionJustPressed</c> stays true for the whole frame it fired in, so reading it from
    /// both would send two requests for one press. The second is harmless here (the cooldown eats
    /// it) — but a verb whose correctness depends on a downstream latch absorbing a bug is a verb
    /// that is one refactor away from being wrong, and <c>FlashlightController</c> already
    /// establishes reading in exactly one place as the shape.
    ///
    /// <para><b>The captured-mouse guard is what keeps a honk out of a text field.</b> A released
    /// mouse means UI focus in this game — a menu, the pause screen, the settings panel, a room
    /// code being typed — and a menu keystroke must never reach a world verb. Typing an <c>H</c>
    /// into the join-code box therefore cannot honk, and neither can a modal being open, without
    /// this class needing to know anything about focus.</para></summary>
    public override void _Process(double delta)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        if (!InputMap.HasAction(HonkConfig.ActionName) || !Input.IsActionJustPressed(HonkConfig.ActionName))
            return;
        HonkManager.Instance?.ClientRequestHonk();
    }
}
