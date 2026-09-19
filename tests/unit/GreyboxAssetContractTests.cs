using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace Sail.Tests;

/// <summary>
/// <b>The authored player asset and the constants the rest of the game derives from, compared —
/// with no engine in the room.</b>
///
/// <para><b>The failure this file exists to make impossible.</b> As of INTEG-1 the body a player is
/// comes out of <c>assets/creatures/boxkid/BoxKid.glb</c>, a file Talon can open and change. Three
/// places in the codebase still derive from <see cref="GreyboxAvatarBody"/>'s constants instead —
/// <c>FaunaSpec.FloatHeightM</c> takes <c>CrownM * FloatHeightCrownFraction</c> for the firefly's
/// float height, and <c>CatchInstrumentTests</c> / <c>NetReadyAndReachTests</c> assert against
/// <c>CrownM</c> and <c>EyeY</c>. RIG-1's report flagged all three as going stale <i>silently</i> if
/// the authored asset ever lands at a different crown: nothing in the repo compared the file against
/// the constants, so an edit in Blender could move the played body 10 cm and every suite would stay
/// green while the firefly drifted out of reach. This is that comparison.</para>
///
/// <para><b>Why it parses the glTF by hand rather than asking Godot.</b> The engine answer already
/// exists (<c>SandboxSelfTest.RunAuthoredBodyTestsAsync</c> measures the built body, and
/// <c>GreyboxPlayerLab</c> photographs it), and this is deliberately the OTHER kind of evidence: it
/// reads the shipped bytes, needs no import, no GPU and no display, and it therefore runs in the
/// xUnit suite on any machine. glTF stores each accessor's own <c>min</c>/<c>max</c>, so the exact
/// bounds of every part are available without decoding a single vertex buffer.</para>
///
/// <para>This is also the second job <see cref="GreyboxAvatarBody"/> keeps now that it is no longer
/// what ships: the engine-free test fixture. Every constant read below is a compile-time
/// <c>const</c> — no <c>Node3D</c> is constructed, which is what makes reading them legal in a host
/// with no Godot runtime loaded at all.</para>
/// </summary>
public class GreyboxAssetContractTests
{
    /// <summary>The tolerance INTEG-1's criterion 3 sets on the two load-bearing heights. Tight on
    /// purpose: these are authored to a round number, not measured off an organic shape, so anything
    /// outside half a centimetre is an edit rather than noise.</summary>
    private const float Tol = 0.005f;

    /// <summary>Contract part names the harvest path requires of a <c>Build.HarvestedParts</c> row
    /// and the authored asset therefore has to carry. The eight it legitimately lacks (Belly, Tail,
    /// BackFiller, BlushL, BlushR, Stem, LeafL, LeafR) are declared absent on the roster row and are
    /// deliberately not listed here — see <c>AvatarVisual.GreyboxAbsentParts</c>.</summary>
    private static readonly string[] RequiredParts =
    {
        "Hips", "Torso", "Head", "Mouth", "EyeL", "EyeR",
        "ArmL", "ArmR", "ForearmL", "ForearmR",
        "ThighL", "ThighR", "ShinL", "ShinR", "FootL", "FootR",
    };

    [Fact]
    public void AuthoredAsset_CarriesEveryRequiredContractPart()
    {
        GltfBody body = LoadAuthoredGreybox();
        var missing = new List<string>();
        foreach (string name in RequiredParts)
        {
            if (!body.Parts.ContainsKey(name))
                missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            $"BoxKid.glb is missing contract part(s): {string.Join(", ", missing)}. " +
            $"It carries: {string.Join(", ", body.Parts.Keys)}");
    }

