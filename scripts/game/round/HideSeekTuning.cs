using System;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Every number the hide-seek loop has</b> (packet ROUND-1 §1; program
/// <c>docs/design/2026-09-19-supermarket-mvp.md</c> §2).
///
/// <para><b>A plain data record; it does not clamp itself.</b> Inherited verbatim from the loop
/// this replaces, and the reasoning is unchanged: the floor is applied where each phase's timer is
/// ARMED — the <c>EnterX</c> helpers in <see cref="HideSeekLoop"/>,
/// <c>Math.Max(t.XSec, MinTimerSec)</c> — because a struct that clamped on construction would
/// still need the same floor re-applied at every <c>with</c> expression a test or a tuning panel
/// writes. The floor belongs at the one place it is actually read.</para>
///
/// <para><b>The floor is 1 s.</b> Carried across from <c>RoundLoopTuning.MinTimerSec</c>, which
/// took it from the session loop's written contract ("every timer clamps to &gt;= 1 s and a
/// non-positive configured value clamps and logs rather than zero-firing"). A zero-length phase is
/// a phase whose transition and whose entry teleport land on the same tick, which is exactly the
/// shape that makes a room change unobservable.</para>
/// </summary>
public readonly record struct HideSeekTuning
{
    /// <summary>Every phase timer floors here.</summary>
    public const float MinTimerSec = 1f;

    /// <summary><b>A match is at least one round.</b> The same shape as
    /// <see cref="MinTimerSec"/> and for the same reason: a zero (or negative) match length makes
    /// <c>RoundIndex % MatchRounds</c> a divide-by-zero on the one line the whole match end hangs
    /// off, and a tuning panel can type a zero. Applied where it is READ
    /// (<c>HideSeekLoop</c>'s <c>MatchRoundsOf</c>), never on construction — see the class
    /// doc.</summary>
    public const int MinMatchRounds = 1;

    /// <summary>How long the hider gets in the search room. Packet default 30 s.</summary>
    public float HidingSec { get; init; }

    /// <summary>
    /// <b>The one extension, and it is not generosity.</b> If the Hiding buzzer finds the target
    /// still in the hider's hands or failing REACH-1's reachability check, the loop does NOT start
    /// Seeking on a broken hide — a seeker sent to find something inside a wall is the worst
    /// outcome this design has (program §5b). Hiding is extended by this much, ONCE, and the
    /// reason is broadcast so the hider is told what to fix rather than left wondering why the
    /// clock moved. Still broken after the grace and the round goes to Tally with the hider
    /// scoring nothing: the hide failed, and the seeker cannot be asked to find it.
    /// </summary>
    public float HidingGraceSec { get; init; }

    /// <summary>The seek cap. Packet default 180 s.</summary>
    public float SeekingSec { get; init; }

    /// <summary>How long the card holds before the reset edge. Packet default 6 s.</summary>
    public float TallySec { get; init; }

    /// <summary>
    /// <b>How many rounds make a match</b> (packet MATCH-1; Talon 2026-09-19: "just do one round
    /// of hide, one round of seek so they can both have a turn, and then see who wins"). Default
    /// <b>2</b>, which with the unconditional role swap at the reset edge is exactly "each player
    /// hides once and seeks once".
    ///
    /// <para><b>A knob, not a constant</b>, because "best of four" is the same arithmetic and the
    /// number is a design dial rather than a fact about the loop. Floored at
    /// <see cref="MinMatchRounds"/> where it is read.</para>
    /// </summary>
    public int MatchRounds { get; init; }

    /// <summary>
    /// <b>How long the card holds when the card is a MATCH result</b> — default 10 s against
    /// <see cref="TallySec"/>'s 6. A round card is one line of arithmetic both players already
    /// watched happen; a match card is the only moment the session ever says who won, and six
    /// seconds is not long enough to read a result, look at the other person and say something
    /// about it. Applied at the commit, only when the round that just ended was the last of its
    /// match.
    /// </summary>
    public float MatchTallySec { get; init; }

    /// <summary>The packet's numbers, unchanged.</summary>
    public static readonly HideSeekTuning Default = new()
    {
        HidingSec = 30f,
        HidingGraceSec = 10f,
        SeekingSec = 180f,
        TallySec = 6f,
        MatchRounds = 2,
        MatchTallySec = 10f,
    };

    /// <summary>The live tuning — the one-mutable-static idiom the rest of this codebase's tuning
    /// records use, so the loop, the driver and a future tuning panel can never read two different
    /// sessions.</summary>
    public static HideSeekTuning Current { get; set; } = Default;

    // -------------------------------------------------------------------------------------
    // The match arithmetic — ONE definition, read by the loop, the copy and the tests alike.
    //
    // It lives here rather than in HideSeekLoop because every one of these is a question about
    // the TUNING ("how long is a match, and where in one is round 7"), and because putting it
    // anywhere else would mean the loop's "is this a match end" and the strip's "ROUND 1 OF 2"
    // were two expressions that have to agree by inspection. Rounds are 1-based throughout, as
    // HideSeekState.RoundIndex is.
    // -------------------------------------------------------------------------------------

    /// <summary><see cref="MatchRounds"/> with <see cref="MinMatchRounds"/> applied. Every read
    /// below goes through this; nothing divides by the raw field.</summary>
    public int MatchRoundsOrFloor => Math.Max(MatchRounds, MinMatchRounds);

    /// <summary>Is <paramref name="roundIndex"/> the round that ENDS a match? The packet's
    /// <c>RoundIndex % MatchRounds == 0</c>, with round 0 excluded so a zeroed state cannot read
    /// as a match end.</summary>
    public bool IsLastRoundOfMatch(int roundIndex) =>
        roundIndex >= 1 && roundIndex % MatchRoundsOrFloor == 0;

    /// <summary>Is <paramref name="roundIndex"/> the round that BEGINS a match? This is the one
    /// that clears the scores — round 1, round 3 at <c>MatchRounds = 2</c>.</summary>
    public bool IsFirstRoundOfMatch(int roundIndex) =>
        roundIndex >= 1 && (roundIndex - 1) % MatchRoundsOrFloor == 0;

    /// <summary>Which match <paramref name="roundIndex"/> belongs to, 1-based.</summary>
    public int MatchIndexOf(int roundIndex) =>
        (Math.Max(roundIndex, 1) - 1) / MatchRoundsOrFloor + 1;

    /// <summary>Where in its own match <paramref name="roundIndex"/> sits, 1-based — the "1" in
    /// "ROUND 1 OF 2".</summary>
    public int RoundWithinMatch(int roundIndex) =>
        (Math.Max(roundIndex, 1) - 1) % MatchRoundsOrFloor + 1;
}
