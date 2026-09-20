using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace MpFoundation.Net.Steam;

/// <summary>
/// Godot MultiplayerPeer over Steam Networking Sockets P2P — the relay transport for real
/// hosted matches (Practice stays on ENet: localhost has no NAT to traverse). Star
/// topology, exactly like the ENet setup: clients hold one connection to the server and
/// SceneMultiplayer relays client↔client traffic through it (_IsServerRelaySupported).
///
/// Identity split, and why it matters: the server side runs on the ANONYMOUS GAME-SERVER
/// Steam interfaces (SteamGameServerNetworkingSockets — the identity minted by
/// SteamService.StartGameServer), the client side on the player's own session. That split
/// is what lets a room code resolve to a headless match server that owns no Steam account
/// and shares no friends graph with anyone.
///
/// Wire format: every Godot packet is one SNS message prefixed with a single header byte
/// (transfer mode in the top 2 bits, channel in the low 6), because Godot's channel/mode
/// pair must survive the trip and SNS carries flags per message, not per stream. Header
/// values with mode bits 0b11 are transport-control messages: a client introduces itself
/// with its self-generated peer id (hello), the server confirms (ack) — the same
/// client-picks-its-id convention ENetMultiplayerPeer uses, carried explicitly.
/// </summary>
public partial class SteamPeer : MultiplayerPeerExtension
{
    private const int MaxChannel = 62;
    private const byte CtrlHello = 0xC1; // 0b11_000001
    private const byte CtrlAck = 0xC2;   // 0b11_000010
    private const int VirtualPort = 0;   // one match server per anonymous SteamID — no vport multiplexing needed
    private const int MaxMessagesPerPoll = 64;
    private const double HelloTimeoutSec = 10.0; // transport-slot guard, mirrors NetworkManager.AuthTimeoutSec's intent

    private readonly record struct Packet(int Peer, int Channel, TransferModeEnum Mode, byte[] Data);

    private bool _isServer;
    private bool _useGameServer; // which Steamworks interface flavor owns our sockets
    private int _uniqueId;
    private int _maxClients;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _refuseNew;
    private int _targetPeer;
    private int _transferChannel;
    private TransferModeEnum _transferMode = TransferModeEnum.Reliable;

    private readonly Queue<Packet> _incoming = new();
    private readonly IntPtr[] _msgBuffer = new IntPtr[MaxMessagesPerPoll];

    // Server state.
    private HSteamListenSocket _listen;
    private HSteamNetPollGroup _pollGroup;
    private readonly Dictionary<int, HSteamNetConnection> _connByPeer = new();
    private readonly Dictionary<HSteamNetConnection, int> _peerByConn = new();
    private readonly Dictionary<HSteamNetConnection, ulong> _steamIdByConn = new();
    private readonly Dictionary<HSteamNetConnection, ulong> _helloDeadline = new(); // accepted, no hello yet

    // Client state.
    private HSteamNetConnection _conn;
    private ulong _serverSteamId;

    private Callback<SteamNetConnectionStatusChangedCallback_t>? _statusChanged;

    private SteamPeer() { }

    /// <summary>
    /// Connection options applied to BOTH ends of every P2P socket: ICE pinned to
    /// <see cref="NetProfile.IceEnable"/> so the route cannot fall back to a direct
    /// peer-to-peer path that exposes each player's IP to the other.
    ///
    /// Set per-socket rather than globally because the server half of this class lives on the
    /// SteamGameServer interface and the client half on the SteamUser one — a global set on
    /// SteamNetworkingUtils would silently cover only one of them, which is precisely the kind of
    /// half-applied guard that reads as protection and is not.
    /// </summary>
    private static SteamNetworkingConfigValue_t[] P2POptions()
    {
        // Steamworks.NET exposes this as the raw C struct — a tag, a data-type discriminator and
        // an explicit-layout union — with no setter helper. m_eDataType is NOT optional: leaving
        // it at its default (0) tells the native side the union holds no Int32, and the value is
        // read as the wrong type or ignored. That failure is silent, and its symptom is simply
        // that ICE stays enabled — i.e. it looks exactly like this guard working.
        var ice = new SteamNetworkingConfigValue_t
        {
            m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable,
            m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
        };
        ice.m_val.m_int32 = NetProfile.IceEnable;
        return new[] { ice };
    }

