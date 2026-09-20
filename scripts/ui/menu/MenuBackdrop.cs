using Godot;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// <b>The title screen's picture: a night world, one hero bubble, and exactly one sharp thing in
/// the backdrop.</b> Everything behind the type — sky, ground, the field of colour blocks and their
/// rims, the off-frame campfire, the stars, the bubbles, the scrim and the grain — is built and
/// composited here. The menu's own controls are a separate layer over the top and are never touched
/// by any of it.
///
/// <para><b>MENU-2, 2026-08-29: this went from a light world to a night one, and it was a re-skin.
/// Not one line of the layer stack changed shape.</b> What changed is the palette in
/// <see cref="MenuLook"/> and three additions that only a dark frame needs: the block rims, the
/// night layer (an off-frame campfire and a star field), and the bubble's emissive terms. That the
/// direction could reverse without the structure moving is the property MENU-1 was built for.</para>
///
/// <para><b>Three layers, blurred independently. That is the design, not an implementation
/// detail.</b> Talon, 2026-08-28: "blur the bubble and then HEAVILY BLUR the color blocks ... It's
/// overall too colourful." World heavy (18 px at night, down from the light frame's 30), bubbles
/// light (9 px), <b>UI never</b>. One camera has one depth of field, so this cannot be a DOF
/// setting; each layer renders into its own SubViewport and is blurred on the way out. The UI
/// staying completely unblurred and untinted is what makes the type sit IN FRONT OF the picture
/// rather than ON TOP OF it.</para>
///
/// <para><b>The star field is the one thing that is composited AFTER a blur rather than before
/// it,</b> and that is not a shortcut — painted into the world layer an 18 px gaussian erases it
/// completely. Stars are the only sharp element in the backdrop, and the sharpness is what makes
/// them read as distance instead of as dust on the lens.</para>
///
/// <para><b>The blue depth effect is kept.</b> Talon asked for it by name, and it is the one
/// mechanism carried across from the campfire backdrop this screen replaces: distance reads as
/// blue. It has been through a night key, a light one and back into a night one; the mechanism
/// itself has never changed, only the three colours it interpolates toward.</para>
///
/// <para><b>Everything that moves moves off one number.</b> <see cref="SetPhase"/> drives the
/// bubbles' film, the camera's drift, and — through <see cref="WordmarkRule"/> — the wordmark
/// underline's travelling colour. Talon asked for the rule to "move along with the bubble"; sharing
/// the driver is the only way that is literally true. It also means reduced motion is expressed by
/// simply not advancing the number, so the screen freezes as one picture rather than in pieces.</para>
/// </summary>
public partial class MenuBackdrop : Node
{
    private const string WorldShaderPath = "res://resources/shaders/ui_menu_world.gdshader";
    private const string SkyShaderPath = "res://resources/shaders/ui_menu_sky.gdshader";
    private const string ShadowShaderPath = "res://resources/shaders/ui_menu_shadow.gdshader";
    private const string RimShaderPath = "res://resources/shaders/ui_menu_rim.gdshader";
    private const string BubbleShaderPath = "res://resources/shaders/ui_menu_bubbles.gdshader";
    private const string BlurShaderPath = "res://resources/shaders/ui_menu_blur.gdshader";
    private const string NightShaderPath = "res://resources/shaders/ui_menu_night.gdshader";
    private const string ScrimShaderPath = "res://resources/shaders/ui_menu_scrim.gdshader";
    private const string GrainShaderPath = "res://resources/shaders/FilmGrain.gdshader";

    /// <summary>The blur shader's compile-time tap ceiling. The proxy scale is reduced rather than
    /// the blur being clipped when a very wide display would push the kernel past it — clipping a
    /// gaussian leaves a visible step at the kernel edge, which is exactly the kind of artefact a
    /// heavy blur exists to remove.</summary>
    private const float MaxSigma = 8f;

