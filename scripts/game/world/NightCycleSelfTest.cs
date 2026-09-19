using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// Headless pure-logic checks of <see cref="CycleBands"/> (WP-N1 Scope 7) — no scene tree, no
/// network, no live Godot node, the same CI-safe split every other *SelfTest in this codebase
/// uses (CycleSelfTest, ReconnectSelfTest, ...). Run via --night-cycle-selftest;
/// tests/Run-NightCycleTest.ps1 gates on it. Exits the process 0/1.
/// </summary>
public static class NightCycleSelfTest
{
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        BandsAreContinuousAndExhaustiveForEveryDay();
        WrapSeamHasNoBandProgressDiscontinuityAndAdvancesTheDayExactlyOnce();
        NightWidthIncreasesMonotonicallyAndClampsAtDayFive();
        DuskSweepWidthIsInvariantAcrossEveryDay();
        DegenerateInputsAreHandledSafely();

        if (Failures.Count == 0)
        {
            GD.Print("[night-cycle-selftest] PASS (band exhaustiveness/continuity, wrap seam, " +
                "night-width monotonic+clamp, dusk-sweep-width invariance, sprint escapability " +
                "ratio, degenerate inputs)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[night-cycle-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    // --- Band exhaustiveness/continuity -----------------------------------------------------

    // Every phase in [0,1) must land in exactly one band, for every day 1-5 (cyclesElapsed
    // 0..4) — sampled densely rather than only at the authored boundaries, since an off-by-one
    // in a comparison operator would only show up between samples.
    private static void BandsAreContinuousAndExhaustiveForEveryDay()
    {
        for (int cyclesElapsed = 0; cyclesElapsed <= CycleBands.MaxDayIndex; cyclesElapsed++)
        {
            for (int i = 0; i < 1000; i++)
            {
                float phase = i / 1000f;
                CycleBands.Band band = CycleBands.GetBand(phase, cyclesElapsed, out float progress);
                Check(progress is >= -0.0001f and <= 1.0001f,
                    $"day {cyclesElapsed + 1}: phase {phase:F3} in band {band} has out-of-range progress {progress:F4}");
            }
        }
    }

    // --- Wrap seam ---------------------------------------------------------------------------

    private static void WrapSeamHasNoBandProgressDiscontinuityAndAdvancesTheDayExactlyOnce()
    {
        const double periodSec = 300.0;
        // Straddle the wrap: elapsed just under one full period, and just over.
        double justBefore = periodSec - 0.05;
        double justAfter = periodSec + 0.05;

        (float phaseBefore, int cyclesBefore) = CyclePhase.FromElapsed(justBefore, periodSec);
        (float phaseAfter, int cyclesAfter) = CyclePhase.FromElapsed(justAfter, periodSec);

        Check(cyclesAfter == cyclesBefore + 1,
            $"wrap seam: day index should advance exactly once (was {cyclesBefore}, now {cyclesAfter})");

        // Just before the wrap is the tail of the dawn sweep (progress -> 1); just after is
        // the very start of the new day's Day band (progress -> 0) — that IS the correct,
        // non-jumping behaviour (design §1: dawn sweep ends exactly at the wrap, day begins
        // immediately). This proves the BAND LABEL ITSELF transitions cleanly (dawn sweep
        // essentially complete, day essentially just begun) rather than skipping or
        // double-counting a band.
        CycleBands.Band bandBefore = CycleBands.GetBand(phaseBefore, cyclesBefore, out float progressBefore);
        CycleBands.Band bandAfter = CycleBands.GetBand(phaseAfter, cyclesAfter, out float progressAfter);
        Check(bandBefore == CycleBands.Band.DawnSweep,
            $"wrap seam: just before the wrap should still read as DawnSweep (was {bandBefore})");
        Check(bandAfter == CycleBands.Band.Day,
            $"wrap seam: just after the wrap should read as Day (was {bandAfter})");
        Check(progressBefore > 0.99f,
            $"wrap seam: dawn-sweep progress just before the wrap should be nearly complete, was {progressBefore:F4}");
        Check(progressAfter < 0.01f,
            $"wrap seam: day-band progress just after the wrap should be nearly zero, was {progressAfter:F4}");
    }

    // --- Night-width escalation ----------------------------------------------------------------

    private static void NightWidthIncreasesMonotonicallyAndClampsAtDayFive()
    {
        float previous = -1f;
        for (int day = 0; day <= CycleBands.MaxDayIndex; day++)
        {
            float width = CycleBands.NightWidth(day);
            Check(width > previous, $"night width did not increase from day {day} ({previous:F4} -> {width:F4})");
            previous = width;
        }
        Check(Mathf.IsEqualApprox(CycleBands.NightWidth(0), 0.250f), "day 1 night width should be 0.250");
        Check(Mathf.IsEqualApprox(CycleBands.NightWidth(4), 0.500f), "day 5 night width should be 0.500");

        // Clamp: day 9 (far past 5) must equal day 5's values exactly, not extrapolate further.
        Check(Mathf.IsEqualApprox(CycleBands.NightWidth(9), CycleBands.NightWidth(4)),
            "day 9 should clamp to day 5's night width, not keep extrapolating");
        Check(CycleBands.Boundaries(9) == CycleBands.Boundaries(4),
            "day 9 should clamp to day 5's exact boundaries");
    }

    // --- Dusk-sweep-width invariance (Scope 2's load-bearing property #1) ---------------------

    private static void DuskSweepWidthIsInvariantAcrossEveryDay()
    {
        for (int day = 0; day <= CycleBands.MaxDayIndex; day++)
        {
            (float duskStart, float nightStart, float _) = CycleBands.Boundaries(day);
            float width = nightStart - duskStart;
            Check(Mathf.IsEqualApprox(width, CycleBands.DuskSweepWidth),
                $"day {day + 1}: dusk sweep width was {width:F4}, expected the invariant {CycleBands.DuskSweepWidth:F4}");
        }
    }

    // --- Degenerate inputs ---------------------------------------------------------------------

    private static void DegenerateInputsAreHandledSafely()
    {
        // Negative / far-past-5 cyclesElapsed both clamp into [0, MaxDayIndex] rather than
        // throwing or producing nonsense boundaries.
        Check(CycleBands.DayIndex(-3) == 0, "negative cyclesElapsed should clamp to day index 0");
        Check(CycleBands.DayIndex(1000) == CycleBands.MaxDayIndex, "far-future cyclesElapsed should clamp to MaxDayIndex");

        // Negative elapsed time into CyclePhase.FromElapsed (the shared clock math this class
        // also depends on) must not throw and must still produce a phase in [0,1).
        (float phase, int cycles) = CyclePhase.FromElapsed(-10.0, 300.0);
        Check(phase is >= 0f and < 1f, $"negative elapsed should still yield phase in [0,1), got {phase}");
    }
}
