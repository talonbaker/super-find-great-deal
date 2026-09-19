using System;
using System.Collections.Immutable;
using System.Linq;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The round loop, as pure arithmetic</b> (SESSION-2; D3 in
/// <c>REVIEW-2026-09-15-BOTTLE-FREEWAY-STATE.md</c> §4; design doc
/// <c>docs/design/2026-09-15-round-loop.md</c>).
///
/// <para><b>Gathering, Countdown, Round, Tally, Countdown, ...</b> — fixed-dt, engine-free, and
/// the only mutator of <see cref="RoundLoopState"/>. Copied shape from
/// <see cref="MpFoundation.Dev.Session.ShiftLoop"/>: a value-struct state, a single
/// <see cref="Step"/> entry point, absolute values everywhere, timers that clamp to a floor and
/// fire at <c>&lt;= 0</c> after the decrement.</para>
///
/// <para><b>Facts are folded before any phase transition is evaluated</b> — the same ordering
/// <c>ShiftLoop.Step</c> uses for R1 ("bank first, consult the clock second"). Concretely: a
/// shatter's coin spill and the shatter count both land in <see cref="RoundLoopState"/> at the
/// TOP of <see cref="Step"/>, before the Round phase checks whether this is the horn tick. A
/// bottle that shatters on the exact tick the round ends therefore has ALREADY lost its coins by
/// the time the tally is frozen — "the tally reads coins after the spill" (the packet's own
/// words), and it falls out of the ordering rather than needing a special case.</para>
///
/// <para><b>The verdict is the tally, computed exactly once</b> (MECHANICS §4), at the
/// Round -&gt; Tally commit, and stored on <see cref="RoundLoopState.LastTally"/>. Every
/// subsequent tick in Tally reads it back; nothing recomputes it.</para>
/// </summary>
public static class RoundLoop
{
    /// <summary><b>A fresh session.</b> Round 1, Gathering, nobody carrying anything, no tally
    /// yet. Called once when the server boots the world.</summary>
    public static RoundLoopState Restart(in RoundLoopTuning t) => new()
    {
        Phase = RoundPhase.Gathering,
        RoundIndex = 1,
        RemainingSec = 0f,
        GatherArmed = false,
        CarriedCoinsPerRider = ImmutableDictionary<int, int>.Empty,
        ShattersPerRider = ImmutableDictionary<int, int>.Empty,
        WitnessedShattersPerRider = ImmutableDictionary<int, int>.Empty,
        ResetRequested = false,
        LastTally = null,
    };

    /// <summary>
    /// One fixed-dt tick. The ONLY mutator.
    ///
    /// <para><b>Order, every tick:</b> (1) clear last tick's one-shot
    /// <see cref="RoundLoopState.ResetRequested"/> edge; (2) fold this tick's facts (coins,
    /// shatters) into the state, absolute and unconditional of phase; (3) evaluate this phase's
    /// transition. A driver that calls <see cref="Step"/> once per physics tick observes
    /// <c>ResetRequested</c> true for exactly the one tick it was set on, never longer, regardless
    /// of that tick's <c>dt</c>.</para>
    /// </summary>
    public static RoundLoopState Step(RoundLoopState s, in RoundLoopInput input, float dt,
        in RoundLoopTuning t)
    {
        if (dt < 0f || float.IsNaN(dt))
            dt = 0f;

        if (s.ResetRequested)
            s = s with { ResetRequested = false };

        s = FoldFacts(s, input);

        switch (s.Phase)
        {
            case RoundPhase.Gathering:
            {
                // The host's start press is unconditional here (R4: it works whether or not the
                // gather clock has even armed) and dropped everywhere else — HostPressedStart is
                // simply never read outside this branch, which is what makes a second press
                // during Countdown a silent no-op rather than a case anyone had to guard.
                if (input.HostPressedStart)
                    return EnterCountdownFirst(s, t);

                if (!s.GatherArmed)
                {
                    // Nobody here yet: stay put. The clock has not started, so there is nothing
                    // to decrement — a Gathering phase with zero humans waits forever, by design.
                    if (!input.HumansPresent)
                        return s;

                    // Armed THIS tick. Matches ShiftLoop.EnterLaunching: the tick that arms a
                    // timer does not also decrement it.
                    return s with
                    {
                        GatherArmed = true,
                        RemainingSec = Math.Max(t.GatherSec, RoundLoopTuning.MinTimerSec),
                    };
                }

                float left = s.RemainingSec - dt;
                if (left > 0f)
                    return s with { RemainingSec = left };
                return EnterCountdownFirst(s, t);
            }

            case RoundPhase.Countdown:
            {
                // The ready lever is inert here (ShiftLoop's LAUNCHING note, copied): a start
                // press bouncing in mid-countdown changes nothing because this branch never
                // consults HostPressedStart at all.
                float left = s.RemainingSec - dt;
                if (left > 0f)
                    return s with { RemainingSec = left };
                return EnterRound(s, t);
            }

            case RoundPhase.Round:
            {
                float left = s.RemainingSec - dt;
                s = s with { RemainingSec = left };
                if (left > 0f)
                    return s;
                // The horn. Facts for THIS tick (including any shatter's spill) were already
                // folded above, so the tally this freezes is the post-spill one.
                return EnterTally(s, t);
            }

            case RoundPhase.Tally:
            {
                float left = s.RemainingSec - dt;
                if (left > 0f)
                    return s with { RemainingSec = left };
                return EnterCountdownFromTally(s, t);
            }

            default:
                return s;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Fact folding — absolute, unconditional of phase.
    // ---------------------------------------------------------------------------------------

    private static RoundLoopState FoldFacts(RoundLoopState s, in RoundLoopInput input)
    {
        ImmutableDictionary<int, int> coins = s.CarriedCoinsPerRider;
        if (input.CarriedCoinsPerRider is { } reported)
            foreach ((int riderId, int carried) in reported)
                coins = coins.SetItem(riderId, Math.Max(carried, 0));

        ImmutableDictionary<int, int> shatters = s.ShattersPerRider;
        ImmutableDictionary<int, int> witnessed = s.WitnessedShattersPerRider;
        if (!input.Shatters.IsDefault)
        {
            foreach (RoundShatterEvent ev in input.Shatters)
            {
                shatters = shatters.SetItem(ev.RiderId,
                    (shatters.TryGetValue(ev.RiderId, out int c) ? c : 0) + 1);

                // A shatter always counts; only a CONFIRMED witness (true) counts again here.
                // An unknown witness state (null, phase A's only value today) is neither
                // promoted nor assumed — WITNESS-1 owns turning null into a real answer.
                if (ev.Witnessed == true)
                    witnessed = witnessed.SetItem(ev.RiderId,
                        (witnessed.TryGetValue(ev.RiderId, out int w) ? w : 0) + 1);
            }
        }

        return s with
        {
            CarriedCoinsPerRider = coins,
            ShattersPerRider = shatters,
            WitnessedShattersPerRider = witnessed,
        };
    }

    // ---------------------------------------------------------------------------------------
    // The transitions. One method each (ShiftLoop's shape): what happens on entry is readable.
    // ---------------------------------------------------------------------------------------

    /// <summary>Gathering -&gt; Countdown, round 1. Nothing to reset yet: riders spawned into
    /// Gathering already carrying nothing.</summary>
    private static RoundLoopState EnterCountdownFirst(in RoundLoopState s, in RoundLoopTuning t) =>
        s with
        {
            Phase = RoundPhase.Countdown,
            GatherArmed = false,
            RemainingSec = Math.Max(t.CountdownSec, RoundLoopTuning.MinTimerSec),
        };

    private static RoundLoopState EnterRound(in RoundLoopState s, in RoundLoopTuning t) =>
        s with
        {
            Phase = RoundPhase.Round,
            RemainingSec = Math.Max(t.RoundSec, RoundLoopTuning.MinTimerSec),
        };

    /// <summary>
    /// <b>The tally instant.</b> Evaluated once, here, from state already folded with this tick's
    /// facts — MECHANICS §4. Ties share the win (D3: "Win = most coins at the horn; ties share
    /// it").
    /// </summary>
    private static RoundLoopState EnterTally(RoundLoopState s, in RoundLoopTuning t)
    {
        ImmutableArray<RoundTallyLine> lines = s.CarriedCoinsPerRider.Keys
            .OrderBy(id => id)
            .Select(id => new RoundTallyLine(
                id,
                s.CarriedCoinsPerRider.TryGetValue(id, out int c) ? c : 0,
                s.ShattersPerRider.TryGetValue(id, out int sh) ? sh : 0,
                s.WitnessedShattersPerRider.TryGetValue(id, out int w) ? w : 0))
            .ToImmutableArray();

        int maxCoins = lines.Length == 0 ? 0 : lines.Max(l => l.Coins);
        ImmutableArray<int> winners = lines.Length == 0
            ? ImmutableArray<int>.Empty
            : lines.Where(l => l.Coins == maxCoins).Select(l => l.RiderId).ToImmutableArray();

        return s with
        {
            Phase = RoundPhase.Tally,
            RemainingSec = Math.Max(t.TallySec, RoundLoopTuning.MinTimerSec),
            LastTally = new RoundTallyResult(winners, lines),
        };
    }

    /// <summary>
    /// <b>The reset commit.</b> Tally -&gt; Countdown, same players, next round: every rider's
    /// carried coins and shatter counts return to zero and <see cref="RoundLoopState.ResetRequested"/>
    /// goes true for exactly this one tick, so a server driver can key the WORLD reset (coins to
    /// field, spill cleared, wrecks cleared, cars re-seeded, riders <c>ResetTo</c> a marker) off
    /// this single idempotent signal rather than off the UI closing the card.
    /// </summary>
    private static RoundLoopState EnterCountdownFromTally(RoundLoopState s, in RoundLoopTuning t)
    {
        ImmutableDictionary<int, int> zeroedCoins = s.CarriedCoinsPerRider.Keys
            .ToImmutableDictionary(id => id, _ => 0);
        ImmutableDictionary<int, int> zeroedShatters = s.ShattersPerRider.Keys
            .ToImmutableDictionary(id => id, _ => 0);
        ImmutableDictionary<int, int> zeroedWitnessed = s.WitnessedShattersPerRider.Keys
            .ToImmutableDictionary(id => id, _ => 0);

        return s with
        {
            Phase = RoundPhase.Countdown,
            RoundIndex = s.RoundIndex + 1,
            RemainingSec = Math.Max(t.CountdownSec, RoundLoopTuning.MinTimerSec),
            CarriedCoinsPerRider = zeroedCoins,
            ShattersPerRider = zeroedShatters,
            WitnessedShattersPerRider = zeroedWitnessed,
            ResetRequested = true,
        };
    }
}
