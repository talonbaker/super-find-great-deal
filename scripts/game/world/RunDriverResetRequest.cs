using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.World;

/// <summary>
/// L11's (Issue #114) network entry point onto L1's server-only <c>RunDriver.ResetRun()</c>.
///
/// <c>ResetRun()</c> (see RunDriver.cs) is a direct method call, correct for a server-side
/// trigger — but every player's client, including the host's own, talks to the match server as
/// a separate peer over the network (see <c>Gameplay</c>'s doc comment: "the host IS the
/// server's owner" — a spawned child process, never in-process). A client's "Play Again" button
/// press therefore needs a network hop to reach the one <c>RunDriver</c> instance that is
/// actually the server's. This file adds that hop as a second partial-class file rather than
/// editing RunDriver.cs itself: that file is L1's published, locked contract (PR #116, under
/// review) — this is purely additive UI-facing surface on top of it, never a change to any of
/// L1's four hooks (PhaseCrossed/RunEndedSignal/RunReset/ResetRun).
///
/// Follows this codebase's existing client -> server request idiom exactly (see
/// SandboxAvatar.RequestResetToSpawn, PropManager.RequestGrab/RequestDrop): an
/// <c>[Rpc(RpcMode.AnyPeer)]</c> handler that early-returns unless the caller is actually the
/// server, called via <c>RpcId(1, ...)</c> — peer id 1 is always the server in this codebase's
/// ENet/Steam-relay setup, the same convention every other client -> server RPC here relies on.
/// </summary>
public partial class RunDriver
{
    /// <summary>UI entry point: <see cref="Ui.SessionSummaryPanel"/>'s Play Again button calls
    /// this on ITS OWN local <see cref="Instance"/> (every peer, including the server, has one —
    /// see the class doc), regardless of which peer clicked. Safe to call repeatedly — see
    /// <see cref="RequestReset"/>'s idempotency note; the panel also latches its own button
    /// disabled after the first press (INTERACTION-BIBLE §6/§7), so a double-press on one peer
    /// never even reaches the wire twice, but a duplicate arriving anyway (two different peers
    /// both pressing before either's summary dismisses) costs nothing.</summary>
    public void RequestResetFromClient() => RpcId(1, MethodName.RequestReset);

    /// <summary>Any peer -> server: "please reset." Reliable + <see cref="NetCodec.RunChannel"/>,
    /// matching every other run-state RPC in this file. Idempotent: <see cref="ResetRun"/> itself
    /// documents that repeat calls are safe re-anchors to the same post-reset state, so two
    /// players both pressing Play Again in the same instant (or one peer's request arriving
    /// twice) never double-resets anything observable — MECHANICS-BIBLE §4.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = NetCodec.RunChannel)]
    private void RequestReset()
    {
        if (!_isServer)
            return;
        ResetRun();
    }
}