    /// <summary>
    /// <b>Criterion 3, from the file.</b> The crown and the eyeline the game's derived values assume,
    /// measured off the shipped bytes. If this fails, the firefly's float height and two other test
    /// files are already wrong and the fix is not here.
    /// </summary>
    [Fact]
    public void AuthoredAsset_CrownAndEyelineMatchTheConstantsTheGameDerivesFrom()
    {
        GltfBody body = LoadAuthoredGreybox();

        float crown = body.Bounds.MaxY;
        float floor = body.Bounds.MinY;
        float eye = body.EyeCentreY;

        Assert.True(Math.Abs(crown - GreyboxAvatarBody.CrownM) <= Tol,
            $"authored crown {crown:F4} m vs GreyboxAvatarBody.CrownM {GreyboxAvatarBody.CrownM:F4} m " +
            $"(tolerance {Tol:F3}). FaunaSpec.FloatHeightM derives the firefly's height from that " +
            "constant; they have drifted apart.");
        Assert.True(Math.Abs(eye - GreyboxAvatarBody.EyeY) <= Tol,
            $"authored eye centre {eye:F4} m vs GreyboxAvatarBody.EyeY {GreyboxAvatarBody.EyeY:F4} m " +
            $"(tolerance {Tol:F3}). CatchInstrumentTests and NetReadyAndReachTests assert against " +
            "that constant as the aim-ray height.");
        // A body that does not stand on the floor is a body that floats or sinks, and the crown
        // check alone cannot see it.
        Assert.True(Math.Abs(floor) <= Tol, $"authored asset's lowest vertex is at y {floor:F4} m, not 0");
    }

    /// <summary>
    /// <b>Criterion 4, from the file: the hem and the leg.</b> The defect RIG-1 was built to fix was
    /// a trunk that overhung the legs like a dress. These two numbers are what "it does not" means,
    /// and they are properties of the authored geometry rather than of the code that used to make it.
    /// </summary>
    [Fact]
    public void AuthoredAsset_HasNoHemAndAVisibleLeg()
    {
        GltfBody body = LoadAuthoredGreybox();

        float hipsHalfX = Math.Max(
            Math.Abs(body.Parts["Hips"].MinX), Math.Abs(body.Parts["Hips"].MaxX));
        float legOuterHalfX = 0f;
        foreach (string leg in new[] { "ThighL", "ThighR", "ShinL", "ShinR", "FootL", "FootR" })
        {
            legOuterHalfX = Math.Max(legOuterHalfX,
                Math.Max(Math.Abs(body.Parts[leg].MinX), Math.Abs(body.Parts[leg].MaxX)));
        }

        Assert.True(hipsHalfX < legOuterHalfX,
            $"hips half-X {hipsHalfX:F4} m must be strictly inside the legs' outer half-X " +
            $"{legOuterHalfX:F4} m, or the trunk overhangs the legs (overhang " +
            $"{hipsHalfX - legOuterHalfX:+0.0000;-0.0000} m)");

        float crown = body.Bounds.MaxY;
        float visibleLegFraction = body.Parts["Hips"].MinY / crown;
        Assert.True(visibleLegFraction >= 0.33f,
            $"visible leg is {visibleLegFraction * 100f:F1}% of the crown (hips bottom " +
            $"{body.Parts["Hips"].MinY:F4} m / crown {crown:F4} m); the bar is 33%");
    }

    /// <summary>
    /// <b>The joints the file declares, against the joints the code expects.</b> RIG-1's rig derives
    /// the thigh/shin and upper-arm/forearm splits from the harvested nodes' own rest offsets and
    /// reads no constant to do it — so the file's joint origins are the real source of those lengths.
    /// This asserts the file puts them where the code's own body puts them, which is what makes the
    /// two directly comparable in a contact sheet.
    /// </summary>
    [Fact]
    public void AuthoredAsset_PutsItsJointsWhereTheRigExpectsThem()
    {
        GltfBody body = LoadAuthoredGreybox();

        // Equal-segment legs and a two-segment arm: the shin origin sits half way down the leg, the
        // forearm origin at the elbow. Measured as differences between node origins, so a body
        // authored at a different overall height would still satisfy them.
        float thigh = body.Origins["ThighL"].Y - body.Origins["ShinL"].Y;
        float shin = body.Origins["ShinL"].Y - body.Bounds.MinY;
        float upperArm = body.Origins["ArmL"].Y - body.Origins["ForearmL"].Y;

        Assert.True(thigh > 0.05f && shin > 0.05f,
            $"both leg segments must be real: thigh {thigh:F4} m, shin {shin:F4} m");
        Assert.True(Math.Abs(thigh - shin) <= 0.01f,
            $"the authored leg is not split evenly: thigh {thigh:F4} m vs shin {shin:F4} m");
        Assert.True(upperArm > 0.05f,
            $"the authored elbow must sit below the shoulder: upper arm {upperArm:F4} m");

        // The waist seam. AvatarVisual places the waist joint at the harvested Hips AABB's top edge,
        // so the torso has to start exactly there or the upper body bends away from its own seam.
        float hipsTop = body.Parts["Hips"].MaxY;
        float torsoBottom = body.Parts["Torso"].MinY;
        Assert.True(Math.Abs(hipsTop - torsoBottom) <= Tol,
            $"the waist seam is open: hips top {hipsTop:F4} m vs torso bottom {torsoBottom:F4} m");
    }

