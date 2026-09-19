using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game;

/// <summary>
/// Server-side bookkeeping for the Steam-transport client-reconnection grace window (see
/// docs/superpowers/specs/2026-07-12-client-reconnection-design.md). Keyed by the
/// disconnecting peer's SteamID64 — a stable identity that survives a reconnect, unlike
/// the transport-level Godot peer id, which is freshly generated every connection. Pure
/// data structure (no Godot Node, no networking; an injectable clock via the nowSec
/// parameters) so it is unit-testable in isolation — see ReconnectSelfTest — and trivially
/// owned as a plain field on Gameplay.
/// </summary>
public sealed class ReconnectRegistry : Sail.Game.Run.IWorldStateSlice
{
    /// <summary>Grace window: a reconnect within this many seconds of the disconnect
    /// resumes at the saved position; any later reconnect (or a fresh join) gets a normal
    /// spawn. Matches the design spec's 60-second window; also the number Gameplay's
    /// client-side retry loop budgets its retries against (see Gameplay.OnServerDisconnected)
    /// so the client gives up at the same moment the server's record actually expires.</summary>
    public const double WindowSec = 60.0;

    /// <summary>Everything a successful <see cref="TryConsume"/> hands back to resume a peer:
    /// its last authoritative position, the prop ids it held at disconnect time (P2 — see
    /// <see cref="Capture"/>'s doc comment for the capture-ordering fix), and its dealt
    /// palette index (P11 — restored instead of a fresh deal, see
    /// Gameplay.ResolveColorIndex/OnPeerConnected). <see cref="HeldPropIds"/> is never null,
    /// even for a peer that held nothing (see <see cref="Capture"/>'s empty-array default
    /// below).</summary>
    public readonly record struct ResumeData(Vector3 Position, int[] HeldPropIds, int ColorIndex);

    private readonly record struct Entry(Vector3 Position, int[] HeldPropIds, int ColorIndex, double ExpiresAtSec);

    private readonly Dictionary<ulong, Entry> _pending = new();

    /// <summary>Current pending-record count (test/diagnostic use only).</summary>
    public int Count => _pending.Count;

    /// <summary>Records a disconnecting peer's last authoritative position, keyed by its
    /// SteamID64, expiring <see cref="WindowSec"/> seconds after <paramref name="nowSec"/>.
    /// A later call for the same SteamID64 (e.g. a second disconnect before the first
    /// window expired) overwrites the earlier record — only the most recent position
    /// matters.
    ///
    /// <paramref name="heldPropIds"/> (P2, audit-documented ordering fix) must be captured by
    /// the CALLER before releasing them — by the time this method runs, the props are already
    /// gone from this peer's held set (Gameplay.OnPeerDisconnected calls
    /// PropManager.HeldPropIdsFor BEFORE PropManager.OnPeerLeft, then passes the result here).
    /// This method itself is not the ordering fix; it just stores whatever the caller already
    /// captured correctly. Pass <see cref="System.Array.Empty{T}"/> for a peer holding
    /// nothing — never null, so a resuming caller's foreach never needs a null check.</summary>
    public void Capture(ulong steamId, Vector3 position, int[] heldPropIds, int colorIndex, double nowSec)
    {
        _pending[steamId] = new Entry(position, heldPropIds, colorIndex, nowSec + WindowSec);
    }

    /// <summary>Looks up and REMOVES a pending record for this SteamID64 if one exists and
    /// has not expired as of <paramref name="nowSec"/> — a resume is one-shot, so a second
    /// reconnect attempt for the same identity never replays a stale position. Returns
    /// false (<paramref name="data"/> left at its default, HeldPropIds an empty array rather
    /// than null) when there is no live record.</summary>
    public bool TryConsume(ulong steamId, double nowSec, out ResumeData data)
    {
        if (_pending.TryGetValue(steamId, out Entry entry) && entry.ExpiresAtSec > nowSec)
        {
            _pending.Remove(steamId);
            data = new ResumeData(entry.Position, entry.HeldPropIds, entry.ColorIndex);
            return true;
        }
        data = new ResumeData(default, System.Array.Empty<int>(), -1);
        return false;
    }

    /// <summary>Discards every record that expired before <paramref name="nowSec"/>. Cheap
    /// to call from Gameplay's existing periodic status tick; an expired record needs no
    /// further cleanup beyond removing the entry — the avatar/prop side was already
    /// finalized at disconnect time. Returns the number of records removed.</summary>
    public int SweepExpired(double nowSec)
    {
        List<ulong>? expired = null;
        foreach (KeyValuePair<ulong, Entry> kv in _pending)
        {
            if (kv.Value.ExpiresAtSec <= nowSec)
                (expired ??= new List<ulong>()).Add(kv.Key);
        }
        if (expired is null)
            return 0;
        foreach (ulong steamId in expired)
            _pending.Remove(steamId);
        return expired.Count;
    }

    // --- The playthrough boundary (CORE-PROG-A2, core-spine spec §5.2 / §6 case 3) -------------

    public string SliceId => "reconnect-registry";

    /// <summary>A resume ticket into a world that no longer exists must die at the playthrough
    /// boundary: before this, a peer disconnecting just before Play Again could — for up to
    /// 60 s — resume at its pre-reset position holding pre-reset props inside a freshly wiped
    /// world (the verified live exploit, SD-1 correction 5). Clearing is sufficient and simpler
    /// than id-stamping; a reconnector after the boundary joins as a fresh peer of the new
    /// playthrough. Same-run resumes are untouched — this runs only at the boundary, never at a
    /// round boundary (spec §6 case 2 keeps the legitimate 60 s window). Idempotent trivially.</summary>
    public void ResetForNewPlaythrough() => _pending.Clear();
}
