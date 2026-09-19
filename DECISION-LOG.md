# Decision log — the Watis World extraction, 2026-09-02

Running record of every non-obvious call made while extracting the MVP from `Sail` into this
repository. Newest entries at the bottom. Written for review after the fact, not for approval
during the run.

Source: `talonbaker/Sail` at `origin/master` = `9eed513` (the 2026-09-01 LAND-1 tip). The two
bike branches were fetched and read for context only.

---

## 1. Method

1. **Trace the live path, do not trust the docs' feature list.** `project.godot` names
   `scenes/Boot.tscn`; `Boot.cs` routes to the splash → main menu → host/join → `Gameplay.tscn`;
   `LaunchOptions.DefaultWorld` is `"bubbletest"`; `Gameplay.BuildWorldScene` is the only world
   switch. Everything else in `scripts/game/world` is reached by a `--world` flag or a gate flag
   a default launch cannot pass.
2. **Type-dependency closure.** A script scanned every `.cs` for declared types and their
   references and walked the closure from the autoloads, `Boot`, and the scripts the live scenes
   attach. Unconstrained, the closure was 471 of 523 files — the hub files (`Gameplay.cs`,
   `SandboxAvatar.cs`, `AvatarVisual.cs`, `LaunchOptions.cs`, `BotHarness.cs`, `PropManager.cs`)
   reference every subsystem. So "orphaned" could not be decided by reachability alone; it was
   decided by *which flag or world* reaches a subsystem, cross-checked against the 2026-09-01
   review's measured bloat map (`docs/agents/2026-09-01-REVIEW-steam-mvp-bloat-and-bike.md` in
   Sail, on the bike branch) and Talon's 2026-09-01 ruling quoted in its CLEAN-1 packet.
3. **Copy additively, then prune to compile.** An include list copied 728 files (plus their
   `.uid`/`.import` sidecars) into this repo. The cut subsystems' files were never copied; the
   hub files were then edited to remove every dead code path, using the compiler as the
   checklist. Deleted, never commented out.
4. **Verify in the engine, not on paper.** `dotnet build`, the full xUnit suite, a headless Godot
   import, the headless self-tests, and a headless pack export were all run here (Linux). The
   PowerShell scene marathon cannot run in this container and is reported as such.

## 2. Calls

### 2.1 Repository identity
- **Project name** `mp-foundation` → `Watis World`; assembly `MpFoundation` → `WatisWorld`;
  export artefacts `mp-foundation.*` → `watis-world.*`. Consequence: `user://` resolves by
  project name, so a fresh profile on every machine — the first-run panels show again and
  telemetry consent defaults to ON until the player opts out (which is the ruled behaviour).
- **C# namespaces are NOT renamed** (`MpFoundation.*`, `Sail.*` stay). Renaming ~500 files'
  namespaces is churn with no behaviour, and `git blame`/diff against Sail stays readable. The
  `RootNamespace` is pinned to `MpFoundation` so new files match.
- **Fresh git history.** The brief allows it and the Sail history is 562 MB, 458 MB of which is
  evidence images. Sail remains the record.
- **Target repo** is `talonbaker/Watis_Game` as instructed in the brief's last line; the brief's
  "GitHub org: Great-Grand-Software" line was not actionable from this session (no such org is
  reachable) — transferring the repository is a one-click GitHub operation for Talon.

### 2.2 The main menu keeps its backdrop, loses its name
`CampfireMenu.tscn` / `MpFoundation.Ui.Campfire` is the live main menu; its backdrop is the
night island with bubbles (no campfire is drawn). Talon's playtest note 5 (2026-08-30) asked for
campfire references to go. Renamed to `MainMenu.tscn` / `MpFoundation.Ui.Menu`; `ScenePaths`
updated. No behaviour change.

### 2.3 The run spine stays although the bubble test idles it
`RunDriver`, `PlaythroughDriver`, `QuotaLedger`, `WorldStateStore` and the flow screens are the
quota loop canon §1.5 reframes as the Pot. `PlaythroughMachine.RunsPlaythrough` already makes
them inert for `bubbletest` (LOSS-1). Cutting them would remove the core loop's only
implementation and its 100+ xUnit pins for a level that will be replaced by one that uses it.
Kept. The wallet/vending/photo *economy* around it is camp-specific and cut.

### 2.4 Sight stays, torches and glow sticks go
`PlayerSightService` is the replicated per-player sight range that `OutdoorAtmosphere` turns
into fog and that the flashlight (NIGHT-2, a live playtest fix) feeds. Its torch / fire-node /
glow-stick inputs were removed; the flashlight and the night curve remain.

### 2.5 Aim stays, archery and polaroid go
`AimController` (RMB hold → raised stance, replicated) is what the "Tough guy" achievement reads.
`AimQuery` (the targeting ray) served only the bow and the camera and is gone.

### 2.6 The avatar bodies
The live world binds the box-kid body (`AvatarVisual.BoxKidWorldId == "bubbletest"`); the classic
greybox is its ancestor and the `--avatar` fallback. The puffling model and the ten camper drafts
are retired player nouns per canon and are not loaded by anything a tester can reach — removed,
including the roster rows that named them. The presentation profile the avatar loads
(`puffling_presentation.tres`) was a generic juice profile with a stale name; renamed
`avatar_presentation.tres`.

### 2.7 Tests that went with their subject
Scene suites removed with the systems they proved: camp world, camp door, campfire, fire node,
fire routing (bow vs camera), wallet, vending, wall map, loadout, house scale, polaroid, flash,
arrow, fauna, watcher (×2), net capture (×2), presentation profiles / face / camper outfits,
blind night, pinch census, style rocks, loop shell, full D-N-D-N loop, world-state store (it ran
on the camp world with wallet seeding; the store itself keeps its xUnit pins), ambient bed (it
loaded a dev lab scene), and the armful-carry pose suite (it ran on the archery lab holding a
camera; the pose keeps its xUnit pin `ArmfulPoseTests`). Capture harnesses (`Run-*Capture.ps1`)
were evidence tooling writing into `docs/qa` and are gone with it.

### 2.8 The `.blend` sources
Godot's importer queries Blender for every tracked `.blend` and, finding none, aborts the whole
import batch silently (measured 2026-09-01). The one `.blend` this repo keeps (`boxkid.blend`)
lives with its build script under `assets/creatures/boxkid/source/` behind a `.gdignore`, so a
machine without Blender imports cleanly and the source is still next to its `.glb`.

### 2.9 Protocol version
Removing prop kinds, input bits, RPC channels and the eel seam changes the wire. `Protocol.Version`
is bumped once so an old Sail build can never handshake with this one.

### 2.10 CI
Sail carried a standing "no GitHub Actions" decision (2026-08). This brief asks for baseline CI at
the agent's judgment. A minimal workflow (`dotnet build` + `dotnet test` on push/PR, no Godot)
is added; it costs nothing and catches the one class of regression a cloud agent can. Delete the
file if the earlier decision still stands.

### 2.11 Things left in that a stricter cut would remove
- `DevScreenshot` (F9) and `ViewportCapture`: an autoload a tester can use to attach a screenshot
  to a bug report, and the capture path `--capture-dir` bots use. Small, and shipped before.
- The direction / VFX / sound / UI skills and their bibles: process tooling with no runtime
  cost; the bubble test's night, audio and UI are exactly what they govern.
- `docs/agents` workflow skeleton (README, orchestrator, templates, role preambles): the process
  Talon dispatches work through. Packets, reports, handoffs and the archive stay in Sail.

