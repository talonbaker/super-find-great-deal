---
paths:
  - "tests/**"
---

# Test-suite discipline

> **Write access:** any role may add a measured entry here (Talon, 2026-08-28: "write access
> granted to all who want it"). Add what you measured, with the evidence and the date; never
> delete another agent's entry, and never add one on reasoning alone.

- **"The full suite" is TWO commands. Neither one alone is the full suite.**
  ```
  powershell -File tests/Run-AllTests.ps1        # the Godot scene suites (Windows)
  dotnet test tests/unit/SailNet.Tests.csproj    # the Godot-free xUnit suite (anywhere)
  ```
  `Run-AllTests.ps1` does **not** run `dotnet test`. Run both before every commit, never a
  filtered subset (parallel xUnit interactions fail tests that pass in isolation), and report
  RAW COUNTS from each — counted from the run's own `=== summary ===` block with the anchored
  pattern `^  .+ (PASS|FAIL)$` — never a verdict and never a stale total from this file.
- **Run the scene suite in the FOREGROUND and block on it.** A backgrounded suite plus "wait
  for the notification" ends a dispatched session mid-task. If a run is killed at a lifetime
  limit, do NOT report the partial run as green — finish the remaining suites individually
  with `-SkipBuild`.
- **Redirect, never pipe.** `powershell -File tests/Run-AllTests.ps1 > suite.txt 2>&1`, then
  grep the file. In bash, `... | tee` / `| head` / `| grep` reports the LAST element's exit
  code and hides the runner's; `tail -60` has silently cut the top rows off a summary that then
  read as green. A starved run (mutex timeout) and a red run both exit 1 — read the text.
- **One suite per machine at a time.** Port 7818 and `tests/logs/` are shared; the runner takes
  a machine-wide mutex (`-MutexTimeoutMinutes`, default 30 — use 120+ during a live wave). A
  mutex timeout is UNVERIFIED: not a red, not a pass. Re-invoke; never investigate or kill
  another agent's Godot process.
- **Never rebuild the DLL mid-suite**, and `dotnet build` before any Godot run that touches C#.
- **Stage by path. Never `git add -A` after a suite run** — Godot regenerates `.import`/`.uid`
  sidecars with zero content diff (`.claude/rules/imports-and-encoding.md`).
- **`user://` resolves by project NAME, so every checkout on a machine shares ONE profile**
  (`%APPDATA%/Godot/app_userdata/Super Find Great Deal/` — renamed at the fork, BASE-1 2026-09-19,
  precisely so this game's bots can never reach Watis World's profile). `settings.cfg` there is the player's real
  profile: a suite bot has already spent a real achievement into it once. Gate any persisted
  per-player fact on `IIntentSource.IsHumanInput`, and before adding one, ask whether a bot can
  trigger it.
- **A scripted bot may compute its next move from a predicted position, but must not take an
  irreversible action on one** (the arrive-latch lesson, W6-3, 2026-08-30).
- **Godot CLI: engine flags before `--`, game flags after.** Wrong side is swallowed and looks
  exactly like a hang.
- **Two RenderingServer read-back APIs are editor-only** (`GlobalShaderParameterGet`,
  `GlobalShaderParameterGetList`) and return Nil / empty in a running project. For a global
  shader parameter the RENDER is the only witness.
- **Prove a new test can fail before trusting it.** Every packet that did this found something.
- **`Run-AllTests.ps1` runs every suite in ONE PowerShell process, so an env var a suite sets
  outlives it.** Measured 2026-09-04 (MOVE-1): registering `Run-MovementPlayground.ps1`
  turned the next two suites red without touching them. It sets `SAIL_MOVEPG_OUT` for its
  scripted capture; `MovementPlayground._Ready` checks that variable BEFORE it checks
  `--bike-selftest` and takes the capture branch when it is set — so `Run-BikeSelfTest.ps1`
  and `Run-BikeHandlingSelfTest.ps1` each ran a playground capture and then failed looking
  for an `OVERALL` line that run never prints. Evidence: `tests/logs/bike-selftest.out.log`
  full of `[move-playground]` lines. **A suite must clear what it sets and must not depend
  on what ran before it** — all three scripts now do both. Any suite that sets an env var
  for its child process has this trap.
- **Load-flaky suites.** The scene suite is not deterministically green under machine load; two
  consecutive marathons on an unchanged tree have failed different suites. A single red is not
  a regression and a full-house pass is not the expected result. Discriminate by re-running the
  red suites standalone on an idle machine (3× per side, say what else was running) and by
  comparing the RAW measured quantity, not the verdict. Known load-sensitive in Sail's history,
  carried forward unverified here: `World: run driver`, `World: tidal-cycle phase` (a two-client
  phase comparison with a 1.5 s tolerance — compare the divergence), `Carry: regrab-while-loose`
  (its reds are STAGING failures — read the failing check's NAME; the 0.89 m worst hold distance
  is the quantity), `Reconnect: grace window`, `Netcode: anti-cheat` ("the grab never landed").
  **Measured here, not carried forward:** `Carry: server-authoritative` and `Carry: throw + loose`
  (SHADER-2, 2026-09-04 — both fail as STAGING, "the grab never landed" / "never staged"; see the
  dated section at the end of this file).

## Every test on this branch shares one clock, so no test can see a two-clock bug (HONK-1, 2026-09-04)

**Found in review, after the whole suite was green.** HONK-1's honk cooldown is enforced on the
server and mirrored on the client to save packets, and both latches were set to the same 0.6 s. The
client spaces its sends at exactly that; the server judges them on **arrival**. The gap the server
sees is therefore `Cooldown + (jitter₂ − jitter₁)`, and that term is negative half the time on any
real connection — so every slightly-early arrival was refused, and because a refusal does not
advance the latch, the next honk landed a FULL cooldown after the last granted one. **A player
holding the key would have honked at about half the intended cadence, at random.**

Nothing in `tests/unit` could see it (one process, one clock, no transport), and nothing in the
scene suites could either: bots on localhost have jitter deltas near zero, so the failure is
invisible exactly where CI runs. **A green suite is not evidence about a rule that is enforced on
two clocks.** When the same rule lives on both sides of the wire, the authoritative side must be the
LOOSER of the two by a margin bigger than the jitter — the ordinary client-predicted /
server-checked shape — and the margin belongs in a named constant with the arithmetic written down
(`HonkConfig.ServerJitterToleranceSec`), because the suite cannot defend it for you.

## Two ways a multi-peer suite passes without testing anything (HONK-1, measured 2026-09-04)

Both were found by planting a fault and watching the suite stay green — the "prove a new test can
fail" rule above earning its place twice in one packet. Both generalise past the honk.

- **On an ENet CLIENT, `Multiplayer.GetPeers()` returns only the server (id 1).** A probe that
  enumerates it to reach *other clients* iterates an empty set and attempts nothing. HONK-1's
  forgery probe did exactly that: it fired zero forgeries, and the suite's "no forged honk landed"
  assertion passed with `RpcMode.Authority` deliberately downgraded to `AnyPeer`. **A client's view
  of the other players is the replicated player list, not the transport's peer list** —
  `VoiceManager.GetRemotePlayers()` is the one to read, and it is also the more honest attacker
  model, since an attacker sees avatars rather than ENet peers. **Assert the ATTEMPT count off the
  probe's own log before believing any absence it produces.**
- **A client-side copy of a server rule hides the server rule.** HONK-1's honk cooldown is
  enforced on the server and mirrored on the client purely to save packets. Seven scripted presses
  through the shipped verb produced two honks and the server logged nothing at all — the local copy
  suppressed the extras before a packet left the machine, so the suite was proving the courtesy
  latch while the AUTHORITATIVE one had never executed. Any rule enforced on both sides needs a
  deliberately non-compliant client to reach the side that matters: `--voice-flood` was the
  precedent, `--honk-flood` is the copy (290 raw requests over 2.0 s produced 6 honks).

## A latch that is protected by something else is not being tested (CELEBRATE-1, measured 2026-09-04)

**Deleting an idempotency latch left the whole suite green, and that was a fact about the call
graph, not about the suite.** `BubbleCounter.TryAnnounceCompletion`'s "announce once per
completion" latch is reachable only from `ServerPop`, which returns early once
`BubbleCounterState.TryPop` has no bit left to flip — so with the level already complete, the
latch's branch is never evaluated a second time and removing it changes nothing observable.

The discriminating experiment is two mutations, not one, and it generalises to any guard whose
call site is itself guarded:

| Mutation | Result |
|---|---|
| latch removed, call site unchanged | **PASS** — the latch was never the thing holding it |
| announcement made reachable per frame, latch **intact** | **PASS** — 2 announcements in the session |
| announcement made reachable per frame, latch removed | **FAIL** — **3527** announcements, every peer flooded, and the late joiner greeted by a completion it did not earn |

So: **to prove a guard, first make its call site reach it.** A single "delete the guard and watch
the suite" mutation reports the same green for a guard that works and a guard that is unreachable,
and the second is the one that bites the day someone adds a call site.

## A capture bot renders a flat grey world unless it is given `--capture-cam` (CELEBRATE-1, 2026-09-04)

A `--bot` client builds no `SandboxCamera` of its own. A `--capture-dir` run without
`--capture-cam` therefore writes frames in which every CanvasLayer is perfectly correct — HUD,
toasts, prompts — over the engine's flat clear colour where the 3D world should be. It reads
exactly like a level that failed to build, and the bot's log says nothing (`[world] building` and
the bubble adoption line both appear normally). `Run-Shader1Capture.ps1`'s header already called
`--capture-cam` mandatory for its own reason; this is the second, and it applies to every capture
whether or not the shot needs a specific framing.

