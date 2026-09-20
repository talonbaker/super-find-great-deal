using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-1's arithmetic, asserted rather than described.</b> Every number quoted in the MOVE-1
/// report is here, and the load-bearing one is <see cref="StanceFootDoesNotSlip_AtEverySpeed"/> —
/// "the feet are planted" is a measurement, not an opinion, and this is where it is taken without
/// an engine in the way.
/// </summary>
public class LocomotionTests
{
    /// <summary>Float equality within a tolerance. xUnit's <c>Assert.Equal(float, float, int)</c>
    /// is ambiguous against its <c>(double, double, int)</c> overload here, and a named helper is
    /// clearer than a cast at every call site anyway.</summary>
    private static void Near(float expected, float actual, float eps = 1e-4f) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"expected {expected:F6}, got {actual:F6} (tolerance {eps:G})");

    private static readonly float[] Speeds =
    {
        0.5f, 1.0f, LocomotionProfile.WalkSpeedMps, 3.5f, LocomotionProfile.JogSpeedMps,
        7.0f, LocomotionProfile.SprintSpeedMps,
    };

    // =============================================================================================
    // The identity. Everything else in the gait is downstream of this.
    // =============================================================================================

    [Fact]
    public void StrideTimesCadence_IsExactlyGroundSpeed()
    {
        foreach (float v in Speeds)
        {
            float product = LocomotionProfile.StepLengthAt(v) * LocomotionProfile.CadenceAt(v);
            Assert.True(Mathf.Abs(product - v) < 1e-4f,
                $"at {v:F2} m/s the gait covers {product:F4} m/s — the identity is broken and the " +
                "feet will skate no matter what the amplitudes are");
        }
    }

    /// <summary>
    /// <b>The whole rewrite, in one assertion.</b> Walks a foot through its stance at a fixed ground
    /// speed and measures how far the contact point moves <i>in the world</i>. The shipped waddle
    /// had no such quantity at all — the feet never left their rest position on the fore-aft axis —
    /// so there was nothing to measure and everything to skate.
    /// </summary>
    [Fact]
    public void StanceFootDoesNotSlip_AtEverySpeed()
    {
        const float leg = 0.36f; // the greybox's measured hip height
        const float dt = 1f / 60f;

        foreach (float v in Speeds)
        {
            float duty = LocomotionProfile.DutyFactorAt(v, leg);
            float reach = LocomotionProfile.StanceReachAt(v, leg);
            float cadence = LocomotionProfile.CadenceAt(v);

            float worst = 0f;
            float bodyX = 0f;
            float phase = 1e-4f; // just inside stance
            float? prevWorld = null;
            while (phase < duty)
            {
                // Foot behind the hip is +track; the body has travelled bodyX forward, so the
                // contact point's world coordinate is bodyX minus how far back the foot sits.
                float world = bodyX - LocomotionProfile.FootTrackAt(phase, duty, reach);
                if (prevWorld is float p)
                    worst = Mathf.Max(worst, Mathf.Abs(world - p));
                prevWorld = world;

                bodyX += v * dt;
                phase += dt * cadence * 0.5f;
            }

            Assert.True(worst < 1e-4f,
                $"at {v:F2} m/s a planted foot slides {worst * 1000f:F3} mm per frame in the world " +
                $"(duty {duty:F3}, reach {reach:F3} m) — that is the skate this packet exists to remove");
        }
    }

    /// <summary>
    /// <b>The same measurement, swept across every leg length in the roster's range</b> — and the
    /// test that would have caught the defect the headless suite found instead.
    ///
    /// <para>An earlier cut clamped the duty up to a floor of 0.05, which looked defensive and
    /// broke the identity: <c>duty</c> must equal <c>reach · cadence / speed</c> <i>exactly</i>, and
    /// raising it stretches the stance over more of the cycle than the reach can cover. On an
    /// -uffling's 0.08 m legs at 7 m/s the true duty is 0.024, so a "planted" foot slid 0.10 m
    /// against 0.21 m of body travel. The greybox's 0.36 m legs never trip it — which is exactly
    /// why the single-leg-length test above passed while the engine failed.</para>
    /// </summary>
    [Theory]
    [InlineData(0.06f)]  // shorter than any body in the roster
    [InlineData(0.08f)]  // the -uffling foot node the headless suite actually measures
    [InlineData(0.20f)]
    [InlineData(0.36f)]  // the greybox
    [InlineData(0.60f)]  // a taller cast member than anything specced
    public void StanceFootDoesNotSlip_AtEveryLegLength(float leg)
    {
        foreach (float v in Speeds)
        {
            float duty = LocomotionProfile.DutyFactorAt(v, leg);
            float reach = LocomotionProfile.StanceReachAt(v, leg);
            float cadence = LocomotionProfile.CadenceAt(v);
            if (duty <= 0f || cadence <= 0f)
                continue; // no stance at all on this body at this speed — nothing to slide.

            // Sub-frame sampling: on a short leg the stance can be narrower than one frame, and a
            // per-frame walk would step straight over it and measure nothing. The slip is a
            // property of the curve, not of the sample rate.
            float step = duty / 64f;
            float secondsPerPhase = 2f / cadence;
            float worst = 0f;
            float bodyX = 0f;
            float? prevWorld = null;
            for (float phase = 0f; phase < duty; phase += step)
            {
                float world = bodyX - LocomotionProfile.FootTrackAt(phase, duty, reach);
                if (prevWorld is float p)
                    worst = Mathf.Max(worst, Mathf.Abs(world - p));
                prevWorld = world;
                bodyX += v * step * secondsPerPhase;
            }

            Assert.True(worst < 1e-4f,
                $"leg {leg:F2} m at {v:F2} m/s: a planted foot slides {worst * 1000f:F4} mm per " +
                $"sample (duty {duty:F4}, reach {reach:F4} m, cadence {cadence:F2}) — the identity " +
                "duty = reach * cadence / speed is not holding");
        }
    }

    [Fact]
    public void StanceFoot_NeverLifts_AndSwingFootAlwaysDoes()
    {
        const float leg = 0.36f;
        float v = LocomotionProfile.JogSpeedMps;
        float duty = LocomotionProfile.DutyFactorAt(v, leg);
        float reach = LocomotionProfile.StanceReachAt(v, leg);

        float peak = 0f;
        for (int i = 0; i <= 200; i++)
        {
            float p = i / 200f;
            float lift = LocomotionProfile.FootLiftAt(p, duty, reach);
            if (p < duty)
                Assert.Equal(0f, lift);
            else
                peak = Mathf.Max(peak, lift);
        }
        Assert.True(peak > 0.02f, $"a swinging foot only cleared {peak:F4} m — that is a shuffle");
    }

    /// <summary>
    /// <b>The swing gathers backwards before it drives forwards</b> (MOVE-1, Talon's second
    /// amendment: "anticipation must move OPPOSITE the action before it moves with it"). A swing
    /// that started forward on its first frame would be the smooth-curve mush the amendment is
    /// about; this asserts the foot genuinely travels the wrong way first, and overshoots past its
    /// plant point before settling onto it.
    /// </summary>
    [Fact]
    public void SwingFoot_GathersBackwards_ThenOvershootsItsPlant()
    {
        const float leg = 0.36f;
        float v = LocomotionProfile.JogSpeedMps;
        float duty = LocomotionProfile.DutyFactorAt(v, leg);
        float reach = LocomotionProfile.StanceReachAt(v, leg);

        float atToeOff = LocomotionProfile.FootTrackAt(duty + 1e-4f, duty, reach);
        float gatherPeak = float.MinValue;
        float drivePeak = float.MaxValue;
        for (int i = 0; i <= 400; i++)
        {
            float p = duty + ((1f - duty) * i / 400f);
            float t = LocomotionProfile.FootTrackAt(p, duty, reach);
            gatherPeak = Mathf.Max(gatherPeak, t);   // +track is BEHIND the hip
            drivePeak = Mathf.Min(drivePeak, t);     // -track is IN FRONT of it
        }

        Assert.True(gatherPeak > atToeOff + (reach * 0.05f),
            $"the foot never gathered: it left stance at {atToeOff:F4} m and its furthest-back " +
            $"point in the whole swing was {gatherPeak:F4} m — that is a swing with no anticipation");
        Assert.True(drivePeak < -reach - (reach * 0.05f),
            $"the foot never overshot its plant: furthest forward {drivePeak:F4} m against a plant " +
            $"point at {-reach:F4} m — the drive stopped exactly where it was told to, which is what " +
            "averaging looks like");
        // …and it still arrives EXACTLY on the plant point, or the stance it hands off to starts
        // somewhere the plant arithmetic did not put it.
        Assert.True(Mathf.Abs(LocomotionProfile.FootTrackAt(0.99999f, duty, reach) + reach) < 1e-3f);
    }

    /// <summary>
    /// <b>The swing is fast transitions between held extremes, not a constant glide.</b> Measures
    /// the foot's speed through the swing and requires the fastest part to be well clear of the
    /// average — a curve with no variation in it is the mush the amendment names.
    /// </summary>
    [Fact]
    public void SwingFoot_MovesInFastTransitions_NotAConstantGlide()
    {
        const float leg = 0.36f;
        float v = LocomotionProfile.SprintSpeedMps;
        float duty = LocomotionProfile.DutyFactorAt(v, leg);
        float reach = LocomotionProfile.StanceReachAt(v, leg);

        float fastest = 0f, total = 0f;
        int steps = 400;
        float prev = LocomotionProfile.FootTrackAt(duty, duty, reach);
        for (int i = 1; i <= steps; i++)
        {
            float p = duty + ((1f - duty) * i / steps);
            float now = LocomotionProfile.FootTrackAt(p, duty, reach);
            float d = Mathf.Abs(now - prev);
            fastest = Mathf.Max(fastest, d);
            total += d;
            prev = now;
        }
        float mean = total / steps;
        Assert.True(fastest > mean * 1.8f,
            $"the swing's fastest sample moves {fastest / mean:F2}x its own mean — a fast transition " +
            "between held poses should be well past that, and a constant glide sits at 1.0");
    }

    [Fact]
    public void FootTrack_IsContinuousAcrossBothHandOffs()
    {
        const float leg = 0.36f;
        float v = LocomotionProfile.SprintSpeedMps;
        float duty = LocomotionProfile.DutyFactorAt(v, leg);
        float reach = LocomotionProfile.StanceReachAt(v, leg);

        // Stance ends where swing begins, and swing ends where the next stance begins (wrapping).
        Assert.True(Mathf.Abs(
            LocomotionProfile.FootTrackAt(duty - 1e-5f, duty, reach)
            - LocomotionProfile.FootTrackAt(duty + 1e-5f, duty, reach)) < 1e-3f);
        Assert.True(Mathf.Abs(
            LocomotionProfile.FootTrackAt(0.99999f, duty, reach)
            - LocomotionProfile.FootTrackAt(0f, duty, reach)) < 1e-3f);
    }

    // =============================================================================================
    // Cadence: the "tiny little stuttering step" complaint, as a number.
    // =============================================================================================

    [Fact]
    public void Cadence_IsHumanAtEveryGear_AndNotTheComedyChurn()
    {
        // What shipped: StepHz 3.9 * (1 + RunStepFreqBoost 0.6) = 6.24 steps/s at sprint, and the
        // constant's own comment said "fast churn = comedy". A real sprint is 3.5-4.
        float walk = LocomotionProfile.CadenceAt(LocomotionProfile.WalkSpeedMps);
        float jog = LocomotionProfile.CadenceAt(LocomotionProfile.JogSpeedMps);
        float sprint = LocomotionProfile.CadenceAt(LocomotionProfile.SprintSpeedMps);

        Assert.InRange(walk, 2.2f, 2.8f);
        Assert.InRange(jog, 2.8f, 3.4f);
        Assert.InRange(sprint, 3.0f, 3.9f);
        Assert.True(sprint < 6.24f * 0.65f,
            $"sprint cadence {sprint:F2} steps/s is not meaningfully below the 6.24 that shipped");
    }

    [Fact]
    public void MostOfTheExtraSpeed_BecomesStride_NotCadence()
    {
        // The packet's rule in as many words: "derive it, and let stride length carry the rest".
        float slow = LocomotionProfile.WalkSpeedMps;
        float fast = LocomotionProfile.SprintSpeedMps;
        float speedRatio = fast / slow;
        float cadenceRatio = LocomotionProfile.CadenceAt(fast) / LocomotionProfile.CadenceAt(slow);
        float strideRatio = LocomotionProfile.StepLengthAt(fast) / LocomotionProfile.StepLengthAt(slow);

        // "Most" means more than half in log terms: the stride multiplier must exceed the square
        // root of the speed multiplier, or the cadence is doing the larger share of the work.
        Assert.True(strideRatio > Mathf.Sqrt(speedRatio),
            $"speed x{speedRatio:F2} arrives as cadence x{cadenceRatio:F2} and stride x{strideRatio:F2} " +
            "— the legs are churning rather than striding");
    }

    [Fact]
    public void Cadence_And_Reach_AreFiniteAndSaneOnGarbageInput()
    {
        foreach (float v in new[] { float.NaN, float.PositiveInfinity, -12f, 0f })
        {
            Assert.True(float.IsFinite(LocomotionProfile.CadenceAt(v)));
            Assert.True(float.IsFinite(LocomotionProfile.StanceReachAt(v, 0.36f)));
            Assert.True(float.IsFinite(LocomotionProfile.DutyFactorAt(v, 0.36f)));
            Assert.True(float.IsFinite(LocomotionProfile.FootTrackAt(v, 0.3f, 0.2f)));
        }
        Assert.True(float.IsFinite(LocomotionProfile.StanceReachAt(5f, float.NaN)));
        Assert.True(float.IsFinite(LocomotionProfile.LegAngleFor(99f, 0.36f)));
    }

    /// <summary>The reach is capped by the LEG, never by a taste value — so a rig with longer legs
    /// takes a longer stride with no constant retyped.</summary>
    [Fact]
    public void Reach_ScalesWithTheMeasuredLeg()
    {
        float v = LocomotionProfile.SprintSpeedMps;
        float shortLeg = LocomotionProfile.StanceReachAt(v, 0.30f);
        float longLeg = LocomotionProfile.StanceReachAt(v, 0.60f);
        Assert.True(longLeg > shortLeg * 1.9f,
            $"reach barely moved with the leg ({shortLeg:F3} -> {longLeg:F3} m)");

        // And a leg never swings past the angle LegShorteningFraction allows, which is what keeps
        // the foot from floating and the leg from detaching from the torso it overlaps.
        foreach (float leg in new[] { 0.20f, 0.36f, 0.60f })
        {
            float angle = LocomotionProfile.LegAngleFor(-LocomotionProfile.StanceReachAt(v, leg), leg);
            Assert.True(Mathf.Abs(angle) <= LocomotionProfile.MaxLegSwingRad + 1e-4f,
                $"leg {leg:F2} m swings {angle:F4} rad, past the {LocomotionProfile.MaxLegSwingRad:F4} cap");
        }
    }

    [Fact]
    public void DutyFalls_AsSpeedRises_WhichIsTheFlightPhase()
    {
        const float leg = 0.36f;
        float walkDuty = LocomotionProfile.DutyFactorAt(LocomotionProfile.WalkSpeedMps, leg);
        float sprintDuty = LocomotionProfile.DutyFactorAt(LocomotionProfile.SprintSpeedMps, leg);
        Assert.True(sprintDuty < walkDuty,
            $"duty {walkDuty:F3} at a walk and {sprintDuty:F3} at a sprint — a body cannot keep the " +
            "same stance fraction at 3.5x the speed on the same legs");
        Assert.True(walkDuty <= LocomotionProfile.NominalDutyFactor + 1e-4f);
    }

    // =============================================================================================
    // The gears
    // =============================================================================================

    [Fact]
    public void Gears_HaveHysteresis_SoTheyCannotFlicker()
    {
        // Sitting exactly on a boundary must resolve differently depending on which side you came
        // from — that IS the hysteresis, and without it the camera FOV, the orbit and the gait
        // amplitude would all chatter together at a constant speed.
        float boundary = (LocomotionProfile.SprintEnterMps + LocomotionProfile.SprintExitMps) * 0.5f;
        Assert.Equal(Gear.Jog, LocomotionProfile.GearFor(Gear.Jog, boundary));
        Assert.Equal(Gear.Sprint, LocomotionProfile.GearFor(Gear.Sprint, boundary));

        float walkJog = (LocomotionProfile.JogEnterMps + LocomotionProfile.JogExitMps) * 0.5f;
        Assert.Equal(Gear.Walk, LocomotionProfile.GearFor(Gear.Walk, walkJog));
        Assert.Equal(Gear.Jog, LocomotionProfile.GearFor(Gear.Jog, walkJog));
    }

    [Fact]
    public void EveryGear_IsReachable_FromTheSpeedsTheMotorCanProduce()
    {
        Assert.Equal(Gear.Idle, LocomotionProfile.GearFor(Gear.Idle, 0f));
        Assert.Equal(Gear.Walk, LocomotionProfile.GearFor(Gear.Idle, LocomotionProfile.WalkSpeedMps));
        Assert.Equal(Gear.Jog, LocomotionProfile.GearFor(Gear.Walk, LocomotionProfile.JogSpeedMps));
        Assert.Equal(Gear.Sprint, LocomotionProfile.GearFor(Gear.Jog, LocomotionProfile.SprintSpeedMps));
        // …and every one of them comes back down again. A state with no exit is the defect this
        // repo's definition of done exists to catch.
        Assert.Equal(Gear.Jog, LocomotionProfile.GearFor(Gear.Sprint, LocomotionProfile.JogSpeedMps));
        // Below the DOWN threshold, not below the walk gear's top speed — coming down out of a jog
        // you stay in a jog until JogExitMps, which is the hysteresis doing its job.
        Assert.Equal(Gear.Walk, LocomotionProfile.GearFor(Gear.Jog, LocomotionProfile.JogExitMps * 0.9f));
        Assert.Equal(Gear.Idle, LocomotionProfile.GearFor(Gear.Walk, 0f));
        // A sprint that stops dead in one sample must not get stuck in Sprint on the way down.
        Assert.Equal(Gear.Walk, LocomotionProfile.GearFor(Gear.Sprint, 0.6f));
    }

    [Fact]
    public void WalkGear_IsGenuinelySlowerThanAJog_AndTheModifierBeatsSprint()
    {
        Assert.True(LocomotionProfile.WalkSpeedMps < LocomotionProfile.JogSpeedMps * 0.6f);
        // The walk fraction is what LocalInputIntentSource caps the analog request at, so the gear
        // the animation names and the speed the motor produces come from ONE number.
        Near(AvatarMotor.MoveSpeed * LocomotionProfile.WalkFraction,
            LocomotionProfile.WalkSpeedMps);
    }

    // =============================================================================================
    // The ramps, and the lean that reports them
    // =============================================================================================

    [Fact]
    public void SprintRamp_IsLongEnoughToSee_AndBrakingIsFaster()
    {
        float toSprint = LocomotionProfile.SprintSpeedMps / AvatarMotor.Acceleration;
        float toStop = LocomotionProfile.SprintSpeedMps / AvatarMotor.Deceleration;

        // The packet asked for "roughly 0.5-1.0 s to reach sprint top speed"; Talon's amendment
        // asked for the bold end of any range given. 0.96 s.
        Assert.InRange(toSprint, 0.5f, 1.1f);
        Assert.True(toStop < toSprint * 0.75f,
            $"stop {toStop:F3} s against launch {toSprint:F3} s — braking must read as the harder " +
            "of the two gestures");
        Assert.True(toStop > 0.25f, $"stop {toStop:F3} s is close enough to instant to read as one");
    }

    [Fact]
    public void TurningStaysAsSharpAsItShipped_WhileTheStraightLineRampSlowed()
    {
        var fast = new Vector3(0f, 0f, -8.64f);          // travelling forward at a dead sprint
        var wishAhead = new Vector3(0f, 0f, -8.64f);
        var wishSideways = new Vector3(5.4f, 0f, 0f);
        var wishBack = new Vector3(0f, 0f, 8.64f);

        Near(AvatarMotor.Acceleration, AvatarMotor.RateFor(fast, wishAhead));
        Near(AvatarMotor.TurnAcceleration, AvatarMotor.RateFor(fast, wishSideways));
        Near(AvatarMotor.TurnAcceleration, AvatarMotor.RateFor(fast, wishBack));
        // No input at all is the brake, unchanged in kind from what shipped.
        Near(AvatarMotor.Deceleration, AvatarMotor.RateFor(fast, Vector3.Zero));
        // A standing start is the ramp, because there is no heading to be aligned with yet.
        Near(AvatarMotor.Acceleration, AvatarMotor.RateFor(Vector3.Zero, wishAhead));
        Assert.True(float.IsFinite(AvatarMotor.RateFor(
            new Vector3(float.NaN, 0f, 0f), wishAhead)));
    }

    [Fact]
    public void Lean_EncodesAcceleration_AndIsZeroAtEverySteadySpeed()
    {
        // The complaint this answers: a lean proportional to speed reads identically at 3 m/s and
        // at 8 m/s, so it tells the player nothing. At steady state there is no acceleration and
        // therefore no pose at all.
        Near(0f, LocomotionProfile.LeanForAccel(0f));

        float launch = LocomotionProfile.LeanForAccel(AvatarMotor.Acceleration);
        float brake = LocomotionProfile.LeanForAccel(-AvatarMotor.Deceleration);
        Assert.True(launch < -0.10f, $"launch lean {launch:F4} rad is too small to read");
        Assert.True(brake > 0.10f, $"braking lean {brake:F4} rad is too small to read");
        Assert.True(Mathf.Abs(brake) >= Mathf.Abs(launch),
            "braking should be at least as loud a gesture as launching");

        // …and hard-capped far below the ~30 degrees GREY-1 measured on the shipped body.
        foreach (float a in new[] { -1000f, -50f, 50f, 1000f })
            Assert.True(Mathf.Abs(LocomotionProfile.LeanForAccel(a))
                <= LocomotionProfile.MaxAccelLeanRad + 1e-5f);
        Assert.True(LocomotionProfile.MaxAccelLeanRad < 0.520f / 2f,
            "the cap is not meaningfully below the 0.520 rad the shipped body held at every sprint");

        foreach (float a in new[] { -1000f, 1000f })
            Assert.True(Mathf.Abs(LocomotionProfile.RollForAccel(a))
                <= LocomotionProfile.MaxTurnRollRad + 1e-5f);
    }

    // =============================================================================================
    // Facing: the action camera's half that lives in the motor
    // =============================================================================================

    [Fact]
    public void Facing_TracksTravel_UnlessAnAimIsHandedIn()
    {
        const float dt = 1f / 60f;
        var wishNorth = new Vector3(0f, 0f, -5.4f); // -Z is forward, so yaw 0

        // No aim: travel facing, exactly as it always has.
        float travel = AvatarMotor.ResolveYaw(0f, wishNorth, null, dt);
        Assert.True(Mathf.Abs(travel) < 1e-4f);

        // Standing still with no aim holds the last facing rather than snapping to zero.
        Near(1.23f, AvatarMotor.ResolveYaw(1.23f, Vector3.Zero, null, dt));

        // An aim wins even while running the other way — this is the whole point of the change.
        float aimed = AvatarMotor.ResolveYaw(0f, wishNorth, Mathf.Pi * 0.5f, dt);
        Assert.True(aimed > 0.05f, $"the body barely turned toward its aim ({aimed:F4} rad)");
        // …and it BLENDS. One tick must not close the whole angle, or the character snaps.
        Assert.True(aimed < Mathf.Pi * 0.5f * 0.75f,
            $"the body snapped {aimed:F4} rad in a single tick instead of turning");

        // Repeated ticks converge on the aim.
        float y = 0f;
        for (int i = 0; i < 30; i++)
            y = AvatarMotor.ResolveYaw(y, wishNorth, Mathf.Pi * 0.5f, dt);
        Assert.True(Mathf.Abs(y - (Mathf.Pi * 0.5f)) < 0.05f,
            $"after half a second of aiming the body is still {y:F4} rad off its target");

        // A garbage aim falls back to the travel rule rather than poisoning the transform.
        Assert.True(float.IsFinite(AvatarMotor.ResolveYaw(0f, wishNorth, float.NaN, dt)));
    }

    /// <summary>
    /// <b>Knob 58, both settings, on the pure seam</b> (FP-1, 2026-09-19). The knob's whole job is
    /// to decide WHICH angle reaches <see cref="AvatarMotor.ResolveYaw"/>, so what is pinned here
    /// is that each setting picks the documented one and that the "off" setting reproduces the
    /// foundation's travel-facing rule exactly. <c>Step</c> itself needs a
    /// <c>CharacterBody3D</c> and a physics world, which is the slowest tier and not where a
    /// facing POLICY should be provable.
    ///
    /// <para>The arithmetic below is <c>Step</c>'s own selection expression, transcribed: an
    /// explicit <c>faceYaw</c> from a caller wins, then the knob's aim, then null. A reader who
    /// suspects the transcription has drifted should read <c>AvatarMotor.Step</c>'s yaw block —
    /// it is six lines and it says the same thing.</para>
    /// </summary>
    [Fact]
    public void BodyYawFollowsAim_TurnsTheBodyTowardTheLook_AndOffRestoresTravelFacing()
    {
        const float dt = 1f / 60f;
        var wishEast = new Vector3(5.4f, 0f, 0f);     // travel facing for +X is -pi/2
        float travelFacing = Mathf.Atan2(-wishEast.X, -wishEast.Z);
        const float look = 2.0f;                       // the player is looking somewhere else

        // ON (this game's shipped default): the look is what the body turns toward.
        float on = AvatarMotor.ResolveYaw(0f, wishEast, AvatarMotor.SanitizeAimYaw(look), dt);
        Assert.True(on > 0.05f, $"the body did not turn toward the look ({on:F4} rad)");
        Assert.True(on < look * 0.75f, $"the body snapped {on:F4} rad in one tick instead of turning");

        // OFF: byte-for-byte the travel rule the foundation shipped.
        float off = AvatarMotor.ResolveYaw(0f, wishEast, null, dt);
        Assert.Equal(AvatarMotor.ResolveYaw(0f, wishEast, null, dt), off);
        Assert.True(Mathf.Abs(off - Mathf.LerpAngle(0f, travelFacing, Mathf.Min(1f, AvatarMotor.TurnLerp * dt)))
            < 1e-5f);
        // …and the two settings genuinely disagree, or this test would pass on a knob wired to
        // nothing (the CELEBRATE-1 lesson: a guard whose call site is guarded proves nothing).
        Assert.True(Mathf.Abs(Mathf.Wrap(on - off, -Mathf.Pi, Mathf.Pi)) > 0.1f,
            $"both settings produced the same facing ({on:F4} vs {off:F4} rad)");

        // The shipped default IS on, and it is on because this game is first person.
        Assert.True(AvatarMotor.BodyYawFollowsAim);
        Assert.Equal(1f, MotorTuning.Default.BodyYawFollowsAim);

    }

    /// <summary>
    /// <b>The other half of knob 58: a scripted brain reports the look it would have had.</b>
    /// Without this decorator every bot and dummy in the repo reports <c>AimYaw = 0</c>, and with
    /// the knob on they would all pivot to world zero and walk sideways through every capture a
    /// later packet takes. Pure — <c>TravelFacingIntentSource</c> touches no node and no physics,
    /// so the rule is provable in the fastest tier.
    /// </summary>
    [Fact]
    public void TravelFacingDecorator_FillsABotsLookFromItsOwnHeading_AndHoldsItWhenItStops()
    {
        var brain = new FakeIntentSource();
        var wrapped = new TravelFacingIntentSource(brain);

        brain.Next = new MoveIntent { MoveDir = new Vector3(1f, 0f, 0f) };   // due +X
        float east = wrapped.NextIntent(0.016).AimYaw;
        Assert.Equal(Mathf.Atan2(-1f, 0f), east, 1e-5f);
        // The SAME angle AvatarMotor's travel-facing branch would have produced, so a bot's
        // facing under the knob is identical to its facing before the knob existed.
        Assert.Equal(AvatarMotor.ResolveYaw(east, new Vector3(1f, 0f, 0f), null, 1f), east, 1e-4f);

        brain.Next = new MoveIntent { MoveDir = new Vector3(0f, 0f, -1f) };  // due -Z, yaw 0
        Assert.Equal(0f, wrapped.NextIntent(0.016).AimYaw, 1e-5f);

        // Standing still HOLDS the last heading rather than snapping the body to zero.
        brain.Next = MoveIntent.None;
        Assert.Equal(0f, wrapped.NextIntent(0.016).AimYaw, 1e-5f);
        brain.Next = new MoveIntent { MoveDir = new Vector3(1f, 0f, 0f) };
        Assert.Equal(east, wrapped.NextIntent(0.016).AimYaw, 1e-5f);
        brain.Next = MoveIntent.None;
        Assert.Equal(east, wrapped.NextIntent(0.016).AimYaw, 1e-5f);

        // Every other field is passed through untouched — the decorator fills one hole, it does
        // not re-author the brain's tick.
        brain.Next = new MoveIntent { MoveDir = new Vector3(1f, 0f, 0f), Interact = true, Sprint = true };
        MoveIntent got = wrapped.NextIntent(0.016);
        Assert.True(got.Interact);
        Assert.True(got.Sprint);

        // It is NOT a human source, whatever it wraps — the achievement gate reads this.
        Assert.False(wrapped.IsHumanInput);
    }

    private sealed class FakeIntentSource : IIntentSource
    {
        public MoveIntent Next { get; set; }
        public MoveIntent NextIntent(double delta) => Next;
    }

    // =============================================================================================
    // The keyboard's virtual stick
    // =============================================================================================

    [Fact]
    public void KeyboardAnalog_RampsUp_AndFallsFaster()
    {
        const float dt = 1f / 60f;
        float a = 0f;
        int ticksUp = 0;
        while (a < 0.999f && ticksUp < 600)
        {
            a = LocomotionProfile.StepKeyAnalog(a, 1f, dt);
            ticksUp++;
        }
        float riseSec = ticksUp * dt;
        Assert.InRange(riseSec, LocomotionProfile.KeyAnalogRiseSec - dt,
            LocomotionProfile.KeyAnalogRiseSec + dt);

        int ticksDown = 0;
        while (a > 0.001f && ticksDown < 600)
        {
            a = LocomotionProfile.StepKeyAnalog(a, 0f, dt);
            ticksDown++;
        }
        Assert.True(ticksDown < ticksUp, "the virtual stick must release faster than it builds");

        // A single-frame tap is a nudge, not a full-speed request. That is the half of "felt
        // acceleration" no motor constant can supply once the input itself is a step function.
        float tap = LocomotionProfile.StepKeyAnalog(0f, 1f, dt);
        Assert.True(tap < 0.12f, $"one frame of a held key already asks for {tap:F3} of full stick");
    }

    // =============================================================================================
    // THE TURNAROUND SKID (SKID-1)
    //
    // Every number in the SKID-1 report is asserted here, and the two load-bearing ones are
    // Skid_IsPureAndDeterministic (the netcode property the whole design rests on) and
    // Skid_CanNeverTouchABallisticBody (the eel invariant that SkidDeceleration would otherwise
    // breach). A red in this block is a real regression, not a flake.
    // =============================================================================================

    private const float Dt = 1f / 60f;

    /// <summary>A flat velocity travelling due north (the rig's -Z) at <paramref name="speed"/>.</summary>
    private static Vector3 North(float speed) => new(0f, 0f, -speed);

    /// <summary>Runs <c>StepSkid</c> forward from a live skid, decaying the velocity exactly as
    /// <c>Step</c> does, and returns (ticks, metres travelled) until the state exits.</summary>
    private static (int Ticks, float Metres) RunSkid(float entrySpeed)
    {
        Vector3 v = North(entrySpeed);
        Vector3 wish = North(-AvatarMotor.MoveSpeed); // full stick, dead opposite
        float remaining = AvatarMotor.StepSkid(0f, true, false, v, wish, Dt);
        Assert.True(remaining > 0f, $"a reversal at {entrySpeed:F2} m/s did not enter the skid at all");

        int ticks = 0;
        float metres = 0f;
        while (remaining > 0f && ticks < 600)
        {
            v.Z = Mathf.MoveToward(v.Z, 0f, AvatarMotor.SkidDeceleration * Dt);
            metres += Mathf.Abs(v.Z) * Dt;
            ticks++;
            remaining = AvatarMotor.StepSkid(remaining, true, false, v, wish, Dt);
        }
        Assert.True(ticks < 600, "the skid never exited — the hard cap is not doing its job");
        return (ticks, metres);
    }

    /// <summary>
    /// <b>The feel, as arithmetic</b> — the slide's LENGTH is a measured quantity here rather than
    /// an opinion in a report, asserted as a floor because "too much" is a thing Talon can tell us
    /// and "not enough" is not.
    ///
    /// <para><b>MOVE-8 FORK — this is the one Talon should look at hardest.</b> SKID-1's floors
    /// (0.45 s, 2.0 m) were set against a sprint of 8.64 m/s, and he ratified the result in a
    /// standalone build: <i>"it's literally perfect"</i>. His 2026-08-28 speed ruling takes the
    /// sprint to 6.08 m/s and <c>SkidDeceleration</c> did not move with it, so a sprint reversal
    /// is now <b>0.383 s / 1.37 m</b> instead of 0.575 s / 3.02 m — a third shorter in time and
    /// <b>55% shorter on the ground</b>. Nothing is broken; the skid still enters, still scales
    /// with speed, still exits. What is gone is the SIZE of a thing he explicitly approved for its
    /// size.</para>
    ///
    /// <para><b>Why the floors are re-pinned rather than the deceleration retuned.</b> Restoring
    /// 3 m would mean <c>SkidDeceleration</c> around 6.4 instead of 13, which is a second, unruled
    /// opinion sitting beside Talon's — and MOVE-8's job is to land his ruling and report what it
    /// costs. The floors below are the MEASURED values, so this test still catches a skid that
    /// stops entering or stops scaling; the report carries the fork.</para>
    /// </summary>
    [Fact]
    public void Skid_AtASprint_SlidesFarEnoughToWatch()
    {
        (int ticks, float metres) = RunSkid(LocomotionProfile.SprintSpeedMps);
        float seconds = ticks * Dt;

        Assert.True(seconds > 0.30f,
            $"a sprint reversal is over in {seconds:F3} s — that is a turn, not a skid");
        Assert.True(metres > 1.2f,
            $"a sprint reversal travels {metres:F3} m past the turn — MOVE-8 measured 1.37 m and "
            + "anything under about a body length is not read as sliding at all");
        // …and it is a beat, not a loss of the controller.
        Assert.True(seconds < 0.8f, $"a sprint skid lasting {seconds:F3} s is a punishment");
    }

    /// <summary>The state is one mechanism whose loudness is set by how committed the player was:
    /// a jog reversal scuffs, a sprint reversal slides. If those two ever measure the same, the
    /// speed dial has been lost and the skid is just a fixed animation.</summary>
    [Fact]
    public void Skid_ScalesWithHowFastYouWereGoing()
    {
        (_, float jog) = RunSkid(LocomotionProfile.JogSpeedMps);
        (_, float sprint) = RunSkid(LocomotionProfile.SprintSpeedMps);
        Assert.True(sprint > jog * 2f,
            $"jog {jog:F3} m against sprint {sprint:F3} m — the skid barely notices the difference");
        // MOVE-8: 0.60 -> 0.40 m. Same fork as Skid_AtASprint_SlidesFarEnoughToWatch — the ruled
        // jog is 3.8 m/s against 5.4, SkidDeceleration is unmoved, and the scuff shortened with it
        // to a measured 0.478 m. The SCALING claim above is what this test is really for and it is
        // untouched: the sprint still slides nearly 3x the jog.
        Assert.True(jog > 0.40f, $"a full-stick jog reversal slides {jog:F3} m, which is nothing");
    }

    /// <summary>The entry gate: a walk stays exact, a jog and a sprint skid, and a 90-degree corner
    /// never does at any speed.</summary>
    [Fact]
    public void Skid_EntersOnlyOnACommittedReversal()
    {
        Vector3 reverse = North(-AvatarMotor.MoveSpeed);
        var sideways = new Vector3(AvatarMotor.MoveSpeed, 0f, 0f);

        Assert.False(AvatarMotor.ShouldEnterSkid(true, false,
            North(LocomotionProfile.WalkSpeedMps), reverse),
            "the walk gear skidded — creeping around a corner must stay exact");
        Assert.True(AvatarMotor.ShouldEnterSkid(true, false,
            North(LocomotionProfile.JogSpeedMps), reverse));
        Assert.True(AvatarMotor.ShouldEnterSkid(true, false,
            North(LocomotionProfile.SprintSpeedMps), reverse));

        // A 90-degree turn is alignment 0, which is far above the -0.5 gate: steering stays crisp.
        Assert.False(AvatarMotor.ShouldEnterSkid(true, false,
            North(LocomotionProfile.SprintSpeedMps), sideways));
        // The WASD diagonal reversal (135 degrees, alignment -0.707) is the one that MUST skid, or
        // the feature is unreachable from a keyboard without pressing exactly one key.
        Assert.True(AvatarMotor.ShouldEnterSkid(true, false,
            North(LocomotionProfile.SprintSpeedMps),
            new Vector3(AvatarMotor.MoveSpeed, 0f, AvatarMotor.MoveSpeed).Normalized() * AvatarMotor.MoveSpeed));

        // No input is a brake, never a skid.
        Assert.False(AvatarMotor.ShouldEnterSkid(true, false,
            North(LocomotionProfile.SprintSpeedMps), Vector3.Zero));
        // Airborne has nothing to skid on.
        Assert.False(AvatarMotor.ShouldEnterSkid(false, false,
            North(LocomotionProfile.SprintSpeedMps), reverse));
        // Garbage in never yields a skid.
        Assert.False(AvatarMotor.ShouldEnterSkid(true, false,
            new Vector3(float.NaN, 0f, float.NaN), reverse));
    }

    /// <summary>Every exit the state machine declares, each proved separately (MECHANICS-BIBLE §2:
    /// a state with no exit — or with one that depends on the player doing something — is the
    /// defect this repo's definition of done exists to catch).</summary>
    [Fact]
    public void Skid_HasFourExits_AndNoneOfThemCanBeRefused()
    {
        Vector3 fast = North(LocomotionProfile.SprintSpeedMps);
        Vector3 reverse = North(-AvatarMotor.MoveSpeed);
        float live = AvatarMotor.StepSkid(0f, true, false, fast, reverse, Dt);

        // 1. the speed floor
        Assert.Equal(0f, AvatarMotor.StepSkid(live, true, false,
            North(AvatarMotor.SkidExitSpeedMps * 0.9f), reverse, Dt));
        // 2. the stick released
        Assert.Equal(0f, AvatarMotor.StepSkid(live, true, false, fast, Vector3.Zero, Dt));
        // 3. the hard cap — even with the stick held and the speed pinned, one dt past the cap ends it
        Assert.Equal(0f, AvatarMotor.StepSkid(0.5f * Dt, true, false, fast, reverse, Dt));
        // 4. control taken / airborne — covered by the locked-body checks above.

        // The cap is never what a real skid hits: the slowest natural exit is well inside it.
        (int ticks, _) = RunSkid(LocomotionProfile.SprintSpeedMps);
        Assert.True(ticks * Dt < AvatarMotor.SkidMaxSec,
            $"the fastest legal entry takes {ticks * Dt:F3} s, which the {AvatarMotor.SkidMaxSec:F2} s " +
            "cap would clip — the cap is a safety net, not a tuning knob");
    }

    /// <summary>The gap between the entry floor and the exit floor is the anti-chatter mechanism —
    /// the same job hysteresis does for the gears. A body leaving a skid is far too slow to
    /// immediately enter another, so no input pattern can strobe the state.</summary>
    [Fact]
    public void Skid_CannotChatter()
    {
        Assert.True(AvatarMotor.SkidExitSpeedMps < AvatarMotor.SkidEnterSpeedMps * 0.5f,
            $"exit {AvatarMotor.SkidExitSpeedMps:F2} against entry {AvatarMotor.SkidEnterSpeedMps:F2} " +
            "m/s — too close, and the state will strobe");
        Assert.False(AvatarMotor.ShouldEnterSkid(true, false,
            North(AvatarMotor.SkidExitSpeedMps), North(-AvatarMotor.MoveSpeed)));
    }

    /// <summary>
    /// <b>The netcode property the whole design rests on.</b> <c>Step</c> needs an engine, but the
    /// skid's entire decision lives in this pure pair — so running them twice from byte-identical
    /// inputs and demanding byte-identical output is the cheapest possible proof that no clock, no
    /// randomness and no scene read crept in. If this ever fails, owner prediction and the server
    /// have stopped agreeing about where a sliding body ends up.
    /// </summary>
    [Fact]
    public void Skid_IsPureAndDeterministic()
    {
        var rng = new System.Random(20260816);
        for (int i = 0; i < 4000; i++)
        {
            float prev = (float)rng.NextDouble() * AvatarMotor.SkidMaxSec * 1.2f;
            bool grounded = rng.Next(2) == 0;
            bool locked = rng.Next(2) == 0;
            var v = new Vector3((float)rng.NextDouble() * 18f - 9f, (float)rng.NextDouble() * 6f - 3f,
                (float)rng.NextDouble() * 18f - 9f);
            var wish = new Vector3((float)rng.NextDouble() * 18f - 9f, 0f,
                (float)rng.NextDouble() * 18f - 9f);

            float a = AvatarMotor.StepSkid(prev, grounded, locked, v, wish, Dt);
            float b = AvatarMotor.StepSkid(prev, grounded, locked, v, wish, Dt);
            Assert.Equal(a.GetHashCode(), b.GetHashCode()); // bitwise, not "near"
            Assert.Equal(a, b);
            Assert.True(float.IsFinite(a) && a >= 0f && a <= AvatarMotor.SkidMaxSec,
                $"StepSkid returned {a} — the timer left its own range");

            bool p = AvatarMotor.ShouldEnterSkid(grounded, locked, v, wish);
            Assert.Equal(p, AvatarMotor.ShouldEnterSkid(grounded, locked, v, wish));
        }

        // Garbage never poisons the timer. A non-finite timer or dt refuses outright; a negative
        // one is simply "not skidding" and is allowed to enter fresh, which is why the wish here is
        // aligned rather than opposed — the point is that nothing garbage-shaped survives, not that
        // a legal entry is blocked.
        Vector3 forward = North(8f);
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f })
            Assert.Equal(0f, AvatarMotor.StepSkid(bad, true, false, forward, forward, Dt));
        Assert.Equal(0f, AvatarMotor.StepSkid(0.3f, true, false,
            forward, North(-5f), float.NaN));
        // A timer larger than the cap (a doctored snapshot, a future edit) is clamped in, never
        // passed through: the function is total over its own declared range.
        Assert.True(AvatarMotor.StepSkid(9999f, true, false, forward, North(-5f), Dt)
            <= AvatarMotor.SkidMaxSec);
    }

    /// <summary><c>RateFor</c> is the thing the packet forbids touching, and this is the assertion
    /// that says so: every one of MOVE-1's three branches still returns exactly what it returned
    /// before the skid existed. The skid pre-empts this function; it does not change it.</summary>
    [Fact]
    public void Skid_DidNotRetuneOrdinarySteering()
    {
        var fast = new Vector3(0f, 0f, -8.64f);
        Near(AvatarMotor.TurnAcceleration, AvatarMotor.RateFor(fast, new Vector3(0f, 0f, 8.64f)));
        Near(AvatarMotor.TurnAcceleration, AvatarMotor.RateFor(fast, new Vector3(5.4f, 0f, 0f)));
        Near(AvatarMotor.Acceleration, AvatarMotor.RateFor(fast, fast));
        Near(AvatarMotor.Deceleration, AvatarMotor.RateFor(fast, Vector3.Zero));
        Near(34f, AvatarMotor.TurnAcceleration);
    }

    /// <summary>The body faces where it is TRAVELLING during the slide, which is the difference
    /// between sliding past a turn and turning slowly — and an aimed verb still wins over both.</summary>
    [Fact]
    public void Skid_FacingHoldsTheOldLine_AndAnAimStillWins()
    {
        Vector3 travel = North(6f);          // heading yaw 0
        float held = AvatarMotor.ResolveYaw(0f, travel, null, Dt);
        Assert.True(Mathf.Abs(held) < 1e-4f, $"the body turned {held:F4} rad while sliding straight");

        // Halfway through the slide the velocity is smaller but points the same way, so the facing
        // is unchanged — the slide holds its line rather than drifting toward the input.
        Assert.True(Mathf.Abs(AvatarMotor.ResolveYaw(0f, North(2f), null, Dt)) < 1e-4f);

        // An aim overrides it, exactly as it does outside a skid.
        Assert.True(AvatarMotor.ResolveYaw(0f, travel, Mathf.Pi * 0.5f, Dt) > 0.05f);
    }

    // =============================================================================================
    // The camera's speed cue
    // =============================================================================================

    [Fact]
    public void SpeedCue_IsZeroAtAWalk_OneAtASprint_AndClamped()
    {
        Near(0f, LocomotionProfile.SpeedCue01(0f));
        Near(0f, LocomotionProfile.SpeedCue01(LocomotionProfile.WalkSpeedMps));
        Near(1f, LocomotionProfile.SpeedCue01(LocomotionProfile.SprintSpeedMps));
        Near(1f, LocomotionProfile.SpeedCue01(99f));
        Assert.InRange(LocomotionProfile.SpeedCue01(LocomotionProfile.JogSpeedMps), 0.3f, 0.7f);
        Assert.True(float.IsFinite(LocomotionProfile.SpeedCue01(float.NaN)));
    }

    /// <summary>The camera's orbit bound must still refuse to put the lens under the floor, at the
    /// new speed-driven lengths as well as at the old fixed one. <c>CatchInstrumentTests</c> sweeps
    /// the default; this sweeps the range MOVE-1 added.</summary>
    [Fact]
    public void OrbitBound_KeepsTheLensAboveTheFloor_AtEverySpeedDrivenLength()
    {
        const float focusHeight = 0.903f; // the greybox's measured camera focus
        foreach (float requested in new[] { 3.15f, 3.6f, 4.5f, 99f })
        {
            for (float pitch = 0f; pitch <= 1.05f; pitch += 0.02f)
            {
                float arm = SandboxCamera.ArmLimitForPitch(pitch, focusHeight, requested);
                Assert.True(arm <= Mathf.Min(requested, 4.6f) + 1e-4f,
                    $"the bound handed back {arm:F3} m when only {requested:F3} m was asked for");
                float lensY = focusHeight - (arm * Mathf.Sin(pitch));
                Assert.True(lensY > 0f || Mathf.IsEqualApprox(arm, 0.35f),
                    $"pitch {pitch:F3} at {requested:F2} m puts the lens at y={lensY:F4}");
            }
        }
    }
}
