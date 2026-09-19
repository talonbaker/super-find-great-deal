---
name: vfx-lighting
description: Use when a directed TIDE feeling needs to become Godot atmosphere — the day/night clock made felt, fog denying a sightline, colour temperature as threat, light shafts withdrawn, an authored lighting anomaly. Turns a /direct call into WorldEnvironment, DirectionalLight3D and FogVolume work against the shipped CycleDriver. Never originates a feeling, never authors a second clock, never resolves THRILL-BIBLE §6.2.
---

# vfx-lighting

## Overview

The **lighting and atmospheric-density executor** for TIDE. It answers *how does this feeling
get rendered on a GTX 970 in Godot 4.7* and nothing above that line.

**Source of record:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §2
(Atmospheric Density & Lighting Effects), read together with that file's closing *Known
collisions with the shipped repo* — the collisions are binding and override §2 wherever they
disagree. §2's code is unverified research prose, not a patch.

**Serves:** `docs/THRILL-BIBLE.md` §2.2 (the clock is the certainty), §6.1 (prospect and
refuge), §6.2 (the night reversal, **and its live conflict**), §6.6 (colour temperature as a
threat channel).

**Touches, does not own:** `ART-BIBLE.md` owns the palette. This skill claims only that the
*direction* of a shift is meaningful (§6.6) and asks the Art Bible for the hues.
`LEVEL-BIBLE.md` §8 owns whether a cue is perceivable at all; if a fog wall makes a hazard
imperceptible that is an §8 defect, not a tuning problem.

## Directed, not decided

**This skill executes a direction. It never originates one.**

- `/direct` and `THRILL-BIBLE.md` decide **which** feeling, **why there**, and — via the §10
  device register — **whether the device is already spent**. "Night reversal (dark re-signs
  the map)" and "Day/night cycle as clock" are both register rows with decay states. A
  lighting change that re-uses a `spent-here` device is refused upstream, not here.
- This skill decides **how**, in Godot: which node, which property, which curve, what it
  costs per frame, and what breaks in multiplayer.

**If you are invoked with no direction behind you, say so and stop.** The correct response to
"make the forest scarier at night" with nothing else attached is: *this needs `/direct` first
— name the feeling, the altitude and the beat, then come back.* Building atmosphere against an
unstated feeling is how a level ends up with fog because fog is what horror games have.

Two things this skill is **structurally unable to resolve** and must carry visibly instead:

- **`THRILL-BIBLE.md` §9's axis is decided (2026-07-26): horror, dread-forward, atmosphere the
  primary instrument, with gross-comic absurdity as the register the dread spends itself in —
  dissonance (a sincere-dread environment undercut by an absurd payoff) is house style now, not
  a foreclosed option. Only the dread/absurd **ratio** is open (ripeness trigger = the first
  playtest where anyone is actually frightened). Direct dread-forward now; flag anything that
  would spend the ratio toward comedy before that trigger fires.
- **§6.2's night-floor conflict is a live fork.** See *The night floor* below. A lighting skill
  cannot resolve a design fork by choosing a number, and this one will not.

## When to Use

- A directed beat needs the sky, the sun, the ambient or the fog to carry it
- A level's atmosphere pass is starting and the clock needs to be *felt*, not just ticking
- Prospect is being denied somewhere on purpose — a false refuge, a thicket, a hollow
- A colour-temperature shift is being used to say something (§6.6)
- Volumetric light shafts are being added, or deliberately withdrawn
- An existing environment is being audited: does the clock read from inside the game?

**Not for:** deciding what the player should feel (`/direct`), the level's composition
(`/spec-level`), the pressure driver (`/spec-pressure`), the warning cue
(`/spec-urgency-cue` — a fairness contract, a different channel from a dread cue per §8.3),
the escalation conductor that decides *how hard* every system pushes (`/vfx-escalation`), the
palette itself (`ART-BIBLE.md`), or any particle/disturbance work (research §§1, 3–7 have no
skill yet).

## The clock already exists — read it, never rebuild it

**`scripts/game/world/CycleDriver.cs` is the phase clock.** It is server-authoritative,
advanced from the server's own `_PhysicsProcess` sim tick (never a wall-clock `Timer`),
broadcast at 2 Hz unreliable on `NetCodec.CycleChannel`, guarded latest-wins against reordered
samples, and delivered to a late-joining or reconnecting peer by a targeted reliable
`SendPhaseTo` on the same `Gameplay.OnPeerConnected` funnel `PropManager.SendDumpTo` uses.

