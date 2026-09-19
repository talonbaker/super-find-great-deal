---
type: finding
packet: LD-3
role: environment
date: 2026-09-02
branch: docs/2026-09-02-ld-3-cadence-and-weenie-audit
base: 99eb3299
implements: A4-R1, A4-R2
---

> **PORTED FROM `Sail` BY LEVEL-1 (2026-09-04).** Every number below was **measured on `Sail`
> master `583ecc02`, before EGG-2** — that is, against the bubble test as it stood with none of
> the easter-egg wave's geometry in it. EGG-2 subsequently edited four of the six sections
> (`BluePrecision`, `CyanRun`, `GreenHills`, `TvRoom`), so these tables describe a level that no
> longer exists in this repository. They are kept as the **baseline** the re-audit is diffed
> against — `docs/levels/2026-09-04-LEVEL-1-cadence-reaudit.md` is the same measurement taken on
> the combined tip. Read a number here as "before", never as "now".

# LD-3 — the weenie chain: is there a chain, or five islands?

Research A4 (`docs/research/2026-09-02-watis-level-design-world-reactivity-comedy-audio-RESEARCH-RESULT.md`):
a destination is desirable from a distance when it is visible from a common vantage, legibly
climbable at that distance, paid at the top, has one guaranteed line, and is **chained** — from
its top the next weenie is visible. This document answers only the chain question, for the six
points the project has already validated as weenies: the five television summits of
`BubbleTestLayout.TvRoutes` and the hub spawn ring.

Measurement only. No `.tscn`, no `.cs`, no `scripts/game/world/bubbletest/**` — see
`docs/agents/roles/environment/outbox/2026-09-02-LD-3-report.md` for the one exception (below).

## Method

**The six points**, both as vantages and as targets, body 1.20 m / eye 1.10 m: `HubTv` on the
plaza, `RedTv` on the cairn perch, `HiddenTv` on the spire's spur pad, `BlueTv` on the tower
perch, `TangleTv` on the summit cap, and the hub spawn ring.

**Two stances per vantage**, because one alone lies about looking down: *at the return point*
— the eye where the level actually puts a player back on that summit (the television's own
entrance position plus `BubbleTestLayout.RoomReturnOffset` in plan) — and *best on the pad* —
the same eye moved to whichever of up to ten standing spots on that pad (the return point, the
pad's centre, eight points 0.4 m inside its edge) clears the most rays, skipping any spot inside
a block. The spawn ring's stances are its own six markers plus centre.

**Five target rays per cell**, to a silhouette rather than a point: the target's eye; 0.6 m above
and below it (a television is ~1.5 m tall, a player 1.2 m); 1.5 m either side, perpendicular to
the sightline in plan (never along it, or the far pad's own lip would count as occluding the
thing standing on it). A cell reports `clear/5`; ≥ 1 is a link.

**What occludes:** every collider `tools/dev/ld3_tscn.py` reads out of the six section files at
the anchors `BubbleTest.tscn` actually instances them — 529 boxes, cylinders and boulder hulls
(as AABBs) — plus the five television cabinets, runtime-built (`TvPortal.cs`: a solid
1.0 × 0.85 × 0.55 box, 0.62 m off the ground) and added here as boxes so a stance cannot look
through the set it stands beside. Not read: `GreenHills`' terrain (no summit line crosses it —
every point on these six lines sits at x ≥ −22, the terrain lies west of x = −50), and the
counter board.

**The tool:** `tools/dev/ld3_weenie_chain.py` (now 325 lines — a correction below added 5). Reads
`tools/dev/ld3_tscn.py`'s output, no engine, no clock, no randomness.

```
python tools/dev/ld3_weenie_chain.py            # both matrices + the link table
python tools/dev/ld3_weenie_chain.py --rays     # ... plus every blocked ray's first occluder
python tools/dev/ld3_weenie_chain.py --cams     # the capture-camera list, tag,ox,oy,oz,tx,ty,tz
```

## Reproducibility

Stdlib only; two consecutive runs against this tree, **after** the correction below:

```
run 1: e688d7321a6d5f50c40b211e90337571c54bb156f9460339368dda2f38150aa3
run 2: e688d7321a6d5f50c40b211e90337571c54bb156f9460339368dda2f38150aa3
```

identical, byte for byte. (The script's own internal hash, printed as its last line and covering
only the rendered table rather than this whole run's stdout capture, is
`65ed486bcf4ded1d2ee7bc87d081d45ef2e88977c6a7d228538ae191d78b1243` — quoted separately because
it is what a future diff against this document should match.)

## A correction made in-flight: the pad self-occlusion bug

The script this packet inherited excluded a target's television **cabinet** from occluding
itself but not the **pad it stands on**. Because the eye is lifted only 1.10 m above that pad's
own top, a ray to a target sitting directly over it registered a grazing hit on the pad itself in
5 of 30 cells — a false `BLOCKED`, three of them (`spawn→tangle`, `hub→tangle`, `red→tangle`) at
100% of their five rays and total misreads. The captures under `docs/qa/LD-3/` caught this before
the correction did: every one of those cells' frames shows the target television's cabinet
plainly on-screen, in daylight, against open sky.

