using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Deterministic headless-bot brain for the carry-authority convergence test
/// (<c>tests/Run-CarryTest.ps1</c>). Walks straight toward a fixed target — a networked prop's
/// known world position — then, once it has arrived AND its own scripted clock has reached
/// <c>earliestGrabSec</c>, fires a single Interact edge (a grab request). If it actually ends up
/// holding the prop, it holds for <c>holdSec</c> more seconds (measured from the tick the grab
/// fired, not from arrival) and then fires a second Interact edge (a drop request). A negative
/// <c>holdSec</c> means "never auto-drop" — useful for staging a disconnect-while-holding, and
/// harmless for a bot whose grab is expected to be rejected (contested-grab case), since it never
/// actually ends up holding anything to auto-drop.
///
/// Optionally also scripts a single throw instead of (or before) the auto-drop: if
/// <c>throwSec</c> is non-negative, once the grab has landed and the clock reaches
/// <c>grabSentAtSec + throwSec</c> it fires one Throw edge (a throw request) rather than an
/// Interact edge. A negative <c>throwSec</c> (the default) means "never throw" — the plain
/// grab/hold/drop schedule behaves exactly as before.
///
/// Paced by the fixed physics tick (like <see cref="DeterministicWalkIntentSource"/>), not
/// wall-clock timers, so its schedule self-paces to Godot's simulation step regardless of host
/// machine load — only the *test's* staging of one bot's schedule relative to another (via
/// generous elapsed-time buffers) needs to tolerate real-world jitter.
///
/// <para><b>THIS SOURCE DOES NOT LATCH ITS ARRIVAL AND <see cref="ScriptedGotoIntentSource"/>
/// DOES</b> — deliberately, and the reason is written out in full in that class's own doc comment
/// (W6-3, 2026-08-30). The short version: its arrival can open a door and teleport the body, so it
/// must be one-way and must therefore be taken on a position the server has confirmed; this one's
/// arrival only chooses when to press a button the server adjudicates anyway, so it re-derives the
/// distance from a live position every tick and a refused press costs one press. Both obey the
/// same rule (<c>.claude/rules/test-suite.md</c>: compute a MOVE from a prediction, never an
/// irreversible action), and <see cref="_grabRetrySec"/> is how this one buys that guarantee for
/// the single decision of its own that is expensive to get wrong.</para>
/// </summary>
public sealed class ScriptedCarryIntentSource : IIntentSource
{
    private const float ArriveRadius = 1.2f;
    private const float StopMoveEpsilon = 0.05f;

    /// <summary>How long after the throw fires before the regrab chase begins — lets the
    /// Loose transition replicate and the prop actually leave the bot's hands.</summary>
    private const double RegrabChaseDelaySec = 0.8;

    /// <summary>Grab-request retry cadence while chasing (a rolling prop can slip out of
    /// range between a request being sent and validated).</summary>
    private const double RegrabRetrySec = 0.6;

    /// <summary>Opt-in retry cadence for the FIRST grab (--carry-grab-retry), &lt;0 = the original
    /// one-shot press. Off by default so every existing carry suite's pacing is unchanged.
    ///
    /// The one-shot press decides on this bot's PREDICTED position, and CARRY-1 measured a
    /// headless bot's prediction error transiently reaching 1.71 m on an idle machine — far enough
    /// that a press taken at a predicted 1.2 m can arrive at a server whose authoritative body is
    /// still outside PropManager.GrabRange (2.25 m). One refused press, never repeated, and the
    /// whole scenario stages as a no-op. See LaunchOptions.CarryGrabRetrySec.</summary>
    private readonly double _grabRetrySec;

    /// <summary>Opt-in live approach target (--carry-target-prop): the prop's current replicated
    /// position, re-read every tick until the grab lands. Null = walk to the fixed coordinate, as
    /// before. See LaunchOptions.CarryTargetPropId for the measurement behind it — a released prop
    /// rests where its last holder stood, not where it spawned.</summary>
    private readonly System.Func<Vector3?>? _liveTarget;

