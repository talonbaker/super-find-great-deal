using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>Who moves a player between rooms.</b> The epoch-bump teleport, lifted out of the old
/// level's <c>TvPortalHost</c> at the fork (BASE-1, 2026-09-19) and separated from the thing that
/// used to trigger it, because in this game a room change is a ROUND PHASE, not somebody walking
/// into a screen. Nothing calls it yet; ROUND-1 does, from the server's phase-transition tick.
///
/// <para><b>The server owns the move, and that is the whole reason this is a service and not a
/// node with its own RPC.</b> A client that teleported itself would either desync until the next
/// snapshot yanked it back, or send a request the server already had.
/// <see cref="SandboxAvatar.ServerTeleportTo"/> bumps the prediction epoch, which is what makes
/// the owner snap cleanly instead of rubber-banding across 40 m — that method's own doc says this
/// is the case it exists for.</para>
///
/// <para><b>The cooldown is per peer, not global.</b> The version this came from started with one
/// shared field because the game it was ported from had one player; a shared gate means one
/// player's move silently swallows another's, which is the class of bug that only appears with
/// four people in a room. <see cref="CooldownMsec"/> is the whole state machine — one timestamp,
/// tested and written in the same branch, so there is no window between the test and the set
/// (<c>MECHANICS-BIBLE</c> §2).</para>
///
/// <para><b>What it deliberately does NOT do.</b> It does not flash, announce, or play anything.
/// The old host sent a white-crackle to the traveller alone, and that asymmetry was a direction
/// rather than a bandwidth saving — the watchers were meant to see a friend simply not be there
/// any more. Whether this game wants that, and what it looks like, is DOOR-1's and ROUND-1's
/// call, so this file has no opinion and no presentation in it.</para>
/// </summary>
public static class RoomTeleport
{
    /// <summary>Re-entry gate, carried over unchanged. A body that has just been moved is still
    /// overlapping whatever it arrived in for several frames, and any second trigger at the
    /// destination is waiting; without the gate one transition can bounce a player back and
    /// forth.</summary>
    public const ulong CooldownMsec = 800;

    /// <summary>Last move per owning peer. Not per destination: the point of the gate is that the
    /// player who just arrived is standing at the arrival point, so anything that fires second is
    /// a DIFFERENT source and a per-source gate would not catch it.</summary>
    private static readonly Dictionary<int, ulong> LastMoveMsec = new();

    /// <summary>
    /// Moves one avatar, on the server, bumping its prediction epoch. Returns false if the peer
    /// is inside its cooldown, so a caller can tell "refused" from "done" rather than assuming.
    ///
    /// <para>Call this ONLY on the simulating peer. It is not guarded against being called on a
    /// client, deliberately: <see cref="SandboxAvatar.ServerTeleportTo"/> already switches on the
    /// body's own role, and a silent no-op there would hide a caller that is wired to the wrong
    /// side of the wire.</para>
    /// </summary>
    public static bool ServerMove(SandboxAvatar avatar, Vector3 destination)
    {
        int peer = avatar.OwnerPeerId;
        ulong now = Time.GetTicksMsec();
        if (LastMoveMsec.TryGetValue(peer, out ulong last) && now - last < CooldownMsec)
            return false;
        LastMoveMsec[peer] = now;
        avatar.ServerTeleportTo(destination);
        GD.Print($"[room] peer={peer} moved to {destination}");
        return true;
    }

    /// <summary>Drops a peer's gate. ENet peer ids are RECYCLED, so an entry left behind would
    /// make the next player handed this id silently un-teleportable for up to
    /// <see cref="CooldownMsec"/> after they join — the same recycled-id discipline the honk
    /// latch and the voice caches were written under.</summary>
    public static void ForgetPeer(int peerId) => LastMoveMsec.Remove(peerId);

    /// <summary>Clears every gate. A server process can host more than one match, and these are
    /// process-wide statics; a timestamp from the previous session refusing the first move of the
    /// next one is exactly the kind of stale-static bug this repo cleans up on purpose
    /// elsewhere.</summary>
    public static void Reset() => LastMoveMsec.Clear();
}
