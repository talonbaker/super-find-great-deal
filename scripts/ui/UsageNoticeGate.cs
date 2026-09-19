namespace MpFoundation.Ui;

/// <summary>
/// <b>Whether the first-run anonymous-usage notice goes up on this press of Start.</b> Pulled out
/// of <see cref="Menu.MainMenu"/> for the same reason <see cref="LoadingOverlayGate"/> and
/// <see cref="PhaseToastText"/> were pulled out of theirs: the decision is the part that can be
/// wrong, and inside a <c>Node3D</c>'s tier transition it can only be checked by launching a
/// window and looking.
///
/// <para>The acceptance criterion behind this is "appears exactly once on a fresh profile, after
/// the first Start — not on the second launch, and not again after 'don't show this message
/// again'". Every one of those is a different value of one of the four inputs below, so with the
/// decision in one pure function all three launches are provable without a display.</para>
/// </summary>
public static class UsageNoticeGate
{
    /// <param name="enteringOptionsTier">True only on the transition INTO the options tier — the
    /// actual press of Start. Returning from Host / Join / Settings re-enters the menu already
    /// past the title, and a notice that reappeared there would be shown several times per
    /// session rather than once per install.</param>
    /// <param name="suppressPanels">The capture-run flag. A rig photographing the title screen
    /// must never get a modal in the frame — the same reason the playtest foreword honours it.</param>
    /// <param name="alreadySeen">Persisted: the player has closed the notice once, by the button,
    /// the X or ESC. <b>This is the "exactly once" half, and it is not consent</b> — see
    /// <see cref="OnboardingSettings.UsageNoticeSeen"/>.</param>
    /// <param name="telemetryConfigured">Whether a report could actually be sent. Announcing
    /// reporting that is inert would be its own false statement to the player, and this is the
    /// state every dev build had before the Firebase credentials landed.</param>
    public static bool ShouldShow(
        bool enteringOptionsTier, bool suppressPanels, bool alreadySeen, bool telemetryConfigured) =>
        enteringOptionsTier && !suppressPanels && !alreadySeen && telemetryConfigured;
}
