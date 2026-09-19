using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Dev;

/// <summary>
/// <b>BIKE-4A — driving the rider's body from the bike layer.</b> One method, called once per
/// physics tick, that turns <see cref="BikeLayer"/>'s state into an
/// <see cref="AvatarVisual.SetRide"/> call. It is the lab's half of the seam BIKE-3B §1 draws:
/// <c>AvatarVisual</c> owns the limbs and this owns the wiring, and an external pose writer reaching
/// into the skeleton was rejected outright there because limb transforms have exactly one writer.
///
/// <para><b>Nothing here writes velocity, position or tuning.</b> Every value below is read; the one
/// thing it calls is a setter on the cosmetic layer. That is deliberate and it is the packet's
/// acceptance criterion 5: the ride channel adds no second writer of the body's motion. The layer's
/// own <c>PostStep</c> remains the sole writer of <c>_avatar.Velocity</c> in this lab, unchanged.</para>
///
/// <para><b>Why the mode is resolved HERE and not in <c>AvatarVisual</c>.</b> The two facts the
/// PEDAL/COAST trigger needs — is forward input held, and what is the wish — are the LAYER's, and
/// <c>AvatarVisual</c> deliberately knows nothing about a bike (its own <c>SetVerb</c> doc: "nothing
/// in this file computes a verb for itself"). So the arithmetic lives in <see cref="RidePose"/>,
/// which is pure and unit-tested, the STATE it needs (the mode and its hysteresis clock) lives here
/// next to the layer it reads, and what crosses the seam is a resolved mode.</para>
///
/// <para><b>Clock:</b> physics, immediately after <c>BikeLayer.PostStep</c>, so every value read is
/// this tick's settled one — the same reason <c>BikeHandlingPhysics</c> runs last. The pose it sets
/// is consumed by the next <c>AvatarVisual.Animate</c>, one tick later; a single tick of
/// presentation lag on a channel that decides nothing is the same lag the greybox's own
/// <c>Track</c> has.</para>
/// </summary>
public partial class MovementPlayground
{
    /// <summary>Which side of pedal ⇄ coast the rider is on, and how long the other side has been
    /// wanted. Held here rather than in the visual for the class doc's reason; stepped only by
    /// <see cref="RidePose.StepMode"/>.</summary>
    private RideMode _rideMode = RideMode.Coast;

    /// <inheritdoc cref="_rideMode"/>
    private float _rideModeHoldSec;

    /// <summary>The heading the body was travelling on last tick, radians, for the steer rate. Only
    /// meaningful while <see cref="_rideHeadingValid"/>; a body at rest has no heading, and reading
    /// the noise out of a zero-length velocity would jitter the bars.</summary>
    private float _rideHeadingRad;

    /// <inheritdoc cref="_rideHeadingRad"/>
    private bool _rideHeadingValid;

    /// <summary>The eased steer read, −1..+1. Low-passed for the reason <c>AvatarVisual</c>'s lean
    /// is: a heading differentiated frame by frame is noisy, and the bars are a pose rather than a
    /// measurement.</summary>
    private float _rideSteer01;

    /// <summary>Below this speed the heading is not read at all, m/s — the direction of a velocity
    /// this short is numerical noise. Shared with the ride pose's own stall threshold so a body that
    /// has stopped pedalling has also stopped steering.</summary>
    private const float SteerMinSpeedMps = RidePose.CrankStallMps;

    /// <summary>Radians per second of heading change that counts as full steer. 2.6 — a body
    /// turning at ~150°/s, which on the ride tuning's turn acceleration is a hard corner rather than
    /// a lane change. VALUE, stated: the readable range wants full lock at a corner a player thinks
    /// of as a corner, not at the layer's absolute maximum.</summary>
    private const float FullSteerRadPerSec = 2.6f;

    /// <summary>How fast the steer read follows the heading rate, per second. 8 — inside a tenth of
    /// a second, so the bars answer the stick rather than trailing it, but slower than the frame
    /// rate so a single noisy tick cannot snap them.</summary>
    private const float SteerEaseRate = 8f;

