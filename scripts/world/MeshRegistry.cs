using Godot;
using System;

// ObjectType -> Mesh, plus the one shared building ShaderMaterial. The glTF swap SEAM: the procedural unit
// meshes were harvested VERBATIM from CityChunk.GetBuildingMeshes() (lines 1042-1098). Task 8 exercises the
// seam — Taper + Round now load from an imported .glb (see TrySwapGltf) with NO re-bake. Every mesh fits a
// 1x1x1 box, so a PlacedObject's Xform basis scale (fx, height, fz) sets the real size. Cached statically.
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

        TrySwapGltf();
    }

    // Task 8 — the glTF swap seam, exercised. Load the test .glb, normalize its mesh to the centered 1x1x1 unit
    // box the bake expects, reuse the shared building material, and replace the Taper + Round entries with it.
    // PURELY a registry change: world_main.res is untouched and TileBuilder is unchanged (the normalization keeps
    // the unit-mesh contract, so the baked per-instance (fx, height, fz) transforms size + seat it identically).
    // Fail-soft: any miss (asset not imported, no mesh) keeps the procedural meshes + warns — never crashes the
    // registry (it runs during world build, headless included).
    private static void TrySwapGltf()
    {
        const string glbPath = "res://assets/models/test_tower.glb";
        try
        {
            var scene = GD.Load<PackedScene>(glbPath);
            Node root = scene?.Instantiate();
            MeshInstance3D mi = FindFirstMeshInstance(root);
            if (mi?.Mesh != null)
            {
                Mesh unit = NormalizeToUnitBox(mi.Mesh, RelativeTransform(root, mi));
                unit.SurfaceSetMaterial(0, _buildingMat);       // wear the shared neon-window building shader
                _meshes[(int)ObjectType.BuildingTaper] = unit;
                _meshes[(int)ObjectType.BuildingRound] = unit;  // one model, two types (broader seam proof)
            }
            else
                GD.PushWarning($"MeshRegistry: no MeshInstance3D in {glbPath}; keeping procedural Taper/Round.");
            root?.Free();   // out-of-tree throwaway: Free (not QueueFree) since it was never in the tree
        }
        catch (Exception ex)
        {
            GD.PushWarning($"MeshRegistry: glTF swap failed ({ex.Message}); keeping procedural meshes.");
        }
    }

    // Map a mesh's surface-0 AABB to a centered 1x1x1 box (each axis independently), baking the fit into a fresh
    // single-surface ArrayMesh so GetMesh keeps returning a plain unit Mesh. `nodeXform` folds in any transform
    // the glTF importer placed on the MeshInstance3D (our generator authors at identity, so it's normally Identity).
    private static ArrayMesh NormalizeToUnitBox(Mesh raw, Transform3D nodeXform)
    {
        Aabb bb = nodeXform * raw.GetAabb();
        Vector3 size = bb.Size;
        var inv = new Vector3(
            size.X > 1e-6f ? 1.0f / size.X : 1.0f,
            size.Y > 1e-6f ? 1.0f / size.Y : 1.0f,
            size.Z > 1e-6f ? 1.0f / size.Z : 1.0f);   // guard zero/flat extents
        Vector3 c = bb.Position + size * 0.5f;
        var norm = new Transform3D(Basis.Identity.Scaled(inv), -c * inv);   // v -> (v - c) component-wise * inv

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.AppendFrom(raw, 0, norm * nodeXform);   // local -> node/root space -> centered unit box
        return st.Commit();
    }

    private static MeshInstance3D FindFirstMeshInstance(Node node)
    {
        if (node == null) return null;
        if (node is MeshInstance3D mi) return mi;
        foreach (Node child in node.GetChildren())
        {
            MeshInstance3D found = FindFirstMeshInstance(child);
            if (found != null) return found;
        }
        return null;
    }

    // Transform of `node` relative to `root` (accumulate Node3D.Transform up the chain). Identity if node == root.
    private static Transform3D RelativeTransform(Node root, Node3D node)
    {
        Transform3D t = Transform3D.Identity;
        Node n = node;
        while (n != null && n != root)
        {
            if (n is Node3D s) t = s.Transform * t;
            n = n.GetParent();
        }
        return t;
    }
}
