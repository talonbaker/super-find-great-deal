using System;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Every word the round says to a player</b>, in one engine-free place, so the copy is unit
/// testable and the HUD is only a renderer. The precedent is <c>PhaseToastText</c> and
/// <c>HudClock.DayPhaseText</c>, both of which are tested in <c>tests/unit</c> for the same
/// reason: a sentence a player reads under pressure is worth a test, and a sentence built inside a
/// <c>_Ready</c> is worth none.
///
/// <para><b>The refusal sentences say what to DO, not what went wrong.</b>
/// <c>docs/INTERACTION-BIBLE.md</c> §5, out of the three playtests the one shipped in-world
/// control failed: a warning that does not name the fix reads as the game being broken. "Take
/// something off the rack first" is actionable; "invalid state" is not.</para>
/// </summary>
public static class HideSeekText
{
    /// <summary>The phase, as the strip says it. Upper case because the whole strip is, and short
    /// because it sits beside a clock and a role.</summary>
    public static string PhaseName(HideSeekPhase phase) => phase switch
    {
        HideSeekPhase.Holding => "HOLDING",
        HideSeekPhase.Hiding => "HIDING",
        HideSeekPhase.Seeking => "SEEKING",
        HideSeekPhase.Together => "FOUND",
        HideSeekPhase.Tally => "TALLY",
        _ => "—",
    };

    /// <summary>
    /// <c>mm:ss</c>, floored, never negative, and never wider than it has to be below an hour.
    ///
    /// <para><b>Floored, not rounded</b>: a clock that reads 0:30 for the first half second of a
    /// 30 s phase and then 0:30 again is fine, but one that rounds 29.6 up to 0:30 tells the
    /// player they have time that has already gone. Every countdown in a game rounds DOWN.</para>
    /// </summary>
    public static string TimerText(float remainingSec)
    {
        if (float.IsNaN(remainingSec) || remainingSec < 0f)
            remainingSec = 0f;
        int whole = (int)MathF.Floor(remainingSec);
        int minutes = whole / 60;
        int seconds = whole % 60;
        return $"{minutes}:{seconds:00}";
    }

    /// <summary>The sentence a refusal shows. Empty for <see cref="HideSeekRefusal.None"/>, so a
    /// caller can test the string rather than the enum and a widget can hide on empty.</summary>
    public static string RefusalSentence(HideSeekRefusal refusal) => refusal switch
    {
        HideSeekRefusal.NeedTwoPlayers => "TWO PLAYERS ARE NEEDED TO START",
        HideSeekRefusal.HiderMustHoldAnObject => "TAKE SOMETHING OFF THE RACK FIRST",
        HideSeekRefusal.PutTheObjectDownFirst => "PUT THE OBJECT DOWN FIRST",
        HideSeekRefusal.NobodyCouldReachThat => "NOBODY COULD REACH THAT — MOVE IT",
        _ => string.Empty,
    };

    /// <summary>
    /// <b>The intercom lamp's line</b> (VOICE-1): who is talking to you through the PA right now.
    /// Called only when somebody IS — "is anyone on the intercom" is a peer id the caller already
    /// holds (<c>VoiceManager.PaSpeakerNow</c>), not a string this function would have to make
    /// empty to express.
    ///
    /// <para><b>It is a redundant channel, not decoration</b> — <c>INTERACTION-BIBLE.md</c> §8.2.
    /// A cross-room voice is deliberately filtered and quiet (overdrive, a 2.2 kHz lowpass and a
    /// boxy reverb), and the one thing a hider must never be unsure of is whether the seeker is
    /// talking at all, because what they SAY is a bluff and the bluff is the mechanic. An
    /// audio-only cue for that is the defect §8.2 is about.</para>
    ///
    /// <para>A peer whose display name has not replicated yet gets the word SOMEONE rather than
    /// an empty gap: "the intercom is live and I cannot tell you who" is the true statement, and
    /// it is still the half of the message that matters.</para>
    /// </summary>
    public static string IntercomLine(string speakerName)
    {
        string name = speakerName?.Trim() ?? string.Empty;
        return name.Length == 0 ? "INTERCOM: SOMEONE" : $"INTERCOM: {name.ToUpperInvariant()}";
    }

