using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>BIKE-2x-L (2026-09-02): the shader lab's zone-6 rolling heightfield, ported as a course</b> —
/// BIKE-2b, parked since 2026-09-01, unparked because both the bike camera's speed curves and the
/// drift's exit are otherwise judged entirely on plank geometry. This is the only continuous
/// surface in the roster: everything else is discrete block-to-block traversal, and carrying
/// momentum up over a rise and down the far side is a different verb from any of it.
///
/// <para><b>Provenance.</b> The construction is <c>_zone_rolling</c> / <c>_hill</c> / <c>_taper</c>
/// from the shader lab's <c>scripts/terrain/terrain_gen.gd</c> (repo <c>shader-lab</c>, branch
/// <c>feat/2026-08-27-terrain-lab</c>): a single ArrayMesh heightfield over a
/// ConcavePolygonShape3D, two sine octaves plus three gaussian bumps for landmarks, the whole
/// field lifted clear of the base plate and multiplied by a border taper that reaches zero at the
/// patch edge, and three cone mounds sitting ON the rolling ground. Its three hard-won lessons are
/// kept, not re-learned:</para>
///
/// <list type="bullet">
/// <item><b>Backface collision ON.</b> A ConcavePolygonShape3D collides on front faces only by
/// default, and physics front-facing did not agree with rendering front-facing in the source —
/// the hills drew correctly while a body walked clean under them. Not belt-and-braces; without it
/// the terrain is solid only from underneath.</item>
/// <item><b>The taper band is wide because a narrow one is a cliff.</b> The source's first taper
/// made the rim a 47-degree face that the body slid off; spreading the same rise over the outer
/// 60 % of the half-width is what makes the patch ground you ride onto.</item>
/// <item><b>The mound collider is a convex hull of the cone mesh, never a CylinderShape3D.</b> A
/// cylinder is a vertical wall, so every mound becomes an unclimbable pillar and the zone stops
/// testing continuous-surface movement — the only reason it exists.</item>
/// </list>
///
/// <para><b>Scaled for the bike, and the scaling is stated rather than smuggled.</b> The source
/// patch is 30 m, sized for a foot motor: at the bike's 9.424 m/s ride cap it is three seconds of
/// ground. Plan is scaled x3 (90 m patch) and height x1.25, which cuts the source's gradients to
/// ~42 % — the steepest sampled flank lands at ~33 degrees, real enough to load
/// <c>BikeLayer.SlopeBonusMps</c> both ways and still shallow enough to ride up. The field is the
/// source's field magnified, not a new one: same octaves, same bumps, same taper, evaluated in
/// source coordinates and rescaled, so a finding about "the rise past the second mound" maps
/// straight back to the zone Talon has already seen.</para>
///
/// <para><b>Pure math is static and engine-free</b> (<see cref="HeightAt"/>), the shape
/// <c>BikeHandling</c> set: <c>RollingCourseTests</c> pins the border at zero, the field above the
/// plate, and the gradient under the slide-off limit, without an engine in the room.</para>
/// </summary>
public partial class RollingCourse : MovementCourse
{
    public override string CourseName => "Rolling Hills (bike, zone-6 port)";

    /// <summary>On the field just inside the -Z border, where the taper has the ground low and
    /// rising ahead — the first thing a rider sees is the first rise, not the horizon.</summary>
    public override Vector3 SpawnPointLocal => new(0f, HeightAt(0f, SpawnZ) + 0.6f, SpawnZ);

    public override Vector3? SpawnLookAtLocal => new(0f, HeightAt(0f, 0f), 0f);

    /// <summary>Public so the unit tests can pin the spawn's ground without constructing a Node
    /// in an engine-free xUnit host (the MotorTuning.TryApply segfault lesson, generalized).</summary>
    public const float SpawnZ = -38f;

    // --- The field's numbers, all derived from the source's ------------------------------------

    /// <summary>Patch side, metres. Source: 30 (HILL_SIZE); x3 for the bike's speed.</summary>
    public const float SizeM = 90f;

