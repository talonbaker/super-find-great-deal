using Godot;

namespace MpFoundation;

/// <summary>
/// Main-scene entry point. Routes to dedicated-server hosting, scripted bot client,
/// or the normal splash -> menu flow based on command-line user args.
/// </summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        var net = NetworkManager.Instance;
        var options = net.Options;
        string nextScene;

        // The interface's one styling system, before any scene builds a Control. Installed
        // unconditionally and first: every path below — the menu, gameplay, the screen demo and
        // each in-engine self-test — must be looking at the SAME theme, or a self-test proves
        // something about an interface the player never sees. Costs one Theme build; a headless
        // server simply never renders it.
        Ui.Design.UiThemeService.Install(this);

        // The graphics tier, before ANY world scene is instantiated. GraphicsQuality.Current's own
        // doc states the ordering requirement — a world scene reads it in _Ready and does not rebuild
        // afterwards — so this has to be the first thing that happens after options are parsed.
        //
        // Until Story #180 nothing ever set this and every session ran Medium (Issue #201).
        // GraphicsSettings is the deliberate writer: persisted tier for a windowed player (ship
        // default High), the historical Medium baseline for headless processes. Its printed line
        // is load-bearing — tests/Run-GraphicsTierTest.ps1 greps it as proof this assignment
        // still happens, so a future refactor that drops it goes red instead of silently
        // reverting every session to the compiled-in default again.
        GraphicsSettings.Load();
        GraphicsSettings.Apply(net.IsHeadless);

        // THIS GAME'S BODY, before any world is built and before any peer connects (FEEL-1,
        // 2026-09-20). MotorTuning.TryApply refuses while a session is live — correctly, since a
        // motor constant that moves mid-session is a desync generator — so the only place it can
        // go is here, on the one path every process (server, client, bot) passes through.
        // BrowsePace.ApplyAtBoot explains why the supermarket states its own speed rather than
        // editing the foundation's ruled default.
        Game.BrowsePace.ApplyAtBoot(options.WalkSpeedMps < 0f
            ? Game.BrowsePace.DefaultWalkSpeedMps
            : options.WalkSpeedMps);

        // --graphics wins over the settings file, deliberately and last: the parity law (plan
        // §5.2) is only demonstrable if two peers can be made to disagree about their rendering
        // and still agree about their gameplay; see LaunchOptions.GraphicsTier. The printed line is
        // what a two-tier suite greps to prove the override took.
        if (options.GraphicsTier is { } tier)
        {
            World.GraphicsQuality.Current = tier;
            GD.Print($"[graphics] tier forced to {tier} by --graphics");
        }

        // The authored clip library is gated OFF by default (ANIM-M3, 2026-08-21). Same shape and
        // same single-writer reasoning as every process-wide gate in this file: the only assignment anywhere in
        // shipping code, it only ever writes true, and only when --authored-clips was passed. Set
        // here rather than inside Gameplay or AvatarVisual because it is process-wide AND because
        // BuildAppearance reads it — which body a player is must not depend on which scene happens
        // to be loaded, or an avatar-key change mid-session could silently swap the pose source.
        if (options.AuthoredClips)
        {
            Game.Sandbox.Anim.AvatarClipFlag.AuthoredClips = true;
            GD.Print("[avatar-clips] --authored-clips given; the played body's base pose comes from " +
                     "the authored clip library for this process");
        }

        // The night brightness dial (CATCH-1, 2026-08-16). Same single-writer shape as the
        // authored-clips gate above — the only assignment anywhere in shipping code, from the one flag, set
        // process-wide because the gameplay scene can be entered more than once per session.
        //
        // PRESENTATION ONLY. It scales the rendered night ambient and moonlight and nothing else:
        // no sight curve, no ward query, no server state (canon fact 4, the parity law). Two
        // players in the same session with different values here still agree on everything that
        // decides an outcome.
        if (options.NightBrightness > 1f)
        {
            Game.World.OutdoorAtmosphere.NightBrightnessMul = options.NightBrightness;
            GD.Print($"[night-light] --night-brightness {options.NightBrightness:0.##} given; the " +
                     "RENDERED night is brightened by that factor for this process (presentation " +
                     "only — sight range, lit ground and every creature's behaviour are unchanged)");
        }

        // The body value ramp (BODY-2, 2026-08-28). Same single-writer shape as the dial above: the
        // only assignment in shipping code, from the one flag. Guarded on !IsNaN rather than on a
        // magnitude, because 0 is a MEANINGFUL setting here (a flat body — the control that proves
        // the ramp is what the captures are showing) and `> default` would silently swallow it.
        // Unset leaves the property's own env-var source (SAIL_BODY_RAMP) in charge.
        if (!float.IsNaN(options.BodyValueRamp))
        {
            Game.Sandbox.AvatarVisual.BodyValueRampStrength = options.BodyValueRamp;
            GD.Print($"[body-ramp] --body-ramp {options.BodyValueRamp:0.##} given; the player " +
                     "body's limbs-dark-to-head-light gradient is scaled by that factor for this " +
                     "process (presentation only — no sight range, no collision, nothing on the wire)");
        }

        if (options.SteamSelfTest)
        {
            // Pure-logic Steam-transport checks; no Steam client, no network, no scene.
            int exitCode = Net.Steam.SteamSelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }

        if (options.ReconnectSelfTest)
        {
            // Pure-logic reconnect-grace-window checks; no Steam client, no network, no scene.
            int exitCode = Game.ReconnectSelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }

        if (options.CycleSelfTest)
        {
            // Pure-logic tidal-cycle phase-math + light-curve checks; no network, no scene.
            int exitCode = Game.World.CycleSelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }

        // --- WP-N1 (2026-07-26 wave) -------------------------------------------------------
        if (options.NightCycleSelfTest)
        {
            // Pure-logic CycleBands checks (band math, escalation, dome-radius continuity);
            // no network, no scene.
            int exitCode = Game.World.NightCycleSelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }
        // --- end WP-N1 -----------------------------------------------------------------------

        // --- L1 (2026-08-06 lean-MVP wave, Issue #104) ----------------------------------------
        if (options.RunDriverSelfTest)
        {
            // Pure-logic phase-crossing/run-end ordinal-math checks (RunPhaseTracker); no
            // network, no scene.
            int exitCode = Game.World.RunDriverSelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }
        // --- end L1 -----------------------------------------------------------------------------

        // (The self-test entries for the quota schedule, the bubble test level, the TV portal
        // easter egg and the Puffin Lab all went with their systems at the fork - BASE-1,
        // 2026-09-19. BASE-1's own world self-test is registered below, beside the other
        // node-shaped ones, for the reason they all are: a static Run() cannot let a frame pass,
        // and anything that asserts a scene did something needs frames.)

        if (options.VoiceMuteSelfTest)
        {
            // Pure-logic mute-set checks (identity/peer-id bookkeeping); no Steam client,
            // no network, no scene.
            int exitCode = Voice.VoiceMuteSelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }

        if (options.TelemetrySelfTest)
        {
            // Pure-logic telemetry checks; send is stubbed, no real Firestore, no scene.
            int exitCode = Telemetry.TelemetrySelfTest.Run();
            GetTree().Quit(exitCode);
            return;
        }

        if (options.BuildUiTheme)
        {
            // Editor-preview export only — the running game never reads the file. See
            // UiThemeExport's doc for why regenerating is now unconditionally safe.
            GetTree().Quit(Ui.Design.UiThemeExport.Run());
            return;
        }

        if (options.SupermarketSelfTest)
        {
            // BASE-1: instances Supermarket.tscn and checks every named spawn marker plus each
            // room's packed-vs-live node count. A Node rather than a static Run() because the
            // live half of that comparison only means anything after the tree has run frames -
            // a section that builds geometry in _Ready is exactly what it exists to catch
            // (.claude/rules/godot-scenes.md). Quits itself.
            GD.Print("[boot] supermarket world self-test");
            AddChild(new Game.World.SupermarketWorldSelfTest { Name = "SupermarketWorldSelfTest" });
            return;
        }

        if (options.StockSelfTest)
        {
            // STOCK-1: the baked bulk stock against the arithmetic that baked it, and every
            // authored hole against the shipped placement check. A Node rather than a static
            // Run() for SupermarketWorldSelfTest's reason one step further on - the second half
            // needs a PHYSICS SPACE, so it needs a tree that has run frames, and posing a real
            // collider is the only way to ask "will the object the round is about actually rest
            // here" rather than to reason about it. Quits itself.
            GD.Print("[boot] stock self-test");
            AddChild(new Game.World.StockSelfTest { Name = "StockSelfTest" });
            return;
        }

        // The low-spec preset applies before any scene renders a frame (headless peers
        // have no viewport worth scaling).
        if (!net.IsHeadless)
        {
            DisplaySettings.Load();
            DisplaySettings.Apply(GetViewport());
            // Persisted audio (master/voice volume, mic device) applies the same way —
            // the settings panel writes through AudioSettings, this restores at boot.
            AudioSettings.Load();
            // The ambience tree has to exist before its volume can be restored, and boot is the
            // one place guaranteed to run before any world builds a bed. EnsureLayout is
            // idempotent, so the world calling it again later changes nothing.
            Game.World.AudioBuses.EnsureLayout();
            AudioSettings.Apply(0, Voice.VoiceManager.EnsureOutputBus(),
                AudioServer.GetBusIndex(Game.World.AudioBuses.Ambient));
            Ui.OnboardingSettings.Load();
            // W7-8: a capture run and a bot run have no human to read a first-run overlay, and a
            // panel across a windowed capture makes the PNG evidence of nothing without announcing
            // itself. Applied to the whole CATEGORY, once, here — not at each panel's door, because
            // there are already two first-run panels and a third is landing today. Both fields are
            // run-scoped and never persisted; --show-first-run-panels overrides them for the case
            // where a first-run panel IS the subject of the capture.
            Ui.OnboardingSettings.SuppressFirstRunPanels = options.SuppressesFirstRunPanels;
            Ui.OnboardingSettings.ForceShowFirstRunPanels = options.ShowFirstRunPanels;
            // TIDE branding decision 7: the OS window title reads Branding.Wordmark, the
            // one source of truth, retiring "mp-foundation" as a visible name. A runtime
            // DisplayServer call rather than editing project.godot's config/name — that
            // setting also drives export/build metadata (product name, executable naming
            // in export_presets.cfg), which a UI branding pass has no business touching.
            DisplayServer.WindowSetTitle(Ui.Branding.Wordmark);
        }

        if (options.UiThemeDemo)
        {
            // Every component, every state, both temperatures, one screen. The point of the
            // whole substrate is that a token edit is visible here in one launch.
            AddChild(new Ui.Design.UiThemeDemo { Name = "UiThemeDemo" });
            return;
        }

        if (options.UiThemeSelfTest)
        {
            // Deferred like the other in-engine self-tests: theme propagation is asserted about
            // a live tree, so it needs the tree to exist first.
            GD.Print("[boot] ui theme self-test");
            AddChild(new Ui.Design.UiThemeSelfTest { Name = "UiThemeSelfTest" });
            return;
        }

        if (options.Server)
        {
            net.Role = NetworkManager.SessionRole.Server;
            net.PendingPort = options.Port;
            // A server spawned by LocalServerHost (the Host flow) is asked to shut down
            // cleanly via its stdin, so its Steam game-server session logs off instead of
            // being orphaned by a hard kill. Servers launched directly (Practice, CI) omit
            // the flag and skip the watcher.
            if (options.ParentManaged)
                Net.Hosting.ParentShutdownWatcher.Start();
            nextScene = ScenePaths.Gameplay;
        }
        else if (options.HostSelfTest)
        {
            // Headless self-test of the Host flow's server spawn: LocalServerHost spawns
            // the same dedicated child the Host screen spawns (ENet here — CI has no
            // Steam), readiness comes from the child's own listening line, and this
            // process then joins it as a bot-like client. Run-HostingTest.ps1 asserts
            // the reap/crash guarantees around this mode.
            net.PracticeSelfTest = true;
            RunHostSelfTest(net, options);
            return; // scene change happens after the async spawn completes
        }
        else if (options.Practice)
        {
            // Headless self-test of Practice Mode: spawn the same child dedicated server
            // the menu button spawns, then join it as a (bot-like) client. Proves the
            // real spawn -> connect -> reap loop without a windowed client.
            net.PracticeSelfTest = true;
            int port = net.StartPracticeServer();
            net.Role = NetworkManager.SessionRole.Client;
            net.PendingHost = "127.0.0.1";
            net.PendingPort = port;
            net.LocalDisplayName = options.DisplayName;
            nextScene = ScenePaths.Gameplay;
        }
        else if (options.Bot)
        {
            net.Role = NetworkManager.SessionRole.Client;
            net.PendingHost = options.Host;
            net.PendingPort = options.Port;
            net.LocalDisplayName = options.DisplayName;
            // A bot given --room (and no explicit --address) resolves the code via the
            // Steam lobby directory, exercising the same code path the human Join screen
            // uses (needs a live Steam client — a live-Steam harness hook, not a CI one).
            if (options.Room.Length > 0 && !options.AddressExplicit)
                net.PendingRoom = options.Room;
            nextScene = ScenePaths.Gameplay;
        }
        else
        {
            nextScene = ScenePaths.Splash;
            // Real windowed client only (not server/bot/practice/self-test, all of which
            // return early or set nextScene above). Guarded again inside on IsHeadless +
            // config, so a windowed non-client dev launch still stays inert.
            if (!net.IsHeadless)
            {
                Telemetry.Telemetry.Instance.BeginClientSession();

                // FULLSCREEN ON START (Talon, 2026-08-30 playtest note 2) — and THIS BRANCH IS
                // THE WHOLE SAFETY ARGUMENT. Every headed thing this repo launches to prove
                // something (the capture bots, --ui-capture, --screen-demo, the windowed
                // self-tests) is a --server/--bot/--practice/self-test launch that either
                // returned early or set nextScene above, so none of them can reach this line.
                // A fullscreen default expressed in project.godot would have applied to all of
                // them, taken the one display on a shared machine, and broken the verification
                // of every other packet in the wave rather than only this one.
                //
                // --windowed is the read-only override for the remaining case: a human running
                // the real client path while headed suites are live. It does not persist.
                if (options.Windowed)
                {
                    GD.Print("[display] --windowed given; this launch stays windowed "
                             + "(the saved preference is untouched)");
                }
                else
                {
                    DisplaySettings.ApplyWindowMode();
                    // The READ-BACK, not the intent. "We asked for fullscreen" and "the window is
                    // fullscreen" are different claims, and only the second one is evidence.
                    GD.Print($"[display] window mode: wanted="
                             + $"{(DisplaySettings.Fullscreen ? "fullscreen" : "windowed")} "
                             + $"actual={DisplayServer.WindowGetMode()} "
                             + "(user://settings.cfg [display] fullscreen)");
                }
            }
        }

        // Deferred: changing scenes inside _Ready is not allowed (the tree is
        // still busy adding children at this point).
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, nextScene);
    }

    // The mode follows --transport: ENet is the CI default (no Steam anywhere); steam
    // runs the FULL production host loop against live Steam — spawn the relay child,
    // publish a fresh room code as a lobby, then rejoin our own match BY CODE through
    // the exact resolve path a friend's client uses. The live-verification hook for the
    // whole steam-native-matchmaking stack.
    private async void RunHostSelfTest(NetworkManager net, LaunchOptions options)
    {
        // async void: an unexpected throw (e.g. IO inside the spawn before its own guards)
        // would otherwise leave the headless self-test running forever — which reads as a
        // hang, not a failure, to Run-HostingTest.ps1. Route it to a hard exit instead.
        try
        {
            await RunHostSelfTestCore(net, options);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[host-selftest] FAIL: unexpected {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task RunHostSelfTestCore(NetworkManager net, LaunchOptions options)
    {
        bool steam = options.Transport == LaunchOptions.TransportSteam;
        uint appId = steam ? Net.Steam.SteamService.ResolveAppId(options) : 0;
        var server = new Net.Hosting.LocalServerHost();
        Net.Hosting.LocalServerHost.SpawnOutcome outcome =
            await server.StartAsync(options.Transport, options.World, appId);
        if (!outcome.Ok)
        {
            GD.PrintErr($"[host-selftest] FAIL: {outcome.Error}");
            server.Dispose();
            GetTree().Quit(1);
            return;
        }
        net.AdoptHostedServer(server);
        GD.Print($"[host-selftest] child ready pid {server.Pid} at {outcome.Address}");

        net.Role = NetworkManager.SessionRole.Client;
        net.LocalDisplayName = options.DisplayName;
        net.PendingRoom = "";
        net.PendingSteamId = 0;

        if (steam)
        {
            // The Host screen's flow, headless: client session, mint a code, publish
            // the lobby — then join by that code (the production resolve path).
            if (!Net.Steam.SteamService.EnsureClient(appId)
                || !Net.Steam.SteamAddress.TryParse(outcome.Address, out ulong serverSteamId))
            {
                GD.PrintErr($"[host-selftest] FAIL: {Net.Steam.SteamService.LastError}");
                GetTree().Quit(1);
                return;
            }
            string code = Net.RoomCode.Generate();
            if (!await Net.Steam.SteamLobby.CreateForMatchAsync(code, serverSteamId, options.World))
            {
                GD.PrintErr($"[host-selftest] FAIL: {Net.Steam.SteamLobby.LastError}");
                GetTree().Quit(1);
                return;
            }
            // Prove the code actually resolves before joining by it — the lobby list can
            // lag creation by a beat (a real friend types the code seconds later anyway).
            ulong resolved = 0;
            for (int attempt = 0; attempt < 8 && resolved == 0; attempt++)
            {
                if (attempt > 0)
                    await System.Threading.Tasks.Task.Delay(1500);
                (bool found, ulong id, string _) = await Net.Steam.SteamLobby.FindHostByCodeAsync(code);
                if (found)
                    resolved = id;
            }
            if (resolved != serverSteamId)
            {
                GD.PrintErr($"[host-selftest] FAIL: room {code} did not resolve back to its server (got {resolved})");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"[host-selftest] lobby published (room {code}) — resolves; joining by code");
            net.PendingRoom = code;
        }
        else if (LaunchOptions.TryParseAddress(outcome.Address, out string host, out int port))
        {
            net.PendingHost = host;
            net.PendingPort = port;
        }
        else
        {
            GD.PrintErr($"[host-selftest] FAIL: unparseable child address '{outcome.Address}'");
            GetTree().Quit(1);
            return;
        }
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, ScenePaths.Gameplay);
    }
}
