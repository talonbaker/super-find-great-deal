using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Round;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>The jack-in-the-box.</b> The wall between the task room and the seeker's vestibule, which
/// is solid for the whole round and then is not. Both players know it is the only way the round
/// ends; neither knows when.
///
/// <para><b>It is ROUND STATE, not a node with RPCs, and that is the whole architecture.</b>
/// There is no <c>[Rpc]</c> method in this file and there must never be one. The door subscribes
/// to <see cref="HideSeekDriver"/>'s wire on every peer; when the round enters
/// <see cref="HideSeekPhase.Together"/> carrying the server's Found tick, every peer plays the
/// identical staging (<see cref="StartleTimeline"/>) from its own copy of that message. Nothing
/// about "open" is ever requested by a client, and nothing about it is sent by the door.</para>
///
/// <para><b>Why that beats the obvious design</b>, and the reference is
/// <c>Watis_Game@origin/main:scripts/game/world/puffinlab/TinyDoor.cs</c>, which the program names
/// as the thing to read and the thing not to repeat. Its leaf-and-blocker construction is right
/// and is reproduced here; its <c>Open()</c> is LOCAL-ONLY, and its own comment concedes that
/// making it authoritative "means a new networked interactable". A second networked interactable
/// would be a second source of truth about the one instant the whole round hangs on — and a peer
/// that missed the packet would be a player standing in front of a door that, for them, never
/// opened. The wire is ABSOLUTE and already carries the Found tick for exactly this reason
/// (ROUND-1's deviation 5), so a late joiner reads "Together, and T is set" as "the door is
/// already open" with no replay and no catch-up path to write.</para>
///
/// <para><b>What is authored and what is code.</b> Every node this file touches —
/// <see cref="HingeName"/>, <see cref="LeafName"/>, <see cref="BlockerName"/>, the three frame
/// pieces — is authored in <c>TaskRoom.tscn</c> and merely FOUND here, because
/// <c>.claude/rules/godot-scenes.md</c> makes that a hard rule and
/// <c>SupermarketWorldSelfTest</c> enforces it by counting packed nodes against live ones. This
/// class builds nothing, adds no child, and plays its bang through <c>SfxLab</c>'s pooled
/// positional path rather than through an emitter of its own.</para>
///
/// <para><b>Nobody loses input.</b> The leaf is a tween on a mesh with no collider; the only
/// collision this door has is <see cref="BlockerName"/>, which is on or off and never moving. A
/// hinge JOINT would be physically prettier and is refused for the reason §5 states — a fast
/// joint tunnels, and the one thing it would tunnel through is a player. The camera kick is a
/// transient offset on the lens and never touches the replicated aim
/// (<c>FirstPersonCamera.Kick</c>).</para>
/// </summary>
public partial class BurstDoor : Node3D
{
    /// <summary>The node name in <c>TaskRoom.tscn</c>. Named rather than spelled at each lookup,
    /// for the reason <see cref="SupermarketWorld.TaskNodeName"/> is.</summary>
    public const string NodeName = "BurstDoor";

    /// <summary>The leaf's pivot: an empty at the hinge edge of the opening. The leaf mesh hangs
    /// off it, offset by half its own width, so rotating THIS node about +Y swings the leaf.</summary>
    public const string HingeName = "Hinge";

    /// <summary>The leaf itself — a mesh and nothing else. No collider, deliberately: see the
    /// class doc on why this is a tween and not a joint.</summary>
    public const string LeafName = "Leaf";

    /// <summary>The thing that actually stops people: a <see cref="StaticBody3D"/> filling the
    /// opening, whose collision shape is disabled on the burst frame on every peer.</summary>
    public const string BlockerName = "Blocker";

    /// <summary>The task room's lamp, a SIBLING of this node in <c>TaskRoom.tscn</c>. Dipped for
    /// the length of the tell on the hiders' clients only.</summary>
    public const string RoomLightName = "RoomLight";