**How it was found.** `--rays` prints the first occluder on every blocked ray. Every ray in the
five affected cells names the same body: `Tangle/Tower/Summit` (four cells) or
`RedCairns/TvPerch` (two of `hub→red`'s five rays; the other three name real cairn structure —
see below). Both are the `PAD_OF` collider the script already knows about for stance placement,
just never excluded from occlusion the way the cabinet is.

**The fix** (`tools/dev/ld3_weenie_chain.py`, `compute()` and `first_hit()`): `ignore` is now a
set of `section/path` keys rather than a single bare path, and `compute()` adds the target's own
`PAD_OF` collider to it alongside its cabinet. This is a tool correction inside `tools/dev/**`
(this role's own path); it touches no `.tscn`, no `scripts/`, no `tests/` — criterion 6 is
unaffected. Before/after, the cells it moved:

| cell | before (best) | after (best) | occluder removed |
|---|---|---|---|
| spawn → tangle | 0/5 BLOCKED | **5/5 VISIBLE** | `Tangle/Tower/Summit`, all 5 rays |
| hub → tangle | 0/5 BLOCKED | **5/5 VISIBLE** | `Tangle/Tower/Summit`, all 5 rays |
| red → tangle | 0/5 BLOCKED | **5/5 VISIBLE** | `Tangle/Tower/Summit`, all 5 rays |
| blue → tangle | 1/5 MARGINAL | **5/5 VISIBLE** | `Tangle/Tower/Summit`, 4 of 5 rays |
| hub → red | 0/5 BLOCKED | **2/5 MARGINAL** | `RedCairns/TvPerch`, 2 of 5 rays (3 real: `Cairn_Main/StackBlock17`, `StackBlock19` ×2 — hub→red stays genuinely marginal) |
| spawn → spur, hub → spur, red → spur | 2/5, 2/5, 4/5 | **5/5, 5/5, 5/5** | `Tangle/Tower/SpurPad`, the rays each was missing |
| spawn → blue | 2/5 | 4/5 | (a stance-search side effect of the same fix, not `BluePrecision/TvPerch` itself) |

`tangle ↔ spur` is unaffected by the correction and is discussed on its own below — it is real.

All numbers in this document, and both matrices, are **post-correction**. The `--rays` and
`--cams` output regenerate from the corrected script; a `--cams` re-run would move the camera for
`hub→red` and `spawn→blue` (their best stance changed), which the existing captures below predate
— noted per cell.

## A. computed — at the return point (the eye where the level puts a player back on a summit)

```
from \ to      spawn      hub      red     spur     blue   tangle
spawn             --      5/5      2/5      5/5      3/5      5/5
hub              5/5       --      1/5      5/5      1/5      5/5
red              5/5      5/5       --      5/5      5/5      5/5
spur             5/5      0/5      5/5       --      5/5      0/5
blue             0/5      0/5      5/5      0/5       --      5/5
tangle           0/5      0/5      0/5      0/5      5/5       --
```

## B. computed — best stance on the pad (the honest "can you see it from up there")

```
from \ to      spawn      hub      red     spur     blue   tangle
spawn             --      5/5      2/5      5/5      4/5      5/5
hub              5/5       --      2/5      5/5      2/5      5/5
red              5/5      5/5       --      5/5      5/5      5/5
spur             5/5      5/5      5/5       --      5/5      0/5
blue             5/5      5/5      5/5      5/5       --      5/5
tangle           5/5      5/5      5/5      0/5      5/5       --
```

**Reading B (the chain question):** of 30 directed pairs, **29 are a link** (≥ 1/5) and 23 are
fully clear (5/5). The **only** blocked pair in the matrix, either direction, is
**`spur ↔ tangle`** — and spur (`HiddenTv`) is structurally a dead-end branch off the tangle
climb itself (tread 12 of `Tower`'s stair, per the cadence audit), reached only by a player
already partway up the tangle line, not a separately-discovered distant destination the way the
other four summits are. **This is a chain, not five islands** — the finding research A4 asked
this document to settle.

## B (seen) — ≥ 6 captures, computed vs seen

30 captures under `docs/qa/LD-3/from-<vantage>-to-<target>/` (every ordered pair) plus 6 contact
sheets (`sheet-from-<vantage>.png`), taken with `tools/dev/ld3_capture.ps1` at the best-stance eye
from the pre-correction `--cams` list, `--capture-cam` (never a bot's own follow camera), midday
(phase 0.25, sightline shots not lighting shots). Each pair directory holds the full 1920×1080
frame (`LD3-5s.png`) and a 1280×720 centre crop with a yellow reticle on the target's exact eye
point (`LD3-5s-centre.png`, `tools/dev/ld3_crops.py`).

Spot-checked against the corrected matrix (viewed directly, not scraped):

| pair | computed (B, corrected) | seen | agreement |
|---|---|---|---|
| `spawn → tangle` | 5/5 VISIBLE | cabinet fully on-screen at the crosshair, clean sky behind | agree |
| `hub → tangle` | 5/5 VISIBLE | cabinet fully on-screen at the crosshair, clean sky behind | agree |
| `red → tangle` | 5/5 VISIBLE | cabinet fully on-screen at the crosshair, clean sky behind | agree |
| `tangle → red` | 5/5 VISIBLE | cabinet clearly visible past the rubble in frame | agree |
| `red → blue` | 5/5 VISIBLE | cabinet clearly on-screen atop the tower | agree |
| `blue → tangle` | 5/5 VISIBLE | cabinet mostly clear; two spire antenna rods cross in front but do not cover it | agree |
| `hub → red` | 2/5 MARGINAL | a thin sliver of the red set's edge visible past the cairn mass at the crosshair | agree (marginal, not clean) |
| `tangle → spur` | 0/5 BLOCKED | crosshair sits on solid rubble/spire mass, target not visible | agree |
| `spur → tangle` | 0/5 BLOCKED | crosshair sits on solid rubble mass, target not visible | agree |

**Disagreements, pre-correction:** the four `→ tangle` rows above (`spawn`, `hub`, `red`, `blue`)
and `hub → red` were all computed `BLOCKED` before the fix and visibly are not `BLOCKED` in the
captures — this is exactly the discrepancy that led to the pad self-occlusion bug above, and none
remain after the correction. **Disagreements, post-correction: none** in the 9 cells checked
directly against a capture.

**A caveat, stated rather than hidden:** the corrected script's best-stance search moved for
`hub → red` (`return` → `edge2`) and `spawn → blue` (`Spawn3` → `Spawn0`); the shipped captures
for those two cells were taken from the pre-correction stance, a few tenths of a metre away on
the same pad, not the exact corrected one. Both still answer the coarse visible/marginal
question above (confirmed for `hub → red`); a `--cams` re-run and a fresh capture pass would be
needed to frame those two exactly as B now specifies. **UNVERIFIED**: the exact framing of those
two cells only, not the verdict.

## Findings, ripe

**The chain holds.** Of the five summits plus the hub, every pair but one sees the other from
somewhere on its own pad. Research A4's chain requirement — from a summit's top, the next
weenie is visible — is satisfied project-wide already, without building anything: a player atop
any of the four scored summits (red, blue, tangle, and spur once reached) can see at least the
hub and usually two or three of the others.

**The one missing link: `spur ↔ tangle`.** Both directions are a real, physical block — the
tangle spire's own bulk (`Tangle/Tower/Jumble23` looking down from spur; `Tangle/Tower/Summit`
itself, at a −57.5° depression angle, looking down from the summit cap) — not an artifact.
Whether it needs a fix at all is itself the open question: `HiddenTv` is discovered by a player
already committed to the tangle climb (it branches off tread 12, per the cadence audit), not
found from a distance the way the other four summits are, so the A4 "visible from a common
vantage" test may not be the right lens for it. **Cheapest fix, if wanted:** a beacon or emissive
tell on the spur pad, sized to clear the spire's own silhouette from the summit cap's `return`
stance (the steepest, closest angle) rather than notching the spire's mass — cheaper than
geometry and does not touch the climb. **What playing it would answer:** whether a player who
takes the spur branch expects to then spot the summit cap (in which case build the beacon) or
experiences the spur as a self-contained side room that does not need to chain onward (in which
case leave it — the level already forces the player past it on the way to the real summit).

**Feeds A2-R2 (the ring).** With the chain question settled positively, the next environment
packet's job is verifying the *approach* to each weenie reads at range (A4's "legibly climbable"
and "one guaranteed line" tests), not building new inter-visibility — this document found nothing
here that blocks that work.

## The "call it" protocol (A4-R2)

Added to `docs/playtest/INTAKE.md` — see that file. Summary: before touching a climbable
structure, the player calls aloud whether they can reach the top and by which side; a structure
most players call wrong is a legibility defect in the kit, not a skill gap.

## Bibles applied

`Bibles applied:` **`LEVEL-BIBLE.md`** — composition, sightlines and wayfinding legibility, per
the packet's own inbox.
`Items checked:` **§8.1** (whether the level's own state — here, "which destinations exist and
are they reachable" — is perceivable without UI; generalised from its pre-pivot framing around
threat/time state to destination legibility, which is the only part of §8 that survives the
pivot intact) and **§4.2** (verticality as a composition primitive; the one real block found,
`spur ↔ tangle`, is exactly the "descending is cheap, ascending is expensive" asymmetry reading
back on itself — the spur is reached mid-climb rather than approached fresh, so it never needed
its own distant silhouette the way a summit does).
`Result:` **pass.** The chain exists; the one gap has a named cheapest fix and a stated question
for playing it, not a build.

## Corrections to the packet

The pad self-occlusion bug above, and — not specific to this document — the removal of five
foreign `.uid` sidecar files the inherited WIP commit swept in outside this role's paths; see
the LD-3 outbox report for the full account.
