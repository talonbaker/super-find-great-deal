namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>Every number the bike's camera runs on, in one place</b> (BIKE-2x-C, 2026-09-02).
///
/// <para>A LAB record, exactly like <see cref="BikeTuning"/> and for the same reason: nothing in
/// it reaches <c>SandboxCamera</c>, the shipped rig, a preset or the wire. <see cref="BikeCamera"/>
/// is a decorator that runs after the shipped camera has placed the lens and adds to what it did;
/// these are that addition's knobs. <see cref="Current"/> is settable without a guard because
/// there is no session to keep in parity with, and there will not be one until the feel is ruled
/// on — the same argument <see cref="BikeTuning.Current"/> makes.</para>
///
/// <para><b>Why a bike wants its own camera channels at all, stated as a fact about the shipped
/// one rather than as taste.</b> <c>SandboxCamera</c> drives its four speed channels off
/// <c>LocomotionProfile.SpeedCue01</c>, which normalises against the FOOT sprint
/// (<c>MoveSpeed x SprintMultiplier</c> = 6.08 m/s at the shipped tuning) and CLAMPS at 1. The
/// ride cap is <c>MoveSpeed x RideSpeedMul x SprintMultiplier</c> — 9.42 m/s at
/// <see cref="BikeTuning.Default"/> — so the foot sprint is only <b>65 %</b> of what a mounted
/// body can reach, and across the whole top third of the bike's speed range the shipped FOV,
/// orbit, look-ahead and follow stiffness are all pinned at their maxima and say nothing further.
/// Every row here is normalised against the RIDE cap instead, which is the one change that makes
/// the camera able to report the speed the bike actually has.</para>
///
/// <para><b>Four channels, four independent curves — deliberately NOT one shared curve.</b>
/// <c>SandboxCamera</c>'s own doc argues the opposite for the shipped rig ("four channels, one
/// input... a camera whose FOV, orbit and framing each ran on their own curve reads as instability
/// rather than as speed") and that argument is right for a body. It is not what this packet was
/// asked for: the look-ahead in particular has to be tunable on its own, because "how far ahead
/// the bike looks" is the question a headed run is expected to answer first and it must be
/// answerable without dragging the orbit and the lens along with it. Each channel therefore gets
/// its own two rows and its own two damping rates, and the only thing they share is
/// <see cref="SpeedCurveExponent"/> — one shaping row, applied to the normalised speed BEFORE any
/// channel reads it, so bending the response is still a single edit when that is what is
/// wanted.</para>
///
/// <para><b>Every default here is a starting point and the FOV and distance rows are the two
/// least trustworthy.</b> The lab's ground is a checker
/// (<c>MovementCourse.CheckerMaterial</c>, a 1 m world-space pattern at ±10 % of each block's own
/// albedo) and the brief that asked for it — Talon's movement-feel note N4, 2026-08-28 — says
/// plainly why: <i>"perceived speed comes from texture flowing past the eye, and without it a
/// 5.4 m/s jog and an 8.6 m/s sprint look nearly the same."</i> A ±10 % checker on flat greybox is
/// a weak version of that texture, so the lab distorts perceived speed downward, and a lens that
/// looks correct against it may be wrong against a dressed world. <b>No number below was measured
/// in a headed run</b> — they are arithmetic against the shipped rig's own constants, and only a
/// headed run can judge them.</para>
/// </summary>
public readonly record struct BikeCameraTuning
{
    // --- Field of view ------------------------------------------------------------------------

    /// <summary>The lens the bike camera aims at with the body at a standstill, degrees. A TOTAL,
    /// not an addition: <see cref="BikeCamera"/> mixes the rendered lens toward this. Opens at
    /// <c>SandboxCamera</c>'s own <c>DefaultFov</c> (75) so a mount taken standing still changes
    /// the lens by exactly nothing, which is the cheapest possible way to make a mount read as a
    /// transition rather than a cut.</summary>
    public float FovBaseDeg { get; init; }

    /// <summary>How many more degrees of field of view the bike camera wants at the RIDE cap.
    /// Added to <see cref="FovBaseDeg"/>, so 18 means 93 degrees flat out. The shipped rig's own
    /// speed gain is 14 degrees reached at the foot sprint; this one is a shade larger and spans
    /// an axis 1.55x longer, so it is still climbing where the shipped one has stopped.</summary>
    public float FovAtCapDeg { get; init; }

    // --- Orbit distance -----------------------------------------------------------------------

    /// <summary><b>EXTRA</b> orbit length at a standstill, metres — added to whatever
    /// <c>SandboxCamera</c>'s own arm placed, never a total. Small on purpose: a mounted body at a
    /// stand is a rider sitting still, and the frame should say "you are on a thing" rather than
    /// re-stage the shot.</summary>
    public float DistanceBaseM { get; init; }

    /// <summary><b>EXTRA</b> orbit length at the ride cap, metres, on top of
    /// <see cref="DistanceBaseM"/>. The arithmetic worth having in front of you: the shipped arm
    /// runs 3.15 m at rest to 4.5 m at the foot sprint, so 0.30 + 1.10 puts the flat-out lens at
    /// about 5.9 m behind a 1.2 m body. That is a long way back for a character this small and it
    /// is <b>the row most likely to be wrong</b> — see the class doc on why the lab's checker
    /// cannot judge it.</summary>
    public float DistanceAtCapM { get; init; }

    // --- Look-ahead: the row set this packet exists for ----------------------------------------

    /// <summary>
    /// <b>How far along the heading the camera aims past the body at a standstill</b>, metres.
    ///
    /// <para>This is a re-AIM, not a focus throw. <c>SandboxCamera</c> already throws its focus
    /// POINT forward (0.25 m at a walk to 2.4 m at the foot sprint) which slides the whole orbit
    /// down the travel direction; this instead leaves the orbit where it is and points the lens at
    /// a spot ahead of the rider. The two compose rather than fight, and the difference matters on
    /// a bike: throwing the focus puts the rider off-centre, while re-aiming puts the ROAD in the
    /// middle of the frame and leaves the rider where the orbit put him.</para>
    ///
    /// <para><b>Independent of every other channel by construction.</b> No shared curve, no value
    /// derived from the distance or the FOV, its own two rows and its own two damping rates —
    /// <c>BikeCameraTests</c> pins that by rewriting the FOV and distance rows and asserting the
    /// look-ahead output does not move a bit.</para>
    /// </summary>
    public float LookAheadBaseM { get; init; }

    /// <summary>How much further along the heading the camera aims at the RIDE cap, metres, on top
    /// of <see cref="LookAheadBaseM"/>. 0.40 + 3.60 is 4.00 m of lead flat out, which at the
    /// 9.42 m/s ride cap is <b>0.42 s</b> of travel — the horizon a rider steers on rather than the
    /// one he occupies. Stating it as a time rather than a distance is what makes the row
    /// re-derivable if the ride cap ever moves; the pin in <c>BikeCameraTests</c> is written
    /// against that ratio for the same reason.</summary>
    public float LookAheadAtCapM { get; init; }

    // --- Height -------------------------------------------------------------------------------

    /// <summary><b>EXTRA</b> lens height at a standstill, metres, world up — added to where the
    /// shipped rig placed the lens.</summary>
    public float HeightBaseM { get; init; }

    /// <summary><b>EXTRA</b> lens height at the ride cap, metres, on top of
    /// <see cref="HeightBaseM"/>. Rising with speed is what turns a shot of the back of a rider's
    /// head into a shot of the ground he is about to cross; it also buys the look-ahead somewhere
    /// to point, since aiming 4 m down the road from a lens at head height mostly frames the
    /// rider's own shoulders.</summary>
    public float HeightAtCapM { get; init; }

    // --- Damping: fast in, slow out, on all four channels, independently ----------------------
    //
    // Every channel is a first-order lag with TWO rates, and the pair is asymmetric on purpose.
    // "In" is the direction of MORE — more FOV, further back, more lead, higher — and it is the
    // fast one because a camera that arrives late to an acceleration is reporting the speed you
    // had rather than the speed you have. "Out" is the return, and it is the slow one because a
    // corner, a drift and a landing all shed speed for a moment, and a camera that snapped back
    // in on every one of them would flicker the whole frame at exactly the rate the rider is
    // working hardest. The rates are 1/s in an exp lag, so "in twice as fast as out" is a real
    // statement about time constants and not about a per-frame fraction.

    /// <summary>How fast the FOV channel opens, 1/s. The fastest of the four: the lens is the
    /// cheapest speed cue there is (<c>SandboxCamera</c>'s own words) and it is the one that must
    /// not lag a launch.</summary>
    public float FovInRate { get; init; }

    /// <summary>How fast the FOV channel closes, 1/s. Strictly below <see cref="FovInRate"/>.</summary>
    public float FovOutRate { get; init; }

    /// <summary>How fast the orbit pushes out, 1/s. Slower than the FOV: the shipped rig eases its
    /// own arm at 2.2/s and calls it "lazy on purpose - the arm should trail the gear change
    /// rather than track it", and an extra push that arrived faster than the arm it sits on would
    /// read as two separate movements.</summary>
    public float DistanceInRate { get; init; }

    /// <summary>How fast the orbit comes back in, 1/s. Strictly below <see cref="DistanceInRate"/>.</summary>
    public float DistanceOutRate { get; init; }

    /// <summary>How fast the look-ahead extends, 1/s.</summary>
    public float LookAheadInRate { get; init; }

    /// <summary>How fast the look-ahead retracts, 1/s. The slowest "out" of the four, and the
    /// asymmetry is widest here: the lead is what makes a corner readable, and pulling it back the
    /// instant a rider scrubs speed into the corner would take the frame away at the exact moment
    /// it is being used.</summary>
    public float LookAheadOutRate { get; init; }

    /// <summary>How fast the lens rises, 1/s.</summary>
    public float HeightInRate { get; init; }

    /// <summary>How fast the lens settles back down, 1/s. Strictly below
    /// <see cref="HeightInRate"/>.</summary>
    public float HeightOutRate { get; init; }

    // --- Collision ----------------------------------------------------------------------------

    /// <summary>Radius of the sphere swept from the focus point to the pushed lens position,
    /// metres. Larger than the shipped <c>SpringArm3D</c>'s own 0.25 m cast sphere: that sweep
    /// measured the orbit the shipped rig asked for and knows nothing about the extra metre or two
    /// this layer adds, so the extra push has to carry its own clearance and a slightly fatter
    /// probe is the cheapest way to keep the near-clip plane out of a wall the arm never
    /// looked at.</summary>
    public float CollisionRadiusM { get; init; }

    /// <summary>How fast a released push is given back, 1/s. <b>There is deliberately no pull-in
    /// rate.</b> Contraction is instant and undamped — the same rule <c>SandboxCamera</c> states
    /// for its own arm ("CONTRACTION stays instant - the camera must never spend a frame inside
    /// whatever just crossed the arm") — and only the ease-out is graded, so an occluder leaving
    /// the path cannot teleport the view outward. 5/s against the shipped arm's 6/s: a shade
    /// lazier, because this push is longer than the arm's own correction usually is.</summary>
    public float CollisionEaseOutRate { get; init; }

    // --- Shaping ------------------------------------------------------------------------------

    /// <summary>The exponent applied to the normalised ride speed before ANY channel reads it.
    /// 1.0 is linear and is where this deliberately opens: a lab has no business bending a curve
    /// it has not felt, and an exponent chosen before a headed run is a guess wearing a number's
    /// clothes. Above 1 holds the channels back until the top of the range (a bike that only
    /// starts to feel fast when it is fast); below 1 spends them early.</summary>
    public float SpeedCurveExponent { get; init; }

    /// <summary>The values the bike camera opens on. Every one is a starting point; see the class
    /// doc for why the lab's checker floor cannot judge the FOV and distance rows.</summary>
    public static readonly BikeCameraTuning Default = new()
    {
        FovBaseDeg = 75f,
        FovAtCapDeg = 18f,

        DistanceBaseM = 0.30f,
        DistanceAtCapM = 1.10f,

        LookAheadBaseM = 0.40f,
        LookAheadAtCapM = 3.60f,

        HeightBaseM = 0.10f,
        HeightAtCapM = 0.70f,

        FovInRate = 6.0f,
        FovOutRate = 2.2f,
        DistanceInRate = 4.5f,
        DistanceOutRate = 1.8f,
        LookAheadInRate = 5.0f,
        LookAheadOutRate = 1.6f,
        HeightInRate = 4.0f,
        HeightOutRate = 1.5f,

        CollisionRadiusM = 0.30f,
        CollisionEaseOutRate = 5.0f,

        SpeedCurveExponent = 1.0f,
    };

    /// <summary>What the camera reads. Lab-only; no guard, no session to protect.</summary>
    public static BikeCameraTuning Current { get; set; } = Default;
}
