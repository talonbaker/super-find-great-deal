using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The scripted hider</b> (TASK-1, <c>tests/Run-SortTest.ps1</c>): fetch an object from the
/// supply crate, carry it to a named bin, set it down, repeat.
///
/// <para><b>Why a brain of its own rather than another arm on
/// <see cref="ScriptedCarryIntentSource"/>.</b> That one is built around ONE prop — grab it,
/// hold it, maybe throw it, maybe get it back — and every suite in the carry family depends on
/// its exact pacing. The sorting job is a LOOP over a list, and the thing it has to prove is
/// that the second and third deliveries behave differently from the first (once-only counting,
/// the wrong bin, a re-place). Bolting a queue onto a single-prop brain would have changed the
/// timeline of five green suites to serve one new one.</para>
///
/// <para><b>It grabs BY ID and places BY TRANSFORM</b>, through
/// <c>PropManager.ClientRequestGrab</c> and <c>ClientRequestPlace</c> — the same public client
/// entry points a human's E press reaches, so the server-side grab and place paths, arbitration
/// and all, are what the suite exercises. What it deliberately does NOT exercise is
/// <c>InteractTargeting.Pick</c>: eighteen objects sit 0.22 m apart in one crate, and an aim
/// cone asked to choose between them would pick a different one on a loaded machine. That is
/// exactly the split <c>BotHarness.MaybePlace</c> already documents for <c>--carry-place</c> —
/// a bot is not the right instrument for "which of these was the player looking at", and a verb
/// with a test branch in it is a verb nobody has tested.</para>
///
/// <para><b>The bins are read out of this peer's own world</b> (<see cref="SortBin.Find"/>)
/// rather than typed into the suite. Every peer has the same authored task room — that is the
/// premise the whole sort rests on — so a fixture that hard-coded the bins' coordinates would
/// be testing a level that could move underneath it.</para>
///
/// <para><b>It acts only while the round says to.</b> The steps are self-paced off observed
/// state (holding / not holding), never off a wall clock, and the whole loop is gated on a
/// caller-supplied "can I work now" — in the suite, "the round is in Seeking", which is when
/// the hider is in the task room at all. <c>.claude/rules/test-suite.md</c>: derive a schedule
/// from the thing it must outlive, never type one beside it.</para>
///
/// <para><b>The last step may be a HOLD</b> (bin slot &lt; 0): fetch the object and stand there
/// with it. That is how the suite stages an object in the hider's hands at the burst, so
/// DOOR-1's forced drop has something to take.</para>
/// </summary>
public sealed class ScriptedSortIntentSource : IIntentSource
{
    /// <summary>One step: fetch <see cref="PropId"/> and deliver it to bin
    /// <see cref="BinSlot"/>. A negative slot means "fetch it and keep holding it".</summary>
    public readonly record struct Step(int PropId, int BinSlot);

    /// <summary>
    /// How close the body gets to an object in the crate before it presses grab, metres,
    /// measured horizontally.
    ///
    /// <para><b>1.8 m, not <see cref="ScriptedCarryIntentSource"/>'s 1.2 m, and the difference
    /// was measured rather than chosen.</b> The sortables sit on a 1.4 m-wide plinth, so a body
    /// walking at one on the far column of the grid is stopped by the crate itself with the
    /// object still 1.40 m away. On the first run of <c>tests/Run-SortTest.ps1</c> the bot
    /// delivered its first two objects, then walked into the crate and stood at (78.85, 1.01)
    /// for the remaining fifty seconds without ever pressing grab: step 2 wanted the far-column
    /// object and 1.40 m was outside a 1.2 m radius. Nothing was logged, because a press that is
    /// never made is not refused.</para>
    ///
    /// <para>The server's own reach is 2.25 m (<c>SandboxAvatar.PickupRadius</c> 1.5 plus
    /// <c>PropManager.GrabRangeTolerance</c> 0.75) measured in 3D from the avatar's origin, so
    /// 1.8 m horizontal against a 0.7 m-high object is 1.93 m - inside it with room for the
    /// prediction error CARRY-1 measured (transiently 1.71 m on an idle machine), which is what
    /// <see cref="GrabRetrySec"/> is then there to absorb.</para>
    /// </summary>
    private const float FetchStandM = 1.8f;

    /// <summary>Grab-request cadence while standing at the crate. Retried rather than fired
    /// once, for the reason <c>--carry-grab-retry</c> exists: the press is a decision about the
    /// SERVER's body and this brain can only see the predicted one.</summary>
    private const double GrabRetrySec = 0.5;

