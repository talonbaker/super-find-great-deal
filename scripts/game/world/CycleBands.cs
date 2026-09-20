using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// Single source of truth for the loop-v1 5-minute day's phase bands (hand-loop design §1) and
/// their escalation across a run (design §1: "night lengthens as the run progresses"). Pulled
/// out as a pure static class, the same seam <see cref="CyclePhase"/> gives <see cref="CycleDriver"/>
/// — no scene tree, no Node, no Multiplayer API, directly testable headless
/// (tests/Run-NightCycleTest.ps1).
///
/// <b>Why this exists:</b> the two drafts this packet unifies (<see cref="OutdoorAtmosphere"/>,
/// an earlier night dome) each independently hard-coded day-1's band boundaries. That is two
/// copies of one fact (LEVEL-BIBLE §1), and it was about to get worse: design §1 decided the
/// night lengthens day-over-day, which means the boundaries themselves now vary by
/// <see cref="CycleDriver.CyclesElapsed"/>. Both components read THIS class; neither keeps its
/// own copy, so the sky and the dome can never independently disagree about which band the
/// world is in right now.
///
/// <b>The escalation table (design §1, decided values; structure fixed, numbers re-tunable):</b>
/// <code>
///                                  day 1(d=0)  day 2    day 3    day 4    day 5+(d=4)
///  Day ends / dusk sweep starts     0.59375    0.54375  0.49375  0.44375  0.39375
///  Dusk sweep ends / night starts   0.650      0.600    0.550    0.500    0.450
///  Night ends / dawn sweep starts   0.900      0.9125   0.925    0.9375   0.950
///  Wrap                             1.000      1.000    1.000    1.000    1.000
///  Night width (derived)            0.250      0.3125   0.375    0.4375   0.500
/// </code>
///
/// <b>Two load-bearing properties, kept even if the numbers above are re-tuned:</b>
/// <list type="number">
/// <item><see cref="DuskSweepWidth"/> is invariant at 0.05625 of the cycle on every day (it was
/// 0.100 until CONST-1, 2026-08-21). A dusk sweep that narrowed on later days would silently
/// change how fast night arrives relative to the day around it; the escalation lengthens the
/// night, never the sweep.</item>
/// <item>Night both starts earlier AND ends later as the run progresses — moving only one end
/// would give half the escalation design §1 asks for.</item>
/// </list>
/// The values clamp at day 5 (<see cref="MaxDayIndex"/>) and the cycle keeps looping past it —
/// run-end/win-loss shape is `TBD — Talon` (design §1) and not this packet's concern.
/// </summary>
public static class CycleBands
{
    public enum Band
    {
        Day,
        DuskSweep,
        Night,
        DawnSweep,
    }

    /// <summary>Day index clamps here (design §1: "day 5+"); 0-based, so day 1 = index 0.</summary>
    public const int MaxDayIndex = 4;

    /// <summary>Invariant across every day — see the class doc's load-bearing property #1.
    ///
    /// <para>0.100 → 0.05625 (CONST-1, 2026-08-21). The value was derived from a since-removed
    /// level's "night front outruns a sprint by 1.05–1.15×" contract; the invariance across days
    /// is the property that survives, and the value is kept because every kept level and test was
    /// authored against it.</para></summary>
    public const float DuskSweepWidth = 0.05625f;

    // "Day ends / dusk sweep starts" — also the earlier night dome's DuskStartPhase. Each entry is its
    // NightStartByDay partner minus DuskSweepWidth: the sweep narrowed (see that constant), so the
    // day band grew by 0.04375 of the cycle and the night is untouched.
    private static readonly float[] DuskStartByDay = { 0.59375f, 0.54375f, 0.49375f, 0.44375f, 0.39375f };

    // "Dusk sweep ends / night starts" — also the earlier night dome's NightStartPhase. Invariant
    // gap vs. DuskStartByDay (see DuskSweepWidth) — asserted by the headless test, not just
    // documented.
    private static readonly float[] NightStartByDay = { 0.650f, 0.600f, 0.550f, 0.500f, 0.450f };

    // "Night ends / dawn sweep starts" — also the earlier night dome's DawnStartPhase.
    private static readonly float[] DawnStartByDay = { 0.900f, 0.9125f, 0.925f, 0.9375f, 0.950f };

    /// <summary>Clamps a raw elapsed-cycle count to the escalation table's range. Negative
    /// input (a degenerate/defensive case — CyclesElapsed is never actually negative in
    /// practice) and anything past day 5 both land on day 5's values.</summary>
    public static int DayIndex(int cyclesElapsed) => Mathf.Clamp(cyclesElapsed, 0, MaxDayIndex);

    /// <summary>The three named phase boundaries for the given day (see class doc table).
    /// Both <see cref="OutdoorAtmosphere"/> and an earlier night dome derive everything from
    /// this one call.</summary>
    public static (float DuskStart, float NightStart, float DawnStart) Boundaries(int cyclesElapsed)
    {
        int d = DayIndex(cyclesElapsed);
        return (DuskStartByDay[d], NightStartByDay[d], DawnStartByDay[d]);
    }

    /// <summary>Night-band width only (excludes both sweeps) — the table's "Night width" row,
    /// 0.250 (day 1) rising monotonically to 0.500 (day 5+, clamped).</summary>
    public static float NightWidth(int cyclesElapsed)
    {
        (float duskStart, float nightStart, float dawnStart) = Boundaries(cyclesElapsed);
        return dawnStart - nightStart;
    }