### 2.12 The surgery itself
Four agents pruned the hub files in parallel on private copies (Gameplay; the avatar/prop
files; launch options + boot + bot harness; props/sight/HUD/world/net/motor) against one shared
cut list, and the integrator resolved the seams between them with the compiler. Findings that
changed the plan:
- `AimQuery` is still reached by `AvatarMotor` and `SandboxCamera` (the aim ray is the motor's
  facing source, not only archery's) — kept.
- `PropKind` is now `{Crate, Ball}`; `Log`/`Rock` went with the campfire economy they fed.
  Grab-while-holding now refuses with `GrabDenial.HandsFull` (the ordinal the enum already
  reserved), since the two-slot swap is gone.
- `MoveIntent.PolaroidShoot` became the owner-local `Fire` edge (no wire bit); the slot-select
  bits left the wire. `FlashlightProfile` now owns the light-energy constant it borrowed from
  the glow-stick profile.
- The avatar's juice profile (`puffling_presentation.tres`) was a generic footstep/jump/land
  profile with a retired name; it is restored as `assets/creatures/boxkid/boxkid_presentation.tres`
  with the `Sfx` ordinals it serializes now pinned explicitly in the enum.
- The night dome's "escapability contract" (front speed 1.05–1.15× sprint, safe-return radius)
  lived in `CycleBands` + `NightCycleSelfTest` and was already a reported stale fork in Sail
  (the sprint mirror constant was 42% high on purpose). The dome is camp-only and gone, so the
  contract math and its self-test check go too; the band/phase math and its checks stay.
- `IrregularPropShapes` and `PinState` had no consumers left and were deleted.

### 2.13 Unit tests
The xUnit project was pruned by compiling it and deleting the files that failed — by
construction those reference cut types. A first pass over-deleted seventeen pins for KEPT
systems (motor, codec, prediction, world-state store, flashlight, HUD layout, sight) because
Roslyn's phased errors made them look broken; they were restored from the extraction commit
and compile unchanged, except `PlayerSightTests`, which lost its fire/torch/glow-stick cases and
keeps the curve/table/presentation ones.

### 2.14 One process slip, contained
A batch of edits intended for this repository (the scope manifest, the suite registry, three
suite deletions, the rules rewrite, the boxkid source move) was run with the shell's working
directory still inside the read-only Sail checkout. Nothing was committed or pushed there:
Sail's working tree was restored with `git reset --hard` + a targeted `git clean`, verified
clean at `9eed513`, and the same edits were re-applied here. Every later command uses absolute
paths.

### 2.15 Headless import findings
A cold `godot --headless --import` of this repo completes (exit 0) with the same font-ordering
artefact Sail documents (`UITheme.tres` parsed before its `.woff2` fontdata exists — 15 lines,
harmless) and a warm import completes with **0 errors**. No Blender abort: the only `.blend` is
behind a `.gdignore`. Note that deleting `.godot/` also deletes the built assembly, so a rebuild
must precede any engine run after a clean.

### 2.16 The one behaviour fix made on purpose
`SandboxSelfTest`'s `camera_never_crosses_wall` check went red on the extraction while Sail's own
scene passes 169/169 on the same machine. Cause: the self-test now runs the shipped box-kid body
(Sail's ran the retired reference model). A `SpringArm3D` shape cast that starts overlapping a
wall reports no hit, so a thin body pressed against a wall with the arm pointing into it lets
the camera through; the reference model's 0.36 m capsule hid this behind the arm's fixed 0.25 m
cast. `SandboxCamera` now sizes the cast to the followed body's measured capsule (clamped
0.06–0.25 m). This is the only deliberate behaviour change in the extraction; it is a real
defect a tester could hit, and it is recorded here rather than silently.

Bibles applied:  Interaction (the camera is what the player acts through when pushing a wall);
                 Mechanics §boundaries (a rig must not pass through world geometry)
Items checked:   the "camera never crosses / never teleports at a wall" invariants the sandbox
                 self-test already pins, on the shipped body rather than the retired one
Result:          pass — 121/121 after the fix; no other check moved

### 2.17 Input map and leftover assets
`project.godot` loses the `slot_1`/`slot_2`/`slot_next`/`slot_prev` (two-slot loadout),
`glowstick_drop` and `sting` (debug bee sting) actions; `photo_shoot` is renamed `fire` (it is the
owner-local "use held item" edge). The how-to-play glyph rows follow. The paper-UI chrome, the
old flame splash layers, the paper grain overlay, the checker-floor shader (Playground only) and
the iridescent crystal includes had no remaining reference and are removed; the paper *edges*
stay because the theme factory loads them by name.

### 2.18 Last residue pass
Two camp-named unit-test files (`CampLakeBedTests`, `CampTerrainGridTests`) compiled because they
mirror the camp generator's constants locally; their subject is the camp lake bed and the camp
heightfield, so they go. `Run-AvatarIdentityTest.ps1` pinned the retired roster keys on the wire
("gruffling" as the chosen body, "puffling" as the clamp target); it now pins `greybox_classic`
and the clamp target `greybox_primitive` (`AvatarVisual.DefaultAvatarKey`). What remains of the
retired nouns in code is deliberate: identifiers that tests parse as strings
(`IsInCampfireWarmth`, `WaspStingStack`, `TorchHalo`, the `KidQuarters` summary fields), the TV
*cabinet*, Talon's verbatim quotes, and the main menu's off-frame warm glow that its shader still
calls a campfire.

## 3. Numbers — the extraction machine, 2026-09-02

Linux (Ubuntu 24.04 container), .NET SDK 8.0.130, Godot 4.7-stable mono headless, export
templates 4.7.stable.mono. Every line below is the raw output of the named command on the final
tree; the Windows PowerShell scene marathon (`tests/Run-AllTests.ps1`) cannot run here and was
NOT run — its 41 registered suites, including the two re-pointed ones (§2.7 / §2.12), are the
first thing to run on a Windows machine.

```
dotnet build WatisWorld.csproj
    4 Warning(s)     0 Error(s)          (all inherited from Sail: two CS8604 in AvatarVisual,
                                          one CS0618 in AvatarClipDirector, one CS8602)

dotnet test tests/unit/SailNet.Tests.csproj
    Passed!  - Failed: 0, Passed: 1681, Skipped: 0, Total: 1681, Duration: 25 s
    (1731 before §2.18 removed the two camp-named files, 50 cases between them)
    (Sail at 9eed513 on the same machine: Failed: 0, Passed: 3004, Skipped: 4, Total: 3008)

godot --headless --path . --import        cold: exit 0, 15 lines (the UITheme-before-fontdata
                                          ordering artefact Sail documents); warm: exit 0, 0 errors

Headless self-tests, each exit 0:
    --steam-selftest, --reconnect-selftest, --cycle-selftest, --night-cycle-selftest,
    --run-driver-selftest, --flow-selftest, --telemetry-self-test, --voice-mute-selftest,
    --presentation-selftest, --tvportal-selftest, --bubbletest-selftest (collider-audit
    positive control "FAILED AS EXPECTED" present), --screenflow-selftest --world fullhud,
    --hudlayout-selftest --world fullhud,
    tests/scenes/{HostFailure,Incapacitation,NetStep,PropRegistry,Water,WaterFx}SelfTest.tscn,
    scenes/game/sandbox/SandboxSelfTest.tscn  ->  SANDBOX-TEST OVERALL: PASS (121/121)
    (Sail's SandboxSelfTest from a scratch copy on this machine: PASS (169/169) — the 48-check
    difference is the cut subjects: face, polaroid, vending, net capture, stance, loadout)

godot --headless --export-release LinuxServer build/linux-server/watis-world-server.x86_64
    exit 0, 0 ERROR lines, 76 152 312 bytes (embedded pack) + data_WatisWorld_linuxbsd_x86_64/
    (Sail: 68 export errors cold / 5 warm, ~63 of them dangling ragdoll .tres references —
    all of those assets are gone)
```

## 4. Numbers — the first Windows machine, 2026-09-02

Windows 11, .NET SDK 10.0.400, Godot 4.7-stable mono, export templates 4.7.stable.mono already
installed, RTX 4070. This section runs everything §3 could not and reports it raw. Every §3 line
that could be re-measured here **matched**.

**Correction to §3:** it calls the marathon "41 registered suites". The registry
(`tests/Run-AllTests.ps1:54–93`) holds **40**. The 42nd `Run-*.ps1` file,
`Run-VoiceGateBandwidth.ps1`, is a bandwidth measurement tool and is deliberately unregistered —
there is no missing 41st suite.

```
dotnet build WatisWorld.csproj
    4 Warning(s)  0 Error(s)                      (the same four §3 names)

dotnet test tests/unit/SailNet.Tests.csproj
    Passed! - Failed: 0, Passed: 1681, Skipped: 0, Total: 1681, Duration: 3 s

godot --headless --path . --import   (cold)   exit 0, 513 lines, 10 ERROR lines — the
                                              UITheme-before-fontdata ordering artefact only
godot --headless --path . --import   (warm)   exit 0, 16 lines, 0 ERROR, 0 WARNING

powershell -File tests\Run-AllTests.ps1 -MutexTimeoutMinutes 60
    === summary ===  40 suite lines: 35 PASS / 5 FAIL

deploy\Export-WindowsClient.ps1
    exit 0, 0 ERROR lines, 187 MB, steam_api64.dll present, version stamp verified.
    The exported watis-world.exe launches, holds a "Watis World" window, and its own log
    reaches MainMenu._Ready() -> MenuBackdrop.Build() with 0 ERROR lines (tier High,
    Vulkan Forward+). The main menu therefore genuinely constructs and renders.
```

**Both suites §2.7 / §2.12 re-pointed and never ran passed on the first attempt** —
`Authored props: adopted` and `Netcode: arrive latch`. Neither needed the id, position or target
adjustment their headers anticipated: `GoldCube_Hub_0` is at `(10, 0.22, 10)` and ordinal
node-path sort does put it at adopted id 1006, and both latch targets lie inside `GameWorld`'s
64 m slab.

The five reds, each re-run standalone with `-SkipBuild` on an idle machine before being called
anything:

- `Carry: regrab-while-loose` — PASS / FAIL / PASS. Its failing check is the documented staging
  one ("regrab staging broken"); the 0.89 m hold-distance quantity is never reached. **Flake.**
- `Netcode: anti-cheat` — PASS / FAIL / PASS. Failing check is verbatim the documented "the grab
  never landed"; containment itself measured 3.81 m/s peak. **Flake.**
- `Avatar identity: replication` — deterministic. **Extraction residue, fixed.** §2.18 re-pinned
  AvatarBotA and AvatarBotC but missed AvatarBotB, still launching the retired roster key
  `fluffling`, which clamps to the default. Now `boxkid` — a live `BuildRoster()` row, distinct
  from A's key and from the clamp target, so the three-way design is preserved.
- `Flow: playthrough spine` — deterministic. **Extraction residue, fixed.** The boundary-slice
  completeness probe still expected Sail's fourteen slices; only six survive (the other eight went
  with the camp economy of §2.3–2.5, eel-charges with the eel seam). The replacement list is
  derived from `grep -rn 'SliceId =>' scripts/`, which returns exactly those six.
- `Bubbles: shared counter` — deterministic, and **not a defect; deliberately not fixed.** The
  late joiner exits 120 ms before the reset broadcast (C's last sample 1788361393056, A's reset
  1788361393176). Peers A and B both observe the tally go 2 -> 0 and the server broadcasts exactly
  one reset, so replication is sound — `$LateDurationSec = 26` in `tests/Run-BubbleSyncTest.ps1`
  is simply a knife-edge window. Left for the testing pass rather than tuned green here.

A sweep for further residue of that same class found none: every `--world` id the suites pass is
either registered or the deliberate `fullhud` sentinel (any non-`bubbletest` id, which is what
makes `HudProfile` wear the Full profile), and every game flag the suites pass is still parsed by
`LaunchOptions`. The remaining retired nouns in `tests/` are prose or generic English, per §2.18.

**Steam stays on Spacewar (480)** for the playtest — `NetProfile.FallbackSteamAppId`, resolved
through `SteamService.ResolveAppId`'s single path (CLI flag, then Valve's env vars, then the
fallback). No `steam_appid.txt` exists to override it. Unchanged deliberately (Talon, 2026-09-02).

### Still unverified — needs a human at the keyboard

Nothing below can be settled headlessly, and none of it is claimed as done:

- Hosting with Steam running: room code appears, Enter Game loads the bubble test.
- The in-level verbs by hand: move/jump/sprint, carry and throw a cube, pop bubbles and watch the
  counter, use a TV, drown and respawn, toggle the flashlight (F) at night, pause and How To Play.
- **The §2.16 camera change's feel.** Its correctness is already machine-verified — the
  `camera_never_crosses_wall` check lives in `Sandbox: mechanics`, which passes — so what is
  genuinely open is only whether the smaller cast sphere (about 0.135 m on the box-kid body,
  against the old fixed 0.25 m) makes the camera pull in late or abruptly against a wall or in a
  corner. That is a judgement call, not a measurement.

Note for whoever runs those: `--cycle-start-phase midnight --cycle-freeze` puts a hosted session
in permanent night without waiting out the 120 s cycle (`LocalServerHost` forwards both flags to
the child server). Also, `project.godot` declares no `flashlight_toggle` action, so F is
`FlashlightController.FallbackKey` and the How To Play screen correctly shows no Flashlight row —
that is the conditional-row guard working, not a defect.

---

## 2026-09-04 — the lab and the bike come over

**MOVE-1.** The extraction deliberately left the movement lab and the bike in `Sail`
(`MVP-SCOPE.md` §3.2, both rows now struck through). The bike is now the game's core hook and
Talon is actively feel-testing it, so both come over, from `Sail`'s
`claude/bike-handling-camera-harness-17bpx2` @ `1b3e9bd7` (LD-1's metrics gym from `origin/master`
@ `583ecc02`). `Sail` is frozen for new work after its in-flight PRs land; nothing else flows
automatically.

