using Godot;

// Root bootstrap — DIVEPUNK, milestones M0–M3.
//
// Attach to the root Node3D of Main.tscn and press Play. It self-assembles a runnable
// scene (ship + chase camera + minimal night environment + streaming city + UI), registers
// the input actions in code (so the project runs with zero manual setup), and handles
// restart and the crash -> game-over flow.
//
// As you build real, hand-authored scenes you can delete the auto-spawn helpers below
// and place the nodes directly in the scene tree instead.
public partial class Game : Node3D
{
	private static readonly PackedScene ChunkManagerScene = GD.Load<PackedScene>("res://scenes/world/ChunkManager.tscn");
	private static readonly PackedScene GameOverScene = GD.Load<PackedScene>("res://scenes/ui/GameOver.tscn");
	private static readonly PackedScene HudScene = GD.Load<PackedScene>("res://scenes/ui/HUD.tscn");
	private static readonly Shader SkyShader = GD.Load<Shader>("res://shaders/sky.gdshader");   // procedural neon cloud sky (M4 view pass)

	[ExportGroup("Juice")]
	[Export] public float ShakeOnNearMiss = 0.25f;
	[Export] public float ShakeOnCrash = 1.0f;
	[Export] public float ShakeOnBoost = 0.35f;            // camera punch the moment a boost kicks in
	[Export] public bool EnableNearMissFlash = true;       // screen flash on a near-miss (from [fx] near_miss_flash)
	[Export] public float NearMissFlashAmount = 0.35f;     // strength of that flash (0..1)
	[Export] public float CrashFlashAmount = 1.0f;         // red flash on a crash
	[Export] public float NearMissTimeScale = 0.9f;        // brief slow-mo factor on a near-miss (1.0 = off; from [fx] time_dilation)
	[Export] public float TimeDilationDuration = 0.12f;    // seconds (real time) the slow-mo holds before easing back
	[Export] public float TimeDilationCooldown = 0.35f;    // min real seconds between dips, so a chain can't lock slow-mo on

	private Ship _ship;
	private CameraRig _rig;
	private ChunkManager _mgr;
	private Traffic _traffic;
	private ScreenFX _fx;
	private GameOver _gameOver;
	private Hud _hud;
	private MeshInstance3D _ground;

	private bool _wasBoosting = false;
	private float _tdTimer = 0.0f;       // remaining real-time slow-mo (s)
	private float _tdCooldown = 0.0f;    // remaining real-time cooldown before another dip (s)

