using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using MpFoundation.Net;

namespace Sail.Game.World;

/// <summary>
/// <b>Seconds since the body last acted, and stop-seconds — the two cadence numbers LD-2 gives
/// Talon's two rules</b> (research §A2-R1 / §A2-R3, 2026-09-02). No Godot node in it: it is fed
/// one <see cref="MoveState"/> per fixed tick by <see cref="CadenceTracker"/> and holds nothing a
/// test cannot construct, for the reason <c>MovementVerbReadout</c> gives — the xUnit suite has
/// no engine, so the thing this packet has to PROVE (10 s of standing reads 10.0, a tap landing
/// does not reset the clock, a held one does) is a property of a value rather than a claim about
/// a label that only existed inside a window.
///
/// <para><b>What counts as an act — this list is the spec</b> (INTERACTION-BIBLE: an act is
/// something the body does, never something done to it):</para>
/// <list type="bullet">
/// <item><b>A ground jump</b> (or a coyote jump): an upward impulse of more than
///   <see cref="JumpImpulseMps"/> between two consecutive ticks. Gravity only ever lowers
///   <c>Velocity.Y</c>, so a rise that large is a launch and nothing else the motor does.</item>
/// <item><b>An air jump</b>: <see cref="MoveState.AirJumpsUsed"/> went up. Counted from the
///   counter rather than the impulse because mode 1 ASSIGNS the vertical speed — a press taken
///   while still rising is a small delta the impulse test would miss.</item>
/// <item><b>A landing after a held-length airtime</b>: <c>Grounded</c> went false→true after
///   more than <see cref="LandingResetAirtimeSec"/> in the air. A tap's landing does NOT reset —
///   the tap is one act, already counted at takeoff, and a hop that counted twice would make
///   a bunny-hopping player read as acting every quarter second.</item>
/// <item><b>A skid start</b>: <see cref="MoveState.SkidRemaining"/> went 0→positive (SKID-1's
///   turnaround). A skid is a committed run reversed; it is the body doing something.</item>
/// <item><b>A slide start</b>: <see cref="MoveState.Verb"/> became <see cref="MoveVerb.Slide"/>.
///   The tuck and the duck walk are deliberately NOT acts — a crouch held still is the opposite
///   of acting, and a duck walk is a gear, not an event.</item>
/// <item><b>An external act</b> through <see cref="NoteAct"/>: a grab, a throw, a drop, a bubble
///   pop, a lever pull, a television — whatever the owning system reports. The clock never
///   guesses these from the state; the systems that know report them.</item>
/// </list>
///
/// <para><b>Not an act</b>: standing, walking, running (speed is not an event), a teleport (the
/// TV reports itself; a respawn is something done TO the body), being knocked out, a
/// reconciliation snap (see the tracker for the one case that can fake an impulse).</para>
///
/// <para><b>Stopped</b> (research §A2-R3, verbatim intent): grounded, horizontal speed below
/// <see cref="StopSpeedMps"/>, and not in a menu. <see cref="StopSpeedFraction"/> is 0.9 × jog —
/// under tick noise on a body holding jog (the motor's ground ramp settles within a tick, so a
/// held jog never dips 10 %), and comfortably above a walk (0.45 × jog), so a walk counts as
/// stopped. Whether it SHOULD is a direction fork the report raises; the constant is one place so
/// the answer moves one number.</para>
///
/// <para><b>Idempotent per tick</b> (MECHANICS-BIBLE §4): every act is an EDGE between two
/// consecutive fed states, never a level, so a state fed twice cannot fire twice and a held
/// condition (still skidding, still airborne) cannot re-fire. <see cref="Feed"/> assumes it is
/// called exactly once per fixed tick with that tick's resulting state; the tracker guarantees
/// that by reading in <c>_PhysicsProcess</c>.</para>
///
/// <para><b>The rolling window</b> is sixty one-second buckets. <see cref="StopSecondsLastMinute"/>
/// is their sum, so it is the stop time in roughly the last 60 s (59–60 s, bucket-quantised)
/// and, in the first minute, simply the stop time so far. Double accumulators throughout: 600
/// float ticks of 1/60 s drift by a millionth of a second, not a tenth, and the 10.0 ± 0.1 the
/// packet asks for is what a double gives without rounding tricks.</para>
/// </summary>
public sealed class CadenceClock
{
    /// <summary>The stop threshold as a fraction of <see cref="MotorTuning.MoveSpeed"/> (jog).
    /// One constant, per the packet: 3.42 m/s at the shipped 3.8.</summary>
    public const float StopSpeedFraction = 0.9f;

