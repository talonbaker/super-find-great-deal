using System.Collections.Generic;
using Godot;
using MpFoundation;
using MpFoundation.Game.Contracts;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.Watcher;
using MpFoundation.Game.World;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// <b>The night-only creature, in a level that has no campfire.</b> Talon's easter-egg addendum
/// §4: <i>"A creepy entity that only appears at night (ties into the existing day-night cycle).
/// Reuse an existing creature already in the project … that vanishes/disappears when the player
/// gets close — the vanish-on-approach behaviour already works as-is, it just needs to spawn at
/// night in this level."</i>
///
/// <para><b>Nothing under <c>scripts/game/watcher/</c> is touched, and that is deliberate rather
/// than incidental.</b> <see cref="WatcherBrain"/>'s class doc says outright that there are two
/// states and "deliberately no third … a state machine that can express them is one refactor away
/// from performing them". Everything below is a HOST: it constructs the shipped node, feeds it the
/// three providers it asks for, and gates it. The vanish-on-approach is
/// <see cref="WatcherTuning.ApproachArmM"/> / <see cref="WatcherTuning.HardStandoffM"/> exactly as
/// shipped, untuned.</para>
///
/// <para><b>The night gate is a filter on the PEER SOURCE, not a new state.</b> The brain appears
/// when some peer's exposure clears <see cref="WatcherTuning.AppearVisibility"/>; hand it an empty
/// peer list and it has no candidate, so it cannot appear and — if it is already standing there —
/// withdraws through the shipped <see cref="WatcherExit.NoCandidate"/> path. Day is therefore
/// expressed in the creature's own vocabulary, with no branch inside it and no third state. The
/// clock is <c>OutdoorAtmosphere</c>'s, read through
/// <see cref="NightAidDriver.DarknessAt"/> — the same curve the level's night aids already run on.
/// <b>There is no second clock and no second darkness authority</b>
/// (<c>.claude/rules/single-writer.md</c>).</para>
///
/// <para><b>"Lit ground" in a level with no light.</b> The watcher's placement is defined relative
/// to a campfire: it stands <see cref="WatcherTuning.StandoffMinM"/>..<c>StandoffMaxM</c> beyond
/// <c>INightPressure.LitRadiusM</c>, and <c>CampVisibilityScore</c> attenuates a player's exposure
/// to 0.15 of itself while they are inside that radius. This level has no fire and builds none, so
/// the honest lit radius is <b>ZERO</b> — and it has to be, because any positive radius would put
/// every nearby player "in the light", drop their exposure below
/// <see cref="WatcherTuning.AppearVisibility"/> (0.35) and mean the creature never appears at all.
/// With zero, exposure is stance × motion alone: a standing player still reads 0.55 and a running
/// one 1.00, both over the bar, and the "never stands on lit ground" invariant is satisfied
/// vacuously rather than softened.</para>
///
/// <para><b>What the ring is anchored on instead: whoever has drifted from the group.</b> With a
/// zero radius the two search rings sit 11 m and 21 m from <c>FireOrigin</c>, and the placement
/// then requires 18..62 m to the target — so the anchor must be NEAR a player or nothing is ever
/// admissible. The plan centroid works for a huddle and fails for a scattered party (the ring ends
/// up 80 m from everybody). Anchoring on the peer furthest from the centroid always leaves the
/// 21 m ring inside the band for that peer, and it says the right thing: in a level with no fire,
/// the group is the fire, and the one who walked away from it is the one that gets watched. A
/// refusal is still a normal outcome — <c>WatcherPlacement</c>'s own doc says so — and a tick
/// where every candidate is inside somebody's view cone simply produces nothing.</para>
///
/// <para><b>Late join is not optional.</b> A joiner who arrives mid-sighting reads "no creature",
/// which is the most convincing possible wrong answer. This rides
/// <c>MultiplayerApi.PeerConnected</c> directly, the idiom <c>BubbleCounter</c> already uses in
/// this level for the same reason: same moment, same reliability, one fewer thing for the world to
/// remember.</para>
/// </summary>
public partial class BubbleTestWatcher : Node
{
    /// <summary>The node name this host is added under. The watcher itself keeps
    /// <see cref="Watcher.NodeName"/> so its RPC path is the shipped one.</summary>
    public const string NodeName = "BubbleTestWatcher";

    /// <summary>
    /// <b>How dark it has to be.</b> <see cref="NightAidDriver.DarknessAt"/> runs 0 at noon to 1 at
    /// deep night off <c>OutdoorAtmosphere</c>'s own <c>NightFactor</c> curve; the shipped probe
    /// reads 0.00 at midday (phase 0.275), the dusk band opens at 0.550 and night proper at 0.650.
    ///
    /// <para>0.55 puts the creature's window at "after dusk has actually taken hold", not at the
    /// first grey of evening — a thing that appears while you can still see the whole level is a
    /// prop, not a fright. It is a VALUE, picked and stated: raise it toward 0.9 for deep-night
    /// only, lower it toward 0.3 to have it out at dusk.</para></summary>
    public const float NightGateDarkness = 0.55f;

