using Godot;

namespace Sail.Game.Failure;

/// <summary>
/// One player's failure state, as pure arithmetic. No Node, no RNG, no wall clock, no network —
/// exactly the shape <c>SputterSequence</c> and <c>ChillClock</c> already have, and for the same
/// reason: everything that decides whether a player is playing the game or watching it must be
/// provable in <c>dotnet test</c> without an engine, a GPU or a second peer.
///
/// <para><b>This class decides nothing about the world.</b> It does not know where the fires are,
/// who is standing over whom, or what time it is. It is handed those facts as
/// <see cref="AssistInputs"/> once per server tick by <c>IncapacitationService</c>, which owns
/// every query into the live scene. Keeping the split here is what makes the state machine
/// testable and the scene queries replaceable (the fire probe in particular is a delegate,
/// because phase 1d is concurrently rebuilding what "a lit fire" means).</para>
///
/// <para><b>Every state's entry, exit and behaviour is enumerated below</b> — the house rule that
/// no mechanic is done until it has all three:</para>
/// <list type="bullet">
///   <item><description><b>Active.</b> <i>Entry:</i> spawn, or any recovery. <i>Behaviour:</i>
///   full control; input reaches <c>AvatarMotor.Step</c>; impulse hits may accumulate.
///   <i>Exit:</i> <see cref="Enter"/> with any cause, or <see cref="Impulse"/> stacking past
///   <see cref="IncapacityRules.ImpulseHitsToKnockout"/>.</description></item>
///   <item><description><b>Knocked Out.</b> <i>Entry:</i> a Knocked Out cause; carried items
///   scatter, camera detaches from input, control is denied. <i>Behaviour:</i> a teammate's shake
///   accumulates while it continues and <b>resets to zero the moment it stops</b>. <i>Exit:</i>
///   the shake completing (<see cref="IncapacityStep.Woke"/>) or dawn.</description></item>
///   <item><description><b>Frozen.</b> <i>Entry:</i> a Frozen cause; same cascade as Knocked Out.
///   <i>Behaviour:</i> a thaw timer accumulates inside a lit fire's warmth and resets on leaving
///   it; the body is draggable by a teammate and cannot self-propel. <i>Exit:</i> the thaw
///   completing (<see cref="IncapacityStep.Thawed"/>) or dawn.</description></item>
///   <item><description><b>Impulse ragdoll</b> — a <i>modifier on Active</i>, not a state, and
///   deliberately so (beta plan §10: "momentary, auto-recovering, not incapacitation unless
///   stacked"). <i>Entry:</i> <see cref="Impulse"/>. <i>Behaviour:</i> control denied while
///   momentum carries the body; it does <b>not</b> scatter carried items, does <b>not</b> count
///   toward the all-incapacitated loss condition, and does <b>not</b> detach the camera.
///   <i>Exit:</i> its own timer (<see cref="IncapacityStep.ImpulseEnded"/>), stacking into a real
///   Knocked Out, or dawn.</description></item>
/// </list>
/// </summary>
public sealed class IncapacitationMachine
{
    /// <summary>The world facts one <see cref="Advance"/> needs, gathered by the service. A struct
    /// rather than two bare bools so a future recovery channel (a snack that halves thaw time,
    /// beta plan §12) is an added field rather than a changed signature at every call site.</summary>
    public readonly record struct AssistInputs(bool BeingShaken, bool NearWarmth)
    {
        /// <summary>Nobody is helping and there is no fire. The correct default for a peer the
        /// service could not resolve — never "assume they are being rescued".</summary>
        public static readonly AssistInputs None = new(false, false);
    }

    /// <summary>What this body currently is. Replicated (it rides <c>MoveState</c>); everything
    /// else on this class is server-side bookkeeping.</summary>
    public IncapacityState State { get; private set; } = IncapacityState.Active;

