using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace Sail.Game.Failure;

/// <summary>
/// Headless self-test of the failure states <b>where the one that matters actually lives</b> —
/// inside <see cref="AvatarMotor.Step"/>, against a real <c>CharacterBody3D</c>, a real collision
/// world and a real physics tick.
///
/// <para><b>Why this exists alongside the xUnit tier.</b> <c>IncapacitationMachineTests</c> proves
/// the state machine's arithmetic and <c>IncapacityCascadeTests</c> proves the transition is
/// atomic, both far more thoroughly than a scene test could. What neither can prove is that any
/// of it is <i>reachable</i>: that a knocked-out player pushing the stick with everything they
/// have travels zero metres, that a frozen body actually slides when a teammate drags it, and that
/// a frozen body in deep water is held at the surface instead of sinking out of reach. The
/// dominant failure mode on this project is a system built and wired to nothing, and green unit
/// tests are not evidence against it. Everything below is measured off the body's own position.</para>
///
/// <para><b>Every denial check is paired with a positive control</b> proving the harness can
/// observe the thing being granted. A diagnostic that only ever reports "absent" certifies
/// nothing — a standing rule here, and the reason check 2 exists at all.</para>
///
/// <para><b>Honest scope.</b> This covers the motor layer and the drag. It does NOT cover the
/// service's own integration — the dawn floor firing off <c>RunDriver</c>, the night-water join,
/// the prop scatter — because each of those needs a full networked session with real
/// <c>SandboxAvatar</c>s, which is a bot-suite shape rather than a self-test scene. Those are
/// named as owed rather than quietly skipped: their arithmetic and their cascade are pinned in
/// <c>dotnet test</c>, and their wiring is the thing a playtest will exercise first.</para>
///
/// <para>Follows <c>WaterSelfTest</c>'s shape exactly: a <c>Node3D</c> instanced from a
/// <c>.tscn</c>, one printed line per check, and <c>INCAPACITY-TEST OVERALL: PASS|FAIL</c> with
/// exit 0 iff green. Run:
/// <c>Godot --headless --path . res://tests/scenes/IncapacitationSelfTest.tscn</c>.</para>
/// </summary>
public partial class IncapacitationSelfTest : Node3D
{
    private const float Dt = AvatarMotor.TickDelta;

    /// <summary>Ground level for the flat-earth half of the harness.</summary>
    private const float GroundY = 0f;

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

