namespace MpFoundation.Controls;

/// <summary>Which KIND of device the player is actually driving the game with right now.
/// Deliberately a kind, not a device id — nothing downstream needs to know WHICH pad spoke,
/// only whether to read the world as keyboard-and-mouse or as a gamepad.</summary>
public enum InputMode
{
    /// <summary>Keyboard and mouse. The startup default, and the mode this project has always
    /// supported — auto-detect only ever adds the other one, it never takes this away.</summary>
    KeyboardMouse,

    /// <summary>A gamepad. Only ever entered by a deliberate press or a real stick push — never
    /// by a pad merely being plugged in (see <see cref="InputModeTracker.NotifyPadConnected"/>).</summary>
    Gamepad,
}

/// <summary>
/// The auto-detect state machine: two modes, and the rules for what flips between them.
/// Pure — no Node, no <c>Godot.Input</c>, no scene tree, no hardware — so every rule below is
/// provable in the fast engine-free test tier (<c>tests/unit/InputModeTrackerTests.cs</c>,
/// same tier as <c>AimControllerTests</c>) rather than needing a physical pad in someone's hands.
/// <see cref="ActiveDevice"/> is the thin adapter that feeds real <c>InputEvent</c>s in.
///
/// <b>The two failure modes this exists to prevent</b>, both of which are the whole difficulty
/// of the feature and neither of which a naive "last event wins" tracker survives:
/// <list type="number">
/// <item>A drifting or jittering mouse stealing the mode back from a pad mid-session. Guarded by
/// <see cref="MouseTravelThresholdPx"/> — a MEASURED amount of travel, not a single event.</item>
/// <item>A worn stick resting off-centre stealing the mode from the keyboard while someone types.
/// Guarded by <see cref="StickActivationDeadzone"/>, deliberately stricter than the input map's
/// own look deadzone.</item>
/// </list>
///
/// <b>Cost:</b> nothing here runs per frame. There is no timer and no <c>_Process</c> — the
/// mouse-travel window is evaluated lazily from the timestamp handed in with each motion event,
/// so an idle game does exactly zero work and a moving mouse costs a compare and two adds.
/// </summary>
public sealed class InputModeTracker
{
    /// <summary>How far a stick or trigger must be pushed before it counts as the player
    /// CHOOSING the pad. Deliberately well above the input map's own <c>look_*</c> deadzone
    /// (0.25): reading a look axis and deciding the player has switched devices are different
    /// questions, and they want different answers. A stick worn enough to rest at 0.3 should
    /// still steer the camera once you are already on the pad — it should never yank the mode
    /// off the keyboard while someone is typing their name into the join box.</summary>
    public const float StickActivationDeadzone = 0.5f;

    /// <summary>Pixels of accumulated mouse travel needed to claim the mode back from a pad.
    /// A single event is never enough: sensor jitter and a nudged desk both emit small motions
    /// indefinitely, and any per-event threshold either lets them through or makes a slow
    /// deliberate move fail to register. Accumulating instead means jitter has to actually add
    /// up to a real gesture within <see cref="MouseTravelWindowSec"/>, and a slow move still
    /// gets there. 32px is a few times the noise of a high-DPI sensor at rest and a small
    /// fraction of any move a person makes on purpose.</summary>
    public const float MouseTravelThresholdPx = 32f;

    /// <summary>How long accumulated mouse travel stays "live". Motion this far apart is two
    /// separate twitches, not one gesture, so the accumulator restarts — which is precisely
    /// what stops slow drift from summing to the threshold over a quiet minute.</summary>
    public const double MouseTravelWindowSec = 0.5;

    private float _mouseTravelPx;
    private double _lastMouseMotionSec;
    private bool _hasMouseMotion;

    /// <summary>The live mode. Starts on keyboard/mouse: it is the only mode this project
    /// supported before auto-detect, and it is the correct assumption for a player who has not
    /// touched anything yet — including one with a pad plugged in but untouched.</summary>
    public InputMode Mode { get; private set; } = InputMode.KeyboardMouse;

    /// <summary>Convenience for the glyph layer, which only ever asks the yes/no question.</summary>
    public bool IsPad => Mode == InputMode.Gamepad;

    /// <summary>A key went down. Unambiguous — no threshold, no accumulation: a keyboard cannot
    /// press itself. Callers pass only genuine presses (not releases, not auto-repeat echoes);
    /// see <see cref="ActiveDevice"/> for why.</summary>
    public bool NotifyKeyPressed() => SetMode(InputMode.KeyboardMouse);

    /// <summary>A mouse button (or wheel) went down. Same reasoning as a key: deliberate by
    /// construction, so it flips immediately and does not go through the travel accumulator.</summary>
    public bool NotifyMouseButtonPressed() => SetMode(InputMode.KeyboardMouse);

