using Godot;
using MpFoundation.Game.Presentation;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// A physics prop an avatar can pick up, carry, drop, and throw. While held it
/// freezes and tracks the holder's carry anchor; on release it rejoins the physics
/// world with whatever velocity the release gave it. Ownership rules live in
/// CarryController (pure logic); this node is the physical embodiment. The future
/// net layer replicates a carryable as (transform, holderId) and calls the same
/// OnPickedUp/OnDropped/OnThrown entry points on remote instances.
/// </summary>
public partial class Carryable : RigidBody3D, ICarryable, IHighlightable
{
    public const string Group = "sandbox_carryable";

    /// <summary>The physical form a code-built (runtime-spawned) Carryable takes. Authored props
    /// ignore this entirely — they carry their own mesh and collider from the .tscn (see
    /// <see cref="_Ready"/>'s authored-prefab branch). Append-only for the same reason
    /// <c>PropKind</c> is: <c>NetworkedProp</c> maps one onto the other, and <see cref="Kind"/> is
    /// an <c>[Export]</c> serialized into Crate.tscn/Sphere.tscn, so reordering these would
    /// silently repoint an authored prop at a different shape.
    ///
    /// The MVP extraction removed the slot items and the arm-carried props
    /// from both enums together, so the mirror holds: <c>Crate</c> and <c>Ball</c> — the only
    /// two an authored .tscn serializes — keep ordinals 0 and 1, and no call site hardcodes an
    /// ordinal.</summary>
    public enum Shape { Crate, Ball }

    private const float FollowLerp = 22f;   // held-item chase; slightly laggy = alive

    /// <summary>How far an armful-posed SLOT item rides ABOVE the carry mount, as a fraction of
    /// its own half-height (W7-8, 2026-08-30).
    ///
    /// <b>Why a load has to be lifted at all.</b> The armful pose puts both hands UNDER the load
    /// and slightly apart — <c>AvatarVisual.MeasureCarryTargets</c>: <i>"both hands straddle the
    /// same load and sit UNDER it"</i> — at a drop scaled off the BODY's half-width, because until
    /// now the pose layer had no idea what the load was. A 0.44 m golden cube centred on the mount
    /// therefore swallows both hands whole: measured on the first fixed capture, the pose changed
    /// and the arms were still invisible, because they were inside the cube.
    ///
    /// <b>VALUE CALL (W7-8, mine): 0.70.</b> On the player's body the armful hands sit ~0.065 m
    /// under the mount and a crate's half-height is 0.22 m, so 0.70 puts the cube's underside
    /// within a centimetre of the hands — carried, not impaled. Scaled off the LOAD rather than
    /// typed, so a bigger load rides higher by its own bulk and a small one barely moves, and so
    /// this needs no second copy of a constant that lives in the pose layer.
    ///
    /// <b>It applies to arm-POSED SLOT items only</b> — see <see cref="LoadLiftM"/>. Firewood and
    /// stone are deliberately untouched: they were calibrated together with the pose in CARRY-1,
    /// nobody has complained about them, and W7-8's acceptance criterion 3 is that nothing which
    /// was already arm-carried behaves differently.</summary>
    public const float ArmfulLoadLiftFraction = 0.70f;

    private const float ThunkSpeedThreshold = 2.0f;
    private const float ThunkCooldownSec = 0.4f;
    // Outline shell (INTERACTION-BIBLE §1 affordance, tide-polish-pass BUILD-SPEC §4): a
    // uniformly-scaled duplicate of the SAME mesh resource, rendered front-face-culled so only
    // its enlarged backfaces peek out past the real silhouette — the classic cheap "inverted
    // hull" outline. One extra MeshInstance3D per Carryable, but Visible only while THIS prop
    // is InteractHighlighter's current pick (the same InteractTargeting.Pick winner the "E"
    // chip tracks — never re-derived here), so at most one instance in the whole scene ever
    // actually draws. 8% is a value call, not a fork: enough to read as a ring at arm's length
    // on both the 0.44 m crate and the 0.26 m-radius ball, not so much it reads as a second object.
    private const float OutlineScale = 1.08f;
    // Below any legitimate floor (the escape slice bottoms out around y=-5). A prop that
    // gets here has left the world — thrown through a wall, off a ledge — so we recover it.
    // Public: NetworkedProp's server-side loose loop and SandboxAvatar's server kill-plane
    // reuse this exact threshold, so "out of bounds" means the same thing everywhere.
    /// <remarks>An alias, never a second value. It stopped being a <c>const</c> when
    /// <see cref="MpFoundation.Net.NetProfile.KillPlaneY"/> became per-world overridable — a
    /// compile-time copy would have frozen every prop and every avatar on the flat-world default
    /// while the world it is standing in used a different one, which is exactly the
    /// two-out-of-bounds-floors bug the override exists to end.</remarks>
    public static float KillPlaneY => MpFoundation.Net.NetProfile.KillPlaneY;

