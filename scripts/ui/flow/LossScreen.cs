using System;
using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet 3d — the Loss screen: the hard cut. Near-black, unmistakable, run-over verdict
/// with the honest final numbers (the same cumulative pair the verdict used — spec §1.6),
/// no auto-timeout (spec §1.3: players sit with it as long as they like). Two verbs:
/// Play Again (spec §3.3, the driver sequences reset-then-intro) and Leave to Menu (the
/// existing <c>Gameplay.LeaveToMenu</c> seam, injected as a callback so this screen has no
/// Gameplay dependency — CORE-INT-1 wires the real one).
///
/// ALL COPY IS PLACEHOLDER: tone goes through /direct before polish (THRILL §9 is a live
/// blank — deliberately not resolved here). Register law holds even in placeholder: the
/// loss is the season's, never a taken or hurt child.
/// </summary>
public partial class LossScreen : FlowScreenBase
{
    private Label _heading = null!;
    private Label _numbers = null!;
    private Label _body = null!;
    private Button _playAgain = null!;
    private Button _leave = null!;
    private bool _playAgainSent;
    private readonly Action? _leaveToMenu;

    protected override ScreenId Id => ScreenId.Loss;

    protected override Color Scrim => UiKit.LossScrim;

    public LossScreen(IPlaythroughView view, Action? leaveToMenu) : base(view)
    {
        _leaveToMenu = leaveToMenu;
    }

    protected override void BuildContent()
    {
        _heading = UiKit.Heading("");
        Column.AddChild(_heading);
        _numbers = UiKit.Body("");
        Column.AddChild(_numbers);
        _body = UiKit.Caption("");
        Column.AddChild(_body);

        // The screen's ember instance: Play Again is the primary verb of a loss that
        // invites one more run.
        _playAgain = UiKit.PrimaryButton("PLAY AGAIN");
        _playAgain.Pressed += OnPlayAgainPressed;
        Column.AddChild(_playAgain);

        _leave = UiKit.SecondaryButton("LEAVE TO MENU");
        _leave.Pressed += () => _leaveToMenu?.Invoke();
        _leave.Disabled = _leaveToMenu == null; // demo path: visible, labeled, inert.
        Column.AddChild(_leave);
    }

    protected override void OnShow()
    {
        _playAgainSent = false;
        _playAgain.Disabled = false;
        _playAgain.Text = "PLAY AGAIN";
        // Poll the latch, never the event — the late joiner into Loss (spec §6 case 1).
        RunOutcome outcome = View.LastOutcome ?? new RunOutcome(RunOutcomeKind.QuotaMissed, View.Round, 0, 0);
        _heading.Text = VerdictHeading(outcome);
        _numbers.Text = NumbersLine(outcome);
        _body.Text = BodyLine;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _playAgain.GrabFocus();
    }

    private void OnPlayAgainPressed()
    {
        if (_playAgainSent)
            return; // double-press latch; the T10 state guard drops dupes server-side too.
        _playAgainSent = true;
        _playAgain.Disabled = true;
        _playAgain.Text = "WAITING...";
        View.RequestPlayAgain();
    }

    // --- pure display decisions (FlowScreensTests) -----------------------------------------

    /// <summary>PLACEHOLDER — /direct owns the tone.</summary>
    public static string VerdictHeading(RunOutcome outcome) => outcome.Kind switch
    {
        RunOutcomeKind.QuotaMissed => "THE CACHE RAN DRY",
        _ => "THE SEASON IS OVER",
    };

    /// <summary>The verdict's own cumulative numbers, honest — including the 0-banked
    /// extreme, which must read as a statement, not an error.</summary>
    public static string NumbersLine(RunOutcome outcome) =>
        $"Night {(outcome.Round < 1 ? 1 : outcome.Round)}: the cache held {outcome.Banked} of the {outcome.Demand} winter needed.";

    /// <summary>PLACEHOLDER — register-safe by law: the loss is the larder's, nobody is
    /// taken, nobody is hurt.</summary>
    public const string BodyLine = "Winter wins this one. Everyone walks away - the cache doesn't.";
}
