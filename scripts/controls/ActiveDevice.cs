using Godot;

namespace MpFoundation.Controls;

/// <summary>
/// The autoload that watches every input event and keeps <see cref="Mode"/> pointing at whichever
/// kind of device the player is actually driving — keyboard/mouse or a gamepad — so the game can
/// switch itself over without ever asking, and without a settings toggle. Keyboard and mouse are
/// never taken away: this adds a mode, it removes no capability, and the mode flips back the
/// instant a real key or a real mouse gesture arrives.
///
/// Deliberately a thin adapter. Every actual RULE — the stick deadzone, the mouse-travel
/// threshold, presence-is-not-use, the unplug fallback — lives in <see cref="InputModeTracker"/>,
/// which is pure and therefore provable without a physical pad. All this class does is translate
/// <c>InputEvent</c>s into tracker calls and re-broadcast the transitions.
///
/// <b>Reading it.</b> <see cref="Mode"/>/<see cref="IsPad"/> are static, so anything can ask
/// without a node reference and without a scene tree — that is also the seam the unit tier uses.
/// To be told instead of asking, connect to <see cref="ChangedEventHandler"/> on
/// <see cref="Instance"/> (null only when no autoload is present, e.g. headless logic tests;
/// <see cref="Mode"/> stays readable either way).
///
/// <b>Cost.</b> Event-driven only: no <c>_Process</c>, no timer, no per-frame poll. An idle game
/// does nothing at all; a moving mouse costs a switch, a compare and two adds.
/// </summary>
public partial class ActiveDevice : Node
{
    private static readonly InputModeTracker Tracker = new();
    private static ActiveDevice? _instance;

    /// <summary>Emitted only on a real transition, never per event.</summary>
    [Signal]
    public delegate void ChangedEventHandler();

    /// <summary>The live autoload, for callers that want to subscribe to
    /// <see cref="ChangedEventHandler"/> rather than poll. Null when there is no scene tree.</summary>
    public static ActiveDevice? Instance => _instance;

    /// <summary>The kind of device in use right now. Starts at
    /// <see cref="InputMode.KeyboardMouse"/>.</summary>
    public static InputMode Mode => Tracker.Mode;

    /// <summary>True when a gamepad is the active device. The form the glyph layer wants.</summary>
    public static bool IsPad => Tracker.IsPad;

    public override void _Ready()
    {
        _instance = this;
        // Unplugging the pad you are holding has to fall back on its own: a player whose
        // controller just died cannot press the key that would otherwise be needed to flip back.
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
    }

    public override void _ExitTree()
    {
        Input.JoyConnectionChanged -= OnJoyConnectionChanged;
        if (_instance == this)
            _instance = null;
    }

    // _Input, not _UnhandledInput: a button the pause menu consumes for focus navigation is still
    // the player using a pad, and the mode has to know that. Nothing here marks the event handled
    // — this observes the stream, it never eats from it.
    public override void _Input(InputEvent @event)
    {
        bool changed = @event switch
        {
            // Presses only. A release is a stale echo of a press the tracker already saw, and on
            // a pad it is exactly what arrives as your thumb comes off a button you pressed
            // BEFORE switching devices. Echo is auto-repeat from a held key, not a new decision.
            InputEventKey key => key.Pressed && !key.Echo && Tracker.NotifyKeyPressed(),
            InputEventMouseButton mb => mb.Pressed && Tracker.NotifyMouseButtonPressed(),
            InputEventMouseMotion mm => Tracker.NotifyMouseMotion(
                mm.Relative.X, mm.Relative.Y, Time.GetTicksMsec() / 1000.0),
            InputEventJoypadButton jb => jb.Pressed && Tracker.NotifyPadButtonPressed(),
            InputEventJoypadMotion jm => Tracker.NotifyPadAxis(jm.AxisValue),
            _ => false,
        };

        if (changed)
            EmitSignal(SignalName.Changed);
    }

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        bool changed = connected
            ? Tracker.NotifyPadConnected()
            : Tracker.NotifyPadDisconnected(Input.GetConnectedJoypads().Count > 0);

        if (changed)
            EmitSignal(SignalName.Changed);
    }
}
