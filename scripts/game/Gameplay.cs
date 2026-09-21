using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Net;
using MpFoundation.Net.Steam;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.Props;
using MpFoundation.Game.World;

namespace MpFoundation.Game;

/// <summary>
/// The match scene. On the dedicated server it listens and spawns/despawns avatars —
/// which then simulate themselves server-authoritatively from client inputs (see
/// SandboxAvatar; the old plausibility bounds-check is gone because the server now owns
/// the movement truth outright). On clients it (optionally) resolves a room code to a
/// server identity via the Steam lobby directory (SteamLobby) and connects with the
/// Phase 1 flow. The scene is loaded before connecting so replication never races scene
/// setup. The room code's lifecycle lives entirely client-side with the host (HostMenu
/// creates the lobby; the server child never knows its own code).
/// </summary>
public partial class Gameplay : Node3D
{
    private const double ConnectTimeoutSec = 8.0;
    private const double SteamConnectTimeoutSec = 25.0;
    private const float SpawnRadius = 4.0f;
    private const double StatusIntervalSec = 10.0;

    private PackedScene _playerScene = null!;
    private IGameWorld _world = null!;

    private MultiplayerSpawner _spawner = null!;
    private Node3D _players = null!;

    /// <summary>Drives the pickup shimmer and the interact key chip for this peer's own
    /// player. Null on headless peers, which render nothing.</summary>
    private Sandbox.InteractHighlighter? _highlighter;
    private MultiplayerSpawner _propSpawner = null!;
    private Node3D _props = null!;
    private PropManager _propManager = null!;

    /// <summary>The NetworkedEntity replication funnel. Stays empty in every session today — no
    /// shipped gameplay spawns entities — but the spawner is wired unconditionally so the first
    /// real NPC rides the same late-join replay path players and props already use.</summary>
    private MultiplayerSpawner _entitySpawner = null!;
    private Node3D _entities = null!;
    private CycleDriver _cycleDriver = null!;
    private RunDriver _runDriver = null!;
    private Round.HideSeekDriver _hideSeekDriver = null!;
    private Round.RoundControls _roundControls = null!;

    /// <summary>REACH-1's fact source on the server, or null off-server — handed to
    /// <see cref="_roundControls"/> so BTN-1's rack can aim the reachability audit at whatever
    /// the hider actually took off the shelf. Held as a field only to keep the two registrations
    /// in their required order (see where it is assigned).</summary>
    private Round.ReachabilityFactSource? _reachFacts;

    private Label _connectingLabel = null!;
    private Label _roomCodeLabel = null!;
    private int _spawnIndex;
    private bool _connected;
    private bool _finished;
    private bool _serverSignals;
    private bool _clientSignals;

    // Client-only reconnect state (Steam transport only; see OnServerDisconnected).
    // Nonzero only once this client has connected via ConnectSteam - an ENet-connected
    // client (Practice/direct) never sets this and so never enters the retry flow.
    private ulong _steamServerId;
    private bool _reconnecting;
    private ulong _reconnectDeadlineMsec;
    private int _connectAttempt;
    private const double ReconnectRetryDelaySec = 2.0;

    // Test-only (--force-reconnect-at, see LaunchOptions): the ENet host/port this client
    // dialed, remembered so a simulated forced reconnect can redial the same address without a
    // live Steam relay. Harmless to cache unconditionally - only read when the flag is set.
    private string _enetHost = "";
    private int _enetPort;
    private bool _forceReconnectArmed;
    // Guards against re-instantiating BotHarness on a resumed OnConnectedToServer (a real
    // reconnect over Steam is gated off for bots by design, but the --force-reconnect-at test
    // hook drives a bot through this path anyway - see SimulateForcedReconnect). Without this,
    // a second BotHarness would try to re-open the SAME --log file the first one still holds
    // open (FileMode.Create over a handle sharing only FileShare.Read) and crash with a sharing
    // violation instead of exercising the teardown this task is testing.
    private BotHarness? _harness;

    // Server-only state.
    private bool _isServer;
    private ulong _startTicks;
    private double _sinceStatus;
    // Steam-transport-only: SteamID64 -> last authoritative position, expiring 60s after
    // disconnect (see ReconnectRegistry). Populated by OnPeerDisconnected, consumed by
    // OnPeerConnected, swept by the existing status tick in _Process.
    //
    // Live verification of a real Steam-transport disconnect/reconnect exercising this
    // registry end-to-end is out of CI scope (no live Steam client/account here); it is
    // covered by the weekend manual Steam protocol added in Task 3, Step 11 (see
    // docs/superpowers/plans/2026-07-12-client-reconnection-plan.md).
    private readonly ReconnectRegistry _reconnects = new();
    // peer id -> SteamID64, cached at connect time (see OnPeerConnected) rather than
    // re-resolved at disconnect time. SteamPeer.DropServerSideConnection clears its
    // connId -> SteamID mapping BEFORE emitting PeerDisconnected, and Godot signal
    // dispatch is synchronous, so a SteamId64Of((int)id) call from inside
    // OnPeerDisconnected always resolves to 0 by the time it runs - the mapping it needs
    // is already gone. Caching the value while it's still reliably resolvable (at connect
    // time) sidesteps that ordering entirely.
    private readonly Dictionary<int, ulong> _peerSteamIds = new();
    // peer id -> the color index actually IN USE for this peer right now (see
    // SandboxAvatar.PaletteColorFor), cached at connect time for the exact same reason
    // _peerSteamIds is: the value only exists as an OnPeerConnected local, nowhere retrievable
    // at disconnect time otherwise. This is the value ResolveColorIndex settled on for THIS
    // connection - a resumed peer's restored index, or a fresh peer's newly dealt one - so a
    // later disconnect (see OnPeerDisconnected) captures the peer's actual live color, not
    // necessarily its raw spawn-order index (P11, Task A3).
    private readonly Dictionary<int, int> _peerColorIndex = new();

    // BT-7: the seed the random palette deal draws against, chosen ONCE per server process and
    // LOGGED, so "why was I bone twice in a row" is a question the log can answer and a playtest
    // colour layout can be reproduced by re-running with the same seed. Server-side only — the
    // colour itself still travels in the spawn args, so no client ever sees this number.
    //
    // A field rather than a per-call Random: see SandboxAvatar.RandomPaletteIndexFor for why the
    // deal is a pure function of (seed, spawnIndex) and not a running generator.
    private ulong _colorSeed;

