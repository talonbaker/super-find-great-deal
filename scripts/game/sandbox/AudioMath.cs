using System;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// The loop-splice math, extracted so exactly one implementation of it exists.
///
/// It was written inside <see cref="MpFoundation.Game.World.AmbientBed"/> (PR #73) and lived
/// there alone while the bed was the only looping thing in the repo. Packet 1f adds a second
/// looping consumer — <see cref="SfxLab"/>'s looping-emitter mode — and a second copy of a
/// crossfade is the exact drift this repo keeps paying for, so the math moved here and both
/// callers now share it. <c>AmbientBed.RenderSeamlessLoop</c> is kept as a forwarder purely so
/// <c>AmbientBedSelfTest</c>'s existing numeric proof keeps pointing at the name it was written
/// against.
///
/// Deliberately free of Godot types (<c>System.Math</c>, not <c>Mathf</c>) so the pure xUnit
/// suite can reach it without an engine — the same reason <c>SparseSfxSchedule</c> is a separate
/// class from the node that uses it.
/// </summary>
public static class LoopSplice
{
    /// <summary>Renders <paramref name="seconds"/> + <paramref name="overlapSeconds"/> of audio,
    /// then equal-power crossfades the tail overlap into the head and returns exactly
    /// <paramref name="seconds"/> worth of samples.
    ///
    /// The wrap point is the whole reason this exists: a naive trim leaves sample 0 and sample
    /// N-1 at unrelated phases, which is an audible click every loop. After the splice, sample 0
    /// is the natural continuation of sample N-1, so the discontinuity at the wrap is bounded by
    /// the signal's own ordinary adjacent-sample delta. That bound is what
    /// <c>LoopSpliceEliminatesTheNaiveWrapDiscontinuity</c> asserts, against a deliberately
    /// phase-mismatched test tone.</summary>
    /// <param name="sample">(t, u) =&gt; amplitude, where t is seconds and u is normalized
    /// progress through the rendered buffer INCLUDING the overlap.</param>
    public static float[] Render(int sampleRate, float seconds, float overlapSeconds,
        Func<float, float, float> sample)
    {
        int total = (int)(sampleRate * (seconds + overlapSeconds));
        int overlap = (int)(sampleRate * overlapSeconds);
        int loopLen = total - overlap;
        if (loopLen <= 0)
            return Array.Empty<float>();

        var raw = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)sampleRate;
            float u = i / (float)total;
            raw[i] = sample(t, u);
        }

        var spliced = new float[loopLen];
        Array.Copy(raw, spliced, loopLen);

        for (int i = 0; i < overlap; i++)
        {
            float u = i / (float)overlap;
            float fadeIn = (float)Math.Sin(u * Math.PI * 0.5);
            float fadeOut = (float)Math.Cos(u * Math.PI * 0.5);
            spliced[i] = spliced[i] * fadeIn + raw[loopLen + i] * fadeOut;
        }

        for (int i = 0; i < loopLen; i++)
            spliced[i] = Math.Clamp(spliced[i], -1f, 1f);

        return spliced;
    }
}

/// <summary>
/// A Welch power-spectrum estimator, and the band-share measurement built on it.
///
/// <b>Why an FFT is in a game repo.</b> Packet 1f's central mix claim is that the night bed
/// leaves a hole in the 700–2200 Hz lane where a creature's voice would go.
/// That claim is exactly the kind of thing audio certifies falsely: it sounds like a taste
/// judgement, it is invisible in a code review, and no headless run can hear it. It is also,
/// unusually, a measurable property of a rendered buffer — so it is measured. Every other audio
/// claim in this packet is honest about being unverifiable without ears; this one is not, and the
/// difference is worth an FFT.
///
/// Pure C#, no Godot, no engine — reachable from the xUnit suite.
/// </summary>
public static class Spectrum
{
    /// <summary>Block size for the Welch estimate. 2048 at 48 kHz is a 23.4 Hz bin, fine enough
    /// to resolve a band edge at 700 Hz and coarse enough that a noise floor averages smoothly
    /// rather than jittering per block.</summary>
    public const int BlockSize = 2048;

