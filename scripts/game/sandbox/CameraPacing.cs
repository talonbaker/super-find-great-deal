using System;
using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The engine-free half of the first-person lens's frame pacing</b> (SICK-1, 2026-09-20).
///
/// <para>Two pure functions with nothing of the scene tree in them, so both are xUnit-testable
/// and neither can be argued about from a screenshot: where the eye belongs on a RENDER frame
/// that falls between two PHYSICS ticks, and what a recorded run of frames actually measured.</para>
///
/// <para><b>Why this exists at all.</b> The avatar is a <c>CharacterBody3D</c> whose position is
/// written once per physics tick (60 Hz, <c>AvatarMotor.TickDelta</c>) and the lens is a child of
/// it, so before this lane the lens's world position changed 60 times a second no matter how many
/// times a second the screen did. On a 60 Hz display that is invisible. On anything else the
/// render frames do not divide evenly into the physics ticks, so the eye holds still for some
/// frames and jumps a whole tick on others — a repeating stale/fresh beat that the look, which
/// IS smooth because it is driven per render frame, makes impossible to ignore.</para>
/// </summary>
public static class CameraPacing
{
    /// <summary>
    /// <b>How far the body may legitimately travel in one physics tick before the step is read as
    /// a teleport rather than as motion</b>, in metres.
    ///
    /// <para>The fastest legitimate ground step is <c>MoveSpeed × SprintMultiplier ÷ TickRate</c>
    /// = 3.8 × 1.6 ÷ 60 = <b>0.101 m</b>, and a fall adds <c>Gravity ÷ TickRate²</c> per tick from
    /// a standing start; nothing in a three-room supermarket falls long enough to approach this
    /// number. 0.75 m is 45 m/s — seven times the sprint and far outside anything the motor can
    /// produce here — while every real discontinuity is an order of magnitude the other side of
    /// it: <c>RoomTeleport</c> moves a player between rooms that are tens of metres apart.</para>
    ///
    /// <para><b>A reconciliation pop is deliberately NOT a teleport here</b>, and that falls out
    /// of reading <c>SandboxAvatar.RenderGlobalPosition</c> rather than <c>GlobalPosition</c>: a
    /// correction up to <c>MaxVisualErrorM</c> is absorbed into <c>_visualError</c> at the instant
    /// it lands and drained exponentially, so the RENDER position never steps by the pop. What
    /// does step by a whole room is a real teleport, which zeroes the error — so the one thing
    /// this threshold is asked to catch is the one thing that reaches it.</para>
    ///
    /// <para>Getting it wrong in the safe direction costs nothing: a step over the threshold is
    /// rendered exactly the way the shipped build renders every step, which is a snap.</para>
    /// </summary>
    public const float TeleportSnapM = 0.75f;

    /// <summary>
    /// Where the eye belongs this render frame: <paramref name="fraction"/> of the way from the
    /// previous physics tick's eye position to the current one.
    ///
    /// <para><paramref name="fraction"/> is <c>Engine.GetPhysicsInterpolationFraction()</c> —
    /// how far through the current physics tick the renderer is. Clamped rather than trusted,
    /// because a frame taken during a physics catch-up spiral can report past 1 and an
    /// extrapolated eye is a camera moving somewhere the body has not been.</para>
    ///
    /// <para>Beyond <paramref name="teleportSnapM"/> the two samples are not two ends of a motion
    /// and lerping them would sweep the lens across the level over one frame; the current sample
    /// is returned unchanged, which is precisely the shipped behaviour.</para>
    /// </summary>
    public static Vector3 EyePosition(Vector3 previousTick, Vector3 currentTick, float fraction,
        float teleportSnapM = TeleportSnapM)
    {
        if (!(teleportSnapM > 0f))
            return currentTick;
        if ((currentTick - previousTick).LengthSquared() > teleportSnapM * teleportSnapM)
            return currentTick;
        return previousTick.Lerp(currentTick, Mathf.Clamp(fraction, 0f, 1f));
    }

