using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet 3f — Quota strip v0: the in-round demand display as WORDED STAGES, never a meter,
/// never a bar, never a number (the register ban — <c>ChillCueOverlay</c>'s "the register
/// does not carry meters"; <see cref="Sail.Game.Water.ChillReadout"/> is the worded-stages
/// precedent this copies, including the escalate-in-wording-only rule: banked 4-of-9 and
/// 4-of-8 render identically). This is the diegetic placeholder until the baby campers
/// exist (spec §3.5 row 5); the numbers live on the non-diegetic cards instead.
///
/// Fed by the quota events, initialized by poll (never-strand), visible only while
/// <see cref="ScreenRouter.QuotaStripVisible"/> says the round is live.
/// </summary>
public partial class QuotaStripWidget : CanvasLayer
{
    private readonly IPlaythroughView _view;
    private readonly IQuotaView _quota;
    private PanelContainer _panel = null!;
    private Label _label = null!;
    private int _lastStage = -1;
    private bool _showing;

    public QuotaStripWidget(IPlaythroughView view, IQuotaView quota)
    {
        _view = view;
        _quota = quota;
        _onBanked = (_, _) => Refresh(flash: true);
    }

    public override void _Ready()
    {
        Layer = ScreenRouter.QuotaStripLayer;
        Visible = false;

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", Hud.HudTheme.PanelStyle());
        _panel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.End;
        // UNDER the day/phase readout, not on top of it. Both are anchored CenterTop on separate
        // CanvasLayers, and at the bare screen margin they render exactly on each other — which is
        // the upper-centre collision Talon found on 2026-08-14. See Design.UiColumns.
        _panel.OffsetTop = Design.UiColumns.QuotaStripTop;
        _panel.SetMeta(UiKit.ComponentMeta, "Panel");
        AddChild(_panel);

        var column = new VBoxContainer();
        _panel.AddChild(column);
        var caption = UiKit.Caption(CaptionText);
        column.AddChild(caption);
        _label = UiKit.Body("");
        column.AddChild(_label);

        // React to banking for the flash; the TEXT is always re-derived from polled state.
        _quota.BankedChanged += _onBanked;
        Refresh(flash: false);
    }

    // Stored delegate so _ExitTree can unsubscribe — a stale handler on the shared view
    // must never fire against a freed node (the SessionSummaryPanel discipline; caught
    // live by the screenflow self-test's late-join teardown).
    private readonly System.Action<int, int> _onBanked;

    public override void _ExitTree()
    {
        _quota.BankedChanged -= _onBanked;
        Design.UiColumns.QuotaStripHeight = 0f; // the toast below closes up behind a strip that has gone.
    }

    public override void _Process(double delta)
    {
        bool shouldShow = ScreenRouter.QuotaStripVisible(_view.Synced, _view.State) && _quota.Synced;
        if (shouldShow != _showing)
        {
            _showing = shouldShow;
            Visible = shouldShow;
            if (shouldShow)
                Refresh(flash: false);
        }
        else if (shouldShow)
        {
            Refresh(flash: false); // demand can re-anchor at intro; polling is cheap.
        }

        // Re-read the column every frame rather than only at build: the readout above this one
        // changes height with its own string, and the strip's own height changes with the stage
        // wording. Two float writes and a compare — cheaper than the collision.
        _panel.OffsetTop = Design.UiColumns.QuotaStripTop;
        Design.UiColumns.QuotaStripHeight = shouldShow ? _panel.Size.Y : 0f;
    }

    private void Refresh(bool flash)
    {
        int stage = StageOf(_quota.CumulativeBanked, _quota.CumulativeDemand);
        if (stage == _lastStage && !flash)
            return;
        _label.Text = TextFor(stage);
        // Flash only on a banking event, in the kit accent — the change must be seen
        // (HudMotion's own reasoning), but the resting strip stays neutral.
        if (flash && _label.IsInsideTree())
            Hud.HudMotion.FlashText(_label, UiKit.Accent);
        _lastStage = stage;
    }

    // --- pure display decisions (FlowScreensTests) -----------------------------------------

    public const string CaptionText = "WINTER CACHE";

    /// <summary>Worded stage from the two ledger integers. Extremes defined: demand ≤ 0 is
    /// "met" (nothing owed), negative banked clamps to empty, and stage only ever derives
    /// from the ratio — no number leaks to the strip.</summary>
    public static int StageOf(int cumulativeBanked, int cumulativeDemand)
    {
        if (cumulativeDemand <= 0)
            return 3;
        int banked = cumulativeBanked < 0 ? 0 : cumulativeBanked;
        if (banked >= cumulativeDemand)
            return 3;
        if ((long)banked * 2 >= cumulativeDemand) // long: the open-ended tail can push demand near the 1e9 clamp.
            return 2;
        return banked > 0 ? 1 : 0;
    }

    /// <summary>PLACEHOLDER wording (the baby campers become this display later; /direct
    /// owns the eventual voice). Escalates in wording only.</summary>
    public static string TextFor(int stage) => stage switch
    {
        0 => "The cache is empty.",
        1 => "The cache is low.",
        2 => "Nearly enough for dawn.",
        _ => "The quota is met. Surplus carries.",
    };
}
