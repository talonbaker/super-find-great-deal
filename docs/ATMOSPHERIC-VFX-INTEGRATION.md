# Atmospheric VFX — Integration Guide

**Applies to:** the eight `vfx-*` skills as one system. How they wire together, what order they
get built in, what they share, and what has to exist before any of them can do their job.

**Not a skill.** There is nothing to invoke here. Read this before invoking any `vfx-*` skill,
and read it again before building the second one.

**Source:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §9, corrected
against the repo. Where this file and the research disagree, this file wins — see *Corrections*
below, which lists nineteen places the research's code would not have worked.

---

## 0. The division of labour

```
        WHY / WHAT                          HOW
   ┌──────────────────────┐        ┌────────────────────────┐
   │  THRILL-BIBLE.md     │        │  vfx-* skills (8)      │
   │  /direct             │───────▶│  Godot 4.7 C# + shader │
   │                      │  a     │                        │
   │  which feeling       │  d     │  which node            │
   │  why here            │  i     │  which uniform         │
   │  is the device spent │  r     │  which channel         │
   │  does it read (§4)   │  e     │  what it costs         │
   └──────────────────────┘  c     └────────────────────────┘
              ▲              t              │
              │              i              │
              └──── §10 register ◀──────────┘
                    write-back
```

**A `vfx-*` skill invoked with no direction behind it has nothing to build.** It should say so
and point at `/direct`. Every one of the eight enforces this in its own *Directed, not decided*
section — that is not boilerplate, it is the thing that stops the VFX layer from quietly
becoming the design layer.

The loop closes at `THRILL-BIBLE.md` §10. `/direct` checks the register before authorising a
device and writes back after spending it. A `vfx-*` skill that builds a device nobody
registered has made §8.1 (over-exposure) and §8.3 (reliable telegraphs) unenforceable, because
the next agent has no way to know the trick is used up.

## 1. The family

| Skill | Owns | Reads | Serves |
|---|---|---|---|
| `vfx-escalation` | the conductor: session state machine, tension budget, the `SessionProgress` seam | `CycleDriver` | §3 (arc), §3.4 (degrading recovery), §4.4 (spike ceiling), §8.6 |
| `vfx-lighting` | `WorldEnvironment`, sun/moon, fog, light shafts | `CycleDriver` | §2.2 (the clock), §6.1/§6.2 (prospect-refuge), §6.6 (colour) |
| `vfx-disturbance` | "something passed through here" — foliage, ripples, dust, nudged props | escalation | §2.1 (ambiguity), §6.4, §6.5 |
| `vfx-proximity` | per-player spatial encoding of a threat point | escalation | §2.3 (cost-to-know), §5.4 (asymmetry), §7.1 |
| `vfx-corruption` | persistent physical wrongness left in the world | escalation, lighting | §6.4, §6.5, §6.6 |
| `vfx-pulse` | one slow shared rhythm, many read-only subscribers | escalation | §2.1, §3.5 (rhythm) |
| `vfx-audio-sync` | which sync mode a moment wants, when to withhold, and the ratio — not construction, see below | escalation, `/direct`'s ratio | §6.3 (wrong silence), §4.2 (the Phasmophobia test), §4.6 |
| `vfx-particles` | the substrate: pooling, MultiMesh, LOD, batching, the frame budget | everyone | **nothing directly** — infrastructure |

`vfx-particles` is deliberately the odd one out and says so in its own file. It serves no
THRILL section. It exists so the other seven can run without hitching, and the second-order
link is real but indirect: a dropped frame during a group panic destroys the simultaneity §4.1
requires and the legibility §4.2 requires. Do not manufacture a doctrine link it does not have.

## 2. Build order

1. **`vfx-escalation`** — not `vfx-lighting`, which is where the research starts. The clock
   already exists (`CycleDriver`); what does not exist is the **`SessionProgress` seam**, and
   every other skill's intensity traces back through it. Build the seam first, behind one pure
   static function, and the open derivation question stays one function instead of a decision
   smeared across five systems.
