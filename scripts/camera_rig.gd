extends Node3D
## Chase camera that orbits the car on a fixed-radius boom (mouse free-look), with speed-driven
## FOV and shake — DIVEPUNK, milestone M1.
##
## Put this Node3D in the scene; it creates a Camera3D child named "Camera" if one isn't present.
## Call set_target(ship) to follow the ship — game.gd does this for you. The mouse flies the camera
## AROUND the car and always looks straight at it, so the car stays framed from any angle — including
## directly above or below. FOV widening with speed is the single most effective "this feels fast"
## trick, so the FOV range here is worth tuning alongside the ship's speed values.

@export_group("Follow")
@export var offset: Vector3 = Vector3(0.0, 4.0, 12.0)  ## rest pose: behind (+Z) and above the car; its length is the orbit radius
@export var follow_sharpness: float = 6.0              ## how quickly the boom's pivot trails the car (lower = floatier)

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
@export var look_pitch_limit_deg: float = 90.0    ## up / down orbit limit; 90° reaches directly above / below the car. Yaw is unlimited (full 360°)

var _target: Node3D
var _cam: Camera3D
var _shake: float = 0.0
var _snapped: bool = false
var _smooth_pivot: Vector3 = Vector3.ZERO   ## smoothed point the boom orbits (trails the car)
var _look_yaw: float = 0.0      ## held orbit yaw (radians), full 360°; persists until you move the mouse
var _look_pitch: float = 0.0    ## held orbit pitch (radians), clamped to ±limit; persists until you move the mouse


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
		# Yaw orbits a full 360° (look back swings the camera to the front); wrap to keep it tidy.
		_look_yaw = wrapf(_look_yaw - motion.x * mouse_sensitivity, -PI, PI)
		# Pitch orbits up / down to the limit (±90° = directly above / below the car).
		var pitch_limit := deg_to_rad(look_pitch_limit_deg)
		_look_pitch = clampf(_look_pitch - motion.y * mouse_sensitivity, -pitch_limit, pitch_limit)


func _process(delta: float) -> void:
	if _target == null:
		return

	# Smoothly trail the car with the boom's pivot. This is the only lag in the rig; the orbit
	# itself is rigid, so swinging the view never feels mushy or "starved" by the car's forward speed.
	var pivot := _target.global_position
	if not _snapped:
		_smooth_pivot = pivot
		_snapped = true
	else:
		_smooth_pivot = _smooth_pivot.lerp(pivot, 1.0 - exp(-follow_sharpness * delta))

	# Rigid orbit: rotate BOTH the boom offset and the camera's orientation by the same spin, so the
	# camera always looks straight at the car — at any angle, including directly above / below, with
	# no look_at degeneracy at the poles. rest aims the camera's −Z at the car from the rest pose.
	var spin := Basis.IDENTITY
	if mouse_look_enabled:
		spin = Basis.from_euler(Vector3(_look_pitch, _look_yaw, 0.0))
	var rest := Basis.looking_at(-offset, Vector3.UP)
	global_transform = Transform3D(spin * rest, _smooth_pivot + spin * offset)

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
