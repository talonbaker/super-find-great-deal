using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>The bike prototype, driven headlessly and measured in the engine</b> (BIKE-0, 2026-09-01).
/// <c>--bike-selftest</c> after the <c>--</c>. The scripted brain feeds the SAME
/// <see cref="BikeLayer"/> a keyboard does, through the same <c>AvatarMotor.Step</c>, on the
/// same courses; only the summon press arrives by call. Every check prints
/// <c>BIKE-SELFTEST &lt;name&gt;: PASS|FAIL - &lt;the numbers&gt;</c>, then
/// <c>BIKE-SELFTEST OVERALL</c>, and the process exits 1 on any red.
///
/// <para>What it can and cannot prove. It proves the mechanics do what the layer says: the
/// tuning blends and restores, the hops and bursts land in the velocity, the stumble clamps and
/// the switch removes it, the drift swings the nose and keeps speed, the slope pays only while
/// mounted, the slide jump is taller, the ramp climbs. It proves nothing about feel — that is
/// Talon's, headed — and nothing about the picture, since a headless run has no renderer.</para>
///
/// <para>It runs on the Roller Run's own geometry: the flat run-out for everything level, the
/// plateau and the 12° opener for the slope. Those numbers are <see cref="DescentCourse"/>'s and
/// are derived here from the same constants rather than typed twice.</para>
/// </summary>
public partial class MovementPlayground
{
    private readonly List<(string name, bool pass, string detail)> _bikeChecks = new();

    private void BikeCheck(string name, bool pass, string detail)
    {
        _bikeChecks.Add((name, pass, detail));
        GD.Print($"BIKE-SELFTEST {name}: {(pass ? "PASS" : "FAIL")} - {detail}");
    }

    private float HSpeed() => new Vector2(_avatar.Velocity.X, _avatar.Velocity.Z).Length();

    /// <summary>Heading of the horizontal velocity, degrees, 0 = +Z, positive toward +X.</summary>
    private float HeadingDeg() => Mathf.RadToDeg(Mathf.Atan2(_avatar.Velocity.X, _avatar.Velocity.Z));

    private static MoveIntent Go(Vector3 dir, bool sprint = true, bool jump = false, bool drift = false)
        => new() { MoveDir = dir, Sprint = sprint, Jump = jump, JumpHeld = jump, AimRaise = drift };

    private async Task TeleportWorld(Vector3 pos)
    {
        _avatar.ServerTeleportTo(pos);
        _avatar.Velocity = Vector3.Zero;
        _airborneFromJump = false;
        _wasGrounded = true;
        _scripted!.Current = MoveIntent.None;
        await Ticks(30);
    }

