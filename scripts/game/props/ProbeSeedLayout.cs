using System.Collections.Generic;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>Where PROBE-1 puts N extra resting props so that N is the only thing that changes.</b>
/// Engine-free arithmetic: a deterministic lattice over the search room's WALKWAY floor, in a
/// fill order that spreads across the room before it stacks.
///
/// <para><b>Why a lattice and not "scatter them about".</b> The question this probe answers is
/// "what does one more resting rigid body cost", and the only way a row at N = 2000 is comparable
/// to a row at N = 130 is if the first 130 slots of the 2000-slot run are the SAME 130 slots. A
/// seeded random scatter makes every row a different experiment (ReachCostProbe's shove direction
/// is derived from the prop id for exactly this reason). So the order is fixed, the geometry is
/// fixed, and N is a prefix length.</para>
///
/// <para><b>Why the walkways and not the shelves.</b> SHELF-1's boards already carry 120
/// carryable facings and STOCK-1 baked static filler into every cell those facings do not stand
/// in — the boards are full, by construction, and a seeded prop on one would be inside static
/// geometry (STOCK-1 S4.4 is the whole discussion). The walkway floor is the one large surface in
/// this room that is guaranteed clear: SHELF-1's own arithmetic is 4 bays x 0.5 m + 5 walkways x
/// 1.6 m = the room's 10 m width exactly, so a band centred on a walkway and narrower than 1.6 m
/// cannot touch a bay.</para>
///
/// <para><b>The central walkway is deliberately left empty.</b> z = 0 is the lane the measured bot
/// walks and looks down, and a bot wedged in a wall of frozen kinematic cans is a client
/// measurement of a bot that never arrived (the arrive-latch lesson, and CARRY-1's spawn-marker
/// note in SearchRoom.tscn, are both about exactly this). Four bands are seeded, not five.</para>
///
/// <para><b>Above one layer the lattice goes UP, and that is stated rather than hidden.</b> A
/// resting prop is a frozen kinematic body, so a slot 0.32 m above the floor holds it perfectly
/// still — physically fine, visually a shelf's worth of product hanging in a walkway. One layer
/// holds <see cref="SlotsPerLayer"/> = 888 slots, so 130 and 500 are floor-only, 1000 is two
/// layers and 2000 is three; the top layer of the 2000 row sits at 0.64 m, still well under the
/// 1.040 m eyeline REACH-1 measured, so the capture at that row shows the room rather than a wall.
/// The probe measures BODY COUNT, not a shippable layout — STOCK-2 is the lane that turns a
/// number into stock on a shelf.</para>
/// </summary>
public static class ProbeSeedLayout
{
    /// <summary>Walkway band centres, room-local z. SHELF-1's aisles sit at z = -3.15, -1.05,
    /// +1.05, +3.15 and are 0.5 m deep, so the walkable bands are [-5,-3.4], [-2.9,-1.3],
    /// [-0.8,0.8], [1.3,2.9], [3.4,5]. <b>z = 0 is missing on purpose</b> — see the class doc.</summary>
    public static readonly float[] BandCentreZ = { -4.2f, -2.1f, 2.1f, 4.2f };

    /// <summary>Half-width of a seeded band, metres. A walkway is 1.6 m wide; 0.6 m each side of
    /// its centre leaves 0.2 m of clear floor against each shelf face, which is more than the
    /// widest seeded object's plan half-diagonal (the cereal box, 0.0995 m).</summary>
    public const float BandHalfWidthM = 0.6f;

    /// <summary>Lattice pitch in x and z, metres. The widest seeded prop is the cereal box at
    /// 0.19 x 0.06 in plan = a 0.199 m diagonal at the worst yaw, so 0.24 m leaves 41 mm between
    /// two neighbours at their worst — twice PlacementIntegrity.DefaultOverlapToleranceM.</summary>
    public const float PitchM = 0.24f;

    /// <summary>Vertical pitch between layers, metres. The tallest seeded prop is the cereal box
    /// at 0.28 m, so 0.32 m clears it by 0.04 m.</summary>
    public const float LayerPitchM = 0.32f;

