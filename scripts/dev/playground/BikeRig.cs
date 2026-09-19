using Godot;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The bike prototype's arithmetic, pure</b> (BIKE-0, 2026-09-01). Every function here takes
/// values and returns values; nothing reads a node, a clock or <c>MotorTuning.Current</c>. That
/// is what lets <c>tests/unit/BikeRigTests.cs</c> run it without an engine, and it is the same
/// shape <c>AvatarMotor</c>'s own <c>Step*</c> helpers have — so if the feel is ever ruled in, the
/// parts that move into the motor are already in the form the motor takes.
///
/// <para><see cref="BikeLayer"/> is the only caller in the lab. It decides WHEN; this decides
/// WHAT.</para>
/// </summary>
public static class BikeRig
{
    /// <summary>
    /// <b>The ride tuning, derived from the foot tuning, part way there.</b> Not a preset: a
    /// rewrite of whatever the panel currently holds, so a slider dragged on foot still shapes the
    /// bike. <paramref name="blend"/> 0 is the foot tuning exactly, 1 is the full ride, and the
    /// layer walks it from one to the other over <c>MountBlendSec</c> so a mount is felt as a
    /// transition. Only the rows a bike has an opinion about move; the jump arc, the crouch
    /// grammar, the skid and the chain ride across untouched, which is what keeps a jump the same
    /// height on and off the bike.
    /// </summary>
    public static MotorTuning Ride(in MotorTuning foot, in BikeTuning b, float blend = 1f)
    {
        float t = Mathf.Clamp(blend, 0f, 1f);
        // The multiplied rows are held under their knob's ceiling HERE, explicitly, rather than by
        // the validator: a bike preset is pressed over whatever foot preset is loaded (BIKE-1b),
        // and a fast foot times a fast bike must land on a number the readout can name, not one
        // TryApply quietly clamped. BikePresetTests sweeps every pairing for exactly this.
        return foot with
        {
            MoveSpeed = Mul(foot.MoveSpeed, b.RideSpeedMul, t, MotorTuningKnobs.MoveSpeed),
            Acceleration = Mathf.Lerp(foot.Acceleration, b.RideAcceleration, t),
            Deceleration = Mathf.Lerp(foot.Deceleration, b.RideDeceleration, t),
            TurnLerp = Mul(foot.TurnLerp, b.RideTurnMul, t, MotorTuningKnobs.TurnLerp),
            TurnAcceleration = Mul(foot.TurnAcceleration, b.RideTurnAccelMul, t, MotorTuningKnobs.TurnAcceleration),
            AirControlBuild = Mathf.Lerp(foot.AirControlBuild, b.RideAirControlBuild, t),
            AirControlTurn = Mathf.Lerp(foot.AirControlTurn, b.RideAirControlTurn, t),
            AirControlBrake = Mathf.Lerp(foot.AirControlBrake, b.RideAirControlBrake, t),
            // BIKE-1b: the bunny-hop and the double jump ON the bike, each a multiple of the foot's.
            JumpVelocity = Mul(foot.JumpVelocity, b.RideJumpMul, t, MotorTuningKnobs.JumpVelocity),
            AirJumpVelocityFraction = Mul(foot.AirJumpVelocityFraction, b.RideAirJumpMul, t,
                MotorTuningKnobs.AirJumpVelocityFraction),
        };
    }

    /// <summary>A foot row times its ride multiplier, part way there, held inside the knob's range.</summary>
    private static float Mul(float foot, float mul, float t, MotorKnob knob)
        => Mathf.Clamp(Mathf.Lerp(foot, foot * mul, t), knob.Min, knob.Max);

    /// <summary>
    /// <b>The ride tuning with the slope's speed folded into the wish.</b> <paramref name="bonusMps"/>
    /// is added to the sprint wish, so it is divided by the sprint multiplier before it lands in
    /// <c>MoveSpeed</c>, and it is scaled by the blend so a dismount bleeds it out with the rest.
    /// </summary>
    public static MotorTuning RideWithBonus(in MotorTuning foot, in BikeTuning b, float blend, float bonusMps)
    {
        MotorTuning ride = Ride(foot, b, blend);
        float sprint = Mathf.Max(foot.SprintMultiplier, 0.01f);
        float t = Mathf.Clamp(blend, 0f, 1f);
        // Never past the knob's own ceiling: the validator would clamp it there anyway, silently,
        // and a ride that is only legal after clamping is a ride the readout would misreport.
        float wish = Mathf.Min(ride.MoveSpeed + Mathf.Max(bonusMps, 0f) * t / sprint,
            MotorTuningKnobs.MoveSpeed.Max);
        return ride with { MoveSpeed = wish };
    }

