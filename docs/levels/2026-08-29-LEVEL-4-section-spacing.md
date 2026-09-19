---
type: finding
packet: LEVEL-4
role: environment
date: 2026-08-29
branch: feat/2026-08-29-level4
base: 36be9d56
closes: Talon's note 8 — option D approved by the orchestrator 2026-08-29 and BUILT
---

# Note 8, measured: the reading holds, the prescribed mechanism does not

> **RESOLVED 2026-08-29.** The orchestrator approved **option D** below and it is built. The
> mechanism swap was its call to make: the *goal* is Talon's, the mechanism was the packet's, and
> D is strictly more conservative than what the packet originally authorised. §4 records what
> shipped, including the one arm that had to be dropped after a closer look. Everything above §4
> is the measurement that led there and is left exactly as it was written.

> Talon, 2026-08-29: *"Please make each section overall slightly closer together. everything
> is a big too spread apart."*

The LEVEL-4 packet asked me to check its own interpretation before acting on it, and to stop
rather than invent a change if the geometry did not support it. It half does, so this file
says which half and by how much.

**The reading is right.** Note 8 is about the seven level sections, not about UI spacing. The
level is a 300 m cross whose arms are anchored 95–110 m out, and the packet's instinct —
"travel distance in a movement-and-exploration playtest" — is the correct reading of the
complaint.

**The prescribed mechanism is not available.** "Pull the anchors in, do not rescale the
sections" buys at most 5 m on one arm and 10 m on another, costs two of the four hub
connector corridors to get it, and is blocked outright on the other three arms. Below is the
measurement, then what the spread is actually made of, then the fork.

---

## 1. The sections already tile the plan. They are not spread apart; they are adjacent.

Every section's ground plate is a single box centred on its anchor and exactly the size of its
footprint — measured off the shipped `.tscn` files, not off program §4:

| Section | Plate (world) | Size | Plate node |
|---|---|---|---|
| Hub | x −40..40, z −40..40 | 80 × 80 | `Ground/Mesh`, `BoxMesh` 80 × 0.5 × 80 |
| RedCairns | x −45..45, z −145..−45 | 90 × 100 | `BoxMesh` 90 × 0.5 × 100 |
| BluePrecision | x 45..145, z −50..50 | 100 × 100 | `BoxMesh` 100 × 0.5 × 100 |
| CyanRun | x −70..70, z 50..150 | 140 × 100 | `BoxMesh` 140 × 0.5 × 100 |
| GreenHills | x −140..−50, z −50..50 | 90 × 100 | `terrain.res` heightfield |
| Tangle | x 45..105, z −110..−50 | 60 × 60 | `BoxMesh` 60 × 2 × 60 |

The gaps between them are **5 to 10 m**, and every one of them is a hub connector corridor:

| Seam | Width | What is in it |
|---|---|---|
| Hub → Red | 5 m (z −45..−40) | `Hub.tscn` `Connectors/ToRed`, a 12 × 5 slab |
| Hub → Blue | 5 m (x 40..45) | `Connectors/ToBlue`, 5 × 12 |
| Hub → Cyan | 10 m (z 40..50) | `Connectors/ToCyan`, 12 × 10 |
| Hub → Green | 10 m (x −50..−40) | `Connectors/ToGreen`, 10 × 12 |
| Red ↔ Blue | **0** | they touch at x = 45 |
| Tangle ↔ Red | **0** | they touch at x = 45 |
| Tangle ↔ Blue | **0** | they touch at z = −50 |

`BubbleTestLayoutTests.HubConnectorsTouchTheirSectionAndOverlapNoFootprint` asserts each
corridor's far edge touches its section's near edge **exactly** — no gap, no overrun. So
pulling an anchor in by *d* shortens that corridor by *d*, one for one, and pulling it in by
the whole seam deletes the corridor. The corridors are §4 rule 4 and the orchestrator's own
ruling on BT-5's escalation; they are also the four things that point a player at the four
cardinal arms from the plaza.

### What each arm can actually move

- **Red: at most 5 m.** Closes the north corridor.
- **Cyan: at most 10 m.** Closes the south corridor.
- **Blue: 0 m.** Blue's west edge (x = 45) *is* red's east edge. Moving blue in by any *d*
  re-creates an overlap of x[45−d, 45] × z[−50, −45+d] — literally the 5 × 5 m corner overlap
  BT-0 corrected and `NoTwoSectionFootprintsOverlapInPlan` was written to catch.
- **Tangle: 0 m.** Its west edge is red's east edge and its south edge is blue's north edge.
  Both touch exactly; the layout's own doc comment says every metre outward costs sight of
  the pile and every metre inward is illegal.
