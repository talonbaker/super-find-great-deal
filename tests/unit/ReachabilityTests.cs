using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// REACH-1 / program doc §5b layer 3 — <see cref="Reachability.Evaluate"/>, the engine-free
/// grab-reachability rule, against a fake sampler.
///
/// <para><b>Why a fake and not a scene.</b> The rule's job is to decide; the physics queries'
/// job is to report. Those are separate files on purpose, and the deciding half is the half that
/// has to answer for the six cases in §5b. A scene test can prove the rule and the queries agree
/// on ONE world; this proves the rule answers correctly on every world the fake can describe,
/// including the ones nobody has authored yet. The scene half is
/// <c>tests/Run-ReachTest.ps1</c>.</para>
///
/// <para><b>The fake is a table, not a simulator.</b> It answers "is there floor here" and "what
/// does the eye meet" from lists, so every case below reads as the sentence §5b uses for it.</para>
/// </summary>
public class ReachabilityTests
{
    // --- the fake ------------------------------------------------------------------------------

    /// <summary>A sampler whose answers are written down rather than computed. Every field maps
    /// onto one line of §5b.</summary>
    private sealed class FakeSampler : Reachability.IReachSampler
    {
        /// <summary>Ring points that have somewhere to stand. Null = every point does.</summary>
        public Func<Vector3, bool>? Standable;

        /// <summary>What the eye meets first, aiming at the target's centre.</summary>
        public Reachability.ReachHit AtCentre = Reachability.ReachHit.Target;

        /// <summary>What the eye meets first, aiming at the top of the target's bounds.</summary>
        public Reachability.ReachHit AtTop = Reachability.ReachHit.Target;

        /// <summary>Eye positions the rule asked about, in order. The rule is allowed to stop
        /// early; this is how "it stopped early" is asserted rather than assumed.</summary>
        public readonly List<Vector3> Eyes = new();

        public bool HasTarget { get; init; } = true;
        public bool TargetInsideStatic { get; init; }
        public Vector3 TargetCentre { get; init; } = new(0f, 0.22f, 0f);
        public Vector3 TargetTop { get; init; } = new(0f, 0.44f, 0f);

        public bool TryStand(Vector3 ringPoint, out Vector3 eye)
        {
            eye = default;
            if (Standable is not null && !Standable(ringPoint))
                return false;
            eye = new Vector3(ringPoint.X, 0.995f, ringPoint.Z);
            Eyes.Add(eye);
            return true;
        }

        public Reachability.ReachHit FirstHit(Vector3 from, Vector3 to) =>
            to.IsEqualApprox(TargetTop) ? AtTop : AtCentre;
    }

    /// <summary>Nowhere to stand at all.</summary>
    private static readonly Func<Vector3, bool> NoFloorAnywhere = _ => false;

    // --- §5b's six planted cases -----------------------------------------------------------------

    [Fact] // §5b case 1: inside a wall (refused)
    public void InsideAWall_IsRefusedAsInsideStatic_WithoutSamplingAnything()
    {
        var sampler = new FakeSampler { TargetInsideStatic = true };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.False(v.Reachable);
        Assert.Equal(Reachability.ReachReason.InsideStatic, v.Reason);
        // It short-circuits: an object inside a wall would ALSO report "occluded by static" from
        // every point, and that is the less useful of the two true statements.
        Assert.Empty(sampler.Eyes);
        Assert.Equal(0, v.RaysCast);
    }