**The research's `DaylightController` is a rejected proposal.** Not omitted — rejected, and
the reason matters because a future agent will find it in §2 and be tempted:

| `DaylightController` (research §2 pattern 1) | Why it is refused |
|---|---|
| ticks `DayProgress` in `_Process` | wall-clock/render-rate advance drifts from physics; `CycleDriver` ticks the sim step, so anything later derived on the physics cadence (sea height) stays in step |
| `Rpc(...)` every frame | `CycleDriver` broadcasts at 2 Hz and each client dead-reckons between samples; per-frame broadcast is ~120× the bandwidth for no visible gain |
| `[Rpc(CallLocal = false)]` | not this repo's form. Every RPC here is `[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = ..., TransferChannel = ...)]` — see `CycleDriver`, `PropManager`, `VoiceManager`, `SandboxAvatar` |
| no sequence number | unreliable delivery reorders; a stale sample would clobber a newer one |
| no late-join path | a peer joining between broadcasts renders the wrong sky until the next tick |
| no `Synced` gate | **the actual bug** — see below |
| a second clock at all | `BUILD-SPEC` §3: "do not build three independent timers." `DayNightSky` already owns zero timers and reads `CycleDriver.Instance.Phase` |

**Read `CycleDriver.Instance.Phase`. Author nothing new.**

### Never render anything derived from `Phase` while `Synced` is false

`Phase` and `CyclesElapsed` sit at their zero-initialised defaults until the first
authoritative update lands. Treating that default as "the server's phase is 0" means a client
renders **midday for one or more frames on join, then snaps to whatever time it actually is**
— a visible pop at exactly the moment the player is forming their first impression of the
place, and, worse, a moment where two players in the same session are looking at different
skies. `BUILD-SPEC` §5 names this the single most likely bug in the feature: *a client must
never render t=0 first.*

The shipped pattern to copy is `DayNightSky._Process`:

```csharp
if (CycleDriver.Instance is not { Synced: true } driver)
    return;                      // hold the last-applied state; do not advance a guess
Apply(Evaluate(driver.Phase));
```

Hold the last applied state. Do not lerp toward the truth on arrival either — `CycleDriver`
snap-corrects its own accumulator for the same reason, and a consumer that springs re-opens
the drift the driver just closed.

### `Phase` is cyclic. `DayProgress` is monotonic. They are different quantities

`Phase` wraps in `[0,1)` over a 120 s default period; `CyclesElapsed` counts the wraps. The
research assumes a one-way 0→1 ramp across a ~900 s session. **Anything in this skill that
scales with "how far into the session are we" needs a session-monotonic input, and this skill
does not derive one** — that is `/vfx-escalation`'s problem and its derivation is an open
decision. Bind sky, sun and ambient to `Phase` (they are genuinely cyclic — that is what a
day/night cycle is). Bind *escalation* to nothing until the monotonic quantity is decided.

### A repeating cycle is weaker dread than the research assumes — flag it, do not fix it

`DayNightSky.NightFactor` runs `{0, 0, 0.1, 0.3, 0.5, 0.6, 1, 1, 0.6, 0}`: night peaks and
then returns to day. `THRILL-BIBLE.md` §2.2 requires the clock be certain, monotonic and
**inevitable**; a guaranteed reprieve every 120 s is certainty with a built-in escape hatch.
That is a genuine design finding for `/direct` and the §13 fork table (which already carries
"one clock or two"), **not** a licence for this skill to quietly make the cycle one-way.

## Core patterns

### 1. Extend `DayNightSky`, do not add a second environment writer