2. **`vfx-particles`** — infrastructure before the content that needs it. Pooling built after
   profiling reveals a stutter is pooling built twice.
3. **`vfx-lighting`** — the largest immediate surface, and the only skill whose patterns act on
   code that already ships.
4. **`vfx-disturbance` + `vfx-proximity`** — together. Proximity feeds disturbance intensity;
   built apart they will not agree on what a threat point is.
5. **`vfx-corruption`** — level-adjacent, parallelisable once the material plumbing exists.
6. **`vfx-pulse`** — a polish layer. Per the Stage 1 benchmark, add it only once the base loop
   already produces dread without it.
7. **`vfx-audio-sync`** — last among the eight, because it listens to the others. **But its
   blocking dependency is first** — see §3.

This reorders the research, which puts lighting first and escalation second. The reason is
`CycleDriver`: the research assumed the clock had to be built, so it front-loaded the clock.
The clock is shipped. The derivation on top of it is not.

## 3. Three things that do not exist, and one that is blocked

### 3.1 The `SessionProgress` seam — build it, leave the choice visible

`CycleDriver.Phase` is **cyclic**: it wraps in `[0,1)` over a 120 s default period, and
`CyclesElapsed` counts the wraps. The research assumes a monotonic 0→1 `DayProgress` over a
~900 s session. **Anything binding escalation directly to `Phase` resets its escalation every
two minutes.**

`vfx-escalation` owns the derivation behind one pure static function and deliberately does not
pick the source. Three candidates, each with a real cost, all documented in that skill.
`THRILL-BIBLE.md` §13's "one clock or two (tide + cycle)" fork and this derivation are the same
question wearing different hats — **resolving either resolves both**, which is why neither gets
resolved by whoever implements first.

Two hard rules for every caller: gate on `Synced`, and clamp the derived value monotone. A
reconnect delivers a targeted reliable `SyncPhaseTo` that can legitimately move `Phase`
backwards across a wrap; unclamped, a returning player watches the entire game de-escalate.

### 3.2 The ambient bed — the highest-value blocker in the set

`THRILL-BIBLE.md` §6.3 records wrong silence as the strongest environmental device available
and §10 records it `unbuilt`. The reason is not effort. **Withdrawal requires something to
withdraw.** TIDE has no ambient bed, so the technique cannot be spent at any price until the
bed is an investment someone has made.

`SfxLab` documents a **≤24 concurrent 3D audio players budget, inclusive of the five voice
speakers and ambience.** A bed is counted *inside* that budget, not added beside it. Nobody has
allocated its slots.

**Added 2026-07-28 — this map predates the `sound-*` family and needs to say where the bed's
construction actually lives.** Building anything audible — layering the bed, frequency
separation, depth, staggered entry, ducking under proximity voice, the synthesis itself — is
delegated to the `sound-*` family (`sound-integration-guide` is its map; `sound-soundscape-
construction` builds the bed specifically). `vfx-audio-sync` keeps only the sync-layer
decisions: which mode a moment wants (reinforce/withhold/decouple), when, and the ratio that
governs how often withdrawal is spent. The "highest-value blocker" framing above still holds —
the bed still has to exist before wrong silence can be spent — it is just built by a different
skill now than it would have been before 2026-07-28.

### 3.3 The afterlife channel — blocks two devices at once

