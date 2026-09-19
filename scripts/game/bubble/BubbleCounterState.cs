using System;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The shared bubble tally, as pure data.</b> A fixed-capacity bitset of "this bubble has
/// been popped" plus the count that is derived from it — no Godot types, no nodes, no network,
/// so every rule that actually decides whether the counter is correct
/// (idempotency, double-pop, out-of-range ids, encode/decode round-trip, reset) is reachable
/// from <c>dotnet test</c> without an engine. <see cref="BubbleCounter"/> is the thin Godot
/// wrapper that owns one of these on the server and one on every client.
///
/// <para><b>Why a bitset and not a HashSet.</b> The late-join message (BT-6 scope item 4) has to
/// carry the whole popped set to a peer that joined at minute 40, and it has to do it in one
/// reliable RPC. 512 bubbles is 64 bytes as a bitset and up to 2 KB as an int array; the level
/// only asks for ~100 (program D8), so <see cref="Capacity"/> is deliberately far above the
/// requirement and still smaller on the wire than the "obvious" encoding. It is also what makes
/// <see cref="Encode"/> allocation-stable: the message is the same 64 bytes at 0 popped and at
/// 100 popped, so nothing about the traffic pattern leaks progress.</para>
///
/// <para><b>Count is not independent state.</b> It is always the popcount of the bitset, and
/// <see cref="Decode"/> refuses a payload where the two disagree rather than silently adopting a
/// count the bits do not support — MECHANICS-BIBLE §1 (a state machine may not hold two sources
/// of truth for one fact). The count still travels on the wire because the receiving peer needs
/// something to check the bits against, not because it is a second authority.</para>
/// </summary>
public sealed class BubbleCounterState
{
    /// <summary>Hard ceiling on bubble ids, and the reason <see cref="BubbleCounter"/> asserts
    /// N &lt;= this when it adopts a level's bubbles (scope item 2). One bitset, one message,
    /// one reliable send — a level that wants more than 512 bubbles needs a different transport
    /// decision made deliberately, not a silently truncated sync.</summary>
    public const int Capacity = 512;

    /// <summary>Size of <see cref="Encode"/>'s payload, in bytes. Stated as a constant so the
    /// test suite asserts the wire size rather than re-deriving it.</summary>
    public const int EncodedBytes = Capacity / 8;

    private readonly byte[] _bits = new byte[EncodedBytes];

    /// <summary>How many distinct bubbles have been popped. Always equals the popcount of the
    /// bitset — see the class doc.</summary>
    public int Count { get; private set; }

    /// <summary>True when <paramref name="id"/> is a usable bubble id. Out-of-range ids are a
    /// legitimate runtime input (an unassigned bubble carries −1 until
    /// <see cref="BubbleCounter"/> adopts it), so they are refused, never thrown on.</summary>
    public static bool IsValidId(int id) => id >= 0 && id < Capacity;

    /// <summary>Has this bubble already been popped. False for any invalid id.</summary>
    public bool IsPopped(int id) => IsValidId(id) && (_bits[id >> 3] & (1 << (id & 7))) != 0;

    /// <summary>
    /// Flips <paramref name="id"/> to popped and increments <see cref="Count"/>, returning
    /// whether this call is the one that did it.
    ///
    /// <para><b>This is the whole idempotency contract.</b> Two players' bodies entering one
    /// bubble on the same server tick produce two calls; the second returns false and changes
    /// nothing, so the server broadcasts exactly once and the tally moves by exactly one
    /// (acceptance criteria 2 and 4). The same is true of a duplicate delivery, a re-entered
    /// area after a physics hitch, and a pop replayed on top of a late-join sync — all of them
    /// are the same "already true" case, and none of them is a special case in the caller.</para>
    /// </summary>
    public bool TryPop(int id)
    {
        if (!IsValidId(id) || IsPopped(id))
            return false;
        _bits[id >> 3] |= (byte)(1 << (id & 7));
        Count++;
        return true;
    }

    /// <summary>Back to "nothing popped" — the pedestal lever (program D9, wired by BT-8) and
    /// the playthrough boundary. Idempotent: resetting a cleared state is a no-op.</summary>
    public void Reset()
    {
        Array.Clear(_bits, 0, _bits.Length);
        Count = 0;
    }

    /// <summary>The popped set as <see cref="EncodedBytes"/> bytes, for the late-join push. A
    /// fresh copy every call — the caller hands this straight to an RPC, and handing out the
    /// live array would let a queued message observe pops that happened after it was built.</summary>
    public byte[] Encode()
    {
        var copy = new byte[EncodedBytes];
        Array.Copy(_bits, copy, EncodedBytes);
        return copy;
    }

    /// <summary>
    /// Adopts a server-sent snapshot wholesale, replacing whatever this peer believed.
    /// Returns false — and changes nothing — for a payload of the wrong length or one whose
    /// stated <paramref name="count"/> disagrees with its own bits.
    ///
    /// <para>The cross-check is the point. A client that accepted a mismatched pair would carry
    /// a tally its own popped set cannot explain, and the symptom (a HUD reading 38 while 37
    /// bubbles are missing) is exactly the kind of divergence that reads as "the netcode is
    /// flaky" rather than as a bug with a location.</para>
    /// </summary>
    public bool Decode(byte[]? bitset, int count)
    {
        if (bitset == null || bitset.Length != EncodedBytes)
            return false;
        int popcount = 0;
        for (int i = 0; i < EncodedBytes; i++)
            popcount += System.Numerics.BitOperations.PopCount(bitset[i]);
        if (popcount != count)
            return false;
        Array.Copy(bitset, _bits, EncodedBytes);
        Count = count;
        return true;
    }

    /// <summary>Every popped id, ascending. Diagnostics and test instrumentation only — the
    /// gameplay paths all read <see cref="IsPopped"/> or <see cref="Count"/>.</summary>
    public System.Collections.Generic.List<int> PoppedIds()
    {
        var ids = new System.Collections.Generic.List<int>(Count);
        for (int id = 0; id < Capacity; id++)
        {
            if (IsPopped(id))
                ids.Add(id);
        }
        return ids;
    }
}
