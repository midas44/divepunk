using Godot;

// Builds a tile's terrain MeshInstance3D (a heightmap slice -> ArrayMesh via SurfaceTool, per-vertex biome
// COLOR, analytic normals) and its lazy trimesh collider. Terrain samples the SAME heightmap the bake used
// (WorldData.HeightAt) so it rises to meet the baked building bases (TASK05 §4). Two cached dusk materials
// (matte land + wet reflective city streets), both vertex-colour-as-albedo; Task 11 re-grades to cold.
public static class TerrainBuilder
{
    // Biome -> base colour, indexed by (int)Biome (Ocean,Beach,City,Desert,Hills,Mountains). Cold,
    // desaturated, but legible (dark != muddy black) — Task 11 re-grade. (Ocean is the seabed: mostly hidden.)
    private static readonly Color[] Palette =
    {
        new(0.03f, 0.05f, 0.09f),  // Ocean     — cold seabed (mostly hidden under the water plane)
        new(0.40f, 0.41f, 0.45f),  // Beach     — cold gray-sand, drained of warmth
        new(0.09f, 0.09f, 0.12f),  // City      — dark cold asphalt
        new(0.36f, 0.35f, 0.36f),  // Desert    — cold gray-tan dystopian dead ground
        new(0.24f, 0.25f, 0.29f),  // Hills     — cold gray-violet
        new(0.26f, 0.28f, 0.34f),  // Mountains — cold blue-gray rock
    };

    private static StandardMaterial3D _mat;
    private static StandardMaterial3D Material() => _mat ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.92f,
        Metallic = 0.0f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,   // matte land (beach/desert/hills/mountains)
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,           // double-sided: the hand-wound heightmap mesh renders both sides so terrain never backface-culls into torn fragments
    };

    // Wet-street variant for predominantly-City tiles (Task 6): keeps the dark-asphalt vertex colour but goes
    // low-roughness with real specular + a faint clearcoat, so SSR ([fx] ssr) reflects the neon towers in the
    // streets — the rainy-cyberpunk look. Cached like Material() — at most TWO terrain material instances
    // exist for the whole world, regardless of tile count.
    private static StandardMaterial3D _wetMat;
    private static StandardMaterial3D WetMaterial() => _wetMat ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,                              // keep the dark-asphalt biome colour
        Roughness = 0.14f,                                         // low -> SSR/specular catches the neon (wet)
        Metallic = 0.0f,
        MetallicSpecular = 0.6f,                                   // a touch hotter highlight than the 0.5 default
        SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx, // enable spec (the dry mat Disables it)
        Clearcoat = 0.5f,                                          // faint wet-sheen coat on top
        ClearcoatRoughness = 0.1f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,          // double-sided, identical to the dry mat
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

        int cityVerts = 0;   // tally City-biome verts -> a predominantly-city tile gets the wet-street material
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
            Biome b = data.BiomeAt(wx, wz);   // reuse the sample for both the colour and the wet-street tally
            if (b == Biome.City) cityVerts++;
            st.SetColor(Palette[(int)b]);
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
        int totalVerts = (quads + 1) * (quads + 1);
        bool wet = cityVerts * 2 >= totalVerts;   // City is the majority of the tile -> wet streets reflect the neon
        mesh.SurfaceSetMaterial(0, wet ? WetMaterial() : Material());
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
