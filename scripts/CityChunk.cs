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
	private static readonly Shader BeaconShader = GD.Load<Shader>("res://shaders/beacon.gdshader");

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
	[Export(PropertyHint.Range, "0,1")] public float SkybridgeChance = 0.35f;  // chance an adjacent same-side tower pair is linked by a skybridge

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
	private static CylinderMesh _antennaMesh;            // one shared tapered mast, scaled per roof
	private static StandardMaterial3D _antennaMat;       // dark metal
	private static SphereMesh _beaconMesh;               // one shared red light, scaled per roof
	private static ShaderMaterial _beaconMat;            // blinking emissive (beacon.gdshader)
	private static CylinderMesh _spireMesh;              // pointed roof cap (cone), scaled per roof
	private static SphereMesh _domeMesh;                 // dome roof cap, scaled per roof
	private static StandardMaterial3D _structureMat;     // dark concrete/metal for rooftop caps

	public int Index = 0;
	private List<MultiMesh> _mms = new();                // one per silhouette, index-aligned with _mmis
	private List<MultiMeshInstance3D> _mmis = new();
	private List<StaticBody3D> _obstacles = new();
	private MultiMesh _antennaMm;                         // rooftop masts for the whole chunk (one draw call)
	private MultiMeshInstance3D _antennaMmi;
	private MultiMesh _beaconMm;                          // rooftop red beacons for the whole chunk (one draw call)
	private MultiMeshInstance3D _beaconMmi;
	private MultiMesh _spireMm;                           // rooftop spires/cones for the whole chunk
	private MultiMeshInstance3D _spireMmi;
	private MultiMesh _domeMm;                            // rooftop domes for the whole chunk
	private MultiMeshInstance3D _domeMmi;

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
		BuildingLayout layout = ComputeBuildingLayout(baseSeed, pIndex, diff);

		// Bucket each instance under its silhouette (transform + window colour + lighting-profile custom data).
		var bucketT = new List<List<Transform3D>>();
		var bucketC = new List<List<Color>>();
		var bucketX = new List<List<Color>>();
		for (int k = 0; k < _mms.Count; k++)
		{
			bucketT.Add(new List<Transform3D>());
			bucketC.Add(new List<Color>());
			bucketX.Add(new List<Color>());
		}
		for (int i = 0; i < layout.Transforms.Count; i++)
		{
			int k = Mathf.Clamp(layout.Shapes[i], 0, _mms.Count - 1);
			bucketT[k].Add(layout.Transforms[i]);
			bucketC[k].Add(layout.Colors[i]);
			bucketX[k].Add(layout.Custom[i]);
		}

		for (int k = 0; k < _mms.Count; k++)
		{
			List<Transform3D> ts = bucketT[k];
			List<Color> cs = bucketC[k];
			List<Color> xs = bucketX[k];
			MultiMesh mm = _mms[k];
			mm.InstanceCount = ts.Count;
			for (int i = 0; i < ts.Count; i++)
			{
				mm.SetInstanceTransform(i, ts[i]);
				mm.SetInstanceColor(i, cs[i]);          // rgb = window colour, a = lit fraction
				mm.SetInstanceCustomData(i, xs[i]);     // variation, grid class, accent hue, accent amount
			}
			_mmis[k].Visible = ts.Count > 0;
		}

		// Rooftop features ride their own per-chunk MultiMeshes (one draw call each).
		UploadTransforms(_beaconMm, _beaconMmi, layout.Beacons);
		UploadTransforms(_antennaMm, _antennaMmi, layout.Antennas);
		UploadTransforms(_spireMm, _spireMmi, layout.Spires);
		UploadTransforms(_domeMm, _domeMmi, layout.Domes);
	}

	// Pushes a flat list of transforms into a MultiMesh and toggles its instance visible/empty.
	private static void UploadTransforms(MultiMesh mm, MultiMeshInstance3D mmi, List<Transform3D> xforms)
	{
		mm.InstanceCount = xforms.Count;
		for (int i = 0; i < xforms.Count; i++)
			mm.SetInstanceTransform(i, xforms[i]);
		mmi.Visible = xforms.Count > 0;
	}

	// Deterministically computes this chunk's buildings (no rendering side effects, so it's unit-testable
	// headless where MultiMesh readback isn't). Per kept building the RNG stream consumes, in order:
	// class, footprint (base + 2 aspects), height, yaw, x-jitter, z-jitter, shape, window profile
	// (accent hue + archetype + traits), then the roof-feature draws (mast/beacon/cap, see
	// AddRoofFeatures); a final pass links some adjacent towers with skybridges. The builders salt (0)
	// keeps this independent of the obstacle stream (1), so tuning one never reshuffles the other.
	public BuildingLayout ComputeBuildingLayout(long baseSeed, int pIndex, float diff)
	{
		var rng = new RandomNumberGenerator();
		rng.Seed = (ulong)MixSeed(baseSeed, pIndex, 0);
		float scale = Mathf.Max(WorldScale, 0.01f);
		int effRows = Mathf.Max(1, RoundHalfAway((float)RowsPerChunk / scale));
		float rowSpacing = ChunkLength / (float)effRows;

		var layout = new BuildingLayout
		{
			Transforms = new List<Transform3D>(),
			Colors = new List<Color>(),
			Custom = new List<Color>(),
			Shapes = new List<int>(),
			Antennas = new List<Transform3D>(),
			Beacons = new List<Transform3D>(),
			Spires = new List<Transform3D>(),
			Domes = new List<Transform3D>(),
		};

		// Track placed towers (by side/column/row) so the skybridge pass below can link neighbours.
		var placed = new List<PlacedBuilding>();
		var lookup = new Dictionary<(int, int, int), PlacedBuilding>();

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
					layout.Transforms.Add(new Transform3D(basis, new Vector3(x, height * 0.5f, z)));
					layout.Shapes.Add((int)shape);

					// Per-building window lighting profile. COLOR carries the white base + lit fraction;
					// INSTANCE_CUSTOM carries (variation, grid class, accent hue, accent amount). The shader
					// derives its own per-building decorrelation seed from the instance origin.
					WindowProfile wp = PickWindowProfile(rng);
					layout.Colors.Add(new Color(wp.Color.R, wp.Color.G, wp.Color.B, wp.LitFraction));
					layout.Custom.Add(new Color(wp.Variation, wp.GridClass, wp.AccentHue, wp.AccentAmount));

					// Rooftop dressing: mast + beacon on the tallest, and a non-flat cap (setback / spire /
					// dome) on some roofs.
					AddRoofFeatures(rng, layout, cls, x, z, height, fx, fz, yaw, scale);

					var pb = new PlacedBuilding { Side = side, Col = col, Row = row, X = x, Z = z, Top = height, HalfX = halfX };
					placed.Add(pb);
					lookup[(SideIdx(side), col, row)] = pb;
				}
			}
		}

		// Skybridge pass — link some adjacent same-side towers (deterministic, continues the same stream).
		BridgeBuildings(rng, layout, lookup, placed, scale);

		return layout;
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

	// Window light palette. Most windows are WHITE — cold (offices) through warm (homes) — with a
	// minority recoloured to a saturated neon accent. The cold/warm whites are full RGB; the accents are
	// hues (0..1) the shader rebuilds at full saturation.
	private static readonly Color WinColdWhite = new Color(0.72f, 0.80f, 1.00f);
	private static readonly Color WinCyan      = new Color(0.45f, 0.85f, 1.00f);
	private static readonly Color WinNeutral   = new Color(0.92f, 0.93f, 0.97f);
	private static readonly Color WinWarmWhite = new Color(1.00f, 0.86f, 0.62f);
	private static readonly Color WinAmber     = new Color(1.00f, 0.64f, 0.32f);
	private static readonly float[] AccentHues =
	{
		0.00f,   // red
		0.06f,   // amber-orange
		0.33f,   // acid green
		0.50f,   // cyan
		0.58f,   // electric blue
		0.74f,   // violet
		0.85f,   // magenta
		0.92f,   // hot pink
	};

	// One building's window "character", fed to the shader via COLOR (white base + lit fraction) and
	// INSTANCE_CUSTOM (variation, grid class, accent hue, accent amount). `Variation` is the smart bit:
	// low = a uniform block (windows match), high = residential (brightness/warmth differ, many dim).
	// `AccentAmount` is the fraction of windows recoloured to `AccentHue` — usually a small scatter of
	// colour among the white, occasionally near 1.0 for a deliberately fully-toned tower. `GridClass`
	// picks the pane size/shape preset.
	private struct WindowProfile
	{
		public Color Color;         // white base
		public float LitFraction;
		public float Variation;
		public float GridClass;
		public float AccentHue;     // 0..1
		public float AccentAmount;  // 0..1 fraction of windows recoloured
	}

	// Everything Generate needs to draw a chunk's buildings + roof dressing, kept separate from the
	// rendering so it stays pure/testable. Colors[i] is rgb=white window base, a=lit fraction; Custom[i]
	// is (variation, grid class, accent hue, accent amount); Shapes[i] is a Silhouette.
	public struct BuildingLayout
	{
		public List<Transform3D> Transforms;
		public List<Color> Colors;
		public List<Color> Custom;
		public List<int> Shapes;
		public List<Transform3D> Antennas;
		public List<Transform3D> Beacons;
		public List<Transform3D> Spires;
		public List<Transform3D> Domes;
	}

	// Picks a window archetype, then rolls its traits. Lit fractions are deliberately LOW (most windows
	// stay dark) and the colour mostly stays white — only the colourful/toned archetypes push real accent.
	private WindowProfile PickWindowProfile(RandomNumberGenerator rng)
	{
		var p = new WindowProfile();
		p.AccentHue = PickAccentHue(rng);   // every building carries a hue; AccentAmount decides if it shows
		float a = rng.Randf();
		if (a < 0.32f)
		{
			// OFFICE — cold/cyan white, near-uniform, sparse lit, dense small panes; essentially no colour.
			p.Color = WinColdWhite.Lerp(WinCyan, rng.Randf() * 0.5f);
			p.LitFraction = rng.RandfRange(0.10f, 0.24f);
			p.Variation = rng.RandfRange(0.05f, 0.22f);
			p.GridClass = rng.RandfRange(0.00f, 0.34f);
			p.AccentAmount = rng.RandfRange(0.00f, 0.04f);
		}
		else if (a < 0.66f)
		{
			// RESIDENTIAL — warm/amber, highly varied (many dim windows), medium grid, the odd colour pop.
			p.Color = WinWarmWhite.Lerp(WinAmber, rng.Randf());
			p.LitFraction = rng.RandfRange(0.07f, 0.17f);
			p.Variation = rng.RandfRange(0.60f, 1.00f);
			p.GridClass = rng.RandfRange(0.20f, 0.80f);
			p.AccentAmount = rng.RandfRange(0.00f, 0.10f);
		}
		else if (a < 0.84f)
		{
			// MIXED / commercial — neutral white, moderate, a modest scatter of coloured tenant windows.
			p.Color = WinNeutral.Lerp(WinWarmWhite, rng.Randf() * 0.5f);
			p.LitFraction = rng.RandfRange(0.10f, 0.22f);
			p.Variation = rng.RandfRange(0.35f, 0.70f);
			p.GridClass = rng.RandfRange(0.40f, 1.00f);
			p.AccentAmount = rng.RandfRange(0.06f, 0.22f);
		}
		else if (a < 0.93f)
		{
			// COLOURFUL — a white tower with a pronounced MINORITY of saturated neon windows among the white.
			p.Color = (rng.Randf() < 0.5f ? WinColdWhite : WinNeutral).Lerp(WinWarmWhite, rng.Randf() * 0.4f);
			p.LitFraction = rng.RandfRange(0.10f, 0.22f);
			p.Variation = rng.RandfRange(0.30f, 0.70f);
			p.GridClass = rng.RandfRange(0.20f, 1.00f);
			p.AccentAmount = rng.RandfRange(0.16f, 0.34f);
		}
		else
		{
			// TONED — the deliberate stylised case: (almost) ALL windows one saturated neon colour, kept
			// coherent (low variation). Rare, so it punctuates the skyline instead of dominating it.
			p.Color = WinNeutral;
			p.LitFraction = rng.RandfRange(0.12f, 0.28f);
			p.Variation = rng.RandfRange(0.08f, 0.30f);
			p.GridClass = rng.RandfRange(0.00f, 1.00f);
			p.AccentAmount = rng.RandfRange(0.85f, 1.00f);
		}
		return p;
	}

	// A saturated neon hue from the palette with a touch of jitter so two same-hue towers differ slightly.
	private static float PickAccentHue(RandomNumberGenerator rng)
	{
		float h = AccentHues[(int)(rng.Randf() * AccentHues.Length) % AccentHues.Length];
		return Mathf.Clamp(h + rng.RandfRange(-0.02f, 0.02f), 0.0f, 1.0f);
	}

	// Adds this building's rooftop dressing to the layout: a thin tapered mast (more likely the taller the
	// tower), a red aviation beacon on the highest roofs, and — on some roofs — a non-flat cap: a setback
	// penthouse (a smaller lit box, rides the building MultiMesh), a spire/cone, or a dome. Sizes scale
	// with WorldScale so they stay proportional to the enlarged towers.
	private void AddRoofFeatures(RandomNumberGenerator rng, BuildingLayout layout, BuildingClass cls, float x, float z, float height, float fx, float fz, float yaw, float scale)
	{
		float antennaChance = cls switch
		{
			BuildingClass.Low => 0.08f,
			BuildingClass.Mid => 0.30f,
			BuildingClass.High => 0.58f,
			_ => 0.85f,
		};
		float roofTopY = height;   // where the beacon sits when there's no mast
		if (rng.Randf() < antennaChance)
		{
			float antH = Mathf.Clamp(height * rng.RandfRange(0.10f, 0.22f), 12.0f * scale, 170.0f * scale);
			float antRadius = rng.RandfRange(0.7f, 1.2f) * scale;
			Basis ab = Basis.Identity.Scaled(new Vector3(antRadius * 2.0f, antH, antRadius * 2.0f));
			layout.Antennas.Add(new Transform3D(ab, new Vector3(x, height + antH * 0.5f, z)));
			roofTopY = height + antH;
		}

		float rBeacon = rng.Randf();
		bool beacon = cls == BuildingClass.Mega || (cls == BuildingClass.High && rBeacon < 0.7f);
		if (beacon)
		{
			float bR = rng.RandfRange(1.5f, 2.4f) * scale;
			Basis bb = Basis.Identity.Scaled(new Vector3(bR * 2.0f, bR * 2.0f, bR * 2.0f));
			layout.Beacons.Add(new Transform3D(bb, new Vector3(x, roofTopY + bR, z)));
		}

		// Non-flat roof cap — most roofs stay flat (~58%); the rest get a setback, spire, or dome.
		float rCap = rng.Randf();
		if (rCap < 0.22f)
		{
			// SETBACK penthouse — a smaller lit box stepped in from the roof edge (rides the building MMI).
			float frac = rng.RandfRange(0.40f, 0.70f);
			float capH = Mathf.Clamp(height * rng.RandfRange(0.04f, 0.12f), 6.0f * scale, 90.0f * scale);
			Basis cb = new Basis(Vector3.Up, yaw).Scaled(new Vector3(fx * frac, capH, fz * frac));
			AddBoxInstance(layout, new Transform3D(cb, new Vector3(x, height + capH * 0.5f, z)), PenthouseProfile(rng));
		}
		else if (rCap < 0.33f)
		{
			// SPIRE / cone.
			float spireH = Mathf.Clamp(height * rng.RandfRange(0.12f, 0.34f), 10.0f * scale, 260.0f * scale);
			float baseDiam = Mathf.Min(fx, fz) * rng.RandfRange(0.30f, 0.60f);
			Basis sb = Basis.Identity.Scaled(new Vector3(baseDiam, spireH, baseDiam));
			layout.Spires.Add(new Transform3D(sb, new Vector3(x, height + spireH * 0.5f, z)));
		}
		else if (rCap < 0.42f)
		{
			// DOME — sphere centred at the roofline so only the top half shows.
			float domeDiam = Mathf.Min(fx, fz) * rng.RandfRange(0.50f, 0.92f);
			float domeH = domeDiam * rng.RandfRange(0.45f, 0.80f);
			Basis db = Basis.Identity.Scaled(new Vector3(domeDiam, domeH, domeDiam));
			layout.Domes.Add(new Transform3D(db, new Vector3(x, height, z)));
		}
	}

	// Appends an extra Box-silhouette instance (skybridge, rooftop penthouse) so it rides the building
	// MultiMesh and is lit by the same window shader — no separate draw call or material.
	private static void AddBoxInstance(BuildingLayout layout, Transform3D xform, WindowProfile wp)
	{
		layout.Transforms.Add(xform);
		layout.Shapes.Add((int)Silhouette.Box);
		layout.Colors.Add(new Color(wp.Color.R, wp.Color.G, wp.Color.B, wp.LitFraction));
		layout.Custom.Add(new Color(wp.Variation, wp.GridClass, wp.AccentHue, wp.AccentAmount));
	}

	// A dim, mostly-dark mechanical-penthouse window profile (few lit windows, almost no colour).
	private static WindowProfile PenthouseProfile(RandomNumberGenerator rng)
	{
		var p = new WindowProfile();
		p.Color = WinNeutral;
		p.LitFraction = rng.RandfRange(0.06f, 0.18f);
		p.Variation = rng.RandfRange(0.10f, 0.40f);
		p.GridClass = rng.RandfRange(0.00f, 0.50f);
		p.AccentHue = PickAccentHue(rng);
		p.AccentAmount = rng.RandfRange(0.00f, 0.06f);
		return p;
	}

	// A dim skybridge window profile — a softly-lit connecting tube, occasional colour pop.
	private static WindowProfile BridgeProfile(RandomNumberGenerator rng)
	{
		var p = new WindowProfile();
		p.Color = WinNeutral;
		p.LitFraction = rng.RandfRange(0.18f, 0.36f);   // a touch brighter than the towers — a lit walkway reads as a connector
		p.Variation = rng.RandfRange(0.10f, 0.35f);     // fairly uniform (an enclosed corridor, not flats)
		p.GridClass = rng.RandfRange(0.00f, 0.40f);
		p.AccentHue = PickAccentHue(rng);
		p.AccentAmount = rng.RandfRange(0.00f, 0.10f);
		return p;
	}

	// Second pass: link some adjacent same-side towers (column c to c+1, same row) with a skybridge — a
	// lit horizontal box that rides the building MultiMesh (so it gets windows for free) and sits OUTSIDE
	// the flyable corridor. Iterates the deterministic `placed` order and continues the building stream.
	private void BridgeBuildings(RandomNumberGenerator rng, BuildingLayout layout, Dictionary<(int, int, int), PlacedBuilding> lookup, List<PlacedBuilding> placed, float scale)
	{
		foreach (PlacedBuilding a in placed)
		{
			if (a.Col >= ColumnsPerSide - 1)
				continue;
			if (!lookup.TryGetValue((SideIdx(a.Side), a.Col + 1, a.Row), out PlacedBuilding b))
				continue;
			if (rng.Randf() > SkybridgeChance)
				continue;
			float side = a.Side;
			float aFace = a.X + side * a.HalfX;     // a's face toward b (outward from the corridor)
			float bFace = b.X - side * b.HalfX;     // b's face toward a (inward)
			float gap = Mathf.Abs(bFace - aFace);
			if (gap < 4.0f * scale || gap > 140.0f * scale)
				continue;                           // overlapping, or too far apart to bridge cleanly
			float embed = 5.0f * scale;             // sink the ends a little into both towers
			float lenX = gap + 2.0f * embed;
			float centerX = 0.5f * (aFace + bFace);
			float minTop = Mathf.Min(a.Top, b.Top);
			float by = rng.RandfRange(0.3f, 0.72f) * minTop;
			float midZ = 0.5f * (a.Z + b.Z);
			float h = rng.RandfRange(5.0f, 10.0f) * scale;
			float w = rng.RandfRange(8.0f, 16.0f) * scale;
			Basis basis = Basis.Identity.Scaled(new Vector3(lenX, h, w));
			AddBoxInstance(layout, new Transform3D(basis, new Vector3(centerX, by, midZ)), BridgeProfile(rng));
		}
	}

	private static int SideIdx(float side) => side < 0.0f ? 0 : 1;

	// A placed tower's bridge-relevant facts (its side/column/row slot, centre, top, and outward X extent).
	private struct PlacedBuilding
	{
		public float Side;
		public int Col;
		public int Row;
		public float X;
		public float Z;
		public float Top;
		public float HalfX;
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
				mm.UseCustomData = true;             // ...and INSTANCE_CUSTOM: the per-building lighting profile
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
		EnsureRoofMultimeshes();
	}

	// The two per-chunk rooftop MultiMeshes (masts + beacons). Each is a single draw call per chunk,
	// sharing one mesh + material across the whole game (allocated once, statically).
	private void EnsureRoofMultimeshes()
	{
		if (_antennaMmi == null)
		{
			_antennaMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = GetAntennaMesh() };
			_antennaMmi = new MultiMeshInstance3D { Name = "Antennas", Multimesh = _antennaMm, MaterialOverride = GetAntennaMat() };
			_antennaMmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			AddChild(_antennaMmi);
		}
		if (_beaconMmi == null)
		{
			_beaconMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = GetBeaconMesh() };
			_beaconMmi = new MultiMeshInstance3D { Name = "Beacons", Multimesh = _beaconMm, MaterialOverride = GetBeaconMat() };
			_beaconMmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			AddChild(_beaconMmi);
		}
		if (_spireMmi == null)
		{
			_spireMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = GetSpireMesh() };
			_spireMmi = new MultiMeshInstance3D { Name = "Spires", Multimesh = _spireMm, MaterialOverride = GetStructureMat() };
			AddChild(_spireMmi);
		}
		if (_domeMmi == null)
		{
			_domeMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = GetDomeMesh() };
			_domeMmi = new MultiMeshInstance3D { Name = "Domes", Multimesh = _domeMm, MaterialOverride = GetStructureMat() };
			AddChild(_domeMmi);
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
		_buildingMat.SetShaderParameter("window_scale", pWorldScale);   // per-building grid sizes are scaled by this
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

	// A dark, thin tapered mast (unit cylinder, scaled per roof). Hexagonal + low-poly so it reads as a
	// lattice antenna at distance for almost nothing.
	private static Mesh GetAntennaMesh()
	{
		if (_antennaMesh == null)
		{
			_antennaMesh = new CylinderMesh
			{
				Height = 1.0f,
				TopRadius = 0.18f,
				BottomRadius = 0.5f,
				RadialSegments = 6,
				Rings = 0,
			};
		}
		return _antennaMesh;
	}

	private static StandardMaterial3D GetAntennaMat()
	{
		if (_antennaMat == null)
		{
			_antennaMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.02f, 0.022f, 0.03f),
				Metallic = 0.7f,
				Roughness = 0.45f,
			};
		}
		return _antennaMat;
	}

	// A small low-poly sphere (unit radius 0.5, scaled per roof), drawn by the blinking beacon shader.
	private static Mesh GetBeaconMesh()
	{
		if (_beaconMesh == null)
		{
			_beaconMesh = new SphereMesh
			{
				Radius = 0.5f,
				Height = 1.0f,
				RadialSegments = 8,
				Rings = 4,
			};
		}
		return _beaconMesh;
	}

	private static ShaderMaterial GetBeaconMat()
	{
		if (_beaconMat == null)
		{
			_beaconMat = new ShaderMaterial();
			_beaconMat.Shader = BeaconShader;
		}
		return _beaconMat;
	}

	// A pointed spire / cone roof cap (unit cone — top radius 0). Few radial segments for a faceted look.
	private static Mesh GetSpireMesh()
	{
		if (_spireMesh == null)
		{
			_spireMesh = new CylinderMesh
			{
				Height = 1.0f,
				TopRadius = 0.0f,
				BottomRadius = 0.5f,
				RadialSegments = 12,
				Rings = 0,
			};
		}
		return _spireMesh;
	}

	// A dome roof cap (unit sphere; placed with its equator at the roofline so only the top half shows).
	private static Mesh GetDomeMesh()
	{
		if (_domeMesh == null)
		{
			_domeMesh = new SphereMesh
			{
				Radius = 0.5f,
				Height = 1.0f,
				RadialSegments = 16,
				Rings = 6,
			};
		}
		return _domeMesh;
	}

	// Dark structural concrete/metal for rooftop caps — catches a faint sky reflection so domes/spires
	// read as silhouettes against the neon haze. Shared across the whole game.
	private static StandardMaterial3D GetStructureMat()
	{
		if (_structureMat == null)
		{
			// Glossy dark concrete/metal that catches the neon city, plus a faint cool self-glow so the
			// spires/domes read as deliberate forms against the night instead of vanishing into black.
			_structureMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.05f, 0.055f, 0.07f),
				Metallic = 0.5f,
				Roughness = 0.38f,
				EmissionEnabled = true,
				Emission = new Color(0.10f, 0.13f, 0.22f),
				EmissionEnergyMultiplier = 0.5f,
			};
		}
		return _structureMat;
	}
}
