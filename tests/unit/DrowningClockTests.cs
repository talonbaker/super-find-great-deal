using Godot;
using Sail.Game.Run;
using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// <b>WATER-3: the drowning clock, to the boundary.</b>
///
/// <para>Talon, 2026-08-29: <i>"the water should kill the player after about three seconds, and
/// this does not happen."</i> He was right about the cause too — <c>RespawnService.ServerKill</c>
/// existed with zero callers and <c>RespawnCause</c> had no <c>Drowned</c>. This proves the timer
/// that now calls it; <c>BubbleTestSelfTest</c>'s boundary probe proves the same rule end to end
/// with a real body in the real lake.</para>
///
/// <para>The interesting failures here are all arithmetic: a clock that spends a nominal scan
/// period instead of the real one, a partial breath that half-refills a meter, a respawned body
/// killed on its first scan by a timer nobody cleared, and a peer's clock leaking into another
/// peer's. Each has its own test.</para>
/// </summary>
public class DrowningClockTests
{
    private const float Under = 3.0f;   // WaterGeometry.DrownAfterSec, restated so a drift is a red

    private static readonly WaterGeometry.LakeFootprint Green = WaterGeometry.BubbleTestLake;

    private static Vector3 At(float x, float y, float z) => new(x, y, z);

    private static Vector3 LakeAt(float y) =>
        At(WaterGeometry.BubbleTestLakeCentreX, y, WaterGeometry.BubbleTestLakeCentreZ);

    // --- The number itself ---------------------------------------------------------------------

    /// <summary>Talon said "about three seconds"; the packet took 3.0 exactly. If this changes it
    /// is a design decision, not a tuning drift.</summary>
    [Fact]
    public void TheContractIsThreeSeconds()
    {
        Assert.Equal(Under, WaterGeometry.DrownAfterSec);
        Assert.Equal(Under, new DrowningClock().DrownAfterSec);
    }

    /// <summary><b>Submerged is derived from Swimming, never typed twice.</b> If these two ever
    /// disagree there is a band of depth where the state machine says Swimming and the drowning
    /// clock says dry — a body visibly under water that never drowns, or the reverse.</summary>
    [Fact]
    public void SubmergedIsTheSwimmingThreshold()
    {
        Assert.Equal(WaterGeometry.SwimDepthM + WaterGeometry.HysteresisM,
                     WaterGeometry.SubmergedDepthM);
        // ...and the swim line really does sit under it, or a swimmer would never drown at all.
        Assert.True(WaterGeometry.SwimSubmersionM > WaterGeometry.SubmergedDepthM);
    }

    // --- Accumulation and the boundary ----------------------------------------------------------

    [Fact]
    public void AFreshClockIsAtZeroAndNobodyIsUnder()
    {
        var c = new DrowningClock();
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.False(c.IsUnder(1));
        Assert.Equal(0, c.UnderCount);
        Assert.Equal(0, c.Drownings);
    }

    /// <summary>The boundary, stated (MECHANICS-BIBLE §1): the kill fires at exactly
    /// <c>&gt;= DrownAfterSec</c>. 2.9 s is alive; the tick that reaches 3.0 kills.</summary>
    [Fact]
    public void ExactlyThreeSecondsKills_AndAHairUnderDoesNot()
    {
        var c = new DrowningClock();
        Assert.False(c.Tick(1, submerged: true, 2.999f));
        Assert.Equal(2.999f, c.SubmergedSecOf(1), precision: 3);
        Assert.True(c.Tick(1, submerged: true, 0.01f));
        Assert.Equal(1, c.Drownings);

        // ...and the window itself, in one tick, is inclusive.
        var d = new DrowningClock();
        Assert.True(d.Tick(1, submerged: true, Under));
    }

    /// <summary>A single tick longer than the whole window still kills exactly once — a frame spike
    /// is not a way to survive, and it is not a way to die twice.</summary>
    [Fact]
    public void OneEnormousTickKillsOnce()
    {
        var c = new DrowningClock();
        Assert.True(c.Tick(1, submerged: true, 30f));
        Assert.Equal(1, c.Drownings);
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.False(c.IsUnder(1));
    }

    /// <summary><b>The clock spends real time, not nominal scan periods.</b> Thirty 0.1 s scans
    /// reach the threshold on the thirtieth and not before, which is the 10 Hz path
    /// <c>RespawnService</c> actually runs.</summary>
    [Fact]
    public void ThirtyScansAtTenHertzKillOnTheThirtieth()
    {
        var c = new DrowningClock();
        for (int i = 1; i < 30; i++)
            Assert.False(c.Tick(1, submerged: true, 0.1f));
        Assert.True(c.Tick(1, submerged: true, 0.1f));
    }