    /// <summary>
    /// <b>One tick of the slope bonus.</b> <paramref name="downhillSin"/> is the sine of the floor's
    /// pitch along the heading — positive going down, negative going up, zero on the flat. The
    /// bonus rises at <c>g x sin x gain</c> and is bled at <c>RideDeceleration</c> always, so it
    /// climbs downhill, drains on the flat, and drains faster uphill. Never negative, never above
    /// <c>SlopeBonusMaxMps</c>.
    /// </summary>
    public static float SlopeBonusStep(float bonus, float downhillSin, float dt, in BikeTuning b)
    {
        float pull = AvatarMotor.Gravity * Mathf.Clamp(downhillSin, -1f, 1f) * b.RideSlopeGain;
        float next = bonus + (pull - b.RideDeceleration) * dt;
        return Mathf.Clamp(next, 0f, Mathf.Max(b.SlopeBonusMaxMps, 0f));
    }

    /// <summary>The fastest a body on foot can be asked to go: the sprint wish. The dismount
    /// stumble clamps to a fraction of this.</summary>
    public static float FootCapMps(in MotorTuning foot) => foot.MoveSpeed * foot.SprintMultiplier;

    /// <summary>The fastest a body on the bike can be asked to go. The drift's floor is a fraction
    /// of this.</summary>
    public static float RideCapMps(in MotorTuning foot, in BikeTuning b)
        => foot.MoveSpeed * b.RideSpeedMul * foot.SprintMultiplier;

    /// <summary>A jump out of the crouch-slide: the same tuning with a taller jump.</summary>
    public static MotorTuning SlideJump(in MotorTuning current, in BikeTuning b) => current with
    {
        JumpVelocity = current.JumpVelocity * b.SlideJumpMul,
    };

    /// <summary>A hop: vertical speed becomes at least <paramref name="hopMps"/>, never less than it
    /// already was. Used for the hop onto the bike and the hop off it.</summary>
    public static Vector3 Hop(Vector3 velocity, float hopMps)
        => new(velocity.X, Mathf.Max(velocity.Y, hopMps), velocity.Z);

    /// <summary>
    /// <b>The mid-air mount burst.</b> Forward is added along the heading; vertical becomes the
    /// tuned burst on top of any rise the body still has. A body falling at -6 m/s leaves at +6,
    /// a body rising at +2 leaves at +8 — the mount never costs height already bought.
    /// </summary>
    public static Vector3 AirMountBurst(Vector3 velocity, Vector3 heading, in BikeTuning b)
    {
        Vector3 h = Flat(heading);
        return new Vector3(
            velocity.X + h.X * b.AirMountForwardMps,
            Mathf.Max(velocity.Y, 0f) + b.AirMountUpMps,
            velocity.Z + h.Z * b.AirMountForwardMps);
    }

    /// <summary>
    /// <b>The landing dismount burst.</b> Vertical is SET, not added: the body has just touched
    /// down and whatever vertical speed the floor left it is noise. Forward is added.
    /// </summary>
    public static Vector3 LandDismountBurst(Vector3 velocity, Vector3 heading, in BikeTuning b)
    {
        Vector3 h = Flat(heading);
        return new Vector3(
            velocity.X + h.X * b.LandDismountForwardMps,
            b.LandDismountUpMps,
            velocity.Z + h.Z * b.LandDismountForwardMps);
    }

    /// <summary>
    /// <b>The kick-off: the double jump OFF the bike.</b> Vertical is SET to the kick (the rider
    /// pushes off a frame that then drops away, so whatever fall the body had is arrested by the
    /// push), never below a rise it already had. Forward is added along the heading.
    /// </summary>
    public static Vector3 KickOffBurst(Vector3 velocity, Vector3 heading, in BikeTuning b)
    {
        Vector3 h = Flat(heading);
        return new Vector3(
            velocity.X + h.X * b.KickOffForwardMps,
            Mathf.Max(velocity.Y, b.KickOffUpMps),
            velocity.Z + h.Z * b.KickOffForwardMps);
    }

