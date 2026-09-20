using System.Collections.Generic;
using Godot;
using MpFoundation.Net;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Voice;

/// <summary>
/// Autoload owning proximity voice chat. Clients capture/encode (VoiceCapture) and send
/// compressed frames to the server, which relays them — tagged with the sender's peer id
/// and WITHOUT decoding or inspecting audio — to every other client in the match over a
/// dedicated unreliable channel. Each receiving client decodes per speaker (VoiceSpeaker)
/// and plays back through an AudioStreamPlayer3D on that speaker's replicated avatar, so
/// proximity falloff is Godot's built-in 3D attenuation, not hand-rolled distance math.
///
/// Security mirrors the Phase 2 discipline on this new surface: the server bounds packet
/// size, rate-limits per client, and logs rejections; and because the server relays
/// blindly, the client treats received payloads as equally untrusted (decode failures
/// are dropped, never fatal).
/// </summary>
public partial class VoiceManager : Node
{
    private const double RejectLogCooldownSec = 1.0;

    public static VoiceManager Instance { get; private set; } = null!;

    // A voice source sits at head height on the speaker's own avatar for ordinary proximity; a
    // bridged source emits straight from the near-end prop node, so no extra lift is applied.
    //
    // "Head height" is now the speaker's OWN head (SandboxAvatar.VoiceOriginGlobalPosition,
    // which is that character's measured eyeline), not this constant. The constant was 1.60 m —
    // adult human head height — and every character in the game is a metre-tall kid or a
    // knee-high creature, so every voice in every session was emitting from roughly 0.7 m of
    // empty air above the speaker's actual head. It survives only as the fallback for a
    // non-avatar Node3D, and is dropped to a value that at least sits inside a character.
    private static readonly Vector3 AvatarHeadOffset =
        new(0, AvatarProportions.Fallback.EyeHeightM, 0);

    private readonly Dictionary<int, VoiceSpeaker> _speakers = new();
    private readonly Dictionary<int, long> _rxCounts = new();
    // Mute set keyed by both peer id (live filter) and SteamID64 (survives reconnect) — see
    // MuteRegistry. Replaces the old raw HashSet<int> so an intentional mute persists across a
    // player's reconnect instead of being silently cleared with their old peer id (P9).
    private readonly MuteRegistry _mutes = new();
    private readonly Dictionary<string, double> _rejectLogTimes = new();
    // Speaker id -> that speaker's replicated avatar node (perf audit 2026-08-07). EmitPosFor ran
    // speakerId.ToString() + a string-keyed GetNodeOrNull for EVERY speaker EVERY frame, so the
    // cost was (players - 1) x 60 string allocations, NodePath constructions and tree walks per
    // second and it grew with the lobby. The cache is only ever TRUSTED while the node still
    // passes IsInstanceValid + IsInsideTree, which is precisely the window in which the old
    // lookup would have returned that same node - a freed avatar, a queue-freed one, or one
    // reparented out of _playersRoot all fall through to the original lookup unchanged.
    private readonly Dictionary<int, Node3D> _avatarByPeer = new();
    // --- Server-side proximity relay gate (perf followups 2026-08-07) -----------------------
    // OFF unless VoiceProximityGate.EnabledByDefault says otherwise; see that class for the
    // switch, the radii, and why enabling it is Talon's call rather than an agent's. All three
    // fields are server-only state and stay untouched (and unallocated beyond the empty
    // containers) on every client.
    private readonly VoiceRelayDecider _relayGate = new();
    // Peer -> (when sampled, world position). GlobalPosition is an engine interop call; sampling
    // it per (talker, listener) PAIR per packet would be ~1,800 calls/sec at six players, so it
    // is cached for VoiceProximityGate.PositionSampleIntervalSec. See that constant for the
    // accuracy trade.
    private readonly Dictionary<int, (double At, Vector3? Pos)> _serverPosCache = new();
    private bool _gateLogged;
    private ConnectionRateLimiter<int> _rateLimiter = NewRateLimiter();
    private Node3D? _playersRoot;
    private VoiceCapture? _capture;
    private double _decodeDropLogSec;

