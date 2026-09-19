using Godot;

namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>
/// <b>The pose the authored clips propose for one rendered frame.</b> Read out of the rig after the
/// tree has evaluated, in the same units the procedural layer already speaks — so the modifiers
/// layer on top of a clip instead of arguing with it.
///
/// <para><b>The legs are an ANGLE, not a rotation, and that is the whole reason this struct
/// exists.</b> <c>LimbIk</c> is the sole writer of the knee and the elbow, and
/// <c>LimbIkTests.RigidGaitFootPositions_AreReproducedExactly</c> is what pins the ratified walk,
/// run, skid and <c>stride × cadence</c> identity in place (ANIM-M0 §4.2 item 5). If the clip wrote
/// <c>ThighL.Rotation</c> directly it would be a second writer on the node the solver owns, and that
/// test would be deleted rather than kept. So the clip <i>proposes</i> a hip angle, the solver
/// <i>disposes</i>: exactly the same <c>SolveLeg</c> call, with the clip's angle where the derived
/// gait's angle used to go.</para>
/// </summary>
public readonly record struct AvatarClipPose(
    /// <summary>Left hip pitch, radians, positive swings the thigh forward — the same sign
    /// convention <c>LocomotionProfile.LegAngleFor</c> produces.</summary>
    float LegAngleL,
    /// <summary>Right hip pitch, radians.</summary>
    float LegAngleR,
    /// <summary>Left shoulder pitch, radians, relative to the arm's authored rest.</summary>
    float ArmPitchL,
    /// <summary>Right shoulder pitch, radians.</summary>
    float ArmPitchR,
    /// <summary>Trunk yaw at the waist seam, radians. Composes with the runtime's waist PITCH on a
    /// different component — see <see cref="AvatarClipDirector"/>'s remarks.</summary>
    float WaistYaw,
    /// <summary>Head yaw, radians. The clip is the only writer of this channel; nothing in the
    /// procedural layer has ever written <c>Head.Rotation</c>.</summary>
    float HeadYaw);

