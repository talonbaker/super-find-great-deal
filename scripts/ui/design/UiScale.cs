namespace MpFoundation.Ui.Design;

/// <summary>A step on the one spacing scale. Gaps are chosen by <i>role</i>, not by pixel:
/// asking for <see cref="Tight"/> is a claim about how related two things are, and the pixel
/// value that claim resolves to is <see cref="UiScale"/>'s business.
///
/// <para>Naming the steps rather than passing integers is what makes the scale enforceable —
/// the audit counted ~40 distinct spacing values against a scale of five, and every one of the
/// strays was a literal someone typed at a call site.</para></summary>
public enum Space : byte
{
    /// <summary>Nothing. An explicit choice, so a zero gap is distinguishable from a forgotten one.</summary>
    None = 0,

    /// <summary>Inside a component — a glyph and its label, an icon and its number.</summary>
    Tight = 1,

    /// <summary>Between related rows in a group.</summary>
    Snug = 2,

    /// <summary>The default gap. Between controls in a column.</summary>
    Normal = 3,

    /// <summary>Around a group — the padding inside a card.</summary>
    Loose = 4,

    /// <summary>Between regions of a screen.</summary>
    Wide = 5,
}

/// <summary>
/// <b>Structure: the scale, the ranks, the timings.</b> Everything dimensional the interface
/// is allowed to use, and — as with <see cref="UiTokens"/> — there is no second place to put
/// one.
///
/// <para>The <c>ui-design</c> skill's diagnosis, which this file exists to satisfy: <i>"when
/// something looks flat or feels cheap, the cause is nearly always hierarchy, spacing or
/// states — not colour."</i> Spacing sprawl is named there as "the single biggest 'looks
/// amateur' tell". So the scale is closed the same way the palette is.</para>
/// </summary>
public static class UiScale
{
    // --- spacing: five steps on a 4px grid ---------------------------------------------------
    // Kept identical to the shipped HudTheme scale (4/8/12/16/24) that seven components already
    // obey, so consolidating onto it moves the strays to the scale rather than moving the scale.

    public const int SpaceTight = 4;
    public const int SpaceSnug = 8;
    public const int SpaceNormal = 12;
    public const int SpaceLoose = 16;
    public const int SpaceWide = 24;

    /// <summary>Pixels for a step. The only legal way to get a gap.</summary>
    public static int Px(Space step) => step switch
    {
        Space.Tight => SpaceTight,
        Space.Snug => SpaceSnug,
        Space.Normal => SpaceNormal,
        Space.Loose => SpaceLoose,
        Space.Wide => SpaceWide,
        _ => 0,
    };

    /// <summary>Every legal gap, for the test that proves nothing off-scale ships.</summary>
    public static readonly int[] SpaceSteps = { SpaceTight, SpaceSnug, SpaceNormal, SpaceLoose, SpaceWide };

    /// <summary>Distance from any panel to the screen edge. One value, all four sides, every
    /// screen — the thing that makes a set of screens read as one product.</summary>
    public const int ScreenMargin = SpaceLoose;

    // --- corner radius: three, and a dial ----------------------------------------------------
    // Kept as three named values because the audit counted twelve. WHICH of the three a given
    // component takes is a recipe decision (UiRecipes), not a constant baked into the component
    // — that indirection is what lets a square button become a round one in one edit.

    /// <summary>Barely rounded — the scissor-cut corner of a chip.</summary>
    public const int RadiusTight = 4;

    /// <summary>A cut-paper card corner.</summary>
    public const int RadiusCard = 12;

    /// <summary>Fully round. Half the control's height, clamped by Godot.</summary>
    public const int RadiusPill = 999;

    // --- type: six sizes, and rank carried by weight and colour first ------------------------
    // The direction brief: "Hierarchy by weight and colour before size; identical metrics in
    // both temperatures so nothing reflows at dusk." Sizes are the LAST channel, which is why
    // six is enough where the audit found thirteen.
    //
    // UI-3, 2026-08-29 — THE WHOLE SCALE WENT UP 15%, AS A SET. Talon: "please make the UI text
    // overall slightly bigger, too." The lift is uniform (x1.15, rounded to the nearest whole
    // pixel) rather than hand-tuned per label, because the closed scale is what makes a set of
    // screens read as one product — raising one label is how a scale of six becomes a scale of
    // thirteen again. Was 40 / 28 / 20 / 15 / 12 / 10.
    //
    // Why 15% and not 20%: "slightly" is the operative word in the note, and the smallest window
    // the project supports is 1280x720 (project.godot, display/window/size). Body 15 -> 17 is a
    // clearly perceptible step — about one notch on any type ramp — while Hero 40 -> 46 leaves
    // the modal columns (CardMinWidth 420 inside a 1280-wide frame at ScreenMargin 16) with room
    // to spare. It is also why nothing structural moved with it: TargetHeight stays 44, which a
    // 17 px label at PadY = Normal (12) still fits inside with 5 px of slack.
    //
    // The ratios are preserved: 46/32 = 1.44 (was 1.43), 32/23 = 1.39 (was 1.40),
    // 23/17 = 1.35 (was 1.33), 17/14 = 1.21 (was 1.25), 14/12 = 1.17 (was 1.20).

    public const int SizeHero = 46;
    public const int SizeDisplay = 32;
    public const int SizeTitle = 23;
    public const int SizeBody = 17;
    public const int SizeCaption = 14;
    public const int SizeMicro = 12;

    public static readonly int[] TypeSizes = { SizeMicro, SizeCaption, SizeBody, SizeTitle, SizeDisplay, SizeHero };

    // Weights on Sora's variable axis. Three ranks, exactly as the brief specifies.
    public const int WeightBody = 500;
    public const int WeightMedium = 600;
    public const int WeightStrong = 700;
    public const int WeightDisplay = 800;

    // --- borders ------------------------------------------------------------------------------

    public const int BorderHair = 1;
    public const int BorderWeight = 2;

    /// <summary>Focus borders are deliberately thicker than any other state: focus must survive
    /// squint distance on a television, because this game is controller-first.</summary>
    public const int BorderFocus = 3;

    // --- motion --------------------------------------------------------------------------------
    // "Motion 120–300ms eased; the only long motion in the system is the dusk/dawn temperature
    // crossfade (~2s), which is choreography, not decoration."

    /// <summary>A state answering the pointer. Fast enough to feel like cause and effect.</summary>
    public const double MotionInstant = 0.12;

    /// <summary>A thing arriving or leaving.</summary>
    public const double MotionQuick = 0.20;

    /// <summary>A panel settling.</summary>
    public const double MotionSettle = 0.30;

    /// <summary>The dusk/dawn temperature crossfade — the page visibly losing its light. The one
    /// long motion in the system, and it is choreography: it fires off the same exactly-once
    /// crossing event every other day/night consumer uses, never its own clock.</summary>
    public const double MotionTemperature = 2.0;

    /// <summary>Stagger between rows in an entrance.</summary>
    public const double MotionStagger = 0.045;

    // --- component sizing ---------------------------------------------------------------------

    /// <summary>Minimum height of anything a controller can land on. A focus target smaller than
    /// this cannot be hit reliably with a stick.</summary>
    public const int TargetHeight = 44;

    /// <summary>The width a content card wants before it starts wrapping awkwardly.</summary>
    public const int CardMinWidth = 420;
}
