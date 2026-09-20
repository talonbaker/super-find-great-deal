using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>What §5b's audit actually costs.</b> The packet asks for queries per second and server
/// frame time p50/p95 with 150 props being knocked around; this is the instrument that answers
/// it, armed by <c>--reach-cost &lt;seconds&gt;</c> and attached to nothing else.
///
/// <para><b>Counted, not estimated.</b> The query total comes from
/// <see cref="PropManager.IntegrityQueryCount"/>, incremented inside the code that issues the
/// queries — an estimate of "one per settle" would be wrong by exactly the amount the correction
/// path costs, which is the interesting part.</para>
///
/// <para><b>Frame time is the physics process time, not FPS.</b> A dedicated headless server
/// runs its main loop as fast as it is allowed to, so frames per second measures the machine's
/// spare capacity rather than the work; <c>TimePhysicsProcess</c> is the number that grows when
/// 150 props are all settling in one tick.</para>
///
/// <para><b>It is a flag, not an always-on counter.</b> A measurement rig that runs in a real
/// session is part of the thing it measures.</para>
/// </summary>
public sealed partial class ReachCostProbe : Node
{
    /// <summary>Prefix on every line.</summary>
    public const string Prefix = "[reach-cost]";

    /// <summary>The machine-readable line.</summary>
    public const string SummaryPrefix = Prefix + " SUMMARY";

    /// <summary>Seconds of the run to discard before measuring. The first second of a server's
    /// life is world building, prop adoption and JIT; folding it into a p95 would report the
    /// cost of starting up as the cost of the audit.</summary>
    private const double WarmupSec = 1.0;

    /// <summary>The prop manager whose counters are read. Set by <c>Gameplay</c>.</summary>
    public PropManager Props { get; set; } = null!;

    /// <summary>How long to measure for, seconds, from <c>--reach-cost</c>.</summary>
    public double DurationSec { get; set; } = 20.0;

    /// <summary>
    /// How often every prop in the world is shoved back into loose physics, seconds.
    ///
    /// <para><b>Why the probe does the shoving and not the bots.</b> The packet's phrase is "150
    /// props being knocked around by two bots", and two scripted bots walking a headless room
    /// cannot reliably disturb 150 props in twenty seconds — most of them would never leave rest
    /// and the measurement would be of an idle server wearing a crowd. Shoving all of them on a
    /// cadence produces a settle wave every few seconds, which is a STRICTLY HARSHER load than
    /// two bots could ever apply: every prop latches Resting within about a second of each wave,
    /// so the audit runs on all 150 inside one or two physics ticks. The two bots are still in
    /// the room, walking, so the avatar physics and the snapshot traffic are real. Read the
    /// number as an upper bound.</para>
    ///
    /// <para><b>Zero or negative means NEVER shove</b> (SHELF-1, 2026-09-19, via
    /// <c>--cost-shove-every</c>). That turns this probe into the AT-REST instrument as well as
    /// the under-load one: same sampler, same warm-up, same percentile rule, one variable
    /// changed — which is the only way the two numbers in SHELF-1's handoff are comparable to
    /// each other. A room full of sleeping rigid bodies is a real measurement and it is the one
    /// a player spends most of a round inside.</para>
    /// </summary>
    public double ShoveEverySec { get; set; } = 3.0;

    /// <summary>Speed each shove gives a prop, m/s. Enough to slide and tumble a 1 kg crate into
    /// its neighbours — which is what makes the wave produce PROP overlaps and not just a
    /// hundred and fifty identical clean settles.</summary>
    public float ShoveSpeed { get; set; } = 2.5f;

    private readonly List<double> _frameMs = new();
    private double _elapsed;
    private double _measuredFor;
    private long _auditsAtStart;
    private long _queriesAtStart;
    private long _correctionsAtStart;
    private bool _started;
    private bool _done;
    private int _peakLoose;
    private double _sinceShove;
    private int _shoves;

    public override void _Ready() =>
        GD.Print($"{Prefix} measuring for {DurationSec:0.0} s "
                 + $"(after a {WarmupSec:0.0} s warm-up), "
                 + (ShoveEverySec > 0.0
                     ? $"shoving every {ShoveEverySec:0.0} s"
                     : "AT REST (no shove)"));

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
            return;
        _elapsed += delta;
        if (_elapsed < WarmupSec)
            return;

