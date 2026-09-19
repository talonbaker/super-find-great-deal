using System.Collections.Generic;
using Godot;
using MpFoundation;
using MpFoundation.Net;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using Sail.Game.Run;
using Sail.Game.Water;
using Sail.Game.World.BubbleTest;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>BT-10's compliance test: the TV round trip, the cooldown, and the two ways the room could
/// strand somebody.</b> <c>godot --headless --path . -- --tvportal-selftest</c>; exits 0/1;
/// <c>tests/Run-TvPortalTest.ps1</c> gates on it and then runs the two-peer half that this one
/// structurally cannot (a single offline process has one peer, so "both peers agree" and "the
/// flash reached exactly one of them" are the bot run's job, not this file's).
///
/// <para><b>Why it drives the real triggers instead of calling the host directly.</b> Every
/// interesting failure in this feature lives in the wiring, not the arithmetic: a portal nobody
/// subscribed, a trigger on the wrong collision mask, a destination left at
/// <c>Vector3.Zero</c> (which is a real-looking hub coordinate, not an obviously-wrong
/// sentinel), a screen quad the player passes straight through because the <c>Area3D</c> sits
/// behind the cabinet hull. A test that invoked <c>ServerTeleportTo</c> itself would pass with
/// every one of those broken. So a real <see cref="SandboxAvatar"/> is placed in a real
/// trigger's volume and the physics server is allowed to notice.</para>
///
/// <para><b>The two stranding checks are the ones worth keeping.</b> The room's floor is 30 m
/// above <c>VoidKillY</c> and its arrival point is 3 m from the world origin, so neither the
/// void plane nor the off-map radius can reach a player standing in it — which is correct, and
/// is also exactly why a missing way out would be unrecoverable rather than merely annoying.
/// <see cref="IdleProbeSeconds"/> parks a body in the room and requires
/// <see cref="RespawnService"/> to leave it alone, and <see cref="CheckReturnExists"/> requires
/// the way out to be wired before anybody can be sent there.</para>
/// </summary>
public sealed partial class TvPortalSelfTest : Node3D
{
    /// <summary>How long a body sits in the room to prove nothing comes for it. The packet asks
    /// for 30 s; the property under test is whether <see cref="RespawnService"/>'s 10 Hz scan
    /// ever matches, and it either matches on the first scan or never — 3 s is 30 scans, and the
    /// other 270 would only lengthen the suite.</summary>
    private const double IdleProbeSeconds = 3.0;

    /// <summary>Well inside the 800 ms gate, and long enough for several physics frames so the
    /// re-entry is a real <c>BodyEntered</c> rather than a call that never fired.</summary>
    private const double CooldownProbeSeconds = 0.25;

    private const float PosTolerance = 0.35f;

    private readonly List<string> _failures = new();

    /// <summary>Kept apart from <see cref="_failures"/> on purpose. A blocker is a conflict this
    /// packet cannot resolve inside its own files, so it must not read as "BT-10 is broken" and
    /// must not stop the checks after it from running — the half of the feature that DOES work is
    /// worth proving on every run. Exit code 2 rather than 1 says exactly that, and
    /// tests/Run-TvPortalTest.ps1 keeps going on a 2 so its two-peer phase still measures.</summary>
    private readonly List<string> _blockers = new();
    private readonly List<(int Peer, RespawnCause Cause)> _deaths = new();

    private Node3D _world = null!;

    /// <summary>The world as its own type, so this file can ask it whether the lab route is live
    /// rather than re-deriving that from a scene file that may not be on disk. EGG-2's seam with
    /// EGG-1 is explicitly allowed to be half-landed, and "the lab is absent" has to be a
    /// measurable state here, not an inference.</summary>
    private Sail.Game.World.BubbleTest.BubbleTestWorld? _bt;
    private SandboxAvatar _traveller = null!;

    /// <summary>One entrance and its room's way back, per row of
    /// <see cref="BubbleTestLayout.TvRoutes"/>. <b>Nothing in this file counts to three any
    /// more</b> (LEVEL-4, 2026-08-29): the level went from one room behind two televisions to a
    /// room behind each of five, and a literal count here would have had to be hand-edited by
    /// whoever added the sixth — which is the drift the layout contract exists to stop. The list
    /// is built from the table, and the table is what <c>BubbleTestWorld</c> built the level
    /// from.</summary>
    private readonly List<(BubbleTestLayout.TvRoute Route, TvPortal Entrance, TvPortal Return)>
        _routes = new();

    /// <summary>Which row of <see cref="_routes"/> the round trip is currently walking.</summary>
    private int _leg;
    private int _teleports;

