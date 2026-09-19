# LD-6 — the kit's scene self-test. Every rule in docs/levels/KIT.md's validator column,
# asserted against the SHIPPED scene, measured through Godot's own transform code.
#
#   godot --headless --path . --script res://tools/dev/kit_selftest.gd
#   godot --headless --path . --script res://tools/dev/kit_selftest.gd -- --kit-scene res://<path>
#   exit 0 = every rule holds; exit 1 = the failing primitive and rule print.
#
# WHY THIS FILE EXISTS AND WHAT IT IS FOR
# ---------------------------------------
# `tools/dev/kit_course.py` validates the course before it writes a byte. That validation is
# necessary and it is not sufficient, and the reason is on the record: the first KitCourse.tscn
# shipped with all 22 of its rotated pieces mirrored — three Wedges descending, both Sags domed,
# three Tilts leaning the wrong way, the return ramp climbing away from the plateau — and the
# generator's own validator passed it, because the angle rules are sign-blind and every height
# claim was DECLARED by the same arithmetic that placed the piece. A checker that reconstructs
# its subject the way its subject was built agrees with it, wrong sign and all.
#
# The python side now reconstructs the geometry from `pos`/`rot`/`shape` instead of believing
# the declarations. But there is exactly one thing python cannot check about itself: whether its
# Euler-to-basis convention IS Godot's. If it were not, the reconstruction and the generator
# would still agree with each other and the engine would render something else.
#
# So the generator STAMPS what it concluded into the scene as root metadata, and this file
# re-derives the same quantities from the LOADED scene using Godot's own `Transform3D * Vector3`
# — never a hand-indexed basis, for the reason `tools/dev/tangle_stair_check.gd` records in
# `_world_aabb`: a hand-written extent sum silently indexed the transpose and reported a tread
# 5 cm high. Two authorships, one of them the engine's. Disagreement past a millimetre is a red.
#
# THE ARC IS DERIVED, NEVER TYPED. `tools/dev/motor_arc.gd` re-simulates MotorTuning.Default and
# carries its own positive control against MOVE-3e's four in-engine measurements. The stamped
# band edges that are motor numbers exactly (Kerb's ceiling is the tap apex; Edge's ceiling is
# the held apex; the horizontal Edge's ceiling is the held range) are required to match it, so a
# scene generated against a stale envelope is a red rather than a level nobody can jump.
#
# THE BODY RADIUS IS PARSED, NEVER TYPED, out of AvatarProportions.cs — the same read-out the
# generator makes, from the same two consts.
#
# POSITIVE CONTROL. `tests/Run-KitCourseTest.ps1` runs this file a second time against a scene
# the generator emits with one rule deliberately broken (`--emit-violation`), and REQUIRES a
# red. An absence check that has never fired positive is not known to work.
extends SceneTree

const DEFAULT_SCENE := "res://scenes/dev/KitCourse.tscn"
const MOTOR := preload("res://tools/dev/motor_arc.gd")
const CS_PROPORTIONS := "res://scripts/game/sandbox/AvatarProportions.cs"

## A millimetre. Every cross-check between the two authorships is held to it.
const EPS_M := 0.001
## Angles are compared with a tolerance far below the 5 deg margin the kit leaves itself.
const EPS_DEG := 0.01
## Frames to let the shipped body fall the 1.2 m it is authored above the spawn pad and settle.
## At 60 Hz that fall is roughly 30 ticks; 90 leaves three times the margin and costs 1.5 s.
const SETTLE_FRAMES := 90

var _fail := 0
var _frames := 0
var _done := false
var _root: Node3D
var _scene_path := DEFAULT_SCENE
var _meta := {}
var _kinds := {}
var _tops := {}          # node name -> Array of {y, cx, cz, hx, hz}
var _runs := {}          # node name -> Vector2 (x, z), the authored uphill
var _bodies := {}        # node name -> StaticBody3D
var _body_radius := 0.0
var _floor_max := 45.0
var _angle_margin := 5.0


