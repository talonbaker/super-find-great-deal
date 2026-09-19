using Godot;
using MpFoundation.Net;
using Sail.Game.Water;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// <b>The Bubble Test layout contract, as numbers.</b> Program §4 of
/// <c>docs/agents/2026-08-27-bubble-test-program.md</c> is the human copy of this table; this
/// file is the machine copy, and the two are meant to agree literally. Every wave-1 geometry
/// packet (BT-1..5) authors against these anchors, and <see cref="BubbleTestSelfTest"/> asserts
/// the shipped scene still matches them — so a section that drifts is a red test, not a
/// discovery made in a playtest.
///
/// <para><b>Coordinate conventions.</b> North is −Z (repo convention). Ground plane is y = 0
/// unless stated. All distances in metres. Every section scene's root sits at its anchor with
/// identity rotation, and section content is authored in the section's <i>local</i> space, so a
/// section moves by moving its anchor and nothing inside it has to be re-derived.</para>
///
/// <para><b>Footprints are WORLD-space <see cref="Aabb"/>s</b> because that is how program §4
/// states them, and world space is where the one property that matters between sections — that
/// they do not overlap — is expressible. Use <see cref="LocalFootprint"/> for a section's own
/// space, which is what a section file's author works in.</para>
///
/// <para><b>Three of these numbers are CORRECTIONS to program §4, not copies of it</b>
/// (<see cref="RedFootprint"/>, <see cref="CyanFootprint"/> + <see cref="CyanAnchor"/>,
/// <see cref="GreenFootprint"/>): the table as published had red, cyan, blue and green
/// overlapping each other at three of the cross's four diagonal corners. Each correction carries
/// its own reason on the member. BT-1..5 are re-stamped from these values, not from §4 as
/// written.</para>
/// </summary>
public static class BubbleTestLayout
{
    /// <summary>The world id: <c>--world bubbletest</c>, the row in <c>Gameplay.BuildWorld</c>,
    /// and the key <see cref="MpFoundation.Net.VoiceProximityGate"/> reads for D5's per-world
    /// enable. One constant so the three can never disagree. (Program §6 item 15.)</summary>
    public const string WorldId = "bubbletest";

    // --- Anchors (program §4, the "Anchor (world)" column) ---------------------------------

    /// <summary>Hub (gray/white) — the plaza, the seating, the counter pedestal, every spawn.
    /// Program §4.</summary>
    public static readonly Vector3 HubAnchor = new(0f, 0f, 0f);

    /// <summary>Red — the climbable cairns, due north. Program §4.</summary>
    public static readonly Vector3 RedAnchor = new(0f, 0f, -95f);

    /// <summary>Blue — the vertical precision tower and the balance beam, due east.
    /// Program §4.</summary>
    public static readonly Vector3 BlueAnchor = new(95f, 0f, 0f);

    /// <summary>Cyan — the wide flat run and the densest bubbles, due south. Program §4 as
    /// CORRECTED by BT-0: 100, not 95. See <see cref="CyanFootprint"/>.</summary>
    public static readonly Vector3 CyanAnchor = new(0f, 0f, 100f);

    /// <summary>Green — rolling hills, the lake and the Postpile island, due west.
    /// Program §4.</summary>
    public static readonly Vector3 GreenAnchor = new(-95f, 0f, 0f);

    /// <summary>The tangle — zone 8 of the shader lab's terrain playground, ported by TANGLE-1.
    /// <b>The first section that is not on program §4's cross</b>, because all four cardinal arms
    /// were taken and the pile had to go somewhere.
    ///
    /// <para><b>Why this diagonal, and why exactly here.</b> Two constraints fix it almost
    /// completely. The footprint must clear red's east edge (x = 45) and blue's north edge
    /// (z = −50), which puts the nearest legal corner at (45, −50); and
    /// <c>LocalFootprintsRoundTripThroughTheAnchorAndAreCentredOnIt</c> requires a footprint
    /// centred on its anchor, so the smallest box that holds the pile — 60 m, against those two
    /// edges — lands its centre at exactly (75, −80). Every metre further out costs sight of the
    /// pile from the hub and buys nothing. It touches red on one edge and blue on another, which
    /// is the same touching-but-not-overlapping arrangement BT-0 left the cross in.</para>
    ///
    /// <para><b>The NE quadrant rather than the other three.</b> NE is the only diagonal whose two
    /// neighbours are the level's two CLIMBS (red's cairns at 28 m, blue's tower at 42 m): a third
    /// climb between them groups the vertical half of the level on one side and leaves cyan's flat
    /// run beside green's hills on the other. It is also the diagonal a player can see down — the
    /// hub's four connectors point at the cardinals, so a section on a diagonal is framed by the
    /// gap between two arms rather than standing behind one.</para></summary>
    public static readonly Vector3 TangleAnchor = new(75f, 0f, -80f);

    /// <summary>The TV room — the easter egg's sealed alternate zone, straight down. Program §4;
    /// <b>Moved from −40 to −20 on 2026-08-28 (Talon: "put it above the kill plane").</b> There
    /// are TWO out-of-bounds floors and §4 rule 3 originally reasoned about only one:
    /// <see cref="VoidKillY"/> (−70, per-world) AND <c>NetProfile.KillPlaneY</c> (−30, a global
    /// const that <c>SandboxAvatar.ServerTick</c> enforces every tick). At −40 the room sat below
    /// the second one, so the server teleported a player in and reset them to spawn on the next
    /// tick — invisible offline, because ServerTick only runs in ServerSim. At −20 the room is
    /// above KillPlaneY, and a player who falls THROUGH its floor is still recovered (by
    /// KillPlaneY now, rather than by VoidKillY). Guarded by TvPortalSelfTest.</summary>
    public static readonly Vector3 TvRoomAnchor = new(0f, -20f, 0f);

    // --- Footprints, world space (program §4, the "Footprint (world)" column) ---------------

    /// <summary>Hub: x −40..40, z −40..40. Program §4.</summary>
    public static readonly Aabb HubFootprint = FromBounds(-40f, 40f, -40f, 40f);

    /// <summary>The four 12 m connector corridors, world space. They belong to the HUB file
    /// (§4 rule 4 — "the path belongs to the hub file so section files never touch the hub's
    /// footprint") but lie OUTSIDE <see cref="HubFootprint"/>, in the gaps between footprints
    /// that no section claims — which is why admitting them cannot re-open BT-0's overlap
    /// correction. Without them the hub cannot satisfy §4 rule 4 and the self-test's footprint
    /// check at the same time. Ruled by the orchestrator on BT-5's escalation, 2026-08-28.</summary>
    public static readonly Aabb[] HubConnectors =
    {
        FromBounds(-6f, 6f, -45f, -40f),    // to red   (north)
        FromBounds(40f, 45f, -6f, 6f),      // to blue  (east)
        FromBounds(-6f, 6f, 40f, 50f),      // to cyan  (south)
        FromBounds(-50f, -40f, -6f, 6f),    // to green (west)
    };

    /// <summary>Red: x −45..45, z −145..−45.
    ///
    /// <para><b>CORRECTED by BT-0 (was x −50..50).</b> Program §4's table as written had red
    /// reaching x = 50 while blue started at x = 45, so the two overlapped in a 5 × 5 m square at
    /// their shared corner — two section files authoring the same ground, two owners under D3's
    /// "one section, one file, one owner", z-fighting slabs, and a player standing there inside
    /// two <see cref="SectionVolume"/>s at once. Caught by
    /// <c>BubbleTestLayoutTests.NoTwoSectionFootprintsOverlapInPlan</c>. The cross is now
    /// separated on every diagonal with two edges merely touching; see that test for the
    /// proof.</para></summary>
    public static readonly Aabb RedFootprint = FromBounds(-45f, 45f, -145f, -45f);

    /// <summary>Blue: x 45..145, z −50..50. Program §4.</summary>
    public static readonly Aabb BlueFootprint = FromBounds(45f, 145f, -50f, 50f);

    /// <summary>Cyan: x −70..70, z 50..150 — the widest section, because §11's brief asks for
    /// "wide open flat terrain to test pure acceleration/turning/skid feel".
    ///
    /// <para><b>CORRECTED by BT-0 (was z 45..145, anchor 95).</b> At ±70 wide, cyan reached into
    /// both blue's and green's near ends and overlapped each in a corner. Pushing cyan 5 m further
    /// out — rather than narrowing it — is what keeps the brief's "wide open" intact: the arm is
    /// still 140 × 100, it is still the largest open ground in the level, and the cost is a 10 m
    /// connector instead of a 5 m one. The anchor moved with it so the footprint stays centred on
    /// its anchor, which is what section authors work in.</para></summary>
    public static readonly Aabb CyanFootprint = FromBounds(-70f, 70f, 50f, 150f);

    /// <summary>Green: x −140..−50, z −50..50.
    ///
    /// <para><b>CORRECTED by BT-0 (was x −145..−50).</b> §4's original 95 m width left green
    /// off-centre on its own anchor by 2.5 m, which is exactly the sort of thing a section author
    /// assumes away; trimming the far edge to −140 makes it 90 m and centred, and costs nothing
    /// because that edge faces the map boundary, not a neighbour.</para>
    ///
    /// <para><b>The EAST edge is the load-bearing one and it did not move.</b> It is pinned at
    /// exactly x = −50 so the whole green section sits 5 m inside
    /// <see cref="WaterGeometry.ShoreX"/> (−45) — the x below which the shipped <c>DepthAt()</c>
    /// starts calling a sub-<see cref="WaterGeometry.WaterY"/> point "lake". Moving this edge east
    /// re-opens the unmerged per-world water field that D4 exists to avoid needing.</para></summary>
    public static readonly Aabb GreenFootprint = FromBounds(-140f, -50f, -50f, 50f);

    /// <summary>Tangle: x 45..105, z −110..−50 — 60 × 60, the smallest square that holds the
    /// ported pile with room to fall off it.
    ///
    /// <para><b>Both near edges are load-bearing and neither has slack.</b> x = 45 is red's east
    /// edge and z = −50 is blue's north edge; the footprint touches each exactly, which
    /// <c>NoTwoSectionFootprintsOverlapInPlan</c> permits and one centimetre inward would not.
    /// The pile's own mesh AABB spans 40.7 × 49.8 m and is centred on the anchor by the port
    /// (<c>tools/dev/tangle_port.py --shift</c>), so the tightest margin between geometry and
    /// footprint is 5.1 m in z — enough that a player knocked off the spine lands on the section's
    /// own ground plate rather than on nothing.</para></summary>
    public static readonly Aabb TangleFootprint = FromBounds(45f, 105f, -110f, -50f);

    // --- LabRoof: the plot the puffin lab sits in (ROOFTOP-1) --------------------------------

    /// <summary>
    /// <b>The seventh section, and the smallest one: the ground the puffin lab occupies.</b>
    ///
    /// <para><b>Why this exists, plainly.</b> ROOFTOP-1 put a television on the lab's roof, at
    /// (88, −6.39, 91). <c>BubbleTestLayoutTests.EveryTelevisionStandsInsideItsOwnSectionsVolume
    /// InTheShippedScene</c> requires every row of <see cref="TvRoutes"/> to stand inside exactly
    /// ONE section volume in plan, and its failure message says why: <i>"a player on that pad is
    /// attributed to NO section, so the dwell log reports the opposite of the climb they just
    /// made."</i> The lab's plot was inside no footprint at all — cyan stops at x = 70, blue stops
    /// at z = 50, and the lab is at (90, 90). The test was right to fail: the roof is a place
    /// players stand and this level had no word for it.</para>
    ///
    /// <para><b>Why a new section rather than growing cyan.</b> Growing cyan east was the two-line
    /// fix and it is not available: <c>LocalFootprintsRoundTripThroughTheAnchorAndAreCentredOnIt</c>
    /// requires every non-TV-room footprint to stay centred on its own anchor, so reaching x = 102
    /// costs a symmetric 204 m-wide cyan claiming 32 m of empty void to the WEST as well — a lie
    /// about ground ownership in two directions to fix an attribution in one. A section is what
    /// this level calls a place; the lab's plot is a place.</para>
    ///
    /// <para><b>Its anchor is at grade and its only geometry is 6.4 m under grade</b>, which reads
    /// as a contradiction and is not one: <c>NoSectionSitsBelowGroundExceptUnderTheLakeAndTheTv
    /// Room</c> holds every surface section's anchor at y = 0, and this is a surface section whose
    /// surface happens to be a roof in a hole. <see cref="LabRoofMaxHeight"/> says nothing here
    /// rises above grade, and that is true.</para></summary>
    public static readonly Vector3 LabRoofAnchor = new(145f, 0f, 77.5f);

