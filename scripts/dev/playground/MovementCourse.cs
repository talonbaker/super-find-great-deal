using System;
using System.Collections.Generic;
using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The seam three MOVE-3 agents build against in parallel.</b> A course is a self-contained
/// lump of playground geometry that the harness (<c>MovementPlayground</c>) drops into the scene
/// at a position of its choosing. Courses know nothing about the harness, the avatar or the
/// camera; the harness knows nothing about what a course contains.
///
/// <para>This file is the orchestrator's contract, written before the courses were dispatched so
/// that every agent compiles from the first minute. <b>Do not change its shape</b> — three
/// sessions are building against it concurrently, and a signature change here is a build break in
/// two worktrees you cannot see. <i>(MOVE-5i, 2026-08-27: the three courses have all landed and the
/// parallel sessions are over. This file has since grown <see cref="SpawnLookAtLocal"/>,
/// <see cref="YawTowards"/> and <see cref="IndexOfCourse"/> — all three purely additive, with the
/// new virtual defaulting to the old behaviour, so no existing course needed an edit. The rule
/// still stands for anything that would change an existing member.)</i></para>
///
/// <para><b>Why geometry in code rather than a hand-authored <c>.tscn</c>:</b> three agents
/// writing one scene file is three-way merge conflict by construction, and this playground is an
/// explicitly throwaway test rig (Talon, 2026-08-26: "no big deal if it's not perfect"). The
/// standing rule that an asset must be openable in a DCC tool governs <i>assets</i>; a dev
/// harness's grey blockout is not one.</para>
/// </summary>
public abstract partial class MovementCourse : Node3D
{
    /// <summary>Short human name, shown in the harness readout so a player can say which part of
    /// the playground a complaint is about.</summary>
    public abstract string CourseName { get; }

    /// <summary>Where the harness should put the player when it teleports them to this course.
    /// Course-local; the harness adds the course's own origin. Must be a spot that is standing on
    /// something.</summary>
    public abstract Vector3 SpawnPointLocal { get; }

    /// <summary>
    /// <b>What a player arriving at <see cref="SpawnPointLocal"/> should be looking at</b> —
    /// course-local, same space as the spawn. <c>null</c> means "no opinion": the harness leaves the
    /// camera exactly where it was, which is what every course did before this channel existed.
    ///
    /// <para><b>Added by MOVE-5i, and virtual rather than abstract for that reason.</b> MOVE-5h
    /// found the scramble spawning with the camera pointed at bare ground — a course whose first
    /// frame is empty plane reads as a broken level — and <see cref="SpawnPointLocal"/> carries a
    /// position and no facing, so there was nothing for a course to say about it. A default of
    /// <c>null</c> keeps every existing course's arrival frame byte-identical; only a course that
    /// answers this gets aimed.</para>
    ///
    /// <para>A point rather than a yaw on purpose: a course author knows where their geometry is and
    /// should not have to do <see cref="YawTowards"/>'s arithmetic — or get its sign wrong — to say
    /// "look at the pile".</para>
    /// </summary>
    public virtual Vector3? SpawnLookAtLocal => null;

    /// <summary>Build the geometry. Called from <see cref="_Ready"/>. Everything you add must be a
    /// child of this node — the harness moves the whole course by moving one transform.</summary>
    protected abstract void Build();

    public override void _Ready() => Build();

