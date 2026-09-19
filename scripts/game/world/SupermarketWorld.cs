using System.Collections.Generic;
using Godot;
using MpFoundation.Game;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>The three rooms Super Find Great Deal is played in — and nothing else.</b>
///
/// <para>This class builds <b>no geometry</b>, and that is the point rather than an
/// implementation detail: every wall, floor, ceiling and light is authored in
/// <c>scenes/game/world/supermarket/</c> so the level can be opened and flown around in the
/// Godot editor (<c>.claude/rules/godot-scenes.md</c>). <see cref="SupermarketWorldSelfTest"/>
/// enforces it mechanically — it counts nodes in each section's <i>packed</i> state and again in
/// the live tree and fails on any difference — so a lane that quietly builds a shelf in
/// <c>_Ready</c> turns the suite red rather than passing review.</para>
///
/// <para><b>Why the rooms are 40 m apart along +X.</b> Voice proximity cuts off at 24 m
/// (<c>VoiceProximityGate</c>), so 40 m of separation makes "the hider cannot hear the seeker
/// breathing through the wall" a fact about the geometry rather than about a gate somebody might
/// switch off. It also keeps one room's lights out of another's, which is what lets a capture say
/// unambiguously which room it was taken in. The rooms are tinted differently for the same
/// reason.</para>
///
/// <para><b>Spawns.</b> <see cref="SpawnPoints"/> — the array <c>Gameplay.SpawnPlayer</c> reads
/// <c>[i % count]</c> from — is the HOLDING room's four markers, because the holding room is
/// where every session starts and where a late joiner belongs. The other rooms' markers are
/// reachable by name through <see cref="SpawnPointsFor"/>: ROUND-1 teleports players between
/// rooms on a phase change (see <see cref="RoomTeleport"/>), it does not respawn them, so those
/// markers are destinations rather than spawns and must not be mixed into the default array.</para>
///
/// <para>Marker positions are converted to WORLD space rather than read raw. Each room is an
/// instanced section scene with its own transform, and <c>Gameplay.SpawnPlayer</c> treats a spawn
/// point as a world position; the rooms' offsets living in the seam file is a fact about the
/// layout, not a guarantee.</para>
/// </summary>
public partial class SupermarketWorld : Node3D, IGameWorld
{
    /// <summary>The <c>--world</c> id. One constant, read by <c>Gameplay.BuildWorld</c>,
    /// <c>LaunchOptions.DefaultWorld</c>, <c>HudProfile.For</c> and
    /// <c>VoiceProximityGate.DefaultForWorld</c>, so the id and the world it selects cannot
    /// drift apart in four files.</summary>
    public const string WorldId = "supermarket";

    /// <summary>Room keys for <see cref="SpawnPointsFor"/>. They are the marker-name prefixes,
    /// lower-cased, so "which room" and "which markers" are one fact.</summary>
    public const string HoldingRoom = "holding";

    /// <inheritdoc cref="HoldingRoom"/>
    public const string SearchRoom = "search";

    /// <inheritdoc cref="HoldingRoom"/>
    public const string TaskRoom = "task";

    /// <summary>The seeker's entry vestibule behind the burst door (DOOR-1). A destination, not a
    /// room: it is inside the task room's section scene and has exactly one marker.</summary>
    public const string Vestibule = "vestibule";

    /// <summary>The node names of the three instanced section scenes, in the seam file. Named here
    /// rather than spelled at each lookup — a second spelling of one node is a silent miss, and
    /// this repo has paid for that once already.</summary>
    public const string HoldingNodeName = "HoldingRoom";

    /// <inheritdoc cref="HoldingNodeName"/>
    public const string SearchNodeName = "SearchRoom";

    /// <inheritdoc cref="HoldingNodeName"/>
    public const string TaskNodeName = "TaskRoom";

    /// <summary>Marker-name prefixes, per room key. A marker is <c>&lt;Prefix&gt;_&lt;n&gt;</c>,
    /// and the numbering is what fixes the order — see <see cref="CollectMarkers"/>.</summary>
    private static readonly Dictionary<string, string> MarkerPrefixes = new()
    {
        [HoldingRoom] = "HoldingSpawn",
        [SearchRoom] = "SearchSpawn",
        [TaskRoom] = "TaskSpawn",
        [Vestibule] = "Vestibule",
    };

