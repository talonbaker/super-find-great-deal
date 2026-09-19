using System;
using Godot;
using MpFoundation.Dev.Playground;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The zone-6 heightfield port's field function, pinned</b> (BIKE-2x-L, 2026-09-02).
/// <see cref="RollingCourse.HeightAt"/> is pure and engine-free, so the three lessons the source
/// paid for — a border that meets the plate instead of walling it off, a field that never dips
/// under the plate, and gradients under the slide-off limit — are asserted here as arithmetic
/// rather than re-discovered in a headed session.
///
/// <para>Sampled on a grid twice as fine as the mesh's own (121 x 121 against the mesh's 61 x 61),
/// so nothing these tests pass can hide between the vertices the mesh actually places.</para>
/// </summary>
public sealed class RollingCourseTests
{
    private const float Half = RollingCourse.SizeM * 0.5f;

    /// <summary>Twice the mesh resolution per side.</summary>
    private const int N = 120;

    private static float At(int i, int j)
        => RollingCourse.HeightAt((i / (float)N - 0.5f) * RollingCourse.SizeM,
                                  (j / (float)N - 0.5f) * RollingCourse.SizeM);

    /// <summary><b>The border ring is exactly zero</b> — the taper's smoothstep reaches 0 at the
    /// patch edge, which is what lets the field meet the base plate as ground instead of as the
    /// 2 m wall the source's first version fenced itself off with.</summary>
    [Fact]
    public void TheBorderMeetsThePlateAtZero()
    {
        for (int i = 0; i <= N; i++)
        {
            float t = (i / (float)N - 0.5f) * RollingCourse.SizeM;
            Assert.True(MathF.Abs(RollingCourse.HeightAt(t, -Half)) < 1e-4f,
                $"border height at (x={t:F1}, z={-Half}) is {RollingCourse.HeightAt(t, -Half)}");
            Assert.True(MathF.Abs(RollingCourse.HeightAt(t, Half)) < 1e-4f);
            Assert.True(MathF.Abs(RollingCourse.HeightAt(-Half, t)) < 1e-4f);
            Assert.True(MathF.Abs(RollingCourse.HeightAt(Half, t)) < 1e-4f);
        }
    }

    /// <summary><b>No sample anywhere dips below the plate.</b> The source's raw octaves swing to
    /// about -2 m and only the lift keeps the troughs above ground; a re-tune that shrank the lift
    /// without re-checking would bury them again, and this is the check that refuses it.</summary>
    [Fact]
    public void TheFieldNeverDipsBelowThePlate()
    {
        float worst = float.MaxValue;
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                worst = MathF.Min(worst, At(i, j));
        Assert.True(worst >= 0f, $"lowest sample {worst:F4} m is below the plate");
    }

    /// <summary><b>There is a field to ride</b> — the interior actually rises to hill scale. The
    /// bound is deliberately loose (2..9 m): this pins "rolling terrain exists", not a look.</summary>
    [Fact]
    public void TheInteriorRisesToHillScale()
    {
        float peak = 0f;
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                peak = MathF.Max(peak, At(i, j));
        Assert.InRange(peak, 2f, 9f);
    }

    /// <summary><b>The steepest gradient stays under the slide-off limit.</b> The source's rim hit
    /// ~47 degrees and the body slid off it, dropped to the plate and walked under the world. The
    /// port halves the source's gradients by construction (rise x1.5 over run x3); this asserts
    /// the result stays under 35 degrees everywhere, sampled at half the mesh's own cell — so the
    /// whole patch, rim included, is ground a bike can hold.</summary>
    [Fact]
    public void TheSteepestGradientStaysRideable()
    {
        float cell = RollingCourse.SizeM / N;
        float worst = 0f;
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                float dx = MathF.Abs(At(i + 1, j) - At(i, j)) / cell;
                float dz = MathF.Abs(At(i, j + 1) - At(i, j)) / cell;
                worst = MathF.Max(worst, MathF.Max(dx, dz));
            }
        }
        float limit = MathF.Tan(35f * MathF.PI / 180f);
        Assert.True(worst < limit,
            $"steepest sampled gradient {MathF.Atan(worst) * 180f / MathF.PI:F1} deg "
            + $"(slope {worst:F3}) is over the 35 deg rideability bound");
    }

    /// <summary>The spawn row stands on the patch — inside the border, on ground the field keeps
    /// low but real. Asserted from the constants rather than by constructing the Node: a Godot
    /// Node built in an engine-free xUnit host is the <c>MotorTuning.TryApply</c> segfault lesson
    /// wearing a different type.</summary>
    [Fact]
    public void TheSpawnStandsOnThePatch()
    {
        Assert.True(MathF.Abs(RollingCourse.SpawnZ) < Half);
        float ground = RollingCourse.HeightAt(0f, RollingCourse.SpawnZ);
        Assert.InRange(ground, 0f, 3f);
    }
}
