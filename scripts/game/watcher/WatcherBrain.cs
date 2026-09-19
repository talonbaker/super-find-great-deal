using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Watcher;

/// <summary>Two states, and there is deliberately no third. There is no approach state, no
/// pursuit state, no attack state and no wind-up, because the creature has no such capabilities
/// and a state machine that can express them is one refactor away from performing them.</summary>
public enum WatcherState
{
    /// <summary>Not in the world. Not hiding, not waiting nearby — absent.</summary>
    Absent = 0,
    /// <summary>Standing somewhere beyond the light, facing whoever is most exposed.</summary>
    Watching = 1,
}

/// <summary>Why the last sighting ended. Recorded rather than discarded because the whole
/// question the §13 fork asks — how often the same player may be watched — is answered by
/// counting these, and a reason that was never stored cannot be counted later.</summary>
public enum WatcherExit
{
    None = 0,
    /// <summary>The target stopped being exposed — most often, they dropped flat.</summary>
    TargetFaded,
    /// <summary>It had stood there long enough.</summary>
    DwellElapsed,
    /// <summary>Somebody committed to closing the distance. THRILL §10 constraint (c): it may
    /// never be approached and studied.</summary>
    Approached,
    /// <summary>Somebody got close enough that there was no ambiguity left to protect. The one
    /// exit that fires in full view.</summary>
    Breached,
    /// <summary>The lit ground grew out to it — a restoked fire, a widened radius. A hard
    /// invariant, not a behaviour: it does not retreat from the light, it is simply never on
    /// lit ground in any frame anyone could observe.</summary>
    LightReachedIt,
    /// <summary>Nobody left to look at.</summary>
    NoCandidate,
}

/// <summary>What the watcher can know about one player this tick. Position and facing only —
/// there is no health, no carried item and no state it could act on, because it does not act.</summary>
public readonly struct WatcherPeerView
{
    public readonly int PeerId;
    /// <summary>From <c>IVisibilityScore.VisibilityFor</c>. 0 unseen … 1 fully exposed.</summary>
    public readonly float Visibility;
    public readonly Vector3 Position;
    /// <summary>Where this player is looking. Need not be normalised; need not be horizontal.</summary>
    public readonly Vector3 Forward;

    public WatcherPeerView(int peerId, float visibility, Vector3 position, Vector3 forward)
    {
        PeerId = peerId;
        Visibility = visibility;
        Position = position;
        Forward = forward;
    }
}

/// <summary>Supplies a stand position for a chosen target, or refuses. Refusing is a normal
/// outcome and means "there is nowhere it could be standing that nobody is already looking at",
/// in which case nothing appears at all — see <see cref="WatcherPlacement"/>.</summary>
public delegate bool StandPositionChooser(in WatcherPeerView target, float litRadiusM, out Vector3 stand);

/// <summary>
/// The watcher's entire mind. Pure C# — no Node, no scene tree, no rendering — so the behaviour
/// is testable headlessly and the Godot layer (<see cref="Watcher"/>) is a shell that feeds it
/// and reads its answers.
///
/// <para><b>Why there is no position output.</b> The stand position is written exactly once, at
/// the instant of appearing, and is read-only thereafter for the life of the sighting. That is
/// not a policy that a later change could relax by accident — there is no code path in this
/// class that assigns <see cref="StandPosition"/> outside <c>Appear</c>, and
/// <c>Watcher_NeverPursues_StandPositionIsWrittenOnceAndNeverAgain</c> pins it. Pursuit is
/// therefore not "disabled"; it is unrepresentable. This matters more than it looks: the
/// repo's own recorded failures include an entity that pursued off the edge of the map and one
/// that faced backwards while doing it, and the direction for this creature (CLAUDE.md canon
/// fact 3) is that pursuit is not the register of this game and no build introduces it by
/// accident.</para>
///
/// <para><b>The unwitnessed-exit rule, and why it is the design rather than a nicety.</b>
/// THRILL-BIBLE §10's row for this device makes it constitutive: an exit the player watches is
/// an all-clear, and §2.1 forbids the all-clear arriving. So a dwell timer running out, or a
/// target going to ground, only <i>arms</i> the exit — the creature actually leaves on the first
/// moment nobody is looking at it, which is what produces "be gone if you look away and back".
/// Two exits are exempt because they are invariants rather than behaviours: lit ground reaching
/// it, and somebody walking right up to it.</para>
///
/// <para>Deliberately absent: any concept of noise, damage, aggression, alertness, "searching",
/// or memory of a player between sightings.</para>
/// </summary>
public sealed class WatcherBrain
{
    private readonly WatcherTuning _t;

