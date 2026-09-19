# Bubble Test — the Green section: rolling hills, the murky lake, the Postpile island

**Scene:** `scenes/game/world/bubbletest/sections/GreenHills.tscn`
**Anchor:** world (−95, 0, 0), identity rotation (`BubbleTestLayout.GreenAnchor`)
**Footprint:** world x −140..−50, z −50..50 → local x ±45, z ±50 (`GreenFootprint`, BT-0 corrected)
**Baked by:** `tools/dev/bubbletest_bake_green.gd`, once, 2026-08-28. Nothing in the scene
carries a script; nothing generates at runtime (program D2).
**Captured by:** `tools/dev/bubbletest_capture_green.ps1` → `docs/qa/bubble-test/BT-2/`,
11 cameras × day/night, all 1920 × 1080 (program D11).

---

## What is here

A 90 × 100 m field of rolling hills (0 .. +5.51 m) with a lake bitten out of the middle of it.
The lake is a bowl 18 m in radius; at its centre stands an island of hexagonal basalt columns,
reached by eight stepping stones from the west shore. Fall in and the shipped water contract
takes over: cold, slow, and a swim to a shore that is walkable everywhere — **either** shore, since
STONE-2 re-graded the island's flank into a beach (see "The bed, re-graded" below).

```
GreenHills                                   mesh  shape  body
├── Terrain                                    1     1      1    the baked heightfield + trimesh
│   ├── Mesh          terrain.res, green_ground.tres
│   └── StaticBody3D/CollisionShape3D          terrain_shape.res
├── Lake                                       1     0      0    group `no_collider` — see below
│   └── Surface       lake_surface.res, lake_murk.tres
├── SteppingStones    Stone0..7                8     8      8    CylinderMesh + CylinderShape3D
├── Postpile                                   2    32      1
│   ├── Mesh          postpile.res, green_neutral.tres          31 column shafts + 31 caps
│   ├── SummitCap     postpile_summit.res, green_goal.tres      the one saturated thing
│   └── StaticBody3D/Column00..31              ConvexPolygonShape3D per column
└── SectionLabel      "GREEN"                                    depth-tested, at 9 m
                                              ────  ────   ────
                                               12    41     10
```

`--bubbletest-selftest` reports `GreenHills packed[mesh=12 shape=41 body=10]
live[mesh=12 shape=41 body=10] OK`, and the collider audit passes. **The one waiver is the water
surface**, which carries the `no_collider` group: the shipped contract resolves swimming from
*depth* (`WaterGeometry.DepthAt` against the bed), never from a collision with the surface, so a
collider there would break the mechanic it appears to support.

---

## THE GROUNDING PROOF

Water geometry has floated above the ground in this repo before. The packet requires this to be
proven rather than assumed, in numbers and in pixels. Both are below. Every number is printed by
the driver on every run, so a future change that breaks it fails the bake instead of shipping.

### Where the numbers come from

`WaterY` is **not retyped anywhere in this section.** The driver parses
`public const float WaterY = -0.58f;` straight out of `scripts/game/water/WaterGeometry.cs` with a
regex, and cross-checks it against `WATER_Y` in `tools/dev/camp_sites.gd` — the GDScript constant
that C# file's own doc-comment names as the source of truth and calls "duplicated, deliberately
and dangerously". If those two ever drift, this bake refuses to run rather than baking a lake at
one of the two values. `ShoreX`, the wade/swim thresholds and the hysteresis band are read the
same way, as are `GreenMaxHeight`, `GreenMinHeight`, `LakeMaxX`, `LakeBedMinY`,
`LakeBedCentreMaxY`, `StoneTop*`, `StoneGap*` and the four measured jump numbers out of
`BubbleTestLayout.cs`.

```
[bt2] water constants: WaterY=-0.5800 ShoreX=-45.00 (camp_sites.WATER_Y=-0.5800)
```

### Proof 1a — the heightfield (packet acceptance criterion 1)

```
min_outside_basin = +0.0000   (packet: >= 0.0)
max               = +5.5107   (GreenMaxHeight 6.0)
min               = -3.8500   (GreenMinHeight -4.0, LakeBedCentreMaxY -3.5)
basin_max_world_x = -78.0000  (LakeMaxX -50.0)
```

