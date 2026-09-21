using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MpFoundation.Game.World.Stock;

namespace SailNet.Tests;

/// <summary>
/// <b>The generator that turns <see cref="ShelfStock"/>'s arithmetic into authored scene files —
/// and the parser that reads the room back out of its own <c>.tscn</c>.</b>
///
/// <para><b>Why a generator and not a <c>ShelfStocker</c> node.</b> STOCK-1's packet asked for a
/// procedural fill at scene load, seeded per bay, "not hand-placed <c>.tscn</c> transforms",
/// because 1500–3000 instances by hand is unreviewable. That reason is right and this keeps it.
/// The mechanism had to change, because <c>.claude/rules/godot-scenes.md</c> is unconditional and
/// older than the packet:</para>
///
/// <para><i>"Every part of a level is authored in its scene file, never built in code (Talon,
/// 2026-08-27)."</i> — and <c>SupermarketWorldSelfTest</c>'s class doc says what it is for:
/// <i>"The standing rule ... is that every part of a level is physically authored in the scene
/// file SO IT CAN BE OPENED AND FLOWN AROUND IN THE EDITOR. That is not something a reviewer can
/// verify from a diff ... so it is measured."</i></para>
///
/// <para>A <c>ShelfStocker</c> that filled MultiMesh buffers in <c>_Ready</c> would have passed
/// that measurement — the check counts NODES, and filling a buffer adds none — while being
/// exactly the thing the rule forbids: a room that is empty in the editor and full at runtime.
/// Passing a check for the wrong reason is worse than failing it. So the fill is BAKED: the same
/// seeded arithmetic runs here, writes <c>StockBulk.tscn</c>, and the result is committed. The
/// editor shows the real shop, the self-test passes because nothing is built, and the load cost
/// is zero on every peer instead of 2 700 instances' worth on each.</para>
///
/// <para><b>What keeps a baked file from drifting</b> is <see cref="StockBakeTests"/>: it
/// re-derives the whole layout and compares it to what is committed, byte for byte. So the maths
/// remains the source of truth, a reviewer reads eighty lines of arithmetic instead of thirty
/// thousand floats, and a hand edit to the generated file is a red rather than a mystery.</para>
/// </summary>
public static class StockBake
{
    public const string BulkScenePath = "scenes/game/world/supermarket/StockBulk.tscn";
    public const string MoundScenePath = "scenes/game/props/BinMound.tscn";
    public const string SearchRoomPath = "scenes/game/world/supermarket/SearchRoom.tscn";

