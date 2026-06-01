# Task 1 — Scaffolding & Removals (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 1. It is self-contained — you do not
> need the planning conversation. When you finish, the user playtests and brings results back to
> session A for review. One such doc lives in `docs/tasks/` per reframing task.

> **Scope discipline.** Implement **Task 1 only**. Do **not** start Task 2 (RigidBody flight), the world
> bake, terrain, the dusk pass, traffic, or glTF — those are later tasks. If something here seems to
> need a later task, stop and note it in your report instead of pulling it forward.

---

## 0. Context (read first)

DIVEPUNK is being reframed from an *infinite arcade score-corridor flyer* into a **bounded, pre-baked
open-world flying-car sandbox**. Read **`docs/GAME_SPEC.md` §7–9** and **`CLAUDE.md`** before starting —
they define the target architecture, conventions, and the full Task 0–8 roadmap.

Task 0 (the design-doc rewrite) is done. **Task 1 strips out the old loops** (streaming, scoring,
game-over, corridor) so the project becomes a clean slate: a car flying freely in an empty void with a
speed HUD and **no possible game-over**. The car stays a `CharacterBody3D` **for this task only** — Task
2 swaps it to a `RigidBody3D`. Keep your Ship edits here minimal; most of `Ship.cs` is rewritten next task.

**Conventions reminder (from CLAUDE.md):** C# typed throughout; `partial` classes; `PascalCase`
members / `_camelCase` private fields; tunables via `[Export]`; framerate-independent smoothing
(`Lerp(target, 1 - Exp(-k·dt))`); input via `InputMap` actions; unsubscribe autoload signals in
`_ExitTree`.

---

## 1. Definition of Done

- `./run.sh build` → **0 errors**, and `./run.sh build && ./run.sh check` boots headless and exits clean.
- `./run.sh play`: you fly an **empty void freely in all directions** (no corridor clamp), with a HUD
  showing **THROTTLE / SPD / V-SPD** bars — and **no score, combo, or best** anywhere.
- **No game-over is possible** — the crash/near-miss/scoring/game-over code is gone, not just disabled.
- The old streaming/scoring/game-over files are **deleted** and the tree still compiles.
- A flat ground plane sits below the car and follows it (visual reference in the void).

---

## 2. Preconditions

- Branch: confirm with the user which branch to work on (the reframe has been on `dev4`/`dev5`).
- Confirm a green baseline first: `./run.sh build` should already succeed.
- Read the files you'll edit: `scripts/Game.cs`, `scripts/Ship.cs`, `scripts/Hud.cs`,
  `settings/settings.cfg`, `project.godot`.

---

## 3. Work items

### 3.1 Delete files (and their `.cs.uid` siblings)
- `autoload/ScoreManager.cs` (+ `autoload/ScoreManager.cs.uid`)
- `scripts/GameOver.cs` (+ `scripts/GameOver.cs.uid`)
- `scripts/ChunkManager.cs` (+ `scripts/ChunkManager.cs.uid`)
- `scenes/ui/GameOver.tscn`
- `scenes/world/ChunkManager.tscn`

**Do NOT delete** (dead code now, harvested/rewritten later — they compile standalone):
`scripts/CityChunk.cs` + `scenes/world/CityChunk.tscn` (Task 4), `scripts/Traffic.cs` (Task 7).
Their only references to the deleted classes are in **comments** — leave those comments, they're harmless.

### 3.2 `project.godot`
- Remove the autoload line: `ScoreManager="*res://autoload/ScoreManager.cs"`.
- Leave `Config` and `AudioManager` autoloads. (Final order: `Config`, `AudioManager`.)

### 3.3 `scripts/Game.cs` (the orchestrator — most of the work)

**Remove these members entirely:**
- Static loads `ChunkManagerScene` and `GameOverScene` (keep `HudScene` and `SkyShader`).
- Fields `_mgr`, `_traffic`, `_gameOver`.
- The near-miss/crash/time-dilation juice exports: `ShakeOnNearMiss`, `ShakeOnCrash`,
  `EnableNearMissFlash`, `NearMissFlashAmount`, `CrashFlashAmount`, `NearMissTimeScale`,
  `TimeDilationDuration`, `TimeDilationCooldown`. **Keep `ShakeOnBoost`** (used by the boost punch).
- Time-dilation state + methods: fields `_tdTimer`, `_tdCooldown`; methods `TriggerTimeDilation`,
  `UpdateTimeDilation`, `ResetTimeScale`.
