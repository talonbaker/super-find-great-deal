using System.Collections.Generic;
using Godot;

namespace MpFoundation.Ui.Achievements;

/// <summary>
/// The whole toast UI packet W7-5 asks for, and nothing more — its scope is explicit: "if
/// earning one needs a toast, build the toast; stop there," and no achievements browser or
/// gallery screen. Deliberately its OWN class and file rather than a second use of
/// <c>PhaseToastLayer</c>: W7-2 is editing UI files concurrently in this wave, and keeping the
/// two toast systems on entirely separate files means neither packet's diff can collide with the
/// other's. It borrows <c>PhaseToastLayer</c>'s proven shape (a queue so two toasts in quick
/// succession fade in turn rather than garbling on top of each other) without touching that file.
/// </summary>
public partial class AchievementToastLayer : CanvasLayer
{
    private const double HoldSec = 3.5;
    private const double FadeSec = 0.35;

    /// <summary>Where the line sits above the bottom edge — a VALUE fork, picked so an
    /// achievement toast never lands on the same screen real-estate as
    /// <c>PhaseToastLayer</c>'s top-centre phase toasts.</summary>
    private const float BottomOffset = -96f;

    private readonly Queue<string> _queue = new();
    private Label _label = null!;
    private bool _showing;

    public override void _Ready()
    {
        Layer = Design.UiLayers.AchievementToast;

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0),
        };
        // Same on-scrim ink/outline treatment as PhaseToastLayer: a line over the live world
        // with no plate behind it, so it must carry its own legibility.
        _label.ThemeTypeVariation = "Title";
        _label.AddThemeColorOverride("font_color", Design.UiThemeService.Tokens.InkOnScrim);
        _label.AddThemeColorOverride("font_outline_color", Design.UiThemeService.Tokens.TextOutline);
        _label.AddThemeConstantOverride("outline_size", Design.UiScale.SpaceTight);
        Design.UiThemeService.Bind(_label, t => _label.AddThemeColorOverride("font_color", t.InkOnScrim));
        _label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _label.OffsetBottom = BottomOffset;
        _label.GrowHorizontal = Control.GrowDirection.Both;
        _label.GrowVertical = Control.GrowDirection.Begin;
        AddChild(_label);

        // Only relevant while process would matter for animation upkeep; unlike PhaseToastLayer
        // this label's position is fixed (no readout above it to chase), so no _Process is needed
        // at all.
    }

    /// <summary>Enqueues one toast line. Public so <c>AchievementRuntime</c> — the only caller —
    /// can drive it without this class knowing anything about achievements itself.</summary>
    public void Show(string text)
    {
        _queue.Enqueue(text);
        if (!_showing)
            AdvanceQueue();
    }

    private async void AdvanceQueue()
    {
        if (_queue.Count == 0)
        {
            _showing = false;
            return;
        }
        _showing = true;
        _label.Text = _queue.Dequeue();

        Tween inTween = CreateTween();
        inTween.TweenProperty(_label, "modulate:a", 1f, FadeSec).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(inTween, Tween.SignalName.Finished);
        if (!GodotObject.IsInstanceValid(this))
            return;

        await ToSignal(GetTree().CreateTimer(HoldSec), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(this))
            return;

        Tween outTween = CreateTween();
        outTween.TweenProperty(_label, "modulate:a", 0f, FadeSec).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(outTween, Tween.SignalName.Finished);
        if (!GodotObject.IsInstanceValid(this))
            return;

        AdvanceQueue(); // pick up anything that queued while this toast was showing.
    }
}
