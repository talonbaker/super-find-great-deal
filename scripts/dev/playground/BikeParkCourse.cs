using System.Collections.Generic;
using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The Bike Park</b> (BIKE-1b, 2026-09-01) — six lanes side by side, each one a ladder that
/// asks one question of the bike, in the order the answer gets harder. The Roller Run answers
/// "does descent flow" as one line; this park answers the mechanics one at a time, on pieces of
/// ground small enough that a note can name them: <i>"the 0.45 curb stops the bike"</i>,
/// <i>"the 30° kicker sends me 14 m"</i>, <i>"the hairpin needs the drift"</i>.
///
/// <para>Every lane runs along <b>+Z</b> from Z = 0 and is set off in X. Every surface is a
/// <see cref="MovementCourse.Block"/> so the collider cannot disagree with what is drawn, and
/// every lane's start and every rung of every ladder is exposed as a property so the headless
/// self-test stands on the same geometry a note is taken on, rather than on a coordinate typed
/// twice.</para>
///
/// <list type="bullet">
/// <item><b>KICKERS</b> — four lips, 10/20/30/40°, four metres long, off a shared 30 m run-up,
/// onto one long flat deck. The ramp launch (<c>RampLaunchGain</c>) is what makes them lips; with
/// it at 0 they are tables, which is what the shipped motor makes of any slope.</item>
/// <item><b>CURBS</b> — seven full-width steps of rising height along one lane, 0.10 m to 1.00 m.
/// What the bike rolls over, what the hop-on clears, what stops you dead. The capsule's radius
/// sets the physical answer; the ladder makes it a number.</item>
/// <item><b>DROPS</b> — a pit: decks stepping down 1, 2, 3, 4 m, then a 20° ramp back out. The
/// landing dismount window, the kick-off, the landing squash and the stumble-on-landing, each
/// at a height where the arithmetic differs. The return ramp is the climb test for free.</item>
/// <item><b>SLOPES</b> — five humps side by side, 10° to 30°, each 20 m up, 6 m flat, 20 m
/// down. The slope bonus going up (does it bleed to nothing?) and coming down (how fast does it
/// build?), per angle, with a place to turn round at the top.</item>
/// <item><b>SLALOM</b> — pylons alternating either side every 7 m for 60 m, then a hairpin round
/// a divider into a return lane. The drift's course. The ride turn rate without the drift is the
/// control.</item>
/// <item><b>STAIRS</b> — a domestic flight (0.20 rise, 0.40 tread) and a steep one (0.35 / 0.50),
/// a deck, and a 30° ramp down. Stairs are the curb ladder repeated at speed; a bike that stalls
/// on the steep flight is a finding.</item>
/// </list>
/// </summary>
public partial class BikeParkCourse : MovementCourse
{
    public override string CourseName => "Bike Park (bike)";

    private const float ThicknessM = 2f;

    public static readonly float[] KickerAnglesDeg = { 10f, 20f, 30f, 40f };
    public static readonly float[] CurbHeightsM = { 0.05f, 0.10f, 0.15f, 0.20f, 0.30f, 0.45f, 0.60f, 0.80f, 1.00f };
    public static readonly float[] DropHeightsM = { 1f, 2f, 3f, 4f };
    public static readonly float[] HumpAnglesDeg = { 10f, 15f, 20f, 25f, 30f };

    private const float KickerLaneX = -75f;
    private const float KickerSubLaneW = 7f;
    private const float KickerRunUpM = 30f;
    private const float KickerLengthM = 4f;
    private const float KickerDeckM = 50f;

    private const float CurbLaneX = -40f;
    private const float CurbLaneW = 8f;
    private const float CurbFirstZ = 12f;
    private const float CurbPitchM = 9f;

    private const float DropLaneX = -12f;
    private const float DropLaneW = 10f;
    private const float DropDeckM = 12f;

    private const float SlopeLaneX = 24f;
    private const float HumpW = 6f;
    private const float HumpRunM = 20f;
    private const float HumpFlatM = 6f;

    private const float SlalomLaneX = 66f;
    private const float SlalomLaneW = 10f;
    private const float SlalomPitchM = 7f;
    private const float SlalomEndZ = 90f;

    private const float StairsLaneX = 100f;
    private const float StairsLaneW = 6f;

    /// <summary>On the hub pad in front of the lanes, looking down the drop lane.</summary>
    public override Vector3 SpawnPointLocal => new(DropLaneX, 0.5f, -10f);
    public override Vector3? SpawnLookAtLocal => new(DropLaneX, 0f, 40f);

