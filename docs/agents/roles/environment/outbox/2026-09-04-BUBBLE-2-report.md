---
packet: BUBBLE-2
role: environment
date: 2026-09-04
branch: fix/2026-09-04-bubble-2-embedded-bubble
base: playtest/2026-09-04-combined
type: report
---

# BUBBLE-2 — the bubble in the tower, and the check that missed it

**It was `Bubble_Tangle_10`, id 82, in the Tangle.** It sat at world **(76.401, 20.499, −65.948)**,
**inside `Tangle/Tower/Jumble01`** — centre in solid, **100% of its collider shell buried**. It is
now at **(75.151, 20.999, −65.948)**, 1.35 m away, in open air on the stair line between treads 19
and 20. **Its id is unchanged: still 82** (ids come from an ordinal node-path sort and a `position`
edit moves no node).

**The hole is closed.** `BubbleTestSelfTest.CheckBubbleEmbedding` now measures **all 100** bubbles
against the live collision world on every `--bubbletest-selftest` run, with three controls that
must fire first. Before this packet nothing in the suite measured an outdoor bubble's surroundings
at all: `CheckIndoorReach` (BUBBLE-1) covers 25 indoor bubbles, and the other 75 — including this
one — were measured by nobody.

---

## 1. Finding it — and the discriminator

**The probe.** Three tests per bubble, against `GetWorld3D().DirectSpaceState`, on the **authored**
centre (not the live one — the bob is running and is up to 0.15 m off rest at any instant, so
judging the live position would make the verdict irreproducible):

1. **Contact** — a sphere query of the bubble's own `ColliderRadiusM` (0.35 m) at its centre. What
   is inside the volume the player pops. **Reported, never asserted:** grazing a rock is legal.
2. **Containment — the discriminator.** A 0.02 m point probe at the centre. **Is the centre itself
   inside a solid?**
3. **Enclosure** — the avatar's own capsule, at 16 headings × 4 distances (0 … 0.60 m) × 5 heights
   (±0.70 m), keeping only placements whose surface actually reaches the 0.35 m sphere (that *is*
   the pop condition — a bubble is an `Area3D` that fires on `BodyEntered`). **At least one must be
   clear of the world.**

**The discriminator I chose is point containment of the centre, and it separates the two cases by
kind rather than by degree.** A bubble 0.45 m from a golden cube — BUBBLE-1 shipped two at 0.45 m
and 0.92 m before catching them — has a free centre *no matter how close it gets*, right up until
its centre crosses the cube's surface. A bubble clipped into a tower does not. Every proximity
threshold I could have used instead would have needed a number somebody has to tune, and would
have started condemning legal placements the moment a section got denser. This needs no number.
**It is proved on that exact geometry every run**: control 2 below plants a probe 0.45 m from
`GoldCube_Hub_0` and *requires it to come back free*.

Test 3 exists because containment alone is not sufficient — a bubble can have a free centre and
still be walled in. It uses free capsules with **no gravity and no floor requirement**, on purpose:
half this level's bubbles are reached by a jump (`Bubble_Blue_15` is on a 41.9 m summit), and
demanding a floor under one would fail the level's own design.

**I did not use Talon's description to find it.** The probe swept all 100 and named one. Only after
that did I check the corroboration, and it holds — see §5.

**Nothing else in the level is buried.** Exactly one of the 100 came back BAD. Exactly one other
bubble in the whole level even *touches* geometry (`Bubble_Tangle_11`, against `Tower/Tw00`, centre
free, a body 0.25 m out pops it).

### The probe's full output — all 100 bubbles, and the three controls

Run: `godot --headless --path . -- --bubbletest-selftest` on the tip. Node paths trimmed of the
`/root/Boot/BubbleTestSelfTest/BubbleTest/` prefix; nothing else edited.

