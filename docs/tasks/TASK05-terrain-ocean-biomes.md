# Task 5 — Terrain + ocean + biomes (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 5. It is self-contained — you do not need
> the planning conversation. When you finish, you verify (build + headless check + a hands-on GPU playtest),
> commit, and bring results back to session A for review. One such doc lives in `docs/tasks/` per task.

> **What this task is.** Give the baked world a **ground**. Render a **per-tile terrain mesh** from the baked
> heightmap (with **per-vertex biome colours**), give the **tiles near the car trimesh colliders** (so you can
> land on it), and float a **dusk water plane at Y=0** under a new `shaders/water.gdshader`. This is the task
> that **fixes the floating city** — the terrain rises to meet the building bases — and turns the empty plane
> into a **coastal landscape**: ocean with a bay + islands, beaches, and a desert→hills→mountain ring.

> **What this task is NOT.** **No synthwave-dusk retune** — the environment/sky/fog stay on their current
> neon-night tuning (Task 6 makes it warm dusk). Terrain + water ship with **legible starting colours**, not
> final art; the distance will still read cool/dark through the current fog — **that is expected** (see §0.2).
> **No water gameplay** — the ocean is **visual-only**: you fly *through* it, no drag/damage/splash/foam/depth
> transparency (deferred). **No glTF** (Task 8). **No traffic** (Task 7). **No re-bake** — `world_8km.res` is
> **unchanged**; you only *read* the heightmap/biome data already in it (if you find yourself wanting to change
> the bake or `WorldGenerator`, stop — that's a Task-3 concern; the one allowed touch is **adding non-`[Export]`
> helper methods** to `WorldData`, see §3.1). **Do not** touch the flying car / camera / HUD / damage (Task 2)
> or the building-render path (`MeshRegistry`/`TileBuilder` object code, `building.gdshader`).

> **Scope discipline.** Implement Task 5 only. Create `shaders/water.gdshader` and
> `scripts/world/TerrainBuilder.cs`; add two helper methods to `WorldData`; extend `WorldLoader`/`WorldTile`
> to build + collide terrain; in `Game.cs` **remove** the stand-in `RefGround` plane + the test-field floor
> slab + the `_Process` ground-follow, and **add** an ocean plane. Leave `Ship.cs`, `CameraRig.cs`, `Hud.cs`,
> `DamageComponent.cs`, `ScreenFX.cs`, `Traffic.cs`, `WorldGenerator.cs`, `TileBuilder.cs`, `MeshRegistry.cs`,
> and every shader except the new `water.gdshader` **unchanged**. **Do not edit `settings/settings.cfg`** (the
> tunables are `[Export]`s / `[fx]` fallbacks read in code — see §3.5/§3.6; session A curates the new `[fx]`
> water keys on review). **Do not edit `CLAUDE.md` or `docs/` prose** (session A owns docs).

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (read **`docs/GAME_SPEC.md` §7.1–§7.3** and
**`CLAUDE.md`** before starting). The world is **fixed, finite (≈8×8 km), baked once to disk** as layout *data*
— a **terrain heightmap**, a **biome map**, and **object descriptors** — then **loaded at runtime**. Tasks 1–4
are done: the arcade loops are stripped, the car is a `RigidBody3D` 6-DOF flyer with impulse damage (Task 2),
the world is baked to `res://world/world_8km.res` (Task 3), and at runtime the **building descriptors** render
as GPU `MultiMesh` batches on a fixed **world-tile grid** with lazy near-car box colliders (Task 4).

