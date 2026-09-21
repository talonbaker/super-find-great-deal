using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>One line of the holding room's board</b> (HOLD-1, 2026-09-19): a human on the wire, what
/// they are about to do, and what they have scored so far.
///
/// <para><b>Why the score comes LIVE off the wire and the footer does not.</b> This row's
/// <see cref="Score"/> is the running cumulative total, which is exactly the number that must
/// change the instant a round commits. The board's FOOTER reads the frozen card
/// (<see cref="HideSeekView.LastTally"/>) instead, because a card outlives the numbers it
/// describes — the Start that begins the next match zeroes the live map while the card still
/// says who won it. Both readings are correct and they are answering different questions; see
/// <see cref="HideSeekTally.HiderTotal"/> for the same argument from the card's side.</para>
/// </summary>
/// <param name="PeerId">Whose row this is.</param>
/// <param name="Name">The display name, via <see cref="HideSeekText.PlayerName"/>.</param>
/// <param name="Role">What they are doing, already rendered: see
/// <see cref="HoldingBoardModel.RoleCell"/>.</param>
/// <param name="Score">Their cumulative score as the wire currently has it.</param>
/// <param name="IsSelf">This is the peer reading the board. The only thing it changes is the
/// pronoun in <see cref="Role"/> during Holding.</param>
public readonly record struct HoldingBoardRow(
    int PeerId,
    string Name,
    string Role,
    int Score,
    bool IsSelf);

/// <summary>
/// <b>The holding-room board's contents, with no engine in them</b> (HOLD-1, 2026-09-19).
/// Everything the wall shows is computed here from one <see cref="HideSeekView"/>, so the whole
/// readout is testable in <c>tests/unit</c> without a scene, a socket or a clock —
/// <c>HoldingBoard</c> does nothing but push these strings into <c>Label3D</c>s.
///
/// <para><b>Everything comes from the ABSOLUTE wire, which is what makes a late joiner
/// correct.</b> ROUND-1's message is a whole view rather than a delta, so the rows, the header
/// and the footer are all functions of ONE message. A peer that joins in the middle of round 3
/// paints a complete board on the first frame it has anything at all, and the smoke asserts
/// exactly that against the host's own rows rather than trusting it.</para>
/// </summary>
public static class HoldingBoardModel
{
    /// <summary>The separator, the same one every other readout in this game uses. Spelled here
    /// against <c>HideSeekText</c>'s private copy rather than shared, because these two files
    /// disagreeing would be visible on the wall the moment it happened.</summary>
    public const string Sep = " · ";

    /// <summary>
    /// <b>What a row says when its peer holds neither role</b> (SOLO-1, 2026-09-20).
    ///
    /// <para>HOLD-1 printed an em dash here, on the argument that a blank cell reads as a readout
    /// that failed and a word reads as a third role. The first half stands; the second turned out
    /// to be the wrong worry once a third player could actually stand in the room. <b>WAITING is
    /// not a third role, it is the absence of one, and it is the question the reader actually
    /// has</b> — am I in this game? — which a dash does not answer. The board's
    /// <see cref="StatusLine"/> says how they get in.</para>
    /// </summary>
    public const string Waiting = "WAITING";

    /// <summary>
    /// <b>How many rows the panel is authored to hold.</b> The world declares four
    /// <c>HoldingSpawn</c> markers and <c>Gameplay.SpawnPositionFor</c> wraps on them, so four is
    /// the number of bodies this room is built for; the game itself is two.
    ///
    /// <para><b>A fifth human is DROPPED from the board rather than shrinking it</b>, and the
    /// board says so — see <see cref="Overflow"/>. The alternative is a panel whose text size
    /// depends on how many people are in the room, which makes the one thing a board is for
    /// (glance at it, read it) a function of something the reader cannot control.</para>
    /// </summary>
    public const int MaxRows = 4;

