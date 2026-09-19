using System;
using System.Collections.Generic;
using System.Globalization;

namespace MpFoundation.Net;

/// <summary>Which of the two band tables a size belongs to: a ledge you land on top of, or a gap
/// you cross at the same height.</summary>
public enum MetricAxis { Vertical, Horizontal }

/// <summary>The seven envelopes a band edge or an element can be a fraction of. Every one is a
/// number <see cref="MotorArc"/> derives from a tuning; none is remembered from a playtest.</summary>
public enum MetricEnvelope
{
    /// <summary><see cref="MotorArc.JogTap"/>'s apex.</summary>
    TapApex,
    /// <summary><see cref="MotorArc.HeldSprint"/>'s apex — the same apex as a held jog jump, because
    /// the apex does not depend on ground speed.</summary>
    HeldApex,
    /// <summary><see cref="MotorArc.DoubleJumpAtApex"/>'s apex.</summary>
    DoubleApex,
    /// <summary><see cref="MotorArc.JogTap"/>'s flat range.</summary>
    TapRange,
    /// <summary><see cref="MotorArc.HeldJog"/>'s flat range.</summary>
    HeldJogRange,
    /// <summary><see cref="MotorArc.HeldSprint"/>'s flat range.</summary>
    HeldSprintRange,
    /// <summary><see cref="MotorArc.DoubleJumpAtApex"/>'s flat range.</summary>
    DoubleRange,
}

/// <summary>
/// One row of a band table, evaluated at a tuning. <see cref="LowM"/> is exclusive and
/// <see cref="HighM"/> is inclusive, so a size sitting exactly on an envelope belongs to the band
/// whose ceiling that envelope is — a ledge at exactly the held apex is Edge, not Technique.
/// <see cref="HighM"/> is <c>+∞</c> for Denial, whose floor is therefore inclusive instead.
/// </summary>
/// <param name="Axis">Which table.</param>
/// <param name="Name">The band's name as the tables spell it — "Kerb", "Sprint", "Denial".</param>
/// <param name="Envelope">The envelope the band's defining edge is a fraction of.</param>
/// <param name="Fraction">That fraction. For every band but Denial it defines <see cref="HighM"/>;
/// for Denial it defines <see cref="LowM"/>.</param>
/// <param name="LowM">Exclusive floor of the band, metres, at the tuning it was evaluated at.</param>
/// <param name="HighM">Inclusive ceiling, metres.</param>
/// <param name="Verb">How the band is cleared, in the words a marker prints.</param>
/// <param name="Reachable">Whether the arithmetic says the body makes it with <see cref="Verb"/>.
/// False for Denial only.</param>
public readonly record struct MetricBand(
    MetricAxis Axis,
    string Name,
    MetricEnvelope Envelope,
    float Fraction,
    float LowM,
    float HighM,
    string Verb,
    bool Reachable)
{
    /// <summary>The band as a marker spells it: <c>HOP</c>, <c>TECHNIQUE</c>.</summary>
    public string Label => Name.ToUpperInvariant();

    /// <summary>Whether <paramref name="sizeM"/> falls in this band — exclusive floor, inclusive
    /// ceiling, except that Denial (the one band with no ceiling) owns its floor: "≥ 108%" means
    /// at 108% you are denied. See the type remark.</summary>
    public bool Contains(float sizeM) => float.IsPositiveInfinity(HighM)
        ? sizeM >= LowM
        : sizeM > LowM && sizeM <= HighM;
}

