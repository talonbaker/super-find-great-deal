using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Contracts;
using MpFoundation.Net;

namespace MpFoundation.Game.Watcher;

/// <summary>
/// The Godot half of the watcher: a shell that feeds <see cref="WatcherBrain"/> the world's
/// state, moves the blockout to wherever the brain says it is standing, and turns its head.
/// Every decision lives in the brain; nothing here decides anything.
///
/// <para><b>Deliberately uncoupled from everything.</b> Peers arrive through a delegate rather
/// than by reaching into the avatar, the lit radius arrives through <see cref="INightPressure"/>
/// rather than by reading the campfire, and exposure arrives through
/// <see cref="IVisibilityScore"/>. That is not architectural politeness — <c>SandboxAvatar.cs</c>,
/// <c>CampWorld.cs</c> and <c>Camp.tscn</c> are all being edited by other packets right now, and
/// this one is required to land in any order relative to them. The hookups are one line each and
/// are listed unapplied in the PR body.</para>
///
/// <para><b>NETWORKED 2026-08-13 (STORMVAULT-WATCH-1). The paragraph that used to sit here said
/// "Not networked... in a session today every client would run its own", and that was true and
/// shipping.</b> It is now server-authoritative: the brain runs on exactly one machine, and every
/// other peer is told what it decided. Concretely —
/// <list type="bullet">
/// <item><b>Server-authoritative (the only writers of any of it are on the server):</b> whether a
/// creature is out at all, which sighting it is, where it is standing, who it is looking at, the
/// body and head yaw, and why a sighting ended. <see cref="WatcherBrain"/>,
/// <see cref="WatcherPlacement"/> and the placement seed run <b>only</b> under
/// <see cref="WatcherNetRole.Server"/> and <see cref="WatcherNetRole.Offline"/>.</item>
/// <item><b>Presentation (per-peer, and with no path back into gameplay):</b> the
/// <see cref="WatcherBlockout"/> mesh, and the client-side easing that walks the rendered angle
/// toward the authoritative one between the 10 Hz facing messages. Nothing a client computes is
/// ever read by a decision, on any peer, and nothing here varies by graphics tier.</item>
/// </list>
/// <b>No behaviour, tuning or feel changed in this pass</b> — the creature's design is parked
/// pending Talon (see <see cref="World.WatcherFlag"/>). The brain, the tuning, the placement, the
/// exit rules and the neck limit are untouched; what changed is how many machines run them.</para>
///
/// <para><b>Why not <c>NetworkedEntity</c>.</b> That base is the right substrate for a
/// continuously-moving server-simulated body and the wrong one for this creature, for four
/// reasons that are structural rather than stylistic. (1) It is a <c>CharacterBody3D</c>; this is
/// a <c>Node3D</c> with no collider, and giving the watcher a physics body so it could inherit a
/// snapshot stream would be a behaviour change — players could bump into it — which this pass is
/// explicitly forbidden to make. (2) Its wire format is <c>MoveState</c>, which has a position, a
/// velocity and one yaw, and no way to express the three facts that actually matter here: that it
/// is present at all, which player it is looking at, and where its head is pointed independently
/// of its body. (3) Its snapshots are unreliable by design, which is correct for a stream that
/// corrects itself twice a tick and wrong for a discrete event that happens once and must not be
/// lost. (4) It has no late-join path — a fresh remote proxy holds at (0,0,0) until a snapshot
/// arrives — whereas a joiner must learn about a creature that appeared before they connected.
/// So the shape reused here is <c>GlowStickManager</c>'s instead, which is the same problem
/// (rare, discrete, reliable, order-dependent state plus a late-join dump) and already the house
/// answer to it: pure state in a Godot-free type (<see cref="WatcherNetState"/>), one node that
/// is only the wire, and no client write path anywhere.</para>
/// </summary>
public partial class Watcher : Node3D
{
    /// <summary>Stable node name so the RPCs below route on every peer.
    /// <c>Gameplay.SetUpWatcher</c> is the only shipping construction site and uses it.</summary>
    public const string NodeName = "Watcher";

