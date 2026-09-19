#!/usr/bin/env python3
"""LD-3 -- a minimal reader for the bubbletest section .tscn files, world-space colliders out.

Shared by tools/dev/ld3_weenie_chain.py (the 6 x 6 summit inter-visibility matrix) and
tools/dev/ld3_cadence_dump.py (every standable surface, world space, for the cadence audit).
Stdlib only, no engine, deterministic: the same files in produce the same numbers out.

WHAT IT READS. `[sub_resource]` blocks for BoxShape3D (size), CylinderShape3D (height, radius)
and ConvexPolygonShape3D (points); `[node]` blocks with `position`, `rotation` (Godot's YXZ Euler
order), `transform` (twelve floats) and `shape = SubResource(...)`; `instance=ExtResource(...)`
rows for the Bubble.tscn instances. The section anchors come from BubbleTest.tscn's own instance
positions, not from a retyped table, so a moved anchor moves everything here with it.

THE TWELVE FLOATS ARE BASIS ROWS. `.claude/rules/godot-scenes.md` says so and TANGLE-1 measured
it: reading them as columns put a tread top 5 cm off in the unsafe direction. World = R . local +
origin with R's rows being floats [0..3), [3..6), [6..9). The inverse used for ray tests is a real
3 x 3 inverse rather than a transpose, because BT-4's picnic sets carry a scaled basis.

WHAT IT DOES NOT READ, stated so a green run is not read for more than it holds:
  * GreenHills' terrain collider is an external binary (`terrain_shape.res`) -- not parsed. The
    heightfield is 0..5.5 m and lies entirely west of x = -50; no line in the weenie matrix crosses
    it, and the cadence dump reports the stones, the postpile and the terrain-borne bubbles from
    the scene file. Anything standing ON terrain has its Y from the node, not from the ground.
  * ConvexPolygonShape3D hulls become their world AABB. Conservative for occlusion (a hull's box
    is at least the hull) and every one in the level is a 1-3 m boulder or a postpile column,
    none of which lies on a summit-to-summit line.
  * Runtime-built objects (the TVs themselves, the lever, the counter board) are not in any
    section file; tools that need them read BubbleTestLayout.cs by regex.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SECTION_DIR = ROOT / "scenes/game/world/bubbletest/sections"
WORLD_SCENE = ROOT / "scenes/game/world/bubbletest/BubbleTest.tscn"
LAYOUT_CS = ROOT / "scripts/game/world/bubbletest/BubbleTestLayout.cs"

Vec = tuple[float, float, float]

_HEADER = re.compile(r'^\[(\w+)(.*)\]\s*$')
_ATTR = re.compile(r'(\w+)=("([^"]*)"|[^\s\]]+)')
_VEC3 = re.compile(r'Vector3\(\s*([^,]+),\s*([^,]+),\s*([^)]+)\)')
_T3D = re.compile(r'Transform3D\(([^)]*)\)')
_PVA = re.compile(r'PackedVector3Array\(([^)]*)\)')
_SUB = re.compile(r'SubResource\("([^"]+)"\)')
_EXT = re.compile(r'ExtResource\("([^"]+)"\)')


def _f(s: str) -> float:
    return float(s.strip())


# --- linear algebra, 3 x 3 rows ------------------------------------------------------------

Mat = tuple[Vec, Vec, Vec]
IDENT: Mat = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))


def mat_mul(a: Mat, b: Mat) -> Mat:
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))  # type: ignore


def mat_vec(m: Mat, v: Vec) -> Vec:
    return tuple(sum(m[i][k] * v[k] for k in range(3)) for i in range(3))  # type: ignore


def vec_add(a: Vec, b: Vec) -> Vec:
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def vec_sub(a: Vec, b: Vec) -> Vec:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def vec_len(a: Vec) -> float:
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def mat_inv(m: Mat) -> Mat:
    (a, b, c), (d, e, f), (g, h, i) = m
    det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    if abs(det) < 1e-12:
        raise ValueError("singular basis")
    inv = (
        ((e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det),
        ((f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det),
        ((d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det),
    )
    return inv


def euler_yxz(rx: float, ry: float, rz: float) -> Mat:
    """Godot's default Euler order: the basis is Y(ry) * X(rx) * Z(rz)."""
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)
    mx: Mat = ((1, 0, 0), (0, cx, -sx), (0, sx, cx))
    my: Mat = ((cy, 0, sy), (0, 1, 0), (-sy, 0, cy))
    mz: Mat = ((cz, -sz, 0), (sz, cz, 0), (0, 0, 1))
    return mat_mul(my, mat_mul(mx, mz))


