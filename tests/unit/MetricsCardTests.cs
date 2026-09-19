using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MpFoundation.Dev.Playground;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>LD-1: the metric card is printed by a test, not by a person.</b>
///
/// <para>The brief asked for the metrics to be "logged in a general-info section for reuse across
/// levels". This repo has already learned what a logged number does: MOVE-8 found eight stale copies
/// of four literals. So <c>docs/levels/METRICS-CARD.md</c> is GENERATED — from <c>MotorArc</c>,
/// <c>MetricBands</c> and <c>CalibrationPlan</c> at <c>MotorTuning.Default</c> — and
/// <see cref="TheCommittedCard_IsByteIdenticalToTheGeneratedOne"/> fails if the committed file
/// differs from what this class generates. That is the <c>MotorTuningDefaultIdentityTests</c>
/// pattern applied to a document.</para>
///
/// <para><b>To regenerate</b> after a tuning or band change:
/// <c>SAIL_REGENERATE_METRICS_CARD=1 dotnet test tests/unit/SailNet.Tests.csproj --filter MetricsCardTests</c>
/// (PowerShell: <c>$env:SAIL_REGENERATE_METRICS_CARD=1</c> first). With the variable set the test
/// writes the file and then asserts against it, so a regeneration run is green by construction and
/// the diff it leaves in git is the review.</para>
///
/// <para><b>Byte-identical, deliberately.</b> LF line endings, UTF-8 without a BOM, invariant
/// culture, no timestamp — the file is a pure function of the code, so the same code always
/// produces the same bytes on every machine, and <c>core.autocrlf</c> is <c>false</c> in this
/// repo. A comparison that normalised anything would be a comparison that let something drift.</para>
/// </summary>
public class MetricsCardTests
{
    public const string RelativePath = "docs/levels/METRICS-CARD.md";
    public const string RegenerateVariable = "SAIL_REGENERATE_METRICS_CARD";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // =============================================================================================
    // The guard.
    // =============================================================================================

