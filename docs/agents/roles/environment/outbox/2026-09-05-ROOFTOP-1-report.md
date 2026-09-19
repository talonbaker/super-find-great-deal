---
packet: ROOFTOP-1
role: environment
date: 2026-09-05
branch: feat/2026-09-05-rooftop-1-seventh-tv
base: playtest/2026-09-04-combined (cut at 1f4d688; merged forward to 0264570 mid-packet)
type: report
---

# ROOFTOP-1 — the seventh television, the diving board and the last bubble

**Done.** A television on the puffin lab's **far** roof takes the player to a platform 200 m over
the hub, where the level's hundredth bubble sits on the end of a diving board. They walk out, take
it, and fall the height of the world onto the hub plaza with the whole map coming up at them.

**Talon confirmed the siting himself in the running game, 2026-09-05: _"YES! that's the roof!"_**
That confirmation outranks the packet text and outranks this report; the next person to read
`BubbleTestLayout.RooftopTvPos` should not re-open it.

---

## The thing I got wrong first, and the test that would have caught it

**There are TWO roofs on Talon's route and I put the television on the first one.** He saw it and
said: *"That's the wrong roof. It's the one farther back. It's the one out more."*

The packet's wording — *"the top reachable by jumping from cyan onto the lab, crossing the tunnel,
and standing on three stacked golden cubes"* — reads as one surface and is actually a sequence
across three. **The disambiguating test is the one that matters, and it is not "a roof on the
lab":** it is *the surface whose only access is the cube stack at the far end of the tunnel*. The
landing roof fails that test — the cyan jump alone reaches it, with no tunnel and no cubes.

Measured off the shipped scenes, runtime-active variants only:

| Surface | Node | Top y | Extent | Area |
|---|---|---|---|---|
| the **landing** roof | `PuffinLab/Lab/HubAccess_Off/Ceiling` | **−6.410** | x 78.55..101.45, z 81.31..98.69 | 398.3 m² |
| "that little tunnel" | `PuffinLab/Route/HallCeil` | **−16.070** | x 115.81..171.83, z 69.58..74.54 | 278.3 m² |
| the **far** roof | `PuffinLab/Route/VoidCeil` | **−12.206** | x 162.04..173.90, z 74.13..85.45 | 134.3 m² |

### The three numbers the coordinator asked for

**1. The distance and height between the two roofs.** Centre to centre, **78.64 m apart in plan**
— 77.97 m east and 10.21 m north — with the far one **5.796 m LOWER**. "Farther back and out more"
is +78 m of x and −5.8 m of y. Both numbers are now in `BubbleTestLayout.LabLandingRoofTopY`'s and
`LabVoidRoofTopY`'s docs and are printed by the suite on every run, so nobody has to guess again.

**2. Does the far roof genuinely require the cube stack? YES — and more than that.** It is a
**3.864 m** climb from the tunnel and there is nothing in between: I swept every runtime-active
collider in the lab for a surface within a jump of the tunnel's east end and the far roof is the
only thing above it. The nearest intermediate is `Route/VoidWallS_Upper` at −12.620, a 0.41 m-wide
parapet strip 3.45 m up — narrower than a landing and still out of reach. There is no cube-free
route onto that slab.

**3. Is three cubes the count? THE ARITHMETIC SAYS NO AND THE PLAY SAYS YES.** A `Crate.tscn` is
**0.44 m**; three stacked is 1.32 m. A full jump's apex on the shipped motor is
`JumpVelocity² / 2·Gravity` = 8.4² / 48 = **1.47 m**. Three cubes plus a jump lifts a player
**2.79 m** against a **3.864 m** step — **1.07 m short** on those numbers. Six cubes (2.64 m) clear
it; five (2.20 m) miss by 0.19 m; using the 0.41 m parapet as an intermediate step brings the
requirement down to five.

**Talon then closed it by doing it, 2026-09-05: _"The roof is reachable. I have proved it myself so
this is fine."_** No cubes were placed, no step was authored, bubble 100 stays on the diving board.
**The measurement above stands exactly as measured and is not revised to fit** — see Closed
question §1 for the two candidate explanations and for why neither is asserted.

I did not "fix" the shortfall, and that was right for a second reason as well as the first: the
geometry is EGG-1's, the access method is Talon's, and inventing a staircase at the end of a route
he told me not to change is the one thing the packet forbids. `CheckRooftopRoute` **prints** the
arithmetic every run and asserts none of it, which is now exactly the right posture: the printed
numbers are the early warning if the jump, the gravity or the cube height ever moves.

### And the first leg, since I measured it anyway

