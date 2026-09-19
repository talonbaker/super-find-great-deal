using System.Collections.Generic;
using System.Linq;
using Godot;
using MpFoundation;
using MpFoundation.Game;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using Sail.Game.Bubble;
using Sail.Game.Run;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// <b>The Bubble Test's compliance test — and the reason "no procedural generation" is a fact
/// about this level rather than a promise in a packet.</b>
///
/// <para><c>godot --headless --path . -- --bubbletest-selftest</c>; exits 0/1;
/// <c>tests/Run-BubbleTestWorldTest.ps1</c> gates on it.</para>
///
/// <para><b>The load-bearing check is <see cref="CheckBake"/>.</b> Talon's constraint — <i>"Every
/// part of this level ... must be physically authored and present in the Godot scene file, so it
/// can be opened and flown around in the Godot editor directly"</i> — is not something a reviewer
/// can verify by reading a diff, because a section that builds a rock in <c>_Ready</c> looks
/// identical in the editor's inspector to one that does not until you open the file. So it is
/// measured instead: each section's <see cref="PackedScene"/> is walked as <b>packed state</b> —
/// <see cref="SceneState"/>, no instantiation, no <c>_Ready</c>, nothing runs — and its
/// <c>MeshInstance3D</c>/<c>CollisionShape3D</c>/<c>StaticBody3D</c> counted; the same scene is
/// then instantiated into the live tree and counted again. <b>Equal counts mean nothing was
/// constructed at runtime.</b> A packet that bakes with
/// <see cref="MpFoundation.Tools.AuthoredSceneDumper"/> (program D2) passes; a packet that keeps
/// its builder script does not.</para>
///
/// <para><b>The second check is an absence check, and it carries its own control.</b>
/// <see cref="AuditColliders"/> asserts every authored <c>MeshInstance3D</c> has collision — this
/// repo shipped an entire playtest with no colliders anywhere (PLAYTEST-1), which is the failure
/// it exists for. An absence check that is silently looking in the wrong place passes everything,
/// so <see cref="CheckColliderControl"/> builds a throwaway scene with a mesh and no collider and
/// <b>requires the audit to fail on it</b>. If the control ever stops failing, the audit is
/// broken and every green run before it was worthless.</para>
///
/// <para><b>The third check is <see cref="CheckPropHeights"/>, and it is here because its absence
/// cost days.</b> Every prop <see cref="BubbleTestWorld"/> places from code is asserted to stand on
/// the surface a downward ray finds under it — never on a literal height, which would be a second
/// copy of the world. The reset lever floated 0.9 m from BT-8 to LEVER-1 through a green suite
/// every time, on the received belief that a headless run cannot see a prop's Y. It can; nobody had
/// written the assertion.</para>
///
/// <para><b>What it deliberately does not check.</b> Anything a section's <i>author</i> owns:
/// bubble counts (BT-11), material identity (BT-9), the beam's width, the stone gaps. This file
/// pins the seam — anchors, footprints, spawns, extent, the sky, and the two structural laws
/// above — because those are what five parallel packets would otherwise each be free to
/// reinterpret.</para>
/// </summary>
public sealed partial class BubbleTestSelfTest : Node3D
{
    /// <summary>A mesh in this group is declared collision-free on purpose (a decal, a banner, a
    /// thing behind glass). The audit skips it, so the group is the ONE way to opt out and it is
    /// visible in the scene file's node header where a reviewer will see it.</summary>
    public const string NoColliderGroup = "no_collider";

    private const string MeshType = "MeshInstance3D";
    private const string ShapeType = "CollisionShape3D";
    private const string BodyType = "StaticBody3D";

    /// <summary>How close an authored anchor or spawn marker must sit to the constant it claims to
    /// be. 1 cm: tight enough that a typo cannot hide, loose enough that a float round-trip through
    /// the <c>.tscn</c> text format cannot fail it.</summary>
    private const float PosTolerance = 0.01f;

    /// <summary>The death beat, shortened for the test only. The property under test is
    /// <i>where</i> a player comes back and <i>whether</i> the boundary fires — not how long the
    /// comic beat runs — and the shipped 2 s would put four seconds of dead wait into a suite that
    /// otherwise finishes in one.</summary>
    private const float TestBeatSeconds = 0.2f;

    /// <summary>The drowning clock, shortened for the test only — same argument as
    /// <see cref="TestBeatSeconds"/>. The shipped 3.0 s is asserted separately in
    /// <see cref="CheckRespawnConfig"/> and proved to the boundary in <c>RespawnDrowningTests</c>;
    /// what this probe proves is that the clock runs at all and names the right cause.</summary>
    private const float TestDrownSeconds = 0.4f;

    /// <summary>GUARD-1. How far a prop's base may sit off the surface underneath it, in either
    /// direction.
    ///
    /// <para><b>Where 2 cm comes from.</b> Both sides of this subtraction are authored exact
    /// numbers — a prop's base is its scene file's origin and the ground is a box collider's top
    /// face — so the only error that is not a defect is the float round-trip through the
    /// <c>.tscn</c> text format, which is sub-millimetre; that is the same argument
    /// <see cref="PosTolerance"/> makes for 1 cm. It is doubled here for one reason: the ray
    /// reports a point on a <i>collision</i> surface, and a section is free to inset a collider
    /// from its render mesh by a hair without that being a bug. 2 cm is still two orders of
    /// magnitude under the 0.9 m float this check exists to catch, and under anything a player
    /// would read as a prop hovering.</para>
    ///
    /// <para>It is deliberately NOT a one-sided "must not float" bound. A prop sunk into the
    /// ground is the same authoring mistake with the sign flipped, and it hides better.</para>
    /// </summary>
    private const float GroundTolerance = 0.02f;

    /// <summary>GUARD-1, LEVEL-4. Where the golden cubes live in the world scene, and the prefix
    /// every one of them carries. Named here rather than at the two call sites for the reason the
    /// TV node names are: two spellings of one node is the "second copy" trap this file has
    /// already paid for.</summary>
    private const string GoldenCubeParent = "Props";

    /// <inheritdoc cref="GoldenCubeParent"/>
    private const string GoldenCubePrefix = "GoldCube_";

    /// <summary>GUARD-1, LEVEL-4. Looser than <see cref="GroundTolerance"/>, and the reason is
    /// physical rather than a fudge: a cube is a live <c>RigidBody3D</c> in this test (there is no
    /// <c>PropManager</c> in a self-test world to freeze it at Resting), so it has had
    /// <see cref="PhysicsSettleSeconds"/> of gravity by the time the ray is cast. 2 cm is far
    /// tighter than the 0.22 m a centre-vs-base mistake would show, and far tighter than the 0.9 m
    /// float this whole guard exists for.</summary>
    private const float GoldenCubeTolerance = 0.02f;

    /// <summary>GUARD-1. The ground ray starts this far above a prop's base and ends this far
    /// below it. Up: clear of the prop's own footing without leaving the room a sunken prop sits
    /// in (the TV room is a sealed box under the hub's floor slab, so a ray started "high above
    /// the level" would measure the hub's floor for a prop twenty metres beneath it). Down: far
    /// enough that the 0.9 m historical float is measured and reported as a number rather than
    /// as "no ground found", and short enough that a prop with genuinely nothing under it fails
    /// loudly instead of matching some distant surface.</summary>
    private const float GroundProbeUpM = 1.0f;

    /// <inheritdoc cref="GroundProbeUpM"/>
    private const float GroundProbeDownM = 3.0f;

    /// <summary>GUARD-1. How long the guard waits after the world enters the tree before it casts
    /// its rays. Six physics ticks at the default 60 Hz — the space needs one, and the margin
    /// costs a tenth of a second in a suite that already waits over a second for the boundary
    /// probe.</summary>
    private const double PhysicsSettleSeconds = 0.1;

    private readonly List<string> _failures = new();

    private BubbleTestWorld _world = null!;
    private RespawnService _respawn = null!;
    private SandboxAvatar _stray = null!;

    /// <summary>BUBBLE-2's enclosure control — a sealed shell staged far off the map in
    /// <see cref="Run"/> and freed the moment <see cref="CheckBubbleEmbedding"/> has fired it.
    /// <b>It is built there rather than in the check because a body added this frame is not in
    /// the physics space yet</b>: staged inside the check it reported itself free, which is
    /// exactly the "the control silently stopped controlling" failure controls exist to prevent.
    /// It rides the same <see cref="PhysicsSettleSeconds"/> wait everything else here does.
    /// </summary>
    private Node3D? _embedShell;
    private SandboxAvatar _drowner = null!;

    /// <summary>EGG-2. Peer 80: a body in the blue tower's moat, and peer 81: a body inside the
    /// secret bubble. Both ride the SAME 10 Hz boundary scan and the SAME physics frame the two
    /// probes above use, because a second staged world would prove a second world.</summary>
    private SandboxAvatar _moatDrowner = null!;

    /// <inheritdoc cref="_moatDrowner"/>
    private SandboxAvatar _secretFinder = null!;

    /// <summary>The counter's bubble census, read BEFORE the secret bubble is touched. Acceptance
    /// criterion 5 is a before/after on this number, not a claim about it.</summary>
    private int _bubblesBeforeSecret = -1;
    private int _countBeforeSecret = -1;
    private readonly List<(int Peer, RespawnCause Cause)> _deaths = new();
    private SandboxAvatar _predictedOwner = null!;
    private VisualRiseProbe _strayRise = null!;
    private VisualRiseProbe _drownerRise = null!;
    private RenderOffsetCompositionProbe _ownerCompose = null!;
    private bool _ownerRetiring;

    /// <summary>MRF-B / F8. The render-space correction offset seeded onto the predicted-owner
    /// body, world space. <b>Y only, and deliberately huge relative to the beat.</b>
    ///
    /// <para>Y only because <c>SandboxAvatar.TestInjectVisualError</c> converts through
    /// <c>GlobalTransform.Basis.Inverse()</c>, and a yaw-only basis leaves a vertical vector
    /// alone — so the seed means the same thing in local space whatever the body is facing, and
    /// no assertion below has to model the avatar's rotation.</para>
    ///
    /// <para>3 m because the WHOLE vertical budget of the death beat is
    /// <see cref="Sail.Game.Run.DeathBeatPose.LieFlatLiftM"/> = 0.18 m up and
    /// <see cref="Sail.Game.Run.DeathBeatPose.DrownSinkM"/> = 0.90 m down. At 3 m the three
    /// possible outcomes are unmistakable from the sampled Y alone: <b>composed</b> lands in
    /// [3.00, 3.18]; <b>the beat clobbering the offset</b> lands in [0.00, 0.18]; <b>the offset
    /// clobbering the beat</b> pins to exactly 3.00 with no rotation. Pre-W7-4 the code did one of
    /// the last two depending on which callback ran last, which is the defect this probe is
    /// supposed to be able to see and — until MRF-B — could not.</para></summary>
    private static readonly Vector3 RenderOffsetSeed = new(0f, 3f, 0f);

    /// <summary>How long peer 79 is kept alive after its first death. Long enough to cover the
    /// whole beat and settle; short enough that the body is gone before
    /// <see cref="FinishBoundaryProbe"/> checks where the OTHER two ended up — see the retirement
    /// note in <see cref="StartBoundaryProbe"/> for why that matters.</summary>
    private const double ComposeWindowSeconds = TestBeatSeconds + 0.3;

    /// <summary>
    /// <b>W7-4's end-to-end instrument: how far the rendered body actually goes up.</b>
    ///
    /// <para>Talon, 2026-08-30 note 6: <i>"When the player 'drowns', the body floats and spirals
    /// upward, to the surface, into the sky, it looks like a tornado took them up."</i> The curve
    /// that produced it is bounded exhaustively in <c>DeathBeatPoseTests</c>; this measures the
    /// same quantity on the far side of most of the layers between the curve and the screen — the
    /// beat node, the replicated cause, and the visual's three-contributor root transform.</para>
    ///
    /// <para><b>What this class does NOT cover, and what does</b> (MRF-B / F8, 2026-08-30). Its
    /// two bodies are bare <c>new SandboxAvatar</c>s, so <c>_role</c> stays
    /// <c>NetRole.Offline</c>; <c>SandboxAvatar</c>'s per-tick <c>SetRenderOffset</c> write lives
    /// in <c>OwnerTick</c>, which runs only for <c>NetRole.PredictedOwner</c>. Their
    /// <c>_renderOffset</c> is therefore permanently zero and this probe measures the death pose
    /// alone — it could never have seen the render-offset contention W7-4 actually fixed, though
    /// this comment used to claim it did. <see cref="RenderOffsetCompositionProbe"/> is the half
    /// that covers it, on a third body that IS a predicted owner.</para>
    ///
    /// <para><b>It samples the LOCAL offset, deliberately.</b> The body itself legitimately rises
    /// while it drowns — the water contract's swim-settle pulls a sunk body back to
    /// <c>SwimLineY</c> at up to 4 m/s, which is correct and is not this packet's subject. The
    /// defect was the presentation layer adding an excursion on top of it, and the local offset
    /// is exactly that term with the water's own motion divided out.</para></summary>
    private sealed partial class VisualRiseProbe : Node
    {
        public SandboxAvatar? Target;

        /// <summary>Greatest upward local offset seen on the visual, metres. The beat W7-4
        /// replaced peaked at 4.200 m here.</summary>
        public float PeakRiseM { get; private set; }

        /// <summary>Greatest total rotation seen on the visual, in whole turns. The beat W7-4
        /// replaced spent 4.500 by construction and this probe sampled it at 4.487 — the gap is
        /// the frame it never landed on, not a disagreement.</summary>
        public float PeakTurns { get; private set; }

        /// <summary>Samples taken. A probe that never ran reads identically to a body that never
        /// moved, and the difference is the whole point of the measurement.</summary>
        public int Samples { get; private set; }

        public override void _Process(double delta)
        {
            if (Target is null || !IsInstanceValid(Target)) return;
            Node3D? visual = Target.GetNodeOrNull<Node3D>("Visual");
            if (visual is null) return;

            Samples++;
            PeakRiseM = Mathf.Max(PeakRiseM, visual.Position.Y);
            Vector3 r = visual.Rotation;
            PeakTurns = Mathf.Max(PeakTurns,
                (Mathf.Abs(r.X) + Mathf.Abs(r.Y) + Mathf.Abs(r.Z)) / Mathf.Tau);
        }
    }

    /// <summary>
    /// <b>MRF-B / F8: the configuration W7-4 actually fixed, exercised end to end for the first
    /// time.</b>
    ///
    /// <para>W7-4's defect was two writers on one transform: <c>SandboxAvatar.OwnerTick</c> wrote
    /// the render-correction offset onto the visual on every PHYSICS tick while
    /// <c>RespawnService.DeathBeat</c> wrote the death pose onto the same node on every RENDER
    /// frame, so the body Talon watched drown was posed by whichever callback happened to run
    /// last. The fix was <c>AvatarVisual.ApplyRootPose</c>: three contributors DECLARE their terms
    /// and one writer composes them additively.</para>
    ///
    /// <para><b>Both halves have to be non-zero at once or nothing is being tested</b>, and that
    /// is exactly what <see cref="VisualRiseProbe"/>'s bodies cannot do — an <c>Offline</c> role
    /// never runs <c>OwnerTick</c>, so its render offset is a permanent zero and the composition
    /// degenerates to the death pose alone. This probe's target is configured as a real
    /// <c>NetRole.PredictedOwner</c> and carries a seeded correction offset through the beat, so
    /// the sampled Y is genuinely the SUM of two live terms.</para>
    ///
    /// <para><b>The seed is re-planted every frame, and that is the point rather than a
    /// convenience.</b> <c>OwnerTick</c> drains the offset at <c>CorrectionSmoothRate</c> = 12/s,
    /// so a one-shot injection is down to a fraction of itself within half a second and the beat
    /// would finish against an offset of zero — i.e. back to the untested configuration.
    /// Re-planting models a predicted owner under SUSTAINED correction, which is the worst case
    /// and the one the two-writer bug lived in; it also means the drain and the beat are both
    /// writing the node every frame, which is the collision itself.</para></summary>
    private sealed partial class RenderOffsetCompositionProbe : Node
    {
        public SandboxAvatar? Target;

        /// <summary>The world-space correction offset re-planted every frame.</summary>
        public Vector3 Seed;

        /// <summary>Greatest <c>VisualErrorM</c> observed — the offset was live at all.</summary>
        public float PeakVisualErrorM { get; private set; }

        /// <summary>Smallest <c>VisualErrorM</c> observed <b>immediately before</b> a re-plant,
        /// first frame excluded. <b>This is the probe's real positive control, and it is the one
        /// assertion here that master's configuration fails.</b>
        ///
        /// <para>An <c>Offline</c> body never runs <c>OwnerTick</c>, so nothing ever drains the
        /// injected offset and this reads back exactly <see cref="Seed"/> every frame, forever —
        /// which is precisely why seeding <see cref="VisualRiseProbe"/>'s two bodies would still
        /// have proved nothing about the contention. A <c>PredictedOwner</c> drains it by
        /// <c>exp(-CorrectionSmoothRate · dt)</c> = 0.819 per 60 Hz tick, so the value comes back
        /// visibly smaller. Observing the DRAIN is what proves <c>OwnerTick</c> is running, and
        /// therefore that its per-tick <c>SetRenderOffset</c> write — the second writer in the
        /// W7-4 defect — is genuinely competing with the beat for this transform. Asserting the
        /// role enum would prove the configuration; this proves the code path.</para></summary>
        public float MinErrorBeforeReplantM { get; private set; } = float.MaxValue;

        /// <summary>Lowest and highest local Y seen on the visual over the whole window. Both
        /// bounds matter and they fail on opposite defects: a low <see cref="MinY"/> says the beat
        /// discarded the offset, and a <see cref="MaxY"/> pinned at exactly the seed with no
        /// rotation says the offset discarded the beat.</summary>
        public float MinY { get; private set; } = float.MaxValue;

        /// <inheritdoc cref="MinY"/>
        public float MaxY { get; private set; } = float.MinValue;

        /// <summary>Greatest total rotation seen, in whole turns — the second control. Rotation
        /// has only one contributor here, so a zero means the beat never played on this body at
        /// all and the height bounds prove nothing.</summary>
        public float PeakTurns { get; private set; }

        /// <inheritdoc cref="VisualRiseProbe.Samples"/>
        public int Samples { get; private set; }