    /// <summary>The broadcast hook: given a sender's peer id, is that peer currently
    /// broadcasting on the PA bus (see <see cref="EnsurePaBusName"/>) instead of ordinary
    /// proximity? A game wires this to whatever replicated state decides "who's live" (a
    /// held broadcast prop, a zone, etc.) — the route costs zero extra networking, since it
    /// reads state every peer already has. Null (the default) = everyone proximity.
    ///
    /// READ ON THE SERVER TOO, as of the voice proximity gate (2026-08-07). The gate culls
    /// relays by distance, and a PA broadcast has NO distance falloff (VoiceSpeaker.SetRoute
    /// sets MaxDistance = 0), so gating a PA sender by distance would silently mute the
    /// intercom for everyone outside 30 m — the exact "built as literally requested, the
    /// obvious implication unhandled" failure this repo keeps hitting. The exemption reads
    /// THIS hook on the server: PA is a pure function of replicated state and the server is
    /// the source of that state, so the same predicate resolves identically on both sides.
    ///
    /// THE CONTRACT THIS PUTS ON WHOEVER WIRES PA: set it on the server as well as on clients.
    /// A client-only wiring is safe while <see cref="VoiceProximityGate.EnabledByDefault"/> is
    /// false (nothing is culled) and silently wrong the moment it is true. The server prints
    /// which way it went (see <see cref="LogGateOnce"/>) at its first gated relay, so a muted
    /// PA is diagnosable from a log line instead of from a playtest.</summary>
    public System.Func<int, bool>? PaResolver { get; set; }

    /// <summary>
    /// <b>The server's listener-relative PA hook</b> (VOICE-1). Given (talker, listener), is that
    /// talker on the PA route <i>for that listener</i>?
    ///
    /// <para><b>Why the pair, when <see cref="PaResolver"/> already exists.</b> "T is on the PA"
    /// is not a property of T. The supermarket's intercom is cross-ROOM voice: T is on the PA for
    /// everyone in a different room and on ordinary proximity for everyone in the same one, at
    /// the same instant. On a client that distinction is invisible because the listener is always
    /// the local player — which is exactly why the per-talker signature was enough until now, and
    /// exactly why it is not enough on the relay, where one packet is judged against every peer
    /// in the match.</para>
    ///
    /// <para><b>Null = today's behaviour, byte for byte.</b> <see cref="RelayGated"/> falls back
    /// to <see cref="PaResolver"/> for every listener, which is what <c>--voice-pa-all</c> and
    /// every pre-VOICE-1 build do. Wired, it WINS over <see cref="PaResolver"/> on the server —
    /// so anything setting a blanket per-talker resolver on the server (the test flag) must set
    /// this one too or it is silently outvoted.</para>
    ///
    /// <para>Server-side only; a client never relays. The server prints which hooks are wired at
    /// its first gated relay — see <see cref="LogGateOnce"/>.</para>
    /// </summary>
    public System.Func<int, int, bool>? PaPairResolver { get; set; }

    /// <summary>
    /// <b>Where a peer is</b>, as the game decides it — the supermarket wires this to the round's
    /// authoritative room map (<c>HideSeekDriver.RoomOf</c>). Empty string = unknown.
    ///
    /// <para><b>Diagnostic, not a decision.</b> Nothing in the relay or the route reads this; the
    /// two resolvers above are what decide, and the wiring derives all three from one source so
    /// they cannot disagree. This one exists so <see cref="GetEmitRouting"/> can say WHY a route
    /// came out the way it did — "both ends say pa" is also what two unknown rooms produce, and
    /// that is the fail-open path rather than the intercom working.</para>
    /// </summary>
    public System.Func<int, string>? RoomResolver { get; set; }

    /// <summary>Test/telemetry seam: the route the local client would give this sender's
    /// voice right now ("pa" or "proximity"). Resolved exactly like _Process does.</summary>
    public string DescribeRouteFor(int peerId) =>
        PaResolver?.Invoke(peerId) == true ? VoiceRouting.RoutePa : VoiceRouting.RouteProximity;

    /// <summary>This process's room for <paramref name="peerId"/>, or
    /// <see cref="Game.Round.RoundRooms.Unknown"/> when nothing has wired
    /// <see cref="RoomResolver"/>.</summary>
    public string DescribeRoomOf(int peerId) =>
        RoomResolver?.Invoke(peerId) ?? Game.Round.RoundRooms.Unknown;

