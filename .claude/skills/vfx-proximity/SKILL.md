---
name: vfx-proximity
description: Use when a /direct call wants players to sense how near or which way a threat is without ever seeing it — a vignette deepening toward something, dust drifting away from an unseen point, a disturbance strongest where the thing is, a sometimes-stale bearing. The perceptual contract itself, never a threat's voice — that is sound-threat-audio.
---

# vfx-proximity

## Overview

The **spatial-encoding layer** for TIDE: how far and which way, without what. Distance
gradients around an invisible point, directional drift that gives a bearing, per-player
post-process that differs between two people standing ten metres apart.

This is the strongest co-op lever in the `vfx-*` family, and the reason is one rule
(*Replicate the cause, never the effect*, below) that is easy to break by accident and
destroys the whole point when broken.

**Source of record:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §4, read
against that file's *Known collisions with the shipped repo*, which are binding. Read
`docs/ATMOSPHERIC-VFX-INTEGRATION.md` first — it is the family map and overrides the research
where they disagree.

## Directed, not decided

**`/direct` and `docs/THRILL-BIBLE.md` decide WHICH feeling, WHY there, and whether the
device is already spent (§10 register). This skill decides HOW, in Godot. It executes a
direction; it never originates one.**

Sections this skill serves:

- **§2.3 — cost-to-know.** The strongest device in the researched corpus is a tool that
  reduces uncertainty *and* increases danger. Any sharpening of a proximity cue — standing
  still, using a light, going quiet, moving closer — must **cost** something. A free readout
  is a HUD with extra steps, and this skill's whole surface is the place that mistake gets
  made.
- **§5.4 — information asymmetry is the seed of panic.** Not knowing what is happening to the
  others is worse than knowing something bad is. Anything that makes what each player
  perceives *differ* is a dread multiplier. Proximity is inherently per-player; this skill's
  job is to not throw that away.
- **§7.1 — avoidable but barely.** Sensed before seen. The player needs a little control, not
  much. A threat that kills before it is understood produces frustration, and frustration is
  not dread.

Section that **governs** this skill:

- **§8.3 — reliable telegraphs.** If a cue always precedes a threat, players learn to relax
  in its absence and the cue has taught them when they are safe. This skill's cues must
  therefore *sometimes lie*. See *The one cue that must never lie* — the constraint has a
  hard exception and it is easy to violate by merging two channels that look alike.

**If this skill is invoked with no direction behind it, say so and stop.** A proximity field
with no directed beat is a threat indicator, and a threat indicator is a mechanic, not dread
(§8.1: once categorised, it is a known quantity). Point at `/direct` and wait.

## Replicate the cause, never the effect

**This is the load-bearing rule of the skill and it is not a footnote.**

| Replicated, server-authoritative | Computed locally, never sent |
|---|---|
| the threat's world position | each player's distance to it |
| whether a threat is active at all | each player's intensity value |
| a discrete trigger event | vignette strength, saturation, drift bearing |
| — | any "who is closest" ranking |

**Why.** Broadcasting a single computed intensity gives every player the same reading of the
same thing. That is exactly what `THRILL-BIBLE.md` §5.4 says destroys the seed of panic: the
dread multiplier is that what each player *perceives* differs. A shared number collapses
"wait — do you feel that too?" into "yes, obviously, we both have the same bar." §4.1's
simultaneity condition applies to the **spike**; the build that precedes it is asymmetric on
purpose, and §4.1 conditions 2 and 3 are what convert an asymmetric build into a shared
violation. Broadcast the effect and you have pre-answered the question the players were
supposed to ask each other.

**The channel for that question already exists and it is worth being concrete about.**
`scripts/voice/` ships proximity voice — `VoiceManager`, `VoiceSpeaker`, `VoiceConfig`,
`MuteRegistry` — as an `AudioStreamPlayer3D` with `InverseDistance` attenuation,
`ProximityUnitSize = 6.0f`, `ProximityMaxDistance = 24.0f`, plus a PA route that disables
attenuation entirely. `THRILL-BIBLE.md` §5.1 ranks proximity voice the highest-leverage
instrument in the game and §10 records it as `fresh` — shipped, never used as a device.
**This skill is the first thing that gives it something to carry.** Two consequences worth
designing around rather than discovering:

- The comparison only happens inside 24 m. Players further apart than that *cannot* compare
  notes, which is another layer of asymmetry rather than a bug — but it means a threat field
  wider than voice range produces players who feel something and have nobody to tell.
  Whether that is the point or a fairness problem is a `/direct` call (§7.1), not a value to
  pick here.
- It makes the moment self-documenting (§5.1) and supplies the audio half of §4.6's
  alignment for free.

**Rule 2, subordinate to the first: bearing, not location.** Direction without distance stays
actionable and stays ambiguous. Give a blip, never a pin.

