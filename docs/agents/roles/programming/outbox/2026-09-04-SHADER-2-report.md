---
packet: SHADER-2
role: programming
date: 2026-09-04
branch: fix/2026-09-04-shader-2-sunken-tv-artifact
base: playtest/2026-09-04-combined (418556d)
type: report
---

# SHADER-2 — the boxy shape under the sunken television

**Diagnosis, one sentence, category GEOMETRY (a solid box pointed at the wrong material) rather
than sorting, depth or lighting:** `scenes/game/world/bubbletest/sections/GreenHills.tscn:498`
gave `SunkenTvPlinth/Column/Mesh` — a solid 1.4 × 2.4 × 1.4 `BoxMesh` — `lake_murk.tres`, the
**lake surface** material, as its `material_override`, and `lake_murk.gdshader:212` is
`render_mode blend_mix, depth_draw_opaque, cull_disabled`, so the box's own far faces are drawn
unsorted over its near faces and take the shader's "seen from underneath" branch
(`lake_murk.gdshader:1296-1302`: `ALBEDO = deep_col` (0.0115, 0.0225, 0.0195) at `under_alpha`
0.94) — a hard-edged, near-black, near-opaque rectangle sitting in the murk.

**Fix:** one token — that node's `material_override`, `lake_murk.tres` → `green_neutral.tres`.
The water shader, the water material and `bubble_film.tres` are byte-identical to base.

**This repo had already diagnosed this exact mechanism, for a different box.**
`scenes/game/world/bubbletest/sections/BluePrecision.tscn:100-104`, on the moat surface:

> *"A PlaneMesh, NOT a BoxMesh, and the first draft got this wrong in a way only a render showed.
> `lake_murk.gdshader` declares `render_mode ... cull_disabled`, so every face of a box is drawn —
> including its underside and its four 2 cm sides — and a water shader shading the inside of a box
> over the top of itself **renders the whole basin as a flat black rectangle**. It looked exactly
> like 'the murk is working', which is why it survived one capture."*

That note is the mechanism, in the author's own words, written on the same day by the same packet
(EGG-2) that authored this plinth. The moat got a `PlaneMesh`; the plinth column stayed a
`BoxMesh` and kept the water material. "Flat black rectangle" is what Talon saw.

---

## 1. Reproduction, first, on the unfixed tree

`docs/qa/SHADER-2/before/bank-eye/Sh2Cap-18s.png`. **Camera `-95, 0.15, 16.5` looking at
`-95, -0.45, 10.65`**, noon, `--cycle-freeze`, `--capture-cam`, capture at t = 18 s. That is
`Run-Shader1Capture.ps1`'s `sunken` framing verbatim, chosen so this frame is directly comparable
to the already-committed `docs/qa/SHADER-1/after/sunken/Sh1Cap-18s.png` — which shows the same
artifact, so the reproduction is independent of my own harness.

A hard-edged black rectangle sits at **x 579..701**, top edge y ≈ 527, in an otherwise soft
grey-olive murk. Measured pixel values in that frame:

| what | sRGB |
|---|---|
| the box's front face | (25, 27, 25) |
| its left / right edge slivers | (6, 7, 7) / (11, 11, 10) |
| open water either side of it, same row | (86, 84, 77) / (56, 56, 51) |
| the television's cabinet, directly above | (58, 58, 54) |

**The box is `SunkenTvPlinth/Column`, proved by projection rather than by eye.** `BotHarness`
builds a bare `Camera3D`, so the capture camera is Godot's default 75° vertical FOV with
KeepHeight. Projecting the authored geometry through that camera:

| node | predicted screen x | predicted screen y |
|---|---|---|
| `SunkenTvPlinth/Column` | **579 .. 701** | 471 .. 706 |
| `SunkenTvPlinth/Cap` | 558 .. 722 | 461 .. **527** |
| TV `Cabinet` | 598 .. 682 | 401 .. 479 |

The observed silhouette is **579 .. 701**, and its top edge is where the Cap's lower edge is
(527). That is the Column, to the pixel, and it is not the television.

