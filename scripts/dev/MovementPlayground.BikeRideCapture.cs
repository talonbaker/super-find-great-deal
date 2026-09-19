using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>BIKE-4A — the evidence run: the same body, the same instant, with and without the ride
/// channel.</b> <c>--bike-ride-capture</c> after the <c>--</c>, with
/// <c>SAIL_BIKE_RIDE_CAPTURE_OUT</c> naming an absolute output directory. In-engine viewport
/// capture through the same <c>ViewportCapture</c> path BIKE-3A's harness uses — never a screen
/// grab, per the repo's standing verification method.
///
/// <para><b>Why an A/B in ONE run rather than two builds.</b> The obvious "before" is the tree this
/// packet branched from, but a capture taken on another build differs in the camera, the frame the
/// beat happened to land on, and (as the BIKE-2x-L set proves) in whatever else moved between the
/// two commits — <c>docs/qa/2026-09-02-bike-2x-l/</c> was shot before BIKE-3A fixed the stowed
/// stack's orientation, so its bike is drawn differently for reasons that have nothing to do with
/// the rider. Toggling the ride channel off inside one run holds every one of those constant and
/// leaves exactly one difference in the frame: whether the body is running or riding. That is the
/// comparison Talon is being asked to make.</para>
///
/// <para><b>Shot from the SIDE, deliberately.</b> The bike camera sits 1.4 m directly behind the
/// body, which is the right view for judging a lean and the worst possible one for judging a leg:
/// from behind, a bicycle and its rider are one thin silhouette. Every pair below is taken from
/// ninety degrees off the heading, where a knee on a crank is visible and a stride is unmistakable.
/// The chase view is not lost — the stock <c>--bike-capture</c> set is committed beside these.</para>
///
/// <para>Runs headed only: <c>ViewportCapture</c> refuses a headless viewport and each shot logs
/// its own failure rather than filing an empty png.</para>
/// </summary>
public partial class MovementPlayground
{
    /// <summary><c>--bike-ride-capture</c> was passed after the <c>--</c>.</summary>
    private bool _bikeRideCapture;

    private string _bikeRideCaptureDir = "";

    /// <summary><b>The A/B switch, and the only thing in this lab that can turn the ride channel
    /// off.</b> While true, <see cref="BikeRideVisuals"/> hands the body back to the on-foot system
    /// exactly as a dismount would — weight 0 is <c>SetRide</c>'s exact no-op — so the "before"
    /// frame is the body this packet's branch point produced, not an approximation of it.
    ///
    /// <para>Set only by this capture run. It is not a knob, it is not on the panel, and nothing
    /// reads it outside a capture.</para></summary>
    private bool _rideChannelForcedOff;

