using System.Collections.Generic;
using Godot;
using MpFoundation;

namespace Sail.Game.World.PuffinLab;

/// <summary>
/// <b>The Puffin Lab throwback's one runtime owner.</b> Everything the room does that is not
/// authored data goes through this node: which structural variant of the lab is solid, the green
/// system's breath, the room-wide dark beat and the lurker that crosses in it, and whether any of
/// it is audible.
///
/// <para><b>What it replaces, and what it deliberately does not bring with it.</b> In
/// mp-foundation the same job was split across four classes — <c>LabHost</c>, <c>LabEnvironment</c>,
/// <c>EscapeHost</c> and <c>EscapeEnvironment</c> — because that repo's lab WAS the whole running
/// scene. Three quarters of what those classes did is other people's job here and is gone:
/// spawning the player and its camera, the music director, the pause menu, the perf HUD, the
/// offline flashlight prop, the screenshot-vantage hooks, the film grain, and the terminus end
/// beat. <b>The end beat is the one that mattered</b> — <c>EscapeHost.RunEndBeatAsync</c> waited
/// 7 s after the player entered the black room and then tweened a full-screen <c>ColorRect</c> to
/// opaque black over 6 s, and <c>EscapeWorld</c> carried a second copy of it. That is the
/// "self-contained playtest that ends" behaviour the addendum said would break the bubble level,
/// and none of it is ported: there is no <c>CanvasLayer</c> anywhere in this scene, and the
/// <c>Terminus</c> <c>Area3D</c> the beat hung off is left in the authored data with its
/// monitoring switched off, unsubscribed, so nothing can re-arm it by accident.
/// <see cref="ReturnTvName"/> is what stands at that endpoint now.</para>
///
/// <para><b>The dark beat is server-authoritative, and that is the single largest change from the
/// source.</b> <c>LabEnvironment._Process</c> ran a private <see cref="RandomNumberGenerator"/> on
/// every machine — correct for one offline player, and mp-foundation's own comment calls the
/// multiplayer version of it "the exact sync bug the director exists to fix". Two players standing
/// in the same room would have watched the lights die at different moments and seen the lurker
/// cross at different moments, which is precisely the beat they would afterwards want to compare
/// notes on. So the schedule lives on the simulating peer and each beat is broadcast; clients
/// schedule nothing. This is <c>TvPortalHost</c>'s shape, for <c>TvPortalHost</c>'s reason.</para>
///
/// <para><b>What is deliberately still per-client:</b> each fixture's own idle jitter and micro-dip
/// (<see cref="FlickerLight"/>), the buzz-pop it fires, and the ambience's distant hallway sounds.
/// Those are sub-second cosmetic noise on a nine-fixture bank with no gameplay reading; replicating
/// them would mean per-fixture state on the wire for a room the players are visiting as a joke.
/// The line drawn is: <i>a beat two players would talk about is shared; texture is not.</i></para>
///
/// <para><b>Nothing here can end, pause, or black out the session.</b> No <c>GetTree().Quit</c>, no
/// <c>ChangeSceneTo*</c>, no <c>GetTree().Paused</c>, no input suppression, no full-screen overlay,
/// no <c>CanvasLayer</c>. The lurker has no collider, no player awareness and no consequence logic;
/// it is a shape that becomes visible and stops being visible.</para>
/// </summary>
public partial class PuffinLabRoom : Node3D
{
    /// <summary>The seam EGG-2 reads: a <see cref="Marker3D"/> child of this node marking where a
    /// traveller is placed on arrival.</summary>
    public const string ArrivalName = "Arrival";

    /// <summary>The seam EGG-2 wires: the <c>TvPortal</c> child standing at the old
    /// fade-to-black endpoint.</summary>
    public const string ReturnTvName = "ReturnTv";

    /// <summary>Child holding the instanced lab room (<c>LabRoom.tscn</c>).</summary>
    public const string LabName = "Lab";

    /// <summary>Child holding the instanced escape route (<c>EscapeRoute.tscn</c>).</summary>
    public const string RouteName = "Route";

    /// <summary>The lab's own coordinates, covering the sealed room, the crawl, the long hallway
    /// and the black room, with a couple of metres of margin. A local listener inside this box is
    /// what makes the room audible; outside it the ambience holds no voice and fires no one-shots.
    ///
    /// <para>It is an AABB in LOCAL space and tested with <see cref="Node3D.ToLocal"/> on purpose:
    /// the level that instances this scene places it wherever it likes, and a world-space constant
    /// would silently start testing the wrong volume the first time that placement moved.</para></summary>
    public static readonly Aabb AudibleBounds =
        new(new Vector3(-16f, -12f, -24f), new Vector3(110f, 26f, 44f));

