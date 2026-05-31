using Godot;
using System.Collections.Generic;

// One fixed-length segment of the city — DIVEPUNK, milestones M2–M3.
//
// Buildings line both walls of the corridor (centre stays clear). They're split across a few
// MultiMeshInstance3D nodes — ONE per silhouette (box / round / hex / tapered) — so a whole chunk's
// towers still cost only a handful of draw calls (one per shape in use) while the skyline stays varied.
//
// Obstacles (M3) are a small pool of StaticBody3D nodes placed IN the flyable corridor —
// these need real per-object collision (crash) and per-object near-miss detection, which
// a MultiMesh can't provide, so they're individual nodes. They're few (<= MaxObstacles
// per chunk) and pooled per-chunk, so they recycle for free when the chunk does — no
// per-frame instantiate/free. They sit on physics layer "obstacles"; the ship's hitbox /
// near-miss queries monitor that layer.
//
// Generate(index, seed, difficulty) is deterministic (buildings and obstacles use
// independent RNG streams salted off the same seed) and pure (it only touches this
// chunk's own nodes), so it could move to a worker thread later if a profiler asks.
[GlobalClass]
public partial class CityChunk : Node3D
{
	private const string ObstacleGroup = "obstacle";
	private const int ObstacleLayer = 2;          // 1-indexed physics layer for obstacles (see project.godot)

	// M4 neon look — procedural shaders shared by every chunk (see shaders/).
	private static readonly Shader BuildingShader = GD.Load<Shader>("res://shaders/building.gdshader");
	private static readonly Shader HazardShader = GD.Load<Shader>("res://shaders/hazard.gdshader");

	// Building size classes and silhouettes (tuned via the "Building classes" / "Building shapes" groups).
	private enum BuildingClass { Low = 0, Mid = 1, High = 2, Mega = 3 }
	private enum Silhouette { Box = 0, Round = 1, Prism = 2, Taper = 3 }   // index order MUST match GetBuildingMeshes()

	// Both corridor walls, processed in this order (the RNG stream must consume them -1 then +1).
	private static readonly float[] Sides = { -1.0f, 1.0f };

	[ExportGroup("Chunk")]
	[Export] public float ChunkLength = 200.0f;          // metres along -Z (ChunkManager keeps this in sync)

	[ExportGroup("Corridor")]
	[Export] public float CorridorHalfWidth = 120.0f;    // clear flyable half-width
	[Export] public float CorridorFloor = 4.0f;          // corridor floor in metres
	[Export] public float CorridorCeiling = 1700.0f;     // corridor ceiling in metres

	[ExportGroup("Buildings")]
	[Export] public int ColumnsPerSide = 3;              // building columns stacked outward from the corridor
	[Export] public int RowsPerChunk = 5;                // building slots along the chunk's length, per column (÷WorldScale)
	[Export] public float ColumnSpacing = 70.0f;         // X step between columns (×WorldScale)
	[Export] public float EdgeMargin = 12.0f;            // clear gap (m) between the corridor wall and the nearest building face
	[Export(PropertyHint.Range, "0,1")] public float FillChance = 0.6f;      // per-slot chance of a building
	[Export(PropertyHint.Range, "0,1")] public float CellDepthJitter = 0.35f;
	[Export] public float WorldScale = 1.0f;             // set by ChunkManager; >1 enlarges buildings + spacing
	[Export] public bool BuildingWindows = true;         // procedural neon-window shader; false = flat emissive boxes

	[ExportGroup("Building classes")]
	[Export] public float LowWeight = 0.50f;
	[Export] public float MidWeight = 0.38f;
	[Export] public float HighWeight = 0.08f;
	[Export] public float MegaWeight = 0.04f;            // rare; megatowers are also kept off the corridor-edge column
	[Export] public Vector2 LowHeight = new Vector2(30.0f, 80.0f);
	[Export] public Vector2 MidHeight = new Vector2(80.0f, 200.0f);
	[Export] public Vector2 HighHeight = new Vector2(200.0f, 420.0f);
	[Export] public Vector2 MegaHeight = new Vector2(420.0f, 880.0f);
	[Export] public Vector2 LowFootprint = new Vector2(35.0f, 95.0f);
	[Export] public Vector2 MidFootprint = new Vector2(45.0f, 130.0f);
	[Export] public Vector2 HighFootprint = new Vector2(60.0f, 175.0f);
	[Export] public Vector2 MegaFootprint = new Vector2(95.0f, 260.0f);
	[Export] public float FootprintAspectMin = 0.65f;    // per-axis spread on the footprint (square / oblong / slab)
	[Export] public float FootprintAspectMax = 1.6f;

