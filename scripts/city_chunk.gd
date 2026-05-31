class_name CityChunk
extends Node3D
## One fixed-length segment of the city — DIVEPUNK, milestones M2–M3.
##
## Buildings line both walls of the corridor (centre stays clear). They're split across a few
## MultiMeshInstance3D nodes — ONE per silhouette (box / round / hex / tapered) — so a whole chunk's
## towers still cost only a handful of draw calls (one per shape in use) while the skyline stays varied.
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

## M4 neon look — procedural shaders shared by every chunk (see shaders/).
const BUILDING_SHADER := preload("res://shaders/building.gdshader")
const HAZARD_SHADER := preload("res://shaders/hazard.gdshader")

## Building size classes and silhouettes (tuned via the "Building classes" / "Building shapes" groups).
enum {CLS_LOW, CLS_MID, CLS_HIGH, CLS_MEGA}
enum {SHP_BOX, SHP_ROUND, SHP_PRISM, SHP_TAPER}   ## index order MUST match _get_building_meshes()

@export_group("Chunk")
@export var chunk_length: float = 200.0          ## metres along -Z (ChunkManager keeps this in sync)

@export_group("Corridor")
@export var corridor_half_width: float = 120.0   ## clear flyable half-width (set by ChunkManager from settings.cfg [corridor] half_width)
@export var corridor_floor: float = 4.0          ## corridor floor in metres (set by ChunkManager from [corridor] floor)
@export var corridor_ceiling: float = 1700.0     ## corridor ceiling in metres (set by ChunkManager from [corridor] ceiling)

@export_group("Buildings")
@export var columns_per_side: int = 3            ## building columns stacked outward from the corridor
@export var rows_per_chunk: int = 5              ## building slots along the chunk's length, per column (÷world_scale) — lower = sparser
@export var column_spacing: float = 70.0         ## X step between columns (×world_scale); columns are anchored by their inner face and step outward
@export var edge_margin: float = 12.0            ## clear gap (m) kept between the corridor wall and the nearest building face
@export_range(0.0, 1.0) var fill_chance: float = 0.6    ## per-slot chance of a building (gaps add variety; lower = sparser)
@export_range(0.0, 1.0) var cell_depth_jitter: float = 0.35
@export var world_scale: float = 1.0   ## set by ChunkManager; >1 enlarges buildings + spacing around the constant-size car (1.0 = original)
@export var building_windows: bool = true   ## procedural neon-window shader (set by ChunkManager from [fx] building_windows); false = flat emissive boxes

@export_group("Building classes")
## Each slot draws a size CLASS by weight (weights are normalised, so they need not sum to 1). A skyline
## of mostly LOW/MID with rare MEGA towers reads far more interesting than a uniform wall. Heights and
## footprints are metres BEFORE [game] scale — the layout multiplies them by world_scale like the rest
## of the city, so at scale 2.0 a mega tower is roughly twice these numbers tall.
@export var low_weight: float = 0.50
@export var mid_weight: float = 0.38
@export var high_weight: float = 0.08
@export var mega_weight: float = 0.04     ## rare; megatowers are also kept off the corridor-edge column
@export var low_height: Vector2 = Vector2(30.0, 80.0)
@export var mid_height: Vector2 = Vector2(80.0, 200.0)
@export var high_height: Vector2 = Vector2(200.0, 420.0)
@export var mega_height: Vector2 = Vector2(420.0, 880.0)
@export var low_footprint: Vector2 = Vector2(35.0, 95.0)
@export var mid_footprint: Vector2 = Vector2(45.0, 130.0)
@export var high_footprint: Vector2 = Vector2(60.0, 175.0)
@export var mega_footprint: Vector2 = Vector2(95.0, 260.0)
@export var footprint_aspect_min: float = 0.65   ## per-axis spread on the footprint, so towers vary in proportion (square / oblong / slab) — wide range = huge variety
@export var footprint_aspect_max: float = 1.6

@export_group("Building shapes")
## Silhouette mix. Each non-empty shape is one extra MultiMesh per chunk (one draw call), so a 0 weight
## costs nothing. BOX = rectangular, ROUND = cylinder, PRISM = hexagonal, TAPER = tapered/setback tower.
@export var shape_box_weight: float = 0.5
@export var shape_round_weight: float = 0.2
@export var shape_prism_weight: float = 0.15
@export var shape_taper_weight: float = 0.15