    /// <summary>One block of the approved frame's field: footprint, height and which of the six
    /// palette entries it takes. The scatter is <b>baked, not re-rolled</b> — it is part of the
    /// composition Talon approved, and a title screen that reshuffles its own background every
    /// launch cannot be compared against a capture. Generated from the previz's own seeded scatter
    /// (52 scattered plus 4 placed), sorted back to front.</summary>
    private readonly record struct Block(float X, float Z, float W, float H, float D, int Colour);

    private static readonly Block[] Blocks =
    {
        new(-9.4740f, -60.2652f, 1.3771f, 1.0884f, 0.7761f, 1),
        new(8.8972f, -59.9960f, 1.4280f, 2.0188f, 2.0092f, 2),
        new(4.0642f, -58.8356f, 1.2781f, 1.2874f, 2.1281f, 2),
        new(-23.3421f, -57.2883f, 1.4310f, 2.0066f, 2.1264f, 5),
        new(16.2013f, -56.7146f, 2.1921f, 2.0176f, 2.0735f, 0),
        new(-15.7611f, -54.8399f, 1.6752f, 2.3524f, 1.6140f, 2),
        new(-25.5287f, -54.2868f, 2.0961f, 2.8115f, 1.4019f, 1),
        new(22.3104f, -52.5495f, 2.2367f, 2.1522f, 0.9182f, 3),
        new(7.6525f, -50.5068f, 1.7383f, 1.0546f, 1.5616f, 4),
        new(18.8432f, -47.6801f, 0.9568f, 3.2069f, 0.7078f, 4),
        new(14.1049f, -46.9316f, 0.9325f, 1.7148f, 1.9229f, 5),
        new(-21.5266f, -46.8308f, 1.3327f, 0.5880f, 0.8843f, 1),
        new(3.6213f, -46.3088f, 1.4253f, 3.0467f, 1.3175f, 4),
        new(-18.3207f, -46.2480f, 1.6199f, 1.9725f, 2.0070f, 3),
        new(-19.9109f, -42.3503f, 1.2280f, 3.2212f, 1.5076f, 3),
        new(-8.2349f, -41.9777f, 2.1859f, 2.4204f, 0.8974f, 2),
        new(2.7678f, -40.1359f, 1.3397f, 1.6918f, 1.0592f, 5),
        new(15.4397f, -39.1308f, 2.0187f, 1.1531f, 1.1502f, 0),
        new(5.2222f, -37.9084f, 1.8500f, 0.9373f, 1.3605f, 0),
        new(17.4422f, -34.7633f, 2.0475f, 2.3043f, 2.1834f, 3),
        new(-1.1227f, -32.4082f, 2.2592f, 2.3248f, 1.4712f, 5),
        new(-8.1552f, -31.3093f, 0.7200f, 1.0580f, 1.7380f, 0),
        new(2.9725f, -30.2310f, 1.8208f, 1.3893f, 2.1420f, 3),
        new(3.9339f, -29.4162f, 1.7799f, 1.0333f, 0.7928f, 4),
        new(-12.6706f, -28.9030f, 1.9796f, 2.1793f, 1.1495f, 3),
        new(14.2269f, -28.1132f, 1.5764f, 2.0114f, 2.0448f, 3),
        new(7.3733f, -26.1025f, 2.0859f, 2.6170f, 0.8698f, 5),
        new(-9.6482f, -24.2157f, 1.8262f, 2.9086f, 1.2653f, 1),
        new(-6.4962f, -24.1385f, 1.4761f, 2.4060f, 0.8516f, 0),
        new(1.0231f, -23.6752f, 1.0656f, 1.2175f, 1.1948f, 2),
        new(2.4891f, -23.0371f, 0.8007f, 1.6241f, 1.1846f, 4),
        new(2.2101f, -21.9969f, 1.8623f, 1.5307f, 1.4786f, 3),
        new(-4.4030f, -21.6583f, 0.8916f, 2.3172f, 1.8962f, 0),
        new(5.2412f, -20.9573f, 2.1744f, 3.0895f, 0.9423f, 4),
        new(8.3138f, -20.1370f, 2.0551f, 0.8745f, 1.8503f, 1),
        new(-4.9560f, -19.4850f, 1.1333f, 1.7907f, 1.4568f, 2),
        new(7.8165f, -17.3236f, 1.5666f, 2.9567f, 1.6596f, 2),
        new(6.0293f, -16.3110f, 1.8500f, 2.5795f, 1.6444f, 0),
        new(-1.8638f, -15.6134f, 0.8644f, 3.3067f, 1.0225f, 1),
        new(-5.5599f, -14.8118f, 1.3173f, 1.0203f, 1.2191f, 1),
        new(-6.0728f, -14.1338f, 1.7413f, 0.6274f, 0.7535f, 4),
        new(7.3743f, -13.9874f, 2.1283f, 0.8260f, 1.6057f, 2),
        new(-2.2334f, -13.2370f, 0.7063f, 2.9071f, 0.9317f, 1),
        new(7.1468f, -12.7403f, 1.6390f, 1.0236f, 2.0261f, 4),
        new(1.1029f, -12.4377f, 0.7663f, 2.2121f, 0.9490f, 2),
        new(4.5984f, -10.1622f, 1.3451f, 3.3384f, 1.5850f, 5),
        new(1.8975f, -6.8710f, 1.7743f, 2.8893f, 1.9053f, 3),
        new(3.4000f, -4.1000f, 1.2000f, 0.5000f, 1.2000f, 5),
        new(-4.3357f, -3.8458f, 0.7344f, 1.2330f, 1.0729f, 1),
        new(2.6768f, -3.4209f, 2.3321f, 2.9766f, 0.7761f, 5),
        new(-3.1000f, -3.4000f, 1.5000f, 0.6000f, 1.5000f, 4),
        new(3.3070f, -3.3215f, 1.4951f, 2.0881f, 1.1832f, 0),
        new(3.8379f, -2.6288f, 1.0649f, 2.1348f, 2.1172f, 5),
        new(7.4000f, -2.2000f, 3.6000f, 1.5000f, 3.0000f, 2),
        new(3.5726f, -1.8705f, 2.0481f, 1.9877f, 1.7888f, 0),
        new(-6.6000f, -1.4000f, 3.2000f, 2.2000f, 2.6000f, 1),
    };