func _initialize() -> void:
	for i in OS.get_cmdline_user_args().size():
		if OS.get_cmdline_user_args()[i] == "--kit-scene" and i + 1 < OS.get_cmdline_user_args().size():
			_scene_path = OS.get_cmdline_user_args()[i + 1]
	print("[kit-selftest] scene %s" % _scene_path)
	var packed: PackedScene = load(_scene_path)
	if packed == null:
		printerr("[kit-selftest] cannot load %s" % _scene_path)
		quit(1)
		return
	_root = packed.instantiate()
	root.add_child(_root)


func _physics_process(_delta: float) -> bool:
	_frames += 1
	if _frames < SETTLE_FRAMES:
		return false
	_run()
	if not _done:
		printerr("[kit-selftest] the run aborted before the last check -- see the SCRIPT ERROR above")
		_fail += 1
	if _fail > 0:
		print("[kit-selftest] FAIL (%d)" % _fail)
		quit(1)
	else:
		print("[kit-selftest] PASS")
		quit(0)
	return true


func _check(ok: bool, why: String) -> void:
	if ok:
		return
	printerr("[kit-selftest] %s" % why)
	_fail += 1


# =============================================================================================
# Measurement — all of it through Godot's own transform, none of it hand-indexed
# =============================================================================================

## The image of a LOCAL point in world space. `xf * v` is the engine's own operator; nothing
## here reaches into `basis.x/y/z`, which is where tangle_stair_check.gd's hand version went
## wrong by silently indexing the transpose.
func _at(xf: Transform3D, local: Vector3) -> Vector3:
	return xf * local


## The world unit normal of a box's local +Y face.
func _face_up(xf: Transform3D) -> Vector3:
	return (xf * Vector3(0, 1, 0) - xf * Vector3.ZERO).normalized()


## Degrees away from straight up. 0 = a floor, 90 = a wall.
func _angle_from_up(n: Vector3) -> float:
	return rad_to_deg(acos(clampf(n.y, -1.0, 1.0)))


## World height of a box's top-face PLANE over (x, z), or NAN if that face is a wall.
func _height_at(xf: Transform3D, size: Vector3, x: float, z: float) -> float:
	var n := _face_up(xf)
	if n.y <= 1e-9:
		return NAN
	var c := _at(xf, Vector3(0, size.y * 0.5, 0))
	return c.y + (n.x * (c.x - x) + n.z * (c.z - z)) / n.y


## The horizontal direction a face actually CLIMBS, or Vector2.ZERO if it is level.
func _uphill(xf: Transform3D) -> Vector2:
	var n := _face_up(xf)
	var h := Vector2(n.x, n.z)
	if h.length() < 1e-9:
		return Vector2.ZERO
	return -h.normalized()


func _world_aabb(xf: Transform3D, size: Vector3) -> AABB:
	return xf * AABB(-size * 0.5, size)


func _shape_of(body: StaticBody3D) -> Shape3D:
	var cs := body.get_node_or_null("Shape") as CollisionShape3D
	return null if cs == null else cs.shape


func _shape_xf(body: StaticBody3D) -> Transform3D:
	var cs := body.get_node_or_null("Shape") as CollisionShape3D
	return body.global_transform if cs == null else cs.global_transform


# =============================================================================================

func _run() -> void:
	_read_meta()
	if _fail > 0:
		_done = true
		return
	_check_motor()
	_check_body_radius()
	_collect_bodies()
	_check_colliders_and_labels()
	_cross_check_stamp()
	_check_forbidden_band()
	_check_blocks()
	_check_wedges()
	_check_tilts()
	_check_logs()
	_check_sags()
	_check_domes()
	_check_body_spawns()
	_done = true


