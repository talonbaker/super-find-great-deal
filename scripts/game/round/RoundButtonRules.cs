namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Everything the three buttons DECIDE, as arithmetic.</b> Engine-free, so the lamp
/// derivation, the press gate and the "was that press accepted" read-back are all
/// <c>dotnet test</c>-able without a scene tree, a server or a socket. The Godot half
/// (<c>RoundButton</c>, <c>RoundControls</c>) is nodes, meshes, sounds and RPCs, and no
/// judgement.
///
/// <para>Same split, and for the same reason, as <c>Reachability</c> / <c>PhysicsReachSampler</c>
/// one lane over: the rule is the part that is worth a test and the part that rots silently.</para>
/// </summary>
public static class RoundButtonRules
{
    /// <summary>
    /// <b>Everything a peer needs to light a lamp</b>, in one struct so adding an input is one
    /// edit rather than a signature change at four call sites.
    ///
    /// <para><paramref name="Synced"/> is not a formality: <c>HideSeekDriver</c>'s zero value is
    /// "Holding, round 1, no roles", which is a perfectly plausible sentence and, on a peer that
    /// has not heard from the server yet, a wrong one. Same trap the HUD strip and every Synced
    /// flag in <c>BotHarness</c>'s sample exist for.</para>
    /// </summary>
    /// <remarks>
    /// <b><c>HumanCount</c> was removed at SOLO-1 (2026-09-20)</b> rather than left unread. The
    /// START lamp was the only thing that consulted it (<c>HumanCount == 2</c>, mirroring the
    /// loop's old "exactly two humans"), and that condition put the lamp OUT for both players the
    /// moment a third person walked into the room — and would have put it out for a solo tester
    /// too. "Both role slots are filled" is the same question asked of the two ids that actually
    /// decide it, and it is right in all three cases.
    /// </remarks>
    public readonly record struct LampFacts(
        bool Synced,
        HideSeekPhase Phase,
        int SelfPeerId,
        int HiderPeerId,
        int SeekerPeerId,
        bool SelfIsOnTheRoster,
        bool HiderHoldsRackProp,
        bool HiderHoldsTarget);

    /// <summary>
    /// <b>May this press become a fact at all?</b> <see cref="PressRefusal.None"/> means "latch
    /// it and let <see cref="HideSeekLoop"/> answer"; anything else is the button answering for
    /// itself.
    ///
    /// <para><b>The gate is deliberately NARROWER than the lamp.</b> It tests only the two things
    /// the loop cannot answer — the phase this button belongs to, and (for Confirm) whose button
    /// it is — and forwards everything else. So a Start pressed with one player in the room, or
    /// with the hider's hands empty, IS forwarded, is refused by the round, and comes back with
    /// the round's own sentence. That is the packet's whole affordance argument: a Dark button
    /// tells you not to bother before you press it, and the reason text tells you why when you do
    /// anyway. A gate that duplicated the loop's conditions would answer with a second opinion
    /// that could disagree with the authority.</para>
    ///
    /// <para><b>Why the phase is gated here and not in the loop.</b> <c>HideSeekLoop</c> reads
    /// <c>HostPressedStart</c> only inside its Holding branch; in any other phase the fact is
    /// folded and dropped with no refusal and no transition — a silent no-op, which is the defect
    /// class INTERACTION-BIBLE §2 is about. The loop is right to be shaped that way (it is
    /// phase-dispatched arithmetic); the button is the layer that owes the player an answer.</para>
    /// </summary>
    public static PressRefusal Gate(RoundButtonKind kind, HideSeekPhase phase, int presserPeerId,
        int hiderPeerId, int seekerPeerId)
    {
        // NOT IN THIS MATCH, checked before the per-kind rules so all three buttons answer a
        // waiting player alike (SOLO-1, 2026-09-20). It is the TRUE sentence for a third or later
        // joiner and neither of the two below is: the phase is right, so NotNow would be a lie,
        // and NotYourButton is the hider/seeker distinction, which is a different fact about a
        // person who IS playing.
        //
        // Guarded on both roles being dealt, because a round with vacant slots must not answer
        // every player "you are not in this match" -- that is a peer that has simply not synced,
        // and a dead button with a confident wrong reason on it is worse than a dark one.
        if (hiderPeerId != 0 && seekerPeerId != 0 && presserPeerId != 0
            && presserPeerId != hiderPeerId && presserPeerId != seekerPeerId)
        {
            return PressRefusal.NotInThisMatch;
        }

        switch (kind)
        {
            case RoundButtonKind.Start:
                // EITHER player may start, not just the host. The fact is named HostPressedStart
                // because the host hides first (program §1 item 5), but both of them are standing
                // in the holding room looking at the same button, and a button that is dead for
                // one of the two people in front of it is the affordance failure this packet is
                // about. The loop does not read who pressed it.
                return phase == HideSeekPhase.Holding ? PressRefusal.None : PressRefusal.NotNow;

            case RoundButtonKind.Confirm:
                if (phase != HideSeekPhase.Hiding)
                    return PressRefusal.NotNow;
                // The hider is alone in the search room while this is live, so in a real session
                // nobody else can reach it. It is still checked: "unreachable in practice" and
                // "refused" are different guarantees, and only the second survives a level edit.
                return presserPeerId != 0 && presserPeerId == hiderPeerId
                    ? PressRefusal.None
                    : PressRefusal.NotYourButton;

            case RoundButtonKind.End:
                // Either player. The beat after the startle belongs to both of them and whoever
                // has had enough of it ends it (IRoundFactSource.AnyPressedEnd, verbatim).
                return phase == HideSeekPhase.Together ? PressRefusal.None : PressRefusal.NotNow;

            default:
                return PressRefusal.NotNow;
        }
    }

