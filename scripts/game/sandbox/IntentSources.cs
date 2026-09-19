using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Local keyboard intent, camera-relative. Reuses the foundation's input actions
/// (move_*/jump) plus the sandbox-only interact/throw actions. This is the only class
/// in the sandbox that reads Input for locomotion — swap it out and the avatar is
/// fully remote-drivable.
/// </summary>
public sealed class LocalInputIntentSource : IIntentSource
{
    /// <summary>The one true source of a person's input in this repo — see
    /// <see cref="IIntentSource.IsHumanInput"/> for what turns on it. Every other implementation
    /// takes the interface's false default, which is why a scripted bot cannot earn an
    /// achievement.</summary>
    public bool IsHumanInput => true;

    /// <summary>The walk-modifier action. Declared into the live <c>InputMap</c> at runtime rather
    /// than into <c>project.godot</c>, which the packet that added it could not write, and undone
    /// by deleting one method. It defers to the file: if <c>project.godot</c> ever declares
    /// <c>walk</c>, <see cref="EnsureWalkAction"/> touches nothing and a player's rebind survives.</summary>
    public const string WalkActionName = "walk";

    /// <summary><b>Left Ctrl</b>, and every obvious alternative was taken: Shift is <c>sprint</c>,
    /// Alt is the window manager's on Windows, and the letter keys are the verbs (E interact).
    /// Ctrl is also the conventional walk/crouch modifier, so it is the key a player tries
    /// first.</summary>
    private const Key WalkFallbackKey = Key.Ctrl;

    /// <summary>Gamepad: the left stick click, the conventional walk toggle's home. Held, not
    /// toggled — this whole path is level-triggered.</summary>
    private const JoyButton WalkFallbackButton = JoyButton.LeftStick;

    private readonly SandboxCamera _camera;

    /// <summary><b>The virtual analog stick.</b> A key is on or off; a stick reports a held
    /// deflection — and the difference is not cosmetic, because the movement step derives its target
    /// speed from the length of <see cref="MoveIntent.MoveDir"/>. A boolean key therefore asks for
    /// full speed on the frame it goes down no matter how gently the motor ramps, and the ramp is
    /// the only thing left doing the work. Climbing this instead means a tap is a nudge and a hold
    /// builds, on the keyboard, exactly as it does on a pad.
    ///
    /// <para><b>Client-side and harmless if it were forged.</b> It only ever scales a direction the
    /// server re-sanitizes into the unit disc (<c>AvatarMotor.SanitizeMoveDir</c>), so the most a
    /// doctored value can claim is "full stick", which is what holding a key already claims.</para></summary>
    private float _analog;

    /// <summary>BT-7: the double-tap-to-sprint detector. It owns no input of its own — it is fed
    /// the four <c>move_*</c> levels <see cref="NextIntent"/> already samples, and its output is
    /// ORed into the sprint intent. See <see cref="DoubleTapSprint"/> for the state machine and
    /// for why this costs the protocol nothing.</summary>
    private readonly DoubleTapSprint _doubleTapSprint = new();

    public LocalInputIntentSource(SandboxCamera camera)
    {
        _camera = camera;
        EnsureWalkAction();
    }

    /// <summary>Declare <see cref="WalkActionName"/> if nothing already has. Idempotent, never
    /// overwrites an existing binding.</summary>
    public static void EnsureWalkAction()
    {
        if (InputMap.HasAction(WalkActionName))
            return;
        InputMap.AddAction(WalkActionName);
        InputMap.ActionAddEvent(WalkActionName, new InputEventKey { PhysicalKeycode = WalkFallbackKey });
        InputMap.ActionAddEvent(WalkActionName, new InputEventJoypadButton { ButtonIndex = WalkFallbackButton });
    }

