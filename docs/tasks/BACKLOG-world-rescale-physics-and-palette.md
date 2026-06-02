# Backlog — World rescale ×5 + physics/comfort + dystopian palette (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief**. It is self-contained — you do not need the planning
> conversation. When you finish each pass you verify (build + headless check + a hands-on **GPU** playtest),
> commit, and bring results back to session A for review. One such doc lives in `docs/tasks/` per task.

> **Status / scheduling.** This is an **unnumbered backlog task**, scheduled to run **after the existing §9
> roadmap** (Task 7 — Ambient AI traffic, Task 8 — glTF seam). It carries **no `§9` task number** yet — session A
> slots it into the roadmap on review. It bundles **four themes** the user grouped together while interstitially
> testing the car; it is **larger than a normal single-session task**. Tackle it as **four sequential sub-passes**
> (§3.0–§3.3), each ending in its own `build + check + playtest + commit`. You may split the passes across sittings.

> **What this task is.** Four things, in dependency order:
> 1. **Finalize the in-flight dynamics baseline** (§3.0) — commit the confirmed car-feel tuning that's currently
>    uncommitted, and clean the tree of temp diagnostics. *(Small — may already be committed by session A; verify.)*
> 2. **World rescale ×5 + realistic units** (§3.1) — make the city **and** the overall map **5× larger** in linear
>    extent, and **rationalise the now-unrealistic size/distance values** so proportions read believably. **Re-bake.**
> 3. **Collision solidity & comfort** (§3.2) — fix the **car falling through the city ground** (the anchor bug),
>    make **all** static colliders tunnel-proof at speed, add a **recover/flip-upright** key, and a **near-ground
>    hover / landing assist**.
> 4. **Dystopian palette re-grade** (§3.3) — pull the warm **reddish** Task-6 dusk toward a **darker, colder
>    dystopian palette** (subtle dark **cyan / violet / gray**), neon preserved.

> **What this task is NOT.** No new gameplay systems (no missions/economy/AI behaviour). No glTF (Task 8). The
> palette pass is a **re-grade of existing surfaces**, not new render features. The rescale **changes data + tunables
> + a re-bake** — it does **not** rewrite the generator's algorithm (heightmap math, placement loop, determinism kit
> all stay; you scale their **constants**). Do not touch `building.gdshader` (the neon identity — confirm only).

