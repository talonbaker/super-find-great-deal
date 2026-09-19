using System;
using System.Collections.Generic;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// One sized thing on the Calibration course: a ledge height or a gap width, stated as a fraction of
/// a motor envelope and evaluated at a tuning. Everything a marker prints and a test checks is on
/// this record; the course's <c>Build()</c> only turns it into geometry.
/// </summary>
/// <param name="Id">Stable identity, used as the node-name stem and in tests.</param>
/// <param name="Axis">Ledge (vertical) or gap (horizontal).</param>
/// <param name="Envelope">The envelope the size is a fraction of.</param>
/// <param name="Fraction">That fraction — the authored intent, chosen inside a band.</param>
/// <param name="SizeM">Fraction × envelope at the tuning the plan was evaluated at, metres.</param>
/// <param name="Band">The band <see cref="SizeM"/> classifies into at that tuning.</param>
public sealed record MetricElement(
    string Id,
    MetricAxis Axis,
    MetricEnvelope Envelope,
    float Fraction,
    float SizeM,
    MetricBand Band)
{
    /// <summary>Whether the arithmetic says the body makes it with the band's verb.</summary>
    public bool Reachable => Band.Reachable;

    /// <summary><c>HOP 0.70 m — held jump, no run-up</c>: band, metres to two decimals, verb. The
    /// whole of what a marker says about an element; the course prints it verbatim.</summary>
    public string MarkerText => $"{Band.Label} {MetricBands.Metres(SizeM)} — {Band.Verb}";
}

/// <summary>
/// <b>The Calibration course's metrics, evaluated from the motor rather than remembered</b> — LD-1.
///
/// <para>This is the file that replaced five literals. <c>CalibrationCourse</c> used to carry
/// <c>1.534 / 6.192 / 0.467 / 1.710</c> and a predicted <c>3.83</c>, measured once at MOVE-3e and
/// typed in, and its own header admitted after MOVE-8 that "the whole course with them" was stale.
/// Now every ledge height and every gap width is a <see cref="MetricElement"/>: a fraction of one
/// of <see cref="MotorArc"/>'s envelopes, classified into a <see cref="MetricBands"/> band, and
/// evaluated at build time against <see cref="MotorTuning.Default"/>. Move a gravity row and the
/// gym re-spaces itself; the markers re-print; the colours re-decide. <c>CalibrationPlanTests</c>
/// is the guard: it halves <c>MoveSpeed</c> and requires every gap to halve and every ledge to
/// stand still.</para>
///
/// <para><b>Engine-free on purpose.</b> This is a static plan, not a node, so the xUnit suite can
/// enumerate exactly what the course will build without a Godot runtime under it — the same
/// argument <c>MovementCourseSelectionTests</c> makes. Nothing here touches <c>Godot.*</c>.</para>
///
/// <para><b>The fractions inside a band are value calls, made here and stated.</b> A band says
/// where its edges are; which point inside it a gym element sits at is an authoring choice, and
/// these are chosen so each element sits well inside its band rather than on an edge, so that a
/// verdict is about the band and not about a millimetre. The two exceptions are deliberate: the
/// Edge elements sit at 96% of the envelope because Edge is a 92–100% band and bragging rights live
/// near the top of it, and the Denial elements sit at 120% so that a Denial reads as a wall from
/// across the course (Silli's rule — deny by distance, not by look).</para>
/// </summary>
public static class CalibrationPlan
{
    // ---------------------------------------------------------------------------------------
    // The band elements: fractions, one authored point per band. Every metre on the course that
    // is a ledge height or a gap width comes through Element(), and nowhere else.
    // ---------------------------------------------------------------------------------------

    /// <summary>Where the Kerb element sits: most of a tap, so a tap clears it with margin.</summary>
    public const float KerbPoint = 0.80f;

    /// <summary>The low Hop rung: half the held apex — the middle of the standstill-hop band.</summary>
    public const float HopLowPoint = 0.50f;

    /// <summary>The high Hop rung: the top of the PROVEN sub-band. The tangle spire's 41 rises are
    /// 60–68% of the held apex and climb with no run-up; this rung sits at the top of that.</summary>
    public const float HopHighPoint = 0.66f;

    /// <summary>The Commit element: 80% of the held apex, squarely inside 68–92%.</summary>
    public const float CommitPoint = 0.80f;

    /// <summary>The Edge elements: 96% of the held envelope — inside 92–100%, near the top.</summary>
    public const float EdgePoint = 0.96f;

    /// <summary>The vertical Technique element: 80% of the double-jump apex. It must also clear
    /// 100% of the held apex to be a Technique element at all; the plan checks that.</summary>
    public const float TechniqueApexPoint = 0.80f;