    /// <summary>Height of the doorway's centre above the room floor, metres. The bang emits from
    /// here and the burst's impulse falls off from here, so it is one number rather than two that
    /// agree. Matches the authored blocker's own centre in <c>TaskRoom.tscn</c>.</summary>
    public const float DoorwayCentreHeightM = 1.1f;

    /// <summary>Volume of the positional bang, dB. Louder than the pool's −6 dB default because
    /// it is a slam in a small concrete room and the whole beat is that it is sudden; the flat
    /// hider-side shot has its own knob (<c>StartleTuning.BangFlatDb</c>) because that one is a
    /// mix decision rather than a loudness one.</summary>
    private const float BangPositionalDb = -1f;

    /// <summary>Pitch jitter on the bang, as a fraction. <b>Zero, and that is deliberate against
    /// this class's own palette convention</b> — <c>SfxLab</c> jitters every one-shot so repeated
    /// footsteps never machine-gun, and this sound happens once per round. A door that was a
    /// slightly different door each time would make the one instant the game is about feel
    /// unreliable.</summary>
    private const float BangPitchJitter = 0f;

    /// <summary>How far the bang carries. The rooms are 40 m apart, so this is comfortably inside
    /// "the search room cannot hear the task room's door" while covering the whole task room.</summary>
    private const float BangMaxDistanceM = 30f;

    private enum DoorStage
    {
        /// <summary>Shut, blocker solid. The whole round, until the find.</summary>
        Shut,

        /// <summary>Between the Found instant and the end of the staging: the tell, the burst,
        /// the leaf's throw and its settle.</summary>
        Staging,

        /// <summary>Staging finished. Leaf at <c>StartleTuning.LeafOpenDeg</c>, blocker off.</summary>
        Open,

        /// <summary>Swinging shut on the reset edge.</summary>
        Closing,
    }

    private Node3D? _hinge;
    private CollisionShape3D? _blockerShape;
    private OmniLight3D? _roomLight;
    private float _roomLightAuthoredEnergy;

    private HideSeekDriver? _driver;
    private bool _subscribed;

    private DoorStage _stage = DoorStage.Shut;

    /// <summary>The Found tick this door has already staged, or −1. The de-duplication identity:
    /// the wire restates <c>FoundTick</c> in every message of the Together phase, so without this
    /// a peer would re-burst on any message it folded twice.</summary>
    private long _stagedFoundTick = -1;

    private double _sinceFound;
    private bool _burstFired;
    private bool _tellFired;
    private double _sinceReset;
    private float _closeFromDeg;

    public override void _Ready()
    {
        _hinge = GetNodeOrNull<Node3D>(HingeName);
        _blockerShape = GetNodeOrNull<StaticBody3D>(BlockerName)?.GetNodeOrNull<CollisionShape3D>("Collision");
        // The lamp is the task room's, not the door's: it is the ROOM that dips. Resolved by name
        // off the parent rather than through an [Export] NodePath, because an [Export] authored on
        // a node inside an instanced sub-scene reads back as its C# default on this Mono build
        // (CARRY-1's measured trap, docs/agents/handoffs/2026-09-19-CARRY-1.md §6) and TaskRoom.tscn
        // is instanced into Supermarket.tscn.
        _roomLight = GetParent()?.GetNodeOrNull<OmniLight3D>(RoomLightName);
        _roomLightAuthoredEnergy = _roomLight?.LightEnergy ?? 0f;

        if (_hinge == null || _blockerShape == null)
        {
            // Loud: a door with no leaf or no blocker is a round that either never ends or has
            // no wall in it, and both read as a broken round rather than as a missing node.
            GD.PushWarning($"[door] {Name}: hinge={_hinge != null} blocker={_blockerShape != null} "
                           + "— the burst door is not wired to its authored parts");
        }

        SetLeafAngle(0f);
        SetBlockerSolid(true);
    }

    public override void _ExitTree()
    {
        Unsubscribe();
    }

