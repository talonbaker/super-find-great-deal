using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>Spec §6.6, enforced instead of remembered.</b>
/// <c>docs/design/2026-08-27-movement-verbs-state-machine.md</c> §6.6:
///
/// <para><i>"the chain has no numeric readout, no bar, no icon, no text, no HUD element of any kind,
/// in any build, <b>including the lab</b>. §12 is the entire communication channel. This is stated
/// as a prohibition rather than an omission so that a debug readout does not arrive later as a
/// convenience and stay."</i></para>
///
/// <para>"So that it does not arrive later" is a claim about the future, and a claim about the
/// future is a test or it is nothing. MOVE-5c ships the §12 pose; this ships the prohibition that
/// makes the pose the <i>only</i> channel. It is deliberately stricter than Talon's own brief, which
/// only forbade a floating counter in the game — §6.6 names the lab, and MOVE-5b recorded the
/// collision it settles (MOVE-5d's playground readout may show the verb, the verb clock and the
/// air-jump counter, and must not show the chain).</para>
///
/// <para><b>Method.</b> Every UI-facing source and every dev lab is swept, whole file, for the four
/// identifiers by which the chain can be reached: <c>ChainDepth</c>, <c>ChainDepthNow</c>,
/// <c>ChainTimerTicks</c> and <c>ChainRead</c>. The sweep deliberately over-approximates — a hit in
/// a comment costs a reword, a missed <c>Label.Text</c> costs the mechanic's only channel. The pose
/// layer itself (<c>AvatarVisual</c>) and the replication seam (<c>SandboxAvatar</c>) are outside
/// the scanned tree by design: they are what §12 is <i>made of</i>, and §6.6 forbids showing the
/// chain, not reading it.</para>
///
/// <para><b>Positive control, per the verification-methods law.</b> An absence check is worthless
/// until it can prove a presence, and it can fail two independent ways: the matcher can be wrong,
/// and the file set can be empty or pointed at the wrong tree. Both are controlled below — a
/// planted counterexample the matcher must find, and a real identifier that genuinely lives in the
/// scanned tree today and must come back with hits.</para>
/// </summary>
public class ChainNoUiAuditTests
{
    /// <summary>Assembled at runtime so THIS file never contains a contiguous forbidden identifier —
    /// the same trick <see cref="DeadNameAuditTests"/> uses, and for the same reason: a sweep that
    /// grew to cover its own tests must not trip on its own vocabulary.</summary>
    private static readonly string[] Forbidden =
    {
        "Chain" + "Depth",
        "Chain" + "TimerTicks",
        "Chain" + "Read",
    };

    [Fact]
    public void PositiveControl_TheMatcherFindsAPlantedReadout()
    {
        // Exactly the line §6.6 exists to prevent, written out and fed to the scanner.
        string planted = "_label.Text = $\"chain {avatar." + "Chain" + "DepthNow}\";";
        Assert.NotEmpty(HitsIn(planted));
        // Case-insensitive, because a field named `chainDepth` is the same defect in lower case.
        Assert.NotEmpty(HitsIn(planted.ToLowerInvariant()));
        // And it must not fire on text that merely mentions a chain — a scanner that cries wolf
        // gets suppressed, which is how a real hit ships.
        Assert.Empty(HitsIn("// the chain is deliberately invisible; see spec 6.6"));
    }

    [Fact]
    public void PositiveControl_TheSweepIsLookingAtRealFilesInTheRightTree()
    {
        // The second way an absence check fails: an empty or misdirected file set, which reports a
        // clean all-clear over nothing at all. So the sweep is required to find a real identifier
        // that genuinely lives in the scanned tree — UiThemeService, which every screen is built
        // on — before its silence about the chain counts for anything.
        string root = FindRepoRoot();
        var files = new List<string>(ScannedSources(root));
        Assert.True(files.Count > 40, $"only {files.Count} sources scanned — wrong tree");
        Assert.Contains(files, f => f.EndsWith("DevScreenshot.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("SettingsPanel.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(".tscn", StringComparison.Ordinal));

        int sentinelHits = 0;
        foreach (string file in files)
            if (File.ReadAllText(file).Contains("UiThemeService", StringComparison.Ordinal))
                sentinelHits++;
        Assert.True(sentinelHits > 0,
            "the sweep found zero hits for an identifier that is definitely in this tree — "
            + "it is not actually reading these files, and its all-clear is worthless");
    }

    [Fact]
    public void NoUiOrLabSourceCanReachTheChain()
    {
        string root = FindRepoRoot();
        var hits = new List<string>();
        foreach (string file in ScannedSources(root))
        {
            string text = File.ReadAllText(file);
            foreach (string hit in HitsIn(text))
                hits.Add($"{Path.GetRelativePath(root, file)}: {hit}");
        }
        Assert.True(hits.Count == 0,
            "spec 6.6 forbids ANY chain readout, in any build, including the lab. Found: "
            + string.Join("; ", hits));
    }

    private static IEnumerable<string> HitsIn(string text)
    {
        foreach (string token in Forbidden)
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                yield return token;
    }

    /// <summary>Every source that can put something in front of a human: the shipped UI, the UI
    /// scenes, and <c>scripts/dev</c> — which is where the labs and the playground live, and which
    /// §6.6's "including the lab" is specifically about.</summary>
    private static IEnumerable<string> ScannedSources(string root)
    {
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "scripts", "ui"), "*.cs", SearchOption.AllDirectories))
        {
            yield return file;
        }
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "scripts", "dev"), "*.cs", SearchOption.AllDirectories))
        {
            yield return file;
        }
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "scenes", "ui"), "*.tscn", SearchOption.AllDirectories))
        {
            yield return file;
        }
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