func _read_meta() -> void:
	for k in ["kit_bands_up", "kit_bands_gap", "kit_tops", "kit_runs", "kit_kinds",
			"kit_body_radius", "kit_floor_max_angle", "kit_angle_margin", "kit_seed"]:
		if not _root.has_meta(k):
			_check(false, "the scene carries no `%s` metadata -- it was not written by "
				% k + "tools/dev/kit_course.py, and nothing here can be checked against it")
			return
		_meta[k] = _root.get_meta(k)
	_body_radius = float(_meta["kit_body_radius"])
	_floor_max = float(_meta["kit_floor_max_angle"])
	_angle_margin = float(_meta["kit_angle_margin"])
	for row in _meta["kit_kinds"]:
		var f: PackedStringArray = String(row).split("|")
		_kinds[f[0]] = f[1]
	for row in _meta["kit_tops"]:
		var f: PackedStringArray = String(row).split("|")
		if not _tops.has(f[0]):
			_tops[f[0]] = []
		_tops[f[0]].append({"y": float(f[1]), "cx": float(f[2]), "cz": float(f[3]),
			"hx": float(f[4]), "hz": float(f[5])})
	for row in _meta["kit_runs"]:
		var f: PackedStringArray = String(row).split("|")
		_runs[f[0]] = Vector2(float(f[1]), float(f[2]))
	print("[kit-selftest] seed %d, %d pieces stamped, %d tops, %d authored climbs"
		% [int(_meta["kit_seed"]), _kinds.size(), _meta["kit_tops"].size(), _runs.size()])


func _band(which: String, name: String) -> Vector2:
	for row in _meta[which]:
		var f: PackedStringArray = String(row).split("|")
		if f[0] == name:
			return Vector2(float(f[1]), INF if f[2] == "inf" else float(f[2]))
	return Vector2(NAN, NAN)


## The bands are FRACTIONS of the motor envelope, so most edges cannot be checked without
## duplicating the fractions here -- which would be the copy this whole exercise deletes. The
## three edges that ARE motor numbers exactly are checked instead, and they are enough: a scene
## generated against a stale envelope moves all of them at once.
func _check_motor() -> void:
	var why: String = MOTOR.verify()
	_check(why == "", why)
	var arc: Dictionary = MOTOR.arc()
	_check(not arc.is_empty(), "the arc could not be derived from MotorTuning.cs")
	if arc.is_empty():
		return
	print(MOTOR.describe(arc))
	var pairs := [
		["Kerb ceiling", _band("kit_bands_up", "Kerb").y, float(arc["TapApex"]), "the tap apex"],
		["Edge ceiling", _band("kit_bands_up", "Edge").y, float(arc["SprintApex"]), "the held apex"],
		["Technique floor", _band("kit_bands_up", "Technique").x, float(arc["SprintApex"]), "the held apex"],
		["gap Edge ceiling", _band("kit_bands_gap", "Edge").y, float(arc["SprintRange"]), "the held range"],
		["gap Technique floor", _band("kit_bands_gap", "Technique").x, float(arc["SprintRange"]), "the held range"],
	]
	for p in pairs:
		_check(absf(p[1] - p[2]) <= EPS_M,
			"the scene's %s is %.4f m; this engine derives %s as %.4f m from MotorTuning.Default. "
			% [p[0], p[1], p[3], p[2]]
			+ "The course was sized against a motor nobody has -- regenerate it.")


## The generator parses FallbackHalfWidthM x CapsuleRadiusFraction out of AvatarProportions.cs.
## So does this, separately, and the two must agree -- a body width is not a number to retype.
func _check_body_radius() -> void:
	var src := FileAccess.get_file_as_string(CS_PROPORTIONS)
	_check(src != "", "cannot read %s" % CS_PROPORTIONS)
	if src == "":
		return
	var vals := []
	for name in ["FallbackHalfWidthM", "CapsuleRadiusFraction"]:
		var re := RegEx.create_from_string("public const float " + name + r"\s*=\s*(-?[0-9.]+)f\s*;")
		var m := re.search(src)
		_check(m != null, "AvatarProportions.cs no longer declares `public const float %s`" % name)
		if m == null:
			return
		vals.append(float(m.get_string(1)))
	var r: float = vals[0] * vals[1]
	_check(absf(r - _body_radius) <= 1e-5,
		"the scene was sized against a %.5f m body radius; AvatarProportions.cs gives %.5f m"
		% [_body_radius, r])
	print("[kit-selftest] body radius %.4f m (diameter %.4f m), floor_max %.1f deg, margin %.1f deg"
		% [r, r * 2.0, _floor_max, _angle_margin])