/// <summary>
/// <b>The level-design grammar, as fractions of the motor's envelopes</b> — LD-1, from research
/// A1's two band tables.
///
/// <para><b>Why fractions and not metres.</b> The metres are what the fractions come to at
/// <see cref="MotorTuning.Default"/> <i>today</i>; every time the tuning moves they move with it,
/// and a table that had been typed in metres would be wrong the moment it did — which is exactly
/// what happened to the Calibration course, the Flow course and the bubble-test program's rule 6
/// at MOVE-8. So the table is stated once, here, as fractions, and evaluated on demand against
/// whatever tuning is asked about. <c>docs/levels/METRICS-CARD.md</c> is the human copy, generated
/// by <c>MetricsCardTests</c> from this class, and it fails a test if it goes stale.</para>
///
/// <para><b>The five fractions are the research session's starting values, not measurements.</b>
/// They are named constants so they can be argued with in one place:</para>
/// <list type="bullet">
/// <item><see cref="HopFraction"/> (0.68) — the top of the standstill-hop band. The tangle spire's
/// 41 rises are all 0.84–0.95 m against a 1.407 m apex (60–68%), and that climb needs no run-up
/// and no air jump, so 68% is the top of a PROVEN band, not a guess.</item>
/// <item><see cref="CommitFraction"/> (0.92) — where an element stops being "free" and starts
/// being "I have to mean this". The playground's as-played convention and the 60 Hz Euler tick
/// together cost about 4–5% against the closed form, and a band edge inside that error is one
/// nobody can author to; 92% keeps clear of it.</item>
/// <item><see cref="StoneFraction"/> (0.95) — a stepping stone is a tap that must not feel like a
/// jump, so it stops just short of the tap's whole range.</item>
/// <item><see cref="TechniqueFraction"/> (0.93) — the top of "this needs the verb": the double
/// jump's envelope with the same authoring margin as the commit band.</item>
/// <item><see cref="DenialFraction"/> (1.08) — the bottom of "cannot". Silli's Mirror's Edge fix
/// was to move non-reachable surfaces AWAY from the play area rather than dress them differently:
/// a surface 5% out of reach is a trap, one well out of reach is a wall. 108% is where the tables
/// start calling it a wall, and the 93–108% gap between Technique and Denial is deliberately
/// unnamed — nothing should be authored there.</item>
/// </list>
///
/// <para><b>Reading a band.</b> Floors are exclusive and ceilings inclusive, so a size exactly at
/// an envelope belongs to the band that envelope tops. The one gap in each table (between
/// Technique's ceiling and Denial's floor) classifies to <c>null</c>: a level element sized there
/// is a defect in the level, and <see cref="Classify"/> says so by refusing to name it.</para>
/// </summary>
public static class MetricBands
{
    /// <summary>Top of the Hop band, as a fraction of the held apex.</summary>
    public const float HopFraction = 0.68f;

    /// <summary>Top of the Commit (vertical) and Sprint (horizontal) bands, as a fraction of the
    /// held envelope; also the top of the Jog band as a fraction of the held-jog range.</summary>
    public const float CommitFraction = 0.92f;

    /// <summary>Top of the Stone band, as a fraction of the tap range.</summary>
    public const float StoneFraction = 0.95f;

    /// <summary>Top of the Technique band, as a fraction of the double-jump envelope.</summary>
    public const float TechniqueFraction = 0.93f;

    /// <summary>Bottom of the Denial band, as a fraction of the double-jump envelope.</summary>
    public const float DenialFraction = 1.08f;

