# DIVEPUNK — GDScript → C# Migration

> **Task type:** Engine-language port. Stay on **Godot 4.6**; convert the gameplay code from **typed GDScript** to **C# (.NET)**.
> **Executor:** a fresh Claude Code session. Read this whole document, then `@docs/GAME_SPEC.md` and the root `CLAUDE.md`, before touching code.
> **Prime directive:** this is a **behaviour-preserving** port. The game must play *identically* afterwards — same flight feel, same city, same scoring, same juice. No redesigns, no "improvements," no scope changes. If you spot a bug, note it; do not fix it in the same pass.

---

## 0. Why this port (context)

We are **not** switching engines. We are switching the scripting language **inside Godot** from GDScript to C#, because C# gives a large performance headroom over interpreted GDScript while keeping everything that makes this project work: the Godot editor, the text-based `.tscn` scenes, the working Android export path, and the AI-friendly workflow. Rust (via `gdext`) remains the *optional* accelerator for profiled hot paths only — exactly as `GAME_SPEC.md` §3 already says. **Do not introduce Rust in this task.**

Performance reality to keep in mind (so you don't over-engineer): most frame time here is GPU- and engine-C++-bound (MultiMesh rendering, fog, SSR, physics). The C# win is on *our* CPU code — generation, traffic stepping, audio synth. Port faithfully first; optimise only if a profiler later asks.

---

## 1. Guardrails — what must NOT change

These are invariants. Breaking any of them is a failed port.

- **Scene node trees** (`.tscn`) — keep every node, name, and layout. Only the *script reference* on each scene's root flips from `.gd` to `.cs` (see §6). `game.gd` builds almost everything in code, so the scenes are thin — but do not restructure them.
- **Shaders** (`shaders/*.gdshader`) — untouched. C# sets the same shader params by the same string names.
- **`settings/settings.cfg`** — untouched. Config **keys stay snake_case** (e.g. `[ship] base_speed`). Only the *C# property* they map to is renamed (`BaseSpeed`). The keys are strings; they do not change.
- **Autoload singleton names** — `Config`, `ScoreManager`, `AudioManager` (see `project.godot [autoload]`). Other code and `/root/<Name>` lookups depend on these exact names.
- **InputMap action names** — `steer_left/right/up/down`, `accelerate`, `decelerate`, `cycle_camera`, `restart`, `toggle_fullscreen`, `quit`, `rear_view`. Registered in code by `game.gd._register_input()`; keep them byte-identical.
- **Physics tick rate** — `physics_ticks_per_second=160` in `project.godot`. The camera "rigid boom" smoothness depends on this matching the render rate (see `camera_rig.gd` header). Do not change it.
- **Layer numbers** — obstacle physics layer is **2** (1-indexed) in `ship.gd`, `city_chunk.gd`, `traffic.gd`, `project.godot`. CanvasLayer draw order: ScreenFX `30`, HUD `50`, GameOver `100`. Preserve all.
- **Determinism** — `(seed, difficulty) → city` must stay deterministic (see §5.5). Same world seed must produce the same layout every run.
- **The "feel"** — every `@export` tunable and its default value carries over exactly. The M1 flight feel was hand-tuned and signed off (`GAME_SPEC.md` §9 M1). Same numbers in, same feel out.

---

## 2. Prerequisites & environment setup (Phase 0)

Do these before porting any gameplay code, and commit them as the first step.

1. **Branch.** This is a big-bang port (§4); work on a branch so the GDScript version stays as a fallback on `main`.
   ```
   git checkout -b port/csharp
   ```
2. **.NET-enabled Godot 4.6.** The standard Godot binary cannot run C#. Install the **".NET" / Mono build** of Godot 4.6 (AUR: `godot-mono` or the official ".NET" download). The system already has the **.NET SDK** (8/9/10 — confirmed); Godot 4.6 C# targets **`net8.0`**, which is present.
3. **Point `run.sh` at the .NET build.** `run.sh` auto-detects `godot`/`godot4`. Either symlink the .NET build to one of those names, or invoke as `GODOT=godot-mono ./run.sh <cmd>`. **Update `run.sh`** to add a `build` step (C# must be compiled before headless runs):
   - `godot-mono --headless --path . --build-solutions --quit` (or `dotnet build`) before `run.sh check`.
4. **Generate the C# solution.** Open the project once in the .NET editor (it creates `divepunk.csproj` + `divepunk.sln`), or run `--build-solutions`. Confirm `net8.0` in the generated `.csproj`.
5. **`project.godot` autoload paths** — repoint to the C# files (do this in Phase 1, after the autoloads are written):
   ```
   Config="*res://autoload/Config.cs"
   ScoreManager="*res://autoload/ScoreManager.cs"
   AudioManager="*res://autoload/AudioManager.cs"
   ```
6. **`.gitignore`** — add C# build artifacts (keep `divepunk.csproj` and `divepunk.sln` **tracked**):
   ```
   /bin/
   /obj/
   .godot/mono/
   ```
   (The existing `.godot/` and `.mono/` entries already cover most of it.)
7. **Sanity check:** an empty/placeholder C# script builds and the project boots headless before you port real logic.

---

## 3. C# project conventions (new — supersedes the GDScript ones for `.cs` files)

The root `CLAUDE.md` mandates GDScript snake_case. For C# files, use standard Godot-C# conventions instead (update `CLAUDE.md` at the end, §7 step 6):

- **Files & classes:** `PascalCase` — `ship.gd` → `Ship.cs` (class `Ship`). One public class per file, file name == class name.
- **Methods & properties & exports:** `PascalCase` — `get_speed_ratio()` → `GetSpeedRatio()`, `base_speed` → `BaseSpeed`.
- **Private fields:** `_camelCase` — `_forward_speed` → `_forwardSpeed`.
- **Local vars / params:** `camelCase`.
- **Constants:** `PascalCase` or `ALL_CAPS` const — pick one and be consistent (`OBSTACLE_LAYER` → `ObstacleLayer`).
- **Engine lifecycle overrides** keep their underscore: `public override void _Ready()`, `_Process(double delta)`, `_PhysicsProcess(double delta)`, `_Input(InputEvent @event)`, `_UnhandledInput(InputEvent @event)`. (`@event` is escaped because `event` is a C# keyword.)
- Every node-attached class is `public partial class X : BaseType` and **must** be `partial` (the Godot source generator requires it). Add `[GlobalClass]` to classes that GDScript used `class_name` for (`CityChunk`, `ChunkManager`, `TrafficManager`, `HUD`, `ScreenFX`, `GameOverScreen`).
- Keep the explanatory header comments from each `.gd` file — translate them, don't drop them. They encode hard-won design rationale (e.g. why crash detection uses a shape query, not an `Area3D`).

---

## 4. Strategy & order — big-bang on a branch

**Do a big-bang port, not incremental mixed-language.** Mixing C# and GDScript in one project is *possible*, but this codebase is tightly, statically coupled: `game.gd` calls `ScoreManager.reset_run()` as a global, casts `ChunkManagerScene.instantiate() as ChunkManager`, connects typed signals, etc. Cross-language calls would all have to degrade to dynamic `Call("ResetRun")` strings — more work and more fragile than just porting everything. At ~2,200 lines this is a single focused effort.

**Recommended sequence** (dependency order — leaves first, orchestrator last):

| Phase | Files | Notes |
|---|---|---|
| 0 | env setup | §2; commit |
| 1 | `Config.cs`, `ScoreManager.cs`, `AudioManager.cs` | autoloads; repoint `project.godot`; they run at boot so errors surface immediately |
| 2 | `Ship.cs`, `CameraRig.cs`, `ScreenFX.cs`, `Hud.cs`, `GameOver.cs`, `CityChunk.cs` | leaf gameplay + UI |
| 3 | `ChunkManager.cs` (uses `CityChunk`), `Traffic.cs` | |
| 4 | `Game.cs` | the orchestrator; references everything |
| 5 | repoint all 5 `.tscn` script refs (§6); delete `.gd` files | |
| 6 | build clean → `run.sh check` → playtest (§8) → update docs | |

The project **will not compile or run until the big-bang is essentially complete** (because autoloads are repointed and `Game.cs` needs every class). That's expected. Commit a WIP checkpoint once it builds clean, then a second commit once the playtest passes.

---

## 5. The translation rulebook

Concrete GDScript→C# mappings, with the project's own tricky cases called out. (All API names verified against Godot 4.x C#.)

### 5.1 Class skeleton, lifecycle, exports

```gdscript
# ship.gd
extends CharacterBody3D
@export_group("Speed (throttle + inertia)")
@export var base_speed: float = 300.0
@export_range(0.0, 1.0) var fill_chance: float = 0.86
signal speed_changed(speed: float, ratio: float, accelerating: bool)
var _forward_speed: float = 0.0
func _physics_process(delta: float) -> void: ...
```
```csharp
// Ship.cs
using Godot;

public partial class Ship : CharacterBody3D
{
    [ExportGroup("Speed (throttle + inertia)")]
    [Export] public float BaseSpeed = 300.0f;
    [Export(PropertyHint.Range, "0,1")] public float FillChance = 0.86f;

    [Signal] public delegate void SpeedChangedEventHandler(float speed, float ratio, bool accelerating);

    private float _forwardSpeed = 0.0f;

    public override void _PhysicsProcess(double delta) { ... }
}
```
- `@export_group("X")` → `[ExportGroup("X")]`; `@export_subgroup` → `[ExportSubgroup]`.
- `@export_range(a, b)` → `[Export(PropertyHint.Range, "a,b")]`.
- **Float literals need the `f` suffix** (`300.0` → `300.0f`). This matters constantly — see §5.3.

### 5.2 Signals — declare, emit, connect

This project's signals: `Ship` → `SpeedChanged(float,float,bool)`, `NearMiss()`, `Crashed()`; `ScoreManager` → `ScoreChanged(int,float)`, `NearMissRegistered(float)`, `HighScoreChanged(int)`.

```gdscript
signal near_miss                       # no args
near_miss.emit()
_ship.near_miss.connect(_on_ship_near_miss)
```
```csharp
[Signal] public delegate void NearMissEventHandler();          // 'EventHandler' suffix is REQUIRED
EmitSignal(SignalName.NearMiss);                                // generated SignalName helper
_ship.NearMiss += OnShipNearMiss;                              // C# event syntax to connect
```
With args: `EmitSignal(SignalName.SpeedChanged, speed, ratio, accelerating);`.

### 5.3 Float vs double — the #1 porting hazard

GDScript `float` is 64-bit (double). Godot C# uses **`float`** for almost all engine types (`Vector3`, `Color`, `Basis`…), but `_Process`/`_PhysicsProcess` hand you a **`double delta`**. The feel/smoothing math mixes them, so be deliberate:

The framerate-independent smoothing idiom appears everywhere (`ship`, `camera_rig`, `screen_fx`, `game`):
```gdscript
_steer = _steer.lerp(target, 1.0 - exp(-steer_sharpness * delta))
```
```csharp
_steer = _steer.Lerp(target, 1.0f - Mathf.Exp(-SteerSharpness * (float)delta));
```
- Cast `delta` to `float` once where it meets `float` math: `var d = (float)delta;` at the top of the method, then use `d`.
- `Mathf.*` takes/returns `float`; `System.Math.*` takes/returns `double`. Prefer `Mathf` to stay in float.
- Keep the **exact same formula** — `1 - exp(-k·dt)`, never a bare `Lerp(a,b,dt)` (the `CLAUDE.md` rule).

### 5.4 Math & built-ins

| GDScript | C# |
|---|---|
| `clampf/clampi` | `Mathf.Clamp` |
| `maxf/minf/maxi/mini` | `Mathf.Max/Min` |
| `lerpf` | `Mathf.Lerp` |
| `move_toward` | `Mathf.MoveToward` |
| `deg_to_rad` | `Mathf.DegToRad` |
| `roundi` | `Mathf.RoundToInt` |
| `wrapf` | `Mathf.Wrap` |
| `is_equal_approx` | `Mathf.IsEqualApprox` |
| `absf` | `Mathf.Abs` |
| `exp/sin/cos/tan` | `Mathf.Exp/Sin/Cos/Tan` |
| `PI` / `TAU` | `Mathf.Pi` / `Mathf.Tau` |
| `Vector3.UP/ZERO/ONE` | `Vector3.Up/Zero/One` |
| `Color.from_hsv(...)` | `Color.FromHsv(...)` |
| `v.lerp(b, t)` | `v.Lerp(b, t)` |
| `basis.scaled(s)` / `Basis.from_euler` / `Basis.looking_at` | `basis.Scaled(s)` / `Basis.FromEuler` / `Basis.LookingAt` |
| `print` / `push_warning` | `GD.Print` / `GD.PushWarning` |

### 5.5 RNG & determinism — read carefully

`city_chunk.gd` and `traffic.gd` rely on seeded `RandomNumberGenerator`. Godot's RNG is PCG32 and **identical across GDScript and C# for the same seed**, so determinism survives the port **if** you preserve two things:

**(a) The seed-mix integer width.** `_mix_seed` uses 64-bit wrapping integer arithmetic. GDScript `int` is 64-bit and overflow wraps. **C# `int` is 32-bit** and would overflow differently → a different city. Use **`long` + `unchecked`**, then cast to `ulong` for `.Seed`:
```csharp
private static long MixSeed(long baseSeed, long idx, long salt)
{
    unchecked
    {
        long h = baseSeed * 73856093L;
        h ^= idx * 19349663L;
        h ^= (idx >> 3) * 83492791L;   // >> on long is arithmetic shift, matching GDScript
        h ^= salt * 50331653L;
        return h;
    }
}
// usage:
var rng = new RandomNumberGenerator();
rng.Seed = (ulong)MixSeed(baseSeed, pIndex, 0);
```
`RandomNumberGenerator.Seed` is `ulong`; `_random_seed()` → `(ulong)((long)rng.Randi() + 1)`.

**(b) RNG call order & count.** `randf()`/`randf_range()`/`randi()` advance the same state, so the C# loops must consume the RNG in the **exact same order and number** as the GDScript (same nested `for side in [-1,1] → col → row`, same `randf()` before `randf_range(...)`, etc.). Port the generation bodies line-for-line; do not reorder or "optimise" RNG calls.

> Note: exact *floating-point* positions may differ by ulps because `Randf()` returns 32-bit `float` in C# vs 64-bit in GDScript, but the **structural** layout (which slots fill, size class, silhouette, obstacle count — all threshold comparisons) is preserved. That's what "same city" means here; pixel-exact cross-language match is not a requirement.

RNG API: `new RandomNumberGenerator()`, `.Seed = ...`, `.Randf()`, `.RandfRange(a,b)`, `.Randi()`, `.Randomize()`.

### 5.6 Autoload access — singleton pattern

GDScript reaches autoloads two ways; both need translating.

**Direct global** (`game.gd`, `hud.gd`: `ScoreManager.reset_run()`, `ScoreManager.score_changed.connect(...)`). C# has no autoload globals — add a static `Instance` set in `_Ready()`:
```csharp
public partial class ScoreManager : Node
{
    public static ScoreManager Instance { get; private set; }
    public override void _Ready() { Instance = this; /* ... */ }
}
// callers:
ScoreManager.Instance.ResetRun();
ScoreManager.Instance.ScoreChanged += OnScoreChanged;
```
Autoloads `_Ready` in declared order (Config → ScoreManager → AudioManager) *before* the main scene, so `Instance` is always set by the time `Game._Ready()` runs.

**Guarded optional lookup** (`game.gd`: `get_node_or_null(^"/root/AudioManager")` + `has_method` + `call(...)`, and the `_cfg_value` helper). Audio is intentionally optional. Replace the stringly-typed calls with null-safe singleton calls:
```csharp
AudioManager.Instance?.NearMiss();     // was _audio_call(&"near_miss")
```

**Config helper.** `Config.get_value` returns a `Variant`. Add typed helpers so the many call sites in `Game.cs` stay clean:
```csharp
public Variant GetValue(string s, string k, Variant d) => _cfg.GetValue(s, k, d);
public float  GetFloat (string s, string k, float  d) => _cfg.GetValue(s, k, d).AsSingle();
public int    GetInt   (string s, string k, int    d) => _cfg.GetValue(s, k, d).AsInt32();
public bool   GetBool  (string s, string k, bool   d) => _cfg.GetValue(s, k, d).AsBool();
public string GetString(string s, string k, string d) => _cfg.GetValue(s, k, d).AsString();
```
`game.gd`'s `_cfg_value("ship","base_speed", ship.base_speed)` → `Config.Instance.GetFloat("ship", "base_speed", ship.BaseSpeed)`.

### 5.7 Nodes, paths, StringNames

- `get_node_or_null(^"Ship") as CharacterBody3D` → `GetNodeOrNull<CharacterBody3D>("Ship")`.
- `&"action"` (StringName) / `^"Path"` (NodePath) → plain string literals work in most C# APIs (implicit conversion). For the **hot input reads** in `Ship._PhysicsProcess`, optionally cache `private static readonly StringName Accelerate = "accelerate";` to avoid per-frame allocations.
- `add_to_group(&"obstacle")` → `AddToGroup("obstacle")`; `set_collision_layer_value(2, true)` → `SetCollisionLayerValue(2, true)`.
- Node props are PascalCase: `.Visible`, `.Position`, `.GlobalPosition`, `.Name`, `.Rotation`, `.Scale`, `.Velocity`, `MoveAndSlide()`.

### 5.8 `preload` / `load` / instantiate

C# has no `preload`. `game.gd`'s preloaded scenes become cached `GD.Load`:
```csharp
private static readonly PackedScene ChunkManagerScene = GD.Load<PackedScene>("res://scenes/world/ChunkManager.tscn");
var mgr = ChunkManagerScene.Instantiate<ChunkManager>();
```
The preloaded *scripts* (`ShipScript`, `CameraRigScript`, `TrafficManagerScript`, `ScreenFXScript`) used `set_script` on a `new` node. In C# you simply construct the typed class — **no `set_script`**:
```gdscript
_ship = CharacterBody3D.new(); _ship.set_script(ShipScript); _apply_ship_config(_ship); add_child(_ship)
```
```csharp
_ship = new Ship { Name = "Ship" };
ApplyShipConfig(_ship);     // set exported props BEFORE AddChild so _Ready() sees them
AddChild(_ship);
_ship.GlobalPosition = new Vector3(0f, 30f, 0f);
```
`preload("res://shaders/x.gdshader")` → `GD.Load<Shader>("res://shaders/x.gdshader")` (cache in a `static readonly` field).

### 5.9 Collections

- `Array[Transform3D]` → `Godot.Collections.Array<Transform3D>` *only if* it must cross the engine boundary; for pure in-C# layout work, prefer `System.Collections.Generic.List<Transform3D>` (faster, less marshalling) and convert at the MultiMesh upload.
- `PackedColorArray` / `PackedInt32Array` / `PackedFloat32Array` / `PackedByteArray` → C# `Color[]` / `int[]` / `float[]` / `byte[]`.
- `Dictionary` for the near-miss tracker (`_near_now: Dictionary` keyed by instance id) → **`HashSet<ulong>`** (`GetInstanceId()` returns `ulong`):
  ```csharp
  var current = new HashSet<ulong>();
  // ... if (!_nearNow.Contains(id)) EmitSignal(SignalName.NearMiss);
  _nearNow = current;
  ```

### 5.10 Physics shape query (`ship.gd._check_obstacles`)

```csharp
var space = GetWorld3D().DirectSpaceState;
var xform = new Transform3D(Basis.Identity, GlobalPosition);
_crashQuery.Transform = xform;
if (space.IntersectShape(_crashQuery, 1).Count > 0) { Crash(); return; }

_nearQuery.Transform = xform;
Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(_nearQuery, 8);
foreach (Godot.Collections.Dictionary h in hits)
{
    var col = h["collider"].As<GodotObject>();
    if (col == null) continue;
    ulong id = col.GetInstanceId();
    current.Add(id);
    if (!_nearNow.Contains(id)) EmitSignal(SignalName.NearMiss);
}
```
`PhysicsShapeQueryParameters3D`: `.Shape`, `.CollisionMask`, `.CollideWithBodies`, `.CollideWithAreas`, `.Transform`. The mask `1 << (OBSTACLE_LAYER - 1)` ports verbatim.

### 5.11 MultiMesh (`city_chunk.gd`)

```csharp
var mm = new MultiMesh
{
    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
    UseColors = true,        // MUST be set before InstanceCount
    Mesh = meshes[k],
};
mm.InstanceCount = ts.Count;
for (int i = 0; i < ts.Count; i++)
{
    mm.SetInstanceTransform(i, ts[i]);
    mm.SetInstanceColor(i, cs[i]);
}
```
Static caches (`static var _building_meshes`, `_building_mat`, `static func _get_building_meshes`) → C# `static` fields/methods. Mind static init order; lazy-init exactly as the GDScript does (null check then build).

### 5.12 Audio synthesis (`audio_manager.gd`)

All SFX/music are synthesised into buffers at boot. `AudioStreamWAV` → C# **`AudioStreamWav`**:
```csharp
var w = new AudioStreamWav
{
    Format = AudioStreamWav.FormatEnum.Format16Bits,
    MixRate = MixRate,
    Stereo = false,
};
var bytes = new byte[samples.Length * 2];
for (int i = 0; i < samples.Length; i++)
{
    short s = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
    bytes[i * 2]     = (byte)(s & 0xFF);          // little-endian, matches encode_s16
    bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
}
w.Data = bytes;
// loop: w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = samples.Length;
```
`linear_to_db` → `Mathf.LinearToDb`. `AudioStreamPlayer`: `.Stream`, `.VolumeDb`, `.Play()`, `.Playing`, `.Stop()`. The synth loops are float-heavy — keep them as `float[]` and mind §5.3.

### 5.13 UI (`hud.gd`, `game_over.gd`, `screen_fx.gd`)

Built entirely in code. Straight renames:
- `add_theme_font_size_override("font_size", 44)` → `AddThemeFontSizeOverride("font_size", 44)`.
- `add_theme_color_override("font_color", c)` → `AddThemeColorOverride("font_color", c)`.
- `set_anchors_preset(Control.PRESET_FULL_RECT)` → `SetAnchorsPreset(Control.LayoutPreset.FullRect)`.
- `mouse_filter = Control.MOUSE_FILTER_IGNORE` → `MouseFilter = Control.MouseFilterEnum.Ignore`.
- `create_tween().tween_property(_mult_label, "scale", Vector2.ONE, 0.25)` → `CreateTween().TweenProperty(_multLabel, "scale", Vector2.One, 0.25);`.
- `HORIZONTAL_ALIGNMENT_CENTER` → `HorizontalAlignment.Center`; `BoxContainer.ALIGNMENT_CENTER` → `BoxContainer.AlignmentMode.Center`.
- The `_make_meter` / `_label` helpers returning `Dictionary` → return a small C# struct/tuple `(ColorRect Fill, Label Val)` instead of a Godot `Dictionary`.

### 5.14 Misc

- `match` → `switch`. Unnamed `enum {CLS_LOW, ...}` / `{SHP_BOX, ...}` → C# `enum BuildingClass { Low, Mid, High, Mega }`, `enum Silhouette { Box, Round, Prism, Taper }` (cast `(int)` where used as MultiMesh indices — **index order must match `_get_building_meshes`**).
- `FileAccess` (`ScoreManager` save): `FileAccess.FileExists(path)`, `FileAccess.Open(path, FileAccess.ModeFlags.Read)`, `.Get32()` (returns `uint`, cast to `int`), `.Store32((uint)v)`, `.Close()`. `user://highscore.save` path unchanged.
- `ConfigFile`: `new ConfigFile()`, `.Load(path)` returns `Error` (`Error.Ok`), `.GetValue(s,k,d)` returns `Variant`.
- `Engine.time_scale`/`max_fps` → `Engine.TimeScale`/`Engine.MaxFps`. `DisplayServer.get_name()` → `DisplayServer.GetName()`. `get_tree().reload_current_scene()/quit()` → `GetTree().ReloadCurrentScene()/Quit()`.
- `env.set("glow_levels/%d" % lvl, true)` → `env.Set($"glow_levels/{lvl}", true);`.
- Input map registration: `new InputEventKey { PhysicalKeycode = Key.A }`, `new InputEventMouseButton { ButtonIndex = MouseButton.Middle }`. Keycodes via the `Key` enum (`Key.A`, `Key.Left`, `Key.Shift`, `Key.Space`, `Key.Ctrl`, `Key.Escape`, …).

---

## 6. Scenes — repoint script references (Phase 5)

The 5 `.tscn` files reference their root script by path/UID. **First read each `.tscn`** to confirm its node tree and current script ext_resource. Then repoint:

| Scene | Script ref to change |
|---|---|
| `scenes/main/Main.tscn` | `scripts/game.gd` → `Game.cs` |
| `scenes/world/ChunkManager.tscn` | `scripts/chunk_manager.gd` → `ChunkManager.cs` |
| `scenes/world/CityChunk.tscn` | `scripts/city_chunk.gd` → `CityChunk.cs` |
| `scenes/ui/HUD.tscn` | `scripts/hud.gd` → `Hud.cs` |
| `scenes/ui/GameOver.tscn` | `scripts/game_over.gd` → `GameOver.cs` |

Two ways (pick per your tooling): **(a)** open the project in the .NET editor, and for each scene re-attach the C# script to the root (regenerates the UID cleanly — safest); **(b)** edit the `.tscn` text: change the `ext_resource` `path` to the `.cs` and let Godot regenerate the UID on import. After repointing, verify each scene still loads.

> If any `.tscn` stored exported values inline (e.g. `base_speed = ...`), rename those keys to the C# property (`BaseSpeed = ...`). Most tunables here are applied in code (`Game.ApplyShipConfig` etc.), so this is unlikely — but check while reading the scenes.

Keep the script *file* path under `scripts/` and `autoload/` as today, or move to a flat layout — your call, but update scene refs and `project.godot` accordingly. Simplest: mirror the existing folders (`scripts/Ship.cs`, `autoload/Config.cs`).

---

## 7. File-by-file checklist

Autoloads (`autoload/`, all `extends Node`):
- [ ] **`Config.cs`** — add `Instance`; `GetValue` + typed helpers (§5.6); `_apply_display` verbatim (window mode `switch`, vsync, `Engine.MaxFps`). Keep the headless guard.
- [ ] **`ScoreManager.cs`** — `Instance`; 3 signals (§5.2); exports (`NearMissValue`, `ComboStep`, `ComboMax`, `ComboTimeout`, `ComboDecay`); `FileAccess` save/load (§5.14). Pure logic — easiest file; do it first as the template.
- [ ] **`AudioManager.cs`** — `Instance`; optional-by-design (callers use `Instance?.X()`); audio synth buffers (§5.12); SFX round-robin voices; config-driven levels.

Gameplay (`scripts/`):
- [ ] **`Ship.cs`** (`CharacterBody3D`) — the feel-critical file. Throttle+inertia, steering smoothing (§5.3), corridor clamp, visual bank/pitch, shape-query crash + near-miss (§5.10), all exports + signals. **Match the M1-tuned numbers exactly.**
- [ ] **`CameraRig.cs`** (`Node3D`) — rigid orbit boom, mouse free-look in `_Input`, distance levels, speed→FOV, shake. Smoothing math per §5.3.
- [ ] **`CityChunk.cs`** (`[GlobalClass] Node3D`) — **determinism-critical** (§5.5): `MixSeed` as `long`, faithful RNG order in `ComputeBuildingLayout`/`_generate_obstacles`. MultiMesh per silhouette (§5.11), static mesh/material caches, obstacle pool, enums (§5.14).
- [ ] **`ChunkManager.cs`** (`[GlobalClass] Node3D`) — ring/object pool, `_initial_fill`, amortised `builds_per_frame`, `_slot_for` negative-safe modulo. Uses `CityChunk`.
- [ ] **`Traffic.cs`** (`[GlobalClass] Node3D`, class `TrafficManager`) — **determinism-sensitive**; pooled moving cars on the obstacle layer, `_PhysicsProcess` stepping + recycle. Watch per-frame allocations (§9).
- [ ] **`Hud.cs`** (`[GlobalClass] CanvasLayer`, `layer=50`) — code-built UI, polls ship + subscribes to `ScoreManager` signals; tween pop.
- [ ] **`ScreenFX.cs`** (`[GlobalClass] CanvasLayer`, `layer=30`) — fullscreen shader passes, eased drivers, flash.
- [ ] **`GameOver.cs`** (`[GlobalClass] CanvasLayer`, class `GameOverScreen`, `layer=100`) — code-built overlay.
- [ ] **`Game.cs`** (`Node3D`, attached to `Main.tscn` root) — **port last**. Input registration, scene self-assembly (`new Ship()` etc., §5.8), `WorldEnvironment`/sky build, signal wiring, score driving, time-dilation, optional audio calls, all the `ApplyXConfig` methods.

Final:
- [ ] Repoint 5 scenes (§6); delete all `.gd` files under `scripts/` + `autoload/`.
- [ ] Update **`CLAUDE.md`**: Language → C# (.NET 8), conventions → §3, run/build commands. Update **`GAME_SPEC.md` §3** (primary language) to reflect the switch (note the rationale; keep gdext as the optional accelerator).

---

## 8. Verification — definition of done

A port is done only when **all** pass:

1. **Builds clean** — `dotnet build` (or `--build-solutions`) with **zero errors** (warnings OK, but review them).
2. **Headless boot** — `./run.sh check` (import + 180-frame boot + quit) exits **0**, with no script errors/red lines in output. Autoloads print their expected lines (e.g. `[DIVEPUNK] world seed = …`).
3. **No GDScript left** — no `.gd` files remain; `project.godot` autoloads point to `.cs`; all 5 scenes load.
4. **Playtest parity** (`./run.sh play`) — walk the whole loop:
   - Ship throttles up/down with inertia; steering feels the **same** as before (this is the make-or-break check — compare against `main`).
   - Camera orbit (mouse), distance cycle (Q), rear-view glance (middle mouse), speed→FOV all work.
   - City streams endlessly with no hitches; buildings varied (classes + silhouettes); fog/glow/SSR/neon look unchanged.
   - Obstacles (amber) and traffic (magenta) appear; **crash** ends the run; **near-miss** awards points + flash + slow-mo dip + whoosh.
   - HUD: score, multiplier (combo pop), best, throttle + SPD + V-SPD bars all update.
   - Audio: music bed + near-miss/boost/crash SFX play.
   - **Crash → Game Over → press R → instant restart**, score zeroes, high score persists across runs (`user://highscore.save`).
5. **Determinism** — set a fixed `[game] seed` in `settings.cfg`; two runs produce the **same city layout** (same skyline, same obstacle/traffic placement at matching positions).
6. **Frame rate** — desktop holds 60 FPS at the default view distance (no regression vs `main`).

If feel or visuals differ, diff your C# against the `.gd` on `main` for that subsystem — the usual culprits are §5.3 (float/double) and §5.5 (RNG order/width).

---

## 9. divepunk-specific risks & gotchas

- **GC in hot paths.** C# allocations in `_Process`/`_PhysicsProcess` cause GC spikes that GDScript didn't have. Watch: `Traffic._PhysicsProcess` (no per-car allocation — reuse), `CityChunk` generation (build into reused `List`s; avoid LINQ in the per-instance loops), audio synth (boot-time only, fine). Prefer `List<T>`/arrays over `Godot.Collections` inside tight loops; convert only at the engine boundary.
- **Float/double feel drift** (§5.3) — the single most likely cause of "it feels slightly off." Be disciplined with the `(float)delta` cast and `Mathf` (not `Math`).
- **RNG determinism** (§5.5) — `int` overflow in `MixSeed` is a silent city-changer. Use `long` + `unchecked`.
- **`partial` + `[GlobalClass]`** — forget `partial` and the source generator fails cryptically; forget `[GlobalClass]` on the former `class_name` types and scenes/Inspector won't see them.
- **Signal `EventHandler` suffix** — the delegate **must** end in `EventHandler` or the generator won't emit the `SignalName`/event.
- **Autoload `Instance` timing** — only valid after that autoload's `_Ready`. Fine for `Game` (runs later), but don't reference another autoload's `Instance` from within an autoload constructor; use `_Ready`.
- **Mobile C# export (deferred, M5).** C# Android export works in Godot 4.6 but has more moving parts than GDScript (export templates, AOT considerations). Mobile is desktop-first/later per project decision — **just validate the Android export when you reach M5**, and keep this caveat in mind since "mobile/export friction" was a motivation for the language move.
- **Don't fix bugs mid-port.** If the GDScript has a latent bug, port it faithfully and log it for a follow-up; changing behaviour here makes parity impossible to verify.

---

## 10. Rollback

Everything is on the `port/csharp` branch; `main` keeps the working GDScript build. If the port stalls, `git checkout main` is the instant fallback. Commit a checkpoint the moment the C# build first goes green (before playtest), so you never lose a compiling state.
