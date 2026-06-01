using Godot;

// Tracks the car's condition and converts contact impulse into damage (spec §7.5). Signal-driven so the
// HUD/juice stay decoupled. HEALTH REACHING ZERO DOES NOTHING — no death, no reload (consequences are
// deferred, deeper gameplay). Reads [damage] from Config directly (it's created at runtime by Ship, so
// Game can't push config onto it pre-AddChild — mirror how AudioManager reads its own config).
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
