using System;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// LD-4: the engine-free half of the wind speedometer. Acceptance criteria 1–3 of the packet,
/// numbers in the test names. The node (<c>SpeedWindLayer</c>) needs a bus graph and a scene and
/// is checked by a headed run instead; everything here runs without Godot.
/// </summary>
public class SpeedWindCurveTests
{
    private static float Walk => SpeedWindCurve.WalkMps;
    private static float Jog => SpeedWindCurve.JogMps;
    private static float Sprint => SpeedWindCurve.SprintMps;

    [Fact]
    public void LadderIsReadOffMotorTuningNotCopied()
    {
        // 1.71 / 3.80 / 6.08 at the shipped ladder; if MOVE-N moves it, the curve moves with it.
        Assert.Equal(MotorTuning.Default.DuckWalkSpeedMps, Walk);
        Assert.Equal(MotorTuning.Default.MoveSpeed, Jog);
        Assert.Equal(MotorTuning.Default.MoveSpeed * MotorTuning.Default.SprintMultiplier, Sprint);
        Assert.True(Walk < Jog && Jog < Sprint, "the ladder must be ordered walk < jog < sprint");
    }

    [Fact]
    public void GainAtWalkIsSilent_Minus80Db()
    {
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.BodyGainDb(Walk));
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.BodyGainDb(Walk * 0.5f));
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.BodyGainDb(0f));
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.HissGainDb(Walk));
    }

    [Fact]
    public void GainAndCutoffAreNonDecreasing_Walk_Jog_Sprint_1p2Sprint()
    {
        float[] ladder = { Walk, Jog, Sprint, Sprint * 1.2f };
        for (int i = 1; i < ladder.Length; i++)
        {
            Assert.True(SpeedWindCurve.BodyGainDb(ladder[i]) >= SpeedWindCurve.BodyGainDb(ladder[i - 1]),
                $"gain must not fall from {ladder[i - 1]} to {ladder[i]} m/s");
            Assert.True(SpeedWindCurve.CutoffHz(ladder[i]) >= SpeedWindCurve.CutoffHz(ladder[i - 1]),
                $"cutoff must not fall from {ladder[i - 1]} to {ladder[i]} m/s");
        }
        // And strictly rising across the rungs the ear is asked to tell apart.
        Assert.True(SpeedWindCurve.BodyGainDb(Jog) > SpeedWindCurve.BodyGainDb(Walk));
        Assert.True(SpeedWindCurve.BodyGainDb(Sprint) > SpeedWindCurve.BodyGainDb(Jog));
        Assert.True(SpeedWindCurve.BodyGainDb(Sprint * 1.2f) > SpeedWindCurve.BodyGainDb(Sprint));
        Assert.True(SpeedWindCurve.CutoffHz(Sprint) > SpeedWindCurve.CutoffHz(Jog));
    }

    [Fact]
    public void GainAndCutoffAreMonotonicOnAFineSweep_0_To_12Mps()
    {
        float prevGain = float.NegativeInfinity, prevHz = 0f, prevHiss = float.NegativeInfinity;
        for (float s = 0f; s <= 12f; s += 0.01f)
        {
            float g = SpeedWindCurve.BodyGainDb(s);
            float hz = SpeedWindCurve.CutoffHz(s);
            float h = SpeedWindCurve.HissGainDb(s);
            Assert.True(g >= prevGain, $"gain fell at {s} m/s: {prevGain} → {g}");
            Assert.True(hz >= prevHz, $"cutoff fell at {s} m/s: {prevHz} → {hz}");
            Assert.True(h >= prevHiss, $"hiss fell at {s} m/s: {prevHiss} → {h}");
            prevGain = g;
            prevHz = hz;
            prevHiss = h;
        }
    }

    [Fact]
    public void JogToSprintIsAudibleStep_AtLeast6Db()
    {
        // The playtest question is "can he tell 3.8 from 6.08 by ear". 6 dB is the floor a level
        // step needs to be unmistakable on a continuous sound; the curve ships 10.
        Assert.True(SpeedWindCurve.BodyGainDb(Sprint) - SpeedWindCurve.BodyGainDb(Jog) >= 6f);
        // And the filter opens by more than two octaves over the same rung.
        Assert.True(SpeedWindCurve.CutoffHz(Sprint) / SpeedWindCurve.CutoffHz(Jog) >= 4f);
    }

    [Fact]
    public void HissIsSilentAtJogAndAtSprint_AudibleAbove1p2Sprint()
    {
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.HissGainDb(Jog));
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.HissGainDb(Sprint));
        float above = SpeedWindCurve.HissGainDb(Sprint * 1.2f);
        Assert.True(above > SfxLab.SilentDb + 40f, $"hiss at 1.2×sprint must be audible, got {above} dB");
        Assert.True(above <= SpeedWindCurve.HissFullDb);
    }

    [Fact]
    public void CurveIsFiniteAndSilentOnGarbageInput()
    {
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.BodyGainDb(float.NaN));
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.HissGainDb(float.PositiveInfinity));
        Assert.True(float.IsFinite(SpeedWindCurve.CutoffHz(float.NaN)));
        Assert.Equal(SpeedWindCurve.FullDb, SpeedWindCurve.BodyGainDb(1000f));
        Assert.Equal(SpeedWindCurve.FullCutoffHz, SpeedWindCurve.CutoffHz(1000f));
    }

    [Fact]
    public void DbRoundTripsAndSilentIsExactlyZero()
    {
        Assert.Equal(0f, SpeedWindCurve.DbToAmp(SfxLab.SilentDb));
        Assert.Equal(SfxLab.SilentDb, SpeedWindCurve.AmpToDb(0f));
        Assert.Equal((double)1f, (double)SpeedWindCurve.DbToAmp(0f), 5);
        Assert.Equal((double)-6f, (double)SpeedWindCurve.AmpToDb(SpeedWindCurve.DbToAmp(-6f)), 3);
    }

    [Fact]
    public void HorizontalSpeedIgnoresVertical()
    {
        Assert.Equal((double)5f, (double)SpeedWindCurve.HorizontalSpeed(new Vector3(3f, -17f, 4f)), 5);
        Assert.Equal((double)5f, (double)SpeedWindCurve.HorizontalSpeed(new Vector3(3f, 0f, 4f)), 5);
        Assert.Equal(0f, SpeedWindCurve.HorizontalSpeed(new Vector3(0f, 9f, 0f)));
    }
}