    public MoveIntent NextIntent(double delta)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // Release the virtual stick too, or the first frame after a menu closes asks for the
            // speed the player was travelling at when they opened it. Same reasoning drops the
            // double-tap latch (BT-7): a sprint that survives a pause menu is a player walking
            // into a lake while reading a settings screen.
            _analog = 0f;
            _doubleTapSprint.Reset();
            return MoveIntent.None; // mouse released = UI focus, same rule as Player.cs
        }

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        float deflection = Mathf.Min(1f, input.Length());
        bool walking = InputMap.HasAction(WalkActionName) && Input.IsActionPressed(WalkActionName);
        // BT-7: double-tapping a direction latches sprint, exactly as holding Shift does. Fed the
        // levels, not the edges — the detector finds its own edges (see DoubleTapSprint).
        bool doubleTapSprint = _doubleTapSprint.Update(
            Input.IsActionPressed("move_forward"), Input.IsActionPressed("move_back"),
            Input.IsActionPressed("move_left"), Input.IsActionPressed("move_right"), (float)delta);

        // THE GEARS, on the input side. A pad's deflection passes straight through, so the stick
        // maps onto the gears continuously; a keyboard's hard 1.0 is ramped through the virtual
        // stick above. Either way the walk modifier caps the request at the walk gear's fraction of
        // the jog speed — one number, LocomotionProfile.WalkFraction, so the gear the animation
        // layer names and the speed the motor produces cannot drift apart.
        float ceiling = walking ? LocomotionProfile.WalkFraction : 1f;
        float target = Mathf.Min(deflection, ceiling);
        //
        // ONE PATH FOR BOTH DEVICES, deliberately, rather than sniffing which one is connected. The
        // ramp is a rate, so a stick held at 0.4 reaches 0.4 in 0.09 s (imperceptible) while a key
        // slammed to 1.0 takes the full 0.22 s — the device-appropriate behaviour falls out of the
        // same line instead of out of a branch that could disagree with itself.
        _analog = LocomotionProfile.StepKeyAnalog(_analog, target, (float)delta);

        // Rotate flat input by the camera yaw so "forward" is where the player looks.
        Basis yaw = new(Vector3.Up, _camera.Yaw);
        Vector3 dir = yaw * new Vector3(input.X, 0, input.Y);
        if (dir.LengthSquared() > 1e-6f)
            dir = dir.Normalized() * _analog;
        else
            dir = Vector3.Zero;

        return new MoveIntent
        {
            MoveDir = dir,
            Jump = Input.IsActionJustPressed("jump"),
            // MOVE-3, variable jump height: the LEVEL of the same action, alongside the edge above
            // and never instead of it. The edge still feeds the buffer and still is the only thing
            // that fires a jump; this bit only withholds the extra gravity while the body is
            // rising. Both are true on the press tick, so a jump always launches at full height.
            JumpHeld = Input.IsActionPressed("jump"),
            Interact = Input.IsActionJustPressed("interact"),
            Throw = Input.IsActionJustPressed("throw"),
            // The walk modifier BEATS sprint, and that is a gear decision rather than a tie-break:
            // holding both would otherwise ask for 0.45 of a sprint (3.9 m/s), which is neither a
            // walk nor a sprint and is a speed no gear names.
            // BT-7 ORs the double-tap in HERE, inside the walk-modifier's veto rather than beside
            // it: the gear decision above is about what speed is being asked for, not about which
            // key asked, so a player holding Ctrl gets a walk whichever way they requested sprint.
            Sprint = (Input.IsActionPressed("sprint") || doubleTapSprint) && !walking,
            // Aim substrate (WP-L3): "aim" held (right mouse button — project.godot) drives the
            // shared raise-to-aim rig unconditionally; a verb that wants "only while equipped"
            // gates its OWN consumption of it (see MoveIntent.AimRaise's doc comment). Yaw/pitch
            // are sampled straight off the camera every tick, same continuous-value contract as
            // MoveDir — see AimYaw/AimPitch's own doc comments for why the body's travel-facing
            // Yaw isn't enough on its own.
            AimRaise = Input.IsActionPressed("aim"),
            // The primary "use held item" button (left mouse button — project.godot's
            // fire action): unconditional on bare input, same as Interact/Throw above —
            // what it does is resolved from what is in the hand (see MoveIntent.Fire's own doc
            // comment), and a registered verb is itself a request to the server.
            Fire = Input.IsActionJustPressed("fire"),
            AimYaw = _camera.Yaw,
            AimPitch = _camera.Pitch,
        };
    }
}

/// <summary>
/// Deterministic headless-bot brain: idle briefly, walk a fixed distance outward along the
/// spawn heading, then stand still so every peer's replicated view converges before sampling
/// ends. Ported verbatim from the retired Player.BotDirection so the replication test still
/// passes once the plain capsule avatar is gone. Emits movement only — no jump/interact/throw.
/// </summary>
public sealed class DeterministicWalkIntentSource : IIntentSource
{
    private const double StartDelaySec = 0.5;
    private const double SettleSec = 3.0;
    private const float WalkDistance = 15f;

    private readonly Node3D _self;
    private readonly double _durationSec;
    private readonly Vector3 _spawn;
    private readonly Vector3 _heading;
    private double _clock;

