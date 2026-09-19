using Godot;
using System.Collections.Generic;

namespace MpFoundation.Ui;

/// <summary>
/// Shared entrance motion for the menu screens: items fade up in sequence,
/// top-to-bottom, matching the source design's "8px fade-up, 40ms per row".
/// Nothing bounces or overshoots.
/// </summary>
public static class UiMotion
{
    public static async void StaggerIn(Control container, float rise = 8f, double step = 0.045, double dur = 0.30)
    {
        // Let the container lay out so each child's resting position is known. Cache the tree
        // and re-check validity between the awaits: the screen can die within this two-frame
        // window (buffered scene change), and async void has no awaiter to observe a throw.
        SceneTree tree = container.GetTree();
        await container.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(container))
            return;
        await container.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(container))
            return;

        var items = new List<Control>();
        foreach (Node child in container.GetChildren())
            if (child is Control c && c.Visible)
                items.Add(c);

        var tween = container.CreateTween().SetParallel(true);
        for (int i = 0; i < items.Count; i++)
        {
            Control c = items[i];
            float restY = c.Position.Y;
            c.Modulate = new Color(c.Modulate, 0f);
            c.Position = new Vector2(c.Position.X, restY + rise);
            double delay = i * step;
            tween.TweenProperty(c, "modulate:a", 1f, dur)
                .SetDelay(delay).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(c, "position:y", restY, dur)
                .SetDelay(delay).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
    }

    // A control's true rest X (and its in-flight shake), remembered across overlapping shakes:
    // capturing "rest" from a node mid-shake (validation failures mashed faster than the 0.18s
    // tween) drifted the control sideways permanently, and two live tweens fight over the same
    // property. Keyed weakly so freed controls don't accumulate.
    private sealed class ShakeState { public float RestX; public Tween? Active; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, ShakeState> _shakes = new();

    /// <summary>A single ±amount horizontal shake, used on input validation failure.</summary>
    public static void Shake(Control node, float amount = 4f, double dur = 0.18)
    {
        ShakeState state = _shakes.GetValue(node, n => new ShakeState { RestX = n.Position.X });
        if (state.Active != null && state.Active.IsValid())
            state.Active.Kill();
        node.Position = new Vector2(state.RestX, node.Position.Y); // reset any in-flight offset
        Tween tween = node.CreateTween();
        tween.TweenProperty(node, "position:x", state.RestX - amount, dur * 0.25).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(node, "position:x", state.RestX + amount, dur * 0.5).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(node, "position:x", state.RestX, dur * 0.25).SetTrans(Tween.TransitionType.Sine);
        state.Active = tween;
    }
}
