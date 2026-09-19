using Godot;
using MpFoundation.World;

namespace MpFoundation;

/// <summary>
/// The deliberate writer of <see cref="GraphicsQuality.Current"/> (Issue #201).
///
/// <para><b>Why this file exists.</b> <c>GraphicsQuality.Current</c> spent its first months as a
/// public property with no writer anywhere in the repository — every session ever run, including
/// every playtest, silently held the compiled-in default (Medium), and the tier system was never
/// exercised by anyone (found by PR #200 while building the parity-law evidence). This class is
/// the missing settings path: persisted like <see cref="DisplaySettings"/> (same
/// <c>user://settings.cfg</c>, same section), resolved and applied by <c>Boot._Ready</c> before
/// any world scene is instantiated (GrassField reads the tier in its own <c>_Ready</c> and never
/// rebuilds), and adjustable from the settings panel.</para>
///
/// <para><b>The resolution order, and why each rung is what it is:</b>
/// <list type="number">
/// <item><c>--graphics low|medium|high</c> — applied by Boot AFTER this class, so a parity test
/// (tests/Run-FireNodeTest.ps1) can still force two peers onto different tiers regardless of
/// what any settings file says.</item>
/// <item>A headless process resolves <see cref="HeadlessDefault"/> (Medium), ignoring the
/// settings file. Nothing renders on the dummy renderer, so the tier only sizes unseen build
/// work — and Medium is what every headless test and CI run has executed since the tier system
/// existed, so pinning it keeps every historical measurement comparable instead of silently
/// shifting the whole suite's baseline with this fix.</item>
/// <item>A windowed player resolves the persisted tier, or <see cref="ShipDefault"/> (High)
/// when nothing is persisted. High — not Medium — because the comps (Lethal Company / R.E.P.O. /
/// Content Warning) set the shipped-quality floor and the perf bar alike, and the game's
/// intended look is the tier that carries it; the §1.13 floor card (GTX 970 / 1650 at a locked
/// 60 on Medium) is served by the settings panel, one persisted click away, not by shipping
/// every capable machine a reduced world.</item>
/// </list></para>
///
/// <para><b>What "applied on change" honestly means.</b> <see cref="SetTier"/> writes
/// <c>GraphicsQuality.Current</c>, persists, and raises <see cref="TierChanged"/>; a world
/// re-applies the tier's shader parameters live (documented safe on
/// <see cref="GraphicsQuality.ApplyGround"/>). The GEOMETRY half — grass ring count and spacing
/// — is resolved when a world builds and is not rebuilt mid-session, so a mid-session tier
/// change takes full effect at the next world build. That limitation is GrassField's contract,
/// not this class's.</para>
/// </summary>
public static class GraphicsSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "display";
    private const string Key = "graphics_tier";

    /// <summary>The tier a windowed player gets when nothing is persisted: the full intended
    /// visual tier. See the class doc for why this is High and not the floor tier — changing
    /// this constant is a product decision, and tests/unit/GraphicsSettingsTests.cs pins it so
    /// it cannot drift by accident.</summary>
    public const GraphicsQuality.Tier ShipDefault = GraphicsQuality.Tier.High;

    /// <summary>The tier every headless process gets: the value all historical headless runs
    /// actually executed, kept so this fix does not silently re-baseline the whole test suite.
    /// <c>--graphics</c> overrides it for tests that need a specific tier.</summary>
    public const GraphicsQuality.Tier HeadlessDefault = GraphicsQuality.Tier.Medium;

    /// <summary>The persisted tier, or null when the settings file has none (fresh install,
    /// hand-edited garbage, wrong type). Null resolves to <see cref="ShipDefault"/>.</summary>
    public static GraphicsQuality.Tier? Persisted { get; private set; }

    /// <summary>Raised by <see cref="SetTier"/> after <c>GraphicsQuality.Current</c> has been
    /// updated, so a live world can re-apply the shader-parameter half without polling.</summary>
    public static event System.Action<GraphicsQuality.Tier>? TierChanged;

    public static void Load()
    {
        Persisted = null;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            // Type-checked read, same defensive stance as DisplaySettings.Load: a hand-edited
            // cfg survives ConfigFile.Load fine, and an unparseable value must fall back to
            // the default rather than throw in Boot._Ready before any scene shows.
            Variant raw = cfg.GetValue(Section, Key, "");
            if (raw.VariantType == Variant.Type.String)
                Persisted = ParseTier(raw.AsString());
        }
    }

    /// <summary>Resolves the tier for this process and writes it to
    /// <see cref="GraphicsQuality.Current"/> — THE deliberate startup assignment Issue #201 was
    /// missing. Called by Boot before any scene change; the printed line is load-bearing
    /// (tests/Run-GraphicsTierTest.ps1 greps it as proof the writer still runs at boot).</summary>
    public static GraphicsQuality.Tier Apply(bool headless)
    {
        (GraphicsQuality.Tier tier, string source) = ResolveWithSource(headless, Persisted);
        GraphicsQuality.Current = tier;
        GD.Print($"[graphics] tier {tier} ({source})");
        return tier;
    }

    /// <summary>The pure resolution rule, engine-free so `dotnet test` can pin it. See the
    /// class doc for the reasoning behind each rung.</summary>
    public static GraphicsQuality.Tier Resolve(bool headless, GraphicsQuality.Tier? persisted)
        => ResolveWithSource(headless, persisted).Tier;

    public static (GraphicsQuality.Tier Tier, string Source) ResolveWithSource(
        bool headless, GraphicsQuality.Tier? persisted)
    {
        if (headless)
            return (HeadlessDefault, "headless default");
        if (persisted is { } p)
            return (p, "persisted settings");
        return (ShipDefault, "ship default");
    }

    /// <summary>Settings-panel entry point: persists the choice, updates
    /// <see cref="GraphicsQuality.Current"/>, and raises <see cref="TierChanged"/> so a live
    /// world re-applies what can be re-applied (see the class doc for the honest scope).</summary>
    public static void SetTier(GraphicsQuality.Tier tier)
    {
        Persisted = tier;
        Save();
        GraphicsQuality.Current = tier;
        GD.Print($"[graphics] tier {tier} (settings change)");
        TierChanged?.Invoke(tier);
    }

    /// <summary>"low"/"medium"/"high" (any casing, surrounding whitespace tolerated) to a tier;
    /// anything else is null. Mirrors LaunchOptions' --graphics vocabulary exactly, so the CLI
    /// and the settings file can never mean different things by the same word.</summary>
    public static GraphicsQuality.Tier? ParseTier(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "low" => GraphicsQuality.Tier.Low,
            "medium" => GraphicsQuality.Tier.Medium,
            "high" => GraphicsQuality.Tier.High,
            _ => null,
        };

    /// <summary>The persisted spelling of a tier — the inverse of <see cref="ParseTier"/>.</summary>
    public static string TierName(GraphicsQuality.Tier tier) => tier switch
    {
        GraphicsQuality.Tier.Low => "low",
        GraphicsQuality.Tier.High => "high",
        _ => "medium",
    };

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep any other sections (display, audio, onboarding...)
        cfg.SetValue(Section, Key, TierName(Persisted ?? ShipDefault));
        cfg.Save(Path);
    }
}
