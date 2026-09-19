using Godot;

namespace MpFoundation.Ui;

public partial class JoinMenu : Control
{
    private LineEdit _nameEdit = null!;
    private LineEdit _roomEdit = null!;
    private Label _addressLabel = null!;
    private LineEdit _addressEdit = null!;
    private Label _roomLabel = null!;
    private Button _directToggle = null!; // a PillToggle — Button in toggle mode
    private Label _errorLabel = null!;

    public override void _Ready()
    {
        _nameEdit = GetNode<LineEdit>("Column/NameEdit");
        _roomLabel = GetNode<Label>("Column/RoomLabel");
        _roomEdit = GetNode<LineEdit>("Column/RoomEdit");
        _addressLabel = GetNode<Label>("Column/AddressLabel");
        _addressEdit = GetNode<LineEdit>("Column/AddressEdit");
        _directToggle = GetNode<Button>("Column/DirectRow/DirectToggle");
        _errorLabel = GetNode<Label>("Column/ErrorLabel");

        _directToggle.Toggled += OnDirectToggled;
        GetNode<Button>("Column/ConnectButton").Pressed += OnConnectPressed;

        // Enter walks the form: name hops to the code (or address) field, then submitting
        // the code/address joins — nobody should have to reach for the mouse.
        _nameEdit.TextSubmitted += _ =>
            (_directToggle.ButtonPressed ? _addressEdit : _roomEdit).GrabFocus();
        _roomEdit.TextSubmitted += _ => OnConnectPressed();
        _addressEdit.TextSubmitted += _ => OnConnectPressed();

        GetNode<Button>("TopBar/BackButton").Pressed += () => GetTree().ChangeSceneToFile(ScenePaths.MainMenu);

        // Surface the reason we were bounced back here (failed connect, bad code, full).
        var net = NetworkManager.Instance;
        if (net.LastError.Length > 0)
        {
            _errorLabel.Text = net.LastError;
            net.LastError = "";
        }

        UiMotion.StaggerIn(GetNode<Control>("Column"));

        // A name is required to join, so the caret starts in the name field — no click first.
        // Deferred so the grab lands after StaggerIn's first-frame layout pass.
        _nameEdit.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Fail(string message, Control field)
    {
        _errorLabel.Text = message;
        UiMotion.Shake(field);
    }

    private void OnDirectToggled(bool direct)
    {
        _roomLabel.Visible = !direct;
        _roomEdit.Visible = !direct;
        _addressLabel.Visible = direct;
        _addressEdit.Visible = direct;
    }

    private void OnConnectPressed()
    {
        string displayName = _nameEdit.Text.Trim();
        if (displayName.Length == 0)
        {
            Fail("Enter a display name.", _nameEdit);
            return;
        }

        var net = NetworkManager.Instance;
        net.Role = NetworkManager.SessionRole.Client;
        net.LocalDisplayName = displayName;
        // AVATAR-1 (2026-09-05): LocalAvatarKey is deliberately LEFT UNSET — see the identical
        // note in HostMenu.OnPlayPressed. Talon: "I don't want players to be able to change for
        // this initial build because of the movements don't line up right." Unset is the
        // existing "nobody chose" case, so SandboxAvatar resolves
        // ResolveEnvAvatarKey(world) -> PreferredAvatarKey == BoxKidAvatarKey. One source of
        // truth for the played body, not two.
        // Explicit rather than inherited: a joiner is never a host, so a failure bounces back
        // to THIS screen. ResetToOffline already clears the flag on every path that could have
        // set it — stating it at the point of intent is what stops some future path that skips
        // the teardown from silently routing a joiner to the Host screen.
        net.IsHostFlow = false;

        if (_directToggle.ButtonPressed)
        {
            // The direct field accepts either a raw endpoint or a steam:<steamid64>
            // identity (printed by a Steam-relay host at startup) — handy for
            // connectivity tests without going through a room code at all.
            if (Net.Steam.SteamAddress.TryParse(_addressEdit.Text, out ulong steamId))
            {
                net.PendingRoom = "";
                net.PendingSteamId = steamId;
            }
            else if (LaunchOptions.TryParseAddress(_addressEdit.Text, out string host, out int port))
            {
                net.PendingRoom = "";
                net.PendingSteamId = 0;
                net.PendingHost = host;
                net.PendingPort = port;
            }
            else
            {
                Fail("Enter the server address as host, host:port, or steam:<id>.", _addressEdit);
                return;
            }
            net.CurrentRoomCode = "";
            net.JoiningRoomCode = ""; // direct/LAN join presents no code
        }
        else
        {
            // The code resolves via the Steam lobby directory in Gameplay (same pattern
            // as before: failures bounce back here with a reason in the error label).
            string code = _roomEdit.Text.Trim().ToUpperInvariant();
            if (!Net.RoomCode.IsValid(code))
            {
                Fail($"Room codes are {Net.RoomCode.Length} letters.", _roomEdit);
                return;
            }
            net.PendingRoom = code;
            net.PendingSteamId = 0;
            // Present the code in the handshake so a code-gated server admits this joiner.
            net.JoiningRoomCode = code;
        }

        GetTree().ChangeSceneToFile(ScenePaths.Gameplay);
    }
}
