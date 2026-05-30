class_name ChunkManager
extends Node3D
## Streams the city around the player — DIVEPUNK, milestone M2.
##
## Keeps a fixed window of chunks active: spawn ahead of the ship, recycle behind it.
## Uses an OBJECT POOL — chunks are instantiated once at startup and reused forever, so
## memory stays flat no matter how far you fly (no per-frame instantiate/free).
##
## The pool is a ring: the chunk holding index i lives in slot (i mod pool_size). When
## the player advances past a chunk, that same slot is repositioned far ahead and
## regenerated for its new index. Rebuilds are amortised (builds_per_frame) so generation
## never spikes the frame — at flight speed at most one new chunk is needed every few
## seconds, far under the budget.

const CityChunkScene := preload("res://scenes/world/CityChunk.tscn")

@export_group("Streaming")
@export var chunk_length: float = 200.0      ## metres per chunk (pushed down to each CityChunk)
@export var chunks_ahead: int = 14           ## chunks kept generated ahead of the ship (× chunk_length = draw distance)
@export var chunks_behind: int = 1           ## how many to keep behind (so the player isn't on the edge)
@export var builds_per_frame: int = 1        ## amortisation cap: max chunk (re)builds per frame

@export_group("Generation")
@export var world_seed: int = 0              ## 0 = random each run; >0 = fixed, reproducible city
@export var base_difficulty: float = 0.15
@export var difficulty_ramp: float = 0.012   ## difficulty added per chunk index
@export_range(0.0, 1.0) var max_difficulty: float = 1.0

@export_group("Scale")
@export var world_scale: float = 2.0         ## enlarges the city (buildings) around the constant-size car; pushed to each chunk. 1.0 = original

@export_group("Corridor")
@export var corridor_half_width: float = 120.0  ## clear flyable half-width; pushed to each chunk so buildings set back to match the ship's bound_x
@export var corridor_floor: float = 4.0          ## corridor floor; pushed to each chunk so obstacles fill the full height
@export var corridor_ceiling: float = 1700.0     ## corridor ceiling; pushed to each chunk so obstacles fill the full height

@export_group("Debug")
@export var log_streaming: bool = false      ## print spawn/recycle events to the console

var _target: Node3D
var _pool: Array[CityChunk] = []
var _pool_size: int = 0
var _first_index: int = 0                    ## smallest chunk index currently active
var _seed: int = 0
var _build_queue: Array[int] = []
var _initialized: bool = false


func _ready() -> void:
	_seed = world_seed if world_seed > 0 else _random_seed()
	_pool_size = chunks_ahead + chunks_behind + 1
	_create_pool()
	print("[DIVEPUNK] world seed = %d  (pool: %d chunks of %.0fm)" % [_seed, _pool_size, chunk_length])


func set_target(t: Node3D) -> void:
	_target = t
	_initial_fill()


func _process(_delta: float) -> void:
	if not _initialized or _target == null:
		return
	# Advance the ring as the player moves forward (-Z): retire chunks that fell behind,
	# queue their slots for regeneration far ahead.
	var desired_first: int = _current_index() - chunks_behind
	while _first_index < desired_first:
		_queue_build(_first_index + _pool_size)
		_first_index += 1
	_drain_queue()


func _create_pool() -> void:
	for i: int in _pool_size:
		var c := CityChunkScene.instantiate() as CityChunk
		c.chunk_length = chunk_length
		c.world_scale = world_scale
		c.corridor_half_width = corridor_half_width
		c.corridor_floor = corridor_floor
		c.corridor_ceiling = corridor_ceiling
		add_child(c)
		_pool.append(c)


## Builds the whole active window once, synchronously. This is a load-time cost (before
## the player is moving), not an in-flight hitch — the world must exist on frame one.
func _initial_fill() -> void:
	if _target == null:
		return
	_first_index = _current_index() - chunks_behind
	for n: int in _pool_size:
		_build_chunk_now(_first_index + n)
	_initialized = true


func _drain_queue() -> void:
	var built: int = 0
	while built < builds_per_frame and not _build_queue.is_empty():
		_build_chunk_now(_build_queue.pop_front())
		built += 1


func _queue_build(idx: int) -> void:
	if not _build_queue.has(idx):
		_build_queue.append(idx)


func _build_chunk_now(idx: int) -> void:
	var slot: int = _slot_for(idx)
	var c := _pool[slot]
	c.position = Vector3(0.0, 0.0, -float(idx) * chunk_length)
	c.generate(idx, _seed, _difficulty_for(idx))
	if log_streaming:
		print("[DIVEPUNK]   built chunk %d (slot %d) @ z=%.0f" % [idx, slot, c.position.z])


## Chunk index the ship is currently inside (it travels toward -Z, so distance = -z).
func _current_index() -> int:
	return int(floor(-_target.global_position.z / chunk_length))


func _slot_for(idx: int) -> int:
	return ((idx % _pool_size) + _pool_size) % _pool_size   # handles negative indices


func _difficulty_for(idx: int) -> float:
	return clampf(base_difficulty + float(maxi(idx, 0)) * difficulty_ramp, 0.0, max_difficulty)


func _random_seed() -> int:
	var r := RandomNumberGenerator.new()
	r.randomize()
	return int(r.randi()) + 1   # +1 so it's never 0 (0 means "random" in config)
