class_name TrafficManager
extends Node3D
## Moving "traffic" — cars cruising the corridor in both directions at random speeds (M3+).
##
## A pool of code-moved StaticBody3D cars that live on the OBSTACLE layer and in the obstacle
## group, so the ship's existing crash / near-miss shape queries (ship.gd) detect them with no
## extra wiring — exactly like the static obstacles, but these move. Cars occupy a Z-window
## around the ship and recycle (fresh random position / speed / direction) when they drift out
## of range, so the population stays constant no matter how far you fly (no per-frame
## instantiate / free). Their colour is a distinct hot magenta so they read instantly as moving
## hazards, separate from the amber static obstacles and the blue buildings.

const OBSTACLE_GROUP := &"obstacle"
const OBSTACLE_LAYER := 2          ## 1-indexed physics layer for obstacles (matches ship.gd / project.godot)

## Shared M4 hazard shader (pulsing emissive + fresnel rim) — same one the static obstacles use.
const HAZARD_SHADER := preload("res://shaders/hazard.gdshader")

@export_group("Traffic")
@export var car_count: int = 24                  ## how many moving cars exist at once (the pool size)
@export var min_speed: float = 30.0              ## slowest car cruise speed (m/s)
@export var max_speed: float = 140.0             ## fastest car cruise speed (m/s)
@export_range(0.0, 1.0) var toward_fraction: float = 0.5  ## fraction of cars heading TOWARD the player (+Z); the rest cruise the way the player flies (-Z)

@export_group("Window")
@export var spawn_ahead: float = 2800.0          ## how far ahead (-Z) of the ship cars live / re-enter
@export var spawn_behind: float = 600.0          ## how far behind (+Z) the ship a car persists before recycling

@export_group("Corridor")
@export var corridor_half_width: float = 120.0
@export var corridor_floor: float = 4.0
@export var corridor_ceiling: float = 1700.0
@export_range(0.0, 1.0) var x_fraction: float = 0.9   ## cars span ± this fraction of the corridor half-width
@export_range(0.0, 1.0) var y_fraction: float = 0.9   ## ...and this fraction of the floor→ceiling height, centred

@export_group("Car")
@export var car_size: Vector3 = Vector3(5.0, 2.0, 9.0)   ## a touch bigger than the player car so it reads as traffic
@export var hazard_pulse: bool = true   ## pulsing-emissive + fresnel telegraph shader (set from [fx] hazard_pulse)

@export_group("Generation")
@export var world_seed: int = 0                  ## 0 = random; >0 = reproducible initial layout

## Shared mesh + material across every car (one allocation, not one per car).
static var _car_mesh: BoxMesh
static var _car_mat: ShaderMaterial

var _target: Node3D
var _cars: Array[StaticBody3D] = []
var _vel_z: Array[float] = []     ## per-car signed Z velocity (m/s): + heads toward the player
var _rng := RandomNumberGenerator.new()


func _ready() -> void:
	_rng.seed = world_seed if world_seed > 0 else _random_seed()
	_build_pool()


func set_target(t: Node3D) -> void:
	_target = t
	if _target != null:
		_scatter_initial(_target.global_position.z)


func _physics_process(delta: float) -> void:
	if _target == null:
		return
	var ship_z: float = _target.global_position.z
	for i: int in _cars.size():
		var car := _cars[i]
		var p := car.position
		p.z += _vel_z[i] * delta
		car.position = p
		# Recycle when the car drifts out of the window around the ship (ahead = -Z).
		if p.z > ship_z + spawn_behind or p.z < ship_z - spawn_ahead:
			_respawn(i, ship_z)


