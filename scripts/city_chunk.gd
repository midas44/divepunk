class_name CityChunk
extends Node3D
## One fixed-length segment of the city — DIVEPUNK, milestones M2–M3.
##
## Buildings line both walls of the corridor (centre stays clear), rendered as a single
## MultiMeshInstance3D so a whole chunk's towers cost ONE draw call.
##
## Obstacles (M3) are a small pool of StaticBody3D nodes placed IN the flyable corridor —
## these need real per-object collision (crash) and per-object near-miss detection, which
## a MultiMesh can't provide, so they're individual nodes. They're few (<= max_obstacles
## per chunk) and pooled per-chunk, so they recycle for free when the chunk does — no
## per-frame instantiate/free. They sit on physics layer "obstacles"; the ship's hitbox /
## near-miss Areas monitor that layer.
##
## generate(index, seed, difficulty) is deterministic (buildings and obstacles use
## independent RNG streams salted off the same seed) and pure (it only touches this
## chunk's own nodes), so it could move to a worker thread later if a profiler asks.

const OBSTACLE_GROUP := &"obstacle"
const OBSTACLE_LAYER := 2          ## 1-indexed physics layer for obstacles (see project.godot)

@export_group("Chunk")
@export var chunk_length: float = 200.0          ## metres along -Z (ChunkManager keeps this in sync)

@export_group("Corridor")
@export var corridor_half_width: float = 70.0    ## clear flyable half-width (ship bound_x is 60)

@export_group("Buildings")
@export var columns_per_side: int = 3            ## building rows stacked outward from the corridor
@export var rows_per_chunk: int = 12             ## building slots along the chunk's length, per column
@export var column_spacing: float = 26.0         ## X gap between building columns
@export var min_footprint: float = 8.0
@export var max_footprint: float = 20.0
@export var min_height: float = 18.0
@export var max_height: float = 130.0
@export_range(0.0, 1.0) var fill_chance: float = 0.86   ## per-slot chance of a building (gaps add variety)
@export_range(0.0, 1.0) var cell_depth_jitter: float = 0.35

@export_group("Obstacles")
@export var safe_chunks: int = 2                 ## first N chunks have no obstacles (a warm-up runway)
@export var min_obstacles: int = 1               ## obstacle count at lowest difficulty (past the warm-up)
@export var max_obstacles: int = 6               ## obstacle count at full difficulty (also the pool size)
@export var obstacle_x_range: float = 50.0       ## obstacles span +/- this on X (inside the ship's bounds)
@export var obstacle_y_min: float = 10.0
@export var obstacle_y_max: float = 78.0
@export var obstacle_min_size: Vector3 = Vector3(4.0, 6.0, 4.0)
@export var obstacle_max_size: Vector3 = Vector3(12.0, 44.0, 12.0)

## Shared across every chunk so we allocate one mesh + material, not one per chunk/instance.
static var _building_mesh: BoxMesh
static var _obstacle_mesh: BoxMesh
static var _obstacle_mat: StandardMaterial3D

var index: int = 0
var _mm: MultiMesh
var _mmi: MultiMeshInstance3D
var _obstacles: Array[StaticBody3D] = []


func _ready() -> void:
	_ensure_multimesh()
	_ensure_obstacles()


## Deterministically (re)builds this chunk's buildings + obstacles for a given chunk index.
func generate(p_index: int, base_seed: int, difficulty: float) -> void:
	index = p_index
	if _mm == null:
		_ensure_multimesh()
	if _obstacles.is_empty():
		_ensure_obstacles()
	var diff: float = clampf(difficulty, 0.0, 1.0)
	_generate_buildings(base_seed, p_index, diff)
	_generate_obstacles(base_seed, p_index, diff)


func _generate_buildings(base_seed: int, p_index: int, diff: float) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = _mix_seed(base_seed, p_index, 0)
	var row_spacing: float = chunk_length / float(maxi(rows_per_chunk, 1))

	var transforms: Array[Transform3D] = []
	var colors := PackedColorArray()

	for side: float in [-1.0, 1.0]:
		for col: int in columns_per_side:
			var col_x: float = side * (corridor_half_width + column_spacing * (float(col) + 0.5))
			for row: int in rows_per_chunk:
				if rng.randf() > fill_chance:
					continue   # leave a gap
				var fx: float = rng.randf_range(min_footprint, max_footprint)
				var fz: float = rng.randf_range(min_footprint, max_footprint)
				var height: float = rng.randf_range(min_height, max_height)
				height *= 0.65 + 0.35 * diff        # taller as difficulty ramps
				height *= 1.0 + 0.12 * float(col)    # outer columns a touch taller
				var x: float = col_x + rng.randf_range(-3.0, 3.0)
				var z: float = -(float(row) + 0.5) * row_spacing \
					+ rng.randf_range(-row_spacing, row_spacing) * cell_depth_jitter
				var yaw: float = rng.randf_range(-0.12, 0.12)
				var basis := Basis(Vector3.UP, yaw).scaled(Vector3(fx, height, fz))
				transforms.append(Transform3D(basis, Vector3(x, height * 0.5, z)))
				colors.append(Color.from_hsv(rng.randf(), 0.22, rng.randf_range(0.12, 0.30)))

	_mm.instance_count = transforms.size()
	for i: int in transforms.size():
		_mm.set_instance_transform(i, transforms[i])
		_mm.set_instance_color(i, colors[i])