    /// <summary>What put them here. <see cref="IncapacityCause.None"/> while Active.</summary>
    public IncapacityCause Cause { get; private set; } = IncapacityCause.None;

    /// <summary>How they last got up. Survives into Active so telemetry and the bus photo can
    /// read it after the fact.</summary>
    public IncapacityExit LastExit { get; private set; } = IncapacityExit.None;

    /// <summary>Seconds in the current state. Reset on every entry and every recovery. The
    /// Starer's anti-softlock timeout (phase 3c) is expected to read this rather than start its
    /// own clock.</summary>
    public float ElapsedSec { get; private set; }

    /// <summary>Progress toward <see cref="IncapacityRules.ShakeSecondsToWake"/>, in seconds.</summary>
    public float ShakeSec { get; private set; }

    /// <summary>Progress toward <see cref="IncapacityRules.ThawSecondsAtFire"/>, in seconds.</summary>
    public float ThawSec { get; private set; }

    /// <summary>Seconds left of a momentary impulse ragdoll; zero when there is none.</summary>
    public float ImpulseRemainingSec { get; private set; }

    /// <summary>Impulse hits still inside the stacking window.</summary>
    public int ImpulseHits { get; private set; }

    /// <summary>The comic marks this player has collected this run. Append-only; recovery never
    /// clears them, which is what makes the bus photo worth taking (beta plan §4.4).</summary>
    public InjuryMark Injuries { get; private set; } = InjuryMark.None;

    private float _stackWindowRemaining;

    /// <summary>Down: Knocked Out or Frozen. <b>This is the predicate the run-end condition
    /// counts</b> (beta plan §4.1 — "Frozen counts as incapacitated"). An impulse ragdoll is
    /// deliberately NOT included.</summary>
    public bool Incapacitated => State != IncapacityState.Active;

    /// <summary>Momentarily ragdolled but not incapacitated.</summary>
    public bool ImpulseRagdolled => ImpulseRemainingSec > 0f;

    /// <summary>Steering input must not reach the motor. True for both incapacitation and the
    /// momentary impulse — the union, because these are the only two things in the game that
    /// take control, and every consumer wants the union rather than one of them.</summary>
    public bool ControlDenied => Incapacitated || ImpulseRagdolled;

    /// <summary>A teammate can drag this body. Only ice blocks slide; a Knocked Out player is
    /// shaken where they lie (beta plan §10 gives the drag verb to Frozen alone).</summary>
    public bool Draggable => State == IncapacityState.Frozen;

    /// <summary>How far through the relevant assist this body is, in <c>[0,1]</c>. For the
    /// rescuer's progress feedback (INTERACTION-BIBLE §2) and for the headless suite.</summary>
    public float AssistProgress01 => State switch
    {
        IncapacityState.KnockedOut => Mathf.Clamp(ShakeSec / IncapacityRules.ShakeSecondsToWake, 0f, 1f),
        IncapacityState.Frozen => Mathf.Clamp(ThawSec / IncapacityRules.ThawSecondsAtFire, 0f, 1f),
        _ => 0f,
    };

    /// <summary>
    /// The door into incapacitation. <b>Idempotent and first-cause-wins:</b> a body that is
    /// already down stays in the state it is already in, and returns false.
    ///
    /// <para>That rule is a decision, not an accident. Two causes can be live in the same tick
    /// (the Long One reaches an ice block; something stings a Knocked Out player) and the
    /// alternative — last cause wins — makes the state flip-flop every tick that two creatures
    /// are both in range, restarting the rescue timer under the teammate trying to help. Whoever
    /// got there first owns the body until it is exited. An ice block is not further knockable
    /// over, which is also the reading the register law wants.</para>
    /// </summary>
    /// <returns>True only if this call actually put an Active body down.</returns>
    public bool Enter(IncapacityCause cause)
    {
        if (cause == IncapacityCause.None)
            return false;
        // Resolved BEFORE the Active guard, deliberately: a cause with no declared state must
        // throw whether or not the body happened to be down already, or the fork Issue #179 asks
        // us to surface would hide behind whatever the player was doing at the time.
        IncapacityState target = IncapacityRules.StateForCause(cause);
        if (State != IncapacityState.Active)
            return false;

        State = target;
        Cause = cause;
        LastExit = IncapacityExit.None;
        ElapsedSec = 0f;
        ShakeSec = 0f;
        ThawSec = 0f;
        // An impulse ragdoll is subsumed by the real thing, hits and all: being flattened resets
        // the stacking ledger, so a player who is shaken awake starts their next stack from zero
        // rather than one shove from the floor.
        ImpulseRemainingSec = 0f;
        ImpulseHits = 0;
        _stackWindowRemaining = 0f;
        Injuries |= IncapacityRules.MarkForCause(cause);
        return true;
    }

