---
name: vfx-escalation
description: Use when TIDE's atmospheric systems need one conductor deciding how hard each pushes right now — a session escalation curve, a dread state machine, a tension budget forcing an earned breather, decaying sanctuary value, or a gate stopping several channels spiking at once.
---

# vfx-escalation

## Overview

The **conductor of conductors**. It creates no effect of its own; it decides how hard every
other atmospheric system should be pushing at any given moment, and — more importantly — when
they should all back off.

**Source of record:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §8
(Temporal Progression & Escalation) and §9 (Integration Guide), read together with that file's
closing *Known collisions with the shipped repo*. The collisions are binding and override §8
wherever they disagree — and for this skill, collision 2 is the load-bearing one.

**Serves:** `docs/THRILL-BIBLE.md` §3 (the whole arc — build, peak, release, recovery), §3.4
(recovery must degrade), §4.4 (spikes are rare), §8.6 (unrelenting tension is the most common
way a horror game becomes boring).

**Why it exists at all:** §3.1 says an unbroken build normalises — the player stops being tense
and cannot be made tense again cheaply. Every atmospheric channel scaling its own intensity
independently off the same clock produces exactly that: everything ramping together, forever,
with no valleys. This skill is the thing that makes the valleys, and the valleys are §3.5's
"the director's real work" expressed as code.

## Directed, not decided

**This skill executes a direction. It never originates one.**

- `/direct` and `THRILL-BIBLE.md` decide **which** feeling, **why there**, and — via the §10
  device register — **whether the device is already spent**. §3.5 says rhythm is the director's
  real work: *spacing, not intensity*. The director decides where the beats sit. This skill
  builds the machine that keeps them apart.
- This skill decides **how**, in Godot: what quantity drives the ramp, where the state lives,
  who is authoritative for it, how a downstream system asks "should I fire", and what it costs.

**If you are invoked with no direction behind you, say so and stop.** "Make it escalate" with
no beats named is a request to build a difficulty dial, not dread. The correct response is:
*this needs `/direct` first — name the arc, its build, its break and what stays standing after
the break — then come back.* A tension budget with nothing scheduled against it is an empty
accumulator.

Two things this skill is **structurally unable to resolve** and must carry visibly:

- **`THRILL-BIBLE.md` §9 (tone position) had its axis decided 2026-07-26; its *ratio* is still
  open** — how often a dread build cashes out absurd versus stays sincere, with the ripeness
  trigger *the first playtest in which anyone is actually frightened*. Note that the research's
  own state names — Safe → Uneasy → Afraid → Panic — presume a dread-forward reading. Naming the
  top state `Panic` in shipped code quietly asserts a calibration. Prefer neutral internal names,
  or flag the foreclosure explicitly; do not let an enum become a tone decision.
- **§4.4's spike ceiling is `[research default — pending Talon confirmation]`** — one spike per
  session, a second only if the first was a false alarm. Carry that marker verbatim wherever it
  appears in code comments or config. It is not law and this skill must not enforce it as one;
  §13 already records it as an unripe fork whose trigger is a session that produced two spikes
  where one felt cheap.

## When to Use

- A level has directed beats and something has to keep them from stacking
- Several atmospheric channels exist and are each ramping independently off the clock
- Playtesters report habituation — "stopped noticing after a while" (§8.6)
- A sanctuary or safe window needs to get worse over a session (§3.4)
- A downstream VFX system needs a single place to ask "how hard should I be pushing"
- The question is "why does this feel flat despite everything escalating"

**Not for:** deciding what the player should feel (`/direct`), lighting/fog/colour work
(`/vfx-lighting`), the pressure driver itself (`/spec-pressure` — a pressure driver is a
*mechanic* with an anti-unwinnable guarantee; this is a *presentation* conductor and has no
authority over survivability), the urgency cue (`/spec-urgency-cue`), or level composition
(`/spec-level`).

**Nor for the audible expression of a ramp.** *Added 2026-07-28.* This skill owns the tension
quantity, its derivation, its budget and its valleys. Turning a `tension01` into a filter sweep,
a crescendo tier, an accelerating pulse or progressive distortion belongs to
`sound-anticipation-escalation`, which consumes the two-call downstream contract in pattern 6
rather than tracking any state of its own. That skill carries one correction worth knowing here:
a cutoff frequency lerped linearly in Hz sweeps wrong, because brightness is perceived
logarithmically — so a ramp that reads as flat may be a mapping bug rather than a curve problem.
The wider `sound-*` family is mapped in `sound-integration-guide`.

