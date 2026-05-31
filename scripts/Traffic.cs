using Godot;
using System.Collections.Generic;

// Moving "traffic" — cars cruising the corridor in both directions at random speeds (M3+).
//
// A pool of code-moved StaticBody3D cars that live on the OBSTACLE layer and in the obstacle
// group, so the ship's existing crash / near-miss shape queries (Ship.cs) detect them with no
// extra wiring — exactly like the static obstacles, but these move. Cars occupy a Z-window
// around the ship and recycle (fresh random position / speed / direction) when they drift out
// of range, so the population stays constant no matter how far you fly (no per-frame
// instantiate / free). Their colour is a distinct hot magenta so they read instantly as moving
// hazards, separate from the amber static obstacles and the blue buildings.
[GlobalClass]
public partial class Traffic : Node3D
{
	private const string ObstacleGroup = "obstacle";
	private const int ObstacleLayer = 2;          // 1-indexed physics layer for obstacles (matches Ship.cs / project.godot)

	// Shared M4 hazard shader (pulsing emissive + fresnel rim) — same one the static obstacles use.
	private static readonly Shader HazardShader = GD.Load<Shader>("res://shaders/hazard.gdshader");

	[ExportGroup("Traffic")]
	[Export] public int CarCount = 24;               // how many moving cars exist at once (the pool size)
	[Export] public float MinSpeed = 30.0f;          // slowest car cruise speed (m/s)
	[Export] public float MaxSpeed = 140.0f;         // fastest car cruise speed (m/s)
	[Export(PropertyHint.Range, "0,1")] public float TowardFraction = 0.5f;  // fraction of cars heading TOWARD the player (+Z)

	[ExportGroup("Window")]
	[Export] public float SpawnAhead = 2800.0f;      // how far ahead (-Z) of the ship cars live / re-enter
	[Export] public float SpawnBehind = 600.0f;      // how far behind (+Z) the ship a car persists before recycling

	[ExportGroup("Corridor")]
	[Export] public float CorridorHalfWidth = 120.0f;
	[Export] public float CorridorFloor = 4.0f;
	[Export] public float CorridorCeiling = 1700.0f;
	[Export(PropertyHint.Range, "0,1")] public float XFraction = 0.9f;   // cars span ± this fraction of the corridor half-width
	[Export(PropertyHint.Range, "0,1")] public float YFraction = 0.9f;   // ...and this fraction of the floor→ceiling height, centred

	[ExportGroup("Car")]
	[Export] public Vector3 CarSize = new Vector3(5.0f, 2.0f, 9.0f);   // a touch bigger than the player car so it reads as traffic
	[Export] public bool HazardPulse = true;         // pulsing-emissive + fresnel telegraph shader

	[ExportGroup("Generation")]
	[Export] public int WorldSeed = 0;               // 0 = random; >0 = reproducible initial layout

	// Shared mesh + material across every car (one allocation, not one per car).
	private static BoxMesh _carMesh;
	private static ShaderMaterial _carMat;

	private Node3D _target;
	private List<StaticBody3D> _cars = new();
	private List<float> _velZ = new();     // per-car signed Z velocity (m/s): + heads toward the player
	private RandomNumberGenerator _rng = new RandomNumberGenerator();

	public override void _Ready()
	{
		long s = WorldSeed > 0 ? WorldSeed : RandomSeed();
		_rng.Seed = (ulong)s;
		BuildPool();
	}

