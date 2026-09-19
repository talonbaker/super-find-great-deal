using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The bike camera's arithmetic, pure</b> (BIKE-2x-C, 2026-09-02). Every function here takes
/// values and returns values; nothing reads a node, a clock or <c>BikeCameraTuning.Current</c>.
/// That is what lets <c>tests/unit/BikeCameraTests.cs</c> run it without an engine, and it is the
/// same split <see cref="BikeRig"/> already keeps against <see cref="BikeLayer"/>:
/// <see cref="BikeCamera"/> decides WHEN and against WHAT, this decides WHAT BY HOW MUCH.
/// </summary>
public static class BikeCameraRig
{
    /// <summary>Floor on any lag rate, 1/s. A divisor's cousin: at or below zero the lag would
    /// either freeze or invert, and a frozen channel is indistinguishable from a broken one.</summary>
    private const float MinRate = 1e-4f;

    /// <summary>Floor on the shaping exponent. A zero or negative exponent turns
    /// <c>Pow(x, e)</c> into a constant or a divergence at x = 0, either of which would make a
    /// standstill report full speed.</summary>
    private const float MinExponent = 0.01f;

    /// <summary>
    /// <b>The one number every channel reads: ride speed, normalised and shaped.</b> 0 at a
    /// standstill, 1 at (and above) the ride cap, clamped at both ends, with
    /// <see cref="BikeCameraTuning.SpeedCurveExponent"/> applied.
    ///
    /// <para>The cap is the RIDE cap — <c>BikeRig.RideCapMps</c>, 9.42 m/s at the shipped
    /// tunings — and not <c>LocomotionProfile.SpeedCue01</c>'s foot sprint. That single choice is
    /// most of why this class exists: the shipped cue saturates at 6.08 m/s, which a mounted body
    /// passes at 65 % of what it can reach, and a camera reading a saturated cue has nothing left
    /// to say for the whole top third of the bike's range.</para>
    ///
    /// <para>A cap at or below zero returns 0 rather than dividing: a lab that has somehow zeroed
    /// its own ride cap should get a camera that sits still, not one that reports infinity.</para>
    /// </summary>
    public static float Speed01(float speedMps, float rideCapMps, in BikeCameraTuning c)
    {
        if (!float.IsFinite(speedMps) || !float.IsFinite(rideCapMps) || rideCapMps <= 1e-4f)
            return 0f;
        float x = Mathf.Clamp(speedMps / rideCapMps, 0f, 1f);
        float e = Mathf.Max(c.SpeedCurveExponent, MinExponent);
        // Exactly 1 is the common case and Pow(x, 1) is not bit-exactly x on every runtime; the
        // branch keeps a linear curve provably linear, which the pins below rely on.
        return e == 1f ? x : Mathf.Pow(x, e);
    }

    /// <summary>The lens the bike wants at this speed, degrees — a TOTAL, base plus the extra at
    /// the cap. Reads only the two FOV rows.</summary>
    public static float FovFor(float speed01, in BikeCameraTuning c)
        => c.FovBaseDeg + c.FovAtCapDeg * Mathf.Clamp(speed01, 0f, 1f);

    /// <summary>The EXTRA orbit length the bike wants at this speed, metres — added to whatever
    /// <c>SandboxCamera</c>'s own arm placed, never a total. Reads only the two distance
    /// rows.</summary>
    public static float DistanceFor(float speed01, in BikeCameraTuning c)
        => c.DistanceBaseM + c.DistanceAtCapM * Mathf.Clamp(speed01, 0f, 1f);

    /// <summary>How far along the heading the camera aims past the body at this speed, metres.
    /// <b>Reads only the two look-ahead rows</b> — no shared curve, nothing derived from the FOV or
    /// the distance — which is the independence this packet was written for and which
    /// <c>BikeCameraTests</c> pins by rewriting the other rows and asserting this output does not
    /// move a bit.</summary>
    public static float LookAheadFor(float speed01, in BikeCameraTuning c)
        => c.LookAheadBaseM + c.LookAheadAtCapM * Mathf.Clamp(speed01, 0f, 1f);

