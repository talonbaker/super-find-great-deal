using MpFoundation.Game.Round;
using MpFoundation.Game.World;
using MpFoundation.Voice;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// VOICE-1: the intercom's route decision, engine-free.
///
/// <para>Two things are under test and they are deliberately separate files' worth of concern
/// folded into one suite, because neither is meaningful alone: <see cref="RoundRooms.RoomOf"/>
/// (which room is a peer in, from the round's phase and roles) and
/// <see cref="VoiceRouting.IsPa"/> (given two rooms, is this the PA or is it proximity). What a
/// player experiences is the composition, so the middle block below asserts the composition as a
/// (talker, listener) -&gt; route TABLE, phase by phase, which is the form the packet asks
/// for.</para>
///
/// <para>The live end-to-end behaviour — that the server's relay actually exempts a cross-room
/// pair from the distance cull, over real ENet with real Opus frames — cannot live here and is
/// <c>tests/Run-VoiceRoomTest.ps1</c>.</para>
/// </summary>
public class VoiceRoutingTests
{
    private const int Hider = 7001;
    private const int Seeker = 7002;
    private const int Bystander = 7003;

    private static string Room(HideSeekPhase phase, int peer) =>
        RoundRooms.RoomOf(phase, Hider, Seeker, peer);

    private static string Route(HideSeekPhase phase, int talker, int listener) =>
        VoiceRouting.RouteFor(Room(phase, talker), Room(phase, listener));

    // --- Rooms, phase by phase (program §2's table) ------------------------------------------

    [Fact]
    public void Holding_BothRoleHolders_AreInTheHoldingRoom()
    {
        Assert.Equal(SupermarketWorld.HoldingRoom, Room(HideSeekPhase.Holding, Hider));
        Assert.Equal(SupermarketWorld.HoldingRoom, Room(HideSeekPhase.Holding, Seeker));
    }

    [Fact]
    public void Hiding_HiderIsInSearch_SeekerIsShutInHolding()
    {
        Assert.Equal(SupermarketWorld.SearchRoom, Room(HideSeekPhase.Hiding, Hider));
        Assert.Equal(SupermarketWorld.HoldingRoom, Room(HideSeekPhase.Hiding, Seeker));
    }

    [Fact]
    public void Seeking_HiderIsInTask_SeekerIsInSearch()
    {
        Assert.Equal(SupermarketWorld.TaskRoom, Room(HideSeekPhase.Seeking, Hider));
        Assert.Equal(SupermarketWorld.SearchRoom, Room(HideSeekPhase.Seeking, Seeker));
    }

    [Fact]
    public void Together_PutsBothOfThemInTheTaskRoom_BecauseTheDoorIsOpen()
    {
        // REVIEW-1 I1, 2026-09-20. This test used to assert SupermarketWorld.Vestibule for the
        // seeker, and the door bursting open is exactly what makes that wrong: the vestibule is
        // "a destination, not a room -- it is inside the task room's section scene"
        // (SupermarketWorld.cs), so the room KEY was finer-grained than the acoustic space it
        // names. Together has no timer and ends only on an End press, so for the whole of the
        // payoff beat the two players stood two metres apart in one space and heard each other
        // through the intercom chain (overdrive -> 2.2 kHz lowpass -> boxy reverb, no falloff).
        //
        // Vestibule is still the TELEPORT destination key -- HideSeekDriver.OnServerPhaseChanged
        // reads SpawnPointsFor(Vestibule) directly and is untouched by this.
        Assert.Equal(SupermarketWorld.TaskRoom, Room(HideSeekPhase.Together, Hider));
        Assert.Equal(SupermarketWorld.TaskRoom, Room(HideSeekPhase.Together, Seeker));
    }

    [Fact]
    public void Together_IsTheOneMidRoundPhaseOnProximity()
    {
        // The half of I1 that is about the player rather than about a string. This is the social
        // payoff the whole startle is built for; a filtered PA voice across two metres is the
        // failure VoiceRoomTest's "everything exempt" plant exists to catch, arriving here
        // through the room resolver instead of through the relay.
        Assert.Equal(VoiceRouting.RouteProximity, Route(HideSeekPhase.Together, Hider, Seeker));
        Assert.Equal(VoiceRouting.RouteProximity, Route(HideSeekPhase.Together, Seeker, Hider));
    }

