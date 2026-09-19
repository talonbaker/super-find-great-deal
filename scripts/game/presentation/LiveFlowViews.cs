using System;
using UiFlow = MpFoundation.Ui.Flow;
using Run = Sail.Game.Run;

namespace MpFoundation.Game.Presentation;

/// <summary>
/// CORE-INT-1's translation layer between the real spine types (<c>Sail.Game.Run</c>) and
/// B1's UI-side mirror types (<c>MpFoundation.Ui.Flow</c>) — pure statics, so the mapping
/// is walkable by <c>dotnet test</c> (FlowAdapterTests pins byte-for-byte enum agreement
/// and the demand arithmetic below).
///
/// <b>The demand-integer ruling (B1's parked question 1), settled by measurement:</b> the
/// REAL driver's <c>RoundIntroStarted</c> event carries the PER-ROUND increment
/// (<c>QuotaLedger.DemandFor(round)</c> — read from <c>PlaythroughDriver.ExecuteCommit</c>),
/// and the real <c>RoundSummary.NextDemand</c> is likewise <c>DemandFor(round + 1)</c>.
/// B1's screens and fake driver assume CUMULATIVE figures everywhere (the honest number
/// under the shipped carry-surplus model — "the cache must hold X"). Rather than edit five
/// screens and the scripted demo, the VIEW contract stays cumulative and this adapter
/// translates at the seam:
/// <list type="bullet">
/// <item>Intro event: re-fired with the ledger's <c>CumulativeDemand</c>, which
/// <c>ApplyRoundBegin</c> has already set before any driver event fires (the §3.2
/// state-first ordering, documented at <c>BroadcastRoundIntro</c>).</item>
/// <item><c>RoundSummary.NextDemand</c>: cumulative-next = <c>Demand + NextDemand</c> —
/// both numbers are the server's own wire values, so no client ever derives a demand from
/// its local schedule copy (spec §2.2's version-skew rule holds).</item>
/// </list>
/// </summary>
public static class FlowContract
{
    public static UiFlow.PlaythroughState ToView(Run.PlaythroughState s) => (UiFlow.PlaythroughState)(byte)s;

    public static UiFlow.RunOutcomeKind ToView(Run.RunOutcomeKind k) => (UiFlow.RunOutcomeKind)(byte)k;

    /// <summary>Outcome Demand/Banked are already cumulative on the wire (the verdict's own
    /// numbers) — field-for-field.</summary>
    public static UiFlow.RunOutcome ToView(Run.RunOutcome o) =>
        new(ToView(o.Kind), o.Round, o.Demand, o.Banked);

    /// <summary>Summary Demand/Banked are cumulative on the wire; NextDemand is the per-round
    /// increment and is translated (class doc).</summary>
    public static UiFlow.RoundSummary ToView(Run.RoundSummary s) =>
        new(s.Round, s.Demand, s.Banked, CumulativeNextDemand(s.Demand, s.NextDemand));

    /// <summary>Σ demand(1..N+1) from the two wire integers: cumulative-through-N plus round
    /// N+1's increment. Long arithmetic + the ledger's own clamp, so the 1e9 tail can never
    /// wrap (QuotaMathTests' overflow posture, kept at this seam too).</summary>
    public static int CumulativeNextDemand(int cumulativeDemandThroughRound, int nextRoundIncrement) =>
        (int)Math.Min((long)cumulativeDemandThroughRound + Math.Max(0, nextRoundIncrement),
            Run.QuotaMath.DemandClamp);
}

/// <summary>
/// The thin adapter (B1's Decision D1, CORE-INT-1 scope 2): the real
/// <see cref="Run.PlaythroughDriver"/> exposed as <see cref="UiFlow.IPlaythroughView"/> so
/// every B1 screen runs against the live machine without a single screen edit. Plain class,
/// not a Node — it owns no lifecycle. Subscriptions are NOT unsubscribed by design: the
/// driver, the ledger, this adapter and the FlowScreens that hold it are all children (or
/// fields) of the same Gameplay scene and are freed together — there is no lifetime skew
/// for a stale handler to exploit (unlike B1's D7 case, where short-lived screens subscribe
/// to a long-lived shared view).
/// </summary>
public sealed class LivePlaythroughView : UiFlow.IPlaythroughView
{
    private readonly Run.PlaythroughDriver _driver;
    private readonly Run.QuotaLedger _ledger;

    public LivePlaythroughView(Run.PlaythroughDriver driver, Run.QuotaLedger ledger)
    {
        _driver = driver;
        _ledger = ledger;
        driver.StateChanged += (from, to, round) =>
            StateChanged?.Invoke(FlowContract.ToView(from), FlowContract.ToView(to), round);
        // The cumulative translation (FlowContract doc): ApplyRoundBegin ran inside the same
        // broadcast handler, before this event — the ledger's CumulativeDemand IS this
        // round's cumulative-due on every peer, synced flag or not.
        driver.RoundIntroStarted += (round, _) =>
            RoundIntroStarted?.Invoke(round, _ledger.CumulativeDemand);
        driver.RoundLive += round => RoundLive?.Invoke(round);
        driver.RoundEnded += s => RoundEnded?.Invoke(FlowContract.ToView(s));
        driver.UpgradeLobbyStarted += (round, duration) => UpgradeLobbyStarted?.Invoke(round, duration);
        driver.RunConcluded += o => RunConcluded?.Invoke(FlowContract.ToView(o));
    }

    public bool Synced => _driver.Synced;
    public UiFlow.PlaythroughState State => FlowContract.ToView(_driver.State);
    public int Round => _driver.Round;
    public double StateRemainingSec => _driver.StateRemainingSec;

    public UiFlow.RoundSummary? LastRoundSummary =>
        _driver.LastRoundSummary is { } s ? FlowContract.ToView(s) : null;

    public UiFlow.RunOutcome? LastOutcome =>
        _driver.LastOutcome is { } o ? FlowContract.ToView(o) : null;

    public event Action<UiFlow.PlaythroughState, UiFlow.PlaythroughState, int>? StateChanged;
    public event Action<int, int>? RoundIntroStarted;
    public event Action<int>? RoundLive;
    public event Action<UiFlow.RoundSummary>? RoundEnded;
    public event Action<int, double>? UpgradeLobbyStarted;
    public event Action<UiFlow.RunOutcome>? RunConcluded;

    public void RequestReadyAdvance() => _driver.SendReadyAdvance();
    public void RequestPlayAgain() => _driver.SendPlayAgain();
}

/// <summary>The quota half of the seam: <see cref="Run.QuotaLedger"/> as
/// <see cref="UiFlow.IQuotaView"/>. Direct passthrough — the ledger's queryable surface is
/// already cumulative (spec §2.1), which is the view contract's semantics.</summary>
public sealed class LiveQuotaView : UiFlow.IQuotaView
{
    private readonly Run.QuotaLedger _ledger;

    public LiveQuotaView(Run.QuotaLedger ledger)
    {
        _ledger = ledger;
        ledger.BankedChanged += (banked, delta) => BankedChanged?.Invoke(banked, delta);
    }

    public bool Synced => _ledger.Synced;
    public int CumulativeBanked => _ledger.CumulativeBanked;
    public int CumulativeDemand => _ledger.CumulativeDemand;

    public event Action<int, int>? BankedChanged;
}