/// <summary>
/// <b>The <c>AnimationTree</c>, built in code over the fourteen clips ANIM-M2 authored, fed only
/// from replicated state.</b>
///
/// <para><b>The contract, restated because it is the thing that must not drift.</b> Position,
/// velocity, gait/gear, facing, hold-state and action-state stay server-authoritative replicated
/// state. Animation is a pure function of that state plus local time. <b>No animation state is ever
/// replicated.</b> Every input to <see cref="Advance"/> is either something already on the wire or
/// something derived from it by a pure function every peer runs identically
/// (<see cref="AvatarActionStates.Derive"/>, <c>LocomotionProfile.GearFor</c>).</para>
///
/// <h3>The tree's shape, and the two places it deviates from the brief's sketch</h3>
/// <code>
///   root : AnimationNodeBlendTree
///     loco        AnimationNodeBlendSpace1D   Idle @0 · Walk @2.4207 · Run @9.1447   DISCRETE_CARRY
///     warp        AnimationNodeTimeScale      &lt;- loco          scale = speed ÷ nominal
///     act_*       AnimationNodeAnimation      Skid, Jump_Launch, Jump_Air, Jump_Land, Stagger, KnockOut
///     action      AnimationNodeTransition     7 inputs: warp + the six above; xfade from the table
///     act_seek    AnimationNodeTimeSeek       &lt;- action        the (now − entryTick) seek
///     hold_*      AnimationNodeAnimation      Hold_Empty, Hold_NetReady, Hold_NetSwing, Carry_*
///     hold        AnimationNodeTransition     5 inputs; xfade from the table
///     hold_seek   AnimationNodeTimeSeek       &lt;- hold          the swing's replicated-progress seek
///     arms        AnimationNodeBlend2         in=act_seek  in2=hold_seek   FILTERED to ArmL, ArmR
///     output      &lt;- arms
/// </code>
///
/// <para><b>Deviation 1 — <c>AnimationNodeTransition</c> where the brief said
/// <c>AnimationNodeStateMachine</c>.</b> Two reasons, both structural rather than taste.
/// <c>AnimationNodeStateMachinePlayback</c> has no seek, and the brief's own point 3 requires one —
/// a late joiner must land mid-clip, which means the tree must be tellable "you are 0.31 s into
/// this". <c>AnimationNodeTimeSeek</c> is the node that does that and it composes with a Transition.
/// And the crossfade table this packet was asked to formalise is keyed <i>per destination</i>
/// (0.15 s everywhere, 0 s into sudden states), which is exactly <c>xfade_time</c>'s shape; a state
/// machine would want one <c>AnimationNodeStateMachineTransition</c> resource per ORDERED PAIR — 42
/// of them for seven states — each free to carry a different answer to a question that has one.</para>
///
/// <para><b>Deviation 2 — the locomotion blend space is DISCRETE_CARRY, not interpolated, and that is
/// a measurement.</b> See <see cref="AnimationCrossfade.LocomotionBlendSec"/>: a weighted Walk↔Run
/// blend was measured off the shipped bytes at up to <b>99.5 mm</b> of planted-foot skate, against
/// 0.09 mm and 1.09 mm at the pure endpoints. Discrete-carry switches clip at the boundary while
/// carrying the playback position, so the phase is continuous and — because ANIM-M2's leg cap forces
/// both clips onto the identical 0.6859 m foot sweep — the pose is continuous too.</para>
///
/// <h3>What the tree does NOT own</h3>
/// <para>Everything in ANIM-M0 §4.2 stays procedural and stays in <c>AvatarVisual.Animate</c>: the
/// squash spring on <c>Body</c> only, lean-as-acceleration, the crouch absorb, <c>LimbIk</c> keeping
/// the knee and the elbow, the face channels, the sprout sway, the blink and the idle fidget, and
/// the <c>AnimationSuspended</c> early return with its two parks. The tree writes six node rotations
/// and the procedural layer reads them back out as <see cref="AvatarClipPose"/>; it never fights for
/// a transform.</para>
///
/// <para><b>The waist is two writers on two components, deliberately, and it is safe because the
/// asset says so.</b> Every authored trunk and head rotation in the library is a <i>yaw</i>, capped
/// at 3.25° / 2.68° by ANIM-M2's facet-slip budget; the runtime writes <c>_waist.Rotation.X</c>
/// (pitch) and does so with <c>with { X = … }</c>, preserving Y. Two components, no overlap. Do not
/// add an authored waist PITCH without reading ANIM-M2 §8 first: that channel already has a runtime
/// writer that exceeds the seam budget at full lean.</para>
///
/// <para><b>Manual callback mode, and it is what makes the ordering provable.</b> The tree is
/// advanced explicitly from <c>Animate</c> rather than on Godot's own idle/physics callback, so
/// "tree first, then modifiers" is a line of code instead of a hope about node ordering.</para>
/// </summary>
// A plain C# class rather than a GodotObject: it owns no engine lifetime of its own, it holds
// references to nodes the scene tree already owns, and a GodotObject subclass would have to be
// Free()d by hand on every avatar-key rebuild or leak one per rebuild for the life of the process.
// AvatarVisual drops its reference in RestoreBuiltPose before the model is torn down, which is the
// whole of the lifetime contract.
public sealed class AvatarClipDirector
{
    private const string LocoNode = "loco";
    private const string WarpNode = "warp";
    private const string ActionNode = "action";
    private const string ActionSeekNode = "act_seek";
    private const string HoldNode = "hold";
    private const string HoldSeekNode = "hold_seek";
    private const string ArmsNode = "arms";

    private readonly AnimationTree _tree;
    private readonly Node3D _footL;
    private readonly Node3D _footR;
    private readonly Node3D _armL;
    private readonly Node3D _armR;
    private readonly Node3D _waist;
    private readonly Node3D _head;

    private readonly System.Collections.Generic.Dictionary<string, float> _clipLengthSec = new();

    private string _actionInput = "loco";
    private string _holdInput = "empty";
    private AnimationLodTier _tier = AnimationLodTier.Full;

