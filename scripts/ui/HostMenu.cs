using System.Threading.Tasks;
using Godot;
using MpFoundation.Net;
using MpFoundation.Net.Hosting;
using MpFoundation.Net.Steam;

namespace MpFoundation.Ui;

/// <summary>
/// The Host screen: name in, room code out. Pressing Host spawns the dedicated server as
/// a child of THIS client (LocalServerHost — readiness read from the child's own
/// listening line, orphan-reaping by job object), creates the public Steam lobby that
/// maps the freshly minted room code to the child's relay identity (SteamLobby), then
/// shows the code prominently and copyably. "Enter game" connects through the exact same
/// steam:&lt;id64&gt; client path a joiner uses.
///
/// Failure honesty: Steam down, spawn failure, and lobby failure each land in the error
/// label in plain language, with everything already-started torn back down.
/// </summary>
public partial class HostMenu : Control
{
    private LineEdit _nameEdit = null!;
    private Label _statusLabel = null!;
    private Label _codeLabel = null!;
    private LineEdit _codeEdit = null!;
    private HBoxContainer _codeRow = null!;
    private Button _copyButton = null!;
    private Button _hostButton = null!;
    private Button _playButton = null!;
    private Button _cancelButton = null!;
    private Label _errorLabel = null!;
    private HBoxContainer _dotsRow = null!;

    private ulong _serverSteamId;
    private string _roomCode = "";

    // Bumped whenever the player cancels/leaves; in-flight async host attempts compare
    // against it and abandon their result instead of resurrecting a torn-down session.
    private int _generation;

