# BoxKid — the player body, conformed to the greybox

**What it is.** The player body for the bubble-test level (BT-7, 2026-08-27). Roster key `boxkid`,
`AvatarVisual.Build.AuthoredRig`, selectable anywhere with `SAIL_AVATAR=boxkid` or the picker's
first row, and the default body in `--world bubbletest`.

**BODY-3 (2026-08-29) re-proportioned it onto the classic greybox, and that is now the point of
the file.** Talon, after the second bubble-test pass: *"I really like how the 'graybox' model looks
and feels, I don't like how 'box kid' model looks and feels. Please fix this so box kid model looks
exactly like how graybox is as the only different is the box kid is a physical glb and the graybox
is generated. Please make this change using the exact dimentions of the graybox to make box kid
better."*

The greybox he means is **`greybox_classic`** — `scripts/game/sandbox/ClassicGreyboxAvatarBody.cs`,
the roster row displayed as "Greybox", the body MOVE-3g recovered at his own request. Not
`greybox_primitive`, which is the gumdrop he rejected. BT-7's original brief (*"Simple boxy figure:
box body, two box arms, two box legs, box head"*) is superseded by this one: the body is still
simple and still reads boxy, but its numbers are the greybox's rather than its own, and its trunk
and head are tapered frusta because the greybox's are.

**Openable, which is the point.** `source/boxkid.blend` opens in Blender and `BoxKid.glb` opens in the
Godot editor. Talon's standing ruling is that code-only geometry is not an asset, and the bubble
test program's Q3 applied it here: *"BoxKid is trivial to make (it's just boxes) and gives you a
real, versioned, reusable asset instead of a throwaway."*

## Building it

Blender **5.0.1** (`a3db93c5b259`, 2025-12-16), installed at
`C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`.

```
blender.exe --background --python assets/creatures/boxkid/source/build_boxkid.py -- --force
blender.exe --background --python assets/creatures/boxkid/source/build_boxkid.py -- --export-only
```

`source/boxkid.blend` is **authoritative**; `build_boxkid.py` is a **bootstrap**. A bare run refuses to
overwrite the `.blend`. `--force` rebuilds the geometry from THE JOINT BLOCK and THE DIMENSION
BLOCK and re-bootstraps the clip library, destroying hand-keyed animation. `--export-only` re-exports the `.blend` as it
stands. Same precedence rule, same flags, same reasons as
`assets/creatures/greybox/build_greybox.py` — see that file's header.

After a rebuild, re-import so the `.glb.import` sidecar matches the new bytes:

```
Godot_v4.7-stable_mono_win64_console.exe --headless --path <repo> --import
```

## Dimensions — BEFORE and AFTER, both measured in engine

Every number in every column is `GreyboxPlayerLab`'s, taken off emitted vertices; the three logs
are in `docs/qa/BODY-3/`. The "greybox" column is `greybox_classic`, and it and the AFTER column
are the same body to the millimetre except where the last column says otherwise.

| Measure | greybox (the target) | BoxKid BEFORE | BoxKid AFTER |
|---|---|---|---|
| crown | 1.2000 | 1.2000 | 1.2000 |
| eye centre | 1.0400 | 1.0400 | 1.0400 |
| Head | 0.880..1.200, x ±0.175→0.166, z ±0.165→0.157 | 0.860..1.200, x ±0.170, z ±0.170 | **matches** |
| Torso | 0.550..0.880, x ±0.150→0.158, z ±0.145→0.150 | 0.532..0.920, x ±0.200, z ±0.120 | **matches** |
| Hips | 0.420..0.550, x ±0.145→0.150, z ±0.140→0.145 | 0.420..0.532, x ±0.200, z ±0.120 | **matches** |
| shoulder axis, arm | x ±0.175; upper 0.200 + fore 0.180 = 0.380 | x ±0.258; upper 0.156 + fore 0.284 = 0.440 | **matches** |
| hip / knee, leg | 0.440 / 0.220; thigh 0.220 + shin 0.220 | 0.548 / 0.274; thigh 0.274 + shin 0.274 | **matches** |
| leg axis | x ±0.110 | x ±0.190 | **matches** |
| leg column | one part, 0.000..0.242, half 0.050 x 0.055 | shin 0.028..0.274 + a 0.200-deep boot | shin 0.024..0.242 + foot 0.000..0.030, **same half-extents, same union** |
| eyes | sphere r 0.046 | flat panel 0.060 x 0.060 x 0.030 | sphere r 0.046 (±3 mm of facet phase in X/Z) |
| mouth centre | 0.965 | 0.942 | 0.965 |
| bounds w x h x d | 0.4540 x 1.2000 x 0.3618 | 0.6360 x 1.2000 x 0.3650 | 0.4540 x 1.2000 x 0.3607 |
| half-width | 0.2040 | 0.2502 | 0.2037 |
| CapsuleRadiusM | 0.1456 | 0.1786 | 0.1454 |
| ShadowRadiusM | 0.1860 | 0.2283 | 0.1858 |

