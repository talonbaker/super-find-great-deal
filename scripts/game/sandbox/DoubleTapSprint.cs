namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>Double-tap a direction to sprint (BT-7).</b> Talon, 2026-08-27: <i>"sprint triggers via
/// holding Shift OR double-tapping a directional/WASD key."</i> Shift-hold is untouched and lives
/// where it always did (<c>LocalInputIntentSource.NextIntent</c>, the <c>sprint</c> action); this
/// class is the OR.
///
/// <para><b>It is a pure detector, and that is the whole of its design.</b> No Godot type, no
/// <c>Input</c> read, no node, no state outside five primitives — it is fed four booleans and a delta
/// and answers one question. Three consequences worth stating, because each one is why a fatter
/// class was rejected:</para>
/// <list type="bullet">
///   <item><b>It is unit-testable without an engine.</b> A tap sequence is a loop over
///     <see cref="Update"/>; the whole timing contract is provable in the xUnit suite, on any
///     machine, with no display and no GPU.</item>
///   <item><b>It is client-side and costs the protocol nothing.</b> It sets the SAME
///     <c>MoveIntent.Sprint</c> bit that holding Shift sets, so no wire bit, no
///     <c>NetProfile</c> version bump, and no server change. A doctored client can already claim
///     "sprint" by holding Shift; this claims nothing new.</item>
///   <item><b>It declares no input action.</b> It reads the four <c>move_*</c> actions the
///     movement stick already reads, so <c>project.godot</c> is not touched and a player's
///     rebound WASD keeps working — rebind <c>move_forward</c> and the double-tap follows it.</item>
/// </list>
///
/// <para><b>The state machine, in full.</b> Per key: <c>Idle</c> — nothing pending;
/// <c>ArmedForSecondTap</c> — the key went down and came back up inside the window, and a second
/// press before the window closes latches sprint. The latch itself is figure-level, not per-key:
/// once sprint is on, it stays on while ANY direction is held, and releases when every direction
/// has been clear for <see cref="ReleaseGraceSec"/>. That grace is what makes a strafe-round-a-
/// corner (release W, press D, a few frames apart) keep sprinting instead of dropping to a jog
/// mid-turn, which is the single thing that would make the feature feel broken.</para>
///
/// <para><b>Tapping two DIFFERENT keys is not a double-tap</b> — W then D is a player changing
/// direction, and treating it as a sprint request would make the feature fire constantly during
/// ordinary movement. The armed key is remembered, and a press of any other direction re-arms on
/// the new key rather than latching.</para>
/// </summary>
public sealed class DoubleTapSprint
{
    /// <summary><b>0.28 s</b> — the outside edge of "that was one gesture, not two decisions".
    /// The packet's number, kept: it sits between a comfortable deliberate double-tap (a typical
    /// double-click threshold is 0.5 s, which is far too loose here — a player jogging and
    /// re-pressing W after a hop would trip it) and the ~0.15 s below which a real double-tap
    /// starts to require intent to hit. Measured against nothing but hands, which is why it is a
    /// constant on the class rather than a tuned export: the first playtest is what moves it.
    /// Applies to the WHOLE gesture — press, release, press — not to each half.</summary>
    public const float DoubleTapWindowSec = 0.28f;

    /// <summary><b>0.15 s</b> — how long every direction must be clear before the latch drops.
    /// The packet's number. Long enough to cross a W→D strafe transition (a few frames) and short
    /// enough that stopping and standing still ends the sprint before the player notices they are
    /// still in it. Zero would be wrong for the reason above; anything approaching
    /// <see cref="DoubleTapWindowSec"/> would let a released-then-re-tapped key resume a sprint
    /// the player meant to end.</summary>
    public const float ReleaseGraceSec = 0.15f;

    /// <summary>Which of the four directions is currently armed for its second press, as an index
    /// into the (forward, back, left, right) tuple <see cref="Update"/> takes; -1 when nothing is
    /// armed. An index rather than an enum so this file adds no type to the namespace for a value
    /// that never leaves it.</summary>
    private int _armedKey = -1;

    /// <summary>How long the armed key's gesture has been running, measured from its FIRST PRESS
    /// and not from its release. That choice is the whole meaning of
    /// <see cref="DoubleTapWindowSec"/>: the window covers press-release-press as one gesture, so
    /// a player who holds W for a second, lets go and presses again has already spent it and does
    /// not sprint. Timing only the gap between the two presses would make every stop-and-go into
    /// a sprint request.</summary>
    private float _armedForSec;

