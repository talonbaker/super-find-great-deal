using System;
using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using MpFoundation.Ui;
using Sail.Game.Bubble;
using Sail.Game.World.BubbleTest;

namespace Sail.Game.World;

/// <summary>
/// <b>The node that feeds <see cref="CadenceClock"/> from the local body, once per fixed tick.</b>
/// Client-local by construction: it reads the avatar this process drives (the predicted owner, or
/// the offline body in a lab) and sends nothing anywhere — the numbers leave the machine only
/// through the usage report at quit, and only as session totals.
///
/// <para><b>Created on demand, not always.</b> <see cref="Ensure"/> hangs one under the
/// <c>Telemetry</c> autoload — the one node present in every launch mode — the first time
/// something wants it: <c>PerfReadout</c> when <c>--bt-perf-readout</c> is passed, and
/// <c>Telemetry.BeginClientSession</c> on a real windowed client. A headless suite bot asks for
/// nothing and gets nothing, so the suite's per-tick cost is unchanged.</para>
///
/// <para><b>Reads in <c>_PhysicsProcess</c>, so it sees every motor tick exactly once</b> (the
/// clock's idempotency contract). The autoload sits above the scene in the tree, so this runs
/// BEFORE the avatar's step in the same physics frame and reads the PREVIOUS tick's result — a
/// one-tick lag on a readout that shows tenths of a second, and the price of not touching the
/// avatar's tick.</para>
///
/// <para><b>The one way the state can lie to the clock.</b> On the predicted owner a
/// reconciliation snap rewrites <c>_state</c> between two of these reads; a snap that raised
/// <c>Velocity.Y</c> by more than half a jump would read as a launch. That needs a mispredict of
/// ~4 m/s vertical, which the netcode's own drift measurement (6.4 mm) puts far outside normal
/// play; it is recorded rather than guarded because a guard would need the avatar to tell this
/// node when it snapped, and the avatar is closed to this packet.</para>
///
/// <para><b>"In a menu"</b> is the pause overlay being open or the tree being paused. Flow
/// screens that lock the body (tally, loss) leave <c>ControlLocked</c>/<c>Incapacity</c> set on
/// the state and are NOT treated as a menu — whether a body that cannot move should count as
/// stopped is a direction fork the report raises; the literal §A2-R3 definition is what ships.</para>
/// </summary>
public partial class CadenceTracker : Node
{
    /// <summary>Node name under the telemetry autoload, so a harness can find it.</summary>
    public const string NodeName = "CadenceTracker";

    public static CadenceTracker? Instance { get; private set; }

    /// <summary>The clock this node feeds. Readers (the readout, the usage report) read it; only
    /// the systems that know about an act the state cannot show call <see cref="CadenceClock.NoteAct"/>.</summary>
    public CadenceClock Clock { get; } = new();

    private SandboxAvatar? _avatar;
    private int _liveVersion = -1;
    private BubbleCounter? _counter;
    private PauseOverlay? _pause;

    /// <summary><b>Is the pause menu up right now?</b>
    ///
    /// <para>LEVEL-1 divergence from Sail (2026-09-04). Sail's LD-2 answered this with a
    /// <c>public static bool PauseOverlay.IsOpen</c> written by <c>Open</c>/<c>Close</c>/
    /// <c>_ExitTree</c>/<c>DoLeave</c>. <c>scripts/ui/**</c> is outside this packet's allowed
    /// paths (it belongs to the in-flight UI wave), so the same fact is read off the node
    /// instead of off a static this packet may not add. It is the SAME fact: every one of those
    /// four call sites sets <c>Visible</c> in lockstep with the static (<c>Open</c> true,
    /// <c>Close</c> false), and the two that do not — <c>_ExitTree</c> and <c>DoLeave</c> —
    /// are the node leaving the tree, which <see cref="GodotObject.IsInstanceValid"/> and the
    /// <c>IsInsideTree</c> check below catch directly.</para>
    ///
    /// <para>The lookup is cached and revalidated, so the per-tick cost is a validity check on
    /// the common path. Null — no overlay in this scene, as in a bare lab launch — is "not in a
    /// menu", which is what the static's <c>false</c> default said too. The tree walk that finds
    /// it runs ONCE per scene, not once per tick: a world with no overlay (the playground, a
    /// headless suite) must not pay a recursive search sixty times a second, so a failed search is
    /// remembered against the scene it failed in and retried only when the scene changes.</para>
    /// </summary>
    private bool IsPauseOverlayOpen()
    {
        if (_pause != null && (!GodotObject.IsInstanceValid(_pause) || !_pause.IsInsideTree()))
            _pause = null;
        Node? scene = GetTree().CurrentScene;
        if (_pause == null && !ReferenceEquals(scene, _pauseSearchedIn))
        {
            _pauseSearchedIn = scene;
            _pause = FindPauseOverlay(scene);
        }
        return _pause is { Visible: true };
    }

    /// <summary>The scene the (possibly fruitless) overlay search last ran against — see
    /// <see cref="IsPauseOverlayOpen"/>. Compared by reference, never dereferenced, so a freed
    /// scene here is harmless.</summary>
    private Node? _pauseSearchedIn;

