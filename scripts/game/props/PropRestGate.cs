using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>When the rest audit is allowed to teleport a prop</b> — PHYS-1 ruling P3, 2026-09-20.
///
/// <para>Talon: <i>"I want to know they won't freak out and make other objects jump around
/// randomly."</i> The rest audit (REACH-1, <see cref="RestAudit"/>) is itself one of the things
/// that can make a prop jump: its second correction step returns a prop to its last good
/// transform, and a prop that snaps back to a pose it held ten seconds ago, half a metre in front
/// of the player who is looking straight at it, is exactly the freakout the sentence means —
/// even though every line of it is working as designed.</para>
///
/// <para><b>Depenetration is untouched and unconditional.</b> Pushing a crate 4 cm out of a wall
/// is a correction the eye reads as the crate settling; it is the common case, it keeps the prop
/// where the player left it, and gating it would be a regression against the defect REACH-1 was
/// cut to fix. What is gated is only the LAST-GOOD RESTORE, which is the one that moves a prop a
/// distance nobody asked for.</para>
///
/// <para>Engine-free apart from <c>Mathf</c>, so <c>tests/unit/PropRestGateTests.cs</c> takes
/// every branch with no engine present.</para>
/// </summary>
public static class PropRestGate
{
    /// <summary><b>How long a prop has to have been unfixable before the audit will move it.</b>
    /// One second, from P3. Under a second the solver is very often still separating an overlap
    /// of its own accord — the audit's own doc calls a prop-on-prop overlap <i>"usually benign
    /// (the solver is about to separate them)"</i> — so waiting costs nothing and catches the
    /// case where the restore would have been undoing physics that was about to finish.</summary>
    public const float StuckHoldSec = 1.0f;

    /// <summary><b>How close a player has to be before the audit leaves a prop alone.</b> Two
    /// metres from the grab ray, from P3.
    ///
    /// <para>Derived rather than picked: the grab range is <c>PropManager.GrabRange</c>
    /// (<c>SandboxAvatar.PickupRadius</c> 1.5 m + 0.75 m tolerance = 2.25 m) and the hold band's
    /// ceiling is 1.2 m, so 2 m is inside the distance at which a player is DOING something to a
    /// prop rather than merely standing in the same aisle. Past it, a correction happens to
    /// something they are not touching, which is the case the audit was written for.</para></summary>
    public const float PlayerAttentionM = 2.0f;

    /// <summary>
    /// <b>May the audit restore this prop to its last good transform?</b> Both halves of P3 have
    /// to be true: it has been stuck for at least <see cref="StuckHoldSec"/>, AND no player's
    /// grab ray passes within <see cref="PlayerAttentionM"/> of it.
    /// </summary>
    /// <param name="stuckSec">Seconds this prop has been failing the audit without being
    /// correctable in place. Reset to zero by any passing or depenetrated audit.</param>
    /// <param name="nearestGrabRayM">Distance from the prop to the closest point on any avatar's
    /// grab ray, metres. <see cref="float.PositiveInfinity"/> when there are no avatars — an
    /// empty room is exactly where a restore is safe, so "no players" must read as "far", never
    /// as zero.</param>
    public static bool MayRestoreLastGood(float stuckSec, float nearestGrabRayM) =>
        stuckSec >= StuckHoldSec && nearestGrabRayM > PlayerAttentionM;

    /// <summary>
    /// <b>Distance from a point to a player's grab ray</b>, treated as the SEGMENT from the eye
    /// out to <paramref name="rangeM"/> rather than an infinite line — a prop behind the player,
    /// or twenty metres down the aisle in front of them, is not something they are looking at
    /// moving.
    /// </summary>
    public static float DistanceToGrabRay(Vector3 point, Vector3 eye, Vector3 aimDirection,
        float rangeM)
    {
        float len = aimDirection.Length();
        if (len <= 0f || rangeM <= 0f)
            return point.DistanceTo(eye);
        Vector3 unit = aimDirection / len;
        float t = Mathf.Clamp((point - eye).Dot(unit), 0f, rangeM);
        return point.DistanceTo(eye + unit * t);
    }
}
