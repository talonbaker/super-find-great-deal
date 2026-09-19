using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Honk;

/// <summary>
/// <b>The two decisions a honk needs, as pure functions with no Godot runtime behind them.</b>
/// HONK-1, 2026-09-04. Split out of <see cref="HonkManager"/> for the reason
/// <c>VoiceRelayDecider</c> is split out of <c>VoiceManager</c>: the parts that decide are the
/// parts worth hammering in <c>tests/unit</c>, and a decision welded to a <c>Node</c> can only be
/// tested by standing up a server.
///
/// <para><b>The two decisions sit on opposite sides of the wire, on purpose.</b>
/// <list type="bullet">
/// <item><b>The cooldown is the SERVER's</b> (<see cref="TryHonk"/>). It is an authority question —
/// "may this player make this noise yet" — and a client that lies about its own cooldown must
/// change nothing. The client keeps a copy of the same latch, but only so a held key does not
/// spend a packet per frame; the instance that counts is the one on the host.</item>
/// <item><b>Earshot is the RECEIVING CLIENT's</b> (<see cref="InEarshot"/>). It is not an authority
/// question at all: a client deciding what IT hears cannot make anybody else hear anything, and it
/// already holds every input — the honker's replicated avatar and its own. Deciding it here rather
/// than at the relay is what keeps this feature out of an architectural fork that is not its to
/// take: <c>VoiceProximityGate</c>'s class doc records that a server which inspects positions to
/// route audio REVERSES a deliberate stance, and that flipping it is <i>"Talon's call, not an
/// agent's."</i> A honk is ≤1.67 messages per second per player against voice's fifty, so the
/// bandwidth argument that motivated that gate simply is not present here — there is nothing to
/// buy with the stance.</item>
/// </list></para>
///
/// <para><b>Why the earshot test exists at all, when the mixer would do it anyway.</b> The
/// receiving <c>AudioStreamPlayer3D</c> already has a hard <c>MaxDistance</c> cutoff at the same
/// number, so this check changes no audible outcome. What it changes is whether the outcome can be
/// WITNESSED: a mixer cutoff is unobservable on a headless peer, so "the far player did not hear
/// it" would be an assertion no test could make. One <c>if</c> in C# converts an inaudible fact
/// into a countable one — see <c>HonkManager.HeardCount</c> / <c>ReceivedCount</c>, which are what
/// let a suite prove the far peer got the message and still stayed silent, rather than merely
/// proving the wire dropped it.</para>
///
/// <para><b>MECHANICS-BIBLE §1, boundary conditions — both bounds pinned rather than inherited:</b>
/// <list type="bullet">
/// <item><b>Cooldown is exclusive at the bound:</b> a press at exactly
/// <see cref="HonkConfig.CooldownSec"/> after the last one is GRANTED. The alternative eats a
/// press for a floating-point tie, and the failure a player notices is the swallowed honk, never
/// the extra one. <b>The latch stores the NEXT ALLOWED time rather than the last honk's</b>, and
/// that is what makes the bound exact instead of aspirational: the obvious
/// <c>now - last &lt; Cooldown</c> spelling refuses at exactly the bound, because
/// <c>(10.0 + 0.6) - 10.0</c> is <c>0.5999999999999996</c> in doubles rather than <c>0.6</c>.
/// Measured, not reasoned — <c>HonkTests.CooldownBoundary_ExactlyAtTheBound_IsGranted</c> was
/// written against the subtracting version, went red, and is what produced this paragraph.</item>
/// <item><b>Range is inclusive at the bound:</b> exactly <see cref="HonkConfig.AudibleRangeM"/>
/// is IN. This matches <c>VoiceRelayDecider</c>'s own <c>dist &lt;= radius</c> rather than
/// inventing a second convention, and the cost of being wrong is one already-silent sound at
/// exactly one distance.</item>
/// </list></para>
///
/// <para><b>MECHANICS-BIBLE §3, simultaneous events:</b> two peers honking on the same server tick
/// need no resolution order, and that is a property rather than an oversight — a honk arbitrates
/// nothing, holds nothing, and mutates no shared state, so there is no "first wins" to get wrong
/// and no order in which two of them could disagree across machines. The only per-tick state
/// written is the honker's OWN cooldown stamp, which no other peer's honk can touch.</para>
///
/// <para><b>MECHANICS-BIBLE §4, idempotency:</b> the cooldown IS the double-fire stop, and
/// <see cref="TryHonk"/> is the single funnel it lives in — <c>HonkManager</c> has no path that
/// broadcasts without passing through it. A payload-free request carries no sequence number, so a
/// duplicated packet and a very fast second press are indistinguishable; the cooldown deliberately
/// collapses both, which is the wanted behaviour in each case.</para>
/// </summary>
public sealed class HonkGate
{
    /// <summary>Peer id -> the earliest time (seconds, monotonic) at which they may honk again.
    /// The NEXT time rather than the LAST one, deliberately — see the boundary note on the class.
    /// Bounded by the player cap; never grows past it, because <see cref="ForgetPeer"/> removes an
    /// entry the moment its peer leaves.
    ///
    /// <para><b>Peer ids are recycled, so this must be dropped with the peer.</b> A cooldown entry
    /// inherited by whoever gets that id next silently eats a fresh joiner's first honk — the same
    /// recycled-id hazard <c>VoiceManager</c> handles for its avatar cache, its mute entries and
    /// its relay latches, handled the same way.</para></summary>
    private readonly Dictionary<int, double> _nextHonkAt = new();

