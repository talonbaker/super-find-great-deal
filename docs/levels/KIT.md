# The kit — six primitives whose form is their function

**LD-6, 2026-09-02.** The surface-and-shape vocabulary for the geometric, warped block. Six
primitives, one movement meaning each, carried by **form** — convexity, orientation, angle —
and never by colour. Colour stays reserved for goals.

The failure this is built against is documented: Mirror's Edge shipped surfaces that looked
climbable and were not, and the fix it reached for was a highlight system. A highlight is a
second language bolted onto a first one that does not work. The kit's bet is that the first
language can be made to work, and the way it is made to work is that **the surface a player
could mistake for the other kind does not exist in the scene** — every rule below is a
statement about what the generator refuses to write.

- **The spec** is this file.
- **The writer** is `tools/dev/kit_course.py`. It validates before it writes a byte.
- **The scene** is `scenes/dev/KitCourse.tscn`.
- **The proof** is `tools/dev/kit_selftest.gd`, run by `tests/Run-KitCourseTest.ps1`, which
  measures the shipped scene through Godot's own transform code and does not take the
  generator's word for anything.

---

## 1. The bands, and where their numbers come from

Every height and every gap in the kit is a **fraction of the motor's own arc**, never a metre
value. **Do not copy a motor number into this file or into a level.** Four measured numbers
were once typed into eight places; Talon's 2026-08-28 tuning ruling moved the motor and every
copy became wrong at once, in silence, under levels that had been sized against them.

The canonical card is [`docs/levels/METRICS-CARD.md`](METRICS-CARD.md) (LD-1). Everything on it
is derived, and there are now three authorships of that derivation, all parsing the same
primitive rows out of `MotorTuning.Default`:

| Authorship | For | Its own positive control |
|---|---|---|
| `scripts/net/MotorArc.cs` | the game | C# unit tests |
| `tools/dev/motor_arc.gd` | GDScript tools | `verify()` — reproduces MOVE-3e's four in-engine measurements |
| `tools/dev/kit_course.py` (`arc_for`) | the generator | `verify_motor()` — the same four, to the millimetre |

To see the envelope this course was actually built against, run the generator and read its
first lines — it prints the bands it derived:

```
python tools/dev/kit_course.py --print
```

The **vertical** bands are Kerb, Hop, Commit, Edge, Technique, Denial. The **horizontal** bands
are Stone, Jog, Sprint, Edge, Technique, Denial. `Edge`'s ceiling is the held-jump apex exactly,
and the horizontal `Edge`'s ceiling is the held-jump flat range exactly; `Technique` begins
where the single jump ends and is reached only with the double. `Denial` starts *above* the
double jump with margin, and there is a deliberate empty gap between the top of Technique and
the floor of Denial — a rise that lands in it is neither, and the validator says so rather than
rounding it into one.

`floor_max_angle` is Godot's 45° default. The kit leaves itself a 5° margin either side of it.

---

## 2. The one invariant everything else serves

> **THE FORBIDDEN BAND IS EMPTY.** Every upward-facing planar facet in the course wide enough
> to hold the body is either **≤ 40°** (a floor, and it looks like one) or **≥ 50°** (a wall,
> and it looks like one). Nothing sits between them.

A facet narrower than the body's own collision diameter is exempt, because a body cannot stand
on it whatever its angle — and that exemption is asserted per facet against a **measured**
number (`AvatarProportions.FallbackHalfWidthM × CapsuleRadiusFraction`, parsed from the C#, not
retyped), which is what lets a Tilt be a thin plate rather than a wedge of solid rock.

This is stronger than "label the ambiguous surfaces". There is nothing to label.

---

## 3. The six primitives

Every entry states five things, and the validator rule is an acceptance criterion, not a note.

### BLOCK — *land on it, stand on it, climb it*

| | |
|---|---|
| **Form** | A rectangular prism with a flat, horizontal top. Nothing else in the kit has one. |
| **Meaning** | Ground you can hold. A Block's top is a place to arrive. |
| **Collision** | `BoxShape3D`, same size as the mesh, in the same node. |
| **Validator** | Its top rises above its named neighbour by an amount inside **exactly one** band, and it declares which. A Block whose neighbour offers no standable top is a Denial that forgot to say so. Its plan gap from that neighbour is inside the Technique gap. |
| **Never** | Never a ramp, never tilted, never the thing you slide off. A Block that cannot be stood on is not a Block. |

### LOG — *roll along it, balance on it, ride the crown*

| | |
|---|---|
| **Form** | A horizontal cylinder. The crown is the only usable line; the flanks fall away on both sides. |
| **Meaning** | A narrow, committing line of travel. You go **along** a Log, never across it. |
| **Collision** | `CylinderShape3D` — the meaning **is** the collision shape. A box in its place is precisely the Mirror's Edge failure. |
| **Validator** | Cylinder collider; axis horizontal (a vertical cylinder is a pillar); crown walkable strip `2R·sin(45°)` **≥ the body's diameter**, or there is nothing to balance on; crown height inside its declared band; **nothing else placed within 0.75 m of a flank**. |
| **Never** | Never a platform, never a wall, never something to place another piece against. A cylinder too thin to stand on is a Dome in cylinder form and the balance verb it advertises is a lie. |

