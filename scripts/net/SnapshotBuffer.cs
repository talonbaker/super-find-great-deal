using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Aim;

namespace MpFoundation.Net;

/// <summary>
/// Snapshot interpolation for a remote (non-owned, non-predicted) avatar. Authoritative
/// server states are buffered and the avatar is rendered ~<see cref="InterpDelayTicks"/>
/// in the past, interpolating between the two snapshots that bracket the render time —
/// so between-snapshot motion is smooth and a lost snapshot costs nothing while the next
/// one still bounds the segment. On starvation it extrapolates briefly along the last
/// known velocity, then holds. Remote avatars are deliberately NOT predicted.
/// </summary>
public sealed class SnapshotBuffer
{
    /// <summary>Render delay in sim ticks (~100 ms at 60 Hz): two snapshot intervals of
    /// slack at the 30 Hz broadcast rate, so one lost snapshot never starves the lerp.</summary>
    public const int InterpDelayTicks = 6;

    /// <summary>Max extrapolation past the newest snapshot (~200 ms) before holding.</summary>
    private const float MaxExtrapolateTicks = 12f;

    /// <summary>If the render clock drifts further than this from target, snap it
    /// (join, long stall, or teleport) instead of slewing.</summary>
    private const float SnapThresholdTicks = 30f;

    /// <summary>Fraction of the clock error corrected per sample — a gentle slew that
    /// absorbs jitter in snapshot arrival without visible speed-ups.</summary>
    private const float ClockSlew = 0.05f;

    private const int MaxStored = 64;

    /// <summary>AimStance (WP-L3) is a discrete pose, not interpolated like Position/Velocity —
    /// same "nearest bracket wins" treatment as Grounded (t &lt; 0.5f picks a, else b), so a
    /// remote proxy's raise/lower pose changes on a clean snapshot boundary instead of smearing
    /// through an undefined "half-raised" value mid-lerp.</summary>
    public readonly record struct RenderSample(
        Vector3 Position, Vector3 Velocity, float Yaw, bool Grounded, bool Teleported, AimStance AimStance);

    private readonly List<NetCodec.Snapshot> _snaps = new(); // tick-ascending
    private double _renderTick = -1;
    private byte _epoch;
    private bool _teleportPending;
    private bool _hasEpoch;
    private long _maxTickSeen = -1;

    public bool HasSnapshots => _snaps.Count > 0;

    /// <summary>
    /// <b>The server tick this buffer is currently RENDERING</b> — the interpolated clock,
    /// deliberately <see cref="InterpDelayTicks"/> behind the newest tick received, and not the
    /// newest tick itself.
    ///
    /// <para>Exposed for ANIM-M3's clip-time anchor and for <c>--capture-at-tick</c>. Both need the
    /// tick that matches the POSITION on screen: seeking a clip to the newest received tick would put
    /// a proxy's pose ahead of the body it belongs to by the whole interpolation delay, and capturing
    /// two clients at the newest tick would compare two frames showing different moments.</para>
    ///
    /// <para>Negative until the first <see cref="Sample"/> after the first snapshot. A readout of the
    /// clock, never a way to set it.</para>
    /// </summary>
    public double RenderTick => _renderTick;

    /// <summary>The newest server tick this buffer has accepted. Ahead of
    /// <see cref="RenderTick"/> by the interpolation delay; used by the capture harness to know when
    /// a mark is unreachable rather than merely not here yet.</summary>
    public long NewestTick => _maxTickSeen;

    public void Add(in NetCodec.Snapshot snap)
    {
        // Reject stale/reordered snapshots before any epoch handling. ServerTick is a
        // monotonic total order that never resets across an epoch bump, so a snapshot
        // from a superseded epoch that arrives late (after a newer-epoch snapshot) is
        // reliably identified by tick and dropped — otherwise it would be misread as a
        // fresh teleport and wipe the correct buffer.
        if (snap.Tick <= _maxTickSeen)
            return;
        _maxTickSeen = snap.Tick;

        if (!_hasEpoch)
        {
            _hasEpoch = true;
            _epoch = snap.Epoch;
        }
        else if (snap.Epoch != _epoch)
        {
            // Server-side teleport (e.g. reset-to-spawn): old trajectory is meaningless.
            _epoch = snap.Epoch;
            _snaps.Clear();
            _renderTick = -1;
            _teleportPending = true;
        }

        // Insert in tick order; ignore duplicates (unreliable channel may reorder/dupe).
        int i = _snaps.Count;
        while (i > 0 && _snaps[i - 1].Tick > snap.Tick)
            i--;
        if (i > 0 && _snaps[i - 1].Tick == snap.Tick)
            return;
        _snaps.Insert(i, snap);
        if (_snaps.Count > MaxStored)
            _snaps.RemoveAt(0);
    }

    /// <summary>Advances the render clock by one frame and samples the buffered
    /// trajectory. Null until the first snapshot arrives (caller holds at spawn).</summary>
    public RenderSample? Sample(double frameDelta)
    {
        if (_snaps.Count == 0)
            return null;

        NetCodec.Snapshot newest = _snaps[^1];
        double target = newest.Tick - InterpDelayTicks;
        if (_renderTick < 0)
        {
            _renderTick = target;
        }
        else
        {
            _renderTick += frameDelta * AvatarMotor.TickRate;
            double error = target - _renderTick;
            if (Mathf.Abs((float)error) > SnapThresholdTicks)
                _renderTick = target;
            else
                _renderTick += error * ClockSlew;
        }
        // Never render further ahead than bounded extrapolation allows.
        if (_renderTick > newest.Tick + MaxExtrapolateTicks)
            _renderTick = newest.Tick + MaxExtrapolateTicks;

        bool teleported = _teleportPending;
        _teleportPending = false;

        // Drop snapshots the render clock has fully passed (keep one behind for the lerp).
        while (_snaps.Count > 2 && _snaps[1].Tick < _renderTick)
            _snaps.RemoveAt(0);

        // Behind the newest snapshot: interpolate between the bracketing pair.
        if (_renderTick <= newest.Tick)
        {
            NetCodec.Snapshot a = _snaps[0];
            if (_renderTick <= a.Tick || _snaps.Count == 1)
                return Make(a, teleported);
            NetCodec.Snapshot b = _snaps[1];
            for (int i = 1; i < _snaps.Count; i++)
            {
                b = _snaps[i];
                if (b.Tick >= _renderTick)
                {
                    a = _snaps[i - 1];
                    break;
                }
            }
            float t = (float)((_renderTick - a.Tick) / (double)(b.Tick - a.Tick));
            return new RenderSample(
                a.State.Position.Lerp(b.State.Position, t),
                a.State.Velocity.Lerp(b.State.Velocity, t),
                Mathf.LerpAngle(a.State.Yaw, b.State.Yaw, t),
                t < 0.5f ? a.State.Grounded : b.State.Grounded,
                teleported,
                t < 0.5f ? a.AimStance : b.AimStance);
        }

        // Past the newest snapshot: extrapolate along its velocity (already clamped above).
        float aheadSec = (float)(_renderTick - newest.Tick) * AvatarMotor.TickDelta;
        MoveState s = newest.State;
        return new RenderSample(
            s.Position + s.Velocity * aheadSec, s.Velocity, s.Yaw, s.Grounded, teleported, newest.AimStance);
    }

    private static RenderSample Make(in NetCodec.Snapshot snap, bool teleported) =>
        new(snap.State.Position, snap.State.Velocity, snap.State.Yaw, snap.State.Grounded, teleported, snap.AimStance);
}