@export_group("Obstacles")
@export var safe_chunks: int = 2                 ## first N chunks have no obstacles (a warm-up runway)
@export var min_obstacles: int = 1               ## obstacle count at lowest difficulty (past the warm-up)
@export var max_obstacles: int = 6               ## obstacle count at full difficulty (also the pool size)
@export_range(0.0, 1.0) var obstacle_x_fraction: float = 0.85  ## obstacles span ± this fraction of the corridor half-width
@export_range(0.0, 1.0) var obstacle_y_fraction: float = 0.85  ## ...and this fraction of the floor→ceiling height, centred
@export var obstacle_min_size: Vector3 = Vector3(4.0, 6.0, 4.0)
@export var obstacle_max_size: Vector3 = Vector3(12.0, 44.0, 12.0)
@export var hazard_pulse: bool = true   ## pulsing-emissive + fresnel telegraph shader (set by ChunkManager from [fx] hazard_pulse)

## Shared across every chunk so we allocate the meshes + materials once, not one per chunk/instance.
static var _building_meshes: Array[Mesh] = []   ## one unit mesh per SHP_* silhouette, all sharing _building_mat
static var _building_mat: ShaderMaterial
static var _obstacle_mesh: BoxMesh
static var _obstacle_mat: ShaderMaterial

var index: int = 0
var _mms: Array[MultiMesh] = []                  ## one per silhouette (SHP_*), index-aligned with _mmis
var _mmis: Array[MultiMeshInstance3D] = []
var _obstacles: Array[StaticBody3D] = []


func _ready() -> void:
	_ensure_multimeshes()
	_ensure_obstacles()


## Deterministically (re)builds this chunk's buildings + obstacles for a given chunk index.
func generate(p_index: int, base_seed: int, difficulty: float) -> void:
	index = p_index
	if _mms.is_empty():
		_ensure_multimeshes()
	if _obstacles.is_empty():
		_ensure_obstacles()
	var diff: float = clampf(difficulty, 0.0, 1.0)
	_generate_buildings(base_seed, p_index, diff)
	_generate_obstacles(base_seed, p_index, diff)


func _generate_buildings(base_seed: int, p_index: int, diff: float) -> void:
	# Compute the layout (pure, testable), then split it across one MultiMesh per silhouette and upload.
	var layout := compute_building_layout(base_seed, p_index, diff)
	var transforms: Array[Transform3D] = layout["transforms"]
	var colors: PackedColorArray = layout["colors"]
	var shapes: PackedInt32Array = layout["shapes"]

	# Bucket each instance under its silhouette (plain Arrays — safe to mutate by index).
	var bucket_t: Array = []
	var bucket_c: Array = []
	for k: int in _mms.size():
		bucket_t.append([])
		bucket_c.append([])
	for i: int in transforms.size():
		var k: int = clampi(shapes[i], 0, _mms.size() - 1)
		bucket_t[k].append(transforms[i])
		bucket_c[k].append(colors[i])

	for k: int in _mms.size():
		var ts: Array = bucket_t[k]
		var cs: Array = bucket_c[k]
		var mm: MultiMesh = _mms[k]
		mm.instance_count = ts.size()
		for i: int in ts.size():
			mm.set_instance_transform(i, ts[i])
			mm.set_instance_color(i, cs[i])
		_mmis[k].visible = ts.size() > 0


