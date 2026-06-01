using Godot;

// Pure-C# deterministic world generator (spec §7.1). Build(seed, extent) -> WorldData. No nodes, no
// rendering, no file I/O — so the SAME (seed, extent) yields an identical WorldData, hence byte-identical
// bytes when saved. The building variety (size classes, silhouettes, window profiles) and the seed mix are
// HARVESTED from scripts/CityChunk.cs and adapted from a streamed corridor to a fixed grid on the city
// plateau. Tunable consts are pinned here (a static class can't use [Export]); the LOOK is tuned in Task 5.
public static class WorldGenerator
{
    // ── Heightmap resolution ───────────────────────────────────────────────────────────────────────
    private const int N = 256;   // HeightmapResolution (bump WorldData.FormatVersion if you change it)

    // ── Terrain layout consts (metres; world centred on origin, half = extent/2). LAND is the side where
    //    landSD > 0 — the city plateau sits there, above water. The LOOK is tuned in Task 5; these just
    //    need to be deterministic and produce ocean / beach / a flat city plateau / desert→hills→mountains.
    private const float RingInner = 2400.0f, RingOuter = 4300.0f, MountH = 760.0f;     // radial mountain ring
    private const float CityCx = -1400.0f, CityCz = -200.0f, CityR = 1500.0f, PlateauH = 38.0f;  // city plateau
    private const float CoastX = 900.0f, CoastAmp = 520.0f, CoastFreq = 1.0f / 1500.0f, CoastNz = 300.0f; // coastline curve
    private const float BayCx = 600.0f, BayCz = 1700.0f, BayR = 1400.0f, BayEdge = 650.0f;        // carved bay
    private const float OceanDepth = 110.0f, BeachRise = 2.0f, OceanSlope = 16.0f;     // underwater shaping
    private const float BeachBand = 420.0f, LandBase = 10.0f, RollAmp = 60.0f;         // shore ease + rolling land

    // Island bumps (CX, CZ, R, H) — each pokes above the sea where its H beats the local ocean depth.
    private static readonly (float Cx, float Cz, float R, float H)[] Islands =
    {
        (2200.0f, -1400.0f, 520.0f, 150.0f),
        (2700.0f,   900.0f, 440.0f, 140.0f),
        (1500.0f,  2700.0f, 360.0f, 120.0f),
    };

    // ── City placement consts ──────────────────────────────────────────────────────────────────────
    private const float Block = 90.0f;     // placement-grid spacing (m) — INDEPENDENT of the heightmap N
    private const float FillChance = 0.72f; // per-cell chance of a building
    private const int RoadEvery = 6;        // every Kth grid line on each axis is a street (skipped)
    private const long PlaceSalt = 1L;      // RNG stream salt for placement (distinct from the terrain noise)

    // ── Building variety (harvested from CityChunk.cs lines 64–84; values verbatim) ──────────────────
    private const float LowWeight = 0.50f, MidWeight = 0.38f, HighWeight = 0.08f, MegaWeight = 0.04f;
    private static readonly Vector2 LowHeight = new(30.0f, 80.0f), MidHeight = new(80.0f, 200.0f),
        HighHeight = new(200.0f, 420.0f), MegaHeight = new(420.0f, 880.0f);
    private static readonly Vector2 LowFootprint = new(35.0f, 95.0f), MidFootprint = new(45.0f, 130.0f),
        HighFootprint = new(60.0f, 175.0f), MegaFootprint = new(95.0f, 260.0f);
    private const float FootprintAspectMin = 0.65f, FootprintAspectMax = 1.6f;
    private const float ShapeBoxWeight = 0.5f, ShapeRoundWeight = 0.2f, ShapePrismWeight = 0.15f,
        ShapeTaperWeight = 0.15f, ShapeShardWeight = 0.08f;

    private enum BuildingClass { Low = 0, Mid = 1, High = 2, Mega = 3 }
    private enum Silhouette { Box = 0, Round = 1, Prism = 2, Taper = 3, Shard = 4, Sphere = 5 }

    // One building's window "character" -> COLOR (white base + lit fraction) + INSTANCE_CUSTOM (variation,
    // grid class, accent hue, accent amount). (CityChunk.cs WindowProfile, lines 417–425.)
    private struct WindowProfile
    {
        public Color Color;
        public float LitFraction;
        public float Variation;
        public float GridClass;
        public float AccentHue;
        public float AccentAmount;
    }