There is no spectator/afterlife audio route. A dead player currently has no way to be heard.
That blocks §5.5 (the teammate's voice cutting out mid-sentence) and §5.6 (failure-as-content)
together — two register entries that turn out to share one missing piece rather than needing
separate work. It lives near `scripts/voice/`, not in `vfx-audio-sync`, which owns world audio
only.

### 3.4 `AtmosphereEventBus` — do not build it yet

The research routes every skill through a shared `AtmosphereEventBus`. All four authoring
passes independently declined to build it, and they were right to.

**It has one listener.** `vfx-audio-sync` is the only system that would subscribe today; the
research's second listener is a creature system the dread research explicitly says not to build
until Stage 1 succeeds. A bus with one subscriber is a direct call with ceremony on top, and
this repo has an established YAGNI posture on exactly this (decision D2, 2026-07-21).

What is genuinely needed now is narrower and already specified: `vfx-escalation`'s two
downstream calls, which every firing skill routes through so the tension budget governs all
channels rather than one.

```csharp
if (_escalation.ShouldFireEvent(baseChance))   // gated by the shared tension budget
{
    _escalation.RegisterEventSpend(cost);      // so the pullback is felt system-wide
    // the skill then does its own visual/audio work, directly
}
```

**Build the bus when a second listener actually exists.** Until then, note in `vfx-audio-sync`
that its inputs arrive by direct call, and revisit. This is a divergence from the research and
it is deliberate.

## 4. Multiplayer authority

The rule is one line: **replicate causes, derive effects.**

| Server-authoritative, replicated | Client-local, derived |
|---|---|
| `CycleDriver.Phase` (already) | per-player proximity intensity |
| threat position | post-process vignette / saturation |
| wrong-silence start and duration | particle LOD |
| synced-peak triggers | pulse waveform sampling (from a replicated seed) |
| every `GD.Randf()` roll that gates a perceptible event | — |

That last row is the one every authoring pass had to correct. The research rolls dice
client-side in three separate samples (`AnomalousFlicker`, `ShouldFireEvent`,
`WrongSilenceController`). **A per-client roll means players experience different events**,
which destroys the simultaneity §4.1 names as a required spike condition. The server rolls; the
result is replicated.

The house RPC form is `[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = …, TransferChannel = …)]`
— see `CycleDriver`. The research's `[Rpc(CallLocal = false)]` is not this repo's form.

Transfer mode is per-event, not universal. `CycleDriver` broadcasts phase **unreliable** because
a dropped sample is superseded ≤0.5 s later. A wrong-silence cut has **no successor to supersede
it** and must be reliable. Late-join needs the targeted `SendPhaseTo`/`SendDumpTo` treatment or a
peer connecting mid-silence hears a bed that should be absent.

**The per-player asymmetry is a feature and it has a hard limit.** It holds for post-process and
audio, which are per-client. It does **not** hold for world-space geometry, which every client
renders identically — a fog volume is not asymmetric no matter where you stand.

**The night edge (BT-9, 2026-08-27) is the first thing in this repo to publish a global shader
parameter, and it is a worked example of the rule above rather than an exception to it.**
`scripts/game/world/bubbletest/NightAidDriver.cs` writes one `[shader_globals]` float,
`bt_darkness` (0 at noon → 1 at deep night), which `resources/shaders/night_edge.gdshader` reads
to fade an emissive rim in with the dark. Three things about it are worth copying and one is
worth not repeating:

- **It derives, it does not replicate.** `bt_darkness` is never sent over the wire. It is
  computed on every client from `OutdoorAtmosphere.Evaluate(phase, cyclesElapsed).NightFactor` —
  the already-replicated cause — so it lands in the "client-local, derived" column by
  construction and every peer necessarily agrees without a single packet. That is what the table
  above asks for, applied to a visual rather than to a particle LOD.
- **It republishes; it never re-derives.** The driver reads the *same curve the sky is drawn
  from* rather than reimplementing the phase breakpoints. A shader with its own copy of those
  breakpoints is a second darkness authority, and `.claude/rules/single-writer.md` exists
  because this repo has already paid for one of those. Nothing in the driver writes back into
  `OutdoorAtmosphere`, and `NightAmbientFloor` (THRILL-BIBLE §6.2, Talon's open fork) is neither
  touched nor read for a decision.
- **A global is the right shape when many meshes across many scene files need one number in one
  frame.** Four section scenes authored by four packets wear the night-edge materials; a global
  is one write per frame for all of them, and a geometry packet assigns a material and is done.
  Prefer it over a per-material uniform exactly when the alternative is N packets remembering to
  update N materials in step.
- **The trap, which is general and will recur:** a per-frame publisher silently overwrites
  anything set from the command line or the inspector on the very next frame, and the symptom is
  indistinguishable from the uniform being ignored. Any shader driven this way needs a documented
  override that wins over the global — `night_edge.gdshader` ships `darkness_override` for
  exactly this, and it is what the BT-9 captures pin the day and night states with.

## 5. Corrections to the research

Nineteen places the delivered code would not have worked, found by verifying against
`GodotSharp` 4.7.0's own XML docs and the shipped repo rather than by reading.

| # | Research says | Reality |
|---|---|---|
| 1 | Build `DaylightController` | `CycleDriver` exists and is strictly better — sim-tick advance, staleness guard, late-join path, `Synced` gate. Rejected in a seven-row table in `vfx-lighting` |
| 2 | `env.AmbientLightColor = warm.Lerp(cold, eased)` | **No-op in this repo.** Only effective when `AmbientLightSkyContribution < 1.0`; `DayNightSky` sets `AmbientLightSource = Sky` and `Playground.tscn` ships `ambient_light_source = 3`. Route through the sky gradient keys |
| 3 | `_sun.LightEnergy = Lerp(1.2f, 0.05f, eased)` | Drives under the shipped `MinAmbientEnergy = 0.30f` floor — **silently resolves the §6.2 fork** by picking the lower number |
| 4 | `EmissionShapeOffset = worldPos` | Documented **"in local space."** Dust emits in the wrong place |
| 5 | `AdjustmentSaturation = …` | Does nothing unless `AdjustmentEnabled` is true, which the sample omits |
| 6 | `WORLD_POSITION` in `fragment()` | **Fog-shader-only in 4.7.** Both shipped repo shaders use a `MODEL_MATRIX` varying — that is the house form |
| 7 | `COLOR.r` as a foliage bend weight | No such vertex-colour channel exists anywhere in the repo |
| 8 | Client-side `ApplyImpulse` for the ghost nudge | `PropManager.StreamLoose` streams server-authored loose-prop transforms on channel 4 — a local impulse snaps back |
| 9 | `system.Amount = (int)(Amount * lodScale)` per frame | The `Amount` setter **restarts the system**, and the expression compounds geometrically to the floor within ~a second. Two independent bugs |
| 10 | `AmountRatio` as the LOD dial | Doesn't restart, but documented as giving **no performance benefit**. Real LOD here is binary and structural |
| 11 | `INSTANCE_CUSTOM` for per-instance seed | Requires `UseCustomData = true` set **while `InstanceCount` is 0**. Set after, every instance animates in lockstep |
| 12 | `Restart()` in the pool's `Rent` | `Restart()` + `OneShot` has a documented window where it **may silently fail to restart** |
| 13 | `[Rpc(CallLocal = false)]` | Not this repo's form — see §4 |
| 14 | Per-client `GD.Randf()` gating events | Desync; breaks §4.1 simultaneity. Server rolls (three separate samples) |
| 15 | `EmissionEnergyMultiplier = 0.15f` for a glow anomaly | `Playground.tscn` sets `glow_hdr_threshold = 1.0`; peak 0.105 produces **no bloom at all** |
| 16 | `Material.Duplicate()` per instance (three samples) | Costs real per-draw pipeline/uniform state-change overhead Forward+ avoids when instances share a material reference (it does not merge draw calls — each instance is still its own draw), and violates `ART-BIBLE.md` §4.2's shared-material contract |
| 17 | `GeometryInstance3D.GetActiveMaterial` / `SetSurfaceOverrideMaterial` | Both are on **`MeshInstance3D`**, not `GeometryInstance3D` |
| 18 | Screen-space UV warp for spreading distortion | **Rejected outright** — degrades the ground reference `ART-BIBLE.md` §3 declares non-negotiable, the same reasoning §7 already used to reject DoF and motion blur |
| 19 | `PlayUnknownOriginSound` spawning a node per sound | Sets `GlobalPosition` before tree entry; both anti-patterns the repo already documents. Use `SfxLab.PlayStream3D` |

**The pattern worth noticing:** none of these are the research being careless. They are what
happens when good general guidance meets a specific engine version and a specific codebase.
That gap is exactly what the `vfx-*` skills exist to hold — and it is why every skill marks its
unverified claims `unverified against Godot 4.7` inline rather than asserting uniformly.

## 6. Constraints the whole family inherits

- **Forward+, GTX 970 floor.** Confirmed from `project.godot`. Volumetric fog and screen-space
  warp are the two likeliest budget breakers. Never propose integrated graphics.
- **`ART-BIBLE.md` §4.3: no albedo/normal/roughness textures anywhere.** This is not a
  preference; it reshapes what corruption can even mean. Under flat-colour materials a radial
  falloff produces a mathematically perfect soft circle that reads as a failed decal — **shape
  must come from geometry or a procedural, never from the falloff.**
- **`ART-BIBLE.md` §4.2: one material per unique colour, shared.** Which forces a real finding:
  **severity in Sail is a discrete palette step, not a continuous lerp.** Continuous severity
  and material sharing are mutually exclusive; pick sharing.
- **`DayNightSky.Apply` writes `AmbientLightEnergy` and both `LightEnergy` values every
  frame.** Any other system writing those is a last-writer-wins race. Never write a property
  `DayNightSky` owns.
- **Headless tests cannot catch this work.** `tests/Run-*.ps1` will not see a visual or
  frame-time regression. A HEADED run is required — the same lesson the sideways-island export
  bug taught.

## 7. Validating the whole stack

The acceptance test is the dread research's Stage 1 benchmark, not a per-skill checklist:

> Playtesters report not wanting to be out there as it darkens, verbally urge each other to
> leave, and generate at least one spontaneous group-panic moment per session — **without any
> visible creature.**

- Log every gated event with a timestamp. Correlate against proximity-voice activity: two or
  more players' mics spiking in the same second is the operational definition of §4.1's
  simultaneity condition, and the repo already has both the voice pipeline and a live Firebase
  telemetry path to do it with.
- After each session, ask players independently to name their most memorable moment. If two
  name the *same* one, read the event log for what fired together and register the combination
  in `THRILL-BIBLE.md` §11 — that is how the signature gets observed rather than declared.
- **If dread does not emerge from these eight alone, do not add a creature to compensate.** The
  research is explicit and §10's decay states are the diagnostic: check whether devices are
  `burned`, whether escalation is flat, or whether the sync ratio has collapsed into
  always-reinforcing.

## 8. What comes after

Proximity voice consequences, forced-separation mechanics, and a threat entity are all
later-stage per the dread research, layered on this foundation rather than concurrent with it.
This family is scoped to Stage 1 — proving dread works from environment and clock alone.

---

*Scope note: written 2026-07-25 against a repo where none of the eight skills has been executed
once. `CycleDriver` and `DayNightSky` ship; `scripts/voice/` ships; everything else these
skills describe is unbuilt, and three shared dependencies (§3) do not exist at all. **Current
bite: none.** First real test: building `vfx-escalation`'s `SessionProgress` seam, which forces
the derivation decision that §3.1 and `THRILL-BIBLE.md` §13 both defer. Passing looks like the
eight skills producing one coherent atmosphere rather than eight independently-tuned effects
that each looked right alone — and the first honest answer to whether dread emerges from
environment and clock without a creature.*
