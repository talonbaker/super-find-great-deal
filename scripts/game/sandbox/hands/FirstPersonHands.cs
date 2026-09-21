using Godot;
using MpFoundation.Game.Props;

namespace MpFoundation.Game.Sandbox.Hands;

/// <summary>
/// <b>THE PLAYER'S OWN HANDS</b> (HANDS-1, 2026-09-20). Two primitive mitten hands hanging off
/// the first-person lens: at rest they sit low in the frame; on a click the near one reaches to
/// whatever is under the crosshair; on a carryable it closes at the grab point and RIDES it —
/// through the wheel's distance, through RMB rotation, through the holder walking — and on a
/// button it pokes and comes back.
///
/// <para><b>Why the hand shows the grab point, and why that is the whole design</b> (program
/// RIDE-1 §2). Lethal Company draws arms in a fixed pose per item, so the hands say <i>what</i>
/// you carry and never <i>where</i> you hold it. R.E.P.O. draws none and implies them with a
/// tether. Both are right for their games and wrong for this one: after FEEL-1 the object is held
/// at the point you grabbed it, and the verb this whole game runs on is putting a specific thing
/// in a specific spot by hand. So the hand's only job is to show that point.</para>
///
/// <para><b>Holder-side only</b> (ruling R5). This node is built where the
/// <see cref="FirstPersonCamera"/> is built — the one body this process drives — so no other peer
/// has one, nothing crosses the wire, and the avatar's existing arm substrate keeps doing what it
/// does for spectators and the CCTV feed. A later packet can drive that arm to the same grab
/// point once SYNC has settled who owns what.</para>
///
/// <para><b>A CHILD OF THE LENS, and it writes its LOCAL transform</b> (SICK-1, 2026-09-20).
/// SICK-1 made the eye interpolate between physics samples for the frame being drawn, so a hand
/// that computed a world pose against last tick's eye would judder against the world by exactly
/// the amount SICK-1 removed from the camera. Two things together prevent that: the node's
/// <c>ProcessPriority</c> puts it after every other <c>_Process</c> in the frame, so the lens has
/// already been written when this runs; and the pose it stores is the world target expressed in
/// the LENS's frame, so even a later move of the lens carries the hands with it rather than
/// leaving them behind.</para>
///
/// <para><b>Nothing here is animated.</b> Straight lerps, one mesh swap for the grip, no IK and
/// no clips — position and roll come out of the grab point and the surface it sits on
/// (<see cref="HandReach"/>). Simple over juice.</para>
/// </summary>
public partial class FirstPersonHands : Node3D
{
    /// <summary><b>The one set of hands in this process</b>, or null where no first-person lens
    /// was built (every headless suite bot, a <c>--spectate-cam</c> run, a dedicated server).
    /// Same static-instance idiom <see cref="FirstPersonCamera.Local"/> uses, and for the same
    /// reason: the presser of a button is a node in the LEVEL with no route to the local avatar
    /// that is not a tree walk.</summary>
    public static FirstPersonHands? Local { get; private set; }

    /// <summary>Where a hand rests when it has nothing to do, in the LENS's own frame: low in the
    /// frame and in from the edge, so it anchors the bottom of the view without covering the
    /// shelf the player is reading.
    ///
    /// <para>Derived against the lens rather than chosen by eye. At 0.45 m the 75° vertical lens
    /// is 0.345 m tall and (16:9) 0.613 m wide, so y = −0.26 puts the hand 75% of the way down
    /// the frame and x = ±0.34 puts it 55% of the way out — visible, peripheral, and clear of the
    /// centre where the crosshair's target is. <b>Still</b>: no bob, no sway, no breathing idle.
    /// SICK-1's law is that the first-person view contains no motion the player did not ask for,
    /// and a pair of hands that is perfectly steady is also a fixed reference the eye can hold,
    /// which is a documented motion-sickness reducer in its own right.</para></summary>
    public static readonly Vector3 IdleLocal = new(0.34f, -0.26f, -0.45f);

    /// <summary>The idle roll: palms down and fingers forward, tilted slightly inward so the pair
    /// frames the view instead of pointing out of it.</summary>
    public const float IdleInwardYawDeg = 14f;