    /// <summary>What one recorded run measured. Every field is a raw quantity; there is no
    /// verdict in here, because the only budget anyone has agreed to is Talon's inner ear.</summary>
    /// <param name="Frames">Render frames recorded.</param>
    /// <param name="P50Ms">Median frame time, ms.</param>
    /// <param name="P95Ms">95th-percentile frame time, ms.</param>
    /// <param name="MaxMs">Worst frame time, ms.</param>
    /// <param name="OverBudget">Frames slower than the budget (20 ms unless stated).</param>
    /// <param name="ZeroStepFraction"><b>The physics-step tell</b>: the share of render frames on
    /// which the eye did not move at all. On a camera nailed to a 60 Hz body and rendered at
    /// R Hz while walking, this is 1 − 60/R — about 0.58 at 144 Hz and 0.64 at 165 Hz. With
    /// per-frame interpolation it collapses to ~0 while the body is moving.</param>
    /// <param name="MeanStepM">Mean eye movement per render frame, metres. Interpolation must not
    /// change this: the same distance is covered, it is just spread over the frames.</param>
    /// <param name="StepStdDevM">Standard deviation of that per-frame step. <b>This is the
    /// judder number.</b> A camera that moves the same small amount every frame has a small one;
    /// one that alternates "nothing" and "a whole tick" has one of the same order as the mean.</param>
    /// <param name="YawStepStdDevDeg">Standard deviation of the per-frame yaw step under a
    /// constant scripted turn, degrees — the look's own half of the same question.</param>
    public readonly record struct Summary(
        int Frames,
        float P50Ms,
        float P95Ms,
        float MaxMs,
        int OverBudget,
        float ZeroStepFraction,
        float MeanStepM,
        float StepStdDevM,
        float YawStepStdDevDeg);

    /// <summary>
    /// Reduce a recorded run to <see cref="Summary"/>. Pure: the same three lists always produce
    /// the same row, so the table in the handoff can be recomputed from the CSV by anyone who
    /// doubts it.
    /// </summary>
    /// <param name="frameMs">Per-frame wall time, ms.</param>
    /// <param name="stepM">Per-frame eye movement, metres. Same length as <paramref name="frameMs"/>.</param>
    /// <param name="yawStepDeg">Per-frame yaw movement, degrees. Same length.</param>
    /// <param name="budgetMs">The "slow frame" line. 20 ms is the packet's.</param>
    /// <param name="zeroEpsM">Below this a step counts as no movement at all. 1e-5 m is 10 µm —
    /// under float noise on a metre-scale world position and far under any real step.</param>
    public static Summary Summarize(
        IReadOnlyList<float> frameMs,
        IReadOnlyList<float> stepM,
        IReadOnlyList<float> yawStepDeg,
        float budgetMs = 20f,
        float zeroEpsM = 1e-5f)
    {
        ArgumentNullException.ThrowIfNull(frameMs);
        ArgumentNullException.ThrowIfNull(stepM);
        ArgumentNullException.ThrowIfNull(yawStepDeg);
        if (frameMs.Count == 0)
            return default;

        var sorted = new List<float>(frameMs);
        sorted.Sort();
        int over = 0;
        foreach (float ms in frameMs)
            if (ms > budgetMs)
                over++;

        int zero = 0;
        foreach (float m in stepM)
            if (m <= zeroEpsM)
                zero++;

        return new Summary(
            frameMs.Count,
            Percentile(sorted, 0.50f),
            Percentile(sorted, 0.95f),
            sorted[^1],
            over,
            stepM.Count == 0 ? 0f : (float)zero / stepM.Count,
            Mean(stepM),
            StdDev(stepM),
            StdDev(yawStepDeg));
    }

    /// <summary>Nearest-rank percentile over an ALREADY SORTED list — the definition with no
    /// interpolation in it, so a percentile is always a frame time that genuinely occurred.</summary>
    public static float Percentile(IReadOnlyList<float> sortedAscending, float q)
    {
        if (sortedAscending.Count == 0)
            return 0f;
        int rank = (int)Math.Ceiling(Mathf.Clamp(q, 0f, 1f) * sortedAscending.Count) - 1;
        return sortedAscending[Math.Clamp(rank, 0, sortedAscending.Count - 1)];
    }

    private static float Mean(IReadOnlyList<float> xs)
    {
        if (xs.Count == 0)
            return 0f;
        double sum = 0.0;
        foreach (float x in xs)
            sum += x;
        return (float)(sum / xs.Count);
    }

    private static float StdDev(IReadOnlyList<float> xs)
    {
        if (xs.Count < 2)
            return 0f;
        double mean = Mean(xs);
        double acc = 0.0;
        foreach (float x in xs)
        {
            double d = x - mean;
            acc += d * d;
        }
        return (float)Math.Sqrt(acc / xs.Count);
    }
}