    public override void _Ready()
    {
        _nameEdit = GetNode<LineEdit>("Column/NameEdit");
        _statusLabel = GetNode<Label>("Column/StatusLabel");
        _codeLabel = GetNode<Label>("Column/CodeLabel");
        _codeRow = GetNode<HBoxContainer>("Column/CodeRow");
        _codeEdit = GetNode<LineEdit>("Column/CodeRow/CodeEdit");
        _copyButton = GetNode<Button>("Column/CodeRow/CopyButton");

        // Room-code hero treatment: "Display" is a Label-base type variation (UiThemeFactory),
        // so it doesn't resolve on a LineEdit — the .tscn already gets the size/weight from an
        // explicit font override instead. What LineEdit's read-only style CAN'T give us is a
        // mint rule (its read_only stylebox draws a neutral border colour); this ColorRect —
        // one source via GetThemeColor("mint","Accent") — paints that rule under the code
        // itself. Mirror this exact child (name, anchors, 2px band) for the pause-overlay hint.
        var codeUnderline = new ColorRect
        {
            Name = "MintUnderline",
            Color = GetThemeColor("mint", "Accent"),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -3f,
            OffsetBottom = 0f,
        };
        _codeEdit.AddChild(codeUnderline);
        _errorLabel = GetNode<Label>("Column/ErrorLabel");
        _hostButton = GetNode<Button>("Column/HostButton");
        _playButton = GetNode<Button>("Column/PlayButton");
        _cancelButton = GetNode<Button>("Column/CancelButton");

        _hostButton.Pressed += OnHostPressed;
        _playButton.Pressed += OnPlayPressed;
        _cancelButton.Pressed += OnCancelPressed;
        _copyButton.Pressed += OnCopyPressed;
        GetNode<Button>("TopBar/BackButton").Pressed += OnBackPressed;
        _nameEdit.TextSubmitted += _ => OnHostPressed(); // Enter on the name starts hosting

        // The prototype's "starting" beat: three periwinkle dots pulsing on offset
        // clocks above the status line. Shown/hidden in lockstep with StatusLabel.
        _dotsRow = GetNode<HBoxContainer>("Column/DotsRow");
        Color accent = GetThemeColor("teal", "Accent");
        for (int i = 0; i < 3; i++)
        {
            var box = new StyleBoxFlat { BgColor = accent };
            box.SetCornerRadiusAll(4);
            var dot = new Panel { CustomMinimumSize = new Vector2(8, 8) };
            dot.AddThemeStyleboxOverride("panel", box);
            _dotsRow.AddChild(dot);
            Tween pulse = dot.CreateTween().SetLoops();
            pulse.TweenInterval(i * 0.18);
            pulse.TweenProperty(dot, "modulate:a", 0.3f, 0.45).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(dot, "modulate:a", 1f, 0.45).SetTrans(Tween.TransitionType.Sine);
        }

        // Surface the reason we were bounced back HERE rather than to Join: a host whose own
        // connect failed (F1, 2026-08-30). The scene reloads in its authored idle state — Host
        // button shown, code row and Enter-game hidden — so the re-host offer is already
        // standing; what was missing was being told that hosting had ended and why. Same
        // shape as JoinMenu's, and consumed so it cannot reappear on a later visit.
        var net = NetworkManager.Instance;
        if (net.LastError.Length > 0)
        {
            _errorLabel.Text = net.LastError;
            net.LastError = "";
        }

        UiMotion.StaggerIn(GetNode<Control>("Column"));

        // A name is required to host, so the caret starts in the name field — no click first.
        // Deferred so the grab lands after StaggerIn's first-frame layout pass.
        _nameEdit.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void OnHostPressed()
    {
        string displayName = _nameEdit.Text.Trim();
        if (displayName.Length == 0)
        {
            FailIdle("Enter a display name.", _nameEdit);
            return;
        }
        _errorLabel.Text = "";
        _nameEdit.Editable = false;
        _hostButton.Visible = false;
        _cancelButton.Visible = true;
        _ = RunHostAsync(_generation);
    }

    // Discarded-task guard: an unexpected throw inside HostAsync would otherwise vanish
    // unobserved, leaving the screen stuck on the spinner with no error and no way back
    // except Cancel. Route it to the normal BackToIdle failure path.
    private async Task RunHostAsync(int generation)
    {
        try
        {
            await HostAsync(generation);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[host] hosting flow threw {e.GetType().Name}: {e.Message}");
            if (generation == _generation && IsInsideTree())
                BackToIdle("Hosting failed unexpectedly.");
        }
    }

    private async Task HostAsync(int generation)
    {
        var net = NetworkManager.Instance;

        // The interactive Host flow lands in the bubble test (BubbleTest.tscn) rather than the
        // code-built scaffolding — this is the one path a human actually plays through, so it's
        // the one that should show the world the movement-and-feel MVP is asking about. Read by
        // both the server child below (as its --world) and this client's own Gameplay._Ready()
        // once OnPlayPressed loads the match scene, so host and server always agree on the world.
        //
        // An explicit --world WINS. This assignment was written when "playground" was both the
        // default and the only authored world, so overwriting was harmless; the dev-lab worlds
        // (houselab, hoodlab) made it a silent clobber of a deliberate choice, and since the
        // Practice button was removed from MainMenu, Host is the only way in — so forcing here
        // made those worlds unreachable by a human entirely. Flipped "playground" -> "camp" by
        // L2 (2026-08-06, docs/superpowers/specs/2026-08-05-lean-mvp-and-camp-layout.md) and
        // "camp" -> "bubbletest" by LAUNCH-1 (2026-08-28, MVP integration plan §2.4); the MVP
        // extraction then retired every other world (see LaunchOptions.DefaultWorld).
        if (!net.Options.WorldExplicit)
            net.Options.World = LaunchOptions.DefaultWorld;

        // 1. The host's own Steam session — the lobby must be owned by a logged-in
        //    client (the SDK has no game-server lobby API), so this is the first gate.
        uint appId = SteamService.ResolveAppId(net.Options);
        SetStatus("Talking to Steam…");
        if (!SteamService.EnsureClient(appId))
        {
            BackToIdle(SteamService.LastError);
            return;
        }

        // 2. The room code is minted BEFORE the server spawns so it can be handed to the child
        //    as --room-code — the server then admits only joiners (including this host) that
        //    present it, turning the code into a real join capability rather than a lobby label.
        string code = RoomCode.Generate();

        // 3. The dedicated server, spawned as our child. Its anonymous logon takes a
        //    network round-trip; readiness is its own "listening via Steam relay" line.
        SetStatus("Starting the match server…");
        var server = new LocalServerHost();
        LocalServerHost.SpawnOutcome spawn =
            await server.StartAsync(LaunchOptions.TransportSteam, net.Options.World, appId, code);
        if (generation != _generation || !IsInsideTree())
        {
            server.Dispose(); // player cancelled/left while we were spawning
            return;
        }
        if (!spawn.Ok || !SteamAddress.TryParse(spawn.Address, out ulong serverSteamId))
        {
            server.Dispose();
            BackToIdle(spawn.Ok ? "The match server came up with an unusable address." : spawn.Error);
            return;
        }
        net.AdoptHostedServer(server);

        // 4. Publish that code as Steam lobby metadata so joiners can resolve the server.
        SetStatus("Creating the Steam lobby…");
        bool lobbyOk = await SteamLobby.CreateForMatchAsync(code, serverSteamId, net.Options.World);
        if (generation != _generation || !IsInsideTree())
        {
            net.StopHostedServer();
            SteamLobby.LeaveCurrent();
            return;
        }
        if (!lobbyOk)
        {
            net.StopHostedServer();
            BackToIdle(SteamLobby.LastError);
            return;
        }

        // Ready: show the code big and copyable; the host shares it, then enters.
        _serverSteamId = serverSteamId;
        _roomCode = code;
        net.CurrentRoomCode = code;
        _statusLabel.Visible = false;
        _dotsRow.Visible = false;
        _codeLabel.Visible = true;
        _codeRow.Visible = true;
        _codeEdit.Text = code;
        _playButton.Visible = true;
    }

    private void OnPlayPressed()
    {
        if (_serverSteamId == 0)
            return;
        var net = NetworkManager.Instance;
        net.Role = NetworkManager.SessionRole.Client;
        net.LocalDisplayName = _nameEdit.Text.Trim();
        // AVATAR-1 (2026-09-05): LocalAvatarKey is deliberately LEFT UNSET. Talon, on the last
        // change before the Steam playtest upload: "I need this to only be using the main model
        // ... I don't want players to be able to change for this initial build because of the
        // movements don't line up right." Unset is the existing "nobody chose" case, so
        // SandboxAvatar falls through to AvatarVisual.ResolveEnvAvatarKey(world) ->
        // PreferredAvatarKeyFor -> PreferredAvatarKey == BoxKidAvatarKey. That mechanism already
        // existed and is what headless bots and CI have always used; writing "boxkid" here
        // instead would be a SECOND source of truth for the played body, and the whole bug BODY-1
        // fixed was two such sources disagreeing. Restoring the picker is restoring this line.
        net.PendingRoom = "";
        net.PendingSteamId = _serverSteamId;
        net.CurrentRoomCode = _roomCode;
        // From here on this process is a HOST, not just a client: it owns the server child and
        // the lobby. A terminal failure in Gameplay reads this to send us back to THIS screen
        // instead of the Join screen (SessionFailureRoute). Cleared by ResetToOffline.
        net.IsHostFlow = true;
        // The host joins its OWN server by Steam id, so it must present the code too or its own
        // server's room-code gate would refuse it.
        net.JoiningRoomCode = _roomCode;
        GetTree().ChangeSceneToFile(ScenePaths.Gameplay);
    }

    private void OnCopyPressed()
    {
        if (_roomCode.Length == 0)
            return;
        DisplayServer.ClipboardSet(_roomCode);
        _copyButton.Text = "Copied!";
        GetTree().CreateTimer(1.2).Timeout += () =>
        {
            if (IsInstanceValid(_copyButton))
                _copyButton.Text = "Copy";
        };
    }

    private void OnCancelPressed() => TeardownToIdle("");

    private void OnBackPressed()
    {
        Teardown();
        GetTree().ChangeSceneToFile(ScenePaths.MainMenu);
    }

    // --- helpers -------------------------------------------------------------------

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        _statusLabel.Visible = true;
        _dotsRow.Visible = true;
    }

    /// <summary>Validation failure while still idle (nothing started yet).</summary>
    private void FailIdle(string message, Control field)
    {
        _errorLabel.Text = message;
        UiMotion.Shake(field);
    }

    /// <summary>A step failed after the flow started: surface why, return to idle.</summary>
    private void BackToIdle(string message) => TeardownToIdle(message);

    private void TeardownToIdle(string message)
    {
        Teardown();
        _errorLabel.Text = message;
        _statusLabel.Visible = false;
        _dotsRow.Visible = false;
        _codeLabel.Visible = false;
        _codeRow.Visible = false;
        _playButton.Visible = false;
        _cancelButton.Visible = false;
        _hostButton.Visible = true;
        _nameEdit.Editable = true;
    }

    /// <summary>Abandons the hosted session: reap the child, destroy the lobby (the room
    /// code stops resolving the moment its lobby dies), invalidate in-flight attempts.</summary>
    private void Teardown()
    {
        _generation++;
        var net = NetworkManager.Instance;
        net.StopHostedServer();
        SteamLobby.LeaveCurrent();
        net.CurrentRoomCode = "";
        _serverSteamId = 0;
        _roomCode = "";
    }
}
