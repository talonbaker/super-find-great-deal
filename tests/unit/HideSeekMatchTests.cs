using System;
using System.Collections.Immutable;
using MpFoundation.Game.Round;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MATCH-1's engine-free tier: a match is two rounds, and then somebody has won.</b>
///
/// <para>Talon, 2026-09-19: "just do one round of hide, one round of seek so they can both have a
/// turn, and then see who wins." Everything that makes that true is arithmetic —
/// <c>RoundIndex % MatchRounds</c>, two totals compared, a longer Tally, a score map zeroed at
/// the right Start — so all of it is tested here rather than in a scene. The smoke
/// (<c>tests/Run-RoundLoopSmoke.ps1</c>) proves the same facts survive two real clients and the
/// wire; it does not re-prove any rule.</para>
///
/// <para><b>Everything runs at a fixed 1/60 s tick</b> on a SHORTENED tuning (5 s hide, 20 s
/// seek), for the reason <see cref="HideSeekLoopTests"/> states about the tick and one of its
/// own: four full rounds at the shipping 180 s seek is a million ticks of nothing, and the
/// quantity under test is the round INDEX, which does not care how long a round took. Both Tally
/// lengths are kept distinct (6 s / 10 s) because telling them apart is one of the gates.</para>
/// </summary>
public class HideSeekMatchTests
{
    private const float Dt = 1f / 60f;

    private const int Host = 11;
    private const int Joiner = 22;

    /// <summary>Short phases, both tally lengths distinct, two rounds to a match.</summary>
    private static HideSeekTuning Tune(int matchRounds = 2) => HideSeekTuning.Default with
    {
        HidingSec = 5f,
        SeekingSec = 20f,
        TallySec = 6f,
        MatchTallySec = 10f,
        MatchRounds = matchRounds,
    };

    private static HideSeekInput Idle => new() { HumanPeerIds = ImmutableArray.Create(Host, Joiner) };

    private static HideSeekState Tick(HideSeekState s, HideSeekInput input, HideSeekTuning t) =>
        HideSeekLoop.Step(s, input, Dt, t);

    private static HideSeekTally Card(in HideSeekState s)
    {
        Assert.True(s.LastTally.HasValue, "no card was computed");
        return s.LastTally!.Value;
    }

