# BUBBLE-1 — one hundred bubbles, and some of them indoors

**2026-09-04.** Talon, straight off a live playtest he called delightful:

> "There's one comment I want to make about the bubbles overall I would like there to be only 100
> bubbles. There are too many bubbles. And there are not any bubbles at all in most of the TV areas
> and in the puffing lab scene that was recently added. There are absolutely no bubbles, which is a
> shame because it's a really cool place. Therefore I would like some bubbles to be moved from the
> outside to inside of some of these scenes. ... I only want there to be 100 bubbles in total by the
> way."

> "please reduce the number of bubbles overall to 100 and further reduce the number of bubbles on
> the top maps and put them into some of these additional spaces to make it more exciting more fun."

This supersedes the **counts** in [`bubble-test-bubbles.md`](bubble-test-bubbles.md) (BT-11's
record). That document's per-bubble id table is now stale and is marked so at its head; its
*reasoning* — why the tangle pays only for its climb, why cyan cannot hide a bubble, the 0.50 m
clearance bound — still stands and is what the cuts below were made against.

---

## 1. Where the 112 were, and the seven nobody could name from the constants

The seven bubbles the named per-section constants did not cover are **`SeamBubbles = 7`** — bubbles
on the four connecting paths, which belong to no section and are authored in `Hub.tscn` because the
connectors are. 105 named + 7 seam = 112. There is no eighth mystery: `SecretBubble` is **not** one
of them and never was. It is a different class with no common base, so
`BubbleCounter.AdoptAuthoredBubbles` — which collects by TYPE — cannot see it, and
`BubbleTestSelfTest.CheckSecretBubble` measures that census before and after popping it rather than
asserting it in a comment.

**But the constants were not the level.** Counted out of the shipped `.tscn` files, BT-11 placed:

| Section | D8 constant | actually placed | drift |
|---|---|---|---|
| CyanRun | 35 | **33** | −2 |
| RedCairns | 15 | **16** | +1 |
| GreenHills | 15 | **16** | +1 |
| BluePrecision | 15 | 15 | 0 |
| Hub | 10 | 10 | 0 |
| Seam | 7 | 7 | 0 |
| Tangle | 12 | 12 | 0 |
| TvRoom | 3 | 3 | 0 |
| **Total** | **112** | **112** | **0** |

**This was disclosed, not hidden** — all three deviations are inside `BubbleSplitTolerance` (±5) and
all three are written down in BT-11's own counts table. What was missing was any *instrument*: the
only check on the split was `TheBubbleSplitAddsUpToTheTarget`, which compares the constants to each
other and never opens a scene. A split that adds up correctly and describes no level in particular
is arithmetic. **`BubbleTestSelfTest.CheckBubbleCensus` is new in this packet** and walks the live
tree, so every number in the "after" column below is measured on every run.

---

## 2. The rebalance, section by section

Every section is now placed **exactly** on its constant — the drift above is zero everywhere.

| Section | before | after | Δ | why |
|---|---|---|---|---|
| **CyanRun** | 33 | **18** | −15 | The biggest share and the loudest of it. Seven bubbles evenly spaced 10 m apart down x = −30 and five down x = +30 read as a *corridor of markers* rather than a run with bubbles in it; both lanes go to roughly half spacing (3 and 2), so the LINE still reads at a third of the count. The three scatters lose their nearest neighbours only. Cyan stays the densest section by a clear margin, which `CyanCarriesTheDensestBubbles` pins. |
| **RedCairns** | 16 | **11** | −5 | The 23.6 m summit bubble beside `RedTv` and the 10.55 m mid-climb one stay — a climb has to pay. The five cut are all redundant on the flat: a pair 2.8 m apart at the cairn's foot, one stacked directly under another, one in a far corner nothing routes through, one beside its own twin. |
| **BluePrecision** | 15 | **11** | −4 | The 41.9 m summit and the 37.65 m step below it are untouched; blue is a climb and the top of it is the reward. The four cut are from the low approach and the middle of the ladder, where two bubbles were doing one bubble's work. |
| **GreenHills** | 16 | **11** | −5 | **All five stepping-stone bubbles survive** — that five is D8's own instruction and the stones are the one route in green a bubble line is *supposed* to mark. Cut: three from the postpile cluster, where four sat inside a 6 m ball, and two near-lake strays including the y = −0.65 one, which was the least honest placement in the section. |
| **Hub** | 10 | **7** | −3 | The plaza is where a player learns what a bubble is, so the one beside `BubbleCounterDisplay` stays and the three crowding it go. A teaching bubble reads better alone than in a ring of four. |
| **Seam** | 7 | **5** | −2 | One centred on each of the four connectors, and the north path keeps its second because it is the longest run between two sections in the level. |
| **Tangle** | 12 | **12** | **0** | **Deliberately not cut**, and the reason is that the tangle's twelve is not a density. Ten of them ARE the stair, one is the lead-in, one is the summit; there is no scatter here to thin, so a cut would delete treads and take the stair's reading with them — the opposite of what the reduction is for. Twelve was already the lowest outer share before this and is now the second highest, which is the honest cost of leaving it alone. |
| **TvRoom** | 3 | **15** | **+12** | Three each into **RoomB, RoomC, RoomD and RoomF**. All three shipped bubbles sat in the Den; the other five rooms had none between them, which is exactly what Talon reported. |
| **PuffinLab** | 0 | **10** | **+10** | A trail down the whole route: four in the room you arrive in, three down the crawl, two along the hallway, one in the black room beside the stool. The lab is a one-way journey to a television 80 m away and a count that paid only for the first room would pay for none of the journey. |
| **Total** | **112** | **100** | **−12** | |

Outer sections gave up **34**; indoor spaces gained **22**. Net −12.