    /// <summary>
    /// Per RENDER frame, like every other thing on this door that a player watches. The leaf is a
    /// tween and the tell is a light level; both are things the screen does, and stepping them at
    /// the 60 Hz sim rate on a 144 Hz display would make a 0.12 s throw visibly staircase.
    ///
    /// <para>The burst's two SERVER-side effects (the prop shove and the flinch) ride the same
    /// frame rather than waiting for the next physics tick. That costs nothing: an impulse
    /// written now is integrated by the next tick either way, and splitting the burst across two
    /// clocks would mean the frame the blocker vanished on and the frame the tower went over on
    /// were not the same frame.</para>
    /// </summary>
    public override void _Process(double delta)
    {
        if (!_subscribed)
            TrySubscribe();

        switch (_stage)
        {
            case DoorStage.Shut:
                CatchUpIfAlreadyOpen();
                break;
            case DoorStage.Staging:
                AdvanceStaging(delta);
                break;
            case DoorStage.Closing:
                AdvanceClosing(delta);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The round wire
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Gameplay</c> builds the world BEFORE it builds the driver (see its <c>_Ready</c>), so
    /// this node exists for a beat with nothing to subscribe to — and in
    /// <c>SupermarketWorldSelfTest</c>, which instantiates the room on its own, there is never
    /// anything to subscribe to at all. Polling costs one null check a frame and means neither
    /// case needs a special path.
    /// </summary>
    private void TrySubscribe()
    {
        HideSeekDriver? driver = HideSeekDriver.Instance;
        if (driver == null)
            return;
        _driver = driver;
        driver.Found += OnFound;
        driver.PhaseChanged += OnPhaseChanged;
        driver.ResetRequested += OnResetRequested;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _driver == null)
            return;
        _driver.Found -= OnFound;
        _driver.PhaseChanged -= OnPhaseChanged;
        _driver.ResetRequested -= OnResetRequested;
        _subscribed = false;
        _driver = null;
    }

    /// <summary>
    /// The burst instant, on every peer, from one server message. <paramref name="foundTick"/> is
    /// the SERVER's round tick — an identity, not a local clock: a client never runs
    /// <c>HideSeekLoop.Step</c> and so has no tick series of its own to compare it against. What
    /// every peer shares is this message, so each starts its own copy of the timeline here.
    /// </summary>
    private void OnFound(long foundTick)
    {
        if (!StartleTimeline.IsRealFoundTick(foundTick)
            || StartleTimeline.FoundTickOf(foundTick) == _stagedFoundTick)
        {
            return;
        }
        _stagedFoundTick = StartleTimeline.FoundTickOf(foundTick);
        _stage = DoorStage.Staging;
        _sinceFound = 0.0;
        _burstFired = false;
        _tellFired = false;
        StartleTuning t = StartleTuning.Current;
        GD.Print($"[door] armed foundTick={foundTick} tell={t.TellSec:0.000}s "
                 + $"burstAt={StartleTimeline.BurstAtSec(t):0.000}s peer={Multiplayer.GetUniqueId()} "
                 + $"wall={System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        // TellSec 0 means the door simply goes, so the burst is due on this very frame; run the
        // advance immediately rather than waiting a frame for _Process, or a "no tell" setting
        // would still cost one frame of tell.
        AdvanceStaging(0.0);
    }

    /// <summary>
    /// ROUND-1 teleports the seeker into the vestibule on the same server tick as this phase
    /// change, and the packet asks DOOR-1 to verify they land FACING the door.
    ///
    /// <para><b>The body does and the LOOK does not, so this closes the second half.</b> The
    /// teleport rebuilds the avatar's <c>MoveState</c> from <c>MoveState.AtSpawn</c>, whose yaw is
    /// 0 — and the vestibule marker sits on the door's −Z axis, so a yaw of 0 is precisely
    /// "facing the door". But in first person the body follows the LOOK
    /// (<c>MotorTuning.BodyYawFollowsAim</c>, FP-1 knob 58), and the look is the local camera's,
    /// which no teleport touches: within about 83 ms (<c>AimTurnLerp</c>) the body swings back to
    /// wherever the seeker happened to be looking in the search room. Measured, not reasoned —
    /// and without this line the seeker can land with their back to the thing about to open.</para>
    ///
    /// <para><b>Done HERE, by the door, rather than by adding a facing to the teleport.</b> The
    /// door is the thing the seeker must be facing and the only node that knows where the doorway
    /// is; the alternative threads a yaw through <c>RoomTeleport</c>,
    /// <c>SandboxAvatar.ServerTeleportTo</c> and a new replicated "adopt this facing" signal, and
    /// would re-aim every room change in the game rather than this one. It is a ONE-SHOT write at
    /// the instant of arrival: the player has full control on the very next frame and may look
    /// away immediately, so this is an arrival pose, not a cutscene.</para>
    /// </summary>
    private void OnPhaseChanged(HideSeekPhase from, HideSeekPhase to)
    {
        if (to != HideSeekPhase.Together)
            return;
        FirstPersonCamera? lens = FirstPersonCamera.Local;
        if (lens?.Target is not { } body || _driver is null)
            return;
        if (body.OwnerPeerId != _driver.View.SeekerPeerId)
            return;
        Vector3 toDoor = DoorwayGlobalPosition() - body.GlobalPosition;
        toDoor.Y = 0f;
        if (toDoor.LengthSquared() < 0.0001f)
            return;
        toDoor = toDoor.Normalized();
        // AvatarMotor.ResolveYaw's convention, and AimQuery.DirectionFromYawPitch's inverse:
        // forward is -Z at yaw 0.
        float yaw = Mathf.Atan2(-toDoor.X, -toDoor.Z);
        lens.SetLook(yaw, 0f);
        GD.Print($"[door] seeker arrival: look aimed at the doorway, yaw {Mathf.RadToDeg(yaw):F1} deg");
    }

    private void OnResetRequested()
    {
        if (_stage is DoorStage.Shut or DoorStage.Closing)
            return;
        _closeFromDeg = CurrentLeafAngleDeg();
        _stage = DoorStage.Closing;
        _sinceReset = 0.0;
        _stagedFoundTick = -1;
        GD.Print($"[door] closing from {_closeFromDeg:F1} deg over "
                 + $"{StartleTuning.Current.LeafCloseSec:0.000}s peer={Multiplayer.GetUniqueId()}");
    }

    /// <summary>
    /// <b>The late joiner, and the peer that missed a message.</b> Neither witnessed the
    /// transition, so <see cref="HideSeekDriver.Found"/> deliberately never fires for them
    /// (ROUND-1: bursting a door for a client that arrived after the find would announce a find
    /// it missed). They read the absolute wire instead and find the door already open — no bang,
    /// no kick, no tell, because none of those happened to them.
    /// </summary>
    private void CatchUpIfAlreadyOpen()
    {
        if (_driver is not { Synced: true })
            return;
        HideSeekView view = _driver.View;
        if (view.Phase is not (HideSeekPhase.Together or HideSeekPhase.Tally))
            return;
        if (!StartleTimeline.IsRealFoundTick(view.FoundTick)
            || StartleTimeline.FoundTickOf(view.FoundTick) == _stagedFoundTick)
        {
            return;
        }
        _stagedFoundTick = StartleTimeline.FoundTickOf(view.FoundTick);
        _stage = DoorStage.Open;
        _burstFired = true;
        _tellFired = true;
        SetLeafAngle(StartleTuning.Current.LeafOpenDeg);
        SetBlockerSolid(false);
        GD.Print($"[door] open on arrival (no staging replayed) foundTick={view.FoundTick} "
                 + $"peer={Multiplayer.GetUniqueId()} "
                 + $"wall={System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    // ---------------------------------------------------------------------------------------
    // The staging
    // ---------------------------------------------------------------------------------------

    private void AdvanceStaging(double delta)
    {
        StartleTuning t = StartleTuning.Current;
        _sinceFound += delta;

        if (!_tellFired && t.TellSec > 0f)
        {
            _tellFired = true;
            FireTell(t);
        }

        if (!_burstFired && _sinceFound >= StartleTimeline.BurstAtSec(t))
        {
            _burstFired = true;
            FireBurst(t);
        }

        SetLeafAngle(StartleTimeline.LeafAngleDegAt(_sinceFound, t));

        if (_sinceFound >= StartleTimeline.EndsAtSec(t))
        {
            _stage = DoorStage.Open;
            SetLeafAngle(t.LeafOpenDeg);
        }
    }

    private void AdvanceClosing(double delta)
    {
        StartleTuning t = StartleTuning.Current;
        _sinceReset += delta;
        SetLeafAngle(StartleTimeline.ClosingAngleDegAt(_sinceReset, _closeFromDeg, t));
        if (_sinceReset < t.LeafCloseSec)
            return;
        SetLeafAngle(0f);
        // Solid again at the END of the close, not at the start. The blocker is a static body in
        // a doorway: re-arming it while the leaf was still travelling would be a wall appearing
        // around whoever was standing in it. Nobody is, in practice — the reset edge teleports
        // both players to the holding room on the same tick — but "in practice" is not the same
        // as "cannot", and the order that cannot trap anyone costs 0.6 s of an unblocked doorway
        // in an empty room.
        SetBlockerSolid(true);
        _stage = DoorStage.Shut;
        GD.Print($"[door] closed, blocker solid peer={Multiplayer.GetUniqueId()} "
                 + $"wall={System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    /// <summary>§5 step 2: one small tell, sub-second so it reads as a startle and not a warning.
    /// <b>Hiders only</b> — the seeker is standing on the other side of it and already knows.</summary>
    private void FireTell(in StartleTuning t)
    {
        if (!IsLocalHider())
            return;
        if (_roomLight != null)
            _roomLight.LightEnergy = _roomLightAuthoredEnergy * (1f - t.TellLightDipFraction);
        // The intercom clicking live. Deliberately the same PA chain a talker's voice comes
        // through (overdrive -> 2.2 kHz lowpass -> boxy reverb), because what the hider must read
        // is "the intercom just did something", and a click on a clean bus is a UI sound.
        // Sfx.Thunk, not a new palette member: it is already a short low knock with a filtered
        // noise puff on it, which is a speaker clicking live with a breath of room tone. §5 puts
        // that at "0.2 s"; the recipe's own length is 0.15 s and it is NOT dialled here, because
        // a knob whose only effect would be to pitch-shift a 150 ms sample to hit a duration is
        // a knob that lies about what it does. See the handoff's deviation list.
        SfxLab.PlayUi(Sfx.Thunk, volumeDb: -14f, pitchJitter: 0f,
            bus: MpFoundation.Voice.VoiceManager.EnsurePaBusName());
        GD.Print($"[door] tell: light dipped {t.TellLightDipFraction * 100f:F0}%, intercom click "
                 + $"peer={Multiplayer.GetUniqueId()}");
    }

    /// <summary>§5 step 3, every part of it on one frame.</summary>
    private void FireBurst(in StartleTuning t)
    {
        // 1. The blocker, on EVERY peer. The owner predicts its own movement against its own
        //    copy of the world, so a blocker that only the server dropped would be a seeker who
        //    walks through a door their own client still thinks is shut and is then yanked back
        //    by the next reconciliation.
        SetBlockerSolid(false);

        // 2. The light comes back up with the door, so the dip cannot outlive the thing it was
        //    a tell for (a TellSec of 0 never dipped it, and this is then a harmless re-write).
        if (_roomLight != null)
            _roomLight.LightEnergy = _roomLightAuthoredEnergy;

        // 3. The bang: positional at the doorway on every peer...
        Vector3 doorway = DoorwayGlobalPosition();
        SfxLab.PlayStream3D(this, doorway, SfxLab.Get(Sfx.Bang), BangPositionalDb,
            BangPitchJitter, BangMaxDistanceM);

        // 4. ...and flat on the hiders' own intercom, because a positional-only bang is too quiet
        //    if the hider happens to be facing away (§5).
        bool hider = IsLocalHider();
        if (hider)
        {
            SfxLab.PlayUi(Sfx.Bang, t.BangFlatDb, BangPitchJitter,
                MpFoundation.Voice.VoiceManager.EnsurePaBusName());
            FirstPersonCamera.Local?.Kick(t.KickDegrees, t.KickSec);
        }

        GD.Print($"[door] burst leaf->{t.LeafOvershootDeg:F0}deg blocker=off hider={hider} "
                 + $"peer={Multiplayer.GetUniqueId()} "
                 + $"wall={System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

        // 5. The physical half is the SERVER's alone: it owns prop authority, and a client that
        //    shoved its own copy of a crate would be overwritten by the next stream sample.
        if (!IsServer())
            return;
        PropManager? props = PropManager.Instance;
        if (props == null)
            return;
        int moved = props.ServerBurstImpulse(doorway, t.BurstRadiusM, t.BurstImpulseNs);
        GD.Print($"[door] burst shoved {moved} prop(s) within {t.BurstRadiusM:0.0} m "
                 + $"at {t.BurstImpulseNs:0.0} Ns");
        if (!t.ForceDropOnBurst || _driver == null)
            return;
        int hiderPeer = _driver.View.HiderPeerId;
        if (hiderPeer == 0)
            return;
        // THE FLINCH, through PropManager's existing public release funnel rather than a new one.
        // ScatterHeldBy is the funnel that releases into LOOSE (its sibling ReleaseHeldBy latches
        // to Resting, which is right for a disconnect and wrong here: a dropped object has to
        // fall and tumble where everyone can see it). Nothing new about the prop lifecycle is
        // introduced by the door.
        props.ScatterHeldBy(hiderPeer);
        GD.Print($"[door] burst force-dropped whatever hider {hiderPeer} was holding");
    }

    // ---------------------------------------------------------------------------------------
    // Parts
    // ---------------------------------------------------------------------------------------

    /// <summary>The doorway's centre in world space: this node's own origin, raised to the
    /// opening's centre. Read off the authored transform rather than typed, so moving the door in
    /// the editor moves the bang and the impulse with it.</summary>
    public Vector3 DoorwayGlobalPosition() =>
        GlobalPosition + Vector3.Up * DoorwayCentreHeightM;

    /// <summary>The leaf's current hinge angle in degrees, read back off the node. Used by the
    /// close so it starts from wherever the leaf actually is rather than from where the timeline
    /// says it should be — a reset that lands mid-throw is legal.</summary>
    public float CurrentLeafAngleDeg() => _hinge != null ? Mathf.RadToDeg(_hinge.Rotation.Y) : 0f;

    /// <summary>Whether the blocker is currently stopping anyone. Public because it is the one
    /// fact the smoke and a future reviewer both want and neither can see from the outside.</summary>
    public bool BlockerSolid => _blockerShape != null && !_blockerShape.Disabled;

    private void SetLeafAngle(float degrees)
    {
        if (_hinge == null)
            return;
        Vector3 r = _hinge.Rotation;
        r.Y = Mathf.DegToRad(degrees);
        _hinge.Rotation = r;
    }

    /// <summary>Disabled rather than freed, and rather than moved. Freeing it would make the
    /// reset have to rebuild a node the scene authored (the one thing this level's rules forbid),
    /// and moving it would make a physics body travel through wherever the seeker is standing.</summary>
    private void SetBlockerSolid(bool solid)
    {
        if (_blockerShape != null)
            _blockerShape.Disabled = !solid;
    }

    private bool IsServer() =>
        Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();

    /// <summary>Whether the player at THIS machine is this round's hider. False on a dedicated
    /// server (its unique id is 1 and the roster only ever holds joined clients) and false for
    /// the seeker, which is what makes the tell, the flat bang and the kick hider-side.</summary>
    private bool IsLocalHider() =>
        _driver is { Synced: true } d
        && Multiplayer.HasMultiplayerPeer()
        && d.View.HiderPeerId != 0
        && d.View.HiderPeerId == Multiplayer.GetUniqueId();
}
