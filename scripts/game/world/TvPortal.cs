using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <remarks>PORTED VERBATIM from the sibling repo <c>mp-foundation</c> (same path, fetched
/// here as <c>mpf/master</c>) by packet BT-10, 2026-08-28. No shared git history, so this is a
/// file port, not a cherry-pick, and the entire diff against the source is this remark plus one
/// call: mp-foundation has <c>SfxLab.Play3D(node, pos, Sfx, …)</c>, which Sail does not, so the
/// crackle goes through <c>PlayStream3D</c> + <c>SfxLab.Get</c>. Namespaces already matched.
/// Do not "improve" this file in place — drift makes the next re-port unreadable. The one
/// structural addition is the ADOPT-IF-AUTHORED path in <c>_Ready</c>: Sail's Bubble Test
/// forbids runtime-built geometry in a section file and MEASURES it
/// (<c>BubbleTestSelfTest.CheckBake</c> counts packed vs live), so the den's TV authors its
/// Screen / ScreenGlowPool / ScreenTrigger in the <c>.tscn</c> and this script adopts them.
/// mp-foundation authors none of the three, so every lookup there returns null and the build
/// path below runs exactly as it always did — the port stays re-syncable in both directions.
/// </remarks>
/// <summary>
/// The flagship portal moment: an old rabbit-ears television whose static-filled screen
/// is a doorway. Walk into the screen and you're somewhere else — a small vignette room
/// with its own TV that leads back. The transition is a cheap white flash + static
/// crackle (a CanvasLayer fade the host owns — no post-processing, Bible-compliant),
/// never a scene swap.
///
/// The node itself is dumb on purpose: it detects a body at the screen and raises
/// <see cref="BodyAtScreen"/>. WHO moves the player differs by mode — the offline host
/// teleports directly; the networked server teleports authoritatively (epoch bump) so
/// prediction snaps cleanly. Clients keep the trigger unsubscribed. Re-enterable from
/// both sides, always.
///
/// The screen static is one small NoiseTexture2D emissive with a flickering energy —
/// one material, no shader, no per-frame texture work.
/// </summary>
public partial class TvPortal : Node3D
{
    /// <summary>Where a body entering this TV should come out (world space).</summary>
    public Vector3 Destination { get; set; }

    /// <summary>When true, an authored child node (an imported real model, e.g. a
    /// "Model" instance in a hand-built room scene) provides the visual cabinet —
    /// this script skips building its own procedural box cabinet. The screen static
    /// overlay and the walk-in trigger still get built, positioned by the exported
    /// fields below so they can be nudged in the editor to line up with the model.</summary>
    [Export] public bool AuthoredVisual { get; set; }

    [Export] public Vector3 ScreenPosition { get; set; } = new(0, 0.66f, -0.19f);
    [Export] public Vector2 ScreenSize { get; set; } = new(0.74f, 0.56f);
    [Export] public Vector3 GlowPoolPosition { get; set; } = new(0, 0.006f, -0.75f);
    [Export] public Vector3 TriggerPosition { get; set; } = new(0, 0.7f, -0.42f);
    [Export] public Vector3 TriggerSize { get; set; } = new(0.8f, 1.2f, 0.45f);

    /// <summary>Fired on the subscribing (simulating) peer when an avatar walks into
    /// the screen. Argument is the body; the host decides what "teleport" means.</summary>
    public event System.Action<CharacterBody3D>? BodyAtScreen;

    /// <summary>The GENERATED screen material, and null when an authored one was adopted instead
    /// — see the adopt-if-authored block in <c>_Ready</c>. Null is the "the shader owns the
    /// flicker" case, which is why <c>_Process</c> disables itself rather than null-checking
    /// every frame.</summary>
    private StandardMaterial3D? _screenMat;
    private double _time;
    private readonly RandomNumberGenerator _flickerRng = new();

    private static readonly StandardMaterial3D ShellMat = new()
    { AlbedoColor = new Color(0.32f, 0.24f, 0.17f), Roughness = 0.8f }; // old wood-grain cabinet tone
    private static readonly StandardMaterial3D TrimMat = new()
    { AlbedoColor = new Color(0.15f, 0.14f, 0.13f), Metallic = 0.3f, Roughness = 0.6f };

    public override void _Ready()
    {
        if (!AuthoredVisual)
        {
            // Cabinet: a fat old set on stubby legs, screen facing -Z.
            AddBox("Cabinet", new Vector3(1.0f, 0.85f, 0.55f), new Vector3(0, 0.62f, 0.1f), ShellMat, solid: true);
            AddBox("LegL", new Vector3(0.08f, 0.20f, 0.08f), new Vector3(-0.40f, 0.10f, 0.05f), TrimMat, solid: false);
            AddBox("LegR", new Vector3(0.08f, 0.20f, 0.08f), new Vector3(0.40f, 0.10f, 0.05f), TrimMat, solid: false);
            AddBox("Dial", new Vector3(0.06f, 0.06f, 0.03f), new Vector3(0.38f, 0.85f, -0.18f), TrimMat, solid: false);

            // Rabbit ears: two thin cylinders in a V.
            foreach (float sgn in new[] { -1f, 1f })
            {
                AddChild(new MeshInstance3D
                {
                    Name = "Ear",
                    Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.010f, Height = 0.75f, RadialSegments = 6 },
                    MaterialOverride = TrimMat,
                    Position = new Vector3(sgn * 0.18f, 1.35f, 0.12f),
                    RotationDegrees = new Vector3(0, 0, sgn * -28f),
                });
            }
        }

        // The screen: seething static. Seamless noise as albedo+emission; the flicker
        // is just emission-energy wobble in _Process. Position/size are exported so an
        // authored real-TV model can have this overlay nudged to sit on its screen.
        //
        // AN AUTHORED MATERIAL WINS, AND UNTIL 2026-08-28 IT DID NOT (packet FIX-1 item 1).
        // The adopt-if-authored path adopted the authored Screen NODE and then assigned this
        // generated material straight over its `material_override` -- so BT-9's `tv_static.tres`,
        // which TvRoom.tscn has referenced since the day it was authored, had never once been on
        // screen. Nothing caught it because BT-10's acceptance criterion measured screen
        // BRIGHTNESS, not which material produced it, and the generated material is bright enough
        // to pass a brightness test while being the wrong thing.
        //
        // WHICH MATERIAL IS INTENDED, decided rather than defaulted: the AUTHORED one, wherever a
        // scene supplies one. `tv_static.gdshader` carries the ported flicker VERBATIM (the same
        // 1.05 + 0.25*sin(9.7t)*sin(2.3t), the same 1% stutter to 0.4) and adds the three things a
        // StandardMaterial3D cannot do -- noise that actually crawls, scanlines, a vignette -- for
        // no per-frame C# and no per-TV material instance. It also carries BT-9b's measured
        // `emission_energy = 3.2`, without which the screen renders as the mid-grey rectangle a
        // headed night capture caught it as. The generated material is NOT deleted and the .tres
        // is NOT deleted: mp-foundation authors no Screen node at all, so every lookup there is
        // null and the build path below still runs exactly as it always did. Both are live, on
        // disjoint inputs, and this file stays re-syncable in both directions.
        MeshInstance3D? authoredScreen = GetNodeOrNull<MeshInstance3D>("Screen");
        if (authoredScreen?.MaterialOverride is not null)
        {
            // Authored material present: do not touch it. `_screenMat` stays null and _Process
            // turns itself off -- the shader owns the flicker in this case, and a C# wobble on a
            // material this node does not own would be a second animator on one value.
            SetProcess(false);
        }
        else
        {
            var staticNoise = new NoiseTexture2D
            {
                Width = 128,
                Height = 128,
                Noise = new FastNoiseLite { Frequency = 0.9f, NoiseType = FastNoiseLite.NoiseTypeEnum.Value },
            };
            _screenMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.75f, 0.78f, 0.80f),
                AlbedoTexture = staticNoise,
                EmissionEnabled = true,
                Emission = new Color(0.72f, 0.76f, 0.82f),
                EmissionTexture = staticNoise,
                EmissionEnergyMultiplier = 1.2f,
                Roughness = 0.2f,
            };
            if (authoredScreen is not null)
                authoredScreen.MaterialOverride = _screenMat;
            else AddChild(new MeshInstance3D
            {
                Name = "Screen",
                Mesh = new BoxMesh { Size = new Vector3(ScreenSize.X, ScreenSize.Y, 0.02f) },
                MaterialOverride = _screenMat,
                Position = ScreenPosition,
            });
        }

        // Soft screen-glow pool in front (emissive quad, not a light).
        if (GetNodeOrNull<MeshInstance3D>("ScreenGlowPool") is null) AddChild(new MeshInstance3D
        {
            Name = "ScreenGlowPool",
            Mesh = new BoxMesh { Size = new Vector3(1.1f, 0.012f, 0.9f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.11f, 0.13f),
                EmissionEnabled = true,
                Emission = new Color(0.55f, 0.60f, 0.70f),
                EmissionEnergyMultiplier = 0.18f,
            },
            Position = GlowPoolPosition,
        });

        // The doorway: a trigger hugging the screen face.
        Area3D? trigger = GetNodeOrNull<Area3D>("ScreenTrigger");
        if (trigger is null)
        {
            trigger = new Area3D
            {
                Name = "ScreenTrigger",
                Monitoring = true,
                CollisionMask = 1,
                Position = TriggerPosition,
            };
            trigger.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = TriggerSize },
            });
            AddChild(trigger);
        }
        trigger.BodyEntered += body =>
        {
            if (body is CharacterBody3D avatar)
                BodyAtScreen?.Invoke(avatar);
        };
    }

    /// <summary>The transition: one white-static flash over the screen and a crackle,
    /// fading in ~0.35 s. A single self-freeing CanvasLayer ColorRect — no
    /// post-processing, no shader, no budget. Local-player-only by nature (each client
    /// plays its own when IT gets teleported).</summary>
    public static void PlayTransitionFlash(Node parent, Vector3 atGlobal)
    {
        var layer = new CanvasLayer { Name = "TvFlash", Layer = 90 };
        var rect = new ColorRect
        {
            Color = new Color(0.9f, 0.92f, 0.95f, 0.85f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rect);
        parent.AddChild(layer);
        SfxLab.PlayStream3D(parent, atGlobal, SfxLab.Get(Sfx.Crackle), volumeDb: -6f);
        Tween tween = layer.CreateTween();
        tween.TweenProperty(rect, "color:a", 0.0f, 0.35f);
        tween.TweenCallback(Callable.From(layer.QueueFree));
    }

    public override void _Process(double delta)
    {
        // Null when an authored material was adopted; _Ready already called SetProcess(false), and
        // this is the belt-and-braces half of that (a caller re-enabling processing must not crash).
        if (_screenMat is null)
            return;
        _time += delta;
        // Static breathes and occasionally stutters — a dying broadcast, not a strobe.
        float basePulse = 1.05f + 0.25f * Mathf.Sin((float)_time * 9.7f) * Mathf.Sin((float)_time * 2.3f);
        if (_flickerRng.Randf() < 0.01f)
            basePulse *= 0.4f;
        _screenMat.EmissionEnergyMultiplier = basePulse;
    }

    private void AddBox(string name, Vector3 size, Vector3 pos, Material mat, bool solid)
    {
        if (solid)
        {
            var body = new StaticBody3D { Name = name, Position = pos };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
            AddChild(body);
        }
        else
        {
            AddChild(new MeshInstance3D
            {
                Name = name,
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = mat,
                Position = pos,
            });
        }
    }
}