    // --- what the self-test stands on --------------------------------------------------------------

    /// <summary>The start of the kicker run-up for angle index <paramref name="i"/>, on the surface.</summary>
    public Vector3 KickerStartLocal(int i) => new(KickerSubLaneX(i), 0f, 2f);

    /// <summary>Where the lip of kicker <paramref name="i"/> is: the far, high end.</summary>
    public Vector3 KickerLipLocal(int i)
    {
        float rad = Mathf.DegToRad(KickerAnglesDeg[i]);
        return new Vector3(KickerSubLaneX(i), KickerLengthM * Mathf.Sin(rad), KickerRunUpM + KickerLengthM * Mathf.Cos(rad));
    }

    /// <summary>The start of the curb lane, on the surface, before the first curb.</summary>
    public Vector3 CurbLaneStartLocal => new(CurbLaneX, 0f, 2f);

    /// <summary>The Z of curb <paramref name="i"/>'s near face.</summary>
    public float CurbZ(int i) => CurbFirstZ + i * CurbPitchM - 0.5f;

    /// <summary>The top of the first drop deck, on the surface.</summary>
    public Vector3 DropLaneStartLocal => new(DropLaneX, 0f, 2f);

    /// <summary>The surface height of the deck AFTER drop <paramref name="i"/> (0-based), and the Z its
    /// edge is at. Deck 0 is at Y 0 from Z 0 to Z <c>DropDeckM</c>.</summary>
    public (float edgeZ, float belowY) DropEdge(int i)
    {
        float y = 0f;
        for (int k = 0; k <= i; k++) y -= DropHeightsM[k];
        return ((i + 1) * DropDeckM, y);
    }

    /// <summary>Where the swing dummies stand (BIKE-1c): three posts on the hub pad. The first is
    /// the one the self-test walks at.</summary>
    public static readonly Vector3[] DummyLocal =
    {
        new(DropLaneX + 18f, 0f, -8f),
        new(DropLaneX + 22f, 0f, -8f),
        new(DropLaneX + 26f, 0f, -8f),
    };

    /// <summary>The start of the slalom lane, on the surface.</summary>
    public Vector3 SlalomStartLocal => new(SlalomLaneX, 0f, 2f);

    private static float KickerSubLaneX(int i)
        => KickerLaneX + (i - (KickerAnglesDeg.Length - 1) * 0.5f) * KickerSubLaneW;

    protected override void Build()
    {
        // The hub pad every lane starts from: one slab under all the lane starts.
        Block(this, "HubPad", new Vector3(12f, -ThicknessM * 0.5f, -8f),
            new Vector3(210f, ThicknessM, 20f), Palette.Ground);
        Marker(this, "BIKE PARK - six lanes, one question each", new Vector3(12f, 3.5f, -14f), 1.2f);

        for (int i = 0; i < DummyLocal.Length; i++)
        {
            Vector3 d = DummyLocal[i];
            Block(this, $"Dummy{i}", d + new Vector3(0f, 1.0f, 0f), new Vector3(0.6f, 2.0f, 0.6f), Palette.Route);
        }
        Marker(this, "DUMMIES - LMB swings the bike; in the AIR it lunges; Q mid-swing = slingshot",
            DummyLocal[1] + new Vector3(0f, 2.8f, 0f), 0.8f);

        BuildKickers();
        BuildCurbs();
        BuildDrops();
        BuildSlopes();
        BuildSlalom();
        BuildStairs();
    }