## `Bike: handling model` is load-flaky, and the discriminator is CHECKS vs EXIT CODE (CELEBRATE-1, measured 2026-09-04)

Add it to the load-flaky list above with a discriminator of its own, because its red does not look
like the others: **every one of its 20 checks reported PASS and the runner still failed the
suite**, on `bike handling self-test exited -1`. The error stream carried a `GD.PushError` out of
`MovementPlayground` / `SandboxAvatar.ConfigureNetworkedInstance`'s "no `Sync` child" branch — a
lab-boot complaint, not a check.

Measured on `feat/2026-09-04-celebrate-1-all-bubbles`, whose diff cannot reach the bike at all
(zero changes under `scripts/dev/**` or `scripts/net/**`):

| Run | Conditions | Checks | Exit | Verdict |
|---|---|---|---|---|
| 48-suite marathon | loaded, 42 suites already run in the same PowerShell process | **20/20 PASS** | −1 | FAIL |
| standalone ×3, `-SkipBuild` | idle | 20/20 PASS each | 0 | PASS |
| the marathon's own three-suite tail (`Run-MovementPlayground` → `Run-BikeSelfTest` → `Run-BikeHandlingSelfTest`) in ONE PowerShell process | idle | 28/28 and 20/20 | 0 | PASS |

The third row is the one worth copying: the standalone re-run drops the *process sharing* that the
MOVE-1 env-var trap above lives in, so re-running the tail in one process is what separates "load"
from "the suite before it". Here it cleared both. **Read this suite's red by counting its checks
first** — a `20/20 PASS` line above an `exited -1` is a teardown artifact; a missing or changed
check is the real thing.

## Baseline after the extraction (2026-09-02, Linux cloud container)

- `dotnet test tests/unit/SailNet.Tests.csproj`: the raw line lived in `Watis_Game`'s
  `DECISION-LOG.md` §3, which did not come over. This repo's own baseline is the BASE-1 section
  at the end of this file.
- The scene marathon needs Windows PowerShell and was NOT run in the cloud; the headless
  self-tests that do not need PowerShell were run directly (same section).

## A bot lifetime that is typed beside the mark it must outlive (FIX-1, measured 2026-09-04)

**A suite can be reproducibly red and still be reporting a GREEN assertion that has never run.**
Measured on `tests/Run-BubbleSyncTest.ps1` at `c16414d`: the late joiner was launched ~6 s into the
session with a typed `--duration 26` measured from its OWN launch, against a reset scheduled 30 s
into the SERVER'S clock plus a 2.25 s lever confirm. Its last sample landed **18:02:32.199Z** and
the reset's effect first appeared on a peer present throughout at **18:02:32.340Z** — **141 ms
early, byte-identically every run**, because a race that always loses is still a race. The suite's
one failure named the game (`C (late) never saw the reset…`); the cause was the harness, and the
late-joiner half of that assertion had never executed once in the suite's life.

Three things measured here, all reusable:

- **Sample counts discriminate a starved bot from a bot that went home on schedule.** A/B logged
  176 samples over 35.80 s (4.92 Hz) and C 128 over 25.80 s (4.96 Hz): the same rate over a
  shorter window means C exited on its own clock, not that it was disconnected or starved. A rate
  that differs would have meant the opposite. Compute it before blaming replication.
- **`BotHarness`'s `Wall` field (Unix ms) is comparable across processes on one machine** and is
  the only cheap way to put three separate JSONL logs and a server log on one timeline. Use it.
- **Gate a bot's RETENTION on a server log line, never only its launch.** `Wait-ForLogLine` on the
  server's own event before waiting on any bot exit, and assert the late peer logged a sample
  strictly AFTER that event. Without the second half, a window that closed early is indistinguishable
  from a broadcast that never arrived — the distinction is worth the whole assertion.

**Derive bot lifetimes from the schedule they must outlive; never type one beside it.** FIX-1's
fixture announces `[bubbletest] reset schedule: … resetLandsAt=…` and the runner sizes every
`--duration` from it, so `--bubble-reset-at`, `BubbleResetConfirm.MinDwellSec` and
`ConfirmWindowSec` all move the sampling windows with them. `tests/unit/BubbleSyncWindowTests.cs`
pins it — proved able to fail on a planted literal `--duration 26`.

## `Netcode: net-sim` is load-INDEPENDENT flaky, and the quantity is bot3's remote step (COPY-1, measured 2026-09-04)

**`Netcode: net-sim` belongs on the load-flaky list above with one correction: it flakes on an IDLE
machine too, so re-running it standalone does not settle it.** Discriminate by comparing the failing
quantity across trees, not by re-running until it is green.

Measured on `playtest/2026-09-04-combined` @ `36724b4` and on COPY-1's branch off it, standalone,
`-SkipBuild`, nothing else running on the box (checked by process listing before each side):

| Tree | Runs | Result | bot3 `peak remote step` on the red |
|---|---|---|---|
| base @ `36724b4` (detached worktree) | 4 | 3 PASS / 1 FAIL | **6.409 m** |
| COPY-1 (UI copy + one settings row) | 3 | 2 PASS / 1 FAIL | **3.302 m** |

Plus the marathon red that started this: 3.303 m, under load from a second agent's suite.

**Read it this way.** The assertion is `bot3 saw a remote render step of N m (> 0.35 m = visible
jump)` — a smoothness bound on a REMOTE peer's rendered position under `--net-sim 80,5,15`. It is
always **bot3**, always a single step, and the magnitude is not a distribution tail creeping over a
threshold: a passing run measures ~0.03–0.07 m and a failing run measures **metres**, two orders of
magnitude out. That is a discrete event — a snapshot seam, an epoch bump or a spawn — landing or not
landing inside the sampled window, not a machine that was busy. A base run produced a WORSE
magnitude than the branch that was being discriminated against it.

