using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using MpFoundation.Net;

namespace Sail.Game.Water;

/// <summary>
/// The lake's authority and its one public surface. Owns the cold clock, the sputter-out
/// sequence, Soaked, and the five-event stream packet W4 (splash VFX and audio) builds its
/// listener against — the fixed W2↔W4 contract in the lake-water design spec §13.1.
///
/// <b>What it does NOT own.</b> The <see cref="WaterState"/> itself is derived inside
/// <c>AvatarMotor.Step</c> from submersion depth and rides <c>MoveState</c>, because that is the
/// only place all three simulation paths (server authority, owner prediction, reconciliation
/// replay) provably agree. This service <i>reads</i> that state; it never authors it. Nothing
/// here polls a physics query for "am I in water", and there are no trigger volumes anywhere.
///
/// <b>Server-authoritative.</b> Every value below is computed on the server and pushed to
/// clients; a client's copy is display state and is never fed back into anything. Chill is a
/// pure function of position (spec §5), so a doctored client cannot claim to be warm.
///
/// <b>Wiring.</b> Constructed unconditionally by <c>Gameplay</c> as a child node with a
/// <see cref="NodeName"/> const and a static <see cref="Instance"/> — the
/// <c>CycleDriver</c>/<c>RunDriver</c> pattern, not an autoload. In a
/// world with no lake (the playground, the labs, the CI worlds) every avatar simply resolves
/// <see cref="WaterState.Dry"/> forever and the whole thing is inert, which is the same
/// harmlessness contract every replicated manager here already holds.
/// </summary>
public partial class WaterService : Node, Run.IWorldStateSlice
{
    public const string NodeName = "WaterService";

    /// <summary>The live service, or null outside a gameplay session. Nulled in
    /// <c>_ExitTree</c> so a torn-down session never leaves a dangling reference behind (the
    /// same self-nulling singleton shape every other manager in this codebase uses).</summary>
    public static WaterService? Instance { get; private set; }

    // --- The W2 -> W4 event contract (spec §13.1) -----------------------------------------------
    // Raised on EVERY peer, from the server-replicated transition rather than a local guess, so
    // "fires exactly once per event, and every client hears it in the same instant" (spec §9.1)
    // is structural. A client that predicted its own entry a few ms early would otherwise splash
    // on a different frame from everyone else's view of the same splash.

    /// <summary>Toward deeper water — Dry->Wading, Dry->Swimming, Wading->Swimming.</summary>
    public event Action<WaterEvent>? Entered;

    /// <summary>Out to <see cref="WaterState.Dry"/>.</summary>
    public event Action<WaterEvent>? Exited;

    /// <summary>Throttled while moving in Wading/Swimming — the thrashing.</summary>
    public event Action<WaterEvent>? Splash;

    /// <summary>Chill hit 1.0; the swallow has begun.</summary>
    public event Action<WaterEvent>? WentUnder;

    /// <summary>The recovery landed and control has returned. One per sputter-out.</summary>
    public event Action<WaterEvent>? Sputtered;

    /// <summary>Soaked was set or cleared for a peer. Raised on every peer. Not part of the
    /// W4 contract — the presentation layer's hook for the wet visual (spec §7).</summary>
    public event Action<int, bool>? SoakedChanged;

    /// <summary>
    /// The tape in this peer's camcorder must be ruined (spec §6, "the cost is the tape, not the
    /// camera"). Raised on the server only, exactly once per sputter-out.
    ///
    /// <b>Nothing consumes this yet, and that is a known, named gap rather than an oversight.</b>
    /// The camcorder does not exist on this trunk — <c>CamcorderController</c> and its
    /// <c>TapeUsedSec</c> live on the unmerged <c>feat/tier0-camcorder</c> branch, and that
    /// branch has no ruin/spoil path of its own either. When it merges, the wiring is one
    /// subscription here; until then <see cref="TapeRuinCount"/> proves the event fires exactly
    /// once, which is the half of the contract that can be proven today.
    /// </summary>
    public event Action<int>? TapeRuinRequested;

    // --- Tuning that is this service's rather than the geometry's -------------------------------

    /// <summary>Minimum horizontal speed before the thrash splash stream fires at all. Below
    /// this you are drifting, not thrashing.</summary>
    private const float SplashSpeedThreshold = 0.8f;

