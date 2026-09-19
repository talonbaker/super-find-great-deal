---
packet: SHADER-1
role: programming
date: 2026-09-04
branch: fix/2026-09-04-shader-1-bubble-over-water
base: playtest/2026-09-04-combined
type: report
---

# SHADER-1 — bubbles stay visible where they overlap the water

**Diagnosis, one sentence:** `resources/materials/bubbletest/bubble_film.tres:63` and
`resources/materials/bubbletest/lake_murk.tres:19` were both `render_priority = 0`, so Godot's
alpha queue fell through to depth-to-sorting-point, and because a `MeshInstance3D` sorts at its
AABB centre by default while green's lake is ONE 33.9 m mesh, every bubble further from the camera
than the lake's single centre point was blended BEFORE the water and then washed out by it.

**Fix:** one line — `bubble_film.tres` `render_priority` `0 → 1`.

---

## 1. Reproduction, first, on the unfixed tree

`docs/qa/SHADER-1/before/lake-se/Sh1Cap-18s.png`. Camera `-83,9,13` looking at `-102,0.5,-3`,
noon, `--cycle-freeze`, `--capture-cam`, capture at t = 18 s. Crop (520,320)–(650,410):

- One bubble is drawn **in full where its silhouette crosses the grassy bank** and stops dead
  along a line that is **exactly the shoreline**; below the waterline only a trace of rim survives.
- The bubble under it is the same cut from the other side: its top half (over water) is gone, its
  bottom half (over the rock island) is drawn.

That is the whole bug in one frame, and it answers the packet's questions:

| question | answer, from the captures |
|---|---|
| does it depend on viewing angle? | **Yes.** Which bubbles are cut changes with the camera — `lake-se` and `lake-nw` are the same lake from opposite banks and neither loses the same ones. What decides it is whether a bubble is nearer or further than the lake's single sorting point, and moving the camera moves that boundary through the crowd. |
| on distance? | Only through the angle — the boundary is the lake's AABB centre, not a fixed range. |
| above/below the waterline? | It is not about the bubble's height in the water. The `lake-se` bubbles are 1–2 m ABOVE the surface and are still cut, and the cut follows the WATER'S silhouette on screen: a single bubble is drawn against grass and gone against water in the same frame, along the shoreline. |
| night vs day? | **Both.** `lake-night` (midnight, flashlight on) gains two whole bubbles after the fix. The film's night glow was not what was hiding them. |
| bubble id? | Not id-specific. Nothing in the fix or the fault reads an id. |

## 2. Mechanism, with evidence

Both surfaces are in the transparent queue and **neither writes depth**:
`resources/shaders/iridescent/bubble_film.gdshader:2` is `depth_draw_never`;
`resources/shaders/lake_murk.gdshader:212` is `depth_draw_opaque`, which on a transparent material
means it does not write depth either — that shader's own header says so at lines 196–198. So
nothing here can be depth-culled, and the only thing that decides the outcome is **which surface is
blended second**.

Godot sorts the alpha queue by `render_priority` first and by depth to the object's sorting point
second, and `GeometryInstance3D.sorting_use_aabb_center` defaults to true — so the entire lake
sorts at ONE point, its AABB centre. `WaterGeometry.BubbleTestLakeRadiusM` records that mesh
measured in engine: world AABB `(−111.96, −0.58, −16.96) .. (−78.04, −0.58, 16.96)`, i.e. a 33.9 m
disc centred on `(−95, −0.58, 0)`. The bubbles are strung across it. A per-object sort cannot
resolve a 34 m plane against objects distributed over it; whichever side of that single point a
bubble falls on decides whether it is drawn before or after the water.

It reads as *invisible* rather than *dim* because `bubble_film.gdshader`'s `alpha_center` is
`0.06`: 6 % coverage under a near-opaque murk composite is nothing. The rim (`alpha_edge` 0.85) is
what leaves the faint trace.

**Two causes, ranked?** No — one. Blend order is sufficient and depth is provably not involved
(neither material writes it). The bubble film's very low centre alpha is an *amplifier*, not a
second cause: it is why the survivor is a rim trace instead of a visible-but-washed bubble.