    /// <summary>
    /// <b>The routing verdict</b> (VOICE-1): this process's whole decision about every voice it
    /// can currently hear — its own room, each known peer's room, and the route that pair
    /// resolves to — plus which of the three hooks are wired and whether the relay gate is on.
    ///
    /// <para><b>Asserted on by the suites rather than inferred from a log.</b> A bot embeds it in
    /// every sample (<c>vroute</c>), so "cross-room says pa on both peers and same-room says
    /// proximity" is a claim about a value the game published, not about a sentence somebody
    /// wrote in a print statement.</para>
    ///
    /// <para>Peers come from the replicated player list, not from the transport's peer list —
    /// the same rule <see cref="GetRemotePlayers"/> already follows.</para>
    /// </summary>
    public VoiceRouting.Verdict GetEmitRoutingVerdict()
    {
        int self = Multiplayer.MultiplayerPeer is null or OfflineMultiplayerPeer
            ? 0
            : Multiplayer.GetUniqueId();
        var peers = new List<VoiceRouting.PeerVerdict>();
        string selfRoom = DescribeRoomOf(self);
        foreach ((int id, string name) in GetRemotePlayers())
        {
            string room = DescribeRoomOf(id);
            // The ROUTE is read from the resolver that actually drives VoiceSpeaker, not
            // recomputed from the two rooms beside it: a verdict that recomputed its own answer
            // would stay green with the resolver unwired.
            peers.Add(new VoiceRouting.PeerVerdict(id, name, room, DescribeRouteFor(id)));
        }
        peers.Sort(static (a, b) => a.Id.CompareTo(b.Id));

        return new VoiceRouting.Verdict(
            Self: self,
            Room: selfRoom,
            Server: Multiplayer.IsServer(),
            GateOn: VoiceProximityGate.Enabled,
            PaResolverWired: PaResolver != null,
            PaPairResolverWired: PaPairResolver != null,
            RoomResolverWired: RoomResolver != null,
            Peers: peers);
    }

    /// <inheritdoc cref="GetEmitRoutingVerdict"/>
    public string GetEmitRouting() =>
        System.Text.Json.JsonSerializer.Serialize(GetEmitRoutingVerdict(), RoutingJsonOptions);

    private static readonly System.Text.Json.JsonSerializerOptions RoutingJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>
    /// <b>Who is talking on the intercom right now</b>, for the HUD lamp — the loudest remote
    /// peer whose voice this client is routing to the PA bus, or <c>(0, "")</c> when nobody is.
    ///
    /// <para><b>The redundant channel INTERACTION-BIBLE §8.2 asks for.</b> A cross-room voice is
    /// deliberately filtered and quiet; a hider facing away from nothing in particular can miss
    /// that the seeker is taunting them at all, and "did they say something?" is the one thing
    /// this design cannot afford to be ambiguous about, because the bluff IS the mechanic. The
    /// lamp is the non-audio half of the same consequence.</para>
    ///
    /// <para>Loudest wins rather than first-found so two talkers do not make the lamp flicker
    /// between two names at the poll rate.</para>
    /// </summary>
    public (int Id, string Name) PaSpeakerNow()
    {
        int bestId = 0;
        float best = PaLampEnvelopeFloor;
        foreach (KeyValuePair<int, VoiceSpeaker> kv in _speakers)
        {
            if (kv.Value.Envelope <= best || PaResolver?.Invoke(kv.Key) != true)
                continue;
            best = kv.Value.Envelope;
            bestId = kv.Key;
        }
        if (bestId == 0)
            return (0, string.Empty);

        string name = string.Empty;
        if (_playersRoot != null && GodotObject.IsInstanceValid(_playersRoot)
            && _playersRoot.GetNodeOrNull<Node3D>(bestId.ToString()) is SandboxAvatar avatar)
        {
            name = avatar.DisplayName;
        }
        return (bestId, name);
    }

    /// <summary>Below this the envelope is room tone or the tail of a word, not somebody talking.
    /// An order of magnitude above <see cref="VoiceEnvelope.SilenceFloor"/>'s effect so the lamp
    /// does not strobe between syllables; the widget holds it on top of this.</summary>
    private const float PaLampEnvelopeFloor = 0.05f;

