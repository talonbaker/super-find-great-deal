using System.Collections.Generic;
using Godot;

namespace Sail.Game.Run;

/// <summary>
/// --flow-selftest: headless in-engine checks of the quota schedule's RESOURCE path — the one
/// seam <c>dotnet test</c> cannot reach, because a loaded .tres needs the engine. Everything
/// else about the playthrough machine and the quota arithmetic is pure and lives in the xUnit
/// suite (PlaythroughMachineTests, QuotaMathTests).
///
/// What this proves (acceptance criterion 6): (a) <c>assets/run/quota_schedule.tres</c> loads
/// as a <see cref="QuotaSchedule"/> with usable fields; (b) the demand curve the game would
/// use is computed from the FILE'S OWN fields through the same <see cref="QuotaMath"/> path
/// (expectations below derive from the loaded values, never from constants that would go
/// stale when a balance pass edits the file); (c) changing a schedule number changes the
/// curve through UNCHANGED code — "a balance tweak is a .tres edit with zero code changes",
/// demonstrated rather than asserted. Exits 0/1; tests/Run-FlowTest.ps1 gates on it.
/// </summary>
public static class FlowSelfTest
{
    private const string SchedulePath = "res://assets/run/quota_schedule.tres";

    private static readonly List<string> Failures = new();

    public static int Run()
    {
        GD.Print("[flow-selftest] loading " + SchedulePath);

        var schedule = GD.Load<QuotaSchedule>(SchedulePath);
        Check(schedule != null, $"{SchedulePath} failed to load as QuotaSchedule");
        if (schedule == null)
            return Finish();

        int[] early = schedule.EarlyRoundsDemand;
        float factor = schedule.TailGrowthFactor;
        Check(early is { Length: > 0 }, "EarlyRoundsDemand is empty — the shipped schedule must author an opening curve");
        Check(float.IsFinite(factor), $"TailGrowthFactor is not finite: {factor}");
        foreach (int d in early)
            Check(d > 0, $"EarlyRoundsDemand contains a non-positive entry ({d})");
        GD.Print($"[flow-selftest] loaded: early=[{string.Join(",", early)}] factor={factor} carry={schedule.CarrySurplus}");

        // (b) The curve, computed from the file's own fields. Expectations derive from the
        // loaded values: within the authored array the curve is the array itself as long as
        // the authored values already escalate (the strict-increase floor only ever raises).
        int previous = 0;
        for (int n = 1; n <= early.Length; n++)
        {
            int demand = QuotaMath.Demand(n, early, factor);
            int authored = early[n - 1];
            int expected = System.Math.Max(authored, previous + 1);
            Check(demand == expected,
                $"demand({n}) = {demand}, expected {expected} (authored {authored} under the strict-increase floor)");
            previous = demand;
        }
        // Past the array: one differential step against the documented formula, then the
        // structural property (strict increase) for a stretch of the open-ended tail.
        int last = QuotaMath.Demand(early.Length, early, factor);
        int firstTail = QuotaMath.Demand(early.Length + 1, early, factor);
        int formula = System.Math.Max((int)System.Math.Ceiling(last * (double)factor), last + 1);
        Check(firstTail == formula, $"demand({early.Length + 1}) = {firstTail}, formula says {formula}");
        int prev = last;
        for (int n = early.Length + 1; n <= early.Length + 10; n++)
        {
            int demand = QuotaMath.Demand(n, early, factor);
            Check(demand > prev, $"demand({n}) = {demand} did not escalate past demand({n - 1}) = {prev}");
            prev = demand;
        }

        // (c) The schedule-tweak proof: a resource-field change alters the curve through the
        // exact same code path — zero code edits, only data.
        var tweakedFactor = (QuotaSchedule)schedule.Duplicate();
        tweakedFactor.TailGrowthFactor = factor + 0.5f;
        Check(QuotaMath.Demand(early.Length + 3, tweakedFactor.EarlyRoundsDemand, tweakedFactor.TailGrowthFactor)
              != QuotaMath.Demand(early.Length + 3, early, factor),
            "raising TailGrowthFactor by 0.5 did not change the tail — the curve is not reading the data");

        var tweakedEarly = (QuotaSchedule)schedule.Duplicate();
        int[] bumped = (int[])early.Clone();
        bumped[0] += 1;
        tweakedEarly.EarlyRoundsDemand = bumped;
        Check(QuotaMath.Demand(1, tweakedEarly.EarlyRoundsDemand, tweakedEarly.TailGrowthFactor)
              == QuotaMath.Demand(1, early, factor) + 1,
            "bumping EarlyRoundsDemand[0] by 1 did not move demand(1) by 1 — the curve is not reading the data");

        return Finish();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            Failures.Add(message);
    }

    private static int Finish()
    {
        if (Failures.Count > 0)
        {
            foreach (string failure in Failures)
                GD.PrintErr("[flow-selftest] FAIL: " + failure);
            GD.Print($"[flow-selftest] FAIL ({Failures.Count} failure(s))");
            return 1;
        }
        GD.Print("[flow-selftest] PASS");
        return 0;
    }
}
