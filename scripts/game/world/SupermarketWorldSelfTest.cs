using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>The supermarket's compliance test — the reason "no geometry is built in code" is a fact
/// about this level rather than a promise in a packet.</b>
///
/// <para><c>godot --headless --path . -- --supermarket-selftest</c>; exits 0/1, and prints one
/// machine-readable summary line. <c>tests/Run-SupermarketWorldTest.ps1</c> gates on the LINE,
/// not on the exit code — a process that dies before it reaches its own summary exits non-zero
/// for reasons that have nothing to do with the level, and a runner that reads only the exit code
/// cannot tell that apart from a real red.</para>
///
/// <para><b>The load-bearing check is <see cref="CheckAuthored"/>.</b> The standing rule
/// (<c>.claude/rules/godot-scenes.md</c>, Talon 2026-08-27) is that every part of a level is
/// physically authored in the scene file so it can be opened and flown around in the editor. That
/// is not something a reviewer can verify from a diff, because a room that builds a shelf in
/// <c>_Ready</c> looks identical in the inspector to one that does not. So it is measured: each
/// section's <see cref="PackedScene"/> is walked as PACKED STATE — <see cref="SceneState"/>, no
/// instantiation, nothing runs — and its nodes counted; the same scene is then instantiated into
/// the live tree and counted again. <b>Equal counts mean nothing was constructed at runtime.</b>
/// SHELF-1 will add a hundred props to the search room; this is what keeps them authored.</para>
///
/// <para><b>The second check is the spawn contract.</b> Every marker named in
/// <see cref="SupermarketWorld.Contract"/> must exist, and the count must match exactly — not "at
/// least", because a duplicate marker is as wrong as a missing one: <c>Gameplay.SpawnPlayer</c>
/// indexes the array by join order, and every peer builds its own copy of this world, so two
/// peers that disagreed about how many markers a room has would disagree about where a player is.
/// A renamed marker is the failure this catches, and renaming one is how the check was proved
/// able to fail.</para>
///
/// <para><b>The third is separation.</b> The rooms must be at least
/// <see cref="MinRoomSeparationM"/> apart, because the voice proximity cutoff is 24 m and the
/// whole round depends on a hider not hearing a seeker through a wall. Asserted against the
/// measured marker positions rather than against the seam file's transforms, so moving a room in
/// the editor and forgetting the consequence is a red rather than a quiet loss of the game.</para>
/// </summary>
public sealed partial class SupermarketWorldSelfTest : Node3D
{
    /// <summary>The prefix every line this test prints carries, so a runner can grep for its
    /// output without matching the engine's.</summary>
    public const string Prefix = "[supermarket-selftest]";

    /// <summary>The one machine-readable line. The runner gates on this rather than on the exit
    /// code — see the class doc.</summary>
    public const string SummaryPrefix = Prefix + " SUMMARY";

    /// <summary>Minimum distance between any two rooms' spawn markers.
    ///
    /// <para><b>Where 30 m comes from:</b> proximity voice cuts off at 24 m, the rooms are laid
    /// out 40 m apart, and the number here is the floor that keeps the design property true
    /// rather than the value the layout happens to have. Asserting 40 exactly would go red on a
    /// legitimate nudge; asserting 24 would pass a layout in which two players are audible across
    /// a wall by a centimetre.</para></summary>
    public const float MinRoomSeparationM = 30f;

    /// <summary>
    /// Every scene <see cref="CheckAuthored"/> counts. The three rooms, and — since CLOCK-1,
    /// 2026-09-19 — every prefab the rooms instance that is part of the LEVEL rather than of a
    /// gameplay system.
    ///
    /// <para><b>Why the prefab has to be in this list, measured rather than reasoned.</b>
    /// <see cref="CountNodes"/> stops at an instance boundary, because a room's packed state
    /// lists an instanced sub-scene as one node and does not describe its insides. That is
    /// correct for the room — and it means a prefab whose own script builds a child in
    /// <c>_Ready</c> is INVISIBLE to the room's count. Planted exactly that
    /// (<c>AddChild(new Node3D())</c> in <c>RoundClock._Ready</c>) and all three rooms stayed at
    /// packed == live: 27/27, 38/38, 44/44, suite green. The hole predates this lane — a
    /// <c>Carryable</c> was stopped at by type for the same structural reason — but CLOCK-1 is
    /// the first LEVEL prefab, so it is the first time the hole lets a level defect through.
    /// The fix is to check the prefab against ITS OWN scene file, which is exactly the rule
    /// (<c>.claude/rules/godot-scenes.md</c>) applied one level down.</para>
    ///
    /// <para><c>Crate.tscn</c> is deliberately NOT here. It is a gameplay prop, not level
    /// geometry, and its script builds its own outline shell and blob shadow at runtime by
    /// design — that is the documented exception <see cref="CountNodes"/> exists for, and
    /// listing it would assert the opposite of what CARRY-1 decided.</para>
    /// </summary>
    private static readonly string[] SectionScenes =
    {
        "res://scenes/game/world/supermarket/HoldingRoom.tscn",
        "res://scenes/game/world/supermarket/SearchRoom.tscn",
        "res://scenes/game/world/supermarket/TaskRoom.tscn",
        "res://scenes/game/world/supermarket/RoundClock.tscn",
    };