	[ExportGroup("Building shapes")]
	[Export] public float ShapeBoxWeight = 0.5f;
	[Export] public float ShapeRoundWeight = 0.2f;
	[Export] public float ShapePrismWeight = 0.15f;
	[Export] public float ShapeTaperWeight = 0.15f;

	[ExportGroup("Obstacles")]
	[Export] public int SafeChunks = 2;                  // first N chunks have no obstacles (a warm-up runway)
	[Export] public int MinObstacles = 1;                // obstacle count at lowest difficulty (past the warm-up)
	[Export] public int MaxObstacles = 6;                // obstacle count at full difficulty (also the pool size)
	[Export(PropertyHint.Range, "0,1")] public float ObstacleXFraction = 0.85f;  // obstacles span ± this fraction of the corridor half-width
	[Export(PropertyHint.Range, "0,1")] public float ObstacleYFraction = 0.85f;  // ...and this fraction of the floor→ceiling height, centred
	[Export] public Vector3 ObstacleMinSize = new Vector3(4.0f, 6.0f, 4.0f);
	[Export] public Vector3 ObstacleMaxSize = new Vector3(12.0f, 44.0f, 12.0f);
	[Export] public bool HazardPulse = true;             // pulsing-emissive + fresnel telegraph shader

	// Shared across every chunk so we allocate the meshes + materials once, not one per chunk/instance.
	private static Mesh[] _buildingMeshes;               // one unit mesh per Silhouette, all sharing _buildingMat
	private static ShaderMaterial _buildingMat;
	private static BoxMesh _obstacleMesh;
	private static ShaderMaterial _obstacleMat;

	public int Index = 0;
	private List<MultiMesh> _mms = new();                // one per silhouette, index-aligned with _mmis
	private List<MultiMeshInstance3D> _mmis = new();
	private List<StaticBody3D> _obstacles = new();

	public override void _Ready()
	{
		EnsureMultimeshes();
		EnsureObstacles();
	}

	// Deterministically (re)builds this chunk's buildings + obstacles for a given chunk index.
	public void Generate(int pIndex, long baseSeed, float difficulty)
	{
		Index = pIndex;
		if (_mms.Count == 0)
			EnsureMultimeshes();
		if (_obstacles.Count == 0)
			EnsureObstacles();
		float diff = Mathf.Clamp(difficulty, 0.0f, 1.0f);
		GenerateBuildings(baseSeed, pIndex, diff);
		GenerateObstacles(baseSeed, pIndex, diff);
	}

	private void GenerateBuildings(long baseSeed, int pIndex, float diff)
	{
		// Compute the layout (pure, testable), then split it across one MultiMesh per silhouette and upload.
		var (transforms, colors, shapes) = ComputeBuildingLayout(baseSeed, pIndex, diff);

		// Bucket each instance under its silhouette.
		var bucketT = new List<List<Transform3D>>();
		var bucketC = new List<List<Color>>();
		for (int k = 0; k < _mms.Count; k++)
		{
			bucketT.Add(new List<Transform3D>());
			bucketC.Add(new List<Color>());
		}
		for (int i = 0; i < transforms.Count; i++)
		{
			int k = Mathf.Clamp(shapes[i], 0, _mms.Count - 1);
			bucketT[k].Add(transforms[i]);
			bucketC[k].Add(colors[i]);
		}

		for (int k = 0; k < _mms.Count; k++)
		{
			List<Transform3D> ts = bucketT[k];
			List<Color> cs = bucketC[k];
			MultiMesh mm = _mms[k];
			mm.InstanceCount = ts.Count;
			for (int i = 0; i < ts.Count; i++)
			{
				mm.SetInstanceTransform(i, ts[i]);
				mm.SetInstanceColor(i, cs[i]);
			}
			_mmis[k].Visible = ts.Count > 0;
		}
	}

