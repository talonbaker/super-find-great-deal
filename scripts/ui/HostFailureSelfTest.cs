using System.Collections.Generic;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Ui;

/// <summary>
/// In-engine headless proof of F1 (2026-08-30 master review): <b>a failed host-side connect
/// must return the HOST to the Host screen</b>, say that hosting ended and why, and leave a
/// working re-host button — while still reaping the server child and destroying the lobby.
///
/// <para>The bug this exists to keep dead: <c>Gameplay.Fail</c> routed EVERY terminal failure
/// to <see cref="ScenePaths.JoinMenu"/>. A host who had just read a room code aloud, pressed
/// Enter game and hit the connect watchdog was dumped on a screen they never chose, with the
/// lobby already destroyed and no offer to re-host.</para>
///
/// <para><b>Why this is a scene and not an xUnit case.</b> The decision itself
/// (<see cref="SessionFailureRoute"/>) is pure and could live in dotnet test, and phase A does
/// assert it as a table. But the finding is about what a PLAYER lands on, and three of the
/// four things that had to be true are only true inside the engine: that
/// <c>HostMenu.tscn</c> constructs and renders the message, that the real
/// <c>Gameplay.Fail</c> → <c>ResetToOffline</c> → <c>ChangeSceneToFile</c> chain ends on that
/// scene, and that the same chain still kills a real child process. Phase C therefore drives
/// the production path end to end with a REAL spawned server, a real connect failure and a
/// real scene change — no stubs, nothing mocked (see ~/.claude/CLAUDE.md's no-mocking rule).</para>
///
/// <para><b>The observer trick.</b> <c>ChangeSceneToFile</c> frees the current scene, so a
/// self-test that lives in the current scene cannot watch the thing it is testing. This node
/// re-parents itself under <c>GetTree().Root</c> — a sibling of the current scene — and so
/// survives every scene change it causes.</para>
///
/// Prints one line per check and "HOSTFAIL-TEST OVERALL: PASS|FAIL"; exit 0 only if
/// everything passed.
/// Run: Godot --headless --path . res://tests/scenes/HostFailureSelfTest.tscn
/// </summary>
public partial class HostFailureSelfTest : Node
{
    /// <summary>A port nothing is listening on. The client's connect must fail rather than
    /// find something — the failure IS the scenario. High and odd to stay clear of the
    /// suite's own hardcoded ports (7818 and the dynamic hosted-server range).</summary>
    private const int DeadPort = 47913;

    /// <summary>Generous: the ENet watchdog is 8 s, and this machine runs several agents'
    /// suites at once.</summary>
    private const double FailureWaitSec = 45.0;

    private readonly List<(string Name, bool Ok)> _results = new();
    private bool _relocated;

    public override void _Ready()
    {
        // Move out of the current scene BEFORE running anything, so the scene changes this
        // test provokes cannot free the test itself. Deferred: re-parenting during _Ready is
        // a "parent is busy setting up children" error.
        CallDeferred(MethodName.Relocate);
    }

    private void Relocate()
    {
        if (_relocated)
            return;
        _relocated = true;
        Node root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        Run();
    }

    private async void Run()
    {
        // async void: an escaping exception here would leave a headless process alive with no
        // verdict, which reads to the runner as a hang rather than a failure.
        try
        {
            RouteDecisionTable();
            await HostScreenRendersTheFailure();
            await EndToEndHostConnectFailure();
            await EndToEndJoinerConnectFailure();
        }
        catch (System.Exception e)
        {
            Check("no_unhandled_exception", false);
            GD.PrintErr($"HOSTFAIL-TEST unhandled: {e}");
        }
        Finish();
    }

    // --- Phase A: the decision, as a table ------------------------------------------
    private void RouteDecisionTable()
    {
        (string joinScene, string joinMessage) = SessionFailureRoute.For(false, "Room \"ABCDEF\" not found.");
        Check("joiner_still_routes_to_join_screen", joinScene == ScenePaths.JoinMenu);
        // Acceptance criterion 3: a client that joined mid-teardown gets the clean
        // "not found"-class error, unchanged and unhidden — never a hang, never dressed up.
        Check("joiner_message_is_passed_through_verbatim", joinMessage == "Room \"ABCDEF\" not found.");

        (string hostScene, string hostMessage) = SessionFailureRoute.For(true, "Connection timed out");
        Check("host_routes_to_host_screen", hostScene == ScenePaths.HostMenu);
        // The regression this file exists for, named explicitly rather than implied by the
        // check above: the OLD behaviour was "everything lands on Join".
        Check("host_does_not_route_to_join_screen", hostScene != ScenePaths.JoinMenu);
        Check("host_message_says_hosting_ended", hostMessage.StartsWith(SessionFailureRoute.HostingEndedPrefix));
        Check("host_message_keeps_the_typed_reason", hostMessage.Contains("Connection timed out"));
        Check("host_message_offers_a_re_host", hostMessage.Contains(SessionFailureRoute.RehostHint));
        Check("host_message_punctuates_a_fragment", hostMessage.Contains("Connection timed out. "));

        // A reason that is already a sentence must not gain a second full stop.
        (string _, string sentence) = SessionFailureRoute.For(true, "Room \"ABCDEF\" not found.");
        Check("host_message_does_not_double_punctuate", !sentence.Contains("not found.. "));

        // Never leave a player on a screen with a blank error label: "it just went back" is
        // indistinguishable from a crash.
        foreach (string? empty in new string?[] { null, "", "   " })
        {
            (string _, string blank) = SessionFailureRoute.For(true, empty);
            Check($"host_message_never_blank_for_[{empty ?? "null"}]", blank.Contains(SessionFailureRoute.UnknownReason));
            (string _, string joinBlank) = SessionFailureRoute.For(false, empty);
            Check($"joiner_message_never_blank_for_[{empty ?? "null"}]", joinBlank == SessionFailureRoute.UnknownReason);
        }
    }