```
embed: point probe r 0.020, bubble collider r 0.35, body capsule r 0.125 h 1.160, search 0.60 m, 5 avatars excluded
embed CONTROL cube centre        -> caught: EMBEDDED — its centre is inside Props/GoldCube_Hub_0/Body; 16% of its collider shell is buried too
embed CONTROL 0.45 m from cube   -> free: touches Hub/Ground/Body, Props/GoldCube_Hub_0/Body but its centre is free; a body at 0.00 m out, +0.70 m up pops it
embed CONTROL sealed shell       -> caught: ENCLOSED — its centre is free but no body position within 0.60 m can reach it; clear of geometry; 0% of its collider shell is buried
embed id   0 Bubble_Blue_01     (  94.490,   1.450, -22.120) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   1 Bubble_Blue_03     (  79.530,   3.450, -23.790) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   2 Bubble_Blue_04     (  77.000,   1.300, -28.900) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   3 Bubble_Blue_05     (  64.600,   6.450, -12.390) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   4 Bubble_Blue_07     (  80.770,  10.650, -10.050) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   5 Bubble_Blue_08     (  88.810,  11.650,   2.800) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   6 Bubble_Blue_10     (  77.950,  13.650,  10.150) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   7 Bubble_Blue_11     (  71.200,  15.150, -13.400) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   8 Bubble_Blue_13     (  93.940,  19.600,   0.930) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id   9 Bubble_Blue_14     (  70.090,  37.650,   4.970) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  10 Bubble_Blue_15     (  79.000,  41.900,   0.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  11 Bubble_Cyan_01     ( -30.000,   1.100,  55.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  12 Bubble_Cyan_04     ( -30.000,   1.100,  85.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  13 Bubble_Cyan_07     ( -30.000,   1.650, 115.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  14 Bubble_Cyan_08     (  30.000,   1.100,  64.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  15 Bubble_Cyan_11     (  30.000,   1.550, 112.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  16 Bubble_Cyan_13     (   0.000,   1.100, 141.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  17 Bubble_Cyan_15     (   0.000,   2.550, 145.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  18 Bubble_Cyan_16     ( -58.000,   1.250,  60.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  19 Bubble_Cyan_18     ( -63.000,   1.900,  78.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  20 Bubble_Cyan_20     ( -61.700,   0.950,  95.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  21 Bubble_Cyan_22     ( -57.000,   1.000, 114.700) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  22 Bubble_Cyan_24     ( -48.000,   1.850, 143.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  23 Bubble_Cyan_25     ( -16.000,   1.600,  56.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  24 Bubble_Cyan_27     ( -22.000,   1.000,  92.600) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  25 Bubble_Cyan_29     ( -20.000,   1.500, 122.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  26 Bubble_Cyan_30     (  14.000,   1.450,  58.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  27 Bubble_Cyan_32     (  48.000,   1.600,  62.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  28 Bubble_Cyan_33     (  60.000,   1.700,  80.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  29 Bubble_Green_01    (-113.000,   1.100,   0.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  30 Bubble_Green_02    (-110.150,   1.150,   0.050) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  31 Bubble_Green_03    (-106.600,   1.200,  -0.300) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  32 Bubble_Green_04    (-103.350,   1.180,   0.600) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  33 Bubble_Green_05    (-101.950,   0.100,   1.300) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  34 Bubble_Green_06    ( -95.070,   4.150,   0.360) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  35 Bubble_Green_08    ( -92.020,   2.800,  -0.210) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  36 Bubble_Green_11    (-126.000,   5.250, -32.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  37 Bubble_Green_12    ( -60.000,   5.000,   0.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  38 Bubble_Green_13    (-120.000,   4.250, -24.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  39 Bubble_Green_14    (-108.000,   5.400, -40.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  40 Bubble_Hub_01      (   2.600,   1.100,  -8.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  41 Bubble_Hub_05      (  18.000,   1.050,  15.600) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  42 Bubble_Hub_06      ( -18.000,   1.050,  15.600) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  43 Bubble_Hub_07      (   8.000,   1.300, -39.400) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  44 Bubble_Hub_08      ( -38.500,   1.300,   8.900) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  45 Bubble_Hub_09      (  30.000,   1.100, -30.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  46 Bubble_Hub_10      ( -30.000,   1.650,  30.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  47 Bubble_Seam_01     (   0.000,   1.100, -43.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  48 Bubble_Seam_02     (   3.500,   1.450, -44.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  49 Bubble_Seam_03     (  43.000,   1.100,   0.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  50 Bubble_Seam_05     (   0.000,   1.100,  45.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  51 Bubble_Seam_07     ( -45.000,   1.100,   0.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  52 Bubble_Lab_01      (  90.000, -13.000,  90.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  53 Bubble_Lab_02      (  86.000, -13.000,  94.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  54 Bubble_Lab_03      (  95.000, -13.000,  92.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  55 Bubble_Lab_04      (  93.000, -13.000,  88.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  56 Bubble_Lab_05      ( 100.516, -14.411,  76.338) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  57 Bubble_Lab_06      ( 104.628, -17.033,  72.267) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  58 Bubble_Lab_07      ( 112.770, -19.614,  72.060) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  59 Bubble_Lab_08      ( 128.640, -19.900,  72.060) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  60 Bubble_Lab_09      ( 156.240, -19.900,  72.060) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  61 Bubble_Lab_10      ( 165.624, -19.900,  79.788) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  62 Bubble_Red_01      ( -29.910,   6.950, -67.400) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  63 Bubble_Red_03      (  26.210,  12.350, -86.100) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  64 Bubble_Red_04      (  28.420,   3.400, -92.090) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  65 Bubble_Red_05      ( -14.500,   1.600, -72.990) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  66 Bubble_Red_06      (  15.980,   1.750, -65.010) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  67 Bubble_Red_08      (   0.000,   1.800, -68.370) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  68 Bubble_Red_10      (  -6.370,  10.550, -76.810) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  69 Bubble_Red_11      (  -0.160,  23.600, -78.260) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  70 Bubble_Red_12      (  31.520,   5.700, -84.660) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  71 Bubble_Red_13      (   0.000,   1.600, -84.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  72 Bubble_Red_14      ( -10.000,   1.400, -84.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  73 Bubble_Tangle_01   (  57.000,   1.200, -62.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  74 Bubble_Tangle_02   (  84.608,   2.350, -61.126) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  75 Bubble_Tangle_03   (  79.219,   4.702, -58.667) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  76 Bubble_Tangle_04   (  73.301,   6.504, -58.915) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  77 Bubble_Tangle_05   (  68.251,   8.410, -62.007) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  78 Bubble_Tangle_06   (  65.572,  10.227, -67.286) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  79 Bubble_Tangle_07   (  66.369,  12.030, -73.146) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  80 Bubble_Tangle_08   (  76.516,  15.850, -77.351) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  81 Bubble_Tangle_09   (  80.544,  18.719, -70.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  82 Bubble_Tangle_10   (  75.151,  20.999, -65.948) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  83 Bubble_Tangle_11   (  73.408,  21.334, -66.409) ok  touches Tangle/Tower/Tw00 but its centre is free; a body at 0.25 m out, 0.00 m up pops it
embed id  84 Bubble_Tangle_12   (  71.709,  24.036, -71.729) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  85 Bubble_Tv_01       (   0.000, -18.600,  -1.600) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  86 Bubble_Tv_02       (   4.500, -19.000,  -4.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  87 Bubble_Tv_03       (  -5.200, -19.400,   5.200) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  88 Bubble_TvB_01      ( -15.400, -18.800,  -3.200) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  89 Bubble_TvB_02      ( -21.400, -18.800,   1.600) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  90 Bubble_TvB_03      ( -16.200, -18.900,   4.300) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  91 Bubble_TvC_01      (  12.000, -18.800,  -2.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  92 Bubble_TvC_02      (  22.500, -18.800,   1.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  93 Bubble_TvC_03      (  19.000, -18.700,  -4.200) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  94 Bubble_TvD_01      (  -4.000, -18.800, -16.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  95 Bubble_TvD_02      (   5.000, -18.800, -19.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  96 Bubble_TvD_03      (  -3.000, -18.600, -13.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  97 Bubble_TvF_01      ( -17.000, -18.800, -18.500) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  98 Bubble_TvF_02      ( -20.200, -18.800, -21.000) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed id  99 Bubble_TvF_03      ( -15.000, -18.700, -13.200) ok  clear of geometry; a body at 0.00 m out, 0.00 m up pops it
embed: 100 bubbles measured, 0 buried (3 controls fired first)
```