	// Deterministically computes this chunk's building transforms, colors and silhouettes (no rendering
	// side effects, so it's unit-testable headless where MultiMesh readback isn't). Returns
	// (transforms, colors, shapes), where shapes[i] is one of the Silhouette values.
	public (List<Transform3D> transforms, List<Color> colors, List<int> shapes) ComputeBuildingLayout(long baseSeed, int pIndex, float diff)
	{
		var rng = new RandomNumberGenerator();
		rng.Seed = (ulong)MixSeed(baseSeed, pIndex, 0);
		float scale = Mathf.Max(WorldScale, 0.01f);
		int effRows = Mathf.Max(1, RoundHalfAway((float)RowsPerChunk / scale));
		float rowSpacing = ChunkLength / (float)effRows;

		var transforms = new List<Transform3D>();
		var colors = new List<Color>();
		var shapes = new List<int>();

		float classTotal = Mathf.Max(LowWeight, 0.0f) + Mathf.Max(MidWeight, 0.0f) + Mathf.Max(HighWeight, 0.0f) + Mathf.Max(MegaWeight, 0.0f);
		float shapeTotal = Mathf.Max(ShapeBoxWeight, 0.0f) + Mathf.Max(ShapeRoundWeight, 0.0f) + Mathf.Max(ShapePrismWeight, 0.0f) + Mathf.Max(ShapeTaperWeight, 0.0f);

		foreach (float side in Sides)
		{
			for (int col = 0; col < ColumnsPerSide; col++)
			{
				for (int row = 0; row < effRows; row++)
				{
					if (rng.Randf() > FillChance)
						continue;   // leave a gap
					BuildingClass cls = PickClass(rng, classTotal, col);
					Vector2 hRange = ClassHeight(cls);
					Vector2 fRange = ClassFootprint(cls);
					// Footprint: a per-building base size from the class range, then an INDEPENDENT aspect roll
					// on each axis, so towers vary widely in both size and proportion (square / oblong / slab).
					float fp = rng.RandfRange(fRange.X, fRange.Y);
					float fx = fp * rng.RandfRange(FootprintAspectMin, FootprintAspectMax) * scale;
					float fz = fp * rng.RandfRange(FootprintAspectMin, FootprintAspectMax) * scale;
					float height = rng.RandfRange(hRange.X, hRange.Y);
					height *= 0.65f + 0.35f * diff;        // taller as difficulty ramps
					height *= 1.0f + 0.12f * (float)col;   // outer columns a touch taller
					height *= scale;                       // ...and overall bigger with WorldScale
					float yaw = rng.RandfRange(-0.12f, 0.12f);
					// Anchor each building by its INNER face so it grows OUTWARD from the corridor as it gets
					// wider — the flyable tube stays clear at ANY footprint. halfX is the rotated footprint's
					// X half-extent, so the yaw never lets a corner creep inward. Columns step outward by `step`.
					float step = ColumnSpacing * scale;
					float colInner = CorridorHalfWidth + EdgeMargin + step * (float)col;
					float halfX = 0.5f * (Mathf.Abs(fx * Mathf.Cos(yaw)) + Mathf.Abs(fz * Mathf.Sin(yaw)));
					float x = side * (colInner + halfX + rng.RandfRange(0.0f, 0.25f) * step);
					float z = -((float)row + 0.5f) * rowSpacing
						+ rng.RandfRange(-rowSpacing, rowSpacing) * CellDepthJitter;
					Silhouette shape = PickShape(rng, shapeTotal);
					Basis basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(fx, height, fz));
					transforms.Add(new Transform3D(basis, new Vector3(x, height * 0.5f, z)));
					colors.Add(Color.FromHsv(rng.Randf(), 0.22f, rng.RandfRange(0.12f, 0.30f)));
					shapes.Add((int)shape);
				}
			}
		}

