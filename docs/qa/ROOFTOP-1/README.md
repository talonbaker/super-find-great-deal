# ROOFTOP-1 — the seventh television, the diving board and the last bubble

Seven headed frames, produced by [`tools/dev/rooftop1_capture.ps1`](../../../tools/dev/rooftop1_capture.ps1)
at 1920 × 1080, `--cycle-start-phase noon --cycle-freeze`, captured at t = 18 s, **`--capture-cam`
on every shot** (a `--bot` client builds no camera of its own, so `--capture-dir` without it writes
a correct HUD over the engine's flat grey clear colour and nothing in the log says the world was
never drawn).

Re-run one shot with `-Only <tag>`; re-run everything with `-SkipBuild` after a build.

| Shot | What it is evidence of | Camera `x,y,z,tx,ty,tz` |
|---|---|---|
| `two-roofs` | **the re-siting.** From the landing roof, looking east at the far one, with the seventh television's lit screen on it | `88,-3.2,92,168,-11.9,80.5` |
| `tunnel` | the escape hall's ceiling — Talon's "little tunnel" — and the 3.864 m climb off its far end | `145,-11.2,71.5,168,-13.2,78` |
| `roof-tv` | the television on the far roof, on `LabRoof.tscn`'s pad | `161.5,-8.2,87.6,168,-11.6,80.5` |
| `deck` | the sky deck at y = 200: the 14 m platform, the board off its north edge, the way out | `17,208,16,0,200,-6` |
| `board` | the diving board and the hundredth bubble on the end of it | `4.5,202.2,-4.0,0,201.05,-15.9` |
| `map` | **the reward.** The whole level under the board's tip | `0,201.8,-19.5,0,0,-62` |
| `fall` | the frame from 100 m, half way down the board tip's fall line | `0,100,-17,0,0,-24` |

## What each frame is for

**`two-roofs`** is the frame the whole packet turns on. Talon, 2026-09-05, on the first siting:
*"That's the wrong roof. It's the one farther back. It's the one out more."* Standing on the
**landing** roof (`Lab/HubAccess_Off/Ceiling`, top y = −6.410) looking east, the **far** roof
(`Route/VoidCeil`, top y = −12.206) is the block on the horizon with the television's white screen
on top of it, and the tunnel is the pale strip running out to it. Centre to centre the two are
**78.64 m apart in plan** (77.97 east, 10.21 north) with the far one **5.796 m lower**. He
confirmed the new siting on the same day: *"YES! that's the roof!"*

**`tunnel`** is the leg where the arithmetic does not come out where he expected. The tunnel tops
out at −16.070 and the far roof at −12.206: a **3.864 m** climb. A golden cube is 0.44 m and a full
jump's apex on the shipped motor is 1.47 m, so three cubes lift a player 2.79 m — **1.07 m short**
on those numbers. Talon then closed it by doing it (2026-09-05: *"The roof is reachable. I have
proved it myself so this is fine"*), so nothing was placed and nothing was authored. The
measurement stands as measured; `BubbleTestSelfTest.CheckRooftopRoute` prints it every run and
asserts none of it, which makes it the early warning if the jump or the cube height ever changes.
See the report's Closed question 1.

**`roof-tv`** answers the sentence that started this: *"what's on the top of the puffling lab roof?
there's no TV on here?"* The pad under it is `LabRoof.tscn`'s and is 2 cm proud of the roof — enough
that the two top faces cannot z-fight, far below any step.

**`deck`** and **`board`**: a 14 m platform 200 m over the hub with a 10 m plank off its north edge,
a mount block under the plank's root, and the level's hundredth bubble 1.05 m above the tip. No
rail — a rail on a diving platform is a rail against the only thing the place is for.

**`map`** is the shot the height was chosen for, and it is the one that could have failed. All seven
surface sections read from up there: green with its lake, red's cairns, the tangle's pile, blue's
tower, cyan, and the hub plaza with its four connectors in the middle.

## Two ways this shot can lie, both of which happened before it was right

The first `map` attempt stood on the deck aimed at `(0,0,-58)` and came back **flat grey**. That
looks exactly like the failure the packet warns about — *"a vantage that looks into grey haze is not
the reward he described"* — and it was **not** the fog: at that angle the frame covered a ~60 m
radius, which from 200 m is the grey hub plaza and nothing else. The second attempt, from the
board's tip, framed the level correctly and then filled the middle of the lens with the last metre
of plank and the hundredth bubble's film. The camera that ships sits 2.5 m past the tip.

**Fog is real, but only at night.** At noon it is 0.001 m⁻¹ and 259 m of slant to the furthest
corner leaves 77 % of the contrast. `BubbleTestWorld` binds the fog to a 28 m sight range at night
(`SightPresentation.FogDensityWithSight`), which is 0.107 m⁻¹ and ~5e-10 transmittance at 200 m —
**from the sky deck at any night phase the level below is not hazy, it is absent.** These frames are
taken at noon and say so; the vantage is a daylight reward and nothing here changes that.

## What you cannot see here, and why

- **A body falling.** `--capture-cam` parks a fixed camera and cannot follow one, so `fall` is what
  the fall *shows* rather than a picture of somebody falling.
- **The cube stack being built.** The shipped scripted brain walks to one point and latches, and no
  harness in this repo carries a prop through a multi-leg traversal, so the climb is measured
  (`CheckRooftopRoute`) rather than driven.
- **The landing roof's own approach from cyan.** Talon's route is his and this packet does not
  touch it; the absence is proved by `tools/dev/rooftop1_route_probe.ps1`, which has a positive
  control, rather than by a screenshot.