Cyan's east lip is x = 70 and the landing roof's west lip is x = 78.55: **8.55 m of gap**, or
**7.83 m centre-to-centre** for a 0.36 m capsule, across a **6.41 m** drop. Sprinting at 6.08 m/s,
an immediate jump reaches **6.15 m** and a jump spent at the end of the 0.30 s coyote window
reaches **7.54 m**. That is 0.29 m short on a model that deliberately omits the apex hang
(`ApexHangStrength` 0.30) which lengthens every arc — so **his route is a maximum-range,
coyote-timed sprint jump, sitting almost exactly on the limit**, which is entirely consistent with
it being a thing he found rather than a thing anyone designed. I did not touch it and I make no
claim about whether it lands; the numbers are printed, not asserted.

---

## What was built

### 1. The seventh television — `TvRoutes` row 7

`new(RooftopTvNodeName, RooftopTvPos, "RoomG/RoomTv", SkyDeckRoomCentre)` at
**(168, −12.186, 80.5)**, on an authored pad on the far roof. It stands 5.97 m from the west lip,
5.90 m from the east, 6.37 m from the south and 4.95 m from the north — the point on that slab
furthest from every edge, facing the south edge a player climbs over.

**The stale prose is corrected.** Two comments in `BubbleTestLayout.cs` said "five rooms" — the one
on `TvRoomPlanHalf` and the header of `TvRoutes` itself ("Five entrances, five rooms"). Both now
say seven, both carry the history (EGG-2 made it six and did not move them; this packet made it
seven), and both point at `TvRoutes.Length` as the authority. `BubbleTestWorld`'s "six SECTION
scenes" and the `TvRoute` doc's "adding a sixth route" were stale in the same way and are fixed.

### 2. The eighth section, `LabRoof` — and why it had to exist

`EveryTelevisionStandsInsideItsOwnSectionsVolumeInTheShippedScene` (xUnit, **not** a file this role
may write) requires every `TvRoutes` row to stand inside exactly ONE section volume in plan. The
lab's plot was inside none — cyan stops at x = 70, blue stops at z = 50, the lab is at x 162..174.
**That test was right to fail:** its own message says *"a player on that pad is attributed to NO
section, so the dwell log reports the opposite of the climb they just made."*

**Growing cyan east was the two-line fix and it is not available.**
`LocalFootprintsRoundTripThroughTheAnchorAndAreCentredOnIt` requires every non-TV-room footprint to
stay centred on its own anchor, so reaching x = 176 costs a symmetric 212 m-wide cyan that also
claims 106 m of empty void to the WEST. That is a lie about ground ownership in two directions to
fix an attribution in one. A section is what this level calls a place; the far roof is a place.

- anchor **(145, 0, 77.5)**, footprint **x 114..176, z 68..87** — the tunnel and the far roof.
  Overlaps nothing (blue ends at z 50, cyan at x 70, the tangle at z −50) and sits inside
  `OffMapRadiusM` with 34 m to spare.
- **its volume is −17..+70, not −10..+70**, and that is the one place `VolumeOf` grew a branch.
  Every other section's column starts at −10 because every other section's ground is at 0; this
  one's is 12–16 m under it. **−17 is a boundary, not a margin:** below the tunnel a player walks
  on (−16.070) and above the floor of the black room underneath it (−17.588), so the box holds
  everyone on TOP of the lab's escape route and nobody inside it.
- **it authors exactly one thing** — the television's 6 × 6 m pad, top face 2 cm proud of the far
  roof. Two centimetres because a coplanar pair z-fights and only a render shows it; not twenty
  because a player who has just made a 3.9 m climb should not then meet a lip.
- **the pad is why the section is not empty ceremony.** `PuffinLab.tscn` is OPTIONAL and the level
  must come up without it. `CheckPropHeights` rays down under every entrance television and fails
  one standing on nothing, so a television whose ground is an optional scene falls out of the world
  in a build that is supposed to be supported. The pad ships in every build; the roof does not.

**Stated limitation:** the footprint does not cover the *landing* roof. Spanning both would be
100 m wide to hold two slabs 78 m apart, and no television stands on the landing roof — which is
the only thing a section volume is required to hold. A player there still reads as no section,
exactly as they did before this packet.

### 3. The vantage — `RoomG`, the sky deck, at y = 200

A 14 × 14 m platform directly over the hub, authored in `TvRoom.tscn`. **It is in that file because
`TvPortalSelfTest` finds every route's way back at the hard path `TvRoom/<ReturnTvPath>`** — a
destination outside that section node has no way back the shipped test can see, and a room a player
can enter and not leave is the one failure this level treats as unrecoverable. The TV room is
already the section that is nowhere in the world; a room 200 m up is the same kind of nowhere as a
room 20 m down. It is also directly over the hub, which is what makes the fall land on the flattest
80 × 80 m in the level.

