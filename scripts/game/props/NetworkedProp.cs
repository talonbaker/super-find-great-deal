using Godot;
using MpFoundation.Net;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.Sandbox.Feel;
using MpFoundation.Game.Presentation;

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

    /// <summary>
    /// <b>The last transform this prop was known to be somewhere legal</b> — the newest place or
    /// Resting latch that passed <see cref="PlacementIntegrity.Check"/> (program doc §5b). Seeded
    /// to <see cref="HomeTransform"/> so it is never empty and never the world origin.
    ///
    /// <para>CARRY-1 only RECORDS it. REACH-1 is what teleports a prop back here when the
    /// rest-time audit (layer 2) cannot depenetrate it — and the reason the fallback is this and
    /// not spawn is that a hidden object returning to the rack in the middle of a round is a
    /// worse outcome than one sitting a few centimetres off where its hider put it.</para>
    ///
    /// <para>Server-authoritative in practice: the value is only ever written on the peer that
    /// runs the check, which is the server. A client's copy stays at the spawn transform and
    /// nothing reads it there.</para>
    /// </summary>
    public Transform3D LastGoodTransform { get; private set; }

    /// <summary>Server: record that this prop passed placement integrity at
    /// <paramref name="at"/>. Called from the place RPC and from the settle latch — the two
    /// moments a prop's position is decided rather than merely observed.</summary>
    public void NoteLastGood(Transform3D at) => LastGoodTransform = at;

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

    // --- The holder's own view of a held prop (CARRY-1) ------------------------------------
    //
    // Non-null on exactly ONE peer: the one whose local player is holding this prop. Every other
    // peer keeps deriving the held transform from the holder's carry anchor through
    // CarryController, exactly as before. The mismatch between the two views is the spring's lag
    // and is documented in the CARRY-1 handoff, measured, rather than hidden.
    private CarrySpring? _spring;
    private SandboxAvatar? _springHolder;

    /// <summary>How far a full-heft item sags below the hand anchor, metres — the feel system's
    /// own <c>Interactor.CarryDroop</c> default. Here rather than on <see cref="CarrySpring"/>
    /// because the droop is a fact about where the HAND is, not about how the spring chases it;
    /// the lab keeps its copy in <c>Interactor.HandAnchor</c> for the same reason.</summary>
    private const float CarryDroopM = 0.11f;

    /// <summary>Called on every peer by the spawn function before the node enters the tree, so the
    /// prop is born with the right id, kind, and place.</summary>
    public void Init(int id, PropKind kind, Transform3D at)
    {
        PropId = id;
        Kind = kind;
        Name = id.ToString();      // identity: same node name on every peer
        Transform = at;            // Props root sits at the origin, so local == world
        HomeTransform = at;
        LastGoodTransform = at;
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
        LastGoodTransform = at;
    }

    public override void _Ready()
    {
        // Authored path: the .tscn already instances a Carryable (Crate.tscn/Sphere.tscn, with
        // its own authored mesh/collision) as a child literally named "Body" — child nodes'
        // _Ready runs before their parent's, so it already exists and is fully built by the time
        // we get here. Reuse it; never `new` a Carryable over an authored one. Runtime-spawned
        // props (SpawnFromData -> Init, before this node is even in the tree) have no such child
        // yet, so this falls back to building one exactly as before.
        // Carryable.Shape mirrors PropKind 1:1 by construction — both enums say so, and SFX-1's
        // three appended members kept the mirror — so this is one cast rather than a switch that
        // has to be remembered on every append. The `Material` that rides with it is what makes
        // a code-built (--seed-test-props) can sound like tin: an authored prefab resolves its
        // material from its collider instead, because a nested PackedScene instance's exported
        // script properties are not applied on this build (see Carryable.Material).
        Carryable.Shape shape = System.Enum.IsDefined(typeof(Carryable.Shape), (int)Kind)
            ? (Carryable.Shape)(int)Kind
            : Carryable.Shape.Crate;
        Body = GetNodeOrNull<Carryable>("Body")
            ?? new Carryable { Kind = shape, Material = Carryable.MaterialFor(shape) };
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

        // The feel component, resolved once (see _feel). Child _Ready runs before the parent's,
        // so an authored prop's Interactable is already bound to this body by now.
        foreach (Node child in Body.GetChildren())
        {
            if (child is Interactable found)
            {
                _feel = found;
                break;
            }
        }
    }

    /// <summary>Where this prop currently is, for observers/logging. Reads the physical body.</summary>
    public Vector3 WorldPosition => Body?.GlobalPosition ?? GlobalPosition;

    /// <summary>Server-dictated: attach the body to holderAvatar's carry anchor. The body derives
    /// its transform every frame from the anchor thereafter (local holder = predicted avatar,
    /// remote holder = interpolated proxy) — zero per-tick streaming while held. The
    /// <see cref="IsInstanceValid"/> guard closes the freed-holder crash if the holder avatar is
    /// despawned while still bound (e.g. a disconnect race).</summary>
    /// <param name="springOnThisPeer">True on the ONE peer whose local player is the holder: the
    /// prop is then carried by the feel system's critically damped spring
    /// (<see cref="CarrySpring"/>) instead of by the anchor chase, which is what makes it read as
    /// held in the hand rather than welded to a socket. Every other peer passes false and is
    /// unchanged. See <see cref="BindToHolderSpring"/>.</param>
    public void BindToHolder(Node3D holderAvatar, bool springOnThisPeer = false)
    {
        HolderPeerId = long.TryParse(holderAvatar.Name.ToString(), out long id) ? (int)id : 0;
        if (springOnThisPeer && holderAvatar is SandboxAvatar springHolder)
        {
            BindToHolderSpring(springHolder);
            return;
        }
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

    /// <summary>
    /// <b>The holder's own view of what they are carrying</b> (CARRY-1): the feel system's spring,
    /// driving this prop's frozen kinematic body toward the holder's hand every physics tick.
    ///
    /// <para><b>Why the holder alone.</b> A spring is a LOCAL prediction of a server-authoritative
    /// fact — "peer N holds prop P". Running it on every peer would mean every peer integrating a
    /// slightly different spring against a slightly different interpolated anchor, so no two
    /// players would agree where the crate is, for a purely cosmetic gain. Running it on the
    /// holder alone costs one view-to-view mismatch (the spring's lag, measured in the handoff)
    /// and buys the one thing the carry was rebuilt for: on the screen of the person holding it,
    /// the object has weight.</para>
    ///
    /// <para><b>No snap to the anchor, deliberately.</b> <see cref="Carryable.OnPickedUp"/> places
    /// the prop AT the hand on the grab frame, which is right for a chase that would otherwise
    /// visibly slide the prop up off the floor. It is wrong for a spring: seeding at the item and
    /// letting it travel is the documented anti-pop (<see cref="CarrySpring.Seed"/>), and it is
    /// the one frame in which the grab reads. So this takes the freeze/collision/cue half of the
    /// pickup (<see cref="Carryable.OnPickedUpBySpring"/>) and leaves the placement to the
    /// spring.</para>
    /// </summary>
    private void BindToHolderSpring(SandboxAvatar holder)
    {
        _looseFollowing = false;
        _netHolder = null;
        _springHolder = holder;
        _spring = new CarrySpring();
        Body.OnPickedUpBySpring();
        _spring.Seed(Body.GlobalTransform, HandAnchor());
    }

    /// <summary>Where the hand is THIS tick, for the spring: the holder's replicated carry mount,
    /// raised by the load lift an armful gets, nudged by the item's own hold offset, and dropped
    /// by the droop its heft earns. Identical in shape to <c>Interactor.HandAnchor</c>; the inputs
    /// differ because the anchor here is a replicated transform rather than a lab rig.</summary>
    private Vector3 HandAnchor()
    {
        if (_springHolder == null || !IsInstanceValid(_springHolder))
            return Body.GlobalPosition;
        Transform3D mount = Body.EffectiveCarryAnchor(_springHolder.CarryAnchorGlobalTransform);
        Interactable? it = _feel;
        Vector3 offset = it?.HoldOffset ?? Vector3.Zero;
        return mount.Origin + mount.Basis * offset - Vector3.Up * (CarryDroopM * SpringHeft);
    }

    /// <summary>The feel-system component on this prop's body, if it has one. Every prop in this
    /// game is authored with one (CARRY-1 packet item 6); the null path is the code-built CI
    /// fallback, which has no <c>.tscn</c> to carry one.
    ///
    /// <para><b>Resolved once, in <see cref="_Ready"/>, not per access.</b> The carry reads it
    /// three times a physics tick (the anchor, the heft, the hold rotation) and
    /// <c>Node.GetChildren()</c> allocates a <c>Godot.Collections.Array</c> every call — 180
    /// native allocations a second for a component that cannot change. <c>Interactor</c>'s own
    /// header records what that costs when it is left alone.</para></summary>
    private Interactable? _feel;

    /// <summary>0 for weightless, 1 at or above the spring's mass reference. Reads the
    /// <see cref="Interactable"/>'s heft where there is one — ONE number for how heavy a thing is,
    /// so a prop that lags in the hand also thuds when it lands — and the body's own mass
    /// otherwise.</summary>
    private float SpringHeft =>
        _spring?.HeftOf(_feel?.HeftKg ?? Body.MassKg) ?? 0f;

    /// <summary>The holder's live spring-vs-anchor gap in metres, or 0 when this peer is not the
    /// holder. Instrumentation only — BotHarness samples it so the mismatch the handoff reports is
    /// a measurement rather than an estimate.</summary>
    public float SpringLagM => _spring != null ? _spring.LagTo(HandAnchor()) : 0f;

    /// <summary>True while this peer is the one carrying this prop on the spring.</summary>
    public bool SpringActive => _spring != null;

    /// <summary>Drop the holder-side spring. Every transition out of Held runs through here, and
    /// it must, or the spring keeps writing this body's transform every tick underneath whatever
    /// the network says is happening to it.</summary>
    private void ClearSpring()
    {
        if (_spring == null)
            return;
        _spring = null;
        _springHolder = null;
        Body.ReleaseHoldForNetworkFollow();
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
        ClearSpring();
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
        ClearSpring();
        Body.OnThrown(impulse);
    }

    /// <summary>Server-only, test fixture (<c>--seed-props-drop</c>): let a seeded prop FALL from
    /// where it was seeded, with no impulse and no spin. The <c>Loose</c> broadcast that goes with
    /// it is the ordinary one, so every peer follows the stream exactly as it would for a throw;
    /// what differs is only that nothing threw it. See <c>PropManager.StepSeededDrop</c>.</summary>
    public void DropLooseServer()
    {
        HolderPeerId = 0;
        _netHolder = null;
        ClearSpring();
        Body.ReleaseAtRestServer();
    }

    /// <summary>
    /// Server-only: <b>the place verb's release</b> — put the prop exactly at
    /// <paramref name="at"/> and hand it to physics with no linear velocity and no spin, so it
    /// settles where it was set down rather than tumbling off it.
    ///
    /// <para>Loose rather than straight to Resting, and that is the point: a placed prop still has
    /// to fall the last centimetre onto whatever is under it and still has to latch through the
    /// server's own settle loop, so a placement that ends up balanced on a rolling ball behaves
    /// like physics rather than like a decision. What "place" removes is the DROP's toss arc and
    /// <see cref="Carryable.OnThrown"/>'s random tumble spin — both of which exist to make a
    /// discarded object look discarded, and both of which would undo the orientation the player
    /// just spent a rotate-and-line-up on.</para>
    /// </summary>
    public void PlaceLooseServer(Transform3D at)
    {
        HolderPeerId = 0;
        _netHolder = null;
        ClearSpring();
        Body.GlobalTransform = at;
        Body.OnPlaced();
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
        ClearSpring();
        Body.ReleaseHoldForNetworkFollow();
        // SFX-1: THE RELEASE, ANNOUNCED WHERE EVERY PEER CAN HEAR IT.
        //
        // This is the every-peer half of Held -> Loose (ApplyPropState is CallLocal), and it is
        // the only place in that transition that runs everywhere. Carryable.OnThrown looks like
        // the natural home and is not: it is reached only from BeginLooseServer, so a throw
        // announced there is a throw only the host hears, and on the remote player's screen a
        // can leaves a hand in silence. Measured, not assumed — the first run of
        // tests/Run-MaterialSfxTest.ps1 logged the pickups on a client and none of the throws.
        //
        // A PLACE ALSO ARRIVES HERE, and a peer cannot tell it from a throw. That is a fact
        // about the wire rather than a shortcut: CARRY-1's own handoff records that "at
        // ApplyPropState a place and a drop are the same Held->Loose transition and are
        // indistinguishable there". Separating them costs one bit on that RPC. Until somebody
        // spends it, every release plays the same brief shared Whoosh, which is the one sound in
        // this palette that is a fact about the ARM rather than about the object — so it is the
        // least wrong thing to play when the object's own verb is unknown. The deliberate
        // set-down tick (ActorEvent.Placed) stays on the server's PlaceLooseServer path, where
        // the verb IS known, and is therefore host-only today. Written down rather than hidden.
        // `this`, NOT GetParent(). Carryable's own fires pass ITS parent, which is this node —
        // so passing this node's parent would anchor the sound one level too high, on the shared
        // Props root. Measured: the first run logged every release as `src=Props` instead of
        // `src=<propId>`, which is a real defect and not only a logging one, because ActorFx's
        // context is also the particle anchor and every prop in the world would have shared it.
        ActorFx.Fire(this, Body.Profile, ActorEvent.Thrown, Body.GlobalPosition);
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
        // The holder's own spring wins, on the holder's peer only, and runs on the SERVER too
        // when the host is the one carrying (a host is a player; its held prop deserves the same
        // hand as everybody else's). The early-out below is specifically about the loose STREAM,
        // which the server never follows because it is the thing producing it.
        if (_spring != null)
        {
            StepSpring((float)delta);
            return;
        }
        // Only a CLIENT following a Loose stream does anything here — the server's own prop
        // IS the simulation (RigidBody3D physics runs on it directly, no follow needed), and a
        // prop that isn't currently Loose has nothing to chase.
        if (!_looseFollowing || IsServer)
        {
            // Stale-speed guard: the field below is written only while a stream is being
            // followed, so the last value of a finished episode would otherwise persist forever
            // and make a prop that has been resting for a minute read as travelling at 4 m/s the
            // next time anything touched it.
            if (IsInstanceValid(Body) && Body.ObservedSpeedMps != 0f)
                Body.ObservedSpeedMps = 0f;
            return;
        }
        float w = 1f - Mathf.Exp(-25f * (float)delta);
        Vector3 before = Body.GlobalPosition;
        Body.GlobalPosition = before.Lerp(_netLooseTarget.Origin, w);
        Body.GlobalBasis = Body.GlobalBasis.Orthonormalized().Slerp(_netLooseTarget.Basis.Orthonormalized(), w);
        // SFX-1: hand the body the speed it is OBSERVED travelling at, because it has no
        // LinearVelocity of its own here. See Carryable.ObservedSpeedMps for why an impact was
        // otherwise inaudible to everyone except the host.
        Body.ObservedSpeedMps = delta > 0 ? (Body.GlobalPosition - before).Length() / (float)delta : 0f;
    }

    /// <summary>One holder-side carry tick. The pose the spring trails is the holder's own carry
    /// mount, turned by whatever the player has spun the item to since they picked it up
    /// (<see cref="SandboxAvatar.HeldPropLocalRotation"/> — local to the holder, and carried
    /// across to everyone else only by the transform a PLACE sends), then by the item's authored
    /// hold rotation.</summary>
    private void StepSpring(float dt)
    {
        if (_springHolder == null || !IsInstanceValid(_springHolder) || !IsInstanceValid(Body))
        {
            ClearSpring();
            return;
        }
        Transform3D mount = Body.EffectiveCarryAnchor(_springHolder.CarryAnchorGlobalTransform);
        Interactable? it = _feel;
        Basis hold = it != null
            ? Basis.FromEuler(it.HoldRotationDegrees * (Mathf.Pi / 180.0f))
            : Basis.Identity;
        Basis pose = mount.Basis.Orthonormalized() * _springHolder.HeldPropLocalRotation * hold;
        Body.GlobalTransform = _spring!.Step(dt, HandAnchor(), pose, SpringHeft);
    }
}
