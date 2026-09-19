#!/usr/bin/env python3
"""LD-3 -- the cadence audit's arithmetic: seconds between features along each section's line.

Research A2 (docs/research/2026-09-02-watis-...-RESEARCH-RESULT.md) says cadence is the unit:
author each region to a seconds-between-features target at the speed players arrive with, and
Talon's "everything is a bit too spread apart" (2026-08-29) is a cadence complaint. LEVEL-4 proved
the sections tile edge to edge, so the spread is INSIDE them. This tool measures where.

WHAT IS AUTHORED HERE AND WHAT IS DERIVED. The LINES -- which features a player meets in which
order along a section's intended route -- are judgment, and they are written out below as
ordered waypoint lists so the judgment is inspectable. Every waypoint's POSITION is derived: a
node path in a section .tscn (resolved through tools/dev/ld3_tscn.py at the anchor BubbleTest.tscn
instances it), a prop in BubbleTest.tscn, or a constant read out of BubbleTestLayout.cs. Nothing
is retyped. The speeds come out of MotorTuning.cs (MoveSpeed x SprintMultiplier), not copied.

REGION TYPES and their thresholds (research A2, synthesis 1), a value fork this packet owns:
  fast      <= 3.0 s at SPRINT -- open ground a player is meant to cross at speed
  approach  <= 5.0 s at SPRINT -- the way to a thing; the hub plaza, the bare plates, the seams
  climb     <= 5.0 s at JOG    -- a stair or a spiral, where arrival speed is a jog at best
  cap        8.0 s anywhere a player is meant to be moving, at the region's own speed
A gap is flagged when it exceeds its region's threshold; CAP when it exceeds 8 s.

A STAIR IS ONE ROW. Red's 23 treads, blue's 37 pads and the tangle's 23 + 41 treads are listed as
one climb row each, with the per-step statistics computed from every consecutive tread pair --
listing 120 rows of 0.4 s would bury the three gaps that matter.

INBOUND. Every line is also read reversed. Where the inbound route is the same waypoints in the
other order the gaps are identical and the table says so once; where it differs (a summit left by
the drop rather than the stair) the inbound line is its own list.

    python tools/dev/ld3_cadence.py            # markdown tables + summary, to stdout
    python tools/dev/ld3_cadence.py --check    # only verify every waypoint resolves; exit 1 if not
"""

from __future__ import annotations

import argparse
import io
import math
import re
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ld3_tscn as T  # noqa: E402

MOTOR_CS = T.ROOT / "scripts/net/MotorTuning.cs"
THRESH = {"fast": 3.0, "approach": 5.0, "climb": 5.0}
CAP_S = 8.0


def speeds() -> tuple[float, float, float]:
    """(jog, sprint, falling gravity) out of MotorTuning.Default -- read, not copied."""
    text = MOTOR_CS.read_text(encoding="utf-8")
    block = text[text.index("public static MotorTuning Default"):]
    jog = float(re.search(r"MoveSpeed = ([\d.]+)f", block).group(1))
    mult = float(re.search(r"SprintMultiplier = ([\d.]+)f", block).group(1))
    g = float(re.search(r"Gravity = ([\d.]+)f", block).group(1))
    fall = float(re.search(r"FallGravityMultiplier = ([\d.]+)f", block).group(1))
    return jog, jog * mult, g * fall


# --- waypoint resolution ----------------------------------------------------------------------
# A waypoint is (label, ref) where ref is one of:
#   "Section:path"        the TOP-centre of that collider (a standable surface)
#   "Section:bubble:Name" a Bubble instance's position
#   "prop:Name"           a Props/* node in BubbleTest.tscn
#   "layout:key"          a BubbleTestLayout.cs constant: spawn, pedestal, lever, tv:<Name>
#   "at:x,y,z"            a literal, used ONLY for section edges and plate corners (not features)