    /// <summary><b>The ENet channel this creature's four RPCs ride</b> — 14, which is the number
    /// it has always had.
    ///
    /// <para><b>Why the constant lives here rather than in <c>NetProfile</c>'s ladder.</b> In
    /// <c>Sail</c> this was <c>NetProfile.WatcherChannel</c>. The 2026-09-02 MVP extraction cut
    /// this creature and removed the constant with it, and <c>NetProfile</c>'s ladder comment now
    /// records the consequence in as many words: *"The gaps (7-9, 12, 14, 15) belonged to systems
    /// that were cut from this build; they are left unassigned rather than compacted so the
    /// surviving numbers keep their history. NEXT FREE CHANNEL IS 17."* LEVEL-1 (2026-09-04)
    /// brought the creature back for EGG-2's night gate but does not own <c>scripts/net/**</c>, so
    /// it reclaims 14 from where the system that owns it lives instead of re-opening the ladder.
    /// **14 is not free and must not be handed out**: the ladder says the next free number is 17,
    /// and this constant is why.</para>
    ///
    /// <para>The transport reasoning <c>NetProfile</c> carried for this number is unchanged and is
    /// the one shape none of its neighbours has — it carries BOTH orderings at once. Appear and
    /// gone are reliable, rare and strictly ordered relative to each other (an <c>ApplyGone</c>
    /// that overtook its own <c>ApplySighting</c> would leave a creature standing in the dark
    /// forever), while the facing stream between them is an unreliable trickle that is allowed to
    /// drop. Sharing <c>NetProfile.SightChannel</c> would put the reliable pair behind a permanent
    /// 5 Hz drip.</para>
    ///
    /// <para><b>No protocol bump</b>, on the same reasoning the original carried: these are
    /// brand-new methods on a node that is flag-gated off by default
    /// (<c>WatcherFlag.WatcherSpawns</c>), and no existing message changes shape. The
    /// mismatched-build case is a launch-option mismatch inside one binary, which fails loudly on
    /// the receiver (Godot logs an unresolved RPC path) rather than silently-wrong. Worth a bump
    /// the day the flag ships on — called out here rather than discovered then.</para></summary>
    public const int NetChannel = 14;

    /// <summary>Single instance per running game, the same convention as
    /// <c>GlowStickManager.Instance</c> and <c>CycleDriver.Instance</c>, and for the same reason:
    /// <c>BotHarness</c> polls it once per sample so a headless suite can compare two peers' views
    /// of the creature without either of them having a reference handed to it. Null in every
    /// default session (the flag is off) and in any world with no campfire.</summary>
    public static Watcher? Instance { get; private set; }

    /// <summary>How often the server publishes the angle while a sighting is live. 10 Hz: the head
    /// turns at <see cref="WatcherTuning.TurnDegPerSec"/> (38 deg/s by default), so one interval is
    /// under four degrees of travel, and a dropped packet costs a tenth of a second of staleness
    /// that the next one corrects. Deliberately NOT a per-frame stream — the creature does not
    /// move, so there is nothing here that wants snapshot cadence.</summary>
    private const float FacingIntervalSec = 0.1f;

    /// <summary>How much faster than the server a client is allowed to close the angle it is
    /// behind by. Above 1 so the rendered head genuinely converges on the authoritative one
    /// instead of trailing it forever; low enough that catching up still reads as a turn rather
    /// than a snap. Presentation only — see the class remarks.</summary>
    private const float ClientCatchUpFactor = 1.6f;

    /// <summary>Where the light comes from, and how far it reaches. Without one, nothing appears
    /// — a watcher with no idea where lit ground is could stand in it. Server/offline only.</summary>
    public INightPressure? NightPressure { get; set; }

    /// <summary>Exposure per peer. Without one, nothing appears. Server/offline only.</summary>
    public IVisibilityScore? Visibility { get; set; }

