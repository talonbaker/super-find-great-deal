using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b><c>--rotate-look-selftest</c>: the FP-1 x CARRY-1 input-routing probe</b> (INT-0,
/// 2026-09-19). Prints one summary line the runner gates on:
///
/// <code>[rotlook-selftest] SUMMARY failures=&lt;n&gt; result=PASS|FAIL</code>
///
/// <para><b>The seam.</b> CARRY-1's <see cref="HeldPropRotator"/> takes the mouse away from the
/// camera while the rotate modifier is held: it reads motion in <c>_Input</c> and calls
/// <c>SetInputAsHandled</c>. FP-1's <see cref="FirstPersonCamera"/> reads motion in
/// <c>_UnhandledInput</c>, which runs afterwards and only for events nobody consumed. CARRY-1's
/// own header wrote that coupling down as an assumption about "any camera that also reads the
/// mouse as unhandled input" and flagged it for the merge. This measures it instead.</para>
///
/// <para><b>Both directions, because one of them is the failure nobody would notice.</b> If the
/// rotator consumed motion it should not, the player's view would simply stop working after the
/// first time they turned something — and the "prop rotates, view does not" half would still pass.
/// So the probe injects the SAME motion twice: once with the modifier held (the prop must move and
/// the view must not) and once with it released (the view must move and the prop must not). Each
/// half is the other's positive control, which is the CELEBRATE-1 rule in
/// <c>.claude/rules/test-suite.md</c> applied to an event chain.</para>
///
/// <para><b>It builds a real rotator through the shipped entry point.</b>
/// <c>HeldPropRotator.Attach</c> is what <c>SandboxAvatar</c>'s human branch calls; a bot takes the
/// other branch and has none, so the probe calls the same method rather than reproducing what it
/// does. Same pattern, and same reason, as <c>--first-person-cam</c> mounting the shipped camera
/// rig on a bot.</para>
///
/// <para>Reports; never steers. It sends nothing on the wire and writes nothing to the world
/// except the held prop's local rotation, which is the thing under test and is client-side until a
/// PLACE carries it. It restores the mouse mode it found.</para>
/// </summary>
public partial class RotateLookSelfTest : Node
{
    /// <summary>Pixels of synthetic mouse travel per injected event, and how many. Large enough
    /// that the camera's 0.0025 rad/px and the rotator's 0.010 rad/px both produce an angle far
    /// above float noise, so "did not move" is a real zero rather than a small number.</summary>
    private const float MotionPx = 40f;
    private const int MotionEvents = 5;

    /// <summary>An axis that moved must clear this; an axis that must not move has to stay under
    /// it. The two are three orders of magnitude apart in practice (a 200 px sweep is 0.5 rad of
    /// look and 2.0 rad of prop yaw), so the bar is only here to name "exactly zero, up to float
    /// error" without asserting bitwise equality on a composed basis.</summary>
    private const float MovedRad = 0.02f;

    /// <summary>Give up waiting for the scripted grab. The probe cannot run at all with empty
    /// hands — <see cref="HeldPropRotator.Active"/> requires a held prop, deliberately, so that the
    /// shared RMB binding never steals the look from a player who is carrying nothing.</summary>
    private const double GrabTimeoutSec = 20.0;

    public void Setup(FirstPersonCamera camera, SandboxAvatar avatar)
    {
        _camera = camera;
        _avatar = avatar;
    }

    private FirstPersonCamera _camera = null!;
    private SandboxAvatar _avatar = null!;
    private HeldPropRotator? _rotator;

    private readonly List<string> _failures = new();
    private readonly List<string> _facts = new();
    private double _clock;
    private int _phase;
    private Input.MouseModeEnum _restoreMouseMode;

    private float _camYawBefore;
    private Basis _propBefore;
    private float _heldCamYawDelta;
    private float _heldPropDelta;
    private bool _rotatorActiveWhileHeld;

    public override void _Process(double delta)
    {
        switch (_phase)
        {
            case 0:
                // Wait for the scripted carry brain to actually be holding something.
                _clock += delta;
                if (_avatar.Props?.FindHeldBy(_avatar.OwnerPeerId) == null)
                {
                    if (_clock < GrabTimeoutSec)
                        return;
                    Fail($"the bot was still holding nothing after {GrabTimeoutSec:F0}s — the "
                         + "rotate/look seam cannot be measured with empty hands, and this is a "
                         + "staging failure, not a routing one");
                    Report();
                    _phase = 99;
                    return;
                }
                // The rotator and the camera BOTH gate on a captured cursor, so a probe that left
                // the mode alone would measure two rigs that had each correctly decided to do
                // nothing. This is why the probe has to run WINDOWED and why it is a one-off
                // integration check rather than a registered suite: it takes the real cursor for
                // the length of its run, which is exactly what FP-1's suite refuses to do.
                _restoreMouseMode = Input.MouseMode;
                Input.MouseMode = Input.MouseModeEnum.Captured;
                // MEASURED, 2026-09-19: a HEADLESS run cannot take this. Godot's dummy
                // DisplayServer ignores the setter, MouseMode stays Visible, and BOTH rigs
                // correctly decide to do nothing — which prints as "neither the prop nor the view
                // moved" and reads exactly like a routing failure. Caught here as a named STAGING
                // failure instead, because the first version of this probe reported that as a
                // FAIL on two checks with both of its positive controls also silently failing.
                if (Input.MouseMode != Input.MouseModeEnum.Captured)
                {
                    Fail($"could not capture the cursor — Input.MouseMode is {Input.MouseMode} after "
                         + "asking for Captured. Both the rotator and the camera gate on a captured "
                         + "cursor, so NOTHING would move and that is a staging failure, not a "
                         + "routing one. Run this probe WINDOWED.");
                    Report();
                    _phase = 99;
                    return;
                }
                _rotator = HeldPropRotator.Attach(_avatar);
                _phase = 1;
                return;

            case 1:
                // MODIFIER HELD. Record, inject, measure next frame: ParseInputEvent is queued and
                // flushed by the viewport, so a read on this frame would see the before-state.
                Input.ActionPress(HeldPropRotator.RotateAction);
                _camYawBefore = _camera.Yaw;
                _propBefore = _avatar.HeldPropLocalRotation;
                InjectMotion();
                _phase = 2;
                return;

            case 2:
                _heldCamYawDelta = Mathf.Abs(Mathf.AngleDifference(_camYawBefore, _camera.Yaw));
                _heldPropDelta = AngleBetween(_propBefore, _avatar.HeldPropLocalRotation);
                // Read WHILE the modifier is still down. Sampled after the release it is always
                // false and says nothing, which is how the first version of this probe printed a
                // fact that looked like a finding.
                _rotatorActiveWhileHeld = _rotator!.Active;
                Input.ActionRelease(HeldPropRotator.RotateAction);
                _phase = 3;
                return;

            case 3:
                // MODIFIER RELEASED, same motion. The rotator must now let it through.
                _camYawBefore = _camera.Yaw;
                _propBefore = _avatar.HeldPropLocalRotation;
                InjectMotion();
                _phase = 4;
                return;

            case 4:
            {
                float freeCamDelta = Mathf.Abs(Mathf.AngleDifference(_camYawBefore, _camera.Yaw));
                float freePropDelta = AngleBetween(_propBefore, _avatar.HeldPropLocalRotation);

                _facts.Add($"rotate held:     prop turned {_heldPropDelta:F4} rad, "
                           + $"camera yaw moved {_heldCamYawDelta:F4} rad");
                _facts.Add($"rotate released: prop turned {freePropDelta:F4} rad, "
                           + $"camera yaw moved {freeCamDelta:F4} rad");
                _facts.Add($"{MotionEvents} synthetic motion event(s) of {MotionPx:F0} px per pass, "
                           + $"identical in both");
                _facts.Add($"rotator reported Active={_rotatorActiveWhileHeld} while the modifier "
                           + "was down (it polls the held prop and the action together)");

                Check("held_the_prop_turns", _heldPropDelta > MovedRad,
                    $"with the rotate modifier held the prop turned only {_heldPropDelta:F4} rad — "
                    + "the rotator never saw the motion, so the suppression below proves nothing");
                Check("held_the_view_does_not", _heldCamYawDelta < MovedRad,
                    $"with the rotate modifier held the camera yaw still moved {_heldCamYawDelta:F4} rad "
                    + "— the object is turning AND the view is turning, which reads as the object "
                    + "being stuck to the screen");
                Check("released_the_view_turns", freeCamDelta > MovedRad,
                    $"with the modifier released the camera yaw moved only {freeCamDelta:F4} rad — "
                    + "the look never came back after a rotate, which is the failure a player would "
                    + "report as 'I can't look around any more'");
                Check("released_the_prop_does_not", freePropDelta < MovedRad,
                    $"with the modifier released the prop still turned {freePropDelta:F4} rad — the "
                    + "rotator is acting without its modifier");

                CheckHeldPropIsRenderedByThisLens();
                Report();
                _phase = 99;
                return;
            }

            default:
                SetProcess(false);
                return;
        }
    }

    /// <summary>
    /// <b>The third FP-1 x CARRY-1 question, and the one a screenshot answers badly.</b> FP-1
    /// hides the local body by putting its whole visual rig on render layer 19 and dropping 19
    /// from the first-person lens's cull mask. A carried prop that ended up on that layer would be
    /// invisible in the hands of the person carrying it — the whole R.E.P.O. verb, gone, and gone
    /// only for the holder, which is the peer least likely to be the one a capture is taken from.
    ///
    /// <para>Asked as a mask question rather than a photograph because a photograph cannot
    /// distinguish "culled" from "out of frame", "behind a wall" or "clipped by the near plane" —
    /// and the near plane is a real hazard here, not a hypothetical: the crate rides about 0.26 m
    /// from the eye (reported below), which puts its near face within a couple of centimetres of
    /// the lens's 0.05 m near plane.</para>
    /// </summary>
    private void CheckHeldPropIsRenderedByThisLens()
    {
        Props.NetworkedProp? held = _avatar.Props?.FindHeldBy(_avatar.OwnerPeerId);
        if (held?.Body is not Node3D body)
        {
            Fail("the bot stopped holding anything before the render-layer check — not measured");
            return;
        }

        var meshes = new List<VisualInstance3D>();
        CollectVisuals(body, meshes);
        Camera3D lens = _camera.CameraNode;
        uint hiddenBit = 1u << (AvatarVisual.FirstPersonHiddenLayer - 1);
        uint cull = lens.CullMask;

        var onHidden = new List<string>();
        var notRendered = new List<string>();
        foreach (VisualInstance3D v in meshes)
        {
            if ((v.Layers & hiddenBit) != 0)
                onHidden.Add($"{v.Name} (layers 0x{v.Layers:X})");
            if ((v.Layers & cull) == 0)
                notRendered.Add($"{v.Name} (layers 0x{v.Layers:X})");
        }

        float dist = lens.GlobalPosition.DistanceTo(held.WorldPosition);
        _facts.Add($"held prop {held.PropId}: {meshes.Count} renderable(s), "
                   + $"{dist:F3} m from the lens (near plane {FirstPersonCamera.NearPlaneM:F2} m), "
                   + $"lens cull mask 0x{cull:X}, first-person hidden layer "
                   + $"{AvatarVisual.FirstPersonHiddenLayer} (bit 0x{hiddenBit:X})");

        // A prop with no renderables at all would pass both checks below while proving nothing —
        // the positive control this pair needs.
        Check("held_prop_has_renderables", meshes.Count > 0,
            "the held prop has no VisualInstance3D at all, so 'it is not culled' is vacuous");
        Check("held_prop_is_not_on_the_first_person_hidden_layer", onHidden.Count == 0,
            $"{onHidden.Count} of the held prop's renderables are on layer "
            + $"{AvatarVisual.FirstPersonHiddenLayer}, which this lens does not draw: "
            + string.Join(", ", onHidden));
        Check("held_prop_is_rendered_by_this_lens", notRendered.Count == 0,
            $"{notRendered.Count} of the held prop's renderables share no layer with this lens's "
            + $"cull mask 0x{cull:X}: " + string.Join(", ", notRendered));
    }

    private static void CollectVisuals(Node node, List<VisualInstance3D> into)
    {
        if (node is VisualInstance3D v)
            into.Add(v);
        foreach (Node child in node.GetChildren())
            CollectVisuals(child, into);
    }

    private static void InjectMotion()
    {
        for (int i = 0; i < MotionEvents; i++)
        {
            Input.ParseInputEvent(new InputEventMouseMotion
            {
                Relative = new Vector2(MotionPx, 0f),
                ScreenRelative = new Vector2(MotionPx, 0f),
            });
        }
    }

    /// <summary>Total rotation angle between two bases, radians — the honest scalar for "did this
    /// orientation change", where comparing one Euler component would miss a pure pitch.</summary>
    private static float AngleBetween(Basis a, Basis b) =>
        (float)(a.GetRotationQuaternion().AngleTo(b.GetRotationQuaternion()));

    private void Check(string name, bool ok, string failure)
    {
        if (!ok)
            _failures.Add($"{name}: {failure}");
    }

    private void Fail(string failure) => _failures.Add(failure);

    private void Report()
    {
        Input.MouseMode = _restoreMouseMode;
        foreach (string fact in _facts)
            GD.Print($"[rotlook-selftest]   {fact}");
        foreach (string failure in _failures)
            GD.Print($"[rotlook-selftest] FAIL {failure}");
        GD.Print($"[rotlook-selftest] SUMMARY failures={_failures.Count} "
                 + $"result={(_failures.Count == 0 ? "PASS" : "FAIL")}");
    }
}