## `Phase` is cyclic. `DayProgress` is monotonic. This is the whole problem

**Read this before writing a line.**

`CycleDriver.Phase` wraps in `[0,1)` over a 120 s default period. `CyclesElapsed` counts the
wraps. The research assumes a one-way 0→1 ramp across a ~900 s session and writes
`OnDayProgressChanged(float dayProgress)` against it.

**A skill that binds escalation to `Phase` resets its escalation every two minutes.** The state
machine would run Safe → Uneasy → Afraid → Panic → *Safe* on a loop, seven and a half times an
hour, and the tension budget would be the only thing left producing variation. That is not a
subtle bug — it is the feature inverted. §2.2 requires the clock be certain, monotonic and
**inevitable**; a ramp that resets is none of those.

**So: a session-monotonic quantity has to be derived, and the derivation is a stated decision,
not an assumption.** This skill's first output on any invocation is naming which derivation is
in use and why. Whoever builds it writes that down where the next agent will find it.

### The options, none of them yet chosen

| Source | Shape | Cost |
|---|---|---|
| **From `CyclesElapsed`** — e.g. `cyclesElapsed + phase`, normalised against a target session length | Free, already replicated, already sync-gated, already staleness-guarded, already delivered to late joiners | Session length becomes a magic number nobody has decided; `LEVEL-BIBLE.md` §5.1 defers session length entirely. Also inherits `CycleDriver`'s 120 s test-hook overrides, so a short-period test run escalates absurdly fast unless normalised against the *period*, not the cycle count |
| **A separate monotonic driver** — a sibling node modelled on `CycleDriver` | Says exactly what it means; can have its own period, its own curve, its own reason to exist | A second clock. `BUILD-SPEC` §3: "do not build three independent timers." Needs its own sim-tick advance, broadcast, staleness guard, `Synced` gate and late-join path, or it is worse than the thing it replaces |
| **From the tide** — the rising-tide pressure driver | The tide *is* the monotonic, visible, shared, inevitable clock §2.2 describes; escalation would ride the thing players already fear | The tide is on the core-loop stack and **absent from the shipped playtest build**. §10 records it `unbuilt` on master. Also couples presentation to a mechanic that owns an anti-unwinnable guarantee — a coupling that needs `/spec-pressure`'s blessing, not this skill's |

**The choice is open and belongs to Talon and `/direct`, not to whoever implements first.**
`THRILL-BIBLE.md` §13 already carries "one clock or two (tide + cycle)" as an unripe fork whose
trigger is the tide returning to a level alongside the cycle. That fork and this derivation are
the same question wearing a different hat — resolve one and you have resolved the other, which
is precisely why this skill will not resolve either by picking a formula.

**Interim honest position:** build the derivation behind a single named seam (below) so the
choice is one function, not a decision smeared across five systems. Ship the seam; leave the
choice visible.

### The cycle also has a built-in reprieve — flag it, do not fix it

`DayNightSky.NightFactor` runs `{0, 0, 0.1, 0.3, 0.5, 0.6, 1, 1, 0.6, 0}`: night peaks, then
returns to day. Even with a correctly derived monotonic ramp underneath, the *visible* clock
promises the player that dawn is coming. Certainty with a guaranteed escape hatch is a weaker
dread than the research assumes, and it is a genuine design finding for `/direct` — **not** a
licence for this skill to make the cycle one-way.

## Core patterns

### 1. One seam for the monotonic input, pure and testable

The derivation lives in exactly one pure static function, taking the driver's fields as
arguments rather than reaching for `CycleDriver.Instance` — the same seam `CyclePhase.FromElapsed`
gives the wrap arithmetic, and for the same reason: it is directly testable headless, with no
scene tree and no Multiplayer API. `CycleSelfTest.cs` (`--cycle-selftest`,
`tests/Run-CycleTest.ps1`) is the existing home for that kind of test.

```csharp
/// Session-monotonic progress in [0,1]. DERIVATION IS AN OPEN DECISION — see
/// THRILL-BIBLE.md §13 "one clock or two". Currently: <state which option, and why>.
public static float SessionProgress(float phase, int cyclesElapsed, double periodSec, double targetSessionSec)
```

Two hard rules on the caller side:

