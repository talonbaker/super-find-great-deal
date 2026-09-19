using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// One-shot particle helpers — dust puffs for landings, footsteps, and prop impacts.
/// CPU particles with tiny low-poly sphere meshes: a handful per burst, effectively
/// free, and they read perfectly at this fidelity. Purely cosmetic; nothing gameplay-
/// visible ever depends on them.
/// </summary>
public static class JuiceFx
{
    /// <summary>Warm dust colour that sits well on the golden-hour palette.</summary>
    public static readonly Color Dust = new(0.93f, 0.87f, 0.74f, 0.85f);

    /// <summary>Spawns a self-freeing burst of soft particles at a world position.</summary>
    public static void Puff(Node parent, Vector3 globalPos, int count, Color color,
        float size = 0.07f, float speed = 1.3f, float lifetime = 0.55f)
    {
        var particles = new CpuParticles3D
        {
            Amount = Mathf.Max(1, count),
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 1f,
            Direction = Vector3.Up,
            Spread = 75f,
            InitialVelocityMin = speed * 0.35f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -0.8f, 0),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.5f,
            Color = color,
            Mesh = new SphereMesh
            {
                Radius = size,
                Height = size * 2f,
                RadialSegments = 6,
                Rings = 3,
                Material = PuffMaterial,
            },
            // Emitting is turned on AFTER positioning (below), never here: a one-shot
            // Explosiveness=1 burst can otherwise fire at the parent's origin on the
            // frame it enters the tree, before GlobalPosition is applied — the "puff
            // spawns at world origin" bug. Position first, then emit.
            Emitting = false,
        };
        parent.AddChild(particles);
        particles.GlobalPosition = globalPos;
        particles.Emitting = true;
        particles.GetTree().CreateTimer(lifetime + 0.4).Timeout += () => particles.QueueFree();
    }

    private static readonly StandardMaterial3D PuffMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