    // --------------------------------------------------------------------------- what it sees

    private SandboxAvatar _avatar = null!;
    private FirstPersonCamera _camera = null!;
    private Node3D _lens = null!;
    private MeshInstance3D _right = null!;
    private MeshInstance3D _left = null!;
    // Built once. The grip is a mesh SWAP, and a swap that allocated a BoxMesh per hand per frame
    // would be 120 native resources a second for a shape that has exactly two states.
    private Mesh _openMesh = null!;
    private Mesh _closedMesh = null!;

    /// <summary>The lens rig these hands hang off. Read by <see cref="HandsSelfTest"/>, which
    /// aims it.</summary>
    public FirstPersonCamera Camera => _camera;

    /// <summary>The positive control (<c>--hands-plant-offset</c>): a sideways error in metres
    /// pushed into every attached hand's target. 0 in every shipped path. A planted 0.08 m makes
    /// <c>Run-HandsSmoke</c> red on the lateral-error bar and green on nothing, which is what
    /// makes that bar worth having.</summary>
    public float PlantOffsetM { get; set; }

    // -------------------------------------------------------------------------- what it is doing

    private enum Gesture { Idle, Hold, Poke }

    private Gesture _gesture = Gesture.Idle;
    private double _elapsed;                 // seconds in the current gesture
    private int _heldPropId = -1;
    private NetworkedProp? _held;
    private bool _twoHanded;
    private bool _rightIsNear = true;
    private Vector3 _propHalfExtentsLocal = Vector3.One * 0.1f;
    private float _propRadiusM = 0.1f;

    // Where each hand was when the current gesture started, in the LENS's frame -- the reach and
    // the return both lerp FROM here, so a hand that was already half way to something does not
    // snap home before setting off.
    private Transform3D _rightFrom = Transform3D.Identity;
    private Transform3D _leftFrom = Transform3D.Identity;

    private Vector3 _pokeTarget;

    // ------------------------------------------------------------------- what the suite reads

    /// <summary>True while at least one hand is attached to a held prop.</summary>
    public bool IsHolding => _gesture == Gesture.Hold && _held != null && IsInstanceValid(_held);

    /// <summary>How many hands are on the held prop: 0, 1 or 2.</summary>
    public int HandsOnProp => IsHolding ? (_twoHanded ? 2 : 1) : 0;

    /// <summary><b>Has the reach finished?</b> False for the ~120 ms a hand spends travelling
    /// from its idle rest to the thing it just grabbed.
    ///
    /// <para>Exposed because a per-frame "the hand is at the grab point" bar must not be judged
    /// while the hand is deliberately somewhere else. Measured on the first real run: the first
    /// seven held frames reported 26.0, 22.4, 18.8, 15.2, 11.6, 8.0, 4.3 cm — a clean linear
    /// decay, which is the lerp doing exactly what it was asked to do and an instrument reading
    /// it as a defect.</para></summary>
    public bool Settled =>
        IsHolding && HandReach.Progress(_elapsed, HandReach.ReachSec) >= 1f;

    /// <summary>The held prop, or null.</summary>
    public NetworkedProp? HeldProp => IsHolding ? _held : null;

    /// <summary>The attached hands' world positions — one entry, or two. Empty when idle. Read by
    /// <see cref="HandsSelfTest"/>, which is the only thing that needs them and which measures
    /// them against the prop rather than against this node's own arithmetic.</summary>
    public Vector3[] AttachedHandPositions()
    {
        if (!IsHolding)
            return System.Array.Empty<Vector3>();
        return _twoHanded
            ? new[] { _right.GlobalPosition, _left.GlobalPosition }
            : new[] { (_rightIsNear ? _right : _left).GlobalPosition };
    }

    /// <summary>How many pokes have been asked for this session. The smoke asserts it is not
    /// zero before believing anything it measured about one.</summary>
    public int PokeCount { get; private set; }

    // ------------------------------------------------------------------------------- lifecycle

