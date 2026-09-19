---
name: vfx-corruption
description: Use when a directed feeling calls for persistent physical wrongness left in a Sail level — a dead patch of ground, scarring where something passed, a surface that has gone wrong and stayed wrong, one detail that glows when nothing else does.
---

# vfx-corruption

## Overview

The **persistent** half of atmospheric wrongness: marks that stay in the world and get
discovered, as opposed to the transient "something just moved" cues. Source is
`docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §5, but that section was
written for a game with albedo textures to tint and roughness maps to dull. **Sail has
neither.** Most of this file is the rewrite that fact forces — see *The Art Bible collision*,
which is the reason this skill exists as a separate document instead of a link to §5.

**Scope boundary.** `LEVEL-BIBLE.md` §8.1 owns whether the level's state is *perceivable*.
`THRILL-BIBLE.md` §6.0 owns whether perceiving it *feels* like anything. This skill owns
neither — it owns the third thing: what the pixels do once both of those have been decided by
somebody else.

## Directed, not decided

**`/direct` and `docs/THRILL-BIBLE.md` decide which feeling, why here, and whether the device
is already spent (§10 device register). This skill decides how, in Godot. It executes a
direction; it never originates one.**

| The director owns | This skill owns |
|---|---|
| that this zone should accumulate wrongness at all | that the wrongness is geometry, not a gradient |
| where the patches sit in the session's rhythm (§3.5) | how many materials that costs |
| whether "accumulating wrongnesses" is `spent-here` (§10) | whether the shape survives a flat-colour renderer |
| how wrong a thing is allowed to look | which channel carries "wrong" at all |

Sections served:

- **§6.4 accumulating wrongnesses** — the primary one. Small dissonances that individually
  explain nothing. This skill's whole output is the physical form of that section, and §6.4's
  own claim that it is *"the cheapest dread in the file"* only holds if the implementation
  stays cheap; a corruption pass that costs a material per patch has made the cheapest device
  expensive and should be refused on those grounds alone.
- **§6.5 traces, not exposition** — introduce the thing without showing it. A patch is a
  trace. A patch with a readable cause is a sighting.
- **§6.6 colour temperature as a threat channel** — §6.6 explicitly cedes the palette to
  `ART-BIBLE.md` and claims only that the *direction* of a shift is meaningful. This skill
  inherits that split exactly: it will tell you a corrupted surface must move away from its
  zone's authored temperature, and it will not tell you to what.

Governed by **§8.1 over-exposure**: every additional patch spends ambiguity. A player who has
categorised the corruption is looking at a mechanic. §8.1 is the reason the tuning guide below
argues for *fewer, more varied* rather than *more, consistent*, and it is the reason the
director — not this skill — decides the count.

**Three things this skill must not resolve.** Route them, never absorb them:

1. **`THRILL-BIBLE.md` §9's axis is decided (2026-07-26 — horror, dread-forward, atmosphere the
   primary instrument); only the dread/absurd ratio stays open.** This bears on corruption more
   directly than on any other vfx surface: how wrong a patch is allowed to look *is* a ratio
   call. A patch that reads as sick serves sincere dread; one that sits unremarked next to a
   pastel avatar spends toward the absurd register the dread cashes out in (loop-v1 §12.1).
   §9's ripeness trigger is the first playtest where anyone is actually frightened. Until then,
   build the mechanism and leave the amount as a knob — never bake the ratio into a constant.
2. **`THRILL-BIBLE.md` §6.2's night-reversal conflict.** `DayNightSky.MinAmbientEnergy` (0.30,
   decision §6d#25, navigability) is a shipped decision in live opposition to the night
   reversal. Corruption read at night depends on which side wins. No `vfx-*` skill resolves
   it; if a patch is only legible with the floor lowered, that is a finding for the fork, not
   a licence to lower it.
3. **`LEVEL-BIBLE.md` §8.4 "where have we been" is `[BLANK — Talon]`**, and one of its four
   candidate devices is *"signal decay — explored zones read visibly spent."* That is
   corruption wearing a legibility hat. **Do not let a corruption pass quietly become the
   answer to §8.4** — if a direction wants corruption to mark explored ground, that is §8.4
   being resolved by the back door, and it goes to Talon as a fork rather than into a shader.

## When to Use

- A `/direct` call has asked for accumulating wrongness in a zone and something has to be
  built
- A level's blockout needs the physical form of "evidence a thing was here" chosen and costed
- An existing corruption pass reads as decoration, as a bug, or as an obvious hazard zone, and
  the failure needs diagnosing (see *Troubleshooting*)
- Someone is about to transcribe research §5 verbatim and needs to know why three of its four
  patterns do not survive contact with `ART-BIBLE.md`

**Not for:**

- **Deciding that a place should feel wrong** — `/direct`. This skill has no opinion on where
  dread belongs and will produce a technically correct patch in a place that earns nothing.
- **Transient "something is here" cues** — foliage that moves without wind, ghost ripples,
  prop nudges. That is research §1's territory and a different skill; corruption is what
  *stays*.
- **Rhythmic modulation** — `vfx-pulse`. A patch that breathes is two devices, and the pulse
  one has its own amplitude rule.
- **Session-monotonic severity** — `vfx-escalation`, which owns the `SessionProgress`
  derivation and the tension budget. This skill takes severity as an input and will not derive
  it.
- **The urgency cue** — `/spec-urgency-cue`. `LEVEL-BIBLE.md` §8.2's tide line is a fairness
  contract that must stay honest; `THRILL-BIBLE.md` §8.3 says a dread hint must sometimes lie.
  **These are different channels and corruption is the lying one.** Never let a corruption
  visual carry an urgency signal, and never let it become the sole carrier of anything — the
  redundant-channel rule inherited via `LEVEL-BIBLE.md` §8.1 forbids it.
- **Creature capability.** `THRILL-BIBLE.md` §0: directing a feeling is never a licence to
  grant one. A patch implies something did this; it does not create the something.

## The Art Bible collision

**This is the section to read if you read only one.** `docs/ART-BIBLE.md` sets three
constraints that research §5 does not anticipate:

- **§4.3 — "No albedo/normal/roughness textures anywhere."** The gradient ramp is the whole
  look. Repo evidence: `resources/shaders/CheckerFloor.gdshader` and
  `DecorativeWater.gdshader` are the only two custom shaders and neither samples a texture;
  every material in `Playground.tscn` is a flat `StandardMaterial3D` or a shared
  `ShaderMaterial`.
- **§4.2 — one `StandardMaterial3D` per unique *colour*, shared across every instance.** The
  live pattern is `AvatarVisual.MatCache` (`scripts/game/sandbox/AvatarVisual.cs:462`), a
  `Dictionary<Color, StandardMaterial3D>`. "Never a fresh material per instance" is the
  literal wording.
- **§3 — the legibility rule.** The checker floor exists so motion has a ground reference and
  *"it is never traded away for visual polish."* Interactive things separate in **value**, not
  hue.

And one measured constraint from outside the Art Bible: `docs/superpowers/2026-07-23-low-end-performance-PROBLEM-BRIEF.md`
ranks the cost sources **overdraw first, draw calls second, triangles last**, and
`DayNightSky.cs`'s own class doc records that a 6-player Playground already exceeds the
codebase's draw-call ceiling with zero assumed headroom against the GTX 970 floor.

What that does to research §5's four patterns:

| Research §5 pattern | Verdict here |
|---|---|
| 1. Progressive colour/saturation decay in a radius | **Rewritten.** Survives the maths, fails the look — see below. |
| 2. Environmental scarring (tinted meshes) | **Survives, best fit.** Its code sample violates §4.2 and must be rebuilt on the `MatCache` pattern. |
| 3. Spreading distortion (screen-space UV warp) | **Rejected.** Not a perf caveat — a §3 violation. |
| 4. Bloom/glow anomaly on one element | **Survives, re-derived.** Its numbers are wrong under Sail's authored glow gate. |

**Why pattern 1 fails as written.** `ALBEDO = mix(ALBEDO, ALBEDO * sickly, t)` is arithmetic
that works fine on a flat colour. The problem is the *result*. In the research's game, texture
detail breaks up the falloff, so the tint reads as ground that has gone bad. On a flat-colour
surface, the same maths produces a mathematically perfect soft-edged circle of slightly
different flat colour — which the eye reads as a projected light, a decal that failed to load,
or a shader bug. **Under flat-colour materials the shape of a corrupted region cannot come
from the falloff; it has to come from geometry or from a procedural function inside the
shader.** This is the single most important adaptation in this file, and it is the mechanical
reason pattern 2 outranks pattern 1 in a way it does not in the research.

**Why pattern 3 is rejected outright.** Three reasons stack, and the third is the binding one:
the research itself flags per-pixel UV warp over a large screen area as expensive; the repo's
measured bottleneck is overdraw and draw calls on the low-end floor; and `ART-BIBLE.md` §7
already rejected DoF and motion blur with the reasoning *"it fights legibility (§3) and costs
frame time in a fast networked game."* A screen-space warp is the same class of effect and a
worse offender — it distorts the ground reference the checker floor exists to provide. Do not
propose it, do not propose a "cheap version" of it, and if a direction seems to need it, the
finding is that the direction needs a different device.

## Core patterns

### 1. Corruption is geometry first

The strongest, cheapest, most contract-compatible form of "this place is wrong" in Sail is
**placed meshes**: flattened undergrowth, a broken thing, disturbed ground, a shape that
should not be there. Silhouette is the channel Sail already reads well (`ART-BIBLE.md` §6,
"silhouette-first"), and it needs no shader, no texture, and no new material family.

The tint that connects them is where §4.2 bites. Research §5.2's sample does this:

```csharp
var mat = scar.GetActiveMaterial(0).Duplicate() as StandardMaterial3D;  // one material per instance
mat.AlbedoColor = mat.AlbedoColor.Lerp(corruptColor, severity * 0.5f);  // continuous severity
scar.SetSurfaceOverrideMaterial(0, mat);
```

Every line of that is wrong for this repo. `Duplicate()` per instance is the exact thing §4.2
forbids and it un-shares the material, which costs draw-call batching in a scene already over
its ceiling. The continuous `Lerp` is what *forces* the duplication — a continuous severity
and a shared material cache are mutually exclusive.

**So: quantise.** Corruption severity in Sail is a **discrete palette step, not a continuous
lerp.** Three tiers is a reasonable starting count. Key the cache on
`(baseColour, tier)` exactly as `AvatarVisual.MatCache` keys on colour, and a whole level's
scarring costs a handful of shared materials instead of one per prop.

```csharp
// The MatCache pattern (AvatarVisual.cs:462) extended by one axis. Shared, never per-instance.
private static readonly Dictionary<(Color Base, int Tier), StandardMaterial3D> ScarMats = new();

