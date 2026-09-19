---
type: report
packet: MOVE-1
role: programming
date: 2026-09-04
branch: feat/2026-09-04-move-1-bike-lab-port
base: main @ c16414d
source: Sail `claude/bike-handling-camera-harness-17bpx2` @ 1b3e9bd7; LD-1 from Sail `origin/master` @ 583ecc02
summary: The movement lab and the bike come over from Sail. Both self-tests reproduce Sail's output byte for byte, the metrics card regenerates byte-identical, and the Steam export does not grow.
---

# MOVE-1 — the movement lab and the bike come over from Sail

## Verdict

Every one of the seven scope items landed. The two engine self-tests produce **byte-identical
output** on this tree and on `Sail-bike2xl` @ `1b3e9bd7`, `docs/levels/METRICS-CARD.md`
regenerates **byte-identical** to Sail's at `583ecc02`, and the Windows export is unchanged at
187 MB with the lab scene provably absent from the pack.

**Bibles applied: none — a port with no behaviour change; `docs/ANIMATION-CONTRACT.md` is the
contract checked.**
Items checked: the ride channel as built — `SetRide`'s single entry point and its weight-0
identity, the gait short-circuit to `Idle`, the fixed-gear crank rule at 5.875 m/rev, the PEDAL /
COAST hysteresis, the airborne 40 % partition, and the ride-weight crossfade of the turn roll.
Result: pass — the new §10 states the channel as built, and `RidePoseTests` plus
`--bike-selftest`'s five `ride_*` checks are what hold it.

---

## 1. Port the lab

`scenes/dev/MovementPlayground.tscn`; `scripts/dev/MovementPlayground.cs` and all seven `Bike*`
partials (`BikeCapture`, `BikeHandling`, `BikeHandlingSelfTest`, `BikeRide`, `BikeRideCapture`,
`BikeSelfTest`, `BikeWobbleCapture`); `MotorTuningPanel.cs`, `MotorTuningSession.cs`; the entire
`scripts/dev/playground/` (24 `.cs` files — seven concrete `MovementCourse` subclasses: Calibration,
Flow, Scramble, Surf, Descent, BikePark, Rolling, plus every `Bike*.cs`, `CalibrationPlan`,
`MovementPresets`, `ChargeJumpIntentSource`, `SprintDefaultIntentSource`, `IridescentProps`);
`tests/Run-MovementPlayground.ps1`; `tests/unit/MotorTuningSessionTests.cs`.

