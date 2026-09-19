using System;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace MpFoundation.Net.Steam;

/// <summary>
/// Owns the Steamworks lifecycles — the client session (a player's own Steam login) and
/// the anonymous game-server session (a dedicated match server's identity). Everything
/// Steam-global lives here so the rest of the codebase touches Steam through exactly two
/// doors: this service for identity/lifecycle, and <see cref="SteamPeer"/> for transport.
///
/// Design constraints this encodes:
///  - Headless CI machines have no Steam client and often no native steam_api library.
///    Every entry point fails soft (typed false + LastError), never throws into game code.
///  - The App ID has ONE resolution path (<see cref="ResolveAppId"/>): CLI flag, then
///    Valve's own SteamAppId/SteamGameId env vars (what Steam sets when IT launches the
///    build), then this project's STEAM_APP_ID dev override, then steam_appid.txt, then
///    Valve's public test app 480 ("Spacewar"). Swapping in the real App ID later is a
///    config change, not a code change. See docs/STEAM.md.
///  - The dedicated server uses Valve's anonymous game-server login (no Steam account,
///    no Talon friendship required) — the identity that room codes resolve to.
/// </summary>
public static class SteamService
{
    /// <summary>Valve's public test App ID ("Spacewar") — the documented dev/prototyping
    /// fallback until the real App ID exists. Works with any logged-in Steam client.</summary>
    public const uint SpacewarAppId = NetProfile.FallbackSteamAppId;

    public static string LastError { get; private set; } = "";
    public static bool ClientStarted { get; private set; }
    public static bool ServerStarted { get; private set; }

    /// <summary>The anonymous game-server SteamID64, valid once <see cref="StartGameServer"/>
    /// has returned true. This is what the server registers with matchmaking.</summary>
    public static ulong ServerSteamId { get; private set; }

    private static bool _resolverInstalled;

    // --- App ID resolution --------------------------------------------------------
    /// <summary>
    /// Valve's OWN environment variable names for the running App ID, in the order Valve
    /// documents them. A build that Steam launched arrives with these already set to the real
    /// App ID — this is Steam's hand-off, and until 2026-08-30 this class did not read it
    /// (it read only the project-local <c>STEAM_APP_ID</c>, a name Steam never sets, and then
    /// OVERWROTE both of these with whatever the fallback chain produced). That defect could
    /// only ever fire once the real App ID existed, which is why it was invisible: a build
    /// launched by Steam under the real ID would have initialised as Spacewar.
    ///
    /// <c>SteamGameId</c> is second on purpose: for a plain app it equals the App ID, but for
    /// a mod/shortcut it is a PACKED 64-bit value whose low 32 bits are the App ID and whose
    /// upper bits are not. <see cref="TryEnvAppId"/> parses as <c>uint</c>, so a packed value
    /// simply fails to parse and falls through rather than yielding a wrong ID — the safe
    /// direction, and the reason this is not a <c>ulong</c> parse plus a mask.
    /// </summary>
    private static readonly string[] ValveAppIdEnvNames = { "SteamAppId", "SteamGameId" };

    /// <summary>
    /// The single resolution path, highest authority first:
    /// <list type="number">
    /// <item><c>--steam-app-id</c> — a deliberate developer override typed on this launch. It
    /// outranks Steam's own env because it can only be present when a human put it there, and
    /// a Steam-launched build never carries it.</item>
    /// <item><c>SteamAppId</c>, then <c>SteamGameId</c> — Valve's hand-off (see
    /// <see cref="ValveAppIdEnvNames"/>).</item>
    /// <item><c>STEAM_APP_ID</c> — this project's own dev override, kept for existing scripts
    /// and docs. Ranks BELOW Valve's names so a stale value in a developer's shell can never
    /// beat the ID Steam actually launched the build under.</item>
    /// <item><c>steam_appid.txt</c> — gitignored local dev file.</item>
    /// <item>Spacewar (480) — correct today; the real numbers are still pending
    /// (docs/store/2026-08-29-STEAM-STATE-private-playtest.md §3b).</item>
    /// </list>
    /// </summary>
    public static uint ResolveAppId(LaunchOptions options)
    {
        if (options.SteamAppId != 0)
            return options.SteamAppId;

        foreach (string name in ValveAppIdEnvNames)
        {
            if (TryEnvAppId(name, out uint fromValve))
                return fromValve;
        }

        if (TryEnvAppId("STEAM_APP_ID", out uint fromEnv))
            return fromEnv;

        if (TryAppIdFile(out uint fromFile))
            return fromFile;

        return SpacewarAppId;
    }