        public override void _Process(double delta)
        {
            if (Target is null || !IsInstanceValid(Target)) return;

            // Read BEFORE re-planting, and skip the first frame (nothing has been planted yet, so
            // a zero there would be the seed's absence rather than the drain). See
            // MinErrorBeforeReplantM — this ordering is the whole positive control.
            if (Samples > 0)
                MinErrorBeforeReplantM = Mathf.Min(MinErrorBeforeReplantM, Target.VisualErrorM);

            // Through the same seam Reconcile folds a real server pop through, which is what makes
            // this a correction offset rather than a number written into a test's own field.
            Target.TestInjectVisualError(Seed);

            Node3D? visual = Target.GetNodeOrNull<Node3D>("Visual");
            if (visual is null) return;

            Samples++;
            PeakVisualErrorM = Mathf.Max(PeakVisualErrorM, Target.VisualErrorM);
            MinY = Mathf.Min(MinY, visual.Position.Y);
            MaxY = Mathf.Max(MaxY, visual.Position.Y);
            Vector3 r = visual.Rotation;
            PeakTurns = Mathf.Max(PeakTurns,
                (Mathf.Abs(r.X) + Mathf.Abs(r.Y) + Mathf.Abs(r.Z)) / Mathf.Tau);
        }
    }

    public override void _Ready() => CallDeferred(nameof(Run));

    private void Run()
    {
        GD.Print("[bubbletest-selftest] layout contract, bake compliance, collider audit, "
                 + "prop heights");

        CheckRegistration();
        CheckRootFileHasNoGeometry();
        CheckBake();
        CheckColliderControl();

        // Everything past here needs the world alive in a tree.
        var packed = GD.Load<PackedScene>(ScenePaths.BubbleTest);
        if (packed is null)
        {
            Fail($"could not load {ScenePaths.BubbleTest}");
            Finish();
            return;
        }
        _world = packed.Instantiate<BubbleTestWorld>();
        AddChild(_world);

        CheckAnchors();
        CheckFootprints();
        CheckSpawns();
        CheckAtmosphere();
        CheckRespawnConfig();
        CheckLakeBounds();
        CheckBubbleCensus();
        CheckSecretPicture();

        // BUBBLE-2. Staged HERE, before the settle, because a body added inside the check is not
        // in the physics space when the check queries it — the first version of this control
        // reported itself free for exactly that reason.
        StageEmbedControlShell();

        // GUARD-1. The prop-height check queries the physics space, and a body that entered the
        // tree this frame is not in that space until a physics tick has run — a ray cast here
        // finds nothing and the guard would report "no ground" for every prop in the level. One
        // short wait covers it, and it runs BEFORE StartBoundaryProbe rather than beside it so
        // that the probe's two avatars are not standing in a ray while it measures.
        GetTree().CreateTimer(PhysicsSettleSeconds).Timeout += MeasureThenProbe;
    }

    private void MeasureThenProbe()
    {
        CheckPropHeights();
        StartBoundaryProbe();
        // BUBBLE-1: needs the physics space populated for the same reason CheckPropHeights does,
        // so it rides the same one settle rather than taking a second. AFTER StartBoundaryProbe
        // because that is where the probe bodies are made, and this reads the LIVE avatar's own
        // capsule off one of them — before it, _stray is still null. The bodies it stages are all
        // outdoors and none has been through a physics tick yet, so none of them can be what this
        // check finds standing in a sealed room 20 m down.
        CheckIndoorReach();
        // BUBBLE-2. Rides the same settle for the same reason, and runs AFTER the reach check so
        // that when a bubble is both buried and indoors the more specific message comes first.
        // Unlike the reach check this one sweeps ALL of them, indoors and out — the bubble Talon
        // could not reach was outdoors, halfway up a tower, where nothing was looking.
        CheckBubbleEmbedding();
        // ROOFTOP-1: rays the lab's live roof, the diving board and the fall, so it needs the
        // physics space for the same reason the two above do and rides the same one settle.
        CheckRooftopRoute();
    }

    // --- ROOFTOP-1. The seventh television, the sky deck, the board and the fall ---------------

    /// <summary>How far below the sky deck a fall probe is allowed to look before it counts as
    /// "nothing down there". 400 m: the deck's floor is 200 m up and the deepest authored surface
    /// under it is the hub plaza at 0, so this reaches twice as far as it needs to and a miss is a
    /// real miss rather than a short rope.</summary>
    private const float FallProbeDepthM = 400f;

    /// <summary>Clear air a fall must leave between where it lands and the global kill plane
    /// (<c>MpFoundation.Net.NetProfile.KillPlaneY</c>, −30). 20 m: the hub plaza is 30 m above it, so this fails
    /// long before a landing becomes a death and cannot pass by a hair.</summary>
    private const float FallKillPlaneMarginM = 20f;

    /// <summary>
    /// <b>Talon's route, the pad on the end of it, the board in the sky and the fall back down —
    /// measured against the live world rather than asserted from the layout file.</b>
    ///
    /// <para>Talon, 2026-09-04, on the traversal this feature exists to reward:
    /// <i>"a player can jump from the top of the cyan platform down and onto the lab ... I love it
    /// please do not change this"</i>. <b>Nothing here changes it and nothing here tests his
    /// skill.</b> The route's own geometry is PRINTED — the roof's extent, the drop, the gap, the
    /// tunnel, the crate, the shipped jump envelope — so a report can quote measurements instead
    /// of claims, and so the next packet that touches cyan's east edge or the lab's anchor sees
    /// the numbers move. What is ASSERTED is only what this packet is responsible for.</para>
    ///
    /// <para><b>The one that earns its place is the pad-versus-roof ray.</b>
    /// <see cref="BubbleTestLayout.LabCeilingTopY"/> is a mirror of geometry in an optional,
    /// ported, 1.38-scaled scene this contract does not own. A mirror with an instrument on it is
    /// a mirror; one without is a second copy waiting to drift, and this file has paid for that
    /// four times. So the live roof is rayed under the pad every run and the two must agree.</para>
    ///
    /// <para><b>The lab-absent case is a pass, not a skip-and-hope.</b> With no
    /// <c>PuffinLab.tscn</c> in the build there is no roof to compare against — but the PAD is in
    /// <c>LabRoof.tscn</c> and ships regardless, so the television still stands on authored
    /// ground and every other assertion below still runs. That is the whole reason the pad exists
    /// rather than the television standing straight on the lab's ceiling.</para></summary>
    private void CheckRooftopRoute()
    {
        var tv = _world.GetNodeOrNull<Node3D>(BubbleTestLayout.RooftopTvNodeName);
        if (tv is null)
        {
            Fail($"the world has no '{BubbleTestLayout.RooftopTvNodeName}' — the seventh "
                 + "television is not in the level, so there is no way to the sky deck at all.");
            return;
        }

        PhysicsDirectSpaceState3D space = _world.GetWorld3D().DirectSpaceState;

        // --- 1. The pad, and the roof it is supposed to be two centimetres above ---------------
        var pad = _world.GetNodeOrNull<Node3D>(
            $"{BubbleTestLayout.Section.LabRoof}/TvPad");
        if (pad is null)
        {
            Fail($"{BubbleTestLayout.Section.LabRoof} has no TvPad — the seventh television has "
                 + "no authored ground of its own, so a build without the optional PuffinLab "
                 + "scene would have it standing in mid-air.");
        }
        else
        {
            var excludePad = new Godot.Collections.Array<Rid>();
            CollectBodyRids(pad, excludePad);
            float? roofY = SolidYUnder(space, BubbleTestLayout.RooftopTvPos + new Vector3(0f, 1f, 0f),
                                       6f, excludePad);
            if (roofY is null)
            {
                GD.Print("[bubbletest-selftest]   rooftop: no PuffinLab far roof under the pad — "
                         + "the lab is absent from this build. The pad and the television are "
                         + "still here and still measured; the ROUTE to them is not, which is the "
                         + "supported absent case, not a failure.");
            }
            else
            {
                float delta = BubbleTestLayout.LabRoofPadTopY - roofY.Value;
                GD.Print($"[bubbletest-selftest]   rooftop: pad top {BubbleTestLayout.LabRoofPadTopY:F3}, "
                         + $"live FAR roof (Route/VoidCeil) under it {roofY.Value:F3}, proud by "
                         + $"{delta:F3} m (want {BubbleTestLayout.LabRoofPadProudM:F3})");
                Check(Mathf.Abs(delta - BubbleTestLayout.LabRoofPadProudM) <= 0.01f,
                    $"the television's pad stands {delta:F3} m above the far roof, not "
                    + $"{BubbleTestLayout.LabRoofPadProudM:F3}. BubbleTestLayout.LabVoidRoofTopY "
                    + $"({BubbleTestLayout.LabVoidRoofTopY:F3}) is a MIRROR of that roof and it has "
                    + "drifted: either the lab moved, or its scale changed, or the pad did. A "
                    + "coplanar pair z-fights and a lip is a step at the top of a 3.9 m climb — "
                    + "neither is something a screenshot of the working case would show.");
            }
        }

        // --- 2. Talon's route, printed rather than asserted ------------------------------------
        // Everything on this line is a measurement of ground THIS PACKET DID NOT AUTHOR, and it is
        // here so the numbers are on the record and move when the geometry does. The jump envelope
        // is derived from the shipped motor rather than typed: a sprinting body leaving a lip at
        // MoveSpeed x SprintMultiplier with a full jump, falling to the roof.
        // LEG 1: cyan's east lip -> the LANDING roof.
        const float cyanEastLipX = 70f;                       // CyanRun/Ground/Body/Shape
        const float landingRoofWestX = 78.55f;                // Lab/HubAccess_Off/Ceiling
        float drop = 0f - BubbleTestLayout.LabLandingRoofTopY;
        float gap = landingRoofWestX - cyanEastLipX;
        float vSprint = MpFoundation.Net.AvatarMotor.MoveSpeed
                        * MpFoundation.Net.MotorTuning.Current.SprintMultiplier;
        float atOnce = vSprint * AirtimeToDrop(drop, jumpAfterFallingSec: 0f);
        // Coyote time is 0.30 s here and it is NOT a rounding term: a body that runs off the lip,
        // falls for the whole window and only THEN spends its jump is 1.6 m lower and 1.8 m
        // further out when the jump fires, and every metre of that is horizontal reach the
        // immediate jump never gets.
        float coyote = MpFoundation.Net.MotorTuning.Current.CoyoteTimeSec;
        float lateJump = vSprint * AirtimeToDrop(drop, coyote);
        // The capsule is not a point: a body can stand with its centre roughly a radius past the
        // lip and lands the moment its lower sphere touches the roof, so the crossing it actually
        // has to make is a body radius shorter at each end.
        float r = AvatarProportions.Fallback.CapsuleRadiusM;
        GD.Print($"[bubbletest-selftest]   rooftop leg 1 (MEASURED, NOT ASSERTED — Talon's route "
                 + $"is his and this packet does not touch it): cyan's east lip x={cyanEastLipX}, "
                 + $"the LANDING roof's west lip x={landingRoofWestX} ({gap:F2} m of gap, "
                 + $"{gap - 2f * r:F2} m centre-to-centre for a {r:F2} m capsule), landing roof top "
                 + $"y={BubbleTestLayout.LabLandingRoofTopY:F3} ({drop:F2} m of drop). Sprinting at "
                 + $"{vSprint:F2} m/s: an immediate jump reaches {atOnce:F2} m, a jump spent at the "
                 + $"end of the {coyote:F2} s coyote window reaches {lateJump:F2} m.");

        // LEG 2: the tunnel -> the FAR roof, which is the leg Talon's cube stack has to make and
        // the one where the arithmetic does not come out where he expected. Printed, never
        // asserted: the geometry is EGG-1's, the access method is Talon's, and a self-test that
        // turned his design intent red would be this file exceeding its remit.
        const float crateM = 0.44f;   // Crate.tscn's BoxShape3D, the level's golden cube
        float apex = MpFoundation.Net.AvatarMotor.JumpVelocity
                     * MpFoundation.Net.AvatarMotor.JumpVelocity
                     / (2f * MpFoundation.Net.AvatarMotor.Gravity);
        float three = 3f * crateM + apex;
        int need = Mathf.CeilToInt((BubbleTestLayout.LabRoofClimbM - apex) / crateM);
        GD.Print($"[bubbletest-selftest]   rooftop leg 2 (MEASURED, NOT ASSERTED): the tunnel "
                 + $"(Route/HallCeil) tops out at {BubbleTestLayout.LabTunnelTopY:F3} and the FAR "
                 + $"roof (Route/VoidCeil) at {BubbleTestLayout.LabVoidRoofTopY:F3} — a climb of "
                 + $"{BubbleTestLayout.LabRoofClimbM:F3} m. A golden cube is {crateM:F2} m and a "
                 + $"full jump's apex is {apex:F2} m, so THREE cubes lift a player {three:F2} m: "
                 + $"{BubbleTestLayout.LabRoofClimbM - three:F2} m SHORT. {need} cubes clear it.");
        GD.Print($"[bubbletest-selftest]   rooftop roofs: LANDING centre (90.00, 90.00) top "
                 + $"{BubbleTestLayout.LabLandingRoofTopY:F3}; FAR centre (167.97, 79.79) top "
                 + $"{BubbleTestLayout.LabVoidRoofTopY:F3} — 78.64 m apart in plan (77.97 east, "
                 + $"10.21 north), the far one "
                 + $"{BubbleTestLayout.LabLandingRoofTopY - BubbleTestLayout.LabVoidRoofTopY:F3} m "
                 + "lower. The television is on the FAR one.");

        // --- 3. The board, and the hundredth bubble on the end of it ---------------------------
        Vector3 bub = BubbleTestLayout.SkyBubblePos;
        float? boardY = SolidYUnder(space, bub, 4f, new Godot.Collections.Array<Rid>());
        if (boardY is null)
        {
            Fail($"nothing solid within 4 m under the hundredth bubble at {bub} — the diving "
                 + "board is not under it, so the level's last bubble hangs in open sky over a "
                 + "200 m drop and the only way to it is a fall past it.");
        }
        else
        {
            const float crown = AvatarProportions.PlayerCrownM;
            float underside = bub.Y - Sail.Game.Bubble.Bubble.ColliderRadiusM;
            float reach = boardY.Value + crown;
            GD.Print($"[bubbletest-selftest]   rooftop: board top under the bubble {boardY.Value:F3}, "
                     + $"bubble collider underside {underside:F3}, a standing crown reaches "
                     + $"{reach:F3} ({reach - underside:F3} m of margin)");
            Check(Mathf.Abs(boardY.Value - BubbleTestLayout.DivingBoardTopY) <= 0.05f,
                $"the surface under the hundredth bubble is at {boardY.Value:F3}, not the diving "
                + $"board's {BubbleTestLayout.DivingBoardTopY:F3} — the bubble is over something "
                + "else, or the board moved out from under it.");
            Check(reach >= underside,
                $"a standing body on the board reaches {reach:F3} and the bubble's collider "
                + $"bottoms out at {underside:F3} — the level's HUNDREDTH bubble cannot be "
                + "collected by walking to it, which makes 'collect all the bubbles' a lie at "
                + "exactly the last one.");
        }

        // --- 4. The fall, from five points around the deck -------------------------------------
        // Not one probe. Talon's beat is "they get to fall": a player leaves this platform from
        // wherever they were standing, and a deck whose north side lands on the plaza while its
        // east side lands in the void would be a coin flip nobody could see coming.
        float h = BubbleTestLayout.SkyDeckHalfM - 0.5f;
        (string Where, Vector3 At)[] falls =
        {
            ("board tip",  new Vector3(0f, BubbleTestLayout.SkyDeckY, BubbleTestLayout.DivingBoardTipZ)),
            ("deck north", new Vector3(0f, BubbleTestLayout.SkyDeckY, -h)),
            ("deck east",  new Vector3(h, BubbleTestLayout.SkyDeckY, 0f)),
            ("deck south", new Vector3(0f, BubbleTestLayout.SkyDeckY, h)),
            ("deck west",  new Vector3(-h, BubbleTestLayout.SkyDeckY, 0f)),
        };
        foreach ((string where, Vector3 at) in falls)
        {
            float? land = SolidYUnder(space, at + new Vector3(0f, -1f, 0f), FallProbeDepthM,
                                      new Godot.Collections.Array<Rid>());
            if (land is null)
            {
                Fail($"a fall from the sky deck's {where} at {at} finds NOTHING solid in "
                     + $"{FallProbeDepthM} m — the payoff Talon asked for ('then they get to "
                     + "fall') is a drop into the void and a respawn, not a landing.");
                continue;
            }
            float t = Mathf.Sqrt(2f * (at.Y - land.Value)
                                 / (MpFoundation.Net.AvatarMotor.Gravity * MpFoundation.Net.AvatarMotor.FallGravityMultiplier));
            GD.Print($"[bubbletest-selftest]   rooftop fall from {where,-10} at {at} -> lands "
                     + $"y={land.Value:F2} after {t:F2} s of free fall "
                     + $"({land.Value - MpFoundation.Net.NetProfile.KillPlaneY:F1} m above the kill plane)");
            Check(land.Value > MpFoundation.Net.NetProfile.KillPlaneY + FallKillPlaneMarginM,
                $"a fall from the sky deck's {where} lands at y={land.Value:F2}, within "
                + $"{FallKillPlaneMarginM} m of the global kill plane at {MpFoundation.Net.NetProfile.KillPlaneY}. "
                + "The fall is the reward; a fall that kills is a punishment for collecting the "
                + "last bubble.");
        }

        // --- 5. The way out lands on ground -----------------------------------------------------
        Vector3 ret = BubbleTestLayout.RooftopTvReturn;
        float? retGround = SolidYUnder(space, ret, 6f, new Godot.Collections.Array<Rid>());
        if (retGround is null)
        {
            Fail($"the sky deck returns players to {ret} with nothing solid within 6 m below it — "
                 + "a player who does not want to jump is dropped into the void instead.");
        }
        else
        {
            GD.Print($"[bubbletest-selftest]   rooftop return {ret} -> ground at "
                     + $"{retGround.Value:F3}, {ret.X - 0f:F1} m in x from cyan's east lip at 70");
            Check(retGround.Value > BubbleTestLayout.SectionVolumeBottomY,
                $"the sky deck's return at {ret} stands on something at y={retGround.Value:F3}, "
                + "below the level's ground plane — the way out has to leave the room and land "
                + "somewhere a player can walk from.");
        }
    }