## 3. The change

| file | field | was | now |
|---|---|---|---|
| `resources/materials/bubbletest/bubble_film.tres` | `render_priority` | `0` | `1` |

Nothing else. No shader source, no `.tscn`, no C#, no other material. Reverting the one field
restores the old behaviour exactly. The reasoning is written into the file itself and into
`DECISION-LOG.md` §6.

**Why the bubble and not the water.** `render_priority = -1` on `lake_murk.tres` fixes the same
frame, but that material is *also* `GreenHills.tscn`'s `SunkenTvPlinth/Column`, which is genuinely
under the surface and must stay murked — pushing the water earlier would leave the plinth
compositing on top of the lake it is sunk in. `bubble_film.tres` is loaded by exactly one scene
(`scenes/game/props/Bubble.tscn`) and nothing else; `IridescentProps.cs` loads the *shader* by path
and never this material, so the scramble course is untouched. `BubbleCounter.cs:399` hands every
bubble a `Duplicate()` of the authored material and `Duplicate()` carries `render_priority`, so one
file covers all 112 bubbles.

**What it costs.** A bubble now composites over the lake even where it is slightly *under* the
surface — `Bubble_Green_16` sits at y −0.65 against `WaterGeometry.WaterY` −0.58, 7 cm down — so
that one loses its murk tint. Accepted: the packet's own rule is that bubbles are the objective and
visibility beats subtlety. It costs **nothing in occlusion**: `render_priority` only reorders inside
the transparent queue, the depth TEST is untouched and opaque geometry still writes depth, so a
bubble behind a wall is still behind the wall. `SecretBubble` builds its own `StandardMaterial3D`
in code and was deliberately left alone — it hangs 0.77 m under the surface and is meant to read as
a light UNDER the water. The water shader was not touched at all: not made opaque, not disabled,
not retuned.

## 4. Neighbours — measured, not eyeballed

Ten framings, each shot twice from the SAME camera at the SAME frozen cycle phase and the same
capture second: once with `render_priority = 0` and once with `1`. Index and per-shot numbers in
`docs/qa/SHADER-1/README.md`.

