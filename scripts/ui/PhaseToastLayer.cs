using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Ui;

/// <summary>
/// L11 (Issue #114), design spec §1 steps 4/5: one-line toasts on L1's sunset/night/dawn phase
/// crossings — no art, per scope. Subscribes <see cref="RunDriver.PhaseCrossed"/> directly
/// (present on every peer, server included, but this Story only ever adds this layer on
/// non-headless peers — see the Gameplay wiring — so a headless server never builds one).
///
/// Toasts queue rather than overlap: <see cref="RunDriver.PhaseCrossed"/> fires "on every peer,
/// exactly once per crossing, in order" (L1's own contract), and while three crossings in one
/// run are normally minutes apart, a short test <c>--cycle-period</c> or a reset landing right
/// at a crossing could otherwise stack two toasts' fades on top of each other — a queue makes
/// that harmless instead of a garbled overlap.
/// </summary>
public partial class PhaseToastLayer : CanvasLayer
{
    private const double HoldSec = 3.5;
    private const double FadeSec = 0.35;

    private readonly Queue<string> _queue = new();
    private Label _label = null!;
    private bool _showing;
    private Flow.NightfallOverlay _nightfall = null!;

    /// <summary>CORE-PROG-B1's suppression seam (spec §3.5 row 1): when a playthrough view
    /// is attached (CORE-INT-1 sets this beside the FlowScreens attach), the telegraph only
    /// fires while the round is band-live — no dusk fanfare over a tally screen. Null (the
    /// pre-integration default) preserves today's shipped behavior exactly.</summary>
    public static Flow.IPlaythroughView? FlowView { get; set; }

    /// <summary>The live toast layer on this peer, or null where there is none — which is every
    /// headless server and bot (see the class doc: <c>Gameplay</c> only adds this on a
    /// non-headless peer) and every scene that is not <c>Gameplay</c>. The same
    /// static-plus-null-check convention <c>BubbleCounter</c>, <c>CycleDriver</c>,
    /// <c>PropManager</c> and <c>HonkManager</c> already use.
    ///
    /// <para>Added by CELEBRATE-1 (2026-09-04): the all-bubbles line is raised from
    /// <c>Sail.Game.Bubble.BubbleCelebration</c>, which receives a network broadcast and has no
    /// path down the tree to here. Every existing caller reaches this layer through a local
    /// reference it already holds and is unaffected.</para></summary>
    public static PhaseToastLayer? Instance { get; private set; }

    /// <summary>The nested DuskToNight treatment — exposed so the scripted demo can cue it
    /// the way the live crossing does.</summary>
    public Flow.NightfallOverlay Nightfall => _nightfall;

    public override void _Ready()
    {
        Instance = this;
        Layer = Design.UiLayers.PhaseToast;

        // The DuskToNight treatment (packet 3b) rides this layer's existing
        // subscription — extend, never a parallel PhaseCrossed subscriber. Its CanvasLayer
        // slot is its own (ScreenRouter.NightfallLayer); nesting only scopes its lifetime.
        _nightfall = new Flow.NightfallOverlay { Name = "NightfallOverlay" };
        AddChild(_nightfall);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0),
        };
        // A line over the live world, with no plate behind it — so it takes the on-scrim ink
        // (the token for text with darkness rather than paper under it) and keeps the outline,
        // which is the only thing guaranteeing legibility over an arbitrary frame.
        _label.ThemeTypeVariation = "Title";
        _label.AddThemeColorOverride("font_color", Design.UiThemeService.Tokens.InkOnScrim);
        _label.AddThemeColorOverride("font_outline_color", Design.UiThemeService.Tokens.TextOutline);
        _label.AddThemeConstantOverride("outline_size", Design.UiScale.SpaceTight);
        Design.UiThemeService.Bind(_label, t => _label.AddThemeColorOverride("font_color", t.InkOnScrim));
        _label.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _label.OffsetTop = Design.UiColumns.PhaseToastTop;
        _label.GrowHorizontal = Control.GrowDirection.Both;
        AddChild(_label);

        // Only ticks while a line is actually up — see _Process. A transient layer has no business
        // holding a per-frame callback open for the minutes between crossings.
        SetProcess(false);

        if (RunDriver.Instance is { } driver)
            driver.PhaseCrossed += OnPhaseCrossed;
    }

    public override void _ExitTree()
    {
        // Cleared only if it is still THIS layer: a scene change that builds the next one before
        // freeing this one would otherwise leave the static null while a live layer exists.
        if (ReferenceEquals(Instance, this))
            Instance = null;
        if (RunDriver.Instance is { } driver)
            driver.PhaseCrossed -= OnPhaseCrossed;
    }

    /// <summary>Keeps the line under the two persistent top-centre readouts for as long as it is
    /// up. Polled rather than set once when the toast appears, because the readouts above it are
    /// laid out independently and on their own schedule — the winter-cache strip in particular
    /// only publishes its height once it has been visible for a frame, so a toast fired in the
    /// same tick the round goes live would otherwise land straight on top of it. Enabled only
    /// while showing (see <see cref="AdvanceQueue"/>).</summary>
    public override void _Process(double delta) => _label.OffsetTop = Design.UiColumns.PhaseToastTop;

    private void OnPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        if (!Flow.ScreenRouter.TelegraphAllowed(FlowView))
            return; // outside a band-live state the telegraph is noise — see FlowView's doc.
        string? text = PhaseToastText.TextFor(kind);
        if (text == null)
            return; // DawnToDay — no toast, see PhaseToastText's doc.
        ShowLine(text);
        if (PhaseToastText.FullTreatmentFor(kind))
            _nightfall.ShowNightfall();
    }

    /// <summary>Enqueues a toast line directly — the scripted demo's entry (no RunDriver
    /// exists there), and the same path every crossing takes.</summary>
    public void ShowLine(string text)
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
            SetProcess(false);
            return;
        }
        _showing = true;
        SetProcess(true); // re-take the column for as long as the line is up — see _Process.
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