- **Gate on `Synced`.** `CycleDriver.Phase` and `CyclesElapsed` sit at zero-initialised
  defaults until the first authoritative update lands; treating that as "the session just
  started" is the same class of bug as rendering t=0 first. Copy `DayNightSky._Process`'s
  guard: hold, do not advance a guess.
- **Never let the monotonic value go backwards.** A reconnect delivers a targeted reliable
  `SyncPhaseTo` that can legitimately move `Phase` backwards across a wrap; the derived session
  value must be clamped monotone or a returning player watches the whole game de-escalate.

### 2. The state machine, with its thresholds named as starting points

Safe → Uneasy → Afraid → Panic, emitted as a signal so downstream systems subscribe rather than
poll. `[Signal]` + `EmitSignal(SignalName.X, …)` is the house pattern (`CrashReportDialog`,
`FeedbackPanel`, `HowToPlayPanel`).

**The research's 0.35 / 0.65 / 0.9 are starting points with reasoning, never gates.** The
reasoning: playtesters should be verbally urging each other to leave as the third state
approaches; if that has not happened by roughly two-thirds through a session, the second
transition is too late and should be pulled earlier. That is a playtest instruction, not a
constant. Same discipline as the rest of this repo — author the coverage, flag the tuning, and
if a number ever reads as a gate rather than a dial, it is written wrong.

Also worth naming: **a state machine whose state is legible from the sky is a perfectly
reliable telegraph**, and §8.3 forbids exactly that — if a cue always precedes a threat,
players learn to relax in its absence. The escalation *state* may be readable; what fires
inside it must not be predictable. That is what pattern 3 is for. (Note the boundary: the
`LEVEL-BIBLE.md` §8.2 urgency cue is a fairness contract and *stays* honest. Different channel,
different rules — do not route escalation through it.)

### 3. The tension budget — the anti-desensitisation lever

An accumulator that rises when a player-perceptible event fires and decays with time. Effective
intensity is scaled down while the budget is saturated, so a run of events buys an earned
breather even inside a state that would otherwise be escalating. This is the Alien: Isolation
menace-gauge model, and §8 calls it the single highest-leverage tool in the whole VFX set.

It is also the direct code expression of §3.1 (every build must break) and §8.6 (unrelenting
tension). **If playtesters report habituation, the fix is almost never more intensity — it is
checking whether the budget is actually producing variation in *timing*, or whether events are
firing at a flat rate regardless of recent spend.**

Two corrections to the research's version:

- **`_Process` decay → `_PhysicsProcess`.** `CycleDriver` ticks the sim step precisely so that
  anything derived from it stays in step with physics; a conductor decaying on the render frame
  drifts against the clock it reads. Match the driver.
- **`GD.Randf()` in `ShouldFireEvent` cannot run per-client.** The research rolls locally. In
  multiplayer that gives every player a different set of events, which destroys §4.1's
  simultaneity condition — the thing that makes a reaction contagious rather than individual.
  Two players describing different events is not §5.4 information asymmetry, it is a desync.

  **The server rolls; the outcome is replicated.** Repo attribute form, verified against
  `CycleDriver` / `PropManager` / `VoiceManager`:

  ```csharp
  [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
  private void FireAtmosphereEvent(int eventType, Vector3 position, float intensity) { /* … */ }
  ```

  Reliable, because a dropped one-shot beat is a beat that never happened for that player —
  unlike the phase stream, where the next sample supersedes the loss 0.5 s later.

  The §9 integration guide's rule of thumb still holds and is not in conflict: replicate
  *causes* (the trigger, the position), let each client compute its own *effects* (per-player
  distance, LOD, post). What must never be per-client is the **decision** to fire.

### 4. Degrading recovery (§3.4)

A sanctuary drains the tension budget faster than ambient decay — and **that drain rate itself
shrinks as the session runs**. Never to zero (that removes all relief, which is §3.1 from the
other side) and never staying full (that lets the group wait out the clock, which is §8.7's
permanent safety and ends the clock early).

`LEVEL-BIBLE.md` §5.2 already establishes that the pacing lever is frequency, not amplitude.
§3.4 adds that the valleys must also *shorten*. This pattern is where "shorten" becomes a
number, and the number is a playtest call.

**It has no home yet.** TIDE has no sanctuary, no safe zone and no shelter mechanic in the
shipped playtest build. This pattern is a shape waiting for content.

### 5. What this conductor may never do