**So the discriminator is the magnitude and the bot, not the verdict.** A red here is a regression
only if the failing peer changes, or the magnitude climbs materially above the ~3–6.5 m band both
sides already produce, or the passing runs' ~0.03–0.07 m floor moves. `Carry: regrab-while-loose`
(already listed) still discriminates the documented way — it went 3/3 PASS standalone on an idle
machine, worst hold distance 1.07–1.09 m, after failing its STAGING check under load.

**Not investigated further here.** COPY-1 changed one `.tscn` label, one string array, one settings
row and two test files; nothing it touched is reachable from `scripts/net/**`. Someone should chase
the metre-scale step itself — it is a real visible jump for a real player under 5 % loss, and it is
on `main`'s side of the fence, not this branch's.
## `Netcode: anti-cheat` reconfirmed load-flaky (SHADER-1, measured 2026-09-04)

Measured on `fix/2026-09-04-shader-1-bubble-over-water`, whose entire diff is one
`render_priority` field in `resources/materials/bubbletest/bubble_film.tres` — nothing that can
reach netcode, carry, props or the motor. The marathon ran **46 suites, 45 PASS / 1 FAIL**, the
red being `Netcode: anti-cheat` with the exact string the load-flaky list above already names:
`scenario 2: prop 1 was never observed held by CheatCarrier — the grab never landed`. That is a
STAGING failure in scenario 2, not the containment assertion the suite exists for.

**The raw quantity is the discriminator, and it did not move.** Scenario 1 reports the cheater's
peak 1.00 s-window speed in the server's view:

