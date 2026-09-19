using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.World;

/// <summary>
/// Server-authoritative phase clock for the tidal-island loop (BUILD-SPEC §3/§5): one
/// normalized value <see cref="Phase"/> in [0,1) over a configurable period (default 120s),
/// from which sun, moon, sky, and — the successor packet — sea height all derive. This node
/// owns nothing visual; it is the single input every other system reads.
///
/// REPLICATE THE INPUT, DERIVE THE REST: the server owns elapsed time outright and ticks it
/// every <see cref="_PhysicsProcess"/> from the server's own sim tick (never a wall-clock
/// <see cref="Timer"/>, which would drift from physics and desync from whatever later derives
/// the sea height on the same cadence). It broadcasts <see cref="Phase"/> + a cycle count +
/// sequence number at a low cadence; every client extrapolates locally between updates from
/// its own elapsed-time accumulator and SNAP-CORRECTS (hard-sets, never springs/lerps) on
/// receipt — a spring here could let a client's derived sea height visibly lag the
/// authoritative one, exactly the kind of drift §5 calls out.
///
/// Wired into Gameplay exactly like PropManager — present in every world,
/// server or client alike — so late-join and reconnect delivery ride the identical
/// peer-connect funnel that system already uses (see Gameplay.OnPeerConnected calling
/// <see cref="SyncPhaseTo"/> the same place it calls PropManager.SendDumpTo). Consumers
/// (e.g. DayNightSky) read
/// <see cref="Instance"/>.<see cref="Phase"/> every frame once <see cref="Synced"/> is true;
/// nothing here renders anything.
/// </summary>
public partial class CycleDriver : Node
{
    public const string NodeName = "CycleDriver";

    /// <summary>Single instance per running game; DayNightSky and other phase consumers read
    /// it unwired, same convention as PropManager's own static/injected access.</summary>
    public static CycleDriver? Instance { get; private set; }

    /// <summary>How often the server broadcasts phase to every connected peer. Value pick:
    /// 2 Hz — "a few Hz is plenty" (dispatch). The resulting extrapolation gap between
    /// updates (&lt;= 0.5s) is ~0.4% of the default 120s period and small even against the
    /// 10s golden-hour window it is meant to warn about, so client-side dead-reckoning
    /// between broadcasts is visually indistinguishable from the true rate while keeping
    /// broadcast bandwidth negligible (one float + two ints, twice a second, unreliable).</summary>
    private const double BroadcastIntervalSec = 0.5;

    private bool _isServer;
    private double _periodSec = 120.0;

    /// <summary>SERVER ONLY: <c>--cycle-freeze</c>. Elapsed time stops advancing; the broadcast does
    /// NOT stop, so late joiners and reconnects still receive the phase through the ordinary funnel
    /// and a client's own freeze inference below still has samples to work from.</summary>
    private bool _frozen;

    /// <summary>CLIENT ONLY, and DERIVED FROM THE SERVER'S OWN DATA rather than from a flag: true
    /// once two consecutive authoritative samples carried a bit-identical (phase, cyclesElapsed).
    /// While it is true, this client stops extrapolating between broadcasts.
    ///
    /// <para><b>Why it is inferred and not a launch flag</b> (STYLE-4, 2026-08-20). A frozen server
    /// broadcasts the same phase twice a second; a client that kept dead-reckoning between those
    /// broadcasts would ramp away from it and be snapped back, twice a second, forever — a sawtooth
    /// up to half a second of phase wide. Reading <c>--cycle-freeze</c> on the client would fix the
    /// sawtooth by letting a CLIENT-LOCAL flag decide what the phase is doing, which is exactly what
    /// the parity law forbids, and it would still leave any peer launched without the flag drifting.
    /// Inferring it from the samples keeps the phase 100% server-authored: this branch only ever
    /// chooses to trust the last authoritative value INSTEAD of a local guess, which is strictly
    /// more faithful to the server, never less.</para>
    ///
    /// <para><b>False positives are bounded and harmless.</b> Two identical float32 phases one
    /// broadcast apart means the clock moved less than a float epsilon in 0.5 s — about 3e-8 at
    /// phase 0.275, i.e. a period over 1e7 seconds. At any real period (12 s in tests, 120–720 s in
    /// play) consecutive samples differ by 7e-4 to 4e-2 and this never latches. If it somehow did,
    /// the cost is holding at the server's last stated value for 0.5 s instead of guessing past it,
    /// and the very next differing sample clears it.</para></summary>
    private bool _clockStopped;

