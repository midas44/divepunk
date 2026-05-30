class_name CityChunk
extends Node3D
## One fixed-length segment of the city — DIVEPUNK, milestone M2.
##
## Buildings line both walls of the corridor (centre stays clear to fly through),
## rendered as a single MultiMeshInstance3D so a whole chunk costs ONE draw call.
## generate(index, seed, difficulty) is deterministic and pure (it only touches this
## chunk's MultiMesh), so it could move to a worker thread later if a profiler asks —
## for now ChunkManager amortises builds to one per frame, which is plenty.
##
## No collision here on purpose: M2 is "an endless city to fly through". Obstacles +
## Jolt collision (crash) arrive in M3.

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

## Shared across every chunk so we allocate one mesh + material, not one per chunk.
static var _building_mesh: BoxMesh

var index: int = 0
var _mm: MultiMesh
var _mmi: MultiMeshInstance3D


func _ready() -> void:
	_ensure_multimesh()


## Deterministically (re)builds this chunk's buildings for a given chunk index.
func generate(p_index: int, base_seed: int, difficulty: float) -> void:
	index = p_index
	if _mm == null:
		_ensure_multimesh()

	var rng := RandomNumberGenerator.new()
	rng.seed = _mix_seed(base_seed, p_index)
	var diff: float = clampf(difficulty, 0.0, 1.0)
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


## A deterministic, order-independent mix of (seed, chunk index) → RNG seed.
func _mix_seed(base_seed: int, idx: int) -> int:
	var h: int = base_seed * 73856093
	h ^= idx * 19349663
	h ^= (idx >> 3) * 83492791
	return h


## One shared unit-cube mesh + material for all chunks. Per-instance colour comes through
## as vertex colour (hence vertex_color_use_as_albedo). A faint emission lifts the boxes out
## of the dark void; M4's aesthetic pass replaces this with real neon materials.
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
