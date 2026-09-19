using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The guard that stops the drift coming back.</b>
///
/// <para>Every finding in the AUD-UI-1 audit has the same shape: someone needed a colour or a
/// gap, typed one, and it was correct that day. Twenty-seven text colours, forty-six
/// backgrounds, five scrims and ~40 spacing values are not the result of anyone deciding
/// anything — they are the result of nobody being told there was a system. Consolidating them
/// once is worth very little if the next feature re-adds three, so the system has to be able to
/// notice.</para>
///
/// <para>This scans the shipped UI sources for hand-typed colour and spacing values, and fails
/// with the file and line. It is a source-text check rather than a runtime one because that is
/// what a token literal IS — a piece of text nobody routed through the palette.</para>
///
/// <para><b>Allow-listed by name, with the reason attached</b> (an unexplained exemption is how
/// a guard rots into decoration):</para>
/// <list type="bullet">
/// <item><c>design/</c> — the token definitions themselves. This is where hex values live.</item>
/// <item><c>PerfHud</c> — a developer instrument, deliberately outside the palette so nobody
/// mistakes a diagnostic for interface.</item>
/// <item><c>BootWarmup</c> — a 3D shader warm-up scene, not chrome: its colours are material
/// albedos feeding the pipeline, and none of it is ever seen as UI.</item>
/// <item><c>campfire/</c> — the diegetic 3D menu backdrop: firelight, moonlight, bark and the
/// burned-board render. Lighting values for a 3D scene are not interface tokens. The parts of
/// that scene which ARE interface — the menu entries' board and lettering — take tokens, and
/// are checked by the contrast tests.</item>
/// </list>
/// </summary>
public class UiNoBespokeStylingTests
{
    /// <summary>Files exempt from the scan, each with the reason it is exempt.</summary>
    private static readonly (string Fragment, string Why)[] Allowed =
    {
        ("ui/design/", "the token definitions themselves"),
        ("ui/campfire/", "diegetic 3D backdrop lighting, not interface chrome"),
        ("PerfHud.cs", "a developer instrument, deliberately outside the palette"),
        ("BootWarmup.cs", "3D shader warm-up materials, never seen as UI"),
    };

    /// <summary>A hand-typed colour: <c>new Color(0.42f, ...)</c> or <c>new Color("d28bcb")</c>.
    /// Deliberately does NOT match <c>new Color(someToken, 0.5f)</c> — re-alphaing a token is
    /// how translucency is expressed and is not a bespoke value.</summary>
    private static readonly Regex ColourLiteral = new(
        @"new Color\(\s*(?:""[0-9a-fA-F]{3,8}""|[0-9]+(?:\.[0-9]+)?f?\s*,)",
        RegexOptions.Compiled);

    /// <summary>A hand-typed gap: a numeric literal handed to a spacing constant.</summary>
    private static readonly Regex SpacingLiteral = new(
        @"AddThemeConstantOverride\(\s*""(?:separation|margin_left|margin_right|margin_top|margin_bottom)""\s*,\s*[0-9]+\s*\)",
        RegexOptions.Compiled);

    /// <summary>A hand-typed font size. Type rank comes from a theme variation; a screen picking
    /// its own size is how a thirteen-size ladder happens.</summary>
    private static readonly Regex FontSizeLiteral = new(
        @"AddThemeFontSizeOverride\(\s*""font_size""\s*,\s*[0-9]+\s*\)",
        RegexOptions.Compiled);

    /// <summary>Identity-with-alpha: <c>new Color(1, 1, 1, x)</c>. A fade, not a colour.</summary>
    private static readonly Regex WhiteWithAlpha = new(
        @"new Color\(\s*1f?\s*,\s*1f?\s*,\s*1f?\s*,", RegexOptions.Compiled);

    [Fact]
    public void NoUiSource_TypesItsOwnColour() => AssertNoMatches(ColourLiteral, "colour literal");

    [Fact]
    public void NoUiSource_TypesItsOwnSpacing() => AssertNoMatches(SpacingLiteral, "spacing literal");

    [Fact]
    public void NoUiSource_TypesItsOwnFontSize() => AssertNoMatches(FontSizeLiteral, "font-size literal");

    /// <summary>A colour set on a node in a <c>.tscn</c>.</summary>
    private static readonly Regex SceneColour = new(
        @"^(?:color|theme_override_colors/[a-z_]+) = Color\(", RegexOptions.Compiled);

