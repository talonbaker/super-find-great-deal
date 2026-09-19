using System.Collections.Generic;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Light;

/// <summary>
/// <b>The player's flashlight: one bool per peer, replicated, rendered as a soft pool of light on
/// the body.</b> NIGHT-2, closing Talon's 2026-08-29 notes 10 and 11 — *"when night falls, it's
/// extremely difficult to see anything"* and *"give the player a simple 'glow' when they 'toggle'
/// this flashlight."*
///
/// <para>A plain <c>Node</c> child of <c>Gameplay</c> holding every flashlight <c>[Rpc]</c> — the
/// convention every replicated manager in this repo follows, and for the same stated reason.</para>
///
/// <para><b>NOTHING LIKE THIS EXISTED (measured, 2026-08-29).</b> The packet's first instruction
/// was to establish whether a toggleable player light was already in the tree under another name,
/// because Talon wrote *"this flashlight"* as though it did. It did not. What existed at the time
/// was a placed consumable light, a carried fire, a central fire and a pooled one-shot camera flash
/// — none of them a light you switch on and off. The one thing in the tree that read like a hit
/// was a stale doc reference — <c>AvatarVisual.WarmModelCache</c> cites
/// "FlashlightProp.WarmModelCache", and no such type exists anywhere in the repo. Issue #184
/// ratified a battery flashlight into the beta as a <i>design</i> and it was never built. This is
/// that flashlight, at its simplest.</para>
///
/// <para><b>Server-authoritative, so every peer agrees who is lit.</b> The toggle is a request; the
/// server owns the bit and broadcasts it. A purely local light would be invisible to everybody
/// else, and a light only its owner can see reads as a bug in a co-op game. The state is therefore
/// replicated as a per-peer broadcast, including the late-join dump
/// (<see cref="SendFlashlightStateTo"/>) — without which a joiner renders the player standing
/// in front of them as dark while everyone else sees them glowing.</para>
///
/// <para><b>WHICH SIDE OF THE PARITY LINE THIS SITS ON, AND WHAT HOLDS IT THERE.</b> Canon: gameplay
/// visibility is server data identical on every client; rendered light is presentation only. <b>This
/// light is presentation.</b> It illuminates real geometry and it does not extend anybody's sight
/// range by a millimetre — <c>PlayerSightCurve</c> stays the sole producer of that number and this
/// class contributes nothing to it. Three things hold that, in increasing order of how hard they
/// are to defeat:
/// <list type="number">
/// <item>It is written down, here and in <see cref="FlashlightProfile"/>.</item>
/// <item><b>The method does not exist.</b> <c>PlayerSightService.GatherLights</c> can only call
/// <c>AppendLitLightSamples</c> on things that have one; this class has none and there is no
/// <c>Flashlights</c> property to hang it off. Wiring the flashlight into the sight union is not a
/// line somebody adds by forgetting — it is a method they would have to write first.</item>
/// <item><c>FlashlightParityTests</c> scans this file and <see cref="FlashlightProfile"/> for the
/// sight symbols, with a positive control pointed at a file that genuinely uses them, so a scanner
/// that has stopped reading files fails instead of quietly reporting clean.</item>
/// </list>
/// The measured reason this is the correct call rather than a hedge is in
/// <see cref="FlashlightProfile"/>'s doc: in the world Talon plays, the night's limiter is
/// illumination, not range.</para>
///
/// <para><b>Cheap by construction, not by assumption.</b> One shadowless <c>OmniLight3D</c> per lit
/// player, built lazily on first use, hidden rather than destroyed when the light goes off, and
/// never built at all on a headless peer (a dedicated server submits no draw calls). The
/// performance floor is a GTX 970 on Forward+ and the group can be six, so the light that is off
/// costs a hidden node and the light that is on costs one unshadowed omni —
/// <see cref="FlashlightProfile.ShadowsEnabled"/> carries the argument.</para>
/// </summary>
public partial class FlashlightManager : Node
{
    public const string NodeName = "FlashlightManager";

    /// <summary>The live flashlight system, or null in a world that has none. Same static-instance
    /// seam every replicated manager here provides; null-check it.</summary>
    public static FlashlightManager? Instance { get; private set; }

    private sealed class Lamp
    {
        public bool On;
        public OmniLight3D? Light;
    }