    /// <summary>
    /// <b>What the lamp says.</b> Pure, so the affordance is testable and cannot disagree with
    /// itself between the three buttons.
    ///
    /// <list type="bullet">
    /// <item><b>START</b> — Holding, both roles filled, you are in one of them, and the hider is
    /// holding something off the rack. Exactly the conditions <c>HideSeekLoop</c>'s Holding
    /// branch accepts on, plus the one the button owes a waiting player.</item>
    /// <item><b>CONFIRM</b> — Hiding, and you are the hider, and your hands are empty of the
    /// target. Reachability is NOT in here and cannot be: it is a server-side physics
    /// measurement that never crosses the wire (see <see cref="RoundLamp.Lit"/>'s doc).</item>
    /// <item><b>END</b> — Together, and you are in this match. The loop commits the tally
    /// unconditionally, so the only thing to get wrong is lighting it at a spectator.</item>
    /// </list>
    /// </summary>
    public static RoundLamp Lamp(RoundButtonKind kind, in LampFacts f)
    {
        if (!f.Synced)
            return RoundLamp.Dark;

        return kind switch
        {
            // BOTH ROLE SLOTS FILLED, and this peer in one of them. Not a human count: a third
            // body in the room must not put the two players' lamp out (SOLO-1), and a solo
            // session fills both slots with one peer, which this reads correctly without
            // knowing the flag exists.
            RoundButtonKind.Start =>
                f.Phase == HideSeekPhase.Holding
                && f.HiderPeerId != 0 && f.SeekerPeerId != 0 && f.HiderHoldsRackProp
                && InThisMatch(f)
                    ? RoundLamp.Lit : RoundLamp.Dark,

            RoundButtonKind.Confirm =>
                f.Phase == HideSeekPhase.Hiding
                && f.SelfPeerId != 0 && f.SelfPeerId == f.HiderPeerId
                && !f.HiderHoldsTarget
                    ? RoundLamp.Lit : RoundLamp.Dark,

            RoundButtonKind.End =>
                f.Phase == HideSeekPhase.Together && InThisMatch(f)
                    ? RoundLamp.Lit : RoundLamp.Dark,

            _ => RoundLamp.Dark,
        };
    }

    /// <summary>
    /// Is the peer reading this lamp one of the two the round is about? (SOLO-1.)
    ///
    /// <para><b>Two things excuse a peer from the test, and the second one was measured.</b> A
    /// round whose roles are not dealt yet excludes nobody — same guard, same reason, as
    /// <see cref="Gate"/>'s. And <b>a peer that is not on the round's ROSTER is not a waiting
    /// player, it is nobody</b>: the dedicated server builds the world, so it owns a copy of every
    /// <c>RoundButton</c> and derives a lamp from its own multiplayer id, which holds no avatar
    /// and no role. Excluding it turned <c>Run-ButtonsTest.ps1</c> red on "the START lamp never
    /// went Lit" while every press in the same run behaved correctly.
    /// <see cref="LampFacts.SelfIsOnTheRoster"/> is the discriminator and it needs no magic id:
    /// every human the round knows about owns a score row from their first tick
    /// (<c>HideSeekLoop.FoldFacts</c>) and the server does not.</para>
    /// </summary>
    private static bool InThisMatch(in LampFacts f) =>
        !f.SelfIsOnTheRoster
        || f.HiderPeerId == 0 || f.SeekerPeerId == 0
        || f.SelfPeerId == f.HiderPeerId || f.SelfPeerId == f.SeekerPeerId;

    /// <summary>
    /// <b>Reading the round's answer to a press that was forwarded.</b> Called on the server one
    /// instant after <see cref="HideSeekLoop.Step"/> consumed the fact —
    /// <see cref="IRoundFactSource.AfterStep"/> is the hook, which is called in the same method,
    /// on the same tick, immediately after the step.
    ///
    /// <para>Three cases and they are exhaustive by construction:</para>
    /// <list type="number">
    /// <item>the loop set a named refusal ⇒ refused, with that reason forwarded verbatim;</item>
    /// <item>the phase moved ⇒ accepted;</item>
    /// <item>neither ⇒ <b>should be unreachable</b>, and the caller says so loudly. Every branch
    /// of the loop that reads a press either transitions or names a refusal — that is ROUND-1's
    /// stated invariant ("there is no branch in the loop that declines to act and returns the
    /// state unchanged") — so this case reaching a player means that invariant has been broken,
    /// and the player still gets an answer rather than silence.</item>
    /// </list>
    /// </summary>
    public static (bool Accepted, PressRefusal Reason) ReadResult(HideSeekPhase phaseAtPress,
        HideSeekPhase phaseAfterStep, HideSeekRefusal refusalAfterStep)
    {
        if (refusalAfterStep != HideSeekRefusal.None)
            return (false, (PressRefusal)(byte)refusalAfterStep);
        if (phaseAfterStep != phaseAtPress)
            return (true, PressRefusal.None);
        return (false, PressRefusal.NotNow);
    }
}
