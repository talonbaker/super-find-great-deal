---
name: vfx-disturbance
description: Use when /direct calls for "something is here" without ever showing what — foliage bending with no wind, a ripple with no splash, dust stirring where nobody walked, a prop shifting a few centimetres and settling — built as Godot 4.7 C# and shader work.
---

# vfx-disturbance

## Overview

The **execution layer for environmental disturbance** in TIDE: the four ways the world can
say *something passed through here* without the something ever being renderable. Foliage
displacement, a ripple with no source, presence dust, a nudged prop.

This skill is the **HOW**. It takes a beat that has already been directed and turns it into
Godot 4.7 — a uniform, a pooled emitter, an RPC form, a lifetime. It authors no feeling and
schedules no beat.

**Source of record:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §1, read
against that file's own *Known collisions with the shipped repo*. The collisions are binding
and this skill resolves them rather than transcribing around them — see *Corrections to the
research* below. Read `docs/ATMOSPHERIC-VFX-INTEGRATION.md` first — it is the family map and
overrides the research where they disagree.

## Directed, not decided

**`/direct` and `docs/THRILL-BIBLE.md` decide WHICH feeling, WHY it belongs there, and
whether the device is already spent. This skill decides HOW, in Godot. It executes a
direction; it never originates one.**

Sections this skill serves:

- **§2.1 — ambiguity is the fuel.** Every disturbance must be *explicable after the fact but
  unresolvable in the moment*. "Something pushed that branch" is the target sentence; the
  moment the player can name what, the fuel is spent. A disturbance that fully resolves is a
  §2.1 defect regardless of how good it looks.
- **§6.4 — accumulating wrongnesses.** This is the layer that produces them. Each disturbance
  is one small dissonance that explains nothing and raises alertness slightly. §6.4 is the
  cheapest dread in the bible and it is `unbuilt` in the §10 register — this skill is one of
  the things that would build it.
- **§6.5 — traces, not exposition.** The disturbance *is* the trace. Never a sighting.
  `LEVEL-BIBLE.md` §7.3 governs which tell and when; this skill only governs that what lands
  is evidence.

Sections that **govern** this skill (it can violate them, so it is checked against them):

- **§8.1 — over-exposure.** Frequency and clarity are how this device burns out.
- **§8.4 — scripted set-pieces.** A disturbance that fires at the same place at the same
  point of every run stops surprising on run two. Build the possibility space; take the
  schedule from the director.

