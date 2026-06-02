# Task 7 — Ambient AI traffic (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 7. It is self-contained — you do not need the
> planning conversation. When you finish, you verify (build + headless check + a hands-on **GPU** playtest), commit,
> and bring results back to session A for review. One such doc lives in `docs/tasks/` per task.

> **What this task is.** Bring the city to life with **sparse ambient flying-car traffic**: a **fixed population** of
> cruising cars that **roam the bounded 8 km world**, are **collidable** (you bump one → your car **bounces + takes
> damage**, exactly like hitting a building), and **never end the game**. The current `scripts/Traffic.cs` is the
> **corridor-era** version (a recycle-window pool on the wrong physics layer, detected by the long-deleted near-miss
> queries) and **isn't spawned anywhere** — you **rewrite** it for the open world and **wire it into `Game.cs`**.

> **What this task is NOT.** **No traffic *intelligence*** — no obstacle avoidance, no lane-following, no reacting to
> the player. Cars cruise simple headings; "traffic AI behaviour" is explicitly a **post-foundation backlog** item
> (§9). **No re-bake** — `world_8km.res` stays **buildings-only**; spawn points are **deterministic at load**, not
> baked data (see §3.1). **No glTF car model** (Task 8 — the car stays a placeholder box). **No scoring / near-miss /
> combo** (gone in the reframe — do not reintroduce). **Do not touch the flying car, damage, camera, or HUD** — the
> `_IntegrateForces` impulse→damage path and the `OnShipDamaged` juice (shake/sound/flash) **already fire on any
> collision**, so traffic hits work with **zero changes to `Ship.cs` / `DamageComponent.cs` / `CameraRig.cs` /
> `Hud.cs`**. **Don't touch world rendering** (`WorldLoader`/`TileBuilder`/`TerrainBuilder`/`MeshRegistry`) or
> `building.gdshader`.