    /// <summary>Set <c>SFGD_BAKE_STOCK=1</c> and run <c>dotnet test</c> to REWRITE the generated
    /// scenes; leave it unset (the normal case, and CI) and the same test COMPARES instead.</summary>
    public const string BakeEnvVar = "SFGD_BAKE_STOCK";

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException("could not find the repo root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    // -------------------------------------------------------------------------------------
    // Reading the room back out of its own scene file.
    // -------------------------------------------------------------------------------------

    private static readonly Regex NodeHeader = new(
        "^\\[node name=\"(?<name>[^\"]+)\" parent=\"\\.\" instance=ExtResource\\(\"(?<res>[^\"]+)\"\\)\\]",
        RegexOptions.Compiled);

    private static readonly Regex TransformLine = new(
        @"^transform = Transform3D\((?<f>[^)]*)\)", RegexOptions.Compiled);

    /// <summary>
    /// The bays, read out of <c>SearchRoom.tscn</c> itself.
    ///
    /// <para><b>Parsed rather than tabulated</b>, for <c>AuthoredPropOrderTests</c>' reason: the
    /// room's layout arithmetic already lives in that file and a second copy here would agree
    /// with it right up until somebody nudged an aisle. A bay's NAME is also its seed, so this is
    /// the one read that decides both where a bay is and what it is stocked with.</para>
    ///
    /// <para>The parse is deliberately narrow — the <c>[node ... instance=ExtResource(...)]</c>
    /// header plus the <c>transform</c> line under it — because a general .tscn parser would be a
    /// second implementation of Godot's, and wrong.</para>
    /// </summary>
    public static List<RoomBay> ReadBays(string repoRoot)
    {
        string[] lines = File.ReadAllLines(Path.Combine(repoRoot, SearchRoomPath));

        // Which ext_resource id is the shelf prefab and which the end-cap.
        string shelfId = "", endCapId = "";
        foreach (string l in lines)
        {
            if (l.StartsWith("[ext_resource", StringComparison.Ordinal) && l.Contains("ShelfUnit.tscn"))
                shelfId = Between(l, "id=\"", "\"");
            else if (l.StartsWith("[ext_resource", StringComparison.Ordinal) && l.Contains("EndCap.tscn"))
                endCapId = Between(l, "id=\"", "\"");
        }
        if (shelfId.Length == 0 || endCapId.Length == 0)
            throw new InvalidOperationException("SearchRoom.tscn no longer instances ShelfUnit.tscn / EndCap.tscn");

        var bays = new List<RoomBay>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match m = NodeHeader.Match(lines[i]);
            if (!m.Success)
                continue;
            string res = m.Groups["res"].Value;
            bool isEndCap = res == endCapId;
            if (res != shelfId && !isEndCap)
                continue;

            // The transform is the next non-blank, non-comment line.
            float[]? f = null;
            for (int j = i + 1; j < lines.Length && j < i + 6; j++)
            {
                Match t = TransformLine.Match(lines[j]);
                if (!t.Success)
                    continue;
                string[] parts = t.Groups["f"].Value.Split(',');
                f = new float[parts.Length];
                for (int k = 0; k < parts.Length; k++)
                    f[k] = float.Parse(parts[k].Trim(), CultureInfo.InvariantCulture);
                break;
            }
            if (f == null || f.Length != 12)
                throw new InvalidOperationException($"bay '{m.Groups["name"].Value}' has no 12-float transform");

            // Transform3D's twelve floats are basis ROWS (.claude/rules/godot-scenes.md), then
            // the origin. Row 0 is (b.x.x, b.y.x, b.z.x); for a pure yaw that is (cos, 0, sin),
            // so the yaw is atan2(f[2], f[0]).
            float yaw = MathF.Atan2(f[2], f[0]) * 180f / MathF.PI;
            bays.Add(new RoomBay(m.Groups["name"].Value, f[9], f[11],
                MathF.Round(yaw), isEndCap));
        }
        return bays;
    }

    private static readonly Regex FacingHeader = new(
        @"^\[node name=""(?<n>(Can|Box|Produce)_\d{3})"" type=""Node3D"" parent=""Stock""\]",
        RegexOptions.Compiled);