    /// <summary>
    /// <b>The asset ships rigid, with no skin — and now WITH animations.</b>
    ///
    /// <para><b>The skin half is unchanged and is not a nicety.</b> Both the harvest path and
    /// <c>Build.AuthoredRig</c> drive named node transforms, and a skinned mesh would be driven by a
    /// <c>Skeleton3D</c> neither of them touches — the body would import and then refuse to move.
    /// MODEL-1 excluded the armature from the export deliberately (Godot's importer suffixes bones
    /// that share a mesh name, e.g. <c>Torso</c> to <c>Torso_2</c>); whether bones or meshes take the
    /// suffix is Talon's open call, and this half is what will fail loudly the day someone resolves
    /// it by re-exporting with the armature in.</para>
    ///
    /// <para><b>The animations half INVERTED on 2026-08-21 (ANIM-M2 authored, ANIM-M3 flipped the
    /// assertion), and it is a rewrite rather than a deletion.</b> It used to read <i>"BoxKid.glb
    /// ships an animation; the pose is entirely procedural"</i> and it was the correct guard for a
    /// body whose every joint was written from code. The migration made the opposite true: the
    /// fourteen hand-keyed clips in the <c>.glb</c> ARE the base pose now, and an export that quietly
    /// lost them would leave a body that imports, resolves every joint, and stands perfectly still —
    /// which is exactly the silent failure this file exists to make impossible. Deleting the
    /// assertion would have left that hole open; inverting it moves the guard to the new truth.</para>
    /// </summary>
    [Fact]
    public void AuthoredAsset_ShipsRigidWithNoSkinBindings()
    {
        JsonElement gltf = ReadGltfJson(AuthoredGreyboxPath());
        Assert.False(gltf.TryGetProperty("skins", out _), "BoxKid.glb ships a skin; the rig animates node transforms and cannot drive one");
        Assert.True(gltf.TryGetProperty("animations", out JsonElement animations),
            "BoxKid.glb ships NO animations; the played body's base pose comes from its authored " +
            "clip library (ANIM-M2/M3) and a body with no clips stands perfectly still");
        Assert.True(animations.GetArrayLength() > 0, "BoxKid.glb's animations array is empty");
    }

