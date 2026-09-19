using System.Collections.Generic;
using Concentus;
using Godot;

namespace MpFoundation.Voice;

/// <summary>How a remote speaker's decoded voice reaches the local ear (Bible §5).</summary>
internal enum VoiceRoute
{
    /// <summary>Positional with distance falloff — the default social/horror voice.</summary>
    Proximity,

    /// <summary>The intercom: no distance falloff (everyone in the building hears it)
    /// and routed through the PA bus (lowpass + distortion + reverb) — "a voice through
    /// a speaker in a creepy building." §5 calls this the game's best juice-per-cost.</summary>
    Pa,
}

/// <summary>
/// Per-remote-speaker receive state: Opus decoder, sequence tracking, a small jitter
/// buffer, and the AudioStreamPlayer3D attached to that speaker's replicated avatar.
/// Sequence numbers drop late/duplicate frames (the voice channel is unreliable and
/// unordered); the prebuffer absorbs arrival jitter so the generator never underruns
/// into crackle. Playback uses Godot's built-in 3D attenuation on the avatar node —
/// no hand-rolled distance math. The route (proximity vs PA) is re-resolved by
/// VoiceManager every tick from replicated state and applied here.
/// </summary>
internal sealed class VoiceSpeaker
{
    // Attenuation tuning for the current sandbox scale (walk speed 5 m/s, spawn ring
    // radius 4 m): clearly audible in conversation range, gone past ~24 m. Promoted to
    // VoiceConfig.ProximityUnitSize/ProximityMaxDistance — don't reintroduce a local fork here.
    private const float GeneratorBufferSec = 0.2f;
    private const int MaxPendingFrames = 10; // bound memory if playback stalls

    private readonly int _peerId;
    private readonly bool _headless;
    private readonly IOpusDecoder _decoder;
    private readonly Queue<Vector2[]> _pending = new();
    // Recycled playback buffers (perf followups 2026-08-07). Submit used to allocate a fresh
    // Vector2[decoded] for EVERY decoded frame: 50/sec per speaker, 960 samples = 7.7 KB each,
    // so ~1.9 MB/sec of pure garbage on every client at six players — several times the
    // BlobShadow churn the audit ranked as the largest per-frame GC source, and it was missed
    // because it is per-PACKET rather than per-frame. Godot marshals the array into a
    // PackedVector2Array inside PushBuffer, so the buffer is free to be reused the moment
    // PushBuffer returns. Bounded by MaxPendingFrames + the one in flight, so the pool can
    // never grow past what the queue itself is already bounded to.
    private readonly Stack<Vector2[]> _free = new();
    private readonly short[] _pcm = new short[VoiceConfig.FrameSamples];
    private ushort _lastSeq;
    private bool _hasSeq;
    private AudioStreamPlayer3D? _node;
    private AudioStreamGeneratorPlayback? _playback;
    private bool _playing;
    private double _lastPacketSec;
    private VoiceRoute _route = VoiceRoute.Proximity;
    private bool _routeApplied;
    private readonly VoiceEnvelope _envelope = new();
    private float _pendingRms;

    /// <summary>Current route, for tests/telemetry.</summary>
    public VoiceRoute Route => _route;

    /// <summary>0..1 mouth openness for this speaker, derived on THIS client from the audio
    /// it already decodes for playback. Never replicated: deriving it locally is free,
    /// whereas sending it would put a per-frame float on the wire for every speaker in
    /// range. Read by the face layer via VoiceManager.GetVoiceEnvelope.</summary>
    public float Envelope => _envelope.Value;

    /// <summary>True when the most recent Submit dropped the frame because it failed
    /// to decode (as opposed to being stale/duplicate).</summary>
    public bool LastFrameUndecodable { get; private set; }

    public VoiceSpeaker(int peerId, bool headless)
    {
        _peerId = peerId;
        _headless = headless;
        _decoder = OpusCodecFactory.CreateDecoder(VoiceConfig.SampleRate, VoiceConfig.Channels);
    }

    /// <summary>Forgets the sequence high-water mark so the next packet re-anchors it. Called
    /// on unmute: the mark froze while muted and live frames could otherwise read as stale.</summary>
    public void ResetSequence() => _hasSeq = false;

