using Godot;
using System.Collections.Generic;

// Arcade flight controller — DIVEPUNK, milestones M0–M3.
//
// Attach to a CharacterBody3D (Game.cs will create one automatically if you just
// press Play). Throttle-driven forward motion with inertia: the player throttles up/down
// and steers laterally and vertically within a corridor. Every number is exported so you
// can dial in the "feel" live in the Inspector — the speed/steering values ARE Milestone M1,
// so expect to spend real time tuning them.
//
// Crash detection (M3) uses a direct physics-space shape query each frame, NOT an Area3D:
// under Godot 4.6 + Jolt, Area overlap callbacks proved unreliable here, while
// DirectSpaceState.IntersectShape detects obstacles deterministically. This matches the
// spec's intent — use the physics engine to DETECT contact (spec §7.4), never to push the
// ship around. The near-miss query (a slightly larger box, spec §7.5) lands in M3b.
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

	[ExportGroup("Corridor (half-extents from centre)")]
	[Export] public float BoundX = 120.0f;          // horizontal half-width; set from settings.cfg [corridor] half_width
	[Export] public float BoundYMin = 4.0f;         // floor; settings.cfg [corridor] floor
	[Export] public float BoundYMax = 1700.0f;      // ceiling; settings.cfg [corridor] ceiling

	[ExportGroup("Collision")]
	[Export] public Vector3 CrashSize = new Vector3(3.0f, 1.0f, 5.0f);        // crash hitbox (matches the ship body)
	[Export] public Vector3 NearMissSize = new Vector3(16.0f, 12.0f, 14.0f);  // "danger bubble" around the ship (spec §7.5)

	// 1-indexed physics layer obstacles live on (matches CityChunk.cs / project.godot).
	private const int ObstacleLayer = 2;

	// Emitted every physics frame. ratio is 0 at MinSpeed, 1 at MaxSpeed; `accelerating` = throttling up.
	[Signal] public delegate void SpeedChangedEventHandler(float speed, float ratio, bool accelerating);
	// Emitted once per obstacle that enters the near-miss bubble without a crash (spec §7.5).
	[Signal] public delegate void NearMissEventHandler();
	// Emitted once when the ship crashes.
	[Signal] public delegate void CrashedEventHandler();

	private float _forwardSpeed = 0.0f;
	private Vector2 _steer = Vector2.Zero;
	private Node3D _model;
	private bool _alive = true;
	private BoxShape3D _crashShape;
	private PhysicsShapeQueryParameters3D _crashQuery;
	private BoxShape3D _nearShape;
	private PhysicsShapeQueryParameters3D _nearQuery;
	private HashSet<ulong> _nearNow = new();   // instance ids for obstacles currently in the bubble

	public override void _Ready()
	{
		_forwardSpeed = Mathf.Clamp(BaseSpeed, MinSpeed, MaxSpeed);
		EnsureVisualAndCollision();
		BuildQueries();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_alive)
			return;

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

		ClampToCorridor();
		BankModel(d);
		CheckObstacles();
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

	public void Crash()
	{
		if (!_alive)
			return;
		_alive = false;
		EmitSignal(SignalName.Crashed);
	}

	// Shape-query the obstacles layer at the ship's position each frame. An inner (body-sized)
	// box = crash; an outer "danger bubble" box = near-miss. Runs in _PhysicsProcess so
	// DirectSpaceState is valid; masks to ObstacleLayer so it never hits the ship's own body.
	private void CheckObstacles()
	{
		if (!_alive || _crashQuery == null)
			return;
		PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
		if (space == null)
			return;
		Transform3D xform = new Transform3D(Basis.Identity, GlobalPosition);

		// Inner: any hit is a crash (and ends the frame's checks).
		_crashQuery.Transform = xform;
		if (space.IntersectShape(_crashQuery, 1).Count > 0)
		{
			Crash();
			return;
		}

		// Outer: obstacles in the bubble (but not the body) are near-misses. Fire once per
		// obstacle, the first frame it enters the bubble; track who's currently inside so the
		// same obstacle doesn't re-trigger every frame as the ship passes it.
		_nearQuery.Transform = xform;
		Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(_nearQuery, 8);
		var current = new HashSet<ulong>();
		foreach (Godot.Collections.Dictionary h in hits)
		{
			var col = h["collider"].As<GodotObject>();
			if (col == null)
				continue;
			ulong id = col.GetInstanceId();
			current.Add(id);
			if (!_nearNow.Contains(id))
				EmitSignal(SignalName.NearMiss);
		}
		_nearNow = current;
	}

	private void BuildQueries()
	{
		_crashShape = new BoxShape3D();
		_crashShape.Size = CrashSize;
		_crashQuery = new PhysicsShapeQueryParameters3D();
		_crashQuery.Shape = _crashShape;
		_crashQuery.CollisionMask = (uint)(1 << (ObstacleLayer - 1));   // only the obstacles layer
		_crashQuery.CollideWithBodies = true;
		_crashQuery.CollideWithAreas = false;

		_nearShape = new BoxShape3D();
		_nearShape.Size = NearMissSize;
		_nearQuery = new PhysicsShapeQueryParameters3D();
		_nearQuery.Shape = _nearShape;
		_nearQuery.CollisionMask = (uint)(1 << (ObstacleLayer - 1));
		_nearQuery.CollideWithBodies = true;
		_nearQuery.CollideWithAreas = false;
	}

	private void ClampToCorridor()
	{
		Vector3 p = GlobalPosition;
		p.X = Mathf.Clamp(p.X, -BoundX, BoundX);
		p.Y = Mathf.Clamp(p.Y, BoundYMin, BoundYMax);
		GlobalPosition = p;
	}

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
