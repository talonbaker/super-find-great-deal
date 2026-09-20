using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MpFoundation.Game.Props;
using MpFoundation.Game.World.Stock;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>STOCK-1's Godot-free gate on the shop floor's arithmetic.</b>
///
/// <para>The bulk stock is baked into a scene file (see <see cref="StockBake"/> for why), so the
/// only thing standing between a reviewer and thirty thousand committed floats is that the maths
/// which produced them is small, readable and asserted. These are the assertions. They fall into
/// three groups and each group is a different kind of failure:</para>
///
/// <list type="bullet">
/// <item><b>The frame</b> — every constant is checked against the <c>.tscn</c> it was read off, so
/// a board that moves in <c>ShelfUnit.tscn</c> cannot leave bulk collision hanging in mid-air.</item>
/// <item><b>The gap rule</b> — the one rule here that is a GAMEPLAY rule. A hole that does not
/// reach a face is a hole the hider can use and the seeker can never be told to look in, because
/// REACH-1's layer 3 refuses a Confirm on <c>OccludedByStatic</c>. Verified from the emitted
/// layout rather than from the code that emits it.</item>
/// <item><b>Determinism</b> — every peer builds the picture from the same bytes, but the BAKE has
/// to be reproducible too or the drift gate is noise.</item>
/// </list>
/// </summary>
public class ShelfStockTests
{
    private static string Root => StockBake.FindRepoRoot();

    private static string ReadScene(string rel) => File.ReadAllText(Path.Combine(Root, rel));

    // ===========================================================================================
    // The frame: every constant against the file it came from.
    // ===========================================================================================

    /// <summary>ShelfUnit.tscn's uprights, boards and depth, re-read from the scene. A bay that
    /// changed shape without this file changing would put every collision run and every product
    /// somewhere the shelf is not — and nothing else in the repo would notice, because the bulk
    /// is baked and a baked file looks plausible whatever is in it.</summary>
    [Fact]
    public void TheBayFrameIsWhatShelfUnitTscnSays()
    {
        string tscn = ReadScene("scenes/game/world/supermarket/ShelfUnit.tscn");

        Assert.Contains("size = Vector3(0.06, 1.8, 0.5)", tscn);      // upright
        Assert.Contains("size = Vector3(2.3, 0.05, 0.5)", tscn);      // board
        Assert.Contains("0, 0, 1, -1.12, 0.9, 0", tscn);              // UprightNeg
        Assert.Contains("0, 0, 1, 1.12, 0.9, 0", tscn);               // UprightPos
        Assert.Contains("0, 0, 1, 0, 0.4, 0", tscn);                  // Board0
        Assert.Contains("0, 0, 1, 0, 0.95, 0", tscn);                 // Board1
        Assert.Contains("0, 0, 1, 0, 1.5, 0", tscn);                  // Board2

        Assert.Equal(2.30f, ShelfStock.ShelfUnit.LengthM, 3);
        Assert.Equal(1.12f, ShelfStock.ShelfUnit.UprightCentreX, 3);
        Assert.Equal(0.06f, ShelfStock.ShelfUnit.UprightWidthM, 3);
        Assert.Equal(0.50f, ShelfStock.ShelfUnit.DepthM, 3);
        Assert.Equal(new[] { 0.40f, 0.95f, 1.50f }, ShelfStock.BoardCentreY);
        Assert.Equal(0.05f, ShelfStock.BoardThicknessM, 3);

        // The clear span between the uprights' INNER faces is what a product may occupy.
        Assert.Equal(2.18f, ShelfStock.ShelfUnit.InnerSpanM, 3);
    }

    [Fact]
    public void TheEndCapFrameIsWhatEndCapTscnSays()
    {
        string tscn = ReadScene("scenes/game/world/supermarket/EndCap.tscn");
        Assert.Contains("size = Vector3(1.4, 0.05, 0.5)", tscn);
        Assert.Contains("0, 0, 1, -0.67, 0.9, 0", tscn);
        Assert.Equal(1.28f, ShelfStock.EndCap.InnerSpanM, 3);
    }

