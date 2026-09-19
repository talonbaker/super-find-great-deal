using System;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Sail.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>LD-2 — the cadence clock, proved on synthetic tick streams.</b> Every test feeds
/// <see cref="CadenceClock"/> the way <c>CadenceTracker</c> does — one <see cref="MoveState"/>
/// per fixed tick at <see cref="AvatarMotor.TickDelta"/> — and asserts the two numbers the readout
/// shows and telemetry sends. The packet's two acceptance streams are the first two tests; the
/// rest pin the act list (the class doc IS the spec) edge by edge, with a positive control beside
/// each absence so "it did not reset" can be told apart from "nothing resets".
/// </summary>
public class CadenceClockTests
{
    private const float Dt = AvatarMotor.TickDelta;
    private static readonly MotorTuning T = MotorTuning.Default;

    private static MoveState Standing() => new() { Grounded = true, Velocity = Vector3.Zero };

    private static MoveState Sprinting() => new()
    {
        Grounded = true,
        Velocity = new Vector3(MotorArc.SprintSpeedMps(T), 0f, 0f),
    };

    private static MoveState Moving(float mps) => new()
    {
        Grounded = true,
        Velocity = new Vector3(0f, 0f, mps),
    };

    private static MoveState Airborne(float vy) => new()
    {
        Grounded = false,
        Velocity = new Vector3(0f, vy, 0f),
    };

    private static void Feed(CadenceClock c, in MoveState s, int ticks, bool inMenu = false,
        string? section = null)
    {
        for (int i = 0; i < ticks; i++)
            c.Feed(T, s, Dt, inMenu, section);
    }

    private static int TicksOf(double sec) => (int)Math.Round(sec / Dt);

    // =============================================================================================
    // Acceptance criterion 1
    // =============================================================================================

