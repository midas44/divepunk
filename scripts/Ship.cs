using Godot;

// Arcade flight controller — DIVEPUNK.
//
// Attach to a CharacterBody3D (Game.cs will create one automatically if you just
// press Play). Throttle-driven forward motion with inertia: the player throttles up/down
// and steers laterally and vertically. Every number is exported so you can dial in the
// "feel" live in the Inspector.
//
// NOTE: this is the interim CharacterBody3D flyer. Task 2 rewrites it as an assisted-arcade
// RigidBody3D 6-DOF controller with collision bounce + impulse damage (spec §7.4).
public partial class Ship : CharacterBody3D
{
	[ExportGroup("Speed (throttle + inertia)")]
	[Export] public float BaseSpeed = 300.0f;       // forward speed a run STARTS at (m/s)
	[Export] public float MinSpeed = 0.0f;          // slowest the throttle reaches (m/s); raise above 0 so the ship never fully stops
	[Export] public float MaxSpeed = 2000.0f;       // fastest the throttle reaches (m/s)
	[Export] public float AccelerateRate = 240.0f;  // throttle-up: how fast `accelerate` adds speed (m/s²)
	[Export] public float DecelerateRate = 320.0f;  // throttle-down: how fast `decelerate` bleeds speed (m/s²)
	// INERTIA: with neither throttle key held, speed is HELD constant (no auto-ramp, no drag) — the
	// engine maintains whatever speed you last throttled to. Speed is clamped to [MinSpeed, MaxSpeed].

	[ExportGroup("Steering")]
	[Export] public float LateralSpeed = 45.0f;     // max sideways speed (m/s)
	[Export] public float ClimbAngleDeg = 45.0f;    // climb/dive steepness: vertical speed = forward_speed × tan(this). 45° climbs as fast as you fly; 0° = no vertical; clamped under 90°
	[Export] public float SteerSharpness = 8.0f;    // higher = snappier, lower = floatier
	[Export] public float BankAngleDeg = 23.0f;     // visual roll into turns (pure juice)
	[Export] public float PitchAngleDeg = 10.0f;    // visual pitch on climb / dive (pure juice)
	[Export] public bool InvertPitch = true;        // true = nose pitches UP as you climb (natural arcade feel)
	[Export] public bool InvertBank = true;         // true = banks INTO strafes the natural way
	[Export] public float VisualLerp = 10.0f;       // how fast the model banks / pitches

	// Emitted every physics frame. ratio is 0 at MinSpeed, 1 at MaxSpeed; `accelerating` = throttling up.
	[Signal] public delegate void SpeedChangedEventHandler(float speed, float ratio, bool accelerating);

	private float _forwardSpeed = 0.0f;
	private Vector2 _steer = Vector2.Zero;
	private Node3D _model;

	public override void _Ready()
	{
		_forwardSpeed = Mathf.Clamp(BaseSpeed, MinSpeed, MaxSpeed);
		EnsureVisualAndCollision();
	}