    /// <summary>An envelope's value at a tuning, metres. The one place a <see cref="MetricEnvelope"/>
    /// is turned into a number.</summary>
    public static float EnvelopeM(in MotorTuning t, MetricEnvelope envelope) => envelope switch
    {
        MetricEnvelope.TapApex => MotorArc.JogTap(t).ApexM,
        MetricEnvelope.HeldApex => MotorArc.HeldSprint(t).ApexM,
        MetricEnvelope.DoubleApex => MotorArc.DoubleJumpAtApex(t).ApexM,
        MetricEnvelope.TapRange => MotorArc.JogTap(t).RangeM,
        MetricEnvelope.HeldJogRange => MotorArc.HeldJog(t).RangeM,
        MetricEnvelope.HeldSprintRange => MotorArc.HeldSprint(t).RangeM,
        MetricEnvelope.DoubleRange => MotorArc.DoubleJumpAtApex(t).RangeM,
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope, "not an envelope"),
    };

    /// <summary>The name the card and the markers use for an envelope.</summary>
    public static string EnvelopeName(MetricEnvelope envelope) => envelope switch
    {
        MetricEnvelope.TapApex => "tap apex",
        MetricEnvelope.HeldApex => "held apex",
        MetricEnvelope.DoubleApex => "double-jump apex",
        MetricEnvelope.TapRange => "tap range",
        MetricEnvelope.HeldJogRange => "held-jog range",
        MetricEnvelope.HeldSprintRange => "held-sprint range",
        MetricEnvelope.DoubleRange => "double-jump range",
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope, "not an envelope"),
    };

    /// <summary>
    /// <b>Vertical — ledges and step-ups</b>, landing on top from a flat launch. Six bands, in
    /// order, contiguous from zero up to Technique's ceiling, then the unnamed gap, then Denial.
    /// </summary>
    public static IReadOnlyList<MetricBand> Vertical(in MotorTuning t)
    {
        float tapApex = EnvelopeM(t, MetricEnvelope.TapApex);
        float heldApex = EnvelopeM(t, MetricEnvelope.HeldApex);
        float doubleApex = EnvelopeM(t, MetricEnvelope.DoubleApex);

        float kerbTop = tapApex;
        float hopTop = HopFraction * heldApex;
        float commitTop = CommitFraction * heldApex;
        float edgeTop = heldApex;
        float techniqueTop = TechniqueFraction * doubleApex;
        float denialFloor = DenialFraction * doubleApex;

        return new[]
        {
            new MetricBand(MetricAxis.Vertical, "Kerb", MetricEnvelope.TapApex, 1f,
                0f, kerbTop, "a tap", true),
            new MetricBand(MetricAxis.Vertical, "Hop", MetricEnvelope.HeldApex, HopFraction,
                kerbTop, hopTop, "held jump, no run-up", true),
            new MetricBand(MetricAxis.Vertical, "Commit", MetricEnvelope.HeldApex, CommitFraction,
                hopTop, commitTop, "held jump with a run-up", true),
            new MetricBand(MetricAxis.Vertical, "Edge", MetricEnvelope.HeldApex, 1f,
                commitTop, edgeTop, "held jump, the limit", true),
            new MetricBand(MetricAxis.Vertical, "Technique", MetricEnvelope.DoubleApex, TechniqueFraction,
                edgeTop, techniqueTop, "double jump only", true),
            new MetricBand(MetricAxis.Vertical, "Denial", MetricEnvelope.DoubleApex, DenialFraction,
                denialFloor, float.PositiveInfinity, "cannot", false),
        };
    }

    /// <summary>
    /// <b>Horizontal — gaps</b>, launching and landing at the same height. Six bands, same
    /// shape as <see cref="Vertical"/>: contiguous up to Technique, an unnamed gap, Denial.
    /// </summary>
    public static IReadOnlyList<MetricBand> Horizontal(in MotorTuning t)
    {
        float tapRange = EnvelopeM(t, MetricEnvelope.TapRange);
        float heldJog = EnvelopeM(t, MetricEnvelope.HeldJogRange);
        float heldSprint = EnvelopeM(t, MetricEnvelope.HeldSprintRange);
        float doubleRange = EnvelopeM(t, MetricEnvelope.DoubleRange);

        float stoneTop = StoneFraction * tapRange;
        float jogTop = CommitFraction * heldJog;
        float sprintTop = CommitFraction * heldSprint;
        float edgeTop = heldSprint;
        float techniqueTop = TechniqueFraction * doubleRange;
        float denialFloor = DenialFraction * doubleRange;

        return new[]
        {
            new MetricBand(MetricAxis.Horizontal, "Stone", MetricEnvelope.TapRange, StoneFraction,
                0f, stoneTop, "a tap at a jog", true),
            new MetricBand(MetricAxis.Horizontal, "Jog", MetricEnvelope.HeldJogRange, CommitFraction,
                stoneTop, jogTop, "held jump at a jog", true),
            new MetricBand(MetricAxis.Horizontal, "Sprint", MetricEnvelope.HeldSprintRange, CommitFraction,
                jogTop, sprintTop, "held jump at a sprint", true),
            new MetricBand(MetricAxis.Horizontal, "Edge", MetricEnvelope.HeldSprintRange, 1f,
                sprintTop, edgeTop, "held sprint, the limit", true),
            new MetricBand(MetricAxis.Horizontal, "Technique", MetricEnvelope.DoubleRange, TechniqueFraction,
                edgeTop, techniqueTop, "double jump from a sprint", true),
            new MetricBand(MetricAxis.Horizontal, "Denial", MetricEnvelope.DoubleRange, DenialFraction,
                denialFloor, float.PositiveInfinity, "cannot", false),
        };
    }

    /// <summary>The table for an axis.</summary>
    public static IReadOnlyList<MetricBand> Table(in MotorTuning t, MetricAxis axis)
        => axis == MetricAxis.Vertical ? Vertical(t) : Horizontal(t);

    /// <summary>
    /// Which band a size falls in at a tuning, or <c>null</c> for a size that falls in the unnamed
    /// gap between Technique and Denial (or is not positive). A <c>null</c> is a finding about the
    /// element, not about this class: nothing should be authored there.
    /// </summary>
    public static MetricBand? Classify(in MotorTuning t, MetricAxis axis, float sizeM)
    {
        if (!(sizeM > 0f) || !float.IsFinite(sizeM))
            return null;
        foreach (MetricBand band in Table(t, axis))
        {
            if (band.Contains(sizeM))
                return band;
        }
        return null;
    }

    /// <summary>Metres covered in <paramref name="seconds"/> at <paramref name="speedMps"/> — the
    /// cadence conversion the card prints so a "two seconds of sprint" can be drawn as a distance.</summary>
    public static float CadenceM(float speedMps, float seconds) => speedMps * seconds;

    /// <summary>Metres to two decimals, invariant culture — the one formatting every marker and
    /// every card row goes through, so a machine set to a comma decimal cannot print a different
    /// gym from the one the test checked.</summary>
    public static string Metres(float m) => m.ToString("0.00", CultureInfo.InvariantCulture) + " m";
}
