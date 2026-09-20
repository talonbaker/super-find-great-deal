## MOVE-8 — the jump arc, for GDScript tools, derived from the SHIPPED C# rather than retyped.
##
## WHY THIS FILE EXISTS
## --------------------
## Four numbers — 1.534 m / 6.192 m / 0.467 m / 1.710 m — were measured in a headed engine at
## MOVE-3e and then TYPED into eight places, one of which was `BubbleTestLayout` and two of which
## are GDScript tools in this directory. Talon's 2026-08-28 tuning ruling moved the motor, and at
## that instant every one of those copies became wrong at once, in silence, under a level that had
## been sized against them.
##
## MOVE-8 replaced the C# copies with `MotorArc`, a derivation off `MotorTuning.Default`. That
## turned `BubbleTestLayout.SprintApex` and its three siblings from `public const float` into
## `static readonly` — which BROKE `bubbletest_bake_green.gd`, whose `_cs_const()` parses a const's
## literal straight out of the .cs file. There is no longer a literal to parse, and that is the
## point.
##
## So this file does for GDScript what `MotorArc` does for C#: it parses the handful of PRIMITIVE
## rows out of `MotorTuning.Default`'s initializer — which are still literals, and always will be,
## because they are what a person types — and re-runs the same tick-by-tick simulation over them.
## It is a second authorship of one small function, exactly as `bubbletest_bake_green.gd`'s
## `_depth_at()` is a second authorship of `WaterGeometry.DepthAt`, and for the same stated reason:
## a tool that parsed a derived number would be reading a number that is not written down anywhere,
## and a tool that retyped one would be the copy this whole exercise exists to delete.
##
## THE CHECK THAT KEEPS THE TWO AUTHORSHIPS HONEST
## -----------------------------------------------
## `verify()` re-derives the PRE-RULING arc from a hand-written copy of the old
## tuning and requires MOVE-3e's four measured literals back. If this GDScript ever stops agreeing
## with the measurement `MotorArc` also reproduces, the bake refuses rather than baking a level
## against a motor nobody has.
##
##   var arc := MotorArc.arc()          # {sprint_apex, sprint_range, tap_apex, tap_range, ...}
##
## Every distance in metres, every time in seconds, flat ground, MOVE-3d's "as played" reporting
## convention (the launch tick's free JumpVelocity*dt out of the apex, one tick out of the airtime)
## — the same convention the level constants are stated in.

extends RefCounted

const CS_MOTOR := "res://scripts/net/MotorTuning.cs"

## 60 Hz. AvatarMotor.TickDelta is a `const` and is not a knob.
const DT := 1.0 / 60.0

## The rows the arc actually reads. Parsed, never typed.
const ROWS := ["MoveSpeed", "SprintMultiplier", "Gravity", "FallGravityMultiplier",
		"ApexHangStrength", "ApexHangWindowMps", "JumpVelocity",
		"JumpReleaseGravityMultiplier", "AirJumpMode", "AirJumpCountMax",
		"AirJumpVelocityFraction"]


## Parses `<Name> = <number>f,` out of MotorTuning.Default's initializer ONLY. Bounded to that
## block on purpose: `MoveSpeed` also appears in derived properties and in doc comments, and a
## whole-file regex would happily match one of those instead.
static func _rows() -> Dictionary:
	var text := FileAccess.get_file_as_string(CS_MOTOR)
	if text == "":
		push_error("[motor-arc] cannot read %s" % CS_MOTOR)
		return {}
	var start := text.find("public static MotorTuning Default { get; } = new()")
	if start < 0:
		push_error("[motor-arc] %s no longer declares MotorTuning.Default's initializer" % CS_MOTOR)
		return {}
	var end := text.find("\n    };", start)
	if end < 0:
		push_error("[motor-arc] cannot find the end of MotorTuning.Default's initializer")
		return {}
	var block := text.substr(start, end - start)

	var out := {}
	for name in ROWS:
		var re := RegEx.new()
		re.compile("\\b" + name + "\\s*=\\s*(-?[0-9.]+)f\\s*,")
		var m := re.search(block)
		if m == null:
			push_error("[motor-arc] MotorTuning.Default does not set `%s`" % name)
			return {}
		out[name] = float(m.get_string(1))
	return out


## AvatarMotor.GravityFor's three-way cut plus MOVE-4c's apex hang, re-implemented.
## The MOVE-5f launch coil is deliberately absent: AnticipationMode ships at 0 and the term is
## inert there, so modelling it would be modelling a branch that does not run.
static func _gravity_for(t: Dictionary, vy: float, held: bool) -> float:
	var g := 0.0
	if vy < 0.0:
		g = t["Gravity"] * t["FallGravityMultiplier"]
	elif vy > 0.0 and not held:
		g = t["Gravity"] * t["JumpReleaseGravityMultiplier"]
	else:
		g = t["Gravity"]

	var s: float = t["ApexHangStrength"]
	var window: float = t["ApexHangWindowMps"]
	if s > 0.0 and window > 0.0:
		var u: float = min(1.0, absf(vy) / window)
		var w := 1.0 - (u * u * (3.0 - 2.0 * u))
		g *= 1.0 - s * w
	return g