    /// <summary>Average power spectral density across Hann-windowed 50%-overlapped blocks.
    /// Returns <see cref="BlockSize"/>/2 bins; bin i is centred at
    /// <c>i * sampleRate / BlockSize</c> Hz. Power, not amplitude — bands add linearly in power,
    /// which is the whole point of measuring them this way.</summary>
    public static double[] PowerSpectrum(float[] samples, int blockSize = BlockSize)
    {
        int bins = blockSize / 2;
        var acc = new double[bins];
        if (samples.Length < blockSize)
            return acc;

        // Hann, precomputed once: the window is what stops a block boundary's own discontinuity
        // smearing broadband energy across every bin and drowning the measurement in leakage.
        var window = new double[blockSize];
        for (int i = 0; i < blockSize; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / blockSize);

        int hop = blockSize / 2;
        int blocks = 0;
        var re = new double[blockSize];
        var im = new double[blockSize];

        for (int start = 0; start + blockSize <= samples.Length; start += hop)
        {
            for (int i = 0; i < blockSize; i++)
            {
                re[i] = samples[start + i] * window[i];
                im[i] = 0.0;
            }
            Fft(re, im);
            for (int i = 0; i < bins; i++)
                acc[i] += re[i] * re[i] + im[i] * im[i];
            blocks++;
        }

        if (blocks == 0)
            return acc;
        for (int i = 0; i < bins; i++)
            acc[i] /= blocks;
        return acc;
    }

    /// <summary>Summed power in [<paramref name="lowHz"/>, <paramref name="highHz"/>).</summary>
    public static double BandPower(double[] spectrum, int sampleRate, double lowHz, double highHz)
    {
        int blockSize = spectrum.Length * 2;
        double hzPerBin = sampleRate / (double)blockSize;
        double sum = 0;
        for (int i = 0; i < spectrum.Length; i++)
        {
            double f = i * hzPerBin;
            if (f >= lowHz && f < highHz)
                sum += spectrum[i];
        }
        return sum;
    }

    /// <summary>How far a band sits below the buffer's total power, in dB. Positive numbers mean
    /// the band is QUIETER than the whole — 20 dB means the band holds one hundredth of the
    /// signal's power. Returns <see cref="double.PositiveInfinity"/> for an empty band (a
    /// genuinely silent band is infinitely far down, and saying so is more useful than clamping
    /// to an arbitrary floor).
    ///
    /// A share rather than an absolute level, deliberately: the assertion this feeds has to
    /// survive somebody turning a layer's gain up or down, which changes every absolute figure
    /// and must not change whether the vocal band is clear.</summary>
    public static double BandRejectionDb(float[] samples, int sampleRate, double lowHz, double highHz)
    {
        double[] spec = PowerSpectrum(samples);
        double total = BandPower(spec, sampleRate, 0.0, sampleRate / 2.0);
        double band = BandPower(spec, sampleRate, lowHz, highHz);
        if (total <= 0.0)
            return double.PositiveInfinity; // silence: no band can be loud in it
        if (band <= 0.0)
            return double.PositiveInfinity;
        return -10.0 * Math.Log10(band / total);
    }

    /// <summary>In-place radix-2 Cooley-Tukey. <paramref name="re"/>.Length must be a power of
    /// two — every caller here passes <see cref="BlockSize"/>, and the guard is an exception
    /// rather than a silent wrong answer because a wrong-length FFT produces a plausible
    /// spectrum, which is the worst failure mode a measurement can have.</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        if (n == 0 || (n & (n - 1)) != 0)
            throw new ArgumentException($"FFT length {n} is not a power of two", nameof(re));

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1.0, curIm = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