    private readonly Dictionary<int, Lamp> _lamps = new();
    private bool _isServer;
    private System.Func<int, Node3D?>? _avatarResolver;

    /// <summary>Raised on every peer when a player's flashlight changes state. Nothing subscribes
    /// yet; it exists so a later SFX click or a HUD pip subscribes rather than polling.</summary>
    public event System.Action<int, bool>? FlashlightToggled;

    /// <summary>Positions come from the server's OWN authoritative avatar nodes, never from
    /// anything a client asserted — the identical seam <c>PropManager.AvatarResolver</c> takes.
    /// Here it is also what the renderer parents the light to,
    /// on every peer, so the glow travels with the body for free rather than being pushed a
    /// position per frame.</summary>
    public void Setup(bool isServer, System.Func<int, Node3D?> avatarResolver)
    {
        _isServer = isServer;
        _avatarResolver = avatarResolver;
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    // --- Queries ------------------------------------------------------------------------------

    /// <summary>Is this peer's flashlight on? False for an unknown peer, which is the safe
    /// direction: "I do not know about that player" must never answer "yes, they are lit".</summary>
    public bool IsOn(int peerId) => _lamps.TryGetValue(peerId, out Lamp? l) && l.On;

    /// <summary>How many flashlights are currently on, across every peer this node knows about.
    /// Diagnostics and tests — it is what a capture harness asserts against.</summary>
    public int LitCount
    {
        get
        {
            int n = 0;
            foreach (KeyValuePair<int, Lamp> kv in _lamps)
            {
                if (kv.Value.On)
                    n++;
            }
            return n;
        }
    }

    // --- The verb -----------------------------------------------------------------------------

    /// <summary>Client-side entry point for the toggle: one key press, one request.
    /// <b>No local effect until the server confirms</b> — <c>PropManager.ClientRequestGrab</c>'s
    /// precedent, so there is no pending state for a dropped packet to strand the light in.</summary>
    public void ClientRequestToggle()
    {
        if (_isServer)
            ServerToggle(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.RequestToggle);
    }

    /// <summary>Client → server: "flip my flashlight." <b>Carries no payload</b>, deliberately: the
    /// server holds the current bit and flips it, so a hostile client cannot assert a state, only
    /// ask for the other one.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.FlashlightChannel)]
    private void RequestToggle()
    {
        if (!_isServer)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0)
            return;
        ServerToggle(peer);
    }

    /// <summary>Server: flip one peer's light and tell everyone. <b>The single funnel</b> — every
    /// path that changes a flashlight goes through <see cref="ServerSet"/>, so there is no state
    /// change that can skip the broadcast. Idempotency is free here (a toggle has no "already in
    /// that state" case), but the funnel is what makes <see cref="ServerSet"/>'s idempotency
    /// reachable by the reset and disconnect paths below.</summary>
    public bool ServerToggle(int peerId)
    {
        if (!_isServer || peerId <= 0)
            return false;
        return ServerSet(peerId, !IsOn(peerId));
    }