`DayNightSky` has two attach modes. In **attach** mode (`PlaygroundWorld`'s call site) it
reuses the level's authored `WorldEnvironment` and `ProceduralSkyMaterial` in place and its
`Apply` touches *only* sky top/horizon/ground colour, `AmbientLightEnergy`, and the sun/moon
rotation/colour/energy. Every other knob — glow, fog, SSAO, tonemap, colour adjustment — is
left exactly as authored, on purpose, because Talon likes Playground's tuning.

Two writers on the same `Environment` on the same frame is a race with no owner. If a new
property needs to move with phase, **add it to `DayNightSky`'s `LightState` record and its
keyframe arrays** — the record is already the pure, scene-free half tested by
`CycleSelfTest.cs` (`--cycle-selftest`, `tests/Run-CycleTest.ps1`), so a new channel gets a
headless wrap-seam test for free. Every keyframe array's last entry must equal its first, or
the 1.0/0.0 seam pops once per cycle.

### 2. Ambient colour is inert in this repo's setup — a verified trap

The research writes `env.AmbientLightColor = warm.Lerp(cold, eased)`. **In TIDE that line does
nothing.** `Environment.AmbientLightColor` is documented as effective only when
`AmbientLightSkyContribution` is below 1.0; `DayNightSky` sets
`AmbientLightSource = AmbientSource.Sky` and Playground's `Environment` ships
`ambient_light_source = 3` (Sky) with sky contribution at its default. Ambient colour comes
from the sky.

So the ambient half of a §6.6 colour-temperature shift is driven by **`SkyTopKeys` /
`SkyHorizonKeys`**, not by `AmbientLightColor`. `AmbientLightEnergy` is the only ambient
scalar `DayNightSky` moves, and it is the one the §6.2 conflict is about.

### 3. Depth fog as the time channel

`Environment.FogEnabled` / `FogDensity` / `FogLightColor` are verified and already authored in
Playground (`fog_enabled = true`, `fog_density = 0.004`, a warm `fog_light_color`, plus
`fog_sun_scatter`, `fog_aerial_perspective`, `fog_sky_affect`). Depth fog is cheap — it is a
per-pixel blend in the existing pass, not a second pass.

Driving `FogDensity` and `FogLightColor` from phase is the lowest-cost way to make the clock
*felt*: distance closes in as the light goes. Drive them as new `LightState` channels
(pattern 1), not from a separate node. If a level wants fog that behaves differently by
altitude, `FogHeight` / `FogHeightDensity` exist and are also verified.

**The research's `Mathf.Lerp(0.0005f, 0.02f, eased * eased)` is a research value against a
scene that does not exist.** Playground's authored 0.004 is the only density this repo has
ever looked at. Start from what is authored and move it, rather than importing a range.

### 4. Local fog pockets — prospect denial where the level says so

`FogVolume` (`Material`, `Shape`, `Size`) with a `FogMaterial` (`Albedo`, `Density`,
`EdgeFade`, `HeightFalloff`, `Emission`, `DensityTexture`) — all verified in Godot 4.7. Shapes
come from `RenderingServer.FogVolumeShape` (`Box`, `Cone`, `Cylinder`, `Ellipsoid`, `World`).

**Two hard facts before anyone places one:**

1. **A `FogVolume` is invisible unless `Environment.VolumetricFogEnabled` is true.** It is not
   a standalone node; it adds to (or, with a negative `Density`, subtracts from) the global
   volumetric fog buffer. For local-only fog with no global haze, enable volumetric fog and set
   `VolumetricFogDensity = 0.0`.
2. **Volumetric fog is Forward+ only.** TIDE is Forward+ (`project.godot`:
   `config/features=PackedStringArray("4.7", "C#", "Forward Plus")`) so this is fine — and it
   is another reason the Forward+ floor is not negotiable. Never propose a fallback that
   assumes Mobile or Compatibility, and never propose integrated graphics as a target.

Place these where `LEVEL-BIBLE.md` marks a false refuge, so the visual language and the
mechanical trap agree (§6.1). Do not place them where they merely look nice; that is atmosphere
with no direction behind it, and this skill's own first section forbids it.

### 5. Light shafts as prospect, and their withdrawal

`Environment.VolumetricFogDensity` is verified, as are `VolumetricFogAlbedo`,
`VolumetricFogEmission`, `VolumetricFogLength`, `VolumetricFogAnisotropy`,
`VolumetricFogAmbientInject`, `VolumetricFogSkyAffect` and the temporal-reprojection pair.
`Light3D.LightVolumetricFogEnergy` controls how much a given light contributes.

Godot's own documentation notes that using volumetric fog as a *lighting* solution wants
`VolumetricFogDensity` at its lowest non-zero value with light fog-energy pushed very high —
the opposite dial from using it as haze. Shafts fading out as night falls is a legible, felt
loss of prospect (§6.1) and it costs nothing extra to animate a value already being paid for.

**This is the likeliest budget breaker in the whole skill.** It is a froxel buffer evaluated
every frame, on top of a scene whose measured bottleneck is CPU/submission and draw calls, on
a GTX 970 floor budgeted with zero assumed headroom. Volumetric fog is a *GPU* cost added to a
*CPU-bound* scene, which means it may well fit — but "may well" is a hypothesis, not a budget.
Profile before and after, on the floor if possible, with the frame-time monitor, and record
the delta. Do not add a second effect on the same hypothesis.

### 6. Colour temperature is a channel, not a filter

`AdjustmentEnabled` / `AdjustmentSaturation` / `AdjustmentBrightness` / `AdjustmentContrast`
are verified, and Playground already authors all three (`adjustment_saturation = 1.12`).

**The house precedent is already better than the research's.** `DayNightSky`'s golden hour is
not a single warmth scalar blended toward orange — that was its first-pass shape and it reads
as flat orange, which `BUILD-SPEC` §6 explicitly rejects. It is a four-key hue sweep (orange →
deep red → purple → blue-violet) authored as keyframes. Any new colour work matches that bar:
author the arc, do not lerp between two endpoints and call it a shift.

Grading stays just short of unnatural for most of the curve. Oversaturating the cold shift
early collapses ambiguity (§2.1) into "obviously a scare is coming" too soon — the game is
decided-horror per §9, but announcing the grade ahead of the beat still spends the ambiguity
the beat needed.

### 7. A lighting anomaly is a server event, not a local roll

The research's `AnomalousFlicker` rolls `GD.Randf()` in `_Process` on every client. **In
multiplayer that produces a different flicker on every machine**, which destroys §4.1's
simultaneity condition — the thing that makes a spike contagious rather than individual. Two
players describing different flickers is not asymmetry, it is a desync.

The server decides *when*; each client renders it identically from the replicated trigger. Use
the repo's attribute form, verified against `CycleDriver`/`PropManager`/`VoiceManager`:

```csharp
[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
private void FireAnomaly(Vector3 position, float durationSec) { /* … */ }
```

Reliable, because a dropped one-shot beat is a beat that never happened for that player —
unlike the phase stream, where the next sample supersedes the loss 0.5 s later.

A **hard cut** rather than a smooth dim is the deliberate part: a smooth flicker reads as a bad
bulb, which is a mundane explanation, and a mundane explanation is ambiguity spent (§2.1).

Before building one, check §10 — a lighting anomaly is a device and it decays. And note that
TIDE currently has almost no point lights to interrupt; this pattern is largely waiting on
content that does not exist.

## The night floor — a conflict this skill carries and cannot resolve

`DayNightSky` holds `MinAmbientEnergy = 0.30f` and `MaxMoonEnergy = 0.6f` — the two halves of
decision **§6d#25**, "navigability": night never goes fully dark so the return route stays
readable.

`THRILL-BIBLE.md` §6.2 calls the night reversal the strongest environmental device available
and says TIDE currently declaws it. Enclosed shaded space should invert its emotional valence
after dark; that requires the dark to **cost sight**. Navigability requires the dark be
**survivable by sight**. Both readings are legitimate: a dark you cannot navigate is
frustration; a dark that costs nothing is set dressing.

**The research drives energy well under the shipped floor.** `_sun.LightEnergy =
Mathf.Lerp(1.2f, 0.05f, eased)` bottoms out at 0.05, against a shipped night that holds
ambient at 0.30 and moon at up to 0.6. Transcribing that line does not implement a lighting
effect — it silently resolves a design fork by picking the side with the lower number.

**Therefore:**

- The floor is never moved **silently**. `THRILL-BIBLE.md` marks this fork **Ripening**: §9's
  decided dread-forward axis weights it toward the reversal, but a dark nobody can navigate is
  still frustration, not dread, so the fork stays Talon's to call.
- The sanctioned exception: the night-dome packet ships a **stepped darkness dial** (level 1 =
  the shipped §6d#25 floor, levels 2–3 progressively darker) *because it exists to let Talon
  choose experientially* — standing in each level after dusk is the resolution path, not a
  number picked in this skill. Wiring that dial is in-bounds; picking which level ships by
  default is not.
- Outside that sanctioned packet, any request that requires the dark to cost sight gets built
  up to the floor and **stops there**, with the finding stated: *this direction wants the §6.2
  reversal and the shipped floor blocks it; the fork is Talon's.*
- The fork's ripeness trigger: a playtest report treating the dark as a threat rather than a
  palette — which is exactly what the stepped-dial packet is built to produce. Do not surface
  the fork as a decision before that report lands (`DESIGN-BIBLE.md`, 2026-07-20).
- Everything in §6.2 that does **not** need a darker dark is fair game today: sightline
  denial by fog, colour temperature, the *shape* of shaded space, where refuge is offered.
  Prospect can be denied without the ambient floor moving at all, and that is the honest
  work available right now.

## Tuning guide — no numbers as law

Same discipline as every other skill in this repo. Thresholds, densities, durations and
energies are playtest calls. Present a starting point with its reasoning; never a gate.

- **The escalation curve should not be linear.** Slow drift early, accelerating late — dread
  scales with proximity to a certain deadline, so the last stretch should feel like it is
  collapsing rather than ticking. `Curve.Sample` is verified if a `Curve` resource is wanted;
  `DayNightSky`'s piecewise-linear keyframe arrays are the existing house mechanism and are
  cheaper to reason about. **Caveat: on a *cyclic* clock this shaping applies within a cycle,
  and a curve that "collapses" toward phase 1.0 then resets is exactly the reprieve §2.2
  objects to.** The shaping question and the monotonicity question are the same question.
- **Never let fog occlude a sightline the player needs for fair play.** Dread comes from
  denied *comfort*, not denied *information about immediate danger* (§7.1: avoidable but
  barely). If a hazard silhouette stops reading in heavy fog, that is a `LEVEL-BIBLE.md` §8
  legibility defect and it outranks the atmosphere.
- **Do not tune the urgency cue with this skill.** §8.3: a warning that a state is changing is
  a fairness contract and stays honest; a hint that something is about to happen is a dread
  device and must sometimes lie. Different cues, different channels, and the fairness one
  belongs to `/spec-urgency-cue`.
- **Sync is tighter than it looks.** `CycleDriver`'s 2 Hz broadcast leaves a ≤0.5 s
  extrapolation gap, ~0.4 % of a 120 s period. If two players' skies visibly disagree, the bug
  is a consumer deriving from local time, not the broadcast rate.
- **Start from what is authored.** Playground's fog, glow, SSAO, tonemap and adjustment values
  are a tuned baseline Talon likes. A research range imported wholesale discards that.

## Integration points

- **`CycleDriver.Instance.Phase`** — the single input. `Synced`-gated, always.
- **`DayNightSky.LightState` / `Evaluate`** — the seam for any new phase-driven channel, and
  the reason a new channel is headlessly testable via `CycleSelfTest` / `Run-CycleTest.ps1`.
- **`PlaygroundWorld`** — the attach-mode call site; a level with authored atmosphere sets
  `ExistingSun` / `ExistingWorldEnvironment` *before* the node enters the tree.
- **`/vfx-escalation`** — owns the session-monotonic quantity and the conductor that decides
  how hard this skill should be pushing. This skill exposes phase-derived values; it does not
  decide intensity over a session.
- **`/direct`** — upstream. Every atmosphere change that lands should show up in the §10
  register write-back, or §8.1/§8.3 become unenforceable across sessions.
- **`ART-BIBLE.md`** — the palette. Ask; do not invent hues.
- **Telemetry** (`scripts/telemetry/Telemetry.cs`, live Firebase path) — atmosphere events can
  be logged and correlated with proximity-voice activity. That correlation is buildable on what
  ships, not hypothetical.

## Precedent

- **In-repo:** `DayNightSky`'s four-key golden-hour sweep is the standing proof that a
  two-endpoint colour lerp reads as flat and a keyed arc does not. Copy the shape.
- **In-repo:** `DayNightSky`'s shadow decision — zero real-time shadow casters, blob shadows
  instead, "raking dawn light" delivered as a low sun elevation plus a dusty-gold read rather
  than literal cast geometry. That is the house answer to "we need shadows for mood": find the
  cue that costs a rotation instead of a pass.
- **The Forest / Sons of the Forest:** night shorter but denser; the prospect-refuge reversal
  explicit in their day/night tuning.
- **Subnautica:** fog and light attenuation with depth producing "no reference points"
  disorientation with no monster present — directly analogous to canopy-driven light loss.

## Troubleshooting

- **"The sky pops on join."** A consumer is deriving from `Phase` before `Synced`. Copy
  `DayNightSky._Process`'s guard exactly.
- **"Two players see different times of day."** Something is running its own timer. Trace every
  intensity value back to `CycleDriver`; there is exactly one legitimate source.
- **"There's a visible jump once per cycle."** A keyframe array whose last entry does not equal
  its first. `CycleSelfTest` checks phase 0.9999 against 0.0001 for every channel it knows
  about — a new channel not added to that test is a seam waiting to pop.
- **"Ambient colour changes do nothing."** Expected. See pattern 2 — with
  `AmbientLightSource = Sky` and default sky contribution, `AmbientLightColor` is inert. Move
  the sky keys.
- **"The `FogVolume` is invisible."** `Environment.VolumetricFogEnabled` is false. It is not
  optional.
- **"Fog feels like a slider, not a threat."** Usually a linear curve — but check first whether
  the level actually has a clock the player believes in. Fog on no clock is atmosphere, not
  dread, and §2.2 says to name it in exactly those words rather than adding more fog.
- **"Frame time regressed after the atmosphere pass."** Volumetric fog first. Then count new
  draw calls — this scene is CPU/submission-bound and a 6-player Playground already sits at the
  ceiling with zero assumed headroom.

## Caveats

- **Never author a clock.** One driver, already shipped. `BUILD-SPEC` §3.
- **Never render from an unsynced phase.** Not a style rule; the named most-likely bug.
- **Never resolve §6.2 or §9's ratio.** A lighting skill quietly moving `MinAmbientEnergy`
  outside the sanctioned stepped-dial packet, or a grade that spends the dread/absurd ratio
  toward comedy, has silently made a design decision that belongs to Talon.
- **No numbers as law.** Every density, energy and threshold here is a starting point with
  reasoning attached, and playtest outranks all of them.
- **Verified vs unverified.** The following are **verified against Godot 4.7** (GodotSharp
  4.7.0, `MpFoundation.csproj` → `Godot.NET.Sdk/4.7.0`), plus repo usage where noted:
  `Environment.FogEnabled/FogDensity/FogLightColor/FogMode/FogSunScatter/FogAerialPerspective/FogSkyAffect/FogHeight/FogHeightDensity/FogDepthBegin/FogDepthEnd/FogDepthCurve/FogLightEnergy`;
  `Environment.VolumetricFogEnabled/VolumetricFogDensity/VolumetricFogAlbedo/VolumetricFogEmission/VolumetricFogEmissionEnergy/VolumetricFogLength/VolumetricFogAnisotropy/VolumetricFogAmbientInject/VolumetricFogGIInject/VolumetricFogSkyAffect/VolumetricFogDetailSpread/VolumetricFogTemporalReprojectionEnabled/VolumetricFogTemporalReprojectionAmount`;
  `Environment.AdjustmentEnabled/AdjustmentSaturation/AdjustmentBrightness/AdjustmentContrast/AdjustmentColorCorrection`;
  `Environment.AmbientLightSource/AmbientLightColor/AmbientLightEnergy/AmbientLightSkyContribution`
  and the `AmbientSource` enum (`Bg`, `Disabled`, `Color`, `Sky`);
  `FogVolume.Material/Shape/Size`; `FogMaterial.Albedo/Density/DensityTexture/EdgeFade/Emission/HeightFalloff`;
  `RenderingServer.FogVolumeShape.{Box,Cone,Cylinder,Ellipsoid,World}`;
  `Light3D.LightEnergy/LightColor/LightIndirectEnergy/LightVolumetricFogEnergy`;
  `Curve.Sample`/`Curve.SampleBaked`; `Gradient.Sample`; the `[Rpc(MultiplayerApi.RpcMode…)]`
  attribute form. **Marked `unverified against Godot 4.7`:** the *interaction* between
  volumetric fog and `ProceduralSkyMaterial`'s auto-rendered sun disc (nobody has run it here);
  the actual frame cost of volumetric fog on a GTX 970 in this scene (never measured); whether
  `FogVolume` count scales acceptably past a handful in a 2–6 player session. Any research
  snippet from §2 not in the verified list above is unverified — say so inline rather than
  transcribing it as checked.
- **Screen-space UV warp and per-camera post-processing** belong to research §§4–5 and have no
  skill. Do not smuggle them in here.

*Scope note: written 2026-07-25 against a repo that ships `CycleDriver` (server-authoritative
120 s phase clock, sync-gated, late-join delivery) and `DayNightSky` (sun/moon/sky/ambient off
that phase, ten breakpoints, four-key golden hour, night ambient floor at 0.30), one authored
`Environment` in `Playground.tscn` with depth fog at 0.004, no volumetric fog anywhere, no
`FogVolume` anywhere, no ambient bed, and no directed beat. Current bite: partial — patterns 1,
2, 3, 6 act on code that exists today and pattern 2's inert-ambient finding is already true of
the shipped setup; patterns 4, 5 and 7 build nothing that exists and pattern 7 has almost no
lights to interrupt. First real test: the jungle island's first atmosphere pass, directed by
`/direct` before this skill is invoked, profiled on the GTX 970 floor. Passing looks like a
player answering "how long have we got" by glancing at the sky and getting it right, two
players agreeing on what they saw, and the frame-time monitor showing what the fog cost.*
