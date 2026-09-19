using System;

namespace Sail.Game.Run;

/// <summary>
/// Pure quota arithmetic (core-spine spec §2.2), pulled out of <see cref="QuotaLedger"/> so it
/// is directly testable without a scene tree, a Multiplayer API, or a loaded Resource — the
/// exact seam <c>RunPhaseTracker</c> gives <c>RunDriver</c> and <c>CyclePhase</c> gives
/// <c>CycleDriver</c>. The schedule's numbers live in <c>assets/run/quota_schedule.tres</c>
/// (<see cref="QuotaSchedule"/>); this class only ever computes from whatever fields it is
/// handed, which is what makes "a balance pass edits the .tres and zero code changes" true by
/// construction (acceptance criterion 6).
///
/// The demand curve: <c>early[n-1]</c> while in range, then <c>ceil(previous * factor)</c>,
/// with a STRICT-INCREASE floor <c>demand(n) >= demand(n-1) + 1</c> applied to the authored
/// values and the tail alike — the brief's "each round's quota escalates past the previous
/// round's", enforced structurally so no data mistake (a flat array, a factor of 0.9) can
/// flatten the ramp. Extremes (MECHANICS-BIBLE §6): empty/null array falls back to [3];
/// factor &lt;= 1 degrades the tail to linear +1/round via the floor rather than dividing or
/// flat-lining; everything clamps at <see cref="DemandClamp"/> — the int-overflow guard the
/// open-ended round count needs (spec §6 case 14), and the one place the strict-increase
/// floor deliberately loses (a capped curve stays capped).
/// </summary>
public static class QuotaMath
{
    /// <summary>Spec §2.2's overflow guard. Demand and cumulative demand both cap here; a
    /// playthrough that reaches it is hundreds of rounds past any real session and is doomed
    /// by arithmetic anyway (banked can never approach it), so capping the cumulative sum at
    /// the same bound — rather than widening the wire type to long — keeps every wire payload
    /// an int. (Value call, CORE-PROG-A1: the spec clamps demand and is silent on the
    /// cumulative sum's type; capping both at one bound is the smallest consistent answer.)</summary>
    public const int DemandClamp = 1_000_000_000;

    /// <summary>The empty/null-array fallback curve (spec §2.2).</summary>
    public static readonly int[] FallbackEarlyRounds = { 3 };

    /// <summary>demand(n), 1-based round. Defensive on every input: round &lt; 1 reads as 1,
    /// a null/empty array falls back, non-positive authored entries are floored to 1.</summary>
    public static int Demand(int round, int[]? earlyRounds, float tailGrowthFactor)
    {
        if (round < 1)
            round = 1;
        int[] early = earlyRounds is { Length: > 0 } ? earlyRounds : FallbackEarlyRounds;

        int d = Math.Clamp(early[0], 1, DemandClamp);
        for (int n = 2; n <= round; n++)
        {
            long candidate = n - 1 < early.Length
                ? early[n - 1]
                : (long)Math.Ceiling(d * (double)tailGrowthFactor);
            long floored = Math.Max(candidate, (long)d + 1); // strict increase, authored or not
            d = (int)Math.Min(floored, DemandClamp);
        }
        return d;
    }

    /// <summary>Sum of demand(1..round), capped at <see cref="DemandClamp"/> (see that
    /// constant's doc for why the cumulative sum shares the demand bound).</summary>
    public static int CumulativeDemand(int round, int[]? earlyRounds, float tailGrowthFactor)
    {
        if (round < 1)
            round = 1;
        long sum = 0;
        for (int n = 1; n <= round; n++)
        {
            sum += Demand(n, earlyRounds, tailGrowthFactor);
            if (sum >= DemandClamp)
                return DemandClamp;
        }
        return (int)sum;
    }

    /// <summary>Spec §2.4 / §5.6: banking is accepted while play is live — RoundIntro or
    /// InRound — and denied with feedback everywhere else. One function so the server guard
    /// and every test agree by construction.</summary>
    public static bool BankingOpen(PlaythroughState state) =>
        state is PlaythroughState.RoundIntro or PlaythroughState.InRound;

    /// <summary>The verdict-instant quota decision, both carry models (spec §2.1): cumulative
    /// stockpile (<paramref name="carrySurplus"/> true — surplus rolls forward) or per-round
    /// bucket (false — this round's banked against this round's demand alone).</summary>
    public static bool QuotaMissed(bool carrySurplus,
        int cumulativeBanked, int cumulativeDemand, int bankedThisRound, int demandThisRound) =>
        carrySurplus
            ? cumulativeBanked < cumulativeDemand
            : bankedThisRound < demandThisRound;
}
