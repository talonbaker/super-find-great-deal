#!/usr/bin/env python3
"""Generate `scenes/dev/KitCourse.tscn` — the six-primitive affordance kit, LD-6, 2026-09-02.

    Talon, 2026-09-02, ruling on generated-but-authored geometry:
        "I like that the script writes the scene and the file can be opened and modified
         editor and hand-placed. Please begin this kind of work"

WHAT THIS IS. Research §A5's kit — Block, Log, Wedge, Sag, Dome, Tilt — one movement meaning
each, carried by FORM and never by colour, laid out in a line, one of each primitive at each
band it can reach. The doc is `docs/levels/KIT.md`; this file is the writer of the scene, and
the scene is the thing you play.

THE MODEL THIS COPIES is `tools/dev/tangle_tower.py`: seeded, byte-reproducible, and it proves
its geometric properties BEFORE it writes a byte, exiting non-zero and naming the violation.
Every rule in the kit table's validator column is asserted here, and every one of them is
asserted again against the shipped scene by `tools/dev/kit_selftest.gd` — a level that violates
the kit is a red test rather than a playtest discovery.

THE ONE INVARIANT THAT MAKES IT MIRROR'S-EDGE-PROOF, and it is stronger than the research
table asked for:

    THE FORBIDDEN BAND IS EMPTY. Every upward-facing planar facet in this scene that is wide
    enough to hold the body is either <= floor_max_angle - 5 (a floor, and it looks like one)
    or >= floor_max_angle + 5 (a wall, and it looks like one). Nothing in the scene sits in
    40..50 degrees. There is no surface a player can mistake for the other kind, because the
    kind that would be mistakable does not exist.

    A facet narrower than the body's own collision diameter is exempt, because a body cannot
    stand on it whatever its angle. That exemption is not a loophole: it is asserted per facet
    against a measured number (AvatarProportions.Fallback.CapsuleRadiusM), and it is what lets
    a Tilt be a thin plate rather than a wedge of solid rock.

The two curved primitives are governed by SIZE rules instead, for the honest reason that a
convex cap always exposes its own flat pole and no angle rule can take that away:

    DOME     the walkable cap at the pole has radius R * sin(floor_max_angle). Require that to
             be strictly narrower than the body's collision radius, so no body can get its
             footprint onto it. The consequence is a real design law, stated in KIT.md: a
             convex cap larger than bodyRadius / sin(floor_max_angle) is not a Dome, it is a
             HILL — and hills are the terrain layer under the kit, not a kit piece.
    LOG      the mirror of it. The crown's walkable strip is 2R * sin(floor_max_angle) wide;
             require that to be at least the body's collision diameter, or the "log" is a
             cylindrical Dome that sheds and the balance verb is a lie.

USAGE

    python tools/dev/kit_course.py                 # validate, then write the scene
    python tools/dev/kit_course.py --out PATH      # validate, then write it somewhere else
    python tools/dev/kit_course.py --print         # numbers only, writes nothing
    python tools/dev/kit_course.py --controls      # the motor + sign positive controls only
    python tools/dev/kit_course.py --violate NAME  # POSITIVE CONTROL: break one rule on purpose
                                                   # and prove the validator exits non-zero
    python tools/dev/kit_course.py --emit-violation NAME --out PATH
                                                   # write a DELIBERATELY BAD scene, validator
                                                   # skipped, for the self-test's own positive
                                                   # control. Refuses to write to the canonical
                                                   # path.

    python tools/dev/kit_course.py --violations    # list the violation names

HOW TO HAND-EDIT. Open `scenes/dev/KitCourse.tscn` in the editor and move anything you like;
that is the whole point of a generated-but-authored scene. But THIS FILE IS THE SOURCE OF
TRUTH: the next run overwrites the scene completely and your edit is gone. An experiment that
proves itself gets promoted back into the config block below and re-generated. Nothing else
survives.
"""

from __future__ import annotations

import argparse
import math
import random
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / "scenes/dev/KitCourse.tscn"
CS_MOTOR = ROOT / "scripts/net/MotorTuning.cs"
CS_PROPORTIONS = ROOT / "scripts/game/sandbox/AvatarProportions.cs"

# =====================================================================================
# THE MOTOR ENVELOPE — parsed and re-simulated, never typed
# =====================================================================================
#
# This is a third authorship of one small function, and the reason is the reason MOVE-8 gave:
# four measured numbers were TYPED into eight places, Talon's 2026-08-28 tuning ruling moved
# the motor, and every copy became wrong at once in silence under levels sized against them.
# `MotorArc.cs` re-derives them for C#; `tools/dev/motor_arc.gd` re-derives them for GDScript;
# this block re-derives them for the generator. All three parse the same PRIMITIVE rows out of
# `MotorTuning.Default` — the literals a person actually types — and re-run the same tick
# simulation over them.
#
# THE POSITIVE CONTROL that keeps this authorship honest is `verify_motor()`: it re-derives the
# PRE-RULING arc from a hand-copied old tuning and requires MOVE-3e's four in-engine
# measurements (1.534 / 6.192 / 0.467 / 1.710) back to the millimetre. `main()` runs it before
# anything else and refuses to write a byte if it fails. A derivation that cannot reproduce the
# measurement it replaced is a guess with a formula in front of it.

DT = 1.0 / 60.0          # AvatarMotor.TickDelta is a const and is not a knob
MOTOR_ROWS = ("MoveSpeed", "SprintMultiplier", "Gravity", "FallGravityMultiplier",
              "ApexHangStrength", "ApexHangWindowMps", "JumpVelocity",
              "JumpReleaseGravityMultiplier", "AirJumpMode", "AirJumpCountMax",
              "AirJumpVelocityFraction")


def _cs_rows() -> dict:
    """`<Name> = <number>f,` out of MotorTuning.Default's initializer ONLY.

    Bounded to that block on purpose, exactly as motor_arc.gd bounds it: `MoveSpeed` also
    appears in derived properties and in doc comments, and a whole-file regex would happily
    match one of those instead.
    """
    text = CS_MOTOR.read_text(encoding="utf-8")
    start = text.find("public static MotorTuning Default { get; } = new()")
    if start < 0:
        sys.exit(f"FAIL [motor]: {CS_MOTOR} no longer declares MotorTuning.Default's initializer")
    end = text.find("\n    };", start)
    if end < 0:
        sys.exit("FAIL [motor]: cannot find the end of MotorTuning.Default's initializer")
    block = text[start:end]
    out = {}
    for name in MOTOR_ROWS:
        m = re.search(r"\b" + name + r"\s*=\s*(-?[0-9.]+)f\s*,", block)
        if m is None:
            sys.exit(f"FAIL [motor]: MotorTuning.Default does not set `{name}`")
        out[name] = float(m.group(1))
    return out


def _cs_const(path: Path, name: str) -> float:
    m = re.search(r"public const float " + name + r"\s*=\s*(-?[0-9.]+)f\s*;",
                  path.read_text(encoding="utf-8"))
    if m is None:
        sys.exit(f"FAIL [motor]: {path.name} does not declare `public const float {name}`")
    return float(m.group(1))


def _gravity_for(t: dict, vy: float, held: bool) -> float:
    """AvatarMotor.GravityFor's three-way cut plus MOVE-4c's apex hang."""
    if vy < 0.0:
        g = t["Gravity"] * t["FallGravityMultiplier"]
    elif vy > 0.0 and not held:
        g = t["Gravity"] * t["JumpReleaseGravityMultiplier"]
    else:
        g = t["Gravity"]
    s, window = t["ApexHangStrength"], t["ApexHangWindowMps"]
    if s > 0.0 and window > 0.0:
        u = min(1.0, abs(vy) / window)
        w = 1.0 - (u * u * (3.0 - 2.0 * u))
        g *= 1.0 - s * w
    return g


def _simulate(t: dict, hold_ticks: int, air_jump: bool = False):
    vy = t["JumpVelocity"]
    y = vy * DT
    apex = y
    ticks = 1
    spent = False
    while y > 0.0 and ticks < 1200:
        held = ticks <= hold_ticks
        if air_jump and not spent and vy <= 0.0:
            # The air jump gets its own FREE LAUNCH TICK, exactly as the ground jump does:
            # AvatarMotor.Step assigns velocity.Y and hands it straight to MoveAndSlide.
            vy = t["JumpVelocity"] * t["AirJumpVelocityFraction"]
            spent = True
            y += vy * DT
            apex = max(apex, y)
            ticks += 1
            continue
        vy -= _gravity_for(t, vy, held) * DT
        y += vy * DT
        apex = max(apex, y)
        ticks += 1
    return apex, ticks * DT


def _as_played(t: dict, hold_ticks: int, air_jump: bool = False):
    """MOVE-3d's 'as played' convention: the launch tick's free JumpVelocity*dt out of the
    apex, one tick out of the airtime — the convention every level constant is stated in."""
    apex, airtime = _simulate(t, hold_ticks, air_jump)
    return apex - t["JumpVelocity"] * DT, airtime - DT


def arc_for(t: dict) -> dict:
    sprint = t["MoveSpeed"] * t["SprintMultiplier"]
    held = _as_played(t, 1 << 30)
    tap = _as_played(t, 0)                # released on the tick after the press; see MotorArc
    has_double = int(round(t["AirJumpMode"])) == 1 and int(round(t["AirJumpCountMax"])) >= 1
    dbl = _as_played(t, 1 << 30, True) if has_double else held
    return {
        "SprintApex": held[0], "SprintAirtime": held[1], "SprintRange": sprint * held[1],
        "TapApex": tap[0], "TapAirtime": tap[1], "TapRange": t["MoveSpeed"] * tap[1],
        "DoubleApex": dbl[0], "DoubleAirtime": dbl[1], "DoubleRange": sprint * dbl[1],
        "JogSpeed": t["MoveSpeed"], "SprintSpeed": sprint,
        "JogRange": t["MoveSpeed"] * held[1],
    }


def verify_motor() -> str:
    """THE POSITIVE CONTROL. Empty string on success, a human-readable failure otherwise."""
    pre = {
        "MoveSpeed": 5.4, "SprintMultiplier": 1.6, "Gravity": 22.0,
        "FallGravityMultiplier": 1.35, "ApexHangStrength": 0.0, "ApexHangWindowMps": 2.0,
        "JumpVelocity": 8.4, "JumpReleaseGravityMultiplier": 3.0,
        "AirJumpMode": 0.0, "AirJumpCountMax": 1.0, "AirJumpVelocityFraction": 0.8,
    }
    a = arc_for(pre)
    want = {"SprintApex": 1.534, "SprintRange": 6.192, "TapApex": 0.467, "TapRange": 1.710}
    for k, v in want.items():
        if abs(a[k] - v) > 0.001:
            return (f"the python derivation does not reproduce MOVE-3e's {k}: "
                    f"got {a[k]:.4f}, measured {v:.4f}")
    return ""