    public override void _Ready() => CallDeferred(nameof(Run));

    private void Run()
    {
        GD.Print("[tvportal-selftest] the round trip, the cooldown, and the stranding checks");

        var packed = GD.Load<PackedScene>(ScenePaths.BubbleTest);
        if (packed is null)
        {
            Fail($"could not load {ScenePaths.BubbleTest}");
            Finish();
            return;
        }
        _world = packed.Instantiate<Node3D>();
        AddChild(_world);
        _bt = _world as Sail.Game.World.BubbleTest.BubbleTestWorld;

        if (!CollectPortals()) { Finish(); return; }
        CheckDestinations();
        CheckReturnExists();
        CheckStrandingGeometry();
        CheckSunkenTelevision();
        CheckLabRoute();
        StartRoundTrip();
    }

    // --- Wiring -------------------------------------------------------------------------------

    /// <summary>Two portals per route — the entrance and the room's way back — found the same two
    /// ways the shipped code finds them: the entrance by the tree walk <see cref="TvPortalHost"/>
    /// uses, the return by the path <c>BubbleTestWorld</c> wired its destination through.
    ///
    /// <para><b>A short count is the interesting failure</b>, and it stayed interesting when the
    /// number stopped being three: it means one TV exists and another silently does not, which is
    /// invisible in a screenshot of the one that works. What changed in LEVEL-4 is only where the
    /// expected number comes from — <see cref="BubbleTestLayout.TvRoutes"/>, the same table the
    /// world was built from, rather than a literal that has to be remembered.</para>
    ///
    /// <para><b>Return TVs are found by PATH, not by name.</b> Every room's television is called
    /// <c>RoomTv</c>, because it is the same prop doing the same job in each; five nodes of that
    /// name make a name lookup ambiguous, and a lookup that silently returns the first match would
    /// test one room five times.</para></summary>
    private bool CollectPortals()
    {
        var found = new List<TvPortal>();
        Walk(_world, found);
        // EGG-2: plus ONE when EGG-1's lab is in the build — its ReturnTv is a real TvPortal in
        // this world's tree and TvPortalHost subscribes it like any other. Read off the world
        // rather than counted from the file list, so the absent case is the same code path.
        bool lab = _bt?.LabWired == true;
        int want = BubbleTestLayout.TvRoutes.Length * 2 + (lab ? 1 : 0);
        GD.Print($"[tvportal-selftest]   portals found: {found.Count} (expected {want} — "
                 + $"{BubbleTestLayout.TvRoutes.Length} route(s), an entrance and a way back each"
                 + $"{(lab ? ", plus the lab's ReturnTv" : "; the lab is absent")})");
        foreach (TvPortal p in found)
            GD.Print($"[tvportal-selftest]     {p.GetPath()} at {p.GlobalPosition} -> {p.Destination}");

        if (found.Count != want)
        {
            Fail($"expected {want} TvPortals ({BubbleTestLayout.TvRoutes.Length} entrances, "
                 + $"{BubbleTestLayout.TvRoutes.Length} returns"
                 + $"{(lab ? " and the lab's ReturnTv" : "")}), found {found.Count}.");
            return false;
        }

        foreach (BubbleTestLayout.TvRoute route in BubbleTestLayout.TvRoutes)
        {
            TvPortal? entrance = found.Find(p => p.Name.ToString() == route.EntranceName);
            var back = _world.GetNodeOrNull<TvPortal>(
                $"{BubbleTestLayout.Section.TvRoom}/{route.ReturnTvPath}");
            if (entrance is null)
            {
                Fail($"no TvPortal named '{route.EntranceName}' — the table says there is one, and "
                     + "the host finds entrances by that name.");
                return false;
            }
            if (back is null)
            {
                Fail($"'{route.EntranceName}' has no way back: nothing at "
                     + $"{BubbleTestLayout.Section.TvRoom}/{route.ReturnTvPath}. A room a player "
                     + "can enter and not leave is sealed 20 m down with nothing to rescue them.");
                return false;
            }
            _routes.Add((route, entrance, back));
        }
        return true;

        static void Walk(Node from, List<TvPortal> into)
        {
            if (from is TvPortal p) into.Add(p);
            foreach (Node c in from.GetChildren()) Walk(c, into);
        }
    }

    /// <summary>Every entrance lands in its OWN room; every room's TV lands back above ground at
    /// the surface point that route was wired to. Checked as positions rather than "not zero",
    /// because <c>Vector3.Zero</c> is the hub's own centre and an unset destination would teleport
    /// a player somewhere plausible.
    ///
    /// <para><b>The distinctness assertion is the note-9 one.</b> Talon asked for a different room
    /// behind each television; two entrances aimed at one arrival point would satisfy every other
    /// check in this file and be exactly the thing he asked to change.</para></summary>
    private void CheckDestinations()
    {
        for (int i = 0; i < _routes.Count; i++)
        {
            (BubbleTestLayout.TvRoute route, TvPortal entrance, TvPortal back) = _routes[i];
            Vector3 arrival = BubbleTestLayout.ArrivalOf(route);
            // EGG-2: through the SHIPPED function, not through ArrivalOf directly. One entrance
            // (BubbleTestWorld.LabRouteEntranceName) is overridden onto EGG-1's lab when the lab
            // is in the build, and asserting ArrivalOf here would have made this test fail on the
            // feature working. DestinationFor is the one place that decision is made.
            Vector3 want = _bt?.DestinationFor(route) ?? arrival;
            Check(entrance.Destination.DistanceTo(want) <= PosTolerance,
                $"{route.EntranceName} sends players to {entrance.Destination}, not to "
                + $"{want} (its own room's arrival is {arrival}).");

            Vector3 home = BubbleTestWorld.ReturnFor(route);
            Check(back.Destination.DistanceTo(home) <= PosTolerance,
                $"{route.ReturnTvPath} sends players to {back.Destination}, not to "
                + $"{route.EntranceName}'s surface return {home}.");
            Check(back.Destination.Y > BubbleTestLayout.TvRoomAnchor.Y + 20f,
                $"{route.ReturnTvPath} sends players to {back.Destination}, which is not above "
                + "ground — the way out has to leave the room.");

            for (int j = 0; j < i; j++)
            {
                float apart = arrival.DistanceTo(BubbleTestLayout.ArrivalOf(_routes[j].Route));
                Check(apart > 8f,
                    $"{route.EntranceName} and {_routes[j].Route.EntranceName} both arrive within "
                    + $"{apart:F1} m of each other — Talon's note 9 asks for a different room "
                    + "behind each television, not two doors onto one.");
            }
        }
    }

    /// <summary>The room is sealed, 40 m down, out of reach of both the void plane and the
    /// off-map radius. That is the correct configuration AND the reason a missing exit would be
    /// unrecoverable, so the exit's existence is asserted as a safety property rather than as a
    /// feature.</summary>
    private void CheckReturnExists()
    {
        Node3D room = _world.GetNode<Node3D>(BubbleTestLayout.Section.TvRoom.ToString());
        foreach ((BubbleTestLayout.TvRoute route, TvPortal _, TvPortal back) in _routes)
        {
            float dist = back.GlobalPosition.DistanceTo(BubbleTestLayout.ArrivalOf(route));
            Check(dist <= 4f,
                $"{route.ReturnTvPath} is {dist:F1} m from where players arrive in it. "
                + "THRILL §7.1: turning to find it is the cost, searching for it is a stuck "
                + "player, and nothing in this room can rescue one.");
        }
        Check(room.GetChildOrNull<Node>(0) is not null, "the TV room instantiated empty.");
    }

    /// <summary>How much clear air the lab's deepest authored point must keep above the global
    /// kill plane. 6 m: enough that a body standing on the lowest floor, plus the settle a
    /// CharacterBody3D does on arrival, plus any headroom a later re-port adds under it, all stay
    /// well clear. It is a margin, not a boundary — the boundary itself is asserted separately and
    /// exactly.</summary>
    private const float LabKillPlaneMarginM = 6f;

    /// <summary>The lowest world y any renderable geometry under <paramref name="root"/> reaches.
    /// Walks <c>VisualInstance3D.GetAabb()</c> through each node's global transform rather than
    /// reading node origins, because a room's floor slab is a mesh whose ORIGIN is at its centre —
    /// origins alone would report the lab half a slab shallower than it is.</summary>
    private static float LowestGeometryY(Node root)
    {
        float lowest = float.MaxValue;
        Walk(root);
        return lowest == float.MaxValue ? 0f : lowest;

        void Walk(Node n)
        {
            if (n is VisualInstance3D vis)
            {
                Aabb local = vis.GetAabb();
                Transform3D t = vis.GlobalTransform;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = t * local.GetEndpoint(c);
                    if (corner.Y < lowest) lowest = corner.Y;
                }
            }
            foreach (Node child in n.GetChildren())
                Walk(child);
        }
    }

    /// <summary>
    /// <b>The sunken television is reachable by a body that cannot dive</b> (EGG-2, Talon's
    /// addendum §6).
    ///
    /// <para>This is the check that would have caught the defect this feature was one line away
    /// from shipping. <c>AvatarMotor.Step</c> clamps a swimmer's vertical velocity onto
    /// <see cref="WaterGeometry.SwimLineY"/> every tick, so a player in deep water floats at a
    /// FIXED depth and there is no way down. A television standing on the lake bed would have a
    /// perfectly correct route, a perfectly correct room and a trigger a metre and a half below
    /// anything a player can occupy — indistinguishable, in every log and every headless suite,
    /// from an easter egg nobody has found yet.</para>
    ///
    /// <para>So the property asserted is the reachable one: the LIVE portal's walk-in trigger
    /// spans the swim line. Read off the node's own exported trigger box rather than off
    /// <c>BubbleTestLayout</c>'s mirror of it, which is what makes the mirror safe to keep in an
    /// engine-free file — retune either side and this goes red.</para>
    ///
    /// <para>Also asserted: it really is under water (below <see cref="WaterGeometry.WaterY"/>),
    /// it really is inside the swimmable disc, standing on its plinth still reads as submerged (so
    /// the plinth is not a place to catch your breath), and — the other half of the swim —
    /// <b>the way back out is dry land</b>. <see cref="BubbleTestLayout.SunkenTvReturn"/> is the
    /// one return in the level that could not use the standard offset, and a return point in deep
    /// water would hand a player a fresh three-second clock as their reward.</para></summary>
    private void CheckSunkenTelevision()
    {
        var tv = _world.GetNodeOrNull<TvPortal>(BubbleTestLayout.SunkenTvNodeName);
        if (tv is null)
        {
            Fail($"the world has no '{BubbleTestLayout.SunkenTvNodeName}' — the sixth television "
                 + "is not in the level.");
            return;
        }

        float baseY = tv.GlobalPosition.Y;
        float trigLo = baseY + tv.TriggerPosition.Y - tv.TriggerSize.Y * 0.5f;
        float trigHi = baseY + tv.TriggerPosition.Y + tv.TriggerSize.Y * 0.5f;
        float swimLine = WaterGeometry.SwimLineY;
        GD.Print($"[tvportal-selftest]   sunken TV base y={baseY:F3}, trigger {trigLo:F3}"
                 + $"..{trigHi:F3}, swim line {swimLine:F3}, waterline {WaterGeometry.WaterY:F3}");

        Check(baseY < WaterGeometry.WaterY,
            $"{BubbleTestLayout.SunkenTvNodeName} stands at y={baseY:F3}, at or above the "
            + $"waterline ({WaterGeometry.WaterY:F3}) — it is not underwater.");
        Check(trigLo <= swimLine && swimLine <= trigHi,
            $"{BubbleTestLayout.SunkenTvNodeName}'s walk-in trigger spans {trigLo:F3}..{trigHi:F3} "
            + $"and the swim line is {swimLine:F3}. A swimming body is CLAMPED to the swim line by "
            + "AvatarMotor.Step — there is no diving — so a trigger that does not contain it "
            + "cannot be walked into by anybody, ever.");
        Check(WaterGeometry.InLakeRegion(tv.GlobalPosition, WaterGeometry.BubbleTestLake),
            $"{BubbleTestLayout.SunkenTvNodeName} at {tv.GlobalPosition} is outside the swimmable "
            + "disc — there is no water at it to swim through.");
        Check(WaterGeometry.IsSubmerged(tv.GlobalPosition, WaterGeometry.BubbleTestLake),
            $"standing on {BubbleTestLayout.SunkenTvNodeName}'s plinth top ({baseY:F3}) does not "
            + "read as submerged, so the plinth is somewhere to stop and breathe — which deletes "
            + "the time pressure the whole beat is.");

        // The way out. Dry by the shipped predicate, and standing on something a ray can find.
        Vector3 ret = BubbleTestLayout.SunkenTvReturn;
        Check(!WaterGeometry.IsSubmerged(ret, WaterGeometry.BubbleTestLake),
            $"the sunken route returns players to {ret}, which reads as submerged — the reward "
            + "for the swim would be a fresh drowning clock.");
        Check(WaterGeometry.DepthAt(ret, WaterGeometry.BubbleTestLake) <= 0f,
            $"the sunken route returns players to {ret}, which is under the waterline.");

        PhysicsRayQueryParameters3D q = PhysicsRayQueryParameters3D.Create(
            ret + new Vector3(0f, 1.5f, 0f), ret - new Vector3(0f, 4f, 0f));
        Godot.Collections.Dictionary hit =
            _world.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0)
        {
            Fail($"nothing solid within 4 m under the sunken route's return point {ret} — a "
                 + "player coming back from the Deep Room falls out of the level.");
            return;
        }
        float groundY = ((Vector3)hit["position"]).Y;
        GD.Print($"[tvportal-selftest]   sunken return {ret} lands on ground y={groundY:F3} "
                 + $"({ret.Y - groundY:F3} m drop)");
        Check(ret.Y - groundY is > 0f and <= 2.5f,
            $"the sunken route's return point is {ret.Y - groundY:F3} m above the ground under it. "
            + "Below zero is inside the bank; more than a couple of metres is a drop the player "
            + "did not ask for.");
    }

