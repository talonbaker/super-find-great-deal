---
type: finding
packet: LEVEL-1
role: programming
date: 2026-09-04
branch: feat/2026-09-04-level-1-ld-egg-port
base: c16414d
tool: tools/dev/ld3_cadence.py
supersedes: docs/levels/2026-09-02-LD-3-cadence-audit.md (as the CURRENT reading; that file stays as the before)
---

# LEVEL-1 — the cadence re-audit, on the combined tip

`tools/dev/ld3_cadence.py`, re-run on this branch: the six bubble-test sections **with** EGG-2's
four edited section scenes and EGG-1's Puffin Lab present. LD-3's own table
(`docs/levels/2026-09-02-LD-3-cadence-audit.md`) was measured on `Sail` master `583ecc02`, before
EGG-2. This is the same measurement after it.

## The check

```
$ python tools/dev/ld3_cadence.py --check
[ld3-cadence] every waypoint resolves
$ echo $?
0
```

Every waypoint in the tool's table resolves against the ported level — acceptance criterion 6's
first half. No waypoint was orphaned by EGG-2's edits.

## The diff against LD-3's Sail table: **ZERO**

This is the finding, and it is not the one the packet expected. The packet said *"Cyan's numbers
are expected to move; say by how much."* **They moved by nothing.** Measured, not assumed:

```
$ python tools/dev/ld3_cadence.py   > reaudit-now.txt     # this branch, combined tip
$ python tools/dev/ld3_cadence.py   > reaudit-base.txt    # 583ecc02 section scenes, scratch tree
$ diff -u reaudit-base.txt reaudit-now.txt | wc -l
0
```

Both outputs are 437 lines and 36,728 bytes. `sha256(now)[:16] = acf8b25948bff2fe`,
`sha256(base)[:16] = acf8b25948bff2fe` — equal. The baseline run used **this branch's own copy of the
tool** against `583ecc02`'s `BubbleTest.tscn`, the six section `.tscn`s, `BubbleTestLayout.cs` and
`MotorTuning.cs`, assembled into a scratch tree, so the only variable between the two runs is the
level content.

### Why zero, mechanically — the reason, not a shrug

Two independent facts, and both had to hold:

1. **`ld3_cadence.py` measures a fixed waypoint table, not everything in the scene.** A waypoint is
   a named bubble, a named collider's top-centre, a `BubbleTestLayout.cs` constant or a literal —
   see the tool's own `# --- waypoint resolution ---` header. It walks the authored *line*, and
   that line is a hand-authored list of features, not a sweep of the geometry.
2. **EGG-2 is additive everywhere a waypoint lives.** Measured on the source diff
   `583ecc02..825e1087`, counting DELETED lines per section file: `CyanRun` 1 (the `load_steps`
   header), `GreenHills` 1, `TvRoom` 1, `BluePrecision` 8. Only `BluePrecision`'s eight are a real
   removal — the tower's single flat `Ground` plate with its mesh and collider, replaced by four
   `Ground/Apron*` bodies and a `Moat`. Everything else EGG-2 added is new nodes beside the
   existing ones: Cyan's `FinishedRecord` and `Gallery`, Green's and TvRoom's additions, and the
   moat's own bodies.

So EGG-2 moved **no waypoint**, and the one node it did replace — Blue's `Ground` — was never a
waypoint. The tool is honestly reporting that *the authored walking line is unchanged*.

### What that does and does not license

It does **not** say EGG-2 changed nothing about how the level plays. It says the *spacing between
authored features along the measured lines* is unchanged, which is the specific thing LD-3
measured and the specific thing LEVEL-2 will act on. Two changes this tool is constitutionally
unable to see, named here so nobody reads the zero as broader than it is:

- **Blue's tower floor is now water.** The consequence of falling changed completely — from a free
  landing to a drowning — and no gap in metres moved. A cadence audit measures the distance
  between things to reach, never the cost of missing.
- **Cyan's aisle gained a gallery.** The 96 m emptiness is still 96 m of emptiness *to the next
  bubble*; benches and skid marks beside the lane are dressing the waypoint table does not carry.
  Whether dressing counts as cadence is a LEVEL-2 question, and a real one.

## The findings, carried forward unchanged

The three LD-3 findings the packet put out of scope for LEVEL-1 are all still exactly true on this
tip, because the numbers did not move. Raw from this branch's run:

| finding | measured on this tip | LD-3 on `583ecc02` | delta |
|---|---|---|---|
| Cyan, the middle aisle (seam -> `Bubble_Cyan_13`) | 96.0 m / 15.8 s at sprint, flagged `CAP` | 96.0 m / 15.8 s | **0.0 m** |
| Cyan, the west obstacle field (seam -> `Obs_00` box) | 59.9 m / 9.9 s, flagged | 59.9 m / 9.9 s | **0.0 m** |
| Green, seam to the north knoll | 42.8 m / 7.0 s | 42.8 m / 7.0 s | **0.0 m** |
| Blue, seam to the tower's summit (-> `MesaRamp1`) | 42.4 m / 7.0 s | 42.4 m / 7.0 s | **0.0 m** |

Totals, quoted from the run's own footer rather than recounted:

> 156 gaps measured; 23 flagged; 4 over the cap; median gap 2.5 s at the region's speed.

Identical on both sides of the diff.

**Acting on any of these is LEVEL-2, not LEVEL-1.** This document exists so LEVEL-2 starts from a
number measured on the level it is actually going to edit, rather than from one measured on a
level that no longer exists in this repository.

---

## The table, in full, as the tool emitted it on this branch


#### Hub -- north, spawn to the red seam -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | spawn ring centre | (0.0, 0.0, 18.0) | -- | -- | -- | -- | -- | | SpawnRingCentre (0, 0, 18); every session opens here |
| 1 | seating ring, east bench | (2.7, 0.3, -1.5) | 19.7 | 19.7 | 3.2 | 5.2 | approach |  | 0.32 m bench: a held jump, not a tap (TapApex 0.300) |
| 2 | lever + pedestal + Bubble_Hub_01 | (1.5, 0.0, -8.0) | 6.6 | 6.6 | 1.1 | 1.7 | approach |  | the one interaction on the plaza |
| 3 | Bubble_Hub_02 | (0.0, 1.1, -18.0) | 10.2 | 10.1 | 1.7 | 2.7 | approach |  |  |
| 4 | Bubble_Hub_07 (behind the red signpost) | (8.0, 1.3, -39.4) | 22.8 | 22.8 | 3.8 | 6.0 | approach |  | the plaza's north half is empty between these two |
| 5 | Bubble_Seam_01 / _02 (the red seam) | (0.0, 1.1, -43.0) | 8.8 | 8.8 | 1.4 | 2.3 | approach |  | connector ToRed, z -40..-45 |

