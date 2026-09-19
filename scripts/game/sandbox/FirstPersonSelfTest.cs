using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b><c>--first-person-selftest</c>: the machine-readable half of FP-1's smoke.</b> Attached to a
/// windowed client's own avatar beside a real <see cref="FirstPersonCamera"/>, it waits for the
/// spawn to settle, measures the rig, and prints one summary line the runner gates on:
///
/// <code>[fp-selftest] SUMMARY failures=&lt;n&gt; result=PASS|FAIL</code>
///
/// <para><b>Gated on the LINE, not on the exit code</b>, for the reason
/// <c>Run-SupermarketWorldTest.ps1</c>'s header spells out: a process that dies before it reports
/// exits non-zero for reasons that have nothing to do with the thing under test, and "never
/// reported" is a different problem from "reported a failure". The runner checks the code after.</para>
///
/// <para><b>The one check that makes the rest mean anything is the POSITIVE CONTROL</b>
/// (<c>own_body_would_be_in_frame</c>). CELEBRATE-1's lesson, in this file's own terms: a cull
/// mask that excludes a body which was never in shot would pass a "no body in frame" assertion
/// every time while defending nothing. So the probe pitches the look down to the clamp, confirms
/// the body's geometry really does fall inside the frustum from there, and only THEN asserts that
/// every one of those meshes is on the layer this camera does not render. The look is restored
/// before the next frame; the capture the runner takes is at the ordinary level look.</para>
///
/// <para>Reports; never steers. It writes nothing to the world, sends nothing on the wire, and
/// the flag is only ever set by <c>tests/Run-FirstPersonTest.ps1</c>.</para>
/// </summary>
public partial class FirstPersonSelfTest : Node
{
    /// <summary>How long to let the session settle before measuring. The avatar re-measures its
    /// own proportions when the authority's avatar-key pick lands, which is a beat AFTER the
    /// camera attaches, and the eye-height check is a statement about the settled body.</summary>
    private const double SettleSec = 3.0;

    /// <summary>Tolerance on the eye-height check, metres. The camera is parented at
    /// <c>AimAnchorLocal</c>, so agreement is exact up to float error and the body's own vertical
    /// motion between the two reads — a centimetre is already generous.</summary>
    private const float EyeHeightToleranceM = 0.01f;

    /// <summary>Wired after construction, the same shape <c>BotHarness.Setup</c> uses — a Godot
    /// node type has to stay parameterlessly constructible for the engine's own interop.</summary>
    public void Setup(FirstPersonCamera camera, SandboxAvatar avatar)
    {
        Camera = camera;
        Avatar = avatar;
    }

    private FirstPersonCamera Camera { get; set; } = null!;
    private SandboxAvatar Avatar { get; set; } = null!;

    private readonly List<string> _failures = new();
    private readonly List<string> _facts = new();
    private double _clock;
    private int _phase;
    private float _restoreYaw;
    private float _restorePitch;

    /// <summary>
    /// Three phases, because one of the checks cannot be made on the frame that sets it up.
    ///
    /// <para><b>The nameplate check is worthless at a level look, and that is the whole reason
    /// this class is a phase machine instead of a method.</b> The plate's anchor sits ~0.46 m
    /// DIRECTLY above the lens, so with the look level it falls on the near plane and
    /// <c>Camera3D.IsPositionBehind</c> hides it by accident — a probe that measured there would
    /// report "not drawn" just as happily with the suppression deleted. It is only past roughly
    /// five degrees of up-look that the anchor crosses in front of the lens and the plate would
    /// really be projected onto the player's own screen. So phase 1 pitches the look fully UP,
    /// phase 2 waits a frame for <c>SandboxAvatar.UpdateNameplate</c> to run at that angle (it is
    /// a <c>_Process</c> sibling of this one, so its order within a frame is not ours to assume),
    /// and only then reads the label — carrying its own positive control that the anchor is in
    /// frustum from there.</para>
    /// </summary>
    public override void _Process(double delta)
    {
        switch (_phase)
        {
            case 0:
                _clock += delta;
                if (_clock < SettleSec)
                    return;
                _phase = 1;
                RunGeometryChecks();
                return;
            case 1:
                _phase = 2;
                return;
            case 2:
                _phase = 3;
                RunNameplateCheckAndReport();
                return;
            default:
                SetProcess(false);
                return;
        }
    }

