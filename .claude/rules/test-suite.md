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
