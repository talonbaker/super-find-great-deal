using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet 3a — the Round Intro card: "NIGHT N" framing plus the round's demand, from the
/// round-start event/state. This is the non-diegetic "a state transition happened" moment
/// Talon asked for by name; play stays live behind it (spec §1.3 — control is not frozen).
///
/// The demand figure prefers the quota view's live remaining-need (carryover model:
/// surplus already banked counts, spec §2.1) and falls back to the intro event's
/// authoritative demand when no quota view is attached. Numbers are allowed here — this is
/// a non-diegetic card, the in-round strip is where the register bans them.
/// </summary>
public partial class RoundIntroCard : FlowScreenBase
{
    private Label _title = null!;
    private Label _demand = null!;
    private int _lastAnnouncedDemand;

    protected override ScreenId Id => ScreenId.RoundIntro;

    public RoundIntroCard(IPlaythroughView view, IQuotaView? quota) : base(view)
    {
        _quota = quota;
    }

    private readonly IQuotaView? _quota;

    protected override void BuildContent()
    {
        _title = UiKit.Heading(Title(1));
        Column.AddChild(_title);

        // The screen's single ember-accent instance: the keyline under the night number.
        ColorRect keyline = UiKit.Keyline();
        keyline.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Column.AddChild(keyline);

        _demand = UiKit.Body("");
        Column.AddChild(_demand);

        _onIntroStarted = (_, demand) => _lastAnnouncedDemand = demand;
        View.RoundIntroStarted += _onIntroStarted;
    }

    // Stored so _ExitTree can unsubscribe — no stale handler on the shared view.
    private System.Action<int, int>? _onIntroStarted;

    public override void _ExitTree()
    {
        if (_onIntroStarted != null)
            View.RoundIntroStarted -= _onIntroStarted;
    }

    protected override void OnShow()
    {
        _title.Text = Title(View.Round);
        RefreshDemand();
    }

    // Banking is accepted during RoundIntro (spec §1.3), so the need can shrink while the
    // card is up — re-derive it from the polled ledger each frame it shows.
    protected override void OnVisibleProcess(double delta) => RefreshDemand();

    private void RefreshDemand()
    {
        int demand = _quota is { Synced: true } q ? q.CumulativeDemand : _lastAnnouncedDemand;
        int banked = _quota is { Synced: true } q2 ? q2.CumulativeBanked : 0;
        string line = DemandLine(demand, banked);
        if (_demand.Text != line)
            _demand.Text = line;
    }

    // --- pure display decisions (FlowScreensTests) -----------------------------------------

    /// <summary>"NIGHT N". Round clamps up to 1 — a joiner whose sync has not landed a round
    /// yet must never see "NIGHT 0".</summary>
    public static string Title(int round) => $"NIGHT {(round < 1 ? 1 : round)}";

    /// <summary>PLACEHOLDER copy (tone goes through /direct before polish). Extremes
    /// defined: need already met reads as such rather than "0 more".</summary>
    public static string DemandLine(int cumulativeDemand, int cumulativeBanked)
    {
        int need = cumulativeDemand - cumulativeBanked;
        return need > 0
            ? $"The winter cache needs {need} more by dawn."
            : "The winter cache is already full for tonight. Get ahead.";
    }
}
