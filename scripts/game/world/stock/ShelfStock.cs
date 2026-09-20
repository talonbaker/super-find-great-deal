using System;
using System.Collections.Generic;

namespace MpFoundation.Game.World.Stock;

/// <summary>Which product a run of bulk stock is made of. The three are SFX-1's three product
/// prefabs and nothing else: bulk reuses their meshes, their sizes and their colours, so a
/// filler can never read as a different object from the carryable facing in front of it.</summary>
public enum StockMaterial
{
    Can = 0,
    Box = 1,
    Produce = 2,
}

/// <summary>One product's box in metres, and the height it rides at above a board.
/// <para>Every number here is read off the prefab and is ASSERTED against it by
/// <c>ShelfStockTests.EveryProductSizeMatchesItsPrefab</c> — a can that grew 5 mm in
/// <c>Can.tscn</c> and not here would put bulk collision in the wrong place and nothing else in
/// the repo would notice.</para></summary>
public readonly record struct ProductSize(float WidthM, float HeightM, float DepthM)
{
    /// <summary>Resting centre height above the surface it stands on.</summary>
    public float HalfHeightM => HeightM * 0.5f;

    /// <summary>The widest this product can be in PLAN at any yaw — its footprint diagonal. This
    /// is the number a gap has to clear, because a hider's object arrives at whatever yaw the
    /// carry spring left it at, not axis-aligned.</summary>
    public float PlanDiagonalM => MathF.Sqrt(WidthM * WidthM + DepthM * DepthM);
}

/// <summary>One bay's frame, read off <c>ShelfUnit.tscn</c> or <c>EndCap.tscn</c>. Origin is the
/// bay's FLOOR, centred on the bay, +x along its length.</summary>
public readonly record struct BaySpec(
    float LengthM,
    float UprightCentreX,
    float UprightWidthM,
    float DepthM)
{
    /// <summary>Clear span between the two uprights' inner faces — the only x a product may
    /// occupy.</summary>
    public float InnerSpanM => (UprightCentreX - UprightWidthM * 0.5f) * 2f;
}

/// <summary>One bulk instance, in the bay's own local space. Yaw is the only rotation bulk ever
/// gets: a can lying on its side is a can somebody knocked over, and bulk never moves.</summary>
public readonly record struct BulkInstance(float X, float Y, float Z, float YawRad);

/// <summary>
/// An authored hole in the stock, and the thing the whole packet turns on.
///
/// <para><b>Cleared from a FACE inward, never from the middle.</b> <see cref="OpenToNegZ"/> says
/// which of the bay's two open faces this hole reaches. That is not decoration: REACH-1's layer 3
/// refuses a placement whose only sightline is blocked by STATIC geometry
/// (<c>ReachReason.OccludedByStatic</c>), and bulk is static. A hole with bulk still in front of
/// it would be a hole the hider can drop the object into and the seeker can never be told to look
/// in — the round ends in a shrug, which is the worst outcome this game has.</para>
/// </summary>
public readonly record struct StockGap(
    int Board,
    int FirstSlot,
    int SlotCount,
    bool OpenToNegZ,
    int ClearedRows,
    bool FullDepth,
    float CentreX,
    float CentreZ,
    float BoardTopY,
    float WidthM,
    float DepthM);

/// <summary>One static collision box covering a maximal run of equally-filled slots. Sized to the
/// FILLED volume, which is the packet's rule and the reason there is not simply one box per
/// board: a box per board would swallow every gap in it and turn the holes into walls that look
/// like holes.</summary>
public readonly record struct BulkCollisionRun(
    int Board,
    int FirstSlot,
    int SlotCount,
    float CentreX,
    float CentreY,
    float CentreZ,
    float SizeX,
    float SizeY,
    float SizeZ);

