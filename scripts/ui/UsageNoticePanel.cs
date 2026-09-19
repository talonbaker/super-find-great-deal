using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// <b>The first-run anonymous-usage notice.</b> Shown once, on the title screen, the first time a
/// player presses Start — Talon, 2026-08-30 note 15: <i>"a notice in the very beginning after the
/// user starts the game for the first time and presses the start button… that anonymous usage is
/// enabled by default to help the development process… an explanation that explains anonymous
/// usage can be disabled in the settings… a button to say don't show this message again with an X
/// to click out."</i>
///
/// <para><b>It is a notice, not a question, and that distinction is the whole design.</b> The
/// panel it replaced (<c>UsageConsentDialog</c>, Allow / No thanks) was an opt-IN gate at boot.
/// This one tells the player a thing that is already true and points at where to change it.
/// Nothing on it writes consent: <b>neither the X nor the button turns reporting off, and neither
/// turns it on.</b> Dismissing a notice is not a decision in either direction, and the only writer
/// of the consent state anywhere in shipping code is the settings toggle
/// (<see cref="SettingsPanel"/>) via <see cref="Telemetry.TelemetryStore.SetUsageConsent"/>.</para>
///
/// <para><b>Why both closers do the same thing.</b> The requirement is that the notice appears
/// exactly once, so an X-close cannot leave it queued to nag again — which makes the button and
/// the X behaviourally identical by construction. The button is still here because Talon named
/// it, and because "Got it — don't show this again" is the affordance a player looks for when
/// they want to acknowledge rather than dismiss. Both funnel through <see cref="Close"/>, which
/// is also where ESC lands, so there is one path and one write.</para>
///
/// <para><b>The copy is checked against the code, not against the brief.</b> Every claim in
/// <c>UsageNoticePanel.tscn</c> is drawn from <see cref="Telemetry.TelemetryPayload"/>'s
/// <c>BuildUsage</c> and <c>Envelope</c> — the two methods that build the only report this
/// setting governs. A privacy notice that overstates or understates what is sent is a false
/// statement to a player about their own data; if those methods change, this text is part of the
/// change. <c>UsageNoticeCopyTests</c> holds that seam shut from the test side.</para>
///
/// <para>Same CanvasLayer modal pattern as <see cref="PlaytestForewordPanel"/> and
/// <see cref="HowToPlayPanel"/> — one scrim, a centred panel, an X in the header, ESC to close —
/// so the three doors read as one visual language.</para>
/// </summary>
public partial class UsageNoticePanel : CanvasLayer
{
    [Signal]
    public delegate void ClosedEventHandler();

    public override void _Ready()
    {
        // The one scrim. Its colour lives nowhere else — see UiThemeService.BindScrim.
        Design.UiThemeService.BindScrim(this);
        Layer = Design.UiLayers.PauseChildPanel;
        GetNode<Button>("Center/Panel/Header/CloseButton").Pressed += Close;
        var ack = GetNode<Button>("Center/Panel/AckRow/AckButton");
        ack.Pressed += Close;
        // Pad/keyboard reachability: focus starts on the acknowledge button, which is the one
        // action on the panel that reads as "I have read this".
        ack.CallDeferred(Control.MethodName.GrabFocus);
        UiMotion.StaggerIn(GetNode<Control>("Center/Panel"));
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("pause") || @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Records that the player has been told, and closes. <b>Consent is deliberately not
    /// touched here</b> — see the class doc. The write happens on close rather than on show so a
    /// crash while the notice is up leaves the player still owed the notice, which errs toward
    /// telling them.</summary>
    private void Close()
    {
        OnboardingSettings.SetUsageNoticeSeen(true);
        EmitSignal(SignalName.Closed);
    }
}