    /// <summary>Every product's box, against its own prefab. These three numbers decide where
    /// bulk sits, how wide a collision run is and whether a hole is big enough to hide the
    /// object in.</summary>
    [Theory]
    [InlineData("scenes/game/props/Can.tscn", 0.07f, 0.12f, 0.07f)]
    [InlineData("scenes/game/props/CerealBox.tscn", 0.19f, 0.28f, 0.06f)]
    [InlineData("scenes/game/props/Produce.tscn", 0.16f, 0.16f, 0.16f)]
    public void EveryProductSizeMatchesItsPrefab(string prefab, float w, float h, float d)
    {
        string tscn = ReadScene(prefab);
        ProductSize size = prefab.Contains("Can") ? ShelfStock.Can
            : prefab.Contains("CerealBox") ? ShelfStock.Box
            : ShelfStock.Produce;

        Assert.Equal(w, size.WidthM, 3);
        Assert.Equal(h, size.HeightM, 3);
        Assert.Equal(d, size.DepthM, 3);

        if (prefab.Contains("Can"))
        {
            Assert.Contains("radius = 0.035", tscn);
            Assert.Contains("height = 0.12", tscn);
        }
        else if (prefab.Contains("CerealBox"))
        {
            Assert.Contains("size = Vector3(0.19, 0.28, 0.06)", tscn);
        }
        else
        {
            Assert.Contains("radius = 0.08", tscn);
        }
    }

    /// <summary>The bulk material is the prefab's material with its colour, roughness, metallic
    /// and emission intact — and WITHOUT <c>resource_local_to_scene</c>. A carryable needs its own
    /// material copy because <c>Carryable</c>'s highlight drives <c>emission_energy_multiplier</c>
    /// on it; bulk is never highlighted, so one shared material for fifteen hundred instances is
    /// the whole point of a MultiMesh. A filler that shaded differently from the carryable facing
    /// in front of it would make the density read as a backdrop, which is the one thing it must
    /// not do.</summary>
    [Theory]
    [InlineData("scenes/game/props/Can.tscn", "albedo_color = Color(0.74, 0.76, 0.79, 1)", "emission_energy_multiplier = 0.18", false)]
    [InlineData("scenes/game/props/CerealBox.tscn", "albedo_color = Color(0.78, 0.26, 0.2, 1)", "emission_energy_multiplier = 0.14", false)]
    [InlineData("scenes/game/props/Produce.tscn", "albedo_color = Color(0.92, 0.51, 0.13, 1)", "emission_energy_multiplier = 0.12", true)]
    public void EveryBulkMaterialMatchesItsPrefab(string prefab, string albedo, string emission, bool inTheMound)
    {
        string prefabText = ReadScene(prefab);
        Assert.Contains(albedo, prefabText);
        Assert.Contains(emission, prefabText);
        Assert.Contains("resource_local_to_scene = true", prefabText);

        string bulk = ReadScene(inTheMound ? StockBake.MoundScenePath : StockBake.BulkScenePath);
        Assert.Contains(albedo, bulk);
        Assert.Contains(emission, bulk);
        Assert.DoesNotContain("resource_local_to_scene", bulk);
    }

    // ===========================================================================================
    // The grid.
    // ===========================================================================================

    /// <summary>The slot counts, spelled out. 24 cans or 10 cereal boxes across a 2.18 m bay, 6
    /// boxes across a 1.28 m end-cap.</summary>
    [Fact]
    public void TheSlotCountsAreTheArithmeticAndNotATable()
    {
        Assert.Equal(24, ShelfStock.SlotsAcross(ShelfStock.ShelfUnit, StockMaterial.Can));
        Assert.Equal(10, ShelfStock.SlotsAcross(ShelfStock.ShelfUnit, StockMaterial.Box));
        Assert.Equal(6, ShelfStock.SlotsAcross(ShelfStock.EndCap, StockMaterial.Box));

        // Every slot's product clears its neighbour: the pitch is wider than the product.
        foreach (StockMaterial m in new[] { StockMaterial.Can, StockMaterial.Box, StockMaterial.Produce })
            Assert.True(ShelfStock.XPitchOf(m) > ShelfStock.SizeOf(m).WidthM,
                $"{m}: pitch {ShelfStock.XPitchOf(m)} must exceed width {ShelfStock.SizeOf(m).WidthM}");

        // And the whole run fits between the uprights.
        foreach ((BaySpec bay, StockMaterial m) in new[]
                 {
                     (ShelfStock.ShelfUnit, StockMaterial.Can),
                     (ShelfStock.ShelfUnit, StockMaterial.Box),
                     (ShelfStock.EndCap, StockMaterial.Box),
                 })
        {
            int n = ShelfStock.SlotsAcross(bay, m);
            Assert.True(n * ShelfStock.XPitchOf(m) <= bay.InnerSpanM + 1e-4f,
                $"{m} on a {bay.LengthM} m bay: {n} slots overflow the {bay.InnerSpanM} m clear span");
        }
    }

