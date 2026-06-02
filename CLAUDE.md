# CLAUDE.md — DIVEPUNK

Open-world flying-car sandbox in a fixed, pre-baked cyberpunk coastal city at dusk. Fly anywhere in a
bounded ≈40×40 km world; physics collisions cause **damage + bounce, never game-over**.
**Full design, architecture, and the task roadmap live in `@docs/GAME_SPEC.md` — read it before implementing a task.**

> **⚠️ Reframe in progress (2026-06).** The game is being rebuilt from an *infinite arcade score-corridor
> flyer* into the open-world sandbox above. Until **Task 1** (scaffolding & removals) lands, the repo
> still contains the old corridor/streaming/scoring code (`ChunkManager`, `ScoreManager`, `GameOver`,
> corridor clamp, near-miss/combo). Build toward the new design; see the migration table in the spec (§8).

## Stack
- **Engine:** Godot 4.6 — Forward+ renderer on desktop/Linux. (Android deferred to a mature stage.)
- **Language:** C# (.NET 8 / `net8.0`), typed throughout. Requires the **.NET/Mono build** of Godot (`godot-mono`) — the plain `godot` build can't run C#.
- **Physics:** **Jolt** (the Godot 4.6 default), now used for **real rigid-body simulation** of the flying car (collision response, bounce, contact impulses) — not just detection.
- **3D models:** objects are **procedural placeholders now**; they swap to **glTF 2.0 (`.glb`)** later via a `MeshRegistry`, **without re-baking the world layout** (the bake stores placement + type, not geometry).
- **Optional native acceleration:** godot-rust/gdext — **only if a profiler proves C# is the bottleneck.** Do not start there.

> The project is C#: `DIVEPUNK.csproj` / `DIVEPUNK.sln` (`Godot.NET.Sdk`, `net8.0`) are tracked. `run.sh` auto-detects `godot-mono`. Ported from typed GDScript — the porting rulebook is in `docs/MIGRATION_GDSCRIPT_TO_CSHARP.md`.

## Run / build
Use the `run.sh` wrapper (auto-detects `godot-mono`). **C# must be compiled before any headless run.**
- Build C#: `./run.sh build`  (= `dotnet build -c Debug`)
- Open in editor: `./run.sh editor`
- Play the main scene: `./run.sh play`
- Boot smoke-test (build, then import + 180-frame boot + quit): `./run.sh build && ./run.sh check`
- Bake the world to `res://world/world_main.res` (headless; **added in Task 3**): `./run.sh bake`
- Export (configure the export presets in the editor first): `./run.sh export-linux`

## Code conventions
- **C#, typed throughout.** Every node-attached class is `public partial class X : BaseType` (the source generator requires `partial`); add `[GlobalClass]` to types referenced by scenes/Inspector (and to `Resource` types used by the bake). **File name == class name** — `Ship.cs` ⇒ class `Ship`.
- `PascalCase` for files/classes/methods/properties/`[Export]`s; `_camelCase` for private fields; `camelCase` for locals/params. Lifecycle overrides keep the underscore: `_Ready`, `_Process(double delta)`, `_PhysicsProcess`, `_IntegrateForces`, `_Input`, `_UnhandledInput`.
- Expose tunables with `[Export]` (grouped via `[ExportGroup]`) so feel/balance are editable in the Inspector — never bury magic numbers in code. **`settings/settings.cfg` keys stay `snake_case`** (string keys); only the C# property they map to is PascalCase.
- Prefer **signals** (`[Signal] delegate void XEventHandler(...)`; emit `EmitSignal(SignalName.X, …)`; connect `+=`) over tight cross-references. **Unsubscribe from autoload signals in `_ExitTree`** — C# doesn't auto-disconnect on free like GDScript did.
- Use **framerate-independent smoothing**: `v.Lerp(target, 1.0f - Mathf.Exp(-k * (float)delta))`. Never a bare `Lerp(a, b, delta)`. Cast `delta` to `float` once; prefer `Mathf.*` (float) over `System.Math.*` (double); float literals need the `f` suffix.
- Read input through **InputMap actions** (6-DOF flight: pitch/yaw via `steer_*`, plus `roll_*` and a thrust action; `cycle_camera`, …) — never hardcode keys in gameplay code.
- **Bake determinism:** identical `(seed, extent)` must produce a byte-identical `world_main.res`. Keep seeded-RNG call order/count stable; the seed-mix uses `long` + `unchecked` (C# `int` overflows differently); match half-away-from-zero rounding where it feeds loop counts (C# `Math.Round` is banker's). This matters once for the bake, not per-frame.

## Architecture rules (perf-critical — see the spec for detail)
- The world is **fixed, finite (≈40×40 km), and baked once to disk** (`WorldData` resource → `res://world/world_main.res`), then loaded at runtime. **No streaming, no per-run regeneration, no object pool ring.**
- Repeated geometry (buildings, props) → **`MultiMeshInstance3D`** (GPU instancing), one batch per `ObjectType` per fixed **world tile** (≈1250 m). Never spawn thousands of individual nodes.
- The bake stores **object descriptors (type + transform + shader channels)**, not meshes — runtime instantiates via the `MeshRegistry` so visuals swap to glTF later with **zero re-bake**.
- Cull/LOD with `VisibilityRangeBegin/End`; **fog hides the cull boundary.** Instantiate **colliders only near the car** (a small physics radius), not for all 40 km at once.
- The flying car is a **`RigidBody3D`** (assisted-arcade 6-DOF: thrust + torques + PD auto-leveling). Collisions are **resolved by Jolt as a bounce**; damage accumulates from **contact impulse** in `_IntegrateForces`. **Health hitting zero does nothing — there is no game-over.**
- Keep scenes small and single-responsibility (this makes AI-assisted edits far more reliable).

## Workflow
- Implement **one task at a time** (`@docs/GAME_SPEC.md` §9), then stop for a playtest.
- **The feel gate (Task 2):** the `RigidBody3D` flying car must feel *great* in an empty void — and bounce + take damage on impact without ever ending the game — before the world is built. Do not move on until it does.
- Commit after each working increment. Use `/clear` between unrelated tasks.
- Track progress with the checkboxes in `@docs/GAME_SPEC.md` §9 — tick them off as you go.
- GPU visuals (dusk, water, neon, terrain) can't be confirmed headless — eyeball them in `./run.sh play`.

## Project layout
```
res://
├── CLAUDE.md                        # this file (lean — detail lives in docs/)
├── DIVEPUNK.csproj / DIVEPUNK.sln   # C# project (net8.0, Godot.NET.Sdk) — tracked
├── docs/GAME_SPEC.md                # full design + roadmap
├── world/world_main.res             # the baked world (data: heightmap + biomes + object descriptors)
├── scenes/{main,player,world,ui}/   # .tscn scenes
├── scripts/                         # Ship.cs (RigidBody3D), CameraRig.cs, Game.cs, Traffic.cs, DamageComponent.cs, ...
│   └── world/                        # WorldData, PlacedObject, WorldGenerator, WorldLoader, TileBuilder, MeshRegistry, WorldBakeTool
├── autoload/                        # singleton .cs: Config, AudioManager  (ScoreManager removed in Task 1)
├── resources/{materials,meshes,environment}/
├── shaders/                         # building / sky / water / post-fx (*.gdshader)
├── assets/{audio,textures,models}/  # models/ = glTF .glb (later)
└── rust/                            # OPTIONAL gdext extension (only if profiled)
```