    private float _dwell;
    private float _cooldown;
    private float _armedFor;
    private bool _armed;
    private WatcherExit _armedReason;

    public WatcherBrain(WatcherTuning? tuning = null) => _t = tuning ?? WatcherTuning.Default;

    public WatcherState State { get; private set; } = WatcherState.Absent;

    /// <summary>Who it is looking at, or -1. Valid only while <see cref="State"/> is Watching.</summary>
    public int TargetPeerId { get; private set; } = -1;

    /// <summary>Where it is standing. Written once per sighting; see the class remarks.</summary>
    public Vector3 StandPosition { get; private set; }

    /// <summary>How the previous sighting ended. Survives into Absent so it can be counted.</summary>
    public WatcherExit LastExit { get; private set; } = WatcherExit.None;

    /// <summary>True once it has decided to leave and is only waiting to not be looked at.</summary>
    public bool ExitArmed => _armed;

    /// <summary>Seconds this sighting has lasted.</summary>
    public float DwellElapsed => _dwell;

    /// <summary>Seconds until it is willing to appear again.</summary>
    public float CooldownRemaining => _cooldown;

    /// <summary>Advances one step. <paramref name="fireOrigin"/> and <paramref name="litRadiusM"/>
    /// come from <c>INightPressure</c>; the chooser is consulted only when it wants to appear.</summary>
    public void Tick(float dt, IReadOnlyList<WatcherPeerView> peers, Vector3 fireOrigin,
                     float litRadiusM, StandPositionChooser chooser)
    {
        if (State == WatcherState.Absent)
        {
            TickAbsent(dt, peers, litRadiusM, chooser);
            return;
        }
        TickWatching(dt, peers, fireOrigin, litRadiusM);
    }

    private void TickAbsent(float dt, IReadOnlyList<WatcherPeerView> peers, float litRadiusM,
                            StandPositionChooser chooser)
    {
        if (_cooldown > 0f)
        {
            _cooldown = Mathf.Max(0f, _cooldown - dt);
            return;
        }
        if (!TryBest(peers, out WatcherPeerView best) || best.Visibility < _t.AppearVisibility)
            return;
        // A refusal here is not a failure. It means every candidate stand position was already
        // inside somebody's view, and appearing into a view that is watching is the startle
        // §8.2 forbids as a build. It waits for a better moment instead.
        if (!chooser(in best, litRadiusM, out Vector3 stand))
            return;

        State = WatcherState.Watching;
        TargetPeerId = best.PeerId;
        StandPosition = stand;      // the one and only write; see the class remarks
        _dwell = 0f;
        _armed = false;
        _armedFor = 0f;
        _armedReason = WatcherExit.None;
        LastExit = WatcherExit.None;
    }

    private void TickWatching(float dt, IReadOnlyList<WatcherPeerView> peers, Vector3 fireOrigin,
                              float litRadiusM)
    {
        _dwell += dt;

        // --- Invariant, checked before anything else and exempt from the unwitnessed rule -----
        // The lit radius can grow (a restoked fire, a later night's tuning). If it reaches the
        // stand position the creature is standing on lit ground, which it may never do, so it is
        // gone this instant whether or not anyone is looking. This is the reason the invariant
        // is expressed as an exit rather than as a walk away: walking away is movement.
        if (HorizontalDistance(StandPosition, fireOrigin) <= litRadiusM)
        {
            Exit(WatcherExit.LightReachedIt);
            return;
        }

        Retarget(peers);
        if (!TryFind(peers, TargetPeerId, out WatcherPeerView target))
        {
            Exit(WatcherExit.NoCandidate);
            return;
        }

        float range = HorizontalDistance(target.Position, StandPosition);

        // Somebody is close enough to touch it. There is no ambiguity left to protect at this
        // range, so this is the single case where it goes in full view.
        if (range <= _t.HardStandoffM)
        {
            Exit(WatcherExit.Breached);
            return;
        }

        if (!_armed)
        {
            if (target.Visibility < _t.WithdrawVisibility) Arm(WatcherExit.TargetFaded);
            else if (_dwell >= _t.DwellSeconds) Arm(WatcherExit.DwellElapsed);
            else if (range <= _t.ApproachArmM) Arm(WatcherExit.Approached);
        }

        if (!_armed)
            return;

        _armedFor += dt;

        // Stage one: it will not go while ANYONE can see it. Stage two, after it has been waiting
        // a while, it relaxes to only caring about the player it is looking at. Without stage two
        // a group facing in four directions can pin it in place indefinitely, and a creature you
        // can stand and study for minutes has been categorised — which is the §8.1 failure this
        // whole design is built to avoid. The relaxation deliberately never allows the TARGET to
        // watch it leave; that player's all-clear is protected at every stage.
        bool blocked = _armedFor < WatcherLimits.StareRelaxSeconds
            ? AnyoneSees(peers)
            : Sees(target);

        if (!blocked)
            Exit(_armedReason);
    }

