---
type: playtest-notes
from: Talon (played the merged wave-1 build)
date: 2026-08-29
build: feat/2026-08-27-bubble-test @ 6752c625, played in C:\repos\Sail-playtest
status: diagnosed, not yet fixed
---

# Talon's playtest notes — 2026-08-29, the wave-1 bubble test

**Talon's framing, verbatim:** *"these are just notes to be fixed at some point doesn't have to be
right now"* and *"More notes will come in the future."* Nothing here is an emergency; all five are
real. Each note below carries **his words**, then what the code actually does, so nobody re-derives
the diagnosis or fixes the wrong layer.

---

## Note 1 — the bubble counter is overblown

> *"the bubble counter is completely over blown it is the light is too bright. It is too bright and
> the camera makes it overblown brightness and it's blurry."*

**Diagnosed.** Three separate contributors, and they are not the same fix:

1. `scenes/game/props/BubbleCounterDisplay.tscn` sets `emission_energy_multiplier = 200.0`. That
   200 was raised from 40 by FIX-1 (2026-08-28) **by measurement against the night fog that DARK-1
   has since removed**. The scene's own comment states the calibration; the calibration's premise
   is gone. DARK-1 measured the result as a white slab day and night, proposed **~8**, and
   correctly routed it rather than editing another packet's file.
2. *"the camera makes it overblown"* — this is the second contributor and it is **not** the
   material. An emissive surface at 200 drives glow/bloom in the camera's post chain; the bleed is
   what makes it read as a blown slab rather than a bright sign. Whoever takes this must judge the
   material and the glow threshold **together**, or lowering emission alone will just make a dim
   slab that still blooms.
3. *"and it's blurry"* — treat as a third symptom, not a restatement. Candidates, in order:
   glow bleed at this intensity; the `TextMesh` glyph caps failing (see below), which leaves a
   hollow shell that antialiases to mush; `pixel_size = 0.01` at `font_size = 96`.

**Also on this prop, found before Talon's notes and still undiagnosed:**
`ERROR: Convex decomposing failed` fires **exactly twice per headed session**. Established by
elimination — a headless world build produces **zero**; a one-font-per-process probe of
`WorkSans.ttf` against `"- / -"`, `"0 / 6"`, `"12 / 100"`, `"100 / 100"`, `"-"`, `"/"`, `"0"`,
`"O"`, `"8"` at depth 0.0 and 0.02 produces **zero**. So it is neither the font nor the string. It
lands after `[client] connected as peer`, i.e. on the **synced** text path. Twice is consistent
with `FaceSouth` and `FaceNorth` sharing one `SubResource("TextMesh_glyphs")`.
**Do not chase it with vertex counts or `get_aabb()`** — `tools/dev/textmesh_probe.gd`'s header
documents that neither detects this failure, because the triangulator drops the front/back caps,
keeps the extruded sides and carries on. Engine stderr is the only detector. Needs a **headed**
host+client repro.

**Constraint on any fix:** BT-8 acceptance criterion 3 is "readable at 20 m at night". Do not trade
Talon's overblow complaint for a board nobody can read. Measure at 6 m and 20 m, both states.

---

## Note 2 — the water does not kill the player, and it is supposed to

> *"there is a problem with the water as it currently is the water should kill the player after
> about three seconds, and this does not happen. This was written to the game before and it has
> been updated to include this. I'm part of the game already. This is already been built."*

**Talon is right, and the reason is exact: the mechanism is built and wired to nothing.**

- `scripts/game/run/RespawnService.cs` declares `ServerKill(int peerId, RespawnCause cause)` as the
  single authoritative kill seam, and its own docs cite *"Talon's drowning ruling"*.
- **`ServerKill` has zero callers anywhere outside its own file.** Verified by repo-wide grep.
- `RespawnCause` has exactly three members — `Unknown = 0`, `OffTheEdge = 1`, `Void = 2`. **There
  is no `Drowned` cause.**

So nothing is broken in the sense of a regression; the submersion timer that would call it was
never written. What is needed: a `Drowned` cause, a server-side submersion timer, and the call.

**The value:** Talon said *"about three seconds"* — take **3.0 s** submerged and say so, rather
than escalating a value question. Canon already settles the rest: drowning is a **death with a
respawn**, not a faint, and under the caveman register a comic drowning death is in-bounds.
Server-authoritative, like every other kill — the client requests nothing here.

---

## Note 3 — invisible water outside the map, past the green zone

> *"the water outside the map, around the green zone. If the player falls off the map this will
> allow the player to continue swimming as if there is nothing there like floating in air invisible
> water. I would like this to be addressed."*

