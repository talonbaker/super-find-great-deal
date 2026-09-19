using Godot;

namespace MpFoundation.Game;

/// <summary>
/// CODE-BUILT CI SCAFFOLDING, NOT A PLAYED WORLD. Used only for the "open" and "propsync"
/// world ids — the dedicated replication-test world the scene suites need for a stable,
/// code-defined layout (see PropManager.SpawnInitialProps, which seeds the propsync crate/ball
/// set at known positions). Every real play session loads the authored bubbletest scene; an
/// interactive Host cannot reach this world. Do not add content here that a human is meant to
/// see or that a level designer needs to tune — that belongs in an authored .tscn.
///
/// Sky, key light, a 64 m ground slab, and obstacle furniture (pillars, steps, a jumpable
/// tower). Deliberately avatar-free and net-free so it drops into either host unchanged.
///
/// <see cref="SpawnPoints"/> is the one datum a host reads to place avatars.
/// </summary>
public partial class GameWorld : Node3D, IGameWorld
{
    private const float SpawnRadius = 4.0f;

    /// <summary>World-space avatar spawn points (a phyllotaxis ring, matching the prior
    /// Gameplay spawn layout). Hosts read [i % count] per joining peer.</summary>
    public Godot.Collections.Array<Vector3> SpawnPoints { get; } = new();

    public override void _Ready()
    {
        BuildEnvironment();
        BuildTerrain();
        for (int i = 0; i < 5; i++)
        {
            float angle = Mathf.DegToRad(137.5f * i);
            SpawnPoints.Add(new Vector3(Mathf.Cos(angle) * SpawnRadius, 1.1f, Mathf.Sin(angle) * SpawnRadius));
        }
    }

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.55f, 0.74f, 0.95f),
            SkyHorizonColor = new Color(0.92f, 0.88f, 0.86f),
            GroundBottomColor = new Color(0.78f, 0.82f, 0.76f),
            GroundHorizonColor = new Color(0.92f, 0.88f, 0.86f),
        };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        // Unshadowed per the Bible §4 (zero real-time shadow casters): a shadow-casting
        // directional re-renders the whole scene from the sun every frame; grounding is
        // BlobShadow's job now, at one quad per mover.
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48, 35, 0),
            LightColor = new Color(1.0f, 0.96f, 0.88f),
            LightEnergy = 1.3f,
            ShadowEnabled = false,
        });
    }

    private void BuildTerrain()
    {
        // Ground slab: saturated spring green so the pastel cast doesn't wash to white.
        AddStaticBox("Ground", new Vector3(64, 1, 64), new Vector3(0, -0.5f, 0),
            new Color(0.55f, 0.78f, 0.47f));

        // Bump furniture: pillars to run into, steps up to a jumpable ledge.
        AddStaticBox("PillarA", new Vector3(1.2f, 2.4f, 1.2f), new Vector3(5, 1.2f, -3),
            new Color(0.95f, 0.82f, 0.72f));
        AddStaticBox("PillarB", new Vector3(1.2f, 2.4f, 1.2f), new Vector3(-6, 1.2f, 4),
            new Color(0.80f, 0.86f, 0.94f));
        AddStaticBox("StepA", new Vector3(3, 1, 3), new Vector3(-8, 0.5f, -7),
            new Color(0.90f, 0.84f, 0.90f));
        AddStaticBox("StepB", new Vector3(3, 2.2f, 3), new Vector3(-11, 1.1f, -7),
            new Color(0.86f, 0.80f, 0.88f));
        // The big-fall ledge: a high perch to leap from (landings stay light).
        AddStaticBox("Tower", new Vector3(3, 4.4f, 3), new Vector3(-14, 2.2f, -7),
            new Color(0.82f, 0.76f, 0.86f));
    }

    private void AddStaticBox(string name, Vector3 size, Vector3 pos, Color color)
    {
        var body = new StaticBody3D { Name = name, Position = pos };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f },
        });
        AddChild(body);
    }
}