    /// <summary>
    /// The rows, <b>ordered by peer id ascending</b>.
    ///
    /// <para><b>Ascending peer id, NOT join order, and that is a fact about the wire rather than
    /// a preference.</b> The packet asks for join order; the only roster the board is allowed to
    /// read is <see cref="HideSeekView.Scores"/> (everything else would break the late joiner's
    /// one-message guarantee), and an <see cref="ImmutableDictionary{TKey,TValue}"/> is a hash
    /// trie with no insertion order to recover. Join order is simply not on the wire. Peer id
    /// ascending is the next best thing and it has the property that actually matters here:
    /// <b>it is stable for the whole session and identical on every peer</b>, so two players
    /// looking at the same board see the same names in the same places, and a row does not jump
    /// when the roles swap. Ordering by ROLE would have moved every row every round.</para>
    /// </summary>
    public static IReadOnlyList<HoldingBoardRow> Rows(
        in HideSeekView view, int selfPeerId, Func<int, string>? nameOf)
    {
        ImmutableDictionary<int, int> scores = view.Scores ?? ImmutableDictionary<int, int>.Empty;
        if (scores.Count == 0)
            return Array.Empty<HoldingBoardRow>();

        var ids = new List<int>(scores.Count);
        foreach (KeyValuePair<int, int> row in scores)
            ids.Add(row.Key);
        ids.Sort();

        int take = Math.Min(ids.Count, MaxRows);
        var rows = new List<HoldingBoardRow>(take);
        for (int i = 0; i < take; i++)
        {
            int peerId = ids[i];
            rows.Add(new HoldingBoardRow(
                peerId,
                HideSeekText.PlayerName(nameOf, peerId),
                RoleCell(view, peerId, peerId == selfPeerId),
                scores[peerId],
                peerId == selfPeerId));
        }
        return rows;
    }

    /// <summary>How many humans the board could not show, 0 when it showed them all. The panel
    /// prints this rather than silently losing a person.</summary>
    public static int Overflow(in HideSeekView view) =>
        Math.Max((view.Scores?.Count ?? 0) - MaxRows, 0);

    /// <summary>
    /// <b>How many humans on the wire hold neither role</b> (SOLO-1). Derived from the two role
    /// ids the message has carried since ROUND-1 — <b>nothing was added to the wire for
    /// this</b>, so no <c>NetProfile.ProtocolVersion</c> bump is owed by this packet.
    ///
    /// <para>0 in every two-player session and 0 in a solo one, where the single peer holds both
    /// slots. It counts the whole roster rather than the four rows the panel can show, because
    /// "how many people are waiting" is a fact about the room and not about the furniture.</para>
    /// </summary>
    public static int WaitingCount(in HideSeekView view)
    {
        if (view.Scores is null)
            return 0;
        int waiting = 0;
        foreach (KeyValuePair<int, int> row in view.Scores)
            if (row.Key != view.HiderPeerId && row.Key != view.SeekerPeerId)
                waiting++;
        return waiting;
    }

