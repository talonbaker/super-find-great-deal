using Godot;
using MpFoundation.Net;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Props;

/// <summary>
/// One networked physical object as it exists on every peer. Spawned identically on all peers
/// through the <c>PropSpawner</c> (its node name is the server-assigned <see cref="PropId"/>, so
/// every peer agrees on identity), it owns the physical <see cref="Carryable"/> body and applies
/// the authoritative state the server dictates. While Resting it sits frozen at its transform on
/// every machine; the server alone ever simulates or moves it (held items derive from the holder,
/// loose items stream from the server) — clients never simulate prop physics independently, which
/// is what keeps every player's view identical.
/// </summary>
public partial class NetworkedProp : Node3D
{
    public int PropId { get; private set; }
    public PropKind Kind { get; private set; }

    /// <summary>True on the server's instance of this prop only. Set by PropManager right after
    /// spawn. Gates every place only the server may simulate or decide (loose physics, OOB
    /// recovery) — clients always see false and just follow what the server dictates.</summary>
    public bool IsServer { get; set; }

    /// <summary>This prop's spawn transform, captured once in <see cref="Init"/>. The recovery
    /// fallback for a Loose prop that falls out of the world (see PropManager's server loop) —
    /// mirrors Carryable's own home-position OOB fallback for the offline/sandbox path.</summary>
    public Transform3D HomeTransform { get; private set; }

    /// <summary>Peer currently holding this prop, or 0 if none. Set by the net layer.</summary>
    public int HolderPeerId { get; set; }

    /// <summary>The physical prop (mesh + collision + carry entry points). Subclass by Kind.</summary>
    public Carryable Body { get; private set; } = null!;

    // Non-null exactly while server-dictated Held: the lightweight carry binding that chases
    // the holder's anchor every frame. Never null+attached at once with the offline/local
    // CarryController path — a NetworkedProp is only ever driven by the server, not by a local
    // Carry.TryPickUp/Drop.
    private CarryController? _netHolder;

    // Client-only: the latest streamed Loose transform + whether we're currently following one.
    // The server never sets these — it IS the simulation, not a follower of it.
    private Transform3D _netLooseTarget;
    private bool _looseFollowing;

    /// <summary>Called on every peer by the spawn function before the node enters the tree, so the
    /// prop is born with the right id, kind, and place.</summary>
    public void Init(int id, PropKind kind, Transform3D at)
    {
        PropId = id;
        Kind = kind;
        Name = id.ToString();      // identity: same node name on every peer
        Transform = at;            // Props root sits at the origin, so local == world
        HomeTransform = at;
    }

    /// <summary>Adoption path for an AUTHORED prop (see PropManager.AdoptAuthoredProps): called
    /// once on every peer, after <see cref="_Ready"/> has already bound the existing authored
    /// <see cref="Body"/> (this never creates one). Assigns the stable id/kind and captures the
    /// node's CURRENT authored transform as <see cref="HomeTransform"/> — unlike <see cref="Init"/>,
    /// it never moves the node; the level designer's placement in the editor stays authoritative.</summary>
    public void InitAuthored(int id, PropKind kind, Transform3D at)
    {
        PropId = id;
        Kind = kind;
        HomeTransform = at;
    }

    public override void _Ready()
    {
        // Authored path: the .tscn already instances a Carryable (Crate.tscn/Sphere.tscn, with
        // its own authored mesh/collision) as a child literally named "Body" — child nodes'
        // _Ready runs before their parent's, so it already exists and is fully built by the time
        // we get here. Reuse it; never `new` a Carryable over an authored one. Runtime-spawned
        // props (SpawnFromData -> Init, before this node is even in the tree) have no such child
        // yet, so this falls back to building one exactly as before.
        Body = GetNodeOrNull<Carryable>("Body") ?? Kind switch
        {
            PropKind.Ball => new Carryable { Kind = Carryable.Shape.Ball },
            _ => new Carryable { Kind = Carryable.Shape.Crate },
        };
        if (Body.GetParent() == null)
        {
            Body.Name = "Body";
            AddChild(Body);
        }
        // A networked prop owns its own out-of-bounds recovery (PropManager's server loop, using
        // the authoritative HomeTransform) — Carryable's own built-in KillPlaneY safety net (the
        // offline/sandbox path's fallback) must stand down, or the two independently race the
        // same threshold on the same body and fight over where it lands.
        Body.OwnedByNetwork = true;

        // Resting default: frozen kinematic at the prop's place on every peer. The SERVER alone
        // unfreezes and simulates loose props and streams their transforms; clients stay frozen
        // and follow the network, never running divergent local physics. Authored props get this
        // for free too — the level designer never has to set physics flags by hand.
        Body.Freeze = true;
        Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
    }

    /// <summary>Where this prop currently is, for observers/logging. Reads the physical body.</summary>
    public Vector3 WorldPosition => Body?.GlobalPosition ?? GlobalPosition;

