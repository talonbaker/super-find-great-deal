using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-5f — the anticipation crouch.</b> The last unbuilt item in Talon's Movement Feel Lab
/// brief, and the eight acceptance criteria the packet states, in the order it states them.
///
/// <para><b>Why this file is arithmetic rather than screenshots.</b> Every claim MOVE-5f makes is a
/// claim about a number: the launch tick did not move, the coil deepened with the hold, the arc is
/// unchanged, the knee-less rig did not sink. <c>VerbPose</c> and <c>AnticipationCoil</c> exist so
/// those numbers are assertions here instead of captions under a photograph, and the two claims
/// that genuinely live in the engine — the drop and the vertical scale — are checked as source
/// audits with planted positive controls rather than asserted from a still frame.</para>
///
/// <para><b>Nothing here parks a non-default <c>MotorTuning.Current</c></b> except the two tests
/// that exist to prove the motor's live accessors route through the pure functions, and both of
/// those restore it in a <c>finally</c>. Every other test evaluates a candidate tuning through
/// <c>MotorTuningInvariants</c>, which is MOVE-4b's standing rule.</para>
/// </summary>
[Collection(MotorTuningStaticsCollection.Name)]
public class AnticipationCoilTests
{
    private const float Dt = AvatarMotor.TickDelta;

    private static void Near(float expected, float actual, float tol, string what)
        => Assert.True(Math.Abs(expected - actual) <= tol,
            $"{what}: expected {expected:F4} +/- {tol:F4}, got {actual:F4}");

    private static readonly MotorTuning Shipped = MotorTuning.Default;

    /// <summary>Mode 1 with the coil dialled all the way up and its window as short as the knob
    /// allows — the most aggressive approach-1 setting the panel can produce. Used wherever the
    /// claim is "approach 1 changes nothing about the simulation", because a no-op that only holds
    /// at a mild setting is not a no-op.</summary>
    private static readonly MotorTuning RealCoilHard = Shipped with
    {
        AnticipationMode = AnticipationCoil.RealCoil,
        AnticipationDepth = 1.00f,
        AnticipationCoilSec = 0.02f,
    };

    /// <summary>Mode 2 at its shipped slider start positions.</summary>
    private static readonly MotorTuning BakedDefault = Shipped with
    {
        AnticipationMode = AnticipationCoil.BakedCoil,
    };

    // =============================================================================================
    // 1. THE NO-OP (AC-4's foundation). AnticipationMode = 0 must be the jump Talon approved.
    // =============================================================================================

    /// <summary><c>GravityFor</c> exactly as it stood before MOVE-5f — the three-way branch plus
    /// MOVE-4c's hang, independently re-implemented so the comparison below is between two
    /// authorships rather than one function with itself.</summary>
    private static float GravityBeforeMove5f(in MotorTuning t, float velocityY, bool jumpHeld,
        bool locked)
    {
        float g;
        if (velocityY < 0f)
            g = t.Gravity * t.FallGravityMultiplier;
        else if (velocityY > 0f && !jumpHeld && !locked)
            g = t.Gravity * t.JumpReleaseGravityMultiplier;
        else
            g = t.Gravity;

        float s = t.ApexHangStrength;
        if (locked || !(s > 0f) || !(t.ApexHangWindowMps > 0f))
            return g;
        float u = MathF.Min(1f, MathF.Abs(velocityY) / t.ApexHangWindowMps);
        float w = 1f - (u * u * (3f - (2f * u)));
        return g * (1f - (s * w));
    }

    /// <summary>
    /// <b>AC-4's foundation, and the whole safety argument for touching a ratified motor.</b> At the
    /// shipped tuning <c>GravityFor</c> is bit-for-bit its pre-MOVE-5f self, over every branch, both
    /// signs of <c>v_y</c>, both held states, and all four locked/ballistic combinations —
    /// <i>including</i> the reshape from an early return into a multiply, which is the edit most
    /// likely to have changed a rounding.
    ///
    /// <para><b>The positive control is the point.</b> "The two agree" means nothing until the same
    /// sweep can report a disagreement, so a deliberately-broken variant (the release cut applied at
    /// <c>v_y >= 0</c> instead of <c>&gt; 0</c>, which differs on exactly one tick of the arc) is
    /// fed through the same comparison and must be caught.</para>
    /// </summary>
    [Fact]
    public void AtModeZero_GravityForIsBitIdenticalToItsPreMove5fSelf_AndTheSweepCanTellOtherwise()
    {
        // Stepped over INTEGERS and divided, so v_y = 0 — the branch boundary, and the one input a
        // float accumulator quietly steps over — is hit exactly.
        int compared = 0;
        int apexTicks = 0;
        foreach (bool locked in new[] { false, true })
        foreach (bool held in new[] { false, true })
        {
            for (int i = -240; i <= 240; i++)
            {
                float v = i / 20f;
                float now = AvatarMotor.GravityFor(v, held, locked);
                // MOVE-8 note: nothing changed here, deliberately. GravityBeforeMove5f is a
                // "before MOVE-5f" reference, which is AFTER MOVE-4c, so it already carries the
                // apex-hang term — and the hang leaving its no-op on Talon's ruling therefore costs
                // this sweep nothing. (Applying the factor a second time here was tried and is
                // wrong by 0.06%, which the exact Assert.Equal below catches instantly. That is the
                // sweep proving it is not blind.)
                float before = GravityBeforeMove5f(Shipped, v, held, locked);
                Assert.Equal(before, now);        // exact: no tolerance, no rounding allowance
                compared++;
                if (v == 0f)
                    apexTicks++;
            }
        }

        Assert.True(compared > 1500, $"only {compared} inputs swept — the grid is not a sweep");
        Assert.Equal(4, apexTicks);   // the boundary, once per locked/ballistic/held combination

        // POSITIVE CONTROL. The same comparison, against the smallest break the branch admits: the
        // release cut applied at v_y >= 0 instead of > 0, so a released body is cut on the apex tick
        // too. It differs at exactly one input, and the sweep must find it — otherwise the all-clear
        // above is worthless.
        int caught = 0;
        for (int i = -240; i <= 240; i++)
        {
            float v = i / 20f;
            float broken = GravityBeforeMove5f(Shipped, v, jumpHeld: false, locked: false);
            if (v == 0f)   // the break: the release cut applied at v_y >= 0 instead of > 0
                broken = AvatarMotor.Gravity * AvatarMotor.JumpReleaseGravityMultiplier
                    * AvatarMotor.ApexHangFactor(0f, Shipped.ApexHangStrength,
                        Shipped.ApexHangWindowMps);
            if (broken != AvatarMotor.GravityFor(v, jumpHeld: false, locked: false))
                caught++;
        }
        Assert.Equal(1, caught);   // exactly the apex tick, and nothing else
    }

