using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The look angles a human intent source samples.</b> Implemented by both camera rigs so
/// <see cref="LocalInputIntentSource"/> and <see cref="FirstPersonIntentSource"/> can share one
/// sampling routine instead of each carrying its own copy of the WASD/jump/sprint wiring — the
/// two differ in which rig supplies the yaw, and in nothing else.
///
/// <para>Both properties are in the SAME convention <c>MoveIntent.AimYaw</c>/<c>AimPitch</c>
/// document and <c>AimQuery.DirectionFromYawPitch</c> reconstructs (yaw about +Y, pitch about
/// +X, forward is <c>-Z</c>), so an intent built from either rig needs no conversion and the
/// server's aim ray is the ray the player saw.</para>
/// </summary>
public interface ILookAngles
{
    /// <summary>World look yaw, radians.</summary>
    float Yaw { get; }

    /// <summary>World look pitch, radians, within
    /// [<see cref="SandboxCamera.PitchMin"/>, <see cref="SandboxCamera.PitchMax"/>].</summary>
    float Pitch { get; }
}

/// <summary>
/// <b>The player's own eyes.</b> A <see cref="Node3D"/> child of the avatar, parked on that
/// character's measured eyeline (<see cref="AvatarProportions.AimAnchorLocal"/>), carrying one
/// <see cref="Camera3D"/>. Mouse look drives <see cref="Yaw"/> and <see cref="Pitch"/>; WASD is
/// turned into world space against <see cref="Yaw"/> by <see cref="FirstPersonIntentSource"/>;
/// the body turns to follow the look because <c>MotorTuning.BodyYawFollowsAim</c> is on.
///
/// <para><b>This is a deliberate break from the third-person rig, not a second mode</b> (FP-1,
/// 2026-09-19). <see cref="SandboxCamera"/> stays in the tree for the tools that still need an
/// orbit view — <c>--spectate-cam</c> captures and the offline dev harnesses — and no human
/// player path reaches it any more. There is no toggle back: a game whose whole subject is what
/// you can and cannot see from where you are standing cannot have two answers to "where are the
/// eyes".</para>
///
/// <para><b>Everything that moves a lens without the player having moved it is absent, and that
/// is the design</b> (packet FP-1, "tuned against motion sickness"). No head bob, no camera lag
/// or follow spring, no speed-FOV, no landing dip, no depth of field. A third-person rig can
/// afford those because the lens is a camera looking AT a character; in first person the lens is
/// the character's head, and every one of them is a movement the player's inner ear did not ask
/// for. The only three numbers are <see cref="DefaultFovDeg"/>,
/// <see cref="NearPlaneM"/> and the sensitivity, which is <see cref="SandboxCamera.MouseSensitivity"/>
/// itself rather than a second copy of it.</para>
///
/// <para><b>Why a child of the avatar rather than a sibling rig.</b> The eyeline is a point ON the
/// body, so parentage is the honest expression of it: the camera inherits the body's position for
/// free and can never lag it by a frame, which is what a follow spring on a first-person lens
/// would be. The body's own yaw is then CANCELLED every frame — <see cref="_Process"/> writes the
/// camera's global basis from <see cref="Yaw"/>/<see cref="Pitch"/> — because the body is turning
/// toward the look and a camera that inherited that turn would chase its own tail. The local
/// offset is on the rotation axis (0, eye, 0), so the parent's yaw never moves it.</para>
///
/// <para><b>The body is not drawn to itself.</b> Attaching hides the avatar's own visual rig from
/// this camera through a render layer — see <see cref="AvatarVisual.FirstPersonHiddenLayer"/> for
/// why a cull mask rather than a hidden node, and why every other peer still sees the body.</para>
/// </summary>
public partial class FirstPersonCamera : Node3D, ILookAngles
{
    /// <summary>Vertical field of view, degrees. 75 is the third-person rig's own resting lens
    /// (<c>SandboxCamera.DefaultFov</c>) and the conventional first-person default; wider reads as
    /// speed and is exactly the channel this rig is not allowed to use.</summary>
    public const float DefaultFovDeg = 75f;

    /// <summary>Near clip, metres. 5 cm rather than Godot's 0.05-default-by-coincidence: a
    /// first-person lens sits inside the player's own collision capsule, so anything further out
    /// clips through a wall the body is pressed against before the body stops.</summary>
    public const float NearPlaneM = 0.05f;

    /// <summary>Esc toggles mouse capture itself when nothing else will. False in a networked
    /// session, where <c>PauseOverlay</c> owns the capture/release — the same split
    /// <see cref="SandboxCamera.HandlesPauseToggle"/> makes, for the same reason.</summary>
    public bool HandlesPauseToggle { get; init; }

