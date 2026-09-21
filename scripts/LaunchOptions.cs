using System.Collections.Generic;
using System.Globalization;
using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation;

/// <summary>
/// Command-line options passed after Godot's "--" separator, e.g.:
///   godot --headless -- --server --port 7777                         (ENet host, LAN/CI)
///   godot --headless -- --server --transport steam                   (Steam relay host)
///   godot --headless -- --bot --room ABCDEF --name Bot1 --log out.jsonl --duration 15
///   godot --headless -- --bot --address 127.0.0.1:7777 --name Bot1   (direct connect)
/// </summary>
public sealed class LaunchOptions
{
    public const int DefaultPort = MpFoundation.Net.NetProfile.DefaultPort;

    public bool Server { get; private set; }
    public bool Bot { get; private set; }
    public bool Practice { get; private set; }                  // headless self-test of Practice Mode
    /// <summary>--spectate-cam: attach a follow-camera to a --bot avatar so a windowed bot
    /// renders a real gameplay view (marketing screenshots / capture). Additive and off by
    /// default — headless CI never sets it, so bot behavior is unchanged.</summary>
    public bool SpectateCam { get; private set; }

    /// <summary>--first-person-cam: attach the REAL <c>FirstPersonCamera</c> to a --bot avatar, so
    /// a windowed bot renders (and captures) through the rig a player looks through rather than
    /// through the orbit camera <see cref="SpectateCam"/> builds. View-only and off by default —
    /// the bot's intent source, movement and logging are untouched, and the cursor is deliberately
    /// NOT captured: a suite that seizes the mouse of whoever is at the keyboard is a suite nobody
    /// runs twice (FP-1, 2026-09-19).</summary>
    public bool FirstPersonCam { get; private set; }

    /// <summary>--first-person-selftest: <see cref="FirstPersonCam"/> plus the probe that measures
    /// the rig and prints <c>[fp-selftest] SUMMARY failures=&lt;n&gt; result=PASS|FAIL</c>. What
    /// <c>tests/Run-FirstPersonTest.ps1</c> gates on. Implies the camera, so a runner cannot ask
    /// for the measurement of a rig it forgot to build.</summary>
    public bool FirstPersonSelfTest { get; private set; }

    /// <summary>--rotate-look-selftest: the FP-1 x CARRY-1 input-routing probe (INT-0, 2026-09-19).
    /// Attaches a real <see cref="Sandbox.HeldPropRotator"/> to a <c>--bot</c> avatar beside its
    /// real <see cref="Sandbox.FirstPersonCamera"/>, injects synthetic mouse motion with the
    /// rotate modifier held and again with it released, and prints
    /// <c>[rotlook-selftest] SUMMARY failures=&lt;n&gt; result=PASS|FAIL</c>.
    ///
    /// <para><b>Why a flag rather than a bot script.</b> The two behaviours under test are a
    /// property of Godot's input ORDER — the rotator consumes mouse motion in <c>_Input</c>, the
    /// camera reads it in <c>_UnhandledInput</c> — and nothing that steers a bot through
    /// <c>IIntentSource</c> can produce a mouse event at all. The rotator also only attaches on the
    /// human branch, so a bot has none; this builds one through the SAME public
    /// <c>HeldPropRotator.Attach</c> the human branch calls, which is the pattern
    /// <see cref="FirstPersonCam"/> already established for the camera.</para>
    ///
    /// <para>Implies <see cref="FirstPersonCam"/>: the whole question is which of the two rigs gets
    /// the motion, so measuring it without the camera would be measuring nothing.</para></summary>
    public bool RotateLookSelfTest { get; private set; }

    /// <summary>--fp-look &lt;yawDeg&gt;[,&lt;pitchDeg&gt;]: the initial look angles for a
    /// <see cref="FirstPersonCam"/> run, degrees, same convention as <c>SandboxCamera.Yaw</c>
    /// (0 = facing −Z, positive = turning left). Pitch is clamped by the rig.
    ///
    /// <para><b>A capture harness for a mouse-look game has to be able to aim.</b> A bot brain
    /// decides where it WALKS; in first person that no longer decides where it LOOKS, so without
    /// this every first-person capture in the repo would be pointed wherever yaw zero happens to
    /// face. This is how FP-1's two-client evidence puts two bodies in front of each other's
    /// lenses, and CARRY-1, SHELF-1 and DOOR-1 will each want it for their own shots. View-only:
    /// it sets the CAMERA, never the intent, so the bot walks exactly where it was going to.</para></summary>
    public bool HasFirstPersonLook { get; private set; }

    /// <summary>Initial look yaw, RADIANS (the flag is given in degrees). Only read when
    /// <see cref="HasFirstPersonLook"/>.</summary>
    public float FirstPersonLookYaw { get; private set; }

    /// <summary>Initial look pitch, RADIANS. Defaults to level.</summary>
    public float FirstPersonLookPitch { get; private set; }

    /// <summary>--windowed: force this launch into a window, whatever the persisted display
    /// setting says, and WITHOUT writing anything back.
    ///
    /// <para>The fullscreen default (playtest note 2) is applied on one branch of
    /// <see cref="MpFoundation.Boot"/> — the real windowed client — so no server, bot, practice,
    /// self-test or capture launch can reach it in the first place. This flag exists for the case
    /// that gate does not cover: a human running the ACTUAL client path to look at something, on
    /// a machine that is also running headed suites. It is a read-only override; the player's
    /// stored preference is untouched, so a debugging session cannot silently change what the
    /// next real launch does.</para></summary>
    public bool Windowed { get; private set; }

    // --- SICK-1 (2026-09-20): the comfort flags ----------------------------------------------
    //
    // Talon, riding the MVP for the first time: "The game is making me motion sick already."
    // These five are the A/B surface that answer came with. Their defaults ARE the shipped
    // build — every one of them is the value the measurement in
    // docs/agents/handoffs/2026-09-20-SICK-1.md chose — so a launch with none of them given is
    // the recommended configuration and each flag is how you get the other one back.

    /// <summary><c>--cam-interp 0|1</c>, default <b>1</b>. Interpolate the first-person eye
    /// between physics ticks instead of moving it once per tick. 0 is the 2026-09-19 build.
    /// See <see cref="Game.Sandbox.FirstPersonCamera.InterpolateToRenderFrame"/>.
    /// <b>Default 1</b>, on the measurement in that property's own doc.</summary>
    public bool CameraInterpolation { get; private set; } = true;

    /// <summary><c>--fov &lt;deg&gt;</c>, default
    /// <see cref="Game.Sandbox.FirstPersonCamera.DefaultFovDeg"/>. <b>VERTICAL</b> degrees —
    /// see that constant for why the horizontal number is the one a player means, and what this
    /// one comes to on each aspect.</summary>
    public float FovDeg { get; private set; } = Game.Sandbox.FirstPersonCamera.DefaultFovDeg;

    /// <summary><c>--vsync 0|1</c>, default <b>1</b> (the engine default this project has always
    /// had, restated so it can be turned off for a measurement).</summary>
    public bool Vsync { get; private set; } = true;

    /// <summary><c>--max-fps &lt;n&gt;</c>, 0 = uncapped (the default). A cap is only a comfort
    /// setting on a machine where the frame time is not steady; with vsync on it is redundant.</summary>
    public int MaxFps { get; private set; }

    /// <summary><c>--pacing-log &lt;path&gt;</c>: the SICK-1 per-frame instrument's CSV. Empty
    /// means no instrument is built at all — see <see cref="Game.Sandbox.CameraPacingProbe"/>.</summary>
    public string PacingLogPath { get; private set; } = "";

    /// <summary><c>--pacing-sec &lt;s&gt;</c>: how long to record. 30 s is the packet's window.</summary>
    public double PacingSec { get; private set; } = 30.0;

    /// <summary><c>--pacing-turn &lt;degPerSec&gt;</c>: a constant scripted look turn during the
    /// recording, so yaw-step variance is measured under the stimulus that produces it.</summary>
    public float PacingTurnDegPerSec { get; private set; }

    /// <summary><c>--pacing-label &lt;name&gt;</c>: what the summary line calls this run.</summary>
    public string PacingLabel { get; private set; } = "run";

    /// <summary><c>--goto-patrol x1,z1,x2,z2</c>: ping-pong between two ground points for ever,
    /// no hold required. See <see cref="Game.Sandbox.ScriptedPatrolIntentSource"/>.</summary>
    public bool GotoPatrol { get; private set; }

    /// <summary>First patrol end, world ground coordinates.</summary>
    public Vector3 GotoPatrolA { get; private set; }

    /// <summary>Second patrol end, world ground coordinates.</summary>
    public Vector3 GotoPatrolB { get; private set; }

    /// <summary>--capture-dir &lt;dir&gt; + --capture-at &lt;sec[,sec…]&gt;: a WINDOWED bot saves its
    /// own viewport to &lt;dir&gt;/&lt;name&gt;-&lt;sec&gt;s.png at each listed elapsed time, then carries on to
    /// its normal duration/exit. This is the in-engine capture path for anything only a real
    /// session can show — replicated world state seen from a specific peer, and UI, which no
    /// dev-scene capture lab renders at all. It reads the engine's own viewport texture
    /// (never an OS screen-scrape of the window, which is the rule this repo already learned
    /// the hard way on the sideways-island bug), so what lands on disk is exactly what the
    /// renderer produced. Capturing from two bots in one session is how "…for every player"
    /// gates get evidence instead of assurances. Additive and off by default; headless CI never
    /// sets either flag, and with --headless there is nothing to capture.</summary>
    public string CaptureDir { get; private set; } = "";

    /// <summary><c>--show-first-run-panels</c>: force every first-run overlay ON for this run,
    /// overriding both <see cref="SuppressesFirstRunPanels"/> and whatever the player persisted.
    /// The escape hatch for a capture whose SUBJECT is a first-run panel — see
    /// <c>MpFoundation.Ui.OnboardingSettings.ForceShowFirstRunPanels</c>.</summary>
    public bool ShowFirstRunPanels { get; private set; }

    /// <summary><b>Nobody is reading a first-run overlay in this process</b> — it is producing
    /// image files, or driving itself, or both (W7-8, 2026-08-30).
    ///
    /// <para>Both arms are the same fact. A <see cref="CaptureDir"/> run exists to write a PNG,
    /// and a panel across that PNG makes it evidence of nothing while still looking like evidence.
    /// A <see cref="Bot"/> run has no human at the keyboard at all, so a modal asking to be
    /// dismissed is at best furniture. Every affected harness in <c>tests/</c> sets at least one of
    /// the two.</para>
    ///
    /// <para><b>What this now gates, since 2026-09-04.</b> The level's first-run How-to-Play door
    /// — the worst offender, and the one that also freed the mouse and so changed the input state
    /// a bot run was measuring — is gone: How-to-Play is only ever asked for, from the pause menu.
    /// What is left in the level is the on-entry goal line
    /// (<c>MpFoundation.Ui.OnboardingSettings.GoalLineSuppressed</c>), which is not a panel and is
    /// not persisted, but is still copy over the frame during the exact seconds a capture is
    /// taken. The two menu-side first-run panels are unchanged.</para>
    ///
    /// <para>Deliberately expressed as a property of the RUN rather than as a list of panels:
    /// there are already two first-run overlays and a third lands the same day. See
    /// <c>OnboardingSettings.SuppressFirstRunPanels</c> for where it is applied and why it is
    /// applied in one place.</para></summary>
    public bool SuppressesFirstRunPanels => !ShowFirstRunPanels && (CaptureDir.Length > 0 || Bot);

    /// <summary>Elapsed-seconds marks for --capture-dir, ascending, de-duplicated.</summary>
    public List<double> CaptureAtSec { get; } = new();

    /// <summary>--capture-cam &lt;x,y,z,tx,ty,tz&gt;: park a fixed camera at (x,y,z) looking at
    /// (tx,ty,tz) and make it current, instead of following an avatar. --spectate-cam frames the
    /// AVATAR, which is the wrong subject for most visual gates — a small object metres away ends
    /// up a few pixels wide or hidden behind the very body the camera is chasing (measured: a
    /// 0.7m x 3cm cylinder held a few metres out is occluded by the holder's own blob from that
    /// blob's follow cam). Giving every peer the SAME fixed framing also makes a "…for every player"
    /// gate a direct image comparison rather than two differently-framed views a human has to
    /// reconcile. View-only: the bot's intent source, movement and logging are untouched.</summary>
    public bool HasCaptureCam { get; private set; }

    public Vector3 CaptureCamPos { get; private set; }

    public Vector3 CaptureCamLookAt { get; private set; }
    public int Port { get; private set; } = DefaultPort;
    public string Host { get; private set; } = "127.0.0.1";
    public string DisplayName { get; private set; } = "Bot";
    public string LogPath { get; private set; } = "";
    public double DurationSec { get; private set; } = 15.0;

    /// <summary>--perf-log &lt;path&gt;: PerfHud appends one JSON line of Performance-monitor
    /// numbers per second (fps, frame ms, draw calls, VRAM…). How §4 budgets get verified
    /// in-scene (§7) by an automated run instead of someone reading a screen.</summary>
    public string PerfLog { get; private set; } = "";

    // Phase 2 additions.
    public string Room { get; private set; } = "";              // bot: room code to resolve via the Steam lobby directory
    public string LogDir { get; private set; } = "user://logs";
    public int ProtocolOverride { get; private set; } = -1;     // test hook: force a wrong protocol version
    public bool BadAuth { get; private set; }                   // test hook: send malformed auth payload
    public bool AddressExplicit { get; private set; }           // bot: --address was given (skip matchmaking)
    public int ConnLimit { get; private set; } = 40;            // server: per-IP connection attempts per 10s window