    public DeterministicWalkIntentSource(Node3D self, double durationSec)
    {
        _self = self;
        _durationSec = durationSec;
        _spawn = self.Position;
        var flat = new Vector3(_spawn.X, 0, _spawn.Z);
        _heading = flat.LengthSquared() > 0.01f ? flat.Normalized() : Vector3.Forward;
    }

    public MoveIntent NextIntent(double delta)
    {
        _clock += delta;
        if (_clock < StartDelaySec)
            return MoveIntent.None;

        double walkEnd = System.Math.Max(0, _durationSec - SettleSec);
        if (_clock >= walkEnd)
            return MoveIntent.None;

        Vector3 travelled = _self.Position - _spawn;
        travelled.Y = 0;
        if (travelled.Length() >= WalkDistance)
            return MoveIntent.None;

        return new MoveIntent { MoveDir = _heading };
    }
}

/// <summary>
/// Deterministic headless-bot brain (<c>--goto-script</c>): walks straight — sprinting if asked —
/// toward a fixed world-space XZ target, then stands still. Optionally fires a one-shot callback
/// once arrived, so a caller can act on arrival without threading its own knowledge into this
/// class. Reuses <see cref="ScriptedCarryIntentSource"/>'s arrive-radius idiom; a little more
/// generous here since a sprinting bot can overshoot more between ticks.
///
/// <paramref name="minDelaySec"/> delays the EARLIEST possible arrival, for a caller whose
/// <paramref name="onArrive"/> relocates the body: a landing can sit well inside THIS instance's
/// own ArriveRadius of the next target, and a server-side reposition shares a 1 s cooldown with
/// the reset-to-spawn RPC (abuse prevention), so an immediate second call would be silently
/// rejected by the server rather than by this class.
///
/// <para><b>ARRIVAL IS ONE-WAY, AND IT IS DECIDED ON THE SERVER'S BODY</b> (W6-3, 2026-08-30).
/// Two separate properties, and both are load-bearing:</para>
///
/// <para><i>One-way</i> — once the latch fires it never re-opens, because <paramref name="onArrive"/>
/// is allowed to teleport this body away from the target and a re-tested distance would then walk
/// it back toward a door it has already gone through, forever.</para>
///
/// <para><i>Decided on the server's body</i> — the latch is the single irreversible thing this
/// class does, so it may not be taken on a predicted position. Until W6-3 it was: the first tick
/// this client's PREDICTION fell inside <see cref="ArriveRadius"/>, the latch fired; the next
/// reconciliation then pulled the body back to where the server actually had it, and a bot that
/// can never walk again was left standing metres short. LEVER-2 measured it in 2 of 3 runs (1.8 m
/// short) and CARRY-1 measured the same client's prediction error peaking at 1.71 m on an idle
/// machine, both on 2026-08-30. <c>Run-ArriveLatchTest.ps1</c> is the regression.</para>
///
/// <para><b>WHY THIS CLASS LATCHES AND <see cref="ScriptedCarryIntentSource"/> DOES NOT.</b> The
/// question is what each one's arrival decides. Here, arrival is a decision about THE WORLD — its
/// callback may relocate the body — so it has to be one-way, and one-way is exactly what
/// makes a wrong decision unrecoverable. There, arrival only decides when to press a button the
/// SERVER then adjudicates; a refused press costs one press, and re-deriving the distance from a
/// live position every tick is both cheap and self-correcting. So the two differ because their
/// irreversibility differs, not because they disagree about the rule: neither may act on a
/// position the authority has not confirmed, and <c>--carry-grab-retry</c> is how the carry source
/// buys the same guarantee for the one decision of its own that is expensive to get wrong.</para>
/// </summary>
public sealed class ScriptedGotoIntentSource : IIntentSource
{
    private const float ArriveRadius = 1.5f;

    /// <summary>Below this horizontal ground speed (m/s), a bot that is still ASKING to move is
    /// making no headway. Set well under a walk (≈3.2 m/s) and far under a sprint (≈4.86 m/s) so
    /// only a genuine block trips it, never the deceleration of a normal approach or an uphill.</summary>
    private const float StallSpeedM = 0.5f;

    /// <summary>How long that near-zero speed must persist before it is reported. Long enough to
    /// ride out a reconciliation hitch or a momentary graze against a trunk.</summary>
    private const double StallGraceSec = 1.5;

