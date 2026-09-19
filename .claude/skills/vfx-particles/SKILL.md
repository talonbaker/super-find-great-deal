---
name: vfx-particles
description: Use when a Sail VFX layer must run continuously across a 2-6 player session without hitching — atmosphere beds, debris fields, disturbed-foliage bursts, event puffs — or an existing effect drops frames on the GTX 970 Forward+ floor. Owns the particle/instancing substrate: shader-over-simulation, MultiMesh, pooling, distance culling, draw-call batching, the frame budget. Never decides what an effect should make the player feel.
---

# vfx-particles

## Overview

The **performance substrate** for Sail's atmospheric VFX. Research section 6. It answers *how
does this run at a locked frame rate on the floor spec*, and hands every "what should this make
people feel" question back to `/direct`.

**Source:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §6, plus its
*Known collisions with the shipped repo* section, which is binding. **Every code sample in
that file is unverified.** This skill states, per API, whether it checked.

**Perf floor: GTX 970 on Forward+.** This skill owns that constraint for the whole `vfx-*`
family. Integrated graphics is never a target (feedback, 2026-07-23). Anything this skill
approves has to hold at the floor with 6 players, voice decoding on the same CPU, and the
atmosphere layer already running.

## Directed, not decided

`/direct` and `docs/THRILL-BIBLE.md` decide **which** feeling and **why there**. This skill
decides **how it runs**. It executes a direction; it never originates one.

**This skill is the only member of the `vfx-*` family that is infrastructure rather than
affect, and it should be honest about that: it serves no THRILL section directly.** It exists
so the others can run without hitching. The nearest thing to a doctrine link is second-order
and worth stating plainly rather than dressing up:

> A dropped frame during a group panic destroys the exact moment §4.1's **simultaneity**
> condition depends on. Two players perceiving something in the same instant is what makes a
> spike a spike; a 200 ms stutter on one client means they did not. A stutter also reads as
> *the game*, not *the world* — which is the §4.2 legibility failure arriving through the
> renderer instead of through the design.

That is the whole doctrinal claim. Do not manufacture more. If a proposal here starts arguing
about what a particle should *mean*, it has left this skill and belongs in `/direct`.

## When to Use

- An always-on atmosphere layer (dust, haze, wisps, spores, insects) is being added to a level
- An event-triggered burst will fire repeatedly during play (disturbance puffs, impact debris)
- A field of many small repeated elements is needed and per-element simulation is not
- An effect profiles badly, or a HEADED run shows hitching that headless tests did not
- Another `vfx-*` skill needs somewhere to put its particles and there is no pool yet

**Not for:** what an effect signifies (`/direct`); the ambient audio side of any of it
(`vfx-audio-sync`); the session-monotonic escalation curve that drives intensity over time
(`vfx-escalation`); level composition or where atmosphere belongs spatially (`/spec-level`);
creature or asset meshes (`/generate-asset`, `/validate-asset`); UI or menu animation.

It is also **not a licence to add an effect.** Making a thing affordable is not an argument
that it should exist. That argument comes from `/direct` or it does not come at all.

## What exists in the repo today

Read this before proposing anything, because most of the research assumes infrastructure Sail
does not have.

| Thing | State |
|---|---|
| `scripts/game/sandbox/JuiceFx.cs` | The only particle code on this branch. `CpuParticles3D`, one-shot, self-freeing via a `SceneTreeTimer`. A handful per burst. |
| Particle pool | **Does not exist.** |
| Ambient / atmosphere layer | **Does not exist.** No wisps, no dust bed, no `FogVolume` anywhere. |
| Event bus (`AtmosphereEventBus`) | **Does not exist.** The research assumes it throughout. |
| `scripts/game/sandbox/SfxLab.cs` | Does exist, and is the house pooling precedent — see *Precedent*. |
| `docs/ATMOSPHERIC-VFX-INTEGRATION.md` | Referenced by the research, **and it is in the repo** — read it first; it is the family map and overrides the research where they disagree. |