## The device register is why this skill must not free-run

`THRILL-BIBLE.md` §10 tracks decay per device — `unbuilt` / `fresh` / `seeded` /
`spent-here` / `burned`. **Scheduling and frequency are what burn a device**, and both belong
to `/direct`'s register check (step 7) and its write-back (step 9), not to a timer inside a
VFX system.

For this skill specifically the failure has a sharp edge: **the stale-bearing rate is a §8.3
control surface.** A skill that picks its own lie rate, or lies at a fixed cadence, has
authored the reliability of the telegraph — which is the exact thing §8.3 governs. Fixed-rate
lying is itself a reliable pattern. Take the rate from the direction, and write back what was
spent, or §8.1 and §8.3 become unenforceable across sessions and across dispatched agents who
never saw the conversation.

## The one cue that must never lie

`THRILL-BIBLE.md` §8.3 carries its own exception and this skill is where it gets violated:

- A **dread cue** (this skill) hints that something is near. It **must sometimes be wrong**,
  or players decode it and relax in its absence.
- An **urgency cue** (`LEVEL-BIBLE.md` §8.2, owned by `/spec-urgency-cue`) warns that a state
  is changing — the tide is rising, the light is going. It is a **fairness contract** and
  stays honest, always.

**These must not be the same channel.** A proximity vignette and a tide-warning vignette are
the same pixels, the same screen edge, the same instinct to reach for desaturation, and they
will get merged by whoever implements the second one. Keep them visually distinguishable and
say in the code which channel a given cue belongs to. A dishonest urgency cue is not dread;
it is `MECHANICS-BIBLE.md`-level unfairness wearing an atmosphere costume.

## When to Use

- `/direct` has authored a beat where a threat must be sensed before it is seen
- per-player asymmetry is wanted and the implementation needs the authority model right
- a directional cue reads as a minimap arrow and needs diagnosing
- a disturbance needs to be strongest where the threat is (`vfx-disturbance` pattern 1, driven
  from here)

**Not for:**

- deciding that a threat exists, or where it should be → `/direct`, then `/spec-entity` or
  `/spec-pressure` for the thing itself
