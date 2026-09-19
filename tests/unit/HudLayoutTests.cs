using Godot;
using MpFoundation.Ui.Design;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PLAY-1's engine-free tier: the coverage law's predicate and its measurement, and the shared
/// column arithmetic. The in-engine twin (<c>--hudlayout-selftest</c>) proves the real widgets lay
/// out this way; this proves the rules they lay out BY are right, including for the states a live
/// walk cannot conveniently reach.
///
/// <para><b>The positive controls are the point of this file.</b> Every assertion PLAY-1 owes is
/// absence-shaped — "these do not overlap", "this does not cover the screen" — and an absence
/// check whose measurement is broken reports a clean bill of health forever. So each rule is
/// exercised in both directions: the permitted shape must pass, and the shape that was actually
/// shipping on 2026-08-14 must fail.</para>
/// </summary>
public class HudLayoutTests
{
    // --- the coverage law ---------------------------------------------------------------------

    /// <summary>The violating state, and the whole reason this law exists: the nightfall treatment
    /// as it shipped — a full-rect wash — over a player who still has camera and movement.</summary>
    [Fact]
    public void AFullScreenWash_OverALivePlayer_IsRefused()
    {
        var frame = new Vector2(1280, 720);
        (float covered, bool coversAim) =
            UiCoverageLaw.Measure(frame, new[] { new Rect2(Vector2.Zero, frame) });

        Assert.True(covered > 0.99f, $"a full-rect wash measured {covered:P1} coverage — the measurement is broken");
        Assert.True(coversAim, "a full-rect wash does not cover the aim point — the measurement is broken");
        Assert.False(UiCoverageLaw.Permitted(covered, coversAim, cameraTaken: false, controlsTaken: false));
    }

    /// <summary>The same wash is fine once the surface has taken the player with it — that is the
    /// other half of the law, and it is what makes the pause overlay and the flow state screens
    /// legal. Without this case the "law" would just be a size limit.</summary>
    [Fact]
    public void AFullScreenWash_IsPermittedOnceCameraAndControlsAreTaken()
    {
        Assert.True(UiCoverageLaw.Permitted(1f, coversAimPoint: true, cameraTaken: true, controlsTaken: true));
    }

    /// <summary>Half-taken is not taken. A screen that freezes the stick but leaves the camera
    /// live still leaves the player looking at something they cannot see past.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HalfTakingTheInput_DoesNotBuyTheFrame(bool cameraTaken, bool controlsTaken)
    {
        Assert.False(UiCoverageLaw.Permitted(1f, coversAimPoint: true, cameraTaken, controlsTaken));
    }

    /// <summary>The shape the nightfall treatment now takes — a content-sized plate hanging from
    /// the lower-band anchor — passes, and passes at every frame height rather than at the one the
    /// numbers were picked on. The anchor is the real constant, so an edit that pushes it back up
    /// past the middle fails here.</summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 768)]
    public void TheLowerBandPlate_OverALivePlayer_IsPermitted(int width, int height)
    {
        var frame = new Vector2(width, height);
        // The measured plate from the live tree, rounded up: heading, keyline and four body lines.
        var plate = new Rect2(
            new Vector2(frame.X * 0.5f - 300f, frame.Y * UiColumns.LowerBandTopAnchor),
            new Vector2(600f, 250f));

        (float covered, bool coversAim) = UiCoverageLaw.Measure(frame, new[] { plate });

        Assert.False(coversAim, $"the lower-band plate covers the aim point at {width}x{height}");
        Assert.True(covered <= UiCoverageLaw.MaxCoveredFraction,
            $"the lower-band plate covers {covered:P1} at {width}x{height}, over the "
            + $"{UiCoverageLaw.MaxCoveredFraction:P0} bound");
        Assert.True(UiCoverageLaw.Permitted(covered, coversAim, cameraTaken: false, controlsTaken: false));
    }

    /// <summary>The anchor's whole job: below the half, at any height, so the aim point is missed
    /// by construction rather than by a pixel lift that happened to work on one window size.</summary>
    [Fact]
    public void TheLowerBandAnchor_IsBelowTheAimPoint()
    {
        Assert.True(UiColumns.LowerBandTopAnchor > 0.5f,
            "the lower band starts at or above the middle of the frame — a plate hanging from it "
            + "covers the aim point at every resolution");
        Assert.True(UiColumns.LowerBandTopAnchor < 0.75f,
            "the lower band starts so low a plate hanging from it runs off the bottom of the frame");
    }

    /// <summary>A small surface dead centre covers almost nothing and is still refused. The aim
    /// point is a veto, not a contribution — this is the case a pure area bound would wave through.</summary>
    [Fact]
    public void ASmallSurfaceOnTheAimPoint_IsStillRefused()
    {
        var frame = new Vector2(1280, 720);
        var pip = new Rect2(new Vector2(frame.X * 0.5f - 40f, frame.Y * 0.5f - 20f), new Vector2(80f, 40f));

        (float covered, bool coversAim) = UiCoverageLaw.Measure(frame, new[] { pip });

        Assert.True(covered < 0.01f);
        Assert.True(coversAim);
        Assert.False(UiCoverageLaw.Permitted(covered, coversAim, cameraTaken: false, controlsTaken: false));
    }

