using System.Collections.Generic;
using System.Globalization;
using Godot;
using MpFoundation.Net;
using MpFoundation.Net.Steam;

namespace MpFoundation;

/// <summary>
/// Autoload singleton owning the transport peer (ENet or Steam relay P2P), the version
/// handshake, connection-level security (rate limiting, match-full and malformed-auth
/// rejection), and the Practice Mode child-server lifecycle. Scenes record their intent
/// here; Gameplay executes it.
///
/// Transport selection: servers listen on the transport named by --transport (ENet
/// default; "steam" = anonymous game-server + relay). Clients never choose — they follow
/// the address a room code resolves to via the Steam Lobby directory ("host:port" → ENet,
/// "steam:&lt;id64&gt;" → Steam; see SteamLobby). Practice Mode stays ENet-on-localhost by
/// construction: there is no NAT to traverse on one machine, and CI must run without a
/// Steam client.
///
/// The version handshake rides on Godot's documented SceneMultiplayer auth channel:
/// a peer is not considered connected (and no replication happens) until both sides
/// exchange and accept a protocol identifier. This is the idiomatic, reliable place to
/// gate incompatible builds and reject untrusted peers before they can affect the match.
/// The handshake is transport-agnostic — a Steam peer walks the exact same rate-limit and
/// validation path, keyed by its SteamID instead of an IP (see RemoteAddressFor).
/// </summary>
public partial class NetworkManager : Node
{
    public enum SessionRole { None, Server, Client }

    // Transport-level connection cap (both transports). Sits above Protocol.MaxPlayers so
    // the server can still accept an over-cap connection far enough to reject it with a
    // clean "match full" message.
    public const int MaxClients = Net.NetProfile.MaxClients;
    private const double AuthTimeoutSec = 5.0;

    // Bounds concurrent unauthenticated (mid-handshake, not-yet-accepted) connections per
    // remote IP. Without this, one attacker can open MaxClients connections and never
    // complete the handshake, occupying every ENet transport slot and locking out
    // legitimate players. Loopback is exempt (see IsLoopback) because the local test
    // harness and Practice Mode legitimately run many bots from 127.0.0.1/::1 at once.
    private const int MaxPendingPerIp = 4;

    // Global cap on concurrent unauthenticated NON-LOOPBACK connections. The per-IP cap alone
    // still lets a handful of distinct source IPs (4 × 4 = 16) hold every transport slot in a
    // perpetual never-complete-auth cycle; bounding the total keeps at least MaxPlayers slots
    // reachable by peers that actually finish the handshake.
    private const int MaxPendingTotal = MaxClients - Protocol.MaxPlayers;

    // A Steam game server binds its (unused-by-us) game port at the match port and its
    // query port here, so multiple provisioned servers on one machine never collide.
    private const int SteamQueryPortOffset = 2000;

    public static NetworkManager Instance { get; private set; } = null!;

    public LaunchOptions Options { get; private set; } = LaunchOptions.Parse(System.Array.Empty<string>());
    public SessionRole Role { get; set; } = SessionRole.None;
    public string PendingHost { get; set; } = "";
    public int PendingPort { get; set; } = LaunchOptions.DefaultPort;
    // No default: a human must type a name (Host/Join menus reject an empty field). CLI
    // paths (bots, --practice) set this from --name; the sole remaining fallback is "".
    public string LocalDisplayName { get; set; } = "";

    /// <summary>Client-authority avatar roster pick (docs/creatures/avatar-roster-description.md),
    /// mirroring <see cref="LocalDisplayName"/>: the Host/Join menus' picker sets this
    /// alongside the display name. "" means "no picker choice made" — SandboxAvatar falls
    /// back to <c>SAIL_AVATAR</c> (see AvatarVisual.ResolveEnvAvatarKey), so headless bots
    /// and CI, which never touch a picker, keep behaving exactly as before this feature
    /// existed.</summary>
    public string LocalAvatarKey { get; set; } = "";
    public string LastError { get; set; } = "";

    /// <summary>When nonzero, the client connects via Steam relay to this server identity
    /// instead of PendingHost:PendingPort — set by the Join screen when the direct-address
    /// field holds a steam: address. Room-code joins never set this; they branch on the
    /// prefix of whatever address the code resolves to.</summary>
    public ulong PendingSteamId { get; set; }

