using Godot;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The bike's handling model, pure</b> (BIKE-2x, 2026-09-02). The lean, the speed-dependent turn
/// curve, and the drift's charge ladder — every one of them a function of values, returning values.
/// Nothing here reads a node, a clock, <c>MotorTuning.Current</c> or
/// <see cref="BikeHandlingTuning.Current"/>; the same shape <see cref="BikeRig"/> has, and for the
/// same reason: it is what lets <c>tests/unit/BikeHandlingTests.cs</c> run every claim without an
/// engine.
///
/// <para><b>This file writes no velocity, and it is not a second movement system.</b> Talon,
/// 2026-09-02: <i>"Lean is PRESENTATION plus turn shaping, not a new motor. The bike stays a
/// rewrite of the shipped motor's tuning through BikeRig.Ride."</i> So there are exactly two seams
/// out of here, and both are narrow:</para>
/// <list type="number">
/// <item><b>The turn curve is a multiplier on <c>BikeTuning.RideTurnMul</c>.</b> The harness reads
/// <see cref="TurnMultiplier"/>, multiplies the base row by it, and hands the whole record back
/// through <c>BikeLayer.SetTuning</c> — which is the one existing writer of the ride tuning, and
/// which re-derives the ride through <c>BikeRig.Ride</c> exactly as an ALT-row preset does. No new
/// path into the motor is opened.</item>
/// <item><b>The drift's exit boost is an impulse request.</b> <see cref="DriftStep"/> returns the
/// metres per second the exit is worth and <b>nothing applies it here</b>; the harness asks
/// <c>BikeLayer</c> for it through the impulse seam the first agent is adding
/// (<c>RequestImpulse</c>). Until that seam is on the branch the boost is computed, pinned,
/// displayed and logged, and the body does not move for it. That is deliberate: a second writer of
/// <c>Velocity</c> is the exact defect this lab's file ownership exists to prevent.</item>
/// </list>
///
/// <para><b>Nothing here can lock a player out of control.</b> Talon: <i>"no recovery lockouts
/// anywhere."</i> <see cref="GripStep"/> returns a number the lean and the readout consume; it
/// gates no input, cancels no jump, and refuses no steering. The one thing that ends by itself is
/// the drift, at <see cref="BikeHandlingTuning.DriftMaxSec"/>, and ending a drift hands steering
/// back rather than taking it away.</para>
/// </summary>
public static class BikeHandling
{
    /// <summary>Natural log of 100, the constant that turns "seconds to 99 %" into the exponential's
    /// time constant. Spelled out rather than typed as 4.6052 so the derivation is legible: a
    /// first-order lag is at <c>1 - exp(-t/tau)</c>, and <c>1 - exp(-ln 100) = 0.99</c> exactly.</summary>
    private const float Ln100 = 4.6051702f;

    // --- Lean ----------------------------------------------------------------------------------

