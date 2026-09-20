namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Which events one round message raises</b>, as a pure function of the view before it and the
/// view after it. Engine-free, so <c>tests/unit/RoundViewEventsTests.cs</c> holds the whole table
/// and <see cref="HideSeekDriver.ApplyRound"/> is left with the raising and nothing else.
///
/// <para><b>Why it was extracted</b> (REVIEW-1 C1, 2026-09-20). The decision used to live inline
/// in <c>ApplyRound</c>, where it returned early whenever the phase had not moved — so a message
/// carrying a REFUSAL and nothing else raised nothing at all, and the refusal's only remaining
/// delivery was <c>RoundStripWidget</c> reading <c>View.Refusal</c> as a level on
/// <c>GameHud</c>'s 10 Hz poll. The refusal is true for exactly one sim tick (~16 ms at 60 Hz),
/// so that read caught it about one time in six. Splitting the decision out is what makes the
/// difference between "a refusal is an edge" and "a refusal is a level you might sample" a
/// testable sentence rather than a comment.</para>
///
/// <para><b>A refusal is independent of the phase, and the other three are not.</b> Phase change,
/// find and reset are all statements ABOUT a transition and are silent without one. A refusal is
/// an answer to a press, and the presses that get refused are exactly the ones that did NOT move
/// the round — which is why gating it on a transition lost precisely the cases it exists
/// for.</para>
/// </summary>
public static class RoundViewEvents
{
    /// <summary>What one message crossed. <see cref="Refusal"/> is
    /// <see cref="HideSeekRefusal.None"/> on the overwhelming majority of messages; the three
    /// bools are false on all of them except the tick a phase moves.</summary>
    /// <param name="PhaseChanged">The phase is not the one the previous message carried.</param>
    /// <param name="Found">This message is the move into <see cref="HideSeekPhase.Together"/> and
    /// it carries a real found tick (a seek that timed out moves to Together with none).</param>
    /// <param name="ResetRequested">This message is the Tally -&gt; Holding commit, which is the
    /// world reset.</param>
    /// <param name="Refusal">Why the round turned a press down, or
    /// <see cref="HideSeekRefusal.None"/>. Reported whether or not the phase moved.</param>
    public readonly record struct Edges(
        bool PhaseChanged,
        bool Found,
        bool ResetRequested,
        HideSeekRefusal Refusal);

    /// <summary>The whole table. <paramref name="previous"/> is the view this peer held before
    /// the message landed; <paramref name="now"/> is the view folded from it.</summary>
    public static Edges Between(in HideSeekView previous, in HideSeekView now)
    {
        bool phaseChanged = now.Phase != previous.Phase;
        return new Edges(
            PhaseChanged: phaseChanged,
            // FoundTick rides as HideSeekWire.NoFoundTick (-1) when the seek timed out.
            Found: phaseChanged && now.Phase == HideSeekPhase.Together && now.FoundTick >= 0,
            ResetRequested: phaseChanged
                            && previous.Phase == HideSeekPhase.Tally
                            && now.Phase == HideSeekPhase.Holding,
            Refusal: now.Refusal);
    }
}
