# Task 10 — Collision solidity & comfort (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** — self-contained; you do not need the planning chat.
> When you finish, you **verify** (`build` + headless `check` + a hands-on **GPU** `play` — feel can only be judged
> at the stick), **commit by path**, and report back to session A for review.

> **Status.** **§9 Task 10** — the **last foundation task**. Promoted from the bundled backlog's §3.2
> (`docs/tasks/BACKLOG-world-rescale-physics-and-palette.md`). Tasks 1–9 + 11 are merged and green: the car flies
> (`RigidBody3D` 6-DOF, impulse→damage); the world is baked + rescaled to **~40×40 km** (`world_main.res`); it renders
> as `MultiMesh` + terrain/ocean; traffic roams; the glTF seam is proven; the palette is **cold dystopian** (Task 11).

> **What this task is.** Make the world **solid and landable**, and add the comfort affordances that make low/landing
> flight feel good:
> 1. **Fix the transparent ground** — the car falls **through** the city street/terrain on a descent (the headline bug).
> 2. **General high-speed anti-tunnelling** — nothing slips through any static collider at the speed clamp.
> 3. **Recover / flip-upright key** — right + free the car when it wedges or flips.
> 4. **Near-ground hover / landing assist** — settle and land cleanly (and *can't* slam down hard enough to tunnel).

> **What this task is NOT.** No palette/visual work (Task 11 — done; **don't touch colours**). No world/geometry/bake
> changes (Task 9 — done; **no re-bake**, `world_main.res`/`WorldGenerator` untouched). No new gameplay. Don't rework
> the camera, HUD, or the damage *model* (you may add `[flight]` knobs + a `recover` action). Don't touch
> `Traffic.cs`/`MeshRegistry`/`building.gdshader`.

---

## 0. Context (read first)

Skim **`docs/GAME_SPEC.md` §7.4** (the flying car) + **§5/§7.5** (damage, never game-over) and **`CLAUDE.md`** (the
`[Export]`/`settings.cfg` conventions, the **framerate-independent smoothing rule** — `v.Lerp(target, 1 −
Mathf.Exp(−k·delta))`, never a bare `Lerp(a,b,delta)`). The car is a **`RigidBody3D`** with `GravityScale=0` (it
hovers; `Ship.cs:47`), `ContinuousCd=true` (swept CCD, already on; `Ship.cs:54`), a 3×1×5 box collider, a speed clamp
+ contact-impulse→damage in `_IntegrateForces` (`Ship.cs:121–133`). Collisions are **resolved by Jolt as a bounce**;
**health at zero does nothing — never add a game-over.**

### 0.1 The collision contract (already wired — don't break it)
- The **player Ship** is layer **1** / mask **1**. **Terrain** colliders (`TerrainBuilder.BuildTerrainCollider`) and
  **building** colliders (`TileBuilder.BuildColliders`) are both on the **default layer (1)**, in groups `"terrain"` /
  `"building"`. So any car↔world contact already collides, and `Ship._IntegrateForces` already turns the contact
  impulse into damage — **a solid ground bounce will dent + bounce with ZERO Ship damage-path change.**
- Colliders are **lazy + near-field**: `WorldLoader` (`ColliderTileRadius=1` at 1250 m tiles ⇒ ±1250 m) calls
  `WorldTile.EnsureColliders` (`WorldTile.cs:37–47`), which builds the terrain collider **always** + a box per building.

### 0.2 The ground bug — what's known (don't re-investigate the ruled-out parts)
**Symptom (user):** descending onto a city street, the car passes **through** the ground; climbing back **up** from
below, it's **solid** — *asymmetric*. **Ruled out:** the terrain collider is built, valid, on layer 1, at the right
height (an instrumented run showed `128 faces, layer=1, meshY≈38`). **So it is NOT a missing/empty/wrong-layer/
wrong-height collider.** The cause is **the shape**: `TerrainBuilder.BuildTerrainCollider` (`TerrainBuilder.cs:105–111`)
uses **`terrainMesh.CreateTrimeshShape()`** — a **zero-thickness `ConcavePolygonShape3D`**. Two compounding reasons it
leaks:
1. **Speed tunnelling.** At the 200 m/s clamp ÷ 160 physics-ticks = **1.25 m/tick**, a fast dive steps past a
   thin surface between ticks; **Jolt's CCD is known-weak vs concave/trimesh**, so `ContinuousCd=true` (already on)
   doesn't reliably catch it. The slow climb stays in contact → solid. **This matches the asymmetry exactly.**
2. **Possibly one-sided.** `ConcavePolygonShape3D.BackfaceCollision` defaults to **false** — the trimesh may only
   collide from one face side, compounding the leak.

---

## 1. Definition of Done

`./run.sh build && ./run.sh check` → **0/0**, exits 0, loader line unchanged (`3423 objects … 1250 m`; **no re-bake**).
`./run.sh play`:
- **Descend onto a city street and bounce** — at **both a slow drift and a fast dive**; the car **never passes through
  the ground**. Climb, skim, settle — solid from every approach.
- **Ram a building** at full speed → bounces + dents (still works); **nothing tunnels** through terrain or buildings at
  the speed clamp.
- A **recover key** rights a flipped/wedged car (clears pitch/roll, keeps heading) and lifts it free.
- A **near-ground hover / landing assist** lets you **settle and land** cleanly (eases to a hover; doesn't hard-lock
  altitude — you can still fly low and land deliberately).
- Condition **drops on impacts; the game never ends**. Identical world every launch.

---

## 2. Preconditions

- Branch **`dev6`**. `./run.sh build && ./run.sh check` → exits 0, loader `:: WorldLoader: 3423 objects, 1024 terrain
  tiles (101 populated), 1250 m.`
- **No re-bake** anywhere in this task — `world_main.res` stays byte-identical (collision is a runtime concern).

---

## 3. Work items

### 3.A — The transparent/asymmetric ground *(the headline fix)*

**First, confirm the cause live (cheap, 5 min).** Re-instrument a throttled contact diagnostic in
`Ship._IntegrateForces` (the impulse loop at `Ship.cs:123–128`): for each contact, tag
`state.GetContactColliderObject(i) as Node` → `IsInGroup("terrain")?"T":IsInGroup("building")?"B":"?"` and `GD.Print`
it ~4×/s. Slow-drift onto a street, fast-dive onto a street, ram a building. **Expected:** building ram prints `B`
(the collision path works); the fast dive prints **no `T`** (tunnels); the slow approach may print `T`. **Remove this
print before committing.**

**The fix — do BOTH (robust shape + descent cap); they're defense-in-depth:**

1. **Make the terrain collider tunnel-proof — `HeightMapShape3D`** (Jolt supports it; a real solid heightfield can't be
   tunnelled from *either* side, unlike the thin trimesh). Replace (or sit under) the trimesh in
   `TerrainBuilder.BuildTerrainCollider`. Build it from the **same heightmap the tile mesh uses** so it lines up exactly:
   ```csharp
   // K×K real-elevation samples over the tile (reuse the mesh grid: K = quads+1, so 9 at quads=8).
   int K = quads + 1; float step = tileSize / quads, half = tileSize * 0.5f;
   var hts = new float[K * K];
   for (int j = 0; j < K; j++) for (int i = 0; i < K; i++)
       hts[j * K + i] = data.HeightAt(center.X - half + i*step, center.Z - half + j*step);  // WorldData.HeightAt (cs:36)
   var hm = new HeightMapShape3D { MapWidth = K, MapDepth = K, MapData = new Godot.Collections.Array<float>(hts) };
   var col = new CollisionShape3D { Shape = hm, Scale = new Vector3(step, 1.0f, step) };  // K-1 cells span tileSize; Y in real metres
   ```
   The collider is a child of the tile (tile node at world Y=0, the terrain mesh uses **world Y**; `TerrainBuilder.cs:76`),
   so the heightfield's real-metre heights line up with the mesh at local origin. **Verify alignment in `play`** (the car
   must land on the *visible* surface — watch for an X/Z transpose or half-cell offset; fix with the `col` transform if
   so). You'll need `data`/`tileSize`/`quads` in `BuildTerrainCollider` — thread them through (the loader already has
   them; `WorldTile.EnsureColliders` at `cs:43` is the one caller). *Keeping the mesh's trimesh for rendering is fine —
   only the COLLIDER changes.*
   - **Cheap thing to try first / combine:** set `BackfaceCollision = true` on the existing trimesh. If the asymmetry is
     partly one-sidedness, this helps — but it will **not** stop pure speed-tunnelling, so it's not sufficient alone.
   - **Simpler fallback if `HeightMapShape3D` orientation proves fiddly:** a **thick solid `BoxShape3D` floor** under the
     flat **city plateau** (now at world Y≈`PlateauH·HeightScale ≈ 66 m`): top at the plateau height, extending well
     downward — un-tunnelable. Covers the city ground (where the user hits); pair with the heightfield for sloped terrain,
     or ship the box first as a quick win.

