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
  dated section at the end of this file), and **`Carry: place + integrity`** and
  **`Round: buttons + drop-off bin`** (INT-1, 2026-09-19 — the placed pose is 0.008 m / 0.00 deg
  standalone and tens of mm / tens of deg when it reds; the buttons red says `TooFarAway` where
  `NotNow` was staged. Both have their own dated section below).

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

### The port ladder as DOOR-1 saw it — **superseded**: one ladder table now lives in the
### REACH-1 section below (INT-0B, 2026-09-19). DOOR-1 kept 7900; the reasoning below stands.

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

### `Carry: drift`'s red is a STAGING red, and a second red was an EXTERNAL KILL (SFX-1 2026-09-19; corrected by SFX-2 2026-09-19)

SFX-1's marathon on `feat/2026-09-19-sfx-1`: **35 suites, 33 PASS / 2 FAIL.** Both reds
discriminated the documented way, and the machine was **not** idle for either the marathon or the
re-runs — `C:\repos\sfgd-reach1` (REACH-1) held the machine-wide mutex for four minutes
immediately before the re-runs and two Godot processes belonging to it were already alive, which
per the SHADER-2 entry above makes the clearing stronger rather than weaker.

| Suite | Marathon | Standalone, `-SkipBuild`, machine busy | The quantity |
|---|---|---|---|
| `Carry: drift (hold+walk)` | FAIL | **3/3 PASS** | mean **0.997–0.998 m**, peak **1.067–1.071 m**, growth 0.001–0.002 m, n=83 |
| `Voice: proximity gate` | FAIL | **3/3 PASS** | **WITHDRAWN — see the note at the end of this section (INT-0B, 2026-09-19): the red was an external kill, not a flake** |

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

> **WITHDRAWN — `Voice: proximity gate` is NOT load-flaky** (INT-0B, 2026-09-19, on the
> orchestrator's ruling; SFX-2 withdraws the same entry on its own branch and had not landed when
> this tree was built, so it is withdrawn here). **A bot that stops mid-run with an empty
> `.err.log` is what an EXTERNAL KILL looks like** — another session reaching for a Godot
> process that was not its own — and that is what happened to SFX-1's run. It is not a property
> of this suite, and leaving it on the flake list would teach the next lane to explain away a
> real red here. SFX-1's paragraph above is kept verbatim rather than deleted (this file's own
> header forbids deleting another agent's entry) and this note is the correction. **The
> discriminator that matters is the one the rest of that paragraph already gives: read the
> failing PHASE.** A red naming `vgate-on` or `vgate-off` is about the gate; a bot exit code is
> about the machine, and if the bot's own log stops mid-run, look for a neighbour's kill before
> looking at the suite. **Stop Godot only by PIDs you recorded; never `taskkill /IM`, never
> `Get-Process godot* | Stop-Process`** — that rule is what this entry cost.
>
> **The clean datum, folded in at INT-1 from SFX-2's own re-run** (SFX-2 withdrew the same entry
> on its branch; this note is INT-0B's wording and SFX-2's numbers, because the two agreed about
> everything except which paragraph said it). Standalone under the mutex on a quiet machine,
> **PASS, uncontended, first attempt**: far peer **1066–1101 packets at 45.7 m**, ~2195 relays
> taken by the PA exemption, 0 gated — inside SFX-1's own band, so **nothing about this suite
> ever moved.** SFX-2's handoff §5.5 has the raw block. One more generalisation from its side
> that is worth keeping: **before writing a suite into this file, check the other live lanes'
> handoffs for the same clock minute** — the cause of this red was recorded in REACH-1's §8 and
> in no log SFX-1 could reach.

## The xUnit suite has a parallel-collection race in it, and any new test anywhere can trip it (INT-1, measured 2026-09-19)

**`dotnet test` is not deterministically green either, and for a reason that has nothing to do
with load.** The first full run on the tree with TASK-1 merged was:

```
Failed!  - Failed: 1, Passed: 1652, Skipped: 0, Total: 1653
  Failed SailNet.Tests.AnticipationCoilTests.TheLiveGravityForActuallyReadsTheModeAndTheTwoBakedKnobs
  Assert.Equal() Failure
  Expected: 31.9758396
  Actual:   24
```

**24 is `AvatarMotor.Gravity` with the launch-coil factor absent** — the live motor reading a
tuning that was not the one the test applied two lines above it. Re-run on the same tree,
unchanged, **13 times: 13 PASS at 1653.** One red in fourteen.

**It is not load and it is not the merge.** Nothing in wave 2 touches gravity, the coil or the
tuning. The cause is structural and is visible without running anything:

- `MotorTuning.Current` is ONE process-wide static and is what the live `AvatarMotor` reads.
- **Three test classes write it** — `AnticipationCoilTests`, `MotorTuningTests`,
  `MotorTuningSessionTests` — 27 call sites of `TryApply` / `SetSessionProbeForTests` /
  `ResetForTests` between them.
- Each parks the value and restores it in a `finally`, which is correct WITHIN a class and
  defends against nothing: **xUnit gives every class its own collection and runs collections in
  parallel**, and there is no `xunit.runner.json` in this project turning that off. A second
  class can apply a different tuning BETWEEN two lines of the first one's test.

**What changed was the SCHEDULE, not the code**, and that is the part worth carrying forward:
69 new tests landed in an unrelated file and moved the thread timing enough to surface a latent
race. **A test that parks a process-wide static is a trap armed for whoever adds the next test,
anywhere in the suite** — it has no failure of its own to find and nothing in review looks wrong.

**Fixed at INT-1** with `tests/unit/MotorTuningStaticsCollection.cs`: one
`[CollectionDefinition]` and the attribute on all three classes, so xUnit runs them one at a
time. No assertion changed and the suite's wall clock did not move measurably. **If you write a
fourth class that touches `MotorTuning`, put the attribute on it** — and treat any other
process-wide static a test parks the same way.

**Honest about what is and is not proved.** The mechanism is established by construction (one
static, three writers, no serialisation) and the fix removes the concurrency the mechanism needs.
It is NOT proved by measurement that the flake is gone: at one failure in fourteen runs, a green
streak after the fix is not evidence at that rate, and claiming otherwise would be exactly the
absence-without-a-positive-control this file warns about elsewhere. If
`TheLiveGravityForActuallyReadsTheModeAndTheTwoBakedKnobs` is ever red again, the first thing to
check is whether a fourth class has started writing the tuning without joining the collection.

### A bot that crashes AFTER printing `done`, with the stack BASE-1 asked for (INT-1, 2026-09-20)

BASE-1's entry above closes with a request: *"capture the Windows fault log alongside the bot's
stdout, and check whether it only ever happens to a bot that has already printed `done`."*
**Both halves, answered.** `Netcode: arrive latch` was red in INT-1's second marathon with

```
FAIL: LatchBot exited with code -1073741819
```

`-1073741819` is `0xC0000005`, `STATUS_ACCESS_VIOLATION` — a different fault from BASE-1's
`0xC000001D`, same family. **Yes, it had already finished.** `arrivelatch.latch.out.log` ends:

```
[client] connected as peer 869539124
[bot] LatchBot running as peer 869539124 for 40s
[bot] LatchBot done
```

195 JSONL samples written, full duration served. **And the stack names the phase outright** —
`arrivelatch.latch.err.log`:

```
0xC0000005
   at Godot.NativeInterop.NativeFuncs.godotsharp_internal_object_get_associated_gchandle(IntPtr)
   at Godot.GodotObject.Dispose(Boolean)
   at Godot.GodotObject.Finalize()
   at System.GC.RunFinalizers()
```

**A .NET FINALIZER touching a Godot object after the native side is gone.** That is teardown by
construction: `GC.RunFinalizers` at shutdown, a managed wrapper whose native peer has already
been freed, and a null gchandle lookup. Nothing a suite asserts can reach it, and nothing under
test is running by then. **3/3 PASS standalone** on the same tree immediately afterwards.

**So the rule BASE-1 wanted is now earned rather than suspected: read the bot's `.out.log` for
its completion line, and its `.err.log` for a `Finalize`/`Dispose`/`RunFinalizers` frame. Both
present = teardown, re-run and move on. Either absent = a real crash, and the JSONL's last
sample says how far it got.** This suite also runs under `--netsim latency=60ms loss=80%
jitter=800ms` by design, so it tears down more objects under more pressure than most.

## `Sfx: material voices` loses ONE driver intermittently, and the cause is NOT the spawn race (INT-1, measured 2026-09-19/20)

**SHELF-1 named this suite's intermittent red and named a cause. The cause is wrong, and it is
ruled out here by measurement rather than by argument.** SHELF-1 §8 recorded 2 of 3 runs each
losing exactly one driver, a different one each time, and attributed it to spawn markers being
dealt by join order (§6.3). INT-1 added `--spawn-index` (ruling 6) and pinned all five of this
suite's bots by NAME. **The pins demonstrably applied** — the server logs them —

```
[server] spawn pinned peer=1070851738 name=SfxCanBot     marker=0 at=(35.50,1.10,0.00) moved=True
[server] spawn pinned peer=850499413  name=SfxBoxBot     marker=2 at=(38.50,1.10,2.10) moved=True
[server] spawn pinned peer=1451927024 name=SfxProduceBot marker=3 at=(41.50,1.10,-2.10) moved=True
```

