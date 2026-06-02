using Godot;

// The entire baked world as DATA (spec §7.1): a terrain heightmap, a coarse biome map, and the object
// descriptors. Saved to res://world/world_main.res by the bake; loaded once at runtime by the Task-4 loader.
// FormatVersion guards stale bakes when the schema changes.
//
// A Resource serialises only its [Export] members. float[]/byte[] map to PackedFloat32Array/PackedByteArray
// (compact + deterministic); Array<PlacedObject> preserves insertion (= bake) order.
[GlobalClass]
public partial class WorldData : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public int Seed { get; set; } = 0;
    [Export] public float WorldExtent { get; set; } = 8000.0f;   // metres, full width on X and Z (centred on origin)

    // Heightmap: an N×N grid (row-major, N = HeightmapResolution) of NORMALISED heights in [0,1]. Real
    // elevation = Mathf.Lerp(MinY, MaxY, Heights[iz*N + ix]). Sea level is world Y = 0 (so MinY < 0 < MaxY).
    [Export] public int HeightmapResolution { get; set; } = 256;
    [Export] public float MinY { get; set; } = -120.0f;
    [Export] public float MaxY { get; set; } = 900.0f;
    [Export] public float[] Heights { get; set; } = System.Array.Empty<float>();

    // Coarse biome map, same N×N grid, one Biome byte per cell (see the Biome enum). Drives terrain colour
    // (Task 5) and where objects are placed (City cells).
    [Export] public byte[] Biomes { get; set; } = System.Array.Empty<byte>();

    // Object descriptors (buildings now; props/dressing later). Insertion order is the bake order, kept
    // deterministic by the generator's fixed iteration. If load cost ever bites at full density, the spec's
    // fallback is parallel typed arrays behind the same API — NOT a Task-3 concern.
    [Export] public Godot.Collections.Array<PlacedObject> Objects { get; set; } = new();

    // ── Runtime sampling (Task 5) — additive, NOT serialised (no [Export]); world_main.res unchanged. ──
    // Real elevation (metres) at world (x,z): bilinear over the normalised heightmap, then Lerp(MinY,MaxY).
    // MUST stay byte-identical to WorldGenerator.SampleHeight so terrain rises to EXACTLY meet the baked
    // building bases (TASK05 §4). The city plateau is flat, so towers there seat perfectly regardless.
    public float HeightAt(float x, float z)
    {
        int n = HeightmapResolution;
        float half = WorldExtent * 0.5f, cell = WorldExtent / n;
        float gx = Mathf.Clamp((x + half) / cell - 0.5f, 0.0f, n - 1.001f);
        float gz = Mathf.Clamp((z + half) / cell - 0.5f, 0.0f, n - 1.001f);
        int ix = (int)gx, iz = (int)gz;
        int ix1 = Mathf.Min(ix + 1, n - 1), iz1 = Mathf.Min(iz + 1, n - 1);
        float tx = gx - ix, tz = gz - iz;
        float h0 = Mathf.Lerp(Heights[iz * n + ix], Heights[iz * n + ix1], tx);
        float h1 = Mathf.Lerp(Heights[iz1 * n + ix], Heights[iz1 * n + ix1], tx);
        return Mathf.Lerp(MinY, MaxY, Mathf.Lerp(h0, h1, tz));
    }

    // Coarse biome of the heightmap cell containing world (x,z). Mirrors WorldGenerator.BiomeAt verbatim.
    public Biome BiomeAt(float x, float z)
    {
        int n = HeightmapResolution;
        float half = WorldExtent * 0.5f, cell = WorldExtent / n;
        int ix = Mathf.Clamp((int)((x + half) / cell), 0, n - 1);
        int iz = Mathf.Clamp((int)((z + half) / cell), 0, n - 1);
        return (Biome)Biomes[iz * n + ix];
    }
}
