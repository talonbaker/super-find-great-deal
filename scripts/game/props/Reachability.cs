using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>Placement integrity, layer 3</b> — "could a seeker actually walk up to this and get it?"
/// (program doc §5b, Talon's ruling of 2026-09-19: <i>the hidden object is never clipped, out of
/// bounds, or unreachable by accident</i>).
///
/// <para><b>Why this is a rule and not a physics routine.</b> Everything below is arithmetic over
/// an interface (<see cref="IReachSampler"/>) that an adapter fills with engine queries, so the
/// DECISION — how many points, where, which hit counts, what the refusal is called — is xUnit
/// testable without an engine, a world, or a running server. The engine half
/// (<see cref="PhysicsReachSampler"/>) is three query calls and no judgement. That split is the
/// same one <c>HideSeekLoop</c> has from <c>HideSeekDriver</c>, for the same reason: the seam
/// where a mistake is expensive is the seam a test has to be able to hold.</para>
///
/// <para><b>What "reachable" means here, exactly.</b> Stand on the walkable floor within
/// <see cref="GrabReachM"/> of the object; look at it; the first thing your eye meets is either
/// the object itself or something you can pick up and move out of the way. That is the whole
/// rule. <b>Buried under ten movable items is legal — that is the game.</b> Sealed inside static
/// geometry, or up where nobody can stand, is not.</para>
///
/// <para><b>It is deliberately permissive about occlusion and strict about geometry.</b> ONE
/// standing point out of twenty-four is enough, because a seeker walks around; but a point only
/// counts if a body actually fits there (a capsule cast, not a floor ray alone), because a
/// "standing point" inside a shelf is not somewhere a seeker can be.</para>
/// </summary>
public static class Reachability
{
    /// <summary>Standing points per ring. Twelve is one every 30°, which is the coarsest ring
    /// that cannot miss a doorway-sized gap at <see cref="GrabReachM"/>: the arc between two
    /// adjacent points is 2·R·sin(15°) ≈ 1.24 m at R = 2.4 m, narrower than the 1.5 m aisle the
    /// search room is authored around. Both rings use the same count.</summary>
    public const int RingSamples = 12;

    /// <summary>
    /// Ring radius, metres — <b>CARRY-1's grab range plus the hand's own reach</b>, which is the
    /// packet's definition and is the sum of two constants that already exist rather than a third
    /// number nobody maintains:
    /// <see cref="Sandbox.SandboxAvatar.PickupRadius"/> (1.5 m, how far from a BODY the server
    /// will let a grab resolve) + <see cref="PropManager.PlaceReachM"/> (0.9 m, how far in front
    /// of that body the spring holds a prop).
    ///
    /// <para>Note what is NOT added: <c>PropManager.GrabRangeTolerance</c>. That 0.75 m is slack
    /// for a round trip of lag on a live grab, not reach a player has — folding it in here would
    /// make the audit believe in a seeker with 3.15 m arms and pass hides nobody can undo.</para>
    /// </summary>
    public const float GrabReachM =
        Sandbox.SandboxAvatar.PickupRadius + PropManager.PlaceReachM;

    /// <summary>How far below a candidate ring point the floor may be and still be the floor the
    /// seeker stands on, metres. A step down is fine; a storey is not. 1.5 m is what makes "3 m
    /// up" and "the 2.4 m top shelf" answer <see cref="ReachReason.NoStandingPoint"/> rather than
    /// finding the floor far below and then reporting an occlusion.</summary>
    public const float FloorProbeM = 1.5f;

    /// <summary>What the eye is looking at from a standing point. Both are cast, and the point
    /// counts if EITHER lands — a crate on a low shelf is often occluded at its centre and open
    /// at its top edge, and refusing that would refuse the most ordinary legal hide in the
    /// game.</summary>
    public enum Aim
    {
        /// <summary>The target's centre.</summary>
        Centre = 0,

        /// <summary>The top of the target's bounds.</summary>
        Top = 1,
    }

    /// <summary>Why a target is not reachable. Ordinals are stable but do NOT ride the wire —
    /// the refusal a player sees is <c>HideSeekRefusal.NobodyCouldReachThat</c>, one sentence for
    /// all three; this is what the server log and the handoff say.</summary>
    public enum ReachReason
    {
        /// <summary>Reachable. The resting value, so a default verdict is not a phantom
        /// refusal.</summary>
        Reachable = 0,