`JuiceFx.Puff` allocates a node per burst and frees it — precisely what the research forbids.
At its current scale (a few landings and bumps, "effectively free" per its own doc comment)
that is a reasonable call, not a defect. **The finding is that it must not be the pattern an
always-on atmosphere layer is built from**, and that the first system needing sustained bursts
is the one that has to build the pool.

`JuiceFx` also documents a bug worth carrying forward: a one-shot with `Explosiveness = 1`
emits at the parent's origin on the frame it enters the tree unless you position first and set
`Emitting` after. Any pool `Rent` has the same ordering hazard.

## Core patterns

### 1. Shader before simulation — the default, not the optimisation

For anything that does not need discrete, countable, physically-reactive elements, a procedural
noise shader on a low-poly volume costs a fraction of a particle system that looks the same.
Reserve real particles for elements the player could in principle count or that react to
something.

The trap is the other direction: a full-screen or large-volume alpha-blended wisp shader is
**fragment-bound and overdraw-heavy**, which is the GTX 970's weakest axis. A wisp volume the
player can walk inside of, at screen resolution, with `depth_draw_never` and `blend_mix`, is
not automatically cheap — it is cheap *per pixel* and expensive *per pixel covered*. Profile
the covered-area case, not the standing-outside-it case.

### 2. MultiMesh for repeated small elements — and the ordering rule the research omits

`MultiMeshInstance3D` with motion animated in the vertex shader from `TIME` and a per-instance
seed is the correct shape for spores, debris, fireflies. The research's sample is right in
spirit and wrong in its setup order.

**Verified against Godot 4.7** (`GodotSharp` 4.7.0 XML docs):

- `INSTANCE_CUSTOM` **is** available to a MultiMesh shader — but only if
  `MultiMesh.UseCustomData` is `true`, and `UseCustomData` "can only be set when
  `InstanceCount` is `0` or less."
- Setting `InstanceCount` "clears and (re)sizes the buffers. Setting data format or flags
  afterwards will have no effect."

So the order is fixed and the research never mentions it:

```
UseCustomData = true   →   InstanceCount = N   →   SetInstanceTransform / SetInstanceCustomData
```

Get it backwards and `INSTANCE_CUSTOM` silently reads zero for every instance — every element
animates in perfect lockstep, which reads as a glitch, not a field.

- `MultiMesh.SetInstanceTransform(int, Transform3D)` exists — **verified**. Its per-call cost is
  **unverified against Godot 4.7**; measure it before deciding it is fine in a loop. The batched
  alternative is the `Buffer` property (and `SetBufferInterpolated` when physics interpolation is
  wanted), which sets the whole field in one go. Prefer `Buffer` for a full rewrite; per-instance
  setters for touching a few.
- `SetInstanceCustomData` takes a `Color` purely as a four-float container, and each component is
  32-bit on Forward+ (16-bit only on Compatibility) — **verified**. Fine for a seed.

Once the field is uploaded, **do not touch it from C# again.** The whole point is that the
animation lives in the vertex shader; a per-frame transform upload gives back everything the
MultiMesh bought.

### 3. Distance culling — and why the research's LOD sample is wrong twice

The research proposes scaling `GpuParticles3D.Amount` by a distance-derived factor each frame.
**Verified against Godot 4.7, this is broken in two independent ways:**

1. *"Changing this value will cause the particle system to restart"* — so a per-frame `Amount`
   write restarts the emitter every frame. The system never produces a steady state.
2. The sample multiplies `system.Amount` by the LOD scale, using the already-reduced value as
   next frame's base. It decays geometrically to the floor and never comes back.

`AmountRatio` is the documented non-restarting alternative — but read the rest of its doc before
reaching for it: *"Reducing the `AmountRatio` has no performance benefit, since resources need to
be allocated and processed for the total `Amount` of particles regardless."* It is an **art**
knob, not a perf knob.

**Therefore real particle LOD is binary or structural**, not a dial:

- Stop emitting entirely past a cutoff, and let existing particles finish rather than popping.
- Hide or free whole systems; a system that is not visible is not simulated.
- Swap representation with distance — a MultiMesh field or a billboard card in place of a live
  emitter — rather than thinning one.