    private void RunGeometryChecks()
    {
        Camera3D lens = Camera.CameraNode;

        // 1. The camera the viewport is actually rendering through is this rig's lens. Not "a
        //    FirstPersonCamera exists" — a camera that is built and never made current is exactly
        //    the failure a screenshot of a flat grey world is made of (.claude/rules/test-suite.md,
        //    the --capture-cam entry).
        Camera3D? active = GetViewport().GetCamera3D();
        Check("active_camera_is_the_first_person_lens", ReferenceEquals(active, lens),
            $"the viewport's current camera is '{active?.Name ?? "<none>"}', not the first-person lens");

        // 2. It sits on this character's measured eyeline, not on a typed height.
        float expected = Avatar.GlobalPosition.Y + Avatar.Proportions.EyeHeightM;
        float actual = lens.GlobalPosition.Y;
        Check("camera_sits_at_measured_eye_height", Mathf.Abs(actual - expected) <= EyeHeightToleranceM,
            $"lens is at y={actual:F3} m, the measured eyeline is y={expected:F3} m " +
            $"(eye height {Avatar.Proportions.EyeHeightM:F3} m, measured={Avatar.Proportions.EyesMeasured})");
        _facts.Add($"eye height {Avatar.Proportions.EyeHeightM:F3} m (measured={Avatar.Proportions.EyesMeasured}), " +
                   $"lens y={actual:F3} m, body y={Avatar.GlobalPosition.Y:F3} m");

        // 3. The lens is the one the packet specifies, and carries none of the third-person rig's
        //    motion channels (there is nothing to assert an absence of — they were never built
        //    into this class — so what is asserted is the two numbers that WERE).
        Check("lens_fov_is_the_default", Mathf.IsEqualApprox(lens.Fov, FirstPersonCamera.DefaultFovDeg),
            $"fov is {lens.Fov:F2} deg, expected {FirstPersonCamera.DefaultFovDeg:F2}");
        Check("lens_near_plane_is_the_default", Mathf.IsEqualApprox(lens.Near, FirstPersonCamera.NearPlaneM),
            $"near plane is {lens.Near:F4} m, expected {FirstPersonCamera.NearPlaneM:F4}");

        // 4. THE POSITIVE CONTROL. Pitch the look down to the clamp and confirm the body's own
        //    geometry really is inside the frustum from there — otherwise every assertion below is
        //    about a body that was never in shot. Restored immediately; nothing renders in between.
        var ownMeshes = new List<VisualInstance3D>();
        Collect(Avatar.Visual, ownMeshes);
        _restoreYaw = Camera.Yaw;
        _restorePitch = Camera.Pitch;
        Camera.SetLook(_restoreYaw, SandboxCamera.PitchMin);
        int inFrame = CountInFrustum(lens, ownMeshes);
        Check("own_body_would_be_in_frame", inFrame > 0,
            "none of this avatar's own meshes fall inside the frustum even with the look pitched " +
            "fully down — the cull below would be defending nothing");
        _facts.Add($"{ownMeshes.Count} own mesh(es) on this rig, {inFrame} of them inside the " +
                   "frustum with the look pitched fully down");

        // 5. ...and none of them can be rendered by this lens.
        bool maskExcludes = !lens.GetCullMaskValue(AvatarVisual.FirstPersonHiddenLayer);
        Check("lens_cull_mask_excludes_the_hidden_layer", maskExcludes,
            $"the lens still renders layer {AvatarVisual.FirstPersonHiddenLayer}; cull mask is " +
            $"0x{lens.CullMask:X}");
        var stragglers = new List<string>();
        foreach (VisualInstance3D mesh in ownMeshes)
        {
            uint onlyHidden = 1u << (AvatarVisual.FirstPersonHiddenLayer - 1);
            if (mesh.Layers != onlyHidden)
                stragglers.Add($"{mesh.Name} (layers 0x{mesh.Layers:X})");
        }
        Check("every_own_mesh_is_on_the_hidden_layer_only", stragglers.Count == 0,
            $"{stragglers.Count} of this avatar's mesh(es) are still on a layer this lens renders: " +
            string.Join(", ", stragglers));

        // 6. Every OTHER avatar in this process is untouched: same layer 1 every camera renders,
        //    so the fix is local to this lens rather than a body that stopped existing. Reported
        //    rather than asserted when this run is alone in the world — a solo run is a legitimate
        //    way to take the capture, and a check that cannot run must say so, not pass quietly.
        int remoteChecked = 0;
        var hiddenRemotes = new List<string>();
        foreach (SandboxAvatar other in SandboxAvatar.Live)
        {
            if (ReferenceEquals(other, Avatar) || other.Visual == null)
                continue;
            remoteChecked++;
            if (other.Visual.HiddenFromFirstPerson)
                hiddenRemotes.Add(other.Name);
        }
        if (remoteChecked == 0)
            _facts.Add("no other avatar in this process — the 'remote bodies stay visible' check did not run");
        else
            Check("other_avatars_are_not_hidden", hiddenRemotes.Count == 0,
                $"{hiddenRemotes.Count} other avatar(s) were hidden from this lens: " +
                string.Join(", ", hiddenRemotes));

        // Set up the nameplate check: look fully UP, and let a frame pass so the avatar's own
        // _Process re-projects the plate at that angle. See this class's _Process doc for why the
        // check is worthless at any other pitch.
        Camera.SetLook(_restoreYaw, SandboxCamera.PitchMax);
    }