    /// <summary>
    /// <b>The lean the bike would settle at, degrees</b>, for a body travelling at
    /// <paramref name="speedMps"/> and turning at <paramref name="yawRatePerSec"/> radians per
    /// second.
    ///
    /// <para>The physics is one line: a leaning bike balances gravity against the lateral
    /// acceleration of the turn, so <c>tan(lean) = v * w / g</c>. That is the honest angle; it is
    /// then multiplied by <see cref="BikeHandlingTuning.LeanExaggeration"/> (the stylisation dial,
    /// because at the lab's 9.4 m/s ceiling the honest angle is single digits and a lean nobody can
    /// see is a lean nobody can tune) and clamped to
    /// <see cref="BikeHandlingTuning.LeanMaxDeg"/>.</para>
    ///
    /// <para><b>Sign convention, stated once and obeyed everywhere:</b> Godot's yaw is a rotation
    /// about +Y, so a <b>positive</b> yaw rate is a turn to the rider's <b>left</b>, and a bike
    /// turning left leans left. So a positive return value means <b>leaning left</b>, and it is
    /// applied to the greybox as a positive rotation about its local +Z — which tips its +X (right)
    /// side up, i.e. leans it left. A sign error here is invisible headless and obvious in one
    /// headed frame, which is why the convention is written down rather than discovered.</para>
    ///
    /// <para>The gravity used is <c>AvatarMotor.Gravity</c>, the motor's own, not 9.81: the whole
    /// point of a lean derived from the turn is that it agrees with the body it is drawn on.</para>
    /// </summary>
    public static float LeanSteadyDeg(float speedMps, float yawRatePerSec, in BikeHandlingTuning h)
    {
        float max = Mathf.Max(h.LeanMaxDeg, 0f);
        if (max <= 0f)
            return 0f;
        float g = Mathf.Max(AvatarMotor.Gravity, 0.01f);
        float lateral = Mathf.Abs(speedMps * yawRatePerSec);
        float deg = Mathf.RadToDeg(Mathf.Atan(lateral / g)) * Mathf.Max(h.LeanExaggeration, 0f);
        deg = Mathf.Min(deg, max);
        return yawRatePerSec >= 0f ? deg : -deg;
    }

    /// <summary>
    /// <b>One tick of the lean's first-order lag.</b> <c>lean += (target - lean) * (1 - exp(-rate
    /// dt))</c>.
    ///
    /// <para><b>Exponential rather than <c>lerp(a, b, rate * dt)</c>, and that is not a
    /// preference.</b> The linear form is frame-rate dependent — ten steps of 1 ms and one step of
    /// 10 ms give different answers, and it overshoots outright once <c>rate * dt</c> passes 1. This
    /// lab runs its physics at a fixed tick and its presentation on a variable one, so a lean
    /// stepped on the render clock must not change with the frame rate. The exponential form is
    /// exact for any dt and provably never overshoots: the factor is in [0, 1) for every
    /// non-negative rate and dt.</para>
    /// </summary>
    public static float LeanStep(float leanDeg, float targetDeg, float dt, in BikeHandlingTuning h)
    {
        if (dt <= 0f)
            return leanDeg;
        float k = 1f - Mathf.Exp(-Mathf.Max(h.LeanRatePerSec, 0f) * dt);
        return leanDeg + (targetDeg - leanDeg) * k;
    }

    // --- The low-speed wobble (BIKE-4B, 2026-09-02) ---------------------------------------------

    /// <summary><b>The wobble's primary rate, Hz.</b> A rider correcting a slow bike works the bars
    /// at roughly one to two beats a second; 1.3 Hz sits in that band and is slow enough to read as
    /// a rock rather than a buzz at 60 fps. A constant rather than a knob because the packet scopes
    /// this feature to exactly two rows (amplitude, fade speed); if the rate turns out to be the
    /// interesting dial, it is one line from becoming a third.</summary>
    public const float WobblePrimaryHz = 1.30f;

    /// <summary><b>The second rate, Hz.</b> 2.1 against 1.3 is deliberately not a whole-number
    /// ratio, so the sum never lands back on the same shape and the wobble does not read as a
    /// metronome. It is still perfectly deterministic — two sines of a scalar clock, replayable to
    /// the bit from any snapshot.</summary>
    public const float WobbleSecondaryHz = 2.10f;

    /// <summary>How much of the amplitude the second rate carries, 0..1. The two shares sum to 1,
    /// so <see cref="BikeHandlingTuning.WobbleAmplitudeDeg"/> IS the peak degrees the wobble can
    /// reach (both sines peaking together), and the knob means what its name says.</summary>
    public const float WobbleSecondaryShare = 0.35f;

