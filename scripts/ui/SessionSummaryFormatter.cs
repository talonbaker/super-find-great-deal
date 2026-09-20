using System;
using MpFoundation.Game.World;

namespace MpFoundation.Ui;

/// <summary>
/// Pure formatting for <see cref="SessionSummaryPanel"/>'s display lines, pulled out of the
/// Control tree so the actual arithmetic/text (rounding, clamping, the empty-roster case) is
/// testable without a scene tree — same seam as <see cref="LoadingOverlayGate"/> and
/// <see cref="PhaseToastText"/>. The panel itself only ever calls these and sets them onto
/// Labels; it makes no formatting decisions of its own.
/// </summary>
public static class SessionSummaryFormatter
{
    public static string KidLine(KidQuarters kid)
    {
        string name = string.IsNullOrEmpty(kid.DisplayName) ? $"Player {kid.PeerId}" : kid.DisplayName;
        return $"{name} — {kid.Quarters}q";
    }

    public static string GroupTotalLine(ISessionSummarySource data) =>
        $"Group total: {data.GroupTotalQuarters}q";

    public static string PhotosLine(ISessionSummarySource data) =>
        $"Photos taken: {data.PhotosTaken}";

    /// <summary>Clamped to [0,100] and rounded to a whole percent — the source is not required
    /// to hand back an already-clamped value (see <see cref="ISessionSummarySource"/>'s doc).</summary>
    public static string CoverageLine(ISessionSummarySource data)
    {
        double clamped = Math.Clamp(data.MapCoveragePercent, 0f, 100f);
        return $"Map coverage: {Math.Round(clamped):0}%";
    }

    /// <summary>Shown in place of the per-kid list when the roster is empty — e.g. every provider
    /// still defaulted to <see cref="SessionSummarySource.Null"/> (L7/L4/L9 not yet wired in), or
    /// (the degenerate case the spec calls out) a player who joined after the run had already
    /// ended and never earned anything this run.</summary>
    public const string NoKidsLine = "No photographers logged this run.";
}