    /// <summary>The strip's whole line, in one function, so the HUD never assembles copy of its
    /// own. <paramref name="role"/> is dropped when empty rather than leaving a stray separator —
    /// a spectator gets "SEEKING · 2:41 · ROUND 2", not "SEEKING · 2:41 ·  · ROUND 2".</summary>
    public static string StripLine(HideSeekPhase phase, float remainingSec, string role, int round)
    {
        string clock = phase is HideSeekPhase.Holding or HideSeekPhase.Together
            ? string.Empty   // neither phase has a clock; a frozen 0:00 would read as expired.
            : TimerText(remainingSec);

        string line = PhaseName(phase);
        if (clock.Length > 0)
            line += " · " + clock;
        if (role.Length > 0)
            line += " · " + role;
        return line + $" · ROUND {Math.Max(round, 1)}";
    }

    // ===========================================================================================
    // MATCH-1 — the result copy.
    //
    // A match is two rounds (HideSeekTuning.MatchRounds): each player hides once and seeks once,
    // and then the game says who won. These four functions are every word that says so, and they
    // are here rather than in the widget for the reason the class doc gives — HOLD-1's board and
    // CLOCK-1's clock render the SAME sentences, and a sentence that exists twice is a sentence
    // that will disagree with itself the first time one of them is edited.
    //
    // THEY ALL READ THE CARD, NEVER THE LIVE STATE. HideSeekTally freezes its own round index,
    // its own totals and its own winner precisely so a result can outlive the numbers it
    // describes: the Start that begins the next match zeroes Scores while this card is still the
    // last thing anybody saw.
    //
    // The separator, the name fallback and the score dash are each defined once below, because
    // three functions building "X · Y – Z" by hand is three chances to build it differently.
    // ===========================================================================================

    /// <summary>The strip's separator. One definition; every line below is assembled from it.</summary>
    private const string Sep = " · ";

    /// <summary>The dash between two totals — an EN dash (–), not a hyphen and not the em dash
    /// the refusal copy uses. "7–5" is a score pair; "7-5" reads as an id and "7—5" is a
    /// parenthesis.</summary>
    private const string ScoreDash = "–";

    /// <summary>
    /// A peer's name for the copy, or an honest fallback.
    ///
    /// <para><paramref name="nameOf"/> is a resolver rather than a name because every line here
    /// is about a CARD, and a card names two peers whose roles have usually already swapped by
    /// the time it is read. <c>SessionSummaryProvider.ResolveDisplayName</c> is the one in the
    /// game; it answers empty for a peer whose avatar has despawned, which is exactly the case
    /// this fallback is for. Null resolver is legal — every caller in a test has one.</para>
    /// </summary>
    public static string PlayerName(Func<int, string>? nameOf, int peerId)
    {
        if (peerId == 0)
            return "NOBODY";
        string resolved = nameOf?.Invoke(peerId) ?? string.Empty;
        return string.IsNullOrWhiteSpace(resolved) ? $"PLAYER {peerId}" : resolved;
    }

    /// <summary>The two totals, winner first. Order is by VALUE, not by role, so "WINS 7–5" can
    /// never print the loser's number first.</summary>
    private static string TotalsHighFirst(in HideSeekTally card)
    {
        int high = Math.Max(card.HiderTotal, card.SeekerTotal);
        int low = Math.Min(card.HiderTotal, card.SeekerTotal);
        return $"{high}{ScoreDash}{low}";
    }

    /// <summary>What a round that ended on a disconnect says, with no result in it. It replaces
    /// the result and keeps the locator ("ROUND 1 OF 2 · ended: …"), because a player who looks
    /// up mid-sentence still needs to know where in the match they are.
    ///
    /// <para><paramref name="leaverPeerId"/> is 0 when nobody knows who left — the card records
    /// that the round ended this way, not which of the two it was. <see cref="MatchLine"/>
    /// recovers it from the score map (the leaver is the card's role holder who no longer has a
    /// row); a caller that already knows can just say so.</para></summary>
    private static string EndedLine(Func<int, string>? nameOf, int leaverPeerId) =>
        leaverPeerId != 0
            ? $"ended: {PlayerName(nameOf, leaverPeerId)} left"
            : "ended: a player left";