    /// <summary>
    /// <b>The low-speed lean wobble, degrees</b> — the one thing a real bike does that this lab's
    /// mounted body did not (BIKE-3C, §3 gap 1: <i>"the lab's mounted body is rock-steady at
    /// 1 m/s, which is the single most un-bikelike thing about it"</i>). A small oscillating roll at
    /// walking pace that fades to nothing by the ride jog. Positive means leaning left, the same
    /// convention <see cref="LeanSteadyDeg"/> states.
    ///
    /// <para><b>Presentation, and provably only presentation.</b> The return value is added to the
    /// roll the greybox is drawn at and to nothing else: never
    /// <see cref="TurnMultiplier"/>, never a tuning row, never a velocity, never
    /// <see cref="DriftState"/>. That is the whole of its contract, and it is the reason a wobble is
    /// affordable at all — a bike that oscillates its <i>steering</i> at low speed would be a second
    /// movement system with a prediction story; a bike that oscillates its <i>picture</i> costs one
    /// float on one client.</para>
    ///
    /// <para><b>The shape.</b> Two sines of a local clock, summed, times a speed envelope:
    /// <c>amp * smoothstep(1 - speed/fade) * (0.65 sin(2 pi 1.3 t) + 0.35 sin(2 pi 2.1 t))</c>.
    /// The envelope is flat-topped at rest (smoothstep's derivative is zero at both ends), so a
    /// track-stand is alive and the wobble does not visibly ramp the instant the bike creeps; and it
    /// arrives at the fade speed with value AND slope zero, so the wobble dies away rather than
    /// clipping off. At and above the fade speed the answer is <b>exactly</b> <c>0f</c>, by an early
    /// return rather than by arithmetic that happens to round there.</para>
    ///
    /// <para><b>Both sines are zero at <paramref name="phaseSec"/> zero</b>, deliberately: the
    /// harness holds the phase at zero whenever the bike is stowed, so a mount always begins
    /// upright and the wobble grows
    /// out of the mount instead of popping into it.</para>
    ///
    /// <para><b>Fade anchor.</b> BIKE-3C row 13: Wobble faded its own wobble at top speed because
    /// pedalling was the destabiliser, and it names <i>"stability crossover ~4.3 m/s"</i> with the
    /// instruction that this lab's wobble is idle texture and should fade earlier — between walk and
    /// the ride jog. <see cref="BikeHandlingTuning.WobbleFadeSpeedMps"/> defaults there.</para>
    ///
    /// <para>Pure, over value types, no allocation, no node, no clock of its own — BIKE-3C row 12's
    /// standing rule, and it passes that row's test verbatim: <c>DriftStep</c> could replay it from
    /// a snapshot, because <c>(speed, phase, tuning)</c> is the whole of its input.</para>
    /// </summary>
    /// <param name="speedMps">Horizontal ground speed. Sign is ignored — a bike rolling backwards
    /// at 1 m/s is exactly as unstable as one rolling forwards at 1 m/s.</param>
    /// <param name="phaseSec">Seconds on a local, unreplicated clock. Each client advances its own;
    /// nothing about the wobble goes on the wire.</param>
    public static float WobbleDeg(float speedMps, float phaseSec, in BikeHandlingTuning h)
    {
        // Written as `!(x > 0)` rather than `x <= 0` so a NaN row (a knob dragged into one, a
        // preset typed wrong) fails the guard and returns a clean zero instead of poisoning the
        // roll — the same defensive shape LeanSteadyDeg's `max <= 0` guard has, made NaN-tight.
        float amp = h.WobbleAmplitudeDeg;
        float fade = h.WobbleFadeSpeedMps;
        if (!(amp > 0f) || !(fade > 0f))
            return 0f;

        float speed = Mathf.Abs(speedMps);
        if (!(speed < fade))            // at, above, or NaN: exactly zero, no arithmetic
            return 0f;

        float u = 1f - speed / fade;                    // 1 at rest, 0 at the fade speed
        float envelope = u * u * (3f - 2f * u);         // smoothstep: flat at both ends
        float w = Mathf.Sin(Mathf.Tau * WobblePrimaryHz * phaseSec) * (1f - WobbleSecondaryShare)
                + Mathf.Sin(Mathf.Tau * WobbleSecondaryHz * phaseSec) * WobbleSecondaryShare;
        return amp * envelope * w;
    }

