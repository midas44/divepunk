using Godot;

// Chase camera on a RIGID orbit boom (mouse free-look), with speed-driven FOV and shake —
// DIVEPUNK, milestone M1.
//
// Put this Node3D in the scene; it creates a Camera3D child named "Camera" if one isn't present.
// Call SetTarget(ship) to follow the ship — Game.cs does this for you.
//
// The camera sits on a fixed-length boom and orbits the car: the mouse flies it AROUND the car
// (look back swings it to the front), and it always points straight at the car, so the car stays
// dead-centre and visible from any angle — including directly above or below. The boom is RIGID
// (no follow-lag) on purpose: an earlier smoothed pivot trailed the car by ~speed/sharpness
// metres, which at flight speed exceeded the boom length and pinned the camera behind the car at
// every angle. Smoothness instead comes from the physics tick matching the render rate (see
// project.godot physics_ticks_per_second) so the car never judders against the world.
//
// FOV widening with speed is the single most effective "this feels fast" trick, so the FOV range
// here is worth tuning alongside the ship's speed values.
public partial class CameraRig : Node3D
{
	[ExportGroup("Follow")]
	[Export] public Vector3 Offset = new Vector3(0.0f, 4.0f, 12.0f);  // boom: behind (+Z) and above the car; its length is the orbit radius

	[ExportGroup("Distance levels")]
	[Export] public float DistanceClose = 0.7f;      // boom multiplier — closest level
	[Export] public float DistanceNormal = 1.3f;     // boom multiplier — default
	[Export] public float DistanceFar = 2.2f;        // boom multiplier — farthest level
	[Export] public int DistanceDefaultIndex = 1;    // level a run starts on: 0=close, 1=normal, 2=far
	[Export] public float DistanceZoomSharpness = 8.0f;  // how fast the boom eases between levels when you press Q

	[ExportGroup("FOV")]
	[Export] public float BaseFov = 78.0f;           // cruising vertical FOV
	[Export] public float MaxFov = 100.0f;           // widens with speed = sense of velocity
	[Export] public float FovSharpness = 4.0f;

	[ExportGroup("Clipping")]
	[Export] public float NearDistance = 0.1f;       // near clip plane (m)
	[Export] public float FarDistance = 18000.0f;    // far clip = hard draw distance (m); keep it > the city draw distance so fog hides the edge

	[ExportGroup("Shake")]
	[Export] public float ShakeDecay = 5.0f;
	[Export] public float ShakeStrength = 0.6f;

	[ExportGroup("Free-look (mouse)")]
	[Export] public bool MouseLookEnabled = true;       // orbit the camera around the car with the mouse (no auto-return)
	[Export] public float MouseSensitivity = 0.0014f;   // orbit (radians) per pixel of mouse motion
	[Export] public float LookPitchLimitDeg = 90.0f;    // up / down orbit limit; 90° reaches directly above / below. Yaw is unlimited (full 360°)

	private Node3D _target;
	private Camera3D _cam;
	private float _shake = 0.0f;
	private float _lookYaw = 0.0f;      // held orbit yaw (radians), full 360°; persists until you move the mouse
	private float _lookPitch = 0.0f;    // held orbit pitch (radians), clamped to ±limit
	private float[] _distances = System.Array.Empty<float>();
	private int _distanceIndex = 1;     // index into _distances, cycled by the cycle_camera (Q) action
	private float _distanceMult = 1.0f; // smoothed current boom multiplier, eased toward _distances[_distanceIndex]

	public override void _Ready()
	{
		_cam = GetNodeOrNull<Camera3D>("Camera");
		if (_cam == null)
		{
			_cam = new Camera3D();
			_cam.Name = "Camera";
			AddChild(_cam);
		}
		_cam.Near = NearDistance;
		_cam.Far = FarDistance;
		_cam.Fov = BaseFov;
		_distances = new float[] { DistanceClose, DistanceNormal, DistanceFar };
		_distanceIndex = Mathf.Clamp(DistanceDefaultIndex, 0, _distances.Length - 1);
		_distanceMult = _distances[_distanceIndex];   // start at the chosen level (no opening zoom)
	}