public class SpeedWindStreamTests
{
    private const float Dt = 1f / 60f; // the motor's physics tick

    private static float Sprint => SpeedWindCurve.SprintMps;

    private static SpeedWindStream SettledAt(float speedMps, float seconds = 5f)
    {
        var s = new SpeedWindStream();
        var v = new Vector3(speedMps, 0f, 0f);
        for (float t = 0f; t < seconds; t += Dt)
            s.Step(v, Dt);
        return s;
    }

    [Fact]
    public void SettledOutputMatchesTheCurve()
    {
        SpeedWindStream s = SettledAt(Sprint);
        Assert.Equal((double)SpeedWindCurve.BodyGainDb(Sprint), (double)s.BodyDb, 2);
        Assert.Equal((double)SpeedWindCurve.CutoffHz(Sprint), (double)s.CutoffHz, 0);
        Assert.Equal(SfxLab.SilentDb, s.HissDb);
    }

    [Fact]
    public void StepSprintToZero_ReachesMinus12DbRelative_Within100Ms()
    {
        SpeedWindStream s = SettledAt(Sprint);
        float before = s.BodyDb;
        float t = 0f;
        while (t < SpeedWindStream.CutSeconds + 1e-4f)
        {
            s.Step(Vector3.Zero, Dt);
            t += Dt;
            if (s.BodyDb <= before - 12f)
                return; // the cut: −12 dB relative inside the stated attack
        }
        Assert.Fail($"only fell {before - s.BodyDb:F1} dB in {SpeedWindStream.CutSeconds} s");
    }