    /// <summary>
    /// <b>The pose from the last frame the tree actually advanced, cached rather than re-read.</b>
    ///
    /// <para>A skipped frame must NOT re-read the rig, and the reason is a feedback loop rather than
    /// a saving. <c>AvatarVisual</c> hands the hip angle to <c>SolveLeg</c>, which writes
    /// <c>ThighL.Rotation</c> itself — and while a knee fold is non-zero (a landing absorb) the
    /// solver's root angle is deliberately NOT the angle it was given. Re-reading the rig on a
    /// half-rate or frozen body would therefore feed the solver its own previous output as next
    /// frame's clip angle, and the pose would creep. Caching what the tree last said keeps the
    /// clip's proposal the clip's.</para>
    /// </summary>
    private AvatarClipPose _lastPose;
    private int _framesSinceAdvance;
    private double _carriedDelta;

    private AvatarClipDirector(
        AnimationTree tree, Node3D footL, Node3D footR, Node3D armL, Node3D armR,
        Node3D waist, Node3D head,
        System.Collections.Generic.Dictionary<string, float> lengths)
    {
        _tree = tree;
        _footL = footL;
        _footR = footR;
        _armL = armL;
        _armR = armR;
        _waist = waist;
        _head = head;
        _clipLengthSec = lengths;
    }

    /// <summary>Clip length in seconds, or 0 if the library does not carry it.</summary>
    public float LengthOf(string clip) => _clipLengthSec.TryGetValue(clip, out float s) ? s : 0f;

    /// <summary>The tier this body was last advanced at — reported by the LOD capture.</summary>
    public AnimationLodTier Tier => _tier;

    /// <summary>
    /// <b>Builds the tree over an imported <c>Greybox.glb</c> scene, or returns null and says why.</b>
    /// Null is a supported answer, not a failure to swallow: a rig with no <c>AnimationPlayer</c> is
    /// every roster row except this one, and the caller falls back to the procedural gait rather than
    /// crashing. It is loud only when the clips are asked for and the library is broken.
    /// </summary>
    public static AvatarClipDirector? TryBuild(Node3D importedRoot, string modelPath)
    {
        AnimationPlayer? player = null;
        foreach (Node child in importedRoot.GetChildren())
        {
            if (child is AnimationPlayer p)
            {
                player = p;
                break;
            }
        }
        if (player == null)
        {
            GD.PushError($"AvatarClipDirector: '{modelPath}' carries no AnimationPlayer; the clip " +
                         "library cannot be built. Falling back to the procedural gait.");
            return null;
        }

        // Every joint the clips key, resolved by name ONCE. A renamed node is loud here rather than
        // being a track that resolves to nothing — which is silent in Godot and is precisely the
        // failure mode ANIM-M0 §3.1(b) rejected option (b) over.
        if (Find(importedRoot, "ThighL") is not { } footL || Find(importedRoot, "ThighR") is not { } footR
            || Find(importedRoot, "ArmL") is not { } armL || Find(importedRoot, "ArmR") is not { } armR
            || Find(importedRoot, "Waist") is not { } waist || Find(importedRoot, "Head") is not { } head)
        {
            GD.PushError($"AvatarClipDirector: '{modelPath}' is missing one of the six joints the " +
                         "clip library keys (ThighL, ThighR, ArmL, ArmR, Waist, Head).");
            return null;
        }

        var lengths = new System.Collections.Generic.Dictionary<string, float>();
        var missing = new System.Collections.Generic.List<string>();
        foreach (string clip in AvatarClipNames.All)
        {
            if (player.HasAnimation(clip))
                lengths[clip] = (float)player.GetAnimation(clip).Length;
            else
                missing.Add(clip);
        }
        if (missing.Count > 0)
        {
            GD.PushError($"AvatarClipDirector: '{modelPath}' is missing clip(s) " +
                         $"{string.Join(", ", missing)}; the library is incomplete.");
            return null;
        }

        var tree = new AnimationTree { Name = "AnimationTree" };
        importedRoot.AddChild(tree);

        // The libraries move onto the tree rather than the tree pointing at the player
        // (AnimationTree.AnimPlayer, deprecated since 4.3). One mixer, one writer.
        foreach (StringName lib in player.GetAnimationLibraryList())
            tree.AddAnimationLibrary(lib, player.GetAnimationLibrary(lib));
        // And the player is switched off, so it can never become a second writer on the same six
        // nodes. Not freed: it is what an editor session scrubs, which is the whole point of the
        // migration.
        player.Active = false;

        var root = new AnimationNodeBlendTree();
        BuildGraph(root, player);
        tree.TreeRoot = root;
        tree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        tree.Active = true;

        return new AvatarClipDirector(tree, footL, footR, armL, armR, waist, head, lengths);
    }

