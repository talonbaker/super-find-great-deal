using System;
using System.Collections.Generic;
using System.Linq;
using MpFoundation.Game.Props;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The seam W7-8 opened between "how the body holds it" and "what it costs to carry it".</b>
///
/// <para>Talon, 2026-08-30 (note 4): <i>"the cubes ... when picked up, are not actually being
/// 'held' the hands of the player character don't actually move."</i> The cause was that one
/// predicate — <c>PropManager.IsArmCarried</c> — answered both questions at once. It is a
/// GAMEPLAY rule (does this ride the arms channel instead of costing a loadout slot), and using it
/// as the pose rule meant a 0.44 m crate could only ever be posed as a one-handed grip on a
/// handle, with the wrist solving inside the mesh.</para>
///
/// <para><b>Why these are unit tests and not a capture.</b> A capture proves what the crate looks
/// like today. What has to hold FOREVER is that nobody quietly widens the slot rule while fixing a
/// pose — that change is invisible in any screenshot and shows up only as "I can carry less than I
/// used to", weeks later, with no obvious cause. These pin the two predicates apart.</para>
/// </summary>
public class ArmfulPoseTests
{
    private static readonly PropKind[] AllKinds =
        Enum.GetValues(typeof(PropKind)).Cast<PropKind>().ToArray();

    /// <summary>
    /// <b>W7-8 acceptance criterion 3, as an assertion: nothing that was already arm-carried
    /// behaves differently.</b> The load lift is the one new positional change, and firewood and
    /// stone must not take it — they were calibrated with the armful pose when CARRY-1 built it.
    ///
    /// <para><b>This test carries its own positive control</b>, in the second half: the same
    /// predicate must return TRUE for the crate and the ball. Without it, "Log takes no lift" would
    /// stay green if <c>TakesLoadLift</c> were hard-wired to false, and an absence check that
    /// cannot demonstrate a presence proves nothing.</para>
    /// </summary>
    [Fact]
    public void OnlyTheNewlyArmPosedKindsTakeTheLoadLift()
    {
        // The kinds that must NOT take the lift (firewood, stone, the handled tools) left with the
        // MVP extraction, so "only these" is asserted by enumerating every kind the enum still has
        // rather than by naming absentees: an ADDED kind that takes the lift without being posed
        // as an armful shows up here as a red test instead of passing unseen.
        //
        // SFX-1 (2026-09-19) appended Can/Box/Produce and put all three on the armful pose, so
        // they join this list. That is the SAME rule, not a widening of it: the predicate's own
        // doc says it asks "does this have a handle to grip", and a can, a cereal box and an
        // apple do not. The gameplay rule this test exists to protect — that nobody quietly
        // widens the SLOT rule while fixing a pose — is untouched, because there is no slot rule
        // left in this build to widen; what the test now pins is that every kind which takes the
        // lift is also arm-posed, which is the invariant that outlives the specific list.
        PropKind[] lifted = AllKinds.Where(PropManager.TakesLoadLift).ToArray();
        Assert.Equal(
            new[] { PropKind.Crate, PropKind.Ball, PropKind.Can, PropKind.Box, PropKind.Produce },
            lifted);
        foreach (PropKind kind in lifted)
            Assert.True(PropManager.IsArmfulPose(kind), $"{kind} takes the load lift but is not arm-posed");

        // POSITIVE CONTROL — the detector above is only worth something if it can also say yes.
        Assert.True(PropManager.TakesLoadLift(PropKind.Crate));
        Assert.True(PropManager.TakesLoadLift(PropKind.Ball));
    }

    /// <summary>
    /// <c>Carryable.Shape</c> and <see cref="PropKind"/> are mapped onto each other by an ordinal
    /// cast in <c>SandboxAvatar.CarryPoseForVisual</c>'s offline branch and in
    /// <c>Carryable.LoadLiftM</c>. Both enums carry an append-only warning in their own docs; this
    /// pins the correspondence so a reorder is a red test rather than an offline sandbox that poses
    /// a camera as an armful.
    /// </summary>
    [Fact]
    public void CarryableShapeOrdinalsMatchPropKindOrdinals()
    {
        string[] shape = Enum.GetNames(typeof(MpFoundation.Game.Sandbox.Carryable.Shape));
        string[] kind = Enum.GetNames(typeof(PropKind));
        Assert.Equal(kind, shape);
    }
}
