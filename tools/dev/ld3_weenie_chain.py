#!/usr/bin/env python3
"""LD-3 -- is there a chain, or five islands? The summit inter-visibility matrix, computed.

Research A4 (docs/research/2026-09-02-watis-...-RESEARCH-RESULT.md): a destination is desirable
from a distance when it is visible, legibly climbable, paid, has one guaranteed line, and is
CHAINED -- from its top the next weenie is visible. This prints the 6 x 6 matrix that says which
of those links exist in the shipped level, from geometry alone.

THE SIX POINTS, both as vantages and as targets: the five televisions of BubbleTestLayout.TvRoutes
-- HubTv on the plaza, RedTv on the cairn perch, HiddenTv on the spire's spur pad, BlueTv on the
tower perch, TangleTv on the summit cap -- and the hub spawn ring. Body 1.20 m, eye 1.10 m.

TWO STANCES PER VANTAGE, because the first one alone lies about looking DOWN:
  * AT THE RETURN POINT -- the eye 1.10 m over where the level itself puts a player on that
    summit: the television's entrance position plus BubbleTestLayout.RoomReturnOffset in plan
    (0.7 m east, 1.85 m south of the screen). Looking down from a 1.1 m eye across a 5-8 m pad,
    the pad's own lip hides anything under ~15-30 degrees of depression, so a "blocked" here can
    be the pad you are standing on. (The first draft of this tool put the eye ON the entrance
    position; that is 5 cm above the cabinet's top face and the captures came back brown. A
    player cannot stand there either -- the cabinet is solid.)
  * BEST ON THE PAD -- the same eye moved to whichever of ten standing spots on that pad (the
    return point, the pad's centre, and eight points 0.4 m inside the pad's edge) clears the most
    rays, skipping any spot that is inside a block. For the spawn ring the spots are the ring's
    centre and its six markers, and nothing else. This is the honest "can you see it from up
    there".

FIVE TARGET RAYS PER CELL, to a silhouette rather than a point: the target's eye point; 0.6 m
above and below it (a television is ~1.5 m of object, a player 1.2 m); and 1.5 m either side,
PERPENDICULAR to the line of sight in plan -- never along it, or the far pad's own lip would be
counted as an occluder of the thing standing on it. The target's own cabinet is not an occluder
of itself (the 0.5 m sample sits inside it), and NEITHER IS THE PAD IT STANDS ON (added by the
LD-3 correction, 2026-09-02): the first draft excluded only the cabinet, and a summit's own
platform -- with the eye only EYE_M above its top -- registered a grazing hit on every ray to a
target sitting directly over it, a false BLOCKED the docs/qa/LD-3/ captures caught in 4 of 30
cells and partially caused in a 5th. See docs/levels/2026-09-02-LD-3-weenie-chain.md, "the pad
self-occlusion bug", for the measurement. A cell reports clear/5; >= 1 is a link.

WHAT OCCLUDES: every collider tools/dev/ld3_tscn.py reads out of the six section files, at the
anchors BubbleTest.tscn instances them -- 529 boxes, cylinders and boulder hulls (as AABBs) --
plus the five television CABINETS, which are runtime-built (TvPortal.cs: a solid 1.0 x 0.85 x
0.55 box at (0, 0.62, -0.1) from the entrance, the set facing south) and are added here as boxes
so a stance cannot look through the set it stands beside. Not read: the sets' rabbit-ear
antennae (6 mm rods), GreenHills' terrain (no line here crosses it; it lies west of x = -50 and
every point below is at x >= -22), and the counter board. The captures under docs/qa/LD-3/ are
the other half of the matrix and the document compares the two cell by cell.

DETERMINISTIC. No randomness, no clock, no engine. Two runs print the same bytes; the document
quotes the SHA-256 of two runs. `--cams` prints the capture-camera list the PowerShell driver
reads -- the BEST stance's eye looking at the target's eye -- so the seen half of the matrix
cannot drift from the computed half.

    python tools/dev/ld3_weenie_chain.py            # both matrices + the link table, to stdout
    python tools/dev/ld3_weenie_chain.py --rays     # ... plus every blocked ray's first occluder
    python tools/dev/ld3_weenie_chain.py --cams     # tag,x,y,z,tx,ty,tz per ordered pair
"""

from __future__ import annotations

import argparse
import hashlib
import io
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ld3_tscn as T  # noqa: E402

