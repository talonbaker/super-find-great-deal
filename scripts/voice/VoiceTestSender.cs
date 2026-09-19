using Concentus;
using Concentus.Enums;
using Godot;

namespace MpFoundation.Voice;

/// <summary>
/// Headless test hook: attached to a bot (via --voice-send / --voice-flood /
/// --voice-garbage / --voice-oversize) to transmit synthetic voice packets without any
/// microphone hardware, so the relay + security mechanism is provable in CI.
///   Tone     - deterministic 220 Hz sine encoded as real Opus at the nominal rate.
///   Flood    - the same stream at ~4x the legitimate rate; must trip the rate limit.
///   Garbage  - size-valid but non-Opus bytes; must relay (server never decodes) and
///              must not crash receiving clients (their decoder is hardened).
///   Oversize - packets past the size bound; the server must reject and log them.
/// </summary>
public partial class VoiceTestSender : Node
{
    public enum Mode { Tone, Flood, Garbage, Oversize }

    private const int FloodPacketsPerSecond = 200;
    private const int OversizePacketsPerSecond = 5;
    private const int GarbagePayloadBytes = 60;
    private const int MaxPacketsPerTick = 25; // don't burst absurdly after a frame hitch
    private const float ToneHz = 220.0f;
    private const float ToneAmplitude = 0.4f;

    private Mode _mode;
    private IOpusEncoder? _encoder;
    private readonly short[] _pcm = new short[VoiceConfig.FrameSamples];
    private readonly byte[] _encodeBuf = new byte[VoiceConfig.MaxOpusBytes];
    private double _accum;
    private double _phase;
    private ushort _seq;

    private int PacketsPerSecond => _mode switch
    {
        Mode.Flood => FloodPacketsPerSecond,
        Mode.Oversize => OversizePacketsPerSecond,
        _ => VoiceConfig.PacketsPerSecond,
    };

    public void Setup(Mode mode)
    {
        _mode = mode;
        if (mode is Mode.Tone or Mode.Flood)
        {
            _encoder = OpusCodecFactory.CreateEncoder(VoiceConfig.SampleRate, VoiceConfig.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = VoiceConfig.Bitrate;
        }
    }

    public override void _Ready() =>
        GD.Print($"[voice-test] sending mode={_mode} at {PacketsPerSecond} packets/s");

    public override void _Process(double delta)
    {
        _accum += delta * PacketsPerSecond;
        int count = (int)_accum;
        if (count <= 0)
            return;
        _accum -= count;
        if (count > MaxPacketsPerTick)
            count = MaxPacketsPerTick;
        for (int i = 0; i < count; i++)
        {
            byte[]? packet = BuildPacket();
            if (packet != null)
                VoiceManager.Instance.SendVoicePacket(packet);
        }
    }

    private byte[]? BuildPacket()
    {
        switch (_mode)
        {
            case Mode.Tone:
            case Mode.Flood:
            {
                for (int i = 0; i < _pcm.Length; i++)
                {
                    _phase += 2.0 * System.Math.PI * ToneHz / VoiceConfig.SampleRate;
                    _pcm[i] = (short)(ToneAmplitude * System.Math.Sin(_phase) * short.MaxValue);
                }
                int len = _encoder!.Encode(_pcm, VoiceConfig.FrameSamples, _encodeBuf, _encodeBuf.Length);
                if (len <= 0)
                    return null;
                var packet = new byte[VoiceConfig.SeqBytes + len];
                WriteSeq(packet);
                System.Array.Copy(_encodeBuf, 0, packet, VoiceConfig.SeqBytes, len);
                return packet;
            }
            case Mode.Garbage:
            {
                // Deterministic non-Opus bytes at a legitimate size: passes the server's
                // size bound (it never decodes) and lands in receivers' decoders.
                var packet = new byte[VoiceConfig.SeqBytes + GarbagePayloadBytes];
                WriteSeq(packet);
                for (int i = VoiceConfig.SeqBytes; i < packet.Length; i++)
                    packet[i] = (byte)((i * 37 + _seq * 11) & 0xFF);
                return packet;
            }
            case Mode.Oversize:
            default:
            {
                var packet = new byte[VoiceConfig.MaxPacketBytes + 128];
                WriteSeq(packet);
                return packet;
            }
        }
    }

    private void WriteSeq(byte[] packet)
    {
        packet[0] = (byte)(_seq & 0xFF);
        packet[1] = (byte)(_seq >> 8);
        _seq++;
    }
}