    /// <summary>The same, with the ragged deltas a loaded frame produces. The total is what decides,
    /// so an uneven scan cadence must not stretch "about three seconds" into four.</summary>
    [Fact]
    public void RaggedDeltasStillTotalThreeSeconds()
    {
        var c = new DrowningClock();
        float[] deltas = { 0.10f, 0.13f, 0.09f, 0.42f, 0.11f, 0.10f, 0.28f, 0.17f, 0.10f, 0.10f };
        float spent = 0f;
        foreach (float dt in deltas)
        {
            spent += dt;
            Assert.False(c.Tick(1, submerged: true, dt), $"killed early at {spent} s");
        }
        Assert.Equal(1.60f, spent, precision: 4);
        Assert.False(c.Tick(1, submerged: true, 1.3f));   // 2.9 s under: still alive
        Assert.True(c.Tick(1, submerged: true, 0.2f));    // 3.1 s: the tick that crosses
    }

    // --- Reset, not decay -------------------------------------------------------------------------

    /// <summary>Surfacing clears the clock outright. Two seconds under, one dry tick, two more
    /// seconds under: still alive, because the second breath started from zero.</summary>
    [Fact]
    public void SurfacingResetsTheClockOutright()
    {
        var c = new DrowningClock();
        c.Tick(1, submerged: true, 2.0f);
        Assert.Equal(2.0f, c.SubmergedSecOf(1), precision: 4);

        Assert.False(c.Tick(1, submerged: false, 0.1f));
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.False(c.IsUnder(1));

        Assert.False(c.Tick(1, submerged: true, 2.0f));
        Assert.Equal(0, c.Drownings);
    }

    /// <summary>Even the shortest possible surfacing counts — one dry tick is a breath. Stated as
    /// its own test because "clear on any dry tick" and "clear after N dry ticks" look identical
    /// until someone adds a grace period nobody asked for.</summary>
    [Fact]
    public void OneDryTickIsEnoughOfABreath()
    {
        var c = new DrowningClock();
        c.Tick(1, submerged: true, Under - 0.01f);
        c.Tick(1, submerged: false, 0.001f);
        Assert.False(c.Tick(1, submerged: true, Under - 0.01f));
        Assert.Equal(0, c.Drownings);
    }

    /// <summary>After a kill the clock is cleared, so the next scan starts a fresh breath. Without
    /// this the respawned body would die again on its first tick back in the world, forever.
    /// </summary>
    [Fact]
    public void AKillClearsTheClock()
    {
        var c = new DrowningClock();
        Assert.True(c.Tick(1, submerged: true, Under));
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.False(c.Tick(1, submerged: true, 0.1f));
        Assert.Equal(1, c.Drownings);
    }

    /// <summary><c>Clear</c> is what <c>RespawnService.ServerKill</c> calls for EVERY cause — a
    /// body that walked off the edge while submerged must not respawn already drowning.</summary>
    [Fact]
    public void ClearForgetsOnePeerAndClearAllForgetsEveryone()
    {
        var c = new DrowningClock();
        c.Tick(1, submerged: true, 2.0f);
        c.Tick(2, submerged: true, 2.0f);
        c.Clear(1);
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.Equal(2.0f, c.SubmergedSecOf(2), precision: 4);
        c.ClearAll();
        Assert.Equal(0, c.UnderCount);
    }

    // --- Isolation and totality -------------------------------------------------------------------

    /// <summary>Six players can be in the lake at once; one player's breath is not another's.</summary>
    [Fact]
    public void PeersAreIndependent()
    {
        var c = new DrowningClock();
        for (int i = 1; i <= 6; i++)
            c.Tick(i, submerged: true, 0.2f * i);         // 0.2 .. 1.2 s — nobody near the window
        for (int i = 1; i <= 6; i++)
            Assert.False(c.Tick(i, submerged: true, 0f)); // and nobody drowns on a zero tick
        for (int i = 1; i <= 6; i++)
            Assert.Equal(0.2f * i, c.SubmergedSecOf(i), precision: 4);

        Assert.True(c.Tick(6, submerged: true, Under));   // peer 6 drowns
        Assert.Equal(0f, c.SubmergedSecOf(6));
        Assert.Equal(1.0f, c.SubmergedSecOf(5), precision: 4);   // and nobody else moved
        Assert.Equal(5, c.UnderCount);
    }

