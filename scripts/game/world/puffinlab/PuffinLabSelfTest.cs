using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;

namespace Sail.Game.World.PuffinLab;

/// <summary>
/// <b>EGG-1's compliance test: the throwback loads, cannot stop the session, and can actually be
/// walked out of.</b> <c>godot --headless --path . -- --puffinlab-selftest</c>; exits 0/1.
///
/// <para><b>The three things it exists to catch, in order of how badly they would hurt.</b></para>
///
/// <list type="number">
/// <item><b>A route the player physically does not fit down.</b> The source lab was authored
/// around a 0.9 m avatar and its tiny door is a 1.05 m hole; Sail's avatar is measured off its own
/// model at runtime and is taller. At 1:1 the black room — and the return television standing in
/// it — is unreachable, and the symptom is a player wedged in a doorway 20 m underground with no
/// way back. <see cref="CheckClearance"/> puts the LIVE avatar's own capsule at every station of
/// the route and requires the physics space to report it free. It carries a positive control:
/// the same query inside the floor slab must come back solid, because a clearance check that is
/// quietly asking an empty space passes everything.</item>
/// <item><b>Anything that can end, pause or black out the session.</b>
/// <see cref="CheckNoScreenCover"/> walks the PACKED state of all three scene files and the live
/// tree for <c>CanvasLayer</c>, <c>ColorRect</c>, <c>Control</c>, <c>WorldEnvironment</c> and
/// <c>Camera3D</c> and requires none. Two of those really were in the source data — a full-screen
/// vignette CanvasLayer and a WorldEnvironment, both in <c>LabAuthored.tscn</c> — so this is a
/// regression guard, not a formality. It carries its own positive control for the same reason
/// the clearance check does.</item>
/// <item><b>A seam EGG-2 cannot read.</b> <see cref="CheckSeam"/> asserts the two contract nodes
/// by node path and exact type, not by eye, and asserts that exactly one portal type is present
/// and that it is Sail's own <see cref="TvPortal"/> — a second portal class would leave
/// <c>TvPortalHost</c> subscribing to one of them and silently ignoring the other.</item>
/// </list>
///
/// <para><b>What it deliberately does not do.</b> It does not walk the 100 m of route with a
/// scripted brain. The property that matters is <i>can the body occupy every point of the
/// route</i>, and a capsule query answers that directly and deterministically; a scripted walk
/// answers it indirectly, takes a minute of suite time, and fails for reasons (a slope, a gait, a
/// step height) that are the motor's business and not this scene's. The walk is what the headed
/// capture in <c>docs/qa/EGG-1/</c> shows.</para>
/// </summary>
public sealed partial class PuffinLabSelfTest : Node3D
{
    public const string ScenePath = "res://scenes/game/world/puffinlab/PuffinLab.tscn";

    /// <summary>Lift the query capsule off the floor before asking whether it fits. A body
    /// standing on a surface is exactly tangent to it, and an exact-tangency overlap is a coin
    /// flip in any broadphase — 2 cm turns "resting on the floor" into an unambiguous miss without
    /// coming close to hiding a real 1 cm ceiling.</summary>
    private const float StandLiftM = 0.02f;

    /// <summary>Shrink the query capsule by this much on every axis. The route stations are the
    /// authored centreline of a crawl that was authored to fit, not surveyed clearances, so a
    /// station may sit a centimetre off true centre; without a margin this check would report a
    /// blocked tunnel for a body that walks it fine. Kept small enough that a real obstruction —
    /// a wall, a header, a crate — is many times larger than it.</summary>
    private const float FitMarginM = 0.03f;

    /// <summary>How far above and below a route station the floor is searched for. 1.5 m: more
    /// than the slope error between the authored crawl centreline and its real ribbon surface,
    /// less than the height of any room on the route, so it cannot find the floor BELOW the one
    /// the player would be standing on.</summary>
    private const float GroundSearchM = 1.5f;

    /// <summary>How far up a station's ceiling is searched for. Nothing on the route is taller
    /// than the lab room itself, so a miss at this range means "open above", not "unmeasured".
    /// Headroom is the question a crawl and a 1.05 m door actually pose, and unlike a capsule
    /// overlap it is immune to the slope the crawl descends at.</summary>
    private const float CeilingSearchM = 6f;

