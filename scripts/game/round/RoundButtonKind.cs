namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Which of the three round buttons this is.</b> There is exactly one instance of each in the
/// world, so the kind IS the button's identity on the wire — no separate id table to keep in step
/// with the level (packet BTN-1's "buttonId", resolved to the smallest thing that can be one).
///
/// <para><b>Ordinals ride the wire</b> (<c>RoundControls.ApplyPressResult</c>). Append only.</para>
/// </summary>
public enum RoundButtonKind : byte
{
    /// <summary>Holding room, beside the rack. Sets <see cref="IRoundFactSource.HostPressedStart"/>.</summary>
    Start = 0,

    /// <summary>Search room, by the exit wall. Sets
    /// <see cref="IRoundFactSource.HiderPressedConfirm"/>.</summary>
    Confirm = 1,

    /// <summary>Task room, beside the burst-door wall. Sets
    /// <see cref="IRoundFactSource.AnyPressedEnd"/>.</summary>
    End = 2,
}

/// <summary>
/// <b>What the lamp on a button is saying.</b> Derived on every client from the round wire; never
/// broadcast, because a lamp that is pushed rather than derived is a second copy of the round's
/// state that can be stale on exactly the peer that is looking at it.
///
/// <para><see cref="Lit"/> means "this will accept a press right now" as far as THIS PEER CAN
/// SEE, which is the honest bound: grab-reachability (REACH-1) is a server-side physics
/// measurement that never crosses the wire, so a Lit Confirm can still be refused with NOBODY
/// COULD REACH THAT. The lamp is the affordance and the refusal sentence is the answer; the bible
/// (<c>INTERACTION-BIBLE.md</c> §1 and §2) asks for both, not for one that is never wrong.</para>
/// </summary>
public enum RoundLamp : byte
{
    /// <summary>A press will be refused (or the round is not synced yet). Do not bother.</summary>
    Dark = 0,

    /// <summary>A press will be accepted, as far as this peer can tell.</summary>
    Lit = 1,

    /// <summary>The momentary flash while a press result is being played back.</summary>
    Pressed = 2,
}

/// <summary>
/// <b>Why a press was refused, in the words the presser gets.</b>
///
/// <para><b>1–4 are <see cref="HideSeekRefusal"/>'s ordinals, deliberately identical</b>, so
/// <c>(PressRefusal)(byte)roundRefusal</c> is the identity map and there is exactly one list of
/// refusal sentences in the game (<see cref="HideSeekText.RefusalSentence"/>). ROUND-1's enum is
/// not extended and not renumbered: a new value there would be a new byte on the round wire that
/// a stale peer would render as a different sentence than the server refused with, and its own
/// handoff says so.</para>
///
/// <para><b>5–7 are the button's own</b> and never reach the round at all. They are the cases
/// where the press is not a question the round can answer — wrong phase, wrong role, not close
/// enough — so the button answers them itself rather than latching a fact the loop would ignore
/// in silence. <c>HideSeekLoop</c> has no branch for "a Start pressed during Seeking": it folds
/// the fact and moves on, which is exactly the silent no-op INTERACTION-BIBLE §2 is about.</para>
///
/// <para>Ordinals ride the wire. Append only.</para>
/// </summary>
public enum PressRefusal : byte
{
    /// <summary>Accepted.</summary>
    None = 0,

    /// <inheritdoc cref="HideSeekRefusal.NeedTwoPlayers"/>
    NeedTwoPlayers = 1,

    /// <inheritdoc cref="HideSeekRefusal.HiderMustHoldAnObject"/>
    HiderMustHoldAnObject = 2,

    /// <inheritdoc cref="HideSeekRefusal.PutTheObjectDownFirst"/>
    PutTheObjectDownFirst = 3,

    /// <inheritdoc cref="HideSeekRefusal.NobodyCouldReachThat"/>
    NobodyCouldReachThat = 4,

    /// <summary>The round is not in the phase this button belongs to.</summary>
    NotNow = 5,

    /// <summary>This button is the other player's (Confirm is the hider's).</summary>
    NotYourButton = 6,

    /// <summary>The server's own reach re-check: the presser's authoritative body is further from
    /// the button than <c>RoundButton.PressRadiusM</c> plus the latency tolerance.</summary>
    TooFarAway = 7,

    /// <summary>
    /// <b>This player is in the room but not in this match</b> (SOLO-1, 2026-09-20; Talon: "with
    /// two or more players, they can wait in the room"). A third or later joiner holds neither
    /// role, so none of the three buttons is theirs to press.
    ///
    /// <para><b>It is the BUTTON's, and it is NOT mirrored into
    /// <see cref="HideSeekRefusal"/></b> — which is the one thing about it worth a paragraph.
    /// <c>HideSeekLoop</c> never learns who pressed (its <c>HostPressedStart</c> is an OR across
    /// every fact source, deliberately), so the round could not produce this refusal even if it
    /// had a name for it; and 5-7 above are already the button's own range, so a round refusal
    /// numbered into it would be rendered by <see cref="HideSeekText.RefusalSentence"/> as one of
    /// these sentences on any peer that read it off the wire. <c>SoloRoundTests</c> fences both
    /// halves of that.</para>
    /// </summary>
    NotInThisMatch = 8,
}
