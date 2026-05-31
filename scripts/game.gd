extends Node3D
## Root bootstrap — DIVEPUNK, milestones M0–M3.
##
## Attach to the root Node3D of Main.tscn and press Play. It self-assembles a runnable
## scene (ship + chase camera + minimal night environment + streaming city + UI), registers
## the input actions in code (so the project runs with zero manual setup), and handles
## restart and the crash -> game-over flow.
##
## As you build real, hand-authored scenes you can delete the auto-spawn helpers below
## and place the nodes directly in the scene tree instead.

const ShipScript := preload("res://scripts/ship.gd")
const CameraRigScript := preload("res://scripts/camera_rig.gd")
const ChunkManagerScene := preload("res://scenes/world/ChunkManager.tscn")
const GameOverScene := preload("res://scenes/ui/GameOver.tscn")
const HUDScene := preload("res://scenes/ui/HUD.tscn")
const TrafficManagerScript := preload("res://scripts/traffic.gd")  # preloaded (not referenced by class_name) so game.gd parses before the global class cache knows TrafficManager

@export_group("Juice")
@export var shake_on_near_miss: float = 0.25
@export var shake_on_crash: float = 1.0

var _ship: CharacterBody3D
var _rig: Node3D
var _mgr: ChunkManager
var _traffic: Node3D
var _game_over: GameOverScreen
var _hud: HUD


func _ready() -> void:
	_register_input()
	_capture_mouse()
	_ensure_environment()
	_spawn_ship_and_camera()
	_spawn_world()
	_spawn_traffic()
	_spawn_ui()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed(&"quit"):
		get_tree().quit()
	elif event.is_action_pressed(&"toggle_fullscreen"):
		_toggle_fullscreen()
	elif event.is_action_pressed(&"restart"):
		get_tree().reload_current_scene()


## Hide + lock the cursor so mouse motion drives the free-look camera (skipped under --headless).
func _capture_mouse() -> void:
	if DisplayServer.get_name() == "headless":
		return
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED


## Flip between fullscreen and windowed (the `toggle_fullscreen` / F action). Works regardless
## of the boot mode set in settings/settings.cfg.
func _toggle_fullscreen() -> void:
	var win := get_window()
	if win == null:
		return
	var is_fs := win.mode == Window.MODE_FULLSCREEN or win.mode == Window.MODE_EXCLUSIVE_FULLSCREEN
	win.mode = Window.MODE_WINDOWED if is_fs else Window.MODE_FULLSCREEN


func _spawn_ship_and_camera() -> void:
	_ship = get_node_or_null(^"Ship") as CharacterBody3D
	if _ship == null:
		_ship = CharacterBody3D.new()
		_ship.name = "Ship"
		_ship.set_script(ShipScript)   # set script BEFORE add_child so _ready() runs with it
		_apply_ship_config(_ship)      # apply config exports BEFORE add_child so _ready() initialises with them
		add_child(_ship)
		_ship.global_position = Vector3(0.0, 30.0, 0.0)

	_rig = get_node_or_null(^"CameraRig") as Node3D
	if _rig == null:
		_rig = Node3D.new()
		_rig.name = "CameraRig"
		_rig.set_script(CameraRigScript)
		_apply_camera_config(_rig)   # apply config exports BEFORE add_child so _ready() builds levels with them
		add_child(_rig)

	if _rig.has_method(&"set_target"):
		_rig.set_target(_ship)
	if _ship.has_signal(&"crashed"):
		_ship.crashed.connect(_on_ship_crashed)
	if _ship.has_signal(&"near_miss"):
		_ship.near_miss.connect(_on_ship_near_miss)