#### Hub -- north, spawn to the red seam -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_01 / _02 (the red seam) | (0.0, 1.1, -43.0) | -- | -- | -- | -- | -- | | connector ToRed, z -40..-45 |
| 1 | Bubble_Hub_07 (behind the red signpost) | (8.0, 1.3, -39.4) | 8.8 | 8.8 | 1.4 | 2.3 | approach |  | the plaza's north half is empty between these two |
| 2 | Bubble_Hub_02 | (0.0, 1.1, -18.0) | 22.8 | 22.8 | 3.8 | 6.0 | approach |  |  |
| 3 | lever + pedestal + Bubble_Hub_01 | (1.5, 0.0, -8.0) | 10.2 | 10.1 | 1.7 | 2.7 | approach |  | the one interaction on the plaza |
| 4 | seating ring, east bench | (2.7, 0.3, -1.5) | 6.6 | 6.6 | 1.1 | 1.7 | approach |  | 0.32 m bench: a held jump, not a tap (TapApex 0.300) |
| 5 | spawn ring centre | (0.0, 0.0, 18.0) | 19.7 | 19.7 | 3.2 | 5.2 | approach |  | SpawnRingCentre (0, 0, 18); every session opens here |

#### Hub -- east, spawn to the blue seam -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | spawn ring centre | (0.0, 0.0, 18.0) | -- | -- | -- | -- | -- | |  |
| 1 | GoldCube_Hub_0 | (10.0, 0.2, 10.0) | 12.8 | 12.8 | 2.1 | 3.4 | approach |  | carryable |
| 2 | east seating cluster + Bubble_Hub_05 | (18.0, 1.1, 15.6) | 9.8 | 9.8 | 1.6 | 2.6 | approach |  |  |
| 3 | Bubble_Seam_03 / _04 (the blue seam) | (43.0, 1.1, 0.0) | 29.5 | 29.5 | 4.8 | 7.8 | approach |  | connector ToBlue, x 40..45 |

#### Hub -- east, spawn to the blue seam -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_03 / _04 (the blue seam) | (43.0, 1.1, 0.0) | -- | -- | -- | -- | -- | | connector ToBlue, x 40..45 |
| 1 | east seating cluster + Bubble_Hub_05 | (18.0, 1.1, 15.6) | 29.5 | 29.5 | 4.8 | 7.8 | approach |  |  |
| 2 | GoldCube_Hub_0 | (10.0, 0.2, 10.0) | 9.8 | 9.8 | 1.6 | 2.6 | approach |  | carryable |
| 3 | spawn ring centre | (0.0, 0.0, 18.0) | 12.8 | 12.8 | 2.1 | 3.4 | approach |  |  |

#### Hub -- south, spawn to the cyan seam -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | spawn ring centre | (0.0, 0.0, 18.0) | -- | -- | -- | -- | -- | |  |
| 1 | GoldCube_Hub_3 | (-4.0, 0.2, 22.0) | 5.7 | 5.7 | 0.9 | 1.5 | approach |  | behind the ring |
| 2 | Bubble_Seam_05 / _06 (the cyan seam) | (0.0, 1.1, 45.0) | 23.4 | 23.3 | 3.8 | 6.1 | approach |  | connector ToCyan, z 40..50 |

#### Hub -- south, spawn to the cyan seam -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_05 / _06 (the cyan seam) | (0.0, 1.1, 45.0) | -- | -- | -- | -- | -- | | connector ToCyan, z 40..50 |
| 1 | GoldCube_Hub_3 | (-4.0, 0.2, 22.0) | 23.4 | 23.3 | 3.8 | 6.1 | approach |  | behind the ring |
| 2 | spawn ring centre | (0.0, 0.0, 18.0) | 5.7 | 5.7 | 0.9 | 1.5 | approach |  |  |

#### Hub -- west, spawn to the green seam -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | spawn ring centre | (0.0, 0.0, 18.0) | -- | -- | -- | -- | -- | |  |
| 1 | GoldCube_Hub_1 | (-12.0, 0.2, 8.0) | 15.6 | 15.6 | 2.6 | 4.1 | approach |  |  |
| 2 | west seating cluster + Bubble_Hub_06 | (-18.0, 1.1, 15.6) | 9.7 | 9.7 | 1.6 | 2.6 | approach |  |  |
| 3 | HubTv (the grey-level television) | (-22.0, 0.0, -22.0) | 37.8 | 37.8 | 6.2 | 10.0 | approach | FLAG | off the direct line, NW quadrant |
| 4 | Bubble_Hub_08 (behind the green signpost) | (-38.5, 1.3, 8.9) | 35.1 | 35.0 | 5.8 | 9.2 | approach | FLAG |  |
| 5 | Bubble_Seam_07 (the green seam) | (-45.0, 1.1, 0.0) | 11.0 | 11.0 | 1.8 | 2.9 | approach |  | connector ToGreen, x -40..-50 |

#### Hub -- west, spawn to the green seam -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_07 (the green seam) | (-45.0, 1.1, 0.0) | -- | -- | -- | -- | -- | | connector ToGreen, x -40..-50 |
| 1 | Bubble_Hub_08 (behind the green signpost) | (-38.5, 1.3, 8.9) | 11.0 | 11.0 | 1.8 | 2.9 | approach |  |  |
| 2 | HubTv (the grey-level television) | (-22.0, 0.0, -22.0) | 35.1 | 35.0 | 5.8 | 9.2 | approach | FLAG | off the direct line, NW quadrant |
| 3 | west seating cluster + Bubble_Hub_06 | (-18.0, 1.1, 15.6) | 37.8 | 37.8 | 6.2 | 10.0 | approach | FLAG |  |
| 4 | GoldCube_Hub_1 | (-12.0, 0.2, 8.0) | 9.7 | 9.7 | 1.6 | 2.6 | approach |  |  |
| 5 | spawn ring centre | (0.0, 0.0, 18.0) | 15.6 | 15.6 | 2.6 | 4.1 | approach |  |  |

