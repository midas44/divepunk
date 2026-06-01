# Task 3 — World data + bake pipeline (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 3. It is self-contained — you do not
> need the planning conversation. When you finish, you verify headless, commit, and bring results back to
> session A for review. One such doc lives in `docs/tasks/` per reframing task.

> **What this task is.** Produce the **baked world as DATA on disk** — `res://world/world_8km.res` — plus
> the tooling to (re)bake it. This is the **riskiest task, but it is PURE DATA: no rendering, no gameplay
> change.** After Task 3 the game still boots the same empty void you fly today; the *only* new player-
> visible thing is a file on disk and a `./run.sh bake` command. Task 4 makes the data visible.

> **The bar is DETERMINISM, not beauty.** The DoD is: the bake writes the file, **the same seed produces a
> byte-identical file every time**, and a validator prints sane counts. The world does not need to *look*
> good yet — it isn't rendered until Task 4, and the terrain/city aesthetic is tuned in Task 5 once you can
> see it. Spend your care on the **data schema** (everything downstream loads it) and on **reproducibility**.

> **Scope discipline.** Implement **Task 3 only.** Do **not** build the runtime loader, tiles, MeshRegistry,
> terrain mesh, colliders, water, or any `Game.cs`/`Main.tscn` wiring (all Task 4/5). Do **not** edit or
> delete `CityChunk.cs`, `Traffic.cs`, `Ship.cs`, `CameraRig.cs`, `Hud.cs`, or the gameplay scenes. You
> **read** `CityChunk.cs` to harvest its generation logic into the new `WorldGenerator`, but you leave it in
> place (its deletion is Task 4's job). If something seems to need a later task, stop and note it in your
> report instead of pulling it forward.

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (read **`docs/GAME_SPEC.md` §4, §7.1,
§7.3, §7.7** and **`CLAUDE.md`** before starting). The world is **fixed, finite (≈8×8 km), and baked once
to disk** as *layout data* (a heightmap + biome map + object descriptors), then loaded at runtime. **No
streaming, no per-run regeneration.** Object descriptors store **type + transform + shader channels**, never
meshes — so visuals can later swap to glTF via a `MeshRegistry` with **zero re-bake**.

Tasks 1–2 are done: the old arcade loops are stripped and the car is a `RigidBody3D` 6-DOF flyer in an
empty void. **Task 3 adds the world's *data* and the bake pipeline** — it does not touch the runtime.

**What already exists that you will read or rely on:**
- `scripts/CityChunk.cs` — the **harvest source.** It deterministically generates the old corridor's
  buildings: size classes, silhouettes, footprints, heights, and **per-building window lighting profiles**.
  You will **copy and adapt** its generation helpers into the new `WorldGenerator` (see §3.2). **Do not edit
  or delete it.** Key pieces you'll reuse (by line, in the current file):
  - `MixSeed(long baseSeed, long idx, long salt)` — lines **1018–1028**. The deterministic seed mix
    (`long` + `unchecked`). **Copy verbatim** into `WorldGenerator` (see §4).
  - `RoundHalfAway(float x)` — lines **1032–1035**. Half-away-from-zero rounding for RNG-consuming loop
    counts. **Copy verbatim.**
  - `enum Silhouette { Box, Round, Prism, Taper, Shard, Sphere }` — line 33. The 6 building shapes; **the
    index order is load-bearing** (it matches `GetBuildingMeshes()`). Your `ObjectType` mirrors it (§3.1).
  - `PickClass` (339–355), `ClassHeight`/`ClassFootprint` (357–371), `PickShape` (373–389), the building
    class/shape weight + height/footprint `[Export]` ranges (lines 63–84) — the building variety.
  - `WindowProfile` struct (417–425), `PickWindowProfile` (448–500), `PickAccentHue` (503–507), the window
    colour consts `WinColdWhite`…`AccentHues` (394–409), and `AddInstance`'s channel packing (632–638) —
    the **per-building shader channels** (see the contract below).
- `shaders/building.gdshader` — **the building look, UNCHANGED in the reframe.** It lays windows out in
  **world space** and reads its per-building character from the MultiMesh per-instance channels. **This is
  the contract your `PlacedObject` must honour so Task 4 renders correctly with no shader change:**
  - `COLOR.rgb` = the building's **white window base** (cold/warm/neutral white).
  - `COLOR.a`   = **lit fraction** (how many windows are on; kept low).
  - `INSTANCE_CUSTOM` = `(variation, grid_class, accent_hue, accent_amount)`.
  → So a building `PlacedObject` stores `Tint = Color(whiteR, whiteG, whiteB, litFraction)` and
  `Custom = Color(variation, gridClass, accentHue, accentAmount)`. Task 4 pushes these straight into
  `MultiMesh.SetInstanceColor(i, Tint)` / `SetInstanceCustomData(i, Custom)`.
  - **Unit-mesh convention:** every building mesh fits a **1×1×1 box** (`GetBuildingMeshes()`, lines
    1042–1098). The **per-instance `Transform3D` basis scale `(fx, height, fz)` sets the real dimensions**,
    origin = world position of the building's centre. So `PlacedObject.Xform.Basis` carries
    footprint/height as non-uniform scale; `Xform.Origin` is where it sits.
- `autoload/Config.cs` — `Config.Instance.GetInt/GetFloat/GetBool(section, key, fallback)`, null-safe, reads
  `settings/settings.cfg`. **Note:** the autoload only exists when the game is *running*. For the bake (which
  can run in editor-script or headless-scene contexts), read settings with a **fresh `ConfigFile`** instead
  of relying on the autoload (see §3.3).
- `run.sh` — the runner. You add a `bake` command (§3.4). It already auto-detects `godot-mono` and boots
  scenes headless (see the `check` case for the exact pattern).

**Conventions (from `CLAUDE.md`):** C# typed throughout; `public partial class X : Base`; **`[GlobalClass]`
on every `Resource`/node type referenced by the bake, scenes, or Inspector**; `[Tool]` on editor-run types;
`PascalCase` members / `_camelCase` private fields; expose tunables with `[Export]` grouped by
`[ExportGroup]`; `settings.cfg` keys stay `snake_case`; cast `delta`/use `Mathf.*` (float) over
`System.Math.*`; float literals need `f`.

---

## 1. Definition of Done

- `./run.sh build` → **0 errors, 0 warnings**. `./run.sh build && ./run.sh check` still boots the void
  headless and exits clean (Task 3 added files but **did not wire anything into the running game** — the
  void is unchanged).
- `./run.sh build && ./run.sh bake` writes **`res://world/world_8km.res`** and prints a **validation
  summary** (object total + per-type histogram, heightmap resolution + min/max elevation, biome histogram,
  file size).
- **Determinism:** baking **twice with the same seed produces a byte-identical file** (`cmp`/`sha256sum`
  match). This is the gate — verify it (§5).
- `./run.sh play` is **visually unchanged** (still the void + test field). Task 3 adds no rendering.
- The new code lives under **`scripts/world/`**; the bake tooling is runnable both **in-editor** (an
  `EditorScript`) and **headless** (`./run.sh bake`).

**This task is "done" when the file bakes deterministically and the validator reports sane numbers — not
when the world looks good (it isn't drawn yet).**

---

## 2. Preconditions

- **Branch:** the reframe is on `dev5` (confirm with `git branch --show-current`).
- Green baseline: `./run.sh build && ./run.sh check` passes (Task 2 is committed: `ce7bc7d`).
- `res://world/` and `res://scripts/world/` **do not exist yet** — you create them.
- Read: `scripts/CityChunk.cs` (the harvest source), `shaders/building.gdshader` (the channel contract),
  `autoload/Config.cs`, `run.sh`, and `docs/GAME_SPEC.md` §7.1/§7.3/§7.7.

---

## 3. Work items

Create everything under **`scripts/world/`** (new folder). File name == class name.

### 3.1 The data schema (3 files) — *get this exactly right; everything downstream loads it*

> **Critical C# Godot rule:** a `Resource` only serialises its **`[Export]`** properties. A plain
> `public` field/property **is not saved**. Every persisted field below **must** be `[Export]`. Use Godot
> packed types (`float[]`→PackedFloat32Array, `byte[]`→PackedByteArray) and `Godot.Collections.Array<T>` so
> the data serialises compactly and deterministically.

**`scripts/world/ObjectType.cs`** — the type taxonomy (persisted as `int`; **append-only**, never reorder).

```csharp
using Godot;

// What a PlacedObject is. Persisted as an int in the bake, so values are STABLE: APPEND new types, never
// reorder or insert (or bump WorldData.FormatVersion). Values 0..5 mirror CityChunk's `Silhouette` enum and
// GetBuildingMeshes() index order, so the Task-4 MeshRegistry can reuse those mesh factories 1:1. All six
// building types share shaders/building.gdshader and are UNIT meshes (fit 1×1×1) — the PlacedObject.Xform
// basis scale sets the real footprint/height.
[GlobalClass]
public enum ObjectType
{
    BuildingBox    = 0,
    BuildingRound  = 1,
    BuildingPrism  = 2,
    BuildingTaper  = 3,
    BuildingShard  = 4,
    BuildingSphere = 5,
    // Rooftop dressing + props (Antenna, Beacon, Spire, Dome, Pyramid, HexCap, LandingPad, ParkedCar) and
    // glTF set-pieces are a LATER enrichment — append here when added.
}
```

**`scripts/world/PlacedObject.cs`** — one placed thing (type + transform + shader channels).

```csharp
using Godot;

// One object placed in the baked world: its type, where/how big it is, and the per-instance shader channels
// that drive shaders/building.gdshader VERBATIM (so visuals swap to glTF later with zero re-bake). The bake
// stores DESCRIPTORS, never meshes — runtime instantiates the mesh from Type via the MeshRegistry (Task 4).
[GlobalClass]
public partial class PlacedObject : Resource
{
    [Export] public ObjectType Type { get; set; } = ObjectType.BuildingBox;

    // Basis carries footprint/height as non-uniform scale (fx, height, fz); Origin is the world centre.
    [Export] public Transform3D Xform { get; set; } = Transform3D.Identity;

    // → MultiMesh COLOR. rgb = white window base; a = lit fraction. (building.gdshader, lines 8–9.)
    [Export] public Color Tint { get; set; } = new Color(0.9f, 0.93f, 0.97f, 0.16f);

    // → INSTANCE_CUSTOM = (variation, grid_class, accent_hue, accent_amount). (building.gdshader, line 9.)
    [Export] public Color Custom { get; set; } = new Color(0.3f, 0.5f, 0.5f, 0.05f);

    // Reserved for future use (e.g. collidable/landmark bits, per-object decorrelation). Unused in Task 3.
    [Export] public int Flags { get; set; } = 0;
    [Export] public int SubSeed { get; set; } = 0;
}
```

**`scripts/world/WorldData.cs`** — the whole baked world.

```csharp
using Godot;

// The entire baked world as DATA (spec §7.1): a terrain heightmap, a coarse biome map, and the object
// descriptors. Saved to res://world/world_8km.res by the bake; loaded once at runtime by the Task-4 loader.
// FormatVersion guards stale bakes when the schema changes.
[GlobalClass]
public partial class WorldData : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public int Seed { get; set; } = 0;
    [Export] public float WorldExtent { get; set; } = 8000.0f;   // metres, full width on X and Z (world is centred on origin)

    // Heightmap: an N×N grid (row-major, N = HeightmapResolution) of NORMALISED heights in [0,1]. Real
    // elevation = Mathf.Lerp(MinY, MaxY, Heights[iz*N + ix]). Sea level is world Y = 0 (so MinY < 0 < MaxY).
    [Export] public int HeightmapResolution { get; set; } = 256;
    [Export] public float MinY { get; set; } = -120.0f;
    [Export] public float MaxY { get; set; } = 900.0f;
    [Export] public float[] Heights { get; set; } = System.Array.Empty<float>();

    // Coarse biome map, same N×N grid, one Biome byte per cell (see the Biome enum). Drives terrain colour
    // (Task 5) and where objects are placed (City cells).
    [Export] public byte[] Biomes { get; set; } = System.Array.Empty<byte>();

    // Object descriptors (buildings now; props/dressing later). Insertion order is the bake order, kept
    // deterministic by the generator's fixed iteration (see §4). If load cost ever bites at full density,
    // the spec's fallback is parallel typed arrays behind the same API — NOT a Task-3 concern.
    [Export] public Godot.Collections.Array<PlacedObject> Objects { get; set; } = new();
}
```

**`scripts/world/Biome.cs`** — the biome byte values (also append-only).

```csharp
// Coarse biome of a heightmap cell. Stored as the byte in WorldData.Biomes. Append-only.
public enum Biome : byte
{
    Ocean = 0, Beach = 1, City = 2, Desert = 3, Hills = 4, Mountains = 5,
}
```

### 3.2 `scripts/world/WorldGenerator.cs` — the deterministic generator (the core)

A **pure C#** static class: `static WorldData Build(int seed, float extent)`. No nodes, no rendering, no
file I/O — it returns a `WorldData`. **It must be deterministic** (§4): same `(seed, extent)` → identical
`WorldData` → identical bytes when saved.

It does three things, in this fixed order:

1. **Heightmap + biome map.** Allocate `Heights`/`Biomes` (`N*N`). For each cell, compute a real elevation
   from layered noise + analytic masks, and a biome, then normalise heights into `[0,1]` and record
   `MinY`/`MaxY`. Concrete recipe (tunable consts — pin them; the *look* is tuned in Task 5, this just needs
   to be deterministic and produce ocean / beach / a flat city plateau / desert→hills→mountains):

   ```csharp
   const int N = 256;                       // HeightmapResolution (raise later; bump FormatVersion if you do)
   var fnl = new FastNoiseLite {
       Seed = seed,
       NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
       FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
       FractalOctaves = 5, FractalLacunarity = 2.0f, FractalGain = 0.5f,
       Frequency = 1.0f / 2200.0f,          // ~2 km features
   };
   var coastNz = new FastNoiseLite { Seed = seed + 101, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1.0f / 3000.0f };
   // ... layout consts (all metres, world centred on origin; half = extent/2): coastline, bay, plateau,
   //     mountain ring, islands. SET THEM ALL EXPLICITLY (don't rely on FastNoiseLite defaults).

   float half = extent * 0.5f, cell = extent / N;
   var raw = new float[N * N];              // real elevation before normalising
   var biomes = new byte[N * N];
   float minY = float.MaxValue, maxY = float.MinValue;

   for (int iz = 0; iz < N; iz++)
   for (int ix = 0; ix < N; ix++)
   {
       float x = -half + (ix + 0.5f) * cell;
       float z = -half + (iz + 0.5f) * cell;
       float r = Mathf.Sqrt(x * x + z * z);

       float roll = fnl.GetNoise2D(x, z);                      // [-1,1]
       float ring = Smooth01((r - RING_INNER) / (RING_OUTER - RING_INNER)); // 0 centre .. 1 edge
       float land = LAND_BASE + roll * ROLL_AMP + ring * ring * MOUNT_H;    // rolling + mountain ring

       float plateau = 1.0f - Smooth01((Dist(x, z, CITY_CX, CITY_CZ) - CITY_R * 0.7f) / (CITY_R * 0.3f));
       land = Mathf.Lerp(land, PLATEAU_H, plateau);            // flatten the city plateau

       float coast = COAST_X + COAST_AMP * Mathf.Sin(z * COAST_FREQ) + COAST_NZ * coastNz.GetNoise2D(0.0f, z);
       float landSD = x - coast;                               // >0 land, <0 ocean
       float bay = Smooth01((BAY_R - Dist(x, z, BAY_CX, BAY_CZ)) / BAY_EDGE);  // 1 inside bay
       bool isOcean = landSD < 0.0f || bay > 0.5f;

       float elev = isOcean
           ? -Mathf.Min(OCEAN_DEPTH, BEACH_RISE + Mathf.Max(-landSD, bay * BAY_R) / OCEAN_SLOPE)
           : Mathf.Lerp(0.5f, land, Smooth01(landSD / BEACH_BAND));          // ease land down at the shore

       foreach (var isl in ISLANDS)                            // bumps that poke above the sea
           elev += isl.H * Mathf.Max(0.0f, 1.0f - Dist(x, z, isl.CX, isl.CZ) / isl.R);

       raw[iz * N + ix] = elev;
       minY = Mathf.Min(minY, elev); maxY = Mathf.Max(maxY, elev);
       biomes[iz * N + ix] = (byte)ClassifyBiome(elev, plateau, ring);
   }

   var heights = new float[N * N];
   for (int i = 0; i < raw.Length; i++)
       heights[i] = (maxY > minY) ? (raw[i] - minY) / (maxY - minY) : 0.0f;
   ```

   `ClassifyBiome(elev, plateau, ring)`: `elev < -2 → Ocean`; `elev < 3 → Beach`; `plateau > 0.5 → City`;
   `ring < 0.45 → Desert`; `ring < 0.75 → Hills`; else `Mountains`. `Smooth01(t)=Mathf.SmoothStep(0,1,Clamp01(t))`.

2. **Place the city.** Iterate a **fixed-order** placement grid over the plateau and emit a building per
   filled cell. Determinism comes from (a) a fixed nested loop and (b) a per-cell RNG seeded by `MixSeed`:

   ```csharp
   var objects = new Godot.Collections.Array<PlacedObject>();
   int gx0 = CellIndex(CITY_CX - CITY_R, extent, N_PLACE), gx1 = CellIndex(CITY_CX + CITY_R, extent, N_PLACE);
   // (compute plateau cell bounds on a BLOCK-spaced grid; BLOCK ≈ 110 m)
   for (int gz = gz0; gz <= gz1; gz++)
   for (int gx = gx0; gx <= gx1; gx++)
   {
       float x = ..., z = ...;                                  // cell centre in metres
       if (BiomeAt(biomes, N, extent, x, z) != Biome.City) continue;
       if (IsRoadCell(gx, gz)) continue;                        // simple street grid: skip every Kth line
       long cellId = (long)gz * 100000L + gx;                   // stable, unique per cell
       var rng = new RandomNumberGenerator { Seed = (ulong)MixSeed(seed, cellId, PLACE_SALT) };
       if (rng.Randf() > FILL_CHANCE) continue;

       // PORT these from CityChunk (cite the line ranges in §0): pick class → footprint (base + 2 aspects)
       // → height → yaw → shape → window profile. CONSUME THE RNG IN THE SAME ORDER as CityChunk so the
       // stream stays stable. Cap the footprint to < BLOCK so towers don't badly overlap on the open grid.
       // Sample the terrain height under (x,z) for the base Y (the plateau is ~flat at PLATEAU_H).
       float baseY = SampleHeight(heights, N, extent, minY, maxY, x, z);
       var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(fx, height, fz));
       objects.Add(new PlacedObject {
           Type   = SilhouetteToType(shape),                    // Box→BuildingBox, etc.
           Xform  = new Transform3D(basis, new Vector3(x, baseY + height * 0.5f, z)),
           Tint   = new Color(wp.Color.R, wp.Color.G, wp.Color.B, wp.LitFraction),
           Custom = new Color(wp.Variation, wp.GridClass, wp.AccentHue, wp.AccentAmount),
           SubSeed = (int)cellId,
       });
   }
   ```

   - **Use a flat, ordered loop** — **no `Dictionary`/`HashSet` iteration** may influence object order or RNG
     order (hash order is not deterministic across runs). CityChunk's skybridge `Dictionary` is one of the
     features you are **deferring** (next bullet), so this isn't an issue here — but keep the rule in mind.
   - **Defer the flourishes.** Emit **single-silhouette buildings only** (Box/Round/Prism/Taper/Shard, via
     `PickShape`). **Do NOT** port ziggurats, sphere-stacks, skybridges, arches, or rooftop dressing
     (antenna/beacon/spire/dome/pad/car). Those are a later enrichment; the `ObjectType` enum already
     reserves room. A few hundred to a few thousand plain towers across the plateau is the Task-3 target.

3. **Assemble + return** the `WorldData` (`FormatVersion=1`, `Seed`, `WorldExtent=extent`,
   `HeightmapResolution=N`, `MinY`/`MaxY`, `Heights`, `Biomes`, `Objects`).

Also copy in, **verbatim**, `MixSeed` and `RoundHalfAway` from `CityChunk.cs` (§0), and small pure helpers
(`Smooth01`, `Dist`, `SampleHeight` bilinear, `BiomeAt`, `SilhouetteToType`).

> Keep `WorldGenerator` free of `RandomNumberGenerator` shared state: create a fresh `rng`/`FastNoiseLite`
> with an explicit seed where needed, and never read wall-clock time or `System.Random`. See §4.

### 3.3 Bake runners — in-editor + headless (both call one shared baker)

**`scripts/world/WorldBaker.cs`** — the one code path that builds, saves, and prints the summary.

```csharp
using Godot;

// Bakes the world to disk and prints a validation summary. Shared by the editor tool and the headless
// runner so there is ONE bake path. Saving DATA needs no GPU, so this is CI/headless-safe.
public static class WorldBaker
{
    public const string OutPath = "res://world/world_8km.res";

    public static Error Bake(int seed, float extent, string path = OutPath)
    {
        // Ensure res://world/ exists (the folder is not created automatically).
        if (!DirAccess.DirExistsAbsolute("res://world"))
            DirAccess.MakeDirRecursiveAbsolute("res://world");

        WorldData data = WorldGenerator.Build(seed, extent);
        Error err = ResourceSaver.Save(data, path, ResourceSaver.SaverFlags.Compress);
        PrintSummary(data, path, err);
        return err;
    }

    // The "validator": sane-count readout (object total + per-type histogram, heightmap min/max, biome
    // histogram, file size). Reload from disk with CacheMode.Ignore so it reflects the SAVED bytes.
    public static void PrintSummary(WorldData data, string path, Error saveErr) { /* GD.Print(...) — see §3.5 */ }
}
```

**`scripts/world/WorldBakeTool.cs`** — in-editor convenience (Script editor → File → Run).

```csharp
using Godot;

// In-editor bake: open this script in the Godot Script editor and File → Run (Ctrl+Shift+X). Reads the seed
// + extent from settings.cfg [world] (via a fresh ConfigFile — the Config autoload isn't running in editor).
[Tool]
public partial class WorldBakeTool : EditorScript
{
    public override void _Run()
    {
        var (seed, extent) = BakeSettings.Resolve();
        Error err = WorldBaker.Bake(seed, extent);
        GD.Print(err == Error.Ok ? ":: bake OK" : $":: bake FAILED ({err})");
    }
}
```

**`scripts/world/BakeRunner.cs`** + **`scenes/tools/Bake.tscn`** — the headless CLI path (`./run.sh bake`).

```csharp
using Godot;

// Headless bake entry: ./run.sh bake runs scenes/tools/Bake.tscn, whose root is this node. Bakes in _Ready,
// then quits. (The C# assembly must be built first — run.sh documents `build && bake`.)
public partial class BakeRunner : Node
{
    public override void _Ready()
    {
        var (seed, extent) = BakeSettings.Resolve();
        Error err = WorldBaker.Bake(seed, extent);
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
```

`scenes/tools/Bake.tscn` (minimal — root Node running `BakeRunner`; create the `scenes/tools/` folder):

```
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://scripts/world/BakeRunner.cs" id="1"]
[node name="Bake" type="Node"]
script = ExtResource("1")
```

**`scripts/world/BakeSettings.cs`** — resolves `(seed, extent)` from `settings.cfg [world]` with a **fresh
`ConfigFile`** (works in editor *and* headless, no autoload dependency):

```csharp
using Godot;

public static class BakeSettings
{
    public static (int seed, float extent) Resolve()
    {
        var cfg = new ConfigFile();
        int seed = 1337; float extent = 8000.0f;            // defaults
        if (cfg.Load("res://settings/settings.cfg") == Error.Ok)
        {
            seed   = (int)cfg.GetValue("world", "seed", seed);
            extent = (float)cfg.GetValue("world", "extent", extent);
        }
        return (seed, extent);
    }
}
```

### 3.4 `run.sh` — add a `bake` command

Mirror the `check` case (import so the new scripts/scene register on first run, then boot the bake scene
headless). Add to the `usage()` list and the `case`:

```sh
bake)
    echo ":: importing..."
    "$GODOT_BIN" --headless --path . --import
    echo ":: baking world -> res://world/world_8km.res ..."
    "$GODOT_BIN" --headless --path . res://scenes/tools/Bake.tscn --quit-after 600
    echo ":: bake done"
    ;;
```

(`--quit-after 600` is a backstop; `BakeRunner` calls `GetTree().Quit()` itself. Document that **`./run.sh
build`** must run first, like `check`.) Add a `bake` line to `usage()`.

### 3.5 The validation summary (`WorldBaker.PrintSummary`)

After saving, **reload from disk** (`ResourceLoader.Load<WorldData>(path, cacheMode:
ResourceLoader.CacheMode.Ignore)`) and `GD.Print` a readout so a human (and session A) can sanity-check the
bake. Print at least:

- `seed`, `extent`, `FormatVersion`, save `Error`.
- Heightmap: `resolution`, `MinY`, `MaxY`, and mean height — sanity: `MinY < 0 < MaxY` (there's ocean and
  land), range is plausible (hundreds of metres, not NaN/0).
- **Biome histogram:** count per `Biome` — sanity: `Ocean`, `Beach`, `City`, and at least one of
  `Desert/Hills/Mountains` are all non-zero.
- **Object total + per-`ObjectType` histogram** — sanity: a few hundred to a few thousand buildings, spread
  across the silhouettes.
- The output **file size** (`FileAccess.GetFileAsBytes(path).Length` or `FileAccess.Open(...).GetLength()`).

A loud, readable summary is the deliverable for "validator prints sane counts." (No separate validator
binary needed — this readout, printed by `./run.sh bake`, *is* the validator.)

### 3.6 `settings/settings.cfg` — add a `[world]` section

⚠️ **The user edits this file live.** **Re-read it immediately before editing**, **stage it explicitly**
(`git add settings/settings.cfg` — never `git add -A`), and **never revert a tuned value**. You are only
**adding** a new `[world]` section — do not touch `[flight]`, `[damage]`, `[fx]`, `[camera]`, or any other
existing value.

Add (place it after `[game]`, before `[flight]`):

```ini
[world]

; The baked open-world (spec §7.1). These drive the OFFLINE bake (./run.sh bake → res://world/world_8km.res),
; not per-frame gameplay. Change them, re-bake, and the new world ships in the .res. The same seed always
; bakes a byte-identical file.
;   seed   -> the world's fixed layout seed (any int; the committed world_8km.res is baked from this value).
;   extent -> world width in metres on X and Z (the world is centred on the origin). ~8000 = 8x8 km.
seed=1337
extent=8000.0
```

(If you pick a different canonical `seed`, set it here **and** use it for the committed bake — the file and
this key must agree.)

---

## 4. Determinism playbook (this is the gate — read it)

Same `(seed, extent)` **must** produce a byte-identical `world_8km.res`. The world is baked **once** on this
machine and the `.res` is committed, so cross-platform float identity is *not* required — only **same machine,
same engine, repeatable**. To get that:

- **Seed mix:** use `MixSeed` (copied verbatim — `long` + `unchecked`). C# `int` overflows differently from
  the 64-bit math this relies on; keep it `long`. Salt independent streams (terrain vs placement) with
  distinct salt constants so tuning one never reshuffles the other.
- **Rounding:** any `float`→`int` that feeds a **loop count or RNG-consuming count** goes through
  `RoundHalfAway` (copied verbatim). `Math.Round` is banker's (half-to-even) and will desync the stream.
- **Stable RNG order + count:** consume the `RandomNumberGenerator` in a **fixed order**, the **same number
  of draws** every run. Don't branch the draw *count* on anything non-deterministic. Per-cell `new
  RandomNumberGenerator { Seed = MixSeed(...) }` keeps each cell independent and order-free.
- **No hidden nondeterminism:** **no** `System.Random`, `DateTime`, `Guid`, `Math.random`-equivalent, or
  threading. **No `Dictionary`/`HashSet` iteration** feeding object order or RNG order — iterate ordered
  structures (arrays / `List` / fixed loops). `Godot.Collections.Array.Add` preserves insertion order, so
  the fixed placement loop fixes `Objects` order.
- **Pin every `FastNoiseLite` parameter** explicitly (Seed, NoiseType, FractalType, Octaves, Lacunarity,
  Gain, Frequency). Defaults can change between versions; don't rely on them.
- **Serialisation:** `ResourceSaver.Save(..., SaverFlags.Compress)`. Godot's binary serialiser is
  deterministic for identical data. **If you ever observe Compress producing differing bytes** on your build,
  drop to uncompressed (`SaverFlags`(0)) — byte-identity of the DATA is the requirement; compression is only
  size. Report which you used.

**Verify it (don't trust it):** bake, hash, bake again, compare — see §5.

---

## 5. Verify

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → the void still boots headless and exits clean (Task 3 wired nothing
   into the game).
3. `./run.sh build && ./run.sh bake` → writes `world/world_8km.res` and prints the validation summary;
   eyeball the counts (§3.5) for sanity.
4. **Determinism (the gate):**
   ```sh
   ./run.sh bake && sha256sum world/world_8km.res
   ./run.sh bake && sha256sum world/world_8km.res     # the two hashes MUST be identical
   ```
   (or `cp world/world_8km.res /tmp/a.res; ./run.sh bake; cmp /tmp/a.res world/world_8km.res`.)
5. `./run.sh play` → **unchanged** (void + test field); confirm Task 3 added no rendering/gameplay.

---

## 6. Report back to session A (for review)

Include:
- `git status` + `git diff --stat` and the new-file list (`scripts/world/*.cs` + `.uid`s,
  `scenes/tools/Bake.tscn`, `world/world_8km.res`, the `run.sh` + `settings.cfg` edits).
- `./run.sh build`, `./run.sh check`, and `./run.sh bake` output (the latter's full validation summary).
- **The determinism proof:** the two `sha256sum`s from §5.4 (identical), and whether you used Compress or
  uncompressed.
- The **chosen canonical seed** and the headline counts (buildings total + per-type, biome histogram,
  MinY/MaxY, file size).
- **The settings.cfg change:** the exact `[world]` block you added (and confirmation you touched nothing
  else).
- **Any deviations** from this brief and why; **anything deferred** as out-of-scope (e.g. you should be
  deferring all rooftop dressing + composite building forms — confirm you did).
- **Commit** once build + check + bake + determinism are green. Explicit staging by path (never `git add
  -A`; `settings/settings.cfg` staged by path). **Commit `world/world_8km.res`** alongside the code (it's
  tracked authored content). **Leave `docs/GAME_SPEC.md` §9 unticked** — session A ticks it on review.
  Suggested message:
  `Task 3: world data schema + deterministic bake pipeline (WorldData/PlacedObject/WorldGenerator, run.sh bake)`.
  End the commit body with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **Any runtime rendering or loading** — `WorldLoader`, `TileBuilder`, `MeshRegistry`, tile grid, terrain
  mesh, colliders, visibility/LOD, `Game.cs`/`Main.tscn` wiring → **Task 4**.
- **Terrain mesh + ocean/water shader + biome vertex colours on screen** → Task 5 (Task 3 only stores the
  heightmap/biome *data*).
- **Rooftop dressing + composite building forms** (ziggurat, sphere-stack, skybridge, arch, antenna, beacon,
  spire, dome, pyramid, hex-cap, landing pad, parked car) — a later enrichment; emit single silhouettes now.
- **Dusk aesthetic retune** → Task 6. **Ambient traffic** → Task 7. **glTF seam** → Task 8.
- **Editing or deleting `CityChunk.cs` / `Traffic.cs`** — read-and-harvest only; their removal is Task 4.
- **World borders / soft edge force on the car** — that's a `Ship`/runtime concern for when the world is
  loaded (Task 4); the bake only records `WorldExtent`.
- **Changing the flying car, camera, HUD, damage, or any Task-2 behaviour.**
```