    /// <summary>The rolling window, seconds. "Per minute" is this number.</summary>
    public const double WindowSec = 60.0;

    /// <summary>The upward impulse that reads as a launch, as a fraction of
    /// <see cref="MotorTuning.JumpVelocity"/>. Half: a ground jump from rest is the whole
    /// velocity, a coyote jump from a fall is more, and nothing gravity does is positive.</summary>
    public const float JumpImpulseFraction = 0.5f;

    private const int BucketCount = 60;
    private const double BucketSec = WindowSec / BucketCount;

    /// <summary>Below this horizontal speed a grounded body is stopped.</summary>
    public static float StopSpeedMps(in MotorTuning t) => t.MoveSpeed * StopSpeedFraction;

    /// <summary>A rise in <c>Velocity.Y</c> of at least this between two ticks is a jump.</summary>
    public static float JumpImpulseMps(in MotorTuning t) => t.JumpVelocity * JumpImpulseFraction;

    /// <summary>
    /// A landing after longer than this in the air resets the clock. <b>Derived, not typed</b>:
    /// the midpoint between <see cref="MotorArc.JogTap"/>'s airtime and
    /// <see cref="MotorArc.HeldSprint"/>'s, so it sits as far from both as it can and moves with
    /// the tuning rather than going stale against it (the way every hard-coded body dimension in
    /// this repo has). Measured at the shipped tuning (LD-2): tap 0.233 s, held 0.667 s, so the
    /// bar is 0.450 s.
    /// </summary>
    public static float LandingResetAirtimeSec(in MotorTuning t) =>
        0.5f * (MotorArc.JogTap(t).AirtimeSec + MotorArc.HeldSprint(t).AirtimeSec);

    /// <summary>The stop predicate, shared with the tracker so the section attribution and the
    /// accumulator can never disagree about what a stop is.</summary>
    public static bool IsStopped(in MotorTuning t, in MoveState s, bool inMenu) =>
        s.Grounded && !inMenu && HorizontalSpeed(s.Velocity) < StopSpeedMps(t);

    private bool _primed;
    private MoveState _prev;
    private double _elapsed;
    private double _sinceAct;
    private double _stopTotal;
    private double _airborne;
    private readonly double[] _buckets = new double[BucketCount];
    private int _bucket;
    private readonly Dictionary<string, double> _bySection = new(StringComparer.Ordinal);

    /// <summary>Seconds since the last act — the readout's first line. Counts from the first
    /// feed until the first act, so a body that has never acted reads its whole life.</summary>
    public double SinceActSec => _sinceAct;

    /// <summary>Stop-seconds since the clock started.</summary>
    public double StopSecondsTotal => _stopTotal;

    /// <summary>Seconds fed so far.</summary>
    public double ElapsedSec => _elapsed;

