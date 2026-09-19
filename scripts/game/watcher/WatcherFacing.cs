using Godot;

namespace MpFoundation.Game.Watcher;

/// <summary>
/// Which way the head is pointed, and how fast it gets there.
///
/// <para>This is a four-line file with its own tests because the repo has already shipped the
/// bug it exists to prevent — a chase entity that faced backwards while pursuing — and because
/// facing is not cosmetic here. `/direct` (THRILL-BIBLE §12, the watcher entry) makes the head
/// turn the creature's <i>only</i> output channel: "the head turn is the readout", and the
/// second device row ("attention as a resource the group can move") does not exist at all unless
/// a player can watch attention transfer. A watcher facing the wrong way is not a graphical
/// glitch, it is the mechanic reporting the opposite of the truth.</para>
/// </summary>
public static class WatcherFacing
{
    /// <summary>Yaw, in radians, that points a Node3D's local -Z (Godot's forward) from
    /// <paramref name="from"/> at <paramref name="to"/>, ignoring height. Returns 0 for a
    /// degenerate (zero horizontal) delta rather than NaN.</summary>
    public static float YawToward(Vector3 from, Vector3 to)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        if (Mathf.Abs(dx) <= Mathf.Epsilon && Mathf.Abs(dz) <= Mathf.Epsilon)
            return 0f;
        // A node at yaw t has forward (-sin t, 0, -cos t). Solving -sin t = dx, -cos t = dz
        // gives t = atan2(-dx, -dz). Derived rather than guessed, because the sign convention
        // here is exactly what the historical facing bug got wrong.
        return Mathf.Atan2(-dx, -dz);
    }

    /// <summary>Moves <paramref name="currentRad"/> toward <paramref name="desiredRad"/> by at
    /// most <paramref name="maxDeltaRad"/>, taking the short way round.</summary>
    /// <remarks>The wrap is the point. Turning from +179° to -179° is a 2° turn, and the naive
    /// subtraction makes it a 358° one — visible as the head spinning the long way round at the
    /// exact moment a player crosses behind the creature, which is the moment the turn most
    /// needs to be readable.</remarks>
    public static float StepYaw(float currentRad, float desiredRad, float maxDeltaRad)
    {
        float delta = Mathf.Wrap(desiredRad - currentRad, -Mathf.Pi, Mathf.Pi);
        if (Mathf.Abs(delta) <= maxDeltaRad)
            return desiredRad;
        return currentRad + Mathf.Sign(delta) * maxDeltaRad;
    }
}
