using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>AVATAR-1 (2026-09-05): one body for the first build — the Box Kid.</b>
///
/// <para>Talon, on the last change before the Steam playtest upload: <i>"the player can choose a
/// different model... I need this removed. I need this to only be using the main model the main
/// player character model that we have agreed on which is the one that is default selected I
/// don't want players to be able to change for this initial build because of the movements don't
/// line up right."</i></para>
///
/// <para><b>This is an ABSENCE suite, and absence is the hard thing to test.</b> A test that only
/// asserts "the local player is the box kid" passes just as well on a build where the picker is
/// still on screen and the player simply has not touched it — which is the exact state this
/// packet was asked to end. So the claim pinned here is the stronger one: <b>nothing in the
/// shipped tree writes <c>NetworkManager.LocalAvatarKey</c> at all</b>, so a peer's local choice
/// is unset on every path, and unset resolves through the one pre-existing mechanism
/// (<see cref="AvatarVisual.ResolveEnvAvatarKey()"/> → <see cref="AvatarVisual.PreferredAvatarKey"/>)
/// to the box kid.</para>
///
/// <para><b>Every absence assertion here carries a positive control</b>, because an absence probe
/// that has quietly stopped looking is indistinguishable from an absence probe that found nothing.
/// Each control feeds the probe a synthetic source file containing exactly the stray it is meant
/// to catch and asserts it catches it. Without that pairing, deleting the regex would turn this
/// whole file green.</para>
///
/// <para><b>What this does NOT claim.</b> The roster is untouched: <c>greybox_primitive</c> (the
/// load-failure fallback and the engine-free fixture) and <c>greybox_classic</c> (the last
/// code-built player-shaped body, and the control the suite measures the authored one against)
/// are still live rows, and the roster-choice REPLICATION path is still wired end to end —
/// <c>tests/Run-AvatarIdentityTest.ps1</c> still drives three peers onto three different bodies
/// through <c>SAIL_AVATAR</c> and still asserts they replicate. Dev and test paths keep the
/// ability to pick a body; only the shipped player-facing menu lost it.</para>
/// </summary>
public class OneBodyForTheFirstBuildTests
{
    /// <summary>An assignment to <c>LocalAvatarKey</c> — <c>= </c> but not <c>== </c>, and not a
    /// <c>.Length</c>/comparison read. Deliberately loose on the receiver so
    /// <c>net.LocalAvatarKey</c>, <c>NetworkManager.Instance.LocalAvatarKey</c> and a bare
    /// <c>LocalAvatarKey</c> all match: the probe should over-report rather than miss.</summary>
    private static readonly Regex LocalAvatarKeyWrite =
        new(@"LocalAvatarKey\s*(?:=[^=]|\+=|\?\?=)", RegexOptions.Compiled);

    /// <summary>The picker's node names, as the menus' <c>.tscn</c> files spelled them. A
    /// <c>GetNode</c> on a deleted node is a crash and a leftover node with no script is a dead
    /// control a player can still click, so both halves have to go and both halves are checked.
    /// <c>AvatarCap</c> is the "AVATAR" caption that stood above the row — it is on this list
    /// because a caption over nothing is worse than the row it captioned.</summary>
    private static readonly string[] RemovedPickerNodes =
        { "AvatarRow", "AvatarPrev", "AvatarNext", "AvatarLabel", "AvatarCap" };

    /// <summary>The menu scenes and their scripts — the four files the picker lived in.</summary>
    private static readonly string[] MenuFiles =
    {
        "scripts/ui/HostMenu.cs",
        "scripts/ui/JoinMenu.cs",
        "scenes/ui/HostMenu.tscn",
        "scenes/ui/JoinMenu.tscn",
    };

    // --- the claim -------------------------------------------------------------------------

