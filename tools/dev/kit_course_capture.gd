# LD-6 — headed captures of scenes/dev/KitCourse.tscn, one station per primitive family.
#
#   Godot_v4.7-stable_mono_win64_console.exe --path . --script res://tools/dev/kit_course_capture.gd -- --kit-capture docs/qa/LD-6
#
# HEADED, never --headless: the checker, the labels and the silhouette read are the whole point
# and none of them exist in a headless frame.
#
# WHY THE ROOT SCRIPT IS DETACHED. KitCourse.tscn's root carries SandboxWorld.cs so the scene can
# host the shipped body when it is launched to be PLAYED. In capture mode that is in the way:
# SandboxCamera makes itself current in _Ready and the capture would frame whatever the follow
# camera decided. So the capture detaches the root script and frees the four avatars, and what is
# photographed is the geometry and nothing else. The playable launch is the other command, in
# docs/levels/KIT.md, and it keeps the script.
#
# THE ONE-METRE CHECKER IS THE SCALE BAR. There is no ruler in frame and there does not need to
# be: every surface in the course wears a 1 m world-space checker (Talon's standing ruling — a
# flat greybox is a bad speedometer), so any distance in any shot can be counted off it.
extends SceneTree

const SCENE := "res://scenes/dev/KitCourse.tscn"

# (tag, camera position, look-at). Chosen so each family is seen ACROSS the line of travel, the
# way a player meets it, rather than from a plan view that flatters every slope equally.
const STATIONS := [
	["01-approach", Vector3(-6.0, 2.2, 0.0), Vector3(40.0, 1.4, 0.0)],
	["02-blocks", Vector3(16.0, 7.5, 24.0), Vector3(34.0, 1.2, 0.0)],
	["03-logs", Vector3(66.0, 6.0, 20.0), Vector3(78.0, 1.0, 0.0)],
	["04-wedges", Vector3(100.0, 6.0, 20.0), Vector3(114.0, 0.8, 0.0)],
	["05-sags", Vector3(140.0, 7.0, 22.0), Vector3(153.0, 0.0, 0.0)],
	["06-domes-tilts", Vector3(170.0, 5.5, 18.0), Vector3(188.0, 1.4, 0.0)],
	["07-labels", Vector3(96.0, 2.6, -16.0), Vector3(150.0, 2.2, -7.0)],
]

var _dir := "docs/qa/LD-6"
var _cam: Camera3D
var _i := 0
var _frames := 0


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	for i in args.size():
		if args[i] == "--kit-capture" and i + 1 < args.size():
			_dir = args[i + 1]
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://" + _dir))

	var packed: PackedScene = load(SCENE)
	if packed == null:
		printerr("[kit-capture] cannot load %s" % SCENE)
		quit(1)
		return
	var world: Node3D = packed.instantiate()
	world.set_script(null)                     # see the header: no follow camera, no avatars
	for n in ["Player", "DummyA", "DummyB", "DummyC", "Camera"]:
		var c := world.get_node_or_null(n)
		if c != null:
			world.remove_child(c)
			c.queue_free()
	root.add_child(world)

	_cam = Camera3D.new()
	_cam.fov = 62.0
	_cam.far = 600.0
	root.add_child(_cam)
	_cam.make_current()
	DisplayServer.window_set_size(Vector2i(1600, 900))


func _process(_delta: float) -> bool:
	_frames += 1
	# Two frames of settle per station: one to move the camera, one for the frame it produced to
	# be the frame that gets read back.
	var phase := _frames % 3
	if phase == 1:
		if _i >= STATIONS.size():
			print("[kit-capture] %d shots written to %s" % [STATIONS.size(), _dir])
			quit(0)
			return true
		var s: Array = STATIONS[_i]
		_cam.global_position = s[1]
		_cam.look_at(s[2], Vector3.UP)
	elif phase == 0:
		var s: Array = STATIONS[_i]
		var img := root.get_texture().get_image()
		var path := "res://%s/kitcourse-%s.png" % [_dir, s[0]]
		var err := img.save_png(path)
		if err != OK:
			printerr("[kit-capture] could not write %s (error %d)" % [path, err])
			quit(1)
			return true
		print("[kit-capture] %s  cam %s -> %s" % [path, s[1], s[2]])
		_i += 1
	return false
