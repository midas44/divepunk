using Godot;

// The entire baked world as DATA (spec §7.1): a terrain heightmap, a coarse biome map, and the object
// descriptors. Saved to res://world/world_8km.res by the bake; loaded once at runtime by the Task-4 loader.
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
}