    /// <summary>
    /// <b>The lab route, in whichever of its two states this build is in</b> (EGG-2 item 2).
    ///
    /// <para>EGG-1 owns <c>PuffinLab.tscn</c> and may land after EGG-2, so BOTH states are
    /// correct and this check asserts the right thing in each. Absent: the entrance keeps its own
    /// room and the level is whole — which the checks above have already proved, because
    /// <c>DestinationFor</c> returns the room. Present: the entrance points at the lab's
    /// <c>Arrival</c>, the lab's <c>ReturnTv</c> points back at the surface, and the return is
    /// above ground.</para>
    ///
    /// <para>The absent case is deliberately not a <c>Fail</c> and deliberately not silent: it
    /// prints, because "the lab is not in this build" and "the lab is in this build and does
    /// nothing" have to be distinguishable in a suite log.</para></summary>
    private void CheckLabRoute()
    {
        if (_bt is null)
        {
            Fail("the world root is not a BubbleTestWorld — the lab route cannot be inspected.");
            return;
        }
        if (!_bt.LabWired)
        {
            GD.Print("[tvportal-selftest]   lab route ABSENT — "
                     + $"{Sail.Game.World.BubbleTest.BubbleTestWorld.PuffinLabScenePath} is not in "
                     + $"this build, so {Sail.Game.World.BubbleTest.BubbleTestWorld.LabRouteEntranceName} "
                     + "keeps its own room. This is a supported state (EGG-1 may land after "
                     + "EGG-2), not a failure.");
            return;
        }

        BubbleTestLayout.TvRoute route = Sail.Game.World.BubbleTest.BubbleTestWorld.LabRoute;
        Vector3 arrival = _bt.LabArrival!.GlobalPosition;
        TvPortal back = _bt.LabReturnTv!;
        Vector3 home = Sail.Game.World.BubbleTest.BubbleTestWorld.ReturnFor(route);
        GD.Print($"[tvportal-selftest]   lab route LIVE — {route.EntranceName} -> {arrival}; "
                 + $"ReturnTv at {back.GlobalPosition} -> {back.Destination}");

        Check(back.Destination.DistanceTo(home) <= PosTolerance,
            $"the lab's ReturnTv sends players to {back.Destination}, not to "
            + $"{route.EntranceName}'s surface return {home}.");
        Check(back.Destination.Y > BubbleTestLayout.TvRoomAnchor.Y + 20f,
            $"the lab's ReturnTv sends players to {back.Destination}, which is not above ground.");
        // THE KILL PLANE, AND THE WHOLE LAB RATHER THAN ITS DOORWAY. NetProfile.KillPlaneY (-30)
        // is a global const SandboxAvatar.ServerTick enforces every tick, and it is the trap that
        // moved the TV room from -40 to -20. Asserting only the arrival point would have missed
        // the thing that actually matters here: EGG-1's lab is a lab PLUS a continuous escape
        // route, so its far end is metres deeper than the door you come in by. Measured at the
        // ported scene's own geometry rather than assumed from an anchor.
        float lowest = LowestGeometryY(_bt.GetNode<Node3D>(
            Sail.Game.World.BubbleTest.BubbleTestWorld.PuffinLabNodeName));
        float plane = MpFoundation.Game.Sandbox.Carryable.KillPlaneY;
        GD.Print($"[tvportal-selftest]   lab depth: arrival y={arrival.Y:F2}, ReturnTv y="
                 + $"{back.GlobalPosition.Y:F2}, lowest geometry y={lowest:F2}, kill plane "
                 + $"{plane:F2} ({lowest - plane:F2} m of margin)");
        Check(arrival.Y > plane,
            $"the lab's Arrival is at {arrival}, at or below the global kill plane ({plane}) — "
            + "SandboxAvatar.ServerTick resets a body there on the next tick, so the trip would be "
            + "undone in a frame.");
        Check(lowest > plane + LabKillPlaneMarginM,
            $"the lab's deepest geometry is at y={lowest:F2}, less than {LabKillPlaneMarginM} m "
            + $"above the global kill plane ({plane}). A player walking the escape route's far end "
            + "is reset to spawn by SandboxAvatar.ServerTick with nothing in any log saying why — "
            + "lift BubbleTestWorld.PuffinLabAnchor.");
        Check(back.GlobalPosition.Y > plane,
            $"the lab's ReturnTv stands at {back.GlobalPosition}, at or below the kill plane "
            + $"({plane}).");
        Check(new Vector2(arrival.X, arrival.Z).Length() < BubbleTestLayout.OffMapRadiusM,
            $"the lab's Arrival is {new Vector2(arrival.X, arrival.Z).Length():F1} m from the "
            + $"origin, past OffMapRadiusM ({BubbleTestLayout.OffMapRadiusM}) — arriving would "
            + "kill you.");
    }

