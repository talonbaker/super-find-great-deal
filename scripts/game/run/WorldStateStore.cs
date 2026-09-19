using System;
using Godot;
using MpFoundation.Game.World;

namespace Sail.Game.Run;

/// <summary>
/// The world-state store (core-spine spec §5.3, CORE-PROG-A2) — a plain Node child of
/// Gameplay, added identically on every peer (the identical-tree law), wrapping the pure
/// <see cref="WorldStateRegistry"/>. It is the SINGLE world-state subscriber to
/// <c>RunDriver.RunReset</c> and to the <c>NightToDawn</c> crossing: the eight former ad-hoc
/// RunReset subscribers are now registered slices reached through one fan in one order
/// (client-local UI/telemetry latches stay direct subscribers per spec §5.2's last row — they
/// are presentation, not world state).
///
/// <b>Construction position (deviates from spec §5.3's sentence, deliberately):</b> the spec
/// says "immediately after RunDriver", but stateful managers (PropManager among them)
/// are constructed BEFORE RunDriver in Gameplay._Ready — at the
/// spec's stated position they could never self-register from Setup, which is the exact
/// fragility the store exists to close. So the store is constructed before EVERY stateful
/// manager (the spec's actual invariant), and its RunDriver subscriptions attach later via
/// <see cref="HookRunDriver"/> — the lazy-hook precedent IncapacitationService already uses.
///
/// <b>Hook order is a correctness dependency:</b> <see cref="HookRunDriver"/> must run BEFORE
/// <c>PlaythroughDriver.Setup</c> subscribes to PhaseCrossed. C# event order is subscription
/// order, so at a real dawn this store's handler runs first and reads PRE-verdict state
/// (InRound → band-live → the dawn cutoffs fan); hooked after the driver, the verdict would
/// commit RoundEnd/Loss first and the legitimate dawn cutoff would be wrongly skipped.
///
/// <b>The gating rule at the fan (spec §1.2, packet scope 6):</b> behind RoundEnd /
/// UpgradeLobby / Loss the clock free-runs, so NightToDawn genuinely fires there (A1 report
/// correction 2). Night-scoped cutoffs and map snapshots are GAMEPLAY effects, so the fan
/// checks <see cref="PlaythroughDriver.IsBandLive"/> and skips (loudly, for the test's
/// benefit) when the band is not live — no stick is extinguished behind the Loss screen.
/// </summary>
public partial class WorldStateStore : Node, IWorldStateStore
{
    public const string NodeName = "WorldStateStore";

    public static WorldStateStore? Instance { get; private set; }