    /// <summary>
    /// How long a body is airborne between running off a lip and touching down
    /// <paramref name="drop"/> metres lower, on the SHIPPED motor constants: fall for
    /// <paramref name="jumpAfterFallingSec"/> (the coyote window, or 0 for an immediate jump),
    /// then spend the jump, rise under <c>Gravity</c>, and fall the rest under
    /// <c>Gravity × FallGravityMultiplier</c>.
    ///
    /// <para><b>Deliberately conservative, and it says so rather than pretending to be exact.</b>
    /// It does not model <c>ApexHangStrength</c> (0.30 of gravity removed within 2 m/s of the
    /// apex, which lengthens every arc), <c>AirControl*</c>, or the jump buffer. So the number it
    /// prints is a FLOOR on the reach, not a verdict on whether a route is jumpable — which is
    /// exactly the claim this file is entitled to make about a traversal it did not author.</para>
    /// </summary>
    private static float AirtimeToDrop(float drop, float jumpAfterFallingSec)
    {
        float gUp = MpFoundation.Net.AvatarMotor.Gravity;
        float gDown = gUp * MpFoundation.Net.AvatarMotor.FallGravityMultiplier;
        float y = -0.5f * gDown * jumpAfterFallingSec * jumpAfterFallingSec;
        float rise = MpFoundation.Net.AvatarMotor.JumpVelocity / gUp;
        float apex = y + 0.5f * MpFoundation.Net.AvatarMotor.JumpVelocity * rise;
        float remaining = apex + drop;
        return jumpAfterFallingSec + rise + Mathf.Sqrt(2f * remaining / gDown);
    }

    /// <summary>The y of the first solid thing under <paramref name="from"/> within
    /// <paramref name="depth"/>, or null. Shares <see cref="CheckStandsOnGround"/>'s exclusion
    /// discipline — a ray that can hit the thing it is measuring is a detector that can only say
    /// "pass" — and takes its exclusion list from the caller because what counts as "own body"
    /// differs per probe.</summary>
    private static float? SolidYUnder(PhysicsDirectSpaceState3D space, Vector3 from, float depth,
                                      Godot.Collections.Array<Rid> exclude)
    {
        PhysicsRayQueryParameters3D q = PhysicsRayQueryParameters3D.Create(
            from, from - new Vector3(0f, depth, 0f));
        q.Exclude = exclude;
        Godot.Collections.Dictionary hit = space.IntersectRay(q);
        return hit.Count == 0 ? null : ((Vector3)hit["position"]).Y;
    }

    // --- WATER-3. The swimmable lake and the rendered one -------------------------------------

    /// <summary>
    /// <b>The swimmable region and the visible water agree</b> (Talon's note 3, 2026-08-29:
    /// <i>"the water outside the map ... will allow the player to continue swimming as if there is
    /// nothing there like floating in air invisible water"</i>).
    ///
    /// <para>The check is deliberately against the <b>shipped mesh's world AABB</b>, not against a
    /// number copied out of the bake script: <c>WaterGeometry.BubbleTestLake</c> is derived from
    /// what GreenHills renders, so the thing that keeps it honest has to be the render. A re-bake
    /// that moves the shoreline therefore turns into a red test rather than a strip of swimmable
    /// nothing.</para>
    ///
    /// <para>Then sampled points prove the predicate does what the AABB implies: the lake centre is
    /// water; a point 5 m outside the disc at the same depth is not; and <b>the exact case Talon
    /// hit</b> — far west, far below, off the end of the level — is dry, so a body there falls and
    /// the boundary scan can call it <c>OffTheEdge</c>/<c>Void</c> instead of drowning it in
    /// mid-air.</para>
    /// </summary>
    /// <summary>
    /// <b>BUBBLE-1: every section carries the number the layout says it carries.</b>
    ///
    /// <para><b>Why this did not exist before and why it does now.</b> The class doc above lists
    /// bubble counts among the things this file deliberately does not check — that was BT-11's
    /// to own — and the cost of that showed up the moment anyone counted: BT-11 shipped 33 in
    /// cyan against a nominal 35, 16 in red against 15 and 16 in green against 15. All three were
    /// inside <c>BubbleSplitTolerance</c> and all three are written down in
    /// <c>docs/levels/bubble-test-bubbles.md</c>, so nothing was hidden — but the only thing
    /// holding the constants to the scenes was a table maintained by hand, and
    /// <c>TheBubbleSplitAddsUpToTheTarget</c> in the xUnit suite only ever checked the constants
    /// against each other. A split that adds up correctly and describes no scene in particular is
    /// arithmetic, not a level. This walks the LIVE tree and makes the split a measurement.</para>
    ///
    /// <para><b>The seam is split by NAME, and that is a real contract rather than a
    /// convenience.</b> The five <see cref="BubbleTestLayout.SeamBubbles"/> live in
    /// <c>Hub.tscn</c> because the connectors do, so the hub's subtree holds
    /// <c>HubBubbles + SeamBubbles</c> nodes and nothing in the tree distinguishes them except
    /// the <c>Bubble_Seam</c> prefix. If that prefix is ever dropped this check fails loudly on
    /// the hub rather than quietly counting a seam bubble as a hub one.</para>
    ///
    /// <para><b>The lab is counted separately and only when it is there</b>, for the same reason
    /// <see cref="BubbleTestLayout.BubbleTargetWithLab"/> exists: it is optional, it is not a
    /// <see cref="BubbleTestLayout.Section"/>, and a build without it must come up honest rather
    /// than red.</para></summary>
    private void CheckBubbleCensus()
    {
        const string SeamPrefix = "Bubble_Seam";
        int total = 0;

        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            Node? root = _world.GetNodeOrNull(s.ToString());
            if (root is null)
            {
                Fail($"{s}: the section node is not in the world, so its bubbles cannot be counted.");
                continue;
            }

            var found = new List<Bubble.Bubble>();
            CollectBubbles(root, found);
            int seam = 0;
            foreach (Bubble.Bubble b in found)
                if (b.Name.ToString().StartsWith(SeamPrefix, System.StringComparison.Ordinal))
                    seam++;
            int own = found.Count - seam;
            int want = BubbleTestLayout.BubblesOf(s);
            total += found.Count;

            // The id range is printed because ids are a CONSEQUENCE of the ordinal node-path
            // sort and of nothing else, so the only honest way to state one is to read it back
            // off the adoption pass. Anything that renumbers this level — a new section, a
            // renamed root, an inserted bubble — shows up here as a range that moved.
            int lo = int.MaxValue, hi = -1;
            foreach (Bubble.Bubble bb in found) { lo = Mathf.Min(lo, bb.Id); hi = Mathf.Max(hi, bb.Id); }
            GD.Print($"[bubbletest-selftest]   census {s,-14} {own,3} bubbles (want {want,3})"
                     + (seam > 0 ? $" + {seam} seam (want {BubbleTestLayout.SeamBubbles})" : "")
                     + $"  ids {lo}..{hi}");
            if (s == BubbleTestLayout.Section.Hub)
                foreach (Bubble.Bubble bb in found)
                    if (bb.Name.ToString().StartsWith(SeamPrefix, System.StringComparison.Ordinal))
                        GD.Print($"[bubbletest-selftest]     {bb.Name} id {bb.Id} at {bb.GlobalPosition}");
            Check(own == want,
                $"{s} carries {own} bubbles but BubbleTestLayout.BubblesOf says {want}. Either the "
                + "scene was edited without the split, or the split was edited without the scene; "
                + "the table in docs/levels/bubble-test-bubbles.md is wrong either way.");

            if (s == BubbleTestLayout.Section.Hub)
                Check(seam == BubbleTestLayout.SeamBubbles,
                    $"the hub carries {seam} '{SeamPrefix}*' bubbles against "
                    + $"{BubbleTestLayout.SeamBubbles}. The seam bubbles belong to no section and "
                    + "live in Hub.tscn only because the connectors do — if they have been renamed "
                    + "they are now being counted as the hub's own.");
            else
                Check(seam == 0,
                    $"{s} carries {seam} '{SeamPrefix}*' bubbles. The seam belongs in Hub.tscn "
                    + "with the connectors; a seam bubble anywhere else is counted twice over.");
        }

        Node? lab = _world.GetNodeOrNull(BubbleTestWorld.PuffinLabNodeName);
        int labCount = 0;
        if (lab is not null)
        {
            var found = new List<Bubble.Bubble>();
            CollectBubbles(lab, found);
            labCount = found.Count;
            total += labCount;
            Check(labCount == BubbleTestLayout.PuffinLabBubbles,
                $"the puffin lab carries {labCount} bubbles against "
                + $"{BubbleTestLayout.PuffinLabBubbles}.");
        }