func _collect_bodies() -> void:
	for body in _root.find_children("*", "StaticBody3D", true, false):
		_bodies[String(body.name)] = body
	_check(_bodies.size() == _kinds.size(),
		"the scene holds %d StaticBody3D pieces but stamps %d; the file and the generator "
		% [_bodies.size(), _kinds.size()] + "have parted company")
	# The kind a rule is chosen by must not come from the stamp alone, or a lying stamp dodges
	# its own rule. It is cross-checked against where the node actually SITS in the tree.
	var by_group := {"Blocks": "block", "Logs": "log", "Wedges": "wedge", "Domes": "dome",
		"Tilts": "tilt", "SagShallow": "sag", "SagDeep": "sag"}
	for name in _bodies:
		var parent := String(_bodies[name].get_parent().name)
		if by_group.has(parent):
			_check(_kinds.get(name, "") == by_group[parent],
				"%s sits under Kit/%s but is stamped as a %s" % [name, parent, _kinds.get(name, "?")])


## Talon's standing ruling: every touchable mesh carries its collider in the same file. This is
## an ABSENCE check, so the count it audited is printed -- an audit that found no meshes would
## otherwise pass in silence.
func _check_colliders_and_labels() -> void:
	var audited := 0
	for name in _bodies:
		var body: StaticBody3D = _bodies[name]
		_check(body.get_node_or_null("Mesh") is MeshInstance3D, "%s has no Mesh" % name)
		_check(body.get_node_or_null("Shape") is CollisionShape3D, "%s has no CollisionShape3D" % name)
		var cs := body.get_node_or_null("Shape") as CollisionShape3D
		_check(cs != null and cs.shape != null, "%s's CollisionShape3D carries no shape" % name)
		audited += 1
	var labels := _root.find_children("*", "Label3D", true, false)
	_check(labels.size() >= 6, "the course carries %d Label3D nodes; the six primitives must "
		% labels.size() + "each be callable by name for the A4-R2 test")
	for l in labels:
		var lab := l as Label3D
		# Talon's standing ruling: NO billboarded world-space UI. A label that turns to face the
		# player is a HUD element pretending to be a sign.
		_check(lab.billboard == BaseMaterial3D.BILLBOARD_DISABLED,
			"%s has billboard mode %d; world-space labels are never billboarded"
			% [String(lab.name), lab.billboard])
	print("[kit-selftest] collider audit %d/%d pieces, %d labels, none billboarded"
		% [audited, _bodies.size(), labels.size()])


## THE CROSS-AUTHORSHIP CHECK. Everything the python reconstruction concluded, re-measured here
## through Godot's transforms. This is what makes the generator's verdict worth anything.
func _cross_check_stamp() -> void:
	var worst_top := 0.0
	var worst_top_at := ""
	var checked := 0
	for name in _tops:
		if not _bodies.has(name):
			_check(false, "the stamp claims a standable top on %s, which is not in the scene" % name)
			continue
		var body: StaticBody3D = _bodies[name]
		var shape := _shape_of(body)
		var xf := _shape_xf(body)
		for t in _tops[name]:
			var got := NAN
			if shape is BoxShape3D:
				got = _height_at(xf, (shape as BoxShape3D).size, t["cx"], t["cz"])
			elif shape is CylinderShape3D:
				got = xf.origin.y + (shape as CylinderShape3D).radius
			elif shape is SphereShape3D:
				got = xf.origin.y + (shape as SphereShape3D).radius
			_check(not is_nan(got), "%s stamps a standable top but its shape presents no "
				% name + "upward face in the engine at all")
			if is_nan(got):
				continue
			var err: float = absf(got - float(t["y"]))
			if err > worst_top:
				worst_top = err
				worst_top_at = name
			_check(err <= EPS_M,
				"%s: the generator stamped its surface at y=%.4f over (%.3f, %.3f); the engine "
				% [name, float(t["y"]), float(t["cx"]), float(t["cz"])]
				+ "measures y=%.4f there -- %.1f mm apart. The two derivations disagree, and "
				% [got, err * 1000.0]
				+ "the engine's is the one that ships.")
			checked += 1
	# The sign, measured. This is the check the original defect walks straight into.
	var worst_dot := 1.0
	var worst_dot_at := ""
	for name in _runs:
		if not _bodies.has(name):
			_check(false, "the stamp claims an authored climb on %s, which is not in the scene" % name)
			continue
		var shape := _shape_of(_bodies[name])
		if not (shape is BoxShape3D):
			continue
		var up := _uphill(_shape_xf(_bodies[name]))
		_check(up != Vector2.ZERO, "%s is authored to climb but reconstructs LEVEL in the engine" % name)
		if up == Vector2.ZERO:
			continue
		var want: Vector2 = _runs[name]
		var d := up.dot(want)
		if d < worst_dot:
			worst_dot = d
			worst_dot_at = name
		_check(d >= 0.999,
			"%s is authored to climb along (%+.2f, %+.2f) but the shipped geometry climbs along "
			% [name, want.x, want.y]
			+ "(%+.4f, %+.4f) -- dot %+.4f. Same angle, same height, the opposite end: this is "
			% [up.x, up.y, d]
			+ "a mirrored rotation, and no angle rule can see it.")
	print("[kit-selftest] cross-check: %d tops within %.2f mm (worst %s); %d climbs, worst dot %+.4f (%s)"
		% [checked, worst_top * 1000.0, worst_top_at if worst_top_at != "" else "-",
		   _runs.size(), worst_dot, worst_dot_at if worst_dot_at != "" else "-"])


