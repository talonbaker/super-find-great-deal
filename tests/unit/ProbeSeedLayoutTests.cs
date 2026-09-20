using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PROBE-1 (2026-09-20): the seeded population's geometry, checked without an engine.
///
/// <para><b>Why these are facts and not a look at a capture.</b> The whole capacity table rests
/// on the row at N = 130 and the row at N = 2000 being the SAME experiment with more props in it.
/// If the lattice overlapped itself, the first second of every run would be the depenetrate path
/// and the table would be measuring a mistake; if the fill order were not a prefix, no two rows
/// would be comparable at all. Both are arithmetic, so both are pinned here rather than argued
/// about in a handoff.</para>
/// </summary>
public class ProbeSeedLayoutTests
{
    private static readonly IReadOnlyList<Vector3> Nothing = new List<Vector3>();

    [Fact]
    public void AskingForNProducesExactlyNSlots()
    {
        Assert.Equal(130, ProbeSeedLayout.Slots(130, Nothing, 0.85f).Count);
        Assert.Equal(500, ProbeSeedLayout.Slots(500, Nothing, 0.85f).Count);
        Assert.Equal(1000, ProbeSeedLayout.Slots(1000, Nothing, 0.85f).Count);
        Assert.Equal(2000, ProbeSeedLayout.Slots(2000, Nothing, 0.85f).Count);
    }

    [Fact]
    public void ZeroOrNegativeSeedsNothing()
    {
        Assert.Empty(ProbeSeedLayout.Slots(0, Nothing, 0.85f));
        Assert.Empty(ProbeSeedLayout.Slots(-5, Nothing, 0.85f));
    }

    /// <summary><b>The row at 130 is the first 130 slots of the row at 2000.</b> This is the
    /// property that makes the table a table: change N and nothing else changes.</summary>
    [Fact]
    public void ASmallerPopulationIsAPrefixOfALargerOne()
    {
        List<(Vector3 Local, PropKind Kind)> small = ProbeSeedLayout.Slots(130, Nothing, 0.85f);
        List<(Vector3 Local, PropKind Kind)> big = ProbeSeedLayout.Slots(2000, Nothing, 0.85f);
        for (int i = 0; i < small.Count; i++)
        {
            Assert.Equal(small[i].Kind, big[i].Kind);
            Assert.True(small[i].Local.IsEqualApprox(big[i].Local),
                $"slot {i} moved between N=130 and N=2000: {small[i].Local} vs {big[i].Local}");
        }
    }