**The two pinned numbers are not this asset's, and never were.** Crown 1.200 and eye centre 1.040
are the greybox's (GREY-1), `AvatarProportions` sizes the collision capsule off the measured crown
and takes the aim ray's origin from the eye centre, and `build_boxkid.py` still **imports** those
two from `build_greybox.py`. **Every other joint height is this file's now** and is the classic
greybox's, measured rather than copied — see THE JOINT BLOCK in `build_boxkid.py` for each number's
provenance and for what it used to be. Note what the BEFORE column shows: the two pinned numbers
matched perfectly and nothing else did, which is exactly how a body can be the right height and
still be a different character.

**The foot is the one deliberate difference from the greybox**, which has none. ANIM-M2b's
`LimbIk.LevelAnkle` levels the sole about a pivot that has to be at y = 0, and
`GreyboxAssetContractTests` pins the ankle origin, the foot's geometry and the shin's clearance —
so deleting the foot would red a test and silently re-open the foot-slip error that test closes. It
survives with the **shin's own cross-section and no toe**, so shin and foot together occupy exactly
the volume the greybox's shin occupies and the ankle is a seam inside the greybox's own outline.

## Six volumes, seventeen meshes

The silhouette is six volumes. The file has the sixteen contract meshes plus the mouth, because the
body has to bend at the waist, the elbow, the knee and the ankle and a single solid cannot: the
body is `Hips` + `Torso`, each arm is `Arm*` + `Forearm*`, each leg is `Thigh*` + `Shin*` +
`Foot*`, and the head carries `EyeL`/`EyeR`/`Mouth`. That is exactly `AvatarVisual`'s part
contract, which is why the body needs no new pose code.

**The trunk and the head are rectangular frusta, not boxes** (BODY-3): the classic greybox's hips
widen 0.145 → 0.150 on the way up, its torso 0.150 → 0.158, its head narrows 0.175 → 0.166. Those
five millimetres a section are what separate a young figure with a waist from a slab, and they are
why `build_boxkid.py` grew a `frustum()` helper that `build_greybox.py` has no equivalent of.

Node tree (ANIM-M2's, unchanged — the legs hang off `Rig`, outside `Body`'s scale and `Pose`'s
lean, which is RIG-1's invariant):

```
Rig
+- Pose            runtime-owned; never keyed in a clip
|  +- Body         runtime-owned; never keyed in a clip
|     +- Hips
|     +- Waist
|        +- Torso
|        +- Head    -> EyeL, EyeR, Mouth
|        +- ArmL    -> ForearmL
|        +- ArmR    -> ForearmR
+- ThighL          -> ShinL -> FootL
+- ThighR          -> ShinR -> FootR
```

Rigid nodes, **no skin** — `AvatarVisual` writes node transforms and cannot drive a skeleton.

## Animation

The clip library is the greybox's, run against this scene: `build_boxkid.py` calls
`author_greybox_clips.author()`, which keys rotations only, on six nodes (`ThighL`, `ThighR`,
`ArmL`, `ArmR`, `Waist`, `Head`). So the same fourteen clips bind verbatim and
`AvatarClipDirector` — which refuses a model missing any of them — is satisfied.

**BODY-3 had to re-scale the gait, and this is the trap worth knowing.** That module's `LEG_M` is
`gx.HIP_JOINT_Y`, read ONCE at its own import, and step length, stance reach, the leg-swing cap and
the duty factor all derive from it. Shortening the leg 0.548 → 0.440 without telling it would
author a stride a quarter too long onto this body, and the tell would be a planted foot skating a
few millimetres a step — silent, and exactly the failure BT-7's "import the pivots, never copy
them" rule existed to prevent. `build_boxkid.main()` therefore assigns this file's `HIP_JOINT_Y`
onto the `gx` module on the line immediately before that import. The rebuilt library's own
stance-drift check reads **0.11 mm at Walk and 1.34 mm at Run against a 3.00 mm bar**, and both feet
sweep 0.5507 m — the new leg's reach cap, not the old one's.

The `.glb.import` sets `animation/fps=120` (the authoring rate) and `loop_mode=1` on the eight
looping clips (`Idle`, `Walk`, `Run`, `Jump_Air`, `Hold_Empty`, `Hold_NetReady`, `Carry_Handle`,
`Carry_Armful`) — matched to `Greybox.glb.import`, because a walk cycle imported as a one-shot
plays once and stops.

## Colour

Two materials, `BoxKidBody` and `BoxKidFace`. The runtime overrides the body one per player —
`AvatarVisual.BuildAuthoredRig` tints every mesh with the dealt palette colour and skips the three
face meshes — so the greys in the `.blend` are what the editor shows, never what the game shows.
In `--world bubbletest` the colour is dealt at random from the palette's first six
(`SandboxAvatar.RandomPaletteIndexFor`, seeded and logged); everywhere else it is the round robin.

## Evidence

`docs/qa/BODY-3/` — the headed side-by-side, three columns (greybox / BoxKid before / BoxKid after)
at standing, silhouette, mid-walk and mid-run, plus the three `GreyboxPlayerLab` logs the dimension
table above is read out of. `side-by-side-standing.png` is what closes Talon's note;
`side-by-side-moving.png` is the "and feels" half — the waddle hop reads 0.082 m walking and
0.061 m running on the greybox and on the conformed body, against 0.103 / 0.086 before, because
that channel scales off the body it is applied to.

`docs/qa/bubble-test/BT-7/` — headed in-engine captures of the body as BT-7 shipped it, kept as the
before-state and as the Y-up proof the standing memory requires.
