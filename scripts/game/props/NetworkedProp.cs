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

    // --- Held, on every peer (FEEL-1, 2026-09-20) -------------------------------------------
    //
    // A networked prop is no longer driven by a CarryController anchor chase on ANY peer. That
    // chase pulled the prop toward a chest-height socket, which is the snap Talon rode into
    // ("it shouldn't snap to any location") and, with the old CollisionMask = 0, the clip as
    // well. CarryController stays for the offline feel sandbox, which is the only thing that
    // still uses it.
    //
    // The holder's own peer drives the RAY HOLD (see StepSpring): the grabbed point rides the
    // view ray at the hold distance the wheel sets. Every other peer — and the server, for a
    // remote holder — holds the prop at the pose it had RELATIVE TO THE HOLDER at the grab,
    // which costs no wire at all: ApplyPropState's Held broadcast already carries the prop's
    // transform at the moment of the grab and until now simply discarded it.
    private SandboxAvatar? _holder;
    private Transform3D _holdLocalToHolder = Transform3D.Identity;

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

    // The ray hold's state, on the holder's peer only. All four are recorded ONCE, at the grab,
    // from where the prop already is (BindToHolderRayHold) -- except the distance, which is the
    // one thing the wheel moves.
    private Vector3 _grabLocal;          // the grabbed point, in the prop's own frame
    private Basis _holdBasisLocal = Basis.Identity;  // its orientation, relative to the holder's yaw
    private float _holdDistanceM;        // along the view ray, in [_holdMinM, _holdMaxM]
    private float _holdMinM;             // derived from this prop's bulk and this holder's capsule
    private float _holdMaxM;
    private float _blockedSec;           // how long the world has held it off its target

    // --- PHYS-1's bounded energy, server-side (ruling P2) ------------------------------------
    //
    // This prop's own ceiling. PropPhysics.MaxPropSpeedMps ordinarily; raised for the length of a
    // throw's launch and latched back down the first tick the throw's horizontal energy is spent.
    // Per prop rather than global because a throw is one prop's episode, and a second prop that
    // the thrown one knocks into must not inherit the throw's allowance.
    private float _speedCapMps = PropPhysics.MaxPropSpeedMps;

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
        // SFX-2: and its contacts are ANNOUNCED, not played where they happen. See
        // Carryable.ImpactReporter and ReportImpact below.
        Body.ImpactReporter = ReportImpact;
        // PHYS-1 (P1): and the SAME contact signal, read a second time and above both of the
        // sound gates, is what wakes whatever this prop hits. See Carryable.BumpReporter.
        Body.BumpReporter = ReportBump;

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

    /// <summary>
    /// Server-dictated: this prop is now in <paramref name="holderAvatar"/>'s hands.
    ///
    /// <para><paramref name="propAtGrab"/> is the prop's transform at the instant of the grab, as
    /// the server broadcast it. <b>It used to be discarded on this path and it is now the whole
    /// mechanism</b>: every peer that is not the holder records the prop's pose RELATIVE TO THE
    /// HOLDER at that moment and holds it there, so a spectator sees the object where the holder
    /// is actually carrying it instead of welded to a chest socket — and no new field crosses the
    /// wire to achieve it. The holder's own peer additionally derives the ray hold from its own
    /// view (see <see cref="BindToHolderRayHold"/>), which is why the grab offset does not need to
    /// ride the wire either.</para>
    ///
    /// <para>The <see cref="IsInstanceValid"/> guards below close the freed-holder crash if the
    /// holder avatar is despawned while still bound (a disconnect race).</para>
    /// </summary>
    /// <param name="springOnThisPeer">True on the ONE peer whose local player is the holder.</param>
    public void BindToHolder(Node3D holderAvatar, Transform3D propAtGrab, bool springOnThisPeer = false)
    {
        HolderPeerId = long.TryParse(holderAvatar.Name.ToString(), out long id) ? (int)id : 0;
        // A grab is legal while the prop is still Loose (chasing down a rolling ball is half the
        // game), so this transition can arrive mid-stream-follow. Stop following here, or the
        // stream lerp keeps dragging the body toward the last (now frozen) loose sample every tick
        // underneath the hold — the held item visibly drifts away from the holder as they walk
        // (Run-RegrabTest.ps1 pins this down).
        _looseFollowing = false;
        _holder = holderAvatar as SandboxAvatar;
        _holdLocalToHolder = _holder != null && IsInstanceValid(_holder)
            ? _holder.GlobalTransform.AffineInverse() * propAtGrab
            : Transform3D.Identity;
        _blockedSec = 0f;
        // Freeze, drop the collision LAYER, keep the world MASK, pop and cue — everything about a
        // pickup except moving the prop. Nothing snaps: see Carryable.OnPickedUpBySpring.
        Body.OnPickedUpBySpring();
        if (springOnThisPeer && _holder != null)
        {
            BindToHolderRayHold(_holder);
            return;
        }
        // NOT the holder: project this instant, rather than waiting for the first follow tick.
        // Measured -- the witness saw the crate 0.034 m inside its holder on exactly ONE sample,
        // the one taken between the Held broadcast landing and the next _PhysicsProcess, because
        // the pose the grab arrived with was the holder's own and this peer's copy of that body
        // is an interpolated proxy a few centimetres away from it. One tick is still a tick, and
        // the bar is zero.
        if (_holder != null && IsInstanceValid(_holder) && IsInstanceValid(Body))
        {
            Transform3D at = _holder.GlobalTransform * _holdLocalToHolder;
            (Vector3 capA, Vector3 capB, float capR) = HolderCapsule(_holder);
            Body.GlobalPosition = CarryHold.PushOutOfSegment(
                at.Origin, capA, capB, capR + Body.BoundingRadiusM + CarryHold.HolderSkinM,
                at.Origin - _holder.GlobalPosition);
            Body.GlobalBasis = at.Basis.Orthonormalized();
        }
    }

    /// <summary>
    /// <b>The holder's own view of what they are carrying</b> — the ray hold (FEEL-1) driven by
    /// the feel system's spring (CARRY-1).
    ///
    /// <para><b>Nothing moves on the grab frame, and that is the ruling.</b> Talon: <i>"it
    /// shouldn't snap to any location; that's why there's physics and collision on the
    /// objects."</i> So the hold is described in terms of where the prop ALREADY IS: its centre is
    /// projected onto the view ray to give the hold distance, the point on that ray is recorded in
    /// the prop's own frame as the grabbed point, and its orientation is recorded relative to the
    /// holder's yaw. Feed those three back through <c>CarryHold</c> on the very next tick and you
    /// get the prop's own transform, exactly — which is what
    /// <c>CarryHoldTests.OnTheGrabFrame_TheDerivedPoseIsThePropsOwnTransform</c> pins.</para>
    ///
    /// <para><b>The ray rather than a raycast hit.</b> The pick is an aim CONE
    /// (<c>InteractTargeting</c>), not a ray test, so there is no hit point to use — and a bot
    /// has no camera at all. Projecting the centre onto the ray is defined for every caller,
    /// needs no physics query on the grab frame, and gives the identical no-snap property.</para>
    ///
    /// <para><b>Why only the holder.</b> A spring is a LOCAL prediction of a server-authoritative
    /// fact. Running it on every peer would have every peer integrating a slightly different
    /// spring against a slightly different interpolated anchor, so no two players would agree
    /// where the crate is, for a purely cosmetic gain. CARRY-1's reasoning, unchanged.</para>
    /// </summary>
    private void BindToHolderRayHold(SandboxAvatar holder)
    {
        _spring = new CarrySpring();
        Transform3D prop = Body.GlobalTransform;
        Vector3 eye = holder.AimOriginGlobalPosition;
        Vector3 dir = Aim.AimQuery.DirectionFromYawPitch(holder.AimYaw, holder.AimPitch);
        if (!dir.IsFinite() || dir.LengthSquared() < 1e-6f)
            dir = -holder.GlobalTransform.Basis.Z;
        dir = dir.Normalized();

        _holdMinM = CarryHold.HoldMinM(HolderCapsuleRadiusM(holder), Body.BoundingRadiusM);
        _holdMaxM = CarryHold.HoldMaxM(_holdMinM);
        _holdDistanceM = CarryHold.ClampHoldDistance(
            (prop.Origin - eye).Dot(dir), _holdMinM, _holdMaxM);
        // The grabbed point is kept ON the object: see CarryHold.GrabPointOnProp for the run
        // where a bot with no pitch produced a 1.2 m lever and every hold broke 0.3 s after the
        // grab. The hold DISTANCE is then re-derived from that point, so the prop still ends up
        // exactly where it was whenever the ray really did pass through it.
        Vector3 onProp = CarryHold.GrabPointOnProp(
            prop.Origin, CarryHold.HoldPoint(eye, dir, _holdDistanceM), Body.BoundingRadiusM);
        _holdDistanceM = CarryHold.ClampHoldDistance((onProp - eye).Dot(dir), _holdMinM, _holdMaxM);
        _grabLocal = CarryHold.GrabLocalFrom(prop, onProp);
        _holdBasisLocal = CarryHold.HoldBasisLocal(holder.AimYaw, prop.Basis);
        // The documented anti-pop: seed AT THE ITEM, never at the target. BOTH seeds matter and
        // the second one was measured the hard way -- without it _springPos starts at the WORLD
        // ORIGIN, so the first tick's spring races across the map, the speed cap turns that into
        // a long crawl, and the break-hold clock (which reads the prop's distance from its
        // target) gives the hold up 0.3 s after every grab. The suite's first run said exactly
        // that: "hold broken prop=1014 blocked=1.23m for 0.30s", nine samples after the grab.
        _spring.Seed(prop, prop.Origin);
        _springPos = prop.Origin;
        _springVel = Vector3.Zero;
    }

    /// <summary>Where the grabbed point should be this tick: along the holder's view ray, at the
    /// hold distance the wheel has set, and never inside the holder (the same projection the
    /// resolved pose gets, applied to the target as well so the spring is not fighting a target it
    /// can never reach).</summary>
    private Vector3 HoldPointNow(SandboxAvatar holder)
    {
        Vector3 eye = holder.AimOriginGlobalPosition;
        Vector3 dir = Aim.AimQuery.DirectionFromYawPitch(holder.AimYaw, holder.AimPitch);
        if (!dir.IsFinite() || dir.LengthSquared() < 1e-6f)
            dir = -holder.GlobalTransform.Basis.Z;
        return CarryHold.HoldPoint(eye, dir.Normalized(), _holdDistanceM);
    }

    /// <summary>The holder's collision radius, or the reference body's when the avatar has not
    /// measured itself yet. Never a literal here: <c>AvatarProportions</c> is the one place a
    /// body's dimensions are derived, and a second copy is how a mirror goes stale.</summary>
    private static float HolderCapsuleRadiusM(SandboxAvatar holder) =>
        holder.Proportions.CapsuleRadiusM;

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

    /// <summary><b>How far the prop is trailing the point the holder is holding it at</b>,
    /// metres, or 0 on a peer that is not the holder. The spring's steady-state lag, sampled
    /// rather than estimated — <c>BotHarness</c> logs it and the handoff's lag table is built from
    /// it. Since FEEL-1 the target is the ray hold rather than a chest anchor, so this is the
    /// number that has to stay under half of <see cref="HoldMinM"/> at a walk.</summary>
    public float SpringLagM => _spring != null && _holder != null && IsInstanceValid(_holder)
        // THE BODY, not the CarrySpring object's own Position. Measured the hard way: the ray
        // hold integrates the spring's arithmetic against its own state (see SpringTo), so
        // CarrySpring.Position sits at the value it was SEEDED with for the whole hold and
        // LagTo against it reported a mean lag of 1.54 m and a peak of 3.34 m for a carry that
        // was in fact tracking to within a few centimetres. An instrument reading the wrong
        // field looks exactly like the feature being broken.
        ? Body.GlobalPosition.DistanceTo(TargetOriginFor(_holder, HoldPointNow(_holder)))
        : 0f;

    /// <summary>True while this peer is the one carrying this prop on the ray hold.</summary>
    public bool SpringActive => _spring != null;

    /// <summary>This prop's minimum hold distance and the far end of its scroll band, metres —
    /// 0 unless this peer is the holder. Read by the wheel (<c>HoldDistanceController</c>) so the
    /// band it scrolls through is THIS prop's, derived, rather than a constant that is wrong for
    /// either a can or a crate.</summary>
    public float HoldMinM => _spring != null ? _holdMinM : 0f;

    /// <inheritdoc cref="HoldMinM"/>
    public float HoldMaxM => _spring != null ? _holdMaxM : 0f;

    /// <summary>The hold distance right now, metres.</summary>
    public float HoldDistanceM => _holdDistanceM;

    /// <summary>The wheel: move this prop <paramref name="notches"/> steps further out (positive)
    /// or closer in (negative), clamped to its own band. No-op unless this peer is the holder —
    /// the hold distance is a local view decision, exactly as the hold ROTATION has been since
    /// CARRY-1, and it reaches everybody else in the transform a place sends.</summary>
    public void ScrollHold(int notches)
    {
        if (_spring == null)
            return;
        _holdDistanceM = CarryHold.Scroll(_holdDistanceM, notches, _holdMinM, _holdMaxM);
    }

    /// <summary>
    /// <b>Signed clearance between this held prop and its holder's collision capsule</b>, metres:
    /// the distance from the prop's centre to the capsule's axis, less the capsule's radius and
    /// less the prop's own bounding radius. <b>Negative is interpenetration</b> — the prop is
    /// inside the person carrying it, which is the defect this whole packet exists to end.
    ///
    /// <para>Computed on any peer that has both the prop and its holder, so the suite can assert
    /// it on the holder's own view AND on a witness. A bounding SPHERE rather than the prop's
    /// AABB, which is the conservative direction: the sphere contains the box at every
    /// orientation, so a non-negative reading here proves the box is clear too.</para>
    ///
    /// <para>0 when this prop is not held, or its holder is not on this peer — an absence, and
    /// the suite treats it as one rather than as a clearance of zero.</para></summary>
    public float HolderClearanceM
    {
        get
        {
            if (_holder == null || !IsInstanceValid(_holder) || !IsInstanceValid(Body))
                return 0f;
            (Vector3 a, Vector3 b, float radius) = HolderCapsule(_holder);
            Vector3 closest = ClosestOnSegment(Body.GlobalPosition, a, b);
            return Body.GlobalPosition.DistanceTo(closest) - radius - Body.BoundingRadiusM;
        }
    }

    /// <summary>The holder's collision capsule as a segment plus a radius, in world space — the
    /// two cap CENTRES, so a point measured against the segment is measured against the capsule.
    /// Derived from <c>AvatarProportions</c>, never from literals.</summary>
    private static (Vector3 A, Vector3 B, float Radius) HolderCapsule(SandboxAvatar holder)
    {
        Sandbox.AvatarProportions p = holder.Proportions;
        float radius = p.CapsuleRadiusM;
        float half = Mathf.Max(0f, p.CapsuleHeightM * 0.5f - radius);
        Vector3 centre = holder.GlobalTransform * p.CapsuleCentreLocal;
        Vector3 up = Vector3.Up * half;
        return (centre - up, centre + up, radius);
    }

    private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 1e-10f)
            return a;
        return a + ab * Mathf.Clamp((p - a).Dot(ab) / lenSq, 0f, 1f);
    }

    /// <summary>Drop the hold. Every transition out of Held runs through here, and it must, or the
    /// hold keeps writing this body's transform every tick underneath whatever the network says is
    /// happening to it.</summary>
    private void ClearSpring()
    {
        if (_spring == null && _holder == null)
            return;
        _spring = null;
        _holder = null;
        _blockedSec = 0f;
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
        _holder = null;
        ClearSpring();
        _looseFollowing = false;
        // SILENT (SFX-2). This runs on EVERY peer for every Resting transition — a settle, a
        // disconnect release, the round's reset edge — and it used to reach physics through
        // Body.OnDropped(), which fires ActorEvent.Dropped. The toss arc that call applies is
        // discarded on the next four lines; the announcement was not, so a reset fanned a "drop"
        // to every peer for every prop in the world. Inaudible only because nothing maps Dropped
        // to a sound today. The release VERB now rides the state change as PropRelease and is
        // announced once, in BeginLoose, where a release actually happens.
        Body.RejoinPhysicsSilently();  // rejoins physics locally, but we immediately pin it below:
        // PHYS-1: and the episode is over, so the body may sleep again. Given back HERE because
        // this is the every-peer Resting latch that every route into rest funnels through.
        Body.CanSleep = true;
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
    public void BeginLooseServer(Vector3 impulse) => BeginLooseServer(impulse, null);

    /// <summary>As above, with the tumble NAMED rather than drawn (PHYS-2). <paramref name="angular"/>
    /// null keeps <see cref="Carryable.OnThrown(Vector3)"/>'s random +/-2 rad/s, which is every
    /// shipped release; a value hands the body exactly that spin, which is what a fixture asking
    /// for a known shove needs. See <c>Carryable.OnThrown(Vector3, Vector3)</c> for the
    /// measurement that made this necessary.</summary>
    public void BeginLooseServer(Vector3 impulse, Vector3? angular)
    {
        HolderPeerId = 0;
        _holder = null;
        ClearSpring();
        Body.CanSleep = false;   // PHYS-1: for the length of the episode; see WakeFromContactServer
        if (angular is { } spin)
            Body.OnThrown(impulse, spin);
        else
            Body.OnThrown(impulse);
    }

    /// <summary>Server-only, test fixture (<c>--seed-props-drop</c>): let a seeded prop FALL from
    /// where it was seeded, with no impulse and no spin. The <c>Loose</c> broadcast that goes with
    /// it is the ordinary one, so every peer follows the stream exactly as it would for a throw;
    /// what differs is only that nothing threw it. See <c>PropManager.StepSeededDrop</c>.</summary>
    public void DropLooseServer()
    {
        HolderPeerId = 0;
        _holder = null;
        ClearSpring();
        Body.RejoinPhysicsSilently();
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
        _holder = null;
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
    public void BeginLoose(Transform3D t, PropRelease release)
    {
        HolderPeerId = 0;
        _holder = null;
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
        // SFX-2 SPENT THE BIT. SFX-1 had to play the shared Whoosh for every release, because
        // CARRY-1's own handoff recorded that "at ApplyPropState a place and a drop are the same
        // Held->Loose transition and are indistinguishable there" — so a careful set-down and a
        // throw sounded identical to everyone, and the material's settle tick was host-only.
        // The verb now rides the transition as one byte (PropRelease) and this is where it is
        // spent: the SAME every-peer point, so the tick and the whoosh reach the other player on
        // the same path the release itself does.
        //
        // None fires NOTHING, and that arm is not a defensive default — it is the late-join
        // snapshot and the round's reset edge, both of which describe a STATE rather than report
        // an event. A snapshot that replayed the throw which started a roll two minutes ago would
        // be a sound with no cause.
        //
        // `this`, NOT GetParent(). Carryable's own fires pass ITS parent, which is this node —
        // so passing this node's parent would anchor the sound one level too high, on the shared
        // Props root. Measured: the first run logged every release as `src=Props` instead of
        // `src=<propId>`, which is a real defect and not only a logging one, because ActorFx's
        // context is also the particle anchor and every prop in the world would have shared it.
        ActorEvent? announce = release switch
        {
            PropRelease.Thrown => ActorEvent.Thrown,
            PropRelease.Placed => ActorEvent.Placed,
            PropRelease.Dropped => ActorEvent.Dropped,
            _ => null,
        };
        if (announce is { } evt)
            ActorFx.Fire(this, Body.Profile, evt, Body.GlobalPosition);
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
        // PHYS-1: a wake queued last tick is spent HERE, now that the unfreeze has taken. First,
        // and before any early-out, because every branch below can return.
        ApplyPendingImpulse();
        // The holder's own spring wins, on the holder's peer only, and runs on the SERVER too
        // when the host is the one carrying (a host is a player; its held prop deserves the same
        // hand as everybody else's). The early-out below is specifically about the loose STREAM,
        // which the server never follows because it is the thing producing it.
        if (_spring != null)
        {
            StepSpring((float)delta);
            return;
        }
        // HELD, but by somebody else (and on the server whenever a CLIENT is the holder). The
        // pose is the one the prop had relative to that holder at the grab, which is what makes a
        // spectator's view agree with the holder's without a byte on the wire -- see
        // BindToHolder. Smoothed rather than snapped, because the holder's body is itself an
        // interpolated proxy here, and projected out of the holder's capsule for exactly the
        // reason the holder's own view is: an interpolation that lags a turn would otherwise put
        // the crate inside the person carrying it on everybody else's screen.
        if (_holder != null)
        {
            StepFollowHolder((float)delta);
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

    /// <summary>
    /// <b>One tick of the holder's own hold.</b> Four things happen, in this order, and each one
    /// is a different guarantee:
    ///
    /// <list type="number">
    /// <item><b>The target.</b> The grabbed point rides the view ray at the hold distance; the
    /// prop's origin is wherever it has to be for that to be true at this orientation. The
    /// orientation follows the holder's YAW and the rotation the player has spun it to, never the
    /// pitch -- a thing held in front of you does not tumble because you looked down.</item>
    /// <item><b>The spring.</b> Unchanged from CARRY-1 (a critically damped spring, heft on the
    /// response dial) except that its response is floored so the lag at a walk stays under half
    /// of <c>HoldMin</c> -- <c>CarryHold.HoldOmega</c>.</item>
    /// <item><b>The world.</b> The resolved step is SWEPT with the physics server rather than
    /// written straight onto the body, so a held crate stops against a shelf instead of passing
    /// through it. Talon: <i>"that's why there's physics and collision on the objects."</i> If
    /// the world holds it off its target by more than <c>BreakHoldM</c> for <c>BreakHoldSec</c>,
    /// the hold is given up -- you shoved it into a shelf and kept walking.</item>
    /// <item><b>The holder.</b> Whatever is left is projected out of the holder's own capsule.
    /// This is the HARD guarantee and it is applied to the pose that is actually written, not to
    /// the target: the distance clamp and the spring floor make it rare, this makes it
    /// certain.</item>
    /// </list>
    ///
    /// <para>The step is also SPEED-CAPPED (<c>CarryHold.CapStep</c>), so no spring response and
    /// no frame-time spike can turn a carried crate into a projectile -- Talon: <i>"I want to know
    /// objects won't freak out and make other objects jump around randomly."</i></para>
    /// </summary>
    private void StepSpring(float dt)
    {
        if (_holder == null || !IsInstanceValid(_holder) || !IsInstanceValid(Body))
        {
            ClearSpring();
            return;
        }

        // 1. the target
        Vector3 holdPoint = HoldPointNow(_holder);
        Basis pose = PoseFor(_holder);
        Vector3 target = CarryHold.PropOriginFor(holdPoint, pose, _grabLocal);

        // 2. the spring
        float omega = CarryHold.HoldOmega(
            Mathf.Lerp(_spring!.LightResponse, _spring.HeavyResponse, SpringHeft),
            _holdMinM, Net.AvatarMotor.MoveSpeed);
        Vector3 from = Body.GlobalPosition;
        if (ShouldSeat(target))
        {
            // A teleport, a respawn, a room move. Seat the whole hold on the target and start the
            // spring again from there; easing would drag the prop across the level.
            _springPos = target;
            _springVel = Vector3.Zero;
            _blockedSec = 0f;
            Body.GlobalTransform = new Transform3D(pose, target);
            return;
        }
        Vector3 resolved = SpringTo(target, omega, dt);
        resolved = CarryHold.CapStep(from, resolved,
            CarryHold.MaxHoldSpeedMps(Net.AvatarMotor.MoveSpeed), dt);

        // 3. the world
        Body.GlobalBasis = pose;
        Vector3 swept = SweepTo(from, resolved, out PhysicsTestMotionResult3D? hit);
        // PHYS-1 (P1): and whatever the world put in the way, if it was a resting prop, is now
        // moving. Talon: "if they're placing an object on a shelf and accidentally hit a bunch of
        // boxes, those boxes should fall over like dominoes."
        WakeSweptContactServer(hit, resolved - from, dt);

        // 4. the holder
        (Vector3 capA, Vector3 capB, float capR) = HolderCapsule(_holder);
        Vector3 final = CarryHold.PushOutOfSegment(
            swept, capA, capB, capR + Body.BoundingRadiusM + CarryHold.HolderSkinM,
            holdPoint - _holder.AimOriginGlobalPosition);
        Body.GlobalPosition = final;
        _springPos = final;

        // THE BREAK IS ABOUT THE WORLD, NOT ABOUT THE SPRING, and the difference cost a run.
        // A hold that is merely lagging -- the speed cap biting through a fast turn, a heavy
        // crate on a slow response -- is the carry working; a hold the WORLD is sitting on is a
        // hold the player has lost. So the clock only runs on a tick where the sweep actually hit
        // something (swept != resolved) AND the prop is past BreakHoldM from its target. Measured
        // without the first half: every grab broke 0.3 s later, in open floor, with nothing
        // touching the prop at all.
        float blocked = final.DistanceTo(target);
        bool worldIsInTheWay = swept.DistanceSquaredTo(resolved) > 1e-8f;
        _blockedSec = CarryHold.StepBlockedSeconds(_blockedSec, worldIsInTheWay ? blocked : 0f, dt);
        if (CarryHold.ShouldBreakHold(blocked, _blockedSec)
            && _holder.OwnerPeerId == Multiplayer.GetUniqueId()
            && PropManager.Instance is { } mgr)
        {
            // The world has had this prop off its target for BreakHoldSec: it has left your
            // hands. A request, never a local mutation, exactly like every other carry verb.
            //
            // A DROP, NOT A PLACE, and that was measured rather than reasoned. A place can be
            // REFUSED -- the prop is wedged in whatever stopped it, so placement integrity
            // answers Overlapping -- which leaves the player holding something the game has
            // decided they have lost, and spends the refusal channel the HUD and three suites
            // read on an event nobody asked for (Run-PlaceTest reported `ordinal 4` where its
            // own scripted place expected 5). A drop cannot be refused, which is the right
            // property for a transition the WORLD forced: an object torn out of your hands is a
            // drop, not a careful set-down, and the physics that torn it out takes it from here.
            GD.Print($"[carry] hold broken prop={PropId} blocked={blocked:F2}m for {_blockedSec:F2}s");
            _blockedSec = 0f;
            mgr.ClientRequestDrop();
        }
    }

    /// <summary>This tick's world orientation for the held prop: the holder's yaw, the rotation
    /// the player has spun it to since the grab (<see cref="SandboxAvatar.HeldPropLocalRotation"/>
    /// -- local to the holder, and carried to everyone else only by the transform a PLACE sends),
    /// and the orientation it was grabbed at.
    ///
    /// <para>The item's authored hold rotation is deliberately NOT applied any more. It exists to
    /// seat a prop in a socket at a chosen angle, and there is no socket: the ruling is that the
    /// object keeps the orientation it was grabbed at.</para></summary>
    private Basis PoseFor(SandboxAvatar holder) =>
        CarryHold.PoseBasis(holder.AimYaw, holder.HeldPropLocalRotation, _holdBasisLocal);

    /// <summary>Where the prop's ORIGIN has to be for its grabbed point to sit on
    /// <paramref name="holdPoint"/> at this tick's orientation.</summary>
    private Vector3 TargetOriginFor(SandboxAvatar holder, Vector3 holdPoint) =>
        CarryHold.PropOriginFor(holdPoint, PoseFor(holder), _grabLocal);

    /// <summary>One spring step in POSITION only, at the given response. The orientation is
    /// written directly -- the hold's orientation is exact by construction and a second slerp
    /// would add a lag nobody asked for -- so <see cref="CarrySpring.Step"/>'s trailing-tilt arm
    /// is not used here. Its arithmetic is, through <see cref="CarrySpring.Spring"/>, which is the
    /// same unconditionally stable closed form.</summary>
    private Vector3 SpringTo(Vector3 target, float omega, float dt)
    {
        Vector3 pos = _springPos;
        Vector3 vel = _springVel;
        CarrySpring.Spring(ref pos, ref vel, target, omega, dt);
        _springVel = vel;
        return pos;
    }

    private Vector3 _springPos;
    private Vector3 _springVel;

    /// <summary>
    /// Move the held body from <paramref name="from"/> toward <paramref name="to"/> and stop it
    /// where the world does, returning where it actually got to.
    ///
    /// <para><b>A sweep rather than an assignment, and that is the whole of "the objects have
    /// collision".</b> A frozen kinematic body whose transform is written lands wherever it is
    /// told, shelf or no shelf; <c>PhysicsServer3D.BodyTestMotion</c> asks the same broadphase the
    /// simulation uses where the body WOULD have stopped. One query per held prop per tick, and
    /// there is at most one held prop per player.</para>
    ///
    /// <para>The body's own collision MASK is what this reads
    /// (<c>Carryable.OnPickedUpBySpring</c> sets it to the world layer while held); its LAYER
    /// stays 0, so nothing is pushed BY the held prop and the query cannot hit the holder.</para>
    /// </summary>
    /// <param name="hit">What the sweep met, or null. <b>PHYS-1 (P1) reads this rather than
    /// adding a query of its own</b>: the one thing a held crate driven into a stack of boxes
    /// needs is the identity of the first box, and the sweep that already runs every tick to stop
    /// the crate at the shelf knows it. A frozen kinematic body meeting another frozen kinematic
    /// body produces no <c>body_entered</c> on either side (SFX-1 measured exactly that, one
    /// system over), so this result IS the contact signal for a held prop, and it costs
    /// nothing.</param>
    private Vector3 SweepTo(Vector3 from, Vector3 to, out PhysicsTestMotionResult3D? hit)
    {
        hit = null;
        Vector3 motion = to - from;
        if (motion.LengthSquared() < 1e-10f)
            return to;
        var parameters = new PhysicsTestMotionParameters3D
        {
            From = new Transform3D(Body.GlobalBasis, from),
            Motion = motion,
            RecoveryAsCollision = false,
        };
        var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(Body.GetRid(), parameters, result))
            return to;
        hit = result;
        return from + result.GetTravel();
    }

    /// <summary>
    /// <b>Server-only: a held prop has been driven into something — wake it if it is a resting
    /// prop</b> (P1). Fed from the hold's own per-tick sweep, so there is no second query and no
    /// second monitor.
    ///
    /// <para>The approach speed is the step the hold WANTED to take, not the one it got: the
    /// sweep stops the held body at the first contact, so its achieved motion after a block is
    /// near zero and would read as no shove at all. The direction is the sweep's own contact
    /// normal, inverted — <c>GetCollisionNormal</c> points out of the thing that was hit, and the
    /// push goes in.</para>
    /// </summary>
    private void WakeSweptContactServer(PhysicsTestMotionResult3D? hit, Vector3 wantedMotion,
        float dt)
    {
        if (hit == null || !IsServer || dt <= 0f || PropManager.Instance is not { } mgr)
            return;
        if (hit.GetCollider() is not Carryable struck)
            return;
        float approach = wantedMotion.Length() / dt;
        Vector3 push = -hit.GetCollisionNormal();
        if (push.LengthSquared() < 1e-8f)
            push = struck.GlobalPosition - Body.GlobalPosition;
        mgr.ServerBumpProp(struck, Body.Mass > 0f ? (float)Body.Mass : 0f, approach, push,
            hit.GetCollisionPoint());
    }

    /// <summary>
    /// <b>One tick of a held prop on a peer that is not the holder</b> -- and on the server
    /// whenever a client is the holder.
    ///
    /// <para>The pose is the one the prop had relative to the holder's body at the grab, replayed
    /// against wherever that body is now. It needs no wire field (see <see cref="BindToHolder"/>),
    /// it tracks the holder's turn and their carry geometry, and it agrees with the holder's own
    /// ray hold to within the spring's lag and the wheel -- the two documented mismatches, the
    /// same class as the hold ROTATION, which has been local since CARRY-1 and reaches everyone in
    /// the transform a place sends.</para>
    ///
    /// <para>Smoothed rather than snapped because the holder is an interpolated proxy here, and
    /// projected out of their capsule for the same reason the holder's own view is: the suite
    /// asserts zero interpenetration on BOTH views, because a crate inside the hider's chest is
    /// exactly as bad on the seeker's screen as on their own.</para>
    /// </summary>
    private void StepFollowHolder(float dt)
    {
        if (_holder == null || !IsInstanceValid(_holder) || !IsInstanceValid(Body))
        {
            ClearSpring();
            return;
        }
        Transform3D target = _holder.GlobalTransform * _holdLocalToHolder;
        if (_holdMaxM <= 0f
            ? Body.GlobalPosition.DistanceSquaredTo(target.Origin) > 4f
            : ShouldSeat(target.Origin))
        {
            // The holder teleported (see ShouldSeat). A non-holder has no hold band of its own --
            // _holdMaxM is only filled in on the holder's peer -- so it falls back to 2 m, which
            // is the same order as the band and far below any room move.
            Body.GlobalTransform = target;
            return;
        }
        float w = 1f - Mathf.Exp(-FollowResponse * dt);
        Vector3 pos = Body.GlobalPosition.Lerp(target.Origin, w);
        pos = CarryHold.CapStep(Body.GlobalPosition, pos,
            CarryHold.MaxHoldSpeedMps(Net.AvatarMotor.MoveSpeed), dt);
        // PHYS-1 (P1): THE SERVER'S COPY OF A PROP A CLIENT IS HOLDING ALSO KNOCKS THINGS OVER.
        //
        // StepSpring's sweep covers the host's own hands and nothing else: when a CLIENT is the
        // holder, this is the arm that runs on the server, and it writes the pose straight onto
        // the body with no sweep at all. Without a probe here a client could carry a crate
        // through a row of boxes and the server — the only peer that may wake anything — would
        // never learn a contact happened, so the dominoes would fall for the host and for nobody
        // else. One motion query per client-held prop per tick, on the server only, and there is
        // at most one held prop per player.
        //
        // It PROBES and does not clamp: the pose written below is unchanged, because the holder's
        // own peer already ran the sweep that stops the crate at the shelf, and a second,
        // independent stop on the server would put the two views in permanent disagreement.
        if (IsServer)
        {
            SweepTo(Body.GlobalPosition, pos, out PhysicsTestMotionResult3D? hit);
            WakeSweptContactServer(hit, pos - Body.GlobalPosition, dt);
        }
        (Vector3 capA, Vector3 capB, float capR) = HolderCapsule(_holder);
        Body.GlobalPosition = CarryHold.PushOutOfSegment(
            pos, capA, capB, capR + Body.BoundingRadiusM + CarryHold.HolderSkinM,
            target.Origin - _holder.GlobalPosition);
        Body.GlobalBasis = Body.GlobalBasis.Orthonormalized().Slerp(target.Basis.Orthonormalized(), w);
    }

    /// <summary>
    /// <b>A hold does not EASE across a room.</b> When the target is further away than any lag
    /// could honestly put it, the prop is seated on it outright instead of springing toward it.
    ///
    /// <para><b>Measured, and it is the room teleport</b> (<c>Run-CarryNetTest</c> phase 3). The
    /// round moves a holder between rooms; the hold's target moves 40 m in one tick; the spring
    /// and the speed cap then walked the crate across the map at 4.8 m/s while the witness
    /// watched it trail its holder by 33 m, then 32, then 31. The old anchor chase never showed
    /// this because it snapped to the anchor on the grab frame and lerped hard afterwards.</para>
    ///
    /// <para>The threshold is derived rather than typed: a lag can only exceed
    /// <c>HoldMax + BreakHoldM</c> if the world is holding the prop, and the world holding it is
    /// the case the break rule takes -- so anything past that is a discontinuity, not a lag. The
    /// measured worst honest lag is 0.809 m against a threshold of 1.8 m.</para></summary>
    private bool ShouldSeat(Vector3 target) =>
        Body.GlobalPosition.DistanceSquaredTo(target)
            > (_holdMaxM + CarryHold.BreakHoldM) * (_holdMaxM + CarryHold.BreakHoldM);

    /// <summary>How hard a non-holder's view chases the pose the holder is carrying at, s^-1.
    /// Fast: this is not a feel spring, it is a correction against an interpolated body, and the
    /// interpolation has already done the smoothing that matters.</summary>
    private const float FollowResponse = 25f;

    /// <summary><b>This prop just took a contact worth hearing</b> — the body's own handler
    /// calls this instead of playing anything (SFX-2; see <see cref="Carryable.ImpactReporter"/>).
    ///
    /// <para><b>Server only, and the early-out is the design rather than a guard.</b> Only the
    /// server simulates a Loose prop, so only the server's copy of this body is ever asked about
    /// a contact at all — a client's is frozen kinematic and Godot reports it nothing. A client
    /// reaching here would therefore be a contact between two bodies neither of which is
    /// simulating, which is not an event about the world; and if it announced anything, the
    /// server's announcement and its own would both play.</para>
    ///
    /// <para>The position is read here rather than passed from the contact handler because the
    /// two are the same tick and this is the transform the server will also stream — so the
    /// sound and the prop cannot disagree about where the hit was.</para></summary>
    private void ReportImpact(float intensity)
    {
        if (!IsServer)
            return;
        PropManager.Instance?.ServerNoteImpact(PropId, intensity, Body.GlobalPosition);
    }

    /// <summary>Play the server's announced impact on this peer's own copy of the prop
    /// (SFX-2). Delegates to the body because the profile and the position live there.</summary>
    public void PlayWireImpact(float intensity) => Body.PlayWireImpact(intensity);

    // --- PHYS-1: wake on contact (P1) and bounded energy (P2) --------------------------------

    /// <summary>
    /// <b>This prop's body just touched another prop's body</b> — the second consumer of the
    /// contact signal SFX-2 already reads (<see cref="Carryable.BumpReporter"/>).
    ///
    /// <para><b>Server only, and for exactly <see cref="ReportImpact"/>'s reason:</b> a Loose prop
    /// simulates only on the server, so only the server's copy is ever asked about a contact. A
    /// client reaching here would be reporting a collision between two frozen kinematic bodies,
    /// which is not an event about the world — and P1 is explicit that clients never wake a prop
    /// on their own.</para>
    ///
    /// <para><b>The DIRECTION is computed here rather than taken from the signal</b>, because
    /// <c>body_entered</c> carries no normal. The line of centres is the honest approximation for
    /// two compact props, and taking the approach speed ALONG it is what makes a graze different
    /// from a shove — the property <c>PropPhysics.WakeSpeedThresholdMps</c> leans on. The reported
    /// speed is used only where that line is degenerate (two bodies at the same point, which the
    /// solver is about to separate anyway).</para>
    ///
    /// <para><b>The velocity projected is the one this body CARRIED INTO the step
    /// (<see cref="Carryable.ApproachVelocity"/>), not <c>LinearVelocity</c></b> — PHYS-2,
    /// 2026-09-21. By the time <c>body_entered</c> fires, a head-on contact with a frozen prop has
    /// already had its normal impulse applied and <c>LinearVelocity</c> reads the STOPPED body:
    /// a box shoved at 2.8 m/s into its neighbour projected to ~0, nothing woke, and the row stood.
    /// <c>Carryable.ApproachSpeedMps</c> already existed for SFX-2's identical problem one signal
    /// over; the vector beside it is what this consumer needed. The live velocity is kept as a
    /// floor for the case the pre-step sample is stale (a body that was frozen at the top of the
    /// step and thrown inside it).</para>
    /// </summary>
    private void ReportBump(Carryable other, float reportedSpeed)
    {
        if (!IsServer || PropManager.Instance is not { } mgr || !IsInstanceValid(other))
            return;
        Vector3 toward = other.GlobalPosition - Body.GlobalPosition;
        Vector3 mover = Body.ApproachVelocity.LengthSquared() >= Body.LinearVelocity.LengthSquared()
            ? Body.ApproachVelocity
            : Body.LinearVelocity;
        float approach = toward.LengthSquared() > 1e-8f
            ? PropPhysics.ApproachSpeed(mover, other.LinearVelocity, toward)
            : reportedSpeed;
        // WHERE the impulse lands is the difference between a domino and a shuffle -- PHYS-2. A
        // box's support point toward the struck prop is its leading edge (high, when it is
        // toppling; the face's centre, when it is sliding); a sphere's is its surface along the
        // line of centres. See PropPhysics.StrikePoint for the measurement behind it.
        Shape3D? moverShape = Body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape;
        Vector3 strike = moverShape is SphereShape3D sphere && toward.LengthSquared() > 1e-8f
            ? Body.GlobalPosition + toward.Normalized() * sphere.Radius
            : PropPhysics.StrikePoint(Body.GlobalTransform,
                moverShape != null ? PlacementIntegrity.HalfExtentsOf(moverShape) : Vector3.One * 0.05f,
                toward);
        Vector3 spent = mgr.ServerBumpProp(other, Body.Mass > 0f ? (float)Body.Mass : 0f, approach,
            toward, strike);

        // THE FROZEN WALL ATE THE MOVER'S MOMENTUM, AND THIS GIVES IT BACK -- PHYS-2 (2026-09-21),
        // and the row of dominoes did not fall until it did. A Resting prop is a frozen KINEMATIC
        // body on every peer: infinite mass to the solver. So in the step that reports this
        // contact the mover has already been stopped dead against it -- a box shoved at 2.8 m/s
        // reads ~0 here -- and the wake that follows hands the neighbour a FRACTION of the
        // approach speed (ContactImpulse's share) while the mover keeps nothing. Measured: the
        // funnel woke boxes 2, 3 and 4 at 1.66, 0.99 and 0.34 m/s, each stopped in turn by the
        // next frozen one, and not one tilted. Talon's sentence is "fall over like dominoes", and
        // a domino keeps leaning on the next one after the first touch.
        //
        // So when -- and only when -- the neighbour actually woke, the mover is given back the
        // velocity it carried INTO the step less the share it just handed over, and its spin
        // whole. Next tick both bodies are dynamic and the solver resolves the contact between
        // two real masses, which is what P1 promised and a frozen first touch could not deliver.
        // Nothing woken = the wall was real (below threshold, cooling down, or not Resting) and
        // the mover stays stopped, exactly as before.
        if (spent.LengthSquared() > 0f && !Body.IsHeld && !Body.Freeze && Body.Mass > 0f)
        {
            Body.LinearVelocity = Body.ApproachVelocity - spent / (float)Body.Mass;
            Body.AngularVelocity = Body.ApproachAngularVelocity;
        }
    }

    /// <summary>
    /// <b>Server-only: this resting prop has been hit — wake it into Loose and hand it the
    /// impulse</b> (P1). The registry transition and the broadcast are the caller's
    /// (<c>PropManager.ServerBumpProp</c>); this is the physical half.
    ///
    /// <para><b>The impulse is applied at the CONTACT POINT, not at the centre of mass</b>, and
    /// that is what makes P4's dominoes fall rather than slide. A cereal box struck near its top
    /// edge gets a torque that tips it; the same impulse through its centre would push it along
    /// the board. Godot's <c>ApplyImpulse</c> takes the offset relative to the centre of mass,
    /// which is a prefab-authored quantity on the boxes (<c>center_of_mass</c>), so the lever and
    /// the top-heaviness compose.</para>
    ///
    /// <para><c>RejoinPhysicsSilently</c> rather than <c>OnThrown</c>: no toss arc, no random
    /// tumble spin, and no <c>ActorEvent</c>. The fire for this transition is
    /// <see cref="PropRelease.Bumped"/>, spent once on every peer in
    /// <see cref="BeginLoose"/>, exactly as every other release verb is.</para>
    /// </summary>
    public void WakeFromContactServer(Vector3 impulse, Vector3 atWorld)
    {
        HolderPeerId = 0;
        _holder = null;
        ClearSpring();
        _speedCapMps = PropPhysics.MaxPropSpeedMps;
        // SLEEP IS SUSPENDED FOR THE LENGTH OF THE EPISODE, NOT AUTHORED ON THE PREFAB, and the
        // difference is 20 ms of server frame time. MEASURED at rest, 162 props, nothing touched:
        // with `can_sleep = false` in Can.tscn the server's p95 was 23.080 ms against the base
        // tree's 2.712 ms, because every can on every shelf stayed in the physics server's active
        // set forever. What the flag is FOR -- a can must not be put to sleep half way down an
        // aisle -- is only true while it is rolling, which is exactly this episode.
        Body.CanSleep = false;
        Body.RejoinPhysicsSilently();
        // QUEUED FOR THE NEXT TICK, NOT APPLIED NOW, AND THE FIRST RUN OF THE SUITE IS WHY.
        //
        // `Freeze = false` takes effect when the physics server next steps the body; an impulse
        // applied before that is integrated into a state the unfreeze then re-initialises, and
        // it is silently lost. MEASURED, on the first live run of tests/Run-PhysicsFeelTest.ps1:
        // the server logged `[phys] wake prop=1017 at=4.30 m/s -> 2.58 m/s` NINETY-SEVEN times
        // -- wake, nothing moves, settle, wake again -- and the crate travelled 0.093 m in
        // forty seconds. Every counter said the feature worked.
        //
        // The reason the release funnel never hit this is that it does not use an impulse at
        // all: Carryable.Release WRITES LinearVelocity, and a written velocity survives the
        // unfreeze where an accumulated impulse does not. Writing the velocity here instead
        // would work and would throw away the thing P4 needs -- the impulse is applied at the
        // CONTACT POINT, and that lever is what tips a top-heavy cereal box rather than sliding
        // it. So the lever is kept and the impulse waits one tick (16 ms, invisible).
        _pendingImpulse = impulse;
        _pendingImpulseAt = atWorld;
        _hasPendingImpulse = true;
    }

    private Vector3 _pendingImpulse;
    private Vector3 _pendingImpulseAt;
    private bool _hasPendingImpulse;

    /// <summary>Spend a queued wake impulse, on the first server tick after the body was
    /// unfrozen. See <see cref="WakeFromContactServer"/> for why it cannot be spent at the
    /// wake.</summary>
    private void ApplyPendingImpulse()
    {
        if (!_hasPendingImpulse)
            return;
        _hasPendingImpulse = false;
        if (!IsInstanceValid(Body) || Body.Freeze)
            return;   // re-frozen between the wake and now: grabbed, reset, or settled
        // The offset ApplyImpulse wants is relative to the CENTRE OF MASS, in world space.
        // Body.CenterOfMass is the body-local one -- the authored value on a prefab whose
        // center_of_mass_mode is Custom (PHYS-1 makes the cereal box top-heavy that way), and
        // zero on every prop whose collider is centred on its origin, which is all the others.
        Body.ApplyImpulse(_pendingImpulse, _pendingImpulseAt - Body.GlobalTransform * Body.CenterOfMass);
    }

    /// <summary>Server-only: this prop was just THROWN, so it rides
    /// <c>PropPhysics.ThrowSpeedCapMps</c> until its launch energy is spent (P2's one
    /// exception). Called from the release funnel rather than inferred from the velocity,
    /// because "was this a throw" is a fact about the verb and not about a number.</summary>
    public void NoteThrownServer() => _speedCapMps = PropPhysics.ThrowSpeedCapMps;

    /// <summary>
    /// <b>Server-only: hold this loose prop inside the bar</b> (P2). Called once per physics tick
    /// from the server's prop loop, after the solver has run.
    ///
    /// <para>The real guarantee is at the impulse — nothing this build DOES to a prop can hand it
    /// more than <c>PropPhysics.MaxPropSpeedMps</c> (see <c>PropPhysics.WakeSpeed</c>). This is
    /// the backstop for the energy a SOLVER can invent: a prop wedged between a shelf and a
    /// depenetration, a stack resolving a deep overlap in one step. Those are the events a player
    /// reads as "it freaked out", and they do not come in through any call this file makes.</para>
    ///
    /// <para>Returns true when it bit, so the caller can count it — P2 asks for the count to be
    /// ~0 in ordinary play, which is a claim that only a counter can support.</para>
    /// </summary>
    public bool ServerClampMotion(out float fromHorizontalMps)
    {
        fromHorizontalMps = 0f;
        if (!IsServer || !IsInstanceValid(Body))
            return false;
        fromHorizontalMps = HorizontalSpeedMps;
        // The throw's allowance is spent the first tick its horizontal energy is gone, and never
        // comes back for this episode; a fresh throw sets it again through NoteThrownServer.
        if (_speedCapMps > PropPhysics.MaxPropSpeedMps
            && PropPhysics.ThrowEnergySpent(Body.LinearVelocity))
        {
            _speedCapMps = PropPhysics.MaxPropSpeedMps;
        }

        bool bit = false;
        if (PropPhysics.ClampVelocity(Body.LinearVelocity, _speedCapMps, out Vector3 v))
        {
            Body.LinearVelocity = v;
            bit = true;
        }
        if (PropPhysics.ClampSpin(Body.AngularVelocity, out Vector3 w))
        {
            Body.AngularVelocity = w;
            bit = true;
        }
        return bit;
    }

    /// <summary>This prop's speed right now, for the suite's freakout bar and the clamp log.
    /// Reads the body, which on the server is the simulation itself.</summary>
    public float SpeedMps => IsInstanceValid(Body) ? Body.LinearVelocity.Length() : 0f;

    /// <summary>This prop's horizontal speed — the component <c>PropPhysics.ClampVelocity</c>
    /// actually bounds, and therefore the one a clamp line has to report. The magnitude is
    /// dominated by the FALL for anything knocked off a surface, which made the log read like a
    /// clamp that had not worked.</summary>
    public float HorizontalSpeedMps => IsInstanceValid(Body)
        ? new Vector3(Body.LinearVelocity.X, 0f, Body.LinearVelocity.Z).Length()
        : 0f;

    /// <summary>This prop's current ceiling, metres per second — the bar, or a throw's larger
    /// allowance while its launch energy is unspent. Read by the suite so a clamp line can be
    /// checked against the cap that was actually in force.</summary>
    public float SpeedCapMps => _speedCapMps;
}
