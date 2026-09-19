using System.Threading.Tasks;
using Godot;
using MpFoundation.Ui;

namespace MpFoundation.Telemetry;

/// <summary>
/// The telemetry autoload (registered in project.godot [autoload], same pattern as
/// NetworkManager/VoiceManager). It is inert for every launch mode except the real windowed
/// client: Boot calls BeginClientSession() only on the Splash path and only when not headless
/// and no server/bot/practice/self-test flag is set (spec decision 9). It also stays fully
/// dormant until TelemetryConfig.IsConfigured (blocked on Talon's one-time Firebase setup — see
/// docs/telemetry-setup.md), so nothing is ever POSTed to a bogus URL.
///
/// On a real client session it: installs crash hooks + writes the session-active marker,
/// flushes any queued reports from prior sessions, and shows the crash-report prompt if the
/// previous session crashed. It no longer ASKS for usage consent — since 2026-08-30 (Talon's
/// note 15) reporting is on by default and the player is told so by the first-run notice the
/// title screen raises after the first press of Start; the only writer of that decision is the
/// settings toggle.
/// At quit (RequestQuit, also routed from the OS window-close) it shows the feedback panel,
/// enqueues the usage report (if consented) and feedback (if anything was answered or typed),
/// best-effort flushes, and performs the real Quit().
/// </summary>
public partial class Telemetry : Node
{
    public static Telemetry Instance { get; private set; } = null!;

    private TelemetryClient _client = null!;
    private CanvasLayer _layer = null!;
    private bool _active;         // a real, configured client session is being tracked
    private bool _quitting;       // reentrancy guard for the quit sequence
    private bool _flushInFlight;  // reentrancy guard so overlapping flushes can't double-send
    private bool _voiceUsed;
    private int _peakPlayers;
    private ulong _sessionStartMsec;

    // Balance/design-tuning counters — same lifecycle as _voiceUsed/_peakPlayers: accumulate in
    // memory during play, read once at quit in BuildUsageReport, reset per session (a new process
    // starts them at 0). Inert when _active is false, exactly like NoteVoiceUsed below.
    private int _propsGrabbed;
    private int _propsThrown;
    private int _propsDropped;

    public override void _EnterTree() => Instance = this;

    /// <summary>Called by Boot on the real windowed-client path only (decision 9). Dormant when
    /// the backend is unconfigured — in that case nothing player-facing or network-facing runs,
    /// and quit behavior is left completely untouched (auto-accept-quit stays default).</summary>
    public void BeginClientSession()
    {
        if (_active)
            return;
        if (!TelemetryConfig.IsConfigured)
        {
            GD.Print("[telemetry] not configured (see docs/telemetry-setup.md) — telemetry disabled.");
            return;
        }
        _active = true;
        _sessionStartMsec = Time.GetTicksMsec();

        // LD-2: the cadence clock (stop-seconds, research §A2-R3) is fed from the local body by a
        // small node created on demand; a real client session is one of its two customers (the
        // other is bubbletest's --bt-perf-readout), so it is created here and read once at quit.
        Sail.Game.World.CadenceTracker.Ensure();

        // Own an HTTPRequest for sends and a top CanvasLayer for the prompts.
        var request = new HttpRequest { UseThreads = false };
        AddChild(request);
        _client = new TelemetryClient(request);
        _layer = new CanvasLayer { Layer = Ui.Design.UiLayers.Telemetry }; // above all gameplay/menu UI
        AddChild(_layer);

        // Route the OS window-close (X / Alt+F4) through our quit path. Godot notifies every
        // autoload independently, so this is self-contained — it does not touch NetworkManager's
        // own NotificationWMCloseRequest handler.
        GetTree().SetAutoAcceptQuit(false);

        TelemetryStore.Load();
        SessionMonitor.InstallCrashHooks();

        // Resolve the previous session's crash BEFORE writing this session's marker.
        SessionMonitor.CrashSignal signal = SessionMonitor.BootCheck();
        _ = FlushQueueAsync(); // send anything left over from prior sessions

        SessionMonitor.WriteSessionMarker();

        if (signal != SessionMonitor.CrashSignal.None)
            ShowCrashPrompt(signal);
        // No consent PROMPT at boot any more (Talon, 2026-08-30 note 15): reporting is on by
        // default and the player is TOLD rather than asked, by the first-run notice the title
        // screen raises after the first press of Start (MainMenu -> UsageNoticePanel). Boot is
        // the wrong moment for it anyway — his words are "after the user starts the game for the
        // first time and presses the start button", and this runs before the splash.
    }