## Deterministically computes this chunk's building transforms, colors and silhouettes (no rendering
## side effects, so it's unit-testable headless where MultiMesh readback isn't). Returns
## {transforms, colors, shapes}, where shapes[i] is one of SHP_*.
##
## world_scale enlarges the whole city around the (constant-size) car: bigger footprints and heights,
## with row/column spacing widened to match so the building:gap ratio stays put. Each slot rolls a size
## CLASS (mostly low/mid, rare megatowers) and a SILHOUETTE, so the skyline varies in both height and
## shape. corridor_half_width is deliberately NOT scaled, so the towers loom at the same distance (that's
## what makes them read as bigger relative to the car, instead of just receding); megatowers are kept
## off the innermost column so their wide bases never intrude on the flyable corridor.
func compute_building_layout(base_seed: int, p_index: int, diff: float) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = _mix_seed(base_seed, p_index, 0)
	var scale: float = maxf(world_scale, 0.01)
	var eff_rows: int = maxi(1, roundi(float(rows_per_chunk) / scale))
	var row_spacing: float = chunk_length / float(eff_rows)

	var transforms: Array[Transform3D] = []
	var colors := PackedColorArray()
	var shapes := PackedInt32Array()

	var class_total: float = maxf(low_weight, 0.0) + maxf(mid_weight, 0.0) + maxf(high_weight, 0.0) + maxf(mega_weight, 0.0)
	var shape_total: float = maxf(shape_box_weight, 0.0) + maxf(shape_round_weight, 0.0) + maxf(shape_prism_weight, 0.0) + maxf(shape_taper_weight, 0.0)

	for side: float in [-1.0, 1.0]:
		for col: int in columns_per_side:
			for row: int in eff_rows:
				if rng.randf() > fill_chance:
					continue   # leave a gap
				var cls: int = _pick_class(rng, class_total, col)
				var h_range: Vector2 = _class_height(cls)
				var f_range: Vector2 = _class_footprint(cls)
				# Footprint: a per-building base size from the class range, then an INDEPENDENT aspect roll
				# on each axis, so towers vary widely in both size and proportion (square / oblong / slab).
				var fp: float = rng.randf_range(f_range.x, f_range.y)
				var fx: float = fp * rng.randf_range(footprint_aspect_min, footprint_aspect_max) * scale
				var fz: float = fp * rng.randf_range(footprint_aspect_min, footprint_aspect_max) * scale
				var height: float = rng.randf_range(h_range.x, h_range.y)
				height *= 0.65 + 0.35 * diff        # taller as difficulty ramps
				height *= 1.0 + 0.12 * float(col)    # outer columns a touch taller
				height *= scale                      # ...and overall bigger with world_scale
				var yaw: float = rng.randf_range(-0.12, 0.12)
				# Anchor each building by its INNER face so it grows OUTWARD from the corridor as it gets
				# wider — the flyable tube stays clear at ANY footprint. half_x is the rotated footprint's
				# X half-extent, so the yaw never lets a corner creep inward. Columns step outward by `step`.
				var step: float = column_spacing * scale
				var col_inner: float = corridor_half_width + edge_margin + step * float(col)
				var half_x: float = 0.5 * (absf(fx * cos(yaw)) + absf(fz * sin(yaw)))
				var x: float = side * (col_inner + half_x + rng.randf_range(0.0, 0.25) * step)
				var z: float = -(float(row) + 0.5) * row_spacing \
					+ rng.randf_range(-row_spacing, row_spacing) * cell_depth_jitter
				var shape: int = _pick_shape(rng, shape_total)
				var basis := Basis(Vector3.UP, yaw).scaled(Vector3(fx, height, fz))
				transforms.append(Transform3D(basis, Vector3(x, height * 0.5, z)))
				colors.append(Color.from_hsv(rng.randf(), 0.22, rng.randf_range(0.12, 0.30)))
				shapes.append(shape)

	return {"transforms": transforms, "colors": colors, "shapes": shapes}


## Weighted pick of a size class. Megatowers are demoted to high-rise on the innermost column (col 0)
## so their wide footprints never reach into the flyable corridor.
func _pick_class(rng: RandomNumberGenerator, total: float, col: int) -> int:
	var r: float = rng.randf() * maxf(total, 0.0001)
	var a: float = maxf(low_weight, 0.0)
	var b: float = a + maxf(mid_weight, 0.0)
	var c: float = b + maxf(high_weight, 0.0)
	var cls: int = CLS_MEGA
	if r < a:
		cls = CLS_LOW
	elif r < b:
		cls = CLS_MID
	elif r < c:
		cls = CLS_HIGH
	if col == 0 and cls == CLS_MEGA:
		cls = CLS_HIGH
	return cls


func _class_height(cls: int) -> Vector2:
	match cls:
		CLS_LOW:  return low_height
		CLS_MID:  return mid_height
		CLS_HIGH: return high_height
		_:        return mega_height


func _class_footprint(cls: int) -> Vector2:
	match cls:
		CLS_LOW:  return low_footprint
		CLS_MID:  return mid_footprint
		CLS_HIGH: return high_footprint
		_:        return mega_footprint


func _pick_shape(rng: RandomNumberGenerator, total: float) -> int:
	var r: float = rng.randf() * maxf(total, 0.0001)
	var a: float = maxf(shape_box_weight, 0.0)
	var b: float = a + maxf(shape_round_weight, 0.0)
	var c: float = b + maxf(shape_prism_weight, 0.0)
	if r < a:
		return SHP_BOX
	elif r < b:
		return SHP_ROUND
	elif r < c:
		return SHP_PRISM
	return SHP_TAPER


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
		# Spread the active obstacles along the chunk's length, jittered within their slot,
		# and across the FULL corridor cross-section on X and Y (not just a central box).
		var slot: float = (float(i) + rng.randf_range(0.2, 0.8)) / float(maxi(count, 1))
		var x_span: float = corridor_half_width * obstacle_x_fraction
		var y_lo: float = lerpf(corridor_floor, corridor_ceiling, 0.5 - 0.5 * obstacle_y_fraction)
		var y_hi: float = lerpf(corridor_floor, corridor_ceiling, 0.5 + 0.5 * obstacle_y_fraction)
		var pos := Vector3(
			rng.randf_range(-x_span, x_span),
			rng.randf_range(y_lo, y_hi),
			-slot * chunk_length
		)
		ob.position = pos
		(shape.shape as BoxShape3D).size = size
		shape.disabled = false
		mesh.scale = size
		ob.visible = true


