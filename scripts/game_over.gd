class_name GameOverScreen
extends CanvasLayer
## Game-over overlay — DIVEPUNK, milestone M3.
##
## Shown when the ship crashes: final score, local best, a "NEW BEST!" flourish, and the
## retry hint. Restart itself is handled by game.gd (the `restart` action reloads the scene)
## so the overlay stays presentation-only — one key and you're instantly back in, which is
## the whole "one more go" loop.

var _title: Label
var _score: Label
var _best: Label
var _newbest: Label
var _hint: Label


func _ready() -> void:
	layer = 100          # draw above the HUD and everything else
	_build()
	visible = false


func show_over(final_score: int, high_score: int, is_new_best: bool) -> void:
	_score.text = "SCORE   %d" % final_score
	_best.text = "BEST    %d" % high_score
	_newbest.visible = is_new_best
	visible = true


func _build() -> void:
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.55)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", 14)
	center.add_child(vbox)

	_title = _label(vbox, "CRASHED", 72, Color(1.0, 0.3, 0.25))

	_newbest = _label(vbox, "★ NEW BEST! ★", 34, Color(1.0, 0.85, 0.2))
	_newbest.visible = false

	_score = _label(vbox, "SCORE   0", 36, Color(0.9, 0.95, 1.0))
	_best = _label(vbox, "BEST    0", 26, Color(0.6, 0.65, 0.8))

	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 18)
	vbox.add_child(spacer)

	_hint = _label(vbox, "Press  R  to retry", 28, Color(0.8, 0.85, 1.0))


func _label(parent: Node, text: String, size: int, color: Color) -> Label:
	var l := Label.new()
	l.text = text
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	parent.add_child(l)
	return l