    /// <summary>
    /// The look yaw, in radians, that puts <paramref name="target"/> in front of a viewer standing
    /// at <paramref name="from"/>. Y is ignored — this is a heading, not a look-at.
    ///
    /// <para><b>Godot's convention, spelled out because the sign is the whole bug surface:</b> a
    /// node's forward is <c>-Z</c>, and a Y rotation of θ points it at
    /// <c>(-sin θ, 0, -cos θ)</c> — so the heading that faces <c>d = target - from</c> is
    /// <c>atan2(-d.x, -d.z)</c>, not <c>atan2(d.x, d.z)</c>. Matches
    /// <c>SandboxCamera</c>'s <c>Yaw</c>, which is exactly the pivot's Y rotation.</para>
    ///
    /// <para>Degenerate input (the target is the viewer's own position) returns 0 rather than
    /// throwing: a course that names its spawn as its own look-at has said nothing, and the right
    /// answer to nothing is the default heading.</para>
    /// </summary>
    public static float YawTowards(Vector3 from, Vector3 target)
    {
        Vector3 d = target - from;
        var flat = new Vector2(d.X, d.Z);
        return flat.LengthSquared() <= 0f ? 0f : Mathf.Atan2(-flat.X, -flat.Y);
    }

    /// <summary>
    /// <b>Where a named course sits in a roster — by identity, never by ordinal.</b> Returns the
    /// index of the one entry in <paramref name="courseTypes"/> whose type is exactly
    /// <paramref name="wanted"/>.
    ///
    /// <para><b>MOVE-5i exists because of the ordinal this replaces.</b> The scripted capture used
    /// to reach its measurement course with <c>_courseIndex + 1</c> — "the other one", which was
    /// unambiguous while there were two courses and silently became a different course the moment
    /// MOVE-6 added a third. Nothing broke loudly: the capture kept running, kept photographing, and
    /// kept printing a landing speed, and the only symptom was that MOVE-4f's calibrated 17.20 m/s
    /// drop had quietly become a 17.69 m/s one on a jittered rubble pile. An index that means
    /// "somewhere else in the list" is a defect whatever number it currently produces.</para>
    ///
    /// <para><b>It throws rather than returning -1.</b> The failure this guards against is a
    /// selection that keeps working while pointing at the wrong thing, so the one outcome that must
    /// never be silent is "the course this capture is calibrated on is not in the roster". A
    /// duplicate is refused for the same reason — two entries of one type make "the" ambiguous, and
    /// picking the first would be an ordinal again.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The type is absent, or present more than once.</exception>
    public static int IndexOfCourse(IReadOnlyList<Type> courseTypes, Type wanted)
    {
        ArgumentNullException.ThrowIfNull(courseTypes);
        ArgumentNullException.ThrowIfNull(wanted);

        int found = -1;
        for (int i = 0; i < courseTypes.Count; i++)
        {
            if (courseTypes[i] != wanted)
                continue;
            if (found >= 0)
            {
                throw new ArgumentException(
                    $"the roster carries {wanted.Name} twice (at {found} and {i}) — "
                    + "\"the\" course of a type is not a thing a roster with duplicates has, and "
                    + "taking the first would be the ordinal this method exists to replace.",
                    nameof(courseTypes));
            }
            found = i;
        }

        if (found < 0)
        {
            var names = new List<string>(courseTypes.Count);
            foreach (Type t in courseTypes)
                names.Add(t.Name);
            throw new ArgumentException(
                $"no {wanted.Name} in the course roster [{string.Join(", ", names)}] — "
                + "a capture beat calibrated against that course cannot run, and running it "
                + "somewhere else is what MOVE-5i was dispatched to stop.",
                nameof(wanted));
        }

        return found;
    }

    /// <summary>
    /// <b>One solid box: mesh, body and collider together, so a surface cannot ship visual-only.</b>
    ///
    /// <para>Use this for every standable or blocking surface. Building a <c>MeshInstance3D</c>
    /// with no <c>StaticBody3D</c> beside it is the single defect this playground cannot afford —
    /// a platform you fall through wastes the session the playground was built for, and this repo
    /// has already shipped a playtest with no colliders anywhere in it. There is no reason to
    /// hand-roll the pair when this exists.</para>
    /// </summary>
    /// <param name="parent">Node to add the box under — usually <c>this</c>.</param>
    /// <param name="name">Node name; make it descriptive, it is what a debugger shows.</param>
    /// <param name="centre">Centre of the box, in <paramref name="parent"/>'s local space.</param>
    /// <param name="size">Full extents (not half-extents).</param>
    /// <param name="colour">Albedo. Keep the palette flat and readable; see <see cref="Palette"/>.</param>
    /// <returns>The <c>StaticBody3D</c>, so a caller can group or tag it.</returns>
    public static StaticBody3D Block(Node parent, string name, Vector3 centre, Vector3 size,
        Color colour)
    {
        var body = new StaticBody3D { Name = name, Position = centre };

        var mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = CheckerMaterial(colour),
        };
        body.AddChild(mesh);

