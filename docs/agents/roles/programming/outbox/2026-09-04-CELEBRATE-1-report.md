# CELEBRATE-1 — the level notices when the last bubble goes

**Role:** programming · **Branch:** `feat/2026-09-04-celebrate-1-all-bubbles` off
`playtest/2026-09-04-combined` (`418556d`) · **Worktree:** `C:\repos\Watis-celebrate1` ·
**Date:** 2026-09-04

---

## For Talon, in one paragraph

When you collect the last bubble, the game plays a short rising four-note chime, sends up a small
burst of pale-gold sparkles off every player standing in the world, and puts one line near the top
of the screen: *"That's every bubble in the world."* Then it stops. It does not take the camera,
dim the screen, pause anything, cover the play area, or swallow a single key — you can keep
walking, jumping and honking straight through it, and about a second and a half later there is no
trace of it left. It happens for **everybody in the session at the same moment**, not just whoever
touched the last one, because the tally is shared. Pull the reset lever and collect them all again
and it happens again; join someone else's game that is already finished and it does not, because
you didn't earn that one. Frames are in `docs/qa/CELEBRATE-1/`. **If it is too much, the two knobs
are `BubbleCelebration.PuffCount` and `PuffSizeM`, and deleting the
`PhaseToastLayer.Instance?.ShowLine(...)` line removes the text entirely** — both are one-line
edits and every value I picked is written down in `DECISION-LOG.md` §7.

## Corrections to the packet

1. **`DECISION-LOG.md` is not in the programming role's allowed write paths.** The packet requires
   a section there twice (scope item 2 and acceptance criterion 9), so I wrote it and am flagging
   the gap rather than self-correcting across it — the CARRY-1 precedent (`ROLE.md`, the
   `.claude/rules/test-suite.md` grant note). `ROLE.md`'s paths list needs a row, or the packet
   needs to route the decision record somewhere the role owns.
2. **The packet's premise for acceptance criterion 4 was half wrong, in the good direction.**
   "Audit whether `CheckBubblholic` is gated on human input today… If it is not, close it — that is
   in scope and is the most valuable thing in this packet." **It is already gated**, and has been
   since W7-8 (2026-08-30). Evidence below. There was nothing to close, so the most valuable thing
   in the packet turned out to be the two mutation findings in *What broke while building it*.
3. **The packet assumed a completion suite could be built on a bot walk.** It cannot, for the same
   reason the BT-6 fixture's determinism argument works: the six fixture bubbles are placed eight
   metres off the walker's corridor *precisely so* a straight walk pops exactly two. The completion
   is driven by a scheduled server-side pop (`--bubble-pop-all-at`) through the real
   `ServerPop` path instead. Stated because it is a deviation from "a run that pops every bubble".

## Bible check

```
Bibles applied:  MECHANICS-BIBLE (the completion resolves its own state: idempotency, races,
                 late-join, boundaries) and INTERACTION-BIBLE (it is feedback on the one thing
                 the player acts on in this level, in a session other players share).
Items checked:   MECHANICS §1 boundary conditions — completion is inclusive at the total and
                   false at 0 of 0 (the CI scaffolding worlds carry no bubbles; a bare
                   count >= total would announce a completed level on their first frame).
                 MECHANICS §3 simultaneous/race — the announcement rides ServerPop's own
                   transfer channel, so every receiver applies the final pop BEFORE the triumph;
                   a channel of its own would let a client celebrate at 111 of 112.
                 MECHANICS §4 idempotency — one server-side latch, cleared only by ServerReset.
                 MECHANICS §5 persistence — nothing is persisted. Deliberate: the celebration
                   writes no file, and the achievement that does is on its own already-gated path.
                 MECHANICS §7 end conditions — completion is an EVENT, not a state; a late
                   joiner's first sync reaches a full tally and must not fire it.
                 INTERACTION §2 feedback — at the exact moment of the trigger, three channels.
                 INTERACTION §5 multiplayer contention — the tally is server-adjudicated, so
                   "two players pop the last one on the same tick" was already a non-event
                   (TryPop's single flip); the announcement inherits that.
                 INTERACTION §6 reversibility — repeatable, and the reset lever is what resets it.
                 INTERACTION §8.1 consequence scope — declared `all_players`, not defaulted.
                 INTERACTION §8.2 redundant channel — the sound is backed by a world-visible cue
                   and a UI line, because this game deliberately severs a player's audio.
Result:          pass. §8.1 and §8.2 changed the design: the first sketch was the sound alone.
```