    /// <summary>How close the body must be to the DROP POINT before it asks to place, metres.
    /// The server checks the hand against 1.65 m (<c>PropManager.PlaceReachM</c> plus the grab
    /// tolerance) and the hand is up to 0.9 m in front of the body, so 1.35 m of body distance
    /// clears it with room for a round trip of lag.</summary>
    private const float PlaceStandM = 1.35f;

    /// <summary>Place-request cadence while standing at the bin. <c>Run-PlaceTest</c>
    /// deliberately fires once because a REFUSAL is what three of its four cases assert; here
    /// a refusal is not the subject, so a retry is the difference between a fixture that stages
    /// its scenario and one that silently does not.</summary>
    private const double PlaceRetrySec = 1.0;

    /// <summary>How long to wait after a successful hand-off before starting the next step.
    /// One beat, so the Held -&gt; Loose transition has replicated and the tally has seen the
    /// object at rest before the body walks away from it.</summary>
    private const double AfterPlaceSec = 0.8;

    private readonly Node3D _self;
    private readonly IReadOnlyList<Step> _steps;
    private readonly Func<int, Vector3?> _propAt;
    private readonly Func<int> _heldPropId;
    private readonly Action<int> _requestGrab;
    private readonly Action<int, Transform3D> _requestPlace;
    private readonly Func<bool> _canWork;
    private readonly string _who;

    /// <summary>How many objects have already been delivered to each bin, so successive
    /// deliveries take different spots in the same bin — a second placement aimed at the first
    /// one's position is refused <c>DoesNotFitThere</c>, correctly, and the fixture would then
    /// be proving nothing.</summary>
    private readonly Dictionary<int, int> _delivered = new();

    private int _step;
    private double _clock;
    private double _lastGrab = -100;
    private double _lastPlace = -1;
    private double _placedAt = -1;
    private float _aimYaw;

    public ScriptedSortIntentSource(Node3D self, IReadOnlyList<Step> steps,
        Func<int, Vector3?> propAt, Func<int> heldPropId, Action<int> requestGrab,
        Action<int, Transform3D> requestPlace, Func<bool> canWork, string who)
    {
        _self = self;
        _steps = steps;
        _propAt = propAt;
        _heldPropId = heldPropId;
        _requestGrab = requestGrab;
        _requestPlace = requestPlace;
        _canWork = canWork;
        _who = who;
    }

    /// <summary>True once every step has been delivered (or the holding step reached). The suite
    /// reads it only through the log line each step prints.</summary>
    public bool Done => _step >= _steps.Count;

    /// <inheritdoc cref="ScriptedCarryIntentSource.NextIntent"/>
    public MoveIntent NextIntent(double delta)
    {
        MoveIntent intent = Core(delta);
        Vector3 heading = intent.MoveDir;
        heading.Y = 0;
        if (heading.LengthSquared() > 1e-6f)
            _aimYaw = Mathf.Atan2(-heading.X, -heading.Z);
        return intent with { AimYaw = _aimYaw };
    }

