using MpFoundation.Game.Sandbox;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>"What if sprint were the default and SHIFT did nothing?" as a live toggle</b> (2026-08-28,
/// Talon's session).
///
/// <para><b>The question this answers.</b> Plenty of games hold the player at a jog and put top
/// speed behind a button; plenty of others simply run at top speed always, and spend the button on
/// nothing. The second is one fewer thing to hold and one fewer thing to forget. Talon's report is
/// that his pinky lives on SHIFT anyway, which is the exact symptom that says the button is not
/// buying anything — a modifier that is never released is a constant wearing a costume.</para>
///
/// <para><b>Why a decorator and not a knob.</b> This is an INPUT question, not a tuning question.
/// The tempting cheap version — raise <c>MoveSpeed</c> to the sprint speed and stop pressing SHIFT
/// — is not the same experiment and would quietly answer a different one: it moves the walk gear,
/// the skid entry speed, the slide entry speed and the duck-walk speed with it, because all three
/// of those are declared as FRACTIONS of <c>MoveSpeed</c>. What you would feel is a whole new
/// tuning, not the same tuning without a button. Flipping the bit at the intent boundary changes
/// exactly one thing and leaves all 58 knobs alone, which is the only version of this test whose
/// result means anything.</para>
///
/// <para><b>What it does.</b> Wraps any <see cref="IIntentSource"/> and forces <c>Sprint</c> true,
/// except while the walk modifier is held — so SHIFT becomes inert and LEFT CTRL becomes the brake.
/// The walk gear is deliberately kept: "always sprinting" should not mean "cannot ever go slowly",
/// and a body with exactly one speed is a different and much worse proposal than the one being
/// tested.</para>
///
/// <para><b>It reads the walk action itself rather than inferring it</b> from the intent's speed.
/// <see cref="LocalInputIntentSource"/> applies the walk ceiling to <c>MoveDir</c>'s MAGNITUDE, and
/// a stick pushed gently is indistinguishable from a walk modifier by magnitude alone — so
/// inferring it would turn a light touch on the stick into a lost sprint. The action is the
/// truth.</para>
///
/// <para><b>A zero intent stays zero.</b> When the mouse is free the wrapped source returns
/// <see cref="MoveIntent.None"/>, and forcing <c>Sprint</c> on that would have the body sprint on
/// the first frame the mouse is recaptured, before any key is pressed. The guard is the same
/// no-input convention <c>AvatarMotor</c> already applies to a control lock.</para>
/// </summary>
public sealed class SprintDefaultIntentSource : IIntentSource
{
    private readonly IIntentSource _inner;

    /// <summary>Live, so the toggle is an A/B rather than a relaunch. The whole value of this
    /// experiment is switching it mid-run on the same piece of ground.</summary>
    public bool Enabled { get; set; }

    /// <summary>The action the walk gear is bound to (LEFT CTRL — see <c>project.godot</c>). Named
    /// once here rather than repeated, and matching <see cref="LocalInputIntentSource"/>'s own
    /// read of it, because two spellings of a gear are how two gears happen.</summary>
    public const string WalkAction = "walk";

    public SprintDefaultIntentSource(IIntentSource inner) => _inner = inner;

    public MoveIntent NextIntent(double delta)
    {
        MoveIntent intent = _inner.NextIntent(delta);
        if (!Enabled)
            return intent;

        // No input at all (mouse free, control lock) stays no input. See the class doc.
        if (intent.MoveDir.LengthSquared() <= 1e-6f && !intent.Jump && !intent.JumpHeld)
            return intent;

        // The walk modifier still beats sprint, exactly as it does on the shipped path — this
        // inverts the DEFAULT, it does not delete the brake.
        bool walking = Godot.Input.IsActionPressed(WalkAction);
        return intent with { Sprint = !walking };
    }
}
