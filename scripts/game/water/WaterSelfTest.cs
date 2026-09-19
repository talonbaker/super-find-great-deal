using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace Sail.Game.Water;

/// <summary>
/// Headless self-test of the water contract <b>where it actually lives</b> — inside
/// <see cref="AvatarMotor.Step"/>, against a real <c>CharacterBody3D</c>, a real collision
/// world and a real physics tick.
///
/// <b>Why this exists alongside the xUnit tier.</b> <c>WaterGeometryTests</c> and friends prove
/// the state machine's arithmetic, and they prove it far more thoroughly than a scene test could.
/// What they cannot prove is that any of it is <i>reachable</i>: that the multipliers reach the
/// body, that "horizontal only" holds a swimmer at the waterline instead of dropping them through
/// the world forever, that a control lock actually stops a player who is pushing the stick. The
/// dominant failure mode on this project is a system built and wired to nothing, and green unit
/// tests are not evidence against it. Everything below is measured off the body's own position.
///
/// Follows <c>NetStepSelfTest</c>'s shape exactly: a <c>Node3D</c> instanced from a
/// <c>.tscn</c>, one printed line per check, and <c>WATER-TEST OVERALL: PASS|FAIL</c> with exit
/// 0 iff green. Run: <c>Godot --headless --path . res://tests/scenes/WaterSelfTest.tscn</c>.
/// </summary>
public partial class WaterSelfTest : Node3D
{
    private const float Dt = AvatarMotor.TickDelta;

    /// <summary>Tolerance on a measured steady-state speed. Accel is 34 m/s² against a top speed
    /// under 4 m/s, so 120 ticks is far past settled; this only absorbs MoveAndSlide's contact
    /// jitter.</summary>
    private const float SpeedTolerance = 0.05f;

    private readonly List<(string Name, bool Ok)> _results = new();
    private bool _finished;
    private CharacterBody3D _body = null!;

    public override void _Ready()
    {
        GetTree().CreateTimer(60.0).Timeout += () =>
        {
            if (_finished)
                return;
            Check("watchdog_no_hang", false);
            Finish();
        };
        BuildWorld();
        _ = RunAsync();
    }

    /// <summary>
    /// A synthetic lake with the same shape the real one has, built here rather than loaded from
    /// a world scene: when this was written W1 owned that scene and was authoring the shelf in
    /// parallel, so a test that loaded it would be asserting against a moving target and would fail for reasons that have
    /// nothing to do with the state machine. The geometry that matters is the depth under the
    /// plane, and that is fully expressible here.
    /// </summary>
    private void BuildWorld()
    {
        // East of the shoreline: ordinary ground, at a height that is FAR below WaterY. If the
        // lateral gate were missing, a player here would read as 40 m underwater.
        AddBox("DryGround", new Vector3(0f, -40.5f, 0f), new Vector3(80f, 1f, 80f));

        // The shelf, west of the shoreline: bed top at -1.00 against a surface at -0.58, i.e.
        // 0.42 m of water — comfortably Wading, clear of both hysteresis bands.
        AddBox("Shelf", new Vector3(-47f, -1.5f, 0f), new Vector3(8f, 1f, 40f));

        // The deep, at x = -70: DELIBERATELY no floor at all. A swimmer held only by gravity
        // would fall out of the world; the swim hold is the only thing that can stop them, so
        // "did it stop" is an unfakeable measurement of whether the hold is wired.

        _body = new CharacterBody3D { Name = "Body" };
        _body.AddChild(new CollisionShape3D
        {
            // Identical to SandboxAvatar's own collider (SandboxAvatar._Ready) — origin at the
            // feet, which is what makes depth = WaterY - position.Y correct with no offset.
            Shape = new CapsuleShape3D { Radius = 0.36f, Height = 0.9f },
            Position = new Vector3(0, 0.45f, 0),
        });
        AddChild(_body);
    }

    private void AddBox(string name, Vector3 pos, Vector3 size)
    {
        var body = new StaticBody3D { Name = name, Position = pos };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        await MotorChecks();
        ServiceChecks();
        SequenceChecks();
        Finish();
    }

    // --- The motor: is any of this reachable from a real body? ------------------------------------