    public override void _Ready()
    {
        // The shared replicated clock (ANIM-M3) is a running MAXIMUM, so it has to be cleared at
        // the top of every session: the gameplay scene can be entered more than once per process,
        // and the next server's tick series starts again at zero. Without this, a value left over
        // from a previous connection would swallow every --capture-at-tick mark in the next one.
        // Presentation only; nothing downstream of it decides anything.
        Net.NetClock.Reset();

        _playerScene = GD.Load<PackedScene>(ScenePaths.NetworkedAvatar);
        _spawner = GetNode<MultiplayerSpawner>("PlayerSpawner");
        _players = GetNode<Node3D>("Players");

        // Authored-model caches parse their glTF on first use; force that use HERE, at
        // scene setup before connecting, so an avatar/prop arriving mid-play never
        // stalls prediction with a load hitch (a stall rubberbands the local player).
        Sandbox.AvatarVisual.WarmModelCache();

        // Build the world locally before connecting (replication must not race scene setup).
        // Every peer builds its own — the world is static, not replicated.
        //
        // Logged because nothing else records it: a session where the client silently built a
        // different world than the one asked for (HostMenu used to force "playground" over an
        // explicit --world) is indistinguishable in the logs from one that worked. One line here
        // makes every log answer "which world was this?" without a repro.
        string worldId = NetworkManager.Instance.Options.World;
        GD.Print($"[world] building '{worldId}'");
        // BT-7: the random palette deal's seed, chosen once and PRINTED, because a random colour
        // nobody can reproduce is a colour nobody can file a bug about. SAIL_COLOR_SEED overrides
        // it, which is how a playtest's exact colour layout gets replayed.
        string seedOverride = OS.GetEnvironment("SAIL_COLOR_SEED");
        _colorSeed = ulong.TryParse(seedOverride, out ulong parsedSeed)
            ? parsedSeed
            : (ulong)Time.GetTicksUsec() ^ 0xD1B54A32D192ED03UL;
        GD.Print($"[avatar] palette seed {_colorSeed}"
            + (seedOverride.Length > 0 ? " (SAIL_COLOR_SEED)" : ""));
        // The startle overlay (DOOR-1), applied BEFORE the world is built, because the burst door
        // is authored inside TaskRoom.tscn and its _Ready runs the moment the world is added. It
        // reads StartleTuning.Current live at every stage rather than caching, so a late load
        // would not actually be wrong — but a tuning that is only correct because nothing read it
        // early is the shape of a bug waiting for the next lane, and this line costs nothing on
        // the launches (all of them but DOOR-1's smoke and Talon's own tuning runs) that pass no
        // file. Every warning is printed rather than swallowed: an overlay that silently did
        // nothing is indistinguishable from a knob that does not work.
        LoadStartleTuning(NetworkManager.Instance.Options.StartleFile);

        _world = BuildWorld(worldId);
        var worldNode = (Node3D)_world;
        worldNode.Name = "World";
        AddChild(worldNode);
        _connectingLabel = GetNode<Label>("Hud/ConnectingLabel");
        _roomCodeLabel = GetNode<Label>("Hud/RoomCodeLabel");
        // The scene authored this label 14 px off the bottom edge, inside the bottom-left block
        // world-anchored widgets share, so the room code could render underneath them and not be
        // read — Talon, 2026-08-14, and a room code you cannot read is a co-op session nobody can
        // join. Placed from the shared column here rather than left in the .tscn so the two cannot
        // drift apart; see Ui.Design.UiColumns.
        Ui.Design.UiColumns.PlaceRoomCode(_roomCodeLabel);

        // §7: the deployment scene measures itself (F3 readout; --perf-log for automated
        // profiling runs). Headless peers render nothing worth measuring.
        if (!NetworkManager.Instance.IsHeadless)
            AddChild(new Ui.PerfHud { Name = "PerfHud" });
        // INTERACTION-BIBLE 1: an interactable must READ as interactable before the player
        // touches it. The shimmer and the floating key chip both existed already, but were
        // only ever driven by the OFFLINE sandbox — so in the actual networked game the
        // authored props were indistinguishable from scenery until you pressed E and
        // something happened. Both worlds now drive the same helper. Headless peers render
        // nothing, so they poll nothing.
        if (!NetworkManager.Instance.IsHeadless)
        {
            Ui.InteractPrompt.Attach(this);
            // The refusal plate (CARRY-1, 2026-09-19). Beside the interact chip deliberately: the
            // chip says "here is the key for this thing" and this says "that key was refused, and
            // here is why". A refused action the player cannot perceive a REASON for is the defect
            // class INTERACTION-BIBLE §2/§3/§7 were written about; without this attach, every
            // RefusalNotice.Say in the game is a silent no-op.
            Ui.RefusalNotice.Attach(this);
            // The corner HUD (2026-08-08): the per-world corner widgets, HudProfile deciding which.
            // Replaces SessionHud and its rotating sun/moon disc, which Talon pulled ("this timer
            // is not working completely remove it"). Lambda-fed so the HUD never goes looking for
            // a manager itself — the avatar and the players node are both resolved per poll
            // because each is replaced outright on a reconnect resume.
            Ui.Hud.GameHud.Attach(this);
            _highlighter = new Sandbox.InteractHighlighter(LocalAvatar);
        }
        _spawner.SpawnFunction = new Callable(this, MethodName.SpawnPlayer);
        Voice.VoiceManager.Instance.BindPlayersRoot(_players);

        var net = NetworkManager.Instance;

        // The intercom's one knob (VOICE-1). Written before anything can speak, so the PA bus
        // reads it when it is lazily built; ApplyIntercomWetDb covers the case where a previous
        // session in this process already built the bus. 0 is the shipped character.
        Voice.VoiceConfig.IntercomWetDb = net.Options.IntercomWetDb;
        Voice.VoiceManager.ApplyIntercomWetDb();

        // Networked objects: the prop spawner + authoritative registry. Set up before connecting
        // so a prop spawn replicated to a joining client never races the spawn function being wired.
        _props = GetNode<Node3D>("Props");
        _propSpawner = GetNode<MultiplayerSpawner>("PropSpawner");

        // THE WORLD-STATE SLICES ARE FANNED AGAIN, from the round's reset edge (ROUND-1,
        // 2026-09-19). The old quota spine's WorldStateStore is still gone; what replaced it is
        // one subscription (see OnRoundResetRequested, and _hideSeekDriver's construction below),
        // because there are exactly two slices and a registry whose only job would be to call both
        // of them is a layer with nothing in it.
        //
        // THE THING BASE-1 SAID NOT TO LOSE, kept: ReconnectRegistry is fanned as well as
        // PropManager. A 60 s resume ticket into a world that has since been reset is an exploit,
        // not a courtesy - without it, a peer that dropped a second before the reset edge comes
        // back for up to a minute at its pre-reset position holding pre-reset props inside a
        // freshly restored room.

        _propManager = new PropManager { Name = PropManager.NodeName };
        AddChild(_propManager);
        _propManager.Setup(_propSpawner, _props, net.Role == NetworkManager.SessionRole.Server);
        // Server-side arbitration (grab proximity, disconnect-release drop position) needs a
        // peer id -> avatar node lookup; avatar node names are the owning peer id (see SpawnPlayer).
        _propManager.AvatarResolver = id => _players.GetNodeOrNull<Node3D>(id.ToString());
        // Authored props (a world's placed prop instances) are never spawned — they already exist
        // identically in every peer's copy of the world scene. This adopts them into the same
        // netcode funnel runtime-spawned props use (see PropManager.AdoptAuthoredProps); a no-op
        // for worlds with none, which is every world today (bubbletest is prop-free).
        _propManager.AdoptAuthoredProps(worldNode);

        // The entity funnel. Wired unconditionally so the spawn function is in place before
        // connecting — the same race PropSpawner above avoids — but nothing calls Spawn today, so
        // a normal session pays one GetNode.
        _entities = GetNode<Node3D>("Entities");
        _entitySpawner = GetNode<MultiplayerSpawner>("EntitySpawner");
        _entitySpawner.SpawnFunction = new Callable(this, MethodName.SpawnEntity);

        // The tidal-loop phase clock (BUILD-SPEC §3/§5): present in every world exactly like
        // PropManager above, so late-join and reconnect delivery ride the
        // same peer-connect funnel those already use (see the SendPhaseTo call in
        // OnPeerConnected). Harmless for a world with nothing bound to it — it just ticks.
        // Lean-MVP run shape (L1, Issue #104): request the session's default cycle period
        // (720s, design §1's ~7min day + 5min night) — a world that wants its own default
        // already calls RequestDefaultCyclePeriod from its own _Ready(), which — because
        // worldNode was already added to the tree above — has already run by the time this
        // executes, so that request wins; an explicit --cycle-period test hook wins over both
        // (see RequestDefaultCyclePeriod's own doc for the "0 = unset, first requester wins"
        // sentinel). This is only ever the fallback default for a world that asked for nothing
        // of its own.
        net.Options.RequestDefaultCyclePeriod(RunDriver.DefaultCyclePeriodSec);

        _cycleDriver = new CycleDriver { Name = CycleDriver.NodeName };
        AddChild(_cycleDriver);
        _cycleDriver.Setup(net.Role == NetworkManager.SessionRole.Server,
            net.Options.CyclePeriodSec, net.Options.CycleStartPhase,
            frozen: net.Options.CycleFreeze);
        // Print the resolution on whichever half of the session parsed the name, so a launch line
        // written as "--cycle-start-phase noon" can be checked against the number it became without
        // anybody having to recompute duskStart*0.5 for the day in play. The server prints the
        // frozen phase itself from CycleDriver.Setup; this line is about the NAME.
        if (net.Options.CycleStartPhaseName.Length > 0)
            GD.Print($"[cycle] --cycle-start-phase {net.Options.CycleStartPhaseName} -> " +
                     $"{net.Options.CycleStartPhase:F6} (day index {net.Options.CycleStartDay})");

        // The run driver (L1): typed phase-crossing events, the run-length/run-end contract, and
        // the reset hook — a layer above CycleDriver, present in every world exactly like it (see
        // RunDriver's own class doc). Added AFTER CycleDriver so it always has a real phase to
        // read once its own _PhysicsProcess starts (see that method's doc for why the ordering
        // is a nicety, not a correctness dependency).
        _runDriver = new RunDriver { Name = RunDriver.NodeName };
        AddChild(_runDriver);
        // RunCyclesOrUncapped, not RunCycles (CORE-PROG-A1, spec §1.5 / SD-1 D5): the
        // open-ended canon retires configured run length, so an unset --run-cycles resolves to
        // int.MaxValue — RunEndedSignal becomes structurally unreachable in real play without
        // one byte of RunDriver changing. An explicit --run-cycles keeps legacy semantics for
        // the existing suites. See RunCyclesOrUncapped's own doc for why the raw sentinel stays.
        _runDriver.Setup(net.Role == NetworkManager.SessionRole.Server,
            net.Options.RunCyclesOrUncapped, net.Options.CyclePeriodSec, net.Options.RunResetAtSec);

        // The hide-seek round (ROUND-1). Present on every peer exactly like the two drivers above,
        // so its own late-join delivery rides the same peer-connect funnel (see the
        // SendRoundStateTo call in OnPeerConnected). Added AFTER PropManager, because the reset
        // edge fans that manager's slice and a subscriber that fired before its subject existed
        // would be a null on the one tick that matters.
        //
        // The world cast is deliberately a soft one: every world gets a driver, and a world with
        // no named rooms simply never has anybody teleported. A hard cast here would make the
        // round a reason the CI slab worlds could not boot.
        _hideSeekDriver = new Round.HideSeekDriver { Name = Round.HideSeekDriver.NodeName };
        AddChild(_hideSeekDriver);
        // SOLO-1: --solo rides in on the tuning rather than as a second argument, because the
        // tuning is already the one object the driver, the copy and the board all read (MATCH-1's
        // rule), and a session-shaped fact that lived anywhere else would be a second place to
        // disagree about which session this is.
        bool isServer = net.Role == NetworkManager.SessionRole.Server;
        if (net.Options.Solo && !isServer)
        {
            // Loud rather than silent. The loop runs on the server only, so a tester who put the
            // flag on their client gets a game that still refuses to start with one player and
            // nothing anywhere saying why.
            GD.PushWarning("[round] --solo was given to a CLIENT and does nothing there — "
                           + "the round runs on the server, so put it on the server/host launch.");
        }
        _hideSeekDriver.Setup(isServer,
            _world as World.SupermarketWorld, _players,
            Round.HideSeekTuning.Current with { Solo = net.Options.Solo },
            net.Options.RoundScript);
        _hideSeekDriver.ResetRequested += OnRoundResetRequested;

        // THE INTERCOM (VOICE-1). Cross-room voice goes out on the PA route, same-room stays
        // proximity, and both halves are derived from the driver's room map so the client's route
        // and the server's relay exemption cannot disagree. Wired here, immediately after the
        // driver exists and on BOTH sides of the session, because the server half is not optional
        // on this world: VoiceProximityGate.DefaultForWorld is on for the supermarket (the rooms
        // are 40 m apart against a 24 m cutoff, deliberately), so a client-only wiring would
        // leave every cross-room packet culled at the relay and the intercom would be silent with
        // nothing in any log to say why. See VoiceIntercom's class doc.
        //
        // Only on a world that HAS rooms: elsewhere the relay stays exactly what it was.
        if (_world is World.SupermarketWorld)
        {
            Voice.VoiceIntercom.Wire(this, _hideSeekDriver,
                net.Role == NetworkManager.SessionRole.Server);
        }

        // REACH-1 (2026-09-19), program doc §5b layer 3. Registered on the SERVER only and
        // registered FIRST among the real sources, which matters: RoundFacts.Combine takes the
        // first NON-NULL TargetRetrievable in registration order, and this is the lane that owns
        // that fact. It answers null until it has measured something, so a server with no target
        // — every suite that does not pass --reach-target, and the whole holding room — is
        // byte-for-byte unaffected. The phase is passed as a lambda because this source is
        // constructed before the driver has stepped once.
        if (net.Role == NetworkManager.SessionRole.Server)
        {
            var reach = new Round.ReachabilityFactSource(_propManager,
                () => _hideSeekDriver.ServerState.Phase);
            if (net.Options.ReachTargetPropId >= 0)
                reach.TargetPropId = net.Options.ReachTargetPropId;
            _hideSeekDriver.Register(reach);
            _reachFacts = reach;

            if (net.Options.ReachSelfTest)
            {
                // The planted room's self-test quits the process when it is done, so this is a
                // dedicated run and nothing else about the session matters.
                AddChild(new World.ReachPlantSelfTest
                    { Name = "ReachPlantSelfTest", Props = _propManager });
            }
            if (net.Options.ReachCostSec > 0)
            {
                AddChild(new World.ReachCostProbe
                {
                    Name = "ReachCostProbe",
                    Props = _propManager,
                    DurationSec = net.Options.ReachCostSec,
                    // SHELF-1: <= 0 means never shove, which is how the at-rest frame time is
                    // measured at all. See LaunchOptions.CostShoveEverySec.
                    ShoveEverySec = net.Options.CostShoveEverySec,
                });
            }
        }

        // HOLD-1 (2026-09-19): the practice corner's snap pad, on the SERVER side.
        //
        // InteractionSlot is a client-side lab type and the server knows nothing about it
        // (CARRY-1's handoff says so in as many words), so a pad that was only a slot node would
        // snap in the feel lab and do nothing at all in a networked session. This is the shape
        // that handoff prescribes: a validator that reads the pads the server already has in its
        // own copy of the world and answers PlacementDecision.Snap(pad.RestPose(intended)).
        //
        // SERVER ONLY, and only when the world actually has pads. PropManager.PlacementValidator
        // stays null in every other case, which is the shipped free-placement default that the
        // whole carry verb is built on; a session with no practice corner is byte-for-byte
        // unchanged. PracticePadValidator.Next exists so TASK-1's tower pads chain behind this
        // rather than fighting over the one reference — see that class.
        if (net.Role == NetworkManager.SessionRole.Server)
        {
            var pads = new System.Collections.Generic.List<Sandbox.Feel.InteractionSlot>();
            CollectPracticePads(worldNode, pads);
            if (pads.Count > 0)
            {
                _propManager.PlacementValidator = new Round.PracticePadValidator { Pads = pads };
                ServerLog.Info("practice pads", $"count={pads.Count}");
            }
        }

        // The round's diegetic half (CLOCK-1, 2026-09-19): one 10 Hz poll that paints every
        // RoundClock on every wall and fires the round's audio cues off the same two views.
        //
        // NOT inside the !IsHeadless block above, and that is the load-bearing difference. This
        // is not chrome — it is the world saying the time out loud, and every client in tests/ is
        // headless, so a poll gated on a renderer would be a feature no suite in this repo can
        // reach. Attach() makes the narrower decision itself: never on a dedicated server unless
        // --log-clock asked for the suite's reference line, and playback off on any headless peer
        // while the cue DERIVATION still runs and logs.
        //
        // After the driver, because its very first poll reads HideSeekDriver.Instance.
        Round.RoundAudio.Attach(this);
        // BTN-1 (2026-09-19): the three round buttons, the object rack and the drop-off bin.
        //
        // AFTER the round driver, because it registers a fact source with it. AFTER REACH-1's
        // source, because that one must be FIRST among the real sources (the first-non-null
        // TargetRetrievable rule above) — this lane answers null to that fact precisely so it
        // could never win the race, and registering behind it as well makes the ordering a fact
        // rather than a property of one method. AFTER AdoptAuthoredProps, because the rack's
        // three objects must already carry their prop ids.
        //
        // Present on EVERY peer, like every node in this block: the lamp on each button is
        // DERIVED on each client from the round wire and the replicated holder view rather than
        // pushed, so nothing new rides the wire for the affordance (RoundControls.LampFactsFor).
        _roundControls = new Round.RoundControls { Name = Round.RoundControls.NodeName };
        AddChild(_roundControls);
        _roundControls.Setup(net.Role == NetworkManager.SessionRole.Server,
            _propManager, _players, _hideSeekDriver, worldNode,
            net.Options.ReachTargetPropId);
        // The RACK is the production path for the target's identity; --reach-target stays as the
        // dev-script stopgap REACH-1 shipped it as, and loses to the rack the moment a hider
        // picks something up, because the rack's answer is measured rather than typed.
        _roundControls.Reach = _reachFacts;

        // WHAT USED TO BE HERE, in one line each, because every one of these blocks carried a
        // "THIS IS THE ONLY CONSTRUCTION SITE" warning and deleting them is exactly the move those
        // warnings were written against: the lake (WaterService plus the chill overlays and the
        // splash FX), the flashlight, the goose honk, the failure/incapacitation states, and the
        // server-authoritative sight table. All five were pruned at the fork (BASE-1, 2026-09-19)
        // together with the systems that fed them - there is no lake, no night to need a torch, no
        // downed state and no darkness floor in three lit indoor rooms.
        //
        // TWO SEAMS THEY LEFT BEHIND, both deliberate and both still load-bearing:
        //   - MoveState still carries Water/Soaked/ControlLocked and Incapacity, and the motor
        //     still reads them (scripts/game/water/WaterGeometry.cs and
        //     scripts/game/failure/IncapacityContract.cs are kept for exactly that). Nothing
        //     writes them now, so every body resolves Dry and Active forever; the wire bytes are
        //     unchanged, which is why prediction and every net suite are untouched by this prune.
        //   - WaterGeometry.ActiveWaters is EMPTY by default here, so no room can accidentally sit
        //     inside the old camp lake's footprint and drown a player in a dry building.

        // L11's session summary seam (Issue #114) — see SessionSummaryProvider's own class doc
        // for exactly what's real vs. genuinely undefined (MapCoveragePercent — an open question
        // for Talon, not a value this pass may invent). _players is the exact same Players root
        // PropManager's own AvatarResolver delegate already reads.
        World.SessionSummarySource.Current = new World.SessionSummaryProvider(_players);

        // L11 (Issue #114): the loop UI shell — loading/hint overlay, phase toasts, session
        // summary. Client-rendered only, same gate as PerfHud/SessionHud/InteractPrompt above.
        // Added after RunDriver/CycleDriver so RunDriver.Instance/CycleDriver.Instance already
        // exist for these to poll/subscribe against in their own _Ready.
        if (!NetworkManager.Instance.IsHeadless)
        {
            // Toast layer FIRST, loading ground second — the reverse of the order this block
            // shipped in, and the swap is load-bearing. The goal line below is raised from the
            // ground's Dismissed event, and that event can fire synchronously inside the ground's
            // own _Ready (a host is its own authority and is Synced on the first frame), so the
            // layer that has to receive the line must already exist when the ground is added.
            // Nothing else cares about the order: two independent CanvasLayers on two numbers.
            var toasts = new Ui.PhaseToastLayer { Name = "PhaseToastLayer" };
            AddChild(toasts);

            var loading = new Ui.LoadingHintOverlay { Name = "LoadingHintOverlay" };
            // The level's goal, said once, at the one moment the player is actually looking at the
            // level — see LoadingHintOverlay.Dismissed for why _Ready itself is the wrong moment
            // (this ground is opaque and sits 40 layers above the toast).
            //
            // Every session, not once ever: it is a reminder, not onboarding, so there is no
            // "don't show this again" and nothing persisted. It still obeys the single run-level
            // suppression every panel obeys, so it can never land on a capture or a bot run.
            //
            // (The one-line goal toast that used to be raised here named the old level's
            // bubbles. This game's goal is a round, not a collectible, so ROUND-1 owns whatever
            // is said on entry - and the rule the old line was carrying still binds: copy that
            // names a mechanic the running world lacks is the defect, so gate any replacement on
            // the world THIS PEER BUILT, never on the autoload's options.)
            AddChild(loading);
            // The routed flow screens (round intro / tally / upgrade lobby / loss) and their
            // live views were the quota playthrough's surfaces and were pruned with it at the
            // fork (BASE-1, 2026-09-19). What is KEPT is the substrate they were built on and
            // the law they were built under: ScreenRouter, FlowScreenBase and ConnectingGate are
            // still here, and every one of them initialises by POLLING state after Synced rather
            // than by having witnessed a transition (the never-strand rule). ROUND-1's phase
            // screens are written against that same seam.
            //
            // The legacy --run-cycles session summary is likewise gone; RunDriver still ends a
            // capped run and nothing renders the end.
        }

        switch (net.Role)
        {
            case NetworkManager.SessionRole.Server:
                StartAsServer(net);
                break;
            case NetworkManager.SessionRole.Client:
                StartAsClient(net);
                break;
            default:
                net.LastError = "No session role set; returning to menu.";
                _finished = true;
                GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, ScenePaths.MainMenu);
                break;
        }
    }

    public override void _ExitTree()
    {
        // Normalize global input/UI state owned by nodes that may die without cleanup: an
        // involuntary exit (a disconnect bounce, a teardown mid-frame) otherwise strands the next
        // menu scene with a captured-invisible mouse.
        Input.MouseMode = Input.MouseModeEnum.Visible;
        // Same involuntary-exit defense as the two lines above: world UI is suppressed while an
        // inspect panel is open, and an involuntary teardown mid-inspect must not strand the next
        // scene with world UI permanently suppressed.
        Ui.WorldUi.Suppressed = false;
        // CORE-INT-1: the toast layer's flow-view seam is a static holding a reference into
        // THIS scene's driver — a stale value would make the next session's (or the menu's)
        // telegraph gate dereference a freed node. Same stale-static defense as the lines
        // above; the next Gameplay _Ready re-sets it.
        Ui.PhaseToastLayer.FlowView = null;
        Voice.VoiceManager.Instance.UnbindPlayersRoot();
        if (_serverSignals)
        {
            Multiplayer.PeerConnected -= OnPeerConnected;
            Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        }
        if (_clientSignals)
        {
            Multiplayer.ConnectedToServer -= OnConnectedToServer;
            Multiplayer.ConnectionFailed -= OnConnectionFailed;
            Multiplayer.ServerDisconnected -= OnServerDisconnected;
        }
    }

    // --- Server ------------------------------------------------------------------
    private void StartAsServer(NetworkManager net)
    {
        _isServer = true;
        ServerLog.Init(net.Options.LogDir, "matchserver.log");

        // --- Voice proximity gate (perf followups, 2026-08-07) ---------------------------------
        // The relay-side cull is compiled OFF (MpFoundation.Net.VoiceProximityGate.EnabledByDefault)
        // because turning it on reverses SubmitVoice's documented "the server never inspects
        // positions for voice" stance — Talon's call, not an agent's. --voice-gate on|off exists
        // so a measurement or test run can drive both arms from one build. Applied here, on the
        // SERVER only, because a client never relays anything.
        if (net.Options.VoiceGateOverride)
            MpFoundation.Net.VoiceProximityGate.Enabled = net.Options.VoiceGateOn;
        // Test-only (--voice-pa-all): makes every sender read as "on the PA" so the gate's
        // exemption branch is exercised by a live session instead of only compiling. See
        // LaunchOptions.VoicePaAll.
        //
        // BOTH hooks, as of VOICE-1, and that is not belt-and-braces. The relay now prefers the
        // PAIRWISE resolver over the per-talker one, so a blanket per-talker "everyone is on the
        // PA" set on a world whose intercom is wired would be silently outvoted by the room rule
        // and this flag would quietly stop meaning what it says. Setting both keeps "--voice-pa-all
        // means every packet is exempt" true on every world.
        if (net.Options.VoicePaAll)
        {
            Voice.VoiceManager.Instance.PaResolver = _ => true;
            Voice.VoiceManager.Instance.PaPairResolver = (_, _) => true;
        }

        _serverSignals = true;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;

        Error err = net.StartServer(net.PendingPort);
        if (err != Error.Ok)
        {
            ServerLog.Error("failed to start server",
                $"port={net.PendingPort} transport={net.Options.Transport} err={err} detail={net.LastError}");
            GetTree().Quit(1);
            return;
        }

        _startTicks = Time.GetTicksMsec();
        // stdout line the harness gates on before launching clients. The ENet wording is
        // load-bearing (test scripts match on it); the Steam line is informational.
        if (net.Options.Transport == LaunchOptions.TransportSteam)
            GD.Print($"[server] listening via Steam relay as {ServerAddress()}");
        else
            GD.Print($"[server] listening on udp/{net.PendingPort}");
        ServerLog.Info("listening", $"transport={net.Options.Transport} port={net.PendingPort} " +
            $"address={ServerAddress()} maxPlayers={Protocol.MaxPlayers} protocol={Protocol.Version}");
        ServerLog.Info("voice relay mode",
            $"gate={(MpFoundation.Net.VoiceProximityGate.Enabled ? "on" : "off")} " +
            $"enter={MpFoundation.Net.VoiceProximityGate.EnterRadiusM:F0}m " +
            $"exit={MpFoundation.Net.VoiceProximityGate.ExitRadiusM:F0}m");

        // --net-stats: the transport's own byte counters, once a second (see NetStatsLogger).
        // Attached only when asked for, and only after the peer exists — the first sample is
        // meaningless otherwise.
        if (net.Options.NetStatsLog.Length > 0)
        {
            var stats = new MpFoundation.Net.NetStatsLogger { Name = MpFoundation.Net.NetStatsLogger.NodeName };
            AddChild(stats);
            stats.Setup(net.Options.NetStatsLog);
        }

        // Populate the world's networked props (the deployment world's real prop set by
        // default; test worlds get theirs — see PropManager.SpawnInitialProps).
        _propManager.SpawnInitialProps(net.Options.World);

        // --spawn-index (INT-1): parsed once, here, so a malformed spec is reported at startup
        // beside the rest of the server's configuration rather than discovered as a bot that
        // quietly took the wrong marker. SpawnPin.Empty for every session that passes no flag.
        _spawnPins = World.SpawnPin.Parse(net.Options.SpawnIndexSpec);
        if (_spawnPins.Count > 0)
        {
            var pins = new List<string>();
            foreach (System.Collections.Generic.KeyValuePair<string, int> pin in _spawnPins.Pins)
                pins.Add($"{pin.Key}={pin.Value}");
            pins.Sort(System.StringComparer.Ordinal);
            ServerLog.Info("spawn pins", string.Join(",", pins));
        }
    }

    private void OnPeerConnected(long id)
    {
        int index = _spawnIndex++;
        ulong steamId = NetworkManager.Instance.SteamId64Of((int)id);
        // Cache now, while SteamPeer's connId -> SteamID mapping is still intact, so
        // OnPeerDisconnected has a reliable value to read later (see _peerSteamIds).
        _peerSteamIds[(int)id] = steamId;
        Vector3 spawn;
        int[] resumedHeldPropIds = System.Array.Empty<int>();
        int colorIndex;
        if (steamId != 0 && _reconnects.TryConsume(steamId, Time.GetTicksMsec() / 1000.0, out ReconnectRegistry.ResumeData resume))
        {
            // Found and not expired: resume at the saved position instead of a fresh spawn
            // point, re-grant whatever was held below (see the restore loop after Spawn), and
            // restore the palette color it had before the drop (P11, Task A3) instead of
            // dealing a fresh one - see ResolveColorIndex. Otherwise this player is
            // indistinguishable from a fresh joiner below - normal late-join prop dump, a
            // freshly dealt palette color.
            spawn = resume.Position;
            resumedHeldPropIds = resume.HeldPropIds;
            colorIndex = ResolveColorIndex(resumed: true, resume.ColorIndex, index);
            ServerLog.Info("peer reconnected", $"peer={id} resumedAt=({spawn.X:F2},{spawn.Y:F2},{spawn.Z:F2})");
        }
        else
        {
            spawn = SpawnPositionFor(index);
            // BT-7: the bubble test deals a RANDOM colour, every other world keeps the round
            // robin. Talon: "randomly assigned a color on join/start (no picking, no dedup logic
            // needed)." A resumed peer is untouched by this — it restores the index it already
            // had, above, exactly as before, so a reconnect never re-rolls a player's colour.
            // Canon §2.6 (whether the caveman's own colour varies) is OPEN and is not decided
            // here: this is one world's deal, not the game's.
            colorIndex = ResolveColorIndex(
                resumed: false, resumeColorIndex: -1,
                NetworkManager.Instance.Options.World == AvatarVisual.BoxKidWorldId
                    ? SandboxAvatar.RandomPaletteIndexFor(_colorSeed, index)
                    : index);
        }
        // Cache the color actually settled on above (a resumed peer's restored index, not
        // necessarily its raw spawn-order `index` — see _peerColorIndex's declaration comment),
        // so a later disconnect captures this peer's true live color.
        _peerColorIndex[(int)id] = colorIndex;
        // Spawn data carries the dealt pastel color: every peer (and every late joiner,
        // via the spawner's replay) builds this avatar identically from the same data —
        // cosmetics ride the spawn, not a synchronizer (§11 of the Art Bible: the wire
        // carries state, visuals are reconstructed locally).
        Color color = SandboxAvatar.PaletteColorFor(colorIndex);
        _spawner.Spawn(new Godot.Collections.Array { id, spawn, color });
        // P2: re-grant whatever this resumed peer held before the drop, one prop at a time,
        // through PropManager.TryRestoreHeldProp - the exact same SetHolder + ApplyPropState
        // (Held) path a live grab uses (release-then-restore design, not a new held state; see
        // that method's doc comment for the first-grab-wins fairness guard). BEFORE the dump
        // below, so a restored prop's dump entry (if any) is already Held, not a stale Resting
        // that would immediately get overwritten. Empty for a fresh joiner or an expired/absent
        // grace record - a no-op loop.
        foreach (int propId in resumedHeldPropIds)
            _propManager.TryRestoreHeldProp(propId, (int)id);
        // Late-join dump: bring the new peer's client-side prop view up to date with every
        // prop's current authoritative state (held props attach to holders, resting props place).
        // The spawner call above is synchronous locally, so the avatar node already exists.
        _propManager.SendDumpTo((int)id);
        // Cycle-clock late-join delivery (BUILD-SPEC §5's #1 likely bug): a joining OR
        // resumed peer must never fall back to CycleDriver's zero-initialized default before
        // this lands — send it now, same call site and reasoning as the prop dump above. A
        // reconnect over this codebase's ENet-bot harness (--force-reconnect-at) is assigned
        // a brand-new peer id and re-enters OnPeerConnected exactly like a fresh joiner (see
        // Gameplay's own comments on _peerSteamIds), so this one call site covers both.
        _cycleDriver.SendPhaseTo((int)id);
        // Run-state late-join delivery (L1, Issue #104): same call site, same reasoning — a
        // joining or resumed peer must know the run's length and whether it has already ended
        // before it renders anything, never the zero-initialized default (see
        // RunDriver.Synced's doc).
        _runDriver.SendRunStateTo((int)id);
        // Round late-join delivery (ROUND-1): the roster is appended FIRST — join order is what
        // decides who hides in the first round, and it is the order the message's score rows are
        // walked in — and then the one absolute message goes out. That message is the same one the
        // live broadcast sends, so a joiner and a peer who has been here all round hold views that
        // were built by the same code from the same bytes.
        _hideSeekDriver.ServerPeerJoined((int)id);
        _hideSeekDriver.SendRoundStateTo((int)id);
        // EVERY OTHER LATE-JOIN DUMP THAT USED TO BE HERE WENT WITH ITS SYSTEM (BASE-1,
        // 2026-09-19): playthrough state, the quota ledger, the failure states, the flashlight,
        // the water state and the sight table. The call site and its reasoning are what matter
        // to the lanes that follow: a joiner must be COMPLETE from this one funnel, because an
        // event fired before it arrived never reaches it. VOICE-1's room membership belongs on
        // these lines too, after the spawner call above so the new peer is already in whatever
        // table is being sent.
        ServerLog.Info("peer joined", $"peer={id} players={Multiplayer.GetPeers().Length}");
    }

    /// <summary>
    /// <b>The round's reset edge, fanned across the world-state slices</b> (ROUND-1, 2026-09-19).
    /// Raised on every peer by <c>HideSeekDriver</c>'s Tally -&gt; Holding transition; everything
    /// below is server-only, because every slice named here is.
    ///
    /// <para><b>Here rather than in the driver</b> because this is where both slices live —
    /// <see cref="_propManager"/> is a child of this node and <see cref="_reconnects"/> is a plain
    /// field on it. A driver reaching into either would be a round system that knows about prop
    /// authority, and the seam BASE-1 left is this one subscription.</para>
    ///
    /// <para><b>The reconnect registry is the half that is easy to forget and the one with an
    /// exploit behind it.</b> The props going home is visible the moment it fails; a resume ticket
    /// outliving the world it was issued in is not. A peer that drops a second before this edge
    /// would otherwise reconnect, for up to <see cref="ReconnectRegistry.WindowSec"/>, at its
    /// pre-reset position holding pre-reset props inside a freshly restored room. Clearing is
    /// sufficient and simpler than stamping each ticket with a round index: a reconnector after
    /// the edge joins as a fresh peer of the new round, which is what they are. Same-round resumes
    /// are untouched — this runs at the round boundary and nowhere else.</para>
    ///
    /// <para>Both slices are idempotent by their own contract, so a duplicate edge is harmless.</para>
    /// </summary>
    /// <summary>
    /// Applies <c>--startle-file</c> to <c>StartleTuning.Current</c> (DOOR-1). An empty path is
    /// the ordinary launch and touches no disk.
    ///
    /// <para><b>Every peer loads its own.</b> The staging is played from each peer's own copy of
    /// the timeline, so a server with a 0.8 s tell and a client with the shipped 0.4 s would
    /// burst 400 ms apart — visible, and exactly the class of disagreement the absolute wire
    /// exists to prevent everywhere else. That is stated rather than defended: making the tuning
    /// authoritative would mean putting fifteen floats on the round wire for a feel pass, and the
    /// honest answer during tuning is to pass the same file to both processes. It is in the
    /// handoff as an open item for the day a knob turns out to matter to fairness rather than to
    /// taste.</para>
    /// </summary>
    private static void LoadStartleTuning(string path)
    {
        if (path.Length == 0)
            return;
        Round.StartleTuningLoad load = Round.StartleTuningFile.LoadFrom(path);
        Round.StartleTuning.Current = load.Tuning;
        foreach (string warning in load.Warnings)
            GD.PushWarning($"[startle] {warning}");
        GD.Print($"[startle] overlay '{path}' existed={load.FileExisted} "
                 + $"discarded={load.Discarded} warnings={load.Warnings.Count} — "
                 + $"tell {Round.StartleTuning.Current.TellSec:0.000}s, "
                 + $"leaf {Round.StartleTuning.Current.LeafOpenDeg:0}deg "
                 + $"(overshoot {Round.StartleTuning.Current.LeafOvershootDeg:0}deg), "
                 + $"burst {Round.StartleTuning.Current.BurstImpulseNs:0.0} Ns / "
                 + $"{Round.StartleTuning.Current.BurstRadiusM:0.0} m, "
                 + $"kick {Round.StartleTuning.Current.KickDegrees:0.0}deg, "
                 + $"forceDrop={Round.StartleTuning.Current.ForceDropOnBurst}");
    }

    private void OnRoundResetRequested()
    {
        if (!_isServer)
            return;
        _propManager.ResetForNewPlaythrough();
        _reconnects.ResetForNewPlaythrough();
        ServerLog.Info("round reset", "slices=props,reconnect-registry");
    }

    /// <summary>Every <c>InteractionSlot</c> authored into the world, depth first. A method
    /// rather than an inline lambda for <see cref="EnumerateAvatars"/>'s reason: one home for
    /// the walk. It does NOT read <c>InteractionSlot</c>'s static registry, because that holds
    /// every slot loaded anywhere including the feel lab's, and a server-side rule should own
    /// the list of things it enforces (see <see cref="Round.PracticePadValidator.Pads"/>).</summary>
    private static void CollectPracticePads(Node n,
        System.Collections.Generic.List<Sandbox.Feel.InteractionSlot> found)
    {
        foreach (Node child in n.GetChildren())
        {
            if (child is Sandbox.Feel.InteractionSlot slot)
                found.Add(slot);
            CollectPracticePads(child, found);
        }
    }

    /// <summary>Every live avatar. A method rather than an
    /// inline lambda over <c>GetChildren()</c> so the null/type filtering has one home — the
    /// Players root also carries the spectate camera and, transiently, a node mid-free.</summary>
    private System.Collections.Generic.IEnumerable<SandboxAvatar> EnumerateAvatars()
    {
        foreach (Node child in _players.GetChildren())
            if (child is SandboxAvatar avatar)
                yield return avatar;
    }

    private void OnPeerDisconnected(long id)
    {
        Node3D? avatar = _players.GetNodeOrNull<Node3D>(id.ToString());
        // Read the connect-time-cached SteamID64 rather than re-resolving it here:
        // SteamPeer.DropServerSideConnection has already cleared the connId -> SteamID
        // mapping SteamId64Of would need, by the time this signal handler runs (see
        // _peerSteamIds's declaration comment). Remove the entry regardless of whether
        // Capture ends up firing below, so this dictionary never grows unbounded over a
        // long-running server's lifetime.
        ulong steamId = _peerSteamIds.Remove((int)id, out ulong cachedSteamId) ? cachedSteamId : 0;
        int colorIndex = _peerColorIndex.Remove((int)id, out int cachedColorIndex) ? cachedColorIndex : -1;

        // P2 (audit-documented ordering fix): capture which props this peer currently holds
        // BEFORE OnPeerLeft (below) releases them to Resting - a release clears the held state
        // this query reads, so this MUST run first (see PropManager.HeldPropIdsFor's doc
        // comment and ReconnectRegistry.Capture's).
        int[] heldPropIds = _propManager.HeldPropIdsFor((int)id);

        // The per-peer state the water, drowning and incapacitation services used to forget
        // here went with them at the fork (BASE-1, 2026-09-19). The rule they shared is exactly
        // why the round is told next: a departed peer left in a server-side dictionary is not
        // merely litter - one of them counted toward an all-players predicate, so a player who
        // disconnected at the wrong moment armed a loss condition forever. ENet peer ids are also
        // RECYCLED, so a stale entry lands on whoever joins next.
        //
        // ROUND-1's roster IS such a predicate - "exactly two humans" is read off it, and a role
        // holder missing from it is what ends a round early. Dropping the peer here also drops its
        // teleport cooldown, for the recycled-id half of the same rule.
        _hideSeekDriver.ServerPeerLeft((int)id);

        // Release everything this peer held BEFORE freeing its avatar node, so the drop
        // position derives from its still-valid last authoritative transform (and so no prop
        // stays bound to a node that's about to be freed). Props still drop immediately -
        // today's behavior, every existing invariant preserved; heldPropIds (captured above)
        // is what lets a successful resume re-grant them (see OnPeerConnected).
        _propManager.OnPeerLeft((int)id);

        // Steam-hosted matches only: a resolvable identity gets a 60s reconnect grace
        // window at its last position, plus whatever it held (restored on resume if still
        // available - see OnPeerConnected) and its dealt palette index. Unresolvable (e.g.
        // somehow on the ENet transport) skips straight to today's behavior below - no
        // pending record, so OnPeerConnected always spawns fresh for this peer.
        if (steamId != 0 && avatar != null)
            _reconnects.Capture(steamId, avatar.Position, heldPropIds, colorIndex, Time.GetTicksMsec() / 1000.0);

        avatar?.QueueFree();
        ServerLog.Info("peer left", $"peer={id} players={Multiplayer.GetPeers().Length}");
    }

    /// <summary>Runs on EVERY peer via the MultiplayerSpawner, so it is also where the entity's
    /// authority role is chosen: the server simulates and broadcasts (<c>ConfigureServer</c>),
    /// everyone else renders the interpolated snapshot stream (<c>ConfigureRemote</c>, keyed on
    /// <see cref="_isServer"/>). No entity kind is registered today — the first real NPC adds its
    /// kind dispatch here; an unknown kind throws rather than silently building the wrong
    /// thing.</summary>
    private Node SpawnEntity(Variant data)
    {
        var args = data.AsGodotArray();
        string kind = args[0].AsString();
        string name = args[1].AsString();
        throw new System.InvalidOperationException(
            $"unknown entity kind '{kind}' for '{name}' — no NetworkedEntity kind is registered");
    }

    private Node SpawnPlayer(Variant data)
    {
        var args = data.AsGodotArray();
        var player = _playerScene.Instantiate<SandboxAvatar>();
        player.Name = args[0].AsInt64().ToString();
        player.Position = args[1].AsVector3();
        if (args.Count > 2)
            player.BodyColor = args[2].AsColor();
        player.Props = _propManager; // networked carry funnel (Interact routes grab/drop through it)
        return player;
    }

    // internal, not private, for one reason: SupermarketWorldSelfTest asserts that
    // BuildWorld("supermarket") really returns a SupermarketWorld. That check is not a
    // formality — it is the one live proof that the registered id and the scene it loads have
    // not drifted apart. Same assembly, so no visibility is leaked outside it.
    internal static IGameWorld BuildWorld(string id)
    {
        // Which water this world has, before anything in it exists. Server and client both build
        // the world, so both land on the same footprint and the motor's depth test stays a world
        // constant — a reconciliation replay resolves the identical answer the live step did.
        // No world here has any (BASE-1, 2026-09-19), so this installs an empty set. The line
        // stays, and installing it EXPLICITLY per world is the point: ActiveWaters is a
        // process-wide static, and a world that inherits the previous one's shoreline drowns
        // players in a dry building.
        Sail.Game.Water.WaterGeometry.ActiveWaters = Sail.Game.Water.WaterGeometry.WatersForWorld(id);
        return BuildWorldScene(id);
    }

    // The one registered world. An unknown id THROWS rather than falling back: the old default
    // arm silently loaded a code-built testbed, and the first symptom was a playtester saying the
    // level did not load. Failing loudly at build time is the cheaper way to learn a launch line
    // is wrong.
    private static IGameWorld BuildWorldScene(string id) => id switch
    {
        // BASE-1 (2026-09-19): the three boxy rooms - holding, search, task - the whole game
        // is played in. Authored, not code-built (.claude/rules/godot-scenes.md).
        World.SupermarketWorld.WorldId =>
            GD.Load<PackedScene>(ScenePaths.Supermarket).Instantiate<IGameWorld>(),
        // REACH-1 (2026-09-19): the planted room — a copy of the search room with §5b's six
        // failure cases authored into it as real fixtures. Set only by --reach-selftest, never
        // typed by a player, and deliberately NOT the search room: every fixture in it is a
        // prop in an illegal pose on purpose.
        World.ReachPlantWorld.WorldId =>
            GD.Load<PackedScene>(World.ReachPlantWorld.ScenePath).Instantiate<IGameWorld>(),
        // The code-built CI scaffolding the scene suites run on: "open" is prop-free, "propsync"
        // is seeded by PropManager.SpawnInitialProps. Neither is reachable from an interactive
        // launch (HostMenu never passes --world).
        "open" or "propsync" => new GameWorld(),
        _ => throw new System.InvalidOperationException(
            $"unknown world id '{id}' — registered worlds are \"supermarket\" (played), "
            + "\"reachplant\" (REACH-1's planted room), \"open\" and \"propsync\" (CI)"),
    };

    // P11 (Task A3): the resumed-vs-fresh palette-color decision, pulled out as a pure static
    // function (no Node/state involved) so ReconnectSelfTest can exercise it directly without a
    // running scene tree. A resumed peer keeps the exact color it was dealt before the drop
    // (resumeColorIndex, from the grace record); a fresh joiner - no record, or an expired one -
    // gets the next round-robin index like any brand-new player, same as today. Internal (not
    // private) so the selftest, in the same assembly, can call it.
    //
    // KNOWN WALL (see task-A3-brief.md): OnPeerConnected's call to this can never actually take
    // the resumed=true branch in ENet-bot CI - NetworkManager.SteamId64Of always returns 0 for a
    // bot connection, so the TryConsume that would produce a real ResumeData never fires. This
    // pure function is the only seam that decision has for a logic-level proof; the live
    // Steam-transport path is Talon's next interactive session.
    internal static int ResolveColorIndex(bool resumed, int resumeColorIndex, int freshIndex) =>
        resumed ? resumeColorIndex : freshIndex;

    // P4 (Task A4): the outcome of re-resolving the room code against the Steam lobby directory
    // after a FAILED reconnect attempt. The host's own client is the sole owner of the lobby, so
    // a lobby that no longer resolves means the host is gone for good (ResetToOffline and the
    // server child's job-object both destroy it) — no amount of retrying will bring it back.
    internal enum LobbyProbe
    {
        NotChecked,   // nothing to re-resolve (a direct steam:<id64> joiner has no room code)
        Found,        // the lobby is up — the host is still there; retry as today
        Gone,         // authoritative "no such lobby": the host left for good — fail fast
        Inconclusive, // the query itself failed (Steam hiccup/timeout); not proof of anything
    }

    // The terminal decision the reconnect retry loop makes after a failed attempt.
    internal enum ReconnectDecision { Retry, HostGone, WindowExpired }

    // P4 (Task A4): the pure host-gone fast-fail decision, pulled out of ScheduleReconnectRetry so
    // the full (window-remaining x lobby-probe) matrix is CI-testable without a live Steam relay
    // (see SteamSelfTest.ReconnectFastFailDecision). A provably-gone lobby outranks everything —
    // the instant we KNOW the host is gone we stop, rather than burning the rest of the 60s grace
    // window on redials that can never land (the live-playtest bug this fixes). Otherwise the
    // pre-P4 behavior is untouched: retry while the window has time, give up when it runs out.
    // A Found/NotChecked/Inconclusive probe all fall through to that unchanged window logic — only
    // an authoritative Gone triggers the fast-fail, so a transient Steam error never gives up early.
    internal static ReconnectDecision DecideReconnect(bool withinWindow, LobbyProbe probe)
    {
        if (probe == LobbyProbe.Gone)
            return ReconnectDecision.HostGone;
        if (!withinWindow)
            return ReconnectDecision.WindowExpired;
        return ReconnectDecision.Retry;
    }

    // P4 (Task A4): maps a raw SteamLobby.FindHostByCodeAsync result to a LobbyProbe. Pure so the
    // load-bearing distinction — an honest "no such room" (fail fast) vs a query that errored
    // (keep trying) — is CI-tested, even though the query producing it is interactive-only.
    // FindHostByCodeAsync reports found=false with an EMPTY error only for the genuine
    // no-such-lobby outcome; any non-empty error means the lookup didn't get an authoritative
    // answer (timeout, no Steam session, a lookup already in flight).
    internal static LobbyProbe ClassifyLobbyResult(bool found, string error) =>
        found ? LobbyProbe.Found
        : error.Length > 0 ? LobbyProbe.Inconclusive
        : LobbyProbe.Gone;

    // Spawn from the world's declared points, or fall back to the phyllotaxis ring below
    // if it declares none; index wraps for late joiners.
    private Vector3 SpawnPositionFor(int index)
    {
        // --spawn-room (CARRY-1): spawn everyone at a NAMED room's markers instead of the world's
        // default array. The rooms are sealed boxes 40 m apart, so this is the only way a headless
        // suite can put a bot in front of the props authored in a room that is not the holding
        // room. Empty by default, so an ordinary session is byte-for-byte unchanged, and it is a
        // SERVER-side flag because the spawn position is computed here and replicated.
        // ROUND-1 owns the real thing (RoomTeleport on a phase change); this never moves anybody
        // after they spawn.
        if (NetworkManager.Instance?.Options.SpawnRoom is { Length: > 0 } room
            && _world is World.SupermarketWorld supermarket
            && supermarket.SpawnPointsFor(room) is { Count: > 0 } roomPoints)
            return roomPoints[index % roomPoints.Count];
        if (_world != null && _world.SpawnPoints.Count > 0)
            return _world.SpawnPoints[index % _world.SpawnPoints.Count];
        float angle = Mathf.DegToRad(137.5f * index);
        return new Vector3(Mathf.Cos(angle) * SpawnRadius, 1.1f, Mathf.Sin(angle) * SpawnRadius);
    }

    /// <summary>This peer's own avatar, or null before it spawns / after a reconnect frees it.
    /// Avatar node names are the owning peer id (see <see cref="SpawnPlayer"/>). Resolved per
    /// poll rather than cached because the node is replaced outright on a resume.</summary>
    private SandboxAvatar? LocalAvatar() =>
        _players != null && GodotObject.IsInstanceValid(_players)
            ? _players.GetNodeOrNull<SandboxAvatar>(Multiplayer.GetUniqueId().ToString())
            : null;

    public override void _Process(double delta)
    {
        // Affordance poll runs on every rendering peer, server-hosting or pure client — it is
        // local presentation, not authority, so it sits above the _isServer early-out.
        _highlighter?.Tick(delta);

        if (!_isServer)
            return;
        ApplySpawnPins();
        _sinceStatus += delta;
        if (_sinceStatus >= StatusIntervalSec)
        {
            _sinceStatus = 0;
            LogStatus();
            _reconnects.SweepExpired(Time.GetTicksMsec() / 1000.0);
        }
    }

    // --- --spawn-index (INT-1, 2026-09-19, packet ruling 6) ---------------------------------

    /// <summary>Parsed once from <c>--spawn-index</c>. <see cref="World.SpawnPin.Empty"/> in
    /// every session that does not pass the flag, which is every session a player ever starts.
    /// </summary>
    private World.SpawnPin _spawnPins = World.SpawnPin.Empty;

    /// <summary>Peers already pinned (or decided against), so the poll below does its work once
    /// per peer and then costs one set lookup. Keyed by peer id rather than by name because a
    /// name is what we are WAITING for and a peer id is what we have from the first frame.
    /// </summary>
    private readonly HashSet<int> _spawnPinned = new();

    /// <summary>
    /// <b>Moves a NAMED peer onto its pinned spawn marker, once, as soon as its name is known.
    /// </b> Server-only, and a no-op costing one <c>Count == 0</c> test in every session that
    /// passes no <c>--spawn-index</c>.
    ///
    /// <para><b>Why this is a poll and not part of <c>OnPeerConnected</c>, which is where every
    /// other spawn decision is made.</b> The spawn position is computed the instant a peer
    /// connects, and at that instant the server does not know who it is:
    /// <c>SandboxAvatar.DisplayName</c> is written by the OWNING CLIENT (see that property, and
    /// <c>SandboxAvatar</c>'s <c>Sync</c> child taking client authority) and arrives a frame or
    /// several later. So the choice is between inventing a new handshake field — which changes
    /// the payload shape and would owe a <c>ProtocolVersion</c> bump this wave has already
    /// spent — and waiting for a value that already replicates. This waits.</para>
    ///
    /// <para><b>It re-uses <see cref="World.RoomTeleport.ServerMove"/> rather than writing a
    /// position directly</b>, because that is the one place in this codebase that bumps the
    /// prediction epoch — without it the owning client rubber-bands back to where it spawned
    /// instead of snapping, which is the exact case
    /// <c>SandboxAvatar.ServerTeleportTo</c>'s own doc says it exists for. A pinned bot is moved
    /// before it has taken a step, so nothing is interrupted.</para>
    ///
    /// <para><b>It cannot be reached by a player.</b> The flag is the SERVER's; a client has no
    /// way to ask for a marker, and an unpinned name keeps the ordinary join-order deal. That is
    /// deliberately not the same as "harmless if a client could" — a self-teleport verb is an
    /// anti-cheat surface and this lane is not the place to open one.</para>
    /// </summary>
    /// <summary>Peers already told, once each, that their pin is waiting for somebody to move off
    /// the marker. This poll runs every frame on the server, so an unthrottled line here would be
    /// sixty a second; the wait is worth exactly one line and then silence.</summary>
    private readonly HashSet<int> _spawnPinWaitLogged = new();

    private void ApplySpawnPins()
    {
        if (_spawnPins.Count == 0 || _players == null || !GodotObject.IsInstanceValid(_players))
            return;
        // PASS 1 (INT-2 part B, 2026-09-21, ruling 5 -- PHYS-2 §2.1's spawn-pin collision).
        // Where is everybody whose name has NOT replicated yet? Those are the peers still sitting
        // on their join-order marker that this same poll is about to move, and teleporting a
        // named peer on top of one is what put SfxProduceBot at y = 2.30 on the witness's head.
        // Gathered first, because a pin has to be able to see who is standing where it wants to
        // land -- which a single pass that pins as it walks the children cannot.
        List<Vector3>? unsettled = null;
        foreach (Node child in _players.GetChildren())
        {
            if (child is not SandboxAvatar waiting)
                continue;
            if (_spawnPinned.Contains(waiting.OwnerPeerId)
                || !string.IsNullOrEmpty(waiting.DisplayName))
                continue;
            (unsettled ??= new List<Vector3>()).Add(waiting.GlobalPosition);
        }
        // PASS 2: pin whoever can be pinned without landing on one of them.
        foreach (Node child in _players.GetChildren())
        {
            if (child is not SandboxAvatar avatar)
                continue;
            int peer = avatar.OwnerPeerId;
            if (_spawnPinned.Contains(peer))
                continue;
            // Not pinned, or the name has not replicated yet. IndexFor answers -1 for both, and
            // the two are told apart by the name being empty: an avatar with no name yet is
            // asked again next frame rather than written off, which is the whole reason this is
            // a poll. An avatar whose name HAS arrived and is not in the map is settled forever.
            string name = avatar.DisplayName;
            if (string.IsNullOrEmpty(name))
                continue;
            int index = _spawnPins.IndexFor(name);
            if (index < 0)
            {
                // Named and not in the map: settled forever, on the ordinary join-order deal.
                _spawnPinned.Add(peer);
                continue;
            }
            Vector3 destination = SpawnPositionFor(index);
            if (World.SpawnPin.BlockedByAnUnsettledPeer(
                    destination, unsettled, World.SpawnPin.PinClearM))
            {
                // Somebody whose name has not arrived is standing on this marker. It is NOT
                // settled (no _spawnPinned.Add), so this peer is asked again next frame -- by
                // which time that occupant has a name and has been moved to wherever it belongs.
                if (_spawnPinWaitLogged.Add(peer))
                {
                    ServerLog.Info("spawn pin waiting",
                        $"peer={peer} name={name} marker={index} "
                        + $"at=({destination.X:F2},{destination.Y:F2},{destination.Z:F2}) "
                        + "- an unnamed peer is standing there");
                }
                continue;
            }
            _spawnPinned.Add(peer);
            bool moved = World.RoomTeleport.ServerMove(avatar, destination);
            ServerLog.Info("spawn pinned",
                $"peer={peer} name={name} marker={index} "
                + $"at=({destination.X:F2},{destination.Y:F2},{destination.Z:F2}) moved={moved}");
        }
    }

    // The heartbeat line. It used to append names=[…] — every player's typed display name,
    // written to the HOST'S disk on every status interval, in a file that rotates by size and
    // survives uninstall. The count is the whole diagnostic point of a heartbeat ("are people
    // still in here?"); the names only ever answered a question nobody was asking of a log.
    // Per-peer detail still exists where it is actually useful — the connect/reject lines —
    // and is pseudonymous there (LogIdentity).
    private void LogStatus()
    {
        int players = 0;
        foreach (Node child in _players.GetChildren())
        {
            if (child is SandboxAvatar)
                players++;
        }
        double uptime = (Time.GetTicksMsec() - _startTicks) / 1000.0;
        ServerLog.Info("status", $"uptime={uptime:F0}s players={players}/{Protocol.MaxPlayers}");
    }

    // This server's connectable identity: its Steam identity when players reach it
    // through the relay (what the host client publishes in the lobby), a loopback
    // endpoint over ENet (Practice/CI children are always same-machine spawns).
    private string ServerAddress() =>
        NetworkManager.Instance.Options.Transport == LaunchOptions.TransportSteam
            ? Net.Steam.SteamAddress.Format(Net.Steam.SteamService.ServerSteamId)
            : $"127.0.0.1:{NetworkManager.Instance.PendingPort}";

    // --- Client ------------------------------------------------------------------
    private void StartAsClient(NetworkManager net)
    {
        // Netcode CI hook: simulate latency/loss/jitter on this client's movement
        // messages, in-process. Absent flag = zero overhead (NetSim.Instance stays null).
        if (net.Options.NetSimEnabled)
            AddChild(new NetSim(net.Options));

        // Headless join-by-code (--join-room-code): stands in for what JoinMenu/HostMenu set
        // before changing scene, so CI can exercise the room-code gate over ENet. Guarded on
        // non-empty so an absent option never clobbers a menu-set code — no interactive path
        // changes behaviour.
        if (net.Options.JoinRoomCode.Length > 0)
            net.JoiningRoomCode = net.Options.JoinRoomCode;

        _clientSignals = true;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _connectingLabel.Visible = true;

        if (net.PendingRoom.Length > 0)
        {
            net.CurrentRoomCode = net.PendingRoom;
            ResolveThenConnect(net);
        }
        else if (net.PendingSteamId != 0)
        {
            // Direct steam:<id64> connect — the Join screen's advanced field, or the
            // Host flow connecting to its own server child. Consumed so a bounce back
            // to the menu can't replay it.
            ulong steamId = net.PendingSteamId;
            net.PendingSteamId = 0;
            ConnectSteam(steamId);
        }
        else
        {
            ConnectDirect(net.PendingHost, net.PendingPort);
        }
    }

    // Room-code resolve rides the Steam lobby directory (SteamLobby): the host's client
    // published {code -> server identity} as lobby metadata; we filter the public lobby
    // list for it. Completion lands on the main thread (SteamService.Pump), but the
    // scene may have moved on — guard before touching the tree.
    private async void ResolveThenConnect(NetworkManager net)
    {
        // async void: an exception escaping here would take the whole client down unrouted
        // (no awaiter to observe it) — route every unexpected throw into the normal Fail path.
        try
        {
            string room = net.PendingRoom;
            uint appId = SteamService.ResolveAppId(net.Options);
            if (!SteamService.EnsureClient(appId))
            {
                Fail(SteamService.LastError);
                return;
            }
            (bool found, ulong hostSteamId, string error) = await SteamLobby.FindHostByCodeAsync(room);
            if (_finished || !IsInsideTree())
                return;
            if (error.Length > 0)
            {
                Fail($"Room lookup failed: {error}");
                return;
            }
            if (!found)
            {
                Fail($"Room \"{room}\" not found.");
                return;
            }
            ConnectSteam(hostSteamId);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[client] room resolve threw {e.GetType().Name}: {e.Message}");
            if (!_finished && IsInsideTree())
                Fail("Room lookup failed unexpectedly.");
        }
    }

    private void ConnectDirect(string host, int port)
    {
        _enetHost = host;
        _enetPort = port;
        Error err = NetworkManager.Instance.StartClient(host, port);
        if (err != Error.Ok)
        {
            Fail(NetworkManager.Instance.LastError);
            return;
        }
        // Same stale-attempt guard as the Steam watchdog (OnSteamConnectTimeout): in the
        // forced-reconnect flow a timer from the original connect could otherwise fire during
        // the retry window and schedule a duplicate reconnect cycle.
        int attempt = ++_connectAttempt;
        GetTree().CreateTimer(ConnectTimeoutSec).Timeout += () => OnSteamConnectTimeout(attempt);
    }

    private void ConnectSteam(ulong serverSteamId)
    {
        // A reconnect retry (see OnServerDisconnected) calls this again on the same
        // Gameplay instance; close whatever peer is still assigned first so a dead SteamPeer
        // from a failed attempt never leaks its relay connection/callback subscription. A
        // no-op on the very first connect of a session (default peer is an offline no-op).
        Multiplayer.MultiplayerPeer?.Close();
        // Audit P1: before EVERY redial attempt of a reconnect sequence (not just the first —
        // idempotent, so a retry after a failed attempt costs nothing extra), free every stale
        // replicated node this client is still holding from the dropped session. Otherwise the
        // resumed handshake's MultiplayerSpawner replay stacks a second copy of every peer/prop
        // on top of the frozen originals (name-colliding duplicates, a permanently-ghosted own
        // body) — see TeardownReplicatedNodes.
        if (_reconnecting)
            TeardownReplicatedNodes();
        _steamServerId = serverSteamId;
        Error err = NetworkManager.Instance.StartClientSteam(serverSteamId);
        if (err != Error.Ok)
        {
            if (_reconnecting)
            {
                ScheduleReconnectRetry();
                return;
            }
            Fail(NetworkManager.Instance.LastError);
            return;
        }
        // Relay connects ride cert provisioning + route probing on a cold Steam session;
        // give them meaningfully longer than a LAN ENet connect before declaring death.
        int attempt = ++_connectAttempt;
        GetTree().CreateTimer(SteamConnectTimeoutSec).Timeout += () => OnSteamConnectTimeout(attempt);
    }

    /// <summary>Stale-attempt guard shared by both connect watchdogs (ConnectSteam's 25s and
    /// ConnectDirect's ENet timer): a connect is re-attempted on every retry, but a previous
    /// attempt's CreateTimer watchdog isn't cancelled when that attempt resolves through a
    /// faster path (e.g. OnConnectionFailed) — without this check, the orphaned timer fires
    /// later, doesn't know it's stale, and forces a spurious extra reconnect cycle on top of
    /// whatever attempt is currently in flight.</summary>
    private void OnSteamConnectTimeout(int attempt)
    {
        if (attempt != _connectAttempt)
            return;
        OnConnectTimeout();
    }

    private void OnConnectedToServer()
    {
        // Belt-and-suspenders defensive check (audit P1) — see ConnectSteam's call site for the
        // full rationale (this is the same idempotent teardown, called defensively again here in
        // case a future call path reaches a resumed connect without going through ConnectSteam).
        if (_reconnecting)
            TeardownReplicatedNodes();
        _connected = true;
        _reconnecting = false;
        _connectingLabel.Visible = false;
        var net = NetworkManager.Instance;
        if (net.CurrentRoomCode.Length > 0)
        {
            // The creator (and code-joiners) can always read the code off the HUD to share it.
            _roomCodeLabel.Text = $"Room: {net.CurrentRoomCode}";
            _roomCodeLabel.Visible = true;
        }
        GD.Print($"[client] connected as peer {Multiplayer.GetUniqueId()}");
        if ((net.IsBot || net.PracticeSelfTest) && _harness == null)
        {
            _harness = new BotHarness();
            _harness.Setup(_players, _propManager, net.Options, _entities);
            AddChild(_harness);
        }
        // Test-only (--force-reconnect-at): arm once, on the very first connect, a one-shot
        // timer that simulates a drop+resume over ENet - see SimulateForcedReconnect's doc
        // comment for why this stands in for the real (Steam-only, non-bot) retry loop in CI.
        if (!_forceReconnectArmed && net.Options.ForceReconnectAtSec >= 0)
        {
            _forceReconnectArmed = true;
            GetTree().CreateTimer(net.Options.ForceReconnectAtSec).Timeout += SimulateForcedReconnect;
        }
    }

    private void OnConnectTimeout()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _connected || _finished)
            return;
        if (_reconnecting)
        {
            ScheduleReconnectRetry();
            return;
        }
        Fail("Connection timed out");
    }

    private void OnConnectionFailed()
    {
        if (_reconnecting)
        {
            ScheduleReconnectRetry();
            return;
        }
        Fail("Could not connect to server");
    }

    private void OnServerDisconnected()
    {
        var net = NetworkManager.Instance;
        if (_reconnecting)
        {
            // The in-flight retry attempt itself dropped before completing the handshake;
            // treat exactly like a failed connection attempt (see OnConnectionFailed) -
            // the shared deadline set when we first entered reconnect mode still applies.
            ScheduleReconnectRetry();
            return;
        }
        // Steam-transport only (ConnectSteam sets _steamServerId), and never for bot/practice
        // clients (they must die immediately - a silent retry loop reads as a hang to the
        // test scripts, and this feature is for real players only).
        if (_steamServerId != 0 && !(net.IsBot || net.PracticeSelfTest))
        {
            _reconnecting = true;
            _connected = false;
            _reconnectDeadlineMsec = Time.GetTicksMsec() + (ulong)(ReconnectRegistry.WindowSec * 1000);
            _connectingLabel.Text = "Reconnecting...";
            _connectingLabel.Visible = true;
            ConnectSteam(_steamServerId);
            return;
        }
        Fail("Disconnected from server");
    }

    /// <summary>Called after a reconnect attempt fails (synchronously, by timeout, or by an
    /// immediate re-disconnect) while still within the 60s grace window. P4 (Task A4): before
    /// waiting to retry, re-resolve the room code against the Steam lobby directory — if the
    /// lobby is gone the host left for good, so give up immediately ("Host ended the session")
    /// instead of burning the rest of the window on redials that can never land (the live-playtest
    /// bug: 60s of futile "Reconnecting..." after the host closed). Lobby still up, or nothing to
    /// re-resolve (direct steam:<id64> joiner), or the query itself hiccuped ⇒ wait a short beat
    /// (so a downed relay/server isn't hammered) then retry via ConnectSteam, exactly as before.
    /// Past the deadline, give up and fall through to the normal Fail() bounce-to-menu.
    ///
    /// The re-resolve is deliberately NOT done before the FIRST attempt (that path is
    /// OnServerDisconnected → ConnectSteam directly): a momentary blip shouldn't pay a lobby query.
    /// async void mirrors ResolveThenConnect; the lobby query lands on the main thread
    /// (SteamService.Pump), so the post-await tree guard is required.</summary>
    private async void ScheduleReconnectRetry()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _connected || _finished)
            return;
        // Reaching here means the current attempt has resolved (in failure); retire its
        // generation NOW rather than waiting for the next ConnectSteam to bump the counter.
        // Otherwise there's a residual window: an attempt that fails via a non-watchdog path
        // 23-25s after arming leaves its watchdog to fire during this 2s retry delay (now also
        // the lobby-query window), while the counter still matches - which would arm a duplicate
        // retry timer that closes the next attempt mid-negotiation.
        _connectAttempt++;

        // P4: re-resolve the room code (interactive-only Steam query; NotChecked when there's no
        // code to resolve, e.g. a direct steam:<id64> joiner) to learn whether the host is still
        // there. See DecideReconnect for how the probe combines with the remaining window.
        var net = NetworkManager.Instance;
        LobbyProbe probe = LobbyProbe.NotChecked;
        if (net.CurrentRoomCode.Length > 0)
        {
            probe = await ProbeHostLobby(net.CurrentRoomCode);
            // The query can take up to its timeout; the scene may have moved on / resumed / failed.
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _connected || _finished)
                return;
        }

        switch (DecideReconnect(Time.GetTicksMsec() < _reconnectDeadlineMsec, probe))
        {
            case ReconnectDecision.HostGone:
                _reconnecting = false;
                Fail("Host ended the session");
                return;
            case ReconnectDecision.WindowExpired:
                _reconnecting = false;
                Fail("Could not reconnect to server");
                return;
            default: // Retry
                GetTree().CreateTimer(ReconnectRetryDelaySec).Timeout += RetryReconnect;
                return;
        }
    }

    /// <summary>P4 (Task A4): re-resolves a room code to a live/dead verdict via the Steam lobby
    /// directory (SteamLobby.FindHostByCodeAsync). Thin wrapper: it exists only to run the Steam
    /// query and hand its raw outcome to the pure ClassifyLobbyResult.
    ///
    /// INTERACTIVE-ONLY — this is the one line of A4 that CI cannot exercise. FindHostByCodeAsync
    /// needs a live Steam client session and the real relay; headless CI has neither (no Steam
    /// account, and the ENet reconnect bots have no lobby at all — they take the NotChecked path
    /// above and never reach here). The decision this feeds (DecideReconnect) and the raw-result
    /// classification (ClassifyLobbyResult) are both pure and fully covered in SteamSelfTest; the
    /// live re-resolve itself is verified only in Talon's next interactive Steam session. Per the
    /// Global Constraints, NO lobby/identity shim is added to force this path to fire in CI.</summary>
    private static async Task<LobbyProbe> ProbeHostLobby(string roomCode)
    {
        (bool found, ulong _, string error) = await SteamLobby.FindHostByCodeAsync(roomCode);
        return ClassifyLobbyResult(found, error);
    }

    private void RetryReconnect()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _connected || _finished || !_reconnecting)
            return;
        ConnectSteam(_steamServerId);
    }

    /// <summary>Audit P1: frees every client-side replicated node this peer is still holding a
    /// stale copy of — every child of <see cref="_players"/> (each avatar, AND each avatar's own
    /// locally-created <see cref="Sandbox.SandboxCamera"/>, which SandboxAvatar's local-spawn
    /// branch parents as a _players SIBLING, not the avatar's own child — see
    /// SandboxAvatar.ConfigureNetworkedInstance's "GetParent().AddChild(camera)" call) and every
    /// runtime-spawned child of <see cref="_props"/>.
    ///
    /// Authored props (e.g. Playground.tscn's placed Crate/Sphere instances) are ADOPTED, never
    /// spawned (see PropManager.AdoptAuthoredProps) — they live under the World node, not
    /// _props, so this never touches them. That is deliberate, not an oversight: they exist
    /// identically in every peer's copy of the world scene both before and after a resume (the
    /// .tscn instance itself never goes anywhere), and the server's post-resume dump
    /// (PropManager.SendDumpTo) re-syncs each one's live state onto the SAME long-lived instance
    /// via the ordinary ApplyPropState funnel — there is no stale authored-prop copy to free.
    ///
    /// Idempotent (freeing an already-empty container is a no-op), so this is safe to call
    /// before every redial attempt in a reconnect sequence, not just the first, and again
    /// defensively once the resume actually completes (see call sites in ConnectSteam and
    /// OnConnectedToServer). Also clears PropManager's client-side held-by-peer bookkeeping,
    /// which would otherwise dangle on a node this just freed.</summary>
    private void TeardownReplicatedNodes()
    {
        foreach (Node child in _players.GetChildren())
            child.Free();
        foreach (Node child in _props.GetChildren())
            child.Free();
        // Entities replicate through a MultiplayerSpawner exactly like players and props, so the
        // resumed handshake replays them too — without this they stack a second copy under the
        // "@"-suffixed auto-rename (audit P1's symptom). Empty today, but
        // the first real NPC would otherwise inherit the bug this teardown exists to prevent.
        foreach (Node child in _entities.GetChildren())
            child.Free();
        _propManager.ClientResetHeldState();
    }

    /// <summary>Test-only (--force-reconnect-at, see LaunchOptions): fires once, a fixed delay
    /// after this client's first successful connect. Headless CI has no live Steam relay/account,
    /// so the REAL reconnect trigger (OnServerDisconnected's Steam-transport branch, gated off
    /// for bots by design — "a silent retry loop reads as a hang to the test scripts") can never
    /// fire in a bot run. This simulates the same drop+resume shape over the transport CI
    /// actually has (ENet, direct address) so Run-ReconnectTest.ps1 can drive the SAME production
    /// teardown code (TeardownReplicatedNodes, the defensive re-check in OnConnectedToServer)
    /// under TDD, instead of only ever exercising it interactively. Deliberately minimal: unlike
    /// the real retry loop, this does not retry on failure (ScheduleReconnectRetry still only
    /// knows how to redial via ConnectSteam) — the test's server is never actually gone, so a
    /// single redial attempt is expected to succeed every time.</summary>
    private void SimulateForcedReconnect()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _finished)
            return;
        _reconnecting = true;
        _connected = false;
        _reconnectDeadlineMsec = Time.GetTicksMsec() + (ulong)(ReconnectRegistry.WindowSec * 1000);
        TeardownReplicatedNodes();
        Multiplayer.MultiplayerPeer?.Close();
        ConnectDirect(_enetHost, _enetPort);
    }

    /// <summary>Client-side terminal failure: a specific handshake error wins over the generic
    /// one, and the player goes back to the screen they actually started from — Host for a
    /// host, Join for a joiner (Net.SessionFailureRoute; F1 of the 2026-08-30 master review).</summary>
    private void Fail(string message)
    {
        if (_finished)
            return;
        _finished = true;
        var net = NetworkManager.Instance;
        string reason = net.HandshakeError.Length > 0 ? net.HandshakeError : message;
        net.HandshakeError = "";
        // Read the flag BEFORE the teardown: ResetToOffline stops the server child, leaves
        // (and so destroys) the lobby, and clears IsHostFlow with the rest of the session.
        // Sampling it afterwards would route every host to the Join screen — the bug itself.
        (string scene, string shown) = Net.SessionFailureRoute.For(net.IsHostFlow, reason);
        net.LastError = shown;
        net.ResetToOffline();
        if (net.IsBot || net.PracticeSelfTest)
        {
            // Headless harness clients (bots, --practice, --host-selftest) must DIE with
            // the reason, not bounce to a menu no one is looking at — a silent bounce
            // reads as a hang to the test scripts. The RAW reason, not the routed message:
            // the harness greps for the failure, not for the player-facing dressing.
            GD.PrintErr($"[bot] {reason}");
            GetTree().Quit(1);
            return;
        }
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, scene);
    }

    /// <summary>Voluntary exit from the pause overlay.</summary>
    public void LeaveToMenu()
    {
        if (_finished)
            return;
        _finished = true;
        var net = NetworkManager.Instance;
        net.ResetToOffline();
        net.StopPracticeServer();
        GetTree().ChangeSceneToFile(ScenePaths.MainMenu);
    }

    /// <summary>Teleports the local player back to its spawn point (client-local; propagates via sync).</summary>
    public void ResetLocalPlayerPosition()
    {
        int myId = Multiplayer.GetUniqueId();
        foreach (Node child in _players.GetChildren())
        {
            if (child is SandboxAvatar player && player.GetNode<MultiplayerSynchronizer>("Sync").GetMultiplayerAuthority() == myId)
            {
                player.ResetToSpawn();
                return;
            }
        }
    }
}