    /// <summary>Current look yaw, radians. Same convention as <see cref="SandboxCamera.Yaw"/>, so
    /// <c>MoveIntent.AimYaw</c> needs no conversion on either side of the wire.</summary>
    public float Yaw { get; private set; }

    /// <summary>Current look pitch, radians, always within
    /// [<see cref="SandboxCamera.PitchMin"/>, <see cref="SandboxCamera.PitchMax"/>] — the SAME
    /// clamp <c>AvatarMotor.SanitizeAimPitch</c> enforces on receipt, referenced rather than
    /// duplicated so a legitimate look can never be an illegal aim.</summary>
    public float Pitch => _pitch;

    /// <summary>The lens itself. Handed to <c>SandboxAvatar.AimCamera</c> so
    /// <c>InteractTargeting.Pick</c> keeps resolving "the thing I am looking at".</summary>
    public Camera3D CameraNode => _camera;

    /// <summary>The avatar this is the head of, or null before <see cref="Attach"/>.</summary>
    public SandboxAvatar? Target => _target;

    /// <summary><b>The one lens this process is looking through</b>, or null if this process
    /// built none (every headless suite bot, and a <c>--spectate-cam</c> run). Written by
    /// <see cref="Attach"/> and cleared in <see cref="_ExitTree"/>, exactly the
    /// <c>HideSeekDriver.Instance</c> / <c>RunDriver.Instance</c> / <c>CycleDriver.Instance</c>
    /// idiom this repo already uses three times.
    ///
    /// <para><b>Why a static rather than a lookup</b> (DOOR-1, 2026-09-19). The burst's camera
    /// kick is raised by a node in the LEVEL — the door — which has no route to the player's
    /// avatar that is not "walk the whole tree looking for the one body whose
    /// <c>IsLocalFirstPersonBody</c> is set", once per burst, on a path that also runs on a
    /// headless server where the answer is always null. Exactly one of these can exist by
    /// construction (<c>SandboxAvatar.ConfigureNetworkedInstance</c> builds it on the one body
    /// this machine drives), which is what makes a static honest here rather than a shortcut.</para></summary>
    public static FirstPersonCamera? Local { get; private set; }

    private Camera3D _camera = null!;
    private SandboxAvatar? _target;
    private float _pitch;

    // --- The transient kick (DOOR-1) --------------------------------------------------------
    //
    // A kick is an OFFSET applied when the basis is written, and it is never folded back into
    // Yaw/_pitch. That is the whole safety property: Yaw and Pitch are what FirstPersonIntentSource
    // puts in MoveIntent.AimYaw/AimPitch, which is what rides the wire and what the server rebuilds
    // its aim ray from -- so a kick that wrote them would move the player's actual aim, desync the
    // prediction replay against the authority, and change what E resolves against, all to shake a
    // camera. It would also be a frame of input the player did not ask for and did not give, which
    // program §5 forbids in as many words ("nobody ever loses input").
    private float _kickDeg;
    private float _kickSec;
    private double _kickElapsed;

    /// <summary>The kick's live pitch offset in degrees, 0 when nothing is kicking. Read by the
    /// first-person self-test and by DOOR-1's smoke through the log; exposed rather than inferred
    /// because "the kick did nothing" and "the kick is over" are the same picture.</summary>
    public float KickPitchOffsetDeg { get; private set; }

