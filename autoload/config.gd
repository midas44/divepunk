extends Node
## Config — loads res://config/settings.cfg at boot and applies display settings.
##
## INI-style file parsed by Godot's native ConfigFile (zero dependencies). Registered
## as the "Config" autoload, so any script can read a value via Config.get_value(...).
## See config/settings.cfg for the available keys and what they do.

const CONFIG_PATH := "res://config/settings.cfg"

var _cfg := ConfigFile.new()
var _loaded: bool = false


func _ready() -> void:
	var err := _cfg.load(CONFIG_PATH)
	_loaded = err == OK
	if not _loaded:
		push_warning("Config: could not load %s (error %d) — using built-in defaults." % [CONFIG_PATH, err])
	_apply_display()


## Read a value with a fallback. Used across the project for config-driven knobs.
func get_value(section: String, key: String, default: Variant) -> Variant:
	return _cfg.get_value(section, key, default)


func _apply_display() -> void:
	# The frame cap applies in every context, including headless.
	Engine.max_fps = int(get_value("display", "max_fps", 0))

	# Everything below needs a real windowing display server — skip under --headless.
	if DisplayServer.get_name() == "headless":
		return

	var win := get_window()
	if win == null:
		return

	var w := int(get_value("display", "width", 0))
	var h := int(get_value("display", "height", 0))
	if w > 0 and h > 0:
		win.size = Vector2i(w, h)

	match str(get_value("display", "window_mode", "windowed")).to_lower():
		"fullscreen":
			win.mode = Window.MODE_FULLSCREEN
		"exclusive_fullscreen":
			win.mode = Window.MODE_EXCLUSIVE_FULLSCREEN
		"maximized":
			win.mode = Window.MODE_MAXIMIZED
		_:
			win.mode = Window.MODE_WINDOWED

	var vsync_on := bool(get_value("display", "vsync", true))
	DisplayServer.window_set_vsync_mode(
		DisplayServer.VSYNC_ENABLED if vsync_on else DisplayServer.VSYNC_DISABLED
	)
