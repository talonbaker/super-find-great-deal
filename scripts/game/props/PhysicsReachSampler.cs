using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>The engine half of layer 3</b> — three physics queries and no judgement. Everything that
/// DECIDES lives in <see cref="Reachability"/>, engine-free and xUnit-tested; this class only
/// answers "is there floor here", "does a body fit here" and "what does the eye meet first".
///
/// <para><b>Who is excluded from which query, and why each is deliberate.</b>
/// <list type="bullet">
/// <item><b>The floor probe and the body-fit cast exclude every avatar AND every prop.</b> Only
/// STATIC geometry may create or deny a standing point. A seeker does not have to be able to
/// stand on a crate for a hide to be fair, and a crate sitting where they would stand is a crate
/// they pick up and move — refusing the point because of it would make "buried under ten movable
/// items" illegal, which §5b says in as many words is the game.</item>
/// <item><b>The eye ray excludes avatars only.</b> Props are exactly what it is there to see: a
/// first hit on a bin or a box COUNTS, because the seeker can move it. Excluding the other
/// player's body is the same rule <see cref="PlacementIntegrity"/> already applies for the same
/// reason — somebody standing in the way walks off a second later.</item>
/// </list></para>
///
/// <para><b>One sampler per evaluation, and it is a throwaway.</b> It caches the exclusion list
/// and the probe capsule, which are the only two allocations the whole audit makes; the
/// alternative was rebuilding them 24 times per evaluation inside the rule, which would have put
/// engine state inside the engine-free half.</para>
/// </summary>
public sealed class PhysicsReachSampler : Reachability.IReachSampler
{
    /// <summary>A surface counts as floor when its normal is at least this vertical. cos(45°),
    /// so a ramp is floor and a wall is not; nothing in this game has a slope between them, and
    /// picking the boundary explicitly is cheaper than discovering later which side a level
    /// author assumed.</summary>
    private const float MinFloorNormalY = 0.7071f;

    /// <summary>Lift applied to the probe capsule above the floor point it stands on, metres. A
    /// capsule whose bottom cap is exactly tangent to the slab reports a contact about half the
    /// time on a float compare, which would make a perfectly ordinary standing point flicker.
    /// Same reasoning, and the same magnitude, as <see cref="PlacementIntegrity.BoundsEpsilonM"/>.
    /// </summary>
    private const float StandLiftM = 0.02f;

    /// <summary>How far ABOVE the ring point the downward probe starts, metres. The ring point
    /// sits at the target's own height and may be a hair inside a shelf lip; a ray that starts
    /// inside a shape does not report it, so starting slightly clear is what keeps the probe
    /// honest about what is under it.</summary>
    private const float FloorProbeLiftM = 0.05f;

    private readonly PhysicsDirectSpaceState3D? _space;
    private readonly RigidBody3D? _targetBody;
    private readonly Rid _targetRid;
    private readonly Godot.Collections.Array<Rid> _excludeAvatarsOnly = new();
    private readonly Godot.Collections.Array<Rid> _excludeAvatarsAndProps = new();
    private readonly CapsuleShape3D _probeCapsule;
    private readonly float _eyeHeightM;
    private readonly HashSet<ulong> _propInstanceIds = new();

    /// <summary>Total physics queries this sampler has issued. Read by the cost instrumentation
    /// the packet asks for (queries/second under load) — a counter on the thing that does the
    /// work, not an estimate from the call count.</summary>
    public int Queries { get; private set; }

    /// <summary>The probe body the standing test used, for the log line: "could a body this
    /// size have stood there" is not answerable from a boolean.</summary>
    public string ProbeDescription { get; }

    /// <inheritdoc/>
    public bool HasTarget => _targetBody is not null;

    /// <inheritdoc/>
    public bool TargetInsideStatic { get; }

    /// <inheritdoc/>
    public Vector3 TargetCentre { get; }

    /// <inheritdoc/>
    public Vector3 TargetTop { get; }

