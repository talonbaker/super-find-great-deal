using Godot;

namespace MpFoundation.Net;

/// <summary>
/// What kind of physical object a networked prop is. Determines how each peer instantiates
/// it and how it behaves; the server stores it so a late joiner can rebuild the exact prop.
/// The foundation ships the two generic Carryable shapes; extend as new prop types are
/// networked (append-only — the enum's ints ride the wire in the spawner's data array and in
/// <see cref="PropState"/>, so reordering would silently turn every existing crate into a ball
/// on a mixed-version session).
/// </summary>
public enum PropKind
{
    Crate,
    Ball,

    // --- The supermarket's product shapes (SFX-1, 2026-09-19) --------------------------------
    //
    // APPENDED, and the ordinals are explicit so the append is visible in a diff. These ride the
    // wire in the spawner's data array and in PropState, so inserting one anywhere above would
    // turn every existing crate in a mixed-version session into something else.
    //
    // PROTOCOL BUMPED to v16 for exactly this append (NetProfile.ProtocolVersion). Nothing in
    // NetCodec HASHES this enum — it writes the int and NetworkedProp casts it back — but
    // NetProfile's own v8 entry already settled what that means: "A PropKind ordinal is a data-
    // shape change of the worst kind across builds: a stale client receiving an unknown kind
    // falls through NetworkedProp.Init's switch to the Crate default and renders the wrong
    // object instead of failing loudly, which is exactly what this field exists to convert into
    // a refused connection." A v15 peer handed Kind = 4 would show a wooden crate where the
    // other player sees an orange, and would sound like one; that is the silently-wrong failure,
    // not a cosmetic gap.

    /// <summary>A can: tin, cylinder, 0.35 kg. See <c>Carryable.CanRadiusM</c>.</summary>
    Can = 2,

    /// <summary>A cereal box: cardboard, a NON-cubic box, 0.4 kg. The non-cubeness is what tells
    /// it apart from <see cref="Crate"/> at adoption — see <c>Carryable.ShapeFromCollider</c>.</summary>
    Box = 3,

    /// <summary>Fruit or veg: soft, a small sphere, 0.25 kg.</summary>
    Produce = 4,
}

/// <summary>
/// How a networked prop's transform is currently determined — which decides its replication
/// class and cost. <see cref="Resting"/> and <see cref="Held"/> are cheap discrete facts;
/// <see cref="Loose"/> streams a transform while it is in motion, then latches back to Resting.
/// </summary>
public enum PropMode
{
    /// <summary>Latched static fact at <see cref="PropState.Transform"/>; costs nothing until it changes.</summary>
    Resting,

    /// <summary>Owned by <see cref="PropState.HolderPeerId"/>; transform derived from the holder's hand anchor, not streamed.</summary>
    Held,

    /// <summary>In motion; the server simulates it and streams <see cref="PropState.Transform"/> until it sleeps.</summary>
    Loose,
}

/// <summary>
/// <b>WHY a prop just left a hand</b> (SFX-2, 2026-09-19) — the one byte that tells a peer which
/// verb produced the Held -> Loose transition it is applying.
///
/// <para><b>The gap this closes.</b> CARRY-1 split place from drop in the gameplay layer and its
/// own handoff recorded what that left behind: <i>"at ApplyPropState a place and a drop are the
/// same Held->Loose transition and are indistinguishable there"</i>. So every release replicated
/// as the same event, and SFX-1 could only answer it by playing the shared <c>Whoosh</c> — a
/// sound about the ARM rather than about the object — for a careful set-down as well as for a
/// throw. A remote peer never heard a can's rim tick, ever, because the verb did not survive the
/// wire.</para>
///
/// <para><b><see cref="None"/> is not "unknown", it is "nothing to announce".</b> A late-join
/// snapshot describes a STATE, not an event: the prop is loose because it was loose before this
/// peer connected, and replaying the throw that started it minutes ago would be a sound with no
/// cause. The same is true of the round's reset edge, which returns every prop to its authored
/// transform — <c>HideSeekDriver</c>'s reset edge is a world rearrangement, not forty people
/// dropping things, and it must stay <see cref="None"/>.</para>
///
/// <para>Ordinals ride the wire — APPEND ONLY. An ordinal a peer does not recognise decodes to
/// <see cref="None"/> (see <see cref="PropReleaseWire.Decode"/>), which is silence rather than
/// the wrong sound: the one direction an unknown value may fail in.</para>
/// </summary>
public enum PropRelease : byte
{
    /// <summary>Nothing to play. A late-join snapshot, a settle latch, the round reset, and every
    /// transition that is not a release at all.</summary>
    None = 0,