    /// <summary>How often the local listener is tested against <see cref="AudibleBounds"/>. Half a
    /// second: a player cannot cross the wall of a sealed room and be surprised by a room tone
    /// fading in over that, and it keeps a tree lookup off the per-frame path.</summary>
    private const double ListenerPollSeconds = 0.5;

    /// <summary>Which shape a broadcast beat takes. Sent as an int because that is what crosses
    /// the wire cleanly; the values are pinned so a mixed-version session could not silently
    /// reinterpret them.</summary>
    private enum BeatKind
    {
        /// <summary>A short nervous flicker of the whole bank, nothing in it.</summary>
        Flicker = 0,
        /// <summary>Eyes at the door glass — the source's guaranteed first beat.</summary>
        Tease = 1,
        /// <summary>The hero beat: a blackout exactly as long as one crossing of the observation
        /// windows, with the lurker walking it.</summary>
        WalkBy = 2,
    }

    private readonly List<FlickerLight> _fixtures = new();
    private readonly RandomNumberGenerator _rng = new();

    private Node3D _lab = null!;
    private Lurker? _lurker;
    private bool _isServer;

    private float _nextBeatIn = 4.5f;   // first dark beat lands early, while attention is fresh
    private bool _firstBeat = true;
    private float _lurkerCooldown;
    private bool _lurkerHasAppeared;

    private double _listenerAccumulator = ListenerPollSeconds; // test on the very first frame

    public override void _Ready()
    {
        _isServer = NetworkManager.Instance is null
                    || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;

        _lab = GetNode<Node3D>(LabName);
        Node3D route = GetNode<Node3D>(RouteName);

        ResolveStructuralVariants();
        WireGlowBreath();
        DisarmTerminus(route);

        CollectByType(_lab, _fixtures);
        _lurker = FindFirstOfType<Lurker>(_lab);

        // Loud rather than silent, for TvPortalHost's reason: an easter egg that quietly does
        // nothing looks exactly like a level that never had one.
        GD.Print($"[puffinlab] room ready — fixtures={_fixtures.Count} lurker={_lurker != null} "
                 + $"server={_isServer}");
        if (_fixtures.Count == 0)
            GD.PushWarning("[puffinlab] no FlickerLight in the lab — the dark beat has nothing to drop.");
        if (_lurker == null)
            GD.PushWarning("[puffinlab] no Lurker in the lab — the walk-by beat will be lights only.");
    }

    /// <summary>Both members of each authored pair are present in <c>LabRoom.tscn</c> (the
    /// superset: sealed ceiling AND the hub hatch, sealed wall AND the escape hole). The throwback
    /// wants the escape route open and the hub access sealed, so exactly one of each pair is made
    /// visible and solid and the other invisible and non-solid — never one without the other, or a
    /// player walks through a wall that still looks solid, or bumps into one that is not there.
    ///
    /// <para>This is a toggle, not a build: it flips <c>Visible</c> and <c>Disabled</c> on nodes
    /// that are already in the packed scene, so it adds no geometry at runtime.</para></summary>
    private void ResolveStructuralVariants()
    {
        SetGroupActive(_lab.GetNode<Node3D>("EscapeHole_On"), true);
        SetGroupActive(_lab.GetNode<Node3D>("EscapeHole_Off"), false);
        SetGroupActive(_lab.GetNode<Node3D>("HubAccess_On"), false);
        SetGroupActive(_lab.GetNode<Node3D>("HubAccess_Off"), true);
    }

    /// <summary>The green system breathes as one organism: the hole's beacon light, the spill
    /// pool, the hose stub and the chamber's own fluid all ride the same slow curve. The material
    /// references are pulled from the LOADED scene's nodes rather than from code-time statics — a
    /// PackedScene embeds its own material copies, so the live reference has to come from what is
    /// really on screen.</summary>
    private void WireGlowBreath()
    {
        Node3D holeOn = _lab.GetNode<Node3D>("EscapeHole_On");
        var beacon = holeOn.GetNode<OmniLight3D>("EscapeHoleBeacon");
        var glowPool = holeOn.GetNode<MeshInstance3D>("GlowPool");
        var hoseStub = holeOn.GetNode<MeshInstance3D>("HoseStub0");
        var chamberFluid = _lab.GetNode<MeshInstance3D>("ChamberFluid");
        AddChild(new GlowBreath { Name = "GlowBreath" }
            .AddLight(beacon)
            .AddMaterial((StandardMaterial3D)glowPool.MaterialOverride, baseEmission: 0.55f)
            .AddMaterial((StandardMaterial3D)hoseStub.MaterialOverride, baseEmission: 2.6f)
            .AddMaterial((StandardMaterial3D)chamberFluid.MaterialOverride, baseEmission: 0.55f));
    }