@dataclass
class Xform:
    basis: Mat = IDENT
    origin: Vec = (0.0, 0.0, 0.0)

    def apply(self, v: Vec) -> Vec:
        return vec_add(mat_vec(self.basis, v), self.origin)

    def then(self, child: "Xform") -> "Xform":
        """self (parent) composed with child: world = self(child(local))."""
        return Xform(mat_mul(self.basis, child.basis), self.apply(child.origin))


# --- the scene model --------------------------------------------------------------------------

@dataclass
class Shape:
    kind: str                       # box | cylinder | convex
    size: Vec = (0.0, 0.0, 0.0)     # box: full extents; cylinder: (2r, h, 2r)
    points: list[Vec] = field(default_factory=list)


@dataclass
class Node:
    name: str
    type: str
    parent: str                     # "" for the root, "." for a root child, else a path
    path: str                       # full path from the section root, "" for the root
    local: Xform
    shape_id: str = ""
    instance: str = ""              # ext_resource id when the node is an instanced scene
    groups: list[str] = field(default_factory=list)
    world: Xform = field(default_factory=Xform)


@dataclass
class Collider:
    section: str
    path: str                       # node path of the CollisionShape3D's owning body
    shape: Shape
    world: Xform                    # of the shape node itself
    corners: list[Vec]              # 8 world-space corners of the box (or the hull's AABB)

    @property
    def top(self) -> float:
        return max(c[1] for c in self.corners)

    @property
    def bottom(self) -> float:
        return min(c[1] for c in self.corners)

    @property
    def centre(self) -> Vec:
        return self.world.origin

    def plan_aabb(self) -> tuple[float, float, float, float]:
        xs = [c[0] for c in self.corners]
        zs = [c[2] for c in self.corners]
        return (min(xs), max(xs), min(zs), max(zs))

    def ray_hit(self, o: Vec, d: Vec) -> float | None:
        """Parametric t in (0, 1) where the segment o + t d enters this collider, else None."""
        if self.shape.kind == "convex":
            return _ray_aabb(o, d, self.corners)
        inv = mat_inv(self.world.basis)
        lo = mat_vec(inv, vec_sub(o, self.world.origin))
        ld = mat_vec(inv, d)
        half = (self.shape.size[0] * 0.5, self.shape.size[1] * 0.5, self.shape.size[2] * 0.5)
        return _ray_slab(lo, ld, (-half[0], -half[1], -half[2]), half)


def _ray_slab(o: Vec, d: Vec, lo: Vec, hi: Vec) -> float | None:
    tmin, tmax = 0.0, 1.0
    for k in range(3):
        if abs(d[k]) < 1e-12:
            if o[k] < lo[k] or o[k] > hi[k]:
                return None
            continue
        t1 = (lo[k] - o[k]) / d[k]
        t2 = (hi[k] - o[k]) / d[k]
        if t1 > t2:
            t1, t2 = t2, t1
        tmin = max(tmin, t1)
        tmax = min(tmax, t2)
        if tmin > tmax:
            return None
    # A hit at t <= 1e-6 is the origin sitting on (or inside) the box -- the vantage standing on
    # its own pad -- and is not an occlusion of the line.
    if tmax <= 1e-6 or tmin <= 1e-6:
        return None
    return tmin


def _ray_aabb(o: Vec, d: Vec, corners: list[Vec]) -> float | None:
    lo = (min(c[0] for c in corners), min(c[1] for c in corners), min(c[2] for c in corners))
    hi = (max(c[0] for c in corners), max(c[1] for c in corners), max(c[2] for c in corners))
    return _ray_slab(o, d, lo, hi)


@dataclass
class Section:
    name: str
    anchor: Vec
    nodes: dict[str, Node]
    shapes: dict[str, Shape]
    ext: dict[str, str]             # ext_resource id -> path
    colliders: list[Collider]
    bubbles: list[Node]