    // When set, the client resolves this room code via the Steam Lobby directory before
    // connecting; otherwise it connects directly to PendingHost:PendingPort (or PendingSteamId).
    public string PendingRoom { get; set; } = "";

    /// <summary>Room code of the match this client is in (created or joined by code); "" otherwise.</summary>
    public string CurrentRoomCode { get; set; } = "";

    /// <summary>
    /// True while this client's session was started from the HOST screen — i.e. this process
    /// minted the room code, owns <see cref="HostedServer"/>, and owns the Steam lobby the
    /// code resolves through. A joiner is never in this state, and neither is a bot or the
    /// headless self-test harness (they never touch HostMenu).
    ///
    /// <para>It exists for exactly one reason: a terminal connect failure has to send a HOST
    /// back to the Host screen and a JOINER back to the Join screen. Before this flag both
    /// went to Join, so a host whose own connect failed was dumped on a screen they never
    /// chose, with the lobby they had just read aloud already destroyed and no way back to
    /// re-host but to rediscover the Host screen themselves (F1 of the 2026-08-30 master
    /// review). See <see cref="Net.SessionFailureRoute"/>.</para>
    ///
    /// <para><b>It is session state, so <see cref="ResetToOffline"/> clears it</b> along with
    /// the peer, the role and the room code — this process stops being a host at the same
    /// instant it stops owning the server child. Any failure path that wants to know how the
    /// session started must therefore READ IT FIRST and tear down second.</para>
    /// </summary>
    public bool IsHostFlow { get; set; }

    /// <summary>Set by the client-side handshake when a connection is rejected for a specific reason.</summary>
    public string HandshakeError { get; set; } = "";

    /// <summary>Server: the room code joiners must present in their handshake, or "" to require
    /// none. Set from --room-code at StartServer. Empty for LAN / direct / CI / Practice, so
    /// those paths never gain a check (see Handshake and ServerValidateClient).</summary>
    public string ExpectedRoomCode { get; private set; } = "";

    /// <summary>Client: the room code to present when joining (the code this client is joining
    /// by), or "" for a direct/LAN join. Set by the join flow before connecting.</summary>
    public string JoiningRoomCode { get; set; } = "";

    public bool IsBot => Options.Bot;
    public bool IsHeadless => DisplayServer.GetName() == "headless";
    public int AcceptedCount => _acceptedPeers.Count;

    /// <summary>True once a peer has passed the version handshake. The voice relay uses
    /// this as its gate — voice reuses the existing auth state machine, never a second one.</summary>
    public bool IsPeerAccepted(int id) => _acceptedPeers.Contains(id);

    /// <summary>True when running the headless Practice-Mode self-test (behaves like a bot client).</summary>
    public bool PracticeSelfTest { get; set; }

    private readonly HashSet<int> _acceptedPeers = new();
    private readonly HashSet<int> _fullNotified = new();
    private readonly Dictionary<string, int> _pendingCountByIp = new();
    private readonly Dictionary<int, string> _pendingPeerIp = new();
    private ConnectionRateLimiter _rateLimiter = new();
    private System.Diagnostics.Process? _practiceProcess;
    private Net.Hosting.WindowsJobObject? _practiceJob;
    private bool _authConfigured;

    public override void _EnterTree()
    {
        Instance = this;
        Options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        // --log-sfx, latched once here rather than read per sound: ActorFx.FireCore runs per
        // footstep for every avatar in earshot, and its doc promises that path allocates
        // nothing. See ActorFx.LogSfx.
        Game.Presentation.ActorFx.LogSfx = Options.LogSfx;
        ApplyComfortOptions(Options);
    }

