# Bubble Test — the hundred bubbles

> **SUPERSEDED IN PART, 2026-09-04 (BUBBLE-1).** Talon cut the level to **100** bubbles and moved
> some of them indoors, so **every count and every id below is stale**: cyan is 18 not 33, red 11,
> blue 11, green 11, the hub 7, the seam 5, the TV room 15, and the puffin lab — which is not in
> this document at all — carries 10. The current numbers, the before/after and the reasons are in
> [`2026-09-04-BUBBLE-1-hundred-bubbles.md`](2026-09-04-BUBBLE-1-hundred-bubbles.md), and they are
> now *measured* every run by `BubbleTestSelfTest.CheckBubbleCensus` rather than maintained here by
> hand.
>
> **What in this document is still true and still binding** is its REASONING — why the tangle pays
> only for its climb, why cyan cannot hide a bubble, the 0.50 m clearance bound, and the difficulty
> split's intent. BUBBLE-1's cuts were made against it, not around it. Read this for why; read the
> dated file for how many.


BT-11. One hundred `scenes/game/props/Bubble.tscn` instances, hand-placed into the six section
scenes as children of a `Bubbles` `Node3D` in each file. **No geometry was moved to make a hiding
place** — where the level had no cover, the bubble is honest about it and sits in the open.

## Ids are derived, never authored

`BubbleCounter.AdoptAuthoredBubbles` collects every `Bubble` in the tree, sorts by **node path
with an ordinal compare**, and hands out `0 .. N−1`. So the id column below is a *consequence* of
two things and nothing else:

- the section root names in `BubbleTest.tscn` sort `BluePrecision < CyanRun < GreenHills < Hub <
  RedCairns < TvRoom`;
- inside a file, the two-digit suffix sorts `_01 … _33` correctly *because it is zero-padded*.

**Rename a section root, drop the padding, or insert a bubble in the middle of a file and the
whole level renumbers.** Append at the end of a section's `Bubbles` node and only that section's
tail moves. The ids in this table are asserted against the live `BubbleCounter` by
`tools/dev/bubbletest_check_bubbles.gd`; that script's `TABLE` is this table in machine form and
the two are kept in step by hand.

## Counts, against program D8

| Section | D8 | Placed | Δ | Why |
|---|---|---|---|---|
| CyanRun | 35 | 33 | −2 | See "cyan cannot hide a bubble" below. |
| RedCairns | 15 | 16 | +1 | The main cairn is the only cave-like space in the level; it absorbed one of cyan's. |
| GreenHills | 15 | 16 | +1 | The postpile and the lake shallows likewise. Five of green's sixteen are on or beside the stepping stones, per D8. |
| BluePrecision | 15 | 15 | 0 | |
| Hub | 10 + 7 seam | 10 + 7 | 0 | The seven seam bubbles live in `Hub.tscn` because the connectors do (program §4 rule 4). |
| Tangle | 12 | 12 | 0 | Added by TANGLE-1 with the seventh section; see "the tangle pays for its climb and nothing else" below. |
| TvRoom | 3 | 3 | 0 | |
| **Total** | **112** | **112** | | |

Difficulty split: **54 easy / 44 medium / 14 hidden** against D8's "roughly half obvious, a third
medium, the rest well hidden". Fourteen hidden rather than fifteen because the fifteenth candidate
did not survive measurement — see "the stones have no underside lip" below. The tangle's twelve
arrive entirely as *medium* bar one, which is what moved the split from 53/33/14: a bubble you can
only reach by climbing twenty-three treads is not easy, and none of the twelve is hidden, because
a bubble tucked inside that pile is not hidden — it is gone.

### The tangle pays for its climb and nothing else

The zone's brief is "many routes, none of them marked", and bubbles are the one thing in this
level that reads as a marker. A scatter through the pile would draw the route back in: the player
would follow the bubbles instead of choosing, which is exactly the menu the tangle exists to
withhold. So **ten of the twelve sit on the stair** — the one line the zone already promises — one
sits on the ground at the hub-facing corner as the lead-in, and one is the summit reward. The
spine, the stack, the 46 rubble blocks and the three slabs pay nothing. They are the maybes.

Ten rather than eleven treads because three of the obvious treads could not hold one: measured
in-engine, the bubble over tread 13, 16 and 19 sat 0.196–0.427 m from a neighbouring block, inside
the 0.50 m clearance bound (collider 0.35 + bob 0.15). The whole stair was scanned tread by tread
rather than nudging the three by eye — `tools/dev/tangle_stair_check.gd` prints the clearance for
every candidate — and the ten survivors are the ones with room. Worst shipped clearance in the
section: **0.588 m**, at `Bubble_Tangle_04`.

