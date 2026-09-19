using System.Collections.Generic;
using Godot;
using MpFoundation.Ui;

namespace MpFoundation.Game.World;

/// <summary>
/// Headless-observable mirror of L11's (Issue #114) UI decisions, present on EVERY peer
/// (server, bot, or real client) — unlike the actual CanvasLayer UI
/// (<see cref="Ui.LoadingHintOverlay"/>, <see cref="Ui.PhaseToastLayer"/>,
/// <see cref="Ui.SessionSummaryPanel"/>), which only ever exists on a non-headless peer (see
/// Gameplay's IsHeadless gate around those three). Exists purely so <c>BotHarness</c> can sample
/// these decisions and <c>Run-LoopUiTest.ps1</c> can prove the never-strand/toast/summary
/// contract without a windowed client — this cloud session has no way to run one at all (no
/// Godot binary; see that script's header comment).
///
/// Drives its own state from the SAME pure decision functions the real UI uses
/// (<see cref="LoadingOverlayGate"/>, <see cref="PhaseToastText"/>) and the same
/// <see cref="RunDriver"/>/<see cref="CycleDriver"/> singletons — never a second implementation
/// of the decision, just a second OBSERVER of it, so this can never diverge from what the real
/// UI would show.
/// </summary>
public partial class LoopUiTelemetry : Node
{
    public const string NodeName = "LoopUiTelemetry";

    /// <summary>Single instance per running game, same convention as
    /// <see cref="RunDriver.Instance"/>/<see cref="CycleDriver.Instance"/> — <c>BotHarness</c>
    /// reads this directly, unwired, the same way it already polls those two.</summary>
    public static LoopUiTelemetry? Instance { get; private set; }

    private bool _overlayDismissed;
    private ulong _overlayDismissedAtMsec;
    private readonly List<byte> _toastsFired = new(); // PhaseEventKind values that produced a toast line, in order.
    private bool _summaryShown;
    private ulong _summaryShownAtMsec;
    private int _resetCount;
    // CORE-PROG-A1 (core-spine spec §3.2): every PlaythroughDriver transition THIS peer
    // applied, in order, as "from>to@round" strings — the headless-observable mirror of the
    // state/verdict decisions the B1 screens will consume, exactly the role _toastsFired
    // plays for the phase toasts. A second OBSERVER of the driver's events, never a second
    // implementation of any decision.
    private readonly List<string> _flowTransitions = new();

    public bool OverlayDismissed => _overlayDismissed;
    public ulong OverlayDismissedAtMsec => _overlayDismissedAtMsec;
    public IReadOnlyList<byte> ToastsFired => _toastsFired;
    public bool SummaryShown => _summaryShown;
    public ulong SummaryShownAtMsec => _summaryShownAtMsec;
    public int ResetCount => _resetCount;
    public IReadOnlyList<string> FlowTransitions => _flowTransitions;

    public override void _Ready()
    {
        Instance = this;
        if (RunDriver.Instance is { } driver)
        {
            driver.PhaseCrossed += OnPhaseCrossed;
            driver.RunEndedSignal += OnRunEnded;
            driver.RunReset += OnRunReset;
        }
        // Constructed after PlaythroughDriver in Gameplay._Ready (same ordering note as the
        // RunDriver subscription above), so Instance is live here on every peer.
        if (Sail.Game.Run.PlaythroughDriver.Instance is { } flow)
            flow.StateChanged += OnFlowStateChanged;
        PollOverlay(); // see LoadingOverlayGate's doc — must check synchronously here too, not only in _Process.
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        if (RunDriver.Instance is { } driver)
        {
            driver.PhaseCrossed -= OnPhaseCrossed;
            driver.RunEndedSignal -= OnRunEnded;
            driver.RunReset -= OnRunReset;
        }
        if (Sail.Game.Run.PlaythroughDriver.Instance is { } flow)
            flow.StateChanged -= OnFlowStateChanged;
    }

    private void OnFlowStateChanged(Sail.Game.Run.PlaythroughState from,
        Sail.Game.Run.PlaythroughState to, int round) =>
        _flowTransitions.Add($"{(int)from}>{(int)to}@{round}");

    public override void _Process(double delta)
    {
        PollOverlay();
        // Same late-joiner-after-end poll SessionSummaryPanel uses — see that class's doc.
        if (!_summaryShown && RunDriver.Instance is { Synced: true, RunEnded: true })
            MarkSummaryShown();
    }

    private void PollOverlay()
    {
        if (_overlayDismissed)
            return;
        bool synced = CycleDriver.Instance?.Synced ?? false;
        if (LoadingOverlayGate.ShouldBeVisible(synced))
            return;
        _overlayDismissed = true;
        _overlayDismissedAtMsec = Time.GetTicksMsec();
    }

    private void OnPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        if (PhaseToastText.TextFor(kind) != null)
            _toastsFired.Add((byte)kind);
    }

    private void OnRunEnded() => MarkSummaryShown();

    private void MarkSummaryShown()
    {
        if (_summaryShown)
            return;
        _summaryShown = true;
        _summaryShownAtMsec = Time.GetTicksMsec();
    }

    private void OnRunReset()
    {
        _summaryShown = false;
        _resetCount++;
        // Mirrors RunDriver.History's own reset behavior (RunDriver.cs's BroadcastRunReset
        // clears its history before firing this same event) — a reset run's toast log should
        // read fresh, not carry the previous run's crossings forward.
        _toastsFired.Clear();
    }
}
