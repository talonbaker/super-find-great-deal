using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The roller run</b> (BIKE-0, 2026-09-01) — the first course in this playground built for
/// going DOWN. The surf park is a ladder of gradients side by side, which answers "at what angle
/// does a slope start paying"; this is one continuous line, top to bottom, which answers the
/// question the bike packet actually asks: <i>does momentum-based descent flow</i>.
///
/// <para>Read from the top: a plateau to summon on, a 12° opener, a shelf, a 2.5 m lip to drop
/// off, an 18° run, three rollers to pump, a 5 m gap to jump (and to mount across, mid-air), a
/// 20° landing slope, and a long flat run-out where the dismount stumble can be felt against the
/// foot cap. Every surface is a rotated <see cref="MovementCourse.Block"/>, so the collider can
/// never disagree with what is drawn, and the sections are laid end to end by one cursor so a
/// change to any angle moves everything below it rather than opening a seam.</para>
///
/// <para>Descends along <b>+Z</b>. A positive pitch about X drops the +Z end of a block, which is
/// the convention the surf park's ramps established and measured.</para>
/// </summary>
public partial class DescentCourse : MovementCourse
{
    public override string CourseName => "Roller Run (bike)";

    private const float WidthM = 14f;
    private const float ThicknessM = 2f;
    private const float TopY = 40f;

    /// <summary>On the plateau, a few metres back from the lip, facing down the run.</summary>
    public override Vector3 SpawnPointLocal => new(0f, TopY + 0.5f, -6f);
    public override Vector3? SpawnLookAtLocal => new(0f, TopY - 10f, 60f);

    /// <summary>The top-surface point the next section starts from. X is always 0.</summary>
    private Vector3 _cursor;

    /// <summary>Where the flat run-out's top surface begins, in this course's local space —
    /// derived from the section list as it is built, so the self-test that stands on it moves
    /// with any change to the angles above it.</summary>
    public Vector3 RunOutStartLocal { get; private set; }

    protected override void Build()
    {
        _cursor = new Vector3(0f, TopY, -12f);

        Ramp("Plateau", 12f, 0f, Palette.Platform);
        Marker(this, "SUMMON HERE (Q)", new Vector3(0f, TopY + 2.2f, -8f), 1.0f);

        Ramp("Opener12", 30f, 12f, Palette.Reachable);
        Ramp("Shelf", 6f, 0f, Palette.Platform);

        // The lip: the surface simply continues 2.5 m lower. A drop, not a slope.
        Marker(this, "DROP", _cursor + new Vector3(0f, 2.5f, 0f), 0.8f);
        _cursor.Y -= 2.5f;
        Ramp("Run18", 36f, 18f, Palette.Reachable);

        for (int i = 0; i < 3; i++)
        {
            Ramp($"RollerUp{i}", 8f, -10f, Palette.Platform);
            Ramp($"RollerDown{i}", 12f, 14f, Palette.Reachable);
        }

        // The gap. Nothing here for 5 m; the far side starts 1 m lower so a body that only just
        // clears it lands on a slope rather than into a wall.
        Marker(this, "GAP 5 m - jump, then Q in the air", _cursor + new Vector3(0f, 2.5f, 1f), 0.8f);
        _cursor.Z += 5f;
        _cursor.Y -= 1f;
        Ramp("Landing20", 40f, 20f, Palette.Unreachable);

        RunOutStartLocal = _cursor;
        Ramp("RunOut", 46f, 0f, Palette.Ground);
        Marker(this, "RUN-OUT: Q here to feel the stumble", _cursor + new Vector3(0f, 2.2f, -20f), 0.8f);

        // A back wall so the run has an end, and low rails the length of the run so a carve that
        // goes wide falls onto a rail and not off the world.
        Block(this, "EndWall", _cursor + new Vector3(0f, 3f, 1f), new Vector3(WidthM + 2f, 6f, 2f),
            Palette.Obstacle);
        float length = _cursor.Z + 12f;
        float midZ = (_cursor.Z - 12f) * 0.5f;
        float midY = (TopY + _cursor.Y) * 0.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            Block(this, side < 0 ? "RailL" : "RailR",
                new Vector3(side * (WidthM * 0.5f + 0.5f), midY - 1f, midZ),
                new Vector3(1f, TopY - _cursor.Y + 12f, length), Palette.Obstacle);
        }
    }

    /// <summary>
    /// One section from the cursor: a block whose TOP surface starts at the cursor and runs
    /// <paramref name="lengthM"/> along +Z, pitched down by <paramref name="angleDeg"/> (negative
    /// pitches UP). Advances the cursor to the far end of that top surface.
    /// </summary>
    private void Ramp(string name, float lengthM, float angleDeg, Color colour)
    {
        float rad = Mathf.DegToRad(angleDeg);
        // Local +Z after a rotation of +rad about X tilts down: (0, -sin, cos). Local +Y is the
        // surface normal: (0, cos, sin).
        var along = new Vector3(0f, -Mathf.Sin(rad), Mathf.Cos(rad));
        var normal = new Vector3(0f, Mathf.Cos(rad), Mathf.Sin(rad));

        Vector3 centre = _cursor + along * (lengthM * 0.5f) - normal * (ThicknessM * 0.5f);
        StaticBody3D block = Block(this, name, centre, new Vector3(WidthM, ThicknessM, lengthM), colour);
        block.RotationDegrees = new Vector3(angleDeg, 0f, 0f);

        if (Mathf.Abs(angleDeg) > 0.5f)
            Marker(this, $"{angleDeg:F0}°", centre + normal * 2.5f, 0.9f);

        _cursor += along * lengthM;
    }
}