    /// <summary>
    /// Build the hands on <paramref name="camera"/>'s lens. Called from
    /// <c>SandboxAvatar.AttachFirstPersonCamera</c>, which is the one place a first-person rig is
    /// mounted — so the human path and the <c>--first-person-cam</c> capture/probe path get the
    /// same hands, and a suite cannot be photographing a second pair built to resemble them.
    /// </summary>
    public static FirstPersonHands Attach(FirstPersonCamera camera, SandboxAvatar avatar)
    {
        var hands = new FirstPersonHands
        {
            Name = nameof(FirstPersonHands),
            _avatar = avatar,
            _camera = camera,
            // AFTER everything else in the frame. FirstPersonCamera writes the interpolated eye in
            // its own _Process; a hand placed before that is a hand placed against the previous
            // tick's eye, which is the judder SICK-1 measured and removed.
            ProcessPriority = 1000,
        };
        Node3D lens = camera.CameraNode;
        hands._lens = lens;
        lens.AddChild(hands);
        hands._right = HandVisual.Build("HandRight");
        hands._left = HandVisual.Build("HandLeft");
        hands.AddChild(hands._right);
        hands.AddChild(hands._left);
        hands._openMesh = HandVisual.Open();
        hands._closedMesh = HandVisual.Closed();
        hands._right.Mesh = hands._openMesh;
        hands._left.Mesh = hands._openMesh;
        hands._right.Transform = IdlePose(+1f);
        hands._left.Transform = IdlePose(-1f);
        // Seed the gesture's FROM poses at rest, so the first frame lerps from where the hands
        // are rather than from the lens origin.
        hands._rightFrom = hands._right.Transform;
        hands._leftFrom = hands._left.Transform;
        Local = hands;
        GD.Print($"[hands] first-person hands on '{avatar.DisplayName}' -- placeholder "
                 + $"{HandVisual.OpenSizeM.X * 100f:0}x{HandVisual.OpenSizeM.Y * 100f:0}x"
                 + $"{HandVisual.OpenSizeM.Z * 100f:0} cm boxes on render layer "
                 + $"{HandVisual.RenderLayer}, arm {HandReach.ArmLengthM:F2} m "
                 + $"(= CarryHold.HoldMaxBaseM), two-hand over {HandReach.OneHandSpanM:F2} m "
                 + $"or {HandReach.OneHandMassKg:F1} kg");
        return hands;
    }

    public override void _ExitTree()
    {
        if (Local == this)
            Local = null;
    }

    /// <summary>
    /// <b>Poke whatever this player just pressed.</b> Called from
    /// <c>RoundControls.ClientRequestPress</c> — the ONE client-side point every press goes
    /// through, a human's click on a <c>RoundButton</c> and a suite's <c>--press</c> alike — so
    /// there is no path that presses a button without the hand moving.
    ///
    /// <para>The target is resolved here rather than passed in, through the avatar's own
    /// aim-aware <c>FindNearestPressable</c>: that is the same pick the press itself resolved
    /// against, so the hand cannot reach for a different button than the one that was pressed. A
    /// press with nothing in reach (a bot firing <c>--press</c> from across the room) moves no
    /// hand, which is correct — there is nothing there to touch.</para>
    /// </summary>
    public void Poke()
    {
        if (_gesture == Gesture.Hold)
            return;                                   // hands full; the press still happens
        if (_avatar.FindNearestPressable() is not { } pressable)
            return;
        _pokeTarget = HandReach.ClampToArm(
            _avatar.AimOriginGlobalPosition, pressable.GlobalPosition, HandReach.ArmLengthM);
        _rightIsNear = IsOnTheRight(_pokeTarget);
        Begin(Gesture.Poke);
        PokeCount++;
    }

