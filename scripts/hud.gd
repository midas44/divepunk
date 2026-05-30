class_name HUD
extends CanvasLayer
## In-run HUD — DIVEPUNK, milestone M3. Score + combo multiplier + best (from the
## ScoreManager autoload) and a boost-meter bar (polled from the ship each frame).
## Presentation-only: it reads game state, never mutates it.

var _ship: Node            # provides get_boost_meter()
var _score_label: Label
var _mult_label: Label
var _best_label: Label
var _boost_fill: ColorRect
var _boost_bg: ColorRect
var _hspd_fill: ColorRect    # horizontal (forward) speed bar
var _hspd_val: Label         # ...and its digital m/s readout
var _vspd_fill: ColorRect    # vertical (climb/dive) speed bar
var _vspd_val: Label         # ...and its digital m/s readout

const BOOST_BAR_W := 260.0


func _ready() -> void:
	layer = 50
	_build()
	if ScoreManager.has_signal("score_changed"):
		ScoreManager.score_changed.connect(_on_score_changed)
		ScoreManager.high_score_changed.connect(_on_high_score_changed)
		ScoreManager.near_miss_registered.connect(_on_near_miss)
	_on_score_changed(ScoreManager.get_score(), ScoreManager.multiplier)
	_on_high_score_changed(ScoreManager.high_score)


func set_ship(s: Node) -> void:
	_ship = s


func _process(_delta: float) -> void:
	if _ship == null:
		return
	if _ship.has_method(&"get_boost_meter"):
		var m: float = _ship.get_boost_meter()
		_boost_fill.size = Vector2(BOOST_BAR_W * clampf(m, 0.0, 1.0), _boost_fill.size.y)
		# Bar tints toward hot as it fills, dims when nearly empty.
		_boost_fill.color = Color(0.1, 0.9, 1.0) if m > 0.15 else Color(0.6, 0.3, 0.3)
	if _ship.has_method(&"get_speed") and _ship.has_method(&"get_top_speed"):
		var sp: float = _ship.get_speed()
		var top: float = _ship.get_top_speed()
		_hspd_fill.size = Vector2(BOOST_BAR_W * clampf(sp / maxf(top, 0.001), 0.0, 1.0), _hspd_fill.size.y)
		_hspd_val.text = "%.0f m/s" % sp
	if _ship.has_method(&"get_vertical_speed") and _ship.has_method(&"get_max_vertical_speed"):
		var vs: float = _ship.get_vertical_speed()
		var vmax: float = _ship.get_max_vertical_speed()
		_vspd_fill.size = Vector2(BOOST_BAR_W * clampf(absf(vs) / maxf(vmax, 0.001), 0.0, 1.0), _vspd_fill.size.y)
		_vspd_val.text = "%+.0f m/s" % vs
		# Climb tints green, dive tints orange, near-level is dim.
		if vs > 1.0:
			_vspd_fill.color = Color(0.3, 0.9, 0.5)
		elif vs < -1.0:
			_vspd_fill.color = Color(0.95, 0.55, 0.2)
		else:
			_vspd_fill.color = Color(0.4, 0.45, 0.55)


func _on_score_changed(score: int, multiplier: float) -> void:
	_score_label.text = "%08d" % score
	_mult_label.text = "x%.1f" % multiplier
	# Emphasise a live combo.
	_mult_label.add_theme_color_override("font_color",
		Color(1.0, 0.85, 0.2) if multiplier > 1.05 else Color(0.5, 0.55, 0.65))


func _on_high_score_changed(high_score: int) -> void:
	_best_label.text = "BEST  %08d" % high_score


func _on_near_miss(_multiplier: float) -> void:
	# A quick pop on the multiplier label for juice (deepened in M4).
	_mult_label.scale = Vector2(1.4, 1.4)
	create_tween().tween_property(_mult_label, "scale", Vector2.ONE, 0.25)


