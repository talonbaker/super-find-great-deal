using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The surf park (2026-08-28, Talon's session) — the first course in this playground with
/// GRADIENTS.</b>
///
/// <para><b>Why it exists.</b> Talon asked for shield-surfing / hoverboard flow — <i>"surfing is
/// fluid, they're so fluid in motion, it's a nice rhythm to be carving in and out"</i> — and
/// every game he named for it (Jak's hoverboard, BOTW shield surfing, the TOTK bikes) is
/// fundamentally about SLOPES. <c>AvatarMotor</c> reads the floor normal nowhere: the motor is
/// flat-world, so a hill is scenery and running down one gives exactly what running along a plain
/// gives. The other three courses are, without exception, flat tops and vertical walls. <b>There
/// was no ground in this lab on which surfing could even be attempted.</b> This is that ground.</para>
///
/// <para><b>Every surface is a rotated box.</b> <see cref="MovementCourse.Block"/> builds
/// axis-aligned geometry with a matching <c>BoxShape3D</c>, and rotating the returned
/// <c>StaticBody3D</c> turns mesh and collider together — so a ramp is one call plus one rotation
/// and the collision can never disagree with what is drawn. That property is worth more here than
/// anywhere else in the playground: a slope whose collider is a step reads as a bug in the
/// movement rather than a bug in the level.</para>
///
/// <para><b>The gradients are a LADDER, not a mood.</b> 8°, 15°, 22°, 30° and 38°, side by side and
/// all the same length, because the first question a slope prototype has to answer is
/// <i>at what angle does this start being fun</i> — and that is unanswerable on a park whose hills
/// are all different in three ways at once. The steepest is 38° rather than something more
/// dramatic because Godot's default <c>floor_max_angle</c> is 45°: past that a body stops being
/// "on the floor" at all and slides as an ungrounded object, which is a different experiment and
/// would silently contaminate this one.</para>
///
/// <para><b>It is deliberately the largest course here</b> (about 200 m across, against the flow
/// course's ~60). Speed needs room: a surf that ends after two seconds is a ramp, not a run, and
/// Talon asked for a bigger area in the same breath as the board.</para>
/// </summary>
public partial class SurfCourse : MovementCourse
{
    public override string CourseName => "Surf Park";

    /// <summary>Top of the drop-in, so the first thing a player does here is point downhill.</summary>
    public override Vector3 SpawnPointLocal => new(0f, 26f, -78f);

    /// <summary>Down the hill. A course about gradients that opens facing the flat is a course
    /// whose whole subject is behind the player.</summary>
    public override Vector3? SpawnLookAtLocal => new(0f, 0f, 0f);

    // The ladder. One length, one width, five angles — see the class doc for why that shape.
    private static readonly float[] RampAnglesDeg = { 8f, 15f, 22f, 30f, 38f };
    private const float RampLengthM = 46f;
    private const float RampWidthM = 16f;
    private const float RampThicknessM = 2f;
    private const float RampGapM = 4f;

    protected override void Build()
    {
        BuildApron();
        BuildRampLadder();
        BuildDropIn();
        BuildBowl();
        BuildRollers();
        BuildRunOut();
    }

    /// <summary>
    /// The flat plate everything drains onto. Large, because the whole point of a slope is the
    /// speed you leave it with, and a run-out that ends in a wall measures nothing.
    /// </summary>
    private void BuildApron()
    {
        Block(this, "Apron", new Vector3(0f, -1f, 0f), new Vector3(200f, 2f, 200f),
            Palette.Ground);
    }

    /// <summary>
    /// <b>The gradient ladder</b> — five ramps of identical length and width at 8/15/22/30/38°,
    /// laid out left to right so a player can run them back to back and feel where the slope
    /// starts paying.
    ///
    /// <para>Each ramp is pitched about X and then LIFTED so its downhill lip meets the apron
    /// rather than passing through it. The lift is <c>sin(angle) x length / 2</c> — the height the
    /// centre rises when a plank of that length is tilted about its middle — so every ramp in the
    /// ladder touches down at the same place regardless of its angle. Getting that wrong is how a
    /// gradient ladder turns into a step ladder.</para>
    /// </summary>
    private void BuildRampLadder()
    {
        float spacing = RampWidthM + RampGapM;
        float x0 = -(RampAnglesDeg.Length - 1) * spacing * 0.5f;

        for (int i = 0; i < RampAnglesDeg.Length; i++)
        {
            float deg = RampAnglesDeg[i];
            float rad = Mathf.DegToRad(deg);
            float lift = Mathf.Sin(rad) * RampLengthM * 0.5f;

            // Centre sits half a ramp-length up the slope from the apron edge, lifted so the lower
            // lip lands on the apron.
            var centre = new Vector3(x0 + i * spacing, lift, -RampLengthM * 0.5f - 6f);
            StaticBody3D ramp = Block(this, $"Ramp{deg:F0}", centre,
                new Vector3(RampWidthM, RampThicknessM, RampLengthM),
                i >= 3 ? Palette.Unreachable : Palette.Reachable);

            // NEGATIVE pitch drops the far (-Z) end away from the player and raises the near end,
            // which is the direction a body spawned uphill actually runs.
            ramp.RotationDegrees = new Vector3(deg, 0f, 0f);

            Marker(this, $"{deg:F0}°", centre + new Vector3(0f, lift * 0.5f + 3f, 0f), 1.2f);
        }
    }

    /// <summary>
    /// The drop-in: a tall, steep, narrow chute that dumps onto the middle of the ladder's landing
    /// zone. It exists so a run can START fast — an experiment about carrying speed that makes the
    /// player build it from a standstill every time is mostly measuring the acceleration ramp.
    /// </summary>
    private void BuildDropIn()
    {
        var centre = new Vector3(0f, 13f, -78f);
        StaticBody3D chute = Block(this, "DropIn", centre, new Vector3(14f, 2f, 56f),
            Palette.Platform);
        chute.RotationDegrees = new Vector3(28f, 0f, 0f);

        // A back wall, so the spawn cannot be walked off the wrong way.
        Block(this, "DropInBack", new Vector3(0f, 27f, -104f), new Vector3(16f, 6f, 2f),
            Palette.Obstacle);
    }

    /// <summary>
    /// <b>The bowl — four walls leaned inward around a flat floor.</b> This is the carving test:
    /// a surface with no single fall line, where holding speed means choosing an arc rather than
    /// pointing downhill. Four ramps rather than a curved mesh because a box is a box — the
    /// collider is exactly the visible surface, and a swept curve in blockout is a lot of geometry
    /// to get a shape a player will read as "a bowl" from four planes anyway.
    /// </summary>
    private void BuildBowl()
    {
        const float floorHalf = 22f;
        const float wallLen = 30f;
        const float lean = 26f;
        var origin = new Vector3(64f, 0f, 40f);

        Block(this, "BowlFloor", origin + new Vector3(0f, -1f, 0f),
            new Vector3(floorHalf * 2f, 2f, floorHalf * 2f), Palette.Ground);

        float rad = Mathf.DegToRad(lean);
        float lift = Mathf.Sin(rad) * wallLen * 0.5f;
        float inset = Mathf.Cos(rad) * wallLen * 0.5f;

        StaticBody3D north = Block(this, "BowlN",
            origin + new Vector3(0f, lift, -(floorHalf + inset)),
            new Vector3(floorHalf * 2f, 2f, wallLen), Palette.Reachable);
        north.RotationDegrees = new Vector3(-lean, 0f, 0f);

        StaticBody3D south = Block(this, "BowlS",
            origin + new Vector3(0f, lift, floorHalf + inset),
            new Vector3(floorHalf * 2f, 2f, wallLen), Palette.Reachable);
        south.RotationDegrees = new Vector3(lean, 0f, 0f);

        StaticBody3D west = Block(this, "BowlW",
            origin + new Vector3(-(floorHalf + inset), lift, 0f),
            new Vector3(wallLen, 2f, floorHalf * 2f), Palette.Reachable);
        west.RotationDegrees = new Vector3(0f, 0f, lean);

        StaticBody3D east = Block(this, "BowlE",
            origin + new Vector3(floorHalf + inset, lift, 0f),
            new Vector3(wallLen, 2f, floorHalf * 2f), Palette.Reachable);
        east.RotationDegrees = new Vector3(0f, 0f, -lean);

        Marker(this, "BOWL", origin + new Vector3(0f, 6f, 0f), 2.0f);
    }

    /// <summary>
    /// <b>The rollers — alternating up and down slopes in a line.</b> This is the PUMP test, and
    /// the one piece of this course that is about rhythm rather than gradient: a body that times
    /// something at the bottom of each dip should leave faster than it arrived, and a body that
    /// does not should bleed speed on every rise.
    ///
    /// <para>Nothing implements pumping yet. The terrain is built first on purpose — a rhythm
    /// mechanic with nowhere to practise it is the mistake the chain jump already made, where the
    /// reward existed and the ground it should have been earned on did not.</para>
    /// </summary>
    private void BuildRollers()
    {
        var origin = new Vector3(-70f, 0f, 30f);
        const float segLen = 18f;
        const float segAngle = 17f;
        const int pairs = 4;

        float rad = Mathf.DegToRad(segAngle);
        float dz = Mathf.Cos(rad) * segLen;
        float dy = Mathf.Sin(rad) * segLen;

        float z = -pairs * dz;
        float y = 0f;
        for (int i = 0; i < pairs * 2; i++)
        {
            bool down = i % 2 == 0;
            float pitch = down ? segAngle : -segAngle;
            var centre = origin + new Vector3(0f, y + (down ? -dy : dy) * 0.5f, z + dz * 0.5f);
            StaticBody3D seg = Block(this, $"Roller{i}", centre,
                new Vector3(18f, 2f, segLen), Palette.Platform);
            seg.RotationDegrees = new Vector3(pitch, 0f, 0f);

            y += down ? -dy : dy;
            z += dz;
        }

        Marker(this, "ROLLERS", origin + new Vector3(0f, 8f, 0f), 1.6f);
    }

    /// <summary>A long clear lane off the foot of the ladder. Its only job is to be empty: it is
    /// where you find out how far the speed you built actually carries, which is a question a park
    /// full of features cannot answer.</summary>
    private void BuildRunOut()
    {
        Block(this, "RunOutL", new Vector3(-30f, 1f, 70f), new Vector3(2f, 4f, 90f),
            Palette.Obstacle);
        Block(this, "RunOutR", new Vector3(30f, 1f, 70f), new Vector3(2f, 4f, 90f),
            Palette.Obstacle);
        Marker(this, "RUN-OUT", new Vector3(0f, 4f, 60f), 1.6f);
    }
}