    // --- Construction ---------------------------------------------------------------
    /// <summary>Server transport over the anonymous game-server identity. Requires
    /// SteamService.StartGameServer to have succeeded. Null + SteamService.LastError-style
    /// reason on failure (no exceptions into NetworkManager).</summary>
    public static SteamPeer? CreateServer(int maxClients, out string error)
    {
        error = "";
        var peer = new SteamPeer
        {
            _isServer = true,
            _useGameServer = true,
            _uniqueId = 1,
            _maxClients = maxClients,
            _status = ConnectionStatus.Connected,
        };
        peer._statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(peer.OnStatusChanged);
        peer._pollGroup = SteamGameServerNetworkingSockets.CreatePollGroup();
        SteamNetworkingConfigValue_t[] listenOptions = P2POptions();
        peer._listen = SteamGameServerNetworkingSockets.CreateListenSocketP2P(
            VirtualPort, listenOptions.Length, listenOptions);
        if (peer._listen == HSteamListenSocket.Invalid)
        {
            error = "Steam listen socket creation failed.";
            peer.TearDown();
            return null;
        }
        return peer;
    }

    /// <summary>Client transport over the player's own Steam session. Requires
    /// SteamService.EnsureClient to have succeeded.</summary>
    public static SteamPeer? CreateClient(ulong serverSteamId, out string error)
    {
        error = "";
        var peer = new SteamPeer
        {
            _isServer = false,
            _useGameServer = false,
            _serverSteamId = serverSteamId,
            _status = ConnectionStatus.Connecting,
        };
        peer._uniqueId = (int)(peer.GenerateUniqueId() & 0x7FFFFFFF);
        if (peer._uniqueId < 2)
            peer._uniqueId += 2;
        peer._statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(peer.OnStatusChanged);

        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(serverSteamId);
        SteamNetworkingConfigValue_t[] connectOptions = P2POptions();
        peer._conn = SteamNetworkingSockets.ConnectP2P(
            ref identity, VirtualPort, connectOptions.Length, connectOptions);
        if (peer._conn == HSteamNetConnection.Invalid)
        {
            error = "Steam P2P connect failed to start.";
            peer.TearDown();
            return null;
        }
        return peer;
    }

    /// <summary>The remote Steam identity behind a connected peer id, as a
    /// "steam:&lt;steamid64&gt;" string — the Steam-transport equivalent of
    /// ENetPacketPeer.GetRemoteAddress(), feeding the same rate-limit/auth bookkeeping.</summary>
    public string IdentityOf(int peerId)
    {
        if (!_isServer)
            return peerId == 1 ? SteamAddress.Format(_serverSteamId) : "unknown";
        if (_connByPeer.TryGetValue(peerId, out HSteamNetConnection conn)
            && _steamIdByConn.TryGetValue(conn, out ulong id))
            return SteamAddress.Format(id);
        return "unknown";
    }

    /// <summary>The remote peer's raw SteamID64, or 0 if unresolvable (called on the
    /// client side, where a peer's identity is never resolved locally, or before the
    /// peer's connection is tracked). Distinct from <see cref="IdentityOf"/>: this returns
    /// the number itself, not a formatted address string, because the Gameplay-layer
    /// reconnect-grace-window feature keys a lookup table on it rather than logging it —
    /// see docs/superpowers/specs/2026-07-12-client-reconnection-design.md.</summary>
    public ulong SteamId64Of(int peerId)
    {
        if (!_isServer)
            return 0;
        return _connByPeer.TryGetValue(peerId, out HSteamNetConnection conn)
            && _steamIdByConn.TryGetValue(conn, out ulong id)
            ? id
            : 0;
    }