## THE INVARIANT THE KIT IS BUILT ON: nothing in the scene presents a body-wide upward face
## between (floor_max - margin) and (floor_max + margin). There is no surface a player can
## mistake for the other kind, because the mistakable kind does not exist.
func _check_forbidden_band() -> void:
	var lo := _floor_max - _angle_margin
	var hi := _floor_max + _angle_margin
	var diameter := _body_radius * 2.0
	var checked := 0
	var closest := 999.0
	var closest_at := ""
	for name in _bodies:
		var shape := _shape_of(_bodies[name])
		if not (shape is BoxShape3D):
			continue
		var size: Vector3 = (shape as BoxShape3D).size
		var xf := _shape_xf(_bodies[name])
		# The six faces, each as the image of a local unit normal, with the two in-plane
		# dimensions that decide whether a body could fit on it.
		var faces := [[Vector3(0, 1, 0), Vector2(size.x, size.z)],
			[Vector3(0, -1, 0), Vector2(size.x, size.z)],
			[Vector3(1, 0, 0), Vector2(size.y, size.z)],
			[Vector3(-1, 0, 0), Vector2(size.y, size.z)],
			[Vector3(0, 0, 1), Vector2(size.x, size.y)],
			[Vector3(0, 0, -1), Vector2(size.x, size.y)]]
		for f in faces:
			var n: Vector3 = (xf * f[0] - xf * Vector3.ZERO).normalized()
			if n.y <= 1e-6:
				continue
			var dims: Vector2 = f[1]
			if minf(dims.x, dims.y) < diameter:
				continue      # narrower than the body: it cannot hold one whatever its angle
			checked += 1
			var a := _angle_from_up(n)
			_check(a <= lo + EPS_DEG or a >= hi - EPS_DEG,
				"%s presents a %.3f m-wide upward face at %.2f deg, inside the forbidden band "
				% [name, minf(dims.x, dims.y), a]
				+ "(%.0f, %.0f) -- a surface that looks walkable and is not, or the reverse."
				% [lo, hi])
			var margin := minf(absf(a - lo), absf(a - hi))
			if margin < closest:
				closest = margin
				closest_at = "%s at %.2f deg" % [name, a]
	print("[kit-selftest] forbidden band (%.0f, %.0f) deg: %d body-wide upward faces, closest "
		% [lo, hi, checked] + "approach %.3f deg (%s)" % [closest, closest_at])