- Methods `SpawnWorld`, `SpawnTraffic`, `ApplyTrafficConfig`, `OnShipNearMiss`, `OnShipCrashed`,
  `CityDrawDistance`.

**Rewrite these methods to the shapes below:**

`_Ready()` (drop the near-miss/time-dilation config reads, drop `SpawnWorld`/`SpawnTraffic`):
```csharp
public override void _Ready()
{
    RegisterInput();
    Engine.TimeScale = 1.0;        // defensive: clear any leftover slow-mo
    CaptureMouse();
    EnsureEnvironment();
    SpawnShipAndCamera();
    SpawnUi();
    SpawnScreenFx();
    AudioManager.Instance?.StartMusic();
}
```

`SpawnShipAndCamera()` — keep as-is **except** the signal hookups: subscribe **only** `SpeedChanged`
(delete the `Crashed += OnShipCrashed;` and `NearMiss += OnShipNearMiss;` lines).

`SpawnUi()` — drop the `ScoreManager.Instance.ResetRun();` and the whole GameOver instantiation; keep the HUD:
```csharp
private void SpawnUi()
{
    if (GetNodeOrNull("HUD") != null)
        return;
    _hud = HudScene.Instantiate<Hud>();
    _hud.Name = "HUD";
    AddChild(_hud);
    _hud.SetShip(_ship);
}
```

`_Process()` — drop all `ScoreManager` calls and the time-dilation update; keep the ground following the
car, but now on **both X and Z** (the world is open, not a -Z corridor):
```csharp
public override void _Process(double delta)
{
    if (_ship != null && _ground != null)
    {
        Vector3 gp = _ship.GlobalPosition;
        _ground.GlobalPosition = new Vector3(gp.X, 0.0f, gp.Z);
    }
}
```

`OnShipSpeedChanged(...)` — **keep unchanged** (drives speed-line FX + the boost punch + boost SFX).

`ApplyCameraConfig(...)` — the far-clip currently auto-extends from the (now-deleted) streaming knobs.
Replace that tail with a plain config read:
```csharp
rig.FarDistance = CfgFloat("camera", "far", rig.FarDistance);
```
(and delete the `CityDrawDistance()` helper).

`ApplyShipConfig(...)` — remove the three corridor lines (`ship.BoundX`, `ship.BoundYMin`,
`ship.BoundYMax`); **keep** the speed/throttle/climb lines (`base_speed`, `min_speed`, `max_speed`,
`accelerate_rate`, `decelerate_rate`, `climb_angle_deg`, `invert_pitch`, `invert_bank`).

