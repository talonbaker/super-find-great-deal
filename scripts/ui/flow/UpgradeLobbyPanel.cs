using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet 3e — Upgrade Lobby v0: the content-agnostic timed breather (spec §1.3; Talon's
/// Q3 ruling: "upgrades are deliberately undefined... first we need to test the game as it
/// is"). The machine guarantees time, safety and an extension point; this screen shows the
/// time, the framing, and LABELED EMPTY SLOTS where playtest-derived upgrade content will
/// land — nothing here assumes purchases exist. Players are not teleported (spec §1.3);
/// the copy tells the deep-woods player the walk home is theirs to make.
/// </summary>
public partial class UpgradeLobbyPanel : FlowScreenBase
{
    private Label _framing = null!;
    private Label _countdown = null!;
    private Button _ready = null!;
    private bool _readySent;

    protected override ScreenId Id => ScreenId.UpgradeLobby;

    public UpgradeLobbyPanel(IPlaythroughView view) : base(view) { }

    protected override void BuildContent()
    {
        Column.AddChild(UiKit.Heading(HeadingText));
        _framing = UiKit.Body("");
        Column.AddChild(_framing);

        var card = UiKit.Panel(out VBoxContainer slots);
        Column.AddChild(card);
        foreach (string slot in UpgradeSlotLabels)
            slots.AddChild(UiKit.Caption(slot));

        _countdown = UiKit.Caption("");
        Column.AddChild(_countdown);

        // The screen's ember instance: the primary ready-skip (spec §3.3 RequestReadyAdvance).
        _ready = UiKit.PrimaryButton("READY FOR THE DAY");
        _ready.Pressed += OnReadyPressed;
        Column.AddChild(_ready);
    }

    protected override void OnShow()
    {
        _readySent = false;
        _ready.Disabled = false;
        _ready.Text = "READY FOR THE DAY";
        _framing.Text = FramingLine(View.Round);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _ready.GrabFocus();
    }

    protected override void OnVisibleProcess(double delta) =>
        _countdown.Text = FlowFormat.Countdown(View.StateRemainingSec, "Next day");

    private void OnReadyPressed()
    {
        if (_readySent)
            return;
        _readySent = true;
        _ready.Disabled = true;
        _ready.Text = "WAITING FOR THE OTHERS...";
        View.RequestReadyAdvance();
    }

    // --- pure display decisions (FlowScreensTests) -----------------------------------------

    /// <summary>PLACEHOLDER copy — the "living menu" presentation and tone need /direct.</summary>
    public const string HeadingText = "MORNING BREAK";

    /// <summary>The lobby must not assume everyone is at camp (spec §6 case 13).</summary>
    public static string FramingLine(int roundJustSurvived) =>
        $"Stretch your legs. Wander back if you're deep. Night {(roundJustSurvived < 1 ? 1 : roundJustSurvived) + 1} is coming.";

    /// <summary>The declared extension point, visibly labeled as such — content is
    /// playtest-derived, later, by ruling.</summary>
    public static readonly string[] UpgradeSlotLabels =
    {
        "UPGRADE SLOT - to be earned in future playtests",
        "UPGRADE SLOT - to be earned in future playtests",
        "UPGRADE SLOT - to be earned in future playtests",
    };
}