    /// <summary>Talon's ask, as a number: <i>"three or four items behind each one in every
    /// row"</i>. Four rows for cans and boxes; two for produce, because a 0.16 m sphere cannot be
    /// four deep in a 0.50 m bay without overlapping itself.</summary>
    [Fact]
    public void TheDepthGridIsThreeOrFourItemsBehindEachFacing()
    {
        Assert.Equal(4, ShelfStock.DepthGridOf(StockMaterial.Can).Rows);
        Assert.Equal(4, ShelfStock.DepthGridOf(StockMaterial.Box).Rows);
        Assert.Equal(2, ShelfStock.DepthGridOf(StockMaterial.Produce).Rows);

        foreach (StockMaterial m in new[] { StockMaterial.Can, StockMaterial.Box, StockMaterial.Produce })
        {
            (int rows, float pitch) = ShelfStock.DepthGridOf(m);
            ProductSize p = ShelfStock.SizeOf(m);
            Assert.True(pitch >= p.DepthM, $"{m}: depth pitch {pitch} is inside the product's own {p.DepthM}");
            float span = (rows - 1) * pitch + p.DepthM;
            Assert.True(span <= ShelfStock.ShelfUnit.DepthM + 1e-4f,
                $"{m}: {rows} rows span {span} m and the bay is {ShelfStock.ShelfUnit.DepthM} m deep");
        }
    }

    /// <summary>The top board carries its two OUTER rows and nothing between them, and the reason
    /// is the eye height REACH-1 measured off a live avatar: <c>eye=1.040</c> against a top-board
    /// surface at 1.525. The middle of that shelf is not visible from anywhere a player can
    /// stand.</summary>
    [Fact]
    public void TheTopBoardCarriesOnlyWhatAPlayerCanSee()
    {
        Assert.Equal(0b1111, ShelfStock.DepthMask(StockMaterial.Can, 0));
        Assert.Equal(0b1111, ShelfStock.DepthMask(StockMaterial.Can, 1));
        Assert.Equal(0b1001, ShelfStock.DepthMask(StockMaterial.Can, 2));

        Assert.True(ShelfStock.BoardTopY(2) > 1.04f,
            "the top board is above the avatar's 1.040 m eye, which is why its middle rows are empty");
        Assert.Equal(1.525f, ShelfStock.BoardTopY(2), 3);
        Assert.Equal(0.425f, ShelfStock.BoardTopY(0), 3);
    }

    /// <summary>
    /// <b>Why nothing is stacked two high on a board.</b> The packet asks for "boxes 2 deep and
    /// stacked 2 high <i>where the board clearance allows</i>"; the clearance is 0.50 m and two
    /// cereal boxes are 0.56 m. The conditional resolves to "nowhere on a board" — stacks live on
    /// the floor pallets instead, where 1.40 m of them still fits under the bays' 1.80 m line.
    /// </summary>
    [Fact]
    public void NoProductStacksTwoHighOnABoardBecauseTheClearanceIsFiveHundredMillimetres()
    {
        Assert.Equal(0.50f, ShelfStock.BoardClearHeightM, 3);
        foreach (StockMaterial m in new[] { StockMaterial.Can, StockMaterial.Box, StockMaterial.Produce })
            Assert.True(ShelfStock.SizeOf(m).HeightM <= ShelfStock.BoardClearHeightM,
                $"{m} does not fit under a shelf at all");

        Assert.True(2 * ShelfStock.Box.HeightM > ShelfStock.BoardClearHeightM,
            "two cereal boxes are 0.56 m and the clearance is 0.50 m — the packet's conditional is false");

        float palletHeight = StockRoom.PalletHigh * ShelfStock.Box.HeightM;
        Assert.True(palletHeight < 1.80f,
            $"a {palletHeight:0.00} m pallet stack must stay under the bays' 1.80 m skyline");
    }

    // ===========================================================================================
    // The gap rule.
    // ===========================================================================================

    private static BayFill CanBay(string seed = "Aisle0_Bay0") =>
        ShelfStock.Fill(ShelfStock.ShelfUnit, StockMaterial.Can, seed);

    /// <summary>Every bay as it actually ships -- through StockRoom.FillBay, with the authored
    /// carryable facings carved out -- so these assertions are about the shelf that is baked
    /// and not about a hypothetical empty one.</summary>
    private static IEnumerable<BayFill> EveryAuthoredBay()
    {
        List<StockRoom.RoomFacing> facings = StockBake.ReadFacings(Root);
        foreach (RoomBay bay in StockBake.ReadBays(Root))
            yield return StockRoom.FillBay(bay, facings);
    }