    private SubViewport _world3D = null!;
    private SubViewport _worldBlurH = null!;
    private SubViewport _worldBlurV = null!;
    private SubViewport _bubbles = null!;
    private SubViewport _bubbleBlurH = null!;
    private SubViewport _bubbleBlurV = null!;

    private Camera3D _camera = null!;
    private ShaderMaterial? _bubbleMaterial;
    private ShaderMaterial? _nightMaterial;
    private ShaderMaterial? _grainMaterial;

    private Vector2I _builtFor = Vector2I.Zero;
    private float _phase = MenuLook.RestPhase;

    /// <summary>Whether the screen is allowed to move at all. Set before the first frame; the
    /// backdrop reads it only to decide whether the grain animates, because everything else it does
    /// is driven by whether the caller advances <see cref="SetPhase"/>.</summary>
    public bool ReducedMotion { get; set; }

    public override void _Ready()
    {
        Build();
        GetViewport().SizeChanged += OnViewportResized;
    }

    public override void _ExitTree()
    {
        if (IsInsideTree())
            GetViewport().SizeChanged -= OnViewportResized;
    }

    private void OnViewportResized()
    {
        var size = (Vector2I)GetViewport().GetVisibleRect().Size;
        if (size == _builtFor || size.X < 16 || size.Y < 16)
            return;
        foreach (Node child in GetChildren())
            child.QueueFree();
        // Deferred: the old chain's viewports must actually be gone before the new ones claim their
        // render targets, and QueueFree lands at the end of the frame.
        CallDeferred(MethodName.Build);
    }

    // ==============================================================================================
    // Construction
    // ==============================================================================================

    private void Build()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        _builtFor = (Vector2I)view;
        float aspect = view.Y / Mathf.Max(view.X, 1f);

