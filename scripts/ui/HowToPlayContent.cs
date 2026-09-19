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
/// <para><b>The "no mechanic the world lacks" rule is OVERRIDDEN for these lines (Talon,
/// 2026-09-04).</b> This file used to forbid copy naming a mechanic the current world does not
/// have — the lesson of note 4, and it still governs anything written here on an agent's own
/// initiative. Talon's copy is deliberately comic: line 3 promises "a bubble death counter for
/// your satisfaction", which reads at first like copy inventing a system. <b>It is not.</b> Talon,
/// 2026-09-04: <i>"About the 'death counter' I'm making a joke about the HUD bubble counter. Just
/// a joke you know."</i> The counter is <c>Hud.HudBubbleCount</c>, already on screen at the head of
/// the top-centre column (<c>GameHud</c>, behind the profile's <c>BubbleCount</c> flag) — the gag
/// is that the visible bubble tally is secretly a body count. <b>Nothing here needs building, and
/// the rule is not repealed</b> — it is suspended for copy Talon authored himself, dated so the
/// next reader can tell an override from an accident.</para>
///
/// <para><b>What still must NOT go here.</b> A new <i>control</i> ("press X to do Y") belongs in
/// <see cref="ControlGlyphs.Sections"/>, where it gets a real icon resolved from the real
/// <c>InputMap</c> and can never disagree with the binding. This list is for rules, consequences
/// and warnings — what no glyph can carry. Line 1 is the exception that proves it: double-tap-to-run
/// is a *timing*, not a binding, and no glyph can draw it.</para>
///
/// <para><b>Verified against shipped code where it can be.</b> Drowning is real:
/// <c>RespawnCause.Drowned</c> at <c>Water.WaterGeometry.DrownAfterSec</c> = 3.0 s submerged,
/// server-adjudicated — line 2 is comic but true. The bubbles killing the player (line 3) and the
/// salt are flourish, not systems.</para>
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
    /// <para><b>Talon's words, 2026-09-04, verbatim — do not edit, reorder or re-spell.</b>
    /// <c>ForewordCopyTests</c> pins each one character-for-character and fails on any drift.</para>
    /// </summary>
    public static readonly string[] Lines =
    {
        "Double tapping a directional key will let you run in that direction.",

        "Water will kill you if you breath it in. The game is realistic like that.",

        "The bubbles will also kill you, but I coated the player in a fine dusting of salt which "
            + "kills the bubbles first. I've included a bubble death counter for your satisfaction.",
    };
}
