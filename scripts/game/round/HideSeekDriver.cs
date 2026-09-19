using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using MpFoundation.Net;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The server's round.</b> Steps <see cref="HideSeekLoop"/> at the sim tick, folds the facts
/// every other lane registers, teleports the affected players on each phase change, broadcasts one
/// absolute message whenever the round's visible state moves, and raises the three C# events the
/// rest of the program codes against.
///
/// <para><b>A layer above the loop, never a change to it.</b> Same relationship
/// <see cref="RunDriver"/> has to <see cref="CycleDriver"/>, and the file is written against that
/// precedent deliberately: present in every world, server and client alike, so its own late-join
/// delivery rides the identical peer-connect funnel
/// (<see cref="SendRoundStateTo"/>, called from <c>Gameplay.OnPeerConnected</c> on the same lines
/// as <c>CycleDriver.SendPhaseTo</c> and <c>PropManager.SendDumpTo</c>).</para>
///
/// <para><b>The broadcast is reliable, ordered and CallLocal</b>, on
/// <see cref="NetProfile.RoundChannel"/> — <c>PropManager.ApplyPropState</c>'s idiom for "an
/// authoritative discrete state transition, applied identically on the server and every peer". It
/// is NOT CycleDriver's unreliable snapshot style: a missed phase change is a player who never
/// learns which room they are in, and no later sample recovers the transition it implied.</para>
///
/// <para><b>It broadcasts on CHANGE, and the wire's own quantisation is the rate limit.</b> The
/// fastest field is the clock in tenths, so a message goes out about ten times a second at the
/// busiest and not at all while two people stand in the holding room. That works only because
/// <see cref="HideSeekWire"/> has hand-written structural equality — with the synthesized version
/// every tick compared unequal and this would have been a reliable RPC per peer per frame.</para>
///
/// <para><b>What it deliberately does NOT do.</b> No presentation: no door, no bang, no camera
/// kick, no card. Those are DOOR-1's and HOLD-1's, and they hang off the events below. The one
/// visual thing ROUND-1 owns is the HUD strip, and that reads <see cref="View"/> rather than
/// being pushed to.</para>
/// </summary>
public partial class HideSeekDriver : Node
{
    public const string NodeName = "HideSeekDriver";

    /// <summary>Single instance per running game, same convention as
    /// <see cref="RunDriver.Instance"/>.</summary>
    public static HideSeekDriver? Instance { get; private set; }

    private bool _isServer;
    private HideSeekTuning _tuning = HideSeekTuning.Default;
    private HideSeekState _state;
    private SupermarketWorld? _world;
    private Node3D? _players;
    private uint _seq;

    private readonly List<IRoundFactSource> _sources = new();
    private ScriptedRoundFactSource? _script;

    /// <summary>The roster, in JOIN order, maintained by the two hooks below rather than read off
    /// <c>Multiplayer.GetPeers()</c> — that collection has no defined order, and join order is
    /// what decides who hides first.</summary>
    private readonly List<int> _roster = new();

    /// <summary>
    /// Teleports that have been decided but not yet landed. <see cref="RoomTeleport"/> refuses a
    /// move inside its 800 ms per-peer cooldown, and two phase changes CAN fall inside that window
    /// — a seeker who finds the object within a second of Confirm would otherwise never reach the
    /// vestibule and would simply stand in the search room while the door burst somewhere else.
    /// So a refused move is retried on the following ticks instead of being dropped, and the
    /// refusal is logged the first time.
    /// </summary>
    private readonly Dictionary<int, Vector3> _pendingMoves = new();

    /// <summary>The last message sent, so the broadcast can be edge-triggered.</summary>
    private HideSeekWire _lastSent;
    private bool _hasSent;

    /// <summary>What THIS peer knows about the round — the folded view of the last message it
    /// applied. Meaningless until <see cref="Synced"/>; see that property.</summary>
    public HideSeekView View { get; private set; }

    /// <summary>False until this peer has its first authoritative round message. Server: true
    /// from <see cref="Setup"/> (it is its own authority). Client: true once one lands. Same
    /// contract and same reasoning as <see cref="CycleDriver.Synced"/> — "round 1, holding room,
    /// nobody scoring" and "I have not heard from the server yet" are the same bytes, and the
    /// second is a true-sounding wrong answer.</summary>
    public bool Synced { get; private set; }

    /// <summary>The authoritative state. Server-only and meaningless anywhere else; clients read
    /// <see cref="View"/>.</summary>
    public HideSeekState ServerState => _state;