    /// <summary>
    /// Build a sampler for one evaluation of <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The prop being audited, or null for "there is no target" — the
    /// null case is answered rather than thrown, because a round in the holding room legitimately
    /// has no target and the fact source must report <c>null</c> for it, not crash the server.</param>
    /// <param name="props">Every prop this peer knows about, whose bodies are excluded from the
    /// floor and body-fit queries and classified as movable by the eye ray.</param>
    /// <param name="insideStatic">Layer 2's verdict: the target is inside static geometry and the
    /// rest audit could not correct it. Passed in rather than re-derived so the two layers can
    /// never disagree about one object.</param>
    public PhysicsReachSampler(NetworkedProp? target, IEnumerable<NetworkedProp> props,
        bool insideStatic)
    {
        TargetInsideStatic = insideStatic;
        RigidBody3D? body = target is not null && GodotObject.IsInstanceValid(target)
                            && target.Body is { } b && GodotObject.IsInstanceValid(b) && b.IsInsideTree()
            ? b
            : null;
        _targetBody = body;
        _space = body?.GetWorld3D()?.DirectSpaceState;
        if (body is null || _space is null)
        {
            _targetBody = null;
            _probeCapsule = new CapsuleShape3D();
            ProbeDescription = "no target";
            return;
        }

        _targetRid = body.GetRid();
        TargetCentre = body.GlobalPosition;
        Vector3 half = FirstShapeHalfExtents(body);
        TargetTop = TargetCentre + new Vector3(0f, half.Y, 0f);

        foreach (SandboxAvatar avatar in SandboxAvatar.Live)
        {
            if (!GodotObject.IsInstanceValid(avatar) || !avatar.IsInsideTree())
                continue;
            _excludeAvatarsOnly.Add(avatar.GetRid());
            _excludeAvatarsAndProps.Add(avatar.GetRid());
        }
        foreach (NetworkedProp prop in props)
        {
            if (!GodotObject.IsInstanceValid(prop) || prop.Body is not { } pb
                || !GodotObject.IsInstanceValid(pb) || !pb.IsInsideTree())
                continue;
            _excludeAvatarsAndProps.Add(pb.GetRid());
            _propInstanceIds.Add(pb.GetInstanceId());
        }

        // The body that has to fit. A live avatar measures its own model, so when one is present
        // that measurement IS the answer to "does a player fit here"; with none (the planted
        // headless self-test, a server before anyone joins) fall back to the reference figure's
        // measured heights. Stated in the log either way, because a standing test against the
        // wrong body size is a wrong answer that looks exactly like a right one.
        SandboxAvatar? any = FirstLiveAvatar();
        float radius, height;
        if (any is not null)
        {
            radius = any.Proportions.CapsuleRadiusM;
            height = any.Proportions.CapsuleHeightM;
            _eyeHeightM = any.Proportions.EyeHeightM;
            ProbeDescription = $"live avatar r={radius:0.000} h={height:0.000} eye={_eyeHeightM:0.000}";
        }
        else
        {
            radius = AvatarProportions.Fallback.CapsuleRadiusM;
            height = AvatarProportions.PlayerCrownM;
            _eyeHeightM = AvatarProportions.PlayerEyeHeightM;
            ProbeDescription = $"reference figure r={radius:0.000} h={height:0.000} eye={_eyeHeightM:0.000}";
        }
        _probeCapsule = new CapsuleShape3D { Radius = radius, Height = height };
    }

    /// <inheritdoc/>
    public bool TryStand(Vector3 ringPoint, out Vector3 eye)
    {
        eye = Vector3.Zero;
        if (_space is null)
            return false;

        // 1. Is there static floor under this point, within reach of it?
        var ray = new PhysicsRayQueryParameters3D
        {
            From = ringPoint + new Vector3(0f, FloorProbeLiftM, 0f),
            To = ringPoint - new Vector3(0f, Reachability.FloorProbeM, 0f),
            CollisionMask = PlacementIntegrity.QueryMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = _excludeAvatarsAndProps,
        };
        Queries++;
        Godot.Collections.Dictionary hit = _space.IntersectRay(ray);
        if (hit.Count == 0)
            return false;
        if (!hit.TryGetValue("normal", out Variant n) || n.AsVector3().Y < MinFloorNormalY)
            return false;
        if (!hit.TryGetValue("position", out Variant p))
            return false;
        Vector3 floor = p.AsVector3();

        // 2. Does a body fit standing on it?
        var fit = new PhysicsShapeQueryParameters3D
        {
            Shape = _probeCapsule,
            Transform = new Transform3D(Basis.Identity,
                floor + new Vector3(0f, _probeCapsule.Height * 0.5f + StandLiftM, 0f)),
            CollisionMask = PlacementIntegrity.QueryMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = _excludeAvatarsAndProps,
            Margin = 0f,
        };
        Queries++;
        if (_space.IntersectShape(fit, 1).Count > 0)
            return false;

        eye = floor + new Vector3(0f, _eyeHeightM, 0f);
        return true;
    }

    /// <inheritdoc/>
    public Reachability.ReachHit FirstHit(Vector3 from, Vector3 to)
    {
        if (_space is null)
            return Reachability.ReachHit.Static;

        var ray = new PhysicsRayQueryParameters3D
        {
            From = from,
            To = to,
            CollisionMask = PlacementIntegrity.QueryMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = _excludeAvatarsOnly,
        };
        Queries++;
        Godot.Collections.Dictionary hit = _space.IntersectRay(ray);
        if (hit.Count == 0)
            return Reachability.ReachHit.Nothing;
        if (!hit.TryGetValue("rid", out Variant ridVariant))
            return Reachability.ReachHit.Static;
        if (ridVariant.AsRid() == _targetRid)
            return Reachability.ReachHit.Target;
        if (hit.TryGetValue("collider", out Variant collider)
            && collider.As<GodotObject>() is Node node
            && _propInstanceIds.Contains(node.GetInstanceId()))
            return Reachability.ReachHit.MovableProp;
        return Reachability.ReachHit.Static;
    }

    private static SandboxAvatar? FirstLiveAvatar()
    {
        foreach (SandboxAvatar avatar in SandboxAvatar.Live)
            if (GodotObject.IsInstanceValid(avatar) && avatar.IsInsideTree())
                return avatar;
        return null;
    }

    /// <summary>The prop's own half-extents, via the one shape-measuring routine this game has
    /// (<see cref="PlacementIntegrity.HalfExtentsOf"/>) — a second one would be a second answer
    /// to "how big is this crate".</summary>
    private static Vector3 FirstShapeHalfExtents(Node body)
    {
        foreach (Node child in body.GetChildren())
            if (child is CollisionShape3D { Shape: { } shape })
                return PlacementIntegrity.HalfExtentsOf(shape);
        return Vector3.One * 0.25f;
    }
}