TUNING = _cs_rows()
ARC = arc_for(TUNING)

# =====================================================================================
# CONFIG — everything an author turns is here, and nothing below this block is a literal.
# =====================================================================================

SEED = 20260902

# --- The motor envelope. NOT ONE OF THESE IS AN AUTHORED NUMBER ----------------------
#
# Every band below is a FRACTION of these, never a metre value, so the whole course follows
# the motor through the next tuning change instead of silently outliving it. The metric card
# `docs/levels/METRICS-CARD.md` (LD-1) is generated from the same MotorTuning.Default rows;
# if it ever disagrees with this run's printed envelope, one of the two derivations is broken
# and neither number should be used until that is settled.
HELD_APEX_M = ARC["SprintApex"]
HELD_RANGE_M = ARC["SprintRange"]
TAP_APEX_M = ARC["TapApex"]
TAP_RANGE_M = ARC["TapRange"]
DJ_APEX_M = ARC["DoubleApex"]
DJ_RANGE_M = ARC["DoubleRange"]
JOG_HELD_RANGE_M = ARC["JogRange"]   # MoveSpeed x held airtime — research §A1's own arithmetic

# --- The band fractions (research §A1) -----------------------------------------------
F_HOP_UP = 0.68          # of the held apex
F_COMMIT_UP = 0.92       # of the held apex
F_TECHNIQUE_UP = 0.93    # of the double-jump apex
F_DENIAL_UP = 1.08       # of the double-jump apex
F_STONE_GAP = 0.95       # of the tap range
F_JOG_GAP = 0.92         # of the held-jog range
F_SPRINT_GAP = 0.92      # of the held-sprint range
F_TECHNIQUE_GAP = 0.93   # of the double-jump range
F_DENIAL_GAP = 1.08      # of the double-jump range

# --- The body, and Godot's floor rule -------------------------------------------------
#
# BODY_RADIUS_M is AvatarProportions.Fallback.CapsuleRadiusM = FallbackHalfWidthM
# x CapsuleRadiusFraction, PARSED out of AvatarProportions.cs for the same reason the motor
# rows are parsed: it is the same constant the bubbletest geometry is sized against, and a
# retyped copy of it is a copy that goes wrong in silence. It is a FALLBACK because a live
# avatar measures its own model; §A1 records that the radius "was not read out", and this
# is the read-out.
BODY_RADIUS_M = (_cs_const(CS_PROPORTIONS, "FallbackHalfWidthM")
                 * _cs_const(CS_PROPORTIONS, "CapsuleRadiusFraction"))
BODY_DIAMETER_M = BODY_RADIUS_M * 2.0
FLOOR_MAX_ANGLE_DEG = 45.0     # Godot's default; past it a body is not on the floor at all
ANGLE_MARGIN_DEG = 5.0         # the forbidden band is (40, 50); nothing in the scene is in it
FLOOR_ANGLE_DEG = FLOOR_MAX_ANGLE_DEG - ANGLE_MARGIN_DEG   # 40 — the steepest legal floor
WALL_ANGLE_DEG = FLOOR_MAX_ANGLE_DEG + ANGLE_MARGIN_DEG    # 50 — the shallowest legal wall
# A face authored AT a boundary (a 40 deg ramp, a 50 deg wall face) must not be rejected by
# float noise; a face a hundredth of a degree inside the band still is. The margin is 5 deg
# wide, so a 1e-6 tolerance costs nothing real and buys an exact boundary.
ANGLE_EPS_DEG = 1e-6

# --- The line ---------------------------------------------------------------------------
BAND_PLACE_T = 0.60      # where inside its band a piece sits: 0 = floor of the band, 1 = ceiling
DENIAL_PLACE_F = 1.12    # the Denial pieces sit at this multiple of the Denial floor

GROUND_X0, GROUND_X1 = -12.0, 214.0
GROUND_HALF_Z = 14.0
GROUND_THICK = 1.0
CATCH_Y = -6.0           # the catch floor: you cannot lose the scene off a plateau edge
CATCH_MARGIN = 8.0       # how far it reaches past the plateau in every direction
CATCH_THICK = 1.0

SPAWN_X = 2.0
SPAWN_PAD = 5.0
SPAWN_LIP = 0.02

BLOCK_X0 = 14.0          # the six Blocks, in band order
BLOCK_PITCH = 6.0
BLOCK_DENIAL_PITCH = 11.0
BLOCK_PLAN = 3.0

LOG_X0 = 66.0
LOG_PITCH = 11.0
LOG_LEN = 9.0
LOG_RADIUS_F = 0.55              # of its crown height, so a taller log is a fatter log
LOG_RADIUS_MARGIN_F = 1.08       # over the derived crown floor, the same 8% the Denial band uses

WEDGE_X0 = 100.0
WEDGE_PITCH = 12.0
WEDGE_WIDTH = 5.0
WEDGE_THICK = 0.55       # < BODY_DIAMETER on purpose: the ramp's own end faces cannot be stood on
WEDGE_ANGLES_DEG = (12.0, 26.0, 40.0)          # 40 is the cap and it is not a taste pick — see below
WEDGE_BANDS = ("Commit", "Technique", "Denial")

SAG_X0 = 140.0
SAG_PITCH = 16.0
SAG_WIDTH = 6.0
SAG_FACETS = 8
SAG_JITTER = 0.18        # seeded, on the sample spacing: a real sag is not a uniform mesh
SAG_SPECS = (("Shallow", "Hop", 9.0), ("Deep", "Commit", 7.5))   # name, rim band, span
SAG_UNDERCUT = 0.6       # how far the facet slabs are driven below the ground plane

DOME_X0 = 172.0
DOME_PITCH = 5.0
# A Dome's radius is NOT taken from its band — it is taken from the ceiling the body imposes,
# and the band is then whatever that gives. There are only two, and there will never be a third:
# the ceiling sits inside the Hop band, so a Dome can reach Kerb and Hop and nothing above them.
DOME_SPECS = (("Kerb", 0.55), ("Hop", 0.92))   # name, fraction of DOME_RADIUS_CEIL_M

TILT_X0 = 186.0
TILT_PITCH = 8.0
TILT_ANGLES_DEG = (52.0, 64.0, 76.0)
TILT_LEN = 4.4
TILT_WIDTH = 4.0
TILT_THICK = 0.55        # < BODY_DIAMETER: a Tilt is a plate, and its ends cannot be a ledge

RETURN_RAMP_ANGLE_DEG = 40.0     # the way back up from the catch floor — itself a legal Wedge
RETURN_RAMP_X = 12.0
RETURN_RAMP_WIDTH = 6.0
RETURN_RAMP_THICK = 0.6

LOG_FLANK_CLEAR_M = 0.75         # nothing else comes within this of a Log's flank

LABEL_HEIGHT_M = 2.2             # labels stand upright facing -X, never billboarded
LABEL_FONT_SIZE = 96
LABEL_PIXEL_SIZE = 0.006
LABEL_Z = -7.0

# --- Colour. NEUTRAL, and every primitive wears the SAME material -----------------------
#
# This is the thesis, not a palette choice: if two primitives differ in colour, a player can
# learn the colour instead of the form, and the kit has proved nothing. Only the spawn pad is
# saturated (bubbletest program §4 rule 7: "only the goal element saturated"). The ground is
# a separate value so geometry reads against it, per the DARK-1b value ladder.
COL_NEUTRAL = (0.560, 0.570, 0.590)
COL_GROUND = (0.330, 0.345, 0.330)
COL_CATCH = (0.200, 0.205, 0.220)
COL_GOAL = (0.760, 0.400, 0.120)
CHECKER_CELL_M = 1.0
CHECKER_STRENGTH = 0.16          # louder than the level's 0.09: this is a ruler, not a mood

# =====================================================================================
# Derived — the bands. Nothing below is typed; everything is a fraction of the envelope.
# =====================================================================================

HOP_UP = F_HOP_UP * HELD_APEX_M
COMMIT_UP = F_COMMIT_UP * HELD_APEX_M
TECHNIQUE_UP = F_TECHNIQUE_UP * DJ_APEX_M
DENIAL_UP = F_DENIAL_UP * DJ_APEX_M

STONE_GAP = F_STONE_GAP * TAP_RANGE_M
JOG_GAP = F_JOG_GAP * JOG_HELD_RANGE_M
SPRINT_GAP = F_SPRINT_GAP * HELD_RANGE_M
TECHNIQUE_GAP = F_TECHNIQUE_GAP * DJ_RANGE_M
DENIAL_GAP = F_DENIAL_GAP * DJ_RANGE_M

# The two curved-primitive size limits, both derived from the body and Godot's floor rule and
# neither one authored. DOME_RADIUS_CEIL_M is where a convex cap stops being a Dome and becomes
# a hill; LOG_RADIUS_FLOOR_M is where a cylinder stops being a Log and becomes a Dome. They are
# the same number, which is not a coincidence: it is the radius at which the walkable patch on a
# curved surface is exactly as wide as the body.
DOME_RADIUS_CEIL_M = BODY_RADIUS_M / math.sin(math.radians(FLOOR_MAX_ANGLE_DEG))
LOG_RADIUS_FLOOR_M = DOME_RADIUS_CEIL_M

# (name, low exclusive, high inclusive). None = open above.
BANDS_UP = (
    ("Kerb", 0.0, TAP_APEX_M),
    ("Hop", TAP_APEX_M, HOP_UP),
    ("Commit", HOP_UP, COMMIT_UP),
    ("Edge", COMMIT_UP, HELD_APEX_M),
    ("Technique", HELD_APEX_M, TECHNIQUE_UP),
    ("Denial", DENIAL_UP, None),
)

BANDS_GAP = (
    ("Stone", 0.0, STONE_GAP),
    ("Jog", STONE_GAP, JOG_GAP),
    ("Sprint", JOG_GAP, SPRINT_GAP),
    ("Edge", SPRINT_GAP, HELD_RANGE_M),
    ("Technique", HELD_RANGE_M, TECHNIQUE_GAP),
    ("Denial", DENIAL_GAP, None),
)