        // Each layer's proxy scale is reduced if the blur it has to carry would overrun the
        // shader's tap ceiling, so the blur is always the agreed radius and never a clipped one.
        float worldSigmaFull = view.X * MenuLook.WorldBlurFraction;
        float worldScale = Mathf.Min(MenuLook.WorldProxyScale, MaxSigma / Mathf.Max(worldSigmaFull, 0.001f));
        Vector2I worldSize = ProxySize(view, worldScale);
        float worldSigma = worldSigmaFull * worldScale;

        float bubbleSigmaFull = view.X * MenuLook.BubbleBlurFraction;
        float bubbleScale = Mathf.Min(MenuLook.BubbleProxyScale, MaxSigma / Mathf.Max(bubbleSigmaFull, 0.001f));
        Vector2I bubbleSize = ProxySize(view, bubbleScale);
        float bubbleSigma = bubbleSigmaFull * bubbleScale;

        var chains = new Node { Name = "Chains" };
        AddChild(chains);

        _world3D = MakeViewport("World3D", worldSize, transparent: false, is3D: true);
        chains.AddChild(_world3D);
        BuildWorld(_world3D);

        _worldBlurH = MakeBlurStage(chains, "WorldBlurH", worldSize, _world3D.GetTexture(),
            new Vector2(1f, 0f), worldSigma, transparent: false);
        _worldBlurV = MakeBlurStage(chains, "WorldBlurV", worldSize, _worldBlurH.GetTexture(),
            new Vector2(0f, 1f), worldSigma, transparent: false);

