using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>"Is there anything left for this prop's per-frame visual code to do?"</b> — the decision
/// half of PROBE-1's at-rest fix, split out so it is arithmetic an xUnit test can hold rather
/// than a condition buried in a Godot callback (the same split <c>Reachability</c> has from
/// <c>PhysicsReachSampler</c>, and for the same reason).
///
/// <para><b>What the fix is about.</b> <c>Carryable._PhysicsProcess</c> runs on EVERY prop on
/// EVERY peer EVERY physics tick, and until this landed it ended with four unconditional writes
/// through the engine boundary — the visual node's <c>Scale</c>, the material's
/// <c>EmissionEnergyMultiplier</c>, the outline mesh's <c>Visible</c>, and the body's
/// <c>ApproachSpeedMps</c> read off <c>LinearVelocity</c>. Three of those four reach the
/// RenderingServer. A prop that has been resting on a shelf, untouched, for two minutes paid all
/// of them sixty times a second, and so did its 129 neighbours, on the server and on both
/// clients. That is work done for props nothing touched, which is a bug and not a budget.</para>
///
/// <para><b>Why the gate is re-evaluated every tick instead of latched with wake-up hooks.</b>
/// A latch needs every path that can disturb a prop to remember to clear it, and this repo has
/// paid four separate times for identity or state carried somewhere a caller could forget
/// (<c>.claude/rules/godot-scenes.md</c>'s `[Export]` trap). A prop that a missed hook left
/// frozen mid-pop would be a silent, permanent visual bug on one object in a room of two
/// thousand. Re-reading the condition costs one native property read per prop per tick and
/// cannot go stale: the tick after anything unfreezes, grabs, highlights or thumps the prop, the
/// full body runs again with no hook anywhere.</para>
/// </summary>
public static class CarryIdle
{
    /// <summary>How close the pop-spring's scale must be to 1 before it stops being written.
    /// The spring is critically-ish damped at 160/11, so it crosses this about 0.4 s after a
    /// pickup — well after the pop has read as a pop. A residual this size is 0.5 mm on a
    /// 0.12 m can.</summary>
    public const float ScaleEpsilon = 0.001f;

    /// <summary>How slow the pop-spring's velocity must be before it stops being written, in
    /// scale-units per second. Checked as well as the offset because a spring passing through
    /// 1.0 at speed is momentarily at its target and is not finished.</summary>
    public const float ScaleVelEpsilon = 0.002f;

    /// <summary>How close the emission lerp must be to its resting value before it stops being
    /// written. The lerp is 10/s toward the target, so it never arrives exactly; on the
    /// brightest authored prop in the game (a 2.5-energy gold band) this is 0.04 % of the
    /// value.</summary>
    public const float GlowEpsilon = 0.001f;

    /// <summary>
    /// True when every per-frame visual quantity on this prop has arrived and nothing can move
    /// it until an EVENT does.
    ///
    /// <para>Read the arguments as the four things that can still be in flight (the pop spring's
    /// offset and velocity, the emission lerp, the outline's visibility) plus the three things
    /// that mean an event is in progress (held, highlighted, a thunk cooldown still running) plus
    /// the one thing that means the physics solver may move the body under us
    /// (<paramref name="frozen"/> false — a loose prop on the server, or anything unfrozen at
    /// all).</para>
    ///
    /// <para><b><paramref name="homeSet"/> gates the whole thing</b> because the first tick of a
    /// prop's life is the one that captures its home position for kill-plane recovery, and a prop
    /// that went idle before taking it would have no home to be recovered to.</para>
    /// </summary>
    public static bool IsSettled(
        bool homeSet,
        bool frozen,
        bool held,
        bool highlighted,
        bool outlineVisible,
        double thunkCooldown,
        Vector3 visualScale,
        Vector3 visualScaleVel,
        float glow,
        float baseGlow)
        => homeSet
           && frozen
           && !held
           && !highlighted
           && !outlineVisible
           && thunkCooldown <= 0.0
           && visualScaleVel.LengthSquared() <= ScaleVelEpsilon * ScaleVelEpsilon
           && (visualScale - Vector3.One).LengthSquared() <= ScaleEpsilon * ScaleEpsilon
           && Mathf.Abs(glow - baseGlow) <= GlowEpsilon;
}