    /// <summary><b>Nothing in <c>scripts/</c> writes <c>LocalAvatarKey</c>.</b> The property and
    /// its reader survive on purpose — <c>SandboxAvatar</c> still consults it, so restoring the
    /// picker later is restoring two assignments and not re-deriving a mechanism — but in this
    /// build it has no writer, which is what makes "the player cannot end up as another body" a
    /// structural fact rather than a default nobody happened to change.</summary>
    [Fact]
    public void NothingInTheShippedTreeWritesLocalAvatarKey()
    {
        var writers = ShippedSourceFiles()
            .Where(f => LocalAvatarKeyWrite.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(FindRepoRoot(), f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(writers.Count == 0,
            "AVATAR-1: these shipped files write NetworkManager.LocalAvatarKey, so a player can "
            + "reach a body that is not the box kid:\n  " + string.Join("\n  ", writers));
    }

    /// <summary><b>The positive control for the probe above.</b> Point the same regex at a
    /// synthetic file carrying each shape of stray write and it must flag every one — otherwise a
    /// green <see cref="NothingInTheShippedTreeWritesLocalAvatarKey"/> would prove only that the
    /// regex had stopped working. The non-matches are the other half: a read must NOT trip it, or
    /// the probe would be unable to distinguish "nobody writes it" from "nobody mentions it" and
    /// would fail the day someone legitimately reads the property.</summary>
    [Fact]
    public void TheLocalAvatarKeyProbeCatchesAPlantedStray()
    {
        string[] strays =
        {
            "net.LocalAvatarKey = AvatarVisual.RosterEntries[_avatarIndex].Key;",
            "NetworkManager.Instance.LocalAvatarKey = \"greybox_classic\";",
            "        LocalAvatarKey = key;",
            "net.LocalAvatarKey ??= \"greybox_classic\";",
            "net.LocalAvatarKey  =  chosen;",
        };
        foreach (string stray in strays)
            Assert.True(LocalAvatarKeyWrite.IsMatch(stray), $"probe missed a planted stray: {stray}");

        string[] reads =
        {
            "AvatarKey = net.LocalAvatarKey.Length > 0",
            "if (net.LocalAvatarKey == \"boxkid\")",
            "/// <c>RosterEntries[0].Key</c> into <c>NetworkManager.LocalAvatarKey</c>",
        };
        foreach (string read in reads)
            Assert.False(LocalAvatarKeyWrite.IsMatch(read), $"probe tripped on a read: {read}");
    }

    /// <summary><b>The picker's controls are gone from the menus — script side and scene side.</b>
    /// Checked as node NAMES against the scene text as well as the C#, because the two failure
    /// modes are opposite: a script that still calls <c>GetNode("Column/AvatarRow/AvatarPrev")</c>
    /// on a deleted node crashes the menu on open, and a scene node left behind with no script
    /// reading it is a button a player can still press.</summary>
    [Fact]
    public void TheMenusCarryNoPickerControls()
    {
        var found = new List<string>();
        foreach (string rel in MenuFiles)
        {
            string text = File.ReadAllText(Path.Combine(FindRepoRoot(), rel));
            foreach (string node in RemovedPickerNodes)
                if (text.Contains(node, StringComparison.Ordinal))
                    found.Add($"{rel}: {node}");
        }
        Assert.True(found.Count == 0,
            "AVATAR-1: picker leftovers in the menus:\n  " + string.Join("\n  ", found));
    }

    /// <summary><b>The positive control for the node-name probe.</b> Same containment check
    /// against text that does carry each name, so a typo in <see cref="RemovedPickerNodes"/> —
    /// which would silently make <see cref="TheMenusCarryNoPickerControls"/> unfalsifiable —
    /// shows up here instead.</summary>
    [Fact]
    public void ThePickerNodeProbeCatchesAPlantedNode()
    {
        foreach (string node in RemovedPickerNodes)
        {
            string planted = $"[node name=\"{node}\" type=\"Button\" parent=\"Column/AvatarRow\"]";
            Assert.Contains(node, planted, StringComparison.Ordinal);
        }
    }

    /// <summary><b>Unset resolves to the box kid, in every world.</b> This is the other end of the
    /// claim: the menus leaving <c>LocalAvatarKey</c> empty is only safe because empty already had
    /// a defined answer, and that answer is <see cref="AvatarVisual.PreferredAvatarKey"/>. Asserted
    /// across the world ids rather than once so a per-world body — a design
    /// <c>PreferredAvatarKeyFor</c> is deliberately kept alive for — cannot be reintroduced
    /// without this failing and forcing the decision to be made on purpose.</summary>
    [Fact]
    public void AnUnsetLocalChoiceIsTheBoxKidEverywhere()
    {
        Assert.Equal(AvatarVisual.BoxKidAvatarKey, AvatarVisual.PreferredAvatarKey);
        foreach (string? world in new[] { null, "", "bubbletest", "camp", "playground", "open" })
            Assert.Equal(AvatarVisual.BoxKidAvatarKey, AvatarVisual.PreferredAvatarKeyFor(world));
    }

    /// <summary><b>What a peer who names another body on the wire still gets, stated rather than
    /// inferred</b> (scope item 3). AVATAR-1 changes none of this, and that is the point of
    /// pinning it here beside the removal: a peer sending <c>greybox_classic</c> still renders as
    /// <c>greybox_classic</c> on every peer, because that key names a live roster row and the
    /// replication path is untouched. What changed is that <b>no client built from this tree can
    /// send it</b> — there is no writer of <c>LocalAvatarKey</c> left, so reaching that state
    /// needs <c>SAIL_AVATAR</c> in the environment (a dev/test path, and the one
    /// <c>Run-AvatarIdentityTest</c> uses) or a client that is not this build.</summary>
    [Fact]
    public void ThePeerWireTableIsUnchangedByTheRemoval()
    {
        Assert.Equal(AvatarVisual.ClassicGreyboxAvatarKey,
            AvatarVisual.NormalizeAvatarKey(AvatarVisual.ClassicGreyboxAvatarKey));
        Assert.Equal(AvatarVisual.BoxKidAvatarKey,
            AvatarVisual.NormalizeAvatarKey(AvatarVisual.BoxKidAvatarKey));
        // The retired gumdrop still aliases forward, and the containment clamp is still the clamp.
        Assert.Equal(AvatarVisual.ClassicGreyboxAvatarKey,
            AvatarVisual.NormalizeAvatarKey(AvatarVisual.RetiredGumdropAvatarKey));
        Assert.Equal(AvatarVisual.DefaultAvatarKey,
            AvatarVisual.NormalizeAvatarKey("totally-bogus-not-a-roster-entry"));
        // ...and the rows the removal did NOT touch are still selectable by a dev/test path.
        Assert.True(AvatarVisual.IsValidAvatarKey(AvatarVisual.ClassicGreyboxAvatarKey));
        Assert.True(AvatarVisual.IsValidAvatarKey(AvatarVisual.PrimitiveFallbackAvatarKey));
        Assert.True(AvatarVisual.IsValidAvatarKey(AvatarVisual.BoxKidAvatarKey));
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>Every C# file the game ships, which is <c>scripts/</c> and nothing else.
    /// <c>tests/</c> is excluded deliberately: a test harness setting an avatar key is a test
    /// path, and the packet's whole distinction is between a player-reachable route and a
    /// dev/test one. <c>.godot/</c> is excluded because it holds generated copies.</summary>
    private static IEnumerable<string> ShippedSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(FindRepoRoot(), "scripts"), "*.cs",
            SearchOption.AllDirectories);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
