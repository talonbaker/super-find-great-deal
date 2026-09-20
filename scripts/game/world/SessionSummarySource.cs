using System;
using System.Collections.Generic;

namespace MpFoundation.Game.World;

/// <summary>One kid's final tally for the session summary screen.</summary>
public readonly record struct KidQuarters(int PeerId, string DisplayName, int Quarters);

/// <summary>
/// The minimal read surface L11's session summary (Issue #114) needs from L7 (the per-player
/// tally + sunset payout, Issue #110) and, for photo/map stats, L4 (the camera + evidence sites)
/// and L9 (the map) — none of which are in this build; L11 was built in parallel per the dispatch.
/// This is the interface L7 has been told to expose on its own side; reconcile the real
/// implementation against it at merge, not by re-deriving the shape here later.
///
/// <see cref="SessionSummarySource"/> below is the seam: whichever system lands wires its real
/// implementation into <see cref="SessionSummarySource.Current"/>, and
/// <see cref="Ui.SessionSummaryPanel"/> reads it lazily at <c>RunEndedSignal</c> time (never
/// cached earlier), so whichever provider lands last is still picked up without this Story
/// needing a rebuild. Until something wires one in, reads fall back to
/// <see cref="SessionSummarySource.Null"/> — an all-zero test double — so a summary shown before
/// L7/L4/L9 exist (today, and in every test double scenario) degrades to zeroes instead of
/// throwing. Same "never strand" discipline the loading overlay uses, just for a screen instead
/// of a state machine.
/// </summary>
public interface ISessionSummarySource
{
    IReadOnlyList<KidQuarters> PerKidQuarters { get; }
    int GroupTotalQuarters { get; }
    int PhotosTaken { get; }

    /// <summary>0-100. Not required to be clamped by the implementation —
    /// <see cref="Ui.SessionSummaryFormatter"/> clamps defensively on display.</summary>
    float MapCoveragePercent { get; }
}

/// <summary>Process-wide seam L7/L4/L9 wire their real implementation into. See the interface
/// doc above for why this is read lazily rather than injected at construction time.</summary>
public static class SessionSummarySource
{
    /// <summary>Null until a provider wires itself in. <see cref="Ui.SessionSummaryPanel"/> reads
    /// <c>Current ?? Null</c>, never this directly, so a summary shown with no provider wired
    /// (this PR's own test double scenario, or a real session before L7/L4/L9 land) renders
    /// zeroes instead of throwing a NullReferenceException on the summary screen — the one
    /// screen a player is guaranteed to see at the end of every run.</summary>
    public static ISessionSummarySource? Current { get; set; }

    /// <summary>The all-zero fallback described above.</summary>
    public static readonly ISessionSummarySource Null = new NullSessionSummarySource();

    private sealed class NullSessionSummarySource : ISessionSummarySource
    {
        public IReadOnlyList<KidQuarters> PerKidQuarters => Array.Empty<KidQuarters>();
        public int GroupTotalQuarters => 0;
        public int PhotosTaken => 0;
        public float MapCoveragePercent => 0f;
    }
}