**Right now the heightmap and biome map are baked but unrendered**, so the city **floats above a flat dark
plane** (`Game.cs`'s `RefGround`) sitting at sea level. Task 5 renders that data: terrain rises to meet the
towers, the coast appears, and the flat stand-ins go away.

### 0.1 The data you read (already in the bake — do not regenerate)

`WorldData` (`scripts/world/WorldData.cs`) holds everything you need:

- **`float[] Heights`** — an **N×N** grid (row-major, `N = HeightmapResolution`, **256**) of **normalised**
  heights in `[0,1]`. **Real elevation (metres) = `Mathf.Lerp(MinY, MaxY, Heights[iz*N + ix])`.**
- **`float MinY` / `float MaxY`** — the elevation range (the committed bake is roughly `MinY ≈ -110`,
  `MaxY ≈ 804`). **Sea level is world `Y = 0`** (so `MinY < 0 < MaxY`): cells below 0 are underwater.
- **`byte[] Biomes`** — the same N×N grid, one **`Biome`** byte per cell:
  `Ocean=0, Beach=1, City=2, Desert=3, Hills=4, Mountains=5` (`scripts/world/Biome.cs`).
- **`float WorldExtent`** — 8000 m, the world width on X and Z, **centred on the origin** (so X,Z ∈
  `[-4000, 4000]`). `half = WorldExtent/2`, heightmap `cell = WorldExtent/N` (≈31.25 m).
- **`Array<PlacedObject> Objects`** — the ~315 buildings (already rendered by Task 4 — leave that path alone).

The layout the data encodes (from `WorldGenerator`, for your mental model — **do not re-derive it**): a
**city plateau** flattened at `Y≈38` around world `(-1400, -200)` radius ≈1500; a **coastline** on the **+X
side** dropping to ocean; a **bay** carved around `(600, 1700)`; **3 islands** that poke above the sea; a
**mountain ring** lifting toward the world edge; **beaches** where land eases to the waterline.

**The seating function (THE alignment key — §4).** Each building's base Y was set at bake time by a **bilinear
sample** of the normalised heightmap: `WorldGenerator.SampleHeight(...)` (lines **367–378**), with the building
centred at `(x, baseY + height*0.5, z)`. Your terrain **must sample the same heightmap the same way** so it
rises to **exactly** those bases. You will copy that math (and `BiomeAt`, lines **358–364**) into `WorldData`
as runtime helpers (§3.1) — **verbatim**, so the two agree byte-for-byte.

### 0.2 The expected look after Task 5 (so the playtest isn't a surprise)

- The **city sits on the ground** — the Task-4 floating-city gap is **gone**. Tower bases meet the plateau.
- Beyond downtown: a **warm desert** rising through **dusty hills** to a **grey mountain ring** on the horizon.
- The **+X coast drops into an ocean at Y=0**, with a **bay** bitten out of the shore and **3 islands** above
  the water; **beaches** at the waterline.
- The water is a **reflective dusk surface that ripples**; you **fly through it** (no collision, by design).
- **Colours are legible, not final.** The lighting/fog are still the **neon-night** Task-4 tuning, so the
  distance reads **cool and a bit dark**, and biome colours are starting values (sand / asphalt / tan / olive /
  grey / teal) — **Task 6 makes it warm synthwave dusk.** Don't fight the palette here.
- Faint **per-tile lighting seams** on terrain are possible; we avoid them with analytic normals (§3.2) — if
  any remain, note them (a later polish, not a Task-5 gate).

---

## 1. Definition of Done

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → boots headless, **exits 0**, and prints the **updated loader line**
   (objects + terrain-tile counts — see §3.5); the **terrain builds headless** (1024 tile meshes — a real
   integration test), no errors.
3. `./run.sh play` (**GPU — eyeball these; headless can't render**; the repo has a `memory` note that world
   visuals must be verified in `play`):
   - **The city sits on terrain** — the floating gap is gone; tower bases meet the ground plateau.
   - **A coastal landscape:** ocean at Y=0 with a **bay + 3 islands**, beaches at the shore, and **desert →
     hills → mountains** ringing the basin. It **reads as a coast**, not a flat plane.
   - **Land + bounce + damage on terrain:** descend and **rest the car on the ground** — it's **solid**
     (doesn't fall through); ram terrain at speed → **bounce + CONDITION drops + never game-over**. **Thread
     the canyons** between towers; **skim low over the water** (you pass through it — visual-only).
   - **Identical every launch:** quit and re-run — same coastline, same skyline, same terrain (no RNG).
   - **FPS holds** at a cruise (1024 small terrain tiles + ~85 building tiles, all `VisibilityRange` +
     frustum-culled; note any lever you touch — see §5).

---

## 2. Preconditions

- Branch `dev5`, Tasks 1–4 merged. Confirm green before you start:
  `./run.sh build && ./run.sh check` → exits 0 and prints the Task-4 line
  `:: WorldLoader: 315 objects across 85 populated tiles (250 m).` (your new line replaces it — §3.5).
- `res://world/world_8km.res` is present and committed (the baked world; **read-only here**).
- Read `docs/GAME_SPEC.md` **§7.1–§7.3** and `CLAUDE.md` (the architecture rules: terrain as a tile grid with
  `VisibilityRange` LOD, **colliders only near the car**, framerate-independent code, `[Export]` tunables).

---

## 3. Work items

Six pieces. Build them in this order (each compiles on its own); wire + playtest last.

### 3.1 `scripts/world/WorldData.cs` — add the runtime height/biome samplers (additive; no schema change)

Add two **non-`[Export]`** instance methods. They are **not serialised** (a `Resource` persists only
`[Export]` members), so this is **purely additive** — `world_8km.res` is unchanged, no re-bake, no
`FormatVersion` bump. **Copy the math verbatim from `WorldGenerator.SampleHeight`/`BiomeAt`** (using the
`HeightmapResolution` field where the generator used its private `N` const — they're equal for the committed
bake). These are the single source of truth that makes terrain agree with the building bases (§4).

```csharp
// ── Runtime sampling (Task 5) — additive, NOT serialised (no [Export]); world_8km.res unchanged. ──
// Real elevation (metres) at world (x,z): bilinear over the normalised heightmap, then Lerp(MinY,MaxY).
// MUST stay byte-identical to WorldGenerator.SampleHeight so terrain rises to EXACTLY meet the baked
// building bases (TASK05 §4). The city plateau is flat, so towers there seat perfectly regardless.
public float HeightAt(float x, float z)
{
    int n = HeightmapResolution;
    float half = WorldExtent * 0.5f, cell = WorldExtent / n;
    float gx = Mathf.Clamp((x + half) / cell - 0.5f, 0.0f, n - 1.001f);
    float gz = Mathf.Clamp((z + half) / cell - 0.5f, 0.0f, n - 1.001f);
    int ix = (int)gx, iz = (int)gz;
    int ix1 = Mathf.Min(ix + 1, n - 1), iz1 = Mathf.Min(iz + 1, n - 1);
    float tx = gx - ix, tz = gz - iz;
    float h0 = Mathf.Lerp(Heights[iz * n + ix], Heights[iz * n + ix1], tx);
    float h1 = Mathf.Lerp(Heights[iz1 * n + ix], Heights[iz1 * n + ix1], tx);
    return Mathf.Lerp(MinY, MaxY, Mathf.Lerp(h0, h1, tz));
}

// Coarse biome of the heightmap cell containing world (x,z). Mirrors WorldGenerator.BiomeAt verbatim.
public Biome BiomeAt(float x, float z)
{
    int n = HeightmapResolution;
    float half = WorldExtent * 0.5f, cell = WorldExtent / n;
    int ix = Mathf.Clamp((int)((x + half) / cell), 0, n - 1);
    int iz = Mathf.Clamp((int)((z + half) / cell), 0, n - 1);
    return (Biome)Biomes[iz * n + ix];
}
```

### 3.2 `scripts/world/TerrainBuilder.cs` — heightmap slice → `MeshInstance3D` + trimesh collider (NEW)

A static builder, parallel to `TileBuilder` (which stays the *object* builder). It owns the **biome palette**
and **one shared terrain material**, builds a tile's terrain `ArrayMesh` via `SurfaceTool`, and builds the
matching trimesh collider from that same mesh.

```csharp
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
```

Notes:
- **`quads`** is the per-tile resolution; **8** gives ≈one heightmap cell per quad at a 250 m tile (9×9
  vertices, 128 tris). Higher = smoother + more verts; it's an `[Export]` on the loader (§3.5).
- No UVs needed (vertex-colour albedo, no texture). Don't enable shadow casting (leave defaults; Task 6 owns
  lighting).

### 3.3 `shaders/water.gdshader` — dusk ocean (NEW)

A `spatial` shader on a **flat plane** (no vertex displacement): **world-anchored FBM ripples** perturb the
normal, a **fresnel rim** brightens toward grazing angles (so the surface catches the horizon glow), and a
depth-tinted base reads as deep water. Uniforms are driven from `[fx]` by `Game` (§3.6) with fallbacks; Task 6
retunes. Use `shaders/sky.gdshader` as the house style for the noise helpers. **GPU-eyeball + tune in `play`.**

```glsl
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// DIVEPUNK dusk ocean — a flat plane at world Y=0. Fragment-only (no vertex displacement): world-anchored
// FBM ripples perturb the normal; a fresnel rim brightens at grazing angles (catches the horizon glow); a
// depth-tinted base reads as deep water. Visual-only (no collision). Task 6 retunes for synthwave dusk.

uniform vec3 deep_color : source_color = vec3(0.02, 0.06, 0.10);
uniform vec3 shallow_color : source_color = vec3(0.05, 0.16, 0.20);
uniform vec3 fresnel_color : source_color = vec3(0.50, 0.28, 0.55);  // horizon-glow tint at grazing angles
uniform float fresnel_power = 4.0;
uniform float water_roughness = 0.08;     // low -> SSR/specular catches the neon
uniform float ripple_scale = 0.015;       // world-space ripple frequency (1/m)
uniform float ripple_speed = 0.04;
uniform float ripple_strength = 0.25;     // normal-perturbation amount
uniform float water_energy = 1.0;

varying vec3 world_pos;

float hash21(vec2 p){ p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1,0)), c = hash21(i + vec2(0,1)), d = hash21(i + vec2(1,1));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y); }
float fbm(vec2 p){ float v = 0.0, amp = 0.5; for (int k = 0; k < 4; k++){ v += amp * vnoise(p); p *= 2.02; amp *= 0.5; } return v; }
float wave(vec2 uv, float t){ return fbm(uv + vec2(t, t * 0.6)) + fbm(uv * 2.3 - vec2(t * 0.8, t)); }

void vertex(){ world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment(){
    vec2 uv = world_pos.xz * ripple_scale;
    float t = TIME * ripple_speed;
    float h  = wave(uv, t);
    float hx = wave(uv + vec2(0.06, 0.0), t);
    float hz = wave(uv + vec2(0.0, 0.06), t);
    vec3 n = normalize(vec3((h - hx) * ripple_strength, 1.0, (h - hz) * ripple_strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

    float fres = pow(1.0 - clamp(dot(normalize(VIEW), NORMAL), 0.0, 1.0), fresnel_power);
    ALBEDO = mix(deep_color, shallow_color, clamp(h * 0.5, 0.0, 1.0)) * water_energy;
    EMISSION = fresnel_color * fres * water_energy;   // grazing-angle glow toward the horizon
    ROUGHNESS = water_roughness;
    METALLIC = 0.0;
    SPECULAR = 0.5;
}
```

If it ever fails to compile on a GPU (flat magenta), that's what the `[fx] water` gate is for (§3.6) — set it
false to drop the plane. Keep it **opaque** for now (no `blend_mix`, no depth-fade transparency) — shoreline
foam / depth transparency is deferred to Task 6.

### 3.4 `scripts/world/WorldTile.cs` — build terrain too, and stop early-returning on empty tiles

Two edits. **The critical change:** most of the 1024 tiles have **no buildings** but **all** have terrain, so
`BuildVisuals`/`EnsureColliders` **must not early-return on `_objects.Count == 0`** (they do today — that was
correct in Task 4 when only populated tiles existed). Keep the terrain mesh reference so the lazy collider can
reuse it.

```csharp
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
    public void BuildVisuals(WorldData data, float tileSize, int terrainQuads, float viewEnd, float fadeMargin)
    {
        if (_visualsBuilt) return;
        MeshInstance3D terrain = TerrainBuilder.BuildTerrainMesh(data, Center, tileSize, terrainQuads, viewEnd, fadeMargin);
        _terrainMesh = terrain.Mesh;
        AddChild(terrain);
        if (_objects.Count > 0)
            foreach (MultiMeshInstance3D mmi in TileBuilder.BuildMultiMeshes(_objects, Center, viewEnd, fadeMargin))
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
```

### 3.5 `scripts/world/WorldLoader.cs` — full-coverage tile grid + terrain wiring

Today the loader creates tiles **only where objects land** (~85). Terrain needs **every** tile, so: **create
the full grid first**, then bucket objects into the existing tiles, then `BuildVisuals` all of them. Add a
`TerrainTileQuads` `[Export]`, pass `_data`/`TileSize`/`TerrainTileQuads` into `BuildVisuals`, and update the
print line. **`_PhysicsProcess`/`UpdateColliders` are unchanged** — the lazy add/prune already calls
`EnsureColliders`/`ClearColliders`, which now also handle terrain.

Add the export (in the `Tiling / LOD` group):
```csharp
[Export] public int TerrainTileQuads = 8;   // terrain mesh resolution per tile (~1 heightmap cell per quad at 250 m)
```

`_Ready` — replace the build section (load + `_half`/`_colliderMat` setup stay as-is):
```csharp
    BuildTileGrid();    // full coverage: EVERY grid cell gets a tile (terrain everywhere, not just the city)
    BucketObjects();    // drop each PlacedObject into its (already-created) tile
    foreach (WorldTile tile in _tiles.Values)
        tile.BuildVisuals(_data, TileSize, TerrainTileQuads, ViewDistance, ViewFadeMargin);

    int populated = 0;
    foreach (WorldTile t in _tiles.Values) if (t.Count > 0) populated++;
    GD.Print($":: WorldLoader: {_data.Objects.Count} objects, {_tiles.Count} terrain tiles ({populated} populated), {TileSize:F0} m.");
```

New `BuildTileGrid`, and `BucketObjects` adjusted to use the pre-created tiles:
```csharp
private void BuildTileGrid()
{
    int side = Mathf.RoundToInt(_data.WorldExtent / TileSize);   // 8000 / 250 = 32  ->  1024 tiles
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
```

`TileOf`/`TileCenter` are unchanged (`TileCenter` already returns Y=0 — correct, terrain Y is world Y).
**Heads-up:** building 1024 `SurfaceTool` meshes at load adds a beat to startup (still well under a second on
this hardware; the headless `check` exercises it). If it ever bites, the lever is `TerrainTileQuads` or
lazy/visible-only terrain build — note it, don't pre-optimise.

### 3.6 `scripts/Game.cs` — drop the stand-ins, add the ocean

Terrain + water now provide the ground and the sea, so the Task-1/2 stand-ins go away. Make these edits and
**nothing else** (leave `EnsureEnvironment`'s `Sun` + `WorldEnvironment`, the flight/camera config, the HUD,
screen-FX, and input registration untouched — Task 6 owns the environment/sky retune):

1. **Remove `RefGround`** — delete the `RefGround` creation block in `EnsureEnvironment` (the `MeshInstance3D`
   with the dark wet plane, currently ~lines 265–285) **and** the `private MeshInstance3D _ground;` field
   **and** the `_Process` override (its only job is re-centring `_ground`, ~lines 191–199). Terrain is the
   ground now.
2. **Remove the test-field floor** — delete `SpawnTestField()` (the 40 km collision slab, ~lines 117–137) and
   its call in `_Ready`. The lazy **terrain colliders** are the ground; the car hovers (`GravityScale = 0`),
   so there's no fall-through even before the first collider builds. (If the world ever fails to load, there's
   no floor and the car hovers in an empty void — the loader already prints the error; acceptable.)
