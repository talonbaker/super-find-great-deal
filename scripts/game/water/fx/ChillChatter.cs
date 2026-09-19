using System;
using Godot;
using MpFoundation.Game.World;

namespace Sail.Game.Water.Fx;

/// <summary>
/// The cold's third channel: your own teeth, and your own breath.
///
/// <b>Whose this is.</b> <c>ChillCueOverlay</c>'s class doc hands it over by name — "Audio is
/// packet W4's; frost at the screen edge and an irregular quickening rumble are here." The
/// urgency-cue contract (MECHANICS-BIBLE §10.4 via INTERACTION-BIBLE §8.2) asks for three
/// redundant channels and is satisfied by any two, so the shipped visual and haptic pair already
/// meet it. This is the third, and it exists because the lake's whole cue is a slow build and a
/// build with only two channels degrades badly for a player who happens to be looking at a
/// teammate rather than at their own screen edge.
///
/// <b>Non-positional, and that is the entire budget argument.</b> Direction §10.1 flagged a real
/// collision for this packet to resolve: the written ceiling is ≤24 concurrent 3D voices, the camp
/// already spends 19 (5 <c>VoiceSpeaker</c>s + <c>SfxLab</c>'s 14-slot cap), and a six-player
/// lobby all in the water would want 5 positional remote-chatter voices — <i>exactly</i> the five
/// that are left, before anything else asks for one. That is not a budget to manage, it is a
/// budget to not spend.
///
/// So this channel is the OWNER'S ONLY, on a single plain <see cref="AudioStreamPlayer"/>, which
/// is outside the 3D ceiling entirely — the same arithmetic that let <c>AmbientBed</c>'s
/// continuous layers exist at all. Cost to the concurrent-3D-voice count: zero. Cost to
/// <c>SfxLab</c>'s shared pool: zero, because it never rents a slot. The five free voices stay
/// free for proximity speech, which is a fairness channel and outranks a shiver.
///
/// Direction §10.1 also wanted it that way for a reason that is not budgetary: "The owner's own
/// chatter should be non-positional — it is your body, not a thing in the world."
///
/// <b>What is declined, on the record.</b> Positional chatter for REMOTE swimmers. It would carry
/// §5.4's per-player sensory asymmetry, which is a real loss, and it would cost the last five
/// slots. If it is ever wanted, it needs a cap and a steal policy against <c>VoiceSpeaker</c>
/// first, and that is a decision with proximity voice on the other side of it — not an
/// implementation detail this packet gets to take quietly.
/// </summary>
public partial class ChillChatter : Node
{
    private const int SampleRate = 48000;

    /// <summary>Above this the shiver becomes teeth. Two clips, not a blend: a shiver and a
    /// chatter are different events, and crossfading them would produce neither.</summary>
    private const float TeethIntensity = 0.55f;

    /// <summary>Gap between shots at the cue's onset and at chill 1.0. Matched to
    /// <c>ChillCueOverlay</c>'s rumble periods so the three channels quicken TOGETHER — direction
    /// §10.3 requires all three to ramp honestly and in step, because they are a fairness contract
    /// and a channel that led or lagged would be telling a different story about the same
    /// clock.</summary>
    private const float PeriodSlowSec = 2.4f;

    private const float PeriodFastSec = 0.62f;

    /// <summary>
    /// Volume at cue onset. Raised from -22 dB — 2026-08-08 playtest fallout, P2: the field
    /// report was "I did not realize... with or without audio," and the dispatch decision
    /// default is explicit that this packet trades subtlety for legibility. -22 dB paired with a
    /// once-per-2.4s shot was tuned to be a texture you might notice you had been hearing; it was
    /// too easy to not hear at all under footsteps, ambient bed and a teammate's voice. -16 is
    /// still 5 dB under the old channel's ceiling (<see cref="LoudDb"/> is unchanged), so the
    /// escalation from onset to crisis is intact — onset is simply no longer whisper-quiet.
    /// </summary>
    private const float QuietDb = -16f;