    // Keyed by peer id, not by peer id .ToString(). SubmitVoice ran the limiter on EVERY
    // inbound voice packet — 300/sec at six talkers — and each call allocated a string just to
    // look the sender up, plus string hashing on the way in (perf followups 2026-08-07). The
    // int-keyed generic is the same limiter with the same window semantics; the string-keyed
    // form still exists unchanged for NetworkManager, whose keys really are strings (IPs and
    // steam:<id64> identities).
    private static ConnectionRateLimiter<int> NewRateLimiter() =>
        new(VoiceConfig.MaxPacketsPerWindow, VoiceConfig.RateWindowSec);

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        // Autoload lifetime: subscribe once, never torn down (mirrors the pre-existing
        // PeerDisconnected wiring). SceneMultiplayer relays both peer signals to every peer, so
        // these fire on the muting client when a remote peer joins/leaves the match.
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    // On (re)connect, re-apply any identity-keyed mute this client holds against the joining
    // peer's stable identity (P9). NetworkManager.SteamId64Of is the identity source; it returns
    // 0 for ENet and, with today's transport, on the client side (see MuteRegistry's fidelity
    // note), in which case this is a clean no-op and the peer-id fallback path stands.
    private void OnPeerConnected(long id) =>
        _mutes.ReapplyOnConnect((int)id, NetworkManager.Instance.SteamId64Of((int)id));

    // --- Session binding (called by Gameplay on scene enter/exit) -----------------
    public void BindPlayersRoot(Node3D playersRoot)
    {
        _playersRoot = playersRoot;
        _avatarByPeer.Clear(); // every cached node belonged to the previous session's root
        _relayGate.Clear();    // and every open relay pair belonged to its peer set
        _serverPosCache.Clear();
        _gateLogged = false;
        _rxCounts.Clear();
        _mutes.Clear();
        _rejectLogTimes.Clear();
        _rateLimiter = NewRateLimiter();

        var net = NetworkManager.Instance;
        if (net.Role == NetworkManager.SessionRole.Client && !net.IsBot && !net.IsHeadless && _capture == null)
        {
            _capture = new VoiceCapture();
            AddChild(_capture);
        }
    }

    public void UnbindPlayersRoot()
    {
        _playersRoot = null;
        _avatarByPeer.Clear();
        _relayGate.Clear();
        _serverPosCache.Clear();
        _gateLogged = false;
        PaResolver = null;
        // Every VOICE-1 hook closes over the session's round driver and its peer ids, so all
        // three go with the session for the same reason the resolver above always has.
        PaPairResolver = null;
        RoomResolver = null;
        foreach (VoiceSpeaker speaker in _speakers.Values)
            speaker.Cleanup();
        _speakers.Clear();
        if (_capture != null)
        {
            _capture.QueueFree();
            _capture = null;
        }
    }

    private void OnPeerDisconnected(long id)
    {
        // Drop the cached avatar with the peer: a recycled peer id must resolve afresh rather
        // than inherit a departed player's node (same reasoning as the peer-id mute entry below).
        _avatarByPeer.Remove((int)id);
        // Same reasoning, same recycled-id hazard: an open relay pair or a cached position
        // belonging to a departed peer must never be inherited by whoever gets that id next.
        _relayGate.ForgetPeer((int)id);
        _serverPosCache.Remove((int)id);
        if (_speakers.TryGetValue((int)id, out VoiceSpeaker? speaker))
        {
            speaker.Cleanup();
            _speakers.Remove((int)id);
        }
        // A recycled peer id must never inherit a stale mute, so drop the PEER-ID entry here;
        // the identity entry (if any) deliberately SURVIVES so a reconnect under a new peer id
        // re-mutes (P9 — see MuteRegistry.OnPeerDisconnected). _rxCounts also deliberately
        // SURVIVES the peer — it is session-scoped receive telemetry ("how much did I ever hear
        // from X", read by the bot harness and the voice suite after senders have already left)
        // and is cleared with the session in BindPlayersRoot.
        _mutes.OnPeerDisconnected((int)id);
    }

    public override void _Process(double delta)
    {
        if (_speakers.Count == 0)
            return;
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (KeyValuePair<int, VoiceSpeaker> kv in _speakers)
        {
            // Re-resolve the route from replicated state every frame (SetRoute is
            // idempotent — this is a bool check + rare param writes, not per-frame work).
            kv.Value.SetRoute(PaResolver?.Invoke(kv.Key) == true ? VoiceRoute.Pa : VoiceRoute.Proximity);

            // Ticked unconditionally, and before Tick's own early-outs: it is the absence
            // of packets that closes a mouth, so an envelope that stops advancing freezes
            // the mouth open. Independent of whether that speaker's avatar exists yet.
            kv.Value.TickEnvelope((float)delta);

            kv.Value.Tick(now, _playersRoot, EmitPosFor(kv.Key));
        }
    }