    /// <summary>
    /// <b>LabRoof's plan: x 114..176, z 68..87, centred on <see cref="LabRoofAnchor"/>.</b> It
    /// covers the two surfaces at the END of Talon's route — the escape hall's ceiling, which is
    /// the "little tunnel" a player walks across (<c>Route/HallCeil</c>, x 115.81..171.83,
    /// z 69.58..74.54, top y = −16.070), and the void chamber's roof they climb onto at the end of
    /// it (<c>Route/VoidCeil</c>, x 162.04..173.90, z 74.13..85.45, top y = −12.206), which is
    /// where the seventh television stands.
    ///
    /// <para><b>It does NOT cover the landing roof</b> (the lab's own ceiling at x 78.55..101.45,
    /// z 81.31..98.69, y = −6.410), and that is a stated limitation rather than an oversight: a
    /// footprint spanning both would be 100 m wide to hold two slabs 78 m apart, and no television
    /// stands on the landing roof, which is the only thing a section volume is REQUIRED to hold
    /// (<c>EveryTelevisionStandsInsideItsOwnSectionsVolumeInTheShippedScene</c>). A player on the
    /// landing roof still reads as no section, exactly as they did before this packet.</para>
    ///
    /// <para>Overlaps nothing: blue ends at z = 50, cyan ends at x = 70, the tangle ends at
    /// z = −50, and the whole box is inside <see cref="OffMapRadiusM"/> (176 &lt; 210).</para>
    /// </summary>
    public static readonly Aabb LabRoofFootprint = FromBounds(114f, 176f, 68f, 87f);

    /// <summary>
    /// <b>Where LabRoof's section volume starts, in y — the one section that cannot use
    /// <see cref="SectionVolumeBottomY"/>.</b>
    ///
    /// <para>Every other section's column starts at −10 because every other section's ground is at
    /// 0. This one's surfaces are 12 and 16 m UNDER grade, in a plot of the map with no ground at
    /// all, so a −10 floor would put the seventh television 2.2 m below its own section and the
    /// dwell log would report the opposite of the traversal a player just made — the exact defect
    /// <c>SectionVolumesReachTheTopOfTheirOwnSection</c> was written for, one section over.</para>
    ///
    /// <para><b>−17 is a boundary, not a margin.</b> It is below the tunnel a player WALKS on
    /// (<c>Route/HallCeil</c>, −16.070) and above the floor of the black room UNDER that tunnel
    /// (<c>Route/Terminus</c>, −17.588), so this volume holds everyone on TOP of the lab's escape
    /// route and nobody inside it. The lab's interior is EGG-1's and has its own suite; it is not
    /// this section's to claim.</para></summary>
    public const float LabRoofVolumeBottomY = -17f;

    /// <summary>Ceiling on LabRoof's content above its anchor: 1 m, i.e. nothing. The section's one
    /// authored thing is the television's pad at <see cref="LabRoofPadTopY"/>, 6.39 m BELOW the
    /// anchor. If anything here ever rises above grade it should have to move this number and say
    /// why.</summary>
    public const float LabRoofMaxHeight = 1f;

    /// <summary>LabRoof carries no bubbles. The lab's ten are inside <c>PuffinLab.tscn</c>, which
    /// is not a section (<see cref="PuffinLabBubbles"/>), and the hundredth is on the diving board
    /// in the sky. A section with a zero here still gets counted every run by
    /// <c>BubbleTestSelfTest.CheckBubbleCensus</c>, which is the point of counting.</summary>
    public const int LabRoofBubbles = 0;

    /// <summary>Half the TV room section's plan extent, either side of the anchor. LEVEL-4,
    /// 2026-08-29: it was 6 (a single 12 × 12 room) and is now 25, because the section holds
    /// <see cref="TvRoutes"/>'s seven rooms rather than one.
    ///
    /// <para><b>"five" until ROOFTOP-1, 2026-09-05</b>, which is the second time this sentence has
    /// gone stale behind the array (BUBBLE-1 found the other one on <see cref="GoldenCubes"/>).
    /// Count <c>TvRoutes.Length</c>; never retype it. The seventh room, <c>RoomG</c>, is the sky
    /// deck — see <see cref="SkyDeckRoomCentre"/> — and it is the reason
    /// <see cref="TvRoomCeilingM"/> exists.</para></summary>
    public const float TvRoomPlanHalf = 25f;

    /// <summary>The TV room section's sealed underground plan: <see cref="TvRoomPlanHalf"/> either
    /// side of the anchor, <see cref="TvRoomHeight"/> tall, floor at <see cref="TvRoomAnchor"/>.Y.
    /// Program §4 as WIDENED by LEVEL-4 (Talon's note 9, 2026-08-29: <i>"make each TV area take
    /// them to a slightly different room"</i>).
    ///
    /// <para><b>Why widening THIS footprint is safe where widening any other is not.</b>
    /// <c>NoTwoSectionFootprintsOverlapInPlan</c> and
    /// <c>HubConnectorsTouchTheirSectionAndOverlapNoFootprint</c> both skip the TV room by name,
    /// because it shares no ground with anything — it is 20 m under the hub. The five surface
    /// sections tile the plan with 5–10 m seams and cannot grow or shrink by a metre without a
    /// plate resize; see the note-8 finding in
    /// <c>docs/levels/2026-08-29-LEVEL-4-section-spacing.md</c>. The room section is the one place
    /// in this level with free space, and it is free precisely because nobody can see it.</para>
    ///
    /// <para><b>The floor is DERIVED from the anchor, and must stay derived.</b> It was a retyped
    /// −40 while the anchor moved to −20 on 2026-08-28, so this footprint — which
    /// <see cref="VolumeOf"/> returns verbatim for the TV room — described a volume 20 m below the
    /// room it was supposed to bound. Nothing went red, because <c>CheckFootprints</c> reads only
    /// X/Z and takes its ceiling from <see cref="AnchorOf"/>, and
    /// <c>SectionVolumesCoverTheirFootprints</c> asserted <c>Size.Y</c> but never
    /// <c>Position.Y</c> (it does now). This wave lost four separate defects to a constant that
    /// was copied rather than derived; do not reintroduce a literal here.</para></summary>
    public static readonly Aabb TvRoomFootprint =
        new(new Vector3(-TvRoomPlanHalf, TvRoomAnchor.Y, -TvRoomPlanHalf),
            new Vector3(TvRoomPlanHalf * 2f, TvRoomHeight, TvRoomPlanHalf * 2f));

    // --- The TV routes (LEVEL-4, Talon's note 9) --------------------------------------------

    /// <summary>One television and the room behind it. <b>The single table three files read</b>:
    /// <c>BubbleTestWorld</c> builds the entrance from it, <c>TvRoom.tscn</c> authors the room at
    /// <see cref="RoomCentre"/>, and <c>TvPortalSelfTest</c> derives what it expects to find
    /// instead of asserting a literal count of three. Adding a seventh route is a row here, a room
    /// in the scene file, and nothing else — which is the property BT-10's own report asked for
    /// after the count-of-three assertion had to be edited by hand. ROOFTOP-1 added the seventh
    /// and that promise held: one row, one room, no third place.</summary>
    /// <param name="EntranceName">Node name of the entrance <c>TvPortal</c>, a direct child of the
    /// world. <c>TvPortalSelfTest</c> and <c>TvPortalHost</c> both find portals by walking the
    /// tree, so this is identity, not decoration.</param>
    /// <param name="EntrancePos">Where the entrance stands, WORLD space, base on the ground. Every
    /// one of these is on a flat authored ground plate at y = 0 and GUARD-1 measures that with a
    /// ray — see <c>BubbleTestSelfTest.CheckPropHeights</c>.</param>
    /// <param name="ReturnTvPath">The room's own television, as a path under the
    /// <see cref="Section.TvRoom"/> section node.</param>
    /// <param name="RoomCentre">The room's centre, LOCAL to the TV room section. The Den keeps
    /// (0,0,0) so BT-10's directed arrival, its crooked frames and its return TV are untouched.</param>
    public readonly record struct TvRoute(
        string EntranceName, Vector3 EntrancePos, string ReturnTvPath, Vector3 RoomCentre);

    /// <summary>Where a traveller lands inside a room, relative to that room's centre: 0.4 m above
    /// the floor and 3 m toward the room's south side. <b>Ported unchanged from BT-10's directed
    /// value</b> — a teleport resets <c>MoveState.Yaw</c> to 0 (−Z), so the arriving player is
    /// looking INTO the room with the way out behind them, and turning is the cost (THRILL §7.1).
    /// Every room repeats the same geometry so the rule is one rule, not five.</summary>
    public static readonly Vector3 RoomArrivalOffset = new(0f, 0.4f, 3f);

    /// <summary>Where a room's television puts you, relative to the entrance you came in by:
    /// 1.85 m south and 0.7 m east of it, standing.
    ///
    /// <para><b>ROTATED at MRF-B / F6 (2026-08-30) — same length, new bearing.</b> The 1.98 m plan
    /// length is untouched and deliberately so: REV-2 re-derived it against all four pads and the
    /// old (3, 1, 3) overhung two of them, so the length is load-bearing evidence rather than a
    /// preference. What was wrong was the BEARING. Every television stands 1.2 m back (−Z) from the
    /// middle of its pad, so a 45° diagonal spent 1.4 m of a 2.5 m half-width going sideways and
    /// put the returning player <b>1.10 m</b> from the lip of a 22.8 m (red) or 41.2 m (blue) drop
    /// — 1.60 m on the spur pad, 2.61 m on the summit cap. Yaw resets to 0 on every teleport and
    /// forward is typically still held from walking into the screen, so the reward at the end of a
    /// 41–61 m climb was one held key from costing the whole climb.</para>
    ///
    /// <para>(0.7, 1.85) is the bearing that maximises the SMALLEST edge distance at this length:
    /// on the 5 m pads the two binding constraints — the side lip at <c>2.5 − x</c> and the far lip
    /// at <c>2.5 + 1.2 − z</c> — meet at x ≈ 0.665, z ≈ 1.865, and the shipped pair is that
    /// rounded. Measured against the pads as authored, the four returns go
    /// <b>1.10 → 1.80, 1.10 → 1.80, 1.60 → 2.30, 2.61 → 3.30 m</b> of clearance, against a
    /// <b>1.46 m</b> bar (half a second of held forward at the shipped ramp, 1.10 m, plus the
    /// 0.36 m body radius). <c>BubbleTestLayoutTests.EveryTvReturnStandsClearOfItsPadsEdge</c>
    /// reads both the pads and this constant out of the shipped files and re-derives that every
    /// run — the old value fails it.</para>
    ///
    /// <para><b>What this does NOT fix.</b> The yaw itself. A returning player still faces −Z
    /// rather than the pad's middle, because <c>SandboxAvatar.ServerTeleportTo</c> takes a position
    /// and rebuilds the state through <c>MoveState.AtSpawn</c>, which resets <c>Yaw</c> to 0; there
    /// is no yaw channel from a portal to a teleport, and adding one is <c>SandboxAvatar</c>'s
    /// file, not this level's. Reported as a fork rather than improvised — see
    /// <c>docs/agents/roles/environment/outbox/2026-08-30-MRF-B-volumes-and-returns.md</c>.</para>
    ///
    /// <para><i>The paragraphs below are W7-1's, kept because their reasoning still binds; where
    /// they say "1.4 m east" read 0.7, and where they say the diagonal, read the rotated one.</i></para>
    ///
    /// <para><b>3 m → 1.4 m at W7-1, and this is a safety change rather than a taste one.</b>
    /// Talon's note 8 moved every non-hub television onto a summit — a 5 m perch on red and blue,
    /// a 6 m spur pad and an 8 m summit cap on the tangle spire. A 4.2 m diagonal from a
    /// television standing 1.2 m back from the middle of a 5 m pad lands the returning player
    /// 3 m PAST its edge, i.e. off a 41 m tower. 1.98 m keeps all three of the original reasons
    /// intact — it is still far outside the 0.45 m-deep walk-in trigger (so the return is not
    /// swallowed by the screen it came out of), it is still a diagonal so the lit screen sits off
    /// to one side rather than filling the frame, and it is still derived from the entrance rather
    /// than typed per room — while fitting inside the smallest pad in the level with 1.6 m to
    /// spare. Every pad this offset has to fit is authored by this same packet, which is what
    /// makes one constant safe for all four.</para>
    ///
    /// <para><b>Three separate things pick this offset, and none of them is taste.</b> (1) The
    /// entrance's walk-in <c>Area3D</c> is 0.45 m deep and hugs its screen, so 4.2 m diagonal is
    /// far outside it — a returning player is not swallowed by the screen they just came out of,
    /// which the 800 ms cooldown would only have hidden for 800 ms. (2) Yaw resets to 0, i.e. −Z,
    /// and every entrance faces south, so the returning player is looking north with the lit
    /// screen off to one side rather than filling the frame: the beat is the world declining to
    /// acknowledge the trip (THRILL §12), and that needs the unchanged prop in shot, not in the
    /// way. (3) It is derived from the entrance rather than typed per room, so a TV that moves
    /// takes its exit with it.</para>
    ///
    /// <para><b>The hub is the exception</b> and keeps <c>BubbleTestWorld.HubReturn</c>: BT-10
    /// directed that one specifically at the spawn ring.</para></summary>
    public static readonly Vector3 RoomReturnOffset = new(0.7f, 1f, 1.85f);

    // --- The secret bubble (EGG-2, Talon's addendum §3) --------------------------------------