    /// <summary>
    /// <b>SICK-1's comfort flags, latched once</b> — the same "read the option here, not at the
    /// point of use" idiom as <c>ActorFx.LogSfx</c> above and for the same reason: the lens is
    /// built inside <c>SandboxAvatar.AttachFirstPersonCamera</c>, where no launch option is in
    /// scope, and the frame settings are process-wide facts that must be true before the first
    /// frame is drawn rather than whenever a camera happens to attach.
    ///
    /// <para><b>Inert headless.</b> Every suite bot in the repo but one runs
    /// <c>--headless</c>, where there is no swap chain to set a present mode on; the vsync and
    /// fps calls are skipped rather than allowed to no-op, so a headless log never carries a
    /// line claiming a display setting was applied to a process with no display.</para>
    /// </summary>
    private static void ApplyComfortOptions(LaunchOptions options)
    {
        Game.Sandbox.FirstPersonCamera.InterpolateToRenderFrame = options.CameraInterpolation;
        Game.Sandbox.FirstPersonCamera.FovDeg = options.FovDeg;
        Engine.MaxFps = options.MaxFps;
        if (DisplayServer.GetName() == "headless")
            return;
        DisplayServer.WindowSetVsyncMode(options.Vsync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
    }

    public override void _Ready() => ConfigureAuth();

    // --- Session lifecycle -------------------------------------------------------
    public Error StartServer(int port)
    {
        _acceptedPeers.Clear();
        _fullNotified.Clear();
        _pendingCountByIp.Clear();
        _pendingPeerIp.Clear();
        ExpectedRoomCode = Options.RoomCode; // "" unless started with --room-code
        _rateLimiter = new ConnectionRateLimiter(Options.ConnLimit);

        if (Options.Transport == LaunchOptions.TransportSteam)
            return StartServerSteam(port);

        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(port, MaxClients);
        if (err != Error.Ok)
        {
            LastError = $"Failed to start server on port {port}: {err}";
            return err;
        }
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    /// <summary>Steam-relay server: anonymous game-server logon (blocking, startup-only),
    /// then a P2P listen socket under that identity. The port keeps its provisioner-assigned
    /// uniqueness role — Steam binds it as the (unused) game port — but no player traffic
    /// ever arrives on it; everything rides the relay.</summary>
    private Error StartServerSteam(int port)
    {
        uint appId = SteamService.ResolveAppId(Options);
        if (!SteamService.StartGameServer(appId, (ushort)port, (ushort)(port + SteamQueryPortOffset)))
        {
            LastError = SteamService.LastError;
            return Error.CantCreate;
        }
        SteamPeer? peer = SteamPeer.CreateServer(MaxClients, out string error);
        if (peer is null)
        {
            LastError = error;
            return Error.CantCreate;
        }
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    public Error StartClient(string host, int port)
    {
        HandshakeError = "";
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(host, port);
        if (err != Error.Ok)
        {
            LastError = $"Failed to open connection to {host}:{port}: {err}";
            return err;
        }
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    /// <summary>Steam-relay client: needs the player's own Steam session (running, logged
    /// in), then connects P2P to the server's identity. Same contract as StartClient —
    /// synchronous failure here, async progress surfaced through the usual signals.</summary>
    public Error StartClientSteam(ulong serverSteamId)
    {
        HandshakeError = "";
        uint appId = SteamService.ResolveAppId(Options);
        if (!SteamService.EnsureClient(appId))
        {
            LastError = SteamService.LastError;
            return Error.CantConnect;
        }
        SteamPeer? peer = SteamPeer.CreateClient(serverSteamId, out string error);
        if (peer is null)
        {
            LastError = error;
            return Error.CantConnect;
        }
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    public void ResetToOffline()
    {
        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        Role = SessionRole.None;
        CurrentRoomCode = "";
        // This process stops being a host at the same instant it stops owning the child
        // below. A failure path that needs to know how the session started reads IsHostFlow
        // BEFORE calling this (see SessionFailureRoute / Gameplay.Fail).
        IsHostFlow = false;
        _acceptedPeers.Clear();
        _pendingCountByIp.Clear();
        _pendingPeerIp.Clear();
        // If this client was hosting, the match is over: reap the server child and take
        // the room code out of circulation (leaving the lobby destroys it — sole member).
        StopHostedServer();
        Net.Steam.SteamLobby.LeaveCurrent();
    }

    // --- Version handshake / auth ------------------------------------------------
    private void ConfigureAuth()
    {
        if (_authConfigured || Multiplayer is not SceneMultiplayer scene)
            return;
        _authConfigured = true;
        scene.AuthCallback = new Callable(this, MethodName.OnAuthReceived);
        scene.AuthTimeout = AuthTimeoutSec;
        scene.PeerAuthenticating += OnPeerAuthenticating;
        scene.PeerAuthenticationFailed += OnPeerAuthFailed;
        Multiplayer.PeerDisconnected += id =>
        {
            _acceptedPeers.Remove((int)id);
            ReleasePendingSlot((int)id);
        };
    }

    // Both ends send their hello as soon as a peer begins authenticating; the receiver
    // validates in OnAuthReceived. Sending early (before either side has judged the other)
    // guarantees the client learns the server's protocol/status even if the server then
    // rejects it, so the player gets a specific reason rather than a generic failure.
    private void OnPeerAuthenticating(long id)
    {
        if (Multiplayer is not SceneMultiplayer scene)
            return;

        if (Role == SessionRole.Server)
        {
            // Raw here on purpose: the rate limiter and the pending-slot caps key on the real
            // address and are worthless without it. It stays in memory and dies with the process
            // — only LogIdentity.Of(ip) is ever written down. See LogIdentity.
            string ip = RemoteAddressFor((int)id);
            double now = Time.GetTicksMsec() / 1000.0;
            if (!_rateLimiter.Allow(ip, now))
            {
                ServerLog.Warn("rate limit: connection rejected", $"from={LogIdentity.Of(ip)} peer={id}");
                Multiplayer.MultiplayerPeer.DisconnectPeer((int)id, force: true);
                return;
            }

            if (!IsLoopback(ip) && _pendingCountByIp.TryGetValue(ip, out int pending) && pending >= MaxPendingPerIp)
            {
                ServerLog.Warn("connection slot exhaustion: too many unauthenticated peers", $"from={LogIdentity.Of(ip)} peer={id} pending={pending} cap={MaxPendingPerIp}");
                Multiplayer.MultiplayerPeer.DisconnectPeer((int)id, force: true);
                return;
            }

            // _pendingPeerIp holds only non-loopback entries (TrackPendingSlot skips loopback),
            // so this cap can never throttle the local test harness or Practice Mode bots.
            if (!IsLoopback(ip) && _pendingPeerIp.Count >= MaxPendingTotal)
            {
                ServerLog.Warn("connection slot exhaustion: global unauthenticated cap", $"from={LogIdentity.Of(ip)} peer={id} pending={_pendingPeerIp.Count} cap={MaxPendingTotal}");
                Multiplayer.MultiplayerPeer.DisconnectPeer((int)id, force: true);
                return;
            }

            TrackPendingSlot((int)id, ip);

            var status = _acceptedPeers.Count >= Protocol.MaxPlayers
                ? Handshake.ServerStatus.Full
                : Handshake.ServerStatus.Ok;
            scene.SendAuth((int)id, Handshake.ServerHello(Protocol.Version, status));
        }
        else // client
        {
            byte[] payload = Options.BadAuth
                ? BadAuthPayload()
                : Handshake.ClientHello(Options.EffectiveProtocol, JoiningRoomCode);
            scene.SendAuth((int)id, payload);
        }
    }

    private void OnAuthReceived(long id, byte[] data)
    {
        if (Multiplayer is not SceneMultiplayer scene)
            return;

        if (Role == SessionRole.Server)
            ServerValidateClient(scene, (int)id, data);
        else
            ClientValidateServer(scene, (int)id, data);
    }

    private void ServerValidateClient(SceneMultiplayer scene, int id, byte[] data)
    {
        string ip = RemoteAddressFor(id);

        if (!Handshake.TryParseClient(data, out Handshake.ClientInfo info))
        {
            ServerLog.Warn("malformed auth rejected", $"from={LogIdentity.Of(ip)} peer={id} bytes={data?.Length ?? 0}");
            Multiplayer.MultiplayerPeer.DisconnectPeer(id, force: true);
            return;
        }
        if (_acceptedPeers.Count >= Protocol.MaxPlayers)
        {
            // One verdict per peer: without this, every extra auth payload a hostile client
            // sends during its timeout window costs the server a reply + a fresh timer —
            // a free amplification lever on the pre-auth surface.
            if (!_fullNotified.Add(id))
                return;
            ServerLog.Warn("match full: connection rejected", $"from={LogIdentity.Of(ip)} peer={id} cap={Protocol.MaxPlayers}");
            // The hello this peer got at connect time may have said Ok (slots were still
            // mid-handshake during a burst join). Re-send the verdict so the player sees
            // "room is full" instead of a generic auth failure, then give the packet a
            // beat to flush before cutting the connection.
            scene.SendAuth(id, Handshake.ServerHello(Protocol.Version, Handshake.ServerStatus.Full));
            var timer = GetTree().CreateTimer(0.5);
            timer.Timeout += () =>
            {
                try { Multiplayer.MultiplayerPeer?.DisconnectPeer(id); }
                catch { /* peer already gone — nothing to cut */ }
            };
            return;
        }
        if (info.Protocol != Protocol.Version)
        {
            ServerLog.Warn("version mismatch rejected", $"from={LogIdentity.Of(ip)} peer={id} clientProtocol={info.Protocol} serverProtocol={Protocol.Version}");
            Multiplayer.MultiplayerPeer.DisconnectPeer(id);
            return;
        }
        // Room-code capability: a server published under a code admits only joiners that present
        // it. Inert unless ExpectedRoomCode is set (LAN/direct/CI/Practice never set it), so those
        // paths reach here exactly as before. This is what makes a scraped host id unjoinable.
        if (ExpectedRoomCode.Length > 0 && info.RoomCode != ExpectedRoomCode)
        {
            ServerLog.Warn("room code mismatch rejected", $"from={LogIdentity.Of(ip)} peer={id} presented={(info.RoomCode.Length == 0 ? "<none>" : "<wrong>")}");
            Multiplayer.MultiplayerPeer.DisconnectPeer(id);
            return;
        }

        _acceptedPeers.Add(id);
        ReleasePendingSlot(id);
        scene.CompleteAuth(id);
        ServerLog.Info("peer authenticated", $"from={LogIdentity.Of(ip)} peer={id} accepted={_acceptedPeers.Count}");
    }

    private void ClientValidateServer(SceneMultiplayer scene, int id, byte[] data)
    {
        if (!Handshake.TryParseServer(data, out Handshake.ServerInfo info))
        {
            HandshakeError = "Invalid server handshake.";
            return; // server will drop us; Gameplay surfaces HandshakeError
        }
        if (info.Protocol != Options.EffectiveProtocol)
        {
            HandshakeError = $"Version mismatch: client v{Options.EffectiveProtocol}, server v{info.Protocol} — rebuild/update and try again.";
            return;
        }
        if (info.Status == Handshake.ServerStatus.Full)
        {
            HandshakeError = "Room is full.";
            return;
        }
        scene.CompleteAuth(id);
    }

    private void OnPeerAuthFailed(long id)
    {
        if (Role == SessionRole.Server)
        {
            ServerLog.Warn("auth failed/timed out", $"peer={id}");
            _acceptedPeers.Remove((int)id);
            ReleasePendingSlot((int)id);
        }
        else if (HandshakeError.Length == 0)
        {
            HandshakeError = "Authentication failed.";
        }
    }

    private static byte[] BadAuthPayload()
    {
        // Oversized, non-ASCII garbage: exercises the server's malformed-input rejection.
        var bytes = new byte[Handshake.MaxPayloadBytes * 4];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = 0xFF;
        return bytes;
    }

    // --- Per-IP unauthenticated-connection tracking ------------------------------
    // Loopback is same-machine/trusted (test bots, Practice Mode); real players are
    // never on loopback, so it is exempt from the per-IP pending cap. Over the Steam
    // transport identities are steam:<steamid64> strings, never loopback.
    private static bool IsLoopback(string ip) => ip is "127.0.0.1" or "::1" or "localhost";

    private void TrackPendingSlot(int id, string ip)
    {
        if (IsLoopback(ip))
            return;
        _pendingPeerIp[id] = ip;
        _pendingCountByIp.TryGetValue(ip, out int count);
        _pendingCountByIp[ip] = count + 1;
    }

    private void ReleasePendingSlot(int id)
    {
        _fullNotified.Remove(id); // peer id may be recycled; never inherit a stale verdict
        if (!_pendingPeerIp.Remove(id, out string? ip))
            return;
        if (!_pendingCountByIp.TryGetValue(ip, out int count))
            return;
        if (count <= 1)
            _pendingCountByIp.Remove(ip);
        else
            _pendingCountByIp[ip] = count - 1;
    }

    /// <summary>The remote peer's raw SteamID64 when the current transport is Steam and the
    /// peer's identity has been resolved (see SteamPeer.SteamId64Of); 0 otherwise (ENet
    /// transport, or called before the peer's connection is tracked). Feeds Gameplay's
    /// reconnect-grace-window bookkeeping (SteamID64 is the stable identity that survives a
    /// reconnect; the transport-level peer id is freshly generated every connection) — see
    /// docs/superpowers/specs/2026-07-12-client-reconnection-design.md.</summary>
    public ulong SteamId64Of(int peerId) =>
        Multiplayer.MultiplayerPeer is Net.Steam.SteamPeer steam ? steam.SteamId64Of(peerId) : 0;

    /// <summary>The per-connection identity string the rate limiter and security logging
    /// key on: the remote IP over ENet, "steam:&lt;steamid64&gt;" over Steam. A SteamID is the
    /// stronger identity of the two (Steam accounts are rate-limited by economics; IPs by
    /// NAT) — the abuse bookkeeping must never get weaker when the transport upgrades.</summary>
    private string RemoteAddressFor(int id)
    {
        try
        {
            if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
            {
                ENetPacketPeer peer = enet.GetPeer(id);
                return peer?.GetRemoteAddress() ?? "unknown";
            }
            if (Multiplayer.MultiplayerPeer is SteamPeer steam)
                return steam.IdentityOf(id);
        }
        catch
        {
            // Address lookup is best-effort; fall through to "unknown".
        }
        return "unknown";
    }

    // --- Hosted-match child server (the Host flow) --------------------------------
    /// <summary>The dedicated-server child this client spawned by hosting a match; null
    /// when not hosting. Owned here (not by the Host screen) because it must outlive the
    /// menu scene and die with the session (ResetToOffline) or the process (job object).</summary>
    public Net.Hosting.LocalServerHost? HostedServer { get; private set; }

    /// <summary>Transfers ownership of a ready server child from the Host screen.</summary>
    public void AdoptHostedServer(Net.Hosting.LocalServerHost server)
    {
        StopHostedServer();
        HostedServer = server;
        // Crash visibility (voluntary stops suppress this — see LocalServerHost.Stop).
        // The player-facing UX rides the normal "Disconnected from server" path; this
        // line is for logs and the hosting test harness.
        server.Exited += code => GD.Print($"[host] server child exited (code {code})");
    }

    public void StopHostedServer()
    {
        HostedServer?.Dispose();
        HostedServer = null;
    }

    // --- Practice Mode child server ---------------------------------------------
    /// <summary>Launches a headless dedicated server as a child process and returns its port.
    /// Spawned with the same orphan protection as the hosted-match child (job object with
    /// kill-on-close + --parent-managed stdin watcher): a hard-killed client must never
    /// strand a headless practice server burning a port and CPU at 60 Hz.</summary>
    public int StartPracticeServer()
    {
        StopPracticeServer();
        int port = FindFreeUdpPort();
        string exe = OS.GetExecutablePath();
        string logDir = ProjectSettings.GlobalizePath("user://logs");
        // The child server must simulate the SAME world the client renders — otherwise the
        // client draws one world (e.g. the hub) while an escape-default server is
        // authoritative, and every world-specific interactive (hub climb-crates, portal
        // doors) fails because it doesn't exist server-side. Forward the launch world.
        //
        // Also forward the cycle test hooks (feat/playground-daynight): CycleDriver.Setup
        // only does anything meaningful on the SERVER (a client's own CyclePeriodSec/
        // CycleStartPhase are never read — see CycleDriver.Setup's early return), so
        // without this the child server the human/bot actually gets its phase from always
        // runs the 120s-from-phase-0 default regardless of what was passed on Practice's own
        // command line. Safe to forward unconditionally on every real launch: both default
        // to the exact "use the built-in default" sentinel (0 / 0f — see their LaunchOptions
        // doc comments), so an ordinary Practice launch that never touched these flags
        // forwards defaults-that-mean-defaults and nothing changes.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            // Our end of the child's stdin is the shutdown control channel: closing it (or the
            // client dying, which closes it for us) is the EOF ParentShutdownWatcher waits for.
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--headless");
        // Dev runs need the project path; an exported build IS the project (feature "template").
        if (!OS.HasFeature("template"))
        {
            psi.ArgumentList.Add("--path");
            psi.ArgumentList.Add(ProjectSettings.GlobalizePath("res://"));
        }
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--server");
        psi.ArgumentList.Add("--parent-managed");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--world");
        psi.ArgumentList.Add(Options.World);
        psi.ArgumentList.Add("--log-dir");
        psi.ArgumentList.Add(logDir);
        psi.ArgumentList.Add("--cycle-period");
        psi.ArgumentList.Add(Options.CyclePeriodSec.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--cycle-start-phase");
        psi.ArgumentList.Add(Options.CycleStartPhase.ToString(CultureInfo.InvariantCulture));
        // STYLE-4: the freeze is a server-side decision with the identical trap, and it is
        // forwarded from BOTH child-spawn paths on purpose — a hook forwarded by
        // only one of them is the silently-accepted-and-ignored bug one door down, which is the
        // reasoning AppendFlowServerFlagsTo's own doc comment already records. Only-when-set, so an
        // ordinary Practice launch builds a child command line identical to before.
        if (Options.CycleFreeze)
            psi.ArgumentList.Add("--cycle-freeze");
        // CORE-INT-1: the flow/quota hooks are SERVER-side flags with the exact trap the
        // (The flow/quota server hooks that were forwarded here went with the quota spine at
        // the fork - BASE-1, 2026-09-19. THE FORWARDING ITSELF IS THE THING TO REMEMBER: this
        // server is a CHILD PROCESS, so any server-side test hook ROUND-1 adds must be appended
        // to this command line or it reaches only the client half of the launch, is silently
        // accepted, and does nothing. Forward only when explicitly set, so an ordinary Practice
        // launch keeps building the same child command line it always did.)

        var process = new System.Diagnostics.Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                GD.PushWarning("[practice] failed to start the dedicated server process");
                return 0;
            }
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[practice] server spawn threw {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
        _practiceProcess = process;
        _practiceJob = new Net.Hosting.WindowsJobObject();
        _practiceJob.Assign(process);
        GD.Print($"[practice] spawned dedicated server pid {process.Id} on udp/{port}");
        return port;
    }

    public void StopPracticeServer()
    {
        System.Diagnostics.Process? proc = _practiceProcess;
        _practiceProcess = null;
        if (proc is not null)
        {
            int pid = 0;
            try
            {
                pid = proc.Id;
                if (!proc.HasExited)
                {
                    // Graceful first (EOF → ParentShutdownWatcher quits cleanly), force as backstop.
                    try { proc.StandardInput.Close(); } catch { /* child already gone */ }
                    if (!proc.WaitForExit(3000))
                        proc.Kill(entireProcessTree: true);
                }
            }
            catch { /* already gone — the outcome we wanted */ }
            proc.Dispose();
            GD.Print($"[practice] terminated dedicated server pid {pid}");
        }
        _practiceJob?.Dispose();
        _practiceJob = null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            StopPracticeServer();
            StopHostedServer();
        }
    }

    // Steam callback dispatch (connection state changes, logon results) rides the frame
    // loop; both pumps are no-ops when that Steam session isn't up, so ENet-only runs
    // (Practice, CI) pay nothing here.
    public override void _Process(double delta) => SteamService.Pump();

    public override void _ExitTree()
    {
        // Order matters: a live SteamPeer must be closed BEFORE SteamService.Shutdown —
        // tearing down the Steam API under an open relay connection is an access
        // violation at process exit (observed live, 0xC0000005 after a clean quit).
        try { Multiplayer.MultiplayerPeer?.Close(); } catch { /* already closed */ }
        StopPracticeServer();
        StopHostedServer();
        Net.Steam.SteamLobby.LeaveCurrent();
        SteamService.Shutdown();
    }

    private static int FindFreeUdpPort()
    {
        using var udp = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
