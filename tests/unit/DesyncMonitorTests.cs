using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

public class DesyncMonitorTests
{
    [Fact]
    public void OneOffLargeCorrection_IsNotReported()
    {
        var m = new DesyncMonitor(thresholdM: 1.0f, sustainedCount: 10);
        Assert.False(m.Observe(5.0f)); // a single large correction is normal (loss/hitch)
        for (int i = 0; i < 20; i++)
            Assert.False(m.Observe(0.01f)); // healthy stream, never fires
    }

    [Fact]
    public void SustainedLargeCorrection_ReportsExactlyOncePerEpisode()
    {
        var m = new DesyncMonitor(thresholdM: 1.0f, sustainedCount: 10);
        int fires = 0;
        for (int i = 0; i < 50; i++) // well past the threshold count
            if (m.Observe(3.0f))
                fires++;
        Assert.Equal(1, fires); // reported once, not every frame
    }

    [Fact]
    public void RecoveryRearmsForTheNextEpisode()
    {
        var m = new DesyncMonitor(thresholdM: 1.0f, sustainedCount: 5);
        for (int i = 0; i < 10; i++) m.Observe(2.0f); // episode 1 (fired inside)
        Assert.False(m.Observe(0.0f));                // recovered -> rearm
        int fires = 0;
        for (int i = 0; i < 10; i++)
            if (m.Observe(2.0f)) fires++;             // episode 2 must be able to fire again
        Assert.Equal(1, fires);
    }

    [Fact]
    public void JustBelowSustainedCount_DoesNotFire()
    {
        var m = new DesyncMonitor(thresholdM: 1.0f, sustainedCount: 10);
        int fires = 0;
        for (int i = 0; i < 9; i++)                   // one short of the threshold
            if (m.Observe(2.0f)) fires++;
        Assert.Equal(0, fires);
    }
}