    /// <summary>Overlapping rectangles are a UNION, not a sum. Sampling exists precisely so a
    /// plate inside a scrim inside a root cannot report 300% coverage — which an area sum would,
    /// and which would then make the bound meaningless in the direction that matters.</summary>
    [Fact]
    public void OverlappingRects_CountOnce()
    {
        var frame = new Vector2(1000, 1000);
        var half = new Rect2(Vector2.Zero, new Vector2(1000, 500));

        (float covered, _) = UiCoverageLaw.Measure(frame, new[] { half, half, half });

        Assert.InRange(covered, 0.49f, 0.51f);
    }

    /// <summary>A degenerate frame must not silently pass everything.</summary>
    [Fact]
    public void ADegenerateViewport_MeasuresNothing()
    {
        (float covered, bool coversAim) =
            UiCoverageLaw.Measure(Vector2.Zero, new[] { new Rect2(0, 0, 100, 100) });
        Assert.Equal(0f, covered);
        Assert.False(coversAim);
    }

    // --- the top-centre column -----------------------------------------------------------------

    /// <summary>The defect, stated as arithmetic: two surfaces at the bare screen margin occupy
    /// the same band. The column exists so the second one asks where the first one ended.</summary>
    [Fact]
    public void TheTopCentreColumn_StacksInsteadOfColliding()
    {
        UiColumns.DayPhaseHeight = 34f;
        UiColumns.QuotaStripHeight = 52f;

        Assert.Equal(UiColumns.Edge, UiColumns.DayPhaseTop);
        // The positive control for this rule: at the margin they WOULD collide. If the strip's top
        // were still UiColumns.Edge, this is the comparison that would fail.
        Assert.True(UiColumns.QuotaStripTop >= UiColumns.DayPhaseTop + 34f,
            "the winter-cache strip starts inside the day/phase readout");
        Assert.True(UiColumns.PhaseToastTop >= UiColumns.QuotaStripTop + 52f,
            "the phase toast starts inside the winter-cache strip");
    }

    /// <summary>An absent rung closes the column up rather than leaving a hole — the flow-screen
    /// demo and the self-test both run with no HUD at all, and the strip must not float.</summary>
    [Fact]
    public void AnAbsentRung_ClosesTheColumn()
    {
        UiColumns.DayPhaseHeight = 0f;
        UiColumns.QuotaStripHeight = 0f;

        Assert.Equal(UiColumns.Edge, UiColumns.QuotaStripTop);
        Assert.Equal(UiColumns.Edge, UiColumns.PhaseToastTop);
    }

    /// <summary>The met/not-met wording changes the strip's height, and the toast under it has to
    /// move with it. Talon's note pins the collision to that readout's state, so the rule is
    /// checked at two heights rather than one.</summary>
    [Theory]
    [InlineData(40f)]
    [InlineData(64f)]
    public void TheToast_TracksTheStripsOwnHeight(float stripHeight)
    {
        UiColumns.DayPhaseHeight = 34f;
        UiColumns.QuotaStripHeight = stripHeight;

        Assert.Equal(UiColumns.QuotaStripTop + stripHeight + UiColumns.Gap, UiColumns.PhaseToastTop);
    }

    // --- the bottom-left column ----------------------------------------------------------------

    /// <summary>The defect as it shipped: the room code was authored 14 px off the bottom edge,
    /// which is inside the carry block's verb hint. Both bands are computed here so the fix is a
    /// measured clearance rather than a nudge that happened to look right once.</summary>
    [Fact]
    public void TheRoomCode_ClearsTheWholeCarryBlock()
    {
        Assert.True(UiColumns.RoomCodeBottom <= -UiColumns.BottomLeftClearance,
            $"the room code's bottom ({UiColumns.RoomCodeBottom}) is inside the carry block "
            + $"({UiColumns.BottomLeftClearance} up from the edge)");
        Assert.True(UiColumns.RoomCodeTop < UiColumns.RoomCodeBottom, "the room code has no height");

        // The positive control: the authored placement this replaced. Same comparison, and it must
        // report the overlap — otherwise the assertion above is not testing anything.
        const float shippedBottom = -14f;
        Assert.False(shippedBottom <= -UiColumns.BottomLeftClearance,
            "the comparison cannot detect the placement that was actually overlapping");
    }

    // --- the bottom-right column ---------------------------------------------------------------