    /// <summary>
    /// <b>How far out from the lake's centre the secret bubble sits, on the NORTH side of the
    /// postpile island</b> — the opposite side from the sunken television, deliberately.
    ///
    /// <para><b>"Hidden somewhere clever" means somewhere with no other reason to be.</b> The
    /// island's far shore is the one place in this level a player reaches only by choosing to: the
    /// stepping stones land you on the south beach, the postpile is the thing you came for, and
    /// walking round to the back of it pays nothing. So that is where the level's one non-collectible
    /// goes, and what it costs is a swim into the water that kills you.</para>
    ///
    /// <para><b>The swim, as numbers.</b> The bed crosses the submerged contour at r = 8.766 on the
    /// island side, so with the bubble at r = 10.0 and a 0.48 m collider its near face is at 9.52:
    /// <b>0.75 m</b> of swimming against <c>REACH_BEST_M</c> 4.40 m and <c>REACH_WORST_M</c> 3.00 m
    /// (<c>tools/dev/bubbletest_bake_green.gd</c>, both measured in-engine against the real motor and
    /// the shipped <c>DrowningClock</c>). That is deliberately generous where the sunken television
    /// is deliberately not: the television's beat is the risk, and this one's beat is the FINDING.
    /// Making the reward for noticing something also a coin-flip on drowning would have punished the
    /// only behaviour it is trying to reward.</para></summary>
    public const float SecretBubbleRadiusM = 10.0f;

    /// <summary>
    /// Where the secret bubble hangs, world space. <b>Its Y is derived from its own collider</b>:
    /// the underside of the sphere rests exactly on <see cref="WaterGeometry.SwimLineY"/>, which is
    /// the fixed depth <c>AvatarMotor.Step</c> clamps a swimming body to — so a swimmer's feet
    /// enter it, whatever the avatar's height turns out to be. Same defect this level nearly shipped
    /// with the sunken television: a collectible authored at a plausible depth in water nobody can
    /// dive in is a collectible nobody can collect.
    ///
    /// <para>Fully under the surface at −0.73 m against a <see cref="WaterGeometry.WaterY"/> of
    /// −0.58, so it reads as a light UNDER the water rather than as something bobbing on it — and
    /// 1.40 m clear of the bed at that radius (−2.75), so it floats the way every bubble in this
    /// level floats.</para></summary>
    public static readonly Vector3 SecretBubblePos = new(
        WaterGeometry.BubbleTestLakeCentreX,
        WaterGeometry.SwimLineY + Sail.Game.Bubble.SecretBubble.ColliderRadiusM,
        WaterGeometry.BubbleTestLakeCentreZ - SecretBubbleRadiusM);

    // --- The sixth television, under the lake (EGG-2, Talon's addendum §6) -------------------

    /// <summary>Node name of the television at the bottom of green's lake. Named here, and read
    /// by <see cref="TvRoutes"/>, <c>BubbleTestWorld.ReturnFor</c> and <c>TvPortalSelfTest</c> —
    /// two spellings of one node is the second-copy trap this file has already paid for.</summary>
    public const string SunkenTvNodeName = "SunkenTv";

    /// <summary>
    /// <b>How far a <see cref="MpFoundation.Game.World.TvPortal"/>'s walk-in trigger reaches below
    /// the node's own base</b>, metres. The trigger is a 1.2 m-tall box centred 0.7 m up
    /// (<c>TvPortal.TriggerPosition</c>/<c>TriggerSize</c>, both exported defaults), so its floor
    /// is 0.10 m above the base.
    ///
    /// <para>Mirrored here rather than read off the class because those are <c>[Export]</c>
    /// defaults on a <c>Node3D</c> and this file must stay engine-free. The mirror is not left on
    /// trust: <c>TvPortalSelfTest</c> reads the LIVE portal's trigger and asserts the swim line
    /// falls inside its vertical span, so a retune of either side is a red test rather than a
    /// television nobody can reach.</para></summary>
    public const float TvTriggerFloorAboveBaseM = 0.10f;

    /// <summary>Clearance between the trigger's floor and <see cref="WaterGeometry.SwimLineY"/> —
    /// how far below a floating swimmer's feet the trigger's bottom edge sits.</summary>
    public const float SunkenTvSwimClearanceM = 0.12f;

    /// <summary>
    /// <b>The base Y of the sunken television, DERIVED — and the whole reason this constant
    /// exists rather than a literal.</b>
    ///
    /// <para>A swimming body does not sink. <c>AvatarMotor.Step</c> clamps a swimmer's vertical
    /// velocity onto <see cref="WaterGeometry.SwimLineY"/> (<c>WaterY − SwimSubmersionM</c>,
    /// −1.83 m) every tick — "horizontal-only (spec §4), NOT buoyancy and NOT a force" — so
    /// <b>there is no diving in this game</b>. A television standing on the lake bed at −3.27 m
    /// would have its walk-in trigger a metre and a half under the deepest a player can ever get,
    /// and the easter egg would be unreachable in exactly the silent way this repo keeps shipping:
    /// present, correct-looking, and impossible.</para>
    ///
    /// <para>So the television is raised on an authored plinth until its trigger straddles the
    /// swim line: base = swim line − <see cref="SunkenTvSwimClearanceM"/> −
    /// <see cref="TvTriggerFloorAboveBaseM"/> = <b>−2.05 m</b>, which puts the trigger's 1.2 m box
    /// at −1.95 .. −0.75 with the swimmer's feet at −1.83 inside it. It is still well below
    /// <see cref="WaterGeometry.WaterY"/> (−0.58), which is the packet's own bar for "underwater",
    /// and still deep enough that a body standing on the plinth top reads as submerged
    /// (depth 1.47 &gt; <c>SubmergedDepthM</c> 1.15) — so the plinth is not a place to catch your
    /// breath.</para></summary>
    public static readonly float SunkenTvBaseY =
        WaterGeometry.SwimLineY - SunkenTvSwimClearanceM - TvTriggerFloorAboveBaseM;

    /// <summary>
    /// <b>How far out into the lake the sunken television stands</b>, metres from
    /// <see cref="GreenAnchor"/> — which is the lake's centre
    /// (<see cref="WaterGeometry.BubbleTestLakeCentreX"/>).
    ///
    /// <para><b>10.65 is the annulus midpoint, and it is measured rather than chosen for looks.</b>
    /// Green's bed is a bowl with the postpile island in the middle, so the deep water is an
    /// ANNULUS: <c>tools/dev/bubbletest_bake_green.gd</c>'s <c>BED</c> table crosses the submerged
    /// contour (bed y = <c>WaterY − SubmergedDepthM</c> = −1.73) at r = <b>8.766</b> on the island
    /// side and at r = <b>13.888</b> on the bank side, a 5.12 m band the bake asserts is at least
    /// <c>ANNULUS_MIN_M</c> (4.90) wide precisely so it cannot be swum across.</para>
    ///
    /// <para><b>The swim, as numbers.</b> Every entrance faces south and a player enters travelling
    /// −Z (see <c>BubbleTestWorld.SetUpTvPortals</c>), so the approach is from the south bank
    /// inward, and the trigger's leading face sits 0.645 m south of the node: <b>13.888 − 11.295 =
    /// 2.593 m of swimming</b> from the moment the bed drops past the submerged contour, which is
    /// the same moment <c>DrowningClock</c> starts. Against the bake's own in-engine reach
    /// measurements — <c>REACH_BEST_M</c> 4.40 m for a dry unencumbered body in
    /// <c>DrownAfterSec</c> (3.0 s), <c>REACH_WORST_M</c> 3.00 m carrying and Soaked — that is
    /// <b>1.81 m of margin at best and 0.41 m at worst</b>: passable by every loadout in the level,
    /// and not by much.</para>
    ///
    /// <para><b>What makes it a risk rather than a walk.</b> The screen faces south like every
    /// other television here, so a player who comes at it from the island side meets the cabinet
    /// hull and has to swim around — an arc of roughly 3.4 m ON TOP of the approach, past even the
    /// best reach. Missing it is what drowns you; the swim itself is winnable.</para></summary>
    public const float SunkenTvRadiusM = 10.65f;

    /// <summary>Where the sunken television stands, world space. Derived from the lake's own
    /// centre and the two constants above so a shore retune moves the television with it.</summary>
    public static readonly Vector3 SunkenTvPos = new(
        WaterGeometry.BubbleTestLakeCentreX,
        SunkenTvBaseY,
        WaterGeometry.BubbleTestLakeCentreZ + SunkenTvRadiusM);

    /// <summary>
    /// <b>Where the Deep Room puts you back: the south bank, on dry land.</b>
    ///
    /// <para>The sunken television is the one route whose entrance a player <i>cannot be returned
    /// to</i>. <see cref="RoomReturnOffset"/> would land them at (−94.3, −1.05, 12.50) — 0.47 m
    /// under the waterline, out of their depth, with a fresh three-second clock and no idea which
    /// way the bank is. "Reward room, then drown" is the exact class of unhandled implication this
    /// level's own history is made of, so this route takes the hub's escape hatch: a named return,
    /// like <c>BubbleTestWorld.HubReturn</c>.</para>
    ///
    /// <para>1 m of lift over the lake's own rim: <see cref="WaterGeometry.BubbleTestLakeRadiusM"/>
    /// (17.0) plus a metre puts it at r = 18, which is exactly the bake's <c>BASIN_R</c> — the
    /// radius at which the bowl profile returns to y = 0 and the hills have not yet faded in. Dry
    /// by both tests that matter: above <see cref="WaterGeometry.WaterY"/>, and outside the
    /// swimmable disc entirely. <c>TvPortalSelfTest</c> rays it rather than trusting this
    /// paragraph.</para></summary>
    public static readonly Vector3 SunkenTvReturn = new(
        WaterGeometry.BubbleTestLakeCentreX,
        1f,
        WaterGeometry.BubbleTestLakeCentreZ + WaterGeometry.BubbleTestLakeRadiusM + 1f);

    // --- The picture on the Deep Room's wall (FRAME-1, Talon 2026-09-04) ---------------------
    //
    // Talon, 2026-09-04: "the TV in the lake, the room that it takes you ... it's nothing. It's
    // nothing special ... where if you know about it and you find it, you should be rewarded with
    // something sort of fantastic. ... maybe there is a picture frame and or like a drawing on the
    // wall and the drawing can be of something special that the play testers would know and they
    // would appreciate. ... Can you put if I gave you a drawing to put on the wall a PNG with
    // transparency?"
    //
    // The answer was yes, and on the same day he supplied the drawing: a crayon "fwends" on a
    // torn sheet of paper with masking tape at the top, four televisions and four figures under
    // them - this game's own TV room, drawn by hand. Talon, on size: "Please made it large enough
    // to at least the player can see it."
    //
    // IT IS NOT IN A PICTURE FRAME, and that is a decision the artwork made rather than a
    // shortcut. The alpha IS the shape: the paper's own torn edge and its own drawn tape are in
    // the image, so a rectangular moulding around it would box a thing that already draws its own
    // edge and would hide the transparency the whole request was about. It hangs as what it is, a
    // sheet taped to the wall, lit by one lamp.
    //
    // The constants below are what keep this a ONE FILE swap: replace the PNG and it is refitted
    // to its own aspect, alpha respected, with no code change and no scene edit.

    /// <summary>
    /// <b>*** TALON'S DRAWING LIVES HERE: <c>resources/SecretRoomPicture.png</c> ***</b>
    /// (i.e. <c>&lt;repo&gt;/resources/SecretRoomPicture.png</c> in a checkout).
    ///
    /// <para><b>The shipped file is his own crayon drawing</b> — "fwends", four televisions and
    /// four figures on a torn sheet of paper with a strip of masking tape at the top, 1388 x 1053
    /// with a genuine per-pixel alpha channel (24.1% of it fully clear, 0.49% partial along the
    /// torn edge). It is this game's own TV room, drawn by hand, which is why it hangs in the room
    /// only the drowning swim reaches.</para>
    ///
    /// <para><b>Swapping it is still a ONE FILE operation.</b> Replace the PNG and nothing in
    /// code moves: <c>BubbleTestWorld.SetUpSecretPicture</c> reads the new image's own dimensions,
    /// refits the sheet to its aspect inside <see cref="SecretPictureMountM"/>, and hangs it.
    /// Any aspect ratio, any size; keep the long edge at 2048 px or under —
    /// <c>ART-BIBLE.md</c> §4.4 budgets around 1.4 MB of texture for the whole game world and this
    /// image is the named exception to it (see the report).</para>
    ///
    /// <para><b>Checked with <c>ResourceLoader.Exists</c> rather than assumed present</b>, the
    /// same pattern <see cref="MpFoundation.Ui.Branding.WordmarkImagePath"/> sets, and the guard
    /// is kept now that the real file ships: delete or rename the PNG and the wall goes back to a
    /// lit, empty mount under its lamp with a clean log, rather than to an engine error.</para>
    /// </summary>
    public const string SecretPictureImagePath = "res://resources/SecretRoomPicture.png";

    /// <summary>Node path of the picture, under the <see cref="Section.TvRoom"/> section node.
    /// One spelling, read by the scene file's author, <c>BubbleTestWorld.SetUpSecretPicture</c>
    /// and <c>BubbleTestSelfTest</c> — the second-copy trap this file has paid for twice.
    ///
    /// <para><c>RoomF</c> is the Deep Room, and it is the room behind the SUNKEN television:
    /// the last row of <see cref="TvRoutes"/> is keyed on <see cref="SunkenTvNodeName"/>, stands
    /// at <see cref="SunkenTvPos"/> (the lake's own centre, 2 m under the surface) and names
    /// <c>RoomF/RoomTv</c> as its way out.</para></summary>
    public const string SecretPicturePath = "RoomF/SecretPicture";

