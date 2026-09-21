using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MpFoundation.Game.Round;
using MpFoundation.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>SOLO-1 (2026-09-20): one player can run the whole loop alone, and a third player waits.</b>
///
/// <para>Talon, 2026-09-20: <i>"I would like the ability to play the game through, only for
/// testing, with one player. If both players start in the room, that's how a normal game begins;
/// with two or more players, they can wait in the room."</i> Two separate asks, and they meet in
/// one place — <see cref="HideSeekLoop"/>'s idea of who is in this round — which is why they are
/// tested in one file.</para>
///
/// <list type="number">
/// <item><b>Solo is a DEV FLAG</b> (<see cref="HideSeekTuning.Solo"/>, set by <c>--solo</c>), and
/// every assertion here that turns it on has a twin below it that leaves it off and shows the
/// shipped behaviour is untouched. That pairing is the packet's own rule — "without
/// <c>--solo</c>, nothing changes" — and a one-sided test of a flag proves only that the flag
/// exists.</item>
/// <item><b>A third player is NOT a refusal.</b> Before this packet the loop demanded exactly two
/// humans and a third body in the room stopped the game for everybody; now the round runs for the
/// two who hold the roles and the rest wait, which is what Talon asked for in the second
/// sentence.</item>
/// <item><b>Nothing new rides the wire.</b> WAITING is derived from the two role ids the message
/// already carries, so there is no <c>ProtocolVersion</c> bump owed for any of it — see
/// <see cref="AWaitingPlayerIsDerivedFromTheRolesAlreadyOnTheWire"/>.</item>
/// </list>
/// </summary>
public class SoloRoundTests
{
    private const float Dt = 1f / 60f;

    private const int Alone = 11;
    private const int Second = 22;
    private const int Third = 33;

    private static HideSeekTuning Shipped => HideSeekTuning.Default;
    private static HideSeekTuning SoloTuning => HideSeekTuning.Default with { Solo = true };

    private static HideSeekState Tick(HideSeekState s, HideSeekInput input, in HideSeekTuning t) =>
        HideSeekLoop.Step(s, input, Dt, t);

    private static HideSeekInput Roster(params int[] peers) =>
        new() { HumanPeerIds = ImmutableArray.Create(peers) };

    /// <summary>Idle-ticks until the phase changes, so a test can assert on the transition tick
    /// itself. Same helper (and same budget) as <c>HideSeekLoopTests</c>.</summary>
    private static HideSeekState RunUntilPhaseLeaves(HideSeekState s, HideSeekInput input,
        in HideSeekTuning t, int maxTicks = 60 * 400)
    {
        HideSeekPhase from = s.Phase;
        for (int i = 0; i < maxTicks; i++)
        {
            s = Tick(s, input, t);
            if (s.Phase != from)
                return s;
        }
        Assert.Fail($"phase {from} never left after {maxTicks} ticks");
        return s;
    }

    private static HideSeekTally Card(in HideSeekState s)
    {
        Assert.True(s.LastTally.HasValue, "no card was computed");
        return s.LastTally!.Value;
    }

    /// <summary>A solo session sitting in Holding with the roles already folded.</summary>
    private static HideSeekState SoloHolding()
    {
        HideSeekState s = HideSeekLoop.Restart(SoloTuning);
        return Tick(s, Roster(Alone), SoloTuning);
    }

    // =========================================================================================
    // 1. The flag, and the shipped behaviour beside it
    // =========================================================================================

    /// <summary>The whole ask, in one assertion: under <c>--solo</c> the one peer in the room may
    /// press Start and the round begins.</summary>
    [Fact]
    public void Solo_StartWithOnePlayer_BeginsTheRound()
    {
        HideSeekState s = SoloHolding();
        s = Tick(s, Roster(Alone) with { HostPressedStart = true, HiderHeldRackProp = true },
            SoloTuning);

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(HideSeekRefusal.None, s.Refusal);
    }

