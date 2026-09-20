using Godot;

namespace MpFoundation.Voice;

/// <summary>
/// Headless pure-logic checks of the mute set's identity/peer-id bookkeeping
/// (<see cref="MuteRegistry"/>) — the same CI-safe split this project already uses for its
/// other Steam-adjacent logic (see Game.ReconnectSelfTest, Net.Steam.SteamSelfTest):
/// mechanical mute-set semantics prove themselves here; the live identity re-apply across a
/// real Steam-transport reconnect needs live Steam accounts (and, see the report, a
/// client-side identity source the current transport does not expose) and stays a weekend
/// manual protocol, never mocked in CI. Run via --voice-mute-selftest (Run-VoiceTest.ps1);
/// exits the process 0/1.
///
/// The matrix below is the load-bearing proof of P9's contract: mute writes both the peer-id
/// entry and the identity entry when the identity is resolvable (only the peer-id entry when
/// not); a disconnect clears the peer-id entry (recycled-id safety, unchanged) but NOT the
/// identity entry; a reconnect whose identity is a known-muted identity is re-muted
/// immediately; unmute clears both when the identity is resolvable.
/// </summary>
public static class VoiceMuteSelfTest
{
    private static readonly System.Collections.Generic.List<string> Failures = new();

    // Two distinct fake SteamID64s; realistic 64-bit values so nothing accidentally passes
    // by treating them as small ints or colliding with a peer id.
    private const ulong IdX = 76561197960287940UL;
    private const ulong IdY = 76561197960287941UL;