    /// <summary>Stop-seconds in the last 60 s — the readout's second line.</summary>
    public double StopSecondsLastMinute
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < BucketCount; i++)
                sum += _buckets[i];
            return sum;
        }
    }

    /// <summary>Stop-seconds per minute over the whole session — what telemetry sends. Zero
    /// before anything has been fed.</summary>
    public double StopSecondsPerMinuteSession =>
        _elapsed <= 0 ? 0 : _stopTotal * WindowSec / _elapsed;

    /// <summary>Stop-seconds keyed by whatever section key the feeder supplied (bubbletest: the
    /// <c>BubbleTestLayout.Section</c> name). Empty in a world with no sections.</summary>
    public IReadOnlyDictionary<string, double> StopSecondsBySection => _bySection;

    /// <summary>What the last act was, for the readout — "jump", "land", "skid", "slide",
    /// "air-jump", or whatever <see cref="NoteAct"/> was handed. Empty until the first.</summary>
    public string LastAct { get; private set; } = "";

    /// <summary>Acts so far, all kinds.</summary>
    public int ActCount { get; private set; }

    /// <summary>An act the state cannot show — a grab, a pop, a lever. Resets the clock.</summary>
    public void NoteAct(string kind)
    {
        _sinceAct = 0;
        LastAct = kind;
        ActCount++;
    }

    /// <summary>
    /// One fixed tick. <paramref name="next"/> is the state the motor produced this tick;
    /// <paramref name="section"/> is where the body is, used only to attribute a stop.
    /// </summary>
    public void Feed(in MotorTuning tuning, in MoveState next, float dt, bool inMenu,
        string? section = null)
    {
        if (dt <= 0f)
            return;

        _elapsed += dt;
        _sinceAct += dt;
        AdvanceWindow();

        if (_primed)
        {
            // Edges, in the order they can co-occur: a landing tick can also be a skid start.
            if (next.AirJumpsUsed > _prev.AirJumpsUsed)
                NoteAct("air-jump");
            else if (next.Velocity.Y - _prev.Velocity.Y >= JumpImpulseMps(tuning))
                NoteAct("jump");

            if (!_prev.Grounded && next.Grounded && _airborne > LandingResetAirtimeSec(tuning))
                NoteAct("land");

            if (_prev.SkidRemaining <= 0f && next.SkidRemaining > 0f)
                NoteAct("skid");

            if (_prev.Verb != MoveVerb.Slide && next.Verb == MoveVerb.Slide)
                NoteAct("slide");
        }

        if (next.Grounded)
            _airborne = 0;
        else
            _airborne += dt;

        if (IsStopped(tuning, next, inMenu))
        {
            _stopTotal += dt;
            _buckets[_bucket] += dt;
            if (section != null)
                _bySection[section] = _bySection.TryGetValue(section, out double s) ? s + dt : dt;
        }

        _prev = next;
        _primed = true;
    }

    /// <summary>Rotate the ring to the bucket <see cref="_elapsed"/> now falls in, zeroing every
    /// bucket passed on the way — that is how a second older than the window leaves it.</summary>
    private void AdvanceWindow()
    {
        int target = (int)(_elapsed / BucketSec) % BucketCount;
        while (_bucket != target)
        {
            _bucket = (_bucket + 1) % BucketCount;
            _buckets[_bucket] = 0;
        }
    }

    private static float HorizontalSpeed(Vector3 v) => Mathf.Sqrt(v.X * v.X + v.Z * v.Z);
}

/// <summary>
/// The two readout lines, as pure text — screen-space only (Talon's ruling: nothing billboarded
/// in the world). Same column as <c>MovementVerbReadout</c> so a reader used to the playground
/// reads this without re-learning it.
/// </summary>
public static class CadenceReadout
{
    private const int LabelWidth = 11;

    /// <summary>Both lines, newline-separated. A null clock (no local body yet) reads as dashes
    /// rather than zeros, because a zero here would be a claim.</summary>
    public static string Lines(CadenceClock? clock) =>
        clock is null
            ? Label("since-act") + "--\n" + Label("stop") + "--"
            : SinceActLine(clock) + "\n" + StopLine(clock);

    /// <summary><c>since-act  N.N s</c>, with what the last act was when there has been one.</summary>
    public static string SinceActLine(CadenceClock clock)
    {
        string line = Label("since-act") + F1(clock.SinceActSec) + " s";
        return clock.LastAct.Length == 0 ? line : line + "   (last: " + clock.LastAct + ")";
    }

    /// <summary><c>stop  N.N s/min</c> — the last minute — with the session total beside it.</summary>
    public static string StopLine(CadenceClock clock) =>
        Label("stop") + F1(clock.StopSecondsLastMinute) + " s/min   (total "
        + F1(clock.StopSecondsTotal) + " s)";

    private static string Label(string name) => name.PadRight(LabelWidth);

    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
}
