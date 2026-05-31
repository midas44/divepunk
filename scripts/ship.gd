extends CharacterBody3D
## Arcade flight controller — DIVEPUNK, milestones M0–M3.
##
## Attach to a CharacterBody3D (game.gd will create one automatically if you just
## press Play). Throttle-driven forward motion with inertia: the player throttles up/down
## and steers laterally and vertically within a corridor. Every number is exported so you
## can dial in the "feel" live in the Inspector — the speed/steering values ARE Milestone M1,
## so expect to spend real time tuning them.
##
## Crash detection (M3) uses a direct physics-space shape query each frame, NOT an Area3D:
## under Godot 4.6 + Jolt, Area overlap callbacks proved unreliable here, while
## direct_space_state.intersect_shape detects obstacles deterministically. This matches the
## spec's intent — use the physics engine to DETECT contact (spec §7.4), never to push the
## ship around. The near-miss query (a slightly larger box, spec §7.5) lands in M3b.

@export_group("Speed (throttle + inertia)")
@export var base_speed: float = 300.0       ## forward speed a run STARTS at (m/s)
@export var min_speed: float = 0.0          ## slowest the throttle reaches (m/s); raise above 0 so the ship never fully stops
@export var max_speed: float = 2000.0       ## fastest the throttle reaches (m/s)
@export var accelerate_rate: float = 240.0  ## throttle-up: how fast `accelerate` adds speed (m/s²)
@export var decelerate_rate: float = 320.0  ## throttle-down: how fast `decelerate` bleeds speed (m/s²)
## INERTIA: with neither throttle key held, speed is HELD constant (no auto-ramp, no drag) — the
## engine maintains whatever speed you last throttled to. Speed is clamped to [min_speed, max_speed].

@export_group("Steering")
@export var lateral_speed: float = 45.0     ## max sideways speed (m/s)
@export var climb_angle_deg: float = 45.0   ## climb/dive steepness: vertical speed = forward_speed × tan(this). 45° climbs as fast as you fly; 0° = no vertical; clamped under 90°
@export var steer_sharpness: float = 8.0    ## higher = snappier, lower = floatier
@export var bank_angle_deg: float = 23.0    ## visual roll into turns (pure juice) — dialled back ~1/3; fuller tilt deferred to the post-corridor pass
@export var pitch_angle_deg: float = 10.0   ## visual pitch on climb / dive (pure juice) — dialled back ~1/3
@export var invert_pitch: bool = true       ## true = nose pitches UP as you climb (natural arcade feel); false = the old nose-down tilt
@export var invert_bank: bool = true        ## true = banks INTO strafes the natural way (direction corrected); false = the other roll
@export var visual_lerp: float = 10.0       ## how fast the model banks / pitches

@export_group("Corridor (half-extents from centre)")
@export var bound_x: float = 120.0          ## horizontal half-width; set from settings.cfg [corridor] half_width
@export var bound_y_min: float = 4.0        ## floor; settings.cfg [corridor] floor
@export var bound_y_max: float = 1700.0     ## ceiling; settings.cfg [corridor] ceiling (set slightly above the rooftops)

@export_group("Collision")
@export var crash_size: Vector3 = Vector3(3.0, 1.0, 5.0)    ## crash hitbox (matches the ship body)
@export var near_miss_size: Vector3 = Vector3(16.0, 12.0, 14.0)  ## "danger bubble" around the ship (spec §7.5)

## 1-indexed physics layer obstacles live on (matches city_chunk.gd / project.godot).
const OBSTACLE_LAYER := 2

## Emitted every physics frame. ratio is 0 at min_speed, 1 at max_speed; `accelerating` = throttling up.
signal speed_changed(speed: float, ratio: float, accelerating: bool)
## Emitted once per obstacle that enters the near-miss bubble without a crash (spec §7.5).
signal near_miss
## Emitted once when the ship crashes.
signal crashed

var _forward_speed: float = 0.0
var _steer: Vector2 = Vector2.ZERO
var _model: Node3D
var _alive: bool = true
var _crash_shape: BoxShape3D
var _crash_query: PhysicsShapeQueryParameters3D
var _near_shape: BoxShape3D
var _near_query: PhysicsShapeQueryParameters3D
var _near_now: Dictionary = {}   ## instance_id -> true for obstacles currently in the bubble


func _ready() -> void:
	_forward_speed = clampf(base_speed, min_speed, max_speed)
	_ensure_visual_and_collision()
	_build_queries()


func _physics_process(delta: float) -> void:
	if not _alive:
		return

	# THROTTLE + INERTIA. Speed is a held state: `accelerate` adds, `decelerate` bleeds, and with
	# neither held the engine simply MAINTAINS the current speed (no auto-ramp, no drag). Holding
	# both nets the difference. Clamped to [min_speed, max_speed].
	var accel_in: float = Input.get_action_strength(&"accelerate")
	var decel_in: float = Input.get_action_strength(&"decelerate")
	_forward_speed += (accel_in * accelerate_rate - decel_in * decelerate_rate) * delta
	_forward_speed = clampf(_forward_speed, min_speed, max_speed)
	# 3rd signal arg = "actively throttling up" — drives the speed-up juice (FX / shake / whoosh).
	var accelerating: bool = accel_in > decel_in

	# Smooth raw input toward target for a weighty-but-responsive feel (framerate independent).
	var target := Vector2(
		Input.get_axis(&"steer_left", &"steer_right"),
		Input.get_axis(&"steer_down", &"steer_up")
	)
	_steer = _steer.lerp(target, 1.0 - exp(-steer_sharpness * delta))

	# Compose velocity: held forward (−Z) + steering on X / Y. Climb/dive speed is the forward speed
	# projected at climb_angle_deg (vertical = forward × tan θ), so 45° climbs as fast as you fly.
	# The angle is clamped just under 90° to keep tan finite.
	var vert: float = _forward_speed * tan(deg_to_rad(clampf(climb_angle_deg, 0.0, 89.0)))
	velocity = Vector3(_steer.x * lateral_speed, _steer.y * vert, -_forward_speed)
	move_and_slide()

	_clamp_to_corridor()
	_bank_model(delta)
	_check_obstacles()
	speed_changed.emit(_forward_speed, get_speed_ratio(), accelerating)