    private async Task<bool> WaitUntil(Func<bool> cond, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (cond()) return true;
            await Ticks(1);
        }
        return cond();
    }

    private async Task<float> MaxOver(Func<float> f, int ticks)
    {
        float best = float.NegativeInfinity;
        for (int i = 0; i < ticks; i++)
        {
            best = Mathf.Max(best, f());
            await Ticks(1);
        }
        return best;
    }

    private async Task RunBikeSelfTestAsync()
    {
        BikeLayer bike = _bike!;
        ScriptedInput brain = _scripted!;
        BikeTuning b = BikeTuning.Current;
        DescentCourse descent = _courses.OfType<DescentCourse>().First();
        int descentIndex = _courses.IndexOf(descent);

        // The run-out: flat, 46 m along +Z, top surface at the cursor's final Y. Derived from
        // DescentCourse's own section list so a change to any angle moves this with it.
        Vector3 runOut = descent.GlobalPosition + descent.RunOutStartLocal + new Vector3(0f, 0.6f, 2f);
        Vector3 plateau = descent.GlobalPosition + descent.SpawnPointLocal;
        Vector3 fwd = new(0f, 0f, 1f);

        MotorTuning footAtStart = MotorTuning.Current;
        float footCap = BikeRig.FootCapMps(footAtStart);
        float rideCap = BikeRig.RideCapMps(footAtStart, b);

        await Ticks(20);
        GD.Print($"[bike-selftest] foot cap {footCap:F2} m/s, ride cap {rideCap:F2} m/s, run-out at {runOut}");

        // 1. A ground mount blends the tuning in, and the body hops onto the bike. ----------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.PressMount();
        await Ticks(2);
        float speedAfterOneTick = MotorTuning.Current.MoveSpeed;
        float hopVy = await MaxOver(() => _avatar.Velocity.Y, 8);
        await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 5);
        float rideMoveSpeed = MotorTuning.Current.MoveSpeed;
        BikeCheck("mount_blends_tuning_in",
            bike.Mounted && bike.Blend >= 1f
            && speedAfterOneTick > footAtStart.MoveSpeed && speedAfterOneTick < footAtStart.MoveSpeed * b.RideSpeedMul
            && Mathf.Abs(rideMoveSpeed - footAtStart.MoveSpeed * b.RideSpeedMul) < 1e-3f,
            $"MoveSpeed two ticks in {speedAfterOneTick:F3} (foot {footAtStart.MoveSpeed:F2}), settled {rideMoveSpeed:F3} "
          + $"(ride {footAtStart.MoveSpeed * b.RideSpeedMul:F2}), blend {bike.Blend:F2}");
        BikeCheck("ground_mount_hops_on",
            hopVy > b.MountHopMps * 0.6f,
            $"peak vY {hopVy:F2} m/s within 8 ticks of the mount (hop {b.MountHopMps:F1})");

        // 2. Riding is faster than running. ----------------------------------------------------
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(120);
        float rideSpeed = HSpeed();
        BikeCheck("ride_is_faster_than_the_foot_cap",
            rideSpeed > footCap + 1.0f,
            $"ride {rideSpeed:F2} m/s after 2 s vs foot cap {footCap:F2} (ride cap {rideCap:F2})");

        // 3. A rolling dismount over the cap clamps and stumbles; with the switch off it hops. ----
        bike.PressMount();
        await Ticks(1);
        float clamped = HSpeed();
        bool stumbling = bike.Stumbling;
        bool stillStumbling = await WaitUntil(() => !bike.Stumbling, Mathf.CeilToInt(b.StumbleSec * 60f) + 10);
        BikeCheck("rolling_dismount_stumbles",
            !bike.Mounted && stumbling && clamped <= footCap * b.StumbleSpeedFraction + 0.05f && stillStumbling,
            $"speed {rideSpeed:F2} -> {clamped:F2} (cap x {b.StumbleSpeedFraction:F2} = {footCap * b.StumbleSpeedFraction:F2}), "
          + $"stumbling {stumbling}, cleared after {b.StumbleSec:F2} s: {stillStumbling}");

        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.PressMount();
        brain.Current = Go(fwd);
        await Ticks(120);
        float before = HSpeed();
        BikeLayer.SetStumbleEnabled(false);
        bike.PressMount();
        await Ticks(1);
        float afterOff = HSpeed();
        float hopOff = await MaxOver(() => _avatar.Velocity.Y, 6);
        BikeCheck("stumble_off_hops_off_at_full_speed",
            !bike.Mounted && !bike.Stumbling && afterOff > footCap && hopOff > b.DismountHopMps * 0.5f,
            $"speed {before:F2} -> {afterOff:F2} (foot cap {footCap:F2}), hop-off peak vY {hopOff:F2}");
        BikeLayer.SetStumbleEnabled(true);

        // 4. A mid-air mount bursts, once. ----------------------------------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(40);
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        bool falling = await WaitUntil(() => !_avatar.IsOnFloor() && _avatar.Velocity.Y < 0f, 60);
        float vyBefore = _avatar.Velocity.Y;
        bike.PressMount();
        float vyAfter = await MaxOver(() => _avatar.Velocity.Y, 3);
        BikeCheck("air_mount_bursts",
            falling && bike.Mounted && vyAfter > vyBefore + 4f && vyAfter > b.AirMountUpMps - 1.0f,
            $"vY {vyBefore:F2} -> peak {vyAfter:F2} (burst {b.AirMountUpMps:F1}), mounted {bike.Mounted}");
        // A second press in the same airtime is a dismount, not a second burst.
        bool landed = await WaitUntil(() => _avatar.IsOnFloor(), 180);
        BikeCheck("air_mount_lands_still_mounted",
            landed && bike.Mounted,
            $"landed {landed}, mounted {bike.Mounted}");

        // 5. A dismount right after touchdown bursts. --------------------------------------------
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        await WaitUntil(() => !_avatar.IsOnFloor(), 30);
        await WaitUntil(() => _avatar.IsOnFloor(), 180);
        bike.PressMount();                                   // inside the landing window
        float landVy = await MaxOver(() => _avatar.Velocity.Y, 3);
        BikeCheck("landing_dismount_bursts",
            !bike.Mounted && landVy > b.LandDismountUpMps - 1.0f && !bike.Stumbling,
            $"peak vY {landVy:F2} after the press (burst {b.LandDismountUpMps:F1}), mounted {bike.Mounted}, stumbling {bike.Stumbling}");
        await WaitUntil(() => _avatar.IsOnFloor(), 180);
        await WaitUntil(() => bike.Blend <= 0f, 60);

        // 6. The drift swings the nose and keeps momentum. --------------------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.PressMount();
        brain.Current = Go(fwd);
        await Ticks(120);
        float h0 = HeadingDeg();
        float s0 = HSpeed();
        brain.Current = Go(new Vector3(1f, 0f, 0f), drift: true);   // stick hard right, drift held
        await Ticks(15);
        float h1 = HeadingDeg();
        float s1 = HSpeed();
        float floor = rideCap * b.DriftSpeedFloorFraction;
        BikeCheck("drift_swings_the_nose_and_keeps_speed",
            bike.Mounted && Mathf.Abs(Mathf.Wrap(h1 - h0, -180f, 180f)) >= 40f && s1 >= floor - 0.1f,
            $"heading {h0:F1} -> {h1:F1} deg in 0.25 s (240 deg/s tuned), speed {s0:F2} -> {s1:F2} (floor {floor:F2})");
        brain.Current = Go(fwd);
        await Ticks(30);
        // Control: the same stick, no drift.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(120);
        float c0 = HeadingDeg(); float cs0 = HSpeed();
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        await Ticks(15);
        GD.Print($"[bike-selftest] control (mounted, no drift): heading {c0:F1} -> {HeadingDeg():F1}, speed {cs0:F2} -> {HSpeed():F2}");
        brain.Current = MoveIntent.None;
        bike.PressMount();
        await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);

        // 7. The slope pays while mounted, and not on foot. -------------------------------------
        await TeleportWorld(plateau);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(150);
        float footOnSlope = HSpeed();
        await TeleportWorld(plateau);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.PressMount();
        brain.Current = Go(fwd);
        await Ticks(150);
        float rideOnSlope = HSpeed();
        BikeCheck("slope_pays_only_while_mounted",
            rideOnSlope > rideCap + 0.8f && footOnSlope < footCap + 0.5f,
            $"after 2.5 s down the 12 deg opener: on foot {footOnSlope:F2} (cap {footCap:F2}), "
          + $"mounted {rideOnSlope:F2} (ride cap {rideCap:F2}, slope bonus {bike.SlopeBonusMps:F2}, "
          + $"downhill sin {bike.DownhillSin:F3})");
        brain.Current = MoveIntent.None;
        bike.PressMount();
        await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);

        // 8. Right mouse on foot slides; SPACE out of the slide is taller. -------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(60);
        brain.Current = Go(fwd, drift: true);                 // RMB held on foot = the crouch button
        bool slid = await WaitUntil(() => _avatar.VerbNow == MoveVerb.Slide, 30);
        MoveVerb verbSeen = _avatar.VerbNow;
        brain.Current = Go(fwd, jump: true, drift: true);
        await Ticks(1);
        brain.Current = Go(fwd, drift: true);
        float slideJumpVy = await MaxOver(() => _avatar.Velocity.Y, 3);
        brain.Current = MoveIntent.None;
        await WaitUntil(() => _avatar.IsOnFloor(), 180);
        // Control: a plain standing sprint jump.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(60);
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        float plainJumpVy = await MaxOver(() => _avatar.Velocity.Y, 3);
        BikeCheck("slide_jump_is_taller",
            slid && slideJumpVy > plainJumpVy * (1f + (b.SlideJumpMul - 1f) * 0.6f),
            $"verb {verbSeen}, slide-jump peak vY {slideJumpVy:F2} vs plain {plainJumpVy:F2} (x{b.SlideJumpMul:F2} tuned)");
        await WaitUntil(() => _avatar.IsOnFloor(), 180);

        // 9. The hold-to-sprint ramp climbs from the jog to the sprint. ------------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.HoldRampEnabled = true;
        brain.Current = Go(fwd, sprint: false);
        await Ticks(18);
        float early = HSpeed();
        await Ticks(72);
        float late = HSpeed();
        bike.HoldRampEnabled = false;
        brain.Current = MoveIntent.None;
        await Ticks(30);
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd, sprint: false);
        await Ticks(90);
        float jog = HSpeed();
        BikeCheck("hold_ramp_climbs_jog_to_sprint",
            early < late && late > footCap - 0.5f && jog < late - 1.0f,
            $"ramp: {early:F2} m/s at 0.3 s, {late:F2} at 1.5 s; plain jog {jog:F2} (cap {footCap:F2})");
        brain.Current = MoveIntent.None;

        // ==== BIKE-1b (2026-09-01): the double jumps, the kick-off, the prediction, the park. ====
        BikeParkCourse park = _courses.OfType<BikeParkCourse>().First();
        Vector3 parkOrigin = park.GlobalPosition;
        Vector3 lift = new(0f, 0.6f, 0f);

        // 11. SPACE in the air while riding is the double jump ON the bike. ------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.PressMount();
        brain.Current = Go(fwd);
        await Ticks(60);
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        bool fallingOnBike = await WaitUntil(() => !_avatar.IsOnFloor() && _avatar.Velocity.Y < -1f, 60);
        float vyBeforeAirJump = _avatar.Velocity.Y;
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        float vyAirJump = await MaxOver(() => _avatar.Velocity.Y, 3);
        float expectedAirJump = footAtStart.JumpVelocity * b.RideJumpMul * footAtStart.AirJumpVelocityFraction * b.RideAirJumpMul;
        BikeCheck("double_jump_on_the_bike",
            fallingOnBike && bike.Mounted && vyAirJump > expectedAirJump * 0.8f && vyAirJump > vyBeforeAirJump + 4f,
            $"vY {vyBeforeAirJump:F2} -> {vyAirJump:F2} on the second SPACE (expected ~{expectedAirJump:F2}, "
          + $"AirJumpMode {footAtStart.AirJumpMode:F0} x{footAtStart.AirJumpCountMax:F0}), still mounted {bike.Mounted}");
        await WaitUntil(() => _avatar.IsOnFloor(), 240);

        // 12. Q high in the air while riding is the kick-off: the double jump OFF the bike. ---------
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        bool atApex = await WaitUntil(() => !_avatar.IsOnFloor() && _avatar.Velocity.Y <= 0.5f, 60);
        float vyAtPress = _avatar.Velocity.Y;
        bike.PressMount();
        await Ticks(1);
        string kickEvent = bike.LastEvent;
        float vyKick = await MaxOver(() => _avatar.Velocity.Y, 3);
        BikeCheck("kick_off_high_in_the_air",
            atApex && !bike.Mounted && !bike.DismountArmed && vyKick >= b.KickOffUpMps - 0.5f && kickEvent.StartsWith("KICK-OFF"),
            $"pressed at vY {vyAtPress:F2}: {kickEvent}; vY after {vyKick:F2} (kick {b.KickOffUpMps:F1}), mounted {bike.Mounted}");
        await WaitUntil(() => _avatar.IsOnFloor(), 240);
        await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);

        // 13. Q just above the floor arms the landing dismount instead, and it bursts on touchdown. --
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        float floorY = _avatar.GlobalPosition.Y;
        bike.PressMount();
        brain.Current = Go(fwd);
        await Ticks(60);
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        await WaitUntil(() => !_avatar.IsOnFloor(), 30);
        // Wait until the fall will reach the floor inside the window at the current speed alone
        // (gravity only brings it closer), then press.
        bool low = await WaitUntil(() => _avatar.Velocity.Y < 0f
            && (_avatar.GlobalPosition.Y - floorY) <= -_avatar.Velocity.Y * b.LandDismountWindowSec * 0.8f, 120);
        float heightAtPress = _avatar.GlobalPosition.Y - floorY;
        float vyAtArm = _avatar.Velocity.Y;
        bike.PressMount();
        await Ticks(1);
        bool armed = bike.DismountArmed && bike.Mounted;
        string armEvent = bike.LastEvent;
        bool landedArmed = await WaitUntil(() => _avatar.IsOnFloor(), 60);
        float vyLandBurst = await MaxOver(() => _avatar.Velocity.Y, 3);
        BikeCheck("landing_dismount_predicted_before_touchdown",
            low && armed && landedArmed && !bike.Mounted && vyLandBurst >= b.LandDismountUpMps - 1f,
            $"pressed {heightAtPress:F2} m up falling at {vyAtArm:F2} m/s: {armEvent}; armed {armed}; "
          + $"burst on touchdown vY {vyLandBurst:F2} (tuned {b.LandDismountUpMps:F1})");
        await WaitUntil(() => _avatar.IsOnFloor(), 240);
        await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);

        // 14. The curb ladder: what rolls over, on foot and on the bike. A TABLE, then two sanity rows.
        var curbRows = new List<string>();
        bool[] footPass = new bool[BikeParkCourse.CurbHeightsM.Length];
        bool[] bikePass = new bool[BikeParkCourse.CurbHeightsM.Length];
        for (int i = 0; i < BikeParkCourse.CurbHeightsM.Length; i++)
        {
            float curbZ = parkOrigin.Z + park.CurbZ(i);
            Vector3 start = parkOrigin + park.CurbLaneStartLocal + lift;
            start.Z = curbZ - 7f;
            for (int mode = 0; mode < 2; mode++)
            {
                await TeleportWorld(start);
                await WaitUntil(() => _avatar.IsOnFloor(), 60);
                if (mode == 1)
                {
                    bike.PressMount();
                    await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 20);
                }
                brain.Current = Go(fwd);
                bool over = await WaitUntil(() => _avatar.GlobalPosition.Z > curbZ + 2.5f, 180);
                float reachedZ = _avatar.GlobalPosition.Z - curbZ;
                brain.Current = MoveIntent.None;
                if (mode == 0) footPass[i] = over; else bikePass[i] = over;
                curbRows.Add($"{BikeParkCourse.CurbHeightsM[i]:F2} m {(mode == 0 ? "foot" : "bike")}: {(over ? "OVER" : "STOPPED")} ({reachedZ:+0.00;-0.00} m past the face after 3 s)");
                if (mode == 1)
                {
                    bike.PressMount();
                    await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);
                }
            }
        }
        foreach (string row in curbRows)
            GD.Print($"[bike-selftest] curb {row}");
        bool monotoneFoot = true, monotoneBike = true;
        for (int i = 1; i < footPass.Length; i++)
        {
            if (footPass[i] && !footPass[i - 1]) monotoneFoot = false;
            if (bikePass[i] && !bikePass[i - 1]) monotoneBike = false;
        }
        int lastFoot = Array.LastIndexOf(footPass, true);
        int lastBike = Array.LastIndexOf(bikePass, true);
        BikeCheck("curb_ladder_the_bike_rolls_over_more_than_the_foot",
            monotoneFoot && monotoneBike && !footPass[^1] && !bikePass[^1] && lastBike > lastFoot
            && lastBike >= 0 && BikeParkCourse.CurbHeightsM[lastBike] <= b.RideStepUpM + 1e-3f,
            $"tallest curb cleared on foot {(lastFoot < 0 ? "none" : BikeParkCourse.CurbHeightsM[lastFoot].ToString("F2") + " m")}, "
          + $"on the bike {(lastBike < 0 ? "none" : BikeParkCourse.CurbHeightsM[lastBike].ToString("F2") + " m")}; "
          + $"monotone foot {monotoneFoot} bike {monotoneBike}; the 1.00 m curb stops both: {!footPass[^1] && !bikePass[^1]}; "
          + $"step-up limit {b.RideStepUpM:F2} m");

        // 15. The kicker ladder: leaving an uphill lip launches, and steeper flies higher. --------
        var kickerRows = new List<string>();
        float[] launchVy = new float[BikeParkCourse.KickerAnglesDeg.Length];
        float[] apexAboveLip = new float[BikeParkCourse.KickerAnglesDeg.Length];
        float[] flightM = new float[BikeParkCourse.KickerAnglesDeg.Length];
        for (int i = 0; i < BikeParkCourse.KickerAnglesDeg.Length; i++)
        {
            Vector3 lipLocal = park.KickerLipLocal(i);
            float lipZ = parkOrigin.Z + lipLocal.Z;
            float lipY = parkOrigin.Y + lipLocal.Y;
            await TeleportWorld(parkOrigin + park.KickerStartLocal(i) + lift);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
            brain.Current = Go(fwd);
            bool left = await WaitUntil(() => _avatar.GlobalPosition.Z > lipZ - 1.5f && !_avatar.IsOnFloor(), 400);
            float speedAtLip = HSpeed();
            launchVy[i] = _avatar.Velocity.Y;
            string launchEvent = bike.LastEvent;
            float apexY = _avatar.GlobalPosition.Y;
            bool down = false;
            for (int t = 0; t < 300 && !down; t++)
            {
                apexY = Mathf.Max(apexY, _avatar.GlobalPosition.Y);
                await Ticks(1);
                down = _avatar.IsOnFloor();
            }
            apexAboveLip[i] = apexY - lipY;
            flightM[i] = _avatar.GlobalPosition.Z - lipZ;
            brain.Current = MoveIntent.None;
            kickerRows.Add($"{BikeParkCourse.KickerAnglesDeg[i]:F0} deg: left {left} at {speedAtLip:F2} m/s, launch vY {launchVy[i]:F2}, "
                + $"apex {apexAboveLip[i]:F2} m above the lip, landed {flightM[i]:F1} m out ({launchEvent})");
            bike.PressMount();
            await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);
        }
        // Control: the 20 deg kicker with the launch switched off is a table.
        {
            int i = 1;
            Vector3 lipLocal = park.KickerLipLocal(i);
            float lipZ = parkOrigin.Z + lipLocal.Z;
            bike.SetTuning(b with { RampLaunchGain = 0f });
            await TeleportWorld(parkOrigin + park.KickerStartLocal(i) + lift);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
            brain.Current = Go(fwd);
            await WaitUntil(() => _avatar.GlobalPosition.Z > lipZ - 1.5f && !_avatar.IsOnFloor(), 400);
            float controlVy = _avatar.Velocity.Y;
            brain.Current = MoveIntent.None;
            await WaitUntil(() => _avatar.IsOnFloor(), 300);
            bike.PressMount();
            await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);
            bike.SetTuning(b);
            kickerRows.Add($"20 deg with RampLaunchGain 0 (control): launch vY {controlVy:F2} - the shipped motor's own lip");
            foreach (string row in kickerRows)
                GD.Print($"[bike-selftest] kicker {row}");
            float sin20 = Mathf.Sin(Mathf.DegToRad(20f));
            BikeCheck("kicker_ladder_launches_and_steeper_flies_higher",
                launchVy[1] > rideCap * sin20 * 0.6f && launchVy[2] > launchVy[0] && apexAboveLip[2] > apexAboveLip[0]
                && controlVy < 1.0f,
                $"launch vY by angle [{string.Join(", ", launchVy.Select(v => v.ToString("F2")))}], "
              + $"apex above lip [{string.Join(", ", apexAboveLip.Select(v => v.ToString("F2")))}] m, "
              + $"flight [{string.Join(", ", flightM.Select(v => v.ToString("F1")))}] m; control (gain 0) vY {controlVy:F2}");
        }

        // 16. A burst gets its whole arc: the air-mount burst's apex, with the hold and without. -----
        float[] apexGain = new float[2];
        for (int mode = 0; mode < 2; mode++)
        {
            bike.SetTuning(b with { BurstFullArc = mode == 0 });
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            brain.Current = Go(fwd);
            await Ticks(40);
            brain.Current = Go(fwd, jump: true);
            await Ticks(1);
            brain.Current = Go(fwd);
            await WaitUntil(() => !_avatar.IsOnFloor() && _avatar.Velocity.Y < 0f, 60);
            float yAtMount = _avatar.GlobalPosition.Y;
            bike.PressMount();
            float top = yAtMount;
            for (int t = 0; t < 120; t++)
            {
                await Ticks(1);
                top = Mathf.Max(top, _avatar.GlobalPosition.Y);
                if (_avatar.Velocity.Y < 0f && t > 2) break;
            }
            apexGain[mode] = top - yAtMount;
            brain.Current = MoveIntent.None;
            await WaitUntil(() => _avatar.IsOnFloor(), 240);
            bike.PressMount();
            await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);
        }
        bike.SetTuning(b);
        float fullArcM = b.AirMountUpMps * b.AirMountUpMps / (2f * footAtStart.Gravity);
        float cutArcM = b.AirMountUpMps * b.AirMountUpMps / (2f * footAtStart.Gravity * footAtStart.JumpReleaseGravityMultiplier);
        BikeCheck("bursts_get_their_whole_arc",
            apexGain[0] > fullArcM * 0.85f && apexGain[1] < apexGain[0] * 0.6f,
            $"air-mount burst {b.AirMountUpMps:F1} m/s rose {apexGain[0]:F2} m with BurstFullArc (ballistic {fullArcM:F2}), "
          + $"{apexGain[1]:F2} m without (jump-cut at x{footAtStart.JumpReleaseGravityMultiplier:F1}: {cutArcM:F2})");

        // 17. A bike preset pressed while riding changes the ride you are on. ----------------------
        // Each leg from a fresh start: 120 + 150 ticks at bike speed is longer than the run-out,
        // and the first attempt at this check measured 0.00 m/s against its end wall.
        float baselineRide = 0f, rocketRide = 0f, cruiserRide = 0f;
        BikePresets.Preset rocket = BikePresets.ForKey(2)!.Value;
        BikePresets.Preset cruiser = BikePresets.ForKey(1)!.Value;
        foreach (int leg in new[] { 0, 2, 1 })
        {
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            bike.SetTuning(b);
            bike.PressMount();
            brain.Current = Go(fwd);
            await Ticks(60);
            if (leg > 0)
                bike.SetTuning(BikePresets.ForKey(leg)!.Value.Tuning);   // pressed MID-RIDE
            await Ticks(120);
            float v = HSpeed();
            if (leg == 0) baselineRide = v; else if (leg == 2) rocketRide = v; else cruiserRide = v;
            brain.Current = MoveIntent.None;
            if (leg != 1)
            {
                bike.PressMount();
                await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);
            }
        }
        bike.SetTuning(b);
        BikeCheck("bike_presets_apply_live_while_riding",
            bike.Mounted && rocketRide > baselineRide + 1.5f && cruiserRide < baselineRide - 0.8f
            && Mathf.Abs(rocketRide - footCap * rocket.Tuning.RideSpeedMul) < 0.3f
            && Mathf.Abs(cruiserRide - footCap * cruiser.Tuning.RideSpeedMul) < 0.3f,
            $"baseline {baselineRide:F2} m/s -> ALT+2 {rocket.Name} {rocketRide:F2} (x{rocket.Tuning.RideSpeedMul:F2} = {footCap * rocket.Tuning.RideSpeedMul:F2}) "
          + $"-> ALT+1 {cruiser.Name} {cruiserRide:F2} (x{cruiser.Tuning.RideSpeedMul:F2} = {footCap * cruiser.Tuning.RideSpeedMul:F2}), mid-ride");
        bike.PressMount();
        await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);

        // ==== BIKE-1c (2026-09-01): the swing, the lunge, the hit, the slingshot. ====

        // 18. A swing in the air lunges forward, once per airtime. ---------------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(40);
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        // Swing on the RISE so the whole swing fits inside one airtime (0.42 s of a ~0.65 s jump)
        // and the second press is still in the air. The first run of this check pressed at the
        // apex, the swing outlived the airtime, and the "second swing" was a ground swing's push.
        await WaitUntil(() => !_avatar.IsOnFloor(), 30);
        float hBeforeLunge = HSpeed();
        bike.PressSwing();
        await Ticks(1);
        float hAfterLunge = HSpeed();
        bool swinging = bike.Swinging;
        await WaitUntil(() => !bike.Swinging, Mathf.CeilToInt(b.SwingSec * 60f) + 5);
        bool stillAirborne = !_avatar.IsOnFloor();
        float hBeforeSecond = HSpeed();
        bike.PressSwing();
        await Ticks(1);
        float hAfterSecond = HSpeed();
        BikeCheck("air_swing_lunges_forward_once",
            swinging && stillAirborne && hAfterLunge > hBeforeLunge + b.SwingLungeForwardMps * 0.7f
            && hAfterSecond < hBeforeSecond + 0.5f,
            $"first swing {hBeforeLunge:F2} -> {hAfterLunge:F2} m/s (+{b.SwingLungeForwardMps:F1} tuned), "
          + $"second swing in the same airtime (airborne {stillAirborne}) {hBeforeSecond:F2} -> {hAfterSecond:F2} (no lunge)");
        brain.Current = MoveIntent.None;
        await WaitUntil(() => _avatar.IsOnFloor(), 240);
        await WaitUntil(() => !bike.Swinging, 60);

        // 19. A swing on the ground meets the dummy. ----------------------------------------------
        Vector3 dummy = parkOrigin + BikeParkCourse.DummyLocal[0];
        Vector3 standoff = dummy + new Vector3(0f, 0.6f, -6f);
        await TeleportWorld(standoff);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        GD.Print($"[bike-selftest] dummy at {dummy}, standoff {standoff}, body now at {_avatar.GlobalPosition} grounded {_avatar.IsOnFloor()}");
        int hitsBefore = bike.SwingHits;
        brain.Current = Go(fwd, sprint: false);
        // Walk at the post; swing when its face is inside the reach, with the mid-swing cast
        // still ahead of the collision.
        bool close = await WaitUntil(() => dummy.Z - _avatar.GlobalPosition.Z <= b.SwingReachM - 0.2f, 240);
        float gapAtPress = dummy.Z - _avatar.GlobalPosition.Z;
        bike.PressSwing();
        await Ticks(1);                               // the scripted press lands on the next intent
        bool swungAtPost = bike.Swinging;
        await WaitUntil(() => bike.SwingHits > hitsBefore || !bike.Swinging, Mathf.CeilToInt(b.SwingSec * 60f) + 5);
        brain.Current = MoveIntent.None;
        BikeCheck("ground_swing_hits_the_dummy",
            close && swungAtPost && bike.SwingHits == hitsBefore + 1 && bike.LastSwingHit.StartsWith("Dummy"),
            $"pressed {gapAtPress:F2} m from the post (reach {b.SwingReachM:F1}), swinging {swungAtPost}; hits {hitsBefore} -> {bike.SwingHits}, last hit {bike.LastSwingHit}");
        await WaitUntil(() => !bike.Swinging, 60);

        // 20. Q mid-swing in the air is the slingshot: more forward than the plain air mount. --------
        float[] fwdGain = new float[2];
        for (int mode = 0; mode < 2; mode++)
        {
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            brain.Current = Go(fwd);
            await Ticks(40);
            brain.Current = Go(fwd, jump: true);
            await Ticks(1);
            brain.Current = Go(fwd);
            await WaitUntil(() => !_avatar.IsOnFloor() && _avatar.Velocity.Y <= 0.5f, 60);
            float sh0 = HSpeed();
            if (mode == 1)
            {
                bike.PressSwing();
                await Ticks(3);
            }
            float sh1 = HSpeed();
            bike.PressMount();
            await Ticks(1);
            fwdGain[mode] = HSpeed() - sh0;
            GD.Print($"[bike-selftest] {(mode == 0 ? "plain air mount" : "slingshot")}: {sh0:F2} -> {sh1:F2} -> {HSpeed():F2} m/s ({bike.LastEvent})");
            brain.Current = MoveIntent.None;
            await WaitUntil(() => _avatar.IsOnFloor(), 240);
            bike.PressMount();
            await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 90);
        }
        BikeCheck("slingshot_outruns_the_plain_air_mount",
            fwdGain[1] > fwdGain[0] + (b.SwingLungeForwardMps + b.SlingshotForwardMps) * 0.6f,
            $"forward gained through the press: plain air mount +{fwdGain[0]:F2} m/s, swing then Q +{fwdGain[1]:F2} "
          + $"(lunge {b.SwingLungeForwardMps:F1} + slingshot {b.SlingshotForwardMps:F1} + mount {b.AirMountForwardMps:F1} tuned)");

        // ============================================================================================
        // BIKE-4A — THE RIDE CHANNEL. Five checks, every one of which FAILS on the tree before this
        // packet, because before it nothing told AvatarVisual a bike existed.
        // ============================================================================================
        await RunRideChannelChecksAsync(bike, brain, b, runOut, fwd);

        // 10. Everything restores. -------------------------------------------------------------
        await Ticks(30);
        MotorTuning now = MotorTuning.Current;
        string diff = string.Join(", ", MotorTuningKnobs.All
            .Where(k => !Mathf.IsEqualApprox(k.Get(now), k.Get(footAtStart)))
            .Select(k => $"{k.Name} {k.Get(footAtStart):F3}->{k.Get(now):F3}"));
        // Every float row, by reflection, bit for bit - the knob table does not cover all of them.
        string bits = string.Join(", ", typeof(MotorTuning).GetProperties()
            .Where(pi => pi.PropertyType == typeof(float) && pi.CanWrite)
            .Select(pi => (pi.Name, a: (float)pi.GetValue(footAtStart)!, c: (float)pi.GetValue(now)!))
            .Where(t => BitConverter.SingleToInt32Bits(t.a) != BitConverter.SingleToInt32Bits(t.c))
            .Select(t => $"{t.Name} {t.a:R}->{t.c:R}"));
        bool restored = !bike.Mounted && bike.Blend <= 0f && bits.Length == 0;
        BikeCheck("tuning_restored_to_the_foot_tuning",
            restored,
            $"mounted {bike.Mounted}, blend {bike.Blend:F2}, knob rows that differ: [{diff}], "
          + $"any float row differing by a bit: [{bits}]");

        int passed = _bikeChecks.Count(c => c.pass);
        bool ok = passed == _bikeChecks.Count;
        GD.Print($"BIKE-SELFTEST OVERALL: {(ok ? "PASS" : "FAIL")} ({passed}/{_bikeChecks.Count})");
        await Ticks(5);
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// <b>BIKE-4A — the rider stops running, measured rather than looked at.</b> Every check here
    /// asks the body itself what pose it is in, through the readouts <c>AvatarVisual</c> exposes for
    /// exactly this purpose, so "the legs are on the cranks" is a number in a headless run before it
    /// is a still frame in <c>docs/qa/</c>.
    ///
    /// <para><b>Each of the five would fail on the tree this packet branched from</b>, and the first
    /// one carries its own NEGATIVE CONTROL to prove the instrument can read both answers: it takes
    /// the gear at the ride cap ON the bike and OFF it, in the same run, at the same speed band. A
    /// check that has only ever seen one answer is not known to be able to see the other.</para>
    /// </summary>
    private async Task RunRideChannelChecksAsync(
        BikeLayer bike, ScriptedInput brain, BikeTuning b, Vector3 runOut, Vector3 fwd)
    {
        AvatarVisual visual = _avatar.Visual;

        // R1. THE FIELD NOTE, ANSWERED. Mounted at the ride cap the body must not be in Sprint gait
        //     and must not be running a sprint cadence. The unmounted reading at a comparable speed
        //     is taken first, in the same run, as the control.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(150);
        float footSpeed = HSpeed();
        Gear footGear = visual.Gear;
        float footCadence = visual.CadenceHz;

        bike.PressMount();
        await WaitUntil(() => bike.Blend >= 1f, 90);
        await Ticks(180);                                  // long enough to reach the ride cap
        float rideSpeed = HSpeed();
        Gear rideGear = visual.Gear;
        float rideCadence = visual.CadenceHz;
        float rideWeight = visual.RideWeightForTools;
        float crankHz = visual.CrankHz;
        BikeCheck("ride_at_the_cap_is_not_sprint_gait",
            footGear == Gear.Sprint && footCadence > 3f          // the control CAN read a sprint
            && rideSpeed > footSpeed                             // and the bike really is faster
            && rideGear != Gear.Sprint && rideCadence < 0.01f
            && rideWeight >= 1f && crankHz > 0f,
            $"ON FOOT at {footSpeed:F2} m/s: gear {footGear}, cadence {footCadence:F2} steps/s. "
          + $"MOUNTED at {rideSpeed:F2} m/s: gear {rideGear}, gait cadence {rideCadence:F2} steps/s, "
          + $"ride weight {rideWeight:F2}, cranks {crankHz:F2} rev/s "
          + $"(rule says {RidePose.CrankHzAt(rideSpeed):F2})");

        // R2. THE CADENCE RULE. Sampled DURING one acceleration from rest to the cap, so the two
        //     readings are genuinely different non-zero speeds on the same ride rather than two
        //     readings of a stopped body — the first pass of this check sampled a body that had
        //     coasted all the way to 0.00 m/s and passed on a degenerate 0 < 1.60, which is a check
        //     that would have gone on passing with the rule deleted.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        await Ticks(20);
        float atRestSpeed = HSpeed();
        float atRestCrank = visual.CrankHz;                    // mounted, stationary: cranks still
        brain.Current = Go(fwd);
        // Past the mode hysteresis, then a first sample while the body is still building speed.
        await Ticks(Mathf.CeilToInt(RidePose.ModeHoldSec * 60f) + 8);
        float slowSpeed = HSpeed();
        float slowCrank = visual.CrankHz;
        bool slowMatchesRule = Mathf.IsEqualApprox(slowCrank, RidePose.CrankHzAt(slowSpeed), 0.02f);
        await Ticks(180);                                      // out to the cap
        float fastSpeed = HSpeed();
        float fastCrank = visual.CrankHz;
        bool fastMatchesRule = Mathf.IsEqualApprox(fastCrank, RidePose.CrankHzAt(fastSpeed), 0.02f);
        // The proportionality itself, not just two rule matches: twice the speed is twice the rate.
        bool proportional = slowSpeed > 0.5f
            && Mathf.IsEqualApprox(fastCrank / slowCrank, fastSpeed / slowSpeed, 0.02f);
        brain.Current = MoveIntent.None;
        BikeCheck("ride_crank_rate_is_the_cadence_rule_and_zero_at_rest",
            atRestCrank == 0f && slowMatchesRule && fastMatchesRule && proportional
            && slowSpeed > 0.5f && fastSpeed > slowSpeed + 1f,
            $"at rest {atRestSpeed:F3} m/s -> {atRestCrank:F3} rev/s; "
          + $"building {slowSpeed:F2} m/s -> {slowCrank:F3} (rule {RidePose.CrankHzAt(slowSpeed):F3}); "
          + $"at the cap {fastSpeed:F2} m/s -> {fastCrank:F3} (rule {RidePose.CrankHzAt(fastSpeed):F3}); "
          + $"rate ratio {fastCrank / Mathf.Max(slowCrank, 1e-6f):F3} vs speed ratio "
          + $"{fastSpeed / Mathf.Max(slowSpeed, 1e-6f):F3}. "
          + $"{RidePose.CrankMetersPerRev:F3} m of ground per crank revolution "
          + $"(2pi x {RidePose.WheelRadiusM:F2} m x {RidePose.WheelRevsPerCrankRev:F2})");

        // R3. PEDAL vs COAST are two poses, and the hysteresis is what keeps them from flickering.
        //     Driving: the mode is PEDAL, the cranks turn, the torso is down over the bars.
        //     Coasting: the mode is COAST, the cranks stop at 3-and-9, the torso comes up.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(120);
        RideMode pedalMode = visual.RideModeForTools;
        float pedalTilt = visual.BodyTiltX;
        float phaseA = visual.CrankPhase01ForTools;
        await Ticks(6);
        bool cranksMoved = !Mathf.IsEqualApprox(phaseA, visual.CrankPhase01ForTools, 1e-4f);

        brain.Current = MoveIntent.None;
        await Ticks(Mathf.CeilToInt(RidePose.ModeHoldSec * 60f) + 40);
        RideMode coastMode = visual.RideModeForTools;
        float coastTilt = visual.BodyTiltX;
        float coastPhase = visual.CrankPhase01ForTools;
        float phaseB = coastPhase;
        await Ticks(6);
        bool cranksStopped = Mathf.IsEqualApprox(phaseB, visual.CrankPhase01ForTools, 1e-4f);
        // "Level" is phase 0 or 0.5 — one pedal forward, one back, both at the same height.
        float toLevel = Mathf.Min(
            Mathf.Min(coastPhase, 1f - coastPhase), Mathf.Abs(coastPhase - 0.5f));
        BikeCheck("ride_pedal_and_coast_are_two_poses",
            pedalMode == RideMode.Pedal && coastMode == RideMode.Coast
            && cranksMoved && cranksStopped && toLevel < 0.01f
            // The rig's forward is NEGATIVE, so a deeper pitch is a more negative tilt.
            && pedalTilt < coastTilt - 0.05f,
            $"PEDAL: mode {pedalMode}, cranks advancing {cranksMoved}, body tilt {pedalTilt:F3} rad. "
          + $"COAST: mode {coastMode}, cranks held {cranksStopped} at phase {coastPhase:F3} "
          + $"({toLevel:F3} from level), body tilt {coastTilt:F3} rad. "
          + $"Hysteresis {RidePose.ModeHoldSec:F2} s each way");

        // R4. AIRBORNE (3B S2). Leaving the floor mounted freezes the cranks level — an airborne
        //     wheel is geared to nothing — while the ride weight stays on the body.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(90);
        brain.Current = Go(fwd, jump: true);
        await Ticks(1);
        brain.Current = Go(fwd);
        await WaitUntil(() => !_avatar.IsOnFloor(), 30);
        await Ticks(8);
        bool airborne = !_avatar.IsOnFloor();
        float airCrank = visual.CrankHz;
        float airWeight = visual.RideWeightForTools;
        float airSpeed = HSpeed();
        BikeCheck("ride_airborne_freezes_the_cranks_and_keeps_the_weight",
            airborne && airSpeed > 1f && airCrank == 0f && airWeight >= 1f,
            $"airborne {airborne} at {airSpeed:F2} m/s: cranks {airCrank:F3} rev/s (grounded the rule "
          + $"would say {RidePose.CrankHzAt(airSpeed):F3}), ride weight {airWeight:F2}, "
          + $"airborne partition scaled to {RidePose.AirAmplitudeFraction:P0}");
        await WaitUntil(() => _avatar.IsOnFloor(), 240);
        brain.Current = MoveIntent.None;

        // R5. THE INCAPACITY PARK (3B section 6.2). The AnimationSuspended early return is above
        //     every writer, so a knocked-out rider must not hold a pedalling pose. The weight goes
        //     to zero; the crank PHASE is deliberately HELD, so a body that comes back up still
        //     riding resumes the stroke it was on.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        brain.Current = Go(fwd);
        await Ticks(120);
        float weightBefore = visual.RideWeightForTools;
        float phaseBefore = visual.CrankPhase01ForTools;
        visual.SetIncapacity(Sail.Game.Failure.IncapacityState.KnockedOut, 1.2f, 0.3f);
        await Ticks(6);
        float weightDown = visual.RideWeightForTools;
        float phaseDown = visual.CrankPhase01ForTools;
        float crankDown = visual.CrankHz;
        visual.SetIncapacity(Sail.Game.Failure.IncapacityState.Active, 1.2f, 0.3f);
        await Ticks(6);
        float weightBack = visual.RideWeightForTools;
        BikeCheck("ride_channel_parks_while_incapacitated",
            weightBefore >= 1f && weightDown == 0f && crankDown == 0f
            && Mathf.IsEqualApprox(phaseBefore, phaseDown, 1e-4f)
            && weightBack >= 1f,
            $"riding at weight {weightBefore:F2}, phase {phaseBefore:F3}; knocked out -> weight "
          + $"{weightDown:F2}, cranks {crankDown:F3} rev/s, phase HELD at {phaseDown:F3}; "
          + $"recovered -> weight {weightBack:F2}");

        brain.Current = MoveIntent.None;
        if (bike.Mounted)
        {
            bike.PressMount();
            await WaitUntil(() => bike.Blend <= 0f && !bike.Stumbling, 120);
        }
        await Ticks(20);
    }
}