    /// <summary>Seconds between thrash splashes for one peer. Throttled here rather than in W4
    /// so the throttle is authoritative and identical on every client (spec §9: triggered by
    /// events, never by polling).</summary>
    private const float SplashIntervalSec = 0.45f;

    /// <summary>How often the server broadcasts a peer's chill/soaked/phase. 6 Hz: the cue is a
    /// slow build over tens of seconds, so anything faster is bandwidth spent on nothing.</summary>
    private const float BroadcastIntervalSec = 1f / 6f;

    /// <summary>Chill change that forces an immediate broadcast regardless of the interval.</summary>
    private const float BroadcastChillEpsilon = 0.02f;

    // --- Per-peer state --------------------------------------------------------------------------

    private sealed class Peer
    {
        public WaterState State = WaterState.Dry;
        public float Chill;
        public bool Soaked;
        public float WarmthSec;
        public float SplashCooldown;
        public readonly SputterSequence Sputter = new();

        // Server-only broadcast bookkeeping.
        public float SinceBroadcast;
        public float LastSentChill = -1f;
        public WaterState LastSentState = WaterState.Dry;
        public bool LastSentSoaked;
        public SputterPhase LastSentPhase = SputterPhase.None;

        public int TapeRuins;
    }

    private readonly Dictionary<int, Peer> _peers = new();
    private bool _isServer;
    private Func<IEnumerable<SandboxAvatar>>? _avatars;

    /// <summary>The warmth probe, when the caller has a heat source to answer from (none does in
    /// this build; the labs and the self-test never did). Null means nowhere is warm — see
    /// <see cref="IsInCampfireWarmth"/>.</summary>
    private Func<Vector3, bool>? _warmthAt;

    /// <summary>False until this peer has been told the authoritative water state at least once.
    /// Hard contract, identical to <c>CycleDriver.Synced</c>:
    /// nothing visible may be derived from this service while it is false, because a client's
    /// default-constructed "everyone is Dry with zero chill" is a guess, not a fact.</summary>
    public bool Synced { get; private set; }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Wire the service up. <paramref name="avatars"/> enumerates the live avatars; the service
    /// deliberately takes providers rather than reaching for node paths or a sibling's static
    /// <c>Instance</c>, because the players container is Gameplay's and this service must stay
    /// harmless in a world that has none (the playground, the labs, CI).
    ///
    /// <para><paramref name="warmthAt"/> is a heat source's warmth query — the whole answer to
    /// "does this position dry a soaked player off" when one is supplied. Optional: the labs and
    /// <c>WaterSelfTest</c> have no heat source, and neither does any world in this build, so
    /// with it absent nowhere is warm.</para>
    /// </summary>
    public void Setup(bool isServer, Func<IEnumerable<SandboxAvatar>> avatars,
        Func<Vector3, bool>? warmthAt = null)
    {
        _isServer = isServer;
        _avatars = avatars;
        _warmthAt = warmthAt;
        Synced = isServer;
        // CORE-PROG-A2 (spec §5.2's water row): "a new playthrough starts dry and warm; the
        // deferral ends here" — the STATE-CASCADE-TABLE:183 deliberate deferral, closed.
        // Null-safe: the water labs and WaterSelfTest construct this with no store and keep
        // today's no-reset behavior.
        Run.WorldStateStore.Instance?.Register(this);
    }

    // --- The world-state slice (CORE-PROG-A2) --------------------------------------------------

    public string SliceId => "water";

    /// <summary>Everyone dry, warm, and un-soaked. Dropping the per-peer table is the whole
    /// reset on BOTH sides: an absent entry already reads Dry/chill-0 through every query
    /// (<see cref="StateOf"/>/<see cref="ChillOf"/>'s unknown-peer contracts), the server's
    /// next tick lazily rebuilds fresh entries from the live avatars and re-broadcasts them
    /// (a fresh entry's <c>LastSentChill = -1</c> forces the push), and a mid-sputter peer's
    /// control lock is released by that same tick's <c>ServerSetWaterFlags(false, false)</c>
    /// push — no one is left swallowed behind a boundary. Idempotent: clearing an empty table.</summary>
    public void ResetForNewPlaythrough() => _peers.Clear();

    // --- Public read surface (spec §13.1) ---------------------------------------------------------

    /// <summary>This peer's water state. <see cref="WaterState.Dry"/> for an unknown peer —
    /// "not in the water" is the only safe answer to "who?".</summary>
    public WaterState StateOf(int peerId) =>
        _peers.TryGetValue(peerId, out Peer? p) ? p.State : WaterState.Dry;