    private const float LoudDb = -11f;

    private AudioStreamPlayer _player = null!;
    private static AudioStreamWav? _shiver;
    private static AudioStreamWav? _teeth;

    private float _clock;
    private float _elapsed;

    /// <summary>Shots fired since construction. Instrumentation for the self-test — the only way
    /// a headless run can show this channel is wired to the clock rather than merely present.</summary>
    public int ShotsFired { get; private set; }

    /// <summary>The cue intensity read on the last frame.</summary>
    public float LastIntensity { get; private set; }

    /// <summary>The player node, exposed so the self-test can assert its TYPE. That it is an
    /// <see cref="AudioStreamPlayer"/> and not an <see cref="AudioStreamPlayer3D"/> is the whole
    /// budget claim above, and a claim that load-bearing should be checkable.</summary>
    public AudioStreamPlayer Player => _player;

    public override void _Ready()
    {
        AudioBuses.EnsureLayout();
        _player = new AudioStreamPlayer
        {
            Name = "ChillChatter",
            // Sfx, not Ambient_Scenery. Your own body reacting to what you did is gameplay
            // feedback, and direction §10.1 is explicit that routing it to the scenery lane hides
            // it behind the player's ambient slider — which is the wrong slider.
            Bus = AudioBuses.Sfx,
            VolumeDb = QuietDb,
        };
        AddChild(_player);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!float.IsFinite(dt) || dt <= 0f)
            return;
        _elapsed += dt;

        float intensity = 0f;
        if (WaterService.Instance is { Synced: true } water)
        {
            int me = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
            intensity = ChillClock.CueIntensity(water.ChillOf(me));
        }
        LastIntensity = intensity;

        if (intensity <= 0.001f)
        {
            _clock = 0f;
            return;
        }

