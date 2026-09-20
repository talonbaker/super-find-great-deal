using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MpFoundation.Game.World.Stock;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The drift gate: the committed scene files must be exactly what the arithmetic produces.</b>
///
/// <para>This is the test that makes a BAKED level honest. <c>.claude/rules/godot-scenes.md</c>
/// requires the shop floor to be authored in its scene file rather than built in <c>_Ready</c>;
/// authoring 2 700 instances by hand is unreviewable; so the instances are generated from
/// <see cref="ShelfStock"/> and committed. <b>Without this test that trade would be a loss</b> —
/// the generator's source would be decoration and the real level would be thirty thousand floats
/// nobody could check. With it, the committed file is a pure function of eighty lines of
/// arithmetic, a reviewer reads the arithmetic, and a hand edit to the generated file is a red
/// with a diff rather than a mystery a year later.</para>
///
/// <para><b>To regenerate:</b> <c>SFGD_BAKE_STOCK=1 dotnet test tests/unit/SailNet.Tests.csproj</c>.
/// That mode WRITES the files and passes; every other run COMPARES.</para>
/// </summary>
public class StockBakeTests
{
    private static string Root => StockBake.FindRepoRoot();

    private static bool Baking =>
        Environment.GetEnvironmentVariable(StockBake.BakeEnvVar) == "1";

    /// <summary>
    /// The one gate. In bake mode it writes; otherwise it compares, and the failure names the
    /// command that fixes it.
    /// </summary>
    [Fact]
    public void TheBakedSceneFilesAreExactlyWhatTheArithmeticProduces()
    {
        List<RoomBay> bays = StockBake.ReadBays(Root);
        RoomStock stock = StockRoom.Assemble(bays, StockBake.ReadFacings(Root));

        foreach ((string rel, string want) in new[]
                 {
                     (StockBake.BulkScenePath, StockBake.EmitBulkScene(stock)),
                     (StockBake.MoundScenePath, StockBake.EmitMoundScene()),
                 })
        {
            string path = Path.Combine(Root, rel);
            if (Baking)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, want);
                continue;
            }

            Assert.True(File.Exists(path), $"{rel} is missing. Regenerate it: "
                                           + $"{StockBake.BakeEnvVar}=1 dotnet test tests/unit/SailNet.Tests.csproj");
            string got = File.ReadAllText(path).Replace("\r\n", "\n");
            if (got == want)
                continue;

