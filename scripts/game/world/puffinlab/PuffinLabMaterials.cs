using Godot;

namespace Sail.Game.World.PuffinLab;

/// <summary>
/// <b>The two shared materials the ported tiny door needs, and nothing else.</b>
///
/// <para><b>Provenance.</b> A TRIMMED port of mp-foundation's
/// <c>scripts/game/world/HorrorDetail.cs</c> (EGG-1, 2026-09-02). That file is a 373-line toolkit
/// holding fourteen shared <see cref="StandardMaterial3D"/> statics plus the tube/ribbon/lump mesh
/// builders that generated the crawl before it was baked into
/// <c>LabAuthored.tscn</c>/<c>EscapeRouteAuthored.tscn</c>. The baked scenes carry their own
/// material copies, so in the ported set exactly TWO of those fourteen are still referenced —
/// <c>WoodMat</c> and <c>BrassMat</c>, both by <see cref="TinyDoor"/>, which builds its slab and
/// knob in code.</para>
///
/// <para><b>Why trimming was the right call rather than a verbatim copy.</b> Every one of those
/// fourteen materials is a <c>static readonly</c> field, so the C# type initializer builds ALL of
/// them the first time ANY of them is touched — and most carry two or three 512x512
/// <see cref="NoiseTexture2D"/>s. Porting the whole class would have paid roughly a dozen unused
/// procedural texture bakes on the first frame a player walks into the tiny door, for materials
/// nothing in this scene references. The mesh builders are dead for the same reason: the geometry
/// they used to make is authored data now.</para>
///
/// <para><b>If a later packet needs more of the palette</b>, take it from mp-foundation's
/// HorrorDetail rather than inventing it here — the numbers are the source's and the point of a
/// throwback is that it looks like the thing it is quoting.</para>
/// </summary>
public static class PuffinLabMaterials
{
    private static FastNoiseLite Fnl(int seed, float freq, int octaves) => new()
    {
        Seed = seed,
        Frequency = freq,
        FractalOctaves = octaves,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
    };

    /// <summary>Fractal-noise normal map: octaves stack 2-3 frequencies of grain into
    /// one texture, which is what makes flat code-built quads read as rough matter.</summary>
    private static NoiseTexture2D NormalTex(int seed, float freq, float strength) => new()
    {
        Width = 512,
        Height = 512,
        Seamless = true,
        AsNormalMap = true,
        BumpStrength = strength,
        GenerateMipmaps = true,
        Noise = Fnl(seed, freq, 5),
    };

    private static NoiseTexture2D GreyTex(int seed, float freq, int octaves = 4) => new()
    {
        Width = 512,
        Height = 512,
        Seamless = true,
        GenerateMipmaps = true,
        Noise = Fnl(seed, freq, octaves),
    };

    /// <summary>Old dense wood for the tiny door: grain comes from stretching the noise
    /// normal map hard along V, the way real grain streaks one way.</summary>
    public static readonly StandardMaterial3D WoodMat = new()
    {
        AlbedoColor = new Color(0.27f, 0.175f, 0.105f),
        NormalEnabled = true,
        NormalTexture = NormalTex(seed: 21, freq: 0.02f, strength: 6f),
        Roughness = 0.62f,
        RoughnessTexture = GreyTex(seed: 22, freq: 0.05f),
        Uv1Scale = new Vector3(0.4f, 7f, 1f),
    };

    /// <summary>The knob: shiny brass whose roughness map is all scuffs, scratches and
    /// finger-grease — the glint that pulls the eye right is the point of it.</summary>
    public static readonly StandardMaterial3D BrassMat = new()
    {
        AlbedoColor = new Color(0.82f, 0.62f, 0.28f),
        Metallic = 0.95f,
        Roughness = 0.34f,
        RoughnessTexture = GreyTex(seed: 31, freq: 0.18f, octaves: 5),
        NormalEnabled = true,
        NormalTexture = NormalTex(seed: 32, freq: 0.16f, strength: 2.5f),
        Uv1Scale = new Vector3(3f, 3f, 1f),
    };
}
