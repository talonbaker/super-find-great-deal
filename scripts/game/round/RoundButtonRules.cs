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
    public readonly record struct LampFacts(
        bool Synced,
        HideSeekPhase Phase,
        int SelfPeerId,
        int HiderPeerId,
        int SeekerPeerId,
        int HumanCount,
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
        _ = seekerPeerId;   // taken for symmetry; End is deliberately either player's button.
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
    /// <item><b>START</b> — Holding, exactly two humans, both roles filled, and the hider is
    /// holding something off the rack. Exactly the conditions <c>HideSeekLoop</c>'s Holding
    /// branch accepts on.</item>
    /// <item><b>CONFIRM</b> — Hiding, and you are the hider, and your hands are empty of the
    /// target. Reachability is NOT in here and cannot be: it is a server-side physics
    /// measurement that never crosses the wire (see <see cref="RoundLamp.Lit"/>'s doc).</item>
    /// <item><b>END</b> — Together. There is nothing else to get wrong; the loop commits the
    /// tally unconditionally.</item>
    /// </list>
    /// </summary>
    public static RoundLamp Lamp(RoundButtonKind kind, in LampFacts f)
    {
        if (!f.Synced)
            return RoundLamp.Dark;

        return kind switch
        {
            RoundButtonKind.Start =>
                f.Phase == HideSeekPhase.Holding && f.HumanCount == 2
                && f.HiderPeerId != 0 && f.SeekerPeerId != 0 && f.HiderHoldsRackProp
                    ? RoundLamp.Lit : RoundLamp.Dark,

            RoundButtonKind.Confirm =>
                f.Phase == HideSeekPhase.Hiding
                && f.SelfPeerId != 0 && f.SelfPeerId == f.HiderPeerId
                && !f.HiderHoldsTarget
                    ? RoundLamp.Lit : RoundLamp.Dark,

            RoundButtonKind.End =>
                f.Phase == HideSeekPhase.Together ? RoundLamp.Lit : RoundLamp.Dark,

            _ => RoundLamp.Dark,
        };
    }

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
