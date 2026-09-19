namespace MpFoundation.Voice;

/// <summary>
/// Shared constants for the proximity voice pipeline. The wire format is deliberately
/// tiny: [seq:2 bytes little-endian][opus frame], sender id is attached server-side at
/// relay time so a client can never spoof another player's voice.
/// </summary>
public static class VoiceConfig
{
    /// <summary>Must match the project's audio/driver/mix_rate. Opus only accepts
    /// 8/12/16/24/48 kHz, so the project mix rate is pinned to 48000 (the real-time
    /// voice standard) instead of Godot's 44100 default.</summary>
    public const int SampleRate = 48000;

    public const int Channels = 1;
    public const int FrameMs = 20; // standard real-time voice frame size
    public const int FrameSamples = SampleRate / 1000 * FrameMs; // 960
    public const int Bitrate = 24000;
    public const int PacketsPerSecond = 1000 / FrameMs; // 50

    public const int SeqBytes = 2;

    /// <summary>Hard upper bound on a single Opus payload. A legitimate 20ms mono voice
    /// frame at 24 kbps is ~60 bytes; anything past this bound is hostile or broken.</summary>
    public const int MaxOpusBytes = 400;

    public const int MaxPacketBytes = SeqBytes + MaxOpusBytes;
    public const int MinPacketBytes = SeqBytes + 1;

    /// <summary>Per-client packet budget: nominal stream is 50/s; headroom covers frame
    /// timing jitter without letting a hostile client flood the relay.</summary>
    public const int MaxPacketsPerWindow = 75;
    public const double RateWindowSec = 1.0;

    /// <summary>Dedicated unreliable ENet channel so a burst of voice loss/backpressure
    /// can never stall the reliable channel position sync rides on.</summary>
    public const int TransferChannel = MpFoundation.Net.NetProfile.VoiceChannel;

    /// <summary>Frames buffered per speaker before playback starts (~60 ms). Absorbs
    /// network jitter; without it, unreliable delivery underruns the generator and
    /// crackles on any real network even though localhost sounds perfect.</summary>
    public const int JitterPrebufferFrames = 3;

    /// <summary>Playback stops this long after the last received frame (push-to-talk
    /// released or speaker left).</summary>
    public const double SpeakTimeoutSec = 0.35;

    public const string OutputBus = "Voice";
    public const string CaptureBus = "VoiceCapture";
    public const string PttAction = "voice_ptt";

    /// <summary>Proximity voice 3D attenuation — the tuned positional defaults VoiceSpeaker
    /// playback uses. Do not fork these.</summary>
    public const float ProximityUnitSize = 6.0f;
    public const float ProximityMaxDistance = 24.0f;
}
