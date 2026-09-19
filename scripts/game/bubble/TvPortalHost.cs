using System.Collections.Generic;
using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>Who moves the player through a TV.</b> <see cref="TvPortal"/> is deliberately dumb — it
/// detects a body at its screen and raises <c>BodyAtScreen</c>; this node is the half that
/// decides what "teleport" means, ported in shape from mp-foundation's
/// <c>HubHost.OnTvEntered</c> (<c>:221-231</c>) and adapted from one offline player to a
/// six-peer session.
///
/// <para><b>Only the simulating peer subscribes.</b> A client that also subscribed would raise
/// the event locally off its own predicted body and then either teleport itself (a desync the
/// very next snapshot would yank back) or send a second request the server already has. The
/// server owns the move, bumps the epoch through
/// <see cref="SandboxAvatar.ServerTeleportTo"/>, and prediction snaps cleanly — which is
/// exactly the case that method's own doc says it exists for.</para>
///
/// <para><b>The flash is sent to the traveller and to nobody else, and that is a DIRECTION
/// rather than a bandwidth saving</b> (<c>THRILL-BIBLE.md</c> §12, "The TV that is a doorway",
/// 2026-08-28). If every peer flashed, one player vanishing would become an announced event and
/// the watchers would be handed an explanation they did not earn; the whole value of the beat
/// for everyone who did not walk into the screen is §5.4's asymmetry — a friend was there, is
/// not, and comes back claiming there is a couch.</para>
///
/// <para><b>The cooldown is per-peer, not global.</b> mp-foundation's host had one field because
/// it had one player. A single shared gate here would mean one player's trip silently swallowed
/// another player's, which is the class of bug that only ever shows up with four people in a
/// room. The 800 ms value and its job are unchanged: a body that walks INTO a screen is still
/// overlapping that same <c>Area3D</c> for several frames, and the destination TV's trigger is
/// a second one waiting — without the gate a single walk can bounce a player back and forth.
/// <see cref="TeleportCooldownMsec"/> is the whole state machine (MECHANICS-BIBLE §2: one
/// timestamp, checked and written in the same branch, so there is no window between the test
/// and the set).</para>
/// </summary>
public partial class TvPortalHost : Node
{
    public const string NodeName = "TvPortalHost";

    /// <summary>Ported unchanged from <c>HubHost.cs:226</c>.</summary>
    public const ulong TeleportCooldownMsec = 800;

    /// <summary>Last teleport per owning peer. Not per-portal: the point of the gate is that the
    /// player who just arrived is standing in the arrival TV's trigger, so the portal that fires
    /// second is a DIFFERENT one and a per-portal gate would not catch it.</summary>
    private readonly Dictionary<int, ulong> _lastTeleportMsec = new();

    private bool _isServer;

    /// <summary>Portals are found once, after the whole world tree exists. <c>_Ready</c> runs
    /// bottom-up inside <c>AddChild</c>, so a host added by <c>BubbleTestWorld._Ready</c> would
    /// otherwise scan a tree whose sibling sections are present but whose runtime-added TVs are
    /// not.</summary>
    public override void _Ready()
    {
        _isServer = NetworkManager.Instance is null
                    || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;
        CallDeferred(nameof(Subscribe));
    }

    private void Subscribe()
    {
        Node root = GetParent() ?? this;
        int found = 0;
        foreach (TvPortal portal in FindPortals(root))
        {
            found++;
            if (!_isServer)
                continue; // clients keep the trigger unsubscribed — see the class doc.
            TvPortal captured = portal;
            portal.BodyAtScreen += body => OnBodyAtScreen(body, captured);
        }

        // Loud rather than silent: a level whose easter egg quietly does nothing looks exactly
        // like a level that never had one, and the symptom ("I walked into it and nothing
        // happened") reads as a netcode bug rather than as a missing subscription.
        GD.Print($"[bubbletest.tv] host ready — portals={found} server={_isServer}");
        if (found == 0)
            GD.PushWarning("[bubbletest.tv] no TvPortal in the world — the TV easter egg is inert.");
    }

    private static IEnumerable<TvPortal> FindPortals(Node from)
    {
        if (from is TvPortal portal)
            yield return portal;
        foreach (Node child in from.GetChildren())
            foreach (TvPortal nested in FindPortals(child))
                yield return nested;
    }

    private void OnBodyAtScreen(CharacterBody3D body, TvPortal tv)
    {
        if (body is not SandboxAvatar avatar)
            return; // a prop or a creature at the screen is not a traveller.

        int peer = avatar.OwnerPeerId;
        ulong now = Time.GetTicksMsec();
        if (_lastTeleportMsec.TryGetValue(peer, out ulong last)
            && now - last < TeleportCooldownMsec)
            return;
        _lastTeleportMsec[peer] = now;

        Vector3 destination = tv.Destination;
        avatar.ServerTeleportTo(destination);
        GD.Print($"[bubbletest.tv] peer={peer} from={tv.GlobalPosition} to={destination}");
        SendFlashTo(peer, destination);
    }

    /// <summary>The traveller's own white-crackle, and only theirs. Offline and the listen
    /// server's own body take the direct call — there is no peer to address in the first case,
    /// and in the second an <c>RpcId</c> to ourselves would be a round trip through the
    /// multiplayer API to reach a node we are already standing in.</summary>
    private void SendFlashTo(int peer, Vector3 at)
    {
        bool networked = NetworkManager.Instance is not null
                         && NetworkManager.Instance.Role != NetworkManager.SessionRole.None
                         && Multiplayer.MultiplayerPeer is not null;
        if (!networked || peer == Multiplayer.GetUniqueId())
        {
            PlayLocalFlash(at);
            return;
        }
        RpcId(peer, nameof(PlayLocalFlash), at);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PlayLocalFlash(Vector3 at)
    {
        // A headless peer (a bot, the dedicated server) has no screen to flash and no
        // AudioStreamPlayer3D worth spending; the log line is what the scene test reads.
        GD.Print($"[bubbletest.tv.flash] peer={Multiplayer.GetUniqueId()} at={at}");
        // LD-2: walking into a television is an act of the body (the clock's spec list). This is
        // the one place the LOCAL peer learns its own teleport happened, on every role.
        Sail.Game.World.CadenceTracker.Instance?.Clock.NoteAct("tv");
        if (NetworkManager.Instance is { IsHeadless: true })
            return;
        TvPortal.PlayTransitionFlash(this, at);
    }
}
