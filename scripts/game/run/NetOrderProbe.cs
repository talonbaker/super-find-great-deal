using Godot;
using MpFoundation.Net;

namespace Sail.Game.Run;

/// <summary>
/// Test-only (--net-probe-at): measures the two UNVERIFIED wire mechanisms the core-spine
/// design leans on (docs/design/2026-08-13-core-spine-spec.md §1.6/§5.4; CORE-SD-1 report,
/// UNVERIFIED section) BEFORE any production code is allowed to depend on them:
///
/// <b>Probe 1 — CallLocal applies synchronously on the authority.</b> Spec §5.4's T10
/// sequence (store reset → ResetRun() → RoundIntro broadcast) is only race-free if a
/// <c>CallLocal</c> RPC's local application completes INSIDE the <c>Rpc()</c> call — before
/// the next statement runs and therefore before any subsequent broadcast is queued. The
/// probe mutates a plain field, fires a CallLocal RPC whose handler records what it saw, and
/// asserts — on the line after <c>Rpc()</c> returns — that (a) the handler already ran and
/// (b) it read the POST-mutation value. That second half is the fixed 2026-08-09 bug class
/// stated as a measurement: on the authority a handler runs against post-mutation state; on
/// a remote it runs against that REMOTE's own state (never mutated here), so the same
/// handler comparing an incoming value against local state gives different answers per peer.
/// The probe records both sides so the divergence is a printed number, not an assumption.
///
/// <b>Probe 2 — cross-node same-channel reliable ordering.</b> The verdict design puts
/// RunDriver's crossing broadcast and PlaythroughDriver's verdict broadcast on the SAME
/// channel (<see cref="NetCodec.RunChannel"/>) from two DIFFERENT nodes in one server tick,
/// and depends on every peer applying them in send order (crossing-before-verdict, spec
/// §1.6). Two instances of this node (distinct node paths = distinct RPC routing) emit an
/// interleaved marker burst in a single tick; every peer prints its arrival order;
/// tests/Run-NetProbeTest.ps1 asserts the merged A/B sequence arrived exactly in send order
/// on the remote AND on the authority. Honest scope note: a green run demonstrates the
/// ordering holds for a same-tick burst over loopback ENet — it cannot prove ordering under
/// adversarial loss; ENet's per-channel guarantee is the design basis, and this probe's job
/// is to catch the codepath-level ways that guarantee could fail to apply (wrong channel,
/// per-node sequencing, deferred local application).
///
/// Both probe instances exist on every peer that passes the flag (identical tree, identical
/// node paths — the identical-tree law), and only the server emits. Every real launch path
/// leaves the flag unset and no probe node is ever constructed.
/// </summary>
public partial class NetOrderProbe : Node
{
    public const string NodeNameA = "NetOrderProbeA";
    public const string NodeNameB = "NetOrderProbeB";

    /// <summary>How many interleaved cross-node markers the burst emits after the two
    /// CallLocal-sync markers. Even, so A and B send the same number each.</summary>
    public const int BurstMarks = 20;

    private bool _isServer;
    private bool _isPrimary;   // instance A drives the schedule; B only owns its own RPC.
    private double _emitAtSec = -1;
    private double _elapsedSec;
    private bool _fired;
    private NetOrderProbe? _sibling;

    // Probe-1 instrumentation. Static on purpose: both instances share one per-process view,
    // the same way the assertion target (spec §5.4) is about process-wide apply order, not
    // per-node order.
    private static int _stateValue;
    private static int _applied;          // total marks applied on THIS peer, in arrival order.
    private static int _lastAppliedMark;  // the most recent mark applied on THIS peer.

    /// <summary>Instance A: drives the emit schedule against its sibling B. Instance B:
    /// passive RPC owner. Server emits; clients only ever apply.</summary>
    public void Setup(bool isServer, bool isPrimary, double emitAtSec, NetOrderProbe? sibling)
    {
        _isServer = isServer;
        _isPrimary = isPrimary;
        _emitAtSec = emitAtSec;
        _sibling = sibling;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer || !_isPrimary || _fired || _emitAtSec < 0)
            return;
        _elapsedSec += delta;
        if (_elapsedSec < _emitAtSec)
            return;
        // Hold until a remote peer exists — probe 2 is meaningless into an empty room, and a
        // reliable Rpc to zero peers would "pass" vacuously (the positive-control rule:
        // an ordering check is worthless if nothing could have observed a violation).
        if (Multiplayer.GetPeers().Length < 1)
            return;
        _fired = true;
        EmitProbes();
    }

    /// <summary>All in ONE server tick, deliberately — the exact shape of the verdict instant
    /// (crossing + verdict queued back to back, spec §1.4).</summary>
    private void EmitProbes()
    {
        if (_sibling == null)
        {
            GD.Print("[net-probe] FAIL no sibling wired");
            return;
        }

        // ---- Probe 1: CallLocal synchronicity + post-mutation visibility on the authority.
        _stateValue = 111;
        int appliedBefore = _applied;
        Rpc(MethodName.ApplyMark, 1);
        bool syncApplied = _applied == appliedBefore + 1 && _lastAppliedMark == 1;
        bool sawPostMutation = _lastSeenState == 111;

        // Cross-NODE local ordering: mutate again, then fire from the SIBLING node. Its local
        // application must also be synchronous and must run AFTER mark 1's.
        _stateValue = 222;
        _sibling.Rpc(MethodName.ApplyMark, 2);
        bool crossNodeSync = _applied == appliedBefore + 2 && _lastAppliedMark == 2;
        bool siblingSawPostMutation = _lastSeenState == 222;

        GD.Print($"[net-probe] callLocal sync={syncApplied} postMutation={sawPostMutation} " +
                 $"crossNodeSync={crossNodeSync} siblingPostMutation={siblingSawPostMutation}");
        GD.Print(syncApplied && sawPostMutation && crossNodeSync && siblingSawPostMutation
            ? "[net-probe] callLocal PASS"
            : "[net-probe] callLocal FAIL");

        // ---- Probe 2: interleaved cross-node burst on RunChannel, one tick.
        for (int i = 0; i < BurstMarks; i++)
        {
            int mark = 3 + i;
            NetOrderProbe sender = (i % 2 == 0) ? this : _sibling;
            sender.Rpc(MethodName.ApplyMark, mark);
        }
        GD.Print($"[net-probe] emitted marks 1..{2 + BurstMarks}");
    }

    private static int _lastSeenState;

    /// <summary>The exact delivery idiom under test: reliable + ordered + CallLocal on
    /// <see cref="NetCodec.RunChannel"/> — RunDriver.BroadcastPhaseCrossed's attribute set,
    /// verbatim. Prints one line per application so both the authority's and every remote's
    /// arrival order is a diffable artifact in its own out.log.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel, CallLocal = true)]
    private void ApplyMark(int mark)
    {
        _applied++;
        _lastAppliedMark = mark;
        _lastSeenState = _stateValue; // authority: post-mutation value; remote: this peer's own (unmutated) 0.
        GD.Print($"[net-probe] recv {Name} {mark} sawState={_stateValue}");
    }
}
