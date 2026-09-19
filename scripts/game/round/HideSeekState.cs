using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The card</b>, computed exactly once at the commit into <see cref="HideSeekPhase.Tally"/> and
/// then stored — every subsequent tick in Tally reads this back rather than recomputing it.
///
/// <para><b>It carries its own <see cref="RoundIndex"/>.</b> The loop's
/// <see cref="HideSeekState.RoundIndex"/> advances AT this commit (packet ROUND-1 §1, "RoundIndex
/// increments at the commit"), so while the card is on screen the session is already counting the
/// next round. A card that read the live index would label round 1's result "ROUND 2". The number
/// the card is ABOUT lives on the card.</para>
/// </summary>
/// <param name="RoundIndex">The round this card is the result of.</param>
/// <param name="HiderPeerId">Who hid. 0 when the round had no hider (a disconnect before roles).</param>
/// <param name="HiderGained">Towers frozen at the find — the cost of being found late is the tower
/// you did not finish. 0 on a hide that was never retrievable.</param>
/// <param name="SeekerPeerId">Who sought.</param>
/// <param name="SeekerGained">Whole seconds left on the seek clock at the find. 0 on a timeout.</param>
/// <param name="EndedByDisconnect">A role holder left mid-round. Both gains are 0 and the other
/// player's cumulative score is untouched; the card says so rather than reporting a 0-0 round that
/// looks like two people who tried.</param>
/// <param name="MatchOver">This round was the LAST of its match (MATCH-1): the session has a
/// winner to announce, the Tally phase runs long
/// (<see cref="HideSeekTuning.MatchTallySec"/>), and the next Start begins a new match with the
/// scores back at zero.</param>
/// <param name="MatchIndex">Which match this round belonged to, 1-based. Set on EVERY card, not
/// only the last of a match, because "MATCH 2 · ROUND 1 OF 2" is a thing the board wants to say
/// in the middle of a match too. The packet defines it as <c>RoundIndex / MatchRounds</c>, which
/// is what <c>(RoundIndex - 1) / MatchRounds + 1</c> evaluates to on the last round of a match;
/// off that round the packet's form gives 0 and this one keeps counting.</param>
/// <param name="WinnerPeerId">Who won the MATCH, <b>0 for a draw and 0 on any card that is not a
/// match end</b>. Decided from the two totals below, so a winner can never disagree with the
/// numbers printed beside it.</param>
/// <param name="HiderTotal">This round's hider's CUMULATIVE score as it stands after this
/// commit.</param>
/// <param name="SeekerTotal">This round's seeker's cumulative score after this commit.
///
/// <para><b>Why the totals are frozen onto the card instead of read live off
/// <c>Scores</c>.</b> Exactly the argument <see cref="RoundIndex"/> is already here for. The card
/// outlives the numbers it describes: the next Start of a new match zeroes <c>Scores</c> while
/// this card is still the last one anyone saw, so a board rendering "X WON 7–5" off the live map
/// would quietly become "X WON 0–0" the moment the next round began. A frozen result reads its
/// own values.</para></param>
public readonly record struct HideSeekTally(
    int RoundIndex,
    int HiderPeerId,
    int HiderGained,
    int SeekerPeerId,
    int SeekerGained,
    bool EndedByDisconnect,
    bool MatchOver = false,
    int MatchIndex = 0,
    int WinnerPeerId = 0,
    int HiderTotal = 0,
    int SeekerTotal = 0);

