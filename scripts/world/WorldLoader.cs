using Godot;
using System.Collections.Generic;

// Loads the baked WorldData and renders it as a fixed grid of WorldTiles (spec §7.1/§7.3). Buckets the baked
// PlacedObjects into ~TileSize tiles ONCE, eagerly builds each tile's MultiMesh batches (VisibilityRange + fog
// handle the cull), and lazily gives only the tiles near the player solid colliders (bounded physics cost). No
// runtime RNG -> the city is identical every launch. Terrain mesh, ocean, and biome colour are Task 5 — this
// renders the OBJECTS only.
[GlobalClass]
public partial class WorldLoader : Node3D
{
    [ExportGroup("Source")]
    [Export] public string WorldResPath = "res://world/world_main.res";

    [ExportGroup("Tiling / LOD")]
    [Export] public float TileSize = 1250.0f;         // ~32x32 over the 40 km world (Task 9 rescale; keeps the grid/MultiMesh structure ~1024 tiles)
    [Export] public float ViewDistance = 15000.0f;    // per-tile VisibilityRangeEnd (fog hides the boundary)
    [Export] public float ViewFadeMargin = 3000.0f;    // dither-fade band before the cull end (applies to terrain + buildings)
    [Export] public float TerrainViewDistance = 55000.0f; // terrain VisibilityRangeEnd: large enough to cover the whole bounded world (diagonal ≈56 km at 40 km extent) so the distant mountain ring never dither-culls. Terrain is cheap (~128 tris/tile); fog handles the far haze.
    [Export] public int TerrainTileQuads = 8;          // terrain mesh resolution per tile (~1 heightmap cell per quad at 1250 m tile / 156 m cell)

    [ExportGroup("Colliders")]
    [Export] public int ColliderTileRadius = 1;       // tiles around the player kept collidable (±1 = ~1250 m lookahead at 1250 m tiles)
    [Export] public float ColliderBounce = 0.3f;      // match the car's PhysicsMaterial
    [Export] public float ColliderFriction = 0.4f;

    public Node3D Player;                              // set by Game; drives the lazy colliders
    public Vector3 CityCenter { get; private set; }   // centroid of placed objects (a convenient spawn point)

    private WorldData _data;
    private readonly Dictionary<(int, int), WorldTile> _tiles = new();
    private readonly HashSet<(int, int)> _activeColliderTiles = new();
    private PhysicsMaterial _colliderMat;
    private float _half;
    private (int, int) _lastPlayerTile = (int.MinValue, int.MinValue);

    public override void _Ready()
    {
        _data = ResourceLoader.Load<WorldData>(WorldResPath);
        if (_data == null) { GD.PrintErr($":: WorldLoader: could not load {WorldResPath} — world not rendered."); return; }
        if (_data.FormatVersion != 1) GD.PrintErr($":: WorldLoader: FormatVersion {_data.FormatVersion} != 1 (stale bake?).");

        _half = _data.WorldExtent * 0.5f;
        _colliderMat = new PhysicsMaterial { Bounce = ColliderBounce, Friction = ColliderFriction };

        BuildTileGrid();    // full coverage: EVERY grid cell gets a tile (terrain everywhere, not just the city)
        BucketObjects();    // drop each PlacedObject into its (already-created) tile
        foreach (WorldTile tile in _tiles.Values)
            tile.BuildVisuals(_data, TileSize, TerrainTileQuads, TerrainViewDistance, ViewDistance, ViewFadeMargin);

        int populated = 0;
        foreach (WorldTile t in _tiles.Values) if (t.Count > 0) populated++;
        GD.Print($":: WorldLoader: {_data.Objects.Count} objects, {_tiles.Count} terrain tiles ({populated} populated), {TileSize:F0} m.");
    }

    private void BuildTileGrid()
    {
        int side = Mathf.RoundToInt(_data.WorldExtent / TileSize);   // 40000 / 1250 = 32  ->  1024 tiles
        for (int iz = 0; iz < side; iz++)
        for (int ix = 0; ix < side; ix++)
        {
            var key = (ix, iz);
            var tile = new WorldTile { Name = $"Tile_{ix}_{iz}", Center = TileCenter(key) };
            tile.Position = tile.Center;     // tile node sits at the tile centre (TASK04 §4)
            _tiles[key] = tile;
            AddChild(tile);
        }
    }

    private void BucketObjects()
    {
        Vector3 sum = Vector3.Zero;
        foreach (PlacedObject o in _data.Objects)
        {
            Vector3 p = o.Xform.Origin;
            sum += p;
            if (_tiles.TryGetValue(TileOf(p.X, p.Z), out WorldTile tile))
                tile.Add(o);                 // full grid exists; an out-of-bounds object (shouldn't happen) is skipped
        }
        CityCenter = _data.Objects.Count > 0 ? sum / _data.Objects.Count : Vector3.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || _tiles.Count == 0) return;
        (int, int) pt = TileOf(Player.GlobalPosition.X, Player.GlobalPosition.Z);
        if (pt == _lastPlayerTile) return;         // only churn colliders when the player crosses a tile boundary
        _lastPlayerTile = pt;
        UpdateColliders(pt);
    }

    private void UpdateColliders((int, int) center)
    {
        // The set of populated tiles within ColliderTileRadius of the player's tile should be collidable.
        var desired = new HashSet<(int, int)>();
        for (int dz = -ColliderTileRadius; dz <= ColliderTileRadius; dz++)
        for (int dx = -ColliderTileRadius; dx <= ColliderTileRadius; dx++)
        {
            (int, int) key = (center.Item1 + dx, center.Item2 + dz);
            if (_tiles.ContainsKey(key)) desired.Add(key);
        }

        foreach ((int, int) key in desired)            // add colliders for tiles newly in range
            if (_activeColliderTiles.Add(key))
                _tiles[key].EnsureColliders(_colliderMat);

        var stale = new List<(int, int)>();            // drop colliders for tiles that left range
        foreach ((int, int) key in _activeColliderTiles)
            if (!desired.Contains(key)) stale.Add(key);
        foreach ((int, int) key in stale) { _tiles[key].ClearColliders(); _activeColliderTiles.Remove(key); }
    }

    private (int, int) TileOf(float x, float z)
        => (Mathf.FloorToInt((x + _half) / TileSize), Mathf.FloorToInt((z + _half) / TileSize));

    private Vector3 TileCenter((int, int) key)
        => new Vector3(-_half + (key.Item1 + 0.5f) * TileSize, 0.0f, -_half + (key.Item2 + 0.5f) * TileSize);
}
