using System.IO;
using System.Linq;
using MpFoundation;
using MpFoundation.World;

namespace SailNet.Tests;

/// <summary>
/// The GraphicsQuality tier writer (Issue #201), pinned at the two places it can silently die.
///
/// <para><b>The defect this guards against recurring:</b> <c>GraphicsQuality.Current</c> was a
/// public property with no writer anywhere in the repository — every session ever run, every
/// playtest, every perf measurement, silently held the compiled-in default. Nothing crashed,
/// nothing logged, and the tier system went months without being exercised once. A writer that
/// quietly stops being called reproduces that exactly, so this file pins (1) the resolution rule
/// itself, engine-free, and (2) that Boot still routes through it — with
/// tests/Run-GraphicsTierTest.ps1 proving the same thing at runtime against a real boot.</para>
///
/// <para><b>The default choice is a pinned product decision, not an accident.</b> The comps
/// (Lethal Company / R.E.P.O. / Content Warning) set the shipped-quality floor, so a windowed
/// player's out-of-the-box tier is High — the full intended visual tier — and the floor card
/// reaches its Medium target through the settings panel. Whoever changes
/// <see cref="GraphicsSettings.ShipDefault"/> should have to come past a red test and say why.</para>
/// </summary>
public class GraphicsSettingsTests
{
    // ---------------------------------------------------------------------------------------
    // The resolution rule (pure, engine-free)
    // ---------------------------------------------------------------------------------------

    /// <summary>The ship default is the full intended visual tier. If this goes red, someone
    /// reduced the out-of-the-box experience — which may be right, but is a product decision
    /// that belongs in a commit message, not a drift.</summary>
    [Fact]
    public void ShipDefault_IsTheFullIntendedVisualTier()
    {
        Assert.Equal(GraphicsQuality.Tier.High, GraphicsSettings.ShipDefault);
    }

    /// <summary>A fresh install (nothing persisted), windowed: the ship default, never the
    /// compiled-in fallback. This is the assertion that a deleted writer breaks — writerless,
    /// the startup value regresses to <c>GraphicsQuality.Current</c>'s initializer (Medium),
    /// which this pins as NOT the resolved windowed default.</summary>
    [Fact]
    public void WindowedWithNothingPersisted_ResolvesShipDefault_NotTheCompiledInFallback()
    {
        GraphicsQuality.Tier resolved = GraphicsSettings.Resolve(headless: false, persisted: null);
        Assert.Equal(GraphicsSettings.ShipDefault, resolved);
        Assert.NotEqual(GraphicsQuality.Tier.Medium, resolved);
    }

    /// <summary>A persisted choice wins for a windowed player — the settings panel is a real
    /// writer, not a suggestion.</summary>
    [Theory]
    [InlineData(GraphicsQuality.Tier.Low)]
    [InlineData(GraphicsQuality.Tier.Medium)]
    [InlineData(GraphicsQuality.Tier.High)]
    public void WindowedWithAPersistedChoice_ResolvesThatChoice(GraphicsQuality.Tier persisted)
    {
        Assert.Equal(persisted, GraphicsSettings.Resolve(headless: false, persisted: persisted));
    }

    /// <summary>Headless resolves the historical Medium baseline regardless of any persisted
    /// value: nothing renders on the dummy renderer, and every headless measurement ever taken
    /// ran Medium — re-baselining the whole test suite is not something a settings file on the
    /// machine should be able to do. (--graphics still overrides, in Boot, after this.)</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(GraphicsQuality.Tier.Low)]
    [InlineData(GraphicsQuality.Tier.High)]
    public void Headless_ResolvesTheHistoricalBaseline_IgnoringPersisted(GraphicsQuality.Tier? persisted)
    {
        Assert.Equal(GraphicsSettings.HeadlessDefault,
            GraphicsSettings.Resolve(headless: true, persisted: persisted));
        Assert.Equal(GraphicsQuality.Tier.Medium, GraphicsSettings.HeadlessDefault);
    }

    /// <summary>Every source the resolver can report is distinct, so a log line reading
    /// "(ship default)" can never actually have come from the settings file.</summary>
    [Fact]
    public void ResolveWithSource_NamesEachRungDistinctly()
    {
        string headless = GraphicsSettings.ResolveWithSource(true, null).Source;
        string persisted = GraphicsSettings.ResolveWithSource(false, GraphicsQuality.Tier.Low).Source;
        string shipped = GraphicsSettings.ResolveWithSource(false, null).Source;
        Assert.NotEqual(headless, persisted);
        Assert.NotEqual(headless, shipped);
        Assert.NotEqual(persisted, shipped);
    }

    // ---------------------------------------------------------------------------------------
    // The persisted vocabulary
    // ---------------------------------------------------------------------------------------

    /// <summary>TierName and ParseTier are exact inverses for every tier, so what SetTier writes
    /// today is what Load reads tomorrow. A one-sided rename here would silently reset every
    /// player to the default on their next launch.</summary>
    [Theory]
    [InlineData(GraphicsQuality.Tier.Low)]
    [InlineData(GraphicsQuality.Tier.Medium)]
    [InlineData(GraphicsQuality.Tier.High)]
    public void TierName_RoundTripsThroughParseTier(GraphicsQuality.Tier tier)
    {
        Assert.Equal(tier, GraphicsSettings.ParseTier(GraphicsSettings.TierName(tier)));
    }