### WEDGE — *run up it*

| | |
|---|---|
| **Form** | A planar slab tipped along the line of travel, its low edge flush with the ground — no step onto it at all. |
| **Meaning** | The **only** planar slope a body may stand on. A Wedge converts horizontal speed into height. |
| **Collision** | `BoxShape3D`, rotated with the mesh, in the same node. |
| **Validator** | Slope **≤ 40°** (`floor_max_angle − 5`). Its reconstructed uphill direction must match the direction it is authored to climb. Thinner than the body's diameter, so its own end faces cannot become a ledge. |
| **Never** | Never steeper than 40°. The cap is arithmetic, not taste: a tipped slab's end faces sit at `90 − θ`, so at 40° they are exactly 50° — the shallowest legal wall. One degree steeper and the ramp's own ends fall into the forbidden band. |

### SAG — *it catches you and redirects you*

| | |
|---|---|
| **Form** | A concave trough, built as **chords of a parabola**: consecutive facets share their surface endpoint exactly, so "no step" is true by construction rather than to a tolerance. Sample spacing is seeded and non-uniform — a real sag is not a mesh. |
| **Meaning** | Speed is collected rather than stopped. You enter a Sag and it decides where you come out. |
| **Collision** | One `BoxShape3D` per facet, each with its mesh. |
| **Validator** | Every facet **≤ 40°** — every part of a Sag is standable, and that is what catching means. **Zero step** between consecutive facets (bar: 1 mm; the shipped course measures 0.017 mm). Slope strictly increasing along the trough — concave **everywhere**. |
| **Never** | Never convex, never a staircase, never a hole. A convex surface at a Sag's size is not a Dome, it is a hill. |

### DOME — *it sheds you*

| | |
|---|---|
| **Form** | A convex cap, and **small**. |
| **Meaning** | Refusal, expressed by shape rather than by a barrier. There is nowhere on a Dome to put a foot. |
| **Collision** | `SphereShape3D`. The meaning is the convexity. |
| **Validator** | Its walkable pole cap, `R·sin(45°)`, must be **strictly narrower than the body's collision radius**, so no body's footprint can get onto it. It declares no standable top. |
| **Never** | Never a platform, never a stepping stone, never big. **A convex cap larger than `bodyRadius / sin(45°)` is not a Dome — it is a HILL, and hills are the terrain layer under the kit, not a kit piece.** This is the design law the size rule exists to state, and it is why there are only ever two Domes' worth of band in the course: the ceiling sits inside the Hop band. |

### TILT — *a wall that used to be a floor*

| | |
|---|---|
| **Form** | A thin plate leaning past the floor limit, away from the approach. |
| **Meaning** | "Not this way." A Tilt reads as a wall from every angle a player meets it from. |
| **Collision** | `BoxShape3D`, rotated with the mesh. |
| **Validator** | Slope **≥ 50°** (`floor_max_angle + 5`) — a floor pretending to be a wall is the failure. Thickness **< the body's diameter**, so its end face cannot become a ledge. It declares **no** standable top. Its top edge is never within a **Hop** of anything standable within a Technique gap of it, or it reads as a step-up. |
| **Never** | Never a ledge, never a ramp, never a surface a player is meant to reach the top of by standing on it. |

---

## 4. Denial by distance — a kit rule, not a primitive

Anything tagged **Denial** is never within a **Technique** band, vertically, of anything
standable that is also inside a **Denial gap** of it in plan. A piece that is high but close
*teases*: it reads as a hard jump rather than as a closed door, and the player spends the
session finding out it was never open. Denial is separated in both axes, and the separation is
visible.

The validator's message for this is literally `It teases.`

---

## 5. Regenerate, and hand-edit — the contract

Talon, 2026-09-02, ruling on generated-but-authored geometry:

> "I like that the script writes the scene and the file can be opened and modified editor and
> hand-placed. Please begin this kind of work"

So, plainly:

- **`tools/dev/kit_course.py` is the source of truth.** Regenerate with
  `python tools/dev/kit_course.py`.
- **Open `scenes/dev/KitCourse.tscn` in the editor and move anything you like.** That is the
  point of a generated-but-authored scene, and the suite does **not** demand the shipped file
  be byte-identical to a fresh run — it asserts the *kit rules* against whatever ships. A hand
  edit that still obeys the kit stays green.
- **The next generator run overwrites the file completely and your edit is gone.** There is no
  merge, no preservation, no marked region that survives.
- **An experiment that proves itself gets promoted back into the generator's config block** and
  re-generated. Nothing else survives.
- Byte-reproducibility is a property of the *generator*: two runs produce identical bytes, and
  the suite proves it by generating twice to throwaway paths and comparing hashes.

