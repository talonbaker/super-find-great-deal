using Godot;

namespace MpFoundation.Game.Watcher;

/// <summary>Which half of the session this <see cref="Watcher"/> instance is.</summary>
public enum WatcherNetRole
{
    /// <summary>No session (the dev labs, a solo boot with no peer). The brain runs locally and
    /// nothing is ever sent — <b>byte-for-byte the behaviour this feature had before it was
    /// networked</b>, which is what keeps <c>WatcherLab</c> and every existing brain test on the
    /// same code path they were written against.</summary>
    Offline = 0,
    /// <summary>The authority. The brain runs here and <b>only</b> here; every other peer is told
    /// what it decided.</summary>
    Server = 1,
    /// <summary>A non-authoritative peer. Never constructs a decision — it renders the one the
    /// server sent.</summary>
    Client = 2,
}

/// <summary>
/// A peer's converged view of the watcher: everything about the creature that must be the same
/// number on every machine, plus the ordering rules that keep it that way. Pure — no Node, no
/// scene tree, no RPC — so the reconciliation is provable by `dotnet test` and the Godot layer
/// (<see cref="Watcher"/>) is only the wire.
///
/// <para><b>Why this type exists at all.</b> Before 2026-08-13 the watcher had no networking
/// whatsoever: <c>Gameplay.SetUpWatcher</c> ran on every peer, so every peer ran its own
/// <see cref="WatcherBrain"/> against its own view of the world and picked its own stand position
/// from its own private seed. Two players in one co-op session were looking at two different
/// creatures in two different places, and one could be watched by something the other could walk
/// through. The brain is a pure function of its inputs, but its inputs are not identical across
/// peers (interpolated remote avatars, per-peer frame deltas, a per-instance placement seed), so
/// "it will converge anyway" was never true. Canon fact 4's parity law — gameplay state is server
/// data, identical on every client — makes that a correctness bug rather than a polish item.</para>
///
/// <para><b>The three ordering guards, and why each one is load-bearing.</b> The facing stream is
/// unreliable (dropping one costs a tenth of a second of stale head angle and nothing else) while
/// appear/gone are reliable. Godot/ENet orders reliable traffic <i>within</i> a channel and gives
/// no ordering at all between a reliable and an unreliable packet, so a facing message genuinely
/// can land after the sighting it belongs to has ended:
/// <list type="number">
/// <item><see cref="ApplyFacing"/> refuses any id that is not the live sighting's, so a late
/// facing packet can never turn a departed creature's head — or, worse, re-point the head of the
/// <i>next</i> sighting to where the last one was looking.</item>
/// <item><see cref="ApplyAppear"/> refuses an id it has already seen, so the late-join dump and a
/// broadcast that raced it are idempotent rather than a double-appear.</item>
/// <item><see cref="ApplyGone"/> refuses an id that is not live, so a duplicate or reordered
/// departure cannot cancel a sighting that started after it.</item>
/// </list>
/// This is the same discipline <c>SnapshotBuffer</c>'s reordered-stale-snapshot guard applies to
/// the avatar stream; it is restated here because a watcher has no continuous stream to correct
/// itself from — one lost or misapplied discrete message is wrong until the next sighting.</para>
///
/// <para><b><see cref="Synced"/> is not decoration.</b> A client that has never received a dump
/// reads <see cref="Present"/> false — which is a real, plausible-looking answer ("nothing is out
/// there tonight") and not an obviously-fake sentinel. That is the exact failure shape
/// <c>CycleDriver.Synced</c>, <c>RunDriver.Synced</c>, <c>WalletManager.Synced</c> and
/// <c>IGlowStickView.Synced</c> all exist to prevent, and a caller that cannot tell "no creature"
/// from "not told yet" will eventually assert on the difference.</para>
/// </summary>
public sealed class WatcherNetState
{
    /// <summary>Is a creature standing out there right now, as this peer has been told.</summary>
    public bool Present { get; private set; }

    /// <summary>Monotonic id of the current (or most recent) sighting. 0 = none ever. Server-issued
    /// and never reused, which is what makes the three guards above expressible as comparisons
    /// rather than as guesses about timing.</summary>
    public int SightingId { get; private set; }

    /// <summary>Where it is standing. Meaningful only while <see cref="Present"/>; deliberately
    /// left as it was after a departure, matching <see cref="WatcherBrain.StandPosition"/>'s own
    /// "the last place it stood is evidence" rule.</summary>
    public Vector3 StandPosition { get; private set; }

    /// <summary>Who it is looking at, or -1. Replicated rather than re-derived, because "which of
    /// us is it looking at" is the creature's entire output channel (THRILL §12) and two peers
    /// disagreeing about it is the same defect as two peers disagreeing about where it stands.</summary>
    public int TargetPeerId { get; private set; } = -1;

    /// <summary>Authoritative body yaw, radians, world space.</summary>
    public float BodyYaw { get; private set; }

    /// <summary>Authoritative head yaw, radians, world space (the local head rotation is the
    /// difference — see <see cref="Watcher"/>).</summary>
    public float HeadYaw { get; private set; }

    /// <summary>How the last sighting ended, as the server reported it.</summary>
    public WatcherExit LastExit { get; private set; } = WatcherExit.None;

    /// <summary>False until this peer's late-join dump has landed. See the class remarks.</summary>
    public bool Synced { get; private set; }

