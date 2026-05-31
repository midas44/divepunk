extends Node
## AudioManager — DIVEPUNK, milestone M4 (autoload singleton "AudioManager").
##
## Every sound is SYNTHESISED procedurally in code (AudioStreamWAV built at boot) — placeholder
## tones until real audio assets are sourced, so the game ships with working juice + a music bed and
## zero binary assets. SFX: near-miss (bright blip), boost (rising whoosh), crash (noise + thud).
## Music: a short looping A-minor synth bed. Levels come from settings.cfg [audio] (master / music /
## sfx, linear 0..1) + music_enabled. Decoupled from gameplay: game.gd calls it through guarded
## get_node_or_null(/root/AudioManager) lookups, so the game runs fine even if audio is unavailable.

const MIX_RATE := 22050
const SFX_VOICES := 6                       ## overlapping SFX players (round-robin), so chained near-misses don't cut each other off

var _music_player: AudioStreamPlayer
var _sfx_players: Array[AudioStreamPlayer] = []
var _sfx_next: int = 0
var _sfx: Dictionary = {}                    ## name -> AudioStreamWAV

var _master: float = 0.9
var _music_vol: float = 0.5
var _sfx_vol: float = 0.8
var _music_on: bool = true


func _ready() -> void:
	_read_config()
	_build_sfx()
	_build_players()


func _read_config() -> void:
	var cfg := get_node_or_null(^"/root/Config")
	if cfg != null and cfg.has_method(&"get_value"):
		_master = float(cfg.get_value("audio", "master", _master))
		_music_vol = float(cfg.get_value("audio", "music", _music_vol))
		_sfx_vol = float(cfg.get_value("audio", "sfx", _sfx_vol))
		_music_on = bool(cfg.get_value("audio", "music_enabled", _music_on))


func _build_players() -> void:
	_music_player = AudioStreamPlayer.new()
	_music_player.stream = _build_music()
	_music_player.volume_db = _to_db(_master * _music_vol)
	add_child(_music_player)

	for i: int in SFX_VOICES:
		var p := AudioStreamPlayer.new()
		p.volume_db = _to_db(_master * _sfx_vol)
		add_child(p)
		_sfx_players.append(p)


func _build_sfx() -> void:
	_sfx[&"near_miss"] = _wav(_synth_near_miss())
	_sfx[&"boost"] = _wav(_synth_boost())
	_sfx[&"crash"] = _wav(_synth_crash())


# ---- public API (called from game.gd via guarded lookups) ----

func near_miss() -> void:
	_play(&"near_miss")


func boost() -> void:
	_play(&"boost")


func crash() -> void:
	_play(&"crash")


func start_music() -> void:
	if _music_on and _music_player != null and not _music_player.playing:
		_music_player.play()


func stop_music() -> void:
	if _music_player != null:
		_music_player.stop()


func _play(sfx_name: StringName) -> void:
	if _sfx_players.is_empty() or not _sfx.has(sfx_name):
		return
	var p := _sfx_players[_sfx_next]
	_sfx_next = (_sfx_next + 1) % _sfx_players.size()
	p.stream = _sfx[sfx_name]
	p.play()


# ---- synthesis -------------------------------------------------------------

func _to_db(linear: float) -> float:
	return linear_to_db(maxf(linear, 0.0001))


## Wrap a mono float sample buffer ([-1,1]) into a 16-bit PCM AudioStreamWAV (optionally looping).
func _wav(samples: PackedFloat32Array, loop: bool = false) -> AudioStreamWAV:
	var w := AudioStreamWAV.new()
	w.format = AudioStreamWAV.FORMAT_16_BITS
	w.mix_rate = MIX_RATE
	w.stereo = false
	var bytes := PackedByteArray()
	bytes.resize(samples.size() * 2)
	for i: int in samples.size():
		bytes.encode_s16(i * 2, int(clampf(samples[i], -1.0, 1.0) * 32767.0))
	w.data = bytes
	if loop:
		w.loop_mode = AudioStreamWAV.LOOP_FORWARD
		w.loop_begin = 0
		w.loop_end = samples.size()
	return w