    [Fact] // §5b case 2: inside a static shelf back (refused)
    public void BoxedInByStaticOnEverySide_IsRefusedAsOccludedByStatic()
    {
        var sampler = new FakeSampler
        {
            AtCentre = Reachability.ReachHit.Static,
            AtTop = Reachability.ReachHit.Static,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.False(v.Reachable);
        Assert.Equal(Reachability.ReachReason.OccludedByStatic, v.Reason);
        // Every point was tried, and both aims from each: 24 points, 48 rays.
        Assert.Equal(Reachability.RingSamples * 2, v.PointsSampled);
        Assert.Equal(Reachability.RingSamples * 2, v.PointsStandable);
        Assert.Equal(Reachability.RingSamples * 4, v.RaysCast);
    }

    [Fact] // §5b case 3: 3 m up (refused)
    public void ThreeMetresUpWithNoFloorInReach_IsRefusedAsNoStandingPoint()
    {
        var sampler = new FakeSampler
        {
            TargetCentre = new Vector3(0f, 3f, 0f),
            TargetTop = new Vector3(0f, 3.22f, 0f),
            Standable = NoFloorAnywhere,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.False(v.Reachable);
        Assert.Equal(Reachability.ReachReason.NoStandingPoint, v.Reason);
        Assert.Equal(0, v.PointsStandable);
        // Not one ray was cast. A rule that cast rays from points nobody can stand on would
        // report OccludedByStatic for a shelf that is simply too high, and send the hider off to
        // move the scenery instead of the object.
        Assert.Equal(0, v.RaysCast);
    }

    [Fact] // §5b case 4: under an overturned bin (allowed)
    public void UnderAnOverturnedBin_IsReachableBecauseTheFirstHitIsMovable()
    {
        var sampler = new FakeSampler
        {
            AtCentre = Reachability.ReachHit.MovableProp,
            AtTop = Reachability.ReachHit.MovableProp,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.True(v.Reachable);
        Assert.Equal(Reachability.ReachReason.Reachable, v.Reason);
    }

    [Fact] // §5b case 5: inside a closed movable box (allowed)
    public void InsideAClosedMovableBox_IsReachable_AndCostsOneRay()
    {
        // The defining property of this case: NOT ONE ray reaches the object, and the hide is
        // still legal. If a future "the target itself must be visible from somewhere" rule ever
        // creeps in, this is the test that goes red.
        var sampler = new FakeSampler
        {
            AtCentre = Reachability.ReachHit.MovableProp,
            AtTop = Reachability.ReachHit.MovableProp,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.True(v.Reachable);
        Assert.Equal(1, v.RaysCast);          // it stops at the first point that counts
        Assert.Single(sampler.Eyes);
    }

    [Fact] // §5b case 6: pushed through the floor, then returned to last good (allowed there)
    public void AtItsLastGoodTransformAfterARecovery_ItIsReachableAgain()
    {
        // Layer 2 owns the recovery; what layer 3 owes is that the pose it recovers TO is judged
        // on its own merits and not on where the prop had fallen to.
        var recovered = new FakeSampler
        {
            TargetCentre = new Vector3(-2f, 0.22f, 3f),
            TargetTop = new Vector3(-2f, 0.44f, 3f),
            AtCentre = Reachability.ReachHit.Target,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(recovered);

        Assert.True(v.Reachable);
        Assert.Equal(Reachability.ReachReason.Reachable, v.Reason);
    }

    // --- the packet's two extra cases -------------------------------------------------------------

    [Fact]
    public void UnderThreeStackedMovableProps_IsReachable()
    {
        // "Buried under ten movable items is legal; that is the game." Three is the packet's
        // number and the rule does not count them — one movable first hit is the whole test,
        // because the seeker moves them one at a time either way.
        var sampler = new FakeSampler
        {
            AtCentre = Reachability.ReachHit.MovableProp,
            AtTop = Reachability.ReachHit.MovableProp,
        };

        Assert.True(Reachability.Evaluate(sampler).Reachable);
    }

    [Fact]
    public void OnATwoPointFourMetreShelfWithNoFloorInReach_IsNoStandingPoint()
    {
        var sampler = new FakeSampler
        {
            TargetCentre = new Vector3(0f, 2.4f, 0f),
            TargetTop = new Vector3(0f, 2.62f, 0f),
            Standable = NoFloorAnywhere,
            // Deliberately "the target is right there" — so that if the rule ever stopped
            // requiring a standing point, this would go green for the wrong reason and the
            // assertion on the REASON below is what catches it.
            AtCentre = Reachability.ReachHit.Target,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.False(v.Reachable);
        Assert.Equal(Reachability.ReachReason.NoStandingPoint, v.Reason);
    }

    // --- the rule's own edges ----------------------------------------------------------------------

    [Fact]
    public void NoTarget_IsNotARefusal_ItIsNoTarget()
    {
        // The fact source maps this onto a null TargetRetrievable, which the loop reads as
        // "nobody measured it". A refusal here would refuse every Confirm in the holding room.
        Reachability.ReachVerdict v = Reachability.Evaluate(new FakeSampler { HasTarget = false });

        Assert.False(v.Reachable);
        Assert.Equal(Reachability.ReachReason.NoTarget, v.Reason);
    }

    [Fact]
    public void ANullSampler_AnswersNoTargetRatherThanThrowing()
    {
        Reachability.ReachVerdict v = Reachability.Evaluate(null!);

        Assert.Equal(Reachability.ReachReason.NoTarget, v.Reason);
    }

    [Fact]
    public void OneStandingPointOutOfTwentyFourIsEnough()
    {
        // A seeker walks around. The rule is deliberately permissive about WHICH side works and
        // strict about whether a body fits there at all.
        int seen = 0;
        var sampler = new FakeSampler
        {
            Standable = _ => ++seen == 24,   // only the very last candidate
            AtCentre = Reachability.ReachHit.Target,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.True(v.Reachable);
        Assert.Equal(24, v.PointsSampled);
        Assert.Equal(1, v.PointsStandable);
    }

    [Fact]
    public void TheTopOfTheBoundsIsAimedAtWhenTheCentreIsBlocked()
    {
        // A crate on a low shelf is routinely occluded at its centre and open at its top edge.
        // Refusing that would refuse the most ordinary legal hide in the game.
        var sampler = new FakeSampler
        {
            AtCentre = Reachability.ReachHit.Static,
            AtTop = Reachability.ReachHit.Target,
        };

        Reachability.ReachVerdict v = Reachability.Evaluate(sampler);

        Assert.True(v.Reachable);
        Assert.Equal(2, v.RaysCast);   // centre refused, top accepted, then it stops
    }

    [Fact]
    public void AClearLineWithNoHitAtAllCounts()
    {
        // Nothing between the eye and the object is the STRONGEST form of reachable, not an
        // ambiguous one — "a first hit on static geometry does not count" is the rule, and there
        // was no first hit.
        var sampler = new FakeSampler { AtCentre = Reachability.ReachHit.Nothing };

        Assert.True(Reachability.Evaluate(sampler).Reachable);
    }

    // --- the ring ------------------------------------------------------------------------------------

    [Fact]
    public void TwoRingsOfTwelve_WideFirst_AtTheTargetsOwnHeight()
    {
        var centre = new Vector3(3f, 1.4f, -2f);

        var points = new List<Vector3>(Reachability.RingPoints(centre));

        Assert.Equal(Reachability.RingSamples * 2, points.Count);
        // Every point sits at the TARGET's height, not on the floor: that is what makes "3 m up
        // with the floor 3 m below" answer NoStandingPoint instead of quietly finding the ground
        // and then reporting an occlusion.
        Assert.All(points, p => Assert.True(Mathf.Abs(centre.Y - p.Y) < 1e-4f,
            $"ring point y={p.Y} is not at the target's height {centre.Y}"));

        for (int i = 0; i < Reachability.RingSamples; i++)
            Assert.True(Mathf.Abs(Reachability.GrabReachM - Flat(points[i], centre)) < 1e-3f,
                $"outer ring point {i} is {Flat(points[i], centre)} m out, expected {Reachability.GrabReachM}");
        for (int i = Reachability.RingSamples; i < points.Count; i++)
            Assert.True(Mathf.Abs(Reachability.GrabReachM * 0.5f - Flat(points[i], centre)) < 1e-3f,
                $"inner ring point {i} is {Flat(points[i], centre)} m out, expected {Reachability.GrabReachM * 0.5f}");
    }

    [Fact]
    public void GrabReachIsTheGrabRadiusPlusTheHandsReach_AndNotTheLagTolerance()
    {
        // 1.5 m (SandboxAvatar.PickupRadius, the body-to-prop distance the server resolves a grab
        // at) + 0.9 m (PropManager.PlaceReachM, how far in front of the body the spring holds a
        // prop) = 2.4 m. NOT + PropManager.GrabRangeTolerance: that 0.75 m is slack for a round
        // trip of lag on a live grab, and folding it in here would make the audit believe in a
        // seeker with 3.15 m arms and pass hides nobody can undo.
        Assert.True(Mathf.Abs(2.4f - Reachability.GrabReachM) < 1e-4f,
            $"GrabReachM is {Reachability.GrabReachM} m, expected 1.5 + 0.9 = 2.4 m");
    }

    [Fact]
    public void OnlyTheTargetAndMovablePropsCount()
    {
        Assert.True(Reachability.Counts(Reachability.ReachHit.Target));
        Assert.True(Reachability.Counts(Reachability.ReachHit.MovableProp));
        Assert.True(Reachability.Counts(Reachability.ReachHit.Nothing));
        Assert.False(Reachability.Counts(Reachability.ReachHit.Static));
    }

    [Fact]
    public void EveryRefusalHasASentenceAndAReachableVerdictHasNone()
    {
        foreach (Reachability.ReachReason reason in Enum.GetValues<Reachability.ReachReason>())
        {
            var v = new Reachability.ReachVerdict(
                reason == Reachability.ReachReason.Reachable, reason, 0, 0, 0, 0);
            if (reason == Reachability.ReachReason.Reachable)
                Assert.Equal(string.Empty, v.Sentence);
            else
                Assert.False(string.IsNullOrWhiteSpace(v.Sentence));
        }
    }

    private static float Flat(Vector3 a, Vector3 b) =>
        new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