    /// <summary>A plain let-go, and the forced release of anything a burst tears out of a hand
    /// (<c>PropManager.ScatterHeldBy</c>, DOOR-1's door). The object is being got rid of.</summary>
    Dropped = 1,

    /// <summary>CARRY-1's place verb (<c>PropManager.RequestPlace</c>): set down deliberately, at
    /// an orientation the player lined up. The material's settle tick.</summary>
    Placed = 2,

    /// <summary>Thrown along the holder's aim (<c>PropManager.RequestThrow</c>).</summary>
    Thrown = 3,
}

/// <summary>The <see cref="PropRelease"/> byte's wire form, as arithmetic a Godot-free test can
/// reach. <see cref="Decode"/> is the half that earns its keep: Godot RPC arguments are ints, so
/// the value arriving from another build is an arbitrary int, and an ordinal this build has no
/// member for must become <see cref="PropRelease.None"/> — silence — rather than being cast
/// blindly into a member that happens to share its bits.</summary>
public static class PropReleaseWire
{
    /// <summary>The byte to put on the wire.</summary>
    public static int Encode(PropRelease release) => (int)release;

    /// <summary>What an arriving int means here. Anything outside the defined ordinals — a
    /// negative, a future build's fifth verb, a doctored client's 9999 — is
    /// <see cref="PropRelease.None"/>.</summary>
    public static PropRelease Decode(int wire) => wire switch
    {
        (int)PropRelease.Dropped => PropRelease.Dropped,
        (int)PropRelease.Placed => PropRelease.Placed,
        (int)PropRelease.Thrown => PropRelease.Thrown,
        _ => PropRelease.None,
    };
}

/// <summary>
/// The authoritative state of one networked prop. A value type: the registry stores it, the
/// codec serialises it, remotes apply it. <see cref="Transform"/> is meaningful for Resting and
/// Loose; while Held it is derived from the holder, not from this field.
/// </summary>
public readonly record struct PropState(
    int Id,
    PropKind Kind,
    PropMode Mode,
    int HolderPeerId,
    Transform3D Transform,
    PropRelease Release = PropRelease.None)
{
    /// <summary>This prop, now held by <paramref name="holderPeerId"/>. Clears
    /// <see cref="Release"/>: a prop in a hand is not mid-release.</summary>
    public PropState AsHeld(int holderPeerId) =>
        this with { Mode = PropMode.Held, HolderPeerId = holderPeerId, Release = PropRelease.None };

    /// <summary>This prop, released into motion at <paramref name="at"/> (no holder), by
    /// <paramref name="release"/>. The default is <see cref="PropRelease.None"/> so a caller
    /// that has no verb to report — a seeded fixture fall, a test — announces nothing rather
    /// than guessing.</summary>
    public PropState AsLoose(Transform3D at, PropRelease release = PropRelease.None) =>
        this with { Mode = PropMode.Loose, HolderPeerId = 0, Transform = at, Release = release };

    /// <summary>This prop, latched to rest at <paramref name="at"/> (no holder). Clears
    /// <see cref="Release"/>: settling is the END of a release, and a Resting transition that
    /// still carried the verb would play the throw a second time when the can stopped rolling.
    /// </summary>
    public PropState AsResting(Transform3D at) =>
        this with { Mode = PropMode.Resting, HolderPeerId = 0, Transform = at, Release = PropRelease.None };
}