    private void BuildKickers()
    {
        float laneW = KickerSubLaneW * KickerAnglesDeg.Length;
        Block(this, "KickerRunUp", new Vector3(KickerLaneX, -ThicknessM * 0.5f, KickerRunUpM * 0.5f),
            new Vector3(laneW, ThicknessM, KickerRunUpM), Palette.Ground);
        Marker(this, "KICKERS - hold forward, leave the lip, SPACE / SPACE / Q",
            new Vector3(KickerLaneX, 2.5f, 2f), 0.9f);

        for (int i = 0; i < KickerAnglesDeg.Length; i++)
        {
            float deg = KickerAnglesDeg[i];
            float rad = Mathf.DegToRad(deg);
            float x = KickerSubLaneX(i);
            // Rotating -deg about X raises the +Z end (the surf park's sign, reversed: a lip goes UP).
            var along = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
            var normal = new Vector3(0f, Mathf.Cos(rad), -Mathf.Sin(rad));
            Vector3 start = new(x, 0f, KickerRunUpM);
            Vector3 centre = start + along * (KickerLengthM * 0.5f) - normal * (ThicknessM * 0.5f);
            StaticBody3D lip = Block(this, $"Kicker{deg:F0}", centre,
                new Vector3(KickerSubLaneW - 0.4f, ThicknessM, KickerLengthM), Palette.Reachable);
            lip.RotationDegrees = new Vector3(-deg, 0f, 0f);
            Marker(this, $"{deg:F0}°", start + along * KickerLengthM + new Vector3(0f, 1.6f, 0f), 0.8f);

            // Distance ticks on the deck, every 5 m from the lip's Z, so a flight can be read
            // off the ground as well as off the readout.
            float lipZ = start.Z + along.Z * KickerLengthM;
            for (int m = 5; m <= 40; m += 5)
                Marker(this, $"{m}", new Vector3(x + KickerSubLaneW * 0.5f - 0.6f, 0.6f, lipZ + m), 0.45f);
        }

        float deckZ0 = KickerRunUpM + KickerLengthM + 0.5f;
        Block(this, "KickerDeck", new Vector3(KickerLaneX, -ThicknessM * 0.5f, deckZ0 + KickerDeckM * 0.5f),
            new Vector3(laneW, ThicknessM, KickerDeckM), Palette.Platform);
        Block(this, "KickerEndWall", new Vector3(KickerLaneX, 2f, deckZ0 + KickerDeckM + 1f),
            new Vector3(laneW, 4f, 2f), Palette.Obstacle);
    }

    private void BuildCurbs()
    {
        float laneLen = CurbFirstZ + CurbPitchM * CurbHeightsM.Length + 6f;
        Block(this, "CurbLane", new Vector3(CurbLaneX, -ThicknessM * 0.5f, laneLen * 0.5f),
            new Vector3(CurbLaneW, ThicknessM, laneLen), Palette.Ground);
        Marker(this, "CURBS - what rolls over, what the hop-on clears, what stops you",
            new Vector3(CurbLaneX, 2.5f, 2f), 0.9f);
        for (int i = 0; i < CurbHeightsM.Length; i++)
        {
            float h = CurbHeightsM[i];
            float z = CurbZ(i) + 0.5f;
            Block(this, $"Curb{h:F2}", new Vector3(CurbLaneX, h * 0.5f, z),
                new Vector3(CurbLaneW, h, 1f), Palette.Obstacle);
            Marker(this, $"{h:F2} m", new Vector3(CurbLaneX - CurbLaneW * 0.5f + 0.8f, h + 0.9f, z), 0.6f);
        }
        // Rails so a body that sidesteps a curb has to come back for it.
        foreach (float side in new[] { -1f, 1f })
            Block(this, side < 0 ? "CurbRailL" : "CurbRailR",
                new Vector3(CurbLaneX + side * (CurbLaneW * 0.5f + 0.5f), 0.5f, laneLen * 0.5f),
                new Vector3(1f, 1f, laneLen), Palette.Obstacle);
    }

