# HONK-1 — a goose honk on H, for the mic-less and the shy

**Role:** programming · **Packet:** HONK-1 · **Date:** 2026-09-04
**Branch:** `feat/2026-09-04-honk-1-goose-honk`, based on `playtest/2026-09-04-combined` (`36724b4`)
**Worktree:** `C:\repos\Watis-honk1`

---

## The one-line answer

Peer A presses `H`; every peer within voice range hears a synthesised goose honk coming out of A's
avatar's head, on voice's own falloff curve; a peer past that range receives the message and stays
silent; a mashed key produces one honk per 0.6 s; and no client can make a honk come out of anyone
else's body. No protocol bump, no asset file, no new audio voice.

---

## Corrections to the packet

1. **`VoiceProximityGate` and `VoiceRoute` are not in `scripts/voice/**`.** The packet lists them
   there. `VoiceProximityGate` (and `VoiceRelayDecider`) live in `scripts/net/VoiceProximityGate.cs`;
   `VoiceRoute` is an internal enum at the top of `scripts/voice/VoiceSpeaker.cs`.
2. **`LocalInputIntentSource` is not at `scripts/game/sandbox/LocalInputIntentSource.cs`.** It is a
   class inside `scripts/game/sandbox/IntentSources.cs`.
3. **`WalkFallbackKey`'s doc no longer keeps the taken-key list.** The packet says to read it and
   confirm `H` against it. On this tip it has been trimmed to one sentence — *"Shift is `sprint`,
   Alt is the window manager's on Windows, and the letter keys are the verbs (E interact)"* — and
   enumerates nothing. `FlashlightController`'s doc still cites the old, fuller version. I measured
   the list myself instead; it is quoted in full under **`H` is free**, below, and written into
   `DECISION-LOG.md` §6.3 so the next packet does not have to re-measure it.
4. **`SfxLab.cs:510`'s "low honk" comment is a historical note, not a recipe.** The packet points
   at it as a starting point. The line reads *"Higher f0/f1 than the old low honk, so it reads as a
   rubber-toy squeak"* — the low honk was **deleted**; what is at that line is `Sfx.Squeak`. There
   was nothing to start from, so the goose is new.
5. **The packet's phrasing implies a server-side proximity cull** ("whatever you send must be
   tiny"; "reuse the voice proximity gate's own numbers"). I put the earshot decision on the
   receiving client instead and relay to everyone. Reasoning below under **The one design call
   worth arguing**; the numbers are still voice's own, by alias.

---

## What was built

| File | What |
|---|---|
| `scripts/game/honk/HonkConfig.cs` | Every retunable number in one place. Range and falloff are **aliases** to `VoiceConfig`, not copies. |
| `scripts/game/honk/HonkGate.cs` | The two decisions — cooldown, earshot — as pure functions with no Godot runtime, so `tests/unit` can hammer them. |
| `scripts/game/honk/HonkManager.cs` | The replicated verb. Payload-free request, server-attributed identity, one-int broadcast. |
| `scripts/game/honk/HonkController.cs` | The `H` reader. Declared at runtime, rebind-safe, captured-mouse guarded, never attached headless. |
| `scripts/game/sandbox/SfxLab.cs` | `Sfx.GooseHonk = 22` + `GooseHonkPcm()`, the synthesised call. Plus an optional `unitSize` on `PlayStream3D`. |
| `scripts/net/NetProfile.cs` | `HonkChannel = 17`. **`ProtocolVersion` untouched at 14.** |
| `scripts/voice/VoiceManager.cs` | `HeadPositionOf` made `public` so a honk leaves the body at the exact point a voice does. One word. |
| `scripts/game/Gameplay.cs` | The single construction site + the peer-left hook. |
| `scripts/LaunchOptions.cs`, `scripts/game/BotHarness.cs` | `--honk-at`, `--honk-forge`, `--honk-flood`, and the two JSONL counters. |
| `tests/unit/HonkTests.cs` | 27 tests: cooldown, jitter tolerance, earshot, boundaries, recycled ids, the alias pin, the waveform. |
| `tests/Run-HonkTest.ps1` | The three-cell multi-peer suite. Registered in `Run-AllTests.ps1` by appending. |

### The mechanism, in four sentences

