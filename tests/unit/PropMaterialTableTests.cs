using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PHYS-1 (2026-09-20), ruling P4: <b>cans roll, boxes topple, and the table that makes that true
/// is AUTHORED.</b>
///
/// <para>Talon: <i>"if they're placing an object on a shelf and accidentally hit a bunch of boxes,
/// those boxes should fall over like dominoes, and cans should roll around."</i> That sentence is
/// a set of ORDERINGS between two prefabs and four resources, and an ordering is exactly the kind
/// of claim that survives a diff review and then quietly inverts — somebody damps a can to stop it
/// leaving the aisle, and "cans roll around" is gone with no test anywhere going red.</para>
///
/// <para><b>So the relations are asserted, not the values.</b> A can's angular damping must be
/// lower than a box's; a can's friction must be the lowest of the four; a box's must be higher
/// than a can's; the box must be the one with a raised centre of mass. Those hold whatever
/// numbers a later ride talks Talon into, and they are what the sentence actually means.</para>
///
/// <para>A hand parse of the scene and resource TEXT rather than a load, for
/// <c>DealPrefabTests</c>' reason: a <c>PackedScene</c> and a <c>PhysicsMaterial</c> are both
/// <c>Resource</c>s and need the native runtime this suite does not have.</para>
/// </summary>
public class PropMaterialTableTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Prop(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "scenes", "game", "props", file));

    private static string Material(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "assets", "physics", file));

    /// <summary>A root-node scalar, as authored. Reads only the block between the root
    /// <c>[node ...]</c> header and the next section, so a child node's property of the same name
    /// can never be picked up by accident.</summary>
    private static float RootFloat(string scene, string field)
    {
        Match node = Regex.Match(scene, @"^\[node name=""[^""]+"" type=""RigidBody3D""\]$",
            RegexOptions.Multiline);
        Assert.True(node.Success, "no RigidBody3D root node");
        string rest = scene[node.Index..];
        int next = rest.IndexOf("\n[node ", StringComparison.Ordinal);
        string block = next >= 0 ? rest[..next] : rest;
        Match m = Regex.Match(block, $@"^{Regex.Escape(field)}\s*=\s*([-\d.eE+]+)$",
            RegexOptions.Multiline);
        Assert.True(m.Success, $"{field} is not authored on the root node");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static bool RootHas(string scene, string field) =>
        Regex.IsMatch(scene, $@"^{Regex.Escape(field)}\s*=", RegexOptions.Multiline);

    private static float MaterialFloat(string tres, string field)
    {
        Match m = Regex.Match(tres, $@"^{Regex.Escape(field)}\s*=\s*([-\d.eE+]+)$",
            RegexOptions.Multiline);
        Assert.True(m.Success, $"{field} is not authored on the material");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    // --- The orderings the sentence means ----------------------------------------------------

    /// <summary><b>"Cans should roll around."</b> A can that damps its spin like a cereal box
    /// turns a third of a revolution and stops; the ordering is the mechanism.</summary>
    [Fact]
    public void ACansAngularDampingIsLowerThanABoxs() =>
        Assert.True(RootFloat(Prop("Can.tscn"), "angular_damp")
            < RootFloat(Prop("CerealBox.tscn"), "angular_damp"));

    /// <summary>And lower than every other carryable's, because the can is the one prop in the
    /// game whose defining behaviour is that it keeps turning.</summary>
    [Theory]
    [InlineData("CerealBox.tscn")]
    [InlineData("Crate.tscn")]
    [InlineData("Produce.tscn")]
    [InlineData("Sphere.tscn")]
    public void ACanSpinsFreerThanAnythingElseInTheGame(string other) =>
        Assert.True(RootFloat(Prop("Can.tscn"), "angular_damp")
            < RootFloat(Prop(other), "angular_damp"));

    /// <summary>Friction is the other half of the roll, and the can holds the low end of the
    /// table against the floor's default 1.0.</summary>
    [Theory]
    [InlineData("cardboard.tres")]
    [InlineData("produce.tres")]
    [InlineData("wood.tres")]
    public void TinIsTheSlipperiestVoiceInTheTable(string other) =>
        Assert.True(MaterialFloat(Material("tin.tres"), "friction")
            < MaterialFloat(Material(other), "friction"));

    /// <summary><b>"Those boxes should fall over like dominoes."</b> A domino needs its base held
    /// while its top is pushed: cardboard's friction is what turns a shove into a topple instead
    /// of a skate, so it has to beat the can's.</summary>
    [Fact]
    public void CardboardGripsHarderThanTin() =>
        Assert.True(MaterialFloat(Material("cardboard.tres"), "friction")
            > MaterialFloat(Material("tin.tres"), "friction"));

    /// <summary><b>The box is the top-heavy one, and it is the ONLY top-heavy one.</b> P4 names
    /// both halves: "a cereal box is top-heavy enough to fall from a nudge, a can is not". A can
    /// that had been given a raised centre of mass would tip instead of rolling, which deletes
    /// the other half of the sentence.</summary>
    [Fact]
    public void OnlyTheCerealBoxIsTopHeavy()
    {
        string box = Prop("CerealBox.tscn");
        Assert.Equal(1f, RootFloat(box, "center_of_mass_mode"));  // 1 = CUSTOM
        Assert.Matches(@"^center_of_mass = Vector3\(0, 0\.0*[1-9][\d.]*, 0\)$",
            Regex.Match(box, @"^center_of_mass = .*$", RegexOptions.Multiline).Value);

        foreach (string other in new[] { "Can.tscn", "Crate.tscn", "Produce.tscn", "Sphere.tscn" })
            Assert.False(RootHas(Prop(other), "center_of_mass_mode"),
                $"{other} authors a centre of mass; only the cereal box may be top-heavy");
    }

    /// <summary><b>Sleep is NOT authored on any prefab</b>, and that is a measurement rather
    /// than a preference: `can_sleep = false` on the can kept every can on every shelf in the
    /// physics server's active set, and the server's at-rest p95 went from 2.712 ms to 23.080 ms
    /// with 162 props nobody had touched. The behaviour it was for -- a can must not be put to
    /// sleep half way down an aisle -- is only true while it is rolling, so
    /// <c>NetworkedProp.WakeFromContactServer</c> suspends sleep for the length of the episode
    /// and <c>Unbind</c> gives it back at the settle.</summary>
    [Fact]
    public void NoPrefabAuthorsSleep()
    {
        foreach (string f in new[]
                 { "Can.tscn", "DealCan.tscn", "CerealBox.tscn", "DealBox.tscn",
                   "Crate.tscn", "Produce.tscn", "DealProduce.tscn", "Sphere.tscn" })
            Assert.False(RootHas(Prop(f), "can_sleep"),
                $"{f} authors can_sleep; sleep is suspended per episode in code, not per prefab");
    }

    /// <summary>Nothing bounces. PHYS-1 gave tin a 0.10 "ping" and PHYS-2 measured it turning
    /// <c>Run-StockTest</c> red three times out of three (a can released at its exact resting
    /// pose read 0.032 m of board penetration against a 0.020 m tolerance; bounce 0.0 alone
    /// cleared it) — see <c>tin.tres</c>'s own note. A bouncy cereal box is a freakout by
    /// another name, and so, it turns out, is a bouncy can in a hole.</summary>
    [Fact]
    public void NothingBounces()
    {
        foreach (string m in new[] { "tin.tres", "cardboard.tres", "produce.tres", "wood.tres" })
            Assert.Equal(0f, MaterialFloat(Material(m), "bounce"));
    }

    // --- Every prop carries one, and it is the one its VOICE says ---------------------------

    public static IEnumerable<object[]> PropsAndTheirMaterials => new[]
    {
        new object[] { "Can.tscn", "tin" },
        new object[] { "DealCan.tscn", "tin" },
        new object[] { "CerealBox.tscn", "cardboard" },
        new object[] { "DealBox.tscn", "cardboard" },
        new object[] { "Produce.tscn", "produce" },
        new object[] { "DealProduce.tscn", "produce" },
        new object[] { "Crate.tscn", "wood" },
        new object[] { "Sphere.tscn", "wood" },
    };

    /// <summary><b>One classification, used twice.</b> A prop's physics material must be the one
    /// its <c>PropMaterial</c> voice already picks for its SOUND (<c>assets/items/</c>), or the
    /// game would be saying a thing is made of tin and behaving as if it were made of cardboard —
    /// two classifications of one object that nothing keeps in step.</summary>
    [Theory]
    [MemberData(nameof(PropsAndTheirMaterials))]
    public void EveryCarryableCarriesItsOwnVoicesPhysicsMaterial(string file, string material)
    {
        string scene = Prop(file);
        Assert.Contains($"res://assets/physics/{material}.tres", scene, StringComparison.Ordinal);
        Assert.Matches(@"^physics_material_override = ExtResource\(""[^""]+""\)$",
            Regex.Match(scene, @"^physics_material_override = .*$", RegexOptions.Multiline).Value);
    }

    /// <summary>Four voices, four files, no fifth. A physics material with no <c>PropMaterial</c>
    /// member to hang it on would be a classification nothing resolves to.</summary>
    [Fact]
    public void TheTableHasExactlyTheFourVoices()
    {
        string[] files = Directory.GetFiles(
            Path.Combine(RepoRoot(), "assets", "physics"), "*.tres");
        Array.Sort(files);
        Assert.Equal(new[] { "cardboard.tres", "produce.tres", "tin.tres", "wood.tres" },
            Array.ConvertAll(files, Path.GetFileName));
    }
}