- **It has no authority over survivability.** `MECHANICS-BIBLE.md` §10.5's anti-unwinnable
  guarantee has no valid TBD, and `THRILL-BIBLE.md` §7.4 says no amount of directed dread
  justifies a sealed level. A presentation conductor that can make an escape unreachable has
  stopped being a presentation conductor. If escalation ever needs to gate a route, that
  finding belongs to `/spec-pressure`.
- **It may not grant a capability.** `THRILL-BIBLE.md` §0: directing a feeling is never a
  licence to grant one. If a state needs a creature to do something no Family declares, that is
  an entity-contract finding — route it, never absorb it.
- **It may not fire a spike on its own.** §4.1 requires stake, violation, simultaneity and
  legibility, and §4.5 requires the violation be downstream of something the group *chose*. A
  random roll satisfies none of those. This skill can gate *when a directed spike is allowed to
  land*; it cannot manufacture one, and a `syncedPeakChance` float is not a spike system.

### 6. The downstream contract

Two calls, and every other atmospheric system routes through both:

- **"How hard should I be pushing?"** — parameters keyed by current state, looked up in one
  place rather than each system computing its own escalation off the clock. §9's named failure
  mode is that independent per-system curves either all spike together or all plateau early.
- **"Should I fire this?"** — the gated roll, plus registering the spend, so the pullback is
  felt system-wide rather than in one channel.

The research pairs this with an `AtmosphereEventBus` (its §7). **That bus does not exist in
this repo and neither does the audio-sync skill that owns it.** Do not build half of someone
else's skill to have somewhere to send events; define the two calls above, and let the bus
arrive with the skill that owns it.

## Tuning guide — no numbers as law

- **0.35 / 0.65 / 0.9 are starting points**, with the playtest test stated above. So are the
  research's per-state parameter tuples, its decay rate, its 0.4 saturation floor and its
  sanctuary multipliers. None of them has ever been run against TIDE.
- **Variation in *timing* is what makes a ramp not feel like a dial.** If it reads flat, look
  at frequency before intensity.
- **Deltas between states must be large enough to read.** If the top state does not feel
  different from the one below it, the tiers are too close together to perceive and the machine
  is doing nothing a single float could not.
- **Maximum escalation is still "the odds are stacked against us", never "constant assault."**
  §7.1: avoidable but barely. Crossing that line produces frustration, and frustration is not
  dread — it is the Cursed Companions failure, and it reads as unfair rather than frightening.
- **A short `--cycle-period` test run must not produce a nonsense session ramp.** Normalise
  against the period, not the raw cycle count, or every headless test escalates in seconds.
- **This conductor is nearly free; what it turns on is not.** A few float operations per tick.
  The budget it must actually respect is the one downstream — volumetric fog, particles, extra
  draw calls — on a GTX 970 Forward+ floor with zero assumed headroom, in a scene already
  measured as CPU/submission-bound. Never propose integrated graphics as a target. The
  conductor's real perf job is *capping concurrency*: a cap on simultaneously active effects is
  what stops a burst of triggers compounding into a frame-rate cliff at exactly the moment —
  a group panic — when consistent framerate matters most.

## Integration points

- **`CycleDriver.Instance`** (`Phase`, `CyclesElapsed`, `Synced`) — the only clock. Read it,
  never rebuild it; the research's `DaylightController` is rejected for the reasons
  `/vfx-lighting` sets out.
- **`/vfx-lighting`** — downstream. Sky, sun and ambient are genuinely cyclic and bind to
  `Phase` directly; only *escalation* needs the monotonic derivation. Do not make the lighting
  skill wait on this one.
- **`/direct`** — upstream, and the write-back target. A conductor that gates devices without
  §10 knowing which were spent makes §8.1 and §8.3 unenforceable across sessions, which is the
  exact amnesia `/direct` exists to prevent.
- **`/spec-pressure`** — owns the real pressure driver and its anti-unwinnable guarantee. If
  escalation ends up riding the tide, that coupling is specified there, not here.
- **`CycleSelfTest.cs` / `tests/Run-CycleTest.ps1`** — where the pure derivation function gets
  its headless test. Run the full suite before commit, not a filtered subset.
- **Telemetry** (`scripts/telemetry/Telemetry.cs`, live Firebase path) — state transitions and
  event spends are cheap counters and are the only way to check afterwards whether the budget
  actually created variation. Proximity voice (`scripts/voice/`) ships, so correlating event
  timestamps against voice-activity spikes is buildable on what exists, not hypothetical.

## Precedent