A press calls `HonkManager.ClientRequestHonk`, which sends a **payload-free** `RequestHonk` to the
server on reliable channel 17. The server names the honker from `Multiplayer.GetRemoteSenderId()` —
a field no client can write — runs the authoritative cooldown, and broadcasts `PlayHonk(peerId)`.
Each receiver resolves *where* that honk comes out from the honker's own replicated avatar
(`VoiceManager.HeadPositionOf`, the same point a voice leaves the body), because **no position is
ever sent**. It then decides whether it is in earshot and, if so, rents a slot from `SfxLab`'s
existing one-shot pool and plays the goose at that point with voice's `MaxDistance` and `UnitSize`.

### Which mechanism, and why

The packet asked me to say. **The flashlight's shape for the verb, the voice pipeline's shape for
the sound.** `FlashlightManager` is the repo's payload-free client→server→broadcast verb with an
explicit no-local-prediction stance, and a honk is exactly that verb with the state removed;
`VoiceManager.EmitPosFor` is how a sound gets attached to a speaker's replicated body without the
wire carrying a position, which is both the positional behaviour and the anti-forgery property in
one. Copying voice's *transport* would have been wrong: `VoiceChannel` is unreliable by
construction and carries fifty packets a second per talker, so a honk on it would be both droppable
and queued behind the traffic it substitutes for.

### The one design call worth arguing

**Earshot is decided on the receiving client, not culled at the relay.** The packet's phrasing
points at the server; I did not go there, on purpose.

`VoiceProximityGate`'s class doc records that a server which inspects positions to route audio
*reverses* a deliberate architectural stance, and that flipping it is **"Talon's call, not an
agent's."** The thing that bought that reversal for voice was bandwidth — 120 → 48 KB/s of host
upstream. A honk is at most 1.67 messages per player per second against voice's fifty: there is
nothing to buy, so spending the stance would have been paying a real price for nothing.

The client's decision is not an authority hole. A client deciding what *it* hears cannot make
anybody else hear anything; it uses the honker's replicated avatar (which it is already drawing)
and its own; and the mixer's `MaxDistance` at the same number is still behind it as the final word.

The check earns its place by being **witnessable**: a mixer cutoff is invisible on a headless peer,
so "the far player did not hear it" would be an assertion no test could make. One `if` turns it
into a counter. That is what the absence check in cell 1 reads.

### Protocol