**Two different distance decisions, two different reference points — do not conflate them.**

- **Per-client visual LOD culls against the local camera.** Each client simulates and renders
  its own particle systems; a client only ever needs to know whether *its own* viewer can see
  the effect. Godot's native `GeometryInstance3D.visibility_range_begin` /
  `visibility_range_end` (verified present in 4.7) does exactly this per-instance, with a
  built-in fade margin so the cutoff does not pop. Culling a client-local effect against the
  nearest *player* rather than the local camera is the actual bug: in a 2-6 player session a
  remote player can be "nearest" while the local viewer is still standing in the effect, and
  distance-to-remote-player culling would hide it from the one client that should see it.
- **Server-authoritative *activation* — whether the effect exists at all this frame —
  considers all players.** Spawning or keeping alive a whole ambient system (a dust bed, a
  swarm) is a decision made once, authoritatively, and it should stay resident if *any*
  connected player is within range, using the nearest-player-of-all distance. This is a
  existence/simulation decision, not a per-client render cutoff, and it is what actually saves
  CPU/GPU work rather than merely hiding geometry that is still simulating.

A cutoff around 35 m is a plausible starting point for either decision because it sits
comfortably outside `VoiceConfig.ProximityMaxDistance` (24 m) — past the range where a group is
talking to each other, so a culled effect is one nobody is co-experiencing. **That is a reason,
not a rule.** It is a playtest call.

### 4. Pooling — and the `Restart()` hazard on a rented node

Never `Instantiate` / `QueueFree` a particle node per event in the hot path. Pool them.

The research's pool calls `AddChild`, then `Restart()`, then sets `Emitting = true` on rent.
**Verified against Godot 4.7**, that sequence has a documented failure mode:

- `Restart()` *"restarts the particle emission cycle, clearing existing particles. To avoid
  particles vanishing from the viewport, wait for the `Finished` signal before calling."*
- For a `OneShot` emitter, setting `Emitting = true` *"will not restart the emission cycle unless
  all active particles have finished processing"*, and *"there may be a short period after
  receiving the `Finished` signal during which setting this to `true` will not restart"*.
- `Finished` is emitted **only** by `OneShot` emitters.

So a node returned to the pool while its previous burst is still resolving, then immediately
re-rented, either visibly truncates the old burst or silently fails to start the new one. Both
read as "the effect sometimes doesn't play," which is the hardest class of bug to reproduce.

The house answer already exists — `SfxLab` solves the identical problem for audio: prefer an
idle slot; if none is idle, steal the oldest; keep the pool bounded; re-anchor lazily rather
than reparenting per use. Mirror that shape. Return a node to the pool on `Finished`, not on a
guessed timer, and treat a forced steal as a deliberate visible truncation rather than a
correctness bug.

Also verified: `ParticleProcessMaterial.EmissionShapeOffset` is *"the offset for the emission
shape, **in local space**."* The research assigns a world position to it. On any emitter that is
not at the world origin, the burst lands somewhere else.

### 5. Material identity, not material equality

**Correction to a claim this file used to make:** sharing a material reference does not merge
multiple instances into one draw call — each mesh instance is still its own draw call unless
it goes through `MultiMesh`/instancing. What sharing the *same resource reference* actually
buys, on Forward+'s clustered pipeline, is avoiding redundant pipeline and uniform-binding
state changes between consecutive draws — cheaper draws, not fewer of them. `Material.Duplicate()`
per instance — which the research does in three separate samples — still costs real per-draw
state-change overhead each time, and it also breaks `ART-BIBLE.md` §4.2's shared-material
contract. The advice stands (share the reference; duplicate only when a per-instance uniform
genuinely varies, and prefer `INSTANCE_CUSTOM` or vertex colour when the variation is
per-element rather than per-system) — only the mechanism was misstated.

### 6. Billboards

**Verified against Godot 4.7:** the property is `BaseMaterial3D.BillboardMode`, the enum is
`BaseMaterial3D.BillboardModeEnum` with `Disabled` / `Enabled` / `Particles`. Camera-facing
billboards let a handful of well-lit motes read as volumetric from any angle, which is the
cheapest density there is.