func _top_of(name: String) -> AABB:
	var body: StaticBody3D = _bodies[name]
	var shape := _shape_of(body)
	if shape is BoxShape3D:
		return _world_aabb(_shape_xf(body), (shape as BoxShape3D).size)
	if shape is CylinderShape3D:
		var c := shape as CylinderShape3D
		return _world_aabb(_shape_xf(body), Vector3(c.radius * 2, c.height, c.radius * 2))
	var s := shape as SphereShape3D
	return _world_aabb(_shape_xf(body), Vector3.ONE * s.radius * 2.0)


func _plan_gap(a: AABB, b: AABB) -> float:
	var dx := maxf(0.0, maxf(b.position.x - a.end.x, a.position.x - b.end.x))
	var dz := maxf(0.0, maxf(b.position.z - a.end.z, a.position.z - b.end.z))
	return sqrt(dx * dx + dz * dz)


## BLOCK — land, stand, climb. Its top is at a band height above the ground it stands on, and
## the one tagged Denial is separated from everything standable by more than a Technique gap.
func _check_blocks() -> void:
	var ground := _top_of("Ground").end.y
	var technique_up := _band("kit_bands_up", "Technique").y
	var denial_gap := _band("kit_bands_gap", "Denial").x
	var rows := ""
	for name in _kinds:
		if _kinds[name] != "block":
			continue
		var a := _top_of(name)
		var rise := a.end.y - ground
		var band := _band_of_rise(rise)
		var want := String(name).substr(6)
		_check(band == want, "%s is built %.3f m above the ground, which is the %s band, not %s"
			% [name, rise, band, want])
		rows += " %s=%.3f" % [want, rise]
		if want == "Denial":
			for other in _kinds:
				if other == name:
					continue
				var b := _top_of(other)
				var up := a.end.y - b.end.y
				if up <= 0.0 or up > technique_up:
					continue
				_check(_plan_gap(a, b) >= denial_gap,
					"%s sits %.3f m above %s (inside the Technique band) and only %.3f m from "
					% [name, up, other, _plan_gap(a, b)]
					+ "it in plan, under the %.3f m Denial gap. It teases." % denial_gap)
	print("[kit-selftest] blocks (rise above ground):%s" % rows)


func _band_of_rise(rise: float) -> String:
	for row in _meta["kit_bands_up"]:
		var f: PackedStringArray = String(row).split("|")
		var lo := float(f[1])
		if f[2] == "inf":
			if rise >= lo:
				return f[0]
		elif rise > lo and rise <= float(f[2]):
			return f[0]
	return "(between bands)"


## WEDGE — the only PLANAR slope a body may stand on, so it never reaches the floor limit.
func _check_wedges() -> void:
	var lo := _floor_max - _angle_margin
	var rows := ""
	for name in _kinds:
		if _kinds[name] != "wedge":
			continue
		var shape := _shape_of(_bodies[name])
		if not (shape is BoxShape3D):
			_check(false, "%s is not a box; a Wedge is a planar slope" % name)
			continue
		var a := _angle_from_up(_face_up(_shape_xf(_bodies[name])))
		_check(a <= lo + EPS_DEG, "%s slopes at %.3f deg, past the %.0f deg steepest legal floor"
			% [name, a, lo])
		rows += " %s=%.2f" % [name, a]
	print("[kit-selftest] wedges:%s (bar <= %.0f deg)" % [rows, lo])


