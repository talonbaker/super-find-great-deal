using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>BIKE-2x-L (2026-09-02): the headed capture run — the evidence half of the feel packet.</b>
/// <c>--bike-capture</c> after the <c>--</c>, with <c>SAIL_BIKE_CAPTURE_OUT</c> naming an absolute
/// output directory. In-engine viewport capture, never a screen grab, per the repo's standing
/// verification method; the handling readout is bottom-right in every frame, so each picture
/// carries the numbers it is evidence for.
///
/// <para><b>What the run photographs, in order:</b> the mount chain with the bike camera live
/// (on foot, mid-blend, settled); the ride at the cap, where the camera's three curves are at
/// their far ends; a hard LEFT-hander at speed — the one frame that settles the lean's sign;
/// the drift's tier ladder (a frame at each tier the spark changes colour for) and the owed
/// readout across the exit; a jump, the landing and the dismount blend; the wiggle charge model
/// filling the same ladder; and the zone-6 rolling course this packet ports, ridden over its
/// rises. It answers no feel question — it produces the frames Talon's feel session and this
/// packet's report argue from.</para>
///
/// <para>Runs headed only: <see cref="ViewportCapture"/> refuses a headless viewport, and every
/// shot logs its failure loudly rather than filing an empty png.</para>
/// </summary>
public partial class MovementPlayground
{
    private bool _bikeCapture;
    private string _bikeCaptureDir = "";