    /// <summary><b>Approach 1 is pose-only, proved rather than asserted:</b> the gravity a rising
    /// body gets at mode 1 — with the coil at maximum depth and its shortest window — is the exact
    /// float the same body gets at mode 0. A pose that touched the arc would show up here on the
    /// first tick.</summary>
    [Fact]
    public void ModeOne_ChangesNoGravityAtAll_AtAnyDepthOrWindow()
    {
        foreach (bool held in new[] { false, true })
        for (float v = -12f; v <= 12f; v += 0.05f)
        {
            Assert.Equal(
                MotorTuningInvariants.GravityFor(Shipped, v, held),
                MotorTuningInvariants.GravityFor(RealCoilHard, v, held));
        }
    }

    // =============================================================================================
    // 2. AC-2 — NEITHER APPROACH DELAYS THE LAUNCH BY A SINGLE TICK.
    // =============================================================================================

    /// <summary>The vertical half of one tick of <c>AvatarMotor.Step</c>, in <c>Step</c>'s own
    /// order: gravity first and only when airborne, then <c>StepJump</c>, then the impulse. Written
    /// out rather than delegated so this is an independent statement of when a launch happens and
    /// not a tautology against the implementation.</summary>
    /// <param name="delayTicks">THE POSITIVE CONTROL. Withholds a resolved jump for this many ticks
    /// — the deferred launch the weight principle forbids and MOVE-5a rejected. At 0 this is the
    /// shipped motor.</param>
    private static (int LaunchTick, float LaunchVelocityY, float ApexM) SimulateLaunch(
        in MotorTuning t, int pressTick, int holdForTicks, int delayTicks = 0)
    {
        bool grounded = true;
        float vy = 0f, y = 0f, coyote = 0f, buffer = 0f, apex = 0f;
        int launchTick = -1, pending = -1;
        float launchVy = 0f;

        for (int tick = 0; tick < 400; tick++)
        {
            bool held = tick <= pressTick + holdForTicks;
            if (!grounded)
                vy -= MotorTuningInvariants.GravityFor(t, vy, held) * Dt;

            AvatarMotor.JumpResolution r = AvatarMotor.StepJump(
                grounded, coyote, buffer, jumpEdge: tick == pressTick, jumpAllowed: true, locked: false, Dt);
            coyote = r.CoyoteRemaining;
            buffer = r.JumpBufferRemaining;

            bool fires;
            if (delayTicks <= 0)
            {
                fires = r.Jumped;
            }
            else
            {
                if (r.Jumped)
                    pending = delayTicks;
                fires = pending == 0;
                if (pending >= 0)
                    pending--;
            }

            if (fires)
            {
                vy = t.JumpVelocity;
                if (launchTick < 0)
                {
                    launchTick = tick;
                    launchVy = vy;
                }
            }

            y += vy * Dt;
            if (y > apex)
                apex = y;
            // The floor. Without it the body counts as airborne from tick 0 at y = 0, and the press
            // is then served by the COYOTE window rather than by the ground — which still passes and
            // measures the wrong thing. A launch tick is only a launch tick if the body was standing
            // on something when the button went down.
            if (y <= 0f)
            {
                y = 0f;
                if (vy < 0f)
                    vy = 0f;
                grounded = true;
            }
            else
            {
                grounded = false;
            }
            if (grounded && launchTick >= 0 && tick > launchTick + 1)
                break;
        }
        return (launchTick, launchVy, apex);
    }

    /// <summary>
    /// <b>AC-2, with its positive control first.</b> The measurement is a one-tick measurement, so
    /// before it is trusted to report "no delay" it is shown detecting a delay of exactly one tick —
    /// a jump withheld for a single 16.7 ms frame, which is the smallest deferral the deferred-launch
    /// idea could possibly cost.
    ///
    /// <para>Then the real claim: <b>the launch tick and the launch velocity are identical at all
    /// three modes</b>, and identical to the shipped motor's. Mode 1 cannot move them because it is
    /// not in the simulation at all (it writes child transforms — see
    /// <see cref="TheTwoApproachOneKnobs_NeverAppearInAnythingThatSimulates"/>); mode 2 cannot move
    /// them because <c>Step</c> applies gravity only when <c>!prev.Grounded</c>, so the launch tick
    /// never reaches <c>GravityFor</c> — the body leaves the ground at exactly
    /// <c>JumpVelocity</c>, whatever the coil is set to.</para>
    /// </summary>
    [Fact]
    public void NeitherApproachDelaysTheLaunch_AndTheMethodCanDetectAOneTickDelay()
    {
        // ---- POSITIVE CONTROL, run BEFORE the absence check it licenses --------------------------
        (int baseTick, float baseVy, _) = SimulateLaunch(Shipped, pressTick: 3, holdForTicks: 999);
        (int lateTick, float lateVy, _) =
            SimulateLaunch(Shipped, pressTick: 3, holdForTicks: 999, delayTicks: 1);

        Assert.Equal(3, baseTick);
        Assert.Equal(4, lateTick);
        Assert.True(lateTick == baseTick + 1,
            "the harness did not see a deliberately deferred launch, so it cannot report that a "
            + "real one is absent");
        Assert.Equal(baseVy, lateVy);   // the control defers the launch; it does not weaken it

        // ---- THE ABSENCE CHECK -------------------------------------------------------------------
        foreach (MotorTuning t in new[] { Shipped, RealCoilHard, BakedDefault,
                     Shipped with { AnticipationMode = 2f, AnticipationBakedStrength = 2.00f,
                                    AnticipationBakedWindow = 1.00f } })
        {
            (int tick, float vy, _) = SimulateLaunch(t, pressTick: 3, holdForTicks: 999);
            Assert.Equal(baseTick, tick);
            Assert.Equal(AvatarMotor.JumpVelocity, vy);
        }
    }