    [Fact]
    public void Tally_IsUnknownForBothRoles_BecauseNobodyIsMovedThere()
    {
        // Not an omission: Tally is reached from Together, from a Seeking timeout or from a
        // mid-round disconnect, and the driver moves nobody on the commit. In all three the two
        // of them are in DIFFERENT rooms, so the honest unknown resolves (next block) to exactly
        // the route the geometry calls for.
        Assert.Equal(RoundRooms.Unknown, Room(HideSeekPhase.Tally, Hider));
        Assert.Equal(RoundRooms.Unknown, Room(HideSeekPhase.Tally, Seeker));
    }

    [Fact]
    public void APeerWithNeitherRole_IsInTheHoldingRoomInEveryPhase()
    {
        // The round moves exactly two bodies; a third peer is left where the spawn points put
        // them. Asserted for every phase so a lane that starts teleporting spectators has to
        // come here and say so.
        foreach (HideSeekPhase phase in new[]
                 {
                     HideSeekPhase.Holding, HideSeekPhase.Hiding, HideSeekPhase.Seeking,
                     HideSeekPhase.Together, HideSeekPhase.Tally,
                 })
        {
            Assert.Equal(SupermarketWorld.HoldingRoom, Room(phase, Bystander));
        }
    }

    [Fact]
    public void PeerZero_IsUnknown()
    {
        // 0 is "no peer" throughout the round (HideSeekState uses it for a vacant role), so it
        // must never resolve to a real room and hand somebody a proximity cull.
        Assert.Equal(RoundRooms.Unknown, RoundRooms.RoomOf(HideSeekPhase.Seeking, Hider, Seeker, 0));
    }

    [Fact]
    public void BeforeRolesAreAssigned_EverybodyIsInTheHoldingRoom()
    {
        // One human in the lobby: both role ids are 0 and nothing has started. The answer has to
        // be the holding room, not unknown — two people standing next to each other must be on
        // proximity, and unknown would fail them open to the PA and put a filtered reverb on a
        // face-to-face conversation.
        Assert.Equal(SupermarketWorld.HoldingRoom,
            RoundRooms.RoomOf(HideSeekPhase.Holding, 0, 0, Hider));
    }

    // --- The (talker, listener) -> route table ----------------------------------------------

    [Fact]
    public void SameRoom_IsProximity_BothWays()
    {
        Assert.Equal(VoiceRouting.RouteProximity, Route(HideSeekPhase.Holding, Hider, Seeker));
        Assert.Equal(VoiceRouting.RouteProximity, Route(HideSeekPhase.Holding, Seeker, Hider));
    }

    [Fact]
    public void EveryMidRoundPhase_IsPaBothWays()
    {
        // Hiding and Seeking each put the two of them in different rooms, and the route is
        // symmetric: the hider taunting back is the same channel the seeker taunted on.
        //
        // TOGETHER IS NOT IN THIS LIST since REVIEW-1 I1 (2026-09-20) -- the door has burst and
        // they are in one room. Its own test is above, and it asserts proximity rather than
        // merely omitting the phase, so this list shrinking cannot be a coverage loss.
        foreach (HideSeekPhase phase in new[]
                 { HideSeekPhase.Hiding, HideSeekPhase.Seeking })
        {
            Assert.Equal(VoiceRouting.RoutePa, Route(phase, Hider, Seeker));
            Assert.Equal(VoiceRouting.RoutePa, Route(phase, Seeker, Hider));
        }
    }

    [Fact]
    public void ABystanderInTheHoldingRoom_HearsTheSeekerOnProximityWhileTheSeekerIsShutInWithThem()
    {
        // Hiding: the seeker is in the holding room, so a third peer standing there is on
        // proximity with them — and on the PA with the hider two rooms away, in the same frame.
        Assert.Equal(VoiceRouting.RouteProximity, Route(HideSeekPhase.Hiding, Seeker, Bystander));
        Assert.Equal(VoiceRouting.RoutePa, Route(HideSeekPhase.Hiding, Hider, Bystander));
    }

    [Fact]
    public void Tally_ResolvesToPa_ThroughTheFailOpenRule()
    {
        Assert.Equal(VoiceRouting.RoutePa, Route(HideSeekPhase.Tally, Hider, Seeker));
    }

    // --- Unknown fails OPEN, never to silence ------------------------------------------------