def band_bounds(name: str):
    for n, lo, hi in BANDS_UP:
        if n == name:
            return lo, hi
    raise KeyError(f"no vertical band named {name!r}")


def band_of_rise(rise: float):
    """The band a vertical rise lands in, or None if it lands in a gap between bands."""
    for n, lo, hi in BANDS_UP:
        if hi is None:
            if rise >= lo:
                return n
        elif lo < rise <= hi:
            return n
    return None


def height_for_band(name: str) -> float:
    lo, hi = band_bounds(name)
    if hi is None:
        return lo * DENIAL_PLACE_F
    return lo + BAND_PLACE_T * (hi - lo)


# =====================================================================================
# The pieces
# =====================================================================================

class Piece:
    """One authored thing: a mesh and a collider in the same node, always.

    `shape` is ("box", (sx, sy, sz)) | ("cyl", radius, height) | ("sphere", radius).
    `rot` is a single-axis rotation in radians; the kit never composes two, so there is no
    Euler-order ambiguity to get wrong and no hand-computed Transform3D to transpose.
    """

    __slots__ = ("kind", "name", "pos", "rot", "shape", "mat", "band", "label",
                 "neighbour", "tops", "group", "run")

    def __init__(self, kind, name, pos, shape, mat, rot=(0.0, 0.0, 0.0), band=None,
                 label=None, neighbour=None, group=None, run=None):
        self.kind = kind
        self.name = name
        self.pos = pos
        self.rot = rot
        self.shape = shape
        self.mat = mat
        self.band = band
        self.label = label
        self.neighbour = neighbour
        self.group = group
        # `run` is the horizontal direction this piece's top face is AUTHORED to climb, as a
        # unit (x, z). It is a CLAIM about intent, and validate_derived() reconstructs the real
        # uphill direction out of pos/rot/size and refuses to agree with it on the author's
        # word. None = the piece claims nothing (a level top, a curve).
        self.run = run
        # Standable tops this piece offers: (y, (cx, cz), (halfX, halfZ)). Empty = nothing here
        # a body can rest on, which is itself a claim the validator checks.
        self.tops: list = []


def rot_matrix(rx: float, ry: float, rz: float):
    """Row-major 3x3 for a single-axis rotation. The kit only ever uses one axis at a time."""
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)
    # R = Ry * Rx * Rz is Godot's Euler order, but with at most one non-zero angle the order
    # cannot matter; composing all three anyway keeps this honest if the config ever changes.
    rxm = ((1, 0, 0), (0, cx, -sx), (0, sx, cx))
    rym = ((cy, 0, sy), (0, 1, 0), (-sy, 0, cy))
    rzm = ((cz, -sz, 0), (sz, cz, 0), (0, 0, 1))

    def mul(a, b):
        return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3))
                     for i in range(3))

    return mul(mul(rym, rxm), rzm)


def apply(m, v):
    return tuple(sum(m[i][k] * v[k] for k in range(3)) for i in range(3))


def box_faces(piece: Piece):
    """Every planar face of a box: (world normal, (dimA, dimB)). Six of them, always."""
    _, (sx, sy, sz) = piece.shape
    m = rot_matrix(*piece.rot)
    return [
        (apply(m, (0.0, 1.0, 0.0)), (sx, sz)),
        (apply(m, (0.0, -1.0, 0.0)), (sx, sz)),
        (apply(m, (1.0, 0.0, 0.0)), (sy, sz)),
        (apply(m, (-1.0, 0.0, 0.0)), (sy, sz)),
        (apply(m, (0.0, 0.0, 1.0)), (sx, sy)),
        (apply(m, (0.0, 0.0, -1.0)), (sx, sy)),
    ]


def face_angle_deg(normal) -> float:
    """Degrees away from straight up. 0 = a floor, 90 = a wall, >90 = an underside."""
    return math.degrees(math.acos(max(-1.0, min(1.0, normal[1]))))


def plan_gap(a_c, a_h, b_c, b_h) -> float:
    """Edge-to-edge plan distance between two axis-aligned rectangles. 0 = they overlap."""
    dx = max(0.0, abs(a_c[0] - b_c[0]) - a_h[0] - b_h[0])
    dz = max(0.0, abs(a_c[1] - b_c[1]) - a_h[1] - b_h[1])
    return math.hypot(dx, dz)


def box(kind, name, centre, size, mat, rot=(0.0, 0.0, 0.0), band=None, label=None,
        neighbour=None, group=None, run=None) -> Piece:
    return Piece(kind, name, centre, ("box", size), mat, rot, band, label, neighbour, group,
                 run)


# =====================================================================================
# The course
# =====================================================================================