func _synth_near_miss() -> PackedFloat32Array:
	# Bright, quick upward blip — positive feedback for a chained near-miss.
	var dur := 0.18
	var n := int(dur * MIX_RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	var phase := 0.0
	for i: int in n:
		var prog := float(i) / float(n)
		var freq := lerpf(760.0, 1500.0, prog)
		phase += TAU * freq / float(MIX_RATE)
		var env := exp(-prog * 5.0) * (1.0 - exp(-prog * 40.0))   # fast attack, exp decay
		out[i] = sin(phase) * env * 0.5
	return out


func _synth_boost() -> PackedFloat32Array:
	# Rising whoosh — frequency sweep + a noise swell.
	var dur := 0.45
	var n := int(dur * MIX_RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	var phase := 0.0
	var rng := RandomNumberGenerator.new()
	rng.seed = 1234
	for i: int in n:
		var prog := float(i) / float(n)
		var freq := lerpf(180.0, 760.0, prog * prog)
		phase += TAU * freq / float(MIX_RATE)
		var tone := sin(phase) + 0.5 * sin(phase * 2.0)
		var noise := rng.randf_range(-1.0, 1.0) * 0.3
		var env := sin(PI * prog)            # swell in and out
		out[i] = (tone * 0.5 + noise) * env * 0.5
	return out


func _synth_crash() -> PackedFloat32Array:
	# Noise burst + a downward thud.
	var dur := 0.5
	var n := int(dur * MIX_RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	var phase := 0.0
	var rng := RandomNumberGenerator.new()
	rng.seed = 9
	for i: int in n:
		var prog := float(i) / float(n)
		var freq := lerpf(300.0, 60.0, prog)
		phase += TAU * freq / float(MIX_RATE)
		var thud := sin(phase)
		var noise := rng.randf_range(-1.0, 1.0)
		var env := exp(-prog * 6.0)
		out[i] = (noise * 0.6 + thud * 0.6) * env * 0.7
	return out


func _build_music() -> AudioStreamWAV:
	# A short, moody A-minor synth loop: plucky bass roots + a soft arpeggio. Notes are plucky and
	# the tail is faded so the buffer wraps without a click (the head already starts near zero).
	var beats := 8
	var beat_dur := 60.0 / 120.0            # 120 BPM
	var dur := float(beats) * beat_dur
	var n := int(dur * MIX_RATE)
	var out := PackedFloat32Array()
	out.resize(n)                            # resize zero-fills

	var roots := [110.0, 110.0, 87.31, 98.0]      # A2 A2 F2 G2 — one root per 2 beats
	var arp := [220.0, 261.63, 329.63, 261.63]    # A3 C4 E4 C4 — cycled per half-beat

	for b: int in beats:
		var root: float = roots[(b / 2) % roots.size()]
		_add_note(out, float(b) * beat_dur, beat_dur * 1.9, root, 0.35, 2.5)

	var steps := beats * 2
	for s: int in steps:
		var f: float = arp[s % arp.size()]
		_add_note(out, float(s) * (beat_dur * 0.5), beat_dur * 0.45, f, 0.16, 6.0)

	var fade := int(0.008 * MIX_RATE)
	for i: int in fade:
		var idx := n - 1 - i
		if idx >= 0:
			out[idx] *= float(i) / float(fade)

	return _wav(out, true)


## Adds a plucky sine note (+ a 2nd harmonic) into `buf` at start_t for dur seconds.
func _add_note(buf: PackedFloat32Array, start_t: float, dur: float, freq: float, amp: float, decay: float) -> void:
	var start := int(start_t * MIX_RATE)
	var count := int(dur * MIX_RATE)
	var phase := 0.0
	for i: int in count:
		var idx := start + i
		if idx >= buf.size():
			break
		var prog := float(i) / float(count)
		phase += TAU * freq / float(MIX_RATE)
		var env := exp(-prog * decay) * (1.0 - exp(-prog * 60.0))
		var s := (sin(phase) + 0.3 * sin(phase * 2.0)) * env * amp
		buf[idx] = clampf(buf[idx] + s, -1.0, 1.0)