3. **Add the ocean** — a single water plane at Y=0, `[fx]`-gated, read via the existing `Cfg*` fallbacks
   (so it works with **no `settings.cfg` change** — session A adds the curated keys on review). Add a
   `SpawnOcean()` call in `_Ready` **after `SpawnWorld()`**, and a `WaterShader` field next to `SkyShader`:

```csharp
private static readonly Shader WaterShader = GD.Load<Shader>("res://shaders/water.gdshader");

// A dusk ocean plane at sea level (world Y=0), under shaders/water.gdshader. Visual-only (no collider — you
// fly through it). Fixed at the origin (the world is bounded), sized to reach past the view. [fx] water=false
// drops it (you'd then see the bare seabed terrain). Tunables fall back to the shader defaults if absent.
private void SpawnOcean()
{
    if (GetNodeOrNull("Ocean") != null) return;
    if (!CfgBool("fx", "water", true)) return;

    float extent = CfgFloat("world", "extent", 8000.0f);
    var ocean = new MeshInstance3D
    {
        Name = "Ocean",
        Mesh = new PlaneMesh { Size = new Vector2(extent * 2.0f, extent * 2.0f) },   // covers the world + horizon margin
        Position = new Vector3(0.0f, 0.0f, 0.0f),
    };
    var mat = new ShaderMaterial { Shader = WaterShader };
    mat.SetShaderParameter("ripple_speed", CfgFloat("fx", "water_ripple_speed", 0.04f));
    mat.SetShaderParameter("water_energy", CfgFloat("fx", "water_energy", 1.0f));
    ocean.MaterialOverride = mat;
    AddChild(ocean);
}
```