    /// <summary>The EXTRA lens height the bike wants at this speed, metres of world up, added to
    /// where the shipped rig placed the lens. Reads only the two height rows.</summary>
    public static float HeightFor(float speed01, in BikeCameraTuning c)
        => c.HeightBaseM + c.HeightAtCapM * Mathf.Clamp(speed01, 0f, 1f);

    /// <summary>
    /// <b>One first-order lag with two rates, picked by which way the value is moving.</b>
    /// <paramref name="inRate"/> is used when the target is ABOVE the current value (the direction
    /// of more FOV / further back / more lead / higher); <paramref name="outRate"/> on the return.
    ///
    /// <para><b>The form is <c>1 - exp(-rate x dt)</c> and that is not decoration.</b> A raw
    /// <c>lerp(a, b, rate x dt)</c> is frame-rate DEPENDENT — ten steps of 10 ms do not land where
    /// one step of 100 ms lands, because the per-step fraction is linear in dt while the decay it
    /// approximates is exponential. This lab runs its physics at a fixed 60 Hz and its render at
    /// whatever the machine gives, and this camera runs on the RENDER clock (see
    /// <see cref="BikeCamera"/>'s class doc for why), so a channel damped the naive way would
    /// literally feel different on a 60 fps machine and a 144 fps one. The exponential form
    /// composes exactly — <c>exp(-r x 0.01)^10 = exp(-r x 0.1)</c> — and
    /// <c>BikeCameraTests</c> pins it.</para>
    ///
    /// <para>The step fraction is in [0, 1] for any non-negative rate and dt, so the result is
    /// always between <paramref name="current"/> and <paramref name="target"/>: this cannot
    /// overshoot, and it converges monotonically. Both are pinned.</para>
    /// </summary>
    public static float Damp(float current, float target, float inRate, float outRate, float dt)
    {
        if (!(dt > 0f) || !float.IsFinite(current) || !float.IsFinite(target))
            return current;
        float rate = target > current ? inRate : outRate;
        if (!(rate > MinRate))
            return current;
        return current + (target - current) * (1f - Mathf.Exp(-rate * dt));
    }

    /// <summary>
    /// <b>One tick of the extra push against geometry.</b> <paramref name="blockedM"/> is the most
    /// push the sweep will allow this tick; <paramref name="desiredM"/> is what the speed channels
    /// asked for.
    ///
    /// <para><b>Pull-in is instant and undamped, ease-out is graded</b> — the same asymmetry
    /// <c>SandboxCamera</c> states for its own arm: <i>"CONTRACTION stays instant - the camera must
    /// never spend a frame inside whatever just crossed the arm"</i>. So a blocked sweep returns
    /// <paramref name="blockedM"/> exactly, this tick, with no lag of any kind; anything else eases
    /// toward <c>min(desired, blocked)</c> at <paramref name="easeOutRate"/>, which is what stops
    /// an occluder leaving the path from teleporting the view a metre outward in one frame.</para>
    /// </summary>
    public static float CollisionStep(float currentM, float desiredM, float blockedM,
        float easeOutRate, float dt)
    {
        if (blockedM < currentM)
            return blockedM;
        float target = Mathf.Min(desiredM, blockedM);
        if (!(dt > 0f) || !(easeOutRate > MinRate))
            return currentM;
        return currentM + (target - currentM) * (1f - Mathf.Exp(-easeOutRate * dt));
    }

    /// <summary>
    /// <b>How much of this layer's contribution is showing.</b> <paramref name="blend01"/> is
    /// <c>BikeLayer.Blend</c> — 0 on foot, 1 fully mounted, walking between over
    /// <c>MountBlendSec</c> / <c>DismountBlendSec</c>.
    ///
    /// <para>At 0 it returns <paramref name="undecorated"/> <b>exactly</b>, by branch rather than
    /// by arithmetic. That is deliberate: the whole safety argument for decorating a shipped camera
    /// is that at blend 0 the frame is byte-identical to the one <c>SandboxCamera</c> alone
    /// produced, and "a lerp with weight zero is probably the identity" is not the same claim as
    /// "this returns the input".</para>
    /// </summary>
    public static float Blend(float undecorated, float decorated, float blend01)
    {
        if (!(blend01 > 0f))
            return undecorated;
        if (blend01 >= 1f)
            return decorated;
        return undecorated + (decorated - undecorated) * blend01;
    }