    /// <summary>0..1 mouth openness for any peer, the local player included. Returns 0 for a
    /// peer that is not speaking or not known.
    ///
    /// Keyed by PEER ID, deliberately not by the voice emitter's position: VoiceSpeaker
    /// parks its AudioStreamPlayer3D on a stable root and repositions it each tick, and a
    /// PA or phone bridge moves it away from the speaker's body entirely — so where a voice
    /// comes out is not who is talking.</summary>
    public float GetVoiceEnvelope(int peerId)
    {
        if (_capture != null && peerId == Multiplayer.GetUniqueId())
            return _capture.Envelope;
        return _speakers.TryGetValue(peerId, out VoiceSpeaker? speaker) ? speaker.Envelope : 0f;
    }

    // Resolves where speaker <paramref name="speakerId"/>'s voice plays: the speaker's own
    // avatar head, or null when that avatar isn't present yet (spawn/despawn race) so the
    // speaker simply stays quiet until it appears.
    private Vector3? EmitPosFor(int speakerId)
    {
        if (_playersRoot == null || !GodotObject.IsInstanceValid(_playersRoot))
            return null;
        if (_avatarByPeer.TryGetValue(speakerId, out Node3D? cached))
        {
            if (GodotObject.IsInstanceValid(cached) && cached.IsInsideTree())
                return HeadPositionOf(cached);
            _avatarByPeer.Remove(speakerId);
        }
        var avatar = _playersRoot.GetNodeOrNull<Node3D>(speakerId.ToString());
        if (avatar == null)
            return null;
        _avatarByPeer[speakerId] = avatar;
        return HeadPositionOf(avatar);
    }

    /// <summary>Where a speaker's voice leaves their body: their own measured head, or — for
    /// anything that is not an avatar — the fallback lift. See <see cref="AvatarHeadOffset"/>.
    ///
    /// <para><b>Public since HONK-1 (2026-09-04), and shared rather than copied.</b> The goose honk
    /// is a stand-in for proximity voice for players without a microphone, so it has to leave the
    /// body at the same point a voice does — a honk emitting from the feet while a voice emits from
    /// the head is a difference nobody would ever think to look for and everybody would hear. The
    /// alternative was one duplicated ternary, and this rule has already been wrong once in this
    /// file's history (the 1.60 m adult-head constant, against a cast of metre-tall kids); a second
    /// copy is a second place for it to go wrong.</para></summary>
    public static Vector3 HeadPositionOf(Node3D avatar) => avatar is SandboxAvatar sandboxAvatar
        ? sandboxAvatar.VoiceOriginGlobalPosition
        : avatar.GlobalPosition + AvatarHeadOffset;

    // --- Sending (client) ----------------------------------------------------------
    /// <summary>Sends one already-framed voice packet ([seq][opus]) to the server.</summary>
    public void SendVoicePacket(byte[] packet)
    {
        if (Multiplayer.MultiplayerPeer is null or OfflineMultiplayerPeer)
            return;
        if (Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
            return;
        if (Multiplayer.GetUniqueId() == 1)
            return; // the dedicated server has no voice of its own
        RpcId(1, MethodName.SubmitVoice, packet);
    }

    // --- Server relay ----------------------------------------------------------------
    // Validate-then-relay only: the server never decodes, stores, or inspects audio.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = VoiceConfig.TransferChannel)]
    private void SubmitVoice(byte[] packet)
    {
        // SceneMultiplayer relays client->client RPCs by default; a hostile client could
        // aim SubmitVoice at another client, so non-servers ignore it outright.
        if (!Multiplayer.IsServer())
            return;

        int sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0)
            return;
        if (!NetworkManager.Instance.IsPeerAccepted(sender))
        {
            LogRejectThrottled("unauth", sender, "voice from unauthenticated peer dropped", $"peer={sender}");
            return;
        }
        if (packet is null || packet.Length < VoiceConfig.MinPacketBytes)
        {
            LogRejectThrottled("malformed", sender, "malformed voice packet rejected", $"peer={sender} bytes={packet?.Length ?? 0}");
            return;
        }
        if (packet.Length > VoiceConfig.MaxPacketBytes)
        {
            LogRejectThrottled("oversized", sender, "oversized voice packet rejected", $"peer={sender} bytes={packet.Length} max={VoiceConfig.MaxPacketBytes}");
            return;
        }
        double now = Time.GetTicksMsec() / 1000.0;
        if (!_rateLimiter.Allow(sender, now))
        {
            LogRejectThrottled("rate", sender, "voice rate limit: packet dropped", $"peer={sender} budget={VoiceConfig.MaxPacketsPerWindow}/{VoiceConfig.RateWindowSec:F0}s");
            return;
        }

        if (!VoiceProximityGate.Enabled)
        {
            // THE SHIPPING PATH, unchanged. This loop is byte-for-byte what it always was, and
            // it is deliberately duplicated rather than folded into the gated loop below with an
            // `if` inside it: with the switch off, nothing about relaying — not one distance
            // check, not one dictionary probe, not one branch per peer — is different from the
            // build before the gate existed.
            //
            // Only relay to peers that passed the accept handshake: a transport-connected but
            // never-accepted peer must not be able to listen in on the match. (Deliberately no
            // proximity cull — the server never inspects positions for voice, and attenuation
            // is the client's job; see the risk audit's interest-management decision. That
            // stance is what VoiceProximityGate reverses, and it is Talon's switch to throw,
            // not an agent's — the stance is not deleted here, it is the default.)
            foreach (int peer in Multiplayer.GetPeers())
            {
                if (peer != sender && NetworkManager.Instance.IsPeerAccepted(peer))
                    RpcId(peer, MethodName.ReceiveVoice, sender, packet);
            }
            return;
        }

        RelayGated(sender, packet, now);
    }

