using Godot;

// Root bootstrap — DIVEPUNK (open-world reframe).
//
// Attach to the root Node3D of Main.tscn and press Play. It self-assembles a runnable
// scene (ship + chase camera + minimal environment + the baked world: terrain, ocean, city + telemetry HUD)
// and registers the input actions in code, so the project runs with zero manual setup.
//
// As you build real, hand-authored scenes you can delete the auto-spawn helpers below
// and place the nodes directly in the scene tree instead.
public partial class Game : Node3D
{
	private static readonly PackedScene HudScene = GD.Load<PackedScene>("res://scenes/ui/HUD.tscn");
	private static readonly Shader SkyShader = GD.Load<Shader>("res://shaders/sky.gdshader");   // procedural neon cloud sky (M4 view pass)
	private static readonly Shader WaterShader = GD.Load<Shader>("res://shaders/water.gdshader"); // dusk ocean plane (Task 5)

	[ExportGroup("Juice")]
	[Export] public float ShakeOnBoost = 0.35f;            // camera punch the moment a boost kicks in
	[Export] public float ShakeOnImpact = 0.9f;            // camera punch master-scale on a collision (× dent severity)

	private Ship _ship;
	private CameraRig _rig;
	private ScreenFX _fx;
	private RainFX _rain;
	private Hud _hud;
	private WorldLoader _world;
	private Traffic _traffic;

	private bool _wasBoosting = false;

	public override void _Ready()
	{
		RegisterInput();
		Engine.TimeScale = 1.0;        // defensive: clear any leftover slow-mo
		CaptureMouse();
		EnsureEnvironment();
		SpawnShipAndCamera();
		SpawnWorld();
		SpawnTraffic();   // ambient flying cars roaming the bounded world (collidable hazards; no AI)
		SpawnOcean();
		SpawnUi();
		SpawnScreenFx();
		SpawnRain();
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
			ApplyFlightConfig(_ship);    // apply config exports BEFORE AddChild so _Ready() initialises with them
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
		_ship.SpeedChanged += OnShipSpeedChanged;
		// Impact juice: the DamageComponent exists once the ship's _Ready has run (i.e. after AddChild).
		if (_ship.Damage != null)
			_ship.Damage.Damaged += OnShipDamaged;
	}

	// Loads + renders the baked 8 km city and drives its near-player colliders. Replaces the Task-2 test
	// obstacles; the flat sea-level floor stays as a stand-in until Task 5 adds terrain + ocean.
	private void SpawnWorld()
	{
		if (GetNodeOrNull("WorldLoader") != null)
			return;
		_world = new WorldLoader { Name = "WorldLoader" };
		AddChild(_world);              // _Ready() loads world_main.res + builds the tiles
		_world.Player = _ship;         // drives the lazy colliders

		// Convenience: start the run above the city so the playtest opens looking at it (the plateau is centred
		// ~(-7000,-1000) after the Task-9 rescale, well off the (0,30,0) spawn). The standoff scales with the
		// ~5x-wider city so the opening frames the skyline, not its interior. Harmless if the load failed.
		if (_world.CityCenter != Vector3.Zero)
			_ship.GlobalPosition = _world.CityCenter + new Vector3(0.0f, 800.0f, 3500.0f);
	}

	// Spawns the fixed ambient-traffic population — collidable flying cars that roam the bounded world and
	// bounce/damage the player on contact (no AI, no streaming, no re-bake). Built right after the world so it
	// can spawn around the city centroid. [traffic] count<=0 disables it.
	private void SpawnTraffic()
	{
		if (GetNodeOrNull("Traffic") != null)
			return;
		int count = CfgInt("traffic", "count", 24);
		if (count <= 0)
			return;

		_traffic = new Traffic { Name = "Traffic" };
		ApplyTrafficConfig(_traffic);
		_traffic.Initialize(CfgFloat("world", "extent", 8000.0f), _world?.CityCenter ?? Vector3.Zero);
		AddChild(_traffic);   // _Ready() seeds the RNG + builds the deterministic population
	}

	// Pushes the config-driven traffic tunables onto the node before it enters the tree, so _Ready() builds
	// the population with them. Each falls back to the node's own export default when the key is absent.
	// (hazard_pulse lives in [fx] alongside the other telegraph toggles.)
	private void ApplyTrafficConfig(Traffic t)
	{
		t.Count        = CfgInt("traffic", "count", t.Count);
		t.MinSpeed     = CfgFloat("traffic", "min_speed", t.MinSpeed);
		t.MaxSpeed     = CfgFloat("traffic", "max_speed", t.MaxSpeed);
		t.SpawnRadius  = CfgFloat("traffic", "spawn_radius", t.SpawnRadius);
		t.AltitudeBand = new Vector2(
			CfgFloat("traffic", "altitude_min", t.AltitudeBand.X),
			CfgFloat("traffic", "altitude_max", t.AltitudeBand.Y));
		t.Seed         = CfgInt("traffic", "seed", t.Seed);
		t.HazardPulse  = CfgBool("fx", "hazard_pulse", t.HazardPulse);
	}

