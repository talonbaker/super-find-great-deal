using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game;

/// <summary>
/// One rule for "what does E act on". Among every candidate the avatar can physically
/// reach, the one the local camera is actually pointed at wins; with no camera
/// (headless bots, scripted test brains, server sims) — or when nothing in reach is
/// under the aim — the nearest in-reach candidate wins, which is the old behaviour.
///
/// Every highlight poll and every Interact edge must go through the same pick so the
/// thing that shimmers IS the thing the key acts on. The playtest bug this kills:
/// standing near a door while the highlight sat on a box still opened the door,
/// because interactives used to outrank everything by type, not by aim.
/// </summary>
public static class InteractTargeting
{
    /// <summary>Cos of the aim cone's half-angle (~55°). Generous enough that "roughly
    /// toward it" counts, tight enough that a door behind the camera can never steal
    /// the press from the thing the player is looking at.</summary>
    private const float AimConeCos = 0.57f;

    public readonly struct Candidate
    {
        public readonly object Target;
        public readonly Vector3 Focus;
        public readonly float Range;

        public Candidate(object target, Vector3 focus, float range)
        {
            Target = target;
            Focus = focus;
            Range = range;
        }
    }

    /// <summary>The winning candidate's Target, or null when nothing is in reach.
    /// Deterministic: callers invoking this twice with the same inputs (a highlight
    /// poll and the Interact edge a frame later) get the same winner.</summary>
    public static object? Pick(IReadOnlyList<Candidate> candidates, Vector3 avatarPos, Camera3D? aimCamera)
    {
        Vector3 camPos = default, camForward = default;
        bool aimed = aimCamera != null && GodotObject.IsInstanceValid(aimCamera);
        if (aimed)
        {
            camPos = aimCamera!.GlobalPosition;
            camForward = -aimCamera.GlobalTransform.Basis.Z;
        }
        return Pick(candidates, avatarPos, camPos, camForward, aimed);
    }

    /// <summary>Engine-free core of <see cref="Pick(IReadOnlyList{Candidate}, Vector3, Camera3D?)"/>
    /// — the Camera3D overload only resolves the camera's position/forward and delegates here,
    /// so the aim-cone rule itself is testable without an engine (a Camera3D cannot be
    /// constructed under plain xUnit).</summary>
    public static object? Pick(
        IReadOnlyList<Candidate> candidates, Vector3 avatarPos,
        Vector3 camPos, Vector3 camForward, bool aimed)
    {
        object? bestAimed = null;
        float bestDot = AimConeCos;
        object? bestNear = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate c = candidates[i];
            float dist = avatarPos.DistanceTo(c.Focus);
            if (dist > c.Range)
                continue;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestNear = c.Target;
            }
            if (!aimed)
                continue;
            Vector3 to = c.Focus - camPos;
            if (to.LengthSquared() < 1e-6f)
                continue;
            float dot = camForward.Dot(to.Normalized());
            if (dot > bestDot)
            {
                bestDot = dot;
                bestAimed = c.Target;
            }
        }
        return bestAimed ?? bestNear;
    }
}