    /// <summary>Plan magnification against the source field. Height positions and bump centres
    /// are evaluated in source coordinates (world / this).</summary>
    private const float PlanScale = 3f;

    /// <summary>Height magnification. 1.25 against the source, so gradients come out at ~42 % of
    /// the source's (rise x1.25 over run x3) — the port's own unit test measured the source field
    /// carrying ~57-degree flanks on its bump-plus-octave stacks, and 1.5 still left 38 degrees in
    /// this field. 1.25 lands the steepest sampled gradient at ~33 degrees, under the 35-degree
    /// rideability bound <c>RollingCourseTests</c> pins.</summary>
    private const float HeightScale = 1.25f;

    /// <summary>Source: HILL_LIFT, the clearance that keeps every trough above the base plate.</summary>
    private const float SourceLift = 2.15f;

    /// <summary>Grid resolution per side. 60 -> 3600 quads, the same density per metre bracket as
    /// the source's 40 over 30 m; the rises read smooth and the collider stays cheap.</summary>
    private const int Res = 60;

    /// <summary>The base plate's top sits 2 cm below the field's tapered border so the border ring
    /// cannot z-fight the plate it meets.</summary>
    private const float PlateDropM = 0.02f;

    /// <summary>
    /// The field height at course-local (x, z), metres — the source's <c>_hill</c> x
    /// <c>_taper</c>, evaluated in source coordinates and rescaled. Zero outside the patch, zero
    /// exactly at the border (the taper's smoothstep reaches 0 there), never negative anywhere:
    /// the lift clears the source field's ~-2 m troughs and the taper only scales toward zero.
    /// </summary>
    public static float HeightAt(float x, float z)
    {
        float u = x / PlanScale;
        float w = z / PlanScale;

        // The source's two sine octaves — "the frequencies ARE the zone": roughly two cycles
        // across the source patch is what makes rises instead of one smooth dome.
        float h = Mathf.Sin(u * 0.42f) * Mathf.Cos(w * 0.38f) * 1.9f
                + Mathf.Sin(u * 0.90f + 1.3f) * Mathf.Sin(w * 0.85f) * 0.65f;

        // The source's three gaussian landmarks, at the source's centres.
        h += Bump(u, w, -6f, -5f, 5.0f, 2.1f);
        h += Bump(u, w, 7f, 3f, 4.0f, 1.7f);
        h += Bump(u, w, 0f, 10f, 6.0f, 1.2f);

        // Clamped at the plate, and the clamp is a measured fix rather than paranoia: the source's
        // own octave troughs reach about -2.55 m before its bumps help, so (h + 2.15) goes ~7 cm
        // NEGATIVE in spots — the source field dips through its own plate and nobody ever saw it,
        // because backface collision made it physically moot and 7 cm is invisible under a 30 m
        // field. RollingCourseTests.TheFieldNeverDipsBelowThePlate is what surfaced it here.
        return MathF.Max(h + SourceLift, 0f) * Taper(x, z) * HeightScale;
    }

