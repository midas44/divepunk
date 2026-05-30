# History of chosen prompts


I'm building DIVEPUNK — a high-speed arcade flyer through a procedurally generated
cyberpunk megacity — in Godot 4.6 (GDScript), targeting Linux and Android.

The repo currently contains ONLY source files I prepared: CLAUDE.md (root),
docs/GAME_SPEC.md, scripts/game.gd, scripts/ship.gd, scripts/camera_rig.gd, and
scenes/main/Main.tscn. It is NOT a Godot project yet — there is no project.godot,
nothing has been imported, and Godot may not even be installed. Do not assume
anything runs.

First, read CLAUDE.md and docs/GAME_SPEC.md in full. They define the stack, code
conventions, performance/architecture rules, and the milestone roadmap (M0–M5);
follow them throughout. The scripts already implement M0–M1: game.gd is a
self-bootstrapping root that registers input in code and spawns a placeholder ship
+ chase camera + a minimal night environment; ship.gd is the arcade flight
controller; camera_rig.gd is the speed-reactive chase camera. Main.tscn is just a
Node3D with game.gd attached. There are no external art assets, so nothing needs
importing beyond the scripts and scene.

Step 1 — Turn this into a runnable Godot 4.6 project:
  a) Check whether a Godot 4.x binary is on PATH (try `godot --version` and
     `godot4 --version`). If it is NOT installed or not on PATH, STOP and tell me —
     I'll install the Godot 4.6 editor and give you the binary path before you
     continue.
  b) Create a valid Godot 4.6 project.godot: application name "DIVEPUNK", main
     scene res://scenes/main/Main.tscn, desktop Forward+ renderer. Add a standard
     Godot .gitignore (ignore .godot/ and build outputs).
  c) Import and do a headless boot-check (Godot can import via `--import`, then run
     and quit headlessly), and fix any parse/config errors until it starts cleanly.
     Note: you can't see the rendered game — a clean headless boot is the goal.
  d) STOP and tell me it's ready. I'll open it in the Godot editor and playtest the
     M1 flight feel (WASD/arrows steer, Shift/Space boost, R restart) before we add
     anything — per the spec, flying must feel good in the empty corridor before we
     build the city.

Step 2 — ONLY after I've playtested and told you to proceed: implement M2 (chunk
streaming + MultiMesh) exactly as described in the spec (§7 Technical Architecture,
§9 Roadmap): an infinite procedural corridor of pooled, seed-deterministic chunks
(spawn ahead, recycle behind), buildings via MultiMeshInstance3D, generation off
the main thread or amortized so there are no per-frame hitches. Keep scenes small
and single-responsibility; expose tunables as @export fields.

Working rules: one step at a time, pause for me to playtest between increments,
commit after each working change, and ask before any decision that deviates from
the spec.

Start with Step 1.

---------------------------------------

tested - looks awesome! You can start M2. Also please create some config/config.toml / yaml / ini - choose the best format for this project; place here the most      
  important tweaking parameters, like window state / resolution etc

---------------------------------------

 before M4 let's do control improvements: Esc - to exit game; F - toggle full screen / windowed; mouse to change view direction (similar to computer games like Forza  
  Horizon, Cyberpunk 2077 (driving mode)); mouse cursor should be hidden.  

---------------------------------------

---------------------------------------

---------------------------------------

---------------------------------------

---------------------------------------