    /// <summary>Seconds since this driver's epoch. Server: authoritative, advanced every
    /// physics tick from the server's own delta — the single source of truth every other
    /// field below is derived from. Client: locally extrapolated the same way between
    /// authoritative updates, and hard-overwritten (never blended) on every applied update —
    /// see <see cref="Apply"/>.</summary>
    private double _elapsedSec;
    private double _sinceBroadcast;
    private uint _seq;
    private uint _lastAppliedSeq;

    /// <summary>Normalized phase in [0,1) — BUILD-SPEC §3's 120s timeline, before any
    /// per-system scaling (a consumer maps this to its own 0-60/60-70/... windows).</summary>
    public float Phase { get; private set; }

    /// <summary>How many full periods have elapsed since the driver's epoch. Carried on the
    /// wire alongside Phase (not just re-derived locally) so a client's cycle count can never
    /// drift out of step with the server's even if it somehow missed an entire broadcast
    /// interval's worth of wraps — see the MECHANICS-BIBLE §1 boundary-condition note on
    /// <see cref="Apply"/>.</summary>
    public int CyclesElapsed { get; private set; }

    /// <summary>False until the first authoritative phase has been applied. Callers MUST NOT
    /// derive anything visible from <see cref="Phase"/>/<see cref="CyclesElapsed"/> while this
    /// is false — those fields sit at their zero-initialized default until then, and treating
    /// that default as "the server's phase is 0" is exactly the bug BUILD-SPEC §5 calls out as
    /// the single most likely one in this feature ("a client must never render t=0 first").
    /// Always true on the server (it is its own authority from <see cref="Setup"/> onward).</summary>
    public bool Synced { get; private set; }

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>periodSec &lt;= 0 falls back to the default 120s (MECHANICS-BIBLE §6: define
    /// extremes explicitly rather than let a bad launch arg divide-by-zero downstream).
    /// startPhase is clamped to [0,1) — 1.0 itself would wrap to the next cycle's phase 0 on
    /// the very first tick, which is confusing for a "start exactly here" test hook, so the
    /// clamp lands just under 1.0 instead. Both are the --cycle-period / --cycle-start-phase
    /// test hooks (LaunchOptions); every real launch path passes the defaults (120, 0), a
    /// no-op relative to today.
    ///
    /// <para><b>startCycles (additive, CORE-PROG-A1 — core-spine spec §1.5):</b> anchors the
    /// clock at (cycles = startCycles, phase = startPhase) instead of always zeroing the cycle
    /// count. The default 0 reproduces the previous behavior exactly, so every existing caller
    /// — session start, ResetRun's rewind — is untouched; the ONLY caller passing a nonzero
    /// value is PlaythroughDriver's between-rounds forward re-anchor, which owns the
    /// forward-only-ordinal invariant (a backward jump through THIS path would stall
    /// RunDriver's tracker; backward jumps remain the exclusive property of ResetRun, which
    /// resets the tracker explicitly). Negative values clamp to 0 defensively.</para></summary>
    public void Setup(bool isServer, double periodSec, float startPhase, int startCycles = 0, bool frozen = false)
    {
        _isServer = isServer;
        _periodSec = periodSec > 0 ? periodSec : 120.0;
        _frozen = frozen;
        if (!isServer)
            return; // clients wait for the server's first authoritative update — see Synced.

        _elapsedSec = (System.Math.Max(startCycles, 0) + Mathf.Clamp(startPhase, 0f, 0.999999f)) * _periodSec;
        RecomputeFromElapsed();
        Synced = true; // the server is always its own authority; nothing to wait for.
        // The resolved phase, printed, on the SERVER — the half that actually owns it. A review
        // harness that claims to be at noon has to be able to show the number it settled on, and
        // "--cycle-start-phase noon" resolves through CycleBands against the day, so the number is
        // not something the reader can work out from the command line alone.
        if (frozen)
            GD.Print($"[cycle] FROZEN at phase {Phase:F6} (cycle {CyclesElapsed}, period {_periodSec:F1}s)");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isServer)
            ServerTick(delta);
        else
            ClientTick(delta);
    }

