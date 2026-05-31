using Godot;

// In-run HUD — DIVEPUNK, milestone M3. Score + combo multiplier + best (from the
// ScoreManager autoload) and a boost-meter bar (polled from the ship each frame).
// Presentation-only: it reads game state, never mutates it.
[GlobalClass]
public partial class HUD : CanvasLayer
{
	private Ship _ship;            // provides GetBoostMeter()
	private Label _scoreLabel;
	private Label _multLabel;
	private Label _bestLabel;
	private ColorRect _boostFill;
	private ColorRect _boostBg;
	private ColorRect _hspdFill;    // horizontal (forward) speed bar
	private Label _hspdVal;         // ...and its digital m/s readout
	private ColorRect _vspdFill;    // vertical (climb/dive) speed bar
	private Label _vspdVal;         // ...and its digital m/s readout

	private const float BoostBarW = 260.0f;

	public override void _Ready()
	{
		Layer = 50;
		Build();
		ScoreManager sm = ScoreManager.Instance;
		sm.ScoreChanged += OnScoreChanged;
		sm.HighScoreChanged += OnHighScoreChanged;
		sm.NearMissRegistered += OnNearMiss;
		OnScoreChanged(sm.GetScore(), sm.Multiplier);
		OnHighScoreChanged(sm.HighScore);
	}

	public void SetShip(Ship s)
	{
		_ship = s;
	}

	public override void _Process(double delta)
	{
		if (_ship == null)
			return;
		float m = _ship.GetBoostMeter();
		_boostFill.Size = new Vector2(BoostBarW * Mathf.Clamp(m, 0.0f, 1.0f), _boostFill.Size.Y);
		// Bar tints toward hot as it fills, dims when nearly empty.
		_boostFill.Color = m > 0.15f ? new Color(0.1f, 0.9f, 1.0f) : new Color(0.6f, 0.3f, 0.3f);

		float sp = _ship.GetSpeed();
		float top = _ship.GetTopSpeed();
		_hspdFill.Size = new Vector2(BoostBarW * Mathf.Clamp(sp / Mathf.Max(top, 0.001f), 0.0f, 1.0f), _hspdFill.Size.Y);
		_hspdVal.Text = $"{sp:F0} m/s";

		float vs = _ship.GetVerticalSpeed();
		float vmax = _ship.GetMaxVerticalSpeed();
		_vspdFill.Size = new Vector2(BoostBarW * Mathf.Clamp(Mathf.Abs(vs) / Mathf.Max(vmax, 0.001f), 0.0f, 1.0f), _vspdFill.Size.Y);
		_vspdVal.Text = $"{vs:+0;-0} m/s";
		// Climb tints green, dive tints orange, near-level is dim.
		if (vs > 1.0f)
			_vspdFill.Color = new Color(0.3f, 0.9f, 0.5f);
		else if (vs < -1.0f)
			_vspdFill.Color = new Color(0.95f, 0.55f, 0.2f);
		else
			_vspdFill.Color = new Color(0.4f, 0.45f, 0.55f);
	}

	private void OnScoreChanged(int score, float multiplier)
	{
		_scoreLabel.Text = score.ToString("D8");
		_multLabel.Text = $"x{multiplier:F1}";
		// Emphasise a live combo.
		_multLabel.AddThemeColorOverride("font_color",
			multiplier > 1.05f ? new Color(1.0f, 0.85f, 0.2f) : new Color(0.5f, 0.55f, 0.65f));
	}

	private void OnHighScoreChanged(int highScore)
	{
		_bestLabel.Text = $"BEST  {highScore:D8}";
	}

	private void OnNearMiss(float multiplier)
	{
		// A quick pop on the multiplier label for juice (deepened in M4).
		_multLabel.Scale = new Vector2(1.4f, 1.4f);
		CreateTween().TweenProperty(_multLabel, "scale", Vector2.One, 0.25);
	}