    /// <summary>The hint's own rung: it hangs from the frame margin and grows UPWARD by exactly
    /// the height it reported, so the corner it occupies is the one it measured rather than one a
    /// constant guessed. Checked at two heights because the line's height is the type ladder's to
    /// move, and a rung that only worked at today's font size is not a rung.</summary>
    [Theory]
    [InlineData(22f)]
    [InlineData(34f)]
    public void TheHowToPlayHint_HangsFromTheMarginByItsMeasuredHeight(float height)
    {
        UiColumns.HowToPlayHintHeight = height;

        Assert.Equal(-UiColumns.Edge, UiColumns.HowToPlayHintBottom);
        Assert.Equal(UiColumns.HowToPlayHintBottom - height, UiColumns.HowToPlayHintTop);
        // Bottom-anchored offsets are negative and grow upward, so the top must be the SMALLER
        // number. The positive control for the sign: get this backwards and the hint is placed
        // off the bottom of the frame, which is exactly where the room code used to end up.
        Assert.True(UiColumns.HowToPlayHintTop < UiColumns.HowToPlayHintBottom,
            "the how-to-play hint has no height, or grows downward off the frame");
    }

    /// <summary>An absent hint — a world whose <c>HudProfile</c> opted out, or a HUD hidden under
    /// the pause overlay — closes the column up rather than reserving a band nobody draws in.</summary>
    [Fact]
    public void AnAbsentHint_ReservesNothing()
    {
        UiColumns.HowToPlayHintHeight = 0f;
        Assert.Equal(UiColumns.HowToPlayHintBottom, UiColumns.HowToPlayHintTop);
    }

    /// <summary>The hint is bottom-RIGHT and the room code is bottom-LEFT, and they miss each other
    /// on the VERTICAL axis alone: the room code is lifted a whole carry-block clearance off the
    /// edge the hint rests on, so the two bands never meet whatever their widths do.
    ///
    /// <para>Worth pinning rather than assuming, because "different corner" is not a separation
    /// the columns enforce. The room code's box is an authored 284 px and the hint is content-sized
    /// off a font the design system may grow; on a narrow frame, two labels sharing one band would
    /// meet in the middle. Being in different bands is the property that holds at every width.</para></summary>
    [Fact]
    public void TheHint_DoesNotShareABandWithTheRoomCode()
    {
        UiColumns.HowToPlayHintHeight = 22f;

        Assert.True(UiColumns.HowToPlayHintTop >= UiColumns.RoomCodeBottom,
            $"the how-to-play hint (top {UiColumns.HowToPlayHintTop}) reaches up into the room "
            + $"code's band (bottom {UiColumns.RoomCodeBottom}) — the two are then kept apart only "
            + "by sitting in different corners, which no frame width guarantees");

        // The positive control: a hint tall enough to climb into that band must fail the same
        // comparison, or the assertion above is not detecting the overlap it names.
        UiColumns.HowToPlayHintHeight = 200f;
        Assert.False(UiColumns.HowToPlayHintTop >= UiColumns.RoomCodeBottom,
            "the comparison cannot detect a hint that HAS grown into the room code's band");
        UiColumns.HowToPlayHintHeight = 0f;
    }

    /// <summary>The coverage law, applied to the hint: a corner scrap over a player who still has
    /// camera and controls. Generously oversized here (240x40 against a measured ~150x22) so the
    /// case still holds if the copy or the type scale grows, and run at three frame sizes because
    /// the aim-point veto is a fraction of the frame, not a pixel distance.</summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 768)]
    public void TheHowToPlayHint_OverALivePlayer_IsPermitted(int width, int height)
    {
        var frame = new Vector2(width, height);
        const float hintW = 240f;
        const float hintH = 40f;
        UiColumns.HowToPlayHintHeight = hintH;

        // The rect UiColumns.PlaceHowToPlayHint resolves to, in absolute frame coordinates: the
        // offsets are negative from the bottom-right corner.
        var hint = new Rect2(
            new Vector2(frame.X - UiColumns.Edge - hintW, frame.Y + UiColumns.HowToPlayHintTop),
            new Vector2(hintW, hintH));

        (float covered, bool coversAim) = UiCoverageLaw.Measure(frame, new[] { hint });

        Assert.False(coversAim, $"the how-to-play hint covers the aim point at {width}x{height}");
        Assert.True(covered <= UiCoverageLaw.MaxCoveredFraction,
            $"the how-to-play hint covers {covered:P1} at {width}x{height}");
        Assert.True(UiCoverageLaw.Permitted(covered, coversAim, cameraTaken: false, controlsTaken: false));
        Assert.True(frame.Y + UiColumns.HowToPlayHintTop > frame.Y * 0.5f,
            $"the how-to-play hint has climbed above the middle of a {width}x{height} frame");
    }

    /// <summary>Every gap this file places anything with is a step on the one scale — the guard
    /// UiScale exists for, applied to the column ladder.</summary>
    [Fact]
    public void TheColumnGaps_AreOnTheScale()
    {
        Assert.Contains((int)UiColumns.Gap, UiScale.SpaceSteps);
        Assert.Contains((int)UiColumns.Edge, UiScale.SpaceSteps);
    }
}
