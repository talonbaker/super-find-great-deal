using System;
using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Base for the four routed state screens (intro / tally / lobby / loss) + the connecting
/// gate: kit scaffold, and the subscribe-and-poll visibility contract in ONE place —
/// every frame the screen asks <see cref="ScreenRouter.ScreenFor"/> whether it is the
/// routed screen, so a late joiner (or a subscriber that missed every event) lands on the
/// correct screen from the first poll (spec §3.4's never-strand rule). Events are used
/// only for content refresh and motion, never for visibility.
/// </summary>
public abstract partial class FlowScreenBase : CanvasLayer
{
    protected readonly IPlaythroughView View;
    protected VBoxContainer Column = null!;
    private bool _showing;

    /// <summary>Which routed screen this is.</summary>
    protected abstract ScreenId Id { get; }

    /// <summary>Scrim colour — the loss screen overrides to near-black.</summary>
    protected virtual Color Scrim => UiKit.StateScrim;

    protected FlowScreenBase(IPlaythroughView view)
    {
        View = view;
    }

    public override void _Ready()
    {
        // The scaffold is built INTO this CanvasLayer rather than wrapping it, so the
        // screen node itself is the ladder rung.
        Layer = ScreenRouter.StateScreenLayer;
        Visible = false;
        var scrimRect = new ColorRect { Color = Scrim, Name = "Scrim" };
        scrimRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(scrimRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrimRect.AddChild(center);
        Column = new VBoxContainer();
        Column.AddThemeConstantOverride("separation", UiKit.SpaceM);
        Column.CustomMinimumSize = new Vector2(UiKit.CardMinWidth, 0);
        center.AddChild(Column);
        SetMeta(UiKit.ComponentMeta, "StateScreenScaffold");

        BuildContent();
        Poll(); // synchronous first check — the LoadingHintOverlay race reasoning.
    }

    public override void _Process(double delta)
    {
        Poll();
        if (_showing)
            OnVisibleProcess(delta);
    }

    private void Poll()
    {
        bool shouldShow = ScreenRouter.ScreenFor(View.Synced, View.State) == Id;
        if (shouldShow == _showing)
        {
            if (shouldShow)
                RefreshIfStale();
            return;
        }
        _showing = shouldShow;
        Visible = shouldShow;
        if (shouldShow)
        {
            OnShow();
            UiMotion.StaggerIn(Column);
        }
        else
        {
            OnHide();
        }
    }

    /// <summary>Build the column once, from the kit.</summary>
    protected abstract void BuildContent();

    /// <summary>Refresh content from polled state; called on every transition to visible.
    /// Must be safe to call with whatever the queryable state holds (late join included).</summary>
    protected abstract void OnShow();

    /// <summary>Per-frame work while visible (countdowns). Default: nothing.</summary>
    protected virtual void OnVisibleProcess(double delta) { }

    /// <summary>Content refresh while already visible, for screens whose data can change
    /// underneath them. Default: nothing.</summary>
    protected virtual void RefreshIfStale() { }

    protected virtual void OnHide() { }
}

/// <summary>Shared pure formatting for the flow screens (used by tally AND lobby — kit
/// reuse is the deliverable).</summary>
public static class FlowFormat
{
    /// <summary>Whole-second countdown line; clamps at 0 (a stale extrapolation must never
    /// show a negative), em-dash-free ASCII per the repo's encoding discipline.</summary>
    public static string Countdown(double remainingSec, string verb)
    {
        int whole = (int)Math.Ceiling(Math.Max(0, remainingSec));
        return $"{verb} in {whole}s";
    }
}
