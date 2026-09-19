---
packet: FRAME-1
role: environment
date: 2026-09-04
branch: feat/2026-09-04-frame-1-secret-room-picture
base: playtest/2026-09-04-combined
type: report
---

# FRAME-1 — a picture on the wall of the room behind the lake TV

ROLE: environment — PACKET: FRAME-1 — TOKEN: ENV-BASALT-23

> # Talon's drawing lives at `resources/SecretRoomPicture.png`
> Replace that one file and the wall changes. No code edit, no scene edit — just drop the PNG in
> and open the project once (or run `godot --headless --path . --import`).

The room behind the sunken lake television now has Talon's own crayon drawing on its wall — "fwends",
four televisions, four figures on a torn sheet of paper with masking tape at the top — 3.64 × 2.76 m
of it, lit by one lamp, transparency respected, readable from the point the player arrives at.

---

## 1. Which television, and the evidence

**`BubbleTestLayout.TvRoutes`' sixth and last row** is the lake's. Identified, not guessed:

| evidence | where |
|---|---|
| keyed on `BubbleTestLayout.SunkenTvNodeName` (`"SunkenTv"`) | `BubbleTestLayout.cs`, the `TvRoutes` initialiser |
| its entrance stands at `SunkenTvPos`, composed from `WaterGeometry.BubbleTestLakeCentreX` and `…CentreZ` — **the lake's own centre** — at `SunkenTvBaseY` = −2.05 m, which is 1.47 m below `WaterGeometry.WaterY` | `BubbleTestLayout.cs` |
| it is the only route whose entrance is not on dry authored ground; its own doc comment is EGG-2's "a sixth TV, hidden underwater in the level's lake/river feature" | `BubbleTestLayout.cs` |
| it is the only route with a *named* return (`SunkenTvReturn`, the south bank) because the ordinary offset would put the player back in deep water with a fresh drowning clock | `BubbleTestWorld.ReturnFor` |
| `ReturnTvPath = "RoomF/RoomTv"`, `RoomCentre = (−17, 0, −17)` | `BubbleTestLayout.cs` |

So the room is **RoomF, "the Deep Room"** — `TvRoomAnchor + (−17, 0, −17)` = world `(−17, −20, −17)`,
10 × 12 × 4 m, silt floor, murk walls, emissive water ceiling. The drawing is on its **north wall**,
because `ArrivalOf(route)` lands the traveller at room-local `(0, 0.4, 3)` and a teleport resets
`MoveState.Yaw` to 0 (−Z), so a player arrives 9.0 m from that wall already facing it.

**Independently confirmed.** This packet reached RoomF from the table before the orchestrator sent
its own identification, and the two agree; SHADER-2 separately located the sunken television's
plinth as `SunkenTvPlinth/Column/Mesh` in `GreenHills.tscn`, which is the lake section, and that
plinth is what `SunkenTvPos` stands on. Three routes to the same room, no reconciliation needed.

**`RoomE` was never a candidate here, and it is worth writing down why it must not become one.**
RoomE belongs to the `HiddenTv` route, which is not in the lake at all — it is the tangle spire's
spur pad at 34.782 m — and `BubbleTestWorld.DestinationFor` overrides that route to the PuffinLab's
`Arrival` whenever the lab loaded, because `LabRouteEntranceName = HiddenTvNodeName`. The lab loads
in every shipped build, so **RoomE is only reached in the lab-absent fallback** and anything hung
there would be invisible to every player. BUBBLE-1 measured the same thing from the other side
("RoomE got zero; the lab is its replacement"). Nothing in this packet touches RoomE.

**`TvRoutes` has SIX entries, not five**, and two prose comments in `BubbleTestLayout.cs` still say
five — the `TvRoomPlanHalf` note at the top of the TV-room block, and the bubble-spread note lower
down. Both predate EGG-2's sunken television. Nothing in FRAME-1 keys off a count: every constant
here is addressed by node path (`RoomF/SecretPicture`) and every derivation is from the room's own
dimensions. **The two stale comments were deliberately NOT edited** — the lower one is inside
BUBBLE-1's live subject (bubbles per room) and this packet does not reach into another agent's
sentence mid-wave. Flagged for whoever closes the wave.

## 2. What Talon's image has to be — in plain terms

Everything below is already true of the drawing that ships. It is here for the next one.

- **Where it goes:** `resources/SecretRoomPicture.png`. That exact name, in that folder.
- **Format:** PNG with transparency (RGBA). Transparent areas show the dark wall through them —
  that is deliberate and it is how the current drawing's torn paper edge works.
