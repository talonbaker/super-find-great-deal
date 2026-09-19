---
packet: BUBBLE-1
role: environment
date: 2026-09-04
branch: feat/2026-09-04-bubble-1-hundred-bubbles
base: playtest/2026-09-04-combined
type: report
---

# BUBBLE-1 — one hundred bubbles, and some of them indoors

**Done.** The level carries **exactly 100**. The outer sections gave up 34 between them; the TV
destination rooms and the puffin lab — which had 3 and 0 — now carry 15 and 10.

The full before/after, every justification, the id map and the absent-lab demonstration are in
[`docs/levels/2026-09-04-BUBBLE-1-hundred-bubbles.md`](../../../../levels/2026-09-04-BUBBLE-1-hundred-bubbles.md).
This report carries what is *about the work* rather than about the level.

---

## Scope item 1 — all 112 accounted for

**The seven the constants did not cover are `SeamBubbles = 7`.** Bubbles on the four connecting
paths, belonging to no section, authored in `Hub.tscn` because the connectors are. 105 named + 7
seam = 112. `SecretBubble` is **not** among them and never could be: it shares no base class with
`Bubble`, so `AdoptAuthoredBubbles` — which collects by TYPE — cannot see it, and
`CheckSecretBubble` measures the census either side of popping it rather than asserting that in a
comment. There is no eighth candidate.

**But the constants were not the level.** Counted out of the shipped `.tscn` files, BT-11 placed
33 in cyan (const 35), 16 in red (15) and 16 in green (15) — total still 112. **That was disclosed,
not hidden**: all three are inside `BubbleSplitTolerance` (±5) and all three are in BT-11's own
counts table. What was missing was an instrument. `TheBubbleSplitAddsUpToTheTarget` compares the
constants to each other and never opens a scene, so a split that adds up correctly while describing
no level in particular passed forever. **`BubbleTestSelfTest.CheckBubbleCensus` is new here** and
walks the live tree per section, so every number is now measured every run — and the drift is zero
everywhere.

---

## Scope item 4 — the hard constraint, resolved as **(a)**

**(a): the target is composed from what was actually adopted.** `BubbleTarget = 90` (seven sections
+ seam, always present), `PuffinLabBubbles = 10`, `BubbleTargetWithLab = 100` — Talon's hundred.

**The trade, stated.** (b) — treat the lab as always present and make its absence a loud failure —
is the tidier constant (one number, no arithmetic) and it makes a broken build shout. It was
rejected because **the lab's absence is not a failure**, it is a supported state that EGG-2's
acceptance criterion 4 requires and LEVEL-1 demonstrated; turning a supported state into a red test
would mean the next person to work on EGG-1 in isolation gets a red suite for doing nothing wrong.
(a) costs one extra constant and one branch in one assertion, and buys a level that is honest in
both builds. **(c) was not needed** — it would have contradicted Talon's sentence, and it was never
the only honest option, so there was nothing to stop for.

**The thing that made (a) nearly free, and it was worth checking rather than assuming:** nothing
player-facing reads either constant. `HudBubbleCount` and `BubbleCounterDisplay` both render
`BubbleCounter.BubbleCount` — the live adoption count — so an absent lab *already* produced an
honest smaller total on screen and no code path could have lied about it. The constants exist only
so a test can say which case it is in. `BubbleTestSelfTest` therefore reads the lab's presence off
the **tree**, not off `ResourceLoader`: what the counter adopted is a fact about the tree.

Demonstrated with `PuffinLab.tscn` moved aside — **90 adopted, 0 `ERROR` lines, OVERALL PASS**, and
all 90 reachable, because with the lab gone `HiddenTv` reverts to RoomE, which has no bubbles.

---

## The finding that changed a placement: RoomE is unreachable

`BubbleTestWorld.DestinationFor` overrides `HiddenTv` — **RoomE's only television** — to the lab's
`Arrival` whenever the lab loaded, which is every shipped build. So nothing goes to RoomE. It keeps
its way out and has no way in.

That is why RoomE gets **zero** bubbles while B, C, D and F get three each: a bubble there would be
counted and uncollectable at once, which is exactly the unwinnable objective the packet forbids.
The lab is RoomE's replacement, and the lab is where those bubbles went. **RoomE having no route
while the lab is present is EGG-2's, not mine** — recorded so the next person to put something in
that room knows before they do.

---