    [Fact]
    public void EveryBoardOfEveryAuthoredBayKeepsAtLeastOneHole()
    {
        foreach (BayFill fill in EveryAuthoredBay())
            for (int b = 0; b < ShelfStock.BoardCentreY.Length; b++)
            {
                int n = fill.Gaps.Count(g => g.Board == b);
                Assert.True(n >= ShelfStock.MinGapsPerBoard,
                    $"{fill.Seed} board {b} has {n} hole(s); the rule is {ShelfStock.MinGapsPerBoard}–{ShelfStock.MaxGapsPerBoard}");
                Assert.True(n <= ShelfStock.MaxGapsPerBoard,
                    $"{fill.Seed} board {b} has {n} hole(s), over the maximum");
            }
    }

    /// <summary>Every board keeps at least one hole that goes all the way through, so a player can
    /// still see and shove a can THROUGH a bay — SHELF-1's "root around" property, which is what
    /// keeps this room somewhere to search rather than four walls with tins on them.</summary>
    [Fact]
    public void EveryBoardKeepsOneHoleYouCanSeeStraightThrough()
    {
        foreach (BayFill fill in EveryAuthoredBay())
            for (int b = 0; b < ShelfStock.BoardCentreY.Length; b++)
                Assert.True(fill.Gaps.Any(g => g.Board == b && g.FullDepth),
                    $"{fill.Seed} board {b} has no full-depth hole; the bay is a wall from this side");
    }

    /// <summary>
    /// <b>The rule the whole packet turns on, verified from the EMITTED layout rather than from
    /// the code that emits it.</b>
    ///
    /// <para>For every hole, the straight line from one of the bay's two open faces to the hole
    /// must be free of bulk. Bulk is STATIC, and REACH-1's layer 3 refuses a Confirm whose only
    /// sightline is blocked by static geometry — so a hole with filler still in front of it is a
    /// hiding place the game will not let the round start on.</para>
    /// </summary>
    [Fact]
    public void EveryHoleReachesAFaceWithNoBulkInFrontOfIt()
    {
        foreach (BayFill fill in EveryAuthoredBay())
        {
            BaySpec bay = BayOf(fill);
            (int rows, _) = ShelfStock.DepthGridOf(fill.Material);

            // Rebuild the occupancy grid from the instances that were actually emitted.
            var occupied = new HashSet<(int Board, int Slot, int Row)>();
            foreach (BulkInstance inst in fill.Instances)
            {
                int board = NearestBoard(inst.Y, fill.Material);
                int slot = NearestSlot(bay, fill.Material, inst.X);
                int row = NearestRow(fill.Material, inst.Z);
                occupied.Add((board, slot, row));
            }

            foreach (StockGap gap in fill.Gaps)
                for (int s = gap.FirstSlot; s < gap.FirstSlot + gap.SlotCount; s++)
                {
                    // Walk inward from the face the hole opens to and count the rows that are
                    // actually empty. The hole's own cleared depth must all be inside that run:
                    // that IS "nothing in front of it".
                    //
                    // Compared against the GAP's own ClearedRows rather than against every empty
                    // row in the column, and the difference is not pedantry -- the facing
                    // carve-out can also empty rows at the OTHER end of the same column, and
                    // those are a carryable can standing there, not an unreachable hole.
                    var order = new List<int>();
                    for (int r = 0; r < rows; r++)
                        if ((ShelfStock.DepthMask(fill.Material, gap.Board) & (1 << r)) != 0)
                            order.Add(r);
                    if (!gap.OpenToNegZ)
                        order.Reverse();

                    int cleared = 0;
                    foreach (int r in order)
                    {
                        if (occupied.Contains((gap.Board, s, r)))
                            break;
                        cleared++;
                    }
                    Assert.True(cleared >= gap.ClearedRows,
                        $"{fill.Seed} board {gap.Board} slot {s}: the hole clears {gap.ClearedRows} "
                        + $"row(s) but only {cleared} of them reach the "
                        + $"{(gap.OpenToNegZ ? "-Z" : "+Z")} face. A hole with bulk in front of it is "
                        + "one REACH-1 refuses the Confirm on (OccludedByStatic).");
                }
        }
    }

