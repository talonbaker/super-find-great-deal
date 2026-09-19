using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>The hook a later lane plugs a snap surface into</b> — the seam between "the server says
/// this transform is physically legal" (<see cref="PlacementIntegrity"/>, which CARRY-1 owns and
/// nobody overrides) and "this particular surface has an opinion about where things go" (a task
/// pad, a shelf slot, a drop-off bin).
///
/// <para><b>Free placement is the default and the default is null.</b> With no validator
/// registered the server accepts any transform that passes placement integrity, which is the
/// R.E.P.O. verb the game is built on: you set the thing down exactly where you are holding it.
/// TASK-1 registers one for the tower pads; BTN-1's drop-off bin can register one to refuse the
/// wrong prop with its own reason instead of a buzz nobody can explain.</para>
///
/// <para><b>It may MOVE the placement, and that is why it returns a transform.</b> A snap pad's
/// whole job is to say "yes, but there" — the lab's <c>InteractionSlot.RestPose</c> is exactly
/// that shape. Returning the intended transform unchanged is the identity case, not a special
/// one.</para>
///
/// <para><b>It runs on the server, after integrity, and its answer is final.</b> After integrity
/// so a validator never has to re-implement the wall test; and if a validator MOVES the
/// placement, the server re-runs integrity on the moved transform, because a pad that snapped a
/// crate into a wall would be exactly the defect §5b exists to prevent, arriving through the one
/// door that bypassed the check.</para>
/// </summary>
public interface IPlacementValidator
{
    /// <summary>
    /// May <paramref name="prop"/> be placed at <paramref name="intended"/> by
    /// <paramref name="holderPeerId"/>?
    /// </summary>
    /// <returns>Allowed, with the transform to actually use; or refused, with the reason the
    /// presser is told. A refusal MUST carry a reason — a silent one is the defect class
    /// (INTERACTION-BIBLE §2).</returns>
    PlacementDecision Validate(NetworkedProp prop, Transform3D intended, int holderPeerId);
}

/// <summary>One validator's answer. <see cref="Allow"/> is the identity case every "I have no
/// opinion about this prop" branch returns.</summary>
public readonly record struct PlacementDecision(
    bool Allowed, Transform3D Transform, PropManager.PlaceDenial Reason)
{
    /// <summary>Yes, exactly there.</summary>
    public static PlacementDecision Allow(Transform3D at) =>
        new(true, at, PropManager.PlaceDenial.None);

    /// <summary>Yes, but THERE — a snap pad moving the placement onto itself.</summary>
    public static PlacementDecision Snap(Transform3D to) =>
        new(true, to, PropManager.PlaceDenial.None);

    /// <summary>No, and this is why.</summary>
    public static PlacementDecision Refuse(PropManager.PlaceDenial reason) =>
        new(false, Transform3D.Identity, reason);
}
