using Godot;
using MpFoundation.Game;
using MpFoundation.Game.World;

namespace MpFoundation.Ui;

/// <summary>
/// L11 (Issue #114), design spec §1 step 6: the session summary shown after the run's final
/// cycle closes — per-kid quarters, group total, photos taken, map coverage — with Return to
/// Lobby / Play Again. Data comes from <see cref="ISessionSummarySource"/> (see that file's doc
/// for why this is a seam, not a direct dependency on L7/L4/L9).
///
/// Built in code with a flowing <see cref="VBoxContainer"/> column, same reasoning as
/// <see cref="LoadingHintOverlay"/>'s own layout (see that class's doc): the per-kid row count
/// is unknown at layout time, so a manual-Position layout risks the stats/buttons below it
/// overlapping a longer roster — flow avoids that by construction.
///
/// <b>Degenerate states this panel explicitly defines</b> (CLAUDE.md: "every state transition
/// defined, including the degenerate ones"):
/// <list type="bullet">
/// <item>A player who joins AFTER the run already ended never receives L1's
/// <see cref="RunDriver.RunEndedSignal"/> — it already fired, on the peers that were connected
/// at the time (L1's own contract: no replay of History to a late joiner). This panel therefore
/// also POLLS <see cref="RunDriver.Instance"/> every frame, exactly like
/// <see cref="LoadingHintOverlay"/>'s never-strand poll, and shows the summary the instant it
/// observes <c>Synced &amp;&amp; RunEnded</c> true — regardless of whether the event ever fired
/// on this peer.</item>
/// <item>Play Again pressed twice: the button latches disabled on the first press (see
/// <see cref="_playAgainRequested"/>) until <see cref="RunDriver.RunReset"/> actually dismisses
/// this panel, so a second click before the server responds never sends a second request. The
/// server-side request itself (<see cref="RunDriver.RequestResetFromClient"/>) is idempotent
/// regardless, so even two different players double-pressing is harmless.</item>
/// <item>A disconnect while the summary is open: this is a purely personal, client-local
/// CanvasLayer (like <see cref="PauseOverlay"/>) — this peer's own disconnect bounces the whole
/// scene via <see cref="Gameplay.Fail"/> (ChangeSceneToFile), which frees this node along with
/// everything else; no separate teardown is needed beyond unsubscribing in
/// <see cref="_ExitTree"/> so a stale event handler never fires against a freed node.</item>
/// </list>
/// </summary>
public partial class SessionSummaryPanel : CanvasLayer
{
    private VBoxContainer _root = null!;
    private VBoxContainer _kidRows = null!;
    private Label _groupTotalLabel = null!;
    private Label _photosLabel = null!;
    private Label _coverageLabel = null!;
    private Button _playAgainButton = null!;

    private bool _shown;
    private bool _playAgainRequested;

