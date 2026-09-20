using System;

namespace MpFoundation.Game.Aim;

/// <summary>
/// Raise-to-aim stance. A byte enum with explicit ordinals — this replicates over the wire
/// (see NetCodec.Snapshot.AimStance), so the ordinals themselves (not just the names) are
/// load-bearing and must never be renumbered. Shape and naming deliberately mirror
/// CamcorderController.CamcorderStance (feat/tier0-camcorder, Tasks 1-6) — read, not merged;
/// this component has zero camcorder dependency.
/// </summary>
public enum AimStance : byte
{
    Lowered = 0,
    Raising = 1,
    Raised = 2,
    Lowering = 3,
}

/// <summary>
/// The shared aim rig any aimed verb sits on top of. Pure logic: no scene tree, no physics, no
/// networking, no equipment/inventory concept — SandboxAvatar wires this into the net layer as
/// its own concern, and each verb decides for itself whether its equipment is active before
/// feeding wantRaised in (see the PR's consumption contract; this class does not gate on "do you
/// have a tool" at all — verb-agnostic by design).
///
/// State machine (exact — SandboxAvatar's wiring depends on these names/values verbatim),
/// same committed-transition shape as CamcorderController:
/// <code>
///   Lowered --wantRaised--&gt; Raising --RaiseDurationSec elapsed--&gt; Raised
///   Raised --!wantRaised--&gt; Lowering --RaiseDurationSec elapsed--&gt; Lowered
/// </code>
/// Both transitions are COMMITTED: once Raising/Lowering begins, releasing (or re-pressing)
/// wantRaised mid-transition does not cancel or reverse it — only the elapsed-time threshold
/// does. A half-raised pose is a confusing, unreadable state, not a legal resting one.
///
/// Steadiness is a second, independent piece of bookkeeping layered on top of Stance: it only
/// ever accumulates while fully Raised AND the carrier is closer to stationary than
/// <see cref="SteadyMoveSpeedThreshold"/>, resets to zero the instant Stance leaves Raised (no
/// "pre-steadied" carry-over across a lower/re-raise — you re-settle every time), and decays
/// (faster than it builds) the moment the carrier starts moving while still Raised. Both the
/// settle and decay rates are plain elapsed-time integration with no randomness, so an owner's
/// prediction and the server's authority reach bit-identical values given identical
/// (Stance, wantRaised, horizontalSpeed) history — this is what makes Steadiness01 safe for a
/// server-side shot-validity check to read directly (see the class's PR-body consumption
/// contract for exactly which side computes it when).
/// </summary>
public sealed class AimController
{
    /// <summary>Time to complete a raise or a lower, in either direction — committed once
    /// begun (see class doc). Matches CamcorderController.RaiseDurationSec: both are "bring a
    /// two-handed thing up to your face" gestures and sharing the feel is deliberate, not
    /// coincidental — a value fork resolved by precedent rather than picked fresh.</summary>
    public const float RaiseDurationSec = 0.4f;

    /// <summary>Movement speed multiplier while Stance != Lowered — same value and rationale as
    /// CamcorderController.RaisedSpeedFactor (held safely above AvatarMotor.MinSpeedFactor).</summary>
    public const float RaisedSpeedFactor = 0.6f;

    /// <summary>Seconds of continuous, near-stationary Raised holding needed to reach full
    /// (1.0) steadiness from a cold raise. Value fork, picked for feel: fast enough that a
    /// snap-shot a beat after raising isn't punished, slow enough that "raise and immediately
    /// fire" reads as shakier than "raise, hold, then fire."</summary>
    public const float SteadySettleDurationSec = 1.0f;

    /// <summary>Seconds for steadiness to fully decay back to 0 once the carrier starts moving
    /// while Raised — deliberately faster than it builds (SteadySettleDurationSec): losing your
    /// footing should cost you immediately, not gracefully. Value fork, picked for feel.</summary>
    public const float SteadyDecaySettleDurationSec = 0.25f;

    /// <summary>Horizontal speed (m/s) at or below which the carrier counts as "stationary" for
    /// steadiness purposes. Below AvatarMotor.MoveSpeed (3.6) by a wide margin, so ordinary idle
    /// sway/breathing-scale motion never reads as "moving" — only genuine walking does. Value
    /// fork, picked for feel.</summary>
    public const float SteadyMoveSpeedThreshold = 0.4f;

    private float _transitionElapsed;
    private float _steadyElapsed;

    public AimStance Stance { get; private set; } = AimStance.Lowered;

    /// <summary>Raw accumulator backing <see cref="Steadiness01"/>, in seconds, clamped to
    /// [0, SteadySettleDurationSec]. Exposed (not just the normalized 0..1 value) because this
    /// is the value that actually replicates/reconciles (NetCodec.Snapshot.AimSteadyElapsedSec)
    /// — Steadiness01 itself is always re-derived from it, never sent separately.</summary>
    public float SteadyElapsedSec => _steadyElapsed;

    /// <summary>0 (just raised, or actively moving-while-raised) .. 1 (fully steadied after
    /// SteadySettleDurationSec of stationary holding). Always exactly 0 while Stance != Raised
    /// — a verb's validity check does not need to separately gate on Stance if it already reads
    /// this. Deterministic given Step's own inputs on both client and server — see class doc.</summary>
    public float Steadiness01 => Stance == AimStance.Raised
        ? Math.Clamp(_steadyElapsed / SteadySettleDurationSec, 0f, 1f)
        : 0f;

