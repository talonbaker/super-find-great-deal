using System;
using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;

namespace Sail.Game.World.PuffinLab;

/// <remarks>PORTED from the sibling repo <c>mp-foundation</c> @ <c>8d07fed</c>
/// (<c>scripts/game/world/LabAmbience.cs</c>) by packet EGG-1, 2026-09-02. Every synthesis recipe
/// below the "Synthesis recipes" rule is byte-for-byte the source's; the head is not, and the
/// three changes are all forced by the difference between a self-contained one-player playtest
/// scene and a room that lives inside a running 2-6 peer session:
///
/// <list type="number">
/// <item><b>The room tone was a non-positional <c>AudioStreamPlayer</c>, and that would have put
/// a fluorescent hum over the whole game.</b> In mp-foundation the lab WAS the scene, so a
/// 2D player at -24 dB was correct: there was nowhere else to be. Here the lab is a subtree of
/// the bubble level, loaded on every peer from the first frame, and a 2D stream is heard at full
/// level everywhere in the world forever — on a hillside, underwater, in the TV room. It is now
/// an <see cref="AudioStreamPlayer3D"/> at the lab's own position with a bounded
/// <see cref="AudioStreamPlayer3D.MaxDistance"/>, on the scenery bus.</item>
/// <item><b>Nothing sounds unless a local listener is actually in the lab.</b>
/// <see cref="Audible"/> is set by <see cref="PuffinLabRoom"/> from a throttled proximity test
/// against the local camera. Without it the hum holds a voice out of the ≤24 budget for a room
/// nobody is in, and the distant-hallway one-shots rent a pooled player every 14-40 s, forever,
/// to play a sound no listener can reach.</item>
/// <item><b>A headless peer never builds any of it.</b> A dedicated server and a bot have no
/// listener; the source could assume it was always the thing on someone's screen.</item>
/// </list>
///
/// <para><b>Deliberately NOT changed: the schedule is per-client.</b> The hum is continuous, so
/// there is nothing to sync; the distant thuds are 14-40 s apart, unresolved by design, and
/// carry no gameplay information — two players hearing them at different moments is the same
/// class of thing as two players' fire crackles landing on different frames. The room-wide DARK
/// BEAT, which is a shared event two players will talk about, is synced instead, and that is
/// <see cref="PuffinLabRoom"/>'s job.</para></remarks>
/// <summary>
/// The lab's soundscape, fully procedural in the SfxLab tradition (its own tiny synth —
/// SfxLab.cs stays untouched): a seamless low fluorescent hum as the room tone, the
/// buzz-and-pop of failing tubes (fired by FlickerLight), and rare, quiet, unresolved
/// sounds from somewhere down the hallway. Everything is a suggestion; nothing loud,
/// nothing explained.
/// </summary>
public partial class LabAmbience : Node3D
{
    private const int SampleRate = 48000; // matches the project's pinned mix rate

    /// <summary>Where the distant hallway sounds come from (set by PuffinLabRoom).</summary>
    [Export] public Vector3 HallwaySoundPosition { get; set; } = new(15, 1.2f, 0);

    /// <summary>How far the room tone carries. The lab is a sealed box roughly 23 x 17 m once the port's scale is applied; 32 m
    /// puts silence a little way outside its walls, so a player standing in the bubble level
    /// above or beside the buried room hears nothing, and a player inside it hears the tone at
    /// close to full level everywhere in the room.</summary>
    [Export] public float RoomToneMaxDistance { get; set; } = 32f;

    /// <summary><b>Is there a local listener in the lab right now.</b> Written by
    /// <see cref="PuffinLabRoom"/> on a throttled proximity test and read here and by
    /// <see cref="FlickerLight"/>'s buzz-pop. Static because there is exactly one Puffin Lab per
    /// process and this is pure presentation with no gameplay consequence — a second lab would
    /// need this to become an instance field, and that is why it is stated rather than assumed.
    /// Defaults to <c>false</c> so a scene that never gets a room driver is silent rather than
    /// loud.</summary>
    public static bool Audible { get; set; }

