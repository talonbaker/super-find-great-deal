using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The BT-6 fixture: six bubbles at known points, in a real networked session.</b> Installed
/// by <c>Gameplay</c> when <c>--bubble-selftest</c> is passed — to the server AND to every bot,
/// because the whole design is that each peer builds the same bubbles locally and the only thing
/// that travels is the tally (program D6).
///
/// <para><b>This is a fixture, not a level.</b> It builds geometry in code, which the bubble-test
/// level itself may not do (program D2, "no procedural generation" = nothing built at runtime in
/// the played scene). D2 governs the played scene; a test harness that spawns six spheres into
/// the CI "open" world so a three-peer convergence assertion has something to converge on is the
/// same kind of object as <c>--spawn-entity</c> or any other test-seeding flag. The real
/// placement is BT-11's, in <c>.tscn</c> files, from <c>Bubble.tscn</c>.</para>
///
/// <para><b>The layout is chosen to make the walk deterministic.</b> Bot A connects first, so it
/// takes <c>GameWorld.SpawnPoints[0]</c> = (4, 1.1, 0) (<c>Gameplay.OnPeerConnected</c> hands out
/// <c>_spawnIndex++</c>), and walks with <c>--goto-script 4,24</c> straight up the x = 4
/// corridor — which is clear of every piece of <c>GameWorld</c> furniture. Bubbles 2 and 4 sit
/// ON that corridor; 0, 1, 3 and 5 sit 8 m to either side, twenty times the collider radius
/// away. So "A pops exactly 2 and 4" is geometry, not timing.</para>
/// </summary>
public partial class BubbleSelfTest : Node3D
{
    /// <summary>Node name, so the fixture is findable in a log or a remote-tree dump.</summary>
    public const string NodeName = "BubbleSelfTest";

    private const string BubbleScenePath = "res://scenes/game/props/Bubble.tscn";

    /// <summary>The fixture on this peer, or null on any launch without <c>--bubble-selftest</c>.
    /// Read by <c>BotHarness</c> for the JSONL's worst-offset column — the same static-instance
    /// convention <c>BubbleCounter</c>, <c>CycleDriver</c> and <c>PropManager</c> use. Deliberately
    /// on the FIXTURE and not on the counter: tracking the worst offset means a distance per
    /// bubble per frame, which is free for six and pointless for a hundred in a played level.</summary>
    public static BubbleSelfTest? Instance { get; private set; }

    /// <summary>Bubble height. The avatar capsule runs from the ground to its crown (~1.2 m), so
    /// a 0.35 m collider centred here is squarely inside a walking body — the test must fail on
    /// a broken tally, never on a near miss.</summary>
    private const float BubbleY = 0.8f;

    /// <summary>The six fixture positions, in adoption order. Index == the id
    /// <see cref="BubbleCounter.AdoptAuthoredBubbles"/> assigns, because the children are named
    /// Bubble00..Bubble05 and ids come from an ordinal node-path sort.</summary>
    public static readonly Vector3[] Positions =
    {
        new(-4f, BubbleY, 8f),   // 0 — off the corridor. Also the criterion-5 control's subject.
        new(12f, BubbleY, 8f),   // 1 — off the corridor.
        new(4f, BubbleY, 8f),    // 2 — ON the corridor: bot A's first pop.
        new(-4f, BubbleY, 16f),  // 3 — off the corridor.
        new(4f, BubbleY, 16f),   // 4 — ON the corridor: bot A's second pop.
        new(12f, BubbleY, 16f),  // 5 — off the corridor.
    };

    /// <summary>How many of the six fixture bubbles bot A's corridor walk pops — ids 2 and 4.
    /// Named because two independent things key off it: the harness's expected tally, and the
    /// client forge's trigger below.</summary>
    public const int CorridorPops = 2;

    /// <summary>
    /// <b>Acceptance criterion 5's positive control, and it has to be a positive one.</b> This
    /// long after a NON-server peer first SEES the tally reach <see cref="CorridorPops"/>, that
    /// peer locally hides bubble 0 — the strongest unilateral action a client can take on a
    /// bubble, there being no code path by which one can pop (see the audit test in
    /// <c>BubbleCounterStateTests</c>). The suite then asserts that no peer's popped set ever
    /// contains id 0 and that every peer's count is still 2, while at least one peer's HIDDEN set
    /// does contain 0 — which is what makes the absence an observation rather than a claim.
    ///
    /// <para>Relative to the observed tally rather than to a fixed second of this peer's own
    /// session, because the late joiner starts its clock ten-odd seconds after everyone else and
    /// an absolute mark would put its control on the far side of the reset.</para>
    /// </summary>
    public const double ClientForgeDelaySec = 3.0;