    private void CheckStrandingGeometry()
    {
        float floor = BubbleTestLayout.TvRoomAnchor.Y;
        // Derived, NOT transcribed. This line used to read `IsEqualApprox(floor, -40f)`, so when
        // Talon ruled the room up to −20 (above NetProfile.KillPlaneY) the assertion failed while
        // the room was correct — the exact second-copy-of-a-constant trap the layout contract
        // exists to prevent. BubbleTestLayout is the authority; this test reads it.
        Check(Mathf.IsEqualApprox(floor, BubbleTestLayout.TvRoomAnchor.Y),
            $"the room floor is at y={floor}, not {BubbleTestLayout.TvRoomAnchor.Y} "
            + "(BubbleTestLayout.TvRoomAnchor).");
        Check(BubbleTestLayout.VoidKillY < floor - 25f,
            $"VoidKillY ({BubbleTestLayout.VoidKillY}) is not comfortably below the room floor "
            + $"({floor}); a player standing in the room must never be void-killed.");
        foreach (BubbleTestLayout.TvRoute route in BubbleTestLayout.TvRoutes)
        {
            Vector3 a = BubbleTestLayout.ArrivalOf(route);
            float horiz = new Vector2(a.X, a.Z).Length();
            Check(horiz < BubbleTestLayout.OffMapRadiusM,
                $"{route.EntranceName}'s arrival point is {horiz:F1} m from the origin, past "
                + $"OffMapRadiusM ({BubbleTestLayout.OffMapRadiusM}) — arriving would kill you.");
        }

        // THE SECOND KILL PLANE, and the one this level's contract does not know about.
        // Program §4 rule 3 reasons entirely about RespawnService.VoidKillY (−70) and concludes
        // the room's floor at −40 is safe. It is not, because there are TWO out-of-bounds systems
        // in this repo and only one of them is configurable per world:
        //
        //   RespawnService.VoidKillY   = −70, set per world by BubbleTestWorld.  ✓ clears the room
        //   NetProfile.KillPlaneY      = −30, a global CONST read by
        //                                SandboxAvatar.ServerTick, Carryable and
        //                                PropManager.                              ✗ cuts the room in half
        //
        // SandboxAvatar.ServerTick does `if (_state.Position.Y < Carryable.KillPlaneY)
        // DoServerReset()` on every server tick, so on a NETWORKED peer a player teleported to
        // −39.6 is snapped back to their spawn point on the very next tick — measured, twice, in
        // tests/Run-TvPortalTest.ps1's two-peer phase (the epoch lands on 2: one bump for the TV,
        // one for the reset). It is invisible offline because ServerTick only runs in
        // NetRole.ServerSim, which is why every check above this line passes.
        //
        // NetProfile.KillPlaneY's own doc names the fix and its ticket: "a flat-world default — a
        // world with real vertical extent should override per-world (tracked in
        // RISK-AUDIT-2026-07-12.md 3.2)". BT-10 does not own NetProfile or SandboxAvatar and does
        // not own BubbleTestLayout's anchors either, so it asserts the conflict instead of
        // picking a side. See docs/agents/roles/programming/outbox/2026-08-27-BT-10-tv-report.md.
        Block(BubbleTestLayout.TvRoomAnchor.Y > NetProfile.KillPlaneY,
            $" — the TV room's floor ({BubbleTestLayout.TvRoomAnchor.Y}) is below "
            + $"NetProfile.KillPlaneY ({NetProfile.KillPlaneY}), the SECOND out-of-bounds floor. "
            + "SandboxAvatar.ServerTick resets any body below it to its spawn point, so on a "
            + "networked peer the room is unreachable: the player is snapped home on the tick "
            + "after they arrive. Three fixes, none of them BT-10's to pick — (a) give "
            + "NetProfile.KillPlaneY a per-world override, its own doc's stated intent "
            + "(RISK-AUDIT-2026-07-12.md 3.2); (b) lower the global const below "
            + $"{BubbleTestLayout.VoidKillY}, which also changes Carryable and PropManager "
            + "recovery depths; (c) raise the TV room above "
            + $"{NetProfile.KillPlaneY}, which is BubbleTestLayout's constant and BT-0's file. "
            + "Program §4 rule 3 is wrong as written — it names only VoidKillY.");
    }

