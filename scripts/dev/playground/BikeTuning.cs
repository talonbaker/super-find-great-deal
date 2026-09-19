namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>Every number the bike prototype runs on, in one place</b> (BIKE-0, 2026-09-01; reshaped
/// the same day on Talon's answers — see the report).
///
/// <para>This is a LAB record, not a <c>MotorTuning</c> row: nothing in it reaches the shipped
/// motor, the wire, or a preset. The bike is a layer the playground adds to the body from
/// outside (<see cref="BikeLayer"/>), the same way the slope prototype is, and its knobs live
/// beside it rather than inside the 58-row tuning the sliders own. <see cref="Current"/> is
/// settable without a guard for the same reason the layer exists: there is no session to keep in
/// parity with, and there will not be one until the feel is ruled on.</para>
///
/// <para>Three kinds of number live here. The <c>Ride*</c> rows say how the shipped tuning is
/// <i>rewritten</i> while mounted — <see cref="BikeRig.Ride"/> derives a full
/// <c>MotorTuning</c> from whatever the panel currently holds, blended in over
/// <see cref="MountBlendSec"/>, so a slider still tunes the bike and a mount is a transition
/// rather than a switch. The middle block is the layer's own mechanics: the hops, the two bursts,
/// the stumble, the drift, the slide jump, the hold-to-sprint ramp. The last block is juice —
/// the numbers the greybox bike animates on, which change how the mount LOOKS and nothing about
/// where the body goes.</para>
///
/// <para><b>Talon, 2026-09-01:</b> <i>"I want this to feel like walking is nice and riding is
/// great... an extension of walking and running and jumping."</i> Tight turning; the right-mouse
/// on the bike is a <i>drift</i>, not a brake; the stumble stays but he is not sure he likes it,
/// so it is switchable live (<see cref="StumbleEnabled"/>, N in the lab). Everything else: feel
/// it first.</para>
/// </summary>
public readonly record struct BikeTuning
{
    // --- Riding: how the foot tuning is rewritten while mounted ------------------------------

    /// <summary>The ride wish as a multiple of the foot <c>MoveSpeed</c>. The sprint multiplier is
    /// kept, so the bike's top speed is <c>MoveSpeed x RideSpeedMul x SprintMultiplier</c>: at the
    /// shipped 3.8 / 1.6 that is 9.4 m/s against 6.1 on foot. The gap is what the dismount stumble
    /// prices.</summary>
    public float RideSpeedMul { get; init; }

    /// <summary>Ground acceleration while riding, m/s^2. A bike winds up.</summary>
    public float RideAcceleration { get; init; }

    /// <summary>Ground deceleration while riding, m/s^2. The SURF preset's 2.5 — low enough that
    /// gravity beats friction from about 6.5 degrees, which is what makes a slope pay.</summary>
    public float RideDeceleration { get; init; }

    /// <summary>Heading lerp while riding, as a multiple of the foot <c>TurnLerp</c>. 1.0 means the
    /// bike turns exactly as tightly as the body does — Talon asked for tight turning, and the
    /// shipped 12 is already the snappy end; the drift is what makes a corner tighter still.</summary>
    public float RideTurnMul { get; init; }

    /// <summary>Turn acceleration while riding, as a multiple of the foot value.</summary>
    public float RideTurnAccelMul { get; init; }

    /// <summary>Air control while riding, the three shipped rows. Down hard: leaving a lip on the
    /// bike commits you to the line you left on.</summary>
    public float RideAirControlBuild { get; init; }
    public float RideAirControlTurn { get; init; }
    public float RideAirControlBrake { get; init; }

    /// <summary>How much of gravity's along-slope component reaches a mounted body, 0..1.
    ///
    /// <para><b>Measured 2026-09-01, in the engine:</b> writing the slope's pull into the velocity
    /// after the step (the lab's K probe) does NOT make a mounted body faster while forward is
    /// held — <c>AvatarMotor.RateFor</c> pulls speed above the wish back at <c>Acceleration</c>
    /// (10 m/s² on the bike) whenever the stick is aligned with the heading, which beats gravity's
    /// 5 m/s² on a 12° slope every tick; the measured gain was 0.09 m/s. The K probe only pays in
    /// the crouch-slide, whose own step has no such pull. So the bike carries its slope speed the
    /// way the motor carries every speed: as a bonus on the WISH (<see cref="BikeRig.SlopeBonusStep"/>),
    /// integrated at <c>g x sin(slope) x gain</c> downhill, bled at <c>RideDeceleration</c> on
    /// the flat, capped at <see cref="SlopeBonusMaxMps"/>.</para></summary>
    public float RideSlopeGain { get; init; }

    /// <summary>The jump off the bike's pedals as a multiple of the foot <c>JumpVelocity</c>. 1.0
    /// keeps the bunny-hop the same height as a standing jump, which is BIKE-0's rule ("a jump the
    /// same height on and off the bike"); presets move it both ways so the rule can be doubted.</summary>
    public float RideJumpMul { get; init; }

    /// <summary><b>The double jump ON the bike</b>, as a multiple of the foot
    /// <c>AirJumpVelocityFraction</c>. The shipped tuning already carries a traditional double jump
    /// (<c>AirJumpMode</c> 1, one air jump at 0.80 of the first); a mounted body keeps it — SPACE in
    /// the air while riding is the bike's second hop — and this row is the one knob that makes it
    /// a different jump from the foot's. Blended in with the ride tuning.</summary>
    public float RideAirJumpMul { get; init; }

    /// <summary><b>Launching off a lip.</b> The shipped motor keeps a body's velocity horizontal on
    /// a slope (measured 2026-09-01: a mounted body leaving a 20° kicker at 9 m/s left it with
    /// vY 0 — the kicker was a table). While mounted, leaving an UPHILL floor for the air converts
    /// the ride speed into rise: <c>vY = speed x sin(lip) x gain</c>, never less than the vY the
    /// body already had. 1.0 is the physical value; 0 switches kickers back into tables.</summary>
    public float RampLaunchGain { get; init; }

    /// <summary><b>Rolling over a curb.</b> The tallest step a mounted body climbs without a jump,
    /// metres — a wheel's radius, near enough. <b>Measured 2026-09-01, in the engine: the shipped
    /// motor has no step-up at all. A 0.10 m curb stops the body dead, on foot and on the bike</b>
    /// (the capsule's contact normal on a 10 cm edge is past the floor angle, so it is a wall).
    /// The bike is the one body that should roll over one; the layer lifts it when the way ahead
    /// at this height is clear and the floor beyond is no higher than this. 0 turns it off.</summary>
    public float RideStepUpM { get; init; }

    /// <summary><b>A burst gets its whole arc.</b> The shipped motor applies
    /// <c>JumpReleaseGravityMultiplier</c> (3.5x) to ANY rising body whose jump key is not held —
    /// measured 2026-09-01: a 6 m/s air-mount burst reached 0.21 m instead of 0.75, and a 40°
    /// kicker at 9.4 m/s rose 0.15 m. With this on, the layer holds the jump for the body while a
    /// burst, a hop, a kick-off or a ramp launch is still rising, so the arc is the one the number
    /// promises. Off, every burst is jump-cut, which is what BIKE-0 shipped without knowing.</summary>
    public bool BurstFullArc { get; init; }

    /// <summary>The most speed a slope can add to the ride wish, m/s. A cap rather than a
    /// consequence: the knob table caps <c>MoveSpeed</c> at 12, and a bike that keeps finding more
    /// hill should hit a number Talon chose, not one the validator did.</summary>
    public float SlopeBonusMaxMps { get; init; }

    /// <summary>How long the foot tuning takes to become the ride tuning after a mount, seconds.
    /// The transition Talon asked for: the speed cap and the friction arrive, they do not switch.</summary>
    public float MountBlendSec { get; init; }

    /// <summary>How long the ride tuning takes to become the foot tuning after a dismount.</summary>
    public float DismountBlendSec { get; init; }

    // --- The hops and the two bursts ------------------------------------------------------------

    /// <summary>The hop ONTO the bike on a ground mount, m/s of vertical. Not a burst — a body
    /// jumping onto a bike that has just sprung open under it. 2.4 m/s at the shipped 24 m/s^2 is a
    /// 12 cm hop, over in a third of a second.</summary>
    public float MountHopMps { get; init; }

    /// <summary>The hop OFF the bike on a ground dismount that does not stumble, m/s.</summary>
    public float DismountHopMps { get; init; }

    /// <summary>Vertical speed a MID-AIR mount adds, m/s. Added on top of any upward speed the body
    /// still has (a mount at the apex is the envelope; a mount on the rise keeps the rise), never
    /// subtracted. Once per airtime, like an air jump.</summary>
    public float AirMountUpMps { get; init; }

    /// <summary>Forward speed a mid-air mount adds along the heading, m/s.</summary>
    public float AirMountForwardMps { get; init; }

    /// <summary>Vertical speed a LANDING dismount sets, m/s. Sets rather than adds: the body has
    /// just touched down and its vertical speed is whatever the floor left it.</summary>
    public float LandDismountUpMps { get; init; }

    /// <summary>Forward speed a landing dismount adds along the heading, m/s.</summary>
    public float LandDismountForwardMps { get; init; }

    /// <summary><b>The double jump OFF the bike.</b> Vertical speed a mid-air dismount that is NOT a
    /// landing dismount sets: the body kicks off the bike and the bike folds away beneath it. Set,
    /// not added — the kick is the rider pushing off the frame, which then drops. Talon: <i>"a
    /// double jump on a bike but also off the bike."</i> 0 makes a mid-air dismount a plain
    /// dismount again.</summary>
    public float KickOffUpMps { get; init; }

    /// <summary>Forward speed the kick-off adds along the heading, m/s.</summary>
    public float KickOffForwardMps { get; init; }

    /// <summary>How close to touchdown the dismount press has to be, on EITHER side, for it to
    /// count as a landing dismount and earn the burst, seconds. Outside the window a press in the
    /// air is the kick-off (<see cref="KickOffUpMps"/>) and a press on the ground is a rolling
    /// dismount.
    ///
    /// <para><b>The "before" side is predicted, not waited for.</b> A press in the air casts down
    /// for the floor; if the body will reach it inside this window at its current fall it is a
    /// landing dismount, armed, and bursts on touchdown. If the floor is further away than that the
    /// press is a kick-off NOW — a jump-off that waited the window out to find out which it was
    /// would arrive late every time, and late is the one thing a jump button must never be.</para></summary>
    public float LandDismountWindowSec { get; init; }

    // --- The dismount stumble: the one deliberate speed cost, and switchable -----------------

    /// <summary>Whether a rolling dismount above the foot cap costs anything. Talon: <i>"I don't
    /// know if I like the idea of stumbling but I also need to feel it."</i> N toggles it live in
    /// the lab so both halves of that sentence can be answered on the same run-out.</summary>
    public bool StumbleEnabled { get; init; }

    /// <summary>How long the stumble lasts after a rolling dismount above the foot cap, seconds.
    /// No lockout: the body still steers and still jumps, it just steers less and cannot sprint.</summary>
    public float StumbleSec { get; init; }

    /// <summary>What the body is clamped to on a rolling dismount, as a fraction of the foot cap
    /// (<c>MoveSpeed x SprintMultiplier</c>).</summary>
    public float StumbleSpeedFraction { get; init; }

    /// <summary>Steering authority during the stumble, 0..1. Never zero.</summary>
    public float StumbleSteerFraction { get; init; }

    // --- The drift (right mouse, mounted) -----------------------------------------------------

    /// <summary>How fast the drift sheds speed, m/s^2 — gently: a drift is a corner, not a stop.
    /// Compare the foot skid's 13 and the ride deceleration's 2.5.</summary>
    public float DriftDecel { get; init; }

    /// <summary>The drift never sheds below this fraction of the ride's top speed while it is held.
    /// Momentum is kept; only the line changes. Release the button to coast down further.</summary>
    public float DriftSpeedFloorFraction { get; init; }

    /// <summary>How fast the drifting body's heading swings toward the stick, deg/s. This is the
    /// tight corner: a locked rear wheel lets the nose come round far faster than the steering
    /// lerp ever would.</summary>
    public float DriftTurnDegPerSec { get; init; }

    // --- The swing (left mouse, on foot): the bike as the attack (BIKE-1c) -------------------

    /// <summary><b>Talon, 2026-09-01:</b> <i>"the bike being swung around and extending outward
    /// and then being continued the swing and being put back on the back... the player attacks in
    /// midair (not on the bike) and gains a little bit of distance like the momentum of it is
    /// working toward getting this bike going swinging and throwing himself and the bike toward
    /// whatever it is."</i> How long one swing takes, off the back, round, and back on.</summary>
    public float SwingSec { get; init; }

    /// <summary>How far from the body the bike reaches at the middle of the swing, metres. The
    /// hit cast is this long; the greybox sweeps out to it.</summary>
    public float SwingReachM { get; init; }

    /// <summary><b>The lunge.</b> Forward speed a MID-AIR swing adds along the heading, m/s — the
    /// "little bit of distance". Once per airtime.</summary>
    public float SwingLungeForwardMps { get; init; }

    /// <summary>Vertical the mid-air swing sets, m/s, never below a rise the body already has. Small:
    /// the swing throws you AT something, not over it.</summary>
    public float SwingLungeUpMps { get; init; }

    /// <summary>Forward speed a swing on the GROUND adds, m/s. A step into the swing. The motor pulls
    /// it back toward the wish, so it is a nudge, not a dash.</summary>
    public float SwingGroundPushMps { get; init; }

    /// <summary><b>The slingshot.</b> Q during a mid-air swing continues the swing INTO the mount:
    /// the air-mount burst fires, plus this much more forward — the swing's momentum kept. The
    /// chain the original prompt called "later" and Talon asked for today.</summary>
    public float SlingshotForwardMps { get; init; }

    // --- Slide jump (right mouse on foot, then SPACE) --------------------------------------

    /// <summary>What a jump taken out of the crouch-slide multiplies <c>JumpVelocity</c> by.</summary>
    public float SlideJumpMul { get; init; }

    // --- Hold-to-sprint ramp (no sprint button) --------------------------------------------

    /// <summary>Seconds of held forward before the body is at full sprint.</summary>
    public float HoldRampSec { get; init; }

    /// <summary>Where the ramp starts, as a fraction of the sprint wish. 0.625 is exactly the
    /// jog (3.8 / 6.08), so the first tick of a press feels like the shipped jog.</summary>
    public float HoldRampStartFraction { get; init; }

    // --- Juice: the greybox bike's animation. Changes nothing about where the body goes. -------

    /// <summary>How long the bike takes to leave the back, spring open and land under the body.</summary>
    public float UnfoldSec { get; init; }

    /// <summary>How long the bike takes to snap shut and fly back onto the back.</summary>
    public float FoldSec { get; init; }

    /// <summary>How far past fully-open the spring overshoots before settling (1.14 = 14 %).</summary>
    public float UnfoldOvershoot { get; init; }

    /// <summary>How much the bike squashes on a landing, as a vertical scale (0.82 = 18 % down).</summary>
    public float LandSquash { get; init; }

    /// <summary>How high the bike arcs on its way from the back to the ground, metres.</summary>
    public float UnfoldArcM { get; init; }

    /// <summary>The values the prototype opens on. Every one is a starting point.</summary>
    public static readonly BikeTuning Default = new()
    {
        RideSpeedMul = 1.55f,
        RideAcceleration = 10f,
        RideDeceleration = 2.5f,
        RideTurnMul = 1.0f,
        RideTurnAccelMul = 1.0f,
        RideAirControlBuild = 0.25f,
        RideAirControlTurn = 0.20f,
        RideAirControlBrake = 0.10f,
        RideSlopeGain = 1.0f,
        RideJumpMul = 1.0f,
        RideAirJumpMul = 1.0f,
        RampLaunchGain = 1.0f,
        RideStepUpM = 0.35f,
        BurstFullArc = true,
        SlopeBonusMaxMps = 8f,
        MountBlendSec = 0.25f,
        DismountBlendSec = 0.20f,

        MountHopMps = 2.4f,
        DismountHopMps = 1.6f,
        AirMountUpMps = 6.0f,
        AirMountForwardMps = 2.5f,
        LandDismountUpMps = 7.0f,
        LandDismountForwardMps = 1.5f,
        KickOffUpMps = 6.5f,
        KickOffForwardMps = 1.0f,
        LandDismountWindowSec = 0.18f,

        StumbleEnabled = true,
        StumbleSec = 0.30f,
        StumbleSpeedFraction = 0.85f,
        StumbleSteerFraction = 0.35f,

        DriftDecel = 5f,
        DriftSpeedFloorFraction = 0.45f,
        DriftTurnDegPerSec = 240f,

        SlideJumpMul = 1.35f,

        SwingSec = 0.42f,
        SwingReachM = 1.7f,
        SwingLungeForwardMps = 3.0f,
        SwingLungeUpMps = 1.5f,
        SwingGroundPushMps = 1.2f,
        SlingshotForwardMps = 3.0f,

        HoldRampSec = 1.2f,
        HoldRampStartFraction = 0.625f,

        UnfoldSec = 0.32f,
        FoldSec = 0.22f,
        UnfoldOvershoot = 1.14f,
        LandSquash = 0.82f,
        UnfoldArcM = 0.45f,
    };

    /// <summary>What the layer reads. Lab-only; no guard, no session to protect.</summary>
    public static BikeTuning Current { get; set; } = Default;
}