    private MoveIntent Core(double delta)
    {
        _clock += delta;
        if (Done || !_canWork())
            return MoveIntent.None;

        // The hand emptying is an OBSERVATION, not a movement decision, and it has to be made
        // on every tick including the ones this brain asks for nothing on — so it runs here,
        // at the top, rather than in a second callback somebody has to remember to drive.
        AdvanceIfDelivered();
        if (Done)
            return MoveIntent.None;

        // A DELIVERED OBJECT IS LEFT ALONE. Measured on the first run of tests/Run-SortTest.ps1:
        // without this, the tick after a hand-off lands the brain sees an empty hand, decides it
        // still owes step N its object, walks the half metre back to the bin and PICKS THE OBJECT
        // STRAIGHT BACK OUT -- which then makes AdvanceIfDelivered's "am I still holding it"
        // guard true again, so the step never advances and the script wedges on its second item.
        // The log read as a bot placing the same prop three times and then going quiet.
        if (_placedAt >= 0)
            return MoveIntent.None;

        Step step = _steps[_step];
        int held = _heldPropId();

        // --- still empty-handed: go and get it ------------------------------------------------
        if (held != step.PropId)
        {
            // Holding the WRONG thing means a previous step's place was refused and the loop
            // would otherwise wedge. Say so and put it down where we stand; the next tick
            // re-enters this branch with empty hands.
            if (held != 0)
            {
                if (_clock < _lastPlace + PlaceRetrySec)
                    return MoveIntent.None;
                _lastPlace = _clock;
                GD.Print($"[sort-bot] {_who} is holding {held} but step {_step} wants "
                         + $"{step.PropId} — dropping it at {_clock:F2}s");
                return new MoveIntent { MoveDir = Vector3.Zero, Interact = true };
            }

            if (_propAt(step.PropId) is not Vector3 at)
                return MoveIntent.None;   // not replicated yet; stand still rather than guess
            Vector3 toProp = at - _self.Position;
            toProp.Y = 0;
            if (toProp.Length() > FetchStandM)
                return new MoveIntent { MoveDir = toProp.Normalized() };

            if (_clock >= _lastGrab + GrabRetrySec)
            {
                _lastGrab = _clock;
                _requestGrab(step.PropId);
            }
            return MoveIntent.None;
        }

        // --- holding it ------------------------------------------------------------------------
        if (step.BinSlot < 0)
        {
            // The holding step: stand still with it in hand and let the round come to us.
            if (_placedAt < 0)
            {
                _placedAt = _clock;
                GD.Print($"[sort-bot] {_who} holding prop {step.PropId} and waiting "
                         + $"at {_clock:F2}s (step {_step}, no bin)");
            }
            return MoveIntent.None;
        }

        SortBin? bin = SortBin.Find(_self, step.BinSlot);
        if (bin == null)
            return MoveIntent.None;

        _delivered.TryGetValue(step.BinSlot, out int nth);
        Transform3D drop = bin.DropPoint(nth);

        // GATED ON THE DISTANCE TO THE DROP POINT, not to the approach point, and the first run
        // is why: the server measures a place from the HOLDER'S HAND against
        // PropManager.PlaceReachM + GrabRangeTolerance (1.65 m), and a body that has merely got
        // near a stand-here marker can still be over two metres from the spot it is
        // aiming at. Both of that run's steps were refused TooFarToPlace on their first attempt
        // and landed on the retry -- which is a fixture that works by accident.
        Vector3 toDrop = drop.Origin - _self.Position;
        toDrop.Y = 0;
        if (toDrop.Length() > PlaceStandM)
        {
            Vector3 toBin = bin.ApproachPoint - _self.Position;
            toBin.Y = 0;
            if (toBin.LengthSquared() > 1e-6f)
                return new MoveIntent { MoveDir = toBin.Normalized() };
        }

        if (_clock < _lastPlace + PlaceRetrySec)
            return MoveIntent.None;
        _lastPlace = _clock;
        GD.Print($"[sort-bot] {_who} PLACING prop {step.PropId} into bin {step.BinSlot} at "
                 + $"({drop.Origin.X:F2}, {drop.Origin.Y:F2}, {drop.Origin.Z:F2}) "
                 + $"at {_clock:F2}s (step {_step})");
        _requestPlace(step.PropId, drop);
        return MoveIntent.None;
    }

    /// <summary>
    /// Watches for the hand emptying and advances the step. Run at the top of every
    /// <see cref="NextIntent"/> rather than as its own callback: the hand-off must be noticed
    /// on frames this brain asks for nothing, and one driver is one fewer thing to forget.
    /// </summary>
    private void AdvanceIfDelivered()
    {
        if (Done || _steps[_step].BinSlot < 0)
            return;
        if (_heldPropId() == _steps[_step].PropId)
            return;   // still in hand: the place has not landed (or was refused and will retry)
        if (_lastPlace < 0)
            return;   // never even asked yet - the hand is empty because the step has not begun

        if (_placedAt < 0)
        {
            _placedAt = _clock;
            _delivered[_steps[_step].BinSlot] =
                _delivered.TryGetValue(_steps[_step].BinSlot, out int n) ? n + 1 : 1;
            GD.Print($"[sort-bot] {_who} delivered prop {_steps[_step].PropId} to bin "
                     + $"{_steps[_step].BinSlot} at {_clock:F2}s (step {_step})");
            return;
        }
        if (_clock - _placedAt < AfterPlaceSec)
            return;

        _step++;
        _placedAt = -1;
        _lastPlace = -100;
        _lastGrab = -100;
        if (Done)
            GD.Print($"[sort-bot] {_who} finished its sort script at {_clock:F2}s");
    }
}