## Spawns the M2 streaming city around the ship. World seed + debug flags come from the
## Config autoload (edit settings/settings.cfg to change them).
func _spawn_world() -> void:
	if get_node_or_null(^"ChunkManager") != null:
		return
	_mgr = ChunkManagerScene.instantiate() as ChunkManager
	_mgr.name = "ChunkManager"
	_mgr.world_seed = int(_cfg_value("game", "seed", 0))
	_mgr.log_streaming = bool(_cfg_value("debug", "log_streaming", false))
	_mgr.world_scale = float(_cfg_value("game", "scale", 2.0))
	_mgr.corridor_half_width = float(_cfg_value("corridor", "half_width", _mgr.corridor_half_width))
	_mgr.corridor_floor = float(_cfg_value("corridor", "floor", _mgr.corridor_floor))
	_mgr.corridor_ceiling = float(_cfg_value("corridor", "ceiling", _mgr.corridor_ceiling))
	_mgr.building_windows = bool(_cfg_value("fx", "building_windows", _mgr.building_windows))
	_mgr.hazard_pulse = bool(_cfg_value("fx", "hazard_pulse", _mgr.hazard_pulse))
	add_child(_mgr)
	_mgr.set_target(_ship)


## Spawns the moving traffic (a live hazard) once the ship exists. Config lives in
## settings.cfg [traffic]; corridor extents come from [corridor]; the seed from [game].
func _spawn_traffic() -> void:
	if get_node_or_null(^"Traffic") != null:
		return
	_traffic = TrafficManagerScript.new()
	_traffic.name = "Traffic"
	_apply_traffic_config(_traffic)   # config exports BEFORE add_child so _ready() builds the pool with them
	add_child(_traffic)
	_traffic.set_target(_ship)


func _apply_traffic_config(t: Node3D) -> void:
	t.car_count = int(_cfg_value("traffic", "count", t.car_count))
	t.min_speed = float(_cfg_value("traffic", "min_speed", t.min_speed))
	t.max_speed = float(_cfg_value("traffic", "max_speed", t.max_speed))
	t.toward_fraction = float(_cfg_value("traffic", "toward_fraction", t.toward_fraction))
	t.corridor_half_width = float(_cfg_value("corridor", "half_width", t.corridor_half_width))
	t.corridor_floor = float(_cfg_value("corridor", "floor", t.corridor_floor))
	t.corridor_ceiling = float(_cfg_value("corridor", "ceiling", t.corridor_ceiling))
	t.hazard_pulse = bool(_cfg_value("fx", "hazard_pulse", t.hazard_pulse))
	t.world_seed = int(_cfg_value("game", "seed", 0))


## Spawns the UI overlays: the in-run HUD and the Game Over screen. A fresh ScoreManager
## run is started here so a scene reload (restart) zeroes the score.
func _spawn_ui() -> void:
	if get_node_or_null(^"HUD") != null:
		return
	ScoreManager.reset_run()

	_hud = HUDScene.instantiate() as HUD
	_hud.name = "HUD"
	add_child(_hud)
	_hud.set_ship(_ship)

	_game_over = GameOverScene.instantiate() as GameOverScreen
	_game_over.name = "GameOver"
	add_child(_game_over)


func _process(_delta: float) -> void:
	# Drive the score: distance is how far the ship has flown (-Z), plus the decaying combo.
	if _ship != null:
		ScoreManager.set_distance(-_ship.global_position.z)
	ScoreManager.tick(_delta)


func _on_ship_near_miss() -> void:
	ScoreManager.register_near_miss()
	if _ship.has_method(&"add_boost"):
		_ship.add_boost(_ship.boost_gain_per_near_miss)
	if _rig != null and _rig.has_method(&"add_shake"):
		_rig.add_shake(shake_on_near_miss)


func _on_ship_crashed() -> void:
	var is_best := ScoreManager.end_run()
	print("[DIVEPUNK] crashed — score %d (best %d%s)"
		% [ScoreManager.get_score(), ScoreManager.high_score, ", NEW BEST" if is_best else ""])
	if _rig != null and _rig.has_method(&"add_shake"):
		_rig.add_shake(shake_on_crash)
	if _game_over != null:
		_game_over.show_over(ScoreManager.get_score(), ScoreManager.high_score, is_best)


## Safe read from the Config autoload (falls back to the default if it isn't present).
func _cfg_value(section: String, key: String, default: Variant) -> Variant:
	var cfg := get_node_or_null(^"/root/Config")
	if cfg != null and cfg.has_method(&"get_value"):
		return cfg.get_value(section, key, default)
	return default