    /// <summary>
    /// <b>The yaw rate between two facings</b>, radians per second, taking the short way round.
    ///
    /// <para>Without the wrap a body crossing the +/-pi seam reports a yaw rate of about
    /// <c>2 pi / dt</c> — at a 60 Hz tick that is 377 rad/s, which drives the lean straight into its
    /// clamp for one frame and reads as a flicker nobody can explain. <c>Mathf.AngleDifference</c>
    /// is the shipped helper for exactly this.</para>
    /// </summary>
    public static float YawRatePerSec(float yawNowRad, float yawPrevRad, float dt)
        => dt <= 0f ? 0f : Mathf.AngleDifference(yawPrevRad, yawNowRad) / dt;

    // --- Turn shaping --------------------------------------------------------------------------

    /// <summary>
    /// <b>The speed-dependent turn multiplier</b>, to be applied on top of whatever
    /// <c>BikeRig.Ride</c> already derived from <c>RideTurnMul</c>.
    ///
    /// <para>Tight at low speed, wider at high:
    /// <c>lerp(TurnLowSpeedMul, TurnHighSpeedMul, (speed / rideCap)^exponent)</c>, with the
    /// normalised speed clamped to 0..1 so anything above the cap (a slope bonus, a burst) simply
    /// stays at the wide end rather than extrapolating past it.</para>
    ///
    /// <para><b>Why the shipped motor needs this at all.</b> <c>AvatarMotor</c> turns a body toward
    /// its wish at <c>TurnLerp</c> regardless of how fast it is going, so a bike at 1 m/s and a bike
    /// at 9 m/s corner at the same rate — which is what makes a fast body feel like it is skating
    /// rather than riding. The multiplier is the whole of the fix, and the drift is what buys a
    /// tight corner back at speed.</para>
    /// </summary>
    public static float TurnMultiplier(float speedMps, float rideCapMps, in BikeHandlingTuning h)
    {
        float cap = Mathf.Max(rideCapMps, 0.01f);
        float s = Mathf.Clamp(speedMps / cap, 0f, 1f);
        float e = Mathf.Max(h.TurnCurveExponent, 0.01f);
        return Mathf.Lerp(h.TurnLowSpeedMul, h.TurnHighSpeedMul, Mathf.Pow(s, e));
    }

    // --- Grip ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Grip, 0..1, after a drift is released.</b> <c>1 - exp(-t / tau)</c> with
    /// <c>tau = DriftGripRecoverSec / ln 100</c>, so the row means exactly what it says: at
    /// <c>t = DriftGripRecoverSec</c> this returns 0.99.
    ///
    /// <para>A row whose meaning needs a conversion before it can be judged is a row nobody tunes,
    /// which is why the time constant is derived from the human-readable number rather than the
    /// other way round.</para>
    /// </summary>
    public static float GripAfterRelease(float secSinceRelease, in BikeHandlingTuning h)
    {
        if (secSinceRelease <= 0f)
            return 0f;
        float sec = Mathf.Max(h.DriftGripRecoverSec, 1e-4f);
        return Mathf.Clamp(1f - Mathf.Exp(-secSinceRelease * Ln100 / sec), 0f, 1f);
    }

    /// <summary>
    /// <b>One tick of grip.</b> While the drift is live grip is 0 — a locked rear wheel has no grip
    /// and does not lose it gradually. Off the drift it climbs on the same curve
    /// <see cref="GripAfterRelease"/> describes, stepped so the caller does not have to carry a
    /// "seconds since release" clock beside the value.
    /// </summary>
    public static float GripStep(float grip, float dt, bool drifting, in BikeHandlingTuning h)
    {
        if (drifting)
            return 0f;
        if (dt <= 0f)
            return Mathf.Clamp(grip, 0f, 1f);
        float sec = Mathf.Max(h.DriftGripRecoverSec, 1e-4f);
        float k = 1f - Mathf.Exp(-dt * Ln100 / sec);
        return Mathf.Clamp(grip + (1f - grip) * k, 0f, 1f);
    }

