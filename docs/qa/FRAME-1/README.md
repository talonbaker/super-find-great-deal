# FRAME-1 — the drawing on the wall of the room behind the sunken lake television

Captures made by `tools/dev/frame1_capture.ps1`, 1920x1080, midday, `--cycle-freeze`,
`--capture-cam` on every shot (a bot's own follow camera frames the bot, and the subject here is a
wall 20 m underground that no bot walks to). Capture at t = 14 s.

**The drawing lives at `resources/SecretRoomPicture.png`.** That is the one line that matters:
replace that file and the wall changes, with no code edit and no scene edit.

## The room, and why it is the lake's

`BubbleTestLayout.TvRoutes`' last row is keyed on `SunkenTvNodeName` and stands at `SunkenTvPos`,
which is derived from `WaterGeometry.BubbleTestLakeCentreX/Z` — the lake's own centre — 2 m under
the surface. Its `RoomCentre` is `(-17, 0, -17)` and its `ReturnTvPath` is `RoomF/RoomTv`, so
**RoomF is the room the lake television opens onto**, and the drawing hangs on RoomF's north wall.

## The cameras

| tag | position | looking at | why |
|---|---|---|---|
| `arrival` | `-17, -19.005, -14` | `-17, -18.0, -23.0` | `ArrivalOf(route)` exactly, at the shipped avatar's measured eye height (`AvatarProportions.PlayerEyeHeightM` = 0.995 m above the room floor at y = -20). What a player sees the instant the room resolves. |
| `close`   | `-17, -19.005, -20` | `-17, -18.0, -23.0` | 3 m out from the same wall at the same height. |

## The shots

| directory | what it shows |
|---|---|
| `shipped-arrival/` | The drawing from the arrival point, 9.0 m away. "fwends" is readable and the four televisions and four figures are countable without walking up to it. |
| `shipped-close/`   | The same at 3 m. **The transparency evidence**: the murk wall is visible right up to the paper's torn edge and all around it. If the material did not honour alpha this would be a solid rectangle. |
| `empty-arrival/`   | The same build with the PNG moved aside. A lit mount rail under a lit lamp with nothing hung — a designed empty state, not a bug. |
| `empty-close/`     | The empty state at 3 m. |
| `control-alpha-close/`   | **The controlled A/B, half of it.** `picture_alpha.png` — a checkerboard whose alternate cells are fully transparent. |
| `control-opaque-close/`  | The other half. `picture_opaque.png` — the same checkerboard with those cells filled solid grey. Identical geometry, one difference. Side by side they separate "the material honours alpha" from "that part of the image was dark anyway", which the shipped shot alone cannot do. |

`picture_alpha.png` and `picture_opaque.png` are authored by `make_test_images.py` in this
directory — pure stdlib, no Pillow, nothing downloaded, no third-party art. They are test patterns
and are never the shipped asset.

## The empty state's ERROR probe, and its positive control

The probe is a **case-sensitive** scan for `ERROR` across all four log files of a run
(`*-bot.out.log`, `*-bot.err.log`, `*-server.out.log`, `*-server.err.log` in `tests/logs/`).
Case-sensitivity is load-bearing: PowerShell's `Select-String` is case-insensitive by default and
matched the phrase "not an error" in the guard's own friendly `GD.Print`, reporting a false
positive on the first pass.

Measured on the tip:

```
shipped, all 8 log files ..... ERROR = 0
empty,   all 8 log files ..... ERROR = 0
```

**Positive control — the probe is not blind.** In a scratch run the `ResourceLoader.Exists` test in
`BubbleTestWorld.SetUpSecretPicture` was inverted so `GD.Load` runs on a path that genuinely is not
there, and the PNG was moved aside:

```
frame1-control-bot.err.log ... ERROR = 2

ERROR: Resource file not found: res://resources/SecretRoomPicture.png (expected type: unknown)
       at: _load (core/io/resource_loader.cpp:325)
       [4] void Sail.Game.World.BubbleTest.BubbleTestWorld.SetUpSecretPicture()  BubbleTestWorld.cs:161
ERROR: Error loading resource: 'res://resources/SecretRoomPicture.png'.
       at: load (core/core_bind.cpp:82)
```

Two ERROR lines where the shipped and empty runs have zero — so the zero is a measurement, not a
blind spot. The control also shows the fallback still degrades rather than crashing: the run
continued to a `WARNING: [bubbletest.picture] ... would not load as a Texture2D` and the wall
stayed empty. The guard and the drawing were both restored and the project rebuilt in the same
script's `finally` block.

To repeat it: invert the `if (!ResourceLoader.Exists(...))` in `SetUpSecretPicture` to
`if (ResourceLoader.Exists(...))`, move `resources/SecretRoomPicture.png` and its `.import` aside,
`dotnet build` + `godot --headless --import`, run a server and a bot, then scan the `.err.log`
case-sensitively. Put all three back afterwards.

## Reproducing

```
powershell -File tools/dev/frame1_capture.ps1
powershell -File tools/dev/frame1_capture.ps1 -Only shipped
python docs/qa/FRAME-1/make_test_images.py        # regenerates the two control patterns
```

The script moves the shipped PNG aside for the empty and control states and puts it back in a
`finally` block. It never modifies or re-exports the drawing.