    private static Node3D? Find(Node root, string name) =>
        root.FindChild(name, recursive: true, owned: false) as Node3D;

    private static void BuildGraph(AnimationNodeBlendTree root, AnimationPlayer player)
    {
        // --- The locomotion blend space -----------------------------------------------------------
        var loco = new AnimationNodeBlendSpace1D
        {
            MinSpace = 0f,
            MaxSpace = ClipTimeWarp.RunNominalMps,
            // See AnimationCrossfade.LocomotionBlendSec: interpolation here was MEASURED at up to
            // 99.5 mm of planted-foot skate. Discrete-carry keeps the playback position across the
            // switch, which is what makes a hard switch invisible on two clips that share a sweep.
            BlendMode = AnimationNodeBlendSpace1D.BlendModeEnum.DiscreteCarry,
            Sync = true,
        };
        // Named, not left to the safe-index default: Godot 4.7 warns per unnamed point per build,
        // which on a six-player session is a wall of deprecation noise, and a named point is what a
        // future engine version will require anyway. atIndex -1 means "append".
        loco.AddBlendPoint(Clip(AvatarClipNames.Idle), 0f, -1, AvatarClipNames.Idle);
        loco.AddBlendPoint(Clip(AvatarClipNames.Walk), ClipTimeWarp.WalkNominalMps, -1, AvatarClipNames.Walk);
        loco.AddBlendPoint(Clip(AvatarClipNames.Run), ClipTimeWarp.RunNominalMps, -1, AvatarClipNames.Run);
        root.AddNode(LocoNode, loco, new Vector2(0, 0));

        var warp = new AnimationNodeTimeScale();
        root.AddNode(WarpNode, warp, new Vector2(200, 0));
        root.ConnectNode(WarpNode, 0, LocoNode);

        // --- The action transition ----------------------------------------------------------------
        (string input, string clip)[] actions =
        {
            ("skid", AvatarClipNames.Skid),
            ("launch", AvatarClipNames.JumpLaunch),
            ("air", AvatarClipNames.JumpAir),
            ("land", AvatarClipNames.JumpLand),
            ("stagger", AvatarClipNames.Stagger),
            ("knock", AvatarClipNames.KnockOut),
        };
        foreach ((string input, string clip) in actions)
            root.AddNode($"act_{input}", Clip(clip), new Vector2(200, 0));

        var action = new AnimationNodeTransition { AllowTransitionToSelf = false };
        action.Set("input_count", actions.Length + 1);
        action.Set("input_0/name", "loco");
        for (int i = 0; i < actions.Length; i++)
            action.Set($"input_{i + 1}/name", actions[i].input);
        root.AddNode(ActionNode, action, new Vector2(400, 0));
        root.ConnectNode(ActionNode, 0, WarpNode);
        for (int i = 0; i < actions.Length; i++)
            root.ConnectNode(ActionNode, i + 1, $"act_{actions[i].input}");

        var actionSeek = new AnimationNodeTimeSeek();
        root.AddNode(ActionSeekNode, actionSeek, new Vector2(600, 0));
        root.ConnectNode(ActionSeekNode, 0, ActionNode);

        // --- The upper-body override --------------------------------------------------------------
        (string input, string clip)[] holds =
        {
            ("empty", AvatarClipNames.HoldEmpty),
            ("ready", AvatarClipNames.HoldNetReady),
            ("swing", AvatarClipNames.HoldNetSwing),
            ("handle", AvatarClipNames.CarryHandle),
            ("armful", AvatarClipNames.CarryArmful),
        };
        foreach ((string input, string clip) in holds)
            root.AddNode($"hold_{input}", Clip(clip), new Vector2(200, 200));

        var hold = new AnimationNodeTransition { AllowTransitionToSelf = false };
        hold.Set("input_count", holds.Length);
        for (int i = 0; i < holds.Length; i++)
            hold.Set($"input_{i}/name", holds[i].input);
        root.AddNode(HoldNode, hold, new Vector2(400, 200));
        for (int i = 0; i < holds.Length; i++)
            root.ConnectNode(HoldNode, i, $"hold_{holds[i].input}");

        var holdSeek = new AnimationNodeTimeSeek();
        root.AddNode(HoldSeekNode, holdSeek, new Vector2(600, 200));
        root.ConnectNode(HoldSeekNode, 0, HoldNode);

        // --- The layer, and the track filter ANIM-M2 §7 warned this needs --------------------------
        //
        // The .glb carries two channels for Carry_Armful; Godot's importer pads EVERY clip out to six
        // tracks, one per node animated anywhere in the file, with the missing ones constant at rest.
        // remove_immutable_tracks is already on and does not remove them. So an unfiltered Blend2 of
        // an arm override over Walk at weight 1 drives the LEG tracks with the override's padding and
        // freezes the legs. The filter is not an optimisation; it is what makes the override an
        // override.
        //
        // The enabled paths are read out of a real animation rather than spelled out here, so a
        // re-export that moves ArmL in the hierarchy keeps filtering the arm instead of silently
        // filtering nothing.
        var arms = new AnimationNodeBlend2 { FilterEnabled = true, Sync = true };
        foreach (NodePath path in ArmTrackPaths(player))
            arms.SetFilterPath(path, true);
        root.AddNode(ArmsNode, arms, new Vector2(800, 0));
        root.ConnectNode(ArmsNode, 0, ActionSeekNode);
        root.ConnectNode(ArmsNode, 1, HoldSeekNode);
        root.ConnectNode("output", 0, ArmsNode);
    }