EYE_M = 1.10
BODY_RADIUS_M = 0.40               # keeps a "pad edge" stance on the pad
SIDE_M = 1.5                       # lateral silhouette samples
VERT_M = 0.6                       # vertical silhouette samples
SPAWN_RING_RADIUS_M = 6.0          # BubbleTestLayout.SpawnRingRadius
SPAWN_COUNT = 6                    # BubbleTestLayout.SpawnCount

# Short names in chain order: the plaza pair, then the four summits by height.
ORDER = ["spawn", "HubTv", "RedTv", "HiddenTv", "BlueTv", "TangleTv"]
SHORT = {"spawn": "spawn", "HubTv": "hub", "RedTv": "red", "HiddenTv": "spur",
         "BlueTv": "blue", "TangleTv": "tangle"}
# The pad each television stands on: (section, collider path). The hub television stands on the
# plaza, which is treated as a 5 m pad around the stand.
PAD_OF = {"RedTv": ("RedCairns", "TvPerch"), "HiddenTv": ("Tangle", "Tower/SpurPad"),
          "BlueTv": ("BluePrecision", "TvPerch"), "TangleTv": ("Tangle", "Tower/Summit")}
PLAZA_PAD_HALF_M = 2.5
# TvPortal.cs AddBox("Cabinet", size (1.0, 0.85, 0.55), pos (0, 0.62, 0.1)) under a node yawed 180
# degrees, so local +z is world -z: the cabinet's centre sits 0.1 m NORTH of the entrance.
CABINET_SIZE = (1.0, 0.85, 0.55)
CABINET_OFFSET = (0.0, 0.62, -0.1)


def points() -> list[tuple[str, T.Vec]]:
    L = T.layout_constants()
    pts: dict[str, T.Vec] = {"spawn": L["spawn_ring_centre"]}
    for nm, p in L["tvs"]:
        pts[nm] = p
    missing = [k for k in ORDER if k not in pts]
    if missing:
        sys.exit(f"FAIL: BubbleTestLayout.cs did not yield {missing}")
    return [(k, pts[k]) for k in ORDER]


def return_point(key: str, stand: T.Vec, ret: T.Vec) -> T.Vec:
    """Where the level puts a player on this summit: the entrance plus RoomReturnOffset in plan.
    The spawn ring's own point is its centre."""
    if key == "spawn":
        return stand
    return (stand[0] + ret[0], stand[1], stand[2] + ret[2])


def stances(level: dict[str, T.Section], key: str, stand: T.Vec, ret: T.Vec) -> list[tuple[str, T.Vec]]:
    """Candidate standing spots (ground level, before the eye lift), named, in a fixed order."""
    out: list[tuple[str, T.Vec]] = [("return", return_point(key, stand, ret))]
    if key == "spawn":
        for i in range(SPAWN_COUNT):
            a = math.tau * i / SPAWN_COUNT
            out.append((f"Spawn{i}", (stand[0] + math.sin(a) * SPAWN_RING_RADIUS_M, stand[1],
                                      stand[2] - math.cos(a) * SPAWN_RING_RADIUS_M)))
        return out
    if key in PAD_OF:
        sec, path = PAD_OF[key]
        pad = next(c for c in level[sec].colliders if c.path == path)
        cx, cz = pad.centre[0], pad.centre[2]
        half = min(pad.shape.size[0], pad.shape.size[2]) * 0.5 - BODY_RADIUS_M
        top = pad.top
    else:
        cx, cz, half, top = stand[0], stand[2], PLAZA_PAD_HALF_M, stand[1]
    out.append(("centre", (cx, top, cz)))
    for i in range(8):
        a = math.tau * i / 8
        out.append((f"edge{i}", (cx + math.cos(a) * half, top, cz + math.sin(a) * half)))
    return out


def inside_any(colliders: list[T.Collider], p: T.Vec) -> bool:
    """Is a point inside a collider? Used to reject a stance that is inside a block."""
    for c in colliders:
        if c.shape.kind == "convex":
            continue
        x0, x1, z0, z1 = c.plan_aabb()
        if not (x0 <= p[0] <= x1 and z0 <= p[2] <= z1 and c.bottom <= p[1] <= c.top):
            continue
        inv = T.mat_inv(c.world.basis)
        lo = T.mat_vec(inv, T.vec_sub(p, c.world.origin))
        h = c.shape.size
        if abs(lo[0]) <= h[0] * 0.5 and abs(lo[1]) <= h[1] * 0.5 and abs(lo[2]) <= h[2] * 0.5:
            return True
    return False


