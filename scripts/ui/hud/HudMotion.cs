using System;
using Godot;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// Change feedback for the HUD — the half of the fix that answers "boring".
///
/// <para><b>Why this exists.</b> The first HUD had no motion of any kind. The tally snapped to a
/// new number, the live carry slot snapped between two chips, the phase changed silently at dusk.
/// Every value was correct and nothing ever acknowledged anything, which is exactly what a
/// readout feels like when it reads as dead. <c>.claude/skills/ui-design</c> calls this the
/// highest-leverage fix in interface work: <i>"it converts a static-feeling screen into a
/// responsive one with no visual redesign at all."</i> That is the claim being tested here — the
/// palette below is unchanged from a structural pass; only the behaviour is new.</para>
///
/// <para><b>A HUD's states are not a button's.</b> Nothing here is hoverable or focusable, so the
/// usual normal/hover/pressed/focus set does not apply. The equivalent question for a readout is
/// <i>"did the player see it change?"</i> — because a number that silently becomes a different
/// number is indistinguishable from one that was always that value. So the states this models are
/// <b>changed</b>, <b>became live</b>, and <b>crossed a threshold</b>.</para>
///
/// <para><b>Timings are from the skill's band</b> (120–300 ms, eased) and deliberately at its
/// short end. This fires during play, often several times a minute, and motion the player has to
/// wait out becomes worse than no motion by about the third repetition.</para>
///
/// <para><b>Reduced motion is honoured</b> (<see cref="HudSettings.ReducedMotion"/>) — an
/// accessibility non-negotiable, not a nicety: vestibular disorders are real and scaling UI is a
/// common trigger. With it on, every helper below still applies the END state instantly, so the
/// information is identical and only the movement is dropped. It must never mean "the change
/// becomes invisible".</para>
/// </summary>
public static class HudMotion
{
    /// <summary>How long a value-change flash takes to bloom and fall back.</summary>
    private const double FlashInSec = 0.08;
    private const double FlashOutSec = 0.28;

    /// <summary>How long a chip takes to become (or stop being) the live one.</summary>
    private const double SelectSec = 0.16;

    /// <summary>How far a chip lifts when it becomes live. Small on purpose — this reads as
    /// emphasis at 4 px and as a bouncing toy at 12.</summary>
    private const float SelectLiftPx = 3f;

