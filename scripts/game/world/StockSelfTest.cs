using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.World.Stock;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>STOCK-1's compliance test: the baked shop floor is the shop floor the arithmetic describes,
/// and every hole in it is somewhere a player can actually put the object.</b>
///
/// <para><c>godot --headless --path . -- --stock-selftest</c>; prints one machine-readable summary
/// line and exits 0/1. <c>tests/Run-StockTest.ps1</c> gates on the LINE, not the exit code, for
/// <see cref="SupermarketWorldSelfTest"/>'s reason: a process that dies before its own summary
/// exits non-zero for reasons that have nothing to do with the level.</para>
///
/// <para><b>Two questions, and only the second one needs an engine.</b></para>
///
/// <para><b>1. Is the bake the level?</b> <c>dotnet test</c>'s <c>StockBakeTests</c> already
/// proves the committed <c>.tscn</c> text is byte-for-byte what <see cref="ShelfStock"/> produces.
/// What it cannot see is whether Godot LOADS it as that — whether the MultiMesh a scene file
/// describes is the MultiMesh the renderer got. <c>.claude/rules/godot-scenes.md</c> is explicit
/// that this is a real failure mode and a silent one: <i>"MultiMesh buffer stride is not always
/// 12 ... parsing with the wrong stride yields plausible garbage, not a crash."</i> So this reads
/// <see cref="MultiMesh.InstanceCount"/> off the live resource and compares it to the number the
/// arithmetic produces from the bays it finds IN THE TREE.</para>
///
/// <para><b>2. Will a prop stay in a hole?</b> This is the packet's real subject and it cannot be
/// answered without a physics space. Every authored hole gets the shipped
/// <see cref="PlacementIntegrity.Check"/> — the same method the place RPC calls and the same one
/// REACH-1's rest audit calls — posed with a real near-miss prefab's real collider, at the pose a
/// hider would set it down at. <b>Every hole, not three.</b> A sample of three would say nothing
/// about the other hundred and sixty, and the failure this guards against is exactly the one that
/// only shows up in the hole nobody sampled: a prop that reports <c>StaticOverlap</c> is a prop
/// REACH-1 answers <c>InsideStatic</c> on, and a Confirm refused with
/// <c>NobodyCouldReachThat</c> ends the round in a shrug.</para>
///
/// <para><b>Offline, no server, no bots</b>, because none of that is needed to ask either
/// question and every peer of it is a way for the run to fail for an unrelated reason.</para>
/// </summary>
public sealed partial class StockSelfTest : Node3D
{
    public const string Prefix = "[stock-selftest]";
    public const string SummaryPrefix = Prefix + " SUMMARY";

    /// <summary>The three near-miss prefabs. A hole is tested with the object the round is
    /// actually about, not with a stand-in: <c>DealBox</c>'s 0.19 x 0.28 x 0.06 is the widest
    /// footprint a hider can be holding, and it is the one that decides how wide a hole has to
    /// be.</summary>
    private static readonly (StockMaterial Material, string Scene)[] DealPrefabs =
    {
        (StockMaterial.Can, "res://scenes/game/props/DealCan.tscn"),
        (StockMaterial.Box, "res://scenes/game/props/DealBox.tscn"),
        (StockMaterial.Produce, "res://scenes/game/props/DealProduce.tscn"),
    };

    /// <summary>SHELF-1's authored carryable population in the search room: 6 bins, 48 cereal
    /// boxes, 48 cans, 24 produce, ids 1004..1129 — 120 products plus the six bins. The
    /// PRODUCTS are what bulk is carved around; the bins stand on the floor.</summary>
    private const int ExpectedFacings = 120;

    private readonly List<string> _failures = new();
    private int _holesChecked;
    private int _posesChecked;
    private int _propBlocked;
    private float _worstPenetrationM;
    private string _worstWhere = "(none)";

