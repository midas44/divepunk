using Godot;
using System.Collections.Generic;

// Streams the city around the player — DIVEPUNK, milestone M2.
//
// Keeps a fixed window of chunks active: spawn ahead of the ship, recycle behind it.
// Uses an OBJECT POOL — chunks are instantiated once at startup and reused forever, so
// memory stays flat no matter how far you fly (no per-frame instantiate/free).
//
// The pool is a ring: the chunk holding index i lives in slot (i mod poolSize). When
// the player advances past a chunk, that same slot is repositioned far ahead and
// regenerated for its new index. Rebuilds are amortised (BuildsPerFrame) so generation
// never spikes the frame — at flight speed at most one new chunk is needed every few
// seconds, far under the budget.
[GlobalClass]
public partial class ChunkManager : Node3D
{
	private static readonly PackedScene CityChunkScene = GD.Load<PackedScene>("res://scenes/world/CityChunk.tscn");

	[ExportGroup("Streaming")]
	[Export] public float ChunkLength = 200.0f;      // metres per chunk (pushed down to each CityChunk)
	[Export] public int ChunksAhead = 72;            // chunks kept generated ahead of the ship (× ChunkLength = draw distance)
	[Export] public int ChunksBehind = 1;            // how many to keep behind (so the player isn't on the edge)
	[Export] public int BuildsPerFrame = 1;          // amortisation cap: max chunk (re)builds per frame

	[ExportGroup("Generation")]
	[Export] public int WorldSeed = 0;               // 0 = random each run; >0 = fixed, reproducible city
	[Export] public float BaseDifficulty = 0.15f;
	[Export] public float DifficultyRamp = 0.012f;   // difficulty added per chunk index
	[Export(PropertyHint.Range, "0,1")] public float MaxDifficulty = 1.0f;

	[ExportGroup("Scale")]
	[Export] public float WorldScale = 2.0f;         // enlarges the city around the constant-size car; pushed to each chunk. 1.0 = original

	[ExportGroup("Corridor")]
	[Export] public float CorridorHalfWidth = 120.0f;  // clear flyable half-width; pushed to each chunk
	[Export] public float CorridorFloor = 4.0f;        // corridor floor; pushed to each chunk
	[Export] public float CorridorCeiling = 1700.0f;   // corridor ceiling; pushed to each chunk

	[ExportGroup("Aesthetic (M4)")]
	[Export] public bool BuildingWindows = true;     // neon-window shader on buildings (pushed to chunks)
	[Export] public bool HazardPulse = true;         // pulsing telegraph shader on obstacles (pushed to chunks)

	[ExportGroup("Debug")]
	[Export] public bool LogStreaming = false;       // print spawn/recycle events to the console

	private Node3D _target;
	private List<CityChunk> _pool = new();
	private int _poolSize = 0;
	private int _firstIndex = 0;                      // smallest chunk index currently active
	private long _seed = 0;
	private List<int> _buildQueue = new();
	private bool _initialized = false;

	public override void _Ready()
	{
		_seed = WorldSeed > 0 ? WorldSeed : RandomSeed();
		_poolSize = ChunksAhead + ChunksBehind + 1;
		CreatePool();
		GD.Print($"[DIVEPUNK] world seed = {_seed}  (pool: {_poolSize} chunks of {ChunkLength:F0}m)");
	}

	public void SetTarget(Node3D t)
	{
		_target = t;
		InitialFill();
	}

	public override void _Process(double delta)
	{
		if (!_initialized || _target == null)
			return;
		// Advance the ring as the player moves forward (-Z): retire chunks that fell behind,
		// queue their slots for regeneration far ahead.
		int desiredFirst = CurrentIndex() - ChunksBehind;
		while (_firstIndex < desiredFirst)
		{
			QueueBuild(_firstIndex + _poolSize);
			_firstIndex += 1;
		}
		DrainQueue();
	}

	private void CreatePool()
	{
		for (int i = 0; i < _poolSize; i++)
		{
			var c = CityChunkScene.Instantiate<CityChunk>();
			c.ChunkLength = ChunkLength;
			c.WorldScale = WorldScale;
			c.CorridorHalfWidth = CorridorHalfWidth;
			c.CorridorFloor = CorridorFloor;
			c.CorridorCeiling = CorridorCeiling;
			c.BuildingWindows = BuildingWindows;
			c.HazardPulse = HazardPulse;
			AddChild(c);
			_pool.Add(c);
		}
	}

	// Builds the whole active window once, synchronously. This is a load-time cost (before
	// the player is moving), not an in-flight hitch — the world must exist on frame one.
	private void InitialFill()
	{
		if (_target == null)
			return;
		_firstIndex = CurrentIndex() - ChunksBehind;
		for (int n = 0; n < _poolSize; n++)
			BuildChunkNow(_firstIndex + n);
		_initialized = true;
	}

	private void DrainQueue()
	{
		int built = 0;
		while (built < BuildsPerFrame && _buildQueue.Count > 0)
		{
			int idx = _buildQueue[0];
			_buildQueue.RemoveAt(0);
			BuildChunkNow(idx);
			built += 1;
		}
	}

	private void QueueBuild(int idx)
	{
		if (!_buildQueue.Contains(idx))
			_buildQueue.Add(idx);
	}

	private void BuildChunkNow(int idx)
	{
		int slot = SlotFor(idx);
		CityChunk c = _pool[slot];
		c.Position = new Vector3(0.0f, 0.0f, -(float)idx * ChunkLength);
		c.Generate(idx, _seed, DifficultyFor(idx));
		if (LogStreaming)
			GD.Print($"[DIVEPUNK]   built chunk {idx} (slot {slot}) @ z={c.Position.Z:F0}");
	}

	// Chunk index the ship is currently inside (it travels toward -Z, so distance = -z).
	private int CurrentIndex()
	{
		return (int)Mathf.Floor(-_target.GlobalPosition.Z / ChunkLength);
	}

	private int SlotFor(int idx)
	{
		return ((idx % _poolSize) + _poolSize) % _poolSize;   // handles negative indices
	}

	private float DifficultyFor(int idx)
	{
		return Mathf.Clamp(BaseDifficulty + (float)Mathf.Max(idx, 0) * DifficultyRamp, 0.0f, MaxDifficulty);
	}

	private long RandomSeed()
	{
		var r = new RandomNumberGenerator();
		r.Randomize();
		return (long)r.Randi() + 1;   // +1 so it's never 0 (0 means "random" in config)
	}
}