    /// <summary>Advances the mouth envelope. Driven every frame rather than per packet,
    /// because it is the ABSENCE of packets that has to close the mouth: with no new frame
    /// the target is silence, and the release curve shuts the mouth smoothly. Ticked
    /// unconditionally by VoiceManager — including headless and while an avatar has not
    /// spawned — so the value never freezes mid-open.</summary>
    public void TickEnvelope(float dt)
    {
        _envelope.Update(_pendingRms, dt);
        _pendingRms = 0f; // consumed; absent a fresh frame the next target is silence
    }

    /// <summary>Validates, decodes, and buffers one received packet. Returns false if
    /// the frame was dropped (stale sequence or undecodable payload).</summary>
    public bool Submit(byte[] packet, double nowSec)
    {
        LastFrameUndecodable = false;

        ushort seq = (ushort)(packet[0] | (packet[1] << 8));
        // Wrap-safe lateness check: a signed 16-bit delta treats seq as a circle.
        if (_hasSeq && (short)(seq - _lastSeq) <= 0)
            return false;
        _lastSeq = seq;
        _hasSeq = true;

        int decoded;
        try
        {
            decoded = _decoder.Decode(packet.AsSpan(VoiceConfig.SeqBytes), _pcm, VoiceConfig.FrameSamples, false);
        }
        catch (System.Exception)
        {
            // Size-valid garbage sails through the (non-decoding) server relay, so the
            // decoder is the real parsing boundary: drop bad frames, never throw upward.
            LastFrameUndecodable = true;
            return false;
        }
        if (decoded <= 0)
        {
            LastFrameUndecodable = true;
            return false;
        }

        _lastPacketSec = nowSec;

        // The mouth signal is taken HERE, off the decoded frame, rather than from a bus
        // effect: every remote speaker shares one output bus (VoiceConfig.OutputBus, or the
        // PA bus), so a per-speaker analyser on a bus is not possible in this pipeline —
        // and this is cheaper anyway. Set before the headless early-out so headless tests
        // can observe it.
        _pendingRms = VoiceEnvelope.Rms(_pcm, decoded);

        if (_headless)
            return true; // mechanism proven; no audio device to feed in headless runs

        if (_pending.Count >= MaxPendingFrames)
            Recycle(_pending.Dequeue());
        Vector2[] frames = Rent(decoded);
        for (int i = 0; i < decoded; i++)
        {
            float v = _pcm[i] / 32768f;
            frames[i] = new Vector2(v, v);
        }
        _pending.Enqueue(frames);
        return true;
    }

    // A pooled buffer is only reusable at the EXACT decoded length, because PushBuffer pushes
    // the whole array and Tick sizes its CanPushBuffer check off Length. Opus here is a fixed
    // 20 ms frame, so `decoded` is FrameSamples on every real packet and the pool hits every
    // time; an odd-length frame simply allocates and is dropped on the floor rather than
    // poisoning the pool with a wrong-sized buffer.
    private Vector2[] Rent(int length)
    {
        if (_free.Count > 0 && _free.Peek().Length == length)
            return _free.Pop();
        return new Vector2[length];
    }

    private void Recycle(Vector2[] buffer)
    {
        // +1 for the frame currently in flight between Dequeue and PushBuffer.
        if (_free.Count < MaxPendingFrames + 1 && buffer.Length == VoiceConfig.FrameSamples)
            _free.Push(buffer);
    }

    private void DrainPending()
    {
        while (_pending.Count > 0)
            Recycle(_pending.Dequeue());
    }