The scene carries a header comment saying all of this, so a person who opens it in the editor
without reading this file still finds out.

---

## 6. Running it

```
# regenerate
python tools/dev/kit_course.py

# the numbers only, writes nothing
python tools/dev/kit_course.py --print

# the positive controls on their own
python tools/dev/kit_course.py --controls

# PLAY IT (headed, keeps the root script so the shipped body spawns)
Godot_v4.7-stable_mono_win64_console.exe --path . res://scenes/dev/KitCourse.tscn

# captures (headed, never --headless)
Godot_v4.7-stable_mono_win64_console.exe --path . --script res://tools/dev/kit_course_capture.gd -- --kit-capture docs/qa/LD-6

# the suite
powershell -ExecutionPolicy Bypass -File tests\Run-KitCourseTest.ps1
```

Every Godot flag before the `--`; everything after it is a user argument. A `--script` that
lands after the `--` is an inert string and the engine boots the project's main scene instead,
which looks exactly like a hang.

The course runs **west to east along +X** from a spawn pad at `x = 2`, in the order Block, Log,
Wedge, Sag, Dome, Tilt. A catch floor 6 m down means no plateau edge can lose the scene, and a
40° return ramp — itself a legal Wedge, because a return route that broke the kit's own grammar
would be the first thing a player learned — climbs back onto the plateau.

**Labels are `Label3D`, `billboard = 0`, facing −X into an approaching player.** They are never
billboarded (Talon's standing ruling: no billboarded world-space UI), and the self-test asserts
it. They exist so the **"call it" test** can be run: a player names what each shape does before
touching it.

**Every surface wears a 1 m world-space checker.** A flat greybox is a bad speedometer, and the
checker is also the course's only scale bar — any distance in any capture can be counted off it.

---

## 7. The sign, and why the validator is built the way it is

This section is here because the lesson cost a scene and because the next generator in this
repo will be tempted to make the same mistake.

The first `KitCourse.tscn` shipped with **all 22 of its rotated pieces mirrored** — three
Wedges descending, both Sags domed, three Tilts leaning the wrong way, the return ramp climbing
away from the plateau. The generator's own validator passed it. It passed it because:

1. **Angle is sign-blind.** `acos(normal.y)` reports 40° for a slab tipped either way. Every
   angle rule in the kit agreed with both.
2. **Every height claim was declared, not derived.** `w.tops = [(rise, (top_x, 0), …)]` was
   written by the same four lines of arithmetic that positioned the piece, and then checked
   against the bands. The validator was reading the author's intention back to itself. Flip the
   sign and the declaration does not move, so nothing anywhere in the run changes.

**A checker that reconstructs its subject the way its subject was built always agrees with it.**
So the kit now has three layers, and each one is independent of the one below:

1. `validate_derived()` in the generator throws the declarations away and rebuilds every piece
   from `pos`, `rot` and `shape` alone — the three fields actually written into the `.tscn` —
   then requires every declaration to be corroborated. Each ramp-like piece declares the
   direction it is **authored** to climb, and the reconstruction measures the direction it
   **actually** climbs. A mirrored ramp keeps its summit *height* and moves its summit
   *location*, so it is the plan check and the climb direction that catch it, never the angle.
2. `tools/dev/kit_selftest.gd` re-derives the same quantities from the **loaded scene**, using
   Godot's own `Transform3D * Vector3` and never a hand-indexed basis, and requires agreement
   to a millimetre. This is the only check that can catch python's Euler convention disagreeing
   with Godot's — the one thing python cannot check about itself. (The shipped course agrees to
   0.01 mm on 22 tops and to a dot product of +1.0000 on 23 authored climbs.)
3. `tests/Run-KitCourseTest.ps1` feeds **both** of them a scene with one Wedge deliberately
   mirrored and fails if either accepts it. A detector that has never fired positive is not
   known to work.

**Neither of the first two layers rests on one rule.** `run` is itself a declaration, so with
every `run` claim stripped off every piece the mirrored Wedge is *still* rejected — by
`top-corroboration`, which reads no statement of intent at all and simply asks whether the
declared standable top is where the transform actually puts a surface. Measured:

```
  with run-sign active:  REJECTED -> FAIL [run-sign]
  with run-sign REMOVED: REJECTED -> FAIL [top-corroboration]
```

The generator carries **twelve** named violations — `--violations` lists them, `--violate NAME`
proves that rule fires. They cover the whole sign family (Wedge, Sag, Tilt, return ramp) and one
rule from each primitive. They do **not** cover every rule the validator holds: the shape,
axis, band and ledge rules have no violation of their own yet, and that gap is named in the
LD-6 report rather than papered over. Add a rule, add its violation.

---

## 8. Out of scope here

- The slope term / friction knob in the motor — another owner.
- Placing kit pieces in the bubbletest — later packets, after LD-3's findings.
- Art: materials, wear, the warped-city look. The kit ships greybox with the checker.