| run | machine | peak 1.00 s window | displacement / samples | verdict |
|---|---|---|---|---|
| marathon | loaded (queued ~20 min behind another worktree's full suite) | 3.80 m/s | 15.2 m / 59 | FAIL (scenario 2 staging) |
| `Run-CheatTest.ps1` standalone ×1 | idle | 3.81 m/s | 15.2 m / 58 | PASS |
| standalone ×2 | idle | 3.80 m/s | 15.3 m / 59 | PASS |
| standalone ×3 | idle | 3.80 m/s | 15.2 m / 58 | PASS |

Same to within 0.01 m/s across all four. **3/3 PASS standalone on an idle machine.** What was
running during the red: another agent's `Run-AllTests.ps1` out of `C:\repos\Watis-copy1` had just
released the machine-wide mutex, and two other packets were live in their own worktrees.

Reusable: when this suite is red, read the FAILING CHECK'S NAME first. "The grab never landed" is
the staging half and is load-sensitive; a moved *speed* number would be the real thing.

## `Carry: server-authoritative` and `Carry: throw + loose` are load-flaky (SHADER-2, measured 2026-09-04)

Two suites that the load-flaky list above did NOT name, added here because they were measured
rather than reasoned about. **Both fail as STAGING, not as behaviour** — same class as
`Netcode: anti-cheat`'s "the grab never landed", one world over.

Measured on `fix/2026-09-04-shader-2-sunken-tv-artifact`, whose entire diff is one
`material_override` on one `MeshInstance3D` in `GreenHills.tscn` plus documentation and a capture
script. No C#, no netcode, no carry, no prop, no motor — and both suites run the **`propsync`**
world, which a `bubbletest` scene edit cannot reach.

Read the failing check's NAME:

- **`Carry: server-authoritative`** printed four failures that are **one fact**: `bot A: never
  observed prop 1 held by A — A's grab never converged on this peer` (on two peers), then `bot B
  was, at some point, prop 1's holder — contested grab was NOT rejected`, then `bot D's first
  sample: holder=B, expected A via late-join dump`. A's scripted grab never landed, so B won a
  contest A never entered and the late-join dump then honestly reported B. The containment
  assertion the suite exists for was judged on a scenario that was never staged.
- **`Carry: throw + loose`** printed `bot C: prop 3 was never held — the OOB throw was never
  staged`. It says "never staged" in its own words.

**Discriminator — the full assertion line, not the verdict.** A real regression cannot print the
whole list. Standalone with `-SkipBuild` on the same tip:

| suite | runs | result | the line it printed |
|---|---|---|---|
| `Run-CarryTest.ps1` | 3 | **3/3 PASS** | `grab converged, contested grab rejected, drop converged, disconnect released, late-join converged` |
| `Run-ThrowTest.ps1` | 3 | **3/3 PASS** | `holder cleared, prop travelled, settle converged across peers, OOB prop recovered home` |

Those are the *same* assertions the marathon reported on, including the contested-grab containment
check — so the behaviour never moved; only the staging did.

**The re-runs were NOT on an idle machine, and that is the strongest part of the measurement.**
Immediately after the marathon, 10 Godot processes that were not mine were observed; during the
standalone re-runs, 4 more (started after my own suite had exited). Another agent (BUBBLE-1) was
live on the machine throughout. Per the rule above, none of them was investigated or killed. The
two suites therefore cleared their staging **6/6 while competing with the same neighbour that was
there when they failed**, which is a stronger flake verdict than an idle-machine pass would be.

Reusable: for either of these suites, "the grab never landed" / "was never held" / "was never
staged" is the staging half. A moved convergence distance, or a contested grab that was rejected on
a scenario that DID stage, would be the real thing.
## `Reconnect: grace window` — measured flake rate, FRAME-1, 2026-09-04 (Windows, loaded)

Second confirmation of the load-flaky entry above, and this one has a **rate** rather than a single
data point. FRAME-1's marathon went `OVERALL: FAIL` on `Netcode: anti-cheat` and
`Reconnect: grace window`, both already on the named list, and both discriminated by re-running
standalone 3x per side.

**`Netcode: anti-cheat` — 3/3 PASS standalone.** The marathon's failing check was the exact string
the list names: `scenario 2: prop 1 was never observed held by CheatCarrier - the grab never
landed`. The raw quantity did not move:

| run | peak 1.00 s window | displacement / samples | verdict |
|---|---|---|---|
| marathon | 3.81 m/s | 15.3 m / 58 | FAIL (scenario 2 staging) |
| standalone x1 | 3.80 m/s | 15.2 m / 59 | PASS |
| standalone x2 | 3.81 m/s | 15.2 m / 58 | PASS |
| standalone x3 | 3.82 m/s | 15.3 m / 59 | PASS |

**`Reconnect: grace window` — 2/3 PASS standalone, and the red REPRODUCED.** This is the useful
new fact: it is not "green when standalone", it is roughly **one in three under load**, with the
same single assertion every time:

```
post-resume avatarCount = 1, expected 2 (BotA + BotB)
  - stale/duplicate avatar node(s) left over from the pre-drop session
```

Marathon: FAIL, that assertion. Standalone x1: PASS. x2: PASS. x3: FAIL, byte-identical assertion.
So the discriminator for this suite is **not** "does it pass standalone" — a single standalone pass
proves nothing here. Re-run it at least 3x and read whether the assertion is the avatar-count one
(a join/teardown race) or something else.

**What else was running, both times:** six other Godot processes belonging to other worktrees and to
live playtest clients were up throughout, and BUBBLE-1's `Run-AllTests.ps1` out of
`C:\repos\Watis-bubble1` held the machine-wide mutex for ~23 minutes immediately before the
marathon started. **The machine was never idle for any of these runs**, which is why the rate above
is an upper bound on flakiness rather than a clean measurement.

FRAME-1's own change was a mesh, a material, one light and one guarded texture load in a sealed
underground room; it touches no netcode, no reconnect path and no avatar lifecycle.

## `World: tidal-cycle phase` — measured 2026-09-04/05 (STEAM-1)

**Measured**, not reasoned: 16 runs of `tests/Run-CycleTest.ps1` on one Windows machine, across two
worktrees of the same content.

- **Tip** (`feat/2026-09-04-steam-1-app-id-and-exe-name`, a diff of `.cfg` + `.ps1` + `.md` only —
  no C#, no scene): 1 marathon + 9 standalone = **8 PASS / 2 FAIL**.
- **Baseline** (a detached worktree at `playtest/2026-09-04-combined` @ `1f4d688`, untouched):
  **6 PASS / 0 FAIL**.
- The last three pairs were **interleaved** baseline/tip on the same quiet machine: 3/3 PASS each.

**Both tip failures happened while the machine was loaded** — the first immediately after a 48-suite
marathon, the second with two Godot processes still alive. Neither happened on a quiet machine. The
baseline was never run under comparable load, so this is **not** a demonstration that the base fails
too; it is a demonstration that the tip passes whenever the machine is quiet.

**The quantity to compare is the divergence, and it is bit-identical across every failure:**
`147 matched sample pairs, all within 1.5s`, and then exactly 5 consecutive cross-view pairs at one
moment reading **1.52416s** and **2.03222s** against the 1.5 s tolerance, plus one late-join pair at
**2.09722s**. The same three numbers appeared in the marathon and in standalone run 1. A real
phase divergence would wander; this is quantised — one client's phase sample lands one or two
snapshot ticks behind at a single instant (1.524 s is 0.127 of the 12 s period), and only under
scheduler pressure. **Read the divergence, not the verdict**: 147/152 pairs inside tolerance in
every run, failing and passing alike.

`--cycle-selftest` (the phase-math and light-curve half) printed PASS in **all 16 runs**, including
both failures. The failing half is only ever the live two-client comparison.

**Trap found while measuring:** `Run-CycleTest.ps1 -SkipBuild` on a **fresh worktree** dies with
`Cannot find path ...\tests\logs\cycle-selftest.out.log`. `-SkipBuild` skips `Reset-LogDir`, which
is what creates `tests/logs`. Run any suite once **without** `-SkipBuild` in a new worktree before
using `-SkipBuild` there; the failure looks like a suite red and is not one.
## `World: run driver` and `World: tidal-cycle phase` — MEASURED, 2026-09-05 (BUBBLE-2)

Both were on the "known load-sensitive in Sail's history, carried forward unverified" list above.
**They are now measured here, and the quantities are three orders of magnitude apart between a
loaded marathon and an idle machine — so both are load flakes and neither is a regression.**

Tip: `fix/2026-09-04-bubble-2-embedded-bubble` (a bubble-embedding self-test check, one bubble's
`position`, and docs). It touches no netcode, no cycle clock, no run spine and no avatar lifecycle.

| Suite | Marathon (loaded) | Standalone, idle, 3x | Quantity |
|---|---|---|---|
| `World: run driver` | **FAIL**, 2.7358796 s apart (tol 1.5 s) | **PASS, PASS, PASS** — 0.00093 s, 0.00139 s, 0.00069 s | post-reset cross-view phase divergence |
| `World: tidal-cycle phase` | **FAIL**, 10 assertions, 1.5244–2.0975 s (tol 1.5 s) | **PASS, PASS, PASS** — all 147 matched pairs within 1.5 s; late-join 0.098 / 0.557 / 0.590 s; rejoin 0.00056 / 0.00028 / 0.509 s | cross-view / late-join / rejoin phase divergence |

**Read the divergence, not the verdict.** A run-driver divergence of 0.0009 s and one of 2.74 s are
not the same test being flaky about the same thing — the second is a client that lost ~2.7 s of
wall clock to scheduling. Both suites compare two live clients' phase clocks against a fixed 1.5 s
tolerance, which is the shape that turns machine load into a red.

**What else was running.** During the marathon: unknown — STEAM-1 was live on this machine on the
same day and this session did not check at the time, which is itself the lesson (record it while
the suite runs, not afterwards). For the six standalone re-runs: `Get-Process` showed **zero**
Godot and zero dotnet processes before they started, and nothing else was launched during them.

## `World: tidal-cycle phase` fails on an IDLE machine too — the discriminator is the BASE, not the load (AVATAR-1, measured 2026-09-05)

BUBBLE-2's entry directly above measured this suite green 3/3 standalone on an idle machine and
concluded "loaded fails, idle passes". **That conclusion is too strong, and acting on it will burn
a session.** Measured here on the same base a day later: it fails standalone on a *verifiably*
idle machine, and it fails the UNMODIFIED BASE more often and more severely than the branch under
test.

Tip: `feat/2026-09-05-avatar-1-one-body` — removes the avatar picker from the two menu scenes and
their scripts. It touches no netcode, no cycle clock, no world and no avatar lifecycle. Base:
`playtest/2026-09-04-combined` @ `0385753`.

| Tree | Runs (standalone, idle) | Result | Worst cross-view divergence (tol 1.5 s) |
|---|---|---|---|
| Branch under test | 3 | PASS, **FAIL**, PASS | 1.5250 s (3 of 150 pairs, +1.7% over) |
| **Unmodified base** | 5 | PASS, **FAIL**, PASS, PASS, **FAIL** | **2.3781 s** and 2.0325 s |

`Get-Process` showed **zero** Godot processes before each of these eight runs and nothing else was
launched during them; the machine was genuinely idle. In the marathon that started this, the same
suite failed at 1.5247 s on 3 of 150 pairs while late-join (0.032 s) and rejoin (0.0011 s) passed
with three orders of magnitude of headroom.

**Two rules follow.**

1. **"Re-run it standalone on an idle machine until it goes green" is not a sound discriminator
   for this suite** — it is a coin flip with roughly a 1-in-3 tail, so three green re-runs prove
   little and one red re-run proves nothing at all.
2. **A/B against the base is the discriminator that actually decides it.** Run the same suite the
   same number of times in a detached worktree at your merge-base and compare the FAILURE RATE and
   the worst divergence. If the base is equal or worse, the red is not yours. That took eight runs
   and about twelve minutes here and turned an ambiguous `OVERALL: FAIL` into a settled question.

The 1.5 s tolerance is comparing two live clients' wall clocks; a scheduling hiccup of ~25 ms past
it is the entire failure. **Read the divergence and compare it to the base's** — the verdict alone
carries almost no information for this suite.

## The fork's own baseline, and a NEW teardown-crash symptom (BASE-1, measured 2026-09-19)

**This file came over from `Watis_Game` whole, and every dated section above it was measured
there.** Keep them: the suites they name (`Carry: server-authoritative`, `Carry: throw + loose`,
`Reconnect: grace window`, `Netcode: anti-cheat`, `Netcode: net-sim`, `World: tidal-cycle phase`,
`World: run driver`) all still exist here, unchanged, and their discriminators still apply. The
entries that name suites this repo no longer has (`Bike: handling model`, `Run-BubbleSyncTest`,
the LD-2/MOVE-1 env-var trap in `Run-MovementPlayground`) are kept as reasoning rather than as
live guidance — the env-var trap in particular generalises to any suite that sets a variable for
its child process, and the next one to do that will be one of ours.

**Baseline after the fork.** `tests/Run-AllTests.ps1` on `feat/2026-09-19-base-1`: **31 suites,
27 PASS / 4 FAIL**. `dotnet test tests/unit/SailNet.Tests.csproj`: **Failed: 0, Passed: 1254,
Skipped: 0, Total: 1254**.

All four marathon reds are on the load-flaky list above and all four were discriminated the
documented way — re-run standalone with `-SkipBuild` on a machine with zero Godot and zero dotnet
processes running (checked by process listing first):

| Suite | Standalone | The quantity |
|---|---|---|
| `Carry: server-authoritative` | 2/3 PASS | the red printed `C's grab never converged, so the disconnect-release was never staged` — the STAGING half, in its own words |
| `Reconnect: grace window` | 2/3 PASS | marathon red was the byte-identical `post-resume avatarCount = 1, expected 2`; the standalone red was NOT (see below) |
| `World: tidal-cycle phase` | 2/3 PASS | the red was cross-view `1.85416632s` against a 1.5 s tolerance — inside the 1.52–2.38 s band already measured twice above |
| `World: run driver` | **3/3 PASS** | marathon `2.7356483s`, standalone `0.2312500s` — an order of magnitude, the BUBBLE-2 discriminator exactly |

### The new thing: a bot client dying with `-1073741795` AFTER it finished its work

**Two different suites, two different runs, the same exit code on a `--bot` client process:**
`CycleRejoin exited with code -1073741795` in the marathon, and `BotA exited -1073741795` in one
standalone `Run-ReconnectTest.ps1`. `-1073741795` is `0xC000001D`, `STATUS_ILLEGAL_INSTRUCTION`.

**It is a TEARDOWN artifact, and the log says so.** `tests/logs/reconnect-botA.out.log` ends:

```
[client] connected as peer 790196959
[bot] BotA done
```

The bot connected, ran its full duration, printed its own completion line, and *then* the process
died on the way out. This is the same class as the `Bike: handling model` entry above — "a 20/20
PASS line above an `exited -1` is a teardown artifact, not a check" — one exit code further along.

**How to read it.** The runners gate on the child's exit code, so this presents as a suite red
with no failing assertion in it. Before treating one as a regression: open the bot's `.out.log`
and look for its completion line. If the work finished, the red is about process shutdown and not
about the thing under test. **Nothing in BASE-1's diff can reach either suite** — it touched no
file under `scripts/net/**`, no `CycleDriver`, and no part of the reconnect path.

**Not chased further here, and it should be.** Two occurrences in one afternoon on one machine is
a rate worth knowing, and an illegal-instruction crash is a different animal from a scheduling
flake. Whoever picks it up: capture the Windows fault log alongside the bot's stdout, and check
whether it only ever happens to a bot that has already printed `done`.

## ROUND-1's baseline, and a suite whose red is a DISTANCE (measured 2026-09-19)

`Run-RoundLoopSmoke.ps1` (port 7896) joins the list. It is a server plus two bots on
`--world supermarket`, driven end to end by `--round-script`, and **its failures are metre
readings rather than verdicts** — read them the way the carry family's are read.

Baseline on `feat/2026-09-19-round-1`, machine busy (another lane's suite held the mutex
immediately before):

| Quantity | Value |
|---|---|
| worst closest-approach to a server-named destination | **1.10 m** (bar 3 m) |
| server-decided moves checked against a bot's own log | 6 |
| phase transitions | 5 (`Holding->Hiding->Seeking->Together->Tally->Holding`) |
| named refusals the server logged | 1 (`HiderMustHoldAnObject`) |
| room separations, measured from the destinations the run used | holding↔search 38.08 m, task↔search 42.07 m, holding↔task 80 m, task↔vestibule **9.73 m** |

**The vestibule's 9.73 m is not a red.** It is a 3 × 3 m alcove authored behind the task room's
+Z wall — the far side of the door DOOR-1 bursts — so it is SUPPOSED to be adjacent. The suite's
20 m separation bar covers the three rooms only; the vestibule has a clearance bar against the
arrival radius instead. The first run of this suite failed on exactly that and the level was
right.

**How to read a red here.** A teleport that silently never happens produces closest approaches in
the TENS of metres (measured, on a deliberately planted fault: 38.08 / 80 / 31.05 / 76.07 m) and
both bots reported as never having left one room. A number that has crept from 1.1 m to 2.5 m is a
slow machine; a number in the tens is the thing this suite exists for. The phase, card and score
assertions stayed green under that same fault, so a red in those is a different animal again.

**`dotnet test` after ROUND-1: Failed: 0, Passed: 1296, Skipped: 0, Total: 1296.** BASE-1's
baseline was 1254; `RoundLoopTests.cs`'s 30 went with the loop they tested and 72 landed.

## One suite in the registry is not headless, and two capture traps that go with it (FP-1, 2026-09-19)

**`Run-FirstPersonTest.ps1` launches a WINDOWED client and every other suite does not.** It cannot
be headless: what it tests is what a camera renders and what a camera culls, and a headless process
has neither — the same law as the two editor-only `RenderingServer` read-backs above, one system
over. It opens a small window, runs twelve seconds and closes itself, so the marathon is still zero
human interaction, but **`Run-AllTests.ps1` now needs a desktop session**. Driven from a detached or
headless shell it will report that one suite red; its own failure message names a missing
display/GPU as the first suspect. CI is unaffected — the workflow runs only `dotnet test`.

**A `--first-person-cam` bot does NOT need `--capture-cam`.** The CELEBRATE-1 entry above says a
`--bot` client builds no camera of its own and so writes a flat grey frame under `--capture-dir`.
`--first-person-cam` (FP-1) builds the real `FirstPersonCamera` on the bot's own avatar and makes it
current, so the world renders. The rule that entry states is unchanged in substance: **a capture bot
must be given a camera**; there are now two flags that give it one, and `--capture-cam` still wins
when it is also present (`BotHarness` calls `MakeCurrent` after the avatar has attached).

**A capture bot that was given a camera can still photograph nothing, and it is not the camera's
fault.** Measured: the first green run of `Run-FirstPersonTest.ps1` produced a 14 KB frame of flat
dark grey with a correct HUD chip over it — indistinguishable at a glance from the flat-grey failure
above, and the suite passed on it. The cause was the BRAIN, not the lens:
`DeterministicWalkIntentSource` walks radially OUTWARD from the world origin, so in a 10 × 10 m room
it spends the entire run with its face 30 cm from a corner. `--goto-script 0,0` (walk to the middle
and stand there) turned the same run into a 69 KB frame of floor, wall, ceiling and light.
**Before believing a capture, compare its file size to a frame you know is good** — two orders of
magnitude of PNG is what "this is a photograph of a wall" looks like — and give a capture bot
somewhere to stand, not just something to look through. `--fp-look <yawDeg>[,<pitchDeg>]` (FP-1)
aims the lens, which a first-person capture needs because the brain no longer decides where the
camera points.

## A red written in the flake list's own vocabulary was a real regression (CARRY-1, measured 2026-09-19)

**The most useful thing measured in this packet, and it nearly went the other way.** CARRY-1's
first marathon went 30 PASS / 2 FAIL with `Carry: server-authoritative` printing:

```
bot A: observed A grab prop 1 at 1.00s but never observed the drop before the log ended
```

That is the staging half, in this file's own phrasing, on a suite this file already names as
load-flaky. The load-flaky entry would have explained it away. **It was a real defect in that
packet's diff**, and one line of the server log said so:

```
[server] place refused peer=979401151 prop=1 Overlapping (penetration 0.294 m)
         penetrates /root/Gameplay/Players/545392650 by 0.294 m (tolerance 0.020 m)
[server] place denied peer=979401151 reason=DoesNotFitThere
```

Two bots stood at the same crate; the holder's new place-vs-drop rule resolved E to PLACE because
its aim ray met the other bot's capsule, and the placement was then refused because the held crate
penetrated that capsule. Pressing "put this down" next to another player did nothing at all.

**So: read the failing check, then read the SERVER LOG, before reaching for this list.** A flake
list is a hypothesis about a red, not a verdict on it, and it is at its most dangerous when a real
regression happens to produce the same sentence. The discriminator that settled it cost one grep.

### Two measured numbers from the same packet

- **The holder-side carry spring's steady-state lag is 0.372 m mean / 0.403 m peak** at a
  sustained 3.6 m/s walk with a 1 kg crate (n = 83, off `tests/logs/carrydrift.jsonl`, samples
  from 3 s in). It matches the closed form `2v/omega` (0.386 m at omega = 18.65) to within 4%.
- **`Carry: drift (hold+walk)`'s caps were NOT touched by that, by luck rather than by design.**
  Its `MeanMax` is 1.15 m and `PeakMax` 1.45 m on `bd`, the prop's distance from the rendered
  BODY, against BASE-1's measured 1.010 / 1.063. A 0.37 m lag looked certain to blow through them
  and did not, because the lag points backwards along travel while the carry anchor sits in FRONT
  of the body — so the lag moves the prop toward the body and `bd` went DOWN, 1.010 -> 0.997.
  **That cancellation is a fact about where the carry anchor sits, not a property of the suite.**
  Anyone who moves the carry anchor behind or beside the body puts the whole lag straight into
  `bd` and should expect to re-measure these caps.

### `Carry: regrab-while-loose`, third confirmation

Failed once standalone with the documented staging string (`bot A never completed held -> loose ->
held-again for prop 2 (reached phase 2)`), then **3/3 PASS** immediately after on the same tip.
Worst hold distance 1.06 m on the holder's view and 1.06-1.11 m on the witness across the three
passes — inside the 0.89-1.09 m band already recorded. It also passed in both full marathons of
that packet.

## Three lanes off one base chose the same port, and a headless probe cannot capture a cursor (INT-0, measured 2026-09-19)

The wave-1 integration branch (`integration/2026-09-19-mvp` = BASE-1 + ROUND-1 + FP-1 + CARRY-1).
Two facts worth keeping, both measured, and the first is about how a wave is shaped rather than
about any lane's code.

### A "next free port" computed from a snapshot is not free

**ROUND-1, FP-1 and CARRY-1 all shipped a new suite on udp/7896.** Each branched off BASE-1,
each read the same `Run-CarryNetTest` 7893/7894/7895 ladder, and none could see the other two.
The merge appends all three near the end of `Run-AllTests.ps1`'s registry, so in a marathon they
run back to back on one socket. Measured: the first post-merge run of `Run-FirstPersonTest.ps1`
died at `the server never reported listening within 30s`, with `Couldn't create an ENet host` and
`err=CantCreate` in `tests/logs/fp-server.err.log`, immediately after `Run-RoundLoopSmoke.ps1`
released 7896.

**That red looks exactly like the port contention this file already excuses** (FP-1's own handoff
records a 7777 collision with a sibling worktree's Godot, correctly called contention and not a
red). The discriminator is WHOSE process holds the socket: contention is another worktree's Godot
and clears on a re-run; this is two entries of one registry and fails identically every time.
Settled by grepping every port in `tests/` — one line, and it is worth doing whenever a wave lands
more than one suite: ROUND-1 keeps 7896, FP-1 moved to 7897, `Run-PlaceTest` to 7898, and all 27
suite ports are now distinct.

### A headless probe cannot take the cursor, and both rigs correctly do nothing

`Input.MouseMode = Captured` is silently refused by the dummy DisplayServer in a `--headless` run;
the mode stays `Visible`. Both `HeldPropRotator` and `FirstPersonCamera` gate their mouse reads on
a captured cursor, so a headless input probe measures **two rigs that have each correctly decided
to do nothing** — and prints it as zero motion on every axis, which reads exactly like a routing
failure. Measured: INT-0's first `--rotate-look-selftest` run reported `0.0000 rad` on all four
quantities and failed two checks, **including both of its own positive controls**, which is the
tell. Windowed, the same probe prints `prop turned 2.0000 rad, camera yaw moved 0.0000 rad` with
the modifier held and `prop turned 0.0000 rad, camera yaw moved 0.5000 rad` with it released —
exactly `5 x 40 px x 0.010` and `5 x 40 px x 0.0025`.

**Generalises past the mouse: when every quantity in a probe reads zero, suspect the staging
before the subject, and check the positive controls first.** The probe now names the uncaptured
cursor as a staging failure in its own words rather than reporting it as a routing one. It is also
why `--rotate-look-selftest` is a one-off integration check and not a registered suite: it takes
the real cursor for the length of its run, which is what `Run-FirstPersonTest.ps1` deliberately
refuses to do.

### A first-person camera follows a ROUND-1 teleport, and the log could not see it before

`BotHarness` now logs this peer's own lens (`lens.off` = |avatar origin -> lens|) and the owner's
consumed epoch bumps (`peers[self].tp`). Neither existed, and without them a lens that had gone
stale, unparented or NaN across a room teleport reads in `peers[]` as a healthy body standing in
the right room. **Measured on the merged tree: eyeline offset min 1.040 m, max 1.040 m, spread
0.000 m over 225 samples per bot, and on all six room-scale jumps across two peers the lens moved
the same distance as the body to three decimals.** Proved able to fail with `TopLevel = true`
planted on `FirstPersonCamera.Attach`: spread 73.999 m, `lens moved 0.00 m` against body jumps of
40.63 / 42.09 / 80.16 m — while the epoch-bump half stayed green, which is what shows the two
halves are independent.

## The intercom's suite, the port grep done up front, and what its two windows discriminate (VOICE-1, measured 2026-09-19)

`Run-VoiceRoomTest.ps1` (port **7899**) joins the registry. Server plus two bots on
`--world supermarket`, driven by `--round-script "start@8,confirm@14"`; it asserts the voice
ROUTE each peer resolved for the other in two windows of one run.

**The port was grepped, not computed.** INT-0's entry above records three lanes off one base all
picking 7896 from the same stale snapshot. 7893–7898 were already claimed by the carry/round/
first-person ladder and a sibling lane in this wave had claimed 7901, so the grep — one line over
`tests/` — chose 7899. **Do this whenever a wave lands more than one suite, and record the claim
here in the same commit as the suite**, which is the half INT-0 could not do retroactively.

**Its windows are selected by `roundPhase` in the bots' own samples, never by a typed second.**
Holding = same room, Seeking = cross room. There is not one wall-clock guess in the assertions.

### The two failures it discriminates, both planted and measured

| Plant | Same-room window | Cross-room window | Relay counters | What it proves |
|---|---|---|---|---|
| *(none — the shipped tree)* | proximity / proximity, rooms `holding`/`holding` | pa / pa, rooms `task`/`search`, bodies **42.1 m** apart | relayed 1628, **PA-exempt 1301, gated 0**, listener 1614 packets / **248 in the final 6 s** | PASS |
| **client-only wiring** (`PaPairResolver = null`, server `PaResolver = _ => false`) | **still green** | **still green** | relayed 639, **PA-exempt 0, gated 1001**, listener 384 packets / **0 in the final 6 s** | FAIL — 4 assertions |
| **everything exempt** (`PaPairResolver = (_,_) => true`, client `PaResolver = _ => true`) | **pa / pa** — FAIL | still green | relayed 1637, PA-exempt 1637, gated 0 | FAIL — 2 assertions |

**Read the middle row.** That is the hazard `VoiceProximityGate`'s header names — a relay that
does not know who is on the PA silently mutes the intercom — and **both clients' routing verdicts
read perfectly correct while it was happening.** Every route said `pa`, every room was right, and
not one packet arrived. A suite that asserted only the route would have passed that build, which
is why the server's own `paExempt`/`gated` counters and the listener's final-window delta are
asserted beside it. The bottom row is the converse and is why the same-room window exists at all:
an intercom that "worked" by exempting everything puts a reverb on two people standing face to
face, and the cross-room half stays green through it.

**So the three quantities to compare across runs are:** the listener's packets in the final 6 s
(248 green / 0 muted), the server's `paExempt` (1301 green / 0 muted) and the measured body
separation (42.1 m — if that drops under the gate's 30 m enter radius the window proves nothing
and the suite says so in its own words).

**One piece of noise to expect in a FRESH worktree:** the first `godot --import` prints
`Cannot open file 'res://.godot/imported/Sora.woff2-….fontdata'` and four following font/theme
errors, then imports them. It is a first-run artifact of an empty `.godot/imported`, appears on
stderr above a suite that then passes, and is not this suite's.

**Baseline after VOICE-1: `dotnet test` Failed: 0, Passed: 1342, Skipped: 0, Total: 1342.**
INT-0's was 1317; the 25 new ones are `VoiceRoutingTests.cs`.

## A frame-sampled beat runs EARLY, and a capture harness distorts its own marks (DOOR-1, measured 2026-09-19)

`Run-BurstDoorTest.ps1` (port **7900**) joins the registry. Server plus two bots on
`--world supermarket`, driven by `--round-script`, with a `--startle-file` overlay handed to all
three; **its failures are millisecond readings and metre readings rather than verdicts.** Baseline
on `feat/2026-09-19-door-1`, machine otherwise idle:

| Quantity | Value |
|---|---|
| peers that burst | 3 of 3 |
| worst \|own burst − (own arm + TellSec)\| | **3–12 ms**, always LATE (bar 50 ms) |
| burst spread across three processes | **4–11 ms** (bar 150 ms) |
| seeker's deepest z while the blocker held | **5.55 m** against the wall's 5.4 m vestibule face |
| seeker's deepest z after the burst | **1.12–1.14 m** against the 5.0 m task-room face |
| best prop movement inside the burst radius, on a CLIENT's log | **0.99–1.38 m** (bar 0.3 m) |

### The port ladder now reads 7893/7894/7895 (CarryNet) · 7896 (RoundLoop) · 7897 (FirstPerson) · 7898 (Place) · 7899 (VoiceRoom) · **7900 (BurstDoor)** · 7901 · 7902+ (SFX-1)

**INT-0's "three lanes off one base chose the same port" happened again one wave later, with the
ladder comment already in place, and the missing sentence is this: a port is not claimed until it
is on `origin`.** DOOR-1 read the ladder, took the next gap (7899), and wrote it into three files;
VOICE-1's `Run-VoiceRoomTest` had landed on 7899 from its own worktree in the meantime. Neither
lane could see the other and both were reading correctly. Two concurrent lanes computing "the next
free port" from the same `tests/` will pick the same one **every** time, so the only reliable
protocols are to fetch and re-grep immediately before committing, or to have the orchestrator hand
each lane a number.

### A beat that accumulates `_Process` deltas fires EARLY by up to one frame

**Measured, and it is bigger than it sounds.** The burst door's staging originally summed frame
deltas from the tick the Found message landed. That message arrives inside network polling,
part-way through a frame; the next `_Process` then hands over the **whole** of that frame's delta,
most of which elapsed before the message existed. So the beat runs early by up to one frame time.

| Run | Configured tell | Measured arm → burst |
|---|---|---|
| headless smoke, 3 peers | 1.200 s | 1.191 / 1.196 / 1.199 s (early by 1–9 ms) |
| **windowed capture client**, whose first post-arm frame also took a viewport screenshot | 1.200 s | **1.070 s — early by 130 ms, 11 % of the beat** |
| after the fix (`Time.GetTicksUsec` read at the arm), headless smoke | 1.200 s | 1.203 / 1.205 / 1.212 s |

**Read the direction, not just the magnitude.** A frame-sampled event can honestly only ever be
LATE, by less than a frame; an early one means time is being counted that the event had not
happened for yet. Any beat keyed to a network message and stepped by `delta` has this, and the fix
is one clock read rather than a running sum.

### A viewport capture costs ~0.18 s, so a dense `--capture-at` grid reports the wrong times

A 0.1 s grid of marks queues behind itself (`BotHarness` fires at most one mark per frame and
gates on `_capturing`), and the lag accumulates: over seven marks it reached **1.0 s**, which put
the burst a full second from where the log ordering said it was and produced four "frames of the
burst" that were all after it. **A 0.25 s grid does not queue** — measured mark-to-file offsets
were 0.250 ± 0.003 s across 22 marks.

Two things that go with it, both cheap:

- **A PNG's mtime is the capture's wall clock**, and it is the only cross-process anchor a capture
  has. Diff it against the subject's own `wall=` log stamps rather than inferring order from
  interleaved stdout.
- **The offset between a bot's `--capture-at` clock and the round's clock is the CONNECT time, and
  it moved by a full second between two runs of one script.** Four marks computed from one
  calibration run therefore land somewhere else on the next one. Shoot the grid, measure, keep the
  four frames you wanted and delete the rest — the alternative is three runs of guessing.

## A second suite needs a desktop, and four ways a sound can be inaudible to somebody (SFX-1, measured 2026-09-19)

`tests/Run-MaterialSfxTest.ps1` joins the registry on **udp/7902**. Continuing the ladder INT-0
settled: 7893/7894/7895 `Run-CarryNetTest`, 7896 `Run-RoundLoopSmoke`, 7897 `Run-FirstPersonTest`,
7898 `Run-PlaceTest`, **7902 `Run-MaterialSfxTest`**. It starts at 7902 rather than 7899 on
purpose — wave-2 lanes this worktree could not see had claimed 7899 and 7901 and DOOR-1 was
taking another while this was written, so the whole 7899–7901 band was left alone. INT-0's rule
stands and this is the second lane to pay for it: **a "next free port" computed from a snapshot
of `tests/` is not free while a wave is live.**

### `Run-AllTests.ps1` now needs a desktop session for TWO suites, and the reason is not the same

FP-1's entry above says its suite opens a window because what it tests is what a camera renders.
This one opens three (two in phase 1, one in phase 2) because of an **early-out**, not a
renderer: `ActorFx.Fire` returns immediately when `NetworkManager.IsHeadless`, and
`IsHeadless` is `DisplayServer.GetName() == "headless"` — **a property of the display, not of the
`--server` flag.** So a headless `--server` and a headless `--bot` both play nothing, and a
**windowed `--server` is a host who is a player**, which is also the only peer that simulates
loose prop physics. Any future audio suite has to put a window on whichever peer it wants to
listen with, and if it wants to hear an IMPACT that peer must be the simulating one.

### Four ways a prop sound was inaudible, all found by one suite, all one-sided

Worth keeping because each one produced a *partly* working feature, which is the kind that ships.

| What was measured | Why it happened |
|---|---|
| Props thrown into a **wall** sounded (4.0 and 6.3 m/s); four props dropped 0.6–1.1 m onto the **floor** were silent, every run | `LinearVelocity` read inside `body_entered` has already had the normal impulse applied for a head-on landing. A glancing contact leaves residual velocity; a landing does not. Sample the speed going INTO the physics step (`Carryable.ApproachSpeedMps`). |
| Every impact audible to the **host**, none to the other player | A Loose prop on a non-authority peer is a **frozen kinematic body** lerped toward a streamed transform: `LinearVelocity` is 0 forever, and Godot reports such a body no contact at all. `ObservedSpeedMps` fixes the first half; the second half needs a bit on the wire. |
| Every **throw** audible to the host, none to the other player | `Carryable.OnThrown` is reached only from `NetworkedProp.BeginLooseServer`, which is server-only. The every-peer half of the same transition is `BeginLoose` (via `ApplyPropState`, `CallLocal`). |
| A can dropped on an already-settled can made **no sound at all** | A "lower instance id wins" de-dup picks the winner before knowing whether the winner will be asked — and a settled prop is frozen kinematic, so Godot never asks it. **A first-come claim cannot fail that way.** |

**Generalises: a de-duplication rule that decides its winner up front is silent whenever only the
loser is asked.** The failure looks identical to "the feature does nothing" and passes any test
that counts sounds near a position, because the count it is looking for is 1 and the count it
gets is 0 only in the cases nobody staged.

### A positive control stopped being able to fire, silently, because a constant moved

`FootstepAudioTests.SixSprintingPlayersStayInsideTheOneShotBudget`'s control read
`uncapped > SfxLab.PoolSize - 6`: true at a pool of 14 (12 > 8), false at 18 (12 > 12). SFX-1
raised the pool and the control went red **while every safety assertion above it stayed green**,
which is the good outcome of a badly-written one. It is now stated against the cap
(`uncapped > culled`), which is pool-size independent. **A positive control written against an
absolute constant expires the day that constant moves; write it against the thing it is
controlling for.**

### The voice budget under forty simultaneous impacts, and a bar that pool size cannot reach

Measured, phase 2 — forty mixed props released together with two players walking through them,
which is the shelf-collapse event the aisles will produce:

| PoolSize | fires | PeakLive3DVoices | OneShotSteals | stolen |
|---|---|---|---|---|
| 14 | 54 | **14** | 26 | **48 %** |
| 18 | 56 | **18** | 22 | **39 %** |

**The peak landing exactly ON the pool size both times is the tell** — the pool saturated, rather
than the mix happening to want that many. The 18 was paid for out of `SfxLab.LoopPoolSize`
(5 → 1): four of those slots were reserved for an outdoor fire bed in a game about a forest at
night, and this one is a supermarket. `AudioVoiceBudget.Ceiling` is untouched at 24.

**The 10 % steal bar is not reachable by pool size at this fixture and the suite says so rather
than chasing it**: forty simultaneous impacts want forty voices. It reports the design bar and
gates on a 60 % regression bar. The fix is source-side limiting — the same answer
`FootstepAudioDirector` already gives for six sprinting players — and it is a design call.

### `Voice: proximity gate` is load-flaky too, and `Carry: drift`'s red is a STAGING red (SFX-1, measured 2026-09-19)

SFX-1's marathon on `feat/2026-09-19-sfx-1`: **35 suites, 33 PASS / 2 FAIL.** Both reds
discriminated the documented way, and the machine was **not** idle for either the marathon or the
re-runs — `C:\repos\sfgd-reach1` (REACH-1) held the machine-wide mutex for four minutes
immediately before the re-runs and two Godot processes belonging to it were already alive, which
per the SHADER-2 entry above makes the clearing stronger rather than weaker.

| Suite | Marathon | Standalone, `-SkipBuild`, machine busy | The quantity |
|---|---|---|---|
| `Carry: drift (hold+walk)` | FAIL | **3/3 PASS** | mean **0.997–0.998 m**, peak **1.067–1.071 m**, growth 0.001–0.002 m, n=83 |
| `Voice: proximity gate` | FAIL | **3/3 PASS** | vgate-pa: far **1066–1101** packets at 45.7 m, ~2195 relays taken by the PA exemption, 0 gated |

**`Carry: drift` is not on the flake list above and its red belongs to the family that is.** The
failing lines are `prop 1 not held by bot ... during walk window` and `too few
held-while-walking distance samples (0)` — the staging half, in the suite's own words, and
`tests/logs/carrydrift.server.out.log` names the cause outright:

```
[server] grab denied peer=390899326 reason=OutOfRange
```

The bot never got within `GrabRange`, so the drift measurement had nothing to measure.
**CARRY-1's warning above was followed rather than skipped**: that packet's red was written in
this file's own flake vocabulary and turned out to be a real regression, and the discriminator
was one grep of the server log. Here the grep says `OutOfRange` — a distance, i.e. the bot's walk
— where CARRY-1's said `DoesNotFitThere` / `Overlapping`, a refusal its own diff had introduced.
And **the measured quantity did not move**: INT-0 recorded mean 0.997 / peak 1.071 / n=84 on the
merged base, against 0.997–0.998 / 1.067–1.071 / n=83 here. A `Carry: drift` red whose
`body-distance` numbers have moved is the real thing; one whose grab never landed is not.

**`Voice: proximity gate` is new to this list.** Its red is `FAIL: vgate-pa: a bot exited 1`, and
the speaker bot's own JSONL stops at **t=9227 ms of a 22 s run** — it died mid-run rather than
after finishing, so it is NOT the `-1073741795`-after-`[bot] done` teardown artifact BASE-1
recorded, and its `.err.log` is empty. Read the failing PHASE first: phases 1 and 2 both printed
`ok` in the same failing run, so a red here that names `vgate-on` or `vgate-off` is a different
animal from one that names a bot exit code.

## REACH-1: udp/7903, and four measured things about the placement audit (2026-09-19)

### The port ladder, written down rather than recomputed

`tests/Run-ReachTest.ps1` claims **udp/7903** for all four of its phases. The ladder as it
stands, which is the list to read before picking the next one:

| Port | Suite |
|---|---|
| 7893 / 7894 / 7895 | `Run-CarryNetTest.ps1` (contention / authority / teleport) |
| 7896 | `Run-RoundLoopSmoke.ps1` (ROUND-1) |
| 7897 | `Run-FirstPersonTest.ps1` (FP-1) |
| 7898 | `Run-PlaceTest.ps1` (CARRY-1) |
| 7899 | VOICE-1 |
| 7900 | DOOR-1 |
| 7901 | claimed in the same wave |
| 7902 | SFX-1 |
| **7903** | **`Run-ReachTest.ps1` (REACH-1)** |

7903 was **given by the orchestrator, not computed from a snapshot of `tests/`** — which is
INT-0's lesson above applied rather than re-learned. 7899-7902 do not appear in this branch's
`tests/` at all (their suites are on unmerged lane branches), so a grep here cannot see them and
would happily have produced 7899.

### `$Args` is an automatic variable, and a function parameter of that name is silently empty

Measured: the first run of `Run-ReachTest.ps1` launched three servers with `function
Start-ReachServer([string]$Tag, [string[]]$Args)`. Every one of them started, printed
`[graphics] tier Medium (headless default)`, and then sat there forever — **with none of the game
flags after `--`**, because the parameter never received what the caller passed. The suite failed
at `the server never reported listening on udp/7903`, which is the same sentence a real bind
failure produces and the same sentence INT-0's port-collision defect produced. The discriminator
is the server log: a bind failure prints `Couldn't create an ENet host`; this printed nothing at
all after the graphics line, because the process was not a server. **Never name a PowerShell
function parameter `Args`.**

### A `.ps1` in this repo must be ASCII

`Run-ReachTest.ps1` was first written with em dashes and a section sign in its comment block.
Windows PowerShell 5.1 reads a BOM-less file as ANSI, so the multi-byte characters came back as
mojibake and the parser died with a cascade that pointed at line 190 — `The Try statement is
missing its Catch or Finally block`, 200 lines away from anything that was actually wrong. Every
other `.ps1` in `tests/` is pure ASCII; that is not an accident and it is now written down. (The
C# side is UTF-8 and unaffected: `GD.Print` lines with em dashes in them are fine, and several
already ship.)

### The rest audit's measured cost, 150 props

`Run-ReachTest.ps1` phase 4, on this machine with the two-bot session live:

```
[reach-cost] props in world: 154, shove waves: 6 (every 3.0 s at 2.5 m/s)
[reach-cost] rest audits: 924 in 20.00 s = 46.2/s (0 correction(s))
[reach-cost] integrity queries: 924 in 20.00 s = 46.2/s
[reach-cost] server physics frame time over 1200 tick(s): p50 2.765 ms, p95 20.321 ms, peak 50.580 ms
```

**Exactly one shape query per settle event**, which is what program §5b costed layer 2 at, and the
audit's own share of the tick is negligible: 46 queries a second against a 60 Hz tick is under one
query per tick. The p95 of 20 ms is **150 rigid bodies being simulated**, not the audit — the
shove wave is what costs it, and a real session never shoves 150 props at once. Read it as an
upper bound on the load, not as a budget for the audit.

154 props, not 150: the 150 the suite seeds plus the four CARRY-1 authored into the search
room. The suite's own population check asserts AT LEAST the seeded count for that reason, and it
was measured the hard way -- an equality check went red at "expected exactly 150" and the four
extra crates were the level. SHELF-1's hundred will move the number again.

**Zero corrections in 924 audits is itself the finding.** Props shoved across an open floor settle
legally; the audit's correction path does not fire under ordinary load, which is what it should
look like. The correction branches are exercised by the planted room instead, where the fixtures
are authored into the defects deliberately.
