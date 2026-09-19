using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MpFoundation.Ui.Design;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The silent-fallback guard.</b>
///
/// <para>The runtime theme is hung on the scene tree's root window, and Godot's theme lookup
/// walks up the tree and then falls through to the <i>project default</i> theme for any item no
/// ancestor defines. That default is still <c>resources/UITheme.tres</c> — the committed file,
/// in the old dusk-indigo palette.</para>
///
/// <para>So a control type the factory forgets does not render unstyled, which would be obvious.
/// It renders in the previous palette, on one control, silently. It looks like a bug in that
/// screen rather than a hole in the system, and it is exactly the failure mode that would let
/// the old look creep back one widget at a time.</para>
///
/// <para>Checked against the shipped <c>.tres</c> as text, because constructing a Godot
/// <c>Theme</c> needs an engine this suite deliberately does not have.</para>
/// </summary>
public class UiThemeCoverageTests
{
    /// <summary>Type names in the committed theme, e.g. <c>MenuAction/colors/font_color</c>.</summary>
    private static readonly Regex TypeKey = new(@"^([A-Za-z]+)/(?:colors|fonts|font_sizes|styles|constants)/",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void TheFactory_CoversEveryTypeTheProjectThemeDefines()
    {
        var inTres = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TypeKey.Matches(File.ReadAllText(ProjectThemePath())))
            inTres.Add(m.Groups[1].Value);

        Assert.True(inTres.Count > 20, $"only {inTres.Count} types parsed out of the .tres — the parser is wrong, not the theme.");

        var covered = new HashSet<string>(UiThemeFactory.CoveredTypes, StringComparer.Ordinal);
        List<string> missing = inTres.Where(t => !covered.Contains(t)).OrderBy(t => t).ToList();

        Assert.True(missing.Count == 0,
            "these types exist in the project theme but the factory does not define them, so they "
            + "would silently render in the OLD palette: " + string.Join(", ", missing));
    }

    /// <summary>The positive control: prove the parser actually finds types, and that a planted
    /// gap would be reported. An absence check with a broken parser passes forever.</summary>
    [Fact]
    public void TheCoverageCheck_WouldNoticeAGap()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TypeKey.Matches(File.ReadAllText(ProjectThemePath())))
            found.Add(m.Groups[1].Value);

        // Types the shipped theme certainly has — if the parser stops finding these it has
        // silently started reporting an all-clear over nothing.
        Assert.Contains("MenuAction", found);
        Assert.Contains("PrimaryAction", found);
        Assert.Contains("LineEdit", found);

        // And a type nobody covers would be reported.
        Assert.DoesNotContain("NotARealThemeType", UiThemeFactory.CoveredTypes);
    }

    /// <summary>No duplicates in the covered list — a duplicate is usually the trace of a
    /// half-finished rename, and it makes the count meaningless.</summary>
    [Fact]
    public void TheCoveredList_HasNoDuplicates() =>
        Assert.Equal(UiThemeFactory.CoveredTypes.Length, UiThemeFactory.CoveredTypes.Distinct().Count());

    private static string ProjectThemePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WatisWorld.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string path = Path.Combine(dir!.FullName, "resources", "UITheme.tres");
        Assert.True(File.Exists(path), $"the project theme is missing at {path}");
        return path;
    }
}
