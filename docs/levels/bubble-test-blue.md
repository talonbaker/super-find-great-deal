# Bubble Test -- the blue section: the precision tower and the balance beam

**Owner:** BT-3 (environment). **Scene:** `scenes/game/world/bubbletest/sections/BluePrecision.tscn`. **Written by:** `tools/dev/bt3_blue_author.py` -- that script is the single writer of both this file and the scene, and both come out of the same numbers, so the spec and the geometry cannot drift. Re-run it; never hand-edit one of the two.

Anchor (0, 0, 0) local = (95, 0, 0) world (`BubbleTestLayout.BlueAnchor`). Local footprint x -50..50, z -50..50; ceiling y 42 (`BlueMaxHeight`).

## The two lines

Every tier is 8.00 m of climb and offers both of these, in sight of each other from the tier's first pad:

- **The spiral** -- 4.0 m square pads on a 10.2 m ring, 9 of them per tier, rising 1.00 m at a time with 1.3-3.6 m of air between. This is the easy line and it is the long one. **A metre of rise costs 20% of a jump's flat range** (the arc is back down through 1.00 m at 0.795 of its flight), so a spiral step reaches 3.04 m at a held jog and 4.92 m at a sprint: the shorter steps are jog hops and the longest (3.63 m) needs some sprint, with 1.30 m still spare at a full one.
- **The ladder** -- a zig-zag chimney of 1.6 m ledges hung outboard at r 14-16, forked off the tier's +1 m pad. Five step-ups of 1.32-1.45 m, each a full-hold jump with 0.08-0.21 m under the measured 1.534 m apex, and each with under 1.7 m of horizontal gap. That gap is small because rising eats distance and a 1.45 m climb eats most of it: only 62% of a jump's flat range survives it, which is 2.36 m at a held jog. So the ladder is a hold-and-hop, not a run-up -- the difficulty is the height and the 1.6 m landing target, never the distance. It rejoins the spiral a whole tier up.

Each tier also carries one **flat station**: a place where the spiral stops climbing so that a 5.4-5.9 m flat leap is possible at all. (On a monotonically rising helix there is never a second pad at your own height to leap to, so the flat gap band in program section 4 rule 6 has to be authored a place to exist.) The hard line takes the whole gap in one jump; the easy line takes two short hops round an outboard pad that is visible from the takeoff.

## The measured maximum

Sprint-held **6.192 m** flat / **1.534 m** apex; jog-tap **1.71 m** / **0.467 m** (MOVE-3e, measured; `BubbleTestLayout`). No row below exceeds either. Hard rows sit in **5.4-5.9 m flat** or **1.3-1.45 m up**, never both on one jump. Easy rows are <= 4.5 m and <= 1.0 m up.

**Ratio: 24 hard rows to 61 easy** (1 : 2.54).

`dx` is **free air, edge to edge** -- the centre-to-centre horizontal distance minus each box's half-extent projected onto the direction of travel. That is the distance a player actually crosses, and it is what the acceptance criterion recomputes.

## Tier table

### Tier 1 -- the base (y 0 -> 8, transplanted from `Playground.tscn`) -- 0 hard, 8 easy

| from | to | dx (m) | dy (m) | class | line | note |
|---|---|---|---|---|---|---|
| `MesaRamp1` | `Mesa1` | -1.50 | +0.13 | walk | base | walk up the ramp |
| `Mesa1` | `BlockA` | 2.95 | +1.00 | easy | base |  |
| `BlockA` | `Mesa2` | 3.20 | +1.00 | easy | base |  |
| `Mesa2` | `BlockB` | 1.78 | +1.00 | easy | base |  |
| `BlockB` | `BarkLedge1` | 3.40 | +1.00 | easy | base |  |
| `BarkLedge1` | `BlockC` | 2.94 | +1.00 | easy | base |  |
| `BlockC` | `BarkLedge2` | 2.78 | +1.00 | easy | base |  |
| `BarkLedge2` | `BlockD` | 2.72 | +0.20 | easy | base |  |
| `BlockD` | `R00` | 2.04 | +1.00 | easy | base | off the base tier onto the tower |

### Tier 2 (y 8 -> 16) -- 6 hard, 13 easy