    /// <summary>Who is out there: peer ids with their positions and facings. Exposure is filled
    /// in from <see cref="Visibility"/>, so a provider only supplies geometry. Server/offline only
    /// — a client is never asked, because a client never decides.</summary>
    public Func<IReadOnlyList<(int PeerId, Vector3 Position, Vector3 Forward)>>? PeerSource { get; set; }

    /// <summary>Centre of the lit ground. The campfire, in the camp.</summary>
    public Vector3 FireOrigin { get; set; } = Vector3.Zero;

    /// <summary>Terrain height sampler, so it stands on the ground rather than at the fire's
    /// altitude. Defaults to the fire's own height, which is right on flat ground and visibly
    /// wrong on a slope — supply the real one. Server/offline only: a client is TOLD the height
    /// as part of the stand position and never samples anything.</summary>
    public Func<float, float, float>? GroundHeight { get; set; }

    public WatcherTuning Tuning { get; set; } = WatcherTuning.Default;

    /// <summary>Which half of the session this instance is. <see cref="WatcherNetRole.Offline"/>
    /// until <see cref="Setup"/> says otherwise, so the labs and the brain tests keep the exact
    /// code path they were written against.</summary>
    public WatcherNetRole Role { get; private set; } = WatcherNetRole.Offline;

    /// <summary>This peer's converged view. On the server it is the authority's own record of what
    /// the brain decided; on a client it is whatever the server last said.</summary>
    public WatcherNetState Net { get; } = new();

    /// <summary><b>Every peer agrees on this.</b> Server/offline: the brain's own state. Client:
    /// the replicated one. Reading the brain on a client would be reading a machine that is never
    /// ticked — permanently <see cref="WatcherState.Absent"/> — which is exactly the silently
    /// plausible wrong answer this whole pass exists to remove.</summary>
    public WatcherState State => Role == WatcherNetRole.Client
        ? (Net.Present ? WatcherState.Watching : WatcherState.Absent)
        : _brain.State;

    /// <summary>Who it is looking at, or -1. Replicated; see <see cref="State"/>.</summary>
    public int TargetPeerId => Role == WatcherNetRole.Client ? Net.TargetPeerId : _brain.TargetPeerId;

    /// <summary>Why the last sighting ended. Replicated; see <see cref="State"/>.</summary>
    public WatcherExit LastExit => Role == WatcherNetRole.Client ? Net.LastExit : _brain.LastExit;

    /// <summary>Where it is standing, as this peer understands it. Server/offline reads the brain,
    /// a client reads what it was told — one property, one answer, whoever asks.</summary>
    public Vector3 StandPosition => Role == WatcherNetRole.Client ? Net.StandPosition : _brain.StandPosition;

    /// <summary>False on a client until its late-join dump lands. See
    /// <see cref="WatcherNetState.Synced"/> for why "no creature" and "not told yet" must not be
    /// the same answer.</summary>
    public bool Synced => Net.Synced;

    public WatcherBlockout Blockout { get; private set; } = null!;

    private readonly WatcherBrain _brain;
    private readonly List<WatcherPeerView> _peers = new();
    private uint _seed = 0x9E3779B9;
    private float _bodyYaw;
    private float _headYaw;          // world-space; the local head rotation is the difference
    private WatcherState _lastState = WatcherState.Absent;
    private float _sinceFacingBroadcast;

    /// <summary>How far the neck will twist before the body has to come round with it. Past this
    /// the head is no longer selling a turn, it is selling an owl.</summary>
    private const float NeckLimitDeg = 62f;

    public Watcher() => _brain = new WatcherBrain(Tuning);