    // Proximity-voice test hooks (bots synthesize packets; no microphone involved).
    public bool VoiceSend { get; private set; }                 // bot: transmit a synthetic Opus tone stream
    public bool VoiceFlood { get; private set; }                // bot: transmit at ~4x the legit rate (rate-limit test)
    public bool VoiceGarbage { get; private set; }              // bot: transmit size-valid non-Opus bytes (decoder-hardening test)
    public bool VoiceOversize { get; private set; }             // bot: transmit oversized packets (size-bound test)

    /// <summary>Which world the Gameplay scene builds. Defaults to "supermarket" — the three
    /// rooms the game is played in — so every launch path that does not explicitly override World
    /// (Practice Mode's child server, a bare direct-connect, a Steam tester who just clicks Host)
    /// lands in the game rather than in CI scaffolding. The only other ids are the code-built
    /// replication testbeds "open" and "propsync"; Gameplay's world switch treats anything else
    /// as a launch error rather than falling back, because a silent fallback once shipped a
    /// playtest where the level "did not load". Built locally by each
    /// peer, so pass the same value to server and clients of one session. Settable (not just
    /// CLI-parsed): the interactive Host flow (HostMenu) forces it to <see cref="DefaultWorld"/>
    /// when the launch did NOT name a world — see <see cref="WorldExplicit"/>.</summary>
    public string World { get; set; } = DefaultWorld;

    /// <summary>The world a launch that never says otherwise builds. Named rather than repeated
    /// because there are two sites that must agree — this property's initialiser and HostMenu's
    /// forcing assignment for the interactive Host flow — and a silent disagreement between them
    /// is exactly the defect LAUNCH-1 was fixing (the menu forced the previous default past a
    /// changed one). One symbol, so the next flip is one edit.
    ///
    /// <para><b>TRAP - read this before gating anything on the world id.</b>
    /// <c>NetworkManager</c> is an AUTOLOAD, so <c>NetworkManager.Instance?.Options?.World</c>
    /// is never null and never empty: in ANY scene launched without <c>--world</c> it is this
    /// value. There is therefore no such thing as "a demo scene has no world id" - a demo, a
    /// capture rig and an in-engine self-test all silently read <b>"supermarket"</b>, and any
    /// per-world profile they consult (<c>HudProfile</c>, <c>VoiceProximityGate</c>) hands them
    /// that world's answer. That has already cost real time twice. The way out: pass the decision
    /// in as a parameter and default it to the full behaviour, so no lab can lose a surface
    /// however it was launched. Use <see cref="WorldExplicit"/> only to
    /// answer "did a human ask for this world" - never as a stand-in for "is there a session",
    /// because a plain double-click of the real game is also not explicit.</para></summary>
    public const string DefaultWorld = Game.World.SupermarketWorld.WorldId;

    /// <summary>--world was given on the command line, so <see cref="World"/> is a deliberate
    /// choice rather than the default. The interactive Host flow honours it instead of forcing
    /// <see cref="DefaultWorld"/>; with a single shipped world the two agree, but the seam is
    /// kept so the next world added is reachable through the menu without a HostMenu edit
    /// (Practice is no longer a player-facing button — see MainMenu). Same idiom as
    /// <see cref="AddressExplicit"/>.</summary>
    public bool WorldExplicit { get; private set; }

    // Server-authoritative movement additions (append-only).
    /// <summary>--net-sim &lt;latencyMs&gt;,&lt;lossPct&gt;,&lt;jitterMs&gt;: client-side in-process
    /// network-condition simulator for movement messages (see NetSim). Latency/jitter are
    /// one-way per message; loss is per message. Netcode CI runs under this.</summary>
    public bool NetSimEnabled { get; private set; }
    public double NetSimLatencyMs { get; private set; }
    public double NetSimLossPct { get; private set; }
    public double NetSimJitterMs { get; private set; }

    /// <summary>Test hook: bot sends inflated/garbage movement intents on the wire while
    /// predicting honestly — the anti-cheat suite proves the server contains it.</summary>
    public bool CheatMove { get; private set; }

    // Carry-authority convergence test additions (Run-CarryTest.ps1).
    /// <summary>--carry-script: bot uses ScriptedCarryIntentSource instead of the plain
    /// deterministic walk, so it can walk to a networked prop and script a grab/hold/drop.</summary>
    public bool CarryScript { get; private set; }
    public Vector3 CarryTarget { get; private set; }
    /// <summary>Earliest elapsed-clock second (the bot's own scripted clock) at which it may
    /// fire its grab request, once it has also arrived at <see cref="CarryTarget"/>.</summary>
    public double CarryGrabAtSec { get; private set; }
    /// <summary>Seconds to hold after a successful grab before auto-dropping; negative = never
    /// auto-drop within the scripted window (used to stage a disconnect-while-holding).</summary>
    public double CarryHoldSec { get; private set; } = -1;
    /// <summary>Seconds after a successful grab at which to fire one scripted throw (Run-ThrowTest.ps1);
    /// negative (default) = never throw.</summary>
    public double CarryThrowSec { get; private set; } = -1;

    /// <summary>--carry-throw-scale &lt;f&gt;: scales a scripted bot's throw impulse. Default 1.0 =
    /// the exact impulse a real player throws with, so no non-bot path changes. Exists because a
    /// full-strength throw sends a Ball rolling ~46m — measured on the retired 64m-square fixture
    /// field, a run left the ball at rest 19cm short of the void. Tip it over and the kill-plane
    /// recovers it to its home transform, stranding the regrab bot tens of metres away with no
    /// time to chase — see Run-RegrabTest.ps1 and issue #4.</summary>
    public double CarryThrowScale { get; private set; } = 1.0;

    /// <summary>--carry-walk-to x,z: after grabbing, carry the prop toward this ground point
    /// while holding it. Absent = stand still.</summary>
    public bool CarryWalk { get; private set; }
    public Vector3 CarryWalkTo { get; private set; }

    /// <summary>--carry-patrol x1,z1,x2,z2: after grabbing, ping-pong between these two ground
    /// points for the whole hold instead of walking to one point and standing still — the
    /// sustained walk-while-holding episode the carry-drift regression (Run-CarryDriftTest.ps1)
    /// samples the held-item→anchor offset across. Independent of <see cref="CarryWalk"/>.</summary>
    public bool CarryPatrol { get; private set; }
    public Vector3 CarryPatrolA { get; private set; }
    public Vector3 CarryPatrolB { get; private set; }

    /// <summary>--carry-regrab &lt;propId&gt;: after the scripted throw, chase that networked
    /// prop's live replicated position and grab it AGAIN as soon as it is reachable —
    /// deliberately before the server's settle latch (the Loose → Held transition). The
    /// drift regression proof (Run-RegrabTest.ps1): a re-grabbed prop must track its
    /// holder, not the stale loose stream.</summary>
    public bool CarryRegrab { get; private set; }
    public int CarryRegrabPropId { get; private set; }

    /// <summary>--carry-target-prop &lt;propId&gt;: walk to that networked prop's LIVE replicated
    /// position instead of to the fixed <c>--carry-script</c> coordinate, until the grab lands.
    /// Negative (the default) = the fixed coordinate, so every existing carry suite is unchanged.
    ///
    /// <b>Why (CARRY-1, 2026-08-29, measured).</b> "Walk to (-2.5, 0.5, -2.5)" is only the same
    /// instruction as "walk to the ball" while nobody has moved the ball. A prop released by a
    /// disconnecting holder comes to rest wherever THAT player was standing: measured, the ball
    /// landed at (-2.60, 0.50, -1.38), 1.1 m from its spawn. The bot aimed at the spawn, stopped
    /// 1.99 m from the ball — outside <c>SandboxAvatar.PickupRadius</c> (1.5 m), so its client
    /// never even sent a request — and stood there for 16 s. The suite called that a carry defect
    /// when it was a stale coordinate.
    ///
    /// Only meaningful for a bot whose target can move. Where the prop is genuinely stationary
    /// (the contention phase, Run-CarryTest) the constant is correct and this flag is not passed.</summary>
    public int CarryTargetPropId { get; private set; } = -1;

    /// <summary>--carry-place x,y,z,yawDeg,afterSec: a scripted PLACE (CARRY-1's
    /// <c>tests/Run-PlaceTest.ps1</c>). Off unless asked.</summary>
    public bool CarryPlace { get; private set; }

    /// <summary>
    /// The exact transform a <c>--carry-place</c> bot asks the server to put its held prop at.
    ///
    /// <para><b>A scripted transform rather than "press E and see where it lands", deliberately.</b>
    /// The suite has to prove the observing peer sees the prop within 5 cm and 5° of a KNOWN pose;
    /// a placement taken from wherever the bot's own spring happened to be holding the crate is
    /// its own measurement, so the test could only ever compare the run against itself. Fixing the
    /// transform is also the only way to stage the three refusals — inside a pillar, outside the
    /// room, past arm's reach — since a bot cannot be asked to "aim at the wall".</para>
    ///
    /// <para>It drives <c>BotHarness</c>, not the avatar: the place decision in
    /// <c>SandboxAvatar.HandleCarryIntent</c> stays exactly the rule a human gets, with no test
    /// branch in it. The bot simply calls the same public client entry point
    /// (<c>PropManager.ClientRequestPlace</c>) that rule calls.</para></summary>
    public Transform3D CarryPlaceAt { get; private set; } = Transform3D.Identity;

    /// <summary>Seconds after this bot is first seen HOLDING something before the scripted place
    /// fires. Anchored on the observed hold rather than on the clock, for the reason
    /// <c>--exit-when-holding</c> exists: a wall-clock guess about when a bot has walked somewhere
    /// is a guess about machine speed, and it is the guess this suite family keeps losing.</summary>
    public double CarryPlaceAfterSec { get; private set; }

    /// <summary>
    /// --spawn-room &lt;holding|search|task&gt;: SERVER-side, which room's markers every player
    /// spawns at. Empty (the default) keeps the world's own declared spawn points, which for the
    /// supermarket is the holding room.
    ///
    /// <para><b>A test and dev hook, not a game rule.</b> The three rooms are sealed boxes 40 m
    /// apart and nobody walks between them — ROUND-1 teleports players on a phase change. A scene
    /// suite that needs to exercise a verb against the props authored in the SEARCH room therefore
    /// has no way to get a bot there at all, and seeding fixture props into the holding room
    /// instead would test the verb against content no lane owns. One flag, read in one place
    /// (<c>Gameplay.SpawnPositionFor</c>), is the smaller of the two costs.</para></summary>
    public string SpawnRoom { get; private set; } = "";

    /// <summary><c>--spawn-index "BotA=2,BotB=0"</c>: SERVER-side. Pins a NAMED peer to a spawn
    /// marker index, so a scripted bot lands where the suite staged its fixture regardless of the
    /// order it happened to connect in (INT-1, 2026-09-19, packet ruling 6).
    ///
    /// <para><b>Why a pin is needed at all, measured rather than assumed.</b> Markers are dealt
    /// by JOIN ORDER (<c>Gameplay.OnPeerConnected</c>'s <c>_spawnIndex++</c>) and join order
    /// races. SHELF-1 §6.3 measured it across three runs of <c>Run-MaterialSfxTest</c>: the box
    /// bot drew marker 2 on one run and marker 1 on the next, and the run where it drew the far
    /// marker went red on a pickup that never happened. That was survivable while the search room
    /// was an empty box; the dressed room is four 9.2 m corridors with no cross-aisle, and a
    /// straight-line walk brain in the wrong walkway stops dead at a shelf face.</para>
    ///
    /// <para><b>It takes nothing onto any wire.</b> The name matched on is
    /// <c>SandboxAvatar.DisplayName</c>, which already replicates from the owning client through
    /// the existing <c>Sync</c> synchronizer, so there is no new message and no
    /// <c>ProtocolVersion</c> bump. It is also not something a client can ask for: a peer cannot
    /// pin itself, only the process that started the SERVER can pin it. Empty by default, so an
    /// ordinary session's spawn path is byte-for-byte unchanged.</para>
    ///
    /// <para>Parsing, the sentinel and the per-entry tolerance live in
    /// <c>MpFoundation.Game.World.SpawnPin</c>, which is engine-free and unit-tested.</para>
    /// </summary>
    public string SpawnIndexSpec { get; private set; } = "";

    // --- the sorting job (TASK-1, 2026-09-19) -----------------------------------------------

    /// <summary>
    /// <c>--sort-script "&lt;propId&gt;:&lt;binSlot&gt;[,...]"</c>: a bot that fetches each named
    /// object out of the task room's supply crate and sets it down in the named bin, in order.
    /// A NEGATIVE bin slot means "fetch it and keep holding it", which is how
    /// <c>tests/Run-SortTest.ps1</c> stages an object in the hider's hands at the burst.
    ///
    /// <para><b>Self-paced, with no second in it.</b> Each step advances on an OBSERVED
    /// hand-off, never on a wall clock, and the whole loop is gated on the round being in
    /// <c>Seeking</c> — which is when the hider is in the task room at all.
    /// <c>.claude/rules/test-suite.md</c>: derive a schedule from the thing it must outlive.
    /// The bins' positions come out of the bot's own copy of the authored room
    /// (<c>SortBin.Find</c>), so this flag names ids and slots and no coordinates.</para>
    ///
    /// <para>Empty on every real launch, like every other scripted-bot flag here, and it steers
    /// a <c>--bot</c> client's brain only — there is nothing on a human's path to reach.</para>
    /// </summary>
    public IReadOnlyList<(int PropId, int BinSlot)> SortScript => _sortScript;
    private readonly List<(int PropId, int BinSlot)> _sortScript = new();