    /// <summary>
    /// <b>The structural half of AC-2 for approach 1.</b> A pose cannot delay a launch it is not
    /// consulted by, and "not consulted" is a property of the file set rather than of an intention:
    /// the two knobs approach 1 reads (<c>AnticipationDepth</c>, <c>AnticipationCoilSec</c>) appear
    /// nowhere in anything that simulates, replicates or checksums a body — only in the tuning value
    /// that declares them, the table that bounds them, and the pose layer that reads them.
    ///
    /// <para><b>Positive control:</b> the same sweep, for approach 2's strength, which genuinely
    /// does live in <c>AvatarMotor</c> and must come back with a hit. Without it a typo in the file
    /// list would report a clean all-clear over nothing.</para>
    /// </summary>
    [Fact]
    public void TheTwoApproachOneKnobs_NeverAppearInAnythingThatSimulates()
    {
        string root = FindRepoRoot();
        string[] simulators =
        {
            Path.Combine(root, "scripts", "net", "AvatarMotor.cs"),
            Path.Combine(root, "scripts", "net", "MoveState.cs"),
            Path.Combine(root, "scripts", "net", "NetCodec.cs"),
            Path.Combine(root, "scripts", "net", "StateChecksum.cs"),
            Path.Combine(root, "scripts", "net", "MotorTuningInvariants.cs"),
        };

        foreach (string file in simulators)
        {
            Assert.True(File.Exists(file), $"the sweep is pointed at a file that does not exist: {file}");
            string text = File.ReadAllText(file);
            foreach (string knob in new[] { "AnticipationDepth", "AnticipationCoilSec" })
            {
                Assert.False(text.Contains(knob, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} reads {knob}. Approach 1 is a POSE: the moment a "
                    + "simulating file reads one of its knobs, the claim that it cannot cost a "
                    + "frame stops being structural.");
            }
        }

        // POSITIVE CONTROL: approach 2's strength IS a motor term and must be found.
        Assert.Contains("AnticipationBakedStrength",
            File.ReadAllText(Path.Combine(root, "scripts", "net", "AvatarMotor.cs")),
            StringComparison.Ordinal);
    }

    // =============================================================================================
    // 3. AC-3 — COIL DEPTH AGAINST HOLD DURATION, as arithmetic.
    // =============================================================================================

    /// <summary>How long an ascent lasts at a tuning: the launch speed over whichever gravity the
    /// rise gets. Held is <c>v/g</c>; a jump released on the first airborne tick is
    /// <c>v/(g × release)</c>. Derived rather than typed so the figures below move with the
    /// knobs.</summary>
    private static float AscentSec(in MotorTuning t, bool held)
        => t.JumpVelocity / (t.Gravity * (held ? 1f : t.JumpReleaseGravityMultiplier));

    /// <summary>
    /// <b>AC-3, stated as the arithmetic the packet asks for.</b> The relationship is
    /// <c>depth(t) = AnticipationDepth × smoothstep(min(1, t_rise / AnticipationCoilSec))</c>, and
    /// <c>t_rise</c> is bounded by the ascent, which is exactly what the release cut shortens.
    ///
    /// <para><b>MOVE-8 re-measured both ascents at Talon's ruled tuning</b>, and the relationship
    /// is what survived rather than the numbers. A held jump now rises for <c>8.4/24 = 0.3500 s</c>
    /// and a tap for <c>8.4/96 = 0.0875 s</c> — the held ascent lost one gravity step while the
    /// tap's fell by a third, because the release cut went 3.0 → 4.0. Against the same 0.22 s coil
    /// window that is <c>u = 1</c> and <c>u = 0.3977</c>, so the achieved depths are <b>1.000</b>
    /// and <b>0.349</b>. The tap's coil is 35% as deep, where it was 62% — <i>because the jump was
    /// cut short, and the ruling cuts it harder</i>. That is the reconciliation the packet's item 3
    /// asks for and the reason none of this needs to know the future.</para>
    ///
    /// <para>The closed form here is <c>JumpVelocity / (Gravity × cut)</c> and deliberately ignores
    /// the apex hang, which is why these two numbers are not the simulated ascents: the coil's
    /// depth is resolved against the rise the pose code models, not against the tick-accurate arc.
    /// Both are right for their own job, and the difference is under a tick at the shipped
    /// window.</para>
    /// </summary>
    [Fact]
    public void TheCoilDeepensWithTheHold_AndTheRelationshipIsTheOneStated()
    {
        float coilSec = Shipped.AnticipationCoilSec;
        Near(0.22f, coilSec, 1e-6f, "the shipped coil window");

        float heldAscent = AscentSec(Shipped, held: true);
        float tapAscent = AscentSec(Shipped, held: false);
        Near(0.3500f, heldAscent, 5e-4f, "the held ascent");
        Near(0.0875f, tapAscent, 5e-4f, "the tapped ascent");

        Near(1.000f, AnticipationCoil.AchievedDepth(heldAscent, coilSec), 5e-4f, "the held coil");
        Near(0.349f, AnticipationCoil.AchievedDepth(tapAscent, coilSec), 5e-4f, "the tapped coil");

        // The closed form, restated independently of the implementation.
        float u = tapAscent / coilSec;
        Near(u * u * (3f - (2f * u)), AnticipationCoil.Depth(tapAscent, coilSec), 1e-6f,
            "Depth is smoothstep(riseSec / coilSec)");
    }

