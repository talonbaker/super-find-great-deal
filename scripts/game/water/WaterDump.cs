using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Sail.Game.Water;

/// <summary>
/// The late-join wire format for water state. A peer joining a session where somebody is
/// already halfway across the lake must be told that before its world is presented as
/// "playing" — otherwise it renders a player standing bolt upright in deep water, and W4's
/// listener never learns that a swim is already in progress. Same reasoning and the same
/// <c>Gameplay.OnPeerConnected</c> call site as every other dump in this codebase
/// (<c>PropManager.SendDumpTo</c>, <c>CycleDriver.SendPhaseTo</c>).
///
/// Kept as a pure static codec with no Godot node dependency so the round trip is xUnit-testable
/// with no engine running — the same posture <c>NetCodec</c> itself takes, and the reason
/// <c>WaterDumpTests</c> can fuzz it.
///
/// <b>Defensive by contract.</b> Every parse path returns an empty list rather than throwing:
/// a malformed packet from a hostile or mismatched client must never raise inside a Godot C#
/// network callback, which swallows the rest of that callback's work.
/// </summary>
public static class WaterDump
{
    /// <summary>peerId(4) + state(1) + chill(4) + flags(1) + phase(1).</summary>
    private const int EntryBytes = 11;

    /// <summary>Bound on entries in one dump. A session is 2–6 players by design; 64 is a
    /// generous ceiling that still makes a garbage length header cheap to reject.</summary>
    public const int MaxEntries = 64;

    private const byte FlagSoaked = 1;

    public static byte[] Pack(IReadOnlyList<WaterPeerSnapshot> entries)
    {
        int count = entries.Count > MaxEntries ? MaxEntries : entries.Count;
        var buf = new byte[1 + count * EntryBytes];
        var span = buf.AsSpan();
        span[0] = (byte)count;
        int o = 1;
        for (int i = 0; i < count; i++)
        {
            WaterPeerSnapshot e = entries[i].Sanitized();
            BinaryPrimitives.WriteInt32LittleEndian(span[o..], e.PeerId); o += 4;
            span[o++] = (byte)e.State;
            BinaryPrimitives.WriteSingleLittleEndian(span[o..], e.Chill); o += 4;
            span[o++] = (byte)(e.Soaked ? FlagSoaked : 0);
            span[o++] = (byte)e.Phase;
        }
        return buf;
    }

    public static List<WaterPeerSnapshot> Unpack(byte[]? packet)
    {
        var result = new List<WaterPeerSnapshot>();
        if (packet == null || packet.Length < 1)
            return result;
        int count = packet[0];
        if (count > MaxEntries || packet.Length != 1 + count * EntryBytes)
            return result;

        var span = packet.AsSpan();
        int o = 1;
        for (int i = 0; i < count; i++)
        {
            int peerId = BinaryPrimitives.ReadInt32LittleEndian(span[o..]); o += 4;
            byte state = span[o++];
            float chill = BinaryPrimitives.ReadSingleLittleEndian(span[o..]); o += 4;
            byte flags = span[o++];
            byte phase = span[o++];
            // Sanitized on the way in, not trusted: the enum casts below can produce
            // out-of-range values from a doctored packet, and Sanitized() folds those to
            // Dry/None rather than letting a switch fall through to an undefined branch.
            result.Add(new WaterPeerSnapshot(
                peerId,
                (WaterState)state,
                chill,
                (flags & FlagSoaked) != 0,
                (SputterPhase)phase).Sanitized());
        }
        return result;
    }
}
