using System.Collections.Generic;
using Godot;

namespace MpFoundation.Net;

/// <summary>
/// Server-authoritative store of every networked prop's current state — the single source of
/// truth an object's shared, persistent state rests on. Pure logic: no scene tree, no physics,
/// no networking, so it is testable headless (see PropRegistrySelfTest). Ids are assigned here
/// and stay stable for a prop's lifetime; every mutation enforces the state-machine rules
/// (first-grab-wins, legal mode transitions) so a malformed or hostile request can never
/// corrupt the store — it simply returns false. Enumerating <see cref="All"/> is how a late
/// joiner is brought fully up to date.
/// </summary>
public sealed class PropRegistry
{
    // 0 is reserved as "no prop" / "no holder", so ids start at 1.
    private readonly Dictionary<int, PropState> _props = new();
    private int _nextId = 1;

    /// <summary>Every prop's current state — the payload of the late-join dump.</summary>
    public IReadOnlyCollection<PropState> All => _props.Values;

    public int Count => _props.Count;

    /// <summary>Registers a new prop at rest and returns its stable id. Skips forward past any
    /// id already in the store (e.g. the authored-id range adopted via <see cref="RegisterAt"/>)
    /// instead of assuming a fixed boundary between the two ranges — this stays correct
    /// regardless of how many authored props a world adopts or what id base it uses, closing the
    /// class of bug where a long-running session's monotonic counter eventually crosses into
    /// authored-id territory and silently overwrites an authored prop's state.</summary>
    public int Register(PropKind kind, Transform3D at)
    {
        while (_props.ContainsKey(_nextId))
            _nextId++;
        int id = _nextId++;
        _props[id] = new PropState(id, kind, PropMode.Resting, 0, at);
        return id;
    }

    public bool TryGet(int id, out PropState state) => _props.TryGetValue(id, out state);

    /// <summary>Registers an ALREADY-IDENTIFIED prop at rest — adoption, not creation — used only
    /// for authored props (see PropManager.AdoptAuthoredProps), whose id space is assigned by the
    /// caller (a reserved high base). Returns false and leaves the store untouched if the id is
    /// already occupied (an authored-adoption collision, which should be structurally
    /// impossible) rather than silently overwriting whatever prop already held that id — a loud
    /// failure the caller can assert on beats undetected state corruption.</summary>
    public bool RegisterAt(int id, PropKind kind, Transform3D at)
    {
        if (_props.ContainsKey(id))
            return false;
        _props[id] = new PropState(id, kind, PropMode.Resting, 0, at);
        return true;
    }

    /// <summary>
    /// First-grab-wins: assigns the holder only if the prop is not already held by a
    /// <em>different</em> peer (re-asserting the same holder is idempotent). Returns false on
    /// an unknown id, a contested grab, or a non-positive <paramref name="peerId"/> — 0 is the
    /// reserved "no holder" sentinel (e.g. a locally-invoked RPC's <c>GetRemoteSenderId()</c>),
    /// so accepting it here would latch the prop as permanently held by nobody, soft-locking it
    /// forever. The server arbitrates every pickup through here.
    /// </summary>
    public bool SetHolder(int id, int peerId)
    {
        if (peerId <= 0)
            return false;
        if (!_props.TryGetValue(id, out PropState s))
            return false;
        if (s.Mode == PropMode.Held && s.HolderPeerId != peerId)
            return false;
        _props[id] = s.AsHeld(peerId);
        return true;
    }

    /// <summary>Releases a held prop into <see cref="PropMode.Loose"/> at <paramref name="at"/>
    /// (both a drop and a throw go through here; the impulse is applied by the caller). Returns
    /// false unless the prop is currently held.</summary>
    public bool Release(int id, Transform3D at)
    {
        if (!_props.TryGetValue(id, out PropState s) || s.Mode != PropMode.Held)
            return false;
        _props[id] = s.AsLoose(at);
        return true;
    }

    /// <summary>Updates a Loose prop's streamed transform. Returns false unless the prop is Loose,
    /// so a resting or held prop can never be moved by a stray transform update.</summary>
    public bool SetLooseTransform(int id, Transform3D at)
    {
        if (!_props.TryGetValue(id, out PropState s) || s.Mode != PropMode.Loose)
            return false;
        _props[id] = s with { Transform = at };
        return true;
    }

    /// <summary>Latches a prop to <see cref="PropMode.Resting"/> at <paramref name="at"/> (e.g. a
    /// Loose prop that went to sleep). Legal from any mode. Returns false on an unknown id.</summary>
    public bool SetResting(int id, Transform3D at)
    {
        if (!_props.TryGetValue(id, out PropState s))
            return false;
        _props[id] = s.AsResting(at);
        return true;
    }

    /// <summary>Removes a prop from the store entirely — the coin economy's permanent
    /// loss (down a drain) and permanent consumption (spent at the Gachapon/payphone).
    /// Legal from any mode: a held coin is consumed by inserting it, a loose one by the
    /// drain. Returns false on an unknown id. The id is never reused (ids are
    /// monotonic), so a straggler packet about a removed prop can never alias onto a
    /// new one.</summary>
    public bool Remove(int id) => _props.Remove(id);

    /// <summary>Latches every prop currently held by <paramref name="peerId"/> to Resting at
    /// <paramref name="at"/> (a disconnecting holder drops everything it carried). Returns the
    /// number released. peerId 0 is the "no holder" sentinel and releases nothing.</summary>
    public int ReleaseAllHeldBy(int peerId, Transform3D at)
    {
        if (peerId <= 0)
            return 0;
        int n = 0;
        foreach (int id in new List<int>(_props.Keys))
        {
            PropState s = _props[id];
            if (s.Mode == PropMode.Held && s.HolderPeerId == peerId)
            {
                _props[id] = s.AsResting(at);
                n++;
            }
        }
        return n;
    }
}