Not one of the 9 191 samples outside the basin is below y = 0, so **the lake is the only thing in
this section under the ground plane** — which is what makes D4 true, and what makes the shipped
`DepthAt()` correct here without CW-3's unmerged water field. The basin's easternmost wet point is
at world x = −78, twenty-eight metres west of the `LakeMaxX` line and thirty-three west of
`ShoreX`.

### Proof 1b — `DepthAt`, at the deepest water and on the hills (criterion 3)

```
deepest water   -3.0,-11.0   world (-98.0, -3.85,-11.0)  terrain -3.847  depth +3.267  -> Swimming
island centre                world (-95.0,  0.30,  0.0)  terrain +0.300  depth -0.880  -> Dry
hill   30.0,  25.0           terrain +4.287  depth -4.867  -> Dry
hill  -30.0, -30.0           terrain +3.847  depth -4.427  -> Dry
hill   35.0, -20.0           terrain +3.950  depth -4.530  -> Dry
```

> **Packet correction.** The packet asks for "the basin centre" to read terrain ≤ `WaterY` − 2.5
> and `DepthAt` > 2.5, but its own item 1 puts the **island plateau** (radius 6 m, top +0.3) at
> local (0, 0, 0). The centre is dry land by construction and cannot be both. The check is
> therefore measured at the **deepest point of the lake**, found by scanning the baked field —
> which is the thing the criterion is actually asking about — and the island centre is reported
> alongside it and must read `Dry`. Both hold.

### Proof 1c — the water mesh edge is buried all the way round (criterion 2)

```
boundary vertices checked = 96   (all of them)
min margin above WaterY   = +0.2269 m at segment 82   (packet: >= 0.05)
outer radius min/max      = 16.940 / 16.960 m (overlap 0.60 m into the bank)
```

The surface is a 96-segment disc. Its outer radius is found **per ray, against the baked
heightfield** — not against the ideal bowl — by walking outward until the ground stops being below
`WaterY`, then adding the packet's 0.60 m of burial. At every one of the 96 boundary vertices the
terrain is at least 0.227 m *above* the water plane, so the rim is inside the bank with 4.5× the
required margin. There is no vertex anywhere on the boundary where the mesh could be seen from
below.

### Proof 2 — the pixels (criterion 4)

Three frames, day and night, at 1920 × 1080:

| Frame | What it shows |
|---|---|
| `shore-grazing-a-{day,night}` | Camera 0.4 m above the surface looking along the water at the north-east bank. The waterline is one continuous contact from edge to edge of frame; no sliver of sky and no sliver of ground appears under the water's edge at any point. |
| `shore-grazing-b-{day,night}` | The same grazing angle from the opposite side, with the island and one stepping stone in shot. The shoreline sweeps from centre-left to lower-right as an unbroken curve; the far bank rises directly out of the water with no gap under it. |
| `under-bed-{day,night}` | From 8 m below the lake bed looking up. **Sky.** The terrain is back-face culled and the water plane is not visible at all — the packet's "only terrain back-faces or nothing", and specifically *never* a lit quad hanging over a hole. |

Both grazing cameras are aimed on chords that pass clear of the island (closest approach r = 10.7 m
and 11.4 m against an island radius of 6 m), so nothing occludes the far shoreline they exist to
show.

### Proof 3 — against the collider that actually shipped

`--verify` re-loads the **committed** `.tscn` at its anchor, raycasts onto its real collision
shapes, and resolves the shipped state function at the depth a pair of feet would rest at
(criterion 8):

```
deep water    feet y  -3.742  depth  +3.162  -> Swimming  (want Swimming ) OK
hilltop NE    feet y  +4.287  depth  -4.867  -> Dry       (want Dry      ) OK
hilltop SW    feet y  +3.847  depth  -4.427  -> Dry       (want Dry      ) OK
island top    feet y  +3.500  depth  -4.080  -> Dry       (want Dry      ) OK
stone 0 top   feet y  +0.300  depth  -0.880  -> Dry       (want Dry      ) OK
stone 7 top   feet y  +0.420  depth  -1.000  -> Dry       (want Dry      ) OK
```

Every sample lands on a collider — a miss is reported as "NO COLLIDER UNDER IT", which is the
failure this repo has shipped before. **What this does not prove:** it does not run
`SandboxAvatar`, so it shows where a body would rest and what the pure state function says about
that depth, not that the avatar's own integration agrees. That gap is stated in the packet report.

