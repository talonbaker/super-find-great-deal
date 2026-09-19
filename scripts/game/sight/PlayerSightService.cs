using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;
using MpFoundation.Net;

namespace MpFoundation.Game.Sight;

/// <summary>
/// <b>The server-authoritative per-player sight range, and its replication.</b> Canon fact 4's
/// substrate: the server computes how far every connected player can see, from replicated world
/// state and a constant table only, and hands the whole table to every peer. Clients read; clients
/// never derive.
///
/// <para><b>All the arithmetic is somewhere else, on purpose.</b> This node gathers, ticks and
/// replicates; <see cref="PlayerSightCurve"/> decides. That split is what makes the model provable
/// by <c>dotnet test</c> with no engine present — the only thing not covered by a headless test
/// here is RPC plumbing, which no headless test could cover anyway.</para>
///
/// <para><b>Why the server computes it at all, rather than each client computing its own.</b> Every
/// input is already replicated, so a client could in principle reach the same number — and that is
/// precisely the arrangement canon fact 4 forbids. "Gameplay visibility is server data, identical on
/// every client (the parity law)" is a statement about <i>where the number is made</i>, not about
/// whether two implementations happen to agree today. A client-side derivation is one edit away from
/// reading a fog density, a camera far-plane or <see cref="MpFoundation.World.GraphicsQuality"/>,
/// and the symptom of that edit is a player who turned their graphics down seeing further in the
/// dark. There is no client write path here at all: every mutator below is private and reachable
/// only from an <c>[Rpc(Authority)]</c> method, and the only handle anything outside gets is
/// <see cref="RangeFor"/>, which returns a float.</para>
///
/// <para><b>Replication shape — <see cref="CycleDriver"/>'s, deliberately.</b> A low-cadence
/// unreliable broadcast of the whole table with a latest-wins sequence guard, plus a reliable
/// targeted <see cref="SendSightTo"/> that <c>Gameplay.OnPeerConnected</c> fires for every joining
/// or resuming peer — the same funnel <c>CycleDriver.SendPhaseTo</c> and
/// <c>PropManager.SendDumpTo</c> already ride. The repo's shipped failure mode is a late
/// joiner syncing to a zero default instead of live state, and <see cref="Synced"/> plus the
/// floor-valued fallback is how that fails blind here rather than omniscient.</para>
///
/// <para><b>One difference from <see cref="CycleDriver"/>, and it matters: clients do NOT
/// extrapolate between samples.</b> The clock can be dead-reckoned locally because elapsed time
/// advances identically everywhere. Sight cannot: it is a function of avatar positions, and those
/// are interpolated differently on every peer (snapshot buffering, interpolation delay, packet
/// loss). A client that "kept the sight range moving" between broadcasts would be deriving gameplay
/// visibility from its own presentation-side interpolation — the parity law's exact failure mode,
/// arriving through the back door. So a client holds the last authoritative value until the next one
/// lands. Every peer therefore holds the <i>same</i> value at all times, which is what the law
/// actually asks for; a renderer may smooth it for presentation, and must never feed the smoothed
/// value back into anything.</para>
///
/// <para><b>Scope: this is substrate.</b> It renders nothing and reads nothing rendered. Binding fog,
/// the camera or <c>OutdoorAtmosphere</c> to <see cref="RangeFor"/> is a separate pass and is
/// deliberately not done here (<c>SightPresentation</c> is that pass).</para>
/// </summary>
public partial class PlayerSightService : Node, Sail.Game.Run.IWorldStateSlice
{
    /// <summary>Stable node name so the RPCs below route on every peer.</summary>
    public const string NodeName = "PlayerSightService";

    /// <summary>Single instance per running game, same convention as
    /// <see cref="CycleDriver.Instance"/> and <see cref="RunDriver.Instance"/>. Null-check it: the
    /// labs and the CI worlds build their own trees.</summary>
    public static PlayerSightService? Instance { get; private set; }

