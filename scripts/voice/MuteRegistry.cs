using System.Collections.Generic;

namespace MpFoundation.Voice;

/// <summary>
/// The mute set behind proximity voice, keyed two ways at once (P9). Local-only per-player
/// mute is an audio control, not moderation — this holds only the caller's own choices about
/// who they don't want to hear, never anything server-authoritative.
///
/// A mute is recorded against BOTH the current Multiplayer peer id (the key the hot-path
/// receive filter reads — see VoiceManager.ReceiveVoice) AND, when the peer's stable identity
/// is resolvable (a non-zero SteamID64), that identity. The two entries serve different jobs:
///   - the peer-id entry is the live filter and MUST be cleared on disconnect, because a peer
///     id is recycled and a stale entry would silence an unrelated newcomer handed that id
///     (VoiceManager's long-standing recycled-id rationale, preserved);
///   - the identity entry SURVIVES the disconnect, so when the same identity reconnects under a
///     fresh peer id (every reconnect is a new connection => new peer id) it is re-muted at once
///     instead of getting a clean slate (P9: a remuted troll no longer escapes by dropping).
///
/// Pure and Godot-free on purpose (mirrors Game.ReconnectRegistry): the whole write-both /
/// clear-peer-not-identity / re-apply-on-connect / unmute-both contract is CI-testable without a
/// live match — see Voice.VoiceMuteSelfTest. VoiceManager owns one instance and supplies the
/// identity via NetworkManager.SteamId64Of at each call site (0 when unresolvable).
///
/// FIDELITY NOTE (see task-A5 report): SteamId64Of resolves a remote peer's identity only on the
/// SERVER (SteamPeer.SteamId64Of returns 0 on clients, and 0 for any ENet connection). Mute is a
/// client-side control, so with today's transport the identity entry is written only when an
/// identity is actually resolvable; where it is not, the class degrades cleanly to peer-id-only
/// muting (the fallback path this test suite fully exercises). Making the identity re-apply fire
/// on clients needs a client-visible identity source the transport does not expose today — out of
/// this task's no-protocol-change scope; disclosed rather than silently worked around.
/// </summary>
public sealed class MuteRegistry
{
    // The live filter key: peers the local player currently does not want to hear. Cleared per
    // peer on disconnect (recycled-id safety) and wholesale on session reset.
    private readonly HashSet<int> _mutedPeers = new();
    // The persistent key: identities (SteamID64) the local player has muted. Survives disconnect
    // so a reconnect under a new peer id re-mutes; cleared only by an intentional unmute (with a
    // resolvable identity) or a session reset.
    private readonly HashSet<ulong> _mutedIdentities = new();

    /// <summary>Mutes <paramref name="peerId"/>. Always records the peer-id entry (the live
    /// filter); additionally records the identity entry when <paramref name="id64"/> is
    /// resolvable (non-zero), so the mute outlives this peer id.</summary>
    public void Mute(int peerId, ulong id64)
    {
        _mutedPeers.Add(peerId);
        if (id64 != 0)
            _mutedIdentities.Add(id64);
    }

    /// <summary>Unmutes <paramref name="peerId"/>. Removes the peer-id entry always; removes the
    /// identity entry when <paramref name="id64"/> is resolvable, so an intentional unmute sticks
    /// across the player's next reconnect rather than silently re-muting from a stale identity.</summary>
    public void Unmute(int peerId, ulong id64)
    {
        _mutedPeers.Remove(peerId);
        if (id64 != 0)
            _mutedIdentities.Remove(id64);
    }

    /// <summary>The hot-path check (VoiceManager.ReceiveVoice): is this peer id currently
    /// muted? A HashSet lookup — no per-frame cost beyond what the old HashSet had.</summary>
    public bool IsMuted(int peerId) => _mutedPeers.Contains(peerId);

    /// <summary>Called when a peer (re)connects: if its identity is one the local player muted,
    /// re-mute it under this fresh peer id immediately. No-op when the identity is unresolvable
    /// (<paramref name="id64"/> == 0) or was never muted. Returns whether it (re)muted.</summary>
    public bool ReapplyOnConnect(int peerId, ulong id64)
    {
        if (id64 != 0 && _mutedIdentities.Contains(id64))
        {
            _mutedPeers.Add(peerId);
            return true;
        }
        return false;
    }

    /// <summary>Called on disconnect: drop only the peer-id entry (a recycled peer id must never
    /// inherit a stale mute). The identity entry is deliberately left intact so a reconnect under
    /// a new peer id re-applies the mute (ReapplyOnConnect).</summary>
    public void OnPeerDisconnected(int peerId) => _mutedPeers.Remove(peerId);

    /// <summary>Session reset (VoiceManager.BindPlayersRoot): a new match starts with nobody
    /// muted — clears both the live filter and the persistent identity set.</summary>
    public void Clear()
    {
        _mutedPeers.Clear();
        _mutedIdentities.Clear();
    }
}