    [Export] public Shape Kind { get; set; } = Shape.Crate;
    [Export] public Color Tint { get; set; } = new(0.93f, 0.78f, 0.55f);

    /// <summary>This item type's event→cosmetics mapping. Crate/Sphere ship on the
    /// shared default; a future item type overrides it in its own scene, in data.</summary>
    [Export] public PresentationProfile? Profile { get; set; }

    private const string DefaultProfilePath = "res://assets/items/default/prop_presentation.tres";

    /// <summary>Set by NetworkedProp on a networked prop's body. When true, this class's own
    /// KillPlaneY safety net (below) stands down — the networked layer owns out-of-bounds
    /// recovery instead (server-authoritative, latching to the exact spawn HomeTransform via a
    /// replicated state transition), and the two must never race the same threshold on the same
    /// body. Offline/sandbox Carryables leave this false and keep the original self-contained
    /// fallback exactly as before.</summary>
    public bool OwnedByNetwork { get; set; }

    /// <summary>In someone's hands, by either of the two mechanisms: the anchor chase
    /// (<see cref="_holder"/>) or the holder-side feel spring (<see cref="_springHeld"/>, CARRY-1).
    ///
    /// <para><b>Both arms matter and the second one is easy to forget.</b> A spring-carried prop
    /// has no <see cref="CarryController"/> at all — the spring drives the body directly — so a
    /// bare <c>_holder != null</c> would read FALSE for the thing in the local player's hands, and
    /// every consumer of this flag would then be wrong in a different way: the prop would show its
    /// hover outline while held, <c>SandboxAvatar.FindNearestCarryable</c> would offer it back as
    /// a free grab, and the guard that stops E from discarding what you are carrying when
    /// somebody else wins a race would stop seeing it.</para></summary>
    public bool IsHeld => _holder != null || _springHeld;

    public float MassKg => (float)Mass;

    /// <summary>Pulsed by the world script on the current pickup candidate.</summary>
    public bool Highlighted { get; set; }

    private CarryController? _holder;

    // CARRY-1: held by the holder-side feel spring rather than by an anchor chase. See IsHeld.
    private bool _springHeld;

    private Node3D _visual = null!;
    private StandardMaterial3D _material = null!;
    private MeshInstance3D? _outlineMesh;
    private static StandardMaterial3D? _sharedOutlineMaterial;
    private Vector3 _visualScale = Vector3.One;
    private Vector3 _visualScaleVel;
    private double _thunkCooldown;
    private double _pulseTime;
    /// <summary>The emission energy the material arrived with — authored in the .tscn for a
    /// designer-placed prop, 0 for the code-built fallback. This is the highlight pulse's
    /// RESTING value; see _PhysicsProcess. Captured once in _Ready because the pulse writes
    /// the same material every frame and would otherwise have nothing to return to.</summary>
    private float _baseGlow;
    private Transform3D _lastAnchor = Transform3D.Identity;
    private Vector3 _homePosition;   // spawn spot; recovery fallback before the prop is ever held
    private bool _homeSet;
    private bool _everHeld;
    private float _halfHeightM;

