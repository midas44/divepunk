# Task 6 — Synthwave dusk aesthetic pass (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 6. It is self-contained — you do not need
> the planning conversation. When you finish, you verify (build + headless check + a hands-on **GPU** playtest),
> commit, and bring results back to session A for review. One such doc lives in `docs/tasks/` per task.

> **What this task is.** A **look** pass. Retune the existing lighting/atmosphere stack — the **sun**, the
> `WorldEnvironment` (glow/bloom, fog, volumetric fog, SSR, tonemap, post-adjust), the **procedural sky**
> shader, the **water** shader, and the **terrain biome palette** — from the current **neon-night** mood to a
> warm **golden-purple synthwave dusk/sunset**. The bay, desert, hills, and coastline should read as a warm
> California-at-dusk landscape **while the neon city still glows**. Everything stays `[fx]`-gated and live-tunable.

> **What this task is NOT.** **No new systems, no geometry, no gameplay.** You are changing **colours and a
> handful of intensities**, not architecture. **No re-bake** — `world_8km.res` / `WorldGenerator` are untouched
> (this task never reads or writes world data). **Do not touch the flying car / camera / HUD / damage** (Ship,
> CameraRig, Hud, DamageComponent, ScreenFX) — the dusk mood is environmental, not screen-space. **Do not touch
> `building.gdshader`** (the neon identity — it already renders correctly in world space; you only *confirm* it
> still reads). **No traffic** (Task 7), **no glTF** (Task 8). **No water gameplay / foam / depth-transparency**
> (still deferred). This is a taste task: budget **real GPU tuning time** — headless cannot show any of it.

> **Scope discipline.** Edit exactly four code/shader files: **`scripts/Game.cs`** (`EnsureEnvironment` sun +
> `BuildEnvironment` + `BuildSky`), **`shaders/sky.gdshader`**, **`shaders/water.gdshader`**, and
> **`scripts/world/TerrainBuilder.cs`** (the biome `Palette`, the terrain `Material`, and the optional
> winding-flip cleanup — §3.5). **Do NOT commit `settings/settings.cfg`** — you tune the `[fx]` numeric knobs
> *live* to find the look, then **report the values** you landed on; session A applies them on review (the user
> edits that file live — see §3.6). **Do not edit `CLAUDE.md` or `docs/` prose** (session A owns docs) and
> **leave `docs/GAME_SPEC.md` §9 Task 6 unticked** (session A ticks it on review). Anything tempting beyond
> these four files is out of scope — see §7.

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (skim **`docs/GAME_SPEC.md` §5–§7** and
**`CLAUDE.md`** before starting). Tasks 1–5 are done: the arcade loops are stripped; the car is a `RigidBody3D`
6-DOF flyer with impulse damage (Task 2); the 8 km world is baked to `res://world/world_8km.res` (Task 3) and
renders at runtime as GPU `MultiMesh` building batches on a fixed tile grid (Task 4); and **terrain + ocean +
biomes** now give it ground and sea (Task 5). **What's missing is the mood.** The whole scene is still tuned for
the old **neon-night** corridor: a cool blue sun, a dark blue-purple sky, cool/dark fog, a cold-teal sea. The
design's art direction (§7.6, pillar 6) is **synthwave dusk** — *"Golden-purple sunset over the bay; neon still
glows; desert and water stay warm and legible. Bridges cyberpunk and California."* Task 6 makes that real.

### 0.1 The surfaces you touch (and who owns each colour today)

`Main.tscn` is a bare root with `Game.cs` attached — **`Game.cs` builds the entire environment in code**, so
there is **no scene/`.tscn` lighting to chase**. Every colour you'll retune lives in one of four places:

