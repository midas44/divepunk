# Task 2 — Flight: `RigidBody3D` 6-DOF + damage (implementation handoff)

> **Methodology.** DIVEPUNK's reframe runs as **plan (session A) → implement (session B) → review (session A)**.
> This document is the **session-B implementation brief** for Task 2. It is self-contained — you do not
> need the planning conversation. When you finish, the user playtests and brings results back to
> session A for review. One such doc lives in `docs/tasks/` per reframing task.

> **⭐ This is the make-or-break FEEL GATE.** Per the spec (§5 pillar 1, §7.4) and `CLAUDE.md`: *the
> flying car must feel **great** in an empty void — and bounce + take damage on impact without ever
> ending the game — before any world is built.* Expect to spend most of your time **tuning feel**, not
> writing code. Do not rush to "it compiles"; the bar is "it's a joy to fly." Leave every number an
> `[Export]` so the user can keep dialing it in live.

> **Scope discipline.** Implement **Task 2 only**. Do **not** start the world bake (Task 3), world
> load/tiles (Task 4), terrain (Task 5), the dusk pass (Task 6), traffic (Task 7), or glTF (Task 8). Do
> **not** modify `CameraRig.cs` (see §4 — the camera is deliberately out of scope here). If something
> seems to need a later task, stop and note it in your report instead of pulling it forward.

---

## 0. Context (read first)

DIVEPUNK is a **bounded, pre-baked open-world flying-car sandbox** (read **`docs/GAME_SPEC.md` §5, §7.4,
§7.5, §7.8** and **`CLAUDE.md`** before starting). Task 1 already stripped the old arcade loops: the game
now boots into an **empty void** where a `CharacterBody3D` ship flies a kinematic throttle+strafe model,
with a telemetry HUD and **no game-over**.

**Task 2 replaces that interim flyer with the real thing:** an **assisted-arcade `RigidBody3D` 6-DOF
flying car** that Jolt simulates — thrust + pitch/yaw/roll torques + PD auto-leveling + coordinated
banked turns — and that **bounces off and takes damage from collisions** (via contact impulse) **without
ever ending the game**. You'll add a `DamageComponent`, a HUD **condition** bar, **roll** input, and a
few **test boxes** in the void to ram.

**What already exists (post-Task-1) that you will touch or rely on:**
- `scripts/Ship.cs` — the interim `CharacterBody3D` flyer. **You rewrite this** to `RigidBody3D`. It
  currently exposes `GetSpeed/GetVerticalSpeed/GetTopSpeed/GetMaxVerticalSpeed/GetBoostMeter/GetSpeedRatio`
  and emits `SpeedChanged(speed, ratio, accelerating)`. **Preserve all of these** (the camera, HUD, and
  FX read them — see below).
- `scripts/CameraRig.cs` — **do not edit.** It orbits the ship's *position* with world-up (mouse
  free-look) and reads only `ship.GetSpeedRatio()` for FOV + exposes `AddShake(float)`. It does **not**
  read the ship's orientation, so your tumbling RigidBody won't tumble the view. It also consumes the
  `cycle_camera` action on **Q** in its own `_Input` (relevant to input mapping, §3.3).
- `scripts/Game.cs` — the orchestrator. Spawns the ship/camera/env/HUD, pushes config via
  `ApplyShipConfig`/`ApplyCameraConfig` **before** `AddChild`, subscribes `_ship.SpeedChanged`, and does
  the boost juice in `OnShipSpeedChanged` (`_rig.AddShake(ShakeOnBoost)` + `AudioManager.Boost()`). **You
  extend this** for flight config, impact juice, and test boxes.