    /// <summary>Which band <paramref name="phase"/> falls in for the given day, plus normalized
    /// progress [0,1] within that band. Bands are continuous and exhaustive by construction —
    /// every phase in [0,1) lands in exactly one (asserted by the headless test).</summary>
    public static Band GetBand(float phase, int cyclesElapsed, out float progress)
    {
        phase = Mathf.PosMod(phase, 1f);
        (float duskStart, float nightStart, float dawnStart) = Boundaries(cyclesElapsed);

        if (phase < duskStart)
        {
            progress = duskStart > 0f ? phase / duskStart : 0f;
            return Band.Day;
        }
        if (phase < nightStart)
        {
            progress = (phase - duskStart) / Mathf.Max(nightStart - duskStart, 1e-5f);
            return Band.DuskSweep;
        }
        if (phase < dawnStart)
        {
            progress = (phase - nightStart) / Mathf.Max(dawnStart - nightStart, 1e-5f);
            return Band.Night;
        }
        progress = (phase - dawnStart) / Mathf.Max(1f - dawnStart, 1e-5f);
        return Band.DawnSweep;
    }

    /// <summary>Ten phase breakpoints for <see cref="OutdoorAtmosphere"/>'s piecewise keyframe
    /// curves, scaled to the given day's bands. The interior keys' FRACTIONAL position within
    /// their own band is fixed at what the original day-1 draft picked (midday at 50% of the
    /// day band; dusk gold-peak/purple at 1/3 and 2/3 of the dusk band; deep night at 52% of
    /// the night band; dawn gold-peak at 55% of the dawn band) — see OutdoorAtmosphere's class
    /// doc for the original absolute values this reproduces exactly on day 1 (d=0). Re-tuning
    /// night length across the run never moves where "midday" or "deep night" sits relative to
    /// its own band.</summary>
    public static float[] AtmosphereBreakpoints(int cyclesElapsed)
    {
        (float duskStart, float nightStart, float dawnStart) = Boundaries(cyclesElapsed);
        return new[]
        {
            0.000000f,                                             // 0: day start.
            duskStart * 0.5f,                                       // 1: midday.
            duskStart,                                              // 2: dusk sweep start.
            duskStart + (nightStart - duskStart) * (1f / 3f),       // 3: dusk gold peak.
            duskStart + (nightStart - duskStart) * (2f / 3f),       // 4: dusk purple.
            nightStart,                                             // 5: night start.
            nightStart + (dawnStart - nightStart) * 0.52f,          // 6: deep night.
            dawnStart,                                              // 7: night end / dawn start.
            dawnStart + (1f - dawnStart) * 0.55f,                   // 8: dawn gold peak.
            1.000000f,                                              // 9: wrap == index 0.
        };
    }

    // --- Named phases (STYLE-4, 2026-08-20) -----------------------------------------------------
    //
    // A LOOKUP ONLY. Every value below is read out of AtmosphereBreakpoints, which is read out of
    // Boundaries. No band value is defined here, none is duplicated here, and re-tuning the
    // escalation table above moves every name below with it by construction.
    //
    // WHY THIS EXISTS. The visual-style brief (2026-08-20 §2) makes flat midday daylight the one
    // lighting condition every asset is judged under, which means "noon" stops being a number a
    // reviewer looks up and becomes something a launch line has to be able to say.
    // `--cycle-start-phase 0.296875` is right for day 1 and silently wrong for day 5 (0.196875), and
    // nothing about the digits tells you which day they were computed for. The name is
    // day-relative; the digits are not.

    /// <summary>Index into <see cref="AtmosphereBreakpoints"/> for each supported name. That array
    /// is the single source: its index 1 is its own "midday", its index 6 its own "deep night",
    /// and so on — see that method's doc comment.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, int> NamedBreakpointIndex =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["day-start"] = 0,
            ["noon"] = 1,
            ["midday"] = 1,
            ["dusk"] = 2,
            ["night"] = 5,
            ["midnight"] = 6,
            ["deep-night"] = 6,
            ["dawn"] = 7,
        };

    /// <summary>Every name <see cref="TryNamedPhase"/> accepts, for error messages and help text.
    /// Sorted so a printed list is stable rather than dictionary-ordered.</summary>
    public static string[] PhaseNames
    {
        get
        {
            var names = new string[NamedBreakpointIndex.Count];
            NamedBreakpointIndex.Keys.CopyTo(names, 0);
            System.Array.Sort(names, System.StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>Resolves a named phase against a given day's bands — "noon" is 0.296875 on day 1
    /// and 0.196875 on day 5+ (both moved from 0.275/0.175 when CONST-1 narrowed
    /// <see cref="DuskSweepWidth"/>; this is exactly why STYLE-4 built the name and told reviewers
    /// not to hardcode the digits). Returns false (and 0) for an unknown name so a caller can tell a mistyped
    /// name from a legitimate phase 0, rather than silently reviewing the world at dawn.
    ///
    /// <para><b>"noon" is the DAY BAND'S CENTRE</b> — <see cref="AtmosphereBreakpoints"/> index 1,
    /// i.e. <c>duskStart * 0.5</c>. STYLE-4 asked whether the band centre or the sun's elevation
    /// maximum was meant; on this world they are the same instant, so there is no fork to escalate.
    /// <c>OutdoorAtmosphere</c>'s <c>SunElevDeg</c> peaks at 62° on exactly this breakpoint and its
    /// <c>SunEnergyMul</c> sits on its 1.0 plateau across it. If a future atmosphere re-key ever
    /// moves the sun's peak off index 1, THIS doc comment is what has to be re-decided — do not
    /// quietly re-point the index.</para></summary>
    public static bool TryNamedPhase(string name, int cyclesElapsed, out float phase)
    {
        phase = 0f;
        if (string.IsNullOrWhiteSpace(name) || !NamedBreakpointIndex.TryGetValue(name.Trim(), out int idx))
            return false;
        phase = AtmosphereBreakpoints(cyclesElapsed)[idx];
        return true;
    }
}
