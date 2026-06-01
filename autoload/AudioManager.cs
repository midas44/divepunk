using Godot;
using System.Collections.Generic;

// AudioManager — DIVEPUNK, milestone M4 (autoload singleton "AudioManager").
//
// Every sound is SYNTHESISED procedurally in code (AudioStreamWav built at boot) — placeholder
// tones until real audio assets are sourced, so the game ships with working juice + a music bed and
// zero binary assets. SFX: near-miss (bright blip), boost (rising whoosh), crash (noise + thud).
// Music: a short looping A-minor synth bed. Levels come from settings.cfg [audio] (master / music /
// sfx, linear 0..1) + music_enabled. Decoupled from gameplay: Game.cs calls it through the null-safe
// AudioManager.Instance?.X() singleton, so the game runs fine even if audio is unavailable.
public partial class AudioManager : Node
{
	// Audio is intentionally optional: callers use AudioManager.Instance?.NearMiss() etc.
	public static AudioManager Instance { get; private set; }

	private const int MixRate = 22050;
	private const int SfxVoices = 6;             // overlapping SFX players (round-robin), so chained near-misses don't cut each other off

	private AudioStreamPlayer _musicPlayer;
	private List<AudioStreamPlayer> _sfxPlayers = new();
	private int _sfxNext = 0;
	private Dictionary<string, AudioStreamWav> _sfx = new();   // name -> AudioStreamWav

	private float _master = 0.9f;
	private float _musicVol = 0.5f;
	private float _sfxVol = 0.8f;
	private bool _musicOn = true;

	public override void _Ready()
	{
		Instance = this;
		ReadConfig();
		BuildSfx();
		BuildPlayers();
	}

	private void ReadConfig()
	{
		// Config._Ready has already run (autoload order: Config -> AudioManager),
		// but stay defensive — audio is optional and must not hard-depend on Config existing.
		Config cfg = Config.Instance;
		if (cfg != null)
		{
			_master = cfg.GetFloat("audio", "master", _master);
			_musicVol = cfg.GetFloat("audio", "music", _musicVol);
			_sfxVol = cfg.GetFloat("audio", "sfx", _sfxVol);
			_musicOn = cfg.GetBool("audio", "music_enabled", _musicOn);
		}
	}

	private void BuildPlayers()
	{
		_musicPlayer = new AudioStreamPlayer();
		_musicPlayer.Stream = BuildMusic();
		_musicPlayer.VolumeDb = ToDb(_master * _musicVol);
		AddChild(_musicPlayer);

		for (int i = 0; i < SfxVoices; i++)
		{
			var p = new AudioStreamPlayer();
			p.VolumeDb = ToDb(_master * _sfxVol);
			AddChild(p);
			_sfxPlayers.Add(p);
		}
	}

	private void BuildSfx()
	{
		_sfx["near_miss"] = Wav(SynthNearMiss());
		_sfx["boost"] = Wav(SynthBoost());
		_sfx["crash"] = Wav(SynthCrash());
	}

	// ---- public API (called from Game.cs via the null-safe Instance) ----

	public void NearMiss() => Play("near_miss");

	public void Boost() => Play("boost");

	public void Crash() => Play("crash");

	public void StartMusic()
	{
		if (_musicOn && _musicPlayer != null && !_musicPlayer.Playing)
			_musicPlayer.Play();
	}

	public void StopMusic()
	{
		if (_musicPlayer != null)
			_musicPlayer.Stop();
	}

	private void Play(string sfxName)
	{
		if (_sfxPlayers.Count == 0 || !_sfx.ContainsKey(sfxName))
			return;
		var p = _sfxPlayers[_sfxNext];
		_sfxNext = (_sfxNext + 1) % _sfxPlayers.Count;
		p.Stream = _sfx[sfxName];
		p.Play();
	}

	// ---- synthesis -------------------------------------------------------------

	private float ToDb(float linear)
	{
		return Mathf.LinearToDb(Mathf.Max(linear, 0.0001f));
	}