- **Shape:** *any*. Tall, wide, square — the game measures the picture and fits it to the wall
  without stretching, squashing or cropping it. Nothing has to be drawn to a ratio.
- **Size in pixels:** anything up to about **2048 across the long edge**. The current one is
  1388 × 1053. Bigger than 2048 is wasted — it will not look better and it costs memory.
- **Colour:** ordinary sRGB, the default any paint program and any phone saves. Nothing special.
- **Do not** put a white or black background behind the art to "fill in" the transparent parts —
  the transparency is what makes it sit on the wall instead of in a box.
- **Nothing else.** No import settings to touch, no code to change. Drop it in, open the project
  once so Godot imports it, done. Delete it and the wall goes back to an empty lit mount with a
  clean log, so a half-finished swap can never break a build.

## 3. What was built

| thing | where |
|---|---|
| The drawing | `resources/SecretRoomPicture.png` (+ `.import`) |
| The mount: `Canvas` (the sheet), `MountRail`, `LampDrop`, `LampHood`, `PictureLight` under `RoomF/SecretPicture` | `scenes/game/world/bubbletest/sections/TvRoom.tscn` |
| `SecretPictureImagePath`, `SecretPicturePath`, `SecretPictureCanvasName`, `RoomFWallWidthM`, `SecretPictureMountM`, `SecretPictureCentreY` | `scripts/game/world/bubbletest/BubbleTestLayout.cs` |
| `SetUpSecretPicture()` — the `ResourceLoader.Exists` guard, the aspect fit, the visibility flip | `scripts/game/world/bubbletest/BubbleTestWorld.cs` |
| `CheckSecretPicture()` — height pin, mount-box bound, alpha-mode assertion, shadow assertion | `scripts/game/world/bubbletest/BubbleTestSelfTest.cs` |
| Capture driver | `tools/dev/frame1_capture.ps1` |
| Captures, the control patterns and their generator, the index | `docs/qa/FRAME-1/` |
| Level note | `docs/levels/2026-09-04-FRAME-1-secret-room-picture.md` |

### The numbers, and where each comes from

| quantity | value | derived from |
|---|---|---|
| mount box | 8.759 × 2.759 m | each wall bound less **one whole `AvatarProportions.PlayerCrownM`** (1.241 m, measured off the shipped whole-figure model), split per side |
| sheet | **3.637 × 2.759 m** | the drawing's exact 1388 : 1053 fitted inside it; the height binds |
| centre height | **2.000 m** | maximising under an even margin puts the centre at the wall's mid-height by construction (`margin/2 + height/2 = TvRoomHeight/2`) |
| viewing distance | 9.0 m | `ArrivalOf(route)` to the wall's inner face |

The engine agrees, from the shipped launch log:

```
[bubbletest.picture] res://resources/SecretRoomPicture.png hung in the Deep Room —
  1388x1053 px fitted to 3.637 x 2.759 m inside a 8.759 x 2.759 m mount,
  aspect preserved, alpha blended.
```

### The light — exactly what was added

One `SpotLight3D` (`RoomF/SecretPicture/PictureLight`) on a ceiling drop 3.0 m out from the wall:
warm `(1, 0.93, 0.84)`, energy 5.0, range 6.5 m, cone 44°, **`shadow_enabled = false`**, plus its
two-box fixture (`LampDrop`, `LampHood`) so the light has a visible source.

- The room's own `RoomFill` omni is **untouched**.
- **No second `WorldEnvironment`** — EGG-1's recorded trap, not repeated.
- Ceiling-mounted rather than on a bracket, and that is measured rather than styled: a lamp 0.8 m
  out above a 2.76 m sheet gives incidence 0.91 at the top against 0.17 at the bottom (clipped top,
  black bottom). From `(0, 1.50, 3.00)` sheet-local it runs 1.00 → 0.72 down the height and never
  below 0.66 at the far corners. The first pass shipped the short bracket and the capture showed
  the clipping; this is the fix.

## 4. Acceptance criteria

