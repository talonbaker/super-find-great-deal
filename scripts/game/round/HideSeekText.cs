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

        return ChooseMatchSentence(view) switch
        {
            MatchSentence.MatchTally => MatchTallyLine(card, nameOf, LeaverOf(view, card)),
            MatchSentence.RoundTally => RoundTallyLine(card, nameOf, tuning, LeaverOf(view, card)),
            MatchSentence.MatchHolding => MatchHoldingLine(card, nameOf),
            _ => string.Empty,
        };
    }

    /// <summary><b>Which KIND of match-shaped sentence this view calls for</b>, or
    /// <see cref="MatchSentence.None"/>. Extracted from <see cref="MatchLine"/>'s body at INT-0B
    /// (2026-09-19) and called by BOTH it and the clock's own
    /// <see cref="ClockLine(in HideSeekView, Func{int, string}, in HideSeekTuning)"/>, so the two
    /// renderers cannot come to different conclusions about whether a MATCH just ended. The
    /// phase rules live here once; each renderer chooses only how much room it has to say it
    /// in.</summary>
    private enum MatchSentence { None, RoundTally, MatchTally, MatchHolding }

    /// <inheritdoc cref="MatchSentence"/>
    private static MatchSentence ChooseMatchSentence(in HideSeekView view) =>
        view.LastTally is not { } card
            ? MatchSentence.None
            : view.Phase switch
            {
                HideSeekPhase.Tally => card.MatchOver
                    ? MatchSentence.MatchTally
                    : MatchSentence.RoundTally,
                HideSeekPhase.Holding when card.MatchOver => MatchSentence.MatchHolding,
                _ => MatchSentence.None,
            };

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

    /// <summary>
    /// <b>The second line of a <c>RoundClock</c> on the wall</b> (CLOCK-1, 2026-09-19). The first
    /// line is <see cref="PhaseName"/>; this is what sits under it.
    ///
    /// <para><b>It is not <see cref="StripLine"/> with the role removed, and it must not become
    /// that.</b> The strip is one line read from two metres away by the person it belongs to; this
    /// is read across a room from eight metres by both players at once, so it carries one value
    /// and never a sentence. Everything else the round has to say is already on the strip.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Holding</b> — empty. The phase has no clock, and a frozen <c>0:00</c> on a wall
    /// reads as expired rather than as not-started; the same call <see cref="StripLine"/> makes
    /// for the same reason.</item>
    /// <item><b>Hiding / Seeking</b> — <see cref="TimerText"/>, the identical floored value the
    /// strip shows, from the identical replicated field. Two readouts of one number: that is the
    /// whole point of the clock existing, and reimplementing the format here is how they would
    /// come to disagree by a second.</item>
    /// <item><b>Together</b> — what the hider got done before the door. <b>The live
    /// <c>TowersCompleted</c> off the wire</b>, which is the only count that is on the wire at
    /// all; the frozen <c>TowersAtFound</c> is server bookkeeping. The word is SORTED because the
    /// task room sorts objects into bins (Talon, 2026-09-19); the FIELD keeps its name until
    /// TASK-1 renames it, and this line is copy, not a rename.</item>
    /// <item><b>Tally</b> — the card, as two numbers. <c>HID</c> is the hider's sorts and
    /// <c>SEEK</c> is the seeker's seconds; they are not comparable and the card does not pretend
    /// they are (proposal §3.3). A card that ended on a disconnect says so in words instead,
    /// because two zeroes look like two people who tried.</item>
    /// </list>
    ///
    /// <para><b>Every arm is short on purpose.</b> <see cref="RoundClockLayout.FitPixelSize"/>
    /// shrinks the label to keep a long line inside the panel, so a wordy arm here does not
    /// overflow the clock — it makes the clock unreadable at eight metres, which is worse.</para>
    ///
    /// <para><b>This overload knows nothing about matches; the one below it does.</b> Prefer
    /// <see cref="ClockLine(in HideSeekView, Func{int, string}, in HideSeekTuning)"/> in the
    /// game — it takes the KIND of sentence from MATCH-1's chooser, so the wall and the strip
    /// cannot disagree about whether a match ended (INT-0B, 2026-09-19).</para>
    /// </summary>
    public static string ClockLine(HideSeekPhase phase, float remainingSec, int towersCompleted,
        HideSeekTally? tally) => phase switch
    {
        HideSeekPhase.Holding => string.Empty,
        HideSeekPhase.Hiding or HideSeekPhase.Seeking => TimerText(remainingSec),
        HideSeekPhase.Together => $"{Math.Max(towersCompleted, 0)} SORTED",
        HideSeekPhase.Tally => tally is not { } card
            ? string.Empty
            : card.EndedByDisconnect
                ? "ENDED EARLY"
                : $"HID {Math.Max(card.HiderGained, 0)} · SEEK {Math.Max(card.SeekerGained, 0)}",
        _ => string.Empty,
    };

    /// <summary>
    /// <b>The clock's second line, match-aware</b> (INT-0B, 2026-09-19 — packet ruling 3). This
    /// is the overload the game calls.
    ///
    /// <para><b>The KIND of sentence comes from MATCH-1's chooser</b>
    /// (<c>ChooseMatchSentence</c>, the same <c>card.MatchOver</c> branch
    /// <see cref="MatchLine"/> takes), so the wall and the strip cannot disagree about whether a
    /// MATCH just ended. Before this existed they could and did: at a match end the strip read
    /// <c>MATCH 1 · BEN WINS 173–171</c> while the wall still read <c>HID 1 · SEEK 168</c>, and
    /// in the holding room afterwards the strip carried the result and the wall was blank.</para>
    ///
    /// <para><b>The WORDS are still the clock's own, and the rule above it stands: this carries
    /// one value and never a sentence.</b> It does not route through
    /// <see cref="StripLine(in HideSeekView, int, Func{int, string}, in HideSeekTuning)"/> and
    /// must not. MATCH-1's tally sentence is 57 characters
    /// (<c>ROUND 1 OF 2 · ADA hid · 3 sorted · found at 1:12 · BEN +48</c>) against a panel that
    /// holds <see cref="RoundClockLayout.ReferenceChars"/> = 7 at full size and refuses to shrink
    /// past <see cref="RoundClockLayout.MinScale"/> = 0.35 — so it renders at 2.85x the panel's
    /// width, and lowering the floor to fit puts the glyphs at about five pixels of a
    /// 1152-line frame at eight metres. The chooser is shared; the copy is not.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Tally, match over</b> — <c>BEN WINS</c>, or <c>DRAW</c> when there is no winner.
    /// No totals: the two numbers are on the strip, and what the wall is for at that instant is
    /// the one word both players look up for.</item>
    /// <item><b>Tally, mid-match</b> — today's <c>HID 3 · SEEK 168</c>, unchanged.</item>
    /// <item><b>Holding after a match</b> — <c>BEN WON</c> / <c>DRAW</c>, the result still
    /// standing while somebody decides to press Start. <b>A disconnect does not suppress it</b>,
    /// which is MATCH-1's own rule for this phase: the match is over with the totals as they
    /// stand, and hiding them tells the survivor nothing they do not know.</item>
    /// <item><b>Holding otherwise</b> — blank, as before.</item>
    /// <item><b>Ended on a disconnect (either Tally arm)</b> — <c>BEN LEFT</c> when the leaver
    /// can be recovered from the score map the way <see cref="MatchLine"/> recovers it, and
    /// CLOCK-1's <c>ENDED EARLY</c> when it cannot. Two zeroes still look like two people who
    /// tried; a name is better than a word when there is one.</item>
    /// <item>Every other phase — the plain overload above, byte for byte.</item>
    /// </list>
    /// </summary>
    public static string ClockLine(in HideSeekView view, Func<int, string>? nameOf,
        in HideSeekTuning tuning)
    {
        MatchSentence kind = ChooseMatchSentence(view);
        if (kind == MatchSentence.None || view.LastTally is not { } card)
            return ClockLine(view.Phase, view.RemainingSec, view.TowersCompleted, view.LastTally);

        return kind switch
        {
            MatchSentence.MatchTally => card.EndedByDisconnect
                ? LeftLine(view, card, nameOf)
                : card.WinnerPeerId == 0
                    ? "DRAW"
                    : $"{ClockName(nameOf, card.WinnerPeerId)} WINS",

            // The result stands through a disconnect here — MATCH-1's MatchHoldingLine makes the
            // same call for the same reason, and a wall that disagreed with the strip about THAT
            // is the defect this overload exists to close.
            MatchSentence.MatchHolding => card.WinnerPeerId == 0
                ? "DRAW"
                : $"{ClockName(nameOf, card.WinnerPeerId)} WON",

            MatchSentence.RoundTally when card.EndedByDisconnect => LeftLine(view, card, nameOf),

            _ => ClockLine(view.Phase, view.RemainingSec, view.TowersCompleted, view.LastTally),
        };
    }

    /// <summary>How many characters of a player's name the wall gets. Six, because the longest
    /// arm that carries one is <c>&lt;NAME&gt; WINS</c> and the panel can only rescue
    /// <see cref="RoundClockLayout.ReferenceChars"/> / <see cref="RoundClockLayout.MinScale"/>
    /// characters before the line is drawn outside it.
    ///
    /// <para>A truncated name is the right trade HERE and only here: the strip beside the player
    /// carries the full one, and the wall's job at that instant is "which of us", which six
    /// characters answers. <c>PLAYER 1196225384</c> truncating to <c>PLAYER</c> is the worst
    /// case and is still the honest answer the fallback was already giving.</para></summary>
    private const int ClockNameMaxChars = 6;

    /// <inheritdoc cref="ClockNameMaxChars"/>
    private static string ClockName(Func<int, string>? nameOf, int peerId)
    {
        string full = PlayerName(nameOf, peerId);
        return full.Length <= ClockNameMaxChars ? full : full[..ClockNameMaxChars].TrimEnd();
    }

    /// <summary>The disconnect arm, with the leaver recovered exactly as <see cref="MatchLine"/>
    /// recovers it. Falls back to CLOCK-1's own <c>ENDED EARLY</c> when the card's two role
    /// holders are both present or both gone, because naming nobody is worse than a word.</summary>
    private static string LeftLine(in HideSeekView view, in HideSeekTally card,
        Func<int, string>? nameOf)
    {
        int leaver = LeaverOf(view, card);
        return leaver == 0 ? "ENDED EARLY" : $"{ClockName(nameOf, leaver)} LEFT";
    }
}