## Build the car pool once (StaticBody3D + Col + Mesh), hidden until placed.
func _build_pool() -> void:
	for i: int in maxi(car_count, 0):
		var car := StaticBody3D.new()
		car.name = "Car%d" % i
		car.collision_layer = 0
		car.set_collision_layer_value(OBSTACLE_LAYER, true)   # detectable on the obstacles layer
		car.collision_mask = 0                                # cars detect nothing themselves
		car.add_to_group(OBSTACLE_GROUP)

		var col := CollisionShape3D.new()
		col.name = "Col"
		var box := BoxShape3D.new()
		box.size = car_size
		col.shape = box
		car.add_child(col)

		var mesh := MeshInstance3D.new()
		mesh.name = "Mesh"
		mesh.mesh = _get_car_mesh()                # shared unit cube...
		mesh.material_override = _get_car_mat(hazard_pulse)
		mesh.scale = car_size                       # ...scaled to the car size
		car.add_child(mesh)

		car.visible = false
		add_child(car)
		_cars.append(car)
		_vel_z.append(0.0)


## Spread all cars randomly through the whole window on the first placement, so the world is
## populated immediately rather than streaming in from the far plane.
func _scatter_initial(ship_z: float) -> void:
	var span: float = spawn_ahead + spawn_behind
	for i: int in _cars.size():
		_respawn(i, ship_z)
		var car := _cars[i]
		var p := car.position
		p.z = ship_z + spawn_behind - _rng.randf_range(0.0, span)
		car.position = p


## (Re)place car i with a fresh random cross-section position, speed and direction, entering at
## whichever Z edge lets it traverse the window given its motion RELATIVE to the ship (which
## flies -Z). rel >= 0 => it drifts toward +Z (behind), so enter from the far-ahead edge;
## rel < 0 => it drifts toward -Z (ahead), so enter from behind. Without this, fast "away" cars
## spawned ahead would cross the ahead boundary on the very next frame and churn.
func _respawn(i: int, ship_z: float) -> void:
	var car := _cars[i]
	var x_span: float = corridor_half_width * x_fraction
	var y_lo: float = lerpf(corridor_floor, corridor_ceiling, 0.5 - 0.5 * y_fraction)
	var y_hi: float = lerpf(corridor_floor, corridor_ceiling, 0.5 + 0.5 * y_fraction)
	var toward: bool = _rng.randf() < toward_fraction
	var vz: float = _rng.randf_range(min_speed, max_speed) * (1.0 if toward else -1.0)
	_vel_z[i] = vz

	var player_speed: float = 0.0
	if _target != null and _target.has_method(&"get_speed"):
		player_speed = _target.get_speed()
	var rel: float = vz + player_speed
	var z: float
	if rel >= 0.0:
		z = ship_z - spawn_ahead + _rng.randf_range(0.0, spawn_ahead * 0.1)
	else:
		z = ship_z + spawn_behind - _rng.randf_range(0.0, spawn_behind * 0.5)

	car.position = Vector3(_rng.randf_range(-x_span, x_span), _rng.randf_range(y_lo, y_hi), z)
	car.visible = true


func _random_seed() -> int:
	var r := RandomNumberGenerator.new()
	r.randomize()
	return int(r.randi()) + 1   # +1 so it's never 0 (0 means "random")


## One shared unit-cube mesh for all cars (scaled per-car via MeshInstance3D.scale).
static func _get_car_mesh() -> BoxMesh:
	if _car_mesh == null:
		_car_mesh = BoxMesh.new()
		_car_mesh.size = Vector3.ONE
	return _car_mesh


## Hot magenta, pulsing + fresnel-rimmed (the shared hazard shader) — distinct from the amber static
## obstacles and the cool-blue buildings, so moving traffic reads instantly as a separate hazard.
## Cached statically; `pulse` is a global [fx] toggle (false = steady magenta).
static func _get_car_mat(pulse: bool) -> ShaderMaterial:
	if _car_mat == null:
		_car_mat = ShaderMaterial.new()
		_car_mat.shader = HAZARD_SHADER
		_car_mat.set_shader_parameter("base_color", Color(0.55, 0.05, 0.5))
		_car_mat.set_shader_parameter("emission_color", Color(1.0, 0.1, 0.85))
		_car_mat.set_shader_parameter("emission_energy", 2.6)
		_car_mat.set_shader_parameter("pulse_on", 1.0 if pulse else 0.0)
	return _car_mat
