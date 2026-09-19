using System;
using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The hide-and-startle round, as pure arithmetic</b> (packet ROUND-1; program
/// <c>docs/design/2026-09-19-supermarket-mvp.md</c> §2).
///
/// <para>Fixed-dt, engine-free, and the only mutator of <see cref="HideSeekState"/>. It is a
/// rewrite of <c>RoundLoop</c> — the loop imported from the bottle game's session-2 branch — with
/// its phases replaced and its shape kept: one <see cref="Step"/>, facts folded first, transitions
/// evaluated second, absolute values everywhere, timers that clamp to a floor and fire at
/// <c>&lt;= 0</c> after the decrement, one-tick edges cleared at the top of the next call. Three
/// lessons came across with it, each paid for once already and each marked where it lives:</para>
///
/// <list type="number">
/// <item><b>Facts are folded before any transition is evaluated.</b> See
/// <see cref="FoldFacts"/>: the roster, the roles, the tower count and the score rows all land in
/// the state at the TOP of <see cref="Step"/>, before any branch asks whether this is the tick the
/// phase ends. A tower finished on the exact tick the seeker finds the object is therefore already
/// counted when the towers are frozen — it falls out of the ordering rather than needing a special
/// case.</item>
/// <item><b>The card is computed exactly once</b>, at the commit into
/// <see cref="HideSeekPhase.Tally"/>, and stored on <see cref="HideSeekState.LastTally"/>. Every
/// later tick in Tally reads it back.</item>
/// <item><b>Identity is never a byte.</b> That one is <see cref="HideSeekWire"/>'s, and its own
/// class doc carries the measurement.</item>
/// </list>
///
/// <para><b>Every refusal has a name</b> (<see cref="HideSeekRefusal"/>). There is no branch in
/// this file that declines to act and returns the state unchanged: a Start or a Confirm that does
/// not fire sets <see cref="HideSeekState.Refusal"/> for exactly one tick, and the driver
/// broadcasts it. A silent no-op is the defect.</para>
/// </summary>
public static class HideSeekLoop
{
    /// <summary><b>A fresh session.</b> Round 1, the holding room, no roles yet, nobody scoring,
    /// no card. Called once when the server builds the world.</summary>
    public static HideSeekState Restart(in HideSeekTuning t)
    {
        _ = t; // no timer is armed here: Holding has no clock. Taken for symmetry with the
               // EnterX helpers and so a future gather timer is a one-line change.
        return new HideSeekState
        {
            Phase = HideSeekPhase.Holding,
            RoundIndex = 1,
            RemainingSec = 0f,
            Tick = 0,
            HiderPeerId = 0,
            SeekerPeerId = 0,
            Scores = ImmutableDictionary<int, int>.Empty,
            TowersCompleted = 0,
            TowersAtFound = 0,
            RemainingAtFoundSec = 0,
            FoundTick = null,
            HidingExtended = false,
            ResetRequested = false,
            Refusal = HideSeekRefusal.None,
            LastTally = null,
        };
    }

    /// <summary>
    /// One fixed-dt tick. The ONLY mutator.
    ///
    /// <para><b>Order, every tick:</b> (1) clear last tick's one-shot edges
    /// (<see cref="HideSeekState.ResetRequested"/>, <see cref="HideSeekState.Refusal"/>) and
    /// advance <see cref="HideSeekState.Tick"/>; (2) fold this tick's facts, absolute and
    /// unconditional of phase; (3) end the round outright if a role holder has gone; (4) evaluate
    /// this phase's transition. A driver that calls this once per sim tick observes each one-shot
    /// edge true for exactly the one tick it was set on, regardless of that tick's <c>dt</c>.</para>
    /// </summary>
    public static HideSeekState Step(HideSeekState s, in HideSeekInput input, float dt,
        in HideSeekTuning t)
    {
        if (dt < 0f || float.IsNaN(dt))
            dt = 0f;

        if (s.ResetRequested)
            s = s with { ResetRequested = false };
        if (s.Refusal != HideSeekRefusal.None)
            s = s with { Refusal = HideSeekRefusal.None };
        s = s with { Tick = s.Tick + 1 };

        s = FoldFacts(s, input);

        // A hider or a seeker who has left cannot be waited for: the other player would stand in
        // an empty room until a 180 s clock ran out. The round ends where it stands, the survivor
        // keeps the score they already had (both gains are zero — nobody completed anything), and
        // the card says it ended this way rather than reporting a 0-0 round that looks like two
        // people who tried. Checked BEFORE the phase switch so it applies in Hiding, Seeking and
        // Together alike, and never in Holding (where the fold simply re-assigns roles) or in
        // Tally (where the card is already frozen and the reset edge is seconds away).
        if (IsMidRound(s.Phase) && RoleHolderMissing(s, input))
            return CommitTally(s, t, hiderGain: 0, seekerGain: 0, byDisconnect: true,
                refusal: HideSeekRefusal.None);

        switch (s.Phase)
        {
            case HideSeekPhase.Holding:
            {
                if (!input.HostPressedStart)
                    return s;
                // Exactly two, not at least two (program §1 item 5): one hider, one seeker. The
                // transport still accepts six, so this is a refusal with a sentence on it rather
                // than a cap somewhere in the net stack.
                if (input.Humans.Length != 2 || s.HiderPeerId == 0 || s.SeekerPeerId == 0)
                    return s with { Refusal = HideSeekRefusal.NeedTwoPlayers };
                if (!input.HiderHeldRackProp)
                    return s with { Refusal = HideSeekRefusal.HiderMustHoldAnObject };
                return EnterHiding(s, t);
            }

            case HideSeekPhase.Hiding:
            {
                // A refused Confirm must not also stall the clock, so the reason is carried down
                // to the single return below rather than short-circuiting out of the branch.
                HideSeekRefusal refusal = HideSeekRefusal.None;
                if (input.HiderPressedConfirm)
                {
                    if (input.HiderHoldsTarget)
                        refusal = HideSeekRefusal.PutTheObjectDownFirst;
                    else if (!input.TargetRetrievableOrDefault)
                        refusal = HideSeekRefusal.NobodyCouldReachThat;
                    else
                        return EnterSeeking(s, t);
                }

                float left = s.RemainingSec - dt;
                if (left > 0f)
                    return s with { RemainingSec = left, Refusal = refusal };

                // The buzzer. A hide that is not retrievable does NOT start the seek: a seeker who
                // cannot find the object because it is inside a wall is the worst outcome this
                // design has (program §5b), and "the clock ran out" is not a reason to hand them
                // that round.
                if (!input.HiderHoldsTarget && input.TargetRetrievableOrDefault)
                    return EnterSeeking(s, t);

                HideSeekRefusal why = input.HiderHoldsTarget
                    ? HideSeekRefusal.PutTheObjectDownFirst
                    : HideSeekRefusal.NobodyCouldReachThat;

                if (!s.HidingExtended)
                    return s with
                    {
                        RemainingSec = Math.Max(t.HidingGraceSec, HideSeekTuning.MinTimerSec),
                        HidingExtended = true,
                        Refusal = why,
                    };

                // Grace spent and still broken: the hide failed. Tally, hider 0 for the round.
                return CommitTally(s, t, hiderGain: 0, seekerGain: 0, byDisconnect: false,
                    refusal: why);
            }

            case HideSeekPhase.Seeking:
            {
                // The find is consulted BEFORE the decrement, so an object that lands in the bin
                // on the exact tick the clock expires is a find and not a timeout. Same ordering
                // principle as the fact fold: the fact has already happened.
                if (input.TargetInDropOff)
                    return EnterTogether(s);

                float left = s.RemainingSec - dt;
                s = s with { RemainingSec = left };
                if (left > 0f)
                    return s;

                // Timeout. The hider keeps the towers they built (program §2's diagram: "timeout
                // -> Tally (hiders keep towers)"); the seeker scores nothing.
                return CommitTally(
                    s with { TowersAtFound = s.TowersCompleted, RemainingAtFoundSec = 0 },
                    t, hiderGain: s.TowersCompleted, seekerGain: 0, byDisconnect: false,
                    refusal: HideSeekRefusal.None);
            }

            case HideSeekPhase.Together:
            {
                // No clock here on purpose (program §2's table): the beat after the startle
                // belongs to the players.
                if (!input.AnyPressedEnd)
                    return s;
                return CommitTally(s, t, hiderGain: s.TowersAtFound,
                    seekerGain: s.RemainingAtFoundSec, byDisconnect: false,
                    refusal: HideSeekRefusal.None);
            }

            case HideSeekPhase.Tally:
            {
                float left = s.RemainingSec - dt;
                if (left > 0f)
                    return s with { RemainingSec = left };
                return EnterHolding(s);
            }

            default:
                return s;
        }
    }

    /// <summary>The phases in which a missing role holder ends the round. Holding is excluded
    /// because roles there are simply re-assigned; Tally because the card is already frozen.</summary>
    private static bool IsMidRound(HideSeekPhase phase) =>
        phase is HideSeekPhase.Hiding or HideSeekPhase.Seeking or HideSeekPhase.Together;

    private static bool RoleHolderMissing(in HideSeekState s, in HideSeekInput input)
    {
        ImmutableArray<int> humans = input.Humans;
        return s.HiderPeerId == 0 || s.SeekerPeerId == 0
            || !humans.Contains(s.HiderPeerId) || !humans.Contains(s.SeekerPeerId);
    }

    // ---------------------------------------------------------------------------------------
    // Fact folding — absolute, before any transition is evaluated.
    // ---------------------------------------------------------------------------------------

    private static HideSeekState FoldFacts(HideSeekState s, in HideSeekInput input)
    {
        ImmutableArray<int> humans = input.Humans;

        // Every present human owns a score row, even at zero. A board that shows a player only
        // once they have scored is a board that looks broken for the whole first round, and the
        // absolute wire has to carry a row to carry a zero.
        ImmutableDictionary<int, int> scores = s.Scores ?? ImmutableDictionary<int, int>.Empty;
        foreach (int peer in humans)
            if (!scores.ContainsKey(peer))
                scores = scores.SetItem(peer, 0);

        int hider = s.HiderPeerId;
        int seeker = s.SeekerPeerId;

        // ROLES ARE ASSIGNED AND REPAIRED ONLY IN HOLDING, and that is what lets the swap at the
        // reset edge survive. A repair that ran every tick would re-derive "hider = first joined"
        // on the tick after the swap and silently undo it — the same player would hide forever.
        if (s.Phase == HideSeekPhase.Holding)
        {
            bool valid = hider != 0 && seeker != 0 && hider != seeker
                && humans.Contains(hider) && humans.Contains(seeker);
            if (!valid)
            {
                // Program §1 item 5: first round the host hides, the second joiner seeks. The
                // roster is in JOIN order, so that is just index 0 and index 1.
                hider = humans.Length >= 1 ? humans[0] : 0;
                seeker = humans.Length >= 2 ? humans[1] : 0;
            }
        }

        // Absolute, clamped at zero, never an increment. TASK-1 reports what the hider has
        // completed RIGHT NOW; the reset edge is what tells it to start counting again, so a
        // source that kept counting across a round boundary would show up here as a round that
        // began with towers already built.
        int towers = Math.Max(input.TowersCompleted, 0);

        return s with
        {
            Scores = scores,
            HiderPeerId = hider,
            SeekerPeerId = seeker,
            TowersCompleted = towers,
        };
    }

    // ---------------------------------------------------------------------------------------
    // The transitions. One method each, so what happens on entry is readable.
    // ---------------------------------------------------------------------------------------

    /// <summary>Holding -&gt; Hiding. The round's accumulators are cleared HERE, at the start of
    /// the round, rather than at the reset edge: the holding-room board is supposed to still show
    /// the last card while everyone stands around, and a reset that wiped it would take that
    /// away.</summary>
    private static HideSeekState EnterHiding(in HideSeekState s, in HideSeekTuning t) =>
        s with
        {
            Phase = HideSeekPhase.Hiding,
            RemainingSec = Math.Max(t.HidingSec, HideSeekTuning.MinTimerSec),
            HidingExtended = false,
            TowersCompleted = 0,
            TowersAtFound = 0,
            RemainingAtFoundSec = 0,
            FoundTick = null,
            Refusal = HideSeekRefusal.None,
        };

    private static HideSeekState EnterSeeking(in HideSeekState s, in HideSeekTuning t) =>
        s with
        {
            Phase = HideSeekPhase.Seeking,
            RemainingSec = Math.Max(t.SeekingSec, HideSeekTuning.MinTimerSec),
            Refusal = HideSeekRefusal.None,
        };

    /// <summary><b>The find.</b> Both scores freeze here, on one tick, on the server: the hider's
    /// towers stop counting (the cost of being found late is the tower you did not finish) and the
    /// seeker's clock stops (the faster the find, the better the seek). Everything DOOR-1 and
    /// TASK-1 do at the startle keys off <see cref="HideSeekState.FoundTick"/>, so there is one
    /// instant, not one per subsystem.</summary>
    private static HideSeekState EnterTogether(in HideSeekState s) =>
        s with
        {
            Phase = HideSeekPhase.Together,
            RemainingSec = 0f,
            FoundTick = s.Tick,
            TowersAtFound = s.TowersCompleted,
            RemainingAtFoundSec = (int)MathF.Floor(Math.Max(s.RemainingSec, 0f)),
            Refusal = HideSeekRefusal.None,
        };

    /// <summary>
    /// <b>The card instant.</b> Evaluated once, here, from a state already folded with this tick's
    /// facts. The cumulative scores move exactly here and nowhere else.
    ///
    /// <para><see cref="HideSeekState.RoundIndex"/> advances at this commit (packet ROUND-1 §1),
    /// which is why <see cref="HideSeekTally.RoundIndex"/> exists — the card records the round it
    /// is about, so it can never label round 1's result "ROUND 2".</para>
    /// </summary>
    private static HideSeekState CommitTally(HideSeekState s, in HideSeekTuning t,
        int hiderGain, int seekerGain, bool byDisconnect, HideSeekRefusal refusal)
    {
        hiderGain = Math.Max(hiderGain, 0);
        seekerGain = Math.Max(seekerGain, 0);

        ImmutableDictionary<int, int> scores = s.Scores ?? ImmutableDictionary<int, int>.Empty;
        if (s.HiderPeerId != 0)
            scores = scores.SetItem(s.HiderPeerId,
                (scores.TryGetValue(s.HiderPeerId, out int h) ? h : 0) + hiderGain);
        if (s.SeekerPeerId != 0)
            scores = scores.SetItem(s.SeekerPeerId,
                (scores.TryGetValue(s.SeekerPeerId, out int k) ? k : 0) + seekerGain);

        return s with
        {
            Phase = HideSeekPhase.Tally,
            RoundIndex = s.RoundIndex + 1,
            RemainingSec = Math.Max(t.TallySec, HideSeekTuning.MinTimerSec),
            Scores = scores,
            TowersAtFound = hiderGain,
            RemainingAtFoundSec = seekerGain,
            LastTally = new HideSeekTally(s.RoundIndex, s.HiderPeerId, hiderGain,
                s.SeekerPeerId, seekerGain, byDisconnect),
            Refusal = refusal,
        };
    }

    /// <summary>
    /// <b>The reset commit.</b> Tally -&gt; Holding: roles swap, the round's accumulators clear
    /// and <see cref="HideSeekState.ResetRequested"/> goes true for exactly this one tick, so the
    /// server driver can key the WORLD reset off one idempotent signal rather than off a card
    /// closing.
    ///
    /// <para><b>What the driver owes that edge</b> (BASE-1's handoff, and it is the thing not to
    /// lose): the prop slice goes home AND the reconnect registry is cleared. A 60 s resume ticket
    /// into a world that has since been reset is an exploit, not a courtesy — a peer that dropped
    /// just before the edge would otherwise come back holding pre-reset props at a pre-reset
    /// position inside a freshly restored room.</para>
    ///
    /// <para><b>The swap is unconditional and the repair in <see cref="FoldFacts"/> is what makes
    /// it safe.</b> If a role was vacant, swapping produces a vacancy in the other slot, and the
    /// very next tick's Holding repair fills both from the live roster.</para>
    /// </summary>
    private static HideSeekState EnterHolding(in HideSeekState s) =>
        s with
        {
            Phase = HideSeekPhase.Holding,
            RemainingSec = 0f,
            HiderPeerId = s.SeekerPeerId,
            SeekerPeerId = s.HiderPeerId,
            TowersCompleted = 0,
            TowersAtFound = 0,
            RemainingAtFoundSec = 0,
            FoundTick = null,
            HidingExtended = false,
            ResetRequested = true,
            Refusal = HideSeekRefusal.None,
        };
}
