using Godot;

// Assisted-arcade 6-DOF flying car — DIVEPUNK (spec §7.4). A RigidBody3D simulated by Jolt: thrust along
// the nose, pitch/yaw/roll torques, PD auto-leveling (self-rights on release), coordinated banked turns,
// and a speed clamp. Collisions are resolved by Jolt as a bounce; contact impulse becomes damage via the
// child DamageComponent in _IntegrateForces. Health at zero does NOTHING — there is no game-over.
//
// Attach to a RigidBody3D (Game.cs creates one automatically on Play). Every number is exported so you
// can dial in the "feel" live in the Inspector / settings.cfg [flight].
public partial class Ship : RigidBody3D
{
	[ExportGroup("Thrust")]
	[Export] public float MaxSpeed = 1000.0f;     // LinearVelocity clamp (m/s) — carried from old [ship] max_speed
	[Export] public float ThrustForce = 4000.0f;  // forward push (N) along -Basis.Z when accelerating
	[Export] public float BrakeForce = 6000.0f;   // reverse/brake push (N) when decelerating
	[Export] public float BodyMass = 4.0f;        // sets RigidBody3D.Mass in _Ready (force feel scales with it)

	[ExportGroup("Rotation (torque)")]
	[Export] public float PitchTorque = 1400.0f;
	[Export] public float YawTorque = 900.0f;
	[Export] public float RollTorque = 1600.0f;
	[Export] public bool InvertPitch = true;      // carried from old [ship] invert_pitch — flips pitch sign to taste
	[Export] public bool InvertRoll = true;       // carried from old [ship] invert_bank

	[ExportGroup("Assist (auto-level + coordination)")]
	[Export] public float LevelStrength = 9.0f;   // PD kP — self-rights pitch+roll toward the horizon (yaw left free)
	[Export] public float LevelDamping = 4.5f;    // PD kD — kills the wobble so leveling settles crisply
	[Export] public float BankCoordination = 0.7f;// yaw input adds proportional roll → turns feel like flying
	[Export] public float LinearDampValue = 0.6f; // glide/coast (set onto RigidBody3D.LinearDamp)
	[Export] public float AngularDampValue = 3.0f;// rotations settle when you let go (set onto AngularDamp)
	[Export] public float Grip = 3.0f;            // arcade steering: how fast LinearVelocity follows the nose (1/s); 0 = pure Newtonian (rotating won't steer)

	[ExportGroup("Collision")]
	[Export] public float Bounce = 0.3f;          // PhysicsMaterial bounce on impact (0 = dead, 1 = super-ball)
	[Export] public float Friction = 0.4f;

	// Preserved: drives camera FOV, speed-line FX, audio. Emitted every physics frame. ratio is 0..1 of
	// MaxSpeed; `accelerating` = throttling up (drives the speed-up juice / shake / whoosh).
	[Signal] public delegate void SpeedChangedEventHandler(float speed, float ratio, bool accelerating);

	private Node3D _model;
	private DamageComponent _damage;

	public override void _Ready()
	{
		// RigidBody setup for a hovering 6-DOF flyer.
		GravityScale = 0.0f;                 // zero-G: it hovers; no constant fight to stay up (spec §7.4)
		Mass = BodyMass;
		LinearDamp = LinearDampValue;
		AngularDamp = AngularDampValue;
		CanSleep = false;                    // always simulating, so input is always responsive
		ContactMonitor = true;               // required to read contacts in _IntegrateForces
		MaxContactsReported = 8;             // spec §7.4
		ContinuousCd = true;                 // swept collision: a fast impact on a THIN collider (the flat
		                                     // terrain/street plane) bounces instead of tunnelling straight
		                                     // through at speed. Jolt resolves it with a linear cast.
		PhysicsMaterialOverride = new PhysicsMaterial { Bounce = Bounce, Friction = Friction };

		EnsureVisualAndCollision();          // neon-box Model + box CollisionShape3D (rotate WITH the body now)
		_damage = GetNodeOrNull<DamageComponent>("Damage");
		if (_damage == null)
		{
			_damage = new DamageComponent { Name = "Damage" };
			AddChild(_damage);               // it self-reads [damage] from Config in its own _Ready
		}
	}