    /// <summary>An env var holding a usable App ID. Invariant-culture and zero-rejecting:
    /// a zero App ID cannot init, so "0" is treated exactly like "absent".</summary>
    private static bool TryEnvAppId(string name, out uint appId)
    {
        appId = 0;
        string? raw = System.Environment.GetEnvironmentVariable(name);
        return raw is not null
            && uint.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out appId)
            && appId != 0;
    }

    /// <summary>
    /// steam_appid.txt — gitignored, never a place the real App ID gets committed by
    /// accident. Probed in two homes: the project root (dev runs, where "beside the
    /// executable" would mean the Godot install directory — a place no per-project
    /// file belongs; in exports res:// globalizes into the pack and File.Exists is
    /// simply false) and beside the executable (packaged builds launched outside
    /// Steam, how Valve's own tooling finds it).
    /// <para>Internal rather than private so <see cref="SteamSelfTest"/> can assert the
    /// no-override fallback EXACTLY: on a machine that happens to have a steam_appid.txt,
    /// "bare resolution lands on Spacewar" is false and asserting it would be a flake.</para>
    /// </summary>
    internal static bool TryAppIdFile(out uint appId)
    {
        appId = 0;
        string[] appIdDirs =
        {
            ProjectSettings.GlobalizePath("res://"),
            Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
        };
        foreach (string dir in appIdDirs)
        {
            try
            {
                if (dir.Length == 0)
                    continue;
                string file = Path.Combine(dir, "steam_appid.txt");
                if (File.Exists(file)
                    && uint.TryParse(File.ReadAllText(file).Trim(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out uint fromFile)
                    && fromFile != 0)
                {
                    appId = fromFile;
                    return true;
                }
            }
            catch
            {
                // Unreadable file == absent file.
            }
        }
        return false;
    }

    // --- Client session ------------------------------------------------------------
    /// <summary>Initializes the player-facing Steam session (requires a running, logged-in
    /// Steam client). Idempotent; false + LastError on any failure.</summary>
    public static bool EnsureClient(uint appId)
    {
        if (ClientStarted)
            return true;
        if (!PrepareNative(appId))
            return false;

        try
        {
            if (!SteamAPI.Init())
            {
                LastError = "Steam is not available — make sure Steam is running and you are logged in.";
                return false;
            }
        }
        catch (Exception ex)
        {
            LastError = $"Steam init failed: {ex.GetType().Name}";
            return false;
        }

        ClientStarted = true;
        // Warm up the relay network early so the first ConnectP2P doesn't pay the full
        // cert/ping-measurement cost inside the player's connect timeout.
        //
        // Known benign noise from this call (and the ICE candidate gathering it triggers on
        // connect): the Steam SDK's own native layer sometimes prints
        //   "steamnetworkingsockets_p2p_ice.cpp (823) : Assertion Failed: We gathered candidate
        //   type 0x4, but 0x202 is allowed"
        // to stderr, once, right around world-spawn time. This is a GameNetworkingSockets-internal
        // ICE candidate-type mismatch, unrelated to Godot's renderer/shaders/Environment resources
        // — do not chase it as a rendering bug. It was investigated end-to-end (2026-07-23,
        // docs/superpowers/2026-07-22-playground-atmosphere-fix-and-mvp-lock-dispatch.md) after
        // being mistaken for the cause of a suspected dusk-atmosphere render failure; the
        // atmosphere pass (sky, water shader, lighting) rendered correctly in that same session.
        SteamNetworkingUtils.InitRelayNetworkAccess();
        GD.Print($"[steam] client session up (app {appId}, user {SteamUser.GetSteamID().m_SteamID})");
        return true;
    }

    // --- Anonymous game-server session ----------------------------------------------
    /// <summary>
    /// Brings up the dedicated server's Steam identity: init, anonymous logon, relay
    /// access. Blocks (pumping callbacks) until the logon completes so the caller gets a
    /// valid <see cref="ServerSteamId"/> synchronously — a headless server at startup has
    /// nothing better to do, and it keeps NetworkManager.StartServer's contract.
    /// gamePort/queryPort only need to be unique per server process on one machine
    /// (the provisioner's per-child port allocation already guarantees that).
    /// </summary>
    public static bool StartGameServer(uint appId, ushort gamePort, ushort queryPort, double logonTimeoutSec = 15.0)
    {
        if (ServerStarted)
            return true;
        if (!PrepareNative(appId))
            return false;

        try
        {
            // eServerModeAuthentication (not NoAuthentication): the logon below must
            // actually connect this process to Steam — that connection is what mints the
            // anonymous identity and authenticates us to the relay network. The "no auth"
            // mode skips the Steam connection entirely and BLoggedOn() never goes true
            // (verified empirically on this machine, 2026-07-09).
            if (!GameServer.Init(0, gamePort, queryPort, EServerMode.eServerModeAuthentication, $"{Protocol.Version}"))
            {
                LastError = "Steam game-server init failed (ports in use, or native library missing).";
                return false;
            }
        }
        catch (Exception ex)
        {
            LastError = $"Steam game-server init failed: {ex.GetType().Name}";
            return false;
        }

        SteamGameServer.SetProduct(appId.ToString());
        SteamGameServer.SetGameDescription("mp-foundation match server");

        // Failure surfaces only through this callback — poll-only code would report every
        // failure as an indistinguishable timeout.
        string failReason = "";
        var failCallback = Callback<SteamServerConnectFailure_t>.CreateGameServer(
            f => failReason = f.m_eResult.ToString());
        try
        {
            SteamGameServer.LogOnAnonymous();
            SteamGameServerNetworkingUtils.InitRelayNetworkAccess();

            // Anonymous logon is a network round-trip to Steam; poll until it lands.
            ulong deadline = Time.GetTicksMsec() + (ulong)(logonTimeoutSec * 1000);
            while (Time.GetTicksMsec() < deadline)
            {
                GameServer.RunCallbacks();
                if (SteamGameServer.BLoggedOn())
                {
                    ServerSteamId = SteamGameServer.GetSteamID().m_SteamID;
                    ServerStarted = true;
                    GD.Print($"[steam] anonymous game-server logon ok (app {appId}, id {ServerSteamId})");
                    return true;
                }
                if (failReason.Length > 0)
                {
                    LastError = $"Steam anonymous game-server logon failed: {failReason}";
                    GameServer.Shutdown();
                    return false;
                }
                System.Threading.Thread.Sleep(50);
            }

            LastError = "Steam anonymous game-server logon timed out — is this machine online and Steam reachable?";
            GameServer.Shutdown();
            return false;
        }
        finally
        {
            failCallback.Dispose();
        }
    }

    public static void StopGameServer()
    {
        if (!ServerStarted)
            return;
        try
        {
            SteamGameServer.LogOff();
            // Let the log-off actually reach Steam before we tear the API down: an immediate
            // Shutdown can drop the in-flight logoff, which is exactly what leaves the
            // anonymous game-server session registered at Steam's backend (~1-2 min) and
            // makes an immediate re-host fail GameServer.Init with CantCreate. Pump callbacks
            // until the session reports logged-off, capped so a hung logoff can't stall exit.
            ulong deadline = Time.GetTicksMsec() + 500;
            while (Time.GetTicksMsec() < deadline && SteamGameServer.BLoggedOn())
            {
                GameServer.RunCallbacks();
                System.Threading.Thread.Sleep(20);
            }
            GameServer.Shutdown();
        }
        catch
        {
            // Shutting down a dying process; nothing useful to do with a failure here.
        }
        ServerStarted = false;
        ServerSteamId = 0;
    }

    /// <summary>Per-frame callback pump. Call from an autoload _Process; dispatches both
    /// session flavors (each is a no-op when that session isn't up).</summary>
    public static void Pump()
    {
        if (ClientStarted)
        {
            try { SteamAPI.RunCallbacks(); }
            catch (Exception ex) { GD.PrintErr($"[steam] client callback pump: {ex.GetType().Name}"); }
        }
        if (ServerStarted)
        {
            try { GameServer.RunCallbacks(); }
            catch (Exception ex) { GD.PrintErr($"[steam] server callback pump: {ex.GetType().Name}"); }
        }
    }

    public static void Shutdown()
    {
        StopGameServer();
        if (!ClientStarted)
            return;
        try { SteamAPI.Shutdown(); } catch { /* process exit path */ }
        ClientStarted = false;
    }

    // --- Native library + App ID plumbing -------------------------------------------
    private static bool PrepareNative(uint appId)
    {
        ApplyAppIdEnv(appId);

        if (_resolverInstalled)
            return true;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, ResolveNative);
            _resolverInstalled = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Could not install steam_api resolver: {ex.GetType().Name}";
            return false;
        }
    }

    /// <summary>
    /// Publishes the resolved App ID into Valve's env vars — the API discovers it there
    /// (equivalent to steam_appid.txt, but no file management), and it must be set before
    /// the first Init of either flavor.
    /// <para>This used to be an unconditional overwrite of both names, which is the half of
    /// the App ID defect that survives fixing <see cref="ResolveAppId"/>: it meant a build
    /// LAUNCHED BY STEAM had its real App ID replaced by our fallback before
    /// <c>SteamAPI.Init()</c> ever saw it. <see cref="ShouldWriteAppIdEnv"/> is the gate.</para>
    /// </summary>
    private static void ApplyAppIdEnv(uint appId)
    {
        foreach (string name in ValveAppIdEnvNames)
        {
            if (!ShouldWriteAppIdEnv(appId, System.Environment.GetEnvironmentVariable(name)))
                continue;
            System.Environment.SetEnvironmentVariable(
                name, appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Whether the resolved App ID should be written over one of Valve's env vars. Pure and
    /// internal so <see cref="SteamSelfTest"/> can assert the whole table without a Steam
    /// client. The rule, in one line: <b>never let the Spacewar fallback clobber a real App
    /// ID that something else already put there.</b>
    /// <list type="bullet">
    /// <item>Nothing usable in the variable (unset, empty, "0", garbage) → write. This is the
    /// path every current dev run and every CI run takes, so today's behaviour is unchanged:
    /// 480 in, 480 written.</item>
    /// <item>Already exactly the resolved value → skip. Writing would be a no-op.</item>
    /// <item>A different real value already there, and we resolved something OTHER than the
    /// fallback → write. That is an explicit override (a <c>--steam-app-id</c> flag, or
    /// <c>STEAM_APP_ID</c>/steam_appid.txt) and a deliberate override is allowed to win.</item>
    /// <item>A different real value already there, and all we have is the fallback → SKIP.
    /// This is the ship-day case: Steam launched us under the real ID and our chain has
    /// nothing better to say. Leave Steam's value alone.</item>
    /// </list>
    /// </summary>
    internal static bool ShouldWriteAppIdEnv(uint resolved, string? existing)
    {
        if (resolved == 0)
            return false; // a zero App ID cannot init — never publish one
        uint current = 0;
        bool usable = existing is not null
            && uint.TryParse(existing.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out current)
            && current != 0;
        if (!usable)
            return true;
        if (current == resolved)
            return false;
        return resolved != NetProfile.FallbackSteamAppId;
    }

    /// <summary>
    /// Finds the native steam_api next to wherever our managed code actually lives — the
    /// editor's .godot/mono output during dev (the csproj copies it there) or the export's
    /// data dir. Handles every name Steamworks.NET might P/Invoke per platform, so the
    /// same code path serves Windows dev, Windows export, and the Mac client.
    /// </summary>
    private static IntPtr ResolveNative(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Contains("steam_api", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        string fileName, ridDir;
        if (OperatingSystem.IsWindows())
            (fileName, ridDir) = ("steam_api64.dll", "win-x64");
        else if (OperatingSystem.IsMacOS())
            (fileName, ridDir) = ("libsteam_api.dylib", "osx");
        else
            (fileName, ridDir) = ("libsteam_api.so", "linux-x64");

        // Probe order matters for how this runs in practice:
        //  1. the committed redistributable inside the project (editor / headless dev —
        //     res:// is the repo; in an exported build this globalizes into the pack and
        //     File.Exists is simply false),
        //  2. beside the running executable (exported builds ship the file there),
        //  3. beside the managed assembly (empty under Godot's collectible ALC —
        //     Assembly.Location is "" there — but valid under plain .NET hosting),
        //  4. the .NET runtime payload directory. W7-7, 2026-08-30: added because on macOS
        //     probes 1-3 CANNOT hit, measured on the real exported bundle. `dotnet publish`
        //     puts libsteam_api.dylib in
        //     `mp-foundation.app/Contents/Resources/data_MpFoundation_macos_{arm64,x86_64}/`,
        //     while probe 1 is inside the pack, probe 2 is `Contents/MacOS` (verified: the only
        //     thing there is the launcher binary), and probe 3 is "" under Godot's ALC. Without
        //     this the resolver returns Zero, prints a "could not resolve" error, and the mac
        //     client's Steam works only by falling through to .NET's default resolver — which
        //     probably does find it, but "probably" is not what a cross-platform playtest should
        //     rest on. AppContext.BaseDirectory is that payload directory. It cannot regress
        //     Windows or Linux: probe 2 already wins there (steam_api64.dll ships beside the exe,
        //     verified by Export-WindowsClient.ps1's own guard), so this one is never reached.
        //     STILL UNVERIFIED ON MAC HARDWARE, like everything else on this path.
        string[] probeDirs =
        {
            ProjectSettings.GlobalizePath($"res://thirdparty/steamworks/{ridDir}"),
            Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
            Path.GetDirectoryName(typeof(SteamService).Assembly.Location) ?? "",
            AppContext.BaseDirectory ?? "",
        };
        foreach (string dir in probeDirs)
        {
            if (dir.Length == 0)
                continue;
            string candidate = Path.Combine(dir, fileName);
            if (!File.Exists(candidate))
                continue;
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
                return handle;
            GD.PrintErr($"[steam] native library present but failed to load: {candidate}");
        }
        GD.PrintErr($"[steam] could not resolve '{libraryName}' -> {fileName}; probed: {string.Join(" | ", probeDirs)}");
        return IntPtr.Zero; // fall through to default resolution (PATH etc.)
    }
}