    private static AnimationNodeAnimation Clip(string name) =>
        new() { Animation = name, PlayMode = AnimationNodeAnimation.PlayModeEnum.Forward };

    /// <summary>Every track path in the library whose target node is <c>ArmL</c> or <c>ArmR</c>.
    /// Read from the shipped animations so the filter cannot go stale against a re-export.</summary>
    private static System.Collections.Generic.List<NodePath> ArmTrackPaths(AnimationPlayer player)
    {
        var paths = new System.Collections.Generic.List<NodePath>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (string clip in AvatarClipNames.All)
        {
            if (!player.HasAnimation(clip))
                continue;
            Animation anim = player.GetAnimation(clip);
            for (int t = 0; t < anim.GetTrackCount(); t++)
            {
                NodePath path = anim.TrackGetPath(t);
                string s = path.ToString();
                // The node name is the last name component; a 3D transform track's path may or may
                // not carry a ":property" tail depending on the track type, so both are matched.
                string node = s;
                int colon = node.IndexOf(':');
                if (colon >= 0)
                    node = node[..colon];
                int slash = node.LastIndexOf('/');
                if (slash >= 0)
                    node = node[(slash + 1)..];
                if (node is not ("ArmL" or "ArmR"))
                    continue;
                if (seen.Add(s))
                    paths.Add(path);
            }
        }
        return paths;
    }

