namespace MpFoundation.Ui.Flow;

/// <summary>
/// UI-side mirror of the CORE spine contract (docs/design/2026-08-13-core-spine-spec.md
/// §1.2, §1.6, §3.2), for Workstream B (CORE-PROG-B1).
///
/// <b>Why a mirror and not the real types:</b> Workstream A1 builds the real
/// <c>PlaythroughDriver</c> and its types on a DIFFERENT branch, concurrently, and the two
/// branches never see each other before CORE-INT-1 integrates (spec §4). If both workstreams
/// defined the same type names in the same namespace, the integration merge would be a
/// duplicate-definition compile error. So every screen in this package depends only on
/// <see cref="IPlaythroughView"/> over these UI-namespace mirrors; INT-1 writes one thin
/// adapter from the real driver to that interface (byte-for-byte enum mapping — the ordinals
/// below are the spec's, verbatim) and never edits a screen.
///
/// Ordinals are wire values in the real contract ("append-only; ordinals cross the wire",
/// spec §1.6) — they must match the spec exactly and never be reordered here.
/// </summary>
public enum PlaythroughState : byte
{
    Boot = 0,         // server constructing / client connecting; pre-round-1
    RoundIntro = 1,   // "Round N" card, quota announced
    InRound = 2,      // Day/Dusk/Night — fine structure is the band, not a state
    RoundEnd = 3,     // tally screen
    UpgradeLobby = 4, // ~30 s social interlude
    Loss = 5,         // run over
}

/// <summary>Mirror of spec §1.6's <c>RunOutcomeKind</c>. Append-only.</summary>
public enum RunOutcomeKind : byte
{
    QuotaMissed = 0,
}

/// <summary>Mirror of spec §1.6's <c>RunOutcome</c> — one per playthrough, loss-only under
/// the open-ended model. Demand/Banked are cumulative (spec §2.1), for the loss screen's
/// honesty: the numbers shown are the numbers the verdict used.</summary>
public readonly record struct RunOutcome(RunOutcomeKind Kind, int Round, int Demand, int Banked);

/// <summary>Mirror of spec §3.2's <c>RoundSummary</c>. Demand/Banked are cumulative
/// (spec §2.1); carryover = Banked - Demand when positive (the winter cache is a stockpile,
/// SD-1 decision D3).</summary>
public readonly record struct RoundSummary(int Round, int Demand, int Banked, int NextDemand);