## Corrections to the packet

1. **"there are exactly FIVE `TvRoutes` today ... so the rooftop one really will be the sixth"** —
   from the mid-task course correction. **There are six.** Counted off `BubbleTestLayout.cs:434`:
   `HubTv`, `HiddenTv`, `RedTv`, `BlueTv`, `TangleTv` and the EGG-2 sunken television. The rooftop
   TV will be the **seventh**. The likely source is a stale sentence in `GoldenCubes`' doc comment
   — *"one in each of `TvRoutes`'s five rooms"* — written before EGG-2 added the sixth and not
   moved with it. Corrected in place, since it is one line in a file this packet already edits.
   **Nothing in my placement depended on the count** (it depends on which rooms are *reachable*,
   derived from the routes themselves), so nothing needed correcting downstream.

2. **`docs/levels/KIT.md`**, which the packet named as required reading, is about the dev
   greybox course (`scenes/dev/KitCourse.tscn`) and its six primitives. It has no bearing on
   bubble placement in the bubbletest; its own §8 says so (*"Placing kit pieces in the bubbletest
   — later packets"*). Read, and it changed nothing.

3. **`DECISION-LOG.md` is outside this role's write paths.** Acceptance criterion 10 requires a
   section in it; `ROLE.md`'s allowed-path list does not include the repo root, and the contract
   says a packet that seems to require an out-of-path write is the thing that is wrong. **I did
   not write it.** Every number the criterion asks for is in
   `docs/levels/2026-09-04-BUBBLE-1-hundred-bubbles.md` instead — a path this role does own, and a
   more discoverable home for level numbers than the root log — and the "for Talon to retune by
   hand" section is reproduced below. **Someone with the grant should paste it in.**

4. **`tests/unit/BubbleTestLayoutTests.cs` is also outside this role's write paths**, and that
   shaped a real design decision rather than just a note. `TheBubbleSplitAddsUpToTheTarget` asserts
   `SeamBubbles + Σ BubblesOf(section) == BubbleTarget` — an invariant that is now *incomplete*,
   because a bubble-bearing scene (the lab) exists outside the seven sections. The clean fix is a
   one-line change adding `PuffinLabBubbles` to that sum and letting `BubbleTarget` be 100. I could
   not make it, so `BubbleTarget` is instead defined as the level **without** the optional lab (90)
   and `BubbleTargetWithLab` (100) carries Talon's number. That is a defensible shape — it *is*
   resolution (a) made explicit — and the xUnit test passes unmodified and still says something
   true. But **the naming is a compromise forced by a path grant, not a preference**: a constant
   called `BubbleTarget` that is not the level's target wants renaming, and renaming it needs the
   same file I could not touch. Programming should take both changes together.

5. **Two more stale claims found next door and deliberately left alone**, since golden cubes are
   not this packet's subject: there are only five room golden cubes (`RoomA`…`RoomE`) and RoomF
   never got one; and the same doc comment says green has no television, which stopped being true
   when EGG-2 put one under the lake.

---

## The relocation candidate for ROOFTOP-1

**`Bubble_Seam_02` — id 48, in `Hub.tscn` under `Bubbles`, at world (3.5, 1.45, −44.5).**

**Why it is the cleanest single bubble in the level to move.**

- **It is the most redundant.** `Bubble_Seam_01` sits 3.8 m away at (0, 1.1, −43), *centred on the
  north connector*, doing the identical job. Seam_02 is the second of a pair and the off-centre
  one. Nothing else in the level has a neighbour that close doing the same work — the cuts in this
  packet removed every other such pair on purpose.
- **It is the least load-bearing to a route.** Seam bubbles are the only ones in the level that
  belong to **no section** — `SeamBubbles`' own doc says so, and they live in `Hub.tscn` purely
  because the connectors do. So moving one does not make any *section's* count a lie about where
  its bubbles are, which is the semantic damage every other candidate would cause.
- **It is the least likely to be wayfinding.** Seam_01 is the one that marks the north connector;
  Seam_02 adds density to a mark that is already made. A player following the north path is
  following Seam_01.

**What moving it costs — the whole list.**

| Question | Answer |
|---|---|
| Does any test pin id 48? | **No.** Grepped every `.cs`, `.ps1` and `.gd`: nothing anywhere references a bubble by node name or by id. `Run-BubbleSyncTest` asserts on the `--bubble-selftest` **fixture's** six bubbles, not on this level. |
| Does the id change? | **No, and this is the point.** Ids are the index of an ordinal **node-path** sort. Relocation is an edit to one `position` line; the node keeps its parent and its name, so its path is unchanged and **id 48 stays id 48**. Nothing renumbers. |
| Is its section's count asserted? | Its bucket is `SeamBubbles = 5`, and yes — `CheckBubbleCensus` (new here) holds the hub to 7 own + 5 seam, and `TheBubbleSplitAddsUpToTheTarget` holds the constants to 90. **Neither moves**: the node stays in `Hub.tscn` under `Bubbles` with its `Bubble_Seam` prefix, so it is still counted as a seam bubble wherever it hangs. `BubbleTarget` stays 90, `BubbleTargetWithLab` stays 100. **No constant needs editing.** |
| Does CheckBake need a re-bake? | **No.** A `position` edit changes no node count, so `Hub`'s packed and live mesh/shape/body counts are untouched and the section is not re-baked. |
| Does `CheckIndoorReach` object? | **No** — it measures the 25 *indoor* bubbles only, and a seam bubble is neither in the TV room section nor in the lab. If ROOFTOP-1 wants the diving-board bubble measured, the check takes a new origin, not a new design. |
| What DOES need editing? | **Two comments and one doc line.** `SeamBubbles`' doc comment says *"one centred on each of the four connectors, and the north path keeps its second"* — after the move the north path keeps one, and the fifth is on the diving board. And the seam row in `docs/levels/2026-09-04-BUBBLE-1-hundred-bubbles.md` §2. That is the entire blast radius. |

**One caution.** The diving board is above the map, so whatever hangs there is outside every
section footprint and outside `SectionVolumeHeight`'s −10…+70 column. That is fine for a bubble
(it has `collision_layer = 0` and belongs to no volume) but it means `CheckIndoorReach`'s
straight-line sweep cannot be pointed at it — reachability there is a *fall*, not a walk, and it
will need its own argument.

---

## For `DECISION-LOG.md` — every number, so Talon can retune by hand

Paste as one section; I could not write the file (see Corrections 3).

```
## BUBBLE-1 — 112 bubbles to 100 (2026-09-04)

Talon, off a live playtest: "I only want there to be 100 bubbles in total", "further reduce
the number of bubbles on the top maps and put them into some of these additional spaces".

Every number lives in scripts/game/world/bubbletest/BubbleTestLayout.cs and is now held to the
shipped scenes by BubbleTestSelfTest.CheckBubbleCensus, so editing a constant without editing
the scene is a red test rather than a quiet drift.

  Section        const before   placed before   const after   placed after
  CyanRun            35              33             18            18
  RedCairns          15              16             11            11
  BluePrecision      15              15             11            11
  GreenHills         15              16             11            11
  Hub                10              10              7             7
  Tangle             12              12             12            12   (deliberately unchanged)
  TvRoom              3               3             15            15
  Seam                7               7              5             5
  PuffinLab           -               0             10            10   (new; optional scene)
  ----------------------------------------------------------------------
  BubbleTarget      112             112             90            90   (sections + seam)
  BubbleTargetWithLab  -               -            100           100   (+ the lab)

  BubbleSplitTolerance stays 5.  GoldenCubes stays 20.

To retune: change the constant, change the scene to match, run
tests/Run-BubbleTestWorldTest.ps1 and read the "census" lines. Cyan must stay strictly the
densest (CyanCarriesTheDensestBubbles). RoomE must stay at zero while the lab exists — no
television goes there.
```

---

## Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | `dotnet build` 0 errors | **PASS** — `0 Error(s)`, 4 pre-existing warnings |
| 2 | live adopted count is exactly 100 | **PASS** — `[bubble] adopted 100 bubble(s) (server=True)` |
| 3 | before/after table, each change justified, in report + `docs/levels/` | **PASS** |
| 4 | absent-lab case demonstrated | **PASS** — 90 adopted, **0 `ERROR` lines**, OVERALL PASS; resolution **(a)** |
| 5 | `Run-BubbleTestWorldTest` passes, CheckBake equal on every section | **PASS** — all seven `OK` |
| 6 | reachability proven for every new bubble **by a bot route** | **PARTIAL — see below** |
| 7 | `Run-PuffinLabTest`, `Run-WatcherNightGateTest`, `Run-BubbleSyncTest` pass | **PASS** |
| 8 | headed captures under `docs/qa/BUBBLE-1/`, `--capture-cam` | **PASS** — 7 shots |
| 9 | both suite commands, raw counts, reds discriminated | see below |
| 10 | report with corrections, bible check, DECISION-LOG section | **PASS** (DECISION-LOG per Correction 3) |

### Criterion 6 is partial, and I am not going to describe it as met

**What I built:** `BubbleTestSelfTest.CheckIndoorReach`, which measures all **25** indoor bubbles
against the live physics space with the **live avatar capsule** — floor found by a downward ray;
the bubble's lowest reachable point (centre − collider radius − the *worst* phase of the bob, not
the mean) under a standing body's crown; the standing spot clear; and, where a straight line is
genuinely the route, a swept capsule from the room's arrival point sampled every 0.35 m with a
**step-height bound**, so it cannot climb a wall as if it were a ramp and call that a walk.