    private readonly WorldStateRegistry _registry = new();
    private bool _isServer;
    private bool _hooked;
    // Same-physics-frame dedupe for the T10 double pass (spec §5.4 explicitly allows this
    // optimization): boundary reset → ResetRun → RunReset re-fans over the store in the SAME
    // frame; the second pass would despawn/respawn the freshly restored world for nothing.
    // Two passes in DIFFERENT frames (a second Play Again) both run in full. ulong.MaxValue
    // sentinel = "never fanned".
    private ulong _lastResetPhysicsFrame = ulong.MaxValue;
    // Spec §5.4's licensed "internal pristine flag": the SERVER's first boundary (T3, Boot →
    // RoundIntro(1), first physics tick) fans over the world StartAsServer just built — which
    // IS the new playthrough's initial state, and in test configurations is NOT pristine in
    // the spec's assumed sense: test-seeding flags plant their fixtures in StartAsServer BEFORE
    // the first tick, and a full T3 fan would
    // destroy them (measured against the seeding of the test scripts of the day). So the T3
    // pass records the boundary and fans nothing. Only the entry-guard path is eligible —
    // OnRunReset always fans in full, because a CLIENT's first-ever fan can legitimately be a
    // real Play Again reset of a played world.
    private bool _entryGuardPristinePassDone;

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        if (_hooked && RunDriver.Instance is { } run)
        {
            run.RunReset -= OnRunReset;
            run.PhaseCrossed -= OnPhaseCrossed;
            _hooked = false;
        }
    }

    public void Setup(bool isServer) => _isServer = isServer;

    /// <summary>Called by Gameplay immediately after RunDriver is constructed and BEFORE
    /// PlaythroughDriver.Setup — see the class doc for why that order is load-bearing.</summary>
    public void HookRunDriver()
    {
        if (_hooked || RunDriver.Instance is not { } run)
            return;
        run.RunReset += OnRunReset;
        run.PhaseCrossed += OnPhaseCrossed;
        _hooked = true;
    }

    /// <summary>From each manager's Setup (spec §5.3): registration order = construction
    /// order = fan-out order. A duplicate id is refused and reported loudly — it would make
    /// the completeness probe lie about which system resets.</summary>
    public void Register(IWorldStateSlice slice)
    {
        if (!_registry.Register(slice))
            GD.PushError($"[worldstate] duplicate slice id '{slice?.SliceId}' refused - " +
                         "two slices sharing an id would corrupt the completeness probe");
    }

    /// <summary>VER's completeness probe against spec §5.2's mutator table.</summary>
    public System.Collections.Generic.IReadOnlyList<string> RegisteredSliceIds => _registry.Ids;

    /// <summary>The playthrough boundary (spec §5.4's entry guard): fan every slice's reset,
    /// registration order. Runs on the server from PlaythroughDriver's commit into
    /// RoundIntro(1), and on every peer from the RunReset broadcast — slices do the right
    /// thing per side themselves, exactly as their former direct handlers did. A slice that
    /// throws is isolated, reported per slice, and — in a debug build — aborts the boundary
    /// loudly after the fan completes (spec §5.4: never silently begin a playthrough on a
    /// half-reset world).</summary>
    public void ResetForNewPlaythrough()
    {
        // The entry-guard path (PlaythroughDriver's commit into RoundIntro(1)). The server's
        // first commit is T3, whose world StartAsServer built THIS frame — session-start state
        // is the new playthrough's initial state by definition, so the pass records the
        // boundary and fans nothing (the _entryGuardPristinePassDone field's doc has the full
        // reasoning and the measured test-seed hazard). Every later commit through this path
        // is a Play Again over a played world and fans in full.
        if (!_entryGuardPristinePassDone)
        {
            _entryGuardPristinePassDone = true;
            _lastResetPhysicsFrame = Engine.GetPhysicsFrames();
            GD.Print($"[worldstate] boundary reset: pristine first pass over {_registry.Count} slice(s): " +
                     string.Join(",", _registry.Ids));
            return;
        }
        FanResetNow();
    }

    private void OnRunReset()
    {
        // The RunReset broadcast path — NEVER pristine-skipped: on a client the first fan it
        // ever sees can be a real Play Again reset of a played world, and on the server this
        // arriving first (a legacy --run-reset-at test reset) is a real reset too.
        _entryGuardPristinePassDone = true; // a full reset also satisfies the first-pass bookkeeping
        FanResetNow();
    }

    private void FanResetNow()
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (frame == _lastResetPhysicsFrame)
        {
            // The documented T10 double pass collapsing to one (class doc). Print rather than
            // silence so a live test can still see both entry points fired.
            GD.Print("[worldstate] boundary reset deduped (same physics frame)");
            return;
        }
        _lastResetPhysicsFrame = frame;
        var failed = _registry.FanReset((id, e) =>
            GD.PushError($"[worldstate] slice '{id}' FAILED to reset for the new playthrough: {e}"));
        GD.Print($"[worldstate] boundary reset fanned over {_registry.Count} slice(s): " +
                 string.Join(",", _registry.Ids));
        if (failed.Count > 0 && OS.IsDebugBuild())
            throw new InvalidOperationException(
                $"[worldstate] playthrough boundary aborted: slice(s) failed to reset: {string.Join(",", failed)}");
    }

    private void OnPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        if (kind != PhaseEventKind.NightToDawn)
            return;
        // The gating rule (spec §1.2 / scope 6). Evaluated BEFORE the verdict commits — this
        // store subscribes to PhaseCrossed ahead of PlaythroughDriver (HookRunDriver's order
        // contract), so a legitimate dawn reads InRound here even when the same crossing is
        // about to end the run. Behind Loss (or a degenerate short-period RoundEnd/lobby) the
        // fan is skipped: nothing mechanical fires on a band the game is not playing.
        if (PlaythroughDriver.Instance is { IsBandLive: false } driver)
        {
            GD.Print($"[worldstate] dawn cutoffs skipped (state={driver.State} not band-live)");
            return;
        }
        int round = PlaythroughDriver.Instance is { Round: > 0 } flow ? flow.Round : cyclesElapsedAfter;
        // Printed on the live path too (once per real dawn — not spam): a legit dawn reading
        // InRound here is the measurable proof of the hook-before-driver subscription order
        // (Run-StoreTest asserts exactly one of these fires pre-verdict, round 1).
        if (_isServer)
            GD.Print($"[worldstate] dawn cutoffs fanned (round {round}, state=" +
                     $"{PlaythroughDriver.Instance?.State.ToString() ?? "none"})");
        _registry.FanNightEnded(round, (id, e) =>
            GD.PushError($"[worldstate] slice '{id}' FAILED its dawn cutoff: {e}"));
        // The reserved across-nights write path (spec §5.5) — authority-side only: a future
        // disk snapshot could only ever be written by the server; no-op bodies today.
        if (_isServer)
        {
            _registry.FanCaptureSnapshot(round, (id, e) =>
                GD.PushError($"[worldstate] slice '{id}' FAILED its night snapshot: {e}"));
        }
    }
}