    private async System.Threading.Tasks.Task MotorChecks()
    {
        // 1. Dry ground far below the water plane is DRY. The lateral gate, measured.
        MoveState dry = await Settle(new Vector3(0f, -39.9f, 0f), Forward(), ticks: 90);
        Check("dry_ground_below_water_plane_is_not_water", dry.Water == WaterState.Dry);
        float drySpeed = Flat(dry.Velocity);
        Check($"dry_speed_is_full ({drySpeed:F2} m/s)",
            Near(drySpeed, AvatarMotor.MoveSpeed, SpeedTolerance));

        // 2. Sprint on dry land is the positive control for check 4: without it, "sprint had no
        //    effect in water" could just as easily mean sprint has no effect anywhere.
        MoveState drySprint = await Settle(new Vector3(0f, -39.9f, 0f), Forward(sprint: true), 90);
        Check($"positive_control_sprint_works_on_dry_land ({Flat(drySprint.Velocity):F2} m/s)",
            Near(Flat(drySprint.Velocity), AvatarMotor.MoveSpeed * AvatarMotor.SprintMultiplier,
                SpeedTolerance));

        // 3. The shelf: standing in 0.42 m of water is Wading, at 0.55x.
        MoveState wade = await Settle(new Vector3(-47f, -0.9f, 0f), Forward(), 120);
        Check("shelf_resolves_as_wading", wade.Water == WaterState.Wading);
        Check($"wading_speed_is_0.55x ({Flat(wade.Velocity):F2} m/s)",
            Near(Flat(wade.Velocity), AvatarMotor.MoveSpeed * WaterGeometry.WadeSpeedMul,
                SpeedTolerance));

        // 4. Sprint is denied in water — the same tape as check 3 but holding sprint.
        MoveState wadeSprint = await Settle(new Vector3(-47f, -0.9f, 0f), Forward(sprint: true), 120);
        Check($"sprint_denied_while_wading ({Flat(wadeSprint.Velocity):F2} m/s)",
            Near(Flat(wadeSprint.Velocity), AvatarMotor.MoveSpeed * WaterGeometry.WadeSpeedMul,
                SpeedTolerance));

        // 5. Soaked stacks its 0.9x on top, on dry land where nothing else is scaling.
        MoveState soaked = await Settle(new Vector3(0f, -39.9f, 0f), Forward(), 90, soaked: true);
        Check($"soaked_speed_is_0.9x ({Flat(soaked.Velocity):F2} m/s)",
            Near(Flat(soaked.Velocity),
                AvatarMotor.MoveSpeed * WaterGeometry.SoakedSpeedMul, SpeedTolerance));

        // 6. THE ONE THAT CANNOT BE FAKED. Over the deep there is no floor. A player dropped in
        //    either falls out of the world forever or is caught by the swim hold at the
        //    waterline. Nothing else can stop them.
        MoveState swim = await Settle(new Vector3(-70f, 0f, 0f), Forward(), 240);
        Check("deep_water_resolves_as_swimming", swim.Water == WaterState.Swimming);
        Check($"swimmer_is_held_at_the_waterline (y={swim.Position.Y:F2}, want {WaterGeometry.SwimLineY:F2})",
            Near(swim.Position.Y, WaterGeometry.SwimLineY, 0.12f));
        Check($"swim_speed_is_0.40x ({Flat(swim.Velocity):F2} m/s)",
            Near(Flat(swim.Velocity), AvatarMotor.MoveSpeed * WaterGeometry.SwimSpeedMul,
                SpeedTolerance));

        // 7. Jump is denied while swimming. Measured as "the hold was never broken upward",
        //    because a granted jump would fling the body clean out of the water.
        float highest = float.MinValue;
        MoveState jumpy = MoveState.AtSpawn(new Vector3(-70f, 0f, 0f));
        for (int i = 0; i < 300; i++)
        {
            await PhysicsFrame();
            jumpy = AvatarMotor.Step(_body, jumpy, Forward(jump: true), 1f, Dt, out _);
            if (i > 150)
                highest = Mathf.Max(highest, jumpy.Position.Y);
        }
        Check($"jump_denied_while_swimming (peak y={highest:F2})",
            jumpy.Water == WaterState.Swimming && highest < WaterGeometry.WaterY);

        // 8. Control lock: full stick, zero travel. The ragdoll-you-still-steer guard, measured
        //    at the only layer that can actually enforce it.
        var lockedStart = new Vector3(0f, -39.9f, 0f);
        MoveState locked = MoveState.AtSpawn(lockedStart);
        locked.ControlLocked = true;
        for (int i = 0; i < 120; i++)
        {
            await PhysicsFrame();
            locked = AvatarMotor.Step(_body, locked, Forward(sprint: true, jump: true), 1f, Dt, out _);
        }
        float travelled = new Vector2(locked.Position.X - lockedStart.X,
            locked.Position.Z - lockedStart.Z).Length();
        Check($"control_lock_denies_all_movement (travelled {travelled:F3} m)", travelled < 0.05f);

        // 9. ...and the positive control: the identical tape unlocked DOES travel, so check 8 is
        //    measuring the lock rather than a broken harness.
        MoveState free = await Settle(lockedStart, Forward(sprint: true), 120);
        float freeTravel = new Vector2(free.Position.X - lockedStart.X,
            free.Position.Z - lockedStart.Z).Length();
        Check($"positive_control_unlocked_tape_travels ({freeTravel:F2} m)", freeTravel > 2f);

        // 10. The swallow: locked + swimming sinks, and keeps sinking past the swim line.
        MoveState sinking = MoveState.AtSpawn(new Vector3(-70f, WaterGeometry.SwimLineY, 0f));
        sinking.Water = WaterState.Swimming;
        sinking.ControlLocked = true;
        for (int i = 0; i < (int)(WaterGeometry.GoUnderSec / Dt); i++)
        {
            await PhysicsFrame();
            sinking = AvatarMotor.Step(_body, sinking, MoveIntent.None, 1f, Dt, out _);
        }
        float sank = WaterGeometry.SwimLineY - sinking.Position.Y;
        Check($"go_under_sinks_the_body ({sank:F2} m in {WaterGeometry.GoUnderSec}s)",
            sank > 1.0f && sinking.Water == WaterState.Swimming);

        // 11. Exiting: a swimmer lifted onto dry land resolves Dry on the very next step, which
        //     is what makes the shore recovery land in a legal state rather than one tick of
        //     swimming on the bank.
        Vector3 shore = WaterGeometry.NearestShorePoint(new Vector3(-70f, -6f, 0f));
        Check("shore_recovery_point_resolves_dry",
            WaterGeometry.ResolveAt(WaterState.Swimming, shore) == WaterState.Dry);
    }

