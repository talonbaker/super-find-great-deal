using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>SHELF-1's Godot-free gate on the one thing 130 authored props put at risk: the ids.</b>
///
/// <para><c>PropManager.AdoptAuthoredProps</c> assigns prop ids from 1000 in <b>ordinal
/// node-path sort order</b>, identically on every peer because every peer instances the same
/// <c>.tscn</c>. That makes the id of every prop in a room a function of the NAME of every other
/// prop in it — and nothing about that dependency is visible in a diff. Add one product, rename
/// one, or put one in the wrong parent, and every id above it moves by one, silently, on all
/// peers at once, so no desync check anywhere would notice. What breaks is everything that NAMES
/// an id: <c>tests/Run-PlaceTest.ps1</c>'s four crates, BTN-1's drop-off bin, REACH-1's target
/// prop, a hidden object surviving a reconnect.</para>
///
/// <para><b>Why here and not only in the scene suite.</b>
/// <c>tests/Run-AuthoredPropTest.ps1</c> proves the same block on a real server and a real late
/// joiner, which is the honest end-to-end proof and takes a minute of Godot. This reads the
/// authored <c>.tscn</c> TEXT and re-derives the block from the node names in milliseconds with
/// no engine, so the trap is caught by <c>dotnet test</c> before anyone launches anything. The
/// two are not redundant: this one cannot see whether adoption actually runs, and that one
/// cannot run on a machine with no Godot.</para>
///
/// <para><b>A hand parse rather than a scene load</b>, for <c>MaterialSfxTests</c>' reason: a
/// <c>PackedScene</c> is a <c>Resource</c> and needs the native runtime this suite deliberately
/// does not have. The parse is deliberately narrow — <c>[node ...]</c> headers and the
/// <c>script = ExtResource("2_netprop")</c> line — because a general .tscn parser would be a
/// second implementation of Godot's, and wrong.</para>
/// </summary>
public class AuthoredPropOrderTests
{
    private const string SearchRoomPath = "scenes/game/world/supermarket/SearchRoom.tscn";

    /// <summary>The block SHELF-1 authored, and the same table
    /// <c>tests/Run-AuthoredPropTest.ps1</c> and <c>SearchRoom.tscn</c>'s Stock banner state.
    /// First id, last id, and the node-name prefix every member of the block must carry.</summary>
    private static readonly (int First, int Last, string Prefix, string Why)[] Blocks =
    {
        (1000, 1003, "Prop_",          "CARRY-1's four crates; Run-PlaceTest.ps1 names all four ids"),
        (1004, 1009, "Stock/Bin_",     "the six floor bins"),
        (1010, 1057, "Stock/Box_",     "forty-eight cereal boxes"),
        (1058, 1105, "Stock/Can_",     "forty-eight cans"),
        (1106, 1129, "Stock/Produce_", "twenty-four pieces of produce"),
    };

    /// <summary><b>The container trick, stated as an assertion rather than as a comment.</b>
    /// Every product prefix SHELF-1 added — Bin, Box, Can, Produce — sorts BEFORE <c>Prop_</c>,
    /// because all four start below <c>P</c>. Authored beside CARRY-1's crates they would have
    /// renumbered all four of them by 126. They live under a node named <c>Stock</c> because
    /// <c>S</c> sorts after <c>P</c>, and that one character is the whole reason
    /// <c>Run-PlaceTest.ps1</c> still passes.</summary>
    [Fact]
    public void TheStockContainerIsWhatKeepsCarry1sCratesAt1000()
    {
        foreach (string bare in new[] { "Bin_0", "Box_000", "Can_000", "Produce_000" })
        {
            Assert.True(string.CompareOrdinal(bare, "Prop_0") < 0,
                $"'{bare}' sorts BEFORE 'Prop_0' — authored as a sibling it would renumber the crates");
            Assert.True(string.CompareOrdinal("Stock/" + bare, "Prop_0") > 0,
                $"'Stock/{bare}' must sort AFTER 'Prop_0'; that is the only thing protecting ids 1000..1003");
        }
    }

