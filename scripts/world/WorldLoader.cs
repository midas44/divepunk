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
    [Export] public string WorldResPath = "res://world/world_8km.res";

    [ExportGroup("Tiling / LOD")]
    [Export] public float TileSize = 250.0f;          // ~32x32 over the 8 km world
    [Export] public float ViewDistance = 5000.0f;     // per-tile VisibilityRangeEnd (fog hides the boundary)
    [Export] public float ViewFadeMargin = 600.0f;

    [ExportGroup("Colliders")]
    [Export] public int ColliderTileRadius = 2;       // tiles around the player kept collidable (±2 = ~500 m lookahead)
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

        BucketObjects();
        foreach (WorldTile tile in _tiles.Values)
            tile.BuildVisuals(ViewDistance, ViewFadeMargin);

        GD.Print($":: WorldLoader: {_data.Objects.Count} objects across {_tiles.Count} populated tiles ({TileSize:F0} m).");
    }

    private void BucketObjects()
    {
        Vector3 sum = Vector3.Zero;
        foreach (PlacedObject o in _data.Objects)
        {
            Vector3 p = o.Xform.Origin;
            sum += p;
            (int, int) key = TileOf(p.X, p.Z);
            if (!_tiles.TryGetValue(key, out WorldTile tile))
            {
                tile = new WorldTile { Name = $"Tile_{key.Item1}_{key.Item2}", Center = TileCenter(key) };
                tile.Position = tile.Center;       // tile node sits at the tile centre (see §4)
                _tiles[key] = tile;
                AddChild(tile);
            }
            tile.Add(o);
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