    // --- The drift -------------------------------------------------------------------------------

    /// <summary>
    /// <b>Everything the drift model remembers between ticks.</b> A value, so <see cref="DriftStep"/>
    /// stays pure and a test can hold two of these side by side.
    /// </summary>
    public readonly record struct DriftState
    {
        /// <summary>Whether a drift is live right now — the handling model's own answer, which is
        /// stricter than <c>BikeLayer.Drifting</c>: that one is "mounted, held, on the floor" and
        /// has no entry speed.</summary>
        public bool Active { get; init; }

        /// <summary>How long the live drift has run, seconds. Zero when none is live.</summary>
        public float HeldSec { get; init; }

        /// <summary>Charge banked in the live drift, in seconds — real seconds under the duration
        /// model, seconds-equivalent under the wiggle model. Zero when none is live.</summary>
        public float ChargeSec { get; init; }

        /// <summary>Grip, 0..1. Zero while a drift is live; climbs back afterwards.</summary>
        public float Grip { get; init; }

        /// <summary>The steering value at the last registered flick, for the wiggle model. Only
        /// meaningful while <see cref="Active"/>.</summary>
        public float LastFlickSteer { get; init; }

        /// <summary><b>The re-entry latch.</b> A drift that ended because it hit
        /// <see cref="BikeHandlingTuning.DriftMaxSec"/> must not restart on the next tick with the
        /// button still down — that would let a player farm an exit boost per
        /// <c>DriftMaxSec</c> by doing nothing at all. True means "the button has to come up before
        /// another drift may start".</summary>
        public bool NeedsRelease { get; init; }

        /// <summary>A state with full grip and nothing live — what a body on foot has.</summary>
        public static readonly DriftState Rest = new() { Grip = 1f };
    }

    /// <summary>What one <see cref="DriftStep"/> produced besides the next state.</summary>
    public readonly record struct DriftResult
    {
        /// <summary>The state to carry into the next tick.</summary>
        public DriftState Next { get; init; }

        /// <summary><b>The exit boost this tick earned, m/s along the heading.</b> Non-zero on
        /// exactly the tick a drift ends, and zero on every other tick. <b>Nothing in this file
        /// applies it</b> — see the class doc: it is an impulse REQUEST, and only
        /// <c>BikeLayer</c> may write velocity.</summary>
        public float ExitBoostMps { get; init; }

        /// <summary>The tier the drift ended on, 0..3. Zero except on an exit tick.</summary>
        public int ExitTier { get; init; }

        /// <summary>True on the tick a drift started — the harness's cue for the diegetic dust
        /// puff, and the count the telemetry increments.</summary>
        public bool Entered { get; init; }
    }

