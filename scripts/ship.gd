extends CharacterBody3D
## Arcade flight controller — DIVEPUNK, milestones M0–M3.
##
## Attach to a CharacterBody3D (game.gd will create one automatically if you just
## press Play). Constant forward motion that ramps up over a run; the player steers
## laterally and vertically within a corridor; hold `boost` for risky extra speed.
## Every number is exported so you can dial in the "feel" live in the Inspector —
## the speed/steering values ARE Milestone M1, so expect to spend real time tuning them.
##
## Crash detection (M3) uses a direct physics-space shape query each frame, NOT an Area3D:
## under Godot 4.6 + Jolt, Area overlap callbacks proved unreliable here, while
## direct_space_state.intersect_shape detects obstacles deterministically. This matches the
## spec's intent — use the physics engine to DETECT contact (spec §7.4), never to push the
## ship around. The near-miss query (a slightly larger box, spec §7.5) lands in M3b.

@export_group("Speed")
@export var base_speed: float = 60.0        ## forward speed at the start of a run (m/s)
@export var max_speed: float = 160.0        ## ceiling the run-ramp climbs toward (m/s)
@export var ramp_per_second: float = 1.5    ## how fast the speed floor grows over a run
@export var boost_multiplier: float = 1.8   ## speed multiplier while boosting
@export var boost_blend_rate: float = 6.0   ## how quickly boost eases in / out

@export_group("Steering")
@export var lateral_speed: float = 45.0     ## max sideways speed (m/s)
@export var vertical_speed: float = 35.0    ## max climb / dive speed (m/s)
@export var steer_sharpness: float = 8.0    ## higher = snappier, lower = floatier
@export var bank_angle_deg: float = 35.0    ## visual roll into turns (pure juice)
@export var pitch_angle_deg: float = 15.0   ## visual pitch on climb / dive (pure juice)
@export var visual_lerp: float = 10.0       ## how fast the model banks / pitches

@export_group("Corridor (half-extents from centre)")
@export var bound_x: float = 60.0
@export var bound_y_min: float = 4.0
@export var bound_y_max: float = 90.0

@export_group("Collision")
@export var crash_size: Vector3 = Vector3(3.0, 1.0, 5.0)    ## crash hitbox (matches the ship body)
@export var near_miss_size: Vector3 = Vector3(16.0, 12.0, 14.0)  ## "danger bubble" around the ship (spec §7.5)

@export_group("Boost economy")
@export var boost_capacity: float = 4.0           ## tank size = seconds of boost at a full meter
@export var boost_start_fraction: float = 0.5     ## fraction of the tank you start a run with (0..1)
@export var boost_drain: float = 1.0              ## fuel-seconds spent per second of boosting (1.0 = a full tank lasts boost_capacity s)
@export var boost_regen: float = 0.6             ## fuel-seconds refilled per second while NOT boosting (the gradual accumulation)
@export var boost_gain_per_near_miss: float = 0.6  ## bonus fuel-seconds added per near-miss (risk/reward, on top of regen)

## 1-indexed physics layer obstacles live on (matches city_chunk.gd / project.godot).
const OBSTACLE_LAYER := 2

## Emitted every physics frame. ratio is 0 at the run's start speed, 1 at full boost.
signal speed_changed(speed: float, ratio: float, boosting: bool)
## Emitted once per obstacle that enters the near-miss bubble without a crash (spec §7.5).
signal near_miss
## Emitted once when the ship crashes.
signal crashed

var _speed_floor: float = 0.0
var _forward_speed: float = 0.0
var _boost_blend: float = 0.0
var _boost_meter: float = 0.0
var _steer: Vector2 = Vector2.ZERO
var _model: Node3D
var _alive: bool = true
var _crash_shape: BoxShape3D
var _crash_query: PhysicsShapeQueryParameters3D
var _near_shape: BoxShape3D
var _near_query: PhysicsShapeQueryParameters3D
var _near_now: Dictionary = {}   ## instance_id -> true for obstacles currently in the bubble


func _ready() -> void:
	_speed_floor = base_speed
	_forward_speed = base_speed
	_boost_meter = clampf(boost_start_fraction, 0.0, 1.0) * boost_capacity
	_ensure_visual_and_collision()
	_build_queries()


func _physics_process(delta: float) -> void:
	if not _alive:
		return

	# Ramp the speed floor across the run, then ease boost on top of it. Boosting is gated by
	# the boost meter — hold the action AND have fuel. The tank (measured in seconds of boost,
	# boost_capacity) drains while boosting and refills GRADUALLY while you're not; near-misses
	# add a bonus chunk on top (the risk/reward economy, spec §5.3).
	_speed_floor = minf(_speed_floor + ramp_per_second * delta, max_speed)
	var boosting: bool = Input.is_action_pressed(&"boost") and _boost_meter > 0.0
	if boosting:
		_boost_meter = maxf(0.0, _boost_meter - boost_drain * delta)
	else:
		_boost_meter = minf(boost_capacity, _boost_meter + boost_regen * delta)
	_boost_blend = move_toward(_boost_blend, 1.0 if boosting else 0.0, boost_blend_rate * delta)
	_forward_speed = _speed_floor * lerpf(1.0, boost_multiplier, _boost_blend)

	# Smooth raw input toward target for a weighty-but-responsive feel (framerate independent).
	var target := Vector2(
		Input.get_axis(&"steer_left", &"steer_right"),
		Input.get_axis(&"steer_down", &"steer_up")
	)
	_steer = _steer.lerp(target, 1.0 - exp(-steer_sharpness * delta))

	# Compose velocity: constant forward (−Z) + steering on X / Y.
	velocity = Vector3(_steer.x * lateral_speed, _steer.y * vertical_speed, -_forward_speed)
	move_and_slide()

	_clamp_to_corridor()
	_bank_model(delta)
	_check_obstacles()
	speed_changed.emit(_forward_speed, get_speed_ratio(), boosting)


func get_speed() -> float:
	return _forward_speed


## 0..1 fraction of the tank remaining (fuel-seconds / capacity). Drives the HUD bar.
func get_boost_meter() -> float:
	return _boost_meter / maxf(boost_capacity, 0.001)


## Add fuel to the boost tank (called on a near-miss). amount is in fuel-seconds.
func add_boost(amount: float) -> void:
	_boost_meter = clampf(_boost_meter + amount, 0.0, boost_capacity)


## 0 at the run's starting speed, 1 at fully boosted top speed. Drives camera FOV, speed
## lines, audio pitch, etc.
func get_speed_ratio() -> float:
	var top: float = max_speed * boost_multiplier
	return clampf((_forward_speed - base_speed) / maxf(top - base_speed, 0.001), 0.0, 1.0)


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
	# Roll into lateral turns, pitch into vertical movement.
	var target_rot := Vector3(
		deg_to_rad(-_steer.y * pitch_angle_deg),
		0.0,
		deg_to_rad(-_steer.x * bank_angle_deg)
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
