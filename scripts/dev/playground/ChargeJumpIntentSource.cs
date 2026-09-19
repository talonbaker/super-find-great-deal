using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The unified button: press winds up, release jumps, overhold commits to the ground</b>
/// (2026-08-28, Talon's session).
///
/// <para>Talon's design, in his words: <i>"the jump button isn't a jump button, the jump button
/// acts as a sort of a lead into a jump — the character will crouch downward in preparation for the
/// jump, and if they release within a certain time frame then they will jump, but if they don't
/// then they will begin to slide or begin to roll."</i></para>
///
/// <para><b>This resolves a conflict rather than creating one, and that is the point.</b> An
/// earlier draft of this file made the charge and the crouch fight over SPACE: charging had to
/// report <c>JumpHeld = false</c> to stop the crouch verb firing, which meant turning the charge on
/// turned MOVE-5's whole crouch grammar off. Talon's version dissolves that — <b>the crouch IS the
/// wind-up</b>. You are not choosing between a charge and a crouch; the crouch is what a charge
/// looks like, and overholding is what missing the release looks like. One button, three outcomes,
/// no mode.</para>
///
/// <para><b>The window is <see cref="MotorTuning.JumpHoldWindowSec"/> — the one that already
/// exists.</b> Nothing new decides jump-versus-crouch; MOVE-5's own clock does, and this source
/// simply moves the JUMP to the release edge on the near side of it. Read live off
/// <c>MotorTuning.Current</c> every tick rather than latched, so the knob panel's slider retunes
/// the grammar under your hands — which is what makes "how long should the window be" a question
/// the lab can answer.</para>
///
/// <para><b>Height comes from the shipped release cut, not from arithmetic here.</b> On release
/// this fires <c>Jump</c> and then keeps <c>JumpHeld</c> true for a synthetic tail whose LENGTH is
/// how far into the window you got. A quick release drops it almost at once,
/// <c>JumpReleaseGravityMultiplier</c> bites early and the hop is small; a release at the very edge
/// of the window holds through the rise and the jump is full height. Every metre of that arc is
/// <c>AvatarMotor.GravityFor</c> doing its shipped job — nothing here computes a height, and no arc
/// is reachable that the motor could not already produce.</para>
///
/// <para><b>Overholding fires no jump at all, on purpose.</b> Past the window you are in a SLIDE or
/// a TUCK; releasing then stands you up and that is the whole of it. A jump on the way out would
/// mean overholding cost nothing, and the commitment is the entire mechanic.</para>
///
/// <para><b>Getting out of the roll is a fresh press</b>, which arrives here as a new wind-up and
/// leaves as a jump — so the exit from the ground verb is the same verb as the entry to the air
/// one, with no extra button. That is the shipped state machine's own T-table (a press edge is a
/// DuckWalk's only non-trivial exit), reached through this grammar rather than around it.</para>
/// </summary>
public sealed class ChargeJumpIntentSource : IIntentSource
{
    private readonly IIntentSource _inner;

    /// <summary>Live, so the toggle is an A/B on the same ground rather than a relaunch.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hold tail for the shortest possible release: the minimum jump. Short enough to read
    /// as a hop, long enough that a press is never a no-op — a charge whose floor is zero teaches
    /// the player that the button sometimes does not work.</summary>
    public const float MinTailSec = 0.03f;

    /// <summary>Hold tail for a release at the very edge of the window. Comfortably longer than the
    /// rise time of any jump in the lab (0.38 s at the shipped <c>JumpVelocity</c>, 0.21 s at the
    /// skip's), so the top of the range is genuinely uncut rather than merely less cut.</summary>
    public const float MaxTailSec = 0.55f;

    private float _chargeSec;
    private float _tailSec;
    private bool _committed;

    public ChargeJumpIntentSource(IIntentSource inner) => _inner = inner;

    /// <summary>How far through the window the wind-up is, 0..1. A charge the player cannot see is
    /// a charge they have to learn by failing.</summary>
    public float ChargeFraction =>
        Mathf.Clamp(_chargeSec / Mathf.Max(MotorTuning.Current.JumpHoldWindowSec, 0.001f), 0f, 1f);

    /// <summary>True while the button is down and still on the jumping side of the window.</summary>
    public bool Winding => _chargeSec > 0f && !_committed;

    /// <summary>True once the window has elapsed: the jump is gone and the ground verb has it.
    /// </summary>
    public bool Committed => _committed;

    public MoveIntent NextIntent(double delta)
    {
        MoveIntent intent = _inner.NextIntent(delta);
        if (!Enabled)
        {
            _chargeSec = 0f;
            _tailSec = 0f;
            _committed = false;
            return intent;
        }

        float window = Mathf.Max(MotorTuning.Current.JumpHoldWindowSec, 0.001f);

        if (intent.JumpHeld)
        {
            _chargeSec += (float)delta;
            if (_chargeSec >= window)
                _committed = true;
            // JumpHeld passes through UNCHANGED so MOVE-5's verb clock runs normally and the crouch
            // appears on its own schedule. Only the jump edge is withheld.
            return intent with { Jump = false };
        }

        if (_chargeSec > 0f)
        {
            bool committed = _committed;
            float fraction = ChargeFraction;
            _chargeSec = 0f;
            _committed = false;

            // Overheld: the ground verb owns this press. Standing up is the whole of the release.
            if (committed)
                return intent with { Jump = false };

            _tailSec = Mathf.Lerp(MinTailSec, MaxTailSec, fraction) - (float)delta;
            return intent with { Jump = true, JumpHeld = true };
        }

        if (_tailSec > 0f)
        {
            _tailSec -= (float)delta;
            return intent with { JumpHeld = true };
        }

        return intent;
    }
}