    [Fact]
    public void TheCommittedCard_IsByteIdenticalToTheGeneratedOne()
    {
        string path = Path.Combine(RepoRoot(), RelativePath.Replace('/', Path.DirectorySeparatorChar));
        string generated = Generate(MotorTuning.Default);

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RegenerateVariable)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated, Utf8NoBom);
        }

        Assert.True(File.Exists(path), $"{RelativePath} is missing — set {RegenerateVariable}=1 and run this test to write it");
        byte[] committed = File.ReadAllBytes(path);
        byte[] expected = Utf8NoBom.GetBytes(generated);
        Assert.True(committed.AsSpan().SequenceEqual(expected),
            $"{RelativePath} differs from what MetricsCardTests generates ({committed.Length} vs {expected.Length} bytes). "
            + $"The motor, a band fraction or the plan moved: set {RegenerateVariable}=1, re-run this test, and commit the diff.");
    }

    [Fact]
    public void TheGenerator_IsDeterministic_AndCultureProof()
    {
        string a = Generate(MotorTuning.Default);
        CultureInfo was = CultureInfo.CurrentCulture;
        string b;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            b = Generate(MotorTuning.Default);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
        Assert.Equal(a, b);
        Assert.DoesNotContain("\r", a);
        Assert.EndsWith("\n", a, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCard_SaysItIsGenerated_AndByWhichTest()
    {
        string card = Generate(MotorTuning.Default);
        Assert.Contains("GENERATED", card, StringComparison.Ordinal);
        Assert.Contains("tests/unit/MetricsCardTests.cs", card, StringComparison.Ordinal);
        Assert.Contains(RegenerateVariable, card, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCard_FollowsTheTuning()
    {
        string shipped = Generate(MotorTuning.Default);
        string slower = Generate(MotorTuning.Default with { MoveSpeed = MotorTuning.Default.MoveSpeed * 0.5f });
        Assert.NotEqual(shipped, slower);
        Assert.Contains("| Held sprint | 1.407 m | 0.667 s | 4.053 m |", shipped, StringComparison.Ordinal);
        Assert.Contains("| Held sprint | 1.407 m | 0.667 s | 2.027 m |", slower, StringComparison.Ordinal);
    }

    // =============================================================================================
    // The generator.
    // =============================================================================================

    /// <summary>The whole card, as a string, from a tuning. Public so a future consumer (a level
    /// packet's own check, a readout) can print the same card from the same code.</summary>
    public static string Generate(in MotorTuning t)
    {
        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append('\n');

        JumpArc held = MotorArc.HeldSprint(t);
        JumpArc jog = MotorArc.HeldJog(t);
        JumpArc tap = MotorArc.JogTap(t);
        JumpArc dbl = MotorArc.DoubleJumpAtApex(t);
        float jogMps = t.MoveSpeed;
        float sprintMps = MotorArc.SprintSpeedMps(t);
        float walkMps = t.MoveSpeed * MpFoundation.Game.Sandbox.LocomotionProfile.WalkFraction;

        L("# Metrics card — GENERATED, do not edit");
        L();
        L("> **This file is generated by `tests/unit/MetricsCardTests.cs`** from `scripts/net/MotorArc.cs`,");
        L("> `scripts/net/MetricBands.cs` and `scripts/dev/playground/CalibrationPlan.cs`, evaluated at");
        L("> `MotorTuning.Default`. The test **fails if this file differs** from what it generates. Do not");
        L("> edit it by hand and do not copy its numbers into another document — cite this file. To");
        L($"> regenerate after a tuning or band change: set `{RegenerateVariable}=1` and run");
        L("> `dotnet test tests/unit/SailNet.Tests.csproj --filter MetricsCardTests`, then commit the diff.");
        L();
        L("Every metre below is a function of the shipped motor. The band **fractions** are the spec;");
        L("the metres are what they come to today. A level is sized against the fractions, and the gym");
        L("(`Playground / Calibration`) is built from them at launch — see research A1 and LD-1.");
        L();
        L("## Speeds");
        L();
        L("| Gear | m/s | Source |");
        L("|---|---|---|");
        L($"| Walk | {F2(walkMps)} | `MoveSpeed × LocomotionProfile.WalkFraction` |");
        L($"| Jog | {F2(jogMps)} | `MoveSpeed` |");
        L($"| Sprint | {F2(sprintMps)} | `MoveSpeed × SprintMultiplier` (`MotorArc.SprintSpeedMps`) |");
        L();
        L("## The four arcs (flat ground, as played)");
        L();
        L("| Arc | Apex | Airtime | Flat range | Source |");
        L("|---|---|---|---|---|");
        L($"| Held sprint | {Arc(held)} | `MotorArc.HeldSprint` |");
        L($"| Held jog | {Arc(jog)} | `MotorArc.HeldJog` |");
        L($"| Jog tap | {Arc(tap)} | `MotorArc.JogTap` |");
        L($"| Double jump at apex | {Arc(dbl)} | `MotorArc.DoubleJumpAtApex` |");
        L();
        L("The apex does not depend on ground speed, so a held jog and a held sprint rise the same;");
        L("only the range differs. \"As played\" is MOVE-3d's playground convention (the launch step out");
        L("of the apex, one tick out of the airtime). The double jump is the reachability ceiling.");
        L();
        L("## Vertical bands — ledges and step-ups (land on top, launch from flat)");
        L();
        BandTable(L, MetricBands.Vertical(t));
        L();
        L("## Horizontal bands — gaps (launch and land at the same height)");
        L();
        BandTable(L, MetricBands.Horizontal(t));
        L();
        L("A band's floor is exclusive and its ceiling inclusive: a size exactly at an envelope belongs to");
        L("the band that envelope tops. **The gap between Technique's ceiling and Denial's floor is");
        L("deliberately unnamed** — nothing is authored there. Denial is the only band the arithmetic");
        L("calls unreachable; everything else is green on the gym with the verb named.");
        L();
        L("## Band constants");
        L();
        L("| Constant | Value | Defines |");
        L("|---|---|---|");
        L($"| `MetricBands.HopFraction` | {F2(MetricBands.HopFraction)} | top of Hop, × held apex |");
        L($"| `MetricBands.CommitFraction` | {F2(MetricBands.CommitFraction)} | top of Commit / Jog / Sprint, × the held envelope |");
        L($"| `MetricBands.StoneFraction` | {F2(MetricBands.StoneFraction)} | top of Stone, × tap range |");
        L($"| `MetricBands.TechniqueFraction` | {F2(MetricBands.TechniqueFraction)} | top of Technique, × the double-jump envelope |");
        L($"| `MetricBands.DenialFraction` | {F2(MetricBands.DenialFraction)} | floor of Denial, × the double-jump envelope |");
        L();
        L("## Cadence — seconds of travel as metres");
        L();
        L("| Seconds | At jog | At sprint |");
        L("|---|---|---|");
        foreach (float sec in new[] { 2f, 3f })
            L($"| {sec:0} s | {M(MetricBands.CadenceM(jogMps, sec))} | {M(MetricBands.CadenceM(sprintMps, sec))} |");
        L();
        L("## The gym — `CalibrationCourse`, sized from `CalibrationPlan`");
        L();
        L("| Element | Axis | Band | Fraction × envelope | Today | Verb | Colour |");
        L("|---|---|---|---|---|---|---|");
        foreach (MetricElement e in CalibrationPlan.Elements(t))
        {
            L($"| `{e.Id}` | {e.Axis} | {e.Band.Name} | {F2(e.Fraction)} × {MetricBands.EnvelopeName(e.Envelope)} "
              + $"| {M(e.SizeM)} | {e.Band.Verb} | {(e.Reachable ? "green" : "red")} |");
        }
        L();
        L("Derived annotations the gym also prints (not band elements):");
        L();
        L($"- Released-at-the-lip sprint jump lands at **{M(CalibrationPlan.ReleasedSprintRangeM(t))}**");
        L("  (`CalibrationPlan.ReleasedSprintRangeM` — the air brake integrated over the flight; a discrete");
        L("  estimate, not a capture).");
        L($"- Coyote window closes **{M(CalibrationPlan.CoyoteReachM(t, jogMps))}** past a lip at jog,");
        L($"  **{M(CalibrationPlan.CoyoteReachM(t, sprintMps))}** at sprint (`speed × CoyoteTimeSec`).");
        L($"- Ramp to jog ≈ **{M(CalibrationPlan.RampToSpeedM(t, jogMps))}**, to sprint ≈ "
          + $"**{M(CalibrationPlan.RampToSpeedM(t, sprintMps))}**; sprint skid ≈ "
          + $"**{M(CalibrationPlan.SkidFromSpeedM(t, sprintMps))}** (closed forms, `v²/2a`).");
        L();
        L("## What this card cannot say yet");
        L();
        L("- **Drops extend range.** Every arc here is flat ground; a gap with a height delta is authored");
        L("  by feel until `MotorArc.HeldSprintRangeAtDrop(dropM)` exists (research A1, a later packet).");
        L("- **Lateral air control** has no derivation; the gym's redirect pads carry no verdict.");
        L("- **Corridor and door widths** need the body's collision radius as a named constant");
        L("  (`AvatarProportions.CapsuleRadiusM` is per-avatar, from the mesh bounds); no width band yet.");
        L("- **Slopes** have no band — the motor reads no floor normal.");

        return sb.ToString();
    }

    private static void BandTable(Action<string> L, IReadOnlyList<MetricBand> bands)
    {
        L("| Band | Fraction of envelope | Today | Cleared by |");
        L("|---|---|---|---|");
        foreach (MetricBand b in bands)
        {
            string fraction = b.Name == "Denial"
                ? $"≥ {Pct(b.Fraction)} of {MetricBands.EnvelopeName(b.Envelope)}"
                : $"≤ {Pct(b.Fraction)} of {MetricBands.EnvelopeName(b.Envelope)}";
            string today = b.Name == "Denial"
                ? $"≥ {M(b.LowM)}"
                : b.LowM > 0f ? $"{M(b.LowM)} – {M(b.HighM)}" : $"≤ {M(b.HighM)}";
            L($"| {b.Name} | {fraction} | {today} | {b.Verb} |");
        }
    }

    private static string Arc(JumpArc a) =>
        $"{a.ApexM.ToString("0.000", CultureInfo.InvariantCulture)} m | "
        + $"{a.AirtimeSec.ToString("0.000", CultureInfo.InvariantCulture)} s | "
        + $"{a.RangeM.ToString("0.000", CultureInfo.InvariantCulture)} m";

    private static string M(float metres) => MetricBands.Metres(metres);
    private static string F2(float x) => x.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Pct(float fraction) => (fraction * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The repo root: the nearest ancestor of the test assembly that holds
    /// <c>project.godot</c>. The same walk <c>BubbleTestLayoutTests</c> and four siblings do.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not find project.godot above the test assembly");
        return dir!.FullName;
    }
}