## One jump, tick by tick. `hold_ticks` is how many AIRBORNE ticks the jump key stays down;
## `air_jump` spends one traditional air jump on the first non-rising tick, which is the apex and
## the only instant at which mode 1's ASSIGNMENT of velocity.Y throws nothing away.
## Returns [apex_m, airtime_sec] with the launch tick still included.
static func _simulate(t: Dictionary, hold_ticks: int, air_jump: bool = false) -> Array:
	var vy: float = t["JumpVelocity"]
	var y := vy * DT
	var apex := y
	var ticks := 1
	var spent := false
	while y > 0.0 and ticks < 1200:
		var held := ticks <= hold_ticks
		if air_jump and not spent and vy <= 0.0:
			# The air jump gets its own FREE LAUNCH TICK, exactly as the ground jump does:
			# AvatarMotor.Step assigns velocity.Y and hands it straight to MoveAndSlide, so the
			# body travels the whole new velocity for one tick before any gravity is taken off it.
			# Leaving this out costs 0.112 m of apex, which MOVE-8's in-engine capture caught.
			vy = t["JumpVelocity"] * t["AirJumpVelocityFraction"]
			spent = true
			y += vy * DT
			if y > apex:
				apex = y
			ticks += 1
			continue
		vy -= _gravity_for(t, vy, held) * DT
		y += vy * DT
		if y > apex:
			apex = y
		ticks += 1
	return [apex, ticks * DT]


## MOVE-3d's "as played" convention: MovementPlayground latches the takeoff position on the first
## tick Grounded goes false, which is AFTER the launch tick advanced the body, and starts its
## airtime clock on that same tick. Both offsets are exact rather than fitted.
static func _as_played(t: Dictionary, hold_ticks: int, air_jump: bool = false) -> Array:
	var r := _simulate(t, hold_ticks, air_jump)
	return [r[0] - t["JumpVelocity"] * DT, r[1] - DT]


## The arc for an already-parsed row dictionary. Split out so `verify_against` can run it over the
## pre-ruling tuning without touching the file.
static func arc_for(t: Dictionary) -> Dictionary:
	if t.is_empty():
		return {}
	var sprint: float = t["MoveSpeed"] * t["SprintMultiplier"]
	var held := _as_played(t, 1 << 30)
	var tap := _as_played(t, 0)              # released on the tick after the press; see MotorArc
	var has_double := int(round(t["AirJumpMode"])) == 1 and int(round(t["AirJumpCountMax"])) >= 1
	var dbl := _as_played(t, 1 << 30, true) if has_double else held
	return {
		"SprintApex": held[0], "SprintAirtime": held[1], "SprintRange": sprint * held[1],
		"TapApex": tap[0], "TapAirtime": tap[1], "TapRange": t["MoveSpeed"] * tap[1],
		"DoubleApex": dbl[0], "DoubleAirtime": dbl[1], "DoubleRange": sprint * dbl[1],
		"JogSpeed": t["MoveSpeed"], "SprintSpeed": sprint,
	}


## The shipped arc, derived from the shipped tuning. Empty on any parse failure, so a caller that
## forgets to check gets a missing-key error rather than a plausible wrong number.
static func arc() -> Dictionary:
	return arc_for(_rows())


## THE POSITIVE CONTROL. Re-derives the arc from the tuning that shipped BEFORE Talon's ruling and
## requires MOVE-3e's four in-engine measurements back to the millimetre. Returns "" on success or
## a human-readable failure. A derivation that cannot reproduce the measurement it replaced is a
## guess with a formula in front of it.
static func verify() -> String:
	var pre := {
		"MoveSpeed": 5.4, "SprintMultiplier": 1.6, "Gravity": 22.0,
		"FallGravityMultiplier": 1.35, "ApexHangStrength": 0.0, "ApexHangWindowMps": 2.0,
		"JumpVelocity": 8.4, "JumpReleaseGravityMultiplier": 3.0,
		"AirJumpMode": 0.0, "AirJumpCountMax": 1.0, "AirJumpVelocityFraction": 0.8,
	}
	var a := arc_for(pre)
	var want := {"SprintApex": 1.534, "SprintRange": 6.192, "TapApex": 0.467, "TapRange": 1.710}
	for k in want:
		if absf(a[k] - want[k]) > 0.001:
			return "[motor-arc] the GDScript derivation does not reproduce MOVE-3e's %s: got %.4f, measured %.4f" \
				% [k, a[k], want[k]]
	return ""


## One line for a tool's log, so a bake or a verify records which motor it ran against.
static func describe(a: Dictionary) -> String:
	if a.is_empty():
		return "[motor-arc] UNAVAILABLE"
	return "[motor-arc] jog %.2f sprint %.2f | held %.3f m / %.3f m | tap %.3f m / %.3f m | double %.3f m / %.3f m" \
		% [a["JogSpeed"], a["SprintSpeed"], a["SprintApex"], a["SprintRange"],
		   a["TapApex"], a["TapRange"], a["DoubleApex"], a["DoubleRange"]]