| # | criterion | result |
|---|---|---|
| 1 | `dotnet build WatisWorld.sln` 0 errors | **PASS** — `Build succeeded. 0 Error(s)` (4 pre-existing warnings, all in `scripts/game/sandbox/**`, untouched by this packet) |
| 2 | report names the lake `TvRoutes` entry + evidence | **PASS** — §1 |
| 3 | headed capture from the arrival point, `--capture-cam` | **PASS** — `docs/qa/FRAME-1/shipped-arrival/`, cam `-17,-19.005,-14` = `ArrivalOf(route)` at the avatar's measured eye height. "fwends" reads and the four figures count without walking up |
| 4 | transparency proven, alpha vs opaque | **PASS** — `shipped-close/` shows the murk wall through the torn edge and all around the paper; the controlled A/B is `control-alpha-close/` vs `control-opaque-close/`, identical checkerboard geometry, one difference |
| 5 | empty state deliberate, 0 ERROR, positive control | **PASS** — `empty-arrival/`, `empty-close/`: a lit mount rail under a lit lamp, nothing hung. Case-sensitive `ERROR` scan of all 8 log files = **0** for both the shipped and the empty runs. Positive control: with the guard inverted and the file moved aside, the same scan finds **2** ERROR lines from `SetUpSecretPicture` line 161 — full text in `docs/qa/FRAME-1/README.md` |
| 6 | `Run-TvPortal*` and `Run-BubbleTestWorldTest` pass, CheckBake equal on the TV room | **PASS** — `TV PORTAL TEST OVERALL: PASS`, `BUBBLE-TEST WORLD TEST OVERALL: PASS`, and `TvRoom packed[mesh=489 shape=59 body=50] live[mesh=489 shape=59 body=50] OK` |
| 7 | both suite commands on the tip, raw counts | see §5 |
| 8 | report with corrections, bible check, drop-in path, image constraints | this file |

### The new test can fail — proved, not assumed

`BubbleTestSelfTest.CheckSecretPicture` was run against a scratch scene with the picture nudged to
local y = 1.7. The suite went red with exactly one failure and printed the discriminating quantity:

```
[bubbletest-selftest]   secret picture: centre y 1.700 m authored, 2.000 m derived; mount box 8.759 x 2.759 m
[bubbletest-selftest] FAIL (1)
FAIL: bubble test self-test exited 1
```

The scene was restored to 2.0 and the suite re-run green.

## 5. Suites — raw counts

**Baseline**, measured in a throwaway detached worktree at the same base
(`C:/repos/Watis-frame1-baseline`, detached at `418556d`), so the tip is compared against something
rather than against memory:

```
dotnet test tests/unit/SailNet.Tests.csproj
Passed!  - Failed:     0, Passed:  2269, Skipped:     0, Total:  2269, Duration: 3 s - SailNet.Tests.dll (net8.0)
```

**On the tip:**

```
dotnet test tests/unit/SailNet.Tests.csproj
Passed!  - Failed:     0, Passed:  2269, Skipped:     0, Total:  2269, Duration: 3 s - SailNet.Tests.dll (net8.0)

powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180
293 rows in the run's own === summary === block, counted with the anchored pattern
^  .+ (PASS|FAIL)$:

    291 PASS   2 FAIL   ->   OVERALL: FAIL

  Netcode: anti-cheat        FAIL
  Reconnect: grace window    FAIL
```

### Discriminating the two reds

**Both are named, by name, on `.claude/rules/test-suite.md`'s load-sensitive list**, and neither
touches anything this packet changed — FRAME-1's diff is a mesh, a material, one light, one guarded
texture load and a self-test method, in a sealed room 20 m underground. No netcode, no reconnect
path, no avatar lifecycle. Discriminated by the rules file's own procedure: re-run standalone
3× per side and compare the RAW quantity rather than the verdict.

**`Netcode: anti-cheat` — 3/3 PASS standalone.** The marathon's failing check was verbatim the
string the rules file already quotes for this suite:
`scenario 2: prop 1 was never observed held by CheatCarrier - the grab never landed` — the staging
half, not the containment assertion the suite exists for. The raw quantity did not move:

| run | peak 1.00 s window | displacement / samples | verdict |
|---|---|---|---|
| marathon | 3.81 m/s | 15.3 m / 58 | FAIL (scenario 2 staging) |
| `Run-CheatTest.ps1 -SkipBuild` ×1 | 3.80 m/s | 15.2 m / 59 | PASS |
| ×2 | 3.81 m/s | 15.2 m / 58 | PASS |
| ×3 | 3.82 m/s | 15.3 m / 59 | PASS |

**`Reconnect: grace window` — 2/3 PASS standalone, and the red REPRODUCED**, with a
byte-identical single assertion each time:

```
post-resume avatarCount = 1, expected 2 (BotA + BotB)
  - stale/duplicate avatar node(s) left over from the pre-drop session
```

A join/teardown race, and the honest reading is **not** "green when standalone" — it is roughly one
in three under this machine's load. That rate is the new fact and it is written up as a measured
entry in `.claude/rules/test-suite.md` (a write the file's own header grants to any role, recorded
in `ROLE.md`), because a future packet that re-runs this suite once and sees green would draw the
wrong conclusion.