    /// <summary>Attention transfers to whoever is most exposed, but only past a margin, so two
    /// near-equal players cannot make the head oscillate. The doe run is this and nothing else:
    /// a player who stands up and runs outscores a crouching friend by far more than the margin,
    /// so the head comes round to them.</summary>
    private void Retarget(IReadOnlyList<WatcherPeerView> peers)
    {
        if (!TryBest(peers, out WatcherPeerView best))
            return;
        if (best.PeerId == TargetPeerId)
            return;
        if (!TryFind(peers, TargetPeerId, out WatcherPeerView current))
        {
            TargetPeerId = best.PeerId;   // current target vanished (disconnect); take the best left
            return;
        }
        if (best.Visibility > current.Visibility + _t.SwitchMargin)
            TargetPeerId = best.PeerId;
        // Dwell is deliberately NOT reset on a transfer. Dwell measures how long the creature has
        // been standing there, not how long it has looked at any one player — otherwise a group
        // could keep it present forever by taking turns being the most visible, which is a
        // straightforward over-exposure hole (§8.1).
    }

    private void Arm(WatcherExit reason)
    {
        _armed = true;
        _armedFor = 0f;
        _armedReason = reason;
        // Not disarmed if the condition reverses. Standing back up does not summon it back; it
        // has already decided to go and is only waiting for the room to look elsewhere.
    }

    private void Exit(WatcherExit reason)
    {
        State = WatcherState.Absent;
        LastExit = reason;
        TargetPeerId = -1;
        _dwell = 0f;
        _armed = false;
        _armedFor = 0f;
        _armedReason = WatcherExit.None;
        _cooldown = _t.CooldownSeconds;
        // StandPosition is intentionally left as it was: the last place it stood is evidence, and
        // clearing it would only make a stale read look like the origin instead of like the past.
    }

    private bool AnyoneSees(IReadOnlyList<WatcherPeerView> peers)
    {
        for (int i = 0; i < peers.Count; i++)
            if (Sees(peers[i]))
                return true;
        return false;
    }

    private bool Sees(in WatcherPeerView peer)
        => WatcherSight.CanSee(peer.Position, peer.Forward, StandPosition,
                               _t.ViewHalfAngleDeg, WatcherLimits.SightRangeM);

    private static bool TryBest(IReadOnlyList<WatcherPeerView> peers, out WatcherPeerView best)
    {
        best = default;
        bool found = false;
        for (int i = 0; i < peers.Count; i++)
        {
            if (found && peers[i].Visibility <= best.Visibility)
                continue;
            best = peers[i];
            found = true;
        }
        return found;
    }

    private static bool TryFind(IReadOnlyList<WatcherPeerView> peers, int peerId, out WatcherPeerView view)
    {
        for (int i = 0; i < peers.Count; i++)
        {
            if (peers[i].PeerId != peerId)
                continue;
            view = peers[i];
            return true;
        }
        view = default;
        return false;
    }

    public static float HorizontalDistance(Vector3 a, Vector3 b)
        => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}

/// <summary>Two numbers that are structural rather than tuneable, kept out of
/// <see cref="WatcherTuning"/> so a retune pass does not treat them as dials.</summary>
public static class WatcherLimits
{
    /// <summary>How long an armed exit waits on "nobody at all is looking" before it relaxes to
    /// "the target is not looking". See <see cref="WatcherBrain"/>'s TickWatching.</summary>
    public const float StareRelaxSeconds = 8f;

    /// <summary>Beyond this a player is not credited with seeing it, however they are facing.
    /// Without a range term a teammate 300 m away who happens to be pointed at the treeline
    /// holds the creature in place, which is the over-exposure direction.</summary>
    public const float SightRangeM = 90f;
}
