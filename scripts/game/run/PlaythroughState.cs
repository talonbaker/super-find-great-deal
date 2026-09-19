namespace Sail.Game.Run;

/// <summary>
/// The Tier-2 playthrough states (core-spine spec §1.2, CORE-PROG-A1) — the networked,
/// server-authoritative machine <see cref="PlaythroughDriver"/> owns. The brief's states 3–5
/// (Day / Day→Night / Night) are deliberately NOT here: they are <see cref="InRound"/> ×
/// <c>CycleBands.Band</c>, read from the locked crossing layer — duplicating them as outer
/// states would be two copies of one fact, the exact drift CycleBands' own doc warns about.
/// Wire-encoded as a byte (see PlaythroughDriver's Rpc signatures); append-only.
/// </summary>
public enum PlaythroughState : byte
{
    Boot         = 0, // server constructing / client connecting; pre-round-1
    RoundIntro   = 1, // brief state 2 — "Round N" card, quota announced; play is live
    InRound      = 2, // brief states 3–5; fine structure is the Band
    RoundEnd     = 3, // brief state 6 — tally screen; gameplay-inert
    UpgradeLobby = 4, // brief state 7 — timed social interlude; gameplay-inert
    Loss         = 5, // brief state 8 — run over; world intact behind the screen
}

/// <summary>The spec §1.2 (state × band) table's "Band is gameplay-live?" column, as one pure
/// predicate (CORE-PROG-A2 scope 6). Any system whose behavior keys on the band or a crossing
/// for GAMEPLAY effect — payouts, cutoffs, chill/freeze pressure — additionally gates on this;
/// presentation consumers (toasts, HUD, telemetry, the sky itself) never do. Kept beside the
/// enum rather than on the driver so the guard is testable by <c>dotnet test</c> and every
/// consumer's one-line gate calls the SAME predicate — no second copy of the table to drift.</summary>
public static class PlaythroughStates
{
    public static bool IsBandLive(PlaythroughState state) =>
        state is PlaythroughState.RoundIntro or PlaythroughState.InRound;
}

/// <summary>Why a playthrough ended. Ordinals cross the wire — append only, like every wire
/// enum here. Exactly one kind ships under the open-ended model (canon
/// fact 15 as amended 2026-08-13): a missed quota is the only ending.</summary>
public enum RunOutcomeKind : byte
{
    QuotaMissed = 0,
}

/// <summary>One per playthrough, loss-only under the open-ended model. Demand/Banked are the
/// numbers the verdict actually compared (cumulative under carry_surplus, per-round otherwise
/// — spec §2.1), carried so the loss screen's honesty needs no second query.</summary>
public readonly record struct RunOutcome(
    RunOutcomeKind Kind,
    int Round,   // the round whose night ended the run
    int Demand,  // what was due at the verdict instant
    int Banked); // what was actually banked

/// <summary>Fired once per SURVIVED round (spec §1.6 — round-end and run-end are distinct on
/// the wire and in the API, never one event with a flag). Demand/Banked are cumulative (spec
/// §2.1). Per-player contribution lines are deliberately NOT in v0 — adding a per-peer
/// breakdown later is an append to this record plus a broadcast field, no reshape.</summary>
public readonly record struct RoundSummary(int Round, int Demand, int Banked, int NextDemand);

/// <summary>One entry in <see cref="PlaythroughDriver"/>'s ordered loss-predicate chain
/// (spec §1.6). Registration order is evaluation order is priority order (MECHANICS-BIBLE §7:
/// explicit priority, never code-order accident); the first non-null outcome wins. Evaluated
/// server-side at the verdict instant ONLY — there is no polling path and no mid-round loss
/// (spec §1.4). Exactly one predicate ships (QuotaMissedPredicate); the shape exists so a
/// future predicate registers without rework, and no second predicate is invented here.</summary>
public interface ILossPredicate
{
    /// <summary>Stable id, for logs and tests.</summary>
    string Id { get; }

    /// <summary>Null = this predicate does not end the run at this verdict.</summary>
    RunOutcome? Evaluate(int round);
}