    /// <summary>1 in the middle easing to 0 at the border — Chebyshev distance so the taper
    /// follows the square border instead of inscribing a circle in it. The 0.40 onset is the
    /// source's widened band: the whole rim rise spread over ~60 % of the half-width, which is
    /// what keeps the border under the slide-off gradient.</summary>
    private static float Taper(float x, float z)
    {
        float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) / (SizeM * 0.5f);
        return 1f - Mathf.SmoothStep(0.40f, 1f, Mathf.Min(e, 1f));
    }

    private static float Bump(float u, float w, float cx, float cz, float r, float amp)
    {
        float d2 = (u - cx) * (u - cx) + (w - cz) * (w - cz);
        return amp * Mathf.Exp(-d2 / (r * r));
    }

    // --- Geometry -------------------------------------------------------------------------------

    protected override void Build()
    {
        // The plate the patch sits on, oversized so a border overrun lands on ground and not in
        // the void. Its top is PlateDropM below y=0, where the tapered border ring lands.
        Block(this, "BasePlate", new Vector3(0f, -0.5f - PlateDropM, 0f),
            new Vector3(SizeM + 24f, 1f, SizeM + 24f), Palette.Ground);

        BuildHeightfield();

        // The source's three cone mounds, on the field: steep enough to be a different problem
        // from the rises, with no flat top to stand on. Positions x PlanScale, radius x2 and
        // height x HeightScale — wider than plan-proportional so their faces stay climbable.
        Mound("MoundA", new Vector3(-21f, HeightAt(-21f, -18f), -18f), 6.4f, 4.25f);
        Mound("MoundB", new Vector3(18f, HeightAt(18f, 6f), 6f), 4.8f, 5.75f);
        Mound("MoundC", new Vector3(3f, HeightAt(3f, 27f), 27f), 8.4f, 2.75f);

        Marker(this, "zone-6 port  90 m  cont. surface", new Vector3(0f, HeightAt(0f, 0f) + 3.2f, 0f));
    }

    private void BuildHeightfield()
    {
        var verts = new Vector3[(Res + 1) * (Res + 1)];
        var normals = new Vector3[verts.Length];
        var indices = new int[Res * Res * 6];

        for (int j = 0; j <= Res; j++)
        {
            for (int i = 0; i <= Res; i++)
            {
                float x = (i / (float)Res - 0.5f) * SizeM;
                float z = (j / (float)Res - 0.5f) * SizeM;
                verts[j * (Res + 1) + i] = new Vector3(x, HeightAt(x, z), z);
            }
        }

        // Central differences for the normals, exactly as the source: the height function is a sum
        // of sines plus gaussians and differentiating it by hand is how you get a seam.
        float e = SizeM / Res;
        for (int j = 0; j <= Res; j++)
        {
            for (int i = 0; i <= Res; i++)
            {
                float x = (i / (float)Res - 0.5f) * SizeM;
                float z = (j / (float)Res - 0.5f) * SizeM;
                float dx = HeightAt(x + e, z) - HeightAt(x - e, z);
                float dz = HeightAt(x, z + e) - HeightAt(x, z - e);
                normals[j * (Res + 1) + i] = new Vector3(-dx, 2f * e, -dz).Normalized();
            }
        }

        int k = 0;
        for (int j = 0; j < Res; j++)
        {
            for (int i = 0; i < Res; i++)
            {
                int a = j * (Res + 1) + i;
                int b = a + 1;
                int c = a + Res + 1;
                int d = c + 1;
                indices[k++] = a; indices[k++] = c; indices[k++] = b;
                indices[k++] = b; indices[k++] = c; indices[k++] = d;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var body = new StaticBody3D { Name = "RollingBody" };
        AddChild(body);
        body.AddChild(new MeshInstance3D
        {
            Name = "RollingMesh",
            Mesh = mesh,
            MaterialOverride = CheckerMaterial(Palette.Platform),
        });

        // The collider is the render mesh's own triangles, so the two cannot disagree.
        var faces = new Vector3[indices.Length];
        for (int i = 0; i < indices.Length; i++)
            faces[i] = verts[indices[i]];
        var concave = new ConcavePolygonShape3D { BackfaceCollision = true };
        concave.SetFaces(faces);
        body.AddChild(new CollisionShape3D { Name = "Shape", Shape = concave });
    }

    /// <summary>A cone mound: CylinderMesh with a zero top radius, collided by a convex hull OF
    /// THAT MESH — exact for a convex solid, and the only collider that keeps the faces rideable
    /// (see the class doc for the cylinder-wall lesson this ports).</summary>
    private void Mound(string name, Vector3 at, float r, float h)
    {
        var body = new StaticBody3D { Name = name, Position = at + new Vector3(0f, h * 0.5f, 0f) };
        AddChild(body);

        var mesh = new CylinderMesh
        {
            TopRadius = 0f,
            BottomRadius = r,
            Height = h,
            RadialSegments = 14,
        };
        body.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
            MaterialOverride = CheckerMaterial(Palette.Obstacle),
        });
        body.AddChild(new CollisionShape3D { Name = "Shape", Shape = mesh.CreateConvexShape() });
    }
}