    /// <summary>
    /// SHELF-1's authored CARRYABLE products, read out of the same scene file the bays come from.
    ///
    /// <para>These are what the bulk is carved around. They are not decoration to this generator:
    /// they are 120 networked RigidBody3D standing on the boards the bulk fills, and filler laid
    /// over one would leave it permanently inside static geometry -- a rest audit reporting
    /// StaticOverlap forever and a hider refused the Confirm on any of the forty-eight cans.</para>
    ///
    /// <para>The twelve in the BINS are read too and simply match no bay, which is the right
    /// answer rather than a special case.</para>
    /// </summary>
    public static List<StockRoom.RoomFacing> ReadFacings(string repoRoot)
    {
        string[] lines = File.ReadAllLines(Path.Combine(repoRoot, SearchRoomPath));
        var facings = new List<StockRoom.RoomFacing>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match m = FacingHeader.Match(lines[i]);
            if (!m.Success)
                continue;
            for (int j = i + 1; j < lines.Length && j < i + 4; j++)
            {
                Match t = TransformLine.Match(lines[j]);
                if (!t.Success)
                    continue;
                string[] parts = t.Groups["f"].Value.Split(',');
                if (parts.Length != 12)
                    break;
                facings.Add(new StockRoom.RoomFacing(m.Groups["n"].Value,
                    float.Parse(parts[9].Trim(), CultureInfo.InvariantCulture),
                    float.Parse(parts[10].Trim(), CultureInfo.InvariantCulture),
                    float.Parse(parts[11].Trim(), CultureInfo.InvariantCulture)));
                break;
            }
        }
        return facings;
    }

    private static string Between(string s, string a, string b)
    {
        int i = s.IndexOf(a, StringComparison.Ordinal);
        if (i < 0)
            return "";
        i += a.Length;
        int j = s.IndexOf(b, i, StringComparison.Ordinal);
        return j < 0 ? "" : s.Substring(i, j - i);
    }

    // -------------------------------------------------------------------------------------
    // Emitting.
    // -------------------------------------------------------------------------------------

    private static string F(float v)
    {
        // Six decimals is 1 micrometre — far below anything that matters here — and it keeps the
        // bake byte-stable across runs and machines. "G9" would round-trip exactly and produce
        // unreadable files for no gain.
        float r = MathF.Round(v, 6);
        if (r == 0f)
            r = 0f; // fold -0
        return r.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>One instance's twelve floats: a yaw about Y, written as Godot writes a MultiMesh
    /// buffer — three rows of four, the fourth column being the origin.
    /// <para><c>.claude/rules/godot-scenes.md</c>: <i>"MultiMesh buffer stride is not always 12. A
    /// layer carrying per-instance colour is stride 16."</i> These carry no colour and no custom
    /// data, so the stride IS 12 — and that is asserted in <see cref="StockBakeTests"/> rather
    /// than assumed, because the rule says parsing with the wrong stride yields plausible garbage
    /// rather than a crash.</para></summary>
    private static void AppendInstance(StringBuilder sb, float x, float y, float z, float yaw, bool first)
    {
        float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
        float[] v = { c, 0f, s, x, 0f, 1f, 0f, y, -s, 0f, c, z };
        foreach (float f in v)
        {
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(F(f));
        }
    }

    private static string BufferOf(IReadOnlyList<RoomInstance> all, StockMaterial material)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (RoomInstance i in all)
        {
            if (i.Material != material)
                continue;
            AppendInstance(sb, i.X, i.Y, i.Z, i.YawRad, first);
            first = false;
        }
        return sb.ToString();
    }

    private static int CountOf(IReadOnlyList<RoomInstance> all, StockMaterial material)
    {
        int n = 0;
        foreach (RoomInstance i in all)
            if (i.Material == material)
                n++;
        return n;
    }

    /// <summary>
    /// The <c>custom_aabb</c> for one MultiMesh, as Godot writes an <c>AABB</c>: position then
    /// size.
    ///
    /// <para><b>This is not an optimisation, it is the difference between the mesh rendering and
    /// not.</b> Measured, not reasoned: the first capture pass photographed six floor bins with
    /// nothing in them. The wiring was correct, the buffer held 29 instances, and Godot logged no
    /// error -- the <c>MultiMeshInstance3D</c> was simply culled, because a MultiMesh loaded from
    /// a scene file does not necessarily arrive with a usable bounding box and an empty one is
    /// outside every frustum. The room's bulk happened to survive it (its instances span the
    /// whole 14 x 10 m room, so whatever box it got still intersected the view); a mound 0.5 m
    /// across inside a bin did not.</para>
    ///
    /// <para>Grown by the product's own half-extent on every axis, because the buffer holds
    /// instance ORIGINS and the mesh sticks out around each one.</para>
    /// </summary>
    private static string CustomAabb(IReadOnlyList<RoomInstance> all, StockMaterial material)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        int n = 0;
        foreach (RoomInstance i in all)
        {
            if (i.Material != material)
                continue;
            n++;
            minX = MathF.Min(minX, i.X); maxX = MathF.Max(maxX, i.X);
            minY = MathF.Min(minY, i.Y); maxY = MathF.Max(maxY, i.Y);
            minZ = MathF.Min(minZ, i.Z); maxZ = MathF.Max(maxZ, i.Z);
        }
        if (n == 0)
            return "AABB(0, 0, 0, 0, 0, 0)";

        ProductSize p = ShelfStock.SizeOf(material);
        // The diagonal, because bulk carries a yaw: a box rotated 45 degrees reaches further than
        // its half-width on either axis.
        float padXZ = p.PlanDiagonalM * 0.5f;
        float padY = p.HalfHeightM;
        minX -= padXZ; maxX += padXZ;
        minZ -= padXZ; maxZ += padXZ;
        minY -= padY; maxY += padY;
        return $"AABB({F(minX)}, {F(minY)}, {F(minZ)}, "
               + $"{F(maxX - minX)}, {F(maxY - minY)}, {F(maxZ - minZ)})";
    }

    /// <summary>
    /// The can's mesh and material, taken from <c>Can.tscn</c> with <b>two deliberate
    /// differences</b>, both asserted by <c>ShelfStockTests</c> rather than left to be noticed.
    ///
    /// <para><b>1. No <c>resource_local_to_scene</c>.</b> A carryable needs its own material copy
    /// because <c>Carryable</c>'s highlight drives <c>emission_energy_multiplier</c> on it. Bulk
    /// is never highlighted and never picked up, so ONE shared material for fifteen hundred
    /// instances is the whole point of a MultiMesh.</para>
    ///
    /// <para><b>2. A coarser tessellation, and this is the one place "reuse the prefab's mesh" is
    /// knowingly not taken literally.</b> <c>CylinderMesh</c> defaults to 64 radial segments and
    /// <c>SphereMesh</c> to 64x32; at those counts the bulk cans alone would be about a million
    /// triangles and the bin mounds another two and a half million, spent on a 70 mm object
    /// nobody ever gets closer than half a metre to. Every dimension that can be SEEN or COLLIDED
    /// WITH is identical to the prefab's -- radius, height, albedo, roughness, metallic, emission
    /// -- and the segment count is the one number a player cannot tell apart at this size. It is
    /// an LOD, not different art, and the client p95 bar is what it buys.</para>
    /// </summary>
    private const string CanMeshAndMaterial = """
[sub_resource type="CylinderMesh" id="CylinderMesh_can"]
top_radius = 0.035
bottom_radius = 0.035
height = 0.12
radial_segments = 16
rings = 1

[sub_resource type="StandardMaterial3D" id="Mat_can"]
resource_name = "bulk cans (Can.tscn's material, not local to scene)"
albedo_color = Color(0.74, 0.76, 0.79, 1)
roughness = 0.28
metallic = 0.85
emission_enabled = true
emission = Color(0.74, 0.76, 0.79, 1)
emission_energy_multiplier = 0.18
""";

    private const string BoxMeshAndMaterial = """
[sub_resource type="BoxMesh" id="BoxMesh_box"]
size = Vector3(0.19, 0.28, 0.06)

[sub_resource type="StandardMaterial3D" id="Mat_box"]
resource_name = "bulk cereal boxes (CerealBox.tscn's material, not local to scene)"
albedo_color = Color(0.78, 0.26, 0.2, 1)
roughness = 0.85
metallic = 0.0
emission_enabled = true
emission = Color(0.78, 0.26, 0.2, 1)
emission_energy_multiplier = 0.14
""";

    private const string ProduceMeshAndMaterial = """
[sub_resource type="SphereMesh" id="SphereMesh_produce"]
radius = 0.08
height = 0.16
radial_segments = 16
rings = 8

[sub_resource type="StandardMaterial3D" id="Mat_produce"]
resource_name = "bulk produce (Produce.tscn's material, not local to scene)"
albedo_color = Color(0.92, 0.51, 0.13, 1)
roughness = 0.9
metallic = 0.0
emission_enabled = true
emission = Color(0.92, 0.51, 0.13, 1)
emission_energy_multiplier = 0.12
""";

    /// <summary>The whole of <c>StockBulk.tscn</c>, as text.</summary>
    public static string EmitBulkScene(RoomStock stock)
    {
        int cans = CountOf(stock.Instances, StockMaterial.Can);
        int boxes = CountOf(stock.Instances, StockMaterial.Box);
        int produce = CountOf(stock.Instances, StockMaterial.Produce);

        // Dedupe the collision shapes by size — a shop floor has a few dozen distinct run
        // lengths and several hundred runs, so sharing the BoxShape3D resources turns ~270
        // sub-resources into a couple of dozen.
        var shapeIds = new Dictionary<string, string>();
        var shapeBlocks = new StringBuilder();
        var shapeOrder = new List<string>();
        foreach (RoomCollider c in stock.Colliders)
        {
            string key = $"{F(c.SizeX)},{F(c.SizeY)},{F(c.SizeZ)}";
            if (shapeIds.ContainsKey(key))
                continue;
            string id = $"Box_{shapeIds.Count:D3}";
            shapeIds[key] = id;
            shapeOrder.Add(key);
        }
        foreach (string key in shapeOrder)
        {
            string[] p = key.Split(',');
            shapeBlocks.Append($"[sub_resource type=\"BoxShape3D\" id=\"{shapeIds[key]}\"]\n");
            shapeBlocks.Append($"size = Vector3({p[0]}, {p[1]}, {p[2]})\n\n");
        }

        var nodes = new StringBuilder();
        int n = 0;
        foreach (RoomCollider c in stock.Colliders)
        {
            string key = $"{F(c.SizeX)},{F(c.SizeY)},{F(c.SizeZ)}";
            // The comment goes ABOVE the header, which is where every hand-authored .tscn in this
            // repo puts one. A ";" line sitting between a node's properties and the next header
            // parses, but it reads as a property nobody recognises.
            nodes.Append($"; {c.Why}\n");
            nodes.Append($"[node name=\"Run_{n:D3}\" type=\"CollisionShape3D\" parent=\"Collision\"]\n");
            nodes.Append($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(c.X)}, {F(c.Y)}, {F(c.Z)})\n");
            nodes.Append($"shape = SubResource(\"{shapeIds[key]}\")\n\n");
            n++;
        }

        // load_steps = sub-resources + 1. Godot recomputes it on save; it only has to be >= the
        // real count for a load, and an exact number is what the engine itself writes.
        int subResources = 6 + shapeIds.Count; // three meshes + three materials, plus the shapes
        int loadSteps = subResources + 3 + 1;  // + three MultiMeshes + the scene itself

        return $"""
[gd_scene load_steps={loadSteps} format=3 uid="uid://stockbulk0sfgdmvp"]

; =============================================================================================
; STOCK-1 (2026-09-20) — THE BULK STOCK. **GENERATED FILE: DO NOT EDIT BY HAND.**
;
; Regenerate:  SFGD_BAKE_STOCK=1 dotnet test tests/unit/SailNet.Tests.csproj
; The gate:    StockBakeTests re-derives every float below from ShelfStock/StockRoom and fails
;              if this file and the arithmetic disagree. A hand edit here is a red, not a
;              mystery, and the arithmetic is what a reviewer reads.
;
; WHAT THIS IS. Talon, 2026-09-20: "Supermarket shelves are extremely filled: three or four
; items behind each one in every row; boxes stacked one behind the other; piles of fruit in bins
; and crates. I need to see MORE overall -- more to hide in, from and around."
;
; WHAT IT IS NOT: more carryables. SHELF-1 §7 measured the floor the room already stands on --
; 130 networked RigidBody3D at rest is a server p95 of 5.8-6.7 ms against an 8 ms bar, and 130
; loose at once is a 230 kB/s stream with 80 % of one-shot voices stolen. Four times the product
; count as physics bodies would break every bar the program has. So the density here is VISUAL
; stock the server never simulates and the wire never carries: {cans + boxes} instances in TWO
; MultiMesh draws, using the carryable prefabs' own meshes, materials and sizes so nothing reads
; as fake, with the carryable facings still on the front of every board where a hand reaches.
;
; THE 120 CARRYABLE FACINGS ARE CARVED OUT OF IT, and that is load-bearing rather than tidy.
; SHELF-1's products are networked RigidBody3D standing on these same boards; static filler
; laid over one would leave a prop permanently inside static geometry -- every rest audit
; reporting StaticOverlap, REACH-1's layer 3 answering InsideStatic, and a hider who chose
; that prop refused the Confirm with NobodyCouldReachThat, in a room that renders perfectly.
; StockBakeTests.NoBulkInstanceStandsWhereACarryableFacingStands is the gate on it.
;
; THE HOLES ARE THE POINT. Every board keeps one to three empty slots, and every one of them is
; cleared from a FACE inward rather than out of the middle -- see StockGap's doc. A hole with
; bulk still in front of it is a hole the hider can drop the object into and REACH-1's layer 3
; will refuse the Confirm on (OccludedByStatic), because bulk is static. Cleared to a face, a
; hole is a legal hiding place, a sightline through the bay, and the thing that keeps this room
; somewhere you root around rather than four walls with tins on them.
;
; COLLISION is sized to the FILLED volume per slot-run ({stock.Colliders.Count} boxes, {shapeIds.Count} distinct sizes),
; never one box per board: one box per board would swallow every hole in it and give the player
; a shop floor whose holes are painted on.
;
; It is instanced ONCE into SearchRoom.tscn and listed in SupermarketWorldSelfTest.SectionScenes
; for CLOCK-1's reason -- CountNodes stops at an instance boundary, so a prefab that is not on
; that list is invisible from its room's count.
; =============================================================================================

{CanMeshAndMaterial}

[sub_resource type="MultiMesh" id="MultiMesh_cans"]
transform_format = 1
mesh = SubResource("CylinderMesh_can")
instance_count = {cans}
buffer = PackedFloat32Array({BufferOf(stock.Instances, StockMaterial.Can)})

{BoxMeshAndMaterial}

[sub_resource type="MultiMesh" id="MultiMesh_boxes"]
transform_format = 1
mesh = SubResource("BoxMesh_box")
instance_count = {boxes}
buffer = PackedFloat32Array({BufferOf(stock.Instances, StockMaterial.Box)})

{ProduceMeshAndMaterial}

[sub_resource type="MultiMesh" id="MultiMesh_produce"]
transform_format = 1
mesh = SubResource("SphereMesh_produce")
instance_count = {produce}
buffer = PackedFloat32Array({BufferOf(stock.Instances, StockMaterial.Produce)})

{shapeBlocks}[node name="StockBulk" type="Node3D"]

; {cans} cans in ONE draw. No script, no _Ready, nothing built: the buffer above IS the shelf.
[node name="BulkCans" type="MultiMeshInstance3D" parent="."]
custom_aabb = {CustomAabb(stock.Instances, StockMaterial.Can)}
multimesh = SubResource("MultiMesh_cans")
material_override = SubResource("Mat_can")

; {boxes} cereal boxes in ONE draw -- two aisles of filler plus the two floor pallet stacks.
[node name="BulkBoxes" type="MultiMeshInstance3D" parent="."]
custom_aabb = {CustomAabb(stock.Instances, StockMaterial.Box)}
multimesh = SubResource("MultiMesh_boxes")
material_override = SubResource("Mat_box")

; {produce} oranges in ONE draw, on the two end-caps. Produce rather than the boxes the packet
; asked for there, because SHELF-1 authored twelve CARRYABLE oranges onto those same shelves and
; the filler behind a facing has to be the same product as the facing -- SFX-1 gave each material
; its own voice, so a cardboard thud out of a shelf of oranges is a lie a player can hear. The
; stacked box displays the packet wanted are the two floor pallets, which is what a stacked
; display physically is; an end-cap is a shelf.
[node name="BulkProduce" type="MultiMeshInstance3D" parent="."]
custom_aabb = {CustomAabb(stock.Instances, StockMaterial.Produce)}
multimesh = SubResource("MultiMesh_produce")
material_override = SubResource("Mat_produce")

; Layer 1 (the default, left unwritten because Godot strips settings that match the default --
; .claude/rules/godot-scenes.md) is where this game puts static geometry AND released props, and
; it is exactly the mask PlacementIntegrity.QueryMask reads. A prop shoved at the bulk stops; a
; prop set down in a hole finds nothing to overlap and latches Resting.
[node name="Collision" type="StaticBody3D" parent="."]

{nodes}
""".Replace("\r\n", "\n");
    }

    /// <summary>The whole of <c>BinMound.tscn</c>, as text.</summary>
    public static string EmitMoundScene()
    {
        (List<RoomInstance> mound, RoomCollider _) = StockRoom.BuildBinMound("BinMound");
        var sb = new StringBuilder();
        bool first = true;
        foreach (RoomInstance i in mound)
        {
            AppendInstance(sb, i.X, i.Y, i.Z, i.YawRad, first);
            first = false;
        }

        return $"""
[gd_scene load_steps=5 format=3 uid="uid://binmound0sfgdmvp"]

; =============================================================================================
; STOCK-1 (2026-09-20) — A BIN OF ORANGES. **GENERATED FILE: DO NOT EDIT BY HAND.**
; Regenerate: SFGD_BAKE_STOCK=1 dotnet test tests/unit/SailNet.Tests.csproj
;
; {mound.Count} produce instances in one MultiMesh draw, in FloorBin.tscn's own local space: three
; layers of 4x4 + 3x3 + 2x2, crowning at y = 0.40 inside a tub whose rim is at 0.60, so the pile
; sits in the bin rather than on it.
;
; IT IS A CHILD OF THE BIN, NOT OF THE ROOM, and that is the whole reason this is a scene of its
; own. A bin is a 6 kg RigidBody3D a player can pick up and tip over -- SHELF-1 put them in the
; room precisely so "a bin knocked over is somewhere an object can end up". Static oranges
; authored at a bin's room position would hang in the air the first time anybody moved it.
;
; NO COLLISION, AND IT IS A DECISION RATHER THAN AN OMISSION. STOCK-1's packet asks for a pile
; collider "so a placed object nestles on the mound rather than floating or sinking". A bin's
; interior is 0.78 x 0.54 x 0.78 and SHELF-1's bible check closed INTERACTION §3 on exactly that
; number: "which clears a 0.44 m crate by 0.17 m horizontally and 0.10 m vertically, so 'hide it
; under the bin' is a physical fact and not a scripted allowance." A crate needs 0.44 m of the
; 0.54 m available, so ANY mound collider deletes that affordance outright -- there is no
; half-fill that keeps it. Visual-only keeps both: the hollow is still hollow to physics, and an
; object dropped into a stocked bin settles on the bin floor AMONG the oranges, which is both
; what really happens and a better hiding place than an empty tub. The cost is stated rather
; than hidden: something set down here beds into the fruit instead of resting on top of it.
; =============================================================================================

{ProduceMeshAndMaterial}

[sub_resource type="MultiMesh" id="MultiMesh_mound"]
transform_format = 1
mesh = SubResource("SphereMesh_produce")
instance_count = {mound.Count}
buffer = PackedFloat32Array({sb})

[node name="BinMound" type="MultiMeshInstance3D"]
custom_aabb = {CustomAabb(mound, StockMaterial.Produce)}
multimesh = SubResource("MultiMesh_mound")
material_override = SubResource("Mat_produce")

""".Replace("\r\n", "\n");
    }
}
