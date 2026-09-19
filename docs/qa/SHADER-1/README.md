# SHADER-1 — bubbles stay visible where they overlap the water

Captures for `fix/2026-09-04-shader-1-bubble-over-water`. Every frame here was produced by
`tests/Run-Shader1Capture.ps1` (`--capture-cam` on every shot, so nothing is framed by a bot's
own follow camera), at `--capture-at 18` on a `--cycle-freeze`'d clock.

- `before/` — the UNFIXED tree (`bubble_film.tres` at `render_priority = 0`).
- `after/`  — the fix (`bubble_film.tres` at `render_priority = 1`).

Same camera, same cycle phase, same capture second in both. The only difference between the two
runs is that one line.

## The reproduction — `before/lake-se`

Camera `-83,9,13` looking at `-102,0.5,-3`; noon; green's lake. Crop the frame around
(520, 320)–(650, 410) and two bubbles show the bug in one picture: **each is drawn in full where
its silhouette crosses the bank, and stops dead along a line that is exactly the shoreline.**
One keeps only its top cap (the part over grass); the one below it keeps only its bottom (the
part over rock). Over water there is a trace of rim and nothing else.

`after/lake-se` is the same camera and the same two bubbles, whole.

## Index

| shot | phase | camera (x,y,z → tx,ty,tz) | what it is for |
|---|---|---|---|
| `lake-se` | noon | `-83,9,13 → -102,0.5,-3` | **the reproduction and the fix.** Bubbles cut at the shoreline. |
| `lake-nw` | noon | `-107,9,-13 → -88,0.5,3` | the mirrored bank: which bubbles are lost flips with the camera, which is the sort-order signature. |
| `lake-east` | noon | `-70,14,0 → -98,0,0` | the whole lake down its long axis. |
| `lake-west` | noon | `-120,14,0 → -92,0,0` | the same, from the far bank. |
| `lake-night` | midnight | `-70,14,0 → -98,0,0` | night lighting + flashlight. The night glow is not what was hiding them: two more bubbles appear after the fix. |
| `moat` | noon | `64,22,32 → 81,-2,-2` | **neighbour:** BluePrecision's drowning moat (the second `lake_murk` surface) and the golden cubes around it. |
| `sunken` | noon | `-95,0.15,16.5 → -95,-0.45,10.65` | **neighbour:** the murk read at eye height, and the sixth television 2 m under it. |
| `secret` | midnight | `-95,6,-24 → -95,-1.35,-10` | **neighbour:** the `SecretBubble` under the surface, at night. |
| `cubes` | noon | `26,10,26 → 0,0.3,6` | **neighbour:** the hub's golden cubes. |
| `tvroom` | noon | `0,-18.4,-4.5 → 0,-18.9,5.2` | **neighbour:** inside television room A, 20 m under the level. |

## Neighbours: measured, not eyeballed

Per-pixel difference between the `before` and `after` frame of each shot (mean |Δ| over RGB, and
the share of pixels moving by more than 8/255). The residual on the neighbour shots is the
bubbles' own `BubbleOscillation` wobble, which is time-driven and never lands on the identical
phase twice — it is not the fix.

| shot | mean abs Δ | pixels > 8 |
|---|---|---|
| `lake-se` | 0.486 | 1.78 % |
| `lake-nw` | 1.330 | 3.27 % |
| `lake-east` | 0.306 | 1.20 % |
| `lake-west` | 2.502 | 3.86 % |
| `lake-night` | 0.035 | 0.05 % |
| `moat` | 0.003 | **0.00 %** (20 pixels of 921 600) |
| `sunken` | 0.048 | 0.13 % |
| `secret` | 0.032 | 0.03 % |
| `cubes` | 0.012 | 0.02 % |
| `tvroom` | 0.076 | 0.13 % |

The four lake shots move; the five neighbours do not. That split is the claim.
