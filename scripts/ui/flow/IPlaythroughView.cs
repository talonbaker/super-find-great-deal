using System;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// The exact surface CORE-PROG-B1's screens read — spec §3.2's queryable state + typed
/// events, plus §3.3's two client→server requests — expressed as an interface so the
/// screens run identically against <c>FakePlaythroughDriver</c> (this packet's test double
/// and demo driver) and against CORE-INT-1's adapter over the real <c>PlaythroughDriver</c>.
///
/// <b>The never-strand rule is the caller's discipline, stated here as contract:</b> an
/// event fired before a subscriber existed never reaches it, so every surface initializes
/// by POLLING the queryable state after <see cref="Synced"/> and only uses events for
/// reaction/motion (spec §3, "subscribe-and-poll"; the SessionSummaryPanel idiom). No
/// screen in this package may depend on having witnessed a transition.
///
/// <b>Ordering contract (spec §3.2, binding on every implementation):</b> applying a
/// transition sets the queryable state FIRST, then invokes <see cref="StateChanged"/>, then
/// the typed event — so a handler for either always reads post-transition state.
/// </summary>
public interface IPlaythroughView
{
    /// <summary>Server: true from setup; client: true once the first flow sync lands. The
    /// established Synced-gating idiom — nothing renders flow UI before this.</summary>
    bool Synced { get; }

    PlaythroughState State { get; }

    /// <summary>1-based; valid from RoundIntro(1) onward.</summary>
    int Round { get; }

    /// <summary>Countdown for RoundIntro/RoundEnd/UpgradeLobby; -1 where no timer runs.
    /// Client-local extrapolation between syncs; cosmetic only.</summary>
    double StateRemainingSec { get; }

    /// <summary>Latched at each RoundEnded; null before the first. A late joiner into
    /// RoundEnd shows the tally from this poll, never from the missed event.</summary>
    RoundSummary? LastRoundSummary { get; }

    /// <summary>Latched at RunConcluded; null unless State == Loss.</summary>
    RunOutcome? LastOutcome { get; }

    event Action<PlaythroughState /*from*/, PlaythroughState /*to*/, int /*round*/>? StateChanged;
    event Action<int /*round*/, int /*demandAuthoritative*/>? RoundIntroStarted;
    event Action<int /*round*/>? RoundLive;
    event Action<RoundSummary>? RoundEnded;
    event Action<int /*roundJustSurvived*/, double /*durationSec*/>? UpgradeLobbyStarted;
    event Action<RunOutcome>? RunConcluded;

    /// <summary>Spec §3.3. Valid in RoundEnd/UpgradeLobby; stale sends are silently
    /// dropped server-side, so the UI may fire optimistically.</summary>
    void RequestReadyAdvance();

    /// <summary>Spec §3.3. Valid in Loss only; the driver, not the UI, sequences
    /// reset-then-intro.</summary>
    void RequestPlayAgain();
}

/// <summary>
/// The quota surface (spec §2.3's <c>QuotaLedger</c>, as consumed by UI — §3.5 row 5).
/// Integers only, by register law: the contract feeds numbers, the in-round display words
/// them (no meter, no bar — the ChillCueOverlay precedent).
/// </summary>
public interface IQuotaView
{
    bool Synced { get; }

    /// <summary>Only ever grows within a playthrough (spec §2.1).</summary>
    int CumulativeBanked { get; }

    /// <summary>Σ demand(1..currentRound) — what must be banked by this round's dawn.</summary>
    int CumulativeDemand { get; }

    event Action<int /*banked*/, int /*delta*/>? BankedChanged;
}