def first_hit(colliders: list[T.Collider], o: T.Vec, target: T.Vec,
              ignore: frozenset[str] = frozenset()) -> tuple[float, T.Collider] | None:
    d = T.vec_sub(target, o)
    best: tuple[float, T.Collider] | None = None
    for c in colliders:
        if f"{c.section}/{c.path}" in ignore:
            continue                # the target's own cabinet, and the pad it stands on,
                                     # cannot occlude the target (LD-3 correction: the pad was
                                     # missing here and produced a false BLOCKED in 5 of 30
                                     # cells -- see docs/levels/2026-09-02-LD-3-weenie-chain.md
                                     # "the pad self-occlusion bug")
        x0, x1, z0, z1 = c.plan_aabb()
        if max(o[0], target[0]) < x0 or min(o[0], target[0]) > x1:
            continue
        if max(o[2], target[2]) < z0 or min(o[2], target[2]) > z1:
            continue
        if max(o[1], target[1]) < c.bottom or min(o[1], target[1]) > c.top:
            continue
        t = c.ray_hit(o, d)
        if t is not None and (best is None or t < best[0]):
            best = (t, c)
    return best


def target_rays(eye_from: T.Vec, eye_to: T.Vec) -> list[T.Vec]:
    dx, dz = eye_to[0] - eye_from[0], eye_to[2] - eye_from[2]
    n = math.hypot(dx, dz) or 1.0
    px, pz = -dz / n, dx / n          # perpendicular in plan
    return [
        eye_to,
        (eye_to[0], eye_to[1] + VERT_M, eye_to[2]),
        (eye_to[0], eye_to[1] - VERT_M, eye_to[2]),
        (eye_to[0] + px * SIDE_M, eye_to[1], eye_to[2] + pz * SIDE_M),
        (eye_to[0] - px * SIDE_M, eye_to[1], eye_to[2] - pz * SIDE_M),
    ]


def clear_count(colliders, eye_from: T.Vec, eye_to: T.Vec,
                 ignore: frozenset[str] = frozenset()) -> tuple[int, list[str]]:
    clear = 0
    blockers = []
    for tgt in target_rays(eye_from, eye_to):
        h = first_hit(colliders, eye_from, tgt, ignore)
        if h is None:
            clear += 1
        else:
            blockers.append(f"{h[1].section}/{h[1].path}@{h[0] * T.vec_len(T.vec_sub(tgt, eye_from)):.1f}m")
    return clear, blockers


def cabinets(pts) -> list[T.Collider]:
    out = []
    for k, p in pts:
        if k == "spawn":
            continue
        centre = (p[0] + CABINET_OFFSET[0], p[1] + CABINET_OFFSET[1], p[2] + CABINET_OFFSET[2])
        x = T.Xform(T.IDENT, centre)
        out.append(T.Collider("TvPortal", f"{k}/Cabinet", T.Shape("box", CABINET_SIZE), x,
                              T._box_corners(x, CABINET_SIZE)))
    return out


def compute(level: dict[str, T.Section]):
    pts = points()
    colliders = [c for s in level.values() for c in s.colliders] + cabinets(pts)
    ret = T.layout_constants()["return_offset"]
    eye_at_stand = {k: (p[0], p[1] + EYE_M, p[2]) for k, p in pts}
    eye_at_return = {}
    for k, p in pts:
        r = return_point(k, p, ret)
        eye_at_return[k] = (r[0], r[1] + EYE_M, r[2])
    cells: dict[tuple[str, str], dict] = {}
    for a, pa in pts:
        cands = []
        for nm, sp in stances(level, a, pa, ret):
            eye = (sp[0], sp[1] + EYE_M, sp[2])
            if inside_any(colliders, (sp[0], sp[1] + 0.6, sp[2])):
                continue
            cands.append((nm, eye))
        for b, pb in pts:
            if a == b:
                continue
            eye_to = eye_at_stand[b]
            own = {f"TvPortal/{b}/Cabinet"}   # the target's own set cannot occlude itself
            if b in PAD_OF:                   # ...and neither can the pad it stands on: the
                psec, ppath = PAD_OF[b]       # eye sits only EYE_M above that pad's own top,
                own.add(f"{psec}/{ppath}")    # so an unexcluded pad reads as a grazing hit on
                                               # every ray to a target it is directly under
            strict, strict_bl = clear_count(colliders, eye_at_return[a], eye_to, own)
            best = (strict, "return", eye_at_return[a], strict_bl)
            for nm, eye in cands:
                n, bl = clear_count(colliders, eye, eye_to, own)
                if n > best[0]:
                    best = (n, nm, eye, bl)
            d = T.vec_sub(eye_to, eye_at_return[a])
            plan = math.hypot(d[0], d[2])
            cells[(a, b)] = {
                "strict": strict, "strict_blockers": strict_bl,
                "best": best[0], "best_stance": best[1], "best_eye": best[2], "best_blockers": best[3],
                "dist": T.vec_len(d), "plan": plan, "elev": math.degrees(math.atan2(d[1], plan)),
            }
    return pts, eye_at_stand, cells, len(colliders)