    /// <summary>
    /// <b>Scenes count too.</b> The first cut of this guard scanned only <c>scripts/ui/**.cs</c>,
    /// and six scrims were sitting in scene files the whole time — a flat black at 0.55 under the
    /// pause menu and a near-black at 0.85 under five dialogs. The sweep could not see them and
    /// neither could the guard, so "one scrim" was a claim about half the project.
    ///
    /// <para>Those nodes have no <c>color</c> line at all now: their owning script binds them
    /// through <c>UiThemeService.BindScrim</c>, which means a scrim that loses its binding turns
    /// white and screams instead of quietly reverting to a palette we retired.</para>
    /// </summary>
    [Fact]
    public void NoScene_TypesItsOwnColour()
    {
        var hits = new List<string>();
        string root = FindRepoRoot();

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(root, "scenes"), "*.tscn", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (SceneColour.IsMatch(lines[i]))
                    hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}  {lines[i].Trim()}");
        }

        Assert.True(hits.Count == 0,
            $"{hits.Count} colour(s) set in scene files — bind them from the owning script instead "
            + "(UiThemeService.Bind / BindScrim):\n  " + string.Join("\n  ", hits));
    }

    /// <summary>The positive control. An absence check whose scanner is broken reports a clean
    /// bill of health forever, so prove the patterns actually bite before trusting them.</summary>
    [Fact]
    public void TheScanner_CatchesPlantedHits()
    {
        Assert.Matches(ColourLiteral, "var c = new Color(0.42f, 0.31f, 0.21f);");
        Assert.Matches(ColourLiteral, "var c = new Color(\"d28bcb\");");
        Assert.Matches(SpacingLiteral, "box.AddThemeConstantOverride(\"separation\", 14);");
        Assert.Matches(FontSizeLiteral, "label.AddThemeFontSizeOverride(\"font_size\", 22);");

        // ...and does NOT bite the legitimate forms, or it would be unusable and get deleted.
        Assert.DoesNotMatch(ColourLiteral, "var c = new Color(tokens.Accent, 0.5f);");
        Assert.DoesNotMatch(ColourLiteral, "var c = new Color(t.PageGround, 1f);");
        Assert.DoesNotMatch(SpacingLiteral, "box.AddThemeConstantOverride(\"separation\", UiScale.SpaceNormal);");
    }

    /// <summary>And prove the sweep is looking somewhere. An all-clear over an empty file set is
    /// worth nothing.</summary>
    [Fact]
    public void TheSweep_ActuallySweptSomething()
    {
        var files = ScannedFiles().ToList();
        Assert.True(files.Count > 25, $"only {files.Count} UI sources scanned — the sweep is looking in the wrong place.");
        Assert.Contains(files, f => f.EndsWith("HudTheme.cs"));
        Assert.Contains(files, f => f.EndsWith("UiKit.cs"));
    }

    private static void AssertNoMatches(Regex pattern, string what)
    {
        var hits = new List<string>();
        string root = FindRepoRoot();

        foreach (string file in ScannedFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//"))
                    continue; // a literal quoted in a comment is documentation, usually of the
                              // very defect that was removed.

                // Modulate is an alpha/tint OPERATION, not a palette choice: `new Color(1,1,1,a)`
                // is identity-with-alpha, which is how every fade in the codebase is written.
                // Excluded by name rather than by pattern so the exemption is legible, and
                // narrowly — a Modulate holding an actual colour would still be caught below.
                if (line.Contains("Modulate", StringComparison.Ordinal) && WhiteWithAlpha.IsMatch(line))
                    continue;

                if (pattern.IsMatch(line))
                    hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}  {line.Trim()}");
            }
        }

        Assert.True(hits.Count == 0,
            $"{hits.Count} {what}(s) reintroduced — route them through UiTokens / UiScale instead:\n  "
            + string.Join("\n  ", hits));
    }

    /// <summary>
    /// Sources outside <c>scripts/ui/</c> that are nonetheless styled by the design system and
    /// must be swept, each named individually with the reason.
    ///
    /// <para><b>Why a list and not a second directory.</b> The sweep's whole claim is "every
    /// colour in the INTERFACE resolves through the tokens", and the boundary of "the interface"
    /// stopped matching the boundary of <c>scripts/ui/</c> the moment a piece of chrome moved
    /// into the world. Widening the sweep to <c>scripts/game/</c> would drag in several hundred
    /// files of material albedos, light energies and particle tints, which are not interface and
    /// which would then all need exemptions — an allow-list that long is how a guard rots into
    /// decoration. Naming the handful of world objects that ARE chrome keeps the claim true and
    /// keeps the reason attached to each one.</para>
    /// </summary>
    private static readonly (string RelativePath, string Why)[] AlsoScanned =
    {
        ("scripts/game/round/RoundClock.cs",
            "CLOCK-1: a diegetic readout. Its panel and digits take UiTokens so the wall and the "
            + "HUD strip cannot disagree about what dark is — the same substrate, on a mesh."),
    };

    /// <summary>The sweep reaches the world chrome, not only <c>scripts/ui/</c>. An absence check
    /// that stopped looking at a file the day it moved is an absence check about nothing.</summary>
    [Fact]
    public void TheSweep_ReachesTheWorldChromeToo()
    {
        var files = ScannedFiles().Select(f => f.Replace('\\', '/')).ToList();
        foreach ((string relative, string why) in AlsoScanned)
            Assert.True(files.Any(f => f.EndsWith(relative, StringComparison.Ordinal)),
                $"{relative} is not being scanned — {why}");
    }

    private static IEnumerable<string> ScannedFiles()
    {
        string root = FindRepoRoot();
        string uiRoot = Path.Combine(root, "scripts", "ui");
        foreach (string file in Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalised = file.Replace('\\', '/');
            if (Allowed.Any(a => normalised.Contains(a.Fragment, StringComparison.Ordinal)))
                continue;
            yield return file;
        }

        foreach ((string relative, string _) in AlsoScanned)
        {
            string file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            // A named file that has been moved or deleted is a silent hole in the sweep, so it
            // fails here rather than quietly shrinking the file set.
            Assert.True(File.Exists(file), $"{relative} is named in AlsoScanned but does not exist");
            yield return file;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