    /// <summary>How much early a press is still granted. <b>Zero on a client, non-zero on the
    /// server</b> (<see cref="HonkConfig.ServerJitterToleranceSec"/>) — see that constant for the
    /// held-key bug this exists to prevent. Zero by default so every caller that does not think
    /// about it, the unit suite included, gets the strict rule.</summary>
    public double ToleranceSec { get; init; }

    private long _granted;
    private long _throttled;

    /// <summary>May <paramref name="peerId"/> honk at <paramref name="nowSec"/>? Records the grant
    /// when it says yes, so this is the single place the latch advances — calling it twice for one
    /// press would eat the second honk, which is why <c>HonkManager</c> calls it exactly once per
    /// inbound request.</summary>
    public bool TryHonk(int peerId, double nowSec)
    {
        if (peerId <= 0)
            return false;
        if (_nextHonkAt.TryGetValue(peerId, out double next) && nowSec < next - ToleranceSec)
        {
            _throttled++;
            return false;
        }
        _nextHonkAt[peerId] = nowSec + HonkConfig.CooldownSec;
        _granted++;
        return true;
    }

    /// <summary>Seconds until this peer may honk again; 0 when they may honk now. Read by the
    /// client-side copy of the latch so a held key does not spend a packet per frame.</summary>
    public double RemainingSec(int peerId, double nowSec)
    {
        if (!_nextHonkAt.TryGetValue(peerId, out double next))
            return 0;
        double left = next - nowSec;
        return left > 0 ? left : 0;
    }

    /// <summary>Drops this peer's latch. Called on disconnect — see the recycled-id note on
    /// <see cref="_nextHonkAt"/>.</summary>
    public void ForgetPeer(int peerId) => _nextHonkAt.Remove(peerId);

    /// <summary>Session reset: every latch belonged to the previous match's peer set.</summary>
    public void Clear()
    {
        _nextHonkAt.Clear();
        _granted = 0;
        _throttled = 0;
    }

    /// <summary>Presses granted / refused for being inside the cooldown, cumulative for the
    /// session. Cumulative rather than read-and-reset because the only consumer is a suite reading
    /// a total at the end of a run — <c>VoiceRelayDecider</c>'s reset-on-read exists to feed an
    /// interval logger, and there is no interval logger here.</summary>
    public (long Granted, long Throttled) Counters => (_granted, _throttled);

    /// <summary>Is <paramref name="listenerPos"/> inside earshot of a honk at
    /// <paramref name="honkerPos"/>?
    ///
    /// <para><b>Null on either side is "I do not know", and it is DELIVERED.</b> If either avatar
    /// cannot be located — the spawn/despawn race <c>VoiceManager.EmitPosFor</c> already documents
    /// — the honk plays. The failure a player would report is "nobody could hear me", so the safe
    /// direction is loud, and the mixer's own <c>MaxDistance</c> is still behind this as the final
    /// word, so a fail-open cannot actually produce an audible honk from across the map. Same
    /// direction, same reasoning, as <c>VoiceRelayDecider</c>'s fail-open.</para>
    ///
    /// <para>3D distance, matching the receiving <c>AudioStreamPlayer3D.MaxDistance</c>'s own 3D
    /// cutoff exactly, so a listener one storey below is as far away to this gate as they are to
    /// the mixer.</para></summary>
    public static bool InEarshot(Vector3? honkerPos, Vector3? listenerPos)
    {
        if (honkerPos is not Vector3 a || listenerPos is not Vector3 b)
            return true;
        return a.DistanceTo(b) <= HonkConfig.AudibleRangeM;
    }
}