**It carries three positive controls and each fires on the item it is aimed at:**

```
reach CONTROL 5 cm off RoomB's west wall  -> rejected: nothing can stand under it
                                             — blocked by .../TvRoom/RoomB/WallWest/Body
reach CONTROL over the void between rooms -> rejected: no floor within 6 m below it
reach CONTROL 3.5 m over the Den's floor  -> rejected: its collider bottoms out at -17.00,
                                             1.84 m over a standing body's crown at -18.84
```

**It found two real defects,** which is the test earning its place: `Bubble_TvB_03` sat 0.45 m from
`GoldCube_RoomB_0` so the walk from arrival went through the crate, and `Bubble_TvD_02` sat 0.92 m
from `GoldCube_RoomD_0`. Both moved. It also reproduced, on `Bubble_Lab_06`, the exact artefact
`PuffinLabSelfTest` documents — a vertical capsule on a sloped trimesh clipping the surface it
rests on — and is fixed the same way, by excluding the one body the floor ray landed on.

**Why it is not a bot route.** The shipped scripted brain, `ScriptedGotoIntentSource`, walks to
**one** point and latches there permanently, and a bot cannot be spawned inside a sealed room 20 m
underground — the only way in is a teleport. So a multi-leg indoor route is not something the
harness can drive today. Building one means changing the intent sources under
`scripts/game/sandbox/`, which this role does not own. **This is a genuine gap in the packet's
assumption that "the bot harness and `--goto-script` exist for this": for the surface they do; for
sealed rooms they do not.** A physics measurement with three controls is stronger than eyeballing
and weaker than a driven bot, and calling it the latter would be the kind of claim this repo keeps
paying for.

