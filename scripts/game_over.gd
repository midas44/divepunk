class_name GameOverScreen
extends CanvasLayer
## Game-over overlay — DIVEPUNK, milestone M3.
##
## Shown when the ship crashes. M3a: title + retry hint. M3b enriches show_over() with the
## final score, the local best, and a "new best!" flourish. Restart itself is handled by
## game.gd (the `restart` action reloads the scene) so the overlay stays presentation-only —
## one tap / key and you're instantly back in, which is the whole "one more go" loop.

var _title: Label
var _hint: Label


func _ready() -> void:
	layer = 100          # draw above everything
	_build()
	visible = false


func show_over() -> void:
	visible = true


func _build() -> void:
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(root)

	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.55)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(center)

	var vbox := VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", 18)
	center.add_child(vbox)

	_title = Label.new()
	_title.text = "CRASHED"
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_title.add_theme_font_size_override("font_size", 72)
	_title.add_theme_color_override("font_color", Color(1.0, 0.3, 0.25))
	vbox.add_child(_title)

	_hint = Label.new()
	_hint.text = "Press  R  to retry"
	_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hint.add_theme_font_size_override("font_size", 28)
	_hint.add_theme_color_override("font_color", Color(0.8, 0.85, 1.0))
	vbox.add_child(_hint)