**200 m is a value I picked, off the camera's own frame rather than off how big the number sounds.**
`SandboxCamera.DefaultFov` is 75° *vertical*, so a camera looking straight down from height *h*
frames a radius of `h·tan(37.5°) = 0.767h`. The furthest authored corner from the origin is cyan's
(70, 150) at 165 m; blue is 153, red 152, green 149. At 200 m the frame radius is **153 m** — the
hub, all four surface sections and the tangle in one frame, with cyan's far corner arriving the
moment the camera pitches (its horizontal half-angle is 57°, i.e. 308 m). At 180 m the radius is
138 m and two corners fall outside; at 250 m everything fits and every landmark is 25 % smaller.
**200 is the smallest round height that frames the level**, and it is 3.29× the tangle spire's
60.789 m, which was the top of this world until today.

**The one constant this cost:** `MaxHeightOf(TvRoom)` split off `TvRoomHeight` into a new
`TvRoomCeilingM` (derived, 225 m), because `CheckFootprints` bounds a section's meshes at
`anchor + MaxHeightOf + 0.5`. **`TvRoomHeight` itself did not move** — it is the sealed box's height
and `TvRoomFootprint`, `VolumeOf`, `SecretPictureMountM` and `SecretPictureCentreY` all read it.
Raising it would have grown the `SectionVolume` trigger 220 m up through the whole world and
resized the Deep Room's picture.

### 4. What the altitude actually costs — scope item 2, measured

| Thing | Finding |
|---|---|
| **fog, day** | 0.001 m⁻¹ at noon. 259 m of slant to the furthest corner leaves **77 %** of the contrast. The map reads — see `docs/qa/ROOFTOP-1/map/`. |
| **fog, night** | **The level below is not hazy, it is ABSENT.** `BubbleTestWorld` binds fog to a 28 m sight range (`SightPresentation.FogDensityWithSight`, `NightSightRangeM`) = 0.107 m⁻¹ ≈ **5e-10** transmittance at 200 m. **This vantage is a daylight reward.** Not fixed here: the night fog is `SightPresentation`'s, deliberate, and not a level constant to reach into. |
| **camera far plane** | Godot's default **4000 m**; nothing in the gameplay path sets it. Worst-case sight line from the deck is 276 m. Safe by a factor of 14. |
| **star dome** | radius 700 m, origin-centred, `depth_draw_never`, `cull_front`, lower hemisphere faded out. A camera at 200 m is well inside and it cannot occlude a downward view. |
| **night dome** | **There isn't one in this repo.** Every reference is a comment about the removed camp. |
| **distance culling / LOD** | none on any bubbletest section scene. The only `visibility_range_end` in the repo is inside the sealed lab. |
| **shadows** | **the one visible artefact.** `ShadowRangeM` is 120 m, so from 200 m the whole level renders unshadowed and a player riding up watches every shadow fade out through 102–120 m. Not fixed: `SunShadowMaxDistanceM` is `OutdoorAtmosphere`'s and lighting is not this role's. |
| **the moon** | a 34 m opaque unfogged sphere anchored 650 m from the ORIGIN, not the camera. At a low moon elevation it can appear *below* the horizon line from this altitude. Night only; recorded, not fixed. |
| **section volumes** | the deck at y = 200 is outside every `SectionVolume` (they span −10..+70), so a player on it reads as no section in the dwell log. Nothing asserts otherwise; recorded. |

### 5. The diving board and the hundredth bubble

A 1.2 × 10 m plank from the deck's north edge (z = −7) to **z = −17**, with a mount block under its
root so the silhouette reads as a diving board rather than as a plank somebody left out. **Its top
is flush with the deck** — the walk out has to be a walk, and this game has no automatic step-up, so
a lip at the root of a board over a 200 m drop is a stumble the player pays for with the whole ride.
No spring, no animation, no effect, no camera work.

A teleport resets yaw to −Z, so a player arriving at `RoomArrivalOffset` is **already looking
straight down the board** with the way out behind them — the same rule every other room in the file
follows, here aimed at the joke.

**The bubble is RELOCATED, not added, and the total is still exactly 100.**

```
[bubble] adopted 100 bubble(s) (server=True)
[bubbletest-selftest]     Bubble_Seam_02 id 48 at (0, 201.05, -15.9)
```

| | |
|---|---|
| node | `Bubble_Seam_02`, in **`Hub.tscn`** under `Bubbles` — unmoved between files |
| id **before** | **48** |
| id **after** | **48** — unchanged, because ids are the index of an ordinal NODE-PATH sort and a `position` edit changes no path |
| position before | (3.5, 1.45, −44.5) |
| position after | **(0, 201.05, −15.9)** |
| re-bake | none needed — no node count changed, and `CheckBake` reports `Hub` packed == live |
| constants moved | **none.** `SeamBubbles` stays 5, `HubBubbles` 7, `BubbleTarget` 90, `BubbleTargetWithLab` 100 |

