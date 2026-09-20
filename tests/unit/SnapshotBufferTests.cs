using Godot;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Remote-avatar snapshot interpolation. Covers the tick high-water-mark that rejects
/// stale/reordered snapshots, the epoch-bump teleport signal, and interpolation
/// monotonicity. The headline case is the named regression below: a late snapshot from a
/// superseded epoch must be dropped by tick, not misread as a fresh teleport.
/// </summary>
public class SnapshotBufferTests
{
    private const double FrameDelta = 1.0 / 60.0; // one sim tick per frame at TickRate=60

    private static NetCodec.Snapshot Snap(uint tick, byte epoch, float x) =>
        new(tick, epoch, 0, new MoveState
        {
            Position = new Vector3(x, 0, 0),
            Velocity = Vector3.Zero, // zero velocity => flat extrapolation, clean monotonicity
            Yaw = 0,
            Grounded = true,
        });

    [Fact]
    public void NoSnapshots_SampleReturnsNull()
    {
        var b = new SnapshotBuffer();
        Assert.False(b.HasSnapshots);
        Assert.Null(b.Sample(FrameDelta));
    }

    [Fact]
    public void StaleAndDuplicateTicks_RejectedByHighWaterMark()
    {
        var b = new SnapshotBuffer();
        b.Add(Snap(10, 0, 10f));
        b.Add(Snap(5, 0, 5f));    // older tick: dropped
        b.Add(Snap(10, 0, 111f)); // duplicate tick: dropped (payload 111 must not win)

        RenderSampleX(b, out float x);
        Assert.Equal(10f, x);     // still the original tick-10 payload
    }

    [Fact]
    public void EpochBump_SignalsTeleportExactlyOnce()
    {
        var b = new SnapshotBuffer();
        b.Add(Snap(10, 0, 10f));
        Assert.False(b.Sample(FrameDelta)!.Value.Teleported); // fresh join is not a teleport

        b.Add(Snap(20, 1, 20f));                              // epoch change: server teleport
        Assert.True(b.Sample(FrameDelta)!.Value.Teleported);
        Assert.False(b.Sample(FrameDelta)!.Value.Teleported); // flag consumed once
    }

    [Fact]
    public void ReorderedStaleEpochSnapshot_IsDropped_NotMisreadAsTeleport()
    {
        // REGRESSION: a snapshot from a superseded epoch that arrives late (after a
        // newer-epoch snapshot) has a lower tick than the high-water mark, so it must be
        // rejected up front. If it slipped through, its mismatched epoch would be read as
        // a brand-new teleport, wiping the correct buffer and snapping the avatar back to
        // the stale trajectory.
        var b = new SnapshotBuffer();
        b.Add(Snap(10, epoch: 0, x: 10f));
        b.Add(Snap(20, epoch: 1, x: 999f));   // legitimate teleport to the new epoch

        var first = b.Sample(FrameDelta);
        Assert.NotNull(first);
        Assert.True(first!.Value.Teleported);  // the real teleport is signaled

        // Now the reordered stale epoch-0 snapshot arrives late (tick 15 < high-water 20).
        b.Add(Snap(15, epoch: 0, x: 15f));

        var after = b.Sample(FrameDelta);
        Assert.NotNull(after);
        Assert.False(after!.Value.Teleported);        // NOT a second, spurious teleport
        Assert.True(after.Value.Position.X > 100f,    // still on the epoch-1 trajectory...
            $"expected epoch-1 position (~999), got X={after.Value.Position.X} " +
            "(stale epoch-0 snapshot was misread as a teleport)");
    }

    [Fact]
    public void Interpolation_IsMonotonic_AcrossAdvancingStream()
    {
        // Feed a live stream whose X strictly increases with tick; sample every frame and
        // require the rendered X never to move backward. Exercises the bracketing lerp,
        // the render-clock slew, and the bounded extrapolation together.
        var b = new SnapshotBuffer();
        float last = float.NegativeInfinity;
        for (uint tick = 0; tick <= 180; tick += 6)
        {
            b.Add(Snap(tick, 0, tick)); // X == tick
            for (int f = 0; f < 6; f++)
            {
                var s = b.Sample(FrameDelta);
                if (s == null) continue;
                Assert.True(s.Value.Position.X >= last - 1e-3f,
                    $"rendered X moved backward: {s.Value.Position.X} < {last}");
                last = s.Value.Position.X;
            }
        }
    }

    [Fact]
    public void Extrapolation_IsBounded_DoesNotRunAwayOnStarvation()
    {
        // One snapshot with non-zero velocity, then starve the buffer: extrapolation must
        // stop advancing after MaxExtrapolateTicks rather than flying off indefinitely.
        var b = new SnapshotBuffer();
        b.Add(new NetCodec.Snapshot(100, 0, 0, new MoveState
        {
            Position = Vector3.Zero,
            Velocity = new Vector3(10f, 0, 0),
            Grounded = false,
        }));

        float maxX = float.NegativeInfinity;
        for (int f = 0; f < 600; f++) // 10s of starvation
        {
            var s = b.Sample(FrameDelta);
            if (s != null) maxX = Mathf.Max(maxX, s.Value.Position.X);
        }
        // MaxExtrapolateTicks=12 at TickDelta=1/60 * 10 units/s => far under a big bound.
        Assert.True(float.IsFinite(maxX));
        Assert.True(maxX < 100f, $"extrapolation ran away: maxX={maxX}");
    }

    private static void RenderSampleX(SnapshotBuffer b, out float x)
    {
        var s = b.Sample(FrameDelta);
        Assert.NotNull(s);
        x = s!.Value.Position.X;
    }
}