    /// <summary>Parsed off the raw command line rather than added to <c>_Ready</c>'s existing loop,
    /// for <c>BikeScriptedFlagPresent</c>'s reason: this answer is needed before the flag loop runs,
    /// and asking the command line twice costs nothing.</summary>
    private static bool BikeRideCaptureFlagPresent()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--bike-ride-capture")
                return true;
        return false;
    }

    private bool BikeRideCaptureReady()
    {
        if (!BikeRideCaptureFlagPresent())
            return false;
        _bikeRideCapture = true;
        _bikeRideCaptureDir = OS.GetEnvironment("SAIL_BIKE_RIDE_CAPTURE_OUT");
        if (_bikeRideCaptureDir.Length == 0)
        {
            GD.PrintErr("--bike-ride-capture needs SAIL_BIKE_RIDE_CAPTURE_OUT (absolute output dir)");
            GetTree().Quit(1);
            return false;
        }
        BuildScriptedBrainForRideCapture();
        return true;
    }

    /// <summary>
    /// The scripted brain, rebuilt for this run. <c>BuildAvatarAndCamera</c> chooses the brain off
    /// flags read earlier in <c>_Ready</c> than this one, so on a <c>--bike-ride-capture</c> run it
    /// has already wired the keyboard and <c>_scripted</c> is null — the identical trap
    /// <c>BuildScriptedBrainForSelfTest</c> documents, and the reason a headless run of the sibling
    /// harness once spun a core for eleven minutes instead of failing.
    ///
    /// <para>Four lines of its own rather than a call into the sibling packet's file, deliberately:
    /// BIKE-4A and BIKE-4B are branched from the same commit at the same moment, and a private
    /// method borrowed across that seam is a merge conflict waiting to be discovered by whoever
    /// integrates. The brain is the same <c>ScriptedInput</c> in the same <c>BikeLayer</c>, so this
    /// still drives the production seam and not a second input path.</para>
    /// </summary>
    private void BuildScriptedBrainForRideCapture()
    {
        _scripted = new ScriptedInput();
        _bike = new BikeLayer(_scripted, _avatar) { Scripted = true };
        _avatar.IntentSource = _bike;
        if (_tuning is not null)
            _bike.Notice = line => _tuning.Session.Note(line);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private async Task RunBikeRideCaptureAsync()
    {
        try
        {
            await BikeRideCaptureBeatsAsync();
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"BIKE-RIDE-CAPTURE THREW: {e}");
            GetTree().Quit(1);
        }
    }

    private async Task BikeRideCaptureBeatsAsync()
    {
        var log = new List<string>();
        BikeLayer bike = _bike!;
        ScriptedInput brain = _scripted!;

        int park = CourseIndexOf<BikeParkCourse>();
        _courseIndex = park;
        Vector3 parkOrigin = _courses[park].Position;
        Vector3 padWest = parkOrigin + new Vector3(-85f, 0.6f, -10f);
        var goX = new Vector3(1f, 0f, 0f);

        await Ticks(30);

        // --- 1. MOUNTED, STILL. The settled mount with no input: COAST, cranks level at 3-and-9.
        await TeleportWorld(padWest);
        await WaitUntil(() => _avatar.IsOnFloor(), 90);
        bike.PressMount();
        await WaitUntil(() => bike.Blend >= 1f, 120);
        await Ticks(40);
        await RidePair("01-mounted-still", log);

        // --- 2. AT THE RIDE CAP, DRIVING. PEDAL: the cranks turning at the cadence rule's rate and
        //        the torso down over the bars. This is the field note's frame — before the packet
        //        the body here is in Sprint gear at a full sprint cadence.
        brain.Current = Go(goX);
        await Ticks(200);
        await RidePair("02-ride-at-cap-pedal", log);

        // --- 3. COASTING AT SPEED. Stick released, past the 0.3 s hysteresis: the cranks settle
        //        level and the chest comes up 6 degrees. Shot while the body is still well above
        //        walking pace, so it is genuinely coasting rather than merely stopped.
        brain.Current = MoveIntent.None;
        await Ticks(Mathf.CeilToInt(RidePose.ModeHoldSec * 60f) + 30);
        await RidePair("03-coasting-at-speed", log);

        // --- 4. MOUNTED AIRBORNE. The airborne partition at 40 %: knees up through the rise with
        //        the feet still on the pedals, rather than a runner's tuck.
        await BikeRideAirborneShot("04-mounted-airborne", brain, goX, padWest, log);

        WriteBikeRideCaptureLog(log);
        foreach (string line in log)
            GD.Print($"[bike-ride-capture] {line}");
        await Ticks(5);
        GetTree().Quit(0);
    }

    /// <summary>One beat, twice: the channel forced off, then live. The OFF frame is taken first so
    /// the ON frame is the one the run leaves the body in.</summary>
    private async Task RidePair(string name, List<string> log)
    {
        _rideChannelForcedOff = true;
        await Ticks(40);                       // the gait ramps back in at GaitAmpRate
        SideCam();
        await Ticks(4);
        await RideShot(name + "-BEFORE", log);

        _rideChannelForcedOff = false;
        await Ticks(40);
        SideCam();
        await Ticks(4);
        await RideShot(name + "-AFTER", log);
    }

    /// <summary>The airborne pair. Two separate jumps rather than a toggle mid-flight, because an
    /// airborne frame lasts a fraction of a second and the honest comparison is two flights taken
    /// the same way — same run-up, same press, same tick after leaving the floor.</summary>
    private async Task BikeRideAirborneShot(
        string name, ScriptedInput brain, Vector3 dir, Vector3 start, List<string> log)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            _rideChannelForcedOff = pass == 0;
            brain.Current = MoveIntent.None;
            await TeleportWorld(start);
            await WaitUntil(() => _avatar.IsOnFloor(), 120);
            if (!(_bike?.Mounted ?? false))
            {
                _bike!.PressMount();
                await WaitUntil(() => _bike.Blend >= 1f, 120);
            }
            brain.Current = Go(dir);
            await Ticks(120);
            brain.Current = Go(dir, jump: true);
            await Ticks(1);
            brain.Current = Go(dir);
            await WaitUntil(() => !_avatar.IsOnFloor(), 30);
            await Ticks(8);                     // into the rise, before the apex
            SideCam();
            await RideShot(name + (pass == 0 ? "-BEFORE" : "-AFTER"), log);
            await WaitUntil(() => _avatar.IsOnFloor(), 240);
        }
        brain.Current = MoveIntent.None;
        _rideChannelForcedOff = false;
    }

    /// <summary>Ninety degrees off the body's heading, slightly above level — the view a leg on a
    /// crank is legible from. See the class doc for why the chase view is not used here.</summary>
    private void SideCam() => _camera.SetOrbit(_avatar.GlobalRotation.Y + (Mathf.Pi / 2f), -0.10f);

    private async Task RideShot(string name, List<string> log)
    {
        await Ticks(1);
        string path = System.IO.Path.Combine(_bikeRideCaptureDir, name + ".png");
        bool ok = await ViewportCapture.SaveAsync(this, path, name);
        AvatarVisual v = _avatar.Visual;
        log.Add($"{name}: speed {HSpeed():F2} m/s, {(_bike?.Mounted ?? false ? "MOUNTED" : "on foot")}"
              + $", blend {_bike?.Blend ?? 0f:F2}, ride weight POSED {v.RideWeightForTools:F2}"
              + $", mode {v.RideModeForTools}, cranks {v.CrankHz:F3} rev/s at phase "
              + $"{v.CrankPhase01ForTools:F3}, gear {v.Gear}, gait cadence {v.CadenceHz:F2} steps/s"
              + $", body tilt {v.BodyTiltX:F3} rad, grounded {_avatar.IsOnFloor()}"
              + (ok ? "" : "   [CAPTURE FAILED]"));
    }

    private void WriteBikeRideCaptureLog(List<string> log)
    {
        string path = System.IO.Path.Combine(_bikeRideCaptureDir, "ride-capture-log.txt");
        try
        {
            System.IO.Directory.CreateDirectory(_bikeRideCaptureDir);
            System.IO.File.WriteAllLines(path, log);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[bike-ride-capture] could not write {path}: {e.Message}");
        }
    }
}