/// <summary>
/// <b>The whole round, as one value.</b> A <c>readonly record struct</c> with no engine types, no
/// clock of its own, no randomness and exactly one mutator
/// (<see cref="HideSeekLoop.Step"/>) — the shape inherited from the loop this replaces.
/// </summary>
public readonly record struct HideSeekState
{
    /// <summary>Where in the round we are.</summary>
    public HideSeekPhase Phase { get; init; }

    /// <summary>1-based. Round 1 is the session's first; there is no cap. Advances at the commit
    /// into Tally — see <see cref="HideSeekTally.RoundIndex"/> for why the card carries its own.</summary>
    public int RoundIndex { get; init; }

    /// <summary>Counting down in the current phase; 0 in <see cref="HideSeekPhase.Holding"/> and
    /// <see cref="HideSeekPhase.Together"/>, which have no clock. One field for every phase (not
    /// one timer each) because only one is ever live and the wire's "remaining tenths" field is
    /// phase-agnostic by the same logic.</summary>
    public float RemainingSec { get; init; }

    /// <summary>Monotonic tick counter, incremented once per <see cref="HideSeekLoop.Step"/>.
    /// Exists so <see cref="FoundTick"/> is a real, comparable instant rather than a wall clock the
    /// loop is not allowed to read.</summary>
    public long Tick { get; init; }

    /// <summary>Who is hiding this round. 0 = nobody assigned yet (fewer than two humans).</summary>
    public int HiderPeerId { get; init; }

    /// <summary>Who is seeking this round. 0 = nobody assigned yet.</summary>
    public int SeekerPeerId { get; init; }

    /// <summary>Cumulative score per peer across the whole session. Absolute ints, so the wire
    /// carries what IS rather than what changed.</summary>
    public ImmutableDictionary<int, int> Scores { get; init; }

    /// <summary>Towers the hider has completed so far THIS round — the live value, folded from
    /// the facts every tick and reset at the start of each round.</summary>
    public int TowersCompleted { get; init; }

    /// <summary>Towers frozen at the Found tick (or at the Seeking buzzer). The hider's score for
    /// the round: progress stops the instant the door bursts.</summary>
    public int TowersAtFound { get; init; }

    /// <summary>Whole seconds left on the seek clock at the find; 0 on a timeout. The seeker's
    /// score for the round, frozen at the same instant for the same reason.</summary>
    public int RemainingAtFoundSec { get; init; }

    /// <summary>The tick the target landed in the bin — the startle instant every other lane keys
    /// off (DOOR-1's burst, TASK-1's freeze). <c>null</c> until it happens, and cleared at the
    /// start of each round; there is no 0 sentinel because tick 0 is a real tick.</summary>
    public long? FoundTick { get; init; }

    /// <summary><see cref="HideSeekTuning.HidingGraceSec"/> has already been spent this round.
    /// Once, not once per buzzer.</summary>
    public bool HidingExtended { get; init; }

    /// <summary>
    /// <b>True for exactly one tick: the Tally -&gt; Holding commit.</b> The one idempotent signal
    /// the server driver watches to perform the world reset (props home, hands empty, resume
    /// tickets into the old world torn up, everyone teleported back to the holding room). Cleared
    /// at the top of the very next <see cref="HideSeekLoop.Step"/>, so a driver stepping once per
    /// tick observes it true for one and only one tick no matter how long that tick's <c>dt</c>
    /// was.
    /// </summary>
    public bool ResetRequested { get; init; }

    /// <summary>
    /// <b>Why something was refused, true for exactly one tick</b>, on the same one-shot contract
    /// as <see cref="ResetRequested"/>. <see cref="HideSeekRefusal.None"/> on every other tick.
    /// The driver broadcasts it; BTN-1 shakes the button and the HUD strip says the sentence.
    ///
    /// <para>It is a one-tick edge and not a sticky field because a refusal is an ANSWER TO A
    /// PRESS. A sticky reason would still be on screen two minutes later, describing a question
    /// nobody remembers asking.</para>
    /// </summary>
    public HideSeekRefusal Refusal { get; init; }

    /// <summary>What the last card said. <c>null</c> before the first round ever finishes.</summary>
    public HideSeekTally? LastTally { get; init; }

    /// <summary>This peer's cumulative score, or 0. Convenience for the HUD and the board.</summary>
    public int ScoreOf(int peerId) =>
        Scores is not null && Scores.TryGetValue(peerId, out int v) ? v : 0;
}
