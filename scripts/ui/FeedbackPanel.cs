using Godot;

namespace MpFoundation.Ui;

/// <summary>On-quit "Feedback" panel (spec §7, decision 6; trimmed in the survey rework):
/// three confirmed questions — a one-tap 3–5 scale, one one-tap "did it run smoothly" row,
/// and one optional free-text box. Every question is independently skippable: Send works
/// with zero answers, Skip always works, nothing is required. Emits Submitted(answers, text).
/// <c>answers</c> carries a key only for questions the player actually tapped — an untapped
/// question is an absent key, never a sentinel value — and <c>text</c> is the trimmed box,
/// "" on Skip or an empty box. Always shown on a real-client quit, never blocking, anonymous.
/// Frame/scrim/motion are unchanged from the original panel (Talon likes the frame) — only
/// the content between the title and the Send/Skip row changed.
/// </summary>
public partial class FeedbackPanel : Control
{
    [Signal]
    public delegate void SubmittedEventHandler(Godot.Collections.Dictionary answers, string text);

    private TextEdit _edit = null!;
    private ButtonGroup _overall = null!;
    private ButtonGroup _performance = null!;

    /// <summary>Longest free-text comment transmitted. Godot's TextEdit has no max_length, so
    /// this is the only bound between a paste and the wire.</summary>
    public const int MaxFeedbackTextLength = 1000;

    public override void _Ready()
    {
        // The one scrim. Its colour lives nowhere else — see UiThemeService.BindScrim.
        Design.UiThemeService.BindScrim(this);
        _edit = GetNode<TextEdit>("Center/Panel/Q3/Edit");
        _overall = GetNode<Button>("Center/Panel/Q1/Options/Opt1").ButtonGroup;
        _performance = GetNode<Button>("Center/Panel/Q2/Options/Opt1").ButtonGroup;

        GetNode<Button>("Center/Panel/Buttons/SendButton").Pressed += () =>
        {
            // Cap the free text before it leaves the machine. TextEdit has no max_length, so
            // without this an accidental paste of a whole clipboard buffer ships wholesale —
            // and a free-text box is exactly where players type things they did not mean to
            // send. The cap bounds the blast radius of a slip; it cannot prevent one.
            string typed = _edit.Text.Trim();
            Submit(CollectAnswers(), typed.Length > MaxFeedbackTextLength
                ? typed[..MaxFeedbackTextLength]
                : typed);
        };
        var skip = GetNode<Button>("Center/Panel/Buttons/SkipButton");
        skip.Pressed += () => Submit(new Godot.Collections.Dictionary(), "");
        // Pad/keyboard reachability: land focus on the safe choice (same as the host-leave confirm).
        skip.CallDeferred(Control.MethodName.GrabFocus);
        UiMotion.StaggerIn(GetNode<Control>("Center/Panel"));
    }

    // Only the questions the player tapped appear. Absent key == unanswered; there is
    // deliberately no "unanswered" sentinel value (spec §7 back-compat requirement).
    private Godot.Collections.Dictionary CollectAnswers()
    {
        var answers = new Godot.Collections.Dictionary();
        AddIfAnswered(answers, "overall", _overall);
        AddIfAnswered(answers, "performance", _performance);
        return answers;
    }

    // Each option button carries its answer value as node metadata (int for the 1-5 scale,
    // string for the three-choice row) so this stays a plain lookup with no text parsing.
    private static void AddIfAnswered(Godot.Collections.Dictionary answers, string key, ButtonGroup group)
    {
        BaseButton? pressed = group.GetPressedButton();
        if (pressed != null)
            answers[key] = pressed.GetMeta("value");
    }

    private void Submit(Godot.Collections.Dictionary answers, string text) =>
        EmitSignal(SignalName.Submitted, answers, text);
}
