# DIVEPUNK — Project Spec & Roadmap

> **Working title:** DIVEPUNK *(verify store, trademark, and domain availability before commercial launch)*
> **Genre:** Open-world flying-car sandbox set in a fixed, hand-reproducible cyberpunk coastal city at dusk.
> **Status:** Reframing in progress (2026-06). This document is the north star — implement it task by task (see [Roadmap](#9-development-roadmap)).
> **How to use this file:** Keep it in `docs/GAME_SPEC.md` and reference it from a lean `CLAUDE.md`.

> **⚠️ This is a reframe.** DIVEPUNK began as an *infinite arcade score-corridor flyer* (streamed neon
> corridor, 3-DOF tube-clamped ship, crash = game over, distance/combo scoring). It is being rebuilt
> into a **bounded, pre-baked open-world flying-car sandbox**. Much of the old code is reused (city
> geometry, shaders, camera, audio, config); the corridor, streaming, scoring, and game-over are gone.
> See the [Reuse/rewrite/delete](#8-migration--reuse--rewrite--delete) table.

---

## 1. Vision & Differentiation

You own a flying car in a **fixed, finite, lovingly-built cyberpunk city on a California-like ocean
coast at golden-purple dusk.** You can fly **anywhere** — thread the neon canyons downtown, skim the
bay, buzz the islands, climb to the desert hills ringing the basin. The car obeys **real physics**:
bump a tower or the ground and you take **measurable damage and bounce off** — but you are never
killed, never "game-over'd," never kicked back to a menu. The world is a **place you inhabit**, not a
gauntlet you survive.

**The shift from the original vision.** The old DIVEPUNK was *Race the Sun* / *Distance* energy —
fast, run-based, score-chasing, fail-and-retry. The reframe trades that for **presence and freedom**:
a persistent, reproducible world you explore at your own pace. Speed and neon stay in the DNA; the
fail-loop and the procedural-novelty treadmill do not. Deeper gameplay (objectives, economy, traffic
AI, mission structure) layers on top later — the foundation is *a great-feeling car in a beautiful,
solid, open world.*

**Design north star:** *Flying around this city should feel good enough that you do it for its own sake.*

---

## 2. Platforms & Constraints

| | Detail |
|---|---|
| **Primary platform** | Linux desktop (Forward+), fastest iteration |
| **Stretch platforms** | Windows, macOS (low-effort from this stack) |
| **Android** | Deferred to a mature stage (the open-world memory/draw budget is a separate effort from the desktop look) |
| **Orientation** | Landscape |
| **Target framerate** | 60 FPS desktop on the dev machine (RTX-class) |

> The world is **fixed and finite (≈8×8 km)**, so the perf model is "render a known city cheaply,"
> not "stream an infinite one." That makes draw-call batching (MultiMesh) and distance culling the
> levers, with fog hiding the cull boundary.

---

## 3. Technology Stack

- **Engine:** **Godot 4.6** (stable, Jan 2026). MIT-licensed → zero fees/royalties.
- **Language:** **C# (.NET 8 / `net8.0`)**, typed throughout. Needs the **.NET/Mono build** of Godot
  (`godot-mono`). Ported from typed GDScript — conventions in `docs/MIGRATION_GDSCRIPT_TO_CSHARP.md`.
- **Physics:** **Jolt** (the Godot 4.6 default). Now used for **real rigid-body simulation** of the
  flying car (collision *response*, bounce, contact impulses), not just detection. Jolt allows
  non-uniform `HeightMapShape3D` scaling (GodotPhysics does not) — relevant for terrain later.
- **Rendering:** **Forward+** on desktop/Linux.
- **3D models:** objects (buildings, cars, props) are **procedurally generated placeholders now**, and
  will be swapped to **glTF 2.0 (`.glb`)** model files later through a `MeshRegistry` seam — **without
  re-baking the world layout** (the bake stores placement + type, not geometry).
- **Optional native acceleration:** Rust via godot-rust/gdext — *only if* a profiler proves C# is the
  bottleneck. Do not start here.
- **Version control:** Git from commit zero.

---

## 4. The World

A single, authored, **deterministic-from-seed** world, **generated once and saved to disk**, then
loaded at runtime. No per-run regeneration, no streaming ring buffer.

- **Size:** **≈8×8 km**, bounded, with **soft borders** (a restoring push-back force past the edge —
  no invisible wall snap, no death).
- **Layout (one coherent basin):**
  - **City** on the **ocean coast** — the dense neon downtown the old generator already knows how to build, now placed on an open ground plane instead of along a corridor.
  - **Ocean** with a **bay** carved into the coastline and **several islands**.
  - **California-like desert** ringing the city: scrub flats rising to **hills/mountains on the horizon** (a raised rim that also reads as the world's natural boundary).
  - **Beaches / coastline** transitioning city → water and desert → water.
- **Sea level = world Y = 0.** Terrain below 0 is underwater; the city sits on a plateau above it.
- **Biomes** (ocean, beach, city plateau, desert, hills) drive terrain colour/material and where
  objects are placed.

### The bake (data, not geometry)
The world is serialized as a **binary Godot `Resource` (`res://world/world_8km.res`)** holding
**layout data**, never baked meshes:
- a **terrain heightmap** (normalised heights + resolution + min/max Y),
- a coarse **biome map**,
- an array of **object descriptors** (type + `Transform3D` + shader-channel colours + sub-seed).

At runtime the loader instantiates meshes from those descriptors via a swappable
**`ObjectType → Mesh/PackedScene` registry** — so upgrading a building from a procedural box to a glTF
model is a one-line registry change with **zero re-bake**. `FormatVersion` guards stale bakes.

---

## 5. Core Experience & Pillars

*(The reframe is young; deeper gameplay is deferred. These are the foundations that must feel right.)*

1. **Great-feeling flight [now].** An assisted-arcade `RigidBody3D` flying car: thrust + 6 degrees of
   freedom, but self-stabilizing (auto-leveling, coordinated banked turns) so it's a joy to fly, not a
   wrestling match. *If the car doesn't feel great in an empty void, nothing else matters — build and
   tune this first (the original "M1 gate" still rules).*
2. **Solid, physical world [now].** Buildings, terrain, and the ground are **collidable**. Contact is
   resolved by Jolt as a **realistic bounce**, scaled by impact.
3. **Damage, not death [now].** Collisions accumulate **measurable damage** (a condition read-out on
   the HUD) from contact impulse. **Health reaching zero does nothing yet** — no game-over, no reload.
   Consequences (handling loss, forced landing, repair) come later.
4. **A real place [now].** A **fixed, reproducible** city — the same world every launch — that rewards
   learning its geography. Bounded and knowable, not an infinite treadmill.
5. **Freedom of movement [now].** Fly in **all directions**, land anywhere, hover, skim the water,
   crest the hills. No corridor, no rails, no forced forward motion.
6. **Synthwave-dusk mood [now, polished later].** Golden-purple sunset over the bay; neon still glows;
   desert and water stay warm and legible. Bridges cyberpunk and California.
7. **Ambient life [soon].** Sparse AI traffic roaming the city — collidable, not a score hazard.
8. **Deeper gameplay [later].** Objectives, economy, missions, factions, traffic behaviour, day/night
   — all TBD once the sandbox feels good. Intentionally unscoped here.

---

## 6. Current Scope

**In scope (the reframe foundation):**
- The bounded ≈8×8 km world, **baked once to disk** and loaded at runtime.
- Coastal city (procedural placeholder geometry) + terrain (ocean/bay/islands/desert/hills) + water.
- An assisted-arcade `RigidBody3D` flying car with full 6-DOF and **collision + bounce + damage**.
- A condition (damage) HUD read-out. **No game-over.**
- Synthwave-dusk aesthetic.
- Sparse ambient AI traffic.
- A glTF model-loading seam (proven on one or two object types).
- Runs at 60 FPS on the dev desktop.

**Out of scope (for now):** scoring/combos/leaderboards (removed), objectives/missions, economy,
narrative, day/night cycle, weather, interiors, multiplayer, Android, monetization, real art assets
(beyond proving the glTF seam).

**Definition of Done (reframe foundation):** you can launch the game, fly a physics-driven car freely
around a beautiful, fixed, solid 8 km coastal city at dusk, bump into things and take damage + bounce
without ever being "killed," and the world is identical every time you launch.

---

## 7. Technical Architecture

### 7.1 World bake pipeline — *data, not geometry*
- **`WorldData : Resource` `[GlobalClass]`** — `FormatVersion`, `Seed`, `WorldExtent` (≈8000 m), a
  terrain heightmap (`float[] Heights` 0–1 + `HeightmapResolution` + `MinY`/`MaxY`), a `byte[] Biomes`
  map, and `Array<PlacedObject> Objects`.
- **`PlacedObject : Resource` `[GlobalClass]`** — `ObjectType` enum, `Transform3D Xform` (its basis
  carries footprint/height as non-uniform scale), `Color Tint` (→ MultiMesh `COLOR`), `Color Custom`
  (→ `INSTANCE_CUSTOM`, reusing the existing building shader's variation/grid/accent channels
  **verbatim**), `Flags`, `SubSeed`. *(If `Array<PlacedObject>` load cost bites at full density, swap to
  parallel typed arrays behind the same loader API.)*
- **`WorldGenerator`** — one **pure-C#** `Build(seed, extent)` producing a `WorldData`. Deterministic:
  identical seed → byte-identical bake. (Carries forward the determinism discipline from the old city
  gen — see [§7.7](#77-determinism).)
- **Bake runners** — a `WorldBakeTool : EditorScript` (`[Tool]`) that calls `ResourceSaver.Save(...,
  Compress)`, **and** a headless `run.sh bake` path (saving *data* needs no GPU → CI-safe).
- **File location** — `res://world/world_8km.res` (tracked authored content, shipped in the PCK).
  Player-generated worlds would later go to `user://`; the loader takes a path either way.

### 7.2 Runtime world load + tile-grid rendering
- **`WorldLoader`** — `ResourceLoader.Load<WorldData>(...)`, builds terrain, **buckets `Objects` into a
  fixed world-tile grid once** (≈250 m tiles, ≈32×32), and lazily gives near tiles colliders.
- **WorldTile** — the direct re-bind of the old `CityChunk`: "a square of the **fixed** world populated
  from `WorldData`" instead of "a corridor slice from live RNG." Holds its terrain mesh, one
  **`MultiMeshInstance3D` per `ObjectType`** (reusing `CityChunk`'s batched-upload + shader machinery),
  and lazy colliders. **Tiles never rebuild or recycle** (the world is static) — a big simplification
  over the old streaming ring.
- **`MeshRegistry`** — `ObjectType → Mesh/PackedScene`. The **glTF swap seam**: change one entry from a
  procedural `BoxMesh` to an imported `.glb` and the whole type re-skins, no re-bake.
- **Culling/LOD** — per-`GeometryInstance3D` `VisibilityRangeBegin/End` (fade mode `Self`); **fog hides
  the cull boundary** exactly as it hid the old chunk draw distance. All tile MultiMeshes stay resident
  (cheap, frustum-culled); **colliders instantiate only within a small physics radius** around the car.

### 7.3 Terrain, ocean, biomes
- **Terrain mesh** — a grid of tiles, each a `MeshInstance3D` whose `ArrayMesh` (via `SurfaceTool`) is
  displaced from the heightmap slice, with per-vertex `COLOR` from the biome map and one dusk material;
  per-tile visibility-range LOD.
- **Heightmap generation** — layered FBM noise + analytic masks: a radial edge-lift (mountain ring), a
  coastline curve, a signed-distance-carved bay, island bumps, and a flattened city-plateau polygon.
- **Collision** — **per-tile trimesh static colliders near the car first** (`Mesh.CreateTrimeshShape()`);
  a single Jolt-scaled `HeightMapShape3D` is a later optimization.
- **Water** — a new `shaders/water.gdshader` on an ≈8 km plane at Y=0 (dusk ripple + fresnel + SSR),
  `[fx]`-gated, **visual-only for now**.

### 7.4 The flying car — `RigidBody3D`, assisted-arcade, 6-DOF
- **Body** — `RigidBody3D` + box `CollisionShape3D`; `GravityScale = 0` (hover via altitude assist);
  `ContactMonitor = true`, `MaxContactsReported ≈ 8`; tuned linear/angular damping; Jolt.
- **Control** (forces; every gain `[Export]`ed for live tuning):
  - **Thrust** along `-Basis.Z` via `ApplyCentralForce` (keeps the old throttle + inertia *intent*).
  - **Pitch / yaw / roll torques** via `ApplyTorque`.
  - **PD auto-leveling** (`-kP·error − kD·angularVelocity`) — the "assisted" feel; the car self-rights
    when you release the stick.
  - **Coordinated banked turns** — yaw input adds proportional roll so turns feel like flying.
  - **Speed clamp** on `LinearVelocity`; **hover / altitude assist** to hold height without input.
- **Bounce** — let Jolt resolve impacts (no clamp/teleport); tune the `PhysicsMaterial` bounce; the
  control forces re-stabilize after a hit (tumble-then-recover).
- **Damage** — override `_IntegrateForces(PhysicsDirectBodyState3D state)`, sum
  `state.GetContactImpulse(i).Length()` above a threshold, forward to the damage component;
  `GetContactColliderObject(i)` distinguishes terrain / building / traffic.
- **Borders** — a soft restoring force past `±WorldExtent/2` (no hard clamp, no death).
- **Preserved API** — `GetSpeed()`, `GetSpeedRatio()`, `GetVerticalSpeed()` stay, so `CameraRig`, the
  HUD, and audio are untouched. **Removed** — corridor clamp, `IntersectShape` crash/near-miss,
  `MoveAndSlide`, climb-angle, `Crashed`/`NearMiss` signals.

### 7.5 Damage system — `DamageComponent`
A signal-driven child of the car: `MaxHealth`, `ImpulseToDamage`, `MinImpulseThreshold`, optional
`RepairRate`; `ApplyImpact(impulse)`; emits `HealthChanged` / `Damaged` → HUD condition bar.
**Health at zero does nothing** — no death, no reload. (Consequences are deferred, deeper gameplay.)
Unsubscribe from any autoload signals in `_ExitTree` (the C# rule).

### 7.6 Aesthetic — synthwave dusk
Reuse the existing `WorldEnvironment` stack (glow/bloom, exponential + volumetric fog, SSR, ACES
tonemap) and the procedural sky shader, **retuned from neon-night to a warm golden-purple sunset** so
the desert, water, and coastline read while neon still pops. All effects stay `[fx]`-gated. The
existing `building.gdshader` lays its windows out in **world space**, so it renders correctly for
fixed-grid placement with **no shader change**.

### 7.7 Determinism
The bake must be reproducible: identical `(seed, extent)` → byte-identical `world_8km.res`. Keep
seeded-RNG call order/count stable; the seed-mix uses `long` + `unchecked` (C# `int` overflows
differently from GDScript); match half-away-from-zero rounding where it feeds loop counts (C#
`Math.Round` is banker's). This matters once for the bake, not per-frame.

### 7.8 Input (abstract it!)
All gameplay reads **`InputMap` actions**, never raw keys. The flight set extends to 6-DOF —
pitch/yaw via the primary steer actions, plus **roll** (and any strafe/vertical assists) as new
actions. Camera free-look stays on the mouse. (Touch input is an Android-stage concern.)

---

## 8. Migration — reuse / rewrite / delete

| Action | Files | Why |
|---|---|---|
| **Reuse as-is** | `scripts/CameraRig.cs`, `autoload/Config.cs`, `shaders/{building,hazard,beacon,post,speed_lines}.gdshader`, `scenes/main/Main.tscn` | Camera/config/shaders are model-agnostic; the building shader is world-space already. |
| **Retune** | `Game.cs` environment/sky builders, `shaders/sky.gdshader`, `autoload/AudioManager.cs` (drop score cues), `scripts/ScreenFX.cs` (flash on *impact*, not near-miss) | Night → dusk; juice repurposed from scoring to collisions. |
| **Rewrite** | `scripts/Ship.cs` (→ `RigidBody3D` 6-DOF + impulse damage), `scripts/Game.cs` (orchestration around `WorldLoader`, no score/corridor), `scripts/Hud.cs` (condition bar, not score/combo), `scripts/Traffic.cs` (fixed roaming population) | Core mechanics change. |
| **Harvest → delete** | `scripts/CityChunk.cs` (its MultiMesh/shader/mesh factories migrate to `TileBuilder`/`MeshRegistry`/`WorldGenerator`) | The geometry knowledge is gold; the corridor framing is not. |
| **Delete** | `scripts/ChunkManager.cs`, `scripts/GameOver.cs`, `autoload/ScoreManager.cs` (+ its `project.godot` autoload entry), `scenes/world/ChunkManager.tscn`, `scenes/ui/GameOver.tscn` | Streaming, game-over, and scoring are gone. |
| **Create** | `scripts/world/{WorldData,PlacedObject,WorldGenerator,WorldLoader,TileBuilder,MeshRegistry,WorldBakeTool}.cs`, `scripts/DamageComponent.cs`, `shaders/water.gdshader`, `res://world/world_8km.res` | The new pipeline. |
| **Edit** | `settings/settings.cfg` (new `[world]`/`[flight]`/`[damage]`; drop `[corridor]`/`[streaming]`/score keys), `project.godot` (drop ScoreManager autoload), `run.sh` (add `bake`) | New sections/commands. |
| **Keep** | `docs/MIGRATION_GDSCRIPT_TO_CSHARP.md` | Still the valid C# conventions rulebook. |

---

## 9. Development Roadmap

Ordered by **risk and dependency**: rewrite the docs, strip the old loops, **re-prove the flight feel
in a void**, then build the bake → load → terrain → polish stack. Implement **one task per session**
and **playtest between each**. Verify each with `./run.sh build && ./run.sh check`, then `./run.sh play`
(GPU visuals must be eyeballed — headless can't render shaders).

### Task 0 — Reframe the design docs
- [x] Rewrite `docs/GAME_SPEC.md` to the open-world design (this document).
- [x] Update `CLAUDE.md` (stack, architecture rules, layout, workflow).
- [ ] *(settings.cfg restructure folded into Task 1, so config keys and the code reading them change together.)*
- **Goal:** the north-star docs describe the new game; the project still builds.

### Task 1 — Scaffolding & removals
- [x] Delete `autoload/ScoreManager.cs` (+ its `project.godot` autoload line), `scripts/GameOver.cs`, `scripts/ChunkManager.cs`, and the two dead `.tscn`s.
- [x] Scrub score / near-miss / combo / crash→game-over / corridor-clamp / streaming from `Game.cs`, `Hud.cs`, `Ship.cs`.
- [x] Trim `settings/settings.cfg`: drop the obsolete `[corridor]`/`[streaming]` sections; keep the rest intact. *(The new `[flight]`/`[damage]`/`[world]` sections land with their code in Tasks 2–3.)* Re-read it immediately before editing, preserve tuned values, stage it explicitly (`settings/settings.cfg` only, never `git add -A`).
- [x] `Game.cs` spawns car + camera + environment + a flat ground void (the car stays `CharacterBody3D` **for this task only**).
- **Goal:** boots clean, fly the void with a speed HUD, **no game-over possible**.

### Task 2 — Flight: `RigidBody3D` 6-DOF + damage *(the make-or-break feel gate)*
- [x] Rewrite `Ship.cs` to the assisted-arcade `RigidBody3D` 6-DOF controller.
- [x] Add `scripts/DamageComponent.cs`; wire `_IntegrateForces` → contact impulse → damage.
- [x] HUD condition bar; add roll/yaw input actions; drop test boxes to hit.
- **Goal:** 6-DOF self-stabilizing flight feels **great**; ramming a box bounces realistically + drops condition + **never ends the game**; releasing the stick re-levels. *Do not proceed until this feels right (the M1 gate).*

### Task 3 — World data + bake pipeline *(riskiest; pure data, no rendering)*
- [x] Add `WorldData` / `PlacedObject` / `ObjectType`, `WorldGenerator` (heightmap + coastline/bay/island/mountain masks + biome map + deterministic object placement on the city plateau).
- [x] Add `WorldBakeTool` (EditorScript) + a `run.sh bake` headless path + a headless validator.
- **Goal:** the bake writes `res://world/world_8km.res`; **same seed → identical bytes**; the validator prints sane counts.

### Task 4 — Runtime world load + tile-grid rendering *(joins Tasks 2 + 3)*
- [x] Add `WorldLoader`, `TileBuilder`, `MeshRegistry` (harvest `CityChunk`'s MultiMesh uploads + the unchanged building shader); lazy near-tile colliders; visibility-range LOD; `Game.cs` spawns the loader.
- **Goal:** the full baked 8 km city renders as MultiMesh batches, **identical every launch**, buildings solid (bounce + damage), far tiles cull into fog, FPS holds.

### Task 5 — Terrain + ocean + biomes
- [x] Per-tile terrain mesh from the heightmap with biome vertex colours; near-tile trimesh colliders; `shaders/water.gdshader` + ocean plane at Y=0.
- **Goal:** reads as a coastal city on an ocean with a bay + islands ringed by desert/hills; land on terrain, skim the water, thread the canyons; terrain solid.

### Task 6 — Synthwave dusk aesthetic pass
- [x] Retune the `WorldEnvironment` + sky from neon-night to warm dusk/sunset; confirm neon still glows; retune `[fx]`.
- **Goal:** warm synthwave dusk; neon reads; desert + ocean warm and legible; FPS holds.

### Task 7 — Ambient AI traffic
- [x] Rewrote `Traffic.cs` into a fixed `AnimatableBody3D` population roaming the bounded world from deterministic (un-baked) spawn points, collidable on layer 1 (bump = bounce + damage via the existing impulse path). *(Collision contract / determinism / wrap verified by construction + headless + a GPU render; the hands-on hit-feel is the standing playtest gate.)*
- **Goal:** sparse cars roam in-bounds; bumping one = damage + bounce.

### Task 8 — glTF model-loading seam
- [x] `MeshRegistry.TrySwapGltf` loads `assets/models/test_tower.glb`, normalizes its mesh to the centered 1×1×1 unit box (per-axis AABB fit baked into a fresh single-surface `ArrayMesh`), reuses the shared `building.gdshader`, and swaps it into **`BuildingTaper` + `BuildingRound`** — purely a registry change, fail-soft to the procedural mesh on any miss. A headless `GltfDocument` generator (`./run.sh modelgen`) authors the test model. *(Verified: `world_8km.res` byte-identical — sha unchanged + not in the commit — and `TileBuilder`/`WorldGenerator`/bake untouched; same 315-object headless load; the swap fires headless with no fallback warning; a GPU render shows the stepped-tower-with-antenna silhouette on those types, correctly seated/sized, still MultiMesh-instanced.)*
- **Goal:** a type renders from glTF purely by a registry change; transforms line up; the bake is untouched.

### Task 9 — World rescale ×5 + realistic units
- [ ] Scale the map **and** city ~5× in linear extent (`[world] extent` 8 km → 40 km) by scaling `WorldGenerator`'s metre **constants** (horizontal ×5, heights ≈×1.5–2 to fix the needle-thin aspect, `Block` widened), scaling the loader's tile/view knobs + `[camera] far` + fog — then **re-bake** (determinism preserved). Scale constants, not the algorithm.
- **Goal:** the world reads ~5× bigger with believable proportions, renders + holds FPS, same seed re-bakes byte-identical. → brief: [`TASK09-world-rescale-realistic-units.md`](tasks/TASK09-world-rescale-realistic-units.md)

### Task 10 — Collision solidity & comfort
- [ ] Fix the car falling **through** the city ground (tunnel-proof near-field collider — `HeightMapShape3D` / solid plateau box), make all static colliders tunnel-proof at the speed clamp, add a **recover / flip-upright** key + a **near-ground hover / landing assist**.
- **Goal:** descend onto a street and bounce (slow drift **and** fast dive), recover frees a wedged car, you can settle + land; condition drops, the game never ends. → source brief: [`BACKLOG …§3.2`](tasks/BACKLOG-world-rescale-physics-and-palette.md) (promote to `TASK10-*.md` when scheduled).

### Task 11 — Dystopian palette re-grade
- [ ] Pull the warm reddish Task-6 dusk toward a **darker, colder dystopian** palette (subtle dark cyan/violet/gray) across sun/fog/sky/water/terrain — **neon preserved**. *(Changes the §7.6 "synthwave dusk" art direction — update §7.6 + §11 on completion.)*
- **Goal:** the scene reads cold + dark + dystopian, distance fades cold, land/sea cold-but-legible, neon still blooms. → source brief: [`BACKLOG …§3.3`](tasks/BACKLOG-world-rescale-physics-and-palette.md) (promote to `TASK11-*.md` when scheduled).

### Backlog (post-foundation)
Objectives/missions → economy → traffic AI behaviour → day/night & weather → real art (buildings,
the player car) → interiors → audio design → Android. Prioritize against [Open Decisions](#11-open-decisions).

---

## 10. Working with Claude Code

- **Put this file at `docs/GAME_SPEC.md`** and keep a **lean `CLAUDE.md`** at the repo root (auto-loaded
  every session — short and specific; the full design lives here and gets referenced).
- **Work one task at a time** (Roadmap §9), then **playtest** before moving on. Use **`/clear`** between
  unrelated tasks.
- **Track progress with the checkboxes** above — tick them as work completes.
- **Commit after each working increment.**

**Suggested next prompt:**
> "Read `docs/GAME_SPEC.md`. Implement Task 1 (scaffolding & removals). Then stop so I can playtest."

---

## 11. Open Decisions

- [x] **Flight control mapping** — *settled (Task 2 playtest):* WASD rotates the car (W/S pitch, A/D yaw + coordinated bank), Q/E roll, Tab cycles camera, mouse free-look; assisted auto-level on release. (A WASD-translate / mouse-aim scheme stays a possible future alternative.)
- [ ] **Object density gradient** — uniform city, or dense core fading to sparse outskirts?
- [ ] **Water as gameplay** — visual-only now; later add soft drag/damage in the bay, or keep it scenery?
- [ ] **Damage consequences** — what (if anything) happens as condition drops (handling loss, forced landing, repair stations)?
- [ ] **The deeper game** — objectives, economy, missions, factions: the whole "what do you *do*" layer, intentionally deferred.
- [ ] **Real art** — when to source glTF building/car models (the seam lands in Task 8).
- [ ] **Android** — still a target eventually, or desktop-first indefinitely?
- [ ] **Final title.**

---

## 12. Reference — Target Versions

- **Godot 4.6** (stable, Jan 2026). Renderer: Forward+ (desktop). Jolt is the default 3D physics engine.
- **glTF 2.0 (`.glb`)** for object models (loaded via the `MeshRegistry` seam, later).
- **godot-rust / gdext** — optional native acceleration; only if profiling demands.
- **Export target:** Linux (desktop) for the foundation.

*Verify exact versions/features against current Godot docs when you start — point releases move.*
