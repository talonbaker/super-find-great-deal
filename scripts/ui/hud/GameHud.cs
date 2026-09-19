using System;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// The in-game HUD: one <see cref="CanvasLayer"/> owning the top-centre readouts — the shared
/// bubble tally and the day/phase line — plus the bottom-right control-reference hint, replacing
/// the deleted <c>SessionHud</c> and its rotating sun/moon disc.
///
/// <code>
///                                 DAY 2 · NIGHT
///
///
///                                                            ESC · HOW TO PLAY
/// </code>
///
/// <b>One layer, one poll.</b> The alternative — a CanvasLayer per readout, each with its own
/// <see cref="Node._Process"/> — is what the HUD was drifting toward, and it costs a per-frame
/// callback and an independent cadence per corner. Here a single 10 Hz tick fans out to each
/// widget's <c>Tick()</c>, which early-returns when its own value has not moved, so a quiet second
/// of gameplay costs a couple of comparisons rather than scene-tree walks. 10 Hz is the cadence
/// <c>InteractHighlighter</c> already established for reading live gameplay state into UI, and it
/// is well inside the ~200 ms a readout has to stay under to feel instant.
///
/// <b>Suppression is shared.</b> Every widget rides <see cref="WorldUi.Suppressed"/>, the same
/// flag the pause overlay already uses to clear world-anchored UI — so opening pause hides the
/// whole HUD as one thing rather than leaving a readout floating over the menu.
///
/// <b>Which readouts exist is per world</b> (<see cref="HudProfile"/>, BT-8). That is a different
/// question from the one above and needs a different mechanism: <see cref="WorldUi.Suppressed"/>
/// is all-or-nothing, momentary, and also clears nameplates, whereas "this world has no clock the
/// player cares about" is permanent and per widget. A suppressed widget here is never built, so it
/// costs no node and no <c>Tick()</c>. Every field below is therefore nullable, and the poll fans
/// out through <c>?.</c>.
///
/// <b>Client-only.</b> <see cref="Attach"/> is a no-op on a headless peer, matching every other
/// render-side UI in <c>Gameplay</c>.
/// </summary>
public partial class GameHud : CanvasLayer
{
    private static GameHud? _instance;

    /// <summary>See the class doc.</summary>
    private const double PollIntervalSec = 0.1;

    private DayPhaseWidget? _dayPhase;
    private HudBubbleCount? _bubbles;
    private HudHowToPlayHint? _howToPlay;
    private double _poll;

    /// <summary>Which readouts this world has a game for. Resolved once, at build.</summary>
    private HudProfile _profile = HudProfile.Full;

    /// <summary>Adds the HUD if this peer renders. Same contract as
    /// <see cref="InteractPrompt.Attach"/>: idempotent, so a second call (a reload, a resumed
    /// session) does not stack two HUDs. Both widgets read their live state through their own
    /// static-instance seams, so the HUD never reaches into the scene tree hunting for a
    /// manager.</summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsHeadless)
            return;
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
            return;
        sceneRoot.AddChild(new GameHud { Name = "GameHud" });
    }

    public override void _Ready()
    {
        _instance = this;
        Layer = Design.UiLayers.GameHud; // where SessionHud sat: above InteractPrompt's world
                    // furniture at 70, below the pause overlay.

        HudSettings.Load();

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // BT-8: which of these this world actually has a game for. Resolved once, here, because
        // the answer cannot change inside a session — the world is built before the HUD is.
        _profile = HudProfile.Current;

        // BT-8: the shared bubble tally takes the HEAD of the top-centre column and the day/phase
        // readout stacks under it (UiColumns). No world has both today, and the column derives
        // rather than assumes that.
        if (_profile.BubbleCount)
        {
            _bubbles = new HudBubbleCount();
            _bubbles.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
            _bubbles.OffsetTop = Design.UiColumns.BubbleCountTop;
            root.AddChild(_bubbles);
            _bubbles.Resized += () =>
            {
                CentreTop(_bubbles);
                PublishColumn();
            };
            CentreTop(_bubbles);
        }

        if (_profile.DayPhase)
        {
            _dayPhase = new DayPhaseWidget();
            // Top-centre: anchored to the centre preset and offset by half its own width once it
            // has one. A fixed width would have to be re-guessed every time the string changes
            // ("DAY 1 · DAY" vs "DAY 12 · NIGHT"), so it is centred on resize instead — see
            // CentreTop.
            _dayPhase.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
            _dayPhase.OffsetTop = Design.UiColumns.DayPhaseTop;
            root.AddChild(_dayPhase);
            _dayPhase.Resized += () =>
            {
                CentreTop(_dayPhase);
                PublishColumn();
            };
            CentreTop(_dayPhase);
        }

        // Bottom-right, and its own column: the permanent control-reference hint. Not polled —
        // it reports a key binding, which cannot change inside a session — so unlike its siblings
        // it is placed on resize and then left alone.
        if (_profile.HowToPlayHint)
        {
            _howToPlay = new HudHowToPlayHint();
            root.AddChild(_howToPlay);
            // Placed ONCE. Its corner is pinned by anchors and its extent comes from its own
            // content, so a viewport resize is handled by the anchors themselves — unlike the
            // top-centre occupants, which have to be re-centred on every width change.
            Design.UiColumns.PlaceHowToPlayHint(_howToPlay);
            _howToPlay.Resized += PublishColumn;
        }
        PublishColumn();
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
        // The column has to close up behind a HUD that has gone, or the strip under it would
        // stay pushed down by a readout nobody is drawing (see UiColumns).
        Design.UiColumns.DayPhaseHeight = 0f;
        Design.UiColumns.BubbleCountHeight = 0f;
        Design.UiColumns.HowToPlayHintHeight = 0f;
    }

    /// <summary>Reports what this HUD's top-centre occupants take, so the surfaces below them can
    /// stack off measured heights rather than guessed ones. Hidden counts as absent — under the
    /// pause overlay the whole HUD is gone and the column should close — and so does a widget
    /// this world's <see cref="HudProfile"/> never built.</summary>
    private void PublishColumn()
    {
        bool shown = Visible;
        Design.UiColumns.BubbleCountHeight =
            shown && _bubbles is { Visible: true } b ? b.Size.Y : 0f;
        Design.UiColumns.DayPhaseHeight = shown && _dayPhase != null ? _dayPhase.Size.Y : 0f;
        // The day/phase readout's own top depends on the rung above it, so it is re-placed here
        // rather than only at build: the tally appears one poll after the world adds its counter.
        if (_dayPhase != null)
            _dayPhase.OffsetTop = Design.UiColumns.DayPhaseTop;

        // The bottom-right column. Published only — the hint is placed once, at build, and never
        // reads this back. See the trap on UiColumns.PlaceHowToPlayHint for why that separation is
        // load-bearing rather than tidiness.
        Design.UiColumns.HowToPlayHintHeight =
            shown && _howToPlay != null ? _howToPlay.Size.Y : 0f;
    }

    public override void _Process(double delta)
    {
        Visible = !WorldUi.Suppressed;
        PublishColumn();
        if (!Visible)
            return;
        _poll += delta;
        if (_poll < PollIntervalSec)
            return;
        _poll = 0;

        _dayPhase?.Tick();
        _bubbles?.Tick();
    }

    /// <summary>Re-centres a top-anchored widget on its own current width. Called on every resize
    /// because the day/phase string changes length as the run advances.</summary>
    private static void CentreTop(Control control)
    {
        float half = control.Size.X * 0.5f;
        control.OffsetLeft = -half;
        control.OffsetRight = half;
    }
}
