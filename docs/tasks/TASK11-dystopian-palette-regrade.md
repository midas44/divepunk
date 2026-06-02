# Task 11 — Dystopian palette re-grade (+ restore a cold fog haze) (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** — self-contained; you do not need the planning chat.
> When you finish, you **verify** (`build` + headless `check` + a hands-on **GPU** `play` — this is a *look* task, so
> `play` is the real test), **commit by path**, and report back to session A for review.

> **Status / priority.** **§9 Task 11**, pulled AHEAD of Task 10 (collision) at the user's request — the warm look is
> bothering them now, and the palette is **visual-only** so it has no dependency on Task 10. Tasks 1–9 are merged and
> green: the car flies; the world is baked + rescaled to **~40×40 km** (`world_main.res`, Task 9) and renders as
> `MultiMesh` batches + terrain/ocean; traffic roams; the glTF seam is proven. **Task 10 (solid ground) is NOT done
> yet** — the ground still tunnels on a fast descent, so during your playtest **fly/observe from the air; don't try to
> land.** Don't touch collision here.

> **What this task is.** Re-grade the scene from the warm **synthwave dusk** (Task 6) to a **darker, colder,
> dystopian** palette — subtle dark **cyan / violet / gray**, *not* warm California sunset — and **bring back a visible
> (now COLD) fog haze**, which the Task-9 rescale thinned to almost nothing. **Neon must still bloom.** Direct user
> feedback driving this:
> - *"red gamma is really bad, change to darker dystopian ASAP"* → kill the warm magenta/gold/rose; go cold + darker.
> - *"no fog at all, please add some"* → restore a reads-at-a-few-km haze, but **cold** (see §3.0).

> **What this task is NOT.** No new render features — it's a **re-grade of existing surfaces + fog tuning**, not new
> passes. No collision/physics (Task 10). No geometry/bake changes (Task 9 owns the world; do **not** re-bake or touch
> `WorldGenerator`/`world_main.res`). **Never edit `building.gdshader`** (the neon identity — confirm only). No new
> gameplay.

---

## 0. Context (read first)