    [Fact]
    public void StepSprintToZero_LandsOnExactSilence_Within200Ms()
    {
        // Not merely quiet: the cut ends ON SilentDb, so the wind is gone rather than lurking
        // at −70 dB. Twice the cut time is the generous bound; from −6 dB it is ~0.05 s.
        SpeedWindStream s = SettledAt(Sprint);
        for (float t = 0f; t < 0.2f; t += Dt)
            s.Step(Vector3.Zero, Dt);
        Assert.Equal(SfxLab.SilentDb, s.BodyDb);
    }

    [Fact]
    public void StepZeroToSprint_TakesAtLeast300Ms_ToReachMinus3DbOfTarget()
    {
        var s = new SpeedWindStream();
        var v = new Vector3(0f, 0f, Sprint);
        float target = SpeedWindCurve.BodyGainDb(Sprint);
        float t = 0f;
        while (s.BodyDb < target - 3f)
        {
            s.Step(v, Dt);
            t += Dt;
            Assert.True(t < 5f, "the rise never arrived");
        }
        Assert.True(t >= SpeedWindStream.RiseToMinus3DbSeconds,
            $"reached −3 dB of target after {t:F3} s; the wind must take at least "
            + $"{SpeedWindStream.RiseToMinus3DbSeconds} s to build");
        // And it does arrive — a rise that never opens is not a speedometer.
        Assert.True(t < 1.0f, $"took {t:F3} s to come within 3 dB; that is a fade, not a build");
    }

    [Fact]
    public void RiseIsSlowerThanCut_TheAsymmetryIsTheDesign()
    {
        // From silence to the sprint level, then back: down is faster than up by a wide margin.
        var s = new SpeedWindStream();
        var v = new Vector3(Sprint, 0f, 0f);
        float target = SpeedWindCurve.BodyGainDb(Sprint);
        float up = 0f;
        while (s.BodyDb < target - 1f) { s.Step(v, Dt); up += Dt; }
        float down = 0f;
        while (s.BodyDb > SfxLab.SilentDb) { s.Step(Vector3.Zero, Dt); down += Dt; }
        Assert.True(down * 3f < up, $"down {down:F3} s should be far quicker than up {up:F3} s");
    }

    [Fact]
    public void LandingAtConstantHorizontalSpeed_LeavesOutputUnchangedWithin0p1Db()
    {
        // Airborne (falling at 17 m/s, the harness's calibrated drop) → grounded (vY = 0) with the
        // same horizontal velocity: the wind must not notice. Body gain, hiss gain and cutoff.
        SpeedWindStream s = SettledAt(Sprint * 0.9f);
        var airborne = new Vector3(Sprint * 0.9f, -17f, 0f);
        var grounded = new Vector3(Sprint * 0.9f, 0f, 0f);
        for (int i = 0; i < 30; i++)
            s.Step(airborne, Dt);
        float bodyBefore = s.BodyDb, hissBefore = s.HissDb, hzBefore = s.CutoffHz;
        for (int i = 0; i < 30; i++)
        {
            s.Step(grounded, Dt);
            Assert.True(MathF.Abs(s.BodyDb - bodyBefore) <= 0.1f, $"body moved {s.BodyDb - bodyBefore} dB on landing");
            Assert.True(MathF.Abs(s.HissDb - hissBefore) <= 0.1f, "hiss moved on landing");
            Assert.True(MathF.Abs(s.CutoffHz - hzBefore) <= hzBefore * 0.01f, "cutoff moved on landing");
        }
    }

    [Fact]
    public void JumpAtSpeed_AirborneTicksDoNotDuckTheWind()
    {
        // The other half of "landing costs nothing": leaving the ground at speed does not cut it.
        SpeedWindStream s = SettledAt(Sprint);
        float before = s.BodyDb;
        for (int i = 0; i < 40; i++)
        {
            s.Step(new Vector3(Sprint, 8f - 24f * i * Dt, 0f), Dt); // a jump arc under 24 m/s² gravity
            Assert.True(MathF.Abs(s.BodyDb - before) <= 0.1f, $"wind moved {s.BodyDb - before} dB mid-air");
        }
    }

