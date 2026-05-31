using Godot;

// ScoreManager — DIVEPUNK, milestone M3 (autoload singleton "ScoreManager").
//
// score = distance + Σ(near_miss_value × multiplier)   (spec §7.6)
// The multiplier climbs with each chained near-miss and decays back toward 1.0 after
// combo_timeout seconds without one (the "don't break the chain" tension). High score is
// persisted locally to user://highscore.save.
//
// Game.cs drives this each frame (SetDistance + Tick) and forwards the ship's NearMiss
// signal to RegisterNearMiss(), so the manager stays decoupled from the ship node.
public partial class ScoreManager : Node
{
	// Autoload global access (C# has no GDScript-style autoload globals). Set in _Ready().
	public static ScoreManager Instance { get; private set; }

	private const string SavePath = "user://highscore.save";

	[ExportGroup("Scoring")]
	[Export] public float NearMissValue = 50.0f;   // base points per near-miss (before multiplier)
	[Export] public float ComboStep = 0.5f;        // multiplier added per near-miss
	[Export] public float ComboMax = 9.0f;         // multiplier ceiling
	[Export] public float ComboTimeout = 2.5f;     // seconds without a near-miss before decay starts
	[Export] public float ComboDecay = 2.0f;       // multiplier units shed per second once decaying

	[Signal] public delegate void ScoreChangedEventHandler(int score, float multiplier);
	[Signal] public delegate void NearMissRegisteredEventHandler(float multiplier);
	[Signal] public delegate void HighScoreChangedEventHandler(int highScore);

	public float Distance = 0.0f;
	public float NearPoints = 0.0f;
	public float Multiplier = 1.0f;
	public int HighScore = 0;

	private float _sinceNear = 0.0f;
	private bool _runActive = true;

	public override void _Ready()
	{
		Instance = this;
		HighScore = LoadHighScore();
		EmitSignal(SignalName.HighScoreChanged, HighScore);
	}

	// Reset for a fresh run (the scene reload does this via _Ready, but kept explicit for clarity).
	public void ResetRun()
	{
		Distance = 0.0f;
		NearPoints = 0.0f;
		Multiplier = 1.0f;
		_sinceNear = 0.0f;
		_runActive = true;
		EmitSignal(SignalName.ScoreChanged, GetScore(), Multiplier);
	}

	public void Tick(double delta)
	{
		if (!_runActive)
			return;
		// Decay the multiplier back toward 1.0 once the combo window lapses.
		_sinceNear += (float)delta;
		if (_sinceNear > ComboTimeout && Multiplier > 1.0f)
			Multiplier = Mathf.Max(1.0f, Multiplier - ComboDecay * (float)delta);
		EmitSignal(SignalName.ScoreChanged, GetScore(), Multiplier);
	}

	// Forward distance in metres (the ship travels toward -Z, so distance = -z).
	public void SetDistance(float d)
	{
		Distance = Mathf.Max(0.0f, d);
	}

	public void RegisterNearMiss()
	{
		if (!_runActive)
			return;
		NearPoints += NearMissValue * Multiplier;
		Multiplier = Mathf.Min(ComboMax, Multiplier + ComboStep);
		_sinceNear = 0.0f;
		EmitSignal(SignalName.NearMissRegistered, Multiplier);
		EmitSignal(SignalName.ScoreChanged, GetScore(), Multiplier);
	}

	public int GetScore()
	{
		return (int)(Distance + NearPoints);
	}

	// Ends the run, commits the high score, returns true if it was a new best.
	public bool EndRun()
	{
		_runActive = false;
		int final = GetScore();
		bool isBest = final > HighScore;
		if (isBest)
		{
			HighScore = final;
			SaveHighScore(HighScore);
			EmitSignal(SignalName.HighScoreChanged, HighScore);
		}
		return isBest;
	}

	private int LoadHighScore()
	{
		if (!FileAccess.FileExists(SavePath))
			return 0;
		FileAccess f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		if (f == null)
			return 0;
		uint v = f.Get32();
		f.Close();
		return (int)v;
	}

	private void SaveHighScore(int value)
	{
		FileAccess f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		if (f == null)
		{
			GD.PushWarning($"ScoreManager: could not write {SavePath}");
			return;
		}
		f.Store32((uint)Mathf.Max(0, value));
		f.Close();
	}
}
