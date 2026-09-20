using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>A near-miss object is its ordinary twin plus a band, and nothing else.</b> SHELF-1,
/// 2026-09-19.
///
/// <para>The whole design of the hide (PROPOSAL-2026-09-19-SOUND-STATES §2) is that the hider's
/// choice of object is the choice of WHICH POPULATION IT HIDES IN, and that the seeker tells a
/// deal item from its forty-seven neighbours <b>by the gold band alone</b>. That property is not
/// in any one line of code: it is the claim that <c>DealCan.tscn</c> is <c>Can.tscn</c> with one
/// mesh added. A deal can that were a millimetre bigger, ten grams heavier or a shade shinier
/// would be findable by silhouette, the near-miss would stop being a near-miss, and nothing in
/// the repo would go red.</para>
///
/// <para>So it is asserted from the authored scene TEXT, field by field, against the plain
/// prefab. Three things follow that are worth stating because a reviewer cannot see them in a
/// diff of two files that look alike:</para>
///
/// <list type="bullet">
/// <item>The COLLIDER is identical, which is what keeps
/// <c>Carryable.ShapeFromCollider</c> answering Can/Box/Produce for the deal variants — so they
/// carry their kind's material voice and their kind's wire <c>PropKind</c> with no new code.</item>
/// <item>The MASS is identical, so a deal object lags in the hand exactly like its neighbours
/// (<c>Interactable.HeftKg</c> reads the body's mass).</item>
/// <item>The BODY MATERIAL is identical, so the near-miss is by band and not by shade.</item>
/// </list>
///
/// <para>A hand parse rather than a scene load, for <c>MaterialSfxTests</c>' reason: a
/// <c>PackedScene</c> is a <c>Resource</c> and needs the native runtime this suite does not
/// have.</para>
/// </summary>
public class DealPrefabTests
{
    public static IEnumerable<object[]> Pairs => new[]
    {
        new object[] { "Can.tscn", "DealCan.tscn", "CylinderShape3D", "CylinderMesh" },
        new object[] { "CerealBox.tscn", "DealBox.tscn", "BoxShape3D", "BoxMesh" },
        new object[] { "Produce.tscn", "DealProduce.tscn", "SphereShape3D", "TorusMesh" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ADealPrefabIsItsOrdinaryTwinPlusOneBand(
        string plainFile, string dealFile, string colliderType, string bandMeshType)
    {
        string plain = ReadProp(plainFile);
        string deal = ReadProp(dealFile);

        // The collider, to the digit. Same sub-resource type and same body text.
        Assert.Equal(SubResource(plain, colliderType), SubResource(deal, colliderType));

        // The mass, and the damping a prefab sets (Produce.tscn damps itself so it stops rolling;
        // a deal orange that rolled further than an ordinary one would be findable by watching it).
        foreach (string field in new[] { "mass", "linear_damp", "angular_damp" })
            Assert.Equal(RootField(plain, field), RootField(deal, field));

        // The body's own material: every albedo/roughness/metallic/emission line, in order.
        Assert.Equal(BodyMaterialLines(plain), BodyMaterialLines(deal));

        // Exactly ONE added mesh, it is the band, and it is NOT the node Carryable binds.
        Assert.DoesNotContain("DealBand", plain, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(deal, @"^\[node name=""DealBand"" type=""MeshInstance3D"" parent=""Visual""\]",
            RegexOptions.Multiline));
        Assert.Contains($"[sub_resource type=\"{bandMeshType}\" id=", deal, StringComparison.Ordinal);

        // The root node type, because BTN-1's rack instances these at the placeholder paths and
        // a changed root type would break its instance lines silently.
        Assert.Equal(RootType(plain), RootType(deal));
        Assert.Equal("RigidBody3D", RootType(deal));

        // NO RENDER-LAYER LINE ANYWHERE. Layer 19 is FP-1's first-person self-cull and a prop on
        // it vanishes from the hands of whoever is carrying it, for that player only. SFX-1 wrote
        // the absence into all three plain prefabs as deliberate; this keeps it true for the
        // three that a player is guaranteed to be carrying.
        Assert.DoesNotContain("layers =", deal, StringComparison.Ordinal);
    }

    /// <summary>The three deal prefabs share one gold, by value rather than by a .tres, so a
    /// reader of any one file sees the whole object. That is only safe while they agree.</summary>
    [Fact]
    public void AllThreeBandsAreTheSameGold()
    {
        string[] golds = new[] { "DealCan.tscn", "DealBox.tscn", "DealProduce.tscn" }
            .Select(f => SubResourceById(ReadProp(f), "StandardMaterial3D_gold"))
            .ToArray();
        Assert.Equal(golds[0], golds[1]);
        Assert.Equal(golds[0], golds[2]);
        Assert.Contains("emission_energy_multiplier = 0.3", golds[0], StringComparison.Ordinal);
    }

    // --- the parse, deliberately narrow -----------------------------------------------------

    private static string ReadProp(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, "scenes", "game", "props", file);
        Assert.True(File.Exists(path), $"no prefab at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The body of the first <c>[sub_resource type="X" …]</c> block, comments and blank
    /// lines stripped, so two files that differ only in prose compare equal.</summary>
    private static string SubResource(string text, string type) =>
        Block(text, $"[sub_resource type=\"{type}\"");

    private static string SubResourceById(string text, string id) =>
        Block(text, $"id=\"{id}\"]");

    private static string Block(string text, string needle)
    {
        int start = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no block matching '{needle}'");
        start = text.IndexOf('\n', start) + 1;
        int end = text.IndexOf("\n[", start, StringComparison.Ordinal);
        if (end < 0) end = text.Length;
        return Clean(text.Substring(start, end - start));
    }

    /// <summary>A field on the ROOT node's block (the first <c>[node …]</c>), or "" if absent.
    /// "" is a meaningful answer — "neither prefab sets damping" is as much an agreement as
    /// "both set 2.0".</summary>
    private static string RootField(string text, string field)
    {
        int start = text.IndexOf("\n[node ", StringComparison.Ordinal);
        Assert.True(start >= 0, "no [node] block");
        int end = text.IndexOf("\n[node ", start + 1, StringComparison.Ordinal);
        if (end < 0) end = text.Length;
        Match m = Regex.Match(text.Substring(start, end - start),
            $@"^{Regex.Escape(field)} = (?<v>.+)$", RegexOptions.Multiline);
        return m.Success ? m.Groups["v"].Value.Trim() : "";
    }

    private static string RootType(string text) =>
        Regex.Match(text, @"^\[node name=""[^""]+"" type=""(?<t>[^""]+)""\]", RegexOptions.Multiline)
             .Groups["t"].Value;

    /// <summary>Every visual line of the body's own StandardMaterial3D — the one whose id ends
    /// in something other than <c>_gold</c>. Order-sensitive on purpose.</summary>
    private static string BodyMaterialLines(string text)
    {
        MatchCollection blocks = Regex.Matches(text,
            @"^\[sub_resource type=""StandardMaterial3D"" id=""(?<id>[^""]+)""\]", RegexOptions.Multiline);
        Match body = blocks.Cast<Match>().First(m => !m.Groups["id"].Value.EndsWith("_gold", StringComparison.Ordinal));
        return Block(text, $"id=\"{body.Groups["id"].Value}\"]");
    }

    private static string Clean(string s) => string.Join("\n", s
        .Split('\n')
        .Select(l => l.TrimEnd('\r'))
        .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith(";", StringComparison.Ordinal)));
}