    private static AudioStreamWav? _hum;
    private static AudioStreamWav? _buzzPop;
    private static AudioStreamWav? _distantThud;
    private static AudioStreamWav? _distantShuffle;

    private readonly RandomNumberGenerator _rng = new();
    private float _nextDistantIn = 12f;
    private AudioStreamPlayer3D? _humPlayer;

    public override void _Ready()
    {
        // A dedicated server or a bot has no listener; building a voice for it is pure waste.
        if (NetworkManager.Instance is { IsHeadless: true })
        {
            SetProcess(false);
            return;
        }

        // Room tone: positional now (see the remarks), very quiet, always there while a listener
        // is in the room. Its absence during dark beats is done by ear elsewhere — the hum itself
        // never stops, which is somehow worse.
        _humPlayer = new AudioStreamPlayer3D
        {
            Name = "Hum",
            Stream = BuildHum(),
            VolumeDb = -24f,
            MaxDistance = RoomToneMaxDistance,
            Bus = AudioServer.GetBusIndex(AudioBuses.Scenery) >= 0 ? AudioBuses.Scenery : "Master",
        };
        AddChild(_humPlayer);
    }

    public override void _Process(double delta)
    {
        if (_humPlayer == null)
            return;

        // Start and stop with the listener rather than holding a voice open for an empty room.
        if (Audible && !_humPlayer.Playing)
            _humPlayer.Play();
        else if (!Audible && _humPlayer.Playing)
            _humPlayer.Stop();

        if (!Audible)
            return;

        _nextDistantIn -= (float)delta;
        if (_nextDistantIn > 0f)
            return;
        _nextDistantIn = _rng.RandfRange(14f, 40f);

        // ToGlobal, not the raw field: HallwaySoundPosition is authored in the lab's own
        // coordinates and SfxLab.PlayStream3D positions a POOLED player in world space. In
        // mp-foundation the lab sat at the origin so the two were the same number; here the
        // whole scene is placed at an offset by the level that instances it.
        Vector3 pos = ToGlobal(HallwaySoundPosition + new Vector3(_rng.RandfRange(-3f, 3f), 0, 0));
        AudioStreamWav stream = _rng.Randf() < 0.7f
            ? (_distantThud ??= BuildDistantThud())
            : (_distantShuffle ??= BuildDistantShuffle());
        PlayOneShot(this, pos, stream, volumeDb: -18f, pitchJitter: 0.12f);
    }

    /// <summary>The buzz-rattle of a tube giving out; FlickerLight fires this on dips. Silent
    /// unless a listener is actually in the lab — see <see cref="Audible"/>.</summary>
    public static void PlayBuzzPop(Node parent, Vector3 globalPos)
    {
        if (!Audible)
            return;
        PlayOneShot(parent, globalPos, _buzzPop ??= BuildBuzzPop(), volumeDb: -14f, pitchJitter: 0.10f);
    }

    /// <summary>Positional one-shot through SfxLab's shared pool (Bible §4: pooled audio
    /// path — this class keeps its own synth but never its own player nodes).</summary>
    private static void PlayOneShot(Node parent, Vector3 globalPos, AudioStreamWav stream,
        float volumeDb, float pitchJitter) =>
        SfxLab.PlayStream3D(parent, globalPos, stream, volumeDb, pitchJitter, maxDistance: 50f,
            bus: AudioBuses.Scenery);

    // --- Synthesis recipes ---------------------------------------------------------