    /// <summary><b>No seeded prop starts inside another one.</b> The pitch is 0.24 m and the
    /// widest seeded object is the cereal box at a 0.199 m plan diagonal, so the bound checked
    /// here is the one the class doc claims.</summary>
    [Fact]
    public void NoTwoSlotsAreCloserThanTheWidestPropsPlanDiagonal()
    {
        const float boxPlanDiagonalM = 0.199f;
        List<(Vector3 Local, PropKind Kind)> slots = ProbeSeedLayout.Slots(2000, Nothing, 0.85f);
        float worst = float.MaxValue;
        (int A, int B) at = (-1, -1);
        for (int i = 0; i < slots.Count; i++)
        {
            for (int j = i + 1; j < slots.Count; j++)
            {
                // Only slots on the same layer can collide in plan; different layers are
                // separated by LayerPitchM, which clears the tallest prop.
                if (!Mathf.IsEqualApprox(slots[i].Local.Y - ProbeSeedLayout.HalfHeightOf(slots[i].Kind),
                        slots[j].Local.Y - ProbeSeedLayout.HalfHeightOf(slots[j].Kind)))
                    continue;
                float dx = slots[i].Local.X - slots[j].Local.X;
                float dz = slots[i].Local.Z - slots[j].Local.Z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < worst)
                {
                    worst = d;
                    at = (i, j);
                }
            }
        }
        Assert.True(worst > boxPlanDiagonalM,
            $"slots {at.A} and {at.B} are {worst:0.000} m apart in plan, "
            + $"inside the widest prop's {boxPlanDiagonalM} m diagonal");
    }

    /// <summary>Layers are far enough apart vertically for the tallest prop (a 0.28 m cereal
    /// box) not to intersect the one above it.</summary>
    [Fact]
    public void LayerPitchClearsTheTallestProp()
    {
        const float boxHeightM = 0.28f;
        Assert.True(ProbeSeedLayout.LayerPitchM > boxHeightM,
            $"layer pitch {ProbeSeedLayout.LayerPitchM} m does not clear a {boxHeightM} m box");
    }

    /// <summary><b>Every slot is on walkable floor and out of the bays.</b> SHELF-1's aisles are
    /// 0.5 m deep at z = -3.15 / -1.05 / +1.05 / +3.15 and run to x = +/-4.6; a slot inside one
    /// would be a networked prop born inside static geometry, which is STOCK-1 S4.4's whole
    /// lesson (every rest audit reports StaticOverlap, layer 3 answers InsideStatic, and the
    /// room looks perfect).</summary>
    [Fact]
    public void NoSlotLandsInsideAnAisleFootprintOrACrossAisle()
    {
        float[] aisleZ = { -3.15f, -1.05f, 1.05f, 3.15f };
        const float aisleHalfDepth = 0.25f;   // bays are 0.5 m deep
        const float boxHalfDiagonal = 0.0995f;
        foreach ((Vector3 local, PropKind _) in ProbeSeedLayout.Slots(2000, Nothing, 0.85f))
        {
            Assert.True(Mathf.Abs(local.X) <= ProbeSeedLayout.HalfSpanX + 0.001f,
                $"slot at x={local.X:0.000} is outside the bay span and into a cross-aisle");
            foreach (float z in aisleZ)
                Assert.True(Mathf.Abs(local.Z - z) > aisleHalfDepth + boxHalfDiagonal,
                    $"slot at z={local.Z:0.000} overlaps the aisle at z={z}");
        }
    }

    /// <summary><b>The central walkway stays empty</b> — it is the lane the measured bot walks
    /// and looks down, and a bot wedged in frozen cans is a client measurement of a bot that
    /// never arrived.</summary>
    [Fact]
    public void TheCentralWalkwayIsNeverSeeded()
    {
        foreach ((Vector3 local, PropKind _) in ProbeSeedLayout.Slots(2000, Nothing, 0.85f))
            Assert.True(Mathf.Abs(local.Z) > 0.8f,
                $"slot at z={local.Z:0.000} is in the central walkway, which must stay clear");
    }

    /// <summary>All three material voices are represented in roughly equal thirds at every
    /// population, so no row of the table measures one mesh and one mass.</summary>
    [Fact]
    public void EveryPopulationCarriesAllThreeProductKinds()
    {
        foreach (int n in new[] { 130, 500, 1000, 2000 })
        {
            var counts = new Dictionary<PropKind, int>();
            foreach ((Vector3 _, PropKind kind) in ProbeSeedLayout.Slots(n, Nothing, 0.85f))
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            foreach (PropKind k in ProbeSeedLayout.Kinds)
                Assert.True(counts.GetValueOrDefault(k) >= n / 3 - 2,
                    $"N={n}: only {counts.GetValueOrDefault(k)} of kind {k}");
        }
    }

    /// <summary>A blocked point removes the slots around it and nothing else — and the
    /// requested count is still met, by taking later slots.</summary>
    [Fact]
    public void BlockedPointsEmptyTheirNeighbourhoodAndTheCountIsStillMet()
    {
        var blocked = new List<Vector3> { new(-4.4f, 0f, -4.2f) };
        List<(Vector3 Local, PropKind Kind)> slots = ProbeSeedLayout.Slots(500, blocked, 0.85f);
        Assert.Equal(500, slots.Count);
        foreach ((Vector3 local, PropKind _) in slots)
        {
            float dx = local.X - blocked[0].X;
            float dz = local.Z - blocked[0].Z;
            Assert.True(Mathf.Sqrt(dx * dx + dz * dz) >= 0.85f,
                $"slot at ({local.X:0.00}, {local.Z:0.00}) is inside the blocked radius");
        }
    }

    /// <summary><b>A kind is a function of the SLOT, not of the accepted count.</b> Adding one
    /// authored prop to the room must not shuffle the material of every seeded prop after it —
    /// otherwise a re-measurement after any level change is a different experiment.</summary>
    [Fact]
    public void BlockingASlotDoesNotRenumberTheKindsAfterIt()
    {
        var blocked = new List<Vector3> { new(-4.4f, 0f, -4.2f) };
        List<(Vector3 Local, PropKind Kind)> clean = ProbeSeedLayout.Slots(600, new List<Vector3>(), 0.85f);
        List<(Vector3 Local, PropKind Kind)> holed = ProbeSeedLayout.Slots(500, blocked, 0.85f);
        var kindAt = new Dictionary<(int, int), PropKind>();
        foreach ((Vector3 local, PropKind kind) in clean)
            kindAt[(Mathf.RoundToInt(local.X * 100f), Mathf.RoundToInt(local.Z * 100f))] = kind;
        int compared = 0;
        foreach ((Vector3 local, PropKind kind) in holed)
        {
            var key = (Mathf.RoundToInt(local.X * 100f), Mathf.RoundToInt(local.Z * 100f));
            if (!kindAt.TryGetValue(key, out PropKind expected))
                continue;
            Assert.Equal(expected, kind);
            compared++;
        }
        Assert.True(compared > 100, $"only {compared} slots were comparable — the test proved little");
    }

    /// <summary>The floor layer rests ON the floor: a slot's y is its own half-height, so
    /// nothing in the first layer hovers or sinks.</summary>
    [Fact]
    public void TheFloorLayerSitsOnTheFloor()
    {
        foreach ((Vector3 local, PropKind kind) in ProbeSeedLayout.Slots(ProbeSeedLayout.SlotsPerLayer, Nothing, 0.85f))
            Assert.Equal(ProbeSeedLayout.HalfHeightOf(kind), local.Y, 4);
    }

    /// <summary>How many layers each population of the packet's table needs — pinned, because
    /// the handoff says it in words and a reader has to be able to check the words.</summary>
    [Theory]
    [InlineData(130, 1)]
    [InlineData(500, 1)]
    [InlineData(1000, 2)]
    [InlineData(2000, 3)]
    public void EachPopulationNeedsTheNumberOfLayersTheHandoffClaims(int n, int layers)
    {
        List<(Vector3 Local, PropKind Kind)> slots = ProbeSeedLayout.Slots(n, Nothing, 0.85f);
        var floors = new HashSet<int>();
        foreach ((Vector3 local, PropKind kind) in slots)
            floors.Add(Mathf.RoundToInt(
                (local.Y - ProbeSeedLayout.HalfHeightOf(kind)) / ProbeSeedLayout.LayerPitchM));
        Assert.Equal(layers, floors.Count);
    }

    /// <summary>The top of the tallest population stays below the 1.040 m eyeline REACH-1
    /// measured, so the capture at N = 2000 photographs the room and not a wall of tins.</summary>
    [Fact]
    public void TheTallestPopulationStaysBelowTheEyeline()
    {
        const float eyelineM = 1.040f;
        foreach ((Vector3 local, PropKind kind) in ProbeSeedLayout.Slots(2000, Nothing, 0.85f))
            Assert.True(local.Y + ProbeSeedLayout.HalfHeightOf(kind) < eyelineM,
                $"a slot's top is at {local.Y + ProbeSeedLayout.HalfHeightOf(kind):0.000} m, "
                + $"above the {eyelineM} m eyeline");
    }
}