    /// <summary>This peer's chill in <c>[0,1]</c>. Zero for an unknown peer.</summary>
    public float ChillOf(int peerId) =>
        _peers.TryGetValue(peerId, out Peer? p) ? p.Chill : 0f;

    /// <summary>Whether this peer is Soaked (spec §7).</summary>
    public bool IsSoaked(int peerId) =>
        _peers.TryGetValue(peerId, out Peer? p) && p.Soaked;

    /// <summary>Where this peer is in the sputter-out sequence.</summary>
    public SputterPhase PhaseOf(int peerId) =>
        _peers.TryGetValue(peerId, out Peer? p) ? p.Sputter.Phase : SputterPhase.None;

    /// <summary>
    /// Whether the camcorder is usable right now (spec §4: "the camcorder cannot be used while
    /// swimming — the lake is a place your primary verb does not work"). Also false through a
    /// sputter-out, because control is locked and a verb you cannot decline is not a verb.
    ///
    /// <b>Query, not enforcement.</b> The camcorder is not on this trunk (see
    /// <see cref="TapeRuinRequested"/>), so there is nothing here to disable; this is the single
    /// predicate the camcorder's own input gate reads when it merges, stated now so the branch
    /// converges on one rule rather than re-deriving "is he swimming" from the avatar.
    /// </summary>
    public bool CamcorderUsable(int peerId)
    {
        if (!_peers.TryGetValue(peerId, out Peer? p))
            return true;
        return p.State != WaterState.Swimming && p.Sputter.Phase == SputterPhase.None;
    }

    /// <summary>
    /// Footstep noise multiplier for a peer — 1.0 dry, <see cref="WaterGeometry.SoakedFootstepNoiseMul"/>
    /// while Soaked (the squelch).
    ///
    /// <b>Hook point only: nothing consumes this.</b> Spec §7 asks for exactly that, and the same
    /// discipline M5's dock creak uses. The shared noise channel it would feed
    /// (INTERACTION-BIBLE §9.3) has no implementation on this trunk and the creature sim that
    /// would listen does not exist. Wiring it to nothing on purpose is the point; it is here so
    /// the hook is discoverable rather than reinvented.
    /// </summary>
    public float FootstepNoiseMultiplierFor(int peerId) =>
        IsSoaked(peerId) ? WaterGeometry.SoakedFootstepNoiseMul : 1f;

    /// <summary>How many times this peer's tape has been ruined. Instrumentation for the
    /// exactly-once proof.</summary>
    public int TapeRuinCount(int peerId) =>
        _peers.TryGetValue(peerId, out Peer? p) ? p.TapeRuins : 0;

    /// <summary>Drop a peer's water state entirely (disconnect). Idempotent.</summary>
    public void ForgetPeer(int peerId) => _peers.Remove(peerId);

    /// <summary>
    /// Lab/test-only: stamp a peer's chill directly, bypassing the clock entirely. Used by
    /// <c>MpFoundation.Dev.WaterCueLab</c> (P2, 2026-08-08) to walk the urgency-cue escalation
    /// ladder for a contact-sheet capture without a real multi-minute swim.
    ///
    /// <b>Not part of the water contract.</b> The clock's own arithmetic — hysteresis, the cold
    /// curve, the anti-unwinnable sweep — is already proven by <c>WaterChillTests</c> and is not
    /// this method's concern; it exists purely to drive the PRESENTATION layer
    /// (<see cref="ChillCueOverlay"/>, <see cref="ChillReadout"/>,
    /// <see cref="Sail.Game.Water.Fx.ChillChatter"/>), which is what P2 is about. Safe to call with
    /// no avatar provider wired (<see cref="Setup"/> never called) — a lab that only ever reads
    /// <see cref="ChillOf"/> back through the presentation classes above never touches
    /// <see cref="_avatars"/>, so there is nothing here for a real tick to fight with.
    /// </summary>
    public void DebugSetPeerChill(int peerId, float chill)
    {
        if (!_peers.TryGetValue(peerId, out Peer? p))
        {
            p = new Peer();
            _peers[peerId] = p;
        }
        p.Chill = float.IsFinite(chill) ? Mathf.Clamp(chill, 0f, 1f) : 0f;
        Synced = true;
    }