    /// <summary>Hysteresis on the gate, so a clock sitting on the threshold cannot make the
    /// creature strobe in and out on consecutive frames. Once open, the window stays open until
    /// darkness falls this far BELOW <see cref="NightGateDarkness"/>.</summary>
    public const float NightGateHysteresis = 0.05f;

    /// <summary>
    /// <b>The lit radius this level reports: NO lit ground at all.</b> See the class doc for why
    /// "none" is the honest answer here rather than a stub, and named as a constant because a
    /// future packet that lights this level (string lights, a fire) has exactly one number to
    /// change.
    ///
    /// <para><b>Why it is −0.01 and not 0, which is a real defect and was measured, not
    /// foreseen.</b> <c>CampVisibilityScore.Refresh</c> decides "inside lit ground" with
    /// <c>planarDist &lt;= lit</c> — an INCLUSIVE bound. The standoff anchor is one player's own
    /// position (see the class doc), so that player's planar distance to it is exactly 0, and at
    /// <c>lit = 0</c> the comparison is <c>0 &lt;= 0</c>: TRUE. Their exposure is then attenuated
    /// to 0.15 of itself — 0.08 standing still, 0.12 walking — against an
    /// <see cref="WatcherTuning.AppearVisibility"/> of 0.35, so in a one-player session NOBODY is
    /// ever exposed enough and the creature can never appear. A full night-phase server run said
    /// exactly that: <c>exposure(best peer=…)=0.08 vs appear 0.35 placement=not-asked</c>, fifty
    /// times, with every other number correct.</para>
    ///
    /// <para>A radius below zero contains no point at all, which is what "there is no fire" means
    /// and is the only value that survives the inclusive bound. Everything downstream is unmoved:
    /// <c>WatcherPlacement</c>'s rings are <c>lit + 11</c> and <c>lit + 21</c> (10.99 and 20.99 m,
    /// a centimetre off where they were), and <see cref="WatcherExit.LightReachedIt"/>'s
    /// <c>distance &lt;= litRadiusM</c> is unreachable for a non-negative distance, which is the
    /// correct reading of "the light can never reach it because there is none".</para></summary>
    public const float LitRadiusM = -0.01f;

    /// <summary>True while the night window is open on this peer. Read by the self-test and by
    /// captures; the creature's own presence is <c>Watcher.Instance.State</c>.</summary>
    public bool NightWindowOpen { get; private set; }

    /// <summary>The darkness last read, for the log line and for capture evidence.</summary>
    public float Darkness { get; private set; }

    /// <summary>The watcher this host owns, or null before <c>_Ready</c>.</summary>
    public Watcher? Creature { get; private set; }

    private bool _isServer;
    private bool _subscribed;
    private double _sinceLog;
    private WatcherState _lastLoggedState = WatcherState.Absent;
    private readonly List<(int PeerId, Vector3 Position, Vector3 Forward)> _views = new();
    private readonly List<(int PeerId, Vector3 Position, Vector3 Velocity)> _motion = new();

    /// <summary>Where the avatars are. Injected by the world rather than walked from here, so this
    /// class does not have to know how <c>Gameplay</c> parents its <c>Players</c> node.</summary>
    public System.Func<IEnumerable<SandboxAvatar>>? Avatars { get; set; }

