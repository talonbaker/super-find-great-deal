using System.Collections.Generic;
using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>
/// <b>The standing law: non-diegetic UI may cover the screen ONLY if the camera AND the controls
/// were also taken.</b>
///
/// <para>A player who can still look and still walk is still playing, and a surface that takes the
/// frame away from them while they do is taking away the only instrument they have. The pause
/// overlay and the flow state screens are allowed to own the frame because they own the input with
/// it. A beat that plays <i>over live play</i> is not: it is a moment, and a moment has to leave
/// the game visible underneath it.</para>
///
/// <para><b>Why the rule is expressed as a measurement rather than a review note.</b> It has been
/// broken twice by surfaces whose authors each knew the law — because "does this cover the screen"
/// was answered by looking at the code, and a full-rect <c>ColorRect</c> at 55% alpha does not read
/// as a modal in a diff. So the law is a predicate over two measured facts (how much of the frame
/// is painted, and whether the point the player is aiming at is one of them) and two state facts
/// (was the camera taken, were the controls taken). <see cref="Measure"/> produces the first pair
/// from real node rectangles; <c>--hudlayout-selftest</c> feeds it the live nightfall treatment and
/// a deliberately violating control, so the check is shown capable of returning both answers.</para>
///
/// <para>This is not a general-purpose "is the UI too big" heuristic and must not grow into one.
/// It answers exactly one question, for surfaces that draw while the player still has the stick.</para>
/// </summary>
public static class UiCoverageLaw
{
    /// <summary>The most of the frame a surface may paint while the player still has camera and
    /// controls. A quarter is generous on purpose: the point of the bound is to separate a plate
    /// from a scrim, not to police a layout. The nightfall treatment measures around an eighth.</summary>
    public const float MaxCoveredFraction = 0.25f;

    /// <summary>Sampling resolution for <see cref="Measure"/>. 64x64 = 4096 points over the frame:
    /// fine enough that a plate's fraction is accurate to well under a percent, coarse enough to
    /// run inside one frame of a headless self-test.</summary>
    public const int SampleGrid = 64;

    /// <summary>
    /// The law itself. Either the surface took the player's camera and controls with it — in which
    /// case it may own the whole frame — or it left the aim point clear and stayed under the bound.
    ///
    /// <para>Note the aim point is a hard veto, not a contribution to the fraction: a small plate
    /// dead centre covers almost nothing and still blinds the player exactly where they are looking.</para>
    /// </summary>
    public static bool Permitted(
        float coveredFraction, bool coversAimPoint, bool cameraTaken, bool controlsTaken) =>
        (cameraTaken && controlsTaken) || (!coversAimPoint && coveredFraction <= MaxCoveredFraction);

    /// <summary>
    /// What share of the frame a set of painted rectangles covers, and whether any of them covers
    /// the aim point (the frame's centre).
    ///
    /// <para>Sampled rather than analytic because the rectangles overlap — a plate inside a scrim
    /// inside a root — and summing their areas would count the same pixel several times and report
    /// coverage above 1.0 for a surface that covers half the screen. A grid of points is exact
    /// about the union, which is the quantity the law is actually about.</para>
    ///
    /// <para>Returns (0, false) for a degenerate viewport rather than dividing by zero; the caller
    /// is expected to assert the viewport is real, since a zero-size frame would otherwise let
    /// every surface pass.</para>
    /// </summary>
    public static (float Covered, bool CoversAim) Measure(
        Vector2 viewport, IReadOnlyList<Rect2> painted, int grid = SampleGrid)
    {
        if (viewport.X <= 0f || viewport.Y <= 0f || grid <= 0)
            return (0f, false);

        int hits = 0;
        for (int iy = 0; iy < grid; iy++)
        {
            float y = (iy + 0.5f) / grid * viewport.Y;
            for (int ix = 0; ix < grid; ix++)
            {
                float x = (ix + 0.5f) / grid * viewport.X;
                if (Covers(painted, new Vector2(x, y)))
                    hits++;
            }
        }

        return ((float)hits / (grid * grid), Covers(painted, viewport * 0.5f));
    }

    private static bool Covers(IReadOnlyList<Rect2> painted, Vector2 point)
    {
        for (int i = 0; i < painted.Count; i++)
            if (painted[i].HasPoint(point))
                return true;
        return false;
    }
}