**No bump, and no spare bit or channel was borrowed either — a new one was taken.** `HonkChannel =
17` was the next free number on this base (the ladder's own comment said so; it now says 18). A new
channel constant is not a protocol change: `SightChannel` and `FlashlightChannel` both landed with
that reasoning written down, and the test is the mismatched-build failure mode. Here it is
fail-**silent** — a peer with no `HonkManager` to dispatch to plays no honk, which is yesterday's
state rather than a wrong one. NET-1 keeps the wave's single bump.

---

## `H` is free — the taken-key list, quoted

Three places can claim a key in this repo, and all three were read.

**1. `project.godot`'s `[input]` block**, by *physical* keycode:

| Action | Keycode | Key |
|---|---|---|
| `move_forward` | 87 | W |
| `move_back` | 83 | S |
| `move_left` | 65 | A |
| `move_right` | 68 | D |
| `pause` | 4194305 | Escape |
| `jump` | 32 | Space |
| `interact` | 69 | E |
| `throw` | 81 | Q |
| `voice_ptt` | 86 | V |
| `sprint` | 4194325 | Shift |
| `look_*`, `aim`, `fire` | — | mouse / stick only |

**72 (H) appears nowhere in it.**

**2. Runtime-declared actions.** A repo-wide search for `InputMap.AddAction` returns exactly two
call sites: `FlashlightController` → `flashlight_toggle` on **F**, and `IntentSources` →
`walk` on **Left Ctrl**. Neither is H.

**3. Raw key polling**, which bypasses the action map entirely. The only
`Input.IsPhysicalKeyPressed` calls in `scripts/` are `BikeLayer`'s three — and one of them **is
`Key.H`**: `BikeLayer.RampKey`, the hold-to-sprint ramp toggle.

**That is not a collision and I did not treat it as the packet's STOP condition.** `BikeLayer` is
constructed only by `MovementPlayground`, which is `scenes/dev/MovementPlayground.tscn` — a
separate scene launched on its own, never a child of `Gameplay.tscn`, and one `HonkController` is
never attached to. The two verbs cannot be alive in the same process. **Worth knowing anyway: a
future packet that networks the bike into the game scene would collide**, and this paragraph is the
cheapest place to find that out. It is also in `DECISION-LOG.md` §6.3.

---

## Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | `dotnet build WatisWorld.sln` 0 errors | **PASS** — `Build succeeded. 4 Warning(s), 0 Error(s)`; the same 4 pre-existing warnings the base has. |
| 2 | `H` confirmed free, list quoted | **PASS** — quoted above in full. |
| 3 | Two-peer test proves the honk crosses the wire | **PASS** — cell 1: `near heard 2/2 received at 4 m`. |
| 4 | ABSENCE with a positive control | **PASS** — cell 1: `far heard 0/2 received at 41.8 m` (its *closest approach* across the whole honk window); cell 2 moves the honker next to that same bot and it hears 2. |
| 5 | A forged honk moves nothing | **PASS** — cell 2: `forged honks landed: 0, against 2 real ones on the same counter`. Proven able to fail. |
| 6 | Cooldown proven, numbers quoted | **PASS** — cell 1: **7 presses → 2 honks**. Cell 3: **290 raw requests over 2.0 s → 6 honks**, against a derived ceiling of 8. |
| 7 | `ProtocolVersion` UNCHANGED at 14 | **PASS** — absence check below. |
| 8 | Both suite commands, raw counts | See **Suites**. |
| 9 | Report + bible check + `DECISION-LOG.md` | This document; `DECISION-LOG.md` §6. |

### Criterion 7 — the absence check, with its positive control

```
$ grep -n "public const int ProtocolVersion" scripts/net/NetProfile.cs
110:    public const int ProtocolVersion = 14;

ABSENCE — no changed line assigns ProtocolVersion:
$ git diff scripts/net/NetProfile.cs | grep -cE "^[+-].*ProtocolVersion\s*="
0

POSITIVE CONTROL — the same grep, relaxed to any ProtocolVersion mention, DOES match,
so the zero above is an absence rather than a broken pattern:
$ git diff scripts/net/NetProfile.cs | grep -cE "^[+-].*ProtocolVersion"
1                       (an added <see cref="ProtocolVersion"/> inside the new channel's doc)

BASE vs TIP:
$ git show playtest/2026-09-04-combined:scripts/net/NetProfile.cs | grep "const int ProtocolVersion"
    public const int ProtocolVersion = 14;
$ grep "const int ProtocolVersion" scripts/net/NetProfile.cs
    public const int ProtocolVersion = 14;
```

---

## Suites

**xUnit** — `dotnet test tests/unit/SailNet.Tests.csproj`, on the tip:

```
Passed!  - Failed:     0, Passed:  2250, Skipped:     0, Total:  2250
```

**Baseline, measured myself** in a throwaway detached worktree at
`C:\repos\Watis-honk1-baseline` (`git worktree add --detach ... playtest/2026-09-04-combined`),
not inherited from any report:

```
Passed!  - Failed:     0, Passed:  2223, Skipped:     0, Total:  2223
```

**Delta +27, all of them `HonkTests`.** Nothing pre-existing moved, and nothing is skipped on
either side.

**Scene marathon** — `powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`:

```
=== summary ===  (raw, counted with `^  .+ (PASS|FAIL)$` inside the summary block)
  45 PASS
   2 FAIL
  47 suites

  Honk: proximity + anti-spam   PASS       <- the new suite

  Carry: throw + loose          FAIL
  World: run driver             FAIL
OVERALL: FAIL
```

**Both reds discriminated per `.claude/rules/test-suite.md`, and neither is this branch.**

| Suite | Discriminator | Verdict |
|---|---|---|
| `Carry: throw + loose` | The failing check's NAME is a **staging** failure — *"bot A: prop 1 was never held in this log — the grab never happened, so no throw was staged"* — which is the carry family's documented red. Re-run standalone on the tip **3×: PASS, PASS, PASS**. | load flake |
| `World: run driver` | Named in the rules file's own load-sensitive list. Re-run standalone on the tip **3×: PASS, PASS, PASS**. | load flake |

Neither suite touches a line this branch changed: the honk adds a new node, a new channel constant,
an optional parameter on `SfxLab.PlayStream3D` that every existing call site leaves defaulted, and
three bot flags. The marathon runs 47 suites back to back on a machine that was also running
SHADER-1's and COPY-1's work; the two reds went green 3/3 the moment they had it to themselves.

`Run-HonkTest.ps1` also passed **inside** the marathon, not only standalone.

`tests/Run-HonkTest.ps1` standalone, on the clean tip:

```
[1/3] honk crosses the wire; near hears it, far receives it and stays silent...
      ok (7 presses -> 2 honks; near heard 2/2 received at 4 m, far heard 0/2 received at 41.8 m)
[2/3] positive control: the same far bot, honker moved next to it; plus a forged honk...
      ok (same bot at the same spot heard 2 honks with the honker at 4 m; forged honks landed: 0,
          against 2 real ones on the same counter)
[3/3] the server's own cooldown, against a client that has none...
      ok (290 raw requests over 2s with the client cooldown bypassed -> 6 honks heard,
          ceiling 8; server logged the drop)
PASS
```

### Proving the new tests can fail — five planted faults

Every one of these was built, run, and reverted.

| Fault planted | Expected red | Result |
|---|---|---|
| Pitch arc flattened (`HonkPeakHz = HonkEndHz = HonkStartHz = 300`) | the goose reads as a car horn | `GooseHonk_RisesIntoTheShoutAndFallsAwayFromIt` RED |
| Cooldown loosened by 0.05 s | the throttle is not the stated one | `CooldownBoundary...` + `HeldKeyForThreeSeconds...` RED |
| `AudibleRangeM` retyped as a literal `24.0f` | the alias became a copy | `HonkRange_IsAnAlias_NotACopiedLiteral` RED |
| `PlayHonk` downgraded `Authority` → `AnyPeer` | a forged honk lands | cell 2 RED: *"a FORGED honk landed: the far bot recorded 3 honk events attributed to peers that never honked"* |
| Earshot check bypassed | the far peer hears | cell 1 RED: *"the FAR peer played 2 honks at 45.7 m"* |

**Two of those five found real problems in my own work rather than merely confirming it:**

1. **The alias test was weak and I had claimed it was strong.** A hand-typed `24.0f` passed it,
   because the two numbers agree today. Two numbers that agree today agree whether one is an alias
   or a copy — the copy only reveals itself the day somebody retunes voice and the honk silently
   does not follow, which is exactly criterion 5's failure mode. Replaced with a source-scanning
   test (`DeadNameAuditTests`'s precedent) that requires the constants to be *written* as
   `VoiceConfig` references, with a positive control so a scanner that stopped reading files fails
   instead of reporting clean.
2. **The forgery check was passing vacuously.** With `RpcMode.Authority` deliberately downgraded to
   `AnyPeer`, the suite stayed **green**. Cause: on an ENet **client**, `Multiplayer.GetPeers()`
   returns only the server, so the probe's loop over it attempted **zero** forgeries. The probe now
   reads the replicated player list (`VoiceManager.GetRemotePlayers`) — also the more honest
   attacker model, since an attacker sees avatars, not ENet peers — claims two ids that never
   legitimately honk so a landed forgery is unambiguous, and the suite asserts the *attempt count*
   off the forger's own log before believing any absence.

A third thing was found by a test written before the code it tests: `(10.0 + 0.6) - 10.0` is
`0.5999999999999996`, so the obvious `now - last < Cooldown` refuses a press at exactly the
cooldown. `HonkGate` stores the *next allowed* time instead, which makes the pinned boundary exact
rather than aspirational.

### A review pass, and the bug it found that no test would have

I put the finished diff through an independent correctness review before committing. It returned
six findings; all six are fixed. Two were real bugs, four were the suite being weaker than it
claimed.

**The one that mattered — a held `H` would have honked at half its cadence, at random.** The
client spaces its sends at exactly `CooldownSec`; the server judges them on **arrival**. The gap
the server sees is therefore `Cooldown + (jitter₂ − jitter₁)`, and that term is negative half the
time on any real connection. With both latches set to the same number, every press that is even a
millisecond early through the network is refused — and because a refusal does not advance the
latch, the *next* honk lands a full cooldown after the last granted one. The harder a player held
the key, the worse it would have looked.

That is exactly the swallowed press `HonkGate`'s own boundary note refuses to accept, reintroduced
by the clock split rather than by the bound — and **no test on the branch could have caught it**,
because every test runs on one clock. Fixed by making the authoritative latch the looser of the
pair: `HonkConfig.ServerJitterToleranceSec = 0.05`. That is the ordinary shape of a
client-predicted, server-checked rule, and it costs 9% on the abuse side — a flooder is capped at
1.82 honks/s instead of 1.67.

**The second — a counter named "heard" was counting honks that never played.** `PlayHonk`
incremented `_heard` after the earshot test, but that test fails *open* on an unresolvable avatar,
and with no source position there is nothing to play. A transient spawn race on the far peer would
have produced a suite red blaming the range rule for something that was never a range problem.
Split: an unresolvable **honker** now drops the honk and counts nothing (there is no safe-loud
answer to "where is it"), while an unresolvable **listener** still fails open (there is one to "how
far is it").

**And four places the suite was weaker than its own comments claimed:**

- **Separation was read at the end of the run**, six seconds after the last honk, while the
  assertions claimed to be about the honk window. Now measured *over* that window, and as the
  bound each listener actually needs: the near bot's **max** separation must stay inside the
  cutoff, the far bot's **min** must stay outside it.
- **Cell 3's ceiling was typed beside two constants that live in C#** — the exact rule the script's
  own header claims to follow. The flooder now prints its window and the server's cooldown
  alongside its count, and the runner derives the ceiling from them.
- **Doubles were formatted with the machine's culture** into arguments that `LaunchOptions` parses
  with `InvariantCulture`; on a comma-decimal locale every press mark would be silently dropped.
- A dead helper left over from the discarded zero-crossing pitch estimator, and two doc claims that
  were slightly false (the mismatched-build failure is fail-*harmless*, not fail-*silent* — an old
  peer logs a node-not-found error per honk; and the null JSONL fields are `null` values, not
  absent keys).

One finding I did **not** act on, because it is a coverage fact rather than a defect: the new
`unitSize` argument on `SfxLab.PlayStream3D` is unreachable by any automated test on this branch —
`PlayHonk` returns before it on headless peers, and every suite bot is headless. The line is
correct on inspection (the borrowed pool slot is always written, restoring the engine default when
no caller asks) but it ships unexercised, and the first witness will be Talon's ears.

Both of the multi-peer lessons are now measured entries in `.claude/rules/test-suite.md`, because
neither is about honking:
- a client's transport peer list is not its view of the other players;
- **a client-side copy of a server rule hides the server rule** — seven scripted presses produced
  two honks and the server logged nothing, so the suite was proving the courtesy latch while the
  authoritative one had never executed once. `--honk-flood` (the `--voice-flood` precedent) is what
  reaches it.

---

## The bible check

```
Bibles applied:  INTERACTION (the player presses a key and other players perceive the result),
                 MECHANICS (a server-side cooldown, a boundary, a race, a late-join question).
                 BEHAVIOR does not apply — nothing here moves or decides on its own.
                 LEVEL does not apply — nothing is arranged in space.
                 THRILL was read for §5.1/§10 and is not claimed: this is a comms affordance,
                 not a directed beat, and /direct was not run.

Items checked:   INTERACTION §1 affordance, §2 feedback, §3 physical correspondence,
                 §4 range & timing, §5 multiplayer contention, §8.1 consequence scope,
                 §8.2 redundant channel, §9.2 verb category, §9.3 noise channel.
                 MECHANICS §1 boundary conditions, §3 simultaneous events, §4 idempotency,
                 §5 persistence, §6 numeric extremes. (§2/§2.5 state-transition integrity
                 do not apply — a honk changes no state, and it touches no row of
                 STATE-CASCADE-TABLE.md.)

Result:          Pass, with one gap left open deliberately and reported (INTERACTION §3).
```

**§1 affordance** — there is no world interactable to highlight; the affordance surface is the
keybind, and Talon's foreword is what carries it. Nothing in-game announces `H`. That is the
deliberate-and-said-so branch §1 allows, and COPY-1 owns the settings/how-to-play surface anyway.

**§2 feedback** — the sharpest item, because §2 literally predicts the spam this feature could
cause: *"silent or delayed acknowledgement reads to the player as the input not having registered,
and they press again."* Two answers. A granted press is acknowledged by the honk itself: the honker
is included in the broadcast and hears their own goose at zero distance, full volume. A press
swallowed by the cooldown gets nothing — accepted knowingly, because the silence is **caused and
legible**: the player heard a honk under 0.6 s ago, which is the shortest, clearest cooldown signal
there is. Adding a click or a HUD pip for a refused press would be UI, which the packet puts out of
scope.

**§3 physical correspondence** — **the open gap, reported rather than built.** A honk is a physical
act by a body, and there is no visual tell on the honking avatar: no head bob, no puff, nothing.
Listeners locate it by 3D audio alone. The packet puts visuals out of scope and says to report the
case rather than build it, so: **the audio is locatable without a cue** — it is a positional
one-shot on voice's own falloff at a body a listener can already see — but attribution among two
players standing together is weaker than voice's, because voice drives a mouth
(`VoiceManager.GetVoiceEnvelope`) and a honk drives nothing. `HonkManager.Honked` is left as the
event a later visual would subscribe to. Recommended follow-up, small.

**§4 range & timing** — both pinned rather than inherited: an explicit key press, 24 m, stated in
`HonkConfig` and in the log.

**§5 multiplayer contention** — *"both allowed"*, with a **per-sender** cooldown rather than a
shared one. Six players honking in the same tick is six honks. Pinned by
`CooldownIsPerPeer_OneHonkerNeverGatesAnother`.

**§8.1 scope** — `proximity`, declared, not arrived at by omission.

**§8.2 redundant channel** — this consequence is audio-only with no second channel behind it, and
§8.2's own table is what permits it: *social communication between players* is the row marked "may
be lost", as against run-critical world state, which is not. Cited rather than skipped.

**§9.3 noise channel** — a honk is a noisy verb and would feed the shared noise-propagation channel
if one existed. It does not in this MVP (no threat, no consumer), and I deliberately did **not**
build a private parallel sound-propagation system for it. Flagged for whoever adds that channel.

**MECHANICS §1 boundaries** — both pinned in code *and* in a test. Cooldown **exclusive** at the
bound (a press at exactly 0.6 s is granted; the alternative eats a press on a floating-point tie).
Range **inclusive** at the bound (matching `VoiceRelayDecider`'s own `dist <= radius` rather than
inventing a second convention).

**§3 simultaneous events** — two peers honking on the same tick need no resolution order, and that
is a property rather than an oversight: a honk arbitrates nothing, holds nothing, and mutates no
shared state, so there is no "first wins" to get wrong. The only per-tick write is the honker's own
cooldown stamp, which no other peer's honk can touch.

**§4 idempotency** — the cooldown *is* the double-fire stop, and `HonkGate.TryHonk` is the single
funnel it lives in: `HonkManager` has no path that broadcasts without passing it. A payload-free
request carries no sequence number, so a duplicated packet and a very fast second press are
indistinguishable; the cooldown collapses both, which is wanted in each case.

**§5 persistence / late join** — no persistent state at all. The only server state is the per-peer
cooldown stamp, which is dropped on disconnect (peer ids are recycled — an inherited latch would
silently eat a fresh joiner's first honk; pinned by `RecycledPeerId_AfterForgetPeer...`). **There
is deliberately no late-join dump**, unlike every other replicated manager in this repo: a honk
that happened before you joined is a honk you correctly did not hear.

**§6 numeric extremes** — two peers at 0 m is full volume and six honks stacking, which degrades
through `SfxLab`'s existing oldest-shot-stolen policy. An avatar that has not spawned resolves to
null and **fails open** (delivered), matching `VoiceRelayDecider`'s direction: the failure a player
reports is "nobody could hear me", so the safe direction is loud — and the mixer's own cutoff is
still behind it, so a fail-open cannot produce an audible honk from across the map.

**Audio budget** — `AudioVoiceBudget` reserves all 24 of its documented ceiling with **zero
headroom**, so this allocates no `AudioStreamPlayer3D` of its own and rents from `SfxLab`'s
existing 14-slot one-shot pool through `PlayStream3D`, the single §4-compliant playback path.
`ATMOSPHERIC-VFX-INTEGRATION.md` anti-pattern #19 and the `sound-optimization` skill's *"a second
audio pool does not exist and must not be built"* both point at the same call.

---

## Open questions

1. **A visual tell for the honking body** (INTERACTION §3 / §8.2). Out of scope here and reported
   above rather than built. `HonkManager.Honked` is the event it would hang off. Small; a direction
   call, not a value call.
2. **Whether a refused press should say anything.** Today it is silent by design (§2, above). If a
   playtest shows people mashing, the cheapest fix is a very quiet click on the refused press —
   but that is a feel call, and it wants Talon's ear rather than my judgement.
3. **`Key.H` and the bike.** Free today because `MovementPlayground` is a separate scene. If the
   bike is ever brought into `Gameplay.tscn`, `BikeLayer.RampKey` and the honk collide and one
   moves.

---

## Housekeeping

- Staged by path throughout; never `git add -A`.
- Stayed entirely out of COPY-1's files (`scripts/ui/**`) and SHADER-1's (shaders, materials).
- `.claude/rules/test-suite.md` gained one section, measured entries only, nothing deleted.
- The throwaway baseline worktree `C:\repos\Watis-honk1-baseline` should be removed
  (`git worktree remove`) once this report is read; it is detached and carries no work.
- No working-tree command was run in `C:\repos\Watis_Game` or `C:\repos\Watis-playtest`.
