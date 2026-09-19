using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// Headless pure-logic checks of the tidal-cycle phase math (<see cref="CyclePhase"/>) — no
/// scene tree, no network, no live Godot node. The same CI-safe split every other *SelfTest in
/// this codebase
/// uses (ReconnectSelfTest, SteamSelfTest, ...): mechanical/deterministic logic proves itself
/// here; the networked replication/late-join/reconnect proof (which genuinely needs a live
/// server + bots) is Run-CycleTest.ps1's [2/2] live scenario. Run via --cycle-selftest;
/// exits the process 0/1.
/// </summary>
public static class CycleSelfTest
{
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        PhaseAdvancesMonotonicallyWithinAPeriod();
        PhaseWrapsExactlyAtTheBoundary();
        CyclesElapsedCountsWholePeriods();
        ZeroOrNegativePeriodFallsBackSafely();
        StartPhaseSeedsTheElapsedAccumulator();

        if (Failures.Count == 0)
        {
            GD.Print("[cycle-selftest] PASS (phase wrap/monotonic/cycles-elapsed, start-phase seed)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[cycle-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    // --- CyclePhase ------------------------------------------------------------------

    // Within one period, phase strictly increases as elapsed time grows (sampled at several
    // points) — the basic monotonic-within-a-cycle guarantee Run-CycleTest.ps1's live
    // scenario asserts across real bot samples.
    private static void PhaseAdvancesMonotonicallyWithinAPeriod()
    {
        const double period = 12.0;
        float prev = -1f;
        for (double t = 0; t < period; t += 0.5)
        {
            (float phase, int cycles) = CyclePhase.FromElapsed(t, period);
            Check(cycles == 0, $"monotonic-within-period: elapsed={t} expected cycles=0, got {cycles}");
            Check(phase >= prev, $"monotonic-within-period: phase went backward at elapsed={t} ({phase} < {prev})");
            prev = phase;
        }
    }

    // MECHANICS-BIBLE §1: the wrap boundary is exact — phase reaches arbitrarily close to
    // 1.0 but never equals it; a whole period elapsed maps to phase EXACTLY 0 of the next
    // cycle, with cyclesElapsed incremented.
    private static void PhaseWrapsExactlyAtTheBoundary()
    {
        const double period = 12.0;
        (float justBefore, int cyclesBefore) = CyclePhase.FromElapsed(period - 0.001, period);
        Check(justBefore < 1f && justBefore > 0.99f, $"wrap boundary: phase just before a period should be ~1.0 (got {justBefore})");
        Check(cyclesBefore == 0, $"wrap boundary: cyclesElapsed just before a period should still be 0 (got {cyclesBefore})");

        (float atBoundary, int cyclesAt) = CyclePhase.FromElapsed(period, period);
        Check(atBoundary == 0f, $"wrap boundary: phase AT exactly one period must be exactly 0 (got {atBoundary})");
        Check(cyclesAt == 1, $"wrap boundary: cyclesElapsed AT exactly one period must be 1 (got {cyclesAt})");
    }

    // Across several whole periods plus a remainder, cyclesElapsed must count exactly the
    // whole periods that have passed — this is the "cycle/sequence number" that lets a
    // client tell a wrap happened rather than time going backward (BUILD-SPEC §5).
    private static void CyclesElapsedCountsWholePeriods()
    {
        const double period = 12.0;
        (float phase, int cycles) = CyclePhase.FromElapsed(period * 3 + 5.0, period);
        Check(cycles == 3, $"cycles-elapsed: 3 full periods + 5s should read cyclesElapsed=3, got {cycles}");
        Check(System.Math.Abs(phase - 5.0 / period) < 1e-5f, $"cycles-elapsed: remainder phase should be 5/12, got {phase}");
    }

    // A launch arg of 0 or negative (malformed --cycle-period, or an uninitialized default)
    // must never divide by zero or produce NaN — MECHANICS-BIBLE §6, define the extremes.
    private static void ZeroOrNegativePeriodFallsBackSafely()
    {
        (float phaseZero, _) = CyclePhase.FromElapsed(5.0, 0.0);
        Check(float.IsFinite(phaseZero), "zero period: FromElapsed must not produce NaN/Infinity");
        (float phaseNeg, _) = CyclePhase.FromElapsed(5.0, -3.0);
        Check(float.IsFinite(phaseNeg), "negative period: FromElapsed must not produce NaN/Infinity");
    }

    // The --cycle-start-phase test hook seeds the elapsed accumulator, not just a display
    // value — a server started at phase 0.5 should already report phase 0.5 at elapsed=0,
    // and keep advancing from there.
    private static void StartPhaseSeedsTheElapsedAccumulator()
    {
        const double period = 12.0;
        double seededElapsed = 0.5 * period; // what CycleDriver.Setup computes from startPhase.
        (float phase, int cycles) = CyclePhase.FromElapsed(seededElapsed, period);
        Check(System.Math.Abs(phase - 0.5f) < 1e-5f, $"start-phase seed: expected phase 0.5, got {phase}");
        Check(cycles == 0, $"start-phase seed: expected cyclesElapsed 0, got {cycles}");
    }
}
