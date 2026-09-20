using System;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Proximity-voice audibility parameters for one speaker, in the same units
/// VoiceSpeaker uses (Godot AudioStreamPlayer3D UnitSize / MaxDistance). This is the
/// megaphone's whole multiplayer contract: the sandbox never touches the voice
/// pipeline, it just publishes "this avatar's voice reaches this far" and the net
/// layer later copies it onto the holder's VoiceSpeaker.
/// </summary>
public readonly record struct VoiceRange(float UnitSize, float MaxDistanceMeters)
{
    /// <summary>Mirrors VoiceSpeaker's tuned defaults (UnitSize 6, gone past ~24 m).</summary>
    public static readonly VoiceRange Normal = new(6f, 24f);

    /// <summary>Megaphone: audible across most of a map, faint at the far edge.</summary>
    public static readonly VoiceRange Megaphone = new(20f, 80f);
}

/// <summary>
/// Anything that can change how far its holder's voice carries. The avatar scans its
/// held item for this and republishes the strongest range via VoiceRangeChanged.
/// </summary>
public interface IVoiceRangeSource
{
    VoiceRange Range { get; }
}

/// <summary>
/// Per-avatar published voice range. Owned by the avatar; the item side writes it on
/// pickup/drop and the (future) voice integration reads it / subscribes to it.
/// </summary>
public sealed class VoiceRangePublisher
{
    private VoiceRange _current = VoiceRange.Normal;

    public VoiceRange Current => _current;

    public event Action<VoiceRange>? VoiceRangeChanged;

    /// <summary>Recomputes range from the currently held item (null = hands empty).</summary>
    public void UpdateFromHeld(ICarryable? held)
    {
        VoiceRange next = held is IVoiceRangeSource source ? source.Range : VoiceRange.Normal;
        if (next == _current)
            return;
        _current = next;
        VoiceRangeChanged?.Invoke(next);
    }
}
