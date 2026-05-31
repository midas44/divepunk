extends Node3D
## Chase camera on a RIGID orbit boom (mouse free-look), with speed-driven FOV and shake —
## DIVEPUNK, milestone M1.
##
## Put this Node3D in the scene; it creates a Camera3D child named "Camera" if one isn't present.
## Call set_target(ship) to follow the ship — game.gd does this for you.
##
## The camera sits on a fixed-length boom and orbits the car: the mouse flies it AROUND the car
## (look back swings it to the front), and it always points straight at the car, so the car stays
## dead-centre and visible from any angle — including directly above or below. The boom is RIGID
## (no follow-lag) on purpose: an earlier smoothed pivot trailed the car by ~speed/sharpness
## metres, which at flight speed exceeded the boom length and pinned the camera behind the car at
## every angle. Smoothness instead comes from the physics tick matching the render rate (see
## project.godot physics_ticks_per_second) so the car never judders against the world.
##
## FOV widening with speed is the single most effective "this feels fast" trick, so the FOV range
## here is worth tuning alongside the ship's speed values.

@export_group("Follow")
@export var offset: Vector3 = Vector3(0.0, 4.0, 12.0)  ## boom: behind (+Z) and above the car; its length is the orbit radius

@export_group("Distance levels")
@export var distance_close: float = 0.7      ## boom multiplier — closest level
@export var distance_normal: float = 1.3     ## boom multiplier — default (1.0 = the old fixed distance, midway between close & normal)
@export var distance_far: float = 2.2        ## boom multiplier — farthest level
@export var distance_default_index: int = 1  ## level a run starts on: 0=close, 1=normal, 2=far
@export var distance_zoom_sharpness: float = 8.0  ## how fast the boom eases between levels when you press Q

@export_group("FOV")
@export var base_fov: float = 78.0                      ## cruising vertical FOV — a touch wider than stock for situational awareness at scale
@export var max_fov: float = 100.0                      ## widens with speed = sense of velocity
@export var fov_sharpness: float = 4.0

@export_group("Clipping")
@export var near_distance: float = 0.1                  ## near clip plane (m)
@export var far_distance: float = 18000.0              ## far clip = hard draw distance (m); NOTHING renders past it. Keep it > the city draw distance so fog (not the clip plane) hides the edge.

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
var _look_yaw: float = 0.0      ## held orbit yaw (radians), full 360°; persists until you move the mouse
var _look_pitch: float = 0.0    ## held orbit pitch (radians), clamped to ±limit; persists until you move the mouse
var _distances: Array[float] = []
var _distance_index: int = 1    ## index into _distances, cycled by the cycle_camera (Q) action
var _distance_mult: float = 1.0 ## smoothed current boom multiplier, eased toward _distances[_distance_index]


func _ready() -> void:
	_cam = get_node_or_null(^"Camera") as Camera3D
	if _cam == null:
		_cam = Camera3D.new()
		_cam.name = "Camera"
		add_child(_cam)
	_cam.near = near_distance
	_cam.far = far_distance
	_cam.fov = base_fov
	_distances = [distance_close, distance_normal, distance_far]
	_distance_index = clampi(distance_default_index, 0, _distances.size() - 1)
	_distance_mult = _distances[_distance_index]   # start at the chosen level (no opening zoom)


func set_target(t: Node3D) -> void:
	_target = t


## Advance to the next camera distance level (close → normal → far → close); bound to the
## cycle_camera action (Q). The boom length eases toward the new level in _process.
func _cycle_distance() -> void:
	if _distances.is_empty():
		return
	_distance_index = (_distance_index + 1) % _distances.size()


## Call on impacts / boosts for a punch of screen shake. amount ~0.3–1.0.
func add_shake(amount: float) -> void:
	_shake = minf(_shake + amount, 1.0)


## Accumulate mouse motion into a held orbit angle (no auto-return). Handled in _input (not
## _unhandled_input) so a full-screen Control can never swallow it; the captured cursor (set in
## game.gd) produces the relative-motion events. Non-inverted: right orbits right, up orbits up.
func _input(event: InputEvent) -> void:
	# Cycle the camera distance (Q) — handled before the mouse-look guard so it works even with
	# free-look disabled.
	if event.is_action_pressed(&"cycle_camera"):
		_cycle_distance()
		return
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

	# Rigid boom: orbit the camera around the car's ACTUAL position (no follow-lag). One spin
	# rotates both the boom offset and the camera's orientation, so the camera always faces the
	# car with no look_at — reaching directly above / below with no pole degeneracy, and keeping
	# the car dead-centre at every angle (the lag-free boom is what lets the orbit reach the front).
	var pivot := _target.global_position
	var spin := Basis.IDENTITY
	if mouse_look_enabled:
		spin = Basis.from_euler(Vector3(_look_pitch, _look_yaw, 0.0))
	# Ease the boom length toward the selected distance level (close / normal / far via Q).
	var target_mult: float = _distances[_distance_index] if not _distances.is_empty() else 1.0
	_distance_mult = lerpf(_distance_mult, target_mult, 1.0 - exp(-distance_zoom_sharpness * delta))
	var eff_offset := offset * _distance_mult
	var rest := Basis.looking_at(-eff_offset, Vector3.UP)   # aim the camera's −Z at the car from the rest pose
	global_transform = Transform3D(spin * rest, pivot + spin * eff_offset)

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