> **Scope discipline.** This touches more files than a normal task **because it's four passes** — but each pass has
> a **tight file set** (listed in its §3 subsection). **Do the passes in order and commit between them** so review is
> incremental. As with Task 6: **tune the `[fx]`/`[flight]` numeric knobs in your *local* `settings/settings.cfg`
> live, then REPORT the values — do not blanket-commit settings.cfg** (the user edits it live; stage it **by path,
> [flight]/[world] sections only, never `git add -A`**, and **never revert the user's values** — see §3.0/§4). Leave
> `docs/GAME_SPEC.md` and `CLAUDE.md` to **session A** (this re-grade shifts the stated art direction — flagged in §6).

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (skim **`docs/GAME_SPEC.md` §4–§7** and
**`CLAUDE.md`** before starting). Tasks 1–6 are done: arcade loops stripped; the car is a `RigidBody3D` 6-DOF flyer
with impulse→damage (Task 2); the 8 km world is baked to `res://world/world_8km.res` (Task 3) and renders as GPU
`MultiMesh` building batches on a fixed tile grid (Task 4); terrain + ocean + biomes exist (Task 5); a warm
**synthwave-dusk** look is applied (Task 6). This task came out of an **interstitial bug-fixing session** where the
user reduced speed 5×, fixed the A/D steering and the instant-speed-drop — and then asked to **bundle the remaining
ground-collision bug with a set of bigger improvements into one future parallel-session task** (this doc).

### 0.1 The surfaces you touch (per pass)

| Pass | Files | Theme |
|---|---|---|
| **§3.0 Finalize dynamics** | `settings/settings.cfg [flight]` (by path), `scripts/Ship.cs`, `scripts/world/WorldTile.cs` (remove temp print) | Commit confirmed feel tuning; clean temp diagnostics |
| **§3.1 Rescale ×5 + units** | `scripts/world/WorldGenerator.cs` (the metre constants), `settings/settings.cfg [world] extent` + `[game] scale` (by path), `scripts/world/WorldLoader.cs` (`TileSize`/view/collider exports), `settings/settings.cfg [camera] far`, **re-bake `world/world_8km.res`** | 5× bigger map + city; realistic sizes/distances |
| **§3.2 Collision & comfort** | `scripts/Ship.cs`, `scripts/world/TerrainBuilder.cs` (near-field collider), `scripts/world/WorldLoader.cs` (collider radius), `scripts/Game.cs` (`RegisterInput` — recover key) | Solid ground, anti-tunnel, recover key, hover/land assist |
| **§3.3 Dystopian palette** | `scripts/Game.cs` (`EnsureEnvironment`+`BuildEnvironment`+`BuildSky`), `shaders/sky.gdshader`, `shaders/water.gdshader`, `scripts/world/TerrainBuilder.cs` (`Palette`), `settings/settings.cfg [fx]` (REPORT, don't commit) | Warm-reddish → cold dystopian cyan/violet/gray |

**Not yours, in any pass:** `building.gdshader` (confirm only), the determinism kit / placement algorithm in
`WorldGenerator` (you change **constants**, not the math), `CameraRig`, `Hud` layout, `DamageComponent` logic,
`Traffic.cs` (Task 7), `MeshRegistry`/glTF (Task 8), `ScreenFX`/`post.gdshader`/`RainFX`.

### 0.2 Why these four are bundled (and the dependency order)

They interact, so **order matters**: **0 → 1 → 2 → 3**.
- **§3.1 (rescale) sets the spatial scale** every other pass tunes against — collider distances (§3.2 hover height,
  `ColliderTileRadius`, anti-tunnel speeds) and fog/view distances (§3.3) are all **functions of world size**. Doing
  rescale first means you tune collision and palette **once**, at the final scale, not twice.
- **§3.0 (finalize) first** so you start from a clean, committed tree (and don't fight uncommitted churn).
- The **transparent-ground bug (§3.2) is the user's headline pain** — but its robust fix (a tunnel-proof near-field
  collider + the hover assist) is **easier to tune at the final scale**, so it lands in §3.2 *after* the rescale.
  (If you prefer, you *may* land a quick provisional ground fix during §3.0 and harden it in §3.2 — see §3.2.)

---

## 1. Definition of Done (per pass — each ends with its own playtest + commit)

**§3.0 Finalize dynamics:** `./run.sh build && ./run.sh check` → 0/0, exits 0. The confirmed `[flight]` tuning is
committed (or confirmed already committed); the temp `:: [dbg]` prints are gone from `WorldTile.cs` (the contact
print in `Ship.cs` may stay through §3.2). Flight still feels as the user confirmed ("much better").

**§3.1 Rescale ×5:** `./run.sh bake` writes a new `world/world_8km.res`; **baking twice with the same seed yields
byte-identical files** (determinism preserved). `./run.sh play`: the map + city are **~5× larger** in linear extent,
the **proportions read realistically** (street/block widths, building height-vs-width, the city-vs-world ratio), and
it **renders + holds FPS** at the new scale (tile grid, fog, view distances all scaled so nothing dither-pops or
murk-clips). The loader prints sane counts. **Identical every launch.**

**§3.2 Collision & comfort:** `./run.sh play`: you can **descend onto a city street and bounce** (the car never
passes through the ground) — at **both a slow drift and a fast dive**; ramming a **building** still bounces + dents;
**nothing tunnels** at the speed clamp. A **recover key** rights the car + frees it if wedged. A **near-ground hover /
landing assist** lets you settle and land cleanly. Condition drops on impacts; **the game never ends**. Temp
diagnostics removed.

**§3.3 Dystopian palette:** `./run.sh play`: the scene reads **darker and colder** — a **dystopian cyan/violet/gray**
mood, **not** the warm reddish dusk. **Neon still blooms** (city windows pop against the cold haze); the distance
fades to a **cold** haze; land/sea are **cool but legible** (dark ≠ black, nothing muddy). **Identical every launch.**

---

## 2. Preconditions

- Branch **`dev6`**. Tasks 1–6 merged and green. **If Task 7/8 are also merged by the time this runs**, this task must
  **coexist** with them: the rescale (§3.1) must also rescale any **traffic spawn bounds** and the palette (§3.3)
  should cool the **traffic car material** — but those live in `Traffic.cs`, owned by Task 7; **note the interaction,
  don't silently rewrite Task 7's code**. This brief is written to **not depend on** Task 7/8 existing.
- Confirm green before starting: `./run.sh build && ./run.sh check` → exits 0 and prints the loader line
  `:: WorldLoader: <N> objects, <T> terrain tiles (<P> populated), <S> m.` (counts/size are the **pre-rescale**
  values; §3.1 changes them).
- Read `docs/GAME_SPEC.md` **§7.1** (bake = data not geometry), **§7.4** (the flying car), **§7.6** (art direction —
  *currently* "synthwave dusk"; §3.3 changes this — see §6), and **§7.7** (determinism). Skim `CLAUDE.md` (the
  `[Export]`/`settings.cfg` conventions, the **framerate-independent smoothing rule**, **bake determinism rules**,
  "GPU visuals can't be confirmed headless").

---

## 3. Work items

### 3.0 — Finalize the in-flight dynamics baseline *(small; clean the tree first)*

**Background.** During the interstitial session the user reduced/tuned the car's feel and **confirmed it ("car
dynamics is much better")**. **Session A committed this baseline on 2026-06-02** (commit `Dynamics: confirmed flight
tuning …`) — the `[flight]` values below + the `Ship.cs` `ContinuousCd = true` line, with **both temp diagnostics
removed**. So **this pass is normally just: confirm the baseline is present and the tree is clean**, then move on. The
detail below is reference (in case it ever regresses):

**The confirmed `settings.cfg [flight]` values (user-tuned — commit as-is, never revert):**

| key | value | note |
|---|---|---|
| `max_speed` | `200.0` | was 1000; reduced 5× for comfortable testing |
| `thrust_force` | `350.0` | was 4000; accel ~88 m/s² on mass 4 |
| `brake_force` | `500.0` | was 6000; scaled to thrust |
| `mass` | `4.0` | unchanged (preserves torque feel) |
| `pitch_torque` / `yaw_torque` / `roll_torque` | `45` / `30` / `50` | unchanged from the interstitial tune |
| `level_strength` / `level_damping` | `9.0` / `4.5` | unchanged |
| `bank_coordination` | `0.0` | A/D steers **flat** (pure yaw, auto-levelled) like a car |
| `linear_damp` | `0.2` | was 0.6; less drag → keeps momentum, no instant speed-drop |
| `angular_damp` | `4.0` | unchanged |
| `grip` | `3.0` | unchanged |
| `bounce` / `invert_pitch` / `invert_roll` | `0.3` / `true` / `true` | unchanged |

**`Ship.cs` — keep `ContinuousCd = true;`** (added in `_Ready`, ~L55). It's a partial mitigation for the ground bug
(swept collision); §3.2 hardens it. **Keep it.**

**Temp diagnostics + commit — ✅ already done by session A (2026-06-02).** Both temp prints were removed (the
`WorldTile.cs` one-shot collider sanity print — which proved the terrain collider **valid**: 128 faces, layer 1,
Y≈38, see §3.2 — and the `Ship.cs` throttled contact print), and the baseline was committed (`settings/settings.cfg`
`[flight]` + `Ship.cs` CCD, staged **by path**). **If you ever start from before it:** stage those two by path,
**never `git add -A`**, **never revert the user's settings values**. §3.2 re-instruments a fresh contact diagnostic if
the ground bug needs live discrimination.

---

### 3.1 — World rescale ×5 + realistic units *(re-bake; determinism-critical)*

**The goal:** the city **and** the overall map become **~5× larger in linear extent** (≈25× area), and the many
**unrealistic size/distance values become believable**. The user reduced top speed 5× (1000→200 m/s); a 5× bigger
world restores the sense of scale and gives room to fly.

**⚠️ The gotcha that makes this non-trivial.** `settings.cfg [world] extent` (read by `BakeSettings.Resolve()` →
`WorldBaker.Bake(seed, extent)` → `WorldGenerator.Build(seed, extent)`) sets the world **width**, but
**`WorldGenerator.cs`'s terrain + city constants are ABSOLUTE METRES, not fractions of `extent`** (L16–45:
`RingInner=2400`, `CityCx=-1400, CityCz=-200, CityR=1500`, `Coast*`, `Bay*`, `Islands[]`, `Block=90`, building
height/footprint ranges). **So bumping `extent` alone leaves the existing ~8 km landmass + city sitting in the middle
of a bigger empty ocean** — wrong. A real 5× rescale = **scale those constants too.**

**Recommended approach — a single `WorldScale` factor (cleanest, keeps determinism reasoning simple):**

1. Introduce `private const float WorldScale = 5.0f;` in `WorldGenerator.cs` and **multiply the horizontal/spatial
   constants by it** (positions, radii, spacing): `RingInner`, `RingOuter`, `CityCx/Cz/R`, `CoastX/Amp/Nz`, `Bay*`,
   `Islands[].Cx/Cz/R`, `Block`, `BeachBand`, `OceanSlope`, and the noise **frequencies** (`Frequency = 1/2200` →
   `1/(2200*WorldScale)`, same for the coast noise — so features stay proportionally the same shape, just bigger).
   Set `[world] extent = 40000.0` to match (`8000 × 5`).
2. **Heights are the realism subtlety — do NOT blanket ×5.** Mega towers are already `420–880 m` tall and capped to
   `Block*0.92 ≈ 83 m` **wide** (needle-thin — one of the "unrealistic values"). Scaling heights ×5 → 4400 m towers
   (absurd). Instead: **scale horizontal ×5, keep vertical roughly as-is (or ×1–2)**, which **fixes the aspect ratio**
   — a 5× wider `Block` (→450 m) lets footprints widen to believable building widths while heights stay human-plausible.
   Terrain elevation features (`MountH=760`, `PlateauH=38`, `OceanDepth=110`, island `H`) can scale **modestly**
   (×1.5–2, not ×5) so the mountain ring still reads big without becoming a wall.
3. **City density is a genuine design choice — flag it, pick on playtest.** With `CityR` ×5 the city **footprint** is
   ×25. Two ends of the dial:
   - **(a) Bigger blocks, same tower count** (`Block` ×5): ~same building count spread over 25× area → grand, sparse,
     wide avenues.
   - **(b) Same blocks, more towers** (`Block` ~unchanged, `CityR` ×5): ~25× the towers → a dense megacity (≈more
     `MultiMesh` instances — still fine; colliders are near-field only).
   - **Recommend a blend:** `Block` ×~2–3 (wider, realistic superblocks) + the larger `CityR` → a believably **large,
     reasonably dense** city. Tune the exact factors against the playtest; **`log()`/note the resulting building count.**

**Knock-on "units" that MUST scale with the world (else it won't render right at 40 km):** these are `[Export]`s on
`WorldLoader` (`scripts/world/WorldLoader.cs`) and config:

| knob | current | scale to (≈) | why |
|---|---|---|---|
| `WorldLoader.TileSize` | `250` | `~1250` (×5) | else `40000/250 = 160² = 25 600` tiles (was 1024). Keep tile **count** ~constant. |
| `WorldLoader.ViewDistance` | `5000` | `~12000–20000` | building cull distance scales with the bigger world |
| `WorldLoader.TerrainViewDistance` | `12000` | `~50000+` | must cover the new diagonal (≈56 km) or the far ring dither-culls |
| `WorldLoader.ViewFadeMargin` | `600` | `~3000` | proportional fade band |
| `WorldLoader.ColliderTileRadius` | `2` | re-evaluate (`1`?) | ±2 tiles at 1250 m = ±2500 m lookahead — likely drop to `1` |
| `[camera] far` (settings.cfg) | `10000` | `~50000` | hard draw distance must reach the new horizon |
| `[fx] fog_density` (settings.cfg) | `~0.00018` | `~0.00004` (÷~5) | fog opacity grows with distance; thin it so the bigger basin reads (tune live in §3.3) |
| `[game] scale` (settings.cfg) | `4.0` | **decide** | this separately enlarges **buildings**; with the world rescale, reconsider — likely **reduce toward 1.0** so towers aren't double-enlarged. **Confirm on playtest.** |

The **ocean plane auto-sizes** from `[world] extent` (`Game.SpawnOcean` reads it) — good, no change. The **spawn**
(`Game.SpawnWorld`, `CityCenter + (0,160,700)`) is relative to the (rescaled) city centroid — re-check the height/offset
reads well at the new scale.

**Determinism + re-bake (CLAUDE.md "Bake determinism"):**
- Changing constants changes the **layout**, not the **schema** → **`WorldData.FormatVersion` stays `1`** *unless* you
  change `HeightmapResolution` `N` (don't — keep `N=256`; the brief's frequency-scaling keeps detail proportional).
- **Re-bake:** `./run.sh build && ./run.sh bake` → new `world/world_8km.res`. Then **bake again and diff** — same
  `(seed, extent)` must give **byte-identical** bytes (`sha256sum world/world_8km.res` twice). If they differ you've
  introduced order/`Date`/`float`-rounding nondeterminism — keep the seeded-RNG call order/count **identical** (you're
  only changing constant *values*, so it should stay deterministic).
- **Commit the new `world/world_8km.res`** (it's tracked authored content) alongside the generator + loader changes.

**Commit (Pass 1):** by path — `scripts/world/WorldGenerator.cs`, `scripts/world/WorldLoader.cs`, `world/world_8km.res`,
and `settings/settings.cfg` (`[world] extent`, `[game] scale`, `[camera] far` — by path). Message e.g.:
`World: rescale map + city ~5x (extent 40km) + realistic proportions; rescale tile grid/view; re-bake`.

---

### 3.2 — Collision solidity & comfort *(the anchor bug + bundled physics)*

#### (A) The transparent city ground — the headline bug

**Symptom:** the car passes **through** the city street/ground instead of bouncing (user: *"ground is still
transparent"*). **What's already been ruled out (don't re-investigate):** an instrumented run proved the terrain
collider **is built, valid, and correct** — `Tile_8_15 terrain collider: 128 faces, layer=1, meshY [38.0..38.0]`
(a non-empty `ConcavePolygonShape3D`, on physics layer 1 = "ship", at the flat plateau height Y=38). So it is **NOT**
a missing/empty/wrong-layer/wrong-height collider.

**Therefore the cause is tunnelling / a thin-shape contact gap:** the terrain collider is a **zero-thickness trimesh**
(`Mesh.CreateTrimeshShape()`, `TerrainBuilder.BuildTerrainCollider`). A fast `RigidBody3D` slips through a thin static
trimesh between physics ticks (at 200 m/s ÷ 160 tps = 1.25 m/tick vs a 1 m-tall car box against an infinitely-thin
plane), and **Jolt's CCD (`ContinuousCd`, already on) is known-weak against concave/trimesh shapes** — so it doesn't
reliably catch it. (`ContinuousCd=true` was the first attempted fix; it + the 5× lower speed did **not** fully solve it.)

**Confirm the cause, then pick the fix.** Re-instrument a throttled contact diagnostic to read it live (the §3.0
baseline removed the temp prints — re-add one in `Ship._IntegrateForces`: for each contact, tag
`state.GetContactColliderObject(i) as Node` → `IsInGroup("terrain")?"T":IsInGroup("building")?"B":"?"` and `GD.Print`
it ~4×/s):
1. Slow drift down onto a street, fast dive into a street, ram a building head-on. The tags are `T` (terrain) / `B`
   (building) / `?`. **Slow bounces but fast tunnels → pure tunnelling. Even slow passes through with
   no `T` line → a trimesh contact-generation gap.** Building ram shows `B` → the collision path itself works.

**Robust fixes (in preference order — combine 1+4):**
1. **A tunnel-proof near-field ground collider** instead of (or under) the thin trimesh. Best options:
   - **`HeightMapShape3D`** for the near tiles — Jolt supports it (and allows non-uniform scaling, unlike GodotPhysics);
     a real solid heightfield is far more tunnel-resistant than a trimesh surface. Build it from the same heightmap
     slice the tile's mesh uses (`WorldData.HeightAt`), so it lines up exactly. *(Cleanest; a bit more code.)*
   - **OR a thick solid box floor** under the flat **city plateau** (the plateau is flat at Y=`PlateauH`): a `BoxShape3D`
     spanning the city, top at the plateau height, extending **well downward** — cannot be tunnelled. Simple; covers the
     city ground (where the user hits it) but not the sloped terrain. *(Quick win; pair with 1a for full terrain.)*
2. **Bump the physics solver's robustness for the car:** ensure `ContinuousCd` stays on; consider a slightly larger
   collision **margin** on the car's box, and confirm the near-field colliders exist **before** the car arrives
   (they're built within `ColliderTileRadius` of the player — verify the spawn tile is covered at the new scale).
3. **Cap downward approach speed near the ground** — this is exactly **(E) hover/landing assist** below; it **doubles as
   a tunnelling mitigation** (a car that can't slam down at 200 m/s can't tunnel). Strong synergy — do (E).
4. **Re-verify after §3.1's rescale** (tile size, collider radius changed).

#### (B) General high-speed anti-tunnelling (all static colliders)

Apply the same robustness to **buildings + terrain** generally, not just the city street: at the 200 m/s clamp a
glancing hit on a thin face can still slip through. Buildings are **solid `BoxShape3D` volumes** (`TileBuilder.
BuildColliders`) so they're far less tunnel-prone than the trimesh — **verify** they bounce at a full-speed ram; if any
slip through, the same mitigations (CCD + margin, and the speed clamp already caps the worst case) apply. **Don't
over-engineer** — the speed clamp (200) + CCD + a solid near-field ground likely covers it; confirm by ramming at full
speed and watching for any pass-through.

#### (C) Recover / flip-upright key

Add a **recover** affordance for when the car wedges into geometry or flips after a hard bounce (beyond the existing
auto-level, which only rights gentle tilts and can't free a stuck body):
- **Register an action** in `Game.RegisterInput` (e.g. `AddAction("recover", new[] { Key.Home })` or `Key.G`/`Key.Backspace`
  — pick an unused key; current map uses WASD/arrows, Shift/Space, Ctrl/X/C/V, Q/E, Tab, R, F, Esc).
- **Implement in `Ship.cs`** (read the action in `_PhysicsProcess` or `_UnhandledInput`): zero `AngularVelocity`, set
  the basis upright (preserve heading/yaw, clear pitch/roll), damp `LinearVelocity`, and apply a **small upward nudge**
  to lift clear of whatever it's touching. Keep it a discrete press, not a hold.

#### (D) Near-ground hover / landing assist

When the car is within `H` metres of the surface below it, apply a **soft upward restoring force + descent damping** so
you can **settle and land** (and so you **can't slam down hard enough to tunnel** — see (A.3)):
- **Find the ground height** under the car: sample `WorldData.HeightAt(x,z)` (expose it via the `WorldLoader`/world data
  the ship can reach), **or** cast a short downward ray with `PhysicsDirectSpaceState3D.IntersectRay` from the car. The
  raycast is self-contained (no new ship→world coupling) and also catches **building rooftops**, not just terrain —
  **prefer the raycast.**
- **Force model:** within the assist band, add upward force ∝ how close you are (and ∝ downward speed), framerate-
  independent. Expose the band height + strength as `[Export]`/`[flight]` knobs (`hover_height`, `hover_strength`,
  `hover_damp`) so it's tunable live. **Don't fight the player** — it should ease you to a hover near the ground, not
  hard-lock an altitude (you must still be able to fly low and land deliberately).
- **Tune it at §3.1's final scale.**

**Commit (Pass 2):** by path — `scripts/Ship.cs`, `scripts/world/TerrainBuilder.cs`, `scripts/world/WorldLoader.cs`,
`scripts/Game.cs`, and (if you added hover/recover keys) `settings/settings.cfg [flight]` (by path). **Remove the
`Ship.cs` contact-print** before this commit. Message e.g.:
`Collision: solid tunnel-proof ground + anti-tunnel + recover key + near-ground hover/landing assist`.

---

### 3.3 — Dystopian palette re-grade *(GPU taste loop; cold, dark, neon-preserved)*

**The shift.** Task 6 made everything a **warm golden-purple synthwave dusk**. The user now wants the **opposite
temperature**: *"change ambient color from reddish to some subtle dark cyan/violet/gray — dystopian darker palette."*
So **cool it and darken it** — think cold Blade-Runner haze, not warm California sunset. **This re-grades Task 6** (and
shifts the stated art direction in `GAME_SPEC.md §7.6` — session A updates the docs, see §6). It is **not** a full
revert to neon-night; it's a **colder, darker, desaturated dystopian** grade with the **neon still glowing**.

> **Same `[fx]` discipline as Task 6.** The **colours** live in code/shader **defaults** (retune them there). The
> **numeric intensities** (`fog_density`, `sky_energy`, `exposure`, `saturation`, `glow_intensity`, `bloom`,
> `cloud_*`, `water_*`) are `[fx]` keys you **tune live in your local settings.cfg and REPORT** — session A applies
> them on review. **Do not blanket-commit settings.cfg.** Shaders hot-reload (leave `play` running; save the
> `.gdshader`); C#/`Game.cs` needs a rebuild.

**Starting values (current → suggested cold-dystopian) — all *tune live*, this is a taste loop:**

**Sun** — `Game.cs EnsureEnvironment` (~L261–263):

| | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `LightColor` | `(1.0, 0.62, 0.38)` gold-orange | `(0.55, 0.62, 0.80)` cool steel-violet | a cold key, not a sunset |
| `LightEnergy` | `0.6` | `~0.45` | dimmer (darker mood) — tune vs neon wash-out |
| `Rotation` | `(-18°, 35°, 0)` low raking | keep low, or raise toward `-30°` | cold overcast can sit a touch higher |

**Environment** — `Game.cs BuildEnvironment`:

| | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `FogLightColor` (~L316) | `(0.42, 0.26, 0.30)` mauve/peach | `(0.13, 0.15, 0.20)` cold slate-violet | **the biggest "distance reads cold" lever** — desaturated, dark |
| `AmbientLightEnergy` (~L291) | `0.25` | `~0.18–0.22` | darker; keep ≥~0.15 so towers don't go pure black |
| `VolumetricFogAlbedo` (~L328) | `(0.24, 0.16, 0.22)` warm | `(0.14, 0.16, 0.22)` cold | if volumetric is enabled |
| `VolumetricFogEmission` (~L329) | `(0.16, 0.07, 0.10)` warm | `(0.06, 0.09, 0.14)` cold | faint cold self-glow |
| `TonemapWhite` (~L298) | `6.0` | **keep `6.0`** | keeps bright neon **coloured** (don't drop — it's what stops neon clipping to white) |
| `GlowHdrThreshold` (~L307) | `0.95` | **keep ≥0.95** | only the bright windows bloom, not the (now darker) sky |
| `AdjustmentContrast` (~L349) | `1.08` | `~1.1` | a touch more bite for the cold look |

**Sky** — `shaders/sky.gdshader` defaults (L12–19):

| uniform | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `zenith_color` | `(0.08, 0.05, 0.22)` indigo | `(0.04, 0.05, 0.14)` darker blue-violet | deep, cold, still visible |
| `horizon_color` | `(0.50, 0.17, 0.42)` magenta-rose | `(0.16, 0.16, 0.26)` desaturated violet-gray | kill the rose; cold dusk band |
| `glow_color` | `(0.85, 0.42, 0.28)` gold-orange | `(0.16, 0.26, 0.34)` dim cold cyan-teal | the horizon glow goes **cold**, dim |
| `horizon_falloff` | `2.8` | `~3.4` | tighter/dimmer band (less blooming) |
| `cloud_lit_color` | `(1.0, 0.62, 0.45)` warm | `(0.34, 0.44, 0.58)` cold | cloud undersides catch a **cold** glow |
| (drive `sky_energy` lower via `[fx]`) | `~0.9` | `~0.5–0.7` | darker sky overall |

**Water** — `shaders/water.gdshader` defaults (L8–10):

| uniform | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `deep_color` | `(0.04, 0.05, 0.11)` | `(0.03, 0.05, 0.09)` | deep cold body (barely change) |
| `shallow_color` | `(0.14, 0.12, 0.20)` | `(0.08, 0.12, 0.17)` | cold teal-violet, darker |
| `fresnel_color` | `(1.0, 0.50, 0.42)` gold-pink | `(0.35, 0.55, 0.70)` cold cyan rim | the sea rim catches a **cold** sky, not a sunset |

**Terrain biomes** — `TerrainBuilder.Palette` (L11–19) — **desaturate + cool, keep legible (dark ≠ muddy black):**

| biome | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| Ocean (seabed) | `(0.04, 0.06, 0.10)` | `(0.03, 0.05, 0.09)` | mostly hidden — low priority |
| Beach | `(0.64, 0.50, 0.40)` rosy | `(0.40, 0.41, 0.45)` cold gray-sand | drained of warmth |
| City | `(0.11, 0.08, 0.12)` | `(0.09, 0.09, 0.12)` | dark cold asphalt |
| Desert | `(0.58, 0.38, 0.28)` rosy tan | `(0.36, 0.35, 0.36)` cold gray-tan | dystopian dead ground |
| Hills | `(0.36, 0.28, 0.21)` umber | `(0.24, 0.25, 0.29)` cold gray-violet | |
| Mountains | `(0.34, 0.28, 0.33)` mauve-gray | `(0.26, 0.28, 0.34)` cold blue-gray | |

**`[fx]` to tune live + REPORT** (don't commit): `fog_density` (raise slightly for a murkier dystopian haze, **but**
remember §3.1 thinned it for the bigger world — net: tune for "cold + sees a few km"), `saturation` (drop toward
`~1.05–1.15` — less candy, more grim), `exposure` (drop slightly for darker), `sky_energy` (`~0.5–0.7`), `glow_*`
(keep neon blooming). Keep the city's **wet-street SSR** (`TerrainBuilder.WetMaterial`) — cold neon reflecting in wet
streets is peak dystopia.

**Hard requirement: neon must survive.** Same as Task 6 — keep towers dark (don't touch `building.gdshader`), ambient
modest, `TonemapWhite` high, `GlowHdrThreshold ≈0.95+`. A **darker** scene actually helps neon pop; the risk here is
the opposite of Task 6 — going so dark/desaturated the world reads as **murky black mud**. Keep it **cold but legible**.

**Commit (Pass 3):** by path — `scripts/Game.cs`, `shaders/sky.gdshader`, `shaders/water.gdshader`,
`scripts/world/TerrainBuilder.cs`. **Do NOT commit `settings/settings.cfg`** (report the `[fx]` values). Message e.g.:
`Palette: re-grade warm dusk -> cold dystopian (cyan/violet/gray), neon preserved`.

---

## 4. Correctness / determinism / taste points

1. **Bake determinism (§3.1) is non-negotiable** — bake twice, `sha256sum` must match. You change constant **values**,
   never the RNG call **order/count**, so it stays deterministic. If it doesn't, you accidentally reordered a draw.
2. **The rescale is "scale constants + re-bake," not "rewrite the generator."** Keep the heightmap math, the placement
   loop, the determinism kit (`MixSeed`/`RoundHalfAway`), and `N=256` intact. `FormatVersion` stays `1` (no schema change).
3. **Heights are the realism trap** — scale **horizontal ×5**, **vertical ≈×1–2**. ×5 on everything makes 4 km towers.
4. **The ground bug is tunnelling, not a missing collider** — the collider is proven valid (§3.2). Don't waste time
   re-checking layers/heights; go straight to a tunnel-proof shape + the hover cap.
5. **Hover/landing must not fight the pilot** — ease to a hover, never hard-lock altitude; you must still land deliberately.
6. **Palette: cold ≠ black.** The failure mode is murky mud. Keep it legible; lean on the **cold-neon-vs-cold-haze**
   contrast, not on crushing everything dark.
7. **Neon survives both passes** — never edit `building.gdshader`; fix any neon problem environment-side.
8. **Don't break shader bindings** — keep every uniform name `Game.cs` sets (`sky_energy`, `cloud_coverage`,
   `cloud_speed`, `ripple_speed`, `water_energy`). You change **defaults**, not the interface.
9. **`settings.cfg` is the user's live file** — stage only the sections a pass owns, **by path**, **never revert the
   user's values**, **never `git add -A`**. The `[fx]` colour-intensity values you find are **reported, not committed**.
10. **FPS at 40 km** — the rescale adds area, not necessarily geometry (depends on the density choice). Watch frame time
    after §3.1; the scaled `TileSize`/view distances keep the live set bounded. Volumetric fog + shadows stay **off**.

---

## 5. Verify (per pass)

- **Every pass:** `./run.sh build` → **0/0**; `./run.sh build && ./run.sh check` → exits 0, prints the loader line
  (counts/size reflect the rescale after §3.1). Headless **cannot** show the look or the feel — the real test is `play`.
- **§3.1:** `./run.sh bake` twice → `sha256sum world/world_8km.res` identical. `./run.sh play` → map + city ~5× bigger,
  proportions believable, renders + holds FPS, identical relaunch.
- **§3.2:** `./run.sh play` → the three ground maneuvers (slow drift / fast dive / building ram) all **bounce, none
  tunnel**; recover key rights + frees the car; hover lets you settle + land; condition drops, game never ends.
- **§3.3:** `./run.sh play` → cold dystopian dusk reads, distance fades cold, land/sea cold-but-legible, **neon blooms**,
  identical relaunch.
- **Screenshots (optional, helpful):** run `godot-mono --path . &` (note PID), `sleep ~22`, `spectacle -bnf -o /tmp/x.png`,
  `kill -9 $PID; pkill -9 -x godot-mono` — **as one background Bash command** (foreground `sleep` is blocked). Boots
  fullscreen 4K so `-f` grabs the frame.

---

## 6. Report back to session A (for review)

- `git status` + `git diff --stat` per pass, and the per-file edits grouped by pass (§3.0–§3.3).
- `./run.sh build` + `./run.sh check` output (0/0; loader line with the **new** rescaled counts/size).
- **Determinism proof** for §3.1: the two `sha256sum world/world_8km.res` hashes (must match).
- The **density decision** you landed on for §3.1 (bigger blocks vs more towers vs blend) + the resulting building count,
  and the final `WorldScale`/`Block`/`TileSize`/view-distance/`[game] scale` values.
- The **root-cause confirmation** for the ground bug (which §3.2 maneuver tunnelled) and **which fix** you shipped
  (HeightMapShape3D / solid plateau box / both) + the hover + recover key you chose.
- A description of each **GPU `play` playtest** against §1 (headless renders none of it) — the rescaled world, the
  solid ground + hover/recover feel, and the cold dystopian look (attach §5 screenshots if captured).
- **The recommended `[fx]` values** for §3.3 (and any `fog_density`/`far` you tuned in §3.1) — every key + the value you
  landed on, for session A to transcribe (you did **not** commit settings.cfg's `[fx]`).
- **Two docs items for session A** (session A owns docs — flag, don't edit): **(1)** this re-grade shifts the art
  direction — `GAME_SPEC.md §7.6` says "synthwave dusk (warm)"; update it to the **cold dystopian** direction (and note
  it in §11 Open Decisions). **(2)** slot this task into the `§9` roadmap with a number and tick its boxes.
- **Confirm** you did **not** touch `building.gdshader`, the generator's algorithm/determinism kit (only its
  **constants**), `CameraRig`, `Hud` layout, `DamageComponent` logic, or `Traffic.cs`/glTF.
- **Commit each pass by path** (per §3.0–§3.3 messages); **never `git add -A`**; **do not** commit `settings.cfg [fx]`;
  **do not** tick `§9`. End every commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **Rewriting the generator algorithm** — you scale **constants** + re-bake; the heightmap/placement/determinism math
  is unchanged. **Changing `HeightmapResolution N`** (would force a `FormatVersion` bump) — keep `N=256`.
- **`building.gdshader`** (the neon identity) — confirm it still reads; **never edit it**.
- **Ambient traffic** (`Traffic.cs`) → **Task 7** (only **note** the rescale/palette interaction if Task 7 is merged).
  **glTF models** (`MeshRegistry`) → **Task 8**.
- **New gameplay** — missions/economy/AI behaviour/water-as-gameplay/damage-consequences are all deferred (§11).
- **Camera / HUD / damage logic** — `CameraRig`, `Hud` layout, `DamageComponent` math (you may add `[flight]` hover keys
  and a `recover` action, but don't rework the camera or damage model). HUD **m/s → km/h** display is a *trivial optional*
  units nicety (`Hud.cs` shows `"{sp:F0} m/s"`); do it only if quick, else note it.
- **Screen-space FX** — `ScreenFX`/`post.gdshader`/`speed_lines.gdshader`/`RainFX` and their `[fx]` keys.
- **Sun shadows / volumetric fog** — stay **off** for FPS; enable only behind `[fx]` with the frame time confirmed.
- **Editing `docs/`/`CLAUDE.md` prose or ticking `§9`** — session A owns all docs on review (you only **report** the
  art-direction shift + the roadmap slot).
