extends Node3D
## Smoothed chase camera with speed-driven FOV and shake — DIVEPUNK, milestone M1.
##
## Put this Node3D in the scene; it will create a Camera3D child named "Camera" if one
## isn't present. Call set_target(ship) to follow the ship — game.gd does this for you.
## FOV widening with speed is the single most effective "this feels fast" trick, so the
## FOV range here is worth tuning alongside the ship's speed values.

@export_group("Follow")
@export var offset: Vector3 = Vector3(0.0, 4.0, 12.0)  ## behind (+Z) and above the ship
@export var follow_sharpness: float = 6.0              ## lower = floatier, laggier chase
@export var look_ahead: float = 10.0                   ## look this far ahead of the ship

@export_group("FOV")
@export var base_fov: float = 70.0
@export var max_fov: float = 96.0                       ## widens with speed = sense of velocity
@export var fov_sharpness: float = 4.0

@export_group("Shake")
@export var shake_decay: float = 5.0
@export var shake_strength: float = 0.6

var _target: Node3D
var _cam: Camera3D
var _shake: float = 0.0
var _snapped: bool = false


func _ready() -> void:
	_cam = get_node_or_null(^"Camera") as Camera3D
	if _cam == null:
		_cam = Camera3D.new()
		_cam.name = "Camera"
		add_child(_cam)
	_cam.fov = base_fov


func set_target(t: Node3D) -> void:
	_target = t


## Call on impacts / boosts for a punch of screen shake. amount ~0.3–1.0.
func add_shake(amount: float) -> void:
	_shake = minf(_shake + amount, 1.0)


func _process(delta: float) -> void:
	if _target == null:
		return

	# Smoothly chase a point behind / above the ship.
	var desired := _target.global_position + offset
	if not _snapped:
		global_position = desired          # avoid an ugly swoop from the origin on frame 1
		_snapped = true
	else:
		global_position = global_position.lerp(desired, 1.0 - exp(-follow_sharpness * delta))

	# Look a little ahead of the ship (it always travels −Z) for a forward-leaning feel.
	look_at(_target.global_position + Vector3(0.0, 0.0, -look_ahead), Vector3.UP)

	# Speed → FOV.
	var ratio: float = 0.0
	if _target.has_method(&"get_speed_ratio"):
		ratio = _target.get_speed_ratio()
	var target_fov: float = lerpf(base_fov, max_fov, ratio)
	_cam.fov = lerpf(_cam.fov, target_fov, 1.0 - exp(-fov_sharpness * delta))

	# Shake decays each frame; applied as a small screen-space lens offset.
	_shake = move_toward(_shake, 0.0, shake_decay * delta)
	var s: float = _shake * _shake * shake_strength
	_cam.h_offset = randf_range(-1.0, 1.0) * s
	_cam.v_offset = randf_range(-1.0, 1.0) * s
