namespace MpFoundation.Game.Round;

/// <summary>
/// Which half of the round loop the session is in (D3, <c>REVIEW-2026-09-15-BOTTLE-FREEWAY-STATE.md</c>
/// §4). <c>Gathering -&gt; Countdown -&gt; Round -&gt; Tally -&gt; Countdown -&gt; ...</c>, forever.
///
/// <para><b>There is deliberately no <c>Over</c>.</b> Unlike <c>ShiftPhase</c>'s attrition ending,
/// this session runs until the server process stops; the same players continue and a "loss" is
/// only ever a low tally, never a terminal state. Tally always leads back to Countdown.</para>
/// </summary>
public enum RoundPhase : byte
{
    /// <summary>Riders spawn mounted at markers, engines dead. A banner counts joins. Leaves on
    /// the host's start press, or <see cref="RoundLoopTuning.GatherSec"/> after the first human
    /// is present — whichever comes first.</summary>
    Gathering = 0,

    /// <summary>The shared start moment. Everyone is held at their marker; the host's start
    /// press is inert here (it already fired, to get in).</summary>
    Countdown = 1,

    /// <summary>The ride. Coins carried are the score; a shatter spills them.</summary>
    Round = 2,

    /// <summary>The card: coins, shatters, witnessed shatters per rider, the winner(s). Leads
    /// back to Countdown, which is where the world reset actually lands
    /// (<see cref="RoundLoopState.ResetRequested"/>).</summary>
    Tally = 3,
}
