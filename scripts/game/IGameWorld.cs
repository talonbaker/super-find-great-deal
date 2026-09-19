using Godot;

namespace MpFoundation.Game;

/// <summary>
/// A world the Gameplay scene can host: it builds its own static environment and declares
/// where avatars spawn. Both the pastel <c>GameWorld</c> (mechanics testbed) and the
/// atmospheric World.LabEnvironment implement this, so swapping the game's setting is a
/// one-line scene edit (change the "World" node's script) with no code change in Gameplay.
/// </summary>
public interface IGameWorld
{
    /// <summary>World-space avatar spawn points; the host reads [i % count] per joining peer.</summary>
    Godot.Collections.Array<Vector3> SpawnPoints { get; }
}