    private static PauseOverlay? FindPauseOverlay(Node? root)
    {
        if (root == null)
            return null;
        if (root is PauseOverlay p)
            return p;
        foreach (Node child in root.GetChildren())
        {
            PauseOverlay? found = FindPauseOverlay(child);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>The tracker, creating it under the telemetry autoload if there is none. Null only
    /// when there is no autoload to hang it on (a bare scene-suite instantiation with no Boot).</summary>
    public static CadenceTracker? Ensure()
    {
        if (Instance != null && GodotObject.IsInstanceValid(Instance))
            return Instance;
        Node? host = MpFoundation.Telemetry.Telemetry.Instance;
        if (host == null || !GodotObject.IsInstanceValid(host))
            return null;
        var tracker = new CadenceTracker { Name = NodeName };
        host.AddChild(tracker);
        return tracker;
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        Unhook();
        if (Instance == this)
            Instance = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        SandboxAvatar? avatar = ResolveLocalAvatar();
        HookBubbleCounter();
        if (avatar == null)
            return;

        MotorTuning tuning = MotorTuning.Current;
        MoveState state = avatar.MotionNow;
        bool inMenu = IsPauseOverlayOpen() || GetTree().Paused;
        // The section is only ever needed to attribute a stop, and the lookup is seven AABB tests,
        // so it is skipped on every moving tick — and on every world but bubbletest, whose
        // footprints are the only ones the lookup knows (the playground's origin would otherwise
        // read as the hub).
        string? section = CadenceClock.IsStopped(tuning, state, inMenu) && InBubbleTest()
            ? CadenceSections.KeyAt(state.Position)
            : null;
        Clock.Feed(tuning, state, (float)delta, inMenu, section);
    }

    private static bool InBubbleTest() =>
        NetworkManager.Instance?.Options?.World == BubbleTestLayout.WorldId;

    /// <summary>The avatar this process drives, re-resolved only when the live set changes
    /// (a spawn, a leave, a resume replacing the node).</summary>
    private SandboxAvatar? ResolveLocalAvatar()
    {
        if (_avatar != null && !GodotObject.IsInstanceValid(_avatar))
            _avatar = null;
        if (SandboxAvatar.LiveVersion == _liveVersion && _avatar != null)
            return _avatar;
        _liveVersion = SandboxAvatar.LiveVersion;
        _avatar = null;
        foreach (SandboxAvatar a in SandboxAvatar.Live)
        {
            if (GodotObject.IsInstanceValid(a) && a.IsLocallyDriven)
            {
                _avatar = a;
                break;
            }
        }
        return _avatar;
    }

    /// <summary>A bubble popped by THIS peer is an act. The counter's <c>Changed</c> carries the
    /// popping peer, so no bubble code changes; a reset (<c>id == -1</c>) is not a pop.</summary>
    private void HookBubbleCounter()
    {
        BubbleCounter? now = BubbleCounter.Instance;
        if (now != null && !GodotObject.IsInstanceValid(now))
            now = null;
        if (ReferenceEquals(now, _counter))
            return;
        Unhook();
        _counter = now;
        if (_counter != null)
            _counter.Changed += OnBubbleChanged;
    }

    private void Unhook()
    {
        if (_counter != null && GodotObject.IsInstanceValid(_counter))
            _counter.Changed -= OnBubbleChanged;
        _counter = null;
    }

    private void OnBubbleChanged(int id, int count, int byPeer)
    {
        if (id < 0)
            return;
        int self = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
        if (byPeer == self)
            Clock.NoteAct("pop");
    }
}

/// <summary>
/// Which bubbletest section a world point is in, by position against
/// <see cref="BubbleTestLayout"/>'s footprints — the by-position split the packet named as the
/// fallback, chosen over <see cref="SectionVolume"/> because the volumes are server-only and log
/// to the console; they expose no current-section id a client can read. Engine-free so the
/// attribution is testable.
/// </summary>
public static class CadenceSections
{
    private static readonly BubbleTestLayout.Section[] Sections =
        (BubbleTestLayout.Section[])Enum.GetValues(typeof(BubbleTestLayout.Section));

    /// <summary>The section name at <paramref name="p"/>, or null off every footprint. Surface
    /// footprints are plan-only (zero height); the TV room's carries a real Y extent and is
    /// checked in Y as well, which is what keeps a body on the hub plaza from reading as the room
    /// twenty metres beneath it. Checked first for that reason.</summary>
    public static string? KeyAt(Vector3 p)
    {
        if (Contains(BubbleTestLayout.FootprintOf(BubbleTestLayout.Section.TvRoom), p))
            return nameof(BubbleTestLayout.Section.TvRoom);
        foreach (BubbleTestLayout.Section s in Sections)
        {
            if (s == BubbleTestLayout.Section.TvRoom)
                continue;
            if (Contains(BubbleTestLayout.FootprintOf(s), p))
                return s.ToString();
        }
        return null;
    }

    private static bool Contains(in Aabb f, Vector3 p)
    {
        if (f.Size == Vector3.Zero)
            return false;
        bool inPlan = p.X >= f.Position.X && p.X <= f.Position.X + f.Size.X
                      && p.Z >= f.Position.Z && p.Z <= f.Position.Z + f.Size.Z;
        if (!inPlan)
            return false;
        return f.Size.Y <= 0f || (p.Y >= f.Position.Y && p.Y <= f.Position.Y + f.Size.Y);
    }
}
