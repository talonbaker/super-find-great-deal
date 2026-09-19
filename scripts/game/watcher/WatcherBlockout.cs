using Godot;

namespace MpFoundation.Game.Watcher;

/// <summary>
/// The creature, built out of primitives in code. Long dark legs, a small pale hunched body, two
/// eye dots.
///
/// <para><b>The blockout IS the design claim, not a placeholder for it.</b> The reference is a
/// maned wolf: a canid body on deer legs. What is supposed to carry the horror is
/// <i>proportion</i> — the eye resolves "dog" and then the legs fail to agree — which is exactly
/// why a greybox is sufficient to test it and why a sculpted asset would prove nothing extra. If
/// proportion alone does not do it at 40 m in the dark, no amount of fur fixes that, and the
/// honest outcome is a negative result rather than a nicer mesh. <see cref="MpFoundation.Dev.WatcherLab"/>
/// measures it.</para>
///
/// <para><b>The second claim is that the legs disappear.</b> Near-black, unlit, matte: against
/// dark ground they are meant to fall below the eye's threshold while the pale body stays above
/// it, so the body appears to hover. That is a claim about relative luminance and it is
/// measurable — see the lab's leg/body contrast split.</para>
///
/// <para><b>Colour is constrained by direction, not by taste.</b> THRILL-BIBLE §12 (the watcher
/// entry) requires the body and the eyes to be <i>value, not hue</i> — achromatic, separated
/// from the dark by luminance alone. Five consecutive direction passes have kept the dusk
/// sweep's orange→plum→indigo grade as the game's only warning colour lane, and a pair of amber
/// or red eyes would put a second warning channel in it. So the eyes here are pale and neutral,
/// and they are a <i>presence/absence</i> readout (two dots = it is facing you) rather than a
/// colour signal.</para>
///
/// <para>Scale reference: campers stand 1.16–1.27 m (see <c>SandboxAvatar</c>'s own note). At
/// <see cref="TotalHeightM"/> its belly is above a child's head.</para>
/// </summary>
public partial class WatcherBlockout : Node3D
{
    // --- Proportion. These are the design, and they are the thing under test. ---------------
    public const float TotalHeightM = 2.05f;
    private const float LegLengthM = 1.32f;     // 64% of total height — the whole trick
    private const float LegRadiusM = 0.045f;
    private const float BodyLengthM = 0.86f;    // small: barely 2/3 of a leg
    private const float BodyHeightM = 0.34f;
    private const float BodyWidthM = 0.30f;
    private const float HeadLengthM = 0.26f;
    private const float HeadHeightM = 0.15f;
    private const float HeadWidthM = 0.13f;
    private const float EyeRadiusM = 0.025f;
    private const float EyeSeparationM = 0.085f;
    private const float EarHeightM = 0.19f;     // big canid ears: the "dog" half of the read

    /// <summary>Emission on the pale body. Not decoration: at the shipped night ambient a purely
    /// albedo-driven body is as dark as everything else, and the design needs it to sit above the
    /// background rather than in it. Kept low enough that it reads as a pale animal and not as a
    /// lamp — a self-lit creature would be a light source, and a light source in the treeline is
    /// #108's reserved flash-beacon device, not this one.</summary>
    private const float BodyEmission = 0.22f;

    /// <summary>The head, separately rotatable. The head turn is the creature's only output
    /// channel, so it gets its own pivot and it sits forward of the body mass — a head sunk into
    /// the body rotates without changing the silhouette, which at 40 m says nothing at all.</summary>
    public Node3D Head { get; private set; } = null!;

    private StandardMaterial3D _legMat = null!;
    private StandardMaterial3D _bodyMat = null!;
    private StandardMaterial3D _eyeMat = null!;

    public override void _Ready() => Build();