    /// <summary>
    /// <b>ANIM-M1's pivot sign-off, from the shipped bytes.</b>
    ///
    /// <para>The gate itself is an in-engine capture with a marker on every joint
    /// (<c>GreyboxPlayerLab --greybox-lab-pivots</c>), and that run measured the same numbers on the
    /// live harvested rig and found them identical to these to 0.000 mm. This is the half of it that
    /// survives without a GPU: it is the thing that will fail the day somebody re-exports the asset
    /// with a joint moved, which is the day a library of hand-keyed clips silently stops matching the
    /// body it was keyed on.</para>
    ///
    /// <para><b>The two criteria a clip actually depends on.</b> ON-AXIS — the pivot sits on its own
    /// segment's centreline in X and Z, so a pitch key swings the limb forward rather than forward
    /// and sideways. AT THE PROXIMAL END — the pivot sits at the end of the segment it rotates,
    /// expressed as a fraction of that segment's length: 0 is exactly the end, negative is the
    /// segment reaching back past the pivot (the overlap that seals the joint), and 0.5 would be a
    /// pivot at the MIDDLE of the thing it rotates, which is the shape of the defect this gate exists
    /// to catch. The bound is deliberately loose on the negative side (-0.32) because a rounded limb
    /// cap legitimately stands proud of its joint by its own radius; it is tight on the positive side
    /// (+0.05) because a pivot clear of its own geometry is a visible gap the moment it bends.</para>
    /// </summary>
    [Theory]
    // segment,     parent,  the joint it is named for
    [InlineData("ThighL", "Hips", "hip L")]
    [InlineData("ThighR", "Hips", "hip R")]
    [InlineData("ShinL", "ThighL", "knee L")]
    [InlineData("ShinR", "ThighR", "knee R")]
    [InlineData("ArmL", "Torso", "shoulder L")]
    [InlineData("ArmR", "Torso", "shoulder R")]
    [InlineData("ForearmL", "ArmL", "elbow L")]
    [InlineData("ForearmR", "ArmR", "elbow R")]
    [InlineData("Head", "Torso", "neck")]
    public void AuthoredAsset_PutsEveryJointPivotOnItsSegmentsAxisAndAtItsProximalEnd(
        string segment, string parent, string joint)
    {
        GltfBody body = LoadAuthoredGreybox();
        (float X, float Y, float Z) pivot = body.Origins[segment];
        Bounds3 seg = body.Parts[segment];

        // ON-AXIS. Measured against the segment's own X/Z centre rather than against zero, because a
        // leg's pivot is 0.19 m off the midline and correctly so — "on axis" is a claim about the
        // segment, never about the body.
        float offX = pivot.X - (seg.MinX + seg.MaxX) * 0.5f;
        float offZ = pivot.Z - (seg.MinZ + seg.MaxZ) * 0.5f;
        Assert.True(Math.Abs(offX) <= 0.008f && Math.Abs(offZ) <= 0.008f,
            $"{joint}: pivot is off '{segment}'s own centreline by " +
            $"({offX * 1000f:+0.0;-0.0}, {offZ * 1000f:+0.0;-0.0}) mm; a pitch key here swings the " +
            "segment sideways as well as forward");

        // AT THE PROXIMAL END. Proximal = the Y extreme nearer the pivot; distal = the far one.
        bool topIsProximal = Math.Abs(seg.MaxY - pivot.Y) <= Math.Abs(seg.MinY - pivot.Y);
        float proximal = topIsProximal ? seg.MaxY : seg.MinY;
        float distal = topIsProximal ? seg.MinY : seg.MaxY;
        float length = Math.Abs(distal - pivot.Y);
        Assert.True(length > 0.05f, $"{joint}: '{segment}' is only {length:F4} m long; that is not a segment");

        // Signed along the DISTAL direction, so the sign means the same thing for a limb hanging
        // down (distal below the pivot) and for the head standing up (distal above it).
        float distalDir = distal < pivot.Y ? -1f : 1f;
        float endFrac = (proximal - pivot.Y) * distalDir / length;
        Assert.True(endFrac >= -0.32f && endFrac <= 0.05f,
            $"{joint}: the pivot sits {endFrac:+0.000;-0.000} of '{segment}'s length from its proximal " +
            $"end (segment {length:F4} m, pivot y {pivot.Y:F4}, proximal y {proximal:F4}). " +
            "0 is the end; positive opens a gap when it bends; a value near 0.5 means the pivot is in " +
            "the MIDDLE of the segment it rotates.");

        // The parent has to be real, or "sealed inside the parent" is a claim about nothing.
        Assert.True(body.Parts.ContainsKey(parent), $"{joint}: parent '{parent}' carries no geometry");
    }