    /// <summary>
    /// One tick of the ride channel. A no-op with no bike in the lab, and a no-op that costs one
    /// float compare while the bike is stowed.
    /// </summary>
    private void BikeRideVisuals(float dt)
    {
        if (_bike is null)
            return;
        AvatarVisual visual = _avatar.Visual;
        // THE A/B SWITCH, and it is only ever true inside --bike-ride-capture. Weight 0 is
        // SetRide's exact no-op, so the frame this produces is the body the branch point produced.
        float blend = _rideChannelForcedOff ? 0f : _bike.Blend;
        Vector3 v = _avatar.Velocity;
        float speed = new Vector2(v.X, v.Z).Length();

        if (blend <= 0f)
        {
            // Off the bike entirely: hand the body back, exactly, and let the mode and the steer
            // rest where a fresh mount would want them. SetRide at weight 0 is the exact no-op, so
            // this is the same body the on-foot system had before the layer existed.
            _rideMode = RideMode.Coast;
            _rideModeHoldSec = 0f;
            _rideSteer01 = 0f;
            _rideHeadingValid = false;
            visual.SetRide(0f, RideMode.Coast);
            return;
        }

        // --- PEDAL or COAST (BIKE-3B §S1) --------------------------------------------------------
        //
        // "Forward" is the stick's component ALONG THE BODY'S OWN HEADING, not the world +Z and not
        // the raw stick length: a player holding the stick hard into a turn is still driving, and a
        // player holding it backwards to brake is not. MoveDir is already world-space (see
        // MoveIntent), so the dot is the whole conversion.
        Vector3 dir = _bike.LastMoveDir;
        Vector3 heading = speed > SteerMinSpeedMps
            ? new Vector3(v.X, 0f, v.Z).Normalized()
            : -_avatar.GlobalTransform.Basis.Z;
        float forward = Mathf.Max(0f, new Vector3(dir.X, 0f, dir.Z).Dot(heading));
        RideMode wants = RidePose.Wants(forward, speed, _bike.RideWishMps);
        (_rideMode, _rideModeHoldSec) =
            RidePose.StepMode(_rideMode, _rideModeHoldSec, wants, dt);

        // --- THE STEER, for the bars -------------------------------------------------------------
        //
        // Differentiated from the heading rather than plumbed from the input, because the heading is
        // what the FRONT WHEEL is doing and the stick is only a request: on ice, or mid-drift, the
        // bars should follow where the bike is actually going. Same argument AvatarVisual's lean
        // makes for differentiating acceleration instead of reading the sprint flag.
        float steerTarget = 0f;
        if (speed > SteerMinSpeedMps)
        {
            float yaw = Mathf.Atan2(v.X, v.Z);
            if (_rideHeadingValid && dt > 1e-5f)
            {
                float d = Mathf.Wrap(yaw - _rideHeadingRad, -Mathf.Pi, Mathf.Pi);
                steerTarget = Mathf.Clamp(d / dt / FullSteerRadPerSec, -1f, 1f);
            }
            _rideHeadingRad = yaw;
            _rideHeadingValid = true;
        }
        else
        {
            _rideHeadingValid = false;
        }
        _rideSteer01 = Mathf.Lerp(_rideSteer01, steerTarget, Mathf.Min(1f, dt * SteerEaseRate));

        // THE WEIGHT IS THE LAYER'S BLEND, not a second eased copy of it (3B §1) — so the tuning
        // blend and the body blend cannot disagree, and a mount abandoned mid-blend reverses the
        // body for free.
        //
        // THE LEAN IS WIRED (orchestrator, 2026-09-02, at wave-4 integration — this was BIKE-4A's
        // declared seam and BIKE-4B is the channel it waited for). The rider is handed the machine's
        // roll rather than deriving one, exactly as 4A required; `AvatarVisual` crossfades its own
        // lateral-acceleration bank out against ride weight so the body does not bank twice.
        //
        // IT IS THE CORNER LEAN, NOT THE DRAWN ROLL, AND THE DIFFERENCE IS DELIBERATE. `_rollDeg`
        // is `_leanDeg + _wobbleDeg`; the bike mesh is drawn at the roll, the rider is given the
        // lean. So the rider banks through corners with the machine, and the low-speed wobble
        // happens UNDER them — which is what makes it read as the machine being unsteady rather
        // than the whole world tilting. Hand the rider `_rollDeg` instead and the wobble becomes
        // invisible relative to the bike, visible only against the horizon.
        //
        // That is a FEEL call and it is Talon's, not this seam's: it is one symbol on the next line
        // (`_leanDeg` ⇄ `_rollDeg`), it changes nothing else in either half, and it is written up in
        // the session plan for him to judge with his eyes rather than from this comment.
        visual.SetRide(blend, _rideMode, _rideSteer01, Mathf.DegToRad(_leanDeg));
    }
}
