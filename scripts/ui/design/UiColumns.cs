using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>
/// <b>The regions of the frame that more than one CanvasLayer writes to.</b>
///
/// <para><see cref="UiLayers"/> settled which surface draws <i>over</i> which. It could not settle
/// WHERE they draw, and that is the other half of the same defect: two independent layers, each
/// anchored to <c>CenterTop</c> at the shared screen margin, sit exactly on top of each other and
/// neither one can see the collision. Talon's 2026-08-14 playtest found two of them — the
/// winter-cache strip on the day/phase readout at the top centre, the room code under the carry
/// chips at the bottom left — and in both cases every individual widget was placed correctly
/// against the only neighbour its author knew about.</para>
///
/// <para><b>How to add an occupant:</b> pick the region it belongs to, take the next rung down, and
/// derive its offset from here. Never anchor a second CanvasLayer to a frame edge with a bare
/// margin — a widget that measures only against the frame cannot know what else is already there.</para>
///
/// <para><b>Top-centre is measured, bottom-left is constant, and the asymmetry is deliberate.</b>
/// The top-centre occupants' heights depend on their own content (the day/phase string grows from
/// "DAY 1 · DAY" to "DAY 12 · NIGHT"; the cache strip's wording changes with the stage), so each
/// reports the height it is actually occupying and the ones below it stack off that. The
/// bottom-left clearance is a constant, so where the room code goes is knowable at compile time.
/// The in-engine self-test (<c>--hudlayout-selftest</c>) measures the REAL rects, so a constant
/// that drifts away from what is drawn fails a suite rather than shipping.</para>
///
/// <para>The bottom-right column, added with the how-to-play hint on 2026-09-04, is measured for
/// the same reason the top-centre one is — its occupant is a line of type whose size the design
/// system owns and may move.</para>
/// </summary>
public static class UiColumns
{
    /// <summary>Distance from the frame to the first thing in any column. The shared margin.</summary>
    public const float Edge = UiScale.ScreenMargin;

    /// <summary>Between two occupants of the same column. Related rows in a group.</summary>
    public const float Gap = UiScale.SpaceSnug;

    // --- the top-centre column ------------------------------------------------------------
    // Three independent CanvasLayers, top to bottom: the day/phase readout (GameHud, layer 80),
    // the winter-cache strip (QuotaStripWidget, layer 82), and the transient phase toast
    // (PhaseToastLayer, layer 60). Each reports its own occupied height; each reads the rung
    // above it. A rung that is absent or hidden reports 0 and the column simply closes up.

    /// <summary>Height the day/phase readout is currently occupying; 0 when no HUD renders.
    /// Written by <c>GameHud</c>, read by everything below it.</summary>
    public static float DayPhaseHeight { get; set; }

    /// <summary>Height the winter-cache strip is currently occupying; 0 while it is hidden
    /// (outside RoundIntro/InRound). Written by <c>QuotaStripWidget</c>.</summary>
    public static float QuotaStripHeight { get; set; }

    /// <summary>Height the shared bubble tally is currently occupying; 0 in every world without
    /// bubbles in it, which is every world but the Bubble Test (BT-8). Written by
    /// <c>GameHud</c>.</summary>
    public static float BubbleCountHeight { get; set; }

    /// <summary>Top offset for the bubble tally — the head of the column when it is present.
    ///
    /// <para>It takes the head rather than a rung under the day/phase readout because in the one
    /// world that has it, <c>HudProfile</c> suppresses the day/phase readout, so a rung below
    /// would leave the tally floating under an empty gap. The two are mutually exclusive BY
    /// PROFILE, not by luck — but the column still derives properly below, so a future world that
    /// wants both gets a stacked pair instead of the collision this class exists to
    /// prevent.</para></summary>
    public static float BubbleCountTop => Edge;

    /// <summary>Top offset for the day/phase readout — under the tally, or at the head of the
    /// column when (as in every shipped world) there is no tally.</summary>
    public static float DayPhaseTop => Below(BubbleCountTop, BubbleCountHeight);

    /// <summary>Top offset for the winter-cache strip: under the day/phase readout, or at the
    /// frame margin when there is no HUD (the flow-screen demo, the self-test).</summary>
    public static float QuotaStripTop => Below(DayPhaseTop, DayPhaseHeight);

    /// <summary>Top offset for the transient phase toast — under both persistent readouts, so a
    /// sunset line never lands across the strip it is telling the player to go and fill.</summary>
    public static float PhaseToastTop => Below(QuotaStripTop, QuotaStripHeight);

    // --- the bottom-left column ------------------------------------------------------------
    // The room code (Gameplay's own Hud layer) sits a fixed clearance above the bottom edge. It
    // used to be placed 14 px off the edge, inside a bottom-left widget that has since been cut
    // from this build, which is exactly where Talon found it; the clearance that fix reserved
    // (a hint line, a gap and a 52 px block above the edge margin) is kept as the room code's
    // resting place so the label does not move again.

    /// <summary>The clearance reserved above the bottom edge, including the edge margin itself.
    /// Historically the height of the carry block that lived there; retained as the room code's
    /// offset so the placement Talon signed off on does not shift.</summary>
    public const float BottomLeftClearance = Edge + UiScale.SpaceWide + Gap + 52f;

    /// <summary>The room code's height. It is one line of body text on the Gameplay HUD layer.</summary>
    public const float RoomCodeHeight = 28f;

    /// <summary>Bottom offset (negative — Godot's bottom-anchored convention) for the room code:
    /// clear of the whole reserved block.</summary>
    public const float RoomCodeBottom = -(BottomLeftClearance + Gap);