    /// <summary>The mouse moved by <paramref name="relativeX"/>/<paramref name="relativeY"/>
    /// pixels at <paramref name="nowSec"/>. Only claims the mode once travel inside the window
    /// clears <see cref="MouseTravelThresholdPx"/> — this is the guard that keeps a jittery
    /// sensor from stealing the game back from a pad the player is holding.
    ///
    /// Travel is summed as |dx| + |dy| rather than a true Euclidean length: this runs on every
    /// mouse-motion event forever, the two differ by at most ~41%, and that difference is
    /// meaningless against a threshold whose job is only to separate "noise" from "a gesture".
    /// Trading a square root for an add on the hottest input path is the right trade.</summary>
    public bool NotifyMouseMotion(float relativeX, float relativeY, double nowSec)
    {
        // Already on keyboard/mouse: there is nothing to prove, so skip the bookkeeping
        // entirely. This is the overwhelmingly common case, and it costs one branch.
        if (Mode == InputMode.KeyboardMouse)
            return false;

        // Motion further apart than the window is a separate twitch, not a continuation.
        // Compared as an ABSOLUTE difference so a clock that goes backwards still resets:
        // the caller's time source is Godot's millisecond tick counter, which wraps roughly
        // every 49 days of uptime, and a signed compare would read the wrap as "no time has
        // passed" and let the pre-wrap accumulator keep filling.
        if (!_hasMouseMotion || System.Math.Abs(nowSec - _lastMouseMotionSec) > MouseTravelWindowSec)
            _mouseTravelPx = 0f;

        _lastMouseMotionSec = nowSec;
        _hasMouseMotion = true;
        _mouseTravelPx += System.MathF.Abs(relativeX) + System.MathF.Abs(relativeY);

        return _mouseTravelPx >= MouseTravelThresholdPx && SetMode(InputMode.KeyboardMouse);
    }

    /// <summary>A pad face/shoulder/d-pad button went down. Flips immediately — a button cannot
    /// drift. Callers pass presses only, never releases.</summary>
    public bool NotifyPadButtonPressed()
    {
        ClearMouseTravel();
        return SetMode(InputMode.Gamepad);
    }

    /// <summary>A pad axis reported <paramref name="axisValue"/>. Flips only past
    /// <see cref="StickActivationDeadzone"/>, so a stick resting off-centre is inert. Magnitude
    /// is taken absolute so it reads the same for both stick directions and for triggers,
    /// whose rest value differs between pads.</summary>
    public bool NotifyPadAxis(float axisValue)
    {
        if (System.MathF.Abs(axisValue) < StickActivationDeadzone)
            return false; // drift, not use: banked mouse travel is left alone deliberately
        ClearMouseTravel();
        return SetMode(InputMode.Gamepad);
    }

    /// <summary>A pad was plugged in. Deliberately does nothing: plenty of people leave a
    /// controller connected and never touch it, and switching a typing player's glyphs because
    /// a USB device enumerated would be a bug, not a feature. Presence is not use. Present as a
    /// named no-op rather than an absent case so the decision is visible at the call site.</summary>
    public bool NotifyPadConnected() => false;

    /// <summary>A pad was unplugged. Falls back to keyboard/mouse only once NO pad remains
    /// (<paramref name="anyPadStillConnected"/> is false) — with a second pad still attached the
    /// player has not lost their controller, so the mode stands. The fallback matters because a
    /// player whose pad just died has no way to generate the keyboard event that would otherwise
    /// be needed to flip back, and would be left staring at glyphs for a device that is gone.</summary>
    public bool NotifyPadDisconnected(bool anyPadStillConnected) =>
        !anyPadStillConnected && SetMode(InputMode.KeyboardMouse);

    /// <returns>True if the mode actually changed, so the caller can emit its signal exactly on
    /// real transitions instead of on every event.</returns>
    private bool SetMode(InputMode mode)
    {
        if (Mode == mode)
            return false;
        Mode = mode;
        // Whichever way we just went, stale travel must not carry across the transition: on the
        // way TO the pad it would otherwise sit there ready to flip straight back on the next
        // twitch, and on the way BACK it has already been spent.
        ClearMouseTravel();
        return true;
    }

    /// <summary>Forgets banked mouse travel. Called on every real pad ACTION — not just on a
    /// mode change — because a player who is pushing sticks and pressing buttons right now is
    /// unambiguously on the pad, and that should raise the bar the mouse has to clear rather
    /// than leaving a part-filled accumulator lying around for the next twitch to top up.
    /// Net effect of the rule: the mouse claims the mode only by travelling
    /// <see cref="MouseTravelThresholdPx"/> within a window in which the pad stayed quiet.</summary>
    private void ClearMouseTravel()
    {
        _mouseTravelPx = 0f;
        _hasMouseMotion = false;
    }
}
