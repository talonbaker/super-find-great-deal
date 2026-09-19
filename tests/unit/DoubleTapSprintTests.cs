using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>Double-tap-to-sprint (BT-7), proved without an engine.</b> Talon, 2026-08-27:
/// <i>"sprint triggers via holding Shift OR double-tapping a directional/WASD key."</i>
///
/// <para><b>Why this is a unit test and not a scene suite.</b> The whole feature is a timing
/// decision over four booleans, and a timing decision is exactly the thing a headed run measures
/// worst: at 60 fps a 0.28 s window is seventeen frames, and a scene test that misses by one
/// frame is indistinguishable from a scene test that is flaky under load. Here the clock is the
/// argument, so "0.30 s apart does not sprint" is a fact rather than a hope.</para>
///
/// <para><b>The ABSENCE this file is the control for</b> (acceptance criterion 5): the double-tap
/// sets the SAME sprint bit Shift sets and introduces no wire bit. That claim's positive control
/// is not here — it is the empty <c>NetProfile.cs</c> diff against the base commit, recorded in
/// the packet report. What IS here is the other half: the detector never reaches for a protocol,
/// an input action or a Godot type, which is why this file can compile with none of them.</para>
/// </summary>
public class DoubleTapSprintTests
{
    /// <summary>One 60 fps frame. Every sequence below is written in frames because that is the
    /// resolution the real caller feeds the detector at, so a test that passes here passes at the
    /// rate the game actually samples.</summary>
    private const float Frame = 1f / 60f;

    /// <summary>Holds every direction clear for <paramref name="seconds"/>, one frame at a time.
    /// Stepping rather than jumping matters: a single huge delta would let a bug that only
    /// appears across several frames (the latch's all-clear accumulator, say) pass unseen.</summary>
    private static void Idle(DoubleTapSprint d, float seconds)
    {
        for (float t = 0f; t < seconds; t += Frame)
            d.Update(false, false, false, false, Frame);
    }

    /// <summary>Presses <paramref name="key"/> for one frame and releases it for one frame.</summary>
    private static void Tap(DoubleTapSprint d, int key)
    {
        Press(d, key, Frame);
        Idle(d, Frame);
    }

    private static void Press(DoubleTapSprint d, int key, float seconds)
    {
        for (float t = 0f; t < seconds; t += Frame)
            d.Update(key == 0, key == 1, key == 2, key == 3, Frame);
    }

    // --- The gesture itself -----------------------------------------------------------------

    [Theory]
    [InlineData(0)]  // W
    [InlineData(1)]  // S
    [InlineData(2)]  // A
    [InlineData(3)]  // D
    public void TapTapWithinTheWindow_Sprints(int key)
    {
        var d = new DoubleTapSprint();
        Tap(d, key);
        Assert.False(d.Latched);   // one tap is not a gesture
        Press(d, key, Frame);
        Assert.True(d.Latched);
    }