def build(violation: str | None = None) -> tuple[list[Piece], dict]:
    rng = random.Random(SEED)
    pieces: list[Piece] = []

    # --- The substrate -----------------------------------------------------------------
    gx = (GROUND_X0 + GROUND_X1) * 0.5
    gw = GROUND_X1 - GROUND_X0
    ground = box("ground", "Ground", (gx, -GROUND_THICK * 0.5, 0.0),
                 (gw, GROUND_THICK, GROUND_HALF_Z * 2.0), "ground")
    ground.tops = [(0.0, (gx, 0.0), (gw * 0.5, GROUND_HALF_Z))]
    pieces.append(ground)

    cw = gw + CATCH_MARGIN * 2.0
    cd = GROUND_HALF_Z * 2.0 + CATCH_MARGIN * 2.0
    catch = box("catch", "CatchFloor", (gx, CATCH_Y - CATCH_THICK * 0.5, 0.0),
                (cw, CATCH_THICK, cd), "catch")
    catch.tops = [(CATCH_Y, (gx, 0.0), (cw * 0.5, cd * 0.5))]
    pieces.append(catch)

    spawn = box("spawn", "SpawnPad", (SPAWN_X, SPAWN_LIP * 0.5, 0.0),
                (SPAWN_PAD, SPAWN_LIP, SPAWN_PAD), "goal")
    spawn.tops = [(SPAWN_LIP, (SPAWN_X, 0.0), (SPAWN_PAD * 0.5, SPAWN_PAD * 0.5))]
    pieces.append(spawn)

    # The way back up from the catch floor. It is a Wedge and it obeys the Wedge rules; a
    # return route that broke the kit's own grammar would be the first thing a player learned.
    ramp_theta = math.radians(RETURN_RAMP_ANGLE_DEG)
    ramp_rise = 0.0 - CATCH_Y
    ramp_len = ramp_rise / math.sin(ramp_theta)
    ramp_run = ramp_len * math.cos(ramp_theta)
    ramp_z_top = -GROUND_HALF_Z
    ramp_zc = ramp_z_top - ramp_run * 0.5 + RETURN_RAMP_THICK * 0.5 * math.sin(ramp_theta)
    ramp_yc = (0.0 + CATCH_Y) * 0.5 - RETURN_RAMP_THICK * 0.5 * math.cos(ramp_theta)
    ramp_rot_x = -ramp_theta
    if violation == "ramp-sign":
        ramp_rot_x = +ramp_theta               # the ramp mirrored: it now climbs AWAY from camp
    ramp = box("wedge", "ReturnRamp", (RETURN_RAMP_X, ramp_yc, ramp_zc),
               (RETURN_RAMP_WIDTH, RETURN_RAMP_THICK, ramp_len), "neutral",
               rot=(ramp_rot_x, 0.0, 0.0), run=(0.0, 1.0))
    ramp.tops = [(0.0, (RETURN_RAMP_X, ramp_z_top), (RETURN_RAMP_WIDTH * 0.5, 0.4))]
    pieces.append(ramp)

    # --- BLOCK: land, stand, climb. Tops sit only at band heights ----------------------
    x = BLOCK_X0
    for i, band in enumerate(("Kerb", "Hop", "Commit", "Edge", "Technique", "Denial")):
        top = height_for_band(band)
        if violation == "block-band" and band == "Technique":
            top = height_for_band("Hop")          # declared Technique, built at a Hop
        if violation == "denial-tease" and band == "Denial":
            x -= BLOCK_DENIAL_PITCH - BLOCK_PITCH  # slide it back inside a Technique hop
        b = box("block", f"Block_{band}", (x, top * 0.5, 0.0),
                (BLOCK_PLAN, top, BLOCK_PLAN), "neutral", band=band, label="BLOCK",
                neighbour="Ground", group="Blocks")
        b.tops = [(top, (x, 0.0), (BLOCK_PLAN * 0.5, BLOCK_PLAN * 0.5))]
        pieces.append(b)
        x += BLOCK_DENIAL_PITCH if band == "Technique" else BLOCK_PITCH

    # --- LOG: roll along / balance / ride the crown. The flanks shed -------------------
    x = LOG_X0
    for band in ("Hop", "Commit", "Technique"):
        crown = height_for_band(band)
        # A Log is as fat as its band allows, but never thinner than the crown floor: below
        # 2R sin(floor_max_angle) = the body's diameter there is nothing to balance ON, and the
        # primitive stops meaning what it says. The Hop band's own height would give a 0.38 m
        # radius, so the floor is what actually sizes the smallest log — which is the finding.
        radius = max(crown * LOG_RADIUS_F, LOG_RADIUS_FLOOR_M * LOG_RADIUS_MARGIN_F)
        if violation == "log-thin":
            radius = 0.24                          # a crown too narrow to balance on
        centre_y = crown - radius
        length = LOG_LEN + rng.uniform(-0.6, 0.6)
        lg = Piece("log", f"Log_{band}", (x, centre_y, 0.0), ("cyl", radius, length),
                   "neutral", rot=(0.0, 0.0, math.pi * 0.5), band=band, label="LOG",
                   group="Logs")
        # A cylinder's default axis is +Y; rotating a quarter turn about Z lays it along X.
        lg.tops = [(crown, (x, 0.0), (length * 0.5, radius * math.sin(math.radians(FLOOR_MAX_ANGLE_DEG))))]
        pieces.append(lg)
        x += LOG_PITCH

    # --- WEDGE: the only PLANAR slope a body can stand on ------------------------------
    #
    # The 40-degree cap is arithmetic, not taste. A rotated slab's own end faces sit at
    # (90 - theta); at theta = 40 that is exactly 50, the shallowest legal wall. One degree
    # steeper and the ramp's ends fall into the forbidden band. The steepest legal floor and
    # the steepest legal ramp are the same number for a reason.
    x = WEDGE_X0
    for angle_deg, band in zip(WEDGE_ANGLES_DEG, WEDGE_BANDS):
        deg = angle_deg
        if violation == "wedge-angle" and angle_deg == max(WEDGE_ANGLES_DEG):
            deg = 44.0                             # inside the forbidden band
        theta = math.radians(deg)
        rise = height_for_band(band)
        length = rise / math.sin(theta)
        # Put the low end's TOP CORNER exactly on the ground plane, so a body runs onto the
        # ramp with no step at all. Godot rotates +X to (cos, sin) about +Z, so a slab that
        # RISES along +X carries rotation +theta, not -theta; the sign is the whole difference
        # between a launch and a drop and no angle check can see it.
        xc = x + length * 0.5 * math.cos(theta) + WEDGE_THICK * 0.5 * math.sin(theta)
        yc = length * 0.5 * math.sin(theta) - WEDGE_THICK * 0.5 * math.cos(theta)
        rz = theta
        if violation == "wedge-sign" and angle_deg == max(WEDGE_ANGLES_DEG):
            rz = -theta            # THE ORIGINAL DEFECT: same angle, same height, opposite end
        w = box("wedge", f"Wedge_{int(round(deg))}deg", (xc, yc, 0.0),
                (length, WEDGE_THICK, WEDGE_WIDTH), "neutral", rot=(0.0, 0.0, rz),
                band=band, label="WEDGE", group="Wedges", run=(1.0, 0.0))
        top_x = x + length * math.cos(theta)
        w.tops = [(rise, (top_x, 0.0), (0.4, WEDGE_WIDTH * 0.5))]
        pieces.append(w)
        x += WEDGE_PITCH

    # --- SAG: concave, catches and redirects, no step anywhere in its surface ----------
    #
    # Built as CHORDS of a parabola rather than tangents: consecutive chords share their
    # endpoint exactly, so "no step" is true by construction rather than to a tolerance, and
    # every chord is shallower than the parabola's slope at that point, so the <= 40 rule has
    # margin built in. Sample spacing is seeded and non-uniform — a real sag is not a mesh.
    x = SAG_X0
    for tag, rim_band, span in SAG_SPECS:
        depth = height_for_band(rim_band)
        us = [-1.0]
        for i in range(1, SAG_FACETS):
            base = -1.0 + 2.0 * i / SAG_FACETS
            us.append(base + rng.uniform(-SAG_JITTER, SAG_JITTER) * (2.0 / SAG_FACETS))
        us.append(1.0)
        thick = (depth + SAG_UNDERCUT) / math.cos(math.radians(FLOOR_ANGLE_DEG)) + 0.4
        pts = [(x + (u + 1.0) * 0.5 * span, depth * u * u) for u in us]
        if violation == "sag-step":
            mid = len(pts) // 2
            pts[mid] = (pts[mid][0], pts[mid][1] + 0.22)   # a lip in the middle of the bowl
        for i in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            chord = math.hypot(x1 - x0, y1 - y0)
            theta = math.atan2(y1 - y0, x1 - x0)
            # centre = chord midpoint - (thick/2) * the rotated local UP, so the slab's TOP
            # FACE lies exactly on the chord and consecutive facets share their endpoint to
            # the bit. "No step" is then true by construction rather than to a tolerance.
            cx = (x0 + x1) * 0.5 + thick * 0.5 * math.sin(theta)
            cy = (y0 + y1) * 0.5 - thick * 0.5 * math.cos(theta)
            if violation == "sag-slip" and i == len(pts) // 2:
                cy += 0.14        # one facet lifted off the shared endpoint: a step in the bowl
            rz = theta
            if violation == "sag-sign":
                rz = -theta       # THE ORIGINAL DEFECT, as it shipped: the bowl becomes a dome
            # A facet's authored climb is the sign of its own chord: the upstream half falls
            # along +X, the downstream half rises along it. That is what makes it a bowl, and
            # it is the claim the reconstruction is about to test.
            run = None if abs(theta) < 1e-9 else ((1.0, 0.0) if theta > 0.0 else (-1.0, 0.0))
            f = box("sag", f"Sag_{tag}_F{i:02d}", (cx, cy, 0.0),
                    (chord, thick, SAG_WIDTH), "neutral", rot=(0.0, 0.0, rz),
                    band=rim_band, label=("SAG" if i == 0 else None), group=f"Sag{tag}",
                    run=run)
            pieces.append(f)
        # Three standable tops per Sag and no more: the low line you get collected into, and the
        # two rims you step over to get in. Everything between them is the same continuous
        # surface, so registering every facet would be counting one top eight times.
        facets = pieces[-(len(pts) - 1):]
        low = min(pts, key=lambda q: q[1])
        low_facet = min(facets, key=lambda f: abs(f.pos[0] - low[0]))
        low_facet.tops = [(low[1], (low[0], 0.0), (span * 0.15, SAG_WIDTH * 0.5))]
        facets[0].tops = [(depth, (pts[0][0], 0.0), (0.4, SAG_WIDTH * 0.5))]
        facets[-1].tops = [(depth, (pts[-1][0], 0.0), (0.4, SAG_WIDTH * 0.5))]
        x += span + SAG_PITCH

    # --- DOME: convex, sheds, and SMALL by arithmetic ----------------------------------
    x = DOME_X0
    for band, f in DOME_SPECS:
        radius = DOME_RADIUS_CEIL_M * f
        if violation == "dome-size":
            radius = 1.2                           # big enough to stand on: that is a hill
        d = Piece("dome", f"Dome_{band}", (x, 0.0, 0.0), ("sphere", radius), "neutral",
                  band=band, label="DOME", group="Domes")
        pieces.append(d)                            # no tops: nothing here holds a body
        x += DOME_PITCH

    # --- TILT: a wall that used to be a floor ------------------------------------------
    x = TILT_X0
    for angle_deg in TILT_ANGLES_DEG:
        theta = math.radians(angle_deg)
        length = TILT_LEN
        if violation == "tilt-hop" and angle_deg == min(TILT_ANGLES_DEG):
            length = 0.75 / math.sin(theta)        # a top edge one hop off the ground
        xc = x + length * 0.5 * math.cos(theta) + TILT_THICK * 0.5 * math.sin(theta)
        yc = length * 0.5 * math.sin(theta) - TILT_THICK * 0.5 * math.cos(theta)
        rz = theta
        if violation == "tilt-sign" and angle_deg == max(TILT_ANGLES_DEG):
            rz = -theta            # a Tilt leaning back over the approach instead of away
        t = box("tilt", f"Tilt_{int(round(angle_deg))}deg", (xc, yc, 0.0),
                (length, TILT_THICK, TILT_WIDTH), "neutral", rot=(0.0, 0.0, rz),
                label="TILT", group="Tilts", run=(1.0, 0.0))
        t.tops = []                                 # a Tilt offers nothing to stand on, ever
        pieces.append(t)
        x += TILT_PITCH

    facts = {
        "pieces": len(pieces),
        "bands_up": [(n, lo, hi) for n, lo, hi in BANDS_UP],
        "bands_gap": [(n, lo, hi) for n, lo, hi in BANDS_GAP],
        "body_radius": BODY_RADIUS_M,
        "dome_max_radius": DOME_RADIUS_CEIL_M,
        "log_min_radius": LOG_RADIUS_FLOOR_M,
    }
    return pieces, facts


# =====================================================================================
# DERIVED GEOMETRY — the independent reconstruction, and why this section exists
# =====================================================================================
#
# THE BUG THIS SECTION IS THE ANSWER TO. The first cut of this generator wrote every ramp with
# `rot.z = -theta`, which tips a slab DOWNHILL along +X, and shipped a KitCourse.tscn in which
# all three Wedges descended, both Sags were domes, and all three Tilts leaned the wrong way —
# 22 nodes, every rotated piece in the file. The validator passed it. It passed it because:
#
#   1. `face_angle_deg` is the angle from straight up, and it is SIGN-BLIND: a slab at +40 deg
#      and a slab at -40 deg both report 40. Every angle rule in the kit agreed with both.
#   2. Every height claim — `w.tops = [(rise, (top_x, 0.0), ...)]` — was DECLARED by the same
#      four lines of arithmetic that positioned the piece, and then checked against the bands.
#      The validator was reading the author's intention back to itself. Flip the sign and the
#      declaration does not move, so nothing anywhere in the run changes.
#
# A checker that reconstructs its subject the way its subject was built always agrees with it.
# So this section throws the declarations away and rebuilds the geometry from `pos`, `rot` and
# `shape` alone — the three fields that are actually WRITTEN INTO THE .tscn — and then requires
# every declaration to be corroborated by that reconstruction. A wrong sign now moves the
# reconstructed summit to the other end of the ramp, where the declaration is not, and the run
# dies naming the piece.
#
# The one thing python cannot check about itself is whether its Euler convention is Godot's.
# That is checked in the engine: the generator stamps its reconstructed tops into the scene as
# root metadata, and `tools/dev/kit_selftest.gd` re-derives them from the LOADED scene using
# Godot's own `Transform3D * AABB` and requires agreement to a millimetre. Two authorships, one
# of them the engine's.

def local_axes(piece: Piece):
    """The piece's three local axes in world space — the columns of its rotation."""
    m = rot_matrix(*piece.rot)
    return apply(m, (1.0, 0.0, 0.0)), apply(m, (0.0, 1.0, 0.0)), apply(m, (0.0, 0.0, 1.0))


def box_corners(piece: Piece):
    """All eight world corners of a box, from pos/rot/size and nothing else."""
    _, (sx, sy, sz) = piece.shape
    ex, ey, ez = local_axes(piece)
    out = []
    for a in (-0.5, 0.5):
        for b in (-0.5, 0.5):
            for c in (-0.5, 0.5):
                out.append(tuple(piece.pos[i] + a * sx * ex[i] + b * sy * ey[i]
                                 + c * sz * ez[i] for i in range(3)))
    return out


def derived_summit(piece: Piece):
    """The highest point of the piece's surface: (y, (x, z)). Reconstructed, never declared."""
    if piece.shape[0] == "box":
        top = max(box_corners(piece), key=lambda p: p[1])
        return top[1], (top[0], top[2])
    if piece.shape[0] == "cyl":
        # Only ever laid horizontally in this kit; log-axis asserts that separately.
        return piece.pos[1] + piece.shape[1], (piece.pos[0], piece.pos[2])
    return piece.pos[1] + piece.shape[1], (piece.pos[0], piece.pos[2])