---

## 4. Correctness points (get these right)

1. **Terrain must meet the buildings — same heightmap, same sampler.** Terrain samples elevation via
   `WorldData.HeightAt` (§3.1), a **verbatim copy** of the bilinear `WorldGenerator.SampleHeight` that set each
   building's `baseY`. So the surface rises to **exactly** the tower bases. The city plateau is **flat**
   (`Y≈38`), so downtown seats perfectly; on slopes the two agree to within bilinear-interpolation error
   (sub-metre to a few m — invisible at dusk). **Do not invent a different height function** (no separate
   smoothing, no nearest-cell, no re-derivation) — that reopens the floating/buried-city gap.

2. **No re-bake; `WorldData` change is additive only.** `world_8km.res`, `WorldGenerator.cs`, and
   `FormatVersion` are **untouched**. `HeightAt`/`BiomeAt` are **non-`[Export]`** methods → not serialised →
   no schema/determinism impact. (If you feel the urge to change the bake, you're out of scope.)

3. **Tile-local positioning (same rule as TASK04 §4).** Terrain vertices are **tile-local in XZ**
   (`worldXZ − tileCenter`) and **world in Y** (the tile node sits at `Center` with `Y=0`). Because adjacent
   tiles sample the **shared world coordinate** on their common edge, edge heights are **identical → no
   cracks**. Analytic normals (central differences on the global field) are **continuous across borders → no
   per-tile lighting seams**. The collider mesh is the **same** tile-local mesh, under the tile node → aligned.

4. **The empty-tile gotcha (the most likely bug).** ~940 of the 1024 tiles have **no buildings**.
   `BuildVisuals`/`EnsureColliders` previously early-returned when `_objects.Count == 0`; now they **must not**
   — every tile builds terrain and a terrain collider regardless. Double-check both methods (§3.4).

5. **Identical every launch.** Terrain comes from the static heightmap; there is **no runtime RNG**. Same
   world, same render, every boot.

---

## 5. Verify

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → exits 0; prints the new loader line
   (`:: WorldLoader: 315 objects, 1024 terrain tiles (85 populated), 250 m.` — counts may differ slightly if
   `TileSize`/extent change, but **objects=315** and **populated≈85** should match Task 4). No errors; note the
   startup time if it's visibly slower.
3. `./run.sh play` (**GPU — eyeball; headless can't render**), against §1:
   - **City on the ground** (floating gap gone); descend and **rest on terrain** — solid, no fall-through.
   - **Coast reads:** ocean at Y=0, **bay + 3 islands**, beaches, **desert → hills → mountains** ring.
   - **Bounce + damage + never game-over** ramming terrain and towers; **skim through the water** (no
     collision); **thread the canyons**.
   - **Identical relaunch:** quit + re-run → same coastline + skyline + terrain.
   - **FPS holds** at a cruise. If it hitches, the levers (note any you touch): `TerrainTileQuads` (lower =
     fewer verts), `ViewDistance`/`ViewFadeMargin`, `[fx] water=false`, or lazy/visible-only terrain build.
4. **Water sanity:** the surface **ripples** and brightens at grazing angles; `[fx] water=false` (set it in
   your local `settings.cfg` to test, then **revert** — do not commit that change) drops the plane and reveals
   the seabed terrain (proof terrain covers the sea floor too).

---

## 6. Report back to session A (for review)

Include:
- `git status` + `git diff --stat`, the **new files** (`shaders/water.gdshader`,
  `scripts/world/TerrainBuilder.cs` + their `.uid`s), and the edits (`WorldData.cs`, `WorldLoader.cs`,
  `WorldTile.cs`, `Game.cs`).
- `./run.sh build` + `./run.sh check` output (including the new loader line; note any startup-time change).
- A description of the **`play` GPU playtest** against §1/§5 (you can't screenshot headless — describe what you
  saw: city seated on terrain, the coast/bay/islands/biome ring, landing + bounce + damage on terrain, water
  ripple, identical relaunch, rough FPS).
- **Confirmation** that `world_8km.res` / `WorldGenerator` are untouched (no re-bake), the `WorldData` change
  is additive (`HeightAt`/`BiomeAt`, no `[Export]`), and `Ship`/`Camera`/`HUD`/`Damage`/`Traffic`/the
  building-render path were left alone.
- **Any deviations** from this brief and why; **anything deferred** (warm-dusk retune, water gameplay,
  shoreline foam / depth transparency, the wet-street city-ground look, glTF, traffic — confirm these stay
  out).
- **Commit** once build + check + playtest are green. Stage **by path** (never `git add -A`); **do not** stage
  `settings/settings.cfg` (leave it untouched — session A adds the `[fx]` water keys on review). **Leave
  `docs/GAME_SPEC.md` §9 Task 5 unticked** — session A ticks it on review. Suggested message:
  `Task 5: per-tile terrain mesh (biome vertex colours) + trimesh colliders + dusk water plane`.
  End the commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **Synthwave-dusk retune** of `BuildEnvironment`/`BuildSky`/`sky.gdshader`/fog/`[fx]` → **Task 6.** Ship
  legible starting colours; don't fight the current neon-night lighting.
- **Water as gameplay** (drag, damage, splash, buoyancy) and **water polish** (shoreline foam, depth-fade
  transparency, planar/sub-surface refraction) → later. Visual-only opaque plane now.
- **The wet-street reflective city ground** (the old `RefGround` look) → a Task-6 aesthetic call (e.g. a
  separate City-biome material/shader). Task 5 ships **matte** biome terrain.
- **Cross-tile normal smoothing beyond the analytic approach**, a single Jolt-scaled `HeightMapShape3D`
  terrain collider, terrain LOD meshes → later optimisations. (Task-5 colliders are per-tile trimeshes near
  the car only.)
- **glTF model loading** → Task 8. **Ambient traffic** (`Traffic.cs`) → Task 7. **World-border soft force** on
  the car → later.
- **Re-baking or editing `world_8km.res` / `WorldGenerator` / any Task-3 serialised data** — read-only here
  (the only `WorldData` edit is the two additive non-`[Export]` samplers).
- **Editing `settings/settings.cfg`, `CLAUDE.md`, or `docs/` prose**, or changing the flying car / camera /
  HUD / damage / screen-FX / building-render path (`MeshRegistry`/`TileBuilder` objects, `building.gdshader`).