    /// <summary>
    /// <b>The Tally line for a round that is NOT the last of its match</b> — what the two of them
    /// just did, in the order they did it.
    ///
    /// <code>
    /// ROUND 1 OF 2 · ADA hid · 3 sorted · found at 1:12 · BEN +48
    /// ROUND 1 OF 2 · ADA hid · 3 sorted · not found · BEN +0
    /// ROUND 1 OF 2 · ended: BEN left
    /// </code>
    ///
    /// <para><b>"found at" is the seek's ELAPSED time</b>, reconstructed as
    /// <c>SeekingSec − SeekerGained</c> — the seeker's gain is the clock they had LEFT, and "you
    /// found it with 48 seconds to spare" and "you found it at 1:12" are the same fact told from
    /// the two ends. A seek that timed out gains nothing and is reported as <c>not found</c>
    /// rather than as a find at the full duration, which is what the naive arithmetic would print
    /// and would be a lie about the one instant this game is built around.</para>
    /// </summary>
    public static string RoundTallyLine(in HideSeekTally card, Func<int, string>? nameOf,
        in HideSeekTuning tuning, int leaverPeerId = 0)
    {
        string where = $"ROUND {tuning.RoundWithinMatch(card.RoundIndex)} OF "
                       + $"{tuning.MatchRoundsOrFloor}";
        if (card.EndedByDisconnect)
            return where + Sep + EndedLine(nameOf, leaverPeerId);

        string found = card.SeekerGained > 0
            ? "found at " + TimerText(Math.Max(tuning.SeekingSec - card.SeekerGained, 0f))
            : "not found";

        return where
               + Sep + $"{PlayerName(nameOf, card.HiderPeerId)} hid"
               + Sep + $"{card.HiderGained} sorted"
               + Sep + found
               + Sep + $"{PlayerName(nameOf, card.SeekerPeerId)} +{card.SeekerGained}";
    }

    /// <summary>
    /// <b>The Tally line for the round that ENDS a match</b> — the only moment the session says
    /// who won.
    ///
    /// <code>
    /// MATCH 1 · ADA WINS 7–5
    /// MATCH 1 · DRAW 6–6
    /// MATCH 1 · ended: BEN left
    /// </code>
    ///
    /// <para>A draw is <see cref="HideSeekTally.WinnerPeerId"/> 0 and prints both totals rather
    /// than a name, because "DRAW 6–6" is a result and "DRAW" alone is a shrug.</para>
    /// </summary>
    public static string MatchTallyLine(in HideSeekTally card, Func<int, string>? nameOf,
        int leaverPeerId = 0)
    {
        string where = $"MATCH {Math.Max(card.MatchIndex, 1)}";
        if (card.EndedByDisconnect)
            return where + Sep + EndedLine(nameOf, leaverPeerId);
        return where + Sep + (card.WinnerPeerId == 0
            ? $"DRAW {TotalsHighFirst(card)}"
            : $"{PlayerName(nameOf, card.WinnerPeerId)} WINS {TotalsHighFirst(card)}");
    }

    /// <summary>
    /// <b>The Holding line after a match</b>: the result still standing, and the one thing to do
    /// about it.
    ///
    /// <code>
    /// MATCH 1 · ADA WON 7–5 · START FOR MATCH 2
    /// MATCH 1 · DRAW 6–6 · START FOR MATCH 2
    /// </code>
    ///
    /// <para><b>A disconnect does not suppress the result here</b>, unlike the Tally line above.
    /// The match is over with the totals as they stand (packet MATCH-1) and this is the line the
    /// survivor reads while deciding whether to go again — "ended: someone left" tells them
    /// nothing they do not already know and hides the score that is still on the board.</para>
    ///
    /// <para><b>There is no menu.</b> The same Start button that ran the last round runs the next
    /// match, so the sentence has to carry that: the player is told which match they are about to
    /// begin, not merely that one finished.</para>
    /// </summary>
    public static string MatchHoldingLine(in HideSeekTally card, Func<int, string>? nameOf)
    {
        int match = Math.Max(card.MatchIndex, 1);
        string result = card.WinnerPeerId == 0
            ? $"DRAW {TotalsHighFirst(card)}"
            : $"{PlayerName(nameOf, card.WinnerPeerId)} WON {TotalsHighFirst(card)}";
        return $"MATCH {match}" + Sep + result + Sep + $"START FOR MATCH {match + 1}";
    }