    /// <summary>The gated relay. The talker's own position is still hoisted out of the peer loop
    /// (it cannot change inside it) and is resolved LAZILY, so a packet every listener is exempt
    /// for costs no position sample at all.
    ///
    /// <para><b>The exemption is per (talker, listener) PAIR as of VOICE-1</b>, not per packet.
    /// It used to be one <c>PaResolver(sender)</c> hoisted above the loop, which is correct for a
    /// broadcast prop — "T is on the PA" full stop — and wrong for an intercom keyed on rooms,
    /// where the same sentence is a PA broadcast to the player two rooms away and ordinary
    /// proximity to the one standing beside them. <see cref="PaPairResolver"/> is the pairwise
    /// hook; with it unwired this method is byte-for-byte what it was, because every listener
    /// then gets the same per-talker answer the hoisted call used to produce.</para></summary>
    private void RelayGated(int sender, byte[] packet, double now)
    {
        System.Func<int, int, bool>? pairResolver = PaPairResolver;
        // The per-talker fallback, and the only thing read when nothing wired the pairwise hook.
        bool senderPaForAll = pairResolver is null && PaResolver?.Invoke(sender) == true;
        LogGateOnce();

        Vector3? talkerPos = null;
        bool talkerPosSampled = false;

        foreach (int peer in Multiplayer.GetPeers())
        {
            if (peer == sender || !NetworkManager.Instance.IsPeerAccepted(peer))
                continue;

            bool paExempt = pairResolver is not null ? pairResolver(sender, peer) : senderPaForAll;
            Vector3? listenerPos = null;
            if (!paExempt)
            {
                if (!talkerPosSampled)
                {
                    talkerPos = ServerPosFor(sender, now);
                    talkerPosSampled = true;
                }
                listenerPos = ServerPosFor(peer, now);
            }

            if (!_relayGate.ShouldRelay(sender, peer, paExempt ? null : talkerPos, listenerPos, paExempt))
                continue;
            RpcId(peer, MethodName.ReceiveVoice, sender, packet);
        }
    }

    /// <summary>Server-side authoritative position of <paramref name="peerId"/>'s avatar, or
    /// null when it cannot be resolved (the spawn/despawn race <see cref="EmitPosFor"/> already
    /// documents) — which the gate treats as fail-open. Cached for
    /// <see cref="VoiceProximityGate.PositionSampleIntervalSec"/>; see that constant for the
    /// interop trade. The null result is cached too, deliberately: a peer whose avatar has not
    /// spawned yet would otherwise pay a full failed GetNodeOrNull tree walk on every packet
    /// from every talker, which is the worst case, not the cheap one.</summary>
    private Vector3? ServerPosFor(int peerId, double now)
    {
        if (_serverPosCache.TryGetValue(peerId, out (double At, Vector3? Pos) cached)
            && now - cached.At < VoiceProximityGate.PositionSampleIntervalSec)
            return cached.Pos;
        Vector3? pos = EmitPosFor(peerId);
        _serverPosCache[peerId] = (now, pos);
        return pos;
    }