    /// <summary><b>The removed hard stop, made unre-armable.</b> <c>Terminus</c> is the
    /// <see cref="Area3D"/> mp-foundation's <c>EscapeHost</c> and <c>EscapeWorld</c> both hung the
    /// end-of-playtest fade off. Neither host is ported and nothing subscribes to it, so the fade
    /// cannot fire — but an unsubscribed monitoring area is an invitation, and it costs physics
    /// broadphase work every tick for a signal nobody wants. Monitoring off, monitorable off, and
    /// the reason recorded here so the next reader knows the node is authored data being
    /// deliberately kept inert rather than something half-wired.</summary>
    private static void DisarmTerminus(Node3D route)
    {
        Area3D? terminus = route.GetNodeOrNull<Area3D>("Terminus");
        if (terminus == null)
        {
            GD.PushWarning("[puffinlab] no Terminus area in the route — nothing to disarm, but the "
                           + "authored scene was expected to still carry it.");
            return;
        }
        terminus.Monitoring = false;
        terminus.Monitorable = false;
    }

    internal static void SetGroupActive(Node3D group, bool active)
    {
        group.Visible = active;
        SetCollidersDisabled(group, !active);
    }

    private static void SetCollidersDisabled(Node node, bool disabled)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is CollisionShape3D shape)
                shape.Disabled = disabled;
            SetCollidersDisabled(child, disabled);
        }
    }

    private static void CollectByType<T>(Node node, List<T> into) where T : Node
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T match)
                into.Add(match);
            CollectByType(child, into);
        }
    }

    private static T? FindFirstOfType<T>(Node node) where T : Node
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T match)
                return match;
            T? found = FindFirstOfType<T>(child);
            if (found != null)
                return found;
        }
        return null;
    }

    // --- The listener gate ---------------------------------------------------------------

    public override void _Process(double delta)
    {
        PollLocalListener(delta);
        if (_isServer)
            AdvanceSchedule((float)delta);
    }

    /// <summary>Is a local listener actually inside the lab. Read from the active
    /// <see cref="Camera3D"/> rather than from an avatar reference: it is the ear's real position
    /// on every role that has one, it is null on a headless peer (which is exactly the answer we
    /// want there), and it needs no knowledge of who owns which body.</summary>
    private void PollLocalListener(double delta)
    {
        _listenerAccumulator += delta;
        if (_listenerAccumulator < ListenerPollSeconds)
            return;
        _listenerAccumulator = 0;

        Camera3D? camera = GetViewport()?.GetCamera3D();
        LabAmbience.Audible = camera != null
                              && AudibleBounds.HasPoint(ToLocal(camera.GlobalPosition));
    }

    // --- Dark beats & the lurker ----------------------------------------------------------

    /// <summary>The simulating peer's schedule. Structurally the source's
    /// <c>LabEnvironment._Process</c> with the same numbers, except that every beat it decides on
    /// is handed to <see cref="PlayBeat"/> for broadcast instead of being staged locally, and the
    /// walk-by's direction is chosen here rather than by each machine's own RNG.</summary>
    private void AdvanceSchedule(float dt)
    {
        _lurkerCooldown -= dt;
        _nextBeatIn -= dt;
        if (_nextBeatIn > 0f)
            return;

        // First beat: a guaranteed early eyes-only tease at the door glass, so a short visit is
        // never scare-free. It also arms the walk-by, which only happens after.
        if (_firstBeat)
        {
            _firstBeat = false;
            Broadcast(BeatKind.Tease, seconds: 1.2f, leftToRight: false);
            _lurkerHasAppeared = true;
            _lurkerCooldown = _rng.RandfRange(14f, 22f);
            _nextBeatIn = _rng.RandfRange(6f, 11f);
            return;
        }

        // The hero beat: a rare, long blackout that lasts EXACTLY as long as the corrupted thing
        // takes to cross the observation windows.
        if (_lurkerHasAppeared && _lurkerCooldown <= 0f && _rng.Randf() < 0.55f)
        {
            bool leftToRight = _rng.Randf() < 0.5f;
            float crossing = WalkByDarkSeconds();
            Broadcast(BeatKind.WalkBy, crossing, leftToRight);
            _lurkerCooldown = _rng.RandfRange(24f, 40f);
            _nextBeatIn = crossing + _rng.RandfRange(9f, 17f);
            return;
        }

        // Otherwise: a short nervous flicker, no lurker.
        float flicker = _rng.RandfRange(0.4f, 1.1f);
        Broadcast(BeatKind.Flicker, flicker, leftToRight: false);
        _nextBeatIn = flicker + _rng.RandfRange(5f, 11f);
    }

    /// <summary>Send a decided beat to every peer, including this one. Offline and a listen server
    /// take the direct call for <c>TvPortalHost.SendFlashTo</c>'s reason: with no multiplayer peer
    /// there is nobody to address, and an <c>Rpc</c> to ourselves is a round trip through the
    /// multiplayer API to reach a node we are already standing in.</summary>
    private void Broadcast(BeatKind kind, float seconds, bool leftToRight)
    {
        bool networked = NetworkManager.Instance is not null
                         && NetworkManager.Instance.Role != NetworkManager.SessionRole.None
                         && Multiplayer.MultiplayerPeer is not null;
        if (!networked)
        {
            PlayBeat((int)kind, seconds, leftToRight);
            return;
        }
        Rpc(nameof(PlayBeat), (int)kind, seconds, leftToRight);
    }

    /// <summary>Stage one beat locally. Every peer runs this and only this — no peer decides
    /// anything about timing, which is what makes the lights die on the same moment for everyone
    /// in the room. Deterministic given its arguments except for the per-fixture stagger, which is
    /// a few tens of milliseconds of "one dying circuit rather than nine bulbs" and is beneath
    /// anything a second player could notice.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
    private void PlayBeat(int kind, float seconds, bool leftToRight)
    {
        switch ((BeatKind)kind)
        {
            case BeatKind.Tease:
                StageTease();
                break;
            case BeatKind.WalkBy:
                StageWalkBy(leftToRight);
                break;
        }
        TriggerDarkBeat(seconds);
    }

    /// <summary>Eyes dead-centre in the door window.</summary>
    private void StageTease()
    {
        if (_lurker == null)
            return;
        _lurker.Position = new Vector3(8.9f, 0, -0.04f);
        _lurker.Appear(Lurker.Guise.EyesOnly, delay: 0.2f, seconds: 0.9f);
    }

    private const float GallerySpanX = 7.2f;
    private const float GalleryHallZ = 7.6f;
    private const float LurkerWalkSpeed = 2.6f; // m/s; the blackout lasts exactly one crossing
    private const float WalkEnterDelay = 0.25f;
    private const float WalkExitHold = 0.5f;

    /// <summary>How long the blackout must hold to cover one full crossing. Pure arithmetic on
    /// constants, so the server and every client compute the identical number and the server can
    /// schedule against it before anyone has staged anything.</summary>
    private static float WalkByDarkSeconds() =>
        WalkEnterDelay + (2f * (GallerySpanX + 2f) / LurkerWalkSpeed) + WalkExitHold;

    /// <summary>One crossing of the observation windows — a sideways shamble facing the glass, so
    /// its eyes catch the player. Enters and leaves in full dark.</summary>
    private void StageWalkBy(bool leftToRight)
    {
        if (_lurker == null)
            return;
        float startX = leftToRight ? -(GallerySpanX + 2f) : (GallerySpanX + 2f);
        float dir = leftToRight ? 1f : -1f;
        float travel = 2f * (GallerySpanX + 2f) / LurkerWalkSpeed;
        _lurker.AppearWalking(
            new Vector3(startX, 0, GalleryHallZ),
            new Vector3(dir, 0, 0),
            LurkerWalkSpeed,
            delay: WalkEnterDelay,
            seconds: travel);
    }

    /// <summary>Drops the whole fluorescent bank for <paramref name="seconds"/>, staggered a few
    /// frames per fixture so it reads as one dying circuit rather than nine bulbs.</summary>
    private void TriggerDarkBeat(float seconds)
    {
        if (_fixtures.Count == 0)
            return;
        foreach (FlickerLight fixture in _fixtures)
            fixture.BeginDark(_rng.RandfRange(0f, 0.12f), seconds + _rng.RandfRange(-0.08f, 0.08f));
        LabAmbience.PlayBuzzPop(this, _fixtures[_rng.RandiRange(0, _fixtures.Count - 1)].GlobalPosition);
    }
}