**And the colour is `deep_col`, not the murk.** The surrounding water sits at linear ≈ (0.038,
0.038, 0.033) — the `silt_col` tint. The box sits at linear ≈ (0.0103, 0.0120, 0.0103):
green-dominant, an order of magnitude darker, and matching `deep_col` = (0.0115, 0.0225, 0.0195)
composited at `under_alpha` 0.94. The box is on the shader's underside branch; the water around it
is not.

### What it depends on

| question | answer, from the captures |
|---|---|
| viewing angle? | **No.** Present dead-on (`bank-eye`), from the south-east (`bank-oblique`) and from 3 m above the surface (`over-water`). |
| distance? | **No.** Present at 5.9 m and at 15.4 m (`bank-far`), just smaller. |
| above/below the waterline? | **No, and this is the diagnostic shot.** `under-water` puts the camera at −1.20, below `WaterGeometry.WaterY` (−0.58), with no water plane between it and the plinth. The Column is still wrong there and wrong in a way the murk was hiding: its upper half is visibly **see-through** — the lake bed reads straight through it — with hard black slivers down its vertical edges. That is a transparent material on a solid box, with nothing to blame on the water. |
| standing in the water vs on the bank? | Same answer; the camera position, not the player's, decides the frame, and all six framings agree. |
| day vs night? | **Day only.** At midnight the plinth's whole silhouette moves by at most 6/255 between before and after, and not one pixel crosses 8 — there is nothing above black down there at night for the artifact to be made of. |

## 2. Mechanism, with evidence

Three things in `lake_murk.gdshader` are true of a lake surface and false of a solid box, and the
Column got all three:

1. **`cull_disabled` + no depth write** (`:212`, `render_mode blend_mix, depth_draw_opaque,
   cull_disabled`). `depth_draw_opaque` on a transparent material writes no depth — the shader's
   own header says so at `:196-198`, and `bubble_film.tres:33` quotes it. So both sides of the box
   are drawn, in index-buffer order, with nothing to sort them.
2. **The `!FRONT_FACING` branch** (`:1296-1302`). Written for "the underside of the lake seen by a
   submerged camera", where the honest answer is a flat near-opaque ceiling: `ALBEDO = deep_col`,
   `alpha = under_alpha` (0.94), `ROUGHNESS = 1.0`. On a box, that branch is every far face, and
   94 % opacity of a 0.01-value albedo painted over the near faces is a black rectangle.
3. **The normal is forced straight up** (`:1103`, `n_world = normalize(vec3(slope.x, 1.0,
   slope.y))`, and `:1107` then flips it to point straight DOWN on every far face). The box's
   vertical faces are therefore lit as though they were flat water, so they carry no shading
   gradient at all — which is why the silhouette reads as a hole cut in the scene rather than as
   a lit object.

**It is not sorting, and it is not SHADER-1 one layer down.** SHADER-1's bug was *between* two
meshes and was fixed by reordering them. This one is *inside a single mesh*: swapping which of the
two lake_murk surfaces draws first cannot change the order of a box's own faces against
themselves. It is not depth either — nothing here is depth-culled, because neither surface writes
depth. It is not lighting: at midnight, when there is no light, there is also no artifact.

**The witness is the render, and there was no other one available.** `.claude/rules/test-suite.md`
records that the two `RenderingServer` global-shader-parameter read-backs are editor-only; there
is no read-back at all for per-face transparent draw order, so an A/B render across the one changed
field is the only evidence that exists. That is what §3 is.

## 3. The change

| file | node | field | was | now |
|---|---|---|---|---|
| `scenes/game/world/bubbletest/sections/GreenHills.tscn` (base line 498) | `SunkenTvPlinth/Column/Mesh` | `material_override` | `ExtResource("4_gvttj")` = `lake_murk.tres` | `ExtResource("6_ddkf1")` = `green_neutral.tres` |