**The same sweep on the pre-fix tree**, for the one line that was different:

```
embed id  82 Bubble_Tangle_10   (  76.401,  20.499, -65.948) BAD EMBEDDED — its centre is inside
  Tangle/Tower/Jumble01; 100% of its collider shell is buried too  |  Tangle/Bubbles/Bubble_Tangle_10
embed: 100 bubbles measured, 1 buried (3 controls fired first)
```

---

## 2. Fixing it

| | |
|---|---|
| **Node** | `Tangle/Bubbles/Bubble_Tangle_10`, in `scenes/game/world/bubbletest/sections/Tangle.tscn` |
| **Id** | **82 before, 82 after — UNCHANGED.** Ids are the index of an ordinal node-path sort; the node keeps its parent and its name, so its path does not move. No re-bake, no constant edited, no renumbering. |
| **Before** | local `(1.401, 20.499, 14.052)` → world **(76.401, 20.499, −65.948)** |
| **After** | local `(0.151, 20.999, 14.052)` → world **(75.151, 20.999, −65.948)** |
| **Move** | **1.35 m** — 1.25 m along −x, 0.50 m up |

**How it got there, because the cause matters more than the coordinate.** Every stair bubble in the
tangle sits directly over a tread: measured off the live tree, the twelve are within **0.02–0.06 m**
in xz of their tread's origin, at **+1.77 to +1.94 m**. `Bubble_Tangle_10` was over tread `Stair19`
at +1.93 m — exactly on pattern, and legal when BT-11 placed it, with the section's 0.50 m clearance
bound (collider 0.35 + bob 0.15) satisfied. **Then W7-1 built the spire.** Its skirt block
`Tower/Jumble01` — 2.04 × 3.15 × 4.03 m, centred (77.673, 21.807, −64.898) — landed on that tread's
airspace. A column scan at the bubble's exact xz shows the whole band from **y 17.0 to y 23.4** is
solid (`Rubble39`, `Stair19`, `Jumble00`, `Jumble01`); the first air with 0.50 m of room is
**y 23.9**, on top of the block. The bubble did not move. The level was built over it, through a
green suite, every run.