        var shape = new CollisionShape3D
        {
            Name = "Shape",
            Shape = new BoxShape3D { Size = size },
        };
        body.AddChild(shape);

        parent.AddChild(body);
        return body;
    }

    // --- The metric checker (2026-08-28, Talon's session) -------------------------------------

    /// <summary>
    /// <b>A one-metre world-space checker, on every surface, tinted by the block's own colour.</b>
    /// Talon's ask, and the reasoning is worth keeping: <i>"this will help the player realize their
    /// movement and how fast, and they'll have something to gauge speed and time and distance on."</i>
    ///
    /// <para><b>A flat-shaded greybox is a terrible speedometer</b>, and that is the actual defect
    /// this fixes rather than a decoration. Perceived speed comes almost entirely from texture
    /// flowing past the eye; on an untextured plane the only motion cue left is the horizon, which
    /// barely moves. So a body crossing flat grey at 5.4 m/s and the same body at 8.6 m/s look
    /// nearly identical — which makes every sprint-versus-jog and every chain-jump judgement in
    /// this lab harder than it needs to be, and would have quietly weakened the STRIDE preset most
    /// of all, since that one's whole signal is speed climbing rung by rung.</para>
    ///
    /// <para><b>World-space, not UV.</b> The cell is anchored to world coordinates and is the same
    /// physical size on every surface regardless of how the box is scaled, so it reads as a RULER:
    /// one square is one metre, a 4 m gap is four squares wide, and a distance can be counted off
    /// the floor rather than guessed. A UV checker would stretch with each box and measure
    /// nothing.</para>
    ///
    /// <para><b>Axis picked from the world normal</b> — floors check on XZ, walls on XY or ZY — so
    /// the squares stay square everywhere instead of smearing into stripes down the vertical faces.
    /// Three lines of ALU and no texture: it costs a comparison and a <c>mod</c> per fragment,
    /// which is cheaper than sampling a checker image would have been.</para>
    ///
    /// <para><b>Subtle by construction.</b> The contrast is a &#177;10% modulation of the block's
    /// own albedo rather than a black-and-white pattern, so it reads as surface rather than as
    /// decoration and the palette still carries the meaning. Raise <c>strength</c> if it turns out
    /// to be too quiet on real hardware — that is a taste call, not a structural one.</para>
    /// </summary>
    public static ShaderMaterial CheckerMaterial(Color colour, float cellM = 1.0f,
        float strength = 0.10f)
    {
        var material = new ShaderMaterial { Shader = CheckerShader };
        material.SetShaderParameter("base_color", colour);
        material.SetShaderParameter("cell", cellM);
        material.SetShaderParameter("strength", strength);
        return material;
    }

    /// <summary>Compiled once and shared by every block — one shader, many materials. The code is
    /// inline rather than a <c>.gdshader</c> asset on purpose: this playground is thrown away on
    /// the port (TERRAIN/MOVE handoffs), and a dev-only surface treatment has no business entering
    /// the game's shader catalogue or acquiring an import sidecar.</summary>
    private static readonly Shader CheckerShader = new()
    {
        Code = """
            shader_type spatial;
            render_mode cull_back, diffuse_burley;

            uniform vec3 base_color : source_color = vec3(0.5);
            uniform float cell = 1.0;
            uniform float strength = 0.10;

            varying vec3 world_pos;
            varying vec3 world_nrm;

            void vertex() {
                world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                world_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
            }

            void fragment() {
                // Project onto whichever world plane this face most faces, so squares stay square
                // on floors, walls and sides alike.
                vec3 n = abs(world_nrm);
                vec2 uv = n.y >= max(n.x, n.z)
                    ? world_pos.xz
                    : (n.x >= n.z ? world_pos.zy : world_pos.xy);
                vec2 c = floor(uv / max(cell, 0.001));
                float check = mod(c.x + c.y, 2.0);
                ALBEDO = base_color * mix(1.0 - strength, 1.0 + strength, check);
            }
            """,
    };

    /// <summary>A floating world-space text label, for marking a gap's width or a ledge's height
    /// on the geometry itself. A playground whose distances are only in a design document is one
    /// where the player cannot tell a 4.2 m gap from a 5.8 m one, which is most of the point.</summary>
    public static Label3D Marker(Node parent, string text, Vector3 position, float size = 0.4f)
    {
        var label = new Label3D
        {
            Name = $"Marker_{text}",
            Text = text,
            Position = position,
            FontSize = 64,
            PixelSize = size / 64f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = false,
            Modulate = Palette.Marker,
        };
        parent.AddChild(label);
        return label;
    }

    /// <summary>Flat, readable, deliberately joyless. The playground is a control surface, not an
    /// art pass — grey blockout keeps attention on how the body moves. Courses use the same
    /// palette so two courses side by side read as one place.</summary>
    public static class Palette
    {
        /// <summary>Ground and plazas.</summary>
        public static readonly Color Ground = new(0.38f, 0.40f, 0.42f);

        /// <summary>Ordinary platforms and ledges.</summary>
        public static readonly Color Platform = new(0.52f, 0.54f, 0.56f);

        /// <summary>A surface the spec says should be reachable — a target you are meant to make.</summary>
        public static readonly Color Reachable = new(0.36f, 0.58f, 0.42f);

        /// <summary>A surface the spec says should NOT be reachable. Making one of these is a bug
        /// report, and colouring it is how a player knows to report it.</summary>
        public static readonly Color Unreachable = new(0.62f, 0.36f, 0.36f);

        /// <summary>Walls, pillars, anything you are meant to bump into rather than stand on.</summary>
        public static readonly Color Obstacle = new(0.30f, 0.31f, 0.34f);

        /// <summary>Text markers.</summary>
        public static readonly Color Marker = new(0.95f, 0.93f, 0.80f);

        /// <summary>
        /// <b>THE ROUTE. The one colour in a grey world, and the only thing allowed to wear it.</b>
        ///
        /// <para>Talon, 2026-08-27: <i>"if there are colors please put one block of a different
        /// colour for visual intrigue and make everything gray and plane and make one element color
        /// sort of like a mirror's edge approach."</i> That game paints the world white and picks out
        /// exactly what you can act on in one red, so the route reads at a glance and from any
        /// distance without a marker, an arrow or a tutorial.</para>
        ///
        /// <para><b>It only works if it stays rare.</b> The value of one accent in a grey scene is
        /// entirely a function of nothing else having it — a second decorated thing halves it and a
        /// third kills it. Use this for a surface that is part of the way UP or THROUGH, never for
        /// decoration, and never because a block looked dull.</para>
        ///
        /// <para>ADDED rather than replacing anything: <see cref="Reachable"/> and
        /// <see cref="Unreachable"/> are load-bearing semantics for the calibration course — green
        /// means "the spec says you should make this" and red means "making this is a bug report" —
        /// and quietly repainting them would break a course this file's author is still building.
        /// Whether the whole playground moves to this scheme is Talon's call, not this file's.</para>
        /// </summary>
        public static readonly Color Route = new(0.86f, 0.24f, 0.18f);
    }
}