    /// <summary><b>Monotone, and saturating.</b> Every extra tick of rise buys coil and none loses
    /// it — the same property <c>JumpApex_IsMonotonicInHoldDuration</c> asserts of the height, which
    /// is what makes the coil a legible READ of the height rather than a second, differently-shaped
    /// story about the same press.</summary>
    [Fact]
    public void DepthIsMonotonicInRiseTime_AndSaturatesAtOne()
    {
        float previous = -1f;
        for (int tick = 0; tick <= 60; tick++)
        {
            float d = AnticipationCoil.Depth(tick * Dt, 0.22f);
            Assert.True(d >= previous - 1e-6f,
                $"{tick} ticks of rise gave a SHALLOWER coil ({d:F4}) than {tick - 1} ({previous:F4})");
            Assert.InRange(d, 0f, 1f);
            previous = d;
        }
        Assert.Equal(1f, AnticipationCoil.Depth(10f, 0.22f), 1e-6f);
        Assert.Equal(0f, AnticipationCoil.Depth(0f, 0.22f));
        Assert.Equal(0f, AnticipationCoil.Depth(-1f, 0.22f));
        Assert.Equal(0f, AnticipationCoil.Depth(1f, 0f));      // the divisor guard
    }

    /// <summary>
    /// <b>The coil starts and stops without a corner — demonstrated, not asserted.</b> Smoothstep
    /// gives <c>w'(0) = w'(1) = 0</c>, so the pose neither twitches on the take-off frame nor snaps
    /// when it reaches full depth.
    ///
    /// <para><b>The positive control is a linear ramp</b>, the shape this rejects: continuous in the
    /// value and not in its slope. It is the same control
    /// <c>ApexHangAndLandingDipTests.TheHangIsC1AtBothEnds</c> uses, and for the same reason — the
    /// smoothstep's result means nothing until the measurement can tell the two apart.</para>
    /// </summary>
    [Fact]
    public void TheCoilIsC1AtBothEnds_AndALinearRampWouldNotBe()
    {
        const float W = 0.22f, H = 0.0005f;
        float smooth = MaxSecondDifference(t => AnticipationCoil.Depth(t, W), W, H);
        float linear = MaxSecondDifference(t => Math.Clamp(t / W, 0f, 1f), W, H);

        Assert.True(linear > smooth * 5f,
            $"the measurement cannot tell a smoothstep ({smooth:E2}) from a linear ramp "
            + $"({linear:E2}), so it proves nothing about either");
        Assert.True(smooth < 1e-2f, $"the coil has a corner: second difference {smooth:E2}");
    }

    private static float MaxSecondDifference(Func<float, float> f, float window, float h)
    {
        float worst = 0f;
        for (float t = h * 2f; t <= window * 1.6f; t += h)
        {
            float d2 = Math.Abs(f(t + h) - (2f * f(t)) + f(t - h));
            if (d2 > worst)
                worst = d2;
        }
        return worst;
    }

    // =============================================================================================
    // 4. AC-4 — THE APPROVED ARC, AT THE SHIPPED SETTING AND AT MODE 1.
    // =============================================================================================

    /// <summary>MOVE-3d's reporting convention, the one Talon's figures are quoted in: the playground
    /// latches take-off AFTER the launch tick has already advanced the body, so its apex is measured
    /// from the post-launch height and its airtime is one tick shorter. Both offsets are exact.
    /// Transcribed from <c>ApexHangAndLandingDipTests.AsPlayed</c> deliberately — two files quoting
    /// the same convention is what stops one of them drifting into a different jump.</summary>
    private static (float ApexM, float AirtimeSec) AsPlayed(in MotorTuning t, int holdForTicks)
    {
        (float apex, float airtime) = MotorTuningInvariants.SimulateJump(t, holdForTicks);
        return (apex - (t.JumpVelocity * Dt), airtime - Dt);
    }

    /// <summary>
    /// <b>AC-4. The arcs are identical at the shipped setting AND at mode 1</b> — held
    /// 1.407 / 0.667 and tap 0.300 / 0.233 since MOVE-8 re-measured them at Talon's ruled tuning,
    /// to three decimals, at both. <b>The claim is the IDENTITY between the two modes, not the four
    /// literals</b>, and the identity is what mode 1's "pose only" promise rests on — it is
    /// asserted exactly, with no tolerance at all, at the bottom of this test.
    ///
    /// <para><b>Which setting ships, and why.</b> Mode 0 and mode 1 both reproduce the approved arc
    /// exactly; mode 1 does so because it is pose only and writes no velocity, no gravity and no
    /// wire byte. <b>It still ships at 0</b>, for the reason every MOVE-4 and MOVE-5 row ships at
    /// its no-op: mode 1 changes the BODY Talon approved even though it leaves the arc alone, and
    /// the lab exists so he picks that with his hands. <b>Mode 2 cannot reproduce the arc and is not
    /// supposed to</b> — it is a gravity term, so shipping it on would be shipping a retune.</para>
    /// </summary>
    [Fact]
    public void TheApprovedArcIsUnchanged_AtModeZeroAndAtModeOne()
    {
        foreach (MotorTuning t in new[] { Shipped, RealCoilHard })
        {
            (float apexHeld, float airtimeHeld) = AsPlayed(t, int.MaxValue);
            (float apexTap, float airtimeTap) = AsPlayed(t, 0);

            Near(1.407f, apexHeld, 0.0005f, "held sprint apex");
            Near(0.667f, airtimeHeld, 0.0005f, "held sprint airtime");
            Near(0.300f, apexTap, 0.0005f, "jog tap apex");
            Near(0.233f, airtimeTap, 0.0005f, "jog tap airtime");
        }

        // And the identity is EXACT, not merely inside three decimals.
        Assert.Equal(MotorTuningInvariants.SimulateJump(Shipped, int.MaxValue),
            MotorTuningInvariants.SimulateJump(RealCoilHard, int.MaxValue));
        Assert.Equal(MotorTuningInvariants.SimulateJump(Shipped, 0),
            MotorTuningInvariants.SimulateJump(RealCoilHard, 0));
    }

