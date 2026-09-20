using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MpFoundation.Game.Round;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>HOLD-1 (2026-09-19): the holding-room board's whole contents, with no engine.</b>
///
/// <para>The split between this file and <c>tests/Run-HoldingBoardTest.ps1</c> is the one ROUND-1
/// drew and CLOCK-1 restated: everything here is a RULE and is therefore a pure function of one
/// <see cref="HideSeekView"/>, and none of it is repeated in the scene suite. What the smoke
/// exists for is the half a pure function cannot reach — that a peer which joined in the middle
/// of a round paints the same rows as the host did, off ONE wire message, in a separate
/// process.</para>
/// </summary>
public class HoldingBoardTests
{
    private const int Ada = 11;
    private const int Ben = 22;
    private const int Cal = 33;

    private static readonly HideSeekTuning Tuning = HideSeekTuning.Default;

    private static string Names(int peerId) => peerId switch
    {
        Ada => "ADA",
        Ben => "BEN",
        Cal => "CAL",
        _ => "",
    };

    private static HideSeekView View(
        HideSeekPhase phase = HideSeekPhase.Holding,
        int round = 1,
        int hider = Ada,
        int seeker = Ben,
        IEnumerable<KeyValuePair<int, int>>? scores = null,
        HideSeekTally? card = null) =>
        new(phase, round, 0f, hider, seeker,
            // Kept AS-IS when it is already immutable: ToImmutableDictionary() would rebuild it
            // with the DEFAULT comparer and silently undo the hostile one
            // Rows_AreAscendingEvenWhenTheMapEnumeratesBackwards depends on. (Measured: with the
            // rebuild in place, that test passed against a deliberately unsorted implementation.)
            scores as ImmutableDictionary<int, int>
                ?? (scores ?? new Dictionary<int, int> { [Ada] = 7, [Ben] = 5 })
                    .ToImmutableDictionary(),
            HideSeekRefusal.None, 0, 0, card);

    private static HideSeekTally Card(
        int round = 1, int hiderGain = 3, int seekerGain = 48, bool disconnect = false,
        bool matchOver = false, int matchIndex = 1, int winner = 0,
        int hiderTotal = 7, int seekerTotal = 5) =>
        new(round, Ada, hiderGain, Ben, seekerGain, disconnect,
            matchOver, matchIndex, winner, hiderTotal, seekerTotal);

    // =====================================================================================
    // 1. The rows: who is on the board, in what order
    // =====================================================================================