    /// <summary>Server: put one peer's light into a stated state. Returns false when nothing
    /// changed, so a caller can tell a no-op from a flip. <b>Idempotent</b> (MECHANICS-BIBLE:
    /// a state setter that re-broadcasts an unchanged state is a state setter that will be called
    /// in a loop by somebody).</summary>
    public bool ServerSet(int peerId, bool on)
    {
        if (!_isServer || peerId <= 0)
            return false;
        Lamp l = LampFor(peerId);
        if (l.On == on)
            return false;
        Rpc(MethodName.BroadcastFlashlight, peerId, on);
        return true;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.FlashlightChannel, CallLocal = true)]
    private void BroadcastFlashlight(int peerId, bool on)
    {
        Lamp l = LampFor(peerId);
        bool was = l.On;
        l.On = on;
        PushLight(peerId, l);
        if (was != on)
            FlashlightToggled?.Invoke(peerId, on);
    }

    /// <summary>Server-only: bring a late joiner up to date on every flashlight. Without this a
    /// joiner renders the player standing in front of them as dark while every other peer sees
    /// them glowing — the same funnel and the same reason as <c>PropManager.SendDumpTo</c>. Only
    /// the lights that are ON are sent: OFF is the
    /// zero-initialized default a fresh <see cref="Lamp"/> already reads as, so sending it would be
    /// a message whose whole content is "no change".</summary>
    public void SendFlashlightStateTo(int peerId)
    {
        if (!_isServer)
            return;
        foreach (KeyValuePair<int, Lamp> kv in _lamps)
        {
            if (kv.Value.On)
                RpcId(peerId, MethodName.BroadcastFlashlight, kv.Key, true);
        }
    }

    /// <summary>A peer left. The light goes out on every remaining peer and the entry is dropped —
    /// unlike the glow-stick bank, there is nothing here worth holding across a reconnect (a player
    /// who comes back presses F again, which costs them one keystroke). Dropping the entry also
    /// releases the <see cref="OmniLight3D"/>, which is already gone with the avatar it was
    /// parented to.</summary>
    public void OnPeerLeft(int peerId)
    {
        if (_isServer)
            ServerSet(peerId, false);
        _lamps.Remove(peerId);
    }

    // --- Presentation -------------------------------------------------------------------------

    /// <summary>The glow, on every peer. <b>Presentation only</b> — see the class doc. Parented to
    /// the avatar's own node so it travels with the body with no per-frame position write, which is
    /// also why there is no <c>_Process</c> on this class at all: a light that follows its parent
    /// costs nothing to keep in place.
    ///
    /// <para>Built lazily and then <b>hidden rather than freed</b> when the light goes off. A
    /// player who flicks the switch twice a second would otherwise churn a renderer node twice a
    /// second, and the hidden node costs a scene-tree entry.</para>
    ///
    /// <para>Headless builds nothing: a dedicated server submits no draw calls, and the guard is
    /// what keeps the scene suites from asserting on a renderer node that a real dedicated server
    /// would never have.</para></summary>
    private void PushLight(int peerId, Lamp l)
    {
        Node3D? avatar = _avatarResolver?.Invoke(peerId);
        if (avatar == null || !IsInstanceValid(avatar))
        {
            // No body, no light. The next toggle (or the avatar spawning) rebuilds it; a light
            // with nothing to hang on is not something to guess a position for.
            l.Light = null;
            return;
        }

        if (l.Light == null || !IsInstanceValid(l.Light) || l.Light.GetParent() != avatar)
        {
            if (NetworkManager.Instance?.IsHeadless != false)
                return;
            // An avatar that respawned brings a fresh node, so an old light may still be valid and
            // parented to a corpse — look for one already on THIS body before building a second.
            OmniLight3D? existing = avatar.GetNodeOrNull<OmniLight3D>(FlashlightProfile.LightNodeName);
            l.Light = existing ?? NewLight();
            if (existing == null)
                avatar.AddChild(l.Light);
        }

        l.Light.Visible = l.On;
    }

    private static OmniLight3D NewLight() => new()
    {
        Name = FlashlightProfile.LightNodeName,
        Position = new Vector3(0f, FlashlightProfile.MountHeightM, 0f),
        OmniRange = FlashlightProfile.LitRadiusM,
        LightColor = FlashlightProfile.LightColor,
        LightEnergy = FlashlightProfile.LightEnergy,
        ShadowEnabled = FlashlightProfile.ShadowsEnabled,
        Visible = false,
    };

    private Lamp LampFor(int peerId)
    {
        if (!_lamps.TryGetValue(peerId, out Lamp? l))
        {
            l = new Lamp();
            _lamps[peerId] = l;
        }
        return l;
    }

    /// <summary><b>Re-attach every LIT peer's glow to its current body.</b> The respawn case, and
    /// it is why this class has a per-frame callback at all: an avatar that dies and respawns is a
    /// <i>new node</i>, the old light died with the old body, and without this the player stays lit
    /// in every peer's replicated state while rendering dark — the exact class of desync that reads
    /// as "the feature broke". Self-healing beats a call somebody has to remember to add at each of
    /// the several places an avatar can be replaced.
    ///
    /// <para><b>It is cheap, and the shape is what makes it cheap rather than a claim that it is.</b>
    /// Only lamps that are ON are walked — an off flashlight needs no node and gets no work — so the
    /// upper bound is the supported group size in dictionary entries, each costing two validity
    /// checks and a bool write.</para></summary>
    public void RefreshLights()
    {
        foreach (KeyValuePair<int, Lamp> kv in _lamps)
        {
            if (kv.Value.On)
                PushLight(kv.Key, kv.Value);
        }
    }

    public override void _Process(double delta) => RefreshLights();
}
