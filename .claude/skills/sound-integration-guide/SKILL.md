---
name: sound-integration-guide
description: Use when starting Sail audio work and the owning sound-* skill is not obvious, when sequencing an audio subsystem build, or when the mix is wrong and the cause could be in any of several layers. The entry point and map for the nine other sound-* skills.
---

# sound-integration-guide

## Overview

The map for the `sound-*` family. It owns **no technique** — every one lives in a member skill.
What it owns is the three things that are wrong when audio work goes wrong before a line is
written: **the wrong skill got invoked, the work got done in the wrong order, or it got done
against a repo that does not exist.**

Read this when you do not yet know which member owns your question, or when you are planning more
than one packet of audio work.

**The family is substrate.** Standing to `vfx-audio-sync` and `vfx-escalation` exactly as
`vfx-particles` stands to the rest of `vfx-*`: it answers *how it is built and what it costs*, and
hands every *what should this mean* question back up. There is no exception to this and no member
that is allowed one.

## Directed, not decided — the family posture

| Layer | Owns |
|---|---|
| `/direct` + `docs/THRILL-BIBLE.md` | which feeling, why there, whether a device is already spent |
| `vfx-audio-sync` | which mode a moment wants — reinforce, withhold, decouple — the sync **ratio**, the two-channel rule, the voice boundary |
| `vfx-escalation` | the session curve, the dread state, the tension budget, the earned breather, the monotonic derivation |
| **`sound-*` (this family)** | how all of the above is built, mixed, spatialised, synthesised, replicated and paid for |

**A `sound-*` skill invoked with no direction behind it has nothing to build.** The repo
`CLAUDE.md` states the rule for the affect layer as *"never invoke one bare — a VFX skill with
no direction behind it has nothing to build"*; the substrate version is stronger, because a
substrate skill will happily produce a technically excellent system that means nothing. If you
arrive here with "make it sound scary" and no named beat, the correct output is *this needs
`/direct` first*, not an implementation.

## The nine, and what each one actually owns

| Skill | Owns | Reach for it when |
|---|---|---|
| `sound-soundscape-construction` | frequency lanes, depth, temporal choreography, ducking under voice | a bed is being built, or is muddy, or buries a teammate |
| `sound-spatial-audio` | attenuation model, `UnitSize`/`MaxDistance`, air absorption, reverb zoning, the listener | a sound needs placing, or nobody can tell where it came from |
| `sound-real-time-effects` | the `AudioEffect*` toolbox — real names, ranges, units, chain order, idempotent bus creation | a bus chain is being built or a filter driven at runtime |
| `sound-optimization` | the ≤24 budget, `SfxLab`'s pool contract, steal policy, structural LOD, measurement | anything is being sized, or CPU scales with player count |
| `sound-procedural-reactive` | seeded scheduling, multi-dimensional state response, recipe parameterisation, scheduler lifetime | variety is needed from an asset budget of zero |
| `sound-threat-audio` | threat-voice synthesis, the properties that read as threatening, proximity expressed in sound | something needs a voice, or its nearness needs to be heard |
| `sound-anticipation-escalation` | the DSP expression of a `tension01` it is **handed** | a build has to become audible |
| `sound-silence-negative-space` | envelope shape, duration tiers, lane withdrawal, the recovery beat | a beat is executed by taking sound away |
| `sound-event-wiring` | dispatch, `ActorFx`/`EventResponse`, the unbuilt bus, replication for cross-client simultaneity | a cue needs a trigger, or fires twice, or lands at different times for different players |

**Frequently confused pairs**, because these are the misroutes that actually happen:

- **`vfx-audio-sync` vs `sound-silence-negative-space`** — *whether and when* a silence is spent
  versus *how the cut is shaped and replicated*. The first is directorial and rationed; the second
  is mechanical.
- **`vfx-escalation` vs `sound-anticipation-escalation`** — the tension *quantity* versus its
  *audible expression*. If you are computing a number, you are in the wrong skill.
- **`sound-threat-audio` vs `vfx-proximity`** — how a voice is synthesised versus the perceptual
  contract for sensing something unseen. `vfx-proximity` owns the rules that intensity is computed
  per-client, never broadcast, and that a bearing may be deliberately stale. Audio does not get to
  hand out a cleaner readout than the family that owns the ambiguity.
- **`sound-spatial-audio` vs `sound-optimization`** — how far a sound carries versus how many may
  carry at once.
- **Anything vs `scripts/voice/`** — the proximity voice pipeline is shipped, tuned and out of
  bounds for every member. World-audio-vs-voice is a **mix** question and lives on the world side.