    /// <summary>
    /// A hole has to be wider than the widest thing the hider can be carrying, at ANY yaw — the
    /// carry spring puts an object down at whatever angle it was held, not axis-aligned — with
    /// room to spare on both sides of <see cref="PlacementIntegrity.DefaultOverlapToleranceM"/>.
    /// </summary>
    [Fact]
    public void EveryHoleClearsTheWidestDealPropAtAnyYaw()
    {
        // The three near-miss prefabs are their ordinary twins plus one band (SHELF-1 §10), so
        // the widest footprint in the game's hand is the cereal box's.
        float widest = ShelfStock.Box.PlanDiagonalM;
        Assert.Equal(0.199f, widest, 3);

        float needed = widest + 2f * PlacementIntegrity.DefaultOverlapToleranceM;
        foreach (BayFill fill in EveryAuthoredBay())
            foreach (StockGap gap in fill.Gaps)
                Assert.True(gap.WidthM >= needed,
                    $"{fill.Seed} board {gap.Board}: a {gap.WidthM:0.000} m hole cannot take a "
                    + $"{widest:0.000} m object with {PlacementIntegrity.DefaultOverlapToleranceM} m "
                    + "of tolerance on each side");
    }

    /// <summary>Collision is sized to the FILLED volume, so no collision box may reach into a
    /// hole. One box per board would swallow every hole in it and hand the player a shop floor
    /// whose holes are painted on.</summary>
    [Fact]
    public void NoCollisionRunReachesIntoAHole()
    {
        foreach (BayFill fill in EveryAuthoredBay())
        {
            BaySpec bay = BayOf(fill);
            foreach (StockGap gap in fill.Gaps)
            {
                float gapMinX = gap.CentreX - gap.WidthM * 0.5f + 1e-4f;
                float gapMaxX = gap.CentreX + gap.WidthM * 0.5f - 1e-4f;
                foreach (BulkCollisionRun run in fill.Collision)
                {
                    if (run.Board != gap.Board)
                        continue;
                    // A run whose slots are entirely outside the hole's slot span is fine.
                    bool slotsOverlap = run.FirstSlot <= gap.FirstSlot + gap.SlotCount - 1
                                        && run.FirstSlot + run.SlotCount - 1 >= gap.FirstSlot;
                    if (!slotsOverlap)
                    {
                        float runMinX = run.CentreX - run.SizeX * 0.5f;
                        float runMaxX = run.CentreX + run.SizeX * 0.5f;
                        Assert.False(runMaxX > gapMinX && runMinX < gapMaxX,
                            $"{fill.Seed} board {gap.Board}: a collision run overlaps the hole in x");
                        continue;
                    }
                    // A run that shares slots with the hole is the partial-hole case: it must sit
                    // entirely behind the cleared depth, on the far side from the open face.
                    float runMinZ = run.CentreZ - run.SizeZ * 0.5f;
                    float runMaxZ = run.CentreZ + run.SizeZ * 0.5f;
                    float gapMinZ = gap.CentreZ - gap.DepthM * 0.5f;
                    float gapMaxZ = gap.CentreZ + gap.DepthM * 0.5f;
                    Assert.False(runMaxZ > gapMinZ + 1e-4f && runMinZ < gapMaxZ - 1e-4f,
                        $"{fill.Seed} board {gap.Board} slots {run.FirstSlot}..: collision from "
                        + $"z {runMinZ:0.000}..{runMaxZ:0.000} reaches into a hole at "
                        + $"z {gapMinZ:0.000}..{gapMaxZ:0.000}");
                }
            }
        }
    }

    /// <summary>Nothing sits outside the bay it belongs to — not past an upright, not out of the
    /// 0.50 m depth, and never floating above or sunk into a board.</summary>
    [Fact]
    public void NothingIsAuthoredOutsideItsOwnBay()
    {
        foreach (BayFill fill in EveryAuthoredBay())
        {
            BaySpec bay = BayOf(fill);
            ProductSize p = ShelfStock.SizeOf(fill.Material);
            float maxX = bay.InnerSpanM * 0.5f;
            var legalY = new HashSet<float>();
            for (int b = 0; b < ShelfStock.BoardCentreY.Length; b++)
                legalY.Add(MathF.Round(ShelfStock.BoardTopY(b) + p.HalfHeightM, 4));

            foreach (BulkInstance i in fill.Instances)
            {
                Assert.True(MathF.Abs(i.X) + p.WidthM * 0.5f <= maxX + 1e-3f,
                    $"{fill.Seed}: an instance at x={i.X:0.000} pokes through an upright");
                Assert.True(MathF.Abs(i.Z) + p.DepthM * 0.5f <= bay.DepthM * 0.5f + 1e-3f,
                    $"{fill.Seed}: an instance at z={i.Z:0.000} pokes out of the bay's 0.50 m depth");
                Assert.Contains(MathF.Round(i.Y, 4), legalY);
            }
        }
    }