    /// <summary>Name of the sheet inside <see cref="SecretPicturePath"/> — the one node the
    /// image lands on.</summary>
    public const string SecretPictureCanvasName = "Canvas";

    /// <summary>Width of RoomF's north wall, metres — mirrored from <c>TvRoom.tscn</c>'s
    /// <c>BoxMesh_wallx_F</c>, which is the authority. Only <see cref="SecretPictureMountM"/>
    /// reads it, and only as the non-binding half of that bound.</summary>
    public const float RoomFWallWidthM = 10f;

    /// <summary>
    /// <b>The box the drawing is fitted inside, metres — DERIVED from the room and from the
    /// avatar that is actually on screen, never typed.</b> Talon, 2026-09-04: <i>"Please made it
    /// large enough to at least the player can see it."</i>
    ///
    /// <para>Both bounds are the surface less <b>one whole avatar</b> of clear margin
    /// (<see cref="AvatarProportions.PlayerCrownM"/>, 1.241 m, measured off the shipped
    /// whole-figure model), split evenly on each side. That length is the only one in this room
    /// that means anything to a player: a sheet leaving less than half a body of wall around it
    /// stops reading as a sheet ON a wall and starts reading as the wall. It gives
    /// <b>8.759 x 2.759 m</b>, and at the shipped drawing's 1.318:1 the HEIGHT binds:
    /// <b>3.637 x 2.759 m</b> of paper, 2.9x the avatar's own height, on a 10 x 4 m wall.</para>
    ///
    /// <para><b>What that buys at the arrival point.</b>
    /// <see cref="ArrivalOf"/> stands the traveller 9.0 m from this wall, so 3.637 m of paper
    /// fills about 26% of the frame's width — roughly 500 px at 1080p, of which the hand-lettered
    /// word is about 390. Read, not squinted at. The capture, not this paragraph, is the
    /// evidence: <c>docs/qa/FRAME-1/</c>.</para></summary>
    public static readonly Vector2 SecretPictureMountM = new(
        RoomFWallWidthM - MpFoundation.Game.Sandbox.AvatarProportions.PlayerCrownM,
        TvRoomHeight - MpFoundation.Game.Sandbox.AvatarProportions.PlayerCrownM);

    /// <summary>
    /// <b>Height of the drawing's centre above the Deep Room's floor, metres — DERIVED, and the
    /// one place FRAME-1's packet had to give.</b> The packet asked for the picture at the
    /// avatar's eyeline (<see cref="AvatarProportions.PlayerEyeHeightM"/>, 0.995 m); Talon then
    /// asked for it large enough to read from the arrival point. In a 4 m room those are
    /// arithmetically incompatible: a sheet centred at 0.995 m can be at most 1.99 m tall before
    /// its bottom edge reaches the floor, i.e. 2.62 m wide, against the 3.64 m the wall can
    /// carry. Size won, and the avatar did not stop mattering — it sets the MARGIN in
    /// <see cref="SecretPictureMountM"/> instead, so re-measuring the body still resizes the
    /// drawing.
    ///
    /// <para>Maximising the sheet under an even margin puts its centre at the wall's own
    /// mid-height by construction: <c>margin/2 + height/2 = TvRoomHeight/2</c>. The eyeline then
    /// falls 13.6% up the sheet, and the drawing's own centre of ink — measured off the PNG, at
    /// 46.1% up — sits 1.89 m up, a metre above the player's eyes and 6.4 degrees above the
    /// horizon at the arrival distance, which is inside the camera's vertical field several times
    /// over.</para></summary>
    public static readonly float SecretPictureCentreY = TvRoomHeight * 0.5f;

    // --- The seventh television, the sky deck and the diving board (ROOFTOP-1) ---------------
    //
    // Talon, 2026-09-04/05, in three sentences that are the whole of this feature:
    //   "a player can jump from the top of the cyan platform down and onto the lab ... I love it
    //    please do not change this"
    //   "on the rooftop lab rooftop what if there was another TV? ... all it did was take the
    //    player to like ... an impossibly high angle and on this angle, there is the very last
    //    bubble the 100 bubble and maybe it's on like a diving board so it's funny"
    //   "what's on the top of the puffling lab roof? there's no TV on here?"
    //
    // EVERY NUMBER BELOW IS DERIVED FROM SOMETHING, and the two that are mirrors of another
    // scene say so and are re-measured live by BubbleTestSelfTest.CheckRooftopRoute every run.

    /// <summary>Node name of the television on the puffin lab's roof — the seventh, and the one
    /// at the end of the route Talon asked to have left alone. Read by <see cref="TvRoutes"/>,
    /// <c>BubbleTestWorld.ReturnFor</c> and <c>BubbleTestSelfTest</c>; one spelling, because two
    /// is the trap this file has paid for three times.</summary>
    public const string RooftopTvNodeName = "RooftopTv";

    /// <summary>
    /// <b>The top face of the LANDING roof, world y</b> — the lab's own ceiling slab
    /// (<c>PuffinLab/Lab/HubAccess_Off/Ceiling</c>, 22.90 × 17.38 m, x 78.55..101.45,
    /// z 81.31..98.69). This is where a player arriving off cyan's east lip touches down, and it
    /// is <b>not</b> where the seventh television stands — see <see cref="LabVoidRoofTopY"/>.
    ///
    /// <para>Talon, 2026-09-05, on the first siting of that television: <i>"That's the wrong roof.
    /// It's the one farther back. It's the one out more."</i> Recorded here with both numbers so
    /// the distinction is a measurement and not a direction the next reader has to guess at: the
    /// landing roof's centre is (90.00, 90.00) and the far roof's is (167.97, 79.79) —
    /// <b>78.64 m apart in plan</b> (77.97 east, 10.21 north) with the far one <b>5.796 m
    /// LOWER</b>.</para></summary>
    public const float LabLandingRoofTopY = -6.410f;

    /// <summary>
    /// <b>The top face of the FAR roof, world y — where the seventh television stands, and the one
    /// MIRROR of geometry this file does not own.</b>
    ///
    /// <para><c>PuffinLab/Route/VoidCeil</c>: an 11.86 × 11.32 m slab (x 162.04..173.90,
    /// z 74.13..85.45) roofing the void chamber at the very end of the lab's escape route — the
    /// furthest east anything in this level reaches. It is the surface at the end of Talon's
    /// sequence: <i>"walk across that little tunnel, and then if the player stacks golden cubes up
    /// if they stack three of them, they would be able to jump up onto the top of that other area
    /// so I would like to put something special up here."</i> The tunnel is
    /// <c>Route/HallCeil</c> (56.0 × 4.96 m, top y = −16.070) and the climb off it onto this slab
    /// is <b>3.864 m</b>.</para>
    ///
    /// <para>It is not a number this file can derive: the lab is an optional scene, ported,
    /// instanced at <c>BubbleTestWorld.PuffinLabAnchor</c> and carrying a 1.38 scale on its
    /// children. So it is measured, written down, and then <b>re-measured against the live lab on
    /// every suite run</b> by <c>BubbleTestSelfTest.CheckRooftopRoute</c>, which fails if the roof
    /// and <see cref="LabRoofPadTopY"/> ever stop agreeing. A mirror with an instrument on it is a
    /// mirror; one without is a second copy waiting to drift.</para></summary>
    public const float LabVoidRoofTopY = -12.206f;

    /// <summary>The tunnel a player walks across to reach the far roof: <c>Route/HallCeil</c>'s
    /// top face. Mirrored for the same reason and measured by the same check — it is the surface
    /// the cube stack is built ON, so the climb below is derived from it rather than typed.</summary>
    public const float LabTunnelTopY = -16.070f;

    /// <summary>
    /// <b>The climb Talon's cube stack has to make: 3.864 m from the tunnel to the far roof.</b>
    ///
    /// <para>Recorded because the arithmetic does not come out where he expected it to, and that
    /// is worth stating in the contract rather than only in a report. A <c>Crate.tscn</c> is
    /// 0.44 m, so three stacked give 1.32 m of launch height; a full jump's apex on the shipped
    /// motor is <c>JumpVelocity² / 2·Gravity</c> = 8.4² / 48 = <b>1.47 m</b>. Three cubes plus a
    /// jump therefore lifts a player <b>2.79 m</b> — <b>1.07 m short</b>. Six cubes (2.64 m) clear
    /// it; five (2.20 m) miss by 0.19 m. <c>BubbleTestSelfTest.CheckRooftopRoute</c> prints this
    /// every run and does not assert it: the geometry is EGG-1's and the access method is Talon's
    /// to rule on.</para></summary>
    public const float LabRoofClimbM = LabVoidRoofTopY - LabTunnelTopY;

    /// <summary>How far the television's pad stands proud of the lab's roof, metres. <b>Two
    /// centimetres, and both digits are load-bearing.</b> Above zero so the two top faces cannot
    /// z-fight (<c>.claude/rules/godot-scenes.md</c>: a coplanar pair is a defect only a render
    /// shows), and far below any step a player has to climb — a player who has just made a 3.9 m
    /// climb onto this roof should not then meet a lip.</summary>
    public const float LabRoofPadProudM = 0.02f;

    /// <summary>Top face of the authored pad the seventh television stands on.
    /// <see cref="LabCeilingTopY"/> plus <see cref="LabRoofPadProudM"/> — never typed.</summary>
    public const float LabRoofPadTopY = LabVoidRoofTopY + LabRoofPadProudM;

    /// <summary>Plan size of that pad, metres square. 6 m on an 11.86 × 11.32 m roof: wide enough
    /// that the television, the player who climbs up to it and the player who comes back out of it
    /// are all on authored ground that exists whether or not <c>PuffinLab.tscn</c> is in the
    /// build, and small enough that it reads as the television's pad rather than as a new floor
    /// over a roof this packet does not own.</summary>
    public const float LabRoofPadSizeM = 6f;

    /// <summary>
    /// <b>Where the seventh television stands</b>, world space, base on the pad.
    ///
    /// <para><b>ON THE FAR ROOF, not the landing roof</b> — Talon, 2026-09-05: <i>"That's the
    /// wrong roof. It's the one farther back. It's the one out more."</i> The first siting of this
    /// television was at (88, 91) on the landing roof, i.e. at the START of his sequence; the
    /// reward belongs at the END of it, on the surface whose only access is the climb off the
    /// tunnel. This is 78.64 m further along that route.</para>
    ///
    /// <para><b>Why (168, 80.5) on that slab.</b> A player climbs on over the roof's SOUTH edge
    /// (z ≈ 74.3, anywhere along x 162..171.8 — that is the edge the tunnel runs under), so the
    /// television has to be in front of them as they come up. It stands 5.97 m from the west lip,
    /// 5.90 m from the east, 6.37 m from the south and 4.95 m from the north: the point on this
    /// roof furthest from every edge, on a slab with no parapet and a 8.7 m drop off every side.
    /// It is inside <see cref="LabRoofFootprint"/> with 6 m to spare.</para>
    /// </summary>
    public static readonly Vector3 RooftopTvPos = new(168f, LabRoofPadTopY, 80.5f);

    /// <summary>
    /// <b>Where the sky deck puts a player back — the third named exception to
    /// <see cref="RoomReturnOffset"/>, and it is forced rather than chosen.</b>
    ///
    /// <para><c>TvPortalSelfTest.CheckDestinations</c> requires every room's way out to land
    /// ABOVE ground (<c>Destination.Y &gt; TvRoomAnchor.Y + 20</c>, i.e. above y = 0) — "the way
    /// out has to leave the room". The far roof is 12.21 m BELOW grade and there is no authored
    /// ground above y = 0 within 100 m of it, so the standard "beside the television you came in
    /// by" return is the one thing this route cannot have.</para>
    ///
    /// <para>So it lands where the route STARTS instead: cyan's plate at (64, 1, 90), 6 m in from
    /// the east lip a player jumps off, on flat authored ground at y = 0. A teleport resets yaw to
    /// −Z, so the returning player faces north ALONG the lip rather than over it, and half a
    /// second of held forward moves them 1.66 m further from the drop rather than toward it. The
    /// ride is repeatable by design (the view is the reward, the bubble is the excuse), and this
    /// is the point a player would walk back to in order to ride it again.</para>
    ///
    /// <para><b>The cost is stated rather than hidden:</b> a player who leaves the sky deck by the
    /// television instead of by jumping is put back at the beginning of the whole traversal, not
    /// at the end of it. Jumping is the cheaper way down and that is deliberate — the fall is the
    /// reward.</para></summary>
    public static readonly Vector3 RooftopTvReturn = new(64f, 1f, 90f);