    // --- Steam connection lifecycle ---------------------------------------------------
    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t ev)
    {
        HSteamNetConnection conn = ev.m_hConn;
        switch (ev.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                if (!_isServer)
                    return; // our own outgoing connection progressing
                if (_refuseNew || _peerByConn.Count + _helloDeadline.Count >= _maxClients)
                {
                    Sockets.Close(conn, "server full");
                    return;
                }
                if (Sockets.Accept(conn) != EResult.k_EResultOK)
                    Sockets.Close(conn, "accept failed");
                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                if (_isServer)
                {
                    Sockets.SetPollGroup(conn, _pollGroup);
                    _steamIdByConn[conn] = ev.m_info.m_identityRemote.GetSteamID64();
                    _helloDeadline[conn] = Time.GetTicksMsec() + (ulong)(HelloTimeoutSec * 1000);
                }
                else
                {
                    // Transport is up; introduce ourselves. Status stays Connecting until
                    // the server acks — Godot must not see "connected" before the peer id
                    // exchange makes replication addressable.
                    var hello = new byte[5];
                    hello[0] = CtrlHello;
                    BitConverter.TryWriteBytes(hello.AsSpan(1), _uniqueId);
                    SendRaw(_conn, hello, reliable: true);
                }
                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                if (_isServer)
                    DropServerSideConnection(conn);
                else
                    DropClientConnection();
                break;
        }
    }

    private void DropServerSideConnection(HSteamNetConnection conn)
    {
        Sockets.Close(conn, "closed");
        _helloDeadline.Remove(conn);
        _steamIdByConn.Remove(conn);
        if (_peerByConn.Remove(conn, out int peerId))
        {
            _connByPeer.Remove(peerId);
            EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, peerId);
        }
    }

    private void DropClientConnection()
    {
        bool wasConnected = _status == ConnectionStatus.Connected;
        Sockets.Close(_conn, "closed");
        _conn = HSteamNetConnection.Invalid;
        _status = ConnectionStatus.Disconnected;
        if (wasConnected)
            EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, 1);
    }

    // --- Wire handling ----------------------------------------------------------------
    private void HandleWire(HSteamNetConnection conn, byte[] wire)
    {
        if (wire.Length == 0)
            return;
        byte header = wire[0];

        if ((header >> 6) == 0b11) // transport-control message
        {
            HandleControl(conn, header, wire);
            return;
        }

        int peerId;
        if (_isServer)
        {
            if (!_peerByConn.TryGetValue(conn, out peerId))
                return; // data before hello completes: not addressable, drop
        }
        else
        {
            peerId = 1;
        }

        var payload = new byte[wire.Length - 1];
        Buffer.BlockCopy(wire, 1, payload, 0, payload.Length);
        _incoming.Enqueue(new Packet(peerId, header & 0x3F, (TransferModeEnum)((header >> 6) & 0x3), payload));
    }

    private void HandleControl(HSteamNetConnection conn, byte header, byte[] wire)
    {
        if (_isServer && header == CtrlHello)
        {
            if (wire.Length < 5)
            {
                DropServerSideConnection(conn);
                return;
            }
            int peerId = BitConverter.ToInt32(wire, 1);
            if (peerId < 2 || _connByPeer.ContainsKey(peerId) || !_helloDeadline.Remove(conn))
            {
                // Malformed id, id collision, or a hello from a connection we no longer
                // track — all unrecoverable for this connection, none for the match.
                DropServerSideConnection(conn);
                return;
            }
            _connByPeer[peerId] = conn;
            _peerByConn[conn] = peerId;
            SendRaw(conn, new[] { CtrlAck }, reliable: true);
            EmitSignal(MultiplayerPeer.SignalName.PeerConnected, peerId);
        }
        else if (!_isServer && header == CtrlAck && _status == ConnectionStatus.Connecting)
        {
            _status = ConnectionStatus.Connected;
            EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1);
        }
    }

    private Error SendRaw(HSteamNetConnection conn, byte[] wire, bool reliable)
    {
        int flags = reliable
            ? Constants.k_nSteamNetworkingSend_Reliable
            : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
        GCHandle handle = GCHandle.Alloc(wire, GCHandleType.Pinned);
        try
        {
            EResult result = Sockets.Send(conn, handle.AddrOfPinnedObject(), (uint)wire.Length, flags);
            return result == EResult.k_EResultOK ? Error.Ok : Error.Failed;
        }
        finally
        {
            handle.Free();
        }
    }

    // --- MultiplayerPeerExtension overrides --------------------------------------------
    public override void _Poll()
    {
        // Reap connections that opened a transport slot but never introduced themselves.
        if (_isServer && _helloDeadline.Count > 0)
        {
            ulong now = Time.GetTicksMsec();
            List<HSteamNetConnection>? expired = null;
            foreach (var kv in _helloDeadline)
            {
                if (now > kv.Value)
                    (expired ??= new()).Add(kv.Key);
            }
            if (expired != null)
            {
                foreach (HSteamNetConnection conn in expired)
                    DropServerSideConnection(conn);
            }
        }

        int count = _isServer
            ? Sockets.ReceiveOnPollGroup(_pollGroup, _msgBuffer)
            : (_conn == HSteamNetConnection.Invalid ? 0 : Sockets.ReceiveOnConnection(_conn, _msgBuffer));
        for (int i = 0; i < count; i++)
        {
            var msg = SteamNetworkingMessage_t.FromIntPtr(_msgBuffer[i]);
            var wire = new byte[msg.m_cbSize];
            Marshal.Copy(msg.m_pData, wire, 0, msg.m_cbSize);
            HSteamNetConnection conn = msg.m_conn;
            SteamNetworkingMessage_t.Release(_msgBuffer[i]);
            HandleWire(conn, wire);
        }
    }

    public override Error _PutPacketScript(byte[] pBuffer)
    {
        if (_status != ConnectionStatus.Connected)
            return Error.Unavailable;
        if (_transferChannel is < 0 or > MaxChannel)
            return Error.InvalidParameter;

        var wire = new byte[pBuffer.Length + 1];
        wire[0] = (byte)(((int)_transferMode & 0x3) << 6 | _transferChannel);
        Buffer.BlockCopy(pBuffer, 0, wire, 1, pBuffer.Length);
        bool reliable = _transferMode == TransferModeEnum.Reliable;

        if (!_isServer)
            return SendRaw(_conn, wire, reliable);

        if (_targetPeer > 0)
        {
            return _connByPeer.TryGetValue(_targetPeer, out HSteamNetConnection conn)
                ? SendRaw(conn, wire, reliable)
                : Error.Unavailable;
        }
        int exclude = _targetPeer < 0 ? -_targetPeer : 0;
        foreach (var kv in _connByPeer)
        {
            if (kv.Key != exclude)
                SendRaw(kv.Value, wire, reliable);
        }
        return Error.Ok;
    }

    public override byte[] _GetPacketScript()
    {
        return _incoming.Count > 0 ? _incoming.Dequeue().Data : Array.Empty<byte>();
    }

    public override int _GetAvailablePacketCount() => _incoming.Count;
    public override int _GetPacketPeer() => _incoming.Count > 0 ? _incoming.Peek().Peer : 0;
    public override int _GetPacketChannel() => _incoming.Count > 0 ? _incoming.Peek().Channel : 0;
    public override TransferModeEnum _GetPacketMode() => _incoming.Count > 0 ? _incoming.Peek().Mode : TransferModeEnum.Reliable;

    public override void _SetTargetPeer(int pPeer) => _targetPeer = pPeer;
    public override void _SetTransferChannel(int pChannel) => _transferChannel = pChannel;
    public override int _GetTransferChannel() => _transferChannel;
    public override void _SetTransferMode(TransferModeEnum pMode) => _transferMode = pMode;
    public override TransferModeEnum _GetTransferMode() => _transferMode;

    public override int _GetUniqueId() => _uniqueId;
    public override bool _IsServer() => _isServer;
    public override bool _IsServerRelaySupported() => true;
    public override ConnectionStatus _GetConnectionStatus() => _status;

    // SNS reliable-message ceiling. Godot fragments nothing above this; our real packets
    // (snapshots, RPCs, Opus frames) sit orders of magnitude below it.
    public override int _GetMaxPacketSize() => 512 * 1024;

    public override bool _IsRefusingNewConnections() => _refuseNew;
    public override void _SetRefuseNewConnections(bool pEnable) => _refuseNew = pEnable;

    public override void _DisconnectPeer(int pPeer, bool pForce)
    {
        if (!_isServer)
            return;
        if (_connByPeer.TryGetValue(pPeer, out HSteamNetConnection conn))
            DropServerSideConnection(conn); // closes, unmaps, and emits PeerDisconnected
    }

    public override void _Close() => TearDown();

    private void TearDown()
    {
        if (_isServer)
        {
            foreach (HSteamNetConnection conn in _peerByConn.Keys)
                Sockets.Close(conn, "shutdown");
            foreach (HSteamNetConnection conn in _helloDeadline.Keys)
                Sockets.Close(conn, "shutdown");
            _connByPeer.Clear();
            _peerByConn.Clear();
            _steamIdByConn.Clear();
            _helloDeadline.Clear();
            if (_listen != HSteamListenSocket.Invalid)
            {
                Sockets.CloseListen(_listen);
                _listen = HSteamListenSocket.Invalid;
            }
            if (_pollGroup != HSteamNetPollGroup.Invalid)
            {
                Sockets.DestroyPollGroup(_pollGroup);
                _pollGroup = HSteamNetPollGroup.Invalid;
            }
        }
        else if (_conn != HSteamNetConnection.Invalid)
        {
            Sockets.Close(_conn, "shutdown");
            _conn = HSteamNetConnection.Invalid;
        }
        _statusChanged?.Dispose();
        _statusChanged = null;
        _incoming.Clear();
        _status = ConnectionStatus.Disconnected;
    }

    // --- Interface-flavor dispatch ------------------------------------------------------
    // The game-server and client Steamworks interfaces are separate static APIs with
    // identical shapes; this indirection keeps every call site flavor-correct without
    // duplicating the peer logic. Instance property so it follows _useGameServer.
    private SocketsApi Sockets => new(_useGameServer);

    private readonly struct SocketsApi
    {
        private readonly bool _gs;
        public SocketsApi(bool gameServer) => _gs = gameServer;

        public EResult Accept(HSteamNetConnection conn) =>
            _gs ? SteamGameServerNetworkingSockets.AcceptConnection(conn)
                : SteamNetworkingSockets.AcceptConnection(conn);

        public void Close(HSteamNetConnection conn, string reason)
        {
            if (_gs)
                SteamGameServerNetworkingSockets.CloseConnection(conn, 0, reason, false);
            else
                SteamNetworkingSockets.CloseConnection(conn, 0, reason, false);
        }

        public void CloseListen(HSteamListenSocket listen)
        {
            if (_gs)
                SteamGameServerNetworkingSockets.CloseListenSocket(listen);
            else
                SteamNetworkingSockets.CloseListenSocket(listen);
        }

        public void DestroyPollGroup(HSteamNetPollGroup group)
        {
            if (_gs)
                SteamGameServerNetworkingSockets.DestroyPollGroup(group);
            else
                SteamNetworkingSockets.DestroyPollGroup(group);
        }

        public void SetPollGroup(HSteamNetConnection conn, HSteamNetPollGroup group)
        {
            if (_gs)
                SteamGameServerNetworkingSockets.SetConnectionPollGroup(conn, group);
            else
                SteamNetworkingSockets.SetConnectionPollGroup(conn, group);
        }

        public EResult Send(HSteamNetConnection conn, IntPtr data, uint size, int flags) =>
            _gs ? SteamGameServerNetworkingSockets.SendMessageToConnection(conn, data, size, flags, out _)
                : SteamNetworkingSockets.SendMessageToConnection(conn, data, size, flags, out _);

        public int ReceiveOnPollGroup(HSteamNetPollGroup group, IntPtr[] buffer) =>
            _gs ? SteamGameServerNetworkingSockets.ReceiveMessagesOnPollGroup(group, buffer, buffer.Length)
                : SteamNetworkingSockets.ReceiveMessagesOnPollGroup(group, buffer, buffer.Length);

        public int ReceiveOnConnection(HSteamNetConnection conn, IntPtr[] buffer) =>
            _gs ? SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(conn, buffer, buffer.Length)
                : SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, buffer, buffer.Length);
    }
}