    [Fact]
    public void Rows_AreOnePerHumanOnTheWire()
    {
        IReadOnlyList<HoldingBoardRow> rows = HoldingBoardModel.Rows(View(), Ada, Names);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { Ada, Ben }, rows.Select(r => r.PeerId));
        Assert.Equal(new[] { "ADA", "BEN" }, rows.Select(r => r.Name));
    }

    /// <summary>
    /// <b>Ascending peer id, and the property that matters is that it does not MOVE.</b> The
    /// packet asks for join order; join order is not on the wire (see
    /// <see cref="HoldingBoardModel.Rows"/>), so this is what the board can actually promise —
    /// and it is the promise a reader needs: the same names in the same places every time they
    /// glance up.
    /// </summary>
    [Fact]
    public void Rows_AreOrderedByPeerIdAndDoNotMoveWhenRolesSwap()
    {
        var scores = new Dictionary<int, int> { [Cal] = 1, [Ada] = 7, [Ben] = 5 };

        IReadOnlyList<HoldingBoardRow> before =
            HoldingBoardModel.Rows(View(hider: Ada, seeker: Ben, scores: scores), Ada, Names);
        IReadOnlyList<HoldingBoardRow> after =
            HoldingBoardModel.Rows(View(hider: Ben, seeker: Ada, scores: scores), Ada, Names);

        Assert.Equal(new[] { Ada, Ben, Cal }, before.Select(r => r.PeerId));
        Assert.Equal(before.Select(r => r.PeerId), after.Select(r => r.PeerId));
    }

    /// <summary>The order cannot depend on how the dictionary was built, or two peers folding the
    /// same message in a different order would print different boards. Insertion order is
    /// shuffled and the answer must not move.</summary>
    [Fact]
    public void Rows_AreOrderedIdenticallyWhateverOrderTheScoresWereBuiltIn()
    {
        int[] ids = { Cal, Ada, Ben };
        var forward = ids.ToDictionary(i => i, i => i);
        var backward = ids.Reverse().ToDictionary(i => i, i => i);

        Assert.Equal(
            HoldingBoardModel.Rows(View(scores: forward), Ada, Names).Select(r => r.PeerId),
            HoldingBoardModel.Rows(View(scores: backward), Ada, Names).Select(r => r.PeerId));
    }

    /// <summary>
    /// <b>The sort is PROVED to execute, which took a second mutation to arrange.</b>
    ///
    /// <para>Deleting <c>ids.Sort()</c> left every ordering assertion above green, and that is a
    /// fact about the key type rather than about the sort:
    /// <see cref="ImmutableDictionary{TKey,TValue}"/> with the default <c>int</c> comparer
    /// enumerates in hash order, <c>int</c>'s hash IS the value, and the trie's traversal of
    /// small positive ints comes out ASCENDING. Measured with four real peer ids taken from
    /// <c>tests/logs/</c> (1076010669 / 652333145 / 1788870000 / 233849852), which enumerate as
    /// 233849852, 652333145, 1076010669, 1788870000 — already sorted. So with the shipped wire
    /// the sort is currently unobservable.</para>
    ///
    /// <para><b>To prove a guard, first make its call site reach it</b> (CELEBRATE-1's lesson,
    /// measured in this repo). This builds the score map with a comparer that hashes to the
    /// NEGATED key, which reverses the trie's traversal, and then requires the rows to come out
    /// ascending anyway. It goes red the moment the sort goes away. It is not a scenario the
    /// shipped fold produces — the point is that the board's ordering is a property of the
    /// board, not a lucky property of whichever map someone hands it.</para>
    /// </summary>
    [Fact]
    public void Rows_AreAscendingEvenWhenTheMapEnumeratesBackwards()
    {
        int[] ids = { 1076010669, 652333145, 1788870000, 233849852 };
        ImmutableDictionary<int, int> hostile = ImmutableDictionary
            .Create<int, int>(new NegatedHash())
            .AddRange(ids.Select(i => new KeyValuePair<int, int>(i, 0)));

        // The positive control: this map really does enumerate in a different order from
        // ascending, or the assertion below would be the same free pass as the toy ids.
        Assert.NotEqual(ids.OrderBy(i => i).ToArray(), hostile.Select(kv => kv.Key).ToArray());

        Assert.Equal(
            ids.OrderBy(i => i).ToArray(),
            HoldingBoardModel.Rows(View(scores: hostile), ids[0], null)
                .Select(r => r.PeerId).ToArray());
    }

    /// <inheritdoc cref="Rows_AreAscendingEvenWhenTheMapEnumeratesBackwards"/>
    private sealed class NegatedHash : IEqualityComparer<int>
    {
        public bool Equals(int a, int b) => a == b;
        public int GetHashCode(int v) => -v;
    }

    [Fact]
    public void Rows_AreEmptyBeforeTheFirstMessage()
    {
        var view = new HideSeekView(HideSeekPhase.Holding, 0, 0f, 0, 0,
            ImmutableDictionary<int, int>.Empty, HideSeekRefusal.None, 0, 0, null);
        Assert.Empty(HoldingBoardModel.Rows(view, Ada, Names));
        Assert.Equal(0, HoldingBoardModel.Overflow(view));
    }

    /// <summary>A null score map is what an unfolded view carries, and a board that threw on it
    /// would take the whole room's rendering down for a frame nobody needed.</summary>
    [Fact]
    public void Rows_SurviveANullScoreMap()
    {
        var view = new HideSeekView(HideSeekPhase.Holding, 1, 0f, Ada, Ben,
            null!, HideSeekRefusal.None, 0, 0, null);
        Assert.Empty(HoldingBoardModel.Rows(view, Ada, Names));
    }

    [Fact]
    public void Rows_StopAtMaxRowsAndSayHowManyWereDropped()
    {
        var scores = Enumerable.Range(1, HoldingBoardModel.MaxRows + 2)
            .ToDictionary(i => i * 10, i => i);
        HideSeekView view = View(scores: scores);

        Assert.Equal(HoldingBoardModel.MaxRows, HoldingBoardModel.Rows(view, Ada, Names).Count);
        Assert.Equal(2, HoldingBoardModel.Overflow(view));
    }

    [Fact]
    public void Rows_CarryTheLiveCumulativeScore()
    {
        var scores = new Dictionary<int, int> { [Ada] = 7, [Ben] = 5 };
        IReadOnlyList<HoldingBoardRow> rows =
            HoldingBoardModel.Rows(View(scores: scores), Ada, Names);
        Assert.Equal(7, rows.Single(r => r.PeerId == Ada).Score);
        Assert.Equal(5, rows.Single(r => r.PeerId == Ben).Score);
    }

    [Fact]
    public void Rows_FallBackToAnHonestNameWhenTheResolverHasNone()
    {
        IReadOnlyList<HoldingBoardRow> rows = HoldingBoardModel.Rows(View(), Ada, _ => "");
        Assert.Equal($"PLAYER {Ada}", rows[0].Name);
    }

    // =====================================================================================
    // 2. The role cell
    // =====================================================================================

    [Theory]
    [InlineData(HideSeekPhase.Hiding)]
    [InlineData(HideSeekPhase.Seeking)]
    [InlineData(HideSeekPhase.Together)]
    [InlineData(HideSeekPhase.Tally)]
    public void RoleCell_IsThePlainRoleOutsideHolding(HideSeekPhase phase)
    {
        HideSeekView view = View(phase);
        Assert.Equal("HIDES", HoldingBoardModel.RoleCell(view, Ada, isSelf: true));
        Assert.Equal("SEEKS", HoldingBoardModel.RoleCell(view, Ben, isSelf: false));
    }

    /// <summary><b>During Holding the cell is the announcement</b>, because
    /// <c>HideSeekLoop.EnterHolding</c> has already swapped the two ids at the Tally -&gt; Holding
    /// commit — so the view standing at the rack already names next round's hider.</summary>
    [Fact]
    public void RoleCell_AnnouncesTheNextRoundDuringHolding()
    {
        HideSeekView view = View(HideSeekPhase.Holding);
        Assert.Equal("NEXT: YOU HIDE", HoldingBoardModel.RoleCell(view, Ada, isSelf: true));
        Assert.Equal("NEXT: YOU SEEK", HoldingBoardModel.RoleCell(view, Ben, isSelf: true));
        Assert.Equal("NEXT: HIDES", HoldingBoardModel.RoleCell(view, Ada, isSelf: false));
        Assert.Equal("NEXT: SEEKS", HoldingBoardModel.RoleCell(view, Ben, isSelf: false));
    }

    /// <summary>The pronoun is the ONLY thing that differs between two peers' copies of this
    /// wall. If anything else ever did, two players in one room would be reading two different
    /// boards, which is the failure this pins.</summary>
    [Fact]
    public void Rows_DifferBetweenPeersOnlyInThePronoun()
    {
        HideSeekView view = View(HideSeekPhase.Holding);
        IReadOnlyList<HoldingBoardRow> mine = HoldingBoardModel.Rows(view, Ada, Names);
        IReadOnlyList<HoldingBoardRow> theirs = HoldingBoardModel.Rows(view, Ben, Names);

        Assert.Equal(mine.Select(r => r.PeerId), theirs.Select(r => r.PeerId));
        Assert.Equal(mine.Select(r => r.Name), theirs.Select(r => r.Name));
        Assert.Equal(mine.Select(r => r.Score), theirs.Select(r => r.Score));
        Assert.NotEqual(mine.Select(r => r.Role), theirs.Select(r => r.Role));
        foreach (HoldingBoardRow row in mine.Concat(theirs))
            Assert.Contains(row.Role.Contains("YOU") ? "YOU" : "NEXT", row.Role);
    }

    [Fact]
    public void RoleCell_IsAnEmDashForAPeerWithNeitherRole()
    {
        HideSeekView view = View(HideSeekPhase.Seeking,
            scores: new Dictionary<int, int> { [Ada] = 1, [Ben] = 1, [Cal] = 0 });
        Assert.Equal(HoldingBoardModel.NoRole,
            HoldingBoardModel.RoleCell(view, Cal, isSelf: true));
    }

    /// <summary>Peer 0 is "no hider assigned yet", not "the peer whose id is 0". A row cannot
    /// have id 0 (the wire never carries one), but the cell is also asked about the role ids
    /// directly and must not answer HIDES to a vacancy.</summary>
    [Fact]
    public void RoleCell_NeverMatchesAVacantRole()
    {
        HideSeekView view = View(HideSeekPhase.Seeking, hider: 0, seeker: 0);
        Assert.Equal(HoldingBoardModel.NoRole, HoldingBoardModel.RoleCell(view, 0, isSelf: true));
        Assert.Equal(HoldingBoardModel.NoRole, HoldingBoardModel.RoleCell(view, Ada, isSelf: true));
    }

    [Fact]
    public void RowLine_ReadsNameRoleScore()
    {
        IReadOnlyList<HoldingBoardRow> rows =
            HoldingBoardModel.Rows(View(HideSeekPhase.Holding), Ada, Names);
        Assert.Equal("ADA · NEXT: YOU HIDE · 7", HoldingBoardModel.RowLine(rows[0]));
        Assert.Equal("BEN · NEXT: SEEKS · 5", HoldingBoardModel.RowLine(rows[1]));
    }

    // =====================================================================================
    // 3. The header
    // =====================================================================================

    [Fact]
    public void Header_NamesTheMatchTheRoundAndThePhase()
    {
        Assert.Equal("MATCH 1 · ROUND 1 OF 2 · HOLDING",
            HoldingBoardModel.Header(View(HideSeekPhase.Holding, round: 1), Tuning));
        Assert.Equal("MATCH 1 · ROUND 2 OF 2 · SEEKING",
            HoldingBoardModel.Header(View(HideSeekPhase.Seeking, round: 2), Tuning));
        Assert.Equal("MATCH 2 · ROUND 1 OF 2 · HIDING",
            HoldingBoardModel.Header(View(HideSeekPhase.Hiding, round: 3), Tuning));
    }

    /// <summary><b>The header follows the LIVE round and the footer follows the card</b>, and
    /// between matches those two disagree on purpose: the session is already counting match 2
    /// while the card still reports match 1's result.</summary>
    [Fact]
    public void Header_CountsTheNextMatchWhileTheFooterStillReportsTheLastOne()
    {
        HideSeekView view = View(HideSeekPhase.Holding, round: 3,
            card: Card(round: 2, matchOver: true, matchIndex: 1, winner: Ada));

        Assert.StartsWith("MATCH 2", HoldingBoardModel.Header(view, Tuning));
        Assert.StartsWith("MATCH 1", HideSeekText.BoardFooter(view, Names, Tuning));
    }

    [Fact]
    public void Header_SurvivesAZeroedViewWithoutDividingByZero()
    {
        var zeroRounds = HideSeekTuning.Default with { MatchRounds = 0 };
        string header = HoldingBoardModel.Header(View(round: 0), zeroRounds);
        Assert.Contains("MATCH 1", header);
        Assert.Contains("ROUND 1 OF 1", header);
    }

    // =====================================================================================
    // 4. The footer — the card, never the live scores
    // =====================================================================================

    [Fact]
    public void Footer_IsEmptyBeforeTheFirstRoundCommits()
    {
        Assert.Equal("", HideSeekText.BoardFooter(View(), Names, Tuning));
    }

    [Fact]
    public void Footer_ShowsTheRoundCardDuringTallyAndStillDuringTheNextHolding()
    {
        HideSeekTally card = Card(round: 1, hiderGain: 3, seekerGain: 48);

        string atTally = HideSeekText.BoardFooter(
            View(HideSeekPhase.Tally, round: 1, card: card), Names, Tuning);
        string atHolding = HideSeekText.BoardFooter(
            View(HideSeekPhase.Holding, round: 2, card: card), Names, Tuning);

        Assert.Contains("ADA hid", atTally);
        Assert.Contains("3 sorted", atTally);
        Assert.Contains("BEN +48", atTally);
        // THIS is the case the board has and the strip does not: MatchLine goes empty in Holding
        // after an ordinary round, and the board keeps the card up anyway.
        Assert.Equal("", HideSeekText.MatchLine(
            View(HideSeekPhase.Holding, round: 2, card: card), Names, Tuning));
        Assert.Equal(atTally, atHolding);
    }

    [Fact]
    public void Footer_ShowsTheMatchResultAfterAMatchEnds()
    {
        HideSeekView view = View(HideSeekPhase.Tally, round: 2,
            card: Card(round: 2, matchOver: true, matchIndex: 1, winner: Ada,
                hiderTotal: 7, seekerTotal: 5));
        string footer = HideSeekText.BoardFooter(view, Names, Tuning);
        Assert.Contains("MATCH 1", footer);
        Assert.Contains("ADA WINS", footer);
        Assert.Contains("7–5", footer);
    }

    /// <summary>
    /// <b>The one the orchestrator ruled on: the board reads the CARD, never
    /// <c>view.Scores</c>.</b> The Start that begins the next match zeroes the live map while the
    /// card still holds the result, so a footer built from the live totals would silently become
    /// "ADA WON 0–0" the instant the next round began. The ROW column is the opposite case and
    /// correctly goes to zero — both are asserted here, off the same view, because the whole
    /// point is that they disagree.
    /// </summary>
    [Fact]
    public void Footer_KeepsTheMatchTotalsAfterTheNextStartZeroesTheLiveScores()
    {
        HideSeekTally card = Card(round: 2, matchOver: true, matchIndex: 1, winner: Ada,
            hiderTotal: 7, seekerTotal: 5);
        HideSeekView afterStart = View(HideSeekPhase.Holding, round: 3,
            scores: new Dictionary<int, int> { [Ada] = 0, [Ben] = 0 },
            card: card);

        Assert.Contains("7–5", HideSeekText.BoardFooter(afterStart, Names, Tuning));
        Assert.All(HoldingBoardModel.Rows(afterStart, Ada, Names), r => Assert.Equal(0, r.Score));
    }

    [Fact]
    public void Footer_NamesTheLeaverWhenARoundEndedByDisconnect()
    {
        // BEN's row is gone from the live roster, which is how MatchLine recovers who left.
        HideSeekView view = View(HideSeekPhase.Tally, round: 1,
            scores: new Dictionary<int, int> { [Ada] = 0 },
            card: Card(round: 1, disconnect: true));
        string footer = HideSeekText.BoardFooter(view, Names, Tuning);
        Assert.Contains("ended: BEN left", footer);
    }

    // =====================================================================================
    // 5. The panel's width — the copy against the board it is painted on
    // =====================================================================================

    /// <summary>The layout constants have to describe the scene file, or every bound below is
    /// about a panel that does not exist. Read from <c>HoldingBoard.tscn</c> rather than
    /// restated.</summary>
    [Fact]
    public void TheLayoutConstantsMatchTheAuthoredPanel()
    {
        string scene = System.IO.File.ReadAllText(System.IO.Path.Combine(
            FindRepoRoot(), "scenes", "game", "world", "supermarket", "HoldingBoard.tscn"));

        Assert.Contains(
            $"size = Vector3({HoldingBoardLayout.PanelWidthM.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 2.2, 0.08)",
            scene);
        Assert.Contains("font_size = 40", scene);   // the rows
        Assert.Contains("font_size = 32", scene);   // header and footer
        Assert.True(HoldingBoardLayout.TextWidthM < HoldingBoardLayout.PanelWidthM,
            "the usable width must leave a margin inside the panel");
    }

    /// <summary>
    /// <b>The ordinary row fits at full size</b>, which is the bar that matters: the shrink-to-fit
    /// exists for the exceptional line, and a board whose every row is already shrunk has no
    /// headroom left for a long name. Measured against the LONGEST cell the role column can
    /// produce (the Holding announcement), not against the short one.
    /// </summary>
    [Fact]
    public void AnOrdinaryRowFitsThePanelAtItsAuthoredSize()
    {
        IReadOnlyList<HoldingBoardRow> rows =
            HoldingBoardModel.Rows(View(HideSeekPhase.Holding), Ada, Names);
        Assert.NotEmpty(rows);
        foreach (HoldingBoardRow row in rows)
        {
            string line = HoldingBoardModel.RowLine(row);
            Assert.True(line.Length <= HoldingBoardLayout.RowChars,
                $"'{line}' is {line.Length} chars against a panel that holds "
                + $"{HoldingBoardLayout.RowChars}");
        }
    }

    /// <summary>And the worst row the wire can produce is still above the legibility floor —
    /// the same construction the footer gets, because a row is the thing actually being
    /// scanned.</summary>
    [Fact]
    public void TheRowFloorIsBelowTheWorstRowTheWireCanProduce()
    {
        const int widestPeerId = 2147483647;
        HideSeekView view = new(HideSeekPhase.Holding, 1, 0f, widestPeerId, widestPeerId - 1,
            new Dictionary<int, int> { [widestPeerId] = 255, [widestPeerId - 1] = 255 }
                .ToImmutableDictionary(),
            HideSeekRefusal.None, 0, 0, null);

        foreach (HoldingBoardRow row in HoldingBoardModel.Rows(view, widestPeerId, null))
        {
            string line = HoldingBoardModel.RowLine(row);
            float fitted = HoldingBoardLayout.FitPixelSize(1f, line.Length,
                HoldingBoardLayout.RowChars);
            Assert.True(fitted > HoldingBoardLayout.MinScale,
                $"the worst row the wire can produce is {line.Length} chars ({line}), "
                + $"which the fit clamps at the {HoldingBoardLayout.MinScale} floor");
        }
    }

    [Fact]
    public void FitPixelSize_NeverGrowsAndNeverGoesBelowTheFloor()
    {
        const float authored = 0.005f;
        Assert.Equal(authored, HoldingBoardLayout.FitPixelSize(authored, 1, 20));
        Assert.Equal(authored, HoldingBoardLayout.FitPixelSize(authored, 20, 20));
        Assert.Equal(authored, HoldingBoardLayout.FitPixelSize(authored, 0, 20));
        Assert.True(HoldingBoardLayout.FitPixelSize(authored, 40, 20) < authored);
        Assert.Equal((double)(authored * HoldingBoardLayout.MinScale),
            HoldingBoardLayout.FitPixelSize(authored, 100000, 20), 6);
    }

    /// <summary>
    /// <b>The floor is proved against the worst line the WIRE can produce, not against a line
    /// somebody thought of</b> — <see cref="RoundClockLayout.MinScale"/>'s own derivation, done
    /// by construction here rather than by arithmetic in a comment. Both gains and both totals
    /// are pinned at the byte ceiling the wire clamps to, and both names fall back to the
    /// <c>PLAYER &lt;id&gt;</c> form at the widest peer id Godot hands out.
    /// </summary>
    [Fact]
    public void TheFooterFloorIsBelowTheWorstLineTheWireCanProduce()
    {
        const int widestPeerId = 2147483647;    // int.MaxValue: 10 digits in "PLAYER <id>"
        var worstCard = new HideSeekTally(
            RoundIndex: 99, HiderPeerId: widestPeerId, HiderGained: 255,
            SeekerPeerId: widestPeerId - 1, SeekerGained: 255, EndedByDisconnect: false,
            MatchOver: false, MatchIndex: 99, WinnerPeerId: 0,
            HiderTotal: 255, SeekerTotal: 255);
        var worstView = new HideSeekView(HideSeekPhase.Tally, 99, 0f,
            widestPeerId, widestPeerId - 1,
            new Dictionary<int, int> { [widestPeerId] = 255, [widestPeerId - 1] = 255 }
                .ToImmutableDictionary(),
            HideSeekRefusal.None, 0, 0, worstCard);

        string worst = HideSeekText.BoardFooter(worstView, null, Tuning);
        float fitted = HoldingBoardLayout.FitPixelSize(1f, worst.Length,
            HoldingBoardLayout.SmallChars);

        Assert.True(fitted > HoldingBoardLayout.MinScale,
            $"the worst footer the wire can produce is {worst.Length} chars "
            + $"({worst}), which the fit clamps at the {HoldingBoardLayout.MinScale} floor — "
            + "the floor is too high, or the copy is too long");
    }

    /// <summary>The positive control. A fit that could never clamp would pass the test above
    /// forever without proving anything about the floor.</summary>
    [Fact]
    public void TheFitCanReachItsFloor()
    {
        Assert.Equal((double)HoldingBoardLayout.MinScale,
            HoldingBoardLayout.FitPixelSize(1f, 10000, HoldingBoardLayout.SmallChars), 6);
    }

    private static string FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