    /// <summary>ParseTier accepts the --graphics vocabulary in any casing (a hand-edited cfg is
    /// legal input) and rejects everything else as null — which resolves to the default rather
    /// than throwing in Boot before any scene shows.</summary>
    [Theory]
    [InlineData("low", GraphicsQuality.Tier.Low)]
    [InlineData("MEDIUM", GraphicsQuality.Tier.Medium)]
    [InlineData("  High  ", GraphicsQuality.Tier.High)]
    public void ParseTier_AcceptsTheGraphicsFlagVocabulary(string value, GraphicsQuality.Tier expected)
    {
        Assert.Equal(expected, GraphicsSettings.ParseTier(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ultra")]
    [InlineData("2")]
    [InlineData(null)]
    public void ParseTier_RejectsGarbageAsNull(string? value)
    {
        Assert.Null(GraphicsSettings.ParseTier(value));
    }

    // ---------------------------------------------------------------------------------------
    // The boot wiring (source contract — the writerless-again tripwire dotnet test can run)
    // ---------------------------------------------------------------------------------------

    /// <summary>Boot must route the tier through GraphicsSettings (Load, then Apply) — the
    /// assignment Issue #201 was missing. A refactor that drops either call recreates the
    /// original defect byte for byte: no crash, no log, every session on the compiled-in
    /// default. Runtime proof of the same wiring is tests/Run-GraphicsTierTest.ps1, which greps
    /// the writer's own boot line out of a real headless launch; this source contract is the
    /// half `dotnet test` can enforce without an engine.
    ///
    /// <para>Guarded by two controls, because a source scan that silently never matches is a
    /// failure this technique has already shipped once (see WaterFxTuningTests): it must find a
    /// call that is definitely there, and it must NOT find one that definitely is not.</para></summary>
    [Fact]
    public void Boot_RoutesTheTierThroughGraphicsSettings()
    {
        string boot = BootCode();

        Assert.Contains("options.GraphicsTier is { } tier", boot);        // control: finds present
        Assert.DoesNotContain("ThisCallDoesNotExist.Apply(", boot);       // control: not blind

        Assert.Contains("GraphicsSettings.Load()", boot);
        Assert.Contains("GraphicsSettings.Apply(", boot);
    }

    /// <summary>The settings writer must run BEFORE the --graphics override, or a parity test
    /// forcing two peers onto different tiers would have its forced tier clobbered by the
    /// settings file and prove nothing (tests/Run-FireNodeTest.ps1 part 2's precondition).</summary>
    [Fact]
    public void Boot_AppliesSettingsBeforeTheGraphicsFlagOverride()
    {
        string boot = BootCode();
        int settings = boot.IndexOf("GraphicsSettings.Apply(", System.StringComparison.Ordinal);
        int flagOverride = boot.IndexOf("tier forced to", System.StringComparison.Ordinal);

        Assert.True(settings >= 0, "could not find GraphicsSettings.Apply in Boot.cs");
        Assert.True(flagOverride >= 0, "could not find the --graphics override print in Boot.cs");
        Assert.True(flagOverride > settings,
            "the --graphics override runs before the settings writer, so the settings file " +
            "clobbers a forced tier and the parity suite's precondition is broken");
    }

    /// <summary>The stripper the two Boot contracts above depend on, pinned on both arms.
    ///
    /// <para>Measured 2026-08-13, and the reason this test exists: with the raw file text, both
    /// Boot contracts stayed GREEN against a writer that had been commented out — the assertion
    /// text matched the call inside its own <c>//</c>. A commented-out writer reproduces Issue
    /// #201 exactly (no crash, no log, every session on the compiled-in default), so the scan has
    /// to read code and not prose. Deletion was always caught; commenting-out was the hole.</para></summary>
    [Fact]
    public void StripCommentLines_DropsCommentedOutCode_ButKeepsLiveCode()
    {
        Assert.DoesNotContain("GraphicsSettings.Load()",
            StripCommentLines("class X {\n    // GraphicsSettings.Load();\n}"));
        Assert.DoesNotContain("GraphicsSettings.Load()",
            StripCommentLines("class X {\n    /// GraphicsSettings.Load();\n}"));
        Assert.Contains("GraphicsSettings.Load()",
            StripCommentLines("class X {\n    GraphicsSettings.Load();\n}"));
    }

    /// <summary>Boot.cs with whole-line comments removed, so a source contract cannot be
    /// satisfied by a mention of the call in a comment. Trailing comments on a live line are
    /// left alone — the line still carries real code, which is the thing being asserted.</summary>
    private static string BootCode() =>
        StripCommentLines(File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Boot.cs")));

    private static string StripCommentLines(string source) => string.Join("\n",
        source.Split('\n').Where(line =>
        {
            string t = line.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