    /// <summary>
    /// <b>Leaving a lip.</b> <paramref name="uphillSin"/> is the sine of the floor's pitch along the
    /// heading on the last grounded tick, positive going UP. The horizontal speed's along-slope
    /// component becomes rise: <c>vY = max(vY, speed x sin x gain)</c>. A downhill lip, a flat
    /// edge, or a zero gain returns the velocity untouched — a table stays a table.
    /// </summary>
    public static Vector3 RampLaunch(Vector3 velocity, float uphillSin, in BikeTuning b)
    {
        if (uphillSin <= 0.02f || b.RampLaunchGain <= 0f)
            return velocity;
        float speed = new Vector2(velocity.X, velocity.Z).Length();
        float rise = speed * Mathf.Clamp(uphillSin, 0f, 1f) * b.RampLaunchGain;
        return new Vector3(velocity.X, Mathf.Max(velocity.Y, rise), velocity.Z);
    }

    /// <summary>
    /// <b>How long until a body falling under gravity reaches a floor <paramref name="heightM"/>
    /// below it</b>, given its current vertical speed (positive up). Solves
    /// <c>h = -vY t + g t^2 / 2</c> for the positive root; a body already on the floor returns 0.
    /// This is the "before touchdown" half of the landing-dismount window.
    /// </summary>
    public static float TimeToFloor(float verticalMps, float heightM, float gravity)
    {
        if (heightM <= 0f) return 0f;
        float g = Mathf.Max(gravity, 0.01f);
        // t = (vY + sqrt(vY^2 + 2 g h)) / g   (vY positive up: a rising body takes longer)
        return (verticalMps + Mathf.Sqrt(verticalMps * verticalMps + 2f * g * heightM)) / g;
    }

    /// <summary>
    /// <b>The swing's lunge, in the air.</b> Forward is added along the heading; vertical becomes
    /// at least the tuned lift. The same max-never-add rule as every other vertical write.
    /// </summary>
    public static Vector3 SwingLunge(Vector3 velocity, Vector3 heading, in BikeTuning b)
    {
        Vector3 h = Flat(heading);
        return new Vector3(
            velocity.X + h.X * b.SwingLungeForwardMps,
            Mathf.Max(velocity.Y, b.SwingLungeUpMps),
            velocity.Z + h.Z * b.SwingLungeForwardMps);
    }

    /// <summary>The swing on the ground: a step forward into it, nothing vertical.</summary>
    public static Vector3 SwingPush(Vector3 velocity, Vector3 heading, in BikeTuning b)
    {
        Vector3 h = Flat(heading);
        return new Vector3(velocity.X + h.X * b.SwingGroundPushMps, velocity.Y, velocity.Z + h.Z * b.SwingGroundPushMps);
    }

    /// <summary><b>The slingshot</b>: the air-mount burst with the swing's momentum kept — the
    /// tuned extra forward on top of the mount's own.</summary>
    public static Vector3 Slingshot(Vector3 velocity, Vector3 heading, in BikeTuning b)
    {
        Vector3 v = AirMountBurst(velocity, heading, b);
        Vector3 h = Flat(heading);
        return new Vector3(v.X + h.X * b.SlingshotForwardMps, v.Y, v.Z + h.Z * b.SlingshotForwardMps);
    }

    /// <summary>
    /// <b>Where the bike is during the swing</b>, in the body's frame: an offset from the body's
    /// centre. <paramref name="t"/> 0 is on the back, 1 is on the back again; between, it sweeps
    /// one full turn — out past the right hip, across the front at full reach, past the left hip
    /// — with the reach rising and falling on a half sine. Pure, so the greybox and a test agree.
    /// Returns (right, forward) in metres.
    /// </summary>
    public static Vector2 SwingOffset(float t, in BikeTuning b, float restM = 0.32f)
    {
        float k = Mathf.Clamp(t, 0f, 1f);
        float angle = k * Mathf.Tau;                         // 0 = behind, pi = in front
        float reach = Mathf.Lerp(restM, b.SwingReachM, Mathf.Sin(k * Mathf.Pi));
        return new Vector2(Mathf.Sin(angle) * reach, -Mathf.Cos(angle) * reach);
    }

