using System;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The summonable bike, as a layer the playground puts around the shipped body</b> (BIKE-0,
/// 2026-09-01). It touches no shipped file: it is an <see cref="IIntentSource"/> decorator on the
/// input side and a post-step velocity writer on the output side, which is exactly the shape the
/// slope prototype already uses. Everything a keyboard moves is still <c>AvatarMotor.Step</c>.
///
/// <para><b>How the bike changes the body: by rewriting the tuning, not the motor.</b> Mounting
/// walks the panel's live tuning toward <see cref="BikeRig.Ride"/> of itself over
/// <c>MountBlendSec</c>, through <c>MotorTuning.TryApply</c>, the single writer the sliders use;
/// dismounting walks it back. The motor never learns a bike exists — it runs a faster,
/// slipperier tuning for a while, and the layer adds the hops, the two bursts, the stumble, the
/// drift and the slope term from outside.</para>
///
/// <para><b>The grammar</b> (also printed at the bottom of the readout):
/// <list type="bullet">
/// <item><b>Q</b> summons or stows the bike. Always available; nothing to walk up to. On the
/// ground the body HOPS onto the bike as it springs open under it (<c>MountHopMps</c>), and hops
/// off it on a plain dismount.</item>
/// <item>Q <b>in the air</b> mounts with a burst — up and forward, once per airtime. The double
/// jump's slot, spent on the bike instead.</item>
/// <item>Q <b>within <c>LandDismountWindowSec</c> of touchdown</b>, either side, is a landing
/// dismount: the bike goes away and the body gets a second burst. The "before" side is PREDICTED
/// — a press in the air casts for the floor and arms the landing dismount only if touchdown is
/// inside the window — so the press never waits to find out what it was. Mount in the air,
/// dismount as you land, jump, mount — the chain the packet wants is those four presses.</item>
/// <item>Q <b>in the air, higher than that</b>, is the <b>kick-off</b> (BIKE-1b): the double
/// jump OFF the bike. The body pushes off the frame (<c>KickOffUpMps</c>) and the bike folds
/// away under it. SPACE in the air while riding is the shipped double jump ON the bike, scaled by
/// <c>RideAirJumpMul</c>; a bike jump can therefore go SPACE, SPACE, Q — three rises — and a
/// foot jump can go SPACE, SPACE, Q (mount burst), Q (kick-off). Whether that is too much is a
/// preset's question, not this file's.</item>
/// <item>Leaving an <b>uphill lip</b> while mounted launches: the ride speed's along-slope
/// component becomes rise (<c>RampLaunchGain</c>). The shipped motor keeps velocity horizontal
/// on a slope, so without this a kicker is a table.</item>
/// <item>Q <b>on the ground, rolling</b> above the foot cap is the one deliberate speed cost:
/// the body is clamped under the cap and stumbles for <c>StumbleSec</c> — less steering, no
/// sprint, and the jump still there. No lockout. <b>N</b> switches the stumble off and on so it
/// can be felt both ways on the same run-out.</item>
/// <item><b>Right mouse</b> on foot is the crouch-slide (it presses the shipped crouch grammar's
/// button for you), and SPACE out of the slide is a taller jump. Right mouse on the bike is the
/// <b>drift</b>: the nose swings hard toward the stick, speed sheds gently and never below a
/// floor while it is held.</item>
/// <item><b>H</b> toggles the hold-to-sprint ramp: SHIFT does nothing, speed is how long forward
/// has been held.</item>
/// </list></para>
///
/// <para>The bike's own numbers are <see cref="BikeTuning.Current"/>. The ride tuning shows on
/// the panel while mounted, honestly, because it IS <c>MotorTuning.Current</c> for that while.</para>
/// </summary>
public sealed class BikeLayer : IIntentSource
{
    public const Key MountKey = Key.Q;
    public const Key RampKey = Key.H;
    public const Key StumbleKey = Key.N;
    public const MouseButton SwingButton = MouseButton.Left;

    private readonly IIntentSource _inner;
    private readonly SandboxAvatar _avatar;

    /// <summary>Where refusals and state changes are announced (the panel's notice strip).</summary>
    public Action<string>? Notice { get; set; }

    /// <summary><b>Scripted mode</b>: no keyboard is polled; the summon press arrives through
    /// <see cref="PressMount"/> and the toggles through their setters. This is how
    /// <c>--bike-selftest</c> drives the layer headlessly through the SAME code path a keyboard
    /// does — only the edge's source differs.</summary>
    public bool Scripted { get; init; }

    private bool _pendingPress;

    /// <summary>One press of the summon key, from a script.</summary>
    public void PressMount() => _pendingPress = true;

    private bool _pendingSwingPress;

    /// <summary>One press of the swing button, from a script.</summary>
    public void PressSwing() => _pendingSwingPress = true;

    /// <summary>The stumble switch, from a script (the N key does the same thing).</summary>
    public static void SetStumbleEnabled(bool on)
        => BikeTuning.Current = BikeTuning.Current with { StumbleEnabled = on };

    /// <summary><b>A whole bike tuning, live</b> (an ALT-row preset, or a script). Takes effect on
    /// the next tick even mid-ride: the ride tuning is re-derived from the captured foot tuning
    /// with the new numbers, so a preset pressed while riding changes the ride you are on.</summary>
    public void SetTuning(in BikeTuning t)
    {
        BikeTuning.Current = t;
        _reapply = true;
    }

    /// <summary><b>The foot tuning was replaced under the bike</b> — a number-row preset pressed
    /// while riding, or a slider moved. The layer re-captures it as the foot tuning so the ride is
    /// derived from what the banner now names, and the dismount restores to it rather than to the
    /// preset that was loaded when the bike came out.</summary>
    public void ReplaceFoot(in MotorTuning foot)
    {
        if (!_footCaptured || _blend <= 0f)
            return;
        _foot = foot;
        _reapply = true;
    }

    /// <summary>The foot tuning the ride is derived from, while there is a ride; null on foot.
    /// What the preset banner compares against while mounted.</summary>
    public MotorTuning? FootTuning => _footCaptured && _blend > 0f ? _foot : null;

    private bool _reapply;

    // --- state -------------------------------------------------------------------------------
    private MotorTuning _foot;
    private MotorTuning? _overlayRestore;
    private float _blend;            // 0 = foot tuning, 1 = ride tuning; walks toward _blendTarget
    private float _blendTarget;
    private bool _mountWasDown;
    private bool _rampWasDown;
    private bool _stumbleKeyWasDown;
    private bool _prevGrounded = true;
    private bool _airMountSpent;
    private float _dismountArmedSec;   // counting down after an airborne dismount press
    private float _sinceTouchdownSec = 99f;
    private float _sinceToggleSec = 99f;
    private float _stumbleSec;
    private float _heldSec;
    private bool _pendingAirMountBurst;
    private bool _pendingLandBurst;
    private bool _pendingKickOff;
    private float _lastFloorUphillSin;    // the lip, remembered from the last grounded tick
    private bool _burstRising;            // a layer write put the body on the rise; hold its arc
    private bool _swingWasDown;
    private float _swingSec;              // counting down through one swing; 0 = not swinging
    private bool _swingAirSpent;          // one lunge per airtime
    private bool _swingHitCastDone;
    private bool _pendingLunge;
    private bool _pendingPush;
    private bool _pendingSlingshot;

    /// <summary>0 when the bike is on the back, rising to 1 through a swing; what the greybox
    /// sweeps on. Exactly 0 when not swinging.</summary>
    public float SwingFraction => _swingSec > 0f && BikeTuning.Current.SwingSec > 0f
        ? 1f - _swingSec / BikeTuning.Current.SwingSec : 0f;
    public bool Swinging => _swingSec > 0f;

    /// <summary>How many swings have connected with something, and the last thing hit.</summary>
    public int SwingHits { get; private set; }
    public string LastSwingHit { get; private set; } = "-";
    private Vector3 _preStepVelocity;     // what the body wanted before the wall had its say
    private float _pendingHopMps;
    private bool _driftHeld;
    private MoveIntent _lastIntent;

    public bool Mounted { get; private set; }
    public bool HoldRampEnabled { get; set; }
    public bool Stumbling => _stumbleSec > 0f;
    public float StumbleRemainingSec => _stumbleSec;
    public bool AirMountAvailable => !Mounted && !_airMountSpent;
    public bool DismountArmed => _dismountArmedSec > 0f;
    public bool Drifting => Mounted && _driftHeld && _avatar.IsOnFloor();
    public float HoldRampFraction => HoldRampEnabled
        ? BikeRig.HoldRamp(_heldSec, BikeTuning.Current) : 1f;
    public bool LandingWindowOpen => _sinceTouchdownSec <= BikeTuning.Current.LandDismountWindowSec;

    /// <summary>0 on foot, 1 fully riding, between while the tuning is blending.</summary>
    public float Blend => _blend;

    /// <summary>Seconds since the last mount or dismount — what the greybox animates on.</summary>
    public float SinceToggleSec => _sinceToggleSec;

    /// <summary>Seconds since the last touchdown — what the landing squash animates on.</summary>
    public float SinceTouchdownSec => _sinceTouchdownSec;

    /// <summary>The last velocity write this layer made, for the readout.</summary>
    public string LastEvent { get; private set; } = "-";

    // --- READ-ONLY, ADDED BY BIKE-4A -------------------------------------------------------------
    //
    // Two getters and nothing else. BIKE-4A owns the RIDER's body and this file owns the MACHINE;
    // the packet's seam allows it a getter here and explicitly nothing more, so neither of these
    // stores, decides, writes or memoises anything. Both are already true of this layer's state the
    // instant they are asked; they exist because the ride POSE has to tell "the player is driving"
    // from "momentum is doing the work", and both halves of that sentence live in here.

    /// <summary>The world-space movement direction the wrapped source asked for on the last tick —
    /// <c>MoveIntent.MoveDir</c>, length 0..1. <b>Read-only.</b> The ride pose reads it for the
    /// "forward input held" half of BIKE-3B's PEDAL trigger; nothing about the intent changes by
    /// being looked at.</summary>
    public Vector3 LastMoveDir => _lastIntent.MoveDir;

    /// <summary>
    /// <b>The fastest the motor is being asked to go right now, m/s</b> — the other half of 3B's
    /// PEDAL trigger ("the body at or below its current wish").
    ///
    /// <para>Computed from the same call <c>StepBlend</c> writes through
    /// (<c>BikeRig.RideWithBonus</c> at the live blend and slope bonus) times the sprint multiplier
    /// the motor applies on top, so it is the wish the motor actually got rather than a second
    /// opinion about it. At blend 0 it is simply the foot's own sprint wish; before the first mount
    /// captures a foot tuning it is 0, which <c>RidePose.Wants</c> reads as "no wish stated" and
    /// answers COAST to.</para>
    /// </summary>
    public float RideWishMps
    {
        get
        {
            if (!_footCaptured)
                return 0f;
            MotorTuning wish = BikeRig.RideWithBonus(_foot, BikeTuning.Current, _blend, _slopeBonus);
            return wish.MoveSpeed * wish.SprintMultiplier;
        }
    }

    public BikeLayer(IIntentSource inner, SandboxAvatar avatar)
    {
        _inner = inner;
        _avatar = avatar;
    }

    // IsHumanInput is deliberately NOT declared: the repo's guard (BotAchievementGateTests) lets
    // only the real keyboard source claim a person, and a lab body earns no achievements anyway.

    // --- the input side ----------------------------------------------------------------------

    public MoveIntent NextIntent(double delta)
    {
        float dt = (float)delta;
        MoveIntent intent = _inner.NextIntent(delta);
        BikeTuning b = BikeTuning.Current;
        _sinceToggleSec += dt;

        // A one-tick tuning overlay (the slide jump) is restored on the very next tick, before
        // anything else can read it.
        if (_overlayRestore is { } restore)
        {
            Apply(restore, "slide-jump overlay restore");
            _overlayRestore = null;
        }

        bool grounded = _avatar.IsOnFloor();

        // --- the keys this layer owns, polled here so the lab's key handler need not know them.
        bool mountEdge;
        if (Scripted)
        {
            mountEdge = _pendingPress;
            _pendingPress = false;
        }
        else
        {
            bool mountDown = Input.IsPhysicalKeyPressed(MountKey);
            mountEdge = mountDown && !_mountWasDown;
            _mountWasDown = mountDown;

            bool rampDown = Input.IsPhysicalKeyPressed(RampKey);
            if (rampDown && !_rampWasDown)
            {
                HoldRampEnabled = !HoldRampEnabled;
                Notice?.Invoke(HoldRampEnabled
                    ? "hold ramp ON - SHIFT is inert, speed is how long forward is held"
                    : "hold ramp OFF - SHIFT sprints, as shipped");
            }
            _rampWasDown = rampDown;

            bool stumbleDown = Input.IsPhysicalKeyPressed(StumbleKey);
            if (stumbleDown && !_stumbleKeyWasDown)
            {
                SetStumbleEnabled(!b.StumbleEnabled);
                b = BikeTuning.Current;
                Notice?.Invoke(b.StumbleEnabled
                    ? "stumble ON - a rolling dismount over the foot cap costs speed"
                    : "stumble OFF - a rolling dismount just hops off, no cost");
            }
            _stumbleKeyWasDown = stumbleDown;
        }

        bool swingEdge;
        if (Scripted)
        {
            swingEdge = _pendingSwingPress;
            _pendingSwingPress = false;
        }
        else
        {
            bool swingDown = Input.IsMouseButtonPressed(SwingButton);
            swingEdge = swingDown && !_swingWasDown;
            _swingWasDown = swingDown;
        }
        if (swingEdge)
            OnSwingPress(grounded, b);

        if (mountEdge)
            OnMountPress(grounded);

        if (_swingSec > 0f)
        {
            _swingSec = Mathf.Max(0f, _swingSec - dt);
            if (_swingSec <= 0f)
                LastEvent = "SWING done - bike back on the back";
        }

        // The mount/dismount blend and the slope bonus, AFTER the press so a mount starts blending
        // on the tick it is pressed. Written to the motor only when something moved.
        StepSlopeBonus(dt, grounded, b);
        StepBlend(dt, b);

        // --- right mouse: the shipped aim pose is not what this lab wants from that button.
        bool rmb = intent.AimRaise;
        intent = intent with { AimRaise = false };
        _driftHeld = Mounted && rmb;
        if (!Mounted && rmb && grounded)
        {
            // Press the crouch grammar's button for the player: held on the ground long enough,
            // the motor enters SLIDE at speed and TUCK at rest, and standing back up is its own.
            intent = intent with { JumpHeld = true };
        }

        // --- the slide jump: a SPACE edge taken while the body is sliding gets a taller arc, by
        // overlaying JumpVelocity for exactly this one tick.
        if (!Mounted && intent.Jump && _avatar.VerbNow == MoveVerb.Slide)
        {
            MotorTuning current = MotorTuning.Current;
            if (Apply(BikeRig.SlideJump(current, b), "slide jump"))
            {
                _overlayRestore = current;
                LastEvent = $"SLIDE JUMP x{b.SlideJumpMul:F2}";
            }
        }

        // --- the hold-to-sprint ramp.
        bool moving = intent.MoveDir.LengthSquared() > 0.25f;
        _heldSec = moving ? _heldSec + dt : 0f;
        if (HoldRampEnabled && moving)
        {
            float frac = BikeRig.HoldRamp(_heldSec, b);
            intent = intent with { Sprint = true, MoveDir = intent.MoveDir * frac };
        }

        // --- a burst's arc: hold the jump for the body while a layer write is still rising, so
        // the motor does not apply its release gravity to a rise the player never pressed for.
        if (_burstRising)
        {
            if (grounded || _avatar.Velocity.Y <= 0f)
                _burstRising = false;
            else if (b.BurstFullArc)
                intent = intent with { JumpHeld = true };
        }

        // --- the stumble: steering scaled, no sprint, everything else still the player's.
        if (_stumbleSec > 0f)
        {
            _stumbleSec = Mathf.Max(0f, _stumbleSec - dt);
            intent = intent with
            {
                Sprint = false,
                MoveDir = intent.MoveDir * b.StumbleSteerFraction,
            };
        }

        _lastIntent = intent;
        _preStepVelocity = _avatar.Velocity;
        return intent;
    }

    private float _slopeBonus;
    private float _writtenBonus;
    /// <summary>True once a mount has captured the foot tuning. Until then the layer must never
    /// write to the motor: a write of the never-captured default struct is every row at its knob
    /// MINIMUM after validation — measured 2026-09-01, the body walked at 1.0 m/s.</summary>
    private bool _footCaptured;

    /// <summary>The slope's contribution to the ride wish right now, m/s.</summary>
    public float SlopeBonusMps => _slopeBonus;

    /// <summary>The sine of the floor's pitch along the heading, for the readout.</summary>
    public float DownhillSin { get; private set; }

    private void StepSlopeBonus(float dt, bool grounded, in BikeTuning b)
    {
        float downhill = 0f;
        if (Mounted && grounded)
        {
            Vector3 n = _avatar.GetFloorNormal();
            Vector3 h = BikeRig.Heading(_avatar.Velocity, Facing());
            if (n.IsFinite() && n.LengthSquared() > 0.5f && h.LengthSquared() > 0.5f)
                downhill = n.Dot(h);                // the normal leans forward going downhill
        }
        DownhillSin = downhill;
        if (Mounted && grounded)
            _lastFloorUphillSin = -downhill;
        // Off the bike, or in the air, the bonus only bleeds (downhill 0 => -RideDeceleration).
        _slopeBonus = BikeRig.SlopeBonusStep(_slopeBonus, Mounted && grounded ? downhill : 0f, dt, b);
    }

    private void StepBlend(float dt, in BikeTuning b)
    {
        if (!_footCaptured)
            return;
        // EXACT comparisons, deliberately. An approximate one left the blend parked a few
        // millionths above zero after a dismount, so the exact-restore branch below never ran and
        // the motor kept a lerp of the foot tuning one bit off every blended row (measured
        // 2026-09-01: MoveSpeed 3.8 -> 3.8000002). MoveToward lands exactly on its target.
        bool blendMoves = _blend != _blendTarget;
        if (blendMoves)
        {
            float sec = _blendTarget > _blend ? b.MountBlendSec : b.DismountBlendSec;
            float rate = sec <= 0f ? 1f : dt / sec;
            _blend = Mathf.MoveToward(_blend, _blendTarget, rate);
        }
        bool bonusMoves = _slopeBonus != _writtenBonus;
        if (!blendMoves && !bonusMoves && !_reapply)
            return;
        _reapply = false;
        if (_blend <= 0f)
        {
            _blend = 0f;
            _writtenBonus = 0f;
            _slopeBonus = 0f;
            Apply(_foot, "dismount restore");      // exact, not a lerp of itself
            return;
        }
        _writtenBonus = _slopeBonus;
        Apply(BikeRig.RideWithBonus(_foot, b, _blend, _slopeBonus), "ride blend");
    }

    private void OnMountPress(bool grounded)
    {
        BikeTuning b = BikeTuning.Current;
        if (!Mounted)
        {
            // Q mid-swing in the air: the swing continues into the mount - the slingshot.
            bool slingshot = _swingSec > 0f && !grounded && !_airMountSpent;
            _swingSec = 0f;
            Mount();
            if (!grounded)
            {
                if (!_airMountSpent)
                {
                    _airMountSpent = true;
                    _pendingAirMountBurst = true;
                    _pendingSlingshot = slingshot;
                }
            }
            else
            {
                _pendingHopMps = b.MountHopMps;       // the hop onto the bike
            }
            return;
        }

        // Mounted: which dismount is this?
        if (grounded && _sinceTouchdownSec <= b.LandDismountWindowSec)
        {
            // Just landed: the landing dismount, with its burst.
            _pendingLandBurst = true;
            Dismount(stumble: false);
        }
        else if (!grounded)
        {
            // In the air: which jump-off is this? Cast for the floor. Inside the window it is a
            // landing dismount, armed, bursting on touchdown; further up it is the kick-off, now.
            float toFloor = SecondsToFloor(b);
            if (toFloor <= b.LandDismountWindowSec)
            {
                _dismountArmedSec = b.LandDismountWindowSec;
                LastEvent = $"LANDING DISMOUNT ARMED ({toFloor:F2}s to floor)";
            }
            else
            {
                _pendingKickOff = true;
                Dismount(stumble: false);
                LastEvent = $"KICK-OFF ({toFloor:F2}s to floor, window {b.LandDismountWindowSec:F2})";
            }
        }
        else
        {
            // Rolling on the ground: the one deliberate speed cost, if it is switched on.
            Dismount(stumble: b.StumbleEnabled);
        }
    }

    /// <summary>
    /// <b>The swing</b> (BIKE-1c). On foot only — Talon: the attack is not on the bike. One press
    /// starts one swing; a press mid-swing is ignored (no combo yet). In the air it is also the
    /// lunge, once per airtime; on the ground, a step into it.
    /// </summary>
    private void OnSwingPress(bool grounded, in BikeTuning b)
    {
        if (Mounted || _swingSec > 0f || b.SwingSec <= 0f)
            return;
        _swingSec = b.SwingSec;
        _swingHitCastDone = false;
        if (!grounded)
        {
            if (!_swingAirSpent)
            {
                _swingAirSpent = true;
                _pendingLunge = true;
            }
            LastEvent = _pendingLunge ? "SWING in the air - LUNGE" : "SWING in the air (lunge spent)";
        }
        else
        {
            _pendingPush = true;
            LastEvent = "SWING on the ground";
        }
        Notice?.Invoke("bike SWUNG - it comes off the back, round, and back on");
    }

    /// <summary>The swing's reach, cast once at mid-swing along the heading. Anything the ray
    /// meets is "hit" — the lab has dummies for this and nothing takes damage.</summary>
    private void SwingHitCast(in BikeTuning b)
    {
        Vector3 heading = BikeRig.Heading(_avatar.Velocity, Facing());
        if (heading.LengthSquared() < 0.5f)
            return;
        Vector3 from = _avatar.GlobalPosition + Vector3.Up * 0.9f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + heading * b.SwingReachM);
        query.Exclude = new Godot.Collections.Array<Rid> { _avatar.GetRid() };
        query.CollisionMask = _avatar.CollisionMask;
        var hit = _avatar.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (Scripted)
            GD.Print($"[bike] swing cast from {from} along {heading} x {b.SwingReachM:F2} (vel {_avatar.Velocity}, "
                   + $"pose yaw {_avatar.Visual?.BodyYaw ?? 0f:F2}, mask {_avatar.CollisionMask}): {(hit.Count == 0 ? "nothing" : hit["collider"].As<Node>()?.Name)}");
        if (hit.Count == 0)
            return;
        string name = hit["collider"].As<Node>()?.Name ?? "?";
        SwingHits++;
        LastSwingHit = name;
        LastEvent = $"SWING HIT {name} at {((Vector3)hit["position"] - from).Length():F2} m";
    }

    /// <summary>Seconds until the body's feet reach whatever is below them, at the current fall
    /// under the live gravity; infinity if nothing is within the distance the window could cover.</summary>
    private float SecondsToFloor(in BikeTuning b)
    {
        MotorTuning t = MotorTuning.Current;
        float vy = _avatar.Velocity.Y;
        float g = t.Gravity * (vy < 0f ? Mathf.Max(t.FallGravityMultiplier, 1f) : 1f);
        // The furthest a body could fall inside the window, plus a margin for the cast's start.
        float w = b.LandDismountWindowSec;
        float reach = Mathf.Max(0f, -vy) * w + 0.5f * g * w * w + 0.3f;
        Vector3 from = _avatar.GlobalPosition + Vector3.Up * 0.05f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * (reach + 0.05f));
        query.Exclude = new Godot.Collections.Array<Rid> { _avatar.GetRid() };
        query.CollisionMask = _avatar.CollisionMask;
        var hit = _avatar.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return float.PositiveInfinity;
        float height = Mathf.Max(0f, from.Y - 0.05f - ((Vector3)hit["position"]).Y);
        return BikeRig.TimeToFloor(vy, height, g);
    }

    private void Mount()
    {
        // Capture the foot tuning only when starting from the foot tuning; a re-mount mid-blend
        // keeps the capture it already has, or the "foot" would drift toward the ride each time.
        if (_blend <= 0f)
        {
            _foot = MotorTuning.Current;
            _footCaptured = true;
        }
        Mounted = true;
        _blendTarget = 1f;
        _sinceToggleSec = 0f;
        LastEvent = "MOUNT";
        Notice?.Invoke("bike OUT - ride tuning blending in (the panel shows it)");
    }

    private void Dismount(bool stumble)
    {
        Mounted = false;
        _driftHeld = false;
        _dismountArmedSec = 0f;
        _blendTarget = 0f;
        _sinceToggleSec = 0f;
        if (stumble)
        {
            Vector3 clamped = BikeRig.StumbleClamp(_avatar.Velocity, BikeRig.FootCapMps(_foot),
                BikeTuning.Current, out bool did);
            if (did)
            {
                _avatar.Velocity = clamped;
                _stumbleSec = BikeTuning.Current.StumbleSec;
                LastEvent = $"DISMOUNT + STUMBLE ({BikeTuning.Current.StumbleSec:F2}s)";
                Notice?.Invoke("bike STOWED rolling - clamped under the foot cap, stumbling");
                return;
            }
        }
        if (_avatar.IsOnFloor() && !_pendingLandBurst)
            _pendingHopMps = BikeTuning.Current.DismountHopMps;   // the hop off
        LastEvent = "DISMOUNT";
        Notice?.Invoke("bike STOWED");
    }

    /// <summary>
    /// <b>Where the body is facing</b>, as a horizontal unit vector — read from the body's own
    /// yaw, which IS written every tick: <c>AvatarMotor.Step</c> sets
    /// <c>body.Rotation = (0, yaw, 0)</c> right before <c>MoveAndSlide</c> (AvatarMotor.cs, the
    /// <c>ResolveYaw</c> result), on every role including offline. Measured 2026-09-02 (BIKE-3A):
    /// a scripted mounted ride swept <c>GlobalRotation.Y</c> through a full 360-degree circle
    /// while this method's previous source held the greybox near world -Z the whole way — max
    /// |greybox yaw − avatar yaw| 176.57°, and the error tracked the heading's distance from -Z
    /// sector by sector (37° / 88° / 136° / 172° / 177° / 139° / 86° / 40° across the eight
    /// 45-degree sectors). That is a frozen frame, not a facing.
    ///
    /// <para>This method previously claimed "the CharacterBody3D never rotates in this game" and
    /// read <c>Visual.BodyYaw</c> instead. That claim was false, and <c>BodyYaw</c> is not a
    /// facing: it is the pose's <b>rig-local</b> torso twist — the swing's coil-and-whip plus the
    /// idle fidget, local on purpose (its own doc says the root's facing yaw would swamp it). The
    /// 2026-09-01 dummy-check measurement was real but proved only that BodyYaw beats a hardcoded
    /// -GlobalBasis.Z <i>while the twist happens to point at the target mid-swing</i>; it never
    /// tested a turned body.</para>
    /// </summary>
    public static Vector3 FacingOf(SandboxAvatar avatar)
    {
        float yaw = avatar.GlobalRotation.Y;
        return new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
    }

    private Vector3 Facing() => FacingOf(_avatar);

    private bool Apply(in MotorTuning t, string why)
    {
        if (MotorTuning.TryApply(t, out string refusal))
            return true;
        Notice?.Invoke($"bike: {why} refused - {refusal}");
        return false;
    }

    // --- the output side: after the avatar's own step ------------------------------------------

    /// <summary>Called by the playground AFTER the avatar has stepped this tick (its physics
    /// priority is above the avatar's). Applies whatever the input side queued, and the drift.</summary>
    public void PostStep(double delta)
    {
        float dt = (float)delta;
        BikeTuning b = BikeTuning.Current;
        bool grounded = _avatar.IsOnFloor();
        bool wasGrounded = _prevGrounded;
        bool touchdown = grounded && !wasGrounded;
        _prevGrounded = grounded;

        if (touchdown)
        {
            _sinceTouchdownSec = 0f;
            _airMountSpent = false;
            _swingAirSpent = false;
            if (Mounted && _dismountArmedSec > 0f)
            {
                _pendingLandBurst = true;
                Dismount(stumble: false);
            }
            else if (!Mounted && _stumbleSec <= 0f && b.StumbleEnabled && _blend > 0f)
            {
                // Landing on foot at bike speed (a mid-air dismount) meets the foot cap the same
                // way a rolling dismount does — while the ride tuning is still blending out, which
                // is how "this body was just on a bike" is known.
                Vector3 clamped = BikeRig.StumbleClamp(_avatar.Velocity, BikeRig.FootCapMps(_foot),
                    b, out bool did);
                if (did)
                {
                    _avatar.Velocity = clamped;
                    _stumbleSec = b.StumbleSec;
                    LastEvent = "LANDED OVER THE FOOT CAP - STUMBLE";
                }
            }
        }
        else if (grounded)
        {
            _sinceTouchdownSec += dt;
        }

        if (_dismountArmedSec > 0f && !grounded)
        {
            _dismountArmedSec -= dt;
            if (_dismountArmedSec <= 0f && Mounted)
            {
                // The cast said the floor was inside the window and it was not (a lip, a moving
                // edge). The press was still a jump-off, so it gets the kick — late, and the
                // readout says so, because late is the thing to tune out.
                _pendingKickOff = true;
                Dismount(stumble: false);
                LastEvent = "KICK-OFF (LATE - the armed landing never came)";
            }
        }

        Vector3 heading = BikeRig.Heading(_avatar.Velocity, Facing());

        // Leaving an uphill lip while mounted: speed becomes rise. Max, so a jump off the lip keeps
        // its own launch when that is taller.
        if (Mounted && !grounded && wasGrounded && _lastFloorUphillSin > 0.02f)
        {
            Vector3 beforeLaunch = _avatar.Velocity;
            _avatar.Velocity = BikeRig.RampLaunch(beforeLaunch, _lastFloorUphillSin, b);
            if (_avatar.Velocity.Y > beforeLaunch.Y + 0.05f)
            {
                _burstRising = true;
                LastEvent = $"RAMP LAUNCH vY {beforeLaunch.Y:F1} -> {_avatar.Velocity.Y:F1} (lip sin {_lastFloorUphillSin:F2})";
            }
        }
        if (!grounded)
            _lastFloorUphillSin = 0f;

        if (_pendingKickOff)
        {
            _pendingKickOff = false;
            _avatar.Velocity = BikeRig.KickOffBurst(_avatar.Velocity, heading, b);
            _burstRising = true;
        }

        if (_pendingAirMountBurst)
        {
            _pendingAirMountBurst = false;
            if (_pendingSlingshot)
            {
                _pendingSlingshot = false;
                _avatar.Velocity = BikeRig.Slingshot(_avatar.Velocity, heading, b);
                LastEvent = $"SLINGSHOT: swing -> mount, +{b.AirMountUpMps:F1} up +{b.AirMountForwardMps + b.SlingshotForwardMps:F1} fwd";
            }
            else
            {
                _avatar.Velocity = BikeRig.AirMountBurst(_avatar.Velocity, heading, b);
                LastEvent = $"AIR MOUNT BURST +{b.AirMountUpMps:F1} up +{b.AirMountForwardMps:F1} fwd";
            }
            _burstRising = true;
        }

        if (_pendingLunge)
        {
            _pendingLunge = false;
            _avatar.Velocity = BikeRig.SwingLunge(_avatar.Velocity, heading, b);
            _burstRising = true;
            LastEvent = $"SWING LUNGE +{b.SwingLungeForwardMps:F1} fwd, vY >= {b.SwingLungeUpMps:F1}";
        }
        if (_pendingPush)
        {
            _pendingPush = false;
            _avatar.Velocity = BikeRig.SwingPush(_avatar.Velocity, heading, b);
        }
        if (_swingSec > 0f && !_swingHitCastDone && SwingFraction >= 0.5f)
        {
            _swingHitCastDone = true;
            SwingHitCast(b);
        }

        if (_pendingLandBurst)
        {
            _pendingLandBurst = false;
            _avatar.Velocity = BikeRig.LandDismountBurst(_avatar.Velocity, heading, b);
            _burstRising = true;
            LastEvent = $"LANDING DISMOUNT BURST {b.LandDismountUpMps:F1} up +{b.LandDismountForwardMps:F1} fwd";
        }

        if (_pendingHopMps > 0f)
        {
            _avatar.Velocity = BikeRig.Hop(_avatar.Velocity, _pendingHopMps);
            _burstRising = true;
            LastEvent = Mounted ? $"HOP ON ({_pendingHopMps:F1} m/s)" : $"HOP OFF ({_pendingHopMps:F1} m/s)";
            _pendingHopMps = 0f;
        }

        if (Mounted && grounded && b.RideStepUpM > 0f && _avatar.IsOnWall())
            TryStepUp(b);

        if (Drifting)
        {
            float floor = BikeRig.RideCapMps(_foot, b) * b.DriftSpeedFloorFraction;
            _avatar.Velocity = BikeRig.Drift(_avatar.Velocity, _lastIntent.MoveDir, dt, floor, b);
        }
    }

    /// <summary>
    /// <b>Rolling over a curb</b> (BIKE-1b). The body is against a wall it was moving into; if
    /// the way ahead is clear at <c>RideStepUpM</c> and the floor just beyond is no higher than
    /// that, lift the body onto it and give back the horizontal speed the wall took. Two casts,
    /// both from the feet, both in the heading. Nothing else moves and nothing is remembered.
    /// </summary>
    private void TryStepUp(in BikeTuning b)
    {
        Vector3 wallN = _avatar.GetWallNormal();
        var flat = new Vector3(_preStepVelocity.X, 0f, _preStepVelocity.Z);
        if (flat.LengthSquared() < 1f)
            return;
        Vector3 dir = flat.Normalized();
        if (wallN.Dot(dir) > -0.5f)                 // the wall is not in front of us
            return;

        Vector3 feet = _avatar.GlobalPosition;
        var space = _avatar.GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { _avatar.GetRid() };
        float probeY = b.RideStepUpM + 0.15f;
        Vector3 from = feet + Vector3.Up * probeY;
        Vector3 ahead = from + dir * 0.7f;
        var over = PhysicsRayQueryParameters3D.Create(from, ahead);
        over.Exclude = exclude;
        over.CollisionMask = _avatar.CollisionMask;
        if (space.IntersectRay(over).Count > 0)      // a wall taller than the step
            return;
        var down = PhysicsRayQueryParameters3D.Create(ahead, ahead + Vector3.Down * (probeY + 0.1f));
        down.Exclude = exclude;
        down.CollisionMask = _avatar.CollisionMask;
        var hit = space.IntersectRay(down);
        if (hit.Count == 0)
            return;
        float rise = ((Vector3)hit["position"]).Y - feet.Y;
        if (rise <= 0.01f || rise > b.RideStepUpM)
            return;

        _avatar.GlobalPosition = feet + Vector3.Up * (rise + 0.02f) + dir * 0.05f;
        _avatar.Velocity = new Vector3(_preStepVelocity.X, Mathf.Max(_avatar.Velocity.Y, 0f), _preStepVelocity.Z);
        LastEvent = $"STEP UP {rise:F2} m (curb; limit {b.RideStepUpM:F2})";
    }

    /// <summary>The readout line.</summary>
    public string Line()
    {
        BikeTuning b = BikeTuning.Current;
        string mode = Mounted ? "MOUNTED" : "on foot";
        if (_blend > 0f && _blend < 1f)
            mode += $" (blend {_blend:F2})";
        string chain = Mounted
            ? (DismountArmed ? $"dismount ARMED {_dismountArmedSec:F2}s"
               : LandingWindowOpen ? "LANDING WINDOW open" : "Q dismounts")
            : (AirMountAvailable ? "Q mounts (air burst ready)" : "Q mounts (air burst spent)");
        if (Mounted && !_avatar.IsOnFloor() && !DismountArmed)
            chain = "Q = kick-off / landing dismount (cast decides)   SPACE = bike double jump";
        string stumble = Stumbling ? $"   STUMBLE {_stumbleSec:F2}s" : "";
        string drift = Drifting ? "   DRIFT" : "";
        string ramp = HoldRampEnabled ? $"   ramp {HoldRampFraction:F2}" : "";
        string slope = Mounted ? $"   slope +{_slopeBonus:F2} m/s" : "";
        string swing = Swinging ? $"   SWING {SwingFraction:F2}" : "";
        slope += swing + $"   hits {SwingHits} (last {LastSwingHit})";
        return $"BIKE       {mode}   {chain}{stumble}{drift}{ramp}{slope}   last: {LastEvent}"
             + $"   [Q bike  LMB swing (air = lunge; Q mid-swing = slingshot)  RMB slide/drift  H ramp  N stumble {(b.StumbleEnabled ? "on" : "OFF")}]";
    }
}
