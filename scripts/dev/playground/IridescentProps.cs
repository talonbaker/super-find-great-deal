using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The things in the playground that are worth looking at.</b> Crystals and bubbles, built from
/// the structural-colour shaders developed in the shader lab and copied here under
/// <c>resources/shaders/iridescent/</c>.
///
/// <para><b>Why any of this is in a grey blockout at all.</b> Talon, planning a playtest:
/// <i>"I want to put this up as the playtest and I want something more interesting than just
/// blocks."</i> A course made only of grey boxes measures traversal perfectly and gives a playtester
/// nothing to want. These are what a player climbs TOWARD — and being iridescent rather than merely
/// bright, they change as you move around them, which is the one kind of decoration that rewards
/// the thing this playground is testing.</para>
///
/// <para><b>They are cheap, and the cheapness was measured rather than assumed.</b> Costed in the
/// shader lab at two resolutions, so per-pixel cost separates from fixed cost:</para>
///
/// <code>
///   bubble_film     0.206 ms/megapixel   0 texture fetches   0 backbuffer copies   ALU-leaning
///   crystal_facet   0.217 ms/megapixel   0 texture fetches   0 backbuffer copies   ALU-leaning
/// </code>
///
/// <para><b>crystal_facet, NOT crystal_refraction.</b> The refractive variant declares
/// <c>hint_screen_texture</c>, and that DECLARATION — not any fetch — makes Godot blit a
/// full-resolution copy of the frame before the material draws. That cost is paid whether the
/// crystal fills the screen or nine pixels. The faceted variant has no copy and no fetch, and at
/// playground distances the two are hard to tell apart.</para>
///
/// <para><b>What a bubble actually costs is TRANSPARENCY</b>: blend queue, back-to-front sorting,
/// and <c>cull_disabled</c> doubling its own overdraw. All three scale with the screen area the
/// bubbles cover and how much they overlap — NOT with the frame. A dozen small ones are nearly free;
/// two filling the screen are not. <b>Both numbers above are a ranking on an RTX 4070 and are NOT a
/// floor.</b> This set is ALU-leaning, which degrades harder toward low-end hardware than
/// bandwidth-bound work does. Measure on the floor target before going much past a dozen.</para>
/// </summary>
public static class IridescentProps
{
    private const string CrystalShader = "res://resources/shaders/iridescent/crystal_facet.gdshader";
    private const string BubbleShader = "res://resources/shaders/iridescent/bubble_film.gdshader";

    private static Material? _crystalMat;
    private static ShaderMaterial? _bubbleMat;