    /// <summary>Zero-padding, and why it is not cosmetic: an ordinal sort puts <c>Can_10</c>
    /// between <c>Can_1</c> and <c>Can_2</c>. Unpadded names would still be deterministic and
    /// still identical on every peer — and the id order would not be the reading order, so a
    /// level author would be wrong about which object a number named.</summary>
    [Fact]
    public void ZeroPaddingIsWhatMakesIdOrderTheReadingOrder()
    {
        var unpadded = new[] { "Can_1", "Can_2", "Can_10" }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Can_1", "Can_10", "Can_2" }, unpadded);

        var padded = new[] { "Can_001", "Can_002", "Can_010" }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Can_001", "Can_002", "Can_010" }, padded);
    }

    /// <summary><b>The id block, re-derived from the scene file's own node names.</b> This is the
    /// test that goes red on a rename, an addition or a removal anywhere under the search room —
    /// including one made by a lane that has never heard of this file.</summary>
    [Fact]
    public void TheSearchRoomsIdBlockIsExactlyWhatTheSceneFileSays()
    {
        IReadOnlyList<string> paths = AuthoredPropPaths(SearchRoomPath);

        Assert.Equal(Blocks[^1].Last - Blocks[0].First + 1, paths.Count);

        for (int i = 0; i < paths.Count; i++)
        {
            int id = 1000 + i;
            (int first, int last, string prefix, string why) =
                Blocks.First(b => id >= b.First && id <= b.Last);
            Assert.True(paths[i].StartsWith(prefix, StringComparison.Ordinal),
                $"id {id} is '{paths[i]}'; ids {first}..{last} must all be '{prefix}*' ({why}). "
                + "Something under the search room was renamed, added or removed, and every id "
                + "above it has moved with it.");
        }
    }

    /// <summary>Every product name is padded to three digits and the set is contiguous from 000.
    /// A gap (<c>Can_000, Can_002</c>) is legal to Godot and to the sort, and is exactly the kind
    /// of edit that leaves the block intact while making the NAME a lie about the id.</summary>
    [Fact]
    public void EveryProductIsPaddedToThreeDigitsAndTheRunHasNoGaps()
    {
        IReadOnlyList<string> paths = AuthoredPropPaths(SearchRoomPath);

        foreach ((string prefix, int count) in new[]
                 { ("Stock/Box_", 48), ("Stock/Can_", 48), ("Stock/Produce_", 24) })
        {
            var suffixes = paths.Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
                                .Select(p => p.Substring(prefix.Length))
                                .ToList();
            Assert.Equal(count, suffixes.Count);
            for (int i = 0; i < count; i++)
                Assert.Equal(i.ToString("000"), suffixes[i]);
        }
    }

    /// <summary>The search room's authored props, as node paths relative to the room root, in the
    /// order <c>AdoptAuthoredProps</c> would adopt them — which is <c>string.CompareOrdinal</c>
    /// over the path, the same comparison that method uses.</summary>
    private static IReadOnlyList<string> AuthoredPropPaths(string scenePath)
    {
        string text = File.ReadAllText(RepoFile(scenePath));

        // A [node] header, then the lines belonging to it until the next blank-line-separated
        // header. A prop is a node whose block carries the NetworkedProp script; the Body child
        // that instances the prefab is NOT one and must not be counted.
        var header = new Regex("^\\[node name=\"(?<name>[^\"]+)\"(?<rest>[^\\]]*)\\]",
            RegexOptions.Multiline);
        var parentOf = new Regex("parent=\"(?<parent>[^\"]*)\"");

        var found = new List<string>();
        MatchCollection matches = header.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            int blockStart = matches[i].Index;
            int blockEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            string block = text.Substring(blockStart, blockEnd - blockStart);
            if (!block.Contains("script = ExtResource(\"2_netprop\")", StringComparison.Ordinal))
                continue;
            Match p = parentOf.Match(matches[i].Groups["rest"].Value);
            string parent = p.Success ? p.Groups["parent"].Value : "";
            string name = matches[i].Groups["name"].Value;
            found.Add(parent is "" or "." ? name : parent + "/" + name);
        }

        found.Sort(string.CompareOrdinal);
        return found;
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"no file at {path}");
        return path;
    }
}
