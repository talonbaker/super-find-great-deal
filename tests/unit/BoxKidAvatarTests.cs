using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The box body, the world that asks for it, and the random colour (BT-7).</b>
///
/// <para>Three claims, none of which any existing suite would catch going wrong: that
/// <c>BoxKid.glb</c> carries the rig the runtime harvests and stands at the pinned crown; that the
/// world→body table changes what the bubble test gets and nothing else; and that the random deal
/// is reproducible from its seed and does not dedup.</para>
///
/// <para><b>The asset half reads the shipped bytes rather than asking Godot</b>, the same choice
/// (and most of the same parser) as <c>GreyboxAssetContractTests</c>: glTF stores every accessor's
/// own <c>min</c>/<c>max</c>, so the exact bounds of every part are available with no import, no
/// GPU and no display. The engine-side answer is the headed capture in
/// <c>docs/qa/bubble-test/BT-7/</c>; this is deliberately the other kind of evidence.</para>
/// </summary>
public class BoxKidAvatarTests
{
    /// <summary>Half a centimetre, the tolerance <c>GreyboxAssetContractTests</c> sets on the same
    /// two heights and for the same reason: these are authored to a round number, so anything
    /// outside it is an edit rather than noise.</summary>
    private const float Tol = 0.005f;

    /// <summary>Every part the harvest and <c>BuildAuthoredRig</c> look up by name. The eight the
    /// body legitimately lacks are declared on the roster row (<c>AvatarVisual.BoxKidAbsentParts</c>)
    /// and are deliberately not listed.</summary>
    private static readonly string[] RequiredParts =
    {
        "Hips", "Torso", "Head", "Mouth", "EyeL", "EyeR",
        "ArmL", "ArmR", "ForearmL", "ForearmR",
        "ThighL", "ThighR", "ShinL", "ShinR", "FootL", "FootR",
    };

    /// <summary>The four empties <c>BuildAuthoredRig</c> requires of a <c>Build.AuthoredRig</c>
    /// row. Without Pose and Body it pushes an error and falls back to the built pose; without
    /// Waist the body cannot bend.</summary>
    private static readonly string[] RequiredJoints = { "Rig", "Pose", "Body", "Waist" };

    // --- The asset ---------------------------------------------------------------------------

    [Fact]
    public void BoxKid_CarriesEveryContractPartAndEveryRigJoint()
    {
        GltfBody body = LoadBoxKid();
        var missing = RequiredParts.Concat(RequiredJoints)
            .Where(n => !body.Nodes.Contains(n)).ToList();
        Assert.True(missing.Count == 0,
            $"BoxKid.glb is missing: {string.Join(", ", missing)}. " +
            $"It carries: {string.Join(", ", body.Nodes.OrderBy(n => n))}");
    }

    /// <summary>The two heights the rest of the game derives from. They are the greybox's, and
    /// they have to be: <c>AvatarProportions</c> sizes the collision capsule off the measured
    /// crown and takes the aim ray's origin from the eye centre, so a box body that landed 10 cm
    /// short would change how a player collides and where their swing sweeps — invisibly.</summary>
    [Fact]
    public void BoxKid_StandsOnTheGroundAtThePinnedCrownAndEyeline()
    {
        GltfBody body = LoadBoxKid();
        Assert.True(Math.Abs(body.Bounds.MaxY - 1.20f) < Tol,
            $"BoxKid.glb's crown is {body.Bounds.MaxY:F4} m, not 1.200 m");
        Assert.True(Math.Abs(body.Bounds.MinY) < Tol,
            $"BoxKid.glb's lowest vertex is {body.Bounds.MinY:F4} m, not on the ground");

        (float lo, float hi) = body.YSpanOf("EyeL");
        float eye = 0.5f * (lo + hi);
        Assert.True(Math.Abs(eye - 1.04f) < Tol,
            $"BoxKid.glb's eye centre is {eye:F4} m, not 1.040 m");
    }

