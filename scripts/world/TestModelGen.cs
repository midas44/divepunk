using Godot;

// Builds the Task-8 test glTF and writes it to disk. Shared by the headless runner (ModelGenRunner, via
// ./run.sh modelgen) so there is ONE generation path — mirrors WorldBaker. Authoring a .glb is pure data
// (SurfaceTool + GltfDocument), so this is headless-safe. Tool/build-time only: res:// is writable in the
// editor and in --headless tool runs, but read-only in an exported game (we never run this in the shipped
// build — the .glb is generated once and committed to git).
//
// The model is a recognizably distinct setback/stepped tower (clearly unlike the smooth taper/round cylinders
// it replaces in MeshRegistry). It is authored at real-ish size (~20 x 72 x 20 m) and is NOT pre-normalized:
// MeshRegistry normalizes its AABB to the centered 1x1x1 unit box at load, which is exactly what proves the
// seam handles a real model. The dims here only set the model's proportions (the per-instance bake transform
// drives final size).
public static class TestModelGen
{
    public const string OutPath = "res://assets/models/test_tower.glb";

    // One single-surface ArrayMesh: stacked boxes of decreasing footprint + a thin rooftop antenna, all merged
    // into ONE SurfaceTool surface so the exported glb is single-mesh / single-primitive (a MultiMesh needs one
    // Mesh). No material (MeshRegistry assigns the shared building shader); no GenerateNormals (keep the hard box
    // edges); no UVs/tangents needed (building.gdshader is world-space).
    public static ArrayMesh BuildTowerMesh()
    {
        var steps = new (Vector3 Size, float CenterY)[]
        {
            (new Vector3(20.0f, 24.0f, 20.0f), 12.0f),  // base
            (new Vector3(14.0f, 22.0f, 14.0f), 35.0f),  // mid setback   (24..46)
            (new Vector3(8.0f,  18.0f, 8.0f),  55.0f),  // upper setback (46..64)
            (new Vector3(1.5f,  8.0f,  1.5f),  68.0f),  // rooftop antenna (64..72)
        };

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (size, centerY) in steps)
        {
            var box = new BoxMesh { Size = size };           // centered at origin
            st.AppendFrom(box, 0, new Transform3D(Basis.Identity, new Vector3(0.0f, centerY, 0.0f)));
        }
        return st.Commit();
    }

    public static Error Export(string resPath = OutPath)
    {
        // Ensure assets/models/ exists (not created automatically) — mirrors WorldBaker's res://world guard.
        string dir = resPath.GetBaseDir();
        if (!DirAccess.DirExistsAbsolute(dir))
            DirAccess.MakeDirRecursiveAbsolute(dir);

        var root = new Node3D { Name = "TestTower" };
        root.AddChild(new MeshInstance3D { Name = "TowerMesh", Mesh = BuildTowerMesh() });

        var doc = new GltfDocument();
        var state = new GltfState();
        Error err = doc.AppendFromScene(root, state);
        if (err == Error.Ok)
            err = doc.WriteToFilesystem(state, resPath);
        root.Free();   // out-of-tree throwaway

        GD.Print(err == Error.Ok ? $":: test model written -> {resPath}" : $":: test model export FAILED ({err})");
        return err;
    }
}