	// CONTROL — forces/torques applied each physics tick (standard RigidBody pattern). All inputs via
	// InputMap actions. Auto-level runs only when you're NOT actively pitching/rolling (assisted feel).
	public override void _PhysicsProcess(double delta)
	{
		Basis b = GlobalTransform.Basis;

		// Thrust along the nose (-Z). accelerate pushes forward, decelerate brakes/reverses.
		float thrustIn = Input.GetActionStrength("accelerate") - Input.GetActionStrength("decelerate");
		float force = thrustIn >= 0.0f ? ThrustForce : BrakeForce;
		ApplyCentralForce(-b.Z * thrustIn * force);

		// Rotation input → torque about the body's local axes. Sign flips via the invert toggles + the
		// GetAxis arg order; confirm the directions feel right in the playtest (that's what they're for).
		float pitchIn = Input.GetAxis("steer_down", "steer_up") * (InvertPitch ? 1.0f : -1.0f);
		float yawIn   = Input.GetAxis("steer_right", "steer_left");
		float rollIn  = Input.GetAxis("roll_right", "roll_left") * (InvertRoll ? 1.0f : -1.0f);
		ApplyTorque(b.X * pitchIn * PitchTorque);
		ApplyTorque(b.Y * yawIn   * YawTorque);
		ApplyTorque(b.Z * rollIn  * RollTorque);

		// Coordinated turn: yaw adds proportional roll so the car banks INTO the turn.
		ApplyTorque(b.Z * (-yawIn) * BankCoordination * RollTorque);

		// PD auto-level: when not manually pitching/rolling, restore local-up toward world-up. (b.Y × Up)
		// is a horizontal axis → it levels pitch+roll but does NOT yaw, so your heading is preserved.
		if (Mathf.Abs(pitchIn) < 0.01f && Mathf.Abs(rollIn) < 0.01f)
		{
			Vector3 levelAxis = b.Y.Cross(Vector3.Up);
			ApplyTorque(levelAxis * LevelStrength - AngularVelocity * LevelDamping);
		}

		// Arcade steering assist: ease the velocity toward where the nose points, so ROTATING the car actually
		// changes your direction of travel. Without this the RigidBody keeps its Newtonian momentum and turning
		// doesn't steer (you drift the old way). Framerate-independent; preserves speed. Grip = 0 -> Newtonian.
		if (Grip > 0.0f)
		{
			Vector3 vel = LinearVelocity;
			float spd0 = vel.Length();
			if (spd0 > 1.0f)
			{
				float k = 1.0f - Mathf.Exp(-Grip * (float)delta);
				Vector3 blended = vel + (-b.Z * spd0 - vel) * k;   // ease toward the nose (-Z) at the current speed
				float bl = blended.Length();
				if (bl > 0.001f)
					LinearVelocity = blended / bl * spd0;          // renormalise -> speed kept, heading steered
			}
		}

		float spd = LinearVelocity.Length();
		EmitSignal(SignalName.SpeedChanged, spd, GetSpeedRatio(), thrustIn > 0.01f);
	}

	// DAMAGE + speed clamp — runs in the physics solver callback, where contact impulses are valid.
	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		int contacts = state.GetContactCount();
		float impulse = 0.0f;
		for (int i = 0; i < contacts; i++)
			impulse += state.GetContactImpulse(i).Length();   // GetContactImpulse returns a Vector3 in Godot 4.6
		if (impulse > 0.0f)
			_damage?.ApplyImpact(impulse);

		Vector3 v = state.LinearVelocity;
		if (v.Length() > MaxSpeed)
			state.LinearVelocity = v.Normalized() * MaxSpeed;
	}

	// ---- preserved HUD/camera/FX API (keep the names + signatures) ----
	public float GetSpeed() => LinearVelocity.Length();
	public float GetVerticalSpeed() => LinearVelocity.Y;
	public float GetTopSpeed() => MaxSpeed;
	public float GetMaxVerticalSpeed() => MaxSpeed;          // V-SPD bar scale; vertical can reach top speed in 6-DOF
	public float GetBoostMeter() => GetSpeedRatio();         // THROTTLE bar = current speed fraction
	public float GetSpeedRatio() => Mathf.Clamp(GetSpeed() / Mathf.Max(MaxSpeed, 0.001f), 0.0f, 1.0f);
	public float GetConditionRatio() => _damage?.GetHealthRatio() ?? 1.0f;   // NEW — HUD condition bar
	public DamageComponent Damage => _damage;               // NEW — Game subscribes to its Damaged signal

	// Builds a placeholder neon box + collision so you can press Play with zero art. The body itself
	// pitches/rolls now, so the Model child sits at identity and rotates with its parent (no BankModel).
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