	public void SetTarget(Node3D t)
	{
		_target = t;
	}

	// Advance to the next camera distance level (close → normal → far → close); bound to the
	// cycle_camera action (Q). The boom length eases toward the new level in _Process.
	private void CycleDistance()
	{
		if (_distances.Length == 0)
			return;
		_distanceIndex = (_distanceIndex + 1) % _distances.Length;
	}

	// Call on impacts / boosts for a punch of screen shake. amount ~0.3–1.0.
	public void AddShake(float amount)
	{
		_shake = Mathf.Min(_shake + amount, 1.0f);
	}

	// Accumulate mouse motion into a held orbit angle (no auto-return). Handled in _Input (not
	// _UnhandledInput) so a full-screen Control can never swallow it; the captured cursor (set in
	// Game.cs) produces the relative-motion events. Non-inverted: right orbits right, up orbits up.
	public override void _Input(InputEvent @event)
	{
		// Cycle the camera distance (Q) — handled before the mouse-look guard so it works even with
		// free-look disabled.
		if (@event.IsActionPressed("cycle_camera"))
		{
			CycleDistance();
			return;
		}
		if (!MouseLookEnabled)
			return;
		if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			Vector2 motion = mm.Relative;
			// Yaw orbits a full 360° (look back swings the camera to the front); wrap to keep it tidy.
			_lookYaw = (float)Mathf.Wrap(_lookYaw - motion.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
			// Pitch orbits up / down to the limit (±90° = directly above / below the car).
			float pitchLimit = Mathf.DegToRad(LookPitchLimitDeg);
			_lookPitch = Mathf.Clamp(_lookPitch - motion.Y * MouseSensitivity, -pitchLimit, pitchLimit);
		}
	}

	public override void _Process(double delta)
	{
		if (_target == null)
			return;

		float d = (float)delta;

		// Rigid boom: orbit the camera around the car's ACTUAL position (no follow-lag). One spin
		// rotates both the boom offset and the camera's orientation, so the camera always faces the
		// car with no LookAt — reaching directly above / below with no pole degeneracy, and keeping
		// the car dead-centre at every angle (the lag-free boom is what lets the orbit reach the front).
		Vector3 pivot = _target.GlobalPosition;
		// Held free-look orbit, plus a hold-to-glance-behind on `rear_view` (middle mouse): while it's
		// held, swing the orbit a half-turn so the camera flips to the FRONT and looks back down the trail.
		float viewYaw = MouseLookEnabled ? _lookYaw : 0.0f;
		float viewPitch = MouseLookEnabled ? _lookPitch : 0.0f;
		if (Input.IsActionPressed("rear_view"))
			viewYaw += Mathf.Pi;
		Basis spin = Basis.FromEuler(new Vector3(viewPitch, viewYaw, 0.0f));
		// Ease the boom length toward the selected distance level (close / normal / far via Q).
		float targetMult = _distances.Length > 0 ? _distances[_distanceIndex] : 1.0f;
		_distanceMult = Mathf.Lerp(_distanceMult, targetMult, 1.0f - Mathf.Exp(-DistanceZoomSharpness * d));
		Vector3 effOffset = Offset * _distanceMult;
		Basis rest = Basis.LookingAt(-effOffset, Vector3.Up);   // aim the camera's −Z at the car from the rest pose
		GlobalTransform = new Transform3D(spin * rest, pivot + spin * effOffset);

		// Speed → FOV.
		float ratio = 0.0f;
		if (_target is Ship ship)
			ratio = ship.GetSpeedRatio();
		float targetFov = Mathf.Lerp(BaseFov, MaxFov, ratio);
		_cam.Fov = Mathf.Lerp(_cam.Fov, targetFov, 1.0f - Mathf.Exp(-FovSharpness * d));

		// Shake decays each frame; applied as a small screen-space lens offset.
		_shake = Mathf.MoveToward(_shake, 0.0f, ShakeDecay * d);
		float s = _shake * _shake * ShakeStrength;
		_cam.HOffset = (float)GD.RandRange(-1.0, 1.0) * s;
		_cam.VOffset = (float)GD.RandRange(-1.0, 1.0) * s;
	}
}