    /// <summary>BT-8: where the fixture's reset lever stands. Twelve metres off the corridor bot A
    /// walks and eight from the nearest bubble — far enough that no bot can brush it and no
    /// bubble's collider can overlap it, so adding it cannot perturb the pop geometry this
    /// fixture's determinism argument rests on.</summary>
    private static readonly Vector3 LeverPos = new(-12f, 0f, 0f);

    /// <summary>LEVER-1: the gap between the scripted arming pulls and the scripted confirming
    /// ones. Comfortably past <c>BubbleResetConfirm.MinDwellSec</c> (so the confirms are not
    /// swallowed as a double-tap) and comfortably inside <c>ConfirmWindowSec</c> (so they are not
    /// swallowed as a lapse) — derived from those two constants rather than typed, so a change to
    /// either moves the fixture with it instead of silently making this suite meaningless.
    ///
    /// <para><b>Public, and announced in the server's log, since FIX-1.</b> The harness has to
    /// size every bot's lifetime so that it outlasts the reset, and the reset does not land at
    /// <see cref="ResetAtSec"/> — it lands this much later, because the lever is a two-stage
    /// press. A harness that retyped the number would be a third independent copy of a timing
    /// that already has two, which is precisely the trap FIX-1 exists to close.</para>
    /// </summary>
    public static readonly double ConfirmDelaySec =
        (BubbleResetConfirm.MinDwellSec + BubbleResetConfirm.ConfirmWindowSec) / 2.0;

    /// <summary>The log line the server prints once, at <c>_Ready</c>, so
    /// <c>tests/Run-BubbleSyncTest.ps1</c> can DERIVE its bot lifetimes from the reset schedule
    /// instead of typing constants beside it. Invariant-formatted on purpose: it is parsed.
    /// Named so a grep for the harness's regex lands here.</summary>
    public const string ResetSchedulePrefix = "[bubbletest] reset schedule:";

    /// <summary>CELEBRATE-1: how long after the reset LANDS the fixture pops every bubble again.
    /// Long enough that the reset broadcast has provably reached every peer first (the sync suite
    /// measures that at well under a second on loopback) and short enough to keep the celebrate
    /// suite near the sync suite's ~40 s. It is a MARGIN, not a mark — the mark it produces is
    /// derived from the lever's own two constants and announced, never typed in a harness.</summary>
    public const double RecompleteDelaySec = 3.0;

    /// <summary>CELEBRATE-1's schedule line, printed once by the server at <c>_Ready</c> so
    /// <c>tests/Run-CelebrateTest.ps1</c> can DERIVE every bot lifetime and every assertion window
    /// from the two completion marks instead of retyping them. Invariant-formatted: it is
    /// parsed.</summary>
    public const string CelebrateSchedulePrefix = "[bubbletest] celebrate schedule:";

    /// <summary>The two pretend peers the fixture pulls the lever as. Two DIFFERENT ids on
    /// purpose: warnings are per peer, so one peer pulling twice would be one player deciding,
    /// and the case worth measuring here is two players each acting alone.</summary>
    private const int StrayPeer = 1;

    /// <summary>The second of the two. See <see cref="StrayPeer"/>.</summary>
    private const int SecondPeer = 2;

    private BubbleCounter? _counter;
    private BubbleResetLever? _lever;
    private readonly List<Bubble> _bubbles = new();
    private bool _isServer;
    private double _clock;
    private double _resetAtSec = -1;
    private bool _resetArmed;
    private bool _resetFired;
    private bool _forgeFired;
    private double _forgeArmedAt = -1;
    private double _popAllAtSec = -1;

    /// <summary>Worst offset from the authored position seen on this peer, over the whole run,
    /// across every bubble (acceptance criterion 6). Sampled every frame, never reset.</summary>
    public float MaxOffsetM { get; private set; }

    /// <summary>Server-only: seconds into the session at which <c>ServerReset</c> fires.
    /// Negative = never. Set from <c>--bubble-reset-at</c> before this node enters the tree.</summary>
    public double ResetAtSec { get => _resetAtSec; set => _resetAtSec = value; }

    /// <summary>Server or client — the counter needs it before adoption.</summary>
    public bool IsServer { get => _isServer; set => _isServer = value; }

    /// <summary>CELEBRATE-1, server-only: seconds into the session at which every remaining bubble
    /// is popped through <c>BubbleCounter.ServerPop</c>. Negative = never. Set from
    /// <c>--bubble-pop-all-at</c> before this node enters the tree.</summary>
    public double PopAllAtSec { get => _popAllAtSec; set => _popAllAtSec = value; }

