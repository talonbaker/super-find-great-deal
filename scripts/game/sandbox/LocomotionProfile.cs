using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game.Sandbox;

/// <summary>Which gear a body is in. A <b>named, readable state</b> (Talon, 2026-08-16: "all of the
/// states… and the speed up and the slow down"), derived from ground speed rather than stored — see
/// <see cref="LocomotionProfile.GearFor"/> for why that is not a shortcut.
///
/// <para>Ordered slowest-to-fastest so a comparison (<c>gear &gt;= Gear.Jog</c>) means what it looks
/// like it means.</para></summary>
public enum Gear
{
    /// <summary><b>Entry:</b> ground speed falls below <see cref="LocomotionProfile.IdleExitMps"/>.
    /// <b>Behaviour:</b> no gait phase advance, feet ease to rest, idle breath and fidget run.
    /// <b>Exit:</b> speed rises past <see cref="LocomotionProfile.IdleEnterMps"/> → Walk.</summary>
    Idle = 0,

    /// <summary><b>Entry:</b> moving, below the walk/jog band. <b>Behaviour:</b> the full derived
    /// gait at low cadence and short stride. <b>Exit:</b> down to Idle, or up past
    /// <see cref="LocomotionProfile.JogEnterMps"/> → Jog.</summary>
    Walk = 1,

    /// <summary><b>The default</b> — what you are when you just hold a direction.
    /// <b>Entry:</b> speed past <see cref="LocomotionProfile.JogEnterMps"/>. <b>Behaviour:</b> gait
    /// at full amplitude. <b>Exit:</b> down past <see cref="LocomotionProfile.JogExitMps"/> → Walk,
    /// up past <see cref="LocomotionProfile.SprintEnterMps"/> → Sprint.</summary>
    Jog = 2,

    /// <summary><b>Entry:</b> speed past <see cref="LocomotionProfile.SprintEnterMps"/>.
    /// <b>Behaviour:</b> longest stride, highest cadence, widest camera. <b>Exit:</b> down past
    /// <see cref="LocomotionProfile.SprintExitMps"/> → Jog.</summary>
    Sprint = 3,
}

/// <summary>
/// <b>The locomotion model: three gears, a stride derived from the ground, and a lean that encodes
/// acceleration.</b> Pure arithmetic — no <c>Node</c>, no <c>Input</c>, no scene tree — so every
/// claim in the MOVE-1 report is an assertion in <c>tests/unit/LocomotionTests.cs</c> rather than a
/// human squinting at a capture.
///
/// <para><b>The one rule everything else serves:</b>
/// <c>stride length × cadence = ground speed</c>. The shipped waddle drove a fixed 3.9 Hz churn with
/// a tuned amplitude and had <i>no arithmetic relationship to the sliding at all</i> — which is
/// exactly what "vibrating while skating" is, and why no amount of amplitude tuning ever helped.
/// Here the cadence is a function of speed, the reach falls out of the identity, and the stance foot
/// is world-stationary <b>by construction</b> rather than by tuning:
/// <see cref="StanceReachAt(float, float)"/> is defined so that the foot's local rearward rate is
/// exactly the ground speed.</para>
///
/// <para><b>Everything that can scale with the body, does.</b> The reach, the bob and the swing
/// angle are all derived from a measured leg length, so the day the real cast
/// (<c>pendling</c>/<c>banneret</c>) lands, nothing here is retyped. That is
/// <c>AvatarProportions</c>' standing rule applied to motion instead of to dimensions.</para>
/// </summary>
public static class LocomotionProfile
{
    // --- The gears --------------------------------------------------------------------------
    //
    // Talon, 2026-08-16: discrete gears AND felt acceleration, not one continuous axis and not
    // instant gear changes. Jog is what you are when you just hold a direction; walk is a modifier
    // and a partial stick deflection; sprint is a button.
    //
    // The two speeds are AvatarMotor's and are NOT re-typed here — a second copy of a speed is how
    // a mirror goes stale (CycleBands and NightPressureGuarantee already carry that scar).

    /// <summary>Walk gear top speed as a fraction of the jog speed. 0.45 — slow enough that
    /// walking is a genuinely different gait to look at (the derived cadence drops to its floor and
    /// the stride roughly halves), fast enough that a player who holds the walk modifier by accident
    /// is not stranded. Tuned against the derived cadence: at this fraction the walk lands at
    /// 2.5 steps/s, which is the middle of the packet's stated 2.2–2.8 walking band.</summary>
    public const float WalkFraction = 0.45f;

