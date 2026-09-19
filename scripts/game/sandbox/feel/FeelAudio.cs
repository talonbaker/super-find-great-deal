using Godot;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// THREE CUES, SYNTHESISED AT STARTUP. No audio files, no import step, no licence.
///
/// The brief asks for an audio cue on a rejected placement. Shipping a .wav to satisfy that would
/// put a binary asset into a repo that has deliberately not got any, and would make the cue
/// something you have to go and find rather than something you can read. Twelve milliseconds of
/// arithmetic at startup gives the same result and the waveform is right there in the source.
///
/// -----------------------------------------------------------------------------------------------
/// WHY AUDIO IS PART OF "FEEL" AND NOT DECORATION
/// -----------------------------------------------------------------------------------------------
/// A grab, a placement and a rejection are three different outcomes that can look nearly identical
/// for the first few frames — the item leaves the hand in all three. Sound is what separates them
/// AT THE MOMENT OF THE INPUT rather than a quarter of a second later when the item has landed.
/// That is also why the rejection cue is the one with the most distinct timbre: it is the only one
/// of the three that means "that did not do what you asked".
///
/// COST: three short buffers, about 26 KB of 16-bit mono at 22.05 kHz, decoded once. Playback is
/// three voices in the worst case. This is not a budget line on any hardware.
///
/// DETERMINISTIC BY CONSTRUCTION. The noise in the grab click comes from a fixed-seed LCG rather
/// than from GD.Randf, so the baked buffers are byte-identical on every run. A lab whose output
/// changes between runs cannot be A/B'd, and this repo's whole metrology argument rests on being
/// able to take two captures that differ only by the thing under test.
/// </summary>
public partial class FeelAudio : Node
{
	private const int Rate = 22050;

	private AudioStreamWav _grab = null!;
	private AudioStreamWav _place = null!;
	private AudioStreamWav _reject = null!;

	private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[3];
	private int _voice;

	/// <summary>Master switch, so the lab can A/B whether the cues are doing any of the work people
	/// think the visuals are doing. They usually are.</summary>
	[Export] public bool Enabled { get; set; } = true;

	/// <summary>Cue level in decibels. Quiet by default: these are confirmations, not events.</summary>
	[Export(PropertyHint.Range, "-40,6,0.5")] public float VolumeDb { get; set; } = -9.0f;

	public override void _Ready()
	{
		_grab = BakeGrab();
		_place = BakePlace();
		_reject = BakeReject();

		for (int i = 0; i < _voices.Length; i++)
		{
			// A small fixed pool, round-robined. One player would cut a cue off mid-tail whenever
			// two land close together — which is exactly what happens on a fast place-then-grab, so
			// it would be audible in the most common sequence rather than a rare one.
			_voices[i] = new AudioStreamPlayer { Name = $"Voice{i}" };
			AddChild(_voices[i]);
		}
	}

	private void Play(AudioStream s, float pitch)
	{
		if (!Enabled) return;
		var v = _voices[_voice];
		_voice = (_voice + 1) % _voices.Length;
		v.Stream = s;
		v.PitchScale = Mathf.Clamp(pitch, 0.4f, 2.5f);
		v.VolumeDb = VolumeDb;
		v.Play();
	}

	/// <summary>Heavier items sound lower and duller. Same heft the spring and the squash read, so
	/// all three agree about what this object weighs without three tunings to keep in sync.</summary>
	public void Grab(float heft) => Play(_grab, Mathf.Lerp(1.22f, 0.74f, heft));

	public void Place(float heft) => Play(_place, Mathf.Lerp(1.30f, 0.68f, heft));

	/// <summary>Fixed pitch. A rejection is a statement about the SYSTEM's answer, not about the
	/// object, so making it vary by item would imply the object had something to do with it.</summary>
	public void Reject() => Play(_reject, 1.0f);

	// ------------------------------------------------------------------------------- baking

