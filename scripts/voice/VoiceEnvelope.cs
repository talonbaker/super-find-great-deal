namespace MpFoundation.Voice;

/// <summary>Turns a decoded voice frame into a 0..1 mouth-openness value.
///
/// Why RMS and not peak: RMS tracks perceived loudness; peak spikes on transients and
/// makes the mouth chatter. Why asymmetric smoothing: a fast attack opens the mouth on the
/// syllable, a slow release stops it snapping shut between phonemes (which reads as
/// robotic). Everything here is pure math on a float — no Godot types, no audio bus, no
/// FFT — so it runs identically for a remote speaker's decoded Opus and for the local
/// microphone, and is testable without an engine.</summary>
public sealed class VoiceEnvelope
{
    /// <summary>Below this RMS the frame is treated as silence, so mic hiss between words
    /// never mumbles the mouth.</summary>
    public const float SilenceFloor = 0.01f;

    /// <summary>RMS that maps to a fully open mouth. Voice reaching here has already been
    /// through VoiceCapture's AGC (target peak 0.6), so this is consistent across mics.</summary>
    public const float TalkReference = 0.18f;

    public const float AttackTau = 0.02f;   // ~20 ms — open promptly
    public const float ReleaseTau = 0.12f;  // ~120 ms — close smoothly

    public float Value { get; private set; }

    public static float Rms(System.ReadOnlySpan<short> pcm, int count)
    {
        if (count <= 0)
            return 0f;
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double s = pcm[i] / 32768.0;
            sum += s * s;
        }
        return (float)System.Math.Sqrt(sum / count);
    }

    public float Update(float rms, float dt)
    {
        float target = rms <= SilenceFloor
            ? 0f
            : System.Math.Clamp((rms - SilenceFloor) / (TalkReference - SilenceFloor), 0f, 1f);

        float tau = target > Value ? AttackTau : ReleaseTau;
        float alpha = 1f - (float)System.Math.Exp(-dt / tau);
        Value += (target - Value) * alpha;

        if (Value < 0.001f)
            Value = 0f; // settle exactly closed rather than asymptotically ajar
        return Value;
    }
}