    // --- The service: is it wired, and is it inert where it should be? --------------------------------

    private void ServiceChecks()
    {
        var service = new WaterService { Name = WaterService.NodeName };
        AddChild(service);
        Check("service_registers_its_singleton", WaterService.Instance == service);

        // A world with no lake and no players: the service must be completely harmless. This is
        // the contract every replicated manager here holds, and the reason Gameplay can add it
        // unconditionally.
        service.Setup(isServer: true, () => System.Array.Empty<SandboxAvatar>());
        service._PhysicsProcess(Dt);
        service._PhysicsProcess(Dt);
        Check("service_is_inert_with_no_avatars", true);

        // Unknown peer: every read has a safe, boring answer. "Not in the water" is the only
        // correct response to "who?".
        Check("unknown_peer_reads_dry", service.StateOf(12345) == WaterState.Dry);
        Check("unknown_peer_reads_zero_chill", Mathf.IsZeroApprox(service.ChillOf(12345)));
        Check("unknown_peer_is_not_soaked", !service.IsSoaked(12345));
        Check("unknown_peer_has_no_sputter_phase", service.PhaseOf(12345) == SputterPhase.None);
        Check("unknown_peer_may_use_the_camcorder", service.CamcorderUsable(12345));
        Check("unknown_peer_has_no_tape_ruins", service.TapeRuinCount(12345) == 0);
        Check("unknown_peer_footstep_noise_is_unmodified",
            Mathf.IsEqualApprox(service.FootstepNoiseMultiplierFor(12345), 1f));
        service.ForgetPeer(12345);
        service.ForgetPeer(12345);
        Check("forget_peer_is_idempotent", service.StateOf(12345) == WaterState.Dry);

        service.QueueFree();
    }

    // --- The cold clock and the sputter-out, end to end at the real tick rate ---------------------------