    /// <summary>The window covers the WHOLE gesture, so a leisurely re-press does not sprint.
    /// 0.40 s is comfortably outside 0.28 s and comfortably inside what a player does by accident
    /// while walking, which is the case that matters — if this went the other way the feature
    /// would fire during ordinary movement rather than when asked for.</summary>
    [Fact]
    public void TapTapOutsideTheWindow_DoesNotSprint()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Idle(d, 0.40f);
        Press(d, 0, Frame);
        Assert.False(d.Latched);
    }

    /// <summary>Holding the key through the window spends it: press, hold 0.40 s, release,
    /// re-press is a player walking, stopping and walking again. Timing only the GAP between the
    /// two presses would make this sprint, which is the defect the gesture-length window exists
    /// to prevent.</summary>
    [Fact]
    public void HoldThenRelease_ThenPress_DoesNotSprint()
    {
        var d = new DoubleTapSprint();
        Press(d, 0, 0.40f);
        Idle(d, Frame);
        Press(d, 0, Frame);
        Assert.False(d.Latched);
    }

    /// <summary>W then D is a direction change. Firing here would put the player in a sprint
    /// every time they rounded a corner.</summary>
    [Fact]
    public void TapTapOnDifferentKeys_DoesNotSprint()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Press(d, 3, Frame);
        Assert.False(d.Latched);
    }

    /// <summary>...and the mismatched second tap RE-ARMS on its own key rather than throwing the
    /// gesture away, so W, D, D sprints. Without this a player who fumbles the first key has to
    /// stop and start over, which reads as the feature being unreliable.</summary>
    [Fact]
    public void AMismatchedTap_ReArmsOnItsOwnKey()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Tap(d, 3);
        Press(d, 3, Frame);
        Assert.True(d.Latched);
    }

    // --- The latch's life -------------------------------------------------------------------

    /// <summary>Sprint persists while the direction is held — the point of a latch. Two seconds
    /// is far longer than any window in the class, so a latch that quietly expired would fail.</summary>
    [Fact]
    public void TheLatchSurvivesAContinuedHold()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Press(d, 0, 2.0f);
        Assert.True(d.Latched);
    }

    /// <summary>Releasing every direction ends the sprint — after the grace, not before.</summary>
    [Fact]
    public void TheLatchReleasesWhenEveryKeyIsUp()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Press(d, 0, 0.5f);
        Assert.True(d.Latched);

        Idle(d, DoubleTapSprint.ReleaseGraceSec * 0.5f);
        Assert.True(d.Latched);        // inside the grace: still sprinting

        Idle(d, DoubleTapSprint.ReleaseGraceSec);
        Assert.False(d.Latched);
    }

    /// <summary>The grace's whole job: a W→D strafe a couple of frames apart keeps the sprint.
    /// A zero grace would drop the player to a jog mid-corner, which is the one behaviour that
    /// would make the feature feel broken rather than absent.</summary>
    [Fact]
    public void AStrafeTransitionKeepsTheSprint()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Press(d, 0, 0.5f);
        Idle(d, 2f * Frame);           // both keys briefly up, well inside the grace
        Press(d, 3, 0.2f);
        Assert.True(d.Latched);
    }

    /// <summary>Losing input focus drops the latch outright — the caller does this when the mouse
    /// is released to a menu.</summary>
    [Fact]
    public void ResetDropsTheLatchAndEveryPendingTap()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Press(d, 0, 0.2f);
        Assert.True(d.Latched);

        d.Reset();
        Assert.False(d.Latched);

        // ...and the pending gesture is gone too: a lone press after a reset is a FIRST tap.
        Press(d, 0, Frame);
        Assert.False(d.Latched);
    }

    /// <summary>A first press arriving on the frame the latch expires starts a new gesture rather
    /// than completing the old one. Otherwise a player who stops, waits, and taps once would
    /// resume sprinting off a gesture they finished seconds ago.</summary>
    [Fact]
    public void ASinglePressAfterTheLatchExpires_DoesNotResumeIt()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        Press(d, 0, 0.3f);
        Idle(d, 1.0f);
        Assert.False(d.Latched);

        Press(d, 0, 0.3f);
        Assert.False(d.Latched);
    }

    /// <summary>A negative delta cannot come from the frame loop but can come from a bad caller,
    /// and a clock running backwards would hold a window open for ever. Clamped at zero.</summary>
    [Fact]
    public void ANegativeDeltaDoesNotRunTheWindowBackwards()
    {
        var d = new DoubleTapSprint();
        Tap(d, 0);
        d.Update(false, false, false, false, -100f);
        Idle(d, 0.40f);
        Press(d, 0, Frame);
        Assert.False(d.Latched);
    }

    /// <summary>The two published constants are the packet's numbers, pinned so a tuning pass is a
    /// deliberate edit with a test to update rather than a silent drift.</summary>
    [Fact]
    public void TheTimingConstantsAreTheAuthoredOnes()
    {
        Assert.Equal(0.28f, DoubleTapSprint.DoubleTapWindowSec, 1e-6f);
        Assert.Equal(0.15f, DoubleTapSprint.ReleaseGraceSec, 1e-6f);
        Assert.True(DoubleTapSprint.ReleaseGraceSec < DoubleTapSprint.DoubleTapWindowSec,
            "a grace at or above the double-tap window would let a re-tap resume a sprint the " +
            "player meant to end");
    }
}