    /// <summary>How often the server broadcasts the whole table. 5 Hz — four times
    /// <see cref="CycleDriver"/>'s rate, because the clock changes at a fixed slow rate and this
    /// changes at whatever rate a player walks. At the avatar's 4.86 m/s sprint the worst-case
    /// staleness is 0.97 m of movement, which inside an 18 m light pool is at most 1.7 m of
    /// sight — under a stride, and identical on every peer, which is the property that matters.
    /// Bandwidth is a peer id and a float per player: 48 bytes at 6 players, five times a second,
    /// unreliable.</summary>
    private const double BroadcastIntervalSec = 0.2;

    private bool _isServer;
    private double _sinceBroadcast;
    private uint _seq;

    // Who can see how far. Server: filled from scratch every tick. Client: replaced wholesale by
    // each applied message. Every rule about reading it - the floor for an unknown peer, the
    // latest-wins guard, the NaN clamp - lives in the table itself, which is a pure object and is
    // therefore pinned by dotnet test rather than by joining a session and watching.
    private readonly PlayerSightTable _table = new();

    // Reused rather than allocated at tick rate, same reasoning as PropManager's _loosePropsScratch.
    private readonly List<LightSample> _lightScratch = new();
    private readonly List<int> _idScratch = new();
    private readonly List<float> _rangeScratch = new();

    /// <summary>Where the authoritative avatar positions come from, injected by <c>Gameplay</c> —
    /// the same seam <c>IncapacitationService</c>'s avatar enumerator uses. Null means no players
    /// are known, which yields an empty table and therefore the darkness floor for everyone who
    /// asks.</summary>
    public Func<IEnumerable<(int PeerId, Vector3 Position)>>? PeerSource { get; set; }

    /// <summary>False until this peer holds an authoritative table. <b>Consumers MUST NOT present
    /// anything derived from <see cref="RangeFor"/> while this is false</b> — the identical contract
    /// <see cref="CycleDriver.Synced"/> states, and for the identical reason. Always true on the
    /// server from <see cref="Setup"/> onward. If a consumer
    /// ignores it anyway the value it gets is <see cref="PlayerSightCurve.DarkFloorM"/>, so the
    /// unhandled case is a player who cannot see rather than a player who can see everything.</summary>
    public bool Synced => _table.Synced;

    /// <summary>How many peers this table currently holds. Diagnostics and tests; a consumer wants
    /// <see cref="RangeFor"/>.</summary>
    public int TrackedPeerCount => _table.Count;

    /// <summary><b>The query surface — how far peer <paramref name="peerId"/> can see, in
    /// metres.</b> Identical on every peer by construction (the server is the only producer and it
    /// ships the whole table). Safe to call every frame; it is a dictionary probe.
    ///
    /// <para><b>Every miss returns <see cref="PlayerSightCurve.DarkFloorM"/>, never a maximum.</b>
    /// Unsynced peer, unknown peer id, peer that just disconnected, service that was never set up:
    /// each is "we do not know how far this player can see", and the safe answer to that is the one
    /// that makes them blind. The other direction has already cost this repo once — a radius that
    /// silently read as its degenerate default and inverted a gameplay rule for the whole session
    /// while every test stayed green.</para></summary>
    public float RangeFor(int peerId) => _table.RangeFor(peerId);

    /// <summary>This peer's own sight range — the value a local renderer or HUD wants.</summary>
    public float LocalRangeM => RangeFor(Multiplayer.GetUniqueId());

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Called by <c>Gameplay</c> once per session, the same shape as
    /// <see cref="CycleDriver.Setup"/> and <see cref="RunDriver.Setup"/>. The server is always its
    /// own authority, so it is
    /// <see cref="Synced"/> from here on; a client stays unsynced — and therefore blind — until its
    /// first table lands.</summary>
    public void Setup(bool isServer)
    {
        _isServer = isServer;
        _sinceBroadcast = 0;
        _table.Reset();
        if (isServer)
            _table.MarkSynced();
        // CORE-PROG-A2 (spec §5.2's sight row): per-playthrough perception state — previously
        // reset only from this Setup, i.e. never at a Play Again boundary. Null-safe for
        // store-less contexts.
        Sail.Game.Run.WorldStateStore.Instance?.Register(this);
    }

