namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Every word the three buttons say</b>, engine-free and unit-tested, for the reason
/// <see cref="HideSeekText"/> exists one file over: a sentence a player reads under pressure is
/// worth a test, and a sentence assembled inside a <c>_Ready</c> is worth none.
///
/// <para><b>The refusal sentences say what to DO.</b> <c>docs/INTERACTION-BIBLE.md</c> §5, out of
/// the three playtests the one shipped in-world control failed: a warning that does not name the
/// fix reads as the game being broken.</para>
///
/// <para><b>The face labels never contain a key.</b> The key glyph is whatever the live
/// <c>InputMap</c> binds to <c>interact</c>, drawn over the button by <c>InteractPrompt</c> when
/// it is the pick target. Writing "PRESS E" on a mesh is the second of the three failures BTN-1
/// exists to remove — a prompt that never said which key, in a build where the key is
/// rebindable.</para>
/// </summary>
public static class RoundButtonText
{
    /// <summary>What is written on the button's face. Verbatim from the packet.</summary>
    public static string Label(RoundButtonKind kind) => kind switch
    {
        RoundButtonKind.Start => "START",
        RoundButtonKind.Confirm => "I'M DONE HIDING",
        RoundButtonKind.End => "END ROUND",
        _ => "?",
    };

    /// <summary>
    /// The sentence the presser is shown. Empty for <see cref="PressRefusal.None"/>, so a caller
    /// can test the string and a widget can hide on empty — the same contract
    /// <see cref="HideSeekText.RefusalSentence"/> has.
    ///
    /// <para><b>The round's own four reasons are forwarded to
    /// <see cref="HideSeekText.RefusalSentence"/>, never re-spelled here.</b> Two copies of
    /// "PUT THE OBJECT DOWN FIRST" is two copies that can drift, and the HUD strip already draws
    /// the wire's version of the same refusal in the same two seconds.</para>
    ///
    /// <para><paramref name="kind"/> is taken because <see cref="PressRefusal.NotNow"/> means
    /// three different things: a Start pressed mid-round, a Confirm pressed outside the hide, an
    /// End pressed before anybody has been found. One sentence for all three would be the
    /// "invalid state" non-answer the bible names.</para>
    /// </summary>
    public static string Sentence(PressRefusal refusal, RoundButtonKind kind) => refusal switch
    {
        PressRefusal.None => string.Empty,

        PressRefusal.NeedTwoPlayers or PressRefusal.HiderMustHoldAnObject
            or PressRefusal.PutTheObjectDownFirst or PressRefusal.NobodyCouldReachThat
            => HideSeekText.RefusalSentence((HideSeekRefusal)(byte)refusal),

        PressRefusal.NotNow => kind switch
        {
            RoundButtonKind.Start => "WAIT — THIS ROUND IS STILL RUNNING",
            RoundButtonKind.Confirm => "HIDE THE OBJECT FIRST",
            RoundButtonKind.End => "FINISH THE ROUND FIRST",
            _ => "NOT RIGHT NOW",
        },

        PressRefusal.NotYourButton => "ONLY THE HIDER CAN PRESS THIS",
        PressRefusal.TooFarAway => "STEP CLOSER TO THE BUTTON",

        // SOLO-1. One sentence for all three buttons, unlike NotNow above, because the fact is
        // the same whichever one they walked up to: they are not in this match. It says the exit
        // as well as the state -- the wait ends at the next free seat -- which is the half
        // INTERACTION-BIBLE Sec.5 is about and the half "NOT IN THIS MATCH" alone would miss.
        PressRefusal.NotInThisMatch => "YOU'RE UP NEXT ROUND — WAIT HERE",
        _ => string.Empty,
    };
}