    // --- Phase B: the Host screen actually renders it -------------------------------
    private async System.Threading.Tasks.Task HostScreenRendersTheFailure()
    {
        var net = NetworkManager.Instance;
        (string _, string message) = SessionFailureRoute.For(true, "Connection timed out");

        net.LastError = message;
        (Control screen, Label error, Button host, Button play, LineEdit name) = await BuildHostMenu();
        Check("host_screen_shows_the_failure", error.Text == message);
        Check("host_screen_consumes_the_error", net.LastError.Length == 0);
        // "A working re-host button": present, visible, enabled AND actually connected to a
        // handler. Visibility alone would pass on a button wired to nothing.
        Check("re_host_button_is_offered", host.Visible && !host.Disabled);
        Check("re_host_button_is_wired",
            host.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0);
        // The screen must come back IDLE, not stranded mid-flow: an "Enter game" button left
        // over from the dead session would connect to a server that no longer exists.
        Check("dead_session_ui_is_not_left_standing",
            !play.Visible
            && !screen.GetNode<Control>("Column/CodeRow").Visible
            && !screen.GetNode<Control>("Column/StatusLabel").Visible);
        Check("name_field_is_editable_again", name.Editable);
        screen.QueueFree();
        await WaitFrames(1);

        // POSITIVE CONTROL for every assertion above: with no error pending, the same screen
        // built the same way must show NOTHING. Without this, "the label says X" could just be
        // a label that always says X, and the check would pass on a broken fix.
        net.LastError = "";
        (Control clean, Label cleanError, Button _, Button _, LineEdit _) = await BuildHostMenu();
        Check("control_no_error_means_no_message", cleanError.Text.Length == 0);
        clean.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task<(Control Screen, Label Error, Button Host, Button Play, LineEdit Name)>
        BuildHostMenu()
    {
        var screen = GD.Load<PackedScene>(ScenePaths.HostMenu).Instantiate<Control>();
        AddChild(screen);
        await WaitFrames(2);
        return (screen,
            screen.GetNode<Label>("Column/ErrorLabel"),
            screen.GetNode<Button>("Column/HostButton"),
            screen.GetNode<Button>("Column/PlayButton"),
            screen.GetNode<LineEdit>("Column/NameEdit"));
    }

    // --- Phase C: the whole production path, with a real server child ---------------
    private async System.Threading.Tasks.Task EndToEndHostConnectFailure()
    {
        var net = NetworkManager.Instance;

        // A real dedicated-server child, spawned exactly the way HostMenu spawns one. ENet
        // rather than Steam because CI has no Steam client; the spawn/adopt/reap machinery
        // under test is transport-agnostic (same reasoning as Run-HostingTest.ps1).
        var server = new Net.Hosting.LocalServerHost();
        Net.Hosting.LocalServerHost.SpawnOutcome spawn =
            await server.StartAsync(LaunchOptions.TransportEnet, net.Options.World, 0);
        if (!spawn.Ok)
        {
            Check("hosted_child_spawned", false);
            GD.PrintErr($"HOSTFAIL-TEST spawn failed: {spawn.Error}");
            server.Dispose();
            return;
        }
        int childPid = server.Pid;
        net.AdoptHostedServer(server);
        Check("hosted_child_spawned", childPid != 0);
        // POSITIVE CONTROL for the orphan check below. A "the process is gone" assertion that
        // has never been shown to observe a LIVE process proves nothing at all — this repo has
        // lost four calls to exactly that mistake (tasklist /FI fails open and silent).
        Check("control_child_is_alive_before_teardown", ProcessIsAlive(childPid));

        // The scenario, verbatim: the host has a server and a code, presses Enter game, and
        // the connect never completes. The dead port is what stands in for the cold-relay
        // watchdog — the SAME Fail() path, over the transport CI actually has.
        net.Role = NetworkManager.SessionRole.Client;
        net.LocalDisplayName = "HostFail";
        net.CurrentRoomCode = "ABCDEF";
        net.PendingRoom = "";
        net.PendingSteamId = 0;
        net.PendingHost = "127.0.0.1";
        net.PendingPort = DeadPort;
        net.IsHostFlow = true;

        GetTree().ChangeSceneToFile(ScenePaths.Gameplay);
        bool landed = await WaitForScene(nameof(HostMenu));
        Check("host_lands_on_the_host_screen", landed);
        if (!landed)
            return;

        var screen = (Control)GetTree().CurrentScene;
        string shown = screen.GetNode<Label>("Column/ErrorLabel").Text;
        GD.Print($"HOSTFAIL-TEST host screen error label reads: \"{shown}\"");
        Check("host_is_told_hosting_ended", shown.StartsWith(SessionFailureRoute.HostingEndedPrefix));
        Check("host_is_told_why", shown.Contains("Connection timed out") || shown.Contains("Could not connect"));
        Check("host_is_offered_a_new_code", shown.Contains(SessionFailureRoute.RehostHint));
        Button hostButton = screen.GetNode<Button>("Column/HostButton");
        Check("re_host_button_works_after_the_real_failure",
            hostButton.Visible && !hostButton.Disabled
            && hostButton.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0);

        // Acceptance criterion 2 — the teardown still happens. A joinable lobby with no server
        // behind it would be worse than none, so this is not "collateral we tolerate": it is
        // required, and it is asserted rather than assumed.
        Check("hosted_child_was_released", net.HostedServer == null);
        Check("hosted_child_left_no_orphan", await WaitForProcessExit(childPid, 20.0));
        Check("no_zombie_lobby", Net.Steam.SteamLobby.CurrentLobbyId == 0);
        // The flag is session state: the session is over, so the next Fail must not think it
        // is still hosting.
        Check("host_flow_flag_cleared_by_teardown", !net.IsHostFlow);
    }

    // --- Phase D: the same failure for a JOINER still lands on Join ------------------
    private async System.Threading.Tasks.Task EndToEndJoinerConnectFailure()
    {
        // The control for phase C. Without it, "the host lands on Host" is equally consistent
        // with "everything now lands on Host", which would be a new bug wearing the fix's
        // clothes — and would break the joiner's own retry screen.
        var net = NetworkManager.Instance;
        net.Role = NetworkManager.SessionRole.Client;
        net.LocalDisplayName = "JoinFail";
        net.CurrentRoomCode = "";
        net.PendingRoom = "";
        net.PendingSteamId = 0;
        net.PendingHost = "127.0.0.1";
        net.PendingPort = DeadPort;
        net.IsHostFlow = false;

        GetTree().ChangeSceneToFile(ScenePaths.Gameplay);
        bool landed = await WaitForScene(nameof(JoinMenu));
        Check("control_joiner_lands_on_the_join_screen", landed);
        if (!landed)
            return;

        string shown = ((Control)GetTree().CurrentScene).GetNode<Label>("Column/ErrorLabel").Text;
        GD.Print($"HOSTFAIL-TEST join screen error label reads: \"{shown}\"");
        Check("control_joiner_is_not_told_about_hosting",
            !shown.Contains(SessionFailureRoute.HostingEndedPrefix) && shown.Length > 0);
    }

    // --- helpers --------------------------------------------------------------------
    private async System.Threading.Tasks.Task<bool> WaitForScene(string typeName)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(FailureWaitSec * 1000);
        while (Time.GetTicksMsec() < deadline)
        {
            await WaitFrames(10);
            Node? current = GetTree().CurrentScene;
            if (current is not null && GodotObject.IsInstanceValid(current) && current.GetType().Name == typeName)
                return true;
        }
        GD.PrintErr($"HOSTFAIL-TEST timed out waiting for {typeName}; current scene is " +
            $"{GetTree().CurrentScene?.GetType().Name ?? "nothing"}");
        return false;
    }

    private static bool ProcessIsAlive(int processId)
    {
        try
        {
            using System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(processId);
            return !p.HasExited;
        }
        catch (System.ArgumentException)
        {
            return false; // no such process
        }
    }

    private async System.Threading.Tasks.Task<bool> WaitForProcessExit(int processId, double timeoutSec)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(timeoutSec * 1000);
        while (Time.GetTicksMsec() < deadline)
        {
            if (!ProcessIsAlive(processId))
                return true;
            await WaitFrames(10);
        }
        return false;
    }

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string name, bool ok)
    {
        _results.Add((name, ok));
        GD.Print($"HOSTFAIL-TEST {name}: {(ok ? "PASS" : "FAIL")}");
    }

    private void Finish()
    {
        int failed = _results.FindAll(r => !r.Ok).Count;
        GD.Print($"HOSTFAIL-TEST OVERALL: {(failed == 0 ? "PASS" : "FAIL")} ({_results.Count - failed}/{_results.Count})");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