    /// <summary>
    /// One momentary impulse ragdoll — the Breaker's arm-swing shape (beta plan §8.1: it shoves
    /// through, it never blocks). Comic and self-clearing on its own timer, <b>unless</b> it is
    /// the <see cref="IncapacityRules.ImpulseHitsToKnockout"/>th inside
    /// <see cref="IncapacityRules.ImpulseStackWindowSec"/>, at which point it becomes a real
    /// Knocked Out under <paramref name="stackCause"/>.
    ///
    /// <para>No-op on a body that is already down: you cannot shove over something already flat,
    /// and letting the Breaker re-ragdoll a Knocked Out player would let it hold a player on the
    /// floor indefinitely — a soft-lock wearing a physics effect's clothes.</para>
    /// </summary>
    /// <param name="stackCause">The cause recorded if this hit is the one that stacks. The
    /// Breaker passes <see cref="IncapacityCause.BreakerRampage"/>; a stinging creature passes its own, which
    /// is why this is a parameter and not hard-coded to the Breaker.</param>
    public IncapacityStep Impulse(IncapacityCause stackCause = IncapacityCause.BreakerRampage)
    {
        if (Incapacitated)
            return IncapacityStep.None;

        ImpulseHits++;
        _stackWindowRemaining = IncapacityRules.ImpulseStackWindowSec;
        if (ImpulseHits >= IncapacityRules.ImpulseHitsToKnockout)
        {
            Enter(stackCause);
            return IncapacityStep.StackedToKnockout;
        }
        // Re-armed, not extended: each shove is its own full stumble. Extending would let a
        // stream of sub-threshold hits deny control forever without ever crossing the KO line.
        ImpulseRemainingSec = IncapacityRules.ImpulseRagdollSec;
        return IncapacityStep.None;
    }

