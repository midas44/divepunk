using Godot;

// Config — loads res://settings/settings.cfg at boot and applies display settings.
//
// INI-style file parsed by Godot's native ConfigFile (zero dependencies). Registered
// as the "Config" autoload, so any script can read a value via Config.Instance.GetValue(...).
// See settings/settings.cfg for the available keys and what they do.
public partial class Config : Node
{
	// Autoload global access (set in _Ready, before the main scene / other autoloads' _Ready that use it).
	public static Config Instance { get; private set; }

	private const string ConfigPath = "res://settings/settings.cfg";

	private ConfigFile _cfg = new ConfigFile();
	private bool _loaded = false;

	public override void _Ready()
	{
		Instance = this;
		Error err = _cfg.Load(ConfigPath);
		_loaded = err == Error.Ok;
		if (!_loaded)
			GD.PushWarning($"Config: could not load {ConfigPath} (error {(int)err}) — using built-in defaults.");
		ApplyDisplay();
	}

	// Read a value with a fallback. Used across the project for config-driven knobs.
	public Variant GetValue(string section, string key, Variant fallback) => _cfg.GetValue(section, key, fallback);

	// Typed convenience helpers so the many Game.cs call sites stay clean (settings keys stay snake_case).
	public float GetFloat(string section, string key, float fallback) => _cfg.GetValue(section, key, fallback).AsSingle();
	public int GetInt(string section, string key, int fallback) => _cfg.GetValue(section, key, fallback).AsInt32();
	public bool GetBool(string section, string key, bool fallback) => _cfg.GetValue(section, key, fallback).AsBool();
	public string GetString(string section, string key, string fallback) => _cfg.GetValue(section, key, fallback).AsString();

	private void ApplyDisplay()
	{
		// The frame cap applies in every context, including headless.
		Engine.MaxFps = GetInt("display", "max_fps", 0);

		// Everything below needs a real windowing display server — skip under --headless.
		if (DisplayServer.GetName() == "headless")
			return;

		Window win = GetWindow();
		if (win == null)
			return;

		int w = GetInt("display", "width", 0);
		int h = GetInt("display", "height", 0);
		if (w > 0 && h > 0)
			win.Size = new Vector2I(w, h);

		switch (GetString("display", "window_mode", "windowed").ToLower())
		{
			case "fullscreen":
				win.Mode = Window.ModeEnum.Fullscreen;
				break;
			case "exclusive_fullscreen":
				win.Mode = Window.ModeEnum.ExclusiveFullscreen;
				break;
			case "maximized":
				win.Mode = Window.ModeEnum.Maximized;
				break;
			default:
				win.Mode = Window.ModeEnum.Windowed;
				break;
		}

		bool vsyncOn = GetBool("display", "vsync", true);
		DisplayServer.WindowSetVsyncMode(
			vsyncOn ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled
		);
	}
}