    /// <summary>
    /// <b>The sky deck's floor, world y — "an impossibly high angle" as a number.</b>
    ///
    /// <para><b>200 m, and it is picked off the camera's own frame rather than off how big the
    /// number sounds.</b> The furthest authored corner of the level from the origin is cyan's
    /// (70, 150) at 165 m in plan; every other section corner is nearer (blue 153, red 152, green
    /// 149). <c>SandboxCamera.DefaultFov</c> is 75° VERTICAL, so a camera looking straight down
    /// from height <c>h</c> frames a radius of <c>h × tan(37.5°) = 0.767 h</c>. At 200 m that is
    /// <b>153 m</b> — the hub, all four surface sections and the tangle inside one frame, with
    /// cyan's far corner arriving as soon as the camera pitches at all (its horizontal half-angle
    /// is 57°, i.e. 308 m, so the corner is never out of shot sideways). At 180 m the radius is
    /// 138 m and two section corners sit outside a straight-down frame; at 250 m the whole map fits
    /// with room to spare and every landmark in it is 25 % smaller. 200 is the smallest round
    /// height that frames the level.</para>
    ///
    /// <para>It is also <b>3.29×</b> the tangle spire's 60.789 m summit, which was the top of this
    /// world until today, so "the very top of the whole world" is measured rather than argued.</para>
    ///
    /// <para><b>The fall is the other half of the choice.</b> Nothing in this game clamps terminal
    /// velocity (<c>AvatarMotor</c> has no fall-speed cap) and nothing does fall damage, so height
    /// buys airtime as √h: <c>t = sqrt(2h / (Gravity × FallGravityMultiplier))</c> =
    /// sqrt(2 × 200 / 36) = <b>3.33 s</b> from a standing drop, ending at 120 m/s on the hub
    /// plaza. Long enough to be an event; short enough that a player who wanted the bubble and not
    /// the view is not held hostage by it.</para>
    ///
    /// <para><b>What the altitude costs, measured rather than assumed (ROOFTOP-1 scope item 2).</b>
    /// At the house capture phase (<c>--cycle-start-phase noon</c>) fog is 0.001 m⁻¹ and 259 m of
    /// slant to the furthest corner leaves <b>77 %</b> of the contrast — the map reads. At NIGHT it
    /// does not: <c>BubbleTestWorld</c> binds the fog to a 28 m sight range
    /// (<c>SightPresentation.FogDensityWithSight</c>, <c>NightSightRangeM</c>), which is 0.107 m⁻¹
    /// and ~5e-10 transmittance at 200 m, so the level below is not hazy, it is absent. <b>This
    /// vantage is a daylight reward and nothing here changes that</b> — the night fog is
    /// <c>SightPresentation</c>'s, deliberate, and not a level constant to reach into. Two smaller
    /// findings, both stated and neither fixed here: the sun's shadow cascade is
    /// <c>ShadowRangeM</c> = 120 m, so from up here the whole level renders unshadowed and a player
    /// riding up watches every shadow fade out through 102–120 m; and the moon disc is a 34 m
    /// opaque sphere anchored 650 m from the ORIGIN, so at a low moon elevation it can appear
    /// below the horizon line from this altitude. The star dome (radius 700 m, origin-centred,
    /// <c>depth_draw_never</c>, lower hemisphere faded to nothing) cannot occlude the view and the
    /// camera's far plane is Godot's default 4000 m against a 276 m worst-case sight line.</para>
    /// </summary>
    public const float SkyDeckY = 200f;

    /// <summary>The sky deck's centre as a <see cref="TvRoute.RoomCentre"/> — LOCAL to the TV room
    /// section, which is anchored at <see cref="TvRoomAnchor"/>. Derived from
    /// <see cref="SkyDeckY"/>, never typed, because the arithmetic between world y and room-local
    /// y is exactly the retyping that put <c>TvRoomFootprint</c> 20 m underground once already.
    ///
    /// <para><b>Why the sky deck is a TV ROOM at all.</b> <c>TvPortalSelfTest</c> finds every
    /// route's way out at <c>TvRoom/&lt;ReturnTvPath&gt;</c> — a hard path under this one section
    /// node — so a destination that is not under it has no way back the shipped test can see. The
    /// TV room is already the section that is nowhere in the world (its footprint overlaps
    /// everything and is exempted by name from three separate checks); a room 180 m UP is the same
    /// kind of nowhere as a room 20 m down. It is also directly above the hub, which is what makes
    /// the fall land on the flattest 80 × 80 m in the level.</para></summary>
    public static readonly Vector3 SkyDeckRoomCentre = new(0f, SkyDeckY - TvRoomAnchor.Y, 0f);

    /// <summary>
    /// <b>How high the TV room section's content may reach above its anchor — the one constant
    /// this packet had to split off an existing one.</b>
    ///
    /// <para><c>MaxHeightOf(TvRoom)</c> was <see cref="TvRoomHeight"/> (4 m), and that was right
    /// while every room in the section was a 4 m box. The sky deck is in the same section and 220 m
    /// above its anchor, so <c>BubbleTestSelfTest.CheckFootprints</c> — which bounds every section's
    /// meshes at <c>anchor + MaxHeightOf + 0.5</c> — would have failed on the feature working.</para>
    ///
    /// <para><b><see cref="TvRoomHeight"/> itself does NOT move</b>, and that separation is the
    /// whole reason this is a second constant rather than a bigger first one: <c>TvRoomHeight</c> is
    /// the sealed box's height and is read by <see cref="TvRoomFootprint"/>, <see cref="VolumeOf"/>,
    /// <see cref="SecretPictureMountM"/> and <see cref="SecretPictureCentreY"/>. Raising it would
    /// have grown the <c>SectionVolume</c> trigger 220 m up through the whole world and resized the
    /// Deep Room's picture. Derived from <see cref="SkyDeckY"/> with 5 m of headroom for the deck's
    /// own props, so moving the deck moves this with it.</para></summary>
    public static readonly float TvRoomCeilingM = (SkyDeckY - TvRoomAnchor.Y) + 5f;

    /// <summary>Half-extent of the sky deck's square floor, metres. 7 — a 14 × 14 m platform: room
    /// to arrive, turn, walk to the board and walk to the way out, and small enough that standing
    /// on it is standing on something with an edge in sight on every side.</summary>
    public const float SkyDeckHalfM = 7f;

    /// <summary>Where the diving board's tip is, in z, local to the sky deck. The board leaves the
    /// deck's north edge (−<see cref="SkyDeckHalfM"/>) and runs 10 m further north, so a player who
    /// arrives at <see cref="RoomArrivalOffset"/> facing −Z is looking straight down it.</summary>
    public const float DivingBoardTipZ = -17f;

    /// <summary>The board's walking surface, world y. Flush with the deck floor rather than proud
    /// of it: the walk out has to be a walk, and a lip at the root of a board over a 180 m drop is
    /// a trip hazard the player pays for with the whole ride.</summary>
    public const float DivingBoardTopY = SkyDeckY;

    /// <summary>
    /// <b>The hundredth bubble, world space — on the end of the board.</b>
    ///
    /// <para>1.05 m above the plank and 1.10 m short of its tip. <b>Standing-reachable, and that
    /// is a decision rather than an accident:</b> Talon's <i>"the player can jump and catch the
    /// bubble"</i> is satisfied by a player walking out over a 180 m drop to get it — putting it
    /// off the END of the board, where only a jump reaches, would make the level's hundredth
    /// bubble missable on the attempt and dependent on a re-ride. The ride IS repeatable, so it
    /// would not be unwinnable; it would just be the one bubble in the level that punishes a
    /// mistake, on the one platform where a mistake is a 3.16 s fall.</para>
    ///
    /// <para>The height is derived from the body, not chosen: a standing avatar's crown is
    /// <c>AvatarProportions.PlayerCrownM</c> above the plank and the bubble's collider reaches
    /// 0.35 m below its centre, so 1.05 m puts the sphere's underside at 0.70 m — half a body
    /// under the crown, with the whole of the bob inside the margin.</para></summary>
    public static readonly Vector3 SkyBubblePos = new(0f, DivingBoardTopY + 1.05f, DivingBoardTipZ + 1.1f);

    /// <summary>Which authored bubble was moved onto the board. <b>It is a RELOCATION, not an
    /// addition</b> — the level's total stays exactly 100 — and it is this one because BUBBLE-1
    /// nominated it and the reasoning still holds: seam bubbles belong to no section, so moving one
    /// makes no section's count a lie; <c>Bubble_Seam_01</c> sits 3.8 m away marking the same north
    /// connector; and because ids are the index of an ordinal NODE-PATH sort, editing a
    /// <c>position</c> keeps the node's path, keeps its id, needs no re-bake and moves no constant.
    /// The node stays in <c>Hub.tscn</c> under <c>Bubbles</c> for exactly that reason.</summary>
    public const string SkyBubbleNodeName = "Bubble_Seam_02";

    /// <summary>Every television in the level and the room it opens onto. Seven entrances, seven
    /// rooms, one room each — Talon, 2026-08-29: <i>"give the player different areas to teleport
    /// to when going through each of the TVs."</i>
    ///
    /// <para><b>"Five entrances, five rooms" until ROOFTOP-1, 2026-09-05.</b> EGG-2 made it six
    /// and this packet made it seven, and the sentence had not moved either time. It is the
    /// second stale count BUBBLE-1's finding predicted, and it is exactly why every consumer
    /// derives from <c>TvRoutes.Length</c> rather than reading a sentence. <b>Count the array.</b>
    /// </para>
    ///
    /// <para><b>Every entrance stands on a flat authored ground plate</b>, never on green's
    /// heightfield: an entrance's Y is a literal in this table and GUARD-1 asserts it against a
    /// downward ray, so a TV on terrain would be a number nobody in this level can derive. Green
    /// is the section without a television for that reason and no other.</para>
    ///
    /// <para><b>Room order is the order they were added</b>, and the Den is first because its
    /// path (<c>RoomTv</c>, no room node) is BT-10's and is deliberately left alone.</para></summary>
    public static readonly TvRoute[] TvRoutes =
    {
        // ONE TELEVISION STAYS ON THE GREY LEVEL AND FOUR MOVED UP — Talon's note 8, 2026-08-30:
        // "Please keep one of the 'TV sets' on the gray level, place. Please put the rest of the
        // TVs at the top, in the hard to reach places of the world around. Please make these the
        // reward at the end."
        //
        // The paragraph above about entrances standing on flat AUTHORED ground plates still binds
        // and is what made this movable at all: every destination below is the top face of an
        // authored box, so its Y is a number this level can derive and GUARD-1's downward ray in
        // BubbleTestSelfTest.CheckPropHeights still measures it. Green remains the section without
        // a television for the same reason it always was — its ground is a heightfield.
        //
        // WHY THESE FOUR SUMMITS AND NOT FOUR OTHERS. They are the four highest authored standing
        // surfaces in the level, they escalate (22.8 → 34.8 → 41.2 → 60.8 m), and they are spread
        // over three sections and three compass directions, so "the hard to reach places of the
        // world around" is a tour rather than one tower visited four times. Cyan and green get
        // none: cyan's ceiling is 3 m and green's is 6 m, and a reward for climbing has to be at
        // the top of a climb.

        // The Den. BT-10's room, untouched — same node paths, same crooked frames, same lamp —
        // and THE ONE ON THE GREY LEVEL. It keeps BubbleTestWorld.HubReturn's spawn-ring return.
        new("HubTv", new Vector3(-22f, 0f, -22f), "RoomTv", new Vector3(0f, 0f, 0f)),
        // W7-1: the tangle spire's SPUR PAD, 34.782 m — a four-block branch off tread 12 out to a
        // 6 m dead-end platform, so reaching it costs the detour AND the climb back onto the
        // spire. Authored and printed by tools/dev/tangle_tower.py; 1.2 m north of the pad centre
        // so the screen faces the arriving player and the pad holds the standing room in front.
        // (It was cyan's far end, placed there under THRILL §12 to be found while arriving for
        // another reason. Note 8 replaces that reading: the prize is now the climb's payment.)
        new("HiddenTv", new Vector3(60.383f, 34.782f, -81.066f), "RoomE/RoomTv", new Vector3(0f, 0f, 17f)),
        // W7-1: red's Cairn_Main summit, 22.810 m, on the 5 m TvPerch this packet authored flush
        // with the shipped SummitBlock top — the cap that gives the cairn's summit the standing
        // room a television and a returning player both need. StackBlock19 pokes 0.24 m through
        // it, which is under the 0.300 m jog-tap apex and keeps the summit reading as a cairn.
        new("RedTv", new Vector3(-0.159f, 22.81f, -79.456f), "RoomB/RoomTv", new Vector3(-18f, 0f, 0f)),
        // W7-1: the blue tower's summit, 41.200 m, on the 5 m TvPerch authored flush with the
        // shipped SummitBlock top. The climb is unchanged — the last step off the Core roof is the
        // same 1.2 m it always was.
        new("BlueTv", new Vector3(79f, 41.2f, -1.2f), "RoomC/RoomTv", new Vector3(17f, 0f, 0f)),
        // W7-1: THE TOP OF THE WORLD — the tangle spire's 8 m summit cap at 60.789 m, 19.6 m above
        // anything else in the level. This is note 8's "reward at the end" in its strongest form:
        // 41 jumbled treads above the shipped pile's own 23 m stair.
        new("TangleTv", new Vector3(70.593f, 60.789f, -70.442f), "RoomD/RoomTv", new Vector3(0f, 0f, -17f)),

        // EGG-2, Talon's addendum §6: "Add a sixth TV, hidden underwater in the level's lake/river
        // feature. The player must swim out into water that will drown them and reach the TV
        // before drowning — a deliberate risk/reward, swim-under-time-pressure test."
        //
        // THE ONE ENTRANCE THAT IS NOT ON THE GROUND, and the paragraph above about flat authored
        // plates is what makes that legal rather than an exception: it stands on an authored
        // plinth (GreenHills.tscn, "SunkenTvPlinth") whose top face is SunkenTvBaseY, so its Y is
        // still a number this level derives and GUARD-1's downward ray in
        // BubbleTestSelfTest.CheckPropHeights still measures it. Green is no longer the section
        // without a television — it is the section whose television is under the water, which is
        // the one place a heightfield could not have put one.
        //
        // ITS ROOM IS NOT ANOTHER DEN. RoomF is the Deep Room: no couch, no picture wall, no lamp
        // — a silt floor under a lit water ceiling, which is the lake from the inside. The reward
        // for a drowning risk should not be the room the player has already seen five times.
        new(SunkenTvNodeName, SunkenTvPos, "RoomF/RoomTv", new Vector3(-17f, 0f, -17f)),

        // ROOFTOP-1, Talon 2026-09-04/05: "on the rooftop lab rooftop what if there was another
        // TV? ... all it did was take the player to ... an impossibly high angle and on this
        // angle, there is the very last bubble the 100 bubble and maybe it's on like a diving
        // board so it's funny ... and then they get to fall and they get to see the whole map
        // from a very top of the whole world?"
        //
        // THE ONE ENTRANCE THAT IS BELOW GRADE, and the paragraph above about flat authored
        // plates is what makes it legal rather than an exception: it stands on LabRoof.tscn's
        // authored TvPad, whose top face is LabRoofPadTopY, so its Y is still a number this level
        // derives and GUARD-1's downward ray in BubbleTestSelfTest.CheckPropHeights still
        // measures it. The pad exists in every build; the LAB under it does not, and that is the
        // point — an optional scene cannot be the ground a shipped television stands on.
        //
        // ITS ROOM IS THE SKY DECK, RoomG, and it is not a room. No couch, no walls, no ceiling —
        // a 14 m platform 180 m over the hub with a diving board off its north edge and the
        // level's hundredth bubble on the end of it. The reward for the one traversal in this
        // level nobody designed is the only view in it nobody has seen.
        new(RooftopTvNodeName, RooftopTvPos, "RoomG/RoomTv", SkyDeckRoomCentre),
    };