    public override void _Ready()
    {
        _isServer = NetworkManager.Instance is null
                    || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;

        var visibility = new CampVisibilityScore(
            () => _motion, () => Creature?.FireOrigin ?? Vector3.Zero, () => LitRadiusM);
        var pressure = new NoFirePressure();

        // Named so the RPC path resolves identically on every peer, and set BEFORE AddChild -- the
        // shipped construction order in Gameplay.SetUpWatcher, for the same reason.
        var watcher = new Watcher { Name = Watcher.NodeName };
        AddChild(watcher);
        Creature = watcher;
        watcher.Setup(NetworkManager.Instance?.Role switch
        {
            NetworkManager.SessionRole.Server => WatcherNetRole.Server,
            NetworkManager.SessionRole.Client => WatcherNetRole.Client,
            _ => WatcherNetRole.Offline,
        });
        // ALL THREE PROVIDERS, and NightPressure is the one that is easy to leave out: Watcher's
        // _Process early-returns SILENTLY when any of NightPressure / Visibility / PeerSource is
        // null, so a host that sets two of the three gets a creature that never appears, never
        // logs and never errors. (Measured here: the first version of this file omitted it, and a
        // full night-phase server run produced a heartbeat reading darkness=0.000 forever because
        // GatherViews was never called at all.)
        watcher.NightPressure = pressure;
        watcher.Visibility = visibility;
        // The level's authored ground plane. Every surface section's plate top is y = 0 (hub, red,
        // blue's apron, cyan and the tangle all measure 0.000 in GUARD-1's own ray dump), so a flat
        // sampler is right for the ground the creature can stand on. Green's heightfield and the
        // summits are the known exception and are stated rather than papered over: a sighting
        // anchored on a player up a tower puts the body at plaza level, small and far below, which
        // is a legible silhouette rather than a floating one. A per-candidate raycast was the
        // alternative and costs 144 rays per attempt on a search that retries every tick while it
        // is refusing.
        watcher.GroundHeight = (_, _) => 0f;
        watcher.PeerSource = GatherViews;

        if (Multiplayer is not null && _isServer)
        {
            Multiplayer.PeerConnected += OnPeerConnected;
            _subscribed = true;
        }

        GD.Print($"[bubbletest.watcher] host ready — server={_isServer} "
                 + $"gate={NightGateDarkness:F2} litRadius={LitRadiusM:F1}");
    }

    public override void _ExitTree()
    {
        if (_subscribed && Multiplayer is not null)
            Multiplayer.PeerConnected -= OnPeerConnected;
        _subscribed = false;
    }

    private void OnPeerConnected(long id) => Creature?.SendSyncTo((int)id);

    /// <summary>
    /// <b>The peer list the brain sees — empty by day, and that IS the gate.</b>
    ///
    /// <para>Also the one place per tick the exposure scorer is refreshed and the standoff anchor
    /// is moved, because both have to be computed from the same snapshot of the same bodies: a
    /// scorer refreshed against one frame and an anchor taken from another is exactly the kind of
    /// half-tick disagreement that makes a creature appear somewhere nobody can explain.</para>
    /// </summary>
    private IReadOnlyList<(int PeerId, Vector3 Position, Vector3 Forward)> GatherViews()
    {
        _views.Clear();
        _motion.Clear();

        Darkness = CurrentDarkness();
        // Hysteresis in the direction that matters: opening needs the full threshold, staying open
        // only needs to clear it by the band, so a clock parked on the line cannot strobe.
        float bar = NightWindowOpen ? NightGateDarkness - NightGateHysteresis : NightGateDarkness;
        bool open = Darkness >= bar;
        if (open != NightWindowOpen)
        {
            NightWindowOpen = open;
            GD.Print($"[bubbletest.watcher] night window {(open ? "OPEN" : "CLOSED")} — "
                     + $"darkness={Darkness:F3} bar={bar:F2}");
        }

        if (Avatars is null || !open)
            return _views;   // day: no candidates, so the brain cannot appear and withdraws.

        Vector3 sum = Vector3.Zero;
        int n = 0;
        foreach (SandboxAvatar a in Avatars())
        {
            Vector3 p = a.GlobalPosition;
            _views.Add((a.OwnerPeerId, p, -a.GlobalTransform.Basis.Z));
            _motion.Add((a.OwnerPeerId, p, a.Velocity));
            sum += p;
            n++;
        }
        if (n == 0)
            return _views;

        // The anchor: whoever is furthest from the group's own plan centroid. See the class doc --
        // with a zero lit radius the rings are 11 m and 21 m out, so the anchor has to sit near
        // somebody or the 18 m floor rejects every candidate.
        Vector3 centre = sum / n;
        Vector3 anchor = _views[0].Position;
        float best = -1f;
        foreach ((int _, Vector3 p, Vector3 _) in _views)
        {
            float d = new Vector2(p.X - centre.X, p.Z - centre.Z).LengthSquared();
            if (d <= best) continue;
            best = d;
            anchor = p;
        }
        if (Creature is not null)
            Creature.FireOrigin = new Vector3(anchor.X, 0f, anchor.Z);

        // AFTER the anchor moves and BEFORE the brain reads a score: CampVisibilityScore.Refresh
        // reads the anchor through the closure above, and the whole point of its own doc's
        // "called once per watcher tick, before the brain reads any of it" is that every peer is
        // scored against the same anchor.
        (Creature?.Visibility as CampVisibilityScore)?.Refresh();
        return _views;
    }