    /// <summary>The horizontal Technique element: 85% of the double-jump range — comfortably above
    /// the held-sprint range (the band's floor) and under the 93% ceiling.</summary>
    public const float TechniqueRangePoint = 0.85f;

    /// <summary>The Denial elements: 120% of the double-jump envelope. Past the 108% floor by
    /// enough to read as a wall rather than a near miss.</summary>
    public const float DenialPoint = 1.20f;

    /// <summary>The Stone element: 70% of the tap range.</summary>
    public const float StonePoint = 0.70f;

    /// <summary>The Jog elements (gap bank, coyote ledge, hold chain, buffer gap, precision
    /// target's near edge): 75% of the held-jog range — inside the band, clear of both edges.</summary>
    public const float JogPoint = 0.75f;

    /// <summary>The Sprint element: 80% of the held-sprint range.</summary>
    public const float SprintPoint = 0.80f;

    /// <summary>The plan at a tuning: every band element the course builds, in build order.</summary>
    public static IReadOnlyList<MetricElement> Elements(in MotorTuning t) => new[]
    {
        // Ledge bank — one rung per vertical band, plus the second Hop rung.
        Element(t, "Ledge_Kerb", MetricAxis.Vertical, MetricEnvelope.TapApex, KerbPoint),
        Element(t, "Ledge_HopLow", MetricAxis.Vertical, MetricEnvelope.HeldApex, HopLowPoint),
        Element(t, "Ledge_HopHigh", MetricAxis.Vertical, MetricEnvelope.HeldApex, HopHighPoint),
        Element(t, "Ledge_Commit", MetricAxis.Vertical, MetricEnvelope.HeldApex, CommitPoint),
        Element(t, "Ledge_Edge", MetricAxis.Vertical, MetricEnvelope.HeldApex, EdgePoint),
        Element(t, "Ledge_Technique", MetricAxis.Vertical, MetricEnvelope.DoubleApex, TechniqueApexPoint),
        Element(t, "Ledge_Denial", MetricAxis.Vertical, MetricEnvelope.DoubleApex, DenialPoint),

        // Gap bank — one lane per horizontal band.
        Element(t, "Gap_Stone", MetricAxis.Horizontal, MetricEnvelope.TapRange, StonePoint),
        Element(t, "Gap_Jog", MetricAxis.Horizontal, MetricEnvelope.HeldJogRange, JogPoint),
        Element(t, "Gap_Sprint", MetricAxis.Horizontal, MetricEnvelope.HeldSprintRange, SprintPoint),
        Element(t, "Gap_Edge", MetricAxis.Horizontal, MetricEnvelope.HeldSprintRange, EdgePoint),
        Element(t, "Gap_Technique", MetricAxis.Horizontal, MetricEnvelope.DoubleRange, TechniqueRangePoint),
        Element(t, "Gap_Denial", MetricAxis.Horizontal, MetricEnvelope.DoubleRange, DenialPoint),

        // The rest of the course, each a band element too.
        Element(t, "AirBrake_HoldGap", MetricAxis.Horizontal, MetricEnvelope.HeldSprintRange, SprintPoint),
        Element(t, "Coyote_Gap", MetricAxis.Horizontal, MetricEnvelope.HeldJogRange, JogPoint),
        Element(t, "Buffer_Gap", MetricAxis.Horizontal, MetricEnvelope.HeldJogRange, JogPoint),
        Element(t, "HoldChain_Gap", MetricAxis.Horizontal, MetricEnvelope.HeldJogRange, JogPoint),
        Element(t, "TapChain_Gap", MetricAxis.Horizontal, MetricEnvelope.TapRange, StonePoint),
        Element(t, "Precision_NearEdge", MetricAxis.Horizontal, MetricEnvelope.HeldJogRange, JogPoint),
        Element(t, "Redirect_StraightGap", MetricAxis.Horizontal, MetricEnvelope.HeldJogRange, JogPoint),
        Element(t, "WalkStair_Rise", MetricAxis.Vertical, MetricEnvelope.TapApex, KerbPoint),
        Element(t, "JumpStair_Rise", MetricAxis.Vertical, MetricEnvelope.HeldApex, HopLowPoint),
    };

    /// <summary>The one element by id, or throw — a course asking for an element the plan does not
    /// have is a course about to size something from nothing.</summary>
    public static MetricElement Get(IReadOnlyList<MetricElement> plan, string id)
    {
        foreach (MetricElement e in plan)
        {
            if (e.Id == id)
                return e;
        }
        throw new ArgumentException($"no element '{id}' in the calibration plan", nameof(id));
    }