        _bubbles = MakeViewport("Bubbles", bubbleSize, transparent: true, is3D: false);
        chains.AddChild(_bubbles);
        _bubbleMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(BubbleShaderPath) };
        _bubbleMaterial.SetShaderParameter("aspect", aspect);
        _bubbleMaterial.SetShaderParameter("phase", _phase);
        // The emissive terms. Against a dark ground the film is the brightest thing on screen; these
        // six are what turn a lit bubble into one that gives off light. See `MenuLook`.
        _bubbleMaterial.SetShaderParameter("film_gain", MenuLook.BubbleFilmGain);
        _bubbleMaterial.SetShaderParameter("base_lift", MenuLook.BubbleBaseLift);
        _bubbleMaterial.SetShaderParameter("spec_gain", MenuLook.BubbleSpecGain);
        _bubbleMaterial.SetShaderParameter("alpha_gain", MenuLook.BubbleAlphaGain);
        _bubbleMaterial.SetShaderParameter("spec_alpha", MenuLook.BubbleSpecAlpha);
        _bubbleMaterial.SetShaderParameter("halo_gain", MenuLook.BubbleHaloGain);
        _bubbles.AddChild(FullRect("Field", bubbleSize, _bubbleMaterial));

        _bubbleBlurH = MakeBlurStage(chains, "BubbleBlurH", bubbleSize, _bubbles.GetTexture(),
            new Vector2(1f, 0f), bubbleSigma, transparent: true);
        _bubbleBlurV = MakeBlurStage(chains, "BubbleBlurV", bubbleSize, _bubbleBlurH.GetTexture(),
            new Vector2(0f, 1f), bubbleSigma, transparent: true);

        BuildComposite();
    }

    private static Vector2I ProxySize(Vector2 view, float scale) => new(
        Mathf.Max(64, Mathf.RoundToInt(view.X * scale)),
        Mathf.Max(36, Mathf.RoundToInt(view.Y * scale)));

    private static SubViewport MakeViewport(string name, Vector2I size, bool transparent, bool is3D)
    {
        var vp = new SubViewport
        {
            Name = name,
            Size = size,
            TransparentBg = transparent,
            Disable3D = !is3D,
            // A SubViewport with no SubViewportContainer is never "visible" in the engine's sense,
            // so the default WhenVisible would leave it black forever.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            GuiDisableInput = true,
            HandleInputLocally = false,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
        };
        if (is3D)
        {
            // Its own World3D: the menu's root sits in the main scene's 3D world and this picture
            // has no business being in it, or it in this.
            vp.OwnWorld3D = true;
            // Cheap at a quarter resolution, and the blocks' edges are what would otherwise crawl
            // while the camera drifts.
            vp.Msaa3D = Viewport.Msaa.Msaa4X;
        }
        return vp;
    }

    private static SubViewport MakeBlurStage(
        Node parent, string name, Vector2I size, Texture2D source, Vector2 direction, float sigma, bool transparent)
    {
        SubViewport vp = MakeViewport(name, size, transparent, is3D: false);
        parent.AddChild(vp);

        var material = new ShaderMaterial { Shader = GD.Load<Shader>(BlurShaderPath) };
        material.SetShaderParameter("direction", direction);
        material.SetShaderParameter("sigma_px", sigma);
        material.SetShaderParameter("source_size", (Vector2)size);

        var rect = new TextureRect
        {
            Name = "Pass",
            Texture = source,
            Material = material,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Size = size,
        };
        vp.AddChild(rect);
        return vp;
    }

    private static ColorRect FullRect(string name, Vector2I size, ShaderMaterial material) => new()
    {
        Name = name,
        Material = material,
        Color = Colors.White,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Size = size,
    };

    // ==============================================================================================
    // The 3D world
    // ==============================================================================================

    private void BuildWorld(SubViewport into)
    {
        var sky = new ShaderMaterial { Shader = GD.Load<Shader>(SkyShaderPath) };
        sky.SetShaderParameter("top_srgb", ToVec3(MenuLook.SkyTop));
        sky.SetShaderParameter("horizon_srgb", ToVec3(MenuLook.SkyHorizon));

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = sky,
                ProcessMode = Sky.ProcessModeEnum.Realtime,
                RadianceSize = Sky.RadianceSizeEnum.Size32,
            },

            // Nothing here is lit. Every material on this screen computes its own value, so an
            // ambient or a reflection source would be a second, disagreeing opinion about colour.
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,

            // Linear at exposure 1, so what the world shader writes is what lands in the viewport
            // texture. A filmic curve here would quietly desaturate the one frame Talon approved.
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1f,

            // The haze is the world shader's, computed in sRGB alongside everything else it does.
            // The engine's own depth fog is a linear-space operation and would disagree with it.
            FogEnabled = false,
            GlowEnabled = false,
            SsaoEnabled = false,
            SsilEnabled = false,
            SdfgiEnabled = false,
            AdjustmentEnabled = false,
        };
        into.AddChild(new WorldEnvironment { Name = "Environment", Environment = env });

        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Fov = MenuLook.CameraFovDegrees,
            Near = 0.05f,
            Far = 3000f,
            Position = MenuLook.CameraPosition,
        };
        into.AddChild(_camera);
        ApplyCameraDrift();

        ShaderMaterial ground = WorldMaterial();
        ground.SetShaderParameter("far_srgb", ToVec3(MenuLook.GroundFar));
        ground.SetShaderParameter("haze_extra", MenuLook.GroundHazeExtra);
        ground.SetShaderParameter("key", 1f);
        // The ground has one face and it is the top one; flattening the other two stops a grazing
        // normal at the horizon picking up a side value.
        ground.SetShaderParameter("shade_front", MenuLook.ShadeTop);
        ground.SetShaderParameter("shade_side", MenuLook.ShadeTop);

        // Big enough that its far edge sits a fraction of a pixel below the skyline and fully
        // hazed, so there is no visible end to the ground. A plane that stops short leaves a band
        // of sky beneath the horizon, and that is exactly the kind of thing only a render shows.
        var groundMesh = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(4000f, 4000f) },
            MaterialOverride = ground,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, 0f, MenuLook.CameraPosition.Z - 1990f),
        };
        into.AddChild(groundMesh);
        groundMesh.SetInstanceShaderParameter("base_srgb", ToVec4(MenuLook.GroundNear));

        BuildBlocks(into);
    }

    private static ShaderMaterial WorldMaterial()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(WorldShaderPath) };
        material.SetShaderParameter("haze_srgb", ToVec3(MenuLook.Haze));
        material.SetShaderParameter("far_srgb", ToVec3(MenuLook.Haze));
        material.SetShaderParameter("haze_extra", 0f);
        material.SetShaderParameter("density", MenuLook.HazeDensity);
        material.SetShaderParameter("haze_curve", MenuLook.HazeCurve);
        material.SetShaderParameter("key", MenuLook.Key);
        material.SetShaderParameter("shade_top", MenuLook.ShadeTop);
        material.SetShaderParameter("shade_front", MenuLook.ShadeFront);
        material.SetShaderParameter("shade_side", MenuLook.ShadeSide);
        return material;
    }

    private static void BuildBlocks(SubViewport into)
    {
        // ONE material and ONE mesh for all 56 blocks; the colour rides on an instance uniform. A
        // MultiMesh would have been the reflex here and it is the wrong tool: its per-instance
        // colour goes through the engine's own colour handling, and this shader deliberately works
        // in sRGB right up until its single conversion. 56 draws of a cube is nothing on a menu.
        ShaderMaterial blockMaterial = WorldMaterial();
        var cube = new BoxMesh { Size = Vector3.One };

        var shadowMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(ShadowShaderPath) };
        shadowMaterial.SetShaderParameter("smudge_srgb", ToVec3(MenuLook.SmudgeColour));
        var quad = new QuadMesh { Size = Vector2.One };

        // The rims share the cube and take their own material — see `ui_menu_rim.gdshader` for why
        // a rim is geometry here and not a term on the block's own shader.
        var rimMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(RimShaderPath) };

        var field = new Node3D { Name = "Blocks" };
        into.AddChild(field);

        for (int i = 0; i < Blocks.Length; i++)
        {
            Block b = Blocks[i];
            var mesh = new MeshInstance3D
            {
                Name = "Block" + i,
                Mesh = cube,
                MaterialOverride = blockMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(b.X, b.H * 0.5f, b.Z),
                Scale = new Vector3(b.W, b.H, b.D),
            };
            field.AddChild(mesh);
            mesh.SetInstanceShaderParameter("base_srgb", ToVec4(MenuLook.BlockColours[b.Colour]));

            float distance = Mathf.Abs(b.Z - MenuLook.CameraPosition.Z);
            var smudge = new MeshInstance3D
            {
                Name = "Smudge" + i,
                Mesh = quad,
                MaterialOverride = shadowMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(b.X, 0.012f, b.Z),
                RotationDegrees = new Vector3(-90f, 0f, 0f),
                Scale = new Vector3(b.W * 1.35f, b.D * 1.35f, 1f),
            };
            field.AddChild(smudge);
            // The approved frame fades the contact smudges with distance: 86/255 at the camera on
            // the night frame, decaying at exp(-0.024 d).
            smudge.SetInstanceShaderParameter(
                "peak_alpha",
                MenuLook.SmudgePeakAlpha * Mathf.Exp(-MenuLook.SmudgeDistanceFalloff * distance));

            BuildRims(field, cube, rimMaterial, b, i, distance);
        }
    }

    /// <summary>The two rim slivers on a block's visible top edges.
    ///
    /// <para><b>Thickness is per block, and that is the reason these are slivers rather than a
    /// shader term.</b> The rim owes a constant number of SCREEN pixels at every distance — a fixed
    /// world thickness would make the near blocks wear a stripe and the far ones nothing — so each
    /// sliver is sized from its own distance through the camera's own frustum. A rim that thins
    /// with distance on top of an alpha that already fades with distance would take the far field
    /// away twice.</para>
    ///
    /// <para><b>Which flank, and why this is not what the previz drew.</b> The previz always put its
    /// second rim on the LEFT top edge, while it draws the left flank only for blocks right of
    /// centre — so for everything on the left half its second rim is on an edge the camera cannot
    /// see. Under an 18 px blur the difference is invisible, which is exactly why it survived; it is
    /// corrected here because "draw the edge that is on the silhouette" is not a look decision and
    /// carrying the mistake forward would make it one.</para></summary>
    private static void BuildRims(
        Node3D field, Mesh cube, ShaderMaterial material, Block b, int index, float distance)
    {
        // World units per design-frame pixel at this distance: the frustum's height there, divided
        // by the design frame's height in pixels.
        float frustumHeight = 2f * distance * Mathf.Tan(Mathf.DegToRad(MenuLook.CameraFovDegrees * 0.5f));
        float t = MenuLook.RimWidthPx * frustumHeight / MenuLook.DesignHeight;

        Color raw = MenuLook.BlockColours[b.Colour];
        var rim = new Color(
            Mathf.Min(raw.R * MenuLook.RimGain, 1f),
            Mathf.Min(raw.G * MenuLook.RimGain, 1f),
            Mathf.Min(raw.B * MenuLook.RimGain, 1f),
            MenuLook.RimAlpha * Mathf.Exp(-MenuLook.RimDistanceFalloff * distance));

        // The camera sees the LEFT flank of a block right of centre and the right flank of one left
        // of it, so the visible top-flank edge changes sides with the block.
        float flankX = b.X > 0f ? b.X - b.W * 0.5f : b.X + b.W * 0.5f;

        (string Name, Vector3 Position, Vector3 Size)[] edges =
        {
            ("RimFront" + index, new Vector3(b.X, b.H, b.Z + b.D * 0.5f), new Vector3(b.W + t, t, t)),
            ("RimFlank" + index, new Vector3(flankX, b.H, b.Z), new Vector3(t, t, b.D + t)),
        };

        foreach ((string name, Vector3 position, Vector3 size) in edges)
        {
            var sliver = new MeshInstance3D
            {
                Name = name,
                Mesh = cube,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = position,
                Scale = size,
            };
            field.AddChild(sliver);
            sliver.SetInstanceShaderParameter("rim_srgb", ToVec4(rim));
        }
    }

    // ==============================================================================================
    // Compositing
    // ==============================================================================================

    private void BuildComposite()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;

        AddChild(CompositeLayer("WorldLayer", -4, new TextureRect
        {
            Name = "World",
            Texture = _worldBlurV.GetTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        }));

        // THE NIGHT LAYER, AND ITS POSITION IN THE STACK IS THE POINT OF IT. Additive, over the
        // world's blurred texture and under the bubbles. The stars have to land AFTER the blur —
        // painted into the world they would be erased by an 18 px gaussian, which cost a render to
        // find — and the campfire glow rides along because it is the same additive operation on the
        // same layer in the same frame.
        _nightMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(NightShaderPath) };
        _nightMaterial.SetShaderParameter("glow_centre", MenuLook.GlowCentre);
        _nightMaterial.SetShaderParameter("glow_reach", MenuLook.GlowReach);
        _nightMaterial.SetShaderParameter("glow_srgb", ToVec3(MenuLook.GlowColour));
        _nightMaterial.SetShaderParameter("frame_size", view);
        // Cell edge and star radius both scale off the frame width, so the sky is the same PICTURE
        // at any resolution rather than the same star count spread thinner or packed tighter.
        _nightMaterial.SetShaderParameter("star_cell_px", view.X * MenuLook.StarCellFraction);
        _nightMaterial.SetShaderParameter("star_field_height", MenuLook.StarFieldHeight);
        _nightMaterial.SetShaderParameter("star_radius_min_px", MenuLook.StarRadiusMinPx * view.X / MenuLook.DesignWidth);
        _nightMaterial.SetShaderParameter("star_radius_max_px", MenuLook.StarRadiusMaxPx * view.X / MenuLook.DesignWidth);
        _nightMaterial.SetShaderParameter("star_brightness_min", MenuLook.StarBrightnessMin);
        _nightMaterial.SetShaderParameter("star_brightness_max", MenuLook.StarBrightnessMax);
        _nightMaterial.SetShaderParameter("star_amplitude", MenuLook.StarAmplitude);
        _nightMaterial.SetShaderParameter("star_tint", ToVec3(MenuLook.StarTint));
        _nightMaterial.SetShaderParameter("star_seed", MenuLook.StarSeed);
        AddChild(CompositeLayer("NightLayer", -3, new ColorRect
        {
            Name = "Night",
            Material = _nightMaterial,
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        }));

        // PREMULTIPLIED. A transparent SubViewport's contents are already premultiplied — Godot
        // mix-blends onto a cleared (0,0,0,0) buffer, which leaves rgb*a in the colour channels —
        // so compositing it with ordinary alpha blending multiplies by alpha a second time and
        // rings every bubble rim dark.
        AddChild(CompositeLayer("BubbleLayer", -2, new TextureRect
        {
            Name = "Bubbles",
            Texture = _bubbleBlurV.GetTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha },
        }));

        var scrim = new ShaderMaterial { Shader = GD.Load<Shader>(ScrimShaderPath) };
        // The tint is what makes this the SAME scrim on a night frame as on a light one: the light
        // build washed the column toward white to lift the field its dark ink sat on, and this one
        // pushes it toward the sky's own near-black to drop the field its bone ink sits on. One
        // uniform, same curve, opposite direction — which is the whole argument for the tint having
        // been a uniform rather than a literal.
        scrim.SetShaderParameter("tint", ToVec3(MenuLook.ScrimTint));
        scrim.SetShaderParameter("peak", MenuLook.ScrimPeak);
        scrim.SetShaderParameter("x_end", MenuLook.ScrimXEnd);
        scrim.SetShaderParameter("y_centre", MenuLook.ScrimYCentre);
        scrim.SetShaderParameter("y_reach", MenuLook.ScrimYReach);
        scrim.SetShaderParameter("y_toe", MenuLook.ScrimYToe);
        AddChild(CompositeLayer("ScrimLayer", -1, new ColorRect
        {
            Name = "Scrim",
            Material = scrim,
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        }));

        if (!ResourceLoader.Exists(GrainShaderPath))
            return;

        _grainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(GrainShaderPath) };
        // Kept only as a dither: the frame is one enormous smooth gradient and an 8-bit sky bands
        // visibly without it — a NIGHT sky worse, because the whole ramp now lives in the bottom
        // two dozen code values where the quantisation steps are widest relative to the range.
        _grainMaterial.SetShaderParameter("grain_strength", MenuLook.GrainStrength);
        // OFF. The shader also offers a vignette, and this frame is already dark at its edges: a
        // second darkening would only crush what is left of the corners. It was off on the light
        // frame for the opposite reason and stays off for this one.
        _grainMaterial.SetShaderParameter("vignette_strength", 0f);
        _grainMaterial.SetShaderParameter("grain_speed", ReducedMotion ? 0f : MenuLook.GrainSpeed);
        AddChild(CompositeLayer("GrainLayer", 0, new ColorRect
        {
            Name = "Grain",
            Material = _grainMaterial,
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        }));
    }

    private static CanvasLayer CompositeLayer(string name, int layer, Control content)
    {
        var canvas = new CanvasLayer { Name = name, Layer = layer };
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(content);
        return canvas;
    }

    // ==============================================================================================
    // The one moving number
    // ==============================================================================================

    /// <summary>Advances (or holds) the single phase every moving part of the picture runs on.</summary>
    public void SetPhase(float phase)
    {
        _phase = phase;
        _bubbleMaterial?.SetShaderParameter("phase", phase);
        ApplyCameraDrift();
    }

    private void ApplyCameraDrift()
    {
        if (!IsInstanceValid(_camera))
            return;
        // Two incommensurate rates, so the sway never repeats a path and never reads as a loop.
        _camera.Position = MenuLook.CameraPosition + new Vector3(
            MenuLook.CameraDriftX * Mathf.Sin(_phase * 0.61f),
            MenuLook.CameraDriftY * Mathf.Sin(_phase * 0.43f + 1.1f),
            0f);
    }

    private static Vector3 ToVec3(Color c) => new(c.R, c.G, c.B);

    private static Vector4 ToVec4(Color c) => new(c.R, c.G, c.B, c.A);
}