    /// <summary>Physics needs one tick before a body added this frame is in the space; a shape
    /// query run before it reports an empty world, which is the failure the positive control
    /// exists to catch.</summary>
    private const double PhysicsSettleSeconds = 0.25;

    /// <summary>How long to wait for <c>BodyAtScreen</c> after parking a body in the return TV's
    /// trigger. Several physics frames; the signal either fires immediately or never.</summary>
    private const double TriggerProbeSeconds = 0.4;

    /// <summary>The route through the throwback, in the SOURCE's own lab coordinates — the same
    /// numbers <c>EscapeEnvironment.TunnelPath</c> and the authored rooms are written in, so this
    /// list can be read against mp-foundation directly. They are converted to world space through
    /// the <c>Lab</c> node's transform, which is where the port's scale lives, so this array never
    /// has to know what that scale is.</summary>
    private static readonly (string Name, Vector3 Local)[] Route =
    {
        ("arrival",          new Vector3(-5.6f,  0.00f,   2.40f)),
        ("lab centre",       new Vector3( 0.0f,  0.00f,   0.00f)),
        ("approach to hole", new Vector3( 7.45f, 0.00f,  -4.00f)),
        ("hole mouth",       new Vector3( 7.45f, 0.00f,  -6.10f)),
        ("crawl 1",          new Vector3( 7.45f,-0.12f,  -7.80f)),
        ("crawl 2",          new Vector3( 7.62f,-0.95f,  -9.90f)),
        ("crawl 3 (bend)",   new Vector3( 8.55f,-1.85f, -11.70f)),
        ("crawl 4",          new Vector3(10.60f,-2.85f, -12.85f)),
        ("crawl 5",          new Vector3(13.50f,-3.95f, -13.00f)),
        ("crawl 6",          new Vector3(16.50f,-4.72f, -13.00f)),
        ("hallway mouth",    new Vector3(19.60f,-5.00f, -13.00f)),
        ("hallway 1",        new Vector3(28.00f,-5.00f, -13.00f)),
        ("hallway 2",        new Vector3(38.00f,-5.00f, -13.00f)),
        ("hallway 3",        new Vector3(48.00f,-5.00f, -13.00f)),
        ("outside tiny door",new Vector3(56.50f,-5.00f, -12.10f)),
        ("tiny doorway",     new Vector3(56.50f,-5.00f, -11.35f)),
        ("black room",       new Vector3(56.50f,-5.00f, -10.00f)),
        ("beside the stool", new Vector3(54.80f,-5.00f,  -7.40f)),
        ("in front of the TV", new Vector3(58.60f,-5.00f, -5.60f)),
    };

    /// <summary>Where the headed capture stands and what it looks at, in the SOURCE's lab
    /// coordinates like <see cref="Route"/>, plus the caption that goes in the index. Six frames:
    /// the arrival read, the room, the way on, the crawl, the tiny door, and the way out.</summary>
    private static readonly (string File, string Caption, Vector3 Eye, Vector3 At)[] Vantages =
    {
        ("01-arrival.png",
         "What a traveller sees on arrival: standing at Arrival, facing -Z down the room's long "
         + "axis — cage racks, the fluorescent bank, and the green of the torn wall in the far corner.",
         new Vector3(-5.6f, 0.9f, 2.4f), new Vector3(-3.0f, 0.6f, -5.5f)),
        ("02-the-lab.png",
         "The lab from its west end: the observation gallery glass the lurker crosses behind, the "
         + "door and hallway east, the whole sealed room the throwback opens in.",
         new Vector3(-7.2f, 2.6f, 4.6f), new Vector3(3.0f, 1.0f, 0.0f)),
        ("03-the-hole.png",
         "The way on: the torn hole low in the north-east corner, breathing green (GlowBreath), "
         + "with the mutation chamber's fluid on the same curve beside it.",
         new Vector3(4.6f, 1.4f, -2.6f), new Vector3(7.45f, 0.2f, -6.1f)),
        ("04-the-crawl.png",
         "Inside the descending crawl, looking back up at the bend — the noise-displaced organic "
         + "tube, and the measured 2.4-2.6 m of headroom the fit check reports.",
         new Vector3(13.5f, -3.2f, -13.0f), new Vector3(8.0f, -1.2f, -10.5f)),
        ("05-the-tiny-door.png",
         "Horror-gate #2 at the end of the long hallway: the puffin-sized door, standing open. "
         + "1.45 m of headroom against a 1.20 m avatar — at 1:1 it would have been 1.05 m and "
         + "the room beyond unreachable.",
         new Vector3(56.5f, -4.1f, -14.6f), new Vector3(56.5f, -4.5f, -11.35f)),
        ("06-the-way-out.png",
         "The black room, from the tiny door: the stool and the object under the key light, and "
         + "ReturnTv — the second television standing where the old fade-to-black used to fire.",
         new Vector3(56.5f, -4.3f, -10.9f), new Vector3(58.2f, -4.5f, -5.0f)),
    };