    /// <summary>Drives playback: places the emitter at <paramref name="emitPos"/> — the world point
    /// VoiceManager resolved for this speaker (the speaker's avatar head for ordinary proximity, or
    /// the handset/base node when the phone bridges the pair) — starts after the jitter prebuffer
    /// fills, pushes decoded frames, and stops after silence. The node lives under the stable
    /// <paramref name="stableParent"/> (the Players root) and is REPOSITIONED each tick rather than
    /// reparented, so switching to/from a bridge is a seamless move with no playback restart. A null
    /// <paramref name="emitPos"/> means there is no valid source right now (the speaker's avatar
    /// hasn't spawned, or despawned) — go quiet without tearing the node down.</summary>
    public void Tick(double nowSec, Node3D? stableParent, Vector3? emitPos)
    {
        if (_headless)
            return;

        if (emitPos is not Vector3 pos || stableParent == null || !GodotObject.IsInstanceValid(stableParent))
        {
            // No emit source this frame: stop cleanly and drop buffered audio so a later resume
            // re-fills the jitter prebuffer instead of playing stale frames at the wrong place.
            if (_playing)
                StopPlayback();
            DrainPending();
            return;
        }

        if (_node != null && !GodotObject.IsInstanceValid(_node))
        {
            _node = null;
            _playback = null;
            _playing = false;
        }
        if (_node == null)
            CreateNode(stableParent);
        _node!.GlobalPosition = pos;

        if (!_playing && _pending.Count >= VoiceConfig.JitterPrebufferFrames)
        {
            _node.Play();
            _playback = _node.GetStreamPlayback() as AudioStreamGeneratorPlayback;
            _playing = _playback != null;
        }

        if (!_playing || _playback == null)
            return;

        while (_pending.Count > 0 && _playback.CanPushBuffer(_pending.Peek().Length))
        {
            Vector2[] buffer = _pending.Dequeue();
            _playback.PushBuffer(buffer);
            // Safe the instant PushBuffer returns: it marshals into the engine's own
            // PackedVector2Array rather than retaining the managed array.
            Recycle(buffer);
        }

        if (_pending.Count == 0 && nowSec - _lastPacketSec > VoiceConfig.SpeakTimeoutSec)
            StopPlayback();
    }

    private void StopPlayback()
    {
        if (_node != null && GodotObject.IsInstanceValid(_node))
            _node.Stop();
        _playing = false;
        _playback = null;
    }

    private void CreateNode(Node3D stableParent)
    {
        VoiceManager.EnsureOutputBus();
        _node = new AudioStreamPlayer3D
        {
            Name = $"VoiceOutput_{_peerId}",
            Stream = new AudioStreamGenerator
            {
                MixRate = VoiceConfig.SampleRate,
                BufferLength = GeneratorBufferSec,
            },
            Bus = VoiceConfig.OutputBus,
            UnitSize = VoiceConfig.ProximityUnitSize,
            MaxDistance = VoiceConfig.ProximityMaxDistance,
        };
        // Parked on the stable Players root and positioned every tick via GlobalPosition — never a
        // child of the avatar, so a bridged voice can move to the handset/base without reparenting.
        stableParent.AddChild(_node);
        _routeApplied = false; // fresh node: (re)apply whatever route is current
    }

    /// <summary>Routes this speaker's playback (idempotent; safe to call every tick).
    /// PA = constant volume with no distance falloff, through the filtered PA bus, a
    /// touch quieter so a broadcast never blows out ears at point-blank. Proximity =
    /// the tuned positional defaults on the clean voice bus.</summary>
    public void SetRoute(VoiceRoute route)
    {
        if (route == _route && _routeApplied)
            return;
        _route = route;
        if (_node == null)
            return; // applied on attach
        if (route == VoiceRoute.Pa)
        {
            _node.Bus = VoiceManager.EnsurePaBusName();
            _node.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            _node.MaxDistance = 0; // no cutoff: the whole building hears the PA
            _node.VolumeDb = -6f;
        }
        else
        {
            _node.Bus = VoiceConfig.OutputBus;
            _node.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
            _node.UnitSize = VoiceConfig.ProximityUnitSize;
            _node.MaxDistance = VoiceConfig.ProximityMaxDistance;
            _node.VolumeDb = 0f;
        }
        _routeApplied = true;
    }

    public void Cleanup()
    {
        if (_node != null && GodotObject.IsInstanceValid(_node))
            _node.QueueFree();
        _node = null;
        _playback = null;
        _playing = false;
        // Not Recycle: this speaker is finished, so returning buffers to its own pool would
        // only keep them alive. Drop the pool with the queue.
        _pending.Clear();
        _free.Clear();
    }
}