> **Scope discipline.** Edit essentially **two files**: **`scripts/Traffic.cs`** (rewrite) and **`scripts/Game.cs`**
> (spawn it + an `ApplyTrafficConfig`). Reuse **`shaders/hazard.gdshader`** as-is (confirm only). **Tune the
> `[traffic]` numbers in your *local* `settings/settings.cfg` live, then REPORT them — do not blanket-commit
> settings.cfg** (the user edits it live; stage **by path**, **never `git add -A`**, **never revert their values**).
> Leave `docs/GAME_SPEC.md §9` to **session A** (don't tick it).

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (skim **`docs/GAME_SPEC.md` §5–§7** and
**`CLAUDE.md`** first). Tasks 1–6 are done: the arcade loops are stripped; the car is a `RigidBody3D` 6-DOF flyer with
impulse→damage (Task 2); the 8 km world is baked (Task 3) and renders as GPU `MultiMesh` building batches on a fixed
tile grid (Task 4) with terrain + ocean (Task 5) and a synthwave-dusk look (Task 6). **What's missing is life** — the
city is static. Task 7 adds **moving traffic** as a live, collidable hazard.

### 0.1 The current `Traffic.cs` — why it's a rewrite, not a tweak

`scripts/Traffic.cs` is the **streamed-corridor** implementation and is **structurally wrong** for the open world:

| Corridor-era (now) | Why it's wrong for the open world | Open-world (Task 7) |
|---|---|---|
| A **recycle-window pool**: cars live in a Z-window `±SpawnAhead/Behind` around the ship and respawn when they drift out | The world is **bounded + finite**, not an infinite corridor — there's no "ahead/behind" to recycle through | A **fixed population** that roams **inside the borders** and never recycles |
| Cars are **`StaticBody3D`** moved by setting `Position` | A teleported static body doesn't impart velocity to the player's RigidBody — weak/!tunnelling bounce | **`AnimatableBody3D`** (kinematic mover that pushes the RigidBody correctly — §4) |
| `CollisionLayer = obstacles` (**layer 2**), `group "obstacle"`, read by the old **near-miss shape queries** | The reframed player is **layer 1 / mask 1** and the near-miss queries are **deleted** — so the player **never touches** layer-2 cars | **Layer 1** (like buildings/terrain) + `group "traffic"` → the player's existing mask-1 contact path bounces + damages with **no Ship change** |
| Z-axis corridor motion, `TowardFraction` (toward +Z) | "Toward the player along Z" is a corridor concept | Free **horizontal headings**, wrap at the borders |
| **Not spawned** — `Game.cs` never instantiates it (the reframe dropped it) | — | `Game.cs` spawns + configures it |

So: **keep the bits that still fit** (the shared `hazard.gdshader` magenta material, the box-mesh placeholder, the
seeded-RNG idea, the `[Export]` tunable style) and **replace the corridor machinery** (window, recycle, Z-motion,
layer 2, `TowardFraction`).

### 0.2 The surfaces you touch

| Surface | File / location | What you do |
|---|---|---|
| **Traffic system** | `scripts/Traffic.cs` (rewrite) | Fixed roaming population of collidable `AnimatableBody3D` cars |
| **Spawn + config** | `scripts/Game.cs` (`_Ready` + a new `ApplyTrafficConfig`, mirroring `ApplyFlightConfig` ~L216) | Instantiate `Traffic`, push `[traffic]` config, pass it the player + world extent + city centre |
| **Hazard look** | `shaders/hazard.gdshader` (**confirm only**) | Reuse verbatim — magenta pulse via `base_color`/`emission_color`/`pulse_on` (already the traffic look) |
| **Tunables** | `settings/settings.cfg [traffic]` (REPORT, don't commit) | Retune for the new scale (the corridor values are stale — §3.3) |

**Not yours:** `Ship.cs` / `DamageComponent.cs` / `CameraRig.cs` / `Hud.cs` (collision + juice already work),
`WorldLoader`/`TileBuilder`/`TerrainBuilder`/`MeshRegistry`/`building.gdshader`, `world_8km.res` (**no re-bake**).

---

## 1. Definition of Done

1. `./run.sh build` → **0 errors, 0 warnings**; `./run.sh build && ./run.sh check` → boots headless, **exits 0**, and
   still prints the loader line (`:: WorldLoader: 315 objects, 1024 terrain tiles (85 populated), 250 m.`). (Headless
   has physics but no view — the look is the `play` run.)
2. `./run.sh play` (**GPU — eyeball these**):
   - **Sparse magenta flying cars cruise the world** — you see them moving against the dusk skyline, reading instantly
     as moving hazards (distinct from the buildings).
   - **They stay inside the borders** — fly to the world edge (±~4 km) and the population is still there, roaming; no
     car escapes to infinity, none pile up at an edge, the count is **fixed** (no streaming/recycle).
   - **Bumping one bounces + dents you** — fly into a car: your car **bounces** (Jolt resolves it) and **condition
     drops** with the usual **camera shake + crash thud + impact flash**. **The game never ends.**
   - **Identical population every launch** — quit + re-run → the same cars spawn in the same places (deterministic
     seed). (They then drift via motion — that's fine; only the *spawn* is fixed.)
   - **FPS holds** at a cruise (a sparse fixed count — you did not spawn thousands).

---

## 2. Preconditions

- Branch **`dev6`**, Tasks 1–6 merged + the two recent commits in (`e1c2e99` dynamics, `e2ba023` backlog brief).
  Confirm green: `./run.sh build && ./run.sh check` → exits 0 and prints the loader line above.
- `scripts/Traffic.cs` exists but is **orphaned** (nothing references it — confirmed). You're free to rewrite it whole.
- Read `docs/GAME_SPEC.md` **§5** (pillars — *damage, never game-over*), **§7.4/§7.5** (the car + the impulse→damage
  model), **§7.1** (the bake is **buildings-only** — traffic is **not** baked). Skim `CLAUDE.md` (the `[Export]`/`[fx]`
  conventions, the **framerate-independent smoothing rule** — `v.Lerp(target, 1 - Mathf.Exp(-k*delta))`, *never* a bare
  `Lerp(a,b,delta)`; "GPU visuals can't be confirmed headless").

---

## 3. Work items

### 3.1 Rewrite `scripts/Traffic.cs` — a fixed roaming population

Keep `[GlobalClass] public partial class Traffic : Node3D`. Replace the corridor machinery with:

**Per-car node = `AnimatableBody3D`** (the crux — see §4.1):
- `CollisionLayer` = **layer 1 only** (`CollisionLayer = 1`, i.e. `SetCollisionLayerValue(1, true)`) — so the player's
  mask-1 RigidBody collides and the existing impulse→damage path fires. **NOT the old layer 2.**
- `CollisionMask = 0` — a car detects nothing itself (it's only *hit by* the player; cars don't collide with each
  other, buildings, or terrain — that keeps them jitter-free, and avoidance is out of scope).
- `AddToGroup("traffic")` — a **third** contact group alongside `"terrain"`/`"building"` (so contact attribution /
  future diagnostics can tag a car hit as `R`/traffic).
- `SyncToPhysics = true` — makes the kinematic mover report its velocity so the player bounces correctly (§4.1).
- Child `CollisionShape3D` with a `BoxShape3D` (the car body), + a child `MeshInstance3D` (box mesh, the magenta
  hazard material). Reuse the existing `GetCarMesh()` (unit `BoxMesh` scaled by `CarSize`) and `GetCarMat(HazardPulse)`
  (the `hazard.gdshader` magenta) — those two helpers are fine to keep verbatim.

**Deterministic spawn (this is what "baked spawn points" means here — NO re-bake):**
- Seed a `RandomNumberGenerator` from a fixed `[Export] int Seed` (or the world seed) so the **population is identical
  every launch** — matching the world's "no RNG, identical every launch" philosophy. *(The bake stays buildings-only;
  traffic is dynamic, so there's no value in writing spawn points into `world_8km.res`, and a re-bake is out of scope.)*
- **Spawn over/around the city** for an ambient-city feel: `Game` passes you the **city centre** (`WorldLoader.
  CityCenter`) and the **world extent**. Scatter `Count` cars within a `SpawnRadius` of the city centre (or, nicer:
  sample positions and keep those whose `WorldData.BiomeAt` is `City` — but that couples you to world data; the simple
  radius is fine), each at a random **altitude band** (`[Export] Vector2 AltitudeBand`, e.g. 60–400 m — flying cars
  cruise above the streets, below the megatowers) and a random **horizontal heading** + speed.

**Roam (simple, ambient — `_PhysicsProcess`):**
- Each car moves at a constant horizontal velocity (`heading * speed`) — set its `GlobalPosition` (or `GlobalTransform`)
  each physics frame, and **face the heading** (yaw the mesh/body toward travel).
- **Stay in-bounds by wrapping toroidally**: when a car's X or Z passes `±extent/2`, re-enter at the opposite edge
  (same lane/altitude). This keeps the **fixed population inside the borders forever** with no edge-accumulation and no
  recycle logic. *(Reflecting off the borders is an alternative; wrapping is simplest and invisible enough at sparse
  density.)*
- **Don't hardcode `8000`** — read the extent from `Game` (which reads `[world] extent` / `WorldData.WorldExtent`), so
  traffic **auto-scales** when the backlog rescale (`docs/tasks/BACKLOG-world-rescale-physics-and-palette.md`) makes the
  world 5× bigger.

**Delete** the corridor exports/logic: `SpawnAhead`/`SpawnBehind`, the whole `[Corridor]` group
(`CorridorHalfWidth`/`Floor`/`Ceiling`/`XFraction`/`YFraction`), `TowardFraction`, `Respawn`/`ScatterInitial`/the
recycle branch in `_PhysicsProcess`. **Keep/adapt** `Count` (was `CarCount`), `MinSpeed`/`MaxSpeed`, `CarSize`
(currently `5,2,9`), `HazardPulse`, `Seed` (was `WorldSeed`), and add `AltitudeBand` + `SpawnRadius`.

**Public API for `Game`:** something like `Initialize(Node3D player, float worldExtent, Vector3 cityCenter)` (or set
those as fields before `AddChild`), then build the population in `_Ready`/`Initialize`. You don't actually need the
`player` reference for motion (cars roam independently) — but pass it if you want optional **mesh-only LOD** (hide a
car's `MeshInstance3D` beyond N metres; the collider stays — *optional*, sparse traffic doesn't need it).

### 3.2 Wire it into `scripts/Game.cs`

- Add a `SpawnTraffic()` called from `_Ready` **after `SpawnWorld()`** (so `WorldLoader.CityCenter` is set), e.g.
  alongside the other `Spawn*()` calls (~L37). Guard on `GetNodeOrNull("Traffic") == null` and on a count > 0.
- Mirror `ApplyFlightConfig` (~L216) with an **`ApplyTrafficConfig(Traffic t)`** that pushes `[traffic]` onto the
  exports **before `AddChild`** (so `_Ready` builds with them): `Count`, `MinSpeed`, `MaxSpeed`, `AltitudeBand`,
  `SpawnRadius`, `Seed`, `HazardPulse` (the last from `[fx] hazard_pulse`, default true).
- Pass the world extent (`CfgFloat("world", "extent", 8000.0f)`) and `_world.CityCenter` into the traffic node.
- **Juice is automatic:** `Game.OnShipDamaged` (already connected to `_ship.Damage.Damaged`) fires the camera
  shake + crash sound + flash on **any** dent, including a traffic hit. **No new wiring.** Confirm it triggers in `play`.

### 3.3 Retune `settings/settings.cfg [traffic]` (REPORT, don't commit)

The current values are **corridor-era and stale**:
```
count=10           ; sparse over 8 km — raise for a livelier city (tune live)
min_speed=20.0
max_speed=1000.0   ; STALE — matched the old 1000 m/s player; the player is now 200, so ambient cruisers
                   ; at ~1000 would be teleporting blurs. Drop to ~50–90 m/s.
toward_fraction=0.5 ; OBSOLETE — a corridor (+Z) concept. Drop it; headings are now random in-plane.
```
- Set **sane code `[Export]` defaults** in `Traffic.cs` for the new world (e.g. `MinSpeed 25`, `MaxSpeed 80`,
  `Count 24`, `AltitudeBand (60,400)`), and **tune the `[traffic]` cfg values live**, then **report** what you landed
  on (session A applies them — same discipline as Task 5/6 `[fx]`). **Remove `toward_fraction`** from the section (or
  leave it unread — note which). Add `altitude_min`/`altitude_max`/`spawn_radius`/`seed` keys **only if** you want them
  cfg-exposed (wire `CfgFloat`/`CfgInt` reads with fallbacks and **report the new keys** for session A) — otherwise
  keep them as code exports. **Do not `git add` settings.cfg** (the user edits it live; report the values).

### 3.4 Visuals — reuse the hazard look (confirm only)

The cars use **`shaders/hazard.gdshader`** (already the traffic shader): magenta `base_color (0.55,0.05,0.5)` +
`emission_color (1.0,0.1,0.85)` + the breathing `pulse_on` (gated by `[fx] hazard_pulse`). **Keep it** — it reads as
moving neon traffic against the dusk city. **Don't edit the shader.** *(Forward note: the backlog palette pass will
cool this magenta toward the dystopian grade — that's session A's concern there, not here. Leave it magenta.)* If you
want the cars a touch more legible at distance you may bump `emission_energy` via the material, but default to leaving
the shader untouched.

---

## 4. Correctness points (get these right)

1. **The collision LAYER is the whole ballgame.** The player is **layer 1 / mask 1**; buildings + terrain are layer 1.
   Put traffic on **layer 1** and the player bounces + dents it through the **existing** `_IntegrateForces` path with
   **zero Ship changes**. The old code's **layer 2 ("obstacles")** is invisible to the reframed player — if you copy
   that, nothing collides and the task silently fails. (The `layer_2="obstacles"` name in `project.godot` is a corridor
   leftover; ignore it.)
2. **`AnimatableBody3D` + `SyncToPhysics`, not a teleported `StaticBody3D`.** Move the car in **`_PhysicsProcess`** via
   its transform; `SyncToPhysics = true` makes Godot compute its velocity and feed it to Jolt, so the player gets a
   **proper bounce + contact impulse** (→ damage). A `StaticBody3D` whose `Position` you set just teleports — Jolt may
   depenetrate but won't impart the car's motion, giving a weak/odd bounce or a tunnel. *(If the kinematic velocity
   transfer reads weak on GPU, the fallback is a `RigidBody3D` with `FreezeMode = Kinematic` moved the same way — but
   try `AnimatableBody3D` first.)*
3. **Fixed population, in-bounds, forever.** Spawn `Count` once; **wrap at `±extent/2`**. No recycle window, no
   per-frame instantiate/free, no accumulation at an edge. Fly to the border and confirm the cars are still roaming.
4. **Determinism:** seed the spawn RNG from a fixed `Seed` → identical population each launch. Motion drifts after t=0
   (that's expected and fine — it's `delta`-driven, not nondeterministic). Don't seed from the clock.
5. **No game-over, ever.** Bumping a car dents + bounces; health at 0 still does nothing (the `DamageComponent`
   guarantees this — don't add any death/reload path). This is a **pillar** (§5).
6. **Perf: sparse means sparse.** A few dozen `AnimatableBody3D` cars with always-on colliders is trivial. **Do not**
   scale to hundreds/thousands or add per-car nodes beyond the body+shape+mesh. (Buildings use `MultiMesh` precisely to
   avoid thousands of nodes — traffic is sparse, so individual nodes are fine here.)
7. **Framerate-independent** any smoothing (e.g. easing a car's heading/yaw) per the `CLAUDE.md` rule. Constant-velocity
   cruise is just `pos += vel * (float)delta`.
8. **Don't reintroduce removed systems** — no near-miss, no combo, no score, no `Crashed`/game-over signals.

---

## 5. Verify

1. `./run.sh build` → **0/0**.
2. `./run.sh build && ./run.sh check` → exits 0; still prints the Task-5 loader line. (Physics runs headless, so this
   proves the traffic spawns + simulates without crashing — but you can't *see* it; that's `play`.)
3. `./run.sh play` (**GPU — the real DoD**), against §1:
   - Magenta cars cruise; **fly into one → bounce + condition drop + shake/sound/flash; game continues.**
   - **Fly to the world edge** — cars still roam in-bounds; **none escape, none pile up**, the count is fixed.
   - **Quit + relaunch** → same spawn positions (determinism).
   - Watch **FPS** at a cruise.
4. **Screenshot (optional, for the report):** `godot-mono --path . &` (note PID), `sleep ~22`,
   `spectacle -bnf -o /tmp/traffic.png`, `kill -9 $PID; pkill -9 -x godot-mono` — **as one background Bash command**
   (foreground `sleep` is blocked).

---

## 6. Report back to session A (for review)

- `git status` + `git diff --stat`, and the per-file edits: **`scripts/Traffic.cs`** (the rewrite) +
  **`scripts/Game.cs`** (`SpawnTraffic` + `ApplyTrafficConfig`).
- `./run.sh build` + `./run.sh check` output (0/0; still prints the loader line).
- A description of the **`play` GPU playtest** against §1 (headless renders none of it) — cars cruising, the bounce +
  dent + juice on a hit, in-bounds at the world edge, identical relaunch, rough FPS. (Attach the §5 screenshot if taken.)
- **Recommended `[traffic]` values** — every key you tuned (`count`, `min_speed`, `max_speed`, any altitude/spawn/seed
  keys) and the value you landed on; whether you **removed `toward_fraction`**; any **new keys** you'd want added
  (session A transcribes them — you did **not** commit settings.cfg).
- The **node type** you shipped (`AnimatableBody3D` vs the `RigidBody3D`-kinematic fallback) and that traffic is on
  **layer 1 / group `"traffic"`**.
- **Confirmation** you did **not**: re-bake / touch `world_8km.res` / `WorldGenerator` / world rendering, change
  `Ship.cs` / `DamageComponent.cs` / `CameraRig.cs` / `Hud.cs`, edit `hazard.gdshader` or `building.gdshader`, or
  reintroduce near-miss/scoring.
- **Commit** once build + check + playtest are green. Stage **by path** (`scripts/Traffic.cs`, `scripts/Game.cs`);
  **never `git add -A`**; **do not** stage `settings/settings.cfg` (session A applies `[traffic]`); **do not** tick
  `docs/GAME_SPEC.md §9` (session A ticks it). Suggested message:
  `Task 7: ambient AI traffic — fixed collidable flying-car population roaming the bounded world`.
  End the commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **Traffic intelligence** — obstacle/building avoidance, lane/road following, reacting to the player, inter-car
  spacing. Cars cruise blind and may clip buildings; that's acceptable for ambient sparse traffic. → **backlog**
  ("traffic AI behaviour", §9).
- **Re-bake / baked spawn data** — `world_8km.res` stays buildings-only; spawn points are deterministic at load.
- **glTF car model** — the car is a placeholder box; the model swap is **Task 8** (via `MeshRegistry`, no re-bake).
- **Destructible / damageable traffic** — cars are kinematic and indestructible here; *you* take the damage. (Cars
  reacting to hits is later.)
- **Ground/street vehicles** — these are **flying** cars cruising altitude bands; street-bound traffic is later.
- **Cooling the magenta** to the dystopian palette — that belongs to the backlog palette pass (leave it magenta).
- **Touching the flying car / damage / camera / HUD / world rendering / shaders**, or **editing `settings.cfg` (commit)
  / `CLAUDE.md` / `docs/` prose / ticking §9** — session A owns docs on review; the collision + juice already work.