    public static int Run()
    {
        MuteWritesBothWhenIdentityResolvable();
        MuteWritesPeerOnlyWhenIdentityUnresolvable();
        DisconnectClearsPeerEntryButNotIdentity();
        ReconnectReappliesByIdentityAndOnlyThatIdentity();
        UnmuteRemovesBothWhenResolvable();
        UnmuteWithUnresolvableIdentityLeavesIdentityEntry();
        RecycledPeerIdDoesNotInheritStalePeerOnlyMute();
        ClearWipesBothSets();
        ConnectWithUnmutedIdentityIsNoOp();
        MuteAndUnmuteAreIdempotent();

        if (Failures.Count == 0)
        {
            GD.Print("[voice-mute-selftest] PASS (write-both/peer-only, disconnect keeps identity, " +
                "reconnect re-applies by identity, unmute clears both, recycled-id safety, session clear, " +
                "no-op connect, idempotency)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[voice-mute-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    // Mute with a resolvable identity writes BOTH entries: the peer-id one (drives the hot-path
    // ReceiveVoice filter now) and the identity one (survives the peer id, drives re-apply on a
    // reconnect under a fresh peer id).
    private static void MuteWritesBothWhenIdentityResolvable()
    {
        var reg = new MuteRegistry();
        reg.Mute(peerId: 5, id64: IdX);
        Check(reg.IsMuted(5), "mute (resolvable): peer 5 must be muted immediately");

        // Peer 5 drops and the same identity rejoins as a brand-new peer id 8.
        reg.OnPeerDisconnected(5);
        bool reMuted = reg.ReapplyOnConnect(peerId: 8, id64: IdX);
        Check(reMuted, "mute (resolvable): identity entry must survive the drop and re-mute the new peer id");
        Check(reg.IsMuted(8), "mute (resolvable): re-applied peer 8 must read muted");
    }

    // Mute with an UNresolvable identity (id64 == 0, e.g. ENet, or a client that cannot resolve
    // a remote peer's Steam identity) writes only the peer-id entry — there is no identity to
    // persist, so a reconnect under a new peer id gets a clean slate (documented fallback).
    private static void MuteWritesPeerOnlyWhenIdentityUnresolvable()
    {
        var reg = new MuteRegistry();
        reg.Mute(peerId: 5, id64: 0);
        Check(reg.IsMuted(5), "mute (unresolvable): peer 5 must still be muted immediately (peer-id fallback)");

        reg.OnPeerDisconnected(5);
        bool reMuted = reg.ReapplyOnConnect(peerId: 8, id64: 0);
        Check(!reMuted, "mute (unresolvable): nothing to persist, so a new peer id must NOT be auto-muted");
        Check(!reg.IsMuted(8), "mute (unresolvable): the new peer id must read unmuted");
    }

    // A disconnect strips the peer-id entry (a recycled peer id must never inherit a stale mute —
    // VoiceManager's long-standing rationale) but keeps the identity entry alive for a re-apply.
    private static void DisconnectClearsPeerEntryButNotIdentity()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, IdX);
        reg.OnPeerDisconnected(5);
        Check(!reg.IsMuted(5), "disconnect: peer-id entry must be cleared (recycled-id safety)");
        Check(reg.ReapplyOnConnect(9, IdX), "disconnect: identity entry must survive so a reconnect re-mutes");
    }

    // Re-apply keys strictly on identity: the muted identity is re-muted, an unrelated identity is
    // not, and neither leaks into the other (the set is keyed per identity, not global).
    private static void ReconnectReappliesByIdentityAndOnlyThatIdentity()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, IdX);
        reg.OnPeerDisconnected(5);
        Check(reg.ReapplyOnConnect(8, IdX), "reconnect: the muted identity must be re-muted");
        Check(!reg.ReapplyOnConnect(9, IdY), "reconnect: an unrelated identity must NOT be muted (identity isolation)");
        Check(!reg.IsMuted(9), "reconnect: the unrelated peer must read unmuted");
    }

    // Unmuting a player whose identity is resolvable clears BOTH entries — the whole point of P9's
    // "a remuted troll gets a clean slate" being reversible: an intentional unmute must stick
    // across the troll's next reconnect, not silently re-mute from a stale identity entry.
    private static void UnmuteRemovesBothWhenResolvable()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, IdX);
        reg.Unmute(5, IdX);
        Check(!reg.IsMuted(5), "unmute (resolvable): peer-id entry must be gone");
        reg.OnPeerDisconnected(5);
        Check(!reg.ReapplyOnConnect(8, IdX), "unmute (resolvable): identity entry must be gone — no re-mute on reconnect");
    }

    // Honest edge cell: if the identity is UNresolvable at unmute time (id64 == 0) we can only
    // clear the peer-id entry; any identity entry written by an earlier resolvable mute survives.
    // Defined behavior, not accidental — pinned so it can never drift silently. (In the shipped
    // wiring the same seam resolves identity for both mute and unmute, so this asymmetry only
    // arises when the two calls see different resolvability — see the report's fidelity note.)
    private static void UnmuteWithUnresolvableIdentityLeavesIdentityEntry()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, IdX);           // identity X recorded
        reg.Unmute(5, id64: 0);     // cannot resolve now — peer entry only
        Check(!reg.IsMuted(5), "unmute (unresolvable): peer-id entry must still be cleared");
        Check(reg.ReapplyOnConnect(8, IdX), "unmute (unresolvable): the identity entry survives (defined limitation)");
    }

    // The original rationale, now expressed through the peer-id fallback path: a peer-id-only
    // mute (no identity) must not haunt a DIFFERENT player who is later handed the recycled id.
    private static void RecycledPeerIdDoesNotInheritStalePeerOnlyMute()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, id64: 0);       // peer-id-only mute of whoever is peer 5 now
        reg.OnPeerDisconnected(5);  // they leave; peer id 5 is now free to recycle
        bool reMuted = reg.ReapplyOnConnect(peerId: 5, id64: 0); // a new player is handed id 5
        Check(!reMuted, "recycled id: a stale peer-only mute must not re-apply to a recycled peer id");
        Check(!reg.IsMuted(5), "recycled id: the recycled peer must read unmuted");
    }

    // A session reset (VoiceManager.BindPlayersRoot) must wipe both sets — a new match starts with
    // nobody muted, identity entries included.
    private static void ClearWipesBothSets()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, IdX);
        reg.Clear();
        Check(!reg.IsMuted(5), "clear: peer-id entry must be gone");
        Check(!reg.ReapplyOnConnect(8, IdX), "clear: identity entry must be gone");
    }

    // A connect whose identity was never muted is a pure no-op — the common case (everyone joining)
    // must not accidentally mute anyone.
    private static void ConnectWithUnmutedIdentityIsNoOp()
    {
        var reg = new MuteRegistry();
        Check(!reg.ReapplyOnConnect(5, IdX), "no-op connect: an unmuted identity must not be muted");
        Check(!reg.IsMuted(5), "no-op connect: the peer must read unmuted");
        Check(!reg.ReapplyOnConnect(6, 0), "no-op connect: an unresolvable identity must not be muted");
    }

    // Muting twice or unmuting an already-unmuted peer must be harmless (the sets are idempotent).
    private static void MuteAndUnmuteAreIdempotent()
    {
        var reg = new MuteRegistry();
        reg.Mute(5, IdX);
        reg.Mute(5, IdX);
        Check(reg.IsMuted(5), "idempotent: double-mute stays muted");
        reg.Unmute(5, IdX);
        Check(!reg.IsMuted(5), "idempotent: unmute after double-mute clears");
        reg.Unmute(5, IdX); // already gone
        Check(!reg.IsMuted(5), "idempotent: unmute of an already-unmuted peer is harmless");
    }
}