def derived_top_face(piece: Piece):
    """The local +Y face of a box in world terms: (centre, normal, ex, half_x, ez, half_z)."""
    _, (sx, sy, sz) = piece.shape
    ex, ey, ez = local_axes(piece)
    centre = tuple(piece.pos[i] + 0.5 * sy * ey[i] for i in range(3))
    return centre, ey, ex, sx * 0.5, ez, sz * 0.5


def derived_height_at(piece: Piece, x: float, z: float):
    """World height of the box's top-face PLANE over (x, z), or None if that face is a wall.

    This is the reconstruction the declared tops are checked against. Nothing about the
    author's intended rise enters it.
    """
    centre, n, _, _, _, _ = derived_top_face(piece)
    if n[1] <= 1e-9:
        return None
    return centre[1] + (n[0] * (centre[0] - x) + n[2] * (centre[2] - z)) / n[1]


def derived_on_face(piece: Piece, x: float, z: float, tol: float = 1e-4) -> bool:
    """Is (x, z) inside the top face's own footprint? Reconstructed from the rotated axes."""
    y = derived_height_at(piece, x, z)
    if y is None:
        return False
    centre, _, ex, hx, ez, hz = derived_top_face(piece)
    d = (x - centre[0], y - centre[1], z - centre[2])
    a = sum(d[i] * ex[i] for i in range(3))
    b = sum(d[i] * ez[i] for i in range(3))
    return abs(a) <= hx + tol and abs(b) <= hz + tol


def derived_uphill(piece: Piece):
    """The horizontal direction the top face actually CLIMBS, as a unit (x, z), or None if it
    is level. This is the whole sign question in one function, and it reads only pos/rot."""
    _, n, _, _, _, _ = derived_top_face(piece)
    h = math.hypot(n[0], n[2])
    if h < 1e-9:
        return None
    # Uphill is the direction the surface rises: opposite the normal's horizontal lean.
    return (-n[0] / h, -n[2] / h)


def selfcheck_signs() -> list:
    """POSITIVE CONTROL 1 — the honest case must reconstruct honestly.

    Two slabs are authored by hand here with nothing derived from the course: one made to
    DESCEND along +X, one made to CLIMB along +X. The reconstruction must say so. If a checker
    cannot tell a ramp from a drop on a fixture where the answer is known by construction, its
    verdict on the course is worthless. Returns a list of (label, ok, detail) rows.
    """
    rows = []
    theta = math.radians(30.0)
    for label, rz, want in (("authored to DESCEND along +X", -theta, (-1.0, 0.0)),
                            ("authored to CLIMB   along +X", +theta, (+1.0, 0.0))):
        slab = box("wedge", "Fixture", (0.0, 1.0, 0.0), (4.0, 0.5, 2.0), "neutral",
                   rot=(0.0, 0.0, rz))
        up = derived_uphill(slab)
        summit = derived_summit(slab)
        ok = up is not None and abs(up[0] - want[0]) < 1e-6 and abs(up[1] - want[1]) < 1e-6
        # And the summit must be at the end the uphill direction points to, or "uphill" is
        # a word rather than a measurement.
        ok = ok and (summit[1][0] * want[0] > 0.0)
        rows.append((label, ok,
                     f"reconstructed uphill {up}, summit at x={summit[1][0]:+.3f} "
                     f"y={summit[0]:.3f}"))
    return rows


# =====================================================================================
# The validator — every rule in KIT.md's acceptance column, run before a byte is written
# =====================================================================================

def fail(rule: str, detail: str) -> None:
    sys.exit(f"FAIL [{rule}]: {detail}")


def validate_derived(pieces: list[Piece]) -> dict:
    """Reconstruct every piece from pos/rot/shape and refuse to take a declaration on trust.

    Runs BEFORE every other rule, because every other rule reads `piece.tops`, and until this
    function has passed there is no reason to believe `piece.tops` describes the geometry that
    is about to be written to disk.
    """
    report = {"tops_corroborated": 0, "runs_checked": 0, "worst_top_error_mm": 0.0}

    for p in pieces:
        # --- the authored climb direction, measured rather than believed -------------------
        if p.run is not None:
            up = derived_uphill(p)
            if up is None:
                fail("run-level", f"{p.name} claims to climb along {p.run} but its top face "
                                  "reconstructs level. A ramp that is flat is not a ramp.")
            dot = up[0] * p.run[0] + up[1] * p.run[1]
            if dot < 0.999:
                fail("run-sign",
                     f"{p.name} is authored to climb along {p.run} but the geometry actually "
                     f"written for it climbs along ({up[0]:+.4f}, {up[1]:+.4f}) — dot {dot:+.4f}. "
                     f"Its rotation is {p.rot[2]:+.6f} rad about Z / {p.rot[0]:+.6f} about X. "
                     "A slab tipped the wrong way has the same ANGLE as the right one and every "
                     "angle rule in this file will agree with it; only the reconstruction can "
                     "tell a launch from a drop.")
            report["runs_checked"] += 1

        # --- every declared top must be corroborated by the reconstruction ----------------
        for (ty, (cx, cz), _h) in p.tops:
            if p.shape[0] == "box":
                got = derived_height_at(p, cx, cz)
                if got is None:
                    fail("top-corroboration",
                         f"{p.name} declares a standable top at y={ty:.4f}, but its top face "
                         "reconstructs as a WALL — there is no surface there at all.")
                err = abs(got - ty)
                report["worst_top_error_mm"] = max(report["worst_top_error_mm"], err * 1000.0)
                if err > 1e-3:
                    fail("top-corroboration",
                         f"{p.name} declares a standable top at y={ty:.4f} over "
                         f"({cx:.3f}, {cz:.3f}), but the geometry actually written puts its "
                         f"surface at y={got:.4f} there — {err * 1000.0:.1f} mm out. The "
                         "declaration and the transform disagree; the transform is what ships.")
                if not derived_on_face(p, cx, cz):
                    fail("top-offpiece",
                         f"{p.name} declares a standable top over ({cx:.3f}, {cz:.3f}), which "
                         "is not inside the footprint of the face it reconstructs. The top is "
                         "declared somewhere the piece is not.")
            else:
                got, (sx_, sz_) = derived_summit(p)
                err = abs(got - ty)
                report["worst_top_error_mm"] = max(report["worst_top_error_mm"], err * 1000.0)
                if err > 1e-3:
                    fail("top-corroboration",
                         f"{p.name} declares its crown at y={ty:.4f}; the reconstruction of "
                         f"its actual shape puts it at y={got:.4f}.")
                if math.hypot(sx_ - cx, sz_ - cz) > 1e-3:
                    fail("top-corroboration",
                         f"{p.name} declares its crown over ({cx:.3f}, {cz:.3f}); the "
                         f"reconstruction puts it over ({sx_:.3f}, {sz_:.3f}).")
            report["tops_corroborated"] += 1

        # --- the summit: nothing may stand higher than the piece admits to ----------------
        #
        # A mirrored ramp keeps its summit HEIGHT and moves its summit LOCATION, so height
        # alone would not catch the sign. The plan check is the one that does.
        if p.tops and p.kind in ("block", "wedge", "ground", "catch", "spawn"):
            sy_, (sx_, sz_) = derived_summit(p)
            best = max(p.tops, key=lambda t: t[0])
            if sy_ - best[0] > 1e-3:
                fail("undeclared-high-ground",
                     f"{p.name}'s highest reconstructed point is y={sy_:.4f}, above the "
                     f"y={best[0]:.4f} it declares. There is standable ground on this piece "
                     "that no rule in the kit has been applied to.")
            if (abs(sx_ - best[1][0]) > best[2][0] + 1e-3
                    or abs(sz_ - best[1][1]) > best[2][1] + 1e-3):
                fail("summit-plan",
                     f"{p.name} declares its high point over ({best[1][0]:.3f}, "
                     f"{best[1][1]:.3f}) +-({best[2][0]:.3f}, {best[2][1]:.3f}), but the "
                     f"geometry's actual summit is over ({sx_:.3f}, {sz_:.3f}) — the far end "
                     "of the piece. This is what a flipped rotation looks like: the same "
                     "angle, the same height, the opposite end.")
    return report


def all_tops(pieces: list[Piece]):
    for p in pieces:
        for t in p.tops:
            yield p, t