    /// <summary>--hold-notches &lt;n&gt;: once this bot is holding something, roll the hold
    /// distance by n wheel notches (positive = further out) and stop. Capture-only -- a bot has
    /// no mouse -- and it goes through <c>NetworkedProp.ScrollHold</c>, the same method the
    /// wheel calls. 0 (the default) does nothing.</summary>
    public int HoldNotches { get; private set; }

    /// <summary>--hold-stress: once this bot is holding something, run
    /// <c>HoldStressIntentSource</c>'s loop — forward, backward, strafe both ways, two 180s — for
    /// the rest of the run instead of whatever walk the carry script had left (FEEL-1,
    /// 2026-09-20). Off by default, so every existing carry suite's pacing is unchanged.</summary>
    public bool HoldStress { get; private set; }

    /// <summary>--walk-speed &lt;m/s&gt;: this process's top ground speed, overriding
    /// <c>BrowsePace.DefaultWalkSpeedMps</c> (FEEL-1, 2026-09-20).
    ///
    /// <para><b>It is a MOTOR constant, so a session must give every process the same one.</b>
    /// The owner predicts with it and the server judges with it; two peers holding different
    /// values disagree about where a body is, which is a desync rather than a difference of
    /// opinion. A value the knob table would refuse falls back to the shipped default rather
    /// than being clamped — see <c>BrowsePace.SanitizeWalkSpeed</c> for why refusing is the safe
    /// direction. Negative (the default) means "the shipped browse pace".</para></summary>
    public float WalkSpeedMps { get; private set; } = -1f;

    /// <summary>--carry-grab-retry &lt;sec&gt;: re-fire a scripted bot's FIRST grab on this cadence
    /// until it is actually holding something. Negative (the default) = the original one-shot
    /// press, so every existing carry suite's pacing is byte-for-byte unchanged.
    ///
    /// <b>Why (CARRY-1, 2026-08-29, measured).</b> <c>ScriptedCarryIntentSource</c> fires its grab
    /// the first tick its own <i>predicted</i> position is inside <c>ArriveRadius</c>, and then
    /// never again. Measured on an idle machine, a headless bot's client-side prediction error
    /// transiently reaches <b>1.71 m</b> — so the press can leave the client at a predicted 1.2 m
    /// while the server's authoritative body is still ~2.9 m out, past
    /// <c>PropManager.GrabRange</c> (2.25 m). One press, refused, and a bot that will never press
    /// again: the scenario silently never stages.
    ///
    /// This is the same failure LEVER-2 hit the same night in its own harness — an irreversible
    /// decision taken on a predicted position — and the general shape is worth naming: any scripted
    /// intent that latches on prediction will latch on the wrong thing under load. A retry is also
    /// simply what a real player does when E produces nothing.</summary>
    public double CarryGrabRetrySec { get; private set; } = -1;

    /// <summary>--exit-when-holding &lt;sec&gt;: quit cleanly this many seconds after this bot is
    /// FIRST observed holding anything (hand or arms). Negative (the default) = off; the bot lives
    /// for <c>--duration</c> as before.
    ///
    /// <b>An event-anchored budget, and that is the whole point.</b> Staging a
    /// disconnect-while-holding by giving the bot a short <c>--duration</c> encodes a guess about
    /// how fast it walks, and that guess loses: CARRY-1's first run had the server move its bot
    /// 2.4 m in 5.8 s (~0.42 m/s against a ~3.6 m/s walk) and the 6 s lifetime expired with the
    /// ball never picked up. <c>Run-ThrowTest</c>'s header already states the rule this restores —
    /// "bot lifetimes are COMPLETION BUDGETS, not assumptions about when any step lands". With
    /// this flag <c>--duration</c> goes back to being a real budget: generous, and its expiry
    /// means the scenario genuinely failed rather than that the machine was slow.</summary>
    public double ExitWhenHoldingSec { get; private set; } = -1;

    /// <summary>--seed-test-props "x,y,z[;x,y,z...]": server-only. Spawns one Crate
    /// <c>NetworkedProp</c> at each listed world position, in WHATEVER world is running, through
    /// the ordinary <c>PropManager.ServerSpawn</c> path — no second carry model, no level edit.
    ///
    /// <b>Why this exists (CARRY-1, 2026-08-29).</b> The carry suites used to run in a dedicated
    /// code-built fixture world that seeded its own test props. But two of the things a networked
    /// carry has to survive — a TV portal teleport, and a section crossing — exist only in
    /// <c>bubbletest</c>, and <c>bubbletest</c> is prop-free. Both alternatives were worse:
    /// seeding a prop inside the level puts a test fixture in <c>BubbleTestWorld</c> (another
    /// packet's file, and level content nobody asked for), and driving whatever props that level
    /// happens to contain makes a carry assertion depend on positions a level packet is free to
    /// move next week. A launch flag keeps the fixture inside the test that needs it and leaves
    /// the world's shipped content exactly as it was. With the fixture world retired, this flag is
    /// the only way any suite gets a networked prop to carry.
    ///
    /// Absent by default and read only on the server, so no non-test launch changes at all. The
    /// props it spawns are ordinary <see cref="MpFoundation.Game.Props.PropKind.Crate"/> props:
    /// a seeded crate and an authored crate are indistinguishable once picked up, which is the
    /// point — a fixture that carried differently would prove nothing about carry.
    ///
    /// <para><b>SFX-1 (2026-09-19) appended an optional fourth field:</b>
    /// <c>"x,y,z[,kind]"</c>, where kind is one of <c>crate</c> (the default, so every existing
    /// caller is byte-for-byte unaffected), <c>ball</c>, <c>can</c>, <c>box</c> or
    /// <c>produce</c>. A NAME rather than an ordinal, because an ordinal in a test script is a
    /// second copy of the wire contract and would go quietly wrong the first time
    /// <c>PropKind</c> grows. The material-SFX suite seeds forty mixed props in a heap with it;
    /// nobody is going to author forty.</para></summary>
    public IReadOnlyList<(Vector3 At, MpFoundation.Net.PropKind Kind)> SeedTestProps => _seedTestProps;
    private readonly List<(Vector3 At, MpFoundation.Net.PropKind Kind)> _seedTestProps = new();

    /// <summary>--seed-props-drop &lt;sec&gt;: server-only, test-only. That many seconds into the
    /// session, release every <c>--seed-test-props</c> prop from Resting into Loose, once, so the
    /// fixture actually FALLS. See <c>PropManager.StepSeededDrop</c> for why a seeded prop
    /// otherwise hangs in mid-air forever and why the delay is load-bearing. Negative (the
    /// default) = never, so every suite written before SFX-1 is unaffected.</summary>
    public double SeedPropsDropAtSec { get; private set; } = -1;

    /// <summary>--log-sfx: print one <c>[sfx] sfx &lt;name&gt; event=... intensity=... at (...)</c>
    /// line per sound <c>ActorFx</c> actually plays, plus one
    /// <c>[sfx] SUMMARY fires=... peakLive3DVoices=... oneShotSteals=...</c> line when a bot
    /// finishes. <c>tests/Run-MaterialSfxTest.ps1</c>'s whole assertion surface: a headless run
    /// has no audio device and a windowed one cannot be recorded, so the log IS the evidence that
    /// a given material fired a given sound. Off by default and read nowhere in a real
    /// session.</summary>
    public bool LogSfx { get; private set; }

    // Aim-substrate convergence test additions (WP-L3, Run-AimTest.ps1).
    /// <summary>--aim-script &lt;raiseAtSec&gt;[,&lt;lowerAtSec&gt;]: bot wraps its movement brain in
    /// ScriptedAimIntentSource so it raises (and optionally lowers) the shared aim rig on a
    /// schedule — proves AimStance replicates to remote peers headlessly, no human input.</summary>
    public bool AimScript { get; private set; }
    public double AimRaiseAtSec { get; private set; }
    /// <summary>Negative (default) = never lower within the scripted window.</summary>
    public double AimLowerAtSec { get; private set; } = -1;

    /// <summary>Filename stem for captured stills, set by the combined
    /// <c>--capture-at dir,name,t1,t2,...</c> form. Empty means "unset", in which case
    /// <c>BotHarness</c> falls back to <see cref="DisplayName"/> — which is what the separate
    /// <c>--capture-dir</c> + <c>--capture-at</c> form has always used, and which is what makes
    /// two bots capturing the same session write to distinct files.
    ///
    /// <b>Merge note (integration/2026-08-08-playtest).</b> Two branches shipped the same capture
    /// feature with different CLI shapes and their own duplicate <c>CaptureDir</c>/
    /// <c>CaptureAtSec</c> fields; the textual merge left both sets in one class, which does not
    /// compile. Deduplicated onto <c>capture/harness</c>'s fields (a growable, de-duplicated,
    /// sorted <see cref="CaptureAtSec"/>), with this stem kept so the other branch's capture
    /// scripts keep working unchanged.</summary>
    public string CaptureName { get; private set; } = "";

    // Steam transport additions (append-only).
    public const string TransportEnet = "enet";
    public const string TransportSteam = "steam";

    /// <summary>--transport enet|steam: which transport a dedicated server listens on.
    /// "steam" = anonymous game-server login + Steam relay P2P, registering a
    /// steam:&lt;steamid64&gt; address with matchmaking instead of host:port. Clients don't
    /// use this flag — they pick their transport from the resolved address's prefix.
    /// Practice Mode always spawns its child without the flag (ENet on localhost).</summary>
    public string Transport { get; private set; } = TransportEnet;

    /// <summary>--steam-app-id &lt;id&gt;: highest-priority App ID override (then STEAM_APP_ID
    /// env, then steam_appid.txt beside the exe, then Spacewar 480 — see SteamService).</summary>
    public uint SteamAppId { get; private set; }

    /// <summary>--room-code &lt;CODE&gt;: the room code this dedicated server was published under.
    /// When set (the Steam host-by-code path), the server requires every joiner to present the
    /// matching code in its handshake — possession of the code becomes an actual join
    /// capability, not a directory label. Empty (LAN / direct / CI / Practice) ⇒ no requirement,
    /// so those paths are unaffected. Validated as a well-formed room code or ignored.</summary>
    public string RoomCode { get; private set; } = "";

    /// <summary>--join-room-code &lt;CODE&gt;: the room code this CLIENT presents in its handshake —
    /// the headless equivalent of what JoinMenu sets from the typed code. Exists so the room-code
    /// join capability can be exercised end to end by CI: the gate lives in the handshake and is
    /// transport-agnostic, so it works over ENet and needs no live Steam session. Empty (the
    /// default) presents no code at all, exactly like a direct/LAN join.</summary>
    public string JoinRoomCode { get; private set; } = "";

    /// <summary>--steam-selftest: headless pure-logic checks of the Steam-transport plumbing
    /// that needs no live Steam client (address parsing, transport selection, identity-keyed
    /// rate limiting, room-code codec). Exits 0/1; Run-SteamLogicTest.ps1 gates on it.</summary>
    public bool SteamSelfTest { get; private set; }

    /// <summary>--reconnect-selftest: headless pure-logic checks of the reconnect-grace-window
    /// bookkeeping (ReconnectRegistry) that need no live Steam client and no running match
    /// (capture/resume/expiry/sweep). Exits 0/1; Run-ReconnectTest.ps1 gates on it.</summary>
    public bool ReconnectSelfTest { get; private set; }

    /// <summary>--cycle-selftest: headless pure-logic checks of the tidal-cycle phase math
    /// (CyclePhase) and the phase-driven sun/moon/sky curves (DayNightSky.Evaluate) that
    /// need no live scene, network, or Multiplayer API. Exits 0/1; Run-CycleTest.ps1 gates
    /// on it.</summary>
    public bool CycleSelfTest { get; private set; }

    /// <summary>--voice-mute-selftest: headless pure-logic checks of the mute set's
    /// identity/peer-id bookkeeping (MuteRegistry) that need no live Steam client and no
    /// running match (write-both/peer-only, disconnect keeps identity, reconnect re-apply,
    /// recycled-id safety). Exits 0/1; Run-VoiceTest.ps1 gates on it.</summary>
    public bool VoiceMuteSelfTest { get; private set; }

    /// <summary>--supermarket-selftest: the level's compliance test — the named spawn markers,
    /// the authored-vs-live node count per room (nothing may be built in code), and the room
    /// separation the voice cutoff depends on. Unlike the pure-logic *SelfTest flags above it
    /// instantiates the real shipped scene. Prints one machine-readable summary line and exits
    /// 0/1; tests/Run-SupermarketWorldTest.ps1 gates on the LINE, not the exit code.</summary>
    public bool SupermarketSelfTest { get; private set; }

    /// <summary>--stock-selftest (STOCK-1): the BAKED shop floor's compliance test. Reads the
    /// bays out of the live tree, re-derives the whole bulk layout from ShelfStock's arithmetic,
    /// and checks the loaded MultiMesh instance counts and collision-run count against it — then
    /// poses a real near-miss prefab in EVERY authored hole and runs the shipped
    /// PlacementIntegrity.Check on it. Offline, headless, no server and no bots: neither question
    /// needs one, and every peer is a way for the run to fail for an unrelated reason. Prints one
    /// machine-readable summary line and exits 0/1; tests/Run-StockTest.ps1 gates on the LINE, not
    /// the exit code.</summary>
    public bool StockSelfTest { get; private set; }