    private readonly List<string> _failures = new();
    private SupermarketWorld _world = null!;

    public override void _Ready()
    {
        GD.Print($"{Prefix} the spawn contract, the authored-vs-live node counts, and room separation");

        var packed = GD.Load<PackedScene>(ScenePaths.Supermarket);
        if (packed == null)
        {
            Fail($"{ScenePaths.Supermarket} did not load at all.");
            Finish();
            return;
        }

        var instanced = packed.Instantiate<Node3D>();
        if (instanced is not SupermarketWorld world)
        {
            Fail($"{ScenePaths.Supermarket}'s root is a {instanced.GetType().Name}, not a "
                 + "SupermarketWorld. Gameplay.BuildWorld casts it to IGameWorld, so a wrong root "
                 + "script is a crash on every launch, not a cosmetic slip.");
            instanced.QueueFree();
            Finish();
            return;
        }

        _world = world;
        AddChild(_world);

        CheckSpawnContract();
        CheckSeparation();
        CheckAuthored();
        Finish();
    }

    /// <summary>Every marker the contract names exists, exactly as many as it says.</summary>
    private void CheckSpawnContract()
    {
        foreach ((string room, string prefix, int want) in SupermarketWorld.Contract)
        {
            IReadOnlyList<Vector3> got = _world.SpawnPointsFor(room);
            Check(got.Count == want,
                $"room '{room}' has {got.Count} {prefix}_n marker(s); the contract says {want}. "
                + "A missing marker strands a player and a duplicate desyncs spawn order across "
                + "peers — both are this check's business.");
            // THE NUMBERING, not just the count. These markers are sorted by the number in the
            // name to fix spawn order across peers, so 0..n-1 exactly is the contract: a marker
            // renamed _3 -> _9 keeps the count and breaks the numbering, and a typo that drops
            // the underscore silently removes a spawn point. This check catches both.
            IReadOnlyList<int> indices = _world.MarkerIndicesFor(room);
            var wanted = new List<int>();
            for (int i = 0; i < want; i++)
                wanted.Add(i);
            Check(indices.Count == want && IsSequence(indices),
                $"room '{room}' numbers its markers [{string.Join(", ", indices)}]; the contract "
                + $"is [{string.Join(", ", wanted)}]. Spawn order is the number in the name, and "
                + "every peer builds its own copy of this world — a gap or a duplicate here is "
                + "two peers disagreeing about where a player stands.");
            GD.Print($"{Prefix}   {room,-10} {got.Count}/{want} markers "
                     + $"[{string.Join(",", indices)}], first at "
                     + (got.Count > 0 ? got[0].ToString() : "(none)"));
        }

        // Named, not just counted: a room whose markers were all renamed to another room's prefix
        // would satisfy a pure count on neither, but a room with ONE renamed marker and one
        // spare would. The default spawn array is the holding room's, and that is the one fact
        // Gameplay actually reads.
        Check(_world.SpawnPoints.Count == _world.SpawnPointsFor(SupermarketWorld.HoldingRoom).Count,
            $"SpawnPoints ({_world.SpawnPoints.Count}) is not the holding room's markers "
            + $"({_world.SpawnPointsFor(SupermarketWorld.HoldingRoom).Count}). Every session starts "
            + "in the holding room and every late joiner lands there.");
    }

    /// <summary>The rooms are far enough apart that neither voice nor light crosses.</summary>
    private void CheckSeparation()
    {
        (string, string)[] pairs =
        {
            (SupermarketWorld.HoldingRoom, SupermarketWorld.SearchRoom),
            (SupermarketWorld.HoldingRoom, SupermarketWorld.TaskRoom),
            (SupermarketWorld.SearchRoom, SupermarketWorld.TaskRoom),
        };
        foreach ((string a, string b) in pairs)
        {
            float nearest = float.MaxValue;
            foreach (Vector3 pa in _world.SpawnPointsFor(a))
                foreach (Vector3 pb in _world.SpawnPointsFor(b))
                    nearest = Mathf.Min(nearest, pa.DistanceTo(pb));
            Check(nearest >= MinRoomSeparationM,
                $"'{a}' and '{b}' come within {nearest:F1} m of each other; the floor is "
                + $"{MinRoomSeparationM:F0} m. Proximity voice carries 24 m, so closer than this "
                + "and a hider can hear the seeker through a wall.");
            GD.Print($"{Prefix}   {a} <-> {b}: nearest markers {nearest:F1} m apart");
        }
    }

