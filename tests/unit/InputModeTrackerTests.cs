using MpFoundation.Controls;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Pure-logic coverage for the controller auto-detect state machine (InputModeTracker).
/// Godot-free — the tracker touches nothing but System.MathF — so this runs in the same fast,
/// engine-free tier as AimControllerTests and NetCodec's suite: no editor, no scene tree, no
/// physical gamepad, no port binding.
///
/// The point of testing it at this tier is that the two rules that actually matter — a jittering
/// mouse must not steal the mode from a pad, a drifting stick must not steal it from the keyboard
/// — are precisely the ones that are miserable to verify by hand and easy to get wrong. They are
/// pinned here as arithmetic. What is NOT covered here is anything that needs real hardware:
/// that a real Xbox pad's axes land on the indices project.godot names, and how the thresholds
/// FEEL against a real worn stick. Those are a hands-on check, not a unit test.
/// </summary>
public class InputModeTrackerTests
{
    /// <summary>Comfortably past <see cref="InputModeTracker.StickActivationDeadzone"/>.</summary>
    private const float FullPush = 1.0f;

    /// <summary>One mouse event big enough to clear the travel threshold on its own.</summary>
    private const float BigMouseStep = InputModeTracker.MouseTravelThresholdPx + 1f;

    private static InputModeTracker OnPad()
    {
        var t = new InputModeTracker();
        Assert.True(t.NotifyPadButtonPressed());
        Assert.Equal(InputMode.Gamepad, t.Mode);
        return t;
    }

    // --- Defaults ----------------------------------------------------------------------

    [Fact]
    public void StartsOnKeyboardMouse()
    {
        var t = new InputModeTracker();
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
        Assert.False(t.IsPad);
    }

    // --- Flipping TO the pad -----------------------------------------------------------