    /// <summary>
    /// <b>The small line under the rows: who the board could not show, and who is not in this
    /// match</b> (SOLO-1 extends HOLD-1's overflow line rather than taking a label of its own).
    ///
    /// <code>
    /// 1 WAITING · NEXT FREE SEAT IS YOURS
    /// +1 MORE · 3 WAITING · NEXT FREE SEAT IS YOURS
    /// </code>
    ///
    /// <para><b>Why here and not in the footer.</b> The footer is the last CARD — a fact about a
    /// round that has finished — and it already shrinks to <see cref="HoldingBoardLayout.MinScale"/>
    /// on the longest line the wire can produce, so a second clause on it would make the result
    /// of the match unreadable to say something about the room. This line is a fact about the ROWS
    /// and sits with them; it is the same slot the overflow count already used, for the same
    /// reason.</para>
    ///
    /// <para><b>And it says how the wait ENDS.</b> "WAITING" with no exit beside it is the
    /// <c>docs/INTERACTION-BIBLE.md</c> §5 failure one level up from a button: it names the state
    /// and not the thing to do about it. For the MVP a waiting player is dealt in the moment a
    /// seat opens — <c>HideSeekLoop</c>'s Holding repair fills a vacant role from the roster on
    /// the next round — so that is what it says. Empty when everybody is playing, on the same
    /// test-the-string contract every other copy function here has.</para>
    /// </summary>
    public static string StatusLine(in HideSeekView view)
    {
        int overflow = Overflow(view);
        int waiting = WaitingCount(view);
        if (overflow == 0 && waiting == 0)
            return string.Empty;

        var parts = new List<string>(3);
        if (overflow > 0)
            parts.Add($"+{overflow} MORE");
        if (waiting > 0)
        {
            parts.Add($"{waiting} {Waiting}");
            parts.Add("NEXT FREE SEAT IS YOURS");
        }
        return string.Join(Sep, parts);
    }

    /// <summary>
    /// <b>What this peer is doing, or about to do.</b>
    ///
    /// <para><b>During Holding the cell is the NEXT round's role, announced</b>, and no
    /// separate "next" column is needed for a reason that is a property of the loop rather than
    /// a shortcut: <c>HideSeekLoop.EnterHolding</c> swaps <c>HiderPeerId</c> and
    /// <c>SeekerPeerId</c> AT the Tally -&gt; Holding commit. So while the two of them are
    /// standing at the rack, the view already names next round's hider. A "role this round"
    /// column and a "next round" column would print the same value from the same two fields and
    /// invite a reader to believe they were two facts.</para>
    ///
    /// <para><b>The pronoun is the one thing that differs per peer.</b> Each client paints its
    /// own copy of this wall, so the reader's own row can say YOU; everybody else's row says
    /// what they will do in the third person. Both players are therefore told about the swap —
    /// which is what the packet asks for — and nobody has to work out which row is theirs.</para>
    /// </summary>
    public static string RoleCell(in HideSeekView view, int peerId, bool isSelf)
    {
        bool hider = peerId != 0 && peerId == view.HiderPeerId;
        bool seeker = peerId != 0 && peerId == view.SeekerPeerId;
        if (!hider && !seeker)
            return Waiting;

        if (view.Phase != HideSeekPhase.Holding)
            return hider ? "HIDES" : "SEEKS";

        return isSelf
            ? (hider ? "NEXT: YOU HIDE" : "NEXT: YOU SEEK")
            : (hider ? "NEXT: HIDES" : "NEXT: SEEKS");
    }

    /// <summary>One row rendered for a <c>Label3D</c>: <c>ADA · NEXT: YOU HIDE · 7</c>.</summary>
    public static string RowLine(in HoldingBoardRow row) =>
        row.Name + Sep + row.Role + Sep + row.Score.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <b>The header</b>: where in the match this is, and what is happening.
    ///
    /// <code>
    /// MATCH 1 · ROUND 1 OF 2 · HOLDING
    /// MATCH 2 · ROUND 2 OF 2 · SEEKING
    /// </code>
    ///
    /// <para><b>Derived from <see cref="HideSeekView.Round"/> through the tuning, never from the
    /// card.</b> The card carries its own <c>MatchIndex</c> and is the right source for the
    /// FOOTER, which is about a round that is over; the header is about the round the session is
    /// in now, and between matches those two disagree on purpose.</para>
    /// </summary>
    public static string Header(in HideSeekView view, in HideSeekTuning tuning) =>
        $"MATCH {tuning.MatchIndexOf(view.Round)}"
        + Sep + $"ROUND {tuning.RoundWithinMatch(view.Round)} OF {tuning.MatchRoundsOrFloor}"
        + Sep + HideSeekText.PhaseName(view.Phase).ToUpperInvariant();
}

