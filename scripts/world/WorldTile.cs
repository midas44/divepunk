using Godot;
using System.Collections.Generic;

// One cell of the fixed world-tile grid: the MultiMesh batches for the buildings whose centres fall in it,
// plus lazily-instantiated colliders (only while the player is near). Positioned at the tile CENTRE so each
// tile culls independently via VisibilityRange and the building shader's world-origin seed stays correct
// (see TASK04 §4). Built once (static world); only colliders are added/removed.
public partial class WorldTile : Node3D
{
    private readonly List<PlacedObject> _objects = new();
    private Node3D _colliderRoot;
    private bool _visualsBuilt;

    public Vector3 Center;            // this tile node's world origin (set by the loader before AddChild)

    public int Count => _objects.Count;
    public void Add(PlacedObject o) => _objects.Add(o);

    public void BuildVisuals(float viewEnd, float fadeMargin)
    {
        if (_visualsBuilt || _objects.Count == 0) return;
        foreach (MultiMeshInstance3D mmi in TileBuilder.BuildMultiMeshes(_objects, Center, viewEnd, fadeMargin))
            AddChild(mmi);
        _visualsBuilt = true;
    }

    public void EnsureColliders(PhysicsMaterial mat)
    {
        if (_colliderRoot != null || _objects.Count == 0) return;
        _colliderRoot = new Node3D { Name = "Colliders" };
        AddChild(_colliderRoot);
        foreach (StaticBody3D body in TileBuilder.BuildColliders(_objects, Center, mat))
            _colliderRoot.AddChild(body);
    }

    public void ClearColliders()
    {
        _colliderRoot?.QueueFree();
        _colliderRoot = null;
    }
}
