class_name ScreenFX
extends CanvasLayer
## Fullscreen screen-space FX layer — DIVEPUNK, milestone M4 (juice / "feels fast", spec §7.8).
##
## Hosts three fullscreen passes: speed-line streaks (additive), a chromatic-aberration + vignette
## post pass (reads the composited frame via hint_screen_texture), and a flash overlay for
## near-miss / crash juice. game.gd creates one, sets the [fx] toggles BEFORE add_child (so _ready
## builds the right passes), and feeds it the ship's speed each frame via set_speed_ratio/set_boost.
##
## Sits at layer 30: above the 3D scene + speed lines it composites, but below the HUD (50) and the
## Game Over screen (bumped to 100 in game.gd) so those stay crisp and undistorted.

const SPEED_LINES_SHADER := preload("res://shaders/speed_lines.gdshader")
const POST_SHADER := preload("res://shaders/post.gdshader")

@export var speed_lines_enabled: bool = true
@export var speed_lines_strength: float = 1.0   ## overall gain on the streaks
@export var post_enabled: bool = true           ## chromatic aberration + vignette pass
@export var aberration_at_top: float = 1.0      ## CA amount at full speed ratio
@export var boost_aberration: float = 0.6       ## extra CA while boosting
@export var ramp_sharpness: float = 6.0         ## how fast the effects ease toward their target
@export var flash_decay: float = 4.0            ## how fast a flash fades back out

var _lines_mat: ShaderMaterial
var _post_mat: ShaderMaterial
var _flash_rect: ColorRect

var _ratio: float = 0.0          ## target speed ratio (0..1), from the ship
var _boost: float = 0.0          ## target boost factor (0 / 1)
var _ratio_eased: float = 0.0
var _boost_eased: float = 0.0
var _flash: float = 0.0
var _flash_color: Color = Color(1.0, 1.0, 1.0)


func _ready() -> void:
	layer = 30
	# Child order == draw order: speed lines first (additive over the 3D), then the post pass (which
	# reads the already-composited frame), then the flash on top of everything this layer draws.
	if speed_lines_enabled:
		_lines_mat = _make_fullscreen(SPEED_LINES_SHADER)
	if post_enabled:
		_post_mat = _make_fullscreen(POST_SHADER)
	_flash_rect = ColorRect.new()
	_flash_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	_flash_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_flash_rect.color = Color(1.0, 1.0, 1.0, 0.0)
	add_child(_flash_rect)


## Builds a fullscreen ColorRect driven by `shader`; returns its ShaderMaterial for live params.
func _make_fullscreen(shader: Shader) -> ShaderMaterial:
	var rect := ColorRect.new()
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var mat := ShaderMaterial.new()
	mat.shader = shader
	rect.material = mat
	add_child(rect)
	return mat


## Feed the current speed ratio (0..1) — drives speed-line strength + chromatic aberration.
func set_speed_ratio(r: float) -> void:
	_ratio = clampf(r, 0.0, 1.0)


## Feed whether the ship is boosting — adds an extra chromatic-aberration punch.
func set_boost(boosting: bool) -> void:
	_boost = 1.0 if boosting else 0.0


## Pop a screen flash (near-miss / crash juice). amount 0..1 adds to the current flash level.
func flash(amount: float, color: Color = Color(1.0, 1.0, 1.0)) -> void:
	_flash = clampf(_flash + amount, 0.0, 1.0)
	_flash_color = color


func _process(delta: float) -> void:
	# Ease the drivers so the effects glide rather than snap (framerate-independent).
	var k: float = 1.0 - exp(-ramp_sharpness * delta)
	_ratio_eased = lerpf(_ratio_eased, _ratio, k)
	_boost_eased = lerpf(_boost_eased, _boost, k)

	if _lines_mat != null:
		_lines_mat.set_shader_parameter("strength", _ratio_eased * speed_lines_strength)
	if _post_mat != null:
		var ab: float = _ratio_eased * aberration_at_top + _boost_eased * boost_aberration
		_post_mat.set_shader_parameter("aberration", ab)
	if _flash_rect != null:
		_flash = move_toward(_flash, 0.0, flash_decay * delta)
		_flash_rect.color = Color(_flash_color.r, _flash_color.g, _flash_color.b, _flash)
