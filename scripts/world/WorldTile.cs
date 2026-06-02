using Godot;
using System.Collections.Generic;

// One cell of the fixed world-tile grid: its terrain mesh + the MultiMesh batches for any buildings in it,
// plus lazily-instantiated colliders (terrain trimesh + building boxes) while the car is near. Positioned at
// the tile CENTRE so each tile culls independently via VisibilityRange and stays world-correct (TASK04 §4 /
// TASK05 §4). Built once (static world); only colliders are added/removed.
public partial class WorldTile : Node3D
{
    private readonly List<PlacedObject> _objects = new();
    private Node3D _colliderRoot;
    private Mesh _terrainMesh;       // kept for the lazy trimesh collider
    private bool _visualsBuilt;

    public Vector3 Center;           // this tile's world origin (set by the loader before AddChild; Y = 0)

    public int Count => _objects.Count;
    public void Add(PlacedObject o) => _objects.Add(o);

    // Terrain ALWAYS; object batches only if this tile has buildings. NO early-return on empty objects.
    // Terrain gets its own (large) view distance so the bounded world's terrain never dither-culls; buildings
    // keep the shorter one (fog hides their boundary).
    public void BuildVisuals(WorldData data, float tileSize, int terrainQuads, float terrainViewEnd, float buildingViewEnd, float fadeMargin)
    {
        if (_visualsBuilt) return;
        MeshInstance3D terrain = TerrainBuilder.BuildTerrainMesh(data, Center, tileSize, terrainQuads, terrainViewEnd, fadeMargin);
        _terrainMesh = terrain.Mesh;
        AddChild(terrain);
        if (_objects.Count > 0)
            foreach (MultiMeshInstance3D mmi in TileBuilder.BuildMultiMeshes(_objects, Center, buildingViewEnd, fadeMargin))
                AddChild(mmi);
        _visualsBuilt = true;
    }

    // Lazy colliders near the car: terrain trimesh ALWAYS + a box per building (if any). NO early-return on
    // empty objects — the terrain still needs a collider so the car can land.
    public void EnsureColliders(PhysicsMaterial mat)
    {
        if (_colliderRoot != null) return;
        _colliderRoot = new Node3D { Name = "Colliders" };
        AddChild(_colliderRoot);
        if (_terrainMesh != null)
            _colliderRoot.AddChild(TerrainBuilder.BuildTerrainCollider(_terrainMesh, mat));
        if (_objects.Count > 0)
            foreach (StaticBody3D body in TileBuilder.BuildColliders(_objects, Center, mat))
                _colliderRoot.AddChild(body);
    }

    public void ClearColliders()
    {
        _colliderRoot?.QueueFree();
        _colliderRoot = null;
    }
}