# --- parsing ----------------------------------------------------------------------------------

def _parse_blocks(text: str) -> list[tuple[str, dict[str, str], list[str]]]:
    blocks: list[tuple[str, dict[str, str], list[str]]] = []
    cur: tuple[str, dict[str, str], list[str]] | None = None
    for raw in text.splitlines():
        line = raw.rstrip("\n")
        m = _HEADER.match(line)
        if m:
            attrs = {k: (v3 if v.startswith('"') else v) for k, v, v3 in _ATTR.findall(m.group(2))}
            cur = (m.group(1), attrs, [])
            blocks.append(cur)
            continue
        if cur is not None and line.strip():
            cur[2].append(line)
    return blocks


def _shape_from(kind: str, body: list[str]) -> Shape | None:
    props = {}
    for line in body:
        if "=" in line:
            k, _, v = line.partition("=")
            props[k.strip()] = v.strip()
    if kind == "BoxShape3D":
        m = _VEC3.search(props.get("size", "Vector3(1, 1, 1)"))
        return Shape("box", (_f(m.group(1)), _f(m.group(2)), _f(m.group(3))))
    if kind == "CylinderShape3D":
        h = _f(props.get("height", "2"))
        r = _f(props.get("radius", "0.5"))
        return Shape("cylinder", (2 * r, h, 2 * r))
    if kind == "ConvexPolygonShape3D":
        m = _PVA.search(props.get("points", ""))
        if not m:
            return Shape("convex", points=[])
        nums = [_f(x) for x in m.group(1).split(",") if x.strip()]
        pts = [(nums[i], nums[i + 1], nums[i + 2]) for i in range(0, len(nums) - 2, 3)]
        return Shape("convex", points=pts)
    return None


def _xform_from(body: list[str]) -> Xform:
    pos: Vec = (0.0, 0.0, 0.0)
    rot: Vec = (0.0, 0.0, 0.0)
    basis: Mat | None = None
    for line in body:
        if line.startswith("transform = "):
            m = _T3D.search(line)
            f = [_f(x) for x in m.group(1).split(",")]
            basis = ((f[0], f[1], f[2]), (f[3], f[4], f[5]), (f[6], f[7], f[8]))
            pos = (f[9], f[10], f[11])
        elif line.startswith("position = "):
            m = _VEC3.search(line)
            pos = (_f(m.group(1)), _f(m.group(2)), _f(m.group(3)))
        elif line.startswith("rotation = "):
            m = _VEC3.search(line)
            rot = (_f(m.group(1)), _f(m.group(2)), _f(m.group(3)))
    if basis is None:
        basis = euler_yxz(*rot) if any(rot) else IDENT
    return Xform(basis, pos)


def load_section(name: str, anchor: Vec) -> Section:
    text = (SECTION_DIR / f"{name}.tscn").read_text(encoding="utf-8")
    shapes: dict[str, Shape] = {}
    ext: dict[str, str] = {}
    nodes: dict[str, Node] = {}
    order: list[Node] = []
    for kind, attrs, body in _parse_blocks(text):
        if kind == "ext_resource":
            ext[attrs.get("id", "")] = attrs.get("path", "")
        elif kind == "sub_resource":
            s = _shape_from(attrs.get("type", ""), body)
            if s is not None:
                shapes[attrs["id"]] = s
        elif kind == "node":
            nm = attrs["name"]
            parent = attrs.get("parent", "")
            path = "" if parent == "" else (nm if parent == "." else f"{parent}/{nm}")
            shape_id = ""
            for line in body:
                if line.startswith("shape = "):
                    m = _SUB.search(line)
                    shape_id = m.group(1) if m else ""
            inst = ""
            m = _EXT.search(attrs.get("instance", ""))
            if m:
                inst = m.group(1)
            groups = re.findall(r'"([^"]+)"', attrs.get("groups", ""))
            n = Node(nm, attrs.get("type", ""), parent, path, _xform_from(body), shape_id, inst, groups)
            nodes[path] = n
            order.append(n)
    root_x = Xform(IDENT, anchor)
    for n in order:
        if n.parent == "":
            n.world = root_x.then(n.local)
        else:
            p = nodes[""] if n.parent == "." else nodes[n.parent]
            n.world = p.world.then(n.local)
    colliders: list[Collider] = []
    for n in order:
        if n.type != "CollisionShape3D" or not n.shape_id:
            continue
        s = shapes.get(n.shape_id)
        if s is None:
            continue
        body_path = n.parent if n.parent != "." else ""
        if s.kind == "convex":
            pts = [n.world.apply(p) for p in s.points]
            corners = _aabb_corners(pts) if pts else []
        else:
            corners = _box_corners(n.world, s.size)
        if corners:
            colliders.append(Collider(name, body_path, s, n.world, corners))
    bubbles = [n for n in order if n.instance and "bubble" in ext.get(n.instance, "").lower()]
    return Section(name, anchor, nodes, shapes, ext, colliders, bubbles)