    /// <summary>When the fixture pops everything for the SECOND time — the reset-then-recomplete
    /// half of CELEBRATE-1's idempotency proof. Derived from the reset's own landing time, so it
    /// moves with <c>--bubble-reset-at</c> and with either <c>BubbleResetConfirm</c> constant.
    /// Negative when there is no first pop-all or no reset to recomplete after.</summary>
    public double RecompleteAtSec =>
        _popAllAtSec < 0 || _resetAtSec < 0 ? -1 : _resetAtSec + ConfirmDelaySec + RecompleteDelaySec;

    public override void _Ready()
    {
        Instance = this;
        var packed = GD.Load<PackedScene>(BubbleScenePath);
        if (packed == null)
        {
            GD.PushError($"[bubbletest] fixture could not load {BubbleScenePath}");
            return;
        }
        for (int i = 0; i < Positions.Length; i++)
        {
            var bubble = packed.Instantiate<Bubble>();
            bubble.Name = $"Bubble{i:D2}";
            // BEFORE AddChild: a node already in the tree runs its child's _Ready during AddChild,
            // and Bubble._Ready is what latches the authored position every idle offset is
            // measured against. Positioning afterwards would authored-anchor all six at the origin.
            bubble.Position = Positions[i];
            AddChild(bubble);
            _bubbles.Add(bubble);
        }
        _counter = new BubbleCounter { Name = BubbleCounter.NodeName };
        AddChild(_counter);
        _counter.Setup(_isServer);
        _counter.AdoptAuthoredBubbles(this);

        // BT-8: the reset lever, on EVERY peer and at the same NodePath, because that is how its
        // RPC resolves without any spawn traffic (the same rule the counter above relies on). It
        // stands well clear of the six bubbles and of bot A's x = 4 corridor, so it changes
        // nothing about the walk this fixture's determinism argument rests on.
        var leverScene = GD.Load<PackedScene>(BubbleResetLever.ScenePath);
        if (leverScene == null)
        {
            GD.PushWarning($"[bubbletest] fixture could not load {BubbleResetLever.ScenePath}; "
                           + "the reset falls back to a direct ServerReset.");
        }
        else
        {
            _lever = leverScene.Instantiate<BubbleResetLever>();
            _lever.Name = BubbleResetLever.NodeName;
            _lever.IsServer = _isServer; // before AddChild: _Ready reads it.
            AddChild(_lever);
            _lever.Position = LeverPos;
        }

        // Acceptance criterion 7, proved as arithmetic on this peer as well as in xUnit, so a
        // build where CycleBands moved under us says so in the session log rather than only in a
        // suite nobody reruns. Noon is mid-day-band; deep night is AtmosphereBreakpoints index 6.
        float[] keys = MpFoundation.Game.World.CycleBands.AtmosphereBreakpoints(0);
        float noon = BubbleOscillation.EmissionEnergy(keys[1], 0);
        float deepNight = BubbleOscillation.EmissionEnergy(keys[6], 0);
        GD.Print($"[bubbletest] glow: noon={noon:F3} deepNight={deepNight:F3} " +
                 $"(expect 0 and > 0.5) bubbles={_counter.BubbleCount} server={_isServer}");

        // FIX-1: the reset schedule, announced by the only peer that owns it, at the moment
        // _clock starts. The harness reads resetLandsAt off this line and sizes EVERY bot's
        // lifetime from it, so a change to --bubble-reset-at, to MinDwellSec or to
        // ConfirmWindowSec moves the sampling windows with it. Before FIX-1 the late joiner's
        // window was a typed 26 s that ended 141 ms before the reset, and the suite's late-joiner
        // reset assertion had therefore never executed once.
        if (_isServer && _resetAtSec >= 0)
        {
            var inv = CultureInfo.InvariantCulture;
            GD.Print($"{ResetSchedulePrefix} resetAt={_resetAtSec.ToString("F3", inv)}s "
                     + $"confirmDelay={ConfirmDelaySec.ToString("F3", inv)}s "
                     + $"resetLandsAt={(_resetAtSec + ConfirmDelaySec).ToString("F3", inv)}s "
                     + $"(minDwell={BubbleResetConfirm.MinDwellSec.ToString("F3", inv)}s "
                     + $"confirmWindow={BubbleResetConfirm.ConfirmWindowSec.ToString("F3", inv)}s)");
        }

        // CELEBRATE-1: the completion schedule is ONE class (BubbleCompletionSchedule), shared
        // with the real level's capture run, so the fixture and the played world cannot drift
        // apart about what "pop everything" means. It is added here rather than by Gameplay
        // because only the fixture can derive the RECOMPLETION mark: that mark is the lever's own
        // landing time plus a margin, and the lever is the fixture's.
        if (_popAllAtSec >= 0)
        {
            AddChild(new BubbleCompletionSchedule
            {
                Name = BubbleCompletionSchedule.NodeName,
                IsServer = _isServer,
                PopAllAtSec = _popAllAtSec,
                RecompleteAtSec = RecompleteAtSec,
            });
        }

        // CELEBRATE-1: both completion marks, announced by the only peer that owns them, at the
        // moment _clock starts. Same discipline as the reset schedule above and for the same
        // reason — the harness sizes every bot's lifetime off this line, so a change to
        // --bubble-pop-all-at, to --bubble-reset-at or to either lever constant moves the
        // sampling windows with it instead of silently sliding past them.
        if (_isServer && _popAllAtSec >= 0)
        {
            var inv = CultureInfo.InvariantCulture;
            GD.Print($"{CelebrateSchedulePrefix} popAllAt={_popAllAtSec.ToString("F3", inv)}s "
                     + $"recompleteAt={RecompleteAtSec.ToString("F3", inv)}s "
                     + $"(recompleteDelay={RecompleteDelaySec.ToString("F3", inv)}s after the reset lands) "
                     + $"bubbles={_counter.BubbleCount}");
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        foreach (Bubble bubble in _bubbles)
        {
            if (bubble.Popped)
                continue;
            float off = bubble.OffsetFromAuthoredM;
            if (off > MaxOffsetM)
                MaxOffsetM = off;
        }
        if (_counter == null)
            return;
        if (!_forgeFired && !_isServer)
        {
            if (_forgeArmedAt < 0 && _counter.Count >= CorridorPops)
                _forgeArmedAt = _clock;
            if (_forgeArmedAt >= 0 && _clock >= _forgeArmedAt + ClientForgeDelaySec)
            {
                _forgeFired = true;
                if (_bubbles.Count > 0 && !_bubbles[0].Popped)
                    _bubbles[0].ApplyPopped();
                GD.Print($"[bubbletest] client-side forge: bubble 0 hidden locally, tally still "
                         + $"{_counter.Count}");
            }
        }
        if (_isServer && !_resetArmed && _resetAtSec >= 0 && _clock >= _resetAtSec)
        {
            _resetArmed = true;
            // LEVER-1: stage one. TWO peers each pull the lever ONCE and neither of them is
            // followed by anything. Before LEVER-1 that was two resets; after it, it is two
            // warnings and a board still standing — Talon's own sentence ("someone reset my
            // progress") measured in a real three-peer session rather than only in xUnit.
            // The tally is printed either side of the pulls, because "the stray press did
            // nothing" is worth nothing unless the press demonstrably happened.
            if (_lever != null)
            {
                int before = _counter.Count;
                _lever.ServerPress(StrayPeer);
                _lever.ServerPress(SecondPeer);
                GD.Print($"[bubbletest] lever fixture: two stray pulls, armed="
                         + $"{_lever.ArmedPresses} accepted={_lever.AcceptedPresses} "
                         + $"tally {before} -> {_counter.Count}");
            }
        }
        if (_isServer && _resetArmed && !_resetFired && _clock >= _resetAtSec + ConfirmDelaySec)
        {
            _resetFired = true;
            // LEVER-1: stage two. Both peers now confirm, in the same frame, well inside the
            // warning window and well inside the 1.2 s debounce of each other. Three things fall
            // out, none of them available from a direct ServerReset:
            //   * a deliberate confirmed press DOES reset — the companion bug to the one above,
            //     and the easier of the two to ship;
            //   * BT-8's acceptance criterion 4 survives the new gate: two confirms inside the
            //     cooldown produce exactly ONE reset broadcast, one accepted pull and one refusal;
            //   * this suite's reset assertions (every peer back to 0, every bubble visible) stay
            //     assertions about the LEVER — the path a player actually uses.
            if (_lever != null)
            {
                _lever.ServerPress(StrayPeer);
                _lever.ServerPress(SecondPeer);
                GD.Print($"[bubbletest] lever fixture: accepted={_lever.AcceptedPresses} "
                         + $"refused={_lever.RefusedPresses} armed={_lever.ArmedPresses} "
                         + $"confirmed={_lever.ConfirmedPresses}");
            }
            else
            {
                _counter.ServerReset();
            }
        }
    }
}
