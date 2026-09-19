using System;
using MpFoundation.Ui.Flow;

namespace MpFoundation.Game.Presentation;

/// <summary>
/// CORE-PROG-B1's test double for the spec §3 contract — a plain C# class (deliberately not
/// a Node, so the Godot-free xUnit suite can drive it) implementing
/// <see cref="IPlaythroughView"/> + <see cref="IQuotaView"/> with the same semantics the
/// real <c>PlaythroughDriver</c>/<c>QuotaLedger</c> will have:
///
/// <list type="bullet">
/// <item>State is set BEFORE events fire, <c>StateChanged</c> before the typed event
/// (spec §3.2's ordering contract — asserted by <c>FlowScreensTests</c>).</item>
/// <item><c>LastRoundSummary</c>/<c>LastOutcome</c> latch so a subscriber that never saw
/// the event recovers by polling (the never-strand contract).</item>
/// <item>The §3.3 requests are honoured with the server's T8/T9/T10 semantics
/// (ready-advance out of RoundEnd/UpgradeLobby, play-again out of Loss with a full
/// boundary reset) so the demo's buttons genuinely work pre-integration; out-of-state
/// requests are silently dropped, exactly as the spec says the server drops stale echoes.</item>
/// </list>
///
/// The LoopUiTelemetry pattern, one layer up: never a second implementation of a UI
/// decision — just a drivable stand-in for the machine the decisions read.
/// </summary>
public sealed class FakePlaythroughDriver : IPlaythroughView, IQuotaView
{
    public bool Synced { get; private set; }
    public PlaythroughState State { get; private set; } = PlaythroughState.Boot;
    public int Round { get; private set; }
    public double StateRemainingSec { get; private set; } = -1;
    public RoundSummary? LastRoundSummary { get; private set; }
    public RunOutcome? LastOutcome { get; private set; }

    public int CumulativeBanked { get; private set; }
    public int CumulativeDemand { get; private set; }

    public event Action<PlaythroughState, PlaythroughState, int>? StateChanged;
    public event Action<int, int>? RoundIntroStarted;
    public event Action<int>? RoundLive;
    public event Action<RoundSummary>? RoundEnded;
    public event Action<int, double>? UpgradeLobbyStarted;
    public event Action<RunOutcome>? RunConcluded;
    public event Action<int, int>? BankedChanged;

    /// <summary>How many ready-advance requests the "server" has received — the demo and
    /// tests read this to show/assert that the wire verb actually fired.</summary>
    public int ReadyAdvanceRequests { get; private set; }

    public int PlayAgainRequests { get; private set; }

    // Placeholder timer values, from the spec's value calls (T_intro 4 s, T_tally 15 s,
    // T_lobby 30 s — SD-1 decision D7). Cosmetic here: the fake never auto-advances, the
    // countdown is display fodder for the screens.
    public const double IntroSec = 4;
    public const double TallySec = 15;
    public const double LobbySec = 30;

    /// <summary>Marks the client synced (the SyncFlowStateTo landing). Idempotent.</summary>
    public void CommitSynced() => Synced = true;

    public void CommitRoundIntro(int round, int cumulativeDemand)
    {
        Round = round;
        StateRemainingSec = IntroSec;
        CumulativeDemand = cumulativeDemand;
        Commit(PlaythroughState.RoundIntro);
        RoundIntroStarted?.Invoke(round, cumulativeDemand);
    }

    public void CommitRoundLive()
    {
        StateRemainingSec = -1;
        Commit(PlaythroughState.InRound);
        RoundLive?.Invoke(Round);
    }

    public void CommitRoundEnd(RoundSummary summary)
    {
        LastRoundSummary = summary;
        StateRemainingSec = TallySec;
        Commit(PlaythroughState.RoundEnd);
        RoundEnded?.Invoke(summary);
    }

    public void CommitUpgradeLobby()
    {
        StateRemainingSec = LobbySec;
        int survived = Round;
        Commit(PlaythroughState.UpgradeLobby);
        UpgradeLobbyStarted?.Invoke(survived, LobbySec);
    }

    public void CommitLoss(RunOutcome outcome)
    {
        LastOutcome = outcome;
        StateRemainingSec = -1; // no auto-timeout on Loss (spec §1.3) — players sit with it.
        Commit(PlaythroughState.Loss);
        RunConcluded?.Invoke(outcome);
    }

    /// <summary>Server-side bank landing (spec §2.3's BankedChanged).</summary>
    public void CommitBank(int cacheUnits)
    {
        if (cacheUnits <= 0)
            return;
        CumulativeBanked += cacheUnits;
        BankedChanged?.Invoke(CumulativeBanked, cacheUnits);
    }

    /// <summary>Cosmetic countdown decay, mirroring the client-local extrapolation the real
    /// view does between syncs. Clamps at 0 and never auto-advances — advancing is always
    /// an explicit commit (in the demo, a keypress; on the server, its own timer).</summary>
    public void Tick(double delta)
    {
        if (StateRemainingSec > 0)
            StateRemainingSec = Math.Max(0, StateRemainingSec - delta);
    }

    public void RequestReadyAdvance()
    {
        // T8/T9: valid in RoundEnd and UpgradeLobby; anything else is a stale echo, dropped.
        if (State is not (PlaythroughState.RoundEnd or PlaythroughState.UpgradeLobby))
            return;
        ReadyAdvanceRequests++;
        // Single-peer fake: one ready IS all-ready. RoundEnd -> lobby; lobby -> next intro.
        if (State == PlaythroughState.RoundEnd)
            CommitUpgradeLobby();
        else
            CommitRoundIntro(Round + 1, NextDemandGuess());
    }

    public void RequestPlayAgain()
    {
        if (State != PlaythroughState.Loss) // T10 guard; a second request finds RoundIntro and drops.
            return;
        PlayAgainRequests++;
        ResetForNewPlaythrough();
        CommitRoundIntro(1, ScriptedPlaythrough.DemandRound1);
    }

    /// <summary>The playthrough-boundary reset (spec §5.4) as it bears on this view:
    /// ledger zeroed, latches cleared. Idempotent, like every real slice must be.</summary>
    public void ResetForNewPlaythrough()
    {
        CumulativeBanked = 0;
        CumulativeDemand = 0;
        LastRoundSummary = null;
        LastOutcome = null;
    }

    private int NextDemandGuess() =>
        LastRoundSummary is { } s ? s.NextDemand : CumulativeDemand + 1;

    private void Commit(PlaythroughState to)
    {
        PlaythroughState from = State;
        State = to; // state first, then StateChanged, then the typed event — spec §3.2.
        StateChanged?.Invoke(from, to, Round);
    }
}
