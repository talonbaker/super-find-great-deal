using System;
using System.Collections.Generic;

namespace MpFoundation.Game.World.Stock;

/// <summary>A bay as the room authored it: its node name (which is also its seed) and its
/// placement. <paramref name="YawDeg"/> is 0 for an aisle bay and ±90 for an end-cap, exactly as
/// <c>SearchRoom.tscn</c> instances them.</summary>
public readonly record struct RoomBay(string Name, float X, float Z, float YawDeg, bool IsEndCap);

/// <summary>One bulk instance in ROOM space, with the material that decides which MultiMesh it
/// belongs to.</summary>
public readonly record struct RoomInstance(StockMaterial Material, float X, float Y, float Z, float YawRad);

/// <summary>One static collision box in ROOM space.</summary>
public readonly record struct RoomCollider(
    string Why, float X, float Y, float Z, float SizeX, float SizeY, float SizeZ);

/// <summary>A hole, in ROOM space, with the pose a prop placed in it should take.</summary>
public readonly record struct RoomGap(
    string Bay, int Board, bool FullDepth, StockMaterial Material,
    float X, float BoardTopY, float Z, float WidthM, float DepthM);

/// <summary>Everything the room's bulk stock comes to.</summary>
public sealed class RoomStock
{
    public required IReadOnlyList<RoomInstance> Instances { get; init; }
    public required IReadOnlyList<RoomCollider> Colliders { get; init; }
    public required IReadOnlyList<RoomGap> Gaps { get; init; }

    public int CountOf(StockMaterial m)
    {
        int n = 0;
        foreach (RoomInstance i in Instances)
            if (i.Material == m)
                n++;
        return n;
    }
}

/// <summary>
/// <b>The search room's bulk stock, assembled from the bays the scene file actually authors.</b>
///
/// <para><b>The bay list is a PARAMETER, not a table in this file.</b> It is parsed out of
/// <c>SearchRoom.tscn</c> by the generator and again by <c>dotnet test</c>, so the room layout has
/// exactly one source of truth — the scene — and moving a bay in the editor moves its stock with
/// it. A hard-coded copy of the aisle arithmetic here would be a second layout that agrees with
/// the first until the day somebody nudges an aisle.</para>
/// </summary>
public static class StockRoom
{
    /// <summary>Cans on aisles 0 and 2, cereal boxes on 1 and 3 — SHELF-1's rule, and it is a
    /// gameplay fact rather than merchandising: SFX-1 gave each material its own voice, so
    /// knocking a can over in the dark tells the other player which aisle you are in. Bulk keeps
    /// it because a bay of cans with cardboard filler behind the facings would make that tell a
    /// lie the first time anything fell.
    ///
    /// <para><b>End-caps carry PRODUCE, and that is a correction to the packet rather than a
    /// preference.</b> The packet gives the end-caps "stacked box displays (pallet stacks)".
    /// SHELF-1 already authored TWELVE CARRYABLE ORANGES onto them -- two per shelf, six per
    /// end-cap, ids 1106..1117 -- and the facings are what the bulk fills in behind. Box filler
    /// behind orange facings would be the one place in this room where the thing you pick up and
    /// the thing behind it are different products, and SFX-1's per-material voice is what makes
    /// that a lie a player can hear. The stacked box displays are the floor pallets below, which
    /// is what a stacked display physically is; an end-cap is a shelf.</para></summary>
    public static StockMaterial MaterialFor(RoomBay bay)
    {
        if (bay.IsEndCap)
            return StockMaterial.Produce;
        // "Aisle0_Bay3" -> 0
        int i = bay.Name.IndexOf("Aisle", StringComparison.Ordinal);
        if (i >= 0 && i + 5 < bay.Name.Length && char.IsDigit(bay.Name[i + 5]))
            return (bay.Name[i + 5] - '0') % 2 == 0 ? StockMaterial.Can : StockMaterial.Box;
        return StockMaterial.Can;
    }

