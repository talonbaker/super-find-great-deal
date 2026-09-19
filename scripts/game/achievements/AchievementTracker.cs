using MpFoundation.Game.Aim;
using MpFoundation.Net;

namespace Sail.Game.Achievements;

/// <summary>
/// <b>Drives achievements 1-3 off real, already-resolved player state — nothing here reads raw
/// input.</b> Packet W7-5's stop condition is explicit: the duck walk and the tough-guy pose are
/// existing player states to be found, not invented, and a detector built off a raw button would
/// award the achievement to a player who never actually reached the pose. So this class is fed
/// exactly the two values <c>SandboxAvatar.OwnerTick</c> already computes and publishes every
/// tick — <see cref="MoveVerb"/> (<c>SandboxAvatar.VerbNow</c> / <c>MoveState.Verb</c>, MOVE-5)
/// and <see cref="AimStance"/> (<c>SandboxAvatar.AimStance</c>, the shared raise-to-aim rig,
/// WP-L3) — and nothing else. Both already replicate and are read the same way whether the
/// avatar is the owner's own predicted body or (in principle) a proxy's, so this tracker is
/// simulation-shaped rather than presentation-shaped, even though today only the owner's own
/// avatar ever constructs one (see <c>SandboxAvatar</c>'s wiring comment).
///
/// <para><b>"Duck walk"</b> — Talon's note 12.1. <see cref="MoveVerb.DuckWalk"/> IS the duck
/// walk's latch (<c>MoveState.cs</c>'s own doc: "the latch IS the enum value"), so there is
/// nothing to debounce or time — the achievement unlocks the instant the verb reads
/// <see cref="MoveVerb.DuckWalk"/> for the first time in this player's history.</para>
///
/// <para><b>"Tough guy"</b> — note 12.2, "holds right mouse click for three seconds at least".
/// Right mouse button is bound to the "aim" action (<c>project.godot</c>), which drives
/// <see cref="AimController"/> unconditionally regardless of what is equipped
/// (<c>IntentSources.cs</c>'s own comment: "'aim' held ... drives the shared raise-to-aim rig
/// unconditionally"; <c>AvatarVisual.SetAiming</c>'s doc: "independent of carrying"). So holding
/// right mouse with empty hands raises the same rig — hands up near eye level — that any
/// aimed-equipment raise also uses, and that raised silhouette is the pose Talon is describing.
/// <see cref="ToughGuyHoldSec"/> (a VALUE fork, picked to be exactly his stated number) is how
/// long <see cref="AimStance.Raised"/> must hold CONTINUOUSLY; any tick that is not fully
/// Raised — Lowered, Raising, or Lowering — resets the clock to zero, so a player who dips in
/// and out never accumulates a total past several short holds.</para>
///
/// <para><b>"Tough guy duck walk"</b> — note 12.3, explicitly "a conjunction ... held at the
/// same time, not a sequence" (packet W7-5, acceptance criterion 4). This unlocks when the
/// duck-walk verb and a FULLY-HELD tough-guy pose (the same <see cref="ToughGuyHoldSec"/>
/// threshold, not a bare instantaneous overlap — "makes a tough guy pose" reads as the held pose,
/// not a glance through it mid-raise) are true on the SAME tick, checked directly against this
/// tick's live verb/hold state rather than against <see cref="AchievementUnlocker.IsEarned"/> for
/// 1 or 2. That is the one design decision this class exists to get right: an implementation that
/// instead gated on "has 1 already been earned AND has 2 already been earned" would silently deny
/// a player who reaches both together for the very first time, because those flags would still
/// read false going into the tick that should unlock all three at once.</para>
/// </summary>
public sealed class AchievementTracker
{
    /// <summary>Seconds the aim rig must sit fully <see cref="AimStance.Raised"/>, continuously,
    /// to earn "Tough guy" — Talon's own number ("at least" three seconds), not a fork to
    /// re-pick.</summary>
    public const float ToughGuyHoldSec = 3.0f;

    private readonly AchievementUnlocker _unlocker;
    private float _aimHeldSec;

    public AchievementTracker(AchievementUnlocker unlocker) => _unlocker = unlocker;

    /// <summary>How long the aim rig has been continuously Raised this hold, for a caller that
    /// wants to show progress. Not consumed by this class itself.</summary>
    public float AimHeldSec => _aimHeldSec;

    /// <summary>
    /// One call per owner physics tick. <paramref name="dt"/> follows the same sanitisation
    /// precedent every other Step in this codebase uses (<c>AimController.Step</c>,
    /// <c>StanceController.Step</c>): non-finite or negative is a complete no-op, so a malformed
    /// caller can never wind the hold clock backwards or unlock anything early.
    /// </summary>
    public void Step(float dt, MoveVerb verb, AimStance aim)
    {
        if (!float.IsFinite(dt) || dt < 0f)
            return;

        bool duckWalking = verb == MoveVerb.DuckWalk;
        if (duckWalking)
            _unlocker.TryUnlock(AchievementId.DuckWalk);

        bool fullyRaised = aim == AimStance.Raised;
        _aimHeldSec = fullyRaised ? _aimHeldSec + dt : 0f;
        bool toughGuyPoseHeld = _aimHeldSec >= ToughGuyHoldSec;
        if (toughGuyPoseHeld)
            _unlocker.TryUnlock(AchievementId.ToughGuy);

        // The conjunction: both live conditions true on THIS tick, never gated on 1/2's earned
        // flags — see class doc for why that distinction is the entire point.
        if (duckWalking && toughGuyPoseHeld)
            _unlocker.TryUnlock(AchievementId.ToughGuyDuckWalk);
    }
}