        /// <summary>The object is inside static geometry — layer 2 failed too and could not
        /// correct it. This is the worst case the design has and the one the loud log line names.
        /// </summary>
        InsideStatic = 1,

        /// <summary>Nowhere to stand: too high, under a floor, or boxed in by static geometry so
        /// tightly that no body fits on any of the twenty-four candidate points.</summary>
        NoStandingPoint = 2,

        /// <summary>A seeker can stand near it, but from every one of those places the first
        /// thing the eye meets is static geometry — a shelf back, a wall, a cabinet door that
        /// does not move.</summary>
        OccludedByStatic = 3,

        /// <summary>There is no target to evaluate. Not a refusal: the fact source answers
        /// <c>null</c> on this, which is how "nobody has measured it" stays distinguishable from
        /// "no" (see <c>IRoundFactSource.TargetRetrievable</c>).</summary>
        NoTarget = 4,
    }

    /// <summary>What a standing point's eye ray met first.</summary>
    public enum ReachHit
    {
        /// <summary>The ray reached the target's position without hitting anything at all. Counts
        /// as a hit ON the target: an empty ray to a point inside the object's own volume means
        /// the query started inside it or the shape is not solid to rays, and calling that
        /// "occluded" would refuse a hide for a reason that is about the query, not the
        /// world.</summary>
        Nothing = 0,

        /// <summary>The target itself.</summary>
        Target = 1,

        /// <summary>A movable prop — a bin, a box, a lid, another crate. The seeker picks it up
        /// or knocks it aside. <b>This is the case §5b exists to protect.</b></summary>
        MovableProp = 2,

        /// <summary>Static geometry.</summary>
        Static = 3,
    }

    /// <summary>
    /// One reachability answer, with the counts behind it. The counts are not decoration: a
    /// verdict that says "unreachable" and a verdict that says "unreachable, and by the way zero
    /// of twenty-four candidate points had a floor under them" are the same boolean and very
    /// different bug reports, and the second is the one a level author can act on.
    /// </summary>
    /// <param name="Winner">What the winning ray met first, or <see cref="ReachHit.Static"/>
    /// when nothing won. <b>This is the fact §5b actually states</b> — "the point counts if the
    /// first hit is the target or a movable prop" — and a verdict that only carried the boolean
    /// could not tell "reachable because the seeker can see it" from "reachable because the bin
    /// on top of it is something they can move". The planted self-test asserts it, which is what
    /// stops the bin case passing on a room where the bin was never authored.</param>
    public readonly record struct ReachVerdict(
        bool Reachable,
        ReachReason Reason,
        int PointsSampled,
        int PointsStandable,
        int PointsCounting,
        int RaysCast,
        ReachHit Winner = ReachHit.Static)
    {
        /// <summary>The line the server log and the self-test print.</summary>
        public override string ToString() =>
            $"{(Reachable ? "reachable" : "UNREACHABLE")} ({Reason}) — "
            + $"{PointsCounting}/{PointsStandable} standable of {PointsSampled} sampled, "
            + $"{RaysCast} ray(s)"
            + (Reachable ? $", first hit {Winner}" : "");

        /// <summary>The player-facing half, for the refusal the round carries. One sentence for
        /// every reason on purpose: the hider needs "move it", not a physics tutorial.</summary>
        public string Sentence => Reachable ? string.Empty : Reason switch
        {
            ReachReason.InsideStatic => "it is inside the scenery — move it",
            ReachReason.NoStandingPoint => "nobody can stand near it — move it lower",
            ReachReason.OccludedByStatic => "the scenery is in the way — move it",
            _ => "nobody could reach that — move it",
        };
    }

    /// <summary>
    /// <b>The engine half, as an interface.</b> An adapter answers these with physics queries;
    /// a test answers them from a table. Nothing here decides anything.
    /// </summary>
    public interface IReachSampler
    {
        /// <summary>Is there a target at all?</summary>
        bool HasTarget { get; }

        /// <summary>Layer 2's verdict on the target where it currently sits: true when the object
        /// is inside static geometry and the rest audit could not correct it. Reported rather
        /// than re-derived, so the two layers can never disagree about the same object.</summary>
        bool TargetInsideStatic { get; }

        /// <summary>The target's centre, world space.</summary>
        Vector3 TargetCentre { get; }

        /// <summary>The top of the target's bounds, world space.</summary>
        Vector3 TargetTop { get; }

