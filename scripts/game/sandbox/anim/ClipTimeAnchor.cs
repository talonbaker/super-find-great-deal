namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>
/// <b>Where in a clip a body should be, given only replicated facts and local time.</b> The brief's
/// point 3, in arithmetic: <i>"When a replicated action state changes, the tree transitions and
/// seeks the clip to <c>(now − entryTick)</c>, so a late joiner or a client that received the change
/// late lands mid-clip at the right frame rather than starting from 0."</i>
///
/// <para><b>Two sources, one answer, and the difference is where the entry tick came from.</b></para>
/// <list type="number">
/// <item><b>The tick pair</b> — <see cref="SecondsFromTicks"/>. Used where the entry is observed as a
/// replicated state CHANGE (grounded flips, incapacity begins, the skid starts). Every peer stamps
/// the tick at which the change was true in the server's own clock, so two peers that received the
/// same change at different wall-clock instants still compute the same clip time.</item>
/// <item><b>The replicated progress</b> — <see cref="SecondsFromProgress"/>. Used where the wire
/// already carries normalised progress through the action (an authority-driven stroke whose
/// progress is broadcast to every peer's body). <b>This is the stronger of the two and is preferred
/// wherever it exists</b>: it needs no entry tick to be remembered, it is correct on a joiner's very
/// FIRST frame rather than on its first observed transition, and it cannot drift, because it is
/// recomputed by the authority every tick instead of integrated locally.</item>
/// </list>
///
/// <para><b>Neither is animation state on the wire.</b> The tick is the netcode's own clock; the
/// progress is the action's own gameplay phase, which existed before any clip did. Nothing here adds a field, and nothing here lets a client assert a pose.</para>
///
/// <para>Engine-free; every claim below is an assertion in <c>ClipTimeAnchorTests</c>.</para>
/// </summary>
public static class ClipTimeAnchor
{
    /// <summary>
    /// Clip time in seconds for a state entered at <paramref name="entryTick"/> and rendered at
    /// <paramref name="nowTick"/>.
    /// </summary>
    /// <param name="entryTick">The replicated tick at which the state became true.</param>
    /// <param name="nowTick">The tick this client is RENDERING. On a remote proxy that is the
    /// interpolated render tick, not the newest received tick — the body on screen is
    /// <c>SnapshotBuffer.InterpDelayTicks</c> behind, and seeking the clip to the newest tick would
    /// put the pose ahead of the position it belongs to.</param>
    /// <param name="tickDelta">Seconds per tick (<c>AvatarMotor.TickDelta</c>).</param>
    /// <param name="clipLengthSec">The clip's length.</param>
    /// <param name="loop">Whether the clip loops. A looping clip wraps; a one-shot clamps to its own
    /// end and HOLDS there, which is what makes a knock-out stay knocked out instead of snapping
    /// back to frame 0 on a body that is still down.</param>
    public static float SecondsFromTicks(
        double entryTick, double nowTick, float tickDelta, float clipLengthSec, bool loop)
    {
        // A negative elapse is not a bug to swallow silently but it IS reachable: a proxy's render
        // tick is deliberately behind the newest received tick, so a state change observed on
        // arrival can be stamped ahead of what is being rendered. Clamped to zero, which renders the
        // clip's first frame — the body has not started the action yet, on this client, and showing
        // frame 0 is the truthful answer.
        double elapsed = (nowTick - entryTick) * tickDelta;
        if (!double.IsFinite(elapsed) || elapsed <= 0.0)
            return 0f;
        return Wrap((float)elapsed, clipLengthSec, loop);
    }

    /// <summary>Clip time in seconds for an action whose normalised progress is already
    /// replicated.</summary>
    /// <param name="progress01">0 at entry, 1 at the end. Values outside are clamped rather than
    /// wrapped: a progress the authority reports past 1 means the action finished, and the honest
    /// pose for that is the clip's last frame.</param>
    /// <param name="clipLengthSec">The clip's length.</param>
    public static float SecondsFromProgress(float progress01, float clipLengthSec)
    {
        if (!float.IsFinite(progress01) || progress01 <= 0f)
            return 0f;
        if (clipLengthSec <= 0f)
            return 0f;
        return progress01 >= 1f ? clipLengthSec : progress01 * clipLengthSec;
    }

    /// <summary>
    /// <b>The looping case, and the reason it is not just a modulo.</b> A looping clip entered N
    /// cycles ago is at <c>elapsed mod length</c>; a one-shot entered past its own end is at its end
    /// and stays there. Getting this backwards is what makes a late joiner see a knocked-out body
    /// restart its fall every 0.6 s.
    /// </summary>
    public static float Wrap(float elapsedSec, float clipLengthSec, bool loop)
    {
        if (clipLengthSec <= 0f || !float.IsFinite(elapsedSec) || elapsedSec <= 0f)
            return 0f;
        if (!loop)
            return elapsedSec >= clipLengthSec ? clipLengthSec : elapsedSec;

        float wrapped = elapsedSec % clipLengthSec;
        return wrapped < 0f ? wrapped + clipLengthSec : wrapped;
    }

    /// <summary>
    /// <b>The mid-action join, stated as the thing the test asserts.</b> A client that connects at
    /// <paramref name="joinTick"/> while a state entered at <paramref name="entryTick"/> is still
    /// running must render the frame the action is really at — never frame 0, and never the frame it
    /// would reach if it started now.
    ///
    /// <para>Returns false when the action has already finished by <paramref name="joinTick"/> and
    /// does not loop, so the caller can skip the transition entirely instead of playing a one-shot
    /// that is over. That case is not hypothetical — it is what a joiner sees for every stagger that
    /// happened a second before it arrived.</para>
    /// </summary>
    public static bool TryJoinMidClip(
        double entryTick, double joinTick, float tickDelta, float clipLengthSec, bool loop,
        out float seekSec)
    {
        seekSec = SecondsFromTicks(entryTick, joinTick, tickDelta, clipLengthSec, loop);
        if (loop)
            return true;
        double elapsed = (joinTick - entryTick) * tickDelta;
        return double.IsFinite(elapsed) && elapsed < clipLengthSec;
    }
}