**What came over.** `scenes/dev/MovementPlayground.tscn`; `scripts/dev/MovementPlayground.cs` and
its seven `Bike*` partials; `MotorTuningPanel`, `MotorTuningSession`, `MotorKnobRow`,
`MovementVerbReadout`; the whole of `scripts/dev/playground/` (seven concrete courses —
Calibration, Flow, Scramble, Surf, Descent, Bike Park, Rolling — plus every `Bike*.cs`,
`CalibrationPlan`, `MovementPresets`, the two intent-source decorators and `IridescentProps`); the
ride channel in `AvatarVisual` plus `RidePose.cs`; `MetricBands.cs`; eight test files; three
runners. The lab is excluded from both client export presets, so the Steam build does not carry it.

### Every reference to a cut system that was removed

Each of these is a place where a ported file named something the 2026-09-02 extraction left in
`Sail`. In every case the reference was removed rather than the system re-imported.

1. **`MovementPlayground.cs:810` — `AvatarMotor.GravityFor(..., ballistic: false)`.** The
   `ballistic` parameter belongs to `BlastBallistic` / the eel-blast system, which the extraction
   cut; this repo's `GravityFor` has three parameters. The argument was dropped. The readout's
   answer is unchanged: with no blast system there is no ballistic body to except.
2. **`IridescentProps.CrystalMaterial()` — `res://resources/shaders/iridescent/crystal_facet.gdshader`.**
   The extraction kept `bubble_film` and cut `crystal_facet`. Loading the missing path would log a
   load error at every Scramble summit and hand back a material with a null shader. The method now
   probes with `ResourceLoader.Exists` and falls back to a tinted `StandardMaterial3D` at the same
   silhouette. The shader was **not** re-imported.
3. **`MotorTuningPanel.cs` and `BikeKnobPanel.cs` — the F-key reservation comments.** Both
   recorded "F6 is `CampfireMenu`'s, F8 is `AmbientLab`'s". Both classes were cut. The
   reservations were dropped and the reason recorded in place; F3 (`PerfHud`), F9
   (`DevScreenshot`) and F12 (Steam) survive and are still reserved.
4. **`MotorTuningSessionTests.ThirtyOneRowsArePinned…` → `TwentyNineRows…`.** `MoveSpeed` was
   pinned by `NightPressureTests` and `SprintMultiplier` by `ToolStanceTests`; the extraction cut
   both suites, and `MotorTuningKnobs` already records on each row that the pin "belonged to a
   system that is not part of this build". A pin whose test does not exist is exactly the badge
   this test forbids, so both rows are counted and named as clean. **Measured on this tree: 29
   pinned, 29 clean, 58 rows.**
5. **`MotorTuningSessionTests.APinnedKnobStillMoves…`.** It proved warn-not-lock on `MoveSpeed`,
   which is no longer pinned here (item 4). It now uses `Acceleration`, a real pin on this tree
   held by `LocomotionTests`' sprint-ramp bracket [7.855, 15.75], with an added assertion that the
   row it names is actually pinned so the proof cannot pass for the wrong reason.
6. **`MotorTuningSessionTests.TheInvariantReadout…`.** One legal `Deceleration` breached TWO
   invariants in `Sail`; the second was "EelProfile invariant 1", which `MotorTuningInvariants`
   no longer evaluates here. The count is 1 and the test now asserts the eel invariant is *absent*
   rather than breached.

### One thing put BACK rather than removed

`MotorArc.HeldJog` was pruned by the extraction, along with a doc line rewritten to read "the
since-removed movement playground". `MetricBands.EnvelopeM` needs it for the `HeldJogRange`
envelope, and `docs/levels/METRICS-CARD.md` is generated from that. It is a pure derivation over
numbers already present — no tuning row, no new constant, and `AvatarMotor` does not call
`MotorArc` at all — so it was restored verbatim and the doc line put back to the present tense.
**`AvatarMotor` and `MotorTuning*` were not touched.**

### Numbers, measured 2026-09-04 on this Windows machine

```
dotnet build WatisWorld.sln            Build succeeded, 0 Error(s)
dotnet test tests/unit/SailNet.Tests.csproj
    Passed! - Failed: 0, Passed: 2163, Skipped: 0, Total: 2163

--bike-selftest            this tree  BIKE-SELFTEST OVERALL: PASS (28/28)
                     Sail @ 1b3e9bd7  BIKE-SELFTEST OVERALL: PASS (28/28)
    Every check line is byte-identical between the two trees.

--bike-handling-selftest   this tree  BIKE-HANDLING-SELFTEST OVERALL: PASS (20/20)
                     Sail @ 1b3e9bd7  BIKE-HANDLING-SELFTEST OVERALL: PASS (20/20)
    Byte-identical apart from one telemetry CSV filename (a timestamp).

powershell -File tests\Run-AllTests.ps1 -MutexTimeoutMinutes 120
    === summary ===  43 suite lines: 41 PASS / 2 FAIL
    The two reds are `World: tidal-cycle phase` and `Bubbles: shared counter`. Both were
    discriminated against main @ c16414d by re-running standalone on an idle machine and
    comparing the measured quantity, not the verdict:
      - tidal-cycle: FLAKE on BOTH sides (branch 4 PASS / 3 FAIL, main 5 PASS / 1 FAIL over
        interleaved runs). Divergence 1.52 s against a 1.5 s tolerance on the branch; main's
        own failure is larger at 2.03 s. Already listed load-sensitive in
        .claude/rules/test-suite.md.
      - Bubbles: shared counter: PRE-EXISTING. 3/3 FAIL on this branch AND 3/3 FAIL on main
        @ c16414d, with the identical failing check ("C (late) never saw the reset return the
        tally to 0 and every bubble to visible"). Not this work's; wants its own packet.
    The three new suites - Movement lab: scripted run, Bike: state machine, Bike: handling
    model - all PASS in this run.

docs/levels/METRICS-CARD.md            generated by MetricsCardTests on this tree; BYTE-IDENTICAL
                                       to Sail's card at 583ecc02. No number differs.

deploy\Export-WindowsClient.ps1        exit 0, 0 ERROR lines, 187 MB, steam_api64.dll present,
                                       version stamp verified.
    watis-world.exe                    111,831,216 bytes with scenes/dev/* excluded
                                       111,832,256 bytes with the filter removed (positive control)
                                       "scenes/dev/MovementPlayground" occurs 0 times in the
                                       filtered exe and 1 time in the unfiltered one.
    The exported client boots, holds a "Watis World" window for 30 s, and its own --verbose
    stdout reaches [graphics] tier High -> Splash -> MainMenu -> MenuBackdrop._Ready() ->
    Build() with 0 ERROR lines, Vulkan Forward+ on an RTX 4070. §4's 2026-09-02 record, repeated.
```

The export size is **unchanged at 187 MB** against §4's 2026-09-02 figure: the 1,040-byte
difference the positive control measures is the whole cost of the lab scene, and the filter
removes it. The lab's C# compiles into the assembly either way — only the scene resource is
excluded, which is what "does not grow by the lab scenes" means.
## 5. LEVEL-1 — the LD wave and the easter eggs come over from `Sail` (2026-09-04)

Branch `feat/2026-09-04-level-1-ld-egg-port`, base `main` at `c16414d`. This section records what
the port skipped and why, and the five places where fidelity to `Sail` and this packet's allowed
paths pulled in different directions. The full narrative is
`docs/agents/roles/programming/outbox/2026-09-04-LEVEL-1-report.md`; this is the ledger.

### 5.1 Hunks deliberately SKIPPED, and why

| skipped | source | why |
|---|---|---|
| `scripts/ui/Branding.cs`, `scripts/ui/Splash.cs`, the branding half of `tests/unit/UsageNoticeTests.cs` | EGG-2 `825e1087` | Named out of scope by the packet. `scripts/ui/**` belongs to the UI wave on PR #2. **EGG-2's criterion 10 is UNVERIFIABLE here for exactly this reason** — it is not a failure, it is a hunk this packet was told not to carry. |
| `docs/THRILL-BIBLE.md` | EGG-2 `825e1087` | Named out of scope by the packet. The Cyan dressing EGG-2 built under `/direct` is present; the bible section that directed it is not. |
| `scripts/ui/PauseOverlay.cs` — LD-2's `public static bool IsOpen` | LD-2 `fbeb2a9c` | `scripts/ui/**` is out of scope. Substituted in scope; see §5.2. |
| `scenes/dev/MovementPlayground.tscn` — LD-4's wiring of `SpeedWindLayer` into the playground | LD-4 `6a340ab5` | `scenes/dev/**` other than `KitCourse.tscn` is MOVE-1's, and MOVE-1 is bringing that scene over in the same wave. `SpeedWindCurve`/`SpeedWindLayer`/`SpeedWindTests` are here and green; **nothing in this repo instantiates the layer yet.** Named in the report as a live loose end, not a defect. |
| LD-3's findings (the 96 m Cyan aisle, the stale stepping-stone gaps, the missing weenie link) | LD-3 `33ed5eb3` | Out of scope by the packet: LEVEL-2 acts on them. The measurement is here (`docs/levels/2026-09-04-LEVEL-1-cadence-reaudit.md`); the edit is not. |
| LD-1's metrics gym, `docs/levels/METRICS-CARD.md` | LD-1 `f6f71501` | Not in this packet's seven scope items, and `METRICS-CARD.md` is MOVE-1's by name. |
| LD-5, the Jolt switch | packet `f5053583` | Explicitly out of scope. |
| The EGG addendum §8 "unidentified level piece" | Talon | Still open and still Talon's. Not guessed at. |
| `.claude/rules/test-suite.md` — LD-2's and EGG-1's 42- and 30-line additions | LD-2, EGG-1 | MOVE-1 is editing this file concurrently. Nothing new was measured here that its entries do not already cover, and the standing rule is never to rewrite another agent's entry. Not added. |

### 5.2 Five divergences from `Sail`, each forced and each in-scope

**1. `PauseOverlay.IsOpen` became a node lookup.** LD-2's cadence clock excludes time spent in a
menu (research §A2-R3), and in `Sail` it learns that from a `public static bool` on
`PauseOverlay`. `scripts/ui/**` is out of this packet's scope, so `CadenceTracker` resolves the
overlay node once per scene and reads `.Visible` instead. **It is the same fact:** `Open()` sets
`Visible = true` and the static together, `Close()` clears both, and the two call sites that clear
only the static (`_ExitTree`, `DoLeave`) are the node leaving the tree, which the validity and
`IsInsideTree` checks catch directly. The tree walk runs once per scene, not once per tick, so a
world with no overlay does not pay for the absence. If the UI wave ever adds the static, this is
four lines to delete.