## TILT — a wall that used to be a floor. Past the limit, thinner than the body so its own end
## face cannot become a ledge, and never a Hop above anything standable.
func _check_tilts() -> void:
	var hi := _floor_max + _angle_margin
	var hop := _band("kit_bands_up", "Hop").y
	var technique_gap := _band("kit_bands_gap", "Technique").y
	var diameter := _body_radius * 2.0
	var rows := ""
	for name in _kinds:
		if _kinds[name] != "tilt":
			continue
		var shape := _shape_of(_bodies[name]) as BoxShape3D
		_check(shape != null, "%s is not a box" % name)
		if shape == null:
			continue
		var xf := _shape_xf(_bodies[name])
		var a := _angle_from_up(_face_up(xf))
		_check(a >= hi - EPS_DEG, "%s slopes at %.3f deg, under the %.0f deg shallowest legal "
			% [name, a, hi] + "wall -- it is a floor pretending to be a wall")
		_check(minf(shape.size.y, shape.size.z) < diameter,
			"%s is %.3f m thick against a %.3f m body -- its end face becomes a ledge"
			% [name, minf(shape.size.y, shape.size.z), diameter])
		# The top edge, in world space, straight off the engine's transform.
		var edge := _at(xf, Vector3(shape.size.x * 0.5, shape.size.y * 0.5, 0.0))
		var box := _world_aabb(xf, shape.size)
		for other in _kinds:
			if other == name or not _tops.has(other):
				continue
			var b := _top_of(other)
			if _plan_gap(box, b) > technique_gap:
				continue
			for t in _tops[other]:
				_check(absf(edge.y - float(t["y"])) > hop,
					"%s's top edge is %.3f m from %s's standable top, inside the %.3f m Hop "
					% [name, absf(edge.y - float(t["y"])), other, hop] + "band. It reads as a step-up.")
		rows += " %s=%.2f(top %.2f m)" % [name, a, edge.y]
	print("[kit-selftest] tilts:%s (bar >= %.0f deg)" % [rows, hi])


## LOG — the meaning IS the collision shape. A box in its place is the Mirror's Edge failure.
func _check_logs() -> void:
	var diameter := _body_radius * 2.0
	var rows := ""
	for name in _kinds:
		if _kinds[name] != "log":
			continue
		var shape := _shape_of(_bodies[name]) as CylinderShape3D
		_check(shape != null, "%s's collider is not a CylinderShape3D; a Log's meaning is its "
			% name + "collision shape and a box in its place is the Mirror's Edge failure")
		if shape == null:
			continue
		var xf := _shape_xf(_bodies[name])
		var axis := (xf * Vector3(0, 1, 0) - xf * Vector3.ZERO).normalized()
		_check(absf(axis.y) <= 1e-4, "%s's axis is %+.4f of Y; a vertical cylinder is a pillar"
			% [name, axis.y])
		var strip := 2.0 * shape.radius * sin(deg_to_rad(_floor_max))
		_check(strip >= diameter,
			"%s's crown offers a %.3f m walkable strip, under the body's %.3f m diameter -- "
			% [name, strip, diameter] + "that is a Dome in cylinder form and the balance verb "
			+ "it advertises is a lie")
		rows += " %s=r%.2f/strip%.2f" % [name, shape.radius, strip]
	print("[kit-selftest] logs:%s (strip bar >= %.3f m)" % [rows, diameter])


## SAG — concave everywhere, standable everywhere, and CONTINUOUS: consecutive facets share
## their surface endpoint exactly, or the bowl is a staircase pretending to be one.
func _check_sags() -> void:
	var groups := {}
	for name in _kinds:
		if _kinds[name] == "sag":
			var g := String(_bodies[name].get_parent().name)
			if not groups.has(g):
				groups[g] = []
			groups[g].append(name)
	var lo := _floor_max - _angle_margin
	for g in groups:
		var names: Array = groups[g]
		names.sort_custom(func(a, b): return _bodies[a].global_position.x < _bodies[b].global_position.x)
		var prev_end := Vector3.INF
		var prev_slope := -999.0
		var worst_step := 0.0
		var deepest := 999.0
		for name in names:
			var shape := _shape_of(_bodies[name]) as BoxShape3D
			_check(shape != null, "%s is not a box" % name)
			if shape == null:
				continue
			var xf := _shape_xf(_bodies[name])
			var a := _angle_from_up(_face_up(xf))
			_check(a <= lo + EPS_DEG, "%s slopes at %.3f deg; every part of a Sag is standable, "
				% [name, a] + "that is what catching means")
			# The chord's two surface endpoints, straight off the engine's transform.
			var s := _at(xf, Vector3(-shape.size.x * 0.5, shape.size.y * 0.5, 0.0))
			var e := _at(xf, Vector3(shape.size.x * 0.5, shape.size.y * 0.5, 0.0))
			var slope := rad_to_deg(asin(clampf((e - s).normalized().y, -1.0, 1.0)))
			if prev_end != Vector3.INF:
				var step := prev_end.distance_to(s)
				worst_step = maxf(worst_step, step)
				_check(step <= EPS_M,
					"%s: a %.1f mm step at %s's upstream edge. A Sag's surface is continuous "
					% [g, step * 1000.0, name] + "or it is a staircase pretending to be a bowl.")
				_check(slope > prev_slope,
					"%s: %s turns the surface the wrong way (%+.3f deg after %+.3f deg). A Sag "
					% [g, name, slope, prev_slope] + "is concave everywhere -- a convex one is a "
					+ "Dome, and a Dome the size of a Sag is a hill.")
			prev_end = e
			prev_slope = slope
			deepest = minf(deepest, minf(s.y, e.y))
		print("[kit-selftest] sag %s: %d facets, worst step %.3f mm, floor %.3f m"
			% [g, names.size(), worst_step * 1000.0, deepest])