    private readonly Node3D _self;
    private readonly Vector3 _target;
    private readonly bool _sprint;
    private readonly System.Action? _onArrive;
    private readonly double _minDelaySec;
    private bool _arrived;
    private double _clock;

    // --- stall reporting (diagnostic only; steers nothing) ---
    private Vector3 _lastGroundPos;
    private bool _haveLastPos;
    private double _stalledFor;
    private bool _reported;

    public ScriptedGotoIntentSource(Node3D self, Vector3 target, bool sprint, System.Action? onArrive = null,
        double minDelaySec = 0)
    {
        _self = self;
        _target = target;
        _sprint = sprint;
        _onArrive = onArrive;
        _minDelaySec = minDelaySec;
    }

    public MoveIntent NextIntent(double delta)
    {
        // A true latch, not a re-checked distance: _onArrive may TELEPORT _self away from
        // _target, which would otherwise make the distance check see itself as "far from
        // target" again on the very next tick and walk straight back — an infinite
        // there-and-back loop. Once arrived, stay arrived, forever, regardless of where _self
        // ends up moving afterward.
        if (_arrived)
            return MoveIntent.None;

        _clock += delta;
        Vector3 toTarget = _target - _self.Position;
        toTarget.Y = 0;
        float dist = toTarget.Length();
        if (dist <= ArriveRadius && _clock >= _minDelaySec)
        {
            // THE ARRIVAL IS DECIDED ON THE SERVER'S BODY, NOT ON THIS ONE (W6-3, 2026-08-30).
            // Everything above this line reads _self.Position, which on a networked owner is a
            // PREDICTION — and the latch below is the single irreversible act this class
            // performs, so it is the one thing that may not be taken on a guess. See
            // IServerConfirmedBody for the rule and the two nights of measurements behind it.
            if (!ArrivedOnTheAuthority())
            {
                // Predicted-arrived, not yet confirmed. Stand still and re-test next tick:
                // standing is REVERSIBLE, so it is allowed on a prediction, and it is also the
                // fastest way to earn the confirmation — with no further movement requested, the
                // server drains what it already has and its body converges on this one. If
                // reconciliation instead pulls this body back out of ArriveRadius (the stranding
                // case), the branch above simply fails next tick and the walk resumes, which is
                // the whole recovery: nothing was latched, so there is nothing to un-latch.
                //
                // Deliberately NOT ReportIfStalled: that reporter exists to name a bot that is
                // blocked while still ASKING to move (see its own doc comment), and this bot is
                // asking for nothing. Calling it here would print a stall warning for a body that
                // is waiting exactly as designed.
                return MoveIntent.None;
            }

            _arrived = true;
            _onArrive?.Invoke();
            return MoveIntent.None;
        }

        ReportIfStalled(delta);
        return new MoveIntent { MoveDir = toTarget.Normalized(), Sprint = _sprint };
    }

    /// <summary>Whether the AUTHORITY's copy of this body — not this process's prediction of it —
    /// is inside <see cref="ArriveRadius"/> of the target.
    ///
    /// <para><b>An unknown authority counts as agreement, and that is not a shrug.</b> A body
    /// that returns null is one this process holds no authority information about, and there are
    /// exactly two of those: a body this process is itself the authority for (offline, or the
    /// server's own simulation — <see cref="SandboxAvatar.ServerConfirmedPosition"/> answers with
    /// the live position for those, so they never reach this branch), and a networked owner in
    /// the handful of ticks before its first snapshot lands. Treating the second as "confirmed"
    /// costs nothing real: a bot cannot have walked to a target before it has received a single
    /// snapshot unless it spawned on top of it, in which case the server agrees anyway. Treating
    /// it as "unconfirmed" instead would wedge every non-networked fixture in this repo — an
    /// offline sandbox bot would stand still forever waiting for a server that does not
    /// exist.</para></summary>
    private bool ArrivedOnTheAuthority()
    {
        if (_self is not IServerConfirmedBody body || body.ServerConfirmedPosition is not Vector3 confirmed)
            return true;

        Vector3 toTarget = _target - confirmed;
        toTarget.Y = 0;
        return toTarget.Length() <= ArriveRadius;
    }