    /// <summary>Movement speed multiplier: RaisedSpeedFactor whenever Stance != Lowered, else
    /// 1.0 — mid-raise/mid-lower costs the same mobility penalty as fully raised (two-handed
    /// prop off the hip the whole time), matching CamcorderController.SpeedFactor's rule.</summary>
    public float SpeedFactor => Stance == AimStance.Lowered ? 1f : RaisedSpeedFactor;

    /// <summary>
    /// Advances the state machine by dt seconds. wantRaised is a held level (true = the carrier
    /// wants the aim rig up); horizontalSpeed is this tick's flat (XZ) movement speed in m/s,
    /// read from the SAME MoveState both prediction and authority already compute for movement
    /// — feeding nothing new into the wire, since horizontalSpeed is derivable from data that
    /// already replicates. NaN or negative dt is a complete no-op (AvatarMotor.
    /// SanitizeSpeedFactor hygiene precedent). A non-finite horizontalSpeed is treated as
    /// "moving" (fails safe toward LESS steadiness, never more, on a malformed input).
    /// </summary>
    public void Step(float dt, bool wantRaised, float horizontalSpeed)
    {
        if (!float.IsFinite(dt) || dt < 0f)
            return;

        AdvanceStance(dt, wantRaised);
        AdvanceSteadiness(dt, horizontalSpeed);
    }

    // A committed transition consumes THIS step's dt immediately on entry, so a single huge-dt
    // Step (self-tests only — real callers tick at physics dt, far below RaiseDurationSec)
    // completes the whole raise/lower in one call instead of stalling mid-transition. wantRaised
    // is read only at the moment a transition BEGINS (from Lowered or Raised); Raising and
    // Lowering never re-read it once entered — that is the entire meaning of "committed".
    private void AdvanceStance(float dt, bool wantRaised)
    {
        switch (Stance)
        {
            case AimStance.Lowered:
                if (wantRaised)
                    EnterTransition(AimStance.Raising, AimStance.Raised, dt);
                break;

            case AimStance.Raising:
                ContinueTransition(AimStance.Raised, dt);
                break;

            case AimStance.Raised:
                if (!wantRaised)
                    EnterTransition(AimStance.Lowering, AimStance.Lowered, dt);
                break;

            case AimStance.Lowering:
                ContinueTransition(AimStance.Lowered, dt);
                break;
        }
    }

    private void EnterTransition(AimStance transitional, AimStance destination, float dt)
    {
        Stance = transitional;
        _transitionElapsed = dt;
        if (_transitionElapsed >= RaiseDurationSec)
        {
            Stance = destination;
            _transitionElapsed = 0f;
        }
    }

    private void ContinueTransition(AimStance destination, float dt)
    {
        _transitionElapsed += dt;
        if (_transitionElapsed >= RaiseDurationSec)
        {
            Stance = destination;
            _transitionElapsed = 0f;
        }
    }

    private void AdvanceSteadiness(float dt, float horizontalSpeed)
    {
        if (Stance != AimStance.Raised)
        {
            // No "pre-steadied" carry-over across a lower/re-raise (see class doc) — every
            // stance other than fully Raised (including mid-transition) resets the clock.
            _steadyElapsed = 0f;
            return;
        }

        bool stationary = float.IsFinite(horizontalSpeed) && horizontalSpeed <= SteadyMoveSpeedThreshold;
        float rate = stationary ? 1f / SteadySettleDurationSec : -1f / SteadyDecaySettleDurationSec;
        _steadyElapsed = Math.Clamp(_steadyElapsed + rate * dt, 0f, SteadySettleDurationSec);
    }

    /// <summary>
    /// Hard-sets this controller's state directly from an authoritative source, bypassing
    /// <see cref="Step"/>'s normal per-tick advancement — the owner's prediction mirror's way
    /// of adopting the server's declared state when its own local prediction has diverged.
    /// Same collapse rule as CamcorderController.AdoptAuthoritative: adopting a COMMITTED
    /// stance (Lowered/Raised) is exact; a mid-transition stance collapses to its nearest
    /// committed endpoint (Raising -&gt; Raised, Lowering -&gt; Lowered) rather than threading a
    /// sub-tick transition-progress value over the wire for a correction path that only fires
    /// on genuine divergence, not every tick. steadyElapsedSec is clamped into its documented
    /// range and forced to 0 unless the adopted stance is Raised — a second, independent entry
    /// point into this controller's state, so it must not trust the caller to have upheld that
    /// invariant already (same defensive posture as CamcorderController.AdoptAuthoritative's
    /// battery/tape clamps).
    /// </summary>
    public void AdoptAuthoritative(AimStance stance, float steadyElapsedSec)
    {
        Stance = stance switch
        {
            AimStance.Raising => AimStance.Raised,
            AimStance.Lowering => AimStance.Lowered,
            _ => stance,
        };
        _transitionElapsed = 0f;
        _steadyElapsed = Stance == AimStance.Raised
            ? Math.Clamp(steadyElapsedSec, 0f, SteadySettleDurationSec)
            : 0f;
    }
}