    /// <summary>Room-local x the lattice spans. The bays run to +/-4.6, so staying inside +/-4.4
    /// keeps every slot out of the two 2.4 m cross-aisles, off the pillar at (6, 1, 2.5), and
    /// clear of SearchSpawn_0/_1 at x = +/-4.5.</summary>
    public const float HalfSpanX = 4.4f;

    /// <summary>Columns along x in one band.</summary>
    public static int ColumnCount => (int)(2f * HalfSpanX / PitchM) + 1;   // 37

    /// <summary>Rows across one band.</summary>
    public static int RowCount => (int)(2f * BandHalfWidthM / PitchM) + 1; // 6

    /// <summary>How many slots one floor-level layer holds: 4 bands x 6 rows x 37 columns.</summary>
    public static int SlotsPerLayer => BandCentreZ.Length * RowCount * ColumnCount; // 888

    /// <summary>The three product kinds a seeded prop can be, cycled by slot index so every
    /// population carries all three material voices in equal thirds (SFX-1: tin, cardboard,
    /// produce). A run of one kind would measure one mesh and one mass.</summary>
    public static readonly PropKind[] Kinds = { PropKind.Can, PropKind.Box, PropKind.Produce };

    /// <summary>Half-height of each kind, metres, so a layer-0 slot RESTS on the floor rather
    /// than hovering a centimetre over it. Mirrors Carryable's own dimensions.</summary>
    public static float HalfHeightOf(PropKind kind) => kind switch
    {
        PropKind.Can => 0.06f,      // CanHeightM 0.12
        PropKind.Box => 0.14f,      // BoxSizeM.Y 0.28
        PropKind.Produce => 0.08f,  // ProduceRadiusM
        _ => 0.22f,                 // a crate, for completeness
    };

    /// <summary>
    /// The lattice, in fill order, as (room-local position, kind) pairs.
    ///
    /// <para><b>Order: layer, then band, then column, then row.</b> Layer outermost is what makes
    /// a small N spread over the whole floor instead of piling into one corner; band before
    /// column is what puts the first slots in four different walkways rather than 37 of them down
    /// one. Both matter to the measurement: prop cost is per body, but the AUDIT and the physics
    /// broadphase are per neighbourhood, and a heap in one corner would measure a heap.</para>
    ///
    /// <para><paramref name="blocked"/> is every point a slot must keep away from — the room's
    /// own authored props and its spawn markers, supplied by the caller because this class knows
    /// no scene. A slot within <paramref name="clearanceM"/> of one, in plan (x/z only, since the
    /// lattice stacks), is skipped rather than nudged: a nudged slot is no longer the same slot
    /// between two runs, which is the property this whole class exists to hold.</para>
    /// </summary>
    public static List<(Vector3 Local, PropKind Kind)> Slots(
        int count, IReadOnlyList<Vector3> blocked, float clearanceM)
    {
        var outp = new List<(Vector3, PropKind)>(System.Math.Max(count, 0));
        if (count <= 0)
            return outp;
        float clearSq = clearanceM * clearanceM;
        int index = 0;
        for (int layer = 0; outp.Count < count && layer < 64; layer++)
        {
            foreach (float bandZ in BandCentreZ)
            {
                for (int col = 0; col < ColumnCount; col++)
                {
                    float x = -HalfSpanX + col * PitchM;
                    for (int row = 0; row < RowCount; row++)
                    {
                        if (outp.Count >= count)
                            return outp;
                        float z = bandZ - BandHalfWidthM + row * PitchM;
                        PropKind kind = Kinds[index % Kinds.Length];
                        index++;
                        if (IsBlocked(x, z, blocked, clearSq))
                            continue;
                        outp.Add((new Vector3(x, layer * LayerPitchM + HalfHeightOf(kind), z), kind));
                    }
                }
            }
        }
        return outp;
    }

    /// <summary><b>The kind cycles on the SLOT index, not on the accepted count</b>, so removing
    /// a blocked slot does not renumber every kind after it — two runs at different N, or against
    /// a room with one more authored prop in it, still put the same kind in the same place.</summary>
    private static bool IsBlocked(float x, float z, IReadOnlyList<Vector3> blocked, float clearSq)
    {
        for (int i = 0; i < blocked.Count; i++)
        {
            float dx = blocked[i].X - x;
            float dz = blocked[i].Z - z;
            if (dx * dx + dz * dz < clearSq)
                return true;
        }
        return false;
    }
}