- `scripts/Hud.cs` — polls the ship each frame (`GetBoostMeter`, `GetSpeed`, `GetVerticalSpeed`,
  `GetMaxVerticalSpeed`) to drive THROTTLE / SPD / V-SPD bars via a `MakeMeter(...)` helper. **You add a
  CONDITION bar** the same way (poll, don't subscribe).
- `autoload/AudioManager.cs` — null-safe singleton. Already has **`Crash()`** (a noise+thud SFX, ideal
  for impacts) and `Boost()`. Reuse `AudioManager.Instance?.Crash()`. (`Crash()` is currently uncalled —
  dead since Task 1 — Task 2 revives it.)
- `scripts/ScreenFX.cs` — exposes `Flash(float amount, Color color)`. Optional impact-flash juice (§3.8).
- `autoload/Config.cs` — `Config.Instance.GetFloat/GetInt/GetBool(section, key, fallback)`, null-safe.

**Conventions (from `CLAUDE.md`):** C# typed throughout; every node class is `public partial class X :
Base`; `[GlobalClass]` on types referenced by scenes/Inspector; `PascalCase` members / `_camelCase`
private fields; expose tunables with `[Export]` grouped by `[ExportGroup]`; **framerate-independent
smoothing** `v.Lerp(target, 1 - Mathf.Exp(-k*(float)delta))`; read input via **`InputMap` actions**, never
raw keys; cast `delta` to `float` once; float literals need `f`.

---

## 1. Definition of Done

- `./run.sh build` → **0 errors, 0 warnings**; `./run.sh build && ./run.sh check` boots headless, exits clean.
- `./run.sh play`: you fly a **`RigidBody3D` 6-DOF** car — **thrust** (accelerate/brake along the nose),
  **pitch**, **yaw** (which banks into the turn), and **roll** — and when you **release the stick the car
  self-levels** (wings + nose return to the horizon; heading is kept).
- A few **test boxes** (and the ground) sit in the void. **Ramming them bounces the car realistically**
  (Jolt-resolved, no teleport), the **HUD CONDITION bar drops** by impact severity, a **camera shake +
  thud** fire — and **the game never ends** (condition can hit zero and nothing happens).
- The **HUD shows CONDITION** alongside THROTTLE / SPD / V-SPD.
- `CameraRig.cs` is **unchanged**; `GetSpeed/GetSpeedRatio/GetVerticalSpeed` still work (camera FOV +
  speed-line FX still respond to speed).

**This task is not "done" when it compiles — it's done when the flight feels great and the user signs off
the feel gate.** Build/check are necessary, not sufficient.

---

## 2. Preconditions

- **Branch:** confirm with the user (the reframe has been on `dev5`).
- Green baseline: `./run.sh build` already succeeds (Task 1 is committed).
- Read the files you'll touch: `scripts/Ship.cs`, `scripts/Game.cs`, `scripts/Hud.cs`,
  `scripts/CameraRig.cs` (read-only — to understand the coupling), `autoload/AudioManager.cs`,
  `autoload/Config.cs`, `settings/settings.cfg`.
- Skim `docs/GAME_SPEC.md` §7.4 (flight) and §7.5 (damage) — this brief implements them.

---

## 3. Work items

### 3.1 Rewrite `scripts/Ship.cs` → `RigidBody3D` 6-DOF (the core)

Change the base type to **`RigidBody3D`** and replace the kinematic model with force-based control.
Keep the placeholder neon-box visual + collision shape, the `SpeedChanged` signal, and all the HUD
getters. **Delete** the `MoveAndSlide`/`Velocity` model, the `LateralSpeed`/`ClimbAngleDeg` climb model,
and the visual-only `BankModel` (the body banks for real now).

Skeleton (numbers are **starting points to tune**, not gospel):

```csharp
using Godot;

// Assisted-arcade 6-DOF flying car — DIVEPUNK (spec §7.4). A RigidBody3D simulated by Jolt: thrust along
// the nose, pitch/yaw/roll torques, PD auto-leveling (self-rights on release), coordinated banked turns,
// and a speed clamp. Collisions are resolved by Jolt as a bounce; contact impulse becomes damage via the
// child DamageComponent in _IntegrateForces. Health at zero does NOTHING — there is no game-over.
public partial class Ship : RigidBody3D
{
    [ExportGroup("Thrust")]
    [Export] public float MaxSpeed = 1000.0f;     // LinearVelocity clamp (m/s) — carried from old [ship] max_speed
    [Export] public float ThrustForce = 4000.0f;  // forward push (N) along -Basis.Z when accelerating
    [Export] public float BrakeForce = 6000.0f;   // reverse/brake push (N) when decelerating
    [Export] public float BodyMass = 4.0f;        // sets RigidBody3D.Mass in _Ready (force feel scales with it)

    [ExportGroup("Rotation (torque)")]
    [Export] public float PitchTorque = 1400.0f;
    [Export] public float YawTorque = 900.0f;
    [Export] public float RollTorque = 1600.0f;
    [Export] public bool InvertPitch = true;      // carried from old [ship] invert_pitch — flips pitch sign to taste
    [Export] public bool InvertRoll = true;       // carried from old [ship] invert_bank

    [ExportGroup("Assist (auto-level + coordination)")]
    [Export] public float LevelStrength = 9.0f;   // PD kP — self-rights pitch+roll toward the horizon (yaw left free)
    [Export] public float LevelDamping = 4.5f;    // PD kD — kills the wobble so leveling settles crisply
    [Export] public float BankCoordination = 0.7f;// yaw input adds proportional roll → turns feel like flying
    [Export] public float LinearDampValue = 0.6f; // glide/coast (set onto RigidBody3D.LinearDamp)
    [Export] public float AngularDampValue = 3.0f;// rotations settle when you let go (set onto AngularDamp)

    [ExportGroup("Collision")]
    [Export] public float Bounce = 0.3f;          // PhysicsMaterial bounce on impact (0 = dead, 1 = super-ball)
    [Export] public float Friction = 0.4f;

    // Preserved: drives camera FOV, speed-line FX, audio. Emitted every physics frame.
    [Signal] public delegate void SpeedChangedEventHandler(float speed, float ratio, bool accelerating);

    private Node3D _model;
    private DamageComponent _damage;

    public override void _Ready()
    {
        // RigidBody setup for a hovering 6-DOF flyer.
        GravityScale = 0.0f;                 // zero-G: it hovers; no constant fight to stay up (spec §7.4)
        Mass = BodyMass;
        LinearDamp = LinearDampValue;
        AngularDamp = AngularDampValue;
        CanSleep = false;                    // always simulating, so input is always responsive
        ContactMonitor = true;               // required to read contacts in _IntegrateForces
        MaxContactsReported = 8;             // spec §7.4
        PhysicsMaterialOverride = new PhysicsMaterial { Bounce = Bounce, Friction = Friction };

        EnsureVisualAndCollision();          // neon-box Model + box CollisionShape3D (rotate WITH the body now)
        _damage = GetNodeOrNull<DamageComponent>("Damage");
        if (_damage == null)
        {
            _damage = new DamageComponent { Name = "Damage" };
            AddChild(_damage);               // it self-reads [damage] from Config in its own _Ready
        }
    }

    // CONTROL — forces/torques applied each physics tick (standard RigidBody pattern). All inputs via
    // InputMap actions. Auto-level runs only when you're NOT actively pitching/rolling (assisted feel).
    public override void _PhysicsProcess(double delta)
    {
        Basis b = GlobalTransform.Basis;

        // Thrust along the nose (-Z). accelerate pushes forward, decelerate brakes/reverses.
        float thrustIn = Input.GetActionStrength("accelerate") - Input.GetActionStrength("decelerate");
        float force = thrustIn >= 0.0f ? ThrustForce : BrakeForce;
        ApplyCentralForce(-b.Z * thrustIn * force);

        // Rotation input → torque about the body's local axes. Sign flips via the invert toggles + the
        // GetAxis arg order; confirm the directions feel right in the playtest (that's what they're for).
        float pitchIn = Input.GetAxis("steer_down", "steer_up") * (InvertPitch ? 1.0f : -1.0f);
        float yawIn   = Input.GetAxis("steer_right", "steer_left");
        float rollIn  = Input.GetAxis("roll_right", "roll_left") * (InvertRoll ? 1.0f : -1.0f);
        ApplyTorque(b.X * pitchIn * PitchTorque);
        ApplyTorque(b.Y * yawIn   * YawTorque);
        ApplyTorque(b.Z * rollIn  * RollTorque);

        // Coordinated turn: yaw adds proportional roll so the car banks INTO the turn.
        ApplyTorque(b.Z * (-yawIn) * BankCoordination * RollTorque);

        // PD auto-level: when not manually pitching/rolling, restore local-up toward world-up. (b.Y × Up)
        // is a horizontal axis → it levels pitch+roll but does NOT yaw, so your heading is preserved.
        if (Mathf.Abs(pitchIn) < 0.01f && Mathf.Abs(rollIn) < 0.01f)
        {
            Vector3 levelAxis = b.Y.Cross(Vector3.Up);
            ApplyTorque(levelAxis * LevelStrength - AngularVelocity * LevelDamping);
        }

        float spd = LinearVelocity.Length();
        EmitSignal(SignalName.SpeedChanged, spd, GetSpeedRatio(), thrustIn > 0.01f);
    }

    // DAMAGE + speed clamp — runs in the physics solver callback, where contact impulses are valid.
    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        int contacts = state.GetContactCount();
        float impulse = 0.0f;
        for (int i = 0; i < contacts; i++)
            impulse += state.GetContactImpulse(i).Length();   // GetContactImpulse returns a Vector3 in Godot 4.6
        if (impulse > 0.0f)
            _damage?.ApplyImpact(impulse);

        Vector3 v = state.LinearVelocity;
        if (v.Length() > MaxSpeed)
            state.LinearVelocity = v.Normalized() * MaxSpeed;
    }

    // ---- preserved HUD/camera/FX API (keep the names + signatures) ----
    public float GetSpeed() => LinearVelocity.Length();
    public float GetVerticalSpeed() => LinearVelocity.Y;
    public float GetTopSpeed() => MaxSpeed;
    public float GetMaxVerticalSpeed() => MaxSpeed;          // V-SPD bar scale; vertical can reach top speed in 6-DOF
    public float GetBoostMeter() => GetSpeedRatio();         // THROTTLE bar = current speed fraction
    public float GetSpeedRatio() => Mathf.Clamp(GetSpeed() / Mathf.Max(MaxSpeed, 0.001f), 0.0f, 1.0f);
    public float GetConditionRatio() => _damage?.GetHealthRatio() ?? 1.0f;   // NEW — HUD condition bar
    public DamageComponent Damage => _damage;               // NEW — Game subscribes to its Damaged signal

    private void EnsureVisualAndCollision() { /* keep the existing neon box Model + box CollisionShape3D,
        but DROP the visual BankModel — the RigidBody body itself pitches/rolls now. */ }
}
```

Notes / gotchas:
- **Keep `EnsureVisualAndCollision`** from the current file (the neon box `Model` + the `Col`
  `CollisionShape3D`), but **remove `BankModel` and the `_model.Rotation` lerp** — the body rotates for
  real now, so the Model child should sit at identity and rotate with its parent.
- You may instead apply control inside `_IntegrateForces` (using `state.ApplyForce`/`state.ApplyTorque`)
  if it feels more responsive — both are valid; pick what tunes best. Keep contact-impulse + clamp there
  regardless.
- Emitting `_damage` signals from inside `_IntegrateForces` is fine; if you ever see odd reentrancy,
  defer the emit (`CallDeferred`). Unlikely at this scope.

### 3.2 New file `scripts/DamageComponent.cs`

A signal-driven child of the car (spec §7.5). **Self-reads `[damage]` from Config in `_Ready`** (the
`Ship` creates it during *its* `_Ready`, so `Game` can't push config onto it pre-`AddChild` — mirror how
`AudioManager` reads its own config).

```csharp
using Godot;