    /// <summary>10 s standing, a jump at t = 10 s, 5 s sprinting: 10.0 ± 0.1 stop-seconds, the
    /// clock resets on the jump, and the sprint adds nothing to the stop total.</summary>
    [Fact]
    public void TenSecondsStanding_ThenAJump_ThenFiveSprinting()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(10.0));
        Assert.InRange(c.StopSecondsTotal, 9.9, 10.1);
        Assert.InRange(c.SinceActSec, 9.9, 10.1);   // nothing has acted yet
        Assert.Equal(0, c.ActCount);

        // The jump tick: the motor leaves the ground with (JumpVelocity - one tick of gravity).
        c.Feed(T, Airborne(T.JumpVelocity - T.Gravity * Dt), Dt, inMenu: false);
        Assert.Equal(0.0, c.SinceActSec);
        Assert.Equal("jump", c.LastAct);
        Assert.Equal(1, c.ActCount);

        Feed(c, Sprinting(), TicksOf(5.0));
        Assert.InRange(c.SinceActSec, 4.9, 5.1);
        Assert.InRange(c.StopSecondsTotal, 9.9, 10.1);   // the sprint is not a stop
        Assert.Equal(1, c.ActCount);                       // and sprinting is not an act
    }

    /// <summary>A landing after a tap-length airtime does NOT reset; one after a held-jump airtime
    /// does. The airborne states carry no upward impulse, so the takeoff cannot be what resets —
    /// only the landing is on trial.</summary>
    [Fact]
    public void TapLanding_DoesNotReset_HeldLanding_Does()
    {
        float tap = MotorArc.JogTap(T).AirtimeSec;
        float held = MotorArc.HeldSprint(T).AirtimeSec;
        float bar = CadenceClock.LandingResetAirtimeSec(T);
        Assert.True(tap < bar && bar < held, $"tap {tap:F3} < bar {bar:F3} < held {held:F3}");

        // Tap.
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(1.0));
        Feed(c, Airborne(-1f), TicksOf(tap));
        double before = c.SinceActSec;
        c.Feed(T, Standing(), Dt, inMenu: false);
        Assert.Equal(0, c.ActCount);
        Assert.InRange(c.SinceActSec, before + Dt * 0.5, before + Dt * 1.5);

        // Held — the positive control for the line above.
        c = new CadenceClock();
        Feed(c, Standing(), TicksOf(1.0));
        Feed(c, Airborne(-1f), TicksOf(held));
        Assert.Equal(0, c.ActCount);
        c.Feed(T, Standing(), Dt, inMenu: false);
        Assert.Equal(1, c.ActCount);
        Assert.Equal("land", c.LastAct);
        Assert.Equal(0.0, c.SinceActSec);
    }

    // =============================================================================================
    // Acceptance criterion 2
    // =============================================================================================

    /// <summary>Stops only in the first minute: the rolling figure reads 0 at t = 120 s. The
    /// positive controls beside it show the same window DOES hold the stop time while it is
    /// inside the window.</summary>
    [Fact]
    public void RollingMinute_ReadsZeroAtT120_WhenStopsWereOnlyInTheFirstMinute()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(30.0));
        Assert.InRange(c.StopSecondsLastMinute, 29.9, 30.1);   // positive control, inside the window

        Feed(c, Standing(), TicksOf(30.0));
        Assert.InRange(c.StopSecondsTotal, 59.9, 60.1);

        Feed(c, Sprinting(), TicksOf(30.0));
        Assert.InRange(c.StopSecondsLastMinute, 29.0, 31.0);   // half the window has aged out

        Feed(c, Sprinting(), TicksOf(30.0));
        Assert.InRange(c.ElapsedSec, 119.9, 120.1);
        Assert.Equal(0.0, c.StopSecondsLastMinute);
        Assert.InRange(c.StopSecondsTotal, 59.9, 60.1);        // the total is not a window
        Assert.InRange(c.StopSecondsPerMinuteSession, 29.9, 30.1);
    }

    // =============================================================================================
    // The stop predicate
    // =============================================================================================

    /// <summary>One constant, 0.9 × jog: above a walk (so a walk is stopped — the direction fork
    /// the report raises) and below a held jog (so a jog is not).</summary>
    [Fact]
    public void StopThreshold_SitsBetweenWalkAndJog()
    {
        float bar = CadenceClock.StopSpeedMps(T);
        Assert.InRange(bar, T.MoveSpeed * 0.9f - 1e-5f, T.MoveSpeed * 0.9f + 1e-5f);
        Assert.True(LocomotionProfile.WalkSpeedMps < bar, "a walk must read as stopped");
        Assert.True(bar < LocomotionProfile.JogSpeedMps, "a jog must not");

        Assert.True(CadenceClock.IsStopped(T, Moving(LocomotionProfile.WalkSpeedMps), false));
        Assert.False(CadenceClock.IsStopped(T, Moving(LocomotionProfile.JogSpeedMps), false));
        Assert.False(CadenceClock.IsStopped(T, Moving(MotorArc.SprintSpeedMps(T)), false));
        Assert.False(CadenceClock.IsStopped(T, Airborne(0f), false), "airborne is never stopped");
        Assert.False(CadenceClock.IsStopped(T, Standing(), inMenu: true), "a menu is not a stop");
    }

    [Fact]
    public void MenuTime_IsNotStopTime_AndAWalkIs()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(10.0), inMenu: true);
        Assert.Equal(0.0, c.StopSecondsTotal);

        Feed(c, Moving(LocomotionProfile.WalkSpeedMps), TicksOf(3.0));
        Assert.InRange(c.StopSecondsTotal, 2.9, 3.1);
    }

    // =============================================================================================
    // The act list, edge by edge
    // =============================================================================================

    [Fact]
    public void GroundJump_And_CoyoteJump_Reset_ButWalkingOffALedge_DoesNot()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(2.0));
        c.Feed(T, Airborne(T.JumpVelocity), Dt, false);
        Assert.Equal("jump", c.LastAct);

        // Coyote: already falling, then the launch overwrites the fall.
        c = new CadenceClock();
        Feed(c, Standing(), TicksOf(1.0));
        Feed(c, Airborne(-3f), 3);
        Assert.Equal(0, c.ActCount);
        c.Feed(T, Airborne(T.JumpVelocity), Dt, false);
        Assert.Equal(1, c.ActCount);

        // Walking off a ledge: gravity only, no impulse, no act.
        c = new CadenceClock();
        Feed(c, Sprinting(), TicksOf(1.0));
        Feed(c, Airborne(-T.Gravity * Dt), 1);
        Feed(c, Airborne(-2f * T.Gravity * Dt), 1);
        Assert.Equal(0, c.ActCount);
    }

    [Fact]
    public void AirJump_ResetsOnTheCounter_Once()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(1.0));
        c.Feed(T, Airborne(T.JumpVelocity), Dt, false);          // takeoff
        Feed(c, Airborne(2f), 5);
        Assert.Equal(1, c.ActCount);

        // A mode-1 air jump pressed while still rising: a small vertical delta the impulse test
        // must not need, because the counter is the witness.
        MoveState air = Airborne(T.JumpVelocity * T.AirJumpVelocityFraction);
        air.AirJumpsUsed = 1;
        c.Feed(T, air, Dt, false);
        Assert.Equal(2, c.ActCount);
        Assert.Equal("air-jump", c.LastAct);

        Feed(c, air, 10);                                        // still spent: no re-fire
        Assert.Equal(2, c.ActCount);
    }

    [Fact]
    public void SkidStart_And_SlideStart_ResetOnce_NotWhileHeld()
    {
        var c = new CadenceClock();
        Feed(c, Sprinting(), TicksOf(1.0));

        MoveState skid = Sprinting();
        skid.SkidRemaining = 0.5f;
        c.Feed(T, skid, Dt, false);
        Assert.Equal(1, c.ActCount);
        Assert.Equal("skid", c.LastAct);
        skid.SkidRemaining = 0.4f;
        Feed(c, skid, 10);
        Assert.Equal(1, c.ActCount);

        MoveState slide = Sprinting();
        slide.Verb = MoveVerb.Slide;
        c.Feed(T, slide, Dt, false);
        Assert.Equal(2, c.ActCount);
        Assert.Equal("slide", c.LastAct);
        Feed(c, slide, 10);
        Assert.Equal(2, c.ActCount);

        // The crouch that is not an act: a tuck and a duck walk are postures.
        MoveState tuck = Standing();
        tuck.Verb = MoveVerb.Tuck;
        Feed(c, tuck, 5);
        tuck.Verb = MoveVerb.DuckWalk;
        Feed(c, tuck, 5);
        Assert.Equal(2, c.ActCount);
    }

    [Fact]
    public void NoteAct_Resets_AndNamesTheAct()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(4.0));
        c.NoteAct("grab");
        Assert.Equal(0.0, c.SinceActSec);
        Assert.Equal("grab", c.LastAct);
        Feed(c, Standing(), TicksOf(1.0));
        Assert.InRange(c.SinceActSec, 0.9, 1.1);
    }

    [Fact]
    public void ZeroOrNegativeDt_IsIgnored()
    {
        var c = new CadenceClock();
        c.Feed(T, Standing(), 0f, false);
        c.Feed(T, Standing(), -Dt, false);
        Assert.Equal(0.0, c.ElapsedSec);
        Assert.Equal(0.0, c.StopSecondsTotal);
    }

    // =============================================================================================
    // Sections
    // =============================================================================================

    [Fact]
    public void StopSeconds_AreAttributedToTheSectionKeyTheFeederSupplies()
    {
        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(3.0), section: "CyanRun");
        Feed(c, Standing(), TicksOf(2.0), section: "Hub");
        Feed(c, Sprinting(), TicksOf(2.0), section: "Hub");     // moving: nothing attributed
        Feed(c, Standing(), TicksOf(1.0), section: null);       // off every footprint: total only

        Assert.InRange(c.StopSecondsBySection["CyanRun"], 2.9, 3.1);
        Assert.InRange(c.StopSecondsBySection["Hub"], 1.9, 2.1);
        Assert.Equal(2, c.StopSecondsBySection.Count);
        Assert.InRange(c.StopSecondsTotal, 5.9, 6.1);
    }

    // (The section-footprint mapping test that stood here read the old level's published anchor
    // constants, and went with that level at the fork - BASE-1, 2026-09-19. CadenceSections still
    // has no footprints of its own to check until a world publishes some.)

    // =============================================================================================
    // The readout text
    // =============================================================================================

    [Fact]
    public void Readout_ShowsBothLines_AndMovesWithTheClock()
    {
        string idle = CadenceReadout.Lines(null);
        Assert.Equal("since-act  --\nstop       --", idle);

        var c = new CadenceClock();
        Feed(c, Standing(), TicksOf(3.0));
        string a = CadenceReadout.Lines(c);
        Assert.StartsWith("since-act  3.0 s", a, StringComparison.Ordinal);
        Assert.Contains("\nstop       3.0 s/min", a, StringComparison.Ordinal);
        Assert.DoesNotContain("last:", a, StringComparison.Ordinal);

        c.NoteAct("pop");
        Feed(c, Sprinting(), TicksOf(1.5));
        string b = CadenceReadout.Lines(c);
        Assert.StartsWith("since-act  1.5 s   (last: pop)", b, StringComparison.Ordinal);
        Assert.Contains("stop       3.0 s/min   (total 3.0 s)", b, StringComparison.Ordinal);
        Assert.NotEqual(a, b);   // positive control: the text is live
    }
}
