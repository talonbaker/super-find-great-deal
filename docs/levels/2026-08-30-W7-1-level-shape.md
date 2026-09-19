---
type: level-note
packet: W7-1
world: bubbletest
date: 2026-08-30
summary: The spawn ring backs 18 m, the tangle grows a 60.8 m spire, and four of five televisions move onto summits as the reward for climbing.
---

# W7-1 — the spawn view, the towers, and the TVs as a reward for climbing

Talon's 2026-08-30 playtest notes 1, 8 and 10, read as one intention: **the level should have a
top, and getting up there should pay.**

Numbers here are the ones the level actually holds. Anything derived is named with its source, and
nothing in this file is the authority for anything — `BubbleTestLayout.cs` is, and
`tools/dev/tangle_tower.py` prints the spire's own numbers on every run.

---

## 1. The spawn (note 1)

| | Before | After |
|---|---|---|
| Ring centre, hub-local | (0, 0, 0) | **(0, 0, 18)** |
| `Spawn0` (the first player's) | (0, 0, −6) | (0, 0, 12) |
| `Spawn3` (the furthest back) | (0, 0, 6) | (0, 0, 24) |
| Nearest marker → pedestal | 2 m | 20 m |
| Furthest marker → pedestal | 14 m | 32 m |
| Clearance to the hub plate's south edge (z = 40) | 34 m | 16 m |

**Back is +Z and that is not a choice.** A fresh spawn and a teleport both leave
`MoveState.Yaw` at 0, which is −Z, so every player in this world opens their session looking
north up the red arm. Backing the ring along that axis is what puts more of the level in front of
the camera. Lifting it would have answered a different request.

**Why 18.** It is a value, picked and stated. The ring used to straddle the origin with its
nearest marker 2 m from the counter pedestal, so the counter filled the frame. At +18 the whole
80 m plaza, both seating clusters and all four connectors are in shot, the counter is still
legible, and the ring stands 16 m clear of the plate's south edge with nothing authored between it
and the south connector. Further back reaches the edge; nearer gives the frame back. The
before/after pair in `docs/qa/W7-1/` is the evidence.

**The ring's shape did not move — only its centre.** `BubbleTestLayout.SpawnPosition(i)` still
returns a point on a 6 m circle and still means exactly what it meant;
`BubbleTestLayout.SpawnPointOf(i)` is the composed marker position. Keeping the two apart is what
lets `BubbleTestLayoutTests.SixSpawnsSitOnTheRingAndNoneCoincide` keep proving the ring's shape
while the placement moves.

**Six marker rotations were recomputed**, because `BubbleTestSelfTest.CheckSpawns` asserts every
marker faces the pedestal at (0, 0, −8) to within 0.06°. From +18 the flanking markers turn
inward instead of outward: `Spawn1`/`Spawn5` now yaw ±0.222 rad rather than ±0.805, and
`Spawn2`/`Spawn4` ±0.177 rather than ±0.441.

**`RespawnPoint` did NOT move**, deliberately. It is the hub centre (0, 1, 0), it is what
`RespawnService` returns a drowned or off-map player to, and note 1 is about the opening frame
rather than about recovery. Moving it would have been a second, unasked change to a value four
tests read. Flagged in the report rather than done.

**`BubbleTestWorld.HubReturn` DID move**, and it moved by being derived instead of typed. It was
the literal (0, 1, 3) with the stated reason "inside the spawn ring"; that reason was true only
while the ring straddled the origin. It is now `SpawnRingCentre + (0, 1, 3)`, which keeps BT-10's
direction rather than BT-10's coordinate.

---

## 2. The tall jumble (note 10)

**The level held exactly two block jumbles** and the note asks for one of them to become the tall
one:

| Jumble | Was | Now |
|---|---|---|
| Red's cairns (`Cairn_Main`) | 23.05 m | **23.05 m — untouched, deliberately** |
| The tangle's pile | 23.09 m | **60.79 m** |

**Why the tangle and not red.** It is the jumble by name and by brief ("many routes, none of them
marked"), so growing it undoes none of BT-2's authored cairn composition; it sits on the NE
diagonal, framed by the gap between the north and east arms, which is the one section a player at
the hub spawn sees without an arm standing in front of it — so notes 1 and 10 pay each other; and
it is the least curated section in the level.

### What was authored

`tools/dev/tangle_tower.py` writes one `Tower` node into `Tangle.tscn`, 76 blocks, from a fixed
seed. Re-running it reproduces the file byte for byte.

| Piece | Count | Top (section-local) | Notes |
|---|---|---|---|
| `Base` | 1 | 23.100 m | a 7 m cap flush with the shipped `Stack/StackB19` summit (23.088 m), so the spire starts exactly where the shipped climb ends and adds no step at the seam |
| `Tw00`..`Tw40` | 41 | 24.0 → 59.9 m | a spindle spiral: radius swells 3.2 → 7.0 m at the waist and closes to 3.2 m at the top. Every block yawed at random, plan extents 2.6–3.6 m, heights 1.1–2.6 m |
| `Spur00`..`Spur03` + `SpurPad` | 5 | 34.782 m | a level four-block branch off tread 12 out to a 6 m dead-end platform. Reaching it costs the detour and the climb back |
| `Summit` | 1 | **60.789 m** | an 8 m cap on the axis |
| `Jumble00`..`Jumble27` | 28 | below their neighbouring tread | decorative mass hung off the flanks. They carry collision like everything else but never sit on a tread, so they add silhouette and never raise a step |

Summit, world space: **(70.593, 60.789, −69.242)**. Spur pad: **(60.383, 34.782, −79.866)**.
Plan reach from the anchor: 22.74 m against the section's 30 m half-extent.

### The climb is guaranteed, and the guarantee is geometric

Two properties, both asserted by the generator before it writes a byte, both exiting non-zero on
failure:

1. **Every step rises at most 0.946 m** (band 0.84–0.95), against `MotorArc.HeldSprint`'s apex of
   **1.407 m**. That is under the ceiling of a fully-held jump taken from a standstill, so no
   run-up and no air jump is required anywhere on the spire.
2. **Consecutive blocks always overlap in plan.** A yawed box presents at least `min(sx, sz) / 2`
   in any direction, so the generator checks, pair by pair, that the distance between consecutive
   centres is strictly less than the sum of those two half-widths. The tightest pair on the shipped
   spire leaves **0.574 m of overlap**. No yaw can open a gap that does not exist at the pair's
   narrowest, so every step is a pure vertical step-up with no gap to clear.

This is the same idiom the shipped tangle stair already uses (0.93 m rises, treads touching), which
is why the spire needs no movement change. **MOVE-8's clip-speed fork is untouched.**

### Contract changes this forced

| Constant | Was | Now | Why |
|---|---|---|---|
| `TangleMaxHeight` | 28 | **63** | the summit cap is at 60.789 m; 63 keeps roughly the headroom the old 28 left over the 24.05 m summit bubble |
| `SectionVolumeHeight` | 60 | **80** | the volumes run from `SectionVolumeBottomY` = −10, so a 60 m column tops out at +50 and would put a player on the spire's summit **outside the section they climbed** — the exact defect the note on `SectionVolumeBottomY` describes for blue |

`SectionVolumeBottomY` is unchanged at −10, still comfortably above the TV room's ceiling at −16.

> **Correction, MRF-B / F5, 2026-08-30.** This row originally ended "−10..+70 now covers every
> summit in the level". **It did not, and W7-1 shipped with the defect it had just described.**
> `SectionVolumeHeight` is a *constant*; the section triggers are six hand-authored `Area3D`s in
> `scenes/game/world/bubbletest/BubbleTest.tscn`, and nothing derives one from the other — the
> boxes were left at y-centre 20, `size.Y = 60`, i.e. −10..**+50**. The tangle spire's summit at
> **60.789 m** was 10.6 m outside `Volume_Tangle` for the whole of W7-1, so the dwell log
> attributed the reward beat at the top of the level's longest climb to **no section at all**.
> Nothing went red because `BubbleTestLayoutTests.SectionVolumesReachTheTopOfTheirOwnSection`
> asked `VolumeOf` about `SectionVolumeHeight` — the constant against the constant.
>
> MRF-B raised the six surface boxes to y-centre 30 / `size.Y` 80 (`Volume_TvRoom` is unchanged:
> its volume *is* its room box, and `VolumeOf` returns the footprint verbatim for it). The claim
> above is true of the shipped scene **as of that commit**, and three tests now read the `.tscn`
> itself rather than the constant — `TheShippedSceneAuthorsExactlyTheVolumesTheLayoutDerives`,
> `EveryTelevisionStandsInsideItsOwnSectionsVolumeInTheShippedScene`, and the corrected
> `SectionVolumesReachTheTopOfTheirOwnSection`. Reverting the scene turns all three red; that was
> demonstrated once as their positive control.

---

## 3. The televisions (note 8)

Five shipped by LEVEL-4, all standing on flat authored plates at y = 0. One stays; four moved.

| Route | Was | Now | Height | Pad |
|---|---|---|---|---|
| `HubTv` | (−22, 0, −22) | **unchanged** | 0.000 m | the hub plaza — the one on the grey level |
| `RedTv` | (−30, 0, −53) | (−0.159, 22.81, −79.456) | 22.810 m | red's new 5 m `TvPerch` |
| `HiddenTv` | (−41, 0, 132) | (60.383, 34.782, −81.066) | 34.782 m | the spire's 6 m spur pad |
| `BlueTv` | (56, 0, 20) | (79, 41.2, −1.2) | 41.200 m | blue's new 5 m `TvPerch` |
| `TangleTv` | (50, 0, −58) | (70.593, 60.789, −70.442) | 60.789 m | the spire's 8 m summit cap |

**Green and cyan have none, and that is unchanged reasoning.** Green's ground is a heightfield, so
an authored Y there cannot be derived — the paragraph on `TvRoutes` that made this rule still
binds, and every destination above is the top face of an authored box. Cyan's ceiling is 3 m and
green's is 6 m: a reward for climbing has to be at the top of a climb.

**Two perches were authored**, one each in `RedCairns.tscn` and `BluePrecision.tscn`: a
5 × 0.4 × 5 `TvPerch` whose top is **flush with the shipped SummitBlock's top**, so neither climb
changed by a centimetre — red's last step is still the same 0.9 m off `Tread22`, blue's still the
same 1.2 m off the `Core` roof. They exist because a television plus the space a returning player
lands in needs about 4 m of pad and both shipped summits are 2.0–2.6 m square.

On red the perch swallows `StackBlock19`, which pokes **0.24 m** through it — under the 0.300 m
jog-tap apex, so it is walked over rather than climbed, and it keeps the summit reading as a cairn
rather than a helipad.

**`RoomReturnOffset` went from (3, 1, 3) to (1.4, 1, 1.4), and that is a safety change.** A 4.2 m
diagonal from a television standing 1.2 m back from the middle of a 5 m pad lands the returning
player 3 m past its edge — off a 41 m tower. All three of the original reasons for the offset
survive at 1.98 m: still far outside the 0.45 m-deep walk-in trigger, still a diagonal so the lit
screen sits off to one side, still derived from the entrance rather than typed per room.

> **Correction, MRF-B / F6, 2026-08-30.** The length was right and the **bearing was wrong**.
> At 45° the offset spent 1.4 m of a 2.5 m pad half-width going sideways, so the four returns
> landed **1.10 / 1.10 / 1.60 / 2.61 m** from the lip of a 22.8–60.8 m drop, with yaw reset to −Z
> and forward typically still held from walking into the screen. MRF-B rotated the offset to
> **(0.7, 1, 1.85)** — the plan length is unchanged at 1.98 m, because REV-2's re-derivation
> against all four pads is what makes that length load-bearing — which is the bearing that
> maximises the *smallest* edge distance: **1.80 / 1.80 / 2.30 / 3.30 m**, against a 1.46 m bar
> (half a second of held forward at the shipped ramp, 1.10 m, plus the 0.36 m body radius).
> `BubbleTestLayoutTests.EveryTvReturnStandsClearOfItsPadsEdge` reads the pads out of the section
> `.tscn`s and re-derives all four every run; the old value fails it. **The yaw itself is not
> fixed** — `SandboxAvatar.ServerTeleportTo` takes a position and `MoveState.AtSpawn` zeroes
> `Yaw`, so there is no yaw channel from a portal to a teleport; that is `SandboxAvatar`'s file
> and is reported as a fork, not improvised.

---

## 4. What proves it

| Instrument | What it establishes |
|---|---|
| `--bubbletest-selftest` | every relocated television's base is level with the surface under it to within 0.000 m (a downward ray, own bodies excluded); the six spawn markers sit on the moved ring and face the pedestal; every section's geometry is inside its own footprint under the new `TangleMaxHeight`; packed-vs-live node counts match for all seven sections |
| `--tvportal-selftest` | all five round trips driven with a real body, and every returning body lands within 2 m of the surface point its route was wired to. ~~every returning body settles on **its own pad's top** — 22.810 / 34.782 / 41.200 / 60.789 — rather than falling off it~~ — **overclaimed; corrected MRF-B / F15, 2026-08-30.** `TvPortalSelfTest.AfterReturnLeg` samples once at 0.4 s against a ≤2 m tolerance, and free fall in 0.4 s is ~0.79 m, so a body that had already walked or fallen off the pad would still have passed. What settling on the pad is now proved by is `BubbleTestLayoutTests.EveryTvReturnStandsClearOfItsPadsEdge`, from the authored pad geometry |
| `tools/dev/bubbletest_reach.gd` | a whole-level jump-graph search from the hub plaza reaches all four relocated pads **on single jumps alone**: 19, 33, 48, 59 jumps. Its positive control — a slab 40 m above the level's peak — comes back unreachable |
| `tools/dev/tangle_tower.py` | the two geometric guarantees above, asserted before it writes |
| `docs/qa/W7-1/` | nine in-engine frames, including the before/after spawn pair |