    // --- The world-state slice (CORE-PROG-A2) --------------------------------------------------

    public string SliceId => "player-sight";

    /// <summary>Server-only: drop the per-peer range table; the very next physics tick's
    /// ServerRecompute rebuilds it from the live avatars, and the next broadcast supersedes
    /// every client's copy. The wire <c>_seq</c> is NOT rewound and client tables are
    /// deliberately untouched — a
    /// client clear would flash Synced=false over a world that is still being rendered.
    /// Idempotent: clearing an empty table.</summary>
    public void ResetForNewPlaythrough()
    {
        if (!_isServer)
            return;
        _table.Reset();
        _table.MarkSynced();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer)
            return; // a client holds the last authoritative table; see the class doc.

        // Recomputed every tick, not once per broadcast. The broadcast cadence is a BANDWIDTH
        // decision; the server's own table is the truth, and a future server-side consumer (a
        // creature asking "can that player see me") must read the current value rather than one up
        // to 0.2 s stale. The cost is one gather plus one lerp per player per tick against at most a
        // few dozen light sources, which is nothing next to the physics step it rides.
        ServerRecompute();

        _sinceBroadcast += delta;
        if (_sinceBroadcast < BroadcastIntervalSec)
            return;
        _sinceBroadcast = 0;
        _seq++;
        BuildWirePayload(out int[] ids, out float[] ranges);
        // Unreliable, like CycleDriver's phase: a dropped sample costs at most 0.2 s of staleness
        // and the next one supersedes it outright. Rpc() to zero connected peers is a harmless
        // no-op, so this needs no peer-count guard.
        Rpc(MethodName.BroadcastSight, ids, ranges, _seq);
    }

    /// <summary><b>Server-only: the whole model, once, for every player.</b> Rebuilds the table from
    /// scratch rather than mutating it, so a peer who left cannot linger and a stale entry cannot
    /// outlive the frame that produced it.
    ///
    /// <para><b>Every degradation goes to the floor.</b> No peer source, no clock,
    /// <see cref="CycleDriver.Synced"/> false, no light source publishing a radius — each yields
    /// <see cref="PlayerSightCurve.DarkFloorM"/> for everyone, and none of
    /// them yields daylight or the maximum. The unsynced-clock branch is the one worth stating out
    /// loud: <see cref="CycleDriver"/>'s zero-initialized default is phase 0, which is the middle of
    /// the Day band, so trusting an unsynced clock would hand every player 80 m of sight at
    /// midnight. That is the inversion, and this is where it is refused.</para>
    ///
    /// <para>Public so a scene test can force a recompute without waiting a physics tick; it is a
    /// no-op on a client.</para></summary>
    public void ServerRecompute()
    {
        if (!_isServer)
            return;

        _table.ServerBeginFrame();
        if (PeerSource == null)
            return;

        // A missing clock is treated exactly as an unsynced one, and both go through the SAME pure
        // guard the tests pin (PlayerSightCurve.ResolveForClock) rather than being decided by a
        // branch in here that nothing headless can reach.
        CycleDriver? clock = CycleDriver.Instance;
        bool clockSynced = clock != null && clock.Synced;
        float phase = clock?.Phase ?? 0f;
        int cycles = clock?.CyclesElapsed ?? 0;

        GatherLights(_lightScratch);

        foreach ((int peerId, Vector3 position) in PeerSource())
            _table.ServerSet(peerId,
                PlayerSightCurve.ResolveForClock(position, _lightScratch, clockSynced, phase, cycles));
    }

    /// <summary>Every sight-granting light in the world, unioned into one list. <b>One list, one
    /// call site</b>: a caller that wanted only one family of light would be building a second,
    /// quieter definition of what counts as light, and the union rule
    /// (<see cref="PlayerSightCurve.NightSightM"/>) only holds if the list is complete.
    ///
    /// <para><b>No shipped source publishes a radius today, so the list is empty and night sight
    /// is the curve's floor.</b> The flashlight is presentation only by decision
    /// (<c>FlashlightProfile</c>'s class doc: it illuminates, it does not extend sight), and the
    /// worlds in this build override the sight seam outright (<c>BubbleTestWorld</c>). The seam
    /// stays so a future light that DOES grant sight has exactly one place to append itself, and
    /// so the day/dusk/night blend keeps running through the same pure call it always did.</para></summary>
    public void GatherLights(List<LightSample> into) => into.Clear();

    private void BuildWirePayload(out int[] peerIds, out float[] rangesM)
    {
        _idScratch.Clear();
        _rangeScratch.Clear();
        foreach (KeyValuePair<int, float> kv in _table.All)
        {
            _idScratch.Add(kv.Key);
            _rangeScratch.Add(kv.Value);
        }
        peerIds = _idScratch.ToArray();
        rangesM = _rangeScratch.ToArray();
    }

    /// <summary>Server -> everyone: the periodic table broadcast.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
        TransferChannel = NetProfile.SightChannel)]
    private void BroadcastSight(int[] peerIds, float[] rangesM, uint seq) =>
        Apply(peerIds, rangesM, seq, isSync: false);

    /// <summary>Server -> one peer: late-join / reconnect delivery. Reliable, and applied regardless
    /// of the last applied sequence for exactly <see cref="CycleDriver"/>'s stated reason — a
    /// reconnecting peer is a brand-new peer id server-side but the same node instance client-side,
    /// so its last-applied sequence is stale history from the previous connection rather than a
    /// legitimate ordering claim against the resumed session's sync.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.SightChannel)]
    private void SyncSightTo(int[] peerIds, float[] rangesM, uint seq) =>
        Apply(peerIds, rangesM, seq, isSync: true);

    /// <summary>Server-only: called by <c>Gameplay.OnPeerConnected</c> for every joining or resuming
    /// peer, the exact call site <c>CycleDriver.SendPhaseTo</c> and <c>PropManager.SendDumpTo</c>
    /// already use. Without it a joiner holds <see cref="Synced"/> false — and therefore the
    /// darkness floor — until the next periodic broadcast, which is only 0.2 s but is 0.2 s of a
    /// documented failure shape rather than none.</summary>
    public void SendSightTo(int peerId)
    {
        if (!_isServer)
            return;
        // Recompute first: the joining peer's own avatar was spawned moments ago in the same
        // handler, so a table built on the previous tick would not contain them at all - and "not in
        // the table" is the floor, i.e. the new arrival would be blind until the next broadcast.
        ServerRecompute();
        BuildWirePayload(out int[] ids, out float[] ranges);
        RpcId(peerId, MethodName.SyncSightTo, ids, ranges, _seq);
    }

    /// <summary>Every non-server peer: adopt an authoritative table.
    ///
    /// <para>The server ignores its own broadcasts outright rather than relying on
    /// <c>CallLocal</c> being off: a listen host is the authority and must never overwrite the table
    /// it just computed with a round-tripped copy of it.</para>
    ///
    /// <para>The staleness guard, the wholesale replacement and the NaN clamp all live in
    /// <see cref="PlayerSightTable.Apply"/> rather than here, so all three are pinned by
    /// <c>dotnet test</c>. This method is the transport boundary and nothing else.</para></summary>
    private void Apply(int[] peerIds, float[] rangesM, uint seq, bool isSync)
    {
        if (_isServer)
            return;
        _table.Apply(peerIds, rangesM, seq, isSync);
    }
}