**What else was running.** Never an idle machine: six Godot processes belonging to other worktrees
and to live playtest clients were up throughout, and BUBBLE-1's `Run-AllTests.ps1` out of
`C:\repos\Watis-bubble1` held the machine-wide mutex for ~23 minutes immediately before this
marathon could start (`-MutexTimeoutMinutes 180` is what let it wait rather than time out). No other
agent's process was investigated or killed.

**The rows that bear on this packet were all green in the same marathon:**

```
  World: bubble test (BT-0)  PASS
  World: TV portal (BT-10)   PASS
  Bubbles: shared counter    PASS
```

plus the two targeted runs quoted under criterion 6, with
`TvRoom packed[mesh=489 shape=59 body=50] live[mesh=489 shape=59 body=50] OK`.

## 6. The bible check

```
Bibles applied:  LEVEL-BIBLE (this is composition — a reward placed in a room, and whether the
                 room reads) and ART-BIBLE (colour, material, a texture, and a light). Not
                 Behavior: nothing here moves or decides. Not Mechanics: it holds no state, no
                 timer, no win condition. Not Interaction: it is deliberately not interactable —
                 no press-to-inspect, no zoom, no pickup. Talon asked for a drawing on a wall.

Items checked:   LEVEL-BIBLE §8.1 (ambient legibility — the level's own state perceivable without
                 UI); §8.4's landmark mechanism (one thing visible from arrival orients you);
                 §5.2's mandatory relax valley, which a secret reward room is; §0 ("a level
                 composes contracted entities; it does not define new ones") — this is geometry
                 plus a material plus a light, and grants nothing any entity's contract does not
                 already declare; §2.2's light-and-colour channel (cool/dark/enclosed reads
                 dangerous — the warm lamp is the deliberate counter-signal that this is a reward).
                 ART-BIBLE §1 pillar 2 ("restraint with intent — an accent earns its place by
                 pointing attention"); §2.3 (one hero accent per zone, max two — the drawing is
                 RoomF's one, and the budget is now spent); §2.4 (the sheet is an EVENT, not
                 landscape, which is what licenses a high-value surface against a wall
                 deliberately below the landscape floor; check-question 4 — take the events out
                 and RoomF is boring, which it is: silt, murk, three stones); §3 and its third
                 corollary (separate on VALUE not hue; the accent goes on the object, never on the
                 wall — nothing was done to the wall); §4.2 (one material per unique appearance —
                 the drawing shares its material with nothing and none was created per instance);
                 §4.4 (import settings are part of the contract: desktop compression only,
                 never etc2_astc — set to VRAM Compressed / high quality = BPTC, mipmaps on,
                 detect_3d/compress_to=0 so the mode cannot change silently later); §7 amendment
                 item 3 (real-time shadows are OUT — the lamp ships shadow_enabled = false and the
                 self-test asserts it); §7 item 2 (no colour grade added).

Result:          Pass, with ONE item knowingly spent rather than met — see §7 item (a). ART-BIBLE
                 §4.4's VRAM row says "there is no second texture (contract §6.3)", and the
                 drawing is the second texture in the game. That is not a rule this packet could
                 satisfy and also do what Talon asked; it is recorded as a named exception with
                 the measured cost rather than quietly broken. Everything else passed as authored;
                 nothing had to be fixed to make it pass except the lamp, which was moved from a
                 short bracket to a ceiling drop after the first capture showed it clipping.
```

## 7. Corrections to the packet

**(a) ART-BIBLE §4.4's "there is no second texture" is knowingly spent, with the cost measured.**
The bible budgets ≈ 1.4 MB of texture for the whole game world and names the shared decal atlas as
the only one. Talon's drawing is the second. Measured: 1388 × 1053 at BPTC (BC7, 1 byte/texel) is
**1.39 MB**, **1.86 MB** with mipmaps. Two clean outs existed — fold it into the decal atlas, or
declare a named exception — and the exception is the right one: the atlas is 128 px/m trim-sheet
territory and this is a single 3.6 m hero surface seen from 3–9 m. Not a rule to change on my own
say-so; recorded here so the next texture is a *decision* rather than a precedent. The `.import` is
set so this is the cheapest correct form of the cost (BPTC, mipmaps, no silent re-import).