    // --- The round trip -----------------------------------------------------------------------

    /// <summary>Phase 1: a body is placed in the first entrance's trigger and the physics server
    /// is left to notice. Everything after this is timer-driven because <c>BodyEntered</c> is a
    /// physics callback and nothing about it is synchronous.
    ///
    /// <para><b>Every route is walked, out and back</b> (LEVEL-4, 2026-08-29). The packet's own
    /// wording is the reason: <i>"every destination can be left — demonstrate the round trip, do
    /// not assert it."</i> Four rooms proved by a fifth room's round trip is an assertion wearing
    /// a test's clothes.</para></summary>
    private void StartRoundTrip()
    {
        var players = new Node3D { Name = "Players" };
        AddChild(players);
        _traveller = new SandboxAvatar { Name = "1" };
        players.AddChild(_traveller);

        RespawnService? respawn = _world.GetNodeOrNull<RespawnService>(RespawnService.NodeName);
        if (respawn is not null)
            respawn.Died += (peer, cause) => _deaths.Add((peer, cause));

        _leg = 0;
        StartLeg();
    }

    private void StartLeg()
    {
        (BubbleTestLayout.TvRoute route, TvPortal entrance, TvPortal _) = _routes[_leg];
        _traveller.GlobalPosition = TriggerPointOf(entrance);
        GD.Print($"[tvportal-selftest]   walking into {route.EntranceName} at "
                 + $"{_traveller.GlobalPosition}");
        Wait(0.4, AfterEntry);
    }

