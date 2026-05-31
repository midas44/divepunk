# CLAUDE.md — DIVEPUNK

High-speed arcade flyer through a procedurally generated cyberpunk megacity.
**Full design, scope, and the milestone roadmap live in `@docs/GAME_SPEC.md` — read it before implementing a milestone.**

## Stack
- **Engine:** Godot 4.6 — Forward+ renderer on desktop/Linux, Mobile renderer for Android.
- **Language:** C# (.NET 8 / `net8.0`), typed throughout. Physics: Jolt (the Godot 4.6 default). Requires the **.NET/Mono build** of Godot (`godot-mono`) — the plain `godot` build can't run C#.
- **Targets:** Linux + Android (MVP). Optional native acceleration via godot-rust/gdext — **only if a profiler proves C# is the bottleneck.** Do not start there.

> The project is C#: `DIVEPUNK.csproj` / `DIVEPUNK.sln` (`Godot.NET.Sdk`, `net8.0`) are tracked. `run.sh` auto-detects `godot-mono`. Ported from typed GDScript — the full porting rulebook is in `docs/MIGRATION_GDSCRIPT_TO_CSHARP.md`.

## Run / build
Use the `run.sh` wrapper (auto-detects `godot-mono`). **C# must be compiled before any headless run.**
- Build C#: `./run.sh build`  (= `dotnet build -c Debug`)
- Open in editor: `./run.sh editor`
- Play the main scene: `./run.sh play`
- Boot smoke-test (build, then import + 180-frame boot + quit): `./run.sh build && ./run.sh check`
- Export (configure the export presets in the editor first): `./run.sh export-linux` / `./run.sh export-android`

## Code conventions
- **C#, typed throughout.** Every node-attached class is `public partial class X : BaseType` (the source generator requires `partial`); add `[GlobalClass]` to types referenced by scenes/Inspector. **File name == class name** — Godot loads path-bound scripts by filename, so `Ship.cs` ⇒ class `Ship`.
- `PascalCase` for files/classes/methods/properties/`[Export]`s; `_camelCase` for private fields; `camelCase` for locals/params. Lifecycle overrides keep the underscore: `_Ready`, `_Process(double delta)`, `_PhysicsProcess`, `_Input`, `_UnhandledInput`.
- Expose tunables with `[Export]` (grouped via `[ExportGroup]`) so feel/balance are editable in the Inspector — never bury magic numbers in code. **`settings/settings.cfg` keys stay `snake_case`** (string keys); only the C# property they map to is PascalCase.
- Prefer **signals** (`[Signal] delegate void XEventHandler(...)`; emit `EmitSignal(SignalName.X, …)`; connect `+=`) over tight cross-references. **Unsubscribe from autoload signals in `_ExitTree`** — C# doesn't auto-disconnect on free like GDScript did.
- Use **framerate-independent smoothing**: `v.Lerp(target, 1.0f - Mathf.Exp(-k * (float)delta))`. Never a bare `Lerp(a, b, delta)`. Cast `delta` to `float` once; prefer `Mathf.*` (float) over `System.Math.*` (double); float literals need the `f` suffix.
- Read input through **InputMap actions** (`steer_left/right/up/down`, `accelerate`, `decelerate`, `cycle_camera`, `restart`, …) — never hardcode keys in gameplay code.
- **Determinism (city gen):** keep seeded-RNG call order/count identical; the seed-mix uses `long` + `unchecked` (C# `int` overflows differently); match GDScript's half-away-from-zero rounding where it feeds loop counts (C# `Math.Round` is banker's).

## Architecture rules (perf-critical — see the spec for detail)
- Repeated geometry (buildings, props) → **`MultiMeshInstance3D`** (GPU instancing). Never spawn thousands of individual nodes.
- The city is **streamed chunks** through an **object pool** (spawn ahead of the player, recycle behind). Never instantiate/free per frame.
- Generation is **deterministic from `(seed, difficulty)`**. Run it off the main thread or amortise it across frames — never a per-frame hitch.
- Keep scenes small and single-responsibility (this makes AI-assisted edits far more reliable).
- **Test on a real mid-range Android device by Milestone M3.** Emulators mislead on performance.

## Workflow
- Implement **one milestone (or sub-task) at a time**, then stop for a playtest. Milestones are in `@docs/GAME_SPEC.md` §9.
- **The M1 gate:** flying must feel *great* in an empty void before any city exists. Do not move to M2 until it does.
- Commit after each working increment. Use `/clear` between unrelated tasks.
- Track progress with the checkboxes in `@docs/GAME_SPEC.md` (or a `TASKS.md`) — tick them off as you go.

## Project layout
```
res://
├── CLAUDE.md                        # this file (lean — detail lives in docs/)
├── DIVEPUNK.csproj / DIVEPUNK.sln   # C# project (net8.0, Godot.NET.Sdk) — tracked
├── docs/GAME_SPEC.md                # full design + roadmap
├── scenes/{main,player,world,ui}/   # .tscn scenes
├── scripts/                         # Ship.cs, CameraRig.cs, Game.cs, ChunkManager.cs, ...
├── autoload/                        # singleton .cs: Config, ScoreManager, AudioManager
├── resources/{materials,meshes,environment}/
├── shaders/                         # speed lines, post-fx (*.gdshader — untouched by the port)
├── assets/{audio,textures}/
└── rust/                            # OPTIONAL gdext extension (only if profiled)
```