    private readonly List<string> _failures = new();

    private Node3D _lab = null!;
    private PuffinLabRoom _room = null!;
    private TvPortal _returnTv = null!;
    private SandboxAvatar _probe = null!;
    private bool _bodyAtScreenFired;

    /// <summary>Set from <c>--capture-dir</c>. Non-empty turns this run into the headed evidence
    /// rig: the checks still run, and then six stills are written before the quit. Empty (the
    /// suite's case) skips it entirely, so the marathon never needs a window.</summary>
    private string _captureDir = "";

    public override void _Ready()
    {
        // Read from the command line directly, the same way UiCaptureLab does, rather than being
        // handed a value: this node is constructed by Boot on a flag and has no other wiring.
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--capture-dir")
                _captureDir = args[i + 1];
        CallDeferred(nameof(Run));
    }

    private void Run()
    {
        GD.Print("[puffinlab-selftest] the seam, the absence checks, and whether the route fits");

        var packed = GD.Load<PackedScene>(ScenePath);
        if (packed is null)
        {
            Fail($"could not load {ScenePath}");
            Finish();
            return;
        }

        Node instance = packed.Instantiate();
        if (instance is not PuffinLabRoom room)
        {
            Fail($"{ScenePath}'s root is a {instance.GetType().Name}, not a "
                 + $"{nameof(PuffinLabRoom)} — the scene's own script did not load.");
            Finish();
            return;
        }
        _room = room;
        AddChild(_room);

        CheckSeam();
        CheckNoScreenCover();
        CheckOverlayControl();

        if (_failures.Count > 0)
        {
            Finish();
            return;
        }

        _lab = _room.GetNode<Node3D>(PuffinLabRoom.LabName);
        _probe = new SandboxAvatar { Name = "FitProbe" };
        AddChild(_probe);
        _probe.GlobalPosition = _lab.ToGlobal(Route[0].Local);

        GetTree().CreateTimer(PhysicsSettleSeconds).Timeout += MeasureThenProbeTrigger;
    }

    // --- The seam --------------------------------------------------------------------------

    /// <summary>The contract EGG-2 reads, asserted by node path and exact type. "Verified by node
    /// path, not by eye" is the packet's own wording, and the reason is that a marker renamed or
    /// re-parented one level deeper still looks completely right in the editor.</summary>
    private void CheckSeam()
    {
        var arrival = _room.GetNodeOrNull<Marker3D>(PuffinLabRoom.ArrivalName);
        Check(arrival is not null,
            $"no Marker3D at '{PuffinLabRoom.ArrivalName}' directly under the scene root. That is "
            + "where a traveller is placed; without it the level has to guess a spawn point.");

        var tv = _room.GetNodeOrNull<TvPortal>(PuffinLabRoom.ReturnTvName);
        Check(tv is not null,
            $"no TvPortal at '{PuffinLabRoom.ReturnTvName}' directly under the scene root. That is "
            + "the way out of a room 20 m underground.");
        if (tv is null)
            return;
        _returnTv = tv;

        // ABSENCE CHECK, criterion 6: Sail's TvPortal and no other portal type. mp-foundation has
        // its own TvPortal class; porting that one instead would give the world two portal types,
        // and TvPortalHost's tree walk only finds the type it was compiled against — so the second
        // kind would be a television that silently does nothing.
        Check(tv.GetType().FullName == "MpFoundation.Game.World.TvPortal",
            $"'{PuffinLabRoom.ReturnTvName}' is a {tv.GetType().FullName}, not Sail's own "
            + "MpFoundation.Game.World.TvPortal. TvPortalHost only subscribes to the type it was "
            + "compiled against.");

        var portals = new List<TvPortal>();
        CollectByType(_room, portals);
        Check(portals.Count == 1,
            $"the scene holds {portals.Count} TvPortals; it must hold exactly one — the way back. "
            + "A second one would be an unwired television or a second route EGG-2 does not know "
            + "about.");

        GD.Print($"[puffinlab-selftest]   seam OK — Arrival at {arrival!.GlobalPosition}, "
                 + $"ReturnTv ({tv.GetType().FullName}) at {tv.GlobalPosition}");
    }