func get_speed() -> float:
	return _forward_speed


## Current vertical speed (m/s): + climbing, − diving. Drives the HUD V-SPD indicator.
func get_vertical_speed() -> float:
	return velocity.y


## Absolute top forward speed (m/s) reachable. HUD horizontal-bar scale.
func get_top_speed() -> float:
	return max_speed


## Top vertical speed (m/s) reachable at full forward speed. HUD V-SPD bar scale.
func get_max_vertical_speed() -> float:
	return max_speed * tan(deg_to_rad(clampf(climb_angle_deg, 0.0, 89.0)))


## 0..1 throttle position (current speed across the min→max range). Drives the HUD throttle bar.
func get_boost_meter() -> float:
	return clampf((_forward_speed - min_speed) / maxf(max_speed - min_speed, 0.001), 0.0, 1.0)


## 0 at min_speed, 1 at max_speed. Drives camera FOV, speed lines, audio pitch, etc.
func get_speed_ratio() -> float:
	return clampf((_forward_speed - min_speed) / maxf(max_speed - min_speed, 0.001), 0.0, 1.0)


func crash() -> void:
	if not _alive:
		return
	_alive = false
	crashed.emit()


## Shape-query the obstacles layer at the ship's position each frame. An inner (body-sized)
## box = crash; an outer "danger bubble" box = near-miss. Runs in _physics_process so
## direct_space_state is valid; masks to OBSTACLE_LAYER so it never hits the ship's own body.
func _check_obstacles() -> void:
	if not _alive or _crash_query == null:
		return
	var space := get_world_3d().direct_space_state
	if space == null:
		return
	var xform := Transform3D(Basis(), global_position)

	# Inner: any hit is a crash (and ends the frame's checks).
	_crash_query.transform = xform
	if not space.intersect_shape(_crash_query, 1).is_empty():
		crash()
		return

	# Outer: obstacles in the bubble (but not the body) are near-misses. Fire once per
	# obstacle, the first frame it enters the bubble; track who's currently inside so the
	# same obstacle doesn't re-trigger every frame as the ship passes it.
	_near_query.transform = xform
	var hits := space.intersect_shape(_near_query, 8)
	var current: Dictionary = {}
	for h: Dictionary in hits:
		var col: Object = h.get("collider")
		if col == null:
			continue
		var id: int = col.get_instance_id()
		current[id] = true
		if not _near_now.has(id):
			near_miss.emit()
	_near_now = current


func _build_queries() -> void:
	_crash_shape = BoxShape3D.new()
	_crash_shape.size = crash_size
	_crash_query = PhysicsShapeQueryParameters3D.new()
	_crash_query.shape = _crash_shape
	_crash_query.collision_mask = 1 << (OBSTACLE_LAYER - 1)   # only the obstacles layer
	_crash_query.collide_with_bodies = true
	_crash_query.collide_with_areas = false

	_near_shape = BoxShape3D.new()
	_near_shape.size = near_miss_size
	_near_query = PhysicsShapeQueryParameters3D.new()
	_near_query.shape = _near_shape
	_near_query.collision_mask = 1 << (OBSTACLE_LAYER - 1)
	_near_query.collide_with_bodies = true
	_near_query.collide_with_areas = false


func _clamp_to_corridor() -> void:
	var p := global_position
	p.x = clampf(p.x, -bound_x, bound_x)
	p.y = clampf(p.y, bound_y_min, bound_y_max)
	global_position = p


func _bank_model(delta: float) -> void:
	if _model == null:
		return
	# Roll into lateral turns, pitch into vertical movement. The sign flips are exposed
	# (invert_pitch / invert_bank) so the nose pitches UP on a climb and strafes bank the way
	# that feels right — both directions corrected. Tune in settings.cfg [ship].
	var pitch_sign: float = 1.0 if invert_pitch else -1.0
	var bank_sign: float = -1.0 if invert_bank else 1.0
	var target_rot := Vector3(
		deg_to_rad(pitch_sign * _steer.y * pitch_angle_deg),
		0.0,
		deg_to_rad(bank_sign * _steer.x * bank_angle_deg)
	)
	_model.rotation = _model.rotation.lerp(target_rot, 1.0 - exp(-visual_lerp * delta))


## Builds a placeholder neon box + collision so you can press Play with zero art.
## Replace the "Model" child with a real ship mesh later — the controller doesn't care.
func _ensure_visual_and_collision() -> void:
	_model = get_node_or_null(^"Model") as Node3D
	if _model == null:
		_model = Node3D.new()
		_model.name = "Model"
		add_child(_model)
		var mi := MeshInstance3D.new()
		var box := BoxMesh.new()
		box.size = Vector3(3.0, 1.0, 5.0)
		mi.mesh = box
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(0.05, 0.6, 0.9)
		mat.emission_enabled = true
		mat.emission = Color(0.0, 0.85, 1.0)
		mat.emission_energy_multiplier = 3.0
		mi.material_override = mat
		_model.add_child(mi)
	if get_node_or_null(^"Col") == null:
		var col := CollisionShape3D.new()
		col.name = "Col"
		var shape := BoxShape3D.new()
		shape.size = Vector3(3.0, 1.0, 5.0)
		col.shape = shape
		add_child(col)