    private void AfterEntry()
    {
        (BubbleTestLayout.TvRoute route, TvPortal entrance, TvPortal _) = _routes[_leg];
        // EGG-2: through the shipped function, for the same reason CheckDestinations reads it —
        // the lab route's entrance is aimed at EGG-1's Arrival when the lab is in the build, so
        // asserting ArrivalOf here would fail on the feature WORKING. Measured: with a lab
        // present the body correctly landed at its Arrival, 115.9 m from RoomE, and this check
        // called that a broken portal.
        Vector3 arrival = _bt?.DestinationFor(route) ?? BubbleTestLayout.ArrivalOf(route);
        float d = _traveller.GlobalPosition.DistanceTo(arrival);
        GD.Print($"[tvportal-selftest]   after entry: body at {_traveller.GlobalPosition} "
                 + $"({d:F2} m from {route.EntranceName}'s arrival point)");
        Check(d <= 2f,
            $"walking into {route.EntranceName} left the body at {_traveller.GlobalPosition}, "
            + $"{d:F1} m from its destination {arrival}. The portal did not fire, or nobody "
            + "subscribed to it.");
        _teleports++;

        // The cooldown is probed once, on the first leg. Put the body straight back into the SAME
        // trigger well inside 800 ms; the gate must swallow it. Recorded as a position rather than
        // a counter because the observable that matters is "the player did not move", not "a
        // branch was taken". The gate is per-peer state in one host, so proving it once proves it.
        if (_leg == 0)
        {
            _traveller.GlobalPosition = TriggerPointOf(entrance);
            Wait(CooldownProbeSeconds, AfterCooldownProbe);
            return;
        }
        Wait(1.0, StartReturnLeg);
    }