    /// <summary>
    /// <b>One tick of the drift.</b>
    ///
    /// <para><b>Entry is the rule the packet states hardest:</b> <i>the drift must never enter from
    /// ordinary cornering.</i> So all five of these must hold on the same tick — mounted, on the
    /// floor, the button held, at or above <see cref="BikeHandlingTuning.DriftEntrySpeedMps"/>, and
    /// not latched out by a previous drift that ran to its limit. A body rounding a corner at a
    /// jog with the button down gets nothing at all: no charge, no tier, no boost, and no grip
    /// cost.</para>
    ///
    /// <para><b>Charge climbs one of two ways</b>, per
    /// <see cref="BikeHandlingTuning.DriftWiggleCharge"/>, and both fill the same ladder so a tier
    /// threshold means the same thing under either. Duration: real seconds. Wiggle: a fixed
    /// seconds-equivalent per qualifying flick of <paramref name="steer"/>, where a flick is a
    /// crossing of centre with at least <see cref="BikeHandlingTuning.DriftWiggleFlickMin"/> of
    /// throw on the new side — the throw floor is what stops a stick resting on noise from filling
    /// a charge nobody earned.</para>
    ///
    /// <para><b>Exit pays out on every route out</b>: the button released, the wheels leaving the
    /// ground, a dismount, or the duration cap. Leaving the ground pays because a jump out of a
    /// loaded corner is the good version of this move and charging a player for taking it would
    /// teach them not to. The duration cap is the one exit that latches
    /// (<see cref="DriftState.NeedsRelease"/>).</para>
    ///
    /// <para><b>Speed is not re-checked after entry.</b> A drift that sheds below the entry speed
    /// mid-corner keeps running — the entry gate is about how a drift may START, and cancelling one
    /// underneath a player because a hill slowed them is the sort of thing that reads as the game
    /// taking the controls away. Talon: <i>"no recovery lockouts anywhere."</i></para>
    /// </summary>
    /// <param name="prev">Last tick's state.</param>
    /// <param name="mounted">Is the body on the bike.</param>
    /// <param name="grounded">Are the wheels on the floor.</param>
    /// <param name="held">Is the drift button (RMB) down.</param>
    /// <param name="speedMps">Horizontal speed.</param>
    /// <param name="steer">Lateral steering input, -1..1. Only read under the wiggle model.</param>
    /// <param name="dt">Seconds since the last call.</param>
    public static DriftResult DriftStep(in DriftState prev, bool mounted, bool grounded, bool held,
        float speedMps, float steer, float dt, in BikeHandlingTuning h)
    {
        float step = Mathf.Max(dt, 0f);

        // The latch clears the moment the button comes up, whatever else is true. Done first so a
        // release-and-repress inside one tick sequence can never be swallowed.
        bool needsRelease = prev.NeedsRelease && held;

        if (!prev.Active)
        {
            bool canEnter = mounted && grounded && held && !needsRelease
                         && speedMps >= h.DriftEntrySpeedMps;
            if (!canEnter)
                return new DriftResult
                {
                    Next = prev with
                    {
                        Active = false,
                        HeldSec = 0f,
                        ChargeSec = 0f,
                        Grip = GripStep(prev.Grip, step, drifting: false, h),
                        NeedsRelease = needsRelease,
                    },
                };

            return new DriftResult
            {
                Entered = true,
                Next = new DriftState
                {
                    Active = true,
                    HeldSec = 0f,
                    ChargeSec = 0f,
                    Grip = 0f,
                    LastFlickSteer = steer,
                    NeedsRelease = false,
                },
            };
        }

        // --- a drift is live ---------------------------------------------------------------------

        float heldSec = prev.HeldSec + step;
        float charge = prev.ChargeSec;
        float lastFlick = prev.LastFlickSteer;

        if (h.DriftWiggleCharge)
        {
            if (IsFlick(steer, lastFlick, h))
            {
                charge += Mathf.Max(h.DriftWiggleFlickSec, 0f);
                lastFlick = steer;
            }
        }
        else
        {
            charge += step;
        }

        bool expired = h.DriftMaxSec > 0f && heldSec >= h.DriftMaxSec;
        bool endsNow = expired || !held || !grounded || !mounted;

        if (!endsNow)
            return new DriftResult
            {
                Next = prev with
                {
                    Active = true,
                    HeldSec = heldSec,
                    ChargeSec = charge,
                    Grip = 0f,
                    LastFlickSteer = lastFlick,
                    NeedsRelease = false,
                },
            };

        int tier = DriftTier(charge, h);
        return new DriftResult
        {
            ExitBoostMps = ExitBoostMps(tier, h),
            ExitTier = tier,
            Next = new DriftState
            {
                Active = false,
                HeldSec = 0f,
                ChargeSec = 0f,
                Grip = GripStep(0f, step, drifting: false, h),
                // Only the duration cap latches. A player who let go, jumped, or got off has
                // already done the thing the latch exists to require.
                NeedsRelease = expired && held,
            },
        };
    }