Plus a comment block above the plinth's sub-resources recording why, so the next person to want a
"murk-toned" column does not reach for the water again. Nothing else: no shader source, no
material, no C#, no other node, no new asset, no `.import`.

**Why `green_neutral.tres`, stated as a value call rather than escalated.** It is already an
`ext_resource` in this scene (`6_ddkf1`, the Cap's own material and nine other GreenHills props),
so the diff is one identifier and no new resource reference; and it keeps the section's palette.
`tangle_rock_dark.tres` (albedo 0.184 vs 0.227) was the other candidate and would honour the
authored comment's "murk-toned column, darker than the cap" more literally, but it belongs to
another section and using it here would be the level's first cross-section material borrow. Flip
that one identifier if Talon wants the darker stone.

**The murk the column was trying to have, it now actually has.** The lake surface composites over
it exactly as it composites over the television — which is the read Talon named as the thing he
likes. The plinth is not brighter after this change; it is *soft* instead of *cut out*.

**What is left pointing at `lake_murk.tres`, level-wide, after this change:** exactly two nodes,
both genuine water surfaces — `GreenHills.tscn` `Lake/Surface` (the 96-segment fan) and
`BluePrecision.tscn` `Moat/Surface` (the `PlaneMesh`). No solid geometry anywhere in the level is
shaded by the water any more.

**One knock-on to SHADER-1's reasoning, recorded so it is not a silent drift.** SHADER-1 argued
against `render_priority = -1` on `lake_murk.tres` partly because that material was *also* the
plinth column, which had to stay murked. It is not any more. **SHADER-1's fix is unaffected and
must not be touched** — `render_priority = 1` on `bubble_film.tres` is still the right side of that
fork for its own reasons (blast radius: one scene) — but the *alternative* it ruled out is now less
blocked than it was. That is a fact for whoever next opens that question, not an invitation.

## 4. Neighbours — measured, not eyeballed

Seven framings, each shot twice from the SAME camera at the SAME frozen phase at the SAME capture
second, once per side of the one changed field. Full table and per-shot numbers in
`docs/qa/SHADER-2/README.md`.

| shot | plinth silhouette: mean Δ / px > 8 | rest of frame: mean Δ / px > 32 |
|---|---|---|
| `bank-eye` (the repro) | 14.36 / 7524 | 0.03 / **8** |
| `bank-oblique` | 6.63 / 6184 | 0.09 / 46 |
| `bank-far` | 9.87 / 2730 | 0.01 / 11 |
| `over-water` | 2.77 / 1664 | 0.22 / 203 |
| `under-water` | 4.70 / 19611 | 0.03 / 74 |
| `bank-night` | **0.77 / 0** | 0.31 / 767 |
| `tv-murk` | 3.45 / 11393 | 0.16 / 58 |

**84–99 % of every moving pixel is inside the plinth's own projected silhouette.** The residue
outside it is the two time-driven things in this scene that never land on the identical phase
twice: `BubbleOscillation`'s wobble and the water's own sun glitter (`over-water` frames the
glitter path; `bank-night` frames the flashlight cone). This is the same residue
`docs/qa/SHADER-1/README.md` measured and named on the same lake. The 84 % is `tv-murk`, whose
frame is three-quarters grazing water surface with the glitter lobe in it — and even there only
**58 pixels outside the plinth** move by more than 32/255.

**The regression that would matter — the television's own murk — did not move.** Over the TV
cabinet crop the maximum single-channel difference is **1/255** in `over-water` and `under-water`,
and 10–22/255 in the bank shots, all of it on the antennas, which are 1-pixel-wide cylinders that
alias differently between runs. `docs/qa/SHADER-2/{before,after}/tv-murk/` is the dedicated tight
framing of that read — 3 m out, 0.48 m above the surface, the television filling the upper half of
the frame through the full murk. Its cabinet crop moves by **mean 0.114 / max 16**, and the 6×
amplified diff map of that whole frame is one bright rectangle, the plinth, with the television
itself black. That pair was shot by reverting the one token, capturing, and re-applying it — same
camera, same phase, same second, nothing else different.

## 5. ABSENCE check, with its positive control

**Claim:** SHADER-1's one field is untouched — `bubble_film.tres` `render_priority` is still `1`.

```
1. probe on this branch tip
   PASS  resources/materials/bubbletest/bubble_film.tres: render_priority = 1     exit 0

2. git diff on the file
   0 bytes;  git diff --exit-code: CLEAN (exit 0)

3. byte-for-byte against the base commit 418556d
   branch sha256 82047520bf9e42d5d97798981de77bc212d29b55e412bf8ec81059083a7a82c4
   base   sha256 82047520bf9e42d5d97798981de77bc212d29b55e412bf8ec81059083a7a82c4
   identical: True
```

**Positive control — the probe can catch a change.** A scratch copy of the file with the single
line `render_priority = 1` → `render_priority = 0` and nothing else:

```
1c1
< 63:render_priority = 1
---
> 63:render_priority = 0
FAIL  .../bf_planted.tres: render_priority = 0, expected 1                        exit 1
```

The scratch copy lives outside the repo and nothing was committed from it. The probe also fails
loudly if the `render_priority` line is deleted outright, which is the other way SHADER-1's fix
could go missing.

**The water itself, same standard.** `git diff playtest/2026-09-04-combined --` over
`resources/shaders/lake_murk.gdshader`, `resources/materials/bubbletest/lake_murk.tres` and
`resources/materials/bubbletest/bubble_film.tres` is **empty** — all three byte-identical to base.
The murk was not made opaque, not disabled, not retuned, and not re-prioritised.

## 6. Suites

Build, both halves, raw counts. The xUnit baseline was measured in a **throwaway detached
worktree** at the base commit (`C:\repos\Watis-shader2-base`, detached at `418556d`), not inherited
from any report.

**Build.** `dotnet build WatisWorld.sln` — **Build succeeded. 0 Errors, 4 Warnings.** All four
pre-existing under `scripts/game/sandbox/` (one `CS0618`, three nullable), identical to the set
SHADER-1 quoted.

**xUnit.** `dotnet test tests/unit/SailNet.Tests.csproj`

```
branch tip:  Failed: 0, Passed: 2269, Skipped: 0, Total: 2269   (Duration 3 s)
baseline:    Failed: 0, Passed: 2269, Skipped: 0, Total: 2269   (detached 418556d)
```

Identical. No file under `tests/unit/` was touched. (2269, not SHADER-1's 2223 — HONK-1 landed on
the combined branch between the two packets.)

**Scene marathon.** `powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`, foreground,
redirected to a file, never piped. Counted from the run's own `=== summary ===` block with the
anchored pattern `^  .+ (PASS|FAIL)$`:

```
47 suites: 45 PASS / 2 FAIL      OVERALL: FAIL
```

**Both reds are `Carry`-family STAGING failures, and both pass 3/3 standalone on the same tip.**
Neither is in `.claude/rules/test-suite.md`'s named load-flaky list, so they are discriminated here
from scratch rather than by citation.

| suite | what actually failed in the marathon |
|---|---|
| `Carry: server-authoritative` | `bot A: never observed prop 1 held by A — A's grab never converged on this peer` (×2 peers), then `bot B was, at some point, prop 1's holder — contested grab was NOT rejected`, then `bot D's first sample: holder=B, expected A`. **All four are one fact.** A's scripted grab never landed, so B won a contest A never entered, and the late-join dump then reported B. The containment assertion the suite exists for was judged on a scenario that was never staged. |
| `Carry: throw + loose` | `bot C: prop 3 was never held — the OOB throw was never staged`. The suite says "never staged" in its own words. |

That is the signature `.claude/rules/test-suite.md` already names for `Netcode: anti-cheat` — *"the
grab never landed"* — appearing on two neighbouring suites in the same `propsync` world.

**Standalone, `-SkipBuild`, same branch tip, 3× each:**

```
Run-CarryTest.ps1   run 1 exit=0  CARRY-TEST OVERALL: PASS
                    run 2 exit=0  CARRY-TEST OVERALL: PASS
                    run 3 exit=0  CARRY-TEST OVERALL: PASS
Run-ThrowTest.ps1   run 1 exit=0  THROW-TEST OVERALL: PASS
                    run 2 exit=0  THROW-TEST OVERALL: PASS
                    run 3 exit=0  THROW-TEST OVERALL: PASS
```

Every standalone carry run printed the full assertion line —
`PASS: grab converged, contested grab rejected, drop converged, disconnect released, late-join
converged` — i.e. the *same five* assertions the marathon reported, including the contested-grab
containment check. Throw likewise: `PASS: holder cleared, prop travelled, settle converged across
peers, OOB prop recovered home`.

**What else was running, honestly.** The machine was NOT idle for either half. Immediately after
the marathon ended I observed **10 Godot processes that were not mine**, and during the standalone
re-runs **4 more** (started 19:17, after my own suite had exited). Another agent is working on this
machine — BUBBLE-1 is in flight beside this packet — and per `.claude/rules/test-suite.md` I did not
investigate or touch any of them. **The re-runs therefore passed 6/6 under load rather than on an
idle machine, which strengthens the flake verdict rather than weakening it:** the two suites cleared
their staging while competing with the same neighbour that was there when they failed.

**And the diff cannot reach them.** These two suites run the `propsync` world, not `bubbletest`.
This branch changes one `material_override` on one `MeshInstance3D` in `GreenHills.tscn`, plus
documentation and a capture script. No C#, no netcode, no carry, no prop, no motor.

**The suites the packet named by name were both green, inside the marathon:**

- `World: TV portal (BT-10)` → `TV PORTAL TEST OVERALL: PASS`. This is the suite that owns the
  sunken television: `TvPortalSelfTest` rays the live plinth top against
  `BubbleTestLayout.SunkenTvBaseY`, checks the walk-in trigger straddles the swim line, and checks
  `SunkenTvReturn` lands dry.
- `World: bubble test (BT-0)` → `BUBBLE-TEST WORLD TEST OVERALL: PASS`, with `CheckBake`'s
  packed-vs-live counts equal in every section. GreenHills, measured this run:
  **`packed[mesh=30 shape=59 body=12]  live[mesh=30 shape=59 body=12]  OK`** — the same triple
  SHADER-1 quoted from its own run. A `material_override` cannot move those counts, and the suite
  proves it did not.

Every other bubble- or water-bearing suite was green too: `Bubbles: shared counter`,
`Water: lake contract (W2)`, `Water: splash VFX + audio (W4)`, `Egg: puffin lab (EGG-1)`,
`Level: the kit (LD-6)`, `Watcher: night gate`, `Authored props: adopted`.

## 7. New file

Two, and one edited rule file.

`tests/Run-Shader2Capture.ps1` — the capture harness that produced everything in
`docs/qa/SHADER-2/`. `--capture-cam` on every shot; no `--goto-script`, so the bot stands at its
hub spawn and cannot walk into frame. **Deliberately NOT registered in `Run-AllTests.ps1`** — it
produces frames a human looks at, not a verdict. Same argument and same shape as
`Run-EggCapture.ps1` and `Run-Shader1Capture.ps1`. It carries SHADER-1's `-Only a,b` comma-split
workaround verbatim, because `powershell -File` still hands PowerShell 5.1 one string.

`docs/qa/SHADER-2/README.md` — the index, the camera table, and the full measured-difference
numbers.

`.claude/rules/test-suite.md` — **one added, dated, MEASURED entry** naming
`Carry: server-authoritative` and `Carry: throw + loose` as load-flaky with their staging signature
and their discriminator, plus a two-line cross-reference from the existing load-flaky list. The
role's allowed-paths grant is "measured entries only"; this one is measured (6 standalone runs, the
full assertion lines quoted) and says what was running. **No other agent's entry was touched.**