    /// <summary>Idempotent, so a lab may rebuild it after changing a constant.</summary>
    public void Build()
    {
        foreach (Node child in GetChildren())
            child.QueueFree();

        _legMat = new StandardMaterial3D
        {
            // Near-black and fully matte. This is the "legs vanish" claim expressed as a number.
            AlbedoColor = new Color(0.020f, 0.020f, 0.023f),
            Roughness = 1f,
            Metallic = 0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        _bodyMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.70f, 0.69f, 0.67f),   // pale, deliberately near-neutral
            Roughness = 1f,
            Metallic = 0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = new Color(0.62f, 0.61f, 0.59f),
            EmissionEnergyMultiplier = BodyEmission,
        };
        _eyeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.88f, 0.88f, 0.86f),
            EmissionEnabled = true,
            Emission = new Color(0.92f, 0.92f, 0.90f),      // achromatic: see the class remarks
            EmissionEnergyMultiplier = 1.6f,
        };

        BuildLegs();
        BuildBody();
        BuildHead();
    }

    private void BuildLegs()
    {
        // Sticks. The radius is the point: a leg this thin at 40 m is about two pixels wide, so
        // whether it survives at all is a real question rather than a rhetorical one.
        var leg = new CylinderMesh
        {
            TopRadius = LegRadiusM,
            BottomRadius = LegRadiusM * 0.75f,   // tapering to a point of contact
            Height = LegLengthM,
            RadialSegments = 6,
            Rings = 1,
        };
        (float x, float z)[] feet =
        {
            (-0.13f, -0.28f), (0.13f, -0.28f),   // fore
            (-0.14f, 0.30f), (0.14f, 0.30f),     // hind, very slightly wider
        };
        for (int i = 0; i < feet.Length; i++)
        {
            AddChild(new MeshInstance3D
            {
                Name = $"Leg{i}",
                Mesh = leg,
                MaterialOverride = _legMat,
                Position = new Vector3(feet[i].x, LegLengthM * 0.5f, feet[i].z),
            });
        }
    }

    private void BuildBody()
    {
        // A hunched back: the hump sits over the shoulders and the line falls away to the hips,
        // so the body reads as an arch rather than as a barrel.
        float bellyY = LegLengthM;
        float coreY = bellyY + BodyHeightM * 0.5f;

        AddChild(Ellipsoid("Body", _bodyMat, new Vector3(BodyWidthM, BodyHeightM, BodyLengthM),
                           new Vector3(0, coreY, 0.02f)));
        AddChild(Ellipsoid("Shoulders", _bodyMat,
                           new Vector3(BodyWidthM * 0.96f, BodyHeightM * 0.92f, BodyLengthM * 0.45f),
                           new Vector3(0, coreY + 0.075f, -0.20f)));
        AddChild(Ellipsoid("Haunch", _bodyMat,
                           new Vector3(BodyWidthM * 0.88f, BodyHeightM * 0.80f, BodyLengthM * 0.40f),
                           new Vector3(0, coreY - 0.015f, 0.30f)));
    }

    private void BuildHead()
    {
        float bellyY = LegLengthM;
        float coreY = bellyY + BodyHeightM * 0.5f;

        // The neck drops FORWARD and DOWN off the shoulder hump. The head hanging below the
        // shoulder line is what makes the posture read as hunched rather than as alert, and it
        // buys the silhouette its most important property: the head is clear of the body mass,
        // so turning it changes the outline.
        Head = new Node3D { Name = "Head", Position = new Vector3(0, coreY + 0.04f, -0.34f) };
        AddChild(Head);

        var neck = new CylinderMesh
        {
            TopRadius = 0.055f, BottomRadius = 0.075f, Height = 0.30f, RadialSegments = 6, Rings = 1,
        };
        Head.AddChild(new MeshInstance3D
        {
            Name = "Neck",
            Mesh = neck,
            MaterialOverride = _bodyMat,
            Position = new Vector3(0, -0.02f, 0.09f),
            RotationDegrees = new Vector3(58f, 0, 0),     // leaning down and forward
        });

        var skull = Ellipsoid("Skull", _bodyMat, new Vector3(HeadWidthM, HeadHeightM, HeadLengthM),
                              new Vector3(0, -0.15f, -0.14f));
        Head.AddChild(skull);

        // Ears. Two thin wedges standing off the skull: at the range this thing is meant to be
        // seen at they are most of what says "canid", and they break the head's outline so its
        // rotation is legible as a change of shape rather than only of shading.
        for (int s = -1; s <= 1; s += 2)
        {
            Head.AddChild(new MeshInstance3D
            {
                Name = s < 0 ? "EarL" : "EarR",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.004f, BottomRadius = 0.052f, Height = EarHeightM,
                    RadialSegments = 5, Rings = 1,
                },
                MaterialOverride = _bodyMat,
                Position = new Vector3(s * 0.055f, -0.06f, -0.09f),
                RotationDegrees = new Vector3(-12f, 0, s * 14f),
            });
        }

        // Two dots, forward-facing, on the front of the skull. Their whole job is binary: visible
        // means it is looking at you.
        for (int s = -1; s <= 1; s += 2)
        {
            Head.AddChild(new MeshInstance3D
            {
                Name = s < 0 ? "EyeL" : "EyeR",
                Mesh = new SphereMesh { Radius = EyeRadiusM, Height = EyeRadiusM * 2f, RadialSegments = 8, Rings = 5 },
                MaterialOverride = _eyeMat,
                Position = new Vector3(s * EyeSeparationM * 0.5f, -0.135f, -0.14f - HeadLengthM * 0.42f),
            });
        }
    }

    /// <summary>A unit sphere scaled to the given full diameters. Built through the basis rather
    /// than through Scale so the transform is written once and cannot be transposed — this repo
    /// has shipped transposed Transform3D bases from hand-authored scenes before.</summary>
    private static MeshInstance3D Ellipsoid(string name, Material mat, Vector3 diameters, Vector3 at)
        => new()
        {
            Name = name,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 14, Rings = 8 },
            MaterialOverride = mat,
            Transform = new Transform3D(Basis.Identity.Scaled(diameters), at),
        };

    /// <summary>Lets a lab dial the body's self-illumination while measuring what it costs.</summary>
    public void SetBodyEmission(float energy)
    {
        if (_bodyMat != null)
            _bodyMat.EmissionEnergyMultiplier = energy;
    }
}