    /// <summary>Walk gear top speed, m/s. Derived.</summary>
    public static readonly float WalkSpeedMps = AvatarMotor.MoveSpeed * WalkFraction;

    /// <summary>Jog gear top speed, m/s — the default. Derived.</summary>
    public static readonly float JogSpeedMps = AvatarMotor.MoveSpeed;

    /// <summary>Sprint gear top speed, m/s. Derived.</summary>
    public static readonly float SprintSpeedMps = AvatarMotor.MoveSpeed * AvatarMotor.SprintMultiplier;

    // Hysteresis bands. A gear that flickers at its own boundary is the classic state-machine
    // defect (MECHANICS-BIBLE §2), and here it would flicker the camera FOV, the arm length and the
    // gait amplitude all at once. Enter and exit are DIFFERENT numbers, never one threshold.

    /// <summary>Speed at which a standing body starts walking, m/s.</summary>
    public const float IdleEnterMps = 0.45f;

    /// <summary>Speed at which a walking body goes still, m/s. Below <see cref="IdleEnterMps"/>.</summary>
    public const float IdleExitMps = 0.22f;

    /// <summary>Speed at which a walking body becomes a jogging one, m/s.</summary>
    public static readonly float JogEnterMps = WalkSpeedMps * 1.12f;

    /// <summary>Speed at which a jogging body drops back to a walk, m/s.</summary>
    public static readonly float JogExitMps = WalkSpeedMps * 0.92f;

    /// <summary>Speed at which a jogging body becomes a sprinting one, m/s.</summary>
    public static readonly float SprintEnterMps = JogSpeedMps * 1.22f;

    /// <summary>Speed at which a sprinting body drops back to a jog, m/s.</summary>
    public static readonly float SprintExitMps = JogSpeedMps * 1.08f;

    /// <summary>
    /// <b>Which gear this body is in.</b> Derived from ground speed and the gear it was already in
    /// (the hysteresis), never stored on the wire.
    ///
    /// <para><b>Derived rather than replicated, and that is the safe direction.</b> Ground speed is
    /// already identical on every peer — the server simulates it and the snapshot carries it — so a
    /// gear computed from it cannot disagree between peers, while a gear sent as its own field could
    /// arrive stale or contradict the velocity beside it. It also means a client cannot assert a
    /// gear, because there is no field for one to assert.</para>
    /// </summary>
    public static Gear GearFor(Gear previous, float groundSpeedMps)
    {
        float v = float.IsFinite(groundSpeedMps) ? Mathf.Abs(groundSpeedMps) : 0f;
        return previous switch
        {
            Gear.Idle => v > IdleEnterMps ? UpFrom(Gear.Walk, v) : Gear.Idle,
            Gear.Walk => v < IdleExitMps ? Gear.Idle : UpFrom(Gear.Walk, v),
            Gear.Jog => v < JogExitMps ? Gear.Walk : (v > SprintEnterMps ? Gear.Sprint : Gear.Jog),
            _ => v < SprintExitMps ? (v < JogExitMps ? Gear.Walk : Gear.Jog) : Gear.Sprint,
        };
    }

    private static Gear UpFrom(Gear floorGear, float v)
    {
        if (v > SprintEnterMps)
            return Gear.Sprint;
        if (v > JogEnterMps)
            return Gear.Jog;
        return floorGear;
    }

    // --- Cadence and stride -------------------------------------------------------------------

    /// <summary>Steps per second at <see cref="WalkSpeedMps"/>. Inside the 2.2–2.8 walking band the
    /// packet names.</summary>
    public const float CadenceAtWalk = 2.5f;

    /// <summary>Steps per second at <see cref="SprintSpeedMps"/>. Inside the 3.0–3.6 running band,
    /// a shade over its top because this body's sprint is a shade over a human's.
    /// <b>Against the 6.24 steps/s the shipped waddle produced at sprint, this is the whole
    /// "stuttering step" complaint answered.</b></summary>
    public const float CadenceAtSprint = 3.7f;