## What exists in the repo today

The shared reality table. Every member restates the part that bears on it; this is the whole of it
in one place.

Updated 2026-08-08 by the sound-integration packet (`feat/sound-integration`), which consolidated
PR #154's sparse emitters and PR #73's ambient bed onto one branch and gave the mix an actual bus
tree. **Four rows in this table flipped**; the originals are kept in the right-hand column, because
a table that quietly rewrites itself is how a member skill ends up citing a state that was never
true.

| Thing | State on `feat/sound-integration` | Was (`feat/neighbourhood-exterior`, 2026-07-28) |
|---|---|---|
| Audio asset files | **Zero.** No `.ogg`, `.wav`, `.mp3` anywhere. Every sound is synthesised in code. | unchanged |
| Ambient bed | **Ships, in the played camp.** `AmbientBed` — two continuous non-positional `AudioStreamPlayer` loops (day/night), crossfaded off `CycleDriver.Phase`, seamless-spliced, plus `Withdraw`/`Restore`. Its own sparse positional event layer exists but is OFF in the camp; `SparseSfxEmitter` owns that layer there. | **Did not exist.** Every path was one-shot. |
| Event bus | **Does not exist, deliberately.** `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4 — *"build the bus when a second listener actually exists."* | unchanged |
| Buses | **A declared tree, owned by `AudioBuses`.** `Master` → `Sfx`, `Voice`, `PA`, `VoiceCapture`, and `Ambient` → {`Ambient_Bed` (compressor sidechained to `Voice`), `Ambient_Scenery` (clean)}. Still created in code; still no `default_bus_layout.tres`. | Five flat buses, five private `EnsureBus` copies, no layout. |
| `SfxLab` | Ships. 20 synthesised one-shots, a 14-slot pooled `AudioStreamPlayer3D` set, the concurrency budget. `PlayStream3D` now takes an optional bus, so scenery routes to `Ambient_Scenery` instead of the gameplay `Sfx` bus. Synthesis helpers are **still `private static`**. | 12 one-shots; `PlayStream3D` had no bus parameter. |
| `scripts/voice/` | Ships and is tuned. Out of bounds. The `PA` chain is still the only `AudioEffect*` chain **it** owns; `Ambient_Bed`'s duck compressor now *reads* `Voice` as a sidechain detector and adds nothing to it. | unchanged apart from that read. |
| `ActorFx` / `EventResponse` | Ships. Data-driven, `[Export]`-configured audio+VFX co-dispatch. The nearest thing to a bus, and the thing to extend. | unchanged |
| `SettingsPanel` | Master slider, Voice slider, **World Ambience slider** (on the `Ambient` group), mic dropdown. **Still no Sfx slider.** | No Ambient slider. |
| Audio tests | **Two suites.** `dotnet test`'s `SparseSfxScheduleTests` (scheduler determinism/bounds) and `Run-AmbientBedTest.ps1` (night-weight math, fade targets, loop-splice seam, **plus bus topology, `EnsureLayout` idempotency and duck placement** — headless Godot keeps a real bus graph on a dummy driver, so routing is genuinely CI-provable). `CampWorldSelfTest` additionally asserts the bed is in the played world and every scenery emitter is on the ambience lane. | **None.** |
| The bed spec | `docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md`, and PR #73 (`feat/ambient-bed`) — **both now merged in**, history preserved. | Neither was on the branch. |
| PR #87 (`feat/lab-sound-variety`) | **Still unmerged and still lab-only.** Six bed presets, three threat voices, two DSP chains, none of it reachable from the game. `LabSynth` is the strongest harvest candidate (it can produce a looping `AudioStreamWav`, which `SfxLab` structurally cannot); the threat voices are premature while no creature exists. | not in the table |

## The build order — and why the obvious one is backwards

The natural order is architecture first, content later, optimise last. **Against this repo that
order fails at step one twice over**, and correcting it is the main reason this file exists.

**It fails because the bus is a decided non-goal.** Starting with a `GameEvents` autoload builds
the one thing the repo has explicitly deferred, for a reason that still holds: one listener.

**It fails because optimisation cannot come last.** The ≤24 ceiling is not a thing you discover by
profiling later — it is already spent, and it determines *how large the bed is allowed to be*.
Sizing the bed after building it means rebuilding it.

The order that survives contact:

**0. Reconcile the budget.** `sound-optimization`. Nothing else is decidable until you know how
many voices the bed may have. Do this first; it is the constraint every later step is shaped by.

**1. Bus creation and spatial config.** `sound-real-time-effects` for the idempotent `EnsureBus`
shape (a non-idempotent one stacks duplicate effects silently and cumulatively — two members hit
this hazard from opposite directions), `sound-spatial-audio` for attenuation and the listener.
Retrofitting spatial configuration under existing content is genuinely painful, so this part of
the conventional wisdom does hold.

**2. Synthesis plumbing.** `sound-procedural-reactive`. This is a real step, not a formality:
`SfxLab.ToWav` sets no `LoopMode` and `Envelope` decays every recipe to zero, so **the shipped
palette cannot produce a sustained layer at all.** A bed needs plumbing that does not exist inside
a class whose helpers are all `private static`.

**3. The bed.** `sound-soundscape-construction`. This is the critical path, and `THRILL-BIBLE.md`
§9 says so in as many words: *"§6.3's ambient bed is no longer a nice-to-have investment; it is on
the critical path, because wrong silence is unavailable without it."*

**4. The withdrawal.** `sound-silence-negative-space`, inside `vfx-audio-sync`'s replication
contract. Only now, because a subtraction needs something to subtract from — and because a bed has
to have been running long enough to *stop being noticed* before its absence is a beat.

**5. Threat voice and the audible build.** `sound-threat-audio`, `sound-anticipation-escalation`,
both against a bed that exists to cut through and a tension value `vfx-escalation` supplies.

**6. Dispatch.** `sound-event-wiring`. Extend `ActorFx`/`EventResponse`. Build the bus only if a
genuine second listener has appeared — that is the stated trigger, and it is the only one.

**7. Re-measure.** Back to `sound-optimization`, now against real content. The step-0 numbers were
a budget; these are a measurement.

## The arithmetic every member is bound by

`SfxLab` documents it verbatim: *"§4 budgets ≤24 concurrent 3D players INCLUDING the 5 voice
speakers and ambience; 14 one-shot slots keeps the worst case comfortably inside."*

```
  5   voice speakers (VoiceSpeaker, one per remote player)