func _build() -> void:
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(root)

	# Score (top-left) + multiplier beneath it.
	_score_label = Label.new()
	_score_label.position = Vector2(28, 20)
	_score_label.add_theme_font_size_override("font_size", 44)
	_score_label.add_theme_color_override("font_color", Color(0.85, 0.95, 1.0))
	root.add_child(_score_label)

	_mult_label = Label.new()
	_mult_label.position = Vector2(30, 74)
	_mult_label.pivot_offset = Vector2(0, 16)
	_mult_label.add_theme_font_size_override("font_size", 32)
	root.add_child(_mult_label)

	# Best (top-right).
	_best_label = Label.new()
	_best_label.anchor_left = 1.0
	_best_label.anchor_right = 1.0
	_best_label.position = Vector2(-260, 28)
	_best_label.add_theme_font_size_override("font_size", 24)
	_best_label.add_theme_color_override("font_color", Color(0.6, 0.65, 0.8))
	root.add_child(_best_label)

	# Boost meter (bottom-left): label + background + fill.
	var boost_label := Label.new()
	boost_label.anchor_top = 1.0
	boost_label.anchor_bottom = 1.0
	boost_label.position = Vector2(28, -64)
	boost_label.text = "BOOST"
	boost_label.add_theme_font_size_override("font_size", 18)
	boost_label.add_theme_color_override("font_color", Color(0.6, 0.65, 0.8))
	root.add_child(boost_label)

	_boost_bg = ColorRect.new()
	_boost_bg.anchor_top = 1.0
	_boost_bg.anchor_bottom = 1.0
	_boost_bg.position = Vector2(28, -40)
	_boost_bg.size = Vector2(BOOST_BAR_W, 16)
	_boost_bg.color = Color(0.1, 0.12, 0.18, 0.85)
	root.add_child(_boost_bg)

	_boost_fill = ColorRect.new()
	_boost_fill.anchor_top = 1.0
	_boost_fill.anchor_bottom = 1.0
	_boost_fill.position = Vector2(28, -40)
	_boost_fill.size = Vector2(0, 16)
	_boost_fill.color = Color(0.1, 0.9, 1.0)
	root.add_child(_boost_fill)

	# Speed indicators stacked above the boost meter: horizontal (forward) and vertical
	# (climb/dive), each a bar + a digital m/s readout.
	var hspd := _make_meter(root, -100.0, "SPD", Color(0.3, 0.9, 0.5))
	_hspd_fill = hspd["fill"] as ColorRect
	_hspd_val = hspd["val"] as Label
	var vspd := _make_meter(root, -156.0, "V-SPD", Color(0.4, 0.45, 0.55))
	_vspd_fill = vspd["fill"] as ColorRect
	_vspd_val = vspd["val"] as Label


## Builds one labelled bar + digital readout in the bottom-left stack (bar_y is measured up from
## the bottom edge). Returns {fill, val} so the caller can drive the fill width + number each frame.
func _make_meter(root: Control, bar_y: float, label_text: String, bar_color: Color) -> Dictionary:
	var name_lbl := Label.new()
	name_lbl.anchor_top = 1.0
	name_lbl.anchor_bottom = 1.0
	name_lbl.position = Vector2(28, bar_y - 24)
	name_lbl.text = label_text
	name_lbl.add_theme_font_size_override("font_size", 18)
	name_lbl.add_theme_color_override("font_color", Color(0.6, 0.65, 0.8))
	root.add_child(name_lbl)

	var bg := ColorRect.new()
	bg.anchor_top = 1.0
	bg.anchor_bottom = 1.0
	bg.position = Vector2(28, bar_y)
	bg.size = Vector2(BOOST_BAR_W, 16)
	bg.color = Color(0.1, 0.12, 0.18, 0.85)
	root.add_child(bg)

	var fill := ColorRect.new()
	fill.anchor_top = 1.0
	fill.anchor_bottom = 1.0
	fill.position = Vector2(28, bar_y)
	fill.size = Vector2(0, 16)
	fill.color = bar_color
	root.add_child(fill)

	var val := Label.new()
	val.anchor_top = 1.0
	val.anchor_bottom = 1.0
	val.position = Vector2(28 + BOOST_BAR_W + 12, bar_y - 4)
	val.add_theme_font_size_override("font_size", 20)
	val.add_theme_color_override("font_color", Color(0.85, 0.95, 1.0))
	root.add_child(val)

	return {"fill": fill, "val": val}
