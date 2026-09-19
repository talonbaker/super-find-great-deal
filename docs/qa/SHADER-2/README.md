# SHADER-2 — the boxy shape under the sunken television

Every frame here was produced by `tests/Run-Shader2Capture.ps1`, which is not registered in
`Run-AllTests.ps1`: it makes pictures a human looks at, not a verdict. Both labels were shot from
the **same six cameras** at the **same frozen cycle phase** at the **same capture second (18 s)**,
with `--capture-cam` on every shot and no `--goto-script`, so the bot stands at its hub spawn and
never walks into frame.

- `before/` — the tree at `playtest/2026-09-04-combined` (`418556d`), unmodified.
- `after/` — the same tree with the one field this packet changed.

The only difference between the two runs is `SunkenTvPlinth/Column/Mesh`'s `material_override` in
`scenes/game/world/bubbletest/sections/GreenHills.tscn`: `lake_murk.tres` → `green_neutral.tres`.

## The subject, in numbers

Green's lake is centred on `(-95, -0.58, 0)` with the sixth television at
`BubbleTestLayout.SunkenTvPos` = `(-95, -2.05, 10.65)`. Under it:

| node | mesh | world Y span | width |
|---|---|---|---|
| `SunkenTvPlinth/Cap` | `BoxMesh` 1.8 × 0.15 × 1.8 | −2.20 .. −2.05 | 1.8 m |
| `SunkenTvPlinth/Column` | `BoxMesh` 1.4 × 2.4 × 1.4 | −4.525 .. −2.125 | 1.4 m |

The bake's bed at that radius runs −2.71 (r = 9.95) to −3.27 (r = 10.65), so roughly **0.6–1.2 m
of the Column stands in open water** and is what the player sees. `WaterGeometry.WaterY` is −0.58
and the swim line is −1.83.

## The shots

| tag | camera (x,y,z → tx,ty,tz) | phase | what it is for |
|---|---|---|---|
| `bank-eye` | `-95,0.15,16.5` → `-95,-0.45,10.65` | noon | **The reproduction.** `Run-Shader1Capture.ps1`'s `sunken` framing verbatim, so this pair is directly comparable to `docs/qa/SHADER-1/after/sunken/`. |
| `bank-night` | same camera, flashlight at t = 2 s | midnight | is it a night bug too? |
| `bank-oblique` | `-90.8,0.15,15.0` → `-95,-0.45,10.65` | noon | does it depend on angle? |
| `bank-far` | `-95,1.5,26.0` → `-95,-1.2,10.65` | noon | does it depend on distance? |
| `over-water` | `-95,3.0,16.0` → `-95,-2.4,10.65` | noon | seen from above rather than through a grazing surface. |
| `under-water` | `-95,-1.2,13.5` → `-95,-2.6,10.65` | noon | camera **below** `WaterY`, no water plane between it and the subject: separates "the box is wrong" from "the water over the box is wrong". |
| `tv-murk` | `-95,-0.10,13.5` → `-95,-1.35,10.75` | noon | tight on the television itself — the read Talon likes, and the regression that would matter. |

`bank-eye` is the frame to look at first. `under-water` is the frame that shows *why*: with no
murk in the way, the Column is visibly **see-through** in its upper half (the lake bed reads
straight through it) with hard black slivers down its vertical edges, because it is shaded by a
transparent water shader with `cull_disabled` and no depth write.

### One camera that had to be moved

The first `under-water` attempt was `-95,-1.9,14.6`. The bed crosses the submerged contour
(−1.73) at r = 13.888, so at r = 14.6 that camera was **inside the bank**, rendering the terrain's
backfaces as a flat grey lower third. `-95,-1.2,13.5` is under `WaterY` and clear of the bed. If
this shot ever comes back with a flat grey lower third, that is what happened.

## Measured difference, before → after

Per-pixel `max(|Δr|,|Δg|,|Δb|)` over 1280 × 720. "plinth box" is the Column ∪ Cap silhouette
projected from the camera (Godot's default 75° vertical FOV, KeepHeight — `BotHarness` builds a
bare `Camera3D`); "elsewhere" is the whole rest of the frame.

| shot | plinth box: mean / max / px > 8 | elsewhere: mean / px > 8 / px > 32 | TV cabinet crop: mean / max |
|---|---|---|---|
| `bank-eye` | 14.36 / 69 / 7524 | 0.03 / 226 / **8** | 0.129 / 10 |
| `bank-oblique` | 6.63 / 86 / 6184 | 0.09 / 2360 / 46 | 0.208 / 20 |
| `bank-far` | 9.87 / 105 / 2730 | 0.01 / 195 / 11 | 0.051 / 19 |
| `over-water` | 2.77 / 49 / 1664 | 0.22 / 1505 / 203 | 0.065 / **1** |
| `under-water` | 4.70 / 98 / 19611 | 0.03 / 211 / 74 | 0.128 / **1** |
| `bank-night` | **0.77 / 6 / 0** | 0.31 / 6060 / 767 | 0.363 / 6 |
| `tv-murk` | 3.45 / 52 / 11393 | 0.16 / 2217 / 58 | 0.114 / 16 |

**84–99 % of every moving pixel is inside the plinth's own silhouette** (84 % is `tv-murk`, whose
frame is three-quarters grazing water surface with the sun-glitter lobe in it — its 2217 outside
pixels carry only 58 above 32/255). The residue outside it is
the two things in this scene that are time-driven and never land on the identical phase twice:
`BubbleOscillation`'s wobble (all shots) and the water's own sun glitter (`over-water`, which
frames the glitter path) and the flashlight cone (`bank-night`). This is the same residue
`docs/qa/SHADER-1/README.md` measured and named.

**The television's murk did not move.** The TV cabinet crop — the thing Talon explicitly likes —
differs by a maximum of **1/255** in the two closest shots and 10–22/255 in the bank shots, all of
it on the antenna edges where a 1-pixel-wide cylinder aliases differently between runs. `tv-murk`
is the dedicated framing of that read: 3 m out, 0.48 m above the surface, the television filling
the upper half of the frame through the full murk. Its cabinet crop moves by **mean 0.114 / max
16**, and the diff map of the whole frame is one bright rectangle — the plinth — with the
television itself black.

**The artifact is a DAY artifact.** At midnight the plinth box moves by at most 6/255 and not one
pixel crosses 8, because nothing down there is above black at night in the first place.
