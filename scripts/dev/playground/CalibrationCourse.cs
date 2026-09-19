using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The measured half of the playground: does the movement do what the spec says it does?</b>
/// Ledge and gap banks that deliberately bracket the real jump, so some are makeable and some are
/// not, and a player discovers which by trying rather than by reading a table.
///
/// <para>Built from <c>docs/design/2026-08-26-airborne-control-and-jump-shape.md</c> §8, but
/// coloured and labelled against the <b>measured</b> jump values MOVE-3b read off the shipped
/// motor rather than §8's predictions. Where the two disagree the measurement wins and the marker
/// text says so — see <see cref="SprintHeldApexM"/> and friends.</para>
///
/// <para><b>Layout, in course-local metres</b> (footprint 60 x 60, x and z both -30..30):</para>
/// <list type="bullet">
/// <item>z &lt; 0 is solid ground: the 30 x 30 plaza in the middle (x -15..15) with the sprint and
/// skid lanes on it, the ledge bank on the west slab, the two stair sets on the east slab.</item>
/// <item>z &gt; 0 is the air work: parallel lanes over their own recovery floors, one x band per
/// element, nothing overlapping anything else.</item>
/// </list>
///
/// <para><b>Colour is the bug report.</b> <c>Palette.Reachable</c> means the arithmetic says you
/// should make it; <c>Palette.Unreachable</c> means it says you should not. Landing on a red one
/// is a defect worth reporting, and that is the only reason the two colours exist.</para>
/// </summary>
public partial class CalibrationCourse : MovementCourse
{
    // ---------------------------------------------------------------------------------------
    // Measured jump values (MOVE-3b, off the shipped motor). These are facts, not predictions;
    // every verdict below is computed against them.
    //
    // *** MOVE-8 (2026-08-28): THESE FOUR ARE STALE, DELIBERATELY, AND THE WHOLE COURSE WITH
    // THEM. *** Talon's ruling moved MotorTuning.Default (MoveSpeed 5.4 -> 3.8, Gravity 22 -> 24,
    // FallGravityMultiplier 1.35 -> 1.50, ApexHangStrength 0 -> 0.30, plus a traditional double
    // jump), and the real arc is now held sprint 1.407 m / 4.053 m and jog tap 0.300 m / 0.887 m —
    // computed live by MotorArc (scripts/net/MotorArc.cs), which is where any NEW consumer must
    // read it from. They are NOT updated here because this course's geometry is authored greybox
    // sized against these exact numbers, and re-spacing geometry is outside MOVE-8's scope. The
    // consequence is specific and worth stating plainly: the CALIBRATION course now calibrates
    // against a motor that no longer exists, so every "impossible"/"clears by" verdict printed on
    // its markers is wrong until the course is re-spaced. That re-space is its own packet.
    // Marked rather than silently left, per MOVE-8 scope item 4.
    // ---------------------------------------------------------------------------------------

    /// <summary>Apex of a fully held jump from a sprint, measured. §8 predicted 1.60 m.</summary>
    private const float SprintHeldApexM = 1.534f;

    /// <summary>Horizontal distance of a fully held sprint jump, measured. §8 predicted 6.13 m.</summary>
    private const float SprintHeldDistanceM = 6.192f;

    /// <summary>Apex of a minimum tap from a jog, measured. §8 predicted 0.67 m — it is 0.203 m
    /// lower than that, which is the single biggest disagreement on this course.</summary>
    private const float JogTapApexM = 0.467f;

    /// <summary>Horizontal distance of a minimum tap from a jog, measured. §8 predicted 1.93 m.</summary>
    private const float JogTapDistanceM = 1.710f;

    /// <summary>Held jog jump distance. <b>Not measured</b> — §8 §3.3's 3.83 m is carried forward
    /// and the markers that depend on it say "predicted".</summary>
    private const float JogHeldDistancePredictedM = 3.83f;

    /// <summary>Depth every raised lane platform is extruded to so it meets its own recovery floor
    /// exactly. Matches §8.3's "1.25 m above the recovery floor".</summary>
    private const float LaneDrop = 1.25f;

    public override string CourseName => "Calibration";

