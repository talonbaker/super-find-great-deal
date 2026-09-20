using Godot;

namespace MpFoundation.Game.Aim;

/// <summary>
/// The query both lean-build verbs call: "what am I aimed at, within range R, in group G?"
/// Stateless and generic — no film logic, no arrow logic, no evidence logic lives here (see
/// AimController's class doc for the same "verb-agnostic" rule). Every method takes its inputs
/// explicitly (origin, direction, range, FOV, group) rather than reading a Camera3D, because a
/// dedicated server never has one (SandboxAvatar.ConfigureNetworkedInstance never creates a
/// SandboxCamera on Multiplayer.IsServer()) — the SAME call, with the SAME arguments, must be
/// answerable identically on a client (cosmetic feedback / a shot's local pre-check) and on the
/// server (the actual validity check an untrusted client can never win by lying about).
/// </summary>
public static class AimQuery
{
    /// <summary>One resolved hit from <see cref="QueryGroup"/>: the nearest node in the queried
    /// group that is within range, within the frustum cone, and not occluded.</summary>
    public readonly record struct Hit(Node3D Target, Vector3 Point, Vector3 Normal, float Distance);

    /// <summary>Line-of-sight tolerance for the occlusion check below: a raycast toward the
    /// candidate is allowed to report a hit up to this much CLOSER than the candidate's own
    /// distance before it counts as "something else is in the way" — covers the candidate's own
    /// collider (if it has one) reporting a hit a few centimeters short of its origin/pivot
    /// point without also accepting a real occluder that happens to sit almost exactly at the
    /// candidate's own depth.</summary>
    private const float OcclusionEpsilonM = 0.15f;

    /// <summary>Slack on the frustum-edge cosine comparison: a candidate placed by construction
    /// EXACTLY at the half-angle (e.g. via Vector3.Rotated by fovDegrees/2) lands a few ULPs off
    /// true due to the rotation and the cosine being computed through two independent paths —
    /// without this, the documented "the boundary is inclusive" contract would be flaky rather
    /// than reliably true. Same rationale as SandboxAvatar's own prediction epsilons.</summary>
    private const float AngleCosEpsilon = 1e-4f;

    /// <summary>
    /// Builds the world-space forward direction for a given look yaw/pitch (radians, same
    /// convention SandboxCamera.Yaw/Pitch use — see MoveIntent.AimYaw/AimPitch's doc comments
    /// for why these two floats, not the avatar's own body facing, are the source of truth).
    /// Pure math, no scene-tree/physics dependency — safe to call on either client or server
    /// from whichever AimYaw/AimPitch that side currently holds.
    /// </summary>
    public static Vector3 DirectionFromYawPitch(float yaw, float pitch)
    {
        var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
        return -basis.Z; // Godot forward convention (matches SandboxCamera's own pivot rig).
    }

    /// <summary>
    /// "What am I aimed at?" — a ONE-FRAME, stateless answer suitable for a server-side
    /// validity check: the nearest node in <paramref name="group"/> that is (a) within
    /// <paramref name="range"/> meters of <paramref name="origin"/>, (b) inside the
    /// <paramref name="fovDegrees"/>-wide cone around <paramref name="direction"/>, and (c) not
    /// occluded by world geometry between the two. Both boundaries are INCLUSIVE by design: a
    /// candidate sitting exactly at range, or exactly at the frustum's edge angle, counts as a
    /// hit — a verb that wants a stricter boundary narrows its own range/fovDegrees argument
    /// rather than this method silently rounding down for it.
    ///
    /// <paramref name="fovDegrees"/> is caller-supplied on every call, never read off a
    /// Camera3D — this is what keeps the FOV EASE (SandboxCamera's cosmetic zoom while raised)
    /// structurally unable to affect what actually counts as "in frame": there is no Camera3D
    /// parameter for it to read in the first place. A verb decides its own validity FOV as a
    /// fixed constant (see the WP-L3 PR's consumption contract for a worked example) and passes
    /// it here every time, identically on client and server.
    ///
    /// <paramref name="excludeSelf"/> should be the querying avatar's own physics Rid so a
    /// point-blank occluder can never be its own collider.
    /// </summary>
    public static Hit? QueryGroup(SceneTree tree, World3D world, Vector3 origin, Vector3 direction,
        StringName group, float range, float fovDegrees, Rid? excludeSelf = null)
    {
        if (!direction.IsFinite() || direction.LengthSquared() < 1e-8f)
            return null;
        direction = direction.Normalized();

        float halfAngleCos = Mathf.Cos(Mathf.DegToRad(Mathf.Clamp(fovDegrees, 0f, 360f) * 0.5f));

        Godot.Collections.Array<Rid>? exclude = null;
        if (excludeSelf is Rid selfRid)
            exclude = new Godot.Collections.Array<Rid> { selfRid };

        Node3D? best = null;
        float bestDistance = float.MaxValue;
        Vector3 bestPoint = default, bestNormal = default;

        foreach (Node node in tree.GetNodesInGroup(group))
        {
            if (node is not Node3D candidate || !IsInstanceValidAndInTree(candidate))
                continue;

            Vector3 toCandidate = candidate.GlobalPosition - origin;
            float distance = toCandidate.Length();
            // Range boundary is inclusive (<=). A candidate sitting exactly at origin (distance
            // ~0) cannot form a meaningful direction and is skipped, not treated as a free hit.
            if (distance < 1e-4f || distance > range)
                continue;

            Vector3 toCandidateDir = toCandidate / distance;
            // Frustum boundary is inclusive (>=, not >): exactly at the cone's edge angle counts
            // (AngleCosEpsilon absorbs the floating-point noise an exact-boundary construction
            // picks up crossing two independent trig paths — see its own doc comment).
            if (toCandidateDir.Dot(direction) < halfAngleCos - AngleCosEpsilon)
                continue;

            if (IsOccluded(world, origin, candidate.GlobalPosition, distance, exclude))
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
                bestPoint = candidate.GlobalPosition;
                bestNormal = -toCandidateDir;
            }
        }

        return best != null ? new Hit(best, bestPoint, bestNormal, bestDistance) : null;
    }

    /// <summary>Raycasts from origin toward the candidate's position; occluded means the ray
    /// reports a hit meaningfully CLOSER than the candidate itself (see OcclusionEpsilonM) —
    /// works identically whether the candidate has its own collider (the ray then legitimately
    /// lands at ~its own distance, not "closer") or none at all (an unobstructed ray to a bare
    /// Node3D marker reports no hit whatsoever). No hit at all is trivially unoccluded.</summary>
    private static bool IsOccluded(World3D world, Vector3 origin, Vector3 targetPosition, float targetDistance,
        Godot.Collections.Array<Rid>? exclude)
    {
        var query = PhysicsRayQueryParameters3D.Create(origin, targetPosition);
        if (exclude != null)
            query.Exclude = exclude;
        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return false;
        var hitPosition = (Vector3)hit["position"];
        float hitDistance = origin.DistanceTo(hitPosition);
        return hitDistance < targetDistance - OcclusionEpsilonM;
    }

    private static bool IsInstanceValidAndInTree(Node3D node) =>
        GodotObject.IsInstanceValid(node) && node.IsInsideTree();
}