**2. `NetProfile.WatcherChannel` became `Watcher.NetChannel`.** See 5.3 — the creature came back
but `scripts/net/**` is not this packet's. The number is unchanged (14), and `NetProfile`'s own
ladder comment already records 14 as belonging to a cut system and names 17 as the next free
channel, so nothing else can claim it.

**3. `assets/models/objects/Box.glb` became `assets/models/puffinlab/Box.glb`.** The extraction cut
it; EGG-1's `LabRoom.tscn` instances it three times as the lab's crates, so the port does not load
without it. `assets/models/puffinlab/**` is in scope and `assets/models/objects/**` is not, so the
file lands beside the lab's other model and `LabRoom.tscn` — a file this packet owns — points one
path at it. Side benefit: the easter-egg subtree is now self-contained, so a future sweep of the
shared model pool cannot silently break it.

**4. `Gameplay.BuildWorld`'s water line was merged, not copied.** EGG-2 changes
`ActiveLake = LakeForWorld(id)` to `ActiveWaters = WatersForWorld(id)`; the extraction had already
deleted the comment line above it that mentioned the camp. Resolved by hand keeping the
extraction's deletion and EGG-2's code. **The file was patched as a hunk and never copied**, so
PR #2's own `Gameplay.cs` changes are untouched.

**5. `Telemetry.cs`, `Boot.cs` and `LaunchOptions.cs` were patched as hunks for the same reason.**
`Telemetry.cs` carries an extraction-era rename (`CampfireMenu` -> `MainMenu`) that a whole-file
copy from `Sail` would have reverted; `Boot.cs` and `LaunchOptions.cs` carry the extraction's
removal of `--dump-scene`, and both are files PR #2 also edits. Every other file whose Watis
content was proved byte-identical to its `Sail` blob was copied whole; the proof is in the report.

### 5.3 One row of §3.1 is REVERSED, deliberately: the Watcher

`MVP-SCOPE.md` §3.1 cut *"The Sasquatch / 'watcher', the greybox fauna, the electric-eel prey
kit"* as *"the threats of the previous premises"*. EGG-2 is Talon's own 2026-08-30 note 9 — *"a
creepy entity that only appears at night... reuse an existing creature already in the project"* —
and its acceptance criterion 6 is a night-gate suite. The creature is the subject of the criterion,
so the criterion cannot be met without it.

Brought back: `scripts/game/watcher/**` (11 files) and the two contracts it needs,
`scripts/game/contracts/INightPressure.cs` and `IVisibilityScore.cs`, all verbatim from `Sail`
`583ecc02` apart from divergence 2 above. **The fauna and the eel kit stay cut** — nothing in
EGG-2 reaches them.

**Stated plainly rather than softened: in this world the creature is NOT behind a flag.** In
`Sail` the shipped camp built it behind `--watcher-spawns` and Talon wanted it off by default
there. That world, that flag, `WatcherFlag`, `Gameplay.SetUpWatcher` and
`tests/Run-WatcherGateTest.ps1` were ALL cut by the extraction and **none of them came back with
this port** — the only construction site in this repository is `BubbleTestWorld.SetUpWatcher`,
which builds `BubbleTestWatcher` unconditionally on every peer and lets the day/night clock decide
whether anything is ever standing there. That is EGG-2's own deliberate design (its class doc gives
the reason: an easter egg nobody would pass a flag to see, and a flag matched by hand across peers
is a way for peers to disagree). The practical consequence is the honest one: **playing the bubble
test at night now means a creature may appear.** That is the feature Talon asked for; it is not a
flag someone can forget to unset.

This is the only §3.1 row this packet reverses. §3.2's movement-lab row is MOVE-1's to answer for,
not this branch's.
## 2026-09-04 — the game has its name

**BRAND-1.** `Branding.Wordmark` becomes `Watis World` and the splash's `[ STUDIO MARK ]`
placeholder becomes `Great-Grand-Software`. Talon, easter-egg addendum §10 (2026-09-02):
*"update to display the actual title, Watis World"* and *"change to show the studio name,
Great-Grand-Software"*. Nothing else was renamed.

**Why this landed as its own packet.** It is the three-file branding hunk of `Sail`'s EGG-2
(`feat/2026-09-02-egg-2-surprises` @ `825e1087`, against base `583ecc02`). LEVEL-1, which ported
EGG-2 into this repo, was told to skip the hunk because "PR #2 owns `Branding.cs`, `Splash.cs`
and `UsageNoticeTests.cs`". That was wrong: PR #2 (`feat/2026-09-04-entry-goal-line`, `467bbeb`
and `260a5b2`) never touched those files — `git diff --name-only c16414d..260a5b2 --` over the
three returns empty. LEVEL-1 excluded it correctly, PR #2 never had it, and EGG-2 acceptance
criterion 10 was left unmet by anyone. This packet closes it.

**The name-agnostic rule (2026-08-14) is not repealed — it was satisfied.** Its condition was
"until a real title is picked and cleared"; Talon has picked one. `DeadNameAuditTests` is
unmodified and still passes: the dead name is still absent from every UI-facing source, and the
compiled `Branding.Wordmark` is still asserted against it.

### Every reference to a cut system that was removed

Ported comments in `Sail`'s hunk name surfaces the 2026-09-02 extraction cut. In each case the
reference was removed rather than the system re-imported.

1. **`Branding.cs` class summary — "campfire plank sign" in the list of wordmark surfaces.**
   Sail's ported line reads "(splash, title screen, main menu, campfire plank sign, window title,
   crash/feedback dialogs)". The campfire sign and `Campfire.PlankFont` went with the camp
   economy (§2.3–2.5); this repo's pre-port copy had already dropped the sign from that same list.
   The phrase was dropped from the ported line, leaving "(splash, title screen, main menu, window
   title, crash/feedback dialogs)".
2. **`Branding.Wordmark`'s new doc comment — "the campfire plank sign" among the surfaces the
   title is shown on.** Same cut system, same removal: the comment now reads "the splash's
   hand-off, the title screen and the OS window title", which is the true live set
   (`Splash`/`ScenePaths.Title` hand-off, `MainMenu.BuildWordmark`, `Boot`'s
   `DisplayServer.WindowSetTitle`).
3. **The whole 2026-08-14 placeholder paragraph, including this tree's already-softened
   `PlankFont` residue.** Before the port, this repo's copy still explained the placeholder by
   reference to "the since-removed plank font", "that font's documented D/O and V/U glyph
   collisions" and "the old sign's normal path". Sail's hunk deletes that paragraph outright and
   replaces it with the name-landed note, so the residue goes with it. Neither the font nor the
   sign was re-imported.

Nothing was put back. `Splash.cs`'s and `UsageNoticeTests.cs`'s ported comments name only surfaces
this tree has (`Branding.Studio`, `ScenePaths.Title`, the splash's own Label).

**One cut-system reference deliberately NOT touched.** `tests/unit/DeadNameAuditTests.cs`'s
summary still says the wordmark constant is what "(window title, title screen, campfire plank
sign, menus)" read from. That file is the guard this packet is measured against and its passing
unmodified is an acceptance criterion, so it was left exactly as it is. It is pre-existing residue,
not something this port introduced.

### Numbers, measured 2026-09-04 on this Windows machine

- `dotnet build WatisWorld.sln`: **0 Error(s), 4 Warning(s)** (all four pre-existing, in
  `AvatarClipDirector.cs`, `AvatarVisual.cs` ×2 and `SandboxCamera.cs`).
- `dotnet test tests/unit/SailNet.Tests.csproj`, this branch:
  **Failed: 0, Passed: 1681, Skipped: 0, Total: 1681**.
- The same command measured directly on `main` @ `c16414d` in a throwaway detached worktree:
  **Failed: 0, Passed: 1681, Skipped: 0, Total: 1681**. Identical — this packet edits an existing
  test rather than adding one, so no reconciliation is owed. (Measured rather than inherited: a
  figure of 1687 for main's baseline is quoted somewhere in this repo's outbox and is wrong.)
- `powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`, foreground, lock uncontended:
  **40 suites, 39 PASS, 1 FAIL**, `OVERALL: FAIL`. The one red is `Bubbles: shared counter`, which
  is the §4 finding above, still unfixed on `main`: `tests/Run-BubbleSyncTest.ps1` run standalone on
  an idle machine fails identically on `main` @ `c16414d` and on this branch — same failing check
  (`C (late) never saw the reset return the tally to 0 and every bubble to visible`) and the same
  measured quantities (`A 176 / B 176 / C 128` samples, worst bob offset `0.1356 / 0.1356 /
  0.1350 m`). Not a regression; nothing in this packet is on the bubble path.
- Headed capture: `docs/qa/BRAND-1/frame-02.png` shows the splash reading `Great-Grand-Software`
  with the OS window title reading `Watis World (DEBUG)`; `frame-08.png` shows the title screen's
  `Watis World` wordmark.
## 5. FIX-1 — the late joiner's window, 2026-09-04

**The call: the fixture was wrong, not the game — and the cost was not the red, it was the green.**

`tests/Run-BubbleSyncTest.ps1` failed reproducibly on `main` @ `467bbeb`/`c16414d` with exactly one
failure, `C (late) never saw the reset return the tally to 0 and every bubble to visible`. §4 above
already read that correctly as a knife-edge window and deliberately left it. What §4 did not say —
and what FIX-1 exists for — is that **assertion 6's late-joiner half had therefore never executed
once, in any run, ever.** The suite was not reporting a flaky assertion; it was reporting a vacuous
one, and the repository believed a capability was covered that had never been tested.

That capability is the one every shared-state RPC in NET-1's spec is shaped like. `BubbleCounter`
has two distinct late-join paths and they are different mechanisms: the **dump**
(`SendStateTo` → `RpcId(peerId, SyncState)`, `BubbleCounter.cs:203-207`) which this suite has always
proven, and the **broadcast after the dump** (`ServerReset` → `Rpc(ResetBroadcast)`, `:192-198`,
`[Rpc(Authority, Reliable, CallLocal = true)]`) which it had never once observed against a late
joiner. ECON-1's acceptance criterion 5 depends on the second.

**Evidence, confirmed independently on this tree before anything was changed** (unfixed run,
2026-09-04 18:02 UTC, worktree `C:\repos\Watis-fix1`):

- Server log ordering: `2026-09-04T18:02:32.213Z INFO [server] peer left peer=816426601 players=2`
  precedes `[bubbletest] reset lever pulled byPeer=1` and `[bubbletest] reset`. Peer `816426601` is
  the third joiner — bot C. The broadcast was not lost; C was gone when it was addressed.
- Wall-clock, from the three processes' own JSONL (`Wall` is Unix ms, logged for exactly this):
  C's last sample **18:02:32.199Z**; the reset's effect first observable on A **18:02:32.340Z**.
  **C's window closed 141 ms early.**