    /// <summary>Standing on the plaza at the south end of the sprint run-up lane, facing +Z down
    /// the 24 m lane. One metre up so the harness's teleport never spawns interpenetrating.</summary>
    public override Vector3 SpawnPointLocal => new(-6f, 1f, -29f);

    protected override void Build()
    {
        BuildGround();
        BuildPlazaLanes();
        BuildLedgeBank();
        BuildGapBank();
        BuildAirBrake();
        BuildRedirectPads();
        BuildCoyoteLedge();
        BuildBufferPad();
        BuildHopChains();
        BuildPrecisionTarget();
        BuildStairs();
    }

    // ---------------------------------------------------------------------------------------
    // Helpers. Every standable or blocking surface in this file goes through Block() via Slab().
    // ---------------------------------------------------------------------------------------

    /// <summary>An axis-aligned box given by its extents rather than centre-and-size, because the
    /// whole course is laid out as edges and gaps and converting by hand is how a 4.2 m gap
    /// quietly becomes a 4.1 m one. <paramref name="topY"/> is the standing surface;
    /// <paramref name="thickness"/> extrudes downward from it.</summary>
    private StaticBody3D Slab(string name, float x0, float x1, float z0, float z1, float topY,
        float thickness, Color colour)
        => Block(this, name,
            new Vector3((x0 + x1) * 0.5f, topY - thickness * 0.5f, (z0 + z1) * 0.5f),
            new Vector3(x1 - x0, thickness, z1 - z0), colour);

    /// <summary>The 0.1 m takeoff lip §8 asks every raised platform for. It replaces the last
    /// 0.1 m of the platform rather than sitting on top of it, so the takeoff point is a colour
    /// change and not a 2 cm step the motor has to climb.</summary>
    private void Lip(string name, float x0, float x1, float zEdge, float topY, float thickness)
        => Slab(name, x0, x1, zEdge - 0.1f, zEdge, topY, thickness, Palette.Obstacle);

    /// <summary>A flush floor stripe for the plaza's distance marks: 2 cm proud of the plaza, which
    /// is under any step height and reads as paint.</summary>
    private void FloorMark(string name, float x0, float x1, float z, Color colour)
        => Slab(name, x0, x1, z - 0.1f, z + 0.1f, 0.02f, 0.02f, colour);

    private void Mark(string text, float x, float y, float z, float size = 0.4f)
        => Marker(this, text, new Vector3(x, y, z), size);

    // ---------------------------------------------------------------------------------------
    // 8.1 Plaza and ground
    // ---------------------------------------------------------------------------------------

    /// <summary>Three flush slabs make the whole z &lt; 0 half walkable: the 30 x 30 plaza plus a
    /// west and an east apron, so every lane in the z &gt; 0 half has a run-up that starts on solid
    /// ground instead of in mid-air.</summary>
    private void BuildGround()
    {
        Slab("Plaza", -15f, 15f, -30f, 0f, 0f, 1f, Palette.Ground);
        Slab("WestGround", -30f, -15f, -30f, 0f, 0f, 1f, Palette.Ground);
        Slab("EastGround", 15f, 30f, -30f, 0f, 0f, 1f, Palette.Ground);
    }