    private readonly Node3D _self;
    private readonly Vector3 _target;
    private readonly double _earliestGrabSec;
    private readonly double _holdSec;
    private readonly double _throwSec;
    private readonly Vector3? _walkTo;
    private readonly (Vector3 a, Vector3 b)? _patrol;
    private readonly System.Func<Vector3?>? _regrabTarget;
    private readonly System.Func<bool>? _isHolding;

    /// <summary>Hold Sprint on every walking intent (ANIM-1: <c>tests/Run-SwingBodyCapture.ps1</c>
    /// needs a held item at FULL sprint, which is where the body's run lean is 13 degrees and the
    /// held-item anchor defect is at its worst). Off by default, so every existing carry suite's
    /// pacing is byte-for-byte unchanged. Wired from the existing <c>--goto-sprint</c> flag rather
    /// than a new one — <c>LaunchOptions</c> parses it independently of <c>--goto-script</c>.</summary>
    private readonly bool _sprint;

    private double _clock;
    private bool _grabSent;
    private bool _dropSent;
    private bool _throwSent;
    private bool _patrolToB;
    private bool _regrabDone;
    private double _grabSentAtSec = -1;
    private double _throwSentAtSec = -1;
    private double _lastRegrabAttemptSec = -100;
    private double _lastGrabAttemptSec = -100;

    public ScriptedCarryIntentSource(Node3D self, Vector3 target, double earliestGrabSec, double holdSec,
        double throwSec = -1, Vector3? walkTo = null, (Vector3 a, Vector3 b)? patrol = null,
        System.Func<Vector3?>? regrabTarget = null, System.Func<bool>? isHolding = null,
        bool sprint = false, double grabRetrySec = -1, System.Func<Vector3?>? liveTarget = null)
    {
        _grabRetrySec = grabRetrySec;
        _liveTarget = liveTarget;
        _sprint = sprint;
        _self = self;
        _target = target;
        _earliestGrabSec = earliestGrabSec;
        _holdSec = holdSec;
        _throwSec = throwSec;
        _walkTo = walkTo;
        _patrol = patrol;
        _regrabTarget = regrabTarget;
        _isHolding = isHolding;
    }

