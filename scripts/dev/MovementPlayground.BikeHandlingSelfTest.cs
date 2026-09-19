using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>BIKE-2x, driven headlessly and measured in the engine</b> (2026-09-02).
/// <c>--bike-handling-selftest</c> after the <c>--</c>. A sibling of BIKE-0's
/// <c>--bike-selftest</c>, never a replacement: that one still runs, still prints
/// <c>BIKE-SELFTEST OVERALL</c>, and this packet is explicitly gated out of its physics so its
/// twenty-three checks stay measurements of the state layer rather than of this one.
///
/// <para>Every check here prints
/// <c>BIKE-HANDLING-SELFTEST &lt;name&gt;: PASS|FAIL - &lt;the numbers&gt;</c>, then
/// <c>BIKE-HANDLING-SELFTEST OVERALL</c>, and the process exits 1 on any red.</para>
///
/// <para><b>What it can and cannot prove.</b> <c>BikeHandlingTests</c> and <c>BikeCameraTests</c>
/// already pin the arithmetic without an engine; repeating them here would prove only that the
/// engine can also do arithmetic. So every check below is about something a unit test cannot
/// reach: that the turn curve actually lands in the live <c>MotorTuning</c> through the layer's
/// single writer, that the second-writer bookkeeping survives somebody else editing the same row
/// mid-ride, that the drift charges off a real body's real speed on real geometry, that the exit
/// boost is owed and <b>not applied</b> to the velocity, that the panel binds every row of all
/// three records, that the camera is inert on foot and live mounted, and that the telemetry counts
/// what happened.</para>
///
/// <para>It proves nothing about feel or about the picture. A headless run has no renderer, the
/// lab's ground is untextured checker, and both of those are Talon's to judge with his hands.</para>
/// </summary>
public partial class MovementPlayground
{
    private readonly List<(string name, bool pass, string detail)> _handlingChecks = new();

    private void HandlingCheck(string name, bool pass, string detail)
    {
        _handlingChecks.Add((name, pass, detail));
        GD.Print($"BIKE-HANDLING-SELFTEST {name}: {(pass ? "PASS" : "FAIL")} - {detail}");
    }

    /// <summary>
    /// <b>The entry point, and the reason it is a wrapper.</b> This runs as an un-awaited
    /// <c>Task</c>, so an exception inside it goes nowhere at all: it does not print, it does not
    /// fail the run, and the scene simply keeps ticking. That happened on this packet's first
    /// engine run — a null brain, a swallowed <c>NullReferenceException</c>, and eleven minutes of
    /// a Godot process at 100 % of a core that looked from the outside exactly like a slow test.
    /// A self-test that can hang instead of failing is worse than no self-test, so every path out
    /// of here ends in an explicit exit code.
    /// </summary>
    private async Task RunBikeHandlingSelfTestAsync()
    {
        try
        {
            await BikeHandlingChecksAsync();
        }
        catch (Exception e)
        {
            GD.PrintErr($"BIKE-HANDLING-SELFTEST THREW: {e}");
            GD.Print("BIKE-HANDLING-SELFTEST OVERALL: FAIL (threw before finishing; "
                   + $"{_handlingChecks.Count(c => c.pass)}/{_handlingChecks.Count} checks had run)");
            GetTree().Quit(1);
        }
    }

