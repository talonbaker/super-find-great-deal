using System;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Ui;

/// <summary>
/// The real work behind the splash screen's loading bar (never a fake timer — the bar
/// reports steps this node actually completed). Two costs get paid here instead of in
/// the player's first minute:
///
///   1. Audio synthesis: every SfxLab one-shot is baked now, so no gameplay moment pays
///      a first-use render.
///   2. Shader/pipeline warm-up: Godot compiles each unique material configuration the
///      first time it is DRAWN, not when it is loaded — the classic first-room hitch.
///      A tiny off-screen SubViewport draws one quad per representative material
///      configuration (matte, metallic, emissive, textured-emissive, alpha-transparent),
///      so those compiles happen behind the bar instead of mid-walk.
///
/// Best-effort by design: skipping the splash abandons the remainder harmlessly — the
/// game merely falls back to compiling on first sight, exactly as before.
/// </summary>
public partial class BootWarmup : Node
{
    /// <summary>(0..1 fraction done, short human label for the current step).</summary>
    public event Action<float, string>? Progress;

    private const int WarmFrames = 3; // draws needed to be sure each pipeline actually built

    public async Task RunAsync()
    {
        int step = 0;
        // Every Sfx variant, one per frame so the splash never visibly hitches.
        Sfx[] sounds = (Sfx[])Enum.GetValues(typeof(Sfx));
        int total = sounds.Length + WarmFrames + 1 /*viewport build*/;

        foreach (Sfx sfx in sounds)
        {
            if (sfx == Sfx.None)
                continue; // not a real sound; nothing to bake
            SfxLab.Get(sfx);
            Report(++step, total, "waking the sounds");
            if (!await NextFrameAlive())
                return;
        }

        SubViewport viewport = BuildWarmupViewport();
        AddChild(viewport);
        Report(++step, total, "warming the lights");
        for (int i = 0; i < WarmFrames; i++)
        {
            if (!await NextFrameAlive())
                return;
            Report(++step, total, "warming the lights");
        }
        viewport.QueueFree();
        Report(total, total, "ready");
    }

    private void Report(int step, int total, string label) =>
        Progress?.Invoke(Mathf.Clamp(step / (float)total, 0f, 1f), label);

    /// <summary>One frame later — false if this node was freed while waiting (the
    /// player skipped the splash), so the caller can abandon the remainder cleanly.</summary>
    private async Task<bool> NextFrameAlive()
    {
        SceneTree tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return GodotObject.IsInstanceValid(this) && IsInsideTree();
    }

    /// <summary>A 192×108 off-screen stage with one quad per material configuration.
    /// Update mode Always: it renders every frame while it exists, which is the point.</summary>
    private SubViewport BuildWarmupViewport()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(192, 108),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };

        // Match the neutral field's environment features (filmic tonemap + fog), so the
        // scene pipelines compile in the same configuration they'll run in.
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.02f, 0.02f, 0.03f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.5f),
                AmbientLightEnergy = 0.3f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                FogEnabled = true,
                FogDensity = 0.01f,
            },
        });
        viewport.AddChild(new OmniLight3D { Position = new Vector3(0, 0, 2), LightEnergy = 1f, OmniRange = 20f });

        // Synthetic stand-ins for the material FEATURE combinations any authored world is
        // likely to use (matte, metallic, emissive, textured-emissive, alpha-transparent).
        // What matters is the CONFIGURATION — any material with the same features shares
        // the compiled pipeline, so a fresh world's first-sight materials warm for free.
        var noise = new NoiseTexture2D { Width = 64, Height = 64, Noise = new FastNoiseLite() };
        Material[] mats =
        {
            new StandardMaterial3D { AlbedoColor = Colors.Gray, Roughness = 0.9f },
            new StandardMaterial3D { AlbedoColor = Colors.Gray, Metallic = 0.5f, Roughness = 0.5f },
            new StandardMaterial3D
            {
                EmissionEnabled = true, Emission = Colors.White, EmissionEnergyMultiplier = 1f,
            },
            new StandardMaterial3D // emissive + albedo noise texture (e.g. a screen prop)
            {
                AlbedoTexture = noise, EmissionEnabled = true, Emission = Colors.White,
                EmissionTexture = noise, EmissionEnergyMultiplier = 1f,
            },
            new StandardMaterial3D // alpha transparency (e.g. glass/dome props)
            {
                AlbedoColor = new Color(0.6f, 0.7f, 0.75f, 0.5f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };

        var camera = new Camera3D { Position = new Vector3(0, 0, 6) };
        viewport.AddChild(camera);
        int columns = Mathf.CeilToInt(Mathf.Sqrt(mats.Length));
        for (int i = 0; i < mats.Length; i++)
        {
            viewport.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f) },
                MaterialOverride = mats[i],
                Position = new Vector3((i % columns - columns / 2f) * 0.6f, (i / columns - columns / 2f) * 0.6f, 0),
            });
        }

        return viewport;
    }
}
