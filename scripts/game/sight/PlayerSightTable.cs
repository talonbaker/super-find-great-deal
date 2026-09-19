using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sight;

/// <summary>
/// Who can see how far, right now — the replicated table itself, as a plain C# object with no scene
/// tree and no network. <see cref="PlayerSightService"/> is the Godot node that owns one of these,
/// fills it on the server and applies the wire messages into it on a client; this holds what the
/// table <i>is</i> and every rule about reading a value that is not in it.
///
/// <para><b>The split is the pure-store / Godot-node split every replicated store in this repo
/// draws, and it is here for the same reason.</b> The interesting failures of a replicated table
/// are not in the RPC plumbing —
/// they are "what does an unknown peer read", "does a stale packet clobber a fresh one", "does a
/// peer who left linger forever", and "what happens to a NaN off the wire". Every one of those is
/// answered here, so every one of them is provable by <c>dotnet test</c> instead of by joining a
/// session and watching.</para>
///
/// <para><b>Every unknown reads <see cref="PlayerSightCurve.DarkFloorM"/>.</b> Not zero, not the
/// maximum, not the last value that peer had. See <see cref="RangeFor"/>.</para>
/// </summary>
public sealed class PlayerSightTable
{
    private readonly Dictionary<int, float> _ranges = new();
    private uint _lastAppliedSeq;

    /// <summary>False until this table holds authoritative data. The server marks itself synced at
    /// setup (it is its own authority); a client becomes synced on its first applied message. The
    /// contract for consumers is <see cref="World.CycleDriver.Synced"/>'s, verbatim: do not present
    /// anything derived from an unsynced table.</summary>
    public bool Synced { get; private set; }

    /// <summary>How many peers are in the table.</summary>
    public int Count => _ranges.Count;

    /// <summary>Every entry, for the wire payload builder and for tests. Enumeration order is a
    /// dictionary's and is deliberately not relied on: the payload is a set of (peer, range) pairs
    /// and nothing downstream reads it positionally.</summary>
    public IReadOnlyDictionary<int, float> All => _ranges;

    /// <summary><b>How far this peer can see, metres — and the floor for every way of not
    /// knowing.</b> An unsynced table, an unknown peer id, a peer that disconnected this frame, a
    /// table that was never filled: each returns <see cref="PlayerSightCurve.DarkFloorM"/>.
    ///
    /// <para>The direction is the whole point. The other one has already cost this repo once: a
    /// light radius that silently degraded to its default inverted a
    /// gameplay rule for an entire session, merged cleanly, and passed a green suite because nothing
    /// asserted the sign. "I do not know how far this player can see" must never resolve to "they can
    /// see everything", and here it cannot, because there is no branch that returns a maximum.</para></summary>
    public float RangeFor(int peerId) =>
        Synced && _ranges.TryGetValue(peerId, out float m) ? m : PlayerSightCurve.DarkFloorM;

    /// <summary>Server-only: this table is authoritative from now on.</summary>
    public void MarkSynced() => Synced = true;

    /// <summary>Server-only: begin a fresh recompute. <b>The table is rebuilt every tick rather than
    /// mutated</b>, so a peer who left cannot linger as a stale entry and no removal hook can be
    /// forgotten — the absence of a peer from the server's own avatar enumeration is itself the removal.</summary>
    public void ServerBeginFrame() => _ranges.Clear();

    /// <summary>Server-only: record one peer's computed range. Sanitised in the blind direction — a
    /// NaN lands on the floor, and the value is bounded by
    /// [<see cref="PlayerSightCurve.DarkFloorM"/>, <see cref="PlayerSightCurve.DaylightSightM"/>], the
    /// widest range the model can legitimately produce.</summary>
    public void ServerSet(int peerId, float rangeM) => _ranges[peerId] = Sanitise(rangeM);

    /// <summary>Every non-server peer: adopt an authoritative table. Returns false if the message
    /// was refused, so a caller (and a test) can assert the guard rather than infer it.
    ///
    /// <para><b>Latest-wins</b> (<c>SandboxAvatar.ApplySnapshot</c>'s and
    /// <c>CycleDriver.Apply</c>'s exact reasoning): the periodic broadcast is unreliable and
    /// may reorder, so a superseded table must never clobber a newer one that already landed.
    /// <paramref name="isSync"/> — the reliable late-join/reconnect delivery — always wins outright,
    /// for <see cref="World.CycleDriver"/>'s stated reason: a reconnecting peer is a brand-new peer id
    /// server-side but the same node instance client-side, so the sequence it remembers is stale
    /// history from the previous connection rather than an ordering claim against the resumed
    /// session.</para>
    ///
    /// <para><b>Wholesale replacement, never a merge.</b> A merge would leave a departed peer's last
    /// range in every client's table forever — and because the only consumer of a stale entry is
    /// something asking "how far can that player see", the stale answer would be a live one.</para></summary>
    public bool Apply(IReadOnlyList<int>? peerIds, IReadOnlyList<float>? rangesM, uint seq, bool isSync)
    {
        if (peerIds == null || rangesM == null || peerIds.Count != rangesM.Count)
            return false;
        if (!isSync && Synced && seq <= _lastAppliedSeq)
            return false;
        _lastAppliedSeq = seq;
        _ranges.Clear();
        for (int i = 0; i < peerIds.Count; i++)
            _ranges[peerIds[i]] = Sanitise(rangesM[i]);
        Synced = true;
        return true;
    }

    /// <summary>Back to nothing known — the state a fresh table and a torn-down session both hold.
    /// Unsynced, so every read is the floor.</summary>
    public void Reset()
    {
        _ranges.Clear();
        _lastAppliedSeq = 0;
        Synced = false;
    }

    private static float Sanitise(float rangeM) => float.IsNaN(rangeM)
        ? PlayerSightCurve.DarkFloorM
        : Mathf.Clamp(rangeM, PlayerSightCurve.DarkFloorM, PlayerSightCurve.DaylightSightM);
}