/// <summary>
/// <b>How big the clock's second line is drawn</b> (CLOCK-1, 2026-09-19) — engine-free, so the
/// arithmetic that decides whether a player can read the wall is a unit test rather than a
/// screenshot.
///
/// <para><b>The problem it solves.</b> The line is <c>0:30</c> for almost the whole round and
/// <c>HID 3 · SEEK 172</c> for six seconds of it. A Label3D does not reflow, so a panel sized for
/// the timer has the tally hanging off both ends of it, and a panel sized for the tally has a
/// timer you cannot read from an aisle.</para>
///
/// <para><b>Why character count and not font metrics.</b> <c>Font.GetStringSize</c> would be
/// exact and would also drag a font resource, a theme lookup and a rendering server into a
/// number that has to be identical on a headless bot and on Talon's machine. This is a pure
/// function of an int, and it is the only reason the clock's legibility is a unit test.</para>
///
/// <para><b>What that costs, said plainly, because the first draft got it wrong.</b> A character
/// count is an AVERAGE, so it is not conservative in either direction: a line of wide glyphs is
/// wider than the estimate and a line of digits and colons is much narrower. The first cut
/// assumed the textbook 0.55-of-height advance, and the capture of the <c>Together</c> line
/// showed <c>3 SORTED</c> hanging off both ends of the panel. <see cref="ReferenceChars"/> now
/// carries the ratio measured off that render. <b>The render is the instrument here</b> — this
/// class can hold the arithmetic honest but it cannot tell you the constant is wrong, so a
/// change to the panel or the label size owes a new capture, not just a green suite.</para>
/// </summary>
public static class RoundClockLayout
{
    /// <summary>
    /// How many characters fit across the panel at the authored size — the width everything here
    /// is measured against.
    ///
    /// <para><b>Derived from the panel, and the advance ratio in it was MEASURED off a render
    /// rather than assumed.</b> <c>RoundClock.tscn</c>'s panel is 2.4 m wide and the timer's
    /// glyphs are 0.48 m tall (font_size 96 at pixel_size 0.005). The first cut of this number
    /// assumed the usual "a proportional sans averages 0.55 of its height per advance" and got
    /// 8 — and the capture of the <c>Together</c> line showed <c>3 SORTED</c> hanging off both
    /// ends of the panel. Measured on that frame (the panel spans 275 px, the eight-character
    /// line spans 283), this font's average advance is <b>0.64</b> of the glyph height, so the
    /// panel holds 2.4 / (0.48 × 0.64) ≈ 7.8 characters. Seven is that with a margin.</para>
    ///
    /// <para>Change the panel's width or the label's size and this number moves with them — and
    /// re-take the capture, because that is the only instrument that can see it. The unit test
    /// below bounds the copy against this number, but it cannot tell you the number is
    /// wrong.</para>
    ///
    /// <para><b>The two lines that matter are well inside it</b> — <c>0:30</c> is four and
    /// <c>10:00</c> is five — which is the point: the clock is at full size for the whole round
    /// and only the six-second tally card is ever shrunk.</para>
    /// </summary>
    public const int ReferenceChars = 7;