**Diagnosed, and it is a one-line shape problem.** `scripts/game/water/WaterGeometry.cs`:

```csharp
public const float WaterY = -0.58f;
public const float ShoreX = -45.0f;

public static float DepthAt(Vector3 feetPosition)
{
    if (feetPosition.X > ShoreX) return <dry>;
    return WaterY - feetPosition.Y;
}
```

**The lake is a half-plane, not a lake.** Any point with `X <= -45` is "in water" regardless of Z,
regardless of how far west, and at any depth below `WaterY`. Line 204's region test is the same
shape: `float.IsFinite(position.X) && position.X <= ShoreX`. So walking off the western edge puts
the player in water that extends forever with nothing rendered in it — precisely *"floating in air
invisible water."*

This also **interacts with note 2**: once drowning is wired, an unbounded lake means falling off
the map silently drowns you instead of reading as `OffTheEdge`. **Notes 2 and 3 must be fixed
together, in that order**, or the fix for one produces a worse bug in the other.

Bounding the lake is a `WaterGeometry` change, so it is single-writer on that file — one packet
owns both notes.

---

## Note 4 — the reset lever needs a sign and a warning

> *"the reset lever next to the bubble counter. There needs to be a sign at least that it will
> reset the bubble counter and a warning ... because I would be upset if I spent hours collecting
> all the bubbles and then someone reset my progress."*

**Diagnosed.** `scripts/game/bubble/BubbleResetGate.cs` exists but is **only a 1.2 s debounce** —
its stated job is collapsing a double-tap and "everybody grabbed the lever at once" into one reset.
It is not a confirmation and was never meant to be. There is **no signage on the lever and no
warning before the wipe.**

Two distinct asks, and both are wanted:
1. **A sign** — diegetic, saying what the lever does. Note the Hub's six diegetic `Label3D`s that
   UI-1 kept are the established local idiom for this; match them rather than inventing a widget.
   Note also that UI-1 just removed the *billboarded* labels, so the sign must not billboard.
2. **A warning/confirmation** — a hold-to-confirm or a two-stage press. It must survive six players
   reaching one lever, so it is adjudicated **server-side** exactly as the debounce already is.

This is an **INTERACTION-BIBLE** feature (the player acts on it directly) and a **MECHANICS**
one (a destructive state transition with a confirmation gate). Both apply.

---

## Note 5 — the orange on the Host button clashes

> *"the UI elements in the main menu. I don't like the orange bar that is the button for the host
> game simply please remove this orange it clashes with the game aesthetic overall."*

**Located.** `scripts/ui/campfire/MenuLook.cs`:

```csharp
public static readonly Color Ember        = Rgb(154, 85, 39);   // the fill Talon is objecting to
public static readonly Color EmberHover   = Rgb(172, 96, 44);
public static readonly Color EmberPressed = Rgb(130, 71, 31);
public static readonly Color OnEmber      = Colors.White;
```

`MenuActionButton.cs` fills the primary action with `MenuLook.Ember` at rest and swaps to
`EmberHover`/`EmberPressed`. So it is **four constants in one file**, not a rebuild.

**The constraint that makes this non-trivial:** `MenuActionButton.cs:17` records the ember fill
with a white label as a **measured 5.67:1** contrast pair, and the focus ring is separately
required to hold 3:1 against the fill. Any replacement must be re-measured with `contrast.py`, not
eyeballed — dropping the orange must not drop the button below AA.

**This is MENU-2, which was already scoped and never dispatched** — the planned dark pass on
`MenuLook.cs` only. Fold this note into it rather than opening a new packet, and remember the
standing ruling that MENU-1 shipped the light version and `MenuLook.cs` is to be **re-skinned, not
rebuilt**.

---

## How these were tasked

| Note | Packet | File it owns | Why grouped this way |
|---|---|---|---|
| 2 + 3 | **WATER-3** | `WaterGeometry.cs`, `RespawnService.cs` | Same file, and fixing either alone makes the other worse |
| 1 | **COUNTER-1** | `BubbleCounterDisplay.tscn` + glow | Carries the undiagnosed `Convex` error on the same prop |
| 4 | **LEVER-1** | `BubbleResetLever`/`BubbleResetGate` | Interaction + Mechanics |
| 5 | **MENU-2** | `MenuLook.cs` | Already-scoped packet; four constants, re-measure contrast |

WATER-3 is the one dispatched first: it is the only note that is a missing *mechanic* rather than a
finish problem, and Talon named it "a problem" rather than a preference.