    /// <summary>
    /// <b>The leg is thigh, then shin, then foot — and the ankle pivot is ON THE GROUND.</b>
    ///
    /// <para><b>This test INVERTED on 2026-08-21 (ANIM-M2b), and the inversion is the point.</b> It
    /// used to be called <c>AuthoredAsset_HasNoAnkleAndItsLegIsThighThenShin</c> and it said "the day
    /// someone adds a real foot, this fails and they have to come and say so." Someone did, Talon
    /// ruled it in, and this is them saying so. The thighs — grandfathered onto <c>FootL</c>/
    /// <c>FootR</c> because <c>Foot*</c> was the shipped -uffling vocabulary — are <c>ThighL</c>/
    /// <c>ThighR</c> now, and <c>Foot*</c> means a foot.</para>
    ///
    /// <para><b>The ankle being at y = 0 is the load-bearing half.</b> The runtime levels the foot by
    /// countering hip + knee (<c>LimbIk.LevelAnkle</c>), and that levelling carries the sole
    /// <c>ankleHeight × sin(hip angle)</c> off the leg chain's own line — at the ratified stance
    /// extremes an ankle 24 mm up would put the planted sole 15 mm off and back every step, which is
    /// 4.4% of the stride and is skate. At zero the error is identically zero at every angle, and
    /// <c>AvatarVisual.FootContactGlobal</c> — the point the foot-slip canary measures — and the sole
    /// a player watches become the same place. An asset that raises the ankle silently re-opens
    /// that, so it is pinned here rather than only in the generator's own self-check.</para>
    /// </summary>
    [Fact]
    public void AuthoredAsset_HasAnAnkleOnTheGroundAndItsLegIsThighShinFoot()
    {
        GltfBody body = LoadAuthoredGreybox();

        foreach (string gone in new[] { "AnkleL", "AnkleR", "ToeL", "ToeR", "LegL", "LegR" })
        {
            Assert.False(body.Parts.ContainsKey(gone),
                $"'{gone}' has appeared in BoxKid.glb. The leg's vocabulary is ThighL -> ShinL -> " +
                "FootL and nothing else; a fourth name changes what a clip can be keyed on and " +
                "AvatarVisual's authored path does not look it up.");
        }

        // THE ANKLE PIVOT IS THE GROUND PLANE. Measured as the foot node's own origin, which is what
        // the runtime rotates about.
        foreach (string foot in new[] { "FootL", "FootR" })
        {
            float ankleY = body.Origins[foot].Y;
            Assert.True(Math.Abs(ankleY) <= 0.001f,
                $"{foot}'s pivot is at y {ankleY:F4} m, not on the ground. The runtime levels the " +
                "sole about this point, and the levelling is exact only here — see LimbIk.LevelAnkle " +
                "and build_greybox.py's ANKLE_JOINT_Y for the millimetres it costs elsewhere.");

            // ...and the foot's geometry is entirely ABOVE it, which is the shape that follows from
            // the pivot being the contact point. Every other segment on this body hangs below its
            // joint; this one does not, and a foot built the usual way would put its sole underground.
            Bounds3 f = body.Parts[foot];
            Assert.True(Math.Abs(f.MinY) <= Tol && f.MaxY > 0.01f,
                $"{foot} spans y {f.MinY:F4}..{f.MaxY:F4} m; its sole must sit on the floor and its " +
                "geometry must stand above its own pivot");
        }

        // THE SOLE IS WHAT STANDS ON THE FLOOR — the shin no longer reaches it.
        Bounds3 shin = body.Parts["ShinL"];
        Assert.True(shin.MinY > Tol,
            $"ShinL reaches down to y {shin.MinY:F4} m. Since ANIM-M2b the FOOT carries the ground " +
            "contact; a shin back on the floor means the foot was removed or the ankle seat moved.");
        Assert.True(Math.Abs(body.Bounds.MinY) <= 0.001f,
            $"the body's lowest vertex is at y {body.Bounds.MinY:F4} m, not exactly 0");
    }

    // --- a minimal glTF reader: node graph, accessor bounds, nothing else --------------------

    private readonly record struct Bounds3(
        float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ);

    private sealed class GltfBody
    {
        public Dictionary<string, Bounds3> Parts { get; } = new();
        public Dictionary<string, (float X, float Y, float Z)> Origins { get; } = new();
        public Bounds3 Bounds { get; set; }
        public float EyeCentreY { get; set; }
    }

    private static string AuthoredGreyboxPath() =>
        Path.Combine(FindRepoRoot(), "assets", "creatures", "boxkid", "BoxKid.glb");

