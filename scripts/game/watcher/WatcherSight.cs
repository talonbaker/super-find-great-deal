using Godot;

namespace MpFoundation.Game.Watcher;

/// <summary>
/// "Can this player currently see that spot." Used for the two opposite jobs the watcher's whole
/// appear/vanish rhythm rests on: refusing a stand position that somebody is already looking at
/// (so it is never witnessed arriving) and holding an armed exit until nobody is looking (so it
/// is never witnessed leaving).
///
/// <para><b>Horizontal only, and deliberately.</b> Pitch is ignored: a player looking at their
/// feet while walking is still, for these purposes, facing the way they are walking. Using the
/// true 3D camera vector would make the creature blink out every time somebody glanced down at
/// the ground — which on the dark uneven terrain this game is built around is most of the time.
/// This is a cheap approximation of attention, not a line-of-sight test, and it has no occlusion
/// term: a tree between the player and the creature does not count as looking away.</para>
/// </summary>
public static class WatcherSight
{
    /// <summary>True when <paramref name="spot"/> lies inside the horizontal cone of half-angle
    /// <paramref name="halfAngleDeg"/> about <paramref name="forward"/>, within
    /// <paramref name="rangeM"/>. A degenerate forward vector (a player facing straight up or
    /// down) reports "not seeing", which errs toward the creature staying put rather than toward
    /// it vanishing on a numerical accident.</summary>
    public static bool CanSee(Vector3 eye, Vector3 forward, Vector3 spot, float halfAngleDeg, float rangeM)
    {
        var toSpot = new Vector2(spot.X - eye.X, spot.Z - eye.Z);
        float dist = toSpot.Length();
        if (dist > rangeM)
            return false;
        // Standing on top of it counts as seeing it — otherwise the zero-length direction below
        // would report "not looking" at the one range where that is absurd.
        if (dist <= Mathf.Epsilon)
            return true;

        var fwd = new Vector2(forward.X, forward.Z);
        if (fwd.LengthSquared() <= Mathf.Epsilon)
            return false;

        float cos = fwd.Normalized().Dot(toSpot / dist);
        return cos >= Mathf.Cos(Mathf.DegToRad(halfAngleDeg));
    }
}