| from | to | dx (m) | dy (m) | class | line | note |
|---|---|---|---|---|---|---|
| `R00` | `R01` | 1.34 | +1.00 | easy | easy: the spiral |  |
| `R01` | `R02` | 2.34 | +1.00 | easy | easy: the spiral |  |
| `R02` | `R03` | 1.51 | +1.00 | easy | easy: the spiral |  |
| `R03` | `R04` | 5.65 | +0.00 | **hard** | hard: the long leap | flat leap -- the whole gap at once |
| `R03` | `D04` | 2.61 | +0.00 | easy | easy: round the outside |  |
| `D04` | `R04` | 3.40 | +0.00 | easy | easy: round the outside |  |
| `R04` | `R05` | 2.37 | +1.00 | easy | easy: the spiral |  |
| `R05` | `R06` | 2.12 | +1.00 | easy | easy: the spiral |  |
| `R06` | `R07` | 1.63 | +1.00 | easy | easy: the spiral |  |
| `R07` | `R08` | 1.69 | +1.00 | easy | easy: the spiral |  |
| `R08` | `R09` | 2.18 | +1.00 | easy | easy: the spiral |  |
| `R01` | `L2_1` | 2.20 | +0.00 | easy | hard: the ladder | fork off the spiral |
| `L2_1` | `L2_2` | 1.28 | +1.45 | **hard** | hard: the ladder |  |
| `L2_2` | `L2_3` | 1.49 | +1.44 | **hard** | hard: the ladder |  |
| `L2_3` | `L2_4` | 1.49 | +1.42 | **hard** | hard: the ladder |  |
| `L2_4` | `L2_5` | 1.49 | +1.40 | **hard** | hard: the ladder |  |
| `L2_5` | `L2_6` | 1.49 | +1.38 | **hard** | hard: the ladder |  |
| `L2_6` | `L2_7` | 1.18 | +0.91 | easy | hard: the ladder |  |
| `L2_7` | `R10` | 2.60 | +0.00 | easy | hard: the ladder | rejoin the spiral a whole tier up |

### Tier 3 -- the balance beam (y 16 -> 24) -- 6 hard, 14 easy

| from | to | dx (m) | dy (m) | class | line | note |
|---|---|---|---|---|---|---|
| `R09` | `R10` | 1.44 | +1.00 | easy | easy: the spiral |  |
| `R10` | `R11` | 2.52 | +1.00 | easy | easy: the spiral |  |
| `R11` | `R12` | 1.32 | +1.00 | easy | easy: the spiral |  |
| `R12` | `R13` | 5.65 | +0.00 | **hard** | hard: the long leap | flat leap -- the whole gap at once |
| `R12` | `D13` | 2.35 | +0.00 | easy | easy: round the outside |  |
| `D13` | `R13` | 3.40 | +0.00 | easy | easy: round the outside |  |
| `R13` | `R14` | 2.33 | +1.00 | easy | easy: the spiral |  |
| `R14` | `R15` | 2.96 | +1.00 | easy | easy: the spiral |  |
| `R15` | `R16` | 1.34 | +1.00 | easy | easy: the spiral |  |
| `R16` | `R17` | 2.36 | +1.00 | easy | easy: the spiral |  |
| `R17` | `R18` | 1.66 | +1.00 | easy | easy: the spiral |  |
| `R10` | `L3_1` | 2.57 | +0.00 | easy | hard: the ladder | fork off the spiral |
| `L3_1` | `L3_2` | 1.63 | +1.45 | **hard** | hard: the ladder |  |
| `L3_2` | `L3_3` | 1.70 | +1.44 | **hard** | hard: the ladder |  |
| `L3_3` | `L3_4` | 1.70 | +1.42 | **hard** | hard: the ladder |  |
| `L3_4` | `L3_5` | 1.70 | +1.40 | **hard** | hard: the ladder |  |
| `L3_5` | `L3_6` | 1.70 | +1.38 | **hard** | hard: the ladder |  |
| `L3_6` | `L3_7` | 1.44 | +0.91 | easy | hard: the ladder |  |
| `L3_7` | `R19` | 2.60 | +0.00 | easy | hard: the ladder | rejoin the spiral a whole tier up |
| `R11` | `BeamGate` | 1.76 | +0.00 | easy | hard: the beam | flat hop out to the beam |
| `BeamGate` | `BalanceBeam` | 1.70 | +0.00 | walk | hard: the beam | step onto the beam |
| `BalanceBeam` | `BeamEnd` | 1.62 | +0.00 | walk | hard: the beam | step off the beam |