**If this skill is invoked with no direction behind it, say so and stop.** A disturbance with
no authored belief to break (`/direct`'s five questions, #1) is ambience, and `THRILL-BIBLE.md`
§2.2 has a word for a level whose feeling rests on ambience: *atmospheric*, which is a
different product from dreadful. Point the caller at `/direct` and wait. Building the vocabulary
speculatively is fine and should be labelled as such; firing it into a level is not.

## The device register is why this skill must not free-run

`THRILL-BIBLE.md` §10 tracks a decay state per device — `unbuilt` / `fresh` / `seeded` /
`spent-here` / `burned`. **Scheduling logic is exactly what burns a device.** Interval,
rotation between the four forms, and whether this level has already had its foliage pass are
register decisions, owned by `/direct` step 7 (register check) and step 9 (write-back).

So the shape of the code matters, not just its content:

- This skill emits **trigger entry points** — `TriggerPass(from, to, duration)`,
  `SpawnGhostRipple(pos)`, `FlagPresenceAt(pos, intensity)`, `GhostNudge(prop, dir)`. It does
  **not** own a `_Process` timer that decides when they fire.
- A scheduler that rolls its own interval and never writes back to §10 is how **§8.1** and
  **§8.3** get violated silently. The fourth foliage pass in one level is a shrug, and the
  register is the only thing in the project that remembers there were three — possibly three
  levels ago, by a dispatched Sonnet who never saw the conversation.
- A prototype harness that self-schedules is acceptable **only** if it is labelled a
  prototype and the register entry is written by hand. An unlabelled self-scheduler is the
  finding.

## When to Use

- `/direct` has directed a beat requiring evidence of passage, and it needs building
- a level's atmosphere pass needs the foliage / water / dust / prop vocabulary wired up
- a shipped disturbance reads as a bug rather than a threat and needs diagnosing
- an existing shader or emitter needs a disturbance channel added without breaking what it
  already does

**Not for:**

- deciding whether the level wants a disturbance at all, or where → `/direct`
- per-player intensity or bearing toward a threat point → `vfx-proximity`
- pooling, LOD, MultiMesh, draw-call batching → `vfx-particles` (see *Particle work routes
  through the pool*)
- the session-monotonic escalation curve → `vfx-escalation`
- WorldEnvironment, fog, sun, sky, colour temperature → `vfx-lighting`
- the ambient bed and wrong silence (`THRILL-BIBLE.md` §6.3) → audio. §6.3 is an
  **investment before it is a technique**: TIDE has no ambient bed, so the device cannot be
  spent and must not be claimed in the register
- `THRILL-BIBLE.md` §9's dread/absurd ratio and §6.2 (night floor vs. night reversal).
  **Neither is this skill's to settle.** §9's axis is decided 2026-07-26 (horror,
  dread-forward, atmosphere the primary instrument); only the ratio stays open (ripeness
  trigger = the first playtest where anyone is actually frightened). §6.2 is a live conflict
  between the doctrine and a shipped decision (`DayNightSky.MinAmbientEnergy = 0.30f`,
  decision §6d#25) with an unripe trigger. Flag which side a choice leans on; never overrule
  the shipped decision to get a nicer effect

## Core patterns

### 1. Foliage displacement without a wind source

**The travelling origin is the whole effect.** A static pulse reads as wind and is safe; an
origin with a trajectory triggers agency detection. If the effect goes unnoticed, add travel
before you add magnitude.

House shader form — **compute world position in `vertex()` from `MODEL_MATRIX` and pass it as
a varying.** Both shipped shaders already do exactly this
(`resources/shaders/CheckerFloor.gdshader`, `resources/shaders/DecorativeWater.gdshader`,
each with `varying vec3 world_pos;`). Match them. Do **not** reach for `WORLD_POSITION` —
see *Corrections* #3.

`COLOR.r` as a per-vertex bend weight, as the research writes it, **has no backing in this
repo**: there is no foliage mesh with an authored vertex-colour channel, and there is no
foliage shader at all. Two honest routes, and the skill states which it took:

- author the channel in Blender per `docs/BLENDER-EXPORT.md` and make the mesh a dependency, or
- derive a weight from height (`VERTEX.y` normalised by a uniform) as a stand-in and record
  that the real channel is still owed.

Cost control: keep a `disturbance_active` bool uniform and early-out in `vertex()`, and stop
touching the material from C# entirely when strength reaches exactly `0.0` (dirty flag). The
branch is uniform across the draw call so it should be near-free — *profile it on the GTX 970
floor rather than believing that sentence*.

Multiplayer: **replicate the trigger, never the per-vertex state.** House RPC form only (see
*Corrections* #1).

### 2. Ripple with no splash

The withheld companion cue is the device. A ripple that arrives with a splash sound is a
ripple; a ripple that arrives in silence violates an expectation the player did not know they
had.

**Do not file this under §6.3.** §6.3 is *withdrawal of an established ambient bed* and
requires a bed TIDE does not have. This is the cheaper cousin — withholding an *event* sound
— and it belongs in the register as its own line, not as a spend against wrong silence.

Integration, specifically: `DecorativeWater.gdshader` already exists and already carries the
machinery — two scrolling sine layers, an analytic slope, a view-dependent tint. A radial
ripple term added to that same slope computation is cheaper and more consistent than a second
material. **Read that file's header comment first:** `specular_disabled` is load-bearing (the
material it replaced peaked at 1.2766 linear in the sun-mirror case, over the 1.0 glow gate),
so any new emissive or specular contribution from a ripple must stay under the gate or the
sea blooms.

Prefer **N ripple slots in one material** (array uniforms + a start-time per slot) over
`Duplicate()`-ing a `ShaderMaterial` per ripple. Duplicated materials are distinct resource
references and stop batching even when their values are identical.

### 3. Presence dust

The tell is the *transition* from no dust to dust at a specific point — not ambient dust that
is always on.

- The emitter comes from `vfx-particles`' pool. `Instantiate` / `QueueFree` in the hot path
  is the named failure mode.
- `ParticleProcessMaterial.EmissionShapeOffset` — **verified present in Godot 4.7**, and its
  own documentation says *"in local space."* The research assigns a **world** position to it,
  which is wrong unless the emitter sits at the origin. Either `ToLocal()` first or — better —
  move the pooled emitter node and leave the offset alone.
- Mutating a `ParticleProcessMaterial` mutates it for **every** emitter sharing that resource.
  Pool-owned per-instance materials, or move nodes instead of editing shared state.
- Changing `Amount` while emitting restarts the system; `GpuParticles3D.AmountRatio` is
  documented (verified, Godot 4.7) as *not* restarting and *not* affecting already-emitted
  particles — but it also carries an explicit note that it yields **no performance benefit**,
  since the full `Amount` is still allocated and processed. Use `AmountRatio` for smooth
  intensity, `Amount` for budget. Do not use `AmountRatio` believing it saves cost.

### 4. Ghost nudge

Small impulse on a small prop. A prop that flies is a scripted event (§8.1); a prop that
shifts a few centimetres and settles is something brushing it.

**Server-authoritative, and this is a repo fact rather than a preference.** TIDE already runs
loose-prop physics on the server and streams transforms down
(`scripts/game/props/PropManager.cs` → `StreamLoose`, on its own ENet lane
`NetCodec.PropChannel = 4`). A client-side `ApplyImpulse` fights that stream and produces a
prop that snaps back. Apply the impulse on the server; the existing stream carries the result
to everyone with no new replication.

## Corrections to the research

Made against the shipped repo and against GodotSharp 4.7.0's own API documentation. Each is a
correction the research file does not contain.

1. **The `[Rpc(CallLocal = true)]` / `[Rpc(CallLocal = false)]` samples are wrong for this
   repo — corrected.** The house form is
   `[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = ..., TransferChannel = ...)]`, with
   an explicit transfer mode and a dedicated channel. See `scripts/game/world/CycleDriver.cs`
   (`BroadcastPhase` — Authority + `Unreliable` + `NetCodec.CycleChannel`; `SyncPhaseTo` —
   Authority + `Reliable` for targeted late-join). A disturbance trigger is a discrete,
   non-superseding event, so it wants **Reliable** — unlike a phase sample, a dropped
   disturbance is not replaced by the next one.
2. **`ParticleProcessMaterial.EmissionShapeOffset` is local space** — see pattern 3. Verified
   present; the research's world-space assignment is the bug.
3. **`WORLD_POSITION` is not available in a spatial fragment shader in Godot 4.7 — verified.**
   In the 4.7 engine binary the token appears only clustered with fog-shader built-ins
   (`OBJECT_POSITION`, `UVW`, `SDF`, `SIZE`, and the `height_falloff` fog snippet); it is a
   `shader_type fog;` built-in. The research's §5 terrain fragment using `WORLD_POSITION.xz`
   would not compile as a spatial shader. Use the house varying pattern.
4. **`Tween.TweenMethod(Callable, Variant from, Variant to, double)` with a `Vector3`
   interpolation is verified** against Godot 4.7 — the API documentation's own example tweens
   a `Vector3` through `Callable.From`. Two lifetime notes the research omits: `CreateTween()`
   binds the tween to the node that created it, and a tween that outlives the material it
   writes to is a `SetShaderParameter` on a freed resource. Kill it in `_ExitTree`.
5. **`CycleDriver` already exists; do not author a `DaylightController`.** See *Integration
   points*.

Anything not in this list and not marked verified elsewhere in this file should be treated as
**unverified against Godot 4.7**. Do not transcribe research code as if it were checked.

## Particle work routes through the pool

`vfx-particles` owns pooling, distance LOD, `MultiMeshInstance3D` and draw-call batching.
This skill references it; it does not instantiate particle nodes. `Instantiate` / `QueueFree`
per event in the hot path is the named failure mode and it fires precisely during a burst —
the moment a group panic most needs a stable frame.

If `.claude/skills/vfx-particles/` is not present yet, that is a **dependency flag**, not
licence to instantiate. Say so and build the trigger surface against the pool interface you
would want.

## Tuning guide

Reasoning, not gates. Every number below is a playtest call.

- **Interval.** The research suggests one event per 45–90 s of exploration, derived for a
  ~900 s single-session ramp TIDE does not have. Take the *shape* — irregular beats regular,
  and the mean matters less than the variance, because a regular interval is a §8.3 reliable
  telegraph in disguise. Take the number from `/direct`, and from playtest.
- **Rotation between forms.** Never the same form twice in a row; players pattern-match a
  single tell fast. Which forms are still available in *this* level is a §10 register lookup,
  not a local `Random`.
- **Escalation raises frequency and proximity, never clarity.** Showing more is §8.1.
- **"Edge of peripheral vision" does not survive co-op, and this is an open finding.** The
  research assumes one camera. TIDE has 2–6 players facing six directions; there is no single
  periphery, and a placement that is edge-of-frame for one player is dead-centre for another.
  Options are (a) accept that someone gets it centred, (b) place against the group centroid
  and heading spread, (c) place relative to one chosen player and let the asymmetry be the
  point — which is `vfx-proximity`'s §5.4 argument. **This is a fork for `/direct`**, and it
  is not two-sided.

## Integration points

- **`CycleDriver.Instance.Phase`, gated on `Synced`.** Never derive anything visible from
  `Phase` while `Synced` is false — the field sits at its zero default until the first
  authoritative update lands, and treating that default as "phase 0" is the bug the driver's
  own doc comment names as the single most likely one in the feature.
- **`Phase` is cyclic, and this is the trap.** It wraps in `[0,1)` over a 120 s default period;
  `CyclesElapsed` counts the wraps. It is **not** the research's monotonic 0→1 `DayProgress`
  over a session. **A skill that binds escalation directly to `Phase` resets its escalation
  every two minutes** and no one will notice until a playtester says the dread never went
  anywhere. Any session-monotonic quantity must be *derived* (from `CyclesElapsed`, a separate
  driver, or the tide) and **`vfx-escalation` owns that derivation.** Consume what it exposes;
  do not solve it here.
  - Related design finding, flagged not resolved: `DayNightSky`'s `NightFactor` curve
    (`{0,0,0.1,0.3,0.5,0.6,1,1,0.6,0}`) peaks and returns to day, so the clock offers a
    guaranteed reprieve every period. `THRILL-BIBLE.md` §2.2 wants the clock *monotonic and
    inevitable*. That is a genuine design finding for `/direct`, not a defect to silently fix
    by making the cycle one-way.
- **`vfx-proximity`** supplies intensity when a disturbance is sourced from a threat point.
  Note the boundary carefully: a foliage bend is **world-space and shared** — everyone in
  view sees it. Per-player asymmetry lives in post-process and audio, not in geometry.
- **`vfx-lighting`** owns `WorldEnvironment`, fog and the sun. Do not write Environment knobs
  from here; `DayNightSky` already drives that resource and explicitly preserves the rest of
  it.
- **Audio.** There is no ambient bed, so a disturbance currently arrives in silence *by
  absence, not by design*. That is worth saying out loud, because it looks like the wrong-silence
  device and is not one.
- **Telemetry.** `scripts/telemetry/` ships a live Firebase path. Logging every disturbance
  fire with a timestamp is buildable today, and `scripts/voice/` gives the voice-activity
  channel the research's validation section wants to correlate against. This is the cheapest
  way to find out whether a device is burning out before a playtester tells you.

## Precedent

- **The Forest** — cannibals seen at distance and known mostly by effigies and disturbed
  camps. Evidence, not agent; the direct model for every pattern here.
- **Alien: Isolation** — resolving ambiguity costs safety. Walking up to a disturbance to
  confirm it should cost something, or the disturbance is a free readout
  (`THRILL-BIBLE.md` §2.3).

## Troubleshooting

- **"Nobody noticed it."** Add *travel*, not magnitude. A moving falloff origin is far more
  legible than a bright static pulse, and magnitude is the axis that trips §8.1.
- **"It felt like a bug, not a threat."** The spatial logic is broken — something must have
  passed *through* a coherent path, even though the something is never shown. Unexplainable
  is remembered worse than ordinary (§4.3).
- **"Players see it at different times."** The trigger is being rolled locally per client
  instead of replicated. Reliable, Authority, on a channel.
- **"Strength snaps at the top of every cycle."** You bound to `Phase` and it wrapped. See
  *Integration points*.
- **"Stutter when a burst fires."** Almost always missing pooling — `Instantiate`/`QueueFree`
  in the hot path.
- **"The sea blooms when a ripple lands."** You went over the 1.0 linear glow gate that
  `DecorativeWater.gdshader`'s header comment exists to warn about.

## Caveats

- **No numbers as law.** Intervals, radii, impulse magnitudes, decay times — all playtest
  calls. State the reasoning and the starting value; never present one as a gate.
- **No feelings.** This skill does not decide that a place should feel watched.
- **It does not own the register.** Using a device without a `/direct` write-back is
  unfinished work, not a shortcut.
- **It cannot resolve §9's ratio or §6.2**, and a direction that would foreclose the
  sincere-dread build in favour of an unearned absurd payoff gets flagged as doing so.
- **Everything here is unbuilt.** Treat any sample as a starting point that has never been
  compiled in this repo.

*Scope note: written 2026-07-25 against a repo that ships two spatial shaders
(`CheckerFloor`, `DecorativeWater`), zero particle systems, zero foliage, no vertex-colour
bend-weight channel anywhere, a server-authoritative loose-prop stream, and a
server-authoritative cyclic phase clock. Nothing in this skill is implemented in TIDE today
and `THRILL-BIBLE.md` §10 lists "accumulating wrongnesses" as `unbuilt`. Current bite: none —
the API corrections and the repo integration points are checked and usable, the effects are
not built. First real test: the jungle island's first atmosphere pass, directed by `/direct`
before a line of this is written. Passing looks like a playtester naming something that
unsettled them that was not an entity and not a sound anyone authored as a scare — and a
`/direct` register entry showing which form was spent where.*