#### Hub -- north-east, spawn to the tangle (no connector) -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | spawn ring centre | (0.0, 0.0, 18.0) | -- | -- | -- | -- | -- | |  |
| 1 | GoldCube_Hub_2 | (16.0, 0.2, -14.0) | 35.8 | 35.8 | 5.9 | 9.4 | approach | FLAG |  |
| 2 | Bubble_Hub_09 (the NE apron) | (30.0, 1.1, -30.0) | 21.3 | 21.3 | 3.5 | 5.6 | approach |  |  |
| 3 | hub plate corner (40, -40) | (40.0, 0.0, -40.0) | 14.2 | 14.1 | 2.3 | 3.7 | approach |  | no connector on this diagonal; the plate ends |
| 4 | GoldCube_Tangle_0 | (49.0, 0.2, -65.0) | 26.6 | 26.6 | 4.4 | 7.0 | approach |  | first thing on the tangle plate |
| 5 | Bubble_Tangle_01 (the lead-in) | (57.0, 1.2, -62.0) | 8.6 | 8.5 | 1.4 | 2.3 | approach |  |  |
| 6 | Stair00 (the tangle stair's foot) | (84.6, 1.1, -61.1) | 27.6 | 27.6 | 4.5 | 7.3 | approach |  | the climb begins |

#### Hub -- north-east, spawn to the tangle (no connector) -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Stair00 (the tangle stair's foot) | (84.6, 1.1, -61.1) | -- | -- | -- | -- | -- | | the climb begins |
| 1 | Bubble_Tangle_01 (the lead-in) | (57.0, 1.2, -62.0) | 27.6 | 27.6 | 4.5 | 7.3 | approach |  |  |
| 2 | GoldCube_Tangle_0 | (49.0, 0.2, -65.0) | 8.6 | 8.5 | 1.4 | 2.3 | approach |  | first thing on the tangle plate |
| 3 | hub plate corner (40, -40) | (40.0, 0.0, -40.0) | 26.6 | 26.6 | 4.4 | 7.0 | approach |  | no connector on this diagonal; the plate ends |
| 4 | Bubble_Hub_09 (the NE apron) | (30.0, 1.1, -30.0) | 14.2 | 14.1 | 2.3 | 3.7 | approach |  |  |
| 5 | GoldCube_Hub_2 | (16.0, 0.2, -14.0) | 21.3 | 21.3 | 3.5 | 5.6 | approach |  |  |
| 6 | spawn ring centre | (0.0, 0.0, 18.0) | 35.8 | 35.8 | 5.9 | 9.4 | approach | FLAG |  |

#### Red -- the seam to the main cairn's summit -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_01 (the seam) | (0.0, 1.1, -43.0) | -- | -- | -- | -- | -- | | z -43 |
| 1 | GoldCube_Red_1 | (12.0, 0.2, -55.0) | 17.0 | 17.0 | 2.8 | 4.5 | approach |  | 12 m east of the axis |
| 2 | Rock_04 (a boulder, 1.0 m) | (-6.0, 1.0, -61.0) | 19.0 | 19.0 | 3.1 | 5.0 | approach |  | the first thing on the axis |
| 3 | Bubble_Red_09 over Tread00 | (12.7, 1.7, -67.7) | 19.9 | 19.9 | 3.3 | 5.2 | approach |  | the stair's foot, 1.69 m top: a held jump |
| 4 | STAIR Tread00 -> Tread22 -> SummitBlock | (12.7, 1.7, -67.7) -> (-0.2, 22.8, -78.3) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 23 treads + the summit block; 23 steps, 0.67-3.21 m (median 3.15), rise 0.67-1.12 m, 0.18-0.84 s per step at jog |
| 5 | RedTv on the perch | (-0.2, 22.8, -79.5) | 1.2 | 1.2 | 0.2 | 0.3 | climb |  | the reward |

#### Red -- the seam to the main cairn's summit -- inbound (a different line)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | RedTv on the perch | (-0.2, 22.8, -79.5) | -- | -- | -- | -- | -- | | 22.81 m up |
| 1 | the drop: TvPerch south lip to the plate | (-0.2, 0.0, -75.8) | 22.8 down | 3.7 | 0.6 + 1.1 fall | 1.0 + 1.1 fall | drop | | step off the south lip; recoverable, the walk back is the cost; fall time at Gravity x FallGravityMultiplier = 36 m/s^2, no terminal velocity |
| 2 | Bubble_Red_08 (under the rubble shelf) | (0.0, 1.8, -68.4) | 7.6 | 7.4 | 1.3 | 2.0 | approach |  | hidden, at the cairn's south flank |
| 3 | Rock_04 | (-6.0, 1.0, -61.0) | 9.5 | 9.5 | 1.6 | 2.5 | approach |  |  |
| 4 | Bubble_Seam_01 | (0.0, 1.1, -43.0) | 19.0 | 19.0 | 3.1 | 5.0 | approach |  |  |

#### Red -- the seam to the easy cairn (west) -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_01 (the seam) | (0.0, 1.1, -43.0) | -- | -- | -- | -- | -- | |  |
| 1 | GoldCube_Red_0 | (-20.0, 0.2, -50.0) | 21.2 | 21.2 | 3.5 | 5.6 | approach |  |  |
| 2 | Rock_07 | (-24.0, 0.7, -64.5) | 15.1 | 15.0 | 2.5 | 4.0 | approach |  |  |
| 3 | Cairn_Easy Step00 | (-27.8, 0.4, -65.4) | 3.9 | 3.9 | 0.6 | 1.0 | approach |  | 0.45 m: a tap |
| 4 | STAIR Step00 -> Step13 -> SummitBlock | (-27.8, 0.4, -65.4) -> (-29.9, 6.3, -67.4) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | jog-taps only; 14 steps, 0.40-2.66 m (median 2.63), rise 0.40-0.42 m, 0.11-0.70 s per step at jog |

#### Red -- the seam to the easy cairn (west) -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | STAIR Step00 -> Step13 -> SummitBlock | (-29.9, 6.3, -67.4) -> (-27.8, 0.4, -65.4) | 0.0 | 0.0 | 0.0 | 0.0 | None |  | jog-taps only; 14 steps, 0.40-2.66 m (median 2.63), rise -0.42--0.40 m, 0.07-0.44 s per step at sprint |
| 1 | Cairn_Easy Step00 | (-27.8, 0.4, -65.4) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 0.45 m: a tap |
| 2 | Rock_07 | (-24.0, 0.7, -64.5) | 3.9 | 3.9 | 0.6 | 1.0 | approach |  |  |
| 3 | GoldCube_Red_0 | (-20.0, 0.2, -50.0) | 15.1 | 15.0 | 2.5 | 4.0 | approach |  |  |
| 4 | Bubble_Seam_01 (the seam) | (0.0, 1.1, -43.0) | 21.2 | 21.2 | 3.5 | 5.6 | approach |  |  |

#### Red -- the seam to the mid cairn (east) -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_01 (the seam) | (0.0, 1.1, -43.0) | -- | -- | -- | -- | -- | |  |
| 1 | GoldCube_Red_2 | (28.0, 0.2, -52.0) | 29.4 | 29.4 | 4.8 | 7.7 | approach |  |  |
| 2 | Bubble_Red_06 over Rock_02 | (16.0, 1.8, -65.0) | 17.8 | 17.7 | 2.9 | 4.7 | approach |  |  |
| 3 | Rock_05 | (19.0, 0.8, -81.0) | 16.3 | 16.3 | 2.7 | 4.3 | approach |  |  |
| 4 | Cairn_Mid Step00 | (22.0, 0.4, -86.6) | 6.3 | 6.3 | 1.0 | 1.7 | approach |  |  |
| 5 | STAIR Step00 -> Step09 -> SummitBlock | (22.0, 0.4, -86.6) -> (26.2, 11.7, -86.1) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 1.15 m rises: held jumps; 10 steps, 0.90-4.81 m (median 4.71), rise 0.90-1.15 m, 0.24-1.26 s per step at jog |

#### Red -- the seam to the mid cairn (east) -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | STAIR Step00 -> Step09 -> SummitBlock | (26.2, 11.7, -86.1) -> (22.0, 0.4, -86.6) | 0.0 | 0.0 | 0.0 | 0.0 | None |  | 1.15 m rises: held jumps; 10 steps, 0.90-4.81 m (median 4.71), rise -1.15--0.90 m, 0.15-0.79 s per step at sprint |
| 1 | Cairn_Mid Step00 | (22.0, 0.4, -86.6) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  |  |
| 2 | Rock_05 | (19.0, 0.8, -81.0) | 6.3 | 6.3 | 1.0 | 1.7 | approach |  |  |
| 3 | Bubble_Red_06 over Rock_02 | (16.0, 1.8, -65.0) | 16.3 | 16.3 | 2.7 | 4.3 | approach |  |  |
| 4 | GoldCube_Red_2 | (28.0, 0.2, -52.0) | 17.8 | 17.7 | 2.9 | 4.7 | approach |  |  |
| 5 | Bubble_Seam_01 (the seam) | (0.0, 1.1, -43.0) | 29.4 | 29.4 | 4.8 | 7.7 | approach |  |  |

#### Blue -- the seam to the tower's summit -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_03 (the seam) | (43.0, 1.1, 0.0) | -- | -- | -- | -- | -- | | x 43 |
| 1 | GoldCube_Blue_1 | (52.0, 0.2, 8.0) | 12.1 | 12.0 | 2.0 | 3.2 | approach |  | 8 m north of the axis |
| 2 | GoldCube_Blue_0 | (55.0, 0.2, -20.0) | 28.2 | 28.2 | 4.6 | 7.4 | approach |  | on the way to the tower's front door |
| 3 | MesaRamp1 (the front door) | (97.0, 0.7, -25.7) | 42.4 | 42.4 | 7.0 | 11.2 | approach | FLAG | the base tier begins; Bubble_Blue_01 over Mesa1 |
| 4 | BASE TIER Mesa1 -> BlockD | (94.5, 0.8, -22.1) -> (63.3, 7.0, -1.9) | 4.4 | 4.4 | 0.7 | 1.2 | climb |  | 8 hops of 1.0 m; 7 steps, 5.12-7.91 m (median 6.55), rise 0.20-1.00 m, 1.35-2.08 s per step at jog |
| 5 | SPIRAL R00 -> R36 -> Core deck | (69.4, 8.0, -3.5) -> (79.0, 40.0, 0.0) | 6.4 | 6.3 | 1.0 | 1.7 | climb |  | 37 pads incl. the four flat-station detours; the ladder and the beam are the hard lines beside it; 41 steps, 6.73-8.20 m (median 7.05), rise 0.00-1.00 m, 1.77-2.16 s per step at jog |
| 6 | BlueTv on the perch | (79.0, 41.2, -1.2) | 1.7 | 1.2 | 0.3 | 0.4 | climb |  | the reward |

#### Blue -- the seam to the tower's summit -- inbound (a different line)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | BlueTv on the perch | (79.0, 41.2, -1.2) | -- | -- | -- | -- | -- | | 41.2 m up |
| 1 | the drop: perch south lip to the plate | (79.0, 0.0, 2.5) | 41.2 down | 3.7 | 0.6 + 1.5 fall | 1.0 + 1.5 fall | drop | | onto the base tier or the plate; recoverable; fall time at Gravity x FallGravityMultiplier = 36 m/s^2, no terminal velocity |
| 2 | GoldCube_Blue_1 | (52.0, 0.2, 8.0) | 27.6 | 27.6 | 4.5 | 7.3 | approach |  |  |
| 3 | Bubble_Seam_03 | (43.0, 1.1, 0.0) | 12.1 | 12.0 | 2.0 | 3.2 | approach |  |  |

#### Cyan -- the ruler lane (west), seam to the 60 m line -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | -- | -- | -- | -- | -- | | z 45 |
| 1 | Bubble_Cyan_25 over Obs_10 box | (-16.0, 1.6, 56.0) | 19.4 | 19.4 | 3.2 | 5.1 | fast | FLAG | the mid band's first box |
| 2 | Bubble_Cyan_01 (0 m strip) | (-30.0, 1.1, 55.0) | 14.0 | 14.0 | 2.3 | 3.7 | fast |  | the lane at x -30 |
| 3 | LANE strips 0 -> 60 m (Cyan_01..07) | (-30.0, 1.1, 55.0) -> (-30.0, 1.6, 115.0) | 0.0 | 0.0 | 0.0 | 0.0 | fast |  | one bubble every 10 m; the last at head height; 6 steps, 10.00-10.02 m (median 10.00), rise 0.00-0.55 m, 1.64-1.65 s per step at sprint |
| 4 | Obs_15 box (far mid band) | (-11.0, 0.7, 138.0) | 29.8 | 29.8 | 4.9 | 7.9 | fast | FLAG |  |
| 5 | Goal block + Cyan_13/14/15 | (0.0, 1.5, 145.0) | 13.1 | 13.0 | 2.1 | 3.4 | fast |  | the saturated 1.5 m cube at the far end |

#### Cyan -- the ruler lane (west), seam to the 60 m line -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Goal block + Cyan_13/14/15 | (0.0, 1.5, 145.0) | -- | -- | -- | -- | -- | | the saturated 1.5 m cube at the far end |
| 1 | Obs_15 box (far mid band) | (-11.0, 0.7, 138.0) | 13.1 | 13.0 | 2.1 | 3.4 | fast |  |  |
| 2 | LANE strips 0 -> 60 m (Cyan_01..07) | (-30.0, 1.6, 115.0) -> (-30.0, 1.1, 55.0) | 29.8 | 29.8 | 4.9 | 7.9 | fast | FLAG | one bubble every 10 m; the last at head height; 6 steps, 10.00-10.02 m (median 10.00), rise -0.55-0.00 m, 1.64-1.65 s per step at sprint |
| 3 | Bubble_Cyan_01 (0 m strip) | (-30.0, 1.1, 55.0) | 0.0 | 0.0 | 0.0 | 0.0 | fast |  | the lane at x -30 |
| 4 | Bubble_Cyan_25 over Obs_10 box | (-16.0, 1.6, 56.0) | 14.0 | 14.0 | 2.3 | 3.7 | fast |  | the mid band's first box |
| 5 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | 19.4 | 19.4 | 3.2 | 5.1 | fast | FLAG | z 45 |

#### Cyan -- the slalom (east), seam to the last post -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | -- | -- | -- | -- | -- | |  |
| 1 | Bubble_Cyan_30 over Obs_16 box | (14.0, 1.4, 58.0) | 19.1 | 19.1 | 3.1 | 5.0 | fast | FLAG |  |
| 2 | Post_0 (slalom gate 1) | (31.5, 2.0, 60.0) | 17.6 | 17.6 | 2.9 | 4.6 | fast |  |  |
| 3 | SLALOM Post_0 -> Post_8 | (31.5, 2.0, 60.0) -> (31.5, 2.0, 124.0) | 0.0 | 0.0 | 0.0 | 0.0 | fast |  | 9 posts every 8 m, 3 m stagger; 8 steps, 8.54-8.54 m (median 8.54), rise 0.00-0.00 m, 1.41-1.41 s per step at sprint |
| 4 | Bubble_Cyan_12 (past the last post) | (30.0, 1.5, 124.0) | 1.6 | 1.5 | 0.3 | 0.4 | fast |  |  |

#### Cyan -- the slalom (east), seam to the last post -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Cyan_12 (past the last post) | (30.0, 1.5, 124.0) | -- | -- | -- | -- | -- | |  |
| 1 | SLALOM Post_0 -> Post_8 | (31.5, 2.0, 124.0) -> (31.5, 2.0, 60.0) | 1.6 | 1.5 | 0.3 | 0.4 | fast |  | 9 posts every 8 m, 3 m stagger; 8 steps, 8.54-8.54 m (median 8.54), rise 0.00-0.00 m, 1.41-1.41 s per step at sprint |
| 2 | Post_0 (slalom gate 1) | (31.5, 2.0, 60.0) | 0.0 | 0.0 | 0.0 | 0.0 | fast |  |  |
| 3 | Bubble_Cyan_30 over Obs_16 box | (14.0, 1.4, 58.0) | 17.6 | 17.6 | 2.9 | 4.6 | fast |  |  |
| 4 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | 19.1 | 19.1 | 3.1 | 5.0 | fast | FLAG |  |

#### Cyan -- the middle aisle, seam to the goal -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | -- | -- | -- | -- | -- | | the 12 m aisle is EMPTY by design (program section 6.12 sightline) |
| 1 | Bubble_Cyan_13 (the run-in) | (0.0, 1.1, 141.0) | 96.0 | 96.0 | 15.8 | 25.3 | fast | CAP | the first feature in the aisle |
| 2 | Goal block | (0.0, 1.5, 145.0) | 4.0 | 4.0 | 0.7 | 1.1 | fast |  |  |

#### Cyan -- the middle aisle, seam to the goal -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Goal block | (0.0, 1.5, 145.0) | -- | -- | -- | -- | -- | |  |
| 1 | Bubble_Cyan_13 (the run-in) | (0.0, 1.1, 141.0) | 4.0 | 4.0 | 0.7 | 1.1 | fast |  | the first feature in the aisle |
| 2 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | 96.0 | 96.0 | 15.8 | 25.3 | fast | CAP | the 12 m aisle is EMPTY by design (program section 6.12 sightline) |

#### Cyan -- the west obstacle field, seam to the far corner -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | -- | -- | -- | -- | -- | |  |
| 1 | Obs_00 box + Cyan_16 | (-58.0, 0.6, 60.0) | 59.9 | 59.9 | 9.9 | 15.8 | fast | CAP | x -58 |
| 2 | Obs_01 wall + Cyan_17 | (-46.0, 0.4, 66.0) | 13.4 | 13.4 | 2.2 | 3.5 | fast |  |  |
| 3 | Obs_02 ramp + Cyan_18 | (-63.0, 0.9, 78.0) | 20.8 | 20.8 | 3.4 | 5.5 | fast | FLAG |  |
| 4 | Obs_03 box + Cyan_19 | (-50.0, 0.7, 84.0) | 14.3 | 14.3 | 2.4 | 3.8 | fast |  |  |
| 5 | Obs_04 rock + Cyan_20 | (-60.0, 1.2, 95.0) | 14.9 | 14.9 | 2.4 | 3.9 | fast |  |  |
| 6 | Obs_05 box + Cyan_21 | (-44.0, 0.8, 102.0) | 17.5 | 17.5 | 2.9 | 4.6 | fast |  |  |
| 7 | Obs_06 wall + Cyan_22 | (-57.0, 0.4, 113.0) | 17.0 | 17.0 | 2.8 | 4.5 | fast |  |  |
| 8 | GoldCube_Cyan_2 | (-52.0, 0.2, 118.0) | 7.1 | 7.1 | 1.2 | 1.9 | fast |  |  |
| 9 | Obs_07 picnic set | (-46.1, 0.7, 125.5) | 9.5 | 9.5 | 1.6 | 2.5 | fast |  |  |
| 10 | Obs_08 box + Cyan_23 | (-62.0, 0.9, 134.0) | 18.1 | 18.1 | 3.0 | 4.8 | fast |  |  |
| 11 | Obs_09 ramp + Cyan_24 | (-48.0, 0.8, 143.0) | 16.6 | 16.6 | 2.7 | 4.4 | fast |  |  |

#### Cyan -- the west obstacle field, seam to the far corner -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Obs_09 ramp + Cyan_24 | (-48.0, 0.8, 143.0) | -- | -- | -- | -- | -- | |  |
| 1 | Obs_08 box + Cyan_23 | (-62.0, 0.9, 134.0) | 16.6 | 16.6 | 2.7 | 4.4 | fast |  |  |
| 2 | Obs_07 picnic set | (-46.1, 0.7, 125.5) | 18.1 | 18.1 | 3.0 | 4.8 | fast |  |  |
| 3 | GoldCube_Cyan_2 | (-52.0, 0.2, 118.0) | 9.5 | 9.5 | 1.6 | 2.5 | fast |  |  |
| 4 | Obs_06 wall + Cyan_22 | (-57.0, 0.4, 113.0) | 7.1 | 7.1 | 1.2 | 1.9 | fast |  |  |
| 5 | Obs_05 box + Cyan_21 | (-44.0, 0.8, 102.0) | 17.0 | 17.0 | 2.8 | 4.5 | fast |  |  |
| 6 | Obs_04 rock + Cyan_20 | (-60.0, 1.2, 95.0) | 17.5 | 17.5 | 2.9 | 4.6 | fast |  |  |
| 7 | Obs_03 box + Cyan_19 | (-50.0, 0.7, 84.0) | 14.9 | 14.9 | 2.4 | 3.9 | fast |  |  |
| 8 | Obs_02 ramp + Cyan_18 | (-63.0, 0.9, 78.0) | 14.3 | 14.3 | 2.4 | 3.8 | fast |  |  |
| 9 | Obs_01 wall + Cyan_17 | (-46.0, 0.4, 66.0) | 20.8 | 20.8 | 3.4 | 5.5 | fast | FLAG |  |
| 10 | Obs_00 box + Cyan_16 | (-58.0, 0.6, 60.0) | 13.4 | 13.4 | 2.2 | 3.5 | fast |  | x -58 |
| 11 | Bubble_Seam_05 (the seam) | (0.0, 1.1, 45.0) | 59.9 | 59.9 | 9.9 | 15.8 | fast | CAP |  |

#### Green -- the seam to the island by the stones -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_07 (the seam) | (-45.0, 1.1, 0.0) | -- | -- | -- | -- | -- | | x -45 |
| 1 | Bubble_Green_12 (the greeting ridge) | (-60.0, 5.0, 0.0) | 15.5 | 15.0 | 2.5 | 4.1 | approach |  | on the hill that greets you off the path; the hills are scenery to the motor (no slope term) |
| 2 | Bubble_Green_08 (east postpile column) | (-92.0, 2.8, -0.2) | 32.1 | 32.0 | 5.3 | 8.4 | approach | FLAG | the island's east side is a beach since STONE-2 |
| 3 | Postpile summit cap + Bubble_Green_06 | (-95.0, 1.1, 0.0) | 3.4 | 3.0 | 0.6 | 0.9 | climb |  | the tallest column, 3.5 m |
| 4 | Stone7 (the crossing's east end) | (-101.9, 0.4, 0.1) | 6.9 | 6.9 | 1.1 | 1.8 | climb |  |  |
| 5 | STONES Stone7 -> Stone0 (west) | (-101.9, 0.4, 0.1) -> (-113.0, 0.3, 0.0) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 8 stones, 1.4-1.9 m centre to centre; 7 steps, 1.42-1.87 m (median 1.55), rise -0.08-0.06 m, 0.37-0.49 s per step at jog |
| 6 | Bubble_Green_13 (the west saddle) | (-120.0, 4.2, -24.0) | 25.3 | 25.0 | 4.2 | 6.7 | approach |  |  |
| 7 | Bubble_Green_11 (the NW ridge) | (-126.0, 5.2, -32.0) | 10.0 | 10.0 | 1.7 | 2.6 | approach |  | the far corner |

#### Green -- the seam to the island by the stones -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Green_11 (the NW ridge) | (-126.0, 5.2, -32.0) | -- | -- | -- | -- | -- | | the far corner |
| 1 | Bubble_Green_13 (the west saddle) | (-120.0, 4.2, -24.0) | 10.0 | 10.0 | 1.7 | 2.6 | approach |  |  |
| 2 | STONES Stone7 -> Stone0 (west) | (-113.0, 0.3, 0.0) -> (-101.9, 0.4, 0.1) | 25.3 | 25.0 | 4.2 | 6.7 | approach |  | 8 stones, 1.4-1.9 m centre to centre; 7 steps, 1.42-1.87 m (median 1.55), rise -0.06-0.08 m, 0.23-0.31 s per step at sprint |
| 3 | Stone7 (the crossing's east end) | (-101.9, 0.4, 0.1) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  |  |
| 4 | Postpile summit cap + Bubble_Green_06 | (-95.0, 1.1, 0.0) | 6.9 | 6.9 | 1.1 | 1.8 | climb |  | the tallest column, 3.5 m |
| 5 | Bubble_Green_08 (east postpile column) | (-92.0, 2.8, -0.2) | 3.4 | 3.0 | 0.6 | 0.9 | climb |  | the island's east side is a beach since STONE-2 |
| 6 | Bubble_Green_12 (the greeting ridge) | (-60.0, 5.0, 0.0) | 32.1 | 32.0 | 5.3 | 8.4 | approach | FLAG | on the hill that greets you off the path; the hills are scenery to the motor (no slope term) |
| 7 | Bubble_Seam_07 (the seam) | (-45.0, 1.1, 0.0) | 15.5 | 15.0 | 2.5 | 4.1 | approach |  | x -45 |

#### Green -- the seam to the north knoll -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Seam_07 (the seam) | (-45.0, 1.1, 0.0) | -- | -- | -- | -- | -- | |  |
| 1 | Bubble_Green_12 (the greeting ridge) | (-60.0, 5.0, 0.0) | 15.5 | 15.0 | 2.5 | 4.1 | approach |  |  |
| 2 | Bubble_Green_16 (half-sunk, north shore) | (-100.0, -0.7, -14.0) | 42.8 | 42.4 | 7.0 | 11.3 | approach | FLAG |  |
| 3 | Bubble_Green_14 (the north knoll) | (-108.0, 5.4, -40.0) | 27.9 | 27.2 | 4.6 | 7.3 | approach |  |  |

#### Green -- the seam to the north knoll -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Bubble_Green_14 (the north knoll) | (-108.0, 5.4, -40.0) | -- | -- | -- | -- | -- | |  |
| 1 | Bubble_Green_16 (half-sunk, north shore) | (-100.0, -0.7, -14.0) | 27.9 | 27.2 | 4.6 | 7.3 | approach |  |  |
| 2 | Bubble_Green_12 (the greeting ridge) | (-60.0, 5.0, 0.0) | 42.8 | 42.4 | 7.0 | 11.3 | approach | FLAG |  |
| 3 | Bubble_Seam_07 (the seam) | (-45.0, 1.1, 0.0) | 15.5 | 15.0 | 2.5 | 4.1 | approach |  |  |

#### Tangle -- the hub-facing corner to the spire's summit -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | GoldCube_Tangle_0 (the plate's SW corner) | (49.0, 0.2, -65.0) | -- | -- | -- | -- | -- | | where the NE diagonal lands |
| 1 | Bubble_Tangle_01 (the lead-in) | (57.0, 1.2, -62.0) | 8.6 | 8.5 | 1.4 | 2.3 | approach |  |  |
| 2 | Stair00 | (84.6, 1.1, -61.1) | 27.6 | 27.6 | 4.5 | 7.3 | approach |  | the stair's foot is on the plate's EAST side; the lead-in is on its west |
| 3 | STAIR Stair00 -> Stair22 | (84.6, 1.1, -61.1) -> (71.7, 22.0, -71.7) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 23 treads, 0.8-1.2 m rises, no plan gap; 22 steps, 3.09-3.21 m (median 3.15), rise 0.79-1.20 m, 0.81-0.85 s per step at jog |
| 4 | Tower/Base (the spire's foot) | (70.6, 23.1, -69.2) | 2.9 | 2.7 | 0.5 | 0.8 | climb |  | flush with the stack summit at 23.1 m |
| 5 | SPIRE Base -> Tw40 -> Summit | (70.6, 23.1, -69.2) -> (70.6, 60.8, -69.2) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 41 treads, 0.84-0.95 m rises, proved to overlap in plan; 42 steps, 1.39-3.31 m (median 2.04), rise 0.84-0.95 m, 0.36-0.87 s per step at jog |
| 6 | TangleTv on the summit cap | (70.6, 60.8, -70.4) | 1.2 | 1.2 | 0.2 | 0.3 | climb |  | the top of the world |

#### Tangle -- the hub-facing corner to the spire's summit -- inbound (a different line)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | TangleTv on the summit cap | (70.6, 60.8, -70.4) | -- | -- | -- | -- | -- | | 60.8 m up |
| 1 | the drop: cap south lip to the plate | (70.6, 0.0, -65.2) | 60.8 down | 5.2 | 0.9 + 1.8 fall | 1.4 + 1.8 fall | drop | | onto the pile or the plate; recoverable; fall time at Gravity x FallGravityMultiplier = 36 m/s^2, no terminal velocity |
| 2 | Bubble_Tangle_01 (the lead-in) | (57.0, 1.2, -62.0) | 14.0 | 14.0 | 2.3 | 3.7 | approach |  |  |
| 3 | GoldCube_Tangle_0 | (49.0, 0.2, -65.0) | 8.6 | 8.5 | 1.4 | 2.3 | approach |  |  |

#### Tangle -- the spur (a dead-end branch off tread 12) -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Tw12 (the branch point) | (66.6, 34.8, -73.4) | -- | -- | -- | -- | -- | | 34.8 m up |
| 1 | SPUR Tw12 -> Spur03 -> SpurPad | (66.6, 34.8, -73.4) -> (60.4, 34.8, -79.9) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | four level blocks out to the pad; 5 steps, 1.44-3.24 m (median 1.44), rise -0.00-0.00 m, 0.38-0.85 s per step at jog |
| 2 | HiddenTv on the spur pad | (60.4, 34.8, -81.1) | 1.2 | 1.2 | 0.2 | 0.3 | climb |  | the reward; the way back is the same four blocks |

#### Tangle -- the spur (a dead-end branch off tread 12) -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | HiddenTv on the spur pad | (60.4, 34.8, -81.1) | -- | -- | -- | -- | -- | | the reward; the way back is the same four blocks |
| 1 | SPUR Tw12 -> Spur03 -> SpurPad | (60.4, 34.8, -79.9) -> (66.6, 34.8, -73.4) | 1.2 | 1.2 | 0.2 | 0.3 | climb |  | four level blocks out to the pad; 5 steps, 1.44-3.24 m (median 1.44), rise -0.00-0.00 m, 0.38-0.85 s per step at jog |
| 2 | Tw12 (the branch point) | (66.6, 34.8, -73.4) | 0.0 | 0.0 | 0.0 | 0.0 | climb |  | 34.8 m up |

#### Tangle -- the far corner (GoldCube_Tangle_1) -- outbound

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Stair00 (the stair's foot) | (84.6, 1.1, -61.1) | -- | -- | -- | -- | -- | |  |
| 1 | Rubble21 (a plate-level block) | (81.9, 2.3, -79.9) | 19.0 | 19.0 | 3.1 | 5.0 | approach |  | 2.3 m top: a climb-on, not a hop |
| 2 | GoldCube_Tangle_1 (the plate's far corner) | (100.0, 0.2, -100.0) | 27.1 | 27.0 | 4.5 | 7.1 | approach |  | (100, -100): 40 m from the stair, nothing between |

#### Tangle -- the far corner (GoldCube_Tangle_1) -- inbound (reversed; same gaps, read the other way)

| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |
|---|---|---|---|---|---|---|---|---|---|
| 0 | GoldCube_Tangle_1 (the plate's far corner) | (100.0, 0.2, -100.0) | -- | -- | -- | -- | -- | | (100, -100): 40 m from the stair, nothing between |
| 1 | Rubble21 (a plate-level block) | (81.9, 2.3, -79.9) | 27.1 | 27.0 | 4.5 | 7.1 | approach |  | 2.3 m top: a climb-on, not a hop |
| 2 | Stair00 (the stair's foot) | (84.6, 1.1, -61.1) | 19.0 | 19.0 | 3.1 | 5.0 | approach |  |  |

### Summary

Speeds: jog 3.80 m/s, sprint 6.08 m/s (MotorTuning.Default MoveSpeed x SprintMultiplier). Thresholds: fast <= 3 s at sprint, approach <= 5 s at sprint, climb <= 5 s at jog, cap 8 s.

| line | flagged gaps | over the 8 s cap |
|---|---|---|
| Hub -- north, spawn to the red seam -- outbound | 0 | 0 |
| Hub -- north, spawn to the red seam -- inbound (reversed; same gaps, read the other way) | 0 | 0 |
| Hub -- east, spawn to the blue seam -- outbound | 0 | 0 |
| Hub -- east, spawn to the blue seam -- inbound (reversed; same gaps, read the other way) | 0 | 0 |
| Hub -- south, spawn to the cyan seam -- outbound | 0 | 0 |
| Hub -- south, spawn to the cyan seam -- inbound (reversed; same gaps, read the other way) | 0 | 0 |
| Hub -- west, spawn to the green seam -- outbound | 2 | 0 |
| Hub -- west, spawn to the green seam -- inbound (reversed; same gaps, read the other way) | 2 | 0 |
| Hub -- north-east, spawn to the tangle (no connector) -- outbound | 1 | 0 |
| Hub -- north-east, spawn to the tangle (no connector) -- inbound (reversed; same gaps, read the other way) | 1 | 0 |
| Red -- the seam to the main cairn's summit -- outbound | 0 | 0 |
| Red -- the seam to the main cairn's summit -- inbound (a different line) | 0 | 0 |
| Red -- the seam to the easy cairn (west) -- outbound | 0 | 0 |
| Red -- the seam to the easy cairn (west) -- inbound (reversed; same gaps, read the other way) | 0 | 0 |
| Red -- the seam to the mid cairn (east) -- outbound | 0 | 0 |
| Red -- the seam to the mid cairn (east) -- inbound (reversed; same gaps, read the other way) | 0 | 0 |
| Blue -- the seam to the tower's summit -- outbound | 1 | 0 |
| Blue -- the seam to the tower's summit -- inbound (a different line) | 0 | 0 |
| Cyan -- the ruler lane (west), seam to the 60 m line -- outbound | 2 | 0 |
| Cyan -- the ruler lane (west), seam to the 60 m line -- inbound (reversed; same gaps, read the other way) | 2 | 0 |
| Cyan -- the slalom (east), seam to the last post -- outbound | 1 | 0 |
| Cyan -- the slalom (east), seam to the last post -- inbound (reversed; same gaps, read the other way) | 1 | 0 |
| Cyan -- the middle aisle, seam to the goal -- outbound | 1 | 1 |
| Cyan -- the middle aisle, seam to the goal -- inbound (reversed; same gaps, read the other way) | 1 | 1 |
| Cyan -- the west obstacle field, seam to the far corner -- outbound | 2 | 1 |
| Cyan -- the west obstacle field, seam to the far corner -- inbound (reversed; same gaps, read the other way) | 2 | 1 |
| Green -- the seam to the island by the stones -- outbound | 1 | 0 |
| Green -- the seam to the island by the stones -- inbound (reversed; same gaps, read the other way) | 1 | 0 |
| Green -- the seam to the north knoll -- outbound | 1 | 0 |
| Green -- the seam to the north knoll -- inbound (reversed; same gaps, read the other way) | 1 | 0 |
| Tangle -- the hub-facing corner to the spire's summit -- outbound | 0 | 0 |
| Tangle -- the hub-facing corner to the spire's summit -- inbound (a different line) | 0 | 0 |
| Tangle -- the spur (a dead-end branch off tread 12) -- outbound | 0 | 0 |
| Tangle -- the spur (a dead-end branch off tread 12) -- inbound (reversed; same gaps, read the other way) | 0 | 0 |
| Tangle -- the far corner (GoldCube_Tangle_1) -- outbound | 0 | 0 |
| Tangle -- the far corner (GoldCube_Tangle_1) -- inbound (reversed; same gaps, read the other way) | 0 | 0 |

156 gaps measured; 23 flagged; 4 over the cap; median gap 2.5 s at the region's speed.

Worst gaps, at the region's own speed:

| s | m | region | line | gap |
|---|---|---|---|---|
| 15.8 | 96.0 | fast | Cyan -- the middle aisle, seam to the goal | -> Bubble_Cyan_13 (the run-in) |
| 15.8 | 96.0 | fast | Cyan -- the middle aisle, seam to the goal | -> Bubble_Seam_05 (the seam) |
| 9.9 | 59.9 | fast | Cyan -- the west obstacle field, seam to the far corner | -> Obs_00 box + Cyan_16 |
| 9.9 | 59.9 | fast | Cyan -- the west obstacle field, seam to the far corner | -> Bubble_Seam_05 (the seam) |
| 7.0 | 42.8 | approach | Green -- the seam to the north knoll | -> Bubble_Green_16 (half-sunk, north shore) |
| 7.0 | 42.8 | approach | Green -- the seam to the north knoll | -> Bubble_Green_12 (the greeting ridge) |
| 7.0 | 42.4 | approach | Blue -- the seam to the tower's summit | -> MesaRamp1 (the front door) |
| 6.2 | 37.8 | approach | Hub -- west, spawn to the green seam | -> HubTv (the grey-level television) |
| 6.2 | 37.8 | approach | Hub -- west, spawn to the green seam | -> west seating cluster + Bubble_Hub_06 |
| 5.9 | 35.8 | approach | Hub -- north-east, spawn to the tangle (no connector) | -> GoldCube_Hub_2 |
| 5.9 | 35.8 | approach | Hub -- north-east, spawn to the tangle (no connector) | -> spawn ring centre |
| 5.8 | 35.1 | approach | Hub -- west, spawn to the green seam | -> Bubble_Hub_08 (behind the green signpost) |