    /// <summary>Where <paramref name="route"/>'s traveller comes out, world space.</summary>
    public static Vector3 ArrivalOf(TvRoute route) =>
        TvRoomAnchor + route.RoomCentre + RoomArrivalOffset;

    // --- Max heights (program §4, the "Height" column) --------------------------------------

    /// <summary>Hub seating tops out here; the plaza itself is flat at y = 0. Program §4.</summary>
    public const float HubMaxHeight = 1.2f;

    /// <summary>Ceiling for the hub's four signposts at the connector ends (§4 rule 4; BT-5
    /// scope 4, whose "Done looks like" names the signposts as the stated exception). Seating
    /// still tops out at <see cref="HubMaxHeight"/> — measured at 0.32 m — and this covers the
    /// four post meshes only. Ruled by the orchestrator on BT-5's escalation, 2026-08-28.</summary>
    public const float HubSignpostHeight = 2.5f;

    /// <summary>Red cairn summit ceiling. The scramble pile it is baked from is ~24 m against a
    /// 1.20 m body, so this leaves headroom without licensing a second pile. Program §4.</summary>
    public const float RedMaxHeight = 28f;

    /// <summary>Blue tower summit ceiling — the tallest thing in the level, and the fall that
    /// makes §4 rule 3's "never lethal, the walk back is the cost" mean something.
    /// Program §4.</summary>
    public const float BlueMaxHeight = 42f;

    /// <summary>Cyan obstacle ceiling. Low on purpose: the section tests ground handling, and a
    /// climbable obstacle turns a run into a scramble. Program §4.</summary>
    public const float CyanMaxHeight = 3f;

    /// <summary>Green hill ceiling. Program §4.</summary>
    public const float GreenMaxHeight = 6f;

    /// <summary>Tangle summit ceiling. <b>28 → 63 at W7-1</b> (Talon's playtest note 10,
    /// 2026-08-30: <i>"make a massive 'jumble' of blocks, make one of them extremely tall … there
    /// are two please make one of them super tall and jumbly"</i>).
    ///
    /// <para>The level held exactly two block jumbles — red's cairns (23.05 m) and this pile
    /// (23.09 m). Red keeps its curated cairn stair; the tangle grew a spire.
    /// <c>tools/dev/tangle_tower.py</c> authors it and prints its numbers: the summit cap tops out
    /// at <b>60.789 m</b>, which makes it the tallest thing in the level by 19.6 m over
    /// <see cref="BlueMaxHeight"/>. 63 leaves roughly the same headroom the old 28 left over the
    /// 24.05 m summit bubble, and licenses nothing further.</para>
    ///
    /// <para><b>Raising this obliges <see cref="SectionVolumeHeight"/> to move with it</b> —
    /// <c>BubbleTestLayoutTests.SectionVolumesReachTheTopOfTheirOwnSection</c> is the assertion
    /// that makes that an error rather than a silent dwell-log bug, and it is why the two moved in
    /// one commit.</para></summary>
    public const float TangleMaxHeight = 63f;

    /// <summary>Green's lake bed floor. <b>It was the ONE place in the level authored below
    /// y = 0 (§4 rules 1 and 2) until EGG-2; it is now one of two</b> — see
    /// <see cref="BlueMoatBedY"/>, which Talon's addendum §7 asked for by name. Nothing else
    /// walkable may go under zero.</summary>
    public const float GreenMinHeight = -4f;

    /// <summary>
    /// <b>The top face of the blue tower's moat bed</b> (EGG-2, Talon's addendum §7). The second
    /// and only other place this level authors ground below y = 0.
    ///
    /// <para><b>−3.0 is the shallowest depth that still drowns, plus room.</b> A body must be
    /// <see cref="WaterGeometry.SubmergedDepthM"/> (1.15 m) under
    /// <see cref="WaterGeometry.WaterY"/> (−0.58) before <c>DrowningClock</c> starts, i.e. feet at
    /// or below −1.73. At −3.0 the bed is 2.42 m under the surface — 1.27 m of margin, so a body
    /// standing on it is unambiguously submerged rather than sitting on the boundary where a
    /// hysteresis band decides whether the level's newest hazard works.</para>
    ///
    /// <para><b>And the walls are vertical on purpose.</b> A swimmer floats at
    /// <see cref="WaterGeometry.SwimLineY"/> (−1.83), 1.83 m below the apron, and
    /// <c>WaterGeometry.JumpAllowed</c> is false while Swimming, so the moat has no shore. That is
    /// §7 stated as geometry: <i>"falling means landing in water and drowning."</i> The plan
    /// bounds live in <see cref="WaterGeometry.BluePrecisionMoat"/> — one authority for what
    /// drowns you, this one for how deep it is, and <c>BubbleTestLayoutTests</c> reads the shipped
    /// scene and checks the boxes against both.</para></summary>
    public const float BlueMoatBedY = -3.0f;

    /// <summary>The TV room's interior height. Program §4.</summary>
    public const float TvRoomHeight = 4f;

    // --- Spawns and the pedestal (program §4 rule 5) ----------------------------------------

    /// <summary>Radius of the spawn ring. Program §4 rule 5.</summary>
    public const float SpawnRingRadius = 6f;

    /// <summary>
    /// <b>Where the spawn ring's centre sits, hub-local</b> — 18 m SOUTH of the hub origin.
    /// Talon's playtest note 1, 2026-08-30: <i>"Please start the player slightly farther back,
    /// this will give more view of the level overall."</i>
    ///
    /// <para><b>Back means +Z, and the reason is the shipped facing rather than a preference.</b>
    /// A teleport or a fresh spawn leaves <c>MoveState.Yaw</c> at 0, which is −Z, so every player
    /// in this level opens their session looking north up the red arm with the tangle spire framed
    /// in the gap to its right. Backing the ring along that axis is what puts more of the level in
    /// front of the camera; lifting it would have been a different request (he asked to see more
    /// of the level, not to look down on it).</para>
    ///
    /// <para><b>18 m, and what fixed it.</b> The ring used to straddle the origin, 2 m from the
    /// pedestal at its nearest marker — close enough that the plaza filled the frame. At +18 the
    /// nearest marker is 20 m from the pedestal and the furthest 32 m, the whole 80 m plaza and
    /// both flanking seating clusters are in shot, and the ring still stands 10 m clear of the
    /// hub plate's south edge at z = 40 with nothing authored between it and the connectors. It
    /// is a value, picked and stated per the packet: further back reaches the plate's edge, and
    /// nearer gives the frame back.</para>
    ///
    /// <para><b>Why this is a separate constant and not folded into
    /// <see cref="SpawnPosition"/>.</b> That method's contract is "a point on a ring of
    /// <see cref="SpawnRingRadius"/>", and <c>BubbleTestLayoutTests.SixSpawnsSitOnTheRingAndNoneCoincide</c>
    /// asserts exactly that by measuring its length. Moving the ring's CENTRE is a different fact
    /// from moving a point off the ring, and keeping them apart is what lets the shape stay proven
    /// while the placement moves. <see cref="SpawnPointOf"/> is the composed value everything else
    /// should use.</para>
    /// </summary>
    public static readonly Vector3 SpawnRingCentre = new(0f, 0f, 18f);

    /// <summary>How many spawn markers the hub must carry — <c>Protocol.MaxPlayers</c> is 6 and
    /// a seventh player cannot join, so six is exact rather than generous. Program §4 rule 5.
    /// </summary>
    public const int SpawnCount = 6;

    /// <summary>Where the diegetic counter pedestal stands, and therefore what all six spawns
    /// face: a joining player's first frame looks at the thing the level is about.
    /// Program §4 rule 5; the pedestal itself is BT-8's.</summary>
    public static readonly Vector3 PedestalPos = new(0f, 0f, -8f);

    /// <summary>Where the dead come back — the hub centre, lifted clear of the plaza slab so the
    /// respawn never resolves inside the floor collider. Program §4 rule 3.</summary>
    public static readonly Vector3 RespawnPoint = new(0f, 1f, 0f);

    // --- World extent (program §4 rule 3) ---------------------------------------------------

    /// <summary>Horizontal distance from the origin past which a player is off the map. Sized to
    /// clear the furthest section corner (cyan's, at ~160 m) with room to notice the edge before
    /// it takes you. Program §4 rule 3.</summary>
    public const float OffMapRadiusM = 210f;

    /// <summary>Y below which a falling player has left the world. 30 m below the TV room's
    /// floor, so a bug that drops a player through that floor still respawns them rather than
    /// falling forever. Program §4 rule 3.</summary>
    public const float VoidKillY = -70f;

    // --- The lake (program D4 / §4 rule 2) --------------------------------------------------

    /// <summary>The lake surface's exact y. <b>Referenced, never retyped</b> (BT-0 acceptance
    /// criterion 9): <see cref="WaterGeometry"/> is the shipped authority and a literal here
    /// would be a second copy that drifts the first time the shore is retuned.</summary>
    public const float LakeSurfaceY = WaterGeometry.WaterY;

    /// <summary>The lake's eastern limit. 5 m west of <see cref="WaterGeometry.ShoreX"/> so the
    /// shipped <c>DepthAt()</c> is true for the lake and false for every other point in the
    /// level. Program D4.</summary>
    public const float LakeMaxX = -50f;

    /// <summary>Deepest the lake bed may go. Program §4 rule 2 (bed ≤ −3.5 at the centre,
    /// ≥ −4).</summary>
    public const float LakeBedMinY = -4f;

    /// <summary>Shallowest the lake's centre may be — a lake you can see the bottom of is not
    /// "murky depths, a bit ominous" (§11). Program §4 rule 2.</summary>
    public const float LakeBedCentreMaxY = -3.5f;

    // --- Stepping stones (program §4 rule 2) ------------------------------------------------

    /// <summary>Lowest a stepping stone's top may sit above the water. Program §4 rule 2.</summary>
    public const float StoneTopMin = 0.25f;

    /// <summary>Highest a stepping stone's top may sit. Program §4 rule 2.</summary>
    public const float StoneTopMax = 0.45f;

    /// <summary>Tightest gap between stones. Program §4 rule 2.</summary>
    public const float StoneGapMin = 1.4f;

    /// <summary>Widest gap between stones — authored against <see cref="TapRange"/> so the
    /// crossing is walkable-with-care rather than a sprint-jump sequence. Program §4 rule 2.
    ///
    /// <para><b>MOVE-8 BROKE THIS ONE, and it is the packet's named fork rather than a number to
    /// quietly move.</b> The tap range was 1.710 m when 2.2 was chosen — a gap you clear with a
    /// tap and a little run-up. Talon's speed ruling takes the tap to <b>0.887 m</b>, so the
    /// widest stone gap is now <b>2.5×</b> a tap: the crossing became the sprint-jump sequence
    /// over deep water that program §4 rule 2 exists to forbid. Re-spacing the stones is
    /// explicitly out of MOVE-8's scope (the geometry is baked into <c>GreenHills.tscn</c>), so
    /// the breach is pinned by <c>BubbleTestLayoutTests</c> and listed for Talon instead.</para>
    /// </summary>
    public const float StoneGapMax = 2.2f;