- **Alien: Isolation's Director AI / menace gauge** — the model for the pullback. Pulling back
  is not a reward; it is preparation for the next peak, holding overall tension at a constant
  medium rather than a flat maximum.
- **Left 4 Dead's Director** — the canonical proof that a reactive pacing layer beats a
  hand-authored curve, and the reason §8.4 (no scripted set-pieces) and this skill are
  compatible rather than opposed.
- **Risk of Rain 2's continuous difficulty scaling** — a time-driven, non-scripted curve
  reliably producing a climax with no authored set-pieces.
- **In-repo:** `CyclePhase.FromElapsed` is the house precedent for pulling a decision out of a
  Node into a pure static function purely so it can be tested headless. Copy that shape for the
  derivation.

## Troubleshooting

- **"Escalation resets every couple of minutes."** Something is bound to `Phase` instead of the
  derived monotonic value. This is the failure this whole skill exists to prevent.
- **"Everything escalated at once, then plateaued."** Systems are each scaling off the clock
  independently instead of routing through the shared state and gate.
- **"Feels flat despite escalating."** Check whether the budget is producing variation in
  frequency. A pullback that never actually suppresses anything is a no-op with extra steps.
- **"Players report frustration, not dread, at the top state."** §7.1 line crossed. Recovery
  and decay can no longer catch up with the fire rate.
- **"Different players saw different events."** The roll is running client-side. Server rolls,
  replicates the outcome, reliably.
- **"A reconnecting player watched the game get calmer."** The derived session value went
  backwards across a reconnect sync. Clamp it monotone.
- **"The headless test escalated to maximum in four seconds."** Normalised against cycle count
  rather than elapsed time, with `--cycle-period` overriding the period.

## Caveats

- **The derivation is not decided and this skill will not decide it.** Naming the options is
  the deliverable; picking one silently is the failure.
- **No numbers as law.** Every threshold, rate, tuple and multiplier above is a starting point
  with reasoning attached. Playtest outranks all of them.
- **`[research default — pending Talon confirmation]` markers survive into the code.** §4.4's
  spike ceiling is the one that matters here. Carry the marker verbatim; never launder a
  pending default into a constant.
- **Conducts, never composes.** Any "the escalation needs the creature to…" or "…needs the
  route to close" is routed to `/spec-entity` or `/spec-pressure` as a dependency.
- **Verified vs unverified.** Verified against Godot 4.7 (GodotSharp 4.7.0;
  `MpFoundation.csproj` → `Godot.NET.Sdk/4.7.0`) and repo usage: the
  `[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = …, TransferChannel = …)]` attribute
  form (`CycleDriver`, `PropManager`, `VoiceManager`, `SandboxAvatar`); `[Signal]` +
  `EmitSignal(SignalName.…)` (`CrashReportDialog`, `SteamPeer`); `GD.Randf` / `GD.RandRange` /
  `RandomNumberGenerator` (`Carryable`, `BodyLanguageEvaluator`, `NetSim`); `Curve.Sample`.
  **Marked `unverified against Godot 4.7`:** nothing in the research's §8 sample beyond the
  above needed checking, because §8 is almost entirely plain C# — which is precisely why its
  *design* errors (cyclic-vs-monotonic, the client-side roll, the render-frame decay) matter
  more than its API surface. Anything imported from other research sections
  (`AtmosphereEventBus`, `ParticleProcessMaterial`, `FogVolume`) is out of this skill's scope
  and unverified here.
- **The research's `[Rpc(CallLocal = …)]`-only attribute form is wrong for this repo.** See the
  table in `/vfx-lighting`; it is rejected there in full and the rejection applies here too.

*Scope note: written 2026-07-25 against a repo that ships one clock (`CycleDriver`, cyclic,
120 s default) and one consumer of it (`DayNightSky`), and that has **no** escalation system, no
tension budget, no dread state machine, no sanctuary, no atmosphere event bus, no ambient bed,
no pressure mechanic in the shipped playtest build and no directed beat. `THRILL-BIBLE.md` §10
records zero device spends. Current bite: none — every pattern here is forward-looking, and the
one thing this skill can do today is stop the next agent binding escalation to `Phase`. First
real test: the tidal island directed by `/direct`, with the monotonic derivation chosen out
loud and the pure function under `Run-CycleTest.ps1`. Passing looks like a session whose
tension, if plotted, has visible valleys — the same test `LEVEL-BIBLE.md` §5.4 sets for pacing,
measured on affect instead of threat count — and a playtester saying it got worse, not that it
got busier.*