func _ensure_multimeshes() -> void:
	var meshes := _get_building_meshes(building_windows, world_scale)
	if _mmis.is_empty():
		for k: int in meshes.size():
			var mmi := MultiMeshInstance3D.new()
			mmi.name = "Buildings%d" % k
			var mm := MultiMesh.new()
			mm.transform_format = MultiMesh.TRANSFORM_3D
			mm.use_colors = true                 # must be set before instance_count; feeds the shader's COLOR
			mm.mesh = meshes[k]
			mmi.multimesh = mm
			add_child(mmi)
			_mmis.append(mmi)
			_mms.append(mm)
	else:
		for k: int in _mms.size():
			_mms[k].mesh = meshes[k]


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
		mesh.material_override = _get_obstacle_mat(hazard_pulse)
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


## One shared neon-window ShaderMaterial + one UNIT mesh per silhouette (SHP_*), for all building
## instances (M4). The MultiMesh feeds the per-building hue through COLOR; the shader turns it into lit
## windows laid out in WORLD space, so the pane size is multiplied by world_scale to stay proportional
## to the enlarged towers (a constant 4×5 m grid looks tiny once [game] scale grows the city). Every
## mesh fits a 1×1×1 box, so the per-instance basis scale (fx, height, fz) sets the real dimensions —
## the cylinder variants give the round / hex / tapered towers. Cached statically; `windows` is a global
## [fx] toggle so every chunk passes the same value.
static func _get_building_meshes(windows: bool, p_world_scale: float) -> Array[Mesh]:
	if _building_mat == null:
		_building_mat = ShaderMaterial.new()
		_building_mat.shader = BUILDING_SHADER
	_building_mat.set_shader_parameter("windows_on", 1.0 if windows else 0.0)
	_building_mat.set_shader_parameter("window_size_v", 4.0 * p_world_scale)
	_building_mat.set_shader_parameter("window_size_h", 5.0 * p_world_scale)
	if _building_meshes.is_empty():
		# Index order MUST match the SHP_* enum (BOX, ROUND, PRISM, TAPER).
		var box := BoxMesh.new()
		box.size = Vector3.ONE
		box.material = _building_mat

		var round_tower := CylinderMesh.new()
		round_tower.height = 1.0
		round_tower.top_radius = 0.5
		round_tower.bottom_radius = 0.5
		round_tower.radial_segments = 20
		round_tower.material = _building_mat

		var prism := CylinderMesh.new()
		prism.height = 1.0
		prism.top_radius = 0.5
		prism.bottom_radius = 0.5
		prism.radial_segments = 6
		prism.material = _building_mat

		var taper := CylinderMesh.new()
		taper.height = 1.0
		taper.top_radius = 0.28
		taper.bottom_radius = 0.5
		taper.radial_segments = 20
		taper.material = _building_mat

		var meshes: Array[Mesh] = [box, round_tower, prism, taper]
		_building_meshes = meshes
	return _building_meshes


static func _get_obstacle_mesh() -> BoxMesh:
	if _obstacle_mesh == null:
		_obstacle_mesh = BoxMesh.new()
		_obstacle_mesh.size = Vector3.ONE
	return _obstacle_mesh


## Warm amber, pulsing + fresnel-rimmed (the shared hazard shader) — deliberately distinct from the
## cool-blue buildings so obstacles read instantly as "danger" at speed (spec §5.11, fairness via
## telegraphing). Cached statically; `pulse` is a global [fx] toggle. pulse = false = steady amber.
static func _get_obstacle_mat(pulse: bool) -> ShaderMaterial:
	if _obstacle_mat == null:
		_obstacle_mat = ShaderMaterial.new()
		_obstacle_mat.shader = HAZARD_SHADER
		_obstacle_mat.set_shader_parameter("base_color", Color(0.6, 0.25, 0.05))
		_obstacle_mat.set_shader_parameter("emission_color", Color(1.0, 0.45, 0.1))
		_obstacle_mat.set_shader_parameter("emission_energy", 2.6)
		_obstacle_mat.set_shader_parameter("pulse_on", 1.0 if pulse else 0.0)
	return _obstacle_mat