    // --- Absence check: nothing may cover, end, or pause the screen -------------------------

    /// <summary>Types that draw over the whole viewport, own the world's environment, or take the
    /// camera. None of them belongs in a room instanced inside a running level: a
    /// <c>CanvasLayer</c> draws over the entire game rather than over the room it was authored in,
    /// a second <c>WorldEnvironment</c> fights the level's declared sky writer, and a
    /// <c>Camera3D</c> that becomes current takes the player's view away.</summary>
    private static readonly string[] ForbiddenTypes =
    {
        "CanvasLayer", "ColorRect", "Control", "WorldEnvironment", "Camera3D",
    };

    /// <summary>Walk the PACKED state of every scene file the throwback is made of, and the live
    /// tree, for <see cref="ForbiddenTypes"/>. Packed AND live because the two catch different
    /// mistakes: packed catches an overlay authored into the data (which is what
    /// <c>LabAuthored.tscn</c>'s vignette was), live catches one a script adds in <c>_Ready</c>
    /// (which is what <c>EscapeHost</c>'s fade layer was).</summary>
    private void CheckNoScreenCover()
    {
        foreach (string path in new[]
                 {
                     ScenePath,
                     "res://scenes/game/world/puffinlab/LabRoom.tscn",
                     "res://scenes/game/world/puffinlab/EscapeRoute.tscn",
                 })
        {
            var scene = GD.Load<PackedScene>(path);
            if (scene is null)
            {
                Fail($"could not load {path}");
                continue;
            }
            List<string> found = FindPackedTypes(scene, ForbiddenTypes);
            Check(found.Count == 0,
                $"{path} authors {found.Count} screen-covering node(s): {string.Join(", ", found)}. "
                + "A CanvasLayer in a room scene draws over the whole game, everywhere, for the "
                + "rest of the session.");
        }

        List<string> live = FindLiveTypes(_room, ForbiddenTypes);
        Check(live.Count == 0,
            $"the live scene grew {live.Count} screen-covering node(s) at runtime: "
            + $"{string.Join(", ", live)}. Something in _Ready is building an overlay.");

        GD.Print("[puffinlab-selftest]   no CanvasLayer / ColorRect / Control / WorldEnvironment / "
                 + "Camera3D, packed or live");
    }

    /// <summary><b>The positive control for the check above.</b> An absence check that is looking
    /// in the wrong place passes everything, forever, silently. This builds a throwaway subtree
    /// holding one CanvasLayer with one ColorRect and requires the same walker to find them; if it
    /// does not, every green run of <see cref="CheckNoScreenCover"/> was worthless.</summary>
    private void CheckOverlayControl()
    {
        var control = new Node3D { Name = "OverlayControl" };
        var layer = new CanvasLayer { Name = "ControlLayer" };
        layer.AddChild(new ColorRect { Name = "ControlRect" });
        control.AddChild(layer);
        AddChild(control);

        List<string> found = FindLiveTypes(control, ForbiddenTypes);
        Check(found.Count == 2,
            $"the overlay walker found {found.Count} node(s) in a control subtree that contains "
            + "exactly one CanvasLayer and one ColorRect. The walker is broken, so every clean "
            + "result it has ever returned means nothing.");
        GD.Print($"[puffinlab-selftest]   overlay walker positive control: found "
                 + $"{found.Count}/2 planted node(s)");

        control.QueueFree();
    }

