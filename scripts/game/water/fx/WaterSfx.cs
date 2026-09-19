using System;
using Godot;

namespace Sail.Game.Water.Fx;

/// <summary>
/// The lake's five one-shots, synthesized once and cached — the same procedural discipline
/// <c>SfxLab</c> and <c>AmbientBed</c> already hold, and for the same three reasons: no asset
/// files, no licensing, and the whole palette tunable from one place.
///
/// <b>Why these are not <c>Sfx</c> enum members.</b> <c>SfxLab.PlayStream3D</c> takes an arbitrary
/// <see cref="AudioStream"/> — that overload exists precisely so a system can bring its own clips
/// and still play them through the one sanctioned pooled positional path. <c>AmbientBed</c>'s
/// event layer already uses it that way. Taking it means packet W4 adds five sounds to the game
/// while editing zero shared files, which is worth more than the enum entry would have been.
///
/// <b>There is exactly one set, and it is played identically day and night.</b> Direction §5 is a
/// hard call, not a preference: night is the same event with the release withheld, produced by
/// subtraction — no night sting, no added drone, no darker splash variant. There is therefore no
/// night parameter anywhere in this file, which is how that instruction survives the next edit.
/// The night difference lives entirely in the particle count and lifetime
/// (<see cref="WaterFxTuning"/>) and in the world the sound plays into, which is already thinner
/// because <c>AmbientBed.NightGainScale</c> made it so.
///
/// <b>What "the water darkens" in <see cref="Swallow"/> is not.</b> The go-under's clip closes its
/// own filter as it plays. That is the physical event — your ears going under — and it is
/// identical at noon. It is not the night register arriving in the sound, and it must never be
/// made into one.
/// </summary>
public static class WaterSfx
{
    private const int SampleRate = 48000; // matches SfxLab's and AmbientBed's pinned mix rate

    private static AudioStreamWav? _plunge;
    private static AudioStreamWav? _thrash;
    private static AudioStreamWav? _swallow;
    private static AudioStreamWav? _cough;
    private static AudioStreamWav? _drips;

    /// <summary>
    /// The clip for an event. Takes no night weight, deliberately — see the class doc. The
    /// self-test asserts that this method's signature has no phase input by proving the clip
    /// object returned for each kind is reference-identical across a whole simulated day.
    /// </summary>
    public static AudioStreamWav For(WaterEventKind kind) => kind switch
    {
        WaterEventKind.Entered => _plunge ??= ToWav(Plunge(0.60f)),
        WaterEventKind.Splash => _thrash ??= ToWav(Thrash(0.32f)),
        WaterEventKind.WentUnder => _swallow ??= ToWav(Swallow(1.15f)),
        WaterEventKind.Sputtered => _cough ??= ToWav(Cough(0.72f)),
        WaterEventKind.Exited => _drips ??= ToWav(Drips(0.62f)),
        // Unreachable through the enum, reachable through a corrupt wire byte. The quietest,
        // shortest clip is the right answer to "something happened and I do not know what".
        _ => _drips ??= ToWav(Drips(0.62f)),
    };

    // --- Recipes ---------------------------------------------------------------------------------
    //
    // Water is two things acoustically and both have to be there or it reads as static: a
    // broadband NOISE burst (the surface breaking) and a set of BUBBLE tones whose pitch RISES as
    // each bubble shrinks and collapses. The rising pitch is the whole tell — a bubble tone that
    // falls reads as a cartoon drip, and noise with no bubbles at all reads as a sandbag.

    /// <summary>A body going in: a low thump of displaced water under a broad noise burst, then
    /// a scatter of bubbles as it closes over. The biggest and wettest of the five.</summary>
    private static float[] Plunge(float seconds)
    {
        var rng = new Random(8801);
        float lp = 0f, hp = 0f;
        float[] onsets = { 0.10f, 0.17f, 0.26f, 0.38f, 0.52f };
        float[] pitches = { 380f, 620f, 480f, 900f, 700f };
        return Render(seconds, (t, u) =>
        {
            // The surface breaking: bright at the instant of impact, closing fast as the water
            // swallows the top end. A one-pole whose coefficient itself decays.
            float white = (float)(rng.NextDouble() * 2 - 1);
            float k = Mathf.Lerp(0.55f, 0.06f, Mathf.Min(u * 2.4f, 1f));
            lp += k * (white - lp);
            hp = lp - hp * 0.25f; // keep some air on the transient so it is a splash, not a thud
            float burst = hp * Envelope(u, attack: 0.004f, curve: 2.8f) * 0.62f;

            // Displaced water: a low body that sags, the mass of it.
            float body = Mathf.Sin(Mathf.Tau * Mathf.Lerp(95f, 62f, u) * t)
                         * Envelope(u, attack: 0.008f, curve: 3.4f) * 0.30f;

            return burst + body + Bubbles(t, onsets, pitches, 0.085f, 0.16f);
        });
    }