/// <summary>Everything one bay's fill produced.</summary>
public sealed class BayFill
{
    public required string Seed { get; init; }
    public required StockMaterial Material { get; init; }
    public required int SlotsAcross { get; init; }
    public required IReadOnlyList<BulkInstance> Instances { get; init; }
    public required IReadOnlyList<StockGap> Gaps { get; init; }
    public required IReadOnlyList<BulkCollisionRun> Collision { get; init; }
}

/// <summary>
/// <b>The shop-floor fill, as arithmetic — no engine, no scene, no <c>Node</c>.</b>
///
/// <para>STOCK-1 (2026-09-20). Talon's ask was "shelves extremely filled: three or four items
/// behind each one in every row; boxes stacked one behind the other; piles of fruit in bins and
/// crates. I need to see MORE." SHELF-1 measured what more CARRYABLES would cost (§7.1: 130
/// networked bodies is already 84 % of the 8 ms server bar on the worse run, and 130 loose at
/// once is a 230 kB/s stream), so the density here is <b>visual stock the server never simulates
/// and the wire never carries</b> — same meshes, same materials, same sizes as the carryable
/// facings in front of it, rendered as MultiMesh and collided as static boxes.</para>
///
/// <para><b>Why this file has no Godot in it.</b> The layout is BAKED into the scene files by a
/// generator rather than built in <c>_Ready</c>, because <c>.claude/rules/godot-scenes.md</c> is
/// unconditional — <i>every part of a level is authored in its scene file, never built in
/// code</i> — and <c>SupermarketWorldSelfTest.CheckAuthored</c> is the measurement behind it.
/// Keeping the maths engine-free is what makes the bake checkable: <c>dotnet test</c> re-derives
/// the whole layout in milliseconds and compares it to what is committed, so a baked scene can
/// never silently drift from the rule that produced it, and a reviewer reads eighty lines of
/// arithmetic instead of thirty thousand floats.</para>
///
/// <para><b>The one rule that is a gameplay rule and not a layout rule</b> is
/// <see cref="StockGap"/>'s: every hole reaches a face. Read that type's doc.</para>
/// </summary>
public static class ShelfStock
{
    // ---------------------------------------------------------------------------------------
    // The frame. Every constant here is read off ShelfUnit.tscn / EndCap.tscn and asserted
    // against those files by ShelfStockTests, so the two cannot drift.
    // ---------------------------------------------------------------------------------------

    /// <summary>Board CENTRE heights (ShelfUnit.tscn's Board0/1/2).</summary>
    public static readonly float[] BoardCentreY = { 0.40f, 0.95f, 1.50f };

    /// <summary>Board thickness; the standing surface is the centre plus half of this.</summary>
    public const float BoardThicknessM = 0.05f;

    /// <summary>Clear height above a board: the next board's underside, or — for the top board —
    /// the bay's own 1.80 m top, which is the line SHELF-1 chose so a bay HIDES the aisle behind
    /// it from a standing player. <b>Nothing bulk puts on a shelf may exceed this</b>, which is
    /// why boxes are not stacked two high on a board: two 0.28 m boxes are 0.56 m and the
    /// clearance is 0.50 m. The packet's "stacked 2 high where the board clearance allows"
    /// resolves to "nowhere on a board"; stacks live on the floor instead
    /// (<see cref="StockRoom"/>'s pallets), where 1.40 m of them fits under the same 1.80 m
    /// line.</summary>
    public const float BoardClearHeightM = 0.50f;

    /// <summary>The bay frames, as authored.</summary>
    public static readonly BaySpec ShelfUnit = new(LengthM: 2.30f, UprightCentreX: 1.12f,
        UprightWidthM: 0.06f, DepthM: 0.50f);

    public static readonly BaySpec EndCap = new(LengthM: 1.40f, UprightCentreX: 0.67f,
        UprightWidthM: 0.06f, DepthM: 0.50f);

    // ---------------------------------------------------------------------------------------
    // The products.
    // ---------------------------------------------------------------------------------------

    /// <summary>Can.tscn: a cylinder, r 0.035, h 0.12.</summary>
    public static readonly ProductSize Can = new(0.07f, 0.12f, 0.07f);