    /// <summary>
    /// <b>The dismount stumble's speed cost.</b> Horizontal speed above the cap is clamped to
    /// <c>footCap x StumbleSpeedFraction</c>; a body already under the cap is untouched, and
    /// vertical speed is never touched. Returns whether anything was clamped, which is what the
    /// layer uses to decide whether a stumble happened at all.
    /// </summary>
    public static Vector3 StumbleClamp(Vector3 velocity, float footCapMps, in BikeTuning b,
        out bool clamped)
    {
        float cap = footCapMps * b.StumbleSpeedFraction;
        var flat = new Vector2(velocity.X, velocity.Z);
        float speed = flat.Length();
        clamped = speed > cap && speed > 1e-4f;
        if (!clamped)
            return velocity;
        flat *= cap / speed;
        return new Vector3(flat.X, velocity.Y, flat.Y);
    }

    /// <summary>
    /// <b>One tick of the drift.</b> The heading swings toward the wish at
    /// <c>DriftTurnDegPerSec</c> — that is the tight corner — while speed sheds gently at
    /// <c>DriftDecel</c> and never below <paramref name="floorMps"/> while the drift is held:
    /// momentum is kept, only the line changes. A body already under the floor is not slowed
    /// further. Vertical is untouched.
    /// </summary>
    public static Vector3 Drift(Vector3 velocity, Vector3 wish, float dt, float floorMps,
        in BikeTuning b)
    {
        var flat = new Vector2(velocity.X, velocity.Z);
        float speed = flat.Length();
        if (speed <= 1e-4f)
            return velocity;

        float next = speed > floorMps
            ? Mathf.Max(floorMps, speed - b.DriftDecel * dt)
            : speed;
        Vector2 dir = flat / speed;

        Vector3 w = Flat(wish);
        if (w.LengthSquared() > 1e-6f)
        {
            var wishDir = new Vector2(w.X, w.Z).Normalized();
            float maxStep = Mathf.DegToRad(b.DriftTurnDegPerSec) * dt;
            float delta = Mathf.Clamp(dir.AngleTo(wishDir), -maxStep, maxStep);
            dir = dir.Rotated(delta);
        }

        return new Vector3(dir.X * next, velocity.Y, dir.Y * next);
    }

    /// <summary>
    /// <b>The hold-to-sprint ramp.</b> Returns the fraction of the sprint wish a body gets after
    /// <paramref name="heldSec"/> of held forward: the jog at zero, full sprint at
    /// <c>HoldRampSec</c>, linear between, clamped at both ends.
    /// </summary>
    public static float HoldRamp(float heldSec, in BikeTuning b)
    {
        float t = b.HoldRampSec <= 0f ? 1f : Mathf.Clamp(heldSec / b.HoldRampSec, 0f, 1f);
        return Mathf.Lerp(b.HoldRampStartFraction, 1f, t);
    }

    /// <summary>
    /// <b>The spring the bike opens on</b>: 0 at the start, overshoots to <c>UnfoldOvershoot</c>
    /// around two thirds of the way, settles to exactly 1 at <paramref name="t"/> = 1 and stays
    /// there. Ease-out-back, with the overshoot as the knob.
    /// </summary>
    public static float UnfoldSpring(float t, in BikeTuning b)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        // Ease-out-back: 1 + (s+1)(t-1)^3 + s(t-1)^2, whose peak is exactly
        // 1 + 4s^3 / (27 (s+1)^2). Solve s for the tuned overshoot by bisection - twenty halvings
        // on a monotone function, cheaper than getting the constant wrong.
        float s = BackParam(b.UnfoldOvershoot - 1f);
        float u = t - 1f;
        return 1f + (s + 1f) * u * u * u + s * u * u;
    }

    private static float BackParam(float overshoot)
    {
        if (overshoot <= 0f) return 0f;
        float lo = 0f, hi = 12f;
        for (int i = 0; i < 24; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float peak = 4f * mid * mid * mid / (27f * (mid + 1f) * (mid + 1f));
            if (peak < overshoot) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    /// <summary>The heading a burst is thrown along: the horizontal velocity while there is one,
    /// otherwise the facing. Never the vertical.</summary>
    public static Vector3 Heading(Vector3 velocity, Vector3 facing, float minSpeedMps = 0.5f)
    {
        var flat = new Vector3(velocity.X, 0f, velocity.Z);
        if (flat.LengthSquared() >= minSpeedMps * minSpeedMps)
            return flat.Normalized();
        return Flat(facing);
    }

    private static Vector3 Flat(Vector3 v)
    {
        var flat = new Vector3(v.X, 0f, v.Z);
        return flat.LengthSquared() > 1e-8f ? flat.Normalized() : Vector3.Zero;
    }
}