    /// <summary>Whether this run was launched with either of BIKE-2x's scripted flags — read off
    /// the raw command line because <c>BuildTuningPanel</c> needs the answer before
    /// <c>BikeHandlingReady</c> has parsed them into fields. A scripted run must not load (or
    /// overwrite) the human tuning file, and must not open the panel over the frame it is
    /// photographing.</summary>
    private static bool BikeScriptedFlagPresent()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a is "--bike-handling-selftest" or "--bike-capture")
                return true;
        return false;
    }

    private async Task RunBikeCaptureAsync()
    {
        try
        {
            await BikeCaptureBeatsAsync();
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"BIKE-CAPTURE THREW: {e}");
            GetTree().Quit(1);
        }
    }

    private async Task BikeCaptureBeatsAsync()
    {
        var log = new List<string>();
        BikeLayer bike = _bike!;
        ScriptedInput brain = _scripted!;

        int park = CourseIndexOf<BikeParkCourse>();
        int rolling = CourseIndexOf<RollingCourse>();
        Vector3 parkOrigin = _courses[park].Position;
        // The park's HubPad: local (12, 0, -8), 210 x 20 m — the long flat strip every beat below
        // rides. West end, heading +X, leaves ~190 m of pad ahead.
        Vector3 padWest = parkOrigin + new Vector3(-85f, 0.6f, -10f);

        await Ticks(30);

        // --- 1. The mount chain, with the bike camera live --------------------------------------
        _courseIndex = park;
        await TeleportWorld(padWest);
        await WaitUntil(() => _avatar.IsOnFloor(), 90);
        ChaseCam();
        await Ticks(10);
        await BikeShot("01-park-on-foot", log);

        bike.PressMount();
        await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f / 2f));
        await BikeShot("02-mount-mid-blend", log);
        await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f) + 10);
        await BikeShot("03-mounted", log);

        // --- 2. The ride at the cap: the camera's three curves at their far ends ----------------
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        for (int i = 0; i < 150; i++) { ChaseCam(); await Ticks(1); }
        await BikeShot("04-ride-at-cap", log);

        // --- 3. THE HARD LEFT-HANDER — the lean-sign frame ---------------------------------------
        // Left of a +X heading is -Z. The wish swings 90 degrees and holds; the shot is taken at
        // the first tick the lean passes 8 degrees, so the frame shows the settled lean while the
        // yaw rate that caused it is still on the readout.
        //
        // The look-ahead is parked at its base for this one beat: at the 4.00 m cap the camera's
        // focus is 4 m ahead of the bike and the SUBJECT rides the frame's edge — the first take
        // of this capture proved it. Legitimate to move alone: BikeCameraTests pins the three
        // curves' independence. Restored right after the frame.
        BikeCameraTuning camBefore = BikeCameraTuning.Current;
        BikeCameraTuning.Current = camBefore with { LookAheadAtCapM = camBefore.LookAheadBaseM };
        brain.Current = Go(new Vector3(0.2f, 0f, -1f));
        float shotLean = 0f, shotYaw = 0f;
        bool leanShot = false;
        for (int i = 0; i < 90; i++)
        {
            ChaseCam();
            await Ticks(1);
            if (!leanShot && Mathf.Abs(_leanDeg) > 8f)
            {
                shotLean = _leanDeg;
                shotYaw = _yawRatePerSec;
                await BikeShot("05-hard-left-lean", log);
                leanShot = true;
            }
        }
        log.Add($"lean-sign frame: lean {shotLean:F2} deg at yaw {shotYaw:F2} rad/s "
              + $"(positive yaw = left turn; positive lean = drawn as +Z roll). "
              + $"Frame shows which way the greybox actually tips{(leanShot ? "" : " — NEVER TAKEN, lean stayed under 8 deg")}");
        BikeCameraTuning.Current = camBefore;

        // --- 4. The drift's tier ladder, duration model (CTRL+ALT+0 default) --------------------
        await TeleportWorld(padWest);
        await WaitUntil(() => _avatar.IsOnFloor(), 90);
        if (!bike.Mounted)
        {
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f) + 10);
        }
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        for (int i = 0; i < 60; i++) { ChaseCam(); await Ticks(1); }
        // A light held steer so the frames show a cornering bike without arcing off the 20 m-deep
        // pad — the first take held -0.45 and turned itself off the south edge before tier 3,
        // which is why this loop is gated on the CHARGE rather than on a tick count.
        brain.Current = Go(new Vector3(1f, 0f, -0.15f));
        _scriptedDriftHeld = true;
        int lastTierShot = 0;
        for (int i = 0; i < 400; i++)
        {
            ChaseCam();
            await Ticks(1);
            if (_driftTierNow > lastTierShot)
            {
                lastTierShot = _driftTierNow;
                await BikeShot($"06-drift-tier{lastTierShot}", log);
                if (lastTierShot >= 3)
                    break;
            }
        }
        float owedBefore = _owedBoostMps;
        _scriptedDriftHeld = false;
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        await Ticks(4);
        await BikeShot("07-drift-exit-owed", log);
        log.Add($"drift exit: peak tier shot {lastTierShot}, owed {owedBefore:F2} -> "
              + $"{_owedBoostMps:F2} m/s (the boost is OWED, never applied — the impulse seam is "
              + "not on this branch)");

        // --- 5. Air, landing, dismount --------------------------------------------------------
        brain.Current = Go(new Vector3(1f, 0f, 0f), jump: true);
        await Ticks(3);
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        await Ticks(12);
        await BikeShot("08-airborne", log);
        await WaitUntil(() => _avatar.IsOnFloor(), 180);
        ChaseCam();
        await Ticks(6);
        bike.PressMount();   // dismount
        await Ticks(6);
        await BikeShot("09-dismount-blend", log);
        await WaitUntil(() => !bike.Mounted && bike.Blend <= 0f, 120);
        await Ticks(10);
        await BikeShot("10-on-foot-camera-inert", log);

        // --- 6. The wiggle charge model (CTRL+ALT+3), same ladder ------------------------------
        BikeHandlingTuning before = BikeHandlingTuning.Current;
        BikeHandlingTuning.Current = BikeHandlingTuning.ForKey(3)!.Value.Tuning;
        await TeleportWorld(padWest);
        await WaitUntil(() => _avatar.IsOnFloor(), 90);
        bike.PressMount();
        await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f) + 10);
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        for (int i = 0; i < 60; i++) { ChaseCam(); await Ticks(1); }
        _scriptedDriftHeld = true;
        int wigglePeak = 0;
        for (int i = 0; i < 300; i++)
        {
            // Work the stick: a full-width flick every ten ticks, fed BODY-RELATIVE. SteerNow
            // projects the scripted MoveDir onto the body's CURRENT right vector, so a wish held
            // in world space decays below DriftWiggleFlickMin (0.55) as the body chases it — the
            // first two takes charged nothing for exactly that reason (0.54 on take one, a
            // rotated-out projection on take two). Composing the wish from the live facing keeps
            // the projected steer at 0.9 on every tick.
            float steer = (i / 10) % 2 == 0 ? 0.9f : -0.9f;
            Vector3 fwd = BikeLayer.FacingOf(_avatar);
            var right = new Vector3(fwd.Z, 0f, -fwd.X);   // SteerNow's own convention
            brain.Current = Go(fwd + right * steer);
            ChaseCam();
            await Ticks(1);
            if (_driftTierNow > wigglePeak)
            {
                wigglePeak = _driftTierNow;
                if (wigglePeak >= 2)
                {
                    await BikeShot("11-wiggle-charge-tier2", log);
                    break;
                }
            }
        }
        _scriptedDriftHeld = false;
        log.Add($"wiggle model (CTRL+ALT+3): peak tier {wigglePeak} off stick flicks alone — "
              + "same ladder, different clock");
        BikeHandlingTuning.Current = before;
        await Ticks(4);

        // --- 7. The rolling course, ridden ------------------------------------------------------
        TeleportTo(rolling);
        await WaitUntil(() => _avatar.IsOnFloor(), 120);
        if (!bike.Mounted)
        {
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f) + 10);
        }
        brain.Current = Go(new Vector3(0f, 0f, 1f));
        for (int i = 0; i < 130; i++) { ChaseCam(); await Ticks(1); }
        await BikeShot("12-rolling-first-rise", log);
        for (int i = 0; i < 150; i++) { ChaseCam(); await Ticks(1); }
        await BikeShot("13-rolling-mid-patch", log);
        log.Add($"rolling course: slope bonus {_bike?.SlopeBonusMps ?? 0f:F2} m/s "
              + $"(downhill sin {_bike?.DownhillSin ?? 0f:F2}) at the 13 frame — the continuous "
              + "surface the camera and the drift exit have never been judged on until now");

        // --- 8. BIKE-3A: the turn ladder at a heading OPPOSITE the pre-fix frozen frame ----------
        // The run-out is 46 m along +Z; riding it puts the heading 180 deg from world -Z, which is
        // where the pre-fix greybox sat pointed whatever the body did. One hard corner each way,
        // shot as |lean| passes 8 deg, so each frame carries the lean/yaw it is evidence for.
        DescentCourse bike3aDescent = _courses.OfType<DescentCourse>().First();
        Vector3 bike3aRunOut = bike3aDescent.GlobalPosition + bike3aDescent.RunOutStartLocal
            + new Vector3(0f, 0.6f, 2f);
        foreach (float turnDir in new[] { 1f, -1f })   // +1 = left, -1 = right
        {
            await TeleportWorld(bike3aRunOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 90);
            if (!bike.Mounted)
            {
                bike.PressMount();
                await Ticks(Mathf.CeilToInt(BikeTuning.Current.MountBlendSec * 60f) + 10);
            }
            brain.Current = Go(new Vector3(0f, 0f, 1f));
            for (int i = 0; i < 90; i++) { ChaseCam(); await Ticks(1); }
            bool turnShot = false;
            for (int i = 0; i < 60 && !turnShot; i++)
            {
                // Wish held to one side of the LIVE facing, composed from the body's own yaw.
                float ay = _avatar.GlobalRotation.Y;
                var f = new Vector3(-Mathf.Sin(ay), 0f, -Mathf.Cos(ay));
                Vector3 left = Vector3.Up.Cross(f);
                brain.Current = Go((f + left * (1.2f * turnDir)).Normalized());
                ChaseCam();
                await Ticks(1);
                if (Mathf.Abs(_leanDeg) > 8f)
                {
                    string name = turnDir > 0f ? "14-left-lean-heading-plusZ" : "15-right-lean-heading-plusZ";
                    await BikeShot(name, log);
                    float gbYawDeg = Mathf.RadToDeg(_bikeMesh?.Rotation.Y ?? float.NaN);
                    log.Add($"{name}: avatar yaw {Mathf.RadToDeg(_avatar.GlobalRotation.Y):F1} deg "
                          + $"({Mathf.Abs(Mathf.Wrap(Mathf.RadToDeg(_avatar.GlobalRotation.Y), -180f, 180f)):F0} deg "
                          + $"from world -Z), greybox yaw {gbYawDeg:F1} deg — "
                          + "the bike must be drawn tipping INTO this turn");
                    turnShot = true;
                }
            }
            if (!turnShot)
                log.Add($"turn-ladder {(turnDir > 0f ? "left" : "right")}: NEVER SHOT — lean stayed under 8 deg");
            brain.Current = MoveIntent.None;
            await Ticks(30);
        }

        // --- 9. BIKE-3A: the stowed stack, from behind and from the side -------------------------
        // Flat against the back reads as: coin FACES toward the viewer from behind, thin EDGE from
        // the side. The pre-fix defect is the exact inverse.
        if (bike.Mounted)
            bike.PressMount();   // stow
        await WaitUntil(() => !bike.Mounted && bike.Blend <= 0f, 180);
        await WaitUntil(() => bike.SinceToggleSec > BikeTuning.Current.FoldSec + 0.2f, 120);
        _camera.SetOrbit(_avatar.GlobalRotation.Y, -0.05f);
        await Ticks(10);
        await BikeShot("16-stowed-from-behind", log);
        _camera.SetOrbit(_avatar.GlobalRotation.Y + Mathf.Pi / 2f, -0.05f);
        await Ticks(10);
        await BikeShot("17-stowed-from-the-side", log);
        Vector3 discNormal = _bikeMesh?.DiscNormalLocal ?? Vector3.Zero;
        log.Add($"stowed disc normal, greybox-local: ({discNormal.X:F2}, {discNormal.Y:F2}, "
              + $"{discNormal.Z:F2}) — |Z| ~ 1 is flat against the back, |X| ~ 1 is edge-on");

        brain.Current = MoveIntent.None;
        WriteBikeCaptureLog(log);
        foreach (string line in log)
            GD.Print($"[bike-capture] {line}");
        await Ticks(5);
        GetTree().Quit(0);
    }

    /// <summary>Keeps the orbit camera directly behind the body's current heading, slightly above
    /// level — the view that makes a roll's direction unambiguous in a still frame.</summary>
    private void ChaseCam() => _camera.SetOrbit(_avatar.GlobalRotation.Y, -0.18f);

    private async Task BikeShot(string name, List<string> log)
    {
        await Ticks(1);
        string path = System.IO.Path.Combine(_bikeCaptureDir, name + ".png");
        bool ok = await ViewportCapture.SaveAsync(this, path, name);
        BikeCamera? cam = _bikeCamera;
        log.Add($"{name}: speed {HSpeed():F2} m/s, {(_bike?.Mounted ?? false ? "MOUNTED" : "on foot")}"
              + $", blend {_bike?.Blend ?? 0f:F2}, lean {_leanDeg:F2} deg, yaw {_yawRatePerSec:F2} rad/s"
              + $", drift tier {_driftTierNow} charge {_drift.ChargeSec:F2} s, owed {_owedBoostMps:F2}"
              + $", cam {(cam?.Active ?? false ? "LIVE" : "inert")} fov {cam?.FovNow ?? 0f:F2} deg"
              + $" dist {cam?.DistanceNow ?? 0f:F2} m look-ahead {cam?.LookAheadNow ?? 0f:F2} m"
              + (ok ? "" : "   [CAPTURE FAILED]"));
    }

    private void WriteBikeCaptureLog(List<string> log)
    {
        string path = System.IO.Path.Combine(_bikeCaptureDir, "capture-log.txt");
        try
        {
            System.IO.Directory.CreateDirectory(_bikeCaptureDir);
            System.IO.File.WriteAllLines(path, log);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[bike-capture] could not write {path}: {e.Message}");
        }
    }
}