**BUBBLE-1's nomination was re-verified on my base and still held** — BUBBLE-2 landed while I
worked and touched `Tangle.tscn`, not `Hub.tscn`, so `Bubble_Seam_02` was untouched and
`Bubble_Seam_01` still sits 3.8 m away on the north connector doing the same job.

**Standing-reachable, and that is a decision.** Talon said *"the player can jump and catch the
bubble"*; I put it 1.10 m short of the tip and 1.05 m up, so walking out over a 200 m drop takes it.
Off the END, where only a jump reaches, the level's hundredth bubble would be missable on the
attempt and dependent on a re-ride — not unwinnable (the ride repeats) but the one bubble in the
level that punishes a mistake, on the one platform where a mistake is a 3.3 s fall. Measured live:

```
rooftop: board top under the bubble 200.000, bubble collider underside 200.700,
         a standing crown reaches 201.241 (0.541 m of margin)
embed id  48 Bubble_Seam_02 (0.000, 201.050, -15.900) ok
         clear of geometry; a body at 0.00 m out, 0.00 m up pops it
```

The second line is **BUBBLE-2's** `CheckBubbleEmbedding`, which I merged in mid-packet — an
independent instrument, written for a different defect, agreeing that a body standing right there
takes it.

### 6. The fall — five points, not one

```
rooftop fall from board tip  at (0, 200, -17)   -> lands y=0.00 after 3.33 s (30.0 m above the kill plane)
rooftop fall from deck north at (0, 200, -6.5)  -> lands y=0.00 after 3.33 s (30.0 m above the kill plane)
rooftop fall from deck east  at (6.5, 200, 0)   -> lands y=0.00 after 3.33 s (30.0 m above the kill plane)
rooftop fall from deck south at (0, 200, 6.5)   -> lands y=0.00 after 3.33 s (30.0 m above the kill plane)
rooftop fall from deck west  at (-6.5, 200, 0)  -> lands y=0.00 after 3.33 s (30.0 m above the kill plane)
```

Every one lands on the **hub plaza at y = 0.00**, which is 80 × 80 m of flat authored ground.

**What the fall costs the player: nothing.** There is no fall damage anywhere in this game and no
terminal-velocity clamp in `AvatarMotor` (which is why the fall is 3.33 s and ends at 120 m/s).
`NetProfile.KillPlaneY` is −30 and `VoidKillY` is −70, both 30 m and 70 m below the landing. The
lake is 95 m west in green, so there is no drown. It is not a respawn, not a death, and not a
punishment — it is the ride.

### 7. Repeatable — the stated default

The television is an ordinary `TvPortal` with no one-shot gate, so the ride repeats indefinitely.
**Talon was asked and did not rule; this is the stated default and he can reverse it in a sentence.**
The reasoning: the view is the reward and the bubble is only the excuse, so a player who collects
the bubble on their first trip and *then* wants to look should not have earned a locked door.

### 8. The return — the third named exception, and it is forced

`TvPortalSelfTest.CheckDestinations` requires every room's way out to land **above y = 0** ("the way
out has to leave the room"). The far roof is 12.21 m below grade and there is no authored ground
above y = 0 within 100 m of it, so the standard "beside the television you came in by" return is
the one thing this route cannot have.

So `ReturnFor` gains a third branch beside the hub's and the sunken television's:
**`RooftopTvReturn` = (64, 1, 90)** — cyan's plate, 6 m in from the east lip a player jumps off, on
flat authored ground. Yaw resets to −Z so the returning player faces north ALONG the lip rather
than over it, and half a second of held forward moves them 1.66 m further from the drop.

```
rooftop return (64, 1, 90) -> ground at 0.000
```

**The cost is stated rather than hidden:** leaving by the television puts a player back at the
*beginning* of the whole traversal, not at the end of it. Jumping is the cheaper way down, and that
is deliberate.

---

## Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | `dotnet build` 0 errors | **PASS** — `0 Error(s)`, 4 pre-existing warnings |
| 2 | `TvRoutes` has seven entries; stale "five rooms" corrected; `TvPortalSelfTest` drives the new route | **PASS** — see §1 and the raw counts |
| 3 | adopted count exactly 100; relocated id unchanged; before/after stated | **PASS** — `adopted 100`, id **48** both sides, positions in §5 |
| 4 | headed captures, `--capture-cam` | **PASS** — 7 shots in `docs/qa/ROOFTOP-1/` with an index |
| 5 | Talon's route unchanged, ABSENCE check with a positive control | **PASS** — see below |
| 6 | fall survivable from ≥ 3 points, with where each lands | **PASS** — 5 points, all on the hub plaza |
| 7 | the return path works | **PASS** — §8, and `TvPortalSelfTest` drives the round trip |
| 8 | `Run-BubbleTestWorldTest`, `Run-BubbleSyncTest`, `Run-PuffinLabTest` | **PASS** — rows quoted below |
| 9 | both suite commands, raw counts, reds discriminated, own xUnit baseline | **PASS** — below |