**— and a driver was still lost.** Tally on the merged tree: **3 PASS, 2 FAIL across five runs**,
always exactly ONE driver, a different one each time (marathon: `produce`; standalone:
`cardboard`), and **no run ever played the WRONG sound.**

### What it actually is, from the two failures' own traces

| | marathon (produce lost) | standalone (cardboard lost) |
|---|---|---|
| pinned to | marker 3 `(41.50, -2.10)` | marker 2 `(38.50, 2.10)` |
| its prop at | `(38.0, 0.35, -2.1)` | `(42.0, 0.35, 2.1)` |
| came to rest at | `(38.02, 0.00, -0.65)` | `(41.99, 0.00, 3.55)` |
| **closest approach** | **1.49 m** | **1.49 m** |
| `heldPropId`, ever | −1 | −1 |
| motionless for | the rest of the run | 18 s of a 26 s run |

**1.49 m in BOTH, to the centimetre. That is a geometric constant, not a race.**

```
ScriptedCarryIntentSource.ArriveRadius = 1.2f
    if (dist <= ArriveRadius && _clock >= _earliestGrabSec)   // the grab intent is formed HERE
```

The bot walks AT its prop; the prop is a `RigidBody3D` and **a `CharacterBody3D` does not push
one** (SHELF-1 §6.2), so it wedges between the prop and the adjacent bay and stops 1.49 m away —
**0.29 m outside the 1.2 m arrive radius.** `dist <= ArriveRadius` is therefore never true, the
grab intent is never formed, and **the server log contains no `grab denied` line at all**,
because — TASK-1 §3.3 — *a press that is never made is never refused.* The suite then reports
`prop N never made a sound at all`, which is an assertion about AUDIO describing a bot that never
reached its object.

### The fix is one TASK-1 already found, one file over

TASK-1 §3.3 hit this exact wall with its own intent source and wrote the generalisation down:
*"an arrive radius is a fact about the FURNITURE the target sits on."* It raised
`ScriptedSortIntentSource.FetchStandM` from the copied 1.2 to **1.8 m, DERIVED from the server's
reach** (`SandboxAvatar.PickupRadius` 1.5 + `PropManager.GrabRangeTolerance` 0.75 = 2.25 m in 3D)
rather than guessed. **`ScriptedCarryIntentSource` never got the same treatment**, and it is the
brain four suites drive. Raising its `ArriveRadius` the same way is the fix.

**Deliberately NOT done at INT-1, and the reason is the blast radius rather than the size.** That
constant is shared by `Run-PlaceTest`, `Run-CarryNetTest`, `Run-MaterialSfxTest` and
`Run-CarryDriftTest`; raising it moves where every one of their bots comes to rest, and several
of them assert on a distance. That is a change that wants its own packet and its own four-suite
re-run, not a one-line edit at a wave's final gate. **Handed to the orchestrator.**

### REVIEW-1 (2026-09-20): the wedge is real, the PROPOSED FIX was wrong, and the bar is the CLIENT's

**INT-1's diagnosis above is confirmed. Its prescription — "raise `ArriveRadius` the same way
TASK-1 did, to 1.8 m derived from the server's 2.25 m reach" — is RULED OUT BY MEASUREMENT**, the
same way INT-1 ruled out SHELF-1's spawn race. Raised to 1.8 m, two OTHER suites in the group of
four went red on the first sweep:

| Suite | What it printed | What actually happened |
|---|---|---|
| `Run-CarryNetTest` phase 2 | `STAGING: DropC did not disconnect while holding -- it never held the ball at all` | the bot stood **1.482 m horizontally / 1.564 m in 3D** from a ball at y = 0.5 for 28 s with `heldPropId = -1`, and **the server logged nothing at all** |
| `Run-PlaceTest` | `bot D: prop 1016 holder=0 in the final sample` | D pressed 0.6 m further out, its NAMED prop fell outside the client's pickup radius, nearest-carryable ran instead, and it came away holding **1027 — a shelf product** (`place refused peer=… prop=1027 OutsideRoomBounds`) |

**There are TWO bars between a scripted press and a held prop, and the SMALLER one binds.**

```
SandboxAvatar.FindNearestCarryable   PickupRadius            1.5 m, 3D, from the avatar's ORIGIN
PropManager.RequestGrab              GrabRange               2.25 m, 3D  (= PickupRadius + 0.75)
```

`FindNearestCarryable` is the CLIENT's, it runs first, and `ScriptedGrabPropId` honours a named
prop **only inside that same 1.5 m** — its own doc says it "narrows the choice, never widens the
reach". Outside 1.5 m the press composes **no request**, so:

- **the 2.25 m server reach is unreachable by a press the client declined to send**, and
- **"no `grab denied` line" does not only mean "a press that was never made" any more.** It also
  means "a press that was made and found nothing". TASK-1 §3.3's sentence still holds; this is a
  second way to produce the same silence, and the discriminator between them is the bot's own
  closest approach against **1.5 m in 3D**, not against any arrive radius.

**So `ScriptedCarryIntentSource.ArriveRadius` stays at 1.2 m and it is now DERIVED**: a prop rests
0.2–0.5 m above the avatar's origin, so the client's 1.5 m in 3D is 1.41–1.48 m horizontally, and
1.2 m leaves 0.2 m of slack for the settle and a frame of prediction error. `ScriptedSortIntentSource`'s
1.8 m is safe only because its sortables sit on a 1.4 m plinth that stops the bot well inside it;
the horizontal number overstates the 3D one it is really spending.

**The wedge is fixed at the PRESS instead.** 1.49 m in 3D is *inside* the client's 1.5 m radius —
the wedged bot could have grabbed, it simply never asked, because the arrive branch it asks from
is gated on a 1.2 m horizontal walk it can no longer complete. `ScriptedCarryIntentSource` now
presses **while still walking**, on the `--carry-grab-retry` cadence, as soon as the 3D distance
is inside `SandboxAvatar.PickupRadius` — the client's own question, referenced rather than copied.
The walk is untouched, so no bot's resting position moves, which is what makes it safe for the
four suites that measure distances against the arrive radius.

**Before / after, the four suites that share the constant** (standalone, `-SkipBuild`, one lane on
the machine, nothing else running):

| Suite | Before | After |
|---|---|---|
| `Run-CarryDriftTest` | mean **0.997** m, peak **1.067** m, growth 0.000 m, n = 83 | mean **0.997** m, peak **1.071** m, growth 0.000 m, n = 83 |
| `Run-CarryNetTest` | worst hold 1.01 m own / 1.07 m witness; teleport worst 1.00 m over 104 samples | worst hold **1.01** m own / **1.09** m witness |
| `Run-PlaceTest` | placed **0.008 m / 0.00 deg**; spring-vs-anchor mean 0.047 m, peak 0.759 m, n = 280 | placed **0.008 m / 0.00 deg**; spring-vs-anchor mean **0.048** m, peak **1.099** m, n = 281 |
| `Run-MaterialSfxTest` | PASS | PASS |

**The one number that moved is `Run-PlaceTest`'s spring-vs-anchor PEAK, 0.759 → 1.099 m**, and it
is expected rather than incidental: a bot that grabs while still walking picks the prop up in
motion, so the spring's transient lag is sampled at speed instead of from a standing start. The
mean (0.047 → 0.048 m) and the placed pose (0.008 m / 0.00 deg) are unmoved, which is what says
the change is in when the grab happens and not in what the spring does.

### `Sfx: material voices` after the press fix — 4 PASS / 2 FAIL over six runs, and NEITHER red is the wedge

Tally on the fixed tree, standalone, machine otherwise idle: runs 1 and 3 FAIL, **runs 4, 5 and 6
PASS 3/3 consecutively**. The wedge signature — a driver motionless with `heldPropId = -1` at a
closest approach of 1.49 m — **did not occur once in six runs.** The two reds are different
animals and are worth separating here so the next reader does not re-diagnose them as this one:

- **Confirmed a third time in REVIEW-1's own marathon** (2026-09-20, 43 suites, 42 PASS / 1 FAIL,
  this suite the only red): `cardboard: prop 2 never made a sound at all`. `SfxBoxBot` was pinned
  to marker 2 `(38.50, 1.10, 2.10)` with `moved=True` on the server, its first sample is its
  pre-pin body at `(44.40, 0.65, 0.10)`, and it ended at `(46.16, 0.06, -4.06)` — the far corner,
  **closest approach 3.144 m in 3D**, `heldPropId = -1` throughout. **3/3 PASS standalone**
  immediately afterwards on the same tree, all three drivers firing every run. Same shape as run 3
  below, in the other corner: a bot whose reconciled body starts one walkway over steers into a
  bay and slides. **Three metres is not a radius anybody can set.**