- **Green: 0 m, and this one is a hard block, not a tight fit.** `GreenFootprint`'s east edge
  is pinned at x = −50 by the **water contract**: `TheGreenSectionIsEntirelyWestOfTheLakeLimit`
  asserts `green.MaxX <= LakeMaxX` (−50), and `LakeMaxX < WaterGeometry.ShoreX` (−45) is the
  x below which the shipped `DepthAt()` will call a submerged point "lake". Moving green east
  re-opens the unmerged per-world water field that D4 exists to avoid needing — and
  `WaterGeometry.cs` is programming's file, not the environment role's.

**Total available under the packet's own rule: two arms, 5 m and 10 m, at the cost of two
corridors.** On a 95 m arm that is 5%. It is not something a player perceives, and I am not
willing to spend the level's four cardinal signposts on it.

---

## 2. What the spread is actually made of

Measured off the shipped scenes with parent transforms accumulated (the ground plate,
the hub's connectors and green's terrain excluded, because those *are* the plate):

| Section | Plate | Content | Bare plate on the hub side |
|---|---|---|---|
| Hub | x −40..40, z −40..40 | x −45..43, z −44.5..47.5 | — (the connectors reach past it, by design) |
| RedCairns | z −145..−45 | z −130.8..**−77.0** | **32.0 m** |
| BluePrecision | x 45..145 | x **76.2**..124.8 | **31.2 m** |
| CyanRun | z 50..150 | z 55.0..145.0 | 5.0 m |
| GreenHills | x −140..−50 | x −126.0..**−60.0** | 10.0 m |
| Tangle | x 45..105 | x 57.0..92.5 | 12.0 m |

**Red and blue each have about 31 metres of empty grey plate between the hub's connector and
their first prop**, and their content sits pushed toward the FAR end of a 100 m plate. Cyan is
full end to end. That asymmetry is what "everything is a bit too spread apart" is made of: you
leave the plaza, cross a 5 m corridor, and then sprint for eight seconds over nothing before
the level starts again.

Moving the anchors cannot touch that. The anchors are where the *plates* are, and the plates
are already touching.

---

## 3. The fork, and what I recommend

**A — do nothing.** Note 8 stays open. Honest, and cheap.

**B — close the seams anyway.** Red −5, cyan −10, and delete their two corridors. Blue,
tangle and green unchanged. Below perception, asymmetric, and it spends two signposts.
I recommend against it.

**C — shrink the plates and pull every arm in ~20 m.** The coherent version of what the
packet asked for. It needs red's plate narrowed in x before blue can move at all, and it is
**blocked on green by the water contract** unless `WaterGeometry` gains a per-world lake —
which is programming's file and a separate packet. Six scene files, the layout, the volumes
and the connectors. This is the "whole re-tune" the packet warned about, and it is not a
LEVEL-4-shaped change.

**D — move each arm's CONTENT toward the hub inside its own plate. My recommendation.**
Translate every top-level child of a section except its ground plate by one constant vector
along the hub axis. Nothing else moves:

| Section | Shift | Bare plate on the hub side, after | Content's far margin, after |
|---|---|---|---|
| RedCairns | +16 m in z (south) | 32.0 → 16.0 m | 14.2 → 30.2 m |
| BluePrecision | −16 m in x (west) | 31.2 → 15.2 m | 20.2 → 36.2 m |
| GreenHills | +5 m in x (east) | 10.0 → 5.0 m | 14.0 → 19.0 m |
| CyanRun, Tangle, Hub | none — already full | | |

Why this is the bounded option and not a re-tune:

- **A rigid translation preserves every tuned traversal exactly.** BT-1..5 authored their
  jumps, gaps and step heights *relative to each other*, not relative to the plate edge. A
  constant vector applied to a whole section changes no distance inside it.
- **No anchor, no footprint, no volume and no connector changes.** The layout contract is
  untouched, so every test in `BubbleTestLayoutTests` is unaffected.
- **Bubble ids survive**, and that is checkable rather than hopeful: the shift edits each
  node's `position`/`transform` in place, so no Bubble is renamed or re-parented, and
  `BubbleCounter.AdoptAuthoredBubbles` hands out ids as the ordinal index of the sorted set of
  Bubble node *paths*. (Verified for this branch's work already: the path set is byte-identical
  to 36be9d56.)
- **It stays inside the footprint**, with room. Red's content would sit at local z −19.8..34,
  in a plate spanning ±50; blue's at local x −34.8..13.8, in a plate spanning ±50.
- It is reversible by negating one vector per section.

The cost is real and should be said: the empty plate does not disappear, it moves to the far
end of each arm — which is the right place for it, because a player only crosses the far end
if they choose to. And 16 m is a *value*, not a direction; it is half of red's dead run, which
is my reading of "slightly".

**D is a single pass and I can execute it the moment it is ruled.** I did not execute it
unruled, because substituting a different mechanism for the one the packet named is a change
of direction, not a change of value, and the packet said to stop rather than build on a guess.
