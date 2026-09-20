using MpFoundation.Game.World;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Which room is a given peer standing in right now</b>, derived from the round alone
/// (VOICE-1, program §2's phase table). Engine-free and pure, so the one fact the intercom
/// hangs on is unit-testable and resolves IDENTICALLY on the server and on every client from
/// the same replicated message.
///
/// <para><b>From the round, never from a position sample.</b> ROUND-1's handoff states the rule
/// and this file is it: the teleports are authoritative, the positions are their consequence, and
/// a resolver reading positions would disagree with the round for the frame between the teleport
/// landing and the next snapshot. It would also make a player standing in a doorway FLAP between
/// routes — the PA bus is a different bus with different effects, so a flap is audible as the
/// voice switching character mid-word. Phase plus role is a step function with no boundary at
/// all.</para>
///
/// <para><b>Unknown is a real answer and it is not silence.</b> <see cref="Unknown"/> means "the
/// round cannot say", and every consumer resolves it to the PA route
/// (<see cref="MpFoundation.Voice.VoiceRouting.IsPa"/>) rather than to a distance cull. The
/// failure a player reports is "I could not hear anyone" — the same fail-open reasoning
/// <c>VoiceRelayDecider</c> already applies to an unresolvable avatar position.</para>
/// </summary>
public static class RoundRooms
{
    /// <summary>The round cannot say where this peer is. Empty rather than null so a caller can
    /// compare without a null check and a JSON field is never missing.</summary>
    public const string Unknown = "";

    /// <summary>
    /// Program §2's table, in one switch.
    ///
    /// <list type="bullet">
    /// <item><b>Holding</b> — everyone, role or not, is in the holding room: the driver sends the
    /// whole roster there on the reset edge, and that is also where every peer spawns.</item>
    /// <item><b>Hiding</b> — hider in the search room, seeker shut in the holding room.</item>
    /// <item><b>Seeking</b> — hider in the task room, seeker in the search room.</item>
    /// <item><b>Together</b> — <b>both of them in the task room</b>. The seeker is teleported to
    /// the vestibule for the burst, but <see cref="SupermarketWorld.Vestibule"/> is "a
    /// destination, not a room: it is inside the task room's section scene"
    /// (<c>SupermarketWorld.cs</c>) — so answering it here made the room KEY finer-grained than
    /// the acoustic space it describes, and put two players standing two metres apart on the PA
    /// bus for the whole beat (REVIEW-1 I1, 2026-09-20). Together has no timer and ends only on
    /// an End press, so that was every round's social payoff heard through an overdrive, a
    /// 2.2 kHz lowpass and a boxy reverb with no falloff. The teleport is unaffected:
    /// <c>HideSeekDriver.OnServerPhaseChanged</c> reads <c>SpawnPointsFor(Vestibule)</c> directly
    /// and never asks this function anything.</item>
    /// <item><b>Tally</b> — <see cref="Unknown"/> for both role holders, and that is deliberate
    /// rather than unfinished. Nobody is moved at the Tally commit, so where the two of them are
    /// depends on which transition arrived there: Together (task / vestibule), a Seeking timeout
    /// (task / search) or a mid-round disconnect (search / holding). In EVERY one of those three
    /// they are in different rooms, so the honest "I cannot say" resolves through the fail-open
    /// rule to exactly the route the real geometry calls for, with no extra state to keep. If a
    /// later lane needs the actual room at Tally, the driver's own teleport log is the place to
    /// read it from — not a guess made here.</item>
    /// </list>
    ///
    /// <para><b>A peer with neither role is in the holding room in every phase.</b> The round
    /// moves exactly two bodies, and a third peer (a late joiner, a spectator) is left standing
    /// where <c>SupermarketWorld.SpawnPoints</c> put them, which is the holding room. That is a
    /// fact about the driver, not an assumption: <c>HideSeekDriver.OnServerPhaseChanged</c> names
    /// only <c>HiderPeerId</c> and <c>SeekerPeerId</c> outside the Holding arm.</para>
    /// </summary>
    public static string RoomOf(HideSeekPhase phase, int hiderPeerId, int seekerPeerId, int peerId)
    {
        if (peerId == 0)
            return Unknown;

        bool isHider = hiderPeerId != 0 && peerId == hiderPeerId;
        bool isSeeker = seekerPeerId != 0 && peerId == seekerPeerId;

        // Checked before the phase switch: a peer the round is not moving is in the holding room
        // whatever the phase is.
        if (!isHider && !isSeeker)
            return SupermarketWorld.HoldingRoom;

        return phase switch
        {
            HideSeekPhase.Holding => SupermarketWorld.HoldingRoom,
            HideSeekPhase.Hiding => isHider ? SupermarketWorld.SearchRoom : SupermarketWorld.HoldingRoom,
            HideSeekPhase.Seeking => isHider ? SupermarketWorld.TaskRoom : SupermarketWorld.SearchRoom,
            // Both: the door has burst and the vestibule is inside the task room. See the list
            // above — Vestibule stays a teleport destination key and is not a room here.
            HideSeekPhase.Together => SupermarketWorld.TaskRoom,
            _ => Unknown, // Tally — see the doc comment.
        };
    }
}
