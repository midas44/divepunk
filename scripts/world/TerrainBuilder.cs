using Godot;

// Builds a tile's terrain MeshInstance3D (a heightmap slice -> ArrayMesh via SurfaceTool, per-vertex biome
// COLOR, analytic normals) and its lazy trimesh collider. Terrain samples the SAME heightmap the bake used
// (WorldData.HeightAt) so it rises to meet the baked building bases (TASK05 §4). One shared dusk material
// (vertex colour as albedo); Task 6 retunes the palette/material for warm synthwave dusk.
public static class TerrainBuilder
{
    // Biome -> base colour, indexed by (int)Biome (Ocean,Beach,City,Desert,Hills,Mountains). LEGIBLE
    // starting values only — Task 6 retunes. (Ocean is the seabed: mostly hidden under the water plane.)
    private static readonly Color[] Palette =
    {
        new(0.03f, 0.08f, 0.11f),  // Ocean     — dark teal seabed
        new(0.60f, 0.52f, 0.37f),  // Beach     — warm sand
        new(0.09f, 0.09f, 0.12f),  // City      — dark asphalt between the towers
        new(0.52f, 0.39f, 0.25f),  // Desert    — warm tan
        new(0.33f, 0.33f, 0.20f),  // Hills     — dusty olive
        new(0.30f, 0.29f, 0.31f),  // Mountains — cool grey rock
    };

    private static StandardMaterial3D _mat;
    private static StandardMaterial3D Material() => _mat ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.92f,
        Metallic = 0.0f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,   // matte; the wet-street look is a Task-6 call
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,           // double-sided: the hand-wound heightmap mesh renders both sides so terrain never backface-culls into torn fragments
    };

    // A (quads+1)^2 vertex grid over the tile, sampled from the heightmap. Vertices are TILE-LOCAL in XZ
    // (worldXZ - tileCenter) and WORLD in Y (the tile node sits at Y=0), exactly like the building instances.
    // Edge vertices fall on world coords SHARED with the neighbour tile -> identical heights -> no cracks.
    // Normals are analytic (central differences on the GLOBAL height field) -> continuous across tile borders,
    // so there are no per-tile lighting seams.
    public static MeshInstance3D BuildTerrainMesh(WorldData data, Vector3 tileCenter, float tileSize, int quads,
        float viewEnd, float fadeMargin)
    {
        float half = tileSize * 0.5f;
        float e = tileSize / quads * 0.5f;   // half-a-quad sample step for the analytic normal

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = 0; j <= quads; j++)
        for (int i = 0; i <= quads; i++)
        {
            float wx = tileCenter.X - half + (i / (float)quads) * tileSize;
            float wz = tileCenter.Z - half + (j / (float)quads) * tileSize;
            float y = data.HeightAt(wx, wz);

            // analytic normal from the global field (continuous across tiles)
            float hL = data.HeightAt(wx - e, wz), hR = data.HeightAt(wx + e, wz);
            float hD = data.HeightAt(wx, wz - e), hU = data.HeightAt(wx, wz + e);
            st.SetNormal(new Vector3(hL - hR, 2.0f * e, hD - hU).Normalized());
            st.SetColor(Palette[(int)data.BiomeAt(wx, wz)]);
            st.AddVertex(new Vector3(wx - tileCenter.X, y, wz - tileCenter.Z));   // tile-local XZ, world Y
        }

        int stride = quads + 1;
        for (int j = 0; j < quads; j++)
        for (int i = 0; i < quads; i++)
        {
            int a = j * stride + i, b = a + 1, c = a + stride, d = c + 1;
            st.AddIndex(a); st.AddIndex(c); st.AddIndex(b);   // CCW seen from +Y
            st.AddIndex(b); st.AddIndex(c); st.AddIndex(d);
        }

        ArrayMesh mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, Material());
        return new MeshInstance3D
        {
            Name = "Terrain",
            Mesh = mesh,
            VisibilityRangeEnd = viewEnd,
            VisibilityRangeEndMargin = fadeMargin,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
    }

    // A static trimesh collider from the SAME terrain mesh. DEFAULT physics layer (1, "ship") — like the
    // building boxes — so the car (mask 1) bounces and the existing impulse->damage path fires with NO Ship
    // change. Added to the "terrain" group for future contact attribution. Built lazily, near the car only.
    public static StaticBody3D BuildTerrainCollider(Mesh terrainMesh, PhysicsMaterial mat)
    {
        var body = new StaticBody3D { Name = "TerrainCol", PhysicsMaterialOverride = mat };
        body.AddToGroup("terrain");
        body.AddChild(new CollisionShape3D { Shape = terrainMesh.CreateTrimeshShape() });
        return body;
    }
}
