using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>BIKE-4B (2026-09-02): the wobble's frame sequences.</b> A wobble is motion, so a still is weak
/// evidence for it — the packet asks for a short SEQUENCE at walking pace, where the roll changes
/// frame to frame, and a matching one at ride speed, where it does not.
///
/// <para><b>Why this is a separate flag and a separate file.</b>
/// <c>MovementPlayground.BikeCapture.cs</c> is BIKE-2x-L's, its beat list is fixed, and this packet
/// is explicitly told to read it and not edit it. Its two lowest-speed frames are a standing mount,
/// which shows the wobble's amplitude but not its motion. So this file adds beats of its own and
/// borrows two things from that file without touching it: <c>BikeShot</c> (the shared shot-plus-log
/// helper) and <c>ChaseCam</c>.</para>
///
/// <para><b>Launch it with BOTH flags:</b>
/// <c>-- --bike-capture --bike-capture-wobble</c>. That is not redundant.
/// <c>BikeScriptedFlagPresent</c> lives in the other file and matches <c>--bike-capture</c>
/// exactly; it is what stops <c>BuildTuningPanel</c> loading (and later overwriting) the human
/// tuning file and opening the knob panel over the frame being photographed. A capture run at
/// anything but the defaults is a capture of the wrong tuning, so the plain flag has to be present;
/// this file's own flag then routes the run to the beats below instead of that file's.
/// <c>SAIL_BIKE_CAPTURE_OUT</c> names the absolute output directory, as it does there.</para>
///
/// <para>Headed only, like every capture in this lab: <see cref="ViewportCapture"/> refuses a
/// headless viewport loudly rather than filing an empty png.</para>
/// </summary>
public partial class MovementPlayground
{
    private bool _bikeWobbleCapture;

    /// <summary>Ticks driven between frames in a sequence. Six ticks is a tenth of a second at the
    /// lab's 60 Hz — close enough together that consecutive frames read as one motion, far enough
    /// apart that the roll has visibly moved between them.
    ///
    /// <para><b>The real gap between two filed frames is larger than this and is not uniform</b>,
    /// and the capture log must not be read as a time series: <c>BikeShot</c> waits a tick of its
    /// own and <see cref="ViewportCapture"/> waits on the renderer while it writes half a megabyte
    /// of png. The frames are ORDERED evidence, not sampled evidence. Anything that needs a uniform
    /// time base takes it from <c>--bike-handling-selftest</c>, which prints a tick-by-tick roll
    /// series with no I/O between samples.</para></summary>
    private const int WobbleFrameGapTicks = 6;

    private async Task RunBikeWobbleCaptureAsync()
    {
        try
        {
            await BikeWobbleCaptureBeatsAsync();
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"BIKE-WOBBLE-CAPTURE THREW: {e}");
            GetTree().Quit(1);
        }
    }

    private async Task BikeWobbleCaptureBeatsAsync()
    {
        var log = new List<string>();
        BikeLayer bike = _bike!;
        ScriptedInput brain = _scripted!;
        BikeHandlingTuning h = BikeHandlingTuning.Current;

        int park = CourseIndexOf<BikeParkCourse>();
        Vector3 parkOrigin = _courses[park].Position;
        // The park's HubPad: 210 x 20 m of flat. West end, heading +X, ~190 m of runway ahead —
        // the same strip BIKE-2x-L's capture rides, so the two sets of frames are comparable.
        Vector3 padWest = parkOrigin + new Vector3(-85f, 0.6f, -10f);
        var east = new Vector3(1f, 0f, 0f);

        log.Add($"BIKE-4B wobble capture. Amplitude {h.WobbleAmplitudeDeg:F2} deg, fade "
              + $"{h.WobbleFadeSpeedMps:F2} m/s, primary {BikeHandling.WobblePrimaryHz:F2} Hz, "
              + $"secondary {BikeHandling.WobbleSecondaryHz:F2} Hz at "
              + $"{BikeHandling.WobbleSecondaryShare:F2} of the amplitude. Frames "
              + $"{WobbleFrameGapTicks} physics ticks apart ({WobbleFrameGapTicks / 60f:F2} s).");

        _courseIndex = park;
        await Ticks(30);
        await TeleportWorld(padWest);
        await WaitUntil(() => _avatar.IsOnFloor(), 90);
        bike.PressMount();
        await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f) + 20);

        // --- 1. WALKING PACE: the wobble, frame to frame ----------------------------------------
        // The heading is settled onto +X first, and the corner lean allowed to bleed back to zero,
        // so nothing in these frames is a lean bleeding off a turn — every degree of roll in them
        // is the wobble. The wish is then bang-banged around 1 m/s.
        brain.Current = Go(east, sprint: false);
        for (int i = 0; i < 90; i++) { ChaseCam(); await Ticks(1); }
        // Coast right down to walking pace BEFORE the first frame — and wait on the SPEED, not on
        // the lean. The first take waited only for the corner lean to settle, which it already had,
        // and shot its "walking pace" sequence between 5.6 and 2.1 m/s: eight of its twelve frames
        // were above the 4 m/s fade, where the wobble is zero by design. The frames were correct
        // and proved nothing.
        brain.Current = MoveIntent.None;
        for (int i = 0; i < 600 && (HSpeed() > 1.05f || Mathf.Abs(_leanDeg) >= 0.05f); i++)
        { ChaseCam(); await Ticks(1); }
        log.Add($"walk sequence: coasted to {HSpeed():F2} m/s with the corner lean settled to "
              + $"{_leanDeg:F3} deg before the first frame");

        for (int f = 1; f <= 12; f++)
        {
            for (int i = 0; i < WobbleFrameGapTicks; i++)
            {
                brain.Current = Go(east * (HSpeed() < 1.0f ? 0.3f : 0f), sprint: false);
                ChaseCam();
                await Ticks(1);
            }
            await BikeShot($"w{f:D2}-walking-pace", log);
            log.Add($"  w{f:D2}: drawn roll {_rollDeg,6:F2} deg = corner lean "
                  + $"{_leanDeg,6:F2} + wobble {_wobbleDeg,6:F2}, at {HSpeed():F2} m/s");
        }

        // --- 2. RIDE SPEED: the same sequence, and nothing moves --------------------------------
        brain.Current = Go(east);
        for (int i = 0; i < 150; i++) { ChaseCam(); await Ticks(1); }
        log.Add($"ride sequence: at {HSpeed():F2} m/s, past the {h.WobbleFadeSpeedMps:F2} m/s fade");
        for (int f = 1; f <= 12; f++)
        {
            for (int i = 0; i < WobbleFrameGapTicks; i++)
            {
                brain.Current = Go(east);
                ChaseCam();
                await Ticks(1);
            }
            await BikeShot($"r{f:D2}-ride-speed", log);
            log.Add($"  r{f:D2}: drawn roll {_rollDeg,6:F2} deg = corner lean "
                  + $"{_leanDeg,6:F2} + wobble {_wobbleDeg,6:F2}, at {HSpeed():F2} m/s");
        }

        brain.Current = MoveIntent.None;
        WriteBikeCaptureLog(log);
        foreach (string line in log)
            GD.Print($"[bike-wobble-capture] {line}");
        await Ticks(5);
        GetTree().Quit(0);
    }
}