    /// <summary>
    /// <b>This level's <see cref="INightPressure"/>: no fire, so no lit ground.</b>
    ///
    /// <para>The interface exists for camp, where escalation is spatial and the lit radius shrinks
    /// night over night. The bubble test has neither a campfire nor a night index — it is a
    /// movement and collectible testbed with a day/night cycle over it — so the honest answer to
    /// both questions is the smallest one, and <see cref="BubbleTestWatcher.LitRadiusM"/> carries
    /// the reasoning for why zero is a real answer here rather than a stub.</para>
    ///
    /// <para><c>NightIndex</c> is 1 because the interface documents the range as 1..5 and the
    /// watcher reads it for nothing; returning 0 would be out of contract for no gain.</para>
    /// </summary>
    private sealed class NoFirePressure : INightPressure
    {
        public int NightIndex => 1;

        public float LitRadiusM => BubbleTestWatcher.LitRadiusM;
    }

    /// <summary>
    /// <b>Why nothing appeared, as numbers.</b> A creature that is correctly refusing and a
    /// creature that is broken look identical from outside — <c>WatcherBrain.TickAbsent</c> returns
    /// silently for three different reasons (cooldown, nobody exposed enough, every candidate stand
    /// position already in somebody's view) and prints none of them.
    ///
    /// <para>This re-runs the same two questions the brain asks, READ-ONLY, off the same peer list
    /// and the same shipped <see cref="WatcherPlacement"/>, so the heartbeat can name which of the
    /// three it is. It changes nothing: the seed is a throwaway, the result is discarded, and the
    /// brain's own answer is untouched. It exists because the first night run of this feature
    /// produced fifty identical <c>state=Absent</c> lines and no way to tell a working refusal from
    /// a dead one.</para></summary>
    private string Diagnose()
    {
        if (Creature is null || _views.Count == 0)
            return "exposure=- placement=-";
        IVisibilityScore? vis = Creature.Visibility;
        float best = -1f;
        int bestPeer = -1;
        foreach ((int peer, Vector3 _, Vector3 _) in _views)
        {
            float v = vis?.VisibilityFor(peer) ?? -1f;
            if (v <= best) continue;
            best = v;
            bestPeer = peer;
        }
        string exposure = $"exposure(best peer={bestPeer})={best:F2} vs appear "
                          + $"{WatcherTuning.Default.AppearVisibility:F2}";
        if (best < WatcherTuning.Default.AppearVisibility)
            return exposure + " placement=not-asked";

        var views = new List<WatcherPeerView>(_views.Count);
        foreach ((int peer, Vector3 pos, Vector3 fwd) in _views)
            views.Add(new WatcherPeerView(peer, vis?.VisibilityFor(peer) ?? 0f, pos, fwd));
        WatcherPeerView target = views[0];
        foreach (WatcherPeerView v in views)
            if (v.PeerId == bestPeer) target = v;
        bool ok = WatcherPlacement.TryChoose(Creature.FireOrigin, LitRadiusM, in target, views,
                                             WatcherTuning.Default, (_, _) => 0f, 12345u,
                                             out Vector3 stand);
        return exposure + $" placement={(ok ? $"ok at {stand}" : "REFUSED (every ring candidate is "
                          + "out of the 18..62 m band or inside a view cone)")}";
    }

    /// <summary>The level's one darkness read. <c>NightAidDriver.DarknessAt</c> is a pure static
    /// over <c>OutdoorAtmosphere.Evaluate</c>, so this adds no clock and no second authority — and
    /// the <c>bt_darkness</c> shader global is deliberately NOT read back, because
    /// <c>RenderingServer.GlobalShaderParameterGet</c> is editor-only and returns a Nil Variant in
    /// a running project, which looks exactly like the global being unset.</summary>
    private static float CurrentDarkness()
    {
        if (CycleDriver.Instance is not { Synced: true } driver)
            return 0f;   // an unsynced clock is not night; day is the safe answer.
        return NightAidDriver.DarknessAt(driver.Phase, driver.CyclesElapsed);
    }

    public override void _Process(double delta)
    {
        if (Creature is null)
            return;
        // One line per state change, plus a slow heartbeat: "the creature never appeared" and "the
        // creature appeared and I was facing the other way" are different findings and a headless
        // capture has to be able to tell them apart.
        WatcherState state = Creature.State;
        _sinceLog += delta;
        if (state != _lastLoggedState)
        {
            _lastLoggedState = state;
            GD.Print($"[bubbletest.watcher] state={state} target={Creature.TargetPeerId} "
                     + $"stand={Creature.StandPosition} lastExit={Creature.LastExit} "
                     + $"darkness={Darkness:F3} night={NightWindowOpen}");
        }
        else if (_sinceLog >= 5.0)
        {
            _sinceLog = 0.0;
            GD.Print($"[bubbletest.watcher] heartbeat state={state} darkness={Darkness:F3} "
                     + $"night={NightWindowOpen} peers={_views.Count} {Diagnose()}");
        }
    }
}