    private void BuildWorld()
    {
        // Ordinary dry ground, east of the shoreline so nothing in the water contract can colour
        // the motor and drag results.
        //
        // ITS EXTENT IS LOAD-BEARING, NOT ARBITRARY: 80 m centred on the origin spans x -40..40,
        // which leaves the deep at x = -70 with NO FLOOR AT ALL — exactly as WaterSelfTest builds
        // it, and for exactly the same reason. A frozen player dropped over a bottomless deep
        // either falls out of the world or is caught by the swim-line hold, and nothing else can
        // stop them, so "did it stop" is an unfakeable measurement. A ground plane wide enough to
        // reach x = -70 would make check 9 pass by standing on the floor, which is a control that
        // passes for the wrong reason and therefore certifies nothing.
        AddBox("Ground", new Vector3(0f, GroundY - 0.5f, 0f), new Vector3(80f, 1f, 80f));

        _body = new CharacterBody3D { Name = "Body" };
        _body.AddChild(new CollisionShape3D
        {
            // Identical to SandboxAvatar's own collider — origin at the feet.
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
        await DragChecks();
        await WaterInteractionChecks();
        Finish();
    }

    // --- Is control denial reachable from a real body? -----------------------------------------

    private async System.Threading.Tasks.Task MotorChecks()
    {
        var start = new Vector3(0f, GroundY + 0.1f, 0f);

        // 1. THE POSITIVE CONTROL, first, deliberately. Every "travelled zero metres" check below
        //    is worthless unless this harness can observe a body travelling. An Active player on
        //    the identical tape must cover real ground.
        MoveState free = await Drive(start, Full(), 120, MoveState.AtSpawn(start));
        float freeTravel = FlatTravel(start, free.Position);
        Check($"positive_control_an_active_camper_travels ({freeTravel:F2} m)", freeTravel > 2f);

        // 2-4. Each denial state, on that same tape, must travel nothing. This is the
        //      ragdoll-you-can-still-steer guard measured at the only layer that can enforce it —
        //      not "the flag was set", but "the body did not move".
        foreach ((string name, MoveState seed) in DeniedSeeds(start))
        {
            MoveState result = await Drive(start, Full(), 120, seed);
            float travelled = FlatTravel(start, result.Position);
            Check($"{name}_denies_all_movement (travelled {travelled:F3} m)", travelled < 0.05f);
        }

        // 5. And the state survives the ticks rather than being cleared by the motor. The motor
        //    obeys these fields; it must never author them (the pass-through contract that keeps
        //    a reconciliation replay from re-granting control).
        MoveState frozen = await Drive(start, Full(), 60, Seed(start, IncapacityState.Frozen));
        Check("the_motor_never_clears_the_state_it_obeys",
            frozen.Incapacity == IncapacityState.Frozen);
    }

    private IEnumerable<(string, MoveState)> DeniedSeeds(Vector3 start)
    {
        yield return ("knocked_out", Seed(start, IncapacityState.KnockedOut));
        yield return ("frozen", Seed(start, IncapacityState.Frozen));
        MoveState impulse = MoveState.AtSpawn(start);
        impulse.ImpulseRagdoll = true;
        yield return ("impulse_ragdoll", impulse);
    }

    // --- Does the drag actually move a body? -----------------------------------------------------

    private async System.Threading.Tasks.Task DragChecks()
    {
        var start = new Vector3(20f, GroundY + 0.1f, 0f);

        // 6. A frozen body left alone stays put, even under full stick. The control for check 7.
        MoveState still = await Drive(start, Full(), 90, Seed(start, IncapacityState.Frozen));
        Check($"a_frozen_body_cannot_self_propel (travelled {FlatTravel(start, still.Position):F3} m)",
            FlatTravel(start, still.Position) < 0.05f);

        // 7. The same body, with the server injecting a drag velocity every tick the way
        //    IncapacitationService.ApplyDragMotion does, MOVES — and moves at roughly the tuned
        //    speed. This is the whole drag verb, measured: no steering, no second transform
        //    writer, just an authoritative velocity carried through the same Step everyone uses.
        MoveState dragged = MoveState.AtSpawn(start);
        dragged.Incapacity = IncapacityState.Frozen;
        var pull = new Vector3(0f, 0f, -1f);
        const int dragTicks = 120;
        for (int i = 0; i < dragTicks; i++)
        {
            await PhysicsFrame();
            // Exactly what ServerSetDragVelocity writes: the tuned speed plus one tick of the
            // deceleration Step is about to apply to a body with no movement intent.
            Vector3 v = pull * (IncapacityRules.DragSpeedMps + AvatarMotor.Deceleration * Dt);
            dragged.Velocity = new Vector3(v.X, dragged.Velocity.Y, v.Z);
            dragged = AvatarMotor.Step(_body, dragged, Full(), 1f, Dt, out _);
        }
        float dragTravel = FlatTravel(start, dragged.Position);
        float wanted = IncapacityRules.DragSpeedMps * dragTicks * Dt;
        Check($"a_dragged_block_slides ({dragTravel:F2} m, want ~{wanted:F2} m)",
            dragTravel > wanted * 0.75f && dragTravel < wanted * 1.25f);

        // 8. It slides SLOWLY — dragging is supposed to cost you the night. If this ever passes at
        //    walking pace, the verb has stopped being a decision.
        Check($"dragging_is_slower_than_walking ({IncapacityRules.DragSpeedMps:F2} vs {AvatarMotor.MoveSpeed:F2} m/s)",
            IncapacityRules.DragSpeedMps < AvatarMotor.MoveSpeed * 0.6f);
    }

    // --- The one interaction with the shipped water contract -------------------------------------

    private async System.Threading.Tasks.Task WaterInteractionChecks()
    {
        // 9. THE ANTI-SOFTLOCK ONE. Over the deep there is no floor at all. A frozen player there
        //    either sinks out of the world — unreachable for the drag, rescued only by dawn — or
        //    is held at the swim line where a teammate can get to them. Nothing but the deliberate
        //    narrowing of the sink branch to ControlLocked can hold them, so this is unfakeable.
        var deep = new Vector3(-70f, 0f, 0f);
        MoveState frozenInDeep = await Drive(deep, Full(), 240, Seed(deep, IncapacityState.Frozen));
        Check("a_frozen_body_in_deep_water_is_swimming, not falling",
            frozenInDeep.Water == Water.WaterState.Swimming);
        Check($"a_frozen_body_in_deep_water_is_held_at_the_swim_line "
            + $"(y={frozenInDeep.Position.Y:F2}, want {Water.WaterGeometry.SwimLineY:F2})",
            Mathf.Abs(frozenInDeep.Position.Y - Water.WaterGeometry.SwimLineY) < 0.15f);

        // 10. The positive control for check 9: a SPUTTER-OUT in the same place still sinks. If
        //     this failed, check 9 would be passing because nothing sinks anywhere.
        MoveState sputtering = MoveState.AtSpawn(deep);
        sputtering.ControlLocked = true;
        for (int i = 0; i < 120; i++)
        {
            await PhysicsFrame();
            sputtering = AvatarMotor.Step(_body, sputtering, Full(), 1f, Dt, out _);
        }
        Check($"positive_control_a_sputter_out_still_sinks (y={sputtering.Position.Y:F2})",
            sputtering.Position.Y < frozenInDeep.Position.Y - 0.5f);
    }

    // --- Harness ----------------------------------------------------------------------------------

    private static MoveState Seed(Vector3 start, IncapacityState state)
    {
        MoveState s = MoveState.AtSpawn(start);
        s.Incapacity = state;
        return s;
    }

    /// <summary>Everything a player can push at once: full stick, sprint held, jump held. If a
    /// denial leaks anywhere, this is the input that finds it.</summary>
    private static MoveIntent Full() => new()
    {
        MoveDir = new Vector3(0, 0, -1),
        Sprint = true,
        Jump = true,
    };

    private async System.Threading.Tasks.Task<MoveState> Drive(
        Vector3 start, MoveIntent intent, int ticks, MoveState seed)
    {
        MoveState state = seed;
        state.Position = start;
        for (int i = 0; i < ticks; i++)
        {
            await PhysicsFrame();
            state = AvatarMotor.Step(_body, state, intent, 1f, Dt, out _);
        }
        return state;
    }

    private static float FlatTravel(Vector3 from, Vector3 to) =>
        new Vector2(to.X - from.X, to.Z - from.Z).Length();

    private Godot.SignalAwaiter PhysicsFrame() =>
        ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

    private void Check(string name, bool ok)
    {
        _results.Add((name, ok));
        GD.Print($"[incapacity-selftest] {(ok ? "PASS" : "FAIL")} {name}");
    }

    private void Finish()
    {
        _finished = true;
        int failed = 0;
        foreach ((string name, bool ok) in _results)
            if (!ok)
            {
                failed++;
                GD.PrintErr($"[incapacity-selftest] FAILED: {name}");
            }
        GD.Print($"INCAPACITY-TEST OVERALL: {(failed == 0 ? "PASS" : "FAIL")} " +
            $"({_results.Count - failed}/{_results.Count} checks)");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
