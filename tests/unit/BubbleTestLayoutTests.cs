using System.Globalization;
using System.Text.RegularExpressions;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Sail.Game.World.BubbleTest;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The Bubble Test's layout contract and its one per-world switch, at the level a scene test
/// cannot reach.
///
/// <para><b>Division of labour with <c>BubbleTestSelfTest</c>:</b> that self-test runs in a live
/// Godot tree and answers "does the shipped scene still match the numbers" — anchors, spawn
/// markers, the bake, the colliders. These tests answer "are the numbers themselves still
/// coherent", which needs no engine and therefore belongs where it can run in a second: the lake
/// really is inside the shipped water constants, the stone gaps really are inside the measured
/// jump range, the bubble split really adds to 100, and the voice gate really is on for this
/// world and off for every other. A scene test cannot catch a green run whose constants quietly
/// stopped meaning what program §4 says they mean.</para>
/// </summary>
public class BubbleTestLayoutTests
{
    // --- Program D5 / BT-0 acceptance criterion 8: the voice gate ---------------------------

    /// <summary>Talon's Q4 ruling: the gray hub exists to test proximity voice, and a gate that is
    /// off tests nothing. This is the whole of D5 as a fact.</summary>
    [Fact]
    public void VoiceGateIsOnForTheBubbleTest()
    {
        Assert.True(VoiceProximityGate.DefaultForWorld(BubbleTestLayout.WorldId));
    }

    /// <summary><b>The control, and the more important half.</b> D5 turns the gate on for ONE
    /// world; it does not reverse <c>VoiceProximityGate</c>'s documented stance for the game. If
    /// this ever passes-by-turning-green because someone flipped <c>EnabledByDefault</c>, that is
    /// a decision only Talon makes, and it should break a test rather than ship quietly.</summary>
    [Theory]
    [InlineData("camp")]
    [InlineData("playground")]
    [InlineData("playtest1")]
    [InlineData("house")]
    [InlineData("")]
    public void VoiceGateStaysOffEverywhereElse(string worldId)
    {
        Assert.False(VoiceProximityGate.DefaultForWorld(worldId));
    }

    // --- Program D4 / §4 rules 1-2: the lake is where the shipped constants are true ---------

    /// <summary>BT-0 acceptance criterion 9, as a fact rather than as a diff review: the layout
    /// takes the lake surface FROM <c>WaterGeometry</c>. A literal here would be a second copy of
    /// −0.58 that drifts the first time the shore is retuned, and the failure mode is a lake that
    /// renders above ground — the exact defect Talon's brief flagged as a known risk.</summary>
    [Fact]
    public void LakeSurfaceIsTheShippedWaterConstant()
    {
        Assert.Equal(Sail.Game.Water.WaterGeometry.WaterY, BubbleTestLayout.LakeSurfaceY);
    }

    /// <summary>D4: the lake's east edge sits strictly inside <c>ShoreX</c>.
    ///
    /// <para><b>Historical note, kept because the reasoning changed under it.</b> This originally
    /// held because the shipped <c>DepthAt()</c> was a half-plane — any point with
    /// <c>x &lt;= -45</c> below <c>WaterY</c> was lake — so keeping the level west of -45 was what
    /// made the one global water field usable here. WATER-3 (2026-08-29) replaced the half-plane
    /// with a finite footprint per world, so this no longer carries that weight; it stays as a
    /// layout invariant, and the guard that matters now is
    /// <see cref="TheWaterContractsMirrorOfTheGreenAnchorHasNotDrifted"/>.</para></summary>
    [Fact]
    public void LakeSitsInsideTheShippedShoreThreshold()
    {
        Assert.True(BubbleTestLayout.LakeMaxX < Sail.Game.Water.WaterGeometry.ShoreX,
            $"the lake reaches x={BubbleTestLayout.LakeMaxX}, which is not west of ShoreX="
            + $"{Sail.Game.Water.WaterGeometry.ShoreX}.");
    }

    /// <summary><b>WATER-3's mirror guard.</b> <c>WaterGeometry</c> holds the green lake's centre
    /// as its own constants rather than reading <see cref="BubbleTestLayout.GreenAnchor"/>, because
    /// the dependency runs the other way on purpose — the water contract must not depend on a
    /// level. That makes it a second copy, and this is what stops it drifting: move the green
    /// section and the swimmable disc would stay behind silently, which is exactly the class of
    /// defect Talon's note 3 was.</summary>
    [Fact]
    public void TheWaterContractsMirrorOfTheGreenAnchorHasNotDrifted()
    {
        // Constants first, because xUnit2000 requires the compile-time constant in the `expected`
        // slot; the anchor is still the source of truth and the mirror is still the copy.
        Assert.Equal(Sail.Game.Water.WaterGeometry.BubbleTestLakeCentreX,
                     BubbleTestLayout.GreenAnchor.X);
        Assert.Equal(Sail.Game.Water.WaterGeometry.BubbleTestLakeCentreZ,
                     BubbleTestLayout.GreenAnchor.Z);
        Assert.Equal(Sail.Game.Water.WaterGeometry.BubbleTestWorldId, BubbleTestLayout.WorldId);
    }

    /// <summary>The swimmable disc fits inside the green section's own footprint, so no other
    /// section can contain water and the lake cannot reach ground green does not own. The east edge
    /// is the one worth stating: the disc reaches x = -78, comfortably inside
    /// <see cref="BubbleTestLayout.LakeMaxX"/> (-50).</summary>
    [Fact]
    public void TheSwimmableDiscFitsInsideTheGreenFootprint()
    {
        Aabb green = BubbleTestLayout.GreenFootprint;
        Sail.Game.Water.WaterGeometry.LakeFootprint lake =
            Sail.Game.Water.WaterGeometry.BubbleTestLake;
        Assert.True(lake.MinX >= green.Position.X, $"the lake reaches x={lake.MinX}.");
        Assert.True(lake.MaxX <= BubbleTestLayout.LakeMaxX, $"the lake reaches x={lake.MaxX}.");
        Assert.True(lake.MinZ >= green.Position.Z, $"the lake reaches z={lake.MinZ}.");
        Assert.True(lake.MaxZ <= green.Position.Z + green.Size.Z, $"the lake reaches z={lake.MaxZ}.");
        // ...and its floor clears the level's one authored sub-zero surface.
        Assert.True(lake.FloorY < BubbleTestLayout.LakeBedMinY);
    }