- **Run 3 is SHELF-1's four-corridor room, not an arrive radius.** `SfxProduceBot` spawned pinned
  at `(41.50, 2.30, -2.10)` and its next sample is `(46.16, 0.06, 4.06)` — it slid along the bay
  it was walking into, ended in the far `+X +Z` corner, and stayed there for the remaining 24 s.
  **Closest approach to its prop: 4.01 m in 3D / 3.50 m horizontally**, never once inside any
  radius anybody could set. `--carry-walk-to` is one point walked in a STRAIGHT LINE and this room
  has no cross-aisle except at its ends (HOLD-1's entry above). That is the level change SHELF-1
  deferred to Talon's ride and it is still his call.
- **Run 1 is not a staging failure at all.** All three drivers picked up (`TinPick x1`,
  `CardPick x1`, `ProducePick x1`) and the host heard seven impacts; the witness heard three, all
  `CardThud`, and the suite reported *"THE OTHER PLAYER IS DEAF to tin / to produce"*. Nothing
  about the walk. Unexplained here and NOT chased — `OneShotSteals` was 27.3 % on that run — but
  recorded so that a future reader with the same two lines knows it has been seen once on a tree
  where the drivers demonstrably reached their props.

### How to read a red here

- **`prop N never fired <Pick> on PickedUp` / `never made a sound at all`** with **no
  `grab denied` on the server** and the bot's own trace showing it motionless outside 1.2 m:
  this staging flake. Not the wire, not the profile, not the mix. **Since REVIEW-1, read the
  closest approach against 1.5 m in 3D**: inside it and motionless is a press that found nothing
  (fixed); several metres out is the four-corridor mis-walk, which no constant can reach.
- **The bot reached its prop and still played nothing**, or **played the wrong material**: the
  real thing. Assertion 4 (`-PlantCrossedProfile`) is what defends the second.
- **Check the bot's JSONL before anything else.** Its `heldPropId` and its closest approach
  answer this in two lines, and no server log can.

## A fixture staged against an UNDRESSED room is a defect with no owner (INT-1, measured 2026-09-19)

**`Sfx: material voices` was the only real red in INT-1's first marathon, and neither lane that
made it could have seen it.** SFX-2 added an eighth seeded prop — a lone can for its place
driver — and chose `(37, 4)` in the search room against the rule its own handoff states: *"≥ 2.2 m
from every other occupant of the search room."* That was true when it was written and is still
true. **SHELF-1 then dressed that room**, and `(37, 4)` became a spot in the +Z EDGE WALKWAY with
no `SearchSpawn` marker in it, behind four unbroken 9.2 m aisles.

The failure, and what it looked like from each end:

```
- PLACE IS NOT ON THE WIRE: the host never fired ActorEvent.Placed for prop 8 (the scripted
  place). Events it saw for that prop: . Either the place never landed ... or PropRelease.Placed
  is not reaching NetworkedProp.BeginLoose.
```

**Read that message: it names both candidates, and only the bot's own trace tells them apart.**
From `matsfx.place.jsonl` and its stdout on the merged tree:

| | |
|---|---|
| spawned at | `(35.63, 0.56, 0.13)` — marker 0, the z = 0 walkway |
| ended at | `(36.96, 0.00, 0.65)` — 1.3 m later, against aisle 1's bay face |
| `heldPropId` | **−1**. It never grabbed anything |
| `[bot] SfxPlaceBot PLACING` | **absent** |
| `place denied` on the server | **absent** |

**Both absences are the tell, and TASK-1 §3.3 is why**: *a press that is never made is never
refused.* `grab denied` and `place denied` are the first things anyone greps for and NEITHER can
fire for a request that was not sent — so a staging failure of this shape presents as an
assertion about the WIRE and has no log line anywhere pointing at the floor.

**The generalisation, and it is not about sound.** A fixture's coordinates are a claim about the
LEVEL, and a level is owned by a different lane on a different branch. Two lanes can each be
right on their own base and produce a defect in the merge — so:

- **When a lane dresses a room, every fixture already staged in it is that lane's problem too.**
  SHELF-1 did exactly this for `Run-PlaceTest` and `Run-MaterialSfxTest` (§6) and got the two it
  could see; SFX-2's eighth prop was on a branch it could not.
- **At integration, grep every seeded/scripted coordinate in `tests/` against the rooms the wave
  dressed.** It is one grep and it is cheaper than a marathon.
- **A suite whose fixture cannot be reached should say so about the FLOOR, not about its
  subject.** The assertion above is well written — it names both causes — and it still sent the
  first reader at the wire.

**Fixed at INT-1** by SHELF-1's own rule (put the fixture in the walkway its bot spawns in): the
can moved to `(45.3, 0)` in the z = 0 walkway and `SfxPlaceBot` is pinned to `SearchSpawn_1`
(44.5, 0) with `--spawn-index`, 0.8 m away — inside `PickupRadius`, so there is no walk left to
lose. `Stock/Bin_4` is at `(46.4, 0)` and a `FloorBin` is 0.90 m wide, so the can clears its face
by 0.65 m.

## Two more suites are load-flaky, both measured at INT-1's marathon (2026-09-19)

Both were red in a 43-suite marathon and **3/3 PASS standalone** immediately afterwards on the
same tree, machine otherwise idle. Neither is on the list above; both are now.

### `Carry: place + integrity` — the quantity is the placed pose, and it is identical to the DIGIT standalone

| Run | Result | The quantity |
|---|---|---|
| marathon, 14th of 43 | **FAIL** | `bot B sees prop 1014 0.055 m from the intended transform (bar 0.05)` and `rotated 15.74 deg from the intended 35 deg yaw (bar 5)` |
| standalone ×3, idle | **3/3 PASS** | `0.008 m and 0.00 deg` — **the same to the digit on all three runs** |

**A red here whose numbers are 0.008 m / 0.00 deg is impossible; a red whose numbers are tens of
millimetres and tens of degrees is the prop still settling when the logs stopped.** The three
refusal assertions beside it (`TooFarToPlace`, `DoesNotFitThere`, `OutsideRoom`) stayed green in
the marathon, which is what shows the red is about a settle rather than about placement.

### `Round: buttons + drop-off bin` — the red is `TooFarAway` where `NotNow` was staged

| Run | Result | The failing check |
|---|---|---|
| marathon, 42nd of 43 | **FAIL** | *"the seeker's START press after the round had begun was not refused with NotNow"* |
| standalone ×3, idle | **3/3 PASS** | — |

The server's own line says what happened instead:

```
[buttons] press Start peer=1640182223 refused reason=TooFarAway text="STEP CLOSER TO THE BUTTON"
```

**BTN-1's handoff predicted this exact shape** (§2, *"Why the reach check runs FIRST"*): *"a bot
in the wrong phase is usually also in the wrong room and gets `TooFarAway` rather than
`NotNow`."* `--press` fires on the BOT'S OWN elapsed clock with no anchor — BTN-1's own doc
explains why it cannot be anchored on an observed event for that press — so under marathon load
the walk to the button slips past the scheduled second and the server's reach re-check, which
runs BEFORE the phase gate, answers first. **The discriminator is the REASON in the refusal
line**: `NotNow` missing with `TooFarAway` present is the walk; `NotNow` missing with nothing
present is the gate, and that is the real regression this check exists for.

## REACH-1: udp/7903, and four measured things about the placement audit (2026-09-19)

### The port ladder, written down rather than recomputed

`tests/Run-ReachTest.ps1` claims **udp/7903** for all four of its phases. The ladder as it
stands, which is the list to read before picking the next one:

**THE LADDER — one table, and this is it** (consolidated by INT-0B, 2026-09-19, when the six
wave-2 lanes were merged and their five separate part-tables could finally be resolved into one).
Every number below is a REGISTERED suite's bind port on the merged tree, audited by grepping
every `$Port` in `tests/` after the last merge: **all distinct.** Read this table before picking
the next one, and take **7911** (REVIEW-1, 2026-09-20 — INT-1's own walk took 7910 while this
sentence still said 7910 was free; the table below is the WHOLE wave, all three merges in, plus
that walk, and it is the only copy).

> **Updated by SHELF-1, 2026-09-19.** 7905-7908 are now spoken for; the next free number is
> **7909**. The four were HANDED OUT BY THE ORCHESTRATOR, which is this section's own lesson
> applied: BTN-1, TASK-1 and SHELF-1 branched off two different bases within an hour of each
> other and none of their `tests/` directories contains the other two's suites, so a grep in any
> one of the three worktrees would have produced 7905 three times.

| Port | Suite | Lane |
|---|---|---|
| 7893 / 7894 / 7895 | `Run-CarryNetTest.ps1` (contention / authority / teleport) | CARRY-1 |
| 7896 | `Run-RoundLoopSmoke.ps1` | ROUND-1 (MATCH-1 extended it; port unchanged) |
| 7897 | `Run-FirstPersonTest.ps1` | FP-1 (moved off 7896 by INT-0) |
| 7898 | `Run-PlaceTest.ps1` | CARRY-1 (moved off 7896 by INT-0) |
| 7899 | `Run-VoiceRoomTest.ps1` | VOICE-1 |
| 7900 | `Run-BurstDoorTest.ps1` | DOOR-1 |
| 7901 | *reserved, no suite yet* | BTN-1 (merges at INT-1) |
| 7902 | `Run-MaterialSfxTest.ps1` | SFX-1 (SFX-2 merges at INT-1 on the same port) |
| 7903 | `Run-ReachTest.ps1` | REACH-1 |
| **7904** | **`Run-RoundClockTest.ps1`** (and `Capture-RoundClock.ps1`, unregistered) | **CLOCK-1** |
| 7905 | *the match-end clock probe, unregistered and manual* | INT-0B (§8.5) |
| **7906** | **`Run-ButtonsTest.ps1`** | **BTN-1** |
| **7907** | **`Run-SortTest.ps1`** (and `Capture-SortRoom.ps1`, unregistered) | **TASK-1** |
| **7908** | **`Run-AuthoredPropTest.ps1`** | **SHELF-1** |
| **7909** | **`Run-HoldingBoardTest.ps1`** (and `Capture-HoldingBoard.ps1`, unregistered) | **HOLD-1** |
| **7910** | **`Walk-DefinitionOfDone.ps1`** (the §8 walk, unregistered and manual) | **INT-1** |
| **7911** | **`Run-StockTest.ps1`** (and `Capture-ShopFloor.ps1`, unregistered) | **STOCK-1** |
| **7913** | **`Run-CarryHoldTest.ps1`** | **FEEL-1** (7912 was taken by a live SOLO-1 lane) |
| **7917** | **`Run-HandsSmoke.ps1`** | **HANDS-1** (assigned in the dispatch, not computed) |

Everything below 7893 is the pre-fork ladder and is unchanged: 7777, 7778, 7788, 7799, 7807,
7809/7810, 7815, 7816, 7817, 7818, 7821, 7822, 7830, 7831, 7834.

> **Updated by HOLD-1's BTN-1 merge, 2026-09-19.** BTN-1 carried a fifth partial copy of this
> ladder on its own branch (its `tests/` could not see 7904/7905/7907/7908) and it is DROPPED
> here rather than kept beside this one — five lanes each writing down the part of the ladder
> they could see is exactly how 7899 was claimed twice. Its two real claims are folded into the
> table above: **7906 is `Run-ButtonsTest.ps1`, registered**, and 7901 stays reserved and unused.
> BTN-1's branch listed 7905 as SFX-2's; INT-0B's row (the unregistered match-end clock probe)
> is the one on the merged tree and is what the table says. **HOLD-1 claims 7909** for
> `Run-HoldingBoardTest.ps1`; the next free number is **7910**.

> **Updated by REVIEW-1, 2026-09-20.** **7910 is TAKEN** — `tests/Walk-DefinitionOfDone.ps1`
> binds it, and it landed at `bd69498` in the same delta that left all three "next free" lines
> above and below saying 7910 was free. **The next free number is 7911.** The only record that it
> had moved was one summary row in `docs/agents/handoffs/2026-09-19-INT-1.md`, and *a handoff is
> not the ladder* — the next lane reads this table, sees a free number, claims it, and two suites
> bind the same UDP port. That is the failure this repo has now paid for twice (three lanes on
> 7896, two on 7899), and the table's own text calls itself "the WHOLE wave, all in one place",
> which is only true while somebody keeps adding the rows. **An unregistered harness takes a row
> like anything else**: it is never in `Run-AllTests.ps1`, so it can never collide with a
> marathon, but it can collide with the next lane that grepped this table.

> **Audited on the fully merged tree, INT-1, 2026-09-19.** One grep of every `$Port` in `tests/`
> after the last of the three merges: **every REGISTERED suite's port is distinct.** Four numbers
> appear twice and all four are a registered suite beside its OWN unregistered capture or
> measurement harness, which is the pattern CLOCK-1 established and every lane since has
> followed — 7904 `Run-RoundClockTest` + `Capture-RoundClock`, 7907 `Run-SortTest` +
> `Capture-SortRoom`, 7908 `Run-AuthoredPropTest` + `Measure-ShopFloor`, 7909
> `Run-HoldingBoardTest` + `Capture-HoldingBoard`. **Each pair is never run together** (both
> halves take the machine-wide mutex, and only the registered half is in `Run-AllTests.ps1`), so
> a marathon can never collide with one. Stated rather than left for the next reader to
> rediscover as four scary-looking duplicates.


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

## `Run-RoundLoopSmoke` now runs a whole MATCH, and its first red was in the SUITE (MATCH-1, measured 2026-09-19)

**New baseline for this suite.** It drives two rounds and a third Start (~95 s, was 46), still on
port 7896, and it is now the longest single entry in the registry. The ROUND-1 numbers above are
unchanged by the longer schedule, which is the useful half of that: **worst closest-approach
1.10 m, room separations 38.08 / 42.07 / 80 / 9.73 m, one named refusal** — identical to ROUND-1's
and INT-0's runs across 13 server-decided moves instead of 6. New quantities to compare across
runs: **12 phase transitions, 3 cards** (`r1 matchOver=False, r2 matchOver=True, r3 matchOver=False`),
match tally armed **10 s** against the round card's **6 s**, totals **173–171**.

The third card is round 3 ending by disconnect when the bots exit on their own duration. That is
correct and free: it exercises the disconnect path every run.

### A card outlives the numbers it describes, and a suite can read the wrong instant

**The first run failed with `the totals on the wire are level at 0 but the card names peer N the
winner`, and the game was right.** `HideSeekTally` is still a peer's `LastTally` long after its
Tally phase ended, and the Start that begins the next match zeroes the live score map while the
card still reads 173–171. The suite had keyed its "the winner equals the higher total" check off
the *last* sample carrying the card, which is one round too late.

**So: when a suite compares a FROZEN value against a LIVE one, the sample has to be taken at the
instant they describe the same moment**, and that instant is worth naming in the code. This suite
now builds two maps — `Cards` (the last sample carrying each card, for the card's own frozen
fields and to prove they survived a reset) and `CardsAtTally` (the last sample carrying each card
while phase == Tally, for every live-vs-card comparison). It generalises to any "last N" the
absolute wire keeps around: the round card, the last refusal, anything a board shows after the
thing it describes is over.

It also turned into the strongest assertion in the suite, because the same sample proves both
halves at once:

```
RoundA after the third Start: 2 score row(s), all zero; match card still reads 173-171, winner 1196225384
```

### Proved able to fail: `MatchRounds = 3`

11 failures, **every one a MATCH-1 assertion** — matchOver false on round 2 on both peers, the
winner against the higher total, the draw, the scores surviving the third Start, and the server
never logging match 2 beginning. The phases, rooms, teleports (13 moves, 1.10 m), the refusal and
both cards' field-by-field equality between the two peers **all stayed green**, which is what
shows the new checks are independent rather than one check wearing eleven hats.

Two of those eleven were a wrong diagnosis of a real fault ("the match card's winner went to 0 when
the scores reset — the result is not frozen", fired on a card that was never a match end and so had
no result to freeze). Fixed by guarding that branch on `matchOver`. **A planted fault is also a
test of the failure MESSAGES**, and this one found two that would have sent the next reader at the
wrong file.

### `dotnet test` after MATCH-1: Failed: 0, Passed: 1357, Skipped: 0, Total: 1357

INT-0's baseline was 1317; `HideSeekMatchTests.cs` adds 40 and nothing was changed to make an
existing test pass.

### Three lanes, and the mutex did its job

This run queued **~390 s** behind `C:\repos\sfgd-reach1`'s suite and then `C:\repos\sfgd-clock1`'s,
which the lock's own waiting lines name by pid and worktree. No process this lane did not start was
investigated or killed. Worth recording as the ordinary case: the wait is not a mutex timeout and
it is not a red — read the waiting lines, which say who holds it and for how long.

## The wave-2 port ladder, and why 7904 is stated rather than computed (CLOCK-1, 2026-09-19)

INT-0's entry above is the reason this section exists rather than a grep. **A "next free port"
computed from a snapshot of `tests/` is not free while five lanes are branched off one base and
none can see the others**, which is exactly the shape of wave 2. So the orchestrator hands the
numbers out and each lane WRITES DOWN the one it was given, here and in its own script's header:

**CLOCK-1 took 7904.** Its table has been folded into the one ladder table in the REACH-1
section above (INT-0B, 2026-09-19) rather than kept as a fifth partial copy — five lanes each
writing down the part of the ladder they could see is exactly how 7899 was claimed twice.

**A collision at RUN time on one of these is another lane's live process, not a defect.** That is
the distinction INT-0 drew and it is worth restating from the other side: a registry collision
fails identically on every marathon and is fixed by moving a number; contention with a sibling
worktree's Godot clears on a re-run. If 7904 is genuinely taken by a registered suite after the
merge, CLOCK-1's next is **7905** and nothing else in its script depends on the value.

## A 1 Hz log from three processes needs a stamp, not an ordering (CLOCK-1, 2026-09-19)

`Run-RoundClockTest.ps1` compares what three independent clients painted on a wall against what
the server believed, and the comparison is the suite. Two things made it an assertion rather than
a tolerance, and both generalise to any multi-peer readout check:

- **Every line carries a Unix millisecond stamp and the suite pairs each client line with the
  server line NEAREST IT IN TIME**, not with "the last server line before it". Three independent
  1 Hz timers drift into and out of phase with each other within a period, so "the last one
  before" is between 0 and 1000 ms stale at random — which is the entire tolerance the check is
  trying to spend. `BotHarness`'s `Wall` field made the same point for JSONL in FIX-1's entry
  above; this is the same fact for `GD.Print`.
- **The client's PHASE WORD is read off the Label3D and the server's off `ServerState`.** Neither
  side is recomputed from a folded view: a client line built from `driver.View` would still pass
  with a blank panel on the wall, and a server line built from the server's own folded view would
  let a fold bug make both sides wrong in the same direction.

**One sample class is skipped rather than compared**, and saying which is part of the check: a
client line within 1.2 s of a server phase transition. At 1 Hz logging over a 10 Hz wire a client
can legitimately still be showing the phase it had when the server has already moved, and that is
the wire's latency rather than a wrong clock. The suite counts the skips and the unpaired lines
and **fails if fewer than 30 pairs were actually compared**, so a guard that swallowed everything
cannot read as green.

## Deferring a sound by one tick broke an assertion about ORDER, not about the sound (SFX-2, measured 2026-09-19)

SFX-2 moved every networked prop's impact off the contact handler and onto a server announcement
flushed at the top of the next physics tick, so that the other player hears it at all. One
assertion in `tests/Run-MaterialSfxTest.ps1` went red, and it was not about the impact:

```
[sfx] pair-suppressed self=26 other=Prop_3 speed=2.26 t=7313
[sfx] sfx Thunk event=Impact intensity=0.043 at (38.00,0.22,0.00) src=Prop_3 t=7325 via=wire
```

SFX-1's once-per-contact assertion required the winner's sound to have played **at or before**
the loser's suppression (`$_.T -le $s.T`), which was true by construction when the fire happened
inside the signal dispatch. Twelve milliseconds — one tick at 60 Hz — and the suite called a
correctly-paired suppression an ORPHAN, i.e. reported the exact failure mode the first-come rule
was written to avoid.

**The assertion's question was never about order.** It asks whether the partner PLAYED, so that a
de-duplication rule which is silent rather than single is caught. "Before" was an incidental
property of where the fire lived. The window is symmetric now.

**Generalises past sound: an assertion that pins the ORDER of two events pins the code path that
produced that order.** When a lane moves work onto a later tick, a queue, or a network hop —
which is what most "the other player should see this too" packets do — every before/after window
in the suite becomes a claim about latency that nobody meant to make. Grep for `-le $s.T`,
`-lt $x.T` and their kin when a fire moves, and ask of each one whether the direction was the
point or the accident.

**A second orphan cause now exists and the suite names it in its own failure text**: source-side
limiting can drop the winner on an over-budget tick, which leaves a real suppression with no
partner. That is the design working. The discriminator is an `[sfx] impact-limit` line at the
same `t`.

### A cap that never engages must still print a number

`ImpactBudget.MaxImpactsPerTick` is 4, and on the packet's own 40-prop collapse fixture **it never
engaged**: every prop carries a 0.4 s per-body cooldown, so forty props cannot offer forty
contacts on one tick. The first version of the reporting printed "never engaged", which reads
identically to *the limiter is not wired up* — the same absence-without-a-control trap this file
records for headless probes and for `FootstepAudioTests`' positive control. `PropManager` now
logs its running peak offer (`[sfx] impact-peak offered=N budget=4`), the suite prints the
measured headroom, and a peak of **zero** is a hard failure rather than a quiet pass.

### `Run-RoundLoopSmoke.ps1` takes the machine mutex ITSELF, and that only works from the marathon

Every other registered suite leaves the lock to `Run-AllTests.ps1`. This one calls
`Enter-SuiteMutex` at its own line 105. Inside a marathon that is harmless and invisible: the
marathon runs each suite with `&` in **one PowerShell process on one thread**, and a Windows
named `Mutex` is re-entrant per thread, so the inner acquire succeeds instantly on a recursion
count of 2.

**It deadlocks the moment a parent holds the lock and invokes it as a CHILD PROCESS.** Measured
2026-09-19 (SFX-2): a wrapper that took the mutex and then ran each suite with
`powershell -File` sat at `waiting for machine-wide full-suite lock (900s so far) - held by: pid
30864 ... (C:\repos\sfgd-sfx2)` — **waiting for itself**, and the holder description said so in
as many words, which is the tell. Every suite before it had passed.

So: **run `Run-RoundLoopSmoke.ps1` standalone directly, never from inside a lock-holding
wrapper**, and if you write a one-suite or subset wrapper, either invoke the suites in-process
(`&` in the same thread, as the marathon does) or do not take the lock in the wrapper at all.
The two Godot lanes this machine runs make subset wrappers common enough that this will be hit
again.

## TASK-1: udp/7907, a PowerShell 5.1 trap, and a fixture that stalled on level geometry (2026-09-19)

`tests/Run-SortTest.ps1` claims **udp/7907**, handed out by the orchestrator rather than computed
from a snapshot of `tests/` -- INT-0's lesson applied rather than re-learned for a fourth time.
7905 and 7906 are wave-3 reservations with no suite on this branch (BTN-1 holds 7906), so a grep
here cannot see them and would happily have produced 7905. The one ladder table in the REACH-1
section above has the row; **the next free number is 7908.**

> **Corrected at INT-1, 2026-09-19** (the entry above is kept as TASK-1 wrote it; this file's
> header forbids deleting another agent's entry). **7908 was not free** — SHELF-1 took it for
> `Run-AuthoredPropTest.ps1` on a branch TASK-1 could not see, and HOLD-1 then took 7909. This
> is the SAME defect the entry itself is about, one wave later and from the other side: TASK-1
> read the ladder correctly and the ladder was a snapshot. **The next free number is 7911**
> (7910 is INT-1's `Walk-DefinitionOfDone.ps1`; corrected by REVIEW-1, 2026-09-20), and the one
> table in the REACH-1 section is the only place that sentence should ever be written.

Baseline, machine busy throughout (BTN-1's suite held the machine-wide mutex for ~11 minutes
immediately before the first run and its Godot processes were alive during all four):

```
        hider: roundSorts 2 at Together over 30 sample(s)
        seeker: roundSorts 2 at Together over 29 sample(s)
        server: 2 sort-good, 1 sort-bad
        hider : 2 sort-good, 1 sort-bad
        seeker: 2 sort-good, 1 sort-bad
        hider held prop 1013 in 168 Seeking sample(s)
  right sorts counted on the wire:      2
  sort-good lines across 3 processes:   6
  sort-bad lines across 3 processes:    3
  duplicate counts for a re-placed obj: 0
  burst force-dropped:                  1 prop(s)
  hider's card at Tally:                2 sorted
```

**The quantity to compare is the per-PROCESS sort-good/sort-bad pair, not the wire count.**
TASK-1 adds no wire field: the count rides `HideSeekWire`'s existing slot and every peer DERIVES
its own verdicts from replicated props. So "2 / 1" appearing three times is the assertion, and a
run where the server says 2/1 and the clients say 0/0 is the failure the suite exists for -- it
is exactly what the planted "only the server derives" fault produced, with the wire count still
reading a perfectly correct 2 on both peers.

### `@($list)` on a `List[object]` throws in Windows PowerShell 5.1

Measured, and it cost twenty minutes because the message points nowhere useful. A helper that
built its rows in a `System.Collections.Generic.List[object]` and ended with `return @($out)`
died with

```
Argument types do not match
+     return @($out)
    + CategoryInfo : OperationStopped: (:) [], ArgumentException
```

The throw is at the wrapping `@(...)`, not at anything inside the loop, and the suite reported it
from three statements later -- it read exactly like a type error in the assertion that followed.
`$out.ToArray()` or a plain `$out = @()` with `+=` both avoid it; the counts these parsers handle
are single digits, so the array is free. `[System.Collections.Generic.List[string]]` is fine
(`Run-PlaceTest.ps1` has used one for months) -- it is the `[object]` instantiation that bites.

### A scripted fixture can stall on the level and log absolutely nothing

The first full run delivered two objects and then stood still for fifty seconds. **Nothing was
logged, because a press that is never made is never refused** -- the server's `grab denied` line,
which is the first thing anyone greps for, cannot fire for a request that was not sent.

The cause was geometry: the sortables sit on a 1.4 m-wide crate, so a body walking at an object
on the far column is stopped by the crate itself with the object still **1.40 m** away, outside
the 1.2 m arrive radius copied from `ScriptedCarryIntentSource`. The discriminator was the bot's
own JSONL: its position was byte-identical at t = 25, 31 and 38 s with an empty hand.

Two reusable things:

- **When a scripted bot goes quiet, read its POSITION series before reading any server log.** A
  wedged bot and a bot whose requests are being refused produce completely different evidence,
  and only one of them writes anything on the server.
- **An arrive radius is a fact about the FURNITURE the target sits on, not about the target.**
  Anything on a table, a shelf or a crate is unreachable by its own footprint plus the body's
  radius, so the radius has to be derived from the server's reach (`SandboxAvatar.PickupRadius`
  + `PropManager.GrabRangeTolerance` = 2.25 m in 3D) and not copied from a suite whose props sat
  on the floor. SHELF-1's hundred shelved props will meet this immediately.

### Proved able to fail, twice, in one run

Two faults planted together: the once-only guard removed from `SortTally`, and `SortRoom` gated
so that only the server derives verdicts. **9 failures, and they separate cleanly** --

```
- server logged 2 sort-good line(s) for prop 1004; it was delivered to bin 0 twice and must count ONCE
- hider never logged sort-bad for prop 1007 in bin 2 -- the wrong bin was silent on this peer
- seeker never logged sort-bad for prop 1007 in bin 2 -- the wrong bin was silent on this peer
```

The count on the wire, the card, the burst's forced drop and the census **all stayed green under
both**, which is what shows the per-peer checks are independent rather than one check wearing
nine hats. Worth recording that the first plant was WEAKER than intended and the suite said so
honestly: `SortTally.Completed` is a HashSet count, so removing the guard duplicated the log line
and the sound without changing the score -- the duplicate-count assertion is what caught it, and
the `total > 2` assertion beside it correctly did not fire.

### The level check's hole, closed for a second prefab

CLOCK-1 measured that a LEVEL prefab is invisible to `SupermarketWorldSelfTest` until it is in
`SectionScenes`. Re-proved here for `SortBin.tscn` with the same plant (`AddChild(new Node3D())`
in `SortBin._Ready`):

```
[supermarket-selftest]   TaskRoom.tscn      packed=114 live=114     <- green, cannot see it
[supermarket-selftest]   SortBin.tscn       packed=27  live=28      <- FAIL
```

**The room stayed green** because `CountNodes` stops at an instance boundary, which is the whole
reason the list has to name every prefab. The three sortable prefabs (`SortCube`/`SortBall`/
`SortCan`) are deliberately NOT in it: each is a `Carryable` and builds its own outline shell in
`_Ready` exactly as `Crate.tscn` does, so listing one would assert a failure by construction.
They are covered one level up instead -- each sortable's `SortItem` is authored directly in
`TaskRoom.tscn` rather than inside a prefab, so the room's own count walks it to the leaf.

### `dotnet test` after TASK-1: Failed: 0, Passed: 1619, Skipped: 0, Total: 1619

INT-0B's baseline was 1550. `SortRuleTests.cs` adds 69 and one MATCH-1 assertion changed because
its subject did (TASK-1 appends the hider's sort readout to the strip line that test was
asserting; the seeker's half is added beside it, unchanged).

## `Sandbox: mechanics` is load-flaky, and the discriminator is WHICH CHECK (TASK-1, measured 2026-09-19)

**New to the load-flaky list**, measured rather than reasoned, and it does not look like the
others: **120 of 121 checks passed and the one red was `phys_drop_no_holder_shove`** — an
offline-sandbox physics assertion that the holder's own vertical velocity stays gravity-only for
fifteen ticks after a drop (`SandboxSelfTest.cs`, `avatar.Velocity.Y <= 0.5f`).

Measured on `feat/2026-09-19-task-1`, whose diff cannot reach it: the sandbox self-test runs an
OFFLINE avatar (`NetRole.Offline`, `net.IsBot` false) in the sandbox scene, and TASK-1's only
edits to shared files are one untaken branch in `SandboxAvatar`'s BOT brain selection
(`SortScript.Count > 0`, and nothing in this suite passes `--sort-script`) and two appended
members plus two new recipes in `SfxLab` (no existing recipe changed, no pool size changed,
nothing here calls `Get` on either).

| Run | Conditions | Result | The check |
|---|---|---|---|
| marathon, 6th of 40 suites in one PowerShell process | loaded — BTN-1's Godot processes alive on the machine throughout | **FAIL**, 120/121 | `phys_drop_no_holder_shove` |
| tip, standalone `-SkipBuild` x3 | 4 other Godot processes alive (BTN-1's) for at least the first | **3/3 PASS**, 121/121 each | PASS each time |
| **base** `9c16181`, detached worktree, standalone x3 | idle | **3/3 PASS**, 121/121 each | PASS each time |
| the marathon's own three-suite TAIL (`Run-VoiceTest` -> `Run-HostingTest` -> `Run-SandboxTest`) in ONE PowerShell process | idle | **PASS**, 121/121 | PASS |

**The last row is the one worth copying** and it is CELEBRATE-1's discriminator: a standalone
re-run drops the PROCESS SHARING that the MOVE-1 env-var trap lives in, so re-running the tail in
one process is what separates "the machine was loaded" from "the suite before it". Here it
cleared both, which leaves load.

**How to read a red here: COUNT THE CHECKS AND READ THE NAME.** A `120/121` with
`phys_drop_no_holder_shove` as the only failure is this flake. A red on any of the other 120 — or
more than one at once — is a different animal, and the carry/physics family's own rule applies
first: read the failing check's name, then the log, before reaching for this list (CARRY-1's
entry above is the case where a red written in this file's vocabulary was a real regression).

**The A/B was inconclusive in the useful direction and that is worth saying** rather than
claiming the base fails too: neither tree reproduced it standalone, so what is established is
that the check passes 6/6 outside a marathon on both trees and failed once inside one. That is
weaker than AVATAR-1's tidal-cycle measurement and it is what there is.

## The task room has eighteen more rigid bodies, and `Run-ReachTest` counts them (TASK-1, 2026-09-19)

`[reach-cost] props in world` moves from **154 to 172**: the 150 the suite seeds, CARRY-1's four
in the search room, and TASK-1's eighteen sortables in the task room. The population check
asserts AT LEAST the seeded count and is unaffected — REACH-1 already recorded why it is not an
equality — but the numbers beside it moved and the next reader should not read that as a
regression:

| Quantity | REACH-1's baseline (154 props) | After TASK-1 (172 props) |
|---|---|---|
| rest audits in 20 s | 924 (46.2/s) | **1032 (51.6/s)** |
| integrity queries | 924 | **1036** |
| corrections | 0 | **1** |
| server physics p50 / p95 / peak | 2.765 / 20.321 / 50.580 ms | **4.850 / 20.463 / 48.079 ms** |

**Still one shape query per settle event**, which is the property that costing exercise exists to
protect: 1036 queries against 1032 audits. The audit rate rises because there are more props to
settle, not because each settle got dearer, and the p95 is within 1 % of REACH-1's while the peak
is 5 % below it. The p50 roughly doubling is 18 more rigid bodies being simulated on a machine
running another lane's suite. **SHELF-1's hundred will move all four again.**


## SHELF-1 (2026-09-19): udp/7908, and two things a 130-prop room taught the suites

`tests/Run-AuthoredPropTest.ps1` claims **udp/7908** and is registered last in
`tests/Run-AllTests.ps1`. It is the "authored-prop adoption proof" `docs/PRUNE-BACKLOG.md` has
listed as owed since the fork, returned with the props that made it worth having.

### An id assigned by node-path sort is a dependency nobody can see in a diff

`PropManager.AdoptAuthoredProps` hands out ids from 1000 in **ordinal node-path sort order**, so
the id of every prop in a room is a function of the NAME of every other prop in it. That is fine
until a room has more than a handful. The search room now authors 130, and `Run-PlaceTest.ps1`
names 1000, 1001, 1002 and 1003 in its own source as CARRY-1's four crates.

**Every product SHELF-1 added sorts before `Prop_`** — `Bin_`, `Box_`, `Can_` and `Produce_` all
start below `P` — so dropping them in beside the crates would have renumbered CARRY-1's four by
126 and turned another lane's suite red for a reason no reviewer would find from the diff. They
live under a container node named **`Stock`** instead, because `S` sorts after `P`. That is the
whole trick, and it is written into `SearchRoom.tscn` at the node so the next person to add a
prop there reads it before they name it.

**The generalisation, which is the part worth keeping:** when a system derives identity from a
sort over names, adding a sibling is an edit to every existing id. Put the new population in its
own container and choose the container's name against the sort, not against taste.

### Zero-pad any name an ordinal sort will order

`Can_10` sorts between `Can_1` and `Can_2`. Every product in the room is padded to three digits
(`Can_000`), so the id order and the reading order are the same thing. An unpadded set would have
been deterministic and identical on every peer — and wrong about which object a level author was
looking at.

## BTN-1: udp/7906, and two staging traps worth more than the suite (2026-09-19)

### The ladder, extended

`tests/Run-ButtonsTest.ps1` claims **udp/7906** for all three of its phases, **given by the
orchestrator** rather than computed from a snapshot of `tests/` — INT-0's lesson applied rather
than re-learned for the second time. 7904 and 7905 do not appear in this branch's `tests/` at all
(their suites are on unmerged lane branches), so a grep here would happily have produced 7904.

> BTN-1's own copy of the ladder table was DROPPED by HOLD-1's merge (2026-09-19) — the one
> table lives in the REACH-1 section above and now carries 7906 as this suite's. See the note
> under it.

### An authored prop's id is a fact about the WHOLE world, and a suite that types one will break

`PropManager.AdoptAuthoredProps` numbers authored props by sorting on NODE PATH across every room
at once. BTN-1 put three objects on a rack in `HoldingRoom.tscn`; `HoldingRoom/...` sorts before
`SearchRoom/...`, so those three took 1000..1002 and **the search room's four crates moved from
1000..1003 to 1003..1006**. `tests/Run-PlaceTest.ps1` named all four by literal and had to be
bumped by hand.

**SHELF-1's hundred aisle props will do it again**, and any of them whose node name sorts before
`Prop_0` will move that room's own four a second time. The server now prints one line per adopted
prop at world build, so a suite can read the ids instead of typing them:

```
[props] authored prop 1002 <- /root/Gameplay/World/HoldingRoom/ObjectRack/Deal_2 (Crate)
```

Note the path: the seam scene is added to `Gameplay` under the node name **`World`**, not
`Supermarket`, so a pattern anchored on the scene's own name matches nothing.

### A scripted PLACE is measured from the HAND, and the walk stops 1.2 m short

**Measured, on this suite's first run: both couriers were refused `TooFarToPlace` and nothing
reached the bin, which reads exactly like a bin that does not work.** Two constants compose into a
trap:

- `PropManager.RequestPlace` measures `PlaceReachM + GrabRangeTolerance` = **1.65 m** from the
  avatar's CARRY ANCHOR — about 0.9 m in front of the body, swinging with its heading — not from
  the body. `Run-PlaceTest.ps1`'s header already says "the hand"; it is easy to read as the body.
- `ScriptedCarryIntentSource.ArriveRadius` is **1.2 m**, so `--carry-walk-to` leaves the bot that
  far short of the point it was given, in whatever direction it happened to approach from.

So a walk-to placed a comfortable-looking 1.2 m from the target pose puts the hand 1.6-1.7 m away
and the place is refused. **Aim `--carry-walk-to` AT the thing, not next to it** — a bot that is
blocked by a solid object arrives by being blocked, which is both closer and far more repeatable
than a free-space stopping point.

Reusable discriminator: `[server] place denied peer=N reason=TooFarToPlace` in the server log
separates this from every bin/pad/validator rule. The suite now echoes those lines
unconditionally, for the CARRY-1 reason — read the server log before reaching for the flake list.

### A wrapper that holds the suite mutex around a suite deadlocks against its own child

Ten minutes were lost to `waiting for machine-wide full-suite lock ... held by: pid <my own
wrapper>`. Most scripts in `tests/` take `Enter-SuiteMutex` themselves; a few (`Run-SupermarketWorldTest.ps1`)
do not. A helper that wraps the mutex around one of the former can never proceed — the mutex is
not reentrant across processes. **Wrap the BUILD and IMPORT in the mutex and release before
invoking a suite that takes its own.**

### The session scratchpad is shared between lanes on this machine

A helper written to the scratchpad as `RunUnderMutex.ps1` was overwritten mid-session by another
worktree's file of the same name, and the next invocation ran against that other worktree. Same
class as the shared-stash hazard: **give scratch files a lane-specific subdirectory**, not just a
lane-specific name.

### `Carry: regrab-while-loose` — fourth confirmation, and the band has not moved

The BTN-1 marathon (36 suites, 35 PASS / 1 FAIL) went red on this suite alone, printing the exact
staging string this file already names: `bot A never completed held -> loose -> held-again for
prop 2 (reached phase 2) - regrab staging broken`. Standalone with `-SkipBuild` immediately after,
three times: **3/3 PASS, worst hold distance 1.05-1.09 m** (holder's own view 1.05/1.06/1.06 over
96/96/100 samples; independent witness 1.08/1.09/1.07 over 95/95/99). That is inside the
0.89-1.09 m band recorded here three times before it.

**The machine was not idle for any of these** — another lane's `Run-AllTests.ps1` and its Godot
processes were live throughout the marathon and through all three re-runs — which per SHADER-2's
entry above makes a 3/3 clear a stronger flake verdict than an idle-machine pass.

## HOLD-1 (2026-09-19): udp/7909, and four things measured merging two lanes into one room

`tests/Run-HoldingBoardTest.ps1` claims **udp/7909** and is registered last in
`tests/Run-AllTests.ps1`; `tests/Capture-HoldingBoard.ps1` is unregistered and manual on the same
number. **The next free port is 7911** (7910 went to INT-1's `Walk-DefinitionOfDone.ps1`;
corrected by REVIEW-1, 2026-09-20). The one ladder table is in the REACH-1 section above.

### An `[Export]` is not a level-authoring surface in this project, in EITHER shape

CLOCK-1's entry (and `godot-scenes.md`) records the trap as *"an `[Export]` on a nested
PackedScene instance line is silently dropped"*. **It is broader than that.** HOLD-1 authored an
`InteractionSlot` INLINE in `HoldingRoom.tscn` -- a plain node with `script = ExtResource(...)`
on it, not an instance of anything -- and set four exported properties on it
(`radius`, `rest_height`, `snap_rotation`, `show_marker`). All four read back as their C#
defaults.

**The failure was loud, and only because a list already existed.**
`Run-SupermarketWorldTest` said:

```
FAIL res://scenes/game/world/supermarket/HoldingRoom.tscn: 87 node(s) packed but 88 live.
```

`show_marker = false` never arrived, so `InteractionSlot` built its own marker ring in `_Ready`
and the room grew a node its `.tscn` does not declare. Without the packed-vs-live check this
would have been a silently oversized room and three silently wrong physics constants.

**The fix that works is a NODE NAME, because a node name is native.** `InteractionSlot._Ready`
now adopts a child called `SlotMarker` when the level authored one and builds its own only when
there is none, and the pad sets no properties at all. Same move `RoundClock.ResolveRoom` made for
the same reason. This is the fourth payment in this repo: `PropManager.AuthoredKindOf` (2026-07),
`Carryable.LoadLiftM`, `RoundClock.Room`, and now this.

### A guard can be protected by the KEY TYPE, and a peer id is not a good enough counterexample

`HoldingBoardModel.Rows` sorts the score map's keys so two peers paint the same row order.
**Deleting `ids.Sort()` left every ordering assertion green** -- CELEBRATE-1's trap, arriving
through a door nobody had used. `ImmutableDictionary<int,int>` enumerates in HASH order, `int`'s
hash IS the value, and the trie's traversal of small positive ints comes out ASCENDING.

The obvious "use realistic data instead of toy data" fix is **not enough**, and that is the part
worth keeping. Measured with four real peer ids lifted out of `tests/logs/`:

```
1076010669, 652333145, 1788870000, 233849852
  enumerate as -> 233849852, 652333145, 1076010669, 1788870000   (already sorted)
```

So a sort over `int` keys is unobservable through anything the shipped wire can produce. **To
prove the guard, its call site had to be made to reach it**: the test builds the map with an
`IEqualityComparer<int>` that hashes to the NEGATED key, which reverses the traversal, and
carries a positive control asserting the map really does enumerate differently before asserting
the rows come out ascending anyway. It goes red the moment the sort goes away.

**And the test's own helper had to be fixed first.** `View(scores: hostile)` was calling
`.ToImmutableDictionary()`, which silently rebuilt the map with the DEFAULT comparer -- so the
hostile test passed against a deliberately unsorted implementation. A fixture that normalises its
input can defeat the thing it was written to catch.

### A render is the only witness some defects have, and that cuts both ways

CELEBRATE-1's entry is about captures that are worthless (a flat grey frame passing as green).
This is the converse. `HoldingBoard.SetLine` early-outs when the new text equals the last text,
and the last text started as `""`. An empty row therefore **never got written**, so four labels
sat on the wall showing the placeholder the `.tscn` authors so it is openable in the editor.

Nothing in `tests/unit` could see it -- the model returns the right strings and the bug is in the
pusher. **Nothing in the scene suite could either**, and that is the sharp half: the suite reads
the board through `CurrentText`, which returns the same `_last*` fields the early-out compares,
so both halves agreed with each other while disagreeing with the wall. The first capture showed
it immediately. **When a readout's log line is built from the same cache as its own
change-detection, the log cannot see a paint that never happened.**

### Two suite-staging facts about the dressed search room and the two-player loop

Both cost a run each and both generalise past this lane.

- **`HideSeekLoop` needs EXACTLY two humans to Start**, not at least two. A capture script with
  three windowed lenses produced `[round] refused: NeedTwoPlayers - TWO PLAYERS ARE NEEDED TO
  START (phase=Holding humans=3)` and 72 seconds of frames that all read `HOLDING`. A third peer
  is fine once the round is RUNNING -- `Run-ButtonsTest` phase 3 and this suite both rely on that
  -- but it must arrive after the Start. The corollary bit too: a suite that gates its late
  joiner on `Holding -> Hiding` while that joiner IS the second player is a deadlock, and it
  fails with "the server never left Holding", which reads exactly like a broken round script.
- **`--carry-walk-to` is ONE point walked in a STRAIGHT LINE, and the dressed search room has no
  cross-aisle except at its ends** (bays span world x in [35.4, 44.6], walkways are 1.6 m).
  `Gameplay.SpawnPositionFor` deals `SearchSpawn` markers by JOIN INDEX, so a suite's third bot
  lands one walkway over from its first two. Measured: a courier sent from (38.5, 2.1) to a crate
  at (36, 0) **stopped dead at (36.01, 1.45)** against `Aisle2_Bay0` and never grabbed anything;
  the phase reported "the target was never delivered to the bin", which reads exactly like a bin
  that does not work. **Walk a bot along its own walkway, never diagonally**, and choose which
  prop it carries by where it spawns.

### Derive an authored prop id, do not type it

Three renumberings in two days (CARRY-1's four crates went 1000-1003 -> 1003-1006 -> 1014-1017)
and `tests/_Common.ps1` now has `Get-AuthoredPropId -ServerLog <path> -PathSuffix
"SearchRoom/Prop_0"`, reading `PropManager`'s own `[props] authored prop N <- <path>` line.
Adoption is logged BEFORE `[server] listening`, so a suite that already waits for the listening
line needs no second wait. `Run-PlaceTest` and `Run-ButtonsTest` use it; `Run-AuthoredPropTest`
deliberately keeps its literals, because the block IS its subject and a version that derived them
would pass whatever the world did.

**A container name can only order things WITHIN one room.** SHELF-1's `Stock` works because it
sorts after `Prop_` in the same room. Nothing in the holding room can sort after the search room,
because the room name is a PREFIX of every path under it -- so a prop added to an
alphabetically-earlier room renumbers every later room, always, and the only durable answer is
for the suites to stop typing the number.

## STOCK-1 (2026-09-20): udp/7911, and a level that is GENERATED and committed

`tests/Run-StockTest.ps1` claims **udp/7911** and is registered last in `tests/Run-AllTests.ps1`.
The one ladder table above said 7910 was next; by the time this lane's merge landed, **INT-1's
`Walk-DefinitionOfDone.ps1` had taken it** on a branch this worktree could not see. The
orchestrator handed 7911 out rather than letting this lane compute one from a snapshot of
`tests/` -- INT-0's lesson applied rather than re-learned for a sixth time, and it was right
again. **The next free number is 7912.**

### A level built in `_Ready` passes the check that exists to forbid it

STOCK-1's packet asked for a `ShelfStocker` node that fills `MultiMesh` buffers at scene load.
`.claude/rules/godot-scenes.md` forbids building a level in code, and
`SupermarketWorldSelfTest.CheckAuthored` is the measurement behind that rule -- it counts a
section's PACKED nodes against its LIVE nodes.

**Filling a MultiMesh buffer in `_Ready` adds no node, so it passes that check**, while being
precisely what the rule is about: a room that is empty in the editor and full at runtime. The
class doc says so in its own words -- *"every part of a level is physically authored in the scene
file SO IT CAN BE OPENED AND FLOWN AROUND IN THE EDITOR. That is not something a reviewer can
verify from a diff ... so it is measured."*

**So the fill is BAKED**: the same seeded arithmetic runs in a generator, writes
`scenes/game/world/supermarket/StockBulk.tscn` and `scenes/game/props/BinMound.tscn`, and the
result is committed. `SFGD_BAKE_STOCK=1 dotnet test tests/unit/SailNet.Tests.csproj` regenerates
them; every other `dotnet test` run RE-DERIVES the whole layout and compares it, so the committed
file is a pure function of eighty lines of arithmetic and a hand edit to it is a red with a line
number rather than a mystery a year later. **The generalisation: a node-count check cannot see
work that goes into a RESOURCE rather than into a node, so "it passes the level check" is not
evidence that a level is authored.**

### Bulk that overlaps a carryable is invisible until somebody hides behind it

The 120 products SHELF-1 authored are networked `RigidBody3D` standing on the same boards the
filler fills. Static filler laid over one puts a networked prop permanently inside static
geometry: every rest audit reports `StaticOverlap`, REACH-1's layer 3 answers `InsideStatic`, and
a hider who chose that prop is refused the Confirm with `NobodyCouldReachThat` -- **in a room that
renders perfectly and whose every other suite is green.** The fill carves every cell that would
come within 10 mm of a facing, and
`StockBakeTests.NoBulkInstanceStandsWhereACarryableFacingStands` is the gate.

**Why 10 mm and not the 0.02 m placement tolerance**, measured while writing it: at produce's
0.175 m pitch for a 0.16 m sphere, a 0.02 m clearance empties both NEIGHBOURS of every facing as
well and leaves an end-cap with 2 of 14 cells standing -- barer than SHELF-1 left it. The quantity
that matters is penetration, and any positive clearance is already zero penetration.

## FEEL-1 (2026-09-20): udp/7913, a port collision that had already happened, and how ports are handed out now

### PORTS ARE ASSIGNED BY THE ORCHESTRATOR IN THE DISPATCH. A LANE NEVER COMPUTES "NEXT FREE".

That is the rule now, stated once, and everything below it is why. The ladder table in the
REACH-1 section is a RECORD of what has been claimed, not a source of free numbers: by the time
you read it, another lane branched off the same base has read it too.

**`tests/Run-CarryHoldTest.ps1` claims udp/7913.** It was written for **7912** -- the number the
one ladder table said was next -- and on its first real run the server printed:

```
ERROR: Couldn't create an ENet host.
[server] failed to start server port=7912 transport=enet err=CantCreate
```

`netstat` named the holder, and it was not a stale socket and not a wedged process of this
lane's: a **SOLO-1 lane in `C:\repos\sfgd-solo1`** was live on 7912 at that moment
(`--server --port 7912 --world supermarket --solo --round-script ...`, plus its `SoloA` bot).
Two lanes, one base, one table, the same next-free number. **That is the fourth time this repo
has paid for it** (three lanes on 7896, two on 7899, INT-1's 7910 taken while three sentences
said it was free, and now this), and the previous three entries each said "the orchestrator
should hand the numbers out" without the rule ever being written as a rule. It is written now.

**Assigned for this wave** (orchestrator, 2026-09-20): SOLO-1 **7912**, FEEL-1 **7913**,
SICK-1 **7914**, ART-1 **7915**, PHYS-1 **7916**, HANDS-1 **7917**; PROBE-1 stays on 7908.
Add your row to the ladder table when your suite lands; do not compute one.

> **HANDS-1 added its row, 2026-09-20.** **udp/7917**, `tests/Run-HandsSmoke.ps1`, registered
> last in `Run-AllTests.ps1`. It was handed out in the dispatch and never grepped for, which is
> the rule above being followed rather than re-learned: SICK-1 (7914) and PHYS-1 (7916) were live
> in their own worktrees while this lane ran and neither of their suites exists on this branch,
> so a grep here would have produced 7914. **FEEL-1's own 7913 row is added to the table at the
> same time** -- it was recorded in that lane's handoff and in its suite header but never in the
> table, and REVIEW-1's entry above says in as many words that *a handoff is not the ladder*.
> **This suite is the SECOND that needs a desktop session** (FP-1's is the first); see the
> FP-1 entry above for what that costs a headless shell.

**Neither lane touched the other's processes**, and the discriminator that settled it in one
command is worth copying: `netstat -ano | grep <port>` gives a PID, and
`Get-CimInstance Win32_Process -Filter "ProcessId=<pid>"` gives its **command line**, which names
the worktree. That separates "another lane is live on my port" (move) from "a straggler of mine
is wedged" (stop it, by PID) from "a stale socket" (re-run) without guessing, and without going
anywhere near `taskkill /IM`.

### `Carry: drift (hold+walk)`'s quantity MOVED, and it is not drift

CARRY-1's entry above records that this suite's caps survived the carry spring "by luck rather
than by design", and warns: *"anyone who moves the carry anchor behind or beside the body puts
the whole lag straight into `bd` and should expect to re-measure these caps."* FEEL-1 moved the
anchor -- a held prop now rides the VIEW RAY in front of the eye instead of a chest mount plus an
armful lift -- so here is the re-measurement, same fixture, same flags:

| | mean `bd` | peak `bd` | growth | n |
|---|---|---|---|---|
| before (INT-1 / REVIEW-1's tree) | 1.010 m | 1.063 m | 0.000 m | 83 |
| **after FEEL-1** | **0.894 m** | **0.902 m** | **0.001 m** | 83 |

**The caps were NOT changed** (`MeanMax` 1.15, `PeakMax` 1.45): the number went DOWN, because the
armful lift that used to raise a crate above the carry mount is gone with the socket. A
`Carry: drift` red whose mean has moved back UP toward 1.0 is now the interesting one.

### A suite can measure the wrong thing and call the feature broken

Two of this packet's own instruments were wrong before the feature was, and both cost a run:

- **The shelf beat asserted that a `[carry] hold broken` line appeared.** Measured, the held
  crate stopped dead with its face ON the pillar (centre x = 45.28 against a face at 45.50 --
  0.22 m, exactly its own half-width) and the BOT wedged at the same instant, so the hold never
  ran the 0.6 m past its target that the break rule needs. The sweep had done its entire job and
  the suite reported "a held prop that nothing can stop". It asserts the **penetration** now --
  the quantity the beat is about, and the one a planted `CollisionMask = 0` moves: 0.001 m green
  against 0.214 m planted.
- **The lag table read `CarrySpring.Position`** while the ray hold integrates the spring's
  arithmetic against its own state, so the field sat at its seed for the whole hold and reported
  a mean lag of **1.54 m and a peak of 3.34 m** for a carry that was in fact tracking to within
  0.22 m. **An instrument reading the wrong field looks exactly like the feature being broken**,
  and the tell was that the number did not change when the behaviour did.

### The plant that proved the new suite, and what stayed green under it

`CollisionMask = 0` put back into `Carryable.OnPickedUpBySpring`: the shelf beat goes from
0.001 m to **0.214 m of penetration** (the crate half inside a 1 m pillar). **Both clip bars
stayed green under that plant** -- which is the useful half: the capsule projection and the world
sweep are independent guarantees, so a single plant cannot flatter both, and a red in one says
which mechanism moved.