    /// <summary><b>Mode 2 is a real second candidate, not a second spelling of mode 1.</b> It takes
    /// height away — that is what "baked into the rise curve" means — and the direction is asserted
    /// so a future edit cannot quietly turn it into a no-op and leave the toggle with two identical
    /// positions.</summary>
    [Fact]
    public void ModeTwo_TakesHeightRatherThanAdding_AndTheToggleHasThreeDistinctPositions()
    {
        (float apexOff, float airtimeOff) = AsPlayed(Shipped, int.MaxValue);
        (float apexBaked, float airtimeBaked) = AsPlayed(BakedDefault, int.MaxValue);

        Assert.True(apexBaked < apexOff,
            $"the baked coil did not shorten the rise: {apexBaked:F4} vs {apexOff:F4}");
        Assert.True(airtimeBaked < airtimeOff);
        // A tap is coiled too — the term is keyed to how much launch speed is left, not to the
        // button — so the carriable range survives rather than collapsing at one end.
        Assert.True(AsPlayed(BakedDefault, 0).ApexM < AsPlayed(Shipped, 0).ApexM);
    }

    /// <summary>
    /// <b>The pin on approach 2, live.</b> Mode 2 is a gravity term, so a big enough strength walks
    /// the approved arc out of <c>JumpApex_BracketsTheSpecRange</c> — and the bound is coupled to
    /// the window, <c>JumpVelocity</c> and both gravities, so it is a bisection rather than a
    /// number. Unbounded while the mode is not 2, which is the shipped state: an inert term cannot
    /// constrain anything.
    /// </summary>
    [Fact]
    public void TheBakedStrengthCeiling_IsInertWhenOff_AndRealWhenOn()
    {
        Assert.Equal(2.00f, MotorTuningInvariants.LaunchCoilStrengthCeiling(Shipped));
        Assert.Equal(1.00f, MotorTuningInvariants.LaunchCoilWindowCeiling(Shipped));

        float ceiling = MotorTuningInvariants.LaunchCoilStrengthCeiling(BakedDefault);
        Assert.InRange(ceiling, 0.001f, 2.00f);
        Assert.True(MotorTuningInvariants.JumpApexAssertionsHold(
                BakedDefault with { AnticipationBakedStrength = ceiling }),
            $"the bisected ceiling {ceiling:F3} does not itself hold");
        Assert.False(MotorTuningInvariants.JumpApexAssertionsHold(
                BakedDefault with { AnticipationBakedStrength = ceiling + 0.05f }),
            $"one slider step past the ceiling {ceiling:F3} still passes — the bound is not tight");
    }

    // =============================================================================================
    // 5. AC-5 — A KNEE-LESS RIG NEITHER FOLDS NOR DROPS THE HIP.
    // =============================================================================================

