using System.Collections.Generic;
using Godot;
using Sail.Game.Water;

namespace Sail.Game.Run;

/// <summary>
/// <b>"Has this body been under long enough to drown?" — the whole of the rule, with no scene tree
/// in it.</b>
///
/// <para>Separated from <see cref="RespawnService"/> for the reason <c>BubbleResetGate</c> is
/// separated from <c>BubbleResetLever</c>: the interesting failure is arithmetic — a clock that
/// runs slow on a loaded frame, one that keeps counting after a surface, one that kills a
/// respawned body on its first scan — and arithmetic is worth proving exhaustively without a
/// scene, a server and three processes. The scene suite then proves the same rule end to end
/// (<c>BubbleTestSelfTest</c>'s boundary probe); this proves it to the boundary.</para>
///
/// <para><b>Server-side, and that is the point.</b> No client ever reports being under water — the
/// server tests positions, exactly as it does for the map edge, and exactly as
/// <c>BubbleResetGate</c> adjudicates a lever six players can reach (MECHANICS-BIBLE: the
/// authority adjudicates; the client only ever requests).</para>
///
/// <para><b>Reset, not decay.</b> Surfacing clears a body's clock outright rather than draining
/// it. A partial breath that half-refills a drowning meter is a mechanic nobody asked for, and
/// with one number and two states the boundary is statable: the kill fires at
/// <c>underSec &gt;= <see cref="DrownAfterSec"/></c>, inclusive.</para>
/// </summary>
public sealed class DrowningClock
{
    /// <summary>
    /// Seconds continuously submerged before a body drowns. Defaults to
    /// <see cref="WaterGeometry.DrownAfterSec"/> (3.0, Talon's <i>"about three seconds"</i>) — the
    /// water contract owns the number, this owns only the clock.
    ///
    /// <para>Non-positive turns drowning off entirely and clears every running timer on the next
    /// tick, which is how a lab or a world with no lake opts out without a second flag.</para>
    /// </summary>
    public float DrownAfterSec { get; set; } = WaterGeometry.DrownAfterSec;

    /// <summary>Session-total drownings adjudicated here. Counted for the same reason
    /// <c>BubbleResetGate.Accepted</c> is: an event nobody observed is indistinguishable from an
    /// event that never happened.</summary>
    public int Drownings { get; private set; }

    /// <summary>Per-peer time under, seconds. Only submerged peers have an entry, so the dictionary
    /// is bounded by the number of players actually in the water, not by the number who ever
    /// were.
    ///
    /// <para><b>Accumulated in double, deliberately.</b> Thirty float 0.1 s scans sum to
    /// 2.9999998, so a float accumulator would hold the kill back a whole extra scan on the exact
    /// cadence <c>RespawnService</c> runs — a 3.1 s drowning that nobody could see in the code.
    /// The deltas arrive as floats and the threshold is a float; only the running total is
    /// widened.</para></summary>
    private readonly Dictionary<int, double> _underSec = new();

    /// <summary>How long this peer has been continuously submerged; 0 for a peer at the surface,
    /// a peer that has never been in water, and a peer whose clock was just cleared.</summary>
    public float SubmergedSecOf(int peerId) =>
        _underSec.TryGetValue(peerId, out double t) ? (float)t : 0f;

    /// <summary>True while this peer's clock is running at all.</summary>
    public bool IsUnder(int peerId) => _underSec.ContainsKey(peerId);

    /// <summary>How many peers are currently under.</summary>
    public int UnderCount => _underSec.Count;

    /// <summary>
    /// Advance one peer's clock. <paramref name="dt"/> is the REAL elapsed time since this peer was
    /// last ticked, never a nominal scan period — a scan that fires late must still spend the time
    /// it actually took, or "about three seconds" quietly becomes four under load.
    ///
    /// <para>Returns true on the ONE tick that crosses the threshold, and the clock is cleared
    /// before it returns, so a caller that ignores the answer cannot be handed a second death for
    /// the same breath. A non-finite or negative <paramref name="dt"/> advances nothing (a paused
    /// or rewound clock must not kill).</para>
    /// </summary>
    public bool Tick(int peerId, bool submerged, float dt)
    {
        if (DrownAfterSec <= 0f || !submerged)
        {
            _underSec.Remove(peerId);
            return false;
        }

        double step = float.IsFinite(dt) && dt > 0f ? dt : 0.0;
        double under = (_underSec.TryGetValue(peerId, out double t) ? t : 0.0) + step;
        if (under < DrownAfterSec)
        {
            _underSec[peerId] = under;
            return false;
        }

        _underSec.Remove(peerId);
        Drownings++;
        return true;
    }

    /// <summary>Advance from a world position, against <see cref="WaterGeometry.ActiveLake"/> —
    /// the live path.</summary>
    public bool Tick(int peerId, Vector3 feetPosition, float dt) =>
        Tick(peerId, WaterGeometry.IsSubmerged(feetPosition), dt);

    /// <summary>Advance from a world position against a named lake. The overload the tests use, so
    /// proving the whole position-to-death rule never has to mutate global state in a parallel
    /// xUnit run.</summary>
    public bool Tick(int peerId, Vector3 feetPosition, in WaterGeometry.LakeFootprint lake, float dt) =>
        Tick(peerId, WaterGeometry.IsSubmerged(feetPosition, lake), dt);

    /// <summary>Forget one peer's clock — they died of something else, teleported, or left.</summary>
    public void Clear(int peerId) => _underSec.Remove(peerId);

    /// <summary>Forget every clock (a new playthrough).</summary>
    public void ClearAll() => _underSec.Clear();
}
