using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// The floating interact-key icon: a small rounded chip showing whatever key is actually
/// bound to the <c>interact</c> action, hovering over the object the player can act on
/// right now. It renders ON TOP of the existing world-space shimmer, not instead of it —
/// the shimmer says "something's here", the chip says "here's the key", and together they
/// read better than either alone.
///
/// It computes nothing itself: the hosts' existing 0.1 s proximity polls (the same code
/// that drives every <c>Highlighted</c> boolean) hand it the current best target via
/// <see cref="SetTarget"/>, and each frame this projects that world position to the
/// screen through the live camera (<see cref="Camera3D.UnprojectPosition"/>). One Label
/// in one CanvasLayer; no textures, no focus, no input.
/// </summary>
public partial class InteractPrompt : CanvasLayer
{
    private static InteractPrompt? _instance;

    /// <summary>Suppression is shared by every world-anchored widget — see
    /// <see cref="WorldUi.Suppressed"/>. Kept as a forwarder so call sites that think in
    /// terms of the chip still read naturally; there is only ever one flag behind it.</summary>
    public static bool Suppressed
    {
        get => WorldUi.Suppressed;
        set => WorldUi.Suppressed = value;
    }

    private PanelContainer _chip = null!;
    private Label _key = null!;
    private Vector3 _targetWorld;
    private bool _hasTarget;
    private float _alpha;

    /// <summary>Adds the prompt to a scene if this peer renders. Call once per scene.</summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsHeadless)
            return;
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
            return;
        Suppressed = false; // a stale flag must not survive a scene change
        sceneRoot.AddChild(new InteractPrompt { Name = "InteractPrompt" });
    }

    /// <summary>The host's poll reports the current interactable's anchor position (the
    /// knob, the slot, the carryable's centre), or null when nothing is in reach.</summary>
    public static void SetTarget(Vector3? worldPos)
    {
        if (_instance == null || !GodotObject.IsInstanceValid(_instance))
            return;
        _instance._hasTarget = worldPos.HasValue;
        if (worldPos.HasValue)
            _instance._targetWorld = worldPos.Value;
    }

    public override void _Ready()
    {
        _instance = this;
        Layer = Design.UiLayers.InteractPrompt; // world furniture: under every menu and every state screen

        // The chip: a scrap taped over whatever the player is looking at, with a bright key
        // glyph on it. Shape+text carries the signal (never colour alone — Art Bible §3.4).
        // The plate is the shared HUD-scrap recipe rather than a hand-picked plate, so it is the
        // same paper as every other piece of chrome and re-lights with the temperature.
        _chip = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ThemeTypeVariation = "HudScrap",
        };
        _key = new Label
        {
            Text = InteractKeyLabel(),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ThemeTypeVariation = "Body",
        };
        _chip.AddChild(_key);
        _chip.Modulate = new Color(1, 1, 1, 0);
        AddChild(_chip);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public override void _Process(double delta)
    {
        Camera3D? cam = GetViewport()?.GetCamera3D();
        bool visible = !Suppressed && _hasTarget && cam != null
            && !cam!.IsPositionBehind(_targetWorld);

        _alpha = Mathf.MoveToward(_alpha, visible ? 1f : 0f, (float)delta * 6f);
        _chip.Modulate = new Color(1, 1, 1, _alpha);
        if (_alpha <= 0.001f || cam == null)
            return;

        // Hover a little above the anchor so the chip never covers the shimmer itself.
        Vector2 screen = cam.UnprojectPosition(_targetWorld + Vector3.Up * 0.22f);
        _chip.Position = screen - new Vector2(_chip.Size.X / 2f, _chip.Size.Y + 6f);
    }

    /// <summary>Whatever key the InputMap actually binds to <c>interact</c> right now —
    /// respects rebinds instead of hard-coding "E". Physical keycodes translate through
    /// the active layout so an AZERTY player sees their own key.</summary>
    private static string InteractKeyLabel()
    {
        foreach (InputEvent ev in InputMap.ActionGetEvents("interact"))
        {
            if (ev is not InputEventKey key)
                continue;
            Key code = key.Keycode != Key.None
                ? key.Keycode
                : DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode);
            string label = OS.GetKeycodeString(code);
            if (!string.IsNullOrEmpty(label))
                return label;
        }
        return "E";
    }
}