**Two files the packet did not name but the lab does not compile or run without**, both from the
same commit and both copied verbatim: `scripts/dev/MotorKnobRow.cs` (the panel's row widget) and
`scripts/dev/MovementVerbReadout.cs` (the readout's verb lines). `scripts/dev/ViewportCapture.cs`
was already here and is unchanged.

**`project.godot` was NOT touched.** The lab needs no autoload and no new input action: the only
actions it reads by name are `jump`, `move_left`, `move_right` and `walk`, and `walk` is declared
into the live `InputMap` at runtime by `LocalInputIntentSource.EnsureWalkAction` — which is also
how `Sail` does it (`walk` appears nowhere in either repo's `project.godot`).

**Evidence** — the lab's own headless census, printed by the runner from source:

```
knob table: 58 rows in 10 groups (counted from scripts\net\MotorTuningKnobs.cs)
courses:    7 - BikeParkCourse, CalibrationCourse, DescentCourse, FlowCourse,
                RollingCourse, ScrambleCourse, SurfCourse (counted from scripts\dev\playground\)
```

### Every reference to a cut system that was removed

Full reasoning is in `DECISION-LOG.md`, "2026-09-04 — the lab and the bike come over". In brief,
six removals and one restoration:

| # | File | What it named | What was done |
|---|---|---|---|
| 1 | `scripts/dev/MovementPlayground.cs:810` | `AvatarMotor.GravityFor(…, ballistic: false)` — `ballistic` is the eel-blast system's | argument dropped; this repo's `GravityFor` takes three parameters |
| 2 | `scripts/dev/playground/IridescentProps.cs` | `res://resources/shaders/iridescent/crystal_facet.gdshader` | `ResourceLoader.Exists` probe + tinted `StandardMaterial3D` fallback. The shader was **not** re-imported |
| 3 | `scripts/dev/MotorTuningPanel.cs` | "F6 is `CampfireMenu`'s, F8 is `AmbientLab`'s" | reservations dropped, reason recorded in place |
| 4 | `scripts/dev/playground/BikeKnobPanel.cs` | same two, in its class doc | same |
| 5 | `tests/unit/MotorTuningSessionTests.cs` — the pinned-row census | `NightPressureTests` / `ToolStanceTests` pins on `MoveSpeed` / `SprintMultiplier` | test renamed `TwentyNineRowsArePinned…`; **measured on this tree: 29 pinned, 29 clean, 58 rows**; both rows named in the clean list |
| 6 | `tests/unit/MotorTuningSessionTests.cs` — `APinnedKnobStillMoves…` and `TheInvariantReadout…` | `MoveSpeed`'s pin; `"EelProfile invariant 1"` | warn-not-lock now proved on `Acceleration` (a real pin here, `LocomotionTests`' [7.855, 15.75] bracket) with an added guard that the row it names IS pinned; the invariant count is 1, and the eel invariant is asserted **absent** rather than breached |

**Put back rather than removed: `MotorArc.HeldJog`.** See "Corrections to the packet" C7.

---

## 2. The bike in the shipped body

`scripts/game/sandbox/AvatarVisual.cs` was NOT copied — Watis's copy has diverged from Sail's
extraction base by 206 net lines. The bike hunks were applied by three-way merge
(`git merge-file <watis> <9eed5132> <1b3e9bd7>`), **0 conflicts**, and the result was verified
against the source diff:

```
applied to Watis : 305 added, 10 removed
Sail 9eed5132..1b3e9bd7: 305 added, 10 removed   (the ten removed lines are identical)
```

`git log 9eed5132..1b3e9bd7 -- AvatarVisual.cs` returns exactly the two bike commits
(`86b4190a`, `253220e5`), so the whole base-to-tip diff of that file IS the bike work — there was
nothing else to disentangle. `RidePose.cs` was added verbatim.

**On-foot behaviour is unchanged in effect.** All 2163 xUnit tests pass, including
`VerbPoseTests`, `LocomotionTests`, `ArmfulPoseTests`, `ApexHangAndLandingDipTests` and the rest,
none of which were touched. One exception, and it is a source-text pin rather than a behaviour
pin — see C1 below.

---

## 3. The pins

`BikeHandlingTests.cs`, `BikeCameraTests.cs`, `RidePoseTests.cs`, `BikeRigTests.cs`,
`BikePresetTests.cs`, all verbatim, plus `RollingCourseTests.cs` (C2 below). All pass.

---

## 4. The suites, registered

- `tests/Run-BikeSelfTest.ps1` ported; its "deliberately NOT in Run-AllTests.ps1: a lab suite for
  a prototype that ships nowhere" header rewritten to say why it now is.
- `tests/Run-BikeHandlingSelfTest.ps1` written in the same shape, running `-- --bike-handling-selftest`.
  Sail had this self-test with no runner at all.
- `tests/Run-MovementPlayground.ps1` given a `-Headless` mode (C4 below) and registered.
- `tests/Run-AllTests.ps1` gained three registry rows and an optional per-suite named-switch splat.
- All three now clear `SAIL_MOVEPG_OUT` so one suite cannot hijack the next — see C9, which is the
  bug the marathon found and the reason `.claude/rules/test-suite.md` gained an entry.

---

## 5. The metrics gym

`scripts/net/MetricBands.cs`, `scripts/dev/playground/CalibrationPlan.cs`,
`tests/unit/MetricsCardTests.cs` from `583ecc02`. `docs/levels/METRICS-CARD.md` generated by
running `MetricsCardTests` with `SAIL_REGENERATE_METRICS_CARD=1`; never hand-edited.

```
diff <Sail 583ecc02:docs/levels/METRICS-CARD.md> <this tree's generated card>   →  no output
```

**Byte-identical. No number differs**, so there is nothing to quote a `MotorTuning.Default` value
behind.

---

## 6. The export stays lean

`scenes/dev/*` added to `exclude_filter` on the **WindowsClient** and **MacOSClient** presets
(lines 45 and 93 — see C3 for the packet's line numbers).

```
deploy\Export-WindowsClient.ps1   exit 0, 0 ERROR lines, 187 MB, steam_api64.dll present,
                                  version stamp verified
```

**187 MB, unchanged against `DECISION-LOG.md` §4's 2026-09-02 figure.**

**ABSENCE check, with its positive control** — the same export run twice, once with the filter
removed:

| | `watis-world.exe` bytes | occurrences of `scenes/dev/MovementPlayground` |
|---|---|---|
| filter in place | 111,831,216 | **0** |
| filter removed (control) | 111,832,256 | 1 |

The control moves, so the probe can see the thing it is asserting absent. The lab's whole cost to
the pack is 1,040 bytes; its C# compiles into the assembly either way, which is why "does not grow
by the lab scenes" is the right claim and "does not grow" would not be.

**Boot check** (`DECISION-LOG.md` §4's method — the export script itself has no boot step; see C5):
the exported client launches, holds a "Watis World" window for 30 s, and its own `--verbose`
stdout reaches `[graphics] tier High (ship default)` → `Splash.tscn` → `MainMenu.tscn` →
`MenuBackdrop.cs`, **0 ERROR lines in 582 stdout lines**, Vulkan Forward+ on an RTX 4070.

---

## 7. Talon's fork session

- `docs/design/2026-09-02-bike-mounted-moveset-options.md`, `-rider-animation-states.md`,
  `-wobble-reusables.md` — verbatim.
- `docs/playtest/2026-09-02-bike-session-plan.md` — verbatim except its two Watis-specific facts:
  the telemetry CSV path is now `…/app_userdata/Watis World/bike-telemetry/`, and §0 now says the
  launch is from this repo (`powershell -File tests/Run-MovementPlayground.ps1`) and that `Sail`
  is the historical record.
- `docs/playtest/2026-09-04-talon-notes-bike.md` — **copied verbatim per C1 of the dispatch, not
  created empty.** Byte-identical to the untracked file in `Sail-bike2xl` apart from the
  `build:` frontmatter line, extended exactly as permitted. Not summarised, reworded, renumbered,
  triaged or acted on. **Nothing in those five notes is in scope for MOVE-1.**
- `docs/ANIMATION-CONTRACT.md` — new **§10 "The ride channel — the mounted rider"**, stating the
  contract as built.

---

## Test results — raw

### xUnit

```
dotnet test tests/unit/SailNet.Tests.csproj
Passed!  - Failed:     0, Passed:  2163, Skipped:     0, Total:  2163, Duration: 3 s - SailNet.Tests.dll (net8.0)
```

### The scene marathon

```
=== summary ===
  Netcode: replication       PASS
  Practice: spawn/join/reap  PASS
  Security: hardening        PASS
  Voice: proximity relay     PASS
  Hosting: local spawn       PASS
  Sandbox: mechanics         PASS
  Net-objects: prop store    PASS
  Net-objects: spawn parity  PASS
  Carry: server-authoritative PASS
  Carry: drift (hold+walk)   PASS
  Carry: regrab-while-loose  PASS
  Carry: throw + loose       PASS
  Carry: networked proof     PASS
  Netcode: step determinism  PASS
  Netcode: net-sim           PASS
  Netcode: anti-cheat        PASS
  Netcode: arrive latch      PASS
  Steam: transport logic     PASS
  Authored props: adopted    PASS
  Reconnect: grace window    PASS
  Telemetry: module logic    PASS
  World: tidal-cycle phase   FAIL
  Avatar identity: replication PASS
  Session: room-code gate    PASS
  World: night-cycle bands   PASS
  World: run driver          PASS
  Net: wire-order probes     PASS
  Flow: playthrough spine    PASS
  Aim substrate: stance replication PASS
  World: bubble test (BT-0)  PASS
  Water: lake contract (W2)  PASS
  Water: splash VFX + audio (W4) PASS
  Voice: proximity gate      PASS
  Host: failure routing      PASS
  Failure states (1c)        PASS
  Graphics: tier writer      PASS
  UI: flow screens (CORE-B1) PASS
  UI: HUD layout law (PLAY-1) PASS
  Bubbles: shared counter    FAIL
  World: TV portal (BT-10)   PASS
  Movement lab: scripted run PASS
  Bike: state machine        PASS
  Bike: handling model       PASS

OVERALL: FAIL
```

Raw counts, from the run's own `=== summary ===` block with the anchored pattern
`^  .+ (PASS|FAIL)$`: **41 PASS / 2 FAIL of 43 registered suites**.

#### Every red, discriminated against main at `c16414d`

**`Bubbles: shared counter` — PRE-EXISTING, not this branch.** Re-run standalone on an idle
machine, 3 times per side, nothing else running. **Independently corroborated:** a peer session
reports the same suite failing 3/3 standalone at clean `HEAD 467bbeb` with its UI work stashed
out, and says it is getting its own packet. That is their measurement, quoted, not mine — but it
agrees with mine, which is the table below:

| | run 1 | run 2 | run 3 | the failing check |
|---|---|---|---|---|
| this branch | FAIL | FAIL | FAIL | `C (late) never saw the reset return the tally to 0 and every bubble to visible` |
| **main @ c16414d** | FAIL | FAIL | FAIL | **the same line, verbatim** |

Deterministic on both sides, same check, same measured bob offsets (A 0.1356 m, B 0.1356 m,
C 0.1350 m; lever accepted=1 refused=1 resets=1). It is a late-joiner reset defect that predates
this work and that my diff cannot reach — nothing in `scripts/game/bubbles`, `scripts/game/run` or
the lever path is touched. **Worth its own packet; not this one's to fix.**

**`World: tidal-cycle phase` — FLAKE, and one already documented in `.claude/rules/test-suite.md`**
as load-sensitive ("a two-client phase comparison with a 1.5 s tolerance — compare the
divergence"). It PASSED in the marathon immediately before this one on this same tree. Because the
first three standalone runs came out asymmetric (branch FAIL/FAIL/PASS against main PASS/PASS/PASS)
I did not stop there — I ran three more **interleaved** branch/main pairs to control for machine
drift, and main failed too:

| | m2 | m3 | s1 | s2 | s3 | s4 | s5 | s6 | tally |
|---|---|---|---|---|---|---|---|---|---|
| this branch | PASS | FAIL | FAIL | FAIL | PASS | PASS | PASS | — | **4 PASS / 3 FAIL** |
| main @ c16414d | — | — | PASS | PASS | PASS | PASS | PASS | FAIL | **5 PASS / 1 FAIL** |

Both sides flake, and the measured quantity is a **borderline** divergence rather than a broken
one — 1.6 % over a 1.5 s tolerance on the branch, and main's own failure is larger than either:

```
branch  cross-view: CycleEarly1 0.43129632 vs CycleEarly2 0.55837965
        differ by 1.52499996s (tolerance 1.5s)
branch  cross-view: CycleEarly1 0.55833334 vs CycleEarly2 0.43129632
        differ by 1.52444424s (tolerance 1.5s)
main    rejoin: post-resume phase (0.6480093) disagrees with concurrent CycleEarly1 (0.8173611)
        by 2.0322216s
```

Nothing in this diff can reach a two-process phase clock. **Flake, both sides.**

**`World: run driver` — FLAKE, PASSED in the final marathon**, and also documented load-sensitive.
It was red in the previous marathon, so it was discriminated too, 3 per side:

| | run 1 | run 2 | run 3 | final marathon |
|---|---|---|---|---|
| this branch | PASS | FAIL | PASS | **PASS** |
| main @ c16414d | FAIL | PASS | PASS | — |

Both sides flake, and **the measured quantity is the same to six significant figures**:

```
branch  reset: post-reset phase disagreement RunEarly (9.259259E-05) vs RunLate (0.27370372)
        - 2.7361112741s apart (tolerance 1.5s)
main    reset: post-reset phase disagreement RunEarly (0.00011574074) vs RunLate (0.27372685)
        - 2.7361110926s apart (tolerance 1.5s)
```

**`Bike: state machine` — was a real bug of mine, found by the previous marathon, fixed, and now
covered by a new rule entry.** **PASS in the final marathon (`BIKE-SELFTEST OVERALL: PASS (28/28)`).** See C9.

Two marathons were run on this tree. Both are reported rather than only the better one:

| | raw counts | the reds |
|---|---|---|
| marathon 2 (before the env-leak fix) | 40 PASS / 3 FAIL of 43 | `World: run driver`, `Bubbles: shared counter`, `Bike: state machine` |
| **marathon 3 (reported above)** | **41 PASS / 2 FAIL of 43** | `World: tidal-cycle phase`, `Bubbles: shared counter` |

A third, earlier run (21 PASS / 22 FAIL) is discarded and its reason is C8.


### The two engine self-tests, measured on BOTH trees

```
this tree           BIKE-SELFTEST OVERALL: PASS (28/28)
Sail-bike2xl 1b3e9bd7   BIKE-SELFTEST OVERALL: PASS (28/28)

this tree           BIKE-HANDLING-SELFTEST OVERALL: PASS (20/20)
Sail-bike2xl 1b3e9bd7   BIKE-HANDLING-SELFTEST OVERALL: PASS (20/20)
```

Not just the counts: the two runs' full check lines were diffed. `--bike-selftest` is
**byte-identical**, all 28 lines. `--bike-handling-selftest` differs on exactly two characters
across 20 lines — one em-dash the Watis capture came through PowerShell's console encoding, and
one telemetry CSV filename, which is a timestamp.

### Acceptance criterion 2 — the headed capture

`docs/qa/MOVE-1/` holds four BEFORE/AFTER pairs plus `ride-capture-log.txt`, taken headed through
the in-engine `ViewportCapture` path (never a screen grab). The readout in
`02-ride-at-cap-pedal-AFTER.png` reads `COURSE  Bike Park (bike)`, `gear Idle`,
`BIKE  MOUNTED`, `MOUNTED at 9.42 m/s`, `TAB next course (7)`. The log's numbers:

```
02-ride-at-cap-pedal-AFTER: speed 9.42 m/s, MOUNTED, blend 1.00, ride weight POSED 1.00,
    mode Pedal, cranks 1.604 rev/s at phase 0.222, gear Idle, gait cadence 0.00 steps/s
02-ride-at-cap-pedal-BEFORE: (ride channel forced off, same instant)
    ... ride weight POSED 0.00, gear Sprint, gait cadence 3.90 steps/s
```

A `PressMount()` — which is exactly what `Key.Q` calls, `BikeLayer.MountKey = Key.Q` — produces
ride weight **1.00**, and the gait short-circuit is visible in the same frame (Idle / 0.00
steps/s mounted, Sprint / 3.90 steps/s with the channel off). **One deviation from the criterion's
letter: see C6.**

---

## Corrections to the packet

**C1 — one pre-existing test file WAS edited, and the packet's acceptance criterion 7 forbids it.**
`tests/unit/AnticipationCoilTests.cs` pins the *source text* of `AvatarVisual.cs` and asserted the
literal line `CrouchDropM = _legLengthM * groundDropFold;`. The ride channel legitimately rewrites
that line (`Mathf.Lerp(groundDropFold, 0f, rideW)` — a rider's weight is on the saddle, so a
mounted landing absorb that dropped the hips would sink the body through the bike). **Sail's own
bike commit `86b4190a` made exactly this edit**, replacing one exact spelling with a property of
the assignment: the drop line must mention `_legLengthM` and `groundDropFold` and must contain no
coil/tuck/snap/knee fold term. That 23-line hunk was three-way merged in verbatim, 0 conflicts.
The `Assert.Empty` half — the load-bearing one — is untouched.
This is a source-text pin, not an on-foot behaviour pin, and no behaviour pin failed at any point,
so the packet's STOP condition was read as not triggered. **If that reading is wrong, this is the
one thing to reverse.** `git diff main --stat -- tests/unit` therefore shows this file plus added
files, not added files alone.

**C2 — item 3's list of five test files was short by one.** `9eed5132..1b3e9bd7` also adds
`tests/unit/RollingCourseTests.cs`, which pins `RollingCourse` — a file item 1 requires
("the entire `scripts/dev/playground/` directory"). Ported verbatim.

**C3 — item 6's line numbers are wrong.** "Lines 11 and 45 today" — line 11 is the **LinuxServer**
preset. WindowsClient is line 45 and MacOSClient is line 93. The two presets the packet **names**
were edited; LinuxServer was left alone.

**C4 — `Run-MovementPlayground.ps1` could not be registered as it stood.** Its default mode is
headed and interactive (it launches and exits 0 immediately), and its `-Capture` mode needs a real
rasterizer. Either would break `Run-AllTests.ps1`'s stated promise of a headless, zero-interaction
marathon, and `-Capture` would put a window on a shared GPU on every run. A `-Headless` switch was
added: the same scripted brain with `--headless` in front of it and no PNG assertion, since under
`--headless` the viewport read-back is a dummy and every beat records `[CAPTURE FAILED]`. The
readout — every course built, the jump arcs, the knob writer, the landing-dip A/B, the void-floor
count — is measured and printed on every marathon. The headed `-Capture` path is untouched.

**C5 — acceptance criterion 9 says "boots to the main menu per the export script's own log check".
The export script has no boot check.** `deploy/Export-WindowsClient.ps1` is entirely headless
(`--export-release`) and ends at the size line. The boot was done separately, by
`DECISION-LOG.md` §4's own method, and is reported above.

**C6 — the headed capture reaches the Bike Park by course identity, not by four TAB presses.**
`--bike-ride-capture` selects the park with `CourseIndexOf<BikeParkCourse>()`; `Key.Tab` calls
`TeleportTo(_courseIndex + 1)`. Two attempts to drive a real TAB into the headed window from
outside (WScript.Shell `SendKeys`, then Win32 `keybd_event` after `SetForegroundWindow`) produced
no key events in the process — the window never took foreground in this session — so no F9
in-engine screenshot was written. Rather than fake it with a screen grab, which the repo's
verification method forbids, the capture above is offered as it stands: the lab, headed, on the
Bike Park, mounted, at ride weight 1.00, with the readout in frame stating `TAB next course (7)`.
**The one thing not proven by machine is that four TAB presses land on the Bike Park**; the roster
that TAB walks is the same one the census above prints.

**C7 — `MotorArc.cs` was edited, and it is in neither the YOURS nor the NOT YOURS list.** The
extraction pruned `MotorArc.HeldJog` and rewrote a doc line to read "the since-removed movement
playground". `MetricBands.EnvelopeM` needs `HeldJog` for the `HeldJogRange` envelope, and item 5's
acceptance (a card byte-identical to Sail's) is unreachable without it. It was restored verbatim
and the doc line put back to the present tense. It is a pure derivation over numbers already in
the file — no tuning row, no new constant — and `AvatarMotor` does not call `MotorArc` at all
(checked: the consumers are `MetricBands`, `CalibrationPlan`, `BubbleTestLayout`, three unit test
files and two doc comments). **`AvatarMotor.cs` and `MotorTuning*.cs` were not touched.** The
packet's NOT YOURS names those two by name and not this file, so this was read as inside scope;
flagging it because the ROLE forbids self-correcting across a path list.

**C8 — the first marathon on this branch was run against a contended machine and is discarded.**
Another session's Godot processes were live throughout it (individual `Run-*.ps1` scripts do not
take the machine-wide mutex, so "lock acquired (uncontended)" did not mean the machine was idle).
It reported 21 PASS / 22 FAIL across suites my diff cannot reach — `Security: hardening`,
`Voice: proximity relay`, `Steam: transport logic`, `Graphics: tier writer` — and it also caught a
real bug of mine in the new registry splat (an empty collection returned out of a PowerShell `if`
expression collapses to `$null`, and splatting `$null` passes a positional `$null` every one of
these scripts rejects). The splat was fixed to a named-parameter hashtable and the marathon re-run
on an idle machine; that run is the one reported above.

**C9 — registering `Run-MovementPlayground.ps1` turned the two bike suites red without touching
them, and the marathon is what caught it.** `Run-AllTests.ps1` dot-sources and calls every suite in
ONE PowerShell process. `Run-MovementPlayground.ps1` sets `$env:SAIL_MOVEPG_OUT` for its scripted
capture and never cleared it; `MovementPlayground._Ready` checks that variable **before** it checks
`--bike-selftest` and takes the capture branch when it is set. So `Run-BikeSelfTest.ps1` and
`Run-BikeHandlingSelfTest.ps1` each launched a playground capture and then failed looking for an
`OVERALL` line that run never prints — `tests/logs/bike-selftest.out.log` was full of
`[move-playground]` lines. Fixed at the source (the setter clears it on every exit path) and
guarded at the two consumers (a suite must not depend on what ran before it). **Proved with a
positive control**: the suite was re-run with `SAIL_MOVEPG_OUT` deliberately poisoned and still
reported `BIKE-SELFTEST OVERALL: PASS (28/28)`. Recorded as a measured entry in
`.claude/rules/test-suite.md` — any suite that sets an env var for its child has this trap, and
this one was invisible in every standalone run.

**C11 — a second verbatim notes file, added mid-flight by the dispatching session.** (Relayed as
"C6"; that label was already taken by the TAB-capture deviation above, so it is C11 here and the
mapping is stated so nothing is lost.) `docs/playtest/2026-09-04-talon-notes-levels.md`, copied
**byte for byte** (`cmp` clean) from the untracked file in `C:/repos/Sail-ld6` — Talon's three
2026-09-04 notes from his level sitting: the kit-course verdict, a camera jolt at a slope crest,
and his request that the level and the bike be testable together. Copied because it existed only
as an untracked file in a scratch worktree, one `git clean` from gone. **Not summarised,
renumbered, triaged or acted on**, and nothing in it entered this diff. Its own framing — that the
jolt is a camera/motor symptom seen in a level scene rather than a level-geometry defect, and
likely reproducible outside the kit course — is correct as written and was left alone. The jolt
wants its own packet and does not have one; that is Talon's call.

**C10 — the report's own scope note.** `.claude/rules/test-suite.md` was edited (one measured
entry, C9's). The ROLE grants that path for measured entries only; this one is measured, dated and
carries its evidence, and no other agent's entry was touched.

---

## Open questions — ripe

**Q1 — `AvatarMotor` is flat-world, and that is very likely the root of Talon's slope notes.**
`SurfCourse.cs:12` states it: *"`AvatarMotor` reads the floor normal nowhere: the motor is
flat-world, so a hill is scenery and running down one gives exactly what running along a plain
gives."* **Confirmed by reading the motor.** The only floor normal `AvatarMotor.cs` touches is at
`scripts/net/AvatarMotor.cs:840-843`, inside the wall-bump reporter, and its own comment two lines
above says *"Reported, not reacted to — bumps are cosmetic-only by design rule."* Nothing in
`Step`'s velocity path reads a normal. The lab compensates with `UpdateSlopeMomentum` (K in the
playground) and `BikeLayer.SlopeBonusMps` — harness terms added from outside, not motor behaviour.
Cited, not acted on: `AvatarMotor` is closed for retuning, and Talon's notes are recorded, not
actioned, until he says go.

**Q2 — should `LinuxServer` also exclude `scenes/dev/*`?** The packet named the two client
presets; the dedicated server has no more use for a movement lab than they do. One line, not
taken, because the packet did not ask for it.

**Q3 — the session plan's §0 still points its notes at
`docs/playtest/2026-09-02-talon-notes-bike.md`, which does not exist in either repo** (the notes
that were actually taken are the 2026-09-04 file). Left alone: it is not one of the two
Watis-specific facts item 7 authorises correcting, and it is Sail provenance.

## Not done, and why

Nothing in scope was left undone. Explicitly out of scope and untouched, as instructed: the six
session-plan forks and the rider-lean fork (Talon's hands); no bike default, preset, knob or course
order changed; the bike is not networked and is not in the bubble test (HANDOFF-2026-09-04
Q2/Q3); the crouch wind-up and first-jump pause are motor work (Q15); no level content; no UI
outside the lab's own panels.