        /// <summary>Can a player stand at (or just below) <paramref name="ringPoint"/>? True with
        /// <paramref name="eye"/> set to where that player's eye would be. False when there is no
        /// walkable floor within <see cref="FloorProbeM"/> or no room for a body there.</summary>
        bool TryStand(Vector3 ringPoint, out Vector3 eye);

        /// <summary>What an eye at <paramref name="from"/> meets first looking at
        /// <paramref name="to"/>.</summary>
        ReachHit FirstHit(Vector3 from, Vector3 to);
    }

    /// <summary>
    /// Evaluate the rule. Deterministic, allocation-light, and it stops at the first point that
    /// counts — the ordinary legal hide costs two queries, not forty-eight.
    ///
    /// <para><b>Order of the refusals is load-bearing.</b> <see cref="ReachReason.InsideStatic"/>
    /// is checked before anything is sampled, because an object inside a wall would also report
    /// "occluded by static" from every point and that is the less useful of the two true
    /// statements. <see cref="ReachReason.NoStandingPoint"/> outranks
    /// <see cref="ReachReason.OccludedByStatic"/> for the same reason: "there is nowhere to
    /// stand" tells the hider to move it DOWN, "the scenery is in the way" tells them to move it
    /// OUT, and handing them the wrong instruction is how a 30-second hiding phase is spent
    /// fighting the game.</para>
    /// </summary>
    public static ReachVerdict Evaluate(IReachSampler sampler)
    {
        if (sampler is null || !sampler.HasTarget)
            return new ReachVerdict(false, ReachReason.NoTarget, 0, 0, 0, 0);

        if (sampler.TargetInsideStatic)
            return new ReachVerdict(false, ReachReason.InsideStatic, 0, 0, 0, 0);

        Vector3 centre = sampler.TargetCentre;
        Vector3 top = sampler.TargetTop;

        int sampled = 0, standable = 0, rays = 0;
        foreach (Vector3 ringPoint in RingPoints(centre))
        {
            sampled++;
            if (!sampler.TryStand(ringPoint, out Vector3 eye))
                continue;
            standable++;

            rays++;
            ReachHit atCentre = sampler.FirstHit(eye, centre);
            if (Counts(atCentre))
                return new ReachVerdict(true, ReachReason.Reachable, sampled, standable, 1, rays,
                    atCentre);

            rays++;
            ReachHit atTop = sampler.FirstHit(eye, top);
            if (Counts(atTop))
                return new ReachVerdict(true, ReachReason.Reachable, sampled, standable, 1, rays,
                    atTop);
        }

        ReachReason why = standable == 0 ? ReachReason.NoStandingPoint : ReachReason.OccludedByStatic;
        return new ReachVerdict(false, why, sampled, standable, 0, rays);
    }

    /// <summary>The first hit counts iff it is the target or something the seeker can move.</summary>
    public static bool Counts(ReachHit hit) =>
        hit is ReachHit.Target or ReachHit.MovableProp or ReachHit.Nothing;

    /// <summary>
    /// The twenty-four candidate points: <see cref="RingSamples"/> around
    /// <paramref name="centre"/> at <see cref="GrabReachM"/>, then the same count at half that.
    ///
    /// <para><b>Both rings, and the wide one first.</b> The half ring catches the target tucked
    /// into a corner where the wide ring is all inside walls; the wide ring catches the target on
    /// an open floor where the half ring is inside the shelf the target is leaning against. Wide
    /// first because that is the commoner win and the loop returns on the first point that
    /// counts.</para>
    ///
    /// <para>Points are generated at the TARGET'S height, not at floor level — the floor is what
    /// <see cref="IReachSampler.TryStand"/> goes looking for, and starting the probe at the
    /// target's own height is what makes "3 m up with the floor 3 m below" answer
    /// <see cref="ReachReason.NoStandingPoint"/> instead of quietly finding the ground floor and
    /// then reporting an occlusion.</para>
    /// </summary>
    public static IEnumerable<Vector3> RingPoints(Vector3 centre)
    {
        for (int ring = 0; ring < 2; ring++)
        {
            float radius = ring == 0 ? GrabReachM : GrabReachM * 0.5f;
            for (int i = 0; i < RingSamples; i++)
            {
                float a = Mathf.Tau * i / RingSamples;
                yield return new Vector3(
                    centre.X + Mathf.Cos(a) * radius,
                    centre.Y,
                    centre.Z + Mathf.Sin(a) * radius);
            }
        }
    }
}