    /// <summary>
    /// One whole round, from a Holding state to the Tally it commits: Start, Confirm,
    /// <paramref name="seekSeconds"/> of seeking, the find, End.
    ///
    /// <para><paramref name="seekSeconds"/> is deliberately a half-second off every integer. The
    /// seeker's gain is <c>floor(remaining at the find)</c> and the clock here is reached by
    /// 60 Hz repeated subtraction, so a schedule that lands ON a second boundary can floor either
    /// side of it depending on accumulated float error. Half a second of margin makes every gain
    /// in this file exact.</para>
    /// </summary>
    private static HideSeekState PlayRound(HideSeekState s, HideSeekTuning t, int towers,
        float seekSeconds)
    {
        HideSeekInput carrying = Idle with { TowersCompleted = towers };

        s = Tick(s, Idle, t);                               // the fold assigns/keeps the roles
        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true }, t);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);

        s = Tick(s, carrying with { HiderPressedConfirm = true }, t);
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);

        int ticks = (int)MathF.Round(seekSeconds / Dt);
        for (int i = 0; i < ticks; i++)
            s = Tick(s, carrying, t);
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);

        s = Tick(s, carrying with { TargetInDropOff = true }, t);
        Assert.Equal(HideSeekPhase.Together, s.Phase);

        s = Tick(s, carrying with { TargetInDropOff = true, AnyPressedEnd = true }, t);
        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        return s;
    }

    /// <summary>Runs the card out to the reset edge and returns the Holding state after it.</summary>
    private static HideSeekState FinishTally(HideSeekState s, HideSeekTuning t)
    {
        for (int i = 0; i < 60 * 600; i++)
        {
            s = Tick(s, Idle, t);
            if (s.Phase == HideSeekPhase.Holding)
                return s;
        }
        Assert.Fail("the Tally never reached the reset edge");
        return s;
    }

    /// <summary>A whole round plus its tally, landing back in the holding room.</summary>
    private static HideSeekState PlayRoundAndReset(HideSeekState s, HideSeekTuning t, int towers,
        float seekSeconds) => FinishTally(PlayRound(s, t, towers, seekSeconds), t);

    // ============================================================================================
    // Where a match ends
    // ============================================================================================

    /// <summary><b>The gate, stated as one test.</b> Four rounds at <c>MatchRounds = 2</c>: the
    /// cards for rounds 2 and 4 are match ends and the cards for rounds 1 and 3 are not. Rounds 1
    /// and 3 are what make it a test rather than a tautology — an implementation that set
    /// <c>MatchOver</c> on every card, or on every card after the first, passes the "round 2 ends
    /// a match" half on its own.</summary>
    [Fact]
    public void AMatchEndsOnRoundsTwoAndFour_AndNotOnOneOrThree()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        var over = new bool[5];
        var matchIndex = new int[5];
        for (int round = 1; round <= 4; round++)
        {
            s = PlayRound(s, t, towers: round, seekSeconds: 4.5f);
            HideSeekTally card = Card(s);
            Assert.Equal(round, card.RoundIndex);
            over[round] = card.MatchOver;
            matchIndex[round] = card.MatchIndex;
            s = FinishTally(s, t);
        }

        Assert.False(over[1], "round 1 is the first half of match 1, not the end of anything");
        Assert.True(over[2], "round 2 ends match 1 — both players have now hidden once");
        Assert.False(over[3], "round 3 is the first half of match 2");
        Assert.True(over[4], "round 4 ends match 2");

        Assert.Equal(new[] { 1, 1, 2, 2 }, new[] { matchIndex[1], matchIndex[2], matchIndex[3], matchIndex[4] });
    }

    /// <summary><c>MatchRounds = 1</c>: every round is its own match. The knob's lower end, and
    /// the shape a "one round and done" variant would take without a line of new code.</summary>
    [Fact]
    public void AtOneRoundPerMatch_EveryRoundEndsAMatch()
    {
        HideSeekTuning t = Tune(matchRounds: 1);
        HideSeekState s = HideSeekLoop.Restart(t);

        for (int round = 1; round <= 3; round++)
        {
            s = PlayRound(s, t, towers: 1, seekSeconds: 4.5f);
            Assert.True(Card(s).MatchOver, $"round {round} should be a whole match at MatchRounds=1");
            Assert.Equal(round, Card(s).MatchIndex);
            s = FinishTally(s, t);
        }
    }

    /// <summary><c>MatchRounds = 3</c>: the match end moves with the knob rather than being
    /// pinned to "the second round". If this and the two-round case both pass, the arithmetic is
    /// reading the tuning.</summary>
    [Fact]
    public void AtThreeRoundsPerMatch_TheEndMovesToRoundThree()
    {
        HideSeekTuning t = Tune(matchRounds: 3);
        HideSeekState s = HideSeekLoop.Restart(t);

        var over = new bool[4];
        for (int round = 1; round <= 3; round++)
        {
            s = PlayRound(s, t, towers: 1, seekSeconds: 4.5f);
            over[round] = Card(s).MatchOver;
            s = FinishTally(s, t);
        }

        Assert.False(over[1]);
        Assert.False(over[2]);
        Assert.True(over[3]);
    }

    /// <summary><b>A zero match length is a divide by zero on the one line the whole feature
    /// hangs off</b>, and a tuning panel can type a zero. Floored at 1, exactly the way
    /// <c>MinTimerSec</c> is — and the floor is asserted through the LOOP, not just on the
    /// tuning record, because a floor applied in the record and forgotten at the read site is the
    /// failure mode.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ANonPositiveMatchLength_FloorsToOneRound(int configured)
    {
        HideSeekTuning t = Tune() with { MatchRounds = configured };
        Assert.Equal(1, t.MatchRoundsOrFloor);

        HideSeekState s = PlayRound(HideSeekLoop.Restart(t), t, towers: 1, seekSeconds: 4.5f);
        Assert.True(Card(s).MatchOver, "at the floor of one round per match, round 1 ends a match");
    }

    // ============================================================================================
    // Who won
    // ============================================================================================

    /// <summary><b>The winner is the higher total, and the totals are the ones on the card.</b>
    /// Two rounds with deliberately lopsided scoring, so the answer cannot be produced by
    /// accident: the hider of round 1 is the seeker of round 2, and the totals cross over.</summary>
    [Fact]
    public void TheWinnerIsWhoeverHasTheHigherTotal_AndTheCardCarriesBoth()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        // Round 1: Host hides and finishes 3; Joiner seeks and finds with 15 s left.
        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);
        Assert.Equal(3, s.ScoreOf(Host));
        Assert.Equal(15, s.ScoreOf(Joiner));

        // Round 2: the roles have swapped. Joiner hides and finishes 1; Host seeks, slower, and
        // finds with 10 s left.
        s = PlayRound(s, t, towers: 1, seekSeconds: 9.5f);
        HideSeekTally card = Card(s);

        Assert.True(card.MatchOver);
        Assert.Equal(Joiner, card.HiderPeerId);
        Assert.Equal(Host, card.SeekerPeerId);
        Assert.Equal(16, card.HiderTotal);      // 15 + 1
        Assert.Equal(13, card.SeekerTotal);     //  3 + 10
        Assert.Equal(Joiner, card.WinnerPeerId);

        // And the winner agrees with the live map, which is the thing a board would read.
        Assert.Equal(16, s.ScoreOf(Joiner));
        Assert.Equal(13, s.ScoreOf(Host));
    }

    /// <summary>Equal totals are a DRAW and a draw is peer 0 — never "the hider by default", and
    /// never the last player the loop happened to look at.</summary>
    [Fact]
    public void EqualTotalsAreADraw_AndADrawIsPeerZero()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        s = PlayRoundAndReset(s, t, towers: 5, seekSeconds: 4.5f);   // Host 5, Joiner 15
        s = PlayRound(s, t, towers: 0, seekSeconds: 9.5f);           // Joiner +0, Host +10

        HideSeekTally card = Card(s);
        Assert.True(card.MatchOver);
        Assert.Equal(15, card.HiderTotal);
        Assert.Equal(15, card.SeekerTotal);
        Assert.Equal(0, card.WinnerPeerId);
    }

    /// <summary>A card that is not a match end names no winner. The field means "who won the
    /// match" and a mid-match card has no answer — reporting whoever happens to be ahead would
    /// make the same field mean two things depending on a flag beside it.</summary>
    [Fact]
    public void AMidMatchCard_NamesNoWinner()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = PlayRound(HideSeekLoop.Restart(t), t, towers: 3, seekSeconds: 4.5f);

        HideSeekTally card = Card(s);
        Assert.False(card.MatchOver);
        Assert.Equal(0, card.WinnerPeerId);
        // The totals are still there: a board wants "3–15 so far" in the middle of a match.
        Assert.Equal(3, card.HiderTotal);
        Assert.Equal(15, card.SeekerTotal);
    }

    // ============================================================================================
    // The scores, and the Start that clears them
    // ============================================================================================

    /// <summary><b>The Start after a match clears the scores; the Start after round 1 does
    /// not.</b> Both halves in one test, because the second is the one that fails if the reset is
    /// hung off "a new round" instead of "a new match" — and that version passes every other test
    /// in this file.</summary>
    [Fact]
    public void TheStartAfterAMatchZeroesTheScores_AndTheStartAfterRoundOneDoesNot()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);   // Host 3, Joiner 15
        Assert.Equal(3, s.ScoreOf(Host));
        Assert.Equal(15, s.ScoreOf(Joiner));

        // The Start of round 2 — mid-match. The scores must survive it.
        s = Tick(s, Idle, t);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true }, t);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(3, s.ScoreOf(Host));
        Assert.Equal(15, s.ScoreOf(Joiner));

        // Finish round 2 (the match) and come to rest in the holding room.
        s = Tick(s, Idle with { HiderPressedConfirm = true }, t);
        for (int i = 0; i < (int)MathF.Round(4.5f / Dt); i++)
            s = Tick(s, Idle, t);
        s = Tick(s, Idle with { TargetInDropOff = true }, t);
        s = Tick(s, Idle with { TargetInDropOff = true, AnyPressedEnd = true }, t);
        Assert.True(Card(s).MatchOver);
        s = FinishTally(s, t);

        // THE HOLDING ROOM IS WHERE THE RESULT IS READ. The scores still stand here — the reset
        // is at the next Start, not at the reset edge, precisely so the board keeps the result up
        // while the two of them decide whether to go again.
        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.True(s.ScoreOf(Host) > 0 || s.ScoreOf(Joiner) > 0,
            "the totals must survive the reset edge; the holding room is where they are read");

        // The Start of round 3 — a new match.
        s = Tick(s, Idle, t);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true }, t);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(0, s.ScoreOf(Host));
        Assert.Equal(0, s.ScoreOf(Joiner));
    }

    /// <summary>The reset is ZERO ROWS, not an empty map. A board that showed a player only once
    /// they had scored would look broken for the whole first round of every match, and the
    /// absolute wire has to carry a row to carry a zero.</summary>
    [Fact]
    public void TheNewMatchKeepsARowForEveryoneAtZero()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);
        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);
        s = PlayRoundAndReset(s, t, towers: 1, seekSeconds: 9.5f);   // the match ends here

        s = Tick(s, Idle, t);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true }, t);

        Assert.Equal(2, s.Scores.Count);
        Assert.True(s.Scores.ContainsKey(Host));
        Assert.True(s.Scores.ContainsKey(Joiner));
        Assert.Equal(0, s.Scores[Host]);
        Assert.Equal(0, s.Scores[Joiner]);
    }

    /// <summary><b>The round index keeps counting across a match boundary</b> — round 3 is match
    /// 2's round 1, and the card says so both ways. A session that restarted the round index
    /// would make two different rounds both called "round 1" in the same log.</summary>
    [Fact]
    public void TheRoundIndexKeepsCounting_AndRoundThreeIsMatchTwosFirst()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);
        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);
        s = PlayRoundAndReset(s, t, towers: 1, seekSeconds: 9.5f);
        s = PlayRound(s, t, towers: 2, seekSeconds: 4.5f);

        HideSeekTally card = Card(s);
        Assert.Equal(3, card.RoundIndex);
        Assert.Equal(2, card.MatchIndex);
        Assert.Equal(1, t.RoundWithinMatch(card.RoundIndex));
        Assert.False(card.MatchOver);
    }

    /// <summary><b>Roles keep alternating across the match boundary</b>, which means the player
    /// who SOUGHT the last round of a match hides the first round of the next. The swap is at the
    /// reset edge and knows nothing about matches; this pins the consequence, because it is the
    /// thing a player notices and the thing a "new match resets everything" change would
    /// break.</summary>
    [Fact]
    public void TheWhoSoughtLastHidesFirstInTheNewMatch()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);   // round 1: Host hid
        s = PlayRoundAndReset(s, t, towers: 1, seekSeconds: 9.5f);   // round 2: Joiner hid; match over

        Assert.Equal(Host, s.HiderPeerId);     // Host sought round 2, so Host hides round 3
        Assert.Equal(Joiner, s.SeekerPeerId);
    }

    // ============================================================================================
    // How long the card holds
    // ============================================================================================

    /// <summary><b>The long Tally is applied only at a match end.</b> Measured as the armed
    /// value at the commit rather than by timing the phase, so the assertion is about the one
    /// line that chose it.</summary>
    [Fact]
    public void TheMatchTallyIsLonger_AndOnlyAtAMatchEnd()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        s = PlayRound(s, t, towers: 3, seekSeconds: 4.5f);
        Assert.False(Card(s).MatchOver);
        Assert.Equal(t.TallySec, s.RemainingSec, 0.001f);

        s = FinishTally(s, t);
        s = PlayRound(s, t, towers: 1, seekSeconds: 9.5f);
        Assert.True(Card(s).MatchOver);
        Assert.Equal(t.MatchTallySec, s.RemainingSec, 0.001f);
    }

    /// <summary>The match tally floors like every other timer. Same contract as
    /// <c>MinTimerSec</c>, applied where the value is armed.</summary>
    [Fact]
    public void AZeroMatchTally_FloorsToTheMinimum()
    {
        HideSeekTuning t = Tune() with { MatchTallySec = 0f };
        HideSeekState s = HideSeekLoop.Restart(t);
        s = PlayRoundAndReset(s, t, towers: 1, seekSeconds: 4.5f);
        s = PlayRound(s, t, towers: 1, seekSeconds: 4.5f);

        Assert.True(Card(s).MatchOver);
        Assert.Equal(HideSeekTuning.MinTimerSec, s.RemainingSec, 0.001f);
    }

    // ============================================================================================
    // The disconnect path
    // ============================================================================================

    /// <summary><b>A round ended by a disconnect still counts toward the match</b>, and a match
    /// whose last round ended that way is over with the totals as they stand. Both gains are zero
    /// on such a round, so "as they stand" needs no special case — which is what this asserts.</summary>
    [Fact]
    public void ARoundEndedByADisconnect_StillCountsTowardTheMatch()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);

        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);   // Host 3, Joiner 15
        int hostBefore = s.ScoreOf(Host);
        int joinerBefore = s.ScoreOf(Joiner);

        // Round 2 begins and then the hider walks out.
        s = Tick(s, Idle, t);
        s = Tick(s, Idle with { HostPressedStart = true, HiderHeldRackProp = true }, t);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        s = Tick(s, new HideSeekInput { HumanPeerIds = ImmutableArray.Create(Host) }, t);

        HideSeekTally card = Card(s);
        Assert.True(card.EndedByDisconnect);
        Assert.True(card.MatchOver, "round 2 ended the match, however it ended");
        Assert.Equal(2, card.RoundIndex);
        Assert.Equal(hostBefore, card.SeekerTotal);
        Assert.Equal(joinerBefore, card.HiderTotal);
        Assert.Equal(Joiner, card.WinnerPeerId);   // 15 still beats 3
    }

    // ============================================================================================
    // The wire
    // ============================================================================================

    /// <summary><b>Every match field survives Encode → Pack → Unpack → Fold</b>, which is the
    /// whole path a real message takes. Asserted field by field rather than by comparing two
    /// cards, so a failure names which one was dropped.</summary>
    [Fact]
    public void TheMatchResultRoundTripsTheWholeWire()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);
        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);
        s = PlayRound(s, t, towers: 1, seekSeconds: 9.5f);

        HideSeekTally server = Card(s);
        var roster = new[] { Host, Joiner };

        HideSeekWire wire = HideSeekWire.Encode(s, roster);
        var p = wire.Pack();
        HideSeekWire back = HideSeekWire.Unpack(p.Phase, p.Round, p.RemainingTenths, p.Hider,
            p.Seeker, p.ScorePeers, p.ScoreValues, p.Refusal, p.Towers, p.FoundTick,
            p.TallyRound, p.TallyHider, p.TallyHiderGain, p.TallySeeker, p.TallySeekerGain,
            p.TallyByDisconnect, p.TallyMatchOver, p.TallyMatchIndex, p.TallyWinner,
            p.TallyHiderTotal, p.TallySeekerTotal);
        Assert.Equal(wire, back);

        HideSeekView view = HideSeekWire.Fold(null, back);
        Assert.True(view.LastTally.HasValue);
        HideSeekTally client = view.LastTally!.Value;

        Assert.Equal(server.MatchOver, client.MatchOver);
        Assert.Equal(server.MatchIndex, client.MatchIndex);
        Assert.Equal(server.WinnerPeerId, client.WinnerPeerId);
        Assert.Equal(server.HiderTotal, client.HiderTotal);
        Assert.Equal(server.SeekerTotal, client.SeekerTotal);
        Assert.Equal(server, client);
    }

    /// <summary><b>A peer that arrives during the match card is complete from that one
    /// message</b> — the whole property the absolute wire exists for, re-asserted for the fields
    /// MATCH-1 added. <c>Fold(null, …)</c> is literally the late joiner: no prior view at
    /// all.</summary>
    [Fact]
    public void ALateJoinerIsCompleteFromOneMessage()
    {
        HideSeekTuning t = Tune();
        HideSeekState s = HideSeekLoop.Restart(t);
        s = PlayRoundAndReset(s, t, towers: 3, seekSeconds: 4.5f);
        s = PlayRound(s, t, towers: 1, seekSeconds: 9.5f);

        HideSeekWire wire = HideSeekWire.Encode(s, new[] { Host, Joiner });
        HideSeekView cold = HideSeekWire.Fold(null, wire);
        HideSeekView warm = HideSeekWire.Fold(HideSeekWire.Fold(null, wire), wire);

        Assert.Equal(cold, warm);
        Assert.True(cold.LastTally!.Value.MatchOver);
        Assert.Equal(Joiner, cold.LastTally!.Value.WinnerPeerId);
        Assert.Equal(16, cold.LastTally!.Value.HiderTotal);
        Assert.Equal(13, cold.LastTally!.Value.SeekerTotal);
        Assert.Equal(16, cold.ScoreOf(Joiner));
        Assert.Equal(13, cold.ScoreOf(Host));
    }

    /// <summary>Totals clamp on the wire exactly as the gains and the live scores do — an honest
    /// 255 rather than a wrapped byte reading 4 while the real total is 260. The WINNER is
    /// decided server-side from the unclamped ints, so a clamp can flatten the printed numbers
    /// but can never change who is named.</summary>
    [Fact]
    public void AnAbsurdTotal_ClampsOnTheWireWithoutMovingTheWinner()
    {
        var card = new HideSeekTally(2, Host, 4, Joiner, 9, false,
            MatchOver: true, MatchIndex: 1, WinnerPeerId: Host, HiderTotal: 900, SeekerTotal: 260);
        HideSeekState s = HideSeekLoop.Restart(Tune()) with { LastTally = card };

        HideSeekView view = HideSeekWire.Fold(null,
            HideSeekWire.Encode(s, new[] { Host, Joiner }));

        Assert.Equal(255, view.LastTally!.Value.HiderTotal);
        Assert.Equal(255, view.LastTally!.Value.SeekerTotal);
        Assert.Equal(Host, view.LastTally!.Value.WinnerPeerId);
    }

    // ============================================================================================
    // The copy
    // ============================================================================================

    private static string Name(int id) => id == Host ? "ADA" : id == Joiner ? "BEN" : string.Empty;

    /// <summary>A card shaped by hand, so the expected string is unambiguous rather than derived
    /// from the same arithmetic it is meant to check.</summary>
    private static HideSeekTally RoundOneCard() =>
        new(1, Host, 3, Joiner, 15, false, MatchOver: false, MatchIndex: 1,
            WinnerPeerId: 0, HiderTotal: 3, SeekerTotal: 15);

    private static HideSeekTally MatchWonCard() =>
        new(2, Joiner, 1, Host, 10, false, MatchOver: true, MatchIndex: 1,
            WinnerPeerId: Joiner, HiderTotal: 16, SeekerTotal: 13);

    private static HideSeekTally MatchDrawnCard() =>
        new(2, Joiner, 0, Host, 10, false, MatchOver: true, MatchIndex: 1,
            WinnerPeerId: 0, HiderTotal: 15, SeekerTotal: 15);

    [Fact]
    public void TheRoundTallyLine_SaysWhoDidWhatInTheOrderTheyDidIt() =>
        Assert.Equal("ROUND 1 OF 2 · ADA hid · 3 sorted · found at 0:05 · BEN +15",
            HideSeekText.RoundTallyLine(RoundOneCard(), Name, Tune()));

    /// <summary>"found at" is the ELAPSED seek, reconstructed from the gain — the two ends of one
    /// fact. At a 20 s seek a gain of 15 is a find at 0:05; the shipping 180 s tuning turns the
    /// same gain into 2:45, which is what makes this a function of the tuning rather than a
    /// constant.</summary>
    [Fact]
    public void FoundAt_IsTheElapsedSeekAndTracksTheTuning() =>
        Assert.Contains("found at 2:45",
            HideSeekText.RoundTallyLine(RoundOneCard(), Name, HideSeekTuning.Default));

    /// <summary>A seek that timed out gains nothing and is reported as NOT FOUND. The naive
    /// arithmetic would print "found at 3:00" — a lie about the one instant this game is built
    /// around, and one that reads perfectly plausibly.</summary>
    [Fact]
    public void ASeekThatTimedOut_SaysNotFound()
    {
        var card = new HideSeekTally(1, Host, 3, Joiner, 0, false, MatchOver: false, MatchIndex: 1,
            WinnerPeerId: 0, HiderTotal: 3, SeekerTotal: 0);
        string line = HideSeekText.RoundTallyLine(card, Name, Tune());
        Assert.Contains("not found", line);
        Assert.DoesNotContain("found at", line);
    }

    [Fact]
    public void TheMatchTallyLine_NamesTheWinnerAndPutsTheirScoreFirst() =>
        Assert.Equal("MATCH 1 · BEN WINS 16–13", HideSeekText.MatchTallyLine(MatchWonCard(), Name));

    /// <summary>The winner's total prints FIRST whichever role they held. Round 2's winner is the
    /// HIDER on the card above; here the same result is rebuilt with the seeker winning, and the
    /// order must not follow the roles.</summary>
    [Fact]
    public void TheWinnersTotalPrintsFirst_WhicheverRoleTheyHeld()
    {
        var card = new HideSeekTally(2, Joiner, 1, Host, 10, false, MatchOver: true,
            MatchIndex: 1, WinnerPeerId: Host, HiderTotal: 4, SeekerTotal: 20);
        Assert.Equal("MATCH 1 · ADA WINS 20–4", HideSeekText.MatchTallyLine(card, Name));
    }

    [Fact]
    public void TheMatchTallyLine_SaysDrawWithBothTotals() =>
        Assert.Equal("MATCH 1 · DRAW 15–15", HideSeekText.MatchTallyLine(MatchDrawnCard(), Name));

    [Fact]
    public void TheHoldingLine_KeepsTheResultUpAndPointsAtTheNextMatch() =>
        Assert.Equal("MATCH 1 · BEN WON 16–13 · START FOR MATCH 2",
            HideSeekText.MatchHoldingLine(MatchWonCard(), Name));

    [Fact]
    public void TheHoldingLine_AfterADraw_StillPointsAtTheNextMatch() =>
        Assert.Equal("MATCH 1 · DRAW 15–15 · START FOR MATCH 2",
            HideSeekText.MatchHoldingLine(MatchDrawnCard(), Name));

    /// <summary>A peer with no avatar in the tree still gets a name rather than a hole in the
    /// sentence. One fallback, in one place — <see cref="HideSeekText.PlayerName"/> — because two
    /// would print two different names for one player.</summary>
    [Fact]
    public void AnUnresolvableName_FallsBackToThePeerId()
    {
        Assert.Equal($"PLAYER {Host}", HideSeekText.PlayerName(null, Host));
        Assert.Equal($"PLAYER {Host}", HideSeekText.PlayerName(_ => string.Empty, Host));
        Assert.Equal($"PLAYER {Host}", HideSeekText.PlayerName(_ => "   ", Host));
        Assert.Equal("ADA", HideSeekText.PlayerName(Name, Host));
        Assert.Contains($"PLAYER {Joiner}", HideSeekText.MatchTallyLine(MatchWonCard(), null));
    }

    /// <summary><b>A disconnect replaces the result and keeps the locator.</b> On both lines:
    /// the score is not a result when one of the two walked out, but a player looking up
    /// mid-sentence still needs to know where in the match they are.</summary>
    [Fact]
    public void ADisconnect_ReplacesTheResultAndKeepsTheLocator()
    {
        var round = new HideSeekTally(1, Host, 0, Joiner, 0, true, MatchOver: false,
            MatchIndex: 1, WinnerPeerId: 0, HiderTotal: 0, SeekerTotal: 0);
        var match = new HideSeekTally(2, Host, 0, Joiner, 0, true, MatchOver: true,
            MatchIndex: 1, WinnerPeerId: Joiner, HiderTotal: 3, SeekerTotal: 15);

        Assert.Equal("ROUND 1 OF 2 · ended: BEN left",
            HideSeekText.RoundTallyLine(round, Name, Tune(), leaverPeerId: Joiner));
        Assert.Equal("MATCH 1 · ended: ADA left",
            HideSeekText.MatchTallyLine(match, Name, leaverPeerId: Host));
        Assert.Equal("MATCH 1 · ended: a player left",
            HideSeekText.MatchTallyLine(match, Name));
    }

    /// <summary>The HOLDING line after a disconnect-ended match still shows the result, unlike
    /// the Tally line. The match is over with the totals as they stand, and "ended: someone left"
    /// tells the survivor nothing they do not already know while hiding the score that is still
    /// on the board.</summary>
    [Fact]
    public void TheHoldingLine_AfterADisconnectEndedMatch_StillShowsTheResult()
    {
        var match = new HideSeekTally(2, Host, 0, Joiner, 0, true, MatchOver: true,
            MatchIndex: 1, WinnerPeerId: Joiner, HiderTotal: 3, SeekerTotal: 15);
        Assert.Equal("MATCH 1 · BEN WON 15–3 · START FOR MATCH 2",
            HideSeekText.MatchHoldingLine(match, Name));
    }

    // --- the dispatcher -------------------------------------------------------------------

    private static HideSeekView ViewAt(HideSeekPhase phase, HideSeekTally? card,
        params int[] present)
    {
        ImmutableDictionary<int, int> scores = ImmutableDictionary<int, int>.Empty;
        foreach (int p in present)
            scores = scores.SetItem(p, 0);
        return new HideSeekView(phase, 2, 0f, Host, Joiner, scores, HideSeekRefusal.None,
            0, HideSeekWire.NoFoundTick, card);
    }

    /// <summary><see cref="HideSeekText.MatchLine"/> is the one entry point, and it returns EMPTY
    /// rather than a plausible sentence wherever there is nothing match-shaped to say. That is
    /// what lets a caller test the string instead of re-deriving the phase rules that picked
    /// it.</summary>
    [Fact]
    public void MatchLine_SpeaksOnlyWhereThereIsSomethingToSay()
    {
        HideSeekTuning t = Tune();

        Assert.Equal(string.Empty,
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Tally, null, Host, Joiner), Name, t));
        Assert.Equal(string.Empty,
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Seeking, MatchWonCard(), Host, Joiner), Name, t));
        Assert.Equal(string.Empty,
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Hiding, MatchWonCard(), Host, Joiner), Name, t));
        // Holding after an ORDINARY round: the strip's own phase line is the right thing there.
        Assert.Equal(string.Empty,
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Holding, RoundOneCard(), Host, Joiner), Name, t));

        Assert.Equal("ROUND 1 OF 2 · ADA hid · 3 sorted · found at 0:05 · BEN +15",
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Tally, RoundOneCard(), Host, Joiner), Name, t));
        Assert.Equal("MATCH 1 · BEN WINS 16–13",
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Tally, MatchWonCard(), Host, Joiner), Name, t));
        Assert.Equal("MATCH 1 · BEN WON 16–13 · START FOR MATCH 2",
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Holding, MatchWonCard(), Host, Joiner), Name, t));
    }

    /// <summary><b>Who left, recovered rather than guessed.</b> The card knows a role holder went
    /// but not which one; the score map carries a row for every peer still on the roster and none
    /// for one who is gone. Two rows missing names nobody, and says so.</summary>
    [Fact]
    public void MatchLine_RecoversTheLeaverFromTheScoreMap()
    {
        HideSeekTuning t = Tune();
        var card = new HideSeekTally(1, Host, 0, Joiner, 0, true, MatchOver: false,
            MatchIndex: 1, WinnerPeerId: 0, HiderTotal: 0, SeekerTotal: 0);

        Assert.Equal("ROUND 1 OF 2 · ended: BEN left",
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Tally, card, Host), Name, t));
        Assert.Equal("ROUND 1 OF 2 · ended: ADA left",
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Tally, card, Joiner), Name, t));
        Assert.Equal("ROUND 1 OF 2 · ended: a player left",
            HideSeekText.MatchLine(ViewAt(HideSeekPhase.Tally, card), Name, t));
    }

    /// <summary><b>The strip overload picks for the widget.</b> Where the match has nothing to
    /// say it falls through to the phase/clock/role/round line the strip has always drawn, and
    /// where it does the card wins. The widget makes no choice of its own, so CLOCK-1's clock and
    /// HOLD-1's board cannot make a different one.</summary>
    [Fact]
    public void TheStripOverload_FallsThroughToThePhaseLine()
    {
        HideSeekTuning t = Tune();

        HideSeekView seeking = ViewAt(HideSeekPhase.Seeking, RoundOneCard(), Host, Joiner) with
        {
            RemainingSec = 161f,
        };
        Assert.Equal("SEEKING · 2:41 · YOU HIDE · ROUND 2",
            HideSeekText.StripLine(seeking, Host, Name, t));

        Assert.Equal("MATCH 1 · BEN WINS 16–13",
            HideSeekText.StripLine(ViewAt(HideSeekPhase.Tally, MatchWonCard(), Host, Joiner),
                Host, Name, t));
    }

    /// <summary>The match arithmetic, as a table. Every other test in this file goes through the
    /// loop; this one pins the four helpers directly, because they are what
    /// <see cref="HideSeekText"/>, <c>HideSeekLoop</c> and <c>HideSeekDriver</c> all read and a
    /// disagreement between them would be invisible in any single one.</summary>
    [Theory]
    [InlineData(1, 2, false, true, 1, 1)]
    [InlineData(2, 2, true, false, 1, 2)]
    [InlineData(3, 2, false, true, 2, 1)]
    [InlineData(4, 2, true, false, 2, 2)]
    [InlineData(1, 1, true, true, 1, 1)]
    [InlineData(5, 3, false, false, 2, 2)]
    [InlineData(6, 3, true, false, 2, 3)]
    [InlineData(7, 3, false, true, 3, 1)]
    public void TheMatchArithmetic(int round, int matchRounds, bool last, bool first,
        int matchIndex, int within)
    {
        HideSeekTuning t = Tune(matchRounds);
        Assert.Equal(last, t.IsLastRoundOfMatch(round));
        Assert.Equal(first, t.IsFirstRoundOfMatch(round));
        Assert.Equal(matchIndex, t.MatchIndexOf(round));
        Assert.Equal(within, t.RoundWithinMatch(round));
    }
}
