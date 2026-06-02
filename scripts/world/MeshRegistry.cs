using Godot;

// ObjectType -> Mesh, plus the one shared building ShaderMaterial. The glTF swap SEAM (Task 8): today these
// are procedural unit meshes harvested VERBATIM from CityChunk.GetBuildingMeshes() (lines 1042-1098); later a
// registry entry swaps in a .glb with no re-bake. Every mesh fits a 1x1x1 box, so a PlacedObject's Xform basis
// scale (fx, height, fz) sets the real size. Cached statically (shared, immutable).
public static class MeshRegistry
{
    private static readonly Shader BuildingShader = GD.Load<Shader>("res://shaders/building.gdshader");
    private static Mesh[] _meshes;                 // index = (int)ObjectType
    private static ShaderMaterial _buildingMat;

    public static Mesh GetMesh(ObjectType type)
    {
        EnsureBuilt();
        int i = (int)type;
        return (i >= 0 && i < _meshes.Length) ? _meshes[i] : _meshes[0];
    }

    private static void EnsureBuilt()
    {
        if (_meshes != null) return;
        _buildingMat = new ShaderMaterial { Shader = BuildingShader };
        // Baked transforms are REAL metres (the bake does NOT apply the old [game] scale), so panes are 1:1 ->
        // window_scale = 1.0. windows_on follows [fx] building_windows (runtime; the Config autoload is up).
        bool windows = Config.Instance?.GetBool("fx", "building_windows", true) ?? true;
        _buildingMat.SetShaderParameter("windows_on", windows ? 1.0f : 0.0f);
        _buildingMat.SetShaderParameter("window_scale", 1.0f);

        // Index order MUST match ObjectType (Box, Round, Prism, Taper, Shard, Sphere). Each mesh carries the
        // shared material on .Material, so the MultiMeshInstance3D needs no MaterialOverride.
        var box    = new BoxMesh      { Size = Vector3.One, Material = _buildingMat };
        var round  = new CylinderMesh { Height = 1.0f, TopRadius = 0.5f,  BottomRadius = 0.5f, RadialSegments = 20, Material = _buildingMat };
        var prism  = new CylinderMesh { Height = 1.0f, TopRadius = 0.5f,  BottomRadius = 0.5f, RadialSegments = 6,  Material = _buildingMat };
        var taper  = new CylinderMesh { Height = 1.0f, TopRadius = 0.28f, BottomRadius = 0.5f, RadialSegments = 20, Material = _buildingMat };
        var shard  = new CylinderMesh { Height = 1.0f, TopRadius = 0.05f, BottomRadius = 0.5f, RadialSegments = 4,  Material = _buildingMat };
        var sphere = new SphereMesh   { Radius = 0.5f, Height = 1.0f, RadialSegments = 18, Rings = 9, Material = _buildingMat };
        _meshes = new Mesh[] { box, round, prism, taper, shard, sphere };
    }
}
