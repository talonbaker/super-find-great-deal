#!/usr/bin/env python3
"""LD-3 -- every standable surface in the bubbletest, world space, one line each.

The desk half of the cadence audit (docs/levels/2026-09-02-LD-3-cadence-audit.md) is built from
this listing and nothing else: a feature's position in that document is a row here, and a row
here is a node in a section .tscn plus the anchor BubbleTest.tscn instances it at. Re-run it
after any geometry change and diff the output; the audit's tables go stale the moment this does.

    python tools/dev/ld3_cadence_dump.py               # everything
    python tools/dev/ld3_cadence_dump.py RedCairns     # one section
    python tools/dev/ld3_cadence_dump.py --min-top 0.3 # only surfaces a body has to act on

Columns: section, body path, kind, world centre (x y z), TOP (the highest world-space corner --
what a foot lands on), plan extent (x0..x1, z0..z1), local size. Bubbles, the golden cubes and
the layout's runtime-built props (TVs, spawn ring, pedestal) are listed after the colliders.

Stdlib only; reads the files, never the engine. See tools/dev/ld3_tscn.py for what it does not
read (GreenHills' terrain collider, chiefly).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ld3_tscn as T  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("sections", nargs="*")
    ap.add_argument("--min-top", type=float, default=-100.0,
                    help="skip colliders whose top is below this world y")
    args = ap.parse_args()

    level = T.load_level()
    names = args.sections or list(level.keys())
    print("# LD-3 cadence dump -- world space, metres. TOP = highest corner of the collider.")
    for name in names:
        sec = level[name]
        a = sec.anchor
        print(f"\n## {name}  anchor ({a[0]:.3f}, {a[1]:.3f}, {a[2]:.3f})  "
              f"colliders {len(sec.colliders)}  bubbles {len(sec.bubbles)}")
        print("path | kind | cx cy cz | TOP | x0..x1 | z0..z1 | size")
        for c in sec.colliders:
            if c.top < args.min_top:
                continue
            x0, x1, z0, z1 = c.plan_aabb()
            cx, cy, cz = c.centre
            s = c.shape.size
            print(f"{c.path} | {c.shape.kind} | {cx:.2f} {cy:.2f} {cz:.2f} | {c.top:.3f} | "
                  f"{x0:.1f}..{x1:.1f} | {z0:.1f}..{z1:.1f} | {s[0]:.2f}x{s[1]:.2f}x{s[2]:.2f}")
        for b in sec.bubbles:
            o = b.world.origin
            print(f"{b.path} | bubble | {o[0]:.2f} {o[1]:.2f} {o[2]:.2f}")

    if not args.sections:
        print("\n## World props (BubbleTest.tscn Props/*)")
        for nm, p in T.world_props():
            print(f"{nm} | {p[0]:.2f} {p[1]:.2f} {p[2]:.2f}")
        L = T.layout_constants()
        print("\n## BubbleTestLayout.cs (regex-read)")
        print(f"SpawnRingCentre | {L['spawn_ring_centre']}")
        print(f"PedestalPos | {L['pedestal']}")
        print(f"RoomReturnOffset | {L['return_offset']}")
        for nm, p in L["tvs"]:
            print(f"Tv {nm} | {p[0]:.3f} {p[1]:.3f} {p[2]:.3f}")


if __name__ == "__main__":
    main()