    /// <summary>Top offset for the room code.</summary>
    public const float RoomCodeTop = RoomCodeBottom - RoomCodeHeight;

    /// <summary>How wide the room-code line is allowed to run. "Room: ABCDE" at body size needs a
    /// fraction of this; the width is the authored one, kept so the label's box is unchanged.</summary>
    public const float RoomCodeWidth = 284f;

    /// <summary>Anchors the room code above the bottom-left clearance. <b>The one place that
    /// decides where it goes</b> — Gameplay calls this on the label it pulls out of its scene, and
    /// the in-engine self-test calls it on an identical label and then measures the result. The
    /// offsets authored in <c>Gameplay.tscn</c> are editor preview only; this overwrites them, so
    /// there is no second copy to go stale.</summary>
    public static void PlaceRoomCode(Control label)
    {
        label.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        label.OffsetLeft = Edge;
        label.OffsetRight = Edge + RoomCodeWidth;
        label.OffsetTop = RoomCodeTop;
        label.OffsetBottom = RoomCodeBottom;
    }

    // --- the bottom-right column ------------------------------------------------------------
    // One occupant today: the permanent "ESC · HOW TO PLAY" hint (GameHud, layer 80). It is here
    // rather than in the bottom-left because that corner is the room code's, and here rather than
    // in either top column because both of those carry readouts the player reads DURING play; a
    // hint they read once belongs where the eye goes least.

    /// <summary>Height the how-to-play hint is currently occupying; 0 when the HUD is hidden or
    /// this world's <c>HudProfile</c> never built it. Written by <c>GameHud</c>.
    ///
    /// <para>Measured rather than constant for the same reason the top-centre column is: the line
    /// is one row of type at a size the design system owns, and a second occupant of this corner
    /// must stack off what is actually drawn rather than off a number copied out of a theme file
    /// on the day it was written.</para></summary>
    public static float HowToPlayHintHeight { get; set; }

    /// <summary>Bottom offset (negative — Godot's bottom-anchored convention) for the hint: the
    /// head of the bottom-right column, at the shared frame margin.</summary>
    public static float HowToPlayHintBottom => -Edge;

    /// <summary>Where the top of the hint ends up, derived from the height it reported. <b>Read,
    /// never applied to the hint itself</b> — see the trap on <see cref="PlaceHowToPlayHint"/>.
    /// The in-engine self-test asserts the drawn rectangle agrees with this, which is what makes
    /// the published height trustworthy; a second occupant of this corner takes
    /// <c>HowToPlayHintTop - Gap</c> as its own bottom, and the column closes up on its own when
    /// the hint is absent.</summary>
    public static float HowToPlayHintTop => HowToPlayHintBottom - HowToPlayHintHeight;

    /// <summary>Pins the how-to-play hint's bottom-right corner at the frame margin and lets it
    /// open up and to the left to fit its own content. <b>The one place that decides where it
    /// goes</b> — <c>GameHud</c> calls it once at build, and the in-engine self-test measures the
    /// result against <see cref="HowToPlayHintTop"/>.
    ///
    /// <para>The four offsets describe a ZERO-SIZE box in the corner; the real extent comes from
    /// Godot's minimum-size pass, which <see cref="Control.GrowDirection.Begin"/> sends leftward
    /// and upward. Nothing here names a width or a height, so the string's resolved font and the
    /// type ladder can both move without a constant going stale.</para>
    ///
    /// <para><b>TRAP, and it shipped in a capture before it was caught.</b> The first draft set
    /// <c>OffsetLeft = -Edge - hint.Size.X</c> and <c>OffsetTop = HowToPlayHintTop</c> — deriving
    /// the box from the control's own measured size. That is a feedback loop, not a placement: a
    /// box built that way is a fixed point at ANY size it is currently holding, so the size it has
    /// on the frame it is added — before a layout pass has run — is preserved forever. Measured:
    /// the hint came up 1251x340, a near-full-frame panel with its text adrift at the left edge,
    /// vertically centred. <b>A rung publishes what it measured; it must never place itself from
    /// what it published.</b> Every other occupant of these columns reads the rung ABOVE it, which
    /// is a different widget's height and therefore not a loop.</para></summary>
    public static void PlaceHowToPlayHint(Control hint)
    {
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        hint.GrowHorizontal = Control.GrowDirection.Begin;
        hint.GrowVertical = Control.GrowDirection.Begin;
        hint.OffsetRight = -Edge;
        hint.OffsetLeft = -Edge;
        hint.OffsetBottom = HowToPlayHintBottom;
        hint.OffsetTop = HowToPlayHintBottom;
    }

    // --- the lower band, for a moment that plays over live play ------------------------------

    /// <summary>Where a surface that plays <i>while the player still has the stick</i> starts, as a
    /// fraction of the frame height. The nightfall treatment rides this.
    ///
    /// <para><b>A fraction and not a pixel lift, because the thing it has to miss is a fraction.</b>
    /// <see cref="UiCoverageLaw"/> vetoes covering the aim point, and the aim point is the middle
    /// of the frame at every resolution — so a plate placed by a fixed distance from the bottom
    /// edge clears the centre on one window size and lands on it on another. Anchored just below
    /// the half, growing downward, it cannot cross the centre at any frame height. The 6% below
    /// the half is the margin: enough that the plate's top edge is visibly under the middle rather
    /// than resting on it.</para></summary>
    public const float LowerBandTopAnchor = 0.56f;

    /// <summary>Stacks one occupant under another, closing the gap when the one above is absent.</summary>
    private static float Below(float top, float height) => height > 0f ? top + height + Gap : top;
}