+ 14   SfxLab one-shot pool slots
─────
 19   spent before any world audio exists
  5   remaining, inside a documented ceiling of 24
```

**Five voices is the entire ambient budget** unless something above it changes. Anything proposing
"8–14 ambient layers" has not done this sum.

**Post-merge (2026-08-08): all five are still free.** The bed landed and spent none of them,
because both of its continuous layers are plain `AudioStreamPlayer`s — non-positional, outside the
3D ceiling entirely — and its positional half rents from the existing 14-slot pool rather than
holding anything open. The count is still 19 of 24. That is the first lever in the list below
being taken deliberately rather than discovered by accident, and it has the price that lever
carries: a non-positional bed sounds identical wherever a player stands, so it contributes nothing
to §5.4's per-player sensory asymmetry. The layer that does is `SparseSfxEmitter`'s, and in the
camp that is the one running.

**The largest lever is `AudioStreamPolyphonic`**, and it is the reason the sum is survivable —
verified doc: *"lets the user play custom streams at any time from code, simultaneously using a
single player."* `PlayStream` / `SetStreamVolume` / `StopStream` give per-layer volume and lifetime
handles on **one node**, which is what makes a multi-layer bed fit inside five. `sound-soundscape-construction`
owns this and marks one thing unverified that decides how far it goes: **whether a polyphonic 3D
player costs one mixer voice or N.** Check that before sizing anything on it. `AudioStreamSynchronized`
is explicitly not the answer — its streams must start together, which forecloses staggered entry.

The other levers, in descending order of honesty: fewer positional layers; non-positional layers
costing an `AudioStreamPlayer` rather than a 3D voice; a smaller one-shot pool; and raising the
ceiling, which requires a profile and a stated reason rather than a preference.
`sound-optimization` owns the reconciliation; every other member cites it.

Two things the ceiling does not cover, and both matter: **node count and CPU are separate budgets
and only one is written down**, and a sustained layer **cannot live in `SfxLab`'s pool at all.**
The second is sharper than "it costs a voice": `Rent` selects on `if (!p.Playing)`, so a continuous
layer rented from that pool is `Playing` forever and never returns — it permanently shrinks the
one-shot pool, or gets stolen mid-bed by the next footstep. A player paused mid-fade is likewise
the most attractive steal target there is. Never rent a pool slot for anything continuous.

## What every member carries and none resolves

Restated once here so no member has to be read to find them.

- **`THRILL-BIBLE.md` §9 — tone.** The **axis was decided 2026-07-26**; the **ratio** is open: how
  often a dread build cashes out absurd versus stays sincere. Ripeness trigger: *the first playtest
  in which anyone is actually frightened* — a ratio cannot be tuned against a feeling the game has
  never produced. This bites the whole family, because almost every audio choice is a tonal
  commitment. Flag what a call would foreclose; never pick.
- **`THRILL-BIBLE.md` §6.2 — the night floor.** `DayNightSky`'s `MinAmbientEnergy` keeps night
  navigable; the night reversal wants darkness to cost sight. A shipped decision in live opposition
  to doctrine. Ripening, not ripe.
- **`THRILL-BIBLE.md` §4.4 — the spike ceiling** is `[research default — pending Talon
  confirmation]`. That marker travels **verbatim** into any code comment or config.
- **`LEVEL-BIBLE.md` §8.3 — `[BLANK — Talon]`**, on whether Sail commits to a dedicated diegetic
  channel that is *always* meaningful. The bible itself says it *"constrains sound design broadly,
  which is why it is a call rather than a detail."* It is the most consequential open question for
  this family, and it cuts against the family's own instincts: if every sound carries system state,
  then generative texture is incompatible by construction and masking-based culling becomes unsafe.
  Build for the reversible case; do not answer it.
- **`LEVEL-BIBLE.md` §8.1 — audio alone is never a sufficient cue**, because the tether severs it
  on purpose. Inherited, not restated per skill. Redundancy means the same ambiguity on another
  channel, not a cleaner readout on a second audio one.
- **`THRILL-BIBLE.md` §12 — the device ledger is forked** as of 2026-07-28 between this branch and
  PR #76, and the next merge must reconcile it. A register that disagrees with itself is how a
  device gets spent twice.

## Diagnosing a mix, in order

Each layer depends on the one before it. Diagnosing out of order wastes the most time.

1. **Does a bed exist, and has it been running long enough to stop being noticed?** This is the
   answer more often than everything below it combined. `sound-soundscape-construction`.
2. **Is it muddy?** Lane overlap before more EQ. Same skill.
3. **Can players localise?** If not, threat cues lose most of their value regardless of quality.
   `sound-spatial-audio`.
4. **Is the tension signal real, or a placeholder?** `vfx-escalation` owns the value;
   `sound-anticipation-escalation` owns whether the mapping is perceptually right — a ramp that
   reads as flat is often a linear-in-Hz mapping rather than a bad curve.
5. **Is silence spent sparingly enough to still land?** `vfx-audio-sync` sets the ratio;
   `sound-silence-negative-space` shapes the event.
6. **Only now:** effect character (`sound-real-time-effects`) and procedural variety
   (`sound-procedural-reactive`).
7. **Optimise last** — against a profile, not a guess. `sound-optimization`. The two common causes
   have opposite fixes, so guessing wrong makes it worse.

**One symptom that inverts the whole list:** a beat that feels frightening in play and flat on a
recording is `THRILL-BIBLE.md` §4.2, and the fix is a *sharper trigger at the moment of dread* —
**not more ambience.** Proposing more atmosphere against a legibility failure is the single most
likely wrong answer this family can give.

## Headless cannot hear

`tests/Run-*.ps1` prove mechanism and replication. They cannot hear a mix, a fade, a steal or a
feeling. `Run-VoiceTest.ps1:30` already states the limit for voice: *"Audio quality/feel is
explicitly not provable here — that is the weekend manual [check]."* This repo has shipped an
entire island rotated 90° through a green suite.

**Genuinely CI-provable**, and worth building because it is cheap:

- envelope offset at an arbitrary point, as a pure function — the seam late-join forces anyway
- seeded scheduler determinism: same seed, same event sequence
- bus wiring: the bus exists, the chain is in the stated order, `EnsureBus` is idempotent under
  repeated calls
- pool logic: steal order, exhaustion, no node handed to two callers, no duplicate returns
- replication: an event landed on every peer, in order, once, and a late joiner got the targeted
  sync

`CyclePhase.FromElapsed` + `CycleSelfTest` is the house precedent for extracting a decision into a
pure static function purely so a headless test can reach it. Note the constraint that keeps
defeating this: **`SfxLab`'s synthesis helpers are all `private static`**, so "extract the pure
math and test it" is blocked by accessibility rather than difficulty.

Everything else needs a headed session with more than one human in it. Run the **full** suite
before commit, never a filtered subset.

## What done looks like

A player who cannot see the screen should be able to answer: is something wrong, roughly where and
how far, is it getting closer, and is this getting worse or resolving — from audio alone.

That is the practical test, with one correction the family must not lose: **§8.3 forbids a cue that
always fires.** A system that answers all four questions reliably, every time, has taught players
exactly when they are safe, which is worse than answering none of them. The target is a channel
that is *usually* informative and *occasionally* lies — and the lying is `vfx-audio-sync`'s to
ration, not this family's to invent.

## Precedent

- **`scripts/game/sandbox/SfxLab.cs`** — the pooling contract, the synthesis-and-cache pattern, and
  the only written budget in the repo. Every member of this family is measured against it.
- **`scripts/game/world/CycleDriver.cs`** — the replication discipline: server owns the input, sim
  tick not wall clock, authority-mode RPC, staleness guard, targeted late-join sync, `Synced` gate.
- **`scripts/game/presentation/ActorFx.cs`** — data-driven audio+VFX co-dispatch that already
  ships, and the reason the event bus can keep waiting.
- **`scripts/voice/`** — proof Sail can already do positional audio properly, and the boundary.
- **`.claude/skills/vfx-particles/SKILL.md`** — the structural model for a substrate family member:
  infrastructure, honest that it serves no doctrine section directly, and explicit that making a
  thing affordable is not an argument that it should exist.

## Caveats

- **No technique lives here.** If this file starts explaining how to build something, it has
  absorbed a member's job. Route, do not implement.
- **No numbers as law.** Every threshold in every member is a starting point with reasoning
  attached, and none has been run against Sail. The ≤24 ceiling is the one number with a written
  source, and even that is a documented budget rather than a measurement.
- **Nothing here has been profiled.** Not on the GTX 970 Forward+ floor, not anywhere. Integrated
  graphics is never a target.
- **Not a resolver.** Four open forks are listed above and this file settles none of them. A guide
  that quietly picks one is worse than a member that does, because it launders the choice into
  every downstream skill at once.
- **The family is documentation ahead of content, and that is the honest description** — with one
  exception worth knowing, because it is the only place a packet can start today.
  **`sound-event-wiring` has real current bite:** an actor-scale cue ships as one `ActorEvent`
  ordinal, one `.tres` row and one `PresentationSelfTest` assertion, no new code. Everything else in
  the family is forward-looking. The value of the rest is real but narrow: it stops the next
  agent shipping the specific bugs the source research would have produced — an enum member that
  does not exist, a compressor attack off by three orders of magnitude, a deprecated limiter on the
  master bus, a shuffle that does not compile, a scheduler that walks its emitter off the map. That
  is worth having. It is not the same as having audio.

*Scope note: written 2026-07-28 against a branch with zero audio assets, no ambient bed, no
atmosphere audio system, no event bus (deliberately), no audio test of any kind, and a
`THRILL-BIBLE.md` §10 register in which every audio device is `unbuilt` — two of them blocked on
missing prerequisites rather than on effort. What exists: `SfxLab`'s pooled synthesised one-shots
and its ≤24 budget, `scripts/voice/`'s tuned proximity pipeline, `ActorFx`'s data-driven dispatch,
and `CycleDriver`'s replication pattern. **Current bite: none for seven of the nine members;
`sound-event-wiring` can ship an actor cue today through `ActorFx`, and for this file specifically
the bite is the build order and the arithmetic — both usable before any audio exists.** First real
test: the ambient bed landing on this branch, sized against the budget in this
file rather than against the research's, with the withdrawal built afterwards rather than
alongside. Passing looks like a bed nobody can describe afterwards, a silence two players react to
in the same second, a late joiner reacting with them, and a profile on the floor spec replacing
every number above with a measured one.*

*Scope note addendum, 2026-08-08 (`feat/sound-integration`). **The first real test named above has
now been taken, and in the stated order:** the budget was reconciled first (step 0), the bus tree
built second (step 1), and the bed landed third (step 3) against the budget in this file — with
the withdrawal deliberately NOT built alongside it. `AmbientBed.Withdraw` exists and has exactly
one caller in the repo, a dev-lab F8 toggle; §6.3's device moved from `unbuilt` to `fresh` and was
not spent. **What that changes for the members:** `sound-soundscape-construction`,
`sound-real-time-effects` and `sound-optimization` now have live code to be measured against rather
than a blank branch, so their bite is no longer zero. `sound-silence-negative-space` is now
*unblocked* — a bed exists to subtract from — but it is still `vfx-audio-sync`'s to ration and
nobody has directed the beat. `sound-threat-audio` and `sound-anticipation-escalation` remain
blocked on things that do not exist (a creature, a `tension01`). **What has NOT changed:** nothing
has been profiled, nothing has been mixed by ear, the ≤24 ceiling is still an unmeasured budget,
and none of the four open forks listed above moved — §6.2's night floor in particular is untouched
and stays untouched. Passing still looks like what it looked like, and the audible half of it is
still a headed session with more than one human in it.*
