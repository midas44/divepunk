extends Node3D
## Root bootstrap — DIVEPUNK, milestones M0–M2.
##
## Attach to the root Node3D of Main.tscn and press Play. It self-assembles a runnable
## scene (ship + chase camera + minimal night environment + streaming city), registers
## the input actions in code (so the project runs with zero manual setup), and handles
## restart.
##
## As you build real, hand-authored scenes you can delete the auto-spawn helpers below
## and place the nodes directly in the scene tree instead.

const ShipScript := preload("res://scripts/ship.gd")
const CameraRigScript := preload("res://scripts/camera_rig.gd")
const ChunkManagerScene := preload("res://scenes/world/ChunkManager.tscn")

var _ship: CharacterBody3D
var _rig: Node3D
var _mgr: ChunkManager


func _ready() -> void:
	_register_input()
	_ensure_environment()
	_spawn_ship_and_camera()
	_spawn_world()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed(&"restart"):
		get_tree().reload_current_scene()


func _spawn_ship_and_camera() -> void:
	_ship = get_node_or_null(^"Ship") as CharacterBody3D
	if _ship == null:
		_ship = CharacterBody3D.new()
		_ship.name = "Ship"
		_ship.set_script(ShipScript)   # set script BEFORE add_child so _ready() runs with it
		add_child(_ship)
		_ship.global_position = Vector3(0.0, 30.0, 0.0)

	_rig = get_node_or_null(^"CameraRig") as Node3D
	if _rig == null:
		_rig = Node3D.new()
		_rig.name = "CameraRig"
		_rig.set_script(CameraRigScript)
		add_child(_rig)

	if _rig.has_method(&"set_target"):
		_rig.set_target(_ship)
	if _ship.has_signal(&"crashed"):
		_ship.crashed.connect(_on_ship_crashed)


func _on_ship_crashed() -> void:
	# M3 replaces this with a proper game-over screen; for now, just restart.
	get_tree().reload_current_scene()


## Spawns the M2 streaming city around the ship. World seed + debug flags come from the
## Config autoload (edit config/settings.cfg to change them).
func _spawn_world() -> void:
	if get_node_or_null(^"ChunkManager") != null:
		return
	_mgr = ChunkManagerScene.instantiate() as ChunkManager
	_mgr.name = "ChunkManager"
	_mgr.world_seed = int(_cfg_value("game", "seed", 0))
	_mgr.log_streaming = bool(_cfg_value("debug", "log_streaming", false))
	add_child(_mgr)
	_mgr.set_target(_ship)


## Safe read from the Config autoload (falls back to the default if it isn't present).
func _cfg_value(section: String, key: String, default: Variant) -> Variant:
	var cfg := get_node_or_null(^"/root/Config")
	if cfg != null and cfg.has_method(&"get_value"):
		return cfg.get_value(section, key, default)
	return default


func _ensure_environment() -> void:
	if get_node_or_null(^"Sun") == null:
		var sun := DirectionalLight3D.new()
		sun.name = "Sun"
		sun.rotation = Vector3(deg_to_rad(-50.0), deg_to_rad(40.0), 0.0)
		sun.light_energy = 0.6
		add_child(sun)

	if get_node_or_null(^"WorldEnvironment") == null:
		var we := WorldEnvironment.new()
		we.name = "WorldEnvironment"
		var env := Environment.new()
		env.background_mode = Environment.BG_COLOR
		env.background_color = Color(0.02, 0.02, 0.06)         # near-black night
		env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
		env.ambient_light_color = Color(0.10, 0.10, 0.20)
		env.ambient_light_energy = 0.5
		env.glow_enabled = true                                # neon bloom (M4 tunes this)
		env.fog_enabled = true                                 # depth + hides draw distance
		env.fog_light_color = Color(0.05, 0.07, 0.15)
		env.fog_density = 0.01
		we.environment = env
		add_child(we)

	if get_node_or_null(^"RefGround") == null:
		# A long dark ground plane so motion reads clearly against the city.
		var ground := MeshInstance3D.new()
		ground.name = "RefGround"
		var plane := PlaneMesh.new()
		plane.size = Vector2(4000.0, 44000.0)
		ground.mesh = plane
		var gm := StandardMaterial3D.new()
		gm.albedo_color = Color(0.04, 0.05, 0.09)
		ground.material_override = gm
		ground.position = Vector3(0.0, 0.0, -20000.0)
		add_child(ground)


func _register_input() -> void:
	# Self-contained so the project runs with no manual Input Map setup. You can instead
	# define these in Project Settings → Input Map and delete this function.
	_add_action(&"steer_left",  [KEY_A, KEY_LEFT])
	_add_action(&"steer_right", [KEY_D, KEY_RIGHT])
	_add_action(&"steer_up",    [KEY_W, KEY_UP])
	_add_action(&"steer_down",  [KEY_S, KEY_DOWN])
	_add_action(&"boost",       [KEY_SHIFT, KEY_SPACE])
	_add_action(&"restart",     [KEY_R])


func _add_action(action: StringName, keys: Array) -> void:
	if InputMap.has_action(action):
		return
	InputMap.add_action(action)
	for k: int in keys:
		var ev := InputEventKey.new()
		ev.physical_keycode = k
		InputMap.action_add_event(action, ev)