| Surface | File / location (current value) | What it controls |
|---|---|---|
| **Sun** (key light) | `Game.cs` `EnsureEnvironment`, ~L237–242: `LightColor (0.55,0.65,1.0)`, `LightEnergy 0.35`, `Rotation (-55°, 35°, 0)` | The single directional light. **Cool + steep** today → must become **warm + low (raking)** for sunset. |
| **Tonemap / ambient** | `Game.cs` `BuildEnvironment`, ~L268–276: `AmbientLightEnergy 0.25`, `TonemapMode Aces`, `TonemapWhite 6.0`, `TonemapExposure ← [fx] exposure` | Overall exposure + sky-sourced ambient. `White 6.0` keeps neon **coloured** (don't drop it far). |
| **Glow / bloom** | `Game.cs` `BuildEnvironment`, ~L280–288: `GlowHdrThreshold 0.95`, `GlowHdrScale 2.0`, `GlowBloom`, `GlowIntensity`, additive | The neon halo. Tune so windows bloom but the **warmer sky does not** (§4.2). |
| **Distance fog** | `Game.cs` `BuildEnvironment`, ~L293–300: `FogLightColor (0.10,0.12,0.22)`, `FogDensity ← [fx]`, `FogSkyAffect ← [fx]`, `FogAerialPerspective 0.4` | THE "what colour does the distance fade to" knob. **Cool blue** today → warm dusk haze (§4.1). |
| **Volumetric fog** | `Game.cs` `BuildEnvironment`, ~L303–314: `VolumetricFogAlbedo (0.07,0.08,0.17)`, `VolumetricFogEmission (0.05,0.02,0.10)` | The moody depth haze (gated `[fx] volumetric_fog`, currently **off** in settings). Warm it if you enable it. |
| **SSR / post-adjust** | `Game.cs` `BuildEnvironment`, ~L317–327: SSR steps/fades, `AdjustmentContrast 1.08`, `AdjustmentSaturation ← [fx]` | Wet reflections + the saturation/contrast push that makes neon sing. |
| **Sky** | `Game.cs` `BuildSky` (~L335–357 — sets `sky_energy`/`cloud_coverage`/`cloud_speed`) **+ `shaders/sky.gdshader` defaults** L12–19: `zenith_color`, `horizon_color`, `glow_color`, `horizon_falloff`, `cloud_color`, `cloud_lit_color` | The dome. **Colours live as shader defaults** (Game.cs only drives the three numeric uniforms) → retune them **in `sky.gdshader`**. |
| **Water** | `shaders/water.gdshader` defaults L8–10: `deep_color (0.02,0.06,0.10)`, `shallow_color (0.05,0.16,0.20)`, `fresnel_color (0.50,0.28,0.55)` | The sea. `fresnel_color` is the grazing-angle rim that **catches the sunset** — the key to "water stays warm." |
| **Terrain biomes** | `scripts/world/TerrainBuilder.cs` `Palette[6]` L11–19 + `Material()` L22–29 | Per-vertex biome albedo (Ocean/Beach/City/Desert/Hills/Mountains). Push **warm + legible**. |

**Not yours:** `building.gdshader` (neon — confirm only), `Traffic.cs` car material (Task 7), `ScreenFX` +
`post.gdshader` / `speed_lines.gdshader`, `hazard.gdshader` / `beacon.gdshader`, all world-data / loader /
collider code. See §7.

### 0.2 The art direction (what "synthwave dusk" means here)

- **A golden-purple sunset.** Overhead reads **deep indigo/violet** (not black); the horizon is a **warm band**
  — gold/orange low, blending up through **magenta/rose** into the violet zenith. The classic synthwave glow.
- **A warm, raking sun.** Low on the horizon, **gold-orange**, long warm light grazing the tower faces and the
  desert — not a cool overhead key.
- **The distance fades to warm atmosphere**, not cold black/blue. Far mountains and far towers wash into a
  **dusty mauve/peach haze**, so the basin reads for kilometres and feels *warm*, not murky-night.
- **Desert + hills + coastline are warm and legible** — rosy tan sand, dusty-umber hills, mauve-grey dusk rock.
  No muddy olive-night.
- **The ocean catches the sky** — a deep dusk-teal/violet body with a **warm gold/pink rim** where it meets the
  grazing light and the horizon. It should look like *water at sunset*, not a flat cold sheet.
- **The neon still pops.** Dark tower bodies (unchanged shader) carry their sparse coloured window grid; against
  the warmer-but-still-dim sky the cyan/magenta/white windows still **bloom**. Dusk should *frame* the neon, not
  wash it out. If anything, the warm sky makes the cool neon read *more* like neon (complementary contrast).
- **It's stylised, not photoreal.** Lean into saturation and the symmetric horizon glow — this is *synthwave*.

There is no single "correct" set of numbers — this is a **GPU taste loop**. §3 gives **starting values** that
get you into the right neighbourhood; the DoD (§1) is about how it **reads**, judged in `./run.sh play`.

---

## 1. Definition of Done

1. `./run.sh build` → **0 errors, 0 warnings**; `./run.sh build && ./run.sh check` → boots headless, **exits 0**
   (proves it still compiles + boots — it **cannot** show the look; the aesthetic DoD is the `play` run below).
2. `./run.sh play` (**GPU — eyeball these; headless renders none of it**; the repo's `memory` note says world
   visuals must be verified in `play`):
   - **Warm golden-purple dusk reads at a glance** — sunset horizon band (gold→magenta→violet), warm raking
     sun, deep-but-visible violet zenith. It looks like *dusk*, not night.
   - **The distance is warm + legible** — the desert→hills→mountain ring and far towers fade into a **warm**
     haze (mauve/peach), not cold blue or black. You can read the basin for kilometres.
   - **Desert, hills, beach, and the ocean are warm + legible** — sand/tan/umber land; the sea reads as dusk
     water with a warm rim catching the horizon. Nothing muddy or cold.
   - **Neon still glows** — the city's coloured windows still **bloom** against the warmer sky; the skyline
     still reads as neon cyberpunk. (If the brighter sky washes neon out, you tuned the sky/glow wrong — §4.2.)
   - **No distracting central hotspot** — the sky reads as a *gradient*, not a blown-out central bloom (§4.3).
   - **Identical every launch** — quit + re-run → same dusk, same world (no RNG; nothing here is time-seeded).
   - **FPS holds** at a cruise (you added no geometry; watch only if you enable volumetric fog or shadows — §4.4).

---

## 2. Preconditions

- Branch **`dev6`**, Tasks 1–5 merged. Confirm green before you start:
  `./run.sh build && ./run.sh check` → exits 0 and prints
  `:: WorldLoader: 315 objects, 1024 terrain tiles (85 populated), 250 m.` (Task 5's loader line).
- `res://world/world_8km.res` is present and committed — **read-only here; you never touch it** (this task does
  not load or sample world data at all).
- Read `docs/GAME_SPEC.md` **§7.6** (the one-paragraph art direction) and skim `CLAUDE.md` (the `[Export]`/`[fx]`
  conventions, framerate-independent rule, "GPU visuals can't be confirmed headless").

---

## 3. Work items

Six pieces, all colour/value retunes. Suggested order: **sky → sun → environment fog/tonemap/glow → water →
terrain palette → (optional) winding cleanup**, eyeballing in `play` as you go. The numbers below are
**starting points to get you into the neighbourhood** — expect to nudge them live.

> **Fast iteration tip.** Shader edits (`sky.gdshader`, `water.gdshader`) hot-reload — Godot recompiles the
> shader when the file changes on disk, so you can leave `play` running and just save the `.gdshader` to see
> colour changes. C# edits (`Game.cs`, `TerrainBuilder.cs`) need `./run.sh build` then a fresh `play`. Tune the
> `[fx]` numeric knobs by editing your **local** `settings/settings.cfg` live (then leave it **unstaged** — §3.6).

### 3.1 `shaders/sky.gdshader` — neon-night → golden-purple sunset (defaults only)

The sky colours live as **shader uniform defaults** (Game.cs only drives `sky_energy`/`cloud_coverage`/
`cloud_speed`). Retune the default colours in place — keep the structure, the noise helpers, and the uniform
names (so the Game.cs uniform sets still bind). The gradient is `mix(horizon_color, zenith_color, pow(up,0.45))`
with an additive `glow_color * exp(-horizon_falloff*|up|)` band and warm-lit cloud undersides near the horizon.

Starting values (L12–19) — *tune live*:

| Uniform | Current (neon-night) | Suggested dusk start | Intent |
|---|---|---|---|
| `zenith_color` | `(0.030,0.040,0.110)` | `(0.05,0.04,0.14)` | deep **indigo/violet** overhead, still visible (not black) |
| `horizon_color` | `(0.20,0.10,0.34)` | `(0.55,0.22,0.32)` | warm **rose** base at the horizon |
| `glow_color` | `(0.55,0.22,0.80)` | `(1.0,0.50,0.28)` | hot **gold-orange** sunset band sitting on the horizon |
| `horizon_falloff` | `3.5` | `2.2` | **lower = the glow spreads higher** up the dome (a broad sunset, not a thin line) |
| `cloud_color` | `(0.22,0.20,0.34)` | `(0.28,0.22,0.36)` | cool violet cloud body |
| `cloud_lit_color` | `(0.60,0.32,0.72)` | `(1.0,0.62,0.45)` | cloud **undersides catch the warm gold** glow |

The band is currently **uniform around the whole horizon** (no azimuth term) — that's *on-genre* for synthwave
(the symmetric glow) and is fine to keep. **Optional polish:** if you want a real "sun is *there*" hotspot,
add a gentle azimuthal term that brightens `glow_color` toward the sun's compass direction (pass the sun's
horizontal direction in as a `uniform vec3 sun_dir` set from `Game.cs`). **Default to NOT doing this** — the
uniform band reads great and avoids new plumbing; only add it if the flat band bothers you on GPU.

### 3.2 `scripts/Game.cs` `EnsureEnvironment` — warm the sun (raking sunset key)

Change the three `Sun` lines (~L239–242). **Cool+steep → warm+low.**

```csharp
// A low, warm sunset key — long gold light raking the towers + desert. The city still lights itself (emissive);
// this just models the geometry and warms the lit faces. Keep ShadowEnabled OFF (default) for FPS over 8 km.
sun.Rotation = new Vector3(Mathf.DegToRad(-18.0f), Mathf.DegToRad(35.0f), 0.0f);  // low on the horizon (was -55°)
sun.LightColor = new Color(1.0f, 0.62f, 0.38f);   // warm gold-orange (was cool 0.55,0.65,1.0)
sun.LightEnergy = 0.6f;                            // a touch stronger warm key (was 0.35) — tune vs neon wash-out
```

- **Keep shadows off** (don't set `ShadowEnabled`) — a shadow-casting directional over 1024 terrain tiles is an
  FPS risk and the mood comes from colour/fog/glow, not shadows. If you experiment with shadows, gate it and
  watch FPS hard (§4.4); default is **off**.
- The azimuth (`Rotation.y`) is free — pick whatever lights the city's visible faces best from the spawn vantage.
  If you added a `sun_dir` sky term (§3.1 optional), keep the two consistent.

### 3.3 `scripts/Game.cs` `BuildEnvironment` — warm the fog, keep neon blooming

The **distance fog colour** is the single biggest "warm & legible distance" lever. Retune these constants
(leave the `[fx]`-driven numeric reads alone in code — those are tuned via settings.cfg, §3.6):

```csharp
// ~L294 — the distance fades to WARM dusk atmosphere, not cold blue.
env.FogLightColor = new Color(0.42f, 0.26f, 0.30f);   // dusty mauve/peach haze (was cool 0.10,0.12,0.22)

// ~L305–306 — if volumetric fog is enabled ([fx] volumetric_fog, currently off), warm it too.
env.VolumetricFogAlbedo = new Color(0.24f, 0.16f, 0.22f);     // warm haze (was 0.07,0.08,0.17)
env.VolumetricFogEmission = new Color(0.16f, 0.07f, 0.10f);   // faint warm self-glow (was 0.05,0.02,0.10)
```

- **`AmbientLightEnergy` (~L269, 0.25):** ambient is **sky-sourced**, so it warms automatically as the sky
  warms. Keep it modest (≈0.25–0.35). **Raising it too far flattens the dark towers and kills neon contrast** —
  if the city looks washed-out, this is a prime suspect.
- **`TonemapWhite` (~L276, 6.0):** keep it high — it's what keeps bright emissives **coloured** (so neon blooms
  in colour instead of clipping to white). Don't drop it much.
- **Glow (~L280–288):** the warmer/brighter sky must **not** start blooming. Keep `GlowHdrThreshold` ≥ ~0.95
  (so only the bright emissive windows cross it); tune `GlowBloom`/`GlowIntensity`/`GlowHdrScale` so neon
  windows still halo against dusk. If the *sky* blooms, raise the threshold (§4.2/§4.3).
- **Post-adjust (~L326–327):** synthwave likes punch — `AdjustmentContrast` ~1.08–1.15 and a healthy
  `[fx] saturation` (~1.2–1.35, §3.6) help the warm/cool contrast sing. Tune to taste.
- **SSR (~L317–321):** leave the SSR *settings* as-is; it'll now reflect the warm sky in the water/terrain.

### 3.4 `shaders/water.gdshader` — dusk sea that catches the sunset (defaults only)

Retune the three colour defaults (L8–10); keep the ripple/fresnel structure and uniform names (Game.cs binds
`ripple_speed`/`water_energy`). The `fresnel_color` rim is what makes the sea **catch the horizon glow** — make
it warm to match the sunset.

| Uniform | Current | Suggested dusk start | Intent |
|---|---|---|---|
| `deep_color` | `(0.02,0.06,0.10)` | `(0.04,0.05,0.11)` | deep dusk teal-**violet** body |
| `shallow_color` | `(0.05,0.16,0.20)` | `(0.14,0.12,0.20)` | shallows lit toward **mauve** (less cold teal) |
| `fresnel_color` | `(0.50,0.28,0.55)` | `(1.0,0.50,0.42)` | warm **gold-pink** rim catching the sunset at grazing angles |

Keep it **opaque** (no `blend_mix`, no depth-fade) — shoreline foam / depth transparency stay deferred. If you
make `fresnel_power` lower the rim spreads wider; tune so the sea glows warm toward the horizon without going
neon-magenta everywhere.

### 3.5 `scripts/world/TerrainBuilder.cs` — warm the biome palette (+ optional winding cleanup)

**(a) Palette (L11–19) — warm + legible.** Push every land biome toward dusk-warm; the City asphalt off cold;
the seabed barely matters (mostly under water). *Tune live against the warm sky/fog.*

| Index / biome | Current | Suggested dusk start | Intent |
|---|---|---|---|
| `Ocean` (seabed) | `(0.03,0.08,0.11)` | `(0.04,0.06,0.10)` | mostly hidden under the water plane — low priority |
| `Beach` | `(0.60,0.52,0.37)` | `(0.64,0.50,0.40)` | warm **rosy sand** |
| `City` | `(0.09,0.09,0.12)` | `(0.11,0.08,0.12)` | dark asphalt, nudged **off cold** (warm/violet, not blue-grey) |
| `Desert` | `(0.52,0.39,0.25)` | `(0.58,0.38,0.28)` | sunset-lit **rosy tan** |
| `Hills` | `(0.33,0.33,0.20)` | `(0.36,0.28,0.21)` | dusty **umber/rose** (off the olive-night) |
| `Mountains` | `(0.30,0.29,0.31)` | `(0.34,0.28,0.33)` | **mauve-grey** dusk rock |

The terrain `Material()` stays matte (`Roughness 0.92`, `SpecularMode.Disabled`). **The wet-street reflective
City-ground look is OPTIONAL stretch** (§7) — if you have GPU time after the core dusk reads, you *may* give the
City biome a wetter feel (a second, lower-roughness material applied only to city tiles, so SSR/neon reflects in
the streets). **This is secondary** — do not let it block or destabilise the core retune; if it's fiddly, defer
it and note it.

**(b) Optional winding-flip cleanup (secondary).** The terrain mesh is currently **double-sided**
(`CullMode = CullModeEnum.Disabled`, L28) — a safe fix Task 5 used to dodge a back-face-culling bug (the hand-
wound heightmap top face was culled, giving torn fragments). As cleanup you *may* restore single-sided culling
(halves the terrain triangle count): set `CullMode = CullModeEnum.Back` and **reverse each triangle's winding**
in `BuildTerrainMesh` (L64–65) by swapping the last two indices of each triangle:

```csharp
// from:
st.AddIndex(a); st.AddIndex(c); st.AddIndex(b);
st.AddIndex(b); st.AddIndex(c); st.AddIndex(d);
// to (reversed winding):
st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
st.AddIndex(b); st.AddIndex(d); st.AddIndex(c);
```

Then **GPU-verify in `play`**: terrain must be **solid from above** (you fly over it and see ground). If it goes
**see-through from above** (you see the underside / through the hills), you flipped the wrong way — Godot's
front face is the *opposite* winding. Since **double-sided already works** and the tri count is trivial
(≈256 tris/tile), **treat this as optional polish**: if the flip misbehaves, just **revert to `CullMode.Disabled`
and move on** — do not rabbit-hole. Note in your report whether you landed single- or double-sided.

### 3.6 `settings/settings.cfg` `[fx]` — tune live, REPORT, do **NOT** commit

The dusk **colours** live in code/shaders (above) and take effect on their own. But several **numeric
intensities** that shape the look are `[fx]` keys that **already have live values in `settings.cfg`**, and the
settings value **overrides** the code fallback at runtime. So:

- **Tune these live** in your local `settings/settings.cfg` `[fx]` to dial the dusk look:
  `fog_density`, `fog_sky_affect`, `exposure`, `saturation`, `glow_intensity`, `bloom`, `sky_energy`,
  `cloud_coverage`, `cloud_speed`, `water_energy`, `water_ripple_speed` (and `volumetric_fog`/`ssr` if you
  toggle them). For dusk, expect e.g. a **thinner, warmer-reading fog** (lower `fog_density` so the warm
  distance reads further), and a **healthy `saturation`**. Find what looks right.
- **Do NOT stage or commit `settings/settings.cfg`.** The user edits this file live; session A owns it and will
  apply your recommended values on review (re-reading it live, preserving everything else — exactly how Task 5's
  `[fx]` water keys were curated). Leave your local edits **unstaged** (or revert them) before you commit.
- **In your report (§6), list the exact `[fx]` value you landed on for every key you changed**, so session A can
  transcribe them. (Optional: also update the matching **code fallback** in `Game.cs`/`BuildSky` to the same
  value so the committed default reflects intent — harmless but low-value since settings.cfg overrides it. Skip
  unless trivial.)
- **Don't add a pile of new `[fx]` keys.** Keep colours in the shader/code defaults. If you genuinely want one
  new knob exposed (e.g. a `sun_energy`), wire the `CfgFloat("fx", ...)` read with a fallback **and report the
  suggested key** for session A to add — but default to **not** expanding settings.cfg.

---

## 4. Correctness / taste points (get these right)

1. **The distance must read WARM.** `FogLightColor` (§3.3) and `[fx] fog_density` (§3.6) decide whether the
   far basin fades to warm dusk haze or to cold murk. This is the difference between "California sunset" and
   "blue night with warm stickers." Tune the fog colour first, then the density, judging the **far** mountains
   and **far** towers.
2. **Neon must survive the warm-up.** The risk of dusk is washing the city out. Levers, in order: keep the
   **towers dark** (don't touch `building.gdshader`); keep **ambient modest** (§3.3); keep **`TonemapWhite`
   high** and **`GlowHdrThreshold` ≈0.95+** so only the bright windows bloom; lean on **saturation/contrast**
   for the cool-neon-vs-warm-sky pop. If neon looks dull, you almost certainly over-brightened the sky/ambient.
3. **Tame any central bloom.** During the Task-5 playtest a bright central bloom was noted. The *old* corridor
   "bright cyan square at screen centre" was a corridor-vanishing-point artifact and **no longer applies** (no
   corridor). In the open world, a central hotspot most likely comes from the **horizon glow band × additive
   bloom** (a hot `glow_color`/low `horizon_falloff`/high `sky_energy` blooming into a blob) or a **water/SSR
   specular hotspot**. As part of this pass, make the sky read as a **gradient, not a hotspot**: tune
   `glow_color` brightness, `horizon_falloff`, `sky_energy`, and the env `GlowHdrThreshold`. (If you decide a
   soft sun-glow looks *intentional and pretty*, keeping it is a fine call — just say so.)
4. **FPS: you added no geometry, so it should hold.** The only ways to regress it are **enabling volumetric fog**
   (heavier froxel pass) or **enabling sun shadows** — both default OFF; if you turn either on, watch the frame
   time and gate it behind `[fx]`. Note anything you toggled.
5. **Determinism is free here** — nothing in this task is time-seeded or RNG-driven; same look every launch by
   construction. (The sky/water shaders animate with `TIME`, which is fine — that's drift, not nondeterminism.)
6. **Don't break the bindings.** Keep every `sky.gdshader`/`water.gdshader` **uniform name** that `Game.cs`
   sets (`sky_energy`, `cloud_coverage`, `cloud_speed`, `ripple_speed`, `water_energy`) — rename one and the
   `SetShaderParameter` silently no-ops. You're changing **default values**, not the interface.

---

## 5. Verify

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → exits 0; still prints the Task-5 loader line
   (`:: WorldLoader: 315 objects, 1024 terrain tiles (85 populated), 250 m.`). This only proves it **compiles +
   boots** — it renders nothing; the real test is `play`.
3. `./run.sh play` (**GPU — eyeball; this is the actual DoD**), against §1. The car free-flies now (no corridor
   clamp) and the run **spawns above the city looking at it**, so just fly to vantages — climb for a **skyline
   silhouette against the sunset**, cross the **bay** (judge the water rim + the islands), fly out to the
   **desert/hills/mountain ring** (judge warm legibility + the warm distance haze), and skim low. Tune live,
   then **lock the values** into the four files.
   - Confirm each §1 bullet: dusk reads, distance warm, land/sea/desert warm + legible, **neon still blooms**,
     no central hotspot, identical relaunch, FPS holds.
4. **Screenshots (optional but helpful for review).** You can capture a GPU frame headlessly-ish for the report:
   run `godot-mono --path . &` (note the PID), `sleep ~22`, `spectacle -bnf -o /tmp/dusk.png`, then
   `kill -9 $PID; pkill -9 -x godot-mono` — **as one background Bash command** (foreground `sleep` is blocked).
   settings.cfg boots fullscreen 4K so `-f` grabs the frame. (Attach or describe the captures in §6.)

---

## 6. Report back to session A (for review)

Include:
- `git status` + `git diff --stat`, and the per-file edits: **`Game.cs`** (sun + env + sky builders),
  **`shaders/sky.gdshader`**, **`shaders/water.gdshader`**, **`scripts/world/TerrainBuilder.cs`** (palette +
  material + whether you did the winding-flip).
- `./run.sh build` + `./run.sh check` output (still 0/0; still prints the loader line).
- A description of the **`play` GPU playtest** against §1 — you can't render headless, so **describe what you
  saw** (and attach/describe any screenshots from §5.4): the sunset sky, the warm distance, the warm desert/
  hills/sea, neon still blooming, the central-bloom resolution, identical relaunch, rough FPS.
- **The recommended `[fx]` values** — list every `settings.cfg [fx]` key you tuned and the value you landed on
  (session A transcribes these; you did **not** commit settings.cfg).
- **Confirmation** that you did **not** touch `building.gdshader`, world data (`world_8km.res`/`WorldGenerator`
  — **no re-bake**), the loader/collider/render plumbing, the flying car / camera / HUD / damage / screen-FX, or
  `Traffic.cs`.
- **Any deviations** and why; **deferred items** still out (wet-street city ground if you skipped it, foam/depth
  water, traffic, glTF), and whether terrain ended **single- or double-sided** (§3.5b).
- **Commit** once build + check + playtest are green. Stage **by path** (the four files only); **never
  `git add -A`**; **do not** stage `settings/settings.cfg` (session A applies the `[fx]` values) and **do not**
  tick `docs/GAME_SPEC.md` §9 (session A ticks it). Suggested message:
  `Task 6: synthwave dusk pass — warm sun/sky/fog + dusk water + warm biome palette`.
  End the commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## 7. Out of scope (do not do here)
- **World data / re-bake** — this task never loads, samples, or writes `world_8km.res` / `WorldGenerator` /
  `WorldData`. The geometry, heightmap, biomes, and object placement are **fixed**.
- **`building.gdshader`** (the neon identity) — **confirm it reads; do not edit it.** If neon looks wrong, the
  fix is environment-side (glow threshold / ambient / sky energy), never the building shader.
- **Gameplay & feel** — Ship, CameraRig, Hud, DamageComponent, the `[flight]`/`[damage]` tunables, input map.
  (Flight feel is settled; don't retune torques/grip here.)
- **Screen-space FX** — `ScreenFX`, `post.gdshader`, `speed_lines.gdshader`, and the `[fx]` keys
  `speed_lines`/`chromatic_aberration`/`near_miss_flash`/`time_dilation`. The dusk mood is environmental.
- **Ambient traffic** (`Traffic.cs` + `hazard.gdshader`/`beacon.gdshader`) → **Task 7.** **glTF models** → **Task 8.**
- **Water as gameplay**, **shoreline foam**, **depth-fade transparency**, **planar/SSR refraction** → later.
- **The wet-street reflective City ground** is an **optional stretch** within this task (§3.5a) — ship it only
  if the core dusk reads first and it's quick; otherwise defer and note it.
- **Sun shadows** and **volumetric fog** — both default OFF for FPS; enable only if you gate them behind `[fx]`
  and confirm the frame time holds (§4.4).
- **Editing `settings/settings.cfg` (commit), `CLAUDE.md`, or `docs/` prose**, or **ticking §9** — session A owns
  all of these on review.
