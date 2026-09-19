using Godot;

namespace Sail.Game.Run;

/// <summary>
/// The quota schedule as DATA (core-spine spec §2.2) — the exact file a balance pass edits is
/// <c>assets/run/quota_schedule.tres</c>, and every number tweak there is a .tres edit with
/// zero code changes (acceptance criterion 6; the RagdollProfile pattern: custom Resource
/// class + .tres under assets/). Loaded by the SERVER at Boot; the same file ships to clients
/// in the pack but only the server's numbers are ever authoritative — every demand a client
/// displays arrived in a broadcast or sync payload (version-skew immunity by construction).
///
/// All three values are playtest placeholders (spec §8); balancing is out of CORE-PROG-A1's
/// scope. The demand formula, its strict-increase floor and its extremes live in
/// <see cref="QuotaMath"/> — this class is a data shell and computes nothing.
/// </summary>
[GlobalClass]
public partial class QuotaSchedule : Resource
{
    /// <summary>Hand-authored opening curve, demand per round, 1-based. Placeholder [3,5,8,12].</summary>
    [Export] public int[] EarlyRoundsDemand { get; set; } = { 3, 5, 8, 12 };

    /// <summary>Past the array: demand(n) = ceil(demand(n-1) * this), under QuotaMath's
    /// strict-increase floor (a factor &lt;= 1 degrades to +1/round, never flat). Placeholder 1.35.</summary>
    [Export] public float TailGrowthFactor { get; set; } = 1.35f;

    /// <summary>Spec §2.1's carry model. True (placeholder): the winter cache is a cumulative
    /// stockpile — surplus banked in round N counts toward round N+1, and the verdict compares
    /// cumulative banked against cumulative demand. False: per-round bucket — the verdict is
    /// bankedThisRound >= demand(N), the round counter resetting each round. One flag flips
    /// the model with no code change (SD-1 decision D3 records why true ships).</summary>
    [Export] public bool CarrySurplus { get; set; } = true;
}
