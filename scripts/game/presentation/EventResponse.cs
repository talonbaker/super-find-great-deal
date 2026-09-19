using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Presentation;

/// <summary>One event → one concrete response (a sound and/or a particle puff), flat on
/// purpose — no nested sub-resources, so a profile stays a plain list in the inspector.
/// A response with Sound == Sfx.None and CustomSound == null is particle-only; PuffCount
/// == 0 is sound-only. Intensity (0..1, "how hard" — run-vs-walk, fall severity) scales
/// volume via IntensityVolumeBoostDb and gates the whole response via MinIntensity.</summary>
[GlobalClass]
public partial class EventResponse : Resource
{
    [Export] public ActorEvent Event { get; set; }

    [Export] public Sfx Sound { get; set; } = Sfx.None;
    /// <summary>Authored stream — wins over <see cref="Sound"/> when set. The escape
    /// hatch from the synthesized palette to real audio files, per creature, in data.</summary>
    [Export] public AudioStream? CustomSound { get; set; }
    [Export] public float VolumeDb { get; set; } = -6f;
    [Export] public float PitchJitter { get; set; } = 0.08f;
    /// <summary>dB added at intensity 1 (linear in intensity: final = VolumeDb + boost × i).</summary>
    [Export] public float IntensityVolumeBoostDb { get; set; }
    /// <summary>Response is skipped entirely below this intensity (e.g. dust only on hard falls).</summary>
    [Export] public float MinIntensity { get; set; }

    [ExportGroup("Puff")]
    [Export] public int PuffCount { get; set; }
    [Export] public Color PuffColor { get; set; } = new(0.93f, 0.87f, 0.74f, 0.85f); // JuiceFx.Dust
    [Export] public float PuffSize { get; set; } = 0.07f;
    [Export] public float PuffSpeed { get; set; } = 1.3f;
    [Export] public float PuffLifetime { get; set; } = 0.55f;
}