Skim **`docs/GAME_SPEC.md` §7.6** (art direction — *currently* "synthwave dusk (warm)"; this task **changes** it →
session A updates the docs on review, see §6) and **`CLAUDE.md`** (the `[Export]`/`settings.cfg` conventions; "GPU
visuals can't be confirmed headless"). This re-grades **Task 6**, which made everything a warm golden-purple dusk; the
user now wants the **opposite temperature** — cold Blade-Runner haze, dark and desaturated, neon still glowing.

> **The `[fx]` discipline (same as Task 6 — important).** The **colours** live in **code / shader defaults** — retune
> those and **commit them by path**. The **numeric intensities** (`fog_density`, `sky_energy`, `exposure`,
> `saturation`, `glow_intensity`, `bloom`, `volumetric_*`, `water_*`) are **`settings.cfg [fx]` keys** — tune them
> **live in your local settings.cfg and REPORT the values; do NOT commit `settings.cfg`** (the user edits it live;
> session A applies on review). Shaders **hot-reload** (leave `play` running, save the `.gdshader`); C#/`Game.cs`
> changes need a rebuild.

### 0.1 The surfaces you touch

| File | What changes |
|---|---|
| `scripts/Game.cs` | `EnsureEnvironment` (sun) + `BuildEnvironment` (fog, ambient, volumetric, tonemap/glow/contrast) — **colour defaults** |
| `shaders/sky.gdshader` | uniform **colour defaults** (zenith/horizon/glow/cloud) |
| `shaders/water.gdshader` | uniform **colour defaults** (deep/shallow/fresnel) |
| `scripts/world/TerrainBuilder.cs` | the `Palette[]` biome **colours** |
| `settings/settings.cfg [fx]` | `fog_density` (raise — see §3.0), `saturation`/`exposure`/`sky_energy`/`glow_*` — **tune live + REPORT, do NOT commit** |

**Not yours:** `building.gdshader` (confirm only — fix any neon problem environment-side), `WorldGenerator`/the bake/
`world_main.res` (no re-bake), `Ship.cs`/colliders/`Traffic.cs`/`MeshRegistry`, `CameraRig`/`Hud`, `ScreenFX`/
`post.gdshader`/`RainFX` and their `[fx]` keys.

---

## 1. Definition of Done

`./run.sh build && ./run.sh check` → **0/0**, exits 0. `./run.sh play` (the real test):
- The scene reads **darker and colder** — a **dystopian cyan/violet/gray** mood, **not** the warm reddish dusk. The
  "red gamma" is gone.
- A **cold fog haze is clearly visible** again (distance fades into a cold murk a few km out — not the current
  fogless look, and not warm).
- **Neon still blooms** — city windows pop against the cold haze; the wet-street reflections still catch neon.
- Land/sea are **cool but legible** (dark ≠ black; nothing muddy). **Identical every launch.**

---

## 2. Preconditions

- Branch **`dev6`**. `./run.sh build && ./run.sh check` → exits 0, loader line `:: WorldLoader: 3423 objects, 1024
  terrain tiles (101 populated), 1250 m.` (the Task-9 40 km world).
- Confirm `building.gdshader` is **untouched** at the end (you only read it).

---

## 3. Work items

> All line numbers below are **current (post-Task-9)** — verified against the code. All colours are **start points for
> a taste loop** — tune live in `play`.

### 3.0 — FOG: restore a visible COLD haze *(headline — the user's #1 note)*

Right now there's effectively **no fog**: Task 9 thinned `[fx] fog_density` ÷5 (→ `0.00004`) to see across the 40 km
basin, which killed the near/mid haze, **and** the fog colour is still warm. Two fixes, together:

1. **Cold fog colour** — `Game.cs BuildEnvironment` **L352** `env.FogLightColor`:
   `(0.42, 0.26, 0.30)` dusty mauve/peach → **`(0.13, 0.15, 0.20)` cold slate-violet** (this is the single biggest
   "distance reads cold" lever — desaturated + dark).
2. **Density that actually reads** — `[fx] fog_density` (drives `env.FogDensity`, **L353**): currently `0.00004`
   (≈invisible). Raise it so a **cold murk** returns a few km out — start **`~0.00012`–`0.0002`** and **tune live**
   (lower = see further; higher = murkier/closer dystopia). This is an `[fx]` key → **REPORT, don't commit.**
3. **(Optional) Volumetric fog for a richer haze** — currently OFF (`[fx] volumetric_fog=false`, **L362**). If you want
   true depth haze, enable it and cool its tint: `VolumetricFogAlbedo` **L364** `(0.24,0.16,0.22)`→`(0.14,0.16,0.22)`,
   `VolumetricFogEmission` **L365** `(0.16,0.07,0.10)`→`(0.06,0.09,0.14)`. It's **heavier** — gate behind `[fx]`,
   confirm frame time, and keep `VolumetricFogSkyAffect` low so the dome doesn't go black. Optional; the exponential
   fog (1+2) alone can carry it.

### 3.1 — Sun — `Game.cs EnsureEnvironment` (L297–299)

| field (L) | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `LightColor` (298) | `(1.0, 0.62, 0.38)` gold-orange | `(0.55, 0.62, 0.80)` cool steel-violet | a cold key, not a sunset |
| `LightEnergy` (299) | `0.6` | `~0.45` | dimmer (darker mood) — tune vs neon wash-out |
| `Rotation` (297) | `(-18°, 35°, 0)` low rake | keep low, or raise toward `-30°` | cold overcast can sit a touch higher |

### 3.2 — Environment — `Game.cs BuildEnvironment`

| field (L) | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `AmbientLightEnergy` (327) | `0.25` | `~0.18–0.22` | darker; keep ≥~0.15 so towers don't go pure black |
| `TonemapWhite` (334) | `6.0` | **keep `6.0`** | keeps bright neon **coloured** (don't drop — stops neon clipping to white) |
| `GlowHdrThreshold` (343) | `0.95` | **keep ≥0.95** | only the bright windows bloom, not the (now darker) sky |
| `AdjustmentContrast` (385) | `1.08` | `~1.1` | a touch more bite for the cold look |

*(Fog fields L351–353 + volumetric L362–367 are covered in §3.0.)*

### 3.3 — Sky — `shaders/sky.gdshader` defaults (L12–19)

| uniform (L) | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `zenith_color` (12) | `(0.08, 0.05, 0.22)` indigo | `(0.04, 0.05, 0.14)` darker blue-violet | deep, cold, still visible |
| `horizon_color` (13) | `(0.50, 0.17, 0.42)` magenta-rose | `(0.16, 0.16, 0.26)` desaturated violet-gray | **kill the rose** — the "red gamma" culprit |
| `glow_color` (14) | `(0.85, 0.42, 0.28)` gold-orange | `(0.16, 0.26, 0.34)` dim cold cyan-teal | the horizon glow goes **cold**, dim |
| `horizon_falloff` (15) | `2.8` | `~3.4` | tighter/dimmer band (less blooming) |
| `cloud_lit_color` (19) | `(1.0, 0.62, 0.45)` warm | `(0.34, 0.44, 0.58)` cold | cloud undersides catch a **cold** glow |
| `sky_energy` (16, via `[fx]`) | `1.5` def / `0.75` cfg | `~0.5–0.7` (cfg) | darker sky overall (REPORT) |

### 3.4 — Water — `shaders/water.gdshader` defaults (L8–10)

| uniform (L) | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| `deep_color` (8) | `(0.04, 0.05, 0.11)` | `(0.03, 0.05, 0.09)` | deep cold body (barely change) |
| `shallow_color` (9) | `(0.14, 0.12, 0.20)` | `(0.08, 0.12, 0.17)` | cold teal-violet, darker |
| `fresnel_color` (10) | `(1.0, 0.50, 0.42)` gold-pink | `(0.35, 0.55, 0.70)` cold cyan rim | the sea rim catches a **cold** sky, not a sunset |

### 3.5 — Terrain biomes — `TerrainBuilder.Palette` (L13–18) — **desaturate + cool, keep legible (dark ≠ muddy black)**

| biome (L) | current (warm) | suggested (cold) | intent |
|---|---|---|---|
| Ocean (13) | `(0.04, 0.06, 0.10)` | `(0.03, 0.05, 0.09)` | mostly hidden — low priority |
| Beach (14) | `(0.64, 0.50, 0.40)` rosy | `(0.40, 0.41, 0.45)` cold gray-sand | drained of warmth |
| City (15) | `(0.11, 0.08, 0.12)` | `(0.09, 0.09, 0.12)` | dark cold asphalt |
| Desert (16) | `(0.58, 0.38, 0.28)` rosy tan | `(0.36, 0.35, 0.36)` cold gray-tan | dystopian dead ground |
| Hills (17) | `(0.36, 0.28, 0.21)` umber | `(0.24, 0.25, 0.29)` cold gray-violet | |
| Mountains (18) | `(0.34, 0.28, 0.33)` mauve-gray | `(0.26, 0.28, 0.34)` cold blue-gray | |

### 3.6 — `[fx]` to tune live + REPORT (don't commit)

`fog_density` (§3.0 — raise from `0.00004` toward `~0.00012–0.0002`), `saturation` (currently `1.2` — drop toward
`~1.05–1.15`; less candy, more grim), `exposure` (currently `1.0` — drop slightly for darker), `sky_energy`
(`~0.5–0.7`), `glow_intensity`/`bloom` (keep neon blooming — don't kill them). Keep the city's **wet-street SSR**
(`TerrainBuilder.WetMaterial` + `[fx] ssr=true`) — cold neon reflecting in wet streets is peak dystopia.

---

## 4. Hard requirements / taste points

1. **Neon must survive.** Keep towers dark (never touch `building.gdshader`), ambient modest, `TonemapWhite` high
   (6.0), `GlowHdrThreshold ≥0.95`. A darker scene *helps* neon pop — the risk here is the **opposite** of Task 6:
   going so dark/desaturated it reads as **murky black mud**. Keep it **cold but legible**; lean on the
   **cold-neon-vs-cold-haze** contrast, not on crushing everything to black.
2. **Cold ≠ black.** The failure mode is muddy darkness. Keep land/sea/sky legible.
3. **Don't break shader bindings.** Keep every uniform name `Game.cs` sets (`sky_energy`, `cloud_coverage`,
   `cloud_speed`, `ripple_speed`, `water_energy`, etc.). You change **default values**, not the interface.
4. **No re-bake / no geometry.** `world_main.res`, `WorldGenerator`, building/terrain *placement* are untouched — this
   is colour + fog only.
5. **`settings.cfg` is the user's live file** — tune `[fx]` locally, **REPORT**, **never commit it**, **never
   `git add -A`**, **never revert the user's values**.

---

## 5. Verify

- `./run.sh build` → **0/0**; `./run.sh build && ./run.sh check` → exits 0 (headless renders **none** of the look).
- `./run.sh play` (the real test): cold dystopian dusk reads, **cold fog haze visible** again, land/sea cold-but-
  legible, **neon blooms**, identical relaunch. Fly above the city (Task 10 ground isn't solid yet — don't land).
- **Screenshots (do this — session A reviews by eye):** as **one background** Bash command
  (`godot-mono --path . >/tmp/run.log 2>&1 &` note PID; `sleep ~28`; `spectacle -bnf -o /tmp/palette.png`; `kill -9
  $PID; pkill -9 -x godot-mono`). Boots fullscreen 4K so `-f` grabs the frame. Capture a city overview + a horizon/fog
  shot so the cold haze is visible.

---

## 6. Report back to session A (for review)

- `git status` + `git diff --stat`; the per-file colour edits (sun/env/sky/water/terrain).
- `./run.sh build` + `check` output (0/0).
- **The `[fx]` values you landed on** (you did **not** commit `settings.cfg`): **`fog_density`** (the value that made
  the haze read), `saturation`, `exposure`, `sky_energy`, `glow_*` — every key + value for session A to apply.
- A description of the **GPU `play`** look against §1 + the screenshots (the cold mood, the restored cold fog, neon
  still blooming).
- **Two docs items for session A** (session A owns docs — flag, don't edit): **(1)** this shifts the art direction —
  `GAME_SPEC.md §7.6` says "synthwave dusk (warm)"; update it to the **cold dystopian** direction (+ note in §11 Open
  Decisions); **(2)** tick `§9 Task 11`.
- **Confirm** you did **not** touch `building.gdshader`, `WorldGenerator`/the bake/`world_main.res`, collision/
  `Ship.cs`/`Traffic.cs`, or `ScreenFX`/`post`/`RainFX`.

**Commit (by path):** `scripts/Game.cs`, `shaders/sky.gdshader`, `shaders/water.gdshader`,
`scripts/world/TerrainBuilder.cs`. **Do NOT commit `settings.cfg`** (report the `[fx]` values). **Do NOT tick `§9`.**
End the commit body with:
`Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

Message e.g.: `Palette: re-grade warm dusk -> cold dystopian (cyan/violet/gray) + restore cold fog haze; neon preserved`.

---

## 7. Out of scope (do not do here)
- **Collision / solid ground / hover** → **Task 10** (the ground still tunnels — don't touch it; just don't land
  during the playtest).
- **`building.gdshader`** — never edit (confirm neon still reads; fix any neon issue environment-side).
- **Re-bake / geometry / `WorldGenerator` / `world_main.res`** — colour + fog only.
- **Screen-space FX** (`ScreenFX`/`post.gdshader`/`speed_lines`/`RainFX`) and their `[fx]` keys.
- **Sun shadows** — stay off for FPS unless `[fx]`-gated with frame time confirmed.
- **Editing `docs/`/`CLAUDE.md` or ticking `§9`** — session A owns docs on review (you only **report** the art-direction shift).
