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
}