		return (transforms, colors, shapes);
	}

	// Weighted pick of a size class. Megatowers are demoted to high-rise on the innermost column (col 0)
	// so their wide footprints never reach into the flyable corridor.
	private BuildingClass PickClass(RandomNumberGenerator rng, float total, int col)
	{
		float r = rng.Randf() * Mathf.Max(total, 0.0001f);
		float a = Mathf.Max(LowWeight, 0.0f);
		float b = a + Mathf.Max(MidWeight, 0.0f);
		float c = b + Mathf.Max(HighWeight, 0.0f);
		BuildingClass cls = BuildingClass.Mega;
		if (r < a)
			cls = BuildingClass.Low;
		else if (r < b)
			cls = BuildingClass.Mid;
		else if (r < c)
			cls = BuildingClass.High;
		if (col == 0 && cls == BuildingClass.Mega)
			cls = BuildingClass.High;
		return cls;
	}

	private Vector2 ClassHeight(BuildingClass cls) => cls switch
	{
		BuildingClass.Low => LowHeight,
		BuildingClass.Mid => MidHeight,
		BuildingClass.High => HighHeight,
		_ => MegaHeight,
	};

	private Vector2 ClassFootprint(BuildingClass cls) => cls switch
	{
		BuildingClass.Low => LowFootprint,
		BuildingClass.Mid => MidFootprint,
		BuildingClass.High => HighFootprint,
		_ => MegaFootprint,
	};

	private Silhouette PickShape(RandomNumberGenerator rng, float total)
	{
		float r = rng.Randf() * Mathf.Max(total, 0.0001f);
		float a = Mathf.Max(ShapeBoxWeight, 0.0f);
		float b = a + Mathf.Max(ShapeRoundWeight, 0.0f);
		float c = b + Mathf.Max(ShapePrismWeight, 0.0f);
		if (r < a)
			return Silhouette.Box;
		if (r < b)
			return Silhouette.Round;
		if (r < c)
			return Silhouette.Prism;
		return Silhouette.Taper;
	}

	private void GenerateObstacles(long baseSeed, int pIndex, float diff)
	{
		var rng = new RandomNumberGenerator();
		rng.Seed = (ulong)MixSeed(baseSeed, pIndex, 1);

		int count = 0;
		if (pIndex >= SafeChunks)
			count = RoundHalfAway(Mathf.Lerp((float)MinObstacles, (float)MaxObstacles, diff));
		count = Mathf.Clamp(count, 0, _obstacles.Count);

		for (int i = 0; i < _obstacles.Count; i++)
		{
			StaticBody3D ob = _obstacles[i];
			var shape = ob.GetNode<CollisionShape3D>("Col");
			var mesh = ob.GetNode<MeshInstance3D>("Mesh");
			if (i >= count)
			{
				ob.Visible = false;
				shape.Disabled = true;
				continue;
			}
			Vector3 size = new Vector3(
				rng.RandfRange(ObstacleMinSize.X, ObstacleMaxSize.X),
				rng.RandfRange(ObstacleMinSize.Y, ObstacleMaxSize.Y),
				rng.RandfRange(ObstacleMinSize.Z, ObstacleMaxSize.Z)
			);
			// Spread the active obstacles along the chunk's length, jittered within their slot,
			// and across the FULL corridor cross-section on X and Y (not just a central box).
			float slot = ((float)i + rng.RandfRange(0.2f, 0.8f)) / (float)Mathf.Max(count, 1);
			float xSpan = CorridorHalfWidth * ObstacleXFraction;
			float yLo = Mathf.Lerp(CorridorFloor, CorridorCeiling, 0.5f - 0.5f * ObstacleYFraction);
			float yHi = Mathf.Lerp(CorridorFloor, CorridorCeiling, 0.5f + 0.5f * ObstacleYFraction);
			Vector3 pos = new Vector3(
				rng.RandfRange(-xSpan, xSpan),
				rng.RandfRange(yLo, yHi),
				-slot * ChunkLength
			);
			ob.Position = pos;
			((BoxShape3D)shape.Shape).Size = size;
			shape.Disabled = false;
			mesh.Scale = size;
			ob.Visible = true;
		}
	}

	private void EnsureMultimeshes()
	{
		Mesh[] meshes = GetBuildingMeshes(BuildingWindows, WorldScale);
		if (_mmis.Count == 0)
		{
			for (int k = 0; k < meshes.Length; k++)
			{
				var mmi = new MultiMeshInstance3D();
				mmi.Name = $"Buildings{k}";
				var mm = new MultiMesh();
				mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
				mm.UseColors = true;                 // must be set before InstanceCount; feeds the shader's COLOR
				mm.Mesh = meshes[k];
				mmi.Multimesh = mm;
				AddChild(mmi);
				_mmis.Add(mmi);
				_mms.Add(mm);
			}
		}
		else
		{
			for (int k = 0; k < _mms.Count; k++)
				_mms[k].Mesh = meshes[k];
		}
	}

	private void EnsureObstacles()
	{
		if (_obstacles.Count > 0)
			return;
		for (int i = 0; i < MaxObstacles; i++)
		{
			var ob = new StaticBody3D();
			ob.Name = $"Obstacle{i}";
			ob.CollisionLayer = 0;
			ob.SetCollisionLayerValue(ObstacleLayer, true);   // obstacles sit on the "obstacles" layer
			ob.CollisionMask = 0;                             // they don't detect anything themselves
			ob.AddToGroup(ObstacleGroup);

			var shape = new CollisionShape3D();
			shape.Name = "Col";
			shape.Shape = new BoxShape3D();                   // per-obstacle shape (size set in Generate)
			shape.Disabled = true;
			ob.AddChild(shape);

			var mesh = new MeshInstance3D();
			mesh.Name = "Mesh";
			mesh.Mesh = GetObstacleMesh();                    // shared unit cube, scaled per obstacle
			mesh.MaterialOverride = GetObstacleMat(HazardPulse);
			ob.AddChild(mesh);

			ob.Visible = false;
			AddChild(ob);
			_obstacles.Add(ob);
		}
	}

	// A deterministic, order-independent mix of (seed, chunk index, salt) -> RNG seed.
	// The salt lets buildings (0) and obstacles (1) use independent streams off one world seed,
	// so tuning one never reshuffles the other. Uses 64-bit wrapping math (long + unchecked) to
	// match GDScript's 64-bit int overflow — C# int is 32-bit and would mix differently.
	private static long MixSeed(long baseSeed, long idx, long salt)
	{
		unchecked
		{
			long h = baseSeed * 73856093L;
			h ^= idx * 19349663L;
			h ^= (idx >> 3) * 83492791L;   // >> on long is arithmetic shift, matching GDScript int
			h ^= salt * 50331653L;
			return h;
		}
	}

	// GDScript round()/roundi() round half AWAY from zero; C# Math.Round is banker's (half to even).
	// This feeds RNG-consuming loop counts (eff rows, obstacle count), so it must match GDScript exactly.
	private static int RoundHalfAway(float x)
	{
		return (int)(x >= 0.0f ? Mathf.Floor(x + 0.5f) : Mathf.Ceil(x - 0.5f));
	}

	// One shared neon-window ShaderMaterial + one UNIT mesh per silhouette, for all building instances
	// (M4). The MultiMesh feeds the per-building hue through COLOR; the shader turns it into lit windows
	// laid out in WORLD space, so the pane size is multiplied by WorldScale to stay proportional to the
	// enlarged towers. Every mesh fits a 1×1×1 box, so the per-instance basis scale (fx, height, fz) sets
	// the real dimensions. Cached statically; `windows` is a global [fx] toggle.
	private static Mesh[] GetBuildingMeshes(bool windows, float pWorldScale)
	{
		if (_buildingMat == null)
		{
			_buildingMat = new ShaderMaterial();
			_buildingMat.Shader = BuildingShader;
		}
		_buildingMat.SetShaderParameter("windows_on", windows ? 1.0f : 0.0f);
		_buildingMat.SetShaderParameter("window_size_v", 4.0f * pWorldScale);
		_buildingMat.SetShaderParameter("window_size_h", 5.0f * pWorldScale);
		if (_buildingMeshes == null)
		{
			// Index order MUST match the Silhouette enum (Box, Round, Prism, Taper).
			var box = new BoxMesh();
			box.Size = Vector3.One;
			box.Material = _buildingMat;

			var roundTower = new CylinderMesh();
			roundTower.Height = 1.0f;
			roundTower.TopRadius = 0.5f;
			roundTower.BottomRadius = 0.5f;
			roundTower.RadialSegments = 20;
			roundTower.Material = _buildingMat;

			var prism = new CylinderMesh();
			prism.Height = 1.0f;
			prism.TopRadius = 0.5f;
			prism.BottomRadius = 0.5f;
			prism.RadialSegments = 6;
			prism.Material = _buildingMat;

			var taper = new CylinderMesh();
			taper.Height = 1.0f;
			taper.TopRadius = 0.28f;
			taper.BottomRadius = 0.5f;
			taper.RadialSegments = 20;
			taper.Material = _buildingMat;

			_buildingMeshes = new Mesh[] { box, roundTower, prism, taper };
		}
		return _buildingMeshes;
	}

	private static BoxMesh GetObstacleMesh()
	{
		if (_obstacleMesh == null)
		{
			_obstacleMesh = new BoxMesh();
			_obstacleMesh.Size = Vector3.One;
		}
		return _obstacleMesh;
	}

	// Warm amber, pulsing + fresnel-rimmed (the shared hazard shader) — deliberately distinct from the
	// cool-blue buildings so obstacles read instantly as "danger" at speed (spec §5.11, fairness via
	// telegraphing). Cached statically; `pulse` is a global [fx] toggle. pulse = false = steady amber.
	private static ShaderMaterial GetObstacleMat(bool pulse)
	{
		if (_obstacleMat == null)
		{
			_obstacleMat = new ShaderMaterial();
			_obstacleMat.Shader = HazardShader;
			_obstacleMat.SetShaderParameter("base_color", new Color(0.6f, 0.25f, 0.05f));
			_obstacleMat.SetShaderParameter("emission_color", new Color(1.0f, 0.45f, 0.1f));
			_obstacleMat.SetShaderParameter("emission_energy", 2.6f);
			_obstacleMat.SetShaderParameter("pulse_on", pulse ? 1.0f : 0.0f);
		}
		return _obstacleMat;
	}
}