## 8. Bible check

```
Bibles applied:  none — and here is why.
Items checked:   n/a
Result:          n/a
```

This is not a gameplay feature. One node's `material_override` changed: no state machine, no
autonomous entity, no new thing the player acts on, no composition change (nothing moved, nothing
was added or removed), and no affect target. `INTERACTION-BIBLE` was considered and does not bite —
the plinth's `StaticBody3D`, its `CollisionShape3D` and the television's walk-in trigger are all
untouched, so what a player can stand on and swim to is bit-identical. The nearest applicable
standing document is `ART-BIBLE`, which belongs to art-atmosphere; nothing here re-grades the
water, and the water shader was not opened except to read it.

## Corrections to the packet

1. **The packet's ranked candidate list had the right winner but the wrong reason attached to it.**
   It ranked "the TV's own collision or plinth mesh rendering when it should not" first. The plinth
   mesh is rendering exactly when it should — it is authored to be visible, and the scene comment
   says so. What is wrong is *what it is rendering with*. Worth recording because the instruction
   that actually paid off was the other one — **"check the geometry before you touch a shader"** —
   which is how the material assignment got read at all.

2. **`ROLE.md`'s allowed-write-paths and this packet do not quite line up, and I acted rather than
   stalled — flagging it here rather than self-correcting silently.** `ROLE.md` grants `scenes/**`
   "only scenes your packet names", and says level composition under
   `scenes/game/world/bubbletest/` is the environment role's and materials are art-atmosphere's.
   This packet names the sunken television and its plinth **by description**, not by path, and
   directs "fix it only if the fix is small and safe" with an explicit prohibition list that this
   change does not touch. I judged a one-token `material_override` change — no geometry moved, no
   material file edited, no new asset — to be inside the packet's intent and took it. **If the
   orchestrator reads that boundary the other way, revert the single token and everything in §§1–5
   still stands as the costed report.** The scene comment and the DECISION-LOG entry are written so
   that reverting is one line and the reasoning survives.