    /// <summary>
    /// One momentary impulse ragdoll that is <b>exempt from the stacking ledger</b> — the comic
    /// tumble without the path to a Knocked Out. Strictly additive: it neither reads nor writes
    /// <see cref="ImpulseHits"/> or the stacking window, so a body can be blasted all night and its
    /// Breaker ledger is exactly where the Breaker left it. <see cref="Impulse"/> is untouched and
    /// every existing caller keeps the shipped behaviour.
    ///
    /// <para><b>Built for a since-removed prey creature's chaining shock blast</b>
    /// and exempt for three
    /// reasons, each of which would be sufficient on its own:</para>
    /// <list type="number">
    /// <item><b>The chain makes stacking trivially reachable.</b> In a party of six charged players, two chains
    /// inside the six-second window is ordinary play, not an edge case. Knocking your friends
    /// unconscious by owning a kit would be a punishment with no counterplay and no author.</item>
    /// <item><b><see cref="IncapacityState.KnockedOut"/> as a consequence model is Retired.</b>
    /// Canon's Retired list, 2026-08-11: recoverable incapacitation as the death model is retired by
    /// the prey pivot. Routing a brand-new mechanic into it would be building toward a retired
    /// direction.</item>
    /// <item><b>The register law.</b> A clip of small animals bouncing off trees is the register; a
    /// clip of an animal electrocuted flat is not — and electricity is the one element in this game
    /// whose real-world imagery is injury, so this is the drift that has to be closed structurally
    /// rather than tuned down.</item>
    /// </list>
    ///
    /// <para><b>No-op on a body that is already down</b>, exactly as <see cref="Impulse"/> is and
    /// for the identical reason: you cannot shove over something already flat, and re-ragdolling a
    /// Knocked Out player is a soft-lock wearing a physics effect's clothes. A charged player that is
    /// Knocked Out or Frozen still <i>chains</i> — its blast goes off and reaches everyone — its
    /// body simply does not leave the ground.</para>
    ///
    /// <para><b>Re-armed, not extended</b>, again matching <see cref="Impulse"/>: each launch is its
    /// own full tumble, and the duration is an absolute bound from the moment of the impulse that no
    /// bounce and no second blast may lengthen.</para>
    /// </summary>
    /// <returns>True if this call actually armed a ragdoll — the caller applies its launch velocity
    /// only then, so an incapacitated body is never handed a velocity it is not allowed to use.</returns>
    public bool ImpulseNonStacking()
    {
        if (Incapacitated)
            return false;
        ImpulseRemainingSec = IncapacityRules.ImpulseRagdollSec;
        return true;
    }

    /// <summary>
    /// Advance one server tick. <b>At most one boundary is crossed per call</b>, the same
    /// guarantee <c>SputterSequence.Advance</c> gives: a 999-second hitch (a loading stall, a
    /// debugger break, a headless suite stepping coarsely) can never carry a body through a
    /// state it was supposed to occupy. Degenerate <paramref name="dt"/> — zero, negative,
    /// NaN, infinite — is a no-op rather than an exception or a corrupted timer.
    ///
    /// <para><b>Resolution order is fixed and stated</b> (MECHANICS-BIBLE §3) so two things
    /// landing on the same frame always resolve the same way rather than "whichever ran first":
    /// the stacking window decays, then the impulse ragdoll, then the assist timers. The window
    /// decay is not a boundary — it returns no step — so it may share a call with one.</para>
    /// </summary>
    public IncapacityStep Advance(float dt, in AssistInputs inputs)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
            return IncapacityStep.None;

        ElapsedSec += dt;

        // 1. The stacking window, which decays in every state.
        if (_stackWindowRemaining > 0f)
        {
            _stackWindowRemaining -= dt;
            if (_stackWindowRemaining <= 0f)
            {
                _stackWindowRemaining = 0f;
                ImpulseHits = 0;
            }
        }

        // 2. The momentary ragdoll. Returns early either way: it only ever exists on an Active
        //    body, so there is no assist below it to run.
        if (ImpulseRemainingSec > 0f)
        {
            ImpulseRemainingSec = Mathf.Max(0f, ImpulseRemainingSec - dt);
            return ImpulseRemainingSec <= 0f ? IncapacityStep.ImpulseEnded : IncapacityStep.None;
        }

        if (State == IncapacityState.Active)
            return IncapacityStep.None;