`EnsureEnvironment()` — the `RefGround` is a long thin corridor plane (`8000 × 48000`). Make it a square
so it reads in every direction, e.g. `plane.Size = new Vector2(20000.0f, 20000.0f);` (it now follows the
car's X/Z each frame via `_Process`). Everything else (Sun, WorldEnvironment, sky) stays — the dusk
retune is Task 6.

### 3.4 `scripts/Ship.cs` (minimal scrub — Task 2 rewrites it fully)

Keep the throttle+inertia forward motion, the lateral/vertical steering, the visual bank/pitch, the
`SpeedChanged` signal, and all the HUD getters. **Remove the corridor clamp and the
crash/near-miss/game-over machinery:**
- Delete the `[Signal] ... NearMiss` and `[Signal] ... Crashed` delegates.
- Delete fields: `_alive`, `_crashShape`, `_crashQuery`, `_nearShape`, `_nearQuery`, `_nearNow`, and the
  `private const int ObstacleLayer = 2;`.
- Delete the `[ExportGroup("Collision")]` exports `CrashSize` and `NearMissSize`, and the
  `[ExportGroup("Corridor...")]` exports `BoundX`, `BoundYMin`, `BoundYMax`.
- In `_Ready()`: drop the `BuildQueries();` call (keep the `_forwardSpeed` init + `EnsureVisualAndCollision()`).
- In `_PhysicsProcess()`: drop the `if (!_alive) return;` guard, the `ClampToCorridor();` call, and the
  `CheckObstacles();` call. Keep throttle → steer → compose `Velocity` → `MoveAndSlide()` → `BankModel(d)`
  → emit `SpeedChanged`.
- Delete the methods `Crash()`, `CheckObstacles()`, `BuildQueries()`, `ClampToCorridor()`.
- Remove the now-unused `using System.Collections.Generic;` (it was only for `_nearNow`).

Result: a `CharacterBody3D` that flies freely in the void and emits `SpeedChanged`. (`GetSpeed`,
`GetVerticalSpeed`, `GetTopSpeed`, `GetMaxVerticalSpeed`, `GetBoostMeter`, `GetSpeedRatio` stay — the HUD
and camera use them.) Roll/yaw and 6-DOF are **Task 2**, not now.

### 3.5 `scripts/Hud.cs` (drop scoring, keep telemetry)

- Delete fields `_scoreLabel`, `_multLabel`, `_bestLabel`.
- In `_Ready()`: remove the entire `ScoreManager` block (the `sm` subscribe lines + the initial
  `OnScoreChanged`/`OnHighScoreChanged` calls). Keep `Layer = 50;` and `Build();`.
- Delete `_ExitTree()` entirely (it only unsubscribed `ScoreManager`).
- Delete `OnScoreChanged`, `OnHighScoreChanged`, `OnNearMiss`.
- In `Build()`: remove the score, multiplier, and best label creation blocks. **Keep** the boost/throttle
  meter and the two `MakeMeter(...)` bars (SPD, V-SPD) and their `_Process` polling — those read the ship.
- Update the file header comment to describe a ship-telemetry HUD (no scoring).

### 3.6 `settings/settings.cfg` (obsolete sections only)

⚠️ **The user edits this file live.** **Re-read it immediately before editing**, preserve every value in
the sections that stay, **stage it explicitly** (`git add settings/settings.cfg` — never `git add -A`),
and never revert tuned values.

- **Remove** the `[corridor]` section and the `[streaming]` section (with their comment headers) — the
  corridor and streaming are gone.
- **Keep intact:** `[display]`, `[game]`, `[ship]`, `[camera]`, `[traffic]`, `[debug]`, `[fx]`, `[audio]`.
- The new `[flight]` / `[damage]` / `[world]` sections are **not** added here — they land with their code
  in Tasks 2–3. (Leaving `[ship]`, `[game] seed/scale`, `[traffic]`, `[debug] log_streaming` in place is
  fine; some keys are simply unread until later tasks. Don't delete them — they hold tuned values.)
- Optionally refresh the file's top comment to mention the reframe.

### 3.7 Minor: `autoload/AudioManager.cs` comment
One comment reads `autoload order: Config -> ScoreManager -> AudioManager`. Update it to
`Config -> AudioManager` (ScoreManager is gone). Comment-only; no code change.

---

## 4. Verify

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → headless 180-frame boot exits clean (no exceptions; expect the
   `[DIVEPUNK] world seed` line to be **gone**, since ChunkManager is removed).
3. `./run.sh play` and confirm the DoD by hand (GPU/feel can't be checked headless):
   - Fly with **WASD** (steer), **Shift/Space** (accelerate), **Ctrl** (decelerate), **Q** (camera),
     mouse (free-look). You can climb/dive/strafe freely with **no clamp** stopping you.
   - HUD shows **THROTTLE / SPD / V-SPD** only — **no score, combo, or BEST**.
   - There is **nothing to crash into** and **no game-over** — fly as long as you like.
   - A ground plane stays beneath you wherever you fly.

---

## 5. Report back to session A (for review)

Include:
- `git status` + `git diff --stat` (and the list of deleted files).
- The `./run.sh build` and `./run.sh check` output (pass/fail + any warnings).
- A short playtest note: did free flight + the scrubbed HUD + no-game-over all hold? Any feel oddities
  (e.g. the void flight without a clamp letting you fly to absurd altitudes — expected, fine for now)?
- **Any deviations** from this brief and why, and **anything you deferred** as out-of-scope.
- Commit the increment once build + check are green, with explicit staging (not `git add -A`); suggested
  message: `Task 1: strip streaming/scoring/game-over; free-flight void + telemetry HUD`.

---

## 6. Out of scope (do not do here)
- `RigidBody3D` 6-DOF flight, damage, roll/yaw input → **Task 2**.
- `WorldData`/bake pipeline → **Task 3**. World load/tiles → **Task 4**. Terrain/ocean → **Task 5**.
- Dusk aesthetic retune → **Task 6**. Ambient traffic → **Task 7**. glTF seam → **Task 8**.
- Deleting `CityChunk.cs`/`Traffic.cs` (kept for later harvest/rewrite).