### Tier 4 (y 24 -> 32) -- 6 hard, 13 easy

| from | to | dx (m) | dy (m) | class | line | note |
|---|---|---|---|---|---|---|
| `R18` | `R19` | 1.90 | +1.00 | easy | easy: the spiral |  |
| `R19` | `R20` | 1.80 | +1.00 | easy | easy: the spiral |  |
| `R20` | `R21` | 1.55 | +1.00 | easy | easy: the spiral |  |
| `R21` | `R22` | 5.65 | +0.00 | **hard** | hard: the long leap | flat leap -- the whole gap at once |
| `R21` | `D22` | 2.38 | +0.00 | easy | easy: round the outside |  |
| `D22` | `R22` | 3.40 | +0.00 | easy | easy: round the outside |  |
| `R22` | `R23` | 2.72 | +1.00 | easy | easy: the spiral |  |
| `R23` | `R24` | 1.88 | +1.00 | easy | easy: the spiral |  |
| `R24` | `R25` | 1.49 | +1.00 | easy | easy: the spiral |  |
| `R25` | `R26` | 2.39 | +1.00 | easy | easy: the spiral |  |
| `R26` | `R27` | 1.51 | +1.00 | easy | easy: the spiral |  |
| `R19` | `L4_1` | 2.02 | +0.00 | easy | hard: the ladder | fork off the spiral |
| `L4_1` | `L4_2` | 1.50 | +1.45 | **hard** | hard: the ladder |  |
| `L4_2` | `L4_3` | 1.39 | +1.44 | **hard** | hard: the ladder |  |
| `L4_3` | `L4_4` | 1.39 | +1.42 | **hard** | hard: the ladder |  |
| `L4_4` | `L4_5` | 1.39 | +1.40 | **hard** | hard: the ladder |  |
| `L4_5` | `L4_6` | 1.39 | +1.38 | **hard** | hard: the ladder |  |
| `L4_6` | `L4_7` | 1.48 | +0.91 | easy | hard: the ladder |  |
| `L4_7` | `R28` | 2.60 | +0.00 | easy | hard: the ladder | rejoin the spiral a whole tier up |

### Tier 5 -- the summit (y 32 -> 40) -- 6 hard, 13 easy

| from | to | dx (m) | dy (m) | class | line | note |
|---|---|---|---|---|---|---|
| `R27` | `R28` | 2.96 | +1.00 | easy | easy: the spiral |  |
| `R28` | `R29` | 1.34 | +1.00 | easy | easy: the spiral |  |
| `R29` | `R30` | 2.35 | +1.00 | easy | easy: the spiral |  |
| `R30` | `R31` | 5.65 | +0.00 | **hard** | hard: the long leap | flat leap -- the whole gap at once |
| `R30` | `D31` | 3.11 | +0.00 | easy | easy: round the outside |  |
| `D31` | `R31` | 3.40 | +0.00 | easy | easy: round the outside |  |
| `R31` | `R32` | 3.63 | +1.00 | easy | easy: the spiral |  |
| `R32` | `R33` | 1.34 | +1.00 | easy | easy: the spiral |  |
| `R33` | `R34` | 2.39 | +1.00 | easy | easy: the spiral |  |
| `R34` | `R35` | 1.49 | +1.00 | easy | easy: the spiral |  |
| `R35` | `R36` | 2.55 | +1.00 | easy | easy: the spiral |  |
| `R28` | `L5_1` | 1.64 | +0.00 | easy | hard: the ladder | fork off the spiral |
| `L5_1` | `L5_2` | 1.18 | +1.45 | **hard** | hard: the ladder |  |
| `L5_2` | `L5_3` | 1.17 | +1.43 | **hard** | hard: the ladder |  |
| `L5_3` | `L5_4` | 1.17 | +1.41 | **hard** | hard: the ladder |  |
| `L5_4` | `L5_5` | 1.17 | +1.39 | **hard** | hard: the ladder |  |
| `L5_5` | `L5_6` | 1.49 | +1.32 | **hard** | hard: the ladder |  |
| `L5_6` | `R36` | 2.60 | +0.00 | easy | hard: the ladder | rejoin the top ring pad, level with the deck |
| `R36` | `Core` | 3.04 | +0.00 | easy | summit | step onto the tower's own deck |