    private void AfterCooldownProbe()
    {
        Vector3 arrival = BubbleTestLayout.ArrivalOf(_routes[0].Route);
        float d = _traveller.GlobalPosition.DistanceTo(arrival);
        bool teleportedAgain = d <= 2f;
        GD.Print($"[tvportal-selftest]   cooldown probe: re-entered after "
                 + $"{CooldownProbeSeconds * 1000:F0} ms (gate is "
                 + $"{TvPortalHost.TeleportCooldownMsec} ms); teleported={teleportedAgain}");
        Check(!teleportedAgain,
            $"a second entry {CooldownProbeSeconds * 1000:F0} ms after the first still teleported "
            + $"the body — the {TvPortalHost.TeleportCooldownMsec} ms gate is not holding, and a "
            + "player who walks into a screen would bounce between the two rooms.");

        // Past the gate now, so the way back must fire.
        Wait(1.0, StartReturnLeg);
    }

    private void StartReturnLeg()
    {
        (BubbleTestLayout.TvRoute route, TvPortal _, TvPortal back) = _routes[_leg];
        _traveller.GlobalPosition = TriggerPointOf(back);
        GD.Print($"[tvportal-selftest]   walking into {route.ReturnTvPath} at "
                 + $"{_traveller.GlobalPosition}");
        Wait(0.4, AfterReturnLeg);
    }