	public void SetTarget(Node3D t)
	{
		_target = t;
		if (_target != null)
			ScatterInitial(_target.GlobalPosition.Z);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_target == null)
			return;
		float d = (float)delta;
		float shipZ = _target.GlobalPosition.Z;
		for (int i = 0; i < _cars.Count; i++)
		{
			StaticBody3D car = _cars[i];
			Vector3 p = car.Position;
			p.Z += _velZ[i] * d;
			car.Position = p;
			// Recycle when the car drifts out of the window around the ship (ahead = -Z).
			if (p.Z > shipZ + SpawnBehind || p.Z < shipZ - SpawnAhead)
				Respawn(i, shipZ);
		}
	}

	// Build the car pool once (StaticBody3D + Col + Mesh), hidden until placed.
	private void BuildPool()
	{
		for (int i = 0; i < Mathf.Max(CarCount, 0); i++)
		{
			var car = new StaticBody3D();
			car.Name = $"Car{i}";
			car.CollisionLayer = 0;
			car.SetCollisionLayerValue(ObstacleLayer, true);   // detectable on the obstacles layer
			car.CollisionMask = 0;                             // cars detect nothing themselves
			car.AddToGroup(ObstacleGroup);

			var col = new CollisionShape3D();
			col.Name = "Col";
			var box = new BoxShape3D();
			box.Size = CarSize;
			col.Shape = box;
			car.AddChild(col);

			var mesh = new MeshInstance3D();
			mesh.Name = "Mesh";
			mesh.Mesh = GetCarMesh();                // shared unit cube...
			mesh.MaterialOverride = GetCarMat(HazardPulse);
			mesh.Scale = CarSize;                     // ...scaled to the car size
			car.AddChild(mesh);

			car.Visible = false;
			AddChild(car);
			_cars.Add(car);
			_velZ.Add(0.0f);
		}
	}

	// Spread all cars randomly through the whole window on the first placement, so the world is
	// populated immediately rather than streaming in from the far plane.
	private void ScatterInitial(float shipZ)
	{
		float span = SpawnAhead + SpawnBehind;
		for (int i = 0; i < _cars.Count; i++)
		{
			Respawn(i, shipZ);
			StaticBody3D car = _cars[i];
			Vector3 p = car.Position;
			p.Z = shipZ + SpawnBehind - _rng.RandfRange(0.0f, span);
			car.Position = p;
		}
	}

	// (Re)place car i with a fresh random cross-section position, speed and direction, entering at
	// whichever Z edge lets it traverse the window given its motion RELATIVE to the ship (which
	// flies -Z). rel >= 0 => it drifts toward +Z (behind), so enter from the far-ahead edge;
	// rel < 0 => it drifts toward -Z (ahead), so enter from behind. Without this, fast "away" cars
	// spawned ahead would cross the ahead boundary on the very next frame and churn.
	private void Respawn(int i, float shipZ)
	{
		StaticBody3D car = _cars[i];
		float xSpan = CorridorHalfWidth * XFraction;
		float yLo = Mathf.Lerp(CorridorFloor, CorridorCeiling, 0.5f - 0.5f * YFraction);
		float yHi = Mathf.Lerp(CorridorFloor, CorridorCeiling, 0.5f + 0.5f * YFraction);
		bool toward = _rng.Randf() < TowardFraction;
		float vz = _rng.RandfRange(MinSpeed, MaxSpeed) * (toward ? 1.0f : -1.0f);
		_velZ[i] = vz;

		float playerSpeed = 0.0f;
		if (_target is Ship ship)
			playerSpeed = ship.GetSpeed();
		float rel = vz + playerSpeed;
		float z;
		if (rel >= 0.0f)
			z = shipZ - SpawnAhead + _rng.RandfRange(0.0f, SpawnAhead * 0.1f);
		else
			z = shipZ + SpawnBehind - _rng.RandfRange(0.0f, SpawnBehind * 0.5f);

		car.Position = new Vector3(_rng.RandfRange(-xSpan, xSpan), _rng.RandfRange(yLo, yHi), z);
		car.Visible = true;
	}

	private long RandomSeed()
	{
		var r = new RandomNumberGenerator();
		r.Randomize();
		return (long)r.Randi() + 1;   // +1 so it's never 0 (0 means "random")
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

	// Hot magenta, pulsing + fresnel-rimmed (the shared hazard shader) — distinct from the amber static
	// obstacles and the cool-blue buildings, so moving traffic reads instantly as a separate hazard.
	// Cached statically; `pulse` is a global [fx] toggle (false = steady magenta).
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
