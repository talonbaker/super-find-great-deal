using System.Globalization;
using System.Text;

namespace MpFoundation.Net;

/// <summary>
/// The version handshake payloads exchanged over Godot's SceneMultiplayer auth channel
/// before a connection is treated as established. Deliberately a tiny, strict,
/// fixed-shape text format (not JSON) so the parser is trivial to audit and a malformed
/// or oversized payload from an untrusted peer is cheap to reject.
///
/// Client hello:  "MPF;C;&lt;protocol&gt;"            (no room code — LAN / direct / CI)
///            or  "MPF;C;&lt;protocol&gt;;&lt;roomCode&gt;" (joining a room by code)
/// Server hello:  "MPF;S;&lt;protocol&gt;;&lt;status&gt;"   status = OK | FULL
///
/// The optional room-code field turns possession of the code into an actual capability: a
/// server started for a specific room (see NetworkManager.ExpectedRoomCode) rejects any joiner
/// that doesn't present the matching code, so a directory-scraped host id can't be joined
/// uninvited. The field is absent (3-part hello) for every codeless path, which keeps the LAN,
/// direct-connect, and CI flows byte-identical — a server with no expected code never checks it.
/// </summary>
public static class Handshake
{
    public const int MaxPayloadBytes = 64;
    private const string Magic = "MPF";

    public enum ServerStatus { Ok, Full }

    public static byte[] ClientHello(int protocol, string roomCode = "") =>
        Encode(string.IsNullOrEmpty(roomCode)
            ? $"{Magic};C;{protocol}"
            : $"{Magic};C;{protocol};{roomCode}");

    public static byte[] ServerHello(int protocol, ServerStatus status) =>
        Encode($"{Magic};S;{protocol};{(status == ServerStatus.Full ? "FULL" : "OK")}");

    public readonly record struct ClientInfo(int Protocol, string RoomCode);
    public readonly record struct ServerInfo(int Protocol, ServerStatus Status);

    /// <summary>Parses a client hello. Returns false (never throws) on anything malformed.
    /// Accepts the 3-part (no code) and 4-part (with code) shapes; the code, when present, must
    /// be a well-formed room code (RoomCode.IsValid) or the hello is rejected.</summary>
    public static bool TryParseClient(byte[] data, out ClientInfo info)
    {
        info = default;
        if (!TrySplit(data, out string[] parts) || parts.Length is not (3 or 4))
            return false;
        if (parts[0] != Magic || parts[1] != "C")
            return false;
        if (!TryProtocol(parts[2], out int protocol))
            return false;
        string roomCode = "";
        if (parts.Length == 4)
        {
            if (!RoomCode.IsValid(parts[3]))
                return false;
            roomCode = parts[3];
        }
        info = new ClientInfo(protocol, roomCode);
        return true;
    }

    /// <summary>Parses a server hello. Returns false (never throws) on anything malformed.</summary>
    public static bool TryParseServer(byte[] data, out ServerInfo info)
    {
        info = default;
        if (!TrySplit(data, out string[] parts) || parts.Length != 4)
            return false;
        if (parts[0] != Magic || parts[1] != "S")
            return false;
        if (!TryProtocol(parts[2], out int protocol))
            return false;
        ServerStatus status = parts[3] switch
        {
            "OK" => ServerStatus.Ok,
            "FULL" => ServerStatus.Full,
            _ => (ServerStatus)(-1),
        };
        if ((int)status < 0)
            return false;
        info = new ServerInfo(protocol, status);
        return true;
    }

    private static byte[] Encode(string text) => Encoding.ASCII.GetBytes(text);

    private static bool TrySplit(byte[] data, out string[] parts)
    {
        parts = System.Array.Empty<string>();
        if (data is null || data.Length == 0 || data.Length > MaxPayloadBytes)
            return false;
        // Reject non-ASCII / control bytes before decoding so garbage can't sneak through.
        foreach (byte b in data)
        {
            if (b < 0x20 || b > 0x7E)
                return false;
        }
        parts = Encoding.ASCII.GetString(data).Split(';');
        return true;
    }

    private static bool TryProtocol(string s, out int protocol) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out protocol);
}
