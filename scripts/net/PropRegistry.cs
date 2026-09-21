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

    /// <summary><b>The same collection, typed concretely so a per-tick <c>foreach</c> uses the
    /// dictionary's STRUCT enumerator</b> (PROBE-1, 2026-09-20). <see cref="All"/> is an
    /// interface, so enumerating it boxes an enumerator on the heap and copies every 64-byte
    /// <see cref="PropState"/> through it — once per prop per physics tick in the server's loop,
    /// which at two thousand props is 120 000 struct copies and 60 heap allocations a second for
    /// an answer that is almost always "nothing is loose". Late-join dumps and other one-shot
    /// walks keep using <see cref="All"/>; the hot paths use this.</summary>
    public Dictionary<int, PropState>.ValueCollection AllValues => _props.Values;

    public int Count => _props.Count;

    /// <summary>
    /// <b>How many props are currently <see cref="PropMode.Loose"/></b> — maintained here, on
    /// every transition, rather than counted by the caller (PROBE-1, 2026-09-20).
    ///
    /// <para><b>Why it is worth a field.</b> <c>PropManager._PhysicsProcess</c>'s first act is to
    /// find out whether anything is loose, and its own comment says the common case is that
    /// nothing is. It was answering that by walking every prop in the world, sixty times a
    /// second — O(props) work to discover there was no work. A room is asleep for almost all of a
    /// round, so almost all of that walk was the whole cost. Kept in the store because the store
    /// is where every mode transition already funnels; a count maintained anywhere else is a
    /// count that can drift from the thing it counts.</para>
    ///
    /// <para>Pinned by <c>PropRegistryLooseCountTests</c> against the honest answer (a filter over
    /// <see cref="All"/>) after every verb, including the ones that are refused.</para>
    /// </summary>
    public int LooseCount { get; private set; }

    /// <summary>The one place the count changes: called with the mode a slot held before a write
    /// and the mode it holds after. Every mutator below routes through it, so adding a verb
    /// without adding a line here is a compile-time-visible omission rather than a slow drift.</summary>
    private void Recount(PropMode before, PropMode after)
    {
        if (before == after)
            return;
        if (before == PropMode.Loose)
            LooseCount--;
        if (after == PropMode.Loose)
            LooseCount++;
    }

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
        Recount(s.Mode, PropMode.Held);
        _props[id] = s.AsHeld(peerId);
        return true;
    }

    /// <summary>Releases a held prop into <see cref="PropMode.Loose"/> at <paramref name="at"/>
    /// (drop, throw and place all go through here; the impulse is applied by the caller).
    /// Returns false unless the prop is currently held.
    ///
    /// <para><paramref name="release"/> records WHICH verb did it (SFX-2). Defaulted, so a
    /// caller with no verb to report - a fixture, a test - stores
    /// <see cref="PropRelease.None"/> rather than guessing at one. The stored value is the
    /// registry's answer to "how did this come to be loose"; what goes on the wire is chosen by
    /// the broadcast, and the late-join dump deliberately sends None whatever this says (see
    /// <c>PropManager.SendDumpTo</c>).</para></summary>
    public bool Release(int id, Transform3D at, PropRelease release = PropRelease.None)
    {
        if (!_props.TryGetValue(id, out PropState s) || s.Mode != PropMode.Held)
            return false;
        Recount(s.Mode, PropMode.Loose);
        _props[id] = s.AsLoose(at, release);
        return true;
    }

    /// <summary>
    /// <b>Wakes a resting prop into <see cref="PropMode.Loose"/> at <paramref name="at"/></b> —
    /// the transition an EXTERNAL force makes, as opposed to a holder letting go
    /// (<see cref="Release"/>) or a loose prop going to sleep (<see cref="SetResting"/>). The
    /// impulse is the caller's business, exactly as it is for <see cref="Release"/>.
    ///
    /// <para><b>Why this had to exist</b> (DOOR-1, 2026-09-19, and it was found by a suite rather
    /// than by review). Until the burst door there was no way for anything but a player to move a
    /// prop, so the store had no Resting -> Loose edge at all and <see cref="Release"/> is
    /// explicitly guarded to refuse one. The door's first implementation called
    /// <see cref="Release"/> on a Resting crate: it returned false, the store stayed Resting, the
    /// server's per-tick loop streams only Loose props — and so the server's own rigid body was
    /// unfrozen and shoved across the room while every client's copy sat exactly where it was,
    /// forever. The server's log said "shoved 3 prop(s)" and it was telling the truth.
    /// <c>tests/Run-BurstDoorTest.ps1</c> measures the prop's movement on a CLIENT's log for
    /// precisely this reason.</para>
    ///
    /// <para><b>Idempotent from Loose, refused from Held.</b> Waking something already awake is a
    /// second blast reaching the same tumbling crate, which is fine and should not need the
    /// caller to branch. Waking something in a hand is not: it would take the prop off a player
    /// without any of the release funnel's broadcasts, so the hand would keep claiming it on
    /// every peer. A caller that means to empty a hand has <see cref="Release"/>.</para>
    ///
    /// <para><b>Not <see cref="SetLoose"/>, and the difference is the Held guard</b> (INT-0B,
    /// 2026-09-19, where the two verbs met). <see cref="SetLoose"/> is REACH-1's dev nudge and
    /// takes a prop loose from ANY mode including Held; this one is the shipped external-force
    /// edge and refuses a hand. A caller in the GAME wants this one.</para>
    /// </summary>
    public bool Wake(int id, Transform3D at)
    {
        if (!_props.TryGetValue(id, out PropState s) || s.Mode == PropMode.Held)
            return false;
        Recount(s.Mode, PropMode.Loose);
        _props[id] = s.AsLoose(at);
        return true;
    }

    /// <summary>
    /// Puts a prop into <see cref="PropMode.Loose"/> at <paramref name="at"/> from ANY mode —
    /// the sibling of <see cref="SetResting"/> in the other direction. Returns false on an
    /// unknown id.
    ///
    /// <para><b>Why it exists next to <see cref="Release"/>, which looks like the same thing.</b>
    /// <see cref="Release"/> is the drop/throw verb and deliberately refuses anything that is not
    /// HELD: a resting prop must not be made loose by a stray release packet. REACH-1's
    /// <c>PropManager.ServerNudgeLoose</c> needs exactly what Release refuses — a RESTING prop
    /// shoved back into physics with nobody holding it — because §5b's sixth planted case is a
    /// prop "pushed through the floor by a scripted impulse" in a room with no players in it, and
    /// every shipped route into Loose starts from a hand. Keeping the two verbs separate is what
    /// stops the dev hook from loosening the rule the real one enforces.</para>
    ///
    /// <para><b>Not <see cref="Wake"/></b> (INT-0B, 2026-09-19): DOOR-1's burst shove landed the
    /// same Resting -> Loose edge for the game, and it REFUSES a Held prop. This one does not,
    /// because a planted-room nudge is allowed to be blunter than anything a player can cause.
    /// Two verbs on purpose; do not collapse them.</para>
    /// </summary>
    public bool SetLoose(int id, Transform3D at)
    {
        if (!_props.TryGetValue(id, out PropState s))
            return false;
        Recount(s.Mode, PropMode.Loose);
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
        Recount(s.Mode, PropMode.Resting);
        _props[id] = s.AsResting(at);
        return true;
    }

    /// <summary>Removes a prop from the store entirely — the coin economy's permanent
    /// loss (down a drain) and permanent consumption (spent at the Gachapon/payphone).
    /// Legal from any mode: a held coin is consumed by inserting it, a loose one by the
    /// drain. Returns false on an unknown id. The id is never reused (ids are
    /// monotonic), so a straggler packet about a removed prop can never alias onto a
    /// new one.</summary>
    public bool Remove(int id)
    {
        if (!_props.TryGetValue(id, out PropState s))
            return false;
        Recount(s.Mode, PropMode.Resting);
        return _props.Remove(id);
    }

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
                Recount(s.Mode, PropMode.Resting);
                _props[id] = s.AsResting(at);
                n++;
            }
        }
        return n;
    }
}