    /// <summary>Slope of the cadence line, steps/s per m/s. Derived from the two anchors above so
    /// moving either one moves this with it.</summary>
    public static readonly float CadenceSlope =
        (CadenceAtSprint - CadenceAtWalk) / (SprintSpeedMps - WalkSpeedMps);

    /// <summary>Intercept of the cadence line, steps/s. Derived.</summary>
    public static readonly float CadenceIntercept = CadenceAtWalk - (CadenceSlope * WalkSpeedMps);

    /// <summary>Slowest the legs ever turn over, steps/s. A creeping body takes tiny steps at this
    /// floor rather than slowing the cadence to a stop, which is what real slow walking does.</summary>
    public const float CadenceMinHz = 2.0f;

    /// <summary>Fastest the legs ever turn over, steps/s.</summary>
    public const float CadenceMaxHz = 3.9f;

    /// <summary><b>Steps per second at this ground speed.</b> Sub-linear in speed by construction —
    /// the line's slope is small — so <see cref="StepLengthAt"/> carries most of the extra speed,
    /// which is what a real gait does and what "a stride, not a stuttering step" means
    /// arithmetically.</summary>
    public static float CadenceAt(float groundSpeedMps)
    {
        float v = float.IsFinite(groundSpeedMps) ? Mathf.Abs(groundSpeedMps) : 0f;
        return Mathf.Clamp(CadenceIntercept + (CadenceSlope * v), CadenceMinHz, CadenceMaxHz);
    }

    /// <summary><b>Metres of ground covered by one step</b>, at this speed. This is the identity —
    /// <c>stride × cadence = speed</c> — solved for stride, and it is the reason the feet stop
    /// skating.</summary>
    public static float StepLengthAt(float groundSpeedMps)
    {
        float v = float.IsFinite(groundSpeedMps) ? Mathf.Abs(groundSpeedMps) : 0f;
        return v / CadenceAt(v);
    }

    /// <summary>Seconds one foot's complete cycle takes (two steps: its own and the other foot's).</summary>
    public static float CycleSecondsAt(float groundSpeedMps) => 2f / CadenceAt(groundSpeedMps);

    // --- The leg, and how far it may swing ----------------------------------------------------

    /// <summary>
    /// How much shorter the leg is allowed to get, vertically, at the extremes of its swing — as a
    /// fraction of its own length.
    ///
    /// <para><b>This is what caps the stride, and the cap is anatomical rather than aesthetic.</b>
    /// The rig has hips and no knees, so a leg rotated forward by <c>θ</c> about its hip puts its
    /// foot <c>L·(1 − cos θ)</c> above the ground at the moment it plants. Small values of that
    /// read as heel-strike and toe-off; large ones read as a body hovering with its legs waving,
    /// because the foot is visibly not touching anything when it is supposed to be taking weight.
    /// <b>The leg top never moves</b> — a rotation about the hip node leaves the node where it is —
    /// so this bounds the foot's float and nothing else.</para>
    ///
    /// <para><b>0.22, raised from a first pass at 0.16</b> (2026-08-16, off the capture lab's own
    /// numbers). 0.16 gave a 0.195 m reach on the greybox's 0.36 m leg — a 0.39 m stride, which
    /// read but left the sprint on a 8% stance fraction. 0.22 gives 0.225 m of reach and a 0.45 m
    /// stride against 0.079 m of foot float, which is under a tenth of the body's height and reads
    /// as a high step rather than as hovering. Talon's amendment is explicit that a change he cannot
    /// feel is the failure mode, so the bolder end was taken.</para>
    /// </summary>
    public const float LegShorteningFraction = 0.22f;

    /// <summary>Widest half-swing of a leg about its hip, radians. Derived from
    /// <see cref="LegShorteningFraction"/> — 0.6754 rad, 38.7 degrees — so the stride is capped by
    /// the leg rather than by a number somebody liked.</summary>
    public static readonly float MaxLegSwingRad = Mathf.Acos(1f - LegShorteningFraction);

    /// <summary>Duty factor the gait would like: the fraction of one foot's cycle it spends planted.
    /// 0.55 is a walk's. It is a <i>ceiling</i>, not a promise — see <see cref="StanceReachAt"/>,
    /// where a body moving faster than its legs can reach loses stance time rather than losing the
    /// plant.</summary>
    public const float NominalDutyFactor = 0.55f;