One gotcha the research does not mention: **billboarding discards the mesh's scale** unless
`BillboardKeepScale` is set. A carefully sized dust mote will come out the wrong size.

## The frame budget

Split the cost in two and treat them differently.

**Baseline layer — always on, profiled once, budgeted forever.** The research proposes "well
under 1 ms/frame." That is a **reasonable target to verify by profiling, not a proven number**
— nobody has measured it on a GTX 970, and it did not come from a measurement. Adopt it as the
number to test against and record what the profiler actually says.

**Event layer — spiky, rare, allocation-free.** Pooled so the spike costs no allocation, and
capped so a burst of triggers cannot compound. A cap of about 3 concurrent disturbance zones is
the research's suggestion, and its *reasoning* is the part that matters: the moment a cluster of
triggers is most likely is a group panic, which is exactly the moment a frame-rate cliff is most
expensive. The cap is a playtest call; the reason it exists is not.

**Profiling.** Godot's built-in frame-time monitor plus the GPU/CPU breakdown. Establish whether
the cost is fragment-bound (wisp and fog overdraw) or CPU-bound (too many per-node `_Process`
calls) *before* optimising — the two have opposite fixes and guessing wrong makes it worse.

**Volumetric fog is the most likely single budget breaker in the research document.**
`Environment.VolumetricFogDensity` exists and is exponential — **verified against Godot 4.7**;
`0.0` disables the global effect while still allowing `FogVolume`s. It renders through a
screen-aligned froxel buffer, so its cost is resolution-driven and largely independent of how
much fog you can see. Enable it only with a profiled before/after on the floor spec.

**Headless tests cannot catch any of this.** `tests/Run-*.ps1` (and `Run-AllTests.ps1`) prove
mechanism and logic; they cannot see a frame time or a visual regression. The repo has already
shipped a whole island rotated 90 degrees through a green headless suite. **Any change this
skill touches requires a HEADED run with the frame-time monitor open**, and the observation
recorded — not "looks fine."

## Tuning guide

Everything here is a starting point with a reason attached. None of it is a gate.

- **Fewer, larger, better-lit particles beat many small ones**, on both counts — they read as
  denser and they cost less. If atmosphere looks thin at a high count, the problem is almost
  always lighting, not density.
- **Consistency over density.** A steady low hum of atmosphere that never hitches beats an
  occasional dense burst that stutters. This is the one aesthetic claim this skill makes and it
  is really a perf claim.
- **200 MultiMesh instances** is the research's figure for a debris field. It is cheap at that
  count *because the animation is in the shader* — the number is downstream of pattern 2, not a
  budget in itself. Profile, then pick.
- **Interval irregularity beats interval length** for anything event-driven; a regular cadence
  gets learned. That is §8.3's territory and `vfx-audio-sync` states the rule properly — noted
  here only so a scheduler built in this skill does not default to a fixed timer.

## Integration points

- **`CycleDriver.Instance.Phase`** is the shipped clock. Consume it; author nothing new. Gate
  every read on `CycleDriver.Instance.Synced` — a client renders a zero-initialised phase before
  the first authoritative update lands, and treating that as "phase 0" is the bug `CycleDriver`
  documents as the most likely one in the feature. `DayNightSky` shows the pattern
  (`if (CycleDriver.Instance is not { Synced: true } driver) return;`).
- **`Phase` is cyclic, not monotonic.** It wraps in `[0,1)` over a 120 s default period;
  `CyclesElapsed` counts the wraps. It is *not* the research's one-way 0→1 `DayProgress` over a
  session. Bind escalation straight to `Phase` and it resets every two minutes. Any
  session-monotonic quantity must be **derived and the derivation stated** — that derivation
  belongs to `vfx-escalation`, not here.
- **Multiplayer:** replicate *causes*, derive *effects*. A burst trigger is replicated; the
  particle simulation runs locally on every client. Never replicate per-particle state, and
  never let the server run a full simulation it then streams.