## Pushes the config-driven ship tunables onto the ship before it enters the tree, so _ready()
## initialises with them. The speed model lives in settings.cfg [ship]; the flyable corridor's
## horizontal half-width and vertical floor/ceiling live in [corridor]. Each falls back to the
## ship's own @export default when the key is absent.
func _apply_ship_config(ship: CharacterBody3D) -> void:
	ship.base_speed = float(_cfg_value("ship", "base_speed", ship.base_speed))
	ship.max_speed = float(_cfg_value("ship", "max_speed", ship.max_speed))
	ship.boost_multiplier = float(_cfg_value("ship", "boost_multiplier", ship.boost_multiplier))
	ship.boost_in_rate = float(_cfg_value("ship", "boost_in_rate", ship.boost_in_rate))
	ship.boost_out_rate = float(_cfg_value("ship", "boost_out_rate", ship.boost_out_rate))
	ship.vertical_speed = float(_cfg_value("ship", "vertical_speed", ship.vertical_speed))
	ship.vertical_speed_fraction = float(_cfg_value("ship", "vertical_speed_fraction", ship.vertical_speed_fraction))
	ship.invert_pitch = bool(_cfg_value("ship", "invert_pitch", ship.invert_pitch))
	ship.invert_bank = bool(_cfg_value("ship", "invert_bank", ship.invert_bank))
	ship.bound_x = float(_cfg_value("corridor", "half_width", ship.bound_x))
	ship.bound_y_min = float(_cfg_value("corridor", "floor", ship.bound_y_min))
	ship.bound_y_max = float(_cfg_value("corridor", "ceiling", ship.bound_y_max))


## Pushes the config-driven camera tunables onto the rig before it enters the tree, so _ready()
## builds its distance levels from them. Lives in settings.cfg [camera]; each falls back to the
## rig's own @export default when the key is absent.
func _apply_camera_config(rig: Node3D) -> void:
	rig.distance_close = float(_cfg_value("camera", "distance_close", rig.distance_close))
	rig.distance_normal = float(_cfg_value("camera", "distance_normal", rig.distance_normal))
	rig.distance_far = float(_cfg_value("camera", "distance_far", rig.distance_far))
	rig.distance_default_index = int(_cfg_value("camera", "default_level", rig.distance_default_index))


func _ensure_environment() -> void:
	if get_node_or_null(^"Sun") == null:
		# A dim, cool key light — just enough to model the towers; the city lights itself (emissive).
		var sun := DirectionalLight3D.new()
		sun.name = "Sun"
		sun.rotation = Vector3(deg_to_rad(-55.0), deg_to_rad(35.0), 0.0)
		sun.light_color = Color(0.55, 0.65, 1.0)
		sun.light_energy = 0.35
		add_child(sun)

	if get_node_or_null(^"WorldEnvironment") == null:
		var we := WorldEnvironment.new()
		we.name = "WorldEnvironment"
		we.environment = _build_environment()
		add_child(we)

	if get_node_or_null(^"RefGround") == null:
		# A long, near-black WET street: low roughness + a little metal so SSR mirrors the neon
		# skyline in it (spec §7.7, wet-street reflections). Reads as dark glass when SSR is off.
		var ground := MeshInstance3D.new()
		ground.name = "RefGround"
		var plane := PlaneMesh.new()
		plane.size = Vector2(4000.0, 44000.0)
		ground.mesh = plane
		var gm := StandardMaterial3D.new()
		gm.albedo_color = Color(0.012, 0.016, 0.03)
		gm.metallic = 0.35
		gm.metallic_specular = 0.6
		gm.roughness = 0.22
		ground.material_override = gm
		ground.position = Vector3(0.0, 0.0, -20000.0)
		add_child(ground)


