extends Node3D
## Smoothed chase camera with speed-driven FOV and shake — DIVEPUNK, milestone M1.
##
## Put this Node3D in the scene; it will create a Camera3D child named "Camera" if one
## isn't present. Call set_target(ship) to follow the ship — game.gd does this for you.
## FOV widening with speed is the single most effective "this feels fast" trick, so the
## FOV range here is worth tuning alongside the ship's speed values.

@export_group("Follow")
@export var offset: Vector3 = Vector3(0.0, 4.0, 12.0)  ## rest position behind (+Z) and above the car; also the orbit radius
@export var follow_sharpness: float = 6.0              ## lower = floatier, laggier chase
@export var look_height: float = 1.5                   ## aim this far above the car's origin (frames it slightly low)

@export_group("FOV")
@export var base_fov: float = 70.0
@export var max_fov: float = 96.0                       ## widens with speed = sense of velocity
@export var fov_sharpness: float = 4.0

@export_group("Shake")
@export var shake_decay: float = 5.0
@export var shake_strength: float = 0.6

@export_group("Free-look (mouse)")
@export var mouse_look_enabled: bool = true       ## orbit the camera around the car with the mouse (no auto-return)
@export var mouse_sensitivity: float = 0.0014     ## orbit (radians) per pixel of mouse motion
@export var look_pitch_limit_deg: float = 80.0    ## clamp the up / down orbit so the camera never crosses straight over the car; yaw is unlimited (full 360°)

var _target: Node3D
var _cam: Camera3D
var _shake: float = 0.0
var _snapped: bool = false
var _look_yaw: float = 0.0      ## held orbit yaw (radians), full 360°; persists until you move the mouse
var _look_pitch: float = 0.0    ## held orbit pitch (radians), clamped; persists until you move the mouse


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


## Accumulate mouse motion into a held orbit angle (no auto-return). Handled in _input (not
## _unhandled_input) so a full-screen Control can never swallow it; the captured cursor (set in
## game.gd) produces the relative-motion events. Non-inverted: right orbits right, up orbits up.
func _input(event: InputEvent) -> void:
	if not mouse_look_enabled:
		return
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		var motion := (event as InputEventMouseMotion).relative
		# Yaw orbits a full 360° around the car (look back swings the camera to the front); wrap to keep tidy.
		_look_yaw = wrapf(_look_yaw - motion.x * mouse_sensitivity, -PI, PI)
		# Pitch orbits above / below, clamped so the camera never crosses straight over the car.
		var pitch_limit := deg_to_rad(look_pitch_limit_deg)
		_look_pitch = clampf(_look_pitch - motion.y * mouse_sensitivity, -pitch_limit, pitch_limit)


func _process(delta: float) -> void:
	if _target == null:
		return

	# Orbit the rest offset around the car: looking back swings the camera to the front, pitch
	# lifts it above / drops it below. The distance to the car stays constant, so it is a true
	# fly-around — and because we always look at the car (below), it stays framed from any angle.
	var pivot := _target.global_position
	var orbit := Basis.IDENTITY
	if mouse_look_enabled:
		orbit = Basis.from_euler(Vector3(_look_pitch, _look_yaw, 0.0))
	var desired := pivot + orbit * offset
	if not _snapped:
		global_position = desired          # avoid an ugly swoop from the origin on frame 1
		_snapped = true
	else:
		global_position = global_position.lerp(desired, 1.0 - exp(-follow_sharpness * delta))

	# Always frame the car (aim a touch above its origin) so it is visible at every orbit angle.
	look_at(pivot + Vector3(0.0, look_height, 0.0), Vector3.UP)

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