    /// <summary><b>The twin.</b> The same press on the shipped tuning is still refused with the
    /// same sentence it has always been refused with.</summary>
    [Fact]
    public void WithoutSolo_StartWithOnePlayer_IsStillRefusedNeedTwoPlayers()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone), Shipped);
        s = Tick(s, Roster(Alone) with { HostPressedStart = true, HiderHeldRackProp = true },
            Shipped);

        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.Equal(HideSeekRefusal.NeedTwoPlayers, s.Refusal);
    }

    /// <summary>
    /// <b>One peer holds BOTH roles, and that is the whole of the solo model.</b> Everything
    /// downstream — the teleports, the card, the swap, the board — falls out of it, and nothing
    /// else in the loop learns that a session is solo.
    /// </summary>
    [Fact]
    public void Solo_TheOnePlayerIsBothTheHiderAndTheSeeker()
    {
        HideSeekState s = SoloHolding();
        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(Alone, s.SeekerPeerId);
    }

    /// <summary>The twin: a roster of one on the shipped tuning still leaves the seeker slot
    /// empty, which is what makes <c>NeedTwoPlayers</c> reachable at all.</summary>
    [Fact]
    public void WithoutSolo_ARosterOfOne_StillLeavesTheSeekerSlotEmpty()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone), Shipped);
        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(0, s.SeekerPeerId);
    }

    /// <summary><b>Solo never overrides a real two-player room.</b> With the flag on and two
    /// people present the roles are dealt exactly as they always were, so a host who left
    /// <c>--solo</c> on their dev server does not get a broken two-player game.</summary>
    [Fact]
    public void Solo_WithTwoPlayersPresent_DealsTheOrdinaryTwoRoles()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(SoloTuning), Roster(Alone, Second), SoloTuning);
        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(Second, s.SeekerPeerId);
    }

    // =========================================================================================
    // 2. The solo round, every edge of it
    // =========================================================================================

    /// <summary>Confirm moves a solo session to Seeking with the same peer as the seeker. The
    /// room it is sent to is <c>HideSeekDriver</c>'s business; what the loop owes is a Seeking
    /// phase whose seeker is somebody who is actually here.</summary>
    [Fact]
    public void Solo_Confirm_EntersSeekingWithTheSamePeerSeeking()
    {
        HideSeekState s = SoloHolding();
        s = Tick(s, Roster(Alone) with { HostPressedStart = true, HiderHeldRackProp = true },
            SoloTuning);
        s = Tick(s, Roster(Alone) with { HiderPressedConfirm = true }, SoloTuning);

        Assert.Equal(HideSeekPhase.Seeking, s.Phase);
        Assert.Equal(Alone, s.SeekerPeerId);
        Assert.Equal(s.HiderPeerId, s.SeekerPeerId);
    }

    /// <summary>
    /// <b>A solo round is not a disconnect.</b> The mid-round guard ends the round the instant a
    /// role holder is missing, and "the hider and the seeker are the same person" must not read
    /// as one of them having left — which is exactly what a naive <c>hider != seeker</c>
    /// invariant would do, silently, on the first tick of every solo round.
    /// </summary>
    [Fact]
    public void Solo_TheRoundDoesNotEndAsADisconnectBecauseOneBodyHoldsBothRoles()
    {
        HideSeekState s = SoloHolding();
        s = Tick(s, Roster(Alone) with { HostPressedStart = true, HiderHeldRackProp = true },
            SoloTuning);
        for (int i = 0; i < 120; i++)
            s = Tick(s, Roster(Alone), SoloTuning);

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Null(s.LastTally);
    }

    /// <summary>The find freezes both halves of the score onto one peer, and the card credits
    /// that peer with both gains — it hid and it sought, so it earns both.</summary>
    [Fact]
    public void Solo_TheCardCreditsTheOnePeerWithBothGains()
    {
        HideSeekState s = SoloRoundToTogether(sorts: 3);
        Assert.Equal(HideSeekPhase.Together, s.Phase);

        s = Tick(s, Roster(Alone) with { SortsCompleted = 3, AnyPressedEnd = true }, SoloTuning);
        HideSeekTally card = Card(s);

        Assert.Equal(HideSeekPhase.Tally, s.Phase);
        Assert.Equal(Alone, card.HiderPeerId);
        Assert.Equal(Alone, card.SeekerPeerId);
        Assert.Equal(3, card.HiderGained);
        Assert.True(card.SeekerGained > 0, "the seek clock froze at zero on a find");
        Assert.Equal(card.HiderGained + card.SeekerGained, s.ScoreOf(Alone));
    }

    /// <summary>The reset edge swaps the roles onto the same peer and the next round runs, so a
    /// solo tester gets round two rather than a session that stops after one.</summary>
    [Fact]
    public void Solo_TheRolesSwapToTheSamePeer_AndRoundTwoRuns()
    {
        HideSeekState s = SoloRoundToTogether(sorts: 3);
        s = Tick(s, Roster(Alone) with { AnyPressedEnd = true }, SoloTuning);
        s = RunUntilPhaseLeaves(s, Roster(Alone), SoloTuning);   // Tally -> Holding

        Assert.Equal(HideSeekPhase.Holding, s.Phase);
        Assert.True(s.ResetRequested);
        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(Alone, s.SeekerPeerId);
        Assert.Equal(2, s.RoundIndex);

        s = Tick(s, Roster(Alone) with { HostPressedStart = true, HiderHeldRackProp = true },
            SoloTuning);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
    }

    /// <summary>
    /// <b>The match card on a one-row board is a DRAW, and it is arithmetic rather than a special
    /// case.</b> Both totals are copied out of the same peer's row, so they are equal, so there is
    /// no winner to name — which is the honest answer when one person played both sides. Nothing
    /// crashes, nothing divides by a missing second player, and the card still carries the real
    /// totals so the board can print them.
    /// </summary>
    [Fact]
    public void Solo_TheMatchCardIsADrawBetweenTheOnePeersTwoHalves()
    {
        HideSeekState s = SoloRoundToTogether(sorts: 3);
        s = Tick(s, Roster(Alone) with { SortsCompleted = 3, AnyPressedEnd = true }, SoloTuning);
        s = RunUntilPhaseLeaves(s, Roster(Alone), SoloTuning);            // round 1 card -> Holding

        s = SoloRoundToTogether(sorts: 1, from: s);
        s = Tick(s, Roster(Alone) with { SortsCompleted = 1, AnyPressedEnd = true }, SoloTuning);

        HideSeekTally card = Card(s);
        Assert.True(card.MatchOver, "two rounds is a whole match at the shipping tuning");
        Assert.Equal(0, card.WinnerPeerId);
        Assert.Equal(card.HiderTotal, card.SeekerTotal);
        Assert.True(card.HiderTotal > 0, "the one peer scored nothing across two whole rounds");
    }

    /// <summary>A solo board has exactly one row and nobody on it is waiting: the one peer holds
    /// both roles, so there is no third seat to be out of.</summary>
    [Fact]
    public void Solo_TheBoardHasOneRowAndNobodyIsWaiting()
    {
        HideSeekView view = ViewOf(SoloHolding());
        IReadOnlyList<HoldingBoardRow> rows = HoldingBoardModel.Rows(view, Alone, null);

        Assert.Single(rows);
        Assert.Equal(0, HoldingBoardModel.WaitingCount(view));
        Assert.DoesNotContain(HoldingBoardModel.Waiting, rows[0].Role);
    }

    /// <summary><b>A second player arriving during a solo round waits, and is dealt in at the next
    /// Holding.</b> The round in flight is not disturbed (the one peer keeps both roles to the
    /// end) and the ordinary two-player deal resumes by itself, because the pair the loop is
    /// holding stops being valid the moment a real seeker is available.</summary>
    [Fact]
    public void Solo_ASecondPlayerArrivingMidRound_WaitsAndIsDealtInAtTheNextHolding()
    {
        HideSeekState s = SoloHolding();
        s = Tick(s, Roster(Alone) with { HostPressedStart = true, HiderHeldRackProp = true },
            SoloTuning);

        // The second body arrives. The round must not end and must not reassign anything.
        s = Tick(s, Roster(Alone, Second), SoloTuning);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(Alone, s.SeekerPeerId);
        Assert.Equal(HoldingBoardModel.Waiting,
            HoldingBoardModel.RoleCell(ViewOf(s), Second, isSelf: true));

        // Run the round out and come back to Holding.
        s = Tick(s, Roster(Alone, Second) with { HiderPressedConfirm = true }, SoloTuning);
        s = Tick(s, Roster(Alone, Second) with { TargetInDropOff = true }, SoloTuning);
        s = Tick(s, Roster(Alone, Second) with { AnyPressedEnd = true }, SoloTuning);
        s = RunUntilPhaseLeaves(s, Roster(Alone, Second), SoloTuning);
        Assert.Equal(HideSeekPhase.Holding, s.Phase);

        // ONE MORE TICK, and it is the contract rather than a nicety. Facts are folded BEFORE the
        // phase switch (HideSeekLoop's stated order), so on the Tally -> Holding tick the fold
        // still saw Tally and the Holding role repair had not run yet. The peer that arrived is
        // therefore dealt in on the first tick of Holding, which is the first tick anybody could
        // press Start on anyway.
        s = Tick(s, Roster(Alone, Second), SoloTuning);

        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(Second, s.SeekerPeerId);
        Assert.Equal(0, HoldingBoardModel.WaitingCount(ViewOf(s)));
    }

    /// <summary>
    /// <b>The voice room must be the room the body is actually in</b> (VOICE-1's whole
    /// guarantee), and in solo the two answers came apart. <c>RoundRooms.RoomOf</c> tests
    /// <c>isHider</c> first, so a peer holding BOTH roles read as the hider and was placed in the
    /// TASK room during Seeking — while <c>HideSeekDriver</c> teleports it to the SEARCH room.
    ///
    /// <para>Inaudible today (there is nobody else in a solo session to be routed to) and NOT
    /// inaudible the moment somebody joins one, which is a case this packet deliberately
    /// supports. The seeking half wins wherever both roles are held, because that is what the
    /// driver does with the body — the resolver is supposed to be derivable from phase and role
    /// precisely so it never has to disagree with a position.</para>
    /// </summary>
    [Fact]
    public void Solo_TheVoiceRoomIsTheRoomTheDriverActuallyPutsTheBodyIn()
    {
        Assert.Equal(SupermarketWorld.HoldingRoom,
            RoundRooms.RoomOf(HideSeekPhase.Holding, Alone, Alone, Alone));
        Assert.Equal(SupermarketWorld.SearchRoom,
            RoundRooms.RoomOf(HideSeekPhase.Hiding, Alone, Alone, Alone));
        // The one that was wrong: the driver sends it to the search room to seek.
        Assert.Equal(SupermarketWorld.SearchRoom,
            RoundRooms.RoomOf(HideSeekPhase.Seeking, Alone, Alone, Alone));
        Assert.Equal(SupermarketWorld.TaskRoom,
            RoundRooms.RoomOf(HideSeekPhase.Together, Alone, Alone, Alone));
    }

    /// <summary>The twin: with two distinct players the table is exactly what VOICE-1 wrote, so
    /// the fix above cannot have moved an ordinary session's routing.</summary>
    [Fact]
    public void TwoDistinctRoles_KeepVoiceOnesOwnTable()
    {
        Assert.Equal(SupermarketWorld.SearchRoom,
            RoundRooms.RoomOf(HideSeekPhase.Hiding, Alone, Second, Alone));
        Assert.Equal(SupermarketWorld.HoldingRoom,
            RoundRooms.RoomOf(HideSeekPhase.Hiding, Alone, Second, Second));
        Assert.Equal(SupermarketWorld.TaskRoom,
            RoundRooms.RoomOf(HideSeekPhase.Seeking, Alone, Second, Alone));
        Assert.Equal(SupermarketWorld.SearchRoom,
            RoundRooms.RoomOf(HideSeekPhase.Seeking, Alone, Second, Second));
    }

    /// <summary>A waiting player is in the HOLDING room in every phase — the round never moves
    /// them, so proximity voice works with anyone else standing there and the two who are playing
    /// are on the intercom. Unchanged by this packet; asserted because the waiting room is now a
    /// supported state rather than an accident.</summary>
    [Theory]
    [InlineData(HideSeekPhase.Holding)]
    [InlineData(HideSeekPhase.Hiding)]
    [InlineData(HideSeekPhase.Seeking)]
    [InlineData(HideSeekPhase.Together)]
    public void AWaitingPlayerIsInTheHoldingRoomInEveryPhase(HideSeekPhase phase)
    {
        Assert.Equal(SupermarketWorld.HoldingRoom,
            RoundRooms.RoomOf(phase, Alone, Second, Third));
    }

    // =========================================================================================
    // 3. Three or more players: the round runs and the rest wait
    // =========================================================================================

    /// <summary>
    /// <b>Three humans start the round; they do not block it.</b> This REPLACES ROUND-1's
    /// "exactly two, not at least two" refusal, on Talon's 2026-09-20 ruling — a third person
    /// standing in the holding room used to stop the game for the two who were playing, which is
    /// the opposite of "they can wait in the room". The pair is still exactly two: the third peer
    /// holds no role.
    /// </summary>
    [Fact]
    public void Start_WithThreeHumans_RunsForTheFirstTwo_AndTheThirdHoldsNoRole()
    {
        HideSeekInput three = Roster(Alone, Second, Third);
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), three, Shipped);
        s = Tick(s, three with { HostPressedStart = true, HiderHeldRackProp = true }, Shipped);

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(HideSeekRefusal.None, s.Refusal);
        Assert.Equal(Alone, s.HiderPeerId);
        Assert.Equal(Second, s.SeekerPeerId);
        Assert.NotEqual(Third, s.HiderPeerId);
        Assert.NotEqual(Third, s.SeekerPeerId);
    }

    /// <summary>The waiting player still owns a score row, because a board that showed a player
    /// only once they had scored would look broken for the whole of their wait.</summary>
    [Fact]
    public void AWaitingPlayer_StillOwnsAScoreRowAtZero()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone, Second, Third), Shipped);
        Assert.Equal(3, s.Scores.Count);
        Assert.Equal(0, s.ScoreOf(Third));
    }

    /// <summary>
    /// <b>The board says WAITING, not an em dash.</b> HOLD-1 printed <c>—</c> for a peer with no
    /// role, on the argument that a blank cell reads as a readout that failed. It is a true
    /// statement and it answers the wrong question: the reader wants to know whether they are in
    /// this game, and a dash does not say.
    /// </summary>
    [Fact]
    public void AThirdPlayersRowReadsWaiting()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone, Second, Third), Shipped);
        HideSeekView view = ViewOf(s);

        IReadOnlyList<HoldingBoardRow> rows = HoldingBoardModel.Rows(view, Third, null);
        HoldingBoardRow mine = rows.Single(r => r.PeerId == Third);

        // THE LITERAL WORD, spelled once here and nowhere else in this file. Every other
        // assertion about this cell compares against HoldingBoardModel.Waiting, which is the
        // right thing for a STRUCTURAL check and is satisfied by any value at all — measured:
        // renaming the constant to "PLANTED" left all of them green. This is the copy contract,
        // and it is the half a reader of the board actually experiences.
        Assert.Equal("WAITING", mine.Role);
        Assert.Equal(HoldingBoardModel.Waiting, mine.Role);
        Assert.Equal(1, HoldingBoardModel.WaitingCount(view));
    }

    /// <summary>The two who ARE playing keep their ordinary cells, so the WAITING row is a
    /// distinction the reader can actually use.</summary>
    [Fact]
    public void TheTwoPlayersKeepTheirOwnRoleCellsWhileAThirdWaits()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone, Second, Third), Shipped);
        HideSeekView view = ViewOf(s);

        Assert.Equal("NEXT: YOU HIDE", HoldingBoardModel.RoleCell(view, Alone, isSelf: true));
        Assert.Equal("NEXT: SEEKS", HoldingBoardModel.RoleCell(view, Second, isSelf: false));
        Assert.Equal(HoldingBoardModel.Waiting, HoldingBoardModel.RoleCell(view, Third, isSelf: false));
    }

    /// <summary>
    /// <b>The status line says HOW a waiting player gets in.</b> "WAITING" on its own is a state
    /// with no exit written beside it, which is the INTERACTION-BIBLE §5 failure one level up from
    /// a button: it says what is wrong and not what to do about it. The line the board prints under
    /// the rows answers it.
    /// </summary>
    [Fact]
    public void TheStatusLine_TellsAWaitingPlayerWhenTheyGetIn()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone, Second, Third), Shipped);
        string line = HoldingBoardModel.StatusLine(ViewOf(s));

        Assert.Contains("1 WAITING", line);
        Assert.Contains("SEAT", line);
    }

    /// <summary>With nobody waiting and nobody overflowing, the status line is EMPTY — the same
    /// contract every other copy function in this game has, so the board hides it on empty rather
    /// than printing a reassurance nobody needs.</summary>
    [Fact]
    public void TheStatusLine_IsEmptyWhenEverybodyIsPlaying()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone, Second), Shipped);
        Assert.Equal(string.Empty, HoldingBoardModel.StatusLine(ViewOf(s)));
    }

    /// <summary>The overflow count survives beside the waiting count: a board with more humans
    /// than rows still says how many it could not show.</summary>
    [Fact]
    public void TheStatusLine_KeepsSayingHowManyRowsItCouldNotShow()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped),
            Roster(Alone, Second, Third, 44, 55), Shipped);
        string line = HoldingBoardModel.StatusLine(ViewOf(s));

        Assert.Contains("+1 MORE", line);
        Assert.Contains("3 WAITING", line);
    }

    /// <summary>
    /// <b>Nothing new rides the wire for any of this.</b> WAITING is a function of the two role
    /// ids <see cref="HideSeekWire"/> has carried since ROUND-1, so a message encoded from a
    /// three-player state folds back into a view that knows who is waiting — with no field added
    /// and therefore no <c>NetProfile.ProtocolVersion</c> bump owed.
    /// </summary>
    [Fact]
    public void AWaitingPlayerIsDerivedFromTheRolesAlreadyOnTheWire()
    {
        HideSeekState s = Tick(HideSeekLoop.Restart(Shipped), Roster(Alone, Second, Third), Shipped);
        HideSeekView folded = HideSeekWire.Fold(null,
            HideSeekWire.Encode(s, new[] { Alone, Second, Third }));

        Assert.Equal(1, HoldingBoardModel.WaitingCount(folded));
        Assert.Equal(HoldingBoardModel.Waiting,
            HoldingBoardModel.RoleCell(folded, Third, isSelf: true));
    }

    // =========================================================================================
    // 4. The buttons: a waiting player may not run the round
    // =========================================================================================

    /// <summary>
    /// <b>Every one of the three buttons answers a waiting player by name.</b> Not
    /// <see cref="PressRefusal.NotNow"/> (the phase is right), not
    /// <see cref="PressRefusal.NotYourButton"/> (that is the hider/seeker distinction, a different
    /// fact) — a sentence about not being in this match, which is the true one.
    /// </summary>
    [Theory]
    [InlineData(RoundButtonKind.Start, HideSeekPhase.Holding)]
    [InlineData(RoundButtonKind.Confirm, HideSeekPhase.Hiding)]
    [InlineData(RoundButtonKind.End, HideSeekPhase.Together)]
    public void Gate_RefusesEveryButtonToAPlayerWhoIsNotInThisMatch(
        RoundButtonKind kind, HideSeekPhase phase)
    {
        Assert.Equal(PressRefusal.NotInThisMatch,
            RoundButtonRules.Gate(kind, phase, Third, Alone, Second));
    }

    /// <summary>The twin: the two who ARE in the match are unaffected on the same buttons.</summary>
    [Fact]
    public void Gate_StillAcceptsThePlayersOwnButtonsWithAThirdInTheRoom()
    {
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Start, HideSeekPhase.Holding, Second, Alone, Second));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Confirm, HideSeekPhase.Hiding, Alone, Alone, Second));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.End, HideSeekPhase.Together, Second, Alone, Second));
    }

    /// <summary>In a solo session the one peer holds both roles, so it is never "not in this
    /// match" — the guard must not lock the only player out of their own game.</summary>
    [Fact]
    public void Gate_AcceptsTheSoloPlayersButtons()
    {
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Start, HideSeekPhase.Holding, Alone, Alone, Alone));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Confirm, HideSeekPhase.Hiding, Alone, Alone, Alone));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.End, HideSeekPhase.Together, Alone, Alone, Alone));
    }

    /// <summary>Before the roles are dealt nobody is excluded: an unassigned round must not
    /// answer every player "you are not in this match", which would be a dead button on a peer
    /// that has simply not synced.</summary>
    [Fact]
    public void Gate_DoesNotExcludeAnybodyWhileTheRolesAreVacant()
    {
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Start, HideSeekPhase.Holding, Third, 0, 0));
    }

    /// <summary>The lamp agrees with the gate: a waiting player's buttons are all Dark, so they
    /// are told before they press as well as after.</summary>
    [Fact]
    public void Lamp_IsDarkOnEveryButtonForAWaitingPlayer()
    {
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Start,
            Lamp(HideSeekPhase.Holding, Third, rack: true)));
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Confirm,
            Lamp(HideSeekPhase.Hiding, Third)));
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.End,
            Lamp(HideSeekPhase.Together, Third)));
    }

    /// <summary>And a third body in the room does not put the two players' START lamp out — which
    /// is what the old "exactly two humans" lamp condition did.</summary>
    [Fact]
    public void Lamp_StartStaysLitForBothPlayersWithAThirdInTheRoom()
    {
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Start,
            Lamp(HideSeekPhase.Holding, Alone, rack: true)));
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Start,
            Lamp(HideSeekPhase.Holding, Second, rack: true)));
    }

    /// <summary>
    /// <b>A peer that is not on the round's roster at all is NOT a waiting player</b>, and the
    /// difference is the dedicated server. It builds the world, so it owns a copy of every
    /// <c>RoundButton</c> and derives a lamp from its own <c>Multiplayer.GetUniqueId()</c> — which
    /// is 1, holds no avatar, holds neither role, and is nobody. Excluding it darkened the lamp
    /// the server logs, which is the line <c>Run-ButtonsTest.ps1</c> reads to prove the affordance
    /// exists before the press. **Measured**: that suite went red on
    /// <i>"the START lamp never went Lit"</i> with every press behaving correctly.
    ///
    /// <para>The discriminator is on the wire already and needs no id to be special: a human the
    /// round knows about <b>owns a score row</b> (<c>FoldFacts</c> puts one there at zero on their
    /// first tick) and the server does not. So "not in this match" means <i>on the roster,
    /// holding neither role</i> — which is exactly a third player and exactly not a server.</para>
    /// </summary>
    [Fact]
    public void Lamp_APeerThatIsNotOnTheRosterAtAll_IsNotTreatedAsWaiting()
    {
        // The dedicated server's own copy: it holds neither role AND has no score row.
        var server = new RoundButtonRules.LampFacts(true, HideSeekPhase.Holding, SelfPeerId: 1,
            Alone, Second, SelfIsOnTheRoster: false,
            HiderHoldsRackProp: true, HiderHoldsTarget: false);
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Start, server));
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.End,
            server with { Phase = HideSeekPhase.Together }));

        // The third PLAYER differs in exactly one bit, and is Dark.
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Start,
            server with { SelfPeerId = Third, SelfIsOnTheRoster = true }));
    }

    /// <summary>The solo player's START lamp lights, or the only person in the room is told not
    /// to press the only button that does anything.</summary>
    [Fact]
    public void Lamp_StartIsLitForASoloPlayerHoldingBothRoles()
    {
        var solo = new RoundButtonRules.LampFacts(true, HideSeekPhase.Holding, Alone,
            Alone, Alone, SelfIsOnTheRoster: true,
            HiderHoldsRackProp: true, HiderHoldsTarget: false);
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Start, solo));
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.End,
            solo with { Phase = HideSeekPhase.Together }));
    }

    /// <summary>The sentence says what to do, not merely what went wrong
    /// (<c>docs/INTERACTION-BIBLE.md</c> §5) — and it says it for all three buttons, because a
    /// waiting player can walk up to any of them.</summary>
    [Fact]
    public void NotInThisMatch_HasASentenceOnEveryButton()
    {
        foreach (RoundButtonKind kind in new[]
                 { RoundButtonKind.Start, RoundButtonKind.Confirm, RoundButtonKind.End })
        {
            string sentence = RoundButtonText.Sentence(PressRefusal.NotInThisMatch, kind);
            Assert.NotEqual(string.Empty, sentence);
            Assert.Equal(sentence.ToUpperInvariant(), sentence);
        }
    }

    /// <summary>
    /// <b>The two refusal enums share ONE number space, and this test is the fence.</b>
    /// <c>RoundButtonText</c> maps the round's own reasons across with
    /// <c>(HideSeekRefusal)(byte)refusal</c>, so 1–4 must be identical in both; 5–8 are the
    /// BUTTON's and <see cref="HideSeekRefusal"/> must never take one of them, or a round refusal
    /// crossing the wire would render as a button's sentence on the peer that reads it. SOLO-1
    /// adds <see cref="PressRefusal.NotInThisMatch"/> to the button's range for exactly that
    /// reason: the loop never learns who pressed, so it could not produce this refusal even if it
    /// had a name for it.
    /// </summary>
    [Fact]
    public void ThePressRefusalOrdinals_MirrorTheRoundsAndKeepTheButtonsOwnRangeClear()
    {
        foreach (HideSeekRefusal round in Enum.GetValues<HideSeekRefusal>())
        {
            Assert.True((byte)round <= (byte)HideSeekRefusal.NobodyCouldReachThat,
                $"HideSeekRefusal.{round} = {(byte)round} has entered PressRefusal's own 5+ range");
            Assert.Equal(round.ToString(), ((PressRefusal)(byte)round).ToString());
        }

        Assert.Equal(8, (byte)PressRefusal.NotInThisMatch);
        Assert.False(Enum.IsDefined((HideSeekRefusal)(byte)PressRefusal.NotInThisMatch));
    }

    // =========================================================================================
    // helpers
    // =========================================================================================

    private static RoundButtonRules.LampFacts Lamp(HideSeekPhase phase, int self,
        bool rack = false, bool target = false) =>
        new(true, phase, self, Alone, Second, SelfIsOnTheRoster: true, rack, target);

    /// <summary>The state as a client would see it: encoded and folded, so a board assertion is
    /// about what crossed the wire rather than about the server's own struct.</summary>
    private static HideSeekView ViewOf(in HideSeekState s) =>
        HideSeekWire.Fold(null, HideSeekWire.Encode(s, s.Scores.Keys.OrderBy(k => k).ToArray()));

    /// <summary>Runs a solo round from Holding to the Together edge, with the given sort count
    /// folded in throughout.</summary>
    private static HideSeekState SoloRoundToTogether(int sorts, HideSeekState? from = null)
    {
        HideSeekState s = from ?? SoloHolding();
        HideSeekInput idle = Roster(Alone) with { SortsCompleted = sorts };

        s = Tick(s, idle with { HostPressedStart = true, HiderHeldRackProp = true }, SoloTuning);
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        s = Tick(s, idle with { HiderPressedConfirm = true }, SoloTuning);
        Assert.Equal(HideSeekPhase.Seeking, s.Phase);
        s = Tick(s, idle with { TargetInDropOff = true }, SoloTuning);
        Assert.Equal(HideSeekPhase.Together, s.Phase);
        return s;
    }
}