## DOME — convex, and SMALL by arithmetic: the walkable pole cap is narrower than the body, so
## standing on it is impossible by geometry rather than by discouragement.
func _check_domes() -> void:
	var rows := ""
	for name in _kinds:
		if _kinds[name] != "dome":
			continue
		var shape := _shape_of(_bodies[name]) as SphereShape3D
		_check(shape != null, "%s's collider is not a SphereShape3D; a Dome's meaning is its "
			% name + "convexity")
		if shape == null:
			continue
		var cap := shape.radius * sin(deg_to_rad(_floor_max))
		_check(cap < _body_radius,
			"%s has radius %.3f m, so its walkable pole cap is %.3f m against a %.3f m body "
			% [name, shape.radius, cap, _body_radius] + "radius -- a body fits on it. That is "
			+ "not a Dome, it is a hill, and hills are the terrain layer under the kit.")
		_check(not _tops.has(name), "%s stamps a standable top; a Dome sheds, always" % name)
		rows += " %s=r%.3f/cap%.3f" % [name, shape.radius, cap]
	print("[kit-selftest] domes:%s (cap bar < %.3f m)" % [rows, _body_radius])


## THE SCENE MUST BE PLAYABLE, not merely correct. KitCourse.tscn carries SandboxWorld.cs so the
## SHIPPED body spawns in it with no C# change, and this asserts that it did: the Player is in
## the tree, it has fallen the 1.2 m it is authored above the spawn pad, and it is standing on
## the course rather than lying on the catch floor 6 m below. A course nobody can stand up in is
## a diagram.
##
## NOT asserted here, and deliberately: that the body can be DRIVEN along the line. That needs a
## hand on the keys and it is the first line of the playtest protocol in the LD-6 report.
func _check_body_spawns() -> void:
	var catch_y := -999.0
	for row in _meta["kit_tops"]:
		var f: PackedStringArray = String(row).split("|")
		if f[0] == "CatchFloor":
			catch_y = float(f[1])
	var pad_y := 0.0
	for row in _meta["kit_tops"]:
		var f: PackedStringArray = String(row).split("|")
		if f[0] == "SpawnPad":
			pad_y = float(f[1])
	var spawn := _root.get_node_or_null("Spawn")
	_check(spawn is Marker3D, "the scene has no Spawn marker")
	var names := ["Player", "DummyA", "DummyB", "DummyC"]
	var rows := ""
	for n in names:
		var body := _root.get_node_or_null(n) as CharacterBody3D
		_check(body != null, "the scene has no %s; SandboxWorld.cs wires exactly these four by "
			% n + "name and would throw on the missing one")
		if body == null:
			continue
		var y := body.global_position.y
		_check(y > catch_y + 1.0,
			"%s settled at y=%.3f, on or near the catch floor at %.3f -- it fell off the course "
			% [n, y, catch_y] + "instead of standing on it")
		_check(y >= pad_y - 0.05,
			"%s settled at y=%.3f, below the spawn pad's %.3f m surface" % [n, y, pad_y])
		rows += " %s=%.3f" % [n, y]
	print("[kit-selftest] bodies settled (y after %d ticks):%s (pad %.3f m, catch %.3f m)"
		% [SETTLE_FRAMES, rows, pad_y, catch_y])