	// A dusk ocean plane at sea level (world Y=0), under shaders/water.gdshader. Visual-only (no collider — you
	// fly through it). Fixed at the origin (the world is bounded), sized to reach past the view. [fx] water=false
	// drops it (you'd then see the bare seabed terrain). Tunables fall back to the shader defaults if absent.
	private void SpawnOcean()
	{
		if (GetNodeOrNull("Ocean") != null) return;
		if (!CfgBool("fx", "water", true)) return;

		float extent = CfgFloat("world", "extent", 8000.0f);
		var ocean = new MeshInstance3D
		{
			Name = "Ocean",
			Mesh = new PlaneMesh { Size = new Vector2(extent * 2.0f, extent * 2.0f) },   // covers the world + horizon margin
			Position = new Vector3(0.0f, 0.0f, 0.0f),
		};
		var mat = new ShaderMaterial { Shader = WaterShader };
		mat.SetShaderParameter("ripple_speed", CfgFloat("fx", "water_ripple_speed", 0.04f));
		mat.SetShaderParameter("water_energy", CfgFloat("fx", "water_energy", 1.0f));
		ocean.MaterialOverride = mat;
		AddChild(ocean);
	}

	// Spawns the in-run telemetry HUD (no scoring / game-over in the reframe).
	private void SpawnUi()
	{
		if (GetNodeOrNull("HUD") != null)
			return;
		_hud = HudScene.Instantiate<Hud>();
		_hud.Name = "HUD";
		AddChild(_hud);
		_hud.SetShip(_ship);
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

	// Spawns the fullscreen lens-rain overlay (Task 6, user-requested cyberpunk scope extension). A NEW screen
	// layer (RainFX, layer 31) — does NOT touch ScreenFX/post. [fx] rain defaults ON so it renders out of the
	// box; rain_strength scales the streak alpha. Fed the ship's speed via SpeedChanged so the streaks shear
	// back / intensify with velocity. Toggles are set BEFORE AddChild so _Ready builds the right pass.
	private void SpawnRain()
	{
		if (GetNodeOrNull("RainFX") != null)
			return;
		if (!CfgBool("fx", "rain", true))
			return;
		_rain = new RainFX { Name = "RainFX" };
		_rain.Enabled = true;
		_rain.Strength = CfgFloat("fx", "rain_strength", 1.0f);
		AddChild(_rain);
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
		_rain?.SetSpeedRatio(ratio);   // streaks shear back + intensify with speed
		if (accelerating && !_wasBoosting)
		{
			if (_rig != null)
				_rig.AddShake(ShakeOnBoost);
			AudioManager.Instance?.Boost();
		}
		_wasBoosting = accelerating;
	}

	// Collision juice: the moment the car takes a dent, punch the camera + play the crash thud + a warm
	// impact flash. Scaled by the hit's damage so a graze is a tap and a full-speed ram is a wallop. (The
	// game never ends — this is feedback, not failure.)
	private void OnShipDamaged(float amount)
	{
		_rig?.AddShake(Mathf.Clamp(amount * 0.04f, 0.15f, 1.0f) * ShakeOnImpact);
		AudioManager.Instance?.Crash();
		_fx?.Flash(Mathf.Clamp(amount * 0.02f, 0.0f, 0.6f), new Color(1.0f, 0.4f, 0.3f));
	}

	// Safe read from the Config autoload (falls back to the default if it isn't present).
	private float CfgFloat(string section, string key, float fallback) => Config.Instance != null ? Config.Instance.GetFloat(section, key, fallback) : fallback;
	private int CfgInt(string section, string key, int fallback) => Config.Instance != null ? Config.Instance.GetInt(section, key, fallback) : fallback;
	private bool CfgBool(string section, string key, bool fallback) => Config.Instance != null ? Config.Instance.GetBool(section, key, fallback) : fallback;

	// Pushes the config-driven flight tunables onto the car before it enters the tree, so _Ready()
	// initialises with them. The force-based 6-DOF model lives in settings.cfg [flight]; each falls back
	// to the ship's own export default when the key is absent. ([damage] is NOT pushed here — the
	// DamageComponent self-reads it, since the ship creates that child during its own _Ready.)
	private void ApplyFlightConfig(Ship ship)
	{
		ship.MaxSpeed         = CfgFloat("flight", "max_speed", ship.MaxSpeed);
		ship.ThrustForce      = CfgFloat("flight", "thrust_force", ship.ThrustForce);
		ship.BrakeForce       = CfgFloat("flight", "brake_force", ship.BrakeForce);
		ship.BodyMass         = CfgFloat("flight", "mass", ship.BodyMass);
		ship.PitchTorque      = CfgFloat("flight", "pitch_torque", ship.PitchTorque);
		ship.YawTorque        = CfgFloat("flight", "yaw_torque", ship.YawTorque);
		ship.RollTorque       = CfgFloat("flight", "roll_torque", ship.RollTorque);
		ship.LevelStrength    = CfgFloat("flight", "level_strength", ship.LevelStrength);
		ship.LevelDamping     = CfgFloat("flight", "level_damping", ship.LevelDamping);
		ship.BankCoordination = CfgFloat("flight", "bank_coordination", ship.BankCoordination);
		ship.LinearDampValue  = CfgFloat("flight", "linear_damp", ship.LinearDampValue);
		ship.AngularDampValue = CfgFloat("flight", "angular_damp", ship.AngularDampValue);
		ship.Grip             = CfgFloat("flight", "grip", ship.Grip);
		ship.Bounce           = CfgFloat("flight", "bounce", ship.Bounce);
		ship.InvertPitch      = CfgBool("flight", "invert_pitch", ship.InvertPitch);
		ship.InvertRoll       = CfgBool("flight", "invert_roll", ship.InvertRoll);
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
		rig.FarDistance = CfgFloat("camera", "far", rig.FarDistance);
	}

	private void EnsureEnvironment()
	{
		if (GetNodeOrNull("Sun") == null)
		{
			// A low, warm sunset key — long gold light raking the tower faces + desert. The city still lights
			// itself (emissive); this just models the geometry and warms the lit faces. Shadows stay OFF
			// (default) — a shadow-casting directional over 1024 terrain tiles is an FPS risk and the mood
			// comes from colour/fog/glow, not shadows.
			var sun = new DirectionalLight3D();
			sun.Name = "Sun";
			sun.Rotation = new Vector3(Mathf.DegToRad(-18.0f), Mathf.DegToRad(35.0f), 0.0f);   // low on the horizon (raking)
			sun.LightColor = new Color(1.0f, 0.62f, 0.38f);   // warm gold-orange sunset key
			sun.LightEnergy = 0.6f;                            // a touch stronger warm key — tune vs neon wash-out
			AddChild(sun);
		}

		if (GetNodeOrNull("WorldEnvironment") == null)
		{
			var we = new WorldEnvironment();
			we.Name = "WorldEnvironment";
			we.Environment = BuildEnvironment();
			AddChild(we);
		}
	}

	// Assembles the synthwave-dusk Environment (Task 6 aesthetic pass, spec §7.6). The heavier desktop
	// effects (volumetric fog, SSR, glow) are gated by settings.cfg [fx] so they can be dialled
	// back for performance; the tasteful defaults match the export fallbacks here.
	private Environment BuildEnvironment()
	{
		var env = new Environment();

		// Dusk sky: a procedural drifting-cloud sky (shader) with a warm golden-purple sunset band, or a
		// plain dark sky + horizon band as a cheaper fallback (see BuildSky()). Ambient + reflections
		// are sourced from it below, so the towers sit in a coherent warm dusk.
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

		// Exponential distance fog — depth cue that fades the far edge into the horizon. Tuned for the long
		// view: thin (so the basin reads for kilometres) and a WARM dusk-haze colour (so the distance fades
		// to warm atmosphere, not cold blue/black) — the single biggest "warm & legible distance" lever.
		env.FogEnabled = true;
		env.FogLightColor = new Color(0.42f, 0.26f, 0.30f);   // dusty mauve/peach dusk haze
		env.FogDensity = CfgFloat("fx", "fog_density", 0.00018f);
		// Keep the fog OFF the sky (low sky_affect): at 0.7 it flattened the horizon glow + clouds toward
		// the dark fog colour, which read as "just darkness" above the rooftops. A little blends the far
		// building tops into the horizon without killing the sky.
		env.FogSkyAffect = CfgFloat("fx", "fog_sky_affect", 0.15f);
		env.FogAerialPerspective = 0.4f;

		// Volumetric fog — the real mood layer (desktop): a faint warm-tinted haze with true depth. Gated
		// [fx] volumetric_fog (off by default); warmed here for if/when it's enabled.
		env.VolumetricFogEnabled = CfgBool("fx", "volumetric_fog", true);
		env.VolumetricFogDensity = CfgFloat("fx", "volumetric_fog_density", 0.005f);
		env.VolumetricFogAlbedo = new Color(0.24f, 0.16f, 0.22f);     // warm dusk haze
		env.VolumetricFogEmission = new Color(0.16f, 0.07f, 0.10f);   // faint warm self-glow
		env.VolumetricFogEmissionEnergy = 0.4f;
		env.VolumetricFogLength = CfgFloat("fx", "volumetric_fog_length", 6000.0f);
		env.VolumetricFogGIInject = 0.2f;
		// CRITICAL for a visible sky: this defaults to 1.0, which paints the (dark) volumetric haze over
		// the WHOLE sky dome at full strength — the sky sits behind the entire fog column, so it rendered
		// as "just blackness" no matter how bright the sky shader was. Keep it near 0 so the haze affects
		// the scene depth but not the sky itself.
		env.VolumetricFogSkyAffect = CfgFloat("fx", "volumetric_fog_sky_affect", 0.0f);

		// Screen-space reflections — neon on wet surfaces (Forward+ desktop); reflects the skyline in the water + terrain.
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
		AddAction("roll_left", new[] { Key.Q });           // 6-DOF roll (Task 2)
		AddAction("roll_right", new[] { Key.E });
		AddAction("cycle_camera", new[] { Key.Tab });      // moved off Q (now roll_left); CameraRig reads the action, not the key
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