**And the bound is not a one-time measurement — BUBBLE-2, 2026-09-04.** Every one of those ten
clearances was true when it was measured and stayed true only until something else was built. W7-1
added the spire, its skirt landed on treads 19 and 20, and `Bubble_Tangle_10` went from 0.5 m of
air to being *inside* `Tower/Jumble01` — through a green suite every run, because nothing measured
it again. That is now measured every run:
`BubbleTestSelfTest.CheckBubbleEmbedding` sweeps **all 100** bubbles against the live collision
world. A later packet that builds over a bubble gets a red test instead of a player who cannot
finish the level.

### Cyan cannot hide a bubble, and that is a geometry fact

Cyan is 140 × 100 m of flat ground whose tallest object is a 1.71 m ramp; the walls are 0.40 m
kerbs and the boxes are 0.6–1.0 m. Nothing in it can put a 0.9 m sphere out of sight from more
than one approach. Two candidate "hidden" spots were placed in the picnic sets, measured, and
**withdrawn** — they were visible from every approach and would have made the hidden class a
label rather than a fact. The two bubbles moved to the red cairn's undercroft and the green
postpile, which genuinely hide things. Cyan still keeps the heaviest concentration by a wide
margin — 33 of 100 on 29 % of the level's ground — which is what Talon asked for.

### The stepping stones have no underside lip

The packet names "on a stepping stone's underside lip" as an example hiding place. It does not
exist: `SteppingStones/Stone0..7` are solid boxes running from the lake bed to their tops (e.g.
`Stone5` spans y −3.865 … +0.345), so there is nothing to tuck a bubble under. Nor is there a
usable slot *between* stones — the gaps are 1.35–1.75 m centre to centre against a 0.95 m stone,
which leaves at most 0.80 m of face-to-face gap and cannot hold a 0.5 m clearance on both sides.
The bubble that was going to live there (`Bubble_Green_05`) is instead a **medium** standing in
the shallows off the last stone, and the level ships 14 hidden rather than 15. Labelling it
hidden anyway would have been the cheap answer.

## The rules every row satisfies

- **Clearance ≥ 0.5 m** from the nearest collider surface, measured at the authored centre by a
  binary-searched sphere query against the live physics space. 0.5 m is not arbitrary: the
  collider is 0.35 m (`Bubble.ColliderRadiusM`) and the idle bob is bounded at 0.15 m
  (`BubbleOscillation.MaxOffsetM`), so it is exactly the distance at which a bubble at full bob
  still cannot reach the geometry it is tucked behind. **Measured minimum: 0.542 m** (id 63).
- **Reachable: 0.6–2.2 m above a standable spot within 1.4 m.** Read as reachability rather than
  as a ray straight down, because a bubble hung deliberately between two ladder rungs has the
  blue section's ground 15 m beneath it and is still one step away. A jog-tap apex is 0.467 m and
  a sprint apex 1.534 m (MOVE-3e), so nothing sits higher than a jump can carry. **Measured
  maximum: 1.85 m** (id 82). Two placements failed this check when it was first run and were
  moved.
- **Every hidden bubble has a sighting point**: a walkable point within 6 m from which an
  unobstructed ray reaches the bubble's centre. Furthest is 5.01 m. The sighting point's own
  floor is checked too — a point hanging in mid-air would prove nothing.
- **Nothing inside geometry, nothing pop-able through a wall.** The second follows from the
  first plus the 0.35 m collider being smaller than the 0.45 m visual (program §6.11): the
  visible part may peek past a corner, the part that pops cannot.

## The table

Id order == node-path order == `BubbleCounter`'s ids. Positions are **world** coordinates; the
`.tscn` files store them local to their section anchor.