    /// <summary>One thrash of a limb at the surface. Short, bright, no body — the mass is already
    /// in the water, so there is nothing left to displace. W2 fires these every 0.45 s while a
    /// player is moving, so this clip is a texture that repeats and it is deliberately the least
    /// interesting of the five.</summary>
    private static float[] Thrash(float seconds)
    {
        var rng = new Random(8802);
        float lp = 0f, hp = 0f;
        float[] onsets = { 0.13f, 0.30f };
        float[] pitches = { 820f, 1150f };
        return Render(seconds, (t, u) =>
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            float k = Mathf.Lerp(0.62f, 0.16f, Mathf.Min(u * 2f, 1f));
            lp += k * (white - lp);
            hp = lp - hp * 0.35f;
            float burst = hp * Envelope(u, attack: 0.003f, curve: 3.2f) * 0.42f;
            return burst + Bubbles(t, onsets, pitches, 0.055f, 0.10f);
        });
    }

    /// <summary>
    /// The swallow. A noise burst that does not brighten and then a world closing over the top —
    /// the clip's own filter shuts across its length, and a long train of rising bubbles runs out
    /// underneath it.
    ///
    /// This is the one clip whose character changes as it plays, and the reason is physical, not
    /// dramatic: it is the sound of your own ears going under. It is exactly the same at noon.
    /// Direction §8.2 forbids a startle doing a build's job and §5 forbids a night variant; a
    /// sound that dims because the head submerged is neither.
    /// </summary>
    private static float[] Swallow(float seconds)
    {
        var rng = new Random(8803);
        float lp = 0f;
        float[] onsets = { 0.16f, 0.24f, 0.33f, 0.41f, 0.50f, 0.60f, 0.71f, 0.84f };
        float[] pitches = { 300f, 460f, 380f, 640f, 520f, 780f, 610f, 900f };
        return Render(seconds, (t, u) =>
        {
            // The filter closes over the whole clip rather than over its first fifth: this is a
            // 1.5-second descent, not an impact.
            float white = (float)(rng.NextDouble() * 2 - 1);
            float k = Mathf.Lerp(0.30f, 0.015f, EaseOut(u));
            lp += k * (white - lp);
            float wash = lp * Mathf.Sin(Mathf.Pi * Mathf.Min(u * 1.25f, 1f)) * 0.55f;

            // A low tone sliding down under it — the pressure of going down, and the thing that
            // stops the clip reading as a fade-out rather than a descent.
            float sink = Mathf.Sin(Mathf.Tau * Mathf.Lerp(140f, 58f, EaseOut(u)) * t)
                         * (1f - u * 0.6f) * 0.16f;

            // Bubbles get quieter but do not stop; the last one lands under the hard cut.
            return wash + sink + Bubbles(t, onsets, pitches, 0.10f, 0.13f * (1f - u * 0.5f));
        });
    }

    /// <summary>A wet cough at the shore: two ragged voiced bursts, the second smaller, over a
    /// shed of water off a body. Direction §5 makes the day sputter a COMPLETE release, so this
    /// ends dry and clean with no tail — a cough that trails off is residue, and the day lake is
    /// a relief valve that is not allowed to leave any.</summary>
    private static float[] Cough(float seconds)
    {
        var rng = new Random(8804);
        float lp = 0f, breath = 0f;
        return Render(seconds, (t, u) =>
        {
            // Two bursts: the big one at the top, a smaller one at ~45%.
            float b1 = u < 0.34f ? Envelope(u / 0.34f, attack: 0.02f, curve: 2.4f) : 0f;
            float b2 = u is >= 0.42f and < 0.72f
                ? Envelope((u - 0.42f) / 0.30f, attack: 0.03f, curve: 2.6f) * 0.55f
                : 0f;
            float cough = b1 + b2;

            // Voiced part: a low buzzy vowel with strong odd harmonics — a chest, not a throat.
            float f0 = 165f * (1f - 0.22f * u);
            float phase = Mathf.Tau * f0 * t;
            float voice = Mathf.Sin(phase) + 0.6f * Mathf.Sin(2f * phase) + 0.35f * Mathf.Sin(3f * phase);

            // Rasp: broadband, kept in the same envelope so it never separates into a hiss.
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.22f * (white - lp);

            // The shed of water: a thin continuous trickle under the whole thing, out of phase
            // with the coughs so the two read as separate sources.
            breath += 0.09f * (white - breath);
            float shed = breath * Mathf.Sin(Mathf.Pi * Mathf.Clamp(u, 0f, 1f)) * 0.14f;

            return (voice * 0.30f + lp * 0.34f) * cough + shed;
        });
    }

    /// <summary>Coming out: a handful of droplets off the clothes at irregular spacing, and one
    /// soft shake through the middle. Irregular on purpose — four evenly-spaced plinks is a
    /// metronome, and the ear names a metronome instantly.</summary>
    private static float[] Drips(float seconds)
    {
        var rng = new Random(8805);
        float lp = 0f;
        float[] onsets = { 0.04f, 0.19f, 0.27f, 0.44f, 0.52f };
        float[] pitches = { 1450f, 1150f, 1750f, 1300f, 1900f };
        return Render(seconds, (t, u) =>
        {
            // The shake: a low broad rustle swelling and gone, water leaving fabric.
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.13f * (white - lp);
            float shake = lp
                          * (u is > 0.10f and < 0.55f
                              ? Mathf.Sin(Mathf.Pi * (u - 0.10f) / 0.45f)
                              : 0f)
                          * 0.22f;
            return shake + Bubbles(t, onsets, pitches, 0.045f, 0.20f);
        });
    }

    // --- Shared synthesis ---------------------------------------------------------------------

    /// <summary>
    /// A scatter of bubble tones. Each is a sine whose pitch RISES across its short life — the
    /// Minnaert behaviour of a collapsing bubble, and the single detail that separates
    /// synthesized water from synthesized static. Onsets are fixed rather than random so the clip
    /// is byte-identical every render and a headless test can assert on it; the per-shot variety
    /// comes from <c>SfxLab</c>'s pitch jitter at playback, which is where variety is cheap.
    /// </summary>
    /// <param name="t">Absolute time into the clip, seconds.</param>
    /// <param name="onsets">Fractions of the clip length at which each bubble starts.</param>
    /// <param name="pitches">Each bubble's starting frequency, Hz. Same length as onsets.</param>
    /// <param name="lengthSec">How long one bubble sounds.</param>
    /// <param name="amplitude">Peak amplitude of one bubble.</param>
    private static float Bubbles(float t, float[] onsets, float[] pitches,
        float lengthSec, float amplitude)
    {
        float sum = 0f;
        for (int i = 0; i < onsets.Length && i < pitches.Length; i++)
        {
            float dt = t - onsets[i];
            if (dt < 0f || dt > lengthSec)
                continue;
            float local = dt / lengthSec;
            // Pitch climbs by ~70% across the bubble's life as the cavity shrinks.
            float freq = pitches[i] * (1f + 0.7f * local);
            sum += Mathf.Sin(Mathf.Tau * freq * dt)
                   * Mathf.Pow(1f - local, 2.2f)
                   * amplitude;
        }
        return sum;
    }

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

    private static float Envelope(float u, float attack, float curve)
    {
        float a = u < attack ? u / attack : 1f;
        return a * Mathf.Pow(Mathf.Max(0f, 1f - u), curve);
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
        // Never looping. Every one of these is a one-shot rented from a pool that selects on
        // !Playing — a looping clip would hold its slot open forever and permanently shrink the
        // shared 14, which is the exact failure SparseSfxEmitter's class doc warns about.
        return new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
        };
    }
}