    /// <summary>
    /// <b>The Y-up trap, caught in the bytes.</b> Blender's glTF exporter has shipped an axis bug
    /// in this repo's history, and its signature is a body lying on its side: the standing memory
    /// says a headed capture is the proof, and it is — this is the cheap check that runs first.
    /// A figure 1.20 m tall and roughly 0.64 x 0.35 m in plan is upright; the same numbers
    /// permuted are not.
    /// </summary>
    [Fact]
    public void BoxKid_IsUprightRatherThanLyingDown()
    {
        GltfBody body = LoadBoxKid();
        float height = body.Bounds.MaxY - body.Bounds.MinY;
        float width = body.Bounds.MaxX - body.Bounds.MinX;
        float depth = body.Bounds.MaxZ - body.Bounds.MinZ;
        Assert.True(height > width && height > depth,
            $"BoxKid.glb is {width:F3} x {height:F3} x {depth:F3} (w x h x d) — its tallest axis " +
            "is not Y, which is what a mis-exported Y-up asset looks like");
    }

    /// <summary>The rig is rigid nodes, not a skin — <c>AvatarVisual</c> writes node transforms
    /// and cannot drive a skeleton — and it ships the clip library, without which
    /// <c>AvatarClipDirector.TryBuild</c> pushes an error and the body falls back to the
    /// procedural gait.</summary>
    [Fact]
    public void BoxKid_ShipsRigidWithTheClipLibrary()
    {
        JsonElement gltf = LoadBoxKidJson();
        Assert.False(gltf.TryGetProperty("skins", out _),
            "BoxKid.glb ships a skin; the rig animates node transforms and cannot drive one");
        Assert.True(gltf.TryGetProperty("animations", out JsonElement animations)
            && animations.GetArrayLength() > 0,
            "BoxKid.glb ships no animations; AvatarClipDirector refuses a model without them");
    }

    // BODY-1 (2026-08-28) removed BoxKid_AndGreybox_ShipTheSameClipNames. It compared this file's
    // clip names against Greybox.glb's; Greybox.glb is gone, so the comparison had exactly one
    // file left in it and would have passed against itself forever. The coverage did not move —
    // ClipTimeWarpTests asserts BOTH directions against the code that plays the clips, which is
    // the stronger claim of the two: the file agreeing with a deleted sibling never proved the
    // clip library could actually find a clip.

    // --- The world table ----------------------------------------------------------------------

