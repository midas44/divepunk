extends CharacterBody3D
## Arcade flight controller — DIVEPUNK, milestones M0–M1.
##
## Attach to a CharacterBody3D (game.gd will create one automatically if you just
## press Play). Constant forward motion that ramps up over a run; the player steers
## laterally and vertically within a corridor; hold `boost` for risky extra speed.
## Every number is exported so you can dial in the "feel" live in the Inspector —
## this script IS Milestone M1, so expect to spend real time tuning these.

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

## Emitted every physics frame. ratio is 0 at the run's start speed, 1 at full boost.
signal speed_changed(speed: float, ratio: float, boosting: bool)
## Emitted once when the ship crashes (wired up in M3).
signal crashed

var _speed_floor: float = 0.0
var _forward_speed: float = 0.0
var _boost_blend: float = 0.0
var _steer: Vector2 = Vector2.ZERO
var _model: Node3D
var _alive: bool = true


func _ready() -> void:
	_speed_floor = base_speed
	_forward_speed = base_speed
	_ensure_visual_and_collision()


func _physics_process(delta: float) -> void:
	if not _alive:
		return

	# Ramp the speed floor across the run, then ease boost on top of it.
	_speed_floor = minf(_speed_floor + ramp_per_second * delta, max_speed)
	var boosting: bool = Input.is_action_pressed(&"boost")
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
	speed_changed.emit(_forward_speed, get_speed_ratio(), boosting)


func get_speed() -> float:
	return _forward_speed


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


## Builds a placeholder neon box + collision so you can press Play in M0 with zero art.
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
