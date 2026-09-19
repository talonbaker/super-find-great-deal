using Godot;

namespace Sail.Game.Water;

/// <summary>
/// The cold clock (spec §5), as pure arithmetic. Not stamina: there is no stamina system in
/// this codebase and inventing a global one to gate swimming would leak into sprint, hiking
/// and firewood on day one. Chill is per-player, server-authoritative, lives in <c>[0,1]</c>,
/// accumulates only while <see cref="WaterState.Swimming"/>, and recovers everywhere else.
///
/// <b>It is a pure function of position.</b> No RNG, no wall-clock reads, no dependence on
/// frame rate beyond the <c>dt</c> handed in — replay the same sequence of (state, x, dt) and
/// you get the identical chill, every time, which is what <c>WaterChillTests</c> asserts and
/// what makes the value safe to be authoritative over.
/// </summary>
public static class ChillClock
{
    /// <summary>
    /// Advance chill by one step. Total by construction: a non-finite input chill is treated as
    /// 0, a non-finite or non-positive <paramref name="dt"/> is a no-op, and the result is
    /// always clamped into <c>[0,1]</c> — it can never silently go negative or past the max
    /// (MECHANICS-BIBLE §1).
    /// </summary>
    /// <param name="chill">Chill before the step.</param>
    /// <param name="state">Water state for the whole of this step.</param>
    /// <param name="x">World x, which sets the rate (the lake gets colder as it gets deeper).</param>
    /// <param name="dt">Step length in seconds.</param>
    public static float Advance(float chill, WaterState state, float x, float dt)
    {
        float c = float.IsFinite(chill) ? Mathf.Clamp(chill, 0f, 1f) : 0f;
        if (!float.IsFinite(dt) || dt <= 0f)
            return c;

        c += state == WaterState.Swimming
            ? dt * WaterGeometry.ChillRatePerSec(x)
            : -dt * WaterGeometry.ChillRecoveryPerSec;

        return Mathf.Clamp(c, 0f, 1f);
    }

    /// <summary>
    /// Seconds of continuous swimming at a fixed x before chill reaches exactly 1.0, starting
    /// from <paramref name="chill"/>. The bounded-time half of the anti-unwinnable guarantee
    /// (MECHANICS-BIBLE §10.5): every position in the lake has a finite answer here, so no
    /// player can be stranded — the cold always eventually collects them and puts them on the
    /// shore.
    /// </summary>
    public static float SecondsToFull(float chill, float x)
    {
        float c = float.IsFinite(chill) ? Mathf.Clamp(chill, 0f, 1f) : 0f;
        return (1f - c) * WaterGeometry.TimeToFullChillSec(x);
    }

    /// <summary>
    /// Chill at which the urgency cue (spec §5.1) starts being perceptible at all.
    ///
    /// <b>Lowered from 0.35 to 0.05 — 2026-08-08 playtest fallout, P2.</b> At 0.35 a player
    /// floating inside the rope saw and heard nothing for up to 63 s
    /// (<c>0.35 * TimeToFullInsideRopeSec</c>) before the first sign the cold existed at all —
    /// which is exactly what happened: Talon swam, floated, and "did not realize that anything
    /// was happening... with or without audio." The dispatch decision default is explicit — *cue
    /// the onset, not just the crisis* — so this now fires within roughly 9 s of continuous
    /// swimming even in the safest water and inside 2.5 s at the coldest. A player who dips in
    /// and straight back out for a couple of seconds still sees nothing; anyone who actually
    /// floats does, which is the distinction the old value got backwards.
    /// </summary>
    public const float CueOnsetChill = 0.05f;

    /// <summary>Cue intensity in <c>[0,1]</c>: 0 below <see cref="CueOnsetChill"/>, ramping to
    /// full at chill 1.0. One shared curve so the frost edge and the rumble quicken together
    /// rather than each inventing its own mapping.</summary>
    public static float CueIntensity(float chill)
    {
        if (!float.IsFinite(chill))
            return 0f;
        float c = Mathf.Clamp(chill, 0f, 1f);
        if (c <= CueOnsetChill)
            return 0f;
        return (c - CueOnsetChill) / (1f - CueOnsetChill);
    }
}