    /// <summary>
    /// <b>The lens the bike camera actually writes</b>, given what the shipped rig wrote this
    /// frame and what the bike wants.
    ///
    /// <para><b>A mount never takes field of view away.</b> The shipped speed cue has already
    /// earned whatever width it is showing, and the bike's curve is normalised against a longer
    /// axis — so at a mid speed the bike's own number can sit BELOW the shipped one (at 6.08 m/s
    /// the shipped cue is saturated at 89 degrees while the bike's is 86.6), and mixing straight
    /// toward it would narrow the lens as the rider got faster. That is a speed cue running
    /// backwards. Taking the max first removes the case entirely, and it does not cost the blend
    /// its continuity: <c>max</c> is continuous, and at blend 0 the whole expression is still
    /// exactly the shipped value.</para>
    /// </summary>
    public static float FovMix(float shippedDeg, float bikeDeg, float blend01)
        => Blend(shippedDeg, Mathf.Max(shippedDeg, bikeDeg), blend01);
}

/// <summary>
/// <b>A third-person bike camera that decorates the shipped rig instead of replacing it</b>
/// (BIKE-2x-C, 2026-09-02). Lab-only: it lives beside <see cref="BikeLayer"/> in the movement
/// playground, it touches no shipped file, and <c>SandboxCamera</c> is not modified by a line.
///
/// <para><b>It writes NOTHING back into movement.</b> No velocity, no <c>MoveIntent</c>, no
/// <c>MotorTuning</c>, no <see cref="BikeTuning"/>, no field on the avatar. It reads the body's
/// velocity and facing and it writes a <c>Camera3D</c>'s position, basis and FOV. That is the whole
/// of its outward surface, and it is what makes "the camera cannot change how the bike rides" a
/// structural claim rather than a careful one — the same argument <c>SandboxCamera</c> makes for
/// its own landing dip.</para>
///
/// <para><b>Why decorate rather than replace.</b> <c>SandboxCamera</c> owns a great deal that has
/// been played and approved — the <c>SpringArm3D</c> sphere sweep, the analytic floor bound at high
/// look angles, the focus-clamp ray, the own-body cull, the aim zoom, the teleport cut. Rebuilding
/// any of that for a prototype would be trading a proven rig for an unproven one to answer a
/// question about bikes. So this runs after it and adds four things it cannot know about: a FOV, an
/// orbit push, a lens rise and a re-aim, all four normalised against the RIDE cap that the shipped
/// speed cue saturates well below (see <see cref="BikeCameraTuning"/>).</para>
///
/// <para><b>The two-writer problem, and exactly how it is handled — this repo has been bitten by a
/// second transform writer before.</b> <c>SandboxCamera</c> rewrites <c>CameraNode.Position</c>,
/// the pivot's rotation and <c>CameraNode.Fov</c> every PHYSICS tick, so writing after it and
/// deriving from what it wrote does not compound. But this camera runs on <c>_Process</c>, and a
/// render frame with no physics tick under it would otherwise have this layer decorating its own
/// previous write — twice at 120 Hz, three times at 180. So the shipped values are latched once per
/// PHYSICS frame (<c>Engine.GetPhysicsFrames()</c> is the discriminator, which is exact and free)
/// and every frame derives from that latch, never from the live node. Two consequences worth
/// stating:
/// <list type="bullet">
/// <item><b>Position and basis have no feedback at all.</b> <c>SandboxCamera</c> derives
/// <c>_camera.Position</c> from its own <c>_renderedArm</c> and the pivot's rotation from its own
/// yaw and pitch; it never reads the node back. And <c>LookAt</c> is absolute — it depends on the
/// position and the aim point, not on the prior basis — so re-aiming twice in one frame lands in
/// the same place as re-aiming once.</item>
/// <item><b>The FOV is one real coupling and it is named rather than hidden.</b>
/// <c>SandboxCamera</c>'s line is <c>Fov = MoveToward(Fov, target, rate x dt)</c> — it reads the
/// field back as its own state, so our write is an input to it. Because our write is an ABSOLUTE
/// target and never a delta, that loop has exactly one fixed point (our own target) and reaches it
/// monotonically; it cannot run away. On release the blend carries our contribution to zero, we
/// stop writing at all, and the shipped <c>MoveToward</c> walks the lens back to its own number at
/// its own rate.</item>
/// </list></para>
///
/// <para><b>Active only while there is a bike.</b> <c>bike.Mounted || bike.Blend &gt; 0</c>, and
/// the entire contribution is scaled by <c>bike.Blend</c>, so mount and dismount are continuous. At
/// <c>Blend == 0</c> this method returns before writing anything — byte-identity with the
/// undecorated frame is therefore an early return rather than an arithmetic coincidence.</para>
///
/// <para>Inert and safe before <see cref="Bind"/>, with a freed avatar or camera, with a detached
/// <c>SandboxCamera</c>, and with no <c>CameraNode</c> yet.</para>
/// </summary>
public partial class BikeCamera : Node3D
{
    /// <summary>Where in the frame's process order this node writes. Above anything the lab
    /// registers by default, so the lens is decorated after every other per-frame writer has had
    /// its say. The ordering that actually matters is against the PHYSICS tick, not against other
    /// process nodes — see the class doc's latch — but a high priority makes the intent
    /// legible.</summary>
    public const int WriteAfterEverythingPriority = 200;

