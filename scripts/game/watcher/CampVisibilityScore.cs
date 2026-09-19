using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Contracts;

namespace MpFoundation.Game.Watcher;

/// <summary>
/// The camp's real <see cref="IVisibilityScore"/> producer: exposure computed from live avatar
/// state instead of a hand-poked table. This is the class <see cref="WatcherVisibility"/>'s
/// remarks anticipate ("when the real producer lands... this class is deleted and the watcher is
/// pointed at that instead") — except the stand-in is kept, because the labs and the unit tests
/// still drive the brain through it.
///
/// <para><b>Sign convention — inherited deliberately, not re-decided.</b> Lit ground
/// <i>attenuates</i> exposure, exactly as <see cref="WatcherVisibility"/> §remarks argue: the
/// watcher may not enter lit ground, so standing in the fire's pool is the safest a player can
/// be. The composition is delegated to <see cref="WatcherVisibility.Score"/> rather than
/// re-implemented here, so the two can never drift apart.</para>
///
/// <para><b>The stance term is pinned to Standing today, and that is the one dishonest number in
/// this class.</b> R4's <c>StanceController</c> is the intended source and is not wired into the
/// avatar's authoritative movement path (see the round-1 consolidation notes): crouch has no
/// input action, no replication, and its speed factors sit below the motor's clamp. Until that
/// lands, every peer scores as if upright — so the watcher's target selection is driven by
/// distance-from-fire and movement alone, which is a real mechanic but only two thirds of the
/// intended one. Swapping the pin for a real stance read is a one-line change in
/// <see cref="StanceOf"/> and nothing else here moves.</para>
/// </summary>
public sealed class CampVisibilityScore : IVisibilityScore
{
    /// <summary>Above this planar speed a peer reads as running rather than walking.</summary>
    private const float RunSpeedMps = 4.0f;

    /// <summary>Below this planar speed a peer reads as still. Not zero: a controller's deadzone
    /// drift and the motor's settle both leave a few cm/s on the clock, and a player who believes
    /// they are standing still must score as still or the defence is a lie.</summary>
    private const float StillSpeedMps = 0.35f;

    private readonly Func<IEnumerable<(int PeerId, Vector3 Position, Vector3 Velocity)>> _peers;
    private readonly Func<Vector3> _fireOrigin;
    private readonly Func<float> _litRadiusM;
    private readonly Dictionary<int, float> _cache = new();

    public CampVisibilityScore(
        Func<IEnumerable<(int PeerId, Vector3 Position, Vector3 Velocity)>> peers,
        Func<Vector3> fireOrigin,
        Func<float> litRadiusM)
    {
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _fireOrigin = fireOrigin ?? throw new ArgumentNullException(nameof(fireOrigin));
        _litRadiusM = litRadiusM ?? throw new ArgumentNullException(nameof(litRadiusM));
    }

    /// <summary>Recomputes every peer's exposure. Called once per watcher tick, before the brain
    /// reads any of it, so all peers are scored against the same lit radius — scoring them lazily
    /// would let the radius move between two peers in the same frame.</summary>
    public void Refresh()
    {
        _cache.Clear();
        Vector3 fire = _fireOrigin();
        float lit = _litRadiusM();
        foreach ((int peerId, Vector3 position, Vector3 velocity) in _peers())
        {
            // Planar distance and planar speed: the lit pool is a disc on the ground, and a
            // player's fall speed is not something anything can see them by.
            float planarDist = new Vector2(position.X - fire.X, position.Z - fire.Z).Length();
            float planarSpeed = new Vector2(velocity.X, velocity.Z).Length();
            _cache[peerId] = WatcherVisibility.Score(
                StanceOf(peerId), MotionOf(planarSpeed), planarDist <= lit);
        }
    }

    /// <summary>Pinned — see the class remarks. R4's stance authority is the intended read.</summary>
    private static ExposureStance StanceOf(int peerId) => ExposureStance.Standing;

    private static ExposureMotion MotionOf(float planarSpeed) => planarSpeed switch
    {
        <= StillSpeedMps => ExposureMotion.Still,
        < RunSpeedMps => ExposureMotion.Walking,
        _ => ExposureMotion.Running,
    };

    /// <summary>An unknown peer is unseen, matching <see cref="WatcherVisibilityTable"/>: a peer
    /// that joined between <see cref="Refresh"/> and this read is not instantly the most
    /// interesting thing on the map.</summary>
    public float VisibilityFor(int peerId) => _cache.TryGetValue(peerId, out float v) ? v : 0f;
}