    /// <summary>One line, once per session, recording the facts that decide whether the
    /// gate is safe in THIS build: that it is on, and which PA hooks are wired on the
    /// server. A silenced PA is otherwise a bug with no evidence — it looks exactly like
    /// "the intercom didn't work", which is unreportable.
    ///
    /// <para><b><c>paPairResolver</c> joined the line at VOICE-1</b> and is the one that matters
    /// for a room-keyed intercom: <c>paResolver=wired paPairResolver=NOT-WIRED</c> on the
    /// supermarket means the relay is answering "is T on the PA" for the whole match at once,
    /// which is the exact shape of the hazard this line was written for, one level in.</para></summary>
    private void LogGateOnce()
    {
        if (_gateLogged)
            return;
        _gateLogged = true;
        ServerLog.Info("voice proximity gate active",
            $"enter={VoiceProximityGate.EnterRadiusM:F0}m exit={VoiceProximityGate.ExitRadiusM:F0}m " +
            $"audible={VoiceConfig.ProximityMaxDistance:F0}m " +
            $"paResolver={(PaResolver != null ? "wired" : "NOT-WIRED")} " +
            $"paPairResolver={(PaPairResolver != null ? "wired" : "NOT-WIRED")} " +
            $"roomResolver={(RoomResolver != null ? "wired" : "NOT-WIRED")}");
        GD.Print("[voice] gate on, "
                 + $"paResolver={(PaResolver != null ? "wired" : "NOT-WIRED")} "
                 + $"paPairResolver={(PaPairResolver != null ? "wired" : "NOT-WIRED")} "
                 + $"roomResolver={(RoomResolver != null ? "wired" : "NOT-WIRED")}");
    }

    /// <summary>Reads and resets the relay's packet counters (relayed / gated / PA-exempt) for
    /// <see cref="Net.NetStatsLogger"/>. Zero on a client — a client never relays.</summary>
    public (long Relayed, long Gated, long PaExempt) TakeRelayCounters() => _relayGate.TakeCounters();

