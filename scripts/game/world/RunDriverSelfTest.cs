using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// Headless pure-logic checks of the phase-crossing/run-end ordinal math
/// (<c>RunPhaseTracker</c>) that <see cref="RunDriver"/> drives its replicated events
/// from — no scene tree, no network, no live Godot node. The same CI-safe split every other
/// *SelfTest in this codebase uses: mechanical/deterministic logic proves itself here; the
/// networked replication/late-join/reset proof (which genuinely needs a live server + bots) is
/// Run-RunDriverTest.ps1's live scenario. Run via --run-driver-selftest; exits the process 0/1.
/// </summary>
public static class RunDriverSelfTest
{
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        OrdinalEncodesBandAndCycleMonotonically();
        SingleStepCrossingsMatchTheDesignedSequenceAcrossTwoCycles();
        NoForwardProgressYieldsNoCrossings();
        AMultiBandSkipInOneTickStillYieldsEveryCrossingInOrder();
        RunEndFiresOnlyAtTheConfiguredCyclesFinalDawnToDayCrossing();
        LaunchOptionsParseRunCyclesAndRunResetAt();

        if (Failures.Count == 0)
        {
            GD.Print("[run-driver-selftest] PASS (ordinal encoding, single-step sequence across " +
                "2 cycles, no-progress no-op, multi-band-skip catch-up, run-end threshold, " +
                "--run-cycles/--run-reset-at parsing)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[run-driver-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    // --- RunPhaseTracker.Ordinal --------------------------------------------------------

    // Band index (mod 4) must match the design's fixed cyclic order, and cyclesElapsed must
    // scale the ordinal by whole cycles — the two properties CrossingsBetween's range-walk
    // depends on to never mislabel which cycle a crossing belongs to.
    private static void OrdinalEncodesBandAndCycleMonotonically()
    {
        Check(RunPhaseTracker.Ordinal(CycleBands.Band.Day, 0) == 0, "ordinal: Day@cycle0 should be 0");
        Check(RunPhaseTracker.Ordinal(CycleBands.Band.DuskSweep, 0) == 1, "ordinal: DuskSweep@cycle0 should be 1");
        Check(RunPhaseTracker.Ordinal(CycleBands.Band.Night, 0) == 2, "ordinal: Night@cycle0 should be 2");
        Check(RunPhaseTracker.Ordinal(CycleBands.Band.DawnSweep, 0) == 3, "ordinal: DawnSweep@cycle0 should be 3");
        Check(RunPhaseTracker.Ordinal(CycleBands.Band.Day, 1) == 4, "ordinal: Day@cycle1 should be 4 (wraps forward, never resets to 0)");
        Check(RunPhaseTracker.Ordinal(CycleBands.Band.DawnSweep, 2) == 11, "ordinal: DawnSweep@cycle2 should be 11");
    }

    // --- RunPhaseTracker.CrossingsBetween ------------------------------------------------

    // The exact sequence a real 2-cycle run visits, one boundary at a time (the steady-state
    // case: DetectAndEmitCrossings calls this every physics tick with ordinal advancing by at
    // most 1). Each of the four named events must appear, in order, with the correct
    // CyclesElapsedAfter — this is "exactly once per crossing, in order" as pure math, the same
    // property Run-RunDriverTest.ps1's live scenario re-proves over an actual replicated session.
    private static void SingleStepCrossingsMatchTheDesignedSequenceAcrossTwoCycles()
    {
        var expected = new (PhaseEventKind Kind, int CyclesAfter)[]
        {
            (PhaseEventKind.DayToDusk, 0),
            (PhaseEventKind.DuskToNight, 0),
            (PhaseEventKind.NightToDawn, 0),
            (PhaseEventKind.DawnToDay, 1),
            (PhaseEventKind.DayToDusk, 1),
            (PhaseEventKind.DuskToNight, 1),
            (PhaseEventKind.NightToDawn, 1),
            (PhaseEventKind.DawnToDay, 2),
        };
        int ordinal = 0; // seeded at Day/cycle0, same as DetectAndEmitCrossings' first sample.
        var actual = new List<(PhaseEventKind, int)>();
        for (int i = 0; i < expected.Length; i++)
        {
            int next = ordinal + 1;
            actual.AddRange(RunPhaseTracker.CrossingsBetween(ordinal, next));
            ordinal = next;
        }
        Check(actual.Count == expected.Length,
            $"single-step sequence: expected {expected.Length} crossings, got {actual.Count} ({string.Join(", ", actual)})");
        for (int i = 0; i < System.Math.Min(actual.Count, expected.Length); i++)
        {
            Check(actual[i].Item1 == expected[i].Kind && actual[i].Item2 == expected[i].CyclesAfter,
                $"single-step sequence[{i}]: expected {expected[i]}, got {actual[i]}");
        }
    }

    // A same-band re-sample (or, defensively, a clock that never regresses) must yield nothing —
    // "never emit twice on a frame where nothing crossed."
    private static void NoForwardProgressYieldsNoCrossings()
    {
        var same = RunPhaseTracker.CrossingsBetween(5, 5).ToList();
        Check(same.Count == 0, $"no-progress: same ordinal should yield 0 crossings, got {same.Count}");
        var backward = RunPhaseTracker.CrossingsBetween(5, 3).ToList();
        Check(backward.Count == 0, $"no-progress: a smaller target ordinal should yield 0 crossings, got {backward.Count}");
    }

    // MECHANICS-BIBLE boundary-condition discipline: a server physics tick that (defensively —
    // this should not happen in the steady state) skips MULTIPLE bands in one delta must still
    // report every crossing it skipped, in order, not just the last one — "never zero on a
    // jump," the same property CycleDriver's own snap-correct rule protects for the raw phase
    // float, applied here to this driver's own exactly-once contract.
    private static void AMultiBandSkipInOneTickStillYieldsEveryCrossingInOrder()
    {
        // From Day/cycle0 (ordinal 0) to DawnSweep/cycle1 (ordinal 7) in one call: skips 7
        // boundaries across a full cycle plus three more bands.
        var crossings = RunPhaseTracker.CrossingsBetween(0, 7).ToList();
        Check(crossings.Count == 7, $"multi-band-skip: expected 7 crossings, got {crossings.Count}");
        var expectedKinds = new[]
        {
            PhaseEventKind.DayToDusk, PhaseEventKind.DuskToNight, PhaseEventKind.NightToDawn,
            PhaseEventKind.DawnToDay, PhaseEventKind.DayToDusk, PhaseEventKind.DuskToNight,
            PhaseEventKind.NightToDawn,
        };
        for (int i = 0; i < System.Math.Min(crossings.Count, expectedKinds.Length); i++)
        {
            Check(crossings[i].Kind == expectedKinds[i],
                $"multi-band-skip[{i}]: expected {expectedKinds[i]}, got {crossings[i].Kind}");
        }
        // The lone DawnToDay in this range must report the cycle it just entered (1), not the
        // one it left (0) — CyclesElapsedAfter, not CyclesElapsedBefore.
        Check(crossings[3].CyclesElapsedAfter == 1,
            $"multi-band-skip: the DawnToDay crossing should report CyclesElapsedAfter=1, got {crossings[3].CyclesElapsedAfter}");
    }

    // --- RunPhaseTracker.IsRunEndCrossing ------------------------------------------------

    // Design §1 / L1 scope item 3: run-end is a DawnToDay crossing at or past the configured
    // cycle count. cyclesElapsedAfter=1 with runCycles=2 (cycle 0 closing, cycle 1 still owed)
    // must NOT end the run; cyclesElapsedAfter=2 must. Any other event kind never ends a run,
    // regardless of the cycle count.
    private static void RunEndFiresOnlyAtTheConfiguredCyclesFinalDawnToDayCrossing()
    {
        Check(!RunPhaseTracker.IsRunEndCrossing(PhaseEventKind.DawnToDay, 1, 2),
            "run-end: cycle 0 closing (cyclesAfter=1) with runCycles=2 must NOT end the run");
        Check(RunPhaseTracker.IsRunEndCrossing(PhaseEventKind.DawnToDay, 2, 2),
            "run-end: cycle 1 closing (cyclesAfter=2) with runCycles=2 MUST end the run");
        Check(RunPhaseTracker.IsRunEndCrossing(PhaseEventKind.DawnToDay, 5, 2),
            "run-end: a cycle count past the configured length must still read as ended (defensive)");
        Check(!RunPhaseTracker.IsRunEndCrossing(PhaseEventKind.NightToDawn, 2, 2),
            "run-end: only a DawnToDay crossing may end a run, never NightToDawn");
        Check(!RunPhaseTracker.IsRunEndCrossing(PhaseEventKind.DayToDusk, 2, 2),
            "run-end: only a DawnToDay crossing may end a run, never DayToDusk");
    }

    // --- LaunchOptions --------------------------------------------------------------------

    private static void LaunchOptionsParseRunCyclesAndRunResetAt()
    {
        var withCycles = LaunchOptions.Parse(new[] { "--server", "--run-cycles", "3" });
        Check(withCycles.RunCycles == 3, $"--run-cycles 3 should parse to 3, got {withCycles.RunCycles}");

        var zeroCycles = LaunchOptions.Parse(new[] { "--server", "--run-cycles", "0" });
        Check(zeroCycles.RunCycles == 0, "--run-cycles 0 should be ignored (stays the 0=unset sentinel)");

        var negativeCycles = LaunchOptions.Parse(new[] { "--server", "--run-cycles", "-1" });
        Check(negativeCycles.RunCycles == 0, "--run-cycles -1 should be ignored (stays the 0=unset sentinel)");

        var defaulted = LaunchOptions.Parse(new[] { "--server" });
        Check(defaulted.RunCycles == 0, "no --run-cycles flag should leave the 0=unset sentinel");
        Check(defaulted.RunResetAtSec < 0, "no --run-reset-at flag should leave the disabled (<0) sentinel");

        var withReset = LaunchOptions.Parse(new[] { "--server", "--run-reset-at", "12.5" });
        Check(System.Math.Abs(withReset.RunResetAtSec - 12.5) < 1e-6, $"--run-reset-at 12.5 should parse to 12.5, got {withReset.RunResetAtSec}");

        var selfTest = LaunchOptions.Parse(new[] { "--run-driver-selftest" });
        Check(selfTest.RunDriverSelfTest, "--run-driver-selftest should set the flag");
    }
}