    /// <summary>Best-effort marker that voice was used this session (feeds the usage report).
    /// A future opt-in hook — left unwired so no other existing file changes (spec's four
    /// touches); voice_used defaults false until something calls this.</summary>
    public void NoteVoiceUsed() => _voiceUsed = true;

    // The three prop notes also reset LD-2's since-act clock: PropManager already routes exactly
    // the LOCAL player's own grab/throw/drop through here and nowhere else, so this is the one
    // seam that knows a prop act happened without a second hook in PropManager.

    /// <summary>The local player grabbed a prop (feeds props_grabbed — local actions only).</summary>
    public void NotePropGrabbed()
    {
        _propsGrabbed++;
        Sail.Game.World.CadenceTracker.Instance?.Clock.NoteAct("grab");
    }

    /// <summary>The local player threw a prop (feeds props_thrown — local actions only).</summary>
    public void NotePropThrown()
    {
        _propsThrown++;
        Sail.Game.World.CadenceTracker.Instance?.Clock.NoteAct("throw");
    }

    /// <summary>The local player dropped a prop (feeds props_dropped — local actions only).</summary>
    public void NotePropDropped()
    {
        _propsDropped++;
        Sail.Game.World.CadenceTracker.Instance?.Clock.NoteAct("drop");
    }

    // --- Boot-time prompts -------------------------------------------------------
    private void ShowCrashPrompt(SessionMonitor.CrashSignal signal)
    {
        var dialog = GD.Load<PackedScene>("res://scenes/ui/CrashReportDialog.tscn").Instantiate<CrashReportDialog>();
        dialog.Decided += accepted =>
        {
            if (accepted)
                EnqueueCrashReport(signal);
            // Either way, the crash is never asked about twice.
            SessionMonitor.ClearPendingCrash();
            dialog.QueueFree();
            _ = FlushQueueAsync();
        };
        _layer.AddChild(dialog);
    }

    private void EnqueueCrashReport(SessionMonitor.CrashSignal signal)
    {
        string? logTail = SessionMonitor.ReadLogTail(60);
        if (signal == SessionMonitor.CrashSignal.Detail
            && SessionMonitor.ReadPendingCrash(out string? message, out string? stack))
        {
            // Redact the exception text too, not just the tail. A stack trace or an IO
            // exception message embeds the absolute path it failed on, and every path this
            // game builds globalizes to C:\Users\<account>\… — the player's real name in the
            // common case. The crash dialog promises "identifiers removed"; before this, that
            // promise only covered the log excerpt.
            PendingQueue.Enqueue(TelemetryPayload.BuildCrash(
                SessionMonitor.RedactIdentifiers(stack ?? ""),
                SessionMonitor.RedactIdentifiers(message ?? ""),
                logTail, "managed_exception"));
        }
        else
        {
            PendingQueue.Enqueue(TelemetryPayload.BuildCrash(null, null, logTail, "unclean_shutdown"));
        }
    }

    // --- Quit flow ---------------------------------------------------------------
    /// <summary>The single quit entry point (MainMenu's Quit button + the OS window-close).
    /// Shows the feedback panel, then finalizes and quits. When telemetry is inactive/unconfigured
    /// it quits immediately.</summary>
    public void RequestQuit()
    {
        if (_quitting)
        {
            // Second quit request while the feedback panel (or final flush) is up — the player
            // wants out NOW. Clear the marker so this clean exit isn't misread as a crash, and go.
            SessionMonitor.ClearSessionMarker();
            GetTree().Quit();
            return;
        }
        _quitting = true;

        if (!_active)
        {
            GetTree().Quit();
            return;
        }

        // The OS close can arrive mid-match with the mouse captured — a click-only modal under
        // an invisible cursor reads as "the game refuses to close". Free the cursor first.
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // If the panel can't load for any reason, quitting must still work — fall through to the
        // finalize path with an empty submission rather than wedging the Quit button and the OS X.
        try
        {
            var panel = GD.Load<PackedScene>("res://scenes/ui/FeedbackPanel.tscn").Instantiate<FeedbackPanel>();
            panel.Submitted += (answers, text) => _ = FinalizeAndQuit(answers, text);
            _layer.AddChild(panel);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[telemetry] feedback panel failed to load ({e.Message}); quitting directly.");
            _ = FinalizeAndQuit(new Godot.Collections.Dictionary(), "");
        }
    }

