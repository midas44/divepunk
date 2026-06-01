# Task 4 — Runtime world load + tile-grid rendering (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 4. It is self-contained — you do not need
> the planning conversation. When you finish, you verify (build + headless check + a hands-on playtest),
> commit, and bring results back to session A for review. One such doc lives in `docs/tasks/` per task.

> **What this task is.** Make the baked world (Task 3's `res://world/world_8km.res`) **visible and solid at
> runtime.** Load the `WorldData`, render its building descriptors as **GPU-instanced `MultiMesh` batches** on
> a fixed **world-tile grid**, give the tiles **near the player real colliders** (so the car bounces + takes
> damage on impact), and **cull far tiles into the fog** with `VisibilityRange`. Then **harvest the building
> mesh factories out of `CityChunk.cs` and delete it.** This is the task that joins Task 2 (the flyer) and
> Task 3 (the data) into an actual city you fly through.

> **What this task is NOT.** **No terrain mesh, no ocean/water, no biome colours on screen** — those are
> Task 5 (the heightmap/biome *data* exists in the bake but stays unrendered here; the city will appear to
> float above a flat sea-level plane until Task 5 — that is expected, see §0). **No glTF** (Task 8 — but you
> build the `MeshRegistry` *seam* now). **No ambient traffic** (Task 7). **No re-bake** — you only *read*
> `world_8km.res`; if you find yourself wanting to change it, stop (that's a Task-3 concern). **Do not retune
> the dusk environment** (Task 6) or touch the flying car / camera / HUD / damage (Task 2).

> **Scope discipline.** Implement Task 4 only. Create the runtime loader/tiles/registry under
> `scripts/world/`, wire `Game.cs` to spawn the loader, trim the Task-2 test field, and delete `CityChunk`.
> Leave `Traffic.cs`, `Ship.cs`, `CameraRig.cs`, `Hud.cs`, `DamageComponent.cs`, `ScreenFX.cs`, and the Task-3
> world-data files unchanged. **Do not edit `settings/settings.cfg`** for this task (the tile/LOD/collider
> tunables live as `[Export]`s on the loader — see §3.4; no new config keys). **Do not edit `CLAUDE.md` or
> `docs/` prose** (session A owns docs).

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (read **`docs/GAME_SPEC.md` §7.1, §7.3,
§7.7** and **`CLAUDE.md`** before starting). The world is **fixed, finite (≈8×8 km), baked once to disk** as
layout *data* (heightmap + biome map + object descriptors), then **loaded at runtime**. **No streaming, no
per-run regeneration, no object pool ring.** Repeated geometry → **`MultiMeshInstance3D`** (GPU instancing),
one batch per `ObjectType` per fixed **world tile** (≈250 m). Cull/LOD with `VisibilityRange`; **fog hides the
cull boundary.** Instantiate **colliders only near the car**, not for all 8 km at once.

Tasks 1–3 are done: the arcade loops are stripped, the car is a `RigidBody3D` 6-DOF flyer (Task 2), and the
world is baked to `res://world/world_8km.res` (Task 3 — 315 buildings on a city plateau, a heightmap, a biome
map). **Right now the game boots an empty void + a flat test field.** Task 4 replaces the test obstacles with
the real, baked city.

### 0.1 What you read/harvest, and what you delete

- **`scripts/CityChunk.cs`** — the **harvest source for the meshes + the MultiMesh upload**, then **deleted**
  at the end of this task. You **copy** (not reference) the pieces below into the new files, then remove
  `CityChunk.cs` + `CityChunk.cs.uid` + `scenes/world/CityChunk.tscn` (§3.6). Key pieces (current line
  numbers):
  - **`GetBuildingMeshes(bool windows, float worldScale)` — lines 1042–1098.** Builds **one UNIT mesh per
    silhouette** (`BoxMesh` size 1; `CylinderMesh` round = r0.5/20-seg; prism = r0.5/6-seg; taper =
    top0.28/bot0.5/20-seg; shard = top0.05/bot0.5/4-seg; `SphereMesh` r0.5/18×9) sharing one
    `ShaderMaterial` (`building.gdshader`) set on each mesh's `.Material`, with uniforms `windows_on` and
    `window_scale`. **The index order Box,Round,Prism,Taper,Shard,Sphere is load-bearing — it matches
    `ObjectType` 0..5.** → harvest into `MeshRegistry` (§3.1).
  - **The MultiMesh build + upload — lines 178–192 (the per-bucket upload) and 897–923 (`EnsureMultimeshes`).**
    The pattern you must reproduce exactly: `new MultiMesh { TransformFormat = Transform3D, UseColors = true,
    UseCustomData = true, Mesh = … }`, then set `InstanceCount`, then per-instance `SetInstanceTransform` /
    `SetInstanceColor` / `SetInstanceCustomData`. **`UseColors` and `UseCustomData` MUST be set BEFORE
    `InstanceCount`** (line 908–909 comment) or the per-instance colour/custom channels silently don't
    allocate. → harvest into `TileBuilder` (§3.3).
  - **`AddInstance` channel packing — lines 632–638.** Confirms `COLOR = (white.rgb, litFraction)` and
    `INSTANCE_CUSTOM = (variation, gridClass, accentHue, accentAmount)`. **Task 3 already baked these into
    `PlacedObject.Tint` and `PlacedObject.Custom` in that exact layout**, so the loader just does
    `SetInstanceColor(i, o.Tint)` / `SetInstanceCustomData(i, o.Custom)` — no recompute.
  - You do **NOT** harvest the rooftop/obstacle factories (`GetAntennaMesh`/`GetBeaconMesh`/`GetSpireMesh`/
    `GetDomeMesh`/`GetPyramidMesh`/`GetHexCapMesh`/`GetPadMesh`/`GetCarMesh`/`GetObstacleMesh` + their mats).
    Those serve the deferred rooftop dressing — they die with `CityChunk` and are recoverable from git history
    for the later enrichment task. Harvest **only the 6 building meshes + their shared material.**
- **`shaders/building.gdshader`** — **unchanged.** It lays windows out in **world space** and reads
  `COLOR` + `INSTANCE_CUSTOM` per instance. **It derives each building's window-pattern seed from the
  instance's WORLD origin** (`vec3 origin = (MODEL_MATRIX * vec4(0,0,0,1)).xyz;`, lines 87–88). **This dictates
  how you position tiles** — see §4. `window_scale` keeps panes proportional: the old corridor used
  `window_scale = WorldScale (4.0)` because it enlarged towers; **the bake stores REAL-metre sizes (it does
  NOT apply the old `[game] scale`), so set `window_scale = 1.0`.** `windows_on` follows `[fx]
  building_windows`.
- **`scripts/world/{WorldData,PlacedObject,ObjectType,Biome}.cs`** (Task 3) — the data you consume. Recap:
  - `WorldData`: `WorldExtent` (8000), `HeightmapResolution`/`MinY`/`MaxY`/`Heights`/`Biomes` (Task 5's, ignore
    here), and **`Godot.Collections.Array<PlacedObject> Objects`** (what you render).
  - `PlacedObject`: `ObjectType Type` (0..5); **`Transform3D Xform`** — `Basis` carries footprint/height as
    non-uniform scale `(fx, height, fz)`, `Origin` is the building's **world centre** (`baseY + height*0.5`);
    `Tint` (→ `COLOR`); `Custom` (→ `INSTANCE_CUSTOM`); `Flags`/`SubSeed` (unused here).
  - Every building mesh is a **UNIT mesh** (fits 1×1×1); the `Xform` basis scale sets the real size.
- **`scripts/Game.cs`** — the orchestrator (`Node3D`, attached to `Main.tscn`). It self-assembles the scene
  in `_Ready`: `EnsureEnvironment()` (Sun + `WorldEnvironment` + a wet **`RefGround`** plane at Y=0 that
  follows the ship), `SpawnShipAndCamera()` (creates `_ship` at `(0,30,0)`, `_rig`), **`SpawnTestField()`**
  (the floor + 6 obstacle boxes you will trim), `SpawnUi()`, `SpawnScreenFx()`. It holds `_ship` and pushes
  config onto it. You add a `SpawnWorld()` and trim `SpawnTestField()` (§3.5).
- **`autoload/Config.cs`** — `Config.Instance.GetBool/GetInt/GetFloat(section, key, fallback)`, null-safe,
  available at **runtime** (the loader runs in the live game, so the autoload is up — unlike the bake).
- **Physics layers** (`project.godot`): layer 1 = `"ship"`, layer 2 = `"obstacles"`. **`Ship` sets no explicit
  layer/mask → it is on layer 1, mask 1.** The Task-2 test-field `StaticBody3D`s also use the default (layer 1),
  which is why the car bounces off them. **So your building colliders use the default layer (1)** and the car
  hits them with **zero `Ship` changes**; the existing `_IntegrateForces` impulse→`DamageComponent` path then
  fires automatically (it sums contact impulse regardless of what was hit).

### 0.2 The expected look after Task 4 (so the playtest isn't a surprise)

- The **315 plain single-silhouette towers** (Box/Round/Prism/Taper/Shard — the flourishes were deferred in
  Task 3) render with lit neon windows, on the **city plateau centred near world `(-1400, -200)`** (so from the
  `(0,30,0)` spawn the city is ~1.4 km toward **−X**; §3.5 repositions the spawn over it for convenience).
- There is **no terrain and no water yet** (Task 5). The buildings sit at their baked plateau height
  (Y≈38 m), so they **appear to float above the flat dark `RefGround`/sea-level floor plane**. **This is the
  expected Task-4 artifact** — Task 5 raises terrain to meet them. Don't try to "fix" it here.
- Ramming a tower **bounces the car and drops CONDITION** (existing HUD bar) and **never ends the game**.
- Fly far from the city and the far tiles **fade/cull into the fog**; fly back and they return — **identical
  every launch** (no runtime RNG).

---

## 1. Definition of Done

- `./run.sh build` → **0 errors, 0 warnings**.
- `./run.sh build && ./run.sh check` → the headless 180-frame boot still exits clean. **`check` now actually
  loads `world_8km.res` and builds the tiles/MultiMeshes headless** (a real integration smoke test); it must
  print the loader's summary line and exit 0.
- `./run.sh play` → the **full baked city renders** as `MultiMesh` batches; it is **identical every launch**;
  **buildings are solid** (the car bounces + CONDITION drops on impact, **never game-over**); **far tiles cull
  into the fog** and reappear as you approach; **FPS holds** (GPU-verify — see §5).
- `CityChunk.cs` (+ `.uid`) and `scenes/world/CityChunk.tscn` are **deleted**; the project builds and boots
  without them; nothing references `CityChunk` anymore.
- New runtime code lives under **`scripts/world/`** (`MeshRegistry`, `TileBuilder`, `WorldTile`, `WorldLoader`).

**This task is "done" when you can fly through the real, identical, solid city and bump its towers — terrain
and water are explicitly NOT part of it.**

---

## 2. Preconditions

- **Branch:** the reframe is on `dev5` (`git branch --show-current`).
- Green baseline: `./run.sh build && ./run.sh check && ./run.sh bake` all pass (Task 3 committed at `8e54e9e`;
  `world/world_8km.res` is present and committed).
- Read: `scripts/CityChunk.cs` (harvest), `shaders/building.gdshader` (unchanged contract), `scripts/Game.cs`
  (wiring), the Task-3 `scripts/world/*` data files, `autoload/Config.cs`, and `docs/GAME_SPEC.md` §7.1/§7.3/§7.7.

---

## 3. Work items

Create everything under **`scripts/world/`** (file name == class name). The four new classes form a clean
chain: **`MeshRegistry`** (type→mesh) → **`TileBuilder`** (objects→batches+colliders) → **`WorldTile`** (the
cull/collider unit) → **`WorldLoader`** (load + bucket + lazy colliders). Then wire `Game.cs` and delete
`CityChunk`.

### 3.1 `scripts/world/MeshRegistry.cs` — the `ObjectType → Mesh` seam (harvested meshes)

The **glTF swap seam** (Task 8 swaps an entry for an imported `.glb`, no other change, no re-bake). Today it
returns the procedural unit meshes harvested from `CityChunk.GetBuildingMeshes()`. Cached statically (shared
game-wide, immutable). Mesh index order **must** match `ObjectType` 0..5.

```csharp
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
```

### 3.2 `scripts/world/WorldTile.cs` — one cell of the fixed grid (the cull + collider unit)

A `Node3D` positioned at its **tile centre** (critical — see §4). It owns the `PlacedObject`s whose centres
fall in it, builds its render batches **once** (eager; cheap, and `VisibilityRange` culls far tiles), and
gains/loses colliders lazily as the player nears/leaves. **The world is static → a tile never rebuilds; only
its colliders come and go.**

```csharp
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
```

### 3.3 `scripts/world/TileBuilder.cs` — objects → MultiMesh batches + box colliders (harvested upload)

Static helpers. **`BuildMultiMeshes`** reproduces CityChunk's MultiMesh-per-silhouette upload exactly (note the
`UseColors`/`UseCustomData`-before-`InstanceCount` ordering). **Transforms are made TILE-LOCAL** (`Origin -
tileCenter`) so the parent tile node sits at the tile centre while `MODEL_MATRIX` still resolves to the true
world position the shader needs (§4). **`BuildColliders`** makes one box `StaticBody3D` per building.

```csharp
using Godot;
using System.Collections.Generic;

// Builds a tile's render batches + colliders from its bucketed PlacedObjects. The MultiMesh upload is
// harvested from CityChunk (UseColors/UseCustomData set BEFORE InstanceCount; per-instance transform/color/
// custom). Instance transforms are TILE-LOCAL (origin - tileCenter) so the tile node can sit at the tile
// centre for per-tile VisibilityRange culling while MODEL_MATRIX still yields the true WORLD position the
// building shader's world-space window grid needs (TASK04 §4).
public static class TileBuilder
{
    public static List<MultiMeshInstance3D> BuildMultiMeshes(List<PlacedObject> objects, Vector3 tileCenter, float viewEnd, float fadeMargin)
    {
        // Bucket by ObjectType -> one MultiMesh per type present in this tile.
        var byType = new Dictionary<int, List<PlacedObject>>();
        foreach (PlacedObject o in objects)
        {
            int t = (int)o.Type;
            if (!byType.TryGetValue(t, out List<PlacedObject> list)) { list = new(); byType[t] = list; }
            list.Add(o);
        }

        var result = new List<MultiMeshInstance3D>();
        foreach (KeyValuePair<int, List<PlacedObject>> kv in byType)
        {
            List<PlacedObject> list = kv.Value;
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,           // MUST precede InstanceCount; feeds the shader COLOR
                UseCustomData = true,       // ...and INSTANCE_CUSTOM (the window lighting profile)
                Mesh = MeshRegistry.GetMesh((ObjectType)kv.Key),
            };
            mm.InstanceCount = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                PlacedObject o = list[i];
                mm.SetInstanceTransform(i, new Transform3D(o.Xform.Basis, o.Xform.Origin - tileCenter));
                mm.SetInstanceColor(i, o.Tint);            // rgb = white window base, a = lit fraction
                mm.SetInstanceCustomData(i, o.Custom);     // variation, grid class, accent hue, accent amount
            }
            result.Add(new MultiMeshInstance3D
            {
                Name = $"Batch_{(ObjectType)kv.Key}",
                Multimesh = mm,
                VisibilityRangeEnd = viewEnd,
                VisibilityRangeEndMargin = fadeMargin,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,  // dithered fade into fog
            });
        }
        return result;
    }

    // One axis-aligned box StaticBody3D per building. A box approximates round/taper/shard (over-covers; fine
    // for arcade bounce). DEFAULT physics layer (1, "ship") — exactly like the Task-2 test field — so the car
    // (mask 1) bounces and the existing impulse->damage path fires with NO Ship change. Added to the "building"
    // group for future contact attribution. Transforms are tile-local (the tile node is at tileCenter). The
    // box SIZE comes from the basis scale; the body carries only the yaw rotation (no scaled collision shape).
    public static List<StaticBody3D> BuildColliders(List<PlacedObject> objects, Vector3 tileCenter, PhysicsMaterial mat)
    {
        var bodies = new List<StaticBody3D>(objects.Count);
        foreach (PlacedObject o in objects)
        {
            var body = new StaticBody3D { PhysicsMaterialOverride = mat };
            body.AddToGroup("building");
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = o.Xform.Basis.Scale } });
            body.Transform = new Transform3D(o.Xform.Basis.Orthonormalized(), o.Xform.Origin - tileCenter);
            bodies.Add(body);
        }
        return bodies;
    }
}
```

> `Basis.Scale` returns the `(fx, height, fz)` axis lengths; `Basis.Orthonormalized()` strips the scale,
> leaving the yaw rotation — so the collision shape is sized explicitly and the body transform is unscaled
> (a scaled collision shape is best avoided). The box is centred on `Xform.Origin` (= the building centre),
> matching the box mesh exactly and circumscribing the cylinders/sphere.

### 3.4 `scripts/world/WorldLoader.cs` — load, bucket, render, lazy colliders (the orchestrator)

`[GlobalClass] Node3D`, spawned by `Game.cs`. Loads `WorldData`, buckets `Objects` into ~`TileSize` tiles
**once**, eagerly builds every non-empty tile's batches, and — driven by the player's position — keeps only
the tiles within `ColliderTileRadius` collidable. Tunables are `[Export]`ed (no new `settings.cfg` keys this
task).

```csharp
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
```

> **`UpdateColliders` intent:** *the populated tiles within `ColliderTileRadius` of the player's tile have
> colliders; all others don't* — add `desired \ active`, drop `active \ desired`. `HashSet`/`Dictionary`
> iteration here is **runtime gameplay, not the bake — determinism is unaffected** (the render order came from
> the baked array; colliders don't affect visuals). **Why radius 2:** at the 1000 m/s top speed and 160 Hz
> physics, the player crosses a
> 250 m tile in ~40 frames; radius 2 keeps colliders ≥ ~500 m (≈80 frames) ahead in every direction, so a tower
> is never un-collidable when you reach it. Drop to 1 only if you confirm no tunneling.

### 3.5 `scripts/Game.cs` — spawn the loader, trim the test field

Two edits, no behavioural change to flight/camera/HUD/juice:

1. **Add `SpawnWorld()`** and call it in `_Ready()` **after `SpawnShipAndCamera()`** (it needs `_ship`):

   ```csharp
   private WorldLoader _world;

   // Loads + renders the baked 8 km city and drives its near-player colliders. Replaces the Task-2 test
   // obstacles; the flat sea-level floor stays as a stand-in until Task 5 adds terrain + ocean.
   private void SpawnWorld()
   {
       if (GetNodeOrNull("WorldLoader") != null) return;
       _world = new WorldLoader { Name = "WorldLoader" };
       AddChild(_world);              // _Ready() loads world_8km.res + builds the tiles
       _world.Player = _ship;         // drives the lazy colliders

       // Convenience: start the run above the city so the playtest opens looking at it (the plateau is centred
       // ~(-1400,-200), well off the (0,30,0) spawn). Harmless if the load failed (CityCenter stays origin).
       if (_world.CityCenter != Vector3.Zero)
           _ship.GlobalPosition = _world.CityCenter + new Vector3(0.0f, 160.0f, 700.0f);
   }
   ```

   Call order in `_Ready()`: `EnsureEnvironment()` → `SpawnShipAndCamera()` → **`SpawnWorld()`** →
   `SpawnTestField()` → `SpawnUi()` → `SpawnScreenFx()`.

2. **Trim `SpawnTestField()`** to keep **only the flat `Floor`** (the temporary sea-level ground, Y=0 top
   face) and **delete the six emissive obstacle boxes** (the real city supersedes them). Keep the `Floor`'s
   bounce `PhysicsMaterial`. The `SpawnTestObstacles` `[Export]` now gates nothing useful — either keep the
   `Floor` unconditional and delete the export, or repurpose the export to gate the `Floor`; your call, just
   keep it tidy and keep a sea-level floor by default. (Leave `EnsureEnvironment`/`RefGround` exactly as-is —
   the wet plane is the visible sea-level reference until Task 5.)

> Do **not** wire any of the loader's tunables through `settings.cfg` this task — they're `[Export]` defaults
> on `WorldLoader`. (A `[world]` runtime sub-section is a fine *later* addition, but the file is live-edited by
> the user; leave it untouched here.)

### 3.6 Harvest-then-delete `CityChunk`

Once `MeshRegistry` + `TileBuilder` compile and the city renders, **delete the corridor leftovers**:

```sh
git rm scripts/CityChunk.cs scripts/CityChunk.cs.uid scenes/world/CityChunk.tscn
rmdir scenes/world 2>/dev/null || true     # remove the dir if now empty
```

Then **confirm nothing references it** and the project still builds/boots:

```sh
grep -rIn --exclude-dir=.godot --exclude-dir=build --exclude-dir=obj --exclude-dir=.git CityChunk . | grep -v docs/
./run.sh build && ./run.sh check
```

The only remaining `CityChunk` mentions should be in **`docs/`** prose (session A's to clean — leave them).
`Traffic.cs` does **not** reference `CityChunk` (it has its own corridor exports), so the build stays green.
The deferred rooftop/obstacle mesh factories die with `CityChunk` — that's fine, they're in git history for the
later enrichment task.

---

## 4. The tile-positioning rule (get this right — it's the one subtle correctness point)

The building shader (`building.gdshader`) derives **each tower's window-pattern seed from the instance's WORLD
origin** (`origin = (MODEL_MATRIX * vec4(0,0,0,1)).xyz`), and lays its window grid out in **world space**. And
`VisibilityRange` must measure distance **per tile** (not from the world centre) or every tile culls together.
Both are satisfied by one decision:

- **Each `WorldTile` node sits at its tile centre** (`Position = TileCenter`), and
- **each instance transform is tile-local**: `new Transform3D(o.Xform.Basis, o.Xform.Origin - tileCenter)`.

Then for any instance, `MODEL_MATRIX = tile.GlobalTransform × instanceLocal = translate(tileCenter) ×
(basis, worldOrigin − tileCenter)`, whose origin is **exactly `worldOrigin`** and whose vertex mapping is the
true world placement — so the shader's world-space windows are unchanged from the old corridor — **while** the
tile node's own origin is the tile centre, so `VisibilityRange` culls each tile by its real distance (robust
whether the engine measures from the node origin or the AABB centre — both ≈ the tile centre). **Do not** park
the tiles at world origin with absolute instance transforms: that breaks per-tile culling.

Everything else is deterministic by construction — there is **no runtime RNG**; the render order is the baked
`Objects` array order, so the city is byte-for-byte identical every launch.

---

## 5. Verify

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → boots headless, exits 0, and prints the `:: WorldLoader: N objects
   across M populated tiles` line (the loader ran headless — MultiMesh build + resource load are exercised).
3. `./run.sh play` (**GPU — eyeball these; headless can't render**; the project has a `memory` note that city
   visuals must be verified in `play`):
   - The **city renders** ahead of the opening view (the spawn is repositioned over it): lit neon-window
     towers in varied silhouettes, floating above the flat dark ground plane (the expected no-terrain-yet
     look).
   - **Identical every launch:** quit and re-run — same skyline, same window layouts.
   - **Solid + bounce + damage, never game-over:** fly into a tower at speed — the car **bounces**, the
     **CONDITION bar drops** (and the impact flash/shake/thud fire), and the **session never ends**; rest the
     car against a wall and it doesn't fall through.
   - **Cull into fog:** fly several km away — the far towers **fade/cull**; fly back — they **return**. The
     boundary should sit inside the fog (tune `ViewDistance`/`ViewFadeMargin` vs `[fx] fog_density` if the pop
     is visible).
   - **FPS holds** at a steady cruise through the city (the whole 315-tower city is only a few hundred
     `MultiMesh` instances across ~30 tiles; if it ever hitches at higher density, the levers are
     `ViewDistance`, per-tile shadow-casting, and lazy-building visuals on first visibility — note any you
     touch).
4. **No-game-over invariant:** ram towers repeatedly at full speed — CONDITION can hit 0%, the car keeps
   flying.
5. `CityChunk` gone: `grep -rIn CityChunk` (outside `docs/`) is empty; build + check still green.

---

## 6. Report back to session A (for review)

Include:
- `git status` + `git diff --stat` and the new-file list (`scripts/world/{MeshRegistry,TileBuilder,WorldTile,
  WorldLoader}.cs` + `.uid`s), the `Game.cs` edits, and the **deletions** (`CityChunk.cs`/`.uid`,
  `CityChunk.tscn`).
- `./run.sh build` + `./run.sh check` output (including the loader's summary line).
- A description of the **`play` GPU playtest** against §5 (you can't screenshot headless — describe what you
  saw: city renders, identical relaunch, bounce+damage on a tower, far-tile cull, rough FPS).
- **Confirmation `CityChunk` is deleted** and nothing references it; that `Traffic.cs`/`Ship.cs`/the data files
  were left untouched.
- **Any deviations** from this brief and why; **anything deferred** (rooftop dressing + composite forms remain
  out — confirm; terrain/water/biome colour remain out — confirm).
- **Commit** once build + check + the playtest are green. Stage **by path** (never `git add -A`); do **not**
  stage `settings/settings.cfg` (untouched). **Leave `docs/GAME_SPEC.md` §9 Task 4 unticked** — session A ticks
  it on review. Suggested message:
  `Task 4: runtime world load + tile-grid MultiMesh rendering + lazy colliders; delete CityChunk`.
  End the commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **Terrain mesh, ocean/water shader, biome vertex colours on screen** → **Task 5.** (The heightmap/biome data
  is in the bake; you render only `Objects`. The city floating above the flat plane is expected until Task 5.)
- **Rooftop dressing + composite building forms** (ziggurat, sphere-stack, skybridge, arch, antenna, beacon,
  spire, dome, pyramid, hex-cap, landing pad, parked car) — a later enrichment; render single silhouettes only.
- **glTF model loading** → Task 8 (you build the `MeshRegistry` *seam* now, but it returns procedural meshes).
- **Dusk aesthetic retune** (`BuildEnvironment`/`BuildSky`/`sky.gdshader`) → Task 6.
- **Ambient traffic** (`Traffic.cs`) → Task 7 — leave it untouched (it does not reference `CityChunk`).
- **Re-baking or editing `world_8km.res` / `WorldGenerator` / any Task-3 data file** — read-only here.
- **World borders / soft edge force on the car**, terrain colliders, a single Jolt-scaled `HeightMapShape3D` —
  later tasks. (Task-4 colliders are per-building boxes near the player only.)
- **Editing `settings/settings.cfg`, `CLAUDE.md`, or `docs/` prose**, or changing the flying car / camera /
  HUD / damage / screen-FX (Task 2 behaviour).