    /// <summary>
    /// Never shrink past this fraction of the authored size — below it the line is present but
    /// unreadable, which is a worse failure than an overhang because nothing looks broken. A line
    /// long enough to hit this floor is a copy defect, not a layout one.
    ///
    /// <para><b>0.35 is set by the longest line the wire can produce</b>, not by taste:
    /// <c>HID 255 · SEEK 255</c>, eighteen characters, both gains clamped at the byte ceiling
    /// <see cref="HideSeekWire.ClampByte"/> imposes. <see cref="ReferenceChars"/> / 18 = 0.389,
    /// so anything above that leaves a line the fit cannot rescue, and 0.35 is that with room.
    /// At this floor the glyphs are 0.17 m and subtend about 16 px of a 1152-line frame at 8 m —
    /// legible for the six seconds a tally card is up, and a good deal smaller than anything the
    /// clock shows for the rest of the round.</para></summary>
    public const float MinScale = 0.35f;

    /// <summary>
    /// The pixel size to draw a line of <paramref name="lineLength"/> characters at, given the
    /// authored <paramref name="basePixelSize"/>. Never larger than the authored value — a short
    /// line is not an invitation to grow, because the panel is also a fixed size and the two
    /// labels have to stay in proportion to each other.
    /// </summary>
    public static float FitPixelSize(float basePixelSize, int lineLength)
    {
        if (lineLength <= ReferenceChars)
            return basePixelSize;
        float scale = ReferenceChars / (float)lineLength;
        return basePixelSize * Math.Max(scale, MinScale);
    }
}