---

## What was built

Six files carry the feature; the rest is harness.

| File | What it does |
|---|---|
| `scripts/game/bubble/BubbleCelebration.cs` (new) | The beat, the gate, and the received/celebrated counters. Every line is presentation. |
| `scripts/game/bubble/BubbleCounter.cs` | `TryAnnounceCompletion` + the payload-free `CelebrateBroadcast` RPC + a `Completed` event + the server-side latch. Also `ServerPopAllForTest` — see the audit finding below. |
| `scripts/game/sandbox/SfxLab.cs` | `Sfx.Triumph = 23` and `TriumphPcm()` — synthesised, no audio file. |
| `scripts/game/sandbox/SandboxAvatar.cs` | `IsHumanDriven`, the achievement gate's own question hoisted to a public read. |
| `scripts/ui/PhaseToastText.cs` | `AllBubblesToast`. |
| `scripts/ui/PhaseToastLayer.cs` | A static `Instance`, so a network broadcast can reach the toast layer. |

**No `AchievementId`, no `AchievementCatalog` entry, no Steamworks call, no new art or audio asset,
no protocol bump, no new transfer channel.**

### How it replicates, and why that shape

The server owns the tally. `ServerPop` flips one bit, broadcasts the pop, then asks
`BubblholicRule.AllPopped(count, total)` — **the same predicate the achievement uses, reused rather
than respelled**, so the sound and the unlock can never disagree about what completing the level
means. If it says yes and the latch is clear, the server sends one **payload-free**
`CelebrateBroadcast` (`RpcMode.Authority`, reliable, `CallLocal`, defence-in-depth sender check —
`HonkManager.PlayHonk`'s shape).

**It rides `PopBroadcast`'s own transfer channel deliberately.** HONK-1's channel 17 was the
obvious precedent for a payload-free event, and it is the wrong one here: Godot orders reliable
RPCs *per channel*, so a channel of its own would have bought head-of-line isolation and paid for
it with a race in which a client celebrates while its own tally still reads 111 of 112. There is
one message per completion, so there is no head of line to block. **`ProtocolVersion` stays 14** —
nothing about the wire format, handshake or snapshot codec moves.

---

## Acceptance criteria

### 1. Build — PASS

```
$ dotnet build WatisWorld.sln
Build succeeded.
    4 Warning(s)
    0 Error(s)
```
The four warnings are pre-existing (`AvatarClipDirector.cs:250`, `AvatarVisual.cs:2074/2090`,
`SandboxCamera.cs:970`) and are on the base branch unchanged.

### 2. Completion fires on ALL peers — PASS, three separate processes

`tests/Run-CelebrateTest.ps1`, run 2 (`--celebrate-force` on all three bots, so the presentation
gate is open and the whole path executes):

```
        A (walker)   samples=153  celebrateReceived=2 celebratePlayed=2
        B (witness)  samples=150  celebrateReceived=2 celebratePlayed=2
        C (late)     samples=128  celebrateReceived=1 celebratePlayed=1
```

Three JSONL logs from three OS processes (`tests/logs/celebrate2-A/B/C.jsonl`). C reads 1 because
it joined after the first completion — assertion 4. In run 1 (the shipped gate) the same three
peers all `celebrateReceived` the same broadcasts and `celebratePlayed` none.

### 3. Idempotency — PASS, all three cases with log evidence

| Case | Evidence |
|---|---|
| **The tick after completion** | The server's `[bubbletest] all bubbles popped:` line appears **exactly twice** in a session with two completions. The fixture keeps ticking for ~25 s after the first one. Every peer's `celebrateReceived` is monotone and stops at its expected value. |
| **A late joiner at a finished level** | Bot C is launched only after the server logs the first completion. Its FIRST sample reads `count=6` (a finished board) and `synced=true`, and **every sample it takes while the board is still full reads `celebrateReceived=0`**. It is not greeted. |
| **Reset then recomplete** | `resets broadcast=1`, then a second full pop, then a second announcement. A and B reach `celebrateReceived=2`; C, present for the second only, reaches 1. |

The suite asserts each of these by name, and its failure messages say which one broke.

### 4. The bot gate — PASS, with a positive control

**The audit answer, as it stands on my base.** `AchievementRuntime.CheckBubblholic` **is** gated on
human input today, and was not changed. The gate is not on `CheckBubblholic` itself — it is one
level up, on whether the object exists at all:

```csharp
// scripts/game/sandbox/SandboxAvatar.cs:938-940  (ConfigureAsNetworked)
// A SCRIPTED BODY EARNS NOTHING (W7-8, 2026-08-30). isOwner alone is not "a player": ...
if (source?.IsHumanInput == true)
    _achievements = new Sail.Game.Achievements.AchievementRuntime(this);
```

`_achievements` is `null` for a scripted bot, a remote proxy, a headless server and the offline
labs, and line 1455 (`_achievements != null && …CheckBubblholic(...)`) therefore never runs there.
Two existing tests already hold it up: `tests/unit/BotAchievementGateTests.cs` (the interface
default is "not a person", and exactly one class in the assembly may claim otherwise) and
`SandboxSelfTest.RunAchievementGateTestsAsync` (a real `ScriptedAimIntentSource` body builds no
runtime; a real `LocalInputIntentSource` body does). **Nothing needed closing.**

**The new gate is the same seam.** `BubbleCelebration.HumanPresent` asks
`SandboxAvatar.IsHumanDriven` — which is `IIntentSource.IsHumanInput` and nothing else — across
`SandboxAvatar.Live`. `IsHumanDriven` was added so the two consumers read one property instead of
two copies of the same question.

**The absence, measured** (run 1, an automated run that pops every bubble twice):

```
        A (walker)   celebrateReceived=2 celebratePlayed=0
        B (witness)  celebrateReceived=2 celebratePlayed=0
        C (late)     celebrateReceived=1 celebratePlayed=0
```

`received > 0` with `played == 0` is what makes this an observation about the *gate*; `received==0`
alone would equally be a broken wire. Nothing was written to `user://settings.cfg` because no
`AchievementRuntime` exists on any of those peers to write it.

**The positive control.** The packet asked me to flip the gate in a scratch run and catch the
unlock. I built the flip in permanently instead, as `--celebrate-force` — a test-only probe on the
`--honk-forge` / `--voice-flood` precedent — so the control runs in CI on every suite run rather
than once in a scratch tree. Run 2 above is that control: same session shape, same bots, gate
forced, `celebratePlayed == celebrateReceived > 0` on all three. It reaches exactly one boolean in
`BubbleCelebration`; `AchievementRuntime` is built in `ConfigureAsNetworked`, which never reads it,
so a forced bot **still cannot unlock anything**. In-engine, `SandboxSelfTest` now also checks
`IsHumanDriven` on the same three bodies the achievement gate uses, plus both directions of the
pure decision.

### 5. `ProtocolVersion` unchanged at 14 — PASS (absence + positive control)

```
$ git diff playtest/2026-09-04-combined -- scripts/net/NetProfile.cs scripts/Protocol.cs | grep ProtocolVersion
(no output, exit 1)

$ git diff --stat playtest/2026-09-04-combined -- scripts/net/
(no output — scripts/net/ is untouched entirely)

# positive control: the same pattern on a scratch bump
$ sed -i 's/ProtocolVersion = 14;/ProtocolVersion = 15;/' scripts/net/NetProfile.cs
$ git diff -- scripts/net/NetProfile.cs | grep -n ProtocolVersion
9:-    public const int ProtocolVersion = 14;
10:+    public const int ProtocolVersion = 15;
$ # reverted; NetProfile.cs:110 reads `public const int ProtocolVersion = 14;`
```

### 6. Capture — PASS

`docs/qa/CELEBRATE-1/2026-09-04/` (5 frames) + `docs/qa/CELEBRATE-1/README.md`, produced by
`tests/Run-CelebrateCapture.ps1 -Label 2026-09-04`. A windowed client on the **real `bubbletest`
level** (112 authored bubbles, frozen at noon), captured across the completion:

| Frame | What it shows |
|---|---|
| `CelebCap-9.6s.png` | Before — tally still climbing, nothing on screen. |
| `CelebCap-10s.png` | ~0.2 s in — the line fading up, first sparkles off the head. |
| `CelebCap-10.4s.png` | The beat at full — `x 112`, the line, the burst at its widest. |
| `CelebCap-11.2s.png` | ~1 s in — sparkles gone, line up, world entirely unchanged behind it. |
| `CelebCap-13.2s.png` | After — nothing but the ordinary HUD. |

The one-paragraph plain-language description is at the top of this report and in the capture
README.

### 7. Input is never blocked — PASS, two ways

**Live**, inside the 1.6 s celebration window, on the peer that was celebrating:

```
        input: walker moved 5.320 m inside the 1600 ms celebration window (7 samples)
        input: walker had 9 honk(s) answered at the celebration and 13 by the end of its window
```

Movement is the motor consuming intents; the honk figure is the whole loop — the walker pressed
`H`, the server's authoritative cooldown granted it, the broadcast came back, and its own received
counter moved, four times, while its celebration was on screen. The suite fails loudly and *names
the harness* if the completion does not land inside the walker's motion window, so a green here can
never be vacuous.

**Statically**, an ABSENCE check with a positive control:

```
        blockers: 0 in the celebration path; the same patterns match 1 time(s) in Gameplay.cs (control)
```

The patterns are `SceneTree.*Paused`, `Input.MouseMode`, `SetProcessInput`,
`SetProcessUnhandledInput`, `AcceptEvent`, `GrabFocus`, `MouseFilter`,
`GetViewport().SetInputAsHandled()`.

**Jump specifically**: not probed by a jump-shaped bot, because no scripted intent source in the
repo jumps on a schedule and inventing one is out of proportion. Jump rides the same `MoveIntent`
the movement above proves is being consumed every tick, and the static check shows the celebration
touches no input path at all. Say the word and I will add a `--jump-at` source.

### 8. Both suite halves on the tip

**xUnit** (`dotnet test tests/unit/SailNet.Tests.csproj`), raw lines:

| Tree | Raw line |
|---|---|
| Baseline — `playtest/2026-09-04-combined` @ `418556d`, throwaway detached worktree `C:\repos\Watis-celebrate1-base` | `Failed: 0, Passed: 2269, Skipped: 0, Total: 2269` |
| This branch | `Failed: 0, Passed: 2288, Skipped: 0, Total: 2288` |

+19: 18 in `tests/unit/CelebrateTests.cs`, 1 in `BubbleCounterStateTests` (below).

**Scene marathon** (`powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`),
foreground, redirected to a file and read, never piped. Raw counts from the run's own
`=== summary ===` block with the anchored pattern `^  .+ (PASS|FAIL)$`:

```
rows: 48   PASS: 47   FAIL: 1        OVERALL: FAIL
```

**The one red is `Bike: handling model`, and it is discriminated as load-flaky, not a
regression.** Its own output in that same marathon reads:

```
  BIKE-HANDLING-SELFTEST amplitude_zero_draws_exactly_the_corner_lean_and_nothing_else: PASS - ...
FAIL: bike handling self-test exited -1; see bike-handling-selftest.out.log
```

**All 20 of its checks reported PASS**; only the process exit code failed, on a `GD.PushError` out
of `MovementPlayground` → `SandboxAvatar.ConfigureNetworkedInstance`'s "networked spawn without a
Sync child" branch — a lab-boot complaint, not a check. Discrimination, on an idle machine with
nothing else of mine running:

| Run | Conditions | Checks | Exit | Verdict |
|---|---|---|---|---|
| the marathon | 42 suites already run in the same PowerShell process | **20/20 PASS** | −1 | FAIL |
| standalone ×3, `-SkipBuild` | idle | 20/20 PASS each | 0 | **PASS ×3** |
| the marathon's own three-suite tail (`Run-MovementPlayground` → `Run-BikeSelfTest` → `Run-BikeHandlingSelfTest`) in ONE PowerShell process | idle | 28/28 and 20/20 | 0 | **PASS** |

The third row matters because a plain standalone re-run drops the *process sharing* the MOVE-1
`SAIL_MOVEPG_OUT` trap lives in; reproducing the marathon's ordering in one process is what
separates "load" from "the suite before it". It cleared both. **The raw measured quantity — 20 of
20 checks, with every individual number in them unchanged — never moved.** My diff also cannot
reach the bike: zero changes under `scripts/dev/**` or `scripts/net/**`, and the only file it
shares with that path is `SandboxAvatar.cs`, where the addition is one read-only property nothing
in the bike calls. Recorded as a measured entry in `.claude/rules/test-suite.md`, with the
checks-vs-exit-code discriminator, because this suite's red does not look like the others'.

**`Bubbles: the last one` (the new suite) passed inside the marathon**, on its first registered
run.

### 9. This report — you are reading it

`## Corrections to the packet` is filled, the bible check is above, the Steam gap is below, and
`DECISION-LOG.md` §7 carries every value I picked in a table.

---

## What broke while building it

Two findings, both from the "prove a new test can fail" rule, both now in
`.claude/rules/test-suite.md`.

### The idempotency latch was green for the wrong reason

Deleting `_announcedCompletion` entirely left the whole celebrate suite **green**. That is a fact
about the call graph, not the suite: `TryAnnounceCompletion` is only reachable from `ServerPop`,
which returns early once `BubbleCounterState.TryPop` has no bit left to flip. The latch's branch is
never evaluated a second time.

The discriminating experiment is two mutations, not one:

| Mutation | Result |
|---|---|
| latch removed, call site unchanged | **PASS** — the latch was never what held it |
| announcement reachable per frame, latch **intact** | **PASS** — 2 announcements in the session |
| announcement reachable per frame, latch removed | **FAIL** — **3527** announcements, every peer flooded, and the late joiner greeted by a completion it did not earn |

Five distinct assertions fired on the third, each with the message written for exactly that defect.
**The latch stays**: it is what stops a future second call site from double-announcing, and today
`TryPop` is the wall behind it. Generalisation recorded in the rules file: *to prove a guard, first
make its call site reach it.*

### An existing audit test caught my new file, and it was right to

`BubbleCounterStateTests.NoClientPathCanPopABubble` sweeps `scripts/**` and fails on any file
outside `BubbleCounter.cs` / `Bubble.cs` that calls `ServerPop` — the audit that makes "a client
cannot drive a pop" a swept fact. My completion schedule looped `ServerPop` from its own file and
tripped it. **The fix is not to widen the audit for a harness**: "pop everything" is a counter
operation, so it moved onto the counter as `ServerPopAllForTest`, where the server check and the
per-bubble adjudication are the same ones a real collider gets, and the schedule became a clock.
Because *a method that satisfies an audit by moving has to be audited in its own right*, I added
`OnlyTheCompletionScheduleDrivesTheBulkPop` — exactly one caller in `scripts/**`, and it is the
schedule. The sweep got stricter, not looser.

### A capture bot renders a flat grey world without `--capture-cam`

A `--bot` client builds no `SandboxCamera`. The first capture run wrote frames with a perfectly
correct HUD and toast over the engine's clear colour, and the log says nothing unusual. Recorded in
the rules file; `Run-Shader1Capture.ps1`'s header already called `--capture-cam` mandatory for a
different reason.

### New tests proved able to fail before being trusted

- `TheGateRefusesAPeerWithNobodyDrivingABody` — red with `ShouldCelebrate => true`.
- `Triumph_ClimbsFromFirstNoteToLast` — red with the four notes flattened to one pitch.
- The scene suite — red as tabulated above.

---

## The Steam gap — read this before counting on it

You asked for "one more steam achievement". **There is no such thing in this repository today, for
any of the four achievements that already exist.**

**What exists.** `AchievementStore.cs` writes a boolean per achievement into
`user://settings.cfg`'s `[achievements]` section and reads it back at launch. That is the entire
mechanism: a local file, on one machine, in one profile. `AchievementStore`'s own class doc says so
("contains no Steamworks call, achievement definition or partner-site configuration" — W7-5's
declared scope). Four ids ship: `DuckWalk`, `ToughGuy`, `ToughGuyDuckWalk`, `Bubblholic`. None of
them reaches Steam. Nothing in `scripts/**` calls `SteamUserStats`, `SetAchievement`,
`StoreStats`, `RequestCurrentStats` or `UserStatsReceived` — I swept for all five and the result is
empty.

**What we already have that helps.** More than you might expect. `Steamworks.NET 2024.8.0` is a
real package reference in `WatisWorld.csproj`, and `scripts/net/steam/SteamService.cs` already
calls `SteamAPI.Init()`, pumps `SteamAPI.RunCallbacks()` every frame and shuts down cleanly — all
of it for the P2P relay transport. So the SDK is initialised and callback-pumped in a shipping
build. `deploy/steam/` has a working SteamPipe upload script and a build-script template. The
missing piece is genuinely just the stats API and the definitions behind it.

**The code side — roughly a day, and it is not hard.**

1. A `SteamAchievements` service that requests the user's stats on login, waits for the
   `UserStatsReceived` callback, and exposes `Unlock(AchievementId)`. Steam **will not accept a
   `SetAchievement` before that callback lands**, and this is the single most common way a first
   Steam achievement integration silently does nothing.
2. A string API-name per `AchievementId` (Steam keys on its own identifier, not on our enum), in
   `AchievementCatalog` beside the display names so the three can never drift.
3. A second subscriber on `AchievementUnlocker.Unlocked` — the local store already listens there,
   so Steam becomes a second listener rather than a rewrite. Then `SetAchievement` +
   `StoreStats` (Steam ignores the unlock until you store).
4. Keep the local store. Steam is unavailable in every offline run, every dedicated server, every
   bot and the whole test suite, so the local file has to stay the source of truth for the toast.
5. The bot gate covers this for free, since it sits above `AchievementRuntime` — but it is worth
   re-proving once, because a *spent Steam achievement cannot be un-spent from the client*, unlike
   a line in `settings.cfg`.

**The part only you can do, and no amount of code substitutes for it.** Steam achievements are
**defined on the Steamworks partner site, not in the game**. For each one you must, signed in as
the app admin: create the achievement under *Steamworks → your app → Stats & Achievements*; give
it an **API Name** (the string step 2 above must match exactly, and it is effectively permanent);
give it a display name and description; upload **two 64×64 icons**, achieved and unachieved
(Steam requires both and will not save the row without them); and then **publish** to the
`Stats & Achievements` configuration — publishing is a separate button from saving, and an
unpublished achievement behaves exactly like a typo'd API name, i.e. `SetAchievement` returns
success and nothing happens. You also need the app to actually have an App ID, which the deploy
scripts currently take as a parameter rather than knowing.

**My advice on ordering.** Wire and prove **one** achievement end to end — `Bubblholic` is the
obvious candidate, since you now have a triumph sound firing at exactly the moment it unlocks —
before defining the other three or inventing a fifth. The failure modes above are all silent, and
finding them once with one achievement is much cheaper than finding them four at a time.

---

## Open questions

**The trigger for the new achievement (ripe — it is a direction, not a value).** You asked for "one
more". `Bubblholic` already covers all-bubbles, so the new one needs a different thing to be *for*.
The level now gives us moments it did not have before: completing it **twice in one session**
(possible since the reset lever, and now audibly marked); completing it **with more than one player
in the session** (the tally is shared and nothing currently notices that it was a group effort);
finding the **secret bubble** (`SecretBubble` exists, reveals the level, and touches the tally not
at all — a perfect hidden achievement); or something about the water, the televisions or the
rooftop, all of which have packets of their own. **I did not pick one, per the packet.** Name it and
it is a small packet: one enum value, one catalog row, one rule, one gate check.

**Whether the text line stays.** The sound and the sparkles are the celebration; the line is the
non-audio redundant channel (INTERACTION-BIBLE §8.2). If you would rather the game said nothing,
deleting one line in `BubbleCelebration.Play` removes it and the §8.2 obligation is still met by
the sparkles. I would keep it, but it is the most "UI" thing in the packet and you are the one who
reverts UI.

---

## Files touched

**New:** `scripts/game/bubble/BubbleCelebration.cs`,
`scripts/game/bubble/BubbleCompletionSchedule.cs`, `tests/unit/CelebrateTests.cs`,
`tests/Run-CelebrateTest.ps1`, `tests/Run-CelebrateCapture.ps1`, `docs/qa/CELEBRATE-1/**`.

**Modified:** `scripts/game/bubble/BubbleCounter.cs`, `scripts/game/bubble/BubbleSelfTest.cs`,
`scripts/game/sandbox/SfxLab.cs`, `scripts/game/sandbox/SandboxAvatar.cs`,
`scripts/game/sandbox/SandboxSelfTest.cs`, `scripts/game/Gameplay.cs`,
`scripts/game/BotHarness.cs`, `scripts/LaunchOptions.cs`, `scripts/ui/PhaseToastLayer.cs`,
`scripts/ui/PhaseToastText.cs`, `tests/Run-AllTests.ps1`,
`tests/unit/BubbleCounterStateTests.cs`, `.claude/rules/test-suite.md`, `DECISION-LOG.md`.

**Not touched, per scope:** any `AchievementId` or catalog entry, any Steamworks call,
`scripts/net/**` (zero diff), `BubbleTestLayout.cs`, the sunken TV's material, the picture frame,
`ProtocolVersion`, and every asset directory.

Base is the combined playtest branch; the PR needs re-targeting once #2–#10 land.
