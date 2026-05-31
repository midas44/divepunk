using Godot;

// Game-over overlay — DIVEPUNK, milestone M3.
//
// Shown when the ship crashes: final score, local best, a "NEW BEST!" flourish, and the
// retry hint. Restart itself is handled by Game.cs (the `restart` action reloads the scene)
// so the overlay stays presentation-only — one key and you're instantly back in, which is
// the whole "one more go" loop.
[GlobalClass]
public partial class GameOver : CanvasLayer
{
	private Label _title;
	private Label _score;
	private Label _best;
	private Label _newbest;
	private Label _hint;

	public override void _Ready()
	{
		Layer = 100;          // draw above the HUD and everything else
		Build();
		Visible = false;
	}

	public void ShowOver(int finalScore, int highScore, bool isNewBest)
	{
		_score.Text = $"SCORE   {finalScore}";
		_best.Text = $"BEST    {highScore}";
		_newbest.Visible = isNewBest;
		Visible = true;
	}

	private void Build()
	{
		var dim = new ColorRect();
		dim.Color = new Color(0.0f, 0.0f, 0.0f, 0.55f);
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		dim.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(dim);

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		center.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(center);

		var vbox = new VBoxContainer();
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddThemeConstantOverride("separation", 14);
		center.AddChild(vbox);

		_title = MakeLabel(vbox, "CRASHED", 72, new Color(1.0f, 0.3f, 0.25f));

		_newbest = MakeLabel(vbox, "★ NEW BEST! ★", 34, new Color(1.0f, 0.85f, 0.2f));
		_newbest.Visible = false;

		_score = MakeLabel(vbox, "SCORE   0", 36, new Color(0.9f, 0.95f, 1.0f));
		_best = MakeLabel(vbox, "BEST    0", 26, new Color(0.6f, 0.65f, 0.8f));

		var spacer = new Control();
		spacer.CustomMinimumSize = new Vector2(0, 18);
		vbox.AddChild(spacer);

		_hint = MakeLabel(vbox, "Press  R  to retry", 28, new Color(0.8f, 0.85f, 1.0f));
	}

	private Label MakeLabel(Node parent, string text, int size, Color color)
	{
		var l = new Label();
		l.Text = text;
		l.HorizontalAlignment = HorizontalAlignment.Center;
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", color);
		parent.AddChild(l);
		return l;
	}
}