def _box_corners(x: Xform, size: Vec) -> list[Vec]:
    hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    out = []
    for sx in (-hx, hx):
        for sy in (-hy, hy):
            for sz in (-hz, hz):
                out.append(x.apply((sx, sy, sz)))
    return out


def _aabb_corners(pts: list[Vec]) -> list[Vec]:
    lo = (min(p[0] for p in pts), min(p[1] for p in pts), min(p[2] for p in pts))
    hi = (max(p[0] for p in pts), max(p[1] for p in pts), max(p[2] for p in pts))
    return [(x, y, z) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]


def section_anchors() -> dict[str, Vec]:
    """The anchors as BubbleTest.tscn actually instances them."""
    text = WORLD_SCENE.read_text(encoding="utf-8")
    out: dict[str, Vec] = {}
    for kind, attrs, body in _parse_blocks(text):
        if kind != "node" or "instance" not in attrs or attrs.get("parent") != ".":
            continue
        x = _xform_from(body)
        out[attrs["name"]] = x.origin
    return out


def world_props() -> list[tuple[str, Vec]]:
    """The golden cubes: `Props/GoldCube_*` nodes in BubbleTest.tscn, world space."""
    text = WORLD_SCENE.read_text(encoding="utf-8")
    out = []
    for kind, attrs, body in _parse_blocks(text):
        if kind == "node" and attrs.get("parent") == "Props":
            out.append((attrs["name"], _xform_from(body).origin))
    return out


def load_level(names: tuple[str, ...] = ("Hub", "RedCairns", "BluePrecision", "CyanRun", "GreenHills", "Tangle")) -> dict[str, Section]:
    anchors = section_anchors()
    return {n: load_section(n, anchors.get(n, (0.0, 0.0, 0.0))) for n in names}


# --- BubbleTestLayout.cs, by regex (the same idiom tools/dev/motor_arc.gd uses) ------------

_TVROUTE = re.compile(
    r'new\("(\w+)",\s*new Vector3\(([-\d.]+)f?,\s*([-\d.]+)f?,\s*([-\d.]+)f?\)')
_SPAWN_CENTRE = re.compile(r'SpawnRingCentre = new\(([-\d.]+)f?,\s*([-\d.]+)f?,\s*([-\d.]+)f?\)')
_PEDESTAL = re.compile(r'PedestalPos = new\(([-\d.]+)f?,\s*([-\d.]+)f?,\s*([-\d.]+)f?\)')
_RETURN = re.compile(r'RoomReturnOffset = new\(([-\d.]+)f?,\s*([-\d.]+)f?,\s*([-\d.]+)f?\)')


def layout_constants() -> dict:
    text = LAYOUT_CS.read_text(encoding="utf-8")
    tvs = [(m.group(1), (float(m.group(2)), float(m.group(3)), float(m.group(4))))
           for m in _TVROUTE.finditer(text)]
    sc = _SPAWN_CENTRE.search(text)
    pd = _PEDESTAL.search(text)
    ro = _RETURN.search(text)
    return {
        "tvs": tvs,
        "spawn_ring_centre": tuple(float(sc.group(i)) for i in (1, 2, 3)),
        "pedestal": tuple(float(pd.group(i)) for i in (1, 2, 3)),
        "return_offset": tuple(float(ro.group(i)) for i in (1, 2, 3)),
    }