    /// <summary>
    /// One material per KIND, shared by every prop of that kind. A scene of forty crystals each
    /// carrying its own <c>ShaderMaterial</c> is forty material binds a frame for one look.
    /// </summary>
    private static Material CrystalMaterial()
    {
        if (_crystalMat != null) return _crystalMat;

        // MOVE-1: the MVP extraction kept `bubble_film` and cut `crystal_facet`, so on this repo the
        // faceted shader is simply not there. Loading a missing path would spam the log with a load
        // error every summit and hand back a material with a null shader, which renders as nothing.
        // The lab is a movement lab, not a material lab: fall back to an opaque tinted solid that
        // keeps the summit reward readable at the same silhouette. Never re-import the cut asset.
        var shader = ResourceLoader.Exists(CrystalShader) ? GD.Load<Shader>(CrystalShader) : null;
        if (shader is null)
        {
            _crystalMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.24f, 0.66f, 0.92f),
                Metallic = 0.35f,
                Roughness = 0.18f,
                EmissionEnabled = true,
                Emission = new Color(0.10f, 0.28f, 0.40f),
                EmissionEnergyMultiplier = 0.45f,
            };
            return _crystalMat;
        }

        var m = new ShaderMaterial { Shader = shader };

        // DEEPENED FROM THE SHIPPED DEFAULTS, which are a pale near-clear glass. That is right for
        // judging a material on a turntable and wrong here: photographed on a grey block under a
        // grey sky, the default read as a frosted pebble. A reward has to be worth the climb from
        // the ground. Every dial below is the shader's own.
        m.SetShaderParameter("body_tint", new Vector3(0.24f, 0.66f, 0.92f));
        m.SetShaderParameter("absorption", 1.45f);
        m.SetShaderParameter("thickness", 1.5f);
        m.SetShaderParameter("facet_steps", 4.0f);      // LOW is more faceted
        m.SetShaderParameter("sparkle", 0.75f);
        m.SetShaderParameter("edge_strength", 1.1f);

        // The faceted crystal substitutes an analytic environment for a real reflection, and it only
        // stops being noticeable when these sit near the scene's actual sky and ground. These are
        // MovementPlayground's own background and the course palette's ground.
        m.SetShaderParameter("env_sky", new Vector3(0.60f, 0.67f, 0.74f));
        m.SetShaderParameter("env_horizon", new Vector3(0.66f, 0.68f, 0.72f));
        m.SetShaderParameter("env_ground", new Vector3(0.38f, 0.40f, 0.42f));

        _crystalMat = m;
        return m;
    }

    private static ShaderMaterial BubbleMaterial()
    {
        if (_bubbleMat != null) return _bubbleMat;

        var m = new ShaderMaterial { Shader = GD.Load<Shader>(BubbleShader) };

        // RETUNED FOR ITS JOB, and the original tuning was not wrong. The shipped default is
        // alpha_center 0.06 — nearly invisible face-on, exactly right for a real soap film and
        // exactly useless for something a player is meant to spot from the ground and climb toward.
        // A close-up caught it: the first render photographed the blocks BEHIND the bubble.
        m.SetShaderParameter("alpha_center", 0.22f);
        m.SetShaderParameter("alpha_edge", 0.96f);
        m.SetShaderParameter("iridescence", 1.45f);
        m.SetShaderParameter("highlight", 0.80f);
        m.SetShaderParameter("film_nm", 340.0f);

        _bubbleMat = m;
        return m;
    }

    /// <summary>
    /// A soap bubble. <paramref name="radius"/> is world radius in metres, and it is a REAL dial:
    /// the shader reads the radius out of <c>MODEL_MATRIX</c> to decide how much the surface
    /// wobbles, so a scaled node is the path a game would take to grow one — and therefore the one
    /// worth testing. Anything past about 0.55 m ripples fully.
    /// </summary>
    public static MeshInstance3D Bubble(Node parent, string name, Vector3 at, float radius)
    {
        var mi = new MeshInstance3D
        {
            Name = name,
            Position = at,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = BubbleMaterial(),
            Scale = Vector3.One * (radius * 2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>
    /// A hexagonal point with a pyramidal cap — the shape the word "crystal" actually means: a prism
    /// body whose side faces meet at a hard crease, capped by facets at a different angle again, so
    /// one object shows two families of plane catching the light separately.
    ///
    /// <para><b>FLAT NORMALS ARE THE WHOLE POINT.</b> Every triangle gets its own vertices and one
    /// shared normal, so nothing is smoothed across a crease. A shared-vertex mesh averages the
    /// normals at every edge and rounds off precisely the creases this material exists to show — on
    /// a smooth sphere the facet term is still running and is very nearly invisible.</para>
    /// </summary>
    public static MeshInstance3D Crystal(Node parent, string name, Vector3 at, float size,
        RandomNumberGenerator rng)
    {
        var mi = new MeshInstance3D
        {
            Name = name,
            Position = at,
            Mesh = GemMesh(size * 0.62f, size * 1.5f, size * 0.8f, rng.RandfRange(0f, Mathf.Tau)),
            MaterialOverride = CrystalMaterial(),
            Rotation = new Vector3(0.05f, rng.RandfRange(0f, Mathf.Tau), -0.04f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }

    private static ArrayMesh GemMesh(float r, float body, float cap, float spin)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        var ring = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = spin + Mathf.Tau * i / 6f;
            ring[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        var apex = new Vector3(0f, body + cap, 0f);
        for (int i = 0; i < 6; i++)
        {
            Vector3 p0 = ring[i], p1 = ring[(i + 1) % 6];
            Vector3 t0 = p0 + new Vector3(0f, body, 0f);
            Vector3 t1 = p1 + new Vector3(0f, body, 0f);
            Tri(st, p0, p1, t1);              // side quad
            Tri(st, p0, t1, t0);
            Tri(st, t0, t1, apex);            // cap facet
            Tri(st, p1, p0, Vector3.Zero);    // base, wound the other way so it faces down
        }

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = (b - a).Cross(c - a).Normalized();
        foreach (Vector3 v in new[] { a, b, c })
        {
            st.SetNormal(n);
            st.SetUV(new Vector2(v.X, v.Z));
            st.AddVertex(v);
        }
    }

    /// <summary>
    /// Make a prop drift, so it reads as floating rather than as a decal pinned in the air.
    ///
    /// <para>Eight keys around the cycle rather than two: Godot lerps between value keys, so a bob
    /// keyed only at its extremes travels in straight lines and reads as a lift. The sideways drift
    /// runs at half the vertical rate so the path is a lopsided figure rather than a line.</para>
    /// </summary>
    public static void Float(Node parent, string targetName, Vector3 basePos)
    {
        var anim = new Animation { Length = 7.4f, LoopMode = Animation.LoopModeEnum.Linear };
        int track = anim.AddTrack(Animation.TrackType.Value);
        anim.TrackSetPath(track, $"{targetName}:position");

        const int keys = 8;
        for (int i = 0; i <= keys; i++)
        {
            float t = (float)i / keys;
            float ph = t * Mathf.Tau;
            anim.TrackInsertKey(track, t * anim.Length, basePos + new Vector3(
                Mathf.Sin(ph * 0.5f) * 0.55f,
                Mathf.Sin(ph) * 0.62f,
                Mathf.Cos(ph * 0.75f) * 0.45f));
        }
        anim.ValueTrackSetUpdateMode(track, Animation.UpdateMode.Continuous);

        var lib = new AnimationLibrary();
        lib.AddAnimation("float", anim);

        var player = new AnimationPlayer { Name = $"{targetName}Float" };
        parent.AddChild(player);
        player.AddAnimationLibrary("", lib);
        player.RootNode = new NodePath("..");   // relative to the player: the course node
        player.Autoplay = "float";
    }
}
