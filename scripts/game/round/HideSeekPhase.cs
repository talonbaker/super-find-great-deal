namespace MpFoundation.Game.Round;

/// <summary>
/// Which part of one hide-and-startle round the server is in
/// (<c>docs/design/2026-09-19-supermarket-mvp.md</c> §2).
///
/// <code>
/// Holding --Start--> Hiding(30s) --Confirm/timeout--> Seeking(180s) --Found--> Together
///    ^                                                    |                       |
///    |                                                    | timeout               | End
///    +--------------- Tally(6s) &lt;-------------------------+-----------------------+
/// </code>
///
/// <para><b>There is deliberately no <c>Over</c>.</b> Carried over from the loop this replaces
/// (SESSION-2's <c>RoundPhase</c>): the session runs until the server process stops, the same two
/// players continue, and a "loss" is only ever a low tally. Tally always leads back to
/// Holding.</para>
///
/// <para><b>Why the phases are named for WHERE THE PLAYERS ARE, not for what the clock is doing.</b>
/// Every one of these is also a teleport destination pair (see <c>HideSeekDriver</c>), and a phase
/// whose name did not answer "who is in which room" would be a second fact to keep in step with the
/// first.</para>
/// </summary>
public enum HideSeekPhase : byte
{
    /// <summary>Both players in the holding room. The rack, the Start button and the board are
    /// here. Leaves on the host's Start press, and only then — there is no gather timer, because
    /// the round refuses to begin without exactly two humans and one held rack object, and a
    /// clock that started anyway would have to define what it did about either.</summary>
    Holding = 0,

    /// <summary>The hider is alone in the search room with the target; the seeker is shut in the
    /// holding room. 30 s, extendable ONCE by a grace window when the hide is not retrievable at
    /// the buzzer (see <see cref="HideSeekTuning.HidingGraceSec"/>).</summary>
    Hiding = 1,

    /// <summary>The hider is in the task room stacking for score; the seeker hunts the search
    /// room. 180 s cap.</summary>
    Seeking = 2,

    /// <summary>The object is in the drop-off bin. The seeker has been put in the vestibule behind
    /// the burst door (DOOR-1) and walks in; the hider's towers are already frozen. Ends on
    /// anybody's End press — there is no timer here on purpose, because the beat after the startle
    /// belongs to the players, not to a clock.</summary>
    Together = 3,

    /// <summary>The card. 6 s, then the world reset edge and a role swap.</summary>
    Tally = 4,
}

/// <summary>
/// <b>Why a Start / Confirm was refused.</b> Every refusal the loop can produce has a name here,
/// and that is a requirement rather than a convenience: BTN-1's button has to SAY why it did
/// nothing. A silent no-op is the defect — this foundation's one shipped in-world control failed
/// three playtests in a row and two of those failures were "it refused and never said so"
/// (<c>docs/INTERACTION-BIBLE.md</c> §2/§3, and the packet's own words: "a silent no-op is a
/// defect").
///
/// <para>Wire-encoded as a byte (see <see cref="HideSeekWire.Refusal"/>). The values are stable —
/// an added reason takes the next number, it never renumbers an existing one, because a stale peer
/// would then render a different sentence than the one the server refused with.</para>
/// </summary>
public enum HideSeekRefusal : byte
{
    /// <summary>Nothing was refused this tick. The resting value, so a default-constructed state
    /// carries no phantom refusal.</summary>
    None = 0,

    /// <summary>Start, with a human count that is not exactly two. Program §1 item 5: exactly one
    /// hider and one seeker, and the message goes on the button rather than into silence.</summary>
    NeedTwoPlayers = 1,

    /// <summary>Start, with the hider's hands empty. The round cannot begin without something to
    /// hide.</summary>
    HiderMustHoldAnObject = 2,

    /// <summary>Confirm, while the hider is still holding the target. Hiding it means putting it
    /// down.</summary>
    PutTheObjectDownFirst = 3,

    /// <summary>Confirm (or the Hiding buzzer) with the target not grab-reachable — inside a wall,
    /// behind a shelf back, or 3 m up. Program §5b layer 3; REACH-1 computes the fact, this loop
    /// only refuses on it. Buried under ten movable items is legal; that is the game.</summary>
    NobodyCouldReachThat = 4,
}