    private void BuildDrops()
    {
        Marker(this, "DROPS - land, Q inside the window; or Q up high and kick off",
            new Vector3(DropLaneX, 2.5f, 2f), 0.9f);
        float y = 0f;
        float z = 0f;
        for (int i = 0; i <= DropHeightsM.Length; i++)
        {
            Block(this, $"DropDeck{i}", new Vector3(DropLaneX, y - ThicknessM * 0.5f, z + DropDeckM * 0.5f),
                new Vector3(DropLaneW, ThicknessM, DropDeckM), i == 0 ? Palette.Ground : Palette.Platform);
            if (i < DropHeightsM.Length)
            {
                Marker(this, $"DROP {DropHeightsM[i]:F0} m", new Vector3(DropLaneX, y + 1.8f, z + DropDeckM - 1f), 0.7f);
                // The riser under the edge, so the pit has walls and not a floating stack of decks.
                float h = DropHeightsM[i];
                Block(this, $"DropRiser{i}", new Vector3(DropLaneX, y - h * 0.5f - ThicknessM, z + DropDeckM + 0.5f),
                    new Vector3(DropLaneW, h + ThicknessM * 2f, 1f), Palette.Obstacle);
                y -= h;
            }
            z += DropDeckM;
        }
        // Out of the pit: a 20° ramp back up to the hub level, along +Z, then a return deck.
        float rad = Mathf.DegToRad(20f);
        float rampLen = -y / Mathf.Sin(rad);
        var along = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
        var normal = new Vector3(0f, Mathf.Cos(rad), -Mathf.Sin(rad));
        Vector3 start = new(DropLaneX, y, z);
        StaticBody3D ramp = Block(this, "DropReturnRamp20", start + along * (rampLen * 0.5f) - normal * (ThicknessM * 0.5f),
            new Vector3(DropLaneW, ThicknessM, rampLen), Palette.Reachable);
        ramp.RotationDegrees = new Vector3(-20f, 0f, 0f);
        Marker(this, "20° back up - the climb test", start + along * (rampLen * 0.5f) + normal * 2f, 0.8f);
        Vector3 top = start + along * rampLen;
        Block(this, "DropReturnDeck", new Vector3(DropLaneX, -ThicknessM * 0.5f, top.Z + 8f),
            new Vector3(DropLaneW, ThicknessM, 16f), Palette.Ground);
        // Pit side walls.
        float pitLen = z + 2f;
        foreach (float side in new[] { -1f, 1f })
            Block(this, side < 0 ? "PitWallL" : "PitWallR",
                new Vector3(DropLaneX + side * (DropLaneW * 0.5f + 0.5f), y * 0.5f - 1f, pitLen * 0.5f),
                new Vector3(1f, -y + 4f, pitLen), Palette.Obstacle);
    }

    private void BuildSlopes()
    {
        Marker(this, "SLOPES - up (does the bonus die?), over, down (how fast does it build?)",
            new Vector3(SlopeLaneX, 2.5f, 2f), 0.9f);
        for (int i = 0; i < HumpAnglesDeg.Length; i++)
        {
            float deg = HumpAnglesDeg[i];
            float rad = Mathf.DegToRad(deg);
            float x = SlopeLaneX + (i - (HumpAnglesDeg.Length - 1) * 0.5f) * HumpW;
            var up = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
            var upNormal = new Vector3(0f, Mathf.Cos(rad), -Mathf.Sin(rad));
            Vector3 c = new(x, 0f, 6f);
            Block(this, $"HumpLead{deg:F0}", new Vector3(x, -ThicknessM * 0.5f, 3f),
                new Vector3(HumpW - 0.3f, ThicknessM, 6f), Palette.Ground);

            StaticBody3D upBlock = Block(this, $"HumpUp{deg:F0}", c + up * (HumpRunM * 0.5f) - upNormal * (ThicknessM * 0.5f),
                new Vector3(HumpW - 0.3f, ThicknessM, HumpRunM), Palette.Reachable);
            upBlock.RotationDegrees = new Vector3(-deg, 0f, 0f);
            Marker(this, $"{deg:F0}°", c + up * (HumpRunM * 0.5f) + upNormal * 2.2f, 0.8f);
            c += up * HumpRunM;

            Block(this, $"HumpTop{deg:F0}", new Vector3(x, c.Y - ThicknessM * 0.5f, c.Z + HumpFlatM * 0.5f),
                new Vector3(HumpW - 0.3f, ThicknessM, HumpFlatM), Palette.Platform);
            c.Z += HumpFlatM;

            var down = new Vector3(0f, -Mathf.Sin(rad), Mathf.Cos(rad));
            var downNormal = new Vector3(0f, Mathf.Cos(rad), Mathf.Sin(rad));
            StaticBody3D downBlock = Block(this, $"HumpDown{deg:F0}", c + down * (HumpRunM * 0.5f) - downNormal * (ThicknessM * 0.5f),
                new Vector3(HumpW - 0.3f, ThicknessM, HumpRunM), Palette.Reachable);
            downBlock.RotationDegrees = new Vector3(deg, 0f, 0f);
            c += down * HumpRunM;

            Block(this, $"HumpRunOut{deg:F0}", new Vector3(x, -ThicknessM * 0.5f, c.Z + 15f),
                new Vector3(HumpW - 0.3f, ThicknessM, 30f), Palette.Ground);
        }
    }