private static StandardMaterial3D ScarMat(Color baseColor, int tier)
{
    if (ScarMats.TryGetValue((baseColor, tier), out var cached)) return cached;
    var mat = new StandardMaterial3D
    {
        AlbedoColor = StepToward(baseColor, tier),  // palette step; direction from ART-BIBLE §2/§6.6
        Roughness = 0.9f,
    };
    ScarMats[(baseColor, tier)] = mat;
    return mat;
}
```

Apply via `MaterialOverride`, which is this repo's universal idiom (`AvatarVisual.cs:272`,
`GameWorld.cs:100`, `BlobShadow.cs:60`, `Playground.tscn`) — not via
`SetSurfaceOverrideMaterial`, which appears nowhere in the codebase. Both
`MeshInstance3D.GetActiveMaterial(int)` and `MeshInstance3D.SetSurfaceOverrideMaterial(int,
Material)` **do exist in Godot 4.7** (verified against the 4.7 class reference), and both are
on `MeshInstance3D`, **not** `GeometryInstance3D` as the research implies. Whether
`GetActiveMaterial` hands back the shared resource or a copy is *unverified against Godot 4.7*
— which is precisely why the pattern above never calls it. Build the material from a known
base colour; do not read one back off a mesh and mutate it, or you will mutate every instance
that shares it.

### 2. Surface tint: one shared shader, a patch array, and a shape that is not a circle

When the wrongness has to be on the ground rather than on it, the constraint is that a shader
**uniform belongs to a material, not to an instance** — so N patches cannot each set
`corruption_center` on a material shared by every floor mesh. Three ways out, in preference
order:

**(a) One material, an array of patches.** Pack the patches into a single uniform array on the
one shared material and loop in the fragment shader. Preserves §4.2 exactly, costs N distance
checks per fragment, and needs no per-instance machinery. This is the default.

```glsl
// Sketch, in the house idiom: world-space XZ via a varying, exactly as CheckerFloor.gdshader
// does — this repo's two shipped shaders both compute world position in vertex() and pass it
// down rather than relying on a fragment built-in. WORLD_POSITION as a fragment built-in is
// unverified against Godot 4.7; the varying is verified by two shipping shaders.
uniform vec4 patches[MAX_PATCHES];   // xyz = centre, w = radius (w <= 0 => slot unused)
uniform float patch_severity[MAX_PATCHES];
uniform vec3 corrupt_tint;           // direction only — value from ART-BIBLE, never hardcoded here