    /// <summary>
    /// <b>Feed the tree from one frame of replicated state and read back the pose it proposes.</b>
    /// </summary>
    /// <param name="delta">Rendered frame time.</param>
    /// <param name="gear">Derived from replicated ground speed by <c>LocomotionProfile.GearFor</c>.</param>
    /// <param name="groundSpeedMps">Replicated horizontal speed.</param>
    /// <param name="action">Derived by <see cref="AvatarActionStates.Derive"/>.</param>
    /// <param name="hold">Derived from the replicated hold/aim/swing state.</param>
    /// <param name="actionSeekSec">Where in the action clip this body should be, from
    /// <see cref="ClipTimeAnchor"/>, or negative for "wherever it already is".</param>
    /// <param name="holdSeekSec">Same, for the upper-body override — the swing's replicated
    /// <c>Progress01</c> lands here.</param>
    /// <param name="tier">This frame's LOD tier.</param>
    /// <returns>The clip pose, read off the rig after evaluation.</returns>
    public AvatarClipPose Advance(
        double delta, Gear gear, float groundSpeedMps,
        AvatarActionState action, AvatarHoldState hold,
        float actionSeekSec, float holdSeekSec, AnimationLodTier tier)
    {
        _tier = tier;

        // The blend position is the SPEED, in the blend space's own metres-per-second axis — so the
        // point the space lands on is the clip whose authored speed is nearest, and the warp below
        // makes up the difference exactly.
        _tree.Set($"parameters/{LocoNode}/blend_position", Mathf.Abs(groundSpeedMps));
        _tree.Set($"parameters/{WarpNode}/scale", ClipTimeWarp.RateFor(gear, groundSpeedMps));

        string wantAction = InputFor(action);
        if (wantAction != _actionInput)
        {
            // xfade BEFORE the request: AnimationNodeTransition reads the property when the request
            // is consumed, so setting it after would fade the transition after this one at this
            // one's rate.
            SetXfade(ActionNode, AnimationCrossfade.SecondsFor(action));
            _tree.Set($"parameters/{ActionNode}/transition_request", wantAction);
            _actionInput = wantAction;
        }

        string wantHold = InputFor(hold);
        if (wantHold != _holdInput)
        {
            SetXfade(HoldNode, AnimationCrossfade.OverrideSecondsFor(hold));
            _tree.Set($"parameters/{HoldNode}/transition_request", wantHold);
            _holdInput = wantHold;
        }

        // The seeks. Requested every frame the caller has an authoritative answer, not only on the
        // transition edge — that is what makes a client which received a state change LATE converge
        // rather than merely start in the right place. A negative value means "no opinion", which is
        // the locomotion case: a stride's phase is integrated locally and is not a replicated fact.
        if (actionSeekSec >= 0f)
            _tree.Set($"parameters/{ActionSeekNode}/seek_request", actionSeekSec);
        if (holdSeekSec >= 0f)
            _tree.Set($"parameters/{HoldSeekNode}/seek_request", holdSeekSec);

        // --- The LOD gate -------------------------------------------------------------------------
        // The skipped frame's delta is CARRIED, never dropped. Advancing a half-rate body by half the
        // time would be a body walking in slow motion — a different animation, not a cheaper one —
        // and it would break stride × cadence for every remote peer past 18 m.
        _carriedDelta += delta;
        int every = AvatarAnimationLod.FramesPerAdvance(tier);
        if (every <= 0)
        {
            // Frozen: hold the last evaluated pose. The carried delta keeps accumulating so a body
            // that comes back into range resumes with its phase carried rather than restarting.
            return _lastPose;
        }
        if (++_framesSinceAdvance < every)
            return _lastPose;
        _framesSinceAdvance = 0;

        _tree.Advance(_carriedDelta);
        _carriedDelta = 0.0;
        _lastPose = ReadPose();
        return _lastPose;
    }

    private void SetXfade(string node, float seconds)
    {
        if (_tree.TreeRoot is AnimationNodeBlendTree root
            && root.GetNode(node) is AnimationNodeTransition transition)
        {
            transition.XfadeTime = seconds;
        }
    }

    private static string InputFor(AvatarActionState state) => state switch
    {
        AvatarActionState.Skid => "skid",
        AvatarActionState.JumpLaunch => "launch",
        AvatarActionState.Airborne => "air",
        AvatarActionState.Land => "land",
        AvatarActionState.Stagger => "stagger",
        AvatarActionState.KnockOut => "knock",
        _ => "loco",
    };

    private static string InputFor(AvatarHoldState hold) => hold switch
    {
        AvatarHoldState.NetReady => "ready",
        AvatarHoldState.NetSwing => "swing",
        AvatarHoldState.CarryHandle => "handle",
        AvatarHoldState.CarryArmful => "armful",
        _ => "empty",
    };

    /// <summary>Read the six evaluated joints back out as angles the procedural layer speaks.
    /// <c>Rotation.X</c> on the limbs (the clips are pure pitch quaternions) and <c>Rotation.Y</c> on
    /// the trunk (pure yaw) — measured off the shipped bytes, and asserted by
    /// <c>AvatarClipContractTests</c> so an authored roll cannot arrive unnoticed.</summary>
    private AvatarClipPose ReadPose() => new(
        LegAngleL: _footL.Rotation.X,
        LegAngleR: _footR.Rotation.X,
        ArmPitchL: _armL.Rotation.X,
        ArmPitchR: _armR.Rotation.X,
        WaistYaw: _waist.Rotation.Y,
        HeadYaw: _head.Rotation.Y);
}