    // ------------------------------------------------------------------------------------
    // The floor pallets.
    // ------------------------------------------------------------------------------------

    /// <summary>Boxes across, deep and high in one pallet stack. 3 x 7 x 5 = 105 boxes,
    /// 0.63 x 0.49 m on the floor and <b>1.40 m tall — deliberately under the bays' 1.80 m</b>,
    /// so a stack is cover you crouch behind without becoming a second skyline that flattens
    /// SHELF-1's "a bay hides the aisle behind it" property.</summary>
    public const int PalletAcross = 3;
    public const int PalletDeep = 7;
    public const int PalletHigh = 5;

    /// <summary>
    /// Where the stacks stand, and <b>the reason both are flush against an end wall</b>.
    ///
    /// <para>SHELF-1 §6.1/§6.3 is the scar: the dressed search room is not straight-line
    /// navigable between walkways, this repo's bots walk in straight lines, and <i>every piece of
    /// furniture in this room is part of some other lane's staging</i> — SHELF-1 paid for that
    /// twice in one session (an end-cap on bot D's diagonal, three material-SFX fixtures inside a
    /// bay). The cross-aisles are the one route between walkways, so a stack parked in the middle
    /// of one is a red in somebody else's suite waiting to happen. Against the wall at x = ±6.7
    /// nothing has ever walked and nothing can: the bins are 2.01 m away, the drop-off bin keeps
    /// its 1.6 m clear radius (nearest approach 2.33 m), and the pillar is 4.8 m clear.</para>
    /// </summary>
    public static readonly (string Name, float X, float Z)[] Pallets =
    {
        // x = +/-6.6, not 6.7: a 3-wide stack is 0.63 m across, so 6.7 put its outer face at
        // 7.015 and the wall's inner face is at 7.0. Caught by
        // StockBakeTests.TheFloorPalletsAreClearOfEveryFixtureTheRoomAlreadyHas on the first
        // bake -- fifteen millimetres through a wall, which renders perfectly.
        ("PalletPos", 6.6f, -2.3f),
        ("PalletNeg", -6.6f, -2.3f),
    };

    /// <summary>One pallet stack's instances and its single collision box, in room space.</summary>
    public static (List<RoomInstance> Instances, RoomCollider Collider) BuildPallet(
        string name, float x, float z)
    {
        var rng = new StockRng(name);
        ProductSize box = ShelfStock.Box;
        float pitchX = ShelfStock.XPitchOf(StockMaterial.Box);
        float pitchZ = box.DepthM + 0.01f;
        var list = new List<RoomInstance>();

        for (int h = 0; h < PalletHigh; h++)
            for (int a = 0; a < PalletAcross; a++)
                for (int d = 0; d < PalletDeep; d++)
                {
                    float ix = x + (a - (PalletAcross - 1) * 0.5f) * pitchX;
                    float iz = z + (d - (PalletDeep - 1) * 0.5f) * pitchZ;
                    float iy = box.HalfHeightM + h * box.HeightM;
                    list.Add(new RoomInstance(StockMaterial.Box, ix, iy, iz, rng.NextJitter(0.03f)));
                }

        var collider = new RoomCollider(
            Why: name,
            X: x, Y: PalletHigh * box.HeightM * 0.5f, Z: z,
            SizeX: PalletAcross * pitchX,
            SizeY: PalletHigh * box.HeightM,
            SizeZ: PalletDeep * pitchZ);
        return (list, collider);
    }

    // ------------------------------------------------------------------------------------
    // The bin mounds.
    // ------------------------------------------------------------------------------------

    /// <summary>FloorBin.tscn's interior: 0.78 x 0.54 x 0.78, its floor 0.06 above the prefab's
    /// origin.</summary>
    public const float BinInteriorHalfM = 0.39f;
    public const float BinInteriorFloorY = 0.06f;