        _clock -= dt;
        if (_clock > 0f)
            return;
        _clock = PeriodFor(intensity, _elapsed);
        Fire(intensity);
    }

    /// <summary>
    /// Irregular and quickening, and deliberately the SAME shape <c>ChillCueOverlay.PulsePeriodFor</c>
    /// uses for the rumble — a slow second wave wobbles the interval so consecutive shots are
    /// never the same length apart. Deterministic rather than random for the reason that file
    /// states: a random interval reads as a fault, a shifting one reads as a body.
    /// </summary>
    public static float PeriodFor(float intensity, float elapsed)
    {
        float i = float.IsFinite(intensity) ? Mathf.Clamp(intensity, 0f, 1f) : 0f;
        // Elapsed is sanitised too, and it is not belt-and-braces: Mathf.Max(0.22f, NaN) returns
        // NaN rather than 0.22, so a single non-finite value here would sail straight through the
        // floor below and make the caller's `_clock -= dt; if (_clock > 0) return;` false forever
        // — a shot every frame, which is the worst failure this channel has available. Caught by
        // the hostile-input case in both test tiers, which is what those cases are for.
        float e = float.IsFinite(elapsed) ? elapsed : 0f;
        float basePeriod = Mathf.Lerp(PeriodSlowSec, PeriodFastSec, i);
        float wobble = 1f + 0.28f * Mathf.Sin(e * 0.83f) * (1f - i * 0.5f);
        float period = basePeriod * wobble;
        return float.IsFinite(period) ? Mathf.Max(0.22f, period) : PeriodSlowSec;
    }

    /// <summary>Volume at a given intensity. Quiet at onset: the cold arriving should be something
    /// you notice you have been hearing, not something that announces itself.</summary>
    public static float VolumeDbFor(float intensity) =>
        Mathf.Lerp(QuietDb, LoudDb, float.IsFinite(intensity) ? Mathf.Clamp(intensity, 0f, 1f) : 0f);

    private void Fire(float intensity)
    {
        if (!GodotObject.IsInstanceValid(_player))
            return;
        _player.Stream = intensity >= TeethIntensity ? Teeth() : Shiver();
        _player.VolumeDb = VolumeDbFor(intensity);
        // A little pitch variation per shot, same reason SparseSfxEmitter carries more of it than
        // SfxLab's default: this repeats every second or two and identical repeats are what give a
        // synthesised layer away.
        _player.PitchScale = 1f + (float)(new Random(ShotsFired * 7919).NextDouble() * 2 - 1) * 0.09f;
        _player.Play();
        ShotsFired++;
    }

    // --- The two clips ------------------------------------------------------------------------
    //
    // Deliberately NOT in WaterSfx. That class is the five splash one-shots and its contract is
    // "exactly one public entry point, taking only the event kind" — which is how direction §5's
    // no-night-variant rule is enforced structurally. Adding a sixth and seventh clip through a
    // different door would dissolve that guarantee for no gain; these are a different channel with
    // a different trigger and they belong to it.

    internal static AudioStreamWav Shiver() => _shiver ??= ToWav(RenderShiver(0.55f));
    internal static AudioStreamWav Teeth() => _teeth ??= ToWav(RenderTeeth(0.42f));

    /// <summary>A ragged in-breath: filtered noise that swells in and catches twice on the way,
    /// with a faint voiced edge under it. The catch is the tell — a smooth breath reads as wind.</summary>
    private static float[] RenderShiver(float seconds)
    {
        var rng = new Random(3301);
        float lp = 0f;
        return Render(seconds, (t, u) =>
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.16f * (white - lp);
            // Swell in over the first two thirds, then let go.
            float swell = u < 0.66f ? EaseOut(u / 0.66f) : Mathf.Pow(1f - (u - 0.66f) / 0.34f, 1.6f);
            // Two catches: brief dips where the breath stutters.
            float catch1 = u is > 0.28f and < 0.34f ? 0.35f : 1f;
            float catch2 = u is > 0.48f and < 0.53f ? 0.45f : 1f;
            float voiced = Mathf.Sin(Mathf.Tau * 128f * t) * 0.06f * swell;
            return (lp * 0.42f + voiced) * swell * catch1 * catch2;
        });
    }

    /// <summary>Teeth: a fast train of tiny hard clicks at an uneven spacing, with a thin voiced
    /// hum underneath. Uneven because jaw chatter is not a metronome, and the ear names a
    /// metronome instantly.</summary>
    private static float[] RenderTeeth(float seconds)
    {
        // Fixed, seeded onsets so the clip renders byte-identical every time and a headless test
        // can assert on it; per-shot variety comes from the pitch jitter at playback.
        float[] onsets =
        {
            0.010f, 0.052f, 0.088f, 0.131f, 0.166f, 0.212f,
            0.247f, 0.289f, 0.327f, 0.366f, 0.404f,
        };
        var rng = new Random(3302);
        float lp = 0f;
        return Render(seconds, (t, u) =>
        {
            float click = 0f;
            foreach (float onset in onsets)
            {
                float dt = t - onset;
                if (dt < 0f || dt > 0.016f)
                    continue;
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.75f * (white - lp);
                click += lp * Mathf.Pow(1f - dt / 0.016f, 2.5f) * 0.55f;
            }
            // A thin held hum under the clicks: the throat, not the teeth. Keeps the clip from
            // reading as a typewriter.
            float hum = Mathf.Sin(Mathf.Tau * 116f * t) * 0.05f * Mathf.Sin(Mathf.Pi * u);
            return click + hum;
        });
    }

    private static float[] Render(float seconds, Func<float, float, float> sample)
    {
        int count = (int)(SampleRate * seconds);
        var data = new float[count];
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(sample(i / (float)SampleRate, i / (float)count), -1f, 1f);
        return data;
    }

    private static float EaseOut(float u) => 1f - (1f - u) * (1f - u);

    private static AudioStreamWav ToWav(float[] samples)
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
        };
    }
}
