# CLAUDE.md — DIVEPUNK

High-speed arcade flyer through a procedurally generated cyberpunk megacity.
**Full design, scope, and the milestone roadmap live in `@docs/GAME_SPEC.md` — read it before implementing a milestone.**

## Stack
- **Engine:** Godot 4.6 — Forward+ renderer on desktop/Linux, Mobile renderer for Android.
- **Language:** GDScript (typed). Physics: Jolt (the Godot 4.6 default).
- **Targets:** Linux + Android (MVP). Optional native acceleration via godot-rust/gdext — **only if a profiler proves GDScript is the bottleneck.** Do not start there.

> The CLI binary is written here as `godot`; on some setups it's `godot4`. Adjust as needed.

## Run / build
- Open in editor: `godot --editor --path .`
- Play the main scene: `godot --path . res://scenes/main/Main.tscn`
- Boot smoke-test (does it start without errors): `godot --headless --path . --quit`
- Export (configure the export presets in the editor first):
  - `godot --headless --path . --export-release "Linux" build/divepunk.x86_64`
  - `godot --headless --path . --export-release "Android" build/divepunk.apk`

## Code conventions
- **Typed GDScript** everywhere: typed vars, params, returns, and signals.
- `snake_case` for variables/functions/files; `PascalCase` for nodes/classes; `CONSTANT_CASE` for constants.
- Expose tunables with `@export` (organised via `@export_group`) so feel and balance are editable in the Inspector — never bury magic numbers in code.
- Prefer **signals** over tight cross-references between systems where practical.
- Use **framerate-independent smoothing**: `value.lerp(target, 1.0 - exp(-k * delta))`. Never a bare `lerp(a, b, delta)`.
- Read input through **InputMap actions** (`steer_left/right/up/down`, `boost`, `restart`) — never hardcode keys in gameplay code.

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
├── docs/GAME_SPEC.md                # full design + roadmap
├── scenes/{main,player,world,ui}/   # .tscn scenes
├── scripts/                         # ship.gd, camera_rig.gd, game.gd, chunk_manager.gd, ...
├── autoload/                        # singletons: GameState, ScoreManager, AudioManager
├── resources/{materials,meshes,environment}/
├── shaders/                         # speed lines, post-fx
├── assets/{audio,textures}/
└── rust/                            # OPTIONAL gdext extension (only if profiled)
```