    /// <summary>
    /// Packed node count == live node count, per section. See the class doc for why this is the
    /// load-bearing check.
    /// </summary>
    private void CheckAuthored()
    {
        foreach (string path in SectionScenes)
        {
            var packed = GD.Load<PackedScene>(path);
            if (packed == null)
            {
                Fail($"section scene {path} did not load.");
                continue;
            }

            SceneState state = packed.GetState();
            int packedCount = state.GetNodeCount();

            var live = packed.Instantiate<Node3D>();
            AddChild(live);
            int liveCount = CountNodes(live);
            live.QueueFree();

            Check(packedCount == liveCount,
                $"{path}: {packedCount} node(s) packed but {liveCount} live. Something is being "
                + "built in code. Every part of a level is authored in the scene file "
                + "(.claude/rules/godot-scenes.md) so it can be opened in the editor.");
            GD.Print($"{Prefix}   {path.Substring(path.LastIndexOf('/') + 1),-18} "
                     + $"packed={packedCount} live={liveCount}");
        }
    }

    /// <summary>True if the list is exactly 0, 1, 2, ... in order.</summary>
    private static bool IsSequence(IReadOnlyList<int> indices)
    {
        for (int i = 0; i < indices.Count; i++)
            if (indices[i] != i)
                return false;
        return true;
    }

    /// <summary>
    /// Live nodes in a section, with <b>one deliberate stop</b>: an instanced sub-scene counts as
    /// one node and its insides are not walked.
    ///
    /// <para><b>Why the exception exists</b> (CARRY-1, 2026-09-19). The rule this check enforces is
    /// "every part of a LEVEL is authored in its scene file". A prop is not a part of the level in
    /// that sense — it is an instanced prefab (<c>Crate.tscn</c>) whose own script has always
    /// built its own cosmetic children at runtime: the inverted-hull outline shell and the blob
    /// shadow, neither of which a level author places, neither of which appears in the packed
    /// state of the room that instances it. Counting into a prop therefore compares the room's
    /// authored node list against the room's nodes PLUS a prop's private furniture, and reports a
    /// level defect that is not one.</para>
    ///
    /// <para><b>Why the stop is now the INSTANCE BOUNDARY rather than the <c>Carryable</c> type</b>
    /// (CLOCK-1, 2026-09-19, and it is a generalisation, not a loosening). A room's
    /// <see cref="SceneState"/> lists an instanced sub-scene as ONE node and does not list that
    /// sub-scene's own children at all — that is what an instance is. So the packed side already
    /// counts every instanced prefab as one, and a live walk that descended into any of them was
    /// always going to over-count; <c>Carryable</c> was simply the only prefab in the tree when
    /// that was written. CLOCK-1 instances <c>RoundClock.tscn</c> into all three rooms, which is
    /// the second one, and naming a second type here would have started a list. The honest rule
    /// is the one the packed side is already using: <b>a node with its own
    /// <see cref="Node.SceneFilePath"/> is authored in THAT file, and is that file's business.</b>
    /// The <c>Carryable</c> case is subsumed by it exactly — <c>Crate.tscn</c>'s root carries its
    /// own scene path — so nothing it used to catch is let through.</para>
    ///
    /// <para><b>What it deliberately does NOT weaken.</b> The instance node itself still counts,
    /// so a room that SPAWNS a prop or a clock in <c>_Ready</c> still turns this red — which is
    /// the case the rule is actually about, and is what the planted fault that proved this check
    /// was re-run against. Everything that is scenery (walls, floors, lights, markers, bounds
    /// volumes, the pillar) is still walked to the leaf, because scenery has no scene path of its
    /// own. The section root is walked unconditionally: it has a scene path — its own — and
    /// stopping there would count every room as exactly one node and pass forever.</para>
    /// </summary>
    private static int CountNodes(Node from)
    {
        int n = 1;
        foreach (Node child in from.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.SceneFilePath))
            {
                n += 1;          // an instanced prefab; its insides are that prefab's business
                continue;
            }
            n += CountNodes(child);
        }
        return n;
    }

    private void Check(bool ok, string why)
    {
        if (!ok)
            Fail(why);
    }

    private void Fail(string why) => _failures.Add(why);

    private void Finish()
    {
        foreach (string why in _failures)
            GD.PrintErr($"{Prefix} FAIL {why}");
        // ONE machine-readable line, always printed, pass or fail — the runner gates on it, so a
        // run that dies before this point is "never reported" rather than "red", and those are
        // different problems with different fixes.
        GD.Print($"{SummaryPrefix} failures={_failures.Count} "
                 + $"result={(_failures.Count == 0 ? "PASS" : "FAIL")}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