3. **The first `under-water` camera was inside the terrain and the render did not say so loudly.**
   `-95,-1.9,14.6` is under `WaterY` but the bed crosses the submerged contour (−1.73) at
   r = 13.888, so at r = 14.6 the camera was buried in the bank and rendered its backfaces as a
   flat grey lower third — which looks like a legitimate underwater frame until you ask what the
   grey is. `-95,-1.2,13.5` is the corrected one, and the reason is written into the capture
   script so the next person does not re-derive it. This is the same class as the packet's own
   warning about hand-computed transforms: only a render shows it.

4. **The repo already contained the diagnosis.** `BluePrecision.tscn:100-104` names this exact
   mechanism and its exact symptom — "renders the whole basin as a flat black rectangle" — for the
   moat, authored by the same packet on the same day. Nothing in this packet's investigation was
   novel; it was one grep away the whole time. The cheap lesson for the next dispatch: when a
   scene comment says a mistake was made and fixed, grep the level for the *other* places the same
   mistake could be.

## Open questions

None ripe. Two calls taken and stated rather than escalated: `green_neutral.tres` over
`tangle_rock_dark.tres` for the column (§3 — one identifier to flip if Talon wants it darker), and
fixing at all rather than reporting only (§Corrections 2).

## Out of scope, confirmed untouched

Bubble placement, bubble counts and bubble materials (BUBBLE-1's) — `bubble_film.tres` is
byte-identical to base and no `Bubble_*` node was read or written. The TV portal's destination, its
return trip and what is inside the room it leads to — `BubbleTestLayout.TvRoutes`,
`SunkenTvReturn` and `TvRoom.tscn` were not opened for editing. No gameplay, collision or physics
change: the plinth's bodies and shapes and the portal's trigger are bit-identical. The PuffinLab.
No new asset. No UI. `resources/shaders/lake_murk.gdshader`, `resources/materials/bubbletest/
lake_murk.tres` and `resources/materials/bubbletest/bubble_film.tres` are all byte-identical to
`playtest/2026-09-04-combined`.