    private SandboxCamera? _camera;
    private BikeLayer? _bike;
    private SandboxAvatar? _avatar;

    // --- the four damped channels, in the units their rows are written in ----------------------
    private float _fovDeg;
    private float _distanceM;
    private float _lookAheadM;
    private float _heightM;
    private bool _seeded;

    /// <summary>The push actually rendered this frame, metres along the push direction — the
    /// collision channel's own state, kept apart from the four speed channels because it is graded
    /// one way and instant the other.</summary>
    private float _renderedPushM;

    /// <summary>The swept probe, built once instead of per frame — the same hoist
    /// <c>SandboxCamera._focusQuery</c> made after this repo's 2026-08-07 perf audit, and it
    /// matters more here because this runs on the RENDER clock rather than the physics one. Only
    /// <c>Transform</c> and <c>Motion</c> are rewritten per frame; <c>Exclude</c> is re-ASSIGNED at
    /// <see cref="Bind"/> rather than mutated in place, because the engine snapshots the exclusion
    /// set at assignment time and mutating the array behind its back would leave the sweep still
    /// excluding whatever body was bound before.</summary>
    private readonly SphereShape3D _probe = new() { Radius = 0.30f };

    private readonly Godot.Collections.Array<Rid> _excludeRids = new();

    private readonly PhysicsShapeQueryParameters3D _shapeQuery = new()
    {
        CollisionMask = 1,          // world geometry, the same mask the shipped spring arm sweeps
        CollideWithBodies = true,
        CollideWithAreas = false,
    };

    // --- the once-per-physics-frame latch of what the shipped rig wrote ------------------------
    private ulong _latchedFrame = ulong.MaxValue;
    private Vector3 _shippedLensPos;
    private Vector3 _shippedBackDir = Vector3.Back;
    private float _shippedFovDeg;

    /// <summary>The FOV the bike channel is asking for right now, degrees — the damped channel, not
    /// what was written (the write also takes the max against the shipped lens; see
    /// <see cref="BikeCameraRig.FovMix"/>).</summary>
    public float FovNow => _fovDeg;

    /// <summary>The EXTRA orbit length the bike channel is asking for right now, metres.</summary>
    public float DistanceNow => _distanceM;

    /// <summary>How far along the heading the camera is aiming past the body right now,
    /// metres.</summary>
    public float LookAheadNow => _lookAheadM;

    /// <summary>The EXTRA lens height the bike channel is asking for right now, metres.</summary>
    public float HeightNow => _heightM;