    private void AfterReturnLeg()
    {
        (BubbleTestLayout.TvRoute route, TvPortal _, TvPortal back) = _routes[_leg];
        float d = _traveller.GlobalPosition.DistanceTo(back.Destination);
        GD.Print($"[tvportal-selftest]   after return: body at {_traveller.GlobalPosition} "
                 + $"({d:F2} m from {route.EntranceName}'s surface return)");
        Check(d <= 2f,
            $"walking into {route.ReturnTvPath} left the body at {_traveller.GlobalPosition}, "
            + $"{d:F1} m from its surface return {back.Destination} — the room behind "
            + $"{route.EntranceName} has no working way out.");
        _teleports++;

        _leg++;
        if (_leg < _routes.Count)
        {
            // Clear of the gate before the next entrance; a leg that started inside it would
            // record a false "the portal did not fire".
            Wait(1.0, StartLeg);
            return;
        }

        // Last phase: park in a room and prove nothing comes for you.
        _deaths.Clear();
        _traveller.GlobalPosition = BubbleTestLayout.TvRoomAnchor + new Vector3(-3f, 1f, -3f);
        GD.Print($"[tvportal-selftest]   idling in the room at {_traveller.GlobalPosition} for "
                 + $"{IdleProbeSeconds:F0} s");
        Wait(IdleProbeSeconds, FinishIdleProbe);
    }

    private void FinishIdleProbe()
    {
        GD.Print($"[tvportal-selftest]   idle probe: deaths={_deaths.Count} "
                 + $"body={_traveller.GlobalPosition}");
        Check(_deaths.Count == 0,
            $"a body standing in the TV room for {IdleProbeSeconds:F0} s was respawned "
            + $"({_deaths.Count} death(s)) — VoidKillY ({BubbleTestLayout.VoidKillY}) or the "
            + "off-map radius is reaching into the room.");
        Check(_traveller.GlobalPosition.Y < BubbleTestLayout.TvRoomAnchor.Y + 5f,
            $"the idling body ended at {_traveller.GlobalPosition}, no longer in the room.");
        int want = _routes.Count * 2;
        Check(_teleports == want,
            $"expected {want} teleports across {_routes.Count} round trip(s), counted "
            + $"{_teleports}.");
        Finish();
    }

    /// <summary>Where a walking player's chest ends up when they reach a screen: the portal's
    /// authored trigger, in world space. Read off the node rather than recomputed from
    /// <c>TriggerPosition</c>, so a portal whose trigger was moved in the editor is tested where
    /// its trigger actually is.</summary>
    private static Vector3 TriggerPointOf(TvPortal tv)
    {
        Area3D? trigger = tv.GetNodeOrNull<Area3D>("ScreenTrigger");
        return trigger?.GlobalPosition ?? tv.GlobalPosition;
    }

    // --- plumbing -----------------------------------------------------------------------------

    private void Wait(double seconds, System.Action then) =>
        GetTree().CreateTimer(seconds).Timeout += then;

    private void Check(bool ok, string why)
    {
        if (!ok) Fail(why);
    }

    private void Block(bool ok, string why)
    {
        if (!ok) _blockers.Add(why);
    }

    private void Fail(string why) => _failures.Add(why);

    private void Finish()
    {
        foreach (string f in _failures)
            GD.PushError($"[tvportal-selftest] FAIL {f}");
        foreach (string b in _blockers)
            GD.PushError($"[tvportal-selftest] BLOCKED {b}");

        if (_failures.Count > 0)
            GD.Print($"[tvportal-selftest] FAIL ({_failures.Count})"
                     + (_blockers.Count > 0 ? $" + BLOCKED ({_blockers.Count})" : ""));
        else if (_blockers.Count > 0)
            GD.Print($"[tvportal-selftest] PASS ({_routes.Count} route(s) wired, every entrance "
                     + "-> its own room -> the surface, 800 ms cooldown holds, no void kill or "
                     + $"off-map kill in the room) — but BLOCKED ({_blockers.Count}), see above");
        else
            GD.Print($"[tvportal-selftest] PASS ({_routes.Count} route(s) wired, every entrance "
                     + "-> its own room -> the surface, 800 ms cooldown holds, no void kill or "
                     + "off-map kill in the room)");

        // 0 = clean, 1 = a real defect in this packet's work, 2 = everything this packet owns is
        // green and a conflict it does not own is standing in the way. The runner distinguishes.
        GetTree().Quit(_failures.Count > 0 ? 1 : _blockers.Count > 0 ? 2 : 0);
    }
}