| id | node | file | class | world (x, y, z) | where | sighting point (hidden only) |
|---|---|---|---|---|---|---|
| 0 | `Bubble_Blue_01` | BluePrecision | easy | (110.49, 1.45, -22.12) | over the first mesa - the tower's front door | — |
| 1 | `Bubble_Blue_02` | BluePrecision | easy | (102.91, 2.45, -24.14) | over the first block of the approach | — |
| 2 | `Bubble_Blue_03` | BluePrecision | easy | (95.53, 3.45, -23.79) | over the second mesa | — |
| 3 | `Bubble_Blue_04` | BluePrecision | hidden | (93, 1.3, -28.9) | behind the fallen branch at the tower's foot | (91.6, 1.2, -28.2) |
| 4 | `Bubble_Blue_05` | BluePrecision | medium | (80.6, 6.45, -12.39) | over the west block, off the shortest line | — |
| 5 | `Bubble_Blue_06` | BluePrecision | easy | (85.42, 8.65, -3.49) | over the first spiral platform | — |
| 6 | `Bubble_Blue_07` | BluePrecision | easy | (96.77, 10.65, -10.05) | over the third spiral platform | — |
| 7 | `Bubble_Blue_08` | BluePrecision | medium | (104.81, 11.65, 2.8) | over the narrow tier-2 stepping platform | — |
| 8 | `Bubble_Blue_09` | BluePrecision | hidden | (109.43, 11.65, -3.08) | over the tier-2 dead-end detour, off the spiral | (104.8, 11.6, -5) |
| 9 | `Bubble_Blue_10` | BluePrecision | easy | (93.95, 13.65, 10.15) | over the north spiral platform | — |
| 10 | `Bubble_Blue_11` | BluePrecision | medium | (87.2, 15.15, -13.4) | in the air between two tier-2 ladder rungs | — |
| 11 | `Bubble_Blue_12` | BluePrecision | hidden | (108.47, 18.7, -26.52) | on the far post at the end of the balance beam | (107.03, 18.2, -23.68) |
| 12 | `Bubble_Blue_13` | BluePrecision | hidden | (109.94, 19.6, 0.93) | on the tier-3 dead-end detour, one platform off the spiral | (108.4, 19.2, 0.93) |
| 13 | `Bubble_Blue_14` | BluePrecision | medium | (86.09, 37.65, 4.97) | over the last west platform below the summit | — |
| 14 | `Bubble_Blue_15` | BluePrecision | easy | (95, 41.9, 0) | over the summit block - the prize at the top | — |
| 15 | `Bubble_Cyan_01` | CyanRun | easy | (-30, 1.1, 55) | over the sprint lane's 0 m strip | — |
| 16 | `Bubble_Cyan_02` | CyanRun | easy | (-30, 1.1, 65) | over the 10 m strip | — |
| 17 | `Bubble_Cyan_03` | CyanRun | easy | (-30, 1.1, 75) | over the 20 m strip | — |
| 18 | `Bubble_Cyan_04` | CyanRun | easy | (-30, 1.1, 85) | over the 30 m strip | — |
| 19 | `Bubble_Cyan_05` | CyanRun | easy | (-30, 1.1, 95) | over the 40 m strip | — |
| 20 | `Bubble_Cyan_06` | CyanRun | easy | (-30, 1.1, 105) | over the 50 m strip | — |
| 21 | `Bubble_Cyan_07` | CyanRun | medium | (-30, 1.65, 115) | over the 60 m strip at head height - a jog-tap at speed | — |
| 22 | `Bubble_Cyan_08` | CyanRun | easy | (30, 1.1, 64) | in the first slalom gate | — |
| 23 | `Bubble_Cyan_09` | CyanRun | easy | (30, 1.1, 80) | in the third slalom gate | — |
| 24 | `Bubble_Cyan_10` | CyanRun | easy | (30, 1.1, 96) | in the fifth slalom gate | — |
| 25 | `Bubble_Cyan_11` | CyanRun | medium | (30, 1.55, 112) | above the seventh slalom gate | — |
| 26 | `Bubble_Cyan_12` | CyanRun | medium | (30, 1.5, 124) | past the last post, off the slalom's exit line | — |
| 27 | `Bubble_Cyan_13` | CyanRun | easy | (0, 1.1, 141) | on the run-in to the goal block | — |
| 28 | `Bubble_Cyan_14` | CyanRun | easy | (-3.5, 1.1, 145) | beside the goal block, west face | — |
| 29 | `Bubble_Cyan_15` | CyanRun | medium | (0, 2.55, 145) | on top of the goal block - stand on it and take it | — |
| 30 | `Bubble_Cyan_16` | CyanRun | easy | (-58, 1.25, 60) | over the low box at the west end | — |
| 31 | `Bubble_Cyan_17` | CyanRun | medium | (-46, 1, 68.6) | on the blind side of the west kerb wall | — |
| 32 | `Bubble_Cyan_18` | CyanRun | medium | (-63, 1.9, 78) | above the west ramp's crest | — |
| 33 | `Bubble_Cyan_19` | CyanRun | easy | (-50, 1.35, 84) | over the west box | — |
| 34 | `Bubble_Cyan_20` | CyanRun | medium | (-61.7, 0.95, 95) | against the west rock's off-path flank | — |
| 35 | `Bubble_Cyan_21` | CyanRun | easy | (-44, 1.4, 102) | over the box on the west return | — |
| 36 | `Bubble_Cyan_22` | CyanRun | medium | (-57, 1, 114.7) | behind the long north-west wall | — |
| 37 | `Bubble_Cyan_23` | CyanRun | easy | (-62, 1.55, 134) | over the far north-west box | — |
| 38 | `Bubble_Cyan_24` | CyanRun | easy | (-48, 1.85, 143) | above the north-west ramp | — |
| 39 | `Bubble_Cyan_25` | CyanRun | easy | (-16, 1.6, 56) | over the tall mid box | — |
| 40 | `Bubble_Cyan_26` | CyanRun | medium | (-12, 1.95, 72) | above the mid ramp | — |
| 41 | `Bubble_Cyan_27` | CyanRun | medium | (-22, 1, 92.6) | in the lee of the mid wall | — |
| 42 | `Bubble_Cyan_28` | CyanRun | easy | (-14, 1.3, 106) | over the mid box | — |
| 43 | `Bubble_Cyan_29` | CyanRun | easy | (-20, 1.5, 122) | over the mid rock | — |
| 44 | `Bubble_Cyan_30` | CyanRun | easy | (14, 1.45, 58) | over the first east box | — |
| 45 | `Bubble_Cyan_31` | CyanRun | medium | (20, 1, 74.9) | behind the east wall, hidden from the hub approach | — |
| 46 | `Bubble_Cyan_32` | CyanRun | easy | (48, 1.6, 62) | over the far east box | — |
| 47 | `Bubble_Cyan_33` | CyanRun | medium | (60, 1.7, 80) | above the east rock | — |
| 48 | `Bubble_Green_01` | GreenHills | easy | (-113, 1.1, 0) | over the first stepping stone | — |
| 49 | `Bubble_Green_02` | GreenHills | easy | (-110.15, 1.15, 0.05) | over the third stepping stone | — |
| 50 | `Bubble_Green_03` | GreenHills | easy | (-106.6, 1.2, -0.3) | over the fifth stepping stone | — |
| 51 | `Bubble_Green_04` | GreenHills | medium | (-103.35, 1.18, 0.6) | over the seventh stone, mid-crossing | — |
| 52 | `Bubble_Green_05` | GreenHills | medium | (-101.95, 0.1, 1.3) | beside the last stepping stone, standing in the shallows at the crossing's east end | — |
| 53 | `Bubble_Green_06` | GreenHills | hidden | (-95.07, 4.15, 0.36) | in the cup of the postpile's tallest column | (-95.78, 3.45, -1.27) |
| 54 | `Bubble_Green_07` | GreenHills | medium | (-97.33, 3.15, -0.7) | over a high postpile column | — |
| 55 | `Bubble_Green_08` | GreenHills | medium | (-92.02, 2.8, -0.21) | over an east postpile column | — |
| 56 | `Bubble_Green_09` | GreenHills | easy | (-99.5, 2, 2.25) | over a low west postpile column | — |
| 57 | `Bubble_Green_10` | GreenHills | hidden | (-96.5, 1.6, 5.6) | down the slot between the postpile's north columns | (-96.28, 1.97, 4) |
| 58 | `Bubble_Green_11` | GreenHills | easy | (-126, 5.25, -32) | on the north-west ridge | — |
| 59 | `Bubble_Green_12` | GreenHills | easy | (-60, 5, 0) | on the ridge that greets you off the hub path | — |
| 60 | `Bubble_Green_13` | GreenHills | medium | (-120, 4.25, -24) | in the saddle on the west shoulder | — |
| 61 | `Bubble_Green_14` | GreenHills | medium | (-108, 5.4, -40) | on the north knoll above the lake | — |
| 62 | `Bubble_Green_15` | GreenHills | hidden | (-93.9, 1.6, -5.4) | in the slot on the postpile's south face | (-95.39, 1.56, -5) |
| 63 | `Bubble_Green_16` | GreenHills | hidden | (-100, -0.65, -14) | half-sunk in the shallows off the lake's north shore | (-104, 0.74, -14) |
| 64 | `Bubble_Hub_01` | Hub | easy | (2.6, 1.1, -8) | beside the counter plinth - the first bubble anyone sees | — |
| 65 | `Bubble_Hub_02` | Hub | easy | (0, 1.1, -18) | south of the seating ring | — |
| 66 | `Bubble_Hub_03` | Hub | easy | (6.47, 1, -5.32) | over the ring's east bench | — |
| 67 | `Bubble_Hub_04` | Hub | easy | (-6.47, 1, -10.68) | over the ring's west bench | — |
| 68 | `Bubble_Hub_05` | Hub | easy | (18, 1.05, 15.6) | over the east seating cluster | — |
| 69 | `Bubble_Hub_06` | Hub | medium | (-18, 1.05, 15.6) | over the west seating cluster, out of the spawn's eyeline | — |
| 70 | `Bubble_Hub_07` | Hub | medium | (8, 1.3, -39.4) | behind the red signpost, on the side you never look at | — |
| 71 | `Bubble_Hub_08` | Hub | medium | (-38.5, 1.3, 8.9) | behind the green signpost | — |
| 72 | `Bubble_Hub_09` | Hub | easy | (30, 1.1, -30) | out on the open north-east apron | — |
| 73 | `Bubble_Hub_10` | Hub | easy | (-30, 1.65, 30) | out on the open south-west apron, at head height | — |
| 74 | `Bubble_Seam_01` | Hub | easy | (0, 1.1, -43) | midway across the red seam | — |
| 75 | `Bubble_Seam_02` | Hub | **the last one** | **(0, 201.05, -15.9)** | **MOVED by ROOFTOP-1, 2026-09-05: on the end of the sky deck's diving board, 200 m over the hub.** Was (3.5, 1.45, -44.5), off the red path's east verge. A relocation, not an addition — the level's total is still exactly 100, the node is still in `Hub.tscn` under `Bubbles`, and its live id is unchanged (**48** in the shipped build; the 75 in this column is BT-11's pre-BUBBLE-1 numbering and the id printed by `CheckBubbleCensus` on every run is the authority). Reachable by walking to the tip of the board: the plank is at y = 200.000, the collider's underside at 200.700, and a standing crown reaches 201.241 — 0.541 m of margin, measured live by `BubbleTestSelfTest.CheckRooftopRoute`. | — |
| 76 | `Bubble_Seam_03` | Hub | easy | (43, 1.1, 0) | midway across the blue seam | — |
| 77 | `Bubble_Seam_04` | Hub | medium | (43, 1.6, 3) | over the blue path's north verge, at head height | — |
| 78 | `Bubble_Seam_05` | Hub | easy | (0, 1.1, 45) | midway across the wide cyan seam | — |
| 79 | `Bubble_Seam_06` | Hub | medium | (-4.5, 1.35, 47.5) | against the cyan path's west kerb | — |
| 80 | `Bubble_Seam_07` | Hub | easy | (-45, 1.1, 0) | midway across the green seam | — |
| 81 | `Bubble_Red_01` | RedCairns | easy | (-29.91, 6.95, -83.4) | over the small cairn's summit block | — |
| 82 | `Bubble_Red_02` | RedCairns | easy | (-32.68, 1.85, -82.31) | on the small cairn's third step | — |
| 83 | `Bubble_Red_03` | RedCairns | easy | (26.21, 12.35, -102.1) | over the middle cairn's summit | — |
| 84 | `Bubble_Red_04` | RedCairns | easy | (28.42, 3.4, -108.09) | on the middle cairn's third step | — |
| 85 | `Bubble_Red_05` | RedCairns | easy | (-14.5, 1.6, -88.99) | over the west rubble boulder | — |
| 86 | `Bubble_Red_06` | RedCairns | easy | (15.98, 1.75, -81.01) | over the east rubble boulder | — |
| 87 | `Bubble_Red_07` | RedCairns | easy | (35, 1.8, -108) | over the far east boulder | — |
| 88 | `Bubble_Red_08` | RedCairns | hidden | (0, 1.8, -84.37) | under the rubble shelf on the cairn's south flank | (2, 1.3, -84.37) |
| 89 | `Bubble_Red_09` | RedCairns | medium | (12.74, 2.35, -83.65) | over the main cairn's first tread | — |
| 90 | `Bubble_Red_10` | RedCairns | medium | (-6.37, 10.55, -92.81) | over the tenth tread, halfway up | — |
| 91 | `Bubble_Red_11` | RedCairns | medium | (-0.16, 23.6, -94.26) | over the main cairn's summit block - the top of the tallest climb | — |
| 92 | `Bubble_Red_12` | RedCairns | medium | (31.52, 5.7, -100.66) | on the middle cairn's north skirt, away from the climb | — |
| 93 | `Bubble_Red_13` | RedCairns | hidden | (0, 1.6, -100) | in the hollow at the heart of the cairn, under the stair | (0, 1.3, -98) |
| 94 | `Bubble_Red_14` | RedCairns | hidden | (-10, 1.4, -100) | deep in the cairn's west undercroft | (-8, 1.3, -99.5) |
| 95 | `Bubble_Red_15` | RedCairns | hidden | (3, 1.6, -99) | in the cairn's hollow, under the thirteenth tread | (1.6, 1.3, -98.6) |
| 96 | `Bubble_Red_16` | RedCairns | hidden | (5, 1.6, -101) | east of the cairn's hollow, under the fourteenth tread | (3.2, 1.3, -100.6) |
| 97 | `Bubble_Tangle_01` | Tangle | easy | (57, 1.2, -62) | on the ground at the section's hub-facing corner — the lead-in, and the only bubble here that is not on the pile | — |
| 98 | `Bubble_Tangle_02` | Tangle | medium | (84.608, 2.35, -61.126) | over the stair's first tread | — |
| 99 | `Bubble_Tangle_03` | Tangle | medium | (79.219, 4.702, -58.667) | over the third tread | — |
| 100 | `Bubble_Tangle_04` | Tangle | medium | (73.301, 6.504, -58.915) | over the fifth tread | — |
| 101 | `Bubble_Tangle_05` | Tangle | medium | (68.251, 8.41, -62.007) | over the seventh tread | — |
| 102 | `Bubble_Tangle_06` | Tangle | medium | (65.572, 10.227, -67.286) | over the ninth tread | — |
| 103 | `Bubble_Tangle_07` | Tangle | medium | (66.369, 12.03, -73.146) | over the eleventh tread | — |
| 104 | `Bubble_Tangle_08` | Tangle | medium | (76.516, 15.85, -77.351) | over the fifteenth tread, on the far side of the pile | — |
| 105 | `Bubble_Tangle_09` | Tangle | medium | (80.544, 18.719, -70) | over the eighteenth tread | — |
| 106 | `Bubble_Tangle_10` | Tangle | medium | ~~(76.401, 20.499, -65.948)~~ **(75.151, 20.999, -65.948)** | over the twentieth tread → **on the stair line between treads 19 and 20** | **BUBBLE-2, 2026-09-04.** W7-1's spire dropped `Tower/Jumble01` on this tread's airspace and the bubble ended up inside the block — centre in solid, 100% of its collider shell buried, unreachable in the shipped playtest. Moved 1.35 m; the 0.50 m clearance bound below is honoured with 0.75 m of free radius. |
| 107 | `Bubble_Tangle_11` | Tangle | medium | (73.408, 21.334, -66.409) | over the twenty-first tread, one below the summit | — |
| 108 | `Bubble_Tangle_12` | Tangle | medium | (71.709, 24.036, -71.729) | THE SUMMIT REWARD — 2.0 m over the last tread, the only thing in this section worth the climb | — |
| 109 | `Bubble_Tv_01` | TvRoom | easy | (0, -18.6, -1.6) | over the couch, facing the screen | — |
| 110 | `Bubble_Tv_02` | TvRoom | medium | (4.5, -19, -4.5) | in the corner behind the couch | — |
| 111 | `Bubble_Tv_03` | TvRoom | hidden | (-5.2, -19.4, 5.2) | in the far corner behind the lamp stand | (-3, -18.8, 3.5) |

---

## Verification

```
godot --headless --path . --script res://tools/dev/bubbletest_check_bubbles.gd
```

Instantiates the real `BubbleTest.tscn`, lets `BubbleTestWorld` build its real `BubbleCounter`,
and **reads the ids off the live nodes** rather than recomputing the sort — a GDScript
re-implementation would only prove this file agrees with itself. It also re-measures every clearance, every
sighting point and every bubble's reach. A run that aborts on a script error cannot print PASS: the
last check is a completion sentinel.

Its checks were confirmed to bite by a positive control (2026-08-28): renaming one table row,
raising the clearance bound to 0.6 m and dropping the sight bound to 1 m each produced the
matching failures.

`tools/dev/bubbletest_bubble_probe.gd` is the design instrument the placements were built with —
`inventory` dumps every collider in the assembled level as a world-space AABB, `probe` reports
ground height and clearance for a candidate list, `sight` tests a sighting point.
