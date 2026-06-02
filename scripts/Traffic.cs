using Godot;
using System.Collections.Generic;

// Ambient flying traffic — a FIXED population of collidable cars roaming the bounded ~8 km world
// (open-world reframe). No AI: each car gets a random position, altitude, horizontal heading and speed
// at spawn, then cruises in a straight line forever, toroidally wrapping at the world edge so the
// population never leaves the box and never needs respawning. Cars are AnimatableBody3D with
// SyncToPhysics on, so Jolt imparts their motion to the player on contact (a real shove + impulse →
// damage via Ship._IntegrateForces). They live on the player's collision layer (1) and mask NOTHING (0),
// so they ignore buildings / terrain / each other (no jitter, no avoidance) and only ever matter when the
// player flies into one. Hot magenta hazard shader so they read as moving hazards. Fully deterministic
// from Seed (no Randomize) → identical population every launch.
[GlobalClass]
public partial class Traffic : Node3D
{
	// Shared hazard shader (pulsing emissive + fresnel rim) — same one the static obstacles used.
	private static readonly Shader HazardShader = GD.Load<Shader>("res://shaders/hazard.gdshader");

	[ExportGroup("Population")]
	[Export] public int Count = 24;                                     // fixed number of cars (built once in _Ready)
	[Export] public float MinSpeed = 25.0f;                            // slowest cruise (m/s)
	[Export] public float MaxSpeed = 80.0f;                            // fastest cruise (m/s) — keep well under [flight] max_speed (200) so hits land
	[Export] public Vector2 AltitudeBand = new Vector2(60.0f, 400.0f); // (min,max) cruise altitude (world Y): above the streets, below the megatowers
	[Export] public float SpawnRadius = 1500.0f;                       // cars start within this radius (m) of the world centre

	[ExportGroup("Car")]
	[Export] public Vector3 CarSize = new Vector3(5.0f, 2.0f, 9.0f);   // a touch bigger than the player car so it reads as traffic
	[Export] public bool HazardPulse = true;                           // pulsing-emissive telegraph shader ([fx] hazard_pulse)

	[ExportGroup("Generation")]
	[Export] public int Seed = 1337;                                   // deterministic layout seed (any int)

	// Shared mesh + material across every car (one allocation, not one per car).
	private static BoxMesh _carMesh;
	private static ShaderMaterial _carMat;

	// Runtime context pushed in by Game before AddChild (world facts, not Inspector tunables).
	private float _extent = 8000.0f;
	private Vector3 _center = Vector3.Zero;

	private readonly List<AnimatableBody3D> _cars = new();
	private readonly List<Vector3> _velocities = new();
	private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

	// Game calls this BEFORE AddChild so _Ready builds the population with the real world extent + centre.
	public void Initialize(float worldExtent, Vector3 worldCenter)
	{
		_extent = worldExtent;
		_center = worldCenter;
	}

	public override void _Ready()
	{
		_rng.Seed = (ulong)Seed;   // deterministic: any int (incl. 0 / negative) is a valid, stable seed
		BuildPopulation();
	}

	// One straight-line move per car per physics tick, then toroidal wrap on the world bounds. Runs in
	// _PhysicsProcess (NOT _Process) so SyncToPhysics feeds Jolt the per-tick motion → real contact impulse.
	// Uses LOCAL Position: the Traffic node sits at the world origin, so a car's local position IS its world
	// position, and staying in local space sidesteps the sync_to_physics global-transform timing quirks.
	public override void _PhysicsProcess(double delta)
	{
		float d = (float)delta;
		float half = _extent * 0.5f;
		for (int i = 0; i < _cars.Count; i++)
		{
			Vector3 p = _cars[i].Position + _velocities[i] * d;
			// Wrap on X/Z. A single tick can't move further than `extent`, so one add/subtract suffices.
			// The wrap seam sits at the world edge (~km from the player's usual position), so its one-frame
			// transform jump never teleports a car onto the player.
			if (p.X > half) p.X -= _extent; else if (p.X < -half) p.X += _extent;
			if (p.Z > half) p.Z -= _extent; else if (p.Z < -half) p.Z += _extent;
			_cars[i].Position = p;   // basis is fixed at spawn; only the position changes
		}
	}