    public override void _Ready()
    {
        GD.Print($"{Prefix} the baked stock, the holes in it, and whether a prop rests in one");

        var packed = GD.Load<PackedScene>(ScenePaths.Supermarket);
        if (packed == null)
        {
            Fail($"{ScenePaths.Supermarket} did not load at all.");
            Finish();
            return;
        }

        var world = packed.Instantiate<Node3D>();
        AddChild(world);

        Node3D? room = FindSearchRoom(world);
        if (room == null)
        {
            Fail("no SearchRoom in the supermarket seam scene; the level moved under this test.");
            Finish();
            return;
        }

        // The bays, read out of the LIVE TREE rather than out of the scene text. StockBakeTests
        // reads the text; this reads what Godot built from it. Two readers of one truth, which
        // is the only way a bake can be checked at both ends.
        List<RoomBay> bays = ReadBaysFromTree(room);
        GD.Print($"{Prefix}   bays found in the tree: {bays.Count} "
                 + $"({CountEndCaps(bays)} end-cap(s))");
        if (bays.Count == 0)
        {
            Fail("the search room instances no ShelfUnit/EndCap bays at all.");
            Finish();
            return;
        }

        List<StockRoom.RoomFacing> facings = ReadFacingsFromTree(room);
        GD.Print($"{Prefix}   authored carryable facings: {facings.Count}");
        Check(facings.Count == ExpectedFacings,
            $"the search room authors {facings.Count} carryable products under Stock/; SHELF-1 "
            + $"authored {ExpectedFacings} and their ids are 1000..1129. Bulk is carved around "
            + "these, so a count that has moved means the carve-out is being computed against a "
            + "different room from the one that ships.");

        RoomStock stock = StockRoom.Assemble(bays, facings);
        CheckTheBakeIsWhatLoaded(room, stock);
        CheckEveryBoardKeepsAHole(bays, stock);
        CheckEveryHoleAcceptsAProp(room, stock);
        PrintPlacementCandidates(room, stock);
        Finish();
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// SHELF-1's 120 authored carryable products, in ROOM-LOCAL space, read out of the live tree.
    ///
    /// <para>Found by NAME under the <c>Stock</c> container — <c>Can_</c>, <c>Box_</c>,
    /// <c>Produce_</c> — which is the same handle <c>AuthoredPropOrderTests</c> uses and, per
    /// <c>.claude/rules/godot-scenes.md</c>'s four payments, the one kind of identity that
    /// survives instancing on this build. The six <c>Bin_</c> nodes are deliberately not read:
    /// they stand on the floor, not on a board, and nothing carves around them.</para>
    /// </summary>
    private static List<StockRoom.RoomFacing> ReadFacingsFromTree(Node3D room)
    {
        var facings = new List<StockRoom.RoomFacing>();
        Node? stock = room.GetNodeOrNull("Stock");
        if (stock == null)
            return facings;
        Transform3D toLocal = room.GlobalTransform.AffineInverse();
        foreach (Node child in stock.GetChildren())
        {
            string name = child.Name.ToString();
            if (child is not Node3D node)
                continue;
            if (!name.StartsWith("Can_") && !name.StartsWith("Box_") && !name.StartsWith("Produce_"))
                continue;
            Vector3 p = (toLocal * node.GlobalTransform).Origin;
            facings.Add(new StockRoom.RoomFacing(name, p.X, p.Y, p.Z));
        }
        return facings;
    }

    private static Node3D? FindSearchRoom(Node from)
    {
        if (from.Name.ToString().Contains("SearchRoom") && from is Node3D n)
            return n;
        foreach (Node child in from.GetChildren())
        {
            Node3D? hit = FindSearchRoom(child);
            if (hit != null)
                return hit;
        }
        return null;
    }

    private static int CountEndCaps(List<RoomBay> bays)
    {
        int n = 0;
        foreach (RoomBay b in bays)
            if (b.IsEndCap)
                n++;
        return n;
    }

    /// <summary>Every direct child of the room whose own <see cref="Node.SceneFilePath"/> is the
    /// shelf or end-cap prefab, in room-local space. The name is the seed, which is why it is the
    /// name that is read and not an index — <c>.claude/rules/godot-scenes.md</c> has paid four
    /// times for identity carried on an <c>[Export]</c>, and a node name is native.</summary>
    private static List<RoomBay> ReadBaysFromTree(Node3D room)
    {
        var bays = new List<RoomBay>();
        foreach (Node child in room.GetChildren())
        {
            if (child is not Node3D node || string.IsNullOrEmpty(node.SceneFilePath))
                continue;
            bool isEndCap = node.SceneFilePath.EndsWith("EndCap.tscn");
            if (!isEndCap && !node.SceneFilePath.EndsWith("ShelfUnit.tscn"))
                continue;

            Transform3D local = room.GlobalTransform.AffineInverse() * node.GlobalTransform;
            // A pure yaw's basis row 0 is (cos, 0, sin) — the twelve floats in a .tscn are basis
            // ROWS, and this is the live equivalent of that read.
            float yaw = Mathf.RadToDeg(Mathf.Atan2(local.Basis.Z.X, local.Basis.X.X));
            bays.Add(new RoomBay(node.Name.ToString(), local.Origin.X, local.Origin.Z,
                Mathf.Round(yaw), isEndCap));
        }
        return bays;
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>The MultiMesh resources Godot actually loaded carry the number of instances the
    /// arithmetic says they should, and the collision body carries the number of runs.</summary>
    private void CheckTheBakeIsWhatLoaded(Node3D room, RoomStock stock)
    {
        Node? bulk = room.GetNodeOrNull("StockBulk");
        if (bulk == null)
        {
            Fail("the search room does not instance StockBulk.tscn; the shop floor is empty.");
            return;
        }

        foreach ((string node, StockMaterial material) in new[]
                 {
                     ("BulkCans", StockMaterial.Can),
                     ("BulkBoxes", StockMaterial.Box),
                     ("BulkProduce", StockMaterial.Produce),
                 })
        {
            int want = stock.CountOf(material);
            var mmi = bulk.GetNodeOrNull<MultiMeshInstance3D>(node);
            if (mmi?.Multimesh is not MultiMesh mm)
            {
                Fail($"StockBulk/{node} is missing or carries no MultiMesh.");
                continue;
            }
            Check(mm.InstanceCount == want,
                $"StockBulk/{node} loaded {mm.InstanceCount} instance(s); the layout maths says "
                + $"{want}. The bake and ShelfStock have diverged, or Godot read the buffer at a "
                + "different stride (.claude/rules/godot-scenes.md: a wrong stride yields "
                + "plausible garbage, not a crash).");
            Check(mm.TransformFormat == MultiMesh.TransformFormatEnum.Transform3D,
                $"StockBulk/{node} is not in TRANSFORM_3D format, so its buffer stride is not 12.");
            Check(!mm.UseColors && !mm.UseCustomData,
                $"StockBulk/{node} carries per-instance colour or custom data; its stride is no "
                + "longer 12 and everything that parses it is now wrong.");
            GD.Print($"{Prefix}   {node,-11} instances={mm.InstanceCount} (derived {want})");
        }

        Node? collision = bulk.GetNodeOrNull("Collision");
        int runs = collision?.GetChildCount() ?? -1;
        Check(runs == stock.Colliders.Count,
            $"StockBulk/Collision has {runs} shape(s); the layout maths says "
            + $"{stock.Colliders.Count}. Collision is sized to the FILLED volume per slot-run — a "
            + "count that does not match means some run is missing (a prop falls through stock) "
            + "or some run is extra (a hole has an invisible wall in it).");
        GD.Print($"{Prefix}   collision runs={runs} (derived {stock.Colliders.Count})");
    }

    /// <summary>One to three holes per board, and at least one of them all the way through, on
    /// every board of every bay. The full-depth one is SHELF-1's "root around" property: a bay you
    /// can see and shove a can THROUGH is what keeps this room somewhere to search.</summary>
    private void CheckEveryBoardKeepsAHole(List<RoomBay> bays, RoomStock stock)
    {
        foreach (RoomBay bay in bays)
            for (int board = 0; board < ShelfStock.BoardCentreY.Length; board++)
            {
                int holes = 0, through = 0;
                foreach (RoomGap g in stock.Gaps)
                {
                    if (g.Bay != bay.Name || g.Board != board)
                        continue;
                    holes++;
                    if (g.FullDepth)
                        through++;
                }
                Check(holes >= ShelfStock.MinGapsPerBoard,
                    $"{bay.Name} board {board} has {holes} hole(s); the rule is at least "
                    + $"{ShelfStock.MinGapsPerBoard}. A board with no hole is a board the hider "
                    + "cannot use and a wall the seeker cannot see through.");
                Check(through >= 1,
                    $"{bay.Name} board {board} has no hole that goes all the way through. "
                    + "SHELF-1's no-back-panel rule is what makes an aisle a place to root around "
                    + "in rather than a wall with tins on it, and a solid board undoes it.");
            }
        GD.Print($"{Prefix}   holes authored: {stock.Gaps.Count} across "
                 + $"{bays.Count * ShelfStock.BoardCentreY.Length} board(s)");
    }

    /// <summary>
    /// <b>The packet's subject.</b> For every authored hole, and for each of the three near-miss
    /// prefabs that fits in it, pose the real collider at the pose a hider would set the object
    /// down at and run the shipped <see cref="PlacementIntegrity.Check"/>.
    ///
    /// <para>A refusal here is not a cosmetic defect. Layer 1 would refuse the placement outright
    /// (<c>DoesNotFitThere</c>); layer 2's rest audit would report <c>StaticOverlap</c> and, if it
    /// could not depenetrate, latch <c>Stuck</c>; layer 3 then answers <c>InsideStatic</c> and the
    /// Confirm is refused <c>NobodyCouldReachThat</c>. One hole that is 2 cm too small is a
    /// hiding place the game silently will not let the round start on.</para>
    /// </summary>
    private void CheckEveryHoleAcceptsAProp(Node3D room, RoomStock stock)
    {
        var bodies = new Dictionary<StockMaterial, RigidBody3D>();
        foreach ((StockMaterial material, string path) in DealPrefabs)
        {
            var scene = GD.Load<PackedScene>(path);
            if (scene?.Instantiate() is not RigidBody3D body)
            {
                Fail($"{path} did not load as a RigidBody3D; the hole check cannot run.");
                continue;
            }
            body.Freeze = true;
            AddChild(body);
            bodies[material] = body;
        }
        if (bodies.Count != DealPrefabs.Length)
            return;

        foreach (RoomGap gap in stock.Gaps)
        {
            _holesChecked++;
            foreach ((StockMaterial material, RigidBody3D body) in bodies)
            {
                ProductSize size = ShelfStock.SizeOf(material);
                // It has to FIT before it can be asked to rest: a 0.16 m orange in a 0.27 m can
                // hole is fine, and in a narrower one it is not the hole's fault.
                if (size.PlanDiagonalM > gap.WidthM)
                    continue;

                // The pose a hider leaves it at: centred in the hole, standing on the board.
                var at = new Transform3D(Basis.Identity,
                    room.ToGlobal(new Vector3(gap.X, gap.BoardTopY + size.HalfHeightM, gap.Z)));

                PlacementIntegrity.Verdict v = PlacementIntegrity.Check(body, at);
                _posesChecked++;

                // A PROP in the way is not this test's failure, and the distinction is
                // PlacementIntegrity's own (see Verdict.BlockerIsProp): "a prop inside a WALL is
                // the defect S5b exists to prevent, a prop inside another PROP is usually two
                // crates the solver is about to push apart by itself." The neighbours of a hole
                // are SHELF-1's carryable facings -- objects the hider can pick up and move --
                // and REACH-1's layer 3 counts a movable prop as a hit that COUNTS rather than
                // as an obstruction. Only static geometry produces InsideStatic, which is the
                // sentence this test is here to make impossible. Counted, not waved through.
                if (v.Fault == PlacementIntegrity.PlacementFault.Overlapping && v.BlockerIsProp)
                {
                    _propBlocked++;
                    continue;
                }

                if (v.PenetrationM > _worstPenetrationM)
                {
                    _worstPenetrationM = v.PenetrationM;
                    _worstWhere = $"{gap.Bay} board {gap.Board} ({material})";
                }
                Check(v.Allowed,
                    $"{gap.Bay} board {gap.Board}: a {material} set down in a "
                    + $"{gap.WidthM:0.000} m hole at ({gap.X:0.00}, {gap.BoardTopY:0.00}, "
                    + $"{gap.Z:0.00}) is refused {v.Fault} (penetration {v.PenetrationM:0.000} m, "
                    + $"tolerance {PlacementIntegrity.DefaultOverlapToleranceM:0.000}). "
                    + $"{v.Detail} A hole a prop cannot rest in is one REACH-1 answers "
                    + "InsideStatic on and the round refuses the Confirm on.");
            }
        }

        foreach (RigidBody3D body in bodies.Values)
            body.QueueFree();

        GD.Print($"{Prefix}   holes checked={_holesChecked} poses={_posesChecked} "
                 + $"blockedByAnotherProp={_propBlocked} "
                 + $"worstPenetration={_worstPenetrationM:0.000} m at {_worstWhere} "
                 + $"(tolerance {PlacementIntegrity.DefaultOverlapToleranceM:0.000} m)");
    }

    /// <summary>
    /// One machine-readable <c>PLACE</c> line per material: a full-depth hole, in WORLD
    /// coordinates, that a live suite can seed a prop into.
    ///
    /// <para><b>Printed rather than typed into the suite</b>, for the reason
    /// <c>Get-AuthoredPropId</c> exists one file over: the holes are SEEDED, so their poses are a
    /// function of the bay names and of <see cref="ShelfStock"/>'s arithmetic, and a coordinate
    /// typed into a <c>.ps1</c> would be right until the first re-seed and wrong silently
    /// afterwards. <c>tests/Run-StockTest.ps1</c> reads these and seeds its phase-2 prop at one of
    /// them, so what the live server audits is a hole this run actually authored.</para>
    ///
    /// <para>Full-depth only: those are the holes that reach BOTH faces, so layer 3 has a
    /// sightline from either walkway and the suite is testing the audit rather than the
    /// geometry of a shallow hole it happened to pick.</para>
    /// </summary>
    private void PrintPlacementCandidates(Node3D room, RoomStock stock)
    {
        var seen = new HashSet<StockMaterial>();
        foreach (RoomGap gap in stock.Gaps)
        {
            if (!gap.FullDepth || !seen.Add(gap.Material))
                continue;
            float y = gap.BoardTopY + ShelfStock.SizeOf(gap.Material).HalfHeightM;
            Vector3 world = room.ToGlobal(new Vector3(gap.X, y, gap.Z));
            GD.Print($"{Prefix} PLACE material={gap.Material} bay={gap.Bay} board={gap.Board} "
                     + $"width={gap.WidthM:0.000} "
                     + $"at={world.X:0.000},{world.Y:0.000},{world.Z:0.000}");
        }
        Check(seen.Count > 0, "no full-depth hole anywhere in the room to offer a live suite.");
    }

    // -----------------------------------------------------------------------------------------

    private void Check(bool ok, string why)
    {
        if (!ok)
            Fail(why);
    }

    private void Fail(string why) => _failures.Add(why);

    private void Finish()
    {
        // A cap on the shouting: a systematic defect produces one failure per hole and a wall of
        // identical text helps nobody find the first one.
        int shown = 0;
        foreach (string why in _failures)
        {
            if (shown++ >= 12)
            {
                GD.PrintErr($"{Prefix} FAIL ... and {_failures.Count - 12} more");
                break;
            }
            GD.PrintErr($"{Prefix} FAIL {why}");
        }
        GD.Print($"{SummaryPrefix} holes={_holesChecked} poses={_posesChecked} "
                 + $"propBlocked={_propBlocked} "
                 + $"worstPenetrationM={_worstPenetrationM:0.000} "
                 + $"failures={_failures.Count} "
                 + $"result={(_failures.Count == 0 ? "PASS" : "FAIL")}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