	/// <summary>A short broadband click with a fast exponential decay — the sound of contact, with
	/// no pitch of its own to argue with the object's material.</summary>
	private static AudioStreamWav BakeGrab()
	{
		int n = Rate / 12; // ~83 ms
		var s = new float[n];
		uint seed = 0x5EED_1234u;
		float lp = 0.0f;
		for (int i = 0; i < n; i++)
		{
			float t = (float)i / Rate;
			float env = Mathf.Exp(-46.0f * t);
			float noise = Lcg(ref seed) * 2.0f - 1.0f;
			// One-pole low pass. Raw white noise reads as a burst of static; rolling the top off
			// turns the same buffer into a click with a body to it.
			lp += (noise - lp) * 0.34f;
			// A quiet 180 Hz thump under the click gives it a size. Without it every item sounds
			// like picking up a pebble regardless of the pitch scaling applied on playback.
			float body = Mathf.Sin(Mathf.Tau * 180.0f * t) * 0.45f * Mathf.Exp(-30.0f * t);
			s[i] = (lp * 0.85f + body) * env;
		}
		return Wav(s);
	}

	/// <summary>A downward pitch sweep: the classic "something landed" shape. The sweep is what
	/// makes it read as an impact rather than as a beep.</summary>
	private static AudioStreamWav BakePlace()
	{
		int n = Rate / 5; // ~200 ms
		var s = new float[n];
		float phase = 0.0f;
		for (int i = 0; i < n; i++)
		{
			float t = (float)i / Rate;
			float f = Mathf.Lerp(150.0f, 68.0f, Mathf.Min(1.0f, t * 9.0f));
			phase += Mathf.Tau * f / Rate;
			float env = Mathf.Exp(-16.0f * t);
			// A touch of second harmonic keeps it from sounding like a test tone.
			s[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 2.0f) * 0.22f) * env * 0.8f;
		}
		return Wav(s);
	}

	/// <summary>Two flat blips, deliberately square-ish and deliberately NOT descending — the
	/// opposite shape to the placement cue, so the two can never be confused even at low volume or
	/// through a bad speaker.</summary>
	private static AudioStreamWav BakeReject()
	{
		int n = Rate / 5;
		var s = new float[n];
		for (int i = 0; i < n; i++)
		{
			float t = (float)i / Rate;
			// Blip, gap, blip. The gap is the information: one tone is a confirmation in every
			// interface anyone has ever used, and two is a refusal.
			float local = t < 0.055f ? t : (t >= 0.085f && t < 0.14f ? t - 0.085f : -1.0f);
			if (local < 0.0f) { s[i] = 0.0f; continue; }
			float env = Mathf.Exp(-24.0f * local) * Mathf.Min(1.0f, local * 400.0f);
			float sq = Mathf.Sin(Mathf.Tau * 196.0f * t) >= 0.0f ? 1.0f : -1.0f;
			// Softened square: a hard one is harsh enough that people turn the cues off, and a cue
			// nobody keeps on is a cue that does not exist.
			s[i] = Mathf.Lerp(Mathf.Sin(Mathf.Tau * 196.0f * t), sq, 0.4f) * env * 0.55f;
		}
		return Wav(s);
	}

	/// <summary>Deterministic LCG in [0,1). Numerical Recipes constants; the quality bar for one
	/// low-passed noise burst is "not periodic within 83 ms", which this clears by a mile.</summary>
	private static float Lcg(ref uint state)
	{
		state = state * 1664525u + 1013904223u;
		return (state >> 8) * (1.0f / 16777216.0f);
	}

	/// <summary>Pack floats into 16-bit little-endian mono. Clamped, not scaled to fit: a cue whose
	/// level depends on its own peak is a cue whose loudness changes when you edit its waveform.</summary>
	private static AudioStreamWav Wav(float[] samples)
	{
		var bytes = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
		{
			int v = Mathf.RoundToInt(Mathf.Clamp(samples[i], -1.0f, 1.0f) * 32767.0f);
			bytes[i * 2] = (byte)(v & 0xFF);
			bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
		}
		return new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = Rate,
			Stereo = false,
			Data = bytes,
		};
	}
}
