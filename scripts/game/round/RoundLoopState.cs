using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>One rider's line on the tally card, frozen at the Round -&gt; Tally instant.</summary>
public readonly record struct RoundTallyLine(int RiderId, int Coins, int Shatters, int WitnessedShatters);

/// <summary>
/// The tally, computed exactly once (MECHANICS §4) at the Round -&gt; Tally commit and stored —
/// every tick in Tally reads this back rather than recomputing it, <c>ShiftLoop</c>'s
/// verdict-instant pattern.
/// </summary>
public readonly record struct RoundTallyResult(
    ImmutableArray<int> WinnerRiderIds,
    ImmutableArray<RoundTallyLine> Lines);

/// <summary>
/// <b>The whole round session, as one value</b> (D3). A <c>readonly record struct</c>, the
/// <c>ShiftLoopState</c> / <c>DeliveryState</c> idiom: no engine types, no clock of its own, no
/// randomness, and one mutator (<see cref="RoundLoop.Step"/>).
/// </summary>
public readonly record struct RoundLoopState
{
    /// <summary>Where in the loop we are.</summary>
    public RoundPhase Phase { get; init; }

    /// <summary>1-based. Round 1 is the session's first; there is no cap.</summary>
    public int RoundIndex { get; init; }

    /// <summary>Counting down in the current phase. One field for all four phases (not one timer
    /// per phase) because only one is ever live at a time and the wire's "remaining tenths" field
    /// is phase-agnostic by the same logic.</summary>
    public float RemainingSec { get; init; }

    /// <summary><b>Has Gathering's clock started?</b> Gathering does not count down until the
    /// first human is present (D3: "20 s after the first human joins", not 20 s after the phase
    /// was entered) — false at Restart and at every fresh Gathering entry, and never re-armed
    /// once true within that Gathering. The Correction ShiftLoopInput needed a sixth field for
    /// (<c>ClockHosted</c>) is the same shape of fact this flag exists to avoid needing: nothing
    /// downstream can tell "no human yet" from "the phase clock just started" without it.</summary>
    public bool GatherArmed { get; init; }

    /// <summary>Absolute carried coins, per rider, right now. The score.</summary>
    public ImmutableDictionary<int, int> CarriedCoinsPerRider { get; init; }

    /// <summary>Shatters this round, per rider, cumulative since the last reset commit.</summary>
    public ImmutableDictionary<int, int> ShattersPerRider { get; init; }

    /// <summary>Of those shatters, the ones WITNESS-1 marked witnessed. Always a subset of
    /// <see cref="ShattersPerRider"/> per rider (never counted without also counting there).</summary>
    public ImmutableDictionary<int, int> WitnessedShattersPerRider { get; init; }

    /// <summary>
    /// <b>True for exactly one tick: the Tally -&gt; Countdown commit.</b> The one idempotent
    /// signal a server driver watches to perform the world reset (coins to field, spill cleared,
    /// wrecks cleared, cars re-seeded, every rider <c>ResetTo</c> a marker) — driven by the loop,
    /// never by the UI. Cleared at the top of the very next <see cref="RoundLoop.Step"/> call, so
    /// a driver that steps once per tick observes it true for one and only one tick no matter how
    /// long that tick's <c>dt</c> is.
    /// </summary>
    public bool ResetRequested { get; init; }

    /// <summary>What the last tally decided. <c>null</c> before the first round ever finishes
    /// (mid Gathering/Countdown/Round of round 1) — there is no <c>None</c> sentinel because a
    /// round loop that ran a whole shift and had never once entered Tally by round 2 would be a
    /// different bug than "the card hasn't been drawn yet".</summary>
    public RoundTallyResult? LastTally { get; init; }
}