    public static WorldData Build(int seed, float extent)
    {
        float half = extent * 0.5f;
        float cell = extent / N;

        // ── 1. Heightmap + biome map ─────────────────────────────────────────────────────────────────
        // Pin EVERY FastNoiseLite parameter explicitly — defaults can shift between engine versions.
        var fnl = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 5,
            FractalLacunarity = 2.0f,
            FractalGain = 0.5f,
            Frequency = 1.0f / 2200.0f,   // ~2 km features
        };
        var coastNz = new FastNoiseLite
        {
            Seed = seed + 101,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3,
            FractalLacunarity = 2.0f,
            FractalGain = 0.5f,
            Frequency = 1.0f / 3000.0f,
        };

        var raw = new float[N * N];     // real elevation (metres) before normalising
        var biomes = new byte[N * N];
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int iz = 0; iz < N; iz++)
        for (int ix = 0; ix < N; ix++)
        {
            float x = -half + (ix + 0.5f) * cell;
            float z = -half + (iz + 0.5f) * cell;
            float r = Mathf.Sqrt(x * x + z * z);

            float roll = fnl.GetNoise2D(x, z);                                          // [-1,1]
            float ring = Smooth01((r - RingInner) / (RingOuter - RingInner));           // 0 centre .. 1 edge
            float land = LandBase + roll * RollAmp + ring * ring * MountH;              // rolling + mountain ring

            float plateau = 1.0f - Smooth01((Dist(x, z, CityCx, CityCz) - CityR * 0.7f) / (CityR * 0.3f));
            land = Mathf.Lerp(land, PlateauH, plateau);                                 // flatten the city plateau

            float coast = CoastX + CoastAmp * Mathf.Sin(z * CoastFreq) + CoastNz * coastNz.GetNoise2D(0.0f, z);
            float landSD = coast - x;                                                   // >0 land (-X side), <0 ocean
            float bay = Smooth01((BayR - Dist(x, z, BayCx, BayCz)) / BayEdge);          // 1 inside bay
            bool isOcean = landSD < 0.0f || bay > 0.5f;

            float elev = isOcean
                ? -Mathf.Min(OceanDepth, BeachRise + Mathf.Max(-landSD, bay * BayR) / OceanSlope)
                : Mathf.Lerp(0.5f, land, Smooth01(landSD / BeachBand));                 // ease land down at the shore

            foreach (var isl in Islands)                                                // bumps that poke above the sea
                elev += isl.H * Mathf.Max(0.0f, 1.0f - Dist(x, z, isl.Cx, isl.Cz) / isl.R);

            raw[iz * N + ix] = elev;
            minY = Mathf.Min(minY, elev);
            maxY = Mathf.Max(maxY, elev);
            biomes[iz * N + ix] = (byte)ClassifyBiome(elev, plateau, ring);
        }

        var heights = new float[N * N];
        for (int i = 0; i < raw.Length; i++)
            heights[i] = (maxY > minY) ? (raw[i] - minY) / (maxY - minY) : 0.0f;

        // ── 2. Place the city on the plateau ───────────────────────────────────────────────────────────
        // A fixed nested loop + a per-cell RNG seeded by MixSeed = deterministic, order-free placement.
        float classTotal = LowWeight + MidWeight + HighWeight + MegaWeight;
        float shapeTotal = ShapeBoxWeight + ShapeRoundWeight + ShapePrismWeight + ShapeTaperWeight + ShapeShardWeight;

        var objects = new Godot.Collections.Array<PlacedObject>();
        int gx0 = CellIndex(CityCx - CityR, extent), gx1 = CellIndex(CityCx + CityR, extent);
        int gz0 = CellIndex(CityCz - CityR, extent), gz1 = CellIndex(CityCz + CityR, extent);

        for (int gz = gz0; gz <= gz1; gz++)
        for (int gx = gx0; gx <= gx1; gx++)
        {
            float x = -half + (gx + 0.5f) * Block;     // cell centre in metres
            float z = -half + (gz + 0.5f) * Block;
            if (BiomeAt(biomes, extent, x, z) != Biome.City) continue;
            if (IsRoadCell(gx, gz)) continue;          // simple street grid

            long cellId = (long)gz * 100000L + gx;     // stable, unique per cell
            var rng = new RandomNumberGenerator { Seed = (ulong)MixSeed(seed, cellId, PlaceSalt) };
            if (rng.Randf() > FillChance) continue;

            // Building character, ported from CityChunk's per-building draw order (class → footprint base +
            // 2 aspects → height → yaw → shape → window profile). The corridor x/z-jitter draws are dropped
            // (the grid sets position). Footprint is capped < Block so towers don't sprawl across cells —
            // the cap clamps the RESULT, the RandfRange draw still happens, so the RNG stream is unchanged.
            BuildingClass cls = PickClass(rng, classTotal);
            Vector2 hRange = ClassHeight(cls);
            Vector2 fRange = ClassFootprint(cls);
            float fp = rng.RandfRange(fRange.X, fRange.Y);
            float fx = Mathf.Min(fp * rng.RandfRange(FootprintAspectMin, FootprintAspectMax), Block * 0.92f);
            float fz = Mathf.Min(fp * rng.RandfRange(FootprintAspectMin, FootprintAspectMax), Block * 0.92f);
            float height = rng.RandfRange(hRange.X, hRange.Y);
            float yaw = rng.RandfRange(-0.12f, 0.12f);
            Silhouette shape = PickShape(rng, shapeTotal);
            WindowProfile wp = PickWindowProfile(rng);

            float baseY = SampleHeight(heights, extent, minY, maxY, x, z);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(fx, height, fz));
            objects.Add(new PlacedObject
            {
                Type   = SilhouetteToType(shape),
                Xform  = new Transform3D(basis, new Vector3(x, baseY + height * 0.5f, z)),
                Tint   = new Color(wp.Color.R, wp.Color.G, wp.Color.B, wp.LitFraction),
                Custom = new Color(wp.Variation, wp.GridClass, wp.AccentHue, wp.AccentAmount),
                SubSeed = (int)cellId,
            });
        }

        // ── 3. Assemble + return ─────────────────────────────────────────────────────────────────────
        return new WorldData
        {
            FormatVersion = 1,
            Seed = seed,
            WorldExtent = extent,
            HeightmapResolution = N,
            MinY = minY,
            MaxY = maxY,
            Heights = heights,
            Biomes = biomes,
            Objects = objects,
        };
    }

    // ── Determinism kit, harvested VERBATIM from CityChunk.cs ─────────────────────────────────────────

    // A deterministic, order-independent mix of (seed, cell index, salt) -> RNG seed. The salt decouples
    // independent streams off one world seed. 64-bit wrapping math (long + unchecked) — C# int is 32-bit and
    // would mix differently. (CityChunk.cs 1018–1028.)
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

    // GDScript round()/roundi() round half AWAY from zero; C# Math.Round is banker's. Any float→int that
    // feeds a loop/RNG-consuming count must use this. Carried as part of the determinism kit. (CityChunk.cs
    // 1032–1035.)
    private static int RoundHalfAway(float x)
        => (int)(x >= 0.0f ? Mathf.Floor(x + 0.5f) : Mathf.Ceil(x - 0.5f));

    // ── Building variety, harvested from CityChunk.cs (PickClass adapted; rest verbatim) ──────────────

    // Weighted pick of a size class. (CityChunk.cs 339–355, ADAPTED: the corridor-specific col==0 → Mega
    // demotion is dropped — the open city allows megatowers anywhere; the single Randf() draw is unchanged.)
    private static BuildingClass PickClass(RandomNumberGenerator rng, float total)
    {
        float r = rng.Randf() * Mathf.Max(total, 0.0001f);
        float a = Mathf.Max(LowWeight, 0.0f);
        float b = a + Mathf.Max(MidWeight, 0.0f);
        float c = b + Mathf.Max(HighWeight, 0.0f);
        if (r < a) return BuildingClass.Low;
        if (r < b) return BuildingClass.Mid;
        if (r < c) return BuildingClass.High;
        return BuildingClass.Mega;
    }

    private static Vector2 ClassHeight(BuildingClass cls) => cls switch
    {
        BuildingClass.Low => LowHeight,
        BuildingClass.Mid => MidHeight,
        BuildingClass.High => HighHeight,
        _ => MegaHeight,
    };

    private static Vector2 ClassFootprint(BuildingClass cls) => cls switch
    {
        BuildingClass.Low => LowFootprint,
        BuildingClass.Mid => MidFootprint,
        BuildingClass.High => HighFootprint,
        _ => MegaFootprint,
    };

    // Weighted pick of a silhouette. Never returns Sphere (those came from deferred sphere-stacks), so the
    // bake populates 5 of the 6 ObjectTypes — BuildingSphere stays 0. (CityChunk.cs 373–389, verbatim.)
    private static Silhouette PickShape(RandomNumberGenerator rng, float total)
    {
        float r = rng.Randf() * Mathf.Max(total, 0.0001f);
        float a = Mathf.Max(ShapeBoxWeight, 0.0f);
        float b = a + Mathf.Max(ShapeRoundWeight, 0.0f);
        float c = b + Mathf.Max(ShapePrismWeight, 0.0f);
        float d = c + Mathf.Max(ShapeTaperWeight, 0.0f);
        if (r < a) return Silhouette.Box;
        if (r < b) return Silhouette.Round;
        if (r < c) return Silhouette.Prism;
        if (r < d) return Silhouette.Taper;
        return Silhouette.Shard;
    }

    // Window light palette (CityChunk.cs 394–409, verbatim). Whites are full RGB; accents are hues (0..1).
    private static readonly Color WinColdWhite = new(0.72f, 0.80f, 1.00f);
    private static readonly Color WinCyan      = new(0.45f, 0.85f, 1.00f);
    private static readonly Color WinNeutral   = new(0.92f, 0.93f, 0.97f);
    private static readonly Color WinWarmWhite = new(1.00f, 0.86f, 0.62f);
    private static readonly Color WinAmber     = new(1.00f, 0.64f, 0.32f);
    private static readonly float[] AccentHues = { 0.00f, 0.06f, 0.33f, 0.50f, 0.58f, 0.74f, 0.85f, 0.92f };

    // Picks a window archetype, then rolls its traits. (CityChunk.cs 448–500, verbatim.)
    private static WindowProfile PickWindowProfile(RandomNumberGenerator rng)
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

    // A saturated neon hue from the palette with a touch of jitter. (CityChunk.cs 503–507, verbatim.)
    private static float PickAccentHue(RandomNumberGenerator rng)
    {
        float h = AccentHues[(int)(rng.Randf() * AccentHues.Length) % AccentHues.Length];
        return Mathf.Clamp(h + rng.RandfRange(-0.02f, 0.02f), 0.0f, 1.0f);
    }

    // ── Pure helpers ─────────────────────────────────────────────────────────────────────────────────

    private static float Smooth01(float t) => Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp(t, 0.0f, 1.0f));

    private static float Dist(float x, float z, float cx, float cz)
        => Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));

    private static Biome ClassifyBiome(float elev, float plateau, float ring)
    {
        if (elev < -2.0f) return Biome.Ocean;
        if (elev < 3.0f) return Biome.Beach;
        if (plateau > 0.5f) return Biome.City;
        if (ring < 0.45f) return Biome.Desert;
        if (ring < 0.75f) return Biome.Hills;
        return Biome.Mountains;
    }

    // World metre coord -> placement-grid index along one axis (extent centred on origin, Block spacing).
    private static int CellIndex(float coord, float extent)
        => Mathf.FloorToInt((coord + extent * 0.5f) / Block);

    private static bool IsRoadCell(int gx, int gz) => gx % RoadEvery == 0 || gz % RoadEvery == 0;

    // Biome of the heightmap cell containing world (x,z).
    private static Biome BiomeAt(byte[] biomes, float extent, float x, float z)
    {
        float half = extent * 0.5f, cell = extent / N;
        int ix = Mathf.Clamp((int)((x + half) / cell), 0, N - 1);
        int iz = Mathf.Clamp((int)((z + half) / cell), 0, N - 1);
        return (Biome)biomes[iz * N + ix];
    }

    // Bilinear sample of the normalised heightmap at world (x,z), returned as a real elevation in metres.
    private static float SampleHeight(float[] heights, float extent, float minY, float maxY, float x, float z)
    {
        float half = extent * 0.5f, cell = extent / N;
        float gx = Mathf.Clamp((x + half) / cell - 0.5f, 0.0f, N - 1.001f);
        float gz = Mathf.Clamp((z + half) / cell - 0.5f, 0.0f, N - 1.001f);
        int ix = (int)gx, iz = (int)gz;
        int ix1 = Mathf.Min(ix + 1, N - 1), iz1 = Mathf.Min(iz + 1, N - 1);
        float tx = gx - ix, tz = gz - iz;
        float h0 = Mathf.Lerp(heights[iz * N + ix], heights[iz * N + ix1], tx);
        float h1 = Mathf.Lerp(heights[iz1 * N + ix], heights[iz1 * N + ix1], tx);
        return Mathf.Lerp(minY, maxY, Mathf.Lerp(h0, h1, tz));
    }

    private static ObjectType SilhouetteToType(Silhouette s) => (ObjectType)(int)s;
}