    /// <summary>How many facing messages this peer has accepted, ever.
    ///
    /// <para><b>This counter is a test instrument and it was added because a mutation survived
    /// without it.</b> Deleting the server's facing broadcast entirely left
    /// <c>Run-WatcherNetTest.ps1</c> green: both peers simply kept the yaw the appear message
    /// carried, so they still agreed with each other and with the server's logged stand position,
    /// and the only tell was a cross-peer yaw disagreement of exactly 0.0000 where a healthy run
    /// measures 0.016–0.068 rad. Asserting on that tell would be asserting on jitter — the bots in
    /// the harness idle, so the head barely turns and a working stream and a dead one can carry the
    /// same number. A count cannot be confused that way: it is non-zero if and only if messages are
    /// reaching the wire, whether or not the creature is moving its head.</para></summary>
    public int FacingApplied { get; private set; }

    /// <summary>True the instant an appear is applied, and cleared by
    /// <see cref="TakeAppeared"/>. Lets the presentation layer snap rather than ease exactly once
    /// per sighting without having to diff <see cref="SightingId"/> itself.</summary>
    private bool _appeared;

    /// <summary>Server → everyone: a sighting began. Returns false if the message was a duplicate
    /// or arrived out of order, in which case nothing was written.</summary>
    public bool ApplyAppear(int sightingId, Vector3 stand, int targetPeerId, float bodyYaw, float headYaw)
    {
        if (sightingId <= SightingId)
            return false;
        SightingId = sightingId;
        Present = true;
        StandPosition = stand;
        TargetPeerId = targetPeerId;
        BodyYaw = bodyYaw;
        HeadYaw = headYaw;
        LastExit = WatcherExit.None;
        _appeared = true;
        return true;
    }

    /// <summary>Server → everyone, unreliable: the head/body angle for the live sighting. Returns
    /// false for a stale or orphaned packet — see guard (1) in the class remarks.</summary>
    public bool ApplyFacing(int sightingId, float bodyYaw, float headYaw)
    {
        if (!Present || sightingId != SightingId)
            return false;
        BodyYaw = bodyYaw;
        HeadYaw = headYaw;
        FacingApplied++;
        return true;
    }

    /// <summary>Server → everyone: the sighting ended, and why. Returns false for a duplicate or
    /// an id that is not the live one.</summary>
    public bool ApplyGone(int sightingId, WatcherExit exit)
    {
        if (!Present || sightingId != SightingId)
            return false;
        Present = false;
        TargetPeerId = -1;
        LastExit = exit;
        return true;
    }

    /// <summary>Server → one joining peer: the whole current truth in one message, whether or not
    /// a creature is out. Applied only when this peer has genuinely heard nothing yet
    /// (<see cref="SightingId"/> 0) or when the dump is newer than what it has — a joiner whose
    /// first broadcast beat its own dump must not be dragged backwards by it.
    ///
    /// <para>Always marks <see cref="Synced"/>, including when it declines to write: the dump
    /// arriving <i>is</i> the sync, independently of whether it carried news.</para></summary>
    public void ApplyDump(bool present, int sightingId, Vector3 stand, int targetPeerId,
                          float bodyYaw, float headYaw, WatcherExit lastExit)
    {
        Synced = true;
        if (SightingId != 0 && sightingId <= SightingId)
            return;
        SightingId = sightingId;
        Present = present;
        StandPosition = stand;
        TargetPeerId = present ? targetPeerId : -1;
        BodyYaw = bodyYaw;
        HeadYaw = headYaw;
        LastExit = lastExit;
        _appeared = present;
    }

    /// <summary>The server is its own authority, so it is synced from the instant it exists —
    /// there is nothing for it to wait for. Same call, same reason, as
    /// <c>GlowStickRegistry.MarkSynced</c> on the server side.</summary>
    public void MarkSynced() => Synced = true;

    /// <summary>Consumes the one-shot "this is a fresh sighting" edge. True at most once per
    /// sighting, and the presentation layer uses it to SNAP position and facing instead of easing
    /// into them — the watcher was never seen arriving, so easing into a pose would say it was.</summary>
    public bool TakeAppeared()
    {
        bool was = _appeared;
        _appeared = false;
        return was;
    }

    /// <summary>Server-side: record the sighting the server itself just decided, so the authority's
    /// own converged view is written through the same type every client's is. Returns the new
    /// sighting id.</summary>
    public int ServerBeginSighting(Vector3 stand, int targetPeerId, float bodyYaw, float headYaw)
    {
        int id = SightingId + 1;
        ApplyAppear(id, stand, targetPeerId, bodyYaw, headYaw);
        return id;
    }

    /// <summary>Server-side: record the server's own departure. Returns the id that ended, or 0 if
    /// there was nothing live to end.</summary>
    public int ServerEndSighting(WatcherExit exit)
    {
        int id = SightingId;
        return ApplyGone(id, exit) ? id : 0;
    }

    /// <summary>Server-side: keep the authority's own view of the angle current, so
    /// <see cref="BodyYaw"/>/<see cref="HeadYaw"/> mean the same thing on the host as they do on a
    /// client and one broadcast payload builder serves both.</summary>
    public void ServerSetFacing(float bodyYaw, float headYaw)
    {
        if (!Present)
            return;
        BodyYaw = bodyYaw;
        HeadYaw = headYaw;
    }
}
