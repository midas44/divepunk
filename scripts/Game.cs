using Godot;

// Root bootstrap — DIVEPUNK (open-world reframe).
//
// Attach to the root Node3D of Main.tscn and press Play. It self-assembles a runnable
// scene (ship + chase camera + minimal environment + a flat ground void + telemetry HUD)
// and registers the input actions in code, so the project runs with zero manual setup.
//
// As you build real, hand-authored scenes you can delete the auto-spawn helpers below
// and place the nodes directly in the scene tree instead.
public partial class Game : Node3D
{
	private static readonly PackedScene HudScene = GD.Load<PackedScene>("res://scenes/ui/HUD.tscn");
	private static readonly Shader SkyShader = GD.Load<Shader>("res://shaders/sky.gdshader");   // procedural neon cloud sky (M4 view pass)

	[ExportGroup("Juice")]
	[Export] public float ShakeOnBoost = 0.35f;            // camera punch the moment a boost kicks in
	[Export] public float ShakeOnImpact = 0.9f;            // camera punch master-scale on a collision (× dent severity)

	[ExportGroup("Test")]
	[Export] public bool SpawnTestObstacles = true;        // void test field (boxes + floor) to ram; remove when the real world lands (Task 4)

	private Ship _ship;
	private CameraRig _rig;
	private ScreenFX _fx;
	private Hud _hud;
	private MeshInstance3D _ground;

	private bool _wasBoosting = false;

	public override void _Ready()
	{
		RegisterInput();
		Engine.TimeScale = 1.0;        // defensive: clear any leftover slow-mo
		CaptureMouse();
		EnsureEnvironment();
		SpawnShipAndCamera();
		SpawnTestField();
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

	// A handful of obstacles + a ground collider in the empty void so bounce + damage are testable before
	// the real world lands (Task 4). Gated by SpawnTestObstacles so it's trivially removable. Deterministic
	// placement (no RNG) so the field is identical every launch.
	private void SpawnTestField()
	{
		if (!SpawnTestObstacles || GetNodeOrNull("TestField") != null)
			return;

		var field = new Node3D { Name = "TestField" };
		AddChild(field);

		var bounceMat = new PhysicsMaterial { Bounce = 0.3f, Friction = 0.5f };

		// Collision floor: a large finite slab with its top face at Y=0, so you can bounce off the ground.
		// NOTE: WorldBoundaryShape3D is FINITE under Jolt (a ~1000 m slab centred at origin), so the car
		// would fly off its edge — a big box won't. The visual RefGround (Game._ground) still follows you.
		var floor = new StaticBody3D { Name = "Floor" };
		floor.PhysicsMaterialOverride = bounceMat;
		var floorCol = new CollisionShape3D { Name = "Col" };
		floorCol.Shape = new BoxShape3D { Size = new Vector3(40000.0f, 10.0f, 40000.0f) };
		floor.AddChild(floorCol);
		floor.Position = new Vector3(0.0f, -5.0f, 0.0f);   // top face at Y=0
		field.AddChild(floor);

		// Emissive boxes scattered ahead (-Z) and around the spawn at varied heights, so you can fly out
		// and ram them. Bright neon so they read against the dark void.
		Vector3[] pos =
		{
			new Vector3(   0.0f, 20.0f, -200.0f),
			new Vector3(  90.0f, 45.0f, -340.0f),
			new Vector3(-130.0f, 15.0f, -300.0f),
			new Vector3( 160.0f, 65.0f, -520.0f),
			new Vector3( -70.0f, 35.0f, -560.0f),
			new Vector3(  50.0f, 25.0f, -720.0f),
		};
		float[] sizes = { 40.0f, 60.0f, 30.0f, 70.0f, 35.0f, 50.0f };
		Color[] tints =
		{
			new Color(1.0f, 0.2f, 0.6f),   // magenta
			new Color(1.0f, 0.55f, 0.1f),  // orange
			new Color(0.4f, 1.0f, 0.5f),   // green
			new Color(0.6f, 0.4f, 1.0f),   // violet
			new Color(1.0f, 0.85f, 0.2f),  // amber
			new Color(0.2f, 0.8f, 1.0f),   // cyan
		};

		for (int i = 0; i < pos.Length; i++)
		{
			float s = sizes[i];
			var body = new StaticBody3D { Name = $"Obstacle{i}" };
			body.PhysicsMaterialOverride = bounceMat;

			var col = new CollisionShape3D { Name = "Col" };
			col.Shape = new BoxShape3D { Size = new Vector3(s, s, s) };
			body.AddChild(col);

			var mi = new MeshInstance3D { Name = "Mesh" };
			mi.Mesh = new BoxMesh { Size = new Vector3(s, s, s) };
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = tints[i] * 0.3f;
			mat.EmissionEnabled = true;
			mat.Emission = tints[i];
			mat.EmissionEnergyMultiplier = 2.0f;
			mi.MaterialOverride = mat;
			body.AddChild(mi);

			body.Position = pos[i];
			field.AddChild(body);
		}
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

	// Collision juice: the moment the car takes a dent, punch the camera + play the crash thud + a warm
	// impact flash. Scaled by the hit's damage so a graze is a tap and a full-speed ram is a wallop. (The
	// game never ends — this is feedback, not failure.)
	private void OnShipDamaged(float amount)
	{
		_rig?.AddShake(Mathf.Clamp(amount * 0.04f, 0.15f, 1.0f) * ShakeOnImpact);
		AudioManager.Instance?.Crash();
		_fx?.Flash(Mathf.Clamp(amount * 0.02f, 0.0f, 0.6f), new Color(1.0f, 0.4f, 0.3f));
	}

	public override void _Process(double delta)
	{
		// Keep the reference ground centred under the ship (open world: follow on both X and Z).
		if (_ship != null && _ground != null)
		{
			Vector3 gp = _ship.GlobalPosition;
			_ground.GlobalPosition = new Vector3(gp.X, 0.0f, gp.Z);
		}
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
			// A near-black WET street plane: low roughness + a little metal so SSR mirrors the neon
			// skyline in it (spec §7.7, wet-street reflections). Reads as dark glass when SSR is off.
			// Stored as _ground and re-centred under the ship each frame (see _Process) so the open
			// void always has a floor beneath you wherever you fly.
			var ground = new MeshInstance3D();
			ground.Name = "RefGround";
			var plane = new PlaneMesh();
			plane.Size = new Vector2(20000.0f, 20000.0f);
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