    public override void _Ready()
    {
        Layer = Design.UiLayers.SessionSummary; // was 70, colliding with InteractPrompt — see UiLayers.
        Visible = false;

        // The one scrim. This panel carried its own 0.72 near-black, one of the five distinct
        // dims the audit found spread across the project between 0.45 and 0.85.
        var scrim = new ColorRect { Color = Design.UiThemeService.Tokens.Scrim };
        Design.UiThemeService.Bind(scrim, t => scrim.Color = t.Scrim);
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(scrim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrim.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer { CustomMinimumSize = new Vector2(380, 0) };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", Design.UiScale.SpaceWide);
        panel.AddChild(margin);

        _root = new VBoxContainer();
        _root.AddThemeConstantOverride("separation", Design.UiScale.SpaceNormal);
        margin.AddChild(_root);

        var heading = new Label { Text = "Session Summary", ThemeTypeVariation = "Title" };
        _root.AddChild(heading);

        _kidRows = new VBoxContainer();
        _kidRows.AddThemeConstantOverride("separation", Design.UiScale.SpaceTight);
        _root.AddChild(_kidRows);

        _groupTotalLabel = NewBodyLabel();
        _root.AddChild(_groupTotalLabel);
        _photosLabel = NewBodyLabel();
        _root.AddChild(_photosLabel);
        _coverageLabel = NewBodyLabel();
        _root.AddChild(_coverageLabel);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", Design.UiScale.SpaceSnug);
        _root.AddChild(buttons);

        var returnButton = new Button
        {
            // Renamed from "Return to Lobby" (CORE-SD-1 spec §1.1/§6.16): the button calls
            // LeaveToMenu, and "Lobby" now means the between-rounds UpgradeLobby state —
            // a label promising a lobby while delivering the menu would lie twice.
            Text = "Leave to Menu",
            ThemeTypeVariation = "GhostAction",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        returnButton.Pressed += OnReturnToLobbyPressed;
        buttons.AddChild(returnButton);

        _playAgainButton = new Button { Text = "Play Again", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _playAgainButton.Pressed += OnPlayAgainPressed;
        buttons.AddChild(_playAgainButton);

        if (RunDriver.Instance is { } driver)
        {
            driver.RunEndedSignal += OnRunEnded;
            driver.RunReset += OnRunReset;
        }
    }

    public override void _ExitTree()
    {
        if (RunDriver.Instance is { } driver)
        {
            driver.RunEndedSignal -= OnRunEnded;
            driver.RunReset -= OnRunReset;
        }
        // A stale flag must not survive this node dying mid-summary (e.g. a disconnect bounces
        // the scene while the panel is open) — same discipline HowToPlayPanel/PauseOverlay apply
        // to their own global flags in _ExitTree/DoLeave.
        WorldUi.Suppressed = false;
    }

    public override void _Process(double delta)
    {
        // Never-strand poll for the late-joiner-after-end case — see class doc. Cheap (two bool
        // reads) and only ever does anything the one time it flips the panel visible.
        if (!_shown && RunDriver.Instance is { Synced: true, RunEnded: true })
            ShowSummary();
    }

    private void OnRunEnded() => ShowSummary();

    private void ShowSummary()
    {
        if (_shown)
            return;
        _shown = true;

        ISessionSummarySource data = SessionSummarySource.Current ?? SessionSummarySource.Null;

        foreach (Node child in _kidRows.GetChildren())
            child.QueueFree();
        if (data.PerKidQuarters.Count == 0)
        {
            _kidRows.AddChild(NewBodyLabel(SessionSummaryFormatter.NoKidsLine));
        }
        else
        {
            foreach (KidQuarters kid in data.PerKidQuarters)
                _kidRows.AddChild(NewBodyLabel(SessionSummaryFormatter.KidLine(kid)));
        }

        _groupTotalLabel.Text = SessionSummaryFormatter.GroupTotalLine(data);
        _photosLabel.Text = SessionSummaryFormatter.PhotosLine(data);
        _coverageLabel.Text = SessionSummaryFormatter.CoverageLine(data);

        _playAgainRequested = false;
        _playAgainButton.Disabled = false;
        _playAgainButton.Text = "Play Again";

        Visible = true;
        WorldUi.Suppressed = true; // no world-anchored UI (interact chip, nameplates) over this modal.
        UiMotion.StaggerIn(_root);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void OnRunReset()
    {
        _shown = false;
        Visible = false;
        WorldUi.Suppressed = false;
        _playAgainRequested = false;
        _playAgainButton.Disabled = false;
        _playAgainButton.Text = "Play Again";
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OnPlayAgainPressed()
    {
        if (_playAgainRequested)
            return; // double-press guard — see class doc.
        _playAgainRequested = true;
        _playAgainButton.Disabled = true;
        _playAgainButton.Text = "Waiting...";
        RunDriver.Instance?.RequestResetFromClient();
    }

    private void OnReturnToLobbyPressed()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetParent<Gameplay>().LeaveToMenu();
    }

    private static Label NewBodyLabel(string text = "") => new() { Text = text, ThemeTypeVariation = "Body" };
}