    /// <summary>CerealBox.tscn: 0.19 x 0.28 x 0.06.</summary>
    public static readonly ProductSize Box = new(0.19f, 0.28f, 0.06f);

    /// <summary>Produce.tscn: a sphere, r 0.08.</summary>
    public static readonly ProductSize Produce = new(0.16f, 0.16f, 0.16f);

    public static ProductSize SizeOf(StockMaterial m) => m switch
    {
        StockMaterial.Can => Can,
        StockMaterial.Box => Box,
        StockMaterial.Produce => Produce,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    // ---------------------------------------------------------------------------------------
    // The grid.
    // ---------------------------------------------------------------------------------------

    /// <summary>Slot pitch along the bay's length, per material. A shade wider than the product
    /// so neighbours do not interpenetrate: 0.09 for a 0.07 can, 0.21 for a 0.19 box, 0.17 for a
    /// 0.16 orange.</summary>
    public static float XPitchOf(StockMaterial m) => m switch
    {
        StockMaterial.Can => 0.09f,
        StockMaterial.Box => 0.21f,
        StockMaterial.Produce => 0.17f,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>Depth pitch, and how many rows deep the grid is. Four for cans and boxes —
    /// Talon's "three or four items behind each one in every row", literally — and two for
    /// produce, because a 0.16 m sphere cannot be four deep in 0.50 m without overlapping
    /// itself.</summary>
    public static (int Rows, float Pitch) DepthGridOf(StockMaterial m) => m switch
    {
        StockMaterial.Can => (4, 0.10f),
        StockMaterial.Box => (4, 0.10f),
        StockMaterial.Produce => (2, 0.20f),
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>
    /// Which depth rows each board actually carries, as a bitmask over the depth grid.
    ///
    /// <para><b>This is a sightline decision, not a budget one, and it is worth the paragraph.</b>
    /// The avatar's eye is at <b>1.040 m</b> (REACH-1 measured it: <c>live avatar r=0.145 h=1.200
    /// eye=1.040</c>). Board 0's surface is 0.425 and board 1's is 0.975, so both are at or below
    /// eye and are seen from above or straight on — every row of those reads. Board 2's surface
    /// is <b>1.525, half a metre above the player's eye</b>: from any standing position you see
    /// its front edge and the undersides, and the middle rows are geometrically invisible from
    /// both sides. Filling them would be instances nobody can ever see. So board 2 carries the
    /// two OUTER rows only — a full facing at each of the bay's two open faces — which is what
    /// the player actually sees and is 50 % of the top shelf's instance cost.</para>
    /// </summary>
    public static int DepthMask(StockMaterial m, int board)
    {
        (int rows, _) = DepthGridOf(m);
        int all = (1 << rows) - 1;
        if (board < BoardCentreY.Length - 1)
            return all;
        // Top board: the two outer rows (or the single outer row on a 2-row grid).
        return rows >= 4 ? (1 | (1 << (rows - 1))) : (1 << (rows - 1));
    }

    /// <summary>How many slots wide a hole is, per material. Wide enough that the widest
    /// near-miss object clears both sides of it by more than the placement tolerance — asserted,
    /// not assumed, by <c>ShelfStockTests.EveryGapClearsTheWidestDealProp</c>.</summary>
    public static int GapSlotsOf(StockMaterial m) => m == StockMaterial.Can ? 3 : 2;

    /// <summary>Holes per board: one to three, which is what a real shelf looks like at the end
    /// of a trading day.</summary>
    public const int MinGapsPerBoard = 1;
    public const int MaxGapsPerBoard = 3;

    /// <summary>The standing surface of a board — its centre plus half its thickness.</summary>
    public static float BoardTopY(int board) => BoardCentreY[board] + BoardThicknessM * 0.5f;

    /// <summary>Slot centres along the bay's length: <c>floor(innerSpan / pitch)</c> of them,
    /// centred on the bay so the two end gaps are equal.</summary>
    public static int SlotsAcross(BaySpec bay, StockMaterial m) =>
        Math.Max(1, (int)(bay.InnerSpanM / XPitchOf(m)));

    public static float SlotX(BaySpec bay, StockMaterial m, int slot)
    {
        float pitch = XPitchOf(m);
        int n = SlotsAcross(bay, m);
        return -(n - 1) * pitch * 0.5f + slot * pitch;
    }

    public static float RowZ(StockMaterial m, int row)
    {
        (int rows, float pitch) = DepthGridOf(m);
        return -(rows - 1) * pitch * 0.5f + row * pitch;
    }

    // ---------------------------------------------------------------------------------------
    // The fill.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Fill one bay, deterministically, from its node path.
    /// </summary>
    /// <param name="seed">The bay's node name (e.g. <c>Aisle0_Bay2</c>). Two bays with the same
    /// name get the same picture; that is why the caller passes the path and not an index.</param>
    public static BayFill Fill(BaySpec bay, StockMaterial material, string seed)
    {
        var rng = new StockRng(seed);
        int across = SlotsAcross(bay, material);
        (int rows, _) = DepthGridOf(material);
        ProductSize size = SizeOf(material);
        int gapSlots = Math.Min(GapSlotsOf(material), across);

        // filled[board][slot] = bitmask of depth rows still carrying stock.
        int boards = BoardCentreY.Length;
        var filled = new int[boards][];
        var gaps = new List<StockGap>();

        for (int b = 0; b < boards; b++)
        {
            int mask = DepthMask(material, b);
            filled[b] = new int[across];
            for (int s = 0; s < across; s++)
                filled[b][s] = mask;

            // The rows this board actually has, ascending. A hole is cleared from one END of
            // this list inward, which is what makes it reach a face.
            var boardRows = new List<int>();
            for (int r = 0; r < rows; r++)
                if ((mask & (1 << r)) != 0)
                    boardRows.Add(r);

            int want = rng.NextRange(MinGapsPerBoard, MaxGapsPerBoard);
            var taken = new List<(int First, int Last)>();

            for (int g = 0; g < want; g++)
            {
                int first = -1;
                // Try a few placements; a board that is already mostly hole just gets fewer.
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int candidate = rng.NextInt(Math.Max(1, across - gapSlots + 1));
                    bool clashes = false;
                    foreach ((int f, int l) in taken)
                    {
                        // One filled slot of separation, so two holes never merge into one and
                        // the collision runs between them stay distinct.
                        if (candidate <= l + 1 && candidate + gapSlots - 1 >= f - 1)
                        {
                            clashes = true;
                            break;
                        }
                    }
                    if (!clashes)
                    {
                        first = candidate;
                        break;
                    }
                }
                if (first < 0)
                    continue;

                taken.Add((first, first + gapSlots - 1));

                bool openToNegZ = rng.NextBool();
                // The FIRST hole on every board goes all the way through, so every board keeps a
                // sightline a player can see and shove a can through (SHELF-1's "root around"
                // property). The rest may be shallow — a sold-out facing with stock behind it,
                // which is what a real shelf looks like and is still a legal hiding place,
                // because it is cleared from a face.
                int cleared = g == 0
                    ? boardRows.Count
                    : rng.NextRange(1, boardRows.Count);
                bool full = cleared >= boardRows.Count;

                for (int s = first; s < first + gapSlots; s++)
                    for (int i = 0; i < cleared; i++)
                    {
                        int row = openToNegZ ? boardRows[i] : boardRows[boardRows.Count - 1 - i];
                        filled[b][s] &= ~(1 << row);
                    }

                float centreX = (SlotX(bay, material, first) + SlotX(bay, material, first + gapSlots - 1)) * 0.5f;
                float nearZ = RowZ(material, openToNegZ ? boardRows[0] : boardRows[boardRows.Count - 1]);
                float farZ = RowZ(material, openToNegZ
                    ? boardRows[cleared - 1]
                    : boardRows[boardRows.Count - cleared]);
                gaps.Add(new StockGap(
                    Board: b,
                    FirstSlot: first,
                    SlotCount: gapSlots,
                    OpenToNegZ: openToNegZ,
                    ClearedRows: cleared,
                    FullDepth: full,
                    CentreX: centreX,
                    CentreZ: (nearZ + farZ) * 0.5f,
                    BoardTopY: BoardTopY(b),
                    WidthM: gapSlots * XPitchOf(material),
                    DepthM: MathF.Abs(farZ - nearZ) + size.DepthM));
            }
        }

        // Instances, in a stable order (board, slot, row) so the bake is byte-identical run to
        // run. Jitter is drawn from the same stream AFTER the layout, so adding jitter can never
        // move a product into a different slot.
        var instances = new List<BulkInstance>();
        for (int b = 0; b < boards; b++)
        {
            float y = BoardTopY(b) + size.HalfHeightM;
            for (int s = 0; s < across; s++)
                for (int r = 0; r < rows; r++)
                {
                    if ((filled[b][s] & (1 << r)) == 0)
                        continue;
                    // A few degrees of yaw so a hundred identical cans do not read as a printed
                    // texture. No position jitter on a shelf: products on a shelf are pushed up
                    // against each other, and a jittered one would poke out of its collision run.
                    float yaw = rng.NextJitter(material == StockMaterial.Box ? 0.035f : 0.6f);
                    instances.Add(new BulkInstance(SlotX(bay, material, s), y, RowZ(material, r), yaw));
                }
        }

        return new BayFill
        {
            Seed = seed,
            Material = material,
            SlotsAcross = across,
            Instances = instances,
            Gaps = gaps,
            Collision = CollisionRuns(bay, material, filled),
        };
    }

    /// <summary>
    /// Merge equally-filled adjacent slots into one static box each.
    ///
    /// <para>Two slots merge only when their depth masks are IDENTICAL, so a shallow hole's
    /// slots get their own shorter box rather than being swallowed by the run beside them. The
    /// box is sized to the product volume in z and to the SLOT PITCH in x — so it ends exactly on
    /// the boundary between the last stocked slot and the first empty one, and the clear width of
    /// a hole is exactly <c>slots x pitch</c>.</para>
    /// </summary>
    private static List<BulkCollisionRun> CollisionRuns(BaySpec bay, StockMaterial material, int[][] filled)
    {
        var runs = new List<BulkCollisionRun>();
        ProductSize size = SizeOf(material);
        float pitch = XPitchOf(material);
        int across = filled[0].Length;

        for (int b = 0; b < filled.Length; b++)
        {
            int s = 0;
            while (s < across)
            {
                int mask = filled[b][s];
                if (mask == 0)
                {
                    s++;
                    continue;
                }
                int first = s;
                while (s + 1 < across && filled[b][s + 1] == mask)
                    s++;
                int count = s - first + 1;

                int lo = LowestSetBit(mask);
                int hi = HighestSetBit(mask);
                float zLo = RowZ(material, lo) - size.DepthM * 0.5f;
                float zHi = RowZ(material, hi) + size.DepthM * 0.5f;

                runs.Add(new BulkCollisionRun(
                    Board: b,
                    FirstSlot: first,
                    SlotCount: count,
                    CentreX: (SlotX(bay, material, first) + SlotX(bay, material, s)) * 0.5f,
                    CentreY: BoardTopY(b) + size.HeightM * 0.5f,
                    CentreZ: (zLo + zHi) * 0.5f,
                    SizeX: count * pitch,
                    SizeY: size.HeightM,
                    SizeZ: zHi - zLo));
                s++;
            }
        }
        return runs;
    }

    private static int LowestSetBit(int mask)
    {
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0)
                return i;
        return 0;
    }

    private static int HighestSetBit(int mask)
    {
        for (int i = 31; i >= 0; i--)
            if ((mask & (1 << i)) != 0)
                return i;
        return 0;
    }
}