        // 3. The assist timers. Both are NON-CUMULATIVE — the identical rule, and the identical
        //    reasoning, as WaterService's 20-second dry-off: this is five seconds OF being
        //    shaken, not five seconds of shaking banked across the night. Stepping away resets
        //    it, which is the only reading that makes staying a decision.
        switch (State)
        {
            case IncapacityState.KnockedOut:
                if (!inputs.BeingShaken)
                {
                    ShakeSec = 0f;
                    return IncapacityStep.None;
                }
                ShakeSec += dt;
                if (ShakeSec < IncapacityRules.ShakeSecondsToWake)
                    return IncapacityStep.None;
                Recover(IncapacityExit.TeammateShake);
                return IncapacityStep.Woke;

            case IncapacityState.Frozen:
                if (!inputs.NearWarmth)
                {
                    ThawSec = 0f;
                    return IncapacityStep.None;
                }
                ThawSec += dt;
                if (ThawSec < IncapacityRules.ThawSecondsAtFire)
                    return IncapacityStep.None;
                Recover(IncapacityExit.FireThaw);
                return IncapacityStep.Thawed;

            default:
                return IncapacityStep.None;
        }
    }

    /// <summary>
    /// Dawn. Every state, from every cause, unconditionally — <b>there is no guard here and there
    /// must never be one</b>. This is MECHANICS-BIBLE §10.5's anti-unwinnable floor and beta plan
    /// §4.1's dawn floor, and it is the reason a group that loses every rescuer still has a
    /// morning. It also clears a momentary impulse ragdoll, because "unconditionally" means the
    /// body is handed back whole.
    ///
    /// <para>Injuries deliberately survive: dawn gives you your body back, not your dignity
    /// (beta plan §4.4, the bus photo).</para>
    /// </summary>
    /// <returns>True if this call actually recovered something.</returns>
    public bool RecoverAtDawn()
    {
        bool wasDenied = ControlDenied;
        Recover(IncapacityExit.Dawn);
        return wasDenied;
    }

    /// <summary>Wipe to Active without recording an exit — a run reset or a peer leaving, not a
    /// recovery. Clears the injury ledger too, which <see cref="RecoverAtDawn"/> pointedly does
    /// not: a new run is a new player.</summary>
    public void Reset()
    {
        Recover(IncapacityExit.None);
        Injuries = InjuryMark.None;
    }

    /// <summary>
    /// Put this machine back exactly as it was. <b>Only for a cascade that failed before its
    /// commit</b> — the machine must never believe a transition no other system ever saw, which
    /// is the one-sided version of the defect the cascade table exists to prevent.
    ///
    /// <para>Deliberately NOT <see cref="Reset"/>, and that distinction cost a bug in review:
    /// <c>Reset</c> clears the injury ledger, so rolling back through it would wipe a player's
    /// whole run history — every soot mark and bandage from earlier nights — because one
    /// transition failed. The caller snapshots <paramref name="injuries"/> before the attempt and
    /// hands it straight back.</para>
    /// </summary>
    public void RollBackTo(IncapacityState state, IncapacityCause cause, InjuryMark injuries)
    {
        Recover(IncapacityExit.None);
        Injuries = injuries;
        if (state != IncapacityState.Active)
        {
            State = state;
            Cause = cause;
        }
    }

    /// <summary>
    /// <b>The run-end predicate</b> (beta plan §4.1), as a pure function so the run's only hard
    /// loss condition is provable in <c>dotnet test</c> rather than only reachable through a live
    /// session. <c>IncapacitationService.AllConnectedIncapacitated</c> is a one-line delegation to
    /// this; phase 2c should read the service, not re-derive either.
    ///
    /// <para>Two decisions are baked in and both matter. <b>Frozen counts</b> — the plan says so,
    /// and asking <see cref="Incapacitated"/> rather than "is knocked out" is what makes that
    /// structural rather than remembered. <b>An empty camp is false</b>, not vacuously true: zero
    /// connected players is a session that has not started or has already ended, never a group
    /// that has lost.</para>
    /// </summary>
    public static bool AllIncapacitated(System.Collections.Generic.IEnumerable<IncapacitationMachine> machines)
    {
        bool any = false;
        foreach (IncapacitationMachine m in machines)
        {
            any = true;
            if (!m.Incapacitated)
                return false;
        }
        return any;
    }

    private void Recover(IncapacityExit exit)
    {
        State = IncapacityState.Active;
        Cause = IncapacityCause.None;
        LastExit = exit;
        ElapsedSec = 0f;
        ShakeSec = 0f;
        ThawSec = 0f;
        ImpulseRemainingSec = 0f;
        ImpulseHits = 0;
        _stackWindowRemaining = 0f;
    }
}