	// Wrap a mono float sample buffer ([-1,1]) into a 16-bit PCM AudioStreamWav (optionally looping).
	private AudioStreamWav Wav(float[] samples, bool loop = false)
	{
		var w = new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = MixRate,
			Stereo = false,
		};
		var bytes = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
		{
			short s = (short)(Mathf.Clamp(samples[i], -1.0f, 1.0f) * 32767.0f);
			bytes[i * 2] = (byte)(s & 0xFF);              // little-endian, matches encode_s16
			bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
		}
		w.Data = bytes;
		if (loop)
		{
			w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			w.LoopBegin = 0;
			w.LoopEnd = samples.Length;
		}
		return w;
	}

	private float[] SynthNearMiss()
	{
		// Bright, quick upward blip — positive feedback for a chained near-miss.
		float dur = 0.18f;
		int n = (int)(dur * MixRate);
		var outp = new float[n];
		float phase = 0.0f;
		for (int i = 0; i < n; i++)
		{
			float prog = (float)i / (float)n;
			float freq = Mathf.Lerp(760.0f, 1500.0f, prog);
			phase += Mathf.Tau * freq / (float)MixRate;
			float env = Mathf.Exp(-prog * 5.0f) * (1.0f - Mathf.Exp(-prog * 40.0f));   // fast attack, exp decay
			outp[i] = Mathf.Sin(phase) * env * 0.5f;
		}
		return outp;
	}

	private float[] SynthBoost()
	{
		// Rising whoosh — frequency sweep + a noise swell.
		float dur = 0.45f;
		int n = (int)(dur * MixRate);
		var outp = new float[n];
		float phase = 0.0f;
		var rng = new RandomNumberGenerator();
		rng.Seed = 1234UL;
		for (int i = 0; i < n; i++)
		{
			float prog = (float)i / (float)n;
			float freq = Mathf.Lerp(180.0f, 760.0f, prog * prog);
			phase += Mathf.Tau * freq / (float)MixRate;
			float tone = Mathf.Sin(phase) + 0.5f * Mathf.Sin(phase * 2.0f);
			float noise = rng.RandfRange(-1.0f, 1.0f) * 0.3f;
			float env = Mathf.Sin(Mathf.Pi * prog);            // swell in and out
			outp[i] = (tone * 0.5f + noise) * env * 0.5f;
		}
		return outp;
	}

	private float[] SynthCrash()
	{
		// Noise burst + a downward thud.
		float dur = 0.5f;
		int n = (int)(dur * MixRate);
		var outp = new float[n];
		float phase = 0.0f;
		var rng = new RandomNumberGenerator();
		rng.Seed = 9UL;
		for (int i = 0; i < n; i++)
		{
			float prog = (float)i / (float)n;
			float freq = Mathf.Lerp(300.0f, 60.0f, prog);
			phase += Mathf.Tau * freq / (float)MixRate;
			float thud = Mathf.Sin(phase);
			float noise = rng.RandfRange(-1.0f, 1.0f);
			float env = Mathf.Exp(-prog * 6.0f);
			outp[i] = (noise * 0.6f + thud * 0.6f) * env * 0.7f;
		}
		return outp;
	}

	private AudioStreamWav BuildMusic()
	{
		// A short, moody A-minor synth loop: plucky bass roots + a soft arpeggio. Notes are plucky and
		// the tail is faded so the buffer wraps without a click (the head already starts near zero).
		int beats = 8;
		float beatDur = 60.0f / 120.0f;            // 120 BPM
		float dur = (float)beats * beatDur;
		int n = (int)(dur * MixRate);
		var outp = new float[n];                    // zero-filled

		float[] roots = { 110.0f, 110.0f, 87.31f, 98.0f };      // A2 A2 F2 G2 — one root per 2 beats
		float[] arp = { 220.0f, 261.63f, 329.63f, 261.63f };    // A3 C4 E4 C4 — cycled per half-beat

		for (int b = 0; b < beats; b++)
		{
			float root = roots[(b / 2) % roots.Length];
			AddNote(outp, (float)b * beatDur, beatDur * 1.9f, root, 0.35f, 2.5f);
		}

		int steps = beats * 2;
		for (int s = 0; s < steps; s++)
		{
			float f = arp[s % arp.Length];
			AddNote(outp, (float)s * (beatDur * 0.5f), beatDur * 0.45f, f, 0.16f, 6.0f);
		}

		int fade = (int)(0.008f * MixRate);
		for (int i = 0; i < fade; i++)
		{
			int idx = n - 1 - i;
			if (idx >= 0)
				outp[idx] *= (float)i / (float)fade;
		}

		return Wav(outp, true);
	}

	// Adds a plucky sine note (+ a 2nd harmonic) into `buf` at startT for dur seconds.
	private void AddNote(float[] buf, float startT, float dur, float freq, float amp, float decay)
	{
		int start = (int)(startT * MixRate);
		int count = (int)(dur * MixRate);
		float phase = 0.0f;
		for (int i = 0; i < count; i++)
		{
			int idx = start + i;
			if (idx >= buf.Length)
				break;
			float prog = (float)i / (float)count;
			phase += Mathf.Tau * freq / (float)MixRate;
			float env = Mathf.Exp(-prog * decay) * (1.0f - Mathf.Exp(-prog * 60.0f));
			float s = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2.0f)) * env * amp;
			buf[idx] = Mathf.Clamp(buf[idx] + s, -1.0f, 1.0f);
		}
	}
}