/// <summary>
/// <b>How much text the board's panel actually holds</b> (HOLD-1, 2026-09-19) — the same shape as
/// <see cref="RoundClockLayout"/>, and for the same reason: a <c>Label3D</c> will happily render
/// a line twice as wide as the board it is on, and only a render can see it.
/// </summary>
public static class HoldingBoardLayout
{
    /// <summary>
    /// The advance-per-glyph-height ratio of the project font, <b>measured off a render by
    /// CLOCK-1</b> rather than assumed: the panel spanned 275 px and an eight-character line
    /// spanned 283, so a glyph advances 0.64 of its own height on average. Restated here as a
    /// named constant instead of being multiplied into two unrelated magic numbers.
    /// </summary>
    public const float AdvancePerHeight = 0.64f;

    /// <summary>The board panel's width in metres — <c>HoldingBoard.tscn</c>'s
    /// <c>BoxMesh_panel</c>. Change one and change the other; the unit suite reads this.</summary>
    public const float PanelWidthM = 3.6f;

    /// <summary>Usable width: the panel less a margin at each end, so a line that "fits" is not
    /// touching the edge of the board.</summary>
    public const float TextWidthM = 3.3f;

    /// <summary>Authored glyph height of a ROW line, metres — <c>font_size 40</c> at Godot's
    /// default <c>pixel_size</c> of 0.005.
    ///
    /// <para><b>40 rather than 44, and the two characters it buys are the reason.</b> At 44 the
    /// panel held 23 characters and <c>ADA · NEXT: YOU HIDE · 7</c> is 24 — so the shortest
    /// plausible row in the game was already being shrunk, and every real name would have been
    /// shrunk further. At 40 the panel holds 25 and the common row fits at full size, which is
    /// what the fit is supposed to be FOR: the exception, not the default. 0.20 m of glyph still
    /// subtends about 44 px of a 1152-line frame at the 4 m the packet asks for.</para></summary>
    public const float RowGlyphM = 40f * 0.005f;

    /// <summary>Authored glyph height of the HEADER and FOOTER lines, metres —
    /// <c>font_size 32</c> at the default <c>pixel_size</c>.</summary>
    public const float SmallGlyphM = 32f * 0.005f;

    /// <summary>How many characters a row line fits at its authored size:
    /// 3.3 / (0.22 × 0.64) = 23.4.</summary>
    public static int RowChars => (int)(TextWidthM / (RowGlyphM * AdvancePerHeight));

    /// <summary>How many characters a header/footer line fits at its authored size:
    /// 3.3 / (0.16 × 0.64) = 32.2.</summary>
    public static int SmallChars => (int)(TextWidthM / (SmallGlyphM * AdvancePerHeight));

    /// <summary>
    /// Never shrink past this fraction of the authored size.
    ///
    /// <para><b>Set by the longest line the wire can produce, not by taste</b> — the same
    /// derivation <see cref="RoundClockLayout.MinScale"/> spells out. The worst footer this game
    /// can emit is a full round-tally line with both gains clamped at the byte ceiling
    /// <c>HideSeekWire.ClampByte</c> imposes and two fallback names, and
    /// <c>HoldingBoardTests.TheFooterFloorIsBelowTheWorstLineTheWireCanProduce</c> constructs it
    /// and checks it lands above this floor rather than taking anyone's word. At 0.30 the
    /// footer's glyphs are 0.048 m, which subtends about 21 px of a 1152-line frame at the 4 m
    /// the packet asks the board to be readable from.</para>
    /// </summary>
    public const float MinScale = 0.30f;

    /// <inheritdoc cref="RoundClockLayout.FitPixelSize"/>
    public static float FitPixelSize(float basePixelSize, int lineLength, int referenceChars)
    {
        if (lineLength <= referenceChars || lineLength <= 0)
            return basePixelSize;
        float scale = referenceChars / (float)lineLength;
        return basePixelSize * Math.Max(scale, MinScale);
    }
}
