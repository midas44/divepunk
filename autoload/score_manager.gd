extends Node
## ScoreManager — DIVEPUNK, milestone M3 (autoload singleton "ScoreManager").
##
## score = distance + Σ(near_miss_value × multiplier)   (spec §7.6)
## The multiplier climbs with each chained near-miss and decays back toward 1.0 after
## combo_timeout seconds without one (the "don't break the chain" tension). High score is
## persisted locally to user://highscore.save.
##
## game.gd drives this each frame (set_distance + tick) and forwards the ship's near_miss
## signal to register_near_miss(), so the manager stays decoupled from the ship node.

const SAVE_PATH := "user://highscore.save"

@export_group("Scoring")
@export var near_miss_value: float = 50.0      ## base points per near-miss (before multiplier)
@export var combo_step: float = 0.5            ## multiplier added per near-miss
@export var combo_max: float = 9.0             ## multiplier ceiling
@export var combo_timeout: float = 2.5         ## seconds without a near-miss before decay starts
@export var combo_decay: float = 2.0           ## multiplier units shed per second once decaying

signal score_changed(score: int, multiplier: float)
signal near_miss_registered(multiplier: float)
signal high_score_changed(high_score: int)

var distance: float = 0.0
var near_points: float = 0.0
var multiplier: float = 1.0
var high_score: int = 0

var _since_near: float = 0.0
var _run_active: bool = true


func _ready() -> void:
	high_score = _load_high_score()
	high_score_changed.emit(high_score)


## Reset for a fresh run (the scene reload does this via _ready, but kept explicit for clarity).
func reset_run() -> void:
	distance = 0.0
	near_points = 0.0
	multiplier = 1.0
	_since_near = 0.0
	_run_active = true
	score_changed.emit(get_score(), multiplier)


func tick(delta: float) -> void:
	if not _run_active:
		return
	# Decay the multiplier back toward 1.0 once the combo window lapses.
	_since_near += delta
	if _since_near > combo_timeout and multiplier > 1.0:
		multiplier = maxf(1.0, multiplier - combo_decay * delta)
	score_changed.emit(get_score(), multiplier)


## Forward distance in metres (the ship travels toward -Z, so distance = -z).
func set_distance(d: float) -> void:
	distance = maxf(0.0, d)


func register_near_miss() -> void:
	if not _run_active:
		return
	near_points += near_miss_value * multiplier
	multiplier = minf(combo_max, multiplier + combo_step)
	_since_near = 0.0
	near_miss_registered.emit(multiplier)
	score_changed.emit(get_score(), multiplier)


func get_score() -> int:
	return int(distance + near_points)


## Ends the run, commits the high score, returns true if it was a new best.
func end_run() -> bool:
	_run_active = false
	var final := get_score()
	var is_best := final > high_score
	if is_best:
		high_score = final
		_save_high_score(high_score)
		high_score_changed.emit(high_score)
	return is_best


func _load_high_score() -> int:
	if not FileAccess.file_exists(SAVE_PATH):
		return 0
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	if f == null:
		return 0
	var v := f.get_32()
	f.close()
	return int(v)


func _save_high_score(value: int) -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f == null:
		push_warning("ScoreManager: could not write %s" % SAVE_PATH)
		return
	f.store_32(maxi(0, value))
	f.close()