    /// <summary>
    /// Mount on <paramref name="target"/>'s eyeline and become the current camera.
    /// </summary>
    /// <param name="target">The avatar whose head this is. Must already be this node's parent —
    /// the caller adds the child, so the camera is in the tree before its first frame.</param>
    /// <param name="captureMouse">Grab the cursor, as entering play does. <b>False for the
    /// capture/probe path</b>: a suite that seizes the mouse of whoever is at the keyboard is a
    /// suite nobody runs twice, and the probe drives nothing with it anyway.</param>
    public void Attach(SandboxAvatar target, bool captureMouse)
    {
        _target = target;
        Position = target.Proportions.AimAnchorLocal;
        _camera = new Camera3D
        {
            Name = "Lens",
            Current = true,
            Fov = DefaultFovDeg,
            Near = NearPlaneM,
            // Deliberately no CameraAttributes: SandboxCamera's near-field DOF blur starts at
            // 0.35 m, which in first person is the hands and everything they carry.
        };
        AddChild(_camera);
        // The avatar's own body must not be rendered into its own eyes. Done as a cull mask over
        // a dedicated render layer, so every OTHER peer's camera — and the spectate rig — still
        // draws this body and its nameplate normally.
        _camera.SetCullMaskValue(AvatarVisual.FirstPersonHiddenLayer, false);
        target.HideOwnBodyFromFirstPerson();
        Local = this;
        ApplyLookBasis();
        if (captureMouse)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree()
    {
        if (Local == this)
            Local = null;
    }

    /// <summary>
    /// <b>Throw the view</b> (DOOR-1): a transient rotational offset of
    /// <paramref name="degrees"/> decaying over <paramref name="seconds"/>, on the shape
    /// <see cref="MpFoundation.Game.Round.StartleTimeline.KickPitchDegAt"/> defines. Restarts
    /// rather than accumulating — two kicks inside one window is one event with a double
    /// amplitude, not two flinches, and a player who was kicked twice should be no harder to aim
    /// than one who was kicked once.
    ///
    /// <para><b>It does not touch <see cref="Yaw"/> or <see cref="Pitch"/>.</b> See the fields it
    /// writes for why that is load-bearing rather than tidy.</para>
    /// </summary>
    public void Kick(float degrees, float seconds)
    {
        if (!(seconds > 0f) || degrees == 0f)
            return;
        _kickDeg = degrees;
        _kickSec = seconds;
        _kickElapsed = 0.0;
    }

    /// <summary>Test/replay hook: set the look angles directly (pitch still clamped). Mirrors
    /// <see cref="SandboxCamera.SetOrbit"/>.</summary>
    public void SetLook(float yaw, float pitch)
    {
        Yaw = yaw;
        _pitch = Mathf.Clamp(pitch, SandboxCamera.PitchMin, SandboxCamera.PitchMax);
        ApplyLookBasis();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (HandlesPauseToggle && @event.IsActionPressed("pause"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
            return;
        }

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Sign convention and sensitivity are SandboxCamera's own, read off it rather than
            // retyped: a second copy of 0.0025 is a second thing to keep in step with a settings
            // slider that does not exist yet.
            Yaw -= motion.Relative.X * SandboxCamera.MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * SandboxCamera.MouseSensitivity,
                SandboxCamera.PitchMin, SandboxCamera.PitchMax);
        }
    }

    /// <summary>Per RENDER frame, not per physics tick: mouse look must move at the rate the
    /// player's screen moves, and the position comes free from parentage.</summary>
    public override void _Process(double delta)
    {
        // The kick advances on the RENDER clock, like the look itself: it is a thing the screen
        // does, and a 0.3 s shake sampled at the 60 Hz physics rate on a 144 Hz display would
        // step rather than sweep. Advanced before the early-out so a kick still expires on a
        // frame where the target has gone.
        AdvanceKick(delta);
        if (_target == null || !GodotObject.IsInstanceValid(_target))
            return;
        // Read the eyeline live rather than caching it at Attach: an avatar re-measures its own
        // body whenever its appearance rebuilds, which for a networked spawn lands a beat AFTER
        // the camera attaches (the avatar-key synchronizer catching up to the authority's pick).
        // Same reasoning as SandboxCamera.FocusHeight.
        Position = _target.Proportions.AimAnchorLocal;
        ApplyLookBasis();
    }

    /// <summary>Cancels the body's yaw and writes the look. <c>Basis.FromEuler</c>'s default YXZ
    /// order is <c>Ry(yaw) · Rx(pitch)</c>, which is exactly what
    /// <c>AimQuery.DirectionFromYawPitch</c> builds and what <c>SandboxCamera</c>'s pivot uses —
    /// so the rendered view, the replicated aim angles and the server's aim ray are one
    /// construction, not three that agree.</summary>
    private void ApplyLookBasis() =>
        GlobalBasis = Basis.FromEuler(
            new Vector3(_pitch + Mathf.DegToRad(KickPitchOffsetDeg), Yaw, 0f));

    /// <summary>Steps the kick and recomputes its offset. Deliberately NOT clamped into the
    /// pitch limits: the limits exist so a legitimate LOOK can never be an illegal aim, and this
    /// offset is not a look — it never reaches <c>MoveIntent</c>, so there is nothing for the
    /// motor's <c>SanitizeAimPitch</c> to disagree with. Clamping it would also silently swallow
    /// the kick exactly when the player happens to be looking straight up, which is the one case
    /// where "the door went off behind me" most needs to read.</summary>
    private void AdvanceKick(double delta)
    {
        if (_kickSec <= 0f)
            return;
        _kickElapsed += delta;
        if (_kickElapsed >= _kickSec)
        {
            _kickSec = 0f;
            _kickDeg = 0f;
            KickPitchOffsetDeg = 0f;
            return;
        }
        KickPitchOffsetDeg = MpFoundation.Game.Round.StartleTimeline.KickPitchDegAt(
            _kickElapsed,
            MpFoundation.Game.Round.StartleTuning.Current with { KickDegrees = _kickDeg, KickSec = _kickSec });
    }
}
