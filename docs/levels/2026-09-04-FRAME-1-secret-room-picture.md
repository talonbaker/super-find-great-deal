# FRAME-1 — the drawing in the room behind the sunken lake television

**2026-09-04 · environment role · branch `feat/2026-09-04-frame-1-secret-room-picture`**

> ## The drawing is `resources/SecretRoomPicture.png`.
> Replace that one file and the wall changes. No code edit, no scene edit, no rebuild of anything
> but the project's own asset import. Any aspect ratio; transparency respected.

## Why the room needed anything

Talon, 2026-09-04:

> "the TV in the lake, the room that it takes you — it looks like a special room only it's not,
> it's nothing. It's nothing special, so how do we make that room special? It's kind of a secret TV
> where if you know about it and you find it, you should be rewarded with something sort of
> fantastic, right? So right now there's nothing in the room, that's it, which kind of sucks."

and then:

> "maybe there is a picture frame and or like a drawing on the wall, and the drawing can be of
> something special that the play testers would know and they would appreciate. … Can you put if I
> gave you a drawing to put on the wall, a PNG with transparency? Could you put this on the wall?"

and, once the drawing existed:

> "Please made it large enough to at least the player can see it."

## Which room, and the evidence

`BubbleTestLayout.TvRoutes` (`scripts/game/world/bubbletest/BubbleTestLayout.cs`) has six rows. The
last one is the lake's, and it is identified rather than guessed:

- it is keyed on `BubbleTestLayout.SunkenTvNodeName` (`"SunkenTv"`);
- its entrance position is `SunkenTvPos`, which is composed from
  `WaterGeometry.BubbleTestLakeCentreX` / `…CentreZ` — **the lake's own centre** — at
  `SunkenTvBaseY` (−2.05 m), i.e. 1.47 m under `WaterGeometry.WaterY`;
- its `ReturnTvPath` is `RoomF/RoomTv` and its `RoomCentre` is `(−17, 0, −17)`;
- it is the only route whose entrance is not on dry ground, and the only one with a named return
  (`SunkenTvReturn`, the south bank) because the ordinary offset would drop the player back into
  deep water.

So the room is **RoomF, the Deep Room** — silt floor, murk walls, an emissive "underside of the
water" ceiling, `TvRoomAnchor + (−17, 0, −17)`, i.e. world `(−17, −20, −17)`, 10 × 12 × 4 m.

## Where the drawing hangs, and how the numbers were derived

| quantity | value | derivation |
|---|---|---|
| wall | RoomF north, inner face at room-local z = −6.0 | `WallNorth` sits at −6.25, 0.5 m thick |
| distance from arrival | 9.0 m | `ArrivalOf(route)` = room-local `(0, 0.4, 3)`; a teleport resets `MoveState.Yaw` to 0 = −Z, so the player arrives already facing it |
| mount box | 8.759 × 2.759 m | each surface bound less **one whole `AvatarProportions.PlayerCrownM`** (1.241 m, measured off the shipped whole-figure model), split evenly per side |
| sheet | **3.637 × 2.759 m** | the drawing's exact 1388 : 1053 fitted inside that box; the height binds |
| centre height | **2.000 m** | maximising under an even margin puts the centre at the wall's mid-height by construction: `margin/2 + height/2 = TvRoomHeight/2` |

At 9.0 m the sheet fills about 26% of the frame's width — roughly 500 px at 1080p, of which the
hand-lettered word is about 390. Verified by capture, not by arithmetic: `docs/qa/FRAME-1/`.

**The packet asked for the avatar's eyeline (0.995 m) and Talon then asked for size; in a 4 m room
those are arithmetically incompatible** — a sheet centred at 0.995 m caps at 1.99 m tall, i.e.
2.62 m wide, against the 3.64 m the wall carries. Size won. The avatar did not stop mattering: it
sets the *margin*, so re-measuring the body still resizes the drawing and
`BubbleTestSelfTest.CheckSecretPicture` goes red if the scene and the constant ever disagree.

## It is not in a picture frame, and the artwork is why

The drawing is a torn sheet of paper with a strip of masking tape rendered at the top centre —
24.1% of the image is fully transparent and another 0.49% is partial along that torn edge. A
rectangular moulding around it would box a thing that already draws its own edge, and would cover
the alpha the request was about. It hangs as what it is: a sheet taped to the wall.

What it *does* carry is one slim **`MountRail`** batten 0.14 m above the paper's top edge. That is
the empty state's anchor, not a frame: it does not box the drawing, does not overlap it and hides
none of its alpha, and with the PNG removed the wall reads as *a place a drawing hangs* rather than
as a lamp aimed at nothing. Measured — without it the empty capture is a bare lit oval on a wall.

## The light

One `SpotLight3D`, `RoomF/SecretPicture/PictureLight`, on a ceiling drop 3.0 m out from the wall,
warm (1, 0.93, 0.84), energy 5.0, range 6.5 m, cone 44°, **`shadow_enabled = false`**.

- The room's own `RoomFill` omni is untouched, and **no second `WorldEnvironment` was added**
  (EGG-1's recorded trap).
- Ceiling-mounted rather than on a bracket above the sheet, and that is measured: a lamp 0.8 m out
  above a 2.76 m sheet lands incidence 0.91 at the top against 0.17 at the bottom — clipped top,
  black bottom. From (0, 1.50, 3.00) sheet-local it runs 1.00 to 0.72 down the height and never
  below 0.66 at the far corners.
- `LampDrop` and `LampHood` give the light a visible source rather than leaving it floating; the
  room already had one unsourced omni and this packet did not add a second.

## Files

| path | what changed |
|---|---|
| `resources/SecretRoomPicture.png` (+ `.import`) | **the drawing**. Import set to VRAM Compressed / high quality (BPTC), mipmaps on, `detect_3d/compress_to=0` so the mode cannot change silently later. `etc2_astc` is never used (ART-BIBLE §4.4). |
| `scenes/game/world/bubbletest/sections/TvRoom.tscn` | `RoomF/SecretPicture` — `Canvas`, `MountRail`, `LampDrop`, `LampHood`, `PictureLight`, and five sub-resources. Nothing else in the file moved; the Den's crooked frames are untouched. |
| `scripts/game/world/bubbletest/BubbleTestLayout.cs` | `SecretPictureImagePath`, `SecretPicturePath`, `SecretPictureCanvasName`, `RoomFWallWidthM`, `SecretPictureMountM`, `SecretPictureCentreY`. |
| `scripts/game/world/bubbletest/BubbleTestWorld.cs` | `SetUpSecretPicture()` — the `ResourceLoader.Exists` guard, the aspect fit, the visibility flip. |
| `scripts/game/world/bubbletest/BubbleTestSelfTest.cs` | `CheckSecretPicture()` — the height pin, the mount-box bound, the alpha-mode assertion, the shadow assertion. |
| `tools/dev/frame1_capture.ps1`, `docs/qa/FRAME-1/**` | the evidence and how to remake it. |
