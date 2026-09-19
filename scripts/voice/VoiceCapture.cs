using Concentus;
using Concentus.Enums;
using Godot;

namespace MpFoundation.Voice;

/// <summary>
/// Client-side microphone capture and Opus encoding, gated by push-to-talk. Uses the
/// idiomatic Godot capture path: an AudioStreamMicrophone playing into a muted bus with
/// an AudioEffectCapture, whose frames are pulled, downmixed to mono, encoded as 20 ms
/// Opus frames, and handed to VoiceManager for transport. Nothing is captured or sent
/// while the push-to-talk key is up.
/// </summary>
public partial class VoiceCapture : Node
{
    private AudioStreamPlayer? _micPlayer;
    private AudioEffectCapture? _capture;
    private IOpusEncoder? _encoder;
    private readonly float[] _mono = new float[VoiceConfig.FrameSamples];
    private readonly short[] _pcm = new short[VoiceConfig.FrameSamples];
    private readonly byte[] _encodeBuf = new byte[VoiceConfig.MaxOpusBytes];
    private ushort _seq;
    private bool _transmitting;
    private bool _disabled;

    // Automatic gain control: a quiet OS-level mic (a very common real-world default,
    // not a per-user edge case) would otherwise stay quiet all the way through to
    // playback, since nothing upstream normalizes input level. This boosts a frame
    // toward a target peak when it's below that, smoothed frame-to-frame so gain
    // doesn't pump/flutter, and leaves already-strong input alone rather than also
    // attenuating it. Near-silence frames are left unboosted so the noise floor
    // between words doesn't get amplified into audible hiss.
    private const float AgcTargetPeak = 0.6f;
    private const float AgcMaxGain = 12f;
    private const float AgcSilenceFloor = 0.001f;
    private const float AgcSmoothing = 0.2f;
    private float _agcGain = 1f;

    private readonly VoiceEnvelope _envelope = new();
    private float _localRms;

    /// <summary>0..1 mouth openness for the LOCAL player. Remote clients derive this
    /// player's mouth from the voice they receive; this is the same signal for the local
    /// client's own view of itself (third-person, spectator, a mirror). Taken after AGC so
    /// a quiet microphone still moves the mouth by the same amount everyone else sees.</summary>
    public float Envelope => _envelope.Value;

    public override void _Process(double delta)
    {
        if (_disabled)
            return;
        bool held = Input.IsActionPressed(VoiceConfig.PttAction);
        if (held && !_transmitting)
            StartTransmit();
        else if (!held && _transmitting)
            StopTransmit();
        if (_transmitting)
            Drain();
        else
            _localRms = 0f; // push-to-talk released: the mouth closes on the release curve

        _envelope.Update(_localRms, (float)delta);
    }

    public override void _ExitTree()
    {
        if (_transmitting)
            StopTransmit();
    }

    private void StartTransmit()
    {
        EnsureSetup();
        if (_disabled || _micPlayer == null || _capture == null)
            return;
        _capture.ClearBuffer();
        _micPlayer.Play();
        _transmitting = true;
    }

    private void StopTransmit()
    {
        _micPlayer?.Stop();
        _capture?.ClearBuffer();
        _transmitting = false;
    }

    private void EnsureSetup()
    {
        if (_micPlayer != null || _disabled)
            return;

        // Opus only accepts 8/12/16/24/48 kHz. The project pins mix_rate to 48000; if a
        // future change drifts it, feeding the encoder would be invalid — disable voice
        // loudly rather than transmit garbage.
        if ((int)AudioServer.GetMixRate() != VoiceConfig.SampleRate)
        {
            GD.PushWarning($"[voice] mix rate {AudioServer.GetMixRate()} != {VoiceConfig.SampleRate}; voice capture disabled (audio/driver/mix_rate must be 48000)");
            _disabled = true;
            return;
        }

        int bus = AudioServer.GetBusIndex(VoiceConfig.CaptureBus);
        if (bus < 0)
        {
            bus = AudioServer.BusCount;
            AudioServer.AddBus(bus);
            AudioServer.SetBusName(bus, VoiceConfig.CaptureBus);
            AudioServer.SetBusMute(bus, true); // never loop the local mic to the speakers
            AudioServer.AddBusEffect(bus, new AudioEffectCapture());
        }
        _capture = FindCaptureEffect(bus);
        if (_capture == null)
        {
            GD.PushWarning("[voice] no AudioEffectCapture on capture bus; voice capture disabled");
            _disabled = true;
            return;
        }

        _encoder = OpusCodecFactory.CreateEncoder(VoiceConfig.SampleRate, VoiceConfig.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = VoiceConfig.Bitrate;

        _micPlayer = new AudioStreamPlayer
        {
            Stream = new AudioStreamMicrophone(),
            Bus = VoiceConfig.CaptureBus,
        };
        AddChild(_micPlayer);
    }

    private static AudioEffectCapture? FindCaptureEffect(int bus)
    {
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectCapture capture)
                return capture;
        }
        return null;
    }

    private void Drain()
    {
        if (_capture == null || _encoder == null)
            return;
        while (_capture.GetFramesAvailable() >= VoiceConfig.FrameSamples)
        {
            Vector2[] frames = _capture.GetBuffer(VoiceConfig.FrameSamples);
            for (int i = 0; i < frames.Length; i++)
                _mono[i] = (frames[i].X + frames[i].Y) * 0.5f;

            ApplyAgc(_mono);

            for (int i = 0; i < frames.Length; i++)
                _pcm[i] = (short)Mathf.Clamp((int)(_mono[i] * 32767f), short.MinValue, short.MaxValue);

            // Same PCM the encoder is about to send, so the local mouth and the mouth every
            // remote client derives from this audio are driven by the identical signal.
            _localRms = VoiceEnvelope.Rms(_pcm, frames.Length);

            int len;
            try
            {
                len = _encoder.Encode(_pcm, VoiceConfig.FrameSamples, _encodeBuf, _encodeBuf.Length);
            }
            catch (System.Exception)
            {
                continue; // one bad frame must never take the client down
            }
            if (len <= 0)
                continue;

            var packet = new byte[VoiceConfig.SeqBytes + len];
            packet[0] = (byte)(_seq & 0xFF);
            packet[1] = (byte)(_seq >> 8);
            _seq++;
            System.Array.Copy(_encodeBuf, 0, packet, VoiceConfig.SeqBytes, len);
            VoiceManager.Instance.SendVoicePacket(packet);
        }
    }

    private void ApplyAgc(float[] samples)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Mathf.Abs(samples[i]);
            if (abs > peak)
                peak = abs;
        }

        // Only adapt gain on frames with real signal; leave near-silence frames (gaps
        // between words) at the current gain so we don't chase the noise floor.
        if (peak > AgcSilenceFloor)
        {
            float desiredGain = Mathf.Clamp(AgcTargetPeak / peak, 1f, AgcMaxGain);
            _agcGain = Mathf.Lerp(_agcGain, desiredGain, AgcSmoothing);
        }

        for (int i = 0; i < samples.Length; i++)
            samples[i] = Mathf.Clamp(samples[i] * _agcGain, -1f, 1f);
    }
}