    // --- Jump calibration (program §4 rule 6) -----------------------------------------------
    //
    // MOVE-8: DERIVED FROM THE MOTOR, not measured once and typed. These four were MOVE-3e's
    // in-engine measurements at the pre-MOVE-8 tuning — 1.534 / 6.192 / 0.467 / 1.710 — and the
    // moment Talon's 2026-08-28 ruling moved MotorTuning.Default all four became wrong at once,
    // silently, under a level that had been sized against them. They now come out of MotorArc,
    // which reproduces every one of those four literals to the digit AT THE OLD TUNING
    // (MotorArcTests is that positive control) and tracks the tuning from here on: move a gravity
    // row and these move with it.
    //
    // They are `static readonly`, not `const`, and that costs something real: a const is inlined
    // into every consumer's assembly, and a GDScript tool can parse a const's literal straight out
    // of this file — which is exactly what tools/dev/bubbletest_bake_green.gd did. The GDScript
    // side is handled in tools/dev/motor_arc.gd, which replicates the same simulation over the
    // rows it parses out of MotorTuning.cs rather than reading a number that is no longer written
    // down anywhere.

    /// <summary>Apex of a sprint-held jump, metres. Derived — <see cref="MotorArc.HeldSprint"/>.
    /// Program §4 rule 6. <b>1.534 m before MOVE-8's ruling, 1.407 m after.</b></summary>
    public static readonly float SprintApex = MotorArc.HeldSprint(MotorTuning.Default).ApexM;

    /// <summary>Flat range of a sprint-held jump, metres. Derived. Program §4 rule 6.
    /// <b>6.192 m before MOVE-8's ruling, 4.053 m after</b> — the single largest consequence of
    /// the speed ruling for authored geometry, and the reason MOVE-8 owes Talon a changed-class
    /// list.</summary>
    public static readonly float SprintRange = MotorArc.HeldSprint(MotorTuning.Default).RangeM;

    /// <summary>Apex of a jog-tap jump, metres. Derived — <see cref="MotorArc.JogTap"/>.
    /// Program §4 rule 6. <b>0.467 m before MOVE-8's ruling, 0.300 m after.</b></summary>
    public static readonly float TapApex = MotorArc.JogTap(MotorTuning.Default).ApexM;

    /// <summary>Flat range of a jog-tap jump, metres. Derived. Program §4 rule 6.
    /// <b>1.710 m before MOVE-8's ruling, 0.887 m after.</b></summary>
    public static readonly float TapRange = MotorArc.JogTap(MotorTuning.Default).RangeM;

    /// <summary>
    /// <b>Apex of a held sprint jump with the air jump spent at the top</b> — derived,
    /// <see cref="MotorArc.DoubleJumpAtApex"/>. NEW at MOVE-8, because the double jump is new at
    /// MOVE-8: <see cref="MotorTuning.AirJumpMode"/> shipped at its exact no-op until Talon ruled
    /// for it on 2026-08-28.
    ///
    /// <para><b>This, and not <see cref="SprintApex"/>, is now the reachability ceiling</b> —
    /// 2.410 m against 1.407 m. Every "can a player get up there" question in this level is asked
    /// against this number; <see cref="SprintApex"/> answers the different and still-load-bearing
    /// question of what is reachable <i>without</i> spending the air jump, which is what decides
    /// whether a climb is double-jump-MANDATORY or merely double-jump-easier.</para>
    /// </summary>
    public static readonly float DoubleApex = MotorArc.DoubleJumpAtApex(MotorTuning.Default).ApexM;

    /// <summary>Flat range of a held sprint jump with the air jump spent at the top — 6.485 m.
    /// Derived, NEW at MOVE-8, and the ceiling every gap is now measured against. See
    /// <see cref="DoubleApex"/>.</summary>
    public static readonly float DoubleRange = MotorArc.DoubleJumpAtApex(MotorTuning.Default).RangeM;

    /// <summary>Closest a blue-section gap may come to the reachability ceiling and still be
    /// called "the edge of reachability" — 5.4 m. Beyond the ceiling is not hard, it is
    /// impossible. Program §4 rule 6.
    ///
    /// <para><b>AUTHORED, not derived</b>, and deliberately left alone: it describes the geometry
    /// <c>BluePrecision.tscn</c> actually holds, and MOVE-8 is forbidden to re-space geometry.
    /// Against MOVE-8's arc this band sits past a single jump (4.053 m) and inside a double
    /// (6.485 m), so the blue tower's hardest lines became <b>double-jump-mandatory</b> rather
    /// than impossible. MOVE-8's report carries the per-section list.</para></summary>
    public const float EdgeGapMin = 5.4f;

    /// <summary>Furthest a blue-section gap may be. Program §4 rule 6. Authored; see
    /// <see cref="EdgeGapMin"/>.</summary>
    public const float EdgeGapMax = 5.9f;

    /// <summary>Lowest a blue-section "edge of reachability" step-up may be. Program §4 rule 6.
    /// Authored; see <see cref="EdgeGapMin"/>.</summary>
    public const float EdgeStepMin = 1.3f;

    /// <summary>Highest a blue-section step-up may be — under <see cref="DoubleApex"/> since
    /// MOVE-8, and under <see cref="SprintApex"/> before it. Program §4 rule 6. Authored; see
    /// <see cref="EdgeGapMin"/>.</summary>
    public const float EdgeStepMax = 1.45f;

    // --- Bubbles (program D8) ---------------------------------------------------------------

    /// <summary>
    /// <b>How many bubbles the level carries WITHOUT the optional lab</b> — the seven section
    /// splits below plus <see cref="SeamBubbles"/>, and nothing else.
    /// <c>TheBubbleSplitAddsUpToTheTarget</c> is what makes that true rather than asserted.
    ///
    /// <para><b>100 → 112 at TANGLE-1, and 112 → 90 at BUBBLE-1</b> (Talon, 2026-09-04, off a live
    /// playtest: <i>"There are too many bubbles ... I only want there to be 100 bubbles in
    /// total"</i>). The shipped total a player collects is <see cref="BubbleTargetWithLab"/> —
    /// Talon's hundred — and this constant is that hundred minus
    /// <see cref="PuffinLabBubbles"/>.</para>
    ///
    /// <para><b>Why there are two numbers and not one.</b> <c>PuffinLab.tscn</c> is OPTIONAL:
    /// <c>BubbleTestWorld.SetUpPuffinLab</c> guards on <c>ResourceLoader.Exists</c> and the level
    /// must come up without it (EGG-2 acceptance criterion 4). A single fixed total that counted
    /// lab bubbles would, in a build with no lab, advertise a total the player can never reach —
    /// an unwinnable objective, which <c>LEVEL-BIBLE.md</c> forbids outright. So the target is
    /// COMPOSED from what is actually there rather than fixed: this constant is the part that is
    /// always present, and the lab's share is added only when the lab loaded.
    /// <b>Nothing player-facing reads either constant</b> — <c>HudBubbleCount</c> and
    /// <c>BubbleCounterDisplay</c> both render <c>BubbleCounter.BubbleCount</c>, the live adoption
    /// count, so an absent lab already yields an honest smaller total on screen with no code path
    /// able to lie about it. These two exist so a test can say which case it is in.</para>
    /// </summary>
    public const int BubbleTarget = 90;

    /// <summary>
    /// <b>EGG-1's lab's share, and Talon's hundred is <see cref="BubbleTarget"/> plus this.</b>
    /// Talon, 2026-09-04: <i>"in the puffing lab scene that was recently added ... there are
    /// absolutely no bubbles, which is a shame because it's a really cool place."</i>
    ///
    /// <para>Ten, and they are authored in <c>PuffinLab.tscn</c> like every other bubble in this
    /// level. <b>Not in <see cref="BubblesOf"/>, because the lab is not a
    /// <see cref="Section"/></b> — it is instanced as a direct child of the world by
    /// <c>BubbleTestWorld.SetUpPuffinLab</c>, which runs BEFORE <c>SetUpBubbleCounter</c>, so its
    /// bubbles are adopted with everyone else's and share the one id space.</para>
    ///
    /// <para><b>Where they sort.</b> Ids come from an ordinal node-path sort, and <c>"PuffinLab"</c>
    /// falls between <c>"Hub"</c> and <c>"RedCairns"</c> — so admitting these renumbers red, the
    /// tangle and the TV room, and nothing before them. Every peer in a session runs the same
    /// build and therefore derives the same ids; that is the same assumption the seven sections
    /// have always rested on.</para></summary>
    public const int PuffinLabBubbles = 10;

    /// <summary><b>Talon's hundred</b> — what a player collects in a build that has the lab, which
    /// is every shipped build. <see cref="BubbleTarget"/> is what they collect in one that does
    /// not. Both are honest and neither is unwinnable; see <see cref="BubbleTarget"/> for why
    /// there has to be two.</summary>
    public const int BubbleTargetWithLab = BubbleTarget + PuffinLabBubbles;

    /// <summary>How many golden cubes the level carries (Talon's note 12, 2026-08-29: <i>"a bunch
    /// of golden cubes ... in and around the map"</i>). Four in the hub, three each in red, blue
    /// and cyan, two in the tangle, and one in each of <see cref="TvRoutes"/>'s rooms.
    ///
    /// <para><b>"five rooms" until BUBBLE-1 corrected it, 2026-09-04.</b> EGG-2 added the sunken
    /// television as a sixth row of <see cref="TvRoutes"/> and this sentence was not moved with
    /// it, so the file has been claiming five routes while carrying six. Counted rather than
    /// retyped: the array is the authority and <c>TvPortalSelfTest</c> derives its portal count
    /// from <c>TvRoutes.Length</c> for exactly this reason.</para>
    ///
    /// <para><b>Green has none, and that is the same reason it has no television:</b> its ground
    /// is a heightfield, so an authored Y there could not be derived, and a floating carryable is
    /// the exact defect GUARD-1 exists for. Everything else in this level stands on a flat
    /// authored plate.</para>
    ///
    /// <para>They are <c>Crate.tscn</c> instances under the world's <c>Props</c> node — no new
    /// pickup class, no new interaction, no new binding; carry and throw already exist and Talon
    /// was explicit that they must not be rebuilt. <c>BubbleTestSelfTest.CheckGoldenCubes</c>
    /// counts them and rays every one.</para></summary>
    public const int GoldenCubes = 20;

    /// <summary>Cyan's share — still the heaviest, because §11 picked cyan for how bubbles read
    /// against it, and <c>CyanCarriesTheDensestBubbles</c> pins that.
    ///
    /// <para><b>35 → 18 at BUBBLE-1, and this is where most of Talon's cut came from</b>
    /// (<i>"further reduce the number of bubbles on the top maps"</i>). Cyan was carrying 33
    /// placed against a nominal 35 and the two lane runs were the loudest of it: seven bubbles
    /// evenly spaced 10 m apart down x = −30 and five down x = +30 read as a corridor of markers
    /// rather than as a run with bubbles in it. Both lanes are thinned to roughly half spacing —
    /// the LINE still reads, at a third of the count — and the three scatters lose their nearest
    /// neighbours. Cyan keeps the densest share of any section by a clear margin.</para></summary>
    public const int CyanBubbles = 18;

    /// <summary>Red's share. <b>15 → 11 at BUBBLE-1.</b> The summit bubble beside <c>RedTv</c>
    /// and the mid-climb one stay — a climb has to pay — and the four cut are all redundant
    /// neighbours on the flat: a pair 2.8 m apart at the cairn's foot, one stacked directly under
    /// another, and one in a far corner nothing routes through.</summary>
    public const int RedBubbles = 11;

    /// <summary>Blue's share. <b>15 → 11 at BUBBLE-1.</b> The 41.9 m summit and the 37.65 m step
    /// below it are untouched — blue is a climb and the top of it is the reward. The four cut are
    /// from the low approach and the middle of the ladder, where two bubbles were doing one
    /// bubble's work.</summary>
    public const int BlueBubbles = 11;

    /// <summary>Green's share — <b>15 → 11 at BUBBLE-1</b>, and <b>all five stepping-stone
    /// bubbles survive</b>: that five is D8's own instruction and the stones are the one route in
    /// green that a bubble line is supposed to mark. The five cut are three from the postpile
    /// cluster, where four bubbles sat inside a 6 m ball, and two near-lake strays — one of them
    /// the y = −0.65 one, which was the least honest placement in the section.</summary>
    public const int GreenBubbles = 11;

    /// <summary>The hub's share. <b>10 → 7 at BUBBLE-1.</b> The plaza is where a player learns
    /// what a bubble is, so the one beside <c>BubbleCounterDisplay</c> stays and the three
    /// crowding it go: a teaching bubble reads better alone than in a ring of four.</summary>
    public const int HubBubbles = 7;

