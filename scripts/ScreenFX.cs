using Godot;

// Fullscreen screen-space FX layer — DIVEPUNK, milestone M4 (juice / "feels fast", spec §7.8).
//
// Hosts three fullscreen passes: speed-line streaks (additive), a chromatic-aberration + vignette
// post pass (reads the composited frame via hint_screen_texture), and a flash overlay for
// near-miss / crash juice. Game.cs creates one, sets the [fx] toggles BEFORE AddChild (so _Ready
// builds the right passes), and feeds it the ship's speed each frame via SetSpeedRatio/SetBoost.
//
// Sits at layer 30: above the 3D scene + speed lines it composites, but below the HUD (50) and the
// Game Over screen (bumped to 100 in Game.cs) so those stay crisp and undistorted.
[GlobalClass]
public partial class ScreenFX : CanvasLayer
{
	private static readonly Shader SpeedLinesShader = GD.Load<Shader>("res://shaders/speed_lines.gdshader");
	private static readonly Shader PostShader = GD.Load<Shader>("res://shaders/post.gdshader");

	[Export] public bool SpeedLinesEnabled = true;
	[Export] public float SpeedLinesStrength = 1.0f;   // overall gain on the streaks
	[Export] public bool PostEnabled = true;           // chromatic aberration + vignette pass
	[Export] public float AberrationAtTop = 1.0f;      // CA amount at full speed ratio
	[Export] public float BoostAberration = 0.6f;      // extra CA while boosting
	[Export] public float RampSharpness = 6.0f;        // how fast the effects ease toward their target
	[Export] public float FlashDecay = 4.0f;           // how fast a flash fades back out

	private ShaderMaterial _linesMat;
	private ShaderMaterial _postMat;
	private ColorRect _flashRect;

	private float _ratio = 0.0f;          // target speed ratio (0..1), from the ship
	private float _boost = 0.0f;          // target boost factor (0 / 1)
	private float _ratioEased = 0.0f;
	private float _boostEased = 0.0f;
	private float _flash = 0.0f;
	private Color _flashColor = new Color(1.0f, 1.0f, 1.0f);

	public override void _Ready()
	{
		Layer = 30;
		// Child order == draw order: speed lines first (additive over the 3D), then the post pass (which
		// reads the already-composited frame), then the flash on top of everything this layer draws.
		if (SpeedLinesEnabled)
			_linesMat = MakeFullscreen(SpeedLinesShader);
		if (PostEnabled)
			_postMat = MakeFullscreen(PostShader);
		_flashRect = new ColorRect();
		_flashRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_flashRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		_flashRect.Color = new Color(1.0f, 1.0f, 1.0f, 0.0f);
		AddChild(_flashRect);
	}

	// Builds a fullscreen ColorRect driven by `shader`; returns its ShaderMaterial for live params.
	private ShaderMaterial MakeFullscreen(Shader shader)
	{
		var rect = new ColorRect();
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		rect.MouseFilter = Control.MouseFilterEnum.Ignore;
		var mat = new ShaderMaterial();
		mat.Shader = shader;
		rect.Material = mat;
		AddChild(rect);
		return mat;
	}

	// Feed the current speed ratio (0..1) — drives speed-line strength + chromatic aberration.
	public void SetSpeedRatio(float r)
	{
		_ratio = Mathf.Clamp(r, 0.0f, 1.0f);
	}

	// Feed whether the ship is boosting — adds an extra chromatic-aberration punch.
	public void SetBoost(bool boosting)
	{
		_boost = boosting ? 1.0f : 0.0f;
	}

	// Pop a screen flash (near-miss / crash juice). amount 0..1 adds to the current flash level.
	public void Flash(float amount) => Flash(amount, new Color(1.0f, 1.0f, 1.0f));

	public void Flash(float amount, Color color)
	{
		_flash = Mathf.Clamp(_flash + amount, 0.0f, 1.0f);
		_flashColor = color;
	}

	public override void _Process(double delta)
	{
		float d = (float)delta;
		// Ease the drivers so the effects glide rather than snap (framerate-independent).
		float k = 1.0f - Mathf.Exp(-RampSharpness * d);
		_ratioEased = Mathf.Lerp(_ratioEased, _ratio, k);
		_boostEased = Mathf.Lerp(_boostEased, _boost, k);

		if (_linesMat != null)
			_linesMat.SetShaderParameter("strength", _ratioEased * SpeedLinesStrength);
		if (_postMat != null)
		{
			float ab = _ratioEased * AberrationAtTop + _boostEased * BoostAberration;
			_postMat.SetShaderParameter("aberration", ab);
		}
		if (_flashRect != null)
		{
			_flash = Mathf.MoveToward(_flash, 0.0f, FlashDecay * d);
			_flashRect.Color = new Color(_flashColor.R, _flashColor.G, _flashColor.B, _flash);
		}
	}
}