**Why this position and not the nearest one.** The nearest clear spot is 1.25 m away with only
**0.55 m** of free radius — 7 mm of physical slack under the worst phase of the bob. The chosen one
is 0.10 m further and has **0.75 m** of free radius, better than the section's own worst shipped
clearance (0.588 m at `Bubble_Tangle_04`), with **1.01 m** of headroom over the surface underneath.
Both numbers were measured in the live collision world, not estimated.

**What it keeps.** It stays in the Tangle, in the same cluster, on the stair line between treads 19
(76.401, 18.572, −65.949) and 20 (73.408, 19.502, −66.409) — 41% of the way along, so a climber
going 19 → 20 walks through it. And it stays **between its neighbours in height**: `Tangle_09`
18.719 < **20.999** < `Tangle_11` 21.334, so the climb still reads as a rise, which is the whole
reason `docs/levels/bubble-test-bubbles.md` gives for the tangle keeping twelve ("ten of them ARE
the stair"). The alternative — lifting it to y 23.9 onto the block's roof — clears the geometry with
a smaller edit and destroys that reading, putting tread 19's bubble above tread 20's and 21's.

**Counts and constants: nothing moved.** `BubbleTarget` 90, `PuffinLabBubbles` 10,
`BubbleTargetWithLab` 100, `BubblesOf(Tangle)` 12 — all untouched. `[bubble] adopted 100 bubble(s)
(server=True)`.

**`Bubble_Seam_02` (id 48, Hub) was not the offending bubble** and has not been touched — it comes
back `ok, clear of geometry` at (3.5, 1.45, −44.5) in the sweep above. **BUBBLE-1's relocation
candidate for the rooftop packet is still available.**

---

## 3. The standing check

`BubbleTestSelfTest.CheckBubbleEmbedding`, called from `MeasureThenProbe` right after
`CheckIndoorReach` — it rides the same one `PhysicsSettleSeconds` wait, because a body that entered
the tree this frame is not in the physics space yet. It runs under `--bubbletest-selftest` with
everything else, so `tests/Run-BubbleTestWorldTest.ps1` and the marathon both gate on it.

**Three controls, each firing a different branch, all three run before a single bubble is believed.**
Their live output is the first four lines of the block in §1.

| Control | Branch it proves | Result |
|---|---|---|
| The centre of `GoldCube_Hub_0` | test 2, containment | `caught: EMBEDDED — its centre is inside Props/GoldCube_Hub_0/Body` |
| **0.45 m to the side of the same cube** | **test 2 must NOT over-fire** | `free: touches Hub/Ground/Body, Props/GoldCube_Hub_0/Body but its centre is free` |
| A sealed 6-plate shell, staged 400 m under the map and freed the instant it has fired | test 3, enclosure — **with a free centre**, so it is not a second copy of test 2 | `caught: ENCLOSED — its centre is free but no body position within 0.60 m can reach it` |

The second one is the discriminator proving itself on the exact geometry BUBBLE-1 shipped. If it
ever fires, the check has decayed into a proximity test and is about to start condemning legal
placements — that is worth a red on its own.

**The shell control found a real defect in itself, and the fix is recorded in the code.** Staged
*inside* the check, it reported itself FREE: a body added this frame is not in the physics space
when the check queries it. That is precisely "the control silently stopped controlling". It is now
staged in `Run()`, before the settle, and it fires.

### The positive control, run and reverted

Two healthy bubbles in two different sections were moved into geometry on a scratch tree, the
suite was run, and the check named both — id, node path and position — and exited 1:

```
embed id  11 Bubble_Cyan_01     ( -30.000,  -0.500,  55.000) BAD EMBEDDED — its centre is inside
  CyanRun/Ground/Body; 66% of its collider shell is buried too  |  CyanRun/Bubbles/Bubble_Cyan_01
embed id  83 Bubble_Tangle_11   (  77.673,  21.807, -64.898) BAD EMBEDDED — its centre is inside
  Tangle/Tower/Jumble01; 100% of its collider shell is buried too  |  Tangle/Bubbles/Bubble_Tangle_11
embed: 100 bubbles measured, 2 buried (3 controls fired first)
[bubbletest-selftest] FAIL (1)
```

Both plants were reverted; `git diff` on both scene files is clean of them and the tip run is the
0-buried block in §1. **Two plants rather than one on purpose** — one in a section the check had
never flagged, sunk into flat terrain, so nothing about the catch is specific to the tangle or to
the bubble this packet was sent to fix.

### What this check does NOT cover — read this before quoting a green

**It proves a bubble is not inside geometry and that a body could occupy a position that pops it.
It does not prove a route to that position exists.** A bubble in open air over a chasm passes this
and could still be uncollectable. Concretely, three gaps remain:

1. **No route reachability, outdoors or in.** BUBBLE-1's limitation stands unchanged:
   `ScriptedGotoIntentSource` walks to ONE point and latches there permanently, and a bot cannot be
   spawned inside a sealed room, so a multi-leg driven route is not something this harness can
   produce. Building one is a change to `scripts/game/sandbox/`'s intent sources, which this role
   does not own and which this packet explicitly put out of scope. **Naming it, not fixing it.**
2. **No gravity, no floor requirement.** The body placements are free capsules. That is deliberate
   (see §1) and it means a bubble hung in mid-air with nothing to jump from passes.
3. **The bob is not swept.** The verdict is taken at the authored rest centre, so it is
   reproducible. A bubble whose *rest* centre is 1 cm outside a wall and which oscillates 0.15 m
   into it would pass; the shell-buried percentage would show it, but nothing asserts on that
   number. In practice the 0.50 m clearance convention in `docs/levels/bubble-test-bubbles.md`
   covers this by a wide margin — no shipped bubble is anywhere near the boundary.

**What "all bubbles reachable" is actually backed by, as of this packet:** 100/100 are proved not
embedded and not enclosed (this check); 25/100 additionally have a floor, a standing spot and — for
19 of those — a swept straight-line walk from the room's arrival point (`CheckIndoorReach`); 6 of
the lab's ride `PuffinLabSelfTest`'s nineteen-station route. **Nothing is proved by a driven bot.**
That is stronger than eyeballing and weaker than a playthrough, and it is the honest statement.

---

## 4. What Talon can verify himself, in under a minute

Launch the game, walk to the **Tangle** — the magenta-floored section, north-east — and climb the
stair up the pile. The bubble you could not reach is `Bubble_Tangle_10`; it used to be inside the
big grey block at the foot of the spire and it is now **in open air 1.25 m to the west and half a
metre higher, on the line between the last two treads before the spire** — you pass through it on
the way up, between the two bubbles you *could* already get. If you would rather not climb: run
`godot --headless --path . -- --bubbletest-selftest` and read the `embed:` line at the end — it says
`100 bubbles measured, 0 buried`, and above it there is one line per bubble with its id and world
position. The before/after pictures from the same camera are in `docs/qa/BUBBLE-2/`.

---

## 5. "The magenta area", and the two towers — the corroboration, checked after the fact

**The Tangle is the magenta section, and it is measurable rather than a guess.**
`resources/materials/bubbletest/tangle_ground.tres` is **HSV(300, 0.30, 0.482)** and
`tangle_goal.tres` is **HSV(300, 0.86, 0.555)**. Hue 300° is magenta by definition, and the tangle
is the only section on it — blue is 222°, cyan 183°, green 127°, the hub 38°, red 16°. The whole
tangle **ground plate** is that colour (it is the pink checker under the pile in all four captures),
and the brighter violet chips are the goal colour, worn by exactly three surfaces: the stair's last
tread `Stair22`, and W7-1's `Tower/SpurPad` and `Tower/Summit`. **So when he says magenta, he means
the tangle** — and if he says it again, that is what to look at.

**Which tower.** The **tangle spire**, not the blue tower. The buried bubble is at the spire's foot.
**On "about halfway up" I will not overclaim:** the bubble is at y 20.5 and the spire's summit is
60.789 m, so it is a third of the way up in absolute terms — though the spire's own base sits at
y 22.7 on top of the pile, and the pile's stair (which is what you climb, and which is where he was)
tops out at 24, so "halfway up the thing I was climbing" is a fair description of where he stood.
The description was corroboration; the probe was the search.

---

## Corrections to the packet

1. **"BUBBLE-1's report at `docs/agents/roles/environment/outbox/2026-09-04-BUBBLE-1-report.md` on
   your base"** — correct, and worth stating for the next packet: that file exists **only** on
   `playtest/2026-09-04-combined`, not on `main`. Anyone reading this from `main` will not find it.
2. **`tools/dev/tangle_stair_check.gd`**, cited by `docs/levels/bubble-test-bubbles.md` as the
   instrument that measured the tangle's clearances, **does not exist in this repo.** It is a
   deliberate `Sail` dead link (CLAUDE.md: provenance, not rot) and I did not invent a local copy.
   Its job is now done in-engine and for the whole level by `CheckBubbleEmbedding`.
3. **`DECISION-LOG.md` is outside this role's write paths** — same finding BUBBLE-1 recorded as its
   Correction 3, and it has not been fixed since. `ROLE.md`'s allowed-path list does not include the
   repo root. Acceptance criterion 8 asks for "a `DECISION-LOG.md` section"; I have written it below
   for someone with the grant to paste, and every number in it is also in this report and in
   `docs/levels/bubble-test-bubbles.md`, which this role does own.
4. **"There are two towers by height: the blue tower (summit 41.2 m) and the tangle spire
   (60.789 m)"** — both figures check out (`Bubble_Blue_15` is authored at y 41.9, just over the
   blue summit), and the packet was right to make me find the bubble before believing either. Noted
   only because the buried bubble is at the tangle spire's **foot**, not on either tower's shaft, so
   a search anchored on "halfway up a tower" would have looked in the wrong band.

