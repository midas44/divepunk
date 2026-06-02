# Task 8 — glTF model-loading seam (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 8. It is self-contained — you do not need the
> planning conversation. When you finish, you verify (build + headless check + a hands-on **GPU** playtest), commit,
> and bring results back to session A for review. One such doc lives in `docs/tasks/` per task.

> **What this task is.** **Prove the glТF swap seam.** The whole point of the bake storing **object descriptors
> (type + transform + shader channels), not geometry** (spec §7.1, `CLAUDE.md`) is that visuals can upgrade to real
> **glTF 2.0 `.glb`** models **later, with zero re-bake**. Task 8 **demonstrates that end-to-end**: take **one (or two)
> `ObjectType`s** and make them render from an **imported `.glb`** instead of the procedural placeholder — purely by a
> **`MeshRegistry` change** — with the **same `world_8km.res`** and **no re-bake**. The baked per-instance transforms
> must place + size the model correctly (the "transforms line up" gate).

> **What this task is NOT.** **Not a re-bake** — `world_8km.res` / `WorldGenerator` / `WorldData` / the bake pipeline
> are **untouched** (touch them and you've failed the task's core claim). **Not a real-art pass** — you need **one
> simple, recognizably-distinct test model**, not a building library or pretty assets (real art is a backlog item).
> **Not a rendering-architecture change** — the mass building types **stay `MultiMesh`-instanced** (the perf rule:
> never thousands of nodes). **Not** the player car / traffic car (those aren't `MeshRegistry`-driven — later). **No**
> multi-material/multi-surface complex models, LOD/imposters, or a data-driven model-config system — keep the proof
> minimal.

> **Scope discipline.** The seam is **contained in `scripts/world/MeshRegistry.cs`** (its **only caller** is
> `TileBuilder.cs:31`). Edit **`MeshRegistry.cs`** + add the **`.glb` asset** (and, recommended, a tiny **generator
> tool** to produce it). **Keep `TileBuilder.cs` unchanged** by preserving the **unit-mesh contract** (normalize the
> glТF mesh into the registry — §3.3) so the existing MultiMesh upload + baked transforms work as-is. Leave
> `docs/GAME_SPEC.md §9` to **session A** (don't tick it). This is the **last foundation task** — after it, the roadmap
> is the backlog bundle (`docs/tasks/BACKLOG-world-rescale-physics-and-palette.md`).

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (skim **`docs/GAME_SPEC.md` §7.1–§7.2** and
**`CLAUDE.md`** first). Tasks 1–7 are done. The city renders as GPU **`MultiMesh` batches** — one `MultiMesh` per
`ObjectType` per tile (`TileBuilder.BuildMultiMeshes`), each `mm.Mesh = MeshRegistry.GetMesh(type)`, with one
per-instance transform/COLOR/CUSTOM per building. The **mesh source is the swap seam** Task 8 exercises.

### 0.1 How the seam works **today** (and why normalization is the crux)

`scripts/world/MeshRegistry.cs` builds **6 procedural unit meshes** (`BoxMesh{Size=One}`, `CylinderMesh{Height=1,
radius .5}`, …), index-aligned to `ObjectType` (Box=0…Sphere=5), each carrying the shared `building.gdshader` material.
`GetMesh(type)` returns `_meshes[(int)type]`.

**The load-bearing convention:** every mesh is **centered at its origin and fits a 1×1×1 AABB** (−0.5…+0.5 on each
axis). The bake's `PlacedObject.Xform` is `basis = Basis(Up, yaw).Scaled((fx, height, fz))`, `origin = (x, baseY +
height*0.5, z)` — i.e. it places the **mesh's centre** at the building's centre and scales the **unit** mesh to the
real footprint/height. So:

> **A `.glb` lines up *iff* its mesh is normalized to the same unit box** — centered at origin, AABB exactly 1×1×1.
> Then the **existing** baked transform sizes + seats it identically to the placeholder, with **no `TileBuilder`
> change and no re-bake.** Getting this normalization right **is** the "transforms line up" DoD.

**MultiMesh needs a single `Mesh`** (not a scene): a `.glb` imports as a **`PackedScene`** (a node tree). The seam must
**extract a single-surface `Mesh`** from it and normalize that. So **use a single-mesh, single-material `.glb`** for the
proof (multi-surface is a later concern).

### 0.2 The surfaces you touch

| Surface | File / location | What you do |
|---|---|---|
| **The seam** | `scripts/world/MeshRegistry.cs` (`EnsureBuilt`) | Load the `.glb`, extract + **normalize** its mesh to the unit box, register it for one `ObjectType` |
| **The asset** | `assets/models/<name>.glb` (+ Godot's `.glb.import`) | A simple, recognizably-distinct test building model (create `assets/models/` — it doesn't exist yet) |
| **(Recommended) generator** | a tiny `[Tool] EditorScript` (e.g. `scripts/world/ModelGenTool.cs`) | Builds a distinct Node3D and **exports it to `.glb` via `GltfDocument`** — so the asset is reproducible with no external tools |

**Not yours:** `TileBuilder.cs` (keep it unchanged — the normalization keeps the unit-mesh contract), `WorldData` /
`WorldGenerator` / `WorldBaker` / `world_8km.res` (**no re-bake**), `building.gdshader` (reuse), `Ship`/`Game`/`Traffic`,
the loader/terrain/camera/HUD.

---

## 1. Definition of Done

1. `./run.sh build` → **0 errors, 0 warnings**; `./run.sh build && ./run.sh check` → boots headless, **exits 0**, and
   still prints the **same** loader line (`:: WorldLoader: 315 objects, 1024 terrain tiles (85 populated), 250 m.`) —
   proving the **bake is untouched** (same object count) and the **`.glb` imports + the registry loads it headless**
   with no crash. (Headless imports the asset via `--import`; it can't show the look — that's `play`.)
2. `./run.sh play` (**GPU — eyeball these**):
   - **The swapped `ObjectType` renders from the `.glb`** — its buildings across the city now have the **model's
     silhouette**, visibly different from the procedural neighbours (so the swap is unmistakable).
   - **Transforms line up** — the model buildings sit on the ground at the **right place, footprint, height, and
     yaw**, interleaved correctly with the procedural types at the same scale (no floating, sinking, doubled size,
     off-centre, or mis-rotated instances). **This is the core proof** of the unit-box normalization.
   - **Same world** — quit + re-run → identical city (no re-bake; nothing reseeded). The `.glb` swap is **purely a
     registry change**; `world_8km.res` is byte-for-byte the same as before Task 8.
   - **FPS holds** — the type is still **`MultiMesh`-instanced** (GPU), not individual nodes.

---

## 2. Preconditions

- Branch **`dev6`**, Tasks 1–7 merged (`1b36911` is the latest). Confirm green: `./run.sh build && ./run.sh check` →
  exits 0 and prints the loader line above.
- `assets/models/` does **not exist yet** — create it. There are **no `.glb`s** in the repo (you make the first).
- Read `docs/GAME_SPEC.md` **§7.1** (the bake is data, not geometry — the seam's reason for being) and **§7.2** (the
  MultiMesh tile rendering). Skim `CLAUDE.md` (the **MeshRegistry = glТF seam** note, the **perf rule** "repeated
  geometry → MultiMesh, never thousands of nodes", "GPU visuals can't be confirmed headless").
- Note `MeshRegistry.GetMesh`'s **only caller** is `TileBuilder.cs:31` — keep that call working unchanged.

---

## 3. Work items

### 3.1 Source a simple, distinct test `.glb` (recommended: generate it in-engine)

You need **one `.glb`** with a **single mesh + single material** and a **recognizably different silhouette** from the
primitive it replaces (so the swap is obvious in `play`) — e.g. a **stepped/setback tower**, a **box with a rooftop
cap/antenna**, or a **chamfered slab**. **Author it at arbitrary real size** (e.g. a ~20 × 60 × 20 m tower) — do **not**
pre-normalize it; the registry normalizes (§3.3), which is what actually proves the seam handles real models.

**Recommended — generate it reproducibly with `GltfDocument`** (no Blender needed): a `[Tool] EditorScript` (mirror
`WorldBakeTool.cs`) that builds a small `Node3D` with a `MeshInstance3D` (combine a couple of `BoxMesh`/`PrismMesh`
primitives into a distinct tower, or one `ArrayMesh`), then:
```csharp
var doc = new GltfDocument();
var state = new GltfState();
doc.AppendFromScene(rootNode, state);
doc.WriteToFilesystem(state, "res://assets/models/test_tower.glb");
```
Run it once (editor: File → Run; or wire a headless path). Godot then **imports** the `.glb` (creates `.glb.import`).
*(Alternative: drop in any simple single-mesh `.glb` from elsewhere. Either way, commit the `.glb` + its `.import`.)*

### 3.2 Load the `.glb` + extract its `Mesh` (in `MeshRegistry.EnsureBuilt`)

A `.glb` imports as a **`PackedScene`**. Extract the single mesh once at startup:
```csharp
var scene = GD.Load<PackedScene>("res://assets/models/test_tower.glb");
Node root = scene.Instantiate();
MeshInstance3D mi = FindFirstMeshInstance(root);   // recursive walk for the first MeshInstance3D
Mesh raw = mi.Mesh;
// ... normalize (§3.3) → a unit ArrayMesh; then free the throwaway instance:
root.QueueFree();
```
Keep it defensive: if the load/instantiate/find fails (missing asset, bad import), **fall back to the procedural mesh**
for that type and `GD.PushWarning` — never crash the registry. *(Alternative: runtime `GltfDocument.AppendFromFile` +
`GenerateScene` — more code; the `PackedScene` load above reuses Godot's import and is simpler.)*

### 3.3 Normalize the mesh to the unit box — **the crux**

Map the raw mesh's AABB to a **centered 1×1×1** box (each axis independently, so the baked `(fx, height, fz)` drives the
real size exactly like the placeholders). Bake the normalization into a **new `ArrayMesh`** (so `GetMesh` keeps
returning a plain unit `Mesh` and `TileBuilder` stays unchanged):
```csharp
Aabb bb = raw.GetAabb();
Vector3 c   = bb.Position + bb.Size * 0.5f;
Vector3 inv = new Vector3(1f / bb.Size.X, 1f / bb.Size.Y, 1f / bb.Size.Z);   // guard any zero extent → 1
// vertex v -> (v - c) * inv  (component-wise):
var norm = new Transform3D(Basis.Identity.Scaled(inv), -c * inv);
var st = new SurfaceTool();
st.Begin(Mesh.PrimitiveType.Triangles);
st.AppendFrom(raw, 0, norm);          // bakes the transform (incl. normals) into the surface
ArrayMesh unit = st.Commit();         // AABB is now ~(-0.5..0.5)^3, centered
```
*(Confirm the exact `SurfaceTool` call sequence against `GodotSharp` — `AppendFrom(Mesh, surface, Transform3D)` is the
key API; adjust if Begin/Commit needs tweaking. The **math** above is the contract: `(v − center) ⊙ 1/size`.)*

**Why non-uniform (per-axis) fit:** the placeholders are literally 1×1×1 and get scaled non-uniformly by the bake, so a
building's proportions come from the **bake**, not the model — fitting the glТF AABB to 1×1×1 makes it behave identically.
Author the test model at roughly building-ish proportions so the stretch reads well.

### 3.4 Register the mesh for one (or two) **placed** `ObjectType`(s) + pick a material

In `EnsureBuilt`, after building the procedural `_meshes`, **replace one entry** with the normalized glТF mesh:
```csharp
_meshes[(int)ObjectType.BuildingTaper] = unit;   // e.g. Taper (15% of the city — visible, not dominating)
```
- **Pick a type the bake actually places.** The generator places **Box/Round/Prism/Taper/Shard**; **`BuildingSphere`
  (5) is never placed** (`PickShape` never returns Sphere) — swapping it would render **nothing**. Recommend **`Taper`
  or `Round`** for a clearly-visible-but-not-overwhelming proof (or `Box` (50%) for maximum visibility). "One or two" types
  per the DoD.
- **Material — keep it coherent (recommended):** set the unit mesh's surface material to the shared **`building.gdshader`**
  material (`unit.SurfaceSetMaterial(0, _buildingMat)`) so the new geometry **still wears the neon windows** and sits in
  the city's look — the swap is proven by the **silhouette**, and the building shader is world-space so it works on any
  geometry. *(Alternative: keep the `.glb`'s **own** imported material for an even more obvious "it's a model" look — then
  the per-instance COLOR/CUSTOM window channels are simply unused for that type. Either is a valid proof; say which you
  chose.)*
- **Keep it a hardcoded one-line swap** for the proof — do **not** build an `ObjectType→path` config map (that's a
  future enrichment when there's a real model library).

### 3.5 Leave the bake + TileBuilder untouched (verify, don't edit)

Confirm you did **not** touch `WorldData`/`WorldGenerator`/`WorldBaker`/`world_8km.res` and did **not** change
`TileBuilder` — the normalized unit mesh means `TileBuilder.cs:31`'s `mm.Mesh = MeshRegistry.GetMesh(type)` and the
existing per-instance transforms **just work**. (If you found yourself editing `TileBuilder` or re-baking, step back —
the normalization in §3.3 is what keeps those untouched.)

---

## 4. Correctness points (get these right)

1. **Unit-box normalization is the whole task.** Centered, AABB 1×1×1, per-axis. Get it wrong and models float/sink
   (bad center → off by half-height), are the wrong size (bad scale), or are squashed. Eyeball a model building next to
   a procedural one — same footprint/height, seated on the ground, same yaw.
2. **MultiMesh needs one `Mesh`, single surface.** Extract a single-surface mesh; use a single-material `.glb`. A
   multi-surface model is out of scope (it'd need per-surface materials on the MultiMesh — a later concern).
3. **No re-bake, ever.** `world_8km.res` stays byte-identical; the headless loader line stays `315 objects`. The swap is
   a **runtime registry change only**. This is the task's headline claim — protect it.
4. **Determinism is free** — you changed rendering, not placement. Same city every launch by construction.
5. **Swap a PLACED type** — not `BuildingSphere` (0 instances). Verify the type you pick actually appears in the city.
6. **Stay on `MultiMesh`** — do not switch the type to individual nodes/`PackedScene` instances (perf rule). The proof
   is "a MultiMesh-instanced type sources its mesh from a `.glb`."
7. **Headless import must succeed** — `./run.sh check` runs `--import`, which imports the `.glb`. If it errors, the asset
   or its `.import` is wrong. Commit **both** the `.glb` and its `.glb.import` (and any `.godot/imported` is regenerated,
   not committed).
8. **Fail soft** — if the asset is missing/broken, fall back to the procedural mesh + a warning; never crash the registry
   (it runs during world build).

---

## 5. Verify

1. `./run.sh build` → **0/0**.
2. `./run.sh build && ./run.sh check` → exits 0; **imports the `.glb`**; still prints the **unchanged** loader line
   (`315 objects …`) — proving no re-bake + a clean headless import. (Renders nothing — `play` is the real test.)
3. `./run.sh play` (**GPU — the real DoD**), against §1: fly over the city and confirm the **swapped type wears the
   model silhouette**, **lined up** (place/size/height/yaw) among the procedural neighbours; quit + relaunch → identical.
   Watch FPS (still MultiMesh).
4. **Bake-untouched proof:** `git status` shows **no change** to `world/world_8km.res` (and you ran no `./run.sh bake`).
   Optionally `sha256sum world/world_8km.res` and confirm it matches the pre-Task-8 hash.
5. **Screenshot (optional, for the report):** `godot-mono --path . &` (note PID), `sleep ~22`,
   `spectacle -bnf -o /tmp/gltf.png`, `kill -9 $PID; pkill -9 -x godot-mono` — **as one background Bash command**.

---

## 6. Report back to session A (for review)

- `git status` + `git diff --stat`, and the edits: **`scripts/world/MeshRegistry.cs`** (load + normalize + register),
  the **`assets/models/<name>.glb` (+ `.import`)**, and the **generator tool** if you added one.
- `./run.sh build` + `./run.sh check` output (0/0; **same** loader line — your no-re-bake proof) and confirmation the
  `.glb` imported headless.
- The **GPU `play` description** against §1 — which `ObjectType` you swapped, that it renders from the model and **lines
  up**, identical relaunch, FPS (attach the §5 screenshot if taken).
- **Which type(s)** you swapped, the **material choice** (building shader vs the glТF's own), and **how you sourced the
  `.glb`** (the `GltfDocument` generator vs a dropped-in model).
- **Confirmation** you did **not**: re-bake / change `world_8km.res` / `WorldData` / `WorldGenerator` / `WorldBaker`,
  edit `TileBuilder`, or change the rendering architecture (still `MultiMesh`). Include the `world_8km.res` hash/no-diff.
- **Commit** once build + check + playtest are green. Stage **by path** (`scripts/world/MeshRegistry.cs`, the `.glb` +
  `.glb.import`, the generator tool); **never `git add -A`**; **do not** tick `docs/GAME_SPEC.md §9` (session A ticks
  it). Suggested message:
  `Task 8: glTF model-loading seam — one building type renders from an imported .glb via MeshRegistry, zero re-bake`.
  End the commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **Re-baking / storing meshes in the bake** — the bake stays type+transform+shader-channels; `world_8km.res` is
  untouched. That invariant is the point of the task.
- **Real art / a model library / multiple swapped types beyond one–two** — this is a **seam proof**, not an art pass.
  A data-driven `ObjectType→model-path` map is a later enrichment.
- **Multi-surface / multi-material models, PBR texture sets, LOD/imposters** — single mesh, single material for the proof.
- **Rendering individual nodes / `PackedScene` instances for mass buildings** — they stay `MultiMesh` (perf rule). A
  `PackedScene`-per-instance path for **sparse unique landmarks/props** is a separate future task, not this one.
- **The player car / traffic car models** — those aren't `MeshRegistry`-driven; swapping them to glТF is later.
- **The world rescale / palette / physics** — that's the backlog bundle (`docs/tasks/BACKLOG-world-rescale-physics-and-palette.md`).
- **Editing `settings.cfg` (commit) / `CLAUDE.md` / `docs/` prose / ticking §9** — session A owns docs on review.
