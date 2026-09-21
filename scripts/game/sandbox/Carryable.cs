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
    /// <remarks>SFX-1 (2026-09-19) appended <see cref="Can"/>, <see cref="Box"/> and
    /// <see cref="Produce"/>, mirroring <c>PropKind</c> 1:1 as this enum always has. Appended,
    /// never inserted, for the reason above.</remarks>
    public enum Shape { Crate, Ball, Can, Box, Produce }

    // --- The code-built product shapes (SFX-1) ----------------------------------------------
    //
    // These are the same dimensions the three authored prefabs in scenes/game/props/ carry, and
    // they are here as well as there because a prop has TWO birth paths: an authored .tscn a
    // level places, and PropManager.ServerSpawn building one in code (which is what
    // --seed-test-props uses). The suite seeds forty props in a heap; nobody is going to author
    // forty. Keeping the numbers in one place means the seeded fixture and the shelved product
    // are the same object, which is the only way a voice-budget measurement on the fixture says
    // anything about the game.

    /// <summary>A 355 ml can: 35 mm radius, 120 mm tall.</summary>
    public const float CanRadiusM = 0.035f;
    public const float CanHeightM = 0.12f;
    public const float CanMassKg = 0.35f;

    /// <summary>A cereal box: 190 x 280 x 60 mm. Not a cube, and that is load-bearing — see
    /// <see cref="ShapeFromCollider"/>.</summary>
    public static readonly Vector3 BoxSizeM = new(0.19f, 0.28f, 0.06f);
    public const float BoxMassKg = 0.4f;

    /// <summary>A piece of produce: an 80 mm-radius sphere.</summary>
    public const float ProduceRadiusM = 0.08f;
    public const float ProduceMassKg = 0.25f;

    /// <summary>Midpoint between <see cref="ProduceRadiusM"/> (0.08) and the ball's 0.26 — the
    /// radius at which <see cref="ShapeFromCollider"/> stops calling a sphere produce and starts
    /// calling it the foundation's ball.</summary>
    private const float ProduceVsBallRadiusM = 0.17f;

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

    /// <summary>Below this relative contact speed a collision makes no sound at all. Public since
    /// SFX-1 because it is the LOW end of the intensity ramp — see
    /// <see cref="ImpactIntensity"/> — and a test that restated it would be testing its own
    /// copy.</summary>
    public const float ThunkSpeedThreshold = 2.0f;

    /// <summary>The relative contact speed at which a hit is as hard as it gets: intensity 1.
    /// Eight metres per second is a prop thrown hard into a wall from arm's length; everything
    /// above it clamps, because a scale with no top is a scale nobody can author against.</summary>
    public const float ImpactSpeedCeiling = 8.0f;

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

    /// <summary><b>What this prop is made of</b> (SFX-1), and therefore which
    /// <see cref="PresentationProfile"/> it resolves to when no explicit <see cref="Profile"/> is
    /// authored. Default <see cref="PropMaterial.Wood"/>, which is the shared crate/ball profile,
    /// so nothing that existed before this packet changes sound.
    ///
    /// <para><b>An export, and a fallback behind it, because of a measured Godot trap.</b>
    /// <c>PropManager.AuthoredKindOf</c> records it: this project's Godot/Mono build does not
    /// apply a NESTED PackedScene instance's own exported script properties, so
    /// <c>Crate.tscn</c>'s authored <c>kind</c> and <c>tint</c> both read back as the C# field's
    /// default for every authored prop. A level authors a prop as
    /// <c>[node name="Body" parent="Prop_0" instance=ExtResource("Can.tscn")]</c>, which is
    /// exactly that shape — so this export alone would be silently ignored on every prop a level
    /// designer ever places, and the whole packet would work only for code-built fixtures.
    /// <see cref="ResolveMaterial"/> therefore falls back to the prop's COLLIDER, which is a
    /// native property and provably survives instancing.</para></summary>
    [Export] public PropMaterial Material { get; set; } = PropMaterial.Wood;

    /// <summary>This item type's event→cosmetics mapping. Crate/Sphere ship on the
    /// shared default; a future item type overrides it in its own scene, in data.
    ///
    /// <para>Left unset (the normal case), it is resolved in <see cref="_Ready"/> from
    /// <see cref="ResolveMaterial"/>. Set explicitly in a .tscn or by spawning code, the explicit
    /// value WINS and no material lookup happens at all — that is the escape hatch for a prop
    /// that needs its own voice rather than its material's.</para></summary>
    [Export] public PresentationProfile? Profile { get; set; }

    private const string DefaultProfilePath = "res://assets/items/default/prop_presentation.tres";

    /// <summary>Where each material's profile lives. One function rather than four constants so
    /// the convention — <c>assets/items/&lt;material&gt;/prop_presentation.tres</c> — is stated
    /// once and a new material is one enum member plus one folder.</summary>
    public static string ProfilePathFor(PropMaterial material) => material switch
    {
        PropMaterial.Tin => "res://assets/items/tin/prop_presentation.tres",
        PropMaterial.Cardboard => "res://assets/items/cardboard/prop_presentation.tres",
        PropMaterial.Produce => "res://assets/items/produce/prop_presentation.tres",
        _ => DefaultProfilePath,
    };

    /// <summary><b>The same classification, pointed at the FEEL instead of at the sound</b>
    /// (PHYS-1, 2026-09-20). One `PropMaterial` picks both, which is the whole point of
    /// `assets/physics/README.md`'s table: a prop that sounds like tin and behaves like cardboard
    /// would be two classifications of one object that nothing keeps in step.
    ///
    /// <para><b>Why the code-built path needs this at all.</b> An AUTHORED prefab carries its
    /// `physics_material_override` in its own `.tscn` and never reaches here. A prop built in
    /// code — <c>--seed-test-props</c>, the offline sandbox — has no `.tscn` and would otherwise
    /// get Godot's bare defaults. <c>Produce.tscn</c>'s own header already records what that
    /// costs: <i>"the two birth paths have to agree or the seeded fixture stops being the same
    /// object as the shelved product, which is the whole reason the dimensions are shared
    /// constants."</i> It was written about damping; it is just as true of friction.</para></summary>
    public static string PhysicsMaterialPathFor(PropMaterial material) => material switch
    {
        PropMaterial.Tin => "res://assets/physics/tin.tres",
        PropMaterial.Cardboard => "res://assets/physics/cardboard.tres",
        PropMaterial.Produce => "res://assets/physics/produce.tres",
        _ => "res://assets/physics/wood.tres",
    };

    /// <summary>The material each shape is made of, when nothing says otherwise. Pure, and public
    /// so the Godot-free suite can pin the mapping — this is the table SHELF-1 will rely on when
    /// it fills the aisles.</summary>
    public static PropMaterial MaterialFor(Shape shape) => shape switch
    {
        Shape.Can => PropMaterial.Tin,
        Shape.Box => PropMaterial.Cardboard,
        Shape.Produce => PropMaterial.Produce,
        _ => PropMaterial.Wood,
    };

    /// <summary><b>A prop's shape read off its physical collider</b>, which is the one thing about
    /// an authored prop that survives nested-PackedScene instancing on this build (see
    /// <see cref="Material"/> and <c>PropManager.AuthoredKindOf</c>, which now delegates here).
    ///
    /// <para>Two of the five discriminations are free — a cylinder is only ever a can, and there
    /// is nothing else it could be. The other two are size, and they are written as MEANINGFUL
    /// tests rather than thresholds wherever possible: the foundation's crate is a cube and a
    /// cereal box is emphatically not, so "is this box a cube" separates them without a magic
    /// number. Only the sphere split needs one, and <see cref="ProduceVsBallRadiusM"/> sits
    /// halfway between the two authored radii (0.08 and 0.26) rather than next to either.</para>
    ///
    /// <para>Pure and static so the unit suite can pin every branch; it takes the shape resource
    /// rather than the node so nothing about it needs a scene tree.</para></summary>
    public static Shape ShapeFromCollider(Godot.Shape3D? collider) => collider switch
    {
        CylinderShape3D => Shape.Can,
        SphereShape3D s => s.Radius <= ProduceVsBallRadiusM ? Shape.Produce : Shape.Ball,
        BoxShape3D b => IsCube(b.Size) ? Shape.Crate : Shape.Box,
        _ => Shape.Crate,
    };

    private static bool IsCube(Vector3 size) =>
        Mathf.IsEqualApprox(size.X, size.Y) && Mathf.IsEqualApprox(size.Y, size.Z);

    /// <summary>This prop's material, from the first source that has an opinion:
    /// the <see cref="Material"/> export when it is not the <see cref="PropMaterial.Wood"/>
    /// default (the code-built path, and the authored path on any build where Godot does apply
    /// nested exports), then the collider (the authored path on THIS build), and finally Wood.
    ///
    /// <para>Ordering matters and it is this way round deliberately: the export is what an author
    /// wrote down and the collider is an inference, so the explicit statement wins wherever it
    /// actually arrives.</para></summary>
    private PropMaterial ResolveMaterial()
    {
        if (Material != PropMaterial.Wood)
            return Material;
        return MaterialFor(ShapeFromCollider(GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape));
    }

    /// <summary><b>How hard a contact at this relative speed reads</b>, on 0..1. Linear from
    /// <see cref="ThunkSpeedThreshold"/> (the speed below which nothing sounds at all, so
    /// intensity 0 is exactly the quietest audible hit) to <see cref="ImpactSpeedCeiling"/>,
    /// clamped at both ends.
    ///
    /// <para>Pure and static because the gate asks for monotonic-and-clamped and that is a
    /// property of arithmetic, not of a rigid body. MECHANICS-BIBLE: a feedback channel scaled
    /// off a physical quantity has to be scaled off the RIGHT one — the relative speed at
    /// contact, not this body's own speed, so that a can standing still and struck by a thrown
    /// box is as loud as a thrown can striking a standing box.</para></summary>
    public static float ImpactIntensity(float relativeSpeed) =>
        Mathf.Clamp((relativeSpeed - ThunkSpeedThreshold) / (ImpactSpeedCeiling - ThunkSpeedThreshold), 0f, 1f);

    /// <summary><b>Which of two colliding props plays the impact: whichever one asks first.</b>
    /// The first call for a pair inside <paramref name="windowMsec"/> returns true and records
    /// the claim; a second call for the same pair inside the window returns false. Pure, and it
    /// takes its clock and its table as arguments, so the Godot-free suite can drive it.
    ///
    /// <para><b>Why a rule is needed at all.</b> Godot reports one prop-on-prop contact to BOTH
    /// bodies, so an unguarded handler fires the impact twice: two sounds a millisecond apart at
    /// almost the same position. That is not "louder", it is a flam, and with a shelf of cans
    /// going over it also spends two voices out of fourteen per collision.</para>
    ///
    /// <para><b>Why FIRST-COME, and not "the lower instance id wins", which is what this was
    /// until it was measured.</b> An id comparison is a total order and looks strictly better —
    /// no ties, no state, no clock. It has one fatal property: it picks the winner before
    /// knowing whether the winner will ever be asked. Measured on the stacked-can fixture in
    /// <c>tests/Run-MaterialSfxTest.ps1</c> — a can dropped onto a can that had already settled
    /// produced NO sound at all, run after run. Only the FALLING can got a <c>body_entered</c>
    /// (the resting one is frozen kinematic, and Godot never asked it anything), and it happened
    /// to hold the higher instance id because it was seeded second. The rule silenced the only
    /// body in a position to speak.</para>
    ///
    /// <para><b>First-come cannot fail that way</b>: whoever is actually asked, fires. It costs a
    /// small table and a clock, and it degrades in the right direction — the worst case is a
    /// second callback arriving after the window, which plays one extra sound rather than
    /// swallowing the only one.</para>
    ///
    /// <para>Process-local, and that is correct rather than merely tolerable: this is a
    /// client-local cosmetic decision taken independently on every peer, and nothing requires two
    /// peers to agree on WHICH body played it — only that each peer plays it once.</para></summary>
    public static bool ClaimPropOnPropContact(
        System.Collections.Generic.IDictionary<(ulong, ulong), ulong> claims,
        ulong a, ulong b, ulong nowMsec, ulong windowMsec)
    {
        (ulong, ulong) key = a < b ? (a, b) : (b, a);
        if (claims.TryGetValue(key, out ulong claimedAt) && nowMsec - claimedAt <= windowMsec)
            return false;
        claims[key] = nowMsec;
        // Opportunistic prune, so a long session of collisions cannot grow this without bound.
        // Only when the table is big enough to be worth walking, and only of entries too old to
        // suppress anything.
        if (claims.Count > PairClaimPruneAt)
        {
            var stale = new System.Collections.Generic.List<(ulong, ulong)>();
            foreach (System.Collections.Generic.KeyValuePair<(ulong, ulong), ulong> kv in claims)
            {
                if (nowMsec - kv.Value > windowMsec)
                    stale.Add(kv.Key);
            }
            foreach ((ulong, ulong) k in stale)
                claims.Remove(k);
        }
        return true;
    }

    /// <summary>How long one prop-on-prop contact stays claimed. Comfortably longer than the few
    /// physics ticks Godot can take to tell the second body, and far shorter than the 0.4 s
    /// per-body cooldown, so it can never merge two genuinely separate collisions.</summary>
    public const ulong PairClaimWindowMsec = 60;

    /// <summary>Table size at which <see cref="ClaimPropOnPropContact"/> bothers to sweep — above
    /// the number of distinct pairs one frame of a collapsing shelf can produce.</summary>
    private const int PairClaimPruneAt = 64;

    private static readonly System.Collections.Generic.Dictionary<(ulong, ulong), ulong> PairClaims = new();

    /// <summary>Set by NetworkedProp on a networked prop's body. When true, this class's own
    /// KillPlaneY safety net (below) stands down — the networked layer owns out-of-bounds
    /// recovery instead (server-authoritative, latching to the exact spawn HomeTransform via a
    /// replicated state transition), and the two must never race the same threshold on the same
    /// body. Offline/sandbox Carryables leave this false and keep the original self-contained
    /// fallback exactly as before.</summary>
    public bool OwnedByNetwork { get; set; }

    /// <summary><b>Where an accepted contact goes instead of straight to the speakers</b>
    /// (SFX-2, 2026-09-19). Set by <c>NetworkedProp</c> on a networked prop's body, alongside
    /// <see cref="OwnedByNetwork"/>; null on every offline/sandbox <see cref="Carryable"/>, which
    /// keeps the original local fire exactly as it was.
    ///
    /// <para><b>Why the fire moves off this class for networked props.</b> SFX-1 measured the
    /// defect and could not close it: a Loose prop on a non-authority peer is a FROZEN KINEMATIC
    /// body, and Godot reports such a body no contact at all — so <see cref="OnBodyEntered"/>
    /// never runs there, and the seeker heard nothing when the hider knocked a can off a shelf
    /// in the next aisle. Only the server sees every contact, so only the server can say what
    /// happened; it announces, and every peer INCLUDING THE HOST plays from the announcement.
    /// One path, one sound — if the host kept its local fire as well it would hear each hit
    /// twice, which is a flam rather than a louder hit.</para>
    ///
    /// <para><b>An <c>Action</c> rather than a call into the prop layer</b>, so this file — the
    /// engine-side physical body, shared with the offline feel sandbox — keeps knowing nothing
    /// about netcode. The delegate is owned by the node that already owns this body's networked
    /// identity, and it is that node which decides that a client reports nothing.</para>
    ///
    /// <para>The parameter is the 0..1 intensity from <see cref="ImpactIntensity"/>, already
    /// computed here because the relative-contact-speed rule lives here.</para></summary>
    public System.Action<float>? ImpactReporter { get; set; }

    /// <summary><b>Where a contact with ANOTHER PROP goes, whether or not it was loud enough to
    /// hear</b> (PHYS-1, 2026-09-20, ruling P1). Set by <c>NetworkedProp</c> beside
    /// <see cref="ImpactReporter"/>; null on every offline/sandbox body, which is unchanged.
    ///
    /// <para><b>The same signal, a second consumer — not a second monitor.</b> P1's wording is
    /// exact about this: <c>ContactMonitor</c> and <c>body_entered</c> already exist on this class
    /// for sound, and a resting prop that has to be woken is the same contact the sound layer is
    /// already being told about. What differs is the GATES. The impact fire is behind a 0.4 s
    /// per-body cooldown and a 2 m/s audibility floor, and neither is a fact about whether the
    /// thing that got hit should move: a can rolled into a pyramid at 1 m/s is inaudible and must
    /// still knock it over, and the second can of a collapsing stack is inside the first's cooldown
    /// and must still be knocked. So this reports from ABOVE both gates, with
    /// <c>PropPhysics.WakeSpeedThresholdMps</c> — a seventh of the audibility floor — as its
    /// own.</para>
    ///
    /// <para><b>What it is NOT.</b> It is not the wake itself and it carries no decision: the
    /// arguments are the other body and the speed, and the server's prop layer decides whether
    /// that body is a resting networked prop, whether it may be woken, and with what. This file
    /// stays the physical body and keeps knowing nothing about netcode, exactly as
    /// <see cref="ImpactReporter"/>'s own note says.</para></summary>
    public System.Action<Carryable, float>? BumpReporter { get; set; }

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

    /// <summary><b>The radius of the sphere that contains this prop however it is turned</b>, from
    /// its own mesh bounds — <c>CarryHold.BoundingRadiusM</c>. Read by the carry to derive this
    /// prop's minimum hold distance and its clearance from the holder's capsule, which is why it
    /// is a fact about the BODY and not a constant anywhere else: a can and a crate cannot share
    /// one, and the whole defect FEEL-1 exists to fix was a carry that behaved as if they
    /// could.</summary>
    public float BoundingRadiusM { get; private set; }

    public override void _Ready()
    {
        // An explicitly authored Profile wins outright; otherwise the material picks one, and
        // PropMaterial.Wood picks the same shared default this line loaded before SFX-1.
        Profile ??= GD.Load<PresentationProfile>(ProfilePathFor(ResolveMaterial()));

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
        // ...and the whole bulk, for the carry. Same source as the half-height above and for the
        // same reason: the mesh is what the eye judges a hold against, and the two collider paths
        // do not share a node name. A prop with no mesh reads 0, which the carry treats as a
        // point-sized object rather than as an error.
        BoundingRadiusM = shapeMesh.Mesh is { } bulkMesh
            ? Feel.CarryHold.BoundingRadiusM(bulkMesh.GetAabb().Size)
            : 0f;

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
            // SFX-1's three product shapes. The MASS is set here too, unlike Crate/Ball which
            // leave the engine default: a can that weighs as much as a crate lags in the hand
            // like a crate (CarrySpring reads HeftKg straight off Mass), and the whole point of
            // three products is that they do not feel the same. An AUTHORED prefab sets mass in
            // its own .tscn, where it is a native property and therefore actually applies.
            case Shape.Can:
                mesh = new CylinderMesh
                {
                    TopRadius = CanRadiusM, BottomRadius = CanRadiusM, Height = CanHeightM,
                };
                collider = new CylinderShape3D { Radius = CanRadiusM, Height = CanHeightM };
                Mass = CanMassKg;
                // PHYS-1 (2026-09-20): Can.tscn's own lines, to the digit. See the class-level
                // note on PhysicsMaterialPathFor for why a code-built prop has to carry them.
                LinearDamp = 0.55f;   // re-measured; see Can.tscn
                AngularDamp = 0.25f;
                break;
            case Shape.Box:
                mesh = new BoxMesh { Size = BoxSizeM };
                collider = new BoxShape3D { Size = BoxSizeM };
                Mass = BoxMassKg;
                // PHYS-1 (2026-09-20): CerealBox.tscn's own lines, to the digit -- the centre of
                // mass included, because a seeded box that was not top-heavy would not domino and
                // the suite that measures dominoing seeds its own row.
                LinearDamp = 0.4f;
                AngularDamp = 0.5f;   // re-measured; see CerealBox.tscn
                CenterOfMassMode = CenterOfMassModeEnum.Custom;
                CenterOfMass = new Vector3(0f, 0.07f, 0f);
                break;
            case Shape.Produce:
                mesh = new SphereMesh { Radius = ProduceRadiusM, Height = ProduceRadiusM * 2f };
                collider = new SphereShape3D { Radius = ProduceRadiusM };
                Mass = ProduceMassKg;
                // Damped, matching Produce.tscn, and for the reason that prefab records: an
                // undamped sphere on a flat floor never stops. Measured — a code-built produce
                // dropped 0.27 m rolled 1.9 m and the server logged "rest not good prop=3
                // OutsideRoomBounds". The two birth paths have to agree or the seeded fixture
                // stops being the same object as the shelved product, which is the whole reason
                // the dimensions are shared constants.
                LinearDamp = 2.0f;
                AngularDamp = 3.0f;
                break;
            default:
                mesh = new BoxMesh { Size = new Vector3(0.44f, 0.44f, 0.44f) };
                collider = new BoxShape3D { Size = new Vector3(0.44f, 0.44f, 0.44f) };
                // PHYS-1 (2026-09-20): Crate.tscn's own lines. Mass is left at the engine default
                // 1.0 here exactly as it is there.
                LinearDamp = 0.3f;
                AngularDamp = 1.0f;
                break;
        }
        // PHYS-1 (2026-09-20): and the CONTACT half of the material table, resolved from the same
        // PropMaterial that picks this prop's sound. An authored prefab sets its own
        // physics_material_override in its .tscn and never reaches BuildShape at all.
        PhysicsMaterialOverride ??=
            GD.Load<PhysicsMaterial>(PhysicsMaterialPathFor(ResolveMaterial()));
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
        // LAYER 0, MASK 1 (FEEL-1, 2026-09-20). This used to be mask 0 as well, and that one line
        // was Talon's whole complaint: "there is still an extreme issue with how items are picked
        // up: if the player moves, the item clips into their body, which is stupid and feels bad
        // ... that's why there's physics and collision on the objects." A held prop collided with
        // NOTHING -- not the holder, not a shelf.
        //
        // The two halves are different questions and they get different answers:
        //   LAYER 0 -- nothing collides WITH the held prop. The holder walks freely, another
        //              player is not shoved by what you are carrying, and the aim ray still passes
        //              through it (AimedSurfaceWithinPlaceReach's doc depends on exactly this).
        //   MASK 1  -- the held prop collides with the WORLD. It is frozen kinematic, so nothing
        //              is simulated; the mask is what lets NetworkedProp sweep it with
        //              PhysicsServer3D.BodyTestMotion and stop it against a shelf instead of
        //              posting it through one.
        // The holder's own body is handled separately, by projection rather than by collision
        // (CarryHold.PushOutOfSegment) -- colliding with the holder would let a held crate push
        // the player around, which is a worse bug than the one being fixed.
        CollisionLayer = 0;
        CollisionMask = 1;
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

    /// <remarks><b>Deliberately does NOT fire <c>ActorEvent.Thrown</c></b>, and SFX-1 tried it
    /// here first. This entry point is reached only from <c>NetworkedProp.BeginLooseServer</c>,
    /// which is server-only — so a throw announced here is a throw only the host can hear, and
    /// the remote player watches a can leave a hand in silence. The fire lives in
    /// <c>NetworkedProp.BeginLoose</c> instead, which is the every-peer half of the same
    /// transition.</remarks>
    public virtual void OnThrown(Vector3 impulse) => Release(impulse);

    /// <summary><b>Released with the spin NAMED instead of drawn</b> (PHYS-2, 2026-09-20).
    /// <see cref="Release(Vector3)"/> above draws a random tumble of +/-2 rad/s on every axis,
    /// which is right for a discarded object and is a fact about the verb rather than about the
    /// physics. A test fixture that asks for a known shove and is handed a random spin with it is
    /// not a known shove: +/-2 rad/s is half of what it takes to put a cereal box over, so the
    /// tumble alone decides whether a shoved box topples or slides. This entry point is how
    /// <c>--phys-shove</c> gets a repeatable one; every shipped release still goes through the
    /// overload above and still tumbles.</summary>
    public virtual void OnThrown(Vector3 impulse, Vector3 angular) => Release(impulse, angular);

    /// <summary><b>Set down, not dropped</b> (CARRY-1's place verb): rejoin physics with no
    /// velocity and NO SPIN. The random tumble <see cref="Release(Vector3)"/> applies is what
    /// makes a discarded prop look discarded; applying it to a placement would spin away the
    /// orientation the player just lined the object up in, which is the one thing the verb
    /// exists to preserve.</summary>
    /// <remarks><b>Deliberately does NOT fire <c>ActorEvent.Placed</c> any more</b> (SFX-2), for
    /// exactly the reason <see cref="OnThrown"/> above does not fire <c>Thrown</c>: this entry
    /// point is reached only from <c>NetworkedProp.PlaceLooseServer</c>, which is server-only, so
    /// a set-down announced here is a tick only the host can hear. SFX-1 wrote that limitation
    /// down and left it — <i>"the deliberate set-down tick stays on PlaceLooseServer, where the
    /// verb IS known, and is therefore host-only today"</i>. SFX-2 spends the byte that carries
    /// the verb (<c>PropRelease.Placed</c>), so the fire now lives in
    /// <c>NetworkedProp.BeginLoose</c>, the every-peer half of the same transition, and firing
    /// here as well would give the host two ticks for one placement.</remarks>
    public virtual void OnPlaced() => Release(Vector3.Zero, Vector3.Zero);

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

    /// <summary><b>Server-only fixture release</b> (SFX-1): rejoin physics exactly where the body
    /// already is, with no toss arc and NO TUMBLE, and announce nothing.
    ///
    /// <para>Both omissions are load-bearing. <see cref="Release(Vector3)"/> applies a random
    /// spin of up to 2 rad/s on every axis, which is what makes a DISCARDED object look
    /// discarded and is exactly wrong for a fixture: measured, a can released above another can
    /// tumbled out from under itself and landed 9 cm to the side, so the collision the suite
    /// existed to observe never happened. And the release is silent here because the
    /// <c>ApplyPropState</c> broadcast that accompanies it already fires the release event on
    /// every peer through <c>NetworkedProp.BeginLoose</c> — a second fire would double every
    /// release.</para>
    ///
    /// <para><b>SFX-2 renamed this from <c>ReleaseAtRestServer</c> and gave it a second
    /// caller</b>, because the name had stopped being true. <c>NetworkedProp.Unbind</c> — the
    /// EVERY-PEER Resting latch — used to reach physics through <see cref="OnDropped"/>, whose
    /// toss arc it discarded on the very next line and whose <c>ActorEvent.Dropped</c> fire it
    /// did not: so every settle, every disconnect release and every round-reset rehome announced
    /// a DROP on every peer. That was inaudible only because no profile in the repo maps
    /// <c>Dropped</c> to a sound, which is luck rather than design, and the packet's rule is
    /// explicit — <b>a reset is <c>None</c>, not <c>Dropped</c></b>. Unbind calls this instead;
    /// the physical outcome is identical (Unbind re-freezes and re-pins immediately afterwards),
    /// and the announcement is now the release byte's job alone.</para></summary>
    public void RejoinPhysicsSilently() => Release(Vector3.Zero, Vector3.Zero);

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

    /// <summary><b>PROBE-1's at-rest gate.</b> True while every per-frame visual quantity on this
    /// prop has arrived and nothing can move it until an event does — see
    /// <see cref="CarryIdle.IsSettled"/> for what that means and why it is re-read rather than
    /// latched. Public so a probe can count how many props in a room are actually costing
    /// nothing, which is the claim this fix makes and therefore the claim that has to be
    /// measurable.</summary>
    public bool IsVisuallySettled => Props.PropCostSwitches.CarryableIdleGate
        && CarryIdle.IsSettled(_homeSet, Freeze, IsHeld, Highlighted,
            _outlineMesh?.Visible ?? false, _thunkCooldown,
            _visualScale, _visualScaleVel,
            _material.EmissionEnergyMultiplier, _baseGlow);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // PROBE-1 (2026-09-20): NOTHING TO DO FOR A PROP NOTHING HAS TOUCHED.
        //
        // Everything below this line either chases something (a holder's anchor, a kill-plane, a
        // pop spring, an emission lerp) or writes a value that is already correct. When all four
        // of those have arrived and the body is frozen, the remaining work is four writes through
        // the engine boundary — three of which reach the RenderingServer — repeated sixty times a
        // second, per prop, on every peer, forever.
        //
        // MEASURED, not reasoned: see docs/agents/handoffs/2026-09-20-PROBE-1.md. The gate is
        // what makes a room of two thousand items a question about draw calls rather than about
        // interop.
        //
        // The early-out is BEFORE the ApproachSpeedMps read deliberately. That field is the speed
        // a body carried INTO this step, and a frozen body's LinearVelocity is permanently zero —
        // so re-reading it here for a resting prop is one interop call to learn a number that
        // cannot have changed. It is zeroed on the edge into settled (below) so no stale value
        // survives, which is the same guard NetworkedProp._PhysicsProcess already keeps on
        // ObservedSpeedMps for the same reason.
        if (IsVisuallySettled)
            return;

        // Before the physics server integrates this step — see ApproachSpeedMps for why the
        // velocity read inside body_entered is the wrong number for a landing.
        ApproachSpeedMps = LinearVelocity.Length();

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

        // PROBE-1: THE EDGE INTO SETTLED, taken once so the gate above never freezes a residual.
        //
        // The pop spring and the emission lerp are both asymptotic — they converge on their
        // target and never arrive at it — so the gate's epsilons are what decide when a prop is
        // "done". Landing exactly on the target on the last tick that runs means the resting
        // state a player sees is the authored one, not the authored one minus an epsilon, and it
        // means a prop that goes idle and wakes again resumes from a clean value rather than
        // accumulating a drift per episode. Three writes, once per rest episode, against sixty a
        // second forever.
        if (IsVisuallySettled)
        {
            _visualScale = Vector3.One;
            _visualScaleVel = Vector3.Zero;
            _visual.Scale = Vector3.One;
            _material.EmissionEnergyMultiplier = _baseGlow;
            ApproachSpeedMps = 0f;
        }
    }

    /// <summary><b>How fast this body is observed to be moving on a peer that is not simulating
    /// it</b> — written every physics tick by <c>NetworkedProp</c> while it follows the server's
    /// loose-transform stream, and zero otherwise.
    ///
    /// <para><b>Without this, a prop impact was audible to the host and to nobody else</b>, and
    /// SFX-1 is the packet that found it. A Loose prop on a non-authority peer is a FROZEN
    /// KINEMATIC body lerped toward a streamed transform, so its <c>LinearVelocity</c> is
    /// permanently 0 — which meant <see cref="OnBodyEntered"/>'s speed gate rejected every
    /// contact before this existed. The bug predates this packet (<c>Thunk</c> had it too); what
    /// is new is a packet whose entire point is that a can hitting a floor is a thing the other
    /// player hears, and a seeker who cannot hear the hider knock something over in the next
    /// aisle is the game not working.</para>
    ///
    /// <para><b>Observed rather than replicated, deliberately.</b> The alternative is putting an
    /// impact event on the wire, which is a protocol change for a cosmetic fact that every peer
    /// can already derive: the loose stream IS the prop's motion, and a distance over a delta is
    /// its speed. It is slightly noisier than a real velocity — the lerp smooths the arrival, so
    /// this under-reads a hard impact rather than over-reading it, which is the right direction
    /// for a threshold.</para></summary>
    public float ObservedSpeedMps { get; set; }

    /// <summary><b>This body's speed as it entered the current physics step</b>, before the
    /// solver touched it — sampled at the top of <see cref="_PhysicsProcess"/>, which Godot calls
    /// before the physics server integrates.
    ///
    /// <para><b>Reading LinearVelocity inside body_entered does not give you the impact speed,
    /// and the failure is one-sided, which is what makes it so easy to miss.</b> Measured here:
    /// a can and a cereal box thrown into a WALL reported 4.0 and 6.3 m/s and sounded correctly,
    /// while four props dropped 0.6-1.1 m onto the FLOOR reported under the 2 m/s audible floor
    /// and were silent — every single time, run after run. A glancing contact leaves residual
    /// velocity for the signal handler to read; a head-on landing has already had its normal
    /// impulse applied by the time the signal is emitted, so the handler reads a body that has
    /// stopped. Half the impacts in a game working is exactly the kind of bug that ships.</para></summary>
    public float ApproachSpeedMps { get; private set; }

    /// <summary><b>The speed that actually matters at a contact</b> (SFX-1): this body's velocity
    /// relative to what it hit, when what it hit is another rigid body, and its own velocity
    /// otherwise (static world geometry has no velocity to subtract).
    ///
    /// <para>Before SFX-1 the gate read this body's own <c>LinearVelocity</c> unconditionally,
    /// which is correct for the only case that existed — a prop thrown at a wall — and silently
    /// wrong for two cases this packet cares about. A can sitting on a shelf that a thrown box
    /// slams into is stationary, so its own speed is zero and it would have made no sound at all
    /// while being knocked across the room; and on any peer that is not simulating the prop, the
    /// velocity is zero forever (see <see cref="ObservedSpeedMps"/>).</para></summary>
    private float RelativeContactSpeed(Node body)
    {
        float ownSpeed = SpeedOf(this);
        if (body is not RigidBody3D other)
            return ownSpeed;
        float otherSpeed = other is Carryable otherProp ? SpeedOf(otherProp) : other.LinearVelocity.Length();
        // Both bodies genuinely simulating: the vector difference is the real relative speed and
        // is what a head-on collision needs (two props closing at 3 m/s each meet at 6, not 0).
        // Where either side is only OBSERVED, there is no direction to subtract — the stream
        // gives a distance per tick, not a velocity — so fall back to the faster of the two,
        // which is the quantity that decides whether a contact was hard.
        // Where either side is a body this peer is not simulating, or where the solver has
        // already eaten the velocity, there is no usable direction to subtract — every source
        // below is a SPEED, not a velocity — so the relative figure is the faster of the two,
        // which is the quantity that decides whether a contact was hard.
        return Mathf.Max(ownSpeed, otherSpeed);
    }

    /// <summary>The best available speed for a body at the moment of contact: whichever of the
    /// three sources is largest. Live velocity is right for a glancing hit; the pre-step approach
    /// speed is right for a landing the solver has already stopped; the observed speed is the
    /// only one a non-simulating peer has at all.</summary>
    private static float SpeedOf(Carryable body) =>
        Mathf.Max(body.LinearVelocity.Length(), Mathf.Max(body.ApproachSpeedMps, body.ObservedSpeedMps));

    private void OnBodyEntered(Node body)
    {
        // PHYS-1 (2026-09-20), ruling P1: THE WAKE IS REPORTED FIRST, ABOVE BOTH SOUND GATES.
        //
        // The two early-outs below are correct for AUDIO and wrong for physics, and both cases
        // are exactly the ones Talon named:
        //   IsHeld            -- a held crate driven into a row of boxes is the domino case, and
        //                        a held body's contacts are precisely what must move them. (A
        //                        SPRING-held prop is frozen kinematic and Godot asks it nothing,
        //                        so the holder's own sweep handles that one; this arm is what
        //                        covers a prop held by the anchor chase and any future holder.)
        //   _thunkCooldown    -- the second can of a collapsing stack is inside the first can's
        //                        0.4 s cooldown and must still be knocked over.
        // And ThunkSpeedThreshold (2 m/s) is an AUDIBILITY floor: a can nudged into a pyramid at
        // 1 m/s is silent and must still bring it down. The wake has its own, far lower gate
        // (PropPhysics.WakeSpeedThresholdMps).
        //
        // Only prop-on-prop: a wall has nothing to wake, and the reporter itself is only ever set
        // on a networked prop's body by NetworkedProp, which drops everything off the server.
        if (BumpReporter is { } bump && body is Carryable hitProp)
            bump(hitProp, RelativeContactSpeed(body));

        if (IsHeld || _thunkCooldown > 0)
            return;
        float relativeSpeed = RelativeContactSpeed(body);
        if (relativeSpeed < ThunkSpeedThreshold)
            return;
        // Prop on prop: Godot tells BOTH bodies about the one contact, so exactly one of them
        // plays it. See WinsPropOnPropContact for the rule and why it is instance id. The loser
        // still takes the cooldown: it participated in a contact, and without this a shelf going
        // over would have every prop firing on the NEXT contact of the same pile-up 16 ms later.
        if (body is Carryable otherProp)
        {
            bool claimed = ClaimPropOnPropContact(PairClaims, GetInstanceId(),
                otherProp.GetInstanceId(), Time.GetTicksMsec(), PairClaimWindowMsec);
            // The SUPPRESSION is logged, not just the sound. Without this line the
            // once-per-contact rule can only be checked by inference — "exactly one Impact
            // appeared near this position at this moment" — which cannot tell a rule that
            // suppressed the duplicate from a contact that only ever reported one body, and
            // those are very different states of the world. With it, the suite asserts the
            // thing directly: a suppression happened, and its partner played.
            if (!claimed)
            {
                if (ActorFx.LogSfx)
                {
                    GD.Print($"[sfx] pair-suppressed self={GetParent()?.Name} "
                        + $"other={otherProp.GetParent()?.Name} speed={relativeSpeed:F2} "
                        + $"t={Time.GetTicksMsec()}");
                }
                _thunkCooldown = ThunkCooldownSec;
                return;
            }
        }
        _thunkCooldown = ThunkCooldownSec;
        PunchScale(new Vector3(1.2f, 0.78f, 1.2f));
        float intensity = ImpactIntensity(relativeSpeed);
        // SFX-2: ON A NETWORKED PROP THE SERVER SAYS WHAT HAPPENED, and every peer — this one
        // included — plays it from the announcement. See ImpactReporter for why the local fire
        // must not also run here: two paths would give the host two sounds per hit, and giving
        // the other player none was SFX-1's one unclosed defect. The reporter itself decides
        // that a client reports nothing (only the server sees every contact).
        if (ImpactReporter is { } report)
        {
            report(intensity);
            return;
        }
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Impact, GlobalPosition, intensity);
    }

    /// <summary>Play one impact on THIS peer's copy of the prop, at <paramref name="intensity"/>
    /// — the receiving half of the server's announcement (SFX-2). The squash is deliberately NOT
    /// re-punched here: it is applied at the contact by whichever peer actually had one, and a
    /// remote peer's frozen kinematic body has no contact to squash from. Stated rather than
    /// hidden — a networked prop's impact SQUASH is still host-only, and it is a follow-up, not
    /// part of this packet's "what you hear is what happened".</summary>
    internal void PlayWireImpact(float intensity) =>
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Impact, GlobalPosition, intensity,
            via: ActorFx.ViaWire);

    protected void PunchScale(Vector3 to)
    {
        _visualScale = to;
        _visualScaleVel = Vector3.Zero;
    }

    /// <summary>Test hook: is the outline shell currently flagged visible.</summary>
    internal bool OutlineVisible => _outlineMesh?.Visible ?? false;
}