class Level:
    def __init__(self) -> None:
        self.level = T.load_level()
        self.props = dict(T.world_props())
        self.layout = T.layout_constants()
        self.tvs = dict(self.layout["tvs"])

    def collider(self, sec: str, path: str) -> T.Collider:
        for c in self.level[sec].colliders:
            if c.path == path:
                return c
        raise KeyError(f"{sec}:{path}")

    def resolve(self, ref: str) -> T.Vec:
        if ref.startswith("at:"):
            x, y, z = (float(v) for v in ref[3:].split(","))
            return (x, y, z)
        if ref.startswith("prop:"):
            return self.props[ref[5:]]
        if ref.startswith("layout:"):
            key = ref[7:]
            if key == "spawn":
                return self.layout["spawn_ring_centre"]
            if key == "pedestal":
                return self.layout["pedestal"]
            if key == "lever":
                p = self.layout["pedestal"]
                return (p[0] + 1.5, p[1], p[2])        # BubbleResetLever.OffsetFromPedestalM
            if key.startswith("tv:"):
                return self.tvs[key[3:]]
            raise KeyError(ref)
        sec, _, rest = ref.partition(":")
        if rest.startswith("bubble:"):
            name = rest[7:]
            for b in self.level[sec].bubbles:
                if b.name == name:
                    return b.world.origin
            raise KeyError(ref)
        c = self.collider(sec, rest)
        return (c.centre[0], c.top, c.centre[2])

    def stair(self, sec: str, paths: list[str]) -> list[T.Vec]:
        return [self.resolve(f"{sec}:{p}") for p in paths]


def plan_dist(a: T.Vec, b: T.Vec) -> float:
    return math.hypot(b[0] - a[0], b[2] - a[2])


def dist3(a: T.Vec, b: T.Vec) -> float:
    return T.vec_len(T.vec_sub(b, a))


# --- the lines --------------------------------------------------------------------------------
# Each entry: (label, ref, region-of-the-gap-ARRIVING-here, note). A "stair" entry carries the
# tread list and is reported as one row with per-step stats.

R = "RedCairns"
B = "BluePrecision"
C = "CyanRun"
G = "GreenHills"
Tg = "Tangle"
H = "Hub"

RED_TREADS = [f"Cairn_Main/Tread{i:02d}" for i in range(23)] + ["Cairn_Main/SummitBlock"]
TANGLE_STAIR = [f"Stair/Stair{i:02d}" for i in range(23)]
TANGLE_SPIRE = ["Tower/Base"] + [f"Tower/Tw{i:02d}" for i in range(41)] + ["Tower/Summit"]
TANGLE_SPUR = ["Tower/Tw12"] + [f"Tower/Spur{i:02d}" for i in range(4)] + ["Tower/SpurPad"]
BLUE_BASE = ["Tier1_Base/Mesa1", "Tier1_Base/BlockA", "Tier1_Base/Mesa2", "Tier1_Base/BlockB",
             "Tier1_Base/BarkLedge1", "Tier1_Base/BlockC", "Tier1_Base/BarkLedge2", "Tier1_Base/BlockD"]
BLUE_SPIRAL = (["Tier2/R00", "Tier2/R01", "Tier2/R02", "Tier2/R03", "Tier2/D04", "Tier2/R04",
                "Tier2/R05", "Tier2/R06", "Tier2/R07", "Tier2/R08", "Tier2/R09"]
               + ["Tier3/R10", "Tier3/R11", "Tier3/R12", "Tier3/D13", "Tier3/R13", "Tier3/R14",
                  "Tier3/R15", "Tier3/R16", "Tier3/R17", "Tier3/R18"]
               + ["Tier4/R19", "Tier4/R20", "Tier4/R21", "Tier4/D22", "Tier4/R22", "Tier4/R23",
                  "Tier4/R24", "Tier4/R25", "Tier4/R26", "Tier4/R27"]
               + ["Tier5/R28", "Tier5/R29", "Tier5/R30", "Tier5/D31", "Tier5/R31", "Tier5/R32",
                  "Tier5/R33", "Tier5/R34", "Tier5/R35", "Tier5/R36", "Core"])
GREEN_STONES = [f"SteppingStones/Stone{i}" for i in range(8)]
CYAN_SLALOM = [f"Slalom/Post_{i}/Body" for i in range(9)]
CYAN_LANE = [f"{C}:bubble:Bubble_Cyan_{i:02d}" for i in range(1, 8)]

