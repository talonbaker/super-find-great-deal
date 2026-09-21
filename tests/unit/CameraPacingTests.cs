using Godot;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// SICK-1 (2026-09-20). The engine-free half of the first-person lens's frame pacing: where the
/// eye belongs on a render frame between two physics ticks, and what a recorded run measured.
/// </summary>
public class CameraPacingTests
{
    /// <summary>xUnit 2.4.2 cannot choose between Equal(double,double,int) and
    /// Equal(float,float,float) when both arguments are float, so every approximate comparison
    /// in this file goes through one double-typed helper rather than through a cast at each
    /// site.</summary>
    private static void Near(float expected, float actual, int digits) =>
        Assert.Equal((double)expected, (double)actual, digits);

    // --- CameraPacing.EyePosition -------------------------------------------------------------

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 0.025f)]
    [InlineData(0.5f, 0.05f)]
    [InlineData(1f, 0.1f)]
    public void EyePosition_WalksTheFractionBetweenTheTwoTicks(float fraction, float expectedZ)
    {
        // One sprint tick: 3.8 m/s x 1.6 / 60 Hz = 0.101 m. Rounded to 0.1 for legibility.
        Vector3 got = CameraPacing.EyePosition(Vector3.Zero, new Vector3(0f, 0f, 0.1f), fraction);
        Near(expectedZ, got.Z, 5);
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(1.5f, 0.1f)]
    public void EyePosition_ClampsRatherThanExtrapolating(float fraction, float expectedZ)
    {
        // A frame taken during a physics catch-up can report a fraction outside [0,1], and an
        // extrapolated eye is a camera standing somewhere the body has never been.
        Vector3 got = CameraPacing.EyePosition(Vector3.Zero, new Vector3(0f, 0f, 0.1f), fraction);
        Near(expectedZ, got.Z, 5);
    }

    [Fact]
    public void EyePosition_SnapsRatherThanSweepingAcrossATeleport()
    {
        // RoomTeleport moves a player between rooms tens of metres apart. Lerping that would
        // fly the lens through the wall over one frame.
        var from = new Vector3(0f, 1f, 0f);
        var to = new Vector3(0f, 1f, 40f);
        Assert.Equal(to, CameraPacing.EyePosition(from, to, 0.5f));
    }

    [Fact]
    public void EyePosition_TheSnapThresholdIsWellClearOfTheFastestRealStep()
    {
        // The positive control for the test above: the fastest legitimate step must NOT snap,
        // or interpolation would silently switch itself off at a sprint. 0.101 m is the
        // sprint tick; the threshold is 0.75 m.
        var from = Vector3.Zero;
        var to = new Vector3(0f, 0f, 0.101f);
        Vector3 got = CameraPacing.EyePosition(from, to, 0.5f);
        Near(0.0505f, got.Z, 5);
        Assert.True(CameraPacing.TeleportSnapM > 7f * 0.101f,
            "the snap threshold must leave room for a fall on top of a sprint");
    }

    [Fact]
    public void EyePosition_AZeroThresholdDisablesInterpolationEntirely()
    {
        Vector3 got = CameraPacing.EyePosition(Vector3.Zero, new Vector3(0f, 0f, 0.1f), 0.5f,
            teleportSnapM: 0f);
        Near(0.1f, got.Z, 5);
    }

    // --- CameraPacing.Summarize ---------------------------------------------------------------

    [Fact]
    public void Summarize_EmptyRunIsTheDefaultRowRatherThanAThrow()
    {
        CameraPacing.Summary s = CameraPacing.Summarize(
            System.Array.Empty<float>(), System.Array.Empty<float>(), System.Array.Empty<float>());
        Assert.Equal(0, s.Frames);
    }

    [Fact]
    public void Summarize_PercentilesAreNearestRank_SoEveryOneIsAFrameThatHappened()
    {
        float[] ms = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f };
        float[] zero = new float[10];
        CameraPacing.Summary s = CameraPacing.Summarize(ms, zero, zero);
        Assert.Equal(10, s.Frames);
        Near(5f, s.P50Ms, 4);   // ceil(0.50 x 10) = 5th value
        Near(10f, s.P95Ms, 4);  // ceil(0.95 x 10) = 10th value
        Near(10f, s.MaxMs, 4);
    }

    [Fact]
    public void Summarize_CountsSlowFramesStrictlyOverTheBudget()
    {
        float[] ms = { 19.9f, 20f, 20.1f, 50f };
        float[] zero = new float[4];
        Assert.Equal(2, CameraPacing.Summarize(ms, zero, zero).OverBudget);
    }

    [Fact]
    public void Summarize_TheZeroStepShareIsThePhysicsStepTell()
    {
        // A 60 Hz body rendered at 144 Hz while walking: 2.4 render frames per physics tick, so
        // the eye moves on 60 of every 144 frames and holds still on the rest. This is the
        // number the whole measurement turns on, so it is pinned on a hand-built pattern.
        var frameMs = new float[12];
        var stepM = new float[12];
        var yaw = new float[12];
        for (int i = 0; i < 12; i++)
        {
            frameMs[i] = 6.94f;
            // 5 moving frames in 12 = 60/144.
            stepM[i] = i % 12 is 0 or 2 or 5 or 7 or 10 ? 0.0633f : 0f;
            yaw[i] = 0.5f;
        }
        CameraPacing.Summary s = CameraPacing.Summarize(frameMs, stepM, yaw);
        Near(7f / 12f, s.ZeroStepFraction, 4);
        Assert.True(s.StepStdDevM > 0.5f * s.MeanStepM,
            "an alternating hold/jump pattern must report a step spread of the same order as its mean");
    }

    [Fact]
    public void Summarize_APerfectlySmoothRunHasNoZeroStepsAndNoSpread()
    {
        // The same total distance as the row above, spread evenly over all 12 frames: this is
        // what interpolation is supposed to turn that pattern into.
        var frameMs = new float[12];
        var stepM = new float[12];
        var yaw = new float[12];
        for (int i = 0; i < 12; i++)
        {
            frameMs[i] = 6.94f;
            stepM[i] = 5f * 0.0633f / 12f;
            yaw[i] = 0.5f;
        }
        CameraPacing.Summary s = CameraPacing.Summarize(frameMs, stepM, yaw);
        Near(0f, s.ZeroStepFraction, 5);
        Near(0f, s.StepStdDevM, 5);
        Near(0f, s.YawStepStdDevDeg, 5);
    }

    [Fact]
    public void Summarize_MeanStepIsPreservedBySmoothing_SoTheTwoRowsAreComparable()
    {
        // Interpolation must not change how far the player went, only how the distance is
        // distributed over frames. If the mean moved, the two table rows would be measuring
        // two different walks and the comparison would say nothing.
        var juddered = new float[12];
        var smooth = new float[12];
        for (int i = 0; i < 12; i++)
        {
            juddered[i] = i % 12 is 0 or 2 or 5 or 7 or 10 ? 0.0633f : 0f;
            smooth[i] = 5f * 0.0633f / 12f;
        }
        var frameMs = new float[12];
        CameraPacing.Summary a = CameraPacing.Summarize(frameMs, juddered, frameMs);
        CameraPacing.Summary b = CameraPacing.Summarize(frameMs, smooth, frameMs);
        Near(a.MeanStepM, b.MeanStepM, 6);
    }

    // --- The FOV arithmetic -------------------------------------------------------------------

    [Theory]
    // 16:9 window (project.godot's 1280x720) and Talon's 3440x1440 fullscreen.
    [InlineData(75f, 16f / 9f, 107.5f)]
    [InlineData(90f, 16f / 9f, 121.3f)]
    [InlineData(75f, 3440f / 1440f, 122.8f)]
    [InlineData(90f, 3440f / 1440f, 134.6f)]
    public void HorizontalFov_IsWhatAPlayerMeansByFov(float verticalDeg, float aspect, float expectedHorizontalDeg)
    {
        float got = Mathf.RadToDeg(FirstPersonCamera.HorizontalFovRad(verticalDeg, aspect));
        Near(expectedHorizontalDeg, got, 1);
    }

    [Fact]
    public void HorizontalFov_TheShippedDefaultIsAlreadyWide()
    {
        // The packet's premise was that 75 is "narrow on a 16:9 desktop monitor". It is the
        // VERTICAL angle: on 16:9 it is 107 degrees horizontal, wider than the 90-103 most
        // first-person games ship. This test is the arithmetic that says so, pinned so the
        // claim in the handoff cannot rot.
        float h = Mathf.RadToDeg(FirstPersonCamera.HorizontalFovRad(FirstPersonCamera.DefaultFovDeg, 16f / 9f));
        Assert.InRange(h, 105f, 109f);
    }
}