    // --- Receiving (client) ------------------------------------------------------------
    // Authority mode: only the server (multiplayer authority) may deliver voice, so a
    // hostile client cannot spoof another player's id via the server relay.
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = VoiceConfig.TransferChannel)]
    private void ReceiveVoice(int senderId, byte[] packet)
    {
        if (Multiplayer.IsServer())
            return;
        // The server relays without decoding, so this payload is as untrusted as any
        // client input: bound it and treat decode failures as droppable, never fatal.
        if (packet is null || packet.Length < VoiceConfig.MinPacketBytes || packet.Length > VoiceConfig.MaxPacketBytes)
            return;

        _rxCounts[senderId] = _rxCounts.GetValueOrDefault(senderId) + 1;
        if (_mutes.IsMuted(senderId))
            return;

        if (!_speakers.TryGetValue(senderId, out VoiceSpeaker? speaker))
        {
            speaker = new VoiceSpeaker(senderId, NetworkManager.Instance.IsHeadless);
            _speakers[senderId] = speaker;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        if (!speaker.Submit(packet, now) && speaker.LastFrameUndecodable && now - _decodeDropLogSec >= RejectLogCooldownSec)
        {
            _decodeDropLogSec = now;
            GD.Print($"[voice] dropped undecodable frame from peer {senderId}");
        }
    }

    // --- Local controls ------------------------------------------------------------------
    /// <summary>Local-only per-player mute: an audio control, not moderation. Muting
    /// never affects what the muted player hears or is told. Records against both the peer id
    /// (live filter) and the peer's SteamID64 where resolvable, so the choice survives the
    /// player's reconnect (P9 — see MuteRegistry). SteamId64Of is 0 for ENet / unresolvable
    /// identities, which degrades cleanly to peer-id-only muting.</summary>
    public void SetMuted(int peerId, bool muted)
    {
        ulong id64 = NetworkManager.Instance.SteamId64Of(peerId);
        if (muted)
        {
            _mutes.Mute(peerId, id64);
        }
        else
        {
            _mutes.Unmute(peerId, id64);
            // While muted, the speaker's sequence high-water mark froze (Submit never ran). If
            // the wire counter wrapped past it in the meantime, every live frame would read as
            // stale for up to ~11 minutes — resync from the next packet instead.
            if (_speakers.TryGetValue(peerId, out VoiceSpeaker? speaker))
                speaker.ResetSequence();
        }
    }

    public bool IsMuted(int peerId) => _mutes.IsMuted(peerId);

    /// <summary>Remote players currently in the match (for the mute list UI).</summary>
    public List<(int Id, string Name)> GetRemotePlayers()
    {
        var result = new List<(int, string)>();
        if (_playersRoot == null || !GodotObject.IsInstanceValid(_playersRoot))
            return result;
        int self = Multiplayer.GetUniqueId();
        foreach (Node child in _playersRoot.GetChildren())
        {
            if (child is Game.Sandbox.SandboxAvatar player && int.TryParse(player.Name.ToString(), out int id) && id != self)
                result.Add((id, player.DisplayName));
        }
        return result;
    }

    /// <summary>Per-sender received-packet counts, string-keyed for JSON. The bot
    /// harness logs these so the relay mechanism is provable headlessly.</summary>
    public Dictionary<string, long> GetReceiveCounts()
    {
        var snapshot = new Dictionary<string, long>(_rxCounts.Count);
        foreach (KeyValuePair<int, long> kv in _rxCounts)
            snapshot[kv.Key.ToString()] = kv.Value;
        return snapshot;
    }

    /// <summary>Creates the voice output bus (routed to Master) on first use.</summary>
    public static int EnsureOutputBus()
    {
        int idx = AudioServer.GetBusIndex(VoiceConfig.OutputBus);
        if (idx >= 0)
            return idx;
        idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, VoiceConfig.OutputBus);
        AudioServer.SetBusSend(idx, "Master");
        return idx;
    }

    /// <summary>Creates the PA bus on first use and returns its name. The whole intercom
    /// "voice through a speaker in a creepy building" character is three stock bus
    /// effects — light overdrive (cheap speaker cone), a hard lowpass (small driver, no
    /// highs), a boxy reverb (the building answers back). Bus DSP runs once per audio
    /// block regardless of listener count: §5's best juice-per-cost, by design.</summary>
    public static string EnsurePaBusName()
    {
        const string name = "PA";
        if (AudioServer.GetBusIndex(name) >= 0)
            return name;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, "Master");
        AudioServer.AddBusEffect(idx, new AudioEffectDistortion
        {
            Mode = AudioEffectDistortion.ModeEnum.Overdrive,
            Drive = 0.28f,
            PostGain = -3f,
        });
        AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter
        {
            CutoffHz = 2200f,
            Resonance = 0.6f,
        });
        AudioServer.AddBusEffect(idx, new AudioEffectReverb
        {
            RoomSize = 0.7f,
            Damping = 0.4f,
            Wet = VoiceRouting.WetFromDb(VoiceConfig.IntercomWetDb),
            Dry = 0.9f,
        });
        return name;
    }

    /// <summary>
    /// <b>Re-applies <see cref="VoiceConfig.IntercomWetDb"/> to a PA bus that already exists.</b>
    /// A no-op before the first PA voice builds the bus — the value is read at construction, so
    /// setting the knob early needs nothing else.
    ///
    /// <para>Separate from <see cref="EnsurePaBusName"/> because the bus is built lazily on the
    /// first cross-room voice, which on a two-player round can be a minute into the session. A
    /// launch flag that only wrote the field would appear to do nothing for that minute and then
    /// work, which is worse than either.</para>
    ///
    /// <para>Walks the bus's effects by TYPE, never by index: the three effects are added in a
    /// fixed order today, but a reader who inserts a fourth should not have to know that a
    /// hard-coded <c>2</c> somewhere else depends on it.</para>
    /// </summary>
    public static void ApplyIntercomWetDb()
    {
        int idx = AudioServer.GetBusIndex("PA");
        if (idx < 0)
            return; // nothing has spoken on the PA yet; EnsurePaBusName will read the knob.
        float wet = VoiceRouting.WetFromDb(VoiceConfig.IntercomWetDb);
        for (int i = 0; i < AudioServer.GetBusEffectCount(idx); i++)
            if (AudioServer.GetBusEffect(idx, i) is AudioEffectReverb reverb)
                reverb.Wet = wet;
    }

    private void LogRejectThrottled(string reason, int peer, string message, string kv)
    {
        // A flood would otherwise write hundreds of identical lines per second; one line
        // per reason+peer per second is plenty for diagnosis (same idea as the movement
        // plausibility log cooldown).
        string key = $"{reason}:{peer}";
        double now = Time.GetTicksMsec() / 1000.0;
        if (_rejectLogTimes.TryGetValue(key, out double last) && now - last < RejectLogCooldownSec)
            return;
        _rejectLogTimes[key] = now;
        ServerLog.Warn(message, kv);
    }
}
