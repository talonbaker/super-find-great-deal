using System.Collections.Generic;
using MpFoundation.Game.World;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// L11 (Issue #114): per-kid quarters, group total, photos taken, map coverage. L7 (wallet +
/// payout, Issue #110) — which owns the real numbers — and L4/L9 (photos, map coverage) are all
/// unlanded, being built in parallel; see <c>ISessionSummarySource</c>'s doc for why this Story
/// codes against a minimal interface instead. <see cref="TestSessionSummarySource"/> below is
/// that "implement against it with a test double" the dispatch asked for — a stand-in for
/// whatever L7/L4/L9 eventually wire into <c>SessionSummarySource.Current</c>.
/// </summary>
public class SessionSummaryFormatterTests
{
    private sealed class TestSessionSummarySource : ISessionSummarySource
    {
        public IReadOnlyList<KidQuarters> PerKidQuarters { get; init; } = System.Array.Empty<KidQuarters>();
        public int GroupTotalQuarters { get; init; }
        public int PhotosTaken { get; init; }
        public float MapCoveragePercent { get; init; }
    }

    [Fact]
    public void KidLine_UsesDisplayName_WhenPresent()
    {
        var kid = new KidQuarters(PeerId: 7, DisplayName: "Sam", Quarters: 12);
        Assert.Equal("Sam — 12q", SessionSummaryFormatter.KidLine(kid));
    }

    [Fact]
    public void KidLine_FallsBackToPeerId_WhenDisplayNameEmpty()
    {
        var kid = new KidQuarters(PeerId: 7, DisplayName: "", Quarters: 3);
        Assert.Equal("Player 7 — 3q", SessionSummaryFormatter.KidLine(kid));
    }

    [Fact]
    public void GroupTotalLine_ReadsFromSource()
    {
        var data = new TestSessionSummarySource { GroupTotalQuarters = 41 };
        Assert.Equal("Group total: 41q", SessionSummaryFormatter.GroupTotalLine(data));
    }

    [Fact]
    public void PhotosLine_ReadsFromSource()
    {
        var data = new TestSessionSummarySource { PhotosTaken = 9 };
        Assert.Equal("Photos taken: 9", SessionSummaryFormatter.PhotosLine(data));
    }

    [Theory]
    [InlineData(0f, "0%")]
    [InlineData(100f, "100%")]
    [InlineData(37.4f, "37%")]
    [InlineData(37.6f, "38%")]
    public void CoverageLine_RoundsToWholePercent(float pct, string expectedSuffix)
    {
        var data = new TestSessionSummarySource { MapCoveragePercent = pct };
        Assert.Equal($"Map coverage: {expectedSuffix}", SessionSummaryFormatter.CoverageLine(data));
    }

    [Theory]
    [InlineData(-15f, "Map coverage: 0%")]   // a source handing back an unclamped negative
    [InlineData(140f, "Map coverage: 100%")] // or an unclamped over-100 must still render sane.
    public void CoverageLine_ClampsOutOfRangeSourceValues(float pct, string expected)
    {
        var data = new TestSessionSummarySource { MapCoveragePercent = pct };
        Assert.Equal(expected, SessionSummaryFormatter.CoverageLine(data));
    }

    [Fact]
    public void NullSource_FormatsAsAllZeroes_NeverThrows()
    {
        // SessionSummarySource.Null is what the panel falls back to before L7/L4/L9 wire a real
        // provider in — the summary screen must render zeroes, not throw, in that case.
        ISessionSummarySource data = SessionSummarySource.Null;
        Assert.Empty(data.PerKidQuarters);
        Assert.Equal("Group total: 0q", SessionSummaryFormatter.GroupTotalLine(data));
        Assert.Equal("Photos taken: 0", SessionSummaryFormatter.PhotosLine(data));
        Assert.Equal("Map coverage: 0%", SessionSummaryFormatter.CoverageLine(data));
    }
}