    // --- Clearance: can the body Sail actually ships occupy every point of the route ---------

    private void MeasureThenProbeTrigger()
    {
        CheckClearance();
        StartTriggerProbe();
    }

    private void CheckClearance()
    {
        AvatarProportions p = _probe.Proportions;
        float radius = Mathf.Max(0.01f, p.CapsuleRadiusM - FitMarginM);
        float height = Mathf.Max(radius * 2f, p.CapsuleHeightM - FitMarginM * 2f);
        GD.Print($"[puffinlab-selftest]   live avatar: crown {p.CrownM:F3} m, capsule "
                 + $"r {p.CapsuleRadiusM:F3} m h {p.CapsuleHeightM:F3} m "
                 + $"(query capsule r {radius:F3} h {height:F3}, {FitMarginM * 100f:F0} cm margin)");

        var shape = new CapsuleShape3D { Radius = radius, Height = height };
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

        // POSITIVE CONTROL FIRST. The lab floor slab is 0.4 m thick with its top at local y = 0,
        // so a capsule centred at y = -0.2 is inside solid geometry. If this comes back free, the
        // query is not reaching the world and every "fits" below is a false green.
        int inSlab = Overlaps(space, shape, _lab.ToGlobal(new Vector3(0f, -0.2f, 0f)), null).Count;
        Check(inSlab > 0,
            "the clearance probe reports the inside of the lab's own floor slab as empty space. "
            + "The physics query is not seeing the world, so no 'this fits' result below can be "
            + "believed.");
        GD.Print($"[puffinlab-selftest]   clearance positive control: {inSlab} body/bodies inside "
                 + "the floor slab (expected > 0)");

        // The probe body is itself on layer 1 and is standing on the route; without excluding it
        // the query would report the station it happens to be resting at as blocked by the very
        // body whose fit is being measured.
        var exclude = new Godot.Collections.Array<Rid> { _probe.GetRid() };

        int blocked = 0;
        int floorless = 0;
        foreach ((string name, Vector3 local) in Route)
        {
            // Stand on the surface a ray FINDS, never on the authored y. The source's TunnelPath
            // is documented as "the crawl centreline at floor level", but the crawl's walkable
            // ribbon is a sloped trimesh, so at the two steepest stations the real floor is a
            // handful of centimetres above the centreline number and a capsule placed on the
            // number is buried in it. That is a bad waypoint, not a blocked route — and taking
            // the measured surface is the same discipline BubbleTestSelfTest.CheckPropHeights
            // uses, for the same reason: a literal height is a second copy of the world.
            Vector3 above = _lab.ToGlobal(local) + new Vector3(0f, GroundSearchM, 0f);
            Vector3 below = _lab.ToGlobal(local) - new Vector3(0f, GroundSearchM, 0f);
            var ray = new PhysicsRayQueryParameters3D
            {
                From = above, To = below, CollisionMask = 1, Exclude = exclude,
            };
            Godot.Collections.Dictionary ground = space.IntersectRay(ray);
            if (ground.Count == 0)
            {
                floorless++;
                GD.Print($"[puffinlab-selftest]     NO FLOOR {name,-20} local {local}");
                continue;
            }
            Vector3 floor = ground["position"].As<Vector3>();

            // THE GROUND YOU ARE STANDING ON IS NOT AN OBSTRUCTION, and on a slope it looks like
            // one. The crawl descends at ~24 degrees, and a vertical capsule resting on a slope
            // always clips the uphill side of the surface it is standing on — the first version of
            // this check reported the tunnel blocked by its own floor at exactly the two steepest
            // stations. So the ground body is excluded from the overlap query and the vertical
            // question is asked separately, by measuring headroom.
            var standingExclude = new Godot.Collections.Array<Rid>(exclude);
            if (ground.TryGetValue("rid", out Variant groundRid))
                standingExclude.Add(groundRid.As<Rid>());

            float headroom = Headroom(space, floor, standingExclude);
            if (headroom < height)
            {
                blocked++;
                GD.Print($"[puffinlab-selftest]     TOO LOW  {name,-20} floor y {floor.Y:F2}, "
                         + $"headroom {headroom:F2} m < {height:F2} m of body");
                continue;
            }

            Vector3 stand = floor + new Vector3(0f, StandLiftM + height * 0.5f, 0f);
            List<string> hits = Overlaps(space, shape, stand, standingExclude);
            if (hits.Count > 0)
            {
                blocked++;
                GD.Print($"[puffinlab-selftest]     BLOCKED  {name,-20} floor y {floor.Y:F2}, "
                         + $"headroom {headroom:F2} m -> " + string.Join(", ", hits));
                continue;
            }
            GD.Print($"[puffinlab-selftest]     fits     {name,-20} floor y {floor.Y:F2}, "
                     + $"headroom {(headroom >= CeilingSearchM ? "open" : $"{headroom:F2} m")}");
        }
        Check(floorless == 0,
            $"{floorless} of {Route.Length} station(s) on the route have no floor under them "
            + "within a metre and a half. Either the route is wrong or the room has a hole in it.");
        Check(blocked == 0,
            $"{blocked} of {Route.Length} station(s) on the route cannot hold the body Sail "
            + "actually ships. The throwback's scale is wrong for this avatar, and the return "
            + "television is behind the blockage.");
        GD.Print($"[puffinlab-selftest]   route clearance: {Route.Length - blocked}/{Route.Length} "
                 + "stations free");
    }