def _matrix(w, cells, key: str) -> None:
    w("from \\ to  " + "".join(f"{SHORT[b]:>9s}" for b in ORDER) + "\n")
    for a in ORDER:
        row = f"{SHORT[a]:10s} "
        for b in ORDER:
            row += f"{'--':>9s}" if a == b else f"{cells[(a, b)][key]}/5".rjust(9)
        w(row + "\n")


def render(pts, cells, ncoll: int, rays: bool) -> str:
    out = io.StringIO()
    w = out.write
    w("# LD-3 weenie chain -- summit inter-visibility, computed from the section files\n")
    w(f"# {ncoll} colliders; eye {EYE_M:.2f} m; five silhouette rays per cell (eye, +/-{VERT_M} m, "
      f"+/-{SIDE_M} m across the line); a cell is clear/5, >= 1 is a link\n")
    w("# the six points (world, the stand, before the eye lift):\n")
    for k, p in pts:
        w(f"#   {SHORT[k]:6s} ({p[0]:8.3f}, {p[1]:7.3f}, {p[2]:8.3f})\n")
    w("\n## A. at the return point -- the eye where the level puts a player on that summit\n")
    _matrix(w, cells, "strict")
    w("\n## B. best stance on the pad -- the eye at whichever standing spot on that pad clears most\n")
    _matrix(w, cells, "best")
    w("\n## link table\n")
    w("from   to     return  best  stance   dist_m  plan_m  elev_deg  verdict\n")
    for a in ORDER:
        for b in ORDER:
            if a == b:
                continue
            c = cells[(a, b)]
            n = c["best"]
            verdict = "BLOCKED" if n == 0 else ("MARGINAL" if n <= 2 else "VISIBLE")
            w(f"{SHORT[a]:6s} {SHORT[b]:6s} {c['strict']:>6d}  {n:>4d}  {c['best_stance']:8s} "
              f"{c['dist']:6.1f}  {c['plan']:6.1f}  {c['elev']:8.1f}  {verdict}\n")
    if rays:
        w("\n## first occluder per blocked ray, at the return point (section/body@metres along the ray)\n")
        for a in ORDER:
            for b in ORDER:
                if a != b:
                    for bl in cells[(a, b)]["strict_blockers"]:
                        w(f"{SHORT[a]:6s} {SHORT[b]:6s} {bl}\n")
        w("\n## first occluder per blocked ray, best stance\n")
        for a in ORDER:
            for b in ORDER:
                if a != b:
                    for bl in cells[(a, b)]["best_blockers"]:
                        w(f"{SHORT[a]:6s} {SHORT[b]:6s} {bl}\n")
    return out.getvalue()


def cams(pts, eye_at_stand, cells) -> str:
    """One capture per ordered pair: the best stance's eye, looking at the target's eye point.
    Named `from-<vantage>-to-<target>`; docs/qa/LD-3/ holds the frames."""
    lines = []
    for a in ORDER:
        for b in ORDER:
            if a == b:
                continue
            o = cells[(a, b)]["best_eye"]
            t = eye_at_stand[b]
            lines.append(f"from-{SHORT[a]}-to-{SHORT[b]},{o[0]:.3f},{o[1]:.3f},{o[2]:.3f},"
                         f"{t[0]:.3f},{t[1]:.3f},{t[2]:.3f}")
    return "\n".join(lines) + "\n"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--cams", action="store_true")
    ap.add_argument("--rays", action="store_true")
    args = ap.parse_args()
    level = T.load_level()
    pts, eye_at_stand, cells, ncoll = compute(level)
    if args.cams:
        sys.stdout.write(cams(pts, eye_at_stand, cells))
        return
    text = render(pts, cells, ncoll, args.rays)
    sys.stdout.write(text)
    sys.stdout.write(f"\n# sha256 of everything above: {hashlib.sha256(text.encode('utf-8')).hexdigest()}\n")


if __name__ == "__main__":
    main()