// Tracks the car's condition and converts contact impulse into damage (spec §7.5). Signal-driven so the
// HUD/juice stay decoupled. HEALTH REACHING ZERO DOES NOTHING — no death, no reload (consequences are
// deferred, deeper gameplay). Reads [damage] from Config directly (it's created at runtime by Ship).
[GlobalClass]
public partial class DamageComponent : Node
{
    [Export] public float MaxHealth = 100.0f;
    [Export] public float ImpulseToDamage = 0.02f;     // damage per unit of (impulse − threshold)
    [Export] public float MinImpulseThreshold = 60.0f; // ignore gentle taps / resting contact
    [Export] public float RepairRate = 3.0f;           // passive condition regen (per sec); 0 = off. Handy while testing.

    [Signal] public delegate void HealthChangedEventHandler(float ratio);   // 0..1, for the HUD
    [Signal] public delegate void DamagedEventHandler(float amount);        // this-hit damage, for juice

    private float _health;

    public override void _Ready()
    {
        Config cfg = Config.Instance;
        if (cfg != null)
        {
            MaxHealth = cfg.GetFloat("damage", "max_health", MaxHealth);
            ImpulseToDamage = cfg.GetFloat("damage", "impulse_to_damage", ImpulseToDamage);
            MinImpulseThreshold = cfg.GetFloat("damage", "min_impulse_threshold", MinImpulseThreshold);
            RepairRate = cfg.GetFloat("damage", "repair_rate", RepairRate);
        }
        _health = MaxHealth;
        EmitSignal(SignalName.HealthChanged, GetHealthRatio());
    }