    /// <summary>
    /// <b>How far in front of (and behind) the hip a planted foot reaches, in metres.</b>
    ///
    /// <para>Wants <c>duty × stepLength</c>, which is what makes the foot travel the whole stance at
    /// exactly ground speed. Capped at what the leg can physically reach
    /// (<c>legLength · sin(MaxLegSwingRad)</c>) — and when the cap bites, it is the <b>stance
    /// duration</b> that shortens (see <see cref="DutyFactorAt"/>), never the foot's speed. That
    /// ordering is the whole guarantee: a foot in stance always tracks the ground exactly, and a
    /// body too fast for its own legs gets a flight phase, which is what actually happens to a real
    /// runner.</para>
    /// </summary>
    public static float StanceReachAt(float groundSpeedMps, float legLengthM)
    {
        float leg = float.IsFinite(legLengthM) ? Mathf.Max(0.02f, legLengthM) : 0.35f;
        float want = NominalDutyFactor * StepLengthAt(groundSpeedMps);
        return Mathf.Min(want, leg * Mathf.Sin(MaxLegSwingRad));
    }

    /// <summary>
    /// The fraction of one foot's cycle actually spent planted, after the reach cap. Falls as speed
    /// rises — a flight phase, and an honest one.
    ///
    /// <para><b>There is no lower clamp, and an earlier cut of this had one at 0.05.</b> That was
    /// defensive and it was wrong: the plant only holds while
    /// <c>duty = reach · cadence / speed</c> <i>exactly</i>, so raising a duty that the arithmetic
    /// put lower stretches the stance over more of the cycle than the reach can cover and the foot
    /// slides for the difference. The headless suite measured it on the original squat body — 0.08 m legs, so a
    /// true duty of 0.024 clamped up to 0.05, and a foot that should have been planted travelled
    /// 0.10 m while the body travelled 0.21 m. <b>A body whose legs are too short to reach the
    /// ground at its own speed has no stance</b>, and returning that honestly is what keeps every
    /// other body's stance exact.</para>
    /// </summary>
    public static float DutyFactorAt(float groundSpeedMps, float legLengthM)
    {
        float v = float.IsFinite(groundSpeedMps) ? Mathf.Abs(groundSpeedMps) : 0f;
        if (v < 1e-3f)
            return NominalDutyFactor;
        return Mathf.Clamp(StanceReachAt(v, legLengthM) * CadenceAt(v) / v, 0f, NominalDutyFactor);
    }

    /// <summary>
    /// <b>The fore-aft position of one foot, relative to its hip, in metres — the whole gait.</b>
    /// Positive is behind the body; the caller negates for the rig's <c>-Z</c>-forward convention.
    ///
    /// <para><paramref name="cyclePhase"/> is 0..1 through this foot's own cycle: <c>[0, duty)</c>
    /// is <b>stance</b> and the rest is <b>swing</b>.</para>
    ///
    /// <para><b>Stance is a straight line and that is the point.</b> It runs from <c>-reach</c> (foot
    /// planted out in front) to <c>+reach</c> (foot trailing) at a constant rate, and because
    /// <c>reach = duty · speed / cadence</c>, that rate is <i>exactly</i> the ground speed — so the
    /// foot does not move in the world at all while the body travels over it. Swing is eased at both
    /// ends so the foot decelerates into its plant instead of snapping onto it.</para>
    /// </summary>
    public static float FootTrackAt(float cyclePhase, float duty, float reachM)
    {
        float p = Wrap01(cyclePhase);
        // The clamp is a division guard and nothing more. It used to sit at 0.01, which quietly
        // re-created the very defect DutyFactorAt's lower clamp was removed to fix — a duty below
        // it would have been raised here instead, and the stance foot would have slid by the
        // difference. At 1e-4 a foot's stance is at most a sixtieth of a frame, so nothing can
        // slide inside it.
        float d = Mathf.Clamp(duty, 1e-4f, 0.99f);
        if (p < d)
            return Mathf.Lerp(-reachM, reachM, p / d);

        // SWING, IN THREE POSES (MOVE-1, Talon's second amendment). The stance half is arithmetic
        // and cannot be styled — the plant depends on it — but the swing half is free, because
        // nothing is touching the ground, and it is where the gait gets its weight.
        //
        //   GATHER    the foot keeps going BACK past the end of stance before it comes forward.
        //             Anticipation moving opposite the action, on a foot instead of a body.
        //   DRIVE     a fast throw straight through, overshooting PAST where it will plant.
        //   SET       a short settle back onto the plant point, so the foot arrives rather than
        //             stops.
        //
        // Both extremes are deliberately past comfortable, which is the whole note: a rig that
        // interpolates between reasonable values averages, and averaging is mush.
        float s = (p - d) / (1f - d);
        float gatherTo = reachM * (1f + SwingGatherOvershoot);
        float driveTo = -reachM * (1f + SwingPlantOvershoot);
        if (s < SwingGatherFrac)
        {
            float g = s / SwingGatherFrac;
            return Mathf.Lerp(reachM, gatherTo, g * g * (3f - (2f * g)));
        }
        if (s < SwingDriveEnd)
        {
            float t = (s - SwingGatherFrac) / (SwingDriveEnd - SwingGatherFrac);
            // Ease-IN-out, so the throw is fastest in the middle: the fast transition between two
            // held extremes rather than a constant glide between them.
            return Mathf.Lerp(gatherTo, driveTo, t * t * (3f - (2f * t)));
        }
        float u = (s - SwingDriveEnd) / (1f - SwingDriveEnd);
        return Mathf.Lerp(driveTo, -reachM, u * u * (3f - (2f * u)));
    }