---

## The crossing

`/direct` pass 2026-08-28, `THRILL-BIBLE.md` §12 ("The Bubble Test's green crossing"). The
directed feeling is **hesitation at the moment of committing**, and the affect budget is exactly
one thing: *you can read the whole crossing before you step, and you cannot read the one thing that
would tell you what stepping wrong costs.* Six of the seven dread-gate items fail here and every
one of those failures is correct — this is an instrument level, not a horror level.

Four composition requirements came out of that pass. Each is a constant or an assert in the driver:

1. **The whole route is legible from the west bank before the first step.** One unbroken line
   across open water with nothing occluding it. Shot: `stones-shore-{day,night}`.
2. **Gaps generous at both ends, tightest in the middle, and the last gap is never the hardest.**
   The driver fails the bake if the last gap is the largest.
3. **The line is not straight** — the z drift means each hop needs a re-aim.
4. **Falling in is recoverable everywhere; the island is reachable only by the stones.**

### The stones, measured

| # | local x | z | top | bed under it | depth under | centre-to-centre | edge-to-edge |
|---|---|---|---|---|---|---|---|
| 0 | −18.00 | 0.00 | 0.30 | 0.00 | −0.58 | — | — |
| 1 | −16.65 | 0.45 | 0.28 | −0.46 | −0.12 | 1.423 | 0.473 |
| 2 | −15.15 | 0.05 | 0.35 | −1.11 | 0.53 | 1.552 | 0.602 |
| 3 | −13.40 | −0.55 | 0.32 | −1.96 | 1.38 | 1.850 | 0.900 |
| 4 | −11.60 | −0.30 | 0.40 | −3.02 | 2.44 | 1.817 | 0.867 |
| 5 | −9.85 | 0.35 | 0.34 | −2.64 | 2.06 | 1.867 | 0.917 |
| 6 | −8.35 | 0.60 | 0.38 | −1.52 | 0.94 | 1.521 | 0.571 |
| 7 | −6.90 | 0.15 | 0.42 | −0.48 | −0.10 | 1.518 | 0.568 |

*The `bed under it` and `depth under` columns are STONE-2's, re-measured after the re-grade; the
positions, tops and gaps are untouched. **Stone 7 now stands on dry ground and Stone 6 in 0.94 m of
water** — the shelf that makes a fall survivable had to reach out far enough to meet the last two
stones, and this is what that costs: the final hop lands on a beach rather than over deep water.
Recorded because it changes how the crossing READS, not only what it measures — see
`docs/qa/2026-08-29-STONE-2/stone7-down-day/Stone2-6s.png`.*

Stone diameter 0.95 m. Every top is inside `StoneTopMin..Max` (0.25–0.45). Every centre-to-centre
gap is inside `StoneGapMin..Max` (1.4–2.2). The worst **edge-to-edge** gap — the distance actually
jumped — is 0.917 m, and the worst rise between adjacent tops is 0.08 m. The hardest gap is #5 of
7; the last is 1.518 m, deliberately not the hardest.

> **⚠ CORRECTION, measured 2026-08-29 by STONE-2. The jog-tap numbers this paragraph used to quote
> were MOVE-3e's and are dead.** It said the 0.917 m edge gap sat against "the *measured* jog-tap
> range of 1.710 m". `tools/dev/motor_arc.gd`, re-run against the shipped `MotorTuning`, reports
> **tap 0.300 m apex / 0.887 m range** — MOVE-8 re-derived the arc from the tuning rows and the
> jog-tap range fell to about half of what this page carried. **Two edge gaps are therefore over
> it: #3 at 0.900 m and #5 at 0.917 m**, and `_assert_stones()` has been failing on the shipped
> stone table ever since — which is why `GreenHills.tscn` could not be regenerated by its own
> driver at all. STONE-2 waived those two BY NAME in the bake (each pinned to the value it has
> today, so a third gap, or either of these growing, still fails) rather than re-space the
> crossing, because /direct requirement 2 rules the spacing *pattern* and re-spacing is a design
> pass. The held jump clears them easily (range 4.053 m). **Re-spacing, or re-ruling the bar, is an
> open decision — see `docs/agents/roles/environment/outbox/2026-08-29-STONE-2-report.md`.**