## Assembles the neon-night Environment (M4 aesthetic pass, spec §7.7). The heavier desktop
## effects (volumetric fog, SSR, glow) are gated by settings.cfg [fx] so they can be dialled
## back for performance; the tasteful defaults match the @export fallbacks here.
func _build_environment() -> Environment:
	var env := Environment.new()

	# Dark night sky with a faint neon band along the horizon = distant city glow (spec §7.7).
	var sky_mat := ProceduralSkyMaterial.new()
	sky_mat.sky_top_color = Color(0.01, 0.01, 0.03)
	sky_mat.sky_horizon_color = Color(0.07, 0.03, 0.12)
	sky_mat.sky_curve = 0.12
	sky_mat.sky_energy_multiplier = 0.6
	sky_mat.ground_bottom_color = Color(0.01, 0.01, 0.02)
	sky_mat.ground_horizon_color = Color(0.06, 0.02, 0.10)
	sky_mat.ground_energy_multiplier = 0.3
	var sky := Sky.new()
	sky.sky_material = sky_mat
	env.background_mode = Environment.BG_SKY
	env.sky = sky

	# Ambient + reflections come from the (dark) sky so matte surfaces stay moody and the neon pops.
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.25
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY

	# ACES tonemap keeps neon saturation while taming HDR; a high white point keeps bright
	# emissives COLOURED (so they bloom in colour) instead of clipping to white.
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = float(_cfg_value("fx", "exposure", 1.0))
	env.tonemap_white = 6.0

	# Glow / bloom — the neon halo. Additive reads as light; the HDR threshold keeps the bloom on
	# the bright emissive strips, not the whole frame. Spread over several mips for a soft, wide halo.
	env.glow_enabled = bool(_cfg_value("fx", "glow", true))
	env.glow_intensity = float(_cfg_value("fx", "glow_intensity", 0.85))
	env.glow_strength = 1.0
	env.glow_bloom = float(_cfg_value("fx", "bloom", 0.12))
	env.glow_blend_mode = Environment.GLOW_BLEND_MODE_ADDITIVE
	env.glow_hdr_threshold = 0.95
	env.glow_hdr_scale = 2.0
	for lvl: int in [1, 2, 3, 4, 5]:
		env.set("glow_levels/%d" % lvl, true)

	# Exponential distance fog — depth cue + conveniently hides the chunk draw distance.
	env.fog_enabled = true
	env.fog_light_color = Color(0.05, 0.06, 0.13)
	env.fog_density = float(_cfg_value("fx", "fog_density", 0.01))
	env.fog_sky_affect = 0.3
	env.fog_aerial_perspective = 0.5

	# Volumetric fog — the real mood layer (desktop): a faint neon-tinted haze with true depth.
	env.volumetric_fog_enabled = bool(_cfg_value("fx", "volumetric_fog", true))
	env.volumetric_fog_density = float(_cfg_value("fx", "volumetric_fog_density", 0.018))
	env.volumetric_fog_albedo = Color(0.06, 0.07, 0.16)
	env.volumetric_fog_emission = Color(0.05, 0.02, 0.10)
	env.volumetric_fog_emission_energy = 0.4
	env.volumetric_fog_length = 3200.0
	env.volumetric_fog_gi_inject = 0.2

	# Screen-space reflections — wet-street neon (Forward+ desktop); reflects in the RefGround.
	env.ssr_enabled = bool(_cfg_value("fx", "ssr", true))
	env.ssr_max_steps = 32
	env.ssr_fade_in = 0.15
	env.ssr_fade_out = 2.0
	env.ssr_depth_tolerance = 0.2

	# A touch more contrast + saturation in post to make the neon sing.
	env.adjustment_enabled = true
	env.adjustment_brightness = 1.0
	env.adjustment_contrast = 1.08
	env.adjustment_saturation = float(_cfg_value("fx", "saturation", 1.22))

	return env


func _register_input() -> void:
	# Self-contained so the project runs with no manual Input Map setup. You can instead
	# define these in Project Settings → Input Map and delete this function.
	_add_action(&"steer_left",  [KEY_A, KEY_LEFT])
	_add_action(&"steer_right", [KEY_D, KEY_RIGHT])
	_add_action(&"steer_up",    [KEY_W, KEY_UP])
	_add_action(&"steer_down",  [KEY_S, KEY_DOWN])
	_add_action(&"boost",       [KEY_SHIFT, KEY_SPACE])
	_add_action(&"cycle_camera",[KEY_Q])
	_add_action(&"restart",     [KEY_R])
	_add_action(&"toggle_fullscreen", [KEY_F])
	_add_action(&"quit",        [KEY_ESCAPE])


func _add_action(action: StringName, keys: Array) -> void:
	if InputMap.has_action(action):
		return
	InputMap.add_action(action)
	for k: int in keys:
		var ev := InputEventKey.new()
		ev.physical_keycode = k
		InputMap.action_add_event(action, ev)
