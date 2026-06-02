# Task 9 — World rescale ×5 + realistic units (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** — self-contained; you do not need the planning chat.
> When you finish, you **verify** (`build` + headless `check` + a hands-on **GPU** `play`), **commit by path**, and
> report back to session A for review. One such doc lives in `docs/tasks/` per task.

> **Status.** This is **§9 Task 9** — the **first of three** tasks session A split out of the bundled backlog
> (`docs/tasks/BACKLOG-world-rescale-physics-and-palette.md`). It was **§3.1** of that bundle, now promoted to its
> own task. **Task 10** (collision solidity & comfort) and **Task 11** (dystopian palette re-grade) follow — do
> **not** do them here. Tasks 1–8 are merged and green (the car flies, the 8 km world bakes + renders, terrain/
> ocean/dusk exist, traffic roams, the glTF seam is proven).

> **What this task is.** Make the city **and** the overall map **~5× larger in linear extent** (≈25× area):
> `[world] extent` **8 000 → 40 000 m**, with the world's **metre constants scaled to match** so the landmass +
> city actually grow (not just a bigger empty ocean), and the now-**unrealistic size/distance values rationalised**
> so proportions read believably (the needle-thin megatowers especially). Then **re-bake**. It is a **data + tunable
> rescale, not a generator rewrite** — you change the generator's **constants**, never its heightmap/placement/
> determinism **math**.

> **What this task is NOT.** No collision/physics work (Task 10). No palette/colour work (Task 11). No new gameplay.
> No glTF (Task 8, done). Do **not** rewrite `WorldGenerator`'s algorithm, change `HeightmapResolution N`, or touch
> `building.gdshader`. Do **not** commit `settings/settings.cfg` (report its values — see §3.C).

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (skim **`docs/GAME_SPEC.md` §7.1** = bake is
data not geometry, **§7.7** = determinism; and **`CLAUDE.md`** = the `[Export]`/`settings.cfg` conventions + the
**bake-determinism rules** + "GPU visuals can't be confirmed headless"). The world is generated **once** by a pure-C#
`WorldGenerator.Build(seed, extent)` into `res://world/world_8km.res`, then loaded + rendered as a fixed tile grid of
`MultiMesh` batches. The runtime reads the world's size from the **baked `WorldData.WorldExtent`**, so a re-bake at a
new extent flows through automatically — *except* the few knobs called out below, which are sized in absolute metres
and must be scaled by hand.

This rescale came from an interstitial test session: the user dropped top speed 5× (1000 → 200 m/s) and asked to make
the world **5× bigger** to restore the sense of scale + fix the many "unrealistic values" (a 40 km basin gives room to
fly at 200 m/s, and lets megatowers widen to believable proportions).

### 0.1 The surfaces you touch

| File | What changes |
|---|---|
| `scripts/world/WorldGenerator.cs` | the **metre constants** (positions/radii/spacing/bands/noise frequencies ×5; heights ≈×1.5–2; `Block` widened) — **NOT** the algorithm/determinism kit |
| `scripts/world/WorldLoader.cs` | the `[Export]` LOD/tile knobs (`TileSize`, `ViewDistance`, `TerrainViewDistance`, `ViewFadeMargin`, `ColliderTileRadius`) — **C# defaults are authoritative** (`Game.cs:108` does `new WorldLoader{…}`; **no scene overrides them**) |
| `world/world_8km.res` | **re-baked** (tracked authored content — commit it) |
| `settings/settings.cfg` | `[world] extent`, `[camera] far`, `[fx] fog_density` — **tune locally + REPORT; do NOT commit** (session A applies — see §3.C) |