- **`vfx-audio-sync`** owns whether a visual burst gets an audio companion, and usually the
  answer is no. Do not wire a sound into a particle effect from this side.
- **There is no event bus, and `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4 rules it should not be
  built yet.** The research routes everything through a shared `AtmosphereEventBus`; the map
  declines it — it would have one listener (`vfx-audio-sync`) and this repo's YAGNI posture
  (decision D2) already covers a bus with one subscriber. What is needed now is narrower and
  already specified: route firing decisions through `vfx-escalation`'s `ShouldFireEvent` /
  `RegisterEventSpend` pair so the shared tension budget governs every channel.

## Precedent

- **`scripts/game/sandbox/SfxLab.cs`** — the house pooling contract, and the closest thing Sail
  has to a written budget: a fixed pool, prefer-idle then steal-oldest, bounded node count, a
  dedicated mix bus, lazy re-anchoring across scene changes, and an explicit concurrency budget
  ("≤24 concurrent 3D players INCLUDING the 5 voice speakers and ambience"). A particle pool
  should be recognisably the same object.
- **`scripts/game/sandbox/JuiceFx.cs`** — the position-then-emit ordering hazard, documented in
  place after it shipped as a bug.
- **`scripts/game/world/CycleDriver.cs`** — the replication discipline: replicate the input,
  derive the rest.
- External: the research cites general atmospheric-density practice rather than a specific
  title for this section, which is fair — this is engineering, not reference-matching.

## Troubleshooting

- **Hitching on burst triggers** — almost always `Instantiate`/`QueueFree` in the hot path. Pool.
- **A pooled burst intermittently does not appear** — the `Restart()`/`OneShot` hazard in
  pattern 4. Return on `Finished`, not on a timer.
- **Every element in a MultiMesh field moves identically** — `UseCustomData` was set after
  `InstanceCount`, so `INSTANCE_CUSTOM` is zero everywhere. Pattern 2's ordering rule.
- **A burst appears at the wrong place** — either `EmissionShapeOffset` given a world position
  (it is local space) or `GlobalPosition` assigned before the node entered the tree.
- **An emitter that thins out and never recovers** — the compounding `Amount` bug in pattern 3.
- **Frame cost appears only in multiplayer** — check that a trigger is not being replicated *and*
  re-derived locally, firing twice per client.
- **Atmosphere looks flat at high particle counts** — a lighting problem wearing a density
  costume. Give the particles some ambient response or emission before adding more of them.
- **Green headless suite, visibly wrong game** — expected. Headless proves mechanism only. Run
  it headed.

## Caveats

- **No numbers as law.** 200 instances, a 35 m cutoff, 3 concurrent zones, 1 ms baseline — every
  one is a starting point with a stated reason, and every one is a playtest or profiler call.
- **Unverified is stated, not implied.** Anything this skill did not check against
  `GodotSharp` 4.7.0 is marked `unverified against Godot 4.7` inline. Research code is not
  evidence.
- **No affect.** This skill never argues that an effect should exist, only what it costs.
- **Not a resolver.** `THRILL-BIBLE.md` §9's axis is decided (2026-07-26 — horror,
  dread-forward); only the dread/absurd ratio is open. §6.2 (the night ambient floor vs. the
  night reversal) is a live conflict with a shipped decision. Neither is this skill's to
  settle, and neither should be nudged by a perf argument.
- **Cheap does not mean warranted.** The fastest way to wreck atmosphere is to add every effect
  that profiled fine.

*Scope note: written 2026-07-25 against a branch whose only particle code is `JuiceFx.Puff`
(`CpuParticles3D`, instantiate-and-free per burst) — no pool, no atmosphere layer, no
`FogVolume`, no event bus, and no `vfx-*` system consuming this skill yet. Nothing has been
profiled on the GTX 970 floor. Current bite: none. First real test: the first always-on
atmosphere layer in the jungle island, profiled headed on the floor spec with the frame-time
monitor open. Passing looks like a recorded baseline cost, a burst of simultaneous triggers
during a group panic with no visible stutter, and a stated measured number replacing the 1 ms
target in this file.*