- the disturbance vocabulary itself → `vfx-disturbance`
- pooling, LOD, MultiMesh, batching, frame budget → `vfx-particles`
- session-monotonic escalation → `vfx-escalation`
- WorldEnvironment, fog, sun, colour temperature → `vfx-lighting`
- `THRILL-BIBLE.md` §9's dread/absurd **ratio** (the axis itself is decided 2026-07-26 —
  horror, dread-forward, atmosphere the primary instrument; ripeness trigger = the first
  playtest where anyone is actually frightened) and §6.2 (night ambient floor vs. night
  reversal — a live conflict between doctrine and the shipped
  `DayNightSky.MinAmbientEnergy = 0.30f`, decision §6d#25, with an unripe trigger).
  **Neither is this skill's to settle.** Flag which side a choice leans on; never overrule the
  shipped decision to get a nicer effect

## Core patterns

### 1. Distance field around an invisible point

A `Curve`-driven falloff sampled per client from the replicated threat position.
`Curve.Sample` is verified present in Godot 4.7. Steep near the point, flat far away — a
gentle even gradient across the whole map dilutes wrongness into generic ambience and reads
as nothing.

Early-out below a small intensity threshold and stop touching shader parameters entirely.
Distance checks are free; per-frame shader-parameter writes on every client regardless of
intensity are not.

**Per-player post-process — read this before writing to an `Environment`.**

- `Environment.AdjustmentSaturation` is **verified present in Godot 4.7**, and its own
  documentation adds a condition the research sample omits: it is *"effective only if
  `Environment.AdjustmentEnabled` is `true`."* Setting saturation alone does nothing. That is
  a correction, not a caveat.
- More importantly: **`DayNightSky` already owns the one `WorldEnvironment`** and its doc
  comment states it deliberately preserves every other Environment knob (glow, fog, SSAO,
  tonemap) that Talon tuned. A proximity effect writing `AdjustmentSaturation` on that shared
  resource is writing into a resource another system drives, and `vfx-lighting` owns that
  territory besides.
- **Preferred: a `CanvasLayer` + `ColorRect` + `ShaderMaterial` vignette on the local
  player's viewport.** It is per-player by construction, it costs one fullscreen blend, and
  it does not fight `DayNightSky`.
- `Camera3D.Environment` exists in Godot 4.7 (verified) as a per-camera override, but whether
  a camera-local override composes cleanly with a scene `WorldEnvironment` in 4.7 — rather
  than replacing it wholesale and dropping the authored sky — is **unverified against Godot
  4.7**. Do not take that route without testing it against `DayNightSky`.

### 2. Directional drift

Small drifting elements — leaves, dust, disturbed insects — moving consistently one way as
the *something went that way* cue. More legible and more in-genre than an arrow, and it fits
§6.5's traces-not-exposition.

- `ParticleProcessMaterial.Direction` is **verified present** ("unit vector specifying the
  particles' emission direction") and `ParticleProcessMaterial.Spread` is **verified present**
  ("initial direction range from `+spread` to `-spread` degrees"). A tight cone gives a clear
  bearing; a wide one gives a diffuse cloud that says nothing.
- **Unverified against Godot 4.7:** whether `Direction` is interpreted in the emitter's local
  space or in world space when `GpuParticles3D.LocalCoords` is `false`. `LocalCoords` is
  documented as controlling whether *particles* use parent or global coordinates; it does not
  state which frame `Direction` is read in. The research assigns a world-space vector
  straight into `Direction`. Until this is tested, either transform into the emitter's basis
  or keep the emitter unrotated — and **state in the code which assumption was made.**
- Mutating `ProcessMaterial` mutates it for every emitter sharing the resource. Pool-owned
  per-instance materials only.
- Emitters come from `vfx-particles`' pool. `Instantiate` / `QueueFree` in the hot path is the
  named failure mode, and it fires during exactly the burst where a stable frame matters most.

### 3. Driving the disturbance layer

Feed intensity into `vfx-disturbance`'s displacement strength so the world bends hardest
where the thing is.

**Name the boundary explicitly when you do this, because the research blurs it:** a foliage
bend is world-space geometry and **everyone in view sees the same bend**. Per-player
asymmetry lives only in post-process, in audio, and in what a given player is close enough to
perceive at all. Decide per cue which side of that line it is on and record the answer. A
"per-player" system that mostly drives shared geometry is not asymmetric; it just looks like
it in the class diagram.

### 4. Stale and wrong bearings

§8.3 in code. A drift direction that points at where the threat *was*, or at nothing.

The research offers ~15–20 % as a rate that keeps ambiguity without breaking trust. That is a
playtest call for a game whose threat has never existed, and per *the device register*
section above it is a §8.3 control surface that belongs to `/direct`. Take the rate from the
direction; make the mechanism configurable; never hard-code a "feels about right" constant
and never make the lie itself periodic.

## Corrections to the research

1. **The `[Rpc(CallLocal = true)]` sample is wrong for this repo — corrected.** The house form
   is `[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = ..., TransferChannel = ...)]`
   with an explicit transfer mode and a dedicated ENet channel. See
   `scripts/game/world/CycleDriver.cs`: `BroadcastPhase` is Authority + `Unreliable` +
   `NetCodec.CycleChannel`, `SyncPhaseTo` is Authority + `Reliable` for targeted late-join.
   A moving threat position is a superseding sample and wants the **`BroadcastPhase` shape** —
   unreliable, own channel, sequence number, latest-wins staleness guard — plus a **targeted
   reliable send on peer connect**, the same `Gameplay.OnPeerConnected` funnel
   `CycleDriver.SendPhaseTo` and `PropManager.SendDumpTo` already ride. A late-joining player
   who has no threat position renders no cue and silently disagrees with everyone else.
   Do not invent a fourth replication idiom; copy `CycleDriver`'s.
2. **`Environment.AdjustmentSaturation` requires `AdjustmentEnabled = true`** — see pattern 1.
3. **`ParticleProcessMaterial.Direction`'s coordinate frame is unverified** — see pattern 2.
4. **`CycleDriver` already exists; do not author a `DaylightController`** — see *Integration
   points*.

Anything not marked verified in this file should be treated as **unverified against Godot
4.7**. Do not transcribe research code as if it were checked.

## Tuning guide

Reasoning, not gates. Every number here is a playtest call.

- **Falloff shape beats falloff radius.** Steep near, flat far: most of the map should sit in
  barely-perceptible territory with a tight zone of real signal. The research's 40 m max
  radius is a starting value, not a law — and note it is wider than the 24 m voice range, so
  at that radius the field reliably produces players who feel something and cannot tell
  anyone. That is a `/direct` decision (§7.1 / §5.4), not a constant to pick here.
- **Cap the ceiling below "obviously a threat indicator."** The target report is "something
  felt off over there," not "the screen went dark near the monster." A cue that crosses into
  legibility has stopped being a §2.1 ambiguity and become a §8.1 exposure.
- **Cost-to-know is a design surface, not a number.** If a player can sharpen the cue,
  sharpening must cost. If it cannot be sharpened at all, the cue is a designer withholding
  rather than a player decision — which §2.3 says is worth strictly less.

## Integration points

- **`CycleDriver.Instance.Phase`, gated on `Synced`.** Never derive anything visible from
  `Phase` while `Synced` is false; the field sits at its zero default until the first
  authoritative update, and treating that as "phase 0" is the bug the driver's own doc comment
  calls the most likely one in the feature.
- **`Phase` is cyclic, not monotonic — this is the trap.** It wraps in `[0,1)` over a 120 s
  default period and `CyclesElapsed` counts the wraps. It is **not** the research's monotonic
  0→1 `DayProgress`. **A skill that binds its escalation to `Phase` resets that escalation
  every two minutes.** Any session-monotonic quantity must be derived (from `CyclesElapsed`,
  a separate driver, or the tide), and **`vfx-escalation` owns that derivation** — consume
  what it exposes rather than solving it here.
- **There is no threat point in TIDE, and this skill cannot create one.** Nothing in the
  shipped loop is proximate to anything: the goat and hoarder are off the current loop, the
  tide is off master, and no entity publishes a position for this to key off. The threat
  position is a **required input** supplied by a bespoke entity (`/spec-entity`, formerly a
  Family via `/spec-family`), a pressure driver (`/spec-pressure`), or an authored point.
  `THRILL-BIBLE.md` §0 — directing a feeling is
  never a licence to grant a capability — and implementing one is not either. Flag the
  dependency; never fabricate a threat so the effect has something to point at.
- **`vfx-disturbance`** consumes intensity from here for its displacement strength; see
  pattern 3 for the shared-vs-per-player boundary.
- **`vfx-particles`** owns the pool, LOD and MultiMesh. LOD distance checks should reuse the
  per-player distances computed here rather than running a second pass.
- **`vfx-lighting`** owns `WorldEnvironment`, fog and sun. Do not write Environment knobs from
  here.
- **`scripts/voice/`** is the channel the asymmetry pays off over — see *Replicate the cause*.
- **Telemetry.** `scripts/telemetry/` ships a live Firebase path. Logging intensity crossings
  per player with timestamps, correlated against voice activity, is buildable today and is
  the only honest way to find out whether the asymmetry actually produced dialogue.

## Precedent

- **Alien: Isolation's motion tracker** — a rough bearing, never a clean location, and using
  it makes noise the threat can hear. The direct model for both rules in this skill.
- **Phasmophobia** — a ghost that responds to isolated players is proximity asymmetry at the
  social layer; this skill is its visual analogue.

## Troubleshooting

- **"Everyone reacted identically; nobody asked each other anything."** You broadcast the
  effect instead of the cause. Check that intensity is computed client-side from the local
  player's distance and that nothing ships a computed value over the wire.
- **"It feels like a minimap arrow."** The bearing is too precise or too reliable — widen the
  cone, and check whether the stale rate is actually being applied or was left at zero.
- **"The vignette fights the sky / the sky flickers."** You wrote to the `WorldEnvironment`
  `DayNightSky` owns. Move to a per-viewport `CanvasLayer` vignette.
- **"Late joiners see nothing."** No targeted reliable send on peer connect. Copy
  `CycleDriver.SendPhaseTo`.
- **"Intensity resets every two minutes."** You bound to `Phase`. See *Integration points*.
- **"Players learned the cue and now relax when it is absent."** §8.3 — the telegraph became
  reliable. Route the rate back through `/direct` rather than nudging a constant.
- **"The saturation change does nothing."** `AdjustmentEnabled` is `false`.

## Caveats

- **No numbers as law.** Radii, falloff curves, stale rates, intensity ceilings — playtest
  calls. State the reasoning and the starting value; never present one as a gate.
- **No feelings.** This skill does not decide that something should be sensed.
- **It does not own the register.** Using a device without a `/direct` write-back is
  unfinished work.
- **It cannot resolve §9's ratio or §6.2**, and a choice that would foreclose the sincere-dread
  build in favour of an unearned absurd payoff gets flagged as doing so.
- **It cannot conjure the threat.** Without a position to be proximate to, everything here is
  a well-typed nothing.
- **Everything here is unbuilt.** No sample in this file has been compiled in this repo.

*Scope note: written 2026-07-25 against a repo that ships proximity voice (`scripts/voice/`,
`InverseDistance`, 6 m unit size, 24 m max, PA route), a server-authoritative cyclic phase
clock (`CycleDriver`), a server-authoritative loose-prop stream, live telemetry — and **no
threat entity in the loop, no threat position on the wire, and no proximity VFX of any kind**.
`THRILL-BIBLE.md` §10 records proximity voice as `fresh` and every device this skill would
serve as `unbuilt`. Current bite: none — the authority model and the API corrections are
checked and usable, nothing is implemented. First real test: the first directed beat with a
real threat point behind it, in a session with two or more players inside voice range.
Passing looks like one player describing a feeling the other does not have yet, out loud, over
proximity voice — and a `/direct` register entry recording that the bearing lied once.*