    public override void _Process(double delta)
    {
        if (RepairRate > 0.0f && _health < MaxHealth)
        {
            _health = Mathf.Min(MaxHealth, _health + RepairRate * (float)delta);
            EmitSignal(SignalName.HealthChanged, GetHealthRatio());
        }
    }

    // Convert a contact-impulse magnitude into damage. Sub-threshold contacts are ignored (so resting on
    // the ground or grazing doesn't bleed condition). Health floors at 0 and stays there — no game-over.
    public void ApplyImpact(float impulse)
    {
        if (impulse < MinImpulseThreshold)
            return;
        float dmg = (impulse - MinImpulseThreshold) * ImpulseToDamage;
        if (dmg <= 0.0f)
            return;
        _health = Mathf.Max(0.0f, _health - dmg);
        EmitSignal(SignalName.Damaged, dmg);
        EmitSignal(SignalName.HealthChanged, GetHealthRatio());
    }

    public float GetHealthRatio() => Mathf.Clamp(_health / Mathf.Max(MaxHealth, 0.001f), 0.0f, 1.0f);
    public float GetHealth() => _health;
}
```

> **Threshold tuning matters:** with `GravityScale = 0`, a car resting on the ground produces ~zero
> contact impulse, so it won't self-damage. But fast scrapes along a surface fire impulse every frame —
> that's realistic but can drain fast. Tune `MinImpulseThreshold` / `ImpulseToDamage` so a glancing
> brush is cheap and a full-speed ram is a real dent.

### 3.3 Input actions (`scripts/Game.cs` → `RegisterInput`)

Add **roll**, and free up **Q** for it (the camera owns `cycle_camera` on Q today). Mapping is a feel
decision (`docs/GAME_SPEC.md` §11 lists it as open) — this is a sensible default; mark it tunable.

- Pitch = `steer_up`/`steer_down` (**W/S**, ↑/↓) — already mapped.
- Yaw = `steer_left`/`steer_right` (**A/D**, ←/→) — already mapped (now drives yaw + coordinated roll).
- **Roll = new `roll_left` / `roll_right` on Q / E.**
- Thrust = `accelerate` (Shift/Space) / `decelerate` (Ctrl/X/C/V) — already mapped.
- **Move `cycle_camera` from Q → Tab** (the camera reads the *action*, so just rebind the key here).

```csharp
AddAction("roll_left",  new[] { Key.Q });
AddAction("roll_right", new[] { Key.E });
AddAction("cycle_camera", new[] { Key.Tab });   // moved off Q (now roll_left)
```

> **Call this out in your report** — the camera-cycle key moved from Q to Tab. If the user would rather
> keep Q for camera, roll can go on other keys (e.g. `,`/`.` or Z/C); leave it a one-line change.

### 3.4 `scripts/Game.cs` wiring

1. **Flight config.** Replace `ApplyShipConfig` with `ApplyFlightConfig(Ship)` reading the new `[flight]`
   section, pushed **before `AddChild`** (same pattern as today). Each falls back to the ship's export
   default:
   ```csharp
   private void ApplyFlightConfig(Ship ship)
   {
       ship.MaxSpeed         = CfgFloat("flight", "max_speed", ship.MaxSpeed);
       ship.ThrustForce      = CfgFloat("flight", "thrust_force", ship.ThrustForce);
       ship.BrakeForce       = CfgFloat("flight", "brake_force", ship.BrakeForce);
       ship.BodyMass         = CfgFloat("flight", "mass", ship.BodyMass);
       ship.PitchTorque      = CfgFloat("flight", "pitch_torque", ship.PitchTorque);
       ship.YawTorque        = CfgFloat("flight", "yaw_torque", ship.YawTorque);
       ship.RollTorque       = CfgFloat("flight", "roll_torque", ship.RollTorque);
       ship.LevelStrength    = CfgFloat("flight", "level_strength", ship.LevelStrength);
       ship.LevelDamping     = CfgFloat("flight", "level_damping", ship.LevelDamping);
       ship.BankCoordination = CfgFloat("flight", "bank_coordination", ship.BankCoordination);
       ship.LinearDampValue  = CfgFloat("flight", "linear_damp", ship.LinearDampValue);
       ship.AngularDampValue = CfgFloat("flight", "angular_damp", ship.AngularDampValue);
       ship.Bounce           = CfgFloat("flight", "bounce", ship.Bounce);
       ship.InvertPitch      = CfgBool("flight", "invert_pitch", ship.InvertPitch);
       ship.InvertRoll       = CfgBool("flight", "invert_roll", ship.InvertRoll);
   }
   ```
   (`[damage]` is **not** pushed here — `DamageComponent` self-reads it, §3.2.)

2. **Impact juice.** After `AddChild(_ship)` + `SetTarget`, subscribe to the damage signal (the
   `DamageComponent` exists once the ship's `_Ready` has run, i.e. after `AddChild`):
   ```csharp
   if (_ship.Damage != null)
       _ship.Damage.Damaged += OnShipDamaged;
   ```
   ```csharp
   [Export] public float ShakeOnImpact = 0.9f;   // add to the [ExportGroup("Juice")]

   private void OnShipDamaged(float amount)
   {
       _rig?.AddShake(Mathf.Clamp(amount * 0.04f, 0.15f, 1.0f));   // scale dent → shake; tune the 0.04f
       AudioManager.Instance?.Crash();
       _fx?.Flash(Mathf.Clamp(amount * 0.02f, 0.0f, 0.6f), new Color(1.0f, 0.4f, 0.3f));  // optional, see §3.8
   }
   ```
   (No `_ExitTree` unsubscribe needed: `DamageComponent` is an in-scene child that frees with the scene,
   not an autoload — same as the existing `SpeedChanged` subscription. The `CLAUDE.md` unsubscribe rule
   is specifically about **autoload** signals.)

3. **Test field to ram.** Add a handful of obstacles + a ground collider so the bounce/damage is testable
   in the void. Gate it with an export so it's trivially removable when the real world lands (Task 4):
   ```csharp
   [Export] public bool SpawnTestObstacles = true;
   ```
   - Call a new `SpawnTestField()` from `_Ready` (after `SpawnShipAndCamera`). Create ~5–8
     **`StaticBody3D`** boxes (each: a `CollisionShape3D` with a `BoxShape3D` + a `MeshInstance3D` with an
     emissive `StandardMaterial3D` so they read in the dark), scattered a few hundred metres around/ahead
     of the spawn at varied heights. Give them a `PhysicsMaterial { Bounce = 0.3f }`.
   - **Ground collision:** the visual `RefGround` has no collider. Add a static floor so you can bounce
     off the ground: a `StaticBody3D` named `Floor` with a **`WorldBoundaryShape3D`** (an infinite plane
     at Y=0). A world-boundary plane is translation-invariant on X/Z, so it needs no per-frame follow —
     add it once in `EnsureEnvironment` (or `SpawnTestField`). Leave the visual `RefGround` following as
     it does today.

4. Leave `ApplyCameraConfig`, `EnsureEnvironment`'s sky/sun/env, `OnShipSpeedChanged`, and the `_Process`
   ground-follow **as they are** (dusk retune is Task 6).

### 3.5 `scripts/Hud.cs` — add a CONDITION bar (poll, like the others)

Keep THROTTLE / SPD / V-SPD. Add a **CONDITION** bar driven by `_ship.GetConditionRatio()`, colored
green→amber→red as it drops. Reuse the existing `MakeMeter(...)` helper for the bar, then set its fill
color each frame in `_Process` (exactly how V-SPD already recolors):

- In `Build()`, add one more meter above the stack, e.g.
  `var cond = MakeMeter(root, -212.0f, "CONDITION", new Color(0.3f, 0.9f, 0.5f)); _condFill = cond.Fill; _condVal = cond.Val;`
  (pick a `barY` that doesn't overlap the existing −100/−156 rows).
- Add fields `_condFill` / `_condVal`.
- In `_Process` (guard `_ship == null` as today):
  ```csharp
  float c = _ship.GetConditionRatio();
  _condFill.Size = new Vector2(BoostBarW * Mathf.Clamp(c, 0.0f, 1.0f), _condFill.Size.Y);
  _condFill.Color = new Color(Mathf.Lerp(0.95f, 0.3f, c), Mathf.Lerp(0.25f, 0.9f, c), 0.35f);  // red→green
  _condVal.Text = $"{c * 100.0f:F0}%";
  ```
- HUD stays **poll-only** (no signal subscribe, no `_ExitTree`) — consistent with Task 1.

### 3.6 `settings/settings.cfg` — migrate `[ship]` → `[flight]`, add `[damage]`

⚠️ **The user edits this file live.** **Re-read it immediately before editing** (its values may differ
from what this brief shows), **carry over the tuned values that have a successor**, **stage it explicitly**
(`git add settings/settings.cfg` — never `git add -A`), and **never revert a tuned value**.

The new force-based control model **replaces** the old kinematic `[ship]` model, so most `[ship]` keys
have no equivalent. Migrate as follows:

- **Remove the `[ship]` section.** **Carry these live values forward into `[flight]`:**
  `max_speed` → `[flight] max_speed`; `invert_pitch` → `[flight] invert_pitch`; `invert_bank` →
  `[flight] invert_roll`. The rest (`base_speed`, `min_speed`, `accelerate_rate`, `decelerate_rate`,
  `climb_angle_deg`) belong to the replaced kinematic model and are **dropped** (note this in your
  report — they are obsolete, not reverted).
- **Add `[flight]`** (use the live carried values, not necessarily these defaults):
  ```ini
  [flight]

  ; Assisted-arcade 6-DOF RigidBody flying car (spec §7.4). All forces/torques; tune live, then save+rerun.
  ; Top speed (LinearVelocity clamp, m/s). Carried from the old [ship] max_speed.
  max_speed=1000.0

  ; Thrust along the nose. thrust_force = accelerate; brake_force = decelerate/reverse (N). Heavier mass
  ; needs more force for the same punch.
  thrust_force=4000.0
  brake_force=6000.0
  mass=4.0

  ; Rotation authority (torque). Higher = snappier pitch/yaw/roll.
  pitch_torque=1400.0
  yaw_torque=900.0
  roll_torque=1600.0

  ; Assist. level_strength/level_damping are the PD gains that self-right pitch+roll to the horizon when
  ; you release the stick (heading is kept). bank_coordination makes yaw auto-bank into the turn.
  level_strength=9.0
  level_damping=4.5
  bank_coordination=0.7

  ; Damping. linear_damp = how fast you coast to a stop; angular_damp = how fast spins settle (assist feel).
  linear_damp=0.6
  angular_damp=3.0

  ; Collision bounce on the car's physics material (0 = no bounce, 1 = super-ball).
  bounce=0.3

  ; Control-direction taste (carried from old [ship] invert_pitch / invert_bank).
  invert_pitch=true
  invert_roll=true
  ```
- **Add `[damage]`** (spec §7.5):
  ```ini
  [damage]

  ; Collision damage from contact impulse. Health at zero does NOTHING — no game-over (spec §5/§7.5).
  ; A hit below min_impulse_threshold is ignored (resting/grazing). damage = (impulse − threshold) × impulse_to_damage.
  max_health=100.0
  impulse_to_damage=0.02
  min_impulse_threshold=60.0

  ; Passive condition regen (per second); 0 = off. A little helps you keep testing without restarting.
  repair_rate=3.0
  ```
- **Keep intact:** `[display]`, `[game]`, `[camera]`, `[traffic]`, `[debug]`, `[fx]`, `[audio]`. (The
  dead `[fx] near_miss_flash`/`time_dilation` keys can stay — leave them; a later pass prunes them.)

### 3.7 Bounce / physics materials (summary)

- The car gets a `PhysicsMaterialOverride` with `Bounce`/`Friction` (in `Ship._Ready`, §3.1).
- The test boxes + (optionally) the floor get a `PhysicsMaterial { Bounce ≈ 0.3 }` too.
- **Do not** add any post-collision teleport/clamp — Jolt resolves the bounce; the control forces
  re-stabilize the tumble. That tumble-then-recover IS the feel.

### 3.8 Optional juice — impact flash (only if time)

`ScreenFX.Flash(amount, color)` already exists. The `OnShipDamaged` snippet (§3.4) calls it with a warm
red — wire it if you like the punch; skip it if it muddies the gate. The core impact feedback is **camera
shake + the `Crash()` thud + the condition drop**; the flash is polish. (A later pass formalizes an
`[fx] impact_flash` toggle — don't add config plumbing for it now.)

---

## 4. Feel-tuning guidance & known risks

**This is the gate — budget real time here.** Suggested tuning order:

1. **Hover + thrust.** With `GravityScale = 0` the car floats. Tune `mass` / `thrust_force` / `brake_force`
   / `linear_damp` until accelerating, cruising, and coasting-to-a-stop feel weighty but responsive.
2. **Rotation.** Tune `pitch/yaw/roll_torque` + `angular_damp` for crisp-but-not-twitchy turning. Confirm
   the **invert toggles** (`invert_pitch`, `invert_roll`) and the `GetAxis` arg order give directions that
   feel natural — flip them freely; that's what they're for.
3. **Auto-level.** Tune `level_strength` (kP) / `level_damping` (kD) so releasing the stick re-levels
   **crisply without oscillating**. Too much kP + too little kD = wobble; too little kP = sloppy. Verify
   it levels pitch **and** roll but **keeps your heading** (doesn't auto-yaw).
4. **Coordinated turns.** `bank_coordination` so yaw banks into the turn the way flying should feel.
5. **Bounce + damage.** Ram the boxes/ground at various speeds; tune `bounce`, then `min_impulse_threshold`
   / `impulse_to_damage` so a graze is cheap and a full-speed ram is a real dent. Confirm **no game-over
   ever**, condition can hit 0 harmlessly, and `repair_rate` lets it recover for repeated tests.

**⚠️ Known risk to evaluate (do NOT fix in Task 2): the camera does not follow your heading.**
`CameraRig` is a **mouse-orbit that tracks the car's *position* with world-up** — it does not inherit the
car's orientation (so your rolling/tumbling car won't tumble the view — good, no nausea). But it also
means **when you yaw, the camera does not auto-swing behind the nose** — you re-aim with the mouse. With
the current orbit cam this is the single most likely thing to make 6-DOF feel "off." **Evaluate it in the
playtest and report how it feels.** If it's disconnecting, the fix is a *follow-heading* camera mode — but
that's a **separate, camera-scoped change** the spec deliberately defers (§7.8 keeps free-look on the
mouse; §11 lists control/camera mapping as a playtest-settled open decision). **Leave `CameraRig.cs`
untouched here** and flag it for session A — don't pull a camera rewrite into the feel gate.

Other things to watch and **report** (don't silently fix outside scope):
- Does the car go to sleep / feel laggy after hovering still? (`CanSleep = false` should prevent it.)
- Does resting on the ground bleed condition? (It shouldn't, with zero-G + threshold — if it does, raise
  the threshold.)
- Does the speed clamp feel like a wall, or smooth?

---

## 5. Verify

1. `./run.sh build` → **0 errors, 0 warnings**.
2. `./run.sh build && ./run.sh check` → headless 180-frame boot exits clean (no exceptions). *(Headless
   can't show feel — it only proves it boots without throwing.)*
3. `./run.sh play` and confirm the DoD by hand:
   - 6-DOF: thrust/brake, pitch (W/S), yaw (A/D, banks into the turn), roll (Q/E). Release → **self-levels**.
   - Ram the **test boxes** and the **ground**: realistic **bounce**, **CONDITION drops**, **shake + thud**.
   - Condition can reach **0% with no game-over**; `repair_rate` recovers it.
   - HUD shows **CONDITION** + THROTTLE / SPD / V-SPD. Camera FOV still widens with speed.
   - Tab cycles camera distance (moved off Q); mouse still free-looks.

---

## 6. Report back to session A (for review)

Include:
- `git status` + `git diff --stat` (+ the new files `scripts/DamageComponent.cs`,
  `docs/tasks/…` is session A's, leave it).
- `./run.sh build` and `./run.sh check` output (pass/fail + warnings).
- **A detailed FEEL report** — this is the gate. How does flight feel? What did you land the key
  `[Export]`/`[flight]` values at? Does auto-level feel right? Bounce + damage? **And specifically: how
  did the non-following orbit camera feel during yaw turns (§4)?** Any feel you couldn't get right.
- **The settings.cfg migration:** exactly which old `[ship]` values you carried into `[flight]`
  (`max_speed`, `invert_pitch`→`invert_pitch`, `invert_bank`→`invert_roll`) and which you dropped.
- **The input change:** `cycle_camera` moved Q→Tab; roll on Q/E.
- **Any deviations** from this brief and why; **anything deferred** as out-of-scope.
- Commit once build + check are green, explicit staging (not `git add -A`; remember `settings/settings.cfg`
  is staged by path). Suggested message:
  `Task 2: RigidBody3D 6-DOF flight + impulse damage; condition HUD; test field`.

---

## 7. Out of scope (do not do here)
- **`CameraRig.cs` changes** (incl. follow-heading) → evaluate + report, but defer (§4).
- World bake / `WorldData` → Task 3. World load + tiles → Task 4. Terrain/ocean → Task 5.
- Dusk aesthetic retune (env/sky/`[fx]`) → Task 6. Ambient traffic → Task 7. glTF seam → Task 8.
- World borders / `[world]` config (the soft edge force) → lands with the world (no bounds to enforce in
  an empty void; unlimited flight is wanted for tuning).
- Deleting `CityChunk.cs` / `Traffic.cs` (kept for later harvest/rewrite).
- Damage *consequences* (handling loss, forced landing, repair stations) → deferred, deeper gameplay.