	private void Build()
	{
		var root = new Control();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(root);

		// Score (top-left) + multiplier beneath it.
		_scoreLabel = new Label();
		_scoreLabel.Position = new Vector2(28, 20);
		_scoreLabel.AddThemeFontSizeOverride("font_size", 44);
		_scoreLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.95f, 1.0f));
		root.AddChild(_scoreLabel);

		_multLabel = new Label();
		_multLabel.Position = new Vector2(30, 74);
		_multLabel.PivotOffset = new Vector2(0, 16);
		_multLabel.AddThemeFontSizeOverride("font_size", 32);
		root.AddChild(_multLabel);

		// Best (top-right).
		_bestLabel = new Label();
		_bestLabel.AnchorLeft = 1.0f;
		_bestLabel.AnchorRight = 1.0f;
		_bestLabel.Position = new Vector2(-260, 28);
		_bestLabel.AddThemeFontSizeOverride("font_size", 24);
		_bestLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.8f));
		root.AddChild(_bestLabel);

		// Boost meter (bottom-left): label + background + fill.
		var boostLabel = new Label();
		boostLabel.AnchorTop = 1.0f;
		boostLabel.AnchorBottom = 1.0f;
		boostLabel.Position = new Vector2(28, -64);
		boostLabel.Text = "THROTTLE";
		boostLabel.AddThemeFontSizeOverride("font_size", 18);
		boostLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.8f));
		root.AddChild(boostLabel);

		_boostBg = new ColorRect();
		_boostBg.AnchorTop = 1.0f;
		_boostBg.AnchorBottom = 1.0f;
		_boostBg.Position = new Vector2(28, -40);
		_boostBg.Size = new Vector2(BoostBarW, 16);
		_boostBg.Color = new Color(0.1f, 0.12f, 0.18f, 0.85f);
		root.AddChild(_boostBg);

		_boostFill = new ColorRect();
		_boostFill.AnchorTop = 1.0f;
		_boostFill.AnchorBottom = 1.0f;
		_boostFill.Position = new Vector2(28, -40);
		_boostFill.Size = new Vector2(0, 16);
		_boostFill.Color = new Color(0.1f, 0.9f, 1.0f);
		root.AddChild(_boostFill);

		// Speed indicators stacked above the boost meter: horizontal (forward) and vertical
		// (climb/dive), each a bar + a digital m/s readout.
		var hspd = MakeMeter(root, -100.0f, "SPD", new Color(0.3f, 0.9f, 0.5f));
		_hspdFill = hspd.Fill;
		_hspdVal = hspd.Val;
		var vspd = MakeMeter(root, -156.0f, "V-SPD", new Color(0.4f, 0.45f, 0.55f));
		_vspdFill = vspd.Fill;
		_vspdVal = vspd.Val;
	}

	// Builds one labelled bar + digital readout in the bottom-left stack (barY is measured up from
	// the bottom edge). Returns (fill, val) so the caller can drive the fill width + number each frame.
	private (ColorRect Fill, Label Val) MakeMeter(Control root, float barY, string labelText, Color barColor)
	{
		var nameLbl = new Label();
		nameLbl.AnchorTop = 1.0f;
		nameLbl.AnchorBottom = 1.0f;
		nameLbl.Position = new Vector2(28, barY - 24);
		nameLbl.Text = labelText;
		nameLbl.AddThemeFontSizeOverride("font_size", 18);
		nameLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.8f));
		root.AddChild(nameLbl);

		var bg = new ColorRect();
		bg.AnchorTop = 1.0f;
		bg.AnchorBottom = 1.0f;
		bg.Position = new Vector2(28, barY);
		bg.Size = new Vector2(BoostBarW, 16);
		bg.Color = new Color(0.1f, 0.12f, 0.18f, 0.85f);
		root.AddChild(bg);

		var fill = new ColorRect();
		fill.AnchorTop = 1.0f;
		fill.AnchorBottom = 1.0f;
		fill.Position = new Vector2(28, barY);
		fill.Size = new Vector2(0, 16);
		fill.Color = barColor;
		root.AddChild(fill);

		var val = new Label();
		val.AnchorTop = 1.0f;
		val.AnchorBottom = 1.0f;
		val.Position = new Vector2(28 + BoostBarW + 12, barY - 4);
		val.AddThemeFontSizeOverride("font_size", 20);
		val.AddThemeColorOverride("font_color", new Color(0.85f, 0.95f, 1.0f));
		root.AddChild(val);

		return (fill, val);
	}
}