    /// <summary>--build-ui-theme: regenerates <c>resources/UITheme.tres</c> from the C# design
    /// tokens, for editor preview. Safe to run at any time — the exported file is build output
    /// of the same factory the game runs, so it cannot drift from what ships (which is exactly
    /// what the retired <c>tools/BuildTheme.gd</c> did). Exits 0/1.</summary>
    public bool BuildUiTheme { get; private set; }

    /// <summary>--ui-theme-demo: a headed board of every kit component in all five states, at
    /// both temperatures, on one screen. The thing to look at after changing a token.</summary>
    public bool UiThemeDemo { get; private set; }

    /// <summary>--ui-theme-selftest: headless check that the built theme actually REACHES a
    /// Control through every parent shape the game uses (root window, plain Node, CanvasLayer),
    /// and that the two temperatures resolve different values. The xUnit suite is Godot-free and
    /// structurally cannot see propagation — it passed 1722 tests while the running game
    /// rendered the committed .tres instead of the design system. Exits 0/1.</summary>
    public bool UiThemeSelfTest { get; private set; }

    /// <summary>--telemetry-self-test: headless pure-logic checks of the telemetry module
    /// (TelemetryStore persistence, SessionMonitor crash classification, PendingQueue flush,
    /// payload construction) with the real Firestore send stubbed. Exits 0/1;
    /// Run-TelemetryTest.ps1 gates on it.</summary>
    public bool TelemetrySelfTest { get; private set; }

    /// <summary>--host-selftest: headless proof of the Host flow's client-local server
    /// spawn (LocalServerHost) — spawn over ENet (CI has no Steam), parse the child's
    /// listening line for readiness, join it through the normal client path. The
    /// orphan-reaping and crash-visibility guarantees are asserted around this mode by
    /// Run-HostingTest.ps1.</summary>
    public bool HostSelfTest { get; private set; }

    /// <summary>--force-reconnect-at &lt;sec&gt;: test-only (bot). This many seconds after
    /// connecting, the client deliberately closes its own connection and redials the same
    /// ENet address it started with — standing in for <c>OnServerDisconnected</c>'s real
    /// Steam-transport retry loop (see <c>Gameplay.ConnectSteam</c>), which headless CI cannot
    /// exercise (no live Steam relay/account). Drives the exact same
    /// <c>Gameplay.TeardownReplicatedNodes</c> call sites the Steam path uses, just over the
    /// transport CI actually has. Negative (default) = disabled; every other launch path is
    /// unchanged. See Run-ReconnectTest.ps1 and Task A1's report for the CI-scope rationale.</summary>
    public double ForceReconnectAtSec { get; private set; } = -1;

    // THE SCRIPTED-VERB AND FLOW/QUOTA TEST FLAGS THAT USED TO SIT HERE went with their
    // systems at the fork (BASE-1, 2026-09-19): --net-probe-at, --flow-timers, --quota-early,
    // --quota-bank-at, --flow-ready-at, --flow-play-again-at, --flashlight-at, --honk-at,
    // --honk-forge, --honk-flood and --flow-selftest, along with AppendFlowServerFlagsTo, the
    // helper that re-emitted the server-side half onto a child dedicated server's command line.
    //
    // THAT HELPER IS THE PART WORTH REMEMBERING. The Host flow launches the real server as a
    // CHILD PROCESS, so a server-side test flag typed on the parent's command line reaches
    // nothing at all unless something forwards it. Any test hook ROUND-1 adds that the SERVER
    // must see needs the same forwarding, and the symptom of forgetting is a flag that silently
    // does nothing in exactly one launch mode.

    /// <summary>--cycle-period &lt;sec&gt;: overrides CycleDriver's 120s tidal-loop period.
    /// Test hook (Run-CycleTest.ps1) — a short period (e.g. 12s) makes a full day/night
    /// wrap, late-join-mid-cycle, and re-sync all observable inside a headless test's
    /// duration instead of waiting out a real 120s cycle. Every real launch path leaves
    /// this at 0 (meaning "use the 120s default" — see CycleDriver.Setup).</summary>
    public double CyclePeriodSec { get; private set; }

    /// <summary>--cycle-start-phase &lt;0..1&gt;: the server's CycleDriver starts at this
    /// normalized phase instead of 0. Test hook — makes every window in the BUILD-SPEC §3
    /// timeline (golden hour, moon apex, ...) reachable on demand without waiting for real
    /// elapsed time to reach it. Ignored by clients (only the server's CycleDriver is ever
    /// seeded from it — see CycleDriver.Setup).
    ///
    /// <para><b>Also accepts a NAME</b> (STYLE-4): <c>--cycle-start-phase noon</c> and the rest of
    /// <see cref="Game.World.CycleBands.PhaseNames"/> resolve against <see cref="CycleStartDay"/>'s
    /// bands, so "noon" means noon on the day being reviewed rather than a literal that was right
    /// for day 1 only. Resolution happens ONCE, after the whole argument list is parsed (see
    /// <see cref="ResolveNamedCycleStartPhase"/>) — otherwise the answer would depend on whether
    /// <c>--cycle-start-day</c> came before or after this flag on the command line, which is
    /// exactly the class of silently-wrong-and-plausible bug this file keeps collecting.</para>
    /// </summary>
    public float CycleStartPhase { get; private set; }

    /// <summary>The name given to <c>--cycle-start-phase</c>, empty when a number was given.
    /// Kept so a launch can PRINT what it resolved ("noon -> 0.275") instead of just acting on
    /// it — a review harness whose whole job is "look at the world at noon" has to be able to
    /// show that it is in fact at noon.</summary>
    public string CycleStartPhaseName { get; private set; } = "";

    /// <summary>--cycle-freeze: the SERVER's phase clock stops advancing. Clients keep receiving
    /// the broadcast and keep snap-correcting to it; what they stop doing is extrapolating between
    /// broadcasts, which they work out from the broadcast data itself rather than from this flag —
    /// see CycleDriver's freeze doc. So the frozen phase stays server data on every peer (the
    /// parity law) and a client that never saw this flag is frozen just the same.
    ///
    /// <para>Built for the visual-style brief's §2 review baseline: an asset judged under
    /// "flat midday daylight" must still be under it ten minutes into the look-around, and a clock
    /// that keeps running turns a review session into a countdown.</para></summary>
    public bool CycleFreeze { get; private set; }

    /// <summary>--parent-managed: this dedicated-server child was spawned by a
    /// <see cref="Net.Hosting.LocalServerHost"/> that keeps its stdin open as a control
    /// channel. The child watches that stdin (see ParentShutdownWatcher) and shuts down
    /// cleanly when the parent closes it, so its Steam game-server session is logged off
    /// instead of orphaned by a hard kill. Only LocalServerHost passes this — servers the
    /// test harness or Practice Mode launch directly never do.</summary>
    public bool ParentManaged { get; private set; }

    // --- WP-N1 (2026-07-26 wave) -------------------------------------------------------------
    /// <summary>World-requested default for <see cref="CyclePeriodSec"/> — lets a world's own
    /// _Ready() (a world wanting, say, a 5-minute day) request its own period without ever overriding an explicit launch/test --cycle-period. Reuses
    /// the existing "0 = unset" sentinel CycleDriver.Setup already treats as "use the 120s
    /// default" — a no-op if --cycle-period was already given (nonzero) or if the requested
    /// value itself isn't positive.</summary>
    public void RequestDefaultCyclePeriod(double sec)
    {
        if (CyclePeriodSec <= 0 && sec > 0)
            CyclePeriodSec = sec;
    }

    /// <summary>--cycle-start-day &lt;n&gt;: dev/test hook, the day-index analogue of
    /// <see cref="CycleStartPhase"/>. Added to <see cref="CycleDriver.CyclesElapsed"/> by
    /// consumers that key off it (CycleBands, via OutdoorAtmosphere.DayIndexOffset) so a
    /// specific day's escalation (day 5's long night) is
    /// reachable in one launch instead of waiting out the real days before it. Deliberately
    /// NOT plumbed into CycleDriver itself (that file stays untouched this wave; see
    /// CycleBands' class doc) — this stays a local, presentation-side day-index derivation,
    /// so the actual replicated CyclesElapsed is never altered by a dev flag.</summary>
    public int CycleStartDay { get; private set; }

    /// <summary>--night-cycle-selftest: headless pure-logic checks of CycleBands (the
    /// band-boundary/escalation math OutdoorAtmosphere reads) that need
    /// no live scene, network, or Multiplayer API. Exits 0/1; tests/Run-NightCycleTest.ps1
    /// gates on it.</summary>
    public bool NightCycleSelfTest { get; private set; }
    // --- end WP-N1 -----------------------------------------------------------------------------

    // --- L1 (2026-08-06 lean-MVP wave, Issue #104) ---------------------------------------------
    /// <summary>--run-cycles &lt;n&gt;: the session's run length in day/night cycles. 0 = unset
    /// (the same sentinel <see cref="CyclePeriodSec"/> already uses) — RunDriver resolves an
    /// unset value to its own default (2) at Setup time rather than here, so "was this
    /// explicitly requested" stays answerable the same way CyclePeriodSec's does. n &lt;= 0 on
    /// the command line is ignored outright (a run of zero or negative cycles is meaningless),
    /// same defensive stance as every other count-like flag in this file.</summary>
    public int RunCycles { get; private set; }

    /// <summary>The run length Gameplay actually passes to RunDriver.Setup (CORE-PROG-A1,
    /// core-spine spec §1.5 / SD-1 decision D5): the open-ended canon (fact 15, amended
    /// 2026-08-13) retires the very concept of a configured run length, so an UNSET
    /// --run-cycles now resolves to effectively-uncapped — <see cref="int.MaxValue"/> —
    /// which makes RunPhaseTracker.IsRunEndCrossing structurally unreachable and neutralizes
    /// RunEndedSignal without modifying the locked RunDriver contract.
    /// <see cref="PlaythroughDriver"/>'s RunConcluded is the only run-end; no new subscriber
    /// may attach to RunEndedSignal. An EXPLICIT --run-cycles keeps its legacy meaning
    /// unchanged (every existing suite passes it, and Run-RunDriverTest's run-end/reset
    /// assertions stay valid against it). Deliberately a derived property rather than a new
    /// default on <see cref="RunCycles"/> itself: the raw field's 0=unset sentinel is pinned
    /// by RunDriverSelfTest and shared by CyclePeriodSec, and changing the sentinel would
    /// have silently re-answered "was this explicitly requested" everywhere it is asked.</summary>
    public int RunCyclesOrUncapped => RunCycles > 0 ? RunCycles : int.MaxValue;

    /// <summary>--run-reset-at &lt;sec&gt;: test-only (server). This many seconds after RunDriver's
    /// own Setup, the server fires exactly one <c>RunDriver.ResetRun()</c> on its own — the
    /// server-side analogue of <see cref="ForceReconnectAtSec"/>'s bot-side self-triggering
    /// timer, since a reset is server-authoritative and no bot flag could otherwise ask the
    /// SERVER to invoke it deterministically inside a headless run. Negative (default) =
    /// disabled; every real launch path leaves this unset. See Run-RunDriverTest.ps1.</summary>
    public double RunResetAtSec { get; private set; } = -1;

    /// <summary>--run-driver-selftest: headless pure-logic checks of the phase-crossing/run-end
    /// ordinal math (RunPhaseTracker) that need no live scene, network, or Multiplayer API.
    /// Exits 0/1; tests/Run-RunDriverTest.ps1 gates on it.</summary>
    public bool RunDriverSelfTest { get; private set; }
    // --- end L1 ----------------------------------------------------------------------------------

    // --- the hide-seek round (ROUND-1, 2026-09-19) ------------------------------------------------
    /// <summary>
    /// <c>--round-script "start@2,confirm@8,found@20,end@25"</c>: <b>a DEV flag</b> (server only)
    /// that feeds the round's facts on a schedule, so the whole loop can be driven end to end
    /// before BTN-1's buttons, CARRY-1's bin or TASK-1's sorting exists. Each entry is
    /// <c>&lt;verb&gt;[:&lt;value&gt;]@&lt;seconds&gt;</c>, seconds measured from the driver's own
    /// Setup, comma separated. Empty (every real launch) = nothing armed and no cost.
    ///
    /// <para><b>It can only ever ADD a fact, never suppress one.</b> The scripted source is one
    /// <c>IRoundFactSource</c> among however many are registered and the driver ORs the edges — so
    /// a human pressing a real Start on the same server is never overridden by the script, and the
    /// script can never stop a real press landing. That, plus "a launch flag no human types" and a
    /// loud <c>[round] DEV</c> line at Setup and at every fired verb, is the whole gate; there is
    /// no persisted per-player fact here for <c>IIntentSource.IsHumanInput</c> to guard.</para>
    ///
    /// <para><b>Verbs.</b> <c>start</c>, <c>confirm</c>, <c>end</c> are one-tick presses;
    /// <c>found</c> is the LEVEL "the target is in the bin" and latches on (a bin does not pulse).
    /// <c>object</c>/<c>noobject</c> and <c>holdtarget</c>/<c>droptarget</c> set the two hands
    /// facts; <c>reachable</c>/<c>unreachable</c> stand in for REACH-1; <c>sorts:N</c> sets the
    /// absolute sort count. The hands default to "holding a rack object, not holding the target,
    /// reachable" so the packet's own four-verb example runs a whole round unaided; the others
    /// exist so a suite can provoke each named refusal and watch the HUD render it.</para>
    /// </summary>
    public IReadOnlyList<(string Verb, string Value, double AtSec)> RoundScript => _roundScript;
    private readonly List<(string Verb, string Value, double AtSec)> _roundScript = new();
    // --- end ROUND-1 ------------------------------------------------------------------------------

