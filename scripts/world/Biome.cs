// Coarse biome of a heightmap cell. Stored as the byte in WorldData.Biomes. Append-only (never reorder).
// Plain enum (no [GlobalClass]); the byte value is what persists.
public enum Biome : byte
{
    Ocean = 0, Beach = 1, City = 2, Desert = 3, Hills = 4, Mountains = 5,
}