	private void BuildPopulation()
	{
		for (int i = 0; i < Mathf.Max(Count, 0); i++)
		{
			// Uniform-area disc around the world centre + a random cruise altitude.
			float r = Mathf.Sqrt(_rng.Randf()) * SpawnRadius;
			float discAngle = _rng.Randf() * Mathf.Tau;
			float y = _rng.RandfRange(AltitudeBand.X, AltitudeBand.Y);
			Vector3 pos = new Vector3(_center.X + Mathf.Cos(discAngle) * r, y, _center.Z + Mathf.Sin(discAngle) * r);

			// Random horizontal heading (Y == 0 → never parallel to Up) + constant speed.
			float headAngle = _rng.Randf() * Mathf.Tau;
			Vector3 heading = new Vector3(Mathf.Cos(headAngle), 0.0f, Mathf.Sin(headAngle)); // unit, horizontal
			float speed = _rng.RandfRange(MinSpeed, MaxSpeed);

			var car = new AnimatableBody3D
			{
				Name = $"Car{i}",
				SyncToPhysics = true,   // Jolt imparts the car's motion to the player on contact (so move it in _PhysicsProcess)
				CollisionLayer = 1,     // share the player's layer so the player (mask 1) collides → bounce + damage
				CollisionMask = 0,      // scan nothing: ignore buildings / terrain / other cars (no avoidance, no jitter)
			};
			car.AddToGroup("traffic");

			car.AddChild(new CollisionShape3D { Name = "Col", Shape = new BoxShape3D { Size = CarSize } });
			car.AddChild(new MeshInstance3D
			{
				Name = "Mesh",
				Mesh = GetCarMesh(),                       // shared unit cube...
				Scale = CarSize,                           // ...scaled to the car size (visual only — does NOT touch the shape)
				MaterialOverride = GetCarMat(HazardPulse),
			});

			// Place + orient in LOCAL space BEFORE AddChild. The Traffic node sits at the world origin, so a
			// car's local transform IS its world transform. This is load-bearing: setting GlobalPosition in
			// _Ready does NOT stick on a sync_to_physics body (it must be moved in _PhysicsProcess), which
			// would otherwise leave every car piled at the origin. Build a proper (det +1) basis by hand so the
			// box's long axis (local -Z) faces travel — LookAt can't run here (the node isn't in-tree yet).
			Vector3 back = -heading;                                   // local +Z points opposite travel
			Vector3 right = Vector3.Up.Cross(back).Normalized();
			Vector3 up = back.Cross(right).Normalized();
			car.Transform = new Transform3D(new Basis(right, up, back), pos);
			AddChild(car);

			_cars.Add(car);
			_velocities.Add(heading * speed);
		}
	}

	// One shared unit-cube mesh for all cars (scaled per-car via MeshInstance3D.Scale).
	private static BoxMesh GetCarMesh()
	{
		if (_carMesh == null)
		{
			_carMesh = new BoxMesh();
			_carMesh.Size = Vector3.One;
		}
		return _carMesh;
	}

	// Hot magenta, pulsing + fresnel-rimmed (the shared hazard shader) — distinct from the buildings, so
	// moving traffic reads instantly as a separate hazard. Cached statically; `pulse` is a global [fx]
	// toggle (false = steady magenta). (Cooling this to the cold palette is a separate backlog pass.)
	private static ShaderMaterial GetCarMat(bool pulse)
	{
		if (_carMat == null)
		{
			_carMat = new ShaderMaterial();
			_carMat.Shader = HazardShader;
			_carMat.SetShaderParameter("base_color", new Color(0.55f, 0.05f, 0.5f));
			_carMat.SetShaderParameter("emission_color", new Color(1.0f, 0.1f, 0.85f));
			_carMat.SetShaderParameter("emission_energy", 2.6f);
			_carMat.SetShaderParameter("pulse_on", pulse ? 1.0f : 0.0f);
		}
		return _carMat;
	}
}
