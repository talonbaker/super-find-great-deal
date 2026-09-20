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
    /// <summary>
    /// How close the body gets to its target before it stops walking and presses, metres,
    /// measured HORIZONTALLY.
    ///
    /// <para><b>1.2 m, and it is derived — from the CLIENT's own pickup radius, not from the
    /// server's grab range</b> (REVIEW-1, 2026-09-20, measured). Two bars stand between a
    /// scripted press and a held prop, and the smaller one binds:
    /// <list type="bullet">
    /// <item><c>SandboxAvatar.FindNearestCarryable</c> only ever offers a prop within
    /// <see cref="SandboxAvatar.PickupRadius"/> — <b>1.5 m, in 3D, from the avatar's origin</b> —
    /// and <c>ScriptedGrabPropId</c> honours a named prop only inside that same radius, because
    /// it "narrows the choice, never widens the reach". Outside it the press finds nothing and
    /// <b>no request is sent at all</b>.</item>
    /// <item><c>PropManager.GrabRange</c> (2.25 m) is the SERVER's acceptance bar, and it is
    /// never reached by a press the client refused to compose.</item>
    /// </list>
    /// A prop rests 0.2–0.5 m above the avatar's origin, so 1.5 m in 3D is 1.41–1.48 m
    /// horizontally; 1.2 m leaves 0.2 m of slack for the settle and for a frame of prediction
    /// error.</para>
    ///
    /// <para><b>INT-1's proposed 1.8 m is RULED OUT by measurement, not by argument</b>, the same
    /// way INT-1 ruled out SHELF-1's spawn race. Raised to 1.8 m (TASK-1's number, derived from
    /// the 2.25 m server reach), two suites went red in one sweep:
    /// <c>Run-CarryNetTest</c> phase 2 — <i>"DropC did not disconnect while holding -- it never
    /// held the ball at all"</i>, the bot standing 1.482 m horizontally / 1.564 m in 3D from a
    /// ball at y = 0.5 with <b>no `grab denied` line on the server</b>, because the press found no
    /// candidate and sent nothing; and <c>Run-PlaceTest</c> — bot D pressed 0.6 m further out,
    /// its named prop was outside <c>PickupRadius</c> so the naming fell through to
    /// nearest-carryable, and it came away holding <b>1027</b>, a shelf product, which the server
    /// then refused to place <c>OutsideRoomBounds</c> while the suite watched prop 1016 sit on the
    /// floor. That is SHELF-1's dressed-aisle lesson (<c>Run-PlaceTest.ps1</c>'s own header)
    /// arriving through the arrive radius.</para>
    ///
    /// <para><b>So the three arrive radii in the tree are reconciled by JOB, not by value.</b>
    /// This one and <c>ScriptedGotoIntentSource.ArriveRadius</c> (1.5 m) are waypoint tolerances
    /// on a walk; <c>ScriptedSortIntentSource.FetchStandM</c> (1.8 m) is a stand distance for a
    /// bot that is blocked by a 1.4 m plinth and never gets closer, where the horizontal number
    /// overstates the 3D one it is really spending. Each now carries its own derivation at the
    /// constant.</para>
    /// </summary>
    private const float ArriveRadius = 1.2f;

    /// <summary>
    /// <b>The distance at which this brain may press while it is still walking</b>, metres, in
    /// 3D — <see cref="SandboxAvatar.PickupRadius"/> itself, referenced rather than copied, so
    /// the brain's "could I grab this?" is literally the client's own question.
    ///
    /// <para><b>What it is for</b> (REVIEW-1, 2026-09-20). A bot walks AT its prop and a
    /// <c>CharacterBody3D</c> does not push a <c>RigidBody3D</c> (SHELF-1 §6.2), so it can WEDGE
    /// between the prop and the adjacent shelf bay and stop for good. INT-1 measured
    /// <c>Run-MaterialSfxTest</c> losing exactly one driver in 2 runs of 5, always at a closest
    /// approach of <b>1.49 m in 3D — to the centimetre, a geometric constant rather than a
    /// race</b>: 1.45 m horizontally, outside <see cref="ArriveRadius"/>, so the arrive branch
    /// below is never entered, the press is never made, and the server logs no refusal at all
    /// because a press that is never made is never refused (TASK-1 §3.3). The suite then reports
    /// that a prop "never made a sound", which is an assertion about AUDIO describing a bot that
    /// never reached its object.</para>
    ///
    /// <para><b>1.49 m is INSIDE the client's 1.5 m pickup radius</b>, which is the whole point:
    /// the wedged bot could grab, it simply never asked. So the press is gated on the reach that
    /// decides whether a request can exist at all, and the WALK is left alone — no bot's resting
    /// position moves, which is what makes this safe for the four suites that measure distances
    /// against <see cref="ArriveRadius"/>.</para>
    ///
    /// <para><b>Only for bots that opted into the retry cadence</b> (<c>--carry-grab-retry</c>).
    /// A one-shot press is LATCHED and decides on a predicted position; firing it early, from
    /// further out, would spend the single press this file's own doc says is expensive to get
    /// wrong. Every suite that wants robustness here already passes the flag.</para>
    /// </summary>
    private const float GrabReachM = SandboxAvatar.PickupRadius;

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

    /// <summary>The heading this bot is facing, as a world yaw in <c>AvatarMotor.ResolveYaw</c>'s
    /// convention. Updated from whatever direction the brain last actually walked in.</summary>
    private float _aimYaw;

    /// <summary>
    /// <b>The bot aims where it walks.</b>
    ///
    /// <para>CARRY-1 moved the server's THROW impulse off the body's facing and onto the
    /// replicated aim ray (<c>PropManager.ReleaseIntoLooseDirected</c>'s <c>alongAim</c>) — which
    /// is the only defensible rule once the game is first person, and which would silently break
    /// every scripted throw if scripted brains kept leaving <see cref="MoveIntent.AimYaw"/> at its
    /// default. A bot that has walked to the far edge of a slab and throws a crate off it would
    /// instead throw it at a fixed world bearing, and <c>Run-ThrowTest</c>'s out-of-bounds
    /// recovery case would stop being staged at all — the silent kind of green.</para>
    ///
    /// <para>So the brain reports the heading it is steering on. That is what a body's facing
    /// already tracked (<c>MoveState.Yaw</c> follows travel), so this makes the bot's aim agree
    /// with the bot's body instead of inventing a second answer, and every existing suite's
    /// geometry is preserved by construction rather than by a tolerance. <see cref="MoveIntent.AimPitch"/>
    /// stays 0: a flat throw plus the server's own upward component is exactly the arc these
    /// suites were tuned against, and a bot that randomly pitched would be a bot whose throws
    /// nobody could predict.</para>
    /// </summary>
    public MoveIntent NextIntent(double delta)
    {
        MoveIntent intent = NextIntentCore(delta);
        Vector3 heading = intent.MoveDir;
        heading.Y = 0;
        if (heading.LengthSquared() > 1e-6f)
            _aimYaw = Mathf.Atan2(-heading.X, -heading.Z);
        return intent with { AimYaw = _aimYaw };
    }

    private MoveIntent NextIntentCore(double delta)
    {
        _clock += delta;

        if (!_grabSent)
        {
            // The live target wins while it is available; the fixed coordinate is the fallback for
            // the tick where the prop is held by someone else, despawned, or not yet replicated.
            Vector3 approach = _liveTarget?.Invoke() ?? _target;
            Vector3 toTarget = approach - _self.Position;
            // The CLIENT's own measure, in 3D from the avatar's origin, kept before the flatten:
            // SandboxAvatar.FindNearestCarryable and ScriptedGrabPropId both judge by this, and a
            // press outside it composes no request at all. See GrabReachM.
            float reach = toTarget.Length();
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
            // STILL WALKING, and pressing anyway once the client could actually select the prop
            // (REVIEW-1, 2026-09-20). This is the wedge case: a bot stopped by its own target
            // 1.45 m out is outside ArriveRadius for good, so without this the arrive branch
            // above is never entered and no request is ever composed. The WALK is untouched --
            // the press rides along with the movement intent rather than replacing it, so no
            // bot's resting position moves. Retry-cadence bots only: a one-shot press is latched
            // and must not be spent from further out than it was budgeted for.
            Vector3 dir = dist > StopMoveEpsilon ? toTarget.Normalized() : Vector3.Zero;
            bool pressOnApproach = _grabRetrySec >= 0
                                   && _clock >= _earliestGrabSec
                                   && reach <= GrabReachM
                                   && _clock >= _lastGrabAttemptSec + _grabRetrySec;
            if (pressOnApproach)
                _lastGrabAttemptSec = _clock;
            return new MoveIntent { MoveDir = dir, Interact = pressOnApproach };
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