def validate(pieces: list[Piece]) -> dict:
    # RULE 0, and it runs first on purpose: nothing below may read `piece.tops` until the
    # reconstruction has agreed that `piece.tops` describes the geometry being written.
    report = dict(validate_derived(pieces))

    # --- RULE G: the forbidden band is empty ------------------------------------------
    worst = (0.0, "")
    checked = 0
    for p in pieces:
        if p.shape[0] != "box":
            continue
        for normal, dims in box_faces(p):
            if normal[1] <= 1e-6:
                continue                            # not upward-facing: never a floor
            if min(dims) < BODY_DIAMETER_M:
                continue                            # narrower than the body: cannot hold it
            checked += 1
            a = face_angle_deg(normal)
            if FLOOR_ANGLE_DEG + ANGLE_EPS_DEG < a < WALL_ANGLE_DEG - ANGLE_EPS_DEG:
                fail("forbidden-band",
                     f"{p.name} presents a {min(dims):.3f} m-wide upward face at {a:.2f} deg, "
                     f"inside the forbidden band ({FLOOR_ANGLE_DEG:.0f}, {WALL_ANGLE_DEG:.0f}) "
                     "— a surface that looks walkable and is not, or the reverse.")
            margin = min(abs(a - FLOOR_ANGLE_DEG), abs(a - WALL_ANGLE_DEG))
            if worst[1] == "" or margin < worst[0]:
                worst = (margin, f"{p.name} at {a:.2f} deg")
    report["forbidden_band_faces_checked"] = checked
    report["forbidden_band_worst"] = worst

    # --- BLOCK: every top reachable within a band from its neighbour, or tagged Denial --
    for p in pieces:
        if p.kind != "block":
            continue
        nb = next((q for q in pieces if q.name == p.neighbour), None)
        if nb is None or not nb.tops:
            fail("block-neighbour", f"{p.name} names neighbour {p.neighbour!r}, which offers "
                                    "no standable top — a Block with no neighbour is a Denial "
                                    "that forgot to say so.")
        rise = p.tops[0][0] - nb.tops[0][0]
        actual = band_of_rise(rise)
        if actual != p.band:
            fail("block-band", f"{p.name} declares band {p.band!r} but rises {rise:.3f} m above "
                               f"{nb.name}, which is the {actual!r} band.")
        gap = plan_gap(p.tops[0][1], p.tops[0][2], nb.tops[0][1], nb.tops[0][2])
        if p.band != "Denial" and gap > TECHNIQUE_GAP:
            fail("block-reach", f"{p.name} is {gap:.3f} m from {nb.name} in plan, past the "
                                f"Technique gap {TECHNIQUE_GAP:.3f} m.")

    # --- DENIAL BY DISTANCE: never within a Technique band of anything standable -------
    denials = [p for p in pieces if p.band == "Denial" and p.tops]
    for d in denials:
        dy, dc, dh = d.tops[0]
        for other, (sy, sc, sh) in all_tops(pieces):
            if other is d:
                continue
            rise = dy - sy
            if rise <= 0 or rise > TECHNIQUE_UP:
                continue                            # unreachable upward, or you are above it
            gap = plan_gap(dc, dh, sc, sh)
            if gap < DENIAL_GAP:
                fail("denial-by-distance",
                     f"{d.name} sits {rise:.3f} m above {other.name} (inside the Technique band "
                     f"{TECHNIQUE_UP:.3f} m) and only {gap:.3f} m from it in plan, under the "
                     f"Denial gap {DENIAL_GAP:.3f} m. It teases.")
    report["denial_pieces"] = [d.name for d in denials]

    # --- WEDGE: angle <= floor limit - margin ------------------------------------------
    wedges = []
    for p in pieces:
        if p.kind != "wedge":
            continue
        a = face_angle_deg(box_faces(p)[0][0])
        if a > FLOOR_ANGLE_DEG + 1e-9:
            fail("wedge-angle", f"{p.name} slopes at {a:.3f} deg, past the steepest legal floor "
                                f"{FLOOR_ANGLE_DEG:.0f} deg.")
        # A ramp thick enough for its own end face to hold a body has grown a ledge nobody
        # authored — the same rule as tilt-plate, for the same reason, on the other primitive.
        _, (_wsx, wsy, wsz) = p.shape
        if min(wsy, wsz) >= BODY_DIAMETER_M:
            fail("wedge-plate", f"{p.name} is {min(wsy, wsz):.3f} m thick, at or past the body's "
                                f"{BODY_DIAMETER_M:.3f} m diameter — its own end face becomes a "
                                "ledge the kit never declared.")
        wedges.append((p.name, a))
    report["wedges"] = wedges

    # --- TILT: past the floor limit, and never within a Hop of a standable top ---------
    tilts = []
    for p in pieces:
        if p.kind != "tilt":
            continue
        a = face_angle_deg(box_faces(p)[0][0])
        if a < WALL_ANGLE_DEG:
            fail("tilt-angle", f"{p.name} slopes at {a:.3f} deg, under the shallowest legal wall "
                               f"{WALL_ANGLE_DEG:.0f} deg — it is a floor pretending to be a wall.")
        if p.tops:
            fail("tilt-ledge", f"{p.name} registers a standable top; a Tilt is never a ledge.")
        _, (sx, sy, sz) = p.shape
        if min(sy, sz) >= BODY_DIAMETER_M:
            fail("tilt-plate", f"{p.name} is {min(sy, sz):.3f} m thick, at or past the body's "
                               f"{BODY_DIAMETER_M:.3f} m diameter — its end face becomes a ledge.")
        theta = p.rot[2]
        top_edge_x = p.pos[0] + sx * 0.5 * math.cos(theta) - sy * 0.5 * math.sin(theta)
        top_edge_y = p.pos[1] + sx * 0.5 * math.sin(theta) + sy * 0.5 * math.cos(theta)
        for other, (oy, oc, oh) in all_tops(pieces):
            gap = plan_gap((top_edge_x, p.pos[2]), (0.3, sz * 0.5), oc, oh)
            if gap > TECHNIQUE_GAP:
                continue
            if abs(top_edge_y - oy) <= HOP_UP:
                fail("tilt-hop",
                     f"{p.name}'s top edge is {abs(top_edge_y - oy):.3f} m from {other.name}'s "
                     f"standable top, inside the Hop band {HOP_UP:.3f} m, and {gap:.3f} m away "
                     "in plan. It reads as a step-up.")
        tilts.append((p.name, a, top_edge_y))
    report["tilts"] = tilts

    # --- LOG: cylinder collision, balanceable crown, nothing on its flanks -------------
    logs = []
    for p in pieces:
        if p.kind != "log":
            continue
        if p.shape[0] != "cyl":
            fail("log-shape", f"{p.name} is not a cylinder; a Log's meaning IS its collision "
                              "shape and a box in its place is the Mirror's Edge failure.")
        _, radius, length = p.shape
        axis = apply(rot_matrix(*p.rot), (0.0, 1.0, 0.0))
        if abs(axis[1]) > 1e-6:
            fail("log-axis", f"{p.name}'s axis is not horizontal ({axis[1]:+.4f} of Y); a vertical "
                             "cylinder is a pillar, not a Log.")
        strip = 2.0 * radius * math.sin(math.radians(FLOOR_MAX_ANGLE_DEG))
        if strip < BODY_DIAMETER_M:
            fail("log-crown",
                 f"{p.name}'s crown offers a {strip:.3f} m walkable strip, under the body's "
                 f"{BODY_DIAMETER_M:.3f} m diameter. Nothing can balance on it — that is a Dome "
                 "in cylinder form, and the balance verb it advertises is a lie.")
        actual = band_of_rise(p.tops[0][0])
        if actual != p.band:
            fail("log-band", f"{p.name} declares {p.band!r} but its crown at {p.tops[0][0]:.3f} m "
                             f"is the {actual!r} band.")
        for q in pieces:
            if q is p or q.kind in ("ground", "catch", "spawn", "wedge") or q.kind == "log":
                continue
            qr = 0.0
            if q.shape[0] == "box":
                qr = math.hypot(q.shape[1][0], q.shape[1][2]) * 0.5
            elif q.shape[0] == "sphere":
                qr = q.shape[1]
            dz = abs(q.pos[2] - p.pos[2])
            dx = max(0.0, abs(q.pos[0] - p.pos[0]) - length * 0.5)
            if math.hypot(dx, dz) < radius + LOG_FLANK_CLEAR_M + qr:
                fail("log-flanks", f"{q.name} sits on {p.name}'s flank; nothing is ever placed "
                                   "there, because the round flanks shed.")
        logs.append((p.name, radius, p.tops[0][0], strip))
    report["logs"] = logs

    # --- SAG: concave, no step, nothing past the floor limit ---------------------------
    sags = {}
    for p in pieces:
        if p.kind == "sag":
            sags.setdefault(p.group, []).append(p)
    for group, facets in sags.items():
        facets.sort(key=lambda f: f.pos[0])
        prev_slope = None
        prev_end = None
        for f in facets:
            _, (sx, sy, sz) = f.shape
            theta = f.rot[2]
            a = face_angle_deg(box_faces(f)[0][0])
            if a > FLOOR_ANGLE_DEG + 1e-9:
                fail("sag-angle", f"{f.name} slopes at {a:.3f} deg, past the steepest legal floor. "
                                  "Every part of a Sag is standable; that is what catching means.")
            # The chord's two surface endpoints, in world space: the top-face centre is the
            # body centre plus half the thickness along the ROTATED local up, and the endpoints
            # are half a chord either side of it along the ROTATED local forward.
            hx, hy = sx * 0.5 * math.cos(theta), sx * 0.5 * math.sin(theta)
            ux, uy = -sy * 0.5 * math.sin(theta), sy * 0.5 * math.cos(theta)
            start = (f.pos[0] - hx + ux, f.pos[1] - hy + uy)
            end = (f.pos[0] + hx + ux, f.pos[1] + hy + uy)
            if prev_end is not None:
                step = math.hypot(prev_end[0] - start[0], prev_end[1] - start[1])
                if step > 1e-6 or abs(prev_end[0] - start[0]) > 1e-6:
                    fail("sag-step",
                         f"{group}: a {step * 1000:.2f} mm step at {f.name}'s upstream edge "
                         f"(and {abs(prev_end[0] - start[0]) * 1000:.2f} mm of plan slip). A Sag's "
                         "surface is continuous or it is a staircase pretending to be a bowl.")
            if prev_slope is not None and theta <= prev_slope + 1e-9:
                fail("sag-concave",
                     f"{group}: {f.name} turns the surface the wrong way (slope {math.degrees(theta):+.3f} "
                     f"deg after {math.degrees(prev_slope):+.3f} deg). A Sag is concave everywhere.")
            prev_slope = theta
            prev_end = end
        sags[group] = (len(facets), math.degrees(max(abs(f.rot[2]) for f in facets)))
    report["sags"] = sags

    # --- DOME: convex, and too small to stand on ---------------------------------------
    domes = []
    for p in pieces:
        if p.kind != "dome":
            continue
        if p.shape[0] != "sphere":
            fail("dome-shape", f"{p.name} is not a sphere; a Dome's meaning is its convexity.")
        radius = p.shape[1]
        cap = radius * math.sin(math.radians(FLOOR_MAX_ANGLE_DEG))
        if cap >= BODY_RADIUS_M:
            fail("dome-size",
                 f"{p.name} has radius {radius:.3f} m, so its walkable pole cap is {cap:.3f} m "
                 f"wide against a {BODY_RADIUS_M:.3f} m body radius — a body fits on it. That is "
                 "not a Dome, it is a hill, and hills are the terrain layer under the kit.")
        if p.tops:
            fail("dome-ledge", f"{p.name} registers a standable top; a Dome sheds, always.")
        actual = band_of_rise(p.pos[1] + radius)
        if actual != p.band:
            fail("dome-band", f"{p.name} declares {p.band!r} but its cap tops out at "
                              f"{p.pos[1] + radius:.3f} m, the {actual!r} band.")
        domes.append((p.name, radius, cap))
    report["domes"] = domes

    # --- Every mesh has a collider in the same node ------------------------------------
    # True by construction here (emit writes both children for every piece), and asserted so
    # a future edit to emit() cannot quietly drop one. The bubbletest shipped a playtest with
    # no colliders anywhere in it; this repo does not get to assume.
    for p in pieces:
        if p.shape[0] not in ("box", "cyl", "sphere"):
            fail("collider", f"{p.name} has shape kind {p.shape[0]!r}, which emit() cannot give "
                             "a collider.")

    report["kinds"] = {}
    for p in pieces:
        report["kinds"][p.kind] = report["kinds"].get(p.kind, 0) + 1
    return report