    /// <summary>Fires on every peer (server included) exactly once per phase change, in order.
    /// DOOR-1, VOICE-1 and HOLD-1 all code against this.
    ///
    /// <para><b>Not raised for a joining peer's first message.</b> A client that arrives mid-seek
    /// has not WITNESSED a transition into Seeking, and telling it one just happened would make
    /// the door burst on a peer that missed the find. It polls <see cref="View"/> instead — the
    /// never-strand rule every flow screen in this repo is already built on.</para></summary>
    public event Action<HideSeekPhase, HideSeekPhase>? PhaseChanged;

    /// <summary>Fires on every peer exactly once per round, at the moment the target lands in the
    /// drop-off bin. Carries the SERVER's tick, not the receiving peer's — DOOR-1's burst has to
    /// be alignable to one instant across two machines.</summary>
    public event Action<long>? Found;

    /// <summary>Fires on every peer exactly once per reset edge (Tally -&gt; Holding). On the
    /// SERVER this is the world reset: props home, hands empty, and the reconnect registry torn
    /// up. See <c>Gameplay</c>'s subscription — that is where the slices are fanned, because that
    /// is where they live.</summary>
    public event Action? ResetRequested;

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        // Process-wide statics with a per-session lifetime: a server can host more than one match,
        // and a cooldown left over from the last one would refuse the first teleport of the next.
        if (_isServer)
            RoomTeleport.Reset();
    }

    /// <summary>
    /// Wires the driver up. <paramref name="world"/> and <paramref name="players"/> are the
    /// teleport's two halves — where the rooms are, and where the bodies are — and both may be
    /// null on a peer or in a world that has no rooms, in which case nothing is ever moved and the
    /// loop still runs.
    /// </summary>
    public void Setup(bool isServer, SupermarketWorld? world, Node3D? players,
        HideSeekTuning tuning, IReadOnlyList<(string Verb, string Value, double AtSec)>? roundScript = null)
    {
        _isServer = isServer;
        _world = world;
        _players = players;
        _tuning = tuning;
        _state = HideSeekLoop.Restart(_tuning);
        _roster.Clear();
        _pendingMoves.Clear();
        _hasSent = false;
        Synced = isServer;

        if (isServer)
        {
            RoomTeleport.Reset();
            if (_sources.Count == 0)
                _sources.Add(NullRoundFactSource.Instance);

            // The dev script is server-side and additive-only. See ScriptedRoundFactSource for
            // why that, plus the launch flag and the loud log lines, is the whole gate.
            if (roundScript is { Count: > 0 })
            {
                _script = new ScriptedRoundFactSource(roundScript);
                _sources.Add(_script);
            }
            GD.Print($"[round] driver ready (server) — hiding {_tuning.HidingSec:0}s "
                     + $"(+{_tuning.HidingGraceSec:0}s grace), seeking {_tuning.SeekingSec:0}s, "
                     + $"tally {_tuning.TallySec:0}s, channel {NetProfile.RoundChannel}");
        }
    }

    /// <summary>Another lane's fact provider. Registration order is the order
    /// <see cref="IRoundFactSource.TargetRetrievable"/>'s first-non-null rule walks, so REACH-1
    /// registering after a placeholder wins.</summary>
    public void Register(IRoundFactSource source)
    {
        if (source is null || _sources.Contains(source))
            return;
        // The null source is a placeholder for an empty list, not a participant; drop it the
        // moment a real one arrives so it cannot answer for anybody.
        _sources.Remove(NullRoundFactSource.Instance);
        _sources.Add(source);
    }

    /// <summary>Server-only: a peer joined. Called from <c>Gameplay.OnPeerConnected</c>, which is
    /// the one place that knows join ORDER.</summary>
    public void ServerPeerJoined(int peerId)
    {
        if (!_isServer || peerId == 0 || _roster.Contains(peerId))
            return;
        _roster.Add(peerId);
    }

    /// <summary>Server-only: a peer left. Also drops its teleport cooldown, because ENet peer ids
    /// are RECYCLED and a stale entry would make the next player handed that id silently
    /// un-teleportable for up to <see cref="RoomTeleport.CooldownMsec"/> after they join.</summary>
    public void ServerPeerLeft(int peerId)
    {
        if (!_isServer)
            return;
        _roster.Remove(peerId);
        _pendingMoves.Remove(peerId);
        RoomTeleport.ForgetPeer(peerId);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer)
            return;

        _script?.Advance(delta);

        HideSeekPhase before = _state.Phase;
        _state = HideSeekLoop.Step(_state, CollectFacts(), (float)delta, _tuning);
        foreach (IRoundFactSource source in _sources)
            source.AfterStep();

        if (_state.Phase != before)
            OnServerPhaseChanged(before, _state.Phase);

        if (_state.ResetRequested)
            OnServerResetEdge();

        DrainPendingMoves();
        BroadcastIfChanged();
    }

    // ---------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------

    /// <summary>Combines every registered source into one input. The rules live in
    /// <see cref="RoundFacts.Combine"/>, engine-free, because this is the seam where five
    /// independent lanes' answers get reconciled and a rule that only runs inside a scene tree is
    /// a rule no test can hold.</summary>
    private HideSeekInput CollectFacts() => RoundFacts.Combine(_sources, _roster);

    // ---------------------------------------------------------------------------------------
    // Teleports
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Who goes where, per program §2's table. The whole map is here, in one switch, rather than
    /// spread across the loop's transition helpers: the loop is engine-free and must stay that
    /// way, and "which room is this phase" is exactly the fact a reader comes to this file for.
    /// </summary>
    private void OnServerPhaseChanged(HideSeekPhase from, HideSeekPhase to)
    {
        GD.Print($"[round] phase {from} -> {to} (round {_state.RoundIndex}, "
                 + $"hider={_state.HiderPeerId} seeker={_state.SeekerPeerId})");

        switch (to)
        {
            case HideSeekPhase.Hiding:
                MoveTo(_state.HiderPeerId, SupermarketWorld.SearchRoom, 0);
                break;
            case HideSeekPhase.Seeking:
                MoveTo(_state.HiderPeerId, SupermarketWorld.TaskRoom, 0);
                MoveTo(_state.SeekerPeerId, SupermarketWorld.SearchRoom, 0);
                break;
            case HideSeekPhase.Together:
                // Behind the burst door, already facing in (program §5 step 1). The door itself is
                // DOOR-1's; this only puts the body where the door will open onto.
                MoveTo(_state.SeekerPeerId, SupermarketWorld.Vestibule, 0);
                break;
            case HideSeekPhase.Holding:
                // Everyone home, each to their own marker so two bodies never arrive inside each
                // other. Indexed by roster position, which is the same order every peer's copy of
                // the world sorted its markers into.
                for (int i = 0; i < _roster.Count; i++)
                    MoveTo(_roster[i], SupermarketWorld.HoldingRoom, i);
                break;
        }
    }

    /// <summary>Decides one move and tries it immediately; a refusal is queued rather than
    /// dropped (see <see cref="_pendingMoves"/>).</summary>
    private void MoveTo(int peerId, string room, int markerIndex)
    {
        if (peerId == 0 || _world is null)
            return;
        IReadOnlyList<Vector3> markers = _world.SpawnPointsFor(room);
        if (markers.Count == 0)
        {
            // Loud: a world with no marker for a room is a level defect, and the symptom
            // (everybody stays put through a phase change) reads as a broken round.
            GD.PushWarning($"[round] no spawn marker for room '{room}' — peer {peerId} not moved");
            return;
        }
        Vector3 destination = markers[markerIndex % markers.Count];
        _pendingMoves[peerId] = destination;
        GD.Print($"[round] peer {peerId} -> {room}[{markerIndex % markers.Count}] {destination}");
        TryMove(peerId, destination, first: true);
    }

    private void DrainPendingMoves()
    {
        if (_pendingMoves.Count == 0)
            return;
        // Snapshot the keys: TryMove removes from the dictionary on success.
        var peers = new List<int>(_pendingMoves.Keys);
        foreach (int peerId in peers)
            TryMove(peerId, _pendingMoves[peerId], first: false);
    }

    private void TryMove(int peerId, Vector3 destination, bool first)
    {
        SandboxAvatar? avatar = _players?.GetNodeOrNull<SandboxAvatar>(peerId.ToString());
        if (avatar is null)
            return;   // not spawned yet, or already gone; retried next tick.
        if (RoomTeleport.ServerMove(avatar, destination))
        {
            _pendingMoves.Remove(peerId);
            return;
        }
        if (first)
            GD.Print($"[round] peer {peerId} move deferred — inside the "
                     + $"{RoomTeleport.CooldownMsec} ms teleport cooldown; retrying");
    }

    // ---------------------------------------------------------------------------------------
    // The reset edge
    // ---------------------------------------------------------------------------------------

    private void OnServerResetEdge()
    {
        GD.Print($"[round] reset edge — round {_state.RoundIndex}, "
                 + $"hider={_state.HiderPeerId} seeker={_state.SeekerPeerId} (roles swapped)");
        // The event is raised on the SERVER here and again on every peer when the phase change
        // lands (see ApplyRound). Gameplay's subscription is what fans the world-state slices;
        // this file deliberately does not reach into PropManager or the reconnect registry, both
        // of which Gameplay owns.
    }

    // ---------------------------------------------------------------------------------------
    // The wire
    // ---------------------------------------------------------------------------------------

    private void BroadcastIfChanged()
    {
        HideSeekWire wire = HideSeekWire.Encode(_state, _roster);
        if (_hasSent && wire.Equals(_lastSent))
            return;
        _lastSent = wire;
        _hasSent = true;

        var p = wire.Pack();
        Rpc(MethodName.ApplyRound, p.Phase, p.Round, p.RemainingTenths, p.Hider, p.Seeker,
            p.ScorePeers, p.ScoreValues, p.Refusal, p.Towers, p.FoundTick, p.TallyRound,
            p.TallyHider, p.TallyHiderGain, p.TallySeeker, p.TallySeekerGain, p.TallyByDisconnect,
            ++_seq);
    }

    /// <summary>Server-only: the late-join dump. Called from <c>Gameplay.OnPeerConnected</c> on
    /// the same lines as <c>CycleDriver.SendPhaseTo</c> and <c>PropManager.SendDumpTo</c>, AFTER
    /// the spawner call so the new peer is already in the roster this message describes.
    ///
    /// <para><b>It is the same message the live broadcast sends</b>, not a second code path — that
    /// is the whole point of an absolute wire, and a separate "catch-up" encoder is exactly how a
    /// joiner ends up with a view nobody else has.</para></summary>
    public void SendRoundStateTo(int peerId)
    {
        if (!_isServer)
            return;
        var p = HideSeekWire.Encode(_state, _roster).Pack();
        RpcId(peerId, MethodName.ApplyRound, p.Phase, p.Round, p.RemainingTenths, p.Hider,
            p.Seeker, p.ScorePeers, p.ScoreValues, p.Refusal, p.Towers, p.FoundTick, p.TallyRound,
            p.TallyHider, p.TallyHiderGain, p.TallySeeker, p.TallySeekerGain, p.TallyByDisconnect,
            ++_seq);
    }

    /// <summary>Server -&gt; everyone (CallLocal), reliable and ordered on
    /// <see cref="NetProfile.RoundChannel"/>. One code path for the server, every client and every
    /// late joiner, so nothing downstream can depend on which of those it is.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.RoundChannel, CallLocal = true)]
    private void ApplyRound(byte phase, int round, int remainingTenths, int hider, int seeker,
        int[] scorePeers, int[] scoreValues, byte refusal, int towers, int foundTick,
        int tallyRound, int tallyHider, int tallyHiderGain, int tallySeeker, int tallySeekerGain,
        bool tallyByDisconnect, uint seq)
    {
        _ = seq;   // reliable + ordered: no staleness guard is needed (RunDriver's note).

        HideSeekWire wire = HideSeekWire.Unpack(phase, round, remainingTenths, hider, seeker,
            scorePeers, scoreValues, refusal, towers, foundTick, tallyRound, tallyHider,
            tallyHiderGain, tallySeeker, tallySeekerGain, tallyByDisconnect);

        bool first = !Synced;
        HideSeekView previous = View;
        View = HideSeekWire.Fold(View, wire);
        Synced = true;

        // A joining peer is COMPLETE from this one message and has WITNESSED nothing. Raising
        // PhaseChanged here would burst DOOR-1's door for a client that arrived after the find.
        if (first)
        {
            GD.Print($"[round] synced: {View.Phase} round {View.Round} "
                     + $"hider={View.HiderPeerId} seeker={View.SeekerPeerId}");
            return;
        }
        if (View.Phase == previous.Phase)
            return;

        PhaseChanged?.Invoke(previous.Phase, View.Phase);
        if (View.Phase == HideSeekPhase.Together && View.FoundTick >= 0)
            Found?.Invoke(View.FoundTick);
        // The reset edge, derived on every peer from the one transition that IS the reset. The
        // server's own copy of this fires here too (CallLocal), which is what Gameplay subscribes
        // to — so the slice fan-out and the clients' repaint are driven by the same message rather
        // than by two clocks.
        if (previous.Phase == HideSeekPhase.Tally && View.Phase == HideSeekPhase.Holding)
            ResetRequested?.Invoke();
    }
}