    public override void _Ready()
    {
        Profile ??= GD.Load<PresentationProfile>(DefaultProfilePath);

        AddToGroup(Group);
        ContactMonitor = true;
        MaxContactsReported = 4;
        // Continuous collision detection: a thrown prop can otherwise tunnel through a
        // thin wall/door in one physics step and end up out of bounds. This keeps it in.
        ContinuousCd = true;
        BodyEntered += OnBodyEntered;

        // Authored-prefab detection (Crate.tscn/Sphere.tscn, and anything placed the same
        // way): a level-designer-placed prop already carries its own Visual/MeshInstance3D
        // + CollisionShape3D, saved in the .tscn and visible/movable in the editor viewport
        // — the whole point of an authored prop. When both are present we bind to them and
        // build NOTHING in code; the authored material's baked albedo/emission is
        // authoritative and the Tint export is NOT applied over it (Tint stays meaningful
        // only for the code-built fallback below). Absent either one — the offline Sandbox
        // path, which has never been authored — we fall back to the original code-built
        // shape exactly as before, so Run-SandboxTest is untouched.
        //
        // "Authoritative" is enforced, not just asserted: the emission energy the material
        // arrives with is captured into _baseGlow below and is the value the highlight pulse
        // rests at. Until this was fixed, the pulse in _PhysicsProcess lerped every prop's
        // emission toward absolute 0 each frame and this comment was simply false.
        MeshInstance3D shapeMesh;
        var authoredVisual = GetNodeOrNull<Node3D>("Visual");
        var authoredMesh = authoredVisual?.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        var authoredCollider = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (authoredVisual != null && authoredMesh != null && authoredCollider != null)
        {
            _visual = authoredVisual;
            _material = authoredMesh.MaterialOverride as StandardMaterial3D ?? new StandardMaterial3D();
            authoredMesh.MaterialOverride = _material; // the highlight/pop pulse below mutates this exact instance
            shapeMesh = authoredMesh;
        }
        else
        {
            _material = new StandardMaterial3D
            {
                AlbedoColor = Tint,
                Roughness = 0.75f,
                EmissionEnabled = true,
                Emission = Tint,
                EmissionEnergyMultiplier = 0f,
            };
            _visual = new Node3D { Name = "Visual" };
            AddChild(_visual);
            shapeMesh = BuildShape();
        }

        // Outline shell. Built unconditionally (headless included, like the shimmer material
        // above it) rather than gated the way BlobShadow.Attach gates itself: BlobShadow skips
        // headless because it pays a real per-frame raycast every physics tick that nobody
        // would ever see; this is a one-time node construction with no recurring cost, and a
        // dedicated server's absent renderer already means it submits zero draw calls no
        // matter what Visible says. Keeping it unconditional also means the headless self-test
        // suite can actually assert the toggle logic below, not just trust it unseen. Shares
        // the shape mesh RESOURCE (no copy, no extra vertex data) so the box and the ball each
        // get a correctly-proportioned shell for free; only the transform (parent's Scale
        // spring included, via the child relationship) and the front-cull material differ from
        // the item's own mesh instance.
        _outlineMesh = new MeshInstance3D
        {
            Name = "Outline",
            Mesh = shapeMesh.Mesh,
            MaterialOverride = _sharedOutlineMaterial ??= BuildOutlineMaterial(),
            Scale = Vector3.One * OutlineScale,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _visual.AddChild(_outlineMesh);

        // The load's own vertical half-extent, taken from whichever mesh this prop ended up with
        // (authored or code-built). Read from the MESH rather than the collider because it is what
        // the eye judges "resting on the hands" against, and because the two collider paths do not
        // share a node name. Zero for a prop with no mesh, which LoadLiftM reads as "no lift".
        _halfHeightM = shapeMesh.Mesh is { } loadMesh ? loadMesh.GetAabb().Size.Y * 0.5f : 0f;

        // Both paths converge here: whatever emission the material carries at this moment is
        // the prop's resting glow. Authored props keep their .tscn value; the code-built
        // fallback set 0 above, so it behaves exactly as it did before this was introduced.
        _baseGlow = _material.EmissionEnergyMultiplier;

        // Grounding without a shadow map (Bible §3/§4) — rides the prop even while
        // carried or thrown, which is exactly when the eye needs the height cue.
        BlobShadow.Attach(this, radius: 0.30f);
    }

    private MeshInstance3D BuildShape()
    {
        // GREYBOX SHAPES. Crate/Ball are the foundation's originals. What a greybox prop has to
        // earn (INTERACTION-BIBLE 1: an interactable must READ as interactable before the player
        // touches it) is *findability*, and that is carried by proportion + placement + the
        // existing shimmer/"E" chip, not by silhouette fidelity.
        Mesh mesh;
        Godot.Shape3D collider;
        switch (Kind)
        {
            case Shape.Ball:
                mesh = new SphereMesh { Radius = 0.26f, Height = 0.52f };
                collider = new SphereShape3D { Radius = 0.26f };
                break;
            default:
                mesh = new BoxMesh { Size = new Vector3(0.44f, 0.44f, 0.44f) };
                collider = new BoxShape3D { Size = new Vector3(0.44f, 0.44f, 0.44f) };
                break;
        }
        var meshInstance = new MeshInstance3D { Mesh = mesh, MaterialOverride = _material };
        _visual.AddChild(meshInstance);
        AddChild(new CollisionShape3D { Shape = collider });
        return meshInstance;
    }

    /// <summary>One shared, stateless outline material for every Carryable in the process —
    /// unshaded (no per-fragment lighting cost) flat amber, front-face-culled so only the
    /// scaled shell's backfaces render past the real mesh's silhouette. Depth test/write stay
    /// at their defaults: the shell is a real 3D shape a wall still occludes, not a
    /// screen-space overlay, per the perf brief's "nothing screen-space" constraint.</summary>
    private static StandardMaterial3D BuildOutlineMaterial() => new()
    {
        AlbedoColor = new Color(1f, 0.86f, 0.32f), // warm amber; reads against grass/sand/water alike
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Front,
    };

    // --- ICarryable ------------------------------------------------------------------

    /// <summary>How far this prop rides above the carry mount right now, metres — 0 for almost
    /// everything. See <see cref="ArmfulLoadLiftFraction"/> for why it exists.
    ///
    /// <para>Re-derived per call rather than cached, so a lift is always read against the hold
    /// as it stands rather than latched at bind time.</para>
    ///
    /// <para>The kind comes from the owning <c>NetworkedProp</c> where there is one, because
    /// <see cref="Kind"/> is an <c>[Export]</c> and Godot does not apply a nested PackedScene
    /// instance's exported script properties on this build — every authored prop reads back as
    /// <c>Shape.Crate</c> regardless of what its .tscn says (<c>PropManager.AuthoredKindOf</c>
    /// records the same trap). Offline, with no NetworkedProp, the code-built value is correct
    /// because a code-built prop is the only kind that path spawns.</para></summary>
    private float LoadLiftM()
    {
        if (_halfHeightM <= 0f)
            return 0f;
        Props.NetworkedProp? np = GetParentOrNull<Props.NetworkedProp>();
        MpFoundation.Net.PropKind kind = np != null ? np.Kind : (MpFoundation.Net.PropKind)(int)Kind;
        return Props.PropManager.TakesLoadLift(kind) ? _halfHeightM * ArmfulLoadLiftFraction : 0f;
    }

    /// <summary>The anchor this prop actually rides, which is the holder's carry mount plus
    /// <see cref="LoadLiftM"/> along the mount's OWN up axis — the mount's basis, not the world's,
    /// so the load stays on the hands through the body's lean and roll rather than shearing out of
    /// them.
    ///
    /// <para><b>Internal rather than private because a test has to be able to ask.</b>
    /// <c>SandboxSelfTest.phys_pickup_snaps_to_anchor</c> pins "the prop is already where it belongs
    /// on the very first held tick — no multi-frame slide up from rest", and that invariant is about
    /// the anchor the prop RIDES. Measured against the bare mount instead, the check turns red for a
    /// lifted load while the invariant it names is still perfectly satisfied — the instrument going
    /// wrong, not the behaviour.</para></summary>
    internal Transform3D EffectiveCarryAnchor(Transform3D mount)
    {
        float lift = LoadLiftM();
        return lift <= 0f
            ? mount
            : new Transform3D(mount.Basis, mount.Origin + mount.Basis.Y.Normalized() * lift);
    }

    public virtual void OnPickedUp(CarryController holder)
    {
        _holder = holder;
        _everHeld = true;
        // Order matters. Kill collision FIRST — before the body is frozen or moved —
        // so a prop the holder is standing on can never push them as it detaches or
        // travels to the hand. With layer/mask 0 it collides with nothing, so grabbing
        // an item underfoot just drops the holder into a natural fall (no eject, no
        // launch). Then freeze so physics stops simulating it.
        CollisionLayer = 0;
        CollisionMask = 0;
        FreezeMode = FreezeModeEnum.Kinematic;
        Freeze = true;
        // Snap straight to the carry anchor this same frame. The old code left the prop
        // at its rest position and let _PhysicsProcess lerp it up over several frames —
        // a visible slide/pop from the floor into the hand. Placing it at the anchor now
        // means no travel and no one-frame flash; the gentle bob/chase resumes from here.
        if (holder.AnchorProvider != null)
        {
            Transform3D anchor = EffectiveCarryAnchor(holder.AnchorProvider());
            _lastAnchor = anchor;
            GlobalPosition = anchor.Origin;
            GlobalBasis = anchor.Basis.Orthonormalized();
        }
        PunchScale(new Vector3(1.25f, 1.25f, 1.25f));
        ActorFx.Fire(GetParent(), Profile, ActorEvent.PickedUp, GlobalPosition);
    }

    /// <summary>
    /// <b>Picked up by the holder-side feel spring</b> (CARRY-1) — everything
    /// <see cref="OnPickedUp"/> does EXCEPT move the prop, and without a
    /// <see cref="CarryController"/> to chase.
    ///
    /// <para>The omission is the whole method. <see cref="OnPickedUp"/> snaps the prop to the
    /// anchor on the grab frame because its chase would otherwise slide it visibly up off the
    /// floor; a spring seeded at the item and allowed to travel is the better answer to the same
    /// problem (<c>CarrySpring.Seed</c>), and snapping first would throw away the one frame in
    /// which the grab reads. Freeze, collision-off, the pickup pop and the pickup cue are
    /// identical, because none of them is about where the prop is.</para>
    /// </summary>
    public virtual void OnPickedUpBySpring()
    {
        _springHeld = true;
        _everHeld = true;
        // Same order and same reasoning as OnPickedUp: collision dies FIRST, so a prop the holder
        // is standing on can never push them as it detaches.
        CollisionLayer = 0;
        CollisionMask = 0;
        FreezeMode = FreezeModeEnum.Kinematic;
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        PunchScale(new Vector3(1.25f, 1.25f, 1.25f));
        ActorFx.Fire(GetParent(), Profile, ActorEvent.PickedUp, GlobalPosition);
    }

    /// <summary>Deliberate drops get a light toss arc off the holder's facing —
    /// props leave the hands with a little life instead of dead-falling.</summary>
    public virtual void OnDropped()
    {
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Dropped, GlobalPosition);
        Release(-_lastAnchor.Basis.Z * 1.3f + Vector3.Up * 2.2f);
    }