    /// <summary>
    /// <b>The one entry point every renderer should call</b> (packet MATCH-1: "a
    /// <c>MatchLine(view)</c> beside <c>StripLine</c> for the clock and the board to reuse").
    /// Returns the match-aware sentence for this view, or <b>empty when there is nothing
    /// match-shaped to say</b> — so a caller tests the string rather than re-deriving the phase
    /// rules that picked it.
    ///
    /// <list type="bullet">
    /// <item><b>Tally</b> — the card, either <see cref="RoundTallyLine"/> or
    /// <see cref="MatchTallyLine"/>.</item>
    /// <item><b>Holding, after a match</b> — <see cref="MatchHoldingLine"/>. After an ordinary
    /// round it is empty: the strip's usual phase line is the right thing there.</item>
    /// <item><b>Anywhere else</b> — empty.</item>
    /// </list>
    ///
    /// <para><b>Who left, recovered rather than guessed.</b> The card knows a role holder went
    /// but not which one. <see cref="HideSeekView.Scores"/> carries a row for every peer on the
    /// live roster and none for a peer who is gone, so the leaver is the card's hider or seeker
    /// with no row — derived only when exactly one of the two is missing, because two missing
    /// rows name nobody.</para>
    /// </summary>
    public static string MatchLine(in HideSeekView view, Func<int, string>? nameOf,
        in HideSeekTuning tuning)
    {
        if (view.LastTally is not { } card)
            return string.Empty;

        return view.Phase switch
        {
            HideSeekPhase.Tally => card.MatchOver
                ? MatchTallyLine(card, nameOf, LeaverOf(view, card))
                : RoundTallyLine(card, nameOf, tuning, LeaverOf(view, card)),
            HideSeekPhase.Holding when card.MatchOver => MatchHoldingLine(card, nameOf),
            _ => string.Empty,
        };
    }

    /// <inheritdoc cref="MatchLine"/>
    private static int LeaverOf(in HideSeekView view, in HideSeekTally card)
    {
        if (!card.EndedByDisconnect || view.Scores is null)
            return 0;
        bool hiderHere = card.HiderPeerId != 0 && view.Scores.ContainsKey(card.HiderPeerId);
        bool seekerHere = card.SeekerPeerId != 0 && view.Scores.ContainsKey(card.SeekerPeerId);
        if (hiderHere && !seekerHere)
            return card.SeekerPeerId;
        if (seekerHere && !hiderHere)
            return card.HiderPeerId;
        return 0;
    }

    /// <summary>
    /// <b>The whole strip line for a view</b>, match-aware: <see cref="MatchLine"/> when it has
    /// something to say, and the ordinary phase/clock/role/round line otherwise.
    ///
    /// <para>This overload exists so the WIDGET does not choose between two sentences. Packet
    /// MATCH-1 is explicit that the change belongs in the text function rather than in the
    /// renderer, and the reason is that CLOCK-1 and HOLD-1 need the identical choice made the
    /// identical way — a renderer that decided for itself would be the third place the rule
    /// lives.</para>
    /// </summary>
    public static string StripLine(in HideSeekView view, int selfPeerId,
        Func<int, string>? nameOf, in HideSeekTuning tuning)
    {
        string match = MatchLine(view, nameOf, tuning);
        return match.Length > 0
            ? match
            : StripLine(view.Phase, view.RemainingSec, view.RoleTextFor(selfPeerId), view.Round);
    }
}