    [Fact]
    public void UnknownTalkerRoom_IsPa()
    {
        Assert.True(VoiceRouting.IsPa(RoundRooms.Unknown, SupermarketWorld.SearchRoom));
    }

    [Fact]
    public void UnknownListenerRoom_IsPa()
    {
        Assert.True(VoiceRouting.IsPa(SupermarketWorld.SearchRoom, RoundRooms.Unknown));
    }

    [Fact]
    public void BothRoomsUnknown_IsPa()
    {
        Assert.True(VoiceRouting.IsPa(RoundRooms.Unknown, RoundRooms.Unknown));
    }

    [Fact]
    public void NullRoom_IsPa()
    {
        // An unwired resolver answers null, not "". Both are "I cannot say" and both must be
        // loud: the report a player writes is "I could not hear anyone".
        Assert.True(VoiceRouting.IsPa(null, SupermarketWorld.TaskRoom));
        Assert.True(VoiceRouting.IsPa(SupermarketWorld.TaskRoom, null));
    }

    [Fact]
    public void TheDoorwayCase_NeverFlaps_BecauseTheRoomIsAStepFunctionOfThePhase()
    {
        // The packet's doorway case. There is no geometry in this decision at all: a player
        // standing exactly in a doorway resolves to the room their ROLE puts them in for that
        // phase, and the answer cannot change until the phase does. Asserted as "the same inputs
        // give the same answer with nothing positional to perturb" — the property a
        // position-sampling resolver would not have.
        for (int i = 0; i < 4; i++)
            Assert.Equal(SupermarketWorld.TaskRoom, Room(HideSeekPhase.Seeking, Hider));
    }

    [Fact]
    public void RouteNames_AreTheStringsTheHarnessAndSuitesMatchOn()
    {
        // Pinned: BotHarness's `pa` column, the routing verdict's `route` field and every suite
        // string-compare against these two literals.
        Assert.Equal("pa", VoiceRouting.RoutePa);
        Assert.Equal("proximity", VoiceRouting.RouteProximity);
    }

    // --- The one intercom knob ---------------------------------------------------------------

    [Fact]
    public void IntercomWetDb_AtZero_IsExactlyWhatShips()
    {
        // The property that makes this a trim rather than a retune. If this ever goes red, the
        // knob changed the intercom's character by existing.
        Assert.Equal(VoiceConfig.PaReverbWet, VoiceRouting.WetFromDb(0f), 1e-6f);
    }

    [Fact]
    public void IntercomWetDb_MinusSix_IsHalfTheWetMix()
    {
        Assert.Equal(VoiceConfig.PaReverbWet * 0.5f, VoiceRouting.WetFromDb(-6.0206f), 1e-4f);
    }

    [Fact]
    public void IntercomWetDb_VeryNegative_IsDryAndNeverNegative()
    {
        float wet = VoiceRouting.WetFromDb(-120f);
        Assert.True(wet >= 0f);
        Assert.True(wet < 0.0001f);
    }

    [Fact]
    public void IntercomWetDb_Positive_ClampsAtFullyWet()
    {
        // AudioEffectReverb.Wet is a 0..1 mix; past 1 is not a louder room, it is an invalid
        // parameter.
        Assert.Equal(1f, VoiceRouting.WetFromDb(60f), 1e-6f);
    }

    [Fact]
    public void IntercomWetDb_NaN_FallsBackToTheShippedMix()
    {
        Assert.Equal(VoiceConfig.PaReverbWet, VoiceRouting.WetFromDb(float.NaN), 1e-6f);
    }

    // --- The lamp's copy ----------------------------------------------------------------------

    [Fact]
    public void IntercomLine_NamesTheSpeaker()
    {
        Assert.Equal("INTERCOM: WATI", HideSeekText.IntercomLine("Wati"));
    }

    [Fact]
    public void IntercomLine_WithNoNameYet_StillSaysTheIntercomIsLive()
    {
        // A display name that has not replicated must not silently remove the lamp: "somebody is
        // talking to you and I cannot say who" is still the half of the message that matters.
        Assert.Equal("INTERCOM: SOMEONE", HideSeekText.IntercomLine(""));
        Assert.Equal("INTERCOM: SOMEONE", HideSeekText.IntercomLine("   "));
        Assert.Equal("INTERCOM: SOMEONE", HideSeekText.IntercomLine(null!));
    }
}