void fragment() {
    float t = 0.0;
    for (int i = 0; i < MAX_PATCHES; i++) {
        if (patches[i].w <= 0.0) continue;
        float d = distance(world_pos.xz, patches[i].xy);       // note .xy: centre packed as xz
        float f = clamp(1.0 - d / patches[i].w, 0.0, 1.0);
        // Break the circle. A smooth radial falloff on flat colour reads as a projected light.
        f *= step(BREAKUP_THRESHOLD, some_cheap_procedural(world_pos.xz));
        t = max(t, f * patch_severity[i]);
    }
    ALBEDO = mix(ALBEDO, corrupt_tint, t);
}
```

The `step()` line is not optional dressing. Without something that breaks the outline — a
cheap procedural, or better, letting placed geometry own the shape and the tint only fill
between — you have built a coloured circle. See *Troubleshooting*.

**(b) `GeometryInstance3D.SetInstanceShaderParameter`** exists in Godot 4.7 (verified) and is
the engine's sanctioned way to vary a uniform per instance on a *shared* material — a good fit
for §4.2 in principle. Two reservations: the matching `instance uniform` shader qualifier is
*unverified against Godot 4.7* and nothing in this repo uses either half; and the granularity
is per-mesh-instance, so a patch smaller than a floor slab still cannot be addressed and one
spanning two slabs needs both set. Reach for it when (a)'s array bound is the problem, and
verify the qualifier compiles before designing around it.

**(c) Duplicate the material per patch.** Rejected — §4.2, plus draw-call batching in a scene
already over budget.

**Vertex colour is the fourth option and may be the best one for imported geometry.**
`vertex_color_use_as_albedo` is verified on `BaseMaterial3D` and already used in-repo
(`JuiceFx.PuffMaterial`), and the research's own foliage shader uses `COLOR.r` as a per-vertex
weight. Baking a corruption mask into vertex colour in Blender costs no texture, no extra
material and no per-frame work, and it fits the island's glTF pipeline. Two dependencies to
check before committing: mesh tessellation bounds the smallest expressible patch, and
**whether `docs/BLENDER-EXPORT.md`'s glTF contract carries a vertex-colour layer through at
all is unverified** — confirm before an artist paints one.

**Decals are the obvious answer and are contract-forbidden.** Godot 4.7's `Decal` exists and
its members are `texture_albedo` / `texture_normal` / `texture_orm` / `texture_emission` — a
decal without a texture projects nothing. §4.3 says no albedo textures anywhere. Whether an
authored alpha mask counts as "a texture map" under a rule aimed at surface-detail maps on lit
geometry is **a fork for Talon, not a call for this skill**; raise it as one if a direction
keeps arriving at decals. `Decal` behaviour and cost on Forward+ against the 970 floor is
*unverified against Godot 4.7* either way.

### 3. The one detail that glows — and the glow gate it has to clear

Research §5.4 is right that a single unexplained glow inside an otherwise dark patch is a
stronger hook than a field of glowing things. Its numbers are wrong for Sail.

`Playground.tscn` authors `glow_enabled = true` with `glow_hdr_threshold = 1.0`, which is
`ART-BIBLE.md` §7's "emissive-only, thresholded" rule made concrete: **only emission that
pushes a surface over 1.0 linear blooms, and nothing else does.** The research's
`Emission = (0.55, 0.65, 0.7)` at `EmissionEnergyMultiplier = 0.15` peaks at 0.105 — under
this environment that is a faintly self-lit surface and produces no bloom whatsoever.

These are two different effects and you must pick one on purpose:

- **Sub-threshold self-lit** — visible only up close, in shadow, discoverable. Fits §6.5
  (traces) and §8.1 (does not announce itself) and is the safer default.
- **Above-threshold blooming** — visible across the zone, and therefore spending the zone's
  hero-accent budget. `ART-BIBLE.md` §2.3 caps a zone at two hero accents and §3 forbids
  putting one on a large surface a player must look past. **A blooming corruption detail is
  competing with the objective for the player's eye** — that is a composition decision, not a
  VFX one, and it goes back to whoever owns the zone.

`DecorativeWater.gdshader`'s header comment is the house precedent for doing this arithmetic
deliberately: it disables specular specifically because the material it replaced peaked at
1.2766 linear and *"would have thrown a blooming sun glint across the sea."* Do the same
multiplication before you author an emission value. `EmissionEnabled`, `Emission` and
`EmissionEnergyMultiplier` are all verified as real and in use (`Carryable.cs:123-125`).

### 4. Spreading distortion — rejected

Research §5.3. See *The Art Bible collision*. It is not a budget question; it degrades the
ground reference `ART-BIBLE.md` §3 declares non-negotiable, and §7 already rejected the two
nearest neighbours (DoF, motion blur) on the same reasoning. If a direction wants "it is
spreading and it is catching up," that is a pressure driver (`MECHANICS-BIBLE.md` §10,
`/spec-pressure`) with an anti-unwinnable guarantee, not a screen-space effect.

## Tuning guide

**Every number here is reasoning, not a gate.** Playtest calls, all of them.

- **Count.** Research §5 suggests 3–5 patches per zone. The reasoning that matters is not the
  number: patches must be discovered *individually over time* or §6.4's accumulation collapses
  into one impression, and every one spends §8.1's ambiguity budget. Fewer and more varied
  beats more and consistent. **The director sets the count** — it is a rhythm decision (§3.5),
  and this skill has no standing to pick it.
- **Severity tiers.** Three is a starting count because it is the smallest number that reads
  as a progression rather than a binary. It is also the material-cache multiplier, so the cost
  of being wrong is linear and visible. Start at three; if playtesters cannot tell two tiers
  apart, the fix is a bigger step between them, not a fourth tier.
- **Palette direction, never palette values.** Muted, plausible, low-contrast, and *away from
  the zone's authored temperature* (§6.6). Saturated toxic-green/purple collapses ambiguity
  into "obvious game hazard zone" and the research is right about that. **But the actual
  colours come from `ART-BIBLE.md` §2 and from `/direct`, not from here** — a corruption tint
  is a palette member and picking one in a shader constant is how the palette drifts. See
  also §3's colourblind-safe corollary: separate in **value**, not only hue.
- **Shape variation.** Rotate silhouette and step between instances so players cannot
  pattern-match one "corruption look" and file it as decoration (§8.1 again). Under flat
  colour this matters more than the research assumes, because you have fewer channels to vary.
- **Severity is an input.** Do not derive it here. See *Integration points*.

## Integration points

**`CycleDriver.Instance.Phase`, gated on `Synced`.** `scripts/game/world/CycleDriver.cs` is
the server-authoritative clock — sim-tick advance, 2 Hz unreliable broadcast on its own
channel, latest-wins staleness guard, targeted `SendPhaseTo` for late join and reconnect.
Read it; author nothing new. **Never derive anything visible from `Phase` while `Synced` is
false** — the fields sit at their zero default until the first authoritative update, and
treating that as "the server's phase is 0" is the bug `CycleDriver`'s own doc names as the
most likely one in the feature. `DayNightSky._Process` is the working example of the correct
consumer shape.

**`Phase` is cyclic and severity must not ride it.** `Phase` wraps in `[0,1)` over a 120 s
default period; `CyclesElapsed` counts the wraps. It is **not** the research's monotonic 0→1
`DayProgress` across a session. Research §5's *"severity should correlate with `DayProgress`"*
transcribed literally onto `Phase` gives you patches that get worse and then get better again
every two minutes, in lockstep with sunrise — which is not just wrong, it is a **reliable
telegraph** (`THRILL-BIBLE.md` §8.3): players decode "the ground looks bad, so it's about to
be dawn" and the device stops carrying dread and starts carrying the time.

**A session-monotonic quantity has to be derived, and `vfx-escalation` owns that derivation**
— its `SessionProgress(phase, cyclesElapsed, periodSec, targetSessionSec)` seam, whose source
is itself still an open decision (`CyclesElapsed` vs. a sibling driver vs. the tide). Take
severity from that skill or take it as an explicit input; do not solve it here, do not quietly
integrate `Phase` into an accumulator, and do not invent a second clock — `CycleDriver`'s
class doc says outright *"do not build three independent timers."*

**Static placement, replicated by construction.** Patches authored into the level are
identical on every client for free — no RPC, no authority question. The moment severity
becomes dynamic that stops being true, and the rule from research §9's authority model
applies: **replicate the cause, let each client compute the effect.** A patch's severity tier
is a cause. Its resulting material is an effect.

**Corruption anchors other devices.** Research §5 notes that a disturbance sourced from
*inside* a known corrupted patch reads as connected without any narrative confirmation. That
composition is free and worth taking — but the disturbance half belongs to a different skill
and the decision to connect them belongs to `/direct` (it is one device or two, and §10 tracks
them separately).

## Precedent

**In-repo, and these matter more than the external ones:**

- `scripts/game/sandbox/AvatarVisual.cs:462` — `MatCache`. The shared-material discipline that
  pattern 1 above is a direct extension of.
- `resources/shaders/CheckerFloor.gdshader` — the house shader idiom in 20 lines: world-space
  XZ via a varying, two flat colours, no texture, no shadow interaction. Any corruption shader
  should look like a sibling of this file.
- `resources/shaders/DecorativeWater.gdshader` — the precedent for reasoning about the glow
  gate in a comment before authoring a value, and for `specular_disabled` as a load-bearing
  choice rather than a style one.

**External, via the research:**

- **The Forest's effigies** — physical, discoverable, unexplained evidence objects. The direct
  model for pattern 1, and the reason geometry outranks tint here.
- **Slime Rancher's Tarr** — a "wrong" thing in an otherwise normal palette. The muted-over-
  saturated guidance descends from why that reads. Note that this is also the reference
  `THRILL-BIBLE.md` §9 names for *deliberate dissonance* — now the arguable house style
  (axis decided 2026-07-26), so the palette logic and the tonal conclusion both apply; only
  how often a beat leans on it (the ratio) stays open.

## Troubleshooting

- **"It reads as a coloured circle / a broken decal / a light."** The most likely failure in
  this project specifically, and it is the flat-colour failure mode from *The Art Bible
  collision*. The falloff is doing the shape work. Give the shape to geometry, or break the
  outline with a procedural in the shader. Increasing severity makes it worse, not better —
  a more saturated circle is still a circle.
- **"It reads as a bug, not a threat."** Research's diagnosis is a too-subtle or too-random
  palette; in Sail add a second cause — corruption with no *spatial logic* (something passed
  through here, something died here) reads as an authoring error regardless of palette,
  because flat-colour rendering gives the eye nothing else to interpret.
- **"Players stopped noticing after the second one."** §8.1 over-exposure. Thin them out and
  vary the silhouette. Do **not** raise severity: a louder version of a categorised thing is
  still categorised, and the register in `THRILL-BIBLE.md` §10 exists so this gets caught
  before Talon notices it.
- **"It looks like an obvious hazard zone."** Palette has gone saturated, or the glow crossed
  the 1.0 threshold and is now competing with the zone's hero accent. Check the emission
  arithmetic against `glow_hdr_threshold` before touching the colour.
- **"The frame time got worse."** Almost certainly materials, not fragments. Count unique
  materials before profiling shaders — a per-instance `Duplicate()` that crept in un-shares
  every scarred mesh and the repo is draw-call bound (`2026-07-23-low-end-performance-PROBLEM-BRIEF.md`
  §5), not fragment bound, at the 970 floor.
- **"It disappears at night."** Real, and it lands on an unresolved fork. Report it against
  `THRILL-BIBLE.md` §6.2 / `MinAmbientEnergy`; do not raise the ambient floor and do not
  brighten the patch to compensate — the second is how a subtle device becomes a loud one.

## Caveats

- **Verified vs. unverified, explicitly.** Verified against the Godot 4.7 class reference:
  `MeshInstance3D.GetActiveMaterial(int)`, `MeshInstance3D.SetSurfaceOverrideMaterial(int,
  Material)` (both on `MeshInstance3D`, not `GeometryInstance3D`),
  `GeometryInstance3D.SetInstanceShaderParameter`, `Decal` and its texture-typed members,
  `Environment.FogDensity`, `BaseMaterial3D.EmissionEnergyMultiplier`,
  `BaseMaterial3D.VertexColorUseAsAlbedo`. Verified by shipping repo usage: `MaterialOverride`
  with a shared `ShaderMaterial`, `EmissionEnergyMultiplier` (`Carryable.cs`), world-position
  varying in a spatial shader (both repo shaders). **Unverified against Godot 4.7:**
  `WORLD_POSITION` as a fragment built-in, the `instance uniform` shader qualifier,
  `GetActiveMaterial`'s shared-vs-copy return semantics, `Decal` cost and behaviour on
  Forward+, and per-frame `Environment` write cost. Verify before designing around any of
  those; do not transcribe research code as though it were checked.
- **No numbers as law.** Patch counts, tier counts, radii, severities, emission energies — all
  playtest calls. This file argues for shapes and orderings.
- **Perf floor is a GTX 970 on Forward+** (`project.godot`: `forward_plus`). Never propose
  integrated graphics as a target. The measured ranking is overdraw > draw calls > triangles.
- **Composes, never defines.** A patch that implies a creature does not grant that creature
  anything (`THRILL-BIBLE.md` §0). Route it to `/spec-entity`.
- **This skill cannot make a place matter.** Corruption in a zone the player has no reason to
  look at is decoration, and `ART-BIBLE.md` §1 pillar 2 cuts decoration for its own sake. That
  judgement belongs to `/direct` and this skill will not make it.

*Scope note: written 2026-07-25 against a repo with no corruption of any kind, no terrain
shader on the island (the pirate island ships as flat-colour glTF geometry), and exactly two
custom shaders — `CheckerFloor.gdshader`, a Playground-only two-colour checkerboard, and
`DecorativeWater.gdshader`. There is nothing here to blend into; the first corrupted surface
will also be the first surface anyone asked to look wrong. `vfx-escalation`, named above as
the owner of session-monotonic severity, exists as a skill but its derivation source is still
an open decision, so severity has no real driver yet. Current bite: none. First real test:
the jungle island's first atmosphere pass, once `/direct` has decided a zone should accumulate
wrongness. Passing looks like a playtester describing a patch as unsettling without describing
it as an effect — and the shared-material count in that zone still fitting on one hand.*
