using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>The planted room</b> — a copy of the search room with §5b's six failure cases authored into
/// it, and the only world <c>--reach-selftest</c> runs in.
///
/// <para><b>Why a copy and not the search room itself.</b> The fixtures below are deliberate
/// defects: a crate inside a partition wall, a crate inside a shelf back, a crate on a ledge
/// nobody can reach. Authoring those into the level the game is played in would mean the game
/// ships with three props in illegal poses and every other suite's prop-count and
/// authored-vs-live assertions would have to know about them. It is also the packet's own
/// instruction — "a copy of the search room with authored fixtures".</para>
///
/// <para><b>Every part of it is authored in the scene file</b>
/// (<c>.claude/rules/godot-scenes.md</c>). This class builds nothing; it reads the one spawn
/// marker the room has so a bot could stand in it, and names the fixture nodes so the self-test
/// looks them up by a constant rather than by a string typed twice.</para>
///
/// <para><b>It is not reachable from an interactive launch.</b> <c>HostMenu</c> never passes
/// <c>--world</c>, and the id below is only ever set by <c>--reach-selftest</c>.</para>
/// </summary>
public partial class ReachPlantWorld : Node3D, IGameWorld
{
    /// <summary>The <c>--world</c> id. Set by <c>--reach-selftest</c>; never typed by a player.</summary>
    public const string WorldId = "reachplant";

    /// <summary>The scene this world is authored in.</summary>
    public const string ScenePath = "res://scenes/game/world/supermarket/ReachPlantRoom.tscn";

    /// <summary>Marker prefix, matching the supermarket rooms' convention.</summary>
    public const string SpawnPrefix = "PlantSpawn";

    /// <inheritdoc/>
    public Godot.Collections.Array<Vector3> SpawnPoints { get; } = new();

    public override void _Ready()
    {
        CollectMarkers(this);
        if (SpawnPoints.Count == 0)
            SpawnPoints.Add(new Vector3(0f, 1.1f, 0f));
        GD.Print($"[reachplant] planted room ready — {SpawnPoints.Count} spawn marker(s)");
    }

    private void CollectMarkers(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Marker3D marker && marker.Name.ToString().StartsWith(SpawnPrefix))
                SpawnPoints.Add(marker.GlobalPosition);
            CollectMarkers(child);
        }
    }
}