    private async Task BikeHandlingChecksAsync()
    {
        BikeLayer bike = _bike!;
        ScriptedInput brain = _scripted!;
        DescentCourse descent = _courses.OfType<DescentCourse>().First();
        Vector3 runOut = descent.GlobalPosition + descent.RunOutStartLocal + new Vector3(0f, 0.6f, 2f);
        var fwd = new Vector3(0f, 0f, 1f);

        MotorTuning foot = MotorTuning.Current;
        BikeTuning b = BikeTuning.Current;
        BikeHandlingTuning h = BikeHandlingTuning.Current;
        float rideCap = BikeRig.RideCapMps(foot, b);

        await Ticks(20);
        GD.Print($"[bike-handling-selftest] ride cap {rideCap:F2} m/s, run-out at {runOut}, "
               + $"{_bikeKnobs?.Rows.Count ?? -1} knob rows");

        // 1. The panel binds every float and bool of all three records. -----------------------------
        //    Reflection finds the rows, so the failure this guards against is not "a row is missing
        //    from a list" — it is "a row exists and silently failed to bind", which looks identical
        //    on screen to a row that was never there.
        int expected = BindableCount<BikeTuning>() + BindableCount<BikeHandlingTuning>()
                     + BindableCount<BikeCameraTuning>();
        int found = _bikeKnobs?.Rows.Count ?? 0;
        HandlingCheck("knob_panel_binds_every_row_of_all_three_records",
            found == expected && expected > 0,
            $"{found} bound of {expected} bindable (BikeTuning {BindableCount<BikeTuning>()}, "
          + $"handling {BindableCount<BikeHandlingTuning>()}, camera {BindableCount<BikeCameraTuning>()})");

        // 2. A knob write reaches the LIVE ride, not just the record. ------------------------------
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        bike.PressMount();
        await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        float turnLerpBefore = MotorTuning.Current.TurnLerp;
        BikeKnobPanel.Row? accelRow = _bikeKnobs?.Rows
            .FirstOrDefault(r => r.Group == "BIKE" && r.Name == nameof(BikeTuning.RideAcceleration));
        float accelBefore = MotorTuning.Current.Acceleration;
        accelRow?.Set(accelRow.Get() * 0.5f);
        await Ticks(4);
        float accelAfter = MotorTuning.Current.Acceleration;
        HandlingCheck("a_knob_write_reaches_the_live_ride_mid_ride",
            accelRow is not null && accelAfter < accelBefore - 0.5f,
            $"RideAcceleration knob halved mid-ride: live Acceleration {accelBefore:F2} -> "
          + $"{accelAfter:F2} m/s^2 (blend {bike.Blend:F2})");
        accelRow?.Set(BikeTuning.Default.RideAcceleration);
        await Ticks(4);

        // 3. The turn curve is written into the live ride, and it WIDENS with speed. ---------------
        //    Measured on the same body on the same ground, a standstill against a full run-up.
        brain.Current = MoveIntent.None;
        await Ticks(45);
        float slowTurnLerp = MotorTuning.Current.TurnLerp;
        float slowSpeed = HSpeed();
        brain.Current = Go(fwd);
        await Ticks(150);
        float fastTurnLerp = MotorTuning.Current.TurnLerp;
        float fastSpeed = HSpeed();
        HandlingCheck("the_turn_curve_reaches_the_live_ride_and_widens_with_speed",
            slowTurnLerp > fastTurnLerp + 1e-3f && fastSpeed > slowSpeed + 3f,
            $"TurnLerp {slowTurnLerp:F3} at {slowSpeed:F2} m/s -> {fastTurnLerp:F3} at "
          + $"{fastSpeed:F2} m/s (curve {h.TurnLowSpeedMul:F2}..{h.TurnHighSpeedMul:F2}, "
          + $"ratio {(slowTurnLerp > 0f ? fastTurnLerp / slowTurnLerp : 0f):F3})");

        // 4. Somebody else editing RideTurnMul mid-ride is ADOPTED, not multiplied away. ------------
        //    The two-writer bookkeeping, which is the one piece of this packet that could run away
        //    silently: without the adoption test, one edit compounds with the curve every tick.
        float newBase = BikeTuning.Current.RideTurnMul * 0.5f;
        bike.SetTuning(BikeTuning.Current with { RideTurnMul = newBase });
        await Ticks(120);
        float afterEdit = BikeTuning.Current.RideTurnMul;
        float lo = newBase * Mathf.Min(h.TurnLowSpeedMul, h.TurnHighSpeedMul) - 1e-3f;
        float hi = newBase * Mathf.Max(h.TurnLowSpeedMul, h.TurnHighSpeedMul) + 1e-3f;
        HandlingCheck("an_edit_to_RideTurnMul_mid_ride_is_adopted_not_compounded",
            afterEdit >= lo && afterEdit <= hi,
            $"base set to {newBase:F4}; after 2 s of curve RideTurnMul is {afterEdit:F4}, "
          + $"inside [{lo:F4}, {hi:F4}] (a runaway would be far outside)");

        // 5. A dismount puts the base back, so the next mount does not treat a shaped value as base.
        bike.PressMount();
        await WaitUntil(() => !bike.Mounted && bike.Blend <= 0f, 120);
        await Ticks(10);
        float afterDismount = BikeTuning.Current.RideTurnMul;
        HandlingCheck("the_dismount_restores_the_unshaped_turn_row",
            Mathf.Abs(afterDismount - newBase) < 1e-4f,
            $"RideTurnMul after the dismount {afterDismount:F5}, base {newBase:F5} "
          + $"(a shaped value left here would become the next mount's base)");
        bike.SetTuning(BikeTuning.Default);
        await Ticks(4);

        // 6. Lean: zero on foot, real on a real turn, and signed with the yaw rate. -----------------
        brain.Current = MoveIntent.None;
        await Ticks(60);
        float leanOnFoot = _leanDeg;
        await TeleportWorld(runOut);
        bike.PressMount();
        await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        brain.Current = Go(fwd);
        await Ticks(120);
        // Swing the wish 90 degrees and hold it: the motor turns the body, the yaw rate rises, and
        // the lean is a function of that yaw rate and the speed it is carrying.
        brain.Current = Go(new Vector3(1f, 0f, 0f));
        float peakLean = 0f;
        float peakYaw = 0f;
        for (int i = 0; i < 90; i++)
        {
            if (Mathf.Abs(_leanDeg) > Mathf.Abs(peakLean))
            {
                peakLean = _leanDeg;
                peakYaw = _yawRatePerSec;
            }
            await Ticks(1);
        }
        HandlingCheck("lean_is_zero_on_foot_and_rises_on_a_real_turn",
            Mathf.Abs(leanOnFoot) < 0.05f && Mathf.Abs(peakLean) > 3f
            && Mathf.Abs(peakLean) <= h.LeanMaxDeg + 1e-3f
            && Mathf.Sign(peakLean) == Mathf.Sign(peakYaw),
            $"on foot {leanOnFoot:F3} deg; mounted through a 90 deg wish swing, peak lean "
          + $"{peakLean:F2} deg at yaw {peakYaw:F2} rad/s (max {h.LeanMaxDeg:F0}, "
          + $"signs {(Mathf.Sign(peakLean) == Mathf.Sign(peakYaw) ? "agree" : "DISAGREE")})");

        // 7. The drift charges on a real body and tiers up. -----------------------------------------
        //    Teleported to the TOP of the run-out first, deliberately. The first version of this
        //    block inherited a body that had already run the 46 m run-out out and been caught by
        //    the void floor, so it charged, released and measured at 0.00 m/s — twelve green checks
        //    of which two were green because nothing was moving. The run-out is 44 m from here; a
        //    0.8 s run-up plus a full tier-3 charge at the ride cap is about 24 m, which fits.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        if (!bike.Mounted)
        {
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        }
        brain.Current = Go(fwd);
        await Ticks(50);
        float speedAtDriftEntry = HSpeed();
        int entriesBefore = _driftEntries;
        _scriptedDriftHeld = true;
        int peakTier = 0;
        float peakCharge = 0f;
        for (int i = 0; i < Mathf.CeilToInt(h.DriftTier3Sec * 60f) + 30; i++)
        {
            peakTier = Mathf.Max(peakTier, _driftTierNow);
            peakCharge = Mathf.Max(peakCharge, _drift.ChargeSec);
            await Ticks(1);
        }
        HandlingCheck("the_drift_charges_and_tiers_up_on_a_real_body",
            _driftEntries == entriesBefore + 1 && peakTier >= 3,
            $"entered at {speedAtDriftEntry:F2} m/s (gate {h.DriftEntrySpeedMps:F2}); "
          + $"peak tier {peakTier}, peak charge {peakCharge:F2} s "
          + $"(tiers {h.DriftTier1Sec:F2}/{h.DriftTier2Sec:F2}/{h.DriftTier3Sec:F2})");

        // 8. THE EXIT BOOST IS OWED, NOT APPLIED. --------------------------------------------------
        //    The check that proves this packet keeps its hands off the velocity. The body's speed
        //    across the release tick must not jump by anything like the boost the tier paid.
        float owedBefore = _owedBoostMps;
        float speedBeforeRelease = HSpeed();
        _scriptedDriftHeld = false;
        await Ticks(1);
        float speedAfterRelease = HSpeed();
        await Ticks(3);
        float owedAfter = _owedBoostMps;
        float paid = owedAfter - owedBefore;
        // The body MUST be moving for this check to mean anything. Its first version measured a
        // body that had already run off the run-out and been void-caught: 0.000 -> 0.000 m/s, which
        // satisfies "did not jump by the boost" while proving nothing whatsoever. A green check
        // that cannot go red is worse than no check.
        HandlingCheck("the_drift_exit_boost_is_owed_and_never_written_into_the_velocity",
            paid > 0f && speedBeforeRelease > h.DriftEntrySpeedMps
            && speedAfterRelease - speedBeforeRelease < paid * 0.5f,
            $"tier {_lastBoostTier} paid {paid:F2} m/s into the owed total ({owedAfter:F2}); the "
          + $"body went {speedBeforeRelease:F3} -> {speedAfterRelease:F3} m/s across the release "
          + $"tick, a change of {speedAfterRelease - speedBeforeRelease:+0.000;-0.000} — no impulse "
          + $"seam on this branch, so a jump here would be a second writer of Velocity "
          + $"(and the body was genuinely moving: {speedBeforeRelease:F2} > "
          + $"{h.DriftEntrySpeedMps:F2} m/s gate)");

        // 9. The drift refuses to enter below the gate — ISOLATED to speed. ------------------------
        //    Tested by raising the gate above the ride cap rather than by slowing the body down.
        //    Slowing it would confound the speed gate with "the body is barely cornering at all",
        //    and its first version did exactly that: it reported "held the drift for 2 s at up to
        //    0.00 m/s", which is a true sentence about a body that was parked. Here the body is at
        //    the ride cap, on the floor, mounted, with the button down — every other half of the
        //    entry gate satisfied — and speed is the only thing refusing it.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        if (!bike.Mounted)
        {
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        }
        brain.Current = Go(fwd);
        await Ticks(60);
        BikeHandlingTuning gateRaised = BikeHandlingTuning.Current;
        float unreachableGate = BikeRig.RideCapMps(MotorTuning.Current, BikeTuning.Current) * 3f;
        BikeHandlingTuning.Current = gateRaised with { DriftEntrySpeedMps = unreachableGate };
        int entriesBeforeGate = _driftEntries;
        float fastestSeen = 0f;
        _scriptedDriftHeld = true;
        for (int i = 0; i < 90; i++)
        {
            fastestSeen = Mathf.Max(fastestSeen, HSpeed());
            await Ticks(1);
        }
        _scriptedDriftHeld = false;
        await Ticks(4);
        BikeHandlingTuning.Current = gateRaised;
        HandlingCheck("the_drift_refuses_to_enter_below_the_gate_with_everything_else_satisfied",
            _driftEntries == entriesBeforeGate && !_drift.Active && fastestSeen > 3f,
            $"mounted, grounded, button held, running at up to {fastestSeen:F2} m/s against a gate "
          + $"raised to {unreachableGate:F2} m/s: entries {entriesBeforeGate} -> {_driftEntries}, "
          + "active " + _drift.Active);

        // 10. The camera is inert on foot and live mounted, with finite values. ---------------------
        //     Sampled AT SPEED, so the FOV, distance and look-ahead reported are the ones the
        //     curves actually produce rather than the base values a stationary body would show.
        BikeCamera cam = _bikeCamera!;
        brain.Current = Go(fwd);
        await Ticks(45);
        float camSpeed = HSpeed();
        bool activeMounted = cam.Active;
        float fovMounted = cam.FovNow;
        float lookAheadMounted = cam.LookAheadNow;
        float distanceMounted = cam.DistanceNow;
        brain.Current = MoveIntent.None;
        bike.PressMount();
        await WaitUntil(() => !bike.Mounted && bike.Blend <= 0f, 180);
        await Ticks(20);
        HandlingCheck("the_bike_camera_is_live_mounted_and_inert_on_foot",
            activeMounted && !cam.Active
            && float.IsFinite(fovMounted) && float.IsFinite(lookAheadMounted)
            && float.IsFinite(distanceMounted) && fovMounted > 1f && camSpeed > 3f,
            $"mounted at {camSpeed:F2} m/s: active {activeMounted}, fov {fovMounted:F2} deg, "
          + $"distance {distanceMounted:F2} m, look-ahead {lookAheadMounted:F2} m "
          + $"(base rows {BikeCameraTuning.Current.FovBaseDeg:F0} deg / "
          + $"{BikeCameraTuning.Current.DistanceBaseM:F2} m / "
          + $"{BikeCameraTuning.Current.LookAheadBaseM:F2} m); after the dismount "
          + $"active {cam.Active}, blend {bike.Blend:F2}");

        // 11. The telemetry counted the session. ---------------------------------------------------
        BikeTelemetry telemetry = _bikeTelemetry!;
        HandlingCheck("the_telemetry_counted_the_mounts_the_drifts_and_the_trace",
            telemetry.MountsGround >= 2 && telemetry.DriftEntries >= 1
            && telemetry.PeakDriftTier >= 3 && telemetry.SecondsMounted > 1f
            && telemetry.SecondsOnFoot > 0f,
            $"mounts ground {telemetry.MountsGround} air {telemetry.MountsAir}, dismounts "
          + $"ground {telemetry.DismountsGround} landing {telemetry.DismountsLanding} kick-off "
          + $"{telemetry.DismountsKickOff}, stumbles {telemetry.Stumbles}, drift entries "
          + $"{telemetry.DriftEntries} peak tier {telemetry.PeakDriftTier}, mounted "
          + $"{telemetry.SecondsMounted:F1} s vs on foot {telemetry.SecondsOnFoot:F1} s, "
          + $"{telemetry.TraceRows} trace rows, recording {telemetry.Recording} "
          + $"[{(telemetry.LastError.Length == 0 ? telemetry.FilePath : telemetry.LastError)}]");

        // 12. The handling presets load whole, and the HANDLING OFF control really is off. ----------
        BikeHandlingTuning before = BikeHandlingTuning.Current;
        BikeHandlingTuning.Current = BikeHandlingTuning.ForKey(6)!.Value.Tuning;
        await Ticks(4);
        await TeleportWorld(runOut);
        bike.PressMount();
        await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        brain.Current = Go(new Vector3(1f, 0f, 0.4f));
        float leanUnderOffPreset = 0f;
        float curveUnderOffPreset = 1f;
        for (int i = 0; i < 120; i++)
        {
            leanUnderOffPreset = Mathf.Max(leanUnderOffPreset, Mathf.Abs(_leanDeg));
            curveUnderOffPreset = _turnMulNow;
            await Ticks(1);
        }
        HandlingCheck("the_handling_off_preset_is_a_real_control",
            leanUnderOffPreset < 1e-3f && Mathf.Abs(curveUnderOffPreset - 1f) < 1e-4f,
            $"CTRL+ALT+6 loaded: peak lean over 2 s of hard cornering {leanUnderOffPreset:F5} deg, "
          + $"turn multiplier {curveUnderOffPreset:F5} (both must be inert)");
        BikeHandlingTuning.Current = before;

        // ==== BIKE-3A (2026-09-02): the orientation pass. Three checks, all instruments first —
        // each one was run against the pre-fix tree and went red with the defect's own numbers
        // before the fix was written (the raw figures are in the BIKE-3A report).

        // 13. The greybox yaw tracks the body's real facing through a full circle. -----------------
        //     The instrument that caught the compass needle: FacingOf used to read the pose's
        //     rig-local BodyYaw (the swing's coil-and-whip fidget, a wobble around zero), so the
        //     greybox sat pointed at world -Z while the body rode a circle — this check's max error
        //     on that tree was ~180 deg. Tolerance 10 deg, stated per the packet (<=10): Track reads
        //     the facing on the render clock a frame behind the physics write, and a loaded machine
        //     can stack a dropped frame on top; the defect this pins is 18x the tolerance.
        await TeleportWorld(runOut);
        await WaitUntil(() => _avatar.IsOnFloor(), 60);
        if (!bike.Mounted)
        {
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        }
        brain.Current = Go(fwd);
        await Ticks(40);
        float maxYawErrDeg = 0f;
        float sweptRad = 0f;
        float prevAvatarYaw = _avatar.GlobalRotation.Y;
        int yawSamples = 0;
        var sectorWorst = new float[8];   // worst error per 45-deg heading sector, for the report
        for (int i = 0; i < 700 && sweptRad < Mathf.Tau; i++)
        {
            // Hold the wish left of the LIVE facing, composed from the body's own yaw (never
            // FacingOf — the pre-fix red this check exists for is FacingOf lying).
            float ay = _avatar.GlobalRotation.Y;
            var f = new Vector3(-Mathf.Sin(ay), 0f, -Mathf.Cos(ay));
            Vector3 left = Vector3.Up.Cross(f);
            // 0.45 ~ a 24-deg offset: the first calibration run held 1.2 (~50 deg) and lapped the
            // circle in 32 ticks — too few samples to mean anything per heading sector.
            brain.Current = Go((f + left * 0.45f).Normalized());
            await Ticks(1);
            float nowYaw = _avatar.GlobalRotation.Y;
            sweptRad += Mathf.Abs(Mathf.Wrap(nowYaw - prevAvatarYaw, -Mathf.Pi, Mathf.Pi));
            prevAvatarYaw = nowYaw;
            if (bike.Mounted && _avatar.IsOnFloor() && bike.Blend >= 1f)
            {
                float gbYaw = _bikeMesh!.Rotation.Y;
                float err = Mathf.Abs(Mathf.RadToDeg(Mathf.Wrap(gbYaw - nowYaw, -Mathf.Pi, Mathf.Pi)));
                maxYawErrDeg = Mathf.Max(maxYawErrDeg, err);
                int sector = (int)(Mathf.Wrap(Mathf.RadToDeg(nowYaw), 0f, 360f) / 45f) % 8;
                sectorWorst[sector] = Mathf.Max(sectorWorst[sector], err);
                yawSamples++;
            }
        }
        brain.Current = Go(fwd);
        await Ticks(10);
        HandlingCheck("the_greybox_yaw_tracks_the_body_through_a_full_circle",
            sweptRad >= Mathf.Tau && yawSamples >= 48 && maxYawErrDeg <= 10f,
            $"swept {Mathf.RadToDeg(sweptRad):F0} deg over {yawSamples} mounted-grounded samples; "
          + $"max |greybox yaw - avatar yaw| {maxYawErrDeg:F2} deg (tolerance 10); per-45deg-sector "
          + $"worst [{string.Join(", ", sectorWorst.Select(s => s.ToString("F1")))}]");

        // 14. The lean DRAWS into the turn at a heading far from world -Z. -------------------------
        //     Check 6 pins sign(lean) == sign(yaw rate) — the COMPUTED lean. This one pins the
        //     frame the roll is drawn in: BikeLeanRig rolls about the greybox's local +Z, so if
        //     that axis is stuck near world -Z (defect 13), any heading more than 90 deg away
        //     draws a left lean as a right lean. The drawn tilt is read off the greybox's world
        //     up-vector against the left of the body's live heading; every sample must agree with
        //     the computed lean's sign, in BOTH turn directions, at headings >90 deg from -Z.
        //     No sign in LeanSteadyDeg was wrong and none was changed — this is the frame check.
        int leanAgree = 0, leanDisagree = 0;
        var leanSignsSeen = new HashSet<int>();
        var leanSampleRows = new List<string>();
        foreach (float turnDir in new[] { 1f, -1f })   // +1 = left, -1 = right
        {
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            if (!bike.Mounted)
            {
                bike.PressMount();
                await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
            }
            brain.Current = Go(fwd);   // heading +Z: 180 deg from the pre-fix frozen frame
            await Ticks(90);
            bool rowLogged = false;
            for (int i = 0; i < 50; i++)
            {
                float ay = _avatar.GlobalRotation.Y;
                var f = new Vector3(-Mathf.Sin(ay), 0f, -Mathf.Cos(ay));
                Vector3 left = Vector3.Up.Cross(f);
                brain.Current = Go((f + left * (1.2f * turnDir)).Normalized());
                await Ticks(1);
                float headingFromMinusZDeg = Mathf.Abs(Mathf.Wrap(
                    Mathf.RadToDeg(_avatar.GlobalRotation.Y), -180f, 180f));
                if (Mathf.Abs(_leanDeg) > 3f && headingFromMinusZDeg > 90f
                    && bike.Mounted && _avatar.IsOnFloor())
                {
                    float ay2 = _avatar.GlobalRotation.Y;
                    var heading = new Vector3(-Mathf.Sin(ay2), 0f, -Mathf.Cos(ay2));
                    float tiltLeft = _bikeMesh!.GlobalTransform.Basis.Y.Dot(Vector3.Up.Cross(heading));
                    if (tiltLeft * _leanDeg > 0f) leanAgree++; else leanDisagree++;
                    leanSignsSeen.Add(Mathf.Sign(_leanDeg));
                    if (!rowLogged)
                    {
                        rowLogged = true;
                        leanSampleRows.Add($"{(turnDir > 0f ? "left" : "right")} turn: lean "
                            + $"{_leanDeg:F1} deg drawn tilt-left {tiltLeft:F3} at "
                            + $"{headingFromMinusZDeg:F0} deg from -Z");
                    }
                }
            }
            brain.Current = MoveIntent.None;
            await Ticks(20);
        }
        HandlingCheck("the_lean_draws_into_the_turn_far_from_the_frozen_frame",
            leanAgree >= 10 && leanDisagree == 0 && leanSignsSeen.Count == 2,
            $"{leanAgree} samples agree, {leanDisagree} disagree, both lean signs seen "
          + $"{leanSignsSeen.Count == 2}; [{string.Join("; ", leanSampleRows)}]");

        // 15. The stowed stack lies flat against the back. -----------------------------------------
        //     Riding, the wheels' disc normal is the greybox's +-X (upright, rolling along the
        //     frame). Stowed, it must be +-Z — coin faces toward the wearer's back — or the stack
        //     is fed edge-first into the body: the pre-fix folded branches wrote only an X pitch,
        //     which leaves the normal on +-X in both poses.
        if (!bike.Mounted)
        {
            bike.PressMount();
            await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
        }
        await Ticks(5);
        Vector3 ridingNormal = _bikeMesh!.DiscNormalLocal;
        bike.PressMount();   // stow
        await WaitUntil(() => !bike.Mounted && bike.Blend <= 0f, 180);
        await WaitUntil(() => bike.SinceToggleSec > BikeTuning.Current.FoldSec + 0.15f, 120);
        await Ticks(5);
        Vector3 stowedNormal = _bikeMesh!.DiscNormalLocal;
        HandlingCheck("the_stowed_stack_lies_flat_against_the_back",
            Mathf.Abs(ridingNormal.X) > 0.95f && Mathf.Abs(stowedNormal.Z) > 0.95f,
            $"disc normal riding ({ridingNormal.X:F2}, {ridingNormal.Y:F2}, {ridingNormal.Z:F2}) "
          + $"(need |X| > 0.95: upright), stowed ({stowedNormal.X:F2}, {stowedNormal.Y:F2}, "
          + $"{stowedNormal.Z:F2}) (need |Z| > 0.95: flat against the back)");

        // ==== BIKE-4B (2026-09-02): the low-speed lean wobble. Four checks. Every one of them goes
        //      RED on the base commit ad55ab91, because on that tree the wobble does not exist:
        //      16 measures a variation that is identically zero there, 17 and 18 read fields that
        //      are not declared there, and 19's leg-B fingerprint is identical to leg A's for the
        //      trivial reason that the amplitude row it drags does not exist either. They are
        //      instruments for a feature, not decorations on one.

        // 15b. THE TWO WOBBLE KNOBS ARE ON THE PANEL, AND TUNABLE, AND NO OTHER ANGLE ROW MOVED. ---
        //      Check 1 already proves every row BOUND; this proves the two new ones are reachable
        //      by name, that the amplitude row's slider is narrow enough to nudge (its step is one
        //      per cent of its range, and on the panel's blanket 0..90 for an angle that step would
        //      be 0.9 deg against a 2.5 deg default — a knob whose smallest press is a third of its
        //      own value), and that narrowing it left every angle row that existed before it
        //      exactly as it was.
        {
            BikeKnobPanel.Row? amp = _bikeKnobs?.Rows.FirstOrDefault(r => r.Group == "HANDLING"
                && r.Name == nameof(BikeHandlingTuning.WobbleAmplitudeDeg));
            BikeKnobPanel.Row? fade = _bikeKnobs?.Rows.FirstOrDefault(r => r.Group == "HANDLING"
                && r.Name == nameof(BikeHandlingTuning.WobbleFadeSpeedMps));
            BikeKnobPanel.Row? leanMax = _bikeKnobs?.Rows.FirstOrDefault(r => r.Group == "HANDLING"
                && r.Name == nameof(BikeHandlingTuning.LeanMaxDeg));
            BikeKnobPanel.Row? fovBase = _bikeKnobs?.Rows.FirstOrDefault(r => r.Group == "CAMERA"
                && r.Name == nameof(BikeCameraTuning.FovBaseDeg));
            BikeKnobPanel.Row? fovCap = _bikeKnobs?.Rows.FirstOrDefault(r => r.Group == "CAMERA"
                && r.Name == nameof(BikeCameraTuning.FovAtCapDeg));
            bool anglesUnmoved = leanMax is not null && fovBase is not null && fovCap is not null
                && Mathf.IsEqualApprox(leanMax.Max, 90f) && Mathf.IsEqualApprox(fovBase.Max, 90f)
                && Mathf.IsEqualApprox(fovCap.Max, 90f);
            // A nudge on the live row, then home, so the check proves the WRITE works and not only
            // that a label exists.
            float ampBefore = amp?.Get() ?? float.NaN;
            amp?.Set(ampBefore + (amp?.Step ?? 0f));
            float ampAfter = BikeHandlingTuning.Current.WobbleAmplitudeDeg;
            amp?.Set(ampBefore);
            HandlingCheck("both_wobble_knobs_are_on_the_panel_and_nudgeable",
                amp is not null && fade is not null && anglesUnmoved
                && amp.Step > 0f && amp.Step <= 0.1f
                && Mathf.Abs(ampAfter - ampBefore - amp.Step) < 1e-5f,
                $"HANDLING.WobbleAmplitudeDeg range 0..{amp?.Max ?? float.NaN:F2} step "
              + $"{amp?.Step ?? float.NaN:F3} deg (need a step <= 0.1 or it cannot be tuned); one "
              + $"nudge moved the LIVE record {ampBefore:F3} -> {ampAfter:F3}. "
              + $"HANDLING.WobbleFadeSpeedMps range 0..{fade?.Max ?? float.NaN:F2} step "
              + $"{fade?.Step ?? float.NaN:F3} m/s. Pre-existing angle rows unchanged at 0..90: "
              + $"LeanMaxDeg {leanMax?.Max ?? float.NaN:F0}, FovBaseDeg {fovBase?.Max ?? float.NaN:F0}, "
              + $"FovAtCapDeg {fovCap?.Max ?? float.NaN:F0} ({anglesUnmoved})");
        }

        // 16. THE WOBBLE IS REAL AND IT REACHES THE PICTURE, at walking pace. ----------------------
        //     Sampled off the greybox's WORLD basis, not off the field that drives it: the claim is
        //     that the bike visibly rocks, and a field nobody draws proves nothing. The speed is
        //     held near 1 m/s by a bang-bang wish (sprint off, a fifth of a wish) rather than by a
        //     preset, because the settled speed of a partial wish is a tuning fact that moves.
        {
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            if (!bike.Mounted)
            {
                bike.PressMount();
                await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
            }
            // SETTLE FIRST. The previous check leaves the body facing wherever it stopped, and a
            // wish along world +Z from there snaps the heading round — a yaw rate of several rad/s
            // for one tick, which drives the CORNER lean into the twenties and takes seconds to
            // bleed off through LeanStep's lag. The first cut of this check sampled straight into
            // that decay and measured a 22 deg "wobble" that was a settling corner lean. So: hold
            // the wish until the heading has stopped moving, then wait for the corner lean to come
            // back under a twentieth of a degree, and only then start sampling.
            brain.Current = Go(fwd, sprint: false);
            await Ticks(90);
            brain.Current = MoveIntent.None;
            await WaitUntil(() => Mathf.Abs(_leanDeg) < 0.05f, 300);
            float minTilt = float.MaxValue, maxTilt = float.MinValue;
            float minRoll = float.MaxValue, maxRoll = float.MinValue;
            float peakLeanAtWalk = 0f, minSpeed = float.MaxValue, maxSpeed = 0f;
            int walkSamples = 0, wobbleCrossings = 0;
            float prevWobble = 0f;
            var rollSeries = new List<string>();
            for (int i = 0; i < 240; i++)
            {
                // A bang-bang wish around 1 m/s. 0.3 of a wish rather than a preset, and sprint
                // off: the settled speed of a partial wish is a tuning fact that moves, and a
                // magnitude at or under 0.224 would fall under AvatarMotor's own 0.05 length-squared
                // deadband and stop steering the heading altogether.
                brain.Current = Go(fwd * (HSpeed() < 1.0f ? 0.3f : 0f), sprint: false);
                await Ticks(1);
                float sp = HSpeed();
                // Only samples where the CORNER lean is essentially nothing count: the claim is
                // that a bike at walking pace rocks even when it is not turning, so a sample with a
                // live corner lean in it cannot be evidence for it.
                if (sp < 0.35f || sp > 1.9f || !bike.Mounted || !_avatar.IsOnFloor()
                    || Mathf.Abs(_leanDeg) > 0.5f)
                    continue;
                walkSamples++;
                minSpeed = Mathf.Min(minSpeed, sp);
                maxSpeed = Mathf.Max(maxSpeed, sp);
                peakLeanAtWalk = Mathf.Max(peakLeanAtWalk, Mathf.Abs(_leanDeg));
                float ay = _avatar.GlobalRotation.Y;
                var heading = new Vector3(-Mathf.Sin(ay), 0f, -Mathf.Cos(ay));
                float tiltLeft = _bikeMesh!.GlobalTransform.Basis.Y.Dot(Vector3.Up.Cross(heading));
                minTilt = Mathf.Min(minTilt, tiltLeft);
                maxTilt = Mathf.Max(maxTilt, tiltLeft);
                minRoll = Mathf.Min(minRoll, _rollDeg);
                maxRoll = Mathf.Max(maxRoll, _rollDeg);
                if (prevWobble * _wobbleDeg < 0f) wobbleCrossings++;
                prevWobble = _wobbleDeg;
                if (walkSamples <= 24)
                    rollSeries.Add($"{sp:F2}/{_rollDeg:+0.00;-0.00}");
            }
            brain.Current = MoveIntent.None;
            await Ticks(20);
            float rollSwing = maxRoll - minRoll;
            float tiltSwing = maxTilt - minTilt;
            // The numeric roll series the report quotes, printed here so the evidence and the check
            // come out of the same run rather than out of two.
            GD.Print("[bike-handling-selftest] BIKE-4B roll series at walking pace "
                   + $"(speed m/s / drawn roll deg): {string.Join("  ", rollSeries)}");
            HandlingCheck("the_drawn_roll_rocks_at_walking_pace",
                walkSamples >= 60 && rollSwing > 1.5f && tiltSwing > 0.02f && wobbleCrossings >= 4
                && peakLeanAtWalk <= 0.5f,
                $"{walkSamples} samples between {minSpeed:F2} and {maxSpeed:F2} m/s: drawn roll swung "
              + $"{rollSwing:F2} deg (need > 1.5), the greybox's world tilt swung {tiltSwing:F3} "
              + $"(need > 0.02), the wobble crossed upright {wobbleCrossings} times (need >= 4), and "
              + $"the CORNER lean peaked at only {peakLeanAtWalk:F3} deg over those samples (need "
              + "<= 0.5 by construction, so the swing is the wobble and not steering)");
        }

        // 17. AT RIDE SPEED THE WOBBLE IS GONE — exactly, not nearly. ------------------------------
        //     The claim under test is acceptance criterion 2 in the engine: at and above the fade
        //     speed the drawn roll IS the corner lean, bit for bit. Sampled straight-line, so any
        //     residual swing is the corner lean's own wander on real ground rather than a wobble.
        {
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            if (!bike.Mounted)
            {
                bike.PressMount();
                await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
            }
            brain.Current = Go(fwd);
            await Ticks(150);   // up to the cap, well past the 4.0 m/s fade
            int fastSamples = 0, wobbleNonZero = 0, rollNotLean = 0;
            float minRoll = float.MaxValue, maxRoll = float.MinValue, slowest = float.MaxValue;
            var rollSeries = new List<string>();
            for (int i = 0; i < 120; i++)
            {
                brain.Current = Go(fwd);
                await Ticks(1);
                float sp = HSpeed();
                if (sp < BikeHandlingTuning.Current.WobbleFadeSpeedMps || !bike.Mounted)
                    continue;
                fastSamples++;
                slowest = Mathf.Min(slowest, sp);
                if (!_wobbleDeg.Equals(0f)) wobbleNonZero++;
                if (!_rollDeg.Equals(_leanDeg)) rollNotLean++;
                minRoll = Mathf.Min(minRoll, _rollDeg);
                maxRoll = Mathf.Max(maxRoll, _rollDeg);
                if (fastSamples <= 24)
                    rollSeries.Add($"{sp:F2}/{_rollDeg:+0.00;-0.00}");
            }
            brain.Current = MoveIntent.None;
            await Ticks(20);
            GD.Print("[bike-handling-selftest] BIKE-4B roll series at ride speed "
                   + $"(speed m/s / drawn roll deg): {string.Join("  ", rollSeries)}");
            HandlingCheck("at_ride_speed_the_drawn_roll_is_the_corner_lean_bit_for_bit",
                fastSamples >= 60 && wobbleNonZero == 0 && rollNotLean == 0 && maxRoll - minRoll < 0.5f,
                $"{fastSamples} samples at or above the {BikeHandlingTuning.Current.WobbleFadeSpeedMps:F1} m/s "
              + $"fade (slowest {slowest:F2} m/s): {wobbleNonZero} had a non-zero wobble (need 0), "
              + $"{rollNotLean} had drawn roll != corner lean (need 0), drawn roll swung "
              + $"{maxRoll - minRoll:F3} deg over the straight (need < 0.5)");
        }

        // 18. AMPLITUDE ZERO REPRODUCES TODAY'S TREE EXACTLY. --------------------------------------
        //     Acceptance criterion 3 in the engine: with the knob off, the roll that is drawn is
        //     bit-identical to the corner lean the base commit drew, at every tick of a ride that
        //     spends most of its time under the fade speed. Bit-identical, not close: `Equals`, not
        //     a tolerance.
        {
            BikeHandlingTuning beforeOff = BikeHandlingTuning.Current;
            BikeHandlingTuning.Current = beforeOff with { WobbleAmplitudeDeg = 0f };
            await TeleportWorld(runOut);
            await WaitUntil(() => _avatar.IsOnFloor(), 60);
            if (!bike.Mounted)
            {
                bike.PressMount();
                await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 10);
            }
            int ticks = 0, mismatches = 0, nonZeroWobble = 0, slowTicks = 0;
            for (int i = 0; i < 180; i++)
            {
                brain.Current = Go(fwd * (HSpeed() < 1.2f ? 0.2f : 0f), sprint: false);
                await Ticks(1);
                if (!bike.Mounted) continue;
                ticks++;
                if (HSpeed() < BikeHandlingTuning.Current.WobbleFadeSpeedMps) slowTicks++;
                if (!_rollDeg.Equals(_leanDeg)) mismatches++;
                if (!_wobbleDeg.Equals(0f)) nonZeroWobble++;
            }
            brain.Current = MoveIntent.None;
            await Ticks(10);
            BikeHandlingTuning.Current = beforeOff;
            HandlingCheck("amplitude_zero_draws_exactly_the_corner_lean_and_nothing_else",
                ticks >= 120 && slowTicks >= 100 && mismatches == 0 && nonZeroWobble == 0,
                $"{ticks} mounted ticks ({slowTicks} of them under the fade speed, where a live "
              + $"wobble WOULD show): {mismatches} ticks where the drawn roll differed from the "
              + $"corner lean by even one bit (need 0), {nonZeroWobble} with a non-zero wobble term "
              + "(need 0)");
        }