- Arithmetic: A/B 176 samples over 35.80 s (4.92 Hz), C 128 over 25.80 s (4.96 Hz). Span ratio
  25.80/35.80 = 0.7207 against the typed duration ratio 26/36 = 0.7222. C ran its full 26 s and
  exited exactly on schedule — not crashed, not starved, not disconnected by the server.

**The fix: every bot lifetime is now DERIVED from the reset schedule, and one absolute exit moment
replaces three independent launch clocks.** `BubbleSelfTest` (server only) announces
`[bubbletest] reset schedule: resetAt=… confirmDelay=… resetLandsAt=…` at the frame its clock
starts, `confirmDelay` being `ConfirmDelaySec` = `(BubbleResetConfirm.MinDwellSec +
ConfirmWindowSec)/2` — the constant the fixture already derived rather than typed. The harness reads
`resetLandsAt` off that line, adds `$PostResetSettleSec`, and hands each bot
`session end − its own launch offset` as `--duration`. Moving `--bubble-reset-at`, `MinDwellSec` or
`ConfirmWindowSec` now moves all three sampling windows with it. `$WitnessDurationSec` and
`$LateDurationSec` are gone; bumping `$LateDurationSec` to a larger typed number was rejected as
exactly the failure mode the fixture's own comments warn about.

**A's and B's windows had the same latent defect and it was thinner than it looked.** 36 s typed
against a reset landing at 32.25 s is 3.75 s of margin held by a number that no longer moved when
`ResetAtSec` did. They are derived now too.

**`$PostResetSettleSec = 6` is the one free value, and it is a margin, not a mark** — ~29 post-reset
samples at the measured 4.9 Hz where assertion 8 needs one, chosen large enough that boot skew, a
slow log flush and a late broadcast together cannot eat it.

**Two new controls, per NET-1 §11.2.** A `Wait-ForLogLine` gate on the server's own
`^\[bubbletest\] reset$` before any bot is waited on (rule 1), and **assertion 8**: the late
joiner's last sample must land strictly after the reset's effect first appears on the peers present
throughout (rule 3). Assertion 8 names the HARNESS when it fires, so a closed window can never
again be read as a replication defect. Proved able to fail twice: with the late window shortened
10 s it printed `the late joiner's sampling window CLOSED 2.400s BEFORE the reset…`, and the xUnit
derivation pin fired on a planted literal `--duration 26`.