### Criterion 5 — the absence check, with its control

`tools/dev/rooftop1_route_probe.ps1`. The route is made of three surfaces and four files: cyan's
plate (`sections/CyanRun.tscn`), the landing roof and the tunnel
(`puffinlab/PuffinLab.tscn`, `LabRoom.tscn`, `EscapeRoute.tscn`). **This packet authored its pad in
a NEW section file for exactly this reason** — the surfaces themselves live in scenes it is
forbidden to edit, so "did the route change" reduces to a question `git diff` answers exactly.

```
[route-probe] real run
[route-probe]   scenes/game/world/bubbletest/sections/CyanRun.tscn         unchanged
[route-probe]   scenes/game/world/puffinlab/PuffinLab.tscn                 unchanged
[route-probe]   scenes/game/world/puffinlab/LabRoom.tscn                   unchanged
[route-probe]   scenes/game/world/puffinlab/EscapeRoute.tscn               unchanged
[route-probe] ROUTE UNCHANGED - none of the 4 files ... was touched.

[route-probe] CONTROL: planting a 1 m eastward move of cyan's ground plate.
[route-probe]   scenes/game/world/bubbletest/sections/CyanRun.tscn         CHANGED
[route-probe]      scenes/game/world/bubbletest/sections/CyanRun.tscn | 4 ++--
[route-probe] ROUTE TOUCHED - 1 file(s): ...CyanRun.tscn
[route-probe] CONTROL FIRED AS EXPECTED - the probe can see a change to the route.
[route-probe] CyanRun.tscn restored.
```

The control edits the file on disk and restores it from a copy in a `finally` — **not** `git stash`,
which has eaten an agent's work in a shared checkout on this machine before.

---

## Raw counts

**xUnit — baseline measured in a throwaway detached worktree** (`C:/repos/Watis-r1base`, detached
at `0264570`, the merge parent), then the tip:

```
baseline: Passed!  - Failed: 0, Passed: 2288, Skipped: 0, Total: 2288 - SailNet.Tests.dll (net8.0)
tip:      Passed!  - Failed: 0, Passed: 2288, Skipped: 0, Total: 2288 - SailNet.Tests.dll (net8.0)
```

Identical. Every layout test passes **unmodified** against the eighth section and the seventh route
— including `EveryTelevisionStandsInsideItsOwnSectionsVolumeInTheShippedScene` (the one that forced
`LabRoof`), `LocalFootprintsRoundTripThroughTheAnchorAndAreCentredOnIt`,
`NoTwoSectionFootprintsOverlapInPlan`, `SectionVolumesCoverTheirFootprints`,
`SectionVolumesReachTheTopOfTheirOwnSection`, `TheShippedSceneAuthorsExactlyTheVolumesTheLayoutDerives`
and `EveryTvReturnStandsClearOfItsPadsEdge`.

**Scene marathon** — `tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`, redirected, counted from the
run's own `=== summary ===` block with the anchored pattern `^  .+ (PASS|FAIL)$`:

```
OVERALL: PASS - all suites green.
```

**48 rows, 48 PASS, 0 FAIL.** Exit code 0. No reds, so there is nothing to discriminate and no entry
to add to `.claude/rules/test-suite.md` — nothing from its known load-sensitive list (`World: run
driver`, `World: tidal-cycle phase`, `Carry: regrab-while-loose`, `Reconnect: grace window`,
`Netcode: anti-cheat`) came up. That rule's warning applies in the other direction too: **a
full-house pass is not the expected result**, so this is one green marathon, not proof the tree is
deterministic. (BUBBLE-2, on this same tree yesterday, measured `World: run driver` and `World:
tidal-cycle phase` red under load and green 3× standalone.)

The rows criterion 8 names, plus the two the seam touches:

```
  World: bubble test (BT-0)  PASS
  Bubbles: shared counter    PASS      <- Run-BubbleSyncTest, the id space
  World: TV portal (BT-10)   PASS      <- Run-TvPortalTest
  Egg: puffin lab (EGG-1)    PASS      <- Run-PuffinLabTest
  Watcher: night gate        PASS
```