    // --- the startle (DOOR-1, 2026-09-19) ---------------------------------------------------------
    /// <summary>
    /// <c>--startle-file &lt;path&gt;</c>: a JSON overlay on <c>StartleTuning</c>, so the burst
    /// door can be retuned <b>without a rebuild</b> — the packet's requirement, and the reason it
    /// is a file rather than fifteen more launch flags.
    ///
    /// <para>Empty (every ordinary launch, and every suite but DOOR-1's own) means the shipped
    /// defaults and no file access at all. A path that does not exist is also silent: that is a
    /// first run, not an error. Everything else the document can get wrong — bad JSON, a wrong
    /// version, a knob this build does not have, a value out of range — is reported as a
    /// <c>[startle]</c> warning line and the affected knob falls back, exactly as
    /// <c>MotorTuningFile</c> does with its own file.</para>
    ///
    /// <para><b>A path, not a fixed <c>user://</c> location</b>, which is the one place this
    /// deliberately diverges from <c>MotorTuningFile</c>. That file is written BY the game and
    /// read back, so a fixed private location is right for it; this one is written by a person in
    /// a text editor next to their notes, and burying it in AppData would make the "without a
    /// rebuild" promise cost a folder hunt instead. <c>StartleTuningFile.DefaultUserPath</c> is
    /// still there for anyone who wants the old shape.</para>
    /// </summary>
    public string StartleFile { get; private set; } = "";
    // --- end DOOR-1 -------------------------------------------------------------------------------
    // --- the round buttons (BTN-1, 2026-09-19) ----------------------------------------------------
    /// <summary>
    /// <c>--press "start@6,start@14,confirm@30"</c>: <b>a BOT flag</b> that fires the shipped
    /// press verb at the given elapsed seconds of this bot's own clock. Entries are
    /// <c>&lt;start|confirm|end&gt;@&lt;seconds&gt;</c>, comma separated; empty (every real
    /// launch, and every existing suite) means nothing is scheduled and nothing costs anything.
    ///
    /// <para><b>It drives the SHIPPED verb</b> — <c>RoundControls.ClientRequestPress</c>, the
    /// same method <c>RoundButton.Press</c> calls when a human presses Interact — so what a suite
    /// built on it exercises is the real gate, the real fact latch and the real broadcast. That
    /// is the first of the two rules <c>BotHarness</c>'s own comment left for this lane, written
    /// out of the retired honk probes: a scripted press through a test-only hook proves things
    /// about the harness.</para>
    ///
    /// <para><b>The second rule does not apply here, and it is worth saying why.</b> That comment
    /// warns that a rule the server enforces and the client MIRRORS needs a deliberately
    /// non-compliant client to reach the authoritative copy. There is no mirror to defeat:
    /// <c>RoundButton.Press</c> asks unconditionally, with no local gate and no debounce — the
    /// client never decides whether a press is allowed — so the shipped verb already reaches the
    /// server on every press, including the ones that will be refused. This flag can therefore
    /// stage a refusal by pressing at the wrong moment, which is exactly what the suite does.</para>
    ///
    /// <para><b>Elapsed seconds, and the suite must treat them as a BUDGET.</b> Anchoring a
    /// press on an observed event (the way <c>--carry-place</c> anchors on an observed hold)
    /// would be better and is not possible for the interesting case: the first press of the
    /// BTN-1 smoke has to land when the OTHER BOT IS NOT THERE, which is an absence, and the
    /// only clock either process shares at that point is its own. Every assertion downstream is
    /// anchored on a server log line instead.</para>
    /// </summary>
    public IReadOnlyList<(string Kind, double AtSec)> PressScript => _pressScript;
    private readonly List<(string Kind, double AtSec)> _pressScript = new();
    // --- end BTN-1 --------------------------------------------------------------------------------

    // --- REACH-1 (2026-09-19): placement integrity layers 2 and 3 ---------------------------------

    /// <summary><c>--reach-target &lt;propId&gt;</c>: server-only. Names the prop the round is
    /// hiding, so REACH-1's fact source has something to audit before BTN-1's object rack exists
    /// to choose one. −1 (the default) means no target, and the reachability fact then answers
    /// <c>null</c> — unmeasured — which is exactly what every suite that does not pass this flag
    /// needs it to do.</summary>
    public int ReachTargetPropId { get; private set; } = -1;

    /// <summary><c>--reach-selftest</c>: the planted-room self-test — six authored fixtures, each
    /// one of §5b's named cases, audited by the real layer-2 and layer-3 code on a real server.
    /// Implies <c>--world reachplant</c>. Prints one machine-readable summary line plus one
    /// verdict line per case and quits; <c>tests/Run-ReachTest.ps1</c> gates on the LINES, not on
    /// the exit code (the reason <c>--supermarket-selftest</c> does: a process that dies before
    /// its own summary exits non-zero for reasons that are not about the subject).</summary>
    public bool ReachSelfTest { get; private set; }

    /// <summary><c>--reach-cost &lt;seconds&gt;</c>: server-only. Attaches the cost probe that
    /// measures what §5b's audit actually costs — rest audits per second, physics queries per
    /// second, and server physics frame time p50/p95 — for the given number of seconds, then
    /// prints a summary and quits the server. 0 (the default) attaches nothing.
    ///
    /// <para>A flag rather than an always-on counter because the probe samples every physics
    /// tick, and a measurement rig that runs in a real session is a measurement rig that is part
    /// of the thing it measures.</para></summary>
    public double ReachCostSec { get; private set; }

    /// <summary><c>--cost-shove-every &lt;seconds&gt;</c>: SHELF-1 (2026-09-19). Overrides
    /// <c>ReachCostProbe.ShoveEverySec</c> (default 3). <b>Zero or negative means never shove</b>,
    /// which is the whole reason this exists: the probe is the only instrument in the repo that
    /// measures server physics frame time, and SHELF-1's packet asks for that number BOTH under
    /// a shove AND at rest. Without this flag "at rest" is unmeasurable — the probe would knock
    /// every prop in the room loose three seconds into the window it is sampling. Ignored
    /// unless <c>--reach-cost</c> attached the probe at all.</summary>
    public double CostShoveEverySec { get; private set; } = 3.0;

    // --- end REACH-1 ------------------------------------------------------------------------------

    // --- CLOCK-1 (2026-09-19) ---------------------------------------------------------------------
    /// <summary>
    /// <c>--log-clock</c>: a <b>dev instrument</b>, on both sides of the wire, for
    /// <c>tests/Run-RoundClockTest.ps1</c>.
    ///
    /// <para>On a client it makes <c>RoundAudio</c> print one line per <c>RoundClock</c> per
    /// second — <c>clock &lt;room&gt; &lt;phase&gt; &lt;M:SS&gt; shown=…</c>, read off the labels
    /// — and one line per audio cue as it fires. On a dedicated server it is also what causes
    /// <c>RoundAudio</c> to be attached AT ALL, in a log-only mode that plays nothing and drives
    /// no clock, purely so the suite has a server-side reference second to compare the clients
    /// against.</para>
    ///
    /// <para><b>It covers the cues as well as the clocks, and that is a merge decision rather
    /// than a design one.</b> SFX-1 adds a <c>--log-sfx</c> flag on its own branch for the
    /// material voices; this branch cannot see it, and two lanes each inventing a flag of that
    /// name is a conflict for no gain. The round's cue sequence is a fact about the round, so it
    /// rides the round's own flag; if the orchestrator wants it under <c>--log-sfx</c> after the
    /// merge, it is one line in <c>RoundAudio.Fire</c>.</para>
    ///
    /// <para>Off by default and it prints nothing when off, so it costs a bool per frame.</para>
    /// </summary>
    public bool LogClock { get; private set; }
    // --- end CLOCK-1 ------------------------------------------------------------------------------

    // --- Voice proximity gate + bandwidth instrumentation (perf followups, 2026-08-07) --------
    /// <summary>--net-stats &lt;path&gt;: the dedicated server appends one JSON line per second of
    /// ENet's own transport byte/packet counters plus the voice relay's relayed/gated counts.
    /// The measurement the perf audit's §2 budget never had — see
    /// <see cref="MpFoundation.Net.NetStatsLogger"/>. Empty (the default) = no logger, no cost.</summary>
    public string NetStatsLog { get; private set; } = "";

    /// <summary>--voice-gate on|off: runtime override of
    /// <see cref="MpFoundation.Net.VoiceProximityGate.EnabledByDefault"/>, so a measurement run
    /// or a test can exercise BOTH arms against one build instead of needing two. This is a
    /// test/measurement hook — <b>the shipping switch is the compile-time constant</b>, which is
    /// what Talon flips. Server-side only; a client never relays anything.</summary>
    public bool VoiceGateOverride { get; private set; }
    public bool VoiceGateOn { get; private set; }

    /// <summary>--voice-pa-all: test-only (server). Wires VoiceManager.PaResolver on the SERVER
    /// to "everyone is broadcasting on the PA", which is how the gate's PA exemption is proven
    /// end to end over a live session — the shipped game wires no PA resolver at all yet (grep:
    /// PaResolver has no production assignment), so without this hook the exemption branch would
    /// be untested code. Every real launch path leaves it off.</summary>
    public bool VoicePaAll { get; private set; }

    /// <summary>--intercom-wet-db &lt;db&gt;: the one intercom knob (VOICE-1), a dB trim on the
    /// PA bus reverb's wet mix. <b>0 (the default) is exactly what ships</b>; negative pulls the
    /// room off the voice when the intercom is too muddy for a taunt to be understood, positive
    /// clamps at fully wet. See <c>VoiceConfig.IntercomWetDb</c> and
    /// <c>VoiceRouting.WetFromDb</c>. Client-side — the PA bus is where audio is mixed, and a
    /// dedicated server mixes none.</summary>
    public float IntercomWetDb { get; private set; }
    // --- end perf followups ---------------------------------------------------------------------
    /// <summary>--authored-clips: drive the played body's base pose from the fourteen hand-keyed
    /// clips in <c>Greybox.glb</c> instead of from the shipped procedural gait (ANIM-M3).
    ///
    /// <para><b>Presentation only, in both positions.</b> No simulation reads it, no snapshot carries
    /// it, and two players in one session with different values still agree on every outcome — canon
    /// fact 4, the parity law. What differs is which pose a body is drawn in; every readout other
    /// systems consume (<c>CadenceHz</c>, <c>GaitPhase</c>, <c>DutyFactor</c>, <c>KneeBendRad</c>, …)
    /// keeps being written either way.</para>
    ///
    /// <para><b>Off by default because it replaces something ratified.</b> MOVE-1's and SKID-1's gait
    /// was tuned against Talon's eyes over two packets and is asserted by <c>Run-SandboxTest</c>
    /// (and, until ANIM-M2b deleted it under ruling 9, by <c>Run-BodyLanguageTest</c>); the
    /// migration's own brief says the judgment of whether the
    /// authored version feels better is his to make, and this flag is the instrument that makes it
    /// possible to make it — one build, both gaits. See
    /// <see cref="MpFoundation.Game.Sandbox.Anim.AvatarClipFlag"/>.</para></summary>
    public bool AuthoredClips { get; private set; }

    /// <summary>--capture-at-tick &lt;tick[,tick…]&gt;: capture the viewport when the REPLICATED
    /// server tick reaches each mark, rather than when this process's own wall clock does.
    ///
    /// <para><b>Why the existing <c>--capture-at</c> cannot do this job</b> (ANIM-M0 §5.4).
    /// <c>BotHarness.MaybeCapture</c> fires on <c>_elapsed</c>, the bot's own seconds since entering
    /// the tree. Two clients started sequentially have different origins by however long
    /// <c>Start-Process</c> took, so "the same elapsed second" is not "the same moment" and a
    /// two-client parity comparison made with it proves nothing. This fires on a clock both processes
    /// receive from the same server, which is the only clock that can express parity.</para>
    ///
    /// <para>Composes with <c>--capture-dir</c> / <c>--capture-name</c> exactly as
    /// <c>--capture-at</c> does, and the two may be used together — the marks are independent.</para></summary>
    public List<long> CaptureAtTick { get; } = new();