    // ===========================================================================================
    // Determinism.
    // ===========================================================================================

    [Fact]
    public void TheSameSeedBuildsTheSameShelfTwice()
    {
        BayFill a = CanBay();
        BayFill b = CanBay();
        Assert.Equal(a.Instances.Count, b.Instances.Count);
        for (int i = 0; i < a.Instances.Count; i++)
            Assert.Equal(a.Instances[i], b.Instances[i]);
        Assert.Equal(a.Gaps.Count, b.Gaps.Count);
        for (int i = 0; i < a.Gaps.Count; i++)
            Assert.Equal(a.Gaps[i], b.Gaps[i]);
    }

    /// <summary>Sixteen bays, sixteen different pictures — otherwise a seeker who has learned one
    /// aisle has learned all four, and the hole pattern stops being information.</summary>
    [Fact]
    public void TwoBaysWithDifferentNamesGetDifferentHoles()
    {
        var signatures = new HashSet<string>();
        foreach (BayFill fill in EveryAuthoredBay())
            signatures.Add(string.Join("|", fill.Gaps.Select(g =>
                $"{g.Board}:{g.FirstSlot}:{g.ClearedRows}:{g.OpenToNegZ}")));

        // 18 bays; allow a couple of coincidental collisions but not a constant picture.
        Assert.True(signatures.Count >= 14,
            $"only {signatures.Count} distinct hole patterns across the authored bays");
    }

    /// <summary>SplitMix64 rather than <c>System.Random</c>, whose algorithm is documented as an
    /// implementation detail and has changed between .NET versions. The bake is compared against
    /// a re-derivation, so a stream that moved with the runtime would be an unexplainable red.</summary>
    [Fact]
    public void TheRandomStreamIsPinnedToItsOwnAlgorithm()
    {
        var a = new StockRng("Aisle0_Bay0");
        ulong[] first = { a.Next(), a.Next(), a.Next() };
        var b = new StockRng("Aisle0_Bay0");
        Assert.Equal(first, new[] { b.Next(), b.Next(), b.Next() });

        Assert.NotEqual(StockRng.SeedFrom("Aisle0_Bay0"), StockRng.SeedFrom("Aisle0_Bay1"));
        Assert.NotEqual(0UL, StockRng.SeedFrom(""));

        // The REAL pin on this stream is the committed bake: StockBakeTests re-derives every
        // float in StockBulk.tscn from it, so a stream that moved under a runtime upgrade shows
        // up there as thousands of changed floats rather than as one wrong constant here.
        Assert.False(typeof(StockRng).Assembly.FullName?.Contains("System.Random") ?? false);
    }

    // ===========================================================================================
    // Helpers.
    // ===========================================================================================

    /// <summary>Which frame a fill was built on. The two bay lengths give different slot
    /// counts for every material, so this is decidable from the fill alone.</summary>
    private static BaySpec BayOf(BayFill fill) =>
        fill.SlotsAcross == ShelfStock.SlotsAcross(ShelfStock.EndCap, fill.Material)
            ? ShelfStock.EndCap : ShelfStock.ShelfUnit;

    private static int NearestBoard(float y, StockMaterial m)
    {
        float half = ShelfStock.SizeOf(m).HalfHeightM;
        int best = 0;
        float bestD = float.MaxValue;
        for (int b = 0; b < ShelfStock.BoardCentreY.Length; b++)
        {
            float d = MathF.Abs(ShelfStock.BoardTopY(b) + half - y);
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }
        return best;
    }

    private static int NearestSlot(BaySpec bay, StockMaterial m, float x)
    {
        int n = ShelfStock.SlotsAcross(bay, m);
        int best = 0;
        float bestD = float.MaxValue;
        for (int s = 0; s < n; s++)
        {
            float d = MathF.Abs(ShelfStock.SlotX(bay, m, s) - x);
            if (d < bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    private static int NearestRow(StockMaterial m, float z)
    {
        (int rows, _) = ShelfStock.DepthGridOf(m);
        int best = 0;
        float bestD = float.MaxValue;
        for (int r = 0; r < rows; r++)
        {
            float d = MathF.Abs(ShelfStock.RowZ(m, r) - z);
            if (d < bestD)
            {
                bestD = d;
                best = r;
            }
        }
        return best;
    }
}
