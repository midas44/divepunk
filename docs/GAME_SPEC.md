# DIVEPUNK — Project Spec & MVP Plan

> **Working title:** DIVEPUNK *(chosen — verify store, trademark, and domain availability before commercial launch)*
> **Genre:** High-speed arcade flyer / score-attack, set in a procedurally generated cyberpunk megacity
> **Status:** Pre-production. This document is the north star for the build. Implement it milestone by milestone (see [Roadmap](#9-development-roadmap)).
> **How to use this file:** Keep it in `docs/GAME_SPEC.md` and reference it from a lean `CLAUDE.md` (see [Working with Claude Code](#10-working-with-claude-code)).

---

## 1. Vision & Differentiation

You pilot a flying car at exhilarating speed through the canyons of an endless, dynamically generated neon megacity. The fantasy is **flow + speed + danger**: threading gaps between skyscrapers, skimming traffic, and chaining near-misses while the city blurs past in a wash of neon and fog.

**The hook / what makes it different.** The closest existing game is *Cloudpunk* — beautiful, but a slow, narrative, atmospheric drive. DIVEPUNK deliberately occupies the opposite end: **fast, arcadey, run-based, score-chasing.** Think *Race the Sun* / *Distance* / *WipeOut* energy with a *Cloudpunk* skin and an "easy to play, hard to master" skill ceiling. That gap (high-speed neon flight, score-attack, mobile-friendly) appears genuinely under-served.

**Design north star:** *Every run should make the player think "one more, I can do better."*

---

## 2. Platforms & Constraints

| | Detail |
|---|---|
| **MVP platforms** | Linux (desktop) + Android |
| **Primary dev platform** | Linux desktop (fastest iteration) |
| **Stretch platforms** | Windows, macOS, iOS, Web (all low-effort from this stack later) |
| **Orientation** | Landscape |
| **Target framerate** | 60 FPS desktop; 60 FPS on mid-range Android (fall back to a capped 30 only if forced) |

> ⚠️ **Mobile reality check:** the lavish desktop look and the Android look are *two different effect budgets*. Build the corridor and gameplay to look great with the desktop renderer, then dial effects down for the mobile renderer. Plan for this from the start — don't bolt it on at the end.

---

## 3. Technology Stack

### Chosen stack
- **Engine:** **Godot 4.6** (current stable, Jan 2026). MIT-licensed → zero fees/royalties, ideal for going commercial.
- **Primary language:** **GDScript** — Python-like, stable across the 4.x line, huge training corpus → the best language for AI-assisted ("vibe") coding.
- **Physics:** **Jolt** (the default 3D physics engine in 4.6) — deterministic and stable, good for arcade car feel. We mostly use it for *collision detection*, not full rigid-body simulation (see [Player Controller](#74-player-controller)).
- **Rendering:** **Forward+** renderer on desktop/Linux; **Mobile** renderer for the Android export preset.
- **Optional native acceleration:** **Rust via `godot-rust` / gdext (v0.5, 2026)** — *only if* profiling shows GDScript can't keep up with procedural generation. The GDExtension API has stayed backward-compatible since Godot 4.1, so this is far more stable than building the whole game in a pure-Rust engine. **Do not start here** — it slows iteration. Reach for it only when a profiler tells you to.
- **Version control:** Git from commit zero. Add a `.gitignore` for Godot (`.godot/`, export builds, `rust/target/`).

### Why not the alternatives (for *this* project)
- **Bevy (pure Rust):** tempting given your Rust background, but it's pre-1.0 with constant breaking API changes (bad for AI-generated code, which goes stale fast) and weak, maintainer-acknowledged "not easy" Android support. Wrong fit for a vibe-coded, Android-MVP, commercial game. Revisit for a future Rust-first project.
- **Unity:** viable again (runtime fee cancelled; Personal free under $200K revenue), but heavier, closed-source, and carries lingering licensing-trust risk. Keep as a fallback only if Godot's mobile renderer disappoints.
- **Three.js / web + Capacitor:** maximally reuses your TS skills, but high-speed procedural 3D that stays beautiful is exactly the workload where mobile WebGL/WebGPU struggles. Fine for a quick prototype; shaky as a commercial mobile target.

### Skills note
GDScript is cheap to learn coming from TypeScript (productive in ~a day). Your Rust expertise isn't wasted — it's the optional accelerator above. Nothing here throws away what you know.

---

## 4. Core Gameplay Loop

```
Spawn → auto-fly forward (speed ramps up) → steer to dodge buildings/obstacles
      → chain near-misses to build combo + boost meter → spend boost for risky speed
      → crash ends the run → see score + personal best → INSTANT restart → repeat
```

A single run is short (target **60–180 seconds**) and ends in a crash. The player's own mistake is always obvious, so failure feels fair and fuels the retry.

---

## 5. The Addiction Engine

*(This is the heart of "what makes it interesting/addictive." Pillars are tagged **[MVP]** or **[Later]**. The MVP ones are non-negotiable — they ARE the fun.)*

1. **Flow through speed mastery [MVP].** The fundamental pleasure. Tight, responsive controls + readable obstacles + high speed = flow state. Easy to be okay at; a high skill ceiling to chase. *If the flying doesn't feel good in an empty void, nothing else matters — so build and tune this first.*

2. **Juice / game feel [MVP basic, Later deep].** The dopamine layer. Every action gets instant sensory payoff: screen shake, speed lines, FOV punch, a satisfying near-miss "whoosh" + flash + score popup, controller/haptic rumble. This is cheap to add and disproportionately responsible for "feel." Budget real time for it.

3. **Risk/reward boost economy [MVP].** A **boost meter you fill by flying dangerously** (near-misses, skimming surfaces) and spend for a burst of speed. Faster = harder to survive AND higher scoring. Self-balancing tension that rewards bravery.

4. **Score chains & multipliers [MVP].** A combo multiplier that climbs as you chain near-misses without crashing and **resets (or decays) on a hit or a timeout**. Creates "don't break the chain" tension. Style > raw distance.

5. **Short runs + instant restart [MVP].** Sub-3-minute runs, zero-friction "Retry" (one tap, no load screen). The "one more go" loop lives or dies on restart speed.

6. **Escalating intensity [MVP].** Base speed and obstacle density ramp over the run, so each attempt has a natural arc and a number to beat. Always a personal best in sight.

7. **Procedural novelty [MVP] + Daily Seed [Later].** Procedural generation keeps every run fresh. **Later:** a shared *daily seed* so everyone worldwide races the same city today → instant competitive/social layer and a daily-return habit.

8. **Meta-progression [Later].** Persistent unlocks between runs — ships, neon paint/skins, new city districts, run modifiers. Long-term goals beyond a single score. Roguelite-style "always earning something."

9. **Leaderboards / ghosts [Later].** Global + friends leaderboards; later, race a ghost of your best run or a rival's. Powerful retention driver, pairs perfectly with the daily seed.

10. **Audio-reactive world [Later — signature feature candidate].** The city pulses, and obstacles/lights sync, to the soundtrack (à la *Thumper* / *Tetris Effect* / *Audiosurf*). For a neon speed game this is a *massive* vibe multiplier and a strong differentiator. High effort — prototype it once the core is solid and decide whether it becomes the identity of the game.

11. **Fairness via telegraphing [MVP].** Obstacles must be *readable at speed* — clear silhouettes, lead-in lighting, no unfair pop-in. Deaths must feel like the player's fault, or the retry loop breaks. This is a hard requirement, not polish.

---

## 6. MVP Definition

**The MVP is one playable mode that proves the fun.** Ship exactly this — resist scope creep.

**In scope:**
- One ship, one infinite procedurally-generated neon corridor.
- Auto-forward flight with lateral + vertical steering, clamped to the corridor.
- Speed ramps over the run; boost meter + boost burst.
- Obstacles + collision (crash = game over).
- Near-miss detection → score + combo multiplier + boost gain.
- Distance + score HUD; local high score saved.
- Instant restart.
- Minimum-viable juice: speed lines, screen shake, glow/neon, FOV-on-speed, one music track + core SFX.
- Runs at target framerate on Linux **and** one real Android device.

**Out of scope (post-MVP):** daily seed, leaderboards, meta-progression/unlocks, multiple ships, audio-reactivity, narrative, multiple biomes, monetization plumbing. (All are in [Section 5](#5-the-addiction-engine) / [Open Decisions](#11-open-decisions) for later.)

**Definition of Done (MVP):** A stranger can pick it up, immediately understand "go fast, don't crash, chain near-misses for points," play several runs in a row chasing a high score, and it runs smoothly on both target platforms.

---

## 7. Technical Architecture

### 7.1 Chunk streaming (the backbone)
The city is built from fixed-length **chunks** (e.g. 100m segments). A `ChunkManager` keeps a small window of chunks active around the player: spawn new chunks ahead as the player advances, recycle chunks that fall behind. **Use an object pool** — never instantiate/free per frame; reuse despawned chunks. Memory and GC stay flat regardless of how far the player flies.

### 7.2 Procedural generation
Each `CityChunk` is parametrized by **(seed, difficulty tier)** so generation is deterministic (essential for a future daily seed and for reproducible bugs). Within a chunk: place buildings and obstacles on a grid or along the corridor walls. Generate on a background thread or amortize across frames — **never block the main thread** with a heavy per-frame generation spike. Start simple (boxes), make it *interesting* later — a generator that avoids repetition is the hardest, highest-risk part of the project, so prototype it early.

### 7.3 Rendering buildings cheaply — **MultiMesh**
A megacity = thousands of repeated elements. Render repeated building/prop meshes with **`MultiMeshInstance3D` (GPU instancing)** so thousands of objects cost a handful of draw calls instead of thousands. Add **LOD** so distant towers render as cheap boxes. This single technique is the difference between "runs great" and "slideshow," especially on Android.

### 7.4 Player controller (arcade, not simulation)
Use an **arcade controller**, not realistic rigid-body flight:
- Constant forward velocity that **ramps up** over the run; boost adds a temporary multiplier.
- Player input drives **lateral (X)** and **vertical (Y)** movement, clamped to the corridor bounds, with smoothing/lerp for a weighty-but-responsive feel.
- Movement via `CharacterBody3D` or direct transform; use **Jolt for collision detection** (crash) rather than letting physics push the ship around.

### 7.5 Near-miss detection
Wrap the ship in an **`Area3D` slightly larger than its collision shape.** When that area overlaps an obstacle but the (smaller) collision body does **not** → register a **near-miss**: award points, add to the boost meter, bump the combo multiplier, and fire juice (time-dilation flash, whoosh, popup). This mechanic *is* the risk/reward core.

### 7.6 Scoring & combo
`ScoreManager` (autoload): `score = distance + Σ(near_miss_value × multiplier)`. Multiplier rises per chained near-miss, **resets or rapidly decays on a crash or after N seconds without a near-miss.** Persist the high score locally (`user://`).

### 7.7 Aesthetic stack (the neon look)
Drive the mood through a `WorldEnvironment` node:
- **Glow/bloom** on → neon bloom.
- **Volumetric fog** → mood, depth, *and* it conveniently hides the chunk draw distance. Dark fog/sky color with a city-glow tint.
- **Screen-space reflections** (Forward+/desktop only; 4.6 rewrote SSR for cleaner results) → wet-street neon reflections.
- **Emissive `StandardMaterial3D`** on buildings (neon strips/signage) — emissive sells the look far more cheaply than realistic PBR.
- Dark sky, distant city glow on the horizon.

### 7.8 Speed feel
- `Camera3D` **FOV lerps up with current speed** (the single most effective "fast" trick).
- **Motion blur** (desktop).
- **Speed-line** particles or a fullscreen shader on a `CanvasLayer`.
- **Screen shake** (camera noise offset) on boost and near-miss; brief **chromatic aberration** pulse on boost (desktop).

### 7.9 Input (abstract it!)
Define **`InputMap` actions** (`steer_left/right/up/down`, `boost`) and feed them from platform-specific sources so all game code reads actions, never raw devices:
- **Desktop:** keyboard (WASD/arrows + Shift to boost) and/or gamepad.
- **Android:** virtual on-screen joystick + boost button, **or** accelerometer tilt steering + boost button. Decide via playtest (see [Open Decisions](#11-open-decisions)). Because input is abstracted, swapping schemes touches one layer.

### 7.10 Mobile performance budget
- Keep draw calls low (MultiMesh everywhere repeated).
- Lean on fog to shrink draw distance.
- Few/no realtime dynamic lights — rely on emissive materials + baking.
- Fewer particles than desktop; lighter/no SSR; lighter post-fx.
- **Export to Android and test on a real mid-range device by Milestone 3** — don't let mobile perf be an end-of-project surprise. Emulators lie; use hardware.

---

## 8. Suggested Project Structure

```
res://
├── project.godot
├── CLAUDE.md                     # LEAN: versions, run/export cmds, style, layout, → docs/
├── .gitignore                    # .godot/, builds/, rust/target/
├── docs/
│   └── GAME_SPEC.md              # this file
├── scenes/
│   ├── main/Main.tscn            # root: spawns world + player + UI
│   ├── player/Ship.tscn
│   ├── world/CityChunk.tscn
│   ├── world/ChunkManager.tscn
│   └── ui/{HUD,GameOver,MainMenu}.tscn
├── scripts/
│   ├── ship.gd
│   ├── chunk_manager.gd
│   ├── city_chunk.gd
│   ├── obstacle.gd
│   └── game.gd
├── autoload/                     # singletons (Project > Project Settings > Autoload)
│   ├── GameState.gd
│   ├── ScoreManager.gd
│   └── AudioManager.gd
├── resources/
│   ├── materials/                # neon emissive .tres
│   ├── meshes/                   # building kit pieces
│   └── environment/              # WorldEnvironment .tres
├── shaders/                      # speed lines, post-fx
├── assets/{audio,textures}/
└── rust/                         # OPTIONAL gdext extension — only if profiling demands
    ├── Cargo.toml
    └── src/lib.rs
```

Keep scenes **small and composable** — it makes AI-assisted edits far more reliable.

---

## 9. Development Roadmap

Ordered **by risk**: prove the fun before building content, prove it's a game before making it pretty, and validate Android before it's too late to fix. Implement one milestone at a time and **playtest between each.**

### M0 — Project skeleton
- [x] Create Godot 4.6 project; init Git + `.gitignore`.
- [x] `Main.tscn` with a ship that moves forward through empty space + a follow `Camera3D`.
- [x] Wire `InputMap` actions + keyboard steering.
- [x] **Goal:** something moves and the camera follows.

### M1 — Flight feel *(the make-or-break milestone)*
- [x] Tune forward speed, steering response, smoothing, corridor clamps.
- [x] Boost burst + speed ramp.
- [x] FOV-on-speed and basic screen shake. *(Speed lines deferred — fold into M4 juice pass.)*
- [x] **Goal:** flying feels *great* in an empty void. *(Playtested & approved 2026-05-30.)*

### M2 — Procedural corridor
- [x] `CityChunk.tscn` with buildings via `MultiMeshInstance3D`.
- [x] `ChunkManager`: spawn-ahead / recycle-behind with an object pool.
- [x] Seed + difficulty parametrization; threaded/amortized generation.
- [x] **Goal:** an endless city to fly through, no hitches.

### M3 — Game loop
- [ ] Obstacles + Jolt collision → game-over state.
- [ ] Near-miss `Area3D` → points + boost + combo.
- [ ] `ScoreManager` (distance + near-miss × multiplier, decay on hit); local high score.
- [ ] `HUD` (score, multiplier, boost) + `GameOver` with **instant restart**.
- [ ] **First Android export + on-device perf check.**
- [ ] **Goal:** it's a real game with a score, a fail state, and the "one more go" loop.

### M4 — Aesthetic pass
- [ ] `WorldEnvironment`: glow, volumetric fog, dark sky; SSR on desktop.
- [ ] Emissive neon building materials; obstacle telegraphing.
- [ ] One music track + core SFX; deepen juice (near-miss flash/time-dilation, haptics).
- [ ] **Goal:** it's *beautiful* and it *feels* fast.

### M5 — Android ship
- [ ] Mobile renderer export preset; touch input scheme.
- [ ] Mobile perf tuning to hit framerate target on a mid-range device.
- [ ] **Goal:** MVP playable and smooth on Linux **and** Android. ✅ MVP complete.

### Post-MVP backlog
Daily seed → leaderboards/ghosts → meta-progression & unlocks → multiple ships → audio-reactive world → additional districts → monetization. (Prioritize against [Open Decisions](#11-open-decisions).)

---

## 10. Working with Claude Code

- **Put this file at `docs/GAME_SPEC.md`** and keep a **lean `CLAUDE.md`** at the repo root. `CLAUDE.md` is auto-loaded into context at the start of every Claude Code session, so keep it short and specific — it's for things needed *every* session, not the full design (that lives here and gets referenced).
- A good `CLAUDE.md` for this project contains: Godot version (4.6), how to run/export (commands), GDScript style conventions, the project layout, a few "always do X" rules, and a pointer like `See @docs/GAME_SPEC.md for full design and the milestone roadmap.`
- You can run **`/init`** to scaffold a `CLAUDE.md`, then trim it down.
- **Work milestone by milestone.** Ask Claude Code to implement one milestone (or sub-task) at a time, then playtest before moving on. Use **`/clear`** between unrelated tasks to keep context clean.
- **Track progress with the checkboxes** in this doc (or a separate `TASKS.md`) — Claude Code can tick them off as work completes.
- **Commit after each working increment.** Small, verifiable steps beat big leaps for AI-assisted work.

**Suggested first prompt to Claude Code:**
> "Read `docs/GAME_SPEC.md`. Set up the Godot 4.6 project skeleton and implement Milestone M0. Then stop so I can playtest."

---

## 11. Open Decisions

Things **you** should decide (some via playtest):
- [ ] **Final title.**
- [ ] **Art direction:** color palette (classic magenta/cyan? synthwave sunset? Tron-grid mono?) and mood (rainy *Blade Runner* noir vs. clean neon vs. glitchy). Pin this early — it drives all materials/lighting.
- [ ] **Android input scheme:** virtual joystick vs. tilt steering. Prototype both in M5, decide by feel.
- [ ] **Commercial model:** premium one-time purchase, free + cosmetic unlocks, or ad-supported on mobile? (Godot adds no constraints here, but the choice shapes the meta-progression design.)
- [ ] **Signature feature commitment:** is the **audio-reactive world** the identity of the game (high effort, high differentiation), or a nice-to-have? Decide after the core is fun.
- [ ] **Rust or not:** defer entirely until a profiler proves GDScript is the bottleneck for procedural generation.

---

## 12. Reference — Target Versions

- **Godot 4.6** (stable, Jan 2026). Renderers: Forward+ (desktop) / Mobile (Android). Jolt is the default 3D physics engine.
- **godot-rust / gdext v0.5** (2026) — optional native acceleration; GDExtension API backward-compatible since Godot 4.1. Note: Rust-on-Android via gdext is still experimental — keep any Rust extension desktop-first.
- **Export targets:** Linux + Android (MVP).

*Verify exact versions/features against current Godot docs when you start, since point releases move.*