    /// <summary>Distance from a floor point to whatever is directly above it, or
    /// <see cref="CeilingSearchM"/> when nothing is. Started a few centimetres up so the ray does
    /// not re-hit the surface it starts on.</summary>
    private static float Headroom(PhysicsDirectSpaceState3D space, Vector3 floor,
        Godot.Collections.Array<Rid> exclude)
    {
        Vector3 from = floor + new Vector3(0f, 0.05f, 0f);
        var ray = new PhysicsRayQueryParameters3D
        {
            From = from,
            To = floor + new Vector3(0f, CeilingSearchM, 0f),
            CollisionMask = 1,
            Exclude = exclude,
        };
        Godot.Collections.Dictionary hit = space.IntersectRay(ray);
        return hit.Count == 0
            ? CeilingSearchM
            : hit["position"].As<Vector3>().Y - floor.Y;
    }

    /// <summary>What, by name, is standing in the way at <paramref name="at"/>. Names rather than
    /// a count because "blocked" on its own sends the reader back into the editor to find out by
    /// what — and the answer ("the floor" vs "a crate" vs "the door header") is the difference
    /// between a bad waypoint and a wrong scale.</summary>
    private static List<string> Overlaps(PhysicsDirectSpaceState3D space, Shape3D shape, Vector3 at,
        Godot.Collections.Array<Rid>? exclude)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity, at),
            CollisionMask = 1,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        if (exclude is not null)
            query.Exclude = exclude;

        var names = new List<string>();
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, maxResults: 8))
        {
            names.Add(hit.TryGetValue("collider", out Variant c) && c.As<Node>() is { } node
                ? node.GetPath().ToString()
                : "<unnamed body>");
        }
        return names;
    }

    // --- The way out is live -----------------------------------------------------------------

    /// <summary>Park a real body in the return TV's own trigger and require the portal to raise
    /// <c>BodyAtScreen</c>. This is the half a geometry check cannot reach: a trigger on the wrong
    /// collision mask, or sitting behind the cabinet hull, leaves a television that looks perfect
    /// and does nothing. Who the event teleports the player TO is EGG-2's wiring and deliberately
    /// not asserted here.</summary>
    private void StartTriggerProbe()
    {
        _returnTv.BodyAtScreen += _ => _bodyAtScreenFired = true;
        _probe.GlobalPosition = _returnTv.ToGlobal(_returnTv.TriggerPosition);
        GD.Print($"[puffinlab-selftest]   parking the probe in ReturnTv's trigger at "
                 + $"{_probe.GlobalPosition}");
        GetTree().CreateTimer(TriggerProbeSeconds).Timeout += AfterTriggerProbe;
    }

    private void AfterTriggerProbe()
    {
        Check(_bodyAtScreenFired,
            "a body standing in ReturnTv's screen trigger did not raise BodyAtScreen. The way out "
            + "of the throwback is inert, and a player who walks into it is stuck underground.");
        GD.Print($"[puffinlab-selftest]   ReturnTv raised BodyAtScreen: {_bodyAtScreenFired}");
        Finish();
    }

    // --- Plumbing ------------------------------------------------------------------------------

    private static List<string> FindPackedTypes(PackedScene scene, string[] types)
    {
        var found = new List<string>();
        Walk(scene, "");
        return found;

        void Walk(PackedScene s, string prefix)
        {
            SceneState state = s.GetState();
            for (int i = 0; i < state.GetNodeCount(); i++)
            {
                string type = state.GetNodeType(i);
                if (type.Length == 0)
                {
                    // An instanced child: its own type lives in the scene it points at.
                    if (state.GetNodeInstance(i) is { } nested)
                        Walk(nested, $"{prefix}{state.GetNodeName(i)}/");
                    continue;
                }
                if (System.Array.IndexOf(types, type) >= 0)
                    found.Add($"{prefix}{state.GetNodeName(i)} ({type})");
            }
        }
    }

    private static List<string> FindLiveTypes(Node from, string[] types)
    {
        var found = new List<string>();
        Walk(from);
        return found;

        void Walk(Node n)
        {
            // GetClass() is the NATIVE class, which is what SceneState.GetNodeType returns, so the
            // packed and live halves of this check are measuring the same thing.
            if (System.Array.IndexOf(types, n.GetClass()) >= 0)
                found.Add($"{n.Name} ({n.GetClass()})");
            foreach (Node c in n.GetChildren())
                Walk(c);
        }
    }

    private static void CollectByType<T>(Node node, List<T> into) where T : Node
    {
        if (node is T match)
            into.Add(match);
        foreach (Node child in node.GetChildren())
            CollectByType(child, into);
    }

    private void Check(bool ok, string why)
    {
        if (!ok) Fail(why);
    }

    private void Fail(string why) => _failures.Add(why);

    private void Finish()
    {
        if (_captureDir.Length > 0 && !DisplayServer.GetName().Contains("headless"))
        {
            _ = CaptureThenFinish();
            return;
        }
        FinishNow();
    }

    /// <summary>The headed evidence pass. A camera is parked at each vantage and the ENGINE'S OWN
    /// viewport texture is written — never an OS screen-scrape of the window, which is the rule
    /// this repo learned the hard way. The camera is a child of the SELF-TEST, never of the
    /// ported scene, so it cannot become the thing <see cref="CheckNoScreenCover"/> forbids.</summary>
    private async System.Threading.Tasks.Task CaptureThenFinish()
    {
        DirAccess.MakeDirRecursiveAbsolute(_captureDir);
        _probe.QueueFree(); // the probe body must not stand in its own evidence

        var cam = new Camera3D { Name = "CaptureCam", Fov = 70f, Current = true };
        AddChild(cam);

        foreach ((string file, string caption, Vector3 eye, Vector3 at) in Vantages)
        {
            cam.GlobalPosition = _lab.ToGlobal(eye);
            cam.LookAt(_lab.ToGlobal(at), Vector3.Up);
            // Let the flicker bank, the green breath and the TV static settle onto a real frame
            // before reading the texture; a still taken on the frame the camera moved is a photo
            // of a half-updated world.
            for (int i = 0; i < 20; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Image img = GetViewport().GetTexture().GetImage();
            string path = $"{_captureDir}/{file}";
            Error err = img.SavePng(path);
            GD.Print($"[puffinlab-selftest]   {(err == Error.Ok ? "wrote" : $"FAILED {err}")} "
                     + $"{path} — {caption}");
            Check(err == Error.Ok, $"could not write {path}: {err}");
        }
        FinishNow();
    }

    private void FinishNow()
    {
        foreach (string f in _failures)
            GD.PushError($"[puffinlab-selftest] FAIL {f}");
        if (_failures.Count > 0)
            GD.Print($"[puffinlab-selftest] FAIL ({_failures.Count})");
        else
            GD.Print("[puffinlab-selftest] PASS (seam wired, no screen-covering node packed or "
                     + $"live, all {Route.Length} route stations hold the live avatar's capsule, "
                     + "ReturnTv's trigger fires)");
        GetTree().Quit(_failures.Count > 0 ? 1 : 0);
    }
}