LINES: dict[str, dict] = {
    # ------------------------------------------------------------------ the hub, five ways out
    "Hub -- north, spawn to the red seam": {
        "inbound": "reversed",
        "way": [
            ("spawn ring centre", "layout:spawn", None, "SpawnRingCentre (0, 0, 18); every session opens here"),
            ("seating ring, east bench", f"{H}:Seating/Ring/Bench1/Body", "approach", "0.32 m bench: a held jump, not a tap (TapApex 0.300)"),
            ("lever + pedestal + Bubble_Hub_01", "layout:lever", "approach", "the one interaction on the plaza"),
            ("Bubble_Hub_02", f"{H}:bubble:Bubble_Hub_02", "approach", ""),
            ("Bubble_Hub_07 (behind the red signpost)", f"{H}:bubble:Bubble_Hub_07", "approach", "the plaza's north half is empty between these two"),
            ("Bubble_Seam_01 / _02 (the red seam)", f"{H}:bubble:Bubble_Seam_01", "approach", "connector ToRed, z -40..-45"),
        ],
    },
    "Hub -- east, spawn to the blue seam": {
        "inbound": "reversed",
        "way": [
            ("spawn ring centre", "layout:spawn", None, ""),
            ("GoldCube_Hub_0", "prop:GoldCube_Hub_0", "approach", "carryable"),
            ("east seating cluster + Bubble_Hub_05", f"{H}:bubble:Bubble_Hub_05", "approach", ""),
            ("Bubble_Seam_03 / _04 (the blue seam)", f"{H}:bubble:Bubble_Seam_03", "approach", "connector ToBlue, x 40..45"),
        ],
    },
    "Hub -- south, spawn to the cyan seam": {
        "inbound": "reversed",
        "way": [
            ("spawn ring centre", "layout:spawn", None, ""),
            ("GoldCube_Hub_3", "prop:GoldCube_Hub_3", "approach", "behind the ring"),
            ("Bubble_Seam_05 / _06 (the cyan seam)", f"{H}:bubble:Bubble_Seam_05", "approach", "connector ToCyan, z 40..50"),
        ],
    },
    "Hub -- west, spawn to the green seam": {
        "inbound": "reversed",
        "way": [
            ("spawn ring centre", "layout:spawn", None, ""),
            ("GoldCube_Hub_1", "prop:GoldCube_Hub_1", "approach", ""),
            ("west seating cluster + Bubble_Hub_06", f"{H}:bubble:Bubble_Hub_06", "approach", ""),
            ("HubTv (the grey-level television)", "layout:tv:HubTv", "approach", "off the direct line, NW quadrant"),
            ("Bubble_Hub_08 (behind the green signpost)", f"{H}:bubble:Bubble_Hub_08", "approach", ""),
            ("Bubble_Seam_07 (the green seam)", f"{H}:bubble:Bubble_Seam_07", "approach", "connector ToGreen, x -40..-50"),
        ],
    },
    "Hub -- north-east, spawn to the tangle (no connector)": {
        "inbound": "reversed",
        "way": [
            ("spawn ring centre", "layout:spawn", None, ""),
            ("GoldCube_Hub_2", "prop:GoldCube_Hub_2", "approach", ""),
            ("Bubble_Hub_09 (the NE apron)", f"{H}:bubble:Bubble_Hub_09", "approach", ""),
            ("hub plate corner (40, -40)", "at:40,0,-40", "approach", "no connector on this diagonal; the plate ends"),
            ("GoldCube_Tangle_0", "prop:GoldCube_Tangle_0", "approach", "first thing on the tangle plate"),
            ("Bubble_Tangle_01 (the lead-in)", f"{Tg}:bubble:Bubble_Tangle_01", "approach", ""),
            ("Stair00 (the tangle stair's foot)", f"{Tg}:Stair/Stair00", "approach", "the climb begins"),
        ],
    },
    # ------------------------------------------------------------------ red
    "Red -- the seam to the main cairn's summit": {
        "inbound": "different",
        "way": [
            ("Bubble_Seam_01 (the seam)", f"{H}:bubble:Bubble_Seam_01", None, "z -43"),
            ("GoldCube_Red_1", "prop:GoldCube_Red_1", "approach", "12 m east of the axis"),
            ("Rock_04 (a boulder, 1.0 m)", f"{R}:Rubble/Rock_04/Body", "approach", "the first thing on the axis"),
            ("Bubble_Red_09 over Tread00", f"{R}:Cairn_Main/Tread00", "approach", "the stair's foot, 1.69 m top: a held jump"),
            ("STAIR Tread00 -> Tread22 -> SummitBlock", ("stair", R, RED_TREADS), "climb", "23 treads + the summit block"),
            ("RedTv on the perch", "layout:tv:RedTv", "climb", "the reward"),
        ],
        "inbound_way": [
            ("RedTv on the perch", "layout:tv:RedTv", None, "22.81 m up"),
            ("the drop: TvPerch south lip to the plate", "drop:-0.16,0,-75.8", "climb", "step off the south lip; recoverable, the walk back is the cost"),
            ("Bubble_Red_08 (under the rubble shelf)", f"{R}:bubble:Bubble_Red_08", "approach", "hidden, at the cairn's south flank"),
            ("Rock_04", f"{R}:Rubble/Rock_04/Body", "approach", ""),
            ("Bubble_Seam_01", f"{H}:bubble:Bubble_Seam_01", "approach", ""),
        ],
    },
    "Red -- the seam to the easy cairn (west)": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_01 (the seam)", f"{H}:bubble:Bubble_Seam_01", None, ""),
            ("GoldCube_Red_0", "prop:GoldCube_Red_0", "approach", ""),
            ("Rock_07", f"{R}:Rubble/Rock_07/Body", "approach", ""),
            ("Cairn_Easy Step00", f"{R}:Cairn_Easy/Step00", "approach", "0.45 m: a tap"),
            ("STAIR Step00 -> Step13 -> SummitBlock", ("stair", R, [f"Cairn_Easy/Step{i:02d}" for i in range(14)] + ["Cairn_Easy/SummitBlock"]), "climb", "jog-taps only"),
        ],
    },
    "Red -- the seam to the mid cairn (east)": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_01 (the seam)", f"{H}:bubble:Bubble_Seam_01", None, ""),
            ("GoldCube_Red_2", "prop:GoldCube_Red_2", "approach", ""),
            ("Bubble_Red_06 over Rock_02", f"{R}:bubble:Bubble_Red_06", "approach", ""),
            ("Rock_05", f"{R}:Rubble/Rock_05/Body", "approach", ""),
            ("Cairn_Mid Step00", f"{R}:Cairn_Mid/Step00", "approach", ""),
            ("STAIR Step00 -> Step09 -> SummitBlock", ("stair", R, [f"Cairn_Mid/Step{i:02d}" for i in range(10)] + ["Cairn_Mid/SummitBlock"]), "climb", "1.15 m rises: held jumps"),
        ],
    },
    # ------------------------------------------------------------------ blue
    "Blue -- the seam to the tower's summit": {
        "inbound": "different",
        "way": [
            ("Bubble_Seam_03 (the seam)", f"{H}:bubble:Bubble_Seam_03", None, "x 43"),
            ("GoldCube_Blue_1", "prop:GoldCube_Blue_1", "approach", "8 m north of the axis"),
            ("GoldCube_Blue_0", "prop:GoldCube_Blue_0", "approach", "on the way to the tower's front door"),
            ("MesaRamp1 (the front door)", f"{B}:Tier1_Base/MesaRamp1Mount/MesaRamp1", "approach", "the base tier begins; Bubble_Blue_01 over Mesa1"),
            ("BASE TIER Mesa1 -> BlockD", ("stair", B, BLUE_BASE), "climb", "8 hops of 1.0 m"),
            ("SPIRAL R00 -> R36 -> Core deck", ("stair", B, BLUE_SPIRAL), "climb", "37 pads incl. the four flat-station detours; the ladder and the beam are the hard lines beside it"),
            ("BlueTv on the perch", "layout:tv:BlueTv", "climb", "the reward"),
        ],
        "inbound_way": [
            ("BlueTv on the perch", "layout:tv:BlueTv", None, "41.2 m up"),
            ("the drop: perch south lip to the plate", "drop:79,0,2.5", "climb", "onto the base tier or the plate; recoverable"),
            ("GoldCube_Blue_1", "prop:GoldCube_Blue_1", "approach", ""),
            ("Bubble_Seam_03", f"{H}:bubble:Bubble_Seam_03", "approach", ""),
        ],
    },
    # ------------------------------------------------------------------ cyan
    "Cyan -- the ruler lane (west), seam to the 60 m line": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_05 (the seam)", f"{H}:bubble:Bubble_Seam_05", None, "z 45"),
            ("Bubble_Cyan_25 over Obs_10 box", f"{C}:bubble:Bubble_Cyan_25", "fast", "the mid band's first box"),
            ("Bubble_Cyan_01 (0 m strip)", CYAN_LANE[0], "fast", "the lane at x -30"),
            ("LANE strips 0 -> 60 m (Cyan_01..07)", ("stair", C, [f"bubble:Bubble_Cyan_{i:02d}" for i in range(1, 8)]), "fast", "one bubble every 10 m; the last at head height"),
            ("Obs_15 box (far mid band)", f"{C}:Obstacles/Obs_15_box/Box/Body", "fast", ""),
            ("Goal block + Cyan_13/14/15", f"{C}:Goal/Body", "fast", "the saturated 1.5 m cube at the far end"),
        ],
    },
    "Cyan -- the slalom (east), seam to the last post": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_05 (the seam)", f"{H}:bubble:Bubble_Seam_05", None, ""),
            ("Bubble_Cyan_30 over Obs_16 box", f"{C}:bubble:Bubble_Cyan_30", "fast", ""),
            ("Post_0 (slalom gate 1)", f"{C}:Slalom/Post_0/Body", "fast", ""),
            ("SLALOM Post_0 -> Post_8", ("stair", C, CYAN_SLALOM), "fast", "9 posts every 8 m, 3 m stagger"),
            ("Bubble_Cyan_12 (past the last post)", f"{C}:bubble:Bubble_Cyan_12", "fast", ""),
        ],
    },
    "Cyan -- the middle aisle, seam to the goal": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_05 (the seam)", f"{H}:bubble:Bubble_Seam_05", None, "the 12 m aisle is EMPTY by design (program section 6.12 sightline)"),
            ("Bubble_Cyan_13 (the run-in)", f"{C}:bubble:Bubble_Cyan_13", "fast", "the first feature in the aisle"),
            ("Goal block", f"{C}:Goal/Body", "fast", ""),
        ],
    },
    "Cyan -- the west obstacle field, seam to the far corner": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_05 (the seam)", f"{H}:bubble:Bubble_Seam_05", None, ""),
            ("Obs_00 box + Cyan_16", f"{C}:Obstacles/Obs_00_box/Box/Body", "fast", "x -58"),
            ("Obs_01 wall + Cyan_17", f"{C}:Obstacles/Obs_01_wall/Wall/Body", "fast", ""),
            ("Obs_02 ramp + Cyan_18", f"{C}:Obstacles/Obs_02_ramp/Ramp/Body", "fast", ""),
            ("Obs_03 box + Cyan_19", f"{C}:Obstacles/Obs_03_box/Box/Body", "fast", ""),
            ("Obs_04 rock + Cyan_20", f"{C}:Obstacles/Obs_04_rock/Rock/Body", "fast", ""),
            ("Obs_05 box + Cyan_21", f"{C}:Obstacles/Obs_05_box/Box/Body", "fast", ""),
            ("Obs_06 wall + Cyan_22", f"{C}:Obstacles/Obs_06_wall/Wall/Body", "fast", ""),
            ("GoldCube_Cyan_2", "prop:GoldCube_Cyan_2", "fast", ""),
            ("Obs_07 picnic set", f"{C}:Obstacles/Obs_07_picnic/Table/Body", "fast", ""),
            ("Obs_08 box + Cyan_23", f"{C}:Obstacles/Obs_08_box/Box/Body", "fast", ""),
            ("Obs_09 ramp + Cyan_24", f"{C}:Obstacles/Obs_09_ramp/Ramp/Body", "fast", ""),
        ],
    },
    # ------------------------------------------------------------------ green
    "Green -- the seam to the island by the stones": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_07 (the seam)", f"{H}:bubble:Bubble_Seam_07", None, "x -45"),
            ("Bubble_Green_12 (the greeting ridge)", f"{G}:bubble:Bubble_Green_12", "approach", "on the hill that greets you off the path; the hills are scenery to the motor (no slope term)"),
            ("Bubble_Green_08 (east postpile column)", f"{G}:bubble:Bubble_Green_08", "approach", "the island's east side is a beach since STONE-2"),
            ("Postpile summit cap + Bubble_Green_06", f"{G}:Postpile/StaticBody3D", "climb", "the tallest column, 3.5 m"),
            ("Stone7 (the crossing's east end)", f"{G}:SteppingStones/Stone7", "climb", ""),
            ("STONES Stone7 -> Stone0 (west)", ("stair", G, list(reversed(GREEN_STONES))), "climb", "8 stones, 1.4-1.9 m centre to centre"),
            ("Bubble_Green_13 (the west saddle)", f"{G}:bubble:Bubble_Green_13", "approach", ""),
            ("Bubble_Green_11 (the NW ridge)", f"{G}:bubble:Bubble_Green_11", "approach", "the far corner"),
        ],
    },
    "Green -- the seam to the north knoll": {
        "inbound": "reversed",
        "way": [
            ("Bubble_Seam_07 (the seam)", f"{H}:bubble:Bubble_Seam_07", None, ""),
            ("Bubble_Green_12 (the greeting ridge)", f"{G}:bubble:Bubble_Green_12", "approach", ""),
            ("Bubble_Green_16 (half-sunk, north shore)", f"{G}:bubble:Bubble_Green_16", "approach", ""),
            ("Bubble_Green_14 (the north knoll)", f"{G}:bubble:Bubble_Green_14", "approach", ""),
        ],
    },
    # ------------------------------------------------------------------ tangle
    "Tangle -- the hub-facing corner to the spire's summit": {
        "inbound": "different",
        "way": [
            ("GoldCube_Tangle_0 (the plate's SW corner)", "prop:GoldCube_Tangle_0", None, "where the NE diagonal lands"),
            ("Bubble_Tangle_01 (the lead-in)", f"{Tg}:bubble:Bubble_Tangle_01", "approach", ""),
            ("Stair00", f"{Tg}:Stair/Stair00", "approach", "the stair's foot is on the plate's EAST side; the lead-in is on its west"),
            ("STAIR Stair00 -> Stair22", ("stair", Tg, TANGLE_STAIR), "climb", "23 treads, 0.8-1.2 m rises, no plan gap"),
            ("Tower/Base (the spire's foot)", f"{Tg}:Tower/Base", "climb", "flush with the stack summit at 23.1 m"),
            ("SPIRE Base -> Tw40 -> Summit", ("stair", Tg, TANGLE_SPIRE), "climb", "41 treads, 0.84-0.95 m rises, proved to overlap in plan"),
            ("TangleTv on the summit cap", "layout:tv:TangleTv", "climb", "the top of the world"),
        ],
        "inbound_way": [
            ("TangleTv on the summit cap", "layout:tv:TangleTv", None, "60.8 m up"),
            ("the drop: cap south lip to the plate", "drop:70.59,0,-65.2", "climb", "onto the pile or the plate; recoverable"),
            ("Bubble_Tangle_01 (the lead-in)", f"{Tg}:bubble:Bubble_Tangle_01", "approach", ""),
            ("GoldCube_Tangle_0", "prop:GoldCube_Tangle_0", "approach", ""),
        ],
    },
    "Tangle -- the spur (a dead-end branch off tread 12)": {
        "inbound": "reversed",
        "way": [
            ("Tw12 (the branch point)", f"{Tg}:Tower/Tw12", None, "34.8 m up"),
            ("SPUR Tw12 -> Spur03 -> SpurPad", ("stair", Tg, TANGLE_SPUR), "climb", "four level blocks out to the pad"),
            ("HiddenTv on the spur pad", "layout:tv:HiddenTv", "climb", "the reward; the way back is the same four blocks"),
        ],
    },
    "Tangle -- the far corner (GoldCube_Tangle_1)": {
        "inbound": "reversed",
        "way": [
            ("Stair00 (the stair's foot)", f"{Tg}:Stair/Stair00", None, ""),
            ("Rubble21 (a plate-level block)", f"{Tg}:Rubble/Rubble21", "approach", "2.3 m top: a climb-on, not a hop"),
            ("GoldCube_Tangle_1 (the plate's far corner)", "prop:GoldCube_Tangle_1", "approach", "(100, -100): 40 m from the stair, nothing between"),
        ],
    },
}