    /// <summary>Sprint lane and skid lane — §8.1. Both are paint, not geometry: the ramp distances
    /// and the reversal distances are existing ground behaviour, included so a regression in them
    /// is visible without instrumentation.</summary>
    private void BuildPlazaLanes()
    {
        // Sprint lane: 24 m of straight, marked where each gear is actually reached.
        const float sprintStartZ = -29.5f;
        FloorMark("SprintLane_Start", -7.5f, -4.5f, sprintStartZ, Palette.Obstacle);
        FloorMark("SprintLane_Jog", -7.5f, -4.5f, sprintStartZ + 1.62f, Palette.Reachable);
        FloorMark("SprintLane_Sprint", -7.5f, -4.5f, sprintStartZ + 4.15f, Palette.Reachable);
        FloorMark("SprintLane_End", -7.5f, -4.5f, sprintStartZ + 24f, Palette.Obstacle);
        Mark("sprint lane - start", -6f, 1.2f, sprintStartZ);
        Mark("jog reached 1.62 m", -6f, 1.2f, sprintStartZ + 1.62f);
        Mark("sprint reached 4.15 m", -6f, 1.2f, sprintStartZ + 4.15f);
        Mark("24 m", -6f, 1.2f, sprintStartZ + 24f);

        // Skid lane: same 24 m run-up, then a line and 1 m marks for 6 m past it.
        const float skidStartZ = -30f;
        const float skidLineZ = skidStartZ + 24f;
        FloorMark("SkidLane_Start", 4.5f, 7.5f, skidStartZ + 0.2f, Palette.Obstacle);
        FloorMark("SkidLane_Line", 4.5f, 7.5f, skidLineZ, Palette.Obstacle);
        Mark("skid lane - reverse at the line", 6f, 1.2f, skidLineZ);
        for (int i = 1; i <= 6; i++)
        {
            // The 6 m mark lands exactly on the plaza's north edge; pull it 0.1 m back on.
            float z = Mathf.Min(skidLineZ + i, -0.1f);
            FloorMark($"SkidLane_{i}m", 4.5f, 7.5f, z, Palette.Platform);
            Mark($"{i} m", 7.8f, 0.5f, z, 0.3f);
        }

        Mark("jog reversal 1.06 m", 6f, 0.9f, skidLineZ + 1.06f, 0.3f);
        Mark("sprint reversal 2.82 m", 6f, 0.9f, skidLineZ + 2.82f, 0.3f);
    }