    public virtual void OnThrown(Vector3 impulse) => Release(impulse);

    /// <summary><b>Set down, not dropped</b> (CARRY-1's place verb): rejoin physics with no
    /// velocity and NO SPIN. The random tumble <see cref="Release(Vector3)"/> applies is what
    /// makes a discarded prop look discarded; applying it to a placement would spin away the
    /// orientation the player just lined the object up in, which is the one thing the verb
    /// exists to preserve.</summary>
    public virtual void OnPlaced()
    {
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Dropped, GlobalPosition);
        Release(Vector3.Zero, Vector3.Zero);
    }

    /// <summary>Networked-follow only: on a peer that does NOT simulate this prop's physics
    /// (every client, once it's Loose), stop chasing the holder's anchor without touching
    /// freeze/collision/velocity — NetworkedProp itself owns those for a frozen-kinematic
    /// stream-follower. Without this, <see cref="_holder"/> stays set on every non-authority
    /// peer (only the server's own Release, via OnThrown/OnDropped, ever clears it), so this
    /// class's own anchor-chase in <see cref="_PhysicsProcess"/> keeps fighting the stream
    /// follow every tick — the bug this method closes.</summary>
    public void ReleaseHoldForNetworkFollow()
    {
        _holder = null;
        _springHeld = false;
    }

    private void Release(Vector3 velocity) =>
        Release(velocity, new Vector3(GD.Randf() * 4 - 2, GD.Randf() * 4 - 2, GD.Randf() * 4 - 2));

    private void Release(Vector3 velocity, Vector3 angular)
    {
        _holder = null;
        _springHeld = false;
        Freeze = false;
        CollisionLayer = 1;
        CollisionMask = 1;
        LinearVelocity = velocity;
        AngularVelocity = angular;
    }

    // --- Behaviour ---------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (!_homeSet)
        {
            _homePosition = GlobalPosition;
            _homeSet = true;
        }

        if (_holder?.AnchorProvider != null)
        {
            Transform3D anchor = EffectiveCarryAnchor(_holder.AnchorProvider());
            _lastAnchor = anchor;
            // Chase the holder's carry anchor (slightly laggy = alive). The gentle bob/sway now
            // lives on the anchor itself — SandboxAvatar rides it on AvatarVisual.CarryBobOffset so
            // it's synced to the actual waddle rhythm — and the held prop inherits that motion
            // through this same follow, instead of the old fixed, un-synced sine that lived here.
            float w = 1f - Mathf.Exp(-FollowLerp * dt);
            GlobalPosition = GlobalPosition.Lerp(anchor.Origin, w);
            GlobalBasis = GlobalBasis.Orthonormalized().Slerp(anchor.Basis.Orthonormalized(), w);
        }
        else if (!OwnedByNetwork && GlobalPosition.Y < KillPlaneY)
        {
            // Fell out of the world. Return it to where it was last held (right where the
            // player dropped it) so a carryable can never be permanently lost. CCD makes
            // this rare; this is the guaranteed safety net.
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            GlobalPosition = (_everHeld ? _lastAnchor.Origin : _homePosition) + Vector3.Up * 0.4f;
        }

        // Scale spring for pickup pops and landing squashes.
        Vector3 accel = (Vector3.One - _visualScale) * 160f - _visualScaleVel * 11f;
        _visualScaleVel += accel * dt;
        _visualScale += _visualScaleVel * dt;
        _visual.Scale = _visualScale;

        // Candidate highlight: gentle emission breathing on the nearest grabbable, ON TOP OF
        // the authored resting emission (_baseGlow) — never toward absolute zero. This line
        // used to target 0 when unhighlighted, which erased whatever the .tscn authored: a
        // 2.5-energy gold prop fell under the Environment's glow gate in ~0.055 s, so no
        // authored prop in the game could ever bloom. _baseGlow is 0 on the code-built
        // fallback, so that path's behaviour below is bit-for-bit what it always was.
        //
        // The pulse amplitude is scaled by (1 + _baseGlow): the "1" is the original absolute
        // amount, which is what keeps the affordance alive on a 0 baseline; the "_baseGlow"
        // term makes it proportional, which is what keeps it legible on a bright baseline.
        // Purely proportional would multiply the fallback's 0 and silently delete the
        // highlight; purely additive would be a ±10% wobble on 2.5 and read as no highlight
        // at all. At baseline B the highlight breathes over [B + 0.15(1+B), B + 0.55(1+B)],
        // i.e. never below rest, and at least 1.15x rest for every B.
        _pulseTime += delta;
        float pulse = Highlighted ? 0.35f + 0.2f * Mathf.Sin((float)_pulseTime * 6f) : 0f;
        float targetGlow = _baseGlow + pulse * (1f + _baseGlow);
        _material.EmissionEnergyMultiplier = Mathf.Lerp(_material.EmissionEnergyMultiplier, targetGlow, 10f * dt);

        // Held-object rule (tide-polish-pass BUILD-SPEC §8.5): the outline never renders on a
        // held item. This is a hard gate on IsHeld, not just a consequence of the poll clearing
        // Highlighted a beat later — InteractHighlighter polls at 10 Hz (PollIntervalSec), so a
        // pickup and the next poll can be up to 100 ms apart, and FindNearestCarryable already
        // excludes held props from candidates. Without the explicit "&& !IsHeld" here, THIS
        // frame's outline would still be true for up to that 100 ms window and the shell would
        // flash on the item now riding in the player's hand. Gating on IsHeld directly makes the
        // no-flicker guarantee true by construction instead of by poll-timing luck, and the aimed
        // drop/throw target keeps the existing "E" prompt only, exactly as before this change.
        if (_outlineMesh != null)
            _outlineMesh.Visible = Highlighted && !IsHeld;

        _thunkCooldown -= delta;
    }

    private void OnBodyEntered(Node body)
    {
        if (IsHeld || _thunkCooldown > 0)
            return;
        if (LinearVelocity.Length() < ThunkSpeedThreshold)
            return;
        _thunkCooldown = ThunkCooldownSec;
        PunchScale(new Vector3(1.2f, 0.78f, 1.2f));
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Impact, GlobalPosition);
    }

    protected void PunchScale(Vector3 to)
    {
        _visualScale = to;
        _visualScaleVel = Vector3.Zero;
    }

    /// <summary>Test hook: is the outline shell currently flagged visible.</summary>
    internal bool OutlineVisible => _outlineMesh?.Visible ?? false;
}