	public override void _PhysicsProcess(double delta)
	{
		float d = (float)delta;

		// THROTTLE + INERTIA. Speed is a held state: `accelerate` adds, `decelerate` bleeds, and with
		// neither held the engine simply MAINTAINS the current speed (no auto-ramp, no drag). Holding
		// both nets the difference. Clamped to [MinSpeed, MaxSpeed].
		float accelIn = Input.GetActionStrength("accelerate");
		float decelIn = Input.GetActionStrength("decelerate");
		_forwardSpeed += (accelIn * AccelerateRate - decelIn * DecelerateRate) * d;
		_forwardSpeed = Mathf.Clamp(_forwardSpeed, MinSpeed, MaxSpeed);
		// 3rd signal arg = "actively throttling up" — drives the speed-up juice (FX / shake / whoosh).
		bool accelerating = accelIn > decelIn;

		// Smooth raw input toward target for a weighty-but-responsive feel (framerate independent).
		Vector2 target = new Vector2(
			Input.GetAxis("steer_left", "steer_right"),
			Input.GetAxis("steer_down", "steer_up")
		);
		_steer = _steer.Lerp(target, 1.0f - Mathf.Exp(-SteerSharpness * d));

		// Compose velocity: held forward (−Z) + steering on X / Y. Climb/dive speed is the forward speed
		// projected at ClimbAngleDeg (vertical = forward × tan θ), so 45° climbs as fast as you fly.
		// The angle is clamped just under 90° to keep tan finite.
		float vert = _forwardSpeed * Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(ClimbAngleDeg, 0.0f, 89.0f)));
		Velocity = new Vector3(_steer.X * LateralSpeed, _steer.Y * vert, -_forwardSpeed);
		MoveAndSlide();

		BankModel(d);
		EmitSignal(SignalName.SpeedChanged, _forwardSpeed, GetSpeedRatio(), accelerating);
	}

	public float GetSpeed() => _forwardSpeed;

	// Current vertical speed (m/s): + climbing, − diving. Drives the HUD V-SPD indicator.
	public float GetVerticalSpeed() => Velocity.Y;

	// Absolute top forward speed (m/s) reachable. HUD horizontal-bar scale.
	public float GetTopSpeed() => MaxSpeed;

	// Top vertical speed (m/s) reachable at full forward speed. HUD V-SPD bar scale.
	public float GetMaxVerticalSpeed() => MaxSpeed * Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(ClimbAngleDeg, 0.0f, 89.0f)));

	// 0..1 throttle position (current speed across the min→max range). Drives the HUD throttle bar.
	public float GetBoostMeter() => Mathf.Clamp((_forwardSpeed - MinSpeed) / Mathf.Max(MaxSpeed - MinSpeed, 0.001f), 0.0f, 1.0f);

	// 0 at MinSpeed, 1 at MaxSpeed. Drives camera FOV, speed lines, audio pitch, etc.
	public float GetSpeedRatio() => Mathf.Clamp((_forwardSpeed - MinSpeed) / Mathf.Max(MaxSpeed - MinSpeed, 0.001f), 0.0f, 1.0f);

	private void BankModel(float d)
	{
		if (_model == null)
			return;
		// Roll into lateral turns, pitch into vertical movement. The sign flips are exposed
		// (InvertPitch / InvertBank) so the nose pitches UP on a climb and strafes bank the way
		// that feels right — both directions corrected. Tune in settings.cfg [ship].
		float pitchSign = InvertPitch ? 1.0f : -1.0f;
		float bankSign = InvertBank ? -1.0f : 1.0f;
		Vector3 targetRot = new Vector3(
			Mathf.DegToRad(pitchSign * _steer.Y * PitchAngleDeg),
			0.0f,
			Mathf.DegToRad(bankSign * _steer.X * BankAngleDeg)
		);
		_model.Rotation = _model.Rotation.Lerp(targetRot, 1.0f - Mathf.Exp(-VisualLerp * d));
	}

	// Builds a placeholder neon box + collision so you can press Play with zero art.
	// Replace the "Model" child with a real ship mesh later — the controller doesn't care.
	private void EnsureVisualAndCollision()
	{
		_model = GetNodeOrNull<Node3D>("Model");
		if (_model == null)
		{
			_model = new Node3D();
			_model.Name = "Model";
			AddChild(_model);
			var mi = new MeshInstance3D();
			var box = new BoxMesh();
			box.Size = new Vector3(3.0f, 1.0f, 5.0f);
			mi.Mesh = box;
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.05f, 0.6f, 0.9f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.0f, 0.85f, 1.0f);
			mat.EmissionEnergyMultiplier = 3.0f;
			mi.MaterialOverride = mat;
			_model.AddChild(mi);
		}
		if (GetNodeOrNull("Col") == null)
		{
			var col = new CollisionShape3D();
			col.Name = "Col";
			var shape = new BoxShape3D();
			shape.Size = new Vector3(3.0f, 1.0f, 5.0f);
			col.Shape = shape;
			AddChild(col);
		}
	}
}