    /// <summary>Seamless 2 s mains-hum loop: 120 Hz plus harmonics, with slow amplitude
    /// wobble. Every component completes whole cycles in 2 s, so the loop point is silent.</summary>
    private static AudioStreamWav BuildHum()
    {
        if (_hum != null)
            return _hum;
        float[] samples = Render(2.0f, (t, _) =>
        {
            float a = 0.30f * Mathf.Sin(Mathf.Tau * 120f * t)
                    + 0.14f * Mathf.Sin(Mathf.Tau * 240f * t + 1.3f)
                    + 0.05f * Mathf.Sin(Mathf.Tau * 360f * t + 0.4f)
                    + 0.03f * Mathf.Sin(Mathf.Tau * 480f * t);
            float wobble = 1f + 0.18f * Mathf.Sin(Mathf.Tau * 1.5f * t)
                              + 0.09f * Mathf.Sin(Mathf.Tau * 3.5f * t);
            return a * wobble * 0.5f;
        });
        _hum = ToWav(samples, loop: true);
        return _hum;
    }

    /// <summary>Failing-tube rattle: harmonically rich 120 Hz gated by an irregular
    /// on/off chatter, with a noise edge, dying away fast.</summary>
    private static AudioStreamWav BuildBuzzPop()
    {
        var gateRng = new Random(42);
        bool gate = true;
        int gateCounter = 0;
        float lp = 0;
        var noiseRng = new Random(7);
        float[] samples = Render(0.22f, (t, u) =>
        {
            if (gateCounter-- <= 0)
            {
                gate = gateRng.NextDouble() < 0.6;
                gateCounter = (int)(SampleRate * (0.004 + gateRng.NextDouble() * 0.008));
            }
            float tone = Mathf.Sin(Mathf.Tau * 120f * t)
                       + 0.5f * Mathf.Sin(Mathf.Tau * 240f * t)
                       + 0.3f * Mathf.Sin(Mathf.Tau * 720f * t);
            float white = (float)(noiseRng.NextDouble() * 2 - 1);
            lp += 0.15f * (white - lp);
            float body = tone * 0.45f + lp * 0.5f;
            return body * (gate ? 1f : 0.08f) * Mathf.Pow(1f - u, 1.5f) * 0.7f;
        });
        return ToWav(samples, loop: false);
    }

    /// <summary>A soft, low, muffled thump — a door? something set down? Never resolved.</summary>
    private static AudioStreamWav BuildDistantThud()
    {
        float[] samples = Render(0.40f, (t, u) =>
        {
            float freq = 70f * (1f - 0.3f * u);
            float attack = u < 0.10f ? u / 0.10f : 1f; // slow attack = heard through walls
            return Mathf.Sin(Mathf.Tau * freq * t) * attack * Mathf.Pow(1f - u, 2.5f) * 0.8f;
        });
        return ToWav(samples, loop: false);
    }

    /// <summary>A second of low dragging — footsteps? furniture? — all lows, no answer.</summary>
    private static AudioStreamWav BuildDistantShuffle()
    {
        var noiseRng = new Random(1301);
        float lp = 0;
        float[] samples = Render(1.1f, (t, u) =>
        {
            float white = (float)(noiseRng.NextDouble() * 2 - 1);
            lp += 0.04f * (white - lp); // heavy lowpass: rumble, not hiss
            float lurch = 0.5f + 0.5f * Mathf.Sin(Mathf.Tau * 2.2f * t); // uneven gait
            float env = Mathf.Sin(Mathf.Pi * u); // fade in, fade out
            float rumble = 0.2f * Mathf.Sin(Mathf.Tau * 60f * t);
            return (lp * 2.2f + rumble) * lurch * env * 0.5f;
        });
        return ToWav(samples, loop: false);
    }

    // --- Plumbing --------------------------------------------------------------------

    private static float[] Render(float seconds, Func<float, float, float> sample)
    {
        int count = (int)(SampleRate * seconds);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float u = i / (float)count;
            data[i] = Mathf.Clamp(sample(t, u), -1f, 1f);
        }
        return data;
    }

    private static AudioStreamWav ToWav(float[] samples, bool loop)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(samples[i] * short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = samples.Length,
        };
    }
}