    /// <summary>Walks the glTF node tree accumulating translations, and for every node with a mesh
    /// records that mesh's world-space bounds from its POSITION accessor's declared min/max.
    ///
    /// <para>Rotations and scales are asserted absent rather than applied: this asset is authored as
    /// pure translations (its node origins ARE its joints), and silently ignoring a rotation would
    /// turn a real authoring change into a wrong number instead of a failure.</para></summary>
    private static GltfBody LoadAuthoredGreybox()
    {
        string path = AuthoredGreyboxPath();
        Assert.True(File.Exists(path), $"the authored player asset is missing: {path}");
        JsonElement gltf = ReadGltfJson(path);

        JsonElement nodes = gltf.GetProperty("nodes");
        JsonElement meshes = gltf.GetProperty("meshes");
        JsonElement accessors = gltf.GetProperty("accessors");

        var body = new GltfBody();
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        void Walk(int index, float ox, float oy, float oz)
        {
            JsonElement node = nodes[index];
            Assert.False(node.TryGetProperty("rotation", out _),
                $"node '{NameOf(node)}' carries a rotation; this reader assumes pure translations");
            Assert.False(node.TryGetProperty("scale", out _),
                $"node '{NameOf(node)}' carries a scale; this reader assumes pure translations");

            if (node.TryGetProperty("translation", out JsonElement t))
            {
                ox += t[0].GetSingle();
                oy += t[1].GetSingle();
                oz += t[2].GetSingle();
            }

            string name = NameOf(node);
            body.Origins[name] = (ox, oy, oz);

            if (node.TryGetProperty("mesh", out JsonElement meshRef))
            {
                float pMinX = float.MaxValue, pMaxX = float.MinValue;
                float pMinY = float.MaxValue, pMaxY = float.MinValue;
                float pMinZ = float.MaxValue, pMaxZ = float.MinValue;
                foreach (JsonElement primitive in meshes[meshRef.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    JsonElement acc = accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                    JsonElement lo = acc.GetProperty("min");
                    JsonElement hi = acc.GetProperty("max");
                    pMinX = Math.Min(pMinX, lo[0].GetSingle()); pMaxX = Math.Max(pMaxX, hi[0].GetSingle());
                    pMinY = Math.Min(pMinY, lo[1].GetSingle()); pMaxY = Math.Max(pMaxY, hi[1].GetSingle());
                    pMinZ = Math.Min(pMinZ, lo[2].GetSingle()); pMaxZ = Math.Max(pMaxZ, hi[2].GetSingle());
                }

                var world = new Bounds3(
                    ox + pMinX, ox + pMaxX, oy + pMinY, oy + pMaxY, oz + pMinZ, oz + pMaxZ);
                body.Parts[name] = world;
                minX = Math.Min(minX, world.MinX); maxX = Math.Max(maxX, world.MaxX);
                minY = Math.Min(minY, world.MinY); maxY = Math.Max(maxY, world.MaxY);
                minZ = Math.Min(minZ, world.MinZ); maxZ = Math.Max(maxZ, world.MaxZ);
            }

            if (node.TryGetProperty("children", out JsonElement kids))
            {
                foreach (JsonElement kid in kids.EnumerateArray())
                    Walk(kid.GetInt32(), ox, oy, oz);
            }
        }

        foreach (JsonElement root in gltf.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray())
            Walk(root.GetInt32(), 0f, 0f, 0f);

        body.Bounds = new Bounds3(minX, maxX, minY, maxY, minZ, maxZ);
        // The eyeline the aim ray leaves from is the centre of the eye geometry, exactly as
        // AvatarVisual.EyePartCentre measures it off the built meshes.
        Bounds3 eyeL = body.Parts["EyeL"];
        body.EyeCentreY = (eyeL.MinY + eyeL.MaxY) * 0.5f;
        return body;
    }

    private static string NameOf(JsonElement node) =>
        node.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";

    /// <summary>Pulls the JSON chunk out of a binary glTF container. The format is a 12-byte header
    /// followed by length-prefixed chunks; the JSON one is type <c>0x4E4F534A</c>.</summary>
    private static JsonElement ReadGltfJson(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 20, $"{path} is too short to be a .glb");
        Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = BitConverter.ToInt32(bytes, offset);
            uint type = BitConverter.ToUInt32(bytes, offset + 4);
            if (type == 0x4E4F534A)
            {
                string json = System.Text.Encoding.UTF8.GetString(bytes, offset + 8, length);
                return JsonDocument.Parse(json).RootElement.Clone();
            }
            offset += 8 + length;
        }

        Assert.True(false, $"{path} contains no glTF JSON chunk");
        return default;
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