# =====================================================================================
# Emitting the scene
# =====================================================================================

MATERIALS = {
    "neutral": COL_NEUTRAL,
    "ground": COL_GROUND,
    "catch": COL_CATCH,
    "goal": COL_GOAL,
}

HEADER_NOTE = """; ==========================================================================================
; GENERATED BY tools/dev/kit_course.py — DO NOT HAND-EDIT AND EXPECT IT TO SURVIVE.
;
; This scene is generated-but-authored: open it in the editor, move anything, play it. That is
; the point (Talon, 2026-09-02). But the generator is the source of truth and the next run
; overwrites this file completely. An experiment that proves itself gets promoted back into the
; generator's config block; nothing else survives.
;
; The spec is docs/levels/KIT.md. Every geometric claim the kit makes is asserted twice: once by
; the generator before it writes a byte, and once against this file by tools/dev/kit_selftest.gd
; (tests/Run-KitCourseTest.ps1). A level that violates the kit is a red test, not a playtest
; discovery.
; ==========================================================================================
"""


def _gd_array(rows) -> str:
    """A Godot PackedStringArray literal. Deterministic, and it survives a .tscn round-trip."""
    return "PackedStringArray(" + ", ".join(f'"{r}"' for r in rows) + ")"


def emit(pieces: list[Piece]) -> str:
    ext = [
        ('Script', 'res://scripts/game/sandbox/SandboxWorld.cs', '1_world'),
        ('Shader', 'res://resources/shaders/bubbletest_ground_checker.gdshader', '2_checker'),
        ('Script', 'res://scripts/game/sandbox/SandboxCamera.cs', '3_camera'),
        ('Script', 'res://scripts/game/sandbox/SandboxAvatar.cs', '4_avatar'),
    ]

    subs: list[str] = []
    sub_count = 0

    subs.append('[sub_resource type="ProceduralSkyMaterial" id="ProceduralSkyMaterial_sky"]')
    subs.append("sky_top_color = Color(0.52, 0.6, 0.7, 1)")
    subs.append("sky_horizon_color = Color(0.74, 0.75, 0.76, 1)")
    subs.append("ground_bottom_color = Color(0.3, 0.31, 0.32, 1)")
    subs.append("ground_horizon_color = Color(0.74, 0.75, 0.76, 1)")
    subs.append("")
    subs.append('[sub_resource type="Sky" id="Sky_kit"]')
    subs.append('sky_material = SubResource("ProceduralSkyMaterial_sky")')
    subs.append("")
    subs.append('[sub_resource type="Environment" id="Environment_kit"]')
    subs.append("background_mode = 2")
    subs.append('sky = SubResource("Sky_kit")')
    subs.append("ambient_light_source = 2")
    subs.append("ambient_light_color = Color(0.66, 0.68, 0.72, 1)")
    subs.append("ambient_light_energy = 0.8")
    subs.append("tonemap_mode = 2")
    subs.append("")
    sub_count += 3

    for key, (r, g, b) in MATERIALS.items():
        subs.append(f'[sub_resource type="ShaderMaterial" id="ShaderMaterial_{key}"]')
        subs.append('shader = ExtResource("2_checker")')
        subs.append(f"shader_parameter/base_color = Color({r:.5f}, {g:.5f}, {b:.5f}, 1)")
        subs.append(f"shader_parameter/cell = {CHECKER_CELL_M:.5f}")
        subs.append(f"shader_parameter/strength = {CHECKER_STRENGTH:.5f}")
        subs.append("shader_parameter/roughness_value = 0.92")
        subs.append("shader_parameter/metallic_specular = 0.35")
        subs.append("")
        sub_count += 1

    nodes: list[str] = []
    for n, p in enumerate(pieces):
        mid = f"k{n:03d}"
        kind = p.shape[0]
        if kind == "box":
            sx, sy, sz = p.shape[1]
            subs.append(f'[sub_resource type="BoxMesh" id="BoxMesh_{mid}"]')
            subs.append(f"size = Vector3({sx:.5f}, {sy:.5f}, {sz:.5f})")
            subs.append("")
            subs.append(f'[sub_resource type="BoxShape3D" id="BoxShape3D_{mid}"]')
            subs.append(f"size = Vector3({sx:.5f}, {sy:.5f}, {sz:.5f})")
            subs.append("")
            mesh_ref, shape_ref = f"BoxMesh_{mid}", f"BoxShape3D_{mid}"
        elif kind == "cyl":
            radius, height = p.shape[1], p.shape[2]
            subs.append(f'[sub_resource type="CylinderMesh" id="CylinderMesh_{mid}"]')
            subs.append(f"top_radius = {radius:.5f}")
            subs.append(f"bottom_radius = {radius:.5f}")
            subs.append(f"height = {height:.5f}")
            subs.append("radial_segments = 24")
            subs.append("")
            subs.append(f'[sub_resource type="CylinderShape3D" id="CylinderShape3D_{mid}"]')
            subs.append(f"height = {height:.5f}")
            subs.append(f"radius = {radius:.5f}")
            subs.append("")
            mesh_ref, shape_ref = f"CylinderMesh_{mid}", f"CylinderShape3D_{mid}"
        else:
            radius = p.shape[1]
            subs.append(f'[sub_resource type="SphereMesh" id="SphereMesh_{mid}"]')
            subs.append(f"radius = {radius:.5f}")
            subs.append(f"height = {radius * 2.0:.5f}")
            subs.append("radial_segments = 24")
            subs.append("rings = 12")
            subs.append("")
            subs.append(f'[sub_resource type="SphereShape3D" id="SphereShape3D_{mid}"]')
            subs.append(f"radius = {radius:.5f}")
            subs.append("")
            mesh_ref, shape_ref = f"SphereMesh_{mid}", f"SphereShape3D_{mid}"
        sub_count += 2

        parent = f"Kit/{p.group}" if p.group else "."
        nodes.append(f'[node name="{p.name}" type="StaticBody3D" parent="{parent}"]')
        nodes.append(f"position = Vector3({p.pos[0]:.5f}, {p.pos[1]:.5f}, {p.pos[2]:.5f})")
        if any(abs(a) > 1e-9 for a in p.rot):
            nodes.append(f"rotation = Vector3({p.rot[0]:.7f}, {p.rot[1]:.7f}, {p.rot[2]:.7f})")
        nodes.append("")
        node_path = f"{parent}/{p.name}" if parent != "." else p.name
        nodes.append(f'[node name="Mesh" type="MeshInstance3D" parent="{node_path}"]')
        nodes.append(f'mesh = SubResource("{mesh_ref}")')
        nodes.append(f'material_override = SubResource("ShaderMaterial_{p.mat}")')
        nodes.append("")
        nodes.append(f'[node name="Shape" type="CollisionShape3D" parent="{node_path}"]')
        nodes.append(f'shape = SubResource("{shape_ref}")')
        nodes.append("")

    # --- Labels. Upright, facing -X (into an approaching player), NEVER billboarded ----
    labels: list[str] = []
    for p in pieces:
        if not p.label:
            continue
        lx = p.pos[0]
        labels.append(f'[node name="Label_{p.name}" type="Label3D" parent="Labels"]')
        labels.append(f"position = Vector3({lx:.5f}, {LABEL_HEIGHT_M:.5f}, {LABEL_Z:.5f})")
        labels.append(f"rotation = Vector3(0, {-math.pi / 2:.7f}, 0)")
        labels.append(f'text = "{p.label}"')
        labels.append("billboard = 0")
        labels.append("double_sided = true")
        labels.append("no_depth_test = false")
        labels.append("fixed_size = false")
        labels.append(f"font_size = {LABEL_FONT_SIZE}")
        labels.append(f"pixel_size = {LABEL_PIXEL_SIZE:.5f}")
        labels.append("outline_size = 24")
        labels.append("modulate = Color(0.94, 0.94, 0.95, 1)")
        labels.append("outline_modulate = Color(0.06, 0.06, 0.07, 1)")
        labels.append("")

    groups = []
    for p in pieces:
        if p.group and p.group not in groups:
            groups.append(p.group)

    out: list[str] = []
    out.append(f"[gd_scene load_steps={len(ext) + sub_count + 1} format=3]")
    out.append("")
    out.append(HEADER_NOTE.rstrip("\n"))
    out.append("")
    for kind, path, rid in ext:
        out.append(f'[ext_resource type="{kind}" path="{path}" id="{rid}"]')
    out.append("")
    out.extend(subs)
    out.append('[node name="KitCourse" type="Node3D"]')
    out.append('script = ExtResource("1_world")')
    # THE CROSS-CHECK STAMP. Everything the python reconstruction concluded is written into the
    # scene here so `tools/dev/kit_selftest.gd` can re-derive the same quantities from the
    # LOADED scene, through Godot's own transform code, and refuse to agree on python's word.
    # If python's Euler convention were not Godot's, these rows and the engine's measurement
    # would part company and the self-test would say so — which is the one thing python cannot
    # check about itself.
    out.append("metadata/kit_bands_up = " + _gd_array(
        [f"{n}|{lo:.6f}|{'inf' if hi is None else f'{hi:.6f}'}" for n, lo, hi in BANDS_UP]))
    out.append("metadata/kit_bands_gap = " + _gd_array(
        [f"{n}|{lo:.6f}|{'inf' if hi is None else f'{hi:.6f}'}" for n, lo, hi in BANDS_GAP]))
    out.append("metadata/kit_tops = " + _gd_array(
        [f"{p.name}|{ty:.6f}|{cx:.6f}|{cz:.6f}|{hx:.6f}|{hz:.6f}"
         for p in pieces for (ty, (cx, cz), (hx, hz)) in p.tops]))
    out.append("metadata/kit_runs = " + _gd_array(
        [f"{p.name}|{p.run[0]:.6f}|{p.run[1]:.6f}" for p in pieces if p.run is not None]))
    out.append("metadata/kit_kinds = " + _gd_array(
        [f"{p.name}|{p.kind}" for p in pieces]))
    out.append(f"metadata/kit_body_radius = {BODY_RADIUS_M:.6f}")
    out.append(f"metadata/kit_floor_max_angle = {FLOOR_MAX_ANGLE_DEG:.6f}")
    out.append(f"metadata/kit_angle_margin = {ANGLE_MARGIN_DEG:.6f}")
    out.append(f"metadata/kit_seed = {SEED}")
    out.append("")
    out.append('[node name="WorldEnvironment" type="WorldEnvironment" parent="."]')
    out.append('environment = SubResource("Environment_kit")')
    out.append("")
    out.append('[node name="Sun" type="DirectionalLight3D" parent="."]')
    out.append("rotation = Vector3(-1.0122909, -0.5934119, 0)")
    out.append("light_color = Color(1, 0.97, 0.92, 1)")
    out.append("light_energy = 1.05")
    out.append("shadow_enabled = true")
    out.append("")
    out.append('[node name="Kit" type="Node3D" parent="."]')
    out.append("")
    for g in groups:
        out.append(f'[node name="{g}" type="Node3D" parent="Kit"]')
        out.append("")
    out.append('[node name="Labels" type="Node3D" parent="."]')
    out.append("")
    out.extend(nodes)
    out.extend(labels)
    out.append('[node name="Spawn" type="Marker3D" parent="."]')
    out.append(f"position = Vector3({SPAWN_X:.5f}, {SPAWN_LIP:.5f}, 0)")
    out.append("")
    out.append('[node name="Camera" type="Node3D" parent="."]')
    out.append('script = ExtResource("3_camera")')
    out.append("")
    out.append('[node name="Player" type="CharacterBody3D" parent="."]')
    out.append('script = ExtResource("4_avatar")')
    out.append("body_color = Color(0.62, 0.83, 0.72, 1)")
    out.append(f"position = Vector3({SPAWN_X:.5f}, 1.2, 0)")
    out.append("")
    # SandboxWorld.cs wires exactly three dummies by name and builds nothing; they are the
    # price of hosting the body with no C# change, and they earn it as a live 1.24 m scale
    # reference standing next to the primitives. Parked on the spawn apron, roam radius 7 m.
    for name, colour, z in (("DummyA", "0.98, 0.75, 0.62", -6.0),
                            ("DummyB", "0.97, 0.88, 0.58", 6.0),
                            ("DummyC", "0.78, 0.72, 0.92", -9.0)):
        out.append(f'[node name="{name}" type="CharacterBody3D" parent="."]')
        out.append('script = ExtResource("4_avatar")')
        out.append(f"body_color = Color({colour}, 1)")
        out.append(f"position = Vector3({SPAWN_X - 4.0:.5f}, 1.2, {z:.5f})")
        out.append("")

    return "\n".join(out).rstrip("\n") + "\n"