    private void ServerTick(double delta)
    {
        // Sim-tick advance, not a wall-clock Timer (§5): delta here is the server's own
        // physics step, identical reasoning to SandboxAvatar's ServerTick / PropManager's
        // loose-physics loop. Zero-client semantics (BUILD-SPEC §5 value pick): the server
        // keeps ticking unconditionally, with or without any connected peer — "the island
        // exists without observers" — so there is no peer-count guard here at all.
        // --cycle-freeze (STYLE-4): the ONE line the freeze is. Elapsed time stops; everything
        // below it — the broadcast cadence, the sequence number, the late-join funnel — carries on
        // exactly as before, so a frozen session is an ordinary session whose clock reads the same
        // every tick rather than a session with a different network shape.
        if (!_frozen)
        {
            _elapsedSec += delta;
            RecomputeFromElapsed();
        }

        _sinceBroadcast += delta;
        if (_sinceBroadcast < BroadcastIntervalSec)
            return;
        _sinceBroadcast = 0;
        _seq++;
        // Unreliable + its own channel: a dropped sample costs nothing, the next one
        // (<= 0.5s later) supersedes it — identical reasoning to PropManager.StreamLoose.
        // Rpc() to zero connected peers is a harmless no-op (same as every other broadcast
        // in this codebase), so this needs no connected-peer guard either.
        Rpc(MethodName.BroadcastPhase, Phase, CyclesElapsed, _seq);
    }

    private void ClientTick(double delta)
    {
        // Host-loss semantics (BUILD-SPEC §5 value pick): once the server is gone, Gameplay's
        // own disconnect handling (OnServerDisconnected -> Fail/reconnect) tears this node
        // down or bounces the scene; until then this method only ever touches local state
        // (no Multiplayer API call that could throw on a dead connection), so a vanished
        // server produces no error spam here — the clock simply keeps extrapolating from the
        // last known-good phase (a visual "freeze" relative to a server that no longer
        // corrects it) until the scene itself unwinds.
        if (!Synced)
            return; // hold at the zero default rather than advance a guess — see Synced's doc.
        if (_clockStopped)
            return; // the server's own samples say the clock is not moving — see _clockStopped.
        _elapsedSec += delta;
        RecomputeFromElapsed();
    }

    private void RecomputeFromElapsed()
    {
        (Phase, CyclesElapsed) = CyclePhase.FromElapsed(_elapsedSec, _periodSec);
    }

    /// <summary>Server -> everyone: the periodic low-cadence phase broadcast.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = NetCodec.CycleChannel)]
    private void BroadcastPhase(float phase, int cyclesElapsed, uint seq) => Apply(phase, cyclesElapsed, seq);

    /// <summary>Server -> one peer: late-join / reconnect delivery — the exact SendDumpTo
    /// pattern (PropManager). Reliable, and always applied
    /// regardless of <see cref="_lastAppliedSeq"/> (see the ordering note on <see
    /// cref="Apply"/>): this call is what Gameplay.OnPeerConnected fires for a brand-new OR a
    /// resumed peer, before that peer's world is presented as "playing" rather than
    /// "connecting" — it must win over anything else in flight.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncPhaseTo(float phase, int cyclesElapsed, uint seq) => Apply(phase, cyclesElapsed, seq, isSync: true);

    /// <summary>Server-only: called by Gameplay.OnPeerConnected for every joining/resuming
    /// peer, the same call site PropManager.SendDumpTo already uses — see that method's doc
    /// comment for why a peer needs its own targeted sync rather than relying on the next
    /// periodic broadcast alone.</summary>
    public void SendPhaseTo(int peerId)
    {
        if (!_isServer)
            return;
        RpcId(peerId, MethodName.SyncPhaseTo, Phase, CyclesElapsed, _seq);
    }

    /// <summary>True when this peer believes the clock is not advancing: the server's own
    /// <c>--cycle-freeze</c> on the authority, the inference from its samples on a client. Read by
    /// the STYLE-4 review harness and its self-test; nothing gameplay-facing keys off it.</summary>
    public bool ClockStopped => _isServer ? _frozen : _clockStopped;