**`CheckBake` is packed == live on every section, including both this packet edited:**

```
  Hub            packed[mesh=64  shape=36  body=24 ] live[...same...] OK
  RedCairns      packed[mesh=162 shape=159 body=148] live[...same...] OK
  BluePrecision  packed[mesh=193 shape=112 body=101] live[...same...] OK
  CyanRun        packed[mesh=99  shape=70  body=52 ] live[...same...] OK
  GreenHills     packed[mesh=25  shape=54  body=12 ] live[...same...] OK
  Tangle         packed[mesh=197 shape=196 body=184] live[...same...] OK
  TvRoom         packed[mesh=571 shape=76  body=54 ] live[...same...] OK
  LabRoof        packed[mesh=1   shape=1   body=1  ] live[...same...] OK
```

**`TvPortalSelfTest` drives the seventh route with the other six** — criteria 2 and 7, proven by the
shipped test rather than by me:

```
portals found: 15 (expected 15 — 7 route(s), an entrance and a way back each, plus the lab's ReturnTv)
  .../BubbleTest/RooftopTv        at (168, -12.186, 80.5) -> (0, 200.4, 3)
  .../BubbleTest/TvRoom/RoomG/RoomTv at (0, 200, 5.2)     -> (64, 1, 90)
walking into RooftopTv at (168, -11.486, 80.92)
after entry:  body at (0, 200.00021, 3)          (0.40 m from RooftopTv's arrival point)
walking into RoomG/RoomTv at (0, 200.55, 4.78)
after return: body at (64, 0.00033263862, 90)    (1.00 m from RooftopTv's surface return)
```

The round trip runs on all seven, the 800 ms cooldown gate holds, and the idle probe parks a body in
the room for 3 s with `deaths=0`.

---

## Corrections to the packet

1. **"a television on the PuffinLab rooftop"** and *"the top reachable by jumping from cyan onto the
   lab, crossing the tunnel, and standing on three stacked golden cubes"* — **ambiguous between two
   surfaces**, and I resolved it the wrong way first. The route crosses a landing roof, a tunnel and
   a far roof; the reward is at the end. The disambiguating test is *the surface whose only access
   is the cube stack*, and it is now written into the layout contract so it cannot go ambiguous
   again. Talon corrected me and then confirmed the re-siting: *"YES! that's the roof!"*

2. **"the tangle spire's summit at 60.789 m is currently the top of the world"** — true, and the
   packet's inference that height is therefore safe needs one qualifier it does not have: the
   *kill planes* are safe (−30 and −70, both below), but `SectionVolume`'s columns top out at +70,
   so anything above that is outside every section for dwell purposes. Recorded, not fixed.

3. **"the sunken lake one" is the sixth `TvRoutes` entry** — correct, and the two stale "five rooms"
   comments the packet names are on `TvRoomPlanHalf` and on the `TvRoutes` header itself. There were
   **two more** of the same species next door, both now fixed: `TvRoute`'s doc said "adding a sixth
   route", and `BubbleTestWorld` said `CheckBake` "walks the six SECTION scenes". BUBBLE-1 predicted
   exactly this failure mode and it produced two more instances in one packet.

4. **`DECISION-LOG.md` is outside this role's write paths**, as BUBBLE-1 found on 2026-09-04. The
   acceptance criteria ask for a section in it; `ROLE.md`'s allowed-path list does not include the
   repo root, and the contract says a packet that seems to require an out-of-path write is the thing
   that is wrong. **I did not write it.** The section is reproduced below for someone with the grant
   to paste in.

5. **The base moved under me.** I cut at `1f4d688`; BUBBLE-2, STEAM-1 and two doc commits landed on
   `playtest/2026-09-04-combined` while I worked. I merged forward to `0264570` rather than leave an
   unmergeable branch — one conflict, in `BubbleTestSelfTest.MeasureThenProbe`, where BUBBLE-2 and I
   both added a call. Both calls are kept, BUBBLE-2's first. Its `CheckBubbleEmbedding` now measures
   my diving-board bubble and passes, which is a better witness than anything I wrote.

---

## For `DECISION-LOG.md` — every value I picked, so Talon can retune by hand