    /// <summary>The bubble test gets the box body. Everything else — including a world id nobody
    /// registered — gets exactly what it got before this packet.</summary>
    [Theory]
    [InlineData("camp")]
    [InlineData("playground")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a-world-that-does-not-exist")]
    public void EveryOtherWorldKeepsTodaysPreferredKey(string? worldId)
    {
        Assert.Equal(AvatarVisual.PreferredAvatarKey, AvatarVisual.PreferredAvatarKeyFor(worldId));
    }

    /// <summary><b>This test has now been inverted twice, and the second inversion is the point of
    /// keeping the history in the comment.</b> BT-7 asserted the bubble test prefers the box body.
    /// BODY-1 inverted it: Talon asked for the oldest grey box, so the assertion became that the
    /// bubble test must NOT hand out the box kid. BODY-2 (2026-08-28) inverts it back — Talon:
    /// <i>"The BoxKid.glb is the one I want."</i>
    ///
    /// <para>What survives all three revisions, and is the only thing actually worth pinning here,
    /// is the STRUCTURAL claim rather than the identity: the bubble test's answer is the same as
    /// every other world's. There is one default body, not a per-world one, and the day that stops
    /// being true it must be because someone decided it and not because a branch drifted.</para>
    /// </summary>
    [Fact]
    public void TheBubbleTestGetsTheSameBodyAsEveryOtherWorld()
    {
        Assert.Equal(AvatarVisual.BoxKidAvatarKey,
            AvatarVisual.PreferredAvatarKeyFor(AvatarVisual.BoxKidWorldId));
        Assert.Equal(AvatarVisual.PreferredAvatarKeyFor("camp"),
            AvatarVisual.PreferredAvatarKeyFor(AvatarVisual.BoxKidWorldId));
    }

    /// <summary><b>The live bug BODY-1 fixed, pinned so it cannot come back.</b> The pickers
    /// (<c>HostMenu</c>, <c>JoinMenu</c>) initialise <c>_avatarIndex = 0</c> and write
    /// <c>RosterEntries[0].Key</c> into <c>NetworkManager.LocalAvatarKey</c> whether or not the
    /// player ever touched the picker, and a non-empty <c>LocalAvatarKey</c> BEATS
    /// <see cref="AvatarVisual.PreferredAvatarKeyFor"/> in <c>SandboxAvatar</c>. So the two must
    /// name the same body or the menus silently override the world — which is how Talon played a
    /// whole session on the gumdrop after the world's preferred key had already been
    /// repointed.
    ///
    /// <para><b>AVATAR-1 (2026-09-05): the picker is gone from the shipped menus, and this test
    /// stays.</b> Talon: <i>"I don't want players to be able to change for this initial build
    /// because of the movements don't line up right."</i> Neither menu writes
    /// <c>LocalAvatarKey</c> any more — <c>OneBodyForTheFirstBuildTests</c> is what pins that
    /// absence — so the override described above cannot currently happen. What this test now
    /// guards is the RESTORE: a picker that comes back must come back on index 0 = the default
    /// body, or BODY-1's bug comes back with it. A dormant invariant deleted is an invariant that
    /// has to be rediscovered the hard way.</para></summary>
    [Fact]
    public void ThePickersDefaultRowIsTheDefaultBody()
    {
        // _avatarIndex's own default, spelled out rather than referenced: the field is private and
        // the point of the test is that its VALUE must agree with the roster, not that some symbol
        // matches itself.
        const int PickerStartIndex = 0;
        Assert.Equal(AvatarVisual.PreferredAvatarKey, AvatarVisual.RosterEntries[PickerStartIndex].Key);
        foreach (string? world in new[] { null, "", "camp", "playground", AvatarVisual.BoxKidWorldId })
            Assert.Equal(AvatarVisual.RosterEntries[PickerStartIndex].Key,
                AvatarVisual.PreferredAvatarKeyFor(world));
    }

    /// <summary><b>The retired gumdrop's wire value is aliased forward, not clamped</b> (BODY-1,
    /// scope item 3). A peer still sending <c>"greybox"</c> is an older build or a stale launcher
    /// line, not a hostile one, and it means "I am the greybox player body" — which the classic
    /// now is. The clamp path is untouched for everything else, which is the other half of the
    /// claim and why the bogus key is asserted here beside it.</summary>
    [Fact]
    public void TheRetiredGumdropKeyNormalisesToTheClassicAndNotToTheClamp()
    {
        Assert.Equal(AvatarVisual.ClassicGreyboxAvatarKey,
            AvatarVisual.NormalizeAvatarKey(AvatarVisual.RetiredGumdropAvatarKey));
        Assert.Equal(AvatarVisual.ClassicGreyboxAvatarKey,
            AvatarVisual.NormalizeAvatarKey("  GreyBox  "));
        // An alias, not a resurrection: the row is gone and nothing may index the roster with it.
        Assert.False(AvatarVisual.IsValidAvatarKey(AvatarVisual.RetiredGumdropAvatarKey));
        // ...and the containment path it did NOT weaken.
        Assert.Equal(AvatarVisual.DefaultAvatarKey,
            AvatarVisual.NormalizeAvatarKey("totally-bogus-not-a-roster-entry"));
    }

    /// <summary>The gumdrop is gone from disk, and this is the assertion that keeps it gone —
    /// scope item 2. A roster row pointing at a deleted file fails loudly at load; a roster row
    /// deleted while the file lingers is the dead asset the packet forbids.</summary>
    [Fact]
    public void TheGumdropAssetIsNotOnDisk()
    {
        Assert.False(File.Exists(
            Path.Combine(FindRepoRoot(), "assets", "creatures", "greybox", "Greybox.glb")));
        Assert.False(File.Exists(
            Path.Combine(FindRepoRoot(), "assets", "creatures", "greybox", "Greybox.glb.import")));
    }

    /// <summary>The key rides the wire (<c>SandboxAvatar.AvatarKey</c>), so it must be a real
    /// roster entry or a joining peer's body clamps to the containment default.</summary>
    [Fact]
    public void TheBoxKeyIsAValidRosterEntryOnThePicker()
    {
        Assert.True(AvatarVisual.IsValidAvatarKey(AvatarVisual.BoxKidAvatarKey));
        Assert.True(AvatarVisual.IsAuthoredRig(AvatarVisual.BoxKidAvatarKey));
        Assert.Equal(AvatarVisual.BoxKidModelPath,
            AvatarVisual.DeclaredModelPathFor(AvatarVisual.BoxKidAvatarKey));
        Assert.Contains(AvatarVisual.RosterEntries, e => e.Key == AvatarVisual.BoxKidAvatarKey);
    }

    /// <summary><b>BODY-2 (2026-08-28): the box kid IS the played body now</b> — Talon,
    /// <i>"The BoxKid.glb is the one I want."</i> — so the two roles it takes are asserted here,
    /// and so are the two it deliberately still does NOT take.
    ///
    /// <para>The separation is the load-bearing half. <c>PreferredAvatarKey</c> is "what you are if
    /// you never picked"; <c>PrimitiveFallbackAvatarKey</c> is "what you get when the file did not
    /// load"; <c>DefaultAvatarKey</c> is "what a value that arrived WRONG clamps to". Three
    /// different questions. If the box kid became the load-failure fallback too, a corrupt
    /// <c>BoxKid.glb</c> would fall back to itself and the failure would be silent; if it became
    /// the clamp target, a hostile or stale key would stop being distinguishable from a default,
    /// which is what <c>Run-AvatarIdentityTest</c> exists to pin.</para></summary>
    [Fact]
    public void TheBoxBodyIsTheDefaultAndNeitherFallback()
    {
        Assert.Equal(AvatarVisual.BoxKidAvatarKey, AvatarVisual.PreferredAvatarKey);
        Assert.Equal(AvatarVisual.BoxKidAvatarKey, AvatarVisual.RosterEntries[0].Key);
        Assert.NotEqual(AvatarVisual.BoxKidAvatarKey, AvatarVisual.PrimitiveFallbackAvatarKey);
        Assert.NotEqual(AvatarVisual.BoxKidAvatarKey, AvatarVisual.DefaultAvatarKey);
        // The default body is a FILE again (BODY-2 vs BODY-1), and that is the half of Talon's
        // standing "no assets I cannot open" ruling that BODY-1 had to leave in tension.
        Assert.True(AvatarVisual.IsAuthoredRig(AvatarVisual.PreferredAvatarKey));
        Assert.False(AvatarVisual.IsCodeBuilt(AvatarVisual.PreferredAvatarKey));
        Assert.EndsWith(".glb", AvatarVisual.DeclaredModelPathFor(AvatarVisual.PreferredAvatarKey));
    }

    /// <summary><b>The classic greybox survives BODY-2 — it moved off index 0, it was not
    /// deleted.</b> Scope item 1 says so in as many words, and there are two reasons beyond the
    /// instruction: it is the last code-built player-shaped body, so it is what the suite's
    /// "a failed model load falls back to something with limbs" claims are measured against, and
    /// it is the only surviving control for a value-ramp or silhouette claim about the box kid.
    /// A roster row nothing selects is still a row a test can build.</summary>
    [Fact]
    public void TheClassicGreyboxIsStillOnTheRosterAndStillCodeBuilt()
    {
        Assert.True(AvatarVisual.IsValidAvatarKey(AvatarVisual.ClassicGreyboxAvatarKey));
        Assert.True(AvatarVisual.IsCodeBuilt(AvatarVisual.ClassicGreyboxAvatarKey));
        Assert.Contains(AvatarVisual.RosterEntries, e => e.Key == AvatarVisual.ClassicGreyboxAvatarKey);
        // ...and it is no longer what anybody gets without asking.
        Assert.NotEqual(AvatarVisual.ClassicGreyboxAvatarKey, AvatarVisual.RosterEntries[0].Key);
        Assert.NotEqual(AvatarVisual.ClassicGreyboxAvatarKey, AvatarVisual.PreferredAvatarKey);
    }

    /// <summary><b>Scope item 2: the wire table, asserted rather than described.</b> "A peer must
    /// never render a different body than the sender intended without the choice being written
    /// down" — so every incoming spelling this build can plausibly receive is listed here with the
    /// body it produces, and the prose version lives on
    /// <c>AvatarVisual.RetiredGumdropAvatarKey</c>.
    ///
    /// <para>The decision BODY-2 made and could have made differently: <c>"greybox"</c> keeps
    /// resolving to the CLASSIC, not to the new default. The sender named a body that is still on
    /// the roster and still gets it. Re-pointing the alias at <c>PreferredAvatarKey</c> would turn
    /// a named body into "whatever is default this week", so an old peer's stated choice would
    /// silently change meaning every time the default moves — and the default has now moved
    /// twice in one week.</para></summary>
    [Fact]
    public void TheWireKeyTableIsWhatTheReportSays()
    {
        // An old build, a stale launcher line, a saved picker index.
        Assert.Equal(AvatarVisual.ClassicGreyboxAvatarKey,
            AvatarVisual.NormalizeAvatarKey("greybox"));
        // A live row; BODY-2 moved its index, not its meaning.
        Assert.Equal(AvatarVisual.ClassicGreyboxAvatarKey,
            AvatarVisual.NormalizeAvatarKey("greybox_classic"));
        // The new default, and what an unset local choice resolves to.
        Assert.Equal(AvatarVisual.BoxKidAvatarKey, AvatarVisual.NormalizeAvatarKey("boxkid"));
        Assert.Equal(AvatarVisual.BoxKidAvatarKey, AvatarVisual.PreferredAvatarKeyFor(null));
        // The containment clamp, untouched by any of it.
        Assert.Equal(AvatarVisual.DefaultAvatarKey,
            AvatarVisual.NormalizeAvatarKey("not-a-body-anyone-ever-shipped"));
        // The alias is an alias, not a resurrection: nothing may index the roster with it.
        Assert.False(AvatarVisual.IsValidAvatarKey("greybox"));
        // ...and the default is NOT what the alias lands on, which is the decision itself.
        Assert.NotEqual(AvatarVisual.PreferredAvatarKey, AvatarVisual.NormalizeAvatarKey("greybox"));
    }

    // --- The random colour --------------------------------------------------------------------

    /// <summary>Acceptance criterion 3, first half: the same seed replays the same session.
    /// This is what makes <c>SAIL_COLOR_SEED</c> worth printing — a colour layout nobody can
    /// reproduce is one nobody can file a bug about.</summary>
    [Fact]
    public void TheSameSeedDealsTheSameColours()
    {
        const ulong seed = 20260827UL;
        int[] first = Deal(seed);
        int[] second = Deal(seed);
        Assert.Equal(first, second);
    }

    /// <summary>Second half: different seeds actually differ. Stated over a span of seeds rather
    /// than one pair, because a mixer that collapsed to a constant would pass a single pair by
    /// luck a sixth of the time.</summary>
    [Fact]
    public void DifferentSeedsDealDifferentColours()
    {
        var distinct = new HashSet<string>();
        for (ulong seed = 0; seed < 64; seed++)
            distinct.Add(string.Join(",", Deal(seed)));
        Assert.True(distinct.Count > 32,
            $"64 seeds produced only {distinct.Count} distinct six-player deals");
    }

    /// <summary>
    /// <b>The ABSENCE, with its positive control</b> (acceptance criterion 3). The claim is that
    /// nothing dedups: two players CAN be dealt the same colour and nothing anywhere rejects it.
    /// An absence proved by "I looked and found no dedup code" is worth nothing, so this forces
    /// the collision instead — it searches the seed space for a seed that deals two equal colours
    /// and then asserts the deal stands. The control is the search itself: if a dedup existed,
    /// no seed in the space would produce a collision and this test would fail on
    /// <c>colliding &gt;= 0</c> rather than passing vacuously.
    /// </summary>
    [Fact]
    public void NothingRejectsTwoPlayersDealtTheSameColour()
    {
        ulong colliding = ulong.MaxValue;
        for (ulong seed = 0; seed < 4096 && colliding == ulong.MaxValue; seed++)
        {
            int[] deal = Deal(seed);
            if (deal.Distinct().Count() < deal.Length)
                colliding = seed;
        }
        Assert.True(colliding != ulong.MaxValue,
            "no seed in 4096 dealt a repeated colour to six players — with six slots and no " +
            "dedup that is statistically impossible, so something IS deduplicating");

        int[] colours = Deal(colliding);
        var groups = colours.GroupBy(c => c).Where(g => g.Count() > 1).ToList();
        Assert.NotEmpty(groups);
        // ...and the repeat is a real palette colour, not a sentinel the deal fell back to.
        foreach (var group in groups)
        {
            Assert.InRange(group.Key, 0, SandboxAvatar.RandomPaletteSlots - 1);
            Assert.Equal(SandboxAvatar.PaletteColorFor(group.Key),
                SandboxAvatar.RandomPaletteColorFor(colliding,
                    Array.IndexOf(colours, group.Key)));
        }
    }

    /// <summary>The deal stays inside the six slots the palette guarantees separation across, and
    /// reaches all six — a mixer biased onto a subset would quietly halve the colour variety.</summary>
    [Fact]
    public void TheDealCoversTheDealtSixAndNothingElse()
    {
        var seen = new HashSet<int>();
        for (ulong seed = 0; seed < 256; seed++)
        {
            foreach (int slot in Deal(seed))
            {
                Assert.InRange(slot, 0, SandboxAvatar.RandomPaletteSlots - 1);
                seen.Add(slot);
            }
        }
        Assert.Equal(SandboxAvatar.RandomPaletteSlots, seen.Count);
        Assert.Equal(6, SandboxAvatar.RandomPaletteSlots);
    }

    /// <summary>The random deal and the round-robin deal read the same array, so
    /// <c>PaletteIndexFor</c> — which <c>BotHarness</c> logs with — still reverses either.</summary>
    [Fact]
    public void ARandomlyDealtColourIsStillAPaletteColour()
    {
        for (int i = 0; i < 6; i++)
        {
            Color c = SandboxAvatar.RandomPaletteColorFor(20260827UL, i);
            Assert.Equal(SandboxAvatar.RandomPaletteIndexFor(20260827UL, i),
                SandboxAvatar.PaletteIndexFor(c));
        }
    }

    private static int[] Deal(ulong seed) =>
        Enumerable.Range(0, 6).Select(i => SandboxAvatar.RandomPaletteIndexFor(seed, i)).ToArray();

    // --- The glTF reader ----------------------------------------------------------------------
    //
    // A deliberately small second implementation rather than a shared one with
    // GreyboxAssetContractTests: that file's parser walks the node tree accumulating world
    // transforms for its joint-pivot theory, and this file needs only names and bounds. Sharing
    // it would couple two contracts about two different files, which is the coupling the roster's
    // own absent-parts rule exists to avoid.

    private readonly record struct Bounds(
        float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ);

    private sealed class GltfBody
    {
        public HashSet<string> Nodes { get; } = new();
        public Dictionary<string, Bounds> Parts { get; } = new();
        public Bounds Bounds { get; set; }

        public (float Lo, float Hi) YSpanOf(string part) => (Parts[part].MinY, Parts[part].MaxY);
    }

    private static string BoxKidPath() =>
        Path.Combine(FindRepoRoot(), "assets", "creatures", "boxkid", "BoxKid.glb");

    private static List<string> ClipNames(JsonElement gltf)
    {
        var names = new List<string>();
        if (gltf.TryGetProperty("animations", out JsonElement animations))
        {
            foreach (JsonElement a in animations.EnumerateArray())
                names.Add(a.TryGetProperty("name", out JsonElement n) ? n.GetString()! : "");
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static JsonElement LoadBoxKidJson() => LoadJson(BoxKidPath());

    /// <summary>The JSON chunk of a binary glTF. A .glb is a 12-byte header then length-prefixed
    /// chunks; the first is always JSON.</summary>
    private static JsonElement LoadJson(string glbPath)
    {
        Assert.True(File.Exists(glbPath), $"{glbPath} does not exist");
        byte[] bytes = File.ReadAllBytes(glbPath);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        string json = System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Every node's name, and the WORLD bounds of every mesh node. The rig is a pure
    /// translation hierarchy with no rotation or scale anywhere (the builder's self-check proves
    /// that inside Blender, and reset_rest_pose is what keeps it true through the export), so
    /// accumulating translations is the whole of the transform maths.</summary>
    private static GltfBody LoadBoxKid()
    {
        JsonElement gltf = LoadBoxKidJson();
        var body = new GltfBody();
        JsonElement nodes = gltf.GetProperty("nodes");
        JsonElement meshes = gltf.GetProperty("meshes");
        JsonElement accessors = gltf.GetProperty("accessors");

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        void Walk(int index, float ox, float oy, float oz)
        {
            JsonElement node = nodes[index];
            string name = node.TryGetProperty("name", out JsonElement n) ? n.GetString()! : "";
            body.Nodes.Add(name);

            float x = ox, y = oy, z = oz;
            if (node.TryGetProperty("translation", out JsonElement t))
            {
                x += t[0].GetSingle();
                y += t[1].GetSingle();
                z += t[2].GetSingle();
            }

            if (node.TryGetProperty("mesh", out JsonElement meshIndex))
            {
                float pMinX = float.MaxValue, pMaxX = float.MinValue;
                float pMinY = float.MaxValue, pMaxY = float.MinValue;
                float pMinZ = float.MaxValue, pMaxZ = float.MinValue;
                foreach (JsonElement prim in meshes[meshIndex.GetInt32()].GetProperty("primitives")
                             .EnumerateArray())
                {
                    JsonElement acc = accessors[prim.GetProperty("attributes")
                        .GetProperty("POSITION").GetInt32()];
                    JsonElement lo = acc.GetProperty("min");
                    JsonElement hi = acc.GetProperty("max");
                    pMinX = Math.Min(pMinX, x + lo[0].GetSingle());
                    pMaxX = Math.Max(pMaxX, x + hi[0].GetSingle());
                    pMinY = Math.Min(pMinY, y + lo[1].GetSingle());
                    pMaxY = Math.Max(pMaxY, y + hi[1].GetSingle());
                    pMinZ = Math.Min(pMinZ, z + lo[2].GetSingle());
                    pMaxZ = Math.Max(pMaxZ, z + hi[2].GetSingle());
                }
                body.Parts[name] = new Bounds(pMinX, pMaxX, pMinY, pMaxY, pMinZ, pMaxZ);
                minX = Math.Min(minX, pMinX); maxX = Math.Max(maxX, pMaxX);
                minY = Math.Min(minY, pMinY); maxY = Math.Max(maxY, pMaxY);
                minZ = Math.Min(minZ, pMinZ); maxZ = Math.Max(maxZ, pMaxZ);
            }

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    Walk(child.GetInt32(), x, y, z);
            }
        }

        foreach (JsonElement root in gltf.GetProperty("scenes")[0].GetProperty("nodes")
                     .EnumerateArray())
            Walk(root.GetInt32(), 0f, 0f, 0f);

        body.Bounds = new Bounds(minX, maxX, minY, maxY, minZ, maxZ);
        return body;
    }

    // --- The vertical value ramp (BODY-2, scope item 4) ---------------------------------------
    //
    // The CURVE is pure and lives here; whether it actually lands on the body's meshes needs an
    // engine and is SandboxSelfTest's `the_value_ramp_darkens_the_feet_and_lightens_the_head`.
    // Two kinds of evidence for one claim, deliberately: this half would pass unchanged if
    // ApplyValueRamp were never called, and that half would pass on any monotonic curve.

    /// <summary><b>Strength 1 reproduces the endpoints the body already had</b>, which is what
    /// makes "1.0" the current-classic-equivalent rung of the three captures and what makes this
    /// packet a knob rather than a re-paint. -0.24 was the shins' and feet's delta before BODY-2;
    /// +0.10 was a distinct-volume head's.</summary>
    [Fact]
    public void StrengthOneReproducesTheDeltasThatWereAlreadyOnTheBody()
    {
        Assert.Equal(-0.24f, AvatarVisual.BodyValueRampDelta(0f, 1f), 1e-4f);
        Assert.Equal(0.10f, AvatarVisual.BodyValueRampDelta(1f, 1f), 1e-4f);
    }

    /// <summary>Zero is the exact identity — the control. A ramp that could not be turned off could
    /// not be shown to be the thing a capture is showing.</summary>
    [Fact]
    public void StrengthZeroIsTheExactIdentity()
    {
        foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            Assert.Equal(0f, AvatarVisual.BodyValueRampDelta(t, 0f), 1e-6f);
            var body = new Color(0.4f, 0.7f, 0.35f);
            Assert.Equal(body, AvatarVisual.BodyColorAtRampDelta(body, 0f));
        }
    }

    /// <summary><b>Talon's actual sentence, as an assertion:</b> "darker green of the limbs ...
    /// going to lighter green going up to the head". Monotonic in height at every strength, which
    /// the per-part list BODY-2 replaced was NOT — it had the hips (below the torso) as the second
    /// lightest thing on the body.</summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(1.8f)]
    [InlineData(2.8f)]
    [InlineData(4f)]
    public void TheRampIsMonotonicDarkAtTheBottomLightAtTheTop(float strength)
    {
        float previous = float.NegativeInfinity;
        for (int i = 0; i <= 20; i++)
        {
            float delta = AvatarVisual.BodyValueRampDelta(i / 20f, strength);
            Assert.True(delta > previous, $"t={i / 20f} delta {delta} did not rise above {previous}");
            previous = delta;
        }
        Assert.True(AvatarVisual.BodyValueRampDelta(0f, strength) < 0f, "the feet must darken");
        Assert.True(AvatarVisual.BodyValueRampDelta(1f, strength) > 0f, "the head must lighten");
    }

    /// <summary>Pushing the knob widens the spread and never narrows it — the whole request. Checked
    /// as a spread rather than per-end so it holds through the ±0.95 clamp, where one end can be
    /// pinned while the other still moves.</summary>
    [Fact]
    public void PushingTheKnobWidensTheSpread()
    {
        float previous = 0f;
        foreach (float s in new[] { 0f, 0.5f, 1f, 1.8f, 2.8f, 4f })
        {
            float spread = AvatarVisual.BodyValueRampDelta(1f, s) - AvatarVisual.BodyValueRampDelta(0f, s);
            Assert.True(spread >= previous, $"strength {s} spread {spread} narrowed from {previous}");
            previous = spread;
        }
    }

    /// <summary>Out-of-range inputs are contained rather than trusted, on BOTH axes, and the delta
    /// never reaches ±1 — at which point <c>Darkened</c>/<c>Lightened</c> return pure black and pure
    /// white, and a part that has lost its hue has stopped carrying the player's colour.</summary>
    [Fact]
    public void TheRampContainsItsOwnInputsAndNeverLosesTheHue()
    {
        Assert.Equal(AvatarVisual.BodyValueRampDelta(0f, 1f), AvatarVisual.BodyValueRampDelta(-5f, 1f), 1e-6f);
        Assert.Equal(AvatarVisual.BodyValueRampDelta(1f, 1f), AvatarVisual.BodyValueRampDelta(9f, 1f), 1e-6f);
        Assert.Equal(
            AvatarVisual.BodyValueRampDelta(0f, AvatarVisual.MaxBodyValueRampStrength),
            AvatarVisual.BodyValueRampDelta(0f, 999f), 1e-6f);
        Assert.Equal(
            AvatarVisual.BodyValueRampDelta(0f, 0f), AvatarVisual.BodyValueRampDelta(0f, -3f), 1e-6f);

        var body = new Color(0.35f, 0.62f, 0.30f);
        for (int i = 0; i <= 20; i++)
        {
            float delta = AvatarVisual.BodyValueRampDelta(i / 20f, AvatarVisual.MaxBodyValueRampStrength);
            Assert.InRange(delta, -0.95f, 0.95f);
            Color tinted = AvatarVisual.BodyColorAtRampDelta(body, delta);
            Assert.True(tinted.R + tinted.G + tinted.B > 0.001f, "a ramped part must not be pure black");
            Assert.True(tinted.G > tinted.R, "a ramped part must keep the player's hue");
        }
    }

    /// <summary><b>The knob's precedence and its containment.</b> An explicit set wins over the
    /// environment; the reset hands control back. Clamped on the way IN as well as on the way out,
    /// so nothing downstream can read a strength the captures did not bracket.
    ///
    /// <para>The env var is deliberately not written here: the xUnit suite runs in parallel and
    /// process environment is global, so a test that set <c>SAIL_BODY_RAMP</c> would be setting it
    /// for every other test in the assembly. The env path's real evidence is the headed
    /// captures.</para></summary>
    [Fact]
    public void TheKnobIsSettableResettableAndClamped()
    {
        try
        {
            AvatarVisual.BodyValueRampStrength = 1.8f;
            Assert.Equal(1.8f, AvatarVisual.BodyValueRampStrength, 1e-4f);
            AvatarVisual.BodyValueRampStrength = 999f;
            Assert.Equal(AvatarVisual.MaxBodyValueRampStrength, AvatarVisual.BodyValueRampStrength, 1e-4f);
            AvatarVisual.BodyValueRampStrength = -7f;
            Assert.Equal(AvatarVisual.MinBodyValueRampStrength, AvatarVisual.BodyValueRampStrength, 1e-4f);
        }
        finally
        {
            AvatarVisual.ResetBodyValueRampStrength();
        }
        // Guarded, because the reset hands control back to the ENVIRONMENT and a developer running
        // the suite with SAIL_BODY_RAMP exported would otherwise get a red that is the knob working
        // correctly. Asserting the default unconditionally here would be asserting that nobody has
        // used the feature.
        if (string.IsNullOrWhiteSpace(
                System.Environment.GetEnvironmentVariable(AvatarVisual.BodyValueRampEnvVar)))
        {
            Assert.Equal(AvatarVisual.DefaultBodyValueRampStrength, AvatarVisual.BodyValueRampStrength, 1e-4f);
        }
    }

    /// <summary>Walks up from the test assembly until it finds the repo root. The suite runs from
    /// <c>tests/unit/bin/...</c>, and hard-coding the depth breaks the day the output path
    /// changes.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