    [Fact]
    public void CutoffNeverJumpsMoreThanTheStatedOctavesPerTick()
    {
        // A cut from full to stopped asks the cutoff to fall 5.2 octaves at once; it is rate
        // limited so no buffer sees a click-sized coefficient jump.
        SpeedWindStream s = SettledAt(Sprint * 1.2f);
        float prev = s.CutoffHz;
        for (int i = 0; i < 120; i++)
        {
            s.Step(Vector3.Zero, Dt);
            float oct = MathF.Abs(MathF.Log2(s.CutoffHz / prev));
            Assert.True(oct <= SpeedWindStream.MaxOctavesPerSecond * Dt + 1e-4f, $"cutoff jumped {oct} octaves in one tick");
            prev = s.CutoffHz;
        }
        Assert.Equal((double)SpeedWindCurve.WalkCutoffHz, (double)s.CutoffHz, 0);
    }

    [Fact]
    public void SuppressFadesToExactSilenceAndReleaseBuildsBack()
    {
        SpeedWindStream s = SettledAt(Sprint);
        s.SetSuppressed(true);
        var v = new Vector3(Sprint, 0f, 0f);
        for (float t = 0f; t < SpeedWindStream.SuppressSeconds + 0.1f; t += Dt)
            s.Step(v, Dt);
        Assert.Equal(SfxLab.SilentDb, s.BodyDb);
        Assert.True(s.Suppressed);

        s.SetSuppressed(false);
        for (float t = 0f; t < 3f; t += Dt)
            s.Step(v, Dt);
        Assert.Equal((double)SpeedWindCurve.BodyGainDb(Sprint), (double)s.BodyDb, 1);
    }

    [Fact]
    public void ZeroOrNegativeDtIsANoOp()
    {
        SpeedWindStream s = SettledAt(Sprint);
        float before = s.BodyDb;
        s.Step(Vector3.Zero, 0f);
        s.Step(Vector3.Zero, -1f);
        s.Step(Vector3.Zero, float.NaN);
        Assert.Equal(before, s.BodyDb);
    }
}

public class SpeedWindSynthTests
{
    [Fact]
    public void BodyLoopIsTheStatedLengthNormalisedAndDeterministic()
    {
        float[] a = SpeedWindSynth.RenderBody();
        float[] b = SpeedWindSynth.RenderBody();
        Assert.Equal((int)(SpeedWindSynth.SampleRate * SpeedWindSynth.BodySeconds), a.Length);
        Assert.Equal(a, b);
        float peak = 0f;
        foreach (float x in a) peak = MathF.Max(peak, MathF.Abs(x));
        Assert.Equal((double)SpeedWindSynth.BodyPeak, (double)peak, 3);
    }

    [Fact]
    public void HissLoopIsBandLimitedAbove3kHz_AndTheBodyIsNot()
    {
        // The hiss is the air on top; the body is the broadband source the bus filter opens onto.
        float[] hiss = SpeedWindSynth.RenderHiss();
        float[] body = SpeedWindSynth.RenderBody();
        double hissLow = Spectrum.BandRejectionDb(hiss, SpeedWindSynth.SampleRate, 100, 800);
        double bodyLow = Spectrum.BandRejectionDb(body, SpeedWindSynth.SampleRate, 100, 800);
        Assert.True(hissLow > 20, $"hiss should reject 100–800 Hz by >20 dB, got {hissLow:F1}");
        Assert.True(bodyLow < 10, $"body should keep its low band, rejection {bodyLow:F1} dB");
    }

    [Fact]
    public void LoopWrapIsNotAClick()
    {
        // The splice's promise: the wrap discontinuity is no bigger than an ordinary adjacent-
        // sample delta. Measure the largest step inside the buffer and compare the wrap to it.
        foreach (float[] loop in new[] { SpeedWindSynth.RenderBody(), SpeedWindSynth.RenderHiss() })
        {
            float maxInner = 0f;
            for (int i = 1; i < loop.Length; i++)
                maxInner = MathF.Max(maxInner, MathF.Abs(loop[i] - loop[i - 1]));
            float wrap = MathF.Abs(loop[0] - loop[^1]);
            Assert.True(wrap <= maxInner, $"wrap step {wrap} exceeds the largest inner step {maxInner}");
        }
    }
}