```
## ROOFTOP-1 — the seventh television, the sky deck and the last bubble (2026-09-05)

Talon designed this: "on the rooftop lab rooftop what if there was another TV? ... an impossibly
high angle and on this angle, there is the very last bubble the 100 bubble and maybe it's on like
a diving board so it's funny ... and then they get to fall and they get to see the whole map from
a very top of the whole world?"  Siting confirmed by him in game: "YES! that's the roof!"

  Value                       Picked      Where it lives                       Why that number
  --------------------------------------------------------------------------------------------
  sky deck height             200 m       BubbleTestLayout.SkyDeckY            0.767h frames 153 m
                                                                               vs a 165 m corner
  deck size                   14 x 14 m   SkyDeckHalfM = 7                     arrive, turn, walk
  diving board                1.2 x 10 m  DivingBoardTipZ = -17                cantilever reads
  board surface               flush       DivingBoardTopY = SkyDeckY           no step-up in game
  the hundredth bubble        (0,201.05,  SkyBubblePos                         standing-reachable,
                              -15.9)                                           0.541 m of margin
  television, far roof        (168,       RooftopTvPos                         furthest from every
                              -12.186,                                          edge of the slab
                              80.5)
  pad proud of the roof       0.02 m      LabRoofPadProudM                     no z-fight, no lip
  pad plan size               6 x 6 m     LabRoofPadSizeM                      on an 11.9 x 11.3 roof
  LabRoof anchor              (145,0,     LabRoofAnchor                        centred on tunnel +
                              77.5)                                            far roof
  LabRoof footprint           x 114..176  LabRoofFootprint                     overlaps nothing
                              z 68..87
  LabRoof volume floor        -17 m       LabRoofVolumeBottomY                 below the tunnel,
                                                                               above the room under
  TvRoom mesh ceiling         225 m       TvRoomCeilingM (NEW, split off       CheckFootprints
                                          TvRoomHeight, which did NOT move)
  the way back                (64,1,90)   RooftopTvReturn                      must be above y=0
  repeatable                  yes         (no gate added)                      stated default

To retune the height: change SkyDeckY. SkyDeckRoomCentre, DivingBoardTopY, SkyBubblePos and
TvRoomCeilingM all derive from it, and the fall probes re-measure. Re-run
tests/Run-BubbleTestWorldTest.ps1 and read the "rooftop" lines.

MEASURED, NOT FIXED, AND CLOSED BY TALON: the climb onto the far roof is 3.864 m, and 0.44 m cube
x 3 + a 1.47 m jump apex = 2.79 m, which is 1.07 m short. Six clear it. He resolved this by making
the climb himself on 2026-09-05 ("The roof is reachable. I have proved it myself so this is fine"),
so nothing was placed and nothing was authored. The arithmetic is left as measured and is still
PRINTED by CheckRooftopRoute every run, asserted nowhere -- it is the early warning if the jump,
the gravity or the cube height ever changes. The likeliest reason the real climb succeeds is that
the model is deliberately conservative (it omits ApexHangStrength 0.30, which lengthens every arc);
that is not proven, and neither is the alternative that the 0.41 m parapet is used as a step.
```

---

## The bible check

```
Bibles applied:  LEVEL (this arranges things into a playable space: a television, a platform, a
                 board and where a collectible sits on it — placement and composition, which is
                 this role's whole subject);
                 INTERACTION (the player directly acts on the television and on the bubble, and
                 the packet names it by name for the television).
                 Not BEHAVIOR — nothing placed moves or acts; the sky deck has no entity on it.
                 Not MECHANICS beyond the bubble tally, which is untouched: no new state, no
                 timer, no race, no win condition. Not THRILL — no affect change was directed,
                 /direct was not invoked, and no juice was invented (the board has no spring, the
                 bubble no effect, the fall no camera move).

Items checked:   LEVEL-BIBLE composition — the level gains ONE new place and it is the payoff of
                 a traversal that already existed and had no payoff. Nothing was added to a
                 section that already reads; the sky deck is deliberately bare (no couch, no lamp,
                 no rail) because the view is the content and a rail on a diving platform is a
                 rail against the only thing the place is for.
                 LEVEL-BIBLE path_topology — Talon's route is UNCHANGED and that is proved by a
                 probe with a positive control rather than asserted (criterion 5). One route was
                 ADDED: the television's, which is a teleport and therefore adds no walkable
                 topology at all. The way back was placed with the same care the four summit
                 returns got at MRF-B: it lands 6 m in from the lip it sends you back to, facing
                 along the lip rather than over it.
                 LEVEL-BIBLE ambient legibility — measured rather than assumed, and it produced
                 the packet's most useful negative finding: at night the level is not visible from
                 the deck at all (5e-10 transmittance), so the vantage reads only in daylight.
                 Recorded in the constant's own doc and in the QA index, not hidden.
                 LEVEL-BIBLE anti_unwinnable_guarantee — THE LOAD-BEARING ONE, and the one that is
                 NOT fully closed. "Collect all the bubbles" is the level's objective, so an
                 uncollectable bubble is an unwinnable state. Three things were done rather than
                 asserted: the bubble is standing-reachable from the board with 0.541 m of margin
                 (measured live, and independently confirmed by BUBBLE-2's embedding sweep); the
                 ride is repeatable, so a missed grab is recoverable; and the total is still
                 exactly 100 with the relocated node keeping id 48. The fourth thing — the
                 ACCESS to the television itself — I measured, printed every run, did not paper
                 over, and carried to Talon as a question; he closed it on 2026-09-05 by making
                 the climb himself. So the guarantee holds by demonstration where my arithmetic
                 said it should not, and the arithmetic is still printed rather than deleted
                 (Closed question 1).
                 INTERACTION-BIBLE, the television — it is the SAME prop and the SAME contract as
                 the other six: a TvPortal with an authored screen, glow pool and walk-in trigger,
                 rotated 180 deg like every other entrance so the approach heading is world -Z and
                 "you arrive looking into the room and must turn to leave" is true here too. No
                 new interaction, no new binding, no new class. The one thing that differs is its
                 return branch, and that difference is forced by a shipped assertion rather than
                 chosen.

Result:          PASS. The one item that was open at the time of writing — the ACCESS to the far
                 roof, which is EGG-1's geometry and Talon's design — was measured rather than
                 guessed, carried to him as a question rather than silently fixed, and closed by
                 him on 2026-09-05 by demonstration. Nothing in the level changed to close it.
```

