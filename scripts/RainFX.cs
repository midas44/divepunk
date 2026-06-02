using Godot;

// Fullscreen "lens rain" overlay — DIVEPUNK, Task 6 (user-requested cyberpunk scope extension).
//
// A single procedural rain pass (shaders/rain.gdshader): cool desaturated streaks on the camera glass that
// SHEAR BACK and intensify with flight speed. Mirrors ScreenFX's pattern (a fullscreen ColorRect + a
// ShaderMaterial, with an eased driver pushed each frame in _Process) but is a SEPARATE node so the existing
// screen-FX (ScreenFX / post.gdshader / speed_lines.gdshader) is untouched. Game.cs sets Enabled/Strength
// BEFORE AddChild (so _Ready builds the pass) and feeds it the ship's speed ratio each frame via SetSpeedRatio.
//
// Sits at layer 31: ABOVE the chromatic-aberration/vignette post pass (30, which re-presents the frame) so the
// rain composites cleanly on the finished image; BELOW the HUD (50) so the telemetry stays crisp and un-rained.
[GlobalClass]
public partial class RainFX : CanvasLayer
{
	private static readonly Shader RainShader = GD.Load<Shader>("res://shaders/rain.gdshader");

	[Export] public bool Enabled = true;            // [fx] rain — default true so it renders out of the box
	[Export] public float Strength = 1.0f;          // [fx] rain_strength — master gain on the streaks
	[Export] public float RampSharpness = 5.0f;     // how fast the speed driver eases (framerate-independent)

	private ShaderMaterial _mat;
	private float _ratio = 0.0f;                     // target speed ratio (0..1), from the ship
	private float _ratioEased = 0.0f;

	public override void _Ready()
	{
		Layer = 31;
		if (!Enabled)
			return;
		var rect = new ColorRect();
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		rect.MouseFilter = Control.MouseFilterEnum.Ignore;
		_mat = new ShaderMaterial { Shader = RainShader };
		_mat.SetShaderParameter("rain_strength", Strength);
		rect.Material = _mat;
		AddChild(rect);
	}

	// Feed the current speed ratio (0..1) — shears the streaks back + intensifies them (eased in _Process).
	public void SetSpeedRatio(float r) => _ratio = Mathf.Clamp(r, 0.0f, 1.0f);

	public override void _Process(double delta)
	{
		if (_mat == null)
			return;
		float d = (float)delta;
		float k = 1.0f - Mathf.Exp(-RampSharpness * d);   // framerate-independent easing (CLAUDE.md rule)
		_ratioEased = Mathf.Lerp(_ratioEased, _ratio, k);
		_mat.SetShaderParameter("speed", _ratioEased);
	}
}
