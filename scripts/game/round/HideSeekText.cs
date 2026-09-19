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
/// number that has to be identical on a headless bot and on Talon's machine. Counting characters
/// against a reference width is approximate in the direction that is safe — a proportional font
/// makes every real line NARROWER than the estimate, never wider — and it is a pure function of
/// an int.</para>
/// </summary>
public static class RoundClockLayout
{
    /// <summary>
    /// How many characters fit across the panel at the authored size — the width everything here
    /// is measured against.
    ///
    /// <para><b>Derived from the panel, not chosen.</b> <c>RoundClock.tscn</c>'s panel is 2.4 m
    /// wide and the timer's glyphs are 0.48 m tall (font_size 96 at pixel_size 0.005). A
    /// proportional sans averages roughly 0.55 of its height per advance, so the panel holds
    /// 2.4 / (0.48 × 0.55) ≈ 9 characters; eight is that with a margin. Change the panel's width
    /// or the label's size and this number moves with them.</para>
    ///
    /// <para><b>The two lines that matter are well inside it</b> — <c>0:30</c> is four and
    /// <c>10:00</c> is five — which is the point: the clock is at full size for the whole round
    /// and only the six-second tally card is ever shrunk.</para>
    /// </summary>
    public const int ReferenceChars = 8;

    /// <summary>Never shrink past this fraction of the authored size — below it the line is
    /// present but unreadable, which is a worse failure than an overhang because nothing looks
    /// broken. A line long enough to hit this floor is a copy defect, not a layout one.</summary>
    public const float MinScale = 0.40f;

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
