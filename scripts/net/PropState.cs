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
/// The authoritative state of one networked prop. A value type: the registry stores it, the
/// codec serialises it, remotes apply it. <see cref="Transform"/> is meaningful for Resting and
/// Loose; while Held it is derived from the holder, not from this field.
/// </summary>
public readonly record struct PropState(
    int Id,
    PropKind Kind,
    PropMode Mode,
    int HolderPeerId,
    Transform3D Transform)
{
    /// <summary>This prop, now held by <paramref name="holderPeerId"/>.</summary>
    public PropState AsHeld(int holderPeerId) =>
        this with { Mode = PropMode.Held, HolderPeerId = holderPeerId };

    /// <summary>This prop, released into motion at <paramref name="at"/> (no holder).</summary>
    public PropState AsLoose(Transform3D at) =>
        this with { Mode = PropMode.Loose, HolderPeerId = 0, Transform = at };

    /// <summary>This prop, latched to rest at <paramref name="at"/> (no holder).</summary>
    public PropState AsResting(Transform3D at) =>
        this with { Mode = PropMode.Resting, HolderPeerId = 0, Transform = at };
}
