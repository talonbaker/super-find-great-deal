using System;
using System.Collections.Generic;
using System.IO;
using MpFoundation.Ui;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-B1 acceptance criterion 7: the dead name appears in ZERO player-visible
/// strings — legal-driven (see the title-clearance brief), shipped in this packet.
///
/// <b>Method, stated:</b> (1) the compiled surface — <see cref="Branding.Wordmark"/>, the
/// single constant every wordmark surface (window title, title screen, campfire plank
/// sign, menus) reads from, asserted directly against the compiled assembly, immune to
/// source-scan blind spots; (2) a source sweep of every UI-facing string source — all .cs
/// under <c>scripts/ui/</c> and all .tscn under <c>scenes/ui/</c>, full file text,
/// case-insensitive — which over-approximates "player-visible" on purpose (a comment hit
/// costs a rename; a missed literal costs a lawsuit). <c>Sail</c> namespaces, paths and
/// internal identifiers stay by canon and are not scanned.
///
/// <b>Positive control (the verification-methods law):</b> the same scanner is fed a
/// planted counterexample and must find it before its all-clear counts for anything.
/// </summary>
public class DeadNameAuditTests
{
    // Assembled at runtime so THIS file never contains the contiguous dead name and the
    // sweep can legitimately cover tests too if it ever grows to.
    private static string DeadName => "SLEEP" + "AWAY";

    [Fact]
    public void Wordmark_CompiledConstant_IsNameAgnostic()
    {
        Assert.DoesNotContain(DeadName, Branding.Wordmark, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(Branding.Wordmark)); // a blank wordmark would "pass" vacuously.
    }

    [Fact]
    public void PositiveControl_ScannerFindsAPlantedHit()
    {
        string planted = $"title.Text = \"{DeadName} CAMP\";";
        Assert.True(ContainsDeadName(planted), "the scanner cannot find a planted hit — its all-clear is worthless");
        Assert.True(ContainsDeadName(planted.ToLowerInvariant()), "the scanner is case-sensitive — it would miss 'Sleepaway'");
    }

    [Fact]
    public void UiSources_CarryZeroDeadNameHits()
    {
        string root = FindRepoRoot();
        var hits = new List<string>();
        foreach (string file in UiFacingSources(root))
            if (ContainsDeadName(File.ReadAllText(file)))
                hits.Add(Path.GetRelativePath(root, file));
        Assert.True(hits.Count == 0, "dead name found in: " + string.Join(", ", hits));
    }

    [Fact]
    public void UiSources_TheSweepActuallySweptSomething()
    {
        // An absence check over an empty file set proves nothing — assert the sweep saw
        // the surfaces it claims to cover.
        string root = FindRepoRoot();
        var files = new List<string>(UiFacingSources(root));
        Assert.Contains(files, f => f.EndsWith("Branding.cs"));
        // Was TitleScreen.cs, which B2 retired: it and MainMenu.cs were orphans that no scene
        // path had pointed at since the campfire menu replaced both (audit finding 8). The
        // sentinel is now the file that actually renders the menu.
        Assert.Contains(files, f => f.EndsWith("MainMenu.cs"));
        Assert.Contains(files, f => f.EndsWith(".tscn"));
        Assert.True(files.Count > 30, $"only {files.Count} UI sources found — the sweep is looking in the wrong place");
    }

    private static bool ContainsDeadName(string text) =>
        text.Contains(DeadName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> UiFacingSources(string root)
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "scripts", "ui"), "*.cs", SearchOption.AllDirectories))
            yield return file;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "scenes", "ui"), "*.tscn", SearchOption.AllDirectories))
            yield return file;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