    public MoveIntent NextIntent(double delta)
    {
        _clock += delta;

        if (!_grabSent)
        {
            // The live target wins while it is available; the fixed coordinate is the fallback for
            // the tick where the prop is held by someone else, despawned, or not yet replicated.
            Vector3 approach = _liveTarget?.Invoke() ?? _target;
            Vector3 toTarget = approach - _self.Position;
            toTarget.Y = 0;
            float dist = toTarget.Length();
            if (dist <= ArriveRadius && _clock >= _earliestGrabSec)
            {
                // The press is LATCHED (_grabSent) unless --carry-grab-retry asked otherwise, which
                // keeps every existing suite's timeline identical. With the retry on, the latch is
                // deferred until the bot is genuinely holding something: the press is a request
                // about the SERVER's body and this branch can only see the predicted one, so a
                // single press is a decision taken on a position that may be over a metre wrong.
                if (_grabRetrySec < 0)
                {
                    _grabSent = true;
                    _grabSentAtSec = _clock;
                    return new MoveIntent { MoveDir = Vector3.Zero, Interact = true };
                }
                if (_isHolding?.Invoke() == true)
                {
                    // Confirmed holding: latch WITHOUT another press (a press now would be
                    // refused as GrabDenial.AlreadyHeld and would pollute a suite that reads the
                    // server's refusal trace). _grabSentAtSec anchors on the CONFIRMED grab, which
                    // is what the hold/throw timers below actually mean.
                    _grabSent = true;
                    _grabSentAtSec = _clock;
                    return MoveIntent.None;
                }
                if (_clock >= _lastGrabAttemptSec + _grabRetrySec)
                {
                    _lastGrabAttemptSec = _clock;
                    return new MoveIntent { MoveDir = Vector3.Zero, Interact = true };
                }
                // Inside the cadence and still empty-handed: stand on the spot (letting the
                // server's body catch up with the prediction) and press again shortly.
                return MoveIntent.None;
            }
            Vector3 dir = dist > StopMoveEpsilon ? toTarget.Normalized() : Vector3.Zero;
            return new MoveIntent { MoveDir = dir, Interact = false };
        }

        if (!_throwSent && _throwSec >= 0 && _clock >= _grabSentAtSec + _throwSec)
        {
            _throwSent = true;
            _throwSentAtSec = _clock;
            return new MoveIntent { MoveDir = Vector3.Zero, Throw = true };
        }

        // The regrab phase (drift regression proof): after the throw, chase the prop's LIVE
        // replicated position and grab it again — deliberately while it is still Loose, before
        // the server's settle latch. Interact edges retry on a cadence because a rolling target
        // can slip the window between request and validation. Once holding again, falls through
        // to the carry-away walk below so any drift from the holder has distance to show up in.
        if (_throwSent && _regrabTarget != null && !_regrabDone)
        {
            if (_isHolding?.Invoke() == true)
            {
                _regrabDone = true; // fall through to the carry-away walk below
            }
            else if (_clock >= _throwSentAtSec + RegrabChaseDelaySec)
            {
                Vector3? live = _regrabTarget();
                if (live is not Vector3 prop)
                    return MoveIntent.None; // gone or contested: stand and wait
                Vector3 chase = prop - _self.Position;
                chase.Y = 0;
                if (chase.Length() <= ArriveRadius && _clock >= _lastRegrabAttemptSec + RegrabRetrySec)
                {
                    _lastRegrabAttemptSec = _clock;
                    return new MoveIntent { MoveDir = Vector3.Zero, Interact = true };
                }
                return new MoveIntent { MoveDir = chase.Length() > StopMoveEpsilon ? chase.Normalized() : Vector3.Zero };
            }
            else
            {
                return MoveIntent.None;
            }
        }

        // Continuous hold+walk: ping-pong between two points for the whole hold so the carry-drift
        // regression can measure the held-item→anchor offset across a long, never-stopping walk
        // (a single walk-to point stops the bot, which only ever exercises the settled rest offset,
        // never sustained motion). An explicit auto-drop (holdSec >= 0) still fires.
        if (_patrol is (Vector3 pa, Vector3 pb))
        {
            if (!_dropSent && _holdSec >= 0 && _clock >= _grabSentAtSec + _holdSec)
            {
                _dropSent = true;
                return new MoveIntent { MoveDir = Vector3.Zero, Interact = true };
            }
            Vector3 goal = _patrolToB ? pb : pa;
            Vector3 toGoal = goal - _self.Position;
            toGoal.Y = 0;
            if (toGoal.Length() <= ArriveRadius)
            {
                _patrolToB = !_patrolToB; // reached this end; steer to the other WITHOUT a stop tick
                goal = _patrolToB ? pb : pa;
                toGoal = goal - _self.Position;
                toGoal.Y = 0;
            }
            return new MoveIntent
            {
                MoveDir = toGoal.LengthSquared() > StopMoveEpsilon * StopMoveEpsilon ? toGoal.Normalized() : Vector3.Zero,
                Sprint = _sprint,
            };
        }

        // Carry the grabbed prop off to a second point so a tether/cord lays the line down as it
        // moves. Kept holding (no drop) while walking; the throw/drop timers below still fire.
        if (_walkTo is Vector3 w)
        {
            Vector3 to = w - _self.Position;
            to.Y = 0;
            if (to.Length() > ArriveRadius)
                return new MoveIntent { MoveDir = to.Normalized() };
        }

        if (!_dropSent && _holdSec >= 0 && _clock >= _grabSentAtSec + _holdSec)
        {
            _dropSent = true;
            return new MoveIntent { MoveDir = Vector3.Zero, Interact = true };
        }

        return MoveIntent.None;
    }
}