    // --- The server tick ----------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer || _avatars == null)
            return;
        float dt = (float)delta;
        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        foreach (SandboxAvatar avatar in _avatars())
        {
            if (!IsInstanceValid(avatar))
                continue;
            TickPeer(avatar, dt);
        }
    }

    private void TickPeer(SandboxAvatar avatar, float dt)
    {
        int peerId = avatar.OwnerPeerId;
        if (!_peers.TryGetValue(peerId, out Peer? p))
        {
            p = new Peer();
            _peers[peerId] = p;
        }

        Vector3 pos = avatar.GlobalPosition;
        // Read, never author: AvatarMotor already resolved this from the position MoveAndSlide
        // agreed to, using the hysteresis band and the previous state.
        WaterState now = avatar.WaterStateNow;
        float speed = new Vector2(avatar.Velocity.X, avatar.Velocity.Z).Length();

        // --- Transitions ---------------------------------------------------------------------
        // Resolution order is fixed and stated (MECHANICS-BIBLE §3): transition events first,
        // then the clock, then the sputter sequence, then Soaked, then the splash stream. A
        // transition and a chill tick landing on the same frame therefore always resolve the
        // same way, never "whichever system happened to run first".
        if (now != p.State)
        {
            WaterState from = p.State;
            p.State = now;
            BroadcastEvent(now == WaterState.Dry ? WaterEventKind.Exited : WaterEventKind.Entered,
                peerId, pos, from, now, speed);
        }

        // --- The cold clock (spec §5) -----------------------------------------------------------
        // Frozen for the whole of a sputter-out: the episode already committed to its outcome at
        // the moment it began, and letting chill keep moving underneath it would let a recovery
        // land straight back into a second go-under.
        if (p.Sputter.Phase == SputterPhase.None)
        {
            p.Chill = ChillClock.Advance(p.Chill, now, pos.X, dt);
            // The boundary at exactly 1.0 is inclusive, and the Swimming guard is the forgiving
            // half of it (MECHANICS-BIBLE §1): a player who reaches the shelf on the very tick
            // chill tops out is NOT collected — they made it, and chill immediately starts
            // recovering. You can only be taken by the cold while you are still in the deep.
            if (p.Chill >= 1f && now == WaterState.Swimming && p.Sputter.Begin())
                OnWentUnder(peerId, p, pos, speed);
        }
        else
        {
            switch (p.Sputter.Advance(dt))
            {
                case SputterSequence.Step.HardCut:
                    // The relocation and the cut are the same instant. ServerTeleportTo bumps the
                    // avatar's epoch, which is this codebase's existing "snap, never smooth"
                    // mechanism — every peer cuts rather than swooping the camera across the lake.
                    avatar.ServerTeleportTo(ResolveShorePoint(pos));
                    break;
                case SputterSequence.Step.Recovered:
                    // The single moment control returns (spec §6). Chill zeroes here, which is
                    // what forces a second sputter-out to be re-earned from 0.
                    p.Chill = 0f;
                    BroadcastEvent(WaterEventKind.Sputtered, peerId, avatar.GlobalPosition,
                        WaterState.Dry, WaterState.Dry, 0f);
                    break;
            }
        }

        // --- Soaked (spec §7) ---------------------------------------------------------------------
        if (p.Soaked)
        {
            if (IsInCampfireWarmth(avatar.GlobalPosition))
            {
                p.WarmthSec += dt;
                if (p.WarmthSec >= WaterGeometry.SoakedDryOffSec)
                    SetSoaked(peerId, p, false);
            }
            else
            {
                // Non-cumulative on purpose: drying off is 20 s BY the fire, not 20 s of
                // fireside minutes banked across the evening. Stepping away resets it, which is
                // the only reading that makes the fire a destination rather than a checkbox.
                p.WarmthSec = 0f;
            }
        }

        // --- Push the two server-owned facts back onto the replicated MoveState ---------------------
        avatar.ServerSetWaterFlags(p.Soaked, p.Sputter.ControlLocked);

        // --- The thrash splash stream (spec §9) -------------------------------------------------------
        p.SplashCooldown -= dt;
        if (now != WaterState.Dry && p.Sputter.Phase == SputterPhase.None
            && speed >= SplashSpeedThreshold && p.SplashCooldown <= 0f)
        {
            p.SplashCooldown = SplashIntervalSec;
            BroadcastEvent(WaterEventKind.Splash, peerId, pos, now, now, speed);
        }

        MaybeBroadcastPeerState(peerId, p, dt);
    }

    private void OnWentUnder(int peerId, Peer p, Vector3 pos, float speed)
    {
        // The cost, applied exactly once (spec §6, MECHANICS-BIBLE §4). SputterSequence.Begin is
        // the only door in and it is phase-guarded, so this block is unreachable a second time
        // until the episode has fully ended and chill has been re-earned from zero.
        p.TapeRuins++;
        TapeRuinRequested?.Invoke(peerId);
        SetSoaked(peerId, p, true);
        BroadcastEvent(WaterEventKind.WentUnder, peerId, pos, p.State, p.State, speed);
    }

    private void SetSoaked(int peerId, Peer p, bool soaked)
    {
        if (p.Soaked == soaked)
            return;
        p.Soaked = soaked;
        p.WarmthSec = 0f;
        RpcSoaked(peerId, soaked);
    }

    /// <summary>
    /// Where a sputter-out lands. The XZ is pure geometry (<see cref="WaterGeometry.NearestShorePoint"/>);
    /// the Y is refined by a downward ray against the real terrain, because the shore taper's
    /// height is W1's to author and hard-coding it here would be exactly the silent-drift trap
    /// <c>WATER_Y</c> already has a test guarding. Falls back to the pure value when there is no
    /// physics space (a pure-logic test).
    /// </summary>
    private Vector3 ResolveShorePoint(Vector3 from)
    {
        Vector3 shore = WaterGeometry.NearestShorePoint(from);
        PhysicsDirectSpaceState3D? space = GetViewport()?.World3D?.DirectSpaceState;
        if (space == null)
            return shore;

        var query = PhysicsRayQueryParameters3D.Create(
            shore with { Y = 20f }, shore with { Y = -20f });
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0 || !hit.ContainsKey("position"))
            return shore;
        var point = hit["position"].AsVector3();
        // +0.05 so the capsule starts just clear of the surface and settles rather than
        // spawning one frame interpenetrating the bank.
        return float.IsFinite(point.Y) ? point with { Y = point.Y + 0.05f } : shore;
    }

    /// <summary>
    /// True inside a heat source's warmth (spec §7). Requires the source to actually be lit —
    /// standing next to an unlit pile does not dry you off, and reading a radius without reading
    /// the state would have shipped exactly that.
    ///
    /// <para>Answered by the injected <c>warmthAt</c> probe when there is one, so drying off shares
    /// one distance with thawing. With no probe wired — every caller in this build — nowhere is
    /// warm.</para>
    /// </summary>
    private bool IsInCampfireWarmth(Vector3 position) =>
        _warmthAt != null && _warmthAt(position);

    // --- Replication -------------------------------------------------------------------------------

    private void MaybeBroadcastPeerState(int peerId, Peer p, float dt)
    {
        p.SinceBroadcast += dt;
        bool changed = p.State != p.LastSentState
            || p.Soaked != p.LastSentSoaked
            || p.Sputter.Phase != p.LastSentPhase
            || Mathf.Abs(p.Chill - p.LastSentChill) >= BroadcastChillEpsilon;
        if (!changed && p.SinceBroadcast < BroadcastIntervalSec)
            return;
        if (!changed && p.Chill <= 0f && p.LastSentChill <= 0f)
            return; // nothing is happening to this peer; do not heartbeat an idle player

        p.SinceBroadcast = 0f;
        p.LastSentState = p.State;
        p.LastSentChill = p.Chill;
        p.LastSentSoaked = p.Soaked;
        p.LastSentPhase = p.Sputter.Phase;
        Rpc(MethodName.ReceiveWaterPeer, peerId, (int)p.State, p.Chill, p.Soaked, (int)p.Sputter.Phase);
    }

    private void BroadcastEvent(WaterEventKind kind, int peerId, Vector3 pos,
        WaterState from, WaterState to, float speed)
    {
        // Applied locally AND sent, rather than sent and looped back: the server is a real peer
        // in this codebase's hosted-server topology, and an Rpc does not deliver to self.
        ApplyEvent((int)kind, peerId, pos, (int)from, (int)to, speed);
        Rpc(MethodName.ReceiveWaterEvent, (int)kind, peerId, pos, (int)from, (int)to, speed);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.WaterChannel)]
    private void ReceiveWaterEvent(int kind, int peerId, Vector3 pos, int from, int to, float speed)
        => ApplyEvent(kind, peerId, pos, from, to, speed);

    private void ApplyEvent(int kind, int peerId, Vector3 pos, int from, int to, float speed)
    {
        // Defensive by contract, exactly like NetCodec's parsers: a malformed or hostile packet
        // yields a dropped event, never an exception inside a Godot C# network callback (which
        // swallows the rest of that callback's work) and never an out-of-range enum cast.
        if (kind < 0 || kind > (int)WaterEventKind.Sputtered
            || from < 0 || from > (int)WaterState.Swimming
            || to < 0 || to > (int)WaterState.Swimming
            || !float.IsFinite(speed) || !pos.IsFinite())
            return;
        var evt = new WaterEvent(peerId, pos, (WaterState)from, (WaterState)to,
            Mathf.Max(0f, speed));
        switch ((WaterEventKind)kind)
        {
            case WaterEventKind.Entered: Entered?.Invoke(evt); break;
            case WaterEventKind.Exited: Exited?.Invoke(evt); break;
            case WaterEventKind.Splash: Splash?.Invoke(evt); break;
            case WaterEventKind.WentUnder: WentUnder?.Invoke(evt); break;
            case WaterEventKind.Sputtered: Sputtered?.Invoke(evt); break;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
        TransferChannel = NetProfile.WaterChannel)]
    private void ReceiveWaterPeer(int peerId, int state, float chill, bool soaked, int phase)
    {
        WaterPeerSnapshot snap = new WaterPeerSnapshot(
            peerId,
            state is >= 0 and <= (int)WaterState.Swimming ? (WaterState)state : WaterState.Dry,
            chill,
            soaked,
            phase is >= 0 and <= (int)SputterPhase.Recovering ? (SputterPhase)phase : SputterPhase.None)
            .Sanitized();
        AdoptClientSide(snap);
        Synced = true;
    }

    private void RpcSoaked(int peerId, bool soaked)
    {
        SoakedChanged?.Invoke(peerId, soaked);
        Rpc(MethodName.ReceiveSoaked, peerId, soaked);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.WaterChannel)]
    private void ReceiveSoaked(int peerId, bool soaked)
    {
        if (!_peers.TryGetValue(peerId, out Peer? p))
        {
            p = new Peer();
            _peers[peerId] = p;
        }
        if (p.Soaked == soaked)
            return;
        p.Soaked = soaked;
        SoakedChanged?.Invoke(peerId, soaked);
    }

    /// <summary>
    /// Late-join delivery (spec §12: "swim state and chill survive a late-join dump round-trip").
    /// Same call site and the same reasoning as every other dump in <c>Gameplay.OnPeerConnected</c>:
    /// without it, a peer joining a session where somebody is halfway across the lake renders
    /// them standing bolt upright in deep water, and W4's listener never learns a swim is already
    /// under way.
    /// </summary>
    public void SendWaterStateTo(int peerId)
    {
        if (!_isServer)
            return;
        var entries = new List<WaterPeerSnapshot>(_peers.Count);
        foreach (KeyValuePair<int, Peer> kv in _peers)
            entries.Add(new WaterPeerSnapshot(kv.Key, kv.Value.State, kv.Value.Chill,
                kv.Value.Soaked, kv.Value.Sputter.Phase));
        RpcId(peerId, MethodName.ReceiveWaterDump, WaterDump.Pack(entries));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.WaterChannel)]
    private void ReceiveWaterDump(byte[] packet)
    {
        foreach (WaterPeerSnapshot snap in WaterDump.Unpack(packet))
            AdoptClientSide(snap);
        Synced = true;
    }

    /// <summary>
    /// Adopt one authoritative peer snapshot on a client. Shared by the live broadcast and the
    /// late-join dump so both land in exactly the same place — a dump that took a different path
    /// from the live stream is how "it works until someone joins late" bugs get built.
    /// </summary>
    private void AdoptClientSide(in WaterPeerSnapshot snap)
    {
        if (!_peers.TryGetValue(snap.PeerId, out Peer? p))
        {
            p = new Peer();
            _peers[snap.PeerId] = p;
        }
        bool wasSoaked = p.Soaked;
        p.State = snap.State;
        p.Chill = snap.Chill;
        p.Soaked = snap.Soaked;
        if (snap.Phase != p.Sputter.Phase && snap.Phase == SputterPhase.None)
            p.Sputter.Reset();
        if (wasSoaked != snap.Soaked)
            SoakedChanged?.Invoke(snap.PeerId, snap.Soaked);
    }
}