    /// <summary>How much of the swing is the backward gather, and how far past the end of stance it
    /// carries as a fraction of the reach.</summary>
    public const float SwingGatherFrac = 0.20f;

    /// <inheritdoc cref="SwingGatherFrac"/>
    public const float SwingGatherOvershoot = 0.20f;

    /// <summary>Where the forward drive ends and the settle onto the plant begins.</summary>
    public const float SwingDriveEnd = 0.82f;

    /// <summary>How far past the plant point the drive throws the foot before it settles back, as a
    /// fraction of the reach.</summary>
    public const float SwingPlantOvershoot = 0.16f;

    /// <summary>How far off the ground a foot is lifted at this point in its cycle, metres. Zero
    /// throughout stance — a planted foot is planted — and a single smooth arc through the swing,
    /// scaled off the reach so a long stride lifts higher than a short one without a second
    /// constant to keep in step.</summary>
    public static float FootLiftAt(float cyclePhase, float duty, float reachM)
    {
        float p = Wrap01(cyclePhase);
        float d = Mathf.Clamp(duty, 1e-4f, 0.99f); // see FootTrackAt for why this floor is tiny
        if (p < d)
            return 0f;
        float s = (p - d) / (1f - d);
        return Mathf.Sin(s * Mathf.Pi) * reachM * FootLiftPerReach;
    }

    /// <summary>Peak swing-foot lift as a fraction of the stance reach.</summary>
    public const float FootLiftPerReach = 0.42f;

    /// <summary>The hip angle that puts a foot at <paramref name="trackM"/> behind its hip on a leg
    /// of <paramref name="legLengthM"/>. Positive rotation about local X swings the foot forward, so
    /// this is negated against the track. Clamped, because a track longer than the leg has no
    /// angle — that only happens if a caller ignores <see cref="StanceReachAt"/>'s cap.</summary>
    public static float LegAngleFor(float trackM, float legLengthM)
    {
        float leg = float.IsFinite(legLengthM) ? Mathf.Max(0.02f, legLengthM) : 0.35f;
        return Mathf.Asin(Mathf.Clamp(-trackM / leg, -1f, 1f));
    }

    // --- The lean: acceleration, never speed --------------------------------------------------

    /// <summary>
    /// Radians of body pitch per m/s² of forward acceleration.
    ///
    /// <para><b>Acceleration, and this is the direct answer to Talon's complaint that nothing tells
    /// him he is speeding up or stopping.</b> The shipped lean was proportional to <i>speed</i> plus
    /// a constant run term — so it read identically at 3 m/s and at 8 m/s, which is to say it
    /// carried no information at all, and it did so at 29.8 degrees (GREY-1 measured it, on every
    /// body). Tied to acceleration instead, the pose is a transient: it appears while you are
    /// getting up to speed, settles to nothing once you are there, and goes the other way while you
    /// brake.</para>
    ///
    /// <para>Tuned so a full-throttle launch (<c>AvatarMotor.Acceleration</c>) reaches about 0.85 of
    /// the cap and a full stop (<c>AvatarMotor.Deceleration</c>, which is deliberately the harder of
    /// the two) saturates it — braking therefore reads as the more violent of the two gestures,
    /// which is what it is.</para>
    /// </summary>
    public const float LeanRadPerAccel = 0.020f;

