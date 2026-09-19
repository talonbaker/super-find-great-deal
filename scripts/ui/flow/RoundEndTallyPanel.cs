using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet 3c — the per-round tally (spec §3.5 row 2), the kit sibling of the end-of-run
/// <see cref="SessionSummaryPanel"/> (which stays on the legacy <c>RunEndedSignal</c> path
/// for the surviving <c>--run-cycles</c> test hook; this panel is the open-ended model's
/// per-round screen). Non-diegetic: honest numbers allowed. Banked vs demand, carryover
/// when nonzero, per-kid wallet lines through the existing
/// <see cref="ISessionSummarySource"/> seam + <see cref="SessionSummaryFormatter"/> —
/// composed, not duplicated (spec §3.2's note).
///
/// Ready is a SECONDARY action (the countdown advances everyone anyway — spec T8's timer
/// is the guarantee, the skip is the courtesy); the screen's ember instance is the
/// headline keyline.
/// </summary>
public partial class RoundEndTallyPanel : FlowScreenBase
{
    private Label _heading = null!;
    private Label _banked = null!;
    private Label _carryover = null!;
    private Label _nextDemand = null!;
    private VBoxContainer _kidRows = null!;
    private Label _countdown = null!;
    private Button _ready = null!;
    private bool _readySent;

    protected override ScreenId Id => ScreenId.RoundEnd;

    public RoundEndTallyPanel(IPlaythroughView view) : base(view) { }

    protected override void BuildContent()
    {
        _heading = UiKit.Heading(Heading(1));
        Column.AddChild(_heading);

        ColorRect keyline = UiKit.Keyline();
        keyline.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Column.AddChild(keyline);

        var card = UiKit.Panel(out VBoxContainer content);
        Column.AddChild(card);
        _banked = UiKit.Body("");
        content.AddChild(_banked);
        _carryover = UiKit.Caption("");
        content.AddChild(_carryover);
        _nextDemand = UiKit.Body("");
        content.AddChild(_nextDemand);
        _kidRows = new VBoxContainer();
        _kidRows.AddThemeConstantOverride("separation", UiKit.SpaceS / 2);
        content.AddChild(_kidRows);

        _countdown = UiKit.Caption("");
        Column.AddChild(_countdown);

        _ready = UiKit.SecondaryButton("READY");
        _ready.Pressed += OnReadyPressed;
        Column.AddChild(_ready);

        _onRoundEnded = _ => RefreshFromState();
        View.RoundEnded += _onRoundEnded;
    }

    // Stored so _ExitTree can unsubscribe — no stale handler on the shared view.
    private System.Action<RoundSummary>? _onRoundEnded;

    public override void _ExitTree()
    {
        if (_onRoundEnded != null)
            View.RoundEnded -= _onRoundEnded;
    }

    protected override void OnShow()
    {
        _readySent = false;
        _ready.Disabled = false;
        _ready.Text = "READY";
        RefreshFromState();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _ready.GrabFocus(); // controller focus visible from the first frame.
    }

    protected override void OnVisibleProcess(double delta) =>
        _countdown.Text = FlowFormat.Countdown(View.StateRemainingSec, "Morning break");

    private void RefreshFromState()
    {
        // Poll the LATCH, not the event payload — a late joiner into RoundEnd has the
        // summary only via the sync (spec §3.4).
        RoundSummary summary = View.LastRoundSummary ?? new RoundSummary(View.Round, 0, 0, 0);
        _heading.Text = Heading(summary.Round);
        _banked.Text = BankedLine(summary);
        string? carry = CarryoverLine(summary);
        _carryover.Visible = carry != null;
        _carryover.Text = carry ?? "";
        _nextDemand.Text = NextDemandLine(summary);

        foreach (Node child in _kidRows.GetChildren())
            child.QueueFree();
        ISessionSummarySource data = SessionSummarySource.Current ?? SessionSummarySource.Null;
        foreach (KidQuarters kid in data.PerKidQuarters)
            _kidRows.AddChild(UiKit.Caption(SessionSummaryFormatter.KidLine(kid)));
    }

    private void OnReadyPressed()
    {
        if (_readySent)
            return; // the SessionSummaryPanel double-press latch; the server drops dupes anyway.
        _readySent = true;
        _ready.Disabled = true;
        _ready.Text = "WAITING FOR THE OTHERS...";
        View.RequestReadyAdvance();
    }

    // --- pure display decisions (FlowScreensTests) -----------------------------------------

    public static string Heading(int round) => $"NIGHT {(round < 1 ? 1 : round)} - SURVIVED";

    /// <summary>Cumulative banked vs cumulative demand — the verdict's own numbers, honest.</summary>
    public static string BankedLine(RoundSummary s) => $"Cache banked: {s.Banked} of {s.Demand} needed";

    /// <summary>Shown only when nonzero (packet 3c). Null means "hide the row".</summary>
    public static string? CarryoverLine(RoundSummary s)
    {
        int carry = s.Banked - s.Demand;
        return carry > 0 ? $"Surplus carries forward: +{carry}" : null;
    }

    public static string NextDemandLine(RoundSummary s) => $"By tomorrow's dawn the cache must hold {s.NextDemand}.";
}