    /// <summary>Test seam: deliver an authoritative sample straight into <see cref="Apply"/>, the
    /// same values <see cref="BroadcastPhase"/> would have carried, without a Multiplayer API or a
    /// second process. The client half of the freeze (the inference in Apply plus the extrapolation
    /// gate in <see cref="ClientTick"/>) is a decision about received data, so this is the seam that
    /// makes it provable headlessly — the same reasoning <see cref="CyclePhase"/> is pulled out
    /// under. Never called by gameplay code.</summary>
    internal void TestApplyAuthoritative(float phase, int cyclesElapsed, uint seq) =>
        Apply(phase, cyclesElapsed, seq);

    private void Apply(float phase, int cyclesElapsed, uint seq, bool isSync = false)
    {
        // Latest-wins staleness guard (SandboxAvatar.ApplySnapshot's exact reasoning):
        // unreliable delivery may reorder the periodic broadcast, so a stale, superseded
        // sample must never clobber a newer one that already landed.
        //
        // MECHANICS-BIBLE §3 (race resolution) + §4 (idempotency): a late-join/reconnect
        // Reliable sync (isSync) always wins outright, even against a numerically higher seq
        // already applied from a PRIOR connection on this same node instance — a reconnect
        // over ENet is handed a brand-new peer identity server-side but this client-side node
        // is the same instance across the drop (Gameplay.TeardownReplicatedNodes never frees
        // CycleDriver, by design: it isn't a per-avatar/per-prop replicated node), so its
        // _lastAppliedSeq is stale history from the pre-drop connection, not a legitimate
        // ordering claim against the resumed session's SyncPhaseTo. Applying the exact same
        // (phase, cyclesElapsed, seq) twice — e.g. a duplicated reliable delivery — is a
        // harmless no-op either way (idempotent by construction: it just re-sets the same
        // fields to the same values).
        if (!isSync && Synced && seq <= _lastAppliedSeq)
            return;
        // The freeze inference (STYLE-4) — see _clockStopped for why it is derived here rather than
        // read off a flag. Compared BEFORE the fields are overwritten, and only against a sample
        // that was itself authoritative (Synced), so the zero-initialised default can never be
        // mistaken for the server having said 0 twice.
        _clockStopped = Synced && phase == Phase && cyclesElapsed == CyclesElapsed;
        _lastAppliedSeq = seq;
        Phase = phase;
        CyclesElapsed = cyclesElapsed;
        // Snap-correct: hard-set the elapsed-time accumulator so extrapolation resumes from
        // the true value, never spring/lerp toward it (§5: a lerp here could lag whatever
        // later derives the sea height off the same phase, "one of them drowns on a
        // dry-looking floor").
        _elapsedSec = cyclesElapsed * _periodSec + phase * _periodSec;
        Synced = true;
    }
}

/// <summary>Pure phase-wrap arithmetic, pulled out of <see cref="CycleDriver"/> so it is
/// directly testable without a running scene tree or Multiplayer API — the same seam
/// Gameplay.ResolveColorIndex / Gameplay.DecideReconnect give their own pure decisions (see
/// CycleSelfTest.cs, run via --cycle-selftest / Run-CycleTest.ps1).</summary>
public static class CyclePhase
{
    /// <summary>Normalized phase in [0,1) and whole-periods-elapsed for a given amount of
    /// elapsed time over a period — the exact math CycleDriver.RecomputeFromElapsed runs
    /// every tick on both the server (from true elapsed time) and a client (from its locally
    /// extrapolated accumulator). MECHANICS-BIBLE §1: the wrap boundary is explicit and
    /// exact — phase never reaches 1.0 itself; a whole period maps to phase 0 of the next
    /// cycle. periodSec &lt;= 0 is defensively treated as 1s rather than dividing by zero
    /// (callers are expected to have already validated it — see CycleDriver.Setup — this is
    /// a last-resort guard, not the primary validation).</summary>
    public static (float Phase, int CyclesElapsed) FromElapsed(double elapsedSec, double periodSec)
    {
        if (periodSec <= 0)
            periodSec = 1.0;
        double wrapped = elapsedSec % periodSec;
        if (wrapped < 0)
            wrapped += periodSec; // defensive; both callers only ever grow elapsedSec.
        float phase = (float)(wrapped / periodSec);
        int cycles = (int)System.Math.Floor(elapsedSec / periodSec);
        return (phase, cycles);
    }
}