**Live proof, not the table** — `tests/Run-BubbleTestWorldTest.ps1`, server log:

```
[bubble] adopted 100 bubble(s) (server=True)
[bubbletest-selftest]   census TOTAL          100 bubbles (want 100; lab present, 10)
```

---

## 3. RoomE gets nothing, and that is a reachability fact

`BubbleTestWorld.DestinationFor` overrides `LabRouteEntranceName` — `HiddenTv`, **RoomE's only
television** — to the puffin lab's `Arrival` whenever `PuffinLab.tscn` is in the build. So in every
shipped build **nothing goes to RoomE**. It keeps its way out and has no way in.

A bubble there would be counted and uncollectable at once, which is precisely what turns "collect
all the bubbles" into a lie. The lab is RoomE's replacement and the lab is where those bubbles went
instead. *RoomE having no route while the lab is present is EGG-2's finding, not this packet's to
fix* — it is recorded here because the next person to put something in that room needs to know.

---

## 4. The optional lab, and why the target is composed rather than fixed

`PuffinLab.tscn` is optional: `SetUpPuffinLab` guards on `ResourceLoader.Exists` and the level must
come up without it (EGG-2 acceptance criterion 4). A single fixed `BubbleTarget = 100` that counted
lab bubbles would, in a build with no lab, advertise a total the player can never reach.

**Resolution (a): the target is composed from what was actually adopted.**

- `BubbleTarget = 90` — the seven sections plus the seam. Always present.
- `PuffinLabBubbles = 10` — added only when the lab loaded.
- `BubbleTargetWithLab = 100` — **Talon's hundred**, and what every shipped build carries.

**Nothing player-facing reads either constant.** `HudBubbleCount` and `BubbleCounterDisplay` both
render `BubbleCounter.BubbleCount`, the live adoption count, so an absent lab already yields an
honest smaller total on screen and no code path can lie about it. The two constants exist so a test
can say which case it is in — and `BubbleTestSelfTest` now reads the lab's presence off the **tree**
rather than off `ResourceLoader`, because what the counter adopted is a fact about the tree.

Demonstrated, not asserted — `PuffinLab.tscn` moved aside, same suite:

```
[bubble] adopted 90 bubble(s) (server=True)
[bubbletest-selftest]   census TOTAL           90 bubbles (want 90; lab absent)
[bubbletest-selftest]   puffin lab ABSENT: expecting 90 adopted bubbles (90 + 0 lab)
BUBBLE-TEST WORLD TEST OVERALL: PASS
```

Zero `ERROR` lines in that run, and all 90 are reachable — with the lab gone, `HiddenTv` goes back
to RoomE, which has no bubbles in it.

---

## 5. The id space

Ids are the index of an **ordinal node-path sort** and are a consequence of nothing else.
`"PuffinLab"` falls between `"Hub"` and `"RedCairns"`, so admitting the lab renumbers red, the
tangle and the TV room and nothing before them. Measured, not derived:

| Node path | ids |
|---|---|
| `BluePrecision/Bubbles` | 0 – 10 |
| `CyanRun/Bubbles` | 11 – 28 |
| `GreenHills/Bubbles` | 29 – 39 |
| `Hub/Bubbles` (7 hub, then 5 seam at **47 – 51**) | 40 – 51 |
| `PuffinLab/Bubbles` | 52 – 61 |
| `RedCairns/Bubbles` | 62 – 72 |
| `Tangle/Bubbles` | 73 – 84 |
| `TvRoom/Bubbles` (the Den) | 85 – 87 |
| `TvRoom/RoomB` · `RoomC` · `RoomD` · `RoomF` | 88 – 90 · 91 – 93 · 94 – 96 · 97 – 99 |

Every peer in a session runs the same build and derives the same ids — the same assumption the
seven sections have always rested on. `Run-BubbleSyncTest` is unaffected either way: it drives the
`--bubble-selftest` fixture's own six bubbles, not this level.

---

## 6. Reachability

`BubbleTestSelfTest.CheckIndoorReach` measures all **25** indoor bubbles against the live physics
space, with the LIVE avatar capsule: floor found by a downward ray; the bubble's lowest reachable
point (centre − collider radius − the *worst* phase of the bob) under a standing body's crown; the
standing spot clear; and, where a straight line is genuinely the route, a swept capsule from the
room's arrival point sampled every 0.35 m with a step-height bound so it cannot climb a wall as if
it were a ramp.

Three planted controls must be rejected first, each by the item it violates — 5 cm off a wall
(nothing can stand), over the void between two rooms (no floor), 3.5 m over a floor (out of reach).

**Its honest limit:** it is a physics measurement, not a driven bot. See §"Corrections" in the
BUBBLE-1 report.

It found two real defects during development, both fixed:

- `Bubble_TvB_03` sat 0.45 m from `GoldCube_RoomB_0`, so the walk from the arrival point went
  through the crate. Moved to (1.8, 1.1, 4.3), 4.8 m clear.
- `Bubble_TvD_02` sat 0.92 m from `GoldCube_RoomD_0`. Moved to (5.0, 1.2, −2.5), 2.7 m clear.

---

## 7. Two stale claims found next door, and left alone

- **`GoldenCubes`' doc said "one in each of `TvRoutes`'s five rooms."** There are **six** routes —
  EGG-2 added the sunken television and this sentence was not moved with it. Corrected in place,
  since it is one line in a file this packet already edits.
- **There are only five room golden cubes** (`GoldCube_RoomA_0` … `RoomE_0`); RoomF, EGG-2's Deep
  Room, never got one. Not fixed — golden cubes are not this packet's subject.
- **`GoldenCubes`' doc also says green has no television.** It has one; it is under the lake.
  Left alone for the same reason.