**Six of the ten lab bubbles get their route from elsewhere and it is stated in the check's own
output:** they sit directly above stations in `PuffinLabSelfTest`'s nineteen-station table, which
that suite proves traversable by the live capsule on every run. A straight sweep down a winding
crawl would have passed for the wrong reason, so it is not run there.

---

## Raw counts

**xUnit — baseline measured in a throwaway detached worktree** (`C:/repos/Watis-b1base`, at
`playtest/2026-09-04-combined`, 418556d), then the tip:

```
baseline: Passed!  - Failed: 0, Passed: 2269, Skipped: 0, Total: 2269 - SailNet.Tests.dll (net8.0)
tip:      Passed!  - Failed: 0, Passed: 2269, Skipped: 0, Total: 2269 - SailNet.Tests.dll (net8.0)
```

Identical. `TheBubbleSplitAddsUpToTheTarget` and `CyanCarriesTheDensestBubbles` both pass
**unmodified** against the new constants — 18 > 15 keeps cyan strictly the densest.

**Scene marathon** — `tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`, foreground, redirected:

```
OVERALL: PASS - all suites green.
```

Counted from the run's own `=== summary ===` block with the anchored pattern `^  .+ (PASS|FAIL)$`:
**47 rows, 47 PASS, 0 FAIL.** No reds to discriminate — nothing from
`.claude/rules/test-suite.md`'s known load-sensitive list (`World: run driver`,
`World: tidal-cycle phase`, `Carry: regrab-while-loose`, `Reconnect: grace window`,
`Netcode: anti-cheat`) came up, so there is no flake call to make and no entry to add to that
file. Note the rule's own warning applies in the other direction too: **a full-house pass is not
the expected result**, so this is one green marathon, not proof the tree is deterministic.

