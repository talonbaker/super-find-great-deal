# MVP scope — what this repository contains, and why

**This repository is the Watis World MVP, extracted from the long-running `Sail` development
repository on 2026-09-02.** It carries only what the current playable build reaches. The Sail
repository is untouched and remains the historical record of everything that is not here.

The extraction was done by an autonomous agent working from the repository itself (the live
scene graph, the launch path, the export presets, the project's own docs) rather than from a
hand-written list. `DECISION-LOG.md` records every non-obvious call made along the way.

---

## 1. What the MVP is

**A private, cross-platform Steam playtest of the "bubble test" level** — Talon's words, 2026-08-30:
*"I need both mac and PC. I need this to work for both for this playtest specifically testing
connection between both platforms."* Success is a Windows host and a Mac client sharing a Steam
lobby and playing together without the session falling apart.

The playable path, exactly as `project.godot` and the code reach it:

```
Boot.tscn
  └─ Splash (studio mark → press start)
       └─ Main menu (scenes/ui/MainMenu.tscn)
            ├─ Host  → LocalServerHost spawns the dedicated server child → Steam lobby + room code
            ├─ Join  → room code → Steam lobby lookup → connect
            └─ Settings / How to play / usage notice / playtest foreword
                 └─ Gameplay.tscn → the "bubbletest" world (scenes/game/world/bubbletest/)
```

In the world: server-authoritative movement with client prediction, proximity voice, networked
carry of the golden cubes, bubbles with a shared counter and a reset lever, TV-portal rooms,
a day/night cycle with a flashlight and night-edge shading, a lake that drowns you, incapacity /
death beat / respawn, four in-game achievements, the pause overlay, telemetry with an opt-out.

## 2. What is IN

| Area | What | Where |
|---|---|---|
| Engine project | Godot 4.7 mono, .NET 8, Forward+ | `project.godot`, `WatisWorld.csproj`, `WatisWorld.sln` |
| Boot + options | scene routing, the command-line flags the kept systems and suites use | `scripts/Boot.cs`, `scripts/LaunchOptions.cs` |
| Netcode | ENet + Steam relay transports, handshake, rate limiting, room codes, snapshots, input ring, prediction/reconciliation, desync monitor, net-sim, client-local dedicated-server hosting | `scripts/net/**` |
| Movement | `AvatarMotor` (deterministic step), `MotorTuning` and its file/knob/invariant surface | `scripts/net/AvatarMotor.cs`, `MotorTuning*.cs` |
| Avatar | `SandboxAvatar` (the networked body), `AvatarVisual` (box-kid and classic greybox bodies, authored clip library, carry/aim/death poses), camera, footsteps, juice | `scripts/game/sandbox/**` |
| Carry | server-arbitrated grab/drop/throw, loose-prop streaming, prop registry | `scripts/game/props/**`, `scripts/game/sandbox/Carry*.cs` |
| Bubble test | the seven authored section scenes, the world seam, bubbles, counter, reset lever, TV portals, night-aid shader global, perf readout | `scenes/game/world/bubbletest/**`, `scripts/game/bubble/**`, `scripts/game/world/bubbletest/**`, `scripts/game/world/TvPortal*.cs` |
| Atmosphere | `OutdoorAtmosphere` (single writer of sky/sun/moon/ambient), `CycleDriver`, sight range and its presentation, flashlight | `scripts/game/world/OutdoorAtmosphere.cs`, `Cycle*.cs`, `scripts/game/sight/**`, `scripts/game/light/Flashlight*.cs` |
| Water | lake contract, drowning clock, chill, splash FX/SFX | `scripts/game/water/**` |
| Failure | incapacitation machine, death beat, respawn service | `scripts/game/failure/**`, `scripts/game/run/DeathBeatPose.cs`, `RespawnService.cs` |
| Run spine | `RunDriver`, `PlaythroughDriver`, `QuotaLedger`, `WorldStateStore` — a round/objective loop the bubble test deliberately idles. **Its original justification is void:** it was kept because canon framed it as "the Pot", and that premise was deleted 2026-09-02. It survives now only as generic round-and-objective scaffolding, and whether it earns its place is an open call for Talon. `RespawnService`, `DrowningClock` and `DeathBeatPose` live in the same folder but are NOT part of that question — they are live movement-adjacent systems | `scripts/game/run/**`, `scripts/game/world/RunDriver*.cs` |
| Achievements | the four in-game achievements and their toast | `scripts/game/achievements/**`, `scripts/ui/achievements/**` |
| Audio | ambient bed, buses, synthesized SFX lab, voice budget | `scripts/game/world/AmbientBed.cs`, `AudioBuses.cs`, `scripts/game/sandbox/SfxLab.cs` |
| Voice | capture, Opus encode, blind relay, positional playback, mute registry, proximity gate | `scripts/voice/**` |
| UI | the design system (tokens, theme factory, recipes, layers), menus, host/join/settings, first-run panels, HUD (bubble count), flow screens, pause, crash report, feedback | `scripts/ui/**`, `scenes/ui/**`, `resources/UITheme.tres` |
| Telemetry | anonymous usage reporting with a durable opt-out | `scripts/telemetry/**` |
| Steam | Steamworks.NET wrapper + native redistributables, lobby, relay peer, App ID resolution | `scripts/net/steam/**`, `thirdparty/steamworks/**` |
| Deploy | Windows / macOS / Linux-server export scripts, SteamPipe templates and upload | `deploy/**`, `export_presets.cfg` |
| Tests | the Godot-free xUnit project; the PowerShell scene suites for the kept systems; the headless self-test scenes | `tests/unit/**`, `tests/Run-*.ps1`, `tests/scenes/**` |
| Docs | canon, the bibles the CLAUDE.md check requires, the UI design system, the state cascade table, the Steam playtest state and checklist, the playtest intake process and Talon's verbatim notes, the bubble-test level docs, the shipped motor's specs | `docs/**` |
| Agent tooling | the rules that bind any agent (scene traps, import/encoding traps, test discipline), the workflow map, the role preambles, the direction / VFX / sound / UI skills | `.claude/**`, `docs/agents/**` |

## 3. What is OUT, and why

### 3.1 Retired content — removed entirely (the brief's explicit list and its family)

| Removed | What it was | Evidence it is dead |
|---|---|---|
| The cabin / house (`House.tscn`, `HouseWorld`, `HouseDoor`, room schedule, cabin asset manifest, wall map) | the sleepover / summer-camp interior | `--world house` only; the premise it belonged to is retired |
| The camp world (`Camp.tscn`, `CampWorld`, `CampTerrain`, camp generator tools, terrain maps, grass, woods LOD, canopy shade, godrays) | the lean-MVP camp greybox, 24.7 MB of the old pack | `--world camp` only; Talon 2026-09-01: "the camera, the godrays, the campfire mechanics can be removed" |
| Campfire / fire / torch mechanics, night pressure, night dome, night lanes, glow sticks | the fire-ward survival loop | named retired by Talon; only reachable in camp |
| The Sasquatch / "watcher", the greybox fauna (wasps, inchworms, fireflies, hive), the electric-eel prey kit | the threats of the previous premises | all three gated OFF behind flags a default launch cannot pass (`Boot.cs`) |
| The Polaroid camera, evidence sites, photo ledger, flash, film | the camcorder/camera verb | named retired by Talon; camp/labs only |
| The butterfly net (capture) | the catch verb | arrives only with `--fauna` |
| Archery (bow, arrows, range targets) | the L6 archery lane | `--world archerylab` only |
| Wallet / quarters / vending machine / payout | the economy of the camp loop | camp/labs only; the HUD profile for the live world already hid it as "a readout of a system this world does not run" |
| Two-slot loadout, tool stance, character face (blend-shape mixer, voice-driven face), camper outfits, fabric/hair caches, the ten camper draft models, the puffling roster row | the previous player bodies and their dressing | the live world uses the box-kid body; `-uffling` and `camper` are retired player nouns |
| Playtest-1 world (the Pot, pockets, hand-authored map) | the 2026-08-22 level | `--world playtest1` only |
| The "dead tree" and environment asset pool (trees, rocks, shrubs, stumps, caves, atlases, `.asset.json` contracts) | the camp's dressing | referenced only by `Camp.tscn` and dev roster labs |
| 36 dev lab scenes and 46 dev scripts (movement playground, atmosphere/fire/fabric/face/roster/water-cue labs, nav spike, capture labs) | tooling for retired or deferred work | `scenes/dev/*`; none reachable from the shipped path. **MOVE-1 brought back exactly one of them, the movement playground and its `scripts/dev/playground/` — see §3.2. The other 35 scenes stayed out.** |
| `GameWorld` (the code-built pastel testbed), `Playground.tscn`, `TestWanderer` | CI scaffolding from the foundation era | the netcode suites run on the default world now |
| `artifacts/`, `spike-out/`, `web/`, `docs/qa`, `docs/superpowers`, `docs/agents` packets/reports/handoffs/archive, `docs/creatures`, `docs/design` (except the four ratified specs listed in §2), `docs/levels` camp docs, the risk audit, `FOUNDATION.md`, the Meridian UI flow HTML | evidence images (458 MB), historical planning | records, not code; they stay in Sail |
| Creature-pipeline skills (`spec-creature`, `spec-variants`, `compile-spec`, `generate-asset`, `validate-asset`, `author-clips`), design-contract skills (`spec-entity`, `spec-family`, `spec-pressure`, `spec-interaction`, `spec-urgency-cue`, `spec-level`), `BLENDER-EXPORT.md`, `ENVIRONMENT-ASSET-CONTRACT.md`, `WORLD-ANOMALY-POCKETS.md` | tooling and contracts for the asset pipeline and for future levels | nothing in the MVP produces or consumes them; re-import from Sail when that pipeline is live again |

### 3.2 Deferred threads — deliberately not brought along (the brief's list)

- ~~**The bike** (`scripts/dev/playground/Bike*.cs`, `BikeParkCourse`, `DescentCourse`, the bike self-test, the two bike branches' handoffs and reports). Lives on `claude/playtest-mvp-steam-review-8zqj6i` and `claude/bike-handling-camera-harness-17bpx2` in Sail; nothing of it is on master.~~ **REVERSED 2026-09-04 by MOVE-1** — the bike is now the game's core hook and Talon is feel-testing it, so it came over from `claude/bike-handling-camera-harness-17bpx2` @ `1b3e9bd7` along with the ride channel in `AvatarVisual`/`RidePose`. See `DECISION-LOG.md`, "2026-09-04 — the lab and the bike come over".
- ~~**The movement lab** (`MovementPlayground`, `MovementPresets`, the courses, the motor tuning panel, `ChargeJumpIntentSource`, `SprintDefaultIntentSource`). The shipped motor and its three ratified specs are in; the exploration harness is not.~~ **REVERSED 2026-09-04 by MOVE-1** — the lab is how the bike is tuned, so it came over with it: `scenes/dev/MovementPlayground.tscn`, all seven courses, both knob panels. It is excluded from the client export presets, so it costs the Steam build nothing. Same DECISION-LOG section.
- **The delivery / courier economy** — not in Sail at all as of this extraction.
- **Narrative / story writing** — there is none. `docs/CANON.md` was stripped to the durable §0 tenets on 2026-09-02; this repo asserts no setting, player noun, story or threat.

### 3.3 Ambiguous calls, resolved in favour of leaving out

See `DECISION-LOG.md` for the full reasoning. In brief: the sight system stays (it drives the night presentation the flashlight answers), the `AimController` stays (the "Tough guy" achievement reads it), the puffling model does not (a retired player noun, and the live world never loads it), and the camp-only test suites went with the camp. The run spine stays for now but on a **lapsed** justification — see its row above.

## 4. Numbers

| | Sail (`master` 9eed513) | This repository |
|---|---|---|
| Tracked files | 6 930 (1 859 `.import` + 750 `.uid` sidecars) | 1 091 (666 without sidecars) |
| Game C# sources | 523 files, 151 100 lines | 291 files, 76 473 lines |
| xUnit test files | 149 | 85 |
| Scene suites (`tests/Run-*.ps1`) | 68 registered | 41 registered |
| Scenes (`.tscn`) | 83 | 36 |
| Working tree (no `.git`) | 617 MB (482 MB of it under `docs/`) | 17 MB |
| Git history | 562 MB | 14 MB, fresh |

Verification on the extraction machine (Linux, Godot 4.7 mono headless, .NET 8.0.130) is
recorded with raw output in `DECISION-LOG.md` §3.

---

## 5. Reversals — what has come back since the extraction, and on whose authority

The extraction was a snapshot of 2026-09-02. Section 3's tables say what was left in `Sail` **on
that day**; they are not a standing ban. When a later packet brings something back, it is recorded
here with the row it reverses and the direction that authorised it, so §3 can still be read as
history rather than being quietly rewritten.

### 5.1 The Watcher — §3.1, reversed by LEVEL-1 (2026-09-04)

| reversed row | §3.1: *"The Sasquatch / 'watcher', the greybox fauna (wasps, inchworms, fireflies, hive), the electric-eel prey kit — the threats of the previous premises"* |
|---|---|
| what came back | `scripts/game/watcher/**` (11 sources) and the two contracts it consumes, `scripts/game/contracts/INightPressure.cs` and `IVisibilityScore.cs`. Verbatim from `Sail` `583ecc02`, one divergence (`NetProfile.WatcherChannel` -> `Watcher.NetChannel`, same value 14). |
| what did NOT come back | The greybox fauna and the electric-eel prey kit. Both remain cut; nothing in the easter-egg wave reaches either. The camp world the creature used to stand in, `WatcherFlag`, `Gameplay.SetUpWatcher` and `tests/Run-WatcherGateTest.ps1` are also still gone. |
| on whose authority | Talon, 2026-08-30 note 9, which became the EGG-2 packet: *"a creepy entity that only appears at night (ties into the existing day-night cycle). Reuse an existing creature already in the project."* The creature is the subject of that direction, so it is not a candidate for substitution. |
| the honest consequence | In the bubble test the creature is **not** flag-gated — `BubbleTestWorld.SetUpWatcher` builds it on every peer and the day/night clock is the only gate. Playing at night now means a creature may appear. `DECISION-LOG.md` §5.3 gives the reasoning. |

### 5.2 `scenes/dev/` and `docs/qa/` — §3.1, partially reversed by LEVEL-1 and MOVE-1 (2026-09-04)

§3.1 cut *"36 dev lab scenes and 46 dev scripts"* and, separately, `docs/qa`. Both rows are now
partly back and the reason is the same in each case: **a packet whose acceptance is a measurement
has to be able to keep the thing it measured.**

- `scenes/dev/KitCourse.tscn` and its generator `tools/dev/kit_course.py` (LEVEL-1). The scene is
  generated, never hand-edited: the generator is the source and re-running it reproduces the
  committed file byte for byte. `tools/dev/` also gains LD-3's six audit scripts and the kit's
  two GDScript self-tests.
- `docs/qa/LEVEL-1/**` — the before/after frames this packet is judged on. `docs/qa/**` is a
  role-wide write grant made 2026-08-29, for this exact reason.
- MOVE-1 answers for `scenes/dev/MovementPlayground.tscn` and `scripts/dev/**` in the same wave;
  see its own report, not this one.

Everything else in those two rows is still in `Sail`.