	public override void _Ready()
	{
		RegisterInput();
		Engine.TimeScale = 1.0;   // defensive: a prior run may have left a slow-mo dip active
		EnableNearMissFlash = CfgBool("fx", "near_miss_flash", EnableNearMissFlash);
		NearMissTimeScale = CfgFloat("fx", "time_dilation", NearMissTimeScale);
		CaptureMouse();
		EnsureEnvironment();
		SpawnShipAndCamera();
		SpawnWorld();
		SpawnTraffic();
		SpawnUi();
		SpawnScreenFx();
		AudioManager.Instance?.StartMusic();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("quit"))
			GetTree().Quit();
		else if (@event.IsActionPressed("toggle_fullscreen"))
			ToggleFullscreen();
		else if (@event.IsActionPressed("restart"))
			GetTree().ReloadCurrentScene();
	}

	// Hide + lock the cursor so mouse motion drives the free-look camera (skipped under --headless).
	private void CaptureMouse()
	{
		if (DisplayServer.GetName() == "headless")
			return;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	// Flip between fullscreen and windowed (the `toggle_fullscreen` / F action). Works regardless
	// of the boot mode set in settings/settings.cfg.
	private void ToggleFullscreen()
	{
		Window win = GetWindow();
		if (win == null)
			return;
		bool isFs = win.Mode == Window.ModeEnum.Fullscreen || win.Mode == Window.ModeEnum.ExclusiveFullscreen;
		win.Mode = isFs ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
	}

	private void SpawnShipAndCamera()
	{
		_ship = GetNodeOrNull<Ship>("Ship");
		if (_ship == null)
		{
			_ship = new Ship { Name = "Ship" };
			ApplyShipConfig(_ship);      // apply config exports BEFORE AddChild so _Ready() initialises with them
			AddChild(_ship);
			_ship.GlobalPosition = new Vector3(0.0f, 30.0f, 0.0f);
		}

		_rig = GetNodeOrNull<CameraRig>("CameraRig");
		if (_rig == null)
		{
			_rig = new CameraRig { Name = "CameraRig" };
			ApplyCameraConfig(_rig);     // apply config exports BEFORE AddChild so _Ready() builds levels with them
			AddChild(_rig);
		}

		_rig.SetTarget(_ship);
		_ship.Crashed += OnShipCrashed;
		_ship.NearMiss += OnShipNearMiss;
		_ship.SpeedChanged += OnShipSpeedChanged;
	}

	// Spawns the M2 streaming city around the ship. World seed + debug flags come from the
	// Config autoload (edit settings/settings.cfg to change them).
	private void SpawnWorld()
	{
		if (GetNodeOrNull("ChunkManager") != null)
			return;
		_mgr = ChunkManagerScene.Instantiate<ChunkManager>();
		_mgr.Name = "ChunkManager";
		_mgr.WorldSeed = CfgInt("game", "seed", 0);
		_mgr.LogStreaming = CfgBool("debug", "log_streaming", false);
		_mgr.WorldScale = CfgFloat("game", "scale", 2.0f);
		_mgr.CorridorHalfWidth = CfgFloat("corridor", "half_width", _mgr.CorridorHalfWidth);
		_mgr.CorridorFloor = CfgFloat("corridor", "floor", _mgr.CorridorFloor);
		_mgr.CorridorCeiling = CfgFloat("corridor", "ceiling", _mgr.CorridorCeiling);
		// Streaming / draw distance (the "view area" lever): chunks_ahead × chunk_length metres of city.
		// Set before AddChild so _Ready() sizes the chunk pool from them.
		_mgr.ChunkLength = CfgFloat("streaming", "chunk_length", _mgr.ChunkLength);
		_mgr.ChunksAhead = CfgInt("streaming", "chunks_ahead", _mgr.ChunksAhead);
		_mgr.ChunksBehind = CfgInt("streaming", "chunks_behind", _mgr.ChunksBehind);
		_mgr.BuildsPerFrame = CfgInt("streaming", "builds_per_frame", _mgr.BuildsPerFrame);
		_mgr.BuildingWindows = CfgBool("fx", "building_windows", _mgr.BuildingWindows);
		_mgr.HazardPulse = CfgBool("fx", "hazard_pulse", _mgr.HazardPulse);
		AddChild(_mgr);
		_mgr.SetTarget(_ship);
	}

	// Spawns the moving traffic (a live hazard) once the ship exists. Config lives in
	// settings.cfg [traffic]; corridor extents come from [corridor]; the seed from [game].
	private void SpawnTraffic()
	{
		if (GetNodeOrNull("Traffic") != null)
			return;
		_traffic = new Traffic { Name = "Traffic" };
		ApplyTrafficConfig(_traffic);   // config exports BEFORE AddChild so _Ready() builds the pool with them
		AddChild(_traffic);
		_traffic.SetTarget(_ship);
	}

	private void ApplyTrafficConfig(Traffic t)
	{
		t.CarCount = CfgInt("traffic", "count", t.CarCount);
		t.MinSpeed = CfgFloat("traffic", "min_speed", t.MinSpeed);
		t.MaxSpeed = CfgFloat("traffic", "max_speed", t.MaxSpeed);
		t.TowardFraction = CfgFloat("traffic", "toward_fraction", t.TowardFraction);
		t.CorridorHalfWidth = CfgFloat("corridor", "half_width", t.CorridorHalfWidth);
		t.CorridorFloor = CfgFloat("corridor", "floor", t.CorridorFloor);
		t.CorridorCeiling = CfgFloat("corridor", "ceiling", t.CorridorCeiling);
		t.HazardPulse = CfgBool("fx", "hazard_pulse", t.HazardPulse);
		t.WorldSeed = CfgInt("game", "seed", 0);
	}

	// Spawns the UI overlays: the in-run HUD and the Game Over screen. A fresh ScoreManager
	// run is started here so a scene reload (restart) zeroes the score.
	private void SpawnUi()
	{
		if (GetNodeOrNull("HUD") != null)
			return;
		ScoreManager.Instance.ResetRun();

		_hud = HudScene.Instantiate<Hud>();
		_hud.Name = "HUD";
		AddChild(_hud);
		_hud.SetShip(_ship);

		_gameOver = GameOverScene.Instantiate<GameOver>();
		_gameOver.Name = "GameOver";
		_gameOver.Layer = 100;   // above the screen-FX (30) and HUD (50) layers so it's never distorted
		AddChild(_gameOver);
	}

	// Spawns the fullscreen screen-FX layer (speed lines + chromatic aberration/vignette). Toggles
	// come from settings.cfg [fx]; it's fed the ship's speed via the SpeedChanged signal.
	private void SpawnScreenFx()
	{
		if (GetNodeOrNull("ScreenFX") != null)
			return;
		_fx = new ScreenFX { Name = "ScreenFX" };
		_fx.SpeedLinesEnabled = CfgBool("fx", "speed_lines", true);
		_fx.SpeedLinesStrength = CfgFloat("fx", "speed_lines_strength", 1.0f);
		_fx.PostEnabled = CfgBool("fx", "chromatic_aberration", true);
		AddChild(_fx);
	}

	// Forwards the ship's per-frame speed to the screen-FX layer (speed lines + aberration ramp), and
	// punches the camera + plays the thruster whoosh the instant you start throttling up (rising edge).
	private void OnShipSpeedChanged(float speed, float ratio, bool accelerating)
	{
		if (_fx != null)
		{
			_fx.SetSpeedRatio(ratio);
			_fx.SetBoost(accelerating);
		}
		if (accelerating && !_wasBoosting)
		{
			if (_rig != null)
				_rig.AddShake(ShakeOnBoost);
			AudioManager.Instance?.Boost();
		}
		_wasBoosting = accelerating;
	}

	public override void _Process(double delta)
	{
		// Drive the score: distance is how far the ship has flown (-Z), plus the decaying combo.
		if (_ship != null)
		{
			ScoreManager.Instance.SetDistance(-_ship.GlobalPosition.Z);
			// Slide the (uniform) reflective ground with the ship so the now-long, thin-fog view never reaches its edge.
			if (_ground != null)
			{
				Vector3 gp = _ground.GlobalPosition;
				gp.Z = _ship.GlobalPosition.Z;
				_ground.GlobalPosition = gp;
			}
		}
		ScoreManager.Instance.Tick(delta);
		// Ease any active near-miss slow-mo, using REAL time (recovered from the scaled frame delta).
		UpdateTimeDilation(delta / System.Math.Max(Engine.TimeScale, 0.001));
	}

	private void OnShipNearMiss()
	{
		ScoreManager.Instance.RegisterNearMiss();
		if (_rig != null)
			_rig.AddShake(ShakeOnNearMiss);
		if (_fx != null && EnableNearMissFlash)
			_fx.Flash(NearMissFlashAmount, new Color(0.5f, 0.9f, 1.0f));
		TriggerTimeDilation();
		AudioManager.Instance?.NearMiss();
	}

	private void OnShipCrashed()
	{
		bool isBest = ScoreManager.Instance.EndRun();
		GD.Print($"[DIVEPUNK] crashed — score {ScoreManager.Instance.GetScore()} (best {ScoreManager.Instance.HighScore}{(isBest ? ", NEW BEST" : "")})");
		if (_rig != null)
			_rig.AddShake(ShakeOnCrash);
		if (_fx != null)
		{
			_fx.Flash(CrashFlashAmount, new Color(1.0f, 0.3f, 0.2f));   // red impact flash
			_fx.SetSpeedRatio(0.0f);                                    // kill the speed lines / aberration
			_fx.SetBoost(false);
		}
		ResetTimeScale();                                               // the game-over screen runs at normal speed
		AudioManager.Instance?.Crash();
		if (_gameOver != null)
			_gameOver.ShowOver(ScoreManager.Instance.GetScore(), ScoreManager.Instance.HighScore, isBest);
	}

	// Brief "bullet-time" dip on a near-miss (juice, spec §5.2). Bounded by a cooldown so a fast
	// near-miss chain can't lock the game in slow-mo. NearMissTimeScale = 1.0 disables it.
	private void TriggerTimeDilation()
	{
		if (NearMissTimeScale >= 0.999f || _tdCooldown > 0.0f)
			return;
		_tdTimer = TimeDilationDuration;
		_tdCooldown = TimeDilationCooldown;
	}

	// Holds Engine.TimeScale at the dip for its duration, then eases back to 1.0. Driven with REAL
	// delta (passed in) so its own timing is independent of the slow-mo it applies.
	private void UpdateTimeDilation(double realDelta)
	{
		float rd = (float)realDelta;
		if (_tdCooldown > 0.0f)
			_tdCooldown = Mathf.Max(0.0f, _tdCooldown - rd);
		if (_tdTimer > 0.0f)
		{
			_tdTimer = Mathf.Max(0.0f, _tdTimer - rd);
			Engine.TimeScale = NearMissTimeScale;
		}
		else if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
		{
			Engine.TimeScale = Mathf.Lerp((float)Engine.TimeScale, 1.0f, 1.0f - Mathf.Exp(-12.0f * rd));
			if (Mathf.Abs((float)Engine.TimeScale - 1.0f) < 0.01f)
				Engine.TimeScale = 1.0;
		}
	}

	// Snap time back to normal (on crash / before the game-over screen).
	private void ResetTimeScale()
	{
		_tdTimer = 0.0f;
		_tdCooldown = 0.0f;
		Engine.TimeScale = 1.0;
	}

	// Safe read from the Config autoload (falls back to the default if it isn't present).
	private float CfgFloat(string section, string key, float fallback) => Config.Instance != null ? Config.Instance.GetFloat(section, key, fallback) : fallback;
	private int CfgInt(string section, string key, int fallback) => Config.Instance != null ? Config.Instance.GetInt(section, key, fallback) : fallback;
	private bool CfgBool(string section, string key, bool fallback) => Config.Instance != null ? Config.Instance.GetBool(section, key, fallback) : fallback;

	// Pushes the config-driven ship tunables onto the ship before it enters the tree, so _Ready()
	// initialises with them. The speed model lives in settings.cfg [ship]; the flyable corridor's
	// horizontal half-width and vertical floor/ceiling live in [corridor]. Each falls back to the
	// ship's own export default when the key is absent.
	private void ApplyShipConfig(Ship ship)
	{
		ship.BaseSpeed = CfgFloat("ship", "base_speed", ship.BaseSpeed);
		ship.MinSpeed = CfgFloat("ship", "min_speed", ship.MinSpeed);
		ship.MaxSpeed = CfgFloat("ship", "max_speed", ship.MaxSpeed);
		ship.AccelerateRate = CfgFloat("ship", "accelerate_rate", ship.AccelerateRate);
		ship.DecelerateRate = CfgFloat("ship", "decelerate_rate", ship.DecelerateRate);
		ship.ClimbAngleDeg = CfgFloat("ship", "climb_angle_deg", ship.ClimbAngleDeg);
		ship.InvertPitch = CfgBool("ship", "invert_pitch", ship.InvertPitch);
		ship.InvertBank = CfgBool("ship", "invert_bank", ship.InvertBank);
		ship.BoundX = CfgFloat("corridor", "half_width", ship.BoundX);
		ship.BoundYMin = CfgFloat("corridor", "floor", ship.BoundYMin);
		ship.BoundYMax = CfgFloat("corridor", "ceiling", ship.BoundYMax);
	}

	// Pushes the config-driven camera tunables onto the rig before it enters the tree, so _Ready()
	// builds its distance levels from them. Lives in settings.cfg [camera]; each falls back to the
	// rig's own export default when the key is absent.
	private void ApplyCameraConfig(CameraRig rig)
	{
		rig.DistanceClose = CfgFloat("camera", "distance_close", rig.DistanceClose);
		rig.DistanceNormal = CfgFloat("camera", "distance_normal", rig.DistanceNormal);
		rig.DistanceFar = CfgFloat("camera", "distance_far", rig.DistanceFar);
		rig.DistanceDefaultIndex = CfgInt("camera", "default_level", rig.DistanceDefaultIndex);
		rig.BaseFov = CfgFloat("camera", "base_fov", rig.BaseFov);
		rig.MaxFov = CfgFloat("camera", "max_fov", rig.MaxFov);
		rig.NearDistance = CfgFloat("camera", "near", rig.NearDistance);
		// The far clip must cover the streamed city, or this plane (not the fog) chops the far edge.
		// Auto-size it from the [streaming] knobs so raising chunks_ahead/behind "just works" with no
		// second edit; a manually-set [camera] far that's LARGER still wins (e.g. to see past the fog).
		float manualFar = CfgFloat("camera", "far", rig.FarDistance);
		rig.FarDistance = Mathf.Max(manualFar, CityDrawDistance() * 1.15f + 1000.0f);
	}

	// Farthest extent of the streamed city ahead/behind the ship (m) = the longer run × chunk length.
	// Drives the auto-sized camera far clip above so the clip plane never cuts the city before the fog.
	private float CityDrawDistance()
	{
		float clen = CfgFloat("streaming", "chunk_length", 200.0f);
		int ahead = CfgInt("streaming", "chunks_ahead", 72);
		int behind = CfgInt("streaming", "chunks_behind", 72);
		return (float)(Mathf.Max(ahead, behind) + 1) * clen;
	}

	private void EnsureEnvironment()
	{
		if (GetNodeOrNull("Sun") == null)
		{
			// A dim, cool key light — just enough to model the towers; the city lights itself (emissive).
			var sun = new DirectionalLight3D();
			sun.Name = "Sun";
			sun.Rotation = new Vector3(Mathf.DegToRad(-55.0f), Mathf.DegToRad(35.0f), 0.0f);
			sun.LightColor = new Color(0.55f, 0.65f, 1.0f);
			sun.LightEnergy = 0.35f;
			AddChild(sun);
		}

		if (GetNodeOrNull("WorldEnvironment") == null)
		{
			var we = new WorldEnvironment();
			we.Name = "WorldEnvironment";
			we.Environment = BuildEnvironment();
			AddChild(we);
		}

		if (GetNodeOrNull("RefGround") == null)
		{
			// A long, near-black WET street: low roughness + a little metal so SSR mirrors the neon
			// skyline in it (spec §7.7, wet-street reflections). Reads as dark glass when SSR is off.
			// Stored as _ground and slid along with the ship each frame (see _Process) so the long
			// thin-fog view never flies off its edge.
			var ground = new MeshInstance3D();
			ground.Name = "RefGround";
			var plane = new PlaneMesh();
			plane.Size = new Vector2(8000.0f, 48000.0f);
			ground.Mesh = plane;
			var gm = new StandardMaterial3D();
			gm.AlbedoColor = new Color(0.012f, 0.016f, 0.03f);
			gm.Metallic = 0.35f;
			gm.MetallicSpecular = 0.6f;
			gm.Roughness = 0.22f;
			ground.MaterialOverride = gm;
			ground.Position = new Vector3(0.0f, 0.0f, 0.0f);
			AddChild(ground);
		}
		_ground = GetNodeOrNull<MeshInstance3D>("RefGround");
	}

	// Assembles the neon-night Environment (M4 aesthetic pass, spec §7.7). The heavier desktop
	// effects (volumetric fog, SSR, glow) are gated by settings.cfg [fx] so they can be dialled
	// back for performance; the tasteful defaults match the export fallbacks here.
	private Environment BuildEnvironment()
	{
		var env = new Environment();

		// Neon-night sky: a procedural drifting-cloud sky (shader) with a glowing horizon band, or a
		// plain dark sky + horizon band as a cheaper fallback (see BuildSky()). Ambient + reflections
		// are sourced from it below, so the towers sit in a coherent night.
		env.BackgroundMode = Environment.BGMode.Sky;
		env.Sky = BuildSky();

		// Ambient + reflections come from the (dark) sky so matte surfaces stay moody and the neon pops.
		env.AmbientLightSource = Environment.AmbientSource.Sky;
		env.AmbientLightEnergy = 0.25f;
		env.ReflectedLightSource = Environment.ReflectionSource.Sky;

		// ACES tonemap keeps neon saturation while taming HDR; a high white point keeps bright
		// emissives COLOURED (so they bloom in colour) instead of clipping to white.
		env.TonemapMode = Environment.ToneMapper.Aces;
		env.TonemapExposure = CfgFloat("fx", "exposure", 1.0f);
		env.TonemapWhite = 6.0f;

		// Glow / bloom — the neon halo. Additive reads as light; the HDR threshold keeps the bloom on
		// the bright emissive strips, not the whole frame. Spread over several mips for a soft, wide halo.
		env.GlowEnabled = CfgBool("fx", "glow", true);
		env.GlowIntensity = CfgFloat("fx", "glow_intensity", 0.85f);
		env.GlowStrength = 1.0f;
		env.GlowBloom = CfgFloat("fx", "bloom", 0.12f);
		env.GlowBlendMode = Environment.GlowBlendModeEnum.Additive;
		env.GlowHdrThreshold = 0.95f;
		env.GlowHdrScale = 2.0f;
		for (int lvl = 1; lvl <= 5; lvl++)
			env.Set($"glow_levels/{lvl}", true);

		// Exponential distance fog — depth cue that fades the FAR chunk edge into the horizon. Tuned for
		// the long view: thin (so the city reads for kilometres) and a luminous neon-haze colour (so the
		// distance fades to atmosphere, not to black), blended toward the sky band.
		env.FogEnabled = true;
		env.FogLightColor = new Color(0.10f, 0.12f, 0.22f);
		env.FogDensity = CfgFloat("fx", "fog_density", 0.00018f);
		// Keep the fog OFF the sky (low sky_affect): at 0.7 it flattened the horizon glow + clouds toward
		// the dark fog colour, which read as "just darkness" above the rooftops. A little blends the far
		// building tops into the horizon without killing the sky.
		env.FogSkyAffect = CfgFloat("fx", "fog_sky_affect", 0.15f);
		env.FogAerialPerspective = 0.4f;

		// Volumetric fog — the real mood layer (desktop): a faint neon-tinted haze with true depth.
		env.VolumetricFogEnabled = CfgBool("fx", "volumetric_fog", true);
		env.VolumetricFogDensity = CfgFloat("fx", "volumetric_fog_density", 0.005f);
		env.VolumetricFogAlbedo = new Color(0.07f, 0.08f, 0.17f);
		env.VolumetricFogEmission = new Color(0.05f, 0.02f, 0.10f);
		env.VolumetricFogEmissionEnergy = 0.4f;
		env.VolumetricFogLength = CfgFloat("fx", "volumetric_fog_length", 6000.0f);
		env.VolumetricFogGIInject = 0.2f;
		// CRITICAL for a visible sky: this defaults to 1.0, which paints the (dark) volumetric haze over
		// the WHOLE sky dome at full strength — the sky sits behind the entire fog column, so it rendered
		// as "just blackness" no matter how bright the sky shader was. Keep it near 0 so the haze affects
		// the scene depth but not the sky itself.
		env.VolumetricFogSkyAffect = CfgFloat("fx", "volumetric_fog_sky_affect", 0.0f);

		// Screen-space reflections — wet-street neon (Forward+ desktop); reflects in the RefGround.
		env.SsrEnabled = CfgBool("fx", "ssr", true);
		env.SsrMaxSteps = 32;
		env.SsrFadeIn = 0.15f;
		env.SsrFadeOut = 2.0f;
		env.SsrDepthTolerance = 0.2f;

		// A touch more contrast + saturation in post to make the neon sing.
		env.AdjustmentEnabled = true;
		env.AdjustmentBrightness = 1.0f;
		env.AdjustmentContrast = 1.08f;
		env.AdjustmentSaturation = CfgFloat("fx", "saturation", 1.22f);

		return env;
	}

	// Builds the sky resource: a procedural neon cloud sky (custom shader) when [fx] sky_clouds is on,
	// else a plain ProceduralSkyMaterial (dark sky + neon horizon band, no clouds — cheaper). If the
	// shader ever fails to compile (GPU only — headless can't), set sky_clouds=false to fall back.
	private Sky BuildSky()
	{
		var sky = new Sky();
		if (CfgBool("fx", "sky_clouds", true))
		{
			var mat = new ShaderMaterial();
			mat.Shader = SkyShader;
			mat.SetShaderParameter("sky_energy", CfgFloat("fx", "sky_energy", 0.9f));
			mat.SetShaderParameter("cloud_coverage", CfgFloat("fx", "cloud_coverage", 0.5f));
			mat.SetShaderParameter("cloud_speed", CfgFloat("fx", "cloud_speed", 0.006f));
			sky.SkyMaterial = mat;
			return sky;
		}
		var skyMat = new ProceduralSkyMaterial();
		skyMat.SkyTopColor = new Color(0.01f, 0.01f, 0.03f);
		skyMat.SkyHorizonColor = new Color(0.09f, 0.05f, 0.16f);
		skyMat.SkyCurve = 0.12f;
		skyMat.SkyEnergyMultiplier = 0.8f;
		skyMat.GroundBottomColor = new Color(0.01f, 0.01f, 0.02f);
		skyMat.GroundHorizonColor = new Color(0.07f, 0.03f, 0.12f);
		skyMat.GroundEnergyMultiplier = 0.4f;
		sky.SkyMaterial = skyMat;
		return sky;
	}

	private void RegisterInput()
	{
		// Self-contained so the project runs with no manual Input Map setup. You can instead
		// define these in Project Settings → Input Map and delete this function.
		AddAction("steer_left", new[] { Key.A, Key.Left });
		AddAction("steer_right", new[] { Key.D, Key.Right });
		AddAction("steer_up", new[] { Key.W, Key.Up });
		AddAction("steer_down", new[] { Key.S, Key.Down });
		AddAction("accelerate", new[] { Key.Shift, Key.Space });
		AddAction("decelerate", new[] { Key.Ctrl, Key.X, Key.C, Key.V });
		AddAction("cycle_camera", new[] { Key.Q });
		AddAction("restart", new[] { Key.R });
		AddAction("toggle_fullscreen", new[] { Key.F });
		AddAction("quit", new[] { Key.Escape });
		AddMouseAction("rear_view", MouseButton.Middle);   // hold middle mouse = glance behind
	}

	private void AddAction(string action, Key[] keys)
	{
		if (InputMap.HasAction(action))
			return;
		InputMap.AddAction(action);
		foreach (Key k in keys)
		{
			var ev = new InputEventKey();
			ev.PhysicalKeycode = k;
			InputMap.ActionAddEvent(action, ev);
		}
	}

	private void AddMouseAction(string action, MouseButton button)
	{
		if (InputMap.HasAction(action))
			return;
		InputMap.AddAction(action);
		var ev = new InputEventMouseButton();
		ev.ButtonIndex = button;
		InputMap.ActionAddEvent(action, ev);
	}
}