    /// <summary>The mound's three layers, 4x4 + 3x3 + 2x2 = <b>29 oranges</b>, tapering the way a
    /// dump bin actually looks. The top layer's centres are at y = 0.40 and the bin's rim is at
    /// 0.60, so the pile sits INSIDE the tub and a bin knocked over still spills it.</summary>
    public const int MoundLayers = 3;

    /// <summary>
    /// <b>Three of the six bins are stocked; three are left empty, and that is a design decision
    /// rather than a saving.</b>
    ///
    /// <para>SHELF-1's bible check closed INTERACTION §3 on this exact object: <i>"a bin is an
    /// open-top tub with a real hollow: 0.78 x 0.54 x 0.78 interior, which clears a 0.44 m crate
    /// by 0.17 m horizontally and 0.10 m vertically, so 'hide it under the bin' is a physical
    /// fact and not a scripted allowance."</i> The interior is 0.54 m tall and a crate needs
    /// 0.44 + clearance, so <b>any</b> mound collider in a bin deletes that affordance outright —
    /// there is no half-fill that keeps it. Filling all six would have traded a hiding place the
    /// game is built on for a texture. Filling three keeps both, and it is what a shop looks like
    /// anyway: some bins are full and some have been emptied.</para>
    /// </summary>
    public static readonly string[] StockedBins = { "Bin_1", "Bin_3", "Bin_5" };

    /// <summary>The mound in BIN-LOCAL space (the prefab's origin is its base), plus the one
    /// collision box that makes a placed object nestle on the pile instead of sinking into it.
    /// Lives inside the bin prefab, so it tips and travels with the bin — a static mound left
    /// hanging in the air over an overturned bin would be the worst kind of level bug.</summary>
    public static (List<RoomInstance> Instances, RoomCollider Collider) BuildBinMound(string seed)
    {
        var rng = new StockRng(seed);
        ProductSize p = ShelfStock.Produce;
        float pitch = ShelfStock.XPitchOf(StockMaterial.Produce);
        var list = new List<RoomInstance>();
        float top = BinInteriorFloorY;

        for (int layer = 0; layer < MoundLayers; layer++)
        {
            int n = 4 - layer;
            float y = BinInteriorFloorY + p.HalfHeightM + layer * (p.HeightM * 0.82f);
            top = y;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    float ix = (a - (n - 1) * 0.5f) * pitch + rng.NextJitter(0.012f);
                    float iz = (b - (n - 1) * 0.5f) * pitch + rng.NextJitter(0.012f);
                    list.Add(new RoomInstance(StockMaterial.Produce, ix, y, iz, rng.NextJitter(3.14f)));
                }
        }