    /// <summary>How much of the wanted push geometry took away this frame, metres. 0 in the open;
    /// equal to the whole wanted push when the lens is against a wall.</summary>
    public float CollisionPullInM { get; private set; }

    /// <summary>Whether this camera contributed anything on the last frame it ran — bound, mounted
    /// or still blending out, and with a lens to write to.</summary>
    public bool Active { get; private set; }

    /// <summary>
    /// <b>Hand this camera the three things it reads.</b> Called by the playground's wiring; the
    /// node does nothing at all until it has been. Idempotent, and re-binding re-seeds the channels
    /// so a swapped avatar does not inherit the previous one's damping state.
    /// </summary>
    public void Bind(SandboxCamera camera, BikeLayer bike, SandboxAvatar avatar)
    {
        _camera = camera;
        _bike = bike;
        _avatar = avatar;
        _seeded = false;
        _renderedPushM = 0f;
        _latchedFrame = ulong.MaxValue;
        ProcessPriority = WriteAfterEverythingPriority;

        // Re-assigned, not mutated: see _shapeQuery's doc comment. A re-bind to a different body
        // has to push a rebuilt set through, or the sweep would go on excluding the old one and
        // start colliding with the new one's own capsule.
        _shapeQuery.Shape = _probe;
        _excludeRids.Clear();
        _excludeRids.Add(avatar.GetRid());
        _shapeQuery.Exclude = _excludeRids;
    }

    public override void _Ready() => ProcessPriority = WriteAfterEverythingPriority;