    // ---------------------------------------------------------------------------------------
    // 8.2 Ledge bank — brackets min and max apex
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Six 3 m ledges off the west apron, approached at jog. <b>Two of §8.2's verdicts do not
    /// survive the measurement, and the bank needed a sixth rung because of it.</b> §8.2 called
    /// 0.40 m "a tap clears it easily" and 0.60 m "the tap's practical ceiling (0.07 m of margin)"
    /// on a predicted 0.67 m tap apex. The measured tap apex is 0.467 m: 0.40 m clears by 0.067 m —
    /// tight, not easy — and 0.60 m does not clear on a tap at all, which left the real tap ceiling
    /// unbracketed with nothing sitting on the boundary.
    ///
    /// <para><b>0.45 m is that boundary rung</b> (added by the coordinator's design call, MOVE-3e
    /// follow-up): a tap clears it by 0.017 m and nothing more. It is the rung that makes variable
    /// jump height legible — the one a player <i>just</i> makes with a tap — and 0.60 m directly
    /// above it is then the first rung a tap cannot make, which is the other half of the bracket.
    /// Both stay <c>Reachable</c>, because both are still reachable with a held jump; the marker
    /// text carries the correction so nobody reads the colour as "tap it".</para>
    /// </summary>
    private void BuildLedgeBank()
    {
        (float z0, float z1, float height, bool reachable, string note)[] ledges =
        {
            (-28f, -25f, 0.40f, true, "curb - tap clears by 0.067 m"),
            (-25f, -22f, 0.45f, true, "THE BOUNDARY - tap clears by 0.017 m"),
            (-22f, -19f, 0.60f, true, "TAP FAILS (apex 0.467) - hold needed"),
            (-19f, -16f, 1.00f, true, "partly held jump"),
            (-16f, -13f, 1.45f, true, "full hold, 0.084 m margin"),
            (-13f, -10f, 1.80f, false, "impossible - max apex 1.534"),
        };

        Mark("LEDGE BANK - jog approach, apex test", -17.5f, 3.0f, -29f, 0.6f);

        foreach (var (z0, z1, height, reachable, note) in ledges)
        {
            string tag = $"{height:0.00}m";
            var colour = reachable ? Palette.Reachable : Palette.Unreachable;
            Slab($"Ledge_{tag}", -22f, -19.1f, z0, z1, height, height, colour);
            // Approach-edge lip: the last 0.1 m in x, so the takeoff/landing edge is unambiguous.
            Slab($"LedgeLip_{tag}", -19.1f, -19f, z0, z1, height, height, Palette.Obstacle);
            Mark($"{height:0.00} m - {note}", -20.5f, height + 0.7f, (z0 + z1) * 0.5f);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 8.3 Gap bank — brackets max jump distance
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Five parallel lanes with 12 m of run-up each, takeoff lip and landing pad flush at the same
    /// height, 1.25 m over a shared recovery floor. Verdicts recomputed against the measured
    /// 6.192 m sprint jump and 1.710 m jog tap: 5.8 m gains margin (0.39 m rather than §8.3's
    /// 0.33 m) and 6.6 m stays honestly impossible. The 1.6 m lane gets tighter, not looser — §8.3
    /// leaned on a 1.93 m tap and the real one is 1.710 m, leaving 0.11 m.
    /// </summary>
    private void BuildGapBank()
    {
        const float runupEndZ = 12f;

        Slab("GapBank_Recovery", -30f, -11f, 0f, 23f, -LaneDrop, 1f, Palette.Ground);
        Mark("GAP BANK - sprint distance test", -20.5f, 2.4f, 1.5f, 0.6f);

        (float centreX, float gap, bool reachable, string note)[] lanes =
        {
            (-28.5f, 1.6f, true, "standing jump (1.02) fails, jog tap clears by 0.11"),
            (-24.5f, 3.4f, true, "held jog (3.83 predicted) clears"),
            (-20.5f, 4.2f, true, "jog fails, sprint clears"),
            (-16.5f, 5.8f, true, "sprint only - 6.192 m, 0.39 m margin"),
            (-12.5f, 6.6f, false, "impossible - max measured 6.192 m"),
        };

        foreach (var (centreX, gap, reachable, note) in lanes)
        {
            float x0 = centreX - 1.5f;
            float x1 = centreX + 1.5f;
            string tag = $"{gap:0.0}m";
            var colour = reachable ? Palette.Reachable : Palette.Unreachable;

            Slab($"GapRunup_{tag}", x0, x1, 0f, runupEndZ - 0.1f, 0f, LaneDrop, Palette.Platform);
            Lip($"GapLip_{tag}", x0, x1, runupEndZ, 0f, LaneDrop);
            Slab($"GapLanding_{tag}", x0, x1, runupEndZ + gap, runupEndZ + gap + 3f, 0f, LaneDrop,
                colour);

            Mark($"{gap:0.0} m", centreX, 1.6f, runupEndZ + gap * 0.5f, 0.6f);
            Mark(note, centreX, 1.0f, runupEndZ + gap * 0.5f, 0.28f);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 8.4 Air-brake A/B — the specific thing being fixed
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The one element whose <i>before</i> state is worth capturing. A sprint jump lands at 6.192 m
    /// holding forward and about 4.55 m with input released for the whole flight — both on pad B.
    /// Pad A (1.6-2.6 m past the lip) is only reachable if airborne input can still brake the body
    /// to a near stop, which is exactly what MOVE-3b removes. Landing on A after MOVE-3b is the
    /// regression.
    /// </summary>
    private void BuildAirBrake()
    {
        const float lipZ = 22f;

        Slab("AirBrake_Recovery", -9f, -5f, 0f, 29f, -LaneDrop, 1f, Palette.Ground);
        Slab("AirBrake_Runup", -9f, -5f, 0f, lipZ - 0.1f, 0f, LaneDrop, Palette.Platform);
        Lip("AirBrake_Lip", -9f, -5f, lipZ, 0f, LaneDrop);

        Slab("AirBrake_PadA", -9f, -5f, lipZ + 1.6f, lipZ + 2.6f, 0f, LaneDrop, Palette.Unreachable);
        Slab("AirBrake_PadB", -9f, -5f, lipZ + 4.4f, lipZ + 6.6f, 0f, LaneDrop, Palette.Reachable);

        Mark("AIR-BRAKE A/B - 24 m sprint lane", -7f, 2.4f, 1.5f, 0.6f);
        Mark("PAD A 1.6-2.6 m - must NOT be reachable", -7f, 1.6f, lipZ + 2.1f, 0.42f);
        Mark("landing on A = the air-brake is still 100%", -7f, 1.0f, lipZ + 2.1f, 0.28f);
        Mark("PAD B 4.4-6.6 m", -7f, 1.6f, lipZ + 5.5f, 0.5f);
        Mark("held 6.192 m / released ~4.55 m - both land here", -7f, 1.0f, lipZ + 5.5f, 0.28f);
    }

    // ---------------------------------------------------------------------------------------
    // 8.5 Redirect pads — air control, measured
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Three pads off one jog lip. The reachable redirect is mirrored to +x and the unreachable one
    /// to -x rather than sitting side by side on the same flank: adjacent pads let a player simply
    /// walk from the green one onto the red one, which destroys the only signal the colour carries.
    /// Both keep §8.5's exact lateral offsets (1.5 m and 3.0 m).
    /// </summary>
    private void BuildRedirectPads()
    {
        const float lipZ = 8f;
        const float laneX0 = 1f;
        const float laneX1 = 3f;

        Slab("Redirect_Recovery", -3f, 5.5f, 0f, 14f, -LaneDrop, 1f, Palette.Ground);
        Slab("Redirect_Runup", laneX0, laneX1, 0f, lipZ - 0.1f, 0f, LaneDrop, Palette.Platform);
        Lip("Redirect_Lip", laneX0, laneX1, lipZ, 0f, LaneDrop);

        // Control case: straight ahead, 3.8 m forward.
        Slab("Redirect_Straight", laneX0, laneX1, lipZ + 3.8f, lipZ + 5.3f, 0f, LaneDrop,
            Palette.Reachable);
        // 1.5 m lateral: a diagonal wish from a jog takeoff buys 1.56 m of lateral in 0.710 s.
        Slab("Redirect_Right1_5", 3.5f, 5f, lipZ + 2.9f, lipZ + 4.4f, 0f, LaneDrop,
            Palette.Reachable);
        // 3.0 m lateral: the ceiling made visible.
        Slab("Redirect_Left3_0", -2.5f, -1f, lipZ + 2.9f, lipZ + 4.4f, 0f, LaneDrop,
            Palette.Unreachable);

        Mark("REDIRECT - jog lip, air control", 2f, 2.4f, 1.5f, 0.6f);
        Mark("straight 3.8 m", 2f, 1.4f, lipZ + 4.55f, 0.42f);
        Mark("1.5 m lateral / 2.9 m fwd", 4.25f, 1.4f, lipZ + 3.65f, 0.36f);
        Mark("3.0 m lateral - unreachable", -1.75f, 1.4f, lipZ + 3.65f, 0.36f);
    }

    // ---------------------------------------------------------------------------------------
    // 8.6 Coyote ledge
    // ---------------------------------------------------------------------------------------

    /// <summary>12 m run-up, a lip, a 3.4 m gap and a 3.0 m drop under it — the drop is deeper than
    /// every other lane's so that failing the coyote window reads as a fall rather than a stumble.
    /// The two floating marks are where the window closes at jog (0.65 m past the lip) and at
    /// sprint (1.04 m); they hang in the air because that is literally where they are.</summary>
    private void BuildCoyoteLedge()
    {
        const float lipZ = 12f;
        const float drop = 3f;

        Slab("Coyote_Recovery", 7f, 11f, 0f, 19f, -drop, 1f, Palette.Ground);
        Slab("Coyote_Platform", 7f, 11f, 0f, lipZ - 0.1f, 0f, drop, Palette.Platform);
        Lip("Coyote_Lip", 7f, 11f, lipZ, 0f, drop);
        Slab("Coyote_Landing", 7f, 11f, lipZ + 3.4f, lipZ + 6.4f, 0f, drop, Palette.Reachable);

        Mark("COYOTE LEDGE - 3.4 m gap over a 3.0 m drop", 9f, 2.4f, 1.5f, 0.6f);
        Mark("jog window closes 0.65 m past the lip", 9f, 0.7f, lipZ + 0.65f, 0.3f);
        Mark("sprint window closes 1.04 m past the lip", 9f, 1.3f, lipZ + 1.04f, 0.3f);
        Mark("3.4 m", 9f, 2.0f, lipZ + 1.7f, 0.5f);
    }

    // ---------------------------------------------------------------------------------------
    // 8.7 Buffer pad
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A 2.0 m drop onto a 1.0 m pad and a 3.4 m gap straight after it: about 0.185 s of ground
    /// contact at jog, so clearing the gap needs the jump pressed inside the 0.12 s before
    /// touchdown or that 0.185 s on the pad. The pad is placed where a jog walk-off actually lands
    /// (2.30 m of travel during the 0.426 s fall) rather than flush under the lip, or the geometry
    /// would test the fall arc instead of the buffer.
    /// </summary>
    private void BuildBufferPad()
    {
        const float lipZ = 6f;
        const float lower = -2f;

        Slab("Buffer_Recovery", 13f, 17f, 0f, 16f, -4f, 1f, Palette.Ground);
        Slab("Buffer_Approach", 13f, 17f, 0f, lipZ - 0.1f, 0f, 4f, Palette.Platform);
        Lip("Buffer_Lip", 13f, 17f, lipZ, 0f, 4f);

        // 1.0 m deep pad, 2.0 m below the lip.
        Slab("Buffer_Pad", 13f, 17f, 7.8f, 8.8f, lower, 2f, Palette.Reachable);
        // 3.4 m gap, then somewhere to arrive.
        Slab("Buffer_Landing", 13f, 17f, 12.2f, 15.2f, lower, 2f, Palette.Reachable);

        Mark("BUFFER PAD - 2.0 m drop, 1.0 m of contact, 3.4 m gap", 15f, 2.4f, 1.5f, 0.55f);
        Mark("2.0 m drop", 15f, 0.8f, lipZ + 0.9f, 0.4f);
        Mark("1.0 m pad - ~0.185 s of contact at jog", 15f, -0.6f, 8.3f, 0.32f);
        Mark("3.4 m", 15f, -0.4f, 10.5f, 0.5f);
    }

    // ---------------------------------------------------------------------------------------
    // 8.8 Hop chains — two rhythms, and the landing-cost test
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Both chains are the landing-cost test. With <c>LandingSpeedMultiplier = 1.0</c> the fifth hop
    /// is identical to the first; any landing friction MOVE-3b introduces makes the hold chain fail
    /// at pad four or five, visibly, with no instrumentation. The measured tap distance (1.710 m)
    /// makes both verdicts stronger than §8.8's: it falls further short of the hold chain's 3.2 m
    /// than the predicted 1.93 m did, and it clears the tap chain's 1.5 m by 0.21 m rather than
    /// 0.43 m — a genuinely minimum hop, which is the point of that chain.
    /// </summary>
    private void BuildHopChains()
    {
        // Hold chain: 5 pads, 2.0 m deep, centres 5.2 m apart => 3.2 m edge-to-edge.
        Slab("HoldChain_Recovery", 17f, 21f, 0f, 24f, -LaneDrop, 1f, Palette.Ground);
        Mark("HOLD CHAIN - 3.2 m edge-to-edge, held jumps only", 19f, 2.4f, -1.2f, 0.5f);
        for (int i = 0; i < 5; i++)
        {
            float z0 = i * 5.2f;
            Slab($"HoldPad_{i + 1}", 18.4f, 19.6f, z0, z0 + 2f, 0f, LaneDrop, Palette.Reachable);
            Mark($"hold {i + 1}", 19f, 1.2f, z0 + 1f, 0.3f);
            if (i < 4)
            {
                Mark("3.2 m", 19f, 1.7f, z0 + 3.6f, 0.4f);
            }
        }
        Mark("tap (1.710 m) falls short", 19f, 0.8f, 3.6f, 0.28f);

        // Tap chain: 5 pads, 1.5 m deep, centres 3.0 m apart => 1.5 m edge-to-edge.
        Slab("TapChain_Recovery", 23f, 27f, 0f, 15f, -LaneDrop, 1f, Palette.Ground);
        Mark("TAP CHAIN - 1.5 m edge-to-edge, minimum hops", 25f, 2.4f, -1.2f, 0.5f);
        for (int i = 0; i < 5; i++)
        {
            float z0 = i * 3f;
            Slab($"TapPad_{i + 1}", 24.4f, 25.6f, z0, z0 + 1.5f, 0f, LaneDrop, Palette.Reachable);
            Mark($"tap {i + 1}", 25f, 1.2f, z0 + 0.75f, 0.3f);
            if (i < 4)
            {
                Mark("1.5 m", 25f, 1.7f, z0 + 2.25f, 0.4f);
            }
        }
        Mark("tap 1.710 m clears by 0.21 m", 25f, 0.8f, 2.25f, 0.28f);
    }

    // ---------------------------------------------------------------------------------------
    // 8.9 Precision target and stairs
    // ---------------------------------------------------------------------------------------

    /// <summary>A 1 x 1 m pad with its near edge 3.4 m past a jog lip, so its centre sits at 3.9 m
    /// against the 3.83 m held jog jump. That 3.83 m is <b>predicted, not measured</b> — MOVE-3b
    /// measured the sprint hold and the jog tap, not the jog hold — so the marker says so and a
    /// miss here is a calibration reading rather than a defect.</summary>
    private void BuildPrecisionTarget()
    {
        const float lipZ = 8f;

        Slab("Precision_Recovery", 27f, 30f, 0f, 14f, -LaneDrop, 1f, Palette.Ground);
        Slab("Precision_Runup", 27.5f, 30f, 0f, lipZ - 0.1f, 0f, LaneDrop, Palette.Platform);
        Lip("Precision_Lip", 27.5f, 30f, lipZ, 0f, LaneDrop);
        Slab("Precision_Target", 28.25f, 29.25f, lipZ + 3.4f, lipZ + 4.4f, 0f, LaneDrop,
            Palette.Reachable);

        Mark("PRECISION - 1 x 1 m target", 28.75f, 2.4f, 1.5f, 0.5f);
        Mark("near edge 3.4 m, centre 3.9 m", 28.75f, 1.5f, lipZ + 3.9f, 0.34f);
        Mark("vs 3.83 m held jog (predicted, unmeasured)", 28.75f, 1.0f, lipZ + 3.9f, 0.26f);
    }

    /// <summary>
    /// The traversal regression guard. Walkable stairs are 0.25 m x 0.60 m, under the minimum hop,
    /// so they must be climbable without ever pressing jump — stairs that fight the motor are the
    /// classic way a jump change breaks ordinary walking. Jump stairs are 0.75 m x 1.20 m: above
    /// the measured 0.467 m tap apex and well under the 1.534 m held apex, so every step needs a
    /// held jump and none of them is a coin flip.
    /// </summary>
    private void BuildStairs()
    {
        Mark("WALKABLE STAIRS - 0.25 x 0.60, no jump", 21f, 2.6f, -13f, 0.5f);
        for (int i = 1; i <= 5; i++)
        {
            float h = i * 0.25f;
            float x0 = 18f + (i - 1) * 0.6f;
            Slab($"WalkStep_{i}", x0, x0 + 0.6f, -12f, -8f, h, h, Palette.Platform);
        }
        Slab("WalkStep_Landing", 21f, 24f, -12f, -8f, 1.25f, 1.25f, Palette.Reachable);
        Mark("1.25 m, reached on foot", 22.5f, 2.0f, -10f, 0.4f);

        Mark("JUMP STAIRS - 0.75 x 1.20, held jump each step", 21f, 3.6f, -21f, 0.5f);
        for (int i = 1; i <= 3; i++)
        {
            float h = i * 0.75f;
            float x0 = 18f + (i - 1) * 1.2f;
            Slab($"JumpStep_{i}", x0, x0 + 1.2f, -20f, -16f, h, h, Palette.Reachable);
            Mark($"{h:0.00} m", x0 + 0.6f, h + 0.6f, -15.6f, 0.32f);
        }
        Slab("JumpStep_Landing", 21.6f, 24.6f, -20f, -16f, 2.25f, 2.25f, Palette.Reachable);
        Mark("0.75 m > tap apex 0.467 - tap cannot climb", 22.1f, 3.0f, -18f, 0.32f);
    }
}