    /// <summary>Says so, once, when this bot has stopped making headway while still asking to
    /// move. Reports; never steers.
    ///
    /// <para><b>Why this exists.</b> This source walks a dead-straight heading and the avatar is a
    /// <c>CharacterBody3D</c> using <c>MoveAndSlide</c>. A straight line across 140 m of camp
    /// forest can run into a gap between two trunks narrower than the collision capsule, and when
    /// it does the result is not "slow" — it is EXACTLY zero displacement, at a fixed coordinate,
    /// silently, for the rest of the run, while the bot goes on requesting full sprint every tick.
    /// That is precisely how <c>Run-CampDoorTest</c>'s SprintBot failed after the four-packet
    /// merge, and the twelve seconds of silence cost more than the bug did: the JSONL showed a
    /// frozen position with nothing anywhere saying why, so the stall was chased through the
    /// avatar, the camera and the scene before anyone looked at the geometry. See
    /// docs/superpowers/handoffs/2026-08-09-phase0-baseline.md §4.2.</para>
    ///
    /// <para><b>Reporting only, deliberately.</b> Steering around the obstruction was tried and
    /// does not work for the case that matters: a true pinch between two trunks leaves no
    /// direction that frees the capsule, so a 70° sidestep produced zero motion too (measured, both
    /// sides, six attempts). Escaping one would take real pathfinding, these bots are a
    /// deterministic straight-line fixture rather than an entity with a navigation contract, and a
    /// bot that quietly detours would also stop measuring the straight-line traverse time it
    /// exists to measure. What the Behavior Bible does demand of anything that moves — §1/§3, never
    /// leave it stalled and silent, never let a failure mode be indistinguishable from "still
    /// walking" — is met by naming it, with its coordinate and its remaining distance.</para></summary>
    private void ReportIfStalled(double delta)
    {
        Vector3 here = _self.Position;
        here.Y = 0;
        float moved = _haveLastPos ? here.DistanceTo(_lastGroundPos) : float.MaxValue;
        _lastGroundPos = here;
        _haveLastPos = true;

        // Speed, not raw displacement, so the threshold means the same thing at any tick rate.
        bool making = delta <= 0 || moved / (float)delta >= StallSpeedM;
        if (making)
        {
            _stalledFor = 0;
            _reported = false;
            return;
        }

        _stalledFor += delta;
        if (_reported || _stalledFor < StallGraceSec)
            return;

        _reported = true;
        float remaining = new Vector2(_target.X - here.X, _target.Z - here.Z).Length();
        string blocker = _self is CharacterBody3D cb && cb.GetSlideCollisionCount() > 0
            ? (cb.GetSlideCollision(0).GetCollider() as Node)?.Name.ToString() ?? "?"
            : "nothing in this tick's slide list";
        GD.PushWarning($"[goto-script] STALLED at ({here.X:F2}, {here.Z:F2}) for " +
            $"{StallGraceSec:F1}s with {remaining:F1} m still to run, still requesting full " +
            $"movement — blocked against {blocker}. The route is obstructed; this bot will not " +
            "reach its target.");
    }
}

/// <summary>
/// Dummy-avatar brain: waddle to a random nearby point, pause, occasionally hop.
/// Exists so solo testing has bodies to bump into (and because three dummies
/// pottering about is funnier than an empty field). Deterministic-free comedy only —
/// no gameplay depends on it.
/// </summary>
public sealed class WanderIntentSource : IIntentSource
{
    private readonly Node3D _self;
    private readonly Vector3 _home;
    private readonly float _roamRadius;
    private readonly RandomNumberGenerator _rng = new();

    private Vector3 _target;
    private double _pauseRemaining;

    public WanderIntentSource(Node3D self, float roamRadius = 7f)
    {
        _self = self;
        _home = self.Position;
        _roamRadius = roamRadius;
        _target = self.Position;
        _pauseRemaining = _rng.RandfRange(0.5f, 2f);
    }

    public MoveIntent NextIntent(double delta)
    {
        if (_pauseRemaining > 0)
        {
            _pauseRemaining -= delta;
            if (_pauseRemaining <= 0)
                PickNewTarget();
            return MoveIntent.None;
        }

        Vector3 toTarget = _target - _self.GlobalPosition;
        toTarget.Y = 0;
        if (toTarget.Length() < 0.4f)
        {
            _pauseRemaining = _rng.RandfRange(0.8f, 3.2f);
            return MoveIntent.None;
        }

        return new MoveIntent
        {
            MoveDir = toTarget.Normalized() * 0.6f, // dummies waddle at 60% speed
            Jump = _rng.Randf() < 0.004f,           // the occasional joyful hop
        };
    }

    private void PickNewTarget()
    {
        float angle = _rng.RandfRange(0, Mathf.Tau);
        float dist = _rng.RandfRange(1.5f, _roamRadius);
        _target = _home + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
    }
}