2. **Cap the descent near the ground** — this is **§3.D (hover/landing)**, and it doubles as a tunnelling guard (a car
   that can't slam down at 200 m/s can't out-step the collider). **Do §3.D** — strong synergy.

### 3.B — General high-speed anti-tunnelling (buildings + terrain)

Buildings are **solid `BoxShape3D` volumes** (`TileBuilder.BuildColliders`, `TileBuilder.cs:58–70`) → far less
tunnel-prone than the trimesh. **Verify** a full-speed head-on ram bounces (it should). If any thin glancing hit slips
through, the same mitigations apply (CCD is already on; the speed clamp caps the worst case; a slightly larger
collision **margin** on the car's box is the lever). **Don't over-engineer** — the speed clamp (200) + CCD + a solid
near-field ground likely covers it; confirm by ramming at full speed and watching for pass-through.

### 3.C — Recover / flip-upright key

The auto-level (`Ship.cs:93–97`) only rights *gentle* tilts and can't free a wedged/flipped body. Add a discrete
**recover**:
- **Register the action** in `Game.RegisterInput` (`Game.cs:419+`, mirror the `AddAction(...)` lines): pick a **free**
  key — current map uses WASD/arrows, Shift/Space, Ctrl/X/C/V, Q/E, Tab, R, F, Esc — **`Key.Home`** (or `Key.G`) is free.
- **Implement in `Ship.cs`** (read the action in `_PhysicsProcess`): zero `AngularVelocity`; set the basis **upright**
  preserving heading (rebuild from the current yaw — flatten pitch/roll, e.g. a `Basis` whose −Z is the heading
  projected onto the XZ plane, Y = up); damp `LinearVelocity` (e.g. ×0.3); add a **small upward nudge** to lift clear of
  whatever it's touching. Keep it a discrete press (debounce — `Input.IsActionJustPressed`), not a hold.

### 3.D — Near-ground hover / landing assist

When the car is within `H` metres of the surface below it, apply a **soft upward restoring force + descent damping** so
you can settle and land — and so you **can't slam down hard enough to tunnel** (§3.A.2):
- **Find the ground below the car with a downward raycast** — `GetWorld3D().DirectSpaceState.IntersectRay(
  PhysicsRayQueryParameters3D.Create(pos, pos + Vector3.Down * (H + margin), collisionMask: 1, exclude: [GetRid()]))`.
  This is **self-contained** (no new Ship→world coupling) and also catches **building rooftops**, not just terrain —
  **prefer the raycast** over sampling `WorldData.HeightAt`.
- **Force model** (in `_PhysicsProcess`, framerate-independent): within the assist band, add an upward force that grows
  as you near the ground **and** with downward speed (so a fast drop is arrested). Ease toward a hover; **don't hard-lock
  altitude** — the pilot must still fly low and land deliberately.
- **Expose knobs as `[Export]` + `[flight]`** (mirror the existing pattern exactly): add `HoverHeight`/`HoverStrength`/
  `HoverDamp` `[Export]`s on `Ship` (with the `[ExportGroup("Assist...")]`), and map them in `Game.ApplyFlightConfig`
  (`Game.cs:252–269`, add `ship.HoverHeight = CfgFloat("flight","hover_height", ship.HoverHeight);` etc.). Tune the
  values **live**; **REPORT them** (session A applies — see §6). Tune at the **40 km / 200 m/s** scale.

---

## 4. Correctness / taste points

1. **Never add a game-over.** Damage still just drops condition (`Ship._IntegrateForces` → `DamageComponent`); zero
   health does nothing. The solid ground means you can now *rest* on it — make sure resting contact doesn't rack up
   damage (the `[damage] min_impulse_threshold` already ignores gentle contacts; confirm landing doesn't spam damage).
2. **Hover must not fight the pilot** — ease to a hover, never hard-lock altitude; deliberate landing must still work.
3. **No re-bake / no world change** — `world_main.res`, `WorldGenerator`, the bake are untouched. The heightfield is
   built at runtime from the already-loaded `WorldData`.
4. **Framerate-independent** all new smoothing/forces (`1 − Mathf.Exp(−k·delta)`), `float` delta, `Mathf.*`, `f` literals.
5. **Don't touch the palette** (Task 11) or the collision *layers/groups* (the contract in §0.1 is load-bearing).
6. **Keep it near-field + cheap** — the heightfield is built in the same lazy `EnsureColliders` path (±1 tile), so cost
   is bounded; don't build colliders for all 1024 tiles.

---

## 5. Verify

- `./run.sh build` → **0/0**; `./run.sh build && ./run.sh check` → exits 0, loader line unchanged (no re-bake;
  `world_main.res` byte-identical — `git status` clean for it).
- `./run.sh play` (the real test): the **three ground maneuvers** (slow drift / fast dive / building ram) all **bounce,
  none tunnel**; the **recover key** rights + frees a flipped car; the **hover** lets you settle + land; condition drops,
  the game never ends. The palette still reads cold (you didn't touch it).
- **Screenshots optional** (the fix is *feel*, not look — describe it): the §5 recipe from prior briefs if useful.

---

## 6. Report back to session A (for review)

- `git status` + `git diff --stat`; the per-file edits.
- `./run.sh build` + `check` output (0/0; loader line unchanged; confirm **`world_main.res` untouched** — no re-bake).
- **Root-cause confirmation** (which maneuver tunnelled in the §3.A diagnostic) and **which fix you shipped**
  (`HeightMapShape3D` / solid plateau box / both, + `BackfaceCollision`), and whether building rams ever tunnelled.
- The **recover key** you chose and the **hover** model + the `[flight]` `hover_*` values you landed on — **every key +
  value**, for session A to apply (you did **not** commit `settings.cfg`).
- A description of the **GPU `play`** against §1 (the three maneuvers, recover, hover/landing feel).
- **Confirm** you did **not**: re-bake / touch `world_main.res`/`WorldGenerator`, change the palette (Task 11),
  touch `Traffic.cs`/`MeshRegistry`/`building.gdshader`, or add any game-over.

**Commit (by path):** `scripts/Ship.cs`, `scripts/world/TerrainBuilder.cs`, `scripts/world/WorldTile.cs` (if you threaded
`data/tileSize/quads` through for the heightfield), `scripts/Game.cs` (recover action + `ApplyFlightConfig` hover knobs).
**Do NOT commit `settings.cfg`** (report the `[flight]` `hover_*` values). **Do NOT tick `§9`** (session A owns it). End
the commit body with:
`Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

Message e.g.: `Collision: solid tunnel-proof ground (HeightMapShape3D) + anti-tunnel + recover key + near-ground hover/landing`.

---

## 7. Out of scope (do not do here)
- **Palette / visuals** (Task 11 — done; never touch the colours or `building.gdshader`).
- **Re-bake / world geometry / `WorldGenerator` / `world_main.res`** — collision is runtime-only.
- **`Traffic.cs` / `MeshRegistry` / glTF.**
- **Camera / HUD / the damage *model*** (`DamageComponent` math) — you may add `[flight]` hover knobs + a `recover`
  action, but don't rework the camera, HUD layout, or damage formula.
- **Any game-over / death / forced-respawn** — health at zero stays a no-op (spec §5/§7.5).
- **Ticking `§9` / editing docs** — session A owns docs on review (you only **report**).
