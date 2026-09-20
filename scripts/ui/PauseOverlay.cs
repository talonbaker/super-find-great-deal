using Godot;
using MpFoundation.Game;

namespace MpFoundation.Ui;

/// <summary>
/// Personal, client-side pause overlay. Deliberately does NOT pause the scene tree:
/// the server keeps simulating and other players keep moving; only this client's
/// input is released while the overlay is up.
/// </summary>
public partial class PauseOverlay : CanvasLayer
{
    private Control _menu = null!;
    private SettingsPanel _settings = null!;
    private Label _muteHeader = null!;
    private VBoxContainer _muteList = null!;
    private Label _roomCode = null!;
    private Label _roomHint = null!;
    private Control _codeRow = null!;
    private Control _roomGap = null!;
    private Button _copyButton = null!;
    private bool _enabled;

    /// <summary>The cursor state pause found when it opened, restored on close — see
    /// <see cref="Open"/>. Captured is the right default: it is what ordinary play looks
    /// like, and it is what Close() unconditionally assumed before LOSS-1.</summary>
    private Input.MouseModeEnum _mouseModeBeforeOpen = Input.MouseModeEnum.Captured;

    public override void _Ready()
    {
        // Set here as well as in the scene so the ladder has one authority. This overlay shipped
        // at layer 10 — beneath the HUD it covers, beneath the state screens, beneath every
        // sensory effect — and read as correct only because opening it also raised
        // WorldUi.Suppressed, which three of the overlays above it never check. See UiLayers.
        Layer = Design.UiLayers.PauseOverlay;
        // The one scrim. This node carried a flat black at 0.55 in the scene file — the
        // sixth distinct dim in the project, and the one the C#-only sweep could not see.
        Design.UiThemeService.BindScrim(this, "Dim");
        Visible = false;
        _menu = GetNode<Control>("Dim/Center/Panel/Margin/Menu");
        _settings = GetNode<SettingsPanel>("Dim/Center/Panel/Margin/Settings");
        _muteHeader = GetNode<Label>("Dim/Center/Panel/Margin/Menu/MuteHeader");
        _muteList = GetNode<VBoxContainer>("Dim/Center/Panel/Margin/Menu/MuteList");
        _codeRow = GetNode<Control>("Dim/Center/Panel/Margin/Menu/CodeRow");
        _roomCode = GetNode<Label>("Dim/Center/Panel/Margin/Menu/CodeRow/RoomCodeLabel");
        _roomHint = GetNode<Label>("Dim/Center/Panel/Margin/Menu/RoomHint");
        _roomGap = GetNode<Control>("Dim/Center/Panel/Margin/Menu/RoomGap");

        // Mid-game invite: ghost Copy beside the mono code, mirroring HostMenu's copy
        // affordance ("Copied!" — the latin font subset has no check glyph).
        _copyButton = GetNode<Button>("Dim/Center/Panel/Margin/Menu/CodeRow/CopyButton");
        _copyButton.Pressed += () =>
        {
            string code = NetworkManager.Instance.CurrentRoomCode;
            if (code.Length == 0)
                return;
            DisplayServer.ClipboardSet(code);
            _copyButton.Text = "Copied!";
            GetTree().CreateTimer(1.2).Timeout += () =>
            {
                if (IsInstanceValid(_copyButton))
                    _copyButton.Text = "Copy";
            };
        };

        GetNode<Button>("Dim/Center/Panel/Margin/Menu/ResumeButton").Pressed += Close;
        GetNode<Button>("Dim/Center/Panel/Margin/Menu/ResetButton").Pressed += OnReset;
        GetNode<Button>("Dim/Center/Panel/Margin/Menu/SettingsButton").Pressed += () =>
        {
            _menu.Visible = false;
            _settings.Visible = true;
        };
        GetNode<Button>("Dim/Center/Panel/Margin/Menu/HowToPlayButton").Pressed += ShowHowToPlay;
        _settings.BackRequested += () =>
        {
            _settings.Visible = false;
            _menu.Visible = true;
        };
        GetNode<Button>("Dim/Center/Panel/Margin/Menu/QuitButton").Pressed += QuitToMenu;

        var net = NetworkManager.Instance;
        _enabled = net.Role == NetworkManager.SessionRole.Client && !net.IsBot && !net.IsHeadless;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_enabled || !@event.IsActionPressed("pause"))
            return;
        if (Visible)
            Close();
        else
            Open();
        GetViewport().SetInputAsHandled();
    }

    private void Open()
    {
        // LOSS-1 (2026-08-29): remember what the cursor was doing, because pause is not always
        // opened over captured gameplay. Anything that legitimately frees the cursor — a routed
        // flow state screen, the first-run How-to-Play door — is still up
        // underneath, and Close() used to recapture unconditionally, leaving a click-only dialog
        // on screen with no cursor and no way to get one back. That is exactly the second half of
        // Talon's note 4: "there is no way for the user to select these reset / restart options".
        // Restoring what we found is a no-op for the ordinary case (open from captured play,
        // close back to captured) and correct for every other one.
        _mouseModeBeforeOpen = Input.MouseMode;
        Visible = true;
        _menu.Visible = true;
        _settings.Visible = false;
        // Mid-game invites: the code to share, front and centre (hidden if this client
        // joined without one). InteractPrompt is suppressed while the overlay is up.
        string code = NetworkManager.Instance.CurrentRoomCode;
        bool hasCode = code.Length > 0;
        _codeRow.Visible = hasCode;
        _roomHint.Visible = hasCode;
        _roomGap.Visible = hasCode;
        _roomCode.Text = code;
        InteractPrompt.Suppressed = true;
        RebuildMuteList();
        UiMotion.StaggerIn(_menu);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        // Pad/keyboard reachability: land focus on the first menu button (deferred past
        // StaggerIn's layout pass, same as MainMenu does).
        foreach (Node child in _menu.GetChildren())
        {
            if (child is Button b && b.Visible)
            {
                b.CallDeferred(Control.MethodName.GrabFocus);
                break;
            }
        }
    }

    // Personal per-player mute: a local audio control (like muting one person), NOT a
    // moderation system — it never affects or notifies the muted player. Rows follow
    // the prototype: name left, a ghost Mute/Muted toggle right.
    private void RebuildMuteList()
    {
        foreach (Node child in _muteList.GetChildren())
            child.QueueFree();
        var voice = MpFoundation.Voice.VoiceManager.Instance;
        var players = voice.GetRemotePlayers();
        _muteHeader.Visible = players.Count > 0;
        foreach ((int id, string name) in players)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = name.Length > 0 ? name : $"Player {id}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            bool muted = voice.IsMuted(id);
            var toggle = new Button
            {
                Text = muted ? "Muted" : "Mute",
                ToggleMode = true,
                ButtonPressed = muted,
                ThemeTypeVariation = "GhostAction",
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            int captured = id;
            toggle.Toggled += pressed =>
            {
                voice.SetMuted(captured, pressed);
                toggle.Text = pressed ? "Muted" : "Mute";
            };
            row.AddChild(toggle);
            _muteList.AddChild(row);
        }
    }

    private void Close()
    {
        Visible = false;
        InteractPrompt.Suppressed = false;
        Input.MouseMode = _mouseModeBeforeOpen; // see Open() — never assume there is gameplay under this.
        // The host-leave confirm is a child of this layer, not of the hidden menu — without
        // this it survived an ESC close latent, reappearing stale on the next pause (and
        // stacking a copy per Quit press).
        DismissHostLeaveConfirm();
    }

    /// <summary>The pause door onto the shared How-to-Play panel (spec §3c) — an always-
    /// available entry independent of the first-run nag, and independent of player state:
    /// this overlay never checks whether the local avatar is alive/downed/respawning (see
    /// _Ready's _enabled gate above — role/bot/headless only), so it works in every player
    /// state including dead/respawning, on both master's current state model and the
    /// unmerged tidal stack's Dead state. The panel itself references no player/avatar state
    /// either.</summary>
    private void ShowHowToPlay()
    {
        var panel = GD.Load<PackedScene>(ScenePaths.HowToPlayPanel).Instantiate<HowToPlayPanel>();
        panel.Closed += panel.QueueFree;
        AddChild(panel);
    }

    private void OnReset()
    {
        GetParent<Gameplay>().ResetLocalPlayerPosition();
        Close();
    }

    private void QuitToMenu()
    {
        // The host IS the server's owner: leaving reaps the server child and disconnects
        // everyone (ResetToOffline -> StopHostedServer). Make that consequence explicit and
        // require a deliberate confirm. A plain joiner leaving only removes themselves.
        if (NetworkManager.Instance.HostedServer != null)
        {
            ConfirmHostLeave();
            return;
        }
        DoLeave();
    }

    /// <summary>The prototype's themed end-confirm modal (a 340px panel over its own
    /// scrim — the native ConfirmationDialog ignored the theme entirely). Same flow:
    /// ghost Stay dismisses, danger End Match runs the same DoLeave path.</summary>
    private void ConfirmHostLeave()
    {
        if (_hostLeaveScrim != null)
            return; // one confirm at a time — repeat Quit presses must not stack scrims
        // The one scrim. This was the lightest of the five dims the audit measured (0.45), and
        // being the lightest is exactly what made it look deliberate rather than like drift.
        var scrim = new ColorRect { Color = Design.UiThemeService.Tokens.Scrim };
        Design.UiThemeService.Bind(scrim, t => scrim.Color = t.Scrim);
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hostLeaveScrim = scrim;

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrim.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        column.AddThemeConstantOverride("separation", Design.UiScale.SpaceNormal);
        panel.AddChild(column);

        var heading = new Label
        {
            Text = "End the match?",
            ThemeTypeVariation = "Display",
        };
        heading.ThemeTypeVariation = "Title"; // modal-scale, not hero-scale
        column.AddChild(heading);
        column.AddChild(new Label
        {
            Text = "You're hosting — leaving shuts down the server and disconnects everyone else.",
            ThemeTypeVariation = "Body",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300, 0),
        });

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", Design.UiScale.SpaceSnug);
        var stay = new Button
        {
            Text = "Stay",
            ThemeTypeVariation = "GhostAction",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var end = new Button
        {
            Text = "End Match",
            ThemeTypeVariation = "DangerAction",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        stay.Pressed += DismissHostLeaveConfirm;
        end.Pressed += () => { DismissHostLeaveConfirm(); DoLeave(); };
        buttons.AddChild(stay);
        buttons.AddChild(end);
        column.AddChild(buttons);

        AddChild(scrim);
        stay.GrabFocus(); // keyboard lands on the safe choice
    }

    private ColorRect? _hostLeaveScrim;

    private void DismissHostLeaveConfirm()
    {
        if (_hostLeaveScrim != null && GodotObject.IsInstanceValid(_hostLeaveScrim))
            _hostLeaveScrim.QueueFree();
        _hostLeaveScrim = null;
    }

    private void DoLeave()
    {
        InteractPrompt.Suppressed = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetParent<Gameplay>().LeaveToMenu();
    }
}