    private async Task FinalizeAndQuit(Godot.Collections.Dictionary feedbackAnswers, string feedbackText)
    {
        // Compile + enqueue the usage report (only with consent) and feedback (only if at least
        // one question was answered or the free-text box was filled — a pure Skip, or Send with
        // nothing tapped and nothing typed, enqueues nothing, same as the original text-only gate).
        if (TelemetryStore.UsageReportingEnabled)
            PendingQueue.Enqueue(BuildUsageReport());
        if (feedbackAnswers.Count > 0 || feedbackText.Length > 0)
            PendingQueue.Enqueue(TelemetryPayload.BuildFeedback(feedbackAnswers, feedbackText));

        // Clean shutdown: clear the marker so the next boot doesn't misread this as a crash.
        SessionMonitor.ClearSessionMarker();

        // Best-effort flush, bounded by the client's 5s per-send timeout. The queue is the
        // durable safety net — anything not sent now flushes on the next boot (decision 11).
        await FlushQueueAsync();

        GetTree().Quit();
    }

    // Snapshot NetworkManager's public state read-only (no changes to NetworkManager.cs).
    private Godot.Collections.Dictionary BuildUsageReport()
    {
        var net = NetworkManager.Instance;
        int elapsedSec = (int)((Time.GetTicksMsec() - _sessionStartMsec) / 1000);
        _peakPlayers = Mathf.Max(_peakPlayers, net.AcceptedCount);

        string role = net.Role switch
        {
            NetworkManager.SessionRole.Server => "host",
            NetworkManager.SessionRole.Client => "join",
            _ => "practice",
        };
        // LD-2: session totals off the cadence clock, rounded to a tenth — the readout's
        // resolution, and nothing finer is a measurement. Zeros and an empty split when the
        // tracker never existed (it always does on this path; the null-guard is for shape).
        var clock = Sail.Game.World.CadenceTracker.Instance?.Clock;
        var bySection = new Godot.Collections.Dictionary();
        if (clock != null)
        {
            foreach (var pair in clock.StopSecondsBySection)
                bySection[pair.Key] = Tenths(pair.Value);
        }
        return TelemetryPayload.BuildUsage(elapsedSec, role, net.Options.Transport, _peakPlayers, _voiceUsed,
            _propsGrabbed, _propsThrown, _propsDropped,
            Tenths(clock?.StopSecondsTotal ?? 0), Tenths(clock?.StopSecondsPerMinuteSession ?? 0), bySection);

        static float Tenths(double v) => (float)(System.Math.Round(v * 10.0) / 10.0);
    }

    private async Task FlushQueueAsync()
    {
        if (!TelemetryConfig.IsConfigured || _flushInFlight)
            return;
        _flushInFlight = true;
        try
        {
            // Consent backstop: usage reports queued before the player opted out must not upload
            // afterwards (SetUsageConsent purges eagerly; this catches stragglers).
            //
            // This test READ "!= Granted" until 2026-08-30, and the reasoning behind that has now
            // inverted with the default. It was strict because a lost or corrupted settings.cfg
            // landed on Unset rather than Denied, and treating Unset as permissive would have let
            // a report queued under an old consent upload after a state loss. Under default-on
            // that stricter reading would instead delete the queue of every player who never
            // touched the setting — the overwhelming majority — so the guard now asks the same
            // question the enqueue side asks. The state-loss hole it was covering is closed at the
            // source instead, and closed better: TelemetryStore.OptOutMarkerFile means losing
            // settings.cfg no longer loses the opt-out at all.
            if (!TelemetryStore.UsageReportingEnabled)
                PendingQueue.ApplyRevokedConsent();
            await PendingQueue.FlushAsync(_client.Send);
        }
        finally { _flushInFlight = false; }
    }

    // Track peak player count across the session for the usage report.
    public override void _Process(double delta)
    {
        if (!_active)
            return;
        int seen = NetworkManager.Instance.AcceptedCount;
        if (seen > _peakPlayers)
            _peakPlayers = seen;
    }

    public override void _Notification(int what)
    {
        // Only intercept the OS close when we're actively tracking a real client session;
        // otherwise auto-accept-quit is still default and this is a no-op (decision 9).
        if (what == NotificationWMCloseRequest && _active)
            RequestQuit();
    }
}