    private void RunNameplateCheckAndReport()
    {
        Camera3D lens = Camera.CameraNode;

        // THE POSITIVE CONTROL FIRST, again. Two ways this check can pass while proving nothing:
        // the plate's anchor is not actually in front of the lens (so a projection accident, not
        // the suppression, is hiding it), or world UI is globally suppressed for some unrelated
        // reason. Both are asserted, so "not drawn" can only mean the suppression did it.
        Vector3 anchor = Avatar.RenderGlobalPosition
                         + new Vector3(0f, Avatar.Proportions.NameplateHeightM, 0f);
        bool anchorInShot = !lens.IsPositionBehind(anchor) && lens.IsPositionInFrustum(anchor);
        Check("own_nameplate_anchor_would_project", anchorInShot,
            $"with the look pitched fully up ({SandboxCamera.PitchMax:F2} rad) this avatar's own " +
            "nameplate anchor is still not in shot — the check below would be defending nothing");
        Check("world_ui_is_not_globally_suppressed", !Ui.WorldUi.Suppressed,
            "WorldUi.Suppressed is set, so every nameplate is hidden for an unrelated reason and " +
            "the check below would be defending nothing");

        Check("own_nameplate_is_not_drawn", !Avatar.NameplateVisible,
            "this avatar's own nameplate is being drawn to the camera mounted inside its head");
        _facts.Add($"nameplate anchor {Avatar.Proportions.NameplateHeightM:F3} m above the body, " +
                   $"in shot at full up-look={anchorInShot}, label visible={Avatar.NameplateVisible}");

        Camera.SetLook(_restoreYaw, _restorePitch);

        foreach (string fact in _facts)
            GD.Print($"[fp-selftest]   {fact}");
        foreach (string failure in _failures)
            GD.Print($"[fp-selftest] FAIL {failure}");
        GD.Print($"[fp-selftest] SUMMARY failures={_failures.Count} " +
                 $"result={(_failures.Count == 0 ? "PASS" : "FAIL")}");
    }

    private void Check(string name, bool ok, string detail)
    {
        if (!ok)
            _failures.Add($"{name}: {detail}");
    }

    private static void Collect(Node node, List<VisualInstance3D> into)
    {
        if (node is VisualInstance3D visual)
            into.Add(visual);
        foreach (Node child in node.GetChildren())
            Collect(child, into);
    }

    /// <summary>How many of these meshes have at least one corner of their world-space bounding
    /// box inside the camera's frustum. Corners rather than the centre: a torso whose centre sits
    /// behind a lens mounted in its own head is still very much in shot.</summary>
    private static int CountInFrustum(Camera3D lens, List<VisualInstance3D> meshes)
    {
        int count = 0;
        foreach (VisualInstance3D mesh in meshes)
        {
            Aabb box = mesh.GlobalTransform * mesh.GetAabb();
            bool any = false;
            for (int corner = 0; corner < 8 && !any; corner++)
                any = lens.IsPositionInFrustum(box.GetEndpoint(corner));
            if (any)
                count++;
        }
        return count;
    }
}