            // Say WHERE, not just "they differ" — the file is tens of thousands of floats.
            string[] a = got.Split('\n'), b = want.Split('\n');
            int line = 0;
            while (line < a.Length && line < b.Length && a[line] == b[line])
                line++;
            string near = line < a.Length ? a[line] : "(end of file)";
            if (near.Length > 160)
                near = near.Substring(0, 160) + "...";
            Assert.Fail($"{rel} is not what ShelfStock/StockRoom produce: they first differ at line "
                        + $"{line + 1} ({a.Length} lines committed, {b.Length} derived).\n"
                        + $"  committed: {near}\n"
                        + $"  Either the layout maths changed and the bake was not re-run, or the "
                        + $"generated file was hand-edited. Regenerate with "
                        + $"{StockBake.BakeEnvVar}=1 dotnet test tests/unit/SailNet.Tests.csproj");
        }
    }

    /// <summary>
    /// <c>.claude/rules/godot-scenes.md</c>: <i>"MultiMesh buffer stride is not always 12. A layer
    /// carrying per-instance colour is stride 16; parsing with the wrong stride yields plausible
    /// garbage, not a crash."</i> These carry no colour and no custom data, so the stride IS 12 —
    /// asserted rather than assumed, because "plausible garbage" is exactly what a shelf of
    /// products rotated into the floor would look like in a capture nobody took.
    /// </summary>
    [Fact]
    public void EveryMultiMeshBufferIsTwelveFloatsPerInstanceAndNoMore()
    {
        foreach (string rel in new[] { StockBake.BulkScenePath, StockBake.MoundScenePath })
        {
            string text = File.ReadAllText(Path.Combine(Root, rel));
            Assert.DoesNotContain("use_colors", text);
            Assert.DoesNotContain("use_custom_data", text);
            Assert.Contains("transform_format = 1", text);

            MatchCollection counts = Regex.Matches(text, @"^instance_count = (\d+)$", RegexOptions.Multiline);
            MatchCollection buffers = Regex.Matches(text, @"^buffer = PackedFloat32Array\((?<f>.*)\)$",
                RegexOptions.Multiline);
            Assert.Equal(counts.Count, buffers.Count);
            Assert.NotEmpty(counts);

            for (int i = 0; i < counts.Count; i++)
            {
                int n = int.Parse(counts[i].Groups[1].Value, CultureInfo.InvariantCulture);
                string body = buffers[i].Groups["f"].Value;
                int floats = body.Length == 0 ? 0 : body.Split(',').Length;
                Assert.True(n > 0, $"{rel}: a MultiMesh with {n} instances is a node that draws nothing");
                Assert.Equal(n * 12, floats);
            }
        }
    }

    /// <summary>
    /// The instance budget, stated as a range rather than a number so a re-seed does not fail the
    /// suite, and stated at all because it is the whole subject of the packet.
    ///
    /// <para><b>Both ends matter.</b> The floor is Talon's ask — a shelf with a few hundred things
    /// on it is the sparse room he asked to be rid of. The ceiling is the client frame-time bar:
    /// this is what the p95 measurement in the handoff was taken at, and a bake that quietly
    /// doubled it would invalidate that measurement without failing anything.</para>
    /// </summary>
    [Fact]
    public void TheInstanceCountIsInsideTheBandTheFrameTimeWasMeasuredAt()
    {
        RoomStock stock = StockRoom.Assemble(StockBake.ReadBays(Root), StockBake.ReadFacings(Root));
        int total = stock.Instances.Count;

        Assert.InRange(total, 1500, 3200);
        Assert.InRange(stock.CountOf(StockMaterial.Can), 1000, 2000);
        Assert.InRange(stock.CountOf(StockMaterial.Box), 400, 1200);
        Assert.InRange(stock.CountOf(StockMaterial.Produce), 20, 200);

        // Three MultiMesh draws for the room plus one inside the bin prefab. Whatever else changes,
        // the DRAW count must not start scaling with the bay count.
        string bulk = File.ReadAllText(Path.Combine(Root, StockBake.BulkScenePath));
        Assert.Equal(3, Regex.Matches(bulk, @"type=""MultiMeshInstance3D""").Count);
    }

    /// <summary>
    /// The room's own arithmetic, read back out of <c>SearchRoom.tscn</c> rather than tabulated
    /// here: sixteen aisle bays and two end-caps, the aisles alternating cans and cereal boxes so
    /// SFX-1's per-material voice still tells a player which aisle a noise came from.
    /// </summary>
    [Fact]
    public void TheRoomStillHasSixteenBaysAndTwoEndCapsAndTheMaterialsStillAlternate()
    {
        List<RoomBay> bays = StockBake.ReadBays(Root);
        Assert.Equal(16, bays.Count(b => !b.IsEndCap));
        Assert.Equal(2, bays.Count(b => b.IsEndCap));

        foreach (RoomBay b in bays.Where(b => !b.IsEndCap))
        {
            int aisle = b.Name["Aisle".Length] - '0';
            StockMaterial want = aisle % 2 == 0 ? StockMaterial.Can : StockMaterial.Box;
            Assert.Equal(want, StockRoom.MaterialFor(b));
        }
        // End-caps carry PRODUCE: SHELF-1 authored twelve carryable oranges onto them, and the
        // bulk behind a facing must be the same product as the facing. See StockRoom.MaterialFor.
        foreach (RoomBay b in bays.Where(b => b.IsEndCap))
            Assert.Equal(StockMaterial.Produce, StockRoom.MaterialFor(b));

        // The end-caps are instanced rotated 90 degrees about Y, so their 1.4 m length runs along
        // the room's z. If that stops being true the bulk inside them is rotated into the aisle.
        foreach (RoomBay b in bays.Where(b => b.IsEndCap))
            Assert.Equal(90f, MathF.Abs(b.YawDeg), 1);
    }

    /// <summary>
    /// <b>Nothing new stands where a player, a bot or another lane's fixture already does.</b>
    ///
    /// <para>SHELF-1 §6 paid for this twice in one session — an end-cap on a suite bot's walking
    /// diagonal, and three material-SFX fixtures authored inside a bay — and wrote the rule down:
    /// <i>"a walk brain that goes in a straight line makes every piece of furniture in the room
    /// part of somebody else's staging."</i> STOCK-1 adds exactly two free-standing objects to
    /// the floor, and this is the check that they are clear of every fixture the room already
    /// has. HOLD-1's 1.6 m clear radius around the drop-off bin is the tightest of them.</para>
    /// </summary>
    [Fact]
    public void TheFloorPalletsAreClearOfEveryFixtureTheRoomAlreadyHas()
    {
        // Read the fixtures out of the room rather than typing them, for the same reason the bays
        // are read: a fixture that moves must move this check with it.
        string tscn = File.ReadAllText(Path.Combine(Root, StockBake.SearchRoomPath));
        var fixtures = new List<(string Name, float X, float Z, float Clear)>();

        foreach (Match m in Regex.Matches(tscn,
                     @"^\[node name=""(?<n>SearchSpawn_\d|SearchPillar|DropOffBin|Bin_\d)""[^\n]*\n"
                     + @"transform = Transform3D\([^)]*?,\s*(?<x>-?[\d.]+),\s*(?<y>-?[\d.]+),\s*(?<z>-?[\d.]+)\)",
                     RegexOptions.Multiline))
        {
            string n = m.Groups["n"].Value;
            // The drop-off bin keeps HOLD-1's 1.6 m clear radius; everything else needs only
            // enough room not to be touched by a 0.63 x 0.49 m pallet.
            float clear = n == "DropOffBin" ? 1.6f : 1.0f;
            fixtures.Add((n, float.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups["z"].Value, CultureInfo.InvariantCulture), clear));
        }
        Assert.True(fixtures.Count >= 10,
            $"only found {fixtures.Count} fixtures in SearchRoom.tscn; the parse has gone stale");

        float palletHalfX = StockRoom.PalletAcross * ShelfStock.XPitchOf(StockMaterial.Box) * 0.5f;
        float palletHalfZ = StockRoom.PalletDeep * (ShelfStock.Box.DepthM + 0.01f) * 0.5f;
        float palletRadius = MathF.Sqrt(palletHalfX * palletHalfX + palletHalfZ * palletHalfZ);

        foreach ((string name, float px, float pz) in StockRoom.Pallets)
        {
            // Inside the room, clear of the wall.
            Assert.InRange(MathF.Abs(px) + palletHalfX, 0f, 7f);
            Assert.InRange(MathF.Abs(pz) + palletHalfZ, 0f, 5f);
            // Out of the aisles entirely: the bays stop at x = +/-4.6.
            Assert.True(MathF.Abs(px) - palletHalfX > 4.6f,
                $"{name} at x={px} reaches into the aisles");

            foreach ((string fn, float fx, float fz, float clear) in fixtures)
            {
                float d = MathF.Sqrt((px - fx) * (px - fx) + (pz - fz) * (pz - fz));
                Assert.True(d > palletRadius + clear,
                    $"{name} at ({px}, {pz}) is {d:0.00} m from {fn} at ({fx}, {fz}); it needs "
                    + $"{palletRadius + clear:0.00} m. SHELF-1 §6: every piece of furniture in this "
                    + "room is part of somebody else's staging.");
            }
        }
    }

    /// <summary>
    /// <b>No bulk instance stands where one of SHELF-1's 120 carryable products stands.</b>
    ///
    /// <para>This is the one that would have broken the room. The 120 authored products are
    /// networked <c>RigidBody3D</c> on these same boards; static filler laid over one puts a
    /// networked prop permanently inside static geometry, so every rest audit reports
    /// <c>StaticOverlap</c>, REACH-1's layer 3 answers <c>InsideStatic</c>, and a hider who chose
    /// any of those props is refused the Confirm with <c>NobodyCouldReachThat</c>. It would look
    /// exactly like a room that renders perfectly.</para>
    /// </summary>
    [Fact]
    public void NoBulkInstanceStandsWhereACarryableFacingStands()
    {
        List<StockRoom.RoomFacing> facings = StockBake.ReadFacings(Root);
        Assert.Equal(120, facings.Count);

        RoomStock stock = StockRoom.Assemble(StockBake.ReadBays(Root), facings);
        float worst = float.MaxValue;
        string where = "(none)";

        foreach (StockRoom.RoomFacing f in facings)
            foreach (RoomInstance i in stock.Instances)
            {
                // Only the facing's own shelf can touch it; 0.30 m is under the 0.55 m board pitch.
                if (MathF.Abs(i.Y - f.Y) > 0.30f)
                    continue;
                ProductSize a = ShelfStock.SizeOf(i.Material);
                float clear = MathF.Max(MathF.Abs(i.X - f.X) - a.WidthM,
                                        MathF.Abs(i.Z - f.Z) - a.DepthM);
                if (clear < worst)
                {
                    worst = clear;
                    where = $"{f.Name} at ({f.X:0.00}, {f.Y:0.00}, {f.Z:0.00}) vs bulk "
                            + $"{i.Material} at ({i.X:0.00}, {i.Y:0.00}, {i.Z:0.00})";
                }
            }

        Assert.True(worst > 0f,
            $"bulk overlaps an authored carryable by {-worst:0.000} m: {where}. That prop would "
            + "be inside static geometry for the whole round, every rest audit would report "
            + "StaticOverlap, and a hider who chose it would be refused the Confirm.");
    }

    /// <summary>
    /// The generated files say they are generated, in the first screen, with the command that
    /// regenerates them. A file this size with no banner is one somebody will edit by hand.
    /// </summary>
    [Fact]
    public void EveryGeneratedFileSaysSoAndSaysHowToRegenerateItself()
    {
        foreach (string rel in new[] { StockBake.BulkScenePath, StockBake.MoundScenePath })
        {
            string head = string.Join("\n",
                File.ReadAllLines(Path.Combine(Root, rel)).Take(40));
            Assert.Contains("GENERATED FILE: DO NOT EDIT BY HAND", head);
            Assert.Contains(StockBake.BakeEnvVar, head);
        }
    }
}
