namespace MpFoundation.Ui;

/// <summary>
/// <b>The prose half of the How To Play screen — the things a control table cannot say.</b>
///
/// <para><b>These three lines are Talon's own words, dictated 2026-09-04 for the live playtest,
/// and they are verbatim.</b> His phrasing, his punctuation and his spelling ("breath" for
/// "breathe") ship exactly as given. Anyone reworking this list rewords his copy only on his say-so
/// — a tidy-up here is a regression, not an improvement.</para>
///
/// <para><b>History, because it explains the shape.</b> Talon's 2026-08-29 note 3 read
/// <i>"Please include the following in the 'HOW TO PLAY' section:"</i> and the list after the
/// colon did not come through. It was left deliberately empty rather than guessed at, then on
/// 2026-08-30 he answered <i>"I'm not sure. Do what you think is best"</i>, so five lines were
/// written from what the build actually does. <b>His own list surfaced on 2026-09-04, and per that
/// same standing instruction it has replaced them.</b></para>
///
/// <para><b>TWO OF TALON'S THREE LINES WERE REMOVED AT THE FORK, AND NOT REWORDED (BASE-1,
/// 2026-09-19).</b> They named water that would kill you and bubbles that would also kill you,
/// and this game has neither — the water service, the drowning clock and the bubbles were all
/// pruned. The "no mechanic the world lacks" rule (note 4) is the whole reason: copy that
/// promises a system the build does not have is the defect, and a screen shown to a playtester
/// is exactly where it costs the most. The one surviving line is about double-tap-to-run, which
/// is still true.</para>
///
/// <para><b>Nobody may write replacement lines here on an agent's own initiative.</b> The
/// removed lines were Talon's own words in his own comic register; what replaces them is his to
/// dictate, the way these were (2026-09-04). Until he does, this screen says one true thing
/// rather than three charming untrue ones.</para>
///
/// <para><b>What still must NOT go here.</b> A new <i>control</i> ("press X to do Y") belongs in
/// <see cref="ControlGlyphs.Sections"/>, where it gets a real icon resolved from the real
/// <c>InputMap</c> and can never disagree with the binding. This list is for rules, consequences
/// and warnings — what no glyph can carry. Line 1 is the exception that proves it: double-tap-to-run
/// is a *timing*, not a binding, and no glyph can draw it.</para>
///
/// <para><b>Verified against shipped code.</b> Double-tap-to-run is a real timing in
/// <c>AvatarMotor</c>. That is the standard every line here is held to.</para>
///
/// <para><b>What left with the rewrite, deliberately.</b> The five previous lines carried the
/// objective ("collect all the bubbles", pinned to <see cref="PhaseToastText.BubbleGoalToast"/>),
/// the reset lever's shared scope, the televisions and the dark. The objective is still announced
/// on entry by the toast, which is unchanged; the other three are no longer stated on this screen.
/// Talon's list replaced the screen, not just its wording. <c>GoalCopyTests</c> records which of
/// its assertions that retired and why.</para>
/// </summary>
public static class HowToPlayContent
{
    /// <summary>The heading drawn above <see cref="Lines"/>. Only rendered when there are lines;
    /// change it with the content if the content wants a different word.</summary>
    public const string NotesTitle = "GOOD TO KNOW";

    /// <summary>
    /// One paragraph per entry, in reading order. Rendered as wrapped body text.
    ///
    /// <para><b>Talon's words, 2026-09-04, verbatim — do not edit or re-spell.</b>
    /// <c>ForewordCopyTests</c> pins what is left character-for-character and fails on any drift.
    /// Two lines were REMOVED at the fork because their subjects no longer exist; see the class
    /// doc. Removing untrue copy is not the same as rewording his.</para>
    /// </summary>
    public static readonly string[] Lines =
    {
        "Double tapping a directional key will let you run in that direction.",
    };
}