    /// <summary>The tangle's share. <b>12, deliberately below the 15 the other three outer
    /// sections carry</b>, and the reason is the zone's own brief: "many routes, none of them
    /// marked". Bubbles are the one thing in this level that reads as a marker, so a scatter
    /// through a pile whose whole point is that nobody decided the route would draw the route
    /// back in — the player would follow the bubbles instead of choosing. <b>Ten of the twelve
    /// therefore sit on the stair</b>, which is the one line the zone already promises; one is
    /// the lead-in on the ground at the hub-facing corner; one is the summit reward. The spine,
    /// the stack, the 46 rubble blocks and the three slabs pay nothing — they are the maybes.
    /// Ten treads rather than eleven because three candidate treads measured 0.196–0.427 m from
    /// a neighbouring block, inside the 0.50 m clearance bound; see
    /// <c>docs/levels/bubble-test-bubbles.md</c>.
    ///
    /// <para><b>BUBBLE-1 cut every other outer section and left this one at 12.</b> That is not
    /// an oversight and not an exemption from Talon's <i>"reduce the bubbles on the top maps"</i>
    /// — it is that the tangle's number is not a density. Ten of the twelve ARE the stair, one is
    /// the lead-in and one is the summit; there is no scatter here to thin, so a cut would delete
    /// treads and take the stair's reading with them, which is the opposite of what the reduction
    /// is for. Twelve was already the lowest outer share before the cut and is now the second
    /// highest, which is the honest cost of leaving it alone.</para></summary>
    public const int TangleBubbles = 12;

    /// <summary>
    /// The TV room section's share — finding the egg has to pay. <b>3 → 15 at BUBBLE-1</b>, and
    /// this is the other half of Talon's direction: <i>"there are not any bubbles at all in most
    /// of the TV areas ... I would like some bubbles to be moved from the outside to inside of
    /// some of these"</i>.
    ///
    /// <para><b>Three each into five rooms, and the fifth room is not RoomE.</b> All three of the
    /// shipped bubbles sat in the Den (<c>RoomCentre</c> (0,0,0)); RoomB, C, D, E and F had none.
    /// The twelve added go 3 each to <b>RoomB, RoomC, RoomD and RoomF</b> — the rooms behind
    /// <c>RedTv</c>, <c>BlueTv</c>, <c>TangleTv</c> and the sunken television.</para>
    ///
    /// <para><b>RoomE deliberately gets zero, and that is a reachability fact rather than a
    /// preference.</b> <c>BubbleTestWorld.DestinationFor</c> overrides
    /// <c>LabRouteEntranceName</c> (<c>HiddenTv</c>) to the lab's <c>Arrival</c> whenever the lab
    /// loaded, so in every shipped build <b>no television goes to RoomE</b> — it keeps only its
    /// way out. A bubble there would be one the player cannot reach, which turns "collect all the
    /// bubbles" into a lie; the lab is RoomE's replacement and it is the lab that gets the
    /// bubbles instead (<see cref="PuffinLabBubbles"/>). RoomE having no route while the lab is
    /// present is EGG-2's finding, not this packet's to fix.</para></summary>
    public const int TvRoomBubbles = 15;

    /// <summary>"Seam" bubbles on the connecting paths, which belong to no section — authored in
    /// <c>Hub.tscn</c> because the connectors are. <b>7 → 5 at BUBBLE-1:</b> one centred on each
    /// of the four connectors, and the north path kept a second because it is the longest run
    /// between two sections in the level. Program D8.
    ///
    /// <para><b>Still five, and one of them is no longer on a connector</b> (ROOFTOP-1,
    /// 2026-09-05). The north path's second, <c>Bubble_Seam_02</c>, is now the hundredth bubble on
    /// the sky deck's diving board (<see cref="SkyBubblePos"/>). It is a RELOCATION — the count
    /// here does not move, the node did not leave <c>Hub.tscn</c>, and its id is unchanged —
    /// because ids are the index of an ordinal node-path sort and a <c>position</c> edit changes
    /// no path. The north connector keeps <c>Bubble_Seam_01</c>, centred, which was always the one
    /// a player follows.</para></summary>
    public const int SeamBubbles = 5;

    /// <summary>How far a PLACEMENT packet may move any one section's split, either way, with a
    /// stated reason. Program D8.
    ///
    /// <para><b>This is a licence for the placer, not a bound on the splits themselves.</b>
    /// BT-11 used it and said so — it placed 33 in cyan against 35, 16 in red against 15 and 16
    /// in green against 15, all inside the ±5 and all in the counts table in
    /// <c>docs/levels/bubble-test-bubbles.md</c>. BUBBLE-1 moved the SPLITS instead, on Talon's
    /// direction, and then placed each section exactly on its new number, so the drift is now
    /// zero everywhere and <c>CheckBubbleCensus</c> holds every section to it.</para></summary>
    public const int BubbleSplitTolerance = 5;

    // --- Section volumes --------------------------------------------------------------------

    /// <summary>Height of the <see cref="SectionVolume"/> trigger boxes. <b>60 → 80 at W7-1</b>,
    /// because <see cref="TangleMaxHeight"/> went to 63 and a −10..+50 column would have put a
    /// player on the new spire's summit OUTSIDE the section they climbed — the exact defect the
    /// note on <see cref="SectionVolumeBottomY"/> describes for blue, one section over.</summary>
    public const float SectionVolumeHeight = 80f;

    /// <summary>Where a section volume's box starts, in y. <b>Not centred on y = 0</b>, and the
    /// reason is <see cref="BlueMaxHeight"/>: a 60 m box centred on the ground plane reaches
    /// +30 m, so a player standing on the blue tower's 42 m summit would be outside their own
    /// section and the dwell log would report them as having left it to climb it. −10 to +70
    /// covers every summit in the level — the tangle spire's 60.8 m cap included — and still
    /// leaves 10 m of headroom below grade for green's lake bed, without reaching anywhere near
    /// the TV room 20 m down.</summary>
    public const float SectionVolumeBottomY = -10f;

    // --- Section identity -------------------------------------------------------------------

    /// <summary>The eight sections, in the order the scene instances them (LabRoof is last, and
    /// added last, which is what keeps the bubble id space still). Names are the scene
    /// file names AND the node names AND the telemetry section key — one string, so a rename
    /// cannot go half-done.</summary>
    public enum Section
    {
        Hub,
        RedCairns,
        BluePrecision,
        CyanRun,
        GreenHills,

        /// <summary>TANGLE-1's port of shader-lab zone 8. <b>Sorts between RedCairns and TvRoom
        /// by NODE PATH</b>, which is what <c>BubbleCounter</c> assigns ids on — so admitting
        /// this section renumbers the TV room's three bubbles and nothing else.</summary>
        Tangle,
        TvRoom,

        /// <summary>ROOFTOP-1's plot for the puffin lab's roof. <b>Sorts AFTER TvRoom by node path
        /// and carries no bubbles</b>, so admitting it renumbers nothing: bubble ids are the index
        /// of an ordinal node-path sort over the ADOPTED BUBBLES, and this section contributes
        /// none. <c>CheckBubbleCensus</c> prints every section's id range on every run, which is
        /// where that claim is checked rather than asserted here.</summary>
        LabRoof,
    }

    /// <summary>Every section, in scene order.</summary>
    public static readonly Section[] AllSections =
    {
        Section.Hub, Section.RedCairns, Section.BluePrecision,
        Section.CyanRun, Section.GreenHills, Section.Tangle, Section.TvRoom,
        Section.LabRoof,
    };

    /// <summary>The anchor a section's scene root must sit at, with identity rotation.</summary>
    public static Vector3 AnchorOf(Section s) => s switch
    {
        Section.Hub => HubAnchor,
        Section.RedCairns => RedAnchor,
        Section.BluePrecision => BlueAnchor,
        Section.CyanRun => CyanAnchor,
        Section.GreenHills => GreenAnchor,
        Section.Tangle => TangleAnchor,
        Section.TvRoom => TvRoomAnchor,
        Section.LabRoof => LabRoofAnchor,
        _ => Vector3.Zero,
    };

    /// <summary>The section's footprint in WORLD space.</summary>
    public static Aabb FootprintOf(Section s) => s switch
    {
        Section.Hub => HubFootprint,
        Section.RedCairns => RedFootprint,
        Section.BluePrecision => BlueFootprint,
        Section.CyanRun => CyanFootprint,
        Section.GreenHills => GreenFootprint,
        Section.Tangle => TangleFootprint,
        Section.TvRoom => TvRoomFootprint,
        Section.LabRoof => LabRoofFootprint,
        _ => new Aabb(),
    };

    /// <summary>The section's footprint in its OWN space — what a section file's author actually
    /// types. After BT-0's correction every section's footprint is centred on its own anchor, so
    /// this is symmetric for all six.</summary>
    public static Aabb LocalFootprint(Section s)
    {
        Aabb world = FootprintOf(s);
        return new Aabb(world.Position - AnchorOf(s), world.Size);
    }

    /// <summary>Ceiling on how high a section's content may reach above its anchor.</summary>
    public static float MaxHeightOf(Section s) => s switch
    {
        Section.Hub => HubMaxHeight,
        Section.RedCairns => RedMaxHeight,
        Section.BluePrecision => BlueMaxHeight,
        Section.CyanRun => CyanMaxHeight,
        Section.GreenHills => GreenMaxHeight,
        Section.Tangle => TangleMaxHeight,
        Section.TvRoom => TvRoomCeilingM,
        Section.LabRoof => LabRoofMaxHeight,
        _ => 0f,
    };

    /// <summary>The bubble allocation D8 gives this section. BT-11 may move any one of these by
    /// <see cref="BubbleSplitTolerance"/> with a stated reason.</summary>
    public static int BubblesOf(Section s) => s switch
    {
        Section.Hub => HubBubbles,
        Section.RedCairns => RedBubbles,
        Section.BluePrecision => BlueBubbles,
        Section.CyanRun => CyanBubbles,
        Section.GreenHills => GreenBubbles,
        Section.Tangle => TangleBubbles,
        Section.TvRoom => TvRoomBubbles,
        Section.LabRoof => LabRoofBubbles,
        _ => 0,
    };

    /// <summary>Scene path of a section file. BT-1/2/3/4/5/10 each replace the CONTENTS of one
    /// of these; the paths and the root types are frozen here so the world scene never has to be
    /// re-wired.</summary>
    public static string ScenePathOf(Section s) =>
        $"res://scenes/game/world/bubbletest/sections/{s}.tscn";

    /// <summary>The <see cref="SectionVolume"/> node name for a section — the one place the
    /// "Volume_" prefix is written.</summary>
    public static string VolumeNameOf(Section s) => "Volume_" + s;

    /// <summary>The <see cref="SectionVolume"/> trigger box for a section, world space: the
    /// footprint in plan, spanning <see cref="SectionVolumeBottomY"/> upward by
    /// <see cref="SectionVolumeHeight"/> — except the TV room, whose volume is exactly its sealed
    /// box (it is 40 m under everything else, and a column there would swallow the hub and log
    /// every hub crossing twice).</summary>
    public static Aabb VolumeOf(Section s)
    {
        Aabb f = FootprintOf(s);
        if (s == Section.TvRoom) return f;
        // ROOFTOP-1: LabRoof's column starts lower and is taller by the same amount, because its
        // ground is 12–16 m UNDER grade. Same top (+70) as every other section, so the rule "a
        // section volume reaches the top of its own section" is unchanged; only the floor moves,
        // and it moves to a boundary rather than to a margin — see LabRoofVolumeBottomY.
        float bottom = s == Section.LabRoof ? LabRoofVolumeBottomY : SectionVolumeBottomY;
        return new Aabb(
            new Vector3(f.Position.X, bottom, f.Position.Z),
            new Vector3(f.Size.X, SectionVolumeHeight + (SectionVolumeBottomY - bottom), f.Size.Z));
    }

    /// <summary>Spawn <paramref name="index"/>'s offset from the ring's own centre: on the
    /// <see cref="SpawnRingRadius"/> ring, evenly spaced, starting due north (−Z) and going
    /// clockwise. <b>This is the ring's SHAPE and nothing else</b> — for a marker's hub-local
    /// position use <see cref="SpawnPointOf"/>, which adds <see cref="SpawnRingCentre"/>.
    /// Deterministic so <see cref="BubbleTestSelfTest"/> can assert the authored markers against
    /// it to 0.01 m. Program §4 rule 5.</summary>
    public static Vector3 SpawnPosition(int index)
    {
        float a = Mathf.Tau * index / SpawnCount;
        return new Vector3(Mathf.Sin(a) * SpawnRingRadius, 0f, -Mathf.Cos(a) * SpawnRingRadius);
    }

    /// <summary>Spawn <paramref name="index"/>'s marker position, hub-local: the ring's centre
    /// plus its shape. The one value the hub's <c>Spawn*</c> markers are authored against and the
    /// one <see cref="BubbleTestSelfTest"/> checks them with.</summary>
    public static Vector3 SpawnPointOf(int index) => SpawnRingCentre + SpawnPosition(index);

    /// <summary>Stand-in material path for a section's ground / neutral / goal role. BT-9
    /// replaces the FILES in place; these paths are the contract between it and the geometry
    /// packets, which is why they are constants and not typed per scene. Program §4 rule 7.
    /// </summary>
    public static string MaterialPath(string section, string role) =>
        $"res://resources/materials/bubbletest/{section}_{role}.tres";

    private static Aabb FromBounds(float minX, float maxX, float minZ, float maxZ) =>
        new(new Vector3(minX, 0f, minZ), new Vector3(maxX - minX, 0f, maxZ - minZ));
}