        int want2 = lab is null
            ? BubbleTestLayout.BubbleTarget
            : BubbleTestLayout.BubbleTargetWithLab;
        GD.Print($"[bubbletest-selftest]   census TOTAL          {total,3} bubbles "
                 + $"(want {want2}; lab {(lab is null ? "absent" : $"present, {labCount}")})");
        Check(total == want2,
            $"the level's authored bubbles total {total} against {want2}. The per-section lines "
            + "above say which section moved.");
    }

    private static void CollectBubbles(Node node, List<Bubble.Bubble> into)
    {
        if (node is Bubble.Bubble b)
            into.Add(b);
        foreach (Node child in node.GetChildren())
            CollectBubbles(child, into);
    }

    // --- BUBBLE-1: the indoor bubbles are reachable ------------------------------------------

    /// <summary>How far below a bubble to look for the floor it is meant to be collected from.
    /// Generous — the point is to find a floor or prove there is none, not to bound a drop.
    /// </summary>
    private const float ReachFloorSearchM = 6f;

    /// <summary>Shrink on the query capsule, so a body that fits with millimetres to spare is not
    /// reported as blocked by float noise. The same 2 cm <c>PuffinLabSelfTest</c> uses.</summary>
    private const float ReachFitMarginM = 0.02f;

    /// <summary>Spacing of the samples along a walked leg. Under half the body's diameter, so no
    /// wall can sit between two consecutive samples unnoticed.</summary>
    private const float ReachStrideM = 0.35f;

    /// <summary>How far above and below the destination's floor the walked leg looks for the
    /// ground under each sample. Over every prop in these rooms and under every ceiling — a ray
    /// that starts inside the ceiling slab finds nothing and reads as a hole in the floor.</summary>
    private const float FloorRideProbeM = 2.5f;

    /// <summary>
    /// <b>BUBBLE-1: every bubble added indoors can actually be collected.</b> A bubble the player
    /// cannot get to makes "collect all the bubbles" a lie, and indoors is where that is easiest
    /// to do by accident: the six TV rooms are sealed boxes with no floor at all in the void
    /// between them, and the only way into any of them is a teleport.
    ///
    /// <para><b>What is measured, per bubble, against the live physics space</b> — never against
    /// the authored numbers, which are what a mistake would be written in:</para>
    /// <list type="number">
    /// <item>a downward ray finds a floor under it within <see cref="ReachFloorSearchM"/>;</item>
    /// <item>the bubble is <i>within a body's reach</i> of that floor: the lowest point its
    /// collider ever occupies — centre minus <c>ColliderRadiusM</c> minus the bob's
    /// <c>MaxOffsetM</c>, so the worst phase of the oscillation, not the mean — is below the
    /// crown of the LIVE avatar capsule standing there. Measured off the model the build actually
    /// loads, so this goes red the day the player's height changes rather than the level going
    /// quietly uncollectable;</item>
    /// <item>a body can STAND under it: the shrunk capsule fits at that floor point;</item>
    /// <item>the standing point is joined to where the player arrives by a swept capsule, sampled
    /// every <see cref="ReachStrideM"/>, with nothing solid in the way.</item>
    /// </list>
    ///
    /// <para><b>Item 4 is only run where a straight line IS the route</b>, which is the twelve TV
    /// room bubbles, the Den's three and the four in the lab's own room — each of those sits in
    /// one open sealed volume with its arrival point. The six down the lab's crawl, hallway and
    /// black room are not straight-line reachable from the lab's <c>Arrival</c> and are not
    /// claimed to be: they ride <c>PuffinLabSelfTest</c>'s nineteen-station route, which that
    /// suite proves traversable by the live capsule on every run, and each of the six sits
    /// directly above one of those stations. Items 1-3 still run on all of them. <b>This is
    /// stated rather than papered over</b>: a straight sweep down the crawl would be a check that
    /// passes for the wrong reason.</para>
    ///
    /// <para><b>The honest limit of this check.</b> It is a physics measurement, not a driven bot.
    /// The shipped scripted brain (<c>ScriptedGotoIntentSource</c>) walks to ONE point and latches
    /// there, and a bot cannot be spawned inside a sealed room, so a multi-leg indoor route is not
    /// something the harness can drive today — building it is a change to the intent sources,
    /// which this role does not own. What is below proves the geometry of reachability against the
    /// real collision world with a positive control; it does not prove a bot walked it.</para>
    ///
    /// <para><b>And it carries its own positive control</b>, because an all-clear from a query
    /// that is not reaching the world looks exactly like an all-clear from a level that is fine.
    /// Three deliberately unreachable bubbles are planted — one inside a wall, one over the void
    /// between two rooms, one hung out of reach above its floor — and every one of them must be
    /// rejected, each by the specific item it violates.</para></summary>
    private void CheckIndoorReach()
    {
        AvatarProportions p = _stray.Proportions;
        float radius = Mathf.Max(0.01f, p.CapsuleRadiusM - ReachFitMarginM);
        float height = Mathf.Max(radius * 2f, p.CapsuleHeightM - ReachFitMarginM * 2f);
        var shape = new CapsuleShape3D { Radius = radius, Height = height };
        float lift = p.CapsuleHeightM * 0.5f;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { _stray.GetRid() };

        GD.Print($"[bubbletest-selftest]   reach: live avatar crown {p.CrownM:F3} m, capsule "
                 + $"r {p.CapsuleRadiusM:F3} h {p.CapsuleHeightM:F3} "
                 + $"(query r {radius:F3} h {height:F3})");

        // POSITIVE CONTROL FIRST, and it must fire before a single "reachable" below is believed.
        // Room B's west wall inner face is at section-local x = -23, its floor top at y = 0; the
        // void between RoomB and the Den has no geometry at any height; and 6 m over the Den's
        // floor is past any crown. One planted failure per item 1/2/3.
        Vector3 anchor = BubbleTestLayout.TvRoomAnchor;
        (string Why, Vector3 At)[] controls =
        {
            // Item 3. RoomB's west wall's inner face is at x = -23; 5 cm off it there is floor
            // under the point and open air at the point, but no body fits, because the query
            // capsule's own radius reaches into the wall.
            ("5 cm off RoomB's west wall", anchor + new Vector3(-22.95f, 1.2f, 0f)),
            // Item 1. RoomB's outer edge is x = -23.5 and the Den's is -6.5; -9.5 is the void
            // between them, where this section has no geometry at any height.
            ("over the void between the rooms", anchor + new Vector3(-9.5f, 1.2f, 0f)),
            // Item 2. Inside the Den, in clear air, but 3.5 m over a floor at y = 0 -- more than
            // twice a standing body's crown, so nothing can touch it.
            ("3.5 m over the Den's floor", anchor + new Vector3(0f, 3.5f, 2f)),
        };
        foreach ((string why, Vector3 at) in controls)
        {
            bool reachable = Reachable(space, shape, lift, exclude, at, null, out string detail);
            GD.Print($"[bubbletest-selftest]   reach CONTROL {why,-34} -> "
                     + $"{(reachable ? "REACHABLE (BAD)" : "rejected")}: {detail}");
            Check(!reachable,
                $"the reachability control at {at} ({why}) came back reachable. The check is not "
                + "measuring the world, so every bubble it passed below is unproven.");
        }

        // Where the player is standing when each indoor bubble becomes their problem.
        var arrivals = new Dictionary<string, Vector3>();
        foreach (BubbleTestLayout.TvRoute r in BubbleTestLayout.TvRoutes)
        {
            // "RoomB/RoomTv" -> "RoomB"; the Den's "RoomTv" -> "" (bubbles directly under
            // TvRoom/Bubbles). One table, so a sixth room is a row in TvRoutes and nothing here.
            int slash = r.ReturnTvPath.IndexOf('/');
            arrivals[slash < 0 ? string.Empty : r.ReturnTvPath[..slash]] =
                BubbleTestLayout.ArrivalOf(r);
        }

        int checkedCount = 0;
        foreach (Bubble.Bubble b in CollectIndoor())
        {
            string path = b.GetPath().ToString();
            Vector3? from = null;
            bool sweep = true;

            if (path.Contains($"/{BubbleTestLayout.Section.TvRoom}/"))
            {
                // ".../TvRoom/RoomB/Bubbles/X" -> "RoomB"; ".../TvRoom/Bubbles/X" -> "" (the Den).
                string tail = path[(path.IndexOf($"/{BubbleTestLayout.Section.TvRoom}/",
                    System.StringComparison.Ordinal) + BubbleTestLayout.Section.TvRoom.ToString().Length + 2)..];
                string room = tail.StartsWith("Bubbles/", System.StringComparison.Ordinal)
                    ? string.Empty
                    : tail[..tail.IndexOf('/')];
                if (arrivals.TryGetValue(room, out Vector3 a)) from = a;
            }
            else if (_world.LabArrival is { } labArrival)
            {
                // The lab's own room only. The crawl, the hallway and the black room are not
                // straight-line reachable from Arrival and are not claimed to be — see the note
                // in this method's summary. Its room's interior is |x|,|z| under 11 m of the
                // lab's origin, which is the anchor.
                Vector3 rel = b.GlobalPosition - BubbleTestWorld.PuffinLabAnchor;
                if (Mathf.Abs(rel.X) <= 11.1f && Mathf.Abs(rel.Z) <= 8.3f)
                    from = labArrival.GlobalPosition;
                else
                    sweep = false;
            }

            bool ok = Reachable(space, shape, lift, exclude, b.GlobalPosition, sweep ? from : null,
                out string detail);
            checkedCount++;
            GD.Print($"[bubbletest-selftest]   reach {b.Name,-16} {(ok ? "ok " : "NO ")}{detail}");
            Check(ok,
                $"{b.Name} at {b.GlobalPosition} cannot be collected: {detail}. A bubble the "
                + "player cannot reach makes 'collect all the bubbles' unwinnable.");
        }

        GD.Print($"[bubbletest-selftest]   reach: {checkedCount} indoor bubbles measured "
                 + "(3 controls rejected first)");
        Check(checkedCount == BubbleTestLayout.TvRoomBubbles + BubbleTestLayout.PuffinLabBubbles,
            $"the reach check measured {checkedCount} indoor bubbles, not "
            + $"{BubbleTestLayout.TvRoomBubbles + BubbleTestLayout.PuffinLabBubbles}. It is "
            + "looking in the wrong place, so its greens mean nothing.");
    }

    /// <summary>Every bubble in a sealed indoor space: the TV room section's, and the lab's when
    /// the lab is in this build.</summary>
    private List<Bubble.Bubble> CollectIndoor()
    {
        var found = new List<Bubble.Bubble>();
        if (_world.GetNodeOrNull(BubbleTestLayout.Section.TvRoom.ToString()) is { } tv)
            CollectBubbles(tv, found);
        if (_world.GetNodeOrNull(BubbleTestWorld.PuffinLabNodeName) is { } lab)
            CollectBubbles(lab, found);
        found.Sort((a, b) => string.CompareOrdinal(a.GetPath().ToString(), b.GetPath().ToString()));
        return found;
    }

    /// <summary>The four measurements, in order, with the first failure named. <paramref name="from"/>
    /// null skips the walked leg (item 4) — see <see cref="CheckIndoorReach"/> for when that is
    /// legitimate and when it would be a check passing for the wrong reason.</summary>
    /// <param name="lift">Half the UNSHRUNK capsule's height. The query shape is shrunk by
    /// <see cref="ReachFitMarginM"/> but is centred on the full body's centre, so its bottom sits
    /// that margin ABOVE the floor rather than exactly on it. Without this every standing spot in
    /// the level reports itself blocked by the floor it is standing on — which is what the first
    /// run of this check did, on all 25 bubbles at once.</param>
    private static bool Reachable(PhysicsDirectSpaceState3D space, CapsuleShape3D shape, float lift,
        Godot.Collections.Array<Rid> exclude, Vector3 bubble, Vector3? from, out string detail)
    {
        // 1. Is there a floor under it at all.
        var ray = new PhysicsRayQueryParameters3D
        {
            From = bubble,
            To = bubble + new Vector3(0f, -ReachFloorSearchM, 0f),
            CollisionMask = 1,
            Exclude = exclude,
        };
        Godot.Collections.Dictionary hit = space.IntersectRay(ray);
        if (hit.Count == 0)
        {
            detail = $"no floor within {ReachFloorSearchM:F0} m below it — it is over a void";
            return false;
        }
        Vector3 floor = hit["position"].As<Vector3>();
        // THE SURFACE BEING STOOD ON IS EXCLUDED FROM THE OVERLAP BELOW. A vertical capsule
        // resting on a sloped trimesh always clips it -- PuffinLabSelfTest records exactly this,
        // and its first version reported the lab's crawl blocked by the crawl's own floor. This
        // check's first run reproduced it on Bubble_Lab_06, on that same TubeFloor. Excluding the
        // one body the ray landed on is the narrowest fix that keeps every OTHER body reporting:
        // a wall, a crate or a couch is still found, because none of them is what is underfoot.
        var standingOn = new Godot.Collections.Array<Rid>(exclude);
        if (hit.TryGetValue("rid", out Variant floorRid))
            standingOn.Add(floorRid.As<Rid>());

        // 2. Can a body standing on that floor touch it. Worst phase of the bob, not the mean.
        float lowest = bubble.Y - Bubble.Bubble.ColliderRadiusM - Bubble.BubbleOscillation.MaxOffsetM;
        float crown = floor.Y + shape.Height;
        if (lowest > crown)
        {
            detail = $"floor at {floor.Y:F2}, its collider bottoms out at {lowest:F2}, "
                     + $"{lowest - crown:F2} m over a standing body's crown at {crown:F2}";
            return false;
        }

        // 3. Can a body stand there.
        Vector3 stand = new(bubble.X, floor.Y + lift, bubble.Z);
        List<string> blocked = OverlapNames(space, shape, stand, standingOn);
        if (blocked.Count > 0)
        {
            detail = $"nothing can stand under it — blocked by {string.Join(", ", blocked)}";
            return false;
        }

        // 4. Is that standing spot joined to where the player arrives.
        if (from is { } origin)
        {
            Vector3 a = new(origin.X, 0f, origin.Z);
            Vector3 bxz = new(stand.X, 0f, stand.Z);
            float span = a.DistanceTo(bxz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(span / ReachStrideM));
            float last = floor.Y;
            float worstStep = 0f;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 xz = a.Lerp(bxz, (float)i / steps);
                // RIDE THE FLOOR ALONG THE WAY rather than hold the endpoint's height: a leg that
                // crosses a couch would otherwise be reported blocked by the couch it steps onto.
                // The ray starts 2.5 m over the destination floor -- above every prop in these
                // rooms (the tallest is a 0.8 m couch hull) and below every ceiling (the lowest is
                // RoomD's at 3 m), because a ray that starts inside the ceiling slab finds nothing
                // and would read as a hole in the floor.
                var down = new PhysicsRayQueryParameters3D
                {
                    From = new Vector3(xz.X, floor.Y + FloorRideProbeM, xz.Z),
                    To = new Vector3(xz.X, floor.Y - FloorRideProbeM, xz.Z),
                    CollisionMask = 1,
                    Exclude = exclude,
                };
                Godot.Collections.Dictionary underfoot = space.IntersectRay(down);
                if (underfoot.Count == 0)
                {
                    detail = $"the walk from {origin} crosses a hole {span * i / steps:F1} m along";
                    return false;
                }
                float groundY = underfoot["position"].As<Vector3>().Y;

                // A SWEEP THAT ONLY RIDES THE FLOOR WOULD CLIMB A WALL AS IF IT WERE A RAMP, and
                // report a bubble on the far side of it as walked to. So the rise between two
                // samples 35 cm apart is bounded: anything over a standing body's own height is a
                // climb, not a walk, and this check does not get to claim it.
                float rise = Mathf.Abs(groundY - last);
                worstStep = Mathf.Max(worstStep, rise);
                if (rise > shape.Height)
                {
                    detail = $"the walk from {origin} hits a {rise:F2} m step "
                             + $"{span * i / steps:F1} m along -- that is a climb, not a walk";
                    return false;
                }
                last = groundY;

                List<string> wall = OverlapNames(space, shape, new Vector3(xz.X, groundY + lift, xz.Z),
                    exclude);
                if (wall.Count > 0)
                {
                    detail = $"the walk from {origin} is blocked {span * i / steps:F1} m along by "
                             + string.Join(", ", wall);
                    return false;
                }
            }
            detail = $"floor {floor.Y:F2}, reach {crown - lowest:F2} m to spare, "
                     + $"walked {span:F1} m from arrival in {steps} samples, worst step {worstStep:F2} m";
            return true;
        }

        detail = $"floor {floor.Y:F2}, reach {crown - lowest:F2} m to spare, "
                 + "stands clear (route proven by PuffinLabSelfTest's stations)";
        return true;
    }

    private static List<string> OverlapNames(PhysicsDirectSpaceState3D space, Shape3D shape,
        Vector3 at, Godot.Collections.Array<Rid> exclude)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity, at),
            CollisionMask = 1,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = exclude,
        };
        var names = new List<string>();
        foreach (Godot.Collections.Dictionary h in space.IntersectShape(query, maxResults: 8))
            names.Add(h.TryGetValue("collider", out Variant c) && c.As<Node>() is { } n
                ? n.GetPath().ToString()
                : "<unnamed>");
        return names;
    }

    // --- BUBBLE-2: no bubble is buried in the level -------------------------------------------

    /// <summary>BUBBLE-2. Radius of the point probe that answers "is this exact point inside solid
    /// geometry". Deliberately tiny — 2 cm is the same fit margin
    /// <see cref="ReachFitMarginM"/> uses, and it is there only so a point resting exactly ON a
    /// surface is not called containment by float noise. <b>Containment of a POINT is the whole
    /// discriminator</b>: it is what separates a bubble sunk into a tower from a bubble parked
    /// 0.45 m from a golden cube, which is legal and which BUBBLE-1 shipped two of.</summary>
    private const float EmbedPointProbeM = 0.02f;

    /// <summary>BUBBLE-2. How far from a bubble's centre the search for a clear body position
    /// goes. The pop is <see cref="Area3D.BodyEntered"/> against a
    /// <see cref="Bubble.Bubble.ColliderRadiusM"/> sphere, so the reachable set is exactly the
    /// capsule placements whose surface reaches that sphere — nothing past
    /// <c>capsule radius + 0.35 m</c> can touch it at any distance, and that sum is 0.495 m for
    /// the shipped avatar (measured capsule radius 0.145 m). 0.60 m is comfortably past it, so
    /// the horizontal search cannot be the thing that runs out first; the reach test in
    /// <see cref="CapsuleReachesSphere"/> is what bounds it, and it bounds it exactly.</summary>
    private const float EmbedBodySearchM = 0.60f;

    /// <summary>BUBBLE-2. Directions sampled around a bubble when looking for a body position, and
    /// again around its own collider shell when reporting how buried an offender is. Sixteen
    /// headings is a 22.5° step — 0.23 m of arc at the far edge of the search, under the shipped
    /// capsule's own diameter, so no gap a body could fit through can sit between two consecutive
    /// samples. Coarse enough that the whole level costs a few thousand queries: a healthy bubble
    /// exits on its first placement, and only a buried one pays for the full sweep.</summary>
    private const int EmbedHeadings = 16;

    /// <summary>
    /// <b>BUBBLE-2: not one of the level's bubbles is inside the level.</b>
    ///
    /// <para><b>Why this exists.</b> Talon, off the 2026-09-04 playtest: <i>"one single bubble is
    /// out of access to the player ... it is being clipped into one of the towers ... I could not
    /// reach the bubble therefore I could not successfully conclude that all the bubbles were
    /// reachable."</i> The defect was one bubble; the hole was that nothing in the suite could
    /// have told him. <see cref="CheckIndoorReach"/> measures the 25 <i>indoor</i> bubbles, and it
    /// caught two real defects doing it — but 75 outdoor bubbles were measured by nobody, and
    /// "collect all the bubbles" is this level's only objective, so a bubble inside a wall makes
    /// that objective a lie. This check measures <b>all of them</b>.</para>
    ///
    /// <para><b>The three tests, and why the second one is the one that matters.</b></para>
    /// <list type="number">
    /// <item><b>Contact.</b> A sphere query at the bubble's authored centre with the bubble's own
    /// <see cref="Bubble.Bubble.ColliderRadiusM"/> — what, if anything, is inside the volume the
    /// player pops. This is the candidate finder and it is deliberately not an assertion: plenty
    /// of legal bubbles graze a floor, a rock or a crate.</item>
    /// <item><b>Containment — THE DISCRIMINATOR.</b> A <see cref="EmbedPointProbeM"/> probe at the
    /// centre. A bubble resting near a surface has a free centre no matter how close it is; a
    /// bubble clipped into a tower does not. That is a difference of kind, not of degree, so it is
    /// the thing asserted rather than a proximity threshold somebody would have to tune.</item>
    /// <item><b>Enclosure.</b> A bubble can have a free centre and still be unreachable — a shell
    /// around it, or a slot too narrow for a body. So the placements that could pop it are
    /// enumerated: the avatar's own capsule, at <see cref="EmbedHeadings"/> headings, four
    /// distances out to <see cref="EmbedBodySearchM"/> and five heights, keeping only those whose
    /// surface actually reaches the bubble's collider sphere (the exact pop condition), and at
    /// least one must be clear of the world.</item>
    /// </list>
    ///
    /// <para><b>What this check does NOT cover, stated so nobody reads more into a green than is
    /// there.</b> It proves a bubble is not inside geometry and that a body could occupy a spot
    /// that pops it. <b>It does not prove a route to that spot exists.</b> A bubble in open air
    /// over a chasm passes this and could still be uncollectable. Full route reachability needs a
    /// driven bot, and <c>ScriptedGotoIntentSource</c> walks to one point and latches there, so a
    /// multi-leg route is not something this harness can drive today — BUBBLE-1 recorded that gap
    /// and it is still open. Nor is gravity modelled here: the placements are free capsules, on
    /// purpose, because half this level's bubbles are reached by a jump and requiring a floor
    /// underneath one would fail the level's own design.</para>
    ///
    /// <para><b>Three controls, and each fires a different branch</b> — an all-clear from a query
    /// that is not reaching the world looks exactly like an all-clear from a level that is fine.
    /// A golden cube's own centre must come back <i>embedded</i> and name that cube (test 2); a
    /// point 0.45 m to the side of the same cube must come back <i>clear</i>, which is the
    /// discriminator proving itself on the exact geometry BUBBLE-1 shipped; and a sealed shell
    /// staged for the purpose must come back <i>enclosed</i> with a free centre (test 3).</para>
    /// </summary>
    private void CheckBubbleEmbedding()
    {
        AvatarProportions p = _stray.Proportions;
        float radius = Mathf.Max(0.01f, p.CapsuleRadiusM - ReachFitMarginM);
        float height = Mathf.Max(radius * 2f, p.CapsuleHeightM - ReachFitMarginM * 2f);
        var body = new CapsuleShape3D { Radius = radius, Height = height };
        var probe = new SphereShape3D { Radius = EmbedPointProbeM };
        var contact = new SphereShape3D { Radius = Bubble.Bubble.ColliderRadiusM };
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

        // EVERY staged avatar, not just the stray. This check sweeps the OUTDOOR bubbles too, and
        // the boundary probe parks five capsules around the level — one of them inside the secret
        // bubble's collider. A body of ours standing next to a bubble is not the level.
        var exclude = new Godot.Collections.Array<Rid>();
        CollectAvatarRids(this, exclude);

        GD.Print($"[bubbletest-selftest]   embed: point probe r {EmbedPointProbeM:F3}, bubble "
                 + $"collider r {Bubble.Bubble.ColliderRadiusM:F2}, body capsule r {radius:F3} "
                 + $"h {height:F3}, search {EmbedBodySearchM:F2} m, {exclude.Count} avatars excluded");

        // --- CONTROLS FIRST. Nothing below is believable until all three fire. -----------------
        Node3D? cube = _world!.GetNodeOrNull<Node3D>($"{GoldenCubeParent}/{GoldenCubePrefix}Hub_0");
        if (cube is null)
        {
            Fail($"{GoldenCubeParent}/{GoldenCubePrefix}Hub_0 is not in the world, so the "
                 + "embedding check has no control to fire and every verdict below is unproven.");
        }
        else
        {
            Vector3 c = cube.GlobalPosition;
            bool inCube = BubbleIsFree(space, body, probe, contact, exclude, c, out string d1);
            GD.Print($"[bubbletest-selftest]   embed CONTROL cube centre        -> "
                     + $"{(inCube ? "FREE (BAD)" : "caught")}: {d1}");
            Check(!inCube,
                $"the embedding control at the centre of {cube.Name} ({c}) came back free. The "
                + "point probe is not reaching the world, so every bubble it passed is unproven.");

            // THE DISCRIMINATOR, proving itself on the exact geometry BUBBLE-1 shipped: a bubble
            // 0.45 m from a golden cube is legal and must NOT be flagged. If this ever fires, the
            // check has become a proximity test and it will start deleting legal placements.
            Vector3 beside = c + new Vector3(0.45f, 0f, 0f);
            bool free = BubbleIsFree(space, body, probe, contact, exclude, beside, out string d2);
            GD.Print($"[bubbletest-selftest]   embed CONTROL 0.45 m from cube   -> "
                     + $"{(free ? "free" : "FLAGGED (BAD)")}: {d2}");
            Check(free,
                $"the embedding control 0.45 m from {cube.Name} ({beside}) was flagged: {d2}. "
                + "Resting near a surface is legal — this check has turned into a proximity test "
                + "and would now condemn placements the level is entitled to make.");
        }

        // The enclosure branch needs a shell and no shipped section has one on purpose, so
        // StageEmbedControlShell built one off the map before the settle. It is freed the instant
        // it has fired.
        if (_embedShell is null)
        {
            Fail("the embedding check's sealed-shell control was never staged, so the enclosure "
                 + "half of it is unproven and a bubble walled in behind geometry would pass.");
        }
        else
        {
            Vector3 shellAt = _embedShell.GlobalPosition;
            bool escaped = BubbleIsFree(space, body, probe, contact, exclude, shellAt, out string d3);
            GD.Print($"[bubbletest-selftest]   embed CONTROL sealed shell       -> "
                     + $"{(escaped ? "FREE (BAD)" : "caught")}: {d3}");
            Check(!escaped,
                $"the embedding control inside a sealed shell at {shellAt} came back free: {d3}. "
                + "The enclosure half of this check is not working, so a bubble walled in behind "
                + "geometry with its centre in open air would pass.");
            _embedShell.QueueFree();
            _embedShell = null;
        }

        // --- THE LEVEL. Every bubble, printed, in id order. ------------------------------------
        var all = new List<Bubble.Bubble>();
        CollectBubbles(_world, all);
        all.Sort((a, b) => a.Id != b.Id
            ? a.Id.CompareTo(b.Id)
            : string.CompareOrdinal(a.GetPath().ToString(), b.GetPath().ToString()));

        int buried = 0;
        foreach (Bubble.Bubble b in all)
        {
            // The AUTHORED centre, not the live one: the bob has been running since _Ready and a
            // bubble is up to BubbleOscillation.MaxOffsetM off its rest point at any instant. What
            // is being judged is where the level put it, so the verdict is reproducible.
            Vector3 at = b.GetParent() is Node3D parent
                ? parent.GlobalTransform * b.AuthoredPosition
                : b.GlobalPosition;
            bool ok = BubbleIsFree(space, body, probe, contact, exclude, at, out string detail);
            if (!ok) buried++;
            GD.Print($"[bubbletest-selftest]   embed id {b.Id,3} {b.Name,-18} "
                     + $"({at.X,8:F3},{at.Y,8:F3},{at.Z,8:F3}) {(ok ? "ok " : "BAD")} {detail}"
                     + (ok ? "" : $"  |  {b.GetPath()}"));
        }

        GD.Print($"[bubbletest-selftest]   embed: {all.Count} bubbles measured, {buried} buried "
                 + "(3 controls fired first)");
        Check(buried == 0,
            $"{buried} of the level's {all.Count} bubbles are inside or enclosed by collision "
            + "geometry. The lines above name each one, its id and its node path. A bubble the "
            + "player's body cannot occupy a position to touch makes 'collect all the bubbles' "
            + "unwinnable — LEVEL-BIBLE anti_unwinnable_guarantee.");
        Check(all.Count == (_world.GetNodeOrNull(BubbleTestWorld.PuffinLabNodeName) is null
                ? BubbleTestLayout.BubbleTarget
                : BubbleTestLayout.BubbleTargetWithLab),
            $"the embedding check swept {all.Count} bubbles, which is not the level's total. It "
            + "is looking in the wrong place, so its greens mean nothing.");
    }

    /// <summary>BUBBLE-2. Six 1.0 x 1.0 x 0.06 m plates around a 0.9 m void, 400 m under the map
    /// where nothing else in this test looks. <b>The centre of it is FREE</b> — that is what makes
    /// it a control for the enclosure test and not a second copy of the containment one — and no
    /// avatar capsule can be within reach of that centre without meeting a plate.</summary>
    private void StageEmbedControlShell()
    {
        var shell = new Node3D { Name = "EmbedControlShell" };
        AddChild(shell);
        shell.GlobalPosition = new Vector3(0f, -400f, 0f);
        for (int axis = 0; axis < 3; axis++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            var plate = new StaticBody3D();
            plate.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D
                {
                    Size = axis == 0 ? new Vector3(0.06f, 1.0f, 1.0f)
                         : axis == 1 ? new Vector3(1.0f, 0.06f, 1.0f)
                         : new Vector3(1.0f, 1.0f, 0.06f),
                },
            });
            shell.AddChild(plate);
            plate.Position = axis == 0 ? new Vector3(sign * 0.48f, 0f, 0f)
                           : axis == 1 ? new Vector3(0f, sign * 0.48f, 0f)
                           : new Vector3(0f, 0f, sign * 0.48f);
        }
        _embedShell = shell;
    }

    /// <summary>Every staged <see cref="SandboxAvatar"/>'s body, so the probe never mistakes one
    /// of the test's own capsules for the level.</summary>
    private static void CollectAvatarRids(Node node, Godot.Collections.Array<Rid> into)
    {
        if (node is SandboxAvatar a)
            into.Add(a.GetRid());
        foreach (Node child in node.GetChildren())
            CollectAvatarRids(child, into);
    }

    /// <summary>The three tests of <see cref="CheckBubbleEmbedding"/>, in order, with the first
    /// failure named and the contact set always reported — a legal bubble touching a rock is worth
    /// seeing in the log even though it is not a defect.</summary>
    private static bool BubbleIsFree(PhysicsDirectSpaceState3D space, CapsuleShape3D body,
        SphereShape3D probe, SphereShape3D contact, Godot.Collections.Array<Rid> exclude,
        Vector3 at, out string detail)
    {
        // 1. What is inside the volume the player pops. Candidate finder, never an assertion.
        List<string> touching = OverlapNames(space, contact, at, exclude);
        string contactNote = touching.Count == 0
            ? "clear of geometry"
            : $"touches {string.Join(", ", touching)}";

        // 2. THE DISCRIMINATOR. Is the centre itself inside a solid?
        List<string> inside = OverlapNames(space, probe, at, exclude);
        if (inside.Count > 0)
        {
            detail = $"EMBEDDED — its centre is inside {string.Join(", ", inside)}; "
                     + $"{BuriedShellPercent(space, probe, exclude, at)}% of its collider shell is "
                     + "buried too";
            return false;
        }

        // 3. Can the player's body be anywhere that pops it. Free capsules, no gravity — see the
        // method doc for why that is deliberate and what it therefore does not claim.
        float[] heights = { 0f, -0.35f, 0.35f, -0.70f, 0.70f };
        float[] distances = { 0f, 0.25f, 0.45f, EmbedBodySearchM };
        foreach (float dy in heights)
        foreach (float d in distances)
        for (int h = 0; h < EmbedHeadings; h++)
        {
            float ang = Mathf.Tau * h / EmbedHeadings;
            Vector3 c = at + new Vector3(Mathf.Cos(ang) * d, dy, Mathf.Sin(ang) * d);
            if (!CapsuleReachesSphere(c, body.Radius, body.Height, at,
                    Bubble.Bubble.ColliderRadiusM))
                continue;
            if (OverlapNames(space, body, c, exclude).Count > 0)
                continue;
            detail = touching.Count == 0
                ? $"{contactNote}; a body at {d:F2} m out, {dy:+0.00;-0.00;0.00} m up pops it"
                : $"{contactNote} but its centre is free; a body at {d:F2} m out, "
                  + $"{dy:+0.00;-0.00;0.00} m up pops it";
            return true;
            // The first clear placement is the answer; there is nothing to gain from finding a
            // second, and the offenders are the ones that pay for the full sweep.
        }

        detail = $"ENCLOSED — its centre is free but no body position within {EmbedBodySearchM:F2} m "
                 + $"can reach it; {contactNote}; "
                 + $"{BuriedShellPercent(space, probe, exclude, at)}% of its collider shell is buried";
        return false;
    }

    /// <summary>How much of a bubble's own collider shell sits inside solid geometry, as a
    /// percentage of <see cref="EmbedHeadings"/> headings across three elevations. Reported on
    /// offenders only — it is the number that says "half in a wall" rather than "grazing a
    /// floor", and it is diagnosis rather than the verdict, which test 2 already made.</summary>
    private static int BuriedShellPercent(PhysicsDirectSpaceState3D space, SphereShape3D probe,
        Godot.Collections.Array<Rid> exclude, Vector3 at)
    {
        int hit = 0, n = 0;
        float[] elevations = { -0.6f, 0f, 0.6f };
        foreach (float e in elevations)
        for (int h = 0; h < EmbedHeadings; h++)
        {
            float ang = Mathf.Tau * h / EmbedHeadings;
            float ring = Mathf.Cos(e);
            var dir = new Vector3(Mathf.Cos(ang) * ring, Mathf.Sin(e), Mathf.Sin(ang) * ring);
            n++;
            if (OverlapNames(space, probe, at + dir * Bubble.Bubble.ColliderRadiusM, exclude).Count > 0)
                hit++;
        }
        return n == 0 ? 0 : hit * 100 / n;
    }

    /// <summary>Does a vertical capsule at <paramref name="centre"/> reach a sphere — the exact
    /// pop condition, since a bubble is an <see cref="Area3D"/> that fires on body overlap.
    /// Distance from the sphere's centre to the capsule's axis SEGMENT, not to its centre, so a
    /// body standing beside a bubble at knee height counts and one standing beside it two metres
    /// below does not.</summary>
    private static bool CapsuleReachesSphere(Vector3 centre, float capsuleRadius,
        float capsuleHeight, Vector3 sphere, float sphereRadius)
    {
        float half = Mathf.Max(0f, capsuleHeight * 0.5f - capsuleRadius);
        Vector3 a = centre + new Vector3(0f, half, 0f);
        Vector3 ab = new(0f, -2f * half, 0f);
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp((sphere - a).Dot(ab) / len2, 0f, 1f);
        return (a + ab * t).DistanceTo(sphere) < capsuleRadius + sphereRadius;
    }

    /// <summary>
    /// <b>FRAME-1. The drawing on the Deep Room's wall is where the contract says, it is the size
    /// the room and the avatar say, and its material can still carry alpha.</b>
    ///
    /// <para><c>TvRoom.tscn</c> has to write the literals — a <c>.tscn</c> cannot reference a C#
    /// constant, and the level's law is that every part of a level is authored in its scene file.
    /// This check is the other half of that bargain: it reads the LIVE nodes back and compares
    /// them against <see cref="BubbleTestLayout.SecretPictureCentreY"/> and
    /// <see cref="BubbleTestLayout.SecretPictureMountM"/>, both of which are derived from
    /// <see cref="AvatarProportions.PlayerCrownM"/> and the room's own height. Re-measure the
    /// avatar and this goes red, which is exactly the loud failure the jump-arc constants one file
    /// over were re-derived to get after four defects were lost to a constant that had been copied
    /// rather than derived.</para>
    ///
    /// <para><b>It also asserts the alpha material</b>, because "a PNG with transparency" was the
    /// request and the shipped drawing's own torn edge and clear surround are 24% of the image. An
    /// opaque material would render that surround as a solid rectangle, and that failure is
    /// invisible in every headless test — it needs a render, or this assertion.</para>
    ///
    /// <para><b>It does NOT assert that the image file is present.</b> The guard's whole purpose
    /// is that a build without it comes up clean, and a check that demanded the file would be red
    /// on any tree where the art had been swapped out.</para></summary>
    private void CheckSecretPicture()
    {
        string path = $"{BubbleTestLayout.Section.TvRoom}/{BubbleTestLayout.SecretPicturePath}";
        var picture = _world!.GetNodeOrNull<Node3D>(path);
        if (picture is null)
        {
            Fail($"{path} is missing — the room behind the sunken television has nothing on its "
                 + "wall, which is the 'it's nothing special' state FRAME-1 fixed.");
            return;
        }

        float authored = picture.Position.Y;
        float derived = BubbleTestLayout.SecretPictureCentreY;
        GD.Print($"[bubbletest-selftest]   secret picture: centre y {authored:F3} m authored, "
                 + $"{derived:F3} m derived; mount box "
                 + $"{BubbleTestLayout.SecretPictureMountM.X:F3} x "
                 + $"{BubbleTestLayout.SecretPictureMountM.Y:F3} m");
        Check(Mathf.Abs(authored - derived) < 1e-3f,
            $"{path} hangs at local y {authored:F3} m but the derived centre is {derived:F3} m "
            + "(BubbleTestLayout.SecretPictureCentreY). Move the constant or move the scene, but "
            + "they may not disagree.");

        var canvas = picture.GetNodeOrNull<MeshInstance3D>(
            BubbleTestLayout.SecretPictureCanvasName);
        if (canvas is null)
        {
            Fail($"{path} has no '{BubbleTestLayout.SecretPictureCanvasName}' — the mount and its "
                 + "lamp are there but a supplied drawing would have nothing to land on.");
            return;
        }

        // The authored sheet must fit its own mount box. A .tscn holds the fitted size as a
        // literal (SetUpSecretPicture recomputes it at load from whatever image is on disk), so
        // this is what catches a hand-edit that would hang paper through the floor or the ceiling.
        if (canvas.Mesh is QuadMesh quad)
        {
            Vector2 mount = BubbleTestLayout.SecretPictureMountM;
            Check(quad.Size.X <= mount.X + 1e-3f && quad.Size.Y <= mount.Y + 1e-3f,
                $"{path}/{BubbleTestLayout.SecretPictureCanvasName} is authored at "
                + $"{quad.Size.X:F3} x {quad.Size.Y:F3} m, outside the {mount.X:F3} x "
                + $"{mount.Y:F3} m mount box the room and the avatar allow. A sheet that big "
                + "reaches the floor or the ceiling and stops reading as a sheet on a wall.");
        }
        else
        {
            Fail($"{path}/{BubbleTestLayout.SecretPictureCanvasName} is not a QuadMesh — "
                 + "SetUpSecretPicture refits the drawing by writing that mesh's Size, and "
                 + "without it a swapped image of another aspect would be stretched.");
        }

        var mat = canvas.MaterialOverride as StandardMaterial3D;
        Check(mat is not null
              && mat.Transparency == BaseMaterial3D.TransparencyEnum.Alpha,
            $"{path}/{BubbleTestLayout.SecretPictureCanvasName} does not carry a "
            + "StandardMaterial3D in TRANSPARENCY_ALPHA. Talon asked for \"a PNG with "
            + "transparency\" and the drawing that arrived is 24% clear pixels; an opaque "
            + "material would render its torn edge as a solid rectangle.");

        // The drawing is lit by exactly one light of its own, and it does not cast shadows
        // (ART-BIBLE §7 amendment item 3, closed 2026-08-07). A shadow-casting light added here
        // later is the single most expensive thing that could be done to this room.
        var lamp = picture.GetNodeOrNull<SpotLight3D>("PictureLight");
        Check(lamp is not null && !lamp.ShadowEnabled,
            $"{path}/PictureLight is missing or casts shadows. A drawing nobody can see is not a "
            + "reward, and a shadow-casting light is the one rendering cost this project has "
            + "already closed a decision against.");
    }

    private void CheckLakeBounds()
    {
        var surface = _world.GetNodeOrNull<MeshInstance3D>("GreenHills/Lake/Surface");
        if (surface is null)
        {
            Fail("GreenHills has no Lake/Surface mesh — the lake the water contract is bounded "
                 + "against is not in the scene.");
            return;
        }

        Aabb w = surface.GlobalTransform * surface.GetAabb();
        Vector3 lo = w.Position, hi = w.Position + w.Size;
        float renderedRadius = Mathf.Max(w.Size.X, w.Size.Z) * 0.5f;
        var centre = new Vector3(Water.WaterGeometry.BubbleTestLakeCentreX, 0f,
                                 Water.WaterGeometry.BubbleTestLakeCentreZ);
        GD.Print($"[bubbletest-selftest]   lake surface AABB {lo} .. {hi} "
                 + $"(rendered radius {renderedRadius:F3} m about {centre}, "
                 + $"swimmable radius {Water.WaterGeometry.BubbleTestLakeRadiusM:F3} m, "
                 + $"surface y {w.Position.Y:F3} vs WaterY {Water.WaterGeometry.WaterY:F3})");

        // The mesh is a fan about the section origin, so its AABB centre IS the lake centre.
        Vector3 renderedCentre = w.Position + w.Size * 0.5f;
        Check(Mathf.Abs(renderedCentre.X - centre.X) <= 0.25f
              && Mathf.Abs(renderedCentre.Z - centre.Z) <= 0.25f,
            $"the rendered lake is centred at {renderedCentre}, but WaterGeometry puts the "
            + $"swimmable disc at {centre}.");
        Check(Mathf.IsEqualApprox(w.Position.Y, Water.WaterGeometry.WaterY),
            $"the rendered lake surface sits at y={w.Position.Y}, not WaterGeometry.WaterY "
            + $"({Water.WaterGeometry.WaterY}).");

        // Covering, and not by much: every rendered triangle must be swimmable (or a player can
        // stand in visible water and not be in it), and the disc must not sprawl past the bank.
        Check(Water.WaterGeometry.BubbleTestLakeRadiusM >= renderedRadius,
            $"the swimmable disc is {Water.WaterGeometry.BubbleTestLakeRadiusM} m but the lake "
            + $"renders out to {renderedRadius:F3} m — there is visible water you cannot swim in.");
        Check(Water.WaterGeometry.BubbleTestLakeRadiusM <= renderedRadius + 0.5f,
            $"the swimmable disc is {Water.WaterGeometry.BubbleTestLakeRadiusM} m against a "
            + $"rendered {renderedRadius:F3} m — more than 0.5 m of swimmable nothing outside "
            + "the water.");

        Water.WaterGeometry.LakeFootprint lake =
            Water.WaterGeometry.LakeForWorld(BubbleTestLayout.WorldId);
        float swimY = Water.WaterGeometry.SwimLineY;

        Check(Water.WaterGeometry.DepthAt(centre with { Y = swimY }, lake) > 0f,
            "the middle of the lake does not read as water.");
        Check(Water.WaterGeometry.DepthAt(
                  centre with { X = centre.X - renderedRadius - 5f, Y = swimY }, lake) <= 0f,
            "ground 5 m west of the lake's own edge still reads as water.");

        // Talon's case, verbatim in coordinates: past the western end of the level (GreenFootprint
        // stops at x = -140) and well below the water plane. This is where the half-plane used to
        // answer "swimming".
        var offTheWestEnd = new Vector3(-160f, -20f, 0f);
        Check(Water.WaterGeometry.DepthAt(offTheWestEnd, lake) <= 0f,
            $"a body at {offTheWestEnd} — off the west end of the level and 20 m down — still "
            + "reads as water. This is the exact defect Talon reported.");
        Check(Water.WaterGeometry.ResolveAt(
                  Water.WaterState.Swimming, offTheWestEnd, lake) == Water.WaterState.Dry,
            $"a swimmer carried to {offTheWestEnd} stays Swimming instead of falling.");
    }

    // --- 7a. Registration -------------------------------------------------------------------

    /// <summary>The row in <c>Gameplay.BuildWorld</c> exists and returns the right type.
    /// <b>Not a formality:</b> whatever that switch does with an unregistered id — throw, or fall
    /// back to this very world — the id being registered by name is what this level's launch
    /// depends on, and the first symptom of losing it is a playtester saying the level "didn't
    /// load".</summary>
    private void CheckRegistration()
    {
        IGameWorld built = Gameplay.BuildWorld(BubbleTestLayout.WorldId);
        Check(built is BubbleTestWorld,
            $"BuildWorld(\"{BubbleTestLayout.WorldId}\") returned {built.GetType().Name}, not "
            + "BubbleTestWorld — the id is unregistered.");
        if (built is Node n) n.QueueFree();
    }

    // --- 7d. The bake check -----------------------------------------------------------------

    /// <summary>Acceptance criterion 4's first half: the world file is a seam, not a level. Counts
    /// only nodes authored IN <c>BubbleTest.tscn</c> (types the instanced sections contribute are
    /// not this file's, so the walk is deliberately non-recursive here).</summary>
    private void CheckRootFileHasNoGeometry()
    {
        var packed = GD.Load<PackedScene>(ScenePaths.BubbleTest);
        if (packed is null)
        {
            Fail($"could not load {ScenePaths.BubbleTest}");
            return;
        }
        Counts own = CountPacked(packed, recurseIntoInstances: false);
        Check(own.Meshes == 0,
            $"BubbleTest.tscn authors {own.Meshes} MeshInstance3D of its own — geometry belongs to "
            + "the section files (program D3), and a mesh here is a mesh no section packet owns.");
        GD.Print($"[bubbletest-selftest]   root file (own nodes only): meshes={own.Meshes} "
                 + $"shapes={own.Shapes} bodies={own.Bodies}");
    }

    /// <summary>Packed-vs-live node counts, per section. See the class doc for why this is the
    /// load-bearing check. Raw counts are printed for every section because BT-13 reads them and
    /// because "equal" is only meaningful next to the numbers that were equal.</summary>
    private void CheckBake()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            string path = BubbleTestLayout.ScenePathOf(s);
            var packed = GD.Load<PackedScene>(path);
            if (packed is null)
            {
                Fail($"section scene missing: {path}");
                continue;
            }

            Counts onDisk = CountPacked(packed, recurseIntoInstances: true);

            Node live = packed.Instantiate();
            AddChild(live); // _Ready runs synchronously, bottom-up, inside AddChild
            Counts inTree = CountLive(live);
            live.QueueFree();

            bool equal = onDisk.Equals(inTree);
            GD.Print($"[bubbletest-selftest]   {s,-14} packed[mesh={onDisk.Meshes} "
                     + $"shape={onDisk.Shapes} body={onDisk.Bodies}] live[mesh={inTree.Meshes} "
                     + $"shape={inTree.Shapes} body={inTree.Bodies}] {(equal ? "OK" : "MISMATCH")}");
            Check(equal,
                $"{s}: the live scene has {inTree} where the file has {onDisk} — something is "
                + "building geometry at runtime, which is exactly what 'no procedural generation, "
                + "anywhere' forbids. Bake it with AuthoredSceneDumper (program D2) instead.");

            if (!AuditColliders(packed, s.ToString(), out string why))
                Fail(why);
        }
    }

    // --- 7e. The collider audit and its positive control ------------------------------------

    /// <summary>Every authored <c>MeshInstance3D</c> outside <see cref="NoColliderGroup"/> must
    /// have a <c>CollisionShape3D</c> under the same parent, or under a <c>StaticBody3D</c>
    /// sibling. Program §4 rule 9.
    ///
    /// <para><b>Stated limitation, so nobody reads more into a green run than is there:</b> the
    /// rule is per-PARENT, not per-mesh — several meshes under one parent are covered by one
    /// shape under that parent. That is correct for the common authored case (a visual split
    /// across materials over a single collision hull) and it means this cannot catch "one of five
    /// rocks in this node has no hull". It catches the failure that actually shipped, which was a
    /// whole level with no colliders at all.</para></summary>
    private bool AuditColliders(PackedScene scene, string label, out string why)
    {
        SceneState state = scene.GetState();
        var meshParents = new List<(string Parent, string Path)>();
        var shapeParents = new HashSet<string>();
        var bodyPaths = new HashSet<string>();

        for (int i = 0; i < state.GetNodeCount(); i++)
        {
            string type = state.GetNodeType(i);
            if (type.Length == 0) continue; // an instance reference or an inherited override
            string parent = state.GetNodePath(i, true).ToString();
            string self = state.GetNodePath(i).ToString();
            switch (type)
            {
                case MeshType:
                    if (!state.GetNodeGroups(i).Contains(NoColliderGroup))
                        meshParents.Add((parent, self));
                    break;
                case ShapeType:
                    shapeParents.Add(parent);
                    break;
                case BodyType:
                    bodyPaths.Add(self);
                    break;
            }
        }

        foreach ((string parent, string self) in meshParents)
        {
            if (shapeParents.Contains(parent)) continue;
            // ... or under a StaticBody3D that is a child of the same parent.
            bool viaBody = shapeParents.Any(sp =>
                bodyPaths.Contains(sp) && ParentOf(sp) == parent);
            if (viaBody) continue;

            why = $"{label}: MeshInstance3D '{self}' has no collision — no CollisionShape3D under "
                  + $"'{parent}' and none under a StaticBody3D sibling. A player can walk through "
                  + $"it. If that is intended, put the mesh in the '{NoColliderGroup}' group.";
            return false;
        }
        why = "";
        return true;
    }

    /// <summary><b>The control the absence check owes.</b> A throwaway scene with one mesh and no
    /// collider anywhere; <see cref="AuditColliders"/> must reject it. Without this, an audit that
    /// silently found zero meshes (a changed type name, a walk that never recursed) would report
    /// PASS on every section forever.</summary>
    private void CheckColliderControl()
    {
        var root = new Node3D { Name = "ControlRoot" };
        var mesh = new MeshInstance3D { Name = "UncollidedSlab", Mesh = new BoxMesh() };
        root.AddChild(mesh);
        mesh.Owner = root;

        var packed = new PackedScene();
        Error err = packed.Pack(root);
        root.QueueFree();
        if (err != Error.Ok)
        {
            Fail($"collider-audit control could not be packed ({err}) — the audit is unproven.");
            return;
        }

        bool passed = AuditColliders(packed, "control", out string why);
        Check(!passed,
            "COLLIDER AUDIT CONTROL DID NOT FAIL: a scene containing one MeshInstance3D and no "
            + "collider at all was accepted. Every 'colliders OK' result in this run is worthless "
            + "until this is fixed.");
        if (!passed)
        {
            GD.Print("[bubbletest-selftest]   collider-audit positive control FAILED AS EXPECTED "
                     + $"— {why}");
        }
    }

    // --- 7b/7c. Anchors and spawns -----------------------------------------------------------

    private void CheckAnchors()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            var node = _world.GetNodeOrNull<Node3D>(s.ToString());
            if (node is null)
            {
                Fail($"the world scene has no '{s}' section instance.");
                continue;
            }
            Vector3 want = BubbleTestLayout.AnchorOf(s);
            Check(node.GlobalPosition.DistanceTo(want) <= PosTolerance,
                $"{s} sits at {node.GlobalPosition} but program §4 anchors it at {want}. Five "
                + "geometry packets author in this section's LOCAL space against that anchor, so "
                + "a moved anchor silently moves their work.");
            Check(node.GlobalBasis.IsEqualApprox(Basis.Identity),
                $"{s} has a non-identity rotation. Program §4: sections sit at their anchor with "
                + "identity rotation — .tscn basis rows transpose easily and only a render shows "
                + "it (.claude/rules/godot-scenes.md).");
        }
    }

    /// <summary><b>Sections stay inside their own footprint.</b> The anchors above prove a section
    /// is in the right place; this proves it is the right SIZE, which is the half that decides
    /// whether five packets working in parallel can collide. Two sections authoring the same
    /// ground means z-fighting slabs, a player inside two <see cref="SectionVolume"/>s at once,
    /// and a merge where the last packet in wins by accident.
    ///
    /// <para>Measured on <c>MeshInstance3D</c> world AABBs — the geometry a player can actually
    /// see and stand on — with a 0.5 m slack so a bevel or a decal edge is not a red. Labels,
    /// markers and volumes are not meshes and are deliberately not measured.</para></summary>
    private void CheckFootprints()
    {
        const float slack = 0.5f;
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            var node = _world.GetNodeOrNull<Node3D>(s.ToString());
            if (node is null) continue;
            Aabb f = BubbleTestLayout.FootprintOf(s);
            float minX = f.Position.X - slack, maxX = f.Position.X + f.Size.X + slack;
            float minZ = f.Position.Z - slack, maxZ = f.Position.Z + f.Size.Z + slack;
            float ceiling = BubbleTestLayout.AnchorOf(s).Y + BubbleTestLayout.MaxHeightOf(s) + slack;
            // The hub's four signposts are §4 rule 4's connector markers and BT-5's stated
            // exception; its seating still obeys HubMaxHeight. See BubbleTestLayout.HubSignpostHeight.
            if (s == BubbleTestLayout.Section.Hub)
                ceiling = BubbleTestLayout.AnchorOf(s).Y + BubbleTestLayout.HubSignpostHeight + slack;

            foreach (MeshInstance3D mesh in MeshesUnder(node))
            {
                Aabb w = mesh.GlobalTransform * mesh.GetAabb();
                Vector3 lo = w.Position, hi = w.Position + w.Size;
                if (lo.X >= minX && hi.X <= maxX && lo.Z >= minZ && hi.Z <= maxZ && hi.Y <= ceiling)
                    continue;
                // §4 rule 4 puts the four connector corridors in the HUB file, and they
                // necessarily reach past HubFootprint to touch each section's near edge. They
                // sit in ground no section claims, so this does not weaken the one-owner rule.
                if (s == BubbleTestLayout.Section.Hub && hi.Y <= ceiling
                    && InAnyConnector(lo, hi, slack))
                    continue;
                // ROOFTOP-1: the hundredth bubble hangs on the sky deck's diving board, 200 m up.
                // It is authored in Hub.tscn and it is IN THE HUB'S PLAN COLUMN — the deck is
                // directly over the origin — so the only bound it breaks is the ceiling, and it
                // breaks that by 198 m. It stayed in Hub.tscn because bubble ids are the index of
                // an ordinal node-path sort: re-parenting it into TvRoom.tscn, where the board it
                // hangs over is authored, would have renumbered the level to move one bubble.
                // Named and narrow rather than a raised HubMaxHeight, which would have licensed a
                // 200 m hub. See BubbleTestLayout.SkyBubblePos and CheckRooftopRoute, which
                // measures where this thing actually is instead of exempting it and looking away.
                if (s == BubbleTestLayout.Section.Hub && IsSkyBubble(mesh)) continue;
                Fail($"{s}: mesh '{mesh.Name}' spans {lo} .. {hi}, outside its footprint "
                     + $"(x {minX}..{maxX}, z {minZ}..{maxZ}, ceiling y {ceiling}). Sections must "
                     + "stay in their own ground — program §4, and D3's one-owner rule depends "
                     + "on it.");
                break; // one report per section is enough to act on
            }
        }
    }

    /// <summary>True when <paramref name="mesh"/> is part of
    /// <see cref="BubbleTestLayout.SkyBubbleNodeName"/> — the one authored node in the hub's file
    /// that is deliberately not over the hub's ground. Walks the parent chain by NAME rather than
    /// testing a position, so a bubble that drifted back down to the plaza would stop being
    /// exempt only if it were also renamed, and a second sky bubble would have to be named for it
    /// to be forgiven.</summary>
    private static bool IsSkyBubble(Node mesh)
    {
        for (Node? n = mesh; n is not null; n = n.GetParent())
            if (n.Name.ToString() == BubbleTestLayout.SkyBubbleNodeName) return true;
        return false;
    }

    /// <summary>True when a hub mesh lies inside one of the four connector corridors
    /// (<see cref="BubbleTestLayout.HubConnectors"/>). Plan-only: the ceiling is checked
    /// separately by the caller.</summary>
    private static bool InAnyConnector(Vector3 lo, Vector3 hi, float slack)
    {
        foreach (Aabb c in BubbleTestLayout.HubConnectors)
        {
            float cMinX = c.Position.X - slack, cMaxX = c.Position.X + c.Size.X + slack;
            float cMinZ = c.Position.Z - slack, cMaxZ = c.Position.Z + c.Size.Z + slack;
            if (lo.X >= cMinX && hi.X <= cMaxX && lo.Z >= cMinZ && hi.Z <= cMaxZ)
                return true;
        }
        return false;
    }

    private static IEnumerable<MeshInstance3D> MeshesUnder(Node node)
    {
        if (node is MeshInstance3D m) yield return m;
        foreach (Node child in node.GetChildren())
            foreach (MeshInstance3D nested in MeshesUnder(child))
                yield return nested;
    }

    private void CheckSpawns()
    {
        Godot.Collections.Array<Vector3> spawns = _world.SpawnPoints;
        Check(spawns.Count == BubbleTestLayout.SpawnCount,
            $"the world offers {spawns.Count} spawn point(s); Protocol.MaxPlayers is "
            + $"{BubbleTestLayout.SpawnCount} and Gameplay deals spawns [i % count], so a short "
            + "ring puts two players inside each other.");
        if (spawns.Count != BubbleTestLayout.SpawnCount) return;

        for (int i = 0; i < spawns.Count; i++)
        {
            // SpawnPointOf, not SpawnPosition: W7-1 backed the ring's CENTRE 18 m south for
            // Talon's note 1, and the two are deliberately separate facts (see
            // BubbleTestLayout.SpawnRingCentre).
            Vector3 want = BubbleTestLayout.HubAnchor + BubbleTestLayout.SpawnPointOf(i);
            Check(spawns[i].DistanceTo(want) <= PosTolerance,
                $"Spawn{i} is at {spawns[i]}, not {want} — off the {BubbleTestLayout.SpawnRingRadius} m "
                + $"ring centred at {BubbleTestLayout.SpawnRingCentre} that program §4 rule 5 and "
                + "W7-1 specify.");
        }

        // Facing. Program §4 rule 5: all six look at the pedestal, so the first frame of a
        // joining player's session is the thing the level is about. Marker3D rotation is not
        // carried by SpawnPoints (it is a Vector3 array), so it is read off the markers.
        var hub = _world.GetNodeOrNull<Node3D>(BubbleTestLayout.Section.Hub.ToString());
        if (hub is null) return;
        for (int i = 0; i < BubbleTestLayout.SpawnCount; i++)
        {
            var marker = hub.GetNodeOrNull<Marker3D>($"Spawn{i}");
            if (marker is null) { Fail($"the hub has no Spawn{i} marker."); continue; }
            Vector3 toPedestal = BubbleTestLayout.PedestalPos - marker.GlobalPosition;
            toPedestal.Y = 0f;
            if (toPedestal.LengthSquared() < 0.0001f) continue;
            // Godot's -Z is forward.
            Vector3 forward = -marker.GlobalBasis.Z;
            forward.Y = 0f;
            float dot = forward.Normalized().Dot(toPedestal.Normalized());
            Check(dot > 0.999f,
                $"Spawn{i} faces {forward.Normalized()}, which is {Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(dot, -1f, 1f))):F1}° "
                + $"off the pedestal at {BubbleTestLayout.PedestalPos}.");
        }
    }

    // --- 7g. The sky -------------------------------------------------------------------------

    private void CheckAtmosphere()
    {
        var atmosphere = _world.GetNodeOrNull<OutdoorAtmosphere>("OutdoorAtmosphere");
        Check(atmosphere != null,
            "no OutdoorAtmosphere under the world — this level would have no sun, no moon and no "
            + "WorldEnvironment, so the day/night cycle would run invisibly and every capture "
            + "would look identical.");
        if (atmosphere is null) return;
        Check(atmosphere.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") != null,
            "OutdoorAtmosphere built no WorldEnvironment.");
        Check(atmosphere.GetNodeOrNull<DirectionalLight3D>("Sun") != null,
            "OutdoorAtmosphere built no Sun.");

        // Single-writer: exactly one WorldEnvironment in the whole world, and it is the
        // atmosphere's. A second one authored into a section file would fight it every frame and
        // the winner would depend on tree order.
        int envs = CountOfType<WorldEnvironment>(_world);
        Check(envs == 1,
            $"{envs} WorldEnvironment node(s) under the world; there must be exactly one and it "
            + "must be OutdoorAtmosphere's (.claude/rules/single-writer.md).");
    }

    // --- 7f + criterion 6. Out of bounds -----------------------------------------------------

    private void CheckRespawnConfig()
    {
        var svc = _world.GetNodeOrNull<RespawnService>(RespawnService.NodeName);
        if (svc is null)
        {
            Fail("the world created no RespawnService — walking off the edge would drop a player "
                 + "into the void forever, and program §4 rule 3 says the walk back is the cost.");
            return;
        }
        _respawn = svc;
        Check(Mathf.IsEqualApprox(svc.OffMapRadiusM, BubbleTestLayout.OffMapRadiusM),
            $"OffMapRadiusM is {svc.OffMapRadiusM}, not {BubbleTestLayout.OffMapRadiusM}.");
        Check(Mathf.IsEqualApprox(svc.VoidKillY, BubbleTestLayout.VoidKillY),
            $"VoidKillY is {svc.VoidKillY}, not {BubbleTestLayout.VoidKillY}.");
        Vector3 back = svc.RespawnPoint?.Invoke() ?? Vector3.Inf;
        Check(back.DistanceTo(BubbleTestLayout.RespawnPoint) <= PosTolerance,
            $"the respawn point is {back}, not the hub centre {BubbleTestLayout.RespawnPoint}.");
        Check(svc.VoidKillY < BubbleTestLayout.TvRoomAnchor.Y,
            $"VoidKillY ({svc.VoidKillY}) is not below the TV room floor "
            + $"({BubbleTestLayout.TvRoomAnchor.Y}) — a player who falls through that floor would "
            + "never be caught.");
        // WATER-3: the SHIPPED drowning clock, checked before StartBoundaryProbe shortens it.
        Check(Mathf.IsEqualApprox(svc.DrownSeconds, Water.WaterGeometry.DrownAfterSec),
            $"DrownSeconds is {svc.DrownSeconds}, not the contract's "
            + $"{Water.WaterGeometry.DrownAfterSec} (Talon: \"about three seconds\").");
    }

    // --- GUARD-1. The props this world places from code stand on the ground ---------------------

    /// <summary>
    /// <b>Every prop <see cref="BubbleTestWorld"/> positions from code has its base on the surface
    /// directly beneath it.</b>
    ///
    /// <para><b>Why this check exists.</b> The reset lever floated <b>0.9 m in the air from BT-8
    /// until LEVER-1</b> — its transform was copied from the <c>PedestalMount</c> marker on the
    /// plinth TOP and the Y came along — and it did that through a green suite every single time,
    /// because nothing in this repo measured a prop's Y. LEVER-1 fixed the number and left the
    /// class open; this is the guard.</para>
    ///
    /// <para><b>"Headless cannot see a prop's Y" is false, and it is the reason this is nine lines
    /// rather than a headed capture serialised on one GPU.</b> Three documents in this repo said
    /// it; BT-13d §6.3 disproved it with a positive control, its own <c>--headless</c> transcript
    /// printing <c>BubbleCounterDisplay at (0, 0.9, -8)</c>. Rendering is dummy under
    /// <c>--headless</c>; the scene tree, its transforms and the physics space are exact. Nobody
    /// had written the assertion.</para>
    ///
    /// <para><b>Against the ground, never against a literal.</b> The assertion is
    /// <c>prop.GlobalPosition.Y == (the surface a downward ray finds under it)</c>, within
    /// <see cref="GroundTolerance"/>. A hardcoded 0.0 would be a second copy of the world that
    /// goes stale the first time a section's floor moves — and it would be wrong on arrival for
    /// the counter display, whose ground is the plinth top at 0.9 rather than the hub floor. The
    /// ray is what makes one rule cover both cases: each prop is checked against whatever is
    /// actually under it.</para>
    ///
    /// <para><b>What is covered, and what is not.</b> Covered: the four props whose position is
    /// <i>decided in C#</i> — the counter display, the reset lever, and the two entrance
    /// televisions. That is the whole defect class, because it is the only class the editor
    /// cannot show: a prop authored into a section file is visible at its real height the moment
    /// anyone opens the file, which is the entire point of the level's no-procedural-generation
    /// law, and <see cref="CheckBake"/> and <see cref="CheckFootprints"/> already pin those files.
    /// <b>Not covered, deliberately:</b> the section files' own authored props (the benches,
    /// cairns, signposts, stones — the environment role's, hundreds of them, and asserting their
    /// Y here would be a second copy of six section designs); <c>TvRoom/RoomTv</c>, which is
    /// authored in its scene file and so falls on the visible side of the same line; and the
    /// bubbles, which float on purpose.</para>
    /// </summary>
    private void CheckPropHeights()
    {
        CheckStandsOnGround(BubbleCounterDisplay.NodeName, "the hub plinth top");
        CheckStandsOnGround(BubbleResetLever.NodeName, "the hub floor beside the plinth");

        // Every entrance television, read off the table the world built them from rather than a
        // list retyped here. LEVEL-4 added three of these; a guard that named its props one by one
        // would have covered BT-10's two and silently ignored the new ones, which is exactly the
        // shape of the defect it exists to catch.
        foreach (BubbleTestLayout.TvRoute route in BubbleTestLayout.TvRoutes)
            CheckStandsOnGround(route.EntranceName, "the ground plate under it");

        CheckGoldenCubes();
    }

    /// <summary>
    /// <b>The golden cubes rest on whatever is under them</b> (Talon's note 12, 2026-08-29).
    ///
    /// <para><b>Why these need their own method rather than four more rows above.</b> Every prop
    /// that guard already covers has its origin at its base, so "origin == ground" is the whole
    /// assertion. A <c>Crate.tscn</c>'s origin is its CENTRE — <c>PropManager.AdoptAuthoredProps</c>
    /// and <c>Playground.tscn</c> both place these by centre — so the same assertion would report
    /// every correctly-placed cube as floating by half its own height. The base is therefore
    /// <b>derived from the cube's own collider</b> rather than from a 0.22 typed here:
    /// <c>Crate.tscn</c> owns its size, and a cube re-sized in the editor stays measured
    /// correctly.</para>
    ///
    /// <para><b>The count is asserted too.</b> Cubes across five sections and five rooms are the
    /// spread note 12 asks for ("in and around the map"); a scene that silently lost fifteen of
    /// them would pass every height check it still had.</para>
    /// </summary>
    private void CheckGoldenCubes()
    {
        var props = _world.GetNodeOrNull<Node3D>(GoldenCubeParent);
        if (props is null)
        {
            Fail($"the world has no '{GoldenCubeParent}' node — Talon's note 12 asks for golden "
                 + "cubes in and around the map, and there are none to pick up or throw.");
            return;
        }

        int seen = 0;
        foreach (Node child in props.GetChildren())
        {
            if (child is not Node3D cube || !cube.Name.ToString().StartsWith(GoldenCubePrefix))
                continue;
            seen++;
            CheckRestsOnGround(cube);
        }
        GD.Print($"[bubbletest-selftest]   golden cubes: {seen} "
                 + $"(expected {BubbleTestLayout.GoldenCubes})");
        Check(seen == BubbleTestLayout.GoldenCubes,
            $"the world carries {seen} golden cube(s); the layout contract says "
            + $"{BubbleTestLayout.GoldenCubes}.");
    }

    /// <summary>One cube, one ray, and a base read off its own collision shape rather than
    /// assumed. Shares <see cref="CheckStandsOnGround"/>'s exclusion discipline: without excluding
    /// the cube's own body the ray lands on the cube and reports a gap of zero for every cube at
    /// every height — a detector that can only say "pass".</summary>
    private void CheckRestsOnGround(Node3D cube)
    {
        var exclude = new Godot.Collections.Array<Rid>();
        CollectBodyRids(cube, exclude);

        float halfHeight = 0f;
        foreach (CollisionShape3D shape in ShapesUnder(cube))
        {
            if (shape.Shape is BoxShape3D box)
                halfHeight = Mathf.Max(halfHeight, box.Size.Y * 0.5f);
        }
        if (halfHeight <= 0f)
        {
            Fail($"{cube.Name} has no BoxShape3D under it — its base cannot be derived, and a "
                 + "carryable with no collider is not carryable either.");
            return;
        }

        Vector3 at = cube.GlobalPosition;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            at + new Vector3(0f, GroundProbeUpM, 0f),
            at - new Vector3(0f, GroundProbeDownM, 0f));
        query.Exclude = exclude;

        Godot.Collections.Dictionary hit = _world.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            Fail($"{cube.Name} is at {at} with NOTHING solid within {GroundProbeDownM} m below it "
                 + "— a golden cube over the void is not one a player can pick up.");
            return;
        }

        float groundY = ((Vector3)hit["position"]).Y;
        float gap = at.Y - halfHeight - groundY;
        GD.Print($"[bubbletest-selftest]   {cube.Name,-21} base y={at.Y - halfHeight,7:F3}  "
                 + $"ground y={groundY,7:F3}  gap={gap,7:F3} m");
        Check(Mathf.Abs(gap) <= GoldenCubeTolerance,
            $"{cube.Name} is {gap:F3} m {(gap > 0 ? "ABOVE" : "BELOW")} the surface under it: its "
            + $"base is at y={at.Y - halfHeight:F3} and the ground is at y={groundY:F3}, over the "
            + $"{GoldenCubeTolerance} m tolerance.");
    }

    private static IEnumerable<CollisionShape3D> ShapesUnder(Node node)
    {
        if (node is CollisionShape3D s) yield return s;
        foreach (Node child in node.GetChildren())
            foreach (CollisionShape3D nested in ShapesUnder(child))
                yield return nested;
    }

    /// <summary>One prop, one ray. <paramref name="what"/> names the surface the prop is supposed
    /// to be standing on, so a failure says which floor moved rather than only that a number is
    /// wrong.
    ///
    /// <para>The prop's own bodies are excluded from the ray. Without that the ray lands on the
    /// prop's plinth or cabinet and reports a gap of zero for every prop at every height — a
    /// detector that can only say "pass", which is the failure mode this repo has already paid
    /// four wrong conclusions for. Same lesson as <c>StyleRockWalkSelfTest.GroundYExcluding</c>'s
    /// and <c>CampCaptureLab.TerrainY</c>'s.</para></summary>
    private void CheckStandsOnGround(string nodeName, string what)
    {
        var prop = _world.GetNodeOrNull<Node3D>(nodeName);
        if (prop is null)
        {
            Fail($"the world has no '{nodeName}' — its height cannot be guarded, and a prop that "
                 + "is missing entirely is the louder bug of the two.");
            return;
        }

        var exclude = new Godot.Collections.Array<Rid>();
        CollectBodyRids(prop, exclude);

        Vector3 at = prop.GlobalPosition;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            at + new Vector3(0f, GroundProbeUpM, 0f),
            at - new Vector3(0f, GroundProbeDownM, 0f));
        query.Exclude = exclude;

        Godot.Collections.Dictionary hit = _world.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            Fail($"{nodeName} stands at {at} with NOTHING solid within {GroundProbeDownM} m below "
                 + $"it — it should be on {what}. Either the prop is in the air or the ground it "
                 + "was placed against is gone.");
            return;
        }

        float groundY = ((Vector3)hit["position"]).Y;
        float gap = at.Y - groundY;
        GD.Print($"[bubbletest-selftest]   {nodeName,-21} base y={at.Y,7:F3}  ground y="
                 + $"{groundY,7:F3}  gap={gap,7:F3} m  ({what}, excluded {exclude.Count} own "
                 + "bodies)");
        Check(Mathf.Abs(gap) <= GroundTolerance,
            $"{nodeName} is {gap:F3} m {(gap > 0 ? "ABOVE" : "BELOW")} {what}: its base is at "
            + $"y={at.Y:F3} and the surface under it is at y={groundY:F3}, over the "
            + $"{GroundTolerance} m tolerance. This is the BT-8 defect: a prop whose transform was "
            + "taken from something at a different height.");
    }

    /// <summary>Every <see cref="PhysicsBody3D"/> at or under <paramref name="node"/>, as RIDs for
    /// a ray-query exclusion list.</summary>
    private static void CollectBodyRids(Node node, Godot.Collections.Array<Rid> into)
    {
        if (node is PhysicsBody3D body)
            into.Add(body.GetRid());
        foreach (Node child in node.GetChildren())
            CollectBodyRids(child, into);
    }

    /// <summary>Acceptance criterion 6, and the only check here that runs the real machine end to
    /// end: a body standing at (0, 0, 260) — 50 m past the off-map radius, on the cyan axis — is
    /// killed by the server's own boundary scan and put back at the hub. Everything upstream of
    /// this is configuration; this is the configuration doing something.
    ///
    /// <para><b>WATER-3 adds the second half of the same proof</b>, and the two have to run
    /// together to mean anything: a body dropped in the middle of the green lake dies
    /// <see cref="RespawnCause.Drowned"/> while the body off the map dies
    /// <see cref="RespawnCause.OffTheEdge"/> — one scan, one run, two causes. Before the lake was
    /// bounded these were the same place as far as <c>DepthAt</c> was concerned, so a run where
    /// both come back <c>Drowned</c> is exactly the regression to catch.</para></summary>
    private void StartBoundaryProbe()
    {
        if (_respawn is null) { Finish(); return; }

        _respawn.DeathBeatSeconds = TestBeatSeconds;
        // Same reasoning as TestBeatSeconds: the property under test is that the clock fires and
        // names the right cause, not that it is 3.0 s — the duration and its boundary are proved
        // exhaustively in RespawnDrowningTests, and 3 s of dead wait per suite run buys nothing.
        _respawn.DrownSeconds = TestDrownSeconds;
        _respawn.Died += (peer, cause) => _deaths.Add((peer, cause));

        // The live path sets this in Gameplay.BuildWorld (CheckRegistration above already ran it);
        // stated here so the probe is self-sufficient rather than depending on check order.
        // EGG-2: the SET, matching what Gameplay.BuildWorld installs. Setting ActiveLake alone
        // would have left the blue moat out of the probe's world while the played level has it.
        Water.WaterGeometry.ActiveWaters =
            Water.WaterGeometry.WatersForWorld(BubbleTestLayout.WorldId);

        // The world's own avatar lambda walks GetParent().GetNodeOrNull("Players"), so the probe
        // goes in a real "Players" node next to the world — the lambda is under test too.
        var players = new Node3D { Name = "Players" };
        AddChild(players);
        _stray = new SandboxAvatar { Name = "77" };
        players.AddChild(_stray);
        _stray.GlobalPosition = new Vector3(0f, 1f, 260f);

        // Peer 78, in the middle of the lake at the swim line. It sinks to the bed (−3.85), which
        // is deeper still, so it is submerged for every scan either way.
        _drowner = new SandboxAvatar { Name = "78" };
        players.AddChild(_drowner);
        _drowner.GlobalPosition = new Vector3(Water.WaterGeometry.BubbleTestLakeCentreX,
                                              Water.WaterGeometry.SwimLineY,
                                              Water.WaterGeometry.BubbleTestLakeCentreZ);

        // EGG-2, Talon's addendum §7. Peer 80, in the blue tower's moat — 0.5 m over its authored
        // bed at (70, -2.5, 6), world space, deliberately clear of Moat/CoreFooting (x 76..82) so
        // it is standing in water rather than inside the column's plinth. THE POINT OF THIS BODY
        // is that "the same standard drowns-on-touch water used elsewhere in the level" is the
        // same MECHANISM and not a look-alike: it must come back Drowned, from the same scan that
        // drowns peer 78 in green's lake 165 m away, with no second trigger volume anywhere.
        _moatDrowner = new SandboxAvatar { Name = "80" };
        players.AddChild(_moatDrowner);
        _moatDrowner.GlobalPosition = new Vector3(70f, -2.5f, 6f);

        // EGG-2, Talon's addendum §3. Peer 81, standing inside the secret bubble's collider. The
        // Area3D fires on the first physics frame of overlap, so the pop is staged here and read
        // in FinishBoundaryProbe with the census taken either side of it.
        if (Bubble.BubbleCounter.Instance is { } counter)
        {
            _bubblesBeforeSecret = counter.BubbleCount;
            _countBeforeSecret = counter.Count;
        }
        _secretFinder = new SandboxAvatar { Name = "81" };
        players.AddChild(_secretFinder);
        _secretFinder.GlobalPosition = BubbleTestLayout.SecretBubblePos;

        // BOTH EGG-2 BODIES ARE RETIRED THE INSTANT THEIR EVENT IS RECORDED, and that is a
        // correctness requirement rather than tidiness -- it is the SAME shoving the peer-79 note
        // above describes, measured here the same way. RespawnService.FinishRespawn puts every
        // drowned body on the single RespawnPoint (0, 1, 0); with peers 78, 80 and 81 all dying in
        // one 0.4 s window, three capsules resolved onto one point and launched each other to
        // y = 39.7 m, failing the SHIPPED check that peer 78 comes back to the hub. Their causes
        // are already in _deaths by the time Died fires, so freeing here loses nothing: what each
        // body is for is the cause it reports, not where it stands afterwards -- and "a drowned
        // body comes back at the hub" is peer 78's assertion and stays peer 78's.
        _respawn.Died += (peer, _) =>
        {
            if (peer is not (80 or 81)) return;
            SandboxAvatar? spent = peer == 80 ? _moatDrowner : _secretFinder;
            if (IsInstanceValid(spent)) spent.QueueFree();
        };

        // W7-4: measure what the rendered body does between the kill and the respawn. Added to
        // the probe that already stages both deaths rather than as a suite of its own, because the
        // quantity only exists while a real body is mid-beat and this is the only place in the
        // repo where one is.
        _strayRise = new VisualRiseProbe { Name = "RiseProbe77", Target = _stray };
        AddChild(_strayRise);
        _drownerRise = new VisualRiseProbe { Name = "RiseProbe78", Target = _drowner };
        AddChild(_drownerRise);

        // MRF-B / F8. Peer 79: a THIRD body, off the map beside 77 so the same boundary scan kills
        // it with the same cause, but configured as a real NetRole.PredictedOwner. That is the
        // whole point of it — the two bodies above are Offline, so SandboxAvatar.OwnerTick never
        // runs on them, their render offset is a permanent zero, and the probes above measure the
        // death pose with nothing to compose it against. This one carries a live correction offset
        // THROUGH the beat, which is the configuration W7-4 fixed and the one nothing in this repo
        // exercised until now (2026-08-30 master review, F8).
        //
        // ConfigureAsNetworked AFTER the position is set: it seeds _spawnPosition and
        // MoveState.AtSpawn from Position, so configuring first would spawn-anchor the body at the
        // origin and the boundary scan would have nothing off the map to find.
        //
        // ONE EXPECTED ARTEFACT, printed at the end so it does not read as a defect: ~30 stderr
        // "RPC 'SubmitInput' on yourself is not allowed" lines. OwnerTick calls SendInputs every
        // physics tick and there is no server in this process to send to; the error IS the client
        // half doing its job with nobody listening. Silencing it means editing SandboxAvatar, which
        // this packet does not own, and gating the send would delete the very per-tick behaviour
        // under test.
        _predictedOwner = new SandboxAvatar { Name = "79" };
        players.AddChild(_predictedOwner);
        _predictedOwner.GlobalPosition = new Vector3(6f, 1f, 260f);
        _predictedOwner.ConfigureAsNetworked(isOwner: true, source: null);
        _ownerCompose = new RenderOffsetCompositionProbe
        {
            Name = "ComposeProbe79", Target = _predictedOwner, Seed = RenderOffsetSeed,
        };
        AddChild(_ownerCompose);

        // AND IT IS RETIRED AS SOON AS ITS BEAT IS MEASURED, which is not tidiness.
        // RespawnService.FinishRespawn moves a body with ServerTeleportTo, whose switch has
        // branches for ServerSim and Offline and NONE for PredictedOwner — correctly, because in a
        // real session the client's predicted body is moved by the server's snapshot epoch, not by
        // its own call. With no server copy in this process, peer 79 simply stays off the map and
        // the 10 Hz scan finds it again, and again. Left alone that is merely noisy; but the day
        // somebody gives ServerTeleportTo a PredictedOwner branch, peer 79 would start respawning
        // onto the same point as peer 77 and the two capsules would shove each other off the
        // check below. (Measured, not imagined: with peer 79 left Offline — master's shape — the
        // pair stacked and 77 finished 61 m in the air.) Freeing the body once its window has
        // passed makes this probe's lifetime a stated fact rather than a coincidence.
        _respawn.Died += (peer, _) =>
        {
            if (peer != 79 || _ownerRetiring) return;
            _ownerRetiring = true;
            GetTree().CreateTimer(ComposeWindowSeconds).Timeout += RetirePredictedOwner;
        };

        // The scan is 10 Hz, the drowning clock is TestDrownSeconds and the beat is
        // TestBeatSeconds; one wait covers all three with margin.
        GetTree().CreateTimer(TestDrownSeconds + TestBeatSeconds + 0.6).Timeout
            += FinishBoundaryProbe;
    }

    /// <summary>MRF-B / F8. Takes peer 79 out of the world once its composition window has passed;
    /// the probe reads <c>IsInstanceValid</c> and simply stops sampling. See the retirement note in
    /// <see cref="StartBoundaryProbe"/>.</summary>
    private void RetirePredictedOwner()
    {
        if (IsInstanceValid(_predictedOwner))
            _predictedOwner.QueueFree();
    }

    private void FinishBoundaryProbe()
    {
        RespawnCause? offMap = CauseFor(77), drowned = CauseFor(78);

        Check(offMap is not null,
            "a body parked 50 m outside OffMapRadiusM was never killed — the boundary scan is not "
            + "running, or the world's avatar enumerator found nobody.");
        Check(offMap is null or RespawnCause.OffTheEdge,
            $"the off-map body died of {offMap}, not OffTheEdge.");

        Check(drowned is not null,
            $"a body held under the middle of the green lake for {TestDrownSeconds} s was never "
            + "killed — the drowning clock is not running, or the lake does not read as water "
            + "where it is rendered.");
        Check(drowned is null or RespawnCause.Drowned,
            $"the submerged body died of {drowned}, not Drowned.");

        Check(_stray.GlobalPosition.DistanceTo(BubbleTestLayout.RespawnPoint) <= 1.5f,
            $"after the death beat the body is at {_stray.GlobalPosition}, not back at the hub "
            + $"{BubbleTestLayout.RespawnPoint}.");
        Check(_drowner.GlobalPosition.DistanceTo(BubbleTestLayout.RespawnPoint) <= 1.5f,
            $"after drowning, the body is at {_drowner.GlobalPosition}, not back at the hub "
            + $"{BubbleTestLayout.RespawnPoint}.");

        CheckMoatDrowns();
        CheckSecretBubble();
        // --- W7-4: the beat's rendered excursion, measured rather than described ---------------
        // A positive control first. "The body no longer rises" is an ABSENCE, and an absence is
        // worthless until the instrument can prove it would have seen a presence: a probe that
        // took no samples, or that is pointed at a body whose visual never moved at all, reports
        // the same 0.000 m as a fixed beat.
        Check(_drownerRise.Samples > 0 && _strayRise.Samples > 0,
            $"the rise probe never sampled (drowner={_drownerRise.Samples}, "
            + $"off-map={_strayRise.Samples}); every measurement below is vacuous.");
        Check(_drownerRise.PeakTurns > 0f,
            "the drowned body's visual never rotated at all over the whole beat — either the beat "
            + "did not play or the probe is pointed at the wrong node, and in both cases the "
            + "height bound below proves nothing.");

        Check(_drownerRise.PeakRiseM <= 0.001f,
            $"the drowned body's visual rose {_drownerRise.PeakRiseM:F3} m. A drowning must only "
            + "sink (W7-4, Talon note 6: \"into the sky, it looks like a tornado took them up\"); "
            + "the beat this replaced peaked at 4.200 m.");
        Check(_strayRise.PeakRiseM <= Sail.Game.Run.DeathBeatPose.MaxRiseM + 0.001f,
            $"the off-map body's visual rose {_strayRise.PeakRiseM:F3} m, past the "
            + $"{Sail.Game.Run.DeathBeatPose.MaxRiseM:F3} m flat-lie clearance.");
        Check(_drownerRise.PeakTurns <= Sail.Game.Run.DeathBeatPose.MaxRevolutions + 0.01f
              && _strayRise.PeakTurns <= Sail.Game.Run.DeathBeatPose.MaxRevolutions + 0.01f,
            $"a death beat spun {Mathf.Max(_drownerRise.PeakTurns, _strayRise.PeakTurns):F3} whole "
            + $"turns, past the {Sail.Game.Run.DeathBeatPose.MaxRevolutions:F2} bound — the beat "
            + "this replaced spent 4.500 and Talon called it a tornado.");

        // Cleared, not merely bounded. The respawn puts this body straight back into play; a
        // player who came back still lying on their back is a worse defect than the one W7-4 fixed.
        Vector3 strayPose = _stray.GetNodeOrNull<Node3D>("Visual")?.Position ?? Vector3.Zero;
        Vector3 drownPose = _drowner.GetNodeOrNull<Node3D>("Visual")?.Position ?? Vector3.Zero;
        Check(Mathf.Abs(strayPose.Y) <= 0.001f && Mathf.Abs(drownPose.Y) <= 0.001f,
            $"after the beat the visual is still offset (off-map {strayPose.Y:F3} m, drowned "
            + $"{drownPose.Y:F3} m); a respawned body must be posed by nothing.");

        // --- MRF-B / F8: the render offset and the death pose, live at the same time -----------
        // Everything above measures the beat with the OTHER contributor pinned at zero. This is
        // the pair, on peer 79's predicted-owner body.
        //
        // TWO POSITIVE CONTROLS FIRST, and neither is optional. The bound below is a WINDOW around
        // the seed, so it holds trivially on a body whose visual never moved and on a body whose
        // offset was never really contended — which is precisely how the probe above passed for a
        // whole wave while covering none of this.
        RespawnCause? predicted = CauseFor(79);
        Check(predicted is RespawnCause.OffTheEdge,
            $"the predicted-owner body died of {predicted}, not OffTheEdge — it is parked beside "
            + "the off-map body and must die the same way, or the composition below is measured "
            + "across the wrong beat.");
        Check(_ownerCompose.Samples > 0
              && _ownerCompose.PeakVisualErrorM >= RenderOffsetSeed.Y - 0.01f,
            $"the render offset never went live (samples={_ownerCompose.Samples}, peak "
            + $"VisualErrorM={_ownerCompose.PeakVisualErrorM:F3} m against a "
            + $"{RenderOffsetSeed.Y:F2} m seed) — every bound below would hold vacuously.");
        // THE CONTROL THAT MASTER'S CONFIGURATION FAILS. An Offline body never runs OwnerTick, so
        // nothing drains the injected offset and it reads back as exactly the seed forever — which
        // is why seeding the two bodies above would have proved nothing. Seeing the value come back
        // SMALLER is the evidence that OwnerTick ran, i.e. that the second writer in the W7-4
        // defect is genuinely competing with the beat for this transform. One 60 Hz tick of
        // CorrectionSmoothRate = 12 leaves exp(-0.2) = 0.819 of it; 0.95 is a floor with room for
        // a render frame that happened to land between two physics ticks.
        Check(_ownerCompose.MinErrorBeforeReplantM <= RenderOffsetSeed.Y * 0.95f,
            "the seeded correction offset was never drained (lowest reading before a re-plant: "
            + $"{_ownerCompose.MinErrorBeforeReplantM:F3} m against a {RenderOffsetSeed.Y:F2} m "
            + "seed, i.e. untouched). SandboxAvatar.OwnerTick is what drains it AND what writes "
            + "SetRenderOffset every physics tick, and it runs only for NetRole.PredictedOwner — "
            + "so this body is not one, the second writer is absent, and the composition bounds "
            + "below are measuring one live term against a dead one. That is the F8 defect.");
        Check(_ownerCompose.PeakTurns > 0f,
            "the predicted owner's visual never rotated — the beat did not play on that body, so "
            + "the composition below is a measurement of one live term and one dead one.");

        // THE COMPOSITION. AvatarVisual.ApplyRootPose is `_incapacityPos + _renderOffset +
        // _deathPos`, so with a 3 m offset held and an OffTheEdge beat the sampled local Y must
        // stay inside [seed, seed + LieFlatLiftM]. The two ways it can fail are the two ways the
        // pre-W7-4 code failed, and they land in different places: if the beat discards the offset
        // the Y collapses toward 0 and MinY fails; if the offset discards the beat the Y pins at
        // exactly the seed and the rotation control above is what catches it.
        Check(_ownerCompose.MinY >= RenderOffsetSeed.Y - 0.01f,
            $"with a {RenderOffsetSeed.Y:F2} m correction offset held on a predicted owner, the "
            + $"visual dropped to {_ownerCompose.MinY:F3} m during the death beat. The beat is "
            + "discarding the render offset — two writers on one transform, which is exactly the "
            + "W7-4 defect (AvatarVisual.ApplyRootPose composes; it does not overwrite).");
        Check(_ownerCompose.MaxY <= RenderOffsetSeed.Y + Sail.Game.Run.DeathBeatPose.MaxRiseM + 0.01f,
            $"the composed visual rose to {_ownerCompose.MaxY:F3} m, past the "
            + $"{RenderOffsetSeed.Y + Sail.Game.Run.DeathBeatPose.MaxRiseM:F3} m the seed plus the "
            + "beat's whole vertical budget allows — something is adding an excursion that is "
            + "neither the correction offset nor the beat.");

        GD.Print("[bubbletest-selftest]   render-offset composition (peer 79, PredictedOwner): "
                 + $"seed {RenderOffsetSeed.Y:F2} m, VisualErrorM peak "
                 + $"{_ownerCompose.PeakVisualErrorM:F3} m / drained to "
                 + $"{_ownerCompose.MinErrorBeforeReplantM:F3} m before re-plant, visual Y "
                 + $"{_ownerCompose.MinY:F3}..{_ownerCompose.MaxY:F3} m over "
                 + $"{_ownerCompose.Samples} samples, {_ownerCompose.PeakTurns:F3} turns, "
                 + $"cause={predicted}. (The stderr \"RPC 'SubmitInput' on yourself\" lines are "
                 + "this body and are expected — see StartBoundaryProbe.)");

        GD.Print($"[bubbletest-selftest]   death beat: drowned peak rise "
                 + $"{_drownerRise.PeakRiseM:F3} m / {_drownerRise.PeakTurns:F3} turns over "
                 + $"{_drownerRise.Samples} samples; off-map peak rise {_strayRise.PeakRiseM:F3} m "
                 + $"/ {_strayRise.PeakTurns:F3} turns over {_strayRise.Samples} samples");

        GD.Print($"[bubbletest-selftest]   boundary probe: deaths={_deaths.Count} "
                 + $"off-map(77)={offMap} body={_stray.GlobalPosition}; "
                 + $"submerged(78)={drowned} body={_drowner.GlobalPosition}");
        Finish();
    }

    /// <summary>The cause this peer died of, or null if it never died. Matched by peer rather than
    /// by list index: two probes die in the same run and the scan order is not a contract.</summary>
    /// <summary>
    /// <b>EGG-2 / addendum §7: the blue tower's floor drowns you now.</b>
    ///
    /// <para>Talon's ask was that a fall off the climb stop being free. The implementation is a
    /// second <c>WaterGeometry</c> footprint (<see cref="Water.WaterGeometry.BluePrecisionMoat"/>)
    /// plus a hole in <c>BluePrecision.tscn</c>'s ground plate — no new trigger, no second
    /// drowning authority — so what has to be proved is that the SHIPPED clock fires there. Peer
    /// 80 sits in the moat while peer 78 sits in green's lake 165 m away; one scan, one run, both
    /// <see cref="RespawnCause.Drowned"/>, and peer 77 off the map coming back
    /// <see cref="RespawnCause.OffTheEdge"/> in the same pass is the discriminator that stops
    /// "everything drowns" from reading as a pass.</para>
    ///
    /// <para><b>The absence half is asserted too</b>, and it is the half that would otherwise go
    /// unnoticed: the apron a player walks in on must NOT be water. A point on the west apron —
    /// the one the hub connector lands on — is checked dry against the same predicate, so a moat
    /// accidentally authored across the whole 100 m plate is a red test rather than a section
    /// nobody can enter.</para></summary>
    private void CheckMoatDrowns()
    {
        RespawnCause? moat = CauseFor(80);
        Check(moat is not null,
            $"a body held in the blue tower's moat at {new Vector3(70f, -2.5f, 6f)} for "
            + $"{TestDrownSeconds} s was never killed — the moat is not water as far as "
            + "DrowningClock is concerned, so falling off the climb is still free.");
        Check(moat is null or RespawnCause.Drowned,
            $"the body in the blue moat died of {moat}, not Drowned.");
        // NOT asserted here: where peer 80 ends up. It is freed at death (see the retirement note
        // in StartBoundaryProbe) precisely so it cannot share the single respawn point with peer
        // 78, whose return to the hub is the shipped assertion and stays the shipped assertion.

        // ABSENCE: the dry apron. (55, 1, 0) is on BluePrecision's west apron, where the hub
        // connector arrives; (95, 1, 0) is the moat's middle for contrast.
        var apron = new Vector3(55f, 1f, 0f);
        var inside = new Vector3(95f, -2.5f, 0f);
        bool apronWet = Water.WaterGeometry.InLakeRegion(apron);
        bool insideWet = Water.WaterGeometry.InLakeRegion(inside);
        GD.Print($"[bubbletest-selftest]   blue moat: cause(80)={moat} "
                 + $"apron{apron} inWater={apronWet} moatMiddle{inside} inWater={insideWet} "
                 + $"bed y={BubbleTestLayout.BlueMoatBedY:F2} "
                 + $"depthOnBed={Water.WaterGeometry.WaterY - BubbleTestLayout.BlueMoatBedY:F2} m "
                 + $"vs SubmergedDepthM {Water.WaterGeometry.SubmergedDepthM:F2} m");
        Check(!apronWet,
            $"the west apron at {apron} — where the hub connector arrives — reads as water. The "
            + "moat was meant to be a hole in the plate, not the plate.");
        Check(insideWet,
            $"the middle of the moat at {inside} does not read as water, so nothing in it drowns.");
    }

    /// <summary>
    /// <b>EGG-2 / addendum §3: the secret bubble pops, and the tally does not move.</b>
    ///
    /// <para>Acceptance criterion 5 is a before/after on <c>BubbleCounter</c>'s own numbers, taken
    /// either side of a REAL overlap — peer 81 is staged inside the collider and the shipped
    /// <c>Area3D</c> fires it, exactly as the television probes drive real triggers rather than
    /// calling the host. The count is unchanged by construction (<see cref="Bubble.SecretBubble"/>
    /// shares no base class with <see cref="Bubble.Bubble"/>, so the adoption pass cannot see it),
    /// and this is where that construction is measured instead of asserted in a comment.</para>
    /// </summary>
    private void CheckSecretBubble()
    {
        var secret = _world.GetNodeOrNull<Bubble.SecretBubble>(Bubble.SecretBubble.NodeName);
        if (secret is null)
        {
            Fail($"the world has no '{Bubble.SecretBubble.NodeName}' — the level's one secret is "
                 + "not in it.");
            return;
        }

        Bubble.BubbleCounter? counter = Bubble.BubbleCounter.Instance;
        int bubblesAfter = counter?.BubbleCount ?? -1;
        int countAfter = counter?.Count ?? -1;
        GD.Print($"[bubbletest-selftest]   secret bubble at {secret.GlobalPosition}: popped="
                 + $"{secret.Popped} pops={secret.Pops} revealing={counter?.Revealing} "
                 + $"({counter?.RevealRemainingSec:F1} s left, film energy "
                 + $"{counter?.CurrentEmissionEnergy:F2}); census {_bubblesBeforeSecret} -> "
                 + $"{bubblesAfter} adopted, tally {_countBeforeSecret} -> {countAfter}");

        Check(secret.Popped,
            $"a body staged inside the secret bubble's collider at {secret.GlobalPosition} did not "
            + "pop it — the Area3D never fired, or nothing subscribed it on the server.");
        Check(bubblesAfter == _bubblesBeforeSecret,
            $"the counter adopted {_bubblesBeforeSecret} bubbles before the secret was touched and "
            + $"{bubblesAfter} after. The secret must never enter the census.");
        // BUBBLE-1: the target is COMPOSED, not fixed. PuffinLab.tscn is optional (see
        // BubbleTestWorld.SetUpPuffinLab), so the honest total is BubbleTarget in a build without
        // it and BubbleTargetWithLab in one with it. A single fixed number here would either go
        // red on a legitimate lab-less build or, worse, pass while the level advertised a total
        // the player could not reach. Which case we are in is read off the TREE — the lab node
        // being present — rather than off ResourceLoader, because what the counter adopted is a
        // fact about the tree and nothing else.
        bool labPresent = _world.GetNodeOrNull(BubbleTestWorld.PuffinLabNodeName) is not null;
        int expected = labPresent
            ? BubbleTestLayout.BubbleTargetWithLab
            : BubbleTestLayout.BubbleTarget;
        GD.Print($"[bubbletest-selftest]   puffin lab {(labPresent ? "PRESENT" : "ABSENT")}: "
                 + $"expecting {expected} adopted bubbles "
                 + $"({BubbleTestLayout.BubbleTarget} + "
                 + $"{(labPresent ? BubbleTestLayout.PuffinLabBubbles : 0)} lab)");
        Check(bubblesAfter == expected,
            $"the counter adopted {bubblesAfter} bubbles against a target of {expected} with the "
            + $"lab {(labPresent ? "present" : "absent")} — either a bubble was added or dropped "
            + "without moving the split, or the secret bubble is being counted as a collectible.");
        Check(countAfter == _countBeforeSecret,
            $"the shared tally moved from {_countBeforeSecret} to {countAfter} when the secret was "
            + "popped. A secret that ticks the scoreboard reads as a bug, not a secret.");
        Check(counter is { Revealing: true },
            "the secret was popped and the reveal is not running — its stated effect did not "
            + "happen, which is indistinguishable from an easter egg nobody built.");
    }

    private RespawnCause? CauseFor(int peer)
    {
        foreach ((int p, RespawnCause c) in _deaths)
            if (p == peer) return c;
        return null;
    }

    // --- plumbing ----------------------------------------------------------------------------

    private readonly record struct Counts(int Meshes, int Shapes, int Bodies)
    {
        public static Counts operator +(Counts a, Counts b) =>
            new(a.Meshes + b.Meshes, a.Shapes + b.Shapes, a.Bodies + b.Bodies);

        public override string ToString() => $"mesh={Meshes} shape={Shapes} body={Bodies}";
    }

    /// <summary>Count by type in a scene's PACKED state — no instantiation, nothing runs.
    ///
    /// <para><b>Three entry shapes exist in a <see cref="SceneState"/> and all three matter:</b> a
    /// node authored here has a real type name; a node that is itself an instanced scene has an
    /// EMPTY type and a non-null <c>GetNodeInstance</c>, and we recurse into it (a section that
    /// references a baked <c>.tscn</c> in <c>assets/</c> is still authored — program §6 item 14
    /// asks for exactly that); a node with an empty type and no instance is a property override on
    /// a node inherited from an instance, already counted by the recursion, so it is skipped.
    /// Getting that last case wrong double-counts and turns every green run into a false
    /// mismatch.</para></summary>
    private static Counts CountPacked(PackedScene scene, bool recurseIntoInstances)
    {
        SceneState state = scene.GetState();
        var total = new Counts(0, 0, 0);
        for (int i = 0; i < state.GetNodeCount(); i++)
        {
            string type = state.GetNodeType(i);
            if (type.Length == 0)
            {
                if (recurseIntoInstances && state.GetNodeInstance(i) is { } nested)
                    total += CountPacked(nested, true);
                continue;
            }
            total += Single(type);
        }
        return total;
    }

    private static Counts CountLive(Node node)
    {
        // GetClass() is the NATIVE class name, which is what SceneState.GetNodeType returns for an
        // authored node — so the two sides of the bake check are measuring the same thing. `is`
        // pattern matching is deliberately NOT used: it would count a subclass as its base and the
        // packed side would not.
        var total = Single(node.GetClass());
        foreach (Node child in node.GetChildren())
            total += CountLive(child);
        return total;
    }

    private static Counts Single(string type) => type switch
    {
        MeshType => new Counts(1, 0, 0),
        ShapeType => new Counts(0, 1, 0),
        BodyType => new Counts(0, 0, 1),
        _ => new Counts(0, 0, 0),
    };

    private static int CountOfType<T>(Node node) where T : Node
    {
        int n = node is T ? 1 : 0;
        foreach (Node child in node.GetChildren())
            n += CountOfType<T>(child);
        return n;
    }

    private static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "." : path[..slash];
    }

    private void Check(bool ok, string failure)
    {
        if (!ok) _failures.Add(failure);
    }

    private void Fail(string failure) => _failures.Add(failure);

    private void Finish()
    {
        foreach (string f in _failures)
            GD.PushError($"[bubbletest-selftest] FAIL {f}");

        GD.Print(_failures.Count == 0
            ? "[bubbletest-selftest] PASS (registration, seam file, bake compliance, collider "
              + "audit + control, anchors, footprint containment, spawn ring + facing, "
              + "atmosphere, respawn config, lake bounds vs the rendered water, code-placed "
              + "props on the ground under them, off-map boundary + drowning)"
            : $"[bubbletest-selftest] FAIL ({_failures.Count})");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