---

## Closed questions

1. **CLOSED by Talon, 2026-09-05, by demonstration — "The roof is reachable. I have proved it
   myself so this is fine."** Nothing was changed to close it: no cubes placed, no step authored,
   bubble 100 still on the diving board.

   **What was raised.** The climb from the tunnel onto the far roof is **3.864 m**, and three
   0.44 m golden cubes plus a 1.47 m jump apex is **2.79 m** — 1.07 m short. Six cubes clear it;
   five plus the 0.41 m parapet strip also would. That mattered because the hundredth bubble is
   behind that climb, so an unreachable roof would have made "collect all the bubbles"
   unachievable — the anti-unwinnable item. I offered three ways out and picked none, because two
   of them were changes to what Talon designed.

   **The arithmetic and the outcome genuinely disagree, and both halves are recorded on purpose.**
   The measurement is not revised to fit; the roof is reachable anyway. Two candidate explanations,
   neither of which I am asserting because neither was measured:

   - **The likelier one: my model is conservative and says so.** `AirtimeToDrop`'s own doc states
     it omits `ApexHangStrength` (0.30 of gravity removed within 2 m/s of the apex), `AirControl*`
     and the jump buffer, so every arc it prints is a FLOOR on the real one. The hang lengthens
     the top of a jump, which is exactly where a climb of this shape is decided. **Nobody has
     measured the hang's contribution to apex height** — not me and not the coordinator — so this
     is the probable explanation and not a proven one.
   - **He may be using the 0.41 m parapet** (`Route/VoidWallS_Upper`, top −12.620) as an
     intermediate step, which this report already notes brings the requirement down to five cubes,
     and which the hang could then close.

   **Which one it is does not need determining.** The discrepancy is unexplained in the numbers and
   settled in practice, and that is the honest state to leave it in.

   **`CheckRooftopRoute` keeps PRINTING the arithmetic and asserting none of it**, which is now
   exactly the right posture rather than a compromise: the printed climb, cube height and jump apex
   are the early warning if `JumpVelocity`, `Gravity`, `Crate.tscn`'s box or the lab's geometry ever
   moves — a change that would break a traversal Talon has proved by hand and that no assertion in
   this repo is watching.

## Open questions, ripe

2. **The sky deck is outside every section volume.** `SectionVolume`'s columns top out at +70 and
   the deck is at 200, so the dwell log — the playtest's only "is exploration fun" instrument —
   records nothing for the one place in the level built entirely to be looked from. Fixing it means
   a taller column or a volume of its own, and it is a telemetry decision rather than a level one.

3. **From 200 m the level renders with no shadows at all** (`ShadowRangeM` = 120 m), and the ride up
   fades every shadow out through 102–120 m. It is the most conspicuous thing about the view.
   `SunShadowMaxDistanceM` is `OutdoorAtmosphere`'s and lighting is not this role's, so it is
   reported rather than touched.

## Explicitly not done

The picture in RoomF, the bubble split, the section counts, BUBBLE-2's embedded bubble, any shader,
UI, naming or Steam work, and anything inside the PuffinLab room — its interior, its Lurker and its
`Arrival`/`ReturnTv` seam are byte-identical. `RoomE` is still a dead room while the lab is present
(BUBBLE-1's finding, still EGG-2's). No golden cube moved. No material was authored — the pad and
the deck use the shipped `neutral_gray.tres` and two `StandardMaterial3D` sub-resources in
`TvRoom.tscn`'s own file, which is that file's existing idiom.