> **Ambiguity resolved and recorded.** §4 rule 2 says "gaps of 1.4–2.2 m (jog-tap range is
> 1.71 m)", and 2.2 > 1.71 — the two halves of that sentence cannot both be about the same
> distance. `BubbleTestLayout.StoneGapMax`'s own doc-comment says the value is "held under
> TapRange", which is only true once the stone has a width. So: **gap = centre-to-centre**,
> **jumped distance = edge-to-edge**, and both are asserted, both are printed, and both are in the
> table above. BT-1 measured its cairn ladders as edge gaps for the same reason.

Each stone reaches all the way down to the bed and is driven 0.30 m into it — **no stepping stone
floats**, and the deep ones are 4 m tall posts of which 0.3–0.4 m shows. Once `lake_murk.tres`
is opaque, only the tops are visible, which is the intended read.

### The bed, re-graded — why the island cannot be *swum* to (STONE-2, 2026-08-29)

**This section replaces "Why the island cannot be climbed out of the water", and the mechanism that
section described is deleted.** That mechanism was a **57.6° island wall** running r 6.0 → 8.5, and
it did keep the island the stones' reward. It also made the *other* half of /direct requirement 4
false, and STONE-1 measured how false by driving a real `CharacterBody3D` through
`AvatarMotor.Step` against this collider:

- the deep annulus was **6.65 m** wide (submerged contours r 7.24 … 13.889) against a **measured**
  escape reach of **4.40 m** in the 3.0 s `DrownAfterSec` allows. Falling off **Stone6** or
  **Stone7** drowned on **every one of 16 headings, in every load case**;
- a body left standing on the shelf with **no input at all** slid down the flank and drowned in
  3.1–3.3 s, from every start radius between 6.30 and 7.20.

Requirement 4's second half is now carried by the **width of the deep annulus** instead — the one
mechanism that does not cost the first half. Both halves are the same number measured from opposite
ends, and `_assert_bed()` gates both.

| Measured (STONE-2, against the collider) | Value | Why |
|---|---|---|
| Bowl outer ramp | **26.6°** | Unchanged. Under the 32° walk limit all the way round: a player who falls in can always walk out — `THRILL-BIBLE.md` §7.4's anti-unwinnable guarantee, as geometry. |
| Steepest facet a body can STAND on | **41.8°** | Under `CharacterBody3D.floor_max_angle` (45°, and nothing in this repo overrides it). The terrain is brought to the body, never the reverse. |
| Island beach (r 6.0 → 7.5 → 8.8) | **40.5° then 30.6°** | Walkable. A body left on it with no input slides ≤ 0.031 m and never drowns, at every radius from 6.00 to 8.80. |
| Inner submerged contour | **r 8.666 … 8.711** | Was 7.205 … 7.294. |
| Outer submerged contour | **r 13.877 … 13.889** | **Unchanged** — from r = 12 outward the profile is the old bowl's, bit for bit, so the outer bank, the y = `WaterY` contour at r ≈ 16.36 and the 16.96 m lake mesh are untouched. |
| Deep annulus width | **5.175 … 5.202 m** | Was 6.595 … 6.681. Wider than the 4.40 m best-case reach (so the bank cannot be left) and under twice the 3.00 m worst-case reach (so no landing is out of range of both shores). |
| Deepest bed | **−3.832 at r ≈ 11.4** | Was −3.850 at r = 9. Still inside `LakeBedMinY` (−4) and under `LakeBedCentreMaxY` (−3.5). |

**Driven results, 320 landings** — every stone × 16 headings × four load cases, the worst being
carrying (`speedFactor` 0.75) **and** `Soaked`: **0 drowned**, against **144 of 320** before (42 of 80 in that same worst case). A swimmer
setting off from the outer contour toward the island drowns on all three bearings tested, 0.47–0.57 m
short of the beach. Evidence: `docs/qa/2026-08-29-STONE-2/probe-before.log` and `probe-after.log`;
the instrument is `tools/dev/Stone2Probe.cs`.

The bed profile is deliberately **not** a smoothstep bowl, and the beach is deliberately
**piecewise linear**. A smoothstep over the same span peaks at 1.5× the mean gradient, which put a
~35° face exactly where a swimmer transitions to wading — and any easing on the beach would push
its steepest facet through `floor_max_angle`, which is the one thing the beach may not do. Linear
means the steepest facet *is* the steepest segment. From r = 12 outward the profile is unchanged: a
mostly-linear ramp to 0 at r = 18 with 35 % of a smoothstep blended back in to soften both creases.