    /// <summary>True once the armed key's first press has been RELEASED — the second half of the
    /// gesture. A second press while this is false is not a second press at all (the key is still
    /// down; nothing was released), which is what stops a key-repeat or a re-sampled hold from
    /// latching.</summary>
    private bool _armedReleased;

    /// <summary>How long every direction has been released while the latch is on. Compared
    /// against <see cref="ReleaseGraceSec"/>. Meaningless while <see cref="Latched"/> is false.</summary>
    private float _allClearForSec;

    /// <summary>The four directions as of the previous <see cref="Update"/>, so a press and a
    /// release are EDGES rather than levels. Level-triggering would make a held key an endless
    /// stream of presses and the first tap would latch on its own.</summary>
    private bool _wasForward, _wasBack, _wasLeft, _wasRight;

    /// <summary><b>True while the double-tap sprint is on.</b> The caller ORs this into the
    /// intent's existing sprint bit; it is never the only source of one.</summary>
    public bool Latched { get; private set; }

    /// <summary>Drops the latch and every pending tap. Called when the local player loses input
    /// focus (mouse released to a menu), because a sprint that survives a pause menu is a player
    /// walking into a lake while reading a settings screen.</summary>
    public void Reset()
    {
        Latched = false;
        _armedKey = -1;
        _armedForSec = 0f;
        _armedReleased = false;
        _allClearForSec = 0f;
        _wasForward = _wasBack = _wasLeft = _wasRight = false;
    }

    /// <summary>
    /// One frame of the detector.
    /// </summary>
    /// <param name="forward">Is <c>move_forward</c> (W) held THIS frame?</param>
    /// <param name="back">Is <c>move_back</c> (S) held this frame?</param>
    /// <param name="left">Is <c>move_left</c> (A) held this frame?</param>
    /// <param name="right">Is <c>move_right</c> (D) held this frame?</param>
    /// <param name="deltaSec">Seconds since the previous call. Clamped at zero — a negative
    /// delta cannot happen from the frame loop but can from a badly written test, and a negative
    /// clock would run the window backwards for ever.</param>
    /// <returns><see cref="Latched"/> after this frame, so the call site is one line.</returns>
    public bool Update(bool forward, bool back, bool left, bool right, float deltaSec)
    {
        float dt = deltaSec > 0f ? deltaSec : 0f;
        bool anyHeld = forward || back || left || right;

        // --- The latch's own life, first: it can end this frame regardless of any new tap ------
        if (Latched)
        {
            _allClearForSec = anyHeld ? 0f : _allClearForSec + dt;
            if (_allClearForSec >= ReleaseGraceSec)
            {
                Latched = false;
                _allClearForSec = 0f;
                // A press arriving on the very frame the latch drops is a FIRST tap, not a
                // second: the gesture that latched is over. Falling through with _armedKey
                // already -1 is what makes that true.
                _armedKey = -1;
                _armedForSec = 0f;
                _armedReleased = false;
            }
        }

        // --- The window on a pending second press ---------------------------------------------
        if (_armedKey >= 0)
        {
            _armedForSec += dt;
            if (_armedForSec > DoubleTapWindowSec)
            {
                _armedKey = -1;
                _armedForSec = 0f;
                _armedReleased = false;
            }
        }

        // --- Edges ------------------------------------------------------------------------------
        for (int key = 0; key < 4; key++)
        {
            bool now = Held(key, forward, back, left, right);
            bool before = Held(key, _wasForward, _wasBack, _wasLeft, _wasRight);
            if (now && !before)
            {
                // A PRESS. The SECOND press of an armed and already-released key, inside the
                // window, is the double-tap. Anything else starts a fresh gesture on this key —
                // which is what makes W-then-D a direction change rather than a sprint request,
                // and it re-arms on D rather than throwing the input away, so D-D still works.
                if (_armedKey == key && _armedReleased)
                {
                    Latched = true;
                    _allClearForSec = 0f;
                    _armedKey = -1;
                    _armedForSec = 0f;
                    _armedReleased = false;
                }
                else if (!Latched)
                {
                    _armedKey = key;
                    _armedForSec = 0f;
                    _armedReleased = false;
                }
            }
            else if (!now && before && _armedKey == key && !_armedReleased)
            {
                // A RELEASE of the key mid-gesture. The window clock keeps running across it —
                // see _armedForSec.
                _armedReleased = true;
            }
        }

        _wasForward = forward;
        _wasBack = back;
        _wasLeft = left;
        _wasRight = right;
        return Latched;
    }

    private static bool Held(int key, bool forward, bool back, bool left, bool right) => key switch
    {
        0 => forward,
        1 => back,
        2 => left,
        _ => right,
    };
}