    /// <summary>How many markers each room is REQUIRED to have. The self-test reads this rather
    /// than a second list, so adding a spawn point is one edit.</summary>
    public static readonly (string Room, string Prefix, int Count)[] Contract =
    {
        (HoldingRoom, "HoldingSpawn", 4),
        (SearchRoom, "SearchSpawn", 2),
        (TaskRoom, "TaskSpawn", 4),
        (Vestibule, "Vestibule", 1),
    };

    /// <inheritdoc/>
    public Godot.Collections.Array<Vector3> SpawnPoints { get; } = new();

    private readonly Dictionary<string, List<Vector3>> _byRoom = new();
    private readonly Dictionary<string, List<int>> _indexByRoom = new();

    public override void _Ready()
    {
        CollectMarkers();
        foreach (Vector3 p in SpawnPointsFor(HoldingRoom))
            SpawnPoints.Add(p);
        // Loud, because a world that spawned everyone at the origin looks exactly like a world
        // whose markers were renamed, and the symptom (four bodies inside each other under the
        // floor) reads as a netcode bug.
        GD.Print($"[supermarket] rooms ready — "
                 + string.Join(", ", System.Array.ConvertAll(Contract,
                     c => $"{c.Room}={SpawnPointsFor(c.Room).Count}/{c.Count}")));
    }

    /// <summary>
    /// The named markers of one room, in world space and in marker-number order. Empty for an
    /// unknown key rather than throwing: this is read by gameplay code on every peer, and a
    /// missing room is a level defect the self-test catches at build time, not a reason to take a
    /// live session down.
    /// </summary>
    public IReadOnlyList<Vector3> SpawnPointsFor(string room) =>
        _byRoom.TryGetValue(room, out List<Vector3>? list) ? list : System.Array.Empty<Vector3>();

    /// <summary>
    /// Walks each section for its <c>&lt;Prefix&gt;_&lt;n&gt;</c> markers.
    ///
    /// <para><b>Sorted by the number in the name, not by tree order.</b> Every peer builds its own
    /// copy of this world and <c>Gameplay.SpawnPlayer</c> indexes the array by join order, so two
    /// peers that disagreed about marker ORDER would disagree about where a given player is —
    /// which is not a spawn bug, it is a desync that only shows up with the second player.</para>
    /// </summary>
    private void CollectMarkers()
    {
        foreach ((string room, string prefix, int _) in Contract)
        {
            var found = new List<(int Index, Vector3 Pos)>();
            CollectMarkers(this, prefix, found);
            found.Sort((a, b) => a.Index.CompareTo(b.Index));
            var positions = new List<Vector3>(found.Count);
            var indices = new List<int>(found.Count);
            foreach ((int index, Vector3 pos) in found)
            {
                positions.Add(pos);
                indices.Add(index);
            }
            _byRoom[room] = positions;
            _indexByRoom[room] = indices;
        }
    }

    private static void CollectMarkers(Node from, string prefix, List<(int, Vector3)> into)
    {
        if (from is Marker3D marker && marker.Name.ToString().StartsWith(prefix + "_"))
        {
            string tail = marker.Name.ToString().Substring(prefix.Length + 1);
            into.Add((int.TryParse(tail, out int i) ? i : int.MaxValue, marker.GlobalPosition));
        }
        foreach (Node child in from.GetChildren())
            CollectMarkers(child, prefix, into);
    }

    /// <summary>The marker-name prefix a room key uses, for the self-test's error messages.
    /// Empty for an unknown key.</summary>
    public static string PrefixFor(string room) =>
        MarkerPrefixes.TryGetValue(room, out string? p) ? p : "";

    /// <summary>
    /// The marker INDICES found for a room, in the order they were sorted, for the self-test.
    ///
    /// <para><b>Why the indices and not just the count.</b> A count alone says nothing about the
    /// numbering, and the numbering is the whole contract: these markers are sorted by the number
    /// in the name so that every peer — each of which builds its own copy of this world — agrees
    /// on spawn ORDER, and <c>Gameplay.SpawnPlayer</c> indexes the array by join order. A marker
    /// renamed from <c>_3</c> to <c>_9</c> keeps the count at four and is invisible to a count
    /// check, while a typo that drops the underscore silently removes a spawn point. Requiring
    /// 0..n-1 exactly catches both, and it is the check that was planted to prove this test can
    /// fail at all.</para>
    /// </summary>
    public IReadOnlyList<int> MarkerIndicesFor(string room) =>
        _indexByRoom.TryGetValue(room, out List<int>? list) ? list : System.Array.Empty<int>();
}