    private static MetricElement Element(in MotorTuning t, string id, MetricAxis axis,
        MetricEnvelope envelope, float fraction)
    {
        float size = fraction * MetricBands.EnvelopeM(t, envelope);
        MetricBand? band = MetricBands.Classify(t, axis, size);
        if (band is null)
        {
            throw new InvalidOperationException(
                $"{id}: {MetricBands.Metres(size)} ({fraction:0.00} x {MetricBands.EnvelopeName(envelope)}) "
                + "falls in the unnamed gap between Technique and Denial — nothing is authored there");
        }
        return new MetricElement(id, axis, envelope, fraction, size, band.Value);
    }

    // ---------------------------------------------------------------------------------------
    // Derived annotations: numbers the course prints beside elements that are NOT band elements —
    // where a window closes, where a released jump lands, how far a walk-off travels. Each is a
    // function of the tuning; none is a ledge or a gap. Stated here so the tests can see them.
    // ---------------------------------------------------------------------------------------

    /// <summary>How far past a lip the coyote window is still open at <paramref name="speedMps"/>:
    /// speed × <see cref="MotorTuning.CoyoteTimeSec"/>.</summary>
    public static float CoyoteReachM(in MotorTuning t, float speedMps) => speedMps * t.CoyoteTimeSec;

    /// <summary>
    /// <b>Where a sprint jump lands with the stick released at the lip</b> — the air-brake number.
    /// The motor brakes an airborne body toward zero at <c>Deceleration × AirControlBrake</c> per
    /// second (<c>AvatarMotor.RateFor</c>, <c>grounded: false</c>, no wish), integrated over the
    /// held-sprint airtime in whole ticks. At the shipped 0.55 brake the body stops well inside the
    /// flight, so this is the closed form <c>v²/2a</c> to within a tick; at MOVE-3's 0.30 it would
    /// not stop before landing and the sum below is the honest number. It is the Fork-1 (air
    /// control) question made visible on the gym: move <c>AirControlBrake</c> and this pad walks.
    /// <b>Discrete estimate, not an in-engine measurement</b> — the capture run does not take this
    /// jump, and the marker says so.
    /// </summary>
    public static float ReleasedSprintRangeM(in MotorTuning t)
    {
        float dt = AvatarMotor.TickDelta;
        float v = MotorArc.SprintSpeedMps(t);
        float brake = t.Deceleration * t.AirControlBrake * dt;
        int ticks = (int)MathF.Round(MotorArc.HeldSprint(t).AirtimeSec / dt);
        float x = 0f;
        for (int i = 0; i < ticks; i++)
        {
            x += v * dt;
            v = MathF.Max(0f, v - brake);
        }
        return x;
    }

    /// <summary>
    /// How long a body that walks off a ledge <paramref name="dropM"/> high is in the air, seconds —
    /// the same tick model <see cref="MotorTuningInvariants.SimulateJump"/> runs, started at zero
    /// vertical speed with the jump key up, so the fall gravity and the apex-hang term apply exactly
    /// as the motor applies them. It sizes where the buffer pad has to be for a jog walk-off to land
    /// on it. Not a jump, so it is not on <see cref="MotorArc"/>, whose remit is jumps.
    /// </summary>
    public static float WalkOffFallSec(in MotorTuning t, float dropM)
    {
        float dt = AvatarMotor.TickDelta;
        float vy = 0f;
        float y = 0f;
        int ticks = 0;
        while (y > -dropM && ticks < 1200)
        {
            vy -= MotorTuningInvariants.GravityFor(t, vy, jumpHeld: false) * dt;
            y += vy * dt;
            ticks++;
        }
        return ticks * dt;
    }

    /// <summary>Closed-form distance to reach <paramref name="speedMps"/> from rest at
    /// <see cref="MotorTuning.Acceleration"/>: <c>v²/2a</c>. The sprint-lane paint marks. An
    /// estimate — the motor ramps with a blend on alignment, not a constant — and the marker says
    /// "≈".</summary>
    public static float RampToSpeedM(in MotorTuning t, float speedMps)
        => speedMps * speedMps / (2f * t.Acceleration);

    /// <summary>Closed-form skid distance from <paramref name="speedMps"/> at
    /// <see cref="MotorTuning.SkidDeceleration"/>: <c>v²/2a</c>, or 0 when the speed is under the
    /// skid entry speed and the body just decelerates. The skid-lane paint marks; an estimate,
    /// and the marker says "≈".</summary>
    public static float SkidFromSpeedM(in MotorTuning t, float speedMps)
        => speedMps > t.SkidEnterSpeedMps ? speedMps * speedMps / (2f * t.SkidDeceleration) : 0f;
}