def fmt_s(m: float, v: float) -> str:
    return f"{m / v:.1f}"


def run(L: Level, jog: float, sprint: float, fall_g: float, check_only: bool) -> str:
    out = io.StringIO()
    w = out.write
    flagged: list[tuple[str, str, float, float, str]] = []   # (line, gap label, metres, seconds, region)
    all_gaps_s: list[float] = []
    per_line_counts: dict[str, tuple[int, int]] = {}
    problems: list[str] = []

    def region_speed(region: str) -> float:
        return jog if region == "climb" else sprint

    def emit_way(title: str, way: list, tag: str) -> None:
        nonlocal flagged, all_gaps_s
        w(f"\n#### {title} -- {tag}\n\n")
        w("| # | feature | world (x, y, z) | gap m | plan m | sprint s | jog s | region | flag | note |\n")
        w("|---|---|---|---|---|---|---|---|---|---|\n")
        prev: T.Vec | None = None
        n_flag = n_cap = 0
        for i, (label, ref, region, note) in enumerate(way):
            if isinstance(ref, tuple):        # a stair: one row, per-step stats
                _, sec, paths = ref
                pts = [L.resolve(f"{sec}:{p}") for p in paths]
                steps = [dist3(a, b) for a, b in zip(pts, pts[1:])]
                rises = [b[1] - a[1] for a, b in zip(pts, pts[1:])]
                v = region_speed(region)
                secs = [s / v for s in steps]
                first, last = pts[0], pts[-1]
                gap_in = dist3(prev, first) if prev is not None else 0.0
                plan_in = plan_dist(prev, first) if prev is not None else 0.0
                s_in = gap_in / region_speed(region) if prev is not None else 0.0
                flag = ""
                if prev is not None and s_in > THRESH[region]:
                    flag = "FLAG"; n_flag += 1
                    flagged.append((title, f"-> {label}", gap_in, s_in, region))
                if prev is not None and s_in > CAP_S:
                    flag = "CAP"; n_cap += 1
                if prev is not None:
                    all_gaps_s.append(s_in)
                w(f"| {i} | {label} | ({first[0]:.1f}, {first[1]:.1f}, {first[2]:.1f}) -> ({last[0]:.1f}, {last[1]:.1f}, {last[2]:.1f}) | "
                  f"{gap_in:.1f} | {plan_in:.1f} | {gap_in / sprint:.1f} | {gap_in / jog:.1f} | {region} | {flag} | "
                  f"{note}; {len(steps)} steps, {min(steps):.2f}-{max(steps):.2f} m (median {statistics.median(steps):.2f}), "
                  f"rise {min(rises):.2f}-{max(rises):.2f} m, {min(secs):.2f}-{max(secs):.2f} s per step at {'jog' if v == jog else 'sprint'} |\n")
                prev = last
                continue
            if isinstance(ref, str) and ref.startswith("drop:"):
                # A summit left by the fall. Not a cadence gap: the run to the lip is the only
                # thing the body does, then gravity does the rest. Reported as plan metres to
                # the lip plus the free-fall time under the shipped gravity, never flagged.
                lip = L.resolve("at:" + ref[5:])
                run_m = plan_dist(prev, lip)
                h = prev[1] - lip[1]
                t_fall = math.sqrt(2.0 * h / fall_g)
                w(f"| {i} | {label} | ({lip[0]:.1f}, {lip[1]:.1f}, {lip[2]:.1f}) | {h:.1f} down | {run_m:.1f} | "
                  f"{run_m / sprint:.1f} + {t_fall:.1f} fall | {run_m / jog:.1f} + {t_fall:.1f} fall | drop | | "
                  f"{note}; fall time at Gravity x FallGravityMultiplier = {fall_g:.0f} m/s^2, no terminal velocity |\n")
                prev = lip
                continue
            try:
                p = L.resolve(ref)
            except KeyError as e:
                problems.append(f"{title}: cannot resolve {e}")
                continue
            if prev is None or region is None:
                w(f"| {i} | {label} | ({p[0]:.1f}, {p[1]:.1f}, {p[2]:.1f}) | -- | -- | -- | -- | -- | | {note} |\n")
                prev = p
                continue
            gap = dist3(prev, p)
            plan = plan_dist(prev, p)
            v = region_speed(region)
            s = gap / v
            flag = ""
            if s > THRESH[region]:
                flag = "FLAG"; n_flag += 1
                flagged.append((title, f"-> {label}", gap, s, region))
            if s > CAP_S:
                flag = "CAP"; n_cap += 1
            all_gaps_s.append(s)
            w(f"| {i} | {label} | ({p[0]:.1f}, {p[1]:.1f}, {p[2]:.1f}) | {gap:.1f} | {plan:.1f} | "
              f"{gap / sprint:.1f} | {gap / jog:.1f} | {region} | {flag} | {note} |\n")
            prev = p
        per_line_counts[f"{title} -- {tag}"] = (n_flag, n_cap)

    for title, spec in LINES.items():
        emit_way(title, spec["way"], "outbound")
        if spec["inbound"] == "reversed":
            # reverse the list; the region of a gap belongs to the gap, so shift regions with it
            way = spec["way"]
            rev = []
            for j in range(len(way) - 1, -1, -1):
                label, ref, _, note = way[j]
                region = way[j + 1][2] if j + 1 < len(way) else None
                if isinstance(ref, tuple):
                    ref = (ref[0], ref[1], list(reversed(ref[2])))
                rev.append((label, ref, region, note))
            emit_way(title, rev, "inbound (reversed; same gaps, read the other way)")
        else:
            emit_way(title, spec["inbound_way"], "inbound (a different line)")

    if check_only:
        return "\n".join(problems)

    w("\n### Summary\n\n")
    w(f"Speeds: jog {jog:.2f} m/s, sprint {sprint:.2f} m/s (MotorTuning.Default MoveSpeed x SprintMultiplier). ")
    w(f"Thresholds: fast <= {THRESH['fast']:.0f} s at sprint, approach <= {THRESH['approach']:.0f} s at sprint, "
      f"climb <= {THRESH['climb']:.0f} s at jog, cap {CAP_S:.0f} s.\n\n")
    w("| line | flagged gaps | over the 8 s cap |\n|---|---|---|\n")
    for k, (nf, nc) in per_line_counts.items():
        w(f"| {k} | {nf} | {nc} |\n")
    total_flag = sum(nf for nf, _ in per_line_counts.values())
    total_cap = sum(nc for _, nc in per_line_counts.values())
    w(f"\n{len(all_gaps_s)} gaps measured; {total_flag} flagged; {total_cap} over the cap; "
      f"median gap {statistics.median(all_gaps_s):.1f} s at the region's speed.\n\n")
    w("Worst gaps, at the region's own speed:\n\n| s | m | region | line | gap |\n|---|---|---|---|---|\n")
    for title, label, m, s, region in sorted(flagged, key=lambda t: -t[3])[:12]:
        w(f"| {s:.1f} | {m:.1f} | {region} | {title} | {label} |\n")
    if problems:
        w("\nUNRESOLVED WAYPOINTS:\n" + "\n".join(problems) + "\n")
    return out.getvalue()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    L = Level()
    jog, sprint, fall_g = speeds()
    text = run(L, jog, sprint, fall_g, args.check)
    if args.check:
        if text:
            print(text)
            sys.exit(1)
        print("[ld3-cadence] every waypoint resolves")
        return
    sys.stdout.write(text)


if __name__ == "__main__":
    main()