        // 19. THE ABSENCE CHECK, WITH A POSITIVE CONTROL. ------------------------------------------
        //     Acceptance criterion 5. The claim is that the wobble reaches the visual roll and
        //     NOTHING else, and "I looked and did not see a path" is not evidence — a detector that
        //     has never fired positive is not known to work. So four legs of the SAME scripted ride
        //     from the SAME teleported start, fingerprinted on the simulation's own state (position,
        //     velocity, the live ride's TurnLerp, the turn multiplier, the drift's charge and grip):
        //
        //       A   amplitude 0                          -- the reference
        //       A'  amplitude 0, run again               -- the NOISE FLOOR of this comparison
        //       B   amplitude 20 deg (eight times the default, deep into absurd)
        //       C   amplitude 0, plus ONE real velocity write of the exact form
        //           BikeLayer.PostStep uses (`_avatar.Velocity = <a new Vector3>`, BikeLayer.cs:725
        //           and :737 among others) -- the POSITIVE CONTROL
        //
        //     The check passes only if B is inside the noise floor AND C is far outside it. If C
        //     failed to fire, this check would be a green that proves nothing, so C failing fails
        //     the check.
        {
            BikeHandlingTuning beforeAbs = BikeHandlingTuning.Current;
            // control: 0 = none. 1 = one 0.05 m/s `_avatar.Velocity = <new Vector3>` write, the
            // SENSITIVITY FLOOR. 2 = one `BikeRig.Hop` write, which is verbatim BikeLayer.cs:725 —
            // a named PostStep velocity write, at a size that survives the motor's next re-derive.
            async Task<float[]> Leg(float amplitude, int control)
            {
                BikeHandlingTuning.Current = beforeAbs with { WobbleAmplitudeDeg = amplitude };

                // THE START STATE HAS TO BE IDENTICAL OR THIS CHECK MEASURES NOTHING. The first cut
                // of it teleported, waited on `IsOnFloor`, and composed its wish from the body's
                // LIVE facing — so each leg began pointing wherever the previous leg had stopped,
                // took a different opening turn, and the two amplitude-zero legs ended 26 units
                // apart. That noise floor was larger than the positive control, which is a detector
                // that cannot fire: the check was green-able only by accident. So every leg now
                // starts from a hand-reset pose, waits a FIXED number of ticks rather than on a
                // condition, and drives a WORLD-SPACE wish script that never reads the body back.
                if (bike.Mounted)
                {
                    bike.PressMount();                       // stow
                    await Ticks(Mathf.CeilToInt(b.FoldSec * 60f) + 40);
                }
                brain.Current = MoveIntent.None;
                _scriptedDriftHeld = false;
                await TeleportWorld(runOut);
                _avatar.GlobalRotation = new Vector3(0f, 0f, 0f);
                _avatar.Velocity = Vector3.Zero;
                _prevYawRad = 0f;
                _leanDeg = 0f;
                _wobblePhaseSec = 0f;
                _drift = BikeHandling.DriftState.Rest;
                _driftTierNow = 0;
                await Ticks(40);
                bike.PressMount();
                await Ticks(Mathf.CeilToInt(b.MountBlendSec * 60f) + 20);
                _drift = BikeHandling.DriftState.Rest;
                float owedBefore = _owedBoostMps;

                // A world-space script, identical every leg: a straight run-up, then a hard held
                // turn with the drift down. It exercises every seam the wobble could leak into —
                // the turn curve, the live ride tuning, the drift charge and the velocity — and it
                // does so without ever reading the body's own state back into the input.
                var straight = new Vector3(0f, 0f, 1f);
                var turning = new Vector3(-0.74f, 0f, 0.67f);
                for (int i = 0; i < 120; i++)
                {
                    brain.Current = Go(i < 55 ? straight : turning);
                    _scriptedDriftHeld = i >= 55;
                    await Ticks(1);
                    if (control != 0 && i == 80)
                    {
                        // THE POSITIVE CONTROLS. Both are the statement shape PostStep uses
                        // (`_avatar.Velocity = <a Vector3>`, BikeLayer.cs:551, :663, :725, :737,
                        // :779). The small one is deliberately far under any leak a 20-degree roll
                        // could plausibly cause, so a fire at that size is the detector's FLOOR
                        // rather than its ceiling; the wish-driven motor re-derives most of it
                        // away within a tick, which is exactly why the floor is worth measuring.
                        // The Hop is BikeLayer.cs:725 verbatim, at a size the motor cannot absorb.
                        Vector3 v = _avatar.Velocity;
                        _avatar.Velocity = control == 1
                            ? new Vector3(v.X + 0.05f, v.Y, v.Z)
                            : BikeRig.Hop(v, 0.5f);
                    }
                }
                _scriptedDriftHeld = false;
                brain.Current = MoveIntent.None;
                await Ticks(20);
                Vector3 p = _avatar.GlobalPosition, vel = _avatar.Velocity;
                return new[]
                {
                    p.X, p.Y, p.Z, vel.X, vel.Y, vel.Z,
                    MotorTuning.Current.TurnLerp, MotorTuning.Current.MoveSpeed,
                    BikeTuning.Current.RideTurnMul, _turnMulNow,
                    _drift.ChargeSec, _drift.Grip, _drift.Active ? 1f : 0f, _owedBoostMps - owedBefore,
                };
            }
            static float Delta(float[] x, float[] y)
            {
                float d = 0f;
                for (int i = 0; i < x.Length; i++) d += Mathf.Abs(x[i] - y[i]);
                return d;
            }

            float[] legA = await Leg(0f, 0);
            float[] legA2 = await Leg(0f, 0);
            float[] legB = await Leg(20f, 0);
            float[] legC1 = await Leg(0f, 1);
            float[] legC2 = await Leg(0f, 2);
            BikeHandlingTuning.Current = beforeAbs;

            float noise = Delta(legA, legA2);
            float wobbleLeak = Delta(legA, legB);
            float controlSmall = Delta(legA, legC1);
            float controlHop = Delta(legA, legC2);
            HandlingCheck("the_wobble_reaches_the_roll_and_nothing_else_control_fires",
                wobbleLeak <= noise && controlSmall > noise && controlHop > noise
                && controlHop > 1e-5f && controlHop >= controlSmall,
                $"same ride, five legs, fingerprint = |d| over position, velocity, live TurnLerp "
              + $"and MoveSpeed, RideTurnMul, the turn multiplier, drift charge/grip/active and the "
              + $"owed boost. NOISE FLOOR (amp 0 vs amp 0, run twice) {noise:E3}. A 20 deg WOBBLE "
              + $"-- eight times the default -- moved the simulation by {wobbleLeak:E3} (need <= the "
              + $"noise floor). POSITIVE CONTROL 1, one 0.05 m/s `_avatar.Velocity = ...` write of "
              + $"PostStep's own form: {controlSmall:E3} (need > the noise floor) -- that is the "
              + $"detector's resolution. POSITIVE CONTROL 2, BikeLayer.cs:725's own BikeRig.Hop "
              + $"write at 0.5 m/s: {controlHop:E3} (need > the floor and > 1.000E-005). Both "
              + "controls are small in absolute terms because this is a WISH-driven motor: it "
              + "re-derives velocity from the intent every tick, so a one-tick write is mostly "
              + "absorbed and only its residual survives to the fingerprint. That is the point -- "
              + "the detector resolves what survives, and what survives of an eight-times-default "
              + "wobble is bit-exactly nothing.");
        }

        telemetry.End();

        int passed = _handlingChecks.Count(c => c.pass);
        bool ok = passed == _handlingChecks.Count;
        foreach ((string name, bool pass, string detail) in _handlingChecks.Where(c => !c.pass))
            GD.Print($"BIKE-HANDLING-SELFTEST RED {name}: {detail}");
        GD.Print($"BIKE-HANDLING-SELFTEST OVERALL: {(ok ? "PASS" : "FAIL")} "
               + $"({passed}/{_handlingChecks.Count})");
        await Ticks(5);
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>How many properties of a record the knob panel is expected to bind: the public
    /// instance floats and bools. Computed the same way the panel computes it, deliberately — the
    /// check is that the panel BOUND them all, not that this file and that one agree about a
    /// number typed twice.</summary>
    private static int BindableCount<T>() where T : struct
        => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.CanRead && p.CanWrite
                     && (p.PropertyType == typeof(float) || p.PropertyType == typeof(bool)));
}