    /// <summary>
    /// <b>AC-5, structurally.</b> MOVE-5c found that <c>SolveLeg</c> cannot express a fold without a
    /// shin — every <c>.glb</c> but the greybox — so a pose that drops the hips for a fold that never
    /// happened sinks the body 25 cm through the floor. Its fix was to gate the DROP on
    /// <c>HasKnees</c>.
    ///
    /// <para><b>The coil needs no gate because it has no drop to gate, and that is a property of the
    /// source rather than of a screenshot:</b> <c>CrouchDropM</c> is assigned from
    /// <c>groundDropFold</c> alone, and <c>groundDropFold</c> is the landing absorb plus the verb's
    /// gated fold — the coil term is not in it, and cannot be, because the coil is multiplied by
    /// <c>_airborne</c> and an airborne fold lifts a foot rather than dropping a hip.</para>
    ///
    /// <para><b>Positive control:</b> the exact defect, planted as source text, fed to the same
    /// matcher.</para>
    /// </summary>
    [Fact]
    public void TheCoilContributesNothingToTheHipDrop_AndTheMatcherWouldSeeItIfItDid()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "scripts", "game", "sandbox", "AvatarVisual.cs"));

        // The two lines the guarantee rests on, still exactly as MOVE-5c left them.
        Assert.Contains("float groundDropFold = Mathf.Min(landFold + verbDropFold, MaxLimbFold);",
            source, StringComparison.Ordinal);
        // THE DROP IS STILL DERIVED FROM groundDropFold AND FROM NOTHING ELSE — asserted as a
        // property of the assignment rather than as one exact spelling of it.
        //
        // WHY THIS IS NOT A WEAKENING (BIKE-4A, 2026-09-02). It was
        // `Assert.Contains("CrouchDropM = _legLengthM * groundDropFold;")`, and the ride channel
        // reddened it by scaling the drop out while a body is mounted — a rider's weight is on the
        // saddle, so a mounted landing absorb that dropped the hips would sink the body through the
        // bike. That is a correct change to a correct line, and swapping in a second exact literal
        // would only move the same tripwire one edit further along. What MOVE-5f's guarantee
        // actually needs is (a) the drop comes from groundDropFold and (b) no coil term is in it —
        // (b) is Assert.Empty below, unchanged and still the load-bearing half, and this is (a),
        // now stated as "the assignment mentions groundDropFold and nothing else that folds".
        // The two `CrouchDropM = 0f;` parks (the rig reset and the incapacity park) are not the
        // derivation; the one assignment that DERIVES the drop is the one this is about.
        string dropLine = source.Split('\n')
            .Select(l => l.Trim())
            .Single(l => l.StartsWith("CrouchDropM = ", StringComparison.Ordinal)
                      && !l.Equals("CrouchDropM = 0f;", StringComparison.Ordinal));
        Assert.Contains("_legLengthM", dropLine, StringComparison.Ordinal);
        Assert.Contains("groundDropFold", dropLine, StringComparison.Ordinal);
        foreach (string forbidden in new[] { "coil", "tuckFold", "snapFold", "kneeFold" })
            Assert.DoesNotContain(forbidden, dropLine, StringComparison.OrdinalIgnoreCase);
        // MOVE-5c's own gate, still in place — the coil composing with it is the whole point.
        Assert.Contains("float verbDropFold = HasKnees ? verbPose.KneeFold : 0f;",
            source, StringComparison.Ordinal);

        Assert.Empty(DropLinesMentioningTheCoil(source));

        // POSITIVE CONTROL: exactly the defect, and the matcher must find it.
        Assert.NotEmpty(DropLinesMentioningTheCoil(
            "CrouchDropM = _legLengthM * (groundDropFold + coilFold);"));
        Assert.NotEmpty(DropLinesMentioningTheCoil(
            "float groundDropFold = Mathf.Min(landFold + verbDropFold + coilPose.KneeFold, MaxLimbFold);"));
    }

    private static IEnumerable<string> DropLinesMentioningTheCoil(string source)
        => source.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal))
            .Where(l => l.Contains("CrouchDropM =", StringComparison.Ordinal)
                     || l.Contains("groundDropFold =", StringComparison.Ordinal))
            .Where(l => l.Contains("coil", StringComparison.OrdinalIgnoreCase));

    // =============================================================================================
    // 6. AC-6 — COMPOSITION: no double reading of one event, no vertical scale term.
    // =============================================================================================

    /// <summary>
    /// <b>No vertical scale term, structurally.</b> RIG-1 deleted <c>LandCompressY</c> and left the
    /// argument beside it at <c>AvatarVisual.cs</c> ~line 480: "a squashed mesh and a bent knee are
    /// two different readings of the same landing, and a body that does both is a body doing it
    /// twice." A coil is the same event, so the coil's pose has four channels and none of them is a
    /// scale — asserted off the type itself, which cannot go stale the way a comment can.
    /// </summary>
    [Fact]
    public void TheCoilPoseHasNoVerticalScaleChannel_AndTheProhibitionIsStillOnRecord()
    {
        string[] channels = typeof(AnticipationCoil.Targets)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "ArmFold", "ArmSweepRad", "ForwardTiltRad", "KneeFold" }, channels);

        string source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "scripts", "game", "sandbox", "AvatarVisual.cs"));
        Assert.Contains("Do not reintroduce it: a", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LandCompressY =", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The coil composes with everything already on the knee, and the clamp is what makes that
    /// safe.</b> The deepest stack the body can reach is a full coil on the lead leg of a launch
    /// snap with the apex tuck running — 0.24 + 0.38 + 0.18 = 0.80 of leg length, which is well past
    /// <c>MaxLimbFold</c>. That is not a defect; it is the case <c>MaxLimbFold</c>'s own comment
    /// names ("a landing out of a launch snap stacks two of them"), and the clamp is asserted to
    /// bite rather than assumed to.
    /// </summary>
    [Fact]
    public void TheCoilStacksWithTheSnapAndTheTuck_AndMaxLimbFoldIsWhatBoundsIt()
    {
        float coil = AnticipationCoil.For(1f).KneeFold;
        Near(0.24f, coil, 1e-6f, "the coil's full fold");

        // 2 * acos(1 - fold) on equal segments: 81.1 degrees of knee, between the apex tuck's and
        // the launch snap's. Stated as arithmetic because "the knee visibly bends" is a number.
        float kneeDeg = 2f * MathF.Acos(1f - coil) * 180f / MathF.PI;
        Near(81.1f, kneeDeg, 0.1f, "the coil's knee angle on equal segments");

        float stacked = coil + 0.38f + 0.18f;                    // coil + launch snap + apex tuck
        Assert.True(stacked > AvatarVisual.MaxLimbFoldForTools,
            "the stack no longer exceeds the clamp, so this test has stopped measuring anything");
        Assert.Equal(AvatarVisual.MaxLimbFoldForTools, Math.Clamp(stacked, 0f, AvatarVisual.MaxLimbFoldForTools));
    }

    /// <summary><b>The coil's torso gather fits inside the tilt clamp with everything else.</b>
    /// 0.22 rad against <c>MaxBodyTilt</c>'s 0.60, and against the rise tilt it opposes (0.10 rad
    /// back) it nets ~0.12 rad forward — a 19-degree swing in the outline between a coiled body and
    /// an uncoiled one at the same point of the same rise, which is the quantity that survives
    /// distance.</summary>
    [Fact]
    public void TheCoilsGatherOpposesTheRiseTilt_AndBothFitInsideTheClamp()
    {
        float coilTilt = AnticipationCoil.For(1f).ForwardTiltRad;
        Near(0.22f, coilTilt, 1e-6f, "the coil's full gather");
        Assert.True(coilTilt < AvatarVisual.MaxBodyTilt);

        // The rig's forward is negative; JumpRiseTiltRad (0.10) is positive/back at full drive.
        float uncoiled = 0.10f;
        float coiled = 0.10f - coilTilt;
        Near(-0.12f, coiled, 1e-4f, "a fully coiled body at the top of a held rise");
        float swingDeg = (uncoiled - coiled) * 180f / MathF.PI;
        Near(12.6f, swingDeg, 0.2f, "the swing between coiled and uncoiled");
    }

    /// <summary><b>The coil and the apex hang do not fight.</b> Mode 1 changes no gravity at all
    /// (asserted above), and mode 2's factor multiplies whichever branch applied — exactly as the
    /// hang's does — so the branch ORDERING survives both terms: a released rising body always gets
    /// harsher gravity than a held one, at every <c>v_y</c> and at every combination of the two
    /// terms. That is what makes mashing the key unable to buy height.</summary>
    [Fact]
    public void TheBranchOrderingSurvivesBothTermsTogether()
    {
        MotorTuning both = Shipped with
        {
            ApexHangStrength = 0.30f,
            AnticipationMode = 2f,
            AnticipationBakedStrength = 1.20f,
            AnticipationBakedWindow = 0.80f,
        };
        for (float v = 0.01f; v <= 8.4f; v += 0.01f)
        {
            float released = MotorTuningInvariants.GravityFor(both, v, jumpHeld: false);
            float held = MotorTuningInvariants.GravityFor(both, v, jumpHeld: true);
            Assert.True(released > held,
                $"at v_y = {v:F2} the released branch ({released:F3}) is not harsher than the held "
                + $"one ({held:F3}) — mashing the key would buy height");
        }
    }

    // =============================================================================================
    // 7. THE SHAPE OF APPROACH 2, and its guards.
    // =============================================================================================

    [Fact]
    public void LaunchCoilFactor_IsFullAtLaunchAndGoneAtTheWindowEdge()
    {
        const float V = 8.4f, S = 0.35f, W = 0.35f;
        Assert.Equal(1f + S, AvatarMotor.LaunchCoilFactor(V, V, S, W), 1e-6f);
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(V * (1f - W), V, S, W), 1e-6f);
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(V * 0.3f, V, S, W), 1e-6f);

        // Rising only, strictly. A body walking off a ledge never rose and is never coiled; the apex
        // tick and the whole descent are the ones that shipped.
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(0f, V, S, W));
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(-4f, V, S, W));

        // Every guard is an exact 1, never a NaN or a divide.
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(V, V, 0f, W));
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(V, V, S, 0f));
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(V, 0f, S, W));
        Assert.Equal(1f, AvatarMotor.LaunchCoilFactor(V, V, float.NaN, W));

        // Never below 1: the coil ADDS gravity. A term that could subtract it would be a second,
        // unbounded apex hang wearing this one's name.
        for (float v = 0.01f; v <= V; v += 0.01f)
            Assert.True(AvatarMotor.LaunchCoilFactor(v, V, S, W) >= 1f);
    }

    /// <summary><b>The mirror does not drift.</b> <c>MotorTuningInvariants.GravityFor</c>
    /// re-implements the baked coil rather than delegating to it, so this sweeps the two authorships
    /// against each other across a grid of strengths and windows — a drift guard at NON-default
    /// values, which is what the apex hang needed and got.</summary>
    [Fact]
    public void TheInvariantMirrorsTheBakedCoilShape_AtEveryStrengthAndWindow()
    {
        for (float s = 0.05f; s <= 2.00f; s += 0.15f)
        for (float w = 0.05f; w <= 1.00f; w += 0.05f)
        {
            MotorTuning t = Shipped with
            {
                AnticipationMode = 2f,
                AnticipationBakedStrength = s,
                AnticipationBakedWindow = w,
            };
            for (float v = 0.05f; v <= 8.4f; v += 0.25f)
            {
                // MOVE-8: the apex hang is live at the shipped tuning, so the mirror's output
                // carries it too. Both terms are in the expectation, and the ORDER matters — the
                // hang scales the branch and the coil scales the result, exactly as GravityFor
                // composes them.
                float expected = AvatarMotor.Gravity
                    * AvatarMotor.ApexHangFactor(v, t.ApexHangStrength, t.ApexHangWindowMps)
                    * AvatarMotor.LaunchCoilFactor(v, t.JumpVelocity, s, w);
                Near(expected, MotorTuningInvariants.GravityFor(t, v, jumpHeld: true), 1e-3f,
                    $"the mirror at s={s:F2} w={w:F2} v={v:F2}");
            }
        }
    }

    /// <summary><b>The live motor really routes through the pure function</b>, which the tests above
    /// cannot show because they all evaluate a candidate tuning through the mirror. Parks
    /// <c>MotorTuning.Current</c> for the length of one assertion and restores it in a
    /// <c>finally</c>, the same shape <c>MotorTuningTests</c> uses.</summary>
    [Fact]
    public void TheLiveGravityForActuallyReadsTheModeAndTheTwoBakedKnobs()
    {
        try
        {
            MotorTuning.SetSessionProbeForTests(() => false);
            Assert.True(MotorTuning.TryApply(BakedDefault, out _));
            Assert.Equal(2, AvatarMotor.AnticipationMode);

            float expected = AvatarMotor.Gravity * AvatarMotor.LaunchCoilFactor(
                8.0f, AvatarMotor.JumpVelocity, AvatarMotor.AnticipationBakedStrength,
                AvatarMotor.AnticipationBakedWindow);
            Assert.Equal(expected,
                AvatarMotor.GravityFor(8.0f, jumpHeld: true, locked: false),
                1e-5f);

            // Still gated off for the two cases every gravity term is gated off for.
            Assert.Equal(AvatarMotor.Gravity,
                AvatarMotor.GravityFor(8.0f, true, locked: true), 1e-5f);
        }
        finally
        {
            MotorTuning.ResetForTests();
        }
    }

    /// <summary><b>Mode 1 puts no gravity term on the live motor either</b>, which is the claim the
    /// toggle rests on: switching approach 1 on must change the body and nothing else.</summary>
    [Fact]
    public void TheLiveGravityForIsUntouchedAtModeOne()
    {
        try
        {
            MotorTuning.SetSessionProbeForTests(() => false);
            float[] before = Sample();
            Assert.True(MotorTuning.TryApply(RealCoilHard, out _));
            Assert.Equal(1, AvatarMotor.AnticipationMode);
            Assert.Equal(before, Sample());
        }
        finally
        {
            MotorTuning.ResetForTests();
        }

        static float[] Sample()
        {
            var s = new List<float>();
            foreach (bool held in new[] { false, true })
                for (float v = -9f; v <= 9f; v += 0.25f)
                    s.Add(AvatarMotor.GravityFor(v, held, locked: false));
            return s.ToArray();
        }
    }

    // =============================================================================================
    // 8. THE TOGGLE AND THE TABLE (AC-1).
    // =============================================================================================

    /// <summary><b>AC-1: both approaches behind one toggle, plus off, selectable at runtime from the
    /// knob table.</b> Five rows, one group, defaults that agree with
    /// <c>MotorTuning.Default</c>, and a mode whose whole range is exactly the three positions the
    /// brief asks for.</summary>
    [Fact]
    public void TheFiveRowsExist_AgreeWithTheirDefaults_AndTheModeHasExactlyThreePositions()
    {
        string[] names =
        {
            "AnticipationMode", "AnticipationDepth", "AnticipationCoilSec",
            "AnticipationBakedStrength", "AnticipationBakedWindow",
        };

        foreach (string name in names)
        {
            Assert.True(MotorTuningKnobs.TryByName(name, out MotorKnob knob),
                $"{name} is not in the knob table, so no panel will ever show it");
            Assert.Equal("Anticipation", knob.Group);
            Assert.Equal(knob.Default, knob.Get(MotorTuning.Default));
            Assert.InRange(knob.Default, knob.Min, knob.Max);
            Assert.NotEqual("", knob.BoundReason);
        }

        MotorTuningKnobs.TryByName("AnticipationMode", out MotorKnob mode);
        Assert.Equal(0f, mode.Default);
        Assert.Equal(0f, mode.Min);
        Assert.Equal(2f, mode.Max);
        Assert.Equal(1f, mode.Step);
        Assert.Equal(AnticipationCoil.Off, (int)mode.Default);

        // The three positions are the three the class names, and the motor's own gate agrees with
        // them — a constant spelled twice is a constant that can drift.
        Assert.Equal(0, AnticipationCoil.Off);
        Assert.Equal(1, AnticipationCoil.RealCoil);
        Assert.Equal(2, AnticipationCoil.BakedCoil);

        // The rows are CONSECUTIVE in All, or the panel renders two sections with one name.
        var order = MotorTuningKnobs.All.Select(k => k.Name).ToList();
        int first = order.IndexOf("AnticipationMode");
        Assert.True(first >= 0);
        Assert.Equal(names, order.Skip(first).Take(5).ToArray());
    }

    /// <summary><b>The validator clamps the mode into its three positions</b>, not merely the
    /// widget — a hand-edited file or a stale saved tuning must not be able to select a fourth
    /// approach that does not exist.</summary>
    [Fact]
    public void TheModeIsClampedByTheValidator_AndAnUnknownModeIsInert()
    {
        MotorTuning high = MotorTuning.Validate(Shipped with { AnticipationMode = 9f }, out _);
        Assert.Equal(2f, high.AnticipationMode);
        MotorTuning low = MotorTuning.Validate(Shipped with { AnticipationMode = -4f }, out _);
        Assert.Equal(0f, low.AnticipationMode);
        MotorTuning nan = MotorTuning.Validate(Shipped with { AnticipationMode = float.NaN }, out _);
        Assert.Equal(0f, nan.AnticipationMode);

        // And a mode the validator somehow let through changes nothing, because both readers test
        // for their own value rather than for "not zero".
        MotorTuning bogus = Shipped with { AnticipationMode = 1.4f };
        for (float v = 0.05f; v <= 8.4f; v += 0.25f)
            Assert.Equal(MotorTuningInvariants.GravityFor(Shipped, v, true),
                MotorTuningInvariants.GravityFor(bogus, v, true));
    }

    /// <summary><b>The zero pose is exact.</b> A body outside a coil — and every body at all while
    /// the mode is off — gets four exact zeros, so it is bit-identical to what shipped before this
    /// class existed.</summary>
    [Fact]
    public void AnUncoiledBodyGetsTheExactZeroPose()
    {
        Assert.Equal(AnticipationCoil.None, AnticipationCoil.For(0f));
        Assert.Equal(AnticipationCoil.None, AnticipationCoil.For(-1f));
        Assert.Equal(0f, AnticipationCoil.None.KneeFold);
        Assert.Equal(0f, AnticipationCoil.None.ForwardTiltRad);
        Assert.Equal(0f, AnticipationCoil.None.ArmFold);
        Assert.Equal(0f, AnticipationCoil.None.ArmSweepRad);

        // Linear in the weight, and clamped at 1 — a weight past 1 is a bug upstream and must not
        // become an amplitude past the authored pose.
        AnticipationCoil.Targets half = AnticipationCoil.For(0.5f);
        Near(AnticipationCoil.CoilKneeFold * 0.5f, half.KneeFold, 1e-6f, "half a coil");
        Assert.Equal(AnticipationCoil.For(1f), AnticipationCoil.For(4f));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