**(b) The eye-height requirement and "make it BIG" are arithmetically incompatible; size won.**
Criterion 1 asked for the picture "at a natural eye height for the shipped avatar", derived from the
avatar's real capsule. Talon then asked for it "large enough to at least the player can see it". In
a 4 m room with a 1.24 m avatar, a sheet centred on the 0.995 m eyeline can be at most 1.99 m tall —
2.62 m wide — against the 3.64 m the wall carries. The later instruction wins. **The avatar is still
the ruler**: `AvatarProportions.PlayerCrownM` sets the mount box's margin, so re-measuring the body
resizes the drawing, and `CheckSecretPicture` goes red if the scene and the constant drift apart.
The eyeline now falls 13.6% up the sheet; at 9.0 m the whole wall is inside the camera's vertical
field several times over, so nothing is craned at.

**(c) "A framed picture" became a taped sheet, and the artwork made that call.** The packet's word
was "frame"; Talon's own words allowed either ("a picture frame and or like a drawing on the wall").
The drawing that arrived has its own irregular torn edge and its own strip of masking tape drawn
into it, and 24.1% of the image is fully transparent. A rectangular moulding would box a thing that
already draws its own edge and would cover the alpha the request was about. It hangs as a sheet
taped to the wall. One slim `MountRail` batten survives 0.14 m above the paper — it boxes nothing,
overlaps nothing and hides no alpha, and it is what makes the *empty* state read as "a drawing
hangs here" instead of a lamp aimed at nothing. That was measured: the pass without it is a bare lit
oval on a wall, and the pass with it is in `docs/qa/FRAME-1/empty-arrival/`.

**(d) DISCLOSED WRITE OUTSIDE THE ROLE'S ALLOWED PATHS: `resources/`.** `ROLE.md` grants
`tools/dev/**`, `scenes/game/world/**`, `assets/terrain/**`, `docs/levels/**`, `docs/qa/**`,
`scripts/game/world/bubbletest/**`, the outbox and two named files. `resources/SecretRoomPicture.png`
and its `.import` are outside all of them, and the packet cannot be completed without them: the
artwork has to ship somewhere, and `res://resources/` is where `Branding.WordmarkImagePath` already
puts the other artist-supplied asset this project is waiting on. Nothing else in `resources/` was
touched. Per `ROLE.md`'s own precedent — "BT-1 and BT-11 were both *granted* this by their packets
while this file still forbade it, so a role reading its contract strictly had to choose between the
packet and the contract" — **the orchestrator should record the grant**, either as a standing
`resources/**` line for artwork drop-in or as a per-packet grant. Flagged rather than self-corrected.

**(e) The ERROR probe had to be made case-sensitive, and that is a live trap for the next packet.**
PowerShell's `Select-String` is case-insensitive by default. The first empty-state scan reported
1 ERROR per log — it was matching the phrase "not an error" inside the guard's own friendly
`GD.Print`. Every count in this report is `-CaseSensitive`. A packet that scans a log for `ERROR`
without that flag is measuring its own log messages.

**(f) The packet's `-MutexTimeoutMinutes 180` only exists on `Run-AllTests.ps1`.**
`Run-BubbleTestWorldTest.ps1` takes only `-SkipBuild` and `Run-TvPortalTest.ps1` takes
`-Port`/`-DurationSec`/`-SkipBuild`; passing the mutex flag to either is a hard parameter-binding
error. They were run without it.

**(g) `TvRoom.tscn` is hand-authored, not generated.** Checked before editing, per the packet's "a
generated scene is never hand-edited before its generator has been re-run": nothing under
`tools/dev/` or `scripts/` writes it. The one reference to it outside the layout files is a source
assertion in `tests/unit/HudBubbleCountTests.cs`, which pins `tv_static.tres` and is unaffected.

## 8. Out of scope, untouched

The Den's crooked frames (this file's own header ruling 4 governs them and they are exactly as they
were); the sunken television's own material and the boxy artifact under it (SHADER-2's); bubble
counts and placement, including anything BUBBLE-1 puts in this room (BUBBLE-1's); the sixth
television, the PuffinLab rooftop, the vantage platform, the diving board and the 100th bubble
(ROOFTOP-1's); the TV routes, arrival points, return trips, and every part of RoomF's geometry
except the wall the drawing hangs on. No interaction of any kind was added.

## 9. Open questions

**Is 3.64 × 2.76 m the size he wants?** It is the largest the wall carries under a margin of one
avatar height, and `docs/qa/FRAME-1/shipped-arrival/` is what it looks like from where the player
lands. If he wants it bigger, the only remaining room is the margin — dropping to half an avatar of
clearance gives about 4.5 × 3.4 m, at which point the sheet is 85% of the wall's height and reads as
wallpaper rather than as a drawing someone taped up. That is a look call, not a number, so it is
his.

**Should the room's fill light warm up?** RoomF is deliberately cold and dark, which is what makes
the one warm lit thing land. This packet did not touch `RoomFill` and would not without a direction.