    /// <summary>Only submerged peers hold an entry, so the dictionary tracks who is in the water
    /// rather than everyone who ever has been.</summary>
    [Fact]
    public void TheClockOnlyRemembersPeopleWhoAreUnder()
    {
        var c = new DrowningClock();
        c.Tick(1, submerged: true, 0.2f);
        c.Tick(2, submerged: false, 0.2f);
        Assert.Equal(1, c.UnderCount);
        c.Tick(1, submerged: false, 0.2f);
        Assert.Equal(0, c.UnderCount);
    }

    /// <summary>A non-finite or non-positive delta advances nothing. A paused clock, a rewound one
    /// and a NaN frame must none of them be able to kill.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ADegenerateDeltaAdvancesNothing(float dt)
    {
        var c = new DrowningClock();
        Assert.False(c.Tick(1, submerged: true, dt));
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.Equal(0, c.Drownings);
    }

    /// <summary>Drowning off. Not a second flag: the same knob a world sets to 3.0 sets to 0 to opt
    /// out, and turning it off clears whatever was already running.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ANonPositiveWindowDisablesDrowningAndClearsTheClock(float window)
    {
        var c = new DrowningClock();
        c.Tick(1, submerged: true, 2.0f);
        c.DrownAfterSec = window;
        Assert.False(c.Tick(1, submerged: true, 100f));
        Assert.Equal(0f, c.SubmergedSecOf(1));
        Assert.Equal(0, c.Drownings);
    }

    // --- Position to death, in one unit ------------------------------------------------------------

    /// <summary>The whole rule from a world position: a body at the swim line in the bubble test's
    /// lake drowns after exactly the window.</summary>
    [Fact]
    public void ABodyAtTheSwimLineDrownsAfterTheWindow()
    {
        var c = new DrowningClock();
        Assert.False(c.Tick(1, LakeAt(WaterGeometry.SwimLineY), Green, Under - 0.01f));
        Assert.True(c.Tick(1, LakeAt(WaterGeometry.SwimLineY), Green, 0.01f));
    }

    /// <summary><b>Wading never starts the clock</b>, however long you stand there — the shallows
    /// are the day-register comedy the water contract's §2 designed them to be, not a kill plane.
    /// Sampled right up to the last depth that is still Wading.</summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.2f)]
    [InlineData(0.6f)]
    [InlineData(1.0f)]
    [InlineData(1.1f)]
    [InlineData(1.149f)]
    public void WadingNeverDrowns(float depth)
    {
        var c = new DrowningClock();
        Vector3 feet = LakeAt(WaterGeometry.WaterY - depth);
        Assert.Equal(depth, WaterGeometry.DepthAt(feet, Green), precision: 3);
        for (int i = 0; i < 600; i++)
            Assert.False(c.Tick(1, feet, Green, 0.1f));
        Assert.Equal(0, c.Drownings);
    }

    /// <summary>And the first depth past the threshold does drown — so the boundary between the two
    /// is a line, not a gap.</summary>
    [Fact]
    public void TheSubmergedThresholdItselfDrowns()
    {
        var c = new DrowningClock();
        Vector3 feet = LakeAt(WaterGeometry.WaterY - WaterGeometry.SubmergedDepthM);
        Assert.True(c.Tick(1, feet, Green, Under));
    }

    /// <summary>
    /// <b>The two notes, together, as one test — and the reason they are one packet.</b> A body far
    /// west and far below the bubble test is NOT in water, so no length of time there drowns it: it
    /// falls, and <c>RespawnService</c>'s boundary scan claims it as OffTheEdge or Void. Against the
    /// old half-plane this test fails, which is precisely the worse bug that wiring note 2 without
    /// note 3 would have shipped.
    /// </summary>
    [Theory]
    [InlineData(-160f, -20f, 0f)]
    [InlineData(-200f, -55f, 0f)]
    [InlineData(-95f, -20f, 60f)]
    [InlineData(-95f, -400f, 0f)]
    public void OffTheMapNeverDrowns_HoweverLongYouFall(float x, float y, float z)
    {
        var c = new DrowningClock();
        for (int i = 0; i < 600; i++)
            Assert.False(c.Tick(1, At(x, y, z), Green, 0.1f));
        Assert.Equal(0, c.Drownings);
        Assert.Equal(0f, c.SubmergedSecOf(1));
    }
}