    /// <summary>§4 rule 2: the green section is entirely west of the lake's east limit, so no
    /// green ground can be mistaken for lake and no lake can appear outside green.</summary>
    [Fact]
    public void TheGreenSectionIsEntirelyWestOfTheLakeLimit()
    {
        Aabb green = BubbleTestLayout.GreenFootprint;
        Assert.True(green.Position.X + green.Size.X <= BubbleTestLayout.LakeMaxX);
    }

    // --- §4 rule 1: nothing walkable below zero except the lake bed -------------------------

    /// <summary>Every section's floor is at or above y = 0 — the one exception being green's lake
    /// bed, which is why <c>GreenMinHeight</c> is a separate constant rather than a footprint
    /// property. Break this and a section's ground starts reading as water.</summary>
    [Fact]
    public void NoSectionSitsBelowGroundExceptUnderTheLakeAndTheTvRoom()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            if (s == BubbleTestLayout.Section.TvRoom) continue; // sealed, 40 m down, by design
            Assert.Equal(0f, BubbleTestLayout.AnchorOf(s).Y);
        }
        Assert.True(BubbleTestLayout.GreenMinHeight < 0f);
        Assert.True(BubbleTestLayout.LakeBedMinY <= BubbleTestLayout.GreenMinHeight);
    }

    // --- §4 rule 3: the world extent ---------------------------------------------------------

    /// <summary>The off-map radius has to clear the furthest corner of the furthest section, or
    /// a player standing on authored ground is killed for being there. Cyan is the far one: its
    /// corner is at (70, 145).</summary>
    [Fact]
    public void OffMapRadiusClearsEverySectionCorner()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            Aabb f = BubbleTestLayout.FootprintOf(s);
            float maxX = Mathf.Max(Mathf.Abs(f.Position.X), Mathf.Abs(f.Position.X + f.Size.X));
            float maxZ = Mathf.Max(Mathf.Abs(f.Position.Z), Mathf.Abs(f.Position.Z + f.Size.Z));
            // RespawnService tests |x| and |z| independently — a square boundary, not a circle.
            Assert.True(maxX < BubbleTestLayout.OffMapRadiusM,
                $"{s} reaches x={maxX}, at or past OffMapRadiusM={BubbleTestLayout.OffMapRadiusM}.");
            Assert.True(maxZ < BubbleTestLayout.OffMapRadiusM,
                $"{s} reaches z={maxZ}, at or past OffMapRadiusM={BubbleTestLayout.OffMapRadiusM}.");
        }
    }

    /// <summary>A player who falls through the TV room's floor must still be caught. If the void
    /// plane ever rises above that floor, the egg becomes a hole players fall into forever.</summary>
    [Fact]
    public void TheVoidPlaneIsBelowTheTvRoomFloor()
    {
        Assert.True(BubbleTestLayout.VoidKillY < BubbleTestLayout.TvRoomAnchor.Y);
    }

    // --- §4 rule 5: the spawn ring -----------------------------------------------------------

    [Fact]
    public void SixSpawnsSitOnTheRingAndNoneCoincide()
    {
        var seen = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < BubbleTestLayout.SpawnCount; i++)
        {
            Vector3 p = BubbleTestLayout.SpawnPosition(i);
            Assert.Equal(0f, p.Y);
            Assert.True(Mathf.Abs(p.Length() - BubbleTestLayout.SpawnRingRadius) < 0.001f);
            foreach (Vector3 other in seen)
                Assert.True(p.DistanceTo(other) > 1f, $"spawns {p} and {other} overlap.");
            seen.Add(p);
        }
    }

    // --- §4 rule 2 and rule 6: the stones are jumpable, the tower's edge is reachable ---------

    /// <summary>The stones are above water, ordered, and inside the jump envelope at all. What
    /// they are no longer inside is the TAP, which is the point of the fork test below.</summary>
    [Fact]
    public void SteppingStoneGapsStayInsideTheMeasuredJumpEnvelope()
    {
        Assert.True(BubbleTestLayout.StoneGapMin > 0f);
        Assert.True(BubbleTestLayout.StoneGapMin < BubbleTestLayout.StoneGapMax);
        Assert.True(BubbleTestLayout.StoneGapMax < BubbleTestLayout.SprintRange);
        Assert.True(BubbleTestLayout.StoneTopMin < BubbleTestLayout.StoneTopMax);
        Assert.True(BubbleTestLayout.StoneTopMin > BubbleTestLayout.LakeSurfaceY);
    }

    /// <summary>
    /// <b>MOVE-8 FORK: the stepping-stone crossing left the tap jump, and only Talon can settle
    /// it.</b>
    ///
    /// <para>Talon's brief asks for a crossing on stepping stones, <i>not</i> swimming, and §4
    /// rule 2 sized the gaps against the measured jog-tap range: 2.2 m against a 1.710 m tap, i.e.
    /// a tap plus a short run-up. His 2026-08-28 speed ruling takes the tap to <b>0.887 m</b>, so
    /// the widest gap is now <b>2.5x</b> a tap and the crossing has become exactly the sprint-jump
    /// sequence over deep water that rule 2 exists to forbid.</para>
    ///
    /// <para><b>Why this is pinned rather than repaired.</b> The stones are baked geometry in
    /// <c>GreenHills.tscn</c> and MOVE-8 is explicitly forbidden to re-space geometry; the repair
    /// is either a re-bake or a re-opened speed ruling, and both are Talon's. <b>This test goes RED
    /// the moment the fork closes</b> — re-space the stones under 1.387 m, or move the tuning back,
    /// and it fails, at which point it is deleted and the assertion it replaced
    /// (<c>StoneGapMax &lt;= TapRange + 0.5f</c>) goes back into the test above.</para>
    /// </summary>
    [Fact]
    public void MOVE8_TheStoneCrossingLeftTheTapJump_AndIsAwaitingTalonsRuling()
    {
        Assert.True(BubbleTestLayout.StoneGapMax > BubbleTestLayout.TapRange + 0.5f,
            "the stone crossing fits the tap again — the fork has CLOSED: delete this test and "
            + "restore `StoneGapMax <= TapRange + 0.5f` to SteppingStoneGapsStayInsideTheMeasured"
            + "JumpEnvelope");

        // The size of the breach, so a later drift cannot pass for the state MOVE-8 measured.
        Assert.InRange(BubbleTestLayout.StoneGapMax / BubbleTestLayout.TapRange, 2.4f, 2.6f);
    }

    /// <summary>
    /// <b>"Edge of reachability" must mean hard, never impossible</b> — §4 rule 6, and the claim is
    /// unchanged. What changed at MOVE-8 is WHICH jump the ceiling is.
    ///
    /// <para>Talon ruled the traditional double jump on 2026-08-28, so the reachability ceiling is
    /// <c>DoubleApex</c> / <c>DoubleRange</c> (2.410 m / 6.485 m) rather than the single held
    /// sprint jump (1.407 m / 4.053 m). The blue tower's authored bands — 5.4-5.9 m gaps, 1.3-1.45 m
    /// step-ups — sit past the single jump and inside the double, so <b>the hardest blue lines
    /// became double-jump-MANDATORY rather than impossible</b>. That is a class change, it is
    /// asserted below, and MOVE-8's report carries the per-section list for Talon.</para>
    ///
    /// <para>Re-pinning to the double jump is not a weakening: it is the same "inside the ceiling"
    /// claim, evaluated against the ceiling the motor actually has. The single-jump comparison is
    /// KEPT, as the thing that says the class changed.</para>
    /// </summary>
    [Fact]
    public void TheHardestBlueJumpIsStillInsideTheMeasuredJumpEnvelope()
    {
        Assert.True(BubbleTestLayout.EdgeGapMin < BubbleTestLayout.EdgeGapMax);
        Assert.True(BubbleTestLayout.EdgeGapMax < BubbleTestLayout.DoubleRange);
        Assert.True(BubbleTestLayout.EdgeStepMin < BubbleTestLayout.EdgeStepMax);
        Assert.True(BubbleTestLayout.EdgeStepMax < BubbleTestLayout.DoubleApex);
        Assert.True(BubbleTestLayout.TapApex < BubbleTestLayout.SprintApex);

        // MOVE-8's class change, asserted rather than only reported: both bands are now OUT of
        // reach of a single jump. If either of these two goes red the tower has become
        // single-jump-clearable again, which is a level-design fact worth a red test.
        Assert.True(BubbleTestLayout.EdgeGapMin > BubbleTestLayout.SprintRange,
            "the blue edge gaps are single-jump-clearable again — re-check MOVE-8's changed-class "
            + "list, it is now stale");
        Assert.True(BubbleTestLayout.EdgeStepMax > BubbleTestLayout.SprintApex,
            "the blue step-ups are single-jump-clearable again — see above");
    }

    // --- Program D8: the bubble split --------------------------------------------------------

    /// <summary>BT-11 places the bubbles and BT-13 counts them; if the split here does not add to
    /// the target, one of those two packets fails on a number this file could have caught.</summary>
    [Fact]
    public void TheBubbleSplitAddsUpToTheTarget()
    {
        int sum = BubbleTestLayout.SeamBubbles;
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
            sum += BubbleTestLayout.BubblesOf(s);
        Assert.Equal(BubbleTestLayout.BubbleTarget, sum);
    }

    /// <summary>Talon picked cyan for how bubbles read against it and asked for the heaviest
    /// concentration there. That is a design intent, so it is pinned rather than left to whoever
    /// next edits the split.</summary>
    [Fact]
    public void CyanCarriesTheDensestBubbles()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            if (s == BubbleTestLayout.Section.CyanRun) continue;
            Assert.True(BubbleTestLayout.CyanBubbles > BubbleTestLayout.BubblesOf(s));
        }
    }

    // --- §4 rule 4 and D3: the sections do not overlap ----------------------------------------

    /// <summary>One section, one file, one owner (D3) only works if two owners can never author
    /// the same ground. Overlapping footprints would also make <c>SectionVolume</c>'s enter/exit
    /// logging ambiguous, since a player would be in two sections at once.</summary>
    [Fact]
    public void NoTwoSectionFootprintsOverlapInPlan()
    {
        var all = BubbleTestLayout.AllSections;
        for (int i = 0; i < all.Length; i++)
        for (int j = i + 1; j < all.Length; j++)
        {
            // The TV room is 40 m under everything, so plan overlap with it is intended.
            if (all[i] == BubbleTestLayout.Section.TvRoom || all[j] == BubbleTestLayout.Section.TvRoom)
                continue;
            Aabb a = BubbleTestLayout.FootprintOf(all[i]);
            Aabb b = BubbleTestLayout.FootprintOf(all[j]);
            bool separated =
                a.Position.X + a.Size.X <= b.Position.X || b.Position.X + b.Size.X <= a.Position.X ||
                a.Position.Z + a.Size.Z <= b.Position.Z || b.Position.Z + b.Size.Z <= a.Position.Z;
            Assert.True(separated, $"{all[i]} and {all[j]} overlap in plan.");
        }
    }

    /// <summary>The four hub connector corridors (§4 rule 4) live OUTSIDE
    /// <c>HubFootprint</c>, so admitting them to the self-test could in principle re-open the
    /// overlap correction BT-0 made. This pins the seam instead of loosening it: no corridor may
    /// intersect ANY section footprint in plan, and each corridor's far edge must touch its
    /// section's near edge exactly — a corridor that stops short leaves a gap the player falls
    /// into, and one that overruns puts hub geometry on a section's ground.
    ///
    /// <para>Ruled by the orchestrator on BT-5's escalation, 2026-08-28.</para></summary>
    [Fact]
    public void HubConnectorsTouchTheirSectionAndOverlapNoFootprint()
    {
        Aabb[] corridors = BubbleTestLayout.HubConnectors;
        Assert.Equal(4, corridors.Length);

        foreach (Aabb c in corridors)
        {
            foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
            {
                // The TV room is 40 m below; plan overlap with it is intended and harmless.
                if (s == BubbleTestLayout.Section.TvRoom) continue;
                Aabb f = BubbleTestLayout.FootprintOf(s);
                bool separated =
                    c.Position.X + c.Size.X <= f.Position.X || f.Position.X + f.Size.X <= c.Position.X ||
                    c.Position.Z + c.Size.Z <= f.Position.Z || f.Position.Z + f.Size.Z <= c.Position.Z;
                Assert.True(separated,
                    $"connector at {c.Position} overlaps {s}'s footprint in plan — a corridor "
                    + "must lie in the gap between footprints, not on a section's ground.");
            }
        }

        // Each corridor's far edge touches its section's near edge exactly (no gap, no overrun).
        const float touch = 0.001f;
        Assert.Equal(BubbleTestLayout.RedFootprint.Position.Z + BubbleTestLayout.RedFootprint.Size.Z,
                     corridors[0].Position.Z, touch);
        Assert.Equal(BubbleTestLayout.BlueFootprint.Position.X,
                     corridors[1].Position.X + corridors[1].Size.X, touch);
        Assert.Equal(BubbleTestLayout.CyanFootprint.Position.Z,
                     corridors[2].Position.Z + corridors[2].Size.Z, touch);
        Assert.Equal(BubbleTestLayout.GreenFootprint.Position.X + BubbleTestLayout.GreenFootprint.Size.X,
                     corridors[3].Position.X, touch);
    }

    /// <summary>A section's local footprint is its world footprint moved by its anchor — the one
    /// conversion every geometry packet does by hand.
    ///
    /// <para>The second half pins BT-0's correction: <b>every</b> section is now centred on its own
    /// anchor. Program §4 as published left green 2.5 m off-centre, which is exactly the sort of
    /// thing a section author assumes away and then discovers as a 2.5 m seam at merge time.
    /// If a future edit re-introduces an off-centre section, it should have to say so here.</para>
    /// </summary>
    [Fact]
    public void LocalFootprintsRoundTripThroughTheAnchorAndAreCentredOnIt()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            Aabb local = BubbleTestLayout.LocalFootprint(s);
            Aabb world = BubbleTestLayout.FootprintOf(s);
            Assert.Equal(world.Position, local.Position + BubbleTestLayout.AnchorOf(s));
            Assert.Equal(world.Size, local.Size);

            if (s == BubbleTestLayout.Section.TvRoom) continue; // anchored at its FLOOR, not its centre
            Assert.True(Mathf.Abs(local.Position.X + local.Size.X * 0.5f) < 0.001f,
                $"{s} is off-centre on its anchor in x by {local.Position.X + local.Size.X * 0.5f} m.");
            Assert.True(Mathf.Abs(local.Position.Z + local.Size.Z * 0.5f) < 0.001f,
                $"{s} is off-centre on its anchor in z by {local.Position.Z + local.Size.Z * 0.5f} m.");
        }
    }

    /// <summary>Every section's volume covers its footprint in plan, and only the TV room's is a
    /// box rather than a 60 m column — a column there would reach up into the hub and log every
    /// hub crossing twice.</summary>
    [Fact]
    public void SectionVolumesCoverTheirFootprints()
    {
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            Aabb v = BubbleTestLayout.VolumeOf(s);
            Aabb f = BubbleTestLayout.FootprintOf(s);
            Assert.Equal(f.Position.X, v.Position.X);
            Assert.Equal(f.Position.Z, v.Position.Z);
            Assert.Equal(f.Size.X, v.Size.X);
            Assert.Equal(f.Size.Z, v.Size.Z);
        }
        Assert.Equal(BubbleTestLayout.SectionVolumeHeight,
            BubbleTestLayout.VolumeOf(BubbleTestLayout.Section.Hub).Size.Y);
        Assert.Equal(BubbleTestLayout.TvRoomHeight,
            BubbleTestLayout.VolumeOf(BubbleTestLayout.Section.TvRoom).Size.Y);
    }

    /// <summary>A footprint must sit at the same height as the section it bounds. This is the
    /// assertion whose absence let <c>TvRoomFootprint</c> keep a retyped floor of −40 for a room
    /// whose anchor had moved to −20: <c>SectionVolumesCoverTheirFootprints</c> above checks X, Z
    /// and <c>Size.Y</c>, so a 20 m error in <c>Position.Y</c> was invisible to the whole suite.
    /// The TV room is the only section anchored at its floor, which is exactly why it was the one
    /// that drifted.</summary>
    [Fact]
    public void TheTvRoomFootprintSitsAtItsAnchorNotAtSomeRetypedDepth()
    {
        Aabb f = BubbleTestLayout.FootprintOf(BubbleTestLayout.Section.TvRoom);
        Assert.Equal(BubbleTestLayout.TvRoomAnchor.Y, f.Position.Y);
        Assert.Equal(BubbleTestLayout.TvRoomHeight, f.Size.Y);

        // ...and the volume, which IS the footprint for this section, therefore lands there too.
        Aabb v = BubbleTestLayout.VolumeOf(BubbleTestLayout.Section.TvRoom);
        Assert.Equal(BubbleTestLayout.TvRoomAnchor.Y, v.Position.Y);

        // The room must clear the GLOBAL kill plane (NetProfile.KillPlaneY = −30), not merely the
        // per-world VoidKillY (−70). −30 is the binding floor; ServerTick enforces it every tick,
        // and at −40 the server teleported a player in and reset them to spawn on the next tick.
        Assert.True(f.Position.Y > -30f,
            $"The TV room floor at {f.Position.Y} must sit above the global kill plane at −30.");
    }

    /// <summary>A section volume must reach the top of its own section. Blue is the case that
    /// caught this: at 42 m its summit is above a 60 m box centred on the ground plane, so a
    /// player standing on the goal block would read as having <i>left</i> the section in order to
    /// climb it — the dwell log would say the opposite of what happened.
    ///
    /// <para><b>THE SHIPPED SCENE IS WHAT IS READ HERE, not the constant</b> (MRF-B / F5,
    /// 2026-08-30). Until this run every line below asked <see cref="BubbleTestLayout.VolumeOf"/>
    /// about <see cref="BubbleTestLayout.SectionVolumeHeight"/> — the constant against the
    /// constant — and stayed green for the whole of W7-1 while the live <c>Area3D</c>s in
    /// <c>BubbleTest.tscn</c> still ran −10..+50. The volumes are HAND-AUTHORED, so nothing
    /// derives them from the contract and only a test that opens the file can tell the two apart.
    /// The 60.789 m spire summit was outside <c>Volume_Tangle</c> the entire time; the dwell log
    /// — the playtest's only "is exploration fun" instrument — attributed the reward beat to no
    /// section at all.</para></summary>
    [Fact]
    public void SectionVolumesReachTheTopOfTheirOwnSection()
    {
        Tscn world = Tscn.Load(WorldScenePath);
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            if (s == BubbleTestLayout.Section.TvRoom) continue; // its volume IS its box
            Aabb v = SceneVolumeOf(world, s);
            float top = BubbleTestLayout.AnchorOf(s).Y + BubbleTestLayout.MaxHeightOf(s);
            Assert.True(v.Position.Y + v.Size.Y >= top,
                $"{s}'s volume in {WorldScenePath} tops out at {v.Position.Y + v.Size.Y} but the "
                + $"section may reach {top}.");
            Assert.True(v.Position.Y <= BubbleTestLayout.GreenMinHeight,
                $"{s}'s volume floor {v.Position.Y} is above the lake bed at "
                + $"{BubbleTestLayout.GreenMinHeight}.");
        }
        // ...and never so deep it reaches the TV room, which would double-log every crossing.
        Assert.True(BubbleTestLayout.SectionVolumeBottomY > BubbleTestLayout.TvRoomAnchor.Y
                                                            + BubbleTestLayout.TvRoomHeight);
    }

    /// <summary><b>The authored boxes ARE the derived volumes</b>, number for number, read out of
    /// <c>BubbleTest.tscn</c>. This is the drift guard F5 asked for: the volumes cannot be
    /// generated from <see cref="BubbleTestLayout.VolumeOf"/> without moving them out of the
    /// editor-openable scene Talon's constraint requires, so the next best thing is a test that
    /// fails the moment the two disagree — in either direction, and by any amount.</summary>
    [Fact]
    public void TheShippedSceneAuthorsExactlyTheVolumesTheLayoutDerives()
    {
        Tscn world = Tscn.Load(WorldScenePath);
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            Aabb scene = SceneVolumeOf(world, s);
            Aabb want = BubbleTestLayout.VolumeOf(s);
            Assert.True(scene.Position.IsEqualApprox(want.Position)
                        && scene.Size.IsEqualApprox(want.Size),
                $"{BubbleTestLayout.VolumeNameOf(s)} in {WorldScenePath} spans "
                + $"{scene.Position}..{scene.End}, but BubbleTestLayout.VolumeOf({s}) derives "
                + $"{want.Position}..{want.End}. The scene is hand-authored; when the contract "
                + "moves, the boxes have to move with it.");
        }
    }

    /// <summary><b>Every relocated television — the 60.789 m spire summit included — stands inside
    /// the section volume of the section it belongs to</b>, measured from the shipped scene.
    ///
    /// <para>This is F5's acceptance criterion 1 as a fact rather than as arithmetic. The four
    /// summit televisions are the highest AUTHORED STANDING SURFACES in the level (their Y values
    /// are the top faces of authored boxes — see the note on <see cref="BubbleTestLayout.TvRoutes"/>),
    /// so a player collecting the reward beat is standing at exactly these coordinates. Which
    /// section each belongs to is derived, not typed: it is the one volume whose plan contains the
    /// television, and the test fails just as loudly if that stops being unique.</para></summary>
    [Fact]
    public void EveryTelevisionStandsInsideItsOwnSectionsVolumeInTheShippedScene()
    {
        Tscn world = Tscn.Load(WorldScenePath);
        foreach (BubbleTestLayout.TvRoute route in BubbleTestLayout.TvRoutes)
        {
            (BubbleTestLayout.Section section, Aabb v) = SectionUnder(world, route.EntrancePos);
            Vector3 p = route.EntrancePos;
            Assert.True(p.Y >= v.Position.Y && p.Y <= v.End.Y,
                $"{route.EntranceName} stands at {p.Y:0.###} m, outside {section}'s own volume "
                + $"({v.Position.Y:0.###}..{v.End.Y:0.###}) as authored in {WorldScenePath}. A "
                + "player on that pad is attributed to NO section, so the dwell log reports the "
                + "opposite of the climb they just made.");
        }
    }

    /// <summary>Scene paths and volume names are derived, never typed, so a section rename cannot
    /// go half-done. This pins the derivation against the paths the world scene actually
    /// instances.</summary>
    [Fact]
    public void ScenePathsAndVolumeNamesAreDerivedFromTheSectionName()
    {
        Assert.Equal("res://scenes/game/world/bubbletest/sections/GreenHills.tscn",
            BubbleTestLayout.ScenePathOf(BubbleTestLayout.Section.GreenHills));
        Assert.Equal("Volume_CyanRun",
            BubbleTestLayout.VolumeNameOf(BubbleTestLayout.Section.CyanRun));
        Assert.Equal("res://resources/materials/bubbletest/red_goal.tres",
            BubbleTestLayout.MaterialPath("red", "goal"));
    }

    // --- The TV returns, against the pads they land on (MRF-B / F6) --------------------------

    /// <summary>How far a standing player travels in the first half-second of held forward, from
    /// the standstill <c>MoveState.AtSpawn</c> leaves them in: a ramp at
    /// <see cref="AvatarMotor.Acceleration"/> up to <see cref="AvatarMotor.MoveSpeed"/>, then the
    /// remainder at speed. <b>Derived, never typed</b> — MOVE-8 moved the speed ladder once
    /// already and a literal here would have gone stale silently.</summary>
    private static float HeldForwardHalfSecondM
    {
        get
        {
            const float window = 0.5f;
            float v = AvatarMotor.MoveSpeed;
            float ramp = v / AvatarMotor.Acceleration;
            return ramp >= window
                ? 0.5f * AvatarMotor.Acceleration * window * window
                : 0.5f * v * ramp + v * (window - ramp);
        }
    }

    /// <summary>The bar a return point has to clear: half a second of held forward PLUS the body's
    /// own collision radius, because a capsule centre exactly on the lip is already half off it.
    /// </summary>
    private static float ReturnClearanceBarM =>
        HeldForwardHalfSecondM + AvatarProportions.Fallback.CapsuleRadiusM;

    /// <summary><b>A television return is a reward, not a hazard</b> (LEVEL-BIBLE: an extraction
    /// point must be recoverable). MRF-B / F6: the four summit returns landed 1.10 / 1.10 / 1.60 /
    /// 2.61 m from the lip of a 22.8–60.8 m drop, with yaw reset to −Z and forward typically still
    /// held from walking into the screen — so the reward at the end of a 41–61 m climb was one
    /// held key away from costing the whole climb.
    ///
    /// <para><b>Everything here is read, not asserted from memory.</b> The pad is found in the
    /// SECTION scene file — the unrotated authored box whose top face the television stands on and
    /// whose plan contains it — and the return point comes from
    /// <see cref="BubbleTestWorld.ReturnFor"/>, the shipped function. A change to
    /// <see cref="BubbleTestLayout.RoomReturnOffset"/>, to a pad's size, or to a television's
    /// position all move this test, which is the property REV-2 found missing: the old
    /// <c>TvPortalSelfTest.AfterReturnLeg</c> tolerance (≤2 m at 0.4 s, against ~0.79 m of free
    /// fall in that window) structurally cannot see a walk-off.</para></summary>
    [Fact]
    public void EveryTvReturnStandsClearOfItsPadsEdge()
    {
        Tscn world = Tscn.Load(WorldScenePath);
        var rows = new List<string>();
        var offenders = new List<string>();
        foreach (BubbleTestLayout.TvRoute route in BubbleTestLayout.TvRoutes)
        {
            // The hub's television returns to the spawn ring on a flat 80 m plaza; there is no pad
            // and no lip. Recognised by the shipped branch actually being taken, not by the node
            // name — a second row aimed at the ring would be skipped here for the same real reason.
            Vector3 ret = BubbleTestWorld.ReturnFor(route);
            if (!ret.IsEqualApprox(route.EntrancePos + BubbleTestLayout.RoomReturnOffset)) continue;

            (BubbleTestLayout.Section section, Aabb _) = SectionUnder(world, route.EntrancePos);
            Aabb pad = PadUnder(section, route.EntrancePos);
            float clear = Mathf.Min(
                Mathf.Min(ret.X - pad.Position.X, pad.End.X - ret.X),
                Mathf.Min(ret.Z - pad.Position.Z, pad.End.Z - ret.Z));
            // Every route is measured before anything is asserted, so a failure names ALL of them
            // rather than only the first — four pads that all moved together are one finding.
            rows.Add($"{route.EntranceName} {clear:0.00} m on a {pad.Size.X:0.#}×{pad.Size.Z:0.#} m "
                     + $"pad at {pad.Position.Y + pad.Size.Y:0.###} m");
            if (clear < ReturnClearanceBarM) offenders.Add(route.EntranceName);
        }
        Assert.True(rows.Count >= 4,
            $"only {rows.Count} summit return(s) were measured; TvRoutes lists "
            + $"{BubbleTestLayout.TvRoutes.Length}.");
        Assert.True(offenders.Count == 0,
            $"{string.Join(" and ", offenders)} land inside the "
            + $"{ReturnClearanceBarM:0.00} m bar (half a second of held forward, "
            + $"{HeldForwardHalfSecondM:0.00} m, plus the "
            + $"{AvatarProportions.Fallback.CapsuleRadiusM:0.00} m body radius) — a returning "
            + $"player walks off the climb they just paid for. Measured: {string.Join("; ", rows)}.");
    }

    /// <summary>The lateral offset survives the F6 rotation. Reason (2) on
    /// <see cref="BubbleTestLayout.RoomReturnOffset"/> is a DIRECTION (THRILL §12): the returning
    /// player faces the screen they came out of, and it has to sit off to one side rather than
    /// fill the frame. Rotating the offset toward the pad's open side is allowed to trade angle
    /// for safety; flattening it to a pure axial step is not, because that is the direction being
    /// quietly deleted.</summary>
    [Fact]
    public void TheReturnOffsetStaysADiagonalAtItsShippedLength()
    {
        Vector3 o = BubbleTestLayout.RoomReturnOffset;
        float plan = new Vector2(o.X, o.Z).Length();
        // REV-2 re-derived this length against all four pads; (3,1,3) overhung two of them.
        Assert.InRange(plan, 1.95f, 2.01f);
        Assert.True(Mathf.Abs(o.X) >= 0.5f,
            $"RoomReturnOffset {o} has no lateral component left — the screen would sit dead ahead "
            + "of a player whose yaw is reset to −Z, which is the beat THRILL §12 directs against.");
        Assert.True(o.Z > 0f,
            $"RoomReturnOffset {o} must step AWAY from the television's face (+Z): every entrance "
            + "is rotated 180° so its screen and its 0.45 m walk-in trigger both face +Z, and a "
            + "return inside that trigger is a player bounced straight back into the room.");
        Assert.True(o.Y > 0f, "the return has to be above the pad, not inside it.");
    }

    // --- Reading the shipped scenes ------------------------------------------------------------

    private const string WorldScenePath = "scenes/game/world/bubbletest/BubbleTest.tscn";

    /// <summary>A section's authored trigger box, world space, straight out of
    /// <c>BubbleTest.tscn</c>: the <c>Volume_*</c> <c>Area3D</c>'s position plus its <c>Shape</c>
    /// child's <c>BoxShape3D</c>. The volumes are direct children of the world root, so no parent
    /// chain is involved and none is assumed.</summary>
    private static Aabb SceneVolumeOf(Tscn world, BubbleTestLayout.Section s)
    {
        string name = BubbleTestLayout.VolumeNameOf(s);
        Vector3 centre = world.WorldPositionOf(name);
        Vector3 size = world.BoxSizeOf($"{name}/Shape");
        return new Aabb(centre - size * 0.5f, size);
    }

    /// <summary>Which section a world-space point belongs to, decided by the SHIPPED volumes in
    /// plan rather than by a name typed into this file. The TV room is excluded because its box is
    /// the room itself, 20 m under the hub, and every surface point is inside the hub in plan.
    /// Asserts uniqueness: overlapping volumes would make a dwell reading ambiguous, which is a
    /// finding in its own right.</summary>
    private static (BubbleTestLayout.Section Section, Aabb Volume) SectionUnder(
        Tscn world, Vector3 p)
    {
        var hits = new List<(BubbleTestLayout.Section, Aabb)>();
        foreach (BubbleTestLayout.Section s in BubbleTestLayout.AllSections)
        {
            if (s == BubbleTestLayout.Section.TvRoom) continue;
            Aabb v = SceneVolumeOf(world, s);
            if (p.X >= v.Position.X && p.X <= v.End.X && p.Z >= v.Position.Z && p.Z <= v.End.Z)
                hits.Add((s, v));
        }
        Assert.True(hits.Count == 1,
            $"{p} is inside {hits.Count} section volumes ({string.Join(", ", hits.Select(h => h.Item1))}) "
            + "— exactly one section must own a point in plan, or its dwell time is ambiguous.");
        return hits[0];
    }

    /// <summary>The authored box a television stands on, world space, read out of that section's
    /// own scene file: the unrotated <c>StaticBody3D</c> + <c>BoxShape3D</c> whose top face is at
    /// the television's Y and whose plan contains it. Where a summit block sits flush under the
    /// perch (red and blue both do — the perch was authored flush with the shipped
    /// <c>SummitBlock</c> top) the WIDEST candidate is the pad, and taking it is the conservative
    /// read: any narrower box under the same top face only adds standing room this test does not
    /// count.</summary>
    private static Aabb PadUnder(BubbleTestLayout.Section section, Vector3 tv)
    {
        Tscn sec = Tscn.Load(
            BubbleTestLayout.ScenePathOf(section)["res://".Length..]);
        Vector3 anchor = BubbleTestLayout.AnchorOf(section);
        Aabb? best = null;
        foreach ((string path, Vector3 size) in sec.Boxes())
        {
            if (sec.IsRotatedAnywhereAlong(path)) continue;
            Vector3 centre = anchor + sec.WorldPositionOf(path);
            var box = new Aabb(centre - size * 0.5f, size);
            if (!Mathf.IsEqualApprox(box.End.Y, tv.Y, 0.01f)) continue;
            if (tv.X < box.Position.X || tv.X > box.End.X) continue;
            if (tv.Z < box.Position.Z || tv.Z > box.End.Z) continue;
            if (best is null || box.Size.X * box.Size.Z > best.Value.Size.X * best.Value.Size.Z)
                best = box;
        }
        Assert.True(best.HasValue,
            $"nothing in {BubbleTestLayout.ScenePathOf(section)} is an unrotated box whose top face "
            + $"is at y={tv.Y} under {tv} — the television is not standing on an authored pad, so "
            + "its Y is a number this level cannot derive (see the note on TvRoutes).");
        return best!.Value;
    }

    /// <summary>
    /// <b>The smallest .tscn reader that answers "what does the shipped file actually say".</b>
    /// Deliberately not a Godot loader: these tests run without an engine (see the class header),
    /// and the whole point of F5 is that a green suite proved nothing about a hand-authored scene.
    ///
    /// <para>It understands exactly four things — <c>BoxShape3D</c> sub-resources, node headers,
    /// <c>position</c>, and whether a node carries a rotation — and it is loud rather than clever
    /// about anything else: an unknown node is simply absent, and every lookup below asserts.
    /// Transforms are treated as translation-only and any node with a <c>rotation</c> or
    /// <c>transform</c> line is flagged, because a yawed box's plan AABB is not its
    /// <c>BoxShape3D</c> size and reading one as if it were would produce plausible garbage
    /// (the MultiMesh-stride trap in <c>.claude/rules/godot-scenes.md</c>, one file over).</para>
    /// </summary>
    private sealed class Tscn
    {
        private static readonly Regex Header = new(@"^\[(sub_resource|node)\s+(.*)\]\s*$");
        private static readonly Regex Attr = new(@"(\w+)=""([^""]*)""");
        private static readonly Regex Vec3 = new(
            @"Vector3\(\s*(-?[\d.eE+-]+)\s*,\s*(-?[\d.eE+-]+)\s*,\s*(-?[\d.eE+-]+)\s*\)");

        private readonly Dictionary<string, Vector3> _boxShapes = new();
        private readonly Dictionary<string, Vector3> _positions = new();
        private readonly Dictionary<string, string> _types = new();
        private readonly Dictionary<string, string> _shapeIds = new();
        private readonly HashSet<string> _rotated = new();
        private readonly string _path = "";

        private Tscn() { }

        private Tscn(string repoRelativePath)
        {
            _path = repoRelativePath;
            string full = Path.Combine(FindRepoRoot(),
                repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"{repoRelativePath} is missing.");

            string kind = "", id = "", nodePath = "";
            foreach (string raw in File.ReadAllLines(full))
            {
                Match h = Header.Match(raw.Trim());
                if (h.Success)
                {
                    kind = h.Groups[1].Value;
                    var a = new Dictionary<string, string>();
                    foreach (Match m in Attr.Matches(h.Groups[2].Value))
                        a[m.Groups[1].Value] = m.Groups[2].Value;
                    if (kind == "sub_resource")
                    {
                        id = a.TryGetValue("type", out string? t) && t == "BoxShape3D"
                            ? a.GetValueOrDefault("id", "") : "";
                        nodePath = "";
                    }
                    else
                    {
                        string name = a.GetValueOrDefault("name", "");
                        string parent = a.GetValueOrDefault("parent", "");
                        nodePath = parent switch
                        {
                            "" => ".",           // the scene root
                            "." => name,
                            _ => $"{parent}/{name}",
                        };
                        _types[nodePath] = a.GetValueOrDefault("type", "");
                        id = "";
                    }
                    continue;
                }

                string line = raw.Trim();
                if (kind == "sub_resource" && id.Length > 0 && line.StartsWith("size ="))
                    _boxShapes[id] = ParseVec3(line);
                else if (kind == "node" && nodePath.Length > 0)
                {
                    if (line.StartsWith("position ="))
                        _positions[nodePath] = ParseVec3(line);
                    else if (line.StartsWith("rotation =") || line.StartsWith("transform ="))
                        _rotated.Add(nodePath);
                    else if (line.StartsWith("shape ="))
                    {
                        Match m = new Regex(@"SubResource\(""([^""]*)""\)").Match(line);
                        if (m.Success) _shapeIds[nodePath] = m.Groups[1].Value;
                    }
                }
            }
        }

        internal static Tscn Load(string repoRelativePath) => new(repoRelativePath);

        /// <summary>A node's position with every ancestor's translation applied. Scene-local: the
        /// caller adds the section anchor, because that lives in the WORLD file, not this one.</summary>
        internal Vector3 WorldPositionOf(string nodePath)
        {
            Assert.True(_types.ContainsKey(nodePath), $"{_path} has no node '{nodePath}'.");
            Vector3 sum = Vector3.Zero;
            string[] parts = nodePath.Split('/');
            for (int i = 0; i < parts.Length; i++)
                sum += _positions.GetValueOrDefault(string.Join('/', parts[..(i + 1)]), Vector3.Zero);
            return sum;
        }

        /// <summary>The <c>BoxShape3D</c> size a <c>CollisionShape3D</c> refers to.</summary>
        internal Vector3 BoxSizeOf(string nodePath)
        {
            Assert.True(_shapeIds.TryGetValue(nodePath, out string? id),
                $"{_path}: '{nodePath}' has no `shape = SubResource(...)` line.");
            Assert.True(_boxShapes.TryGetValue(id!, out Vector3 size),
                $"{_path}: '{nodePath}' points at sub-resource '{id}', which is not a BoxShape3D.");
            return size;
        }

        /// <summary>Every body that owns exactly one box collider, as (body path, box size). The
        /// collider is looked up as the body's own child so a stray shape elsewhere in the file
        /// cannot be mistaken for one.</summary>
        internal IEnumerable<(string Path, Vector3 Size)> Boxes()
        {
            foreach ((string shapePath, string id) in _shapeIds)
            {
                int slash = shapePath.LastIndexOf('/');
                if (slash <= 0) continue;
                string body = shapePath[..slash];
                if (!_types.TryGetValue(body, out string? t) || t != "StaticBody3D") continue;
                if (_boxShapes.TryGetValue(id, out Vector3 size)) yield return (body, size);
            }
        }

        /// <summary>True if the node or any ancestor carries a rotation — such a box's plan AABB is
        /// not its <c>BoxShape3D</c> size, so callers skip it rather than misreading it.</summary>
        internal bool IsRotatedAnywhereAlong(string nodePath)
        {
            string[] parts = nodePath.Split('/');
            for (int i = 0; i < parts.Length; i++)
                if (_rotated.Contains(string.Join('/', parts[..(i + 1)]))) return true;
            return false;
        }

        private static Vector3 ParseVec3(string line)
        {
            Match m = Vec3.Match(line);
            Assert.True(m.Success, $"could not read a Vector3 out of `{line}`.");
            return new Vector3(
                float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