    /// <summary>Flashes a label toward <paramref name="colour"/> and settles back to its themed
    /// colour. Used when a value the player cares about changes underneath them — the tally
    /// rising, most of all, which previously happened in total silence.
    ///
    /// Clears the override on completion rather than re-asserting the resting colour, so the
    /// label goes back to inheriting from the theme variation and a later theme change still
    /// reaches it.</summary>
    public static void FlashText(Label label, Color colour)
    {
        if (!GodotObject.IsInstanceValid(label))
            return;
        if (HudSettings.ReducedMotion)
            return; // the new value is already applied by the caller; only the bloom is dropped.

        label.AddThemeColorOverride("font_color", colour);
        Tween tween = label.CreateTween();
        tween.TweenInterval(FlashInSec);
        tween.TweenMethod(
            Callable.From<float>(t => Fade(label, colour, t)), 0f, 1f, FlashOutSec)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(label))
                label.RemoveThemeColorOverride("font_color");
        }));
    }

    /// <summary>Interpolates the flash colour toward transparent-of-itself. Kept as a named method
    /// rather than a lambda capturing a resting colour, because the resting colour lives in the
    /// theme and this must not hardcode a copy of it.</summary>
    private static void Fade(Label label, Color from, float t)
    {
        if (!GodotObject.IsInstanceValid(label))
            return;
        // Ride the alpha down instead of lerping toward a guessed resting colour: at t=1 the
        // override is removed anyway, so the themed colour is what shows through.
        label.AddThemeColorOverride("font_color", new Color(from, 1f - t));
    }

    /// <summary>Marks a chip becoming the live one: a short lift and a settle. The visual state is
    /// already carried by the stylebox, brackets and caption — this only makes the TRANSITION
    /// perceptible, so a player who pressed 2 sees which chip answered rather than having to
    /// re-read both.</summary>
    public static void Select(Control chip, bool live)
    {
        if (!GodotObject.IsInstanceValid(chip))
            return;

        float target = live ? -SelectLiftPx : 0f;
        if (HudSettings.ReducedMotion)
        {
            chip.Position = new Vector2(chip.Position.X, chip.Position.Y + target - LiftOf(chip));
            SetLift(chip, target);
            return;
        }

        float start = LiftOf(chip);
        Tween tween = chip.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(v =>
            {
                if (!GodotObject.IsInstanceValid(chip))
                    return;
                chip.Position = new Vector2(chip.Position.X, chip.Position.Y + (v - LiftOf(chip)));
                SetLift(chip, v);
            }),
            start, target, SelectSec)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    // The chip's current lift is tracked in metadata rather than derived from Position, because
    // Position is also set by the layout pass — subtracting a remembered offset is the only way
    // to move a control repeatedly without the offsets accumulating into a drift down the screen.
    private const string LiftMeta = "hud_lift";

    private static float LiftOf(Control c) =>
        c.HasMeta(LiftMeta) ? (float)c.GetMeta(LiftMeta) : 0f;

    private static void SetLift(Control c, float v) => c.SetMeta(LiftMeta, v);

    /// <summary>How long the shared-tally wobble takes to bloom and settle. The long end of the
    /// band's short half: a ripple needs enough frames to read as a wave passing through the
    /// glyph rather than as one frame of wrongness, and 260 ms is roughly one and a half wave
    /// crossings at <c>HudBubbleCount</c>'s frequency.</summary>
    private const double WobbleSec = 0.26;

    /// <summary>
    /// The acknowledgement a SHARED value gets when it moves: one decaying envelope, handed to
    /// the caller as an amplitude falling 1 → 0, for it to spend on whatever deformation it owns.
    /// Used by the bubble tally, where <i>anyone's</i> pop moves the group total and the HUD's
    /// job is "the number just moved", read identically by every peer.
    ///
    /// <para><b>Why an amplitude and not a transform.</b> <see cref="Select"/> and
    /// <see cref="FlashText"/> act ON a control (its position, its font colour) because a chip
    /// and a label each have one obvious thing to move. A ripple does not: it is a deformation of
    /// what the element draws, not of where the element is. So this helper owns the two things
    /// that must not be re-decided per caller — the timing band and the reduced-motion contract —
    /// and the caller owns the geometry.</para>
    ///
    /// <para><b>Reduced motion applies the END state instantly</b>, which for an envelope means
    /// amplitude 0: the element draws undeformed, and the value it accompanies has already been
    /// applied by the caller. Nothing becomes invisible; only the movement is dropped.</para>
    ///
    /// <para><b>Coalescing is the caller's</b>, deliberately — "one nudge, however many pops
    /// landed in this tick" is a fact about the tally, not about motion.</para>
    /// </summary>
    public static void Wobble(CanvasItem host, Action<float> apply)
    {
        if (!GodotObject.IsInstanceValid(host))
            return;
        if (HudSettings.ReducedMotion)
        {
            apply(0f);
            return;
        }

        Tween tween = host.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(a =>
            {
                if (GodotObject.IsInstanceValid(host))
                    apply(a);
            }),
            1f, 0f, WobbleSec)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        // Settle exactly on zero rather than trusting the last interpolated frame: a ripple left
        // at a hundredth of an amplitude is a permanently, invisibly warped icon.
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(host))
                apply(0f);
        }));
    }

    /// <summary>How long <see cref="Wobble"/> runs, for a caller deciding whether one is already
    /// in flight.</summary>
    public static double WobbleDurationSec => WobbleSec;

    /// <summary>Counts a label from one integer to another over a short roll, so earning money
    /// reads as money arriving rather than as a number that was always different.
    ///
    /// Falls back to setting the value directly for a large jump — a payout of several hundred
    /// counted digit by digit would still be rolling when the player looked away, and the
    /// threshold keeps the animation an accent on small frequent gains rather than a wait.</summary>
    public static void CountTo(Label label, int from, int to, Func<int, string> format)
    {
        if (!GodotObject.IsInstanceValid(label))
            return;

        int delta = Math.Abs(to - from);
        if (HudSettings.ReducedMotion || delta == 0 || delta > 250)
        {
            label.Text = format(to);
            return;
        }

        // Duration scales with the size of the change, clamped: a 1-quarter tick should not take
        // as long as a 40-quarter payout, and neither should take long enough to notice as a wait.
        double duration = Math.Clamp(0.12 + delta * 0.012, 0.12, 0.45);
        Tween tween = label.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(v =>
            {
                if (GodotObject.IsInstanceValid(label))
                    label.Text = format(Mathf.RoundToInt(v));
            }),
            (float)from, (float)to, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }
}