    /// <summary>Server-dictated: attach the body to holderAvatar's carry anchor. The body derives
    /// its transform every frame from the anchor thereafter (local holder = predicted avatar,
    /// remote holder = interpolated proxy) — zero per-tick streaming while held. The
    /// <see cref="IsInstanceValid"/> guard closes the freed-holder crash if the holder avatar is
    /// despawned while still bound (e.g. a disconnect race).</summary>
    public void BindToHolder(Node3D holderAvatar)
    {
        HolderPeerId = long.TryParse(holderAvatar.Name.ToString(), out long id) ? (int)id : 0;
        // A grab is legal while the prop is still Loose (chasing down a rolling ball is
        // half the game), so this transition can arrive mid-stream-follow. Stop following
        // here for the same reason Unbind must: with _looseFollowing left true, the stream
        // lerp in _PhysicsProcess keeps dragging the body toward the last (now frozen)
        // loose sample every tick while Carryable's anchor-chase pulls it toward the hand —
        // the held item visibly drifts away from the holder as they walk
        // (Run-RegrabTest.ps1 pins this down).
        _looseFollowing = false;
        _netHolder = new CarryController
        {
            AnchorProvider = () => IsInstanceValid(holderAvatar) && holderAvatar is SandboxAvatar a
                ? a.CarryAnchorGlobalTransform
                : Body.GlobalTransform,
        };
        Body.OnPickedUp(_netHolder); // reuses existing freeze + collision-off + snap-to-anchor
    }

    /// <summary>Server-dictated: detach and rest at <paramref name="restingAt"/>. Every caller —
    /// grab/drop's reliable transition on every peer, AND the server's own settle-latch — must
    /// stop any stream following here, not just in <see cref="SettleToRest"/>: on a plain client,
    /// ApplyPropState's Resting case calls this directly, and if <see cref="_looseFollowing"/>
    /// were left true, <see cref="_PhysicsProcess"/> would immediately lerp the body straight back
    /// toward the last (now stale, and never-updated-again) stream target on the very next tick —
    /// silently undoing the pin below.</summary>
    public void Unbind(Transform3D restingAt)
    {
        HolderPeerId = 0;
        _netHolder = null;
        _looseFollowing = false;
        Body.OnDropped();          // rejoins physics locally, but we immediately pin it below:
        Body.Freeze = true;
        Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        Body.LinearVelocity = Vector3.Zero;
        Body.AngularVelocity = Vector3.Zero;
        Body.GlobalTransform = restingAt;
    }

    /// <summary>Server-only: release into motion with <paramref name="impulse"/> and let physics
    /// take over. Detaches any hold first (throwing something you hold is also a release), then
    /// unfreezes + restores collision via the same OnThrown entry point Carryable already uses
    /// for the offline path — from here PropManager's server loop drives it every physics tick.</summary>
    public void BeginLooseServer(Vector3 impulse)
    {
        HolderPeerId = 0;
        _netHolder = null;
        Body.OnThrown(impulse);
    }

    /// <summary>Every peer: the reliable Held -> Loose transition (via ApplyPropState). Records
    /// the latest transform and, critically, detaches the holder binding — both NetworkedProp's
    /// own (<see cref="_netHolder"/>/<see cref="HolderPeerId"/>) and the underlying Carryable's
    /// (<see cref="Carryable.ReleaseHoldForNetworkFollow"/>), so Carryable's own anchor-chase in
    /// its _PhysicsProcess stops fighting the stream follow below. The body stays frozen kinematic
    /// (never simulates itself on a non-authority peer) and starts following the per-tick stream.
    /// This is the ONLY thing that may turn <see cref="_looseFollowing"/> on — see
    /// <see cref="ApplyLooseStream"/> for why the per-tick stream must never do so itself.</summary>
    public void BeginLoose(Transform3D t)
    {
        HolderPeerId = 0;
        _netHolder = null;
        Body.ReleaseHoldForNetworkFollow();
        _looseFollowing = true;
        _netLooseTarget = t;
        if (!Body.Freeze)
        {
            Body.Freeze = true;
            Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        }
    }

    /// <summary>Client-only: the latest per-tick sample from the unreliable loose-transform
    /// stream (see PropManager.StreamLoose). Deliberately a no-op unless we are ALREADY following
    /// a Loose episode (<see cref="_looseFollowing"/>, turned on only by <see cref="BeginLoose"/>):
    /// the unreliable channel has no ordering guarantee against the reliable one, so a stale
    /// sample from before a settle can arrive AFTER the reliable Resting transition already ran.
    /// Without this guard such a straggler would silently revive following on a target that will
    /// never update again — the prop visibly stuck away from where it actually settled. Every
    /// <see cref="_PhysicsProcess"/> tick then lerps the visible position/rotation toward this
    /// target — smoothing the unreliable, lower-frequency stream instead of popping to each
    /// sample.</summary>
    public void ApplyLooseStream(Transform3D t)
    {
        if (!_looseFollowing)
            return;
        _netLooseTarget = t;
    }

    /// <summary>Server-dictated: this prop has settled — latch to Resting at <paramref name="at"/>.
    /// Reuses <see cref="Unbind"/>'s freeze-and-pin semantics (a resting prop, held or not, ends
    /// up in the same physical state; Unbind itself stops any stream following now too).</summary>
    public void SettleToRest(Transform3D at) => Unbind(at);

    public override void _PhysicsProcess(double delta)
    {
        // Only a CLIENT following a Loose stream does anything here — the server's own prop
        // IS the simulation (RigidBody3D physics runs on it directly, no follow needed), and a
        // prop that isn't currently Loose has nothing to chase.
        if (!_looseFollowing || IsServer)
            return;
        float w = 1f - Mathf.Exp(-25f * (float)delta);
        Body.GlobalPosition = Body.GlobalPosition.Lerp(_netLooseTarget.Origin, w);
        Body.GlobalBasis = Body.GlobalBasis.Orthonormalized().Slerp(_netLooseTarget.Basis.Orthonormalized(), w);
    }
}