The three suites criterion 7 names are rows in it and are also quoted standalone above:
`Egg: puffin lab (EGG-1) PASS`, `Watcher: night gate PASS`, `Bubbles: shared counter PASS`
(the summary row `Run-BubbleSyncTest.ps1` reports under), plus `World: bubble test (BT-0) PASS`,
which is the suite carrying the new census and reach checks.

---

## The bible check

```
Bibles applied:  LEVEL (this arranges things into a playable space — it is placement, and the
                 only kind of change it makes to the level is where collectibles are);
                 MECHANICS (the bubbles are a counted, networked, resettable tally with a
                 shared id space, so the objective's own resolution is in scope).
                 Not BEHAVIOR — nothing placed moves or acts. Not INTERACTION — the pickup
                 contract is untouched; a bubble is popped by walking into it exactly as
                 before. Not THRILL — no affect change was directed and none was invented.

Items checked:   LEVEL-BIBLE §4 path_topology — the level's routes are UNCHANGED. Nothing was
                 added or removed that a player walks on; only what they collect while walking
                 moved. The one topology fact this packet turned up is a pre-existing one
                 (RoomE has no inbound route while the lab is present) and it constrained the
                 placement rather than being changed by it.
                 LEVEL-BIBLE §composition — cyan keeps the heaviest share by design (§11's
                 reason: bubbles read against cyan); the tangle keeps twelve because its number
                 is a stair, not a density; green keeps its five stepping-stone bubbles because
                 that five is the route-marking D8 asked for. The reduction thins clusters and
                 leaves every reading intact.
                 LEVEL-BIBLE §62 anti_unwinnable_guarantee (required, not optional; the same
                 answer as MECHANICS §10.5) — THE LOAD-BEARING ONE. "Collect all the bubbles"
                 is the level's one objective, so an uncollectable bubble is an unwinnable
                 state. Three things were done rather than asserted: RoomE gets zero because
                 nothing reaches it; the target is composed from what was actually adopted, so
                 a build without the optional lab advertises 90 and not 100; and all 25 indoor
                 bubbles are measured reachable every run, with three planted controls that
                 must be rejected first.
                 MECHANICS-BIBLE §4 idempotency — untouched and re-verified rather than
                 assumed. Pops still go through BubbleCounter.ServerPop's TryPop, which takes
                 the false branch on any repeat; this packet added and removed authored nodes
                 and changed no pop path. Run-BubbleSyncTest still passes.
                 MECHANICS-BIBLE late-join / the id space — ids are the index of an ordinal
                 node-path sort. "PuffinLab" sorts between "Hub" and "RedCairns", so admitting
                 it renumbers red, the tangle and the TV room. Every peer runs the same build
                 and derives the same ids, which is the assumption the seven sections already
                 rested on; the late-join bitset is 512 wide and 100 < 512 with room to spare.
                 The id map is now printed by the census on every run rather than maintained by
                 hand in a document, which is how the old one went stale.

Result:          PASS. The one thing that would have failed it — a fixed 100 that counted lab
                 bubbles in a build with no lab — is what resolution (a) exists to prevent, and
                 it is demonstrated with the scene moved aside rather than argued.
```

---

## Open questions, ripe

1. **RoomE is a dead room while the lab is present.** It has a television, a couch, a lamp, three
   picture frames, a golden cube and a way out, and nothing in the level goes to it. Either
   `HiddenTv` should stop being the lab's entrance and a seventh route should carry the lab, or
   RoomE should be retired. This is EGG-2's decision, not a level-placement one — but it is a whole
   authored room nobody can see.
2. **`BubbleTarget` wants renaming** (Correction 4). It now means "the level without its optional
   room", which is not what the name says. The rename and the `TheBubbleSplitAddsUpToTheTarget`
   fix are one change in a file this role cannot write.

## Explicitly not done

The secret TV room's reward and the PuffinLab roof's "special thing" — no content invented for
either; the lab's black room got one ordinary bubble beside the stool and nothing else. The
PuffinLab roof route is untouched: every lab bubble is *inside* the lab's interior, the highest at
root y = 1.0 against a ceiling underside at 7.176, and none of them has a collision layer at all.
No bubble is on or above the roof. The sunken TV's material and the artifact under it (SHADER-2's)
were not touched, and neither were the bike, the honk, the UI, the copy or any `render_priority`.