func _generate_obstacles(base_seed: int, p_index: int, diff: float) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = _mix_seed(base_seed, p_index, 1)

	var count: int = 0
	if p_index >= safe_chunks:
		count = int(round(lerpf(float(min_obstacles), float(max_obstacles), diff)))
	count = clampi(count, 0, _obstacles.size())

	for i: int in _obstacles.size():
		var ob := _obstacles[i]
		var shape := ob.get_node(^"Col") as CollisionShape3D
		var mesh := ob.get_node(^"Mesh") as MeshInstance3D
		if i >= count:
			ob.visible = false
			shape.disabled = true
			continue
		var size := Vector3(
			rng.randf_range(obstacle_min_size.x, obstacle_max_size.x),
			rng.randf_range(obstacle_min_size.y, obstacle_max_size.y),
			rng.randf_range(obstacle_min_size.z, obstacle_max_size.z)
		)
		# Spread the active obstacles along the chunk's length, jittered within their slot.
		var slot: float = (float(i) + rng.randf_range(0.2, 0.8)) / float(maxi(count, 1))
		var pos := Vector3(
			rng.randf_range(-obstacle_x_range, obstacle_x_range),
			rng.randf_range(obstacle_y_min, obstacle_y_max),
			-slot * chunk_length
		)
		ob.position = pos
		(shape.shape as BoxShape3D).size = size
		shape.disabled = false
		mesh.scale = size
		ob.visible = true


func _ensure_multimesh() -> void:
	_mmi = get_node_or_null(^"Buildings") as MultiMeshInstance3D
	if _mmi == null:
		_mmi = MultiMeshInstance3D.new()
		_mmi.name = "Buildings"
		add_child(_mmi)
	_mm = MultiMesh.new()
	_mm.transform_format = MultiMesh.TRANSFORM_3D
	_mm.use_colors = true                   # must be set before instance_count
	_mm.mesh = _get_building_mesh()
	_mmi.multimesh = _mm


func _ensure_obstacles() -> void:
	if not _obstacles.is_empty():
		return
	for i: int in max_obstacles:
		var ob := StaticBody3D.new()
		ob.name = "Obstacle%d" % i
		ob.collision_layer = 0
		ob.set_collision_layer_value(OBSTACLE_LAYER, true)   # obstacles sit on the "obstacles" layer
		ob.collision_mask = 0                                # they don't detect anything themselves
		ob.add_to_group(OBSTACLE_GROUP)

		var shape := CollisionShape3D.new()
		shape.name = "Col"
		shape.shape = BoxShape3D.new()                       # per-obstacle shape (size set in generate)
		shape.disabled = true
		ob.add_child(shape)

		var mesh := MeshInstance3D.new()
		mesh.name = "Mesh"
		mesh.mesh = _get_obstacle_mesh()                     # shared unit cube, scaled per obstacle
		mesh.material_override = _get_obstacle_mat()
		ob.add_child(mesh)

		ob.visible = false
		add_child(ob)
		_obstacles.append(ob)


## A deterministic, order-independent mix of (seed, chunk index, salt) -> RNG seed.
## The salt lets buildings (0) and obstacles (1) use independent streams off one world seed,
## so tuning one never reshuffles the other.
func _mix_seed(base_seed: int, idx: int, salt: int) -> int:
	var h: int = base_seed * 73856093
	h ^= idx * 19349663
	h ^= (idx >> 3) * 83492791
	h ^= salt * 50331653
	return h


## One shared unit-cube mesh + material for all building instances. Per-instance colour comes
## through as vertex colour (hence vertex_color_use_as_albedo). A faint emission lifts the boxes
## out of the dark void; M4's aesthetic pass replaces this with real neon materials.
static func _get_building_mesh() -> BoxMesh:
	if _building_mesh == null:
		var mesh := BoxMesh.new()
		mesh.size = Vector3.ONE
		var mat := StandardMaterial3D.new()
		mat.vertex_color_use_as_albedo = true
		mat.metallic = 0.0
		mat.roughness = 0.65
		mat.emission_enabled = true
		mat.emission = Color(0.10, 0.20, 0.35)
		mat.emission_energy_multiplier = 0.6
		mesh.material = mat
		_building_mesh = mesh
	return _building_mesh


static func _get_obstacle_mesh() -> BoxMesh:
	if _obstacle_mesh == null:
		_obstacle_mesh = BoxMesh.new()
		_obstacle_mesh.size = Vector3.ONE
	return _obstacle_mesh


## Warm amber, strongly emissive — deliberately distinct from the cool-blue buildings so
## obstacles read instantly as "danger" at speed (spec §5.11, fairness via telegraphing).
static func _get_obstacle_mat() -> StandardMaterial3D:
	if _obstacle_mat == null:
		_obstacle_mat = StandardMaterial3D.new()
		_obstacle_mat.albedo_color = Color(0.6, 0.25, 0.05)
		_obstacle_mat.metallic = 0.0
		_obstacle_mat.roughness = 0.5
		_obstacle_mat.emission_enabled = true
		_obstacle_mat.emission = Color(1.0, 0.45, 0.1)
		_obstacle_mat.emission_energy_multiplier = 2.6
	return _obstacle_mat