    private void SequenceChecks()
    {
        // A full swim-out: 60 Hz from chill 0, at x = -100 (past the ramp, so 20 s), then the
        // whole sputter-out. Everything the server tick does, in the order it does it.
        var sputter = new SputterSequence();
        float chill = 0f;
        int ruins = 0;
        int controlReturns = 0;
        bool lockedLastTick = false;
        int lockedTicks = 0;
        float elapsed = 0f;

        for (int i = 0; i < 60 * 40; i++)
        {
            elapsed += Dt;
            if (sputter.Phase == SputterPhase.None)
            {
                chill = ChillClock.Advance(chill, WaterState.Swimming, -100f, Dt);
                if (chill >= 1f && sputter.Begin())
                    ruins++;
            }
            else
            {
                if (sputter.Advance(Dt) == SputterSequence.Step.Recovered)
                {
                    chill = 0f;
                    controlReturns++;
                }
            }
            if (sputter.ControlLocked)
                lockedTicks++;
            if (lockedLastTick && !sputter.ControlLocked && sputter.Phase != SputterPhase.None)
                Check("control_never_returns_mid_episode", false);
            lockedLastTick = sputter.ControlLocked;
        }

        Check($"cold_collects_a_swimmer_and_returns_control ({elapsed:F0}s simulated)",
            controlReturns >= 1);
        Check($"tape_ruined_exactly_once_per_sputter (ruins={ruins}, recoveries={controlReturns})",
            ruins == controlReturns && sputter.CostAppliedCount == ruins);
        float lockedSec = lockedTicks * Dt;
        float expectedLocked = controlReturns * (WaterGeometry.GoUnderSec + WaterGeometry.RecoverSec);
        Check($"control_was_locked_for_exactly_the_episode_duration ({lockedSec:F2}s of {expectedLocked:F2}s)",
            Mathf.Abs(lockedSec - expectedLocked) < 0.1f);

        // Idempotency, hammered: a thousand attempts to re-enter one episode buy one cost.
        var once = new SputterSequence();
        for (int i = 0; i < 1000; i++)
            once.Begin();
        Check($"a_thousand_begins_cost_one_tape ({once.CostAppliedCount})", once.CostAppliedCount == 1);

        // Positive control for the counter: it CAN read more than one, through the legitimate
        // door. Without this, "exactly one" could be a counter that never increments.
        var twice = new SputterSequence();
        for (int episode = 0; episode < 2; episode++)
        {
            twice.Begin();
            twice.Advance(WaterGeometry.GoUnderSec + 0.01f);
            twice.Advance(WaterGeometry.RecoverSec + 0.01f);
        }
        Check($"positive_control_counter_can_read_two ({twice.CostAppliedCount})",
            twice.CostAppliedCount == 2);

        // Late-join dump: the literal spec §12 line, through the real codec.
        var sent = new List<WaterPeerSnapshot>
        {
            new(11, WaterState.Swimming, 0.734f, false, SputterPhase.None),
            new(22, WaterState.Wading, 0f, true, SputterPhase.None),
            new(33, WaterState.Dry, 1f, true, SputterPhase.Recovering),
        };
        List<WaterPeerSnapshot> got = WaterDump.Unpack(WaterDump.Pack(sent));
        bool round = got.Count == sent.Count;
        for (int i = 0; round && i < sent.Count; i++)
            round = got[i] == sent[i];
        Check("late_join_dump_round_trips_swim_state_and_chill", round);

        // ...and the same packet with one byte corrupted must NOT round-trip, or the check above
        // proves nothing about the comparison.
        byte[] corrupt = WaterDump.Pack(sent);
        corrupt[5] = (byte)WaterState.Dry;
        Check("positive_control_a_corrupt_dump_is_detected",
            WaterDump.Unpack(corrupt)[0] != sent[0]);
    }

    // --- Harness ------------------------------------------------------------------------------------

    private static MoveIntent Forward(bool sprint = false, bool jump = false) => new()
    {
        MoveDir = new Vector3(0, 0, -1),
        Sprint = sprint,
        Jump = jump,
    };

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    private static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;

    private async System.Threading.Tasks.Task<MoveState> Settle(
        Vector3 start, MoveIntent intent, int ticks, bool soaked = false)
    {
        MoveState state = MoveState.AtSpawn(start);
        state.Soaked = soaked;
        for (int i = 0; i < ticks; i++)
        {
            await PhysicsFrame();
            state = AvatarMotor.Step(_body, state, intent, 1f, Dt, out _);
        }
        return state;
    }

    private Godot.SignalAwaiter PhysicsFrame() =>
        ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

    private void Check(string name, bool ok)
    {
        _results.Add((name, ok));
        GD.Print($"[water-selftest] {(ok ? "PASS" : "FAIL")} {name}");
    }

    private void Finish()
    {
        _finished = true;
        int failed = 0;
        foreach ((string name, bool ok) in _results)
            if (!ok)
            {
                failed++;
                GD.PrintErr($"[water-selftest] FAILED: {name}");
            }
        GD.Print($"WATER-TEST OVERALL: {(failed == 0 ? "PASS" : "FAIL")} " +
            $"({_results.Count - failed}/{_results.Count} checks)");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
