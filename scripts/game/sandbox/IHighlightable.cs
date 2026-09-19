using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>Anything InteractHighlighter can shimmer and put a key-chip on — today
/// Carryable and HouseDoor. Structural on purpose: Highlighted is a plain settable bool
/// (no events), matching Carryable's existing field exactly, so generalizing the
/// highlighter cost nothing on the Carryable side beyond declaring this interface.</summary>
public interface IHighlightable
{
    bool Highlighted { get; set; }
    Vector3 GlobalPosition { get; }
}