# =====================================================================================

VIOLATIONS = {
    # The first four are the SIGN family, and they are the reason validate_derived() exists:
    # each one is the exact defect that shipped in the first KitCourse.tscn, and every angle
    # rule in this file passes all four.
    "wedge-sign": "the steepest Wedge mirrored: same 40 deg, descending instead of climbing",
    "sag-sign": "every Sag facet mirrored: the bowl becomes a dome (the defect as it shipped)",
    "tilt-sign": "the steepest Tilt mirrored: it leans over the approach, not away from it",
    "ramp-sign": "the return ramp mirrored: it climbs away from the plateau, not onto it",
    "block-band": "a Block declared Technique, built at a Hop",
    "denial-tease": "the Denial block slid inside a Technique hop of the Technique block",
    "wedge-angle": "the steepest Wedge at 44 deg, inside the forbidden band",
    "tilt-hop": "a Tilt whose top edge is one hop above the ground",
    "sag-step": "a 0.22 m lip in the middle of a Sag's surface",
    "sag-slip": "one Sag facet lifted 0.14 m off its neighbours' shared endpoint",
    "dome-size": "a Dome big enough to stand on",
    "log-thin": "a Log whose crown is narrower than the body",
}


def print_facts(pieces, facts, report) -> None:
    print(f"[kit] {facts['pieces']} pieces, seed {SEED}")
    # The envelope itself, so a run's log records WHICH motor the course was built against
    # rather than leaving a reader to infer it from the bands. Same line shape as
    # motor_arc.gd's describe(), so the two can be diffed by eye.
    print("[kit] motor-arc  jog %.2f sprint %.2f | held %.3f m / %.3f m | tap %.3f m / %.3f m "
          "| double %.3f m / %.3f m" % (ARC["JogSpeed"], ARC["SprintSpeed"], ARC["SprintApex"],
                                        ARC["SprintRange"], ARC["TapApex"], ARC["TapRange"],
                                        ARC["DoubleApex"], ARC["DoubleRange"]))
    print("[kit] VERTICAL BANDS (fractions of the MotorArc envelope):")
    for n, lo, hi in facts["bands_up"]:
        hi_s = "  (open)" if hi is None else f"{hi:7.3f}"
        print(f"[kit]     {n:<10} {lo:7.3f} .. {hi_s} m")
    print("[kit] HORIZONTAL BANDS:")
    for n, lo, hi in facts["bands_gap"]:
        hi_s = "  (open)" if hi is None else f"{hi:7.3f}"
        print(f"[kit]     {n:<10} {lo:7.3f} .. {hi_s} m")
    print(f"[kit] body radius       {facts['body_radius']:.4f} m "
          f"(diameter {facts['body_radius'] * 2:.4f} m)")
    print(f"[kit] forbidden band    ({FLOOR_ANGLE_DEG:.0f}, {WALL_ANGLE_DEG:.0f}) deg — "
          f"{report['forbidden_band_faces_checked']} body-wide upward faces checked, "
          f"closest approach {report['forbidden_band_worst'][0]:.3f} deg "
          f"({report['forbidden_band_worst'][1]})")
    print(f"[kit] Dome ceiling      radius < {facts['dome_max_radius']:.4f} m "
          "— anything larger is a hill, not a Dome")
    print(f"[kit] Log floor         radius >= {facts['log_min_radius']:.4f} m "
          "— anything smaller sheds and cannot be balanced on")
    for name, a in report["wedges"]:
        print(f"[kit] wedge  {name:<20} {a:6.3f} deg")
    for name, a, top in report["tilts"]:
        print(f"[kit] tilt   {name:<20} {a:6.3f} deg, top edge {top:6.3f} m")
    for name, r, crown, strip in report["logs"]:
        print(f"[kit] log    {name:<20} r {r:.3f} m, crown {crown:.3f} m, "
              f"walkable strip {strip:.3f} m")
    for group, (n, a) in report["sags"].items():
        print(f"[kit] sag    {group:<20} {n} facets, steepest {a:6.3f} deg, no step")
    for name, r, cap in report["domes"]:
        print(f"[kit] dome   {name:<20} r {r:.3f} m, walkable pole cap {cap:.3f} m")
    print(f"[kit] denial pieces     {', '.join(report['denial_pieces'])}")
    print("[kit] piece census      " + ", ".join(f"{k}={v}" for k, v in
                                                 sorted(report["kinds"].items())))


def run_controls(quiet: bool = False) -> None:
    """The two positive controls, run before every generation. Neither is optional.

    A validator that has never rejected anything has not been shown to validate, and a motor
    derivation that cannot reproduce the measurement it replaced is a guess with a formula in
    front of it. Both are checked here, and a failure of either stops the run before a byte
    is written.
    """
    why = verify_motor()
    if why:
        sys.exit(f"FAIL [motor-control]: {why}")
    if not quiet:
        print("[kit] motor control  PASS -- the pre-ruling tuning reproduces MOVE-3e's "
              "1.534 / 6.192 / 0.467 / 1.710 to the millimetre")
    for label, ok, detail in selfcheck_signs():
        if not ok:
            sys.exit(f"FAIL [sign-control]: a slab {label} did not reconstruct that way "
                     f"({detail}). The reconstruction cannot tell a ramp from a drop, so its "
                     "verdict on the course means nothing.")
        if not quiet:
            print(f"[kit] sign control   PASS -- a slab {label}: {detail}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--print", action="store_true", dest="dry",
                    help="validate and print the numbers; write nothing")
    ap.add_argument("--controls", action="store_true",
                    help="run the motor and sign positive controls only, then exit")
    ap.add_argument("--violate", metavar="NAME",
                    help="POSITIVE CONTROL: break one rule and prove the validator catches it")
    ap.add_argument("--emit-violation", metavar="NAME", dest="emit_violation",
                    help="write a deliberately bad scene (validator SKIPPED) as a test fixture")
    ap.add_argument("--out", metavar="PATH",
                    help="write the scene here instead of the canonical path (also where "
                         "--emit-violation writes)")
    ap.add_argument("--violations", action="store_true", help="list the violation names")
    args = ap.parse_args()

    if args.violations:
        for name, what in VIOLATIONS.items():
            print(f"{name:<16} {what}")
        return

    if args.controls:
        run_controls()
        return

    run_controls(quiet=bool(args.emit_violation))

    if args.emit_violation:
        if args.emit_violation not in VIOLATIONS:
            sys.exit(f"FAIL: unknown violation {args.emit_violation!r}; "
                     f"try --violations")
        if not args.out:
            sys.exit("FAIL: --emit-violation needs --out")
        out = Path(args.out).resolve()
        if out == SCENE:
            sys.exit("FAIL: --emit-violation refuses to write the canonical scene. The shipped "
                     "KitCourse.tscn is only ever written by a run that passed every rule.")
        pieces, _ = build(violation=args.emit_violation)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(emit(pieces), encoding="utf-8", newline="\n")
        print(f"[kit] wrote VIOLATING fixture ({args.emit_violation}: "
              f"{VIOLATIONS[args.emit_violation]}) to {out}")
        return

    if args.violate and args.violate not in VIOLATIONS:
        sys.exit(f"FAIL: unknown violation {args.violate!r}; try --violations")

    pieces, facts = build(violation=args.violate)
    report = validate(pieces)          # exits non-zero, naming the rule, on any violation
    print_facts(pieces, facts, report)

    if args.violate:
        sys.exit(f"FAIL [positive-control]: --violate {args.violate} passed every rule. "
                 "A validator that has never fired has not been shown to work.")
    if args.dry:
        return

    # --out lets the suite generate twice to throwaway paths and compare the two, which is how
    # byte-reproducibility is asserted WITHOUT requiring the committed scene to equal a fresh
    # run. That difference is deliberate and it is Talon's 2026-09-02 ruling: the scene may be
    # opened and hand-placed in the editor. A suite that demanded byte-equality with the
    # generator would make that ruling unusable; the suite asserts the KIT RULES against
    # whatever is shipped instead, and the generator stays the source of truth for regenerating.
    out = SCENE if not args.out else Path(args.out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(emit(pieces), encoding="utf-8", newline="\n")
    print(f"[kit] wrote {out}")


if __name__ == "__main__":
    main()