    [Fact]
    public void PadButton_FlipsToGamepad()
    {
        var t = new InputModeTracker();
        Assert.True(t.NotifyPadButtonPressed());
        Assert.Equal(InputMode.Gamepad, t.Mode);
        Assert.True(t.IsPad);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    [InlineData(InputModeTracker.StickActivationDeadzone)]        // exactly at the bar counts
    [InlineData(-InputModeTracker.StickActivationDeadzone)]
    public void StickPushPastDeadzone_FlipsToGamepad(float axis)
    {
        var t = new InputModeTracker();
        Assert.True(t.NotifyPadAxis(axis));
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    /// <summary>THE stick-drift guard: a worn stick resting off-centre must not yank the mode
    /// away from someone typing. Includes 0.3, which is past the look actions' own 0.25
    /// deadzone — proof the two thresholds are genuinely independent and not accidentally
    /// the same number.</summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(-0.3f)]
    [InlineData(0.49f)]
    public void StickDriftBelowDeadzone_DoesNotFlip(float axis)
    {
        var t = new InputModeTracker();
        Assert.False(t.NotifyPadAxis(axis));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    [Fact]
    public void SustainedStickDrift_NeverAccumulatesIntoAFlip()
    {
        var t = new InputModeTracker();
        for (int i = 0; i < 10_000; i++)
            Assert.False(t.NotifyPadAxis(0.3f));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    // --- Flipping BACK to keyboard/mouse -----------------------------------------------

    [Fact]
    public void KeyPress_FlipsBackImmediately()
    {
        var t = OnPad();
        Assert.True(t.NotifyKeyPressed());
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    [Fact]
    public void MouseButton_FlipsBackImmediately()
    {
        var t = OnPad();
        Assert.True(t.NotifyMouseButtonPressed());
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    [Fact]
    public void OneLargeMouseMove_FlipsBack()
    {
        var t = OnPad();
        Assert.True(t.NotifyMouseMotion(BigMouseStep, 0f, 1.0));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    /// <summary>Travel is summed on both axes, so a diagonal gesture counts its full length —
    /// a move that is half the threshold on each axis still clears it.</summary>
    [Fact]
    public void DiagonalMouseMove_CountsBothAxes()
    {
        var t = OnPad();
        float half = (InputModeTracker.MouseTravelThresholdPx / 2f) + 1f;
        Assert.True(t.NotifyMouseMotion(half, half, 1.0));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    /// <summary>Negative deltas are travel too — moving left is still moving.</summary>
    [Fact]
    public void NegativeMouseMotion_CountsAsTravel()
    {
        var t = OnPad();
        Assert.True(t.NotifyMouseMotion(-BigMouseStep, 0f, 1.0));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    // --- THE mouse-jitter guard --------------------------------------------------------

    /// <summary>The single most common bug in this feature: a sensor twitching one pixel at a
    /// time drags the game back off the pad the player is holding. One tiny event must never
    /// be enough, no matter how many arrive, as long as they stay spread out.</summary>
    [Fact]
    public void TinyMouseJitter_SpreadOverTime_NeverStealsTheModeFromThePad()
    {
        var t = OnPad();
        double now = 0;
        // A full simulated minute of 1px twitches, each one a fresh window apart: the
        // accumulator restarts every time, so this can run forever without ever tripping.
        for (int i = 0; i < 60; i++)
        {
            now += InputModeTracker.MouseTravelWindowSec + 0.01;
            Assert.False(t.NotifyMouseMotion(1f, 0f, now));
        }
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    /// <summary>The other half of the same guard: jitter inside one window DOES add up, because
    /// motion that dense is a hand on the mouse, not a sensor at rest. Proves the accumulator is
    /// actually accumulating rather than the previous test passing for the wrong reason.</summary>
    [Fact]
    public void ContinuousSmallMotion_InsideOneWindow_DoesAddUpAndFlips()
    {
        var t = OnPad();
        double now = 0;
        bool flipped = false;
        for (int i = 0; i < (int)InputModeTracker.MouseTravelThresholdPx; i++)
        {
            now += 0.01; // well inside the window
            if (t.NotifyMouseMotion(1f, 0f, now))
            {
                flipped = true;
                break;
            }
        }
        Assert.True(flipped, "dense 1px motion should sum past the threshold inside one window");
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    /// <summary>Just under the threshold, all inside one window, must still not flip — the bar
    /// is a real bar, not a rounding artifact.</summary>
    [Fact]
    public void MotionJustUnderThreshold_InsideOneWindow_DoesNotFlip()
    {
        var t = OnPad();
        Assert.False(t.NotifyMouseMotion(InputModeTracker.MouseTravelThresholdPx - 1f, 0f, 1.0));
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    /// <summary>Partial travel banked before switching to the pad must not carry across and flip
    /// the mode straight back on the next twitch.</summary>
    [Fact]
    public void PartialTravel_IsDiscardedWhenThePadTakesOver()
    {
        var t = OnPad();
        Assert.False(t.NotifyMouseMotion(InputModeTracker.MouseTravelThresholdPx - 1f, 0f, 1.0));

        // Player goes back to the pad, then the mouse twitches once.
        Assert.False(t.NotifyPadAxis(FullPush)); // already Gamepad -> no change reported
        Assert.False(t.NotifyMouseMotion(1f, 0f, 1.01));
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    /// <summary>Active pad use clears banked travel even though the mode did not CHANGE (it was
    /// already Gamepad). Without this, a session's worth of near-threshold jitter sits half-full
    /// forever and one stray pixel tips it — which is exactly how the first draft of the tracker
    /// failed this suite. The mouse must clear the bar in a window the pad stayed quiet for.</summary>
    [Fact]
    public void PadActivityMidWindow_ResetsBankedMouseTravel()
    {
        var t = OnPad();
        double now = 1.0;
        Assert.False(t.NotifyMouseMotion(InputModeTracker.MouseTravelThresholdPx - 1f, 0f, now));

        now += 0.01;
        Assert.False(t.NotifyPadAxis(FullPush)); // still Gamepad, so no transition reported...
        Assert.False(t.NotifyMouseMotion(1f, 0f, now)); // ...but the accumulator was wiped
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    /// <summary>Drift below the deadzone is NOT use, so it must not reset the accumulator —
    /// otherwise a worn stick resting at 0.3 would quietly make the mouse unable to ever
    /// reclaim the mode.</summary>
    [Fact]
    public void StickDriftMidWindow_DoesNotResetBankedMouseTravel()
    {
        var t = OnPad();
        double now = 1.0;
        Assert.False(t.NotifyMouseMotion(InputModeTracker.MouseTravelThresholdPx - 1f, 0f, now));

        now += 0.01;
        Assert.False(t.NotifyPadAxis(0.3f)); // drift, ignored entirely
        Assert.True(t.NotifyMouseMotion(1f, 0f, now));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    /// <summary>The autoload's clock is Godot's millisecond tick counter, which wraps roughly
    /// every 49 days of uptime. A signed time compare would read the wrap as "no time passed"
    /// and let a pre-wrap accumulator keep filling into a spurious flip; an absolute one treats
    /// it as a gap and restarts, which is the correct reading either way.</summary>
    [Fact]
    public void ClockGoingBackwards_ResetsTheWindowRatherThanAccumulating()
    {
        var t = OnPad();
        Assert.False(t.NotifyMouseMotion(InputModeTracker.MouseTravelThresholdPx - 1f, 0f, 4_294_967.0));
        Assert.False(t.NotifyMouseMotion(1f, 0f, 0.0)); // counter wrapped back to zero
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    // --- Presence is not use -----------------------------------------------------------

    /// <summary>Plenty of people leave a pad plugged in and never touch it. Enumerating a USB
    /// device must not change what the game thinks the player is holding.</summary>
    [Fact]
    public void PadConnected_ButUntouched_DoesNotFlip()
    {
        var t = new InputModeTracker();
        Assert.False(t.NotifyPadConnected());
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    [Fact]
    public void PadConnected_WhileAlreadyOnPad_ChangesNothing()
    {
        var t = OnPad();
        Assert.False(t.NotifyPadConnected());
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    // --- Disconnect fallback -----------------------------------------------------------

    /// <summary>A player whose pad just died cannot press the key that would otherwise be needed
    /// to flip back, so the unplug itself has to do it.</summary>
    [Fact]
    public void LastPadUnplugged_FallsBackToKeyboardMouse()
    {
        var t = OnPad();
        Assert.True(t.NotifyPadDisconnected(anyPadStillConnected: false));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    /// <summary>With a second pad still attached the player has not lost their controller.</summary>
    [Fact]
    public void OnePadUnplugged_AnotherStillConnected_StaysOnGamepad()
    {
        var t = OnPad();
        Assert.False(t.NotifyPadDisconnected(anyPadStillConnected: true));
        Assert.Equal(InputMode.Gamepad, t.Mode);
    }

    [Fact]
    public void PadUnplugged_WhileAlreadyOnKeyboard_ReportsNoChange()
    {
        var t = new InputModeTracker();
        Assert.False(t.NotifyPadDisconnected(anyPadStillConnected: false));
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }

    // --- "Changed" is a real edge ------------------------------------------------------

    /// <summary>Every Notify* returns true ONLY on a real transition — the autoload emits its
    /// signal straight off that return, so a sloppy "always true" would fire the signal on
    /// every mouse event of the session.</summary>
    [Fact]
    public void RepeatedSameDeviceInput_ReportsChangedExactlyOnce()
    {
        var t = new InputModeTracker();
        Assert.True(t.NotifyPadButtonPressed());
        Assert.False(t.NotifyPadButtonPressed());
        Assert.False(t.NotifyPadAxis(FullPush));

        Assert.True(t.NotifyKeyPressed());
        Assert.False(t.NotifyKeyPressed());
        Assert.False(t.NotifyMouseButtonPressed());
    }

    // --- Keyboard/mouse is never taken away --------------------------------------------

    /// <summary>The whole constraint on this feature in one test: whatever the pad has been
    /// doing, the keyboard can always take the game straight back. Auto-detect adds a mode; it
    /// never removes a capability.</summary>
    [Fact]
    public void KeyboardCanAlwaysReclaimTheMode_NoMatterWhatThePadDid()
    {
        var t = new InputModeTracker();
        for (int i = 0; i < 50; i++)
        {
            t.NotifyPadButtonPressed();
            t.NotifyPadAxis(FullPush);
            Assert.Equal(InputMode.Gamepad, t.Mode);

            t.NotifyKeyPressed();
            Assert.Equal(InputMode.KeyboardMouse, t.Mode);
        }
    }

    [Fact]
    public void AlternatingDevices_TrackTheLastRealUseEveryTime()
    {
        var t = new InputModeTracker();
        Assert.True(t.NotifyPadAxis(FullPush));
        Assert.True(t.NotifyMouseMotion(BigMouseStep, 0f, 1.0));
        Assert.True(t.NotifyPadButtonPressed());
        Assert.True(t.NotifyMouseButtonPressed());
        Assert.True(t.NotifyPadAxis(-FullPush));
        Assert.True(t.NotifyKeyPressed());
        Assert.Equal(InputMode.KeyboardMouse, t.Mode);
    }
}