    /// <summary>Hard cap on the acceleration lean, radians — 0.22 rad, 12.6 degrees. A seventh of
    /// what shipped, and it reads as far more because it moves.</summary>
    public const float MaxAccelLeanRad = 0.22f;

    /// <summary>Radians of body roll per m/s² of lateral acceleration — leaning into a turn. Half
    /// the pitch gain: a body banks less than it pitches.</summary>
    public const float RollRadPerAccel = 0.010f;

    /// <summary>Hard cap on the turn roll, radians (~7.5 degrees).</summary>
    public const float MaxTurnRollRad = 0.13f;

    /// <summary>Seconds of low-pass on the measured acceleration before it becomes a pose. The body
    /// yaws under <c>AvatarMotor.TurnLerp</c>, which injects apparent acceleration into the
    /// body-local frame during a hard turn; without this the lean would twitch on every corner.
    /// Short enough that a launch and a stop still land inside their own ramp.</summary>
    public const float AccelSmoothingSec = 0.10f;

    /// <summary>Pitch, in radians, for a measured forward acceleration. Negative pitches the body
    /// forward (the rig's <c>-Z</c> is forward), so accelerating leans in and braking leans back.</summary>
    public static float LeanForAccel(float forwardAccelMps2) =>
        Mathf.Clamp(-forwardAccelMps2 * LeanRadPerAccel, -MaxAccelLeanRad, MaxAccelLeanRad);

    /// <summary>Roll, in radians, for a measured lateral acceleration.</summary>
    public static float RollForAccel(float lateralAccelMps2) =>
        Mathf.Clamp(-lateralAccelMps2 * RollRadPerAccel, -MaxTurnRollRad, MaxTurnRollRad);

    // --- Keyboard-as-analog -------------------------------------------------------------------

    /// <summary>Seconds for a held key to climb the virtual analog stick from rest to full.
    /// <b>The keyboard emits an analog signal, not a boolean</b> — so a tap is a nudge and a hold
    /// builds, which is the half of "felt acceleration" that no amount of motor tuning can supply
    /// once the input itself is a step function.</summary>
    public const float KeyAnalogRiseSec = 0.22f;

    /// <summary>Seconds for a released key to fall back to rest. Faster than the rise: releasing a
    /// key should stop asking for speed promptly, and the <i>body</i>'s momentum is
    /// <c>AvatarMotor.Deceleration</c>'s job, not the stick's.</summary>
    public const float KeyAnalogFallSec = 0.10f;

    /// <summary>Advance a virtual analog magnitude one frame toward <paramref name="target"/>.</summary>
    public static float StepKeyAnalog(float current, float target, float dt)
    {
        float span = Mathf.Max(1e-4f, target > current ? KeyAnalogRiseSec : KeyAnalogFallSec);
        return Mathf.MoveToward(current, Mathf.Clamp(target, 0f, 1f), dt / span);
    }

    // --- Camera speed cue ---------------------------------------------------------------------

    /// <summary>Normalised 0..1 speed for every presentation channel that reacts to it — the camera
    /// FOV, the orbit length, the look-ahead. Zero at the walk top speed and one at a dead sprint,
    /// so ordinary jogging already sits mid-range and the sprint is visibly the end of a scale
    /// rather than a switch.</summary>
    public static float SpeedCue01(float groundSpeedMps)
    {
        float v = float.IsFinite(groundSpeedMps) ? Mathf.Abs(groundSpeedMps) : 0f;
        return Mathf.Clamp((v - WalkSpeedMps) / (SprintSpeedMps - WalkSpeedMps), 0f, 1f);
    }

    private static float Wrap01(float v)
    {
        if (!float.IsFinite(v))
            return 0f;
        v -= Mathf.Floor(v);
        return v < 0f ? v + 1f : v;
    }
}