## A near-miss worth recording, because it nearly cost more than the bug

While staging the positive control I ran the file edit through PowerShell's `[IO.File]::WriteAllText`
with a **relative** path after a `cd`. **`[System.IO.File]` resolves against .NET's current
directory, which PowerShell's `cd`/`Set-Location` does not update** — so the two `.tscn` edits landed
in **`C:\repos\Watis-playtest`, Talon's live playtest worktree**, not in mine. I caught it on the
next `git status`, confirmed the diff was exactly my two lines and nothing else, and restored both
files to their committed bytes; `git status` in that worktree is clean and no other file was
touched. `dotnet build` and `git` were unaffected (they honour PowerShell's location). **The rule
this earns: in PowerShell, .NET file APIs take absolute paths, always** — the same class of trap as
the `Get-Content`/`Set-Content` encoding one already in `.claude/rules/imports-and-encoding.md`.

---

## The bible check

```
Bibles applied:  LEVEL — this is placement and nothing else: one collectible moved, no route
                 added or removed, no geometry touched.
                 MECHANICS — the bubbles are a counted, networked, resettable tally with a
                 shared id space, so whether the objective can be resolved at all is in scope,
                 and so is whether the id space survived the edit.
                 Not BEHAVIOR — nothing placed moves or acts. Not INTERACTION — the pickup
                 contract is untouched; the bubble is popped by walking into it exactly as
                 before. Not THRILL — no affect change was directed and none was invented.

Items checked:   LEVEL-BIBLE §62 anti_unwinnable_guarantee (required, not optional; the same
                 answer as MECHANICS §10.5) — THE LOAD-BEARING ONE, and it was FAILING in the
                 shipped build. "Collect all the bubbles" is the level's one objective, so a
                 bubble no body can touch is an unwinnable state, and the level had one. It is
                 now (a) fixed and (b) MEASURED for all 100 bubbles every run with three
                 controls that must fire first, rather than asserted. The remaining gap — route
                 reachability, as opposed to embedding — is stated in §3 rather than papered
                 over, because the bible's own §4 note says the failure mode to avoid is
                 "we were sealed in and nobody could tell why", and a check that overclaimed
                 would reproduce exactly that.
                 LEVEL-BIBLE §4 path_topology — UNCHANGED. Nothing a player walks on was added
                 or removed. The one bubble that moved stayed on the same route (the tangle
                 stair, between treads 19 and 20) and kept its place in the climb's rising
                 order, so the section still reads as the stair its twelve bubbles were placed
                 to mark. The topology fact this packet turned up is a pre-existing one — W7-1's
                 spire skirt occupies the airspace over treads 19 and 20 — and it constrained
                 the placement rather than being changed by it.
                 LEVEL-BIBLE §composition — the tangle keeps twelve, keeps ten of them on the
                 stair, keeps its lead-in and its summit. Density unchanged in every section.
                 MECHANICS-BIBLE the id space — ids are the index of an ordinal node-path sort,
                 so a `position` edit cannot renumber anything, and the sweep above confirms it
                 by reading the ids back off the live adoption pass: Tangle_10 is id 82 before
                 and after. Run-BubbleSyncTest, which asserts every peer adopts the same id
                 space, passes unchanged.
                 MECHANICS-BIBLE §4 idempotency — untouched. No pop path was edited.

Result:          PASS, and it is a pass that had to be earned rather than declared: the
                 anti-unwinnable item was genuinely violated in the build Talon played, the
                 violation is fixed, and the class of violation now fails a suite.
```

---

## For `DECISION-LOG.md` — paste as one section (see Correction 3)

```
## BUBBLE-2 — the bubble inside the tangle spire, and the check that missed it (2026-09-04)

Talon, off a live playtest and immediately before a Steam upload: "one single bubble is out of
access to the player ... it is being clipped into one of the towers ... I could not reach the
bubble therefore I could not successfully conclude that all the bubbles were reachable."

THE BUBBLE. Bubble_Tangle_10, id 82, scenes/game/world/bubbletest/sections/Tangle.tscn.
  before  local (1.401, 20.499, 14.052)  world (76.401, 20.499, -65.948)  INSIDE Tower/Jumble01
  after   local (0.151, 20.999, 14.052)  world (75.151, 20.999, -65.948)  0.75 m free radius
  id 82 both sides; no constant, no count and no bake changed.

CAUSE. BT-11 placed it over stair tread 19 at +1.93 m, on the pattern all twelve tangle bubbles
follow (0.02-0.06 m in xz of their tread, +1.77 to +1.94 m up), and it was legal. W7-1 then built
the spire and its skirt block Jumble01 (2.04 x 3.15 x 4.03 m) landed on that tread's airspace. A
column scan at the bubble's xz: solid from y 17.0 to y 23.4. Nothing re-measured it, so it shipped.

THE CHECK. BubbleTestSelfTest.CheckBubbleEmbedding, under --bubbletest-selftest, sweeps ALL 100
bubbles against the live collision space every run. Three tests: a 0.35 m contact query (reported,
not asserted); a 0.02 m point probe at the centre -- THE DISCRIMINATOR, since a bubble resting near
a surface has a free centre and one clipped into a wall does not; and a search for an avatar-capsule
placement that reaches the 0.35 m collider sphere and is clear of the world.

Three controls fire before any bubble is believed: a golden cube's centre must read EMBEDDED, a
point 0.45 m from that same cube must read FREE (the exact legal geometry BUBBLE-1 shipped -- if
this one ever fires the check has become a proximity test), and a sealed shell with a free centre
must read ENCLOSED. Proved failable: two healthy bubbles planted in geometry in two sections were
both named with id and node path and turned the suite red, then reverted.

WHAT IT DOES NOT COVER: route reachability. ScriptedGotoIntentSource walks to one point and latches,
and bots cannot spawn in sealed rooms, so no multi-leg route can be driven. Gravity is not modelled
(deliberate -- half the level's bubbles are reached by a jump). "All bubbles reachable" is backed by:
100/100 not embedded and not enclosed; 25/100 also floor + standing spot + (19 of them) a swept
straight walk from arrival; 6 lab bubbles on PuffinLabSelfTest's station route. None by a driven bot.

To re-run: godot --headless --path . -- --bubbletest-selftest, and read the "embed:" summary line.
```

---

## Raw counts

**xUnit — baseline measured in a throwaway detached worktree** (`C:/repos/Watis-b2base`, at
`playtest/2026-09-04-combined`, `1f4d688`), then the tip:

```
baseline: Passed!  - Failed: 0, Passed: 2288, Skipped: 0, Total: 2288 - SailNet.Tests.dll (net8.0)
tip:      Passed!  - Failed: 0, Passed: 2288, Skipped: 0, Total: 2288 - SailNet.Tests.dll (net8.0)
```

Identical. Nothing in `tests/unit/` was edited; `TheBubbleSplitAddsUpToTheTarget` and
`CyanCarriesTheDensestBubbles` pass unmodified because no constant moved.

**Scene marathon** — `tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`, redirected to a file, read
from the run's own `=== summary ===` block with the anchored pattern `^  .+ (PASS|FAIL)$`:

```
48 rows, 46 PASS, 2 FAIL.  OVERALL: FAIL
```

**Both reds are load flakes, and both were already on `.claude/rules/test-suite.md`'s
known-load-sensitive list — now MEASURED rather than carried forward unverified.** Discriminated by
re-running each standalone **3x on an idle machine** (`Get-Process` showed zero Godot and zero
dotnet processes before they started) and comparing the RAW quantity, not the verdict:

| Suite | Marathon (loaded) | Standalone, idle, 3x | Quantity compared |
|---|---|---|---|
| `World: run driver` | FAIL — 2.7358796 s apart (tolerance 1.5 s) | **PASS, PASS, PASS** — 0.00093 s, 0.00139 s, 0.00069 s | post-reset cross-view phase divergence |
| `World: tidal-cycle phase` | FAIL — 10 assertions, 1.5244–2.0975 s (tolerance 1.5 s) | **PASS, PASS, PASS** — all 147 matched pairs within 1.5 s; late-join 0.098 / 0.557 / 0.590 s; rejoin 0.00056 / 0.00028 / 0.509 s | cross-view / late-join / rejoin phase divergence |

Three orders of magnitude between the loaded and the idle measurement on the run driver. Both
suites compare two live clients' phase clocks against a fixed 1.5 s tolerance, which is the shape
that turns machine load into a red. **This packet touches no netcode, no cycle clock, no run spine
and no avatar lifecycle** — it adds a self-test check and moves one bubble's `position`.

The measurement is written into `.claude/rules/test-suite.md` under the grant in that file's own
header ("write access granted to all who want it" — Talon, 2026-08-28), which `ROLE.md` records for
this role as *measured entries only*. **One thing I got wrong and recorded as such:** I did not
check what else was on the machine *while* the marathon ran, only afterwards, so the loaded row's
context is "unknown, STEAM-1 was live that day" rather than a measurement. Record it during the
run, not after.

`World: bubble test (BT-0)` — the suite carrying the new check — is **PASS** in the marathon and
standalone.

**And note the rule's own warning applies here in both directions:** the scene suite is not
deterministically green under load, so two reds are not a regression *and* a full house would not
have been proof of anything.

**`dotnet build WatisWorld.sln`**: `0 Error(s)`, 4 pre-existing warnings (`AvatarClipDirector.cs:250`
CS0618, `AvatarVisual.cs:2074/2090` CS8604, `SandboxCamera.cs:970` CS8602) — the same four
BUBBLE-1 reported, none in a file this packet touched.

**`CheckBake` on the edited section**, packed vs live, from the tip's self-test:

```
Tangle         packed[mesh=197 shape=196 body=184] live[mesh=197 shape=196 body=184] OK
```

All seven sections `OK`; a `position` edit changes no node count, so nothing needed re-baking.

**Adoption:** `[bubble] adopted 100 bubble(s) (server=True)`, and the census agrees:
`census TOTAL 100 bubbles (want 100; lab present, 10)`.

---

## Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | `dotnet build WatisWorld.sln` 0 errors | **PASS** |
| 2 | Full probe output for all 100, offenders by id/path/position | **PASS** — §1 |
| 3 | Offender moved, before/after quoted, id unchanged | **PASS** — §2, id 82 both sides |
| 4 | New check under `--bubbletest-selftest`, positive control shown and reverted | **PASS** — §3 |
| 5 | Adopted count exactly 100 | **PASS** — `[bubble] adopted 100 bubble(s) (server=True)` |
| 6 | `Run-BubbleTestWorldTest` passes, CheckBake equal on the edited section; `Run-BubbleSyncTest` passes | **PASS** — both re-run standalone on the tip after the marathon: `BUBBLE-TEST WORLD TEST OVERALL: PASS` with all seven sections' `packed[...] live[...] OK` including `Tangle packed[mesh=197 shape=196 body=184] live[mesh=197 shape=196 body=184] OK`; `BUBBLE SYNC TEST OVERALL: PASS` ("one server-adjudicated tally, identical on three peers including a late joiner") |
| 7 | Headed captures in `docs/qa/BUBBLE-2/`, before and after, same camera, `--capture-cam` | **PASS** — 4 PNGs, two cameras × two runs, `docs/qa/BUBBLE-2/README.md` |
| 8 | Both suite commands, raw counts, reds discriminated; own xUnit baseline in a throwaway detached worktree; report with corrections, bible check, remaining-gap paragraph, DECISION-LOG section | **PASS** — xUnit 2288/2288 on a `1f4d688` detached baseline and on the tip, identical; marathon 48 rows / 46 PASS / 2 FAIL with both reds discriminated as load flakes by 3x standalone re-runs on an idle machine and the measurement written into `.claude/rules/test-suite.md`; this report carries all four required sections |

---

## Files changed

| Path | Why |
|---|---|
| `scripts/game/world/bubbletest/BubbleTestSelfTest.cs` | `CheckBubbleEmbedding` + its three controls, the shell staged in `Run()`, `OverlapNames` generalised from `CapsuleShape3D` to `Shape3D` |
| `scenes/game/world/bubbletest/sections/Tangle.tscn` | one `position`, and a comment saying what was there and why it moved |
| `tools/dev/bubble2_capture.ps1` | the before/after capture, two fixed cameras |
| `docs/qa/BUBBLE-2/**` | 4 PNGs + README |
| `docs/levels/bubble-test-bubbles.md` | the stale coordinate, and the note that a clearance measured once is not a clearance that stays true |
| `.claude/rules/test-suite.md` | the measured flake entry for the two reds — **measured entries only**, per that file's own header grant and `ROLE.md`'s record of it |
| `docs/agents/roles/environment/outbox/2026-09-04-BUBBLE-2-report.md` | this |

Every path is inside this role's `ROLE.md` grant. Staged by path; no `git add -A`.

## Open questions, ripe

1. **`ScriptedGotoIntentSource`'s single-leg limitation is now blocking two packets** — BUBBLE-1
   named it, BUBBLE-2 hits the same wall from the outdoor side. Until a bot can be driven along a
   multi-leg route (and spawned somewhere other than the hub), "every bubble is reachable" cannot be
   demonstrated end-to-end by anything but a human playing. That is a programming change to
   `scripts/game/sandbox/`, and it is the single highest-value test-infrastructure item left on this
   level.
2. **Nothing re-measures a placement after later geometry lands near it.** This packet's check now
   covers bubbles. The same class of defect is open for every other placed thing in the level —
   props, spawn markers, the reset lever, the golden cubes — and `CheckPropHeights` only asks
   whether a prop is *on the ground*, not whether something was later built through it.