    /// <summary>--night-brightness &lt;multiplier&gt;: <b>presentation only.</b> Multiplies the
    /// RENDERED night ambient and moonlight by this factor, ramped in with the night so the day is
    /// untouched. 1 (or absent) is the shipped darkness, byte for byte; the code path is skipped
    /// entirely below 1.001, so a default launch cannot reach it.
    ///
    /// <para><b>Why it exists</b> (CATCH-1, 2026-08-16). Talon: <i>"the dark is hard to deal with
    /// because I can't even see anything."</i> The catching loop cannot be evaluated in a world
    /// where the things being caught are invisible, and the alternative — quietly raising the
    /// shipped floor — would trade a design decision for a diagnostic one. This is a dial for a
    /// playtest, not a tuning change.</para>
    ///
    /// <para><b>It cannot affect what anybody can SEE, in the gameplay sense.</b> Canon fact 4's
    /// parity law makes visibility server data identical on every client, and rendered light
    /// presentation only. This multiplier is applied in <c>OutdoorAtmosphere</c> AFTER
    /// <c>Evaluate</c> — which stays the pure, peer-identical function every atmosphere test
    /// asserts against — and it touches nothing in <c>PlayerSightCurve</c>,
    /// <c>FireNodeRegistry.IsGroundLit</c>, or any ward query. Two players on the same server with
    /// different values here still have identical sight ranges, identical lit ground and identical
    /// creature behaviour; one of them just has a brighter screen.</para>
    ///
    /// <para>Deliberately NOT forwarded to a hosted dedicated server by
    /// <c>LocalServerHost</c>: that child is launched <c>--headless</c> and renders nothing, so
    /// there is no atmosphere on it to brighten.</para></summary>
    public float NightBrightness { get; private set; } = 1f;

    /// <summary>--body-ramp &lt;n&gt;: the strength of the player body's vertical value ramp — the
    /// limbs-dark-to-head-light gradient Talon asked to be able to push (BODY-2, 2026-08-28).
    /// 1 reproduces the range the body already had, 0 is a flat body colour, 4 is the ceiling.
    ///
    /// <para>NaN when the flag was not given, so "unset" is distinguishable from "set to the
    /// default" and <c>Boot</c> can leave <c>AvatarVisual.BodyValueRampStrength</c>'s env-var
    /// source in charge rather than overwriting it with a value nobody typed. A sentinel float
    /// rather than a nullable because every other numeric option on this type is a plain float and
    /// a lone <c>float?</c> would read as significant.</para>
    ///
    /// <para><b>Presentation only</b>, for the same reason and with the same guarantee as
    /// <see cref="NightBrightness"/> above: it moves the albedo of player meshes and touches no
    /// sight range, no collision, no server state and nothing on the wire.</para></summary>
    public float BodyValueRamp { get; private set; } = float.NaN;

    /// <summary>--bubbletest-selftest: the Bubble Test level's compliance test — the layout
    /// contract (anchors, spawn ring, world extent), the bake check that proves no section
    /// builds geometry at runtime, the collider audit and its positive control, the sky, and a
    /// live off-map respawn. Unlike the pure-logic *SelfTest flags above, this one instantiates
    /// the real shipped scene (see BubbleTestSelfTest's doc comment for why). Exits 0/1;
    /// tests/Run-BubbleTestWorldTest.ps1 gates on it.</summary>
    public bool BubbleTestSelfTest { get; private set; }

    /// <summary>--tvportal-selftest: BT-10's TV easter egg — three portals wired, the
    /// hub -> room -> hub round trip through the real triggers, the 800 ms re-entry gate, and
    /// the two ways the sealed room could strand a player. Instantiates the real shipped scene,
    /// like --bubbletest-selftest and for the same reason. Exits 0/1;
    /// tests/Run-TvPortalTest.ps1 gates on it.</summary>
    public bool TvPortalSelfTest { get; private set; }

    /// <summary>--puffinlab-selftest: EGG-1's Puffin Lab throwback — the EGG-2 seam (Arrival,
    /// ReturnTv) asserted by node path and exact type, the absence check that no CanvasLayer,
    /// ColorRect, Control, WorldEnvironment or Camera3D is authored into or built by the ported
    /// scene (both halves carry positive controls), the live avatar's own capsule fitted at every
    /// station of the route including the tiny door, and the return TV's trigger proved live.
    /// Exits 0/1; tests/Run-PuffinLabTest.ps1 gates on it.</summary>
    public bool PuffinLabSelfTest { get; private set; }

    /// <summary>--bubble-selftest (BT-6): installs the bubble fixture — six bubbles at known
    /// points plus a <c>BubbleCounter</c> — into whatever world is loaded, on WHICHEVER peer is
    /// given the flag. Pass it to the server AND to every bot: each peer builds the same bubbles
    /// locally and only the tally travels (program D6), so a peer without the flag would have
    /// nothing for the RPCs to land on. Unlike the <c>--*-selftest</c> flags above, this one does
    /// NOT short-circuit Boot into a headless check and quit — the thing under test is a real
    /// networked session, so it rides an ordinary <c>--server</c>/<c>--bot</c> launch.
    /// tests/Run-BubbleSyncTest.ps1 is the harness.</summary>
    public bool BubbleSelfTest { get; private set; }

    /// <summary>--bubble-reset-at &lt;sec&gt;: test-only (server). This many seconds into the
    /// session, calls <c>BubbleCounter.ServerReset</c> once — the same entry point BT-8's
    /// pedestal lever will call (program D9), staged deterministically so the sync test can prove
    /// every bubble comes back and every peer's tally returns to 0 without a second launch. The
    /// <c>--quota-bank-at</c> idiom. Negative (default) = never.
    ///
    /// <para>A second flag rather than a mode packed into <c>--bubble-selftest</c>'s value because
    /// the reset is a SCHEDULE, not a variant: the fixture is identical either way, and folding a
    /// number into a boolean flag is how a launch line stops being greppable.</para></summary>
    public double BubbleResetAtSec { get; private set; } = -1;

    /// <summary>--bubble-pop-all-at &lt;sec&gt;: test-only (server, needs --bubble-selftest).
    /// This many seconds into the session, the fixture pops EVERY remaining bubble through
    /// <c>BubbleCounter.ServerPop</c> — the real adjudication path, one call per bubble, not a
    /// back door into the bitset. CELEBRATE-1's harness: completion is the thing under test and a
    /// bot walk cannot reach six bubbles scattered across two corridors deterministically, so the
    /// walk is replaced by a schedule and everything downstream of the pop is the shipped code.
    ///
    /// <para>If <c>--bubble-reset-at</c> is also given, the fixture schedules a SECOND full pop by
    /// itself, <c>BubbleSelfTest.RecompleteDelaySec</c> after the reset lands, and announces both
    /// marks — so the reset-then-recomplete cycle is derived from the lever's own constants rather
    /// than typed a third time beside them (the FIX-1 lesson). Negative (default) = never.</para></summary>
    public double BubblePopAllAtSec { get; private set; } = -1;

    /// <summary>--celebrate-force: test-only. Bypasses <c>BubbleCelebration</c>'s human-input gate
    /// on THIS peer, so a scripted bot performs the all-bubbles celebration it would otherwise
    /// correctly refuse.
    ///
    /// <para><b>It is the positive control, and the suite is worthless without it.</b> "No bot
    /// celebrated" is indistinguishable from "the broadcast never arrived" unless a peer in the
    /// same shape can be made to celebrate on demand — the <c>--honk-forge</c> / <c>--voice-flood</c>
    /// precedent, probes that exist so a claimed property is measured rather than read off an
    /// attribute.</para>
    ///
    /// <para><b>It cannot unlock an achievement.</b> It reaches exactly one boolean in
    /// <c>BubbleCelebration</c>; <c>AchievementRuntime</c> is built (or not) in
    /// <c>SandboxAvatar.ConfigureAsNetworked</c>, which never reads it, so a forced bot still
    /// writes nothing into the shared <c>user://</c> profile.</para></summary>
    public bool CelebrateForce { get; private set; }

    /// <summary>--graphics low|medium|high: force the <c>GraphicsQuality</c> tier for this peer.
    /// Null leaves the shipped default (<c>Medium</c>) alone.
    ///
    /// <para><b>Why a launch flag and not a setting.</b> Nothing in this repo set the tier before
    /// Story #180 — <c>GraphicsQuality.Current</c> was a public property with no writer, so every
    /// session ran Medium and the tier system was untestable. The parity law (plan §5.2) says
    /// gameplay quantities are identical on every client "regardless of graphics settings", and
    /// that claim cannot be demonstrated at all unless two peers can be launched onto different
    /// tiers and their gameplay readouts diffed. A settings UI for this is a separate, later
    /// job.</para></summary>
    public MpFoundation.World.GraphicsQuality.Tier? GraphicsTier { get; private set; }

    /// <summary>Headless scripted-bot "walk to a point, optionally sprint" brain (see
    /// ScriptedGotoIntentSource) — a timed walk whose arrival the suite can assert on.
    /// --goto-script x,z[,y]: target world point (y optional, defaults to 0 — the intent source
    /// only reads XZ for its arrival check, so an unset y is harmless). --goto-sprint: hold
    /// Sprint the whole walk.</summary>
    public bool GotoScript { get; private set; }
    public Vector3 GotoTarget { get; private set; }
    public bool GotoSprint { get; private set; }

    public int EffectiveProtocol => ProtocolOverride >= 0 ? ProtocolOverride : Protocol.Version;