    public override void _Process(double delta)
    {
        Active = false;
        CollisionPullInM = 0f;

        if (_camera is null || _bike is null || _avatar is null)
            return;
        if (!GodotObject.IsInstanceValid(_camera) || !GodotObject.IsInstanceValid(_avatar))
            return;
        // A detached shipped camera does not place the lens this tick, so the latch below would
        // read our own previous write back as if it were the shipped one and we would compound.
        // Being inert is the correct answer to "nobody is driving the lens" anyway.
        if (_camera.Detached)
            return;
        Camera3D? lens = _camera.CameraNode;
        if (lens is null || !GodotObject.IsInstanceValid(lens))
            return;

        float dt = (float)delta;
        BikeCameraTuning c = BikeCameraTuning.Current;

        // The ride cap comes from the FOOT tuning, which is what BikeLayer captured at the mount:
        // while mounted MotorTuning.Current IS the ride tuning, so asking RideCapMps about it
        // would multiply the ride speed in twice and report a cap a mounted body can never reach.
        MotorTuning foot = _bike.FootTuning ?? MotorTuning.Current;
        float rideCap = BikeRig.RideCapMps(foot, BikeTuning.Current);

        Vector3 velocity = _avatar.Velocity;
        float groundSpeed = new Vector2(velocity.X, velocity.Z).Length();
        float speed01 = BikeCameraRig.Speed01(groundSpeed, rideCap, c);

        // Seed on the first frame after a bind rather than in _Ready: BikeCameraTuning.Current may
        // have been replaced between the two, and a channel that starts at zero would spend its
        // whole first mount climbing out of a hole nothing asked for.
        if (!_seeded)
        {
            _fovDeg = BikeCameraRig.FovFor(speed01, c);
            _distanceM = BikeCameraRig.DistanceFor(speed01, c);
            _lookAheadM = BikeCameraRig.LookAheadFor(speed01, c);
            _heightM = BikeCameraRig.HeightFor(speed01, c);
            _seeded = true;
        }

        // The four channels advance every frame, mounted or not. They are pure arithmetic and cost
        // nothing, and keeping them warm means a mount taken at speed starts from the value the
        // speed already justifies rather than snapping up from a rest value it left long ago.
        _fovDeg = BikeCameraRig.Damp(_fovDeg, BikeCameraRig.FovFor(speed01, c),
            c.FovInRate, c.FovOutRate, dt);
        _distanceM = BikeCameraRig.Damp(_distanceM, BikeCameraRig.DistanceFor(speed01, c),
            c.DistanceInRate, c.DistanceOutRate, dt);
        _lookAheadM = BikeCameraRig.Damp(_lookAheadM, BikeCameraRig.LookAheadFor(speed01, c),
            c.LookAheadInRate, c.LookAheadOutRate, dt);
        _heightM = BikeCameraRig.Damp(_heightM, BikeCameraRig.HeightFor(speed01, c),
            c.HeightInRate, c.HeightOutRate, dt);

        float blend = Mathf.Clamp(_bike.Blend, 0f, 1f);
        Active = _bike.Mounted || blend > 0f;

        // THE BYTE-IDENTITY GUARANTEE, as an early return. On foot the lens is left exactly as
        // SandboxCamera placed it because nothing here touches it — not "multiplied by zero", not
        // "lerped with weight zero". The channels above have already advanced, so a mount an
        // instant from now still starts warm.
        if (!(blend > 0f))
        {
            _renderedPushM = 0f;
            return;
        }

        LatchShipped(lens);

        // --- 1. the lens ----------------------------------------------------------------------
        lens.Fov = BikeCameraRig.FovMix(_shippedFovDeg, _fovDeg, blend);

        // --- 2. the push: further back along the shipped frame's +Z, and higher in world up -----
        // Both are EXTRAS on the shipped placement and both are scaled by the blend before the
        // sweep sees them, so what geometry is asked to allow is what will actually be rendered.
        Vector3 push = _shippedBackDir * (_distanceM * blend) + Vector3.Up * (_heightM * blend);
        float wantPushM = push.Length();
        Vector3 pushDir = wantPushM > 1e-4f ? push / wantPushM : _shippedBackDir;

        float blockedM = BlockedPushM(_shippedLensPos, pushDir, wantPushM, c);
        _renderedPushM = BikeCameraRig.CollisionStep(_renderedPushM, wantPushM, blockedM,
            c.CollisionEaseOutRate, dt);
        CollisionPullInM = Mathf.Max(0f, wantPushM - _renderedPushM);
        lens.GlobalPosition = _shippedLensPos + pushDir * _renderedPushM;

        // --- 3. the re-aim --------------------------------------------------------------------
        // Done AFTER the position write, because LookAt is absolute: it points the lens from where
        // the lens now is, and re-running it on an already-aimed camera lands in the same place.
        Vector3 focus = FocusPoint();
        Vector3 heading = BikeRig.Heading(velocity, BikeLayer.FacingOf(_avatar));
        Vector3 aim = focus + heading * (_lookAheadM * blend);
        Vector3 toAim = aim - lens.GlobalPosition;
        float reach = toAim.Length();
        // LookAt throws on a zero-length direction (aiming at your own position) and on one
        // parallel to the up vector (the basis it would build is degenerate). Both are reachable
        // here — a lens pushed to exactly the aim point, or a straight-down look with no lead — so
        // both are refused rather than caught, and the frame simply keeps the aim SandboxCamera
        // gave it for that tick.
        if (reach > 1e-3f && Mathf.Abs(toAim.Y / reach) < 0.999f)
            lens.LookAt(aim, Vector3.Up);
    }