    /// <summary>
    /// <b>Is this steering value a flick?</b> The stick has crossed centre since the last one and
    /// is now thrown at least <see cref="BikeHandlingTuning.DriftWiggleFlickMin"/> to the new side.
    ///
    /// <para>The throw floor does the real work. Without it a stick resting near centre registers a
    /// flick on every sign change of its own noise, and the wiggle model fills its ladder while
    /// nobody touches anything — which would make the A/B against the duration model meaningless in
    /// the one direction that matters.</para>
    /// </summary>
    public static bool IsFlick(float steer, float lastFlickSteer, in BikeHandlingTuning h)
    {
        float min = Mathf.Max(h.DriftWiggleFlickMin, 0.01f);
        if (Mathf.Abs(steer) < min)
            return false;
        // A first flick (nothing banked yet) counts on either side; after that the side must change.
        return Mathf.Abs(lastFlickSteer) < min || Mathf.Sign(steer) != Mathf.Sign(lastFlickSteer);
    }

    /// <summary>
    /// <b>The tier a charge has reached</b>, 0 (nothing) to 3 (the top). Thresholds are read in
    /// order, highest first, so a mis-ordered set of rows makes a tier unreachable rather than
    /// making the ladder incoherent.
    /// </summary>
    public static int DriftTier(float chargeSec, in BikeHandlingTuning h)
    {
        if (h.DriftTier3Sec > 0f && chargeSec >= h.DriftTier3Sec) return 3;
        if (h.DriftTier2Sec > 0f && chargeSec >= h.DriftTier2Sec) return 2;
        if (h.DriftTier1Sec > 0f && chargeSec >= h.DriftTier1Sec) return 1;
        return 0;
    }

    /// <summary>What a tier pays on release, m/s along the heading. Any of them may be zero — the
    /// packet says the exit boost may be, and a zeroed ladder turns the tiers into pure feedback,
    /// which is a real option once Talon has felt both.</summary>
    public static float ExitBoostMps(int tier, in BikeHandlingTuning h) => tier switch
    {
        3 => Mathf.Max(h.DriftTier3BoostMps, 0f),
        2 => Mathf.Max(h.DriftTier2BoostMps, 0f),
        1 => Mathf.Max(h.DriftTier1BoostMps, 0f),
        _ => 0f,
    };

    /// <summary>
    /// <b>How far the live drift is through its current tier, 0..1</b> — the diegetic feedback's
    /// only input. The spark brightens across a tier and changes colour at the boundary, and there
    /// is deliberately no number anywhere: the packet's rule is that tier feedback is diegetic only,
    /// no counter.
    /// </summary>
    public static float TierProgress01(float chargeSec, in BikeHandlingTuning h)
    {
        int tier = DriftTier(chargeSec, h);
        float from = tier switch { 0 => 0f, 1 => h.DriftTier1Sec, 2 => h.DriftTier2Sec, _ => h.DriftTier3Sec };
        float to = tier switch { 0 => h.DriftTier1Sec, 1 => h.DriftTier2Sec, 2 => h.DriftTier3Sec, _ => 0f };
        if (tier >= 3 || to <= from)
            return 1f;
        return Mathf.Clamp((chargeSec - from) / (to - from), 0f, 1f);
    }

    /// <summary>
    /// <b>The colour a tier's sparks burn.</b> Kept here rather than in the harness so the test can
    /// pin that the four tiers are visually distinct — the whole feedback channel is this colour and
    /// its brightness, because there is no counter to fall back on.
    ///
    /// <para>Tier 0 is the neutral grey of ordinary dust: a drift that has earned nothing must not
    /// look like one that has.</para>
    /// </summary>
    public static Color TierColour(int tier) => tier switch
    {
        3 => new Color(0.75f, 0.35f, 1.00f),   // violet — the top
        2 => new Color(1.00f, 0.45f, 0.10f),   // orange
        1 => new Color(0.30f, 0.70f, 1.00f),   // blue
        _ => new Color(0.72f, 0.70f, 0.66f),   // dust
    };
}