    private void BuildSlalom()
    {
        float returnX = SlalomLaneX + SlalomLaneW + 2f;
        float laneLen = SlalomEndZ + 2f;
        Block(this, "SlalomLane", new Vector3(SlalomLaneX + (SlalomLaneW + 2f) * 0.5f, -ThicknessM * 0.5f, laneLen * 0.5f),
            new Vector3(SlalomLaneW * 2f + 2f, ThicknessM, laneLen), Palette.Ground);
        Marker(this, "SLALOM - weave; HAIRPIN at the end: right mouse and steer",
            new Vector3(SlalomLaneX, 2.5f, 2f), 0.9f);
        int n = 0;
        for (float z = 8f; z <= 64f; z += SlalomPitchM, n++)
        {
            float side = (n % 2 == 0) ? -1f : 1f;
            Block(this, $"Pylon{n}", new Vector3(SlalomLaneX + side * 3f, 1f, z),
                new Vector3(0.8f, 2f, 0.8f), Palette.Route);
        }
        // The hairpin: an end wall across both lanes, a divider up the middle from Z 70, so the
        // turn is a 180 around the divider's tip into the return lane.
        Block(this, "HairpinEndWall", new Vector3(SlalomLaneX + (SlalomLaneW + 2f) * 0.5f, 2f, SlalomEndZ + 1f),
            new Vector3(SlalomLaneW * 2f + 2f, 4f, 2f), Palette.Obstacle);
        Block(this, "HairpinDivider", new Vector3(SlalomLaneX + SlalomLaneW * 0.5f + 1f, 1.5f, (70f + SlalomEndZ) * 0.5f),
            new Vector3(2f, 3f, SlalomEndZ - 70f), Palette.Obstacle);
        Marker(this, "HAIRPIN", new Vector3(SlalomLaneX + SlalomLaneW * 0.5f + 1f, 4f, 69f), 0.9f);
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side < 0 ? SlalomLaneX - SlalomLaneW * 0.5f - 0.5f : returnX + SlalomLaneW * 0.5f + 0.5f;
            Block(this, side < 0 ? "SlalomRailL" : "SlalomRailR", new Vector3(x, 0.75f, laneLen * 0.5f),
                new Vector3(1f, 1.5f, laneLen), Palette.Obstacle);
        }
    }

    private void BuildStairs()
    {
        Marker(this, "STAIRS - domestic, then steep; a stall is a finding",
            new Vector3(StairsLaneX, 2.5f, 2f), 0.9f);
        Block(this, "StairsLead", new Vector3(StairsLaneX, -ThicknessM * 0.5f, 5f),
            new Vector3(StairsLaneW, ThicknessM, 10f), Palette.Ground);
        float y = 0f, z = 10f;
        (float rise, float tread, int count, string name)[] flights =
        {
            (0.20f, 0.40f, 10, "Domestic"),
            (0.35f, 0.50f, 6, "Steep"),
        };
        foreach (var f in flights)
        {
            Marker(this, $"{f.name} {f.rise:F2} / {f.tread:F2}", new Vector3(StairsLaneX, y + 2.2f, z), 0.7f);
            for (int i = 0; i < f.count; i++)
            {
                y += f.rise;
                Block(this, $"{f.name}Step{i}", new Vector3(StairsLaneX, y - (ThicknessM + y) * 0.5f, z + f.tread * 0.5f),
                    new Vector3(StairsLaneW, ThicknessM + y, f.tread), Palette.Platform);
                z += f.tread;
            }
            Block(this, $"{f.name}Landing", new Vector3(StairsLaneX, y - (ThicknessM + y) * 0.5f, z + 3f),
                new Vector3(StairsLaneW, ThicknessM + y, 6f), Palette.Ground);
            z += 6f;
        }
        // Down again: a 30° ramp to the ground and a run-out.
        float rad = Mathf.DegToRad(30f);
        float rampLen = y / Mathf.Sin(rad);
        var along = new Vector3(0f, -Mathf.Sin(rad), Mathf.Cos(rad));
        var normal = new Vector3(0f, Mathf.Cos(rad), Mathf.Sin(rad));
        Vector3 start = new(StairsLaneX, y, z);
        StaticBody3D ramp = Block(this, "StairsDown30", start + along * (rampLen * 0.5f) - normal * (ThicknessM * 0.5f),
            new Vector3(StairsLaneW, ThicknessM, rampLen), Palette.Reachable);
        ramp.RotationDegrees = new Vector3(30f, 0f, 0f);
        Vector3 end = start + along * rampLen;
        Block(this, "StairsRunOut", new Vector3(StairsLaneX, -ThicknessM * 0.5f, end.Z + 10f),
            new Vector3(StairsLaneW, ThicknessM, 20f), Palette.Ground);
    }
}