    // ----------------------------------------------------------------------------- every frame

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_avatar) || !IsInstanceValid(_lens))
            return;
        _elapsed += delta;

        NetworkedProp? held = CurrentHeld();
        if (held != null)
        {
            if (_gesture != Gesture.Hold || held.PropId != _heldPropId)
                BeginHold(held);
            DriveHold();
            return;
        }

        if (_gesture == Gesture.Hold)
        {
            // Let go -- placed, thrown, taken, or FEEL-1's break-hold giving it up against the
            // world. All four look the same from here and should: the hand opens and comes home,
            // and the slower return is what makes a break read as losing the object.
            _held = null;
            _heldPropId = -1;
            Begin(Gesture.Idle);
        }

        if (_gesture == Gesture.Poke)
        {
            DrivePoke();
            return;
        }

        DriveIdle();
    }

    // ------------------------------------------------------------------------------- the hold

    private NetworkedProp? CurrentHeld()
    {
        // Server-authoritative, so it is ASKED every frame rather than remembered -- the same
        // discipline HeldPropRotator and HoldDistanceController follow, and for the same reasons
        // (a disconnect release, a round reset, a hold broken against a shelf).
        NetworkedProp? held = _avatar.Props?.FindHeldBy(_avatar.OwnerPeerId);
        return held is { SpringActive: true } && IsInstanceValid(held.Body) ? held : null;
    }

    private void BeginHold(NetworkedProp prop)
    {
        _held = prop;
        _heldPropId = prop.PropId;
        _propHalfExtentsLocal = LocalHalfExtents(prop.Body);
        float longest = Mathf.Max(_propHalfExtentsLocal.X,
            Mathf.Max(_propHalfExtentsLocal.Y, _propHalfExtentsLocal.Z)) * 2f;
        _propRadiusM = prop.Body.BoundingRadiusM;
        _twoHanded = HandReach.NeedsTwoHands(longest, prop.Body.MassKg);
        _rightIsNear = IsOnTheRight(prop.GrabPointWorld);
        Begin(Gesture.Hold);
        GD.Print($"[hands] grabbed prop {prop.PropId}: longest axis {longest:F3} m, "
                 + $"{prop.Body.MassKg:F2} kg -> {(_twoHanded ? "TWO" : "ONE")} hand(s), "
                 + $"near hand {(_rightIsNear ? "right" : "left")}");
    }

    private void DriveHold()
    {
        NetworkedProp prop = _held!;
        Carryable body = prop.Body;
        Vector3 centre = body.GlobalPosition;
        Vector3 eye = _avatar.AimOriginGlobalPosition;
        Vector3 toward = eye - centre;
        Vector3 grab = prop.GrabPointWorld;
        float t = HandReach.Progress(_elapsed, HandReach.ReachSec);

        if (_twoHanded)
        {
            Vector3 across = HandReach.AcrossView(toward, Vector3.Up);
            float halfSpan = HandReach.SupportHalfExtent(
                body.GlobalBasis, _propHalfExtentsLocal, across);
            (Vector3 a, Vector3 b) = HandReach.StraddlePoints(
                centre, grab, toward, Vector3.Up, halfSpan, HandReach.PalmProudM);
            Vector3 fingers = -toward;                      // palms facing each other, fingers away
            float reach = HandReach.HoldReachM(_propRadiusM);
            Place(_right, a + across * PlantOffsetM, across, fingers, eye, reach, closed: true, t, _rightFrom);
            Place(_left, b + across * PlantOffsetM, -across, fingers, eye, reach, closed: true, t, _leftFrom);
            return;
        }

        Vector3 outward = toward.LengthSquared() > 1e-10f ? toward.Normalized() : Vector3.Back;
        Vector3 surface = HandReach.SurfacePoint(centre, grab, body.BoundingRadiusM, toward)
                          + outward * HandReach.PalmProudM;
        Vector3 sideways = HandReach.AcrossView(toward, Vector3.Up);
        // Fingers up the near face. outward x across is UP for this pair (Z x X = Y); the other
        // order is down, and a hand that gripped a can from above reads as a claw.
        Vector3 up = outward.Cross(sideways).Normalized();
        MeshInstance3D near = _rightIsNear ? _right : _left;
        MeshInstance3D far = _rightIsNear ? _left : _right;
        Place(near, surface + sideways * PlantOffsetM, outward, up, eye,
            HandReach.HoldReachM(_propRadiusM), closed: true, t,
            _rightIsNear ? _rightFrom : _leftFrom);
        // The idle hand goes home on the same clock, so a one-handed grab does not leave the other
        // hand hanging wherever the last gesture left it.
        far.Transform = LerpPose(_rightIsNear ? _leftFrom : _rightFrom,
            IdlePose(_rightIsNear ? -1f : +1f), HandReach.Progress(_elapsed, HandReach.ReturnSec));
        SetGrip(far, closed: false);
    }

    // -------------------------------------------------------------------------------- the poke

    private void DrivePoke()
    {
        double dwellEnds = HandReach.ReachSec + HandReach.PokeDwellSec;
        MeshInstance3D near = _rightIsNear ? _right : _left;
        Transform3D from = _rightIsNear ? _rightFrom : _leftFrom;

        // ONE parameter for the whole gesture: out, dwell, back. Reading the hand's own current
        // pose as the return's start would re-seed the lerp every frame and turn a straight
        // return into an ease nobody asked for.
        float k = _elapsed < HandReach.ReachSec
            ? HandReach.Progress(_elapsed, HandReach.ReachSec)
            : _elapsed < dwellEnds
                ? 1f
                : 1f - HandReach.Progress(_elapsed - dwellEnds, HandReach.ReturnSec);

        Vector3 eye = _avatar.AimOriginGlobalPosition;
        Vector3 outward = eye - _pokeTarget;
        Vector3 outwardN = outward.LengthSquared() > 1e-10f ? outward.Normalized() : Vector3.Back;
        Vector3 up = outwardN.Cross(HandReach.AcrossView(outward, Vector3.Up)).Normalized();
        var world = new Transform3D(HandReach.Orient(outwardN, up),
            HandReach.ClampToArm(eye, _pokeTarget, HandReach.ArmLengthM));
        // A poke keeps the hand OPEN -- an extended finger, not a fist. The grip swap is what says
        // "I have hold of this", and a button is not held.
        near.Transform = LerpPose(from, ToLens(world), k);
        SetGrip(near, closed: false);
        DriveOther(near);

        if (k <= 0f && _elapsed > dwellEnds)
            Begin(Gesture.Idle);
    }

    // -------------------------------------------------------------------------------- the idle

    private void DriveIdle()
    {
        float t = HandReach.Progress(_elapsed, HandReach.ReturnSec);
        _right.Transform = LerpPose(_rightFrom, IdlePose(+1f), t);
        _left.Transform = LerpPose(_leftFrom, IdlePose(-1f), t);
        SetGrip(_right, closed: false);
        SetGrip(_left, closed: false);
    }

    private void DriveOther(MeshInstance3D near)
    {
        MeshInstance3D other = near == _right ? _left : _right;
        other.Transform = LerpPose(near == _right ? _leftFrom : _rightFrom,
            IdlePose(near == _right ? -1f : +1f), HandReach.Progress(_elapsed, HandReach.ReturnSec));
        SetGrip(other, closed: false);
    }

    // ------------------------------------------------------------------------------- plumbing

    private void Begin(Gesture gesture)
    {
        _gesture = gesture;
        _elapsed = 0.0;
        _rightFrom = _right.Transform;
        _leftFrom = _left.Transform;
    }

    /// <summary>Writes one hand's pose: the world target, brought inside the arm, turned into the
    /// LENS's frame and lerped from where the gesture started. <b>Local rather than global, on
    /// purpose</b> — see the class doc: a hand that stored a world pose would judder against the
    /// interpolated eye SICK-1 shipped.</summary>
    private void Place(MeshInstance3D hand, Vector3 target, Vector3 palmNormal, Vector3 fingerDir,
        Vector3 eye, float reachM, bool closed, float t, Transform3D from)
    {
        Vector3 reachable = HandReach.ClampToArm(eye, target, reachM);
        var world = new Transform3D(HandReach.Orient(palmNormal, fingerDir), reachable);
        hand.Transform = LerpPose(from, ToLens(world), t);
        SetGrip(hand, closed);
    }

    /// <summary>Open or closed. Assigned only when it CHANGES: the property write is cheap but
    /// it is not free, and this runs twice a frame for the life of the session.</summary>
    private void SetGrip(MeshInstance3D hand, bool closed)
    {
        Mesh want = closed ? _closedMesh : _openMesh;
        if (hand.Mesh != want)
            hand.Mesh = want;
    }

    private Transform3D ToLens(Transform3D world) => _lens.GlobalTransform.AffineInverse() * world;

    private static Transform3D LerpPose(Transform3D from, Transform3D to, float t) =>
        new(from.Basis.Slerp(to.Basis, t).Orthonormalized(),
            HandReach.Lerp(from.Origin, to.Origin, t));

    private static Transform3D IdlePose(float side) =>
        new(new Basis(Vector3.Up, Mathf.DegToRad(-side * IdleInwardYawDeg)),
            new Vector3(side * IdleLocal.X, IdleLocal.Y, IdleLocal.Z));

    private bool IsOnTheRight(Vector3 worldPoint) =>
        (worldPoint - _lens.GlobalPosition).Dot(_lens.GlobalBasis.X) >= 0f;

    /// <summary>
    /// <b>The prop's own half-extents in its own frame</b>, from its meshes — so the two hands
    /// land on a crate's actual side faces and the two-hand rule is asked about the object's real
    /// longest axis.
    ///
    /// <para>Read here rather than exposed on <c>Carryable</c>: PHYS-1 is live in that file this
    /// wave, and the packet's boundary is that this lane reads the grab point and touches nothing
    /// else under <c>scripts/game/props/</c>. Measured once per grab and cached, because it walks
    /// the subtree.</para>
    ///
    /// <para><b>Not the bounding radius.</b> A 0.44 m crate's bounding sphere is 0.381 m and its
    /// half-width 0.22 m; hands placed on the sphere would float 16 cm off its sides.</para>
    /// </summary>
    private static Vector3 LocalHalfExtents(Carryable body)
    {
        Transform3D inv = body.GlobalTransform.AffineInverse();
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool any = false;
        Collect(body, inv, ref min, ref max, ref any);
        Vector3 half = any
            ? new Vector3(Mathf.Max((max.X - min.X) * 0.5f, 0.005f),
                Mathf.Max((max.Y - min.Y) * 0.5f, 0.005f),
                Mathf.Max((max.Z - min.Z) * 0.5f, 0.005f))
            : Vector3.One * 0.1f;

        // ...and then SCALED so its magnitude is the shipped bulk number.
        //
        // `Carryable.BoundingRadiusM` is half the diagonal of the prop's own shape mesh, computed
        // in that class from the mesh it actually chose, and it is what FEEL-1 derives HoldMin and
        // the holder clearance from. It is therefore the authority on how big a prop is, and this
        // walk only supplies the SHAPE -- which way the bulk is distributed -- because a bounding
        // radius cannot say where a crate's side faces are.
        //
        // Measured on this suite's first real run: the walk reported a 0.12 m can as 0.598 m
        // along its longest axis, so the can was gripped in two hands and each hand sat 25 cm off
        // its surface. Whatever the subtree contains besides the shape mesh -- and a Carryable
        // builds an outline shell, a designer may author anything -- normalising to the shipped
        // radius makes the answer agree with every other consumer of that number by construction
        // rather than by hoping the walk found the right nodes.
        float len = half.Length();
        return len > 1e-4f && body.BoundingRadiusM > 1e-4f
            ? half * (body.BoundingRadiusM / len)
            : half;
    }

    private static void Collect(Node node, Transform3D toBody, ref Vector3 min, ref Vector3 max, ref bool any)
    {
        // MeshInstance3D only, and never the highlight shell: every other VisualInstance3D
        // (a light, a particle system, a decal) reports an AABB that is about its EFFECT rather
        // than about the object's bulk, and the outline is the shape mesh again at 1.08x.
        if (node is MeshInstance3D { Visible: true } visual && visual.Name != "Outline")
        {
            Aabb box = visual.GetAabb();
            Transform3D x = toBody * visual.GlobalTransform;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = x * (box.Position + new Vector3(
                    (corner & 1) != 0 ? box.Size.X : 0f,
                    (corner & 2) != 0 ? box.Size.Y : 0f,
                    (corner & 4) != 0 ? box.Size.Z : 0f));
                min = any ? new Vector3(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y), Mathf.Min(min.Z, p.Z)) : p;
                max = any ? new Vector3(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y), Mathf.Max(max.Z, p.Z)) : p;
                any = true;
            }
        }
        foreach (Node child in node.GetChildren())
            Collect(child, toBody, ref min, ref max, ref any);
    }
}