        // The collider stops just under the top layer's centres, so the crowning oranges stand
        // proud of it and something set down on the pile beds into them rather than balancing on
        // an invisible plate.
        float colliderTop = top;
        var collider = new RoomCollider(
            Why: "BinMound",
            X: 0f, Y: (BinInteriorFloorY + colliderTop) * 0.5f, Z: 0f,
            SizeX: 3.4f * pitch, SizeY: colliderTop - BinInteriorFloorY, SizeZ: 3.4f * pitch);
        return (list, collider);
    }

    // ------------------------------------------------------------------------------------
    // Assembly.
    // ------------------------------------------------------------------------------------

    /// <summary>One of SHELF-1's 120 authored carryable products, in ROOM space.</summary>
    public readonly record struct RoomFacing(string Name, float X, float Y, float Z);

    /// <summary>
    /// Which bay a facing stands in, and where in that bay -- or null if it stands on the floor.
    ///
    /// <para>Twelve of the 120 are loose in the bins rather than on a shelf, so "not in any bay"
    /// is an ordinary answer and not a failure. A facing is matched on all three axes: inside the
    /// bay's footprint AND at one of that bay's board heights, because a bin standing in front of
    /// an end-cap would otherwise capture the oranges inside it.</para>
    /// </summary>
    public static BayFacing? FacingInBay(RoomBay bay, BaySpec spec, StockMaterial material, RoomFacing f)
    {
        float yaw = bay.YawDeg * MathF.PI / 180f;
        float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
        float dx = f.X - bay.X, dz = f.Z - bay.Z;
        // The inverse of Assemble's rotation.
        float lx = dx * cos - dz * sin;
        float lz = dx * sin + dz * cos;

        if (MathF.Abs(lx) > spec.LengthM * 0.5f || MathF.Abs(lz) > spec.DepthM * 0.5f)
            return null;

        float half = ShelfStock.SizeOf(material).HalfHeightM;
        for (int b = 0; b < ShelfStock.BoardCentreY.Length; b++)
            if (MathF.Abs(ShelfStock.BoardTopY(b) + half - f.Y) < 0.03f)
                return new BayFacing(b, lx, lz);
        return null;
    }

    /// <summary>One bay's fill, with the authored facings that stand in it carved out. The one
    /// path -- the generator, the engine self-test and the xUnit assertions all come through
    /// here, so none of them can be checking a different shelf from the one that ships.</summary>
    public static BayFill FillBay(RoomBay bay, IReadOnlyList<RoomFacing>? facings)
    {
        StockMaterial material = MaterialFor(bay);
        BaySpec spec = bay.IsEndCap ? ShelfStock.EndCap : ShelfStock.ShelfUnit;
        var mine = new List<BayFacing>();
        if (facings != null)
            foreach (RoomFacing f in facings)
                if (FacingInBay(bay, spec, material, f) is BayFacing inBay)
                    mine.Add(inBay);
        return ShelfStock.Fill(spec, material, bay.Name, mine);
    }

    /// <summary>Fill every bay the room authors, then add the floor pallets. Bin mounds are NOT
    /// here: they live inside the bin prefab so they move with it.</summary>
    public static RoomStock Assemble(IReadOnlyList<RoomBay> bays,
        IReadOnlyList<RoomFacing>? facings = null)
    {
        var instances = new List<RoomInstance>();
        var colliders = new List<RoomCollider>();
        var gaps = new List<RoomGap>();

        foreach (RoomBay bay in bays)
        {
            StockMaterial material = MaterialFor(bay);
            BayFill fill = FillBay(bay, facings);

            float yaw = bay.YawDeg * MathF.PI / 180f;
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);

            // Godot's +Y rotation: x' = x*cos + z*sin, z' = -x*sin + z*cos.
            foreach (BulkInstance b in fill.Instances)
                instances.Add(new RoomInstance(material,
                    bay.X + b.X * cos + b.Z * sin,
                    b.Y,
                    bay.Z - b.X * sin + b.Z * cos,
                    b.YawRad + yaw));

            foreach (BulkCollisionRun r in fill.Collision)
            {
                bool swap = MathF.Abs(sin) > 0.5f;
                colliders.Add(new RoomCollider(
                    Why: $"{bay.Name}/Board{r.Board}",
                    X: bay.X + r.CentreX * cos + r.CentreZ * sin,
                    Y: r.CentreY,
                    Z: bay.Z - r.CentreX * sin + r.CentreZ * cos,
                    SizeX: swap ? r.SizeZ : r.SizeX,
                    SizeY: r.SizeY,
                    SizeZ: swap ? r.SizeX : r.SizeZ));
            }

            foreach (StockGap g in fill.Gaps)
                gaps.Add(new RoomGap(
                    Bay: bay.Name, Board: g.Board, FullDepth: g.FullDepth, Material: material,
                    X: bay.X + g.CentreX * cos + g.CentreZ * sin,
                    BoardTopY: g.BoardTopY,
                    Z: bay.Z - g.CentreX * sin + g.CentreZ * cos,
                    WidthM: g.WidthM, DepthM: g.DepthM));
        }

        foreach ((string name, float x, float z) in Pallets)
        {
            (List<RoomInstance> list, RoomCollider collider) = BuildPallet(name, x, z);
            instances.AddRange(list);
            colliders.Add(collider);
        }

        return new RoomStock { Instances = instances, Colliders = colliders, Gaps = gaps };
    }
}