**The answer FIX-1 was dispatched for: the late-joiner broadcast path is PROVEN.** With the window
spanning the reset by 7.585 s (reset first observed 18:08:02.427Z, C's last sample 18:08:10.012Z),
C observes the tally return to 0 with an empty popped set and every bubble visible, and the suite
passes. No replication defect exists underneath. ECON-1 can be dispatched on a real foundation.

Verified on the branch tip: `Run-BubbleSyncTest.ps1` **3/3 standalone on an idle machine** (margins
+7.499 s, +7.604 s, +7.565 s), `Run-AllTests.ps1` **40 suites, 39 PASS / 1 FAIL** with
`Bubbles: shared counter PASS` inside the marathon for the first time, and
`dotnet test tests/unit/SailNet.Tests.csproj` **Failed: 0, Passed: 1687, Skipped: 0, Total: 1687**.
The one marathon red is `World: tidal-cycle phase`, the documented load-flaky two-client phase
comparison: 5 of 152 pairs at 1.523–2.031 s against a 1.5 s tolerance under load, then PASS / PASS
/ PASS standalone with all 147 pairs inside tolerance every time. Flake, not a regression — nothing
here touches `CycleDriver` or phase replication.

---

## 6. COPY-1 — Talon's playtest copy, and the windowed toggle (2026-09-04)

Three UI changes dictated by Talon in his own words on the day of the live playtest. **His words
were the ruling**: where a comment, a test or a convention disagreed with the copy he gave, the copy
won and the other thing was corrected. Nothing was reworded, re-spelled or re-punctuated.

**The foreword panel.** `A NOTE FOR PLAYTESTERS` became `Watis this game?`, and the body became
continuous prose — so the `Intro` + `Goals` (three Heading/Body pairs) + `Footer` structure it
replaced had nothing left to hold and was collapsed into one `Body` Label. Scrim, header, X, ESC,
`DontShowRow`, the stagger-in and the `UiLayers` rung are untouched. The panel widened 480 → 720 px:
at 480 the new body did not fit 720 p, at 640 it fitted with ~40 px of margin and two one-word
orphan lines, at 720 it fits with ~65 px and no orphans. Width was the free value; no scroll and no
type change were needed, so the letter is read in one screen the way it was written.

**`**anonymous telemetry playtest data**` ships as literal asterisks.** Talon wrote markdown. The
only Godot control that could render it bold is `RichTextLabel`, which is not in
`UiThemeFactory.CoveredTypes` and has no styling in the theme at all — a `RichTextLabel` here would
render in Godot's default font and colour, outside the design system, and adding the type is an edit
to `scripts/ui/design/`, not to this panel. The packet's stated fallback was taken: the characters
ship as written.

**`TIDE` was live in shipped UI.** The panel's old `Intro` opened *"TIDE is an early multiplayer
playtest…"* — the mockup name `Branding.cs` documents as retired for legal reasons — and it is
visible in the pre-change capture. Talon's rewrite removes it as a side effect.
`DeadNameAuditTests` did not catch it because it sweeps **source**, not `.tscn` scene text. Reported,
not fixed here; extending that audit to scene files is cheap and is a separate call.

**GOOD TO KNOW.** Five agent-written lines replaced by Talon's three, verbatim, "breath" included.
The file's own doc comment forbade copy naming a mechanic the world lacks; it now records that rule
as **suspended for copy Talon authored himself**, dated. His third line's "bubble death counter" is
not an unbuilt promise — Talon, 2026-09-04: *"About the 'death counter' I'm making a joke about the
HUD bubble counter. Just a joke you know."* The referent is the shipping `HudBubbleCount`. Nothing
was built and the line is unchanged.

**The display toggle is inverted at the BINDING, not at the setting.** `FULLSCREEN` became
`WINDOWED`; the row's label, its initial `ButtonPressed` and its `Toggled` handler each negate, and
`DisplaySettings.Fullscreen` / `SetFullscreen` keep their meaning everywhere else — Boot's
real-client branch, `settings.cfg` and the export all still read "fullscreen" as fullscreen. The
node names stay `FullscreenRow`/`FullscreenToggle` because they name the setting, which did not
change. A test fails if anyone ever "simplifies" this by inverting `DisplaySettings` itself.

**Talon's reported mismatch reproduced, and it was three rows, not one.** Against the real profile
(`fullscreen=true`, no opt-out marker) the unfixed panel drew the FULLSCREEN pill **unchecked** over
a game whose window mode was fullscreen — and drew ANONYMOUS USAGE REPORT **unchecked** over a
session that was in fact reporting. Cause: Godot's `BaseButton::set_pressed` returns immediately
when `toggle_mode` is false, and `PillToggle` sets `ToggleMode` in `_Ready` — which has not run when
an object initializer executes, so `ButtonPressed = x` in a code-built pill was silently dropped.
All three code-built pills in `SettingsPanel` had it; the `.tscn`-instantiated one never did, because
a child's `_Ready` runs before its parent's. Fixed by assigning `ToggleMode = true` before
`ButtonPressed`, and pinned structurally so a fourth pill cannot reintroduce it. The usage-report row
is the one that mattered: its own comment argued that an OFF pill over a reporting session would be
"a settings screen lying about its own setting", and this trap made it do exactly that to every
player who had never touched it.

**Evidence.** Six headed frames in `docs/qa/COPY-1/`, and the window-mode round trip measured as
rects rather than claimed: fullscreen `0,0–3440x1442` → check WINDOWED → `1071,292` `1298x767` with
`settings.cfg` `fullscreen=true → false`; relaunch boots windowed with the pill checked; uncheck
returns to `0,0–3440x1442` and `fullscreen=true`. The shared `user://` profile was backed up before
the run and verified byte-identical to that backup afterwards.

**Two `GoalCopyTests` assertions were written against agent copy and became assertions against
Talon's.** `HowToPlay_OpensWithTheSameGoalTheToastStates` pinned HOW TO PLAY's first line to the
entry toast; his three lines do not restate the objective. `TheGoalDoesNotInventAFailState` required
the phrase "no way to lose"; his copy says water and bubbles "will kill you". Both were rewritten
rather than deleted, each carrying why: the objective and the absence of a real fail state are still
held, the COUPLING between the two surfaces is not. `ForewordCopyTests` now pins his words
character-for-character, blank lines included, and was proved able to fail by planting "pseudo" for
"psydo", "breathe" for "breath" and an un-negated `ButtonPressed` in the real files — six assertions
fired, and the pill sweep caught a scanner bug in itself on its first run (a comment naming
`ButtonPressed` read as an assignment to it).
## 6. SHADER-1 — the bubbles that vanished over the water, 2026-09-04

Talon, live in a playtest: *"The bubble shaders and the water shader seem to be fighting at times.
The bubbles image when they're overlapping the water is not visible."*

**Reproduced before anything was changed.** `docs/qa/SHADER-1/before/lake-se` (camera `-83,9,13`
looking at `-102,0.5,-3`, noon, green's lake): two bubbles are each drawn in full where their
silhouette crosses the bank and stop dead along a line that is exactly the shoreline — one keeps
only the cap that overlaps grass, the one below it only the belly that overlaps rock. Over water
there is a trace of rim and nothing else.

**Mechanism — the transparent SORT, not depth and not alpha.**
`resources/materials/bubbletest/bubble_film.tres` and `lake_murk.tres` were both
`render_priority = 0`, so the alpha queue fell through to depth; a `MeshInstance3D`'s sorting
point is its AABB centre by default (`GeometryInstance3D.sorting_use_aabb_center`), and green's
lake is ONE 33.9 m mesh, so the whole surface sorts at a single point in the middle of itself
while the bubbles are spread all over it. Every bubble further from the camera than that one
point was drawn BEFORE the water and the water then blended over it. Neither surface writes
depth — `bubble_film.gdshader:2` is `depth_draw_never`, `lake_murk.gdshader:212` is
`depth_draw_opaque`, which on a transparent material means it does not write depth either — so
nothing was ever depth-culled and blend order was the only thing deciding the outcome. The film's
`alpha_center` is `0.06`, so 6 % coverage under a near-opaque murk composite is nothing a player
can see; that is why the residue is a faint rim rather than a dimmer bubble.

**The one render value changed, and how to revert it knowingly:**

| file | field | was | now |
|---|---|---|---|
| `resources/materials/bubbletest/bubble_film.tres` | `render_priority` | `0` | `1` |

Nothing else — no shader source, no scene, no C#, no other material. Set it back to `0` and the
old behaviour returns exactly.

**Why the bubble and not the water.** `render_priority = -1` on `lake_murk.tres` fixes the same
frame, but that material is also `GreenHills.tscn`'s `SunkenTvPlinth/Column`, which is genuinely
under the surface and must stay murked; pushing the water earlier would leave the plinth
compositing on top of the lake it is sunk in. `bubble_film.tres` is loaded by exactly one scene
(`scenes/game/props/Bubble.tscn`) and by nothing else — `IridescentProps` loads the *shader*
directly and never this material — so the blast radius is the bubbles and only the bubbles.
`BubbleCounter` hands every bubble a `Duplicate()` of the authored material, and `Duplicate()`
carries `render_priority`, so one file covers all 112.

**What it costs.** A bubble now composites over the lake even where it is slightly *under* the
surface — `Bubble_Green_16` sits at y −0.65 against `WaterGeometry.WaterY` −0.58, i.e. 7 cm down —
so that one loses its murk tint. Accepted: bubbles are the level's whole objective and visibility
beats subtlety. It costs nothing in occlusion: `render_priority` only reorders inside the
transparent queue, the depth TEST is untouched, and opaque geometry still writes depth, so a
bubble behind a wall is still behind the wall. `SecretBubble` builds its own `StandardMaterial3D`
in code and was deliberately left alone — it hangs 0.77 m under the surface and is meant to read
as a light UNDER the water.

**Measured, not eyeballed.** Per-pixel before/after difference over ten framings
(`docs/qa/SHADER-1/README.md`): the four lake shots move (1.20–3.86 % of pixels), the five
neighbours — the blue moat, the murk read with the sunken television, the `SecretBubble` at
midnight, the golden cubes, television room A — move 0.00–0.13 %, and that residue is the
bubbles' own time-driven `BubbleOscillation` wobble, which never lands on the identical phase
twice. The moat frame differs by 20 pixels out of 921 600.
## 6. HONK-1 — the goose honk, for the mic-less and the shy (2026-09-04)

Talon, wrapping up a live playtest: *"Please add a simple 'sound' to the playtest which will act
like a voice input from players who don't have microphones or don't want to speak. This should
sound like a goose honking and the button which activates this honk should be mapped to 'H' key."*
And in the foreword shipping beside it: *"For the mic-less or shy, pressing 'H' will act as a kind
of pseudo-proximity voice for testing purposes."*

The second sentence is the specification. A honk only its presser can hear is worthless; the point
is that **other players** hear it, positionally, where they would have heard that player's voice.
So it is a replicated verb, not a local sound effect.

### 6.1 The direction calls

- **An EVENT, not an audio stream.** The wire carries a payload-free request and a one-int
  broadcast; every receiver synthesises the same waveform locally. Host upstream at the six-player
  cap is ~1.5 Mbps, two thirds of it voice (2026-08-07 audit) — a honk had to cost nothing beside
  that, and at 1.67 messages per player per second at most, it does.
- **The cooldown is the SERVER's; earshot is the RECEIVING CLIENT's.** The cooldown is an
  authority question. Earshot is not: a client deciding what *it* hears cannot make anybody else
  hear anything, and it already holds both inputs. Putting earshot on the client also keeps this
  packet out of an architectural fork that is not its to take — `VoiceProximityGate`'s doc records
  that a server which inspects positions to route audio reverses a deliberate stance, and that
  flipping it is *"Talon's call, not an agent's."* Nothing here needed that stance: the bandwidth
  argument that motivated the voice gate is absent at a honk's message rate.
- **The earshot test exists to be WITNESSED, not to change the sound.** The receiving
  `AudioStreamPlayer3D` already cuts off at the same distance. What one `if` in C# buys is that
  "the far player did not hear it" becomes a countable fact on a headless peer instead of an
  inaudible one. It is what makes the absence check in `tests/Run-HonkTest.ps1` mean anything.
- **No local prediction.** A press produces nothing until the server answers
  (`FlashlightManager.ClientRequestToggle`'s stance). Playing locally on press would let a player
  hear a honk the cooldown refused and nobody else received — the worst feedback a social verb can
  give, because it teaches them the button worked when it did not.
- **No late-join dump, deliberately.** Every other replicated manager here has one. A honk is
  transient: a honk that happened before you joined is a honk you correctly did not hear.
- **Synthesised, not sampled.** `SfxLab` already synthesises the whole palette; a committed
  `.wav`/`.ogg` would need a licence story Talon has not supplied.
- **No protocol bump.** `NetProfile.ProtocolVersion` stays at **14**. This is a new node with new
  methods and no change to the shape of any existing message, and the mismatched-build failure is
  fail-*silent* — a peer with no `HonkManager` plays no honk, which is yesterday's state rather
  than a wrong one. Same reasoning `SightChannel` and `FlashlightChannel` already carry. The bump
  for this wave belongs to NET-1's crew-state wire change.

### 6.2 The values Talon can retune by hand

Every number below was picked rather than escalated. The two range numbers are **aliases**, not
values — a honk carries exactly as far as a voice, and retuning voice moves the honk with it.

| Knob | Value | Where | Why this number |
|---|---|---|---|
| Key | `H` (physical) | `HonkController.FallbackKey` | Talon named it. Verified free — see 6.3. |
| Gamepad | D-pad up | `HonkController.FallbackButton` | Both sticks are taken (walk, flashlight); a "shout" on the D-pad is the squad-shooter convention. |
| Cooldown | **0.6 s** | `HonkConfig.CooldownSec` | The call is 0.42 s, so anything shorter lets one player's honks overlap themselves and read as a stuck buzzer. The extra 0.18 s makes a held key sound like deliberate repeated honking (about 1.7/s) rather than a machine gun. Deliberately not a punishment — honk-honk-honk is the joke. |
| Server jitter tolerance | **0.05 s** | `HonkConfig.ServerJitterToleranceSec` | The server judges presses on ARRIVAL, so the gap it sees is the cooldown plus a jitter delta that is negative half the time. With both latches equal, a held key loses roughly half its honks at random. The authoritative latch is therefore the looser of the pair by 50 ms. Costs a flooder 1.82 honks/s instead of 1.67 — a 9% loosening to remove a 50%-loss bug from every honest player. |
| Audible range | 24 m (alias) | `HonkConfig.AudibleRangeM` to `VoiceConfig.ProximityMaxDistance` | Voice's number, referenced not copied. |
| Falloff curve | 6 m (alias) | `HonkConfig.UnitSizeM` to `VoiceConfig.ProximityUnitSize` | Same. |
| Volume | -6 dB | `HonkConfig.VolumeDb` | Level with the pooled one-shot default. A voice substitute louder than a voice is a griefing tool. |
| Pitch jitter | 4% | `HonkConfig.PitchJitter` | Half the pool's footstep jitter: enough that two honks are not byte-identical, little enough that it stays the same animal. |
| Channel | 17 | `NetProfile.HonkChannel` | Next free. Not `VoiceChannel` — that one is unreliable by construction and carries 50 Hz per talker. |

The sound's own knobs, all in `SfxLab.GooseHonkPcm` beside the recipe:

| Knob | Value | Why |
|---|---|---|
| Length | 0.42 s | Long enough to be a goose rather than a duck (about 0.15 s); short enough that two players over each other stay legible. |
| Pitch arc | 300 - 470 - 250 Hz | **The one thing that makes it a goose and not a car horn.** A horn holds one pitch flat. The goose rises fast into a shout and falls away from it: "ah-RONK". |
| Rise ends / fall begins | u = 0.14 / 0.42 | The fall is longer than the rise. Reversed, it is "RONK-ah", which is not a goose. |
| Bleat | 38 Hz, 2.5% | A goose's syrinx is a reed, not a tone generator. Without the wobble the harmonic stack is a kazoo. |
| Harmonics | 7, 1/n, darkening to 0.35x | A sawtooth's recipe: the brassy body lives in harmonics 2-6. The stack darkens across the call — the open "aa" closing into "onk". An unchanging timbre reads as an instrument. |
| Breath | 0.18, fading | A goose moves air. |
| Attack | u = 0.03 (about 13 ms) | A honk arrives; it does not fade in. Not instant, which clicks. |
| Gain | 0.80 | Measured peak about 0.5, so `Render`'s +/-1 clamp never hard-clips. Pinned by `HonkTests.GooseHonk_NeverClips`. |

Measured pitch arc on this tip (three-pole-lowpassed zero-crossing estimate): **opening 411 Hz,
shout 476 Hz, tail 406 Hz**. `HonkTests.GooseHonk_RisesIntoTheShoutAndFallsAwayFromIt` asserts the
shape with a 5% margin, not those numbers, so a retune stays green and a flattened arc does not.

### 6.3 `H` is free — measured on this tip, not assumed

Talon's shipped foreword names `H`, so this had to be checked. Three places can claim a key:

1. **`project.godot`'s `[input]` block**, by physical keycode: `move_forward` 87 **W**,
   `move_back` 83 **S**, `move_left` 65 **A**, `move_right` 68 **D**, `pause` 4194305 **Escape**,
   `jump` 32 **Space**, `interact` 69 **E**, `throw` 81 **Q**, `voice_ptt` 86 **V**, `sprint`
   4194325 **Shift**, plus the mouse/stick-only look/aim/fire actions. **72 (H) is not in it.**
2. **Runtime-declared actions** — a repo-wide search for `InputMap.AddAction` returns exactly two
   call sites: `flashlight_toggle` on **F** and `walk` on **Left Ctrl**.
3. **Raw key polling**, which bypasses the action map: the only `Input.IsPhysicalKeyPressed` calls
   in `scripts/` are `BikeLayer`'s three, and one of them **is `Key.H`** (`RampKey`). **Not a
   collision:** `BikeLayer` is constructed only by `MovementPlayground`, which is
   `scenes/dev/MovementPlayground.tscn` — a separate scene launched on its own, never a child of
   `Gameplay.tscn`, and one `HonkController` is never attached to. The two verbs cannot be alive in
   the same process. Recorded because a future packet that networks the bike **would** collide.

The packet named `LocalInputIntentSource.WalkFallbackKey`'s doc as the home of this list; on this
tip that doc has been trimmed to one sentence and no longer enumerates. The list above is the
measurement it used to carry.

### 6.4 Five things measured the hard way

- **A client's `Multiplayer.GetPeers()` returns only the server.** The forgery probe's first
  spelling enumerated it, attempted **zero** forgeries, and the suite passed green while
  `RpcMode.Authority` was deliberately downgraded to `AnyPeer` to prove the check could fail. The
  probe now reads the replicated player list (`VoiceManager.GetRemotePlayers`) — which is also the
  more honest attacker model — and the suite asserts the attempt count off the forger's own log
  before believing the absence.
- **The client-side cooldown copy hides the server's own latch.** Seven scripted presses produced
  two honks and the server logged nothing at all, because the local copy suppressed the extras
  before a packet left the machine. So the shipped path proves the *courtesy* latch and the
  *authoritative* one had never run once. `--honk-flood` (a client with the local check deleted,
  the `--voice-flood` precedent) is what exercises it: **290 raw requests over 2.0 s produced 6
  honks**, and the server logged its drops.
- **`(10.0 + 0.6) - 10.0` is `0.5999999999999996`.** The obvious `now - last < Cooldown` spelling
  refuses a press at exactly the cooldown. `HonkGate` stores the NEXT allowed time instead, which
  makes the bound exact rather than aspirational. Found by the boundary test, not by reasoning.

- **A client-side latch and a server-side latch set to the same number fight each other.** Caught
  in review, not by a test. The client spaces its sends at exactly the cooldown; the server judges
  them on arrival, so the gap it sees is `Cooldown + (jitter2 - jitter1)` — negative half the time
  on any real connection. Every "early" arrival is refused, and because a refusal does not advance
  the latch, the next honk lands a *full* cooldown after the last granted one. **A player holding
  `H` would have honked at about half the intended cadence, at random.** That is precisely the
  swallowed press `HonkGate`'s boundary note refuses to accept, reintroduced by the clock split
  rather than by the bound. Fixed by making the authoritative latch the looser of the pair
  (`ServerJitterToleranceSec`), which is the ordinary shape of a client-predicted, server-checked
  rule.
- **A counter called "heard" was counting honks that never played.** `PlayHonk` incremented
  `_heard` after the earshot test, but the earshot test fails *open* on an unresolvable avatar —
  and with no source position there is nothing to play. A transient spawn race on the far peer
  would therefore have produced a suite red blaming the range rule for something that was never a
  range problem. Split: an unresolvable **honker** now drops the honk and counts nothing (there is
  no safe-loud answer to "where is it"), while an unresolvable **listener** still fails open (there
  is one to "how far is it").


## 6. SHADER-2 — the boxy shape under the sunken television, 2026-09-04

Talon, live in a playtest: *"There is a strange shader issue happening below the TV. The TV itself
is murky very nice and below this there is a kind of strange boxy shape that it looks like the
shader is doing something strange, please just look into this, but it's no big deal."*

**Reproduced before anything was changed.** `docs/qa/SHADER-2/before/bank-eye` (camera
`-95, 0.15, 16.5` looking at `-95, -0.45, 10.65`, noon, `--cycle-freeze`): a hard-edged black
rectangle at screen x 579..701 with its top edge at y ≈ 527, sitting in an otherwise soft
grey-olive murk. That is `Run-Shader1Capture.ps1`'s `sunken` framing verbatim, so SHADER-1's own
already-committed frame shows the same artifact — the reproduction does not depend on this
packet's harness. Projecting the authored geometry through the capture camera (a bare `Camera3D`,
so Godot's default 75° vertical FOV) puts `SunkenTvPlinth/Column` at x **579..701** and the Cap's
lower edge at y **527**: it is the plinth column, to the pixel, and it is not the television.

**Mechanism — a WATER SURFACE MATERIAL ON A SOLID BOX. Geometry, not sorting and not depth.**
`GreenHills.tscn`'s `SunkenTvPlinth/Column/Mesh` is a solid 1.4 × 2.4 × 1.4 `BoxMesh` and was
authored with `lake_murk.tres` — the lake *surface* material — as its `material_override`, to get
a "murk-toned" column. `lake_murk.gdshader:212` is
`render_mode blend_mix, depth_draw_opaque, cull_disabled`: on a transparent material
`depth_draw_opaque` writes no depth (the shader's own header says so at `:196-198`), and
`cull_disabled` draws both sides, so the box's far faces are composited over its near faces with
nothing to sort them. Those far faces are `!FRONT_FACING`, which takes the shader's "seen from
underneath" branch (`:1296-1302`) — `ALBEDO = deep_col` (0.0115, 0.0225, 0.0195) at `under_alpha`
0.94, a near-opaque near-black ceiling that is the honest answer for a submerged camera looking up
at a lake and a black rectangle on a box. `:1103` compounds it by forcing every fragment's normal
straight up (`n_world = normalize(vec3(slope.x, 1.0, slope.y))`, then `:1107` flips it straight
down on every far face), so the box's vertical faces carry
no shading gradient and the silhouette reads as a hole cut in the scene.

Measured, from the repro frame: the water around the box is linear ≈ (0.038, 0.038, 0.033), the
`silt_col` tint; the box is linear ≈ (0.0103, 0.0120, 0.0103) — green-dominant, an order of
magnitude darker, matching `deep_col` at 0.94 opacity. The box was on the underside branch and the
water was not.

**This repo had already written the diagnosis down, for a different box, the same day.**
`BluePrecision.tscn:100-104`, on the moat surface: *"A PlaneMesh, NOT a BoxMesh… a water shader
shading the inside of a box over the top of itself renders the whole basin as a flat black
rectangle. It looked exactly like 'the murk is working', which is why it survived one capture."*
Same packet (EGG-2), same day, same mechanism. The moat got a `PlaneMesh`; the plinth column
stayed a `BoxMesh` and kept the water material.

**The one render value changed, and how to revert it knowingly:**

| file | node | field | was | now |
|---|---|---|---|---|
| `scenes/game/world/bubbletest/sections/GreenHills.tscn` | `SunkenTvPlinth/Column/Mesh` | `material_override` | `ExtResource("4_gvttj")` = `lake_murk.tres` | `ExtResource("6_ddkf1")` = `green_neutral.tres` |

One token. Put `4_gvttj` back and the old behaviour returns exactly. No shader source, no
material file, no C#, no other node, no new asset.

**Why `green_neutral.tres`.** It is already an `ext_resource` in this scene — the Cap's own
material and nine other GreenHills props — so the diff is one identifier with no new resource
reference, and it keeps the section's palette. `tangle_rock_dark.tres` (albedo 0.184 vs 0.227) is
the darker alternative and honours the authored comment's "murk-toned column" more literally, but
it belongs to another section and would be the level's first cross-section material borrow. One
identifier to flip if the darker stone is wanted.

**What it costs: nothing that was working.** The murk the column was reaching for it now actually
has — the lake surface composites over it exactly as it composites over the television, which is
the read Talon named as the thing he likes. Nothing moved: the plinth's `StaticBody3D`, its
`CollisionShape3D` and the television's walk-in trigger are bit-identical, so what a player can
swim to and stand on is unchanged, and `BubbleTestSelfTest`'s packed-vs-live mesh/shape/body counts
for GreenHills are unchanged by construction.

**One knock-on to SHADER-1's reasoning, recorded so it is not a silent drift.** SHADER-1 ruled out
`render_priority = -1` on `lake_murk.tres` partly because that material was *also* this plinth
column, which had to stay murked. It is not any more: after this change exactly two nodes in the
level use `lake_murk.tres`, and both are genuine water surfaces (`GreenHills` `Lake/Surface`,
`BluePrecision` `Moat/Surface`). **SHADER-1's fix is unaffected and stays** — `render_priority = 1`
on `bubble_film.tres` is byte-identical to base and is still the right side of that fork for its
own reason (blast radius: one scene) — but the alternative it ruled out is now less blocked than it
was, which is a fact for whoever next opens that question rather than an invitation.

**Measured, not eyeballed.** Seven framings, each shot twice from the same camera at the same
frozen phase at the same capture second, across the one changed field
(`docs/qa/SHADER-2/README.md`). **97–99 % of every moving pixel falls inside the plinth's own
projected silhouette**; the residue outside it is `BubbleOscillation`'s time-driven wobble and the
water's own sun glitter, the same residue `docs/qa/SHADER-1/README.md` measured on the same lake.
Over the television's cabinet — the regression that would have mattered — the maximum
single-channel difference is **1/255** in the two closest framings. At midnight the plinth's whole
silhouette moves by at most 6/255 and no pixel crosses 8: the artifact was a day artifact, because
nothing down there is above black at night.
## 7. CELEBRATE-1 — the level notices when the last bubble goes (2026-09-04)

Talon, wrapping the playtest: *"when the player collects all the bubbles, when they get 100 out of
100, I would like a small sound to play like a triumph sound and please add one more steam
achievement to the game they can earn and maybe do something also special to let them know they're
special."*

**The achievement half is not in this packet** and no `AchievementId` was added. An all-bubbles
achievement already exists (`Bubblholic`, via `BubblholicRule.AllPopped`), so "one more" needs a
different trigger, and picking that trigger is Talon's. **Steam is not wired at all** — see the
report's *The Steam gap* section for what it would actually take.

Everything below is a value pick, recorded so it can be retuned or reverted by hand without
reading any code.

### The shape of the beat

| Pick | Value | Why this one |
|---|---|---|
| Channels | sound **+** sparkles **+** one line of text | INTERACTION-BIBLE §8.1 scope is `all_players` (the tally is shared and server-adjudicated, so completion is a fact about the session). §8.2 then says an `all_players` consequence carried only by audio is invisible to a player whose audio this game deliberately severs. Any one of the three carries the event alone. |
| Total length | **1.6 s** (`BubbleCelebration.TotalSeconds`) | The standing constraint is Talon's own reverted-juice history. The beat is bounded before it is designed. |
| Camera / input / pause | **untouched** | No screen effect, no forced camera, no freeze, nothing covering the play area. The suite asserts the player kept moving and honking straight through it, and greps the source for every input/pause/focus API. |
| The sound | a four-note **G-major arpeggio to the octave**, glass timbre, 1.15 s (`SfxLab.TriumphPcm`) | Not a fanfare (brass, long, you stop playing to listen); not a coin pickup (one note, no tail, reads as another tick of the counter the player has heard 111 times). Four notes climbing to the octave is the smallest shape that reads as an *arrival*. Glass rather than brass because the level is made of soap film. |
| Where the sound plays | **non-positional** (`SfxLab.PlayUi`) | There is no place in the world where completing the level happened. A 3D triumph would be quieter for whoever stood furthest from the last bubble — backwards for a shared achievement — and would make "who popped it" audible, which is not a distinction the tally makes. |
| The sparkles | one 20-particle, 0.065 m, 0.75 s burst **at every body in view** | Twice the pop's own burst (10 at 0.05 m) and no more, so the completion reads as a different event from the 111 pops before it. Everyone sparkles because it was everyone's tally. Sized by capture, not by guess: 14 at 0.045 m was a smudge (see `docs/qa/CELEBRATE-1/`). **The two knobs are `BubbleCelebration.PuffCount` and `PuffSizeM`.** |
| The line | *"That's every bubble in the world."* (`PhaseToastText.AllBubblesToast`) | The other end of the on-entry goal line. Warm, short, no exclamation mark — a line that shouts is the text equivalent of the juice Talon reverts. **Names no number**: the HUD is already showing it, and a count in a UI string is a copy that a bubble-count change has to remember to update. Asserts no premise. |

### The mechanism

- **The completion is one payload-free broadcast from the server**, sent from `ServerPop` on the
  same transfer channel as the pop that caused it. Same channel is the whole design: Godot orders
  reliable RPCs per channel, so every receiver applies the final pop *before* it hears the triumph.
  A channel of its own (HONK-1's shape, the obvious precedent for a payload-free event) would have
  bought head-of-line isolation and paid for it with a client celebrating at 111 of 112.
- **`ProtocolVersion` stays 14.** Nothing about the wire format, the handshake or the snapshot
  codec moves.
- **The predicate is `BubblholicRule.AllPopped`, reused rather than respelled**, so the sound and
  the achievement can never disagree about what completing the level means — and the level's
  `total == 0` guard (the CI scaffolding worlds carry no bubbles) comes with it.
- **Idempotency is a server-side latch**, cleared by `ServerReset` and nothing else. On the server
  rather than on each receiver deliberately: "is this count new?" is a question four machines can
  answer four ways, and a late joiner's first sync would answer it wrongly every time. A player who
  joins a finished level hears nothing; a reset then a recompletion sounds again.

### The bot gate

`user://` resolves by project name, so every checkout on this machine shares one profile, and a
suite bot has already spent a real achievement into Talon's. Bubble suites pop bubbles.

- **The existing `CheckBubblholic` gate was already closed** and was found so, not fixed:
  `SandboxAvatar.ConfigureAsNetworked` only builds an `AchievementRuntime` when
  `source?.IsHumanInput == true`, so no bot, headless server, capture run or self-test has one to
  call `CheckBubblholic` on. Nothing about that was changed.
- **The celebration asks the same question through the same seam** —
  `SandboxAvatar.IsHumanDriven`, which is `IIntentSource.IsHumanInput` and nothing else, hoisted to
  a public read so the two consumers cannot answer differently.
- **`--celebrate-force` is the positive control**, not a feature. It reaches one boolean in
  `BubbleCelebration`; `AchievementRuntime` never reads it, so a forced bot still writes nothing.
  Without it, "no bot celebrated" is indistinguishable from a broadcast that never fires.

### Measured while building it

- **The latch is a guarantee, not the thing currently doing the work.** Deleting it left the suite
  green: `TryAnnounceCompletion` is only reachable from `ServerPop`, which returns early once
  `BubbleCounterState.TryPop` has nothing left to flip. Confirmed by making the announcement
  reachable per frame — with the latch, still exactly 2 announcements in a session; without it,
  **3527**, every peer flooded, and the late joiner greeted by a completion it did not earn. The
  latch is what keeps a future second call site from double-announcing; today `TryPop` is the wall.
- **A capture bot renders a flat grey world unless it is given `--capture-cam`.** A bot builds no
  `SandboxCamera` of its own. The first capture run produced a perfectly correct HUD and toast over
  the engine's clear colour, which reads exactly like a broken level.

---

## 8. STEAM-1 — `watis_game.exe`, and the real App ID (2026-09-04)

Talon, from the Steamworks partner site: *"The install folder is now called
`watis_install_folder` and the executable is called `watis_game.exe` This is the number, by the
way 4951240. I've requested playtest keys."* Three partner-site facts; this packet makes the repo
agree with them.

### The executable name, and what a half-rename would have done

`watis-world.exe` → **`watis_game.exe`**. The rename is in three places that all have to move
together, and the interesting one is that they fail *differently*:

- `export_presets.cfg` `[preset.1] export_path` — the preset's own output path.
- `Export-WindowsClient.ps1`'s **CLI argument** to `--export-release`, which **overrides** the
  preset path. So renaming the preset alone is the one half-rename that **passes vacuously**: the
  CLI argument still wins, the export still emits the old name, `$exportBin` still finds it, and
  the script prints OK while the partner site's Launch Option points at a file that is not there.
- `$exportBin`, which every post-export assertion reads. Renaming this, or the CLI argument,
  without the other **fails loudly** — the directory is wiped before each export, so no stale
  binary can satisfy `Test-Path $exportBin`. Loud in two of the three cases, silent in one; the
  silent one is why the preset was not treated as the source of truth on its own.

### One naming scheme, across all three platforms

Talon named only the Windows executable. The scheme he chose is lowercase snake_case, and it is
extended rather than invented:

| | was | now |
|---|---|---|
| Windows client | `watis-world.exe` | **`watis_game.exe`** (his) |
| Linux server | `watis-world-server.x86_64` | **`watis_game_server.x86_64`** |
| macOS client | `watis-world.zip` / `.app` / `Contents/MacOS/watis-world` | **`watis_game.zip`** / `watis_game.app` / `Contents/MacOS/watis_game` |

**These are binary names, not display names, and the display names did not move.** The Windows
preset still carries `application/product_name="Watis World"`, and `Branding.Wordmark` is still
`"Watis World"`; the exported client's window title is still `Watis World`, verified on the boot
run below. The only user-visible consequence is on macOS, where the bundle's *filename* is what
Finder labels — `watis_game.app` rather than `Watis World.app`. Recorded as a value call, not
smuggled: the alternative was one platform naming itself differently from the other two.

**The orphaned change in `C:\repos\Watis_Game` is superseded either way.** That tree carries an
uncommitted, unowned edit renaming the mac bundle to `Watis World.app` with a binary
`Watis World` — six assertion strings in `Export-MacClient.ps1`, nothing else. It predates
today's decision, it disagrees with `watis_game` in both directions (spaces, and a display name
where the other two platforms use a machine name), and STEAM-1 has now rewritten every line it
touched. It should be discarded, not merged.

### The App ID is wired as a parameter, not as a constant

**`4951240` is documented in `deploy/steam/README.md` with the exact invocation, and is committed
nowhere else.** `app_build.vdf.template` says why — the templates carry `%APP_ID%`,
`Upload-Steam.ps1` substitutes at render time, and the rendered file lands in gitignored
`_generated/`. That reasoning still holds with a real number in hand; a real number is exactly
what it was written to keep out of the tree.

**The conflict, stated rather than resolved.** `docs/store/2026-08-29-STEAM-STATE-private-playtest.md`
§4 recorded `4951240` / depot `1718371` as the **Playtest** app pair, and §4c then ruled the
Playtest app was *not* the vehicle — the route Talon chose on 2026-08-30 was Release Override keys
on the unreleased **base app**, because Valve documents that a Playtest must be released to be
playable and that a released app surfaces *"even if it is released with a 'Hidden' store page"*.
Today's message supplies `4951240` and says playtest keys have been requested. Only Talon can say
whether that is a reversal or a number from a different app; both the README and the checklist now
carry the question beside the command instead of asserting an answer.

### `steam_appid.txt` does not ship, and the export now enforces it

It is a **development** convenience: it lets a build started outside Steam identify itself
(`SteamService.TryAppIdFile` probes the exe's directory). Shipping it in a depot is a separate
decision and the answer is no — the file **overrides** the App ID Steam itself handed the process,
so a stale number beside the exe outlives every later App ID change and fails on a tester's
machine rather than here.

It was already gitignored (`.gitignore:26`) and already absent, but "absent because nobody made
one" is not a guarantee. `Export-WindowsClient.ps1` now asserts the absence and fails the export
if the file appears, printing `[export] no steam_appid.txt in the export - dev-only override
correctly excluded` when it does not. A local non-Steam run is unaffected three ways: drop your
own copy beside the exported exe, pass `--steam-app-id 4951240`, or set `SteamAppId` in the shell.

### `FallbackSteamAppId` stays 480 — deliberately, with a trigger

`ResolveAppId` with no env and no file falls through to Spacewar. **Not changed.**

1. **It fails safe.** Spacewar initialises for every logged-in Steam account; a real App ID
   compiled into the binary fails `SteamAPI.Init()` for anyone who does not own that app.
2. **It never fires on the route being shipped.** A Steam-launched build resolves from Valve's
   `SteamAppId`/`SteamGameId` long before reaching the fallback.
3. **It is not a one-constant change.** §3b pairs it with `NetProfile.GameTag`, the lobby-directory
   scope key; changing that mid-playtest makes new builds unable to see rooms hosted by the builds
   already in testers' hands. That is a compatibility break, and this packet's scope excludes it.

**Trigger:** change both in one commit once it is settled which app `4951240` is, then re-run
`tests/Run-SteamLogicTest.ps1` — it asserts the resolution *order*, not the value.

### `watis_install_folder` needs nothing in this repo, and that is the finding

It is the Steamworks **Installation → Install Folder** field: the directory under
`steamapps/common/` on a player's disk. Nothing here reads it and nothing should. The depot
scripts describe **local** paths (`depot_build_client.vdf`'s `ContentRoot` is
`build\windows-client\` on the uploading machine, and `FileMapping` maps `LocalPath "*"` to
`DepotPath "."`, the depot root — Steam decides where that root lands). The game resolves every
path it needs from `OS.GetExecutablePath()` or `user://`, neither of which carries the install
folder's name. Recorded in `deploy/steam/README.md` so the next person does not have to re-derive
the negative.

### Measured

```
dotnet build WatisWorld.sln          4 Warning(s)  0 Error(s)   (the same four §4 names)

deploy\Export-WindowsClient.ps1      exit 0, 0 ERROR lines (warm), 190 MB total
    [export] OK - C:\repos\Watis-steam1\build\windows-client\watis_game.exe (190 MB total)
    watis_game.exe   114,617,584 bytes      (the MOVE-1 section of 2026-09-04 recorded
                                             watis-world.exe at 111,831,216 bytes; +2,786,368,
                                             +2.5%, the LEVEL-1 / HONK-1 / SHADER-1 / SHADER-2 /
                                             CELEBRATE-1 / BRAND-1 / COPY-1 / FIX-1 merges that
                                             landed on the combined branch since)
    build tree       199,266,417 bytes      (§4 recorded 187 MB total on 2026-09-02)

watis_game.exe launched, held 40 s, window title "Watis World", 0 ERROR lines, 1 WARNING:
    [graphics] tier High (ship default)
    [display] window mode: wanted=fullscreen actual=Fullscreen (user://settings.cfg [display] fullscreen)
    WARNING: Realtime Skies can only use a radiance size of 256 ...
        [3] MenuBackdrop.BuildWorld -> [4] MenuBackdrop.Build -> [5] MenuBackdrop._Ready
        -> [12] MainMenu._Ready
```

The first export of the session ran on a cold `.godot` and printed the documented
UITheme-before-fontdata ordering ERRORs (§4 records the same artefact for a cold import). It was
re-run warm after `godot --headless --path . --import` (exit 0, 0 ERROR lines) and the numbers
above are from that clean run.