**Not yours:** `WorldGenerator`'s math (heightmap/placement loop/`MixSeed`/`RoundHalfAway` — you change **constant
values** only), `HeightmapResolution N=256`, `building.gdshader` (confirm only), `Ship.cs`/colliders (Task 10),
`Game.cs` env/sky/water/`TerrainBuilder.Palette` (Task 11), `Traffic.cs` (Task 7 — **note** the interaction in §3.G,
don't rewrite it), `MeshRegistry`/glTF (Task 8).

---

## 1. Definition of Done

- `./run.sh build && ./run.sh check` → **0/0**, exits 0, prints the loader line with the **new** rescaled counts/size,
  e.g. `:: WorldLoader: <N> objects, 1024 terrain tiles (<P> populated), 1250 m.` (tile **count** stays ~1024).
- `./run.sh bake` **twice** with the same `(seed, extent)` → **byte-identical** `world/world_8km.res`
  (`sha256sum` matches). *(Determinism is reproducibility at the **new** constants — the file legitimately differs from
  the old 8 km bake; what must match is bake-vs-bake.)*
- `./run.sh play`: the map + city are **~5× larger** in linear extent, **proportions read realistically** (building
  height-vs-width no longer needle-thin; street/block widths, city-vs-world ratio believable); it **renders + holds
  FPS** (nothing dither-pops or murk-clips — tile grid, view distances, fog, `[camera] far` all scaled); **identical
  every launch**.

---

## 2. Preconditions

- Branch **`dev6`**. Confirm green first: `./run.sh build && ./run.sh check` → exits 0 and prints the **pre-rescale**
  loader line `:: WorldLoader: 315 objects, 1024 terrain tiles (85 populated), 250 m.`
- `sha256sum world/world_8km.res` and note it (you'll prove the new bake is deterministic the same way).
- Read **CLAUDE.md → "Bake determinism"** (seeded-RNG call order/count must stay identical; you change constant
  **values**, so it stays deterministic) and **GAME_SPEC §7.1/§7.7**.

---

## 3. Work items

### 3.A — Scale the generator constants (`scripts/world/WorldGenerator.cs`)

**The gotcha that makes this real work:** `[world] extent` sets the world **width**, but `WorldGenerator`'s terrain +
city constants are **absolute metres, not fractions of `extent`** (L16–45). Bumping `extent` alone leaves the existing
~8 km landmass + city marooned in the middle of a bigger empty ocean. A true 5× rescale = **scale those constants too.**

**Recommended structure — three explicit factors at the top of the class, multiplied into the constant definitions**
(`const float A = 2400.0f * WorldScale;` is a legal const expression; the `static readonly` arrays/`Vector2`s can use
the factors too). This keeps the diff an obvious "× a constant" on each value, which visibly **preserves determinism**
(same RNG call order/count; only constant *values* change) and makes future rescales / playtest tuning a one-line edit.

```csharp
private const float WorldScale  = 5.0f;   // horizontal: positions, radii, spacing, bands (÷ for noise frequencies)
private const float HeightScale = 1.75f;  // vertical: elevations — NOT ×5 (see below); tune ~1.5–2 on playtest
private const float BlockScale  = 2.5f;   // city density dial (see step 3) — Block ×this; tune on playtest
```

**1. Horizontal constants → `× WorldScale`** (positions, radii, spacing, shore/bay bands; **noise frequencies ÷
WorldScale** so feature *wavelengths* grow proportionally and the world keeps the same *shape*, just bigger):

| constant (L) | now | scale | constant (L) | now | scale |
|---|---|---|---|---|---|
| `RingInner` (16) | 2400 | ×5 | `BayCx/Cz/R/Edge` (19) | 600/1700/1400/650 | ×5 |
| `RingOuter` (16) | 4300 | ×5 | `OceanSlope` (20) | 16 | ×5 *(keeps the offshore depth ramp at 5× distance)* |
| `CityCx/Cz/R` (17) | -1400/-200/1500 | ×5 | `BeachBand` (21) | 420 | ×5 |
| `CoastX/Amp/Nz` (18) | 900/520/300 | ×5 | `Islands[].Cx/Cz/R` (24–28) | … | ×5 *(positions+radii; **not** `H`)* |
| `CoastFreq` (18) | 1/1500 | **÷5** | `fnl.Frequency` (77) | 1/2200 | **÷5** |
| `Block` (32) | 90 | **× `BlockScale`** (step 3) | `coastNz.Frequency` (87) | 1/3000 | **÷5** |

*(The plateau-flatten terms `CityR*0.7` / `CityR*0.3` at L105 are **fractions of `CityR`** → they follow
automatically. Leave them.)*

**2. Heights → `× HeightScale` (≈×1.5–2), NOT ×5 — this is the realism fix, do not get it wrong.** The megatowers are
already `420–880 m` tall but capped to `Block*0.92 ≈ 83 m` **wide** → needle-thin (one of the "unrealistic values").
Scaling heights ×5 makes **4 km towers** (absurd). Instead scale **horizontal ×5** (which lets footprints widen — step
3) and **vertical only ×1.5–2**, fixing the aspect ratio. Apply `HeightScale` to the **terrain elevation** constants:
`MountH` (16, 760), `PlateauH` (17, 38 — keep modest; it's where buildings sit), `OceanDepth` (20, 110), `LandBase` +
`RollAmp` (21, 10/60), `Islands[].H` (24–28). **Leave the building `*Height` `Vector2`s (39–40) as-is** — `30–880 m`
is already a realistic tower range.

**3. City density — a genuine design choice; pick on playtest, `BlockScale` is the dial.** With `CityR ×5` the city
**footprint** is ×25. `Block` (placement spacing) decides what fills it:
- **`BlockScale = 5`** → ~same tower count over 25× area = grand, sparse, wide avenues.
- **`BlockScale ≈ 1`** → ~25× the towers = a dense megacity (more `MultiMesh` instances — still fine; colliders are
  near-field only).
- **Recommend `BlockScale ≈ 2.5`** (superblocks ~225 m): wider, realistic blocks **and** a believably large, reasonably
  dense city — and the wider `Block` lifts the footprint cap (`Block*0.92`, L159–160) so megatowers can finally widen to
  believable proportions. **Tune against the playtest; note the resulting building count** (the loader prints it).

> **Determinism caveat (expected, not a bug):** changing these constants changes the **layout** (and thus the building
> **count** — the placement loop spans `CityCx±CityR` over `Block`), so the new `.res` legitimately differs from the old
> one. That is **not** nondeterminism. What must hold: **bake-twice → identical**. You only change constant *values*;
> the RNG draw **order/count per cell** is untouched, so it stays deterministic. (Sanity: `cellId = gz*100000+gx` at
> L147 stays unique — `gx,gz` are ~order 10²
 at this scale, far below 100000.)

### 3.B — Scale the loader's tile / LOD knobs (`scripts/world/WorldLoader.cs` `[Export]` defaults)

`WorldLoader` builds its grid as `side = Round(WorldData.WorldExtent / TileSize)` (L58) — so at 40 km with the old
`TileSize=250` you'd get `160² = 25 600` tiles (was 1024). Scale `TileSize` ×5 to keep the tile **count** ~constant, and
scale the view distances so the bigger basin doesn't dither-cull or clip:

| `[Export]` (L) | now | set to (≈) | why |
|---|---|---|---|
| `TileSize` (16) | 250 | **1250** (×5) | keep tile count ≈ `40000/1250 = 32² = 1024` (grid/MultiMesh structure unchanged) |
| `ViewDistance` (17) | 5000 | **~15000** | building cull distance scales with the bigger world (fog hides the boundary) |
| `TerrainViewDistance` (19) | 12000 | **~55000** | must cover the new diagonal (≈56 km) or the far mountain ring dither-culls (terrain is cheap ~128 tris/tile) |
| `ViewFadeMargin` (18) | 600 | **~3000** | proportional dither-fade band |
| `ColliderTileRadius` (23) | 2 | **1** | ±1 tile at 1250 m = ±1250 m lookahead (±2 was ±500 m at 250 m) — keep near-field physics cheap |
| `TerrainTileQuads` (20) | 8 | **keep 8** | at 1250 m tile / 156 m heightmap cell that's still ~1 quad per cell |

### 3.C — `settings/settings.cfg` (tune locally + **REPORT — do NOT commit**)

The user **edits this file live** and **session A owns it** (exactly as Task 7: session B reported `[traffic]` values,
session A applied them on review). Set these **locally** so you can bake + play, then **report the exact values** — do
**not** stage `settings.cfg`:

| key | now | set to (≈) | note |
|---|---|---|---|
| `[world] extent` | 8000.0 | **40000.0** | **coupled to the re-baked `.res`** — the bake reads it (`BakeSettings.Resolve`), and at runtime `Game.SpawnOcean` (L159) + `Traffic.Initialize` (L131) read it for ocean size + traffic wrap. ⚠️ **Flag this one "apply on review to match the committed 40 km res"** (see the handoff note below). |
| `[camera] far` | 10000.0 | **~55000** | hard draw distance must reach the new horizon (`Game.cs:283` → `rig.FarDistance`) |
| `[fx] fog_density` | 0.00022 | **~0.00004** (÷~5) | fog opacity grows with distance — thin it so the bigger basin reads. *(This is also a Task-11 palette knob; a provisional thin value is enough here.)* |

**`[game] scale = 4.0` is VESTIGIAL — do not touch it as a sizing lever.** It was the old corridor "enlarge buildings"
knob; **nothing reads it anymore** (the bake stores real metres — confirmed: the only mention is a comment in
`MeshRegistry.cs:25`). Ignore it (optionally note it for a future cleanup).

> **Handoff note (the one wrinkle):** you **commit the re-baked `.res`** (extent 40 000 baked in) but **not**
> `settings.cfg`. So between your commit and session A's review, `[world] extent` still reads 8000 on the branch. The
> world itself **renders fine at 40 km** (the loader reads `WorldData.WorldExtent` from the `.res`, not settings) — only
> the **ocean plane** (sized `2×extent`) and **traffic wrap bounds** read the stale 8000 (cosmetic: ocean a bit small,
> traffic wraps early). Session A fixes both by setting `extent=40000` on review. **Call `[world] extent=40000` out
> prominently in your report** so A applies it immediately.

### 3.D — Re-bake + determinism + the filename

1. `./run.sh build && ./run.sh bake` → writes a new `world/world_8km.res`.
2. **Bake again and diff:** `sha256sum world/world_8km.res` **twice** → must be **identical**. If they differ you've
   introduced order/`Date`/rounding nondeterminism — re-check you only changed constant **values**, not RNG call order.
3. `FormatVersion` **stays `1`** (no schema change — you kept `N=256`).
4. **Filename:** `world_8km.res` is now a misnomer at 40 km. **Recommended (optional, your call):** rename to a
   **scale-agnostic** `world_main.res` to end the rename treadmill — it touches exactly **two constants**
   (`WorldBaker.OutPath` L7, `WorldLoader.WorldResPath` L13) + a `git mv` of the file (+ a couple of stale `world_8km`
   comments). If you rename, **flag it for session A** (it updates the `CLAUDE.md` / `GAME_SPEC.md` project-layout
   references). If you'd rather keep churn minimal, **keep `world_8km.res`** and drop a one-line "name is historical —
   world is 40 km" comment. Either is fine; just be consistent.

### 3.E — Re-check spawn + ocean at the new scale (`scripts/Game.cs` — read, light touch)

- **Spawn** (`SpawnWorld`, L115): `_ship.GlobalPosition = CityCenter + (0,160,700)`. `CityCenter` is the placed-objects
  centroid → auto-follows the rescale. **Confirm the `(0,160,700)` height/offset still frames the city well** at 40 km
  (the city is now ~5× wider, so 700 m back may sit you inside it — nudge the offset up/back if the opening view is
  poor; keep it a small literal tweak, this is the only `Game.cs` change you may need).
- **Ocean** (`SpawnOcean`, L159–163): the plane auto-sizes from `[world] extent` (`size = 2×extent`) → no code change;
  it follows once `extent=40000` is applied.

### 3.F — Confirm the neon identity survives (no edit)

`building.gdshader` lays windows out in **world space** from the model matrix. The bake's per-instance transforms now
span 40 km — confirm windows still read at the new scale (`window_scale=1.0`, real metres — `MeshRegistry.cs:29`).
**Do not edit the shader**; just verify in `play`.

### 3.G — Traffic / glTF coexistence (note, don't rewrite)

Tasks 7 + 8 are merged. **Don't touch `Traffic.cs` or `MeshRegistry.cs`**, but note for your report:
- `Traffic` wraps at `±extent/2` from `Initialize(CfgFloat("world","extent"), …)` → **auto-follows** once `extent`
  scales. **But `[traffic] spawn_radius=1500`** now spawns all cars within 1.5 km of centre — tiny in a 40 km world, so
  traffic clusters at the city core. **Recommend scaling `[traffic] spawn_radius` ~×5 (→ ~7500)** so cars spread — but
  that's a `[traffic]` key → **report it for session A**, don't commit it.
- The glTF seam is mesh-only and scale-agnostic → unaffected (confirm the Taper/Round towers still wear the test model
  at the new scale during your playtest).

---

## 4. Correctness / determinism / taste points

1. **Bake determinism is non-negotiable** — bake twice, `sha256sum` must match. You change constant **values**, never
   the RNG call **order/count**. If it doesn't match, you reordered/added a draw.
2. **Scale constants, don't rewrite the generator.** Heightmap math, placement loop, `MixSeed`/`RoundHalfAway`, `N=256`
   all stay. `FormatVersion` stays `1`.
3. **Heights are the realism trap** — horizontal ×5, vertical ≈×1.5–2. ×5 on everything = 4 km towers.
4. **The runtime size comes from the baked `.res`** (`WorldData.WorldExtent`), so the loader follows the bake — but
   `TileSize` (tile count), `[camera] far`, fog, ocean (`[world] extent`) and traffic bounds are sized in metres and
   **must** be scaled too, or it renders wrong at 40 km.
5. **`settings.cfg` is the user's live file** — tune locally, **report**, **never commit it**, **never `git add -A`**,
   **never revert the user's values**.
6. **FPS at 40 km** — the rescale adds **area**, and (depending on `BlockScale`) maybe more instances. Watch frame time
   after the re-bake; the scaled `TileSize`/view distances keep the live set bounded. Volumetric fog + shadows stay off.

---

## 5. Verify

- `./run.sh build` → **0/0**; `./run.sh build && ./run.sh check` → exits 0, prints the loader line (new counts/size).
- **Determinism:** `./run.sh bake` twice → `sha256sum world/world_8km.res` identical (paste both hashes in the report).
- `./run.sh play` (the real test — headless renders none of the look): map + city ~5× bigger, proportions believable
  (towers no longer needle-thin), renders + holds FPS, **identical relaunch**.
- **Screenshot (optional, helpful — I (session A) can read a PNG):** as **one background** Bash command
  (`godot-mono --path . >/tmp/run.log 2>&1 &` note PID; `sleep ~26`; `spectacle -bnf -o /tmp/rescale.png`; `kill -9
  $PID; pkill -9 -x godot-mono`). Boots fullscreen 4K so `-f` grabs the frame. *(See the `gpu-verify-city-visuals`
  recipe; the spawn vantage already frames the skyline — no temp edit needed.)*

---

## 6. Report back to session A (for review)

- `git status` + `git diff --stat`; the per-file edits (the constant table you applied, the loader knobs).
- `./run.sh build` + `check` output (0/0; loader line with the **new** counts/size).
- **Determinism proof:** the two `sha256sum world/world_8km.res` hashes (must match).
- The **density decision** (`BlockScale`) + `HeightScale` you landed on, and the resulting **building count**.
- The final `WorldScale` / `Block` / `TileSize` / view-distance values.
- **The settings values for session A to apply** (you did **not** commit `settings.cfg`): **`[world] extent=40000`
  (⚠️ apply on review to match the committed `.res`)**, `[camera] far`, `[fx] fog_density`, and the recommended
  `[traffic] spawn_radius` (~7500).
- Whether you **renamed** the `.res` (so A updates the doc layout references) or kept `world_8km.res`.
- A description of the **GPU `play` playtest** against §1 (the rescaled world, the believable proportions, FPS) +
  the optional screenshot.
- **Confirm** you did **not** touch the generator's algorithm/determinism kit (only its **constants**),
  `HeightmapResolution N`, `building.gdshader`, `Ship.cs`/colliders, `Traffic.cs`, or the Task-11 palette surfaces.

**Commit (by path):** `scripts/world/WorldGenerator.cs`, `scripts/world/WorldLoader.cs`, `world/world_8km.res`
(+ the renamed file if you renamed it), and `scripts/Game.cs` **only if** you nudged the spawn offset. **Never
`git add -A`. Do NOT commit `settings.cfg`. Do NOT tick `§9`** (session A owns docs + the roadmap tick). End the commit
body with:
`Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

Message e.g.: `World: rescale map + city ~5x (extent 40 km) + realistic proportions; scale tile grid/view; re-bake`.

---

## 7. Out of scope (do not do here)
- **Rewriting the generator algorithm** / changing `HeightmapResolution N` (would force a `FormatVersion` bump).
- **Collision / physics** (solid ground, anti-tunnel, recover key, hover/landing) → **Task 10**.
- **Palette / colour** (sun/fog/sky/water/terrain re-grade) → **Task 11**.
- **`building.gdshader`** (confirm it still reads; never edit), `Traffic.cs` / `MeshRegistry` (note interactions only).
- **Committing `settings.cfg`** or **ticking `§9`** — session A owns both on review.