    public override void _Ready()
    {
        Instance = this;
        Blockout = new WatcherBlockout { Name = "Blockout" };
        AddChild(Blockout);
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Called once by <c>Gameplay.SetUpWatcher</c>, the same shape as
    /// <c>GlowStickManager.Setup</c> and <c>PlayerSightService.Setup</c>. The server and an offline
    /// boot are their own authority, so they are <see cref="Synced"/> from here on; a client stays
    /// unsynced until its dump lands.</summary>
    public void Setup(WatcherNetRole role)
    {
        Role = role;
        if (role != WatcherNetRole.Client)
            Net.MarkSynced();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (Role == WatcherNetRole.Client)
        {
            RenderReplicated(dt);
            return;
        }

        if (NightPressure == null || Visibility == null || PeerSource == null)
            return;

        GatherPeers();
        _brain.Tick(dt, _peers, FireOrigin, NightPressure.LitRadiusM, ChooseStand);

        if (_brain.State != _lastState)
        {
            OnStateChanged();
            _lastState = _brain.State;
        }
        if (_brain.State == WatcherState.Watching)
        {
            FaceTarget(dt);
            ServerPublishFacing(dt);
        }
    }

    private void GatherPeers()
    {
        _peers.Clear();
        IReadOnlyList<(int PeerId, Vector3 Position, Vector3 Forward)> raw = PeerSource!();
        for (int i = 0; i < raw.Count; i++)
            _peers.Add(new WatcherPeerView(raw[i].PeerId, Visibility!.VisibilityFor(raw[i].PeerId),
                                           raw[i].Position, raw[i].Forward));
    }

    private bool ChooseStand(in WatcherPeerView target, float litRadiusM, out Vector3 stand)
    {
        // Advanced per attempt, not per sighting, so a refused attempt does not retry the same
        // spot on the next frame and quietly become a fixed spawn point.
        //
        // THIS SEED IS THE REASON THE FEATURE COULD NOT SELF-CONVERGE. It is per-instance and
        // advanced once per attempt, so two peers that had refused a different NUMBER of attempts
        // — which they always had, because they run at different frame rates against differently
        // interpolated avatars — would pick different points out of the same admissible set. That
        // is not a rounding disagreement that shrinks with better inputs; it is a different place.
        // Now it only ever turns on the server.
        _seed = _seed * 1664525u + 1013904223u;
        Func<float, float, float> ground = GroundHeight ?? ((_, _) => FireOrigin.Y);
        return WatcherPlacement.TryChoose(FireOrigin, litRadiusM, target, _peers, Tuning, ground,
                                          _seed, out stand);
    }

    private void OnStateChanged()
    {
        if (_brain.State == WatcherState.Watching)
        {
            GlobalPosition = _brain.StandPosition;
            // Snapped, not eased. It was not seen arriving, so it has been standing there for as
            // long as the player is concerned, and easing into a facing would say otherwise.
            _headYaw = _bodyYaw = CurrentDesiredYaw();
            ApplyYaw();
            Visible = true;
            ServerAnnounceSighting();
            return;
        }
        Visible = false;
        ServerAnnounceGone();
    }

    private float CurrentDesiredYaw()
    {
        for (int i = 0; i < _peers.Count; i++)
            if (_peers[i].PeerId == _brain.TargetPeerId)
                return WatcherFacing.YawToward(_brain.StandPosition, _peers[i].Position);
        return _bodyYaw;
    }

    /// <summary>The head leads and the body follows it round. Two rates rather than one because
    /// the head turn is the readout: a whole-body swivel at neck speed reads as a prop rotating,
    /// and a body that never comes round reads as a broken one.</summary>
    private void FaceTarget(float dt)
    {
        float desired = CurrentDesiredYaw();
        _headYaw = WatcherFacing.StepYaw(_headYaw, desired, Mathf.DegToRad(Tuning.TurnDegPerSec) * dt);

        float neck = Mathf.Wrap(_headYaw - _bodyYaw, -Mathf.Pi, Mathf.Pi);
        float limit = Mathf.DegToRad(NeckLimitDeg);
        if (Mathf.Abs(neck) > limit)
        {
            float bodyTarget = _headYaw - Mathf.Sign(neck) * limit;
            _bodyYaw = WatcherFacing.StepYaw(_bodyYaw, bodyTarget,
                                             Mathf.DegToRad(Tuning.TurnDegPerSec * 0.45f) * dt);
        }
        ApplyYaw();
    }

    private void ApplyYaw()
    {
        Rotation = new Vector3(0, _bodyYaw, 0);
        if (Blockout?.Head != null)
            Blockout.Head.Rotation = new Vector3(0, Mathf.Wrap(_headYaw - _bodyYaw, -Mathf.Pi, Mathf.Pi), 0);
    }

    // --- Server: publish ------------------------------------------------------------------------
    //
    // The server has ALREADY applied every one of these to its own state by the time it sends
    // them (the brain wrote them; Net records them), so CallLocal is off throughout — exactly the
    // split GlowStickManager's own message block documents. These messages exist to bring remotes
    // to where the authority already is, not to be the authority's own funnel.

    private void ServerAnnounceSighting()
    {
        if (Role != WatcherNetRole.Server)
            return;
        int id = Net.ServerBeginSighting(_brain.StandPosition, _brain.TargetPeerId, _bodyYaw, _headYaw);
        _sinceFacingBroadcast = 0f;
        // Said out loud on the server for the same reason Gameplay's [watcher-gate] line is: a
        // suite that cannot tell "the creature never came out" from "the creature came out and did
        // not replicate" is a suite whose green means nothing. Run-WatcherNetTest.ps1 reads this.
        GD.Print($"[watcher] sighting {id} target={_brain.TargetPeerId} " +
                 $"stand=({_brain.StandPosition.X:F2},{_brain.StandPosition.Y:F2},{_brain.StandPosition.Z:F2})");
        Rpc(MethodName.ApplySighting, id, _brain.StandPosition, _brain.TargetPeerId, _bodyYaw, _headYaw);
    }

    private void ServerAnnounceGone()
    {
        if (Role != WatcherNetRole.Server)
            return;
        int id = Net.ServerEndSighting(_brain.LastExit);
        if (id == 0)
            return; // nothing was live — a state change into Absent from Absent cannot happen, but
                    // saying so structurally costs nothing and a stray broadcast would be a lie.
        GD.Print($"[watcher] gone {id} exit={_brain.LastExit}");
        Rpc(MethodName.ApplyGone, id, (int)_brain.LastExit);
    }

    private void ServerPublishFacing(float dt)
    {
        Net.ServerSetFacing(_bodyYaw, _headYaw);
        if (Role != WatcherNetRole.Server)
            return;
        _sinceFacingBroadcast += dt;
        if (_sinceFacingBroadcast < FacingIntervalSec)
            return;
        _sinceFacingBroadcast = 0f;
        Rpc(MethodName.ApplyFacing, Net.SightingId, _bodyYaw, _headYaw);
    }

    /// <summary>Server-only: the late-join dump, called by <c>Gameplay.OnPeerConnected</c> beside
    /// every other system's. Sent whether or not a creature is currently out — "nothing is out
    /// there" is news a joiner needs as much as the other answer, and it is what releases that
    /// peer from <see cref="Synced"/> false.
    ///
    /// <para>The repo's shipped failure mode is a late joiner syncing to a zero default rather
    /// than to live state, and this one's zero default is unusually convincing: a joiner with no
    /// dump reads "no creature", which is the most common true answer, so the bug would be
    /// invisible until the one night it mattered.</para></summary>
    public void SendSyncTo(int peerId)
    {
        if (Role != WatcherNetRole.Server)
            return;
        RpcId(peerId, MethodName.SyncWatcher, Net.Present, Net.SightingId, Net.StandPosition,
              Net.TargetPeerId, Net.BodyYaw, Net.HeadYaw, (int)Net.LastExit);
    }

    // --- Client: receive and render --------------------------------------------------------------
    //
    // Appear/gone/dump are RELIABLE: each happens once and a lost one is wrong until the next
    // sighting, which can be a minute away. The facing stream is UNRELIABLE: it is a continuous
    // correction, so a dropped one costs FacingIntervalSec of staleness and the next one fixes it.
    // Mixing the two ordering guarantees on one channel is exactly why WatcherNetState carries the
    // sighting-id guards it does — see that type's remarks.

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetChannel)]
    private void ApplySighting(int sightingId, Vector3 stand, int targetPeerId, float bodyYaw, float headYaw)
        => Net.ApplyAppear(sightingId, stand, targetPeerId, bodyYaw, headYaw);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
        TransferChannel = NetChannel)]
    private void ApplyFacing(int sightingId, float bodyYaw, float headYaw)
        => Net.ApplyFacing(sightingId, bodyYaw, headYaw);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetChannel)]
    private void ApplyGone(int sightingId, int exit)
        => Net.ApplyGone(sightingId, (WatcherExit)exit);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetChannel)]
    private void SyncWatcher(bool present, int sightingId, Vector3 stand, int targetPeerId,
                             float bodyYaw, float headYaw, int lastExit)
        => Net.ApplyDump(present, sightingId, stand, targetPeerId, bodyYaw, headYaw, (WatcherExit)lastExit);

    /// <summary>A client's whole per-frame job: put the body where the server said and walk the
    /// rendered angle toward the angle the server said. No brain, no placement, no seed, no
    /// exposure — a client cannot produce a sighting even in principle, which is the property that
    /// makes "every peer sees the same creature" structural rather than a hope about determinism.
    ///
    /// <para>The ease is presentation, and it is bounded: the client closes at
    /// <see cref="ClientCatchUpFactor"/>x the server's own turn rate, so the rendered angle trails
    /// the authoritative one by at most one broadcast interval's worth of turn (under four degrees
    /// at the default tuning) and converges exactly whenever the head is still. Position is never
    /// eased — the creature does not move, so anything that looked like movement would be a lie.</para></summary>
    private void RenderReplicated(float dt)
    {
        if (!Net.Present)
        {
            if (Visible)
                Visible = false;
            return;
        }

        if (Net.TakeAppeared())
        {
            GlobalPosition = Net.StandPosition;
            _bodyYaw = Net.BodyYaw;
            _headYaw = Net.HeadYaw;   // snapped, for OnStateChanged's reason: it was not seen arriving
            ApplyYaw();
            Visible = true;
            return;
        }

        // A sighting whose position moved under us can only be a dump landing mid-sighting; take it
        // rather than sliding, for the same reason the appear snaps.
        if (!GlobalPosition.IsEqualApprox(Net.StandPosition))
            GlobalPosition = Net.StandPosition;

        float step = Mathf.DegToRad(Tuning.TurnDegPerSec * ClientCatchUpFactor) * dt;
        _bodyYaw = WatcherFacing.StepYaw(_bodyYaw, Net.BodyYaw, step);
        _headYaw = WatcherFacing.StepYaw(_headYaw, Net.HeadYaw, step);
        ApplyYaw();
        if (!Visible)
            Visible = true;
    }

    /// <summary>Drives one step with an explicit peer list. The lab's way in; it lets a scripted
    /// case run the same code path <see cref="_Process"/> does without a live world.</summary>
    public void StepForLab(float dt, IReadOnlyList<WatcherPeerView> peers, float litRadiusM)
    {
        _peers.Clear();
        for (int i = 0; i < peers.Count; i++)
            _peers.Add(peers[i]);

        _brain.Tick(dt, _peers, FireOrigin, litRadiusM, ChooseStand);
        if (_brain.State != _lastState)
        {
            OnStateChanged();
            _lastState = _brain.State;
        }
        if (_brain.State == WatcherState.Watching)
        {
            FaceTarget(dt);
            ServerPublishFacing(dt);
        }
    }

    /// <summary>Puts it at a chosen spot facing a chosen point, for the silhouette captures. Does
    /// not touch the brain — a still life, not a sighting.</summary>
    public void PoseForLab(Vector3 at, Vector3 lookAt)
    {
        GlobalPosition = at;
        _headYaw = _bodyYaw = WatcherFacing.YawToward(at, lookAt);
        ApplyYaw();
        Visible = true;
    }
}