        if (!_started)
        {
            _started = true;
            _auditsAtStart = Props.RestAuditCount;
            _queriesAtStart = Props.IntegrityQueryCount;
            _correctionsAtStart = Props.RestCorrectionCount;
        }

        _measuredFor += delta;
        _frameMs.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);

        int count = 0;
        foreach (NetworkedProp _ in Props.LiveProps)
            count++;
        if (count > _peakLoose)
            _peakLoose = count;

        if (ShoveEverySec > 0.0)
        {
            _sinceShove += delta;
            if (_sinceShove >= ShoveEverySec)
            {
                _sinceShove = 0.0;
                Shove();
            }
        }

        if (_measuredFor >= DurationSec)
            Report();
    }

    /// <summary>
    /// One wave: every prop back into loose physics with a horizontal shove and a small hop.
    ///
    /// <para><b>The direction is derived from the prop id, not drawn at random</b>, so two runs
    /// of this probe on the same tree disturb the same props the same way and the frame-time
    /// numbers can be compared. A random seed here would make every measurement a different
    /// experiment.</para>
    /// </summary>
    private void Shove()
    {
        var ids = new List<int>();
        foreach (NetworkedProp prop in Props.LiveProps)
            ids.Add(prop.PropId);
        foreach (int id in ids)
        {
            float angle = Mathf.Tau * ((id * 2654435761u) % 997u) / 997f;
            Props.ServerNudgeLoose(id, new Vector3(
                Mathf.Cos(angle) * ShoveSpeed, 1.2f, Mathf.Sin(angle) * ShoveSpeed));
        }
        _shoves++;
        GD.Print($"{Prefix} shove {_shoves}: {ids.Count} prop(s) back into loose physics");
    }

    private void Report()
    {
        _done = true;
        long audits = Props.RestAuditCount - _auditsAtStart;
        long queries = Props.IntegrityQueryCount - _queriesAtStart;
        long corrections = Props.RestCorrectionCount - _correctionsAtStart;
        double secs = Math.Max(_measuredFor, 0.001);

        _frameMs.Sort();
        double p50 = Percentile(_frameMs, 0.50);
        double p95 = Percentile(_frameMs, 0.95);
        double peak = _frameMs.Count > 0 ? _frameMs[^1] : 0.0;

        GD.Print($"{Prefix} props in world: {_peakLoose}, shove waves: {_shoves} "
                 + (ShoveEverySec > 0.0
                     ? $"(every {ShoveEverySec:0.0} s at {ShoveSpeed:0.0} m/s)"
                     : "(at rest — shoving disabled by --cost-shove-every)"));
        GD.Print($"{Prefix} rest audits: {audits} in {secs:0.00} s = {audits / secs:0.0}/s "
                 + $"({corrections} correction(s))");
        GD.Print($"{Prefix} integrity queries: {queries} in {secs:0.00} s = {queries / secs:0.0}/s");
        GD.Print($"{Prefix} server physics frame time over {_frameMs.Count} tick(s): "
                 + $"p50 {p50:0.000} ms, p95 {p95:0.000} ms, peak {peak:0.000} ms");
        GD.Print($"{SummaryPrefix} props={_peakLoose} audits={audits} corrections={corrections} "
                 + $"queries={queries} auditsPerSec={audits / secs:0.00} "
                 + $"queriesPerSec={queries / secs:0.00} shoves={_shoves} "
                 + $"frameP50Ms={p50:0.000} frameP95Ms={p95:0.000} framePeakMs={peak:0.000} "
                 + $"seconds={secs:0.00}");
        GetTree().Quit(0);
    }

    /// <summary>Nearest-rank percentile on an already-sorted list. Nearest-rank rather than a
    /// linear interpolation because the quantity is a per-tick measurement, and reporting a
    /// frame time no tick actually took would be a made-up number in a handoff.</summary>
    private static double Percentile(List<double> sorted, double q)
    {
        if (sorted.Count == 0)
            return 0.0;
        int rank = (int)Math.Ceiling(q * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