    public static LaunchOptions Parse(string[] args)
    {
        var options = new LaunchOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server":
                    options.Server = true;
                    break;
                case "--bot":
                    options.Bot = true;
                    break;
                case "--practice":
                    options.Practice = true;
                    break;
                case "--spectate-cam":
                    options.SpectateCam = true;
                    break;
                case "--first-person-cam":
                    options.FirstPersonCam = true;
                    break;
                case "--first-person-selftest":
                    options.FirstPersonSelfTest = true;
                    break;
                case "--rotate-look-selftest":
                    options.RotateLookSelfTest = true;
                    options.FirstPersonCam = true;
                    break;
                case "--fp-look":
                {
                    // "<yawDeg>" or "<yawDeg>,<pitchDeg>". A malformed field is DROPPED rather
                    // than defaulting to zero — the same rule --capture-at's marks follow, and for
                    // the same reason: a silently-zeroed angle is a capture pointed at a wall that
                    // nobody can tell from a capture that was aimed there.
                    string[] look = Next(args, ref i).Split(',');
                    if (look.Length >= 1 && double.TryParse(look[0], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double yawDeg))
                    {
                        options.HasFirstPersonLook = true;
                        options.FirstPersonLookYaw = Mathf.DegToRad((float)yawDeg);
                    }
                    if (look.Length >= 2 && double.TryParse(look[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double pitchDeg))
                    {
                        options.HasFirstPersonLook = true;
                        options.FirstPersonLookPitch = Mathf.DegToRad((float)pitchDeg);
                    }
                    break;
                }
                case "--windowed":
                    options.Windowed = true;
                    break;

                // --- SICK-1 (2026-09-20): the comfort flags -------------------------------
                //
                // Every one of these exists so Talon can A/B on his own machine WITHOUT a
                // rebuild, which is the packet's requirement and the reason none of them is a
                // settings-menu row yet: a knob nobody has ridden does not deserve a UI.
                case "--cam-interp":
                    options.CameraInterpolation = ParseOnOff(Next(args, ref i),
                        options.CameraInterpolation);
                    break;
                case "--fov":
                {
                    // Clamped, not validated-and-refused: the flag exists to be swept by hand
                    // during an A/B, and a typo that would make the lens unusable should land on
                    // the nearest usable lens rather than end the launch. The bounds are the two
                    // ends of what the arithmetic in FirstPersonCamera.DefaultFovDeg keeps sane:
                    // 50 deg vertical is 83 deg horizontal on 16:9, 110 is 140.
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double fovDeg))
                        options.FovDeg = Mathf.Clamp((float)fovDeg, 50f, 110f);
                    break;
                }
                case "--vsync":
                    options.Vsync = ParseOnOff(Next(args, ref i), true);
                    break;
                case "--max-fps":
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int maxFps) && maxFps >= 0)
                        options.MaxFps = maxFps;
                    break;
                case "--pacing-log":
                    options.PacingLogPath = Next(args, ref i);
                    break;
                case "--pacing-sec":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double pacingSec) && pacingSec > 0)
                        options.PacingSec = pacingSec;
                    break;
                case "--pacing-turn":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double turnDeg))
                        options.PacingTurnDegPerSec = (float)turnDeg;
                    break;
                case "--pacing-label":
                    options.PacingLabel = Next(args, ref i);
                    break;
                case "--capture-dir":
                    options.CaptureDir = Next(args, ref i);
                    break;
                case "--show-first-run-panels":
                    options.ShowFirstRunPanels = true;
                    break;
                case "--capture-at":
                {
                    // Two accepted forms, because two branches shipped this flag independently and
                    // both have live callers in tests/ (see the CaptureName merge note above):
                    //
                    //   --capture-dir <dir> --capture-at 3,7,11     (capture/harness; bare marks)
                    //   --capture-at <dir>,<name>,3,7,11            (two-slot carry; dir+stem+marks)
                    //
                    // They are told apart by the only thing that distinguishes them: whether the
                    // leading fields parse as numbers. A bare list of marks is form one; anything
                    // whose first field is not a number is form two. Nothing else changes meaning,
                    // so neither caller has to be edited and neither form can be silently misread.
                    string[] parts = Next(args, ref i).Split(',');
                    int firstMark = 0;
                    if (parts.Length >= 3
                        && !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        options.CaptureDir = parts[0];
                        options.CaptureName = parts[1];
                        firstMark = 2;
                    }
                    // Every well-formed, non-negative, finite mark is kept; a malformed one is
                    // dropped rather than defaulting to 0, which would silently shoot frame one.
                    for (int k = firstMark; k < parts.Length; k++)
                    {
                        if (double.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out double atSec)
                            && double.IsFinite(atSec) && atSec >= 0
                            && !options.CaptureAtSec.Contains(atSec))
                        {
                            options.CaptureAtSec.Add(atSec);
                        }
                    }
                    options.CaptureAtSec.Sort();
                    break;
                }
                case "--capture-cam":
                {
                    string[] parts = Next(args, ref i).Split(',');
                    // All six or none — a partially-parsed camera would point somewhere nobody
                    // asked for and the capture would look like a framing mistake, not a bad arg.
                    if (parts.Length >= 6)
                    {
                        var v = new float[6];
                        bool ok = true;
                        for (int k = 0; k < 6; k++)
                        {
                            ok &= float.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k])
                                  && float.IsFinite(v[k]);
                        }
                        // A camera sitting exactly on its own look-at target has no basis to
                        // orient from (LookAt would fail); reject rather than emit a broken view.
                        var pos = new Vector3(v[0], v[1], v[2]);
                        var look = new Vector3(v[3], v[4], v[5]);
                        if (ok && !pos.IsEqualApprox(look))
                        {
                            options.CaptureCamPos = pos;
                            options.CaptureCamLookAt = look;
                            options.HasCaptureCam = true;
                        }
                    }
                    break;
                }
                case "--port":
                    // Same 1..65535 gate TryParseAddress applies — an out-of-range value would
                    // otherwise reach ENet and fail with an opaque engine error.
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                        && port is >= 1 and <= 65535)
                        options.Port = port;
                    break;
                case "--address":
                    if (TryParseAddress(Next(args, ref i), out string host, out int addrPort))
                    {
                        options.Host = host;
                        options.Port = addrPort;
                        options.AddressExplicit = true;
                    }
                    break;
                case "--name":
                    options.DisplayName = Next(args, ref i);
                    break;
                case "--log":
                    options.LogPath = Next(args, ref i);
                    break;
                case "--perf-log":
                    options.PerfLog = Next(args, ref i);
                    break;
                case "--duration":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                        options.DurationSec = duration;
                    break;
                case "--room":
                    options.Room = Next(args, ref i).Trim().ToUpperInvariant();
                    break;
                case "--log-dir":
                    options.LogDir = Next(args, ref i);
                    break;
                case "--protocol":
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int proto))
                        options.ProtocolOverride = proto;
                    break;
                case "--bad-auth":
                    options.BadAuth = true;
                    break;
                case "--voice-send":
                    options.VoiceSend = true;
                    break;
                case "--voice-flood":
                    options.VoiceFlood = true;
                    break;
                case "--voice-garbage":
                    options.VoiceGarbage = true;
                    break;
                case "--voice-oversize":
                    options.VoiceOversize = true;
                    break;
                case "--conn-limit":
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int connLimit) && connLimit > 0)
                        options.ConnLimit = connLimit;
                    break;
                case "--world":
                    string world = Next(args, ref i).Trim().ToLowerInvariant();
                    if (world.Length > 0)
                    {
                        options.World = world;
                        options.WorldExplicit = true;
                    }
                    break;
                case "--room-code":
                    string roomCode = Next(args, ref i).Trim().ToUpperInvariant();
                    if (MpFoundation.Net.RoomCode.IsValid(roomCode))
                        options.RoomCode = roomCode;
                    break;
                case "--net-sim":
                {
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) && lat >= 0)
                    {
                        options.NetSimEnabled = true;
                        options.NetSimLatencyMs = lat;
                    }
                    if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double loss) && loss >= 0)
                        options.NetSimLossPct = System.Math.Min(loss, 100);
                    if (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double jitter) && jitter >= 0)
                        options.NetSimJitterMs = jitter;
                    break;
                }
                case "--cheat-move":
                    options.CheatMove = true;
                    break;
                case "--transport":
                    string transport = Next(args, ref i).Trim().ToLowerInvariant();
                    if (transport is TransportEnet or TransportSteam)
                        options.Transport = transport;
                    break;
                case "--steam-app-id":
                    if (uint.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint steamAppId) && steamAppId != 0)
                        options.SteamAppId = steamAppId;
                    break;
                case "--join-room-code":
                    // Same normalization as --room-code so a case difference can never be
                    // mistaken for a genuine capability mismatch.
                    string joinRoomCode = Next(args, ref i).Trim().ToUpperInvariant();
                    if (MpFoundation.Net.RoomCode.IsValid(joinRoomCode))
                        options.JoinRoomCode = joinRoomCode;
                    break;
                case "--steam-selftest":
                    options.SteamSelfTest = true;
                    break;
                case "--reconnect-selftest":
                    options.ReconnectSelfTest = true;
                    break;
                case "--cycle-selftest":
                    options.CycleSelfTest = true;
                    break;
                case "--voice-mute-selftest":
                    options.VoiceMuteSelfTest = true;
                    break;
                case "--supermarket-selftest":
                    options.SupermarketSelfTest = true;
                    break;
                // --- STOCK-1 -------------------------------------------------------------
                case "--stock-selftest":
                    options.StockSelfTest = true;
                    break;
                // --- REACH-1 -------------------------------------------------------------
                case "--reach-selftest":
                    options.ReachSelfTest = true;
                    // The planted room is the only world this test means anything in, and a run
                    // that forgot the second flag would audit the empty search room and report
                    // six passes about nothing. Implied here rather than required at the call
                    // site, for the same reason --first-person-selftest implies its camera.
                    options.World = Game.World.ReachPlantWorld.WorldId;
                    options.WorldExplicit = true;
                    break;
                case "--reach-target":
                    if (int.TryParse(Next(args, ref i).Trim(), out int reachTarget))
                        options.ReachTargetPropId = reachTarget;
                    else
                        GD.PushWarning("[reach] --reach-target: not an integer prop id; ignored");
                    break;
                case "--reach-cost":
                    if (double.TryParse(Next(args, ref i).Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double reachCost) && reachCost > 0)
                        options.ReachCostSec = reachCost;
                    else
                        GD.PushWarning("[reach] --reach-cost: not a positive number of seconds; ignored");
                    break;
                case "--cost-shove-every":
                    // NOT gated on > 0: zero and negative are the MEANINGFUL values here (never
                    // shove — the at-rest measurement). Only an unparseable argument is ignored.
                    if (double.TryParse(Next(args, ref i).Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double shoveEvery))
                        options.CostShoveEverySec = shoveEvery;
                    else
                        GD.PushWarning("[reach] --cost-shove-every: not a number of seconds; ignored");
                    break;
                case "--build-ui-theme":
                    options.BuildUiTheme = true;
                    break;
                case "--ui-theme-demo":
                    options.UiThemeDemo = true;
                    break;
                case "--ui-theme-selftest":
                    options.UiThemeSelfTest = true;
                    break;
                case "--telemetry-self-test":
                    options.TelemetrySelfTest = true;
                    break;
                case "--host-selftest":
                    options.HostSelfTest = true;
                    break;
                case "--parent-managed":
                    options.ParentManaged = true;
                    break;
                case "--cycle-period":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double cyclePeriod) && cyclePeriod > 0)
                        options.CyclePeriodSec = cyclePeriod;
                    break;
                case "--cycle-start-phase":
                {
                    // A number wins outright; anything else is kept as a NAME and resolved after the
                    // loop, once --cycle-start-day is known whichever order the two were written in.
                    string phaseToken = Next(args, ref i);
                    if (float.TryParse(phaseToken, NumberStyles.Float, CultureInfo.InvariantCulture, out float cycleStartPhase))
                    {
                        options.CycleStartPhase = Mathf.Clamp(cycleStartPhase, 0f, 0.999999f);
                        options.CycleStartPhaseName = "";
                    }
                    else
                    {
                        options.CycleStartPhaseName = phaseToken;
                    }
                    break;
                }
                case "--cycle-freeze":
                    options.CycleFreeze = true;
                    break;
                case "--force-reconnect-at":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double frAt) && frAt >= 0)
                        options.ForceReconnectAtSec = frAt;
                    break;
                case "--carry-script":
                {
                    // x,y,z,earliestGrabSec[,holdSec[,throwSec]]
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 3
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double tx)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ty)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tz))
                    {
                        options.CarryScript = true;
                        options.CarryTarget = new Vector3((float)tx, (float)ty, (float)tz);
                    }
                    if (parts.Length >= 4 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double grabAt))
                        options.CarryGrabAtSec = grabAt;
                    if (parts.Length >= 5 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double hold))
                        options.CarryHoldSec = hold;
                    if (parts.Length >= 6 && double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double throwAt))
                        options.CarryThrowSec = throwAt;
                    break;
                }
                case "--carry-throw-scale":
                {
                    if (double.TryParse(Next(args, ref i).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double throwScale))
                        options.CarryThrowScale = throwScale;
                    break;
                }
                case "--carry-regrab":
                {
                    if (int.TryParse(Next(args, ref i).Trim(), out int regrabId))
                    {
                        options.CarryRegrab = true;
                        options.CarryRegrabPropId = regrabId;
                    }
                    break;
                }
                case "--carry-target-prop":
                    if (int.TryParse(Next(args, ref i).Trim(), out int targetProp))
                        options.CarryTargetPropId = targetProp;
                    break;
                case "--carry-place":
                {
                    // x,y,z,yawDeg,afterSec: once this bot is holding anything, wait afterSec and
                    // then ask the server to PLACE it at that transform. See CarryPlaceAt.
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 5
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double plx)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ply)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double plz)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double plyaw)
                        && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double plafter))
                    {
                        options.CarryPlace = true;
                        options.CarryPlaceAt = new Transform3D(
                            new Basis(Vector3.Up, Mathf.DegToRad((float)plyaw)),
                            new Vector3((float)plx, (float)ply, (float)plz));
                        options.CarryPlaceAfterSec = plafter;
                    }
                    break;
                }
                case "--sort-script":
                {
                    // "propId:binSlot[,propId:binSlot...]" -- see SortScript. A malformed entry
                    // is SKIPPED rather than defaulted: a fixture that silently sorted prop 0
                    // into bin 0 because a digit was fat-fingered would be a green suite about
                    // nothing.
                    foreach (string entry in Next(args, ref i).Split(',',
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] bits = entry.Split(':');
                        if (bits.Length == 2
                            && int.TryParse(bits[0].Trim(), out int sortProp)
                            && int.TryParse(bits[1].Trim(), out int sortBin))
                        {
                            options._sortScript.Add((sortProp, sortBin));
                        }
                    }
                    break;
                }
                case "--spawn-room":
                    options.SpawnRoom = Next(args, ref i).Trim().ToLowerInvariant();
                    break;
                // NOT lower-cased, unlike --spawn-room above: this value contains PEER NAMES and
                // is matched against SandboxAvatar.DisplayName exactly. A room id is a keyword
                // the game owns; a display name belongs to whoever typed it.
                case "--spawn-index":
                    options.SpawnIndexSpec = Next(args, ref i).Trim();
                    break;
                case "--hold-notches":
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int notches))
                        options.HoldNotches = notches;
                    break;
                case "--hold-stress":
                    options.HoldStress = true;
                    break;
                case "--walk-speed":
                    if (float.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out float walkSpeed))
                        options.WalkSpeedMps = walkSpeed;
                    break;
                case "--carry-grab-retry":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double grabRetry))
                        options.CarryGrabRetrySec = grabRetry;
                    break;
                case "--exit-when-holding":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double exitHold))
                        options.ExitWhenHoldingSec = exitHold;
                    break;
                case "--seed-test-props":
                {
                    // "x,y,z[,kind][;x,y,z[,kind]...]" -- one prop per entry, in whatever world
                    // is running, Crate when the kind is omitted.
                    // A malformed entry is DROPPED rather than defaulted to the origin: a crate
                    // silently at (0,0,0) is a fixture in the wrong place, and the suite that
                    // asked for it would then fail on carry rather than on its own arguments.
                    // An UNRECOGNISED kind name is dropped for the same reason and by the same
                    // rule -- a typo that silently seeded a crate would let the material suite
                    // report "the can never fired TinPick" about a can that was never a can.
                    foreach (string triple in Next(args, ref i).Split(';'))
                    {
                        string[] parts = triple.Split(',');
                        if (parts.Length < 3
                            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double sx)
                            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sy)
                            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double sz))
                        {
                            continue;
                        }
                        MpFoundation.Net.PropKind kind = MpFoundation.Net.PropKind.Crate;
                        if (parts.Length >= 4 && parts[3].Trim().Length > 0)
                        {
                            if (!TryParsePropKind(parts[3], out kind))
                                continue;
                        }
                        options._seedTestProps.Add((new Vector3((float)sx, (float)sy, (float)sz), kind));
                    }
                    break;
                }
                case "--seed-props-drop":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double dropAt))
                        options.SeedPropsDropAtSec = dropAt;
                    break;
                case "--log-sfx":
                    options.LogSfx = true;
                    break;
                case "--aim-script":
                {
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 1
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double raiseAt))
                    {
                        options.AimScript = true;
                        options.AimRaiseAtSec = raiseAt;
                    }
                    if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lowerAt))
                        options.AimLowerAtSec = lowerAt;
                    break;
                }
                case "--carry-walk-to":
                {
                    // x,z ground point the bot carries the grabbed prop toward after grabbing.
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 2
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double wx)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double wz))
                    {
                        options.CarryWalk = true;
                        options.CarryWalkTo = new Vector3((float)wx, 0f, (float)wz);
                    }
                    break;
                }
                case "--carry-patrol":
                {
                    // x1,z1,x2,z2: two ground points; while holding, ping-pong between them (never stops).
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 4
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double px1)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pz1)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double px2)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double pz2))
                    {
                        options.CarryPatrol = true;
                        options.CarryPatrolA = new Vector3((float)px1, 0f, (float)pz1);
                        options.CarryPatrolB = new Vector3((float)px2, 0f, (float)pz2);
                    }
                    break;
                }
                // --- WP-N1 (2026-07-26 wave) ---------------------------------------------
                case "--cycle-start-day":
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycleStartDay) && cycleStartDay >= 0)
                        options.CycleStartDay = cycleStartDay;
                    break;
                case "--night-cycle-selftest":
                    options.NightCycleSelfTest = true;
                    break;
                // --- end WP-N1 -------------------------------------------------------------
                // --- L1 (2026-08-06 lean-MVP wave, Issue #104) ------------------------------
                case "--run-cycles":
                    if (int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int runCycles) && runCycles > 0)
                        options.RunCycles = runCycles;
                    break;
                case "--run-reset-at":
                    if (double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double runResetAt) && runResetAt >= 0)
                        options.RunResetAtSec = runResetAt;
                    break;
                case "--run-driver-selftest":
                    options.RunDriverSelfTest = true;
                    break;
                case "--round-script":
                {
                    // "<verb>[:<value>]@<seconds>[,...]". A malformed entry is DROPPED with a
                    // warning rather than defaulted to t=0: a start verb silently firing on the
                    // first frame is a fixture in the wrong place, and the suite that asked for it
                    // would then fail on the round rather than on its own arguments — the same
                    // call --seed-test-props already makes, a few cases up.
                    foreach (string raw in Next(args, ref i).Split(','))
                    {
                        string entry = raw.Trim();
                        if (entry.Length == 0)
                            continue;
                        int at = entry.LastIndexOf('@');
                        if (at <= 0 || !double.TryParse(entry[(at + 1)..], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double sec) || sec < 0)
                        {
                            GD.PushWarning($"[round] --round-script: dropped malformed entry '{entry}' " +
                                           "(expected <verb>[:<value>]@<seconds>)");
                            continue;
                        }
                        string head = entry[..at];
                        int colon = head.IndexOf(':');
                        string verb = (colon < 0 ? head : head[..colon]).Trim().ToLowerInvariant();
                        string value = colon < 0 ? "" : head[(colon + 1)..].Trim();
                        if (verb.Length == 0)
                        {
                            GD.PushWarning($"[round] --round-script: dropped entry with no verb: '{entry}'");
                            continue;
                        }
                        options._roundScript.Add((verb, value, sec));
                    }
                    // Sorted so the driver walks one cursor forward, exactly as CaptureAtSec is.
                    options._roundScript.Sort((a, b) => a.AtSec.CompareTo(b.AtSec));
                    break;
                }
                // CLOCK-1 (2026-09-19). A bare presence flag on purpose: it turns logging on and
                // has nothing to configure, and every "--log-clock on|off" in this file exists
                // only where a flag OVERRIDES a default that is already true.
                case "--log-clock":
                    options.LogClock = true;
                    break;
                // --- end L1 ------------------------------------------------------------------
                // --- the startle (DOOR-1, 2026-09-19) ----------------------------------------
                case "--startle-file":
                    options.StartleFile = Next(args, ref i);
                    break;
                // --- end DOOR-1 --------------------------------------------------------------
                case "--press":
                {
                    // "<start|confirm|end>@<seconds>[,...]". A malformed entry is DROPPED with a
                    // warning rather than defaulted to t=0, the same call --round-script and
                    // --seed-test-props already make: a press silently firing on the first frame
                    // is a fixture in the wrong place, and the suite would then fail on the round
                    // rather than on its own arguments.
                    foreach (string raw in Next(args, ref i).Split(','))
                    {
                        string entry = raw.Trim();
                        if (entry.Length == 0)
                            continue;
                        int at = entry.LastIndexOf('@');
                        if (at <= 0 || !double.TryParse(entry[(at + 1)..], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double sec) || sec < 0)
                        {
                            GD.PushWarning($"[buttons] --press: dropped malformed entry '{entry}' " +
                                           "(expected <start|confirm|end>@<seconds>)");
                            continue;
                        }
                        string kind = entry[..at].Trim().ToLowerInvariant();
                        if (kind is not ("start" or "confirm" or "end"))
                        {
                            GD.PushWarning($"[buttons] --press: dropped entry '{entry}' — " +
                                           "the buttons are start, confirm and end");
                            continue;
                        }
                        options._pressScript.Add((kind, sec));
                    }
                    // Sorted so the harness walks one cursor forward, exactly as CaptureAtSec is.
                    options._pressScript.Sort((a, b) => a.AtSec.CompareTo(b.AtSec));
                    break;
                }
                // --- end BTN-1 ---------------------------------------------------------------
                // --- perf followups (2026-08-07) -------------------------------------------
                case "--net-stats":
                    options.NetStatsLog = Next(args, ref i);
                    break;
                case "--voice-gate":
                {
                    // Explicit on|off rather than a bare presence flag: this overrides a
                    // compile-time default that may itself be either value, so "was it asked
                    // for" and "which way" have to be two separate facts. Anything else is
                    // ignored outright and the compiled default stands — same defensive stance
                    // as every other value-parsing flag in this file.
                    string gate = Next(args, ref i).Trim().ToLowerInvariant();
                    if (gate is "on" or "off")
                    {
                        options.VoiceGateOverride = true;
                        options.VoiceGateOn = gate == "on";
                    }
                    break;
                }
                case "--voice-pa-all":
                    options.VoicePaAll = true;
                    break;
                case "--intercom-wet-db":
                {
                    // A malformed value is IGNORED rather than defaulted to 0, which is the
                    // shipped character: "--intercom-wet-db verydry" must not read as a
                    // deliberate request for exactly what ships. Invariant culture, like every
                    // other numeric flag here — a comma decimal separator is a locale, not a
                    // second syntax.
                    if (float.TryParse(Next(args, ref i), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float intercomWetDb))
                    {
                        options.IntercomWetDb = intercomWetDb;
                    }
                    break;
                }
                // --- end perf followups ------------------------------------------------------
                case "--authored-clips":
                    options.AuthoredClips = true;
                    break;
                case "--capture-at-tick":
                {
                    // Bare comma-separated ticks only — the dir/name two-slot form belongs to
                    // --capture-at and duplicating it here would be a second parser for one
                    // convention. A malformed or negative mark is DROPPED rather than defaulting to
                    // 0, which would silently shoot the first frame of the session.
                    foreach (string part in Next(args, ref i).Split(','))
                    {
                        if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long atTick)
                            && atTick >= 0 && !options.CaptureAtTick.Contains(atTick))
                        {
                            options.CaptureAtTick.Add(atTick);
                        }
                    }
                    options.CaptureAtTick.Sort();
                    break;
                }
                case "--night-brightness":
                    if (float.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out float nightBrightness))
                        options.NightBrightness = Mathf.Clamp(nightBrightness, 1f, 20f);
                    break;
                case "--body-ramp":
                    // Clamped against AvatarVisual's own bounds rather than literals here, so the
                    // flag and the property cannot come to disagree about what the ceiling is.
                    if (float.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out float bodyRamp))
                        options.BodyValueRamp = Mathf.Clamp(
                            bodyRamp,
                            AvatarVisual.MinBodyValueRampStrength,
                            AvatarVisual.MaxBodyValueRampStrength);
                    break;
                case "--graphics":
                {
                    string want = Next(args, ref i).Trim().ToLowerInvariant();
                    options.GraphicsTier = want switch
                    {
                        "low" => MpFoundation.World.GraphicsQuality.Tier.Low,
                        "medium" => MpFoundation.World.GraphicsQuality.Tier.Medium,
                        "high" => MpFoundation.World.GraphicsQuality.Tier.High,
                        _ => null,
                    };
                    // Said out loud rather than silently ignored: a dev flag that is accepted and
                    // then quietly does nothing is indistinguishable from one that is broken, and
                    // costs a relaunch to find out which.
                    if (options.GraphicsTier == null)
                        GD.PushWarning($"[options] --graphics '{want}' is not low|medium|high; ignored");
                    break;
                }
                // --- end --graphics --------------------------------------------------------
                case "--goto-script":
                {
                    // x,z[,y]
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 2
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double gx)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double gz))
                    {
                        double gy = 0;
                        if (parts.Length >= 3)
                            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out gy);
                        options.GotoScript = true;
                        options.GotoTarget = new Vector3((float)gx, (float)gy, (float)gz);
                    }
                    break;
                }
                case "--goto-sprint":
                    options.GotoSprint = true;
                    break;
                case "--goto-patrol":
                {
                    // x1,z1,x2,z2 — the SAME shape as --carry-patrol, on purpose: two ground
                    // points, ping-pong for ever, no hold required. SICK-1's frame-pacing runs
                    // need a body that is still walking thirty seconds in. See
                    // ScriptedPatrolIntentSource.
                    string[] parts = Next(args, ref i).Split(',');
                    if (parts.Length >= 4
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double gpx1)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double gpz1)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double gpx2)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double gpz2))
                    {
                        options.GotoPatrol = true;
                        options.GotoPatrolA = new Vector3((float)gpx1, 0f, (float)gpz1);
                        options.GotoPatrolB = new Vector3((float)gpx2, 0f, (float)gpz2);
                    }
                    break;
                }
                // --- end --goto ------------------------------------------------------------
            }
        }
        options.ResolveNamedCycleStartPhase();
        return options;
    }

    /// <summary>Turns a NAMED <c>--cycle-start-phase</c> into a number, once, after the whole
    /// argument list has been read — so <c>--cycle-start-day</c> is already known no matter which
    /// side of the name it was written on. An unknown name leaves the phase at its default and
    /// prints what it would have accepted, because a review harness silently reviewing at day-start
    /// when it was asked for noon is the worst available outcome: it looks like it worked.
    ///
    /// <para><see cref="CycleStartDay"/> is the right day to resolve against even though it never
    /// reaches <c>CycleDriver.CyclesElapsed</c> (it is presentation-side by design — see its own
    /// doc): the BANDS the atmosphere actually renders are keyed off
    /// <c>CyclesElapsed + DayIndexOffset</c>, and this flag is that offset. Noon on the day you are
    /// looking at is noon where the sun is, which is what a lighting baseline means.</para></summary>
    private void ResolveNamedCycleStartPhase()
    {
        if (CycleStartPhaseName.Length == 0)
            return;
        if (Game.World.CycleBands.TryNamedPhase(CycleStartPhaseName, CycleStartDay, out float named))
        {
            CycleStartPhase = Mathf.Clamp(named, 0f, 0.999999f);
            return;
        }
        // Console rather than GD.PushWarning, and the reason is a finding: GD's print paths go
        // through the engine's native error handler, which faults with an AccessViolation when
        // there is no engine — and LaunchOptions.Parse IS called with no engine, by the xUnit
        // suite (tests/unit/SailNet.Tests.csproj runs against GodotSharp.dll alone). The existing
        // GD.PushWarning on the --graphics branch a few lines up has the same latent crash and has
        // simply never had a test walk into it. Console.Error is captured in Godot's own stdout, so
        // the message lands in the launch log exactly as it would have.
        System.Console.Error.WriteLine(
            $"[launch] --cycle-start-phase '{CycleStartPhaseName}' is neither a number nor a known " +
            $"phase name; leaving the phase at {CycleStartPhase}. Known names: " +
            string.Join(", ", Game.World.CycleBands.PhaseNames));
        CycleStartPhaseName = "";
    }

    /// <summary>Parses "host" or "host:port". Returns false on empty host or invalid port.</summary>
    public static bool TryParseAddress(string input, out string host, out int port)
    {
        host = "";
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        int colon = trimmed.LastIndexOf(':');
        if (colon < 0)
        {
            host = trimmed;
            return true;
        }

        string portPart = trimmed[(colon + 1)..];
        if (!int.TryParse(portPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
            return false;

        host = trimmed[..colon];
        return host.Length > 0;
    }

    private static string Next(string[] args, ref int i)
    {
        i++;
        return i < args.Length ? args[i] : "";
    }

    /// <summary><c>0|1</c> (also <c>off|on</c>, <c>false|true</c>) for the SICK-1 comfort flags.
    /// An unparseable value returns <paramref name="fallback"/>, which for every one of them is
    /// the shipped default — so <c>--cam-interp</c> with a fat-fingered argument gives the
    /// default build rather than silently the other one.</summary>
    private static bool ParseOnOff(string value, bool fallback) => value.Trim().ToLowerInvariant() switch
    {
        "0" or "off" or "false" or "no" => false,
        "1" or "on" or "true" or "yes" => true,
        _ => fallback,
    };

    /// <summary><c>--seed-test-props</c>'s optional kind field, by NAME (SFX-1). Case- and
    /// whitespace-insensitive, and it fails rather than defaulting — see the parse site for why
    /// a silently-defaulted kind is worse than a dropped prop. Not
    /// <c>Enum.TryParse</c>: that would also accept <c>"3"</c>, which is the ordinal, which is
    /// the wire contract, which is the thing a test script must never restate.</summary>
    private static bool TryParsePropKind(string name, out MpFoundation.Net.PropKind kind)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "crate": kind = MpFoundation.Net.PropKind.Crate; return true;
            case "ball": kind = MpFoundation.Net.PropKind.Ball; return true;
            case "can": kind = MpFoundation.Net.PropKind.Can; return true;
            case "box": kind = MpFoundation.Net.PropKind.Box; return true;
            case "produce": kind = MpFoundation.Net.PropKind.Produce; return true;
            default: kind = MpFoundation.Net.PropKind.Crate; return false;
        }
    }
}