| shot | mean abs Δ | pixels moving > 8/255 |
|---|---|---|
| `lake-se` (the repro) | 0.486 | 1.78 % |
| `lake-nw` (mirrored bank) | 1.330 | 3.27 % |
| `lake-east` | 0.306 | 1.20 % |
| `lake-west` | 2.502 | 3.86 % |
| `lake-night` (midnight + flashlight) | 0.035 | 0.05 % |
| **`moat`** (blue tower's water + golden cubes) | 0.003 | **0.00 %** — 20 px of 921 600 |
| **`sunken`** (murk read + the sixth television) | 0.048 | 0.13 % |
| **`secret`** (`SecretBubble`, midnight) | 0.032 | 0.03 % |
| **`cubes`** (the hub's golden cubes) | 0.012 | 0.02 % |
| **`tvroom`** (television room A) | 0.076 | 0.13 % |

The four lake shots move; the five neighbours do not. Their residue is the bubbles' own
`BubbleOscillation` wobble, which is time-driven and never lands on the identical phase twice.
Visually: the murk, the shoreline, the sunken plinth and its antennas, the moat, the cubes and
room A's television are unchanged frame to frame. In `secret` and `lake-night` the only visible
difference is **extra bubbles appearing**, which is the fix.

## 5. ABSENCE check, with its positive control

**Claim:** no bubble moved, none was added or removed, and no id changed.

The probe reads the shipped section `.tscn` files with no engine, applies `BubbleTest.tscn`'s
section anchors to get world positions, and sorts by node path — which is `BubbleCounter`'s own id
rule (`AdoptAuthored` walks the world and sorts by node path). It prints one line per bubble and a
SHA-256 of the whole listing.

```
this branch (render_priority = 1):  count = 112  digest = 245f40d3a4de1d0a23f7143bd2b0773e3cffa18b6db3f8f7de7f180636c1cfae
base playtest/2026-09-04-combined:  count = 112  digest = 245f40d3a4de1d0a23f7143bd2b0773e3cffa18b6db3f8f7de7f180636c1cfae
```

**Positive control — the probe can catch a moved bubble.** In a scratch copy of the sections,
`Bubble_Green_16` was moved 0.50 m up (`position = Vector3(-5, -0.65, -14)` →
`Vector3(-5, -0.15, -14)`) and nothing else:

```
count = 112  digest = 27aeed025c70214d0fdceb0ee799e713cd830f31bf2443c7c88bc3d3fc8c0a5e   (differs)
diff:  063  GreenHills/Bubbles/Bubble_Green_16   (-100.000, -0.650, -14.000)
    →  063  GreenHills/Bubbles/Bubble_Green_16   (-100.000, -0.150, -14.000)
```

The count is an independent cross-check as well: **112 authored bubbles is exactly
`BubbleTestLayout.BubbleTarget`**, which `BubbleTestLayoutTests.TheBubbleSplitAddsUpToTheTarget`
pins from the other side. The probe names the bubble, the section and the delta. The scratch copy lives outside the repo and
nothing was committed from it.

`BubbleCounterState.Capacity` is still `512` (`EncodedBytes` = 64) — untouched source. The bubble
tests run unmodified: no file under `tests/unit/` was edited by this packet.

## 6. Suites

Measured on this branch tip. Baseline measured **in a throwaway detached worktree** at the same
base commit (`36724b4`, `C:\repos\Watis-shader1-base`), not inherited from any report.

**Build.** `dotnet build WatisWorld.sln` — **Build succeeded. 0 Errors, 4 Warnings** (all four
pre-existing: one `CS0618` and three nullable warnings under `scripts/game/sandbox/`).

**xUnit.** `dotnet test tests/unit/SailNet.Tests.csproj`

```
branch tip:  Failed: 0, Passed: 2223, Skipped: 0, Total: 2223   (Duration 3 s)
baseline:    Failed: 0, Passed: 2223, Skipped: 0, Total: 2223   (C:/repos/Watis-shader1-base, detached at 36724b4)
```

Identical. No `tests/unit/` file was touched.

**Scene marathon.** `powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`, foreground,
redirected to a file. Counted from the run's own `=== summary ===` block with `^  .+ (PASS|FAIL)$`:

```
46 suites: 45 PASS / 1 FAIL      OVERALL: FAIL
```

**The one red is `Netcode: anti-cheat`, and it is the flake this repo has already named.**
`.claude/rules/test-suite.md` lists it under "Load-flaky suites" with the exact signature it
printed: *"Netcode: anti-cheat (`the grab never landed`)"*. The failing line was
`scenario 2: prop 1 was never observed held by CheatCarrier — the grab never landed` — a STAGING
failure in the second scenario, not the containment assertion the suite exists for. Scenario 1
passed inside the marathon and its raw quantity is the discriminator:

| run | machine state | peak 1.00 s-window speed | displacement / samples | verdict |
|---|---|---|---|---|
| marathon | loaded (this suite queued 20 min behind another agent's full run) | 3.80 m/s | 15.2 m / 59 | FAIL (staging, scenario 2) |
| standalone 1 | idle | 3.81 m/s | 15.2 m / 58 | PASS |
| standalone 2 | idle | 3.80 m/s | 15.3 m / 59 | PASS |
| standalone 3 | idle | 3.80 m/s | 15.2 m / 58 | PASS |

`powershell -File tests/Run-CheatTest.ps1` **3/3 PASS on an idle machine**, and the measured
quantity is the same to within 0.01 m/s in all four runs — the containment behaviour never moved.
Nothing in this packet touches netcode, carry, props or the motor: the diff is one
`render_priority` field in one `.tres`. Flake, not a regression.

**`Run-BubbleTestWorldTest` specifically** (the packet asks for it by name):
`powershell -File tests/Run-BubbleTestWorldTest.ps1` → `BUBBLE-TEST WORLD TEST OVERALL: PASS`,
with the collider audit's positive control reporting its expected failure. CheckBake's
packed-vs-live counts, every section equal:

```
Hub            packed[mesh=69  shape=41  body=24 ]  live[mesh=69  shape=41  body=24 ]  OK
RedCairns      packed[mesh=167 shape=164 body=148]  live[mesh=167 shape=164 body=148]  OK
BluePrecision  packed[mesh=197 shape=116 body=101]  live[mesh=197 shape=116 body=101]  OK
CyanRun        packed[mesh=114 shape=85  body=52 ]  live[mesh=114 shape=85  body=52 ]  OK
GreenHills     packed[mesh=30  shape=59  body=12 ]  live[mesh=30  shape=59  body=12 ]  OK
Tangle         packed[mesh=197 shape=196 body=184]  live[mesh=197 shape=196 body=184]  OK
TvRoom         packed[mesh=485 shape=59  body=50 ]  live[mesh=485 shape=59  body=50 ]  OK
```

Every other bubble-bearing suite in the marathon was green: `World: bubble test (BT-0)`,
`Bubbles: shared counter`, `World: TV portal (BT-10)`, `Water: lake contract (W2)`,
`Water: splash VFX + audio (W4)`, `Egg: puffin lab (EGG-1)`, `Watcher: night gate`.

## 7. New file

`tests/Run-Shader1Capture.ps1` — the capture harness that produced everything in
`docs/qa/SHADER-1/`. `--capture-cam` on every shot; the bot is given no `--goto-script`, so it
stands at its hub spawn and cannot walk into a bubble and collect it out of frame. **Deliberately
NOT registered in `Run-AllTests.ps1`** — it produces frames a human looks at, not a verdict. Same
argument, and the same shape, as `Run-EggCapture.ps1`.

## 8. Bible check

```
Bibles applied:  none — and here is why.
Items checked:   n/a
Result:          n/a
```

This is not a gameplay feature. It changes one render-ordering field on one material: no state
machine, no autonomous entity, no new thing the player acts on, no composition change, and no
affect target. `INTERACTION-BIBLE` was considered and does not bite — the bubble's collider,
reach, ids and pop path are all untouched; what changed is only whether the same bubble is drawn
where it already was. The nearest applicable standing rule is `ART-BIBLE`'s, which belongs to
art-atmosphere; nothing here re-grades the water, and the water shader was not opened.

## Corrections to the packet

1. **The packet's suggested repro framing needed replacing, and the reason is worth recording.**
   The obvious camera — high, looking down the lake's long axis — is the worst possible one: the
   rock island sits in the middle of green's lake and hides the far half of the bubble line, so an
   east/west pair differs for a geometric reason that has nothing to do with shaders, and the sun
   glitter differs too. Both are confounds a reviewer would be right to reject. The framing that
   actually proves it is oblique, from the south-east, close enough that a SINGLE bubble straddles
   the shoreline: then the cut is inside one object, under one lighting condition, and no
   alternative story survives. `lake-east` / `lake-west` are kept in the index for completeness and
   are explicitly **not** the evidence.

2. **"Two RenderingServer read-back APIs are editor-only… the RENDER is the only witness" applied
   here too, and harder than expected.** There is no read-back for transparent sort order at all —
   not even an editor-only one. The only witness available is an A/B render of the same camera
   across the one changed field, which is what section 4 is.

3. **`powershell -File script.ps1 -Only a,b` hands PowerShell 5.1 ONE string `"a,b"`, not a
   two-element array.** The first run of the new capture script silently selected zero shots and
   still printed its success line. `Run-Shader1Capture.ps1` now re-splits every element on commas
   and fails loudly when the filter matches nothing. Worth knowing for any future runner that takes
   a list parameter under `-File`.

## Open questions

None ripe. One value call taken and stated rather than escalated: `render_priority = 1` on the
bubble rather than `-1` on the water — reasoning in §3, and it is one field to flip if Talon wants
the other side of it.

## Out of scope, confirmed untouched

No gameplay, collision or physics change. No bubble added, removed or moved; no id moved
(§5). No UI. No new asset. The water's look was not retuned — `lake_murk.tres` and
`lake_murk.gdshader` are byte-identical to base.