---

## The Postpile island

32 hexagonal columns, 0.80–3.20 m tall, on a plateau of radius 6 m at +0.3 m. The **cells** come
from `tools/dev/postpile_lab.gd`'s own generator — `_seed_points()` (Poisson, five Lloyd
relaxations toward blue noise) and `_cell_for()` (half-plane Voronoi clip), seed 20260828 — with
the 32 cells nearest the lab pile's centre taken and **uniformly rescaled ×2.71** onto the island.
Uniform scaling leaves the side-count histogram exactly invariant, which is the whole reason that
generator exists rather than a jittered hex grid:

```
side histogram (n = 32): 4-sided 1 · 5-sided 13 · 6-sided 12 · 7-sided 6 · mean 5.72
```

Devils Postpile surveys give 4-sided 2–9.5 %, 5-sided ~37 %, 6-sided 44.5–55 %, 7-sided 5–8 %,
mean 5.5–5.7. At n = 32 this run sits inside the band on 4- and 5-sided, slightly low on 6-sided
and high on 7-sided; that is small-sample scatter on a 32-cell draw, not a different distribution.

Column tops: worst neighbour rise **0.993 m**, worst neighbour centre gap **1.972 m**. The whole
dome is climbable with a held jump. Green is the water section — blue is the precision one.

> **⚠ The MOVE-3e envelope this paragraph used to quote is dead too** (jog-tap apex 0.467 / range
> 1.710, sprint apex 1.534 / range 6.192). Re-run 2026-08-29, `tools/dev/motor_arc.gd` derives from
> the shipped `MotorTuning`: **tap 0.300 / 0.887, held 1.407 / 4.053, double 2.410 / 6.485.** The
> bake prints these live on every run and asserts against them, so its own numbers are current; it
> is this page that was carrying MOVE-3e's. Against the current arc the postpile is a held-jump
> climb, not a jog-tap staircase — which is what the bake's own `_assert_pile()` comment already
> said it was. Same correction as the stepping-stone table above; see STONE-2's report.

**The winding bug is not reproduced.** Playtest-2's postpile shipped with "sixty columns wound
backwards — sides AND cap". Rather than guess Godot's convention, the driver **measures** it at run
time off `BoxMesh`'s own +Y face (a primitive that is correct by construction) and derives the sign
that makes `cross(b−a, c−a)` point along the intended outward normal; every triangle it emits is
flipped against that measured sign. `postpile-tops-{day,night}` confirms it in pixels: solid, lit,
front-facing prisms with the saturated green summit cap on the tallest.

---

## The hills

`camp_height.gd` is **called, never edited** (`.claude/rules/single-writer.md`). Layers used:
`_landform(x, z)` (the domain-warped ridge/roll field) + `_micro(x, z)` (fine rolling texture), on
a 2 m grid, then bilinearly resampled onto the 1 m mesh grid and affinely normalised to
0 .. 5.85 m.

Deliberately excluded, and why: `_bowl` (a radial ramp keyed to the camp's own map centre — it
would tilt this section), `_spur` (two named camp landforms), `_terrace` (contour steps, which
would fight the stepping stones for the eye), `_heave` (gated on canopy cover, and there is no
canopy here), and the trail grading and building pads (all camp-specific).

The sample window is camp-space (60, 150) + local, chosen so both of `_landform`'s gates are fully
open across the whole footprint: minimum radius 101 m against `LANDFORM_R1` = 80, and minimum
x = 15 m against `LAKE_X_MAX + SHORE_TAPER_M` = −25. Sampling anywhere a gate is partly closed
would have faded the hills to nothing across part of the section for reasons belonging to another
map.

Two shaping rules on top:

- **The outer 6 m of the footprint tapers to y = 0**, so the section meets the hub's 12 m
  connector (§4 rule 4) and its neighbours flush rather than in a step.
- **Hills are suppressed inside the basin rim and faded back in over r = 18..28 m.** Without this
  the hills fight the bowl and the −0.58 contour collapses to a puddle round the island — measured:
  the naive version put the shoreline at r ≈ 7 m instead of r ≈ 16.3 m.
