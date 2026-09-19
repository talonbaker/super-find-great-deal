using MpFoundation.Voice;
using Xunit;

public class VoiceEnvelopeTests
{
    private static short[] Tone(int n, double amp)
    {
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
            pcm[i] = (short)(amp * 32767 * System.Math.Sin(i * 0.05));
        return pcm;
    }

    [Fact]
    public void Rms_of_silence_is_zero()
        => Assert.Equal(0d, (double)VoiceEnvelope.Rms(new short[960], 960), 5);

    [Fact]
    public void Rms_rises_with_amplitude()
    {
        float quiet = VoiceEnvelope.Rms(Tone(960, 0.1), 960);
        float loud = VoiceEnvelope.Rms(Tone(960, 0.8), 960);
        Assert.True(loud > quiet * 4f, $"quiet={quiet} loud={loud}");
    }

    [Fact]
    public void Attack_is_faster_than_release()
    {
        var open = new VoiceEnvelope();
        for (int i = 0; i < 3; i++) open.Update(1f, 0.02f);   // 60 ms of loud
        float afterAttack = open.Value;

        var close = new VoiceEnvelope();
        for (int i = 0; i < 20; i++) close.Update(1f, 0.02f); // saturate
        float peak = close.Value;
        for (int i = 0; i < 3; i++) close.Update(0f, 0.02f);  // 60 ms of silence
        float droppedFraction = (peak - close.Value) / peak;

        Assert.True(afterAttack > 0.6f, $"attack too slow: {afterAttack}");
        Assert.True(droppedFraction < 0.5f, $"release too fast: dropped {droppedFraction:P0}");
    }

    [Fact]
    public void Silence_decays_to_zero_so_a_stopped_speaker_closes_their_mouth()
    {
        var e = new VoiceEnvelope();
        for (int i = 0; i < 20; i++) e.Update(1f, 0.02f);
        for (int i = 0; i < 100; i++) e.Update(0f, 0.02f);    // 2 s
        Assert.True(e.Value < 0.02f, $"did not close: {e.Value}");
    }

    [Fact]
    public void Output_is_clamped_to_unit_range()
    {
        var e = new VoiceEnvelope();
        for (int i = 0; i < 50; i++) e.Update(10f, 0.02f);
        Assert.InRange(e.Value, 0f, 1f);
    }

    [Fact]
    public void Noise_floor_is_gated_so_a_hissy_mic_does_not_mumble()
    {
        var e = new VoiceEnvelope();
        for (int i = 0; i < 50; i++) e.Update(0.004f, 0.02f); // below the gate
        Assert.True(e.Value < 0.02f, $"noise opened the mouth: {e.Value}");
    }
}