## The balance beam

`BalanceBeam` is **9.00 x 0.30 x 0.35 m** (long x tall x wide) with its walking surface at **y = 18.00**, a `BoxMesh` and a matching `BoxShape3D`. It runs radially outward from `BeamGate` to `BeamEnd`, hung over open air: the only thing under it is the tier-1 base tier and the section ground, **18.0 m down**. Two `night_edge_blue` rim strips (0.04 m tall, 0.06 m wide) run its full length along both top edges, which is the whole of its night aid -- D10 allows no light node anywhere in this section.

`BeamEnd` is a deliberate dead end. The way back is the beam again or the fall, and the fall lands you on the base tier at the bottom of the climb -- the section's return (LEVEL-BIBLE section 6) is the fall itself plus the walk back up, never a checkpoint.

## The summit

`Core` is a smooth 6 x 6 column from y 0 to y 40; its top face **is** the summit deck, so the last move is a flat hop onto the tower itself rather than onto a pad beside it. `SummitBlock` is the 2 x 2 x 1.2 m `blue_goal.tres` block on it, top at **y 41.2 m** (ceiling 42). It is the only saturated colour in the section; everything else is `blue_neutral.tres` over `blue_ground.tres` (program section 4 rule 7, "don't overdo it").

The column has no ledge, no lip and no attached geometry between y 0 and the deck, and the spiral never comes closer to it than 3.7 m of free air, so there is no <= 1.45 m ladder around the intended line -- asserted in the writer, and the only surface within an apex of the deck is the last ring pad.

## Counts

- Standable/blocking boxes: **88** (15 in the transplanted base tier, 68 in the tower).
- `MeshInstance3D` **169**, `CollisionShape3D` **89**, `StaticBody3D` **89** -- every mesh is a child of the body that carries its shape, which is what `BubbleTestSelfTest.AuditColliders` checks (program section 4 rule 9). No mesh in this section is in the `no_collider` group.
- Jump rows: **88** (24 hard, 61 easy, 3 walk-on).

## How to change it, and how it is checked

Edit `tools/dev/bt3_blue_author.py` and re-run it (`python tools/dev/bt3_blue_author.py`). It refuses to write anything if a row falls outside the bands, so a bad edit fails at the writer rather than in a playtest.

Then re-derive the same facts FROM the committed scene, which is a different question and catches a different class of bug (a mesh and its collider given different sizes, a `position` that is a pad's top rather than its centre):

```
Godot_v4.7-stable_mono_win64_console.exe --headless --path . \
    --script tools/dev/bt3_blue_verify.gd
```

It reports the peak, every mesh/collider pair, the beam's real dimensions, the absence of any long-AND-high pair anywhere in the section, and a breadth-first search proving the summit deck is reachable from the ground -- and reachable by EASY jumps alone. **That search is not a bot run and is not offered as one:** it ignores run-up and cannot tell a jump needing full sprint from one that does not, so it is evidence the route exists, never that it feels good. There is no jumping bot in this repo to do better with (`ScriptedGotoIntentSource` and `WalkToPointIntentSource` never press Jump).

Captures: `powershell -File tools/dev/bt3_blue_capture.ps1` -- nine framings, day and night, all 1920 x 1080 (program D11), into `docs/qa/bubble-test/BT-3/`.

## LEVEL-BIBLE lines

- **section 1 `macro_structure`** -- hub-and-spoke at the level scale; inside the section it is a **single spine with a parallel** (the spiral and the ladder are two routes over the same 8 m, converging every tier).
- **section 1 `path_topology`** -- parallel, not mesh: the two lines meet only at the tier pads, so a player is always on a route they can name.
- **section 4.3 `route_loss_rule`** -- a fall costs the climb and nothing else. There is no checkpoint, no lost resource and no death; `RespawnService` never fires because the ground is at y 0 and `VoidKillY` is -70.
- **section 6 the return** -- the fall IS the return, and it lands on the pale-blue ground at the foot of the base tier, which is where the climb starts. The walk back is the cost, and it is short on purpose: the section is a movement test, not a punisher.
- **section 2.2 the gradient** -- the only ramp here is height, and it is legible because the pads get smaller as they get higher (4.0 m ring pads, 2.6 m leap pads, 1.6 m ladder ledges) and because the night edge is the only lit thing in the section.