    /// <summary>
    /// <b>Latch what the shipped rig wrote, once per physics frame.</b> See the class doc: this is
    /// the whole of the two-writer defence. <c>SandboxCamera</c> places the lens in
    /// <c>_PhysicsProcess</c>, and Godot runs every physics step of a frame before that frame's
    /// process step, so on any frame whose physics counter has moved the node currently holds the
    /// shipped values — and on any frame whose counter has NOT moved it holds ours, which must be
    /// ignored or this layer would decorate its own output.
    /// </summary>
    private void LatchShipped(Camera3D lens)
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (frame == _latchedFrame)
            return;
        _latchedFrame = frame;
        _shippedLensPos = lens.GlobalPosition;
        _shippedFovDeg = lens.Fov;
        Vector3 back = lens.GlobalBasis.Z;
        _shippedBackDir = back.LengthSquared() > 1e-8f ? back.Normalized() : Vector3.Back;
    }

    /// <summary>
    /// <b>The point the camera frames</b> — the avatar's rendered position plus the shipped rig's
    /// own focus height, which is the same quantity <c>SandboxCamera</c> builds its target focus
    /// from and is read off it rather than restated (<c>FocusHeight</c> is derived from the
    /// character's measured proportions and moves when the body does).
    ///
    /// <para>Deliberately the UN-lagged focus. <c>SandboxCamera</c>'s lagged <c>_focus</c> is
    /// private, and it is also the wrong input for a look-ahead: leading from a focus that is
    /// itself trailing the body would fold the follow lag into the lead and make the two channels
    /// impossible to tune apart.</para>
    /// </summary>
    private Vector3 FocusPoint()
        => _avatar!.RenderGlobalPosition + Vector3.Up * _camera!.FocusHeight;

    /// <summary>
    /// <b>How much of the wanted push geometry will allow</b>, metres. A sphere of
    /// <see cref="BikeCameraTuning.CollisionRadiusM"/> is swept from the focus point out to where
    /// the pushed lens wants to be, with the avatar's own body excluded.
    ///
    /// <para><b>The result can only ever reduce the push, never pull the lens in past where the
    /// shipped rig placed it.</b> That is the invariant that makes this safe to run on top of a
    /// proven arm: <c>SandboxCamera</c>'s own <c>SpringArm3D</c> already swept its way to the
    /// current lens position and that position is not this layer's to second-guess. So the sweep's
    /// answer is converted into an allowance on the EXTRA and clamped at zero — a degenerate cast
    /// (an origin that starts inside geometry returns a safe fraction of 0) therefore costs the
    /// extra push and nothing else, instead of dragging the view into the rider's back.</para>
    ///
    /// <para>With no world to query — a node outside the tree, a headless harness with no physics
    /// server — the answer is "nothing is blocking", which is the same thing the shipped rig
    /// assumes when its own cast reports no hit.</para>
    /// </summary>
    private float BlockedPushM(Vector3 lensPos, Vector3 pushDir, float wantPushM,
        in BikeCameraTuning c)
    {
        if (!(wantPushM > 1e-4f) || !IsInsideTree())
            return wantPushM;
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space is null)
            return wantPushM;

        Vector3 focus = FocusPoint();
        Vector3 desiredLens = lensPos + pushDir * wantPushM;
        Vector3 motion = desiredLens - focus;
        if (motion.LengthSquared() < 1e-6f)
            return wantPushM;

        // The probe's radius is a live row, so it is re-read rather than baked at construction —
        // but only WRITTEN when it has moved, because assigning a shape parameter is a marshalled
        // call and this runs on the render clock.
        float radius = Mathf.Max(c.CollisionRadiusM, 0.01f);
        if (!Mathf.IsEqualApprox(_probe.Radius, radius))
            _probe.Radius = radius;
        _shapeQuery.Transform = new Transform3D(Basis.Identity, focus);
        _shapeQuery.Motion = motion;

        float[] fractions = space.CastMotion(_shapeQuery);
        float safe = fractions.Length > 0 ? fractions[0] : 1f;

        float allowedFromFocus = motion.Length() * Mathf.Clamp(safe, 0f, 1f);
        float shippedFromFocus = (lensPos - focus).Length();
        return Mathf.Clamp(allowedFromFocus - shippedFromFocus, 0f, wantPushM);
    }

    /// <summary>The readout line, in the same terse shape <c>BikeLayer.Line()</c> uses: a fixed
    /// eleven-character label, then the state, then the live values, then the one thing that is
    /// only interesting when it is non-zero.</summary>
    public string Line()
    {
        if (_camera is null || _bike is null || _avatar is null)
            return "BIKE-CAM   unbound";
        string mode = Active ? "ON" : "off";
        float blend = Mathf.Clamp(_bike.Blend, 0f, 1f);
        if (blend > 0f && blend < 1f)
            mode += $" (blend {blend:F2})";
        string pull = CollisionPullInM > 0.001f ? $"   PULLED IN {CollisionPullInM:F2} m" : "";
        return $"BIKE-CAM   {mode}   fov {_fovDeg:F1} deg   back +{_distanceM:F2} m"
             + $"   lead {_lookAheadM:F2} m   up +{_heightM:F2} m{pull}";
    }
}
