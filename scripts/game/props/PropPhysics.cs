using System;
using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>The arithmetic behind "dominoes fall, cans roll, nothing freaks out"</b> — PHYS-1,
/// 2026-09-20, rulings P1 and P2.
///
/// <para>Engine-free apart from <c>Vector3</c>/<c>Mathf</c>, which are ordinary managed struct
/// math in GodotSharp.dll, so <c>tests/unit/PropPhysicsTests.cs</c> drives every branch with no
/// engine present. The split is <c>Reachability</c>'s from <c>PhysicsReachSampler</c> and
/// <c>CarryIdle</c>'s from <c>Carryable</c>, for the same reason: a number that decides whether
/// a shelf of cans goes over is a number a test has to be able to hold.</para>
///
/// <para><b>Talon, 2026-09-20, which is this file's whole specification:</b> <i>"I want to
/// manipulate objects and know they cannot clip through walls, the floor, or other objects. I
/// want to know they won't freak out and make other objects jump around randomly. Make it do
/// what the player expects: if they're placing an object on a shelf and accidentally hit a bunch
/// of boxes, those boxes should fall over like dominoes, and cans should roll around."</i></para>
/// </summary>
public static class PropPhysics
{
    // --- P2: bounded energy ------------------------------------------------------------------

    /// <summary><b>The bar, in one constant</b> (program ruling R6): no prop that is not in a
    /// hand may travel faster than this after an interaction. 3 m/s is a fraction over the
    /// browse pace (<c>MpFoundation.Game.BrowsePace</c>, 2.4 m/s), which is the honest ceiling
    /// for "as fast as a player can shove it": a held prop's own step is already capped at twice
    /// the walk by <c>CarryHold.MaxHoldSpeedMps</c>, and nothing a carry can do should launch
    /// what it hits faster than the carry itself could move.</summary>
    public const float MaxPropSpeedMps = 3.0f;

    /// <summary><b>Gravity is not an interaction, and clamping it would be a lie.</b> A prop
    /// knocked off a 1.5 m shelf is doing 5.4 m/s by the time it lands, and a 3 m/s ceiling
    /// applied to the whole velocity would play every fall in slow motion — the opposite of
    /// "physics that does what the player expects".
    ///
    /// <para>So the cap is applied to the components an INTERACTION can write and not to the one
    /// gravity owns: the horizontal speed and any UPWARD speed are held to
    /// <see cref="MaxPropSpeedMps"/>, and the downward component is held only to this terminal
    /// figure — far above any drop inside a supermarket, and there purely so a prop that
    /// somehow ends up accelerating forever cannot tunnel out of the world between two
    /// ticks.</para></summary>
    public const float MaxPropFallSpeedMps = 12.0f;

    /// <summary>A prop this build considers to be spinning out of control, rad/s. A can rolling
    /// along the floor turns at v/r — at 1 m/s on a 35 mm radius that is 28 rad/s, so this
    /// ceiling is deliberately well above an honest roll and only ever catches a solver
    /// explosion.</summary>
    public const float MaxPropSpinRadPerSec = 40.0f;

    /// <summary><b>The one exception P2 names.</b> A throw is allowed to be faster than the
    /// bar, up to CARRY-1's own throw speed (<c>PropManager.ThrowForwardSpeed</c>, 7.5 m/s) plus
    /// its upward component — the cap a thrown prop carries until it has slowed to
    /// <see cref="MaxPropSpeedMps"/> once, at which point it latches down to the ordinary bar and
    /// never rises again. A latch rather than a timer: the interesting quantity is "has this
    /// episode's launch energy been spent", and a clock would have to be guessed at.</summary>
    public const float ThrowSpeedCapMps = 8.5f;

    /// <summary>
    /// <b>Clamp one prop's velocity, and say whether the clamp bit.</b> Horizontal and upward to
    /// <paramref name="capMps"/>, downward to <see cref="MaxPropFallSpeedMps"/>.
    /// </summary>
    /// <param name="velocity">The body's velocity this tick.</param>
    /// <param name="capMps">This prop's current ceiling — <see cref="MaxPropSpeedMps"/>
    /// ordinarily, <see cref="ThrowSpeedCapMps"/> while a throw's launch energy is unspent.</param>
    /// <param name="clamped">The velocity to write back. Equals <paramref name="velocity"/> when
    /// nothing bit.</param>
    /// <returns>True when the velocity was actually reduced.</returns>
    public static bool ClampVelocity(Vector3 velocity, float capMps, out Vector3 clamped)
    {
        var horizontal = new Vector3(velocity.X, 0f, velocity.Z);
        float h = horizontal.Length();
        float y = velocity.Y;
        bool bit = false;

        if (h > capMps)
        {
            horizontal *= capMps / h;
            bit = true;
        }
        if (y > capMps)
        {
            y = capMps;
            bit = true;
        }
        else if (y < -MaxPropFallSpeedMps)
        {
            y = -MaxPropFallSpeedMps;
            bit = true;
        }

        clamped = new Vector3(horizontal.X, y, horizontal.Z);
        return bit;
    }

    /// <summary>Clamp one prop's angular velocity. Returns true when it bit.</summary>
    public static bool ClampSpin(Vector3 angular, out Vector3 clamped)
    {
        float w = angular.Length();
        if (w <= MaxPropSpinRadPerSec || w <= 0f)
        {
            clamped = angular;
            return false;
        }
        clamped = angular * (MaxPropSpinRadPerSec / w);
        return true;
    }

    /// <summary><b>Has this throw's launch energy been spent?</b> A prop released as a throw
    /// carries <see cref="ThrowSpeedCapMps"/> until the first tick its HORIZONTAL speed is back
    /// inside the ordinary bar; from then on it is an ordinary loose prop. Reading the horizontal
    /// component rather than the whole speed is what stops a throw that is mostly falling from
    /// latching early and being clamped mid-arc.</summary>
    public static bool ThrowEnergySpent(Vector3 velocity) =>
        new Vector3(velocity.X, 0f, velocity.Z).LengthSquared()
            <= MaxPropSpeedMps * MaxPropSpeedMps;

    // --- P1: wake on contact -----------------------------------------------------------------

    /// <summary><b>How fast something has to be travelling into a resting prop before the prop
    /// wakes at all.</b> Below this nothing happens and the stack stays a stack — which is half
    /// of P5: a row of boxes that woke on every stray millimetre of spring jitter could never be
    /// "untouched for 30 s, zero movement".
    ///
    /// <para>0.35 m/s is a seventh of the browse pace, so anything a player does on purpose
    /// clears it comfortably, while a held prop's steady-state spring residual (FEEL-1 measured
    /// mean lag 0.223 m at a constant walk, i.e. a settled offset rather than a velocity) does
    /// not. A GRAZE clears it only in the component that matters: the approach speed below is
    /// taken along the contact normal, so walking ALONG a shelf face contributes almost nothing
    /// and walking INTO it contributes all of it.</para></summary>
    public const float WakeSpeedThresholdMps = 0.35f;

    /// <summary><b>How bouncy a prop-on-prop shove is</b>, 0 = perfectly inelastic, 1 = perfectly
    /// elastic. 0.2: cardboard, tin and fruit in a supermarket are all closer to dead than to
    /// springy, and the value's job is to make the first box move rather than to model a
    /// collision properly — the solver takes over the instant the prop is awake.</summary>
    public const float ContactRestitution = 0.2f;

    /// <summary>
    /// <b>The speed a resting prop is woken at</b>, from the one-dimensional collision along the
    /// contact normal: <c>(1 + e)·m·v / (m + M)</c>, clamped to <see cref="MaxPropSpeedMps"/>.
    ///
    /// <para>The clamp is here, at the impulse, and that is P2's real guarantee — the per-tick
    /// clamp in the server loop is the backstop for a solver that finds energy of its own, but
    /// nothing this build DOES to a prop can hand it more than the bar in the first place.</para>
    ///
    /// <para><b>The mover's own mass, with no allowance for the arm behind a held one.</b> A held
    /// crate is driven by a spring, not by a player's shoulder, and the spring's own step is
    /// already speed-capped; crediting the holder's body mass would let a 2.4 m/s walk deliver
    /// the full bar to everything it brushed. At the reference numbers — a 1 kg crate at the
    /// browse pace into a 0.4 kg cereal box — this returns 2.06 m/s, which topples the box and
    /// leaves a third of the bar unspent.</para>
    /// </summary>
    /// <param name="moverMassKg">Mass of the thing doing the hitting.</param>
    /// <param name="targetMassKg">Mass of the resting prop.</param>
    /// <param name="approachSpeedMps">Closing speed along the contact normal. Negative or zero
    /// means the bodies are separating and returns 0.</param>
    public static float WakeSpeed(float moverMassKg, float targetMassKg, float approachSpeedMps)
    {
        if (approachSpeedMps <= 0f || targetMassKg <= 0f)
            return 0f;
        // A mover with no mass of its own (a kinematic body a level author never weighed) still
        // has to be able to push: treat it as the target's equal rather than as a ghost.
        float m = moverMassKg > 0f ? moverMassKg : targetMassKg;
        float v = (1f + ContactRestitution) * m * approachSpeedMps / (m + targetMassKg);
        return Mathf.Min(v, MaxPropSpeedMps);
    }

    /// <summary>Whether a contact at <paramref name="approachSpeedMps"/> is worth waking a
    /// resting prop for at all. See <see cref="WakeSpeedThresholdMps"/>.</summary>
    public static bool ShouldWake(float approachSpeedMps) =>
        approachSpeedMps >= WakeSpeedThresholdMps;

    /// <summary>
    /// <b>The impulse to apply, as a vector.</b> Direction is the contact normal
    /// (<paramref name="pushDirection"/>, normalised here so a caller may hand it an unnormalised
    /// difference of positions); magnitude is <c>M·v'</c> for the <see cref="WakeSpeed"/> above,
    /// because Godot's <c>ApplyImpulse</c> takes momentum and divides by the mass itself.
    ///
    /// <para>Applied at the CONTACT POINT rather than at the centre of mass by the caller, and
    /// that is what makes P4's dominoes fall instead of sliding: a cereal box nudged near its top
    /// edge gets the torque that tips it, and one nudged at the base gets a shove. The arithmetic
    /// here is the same either way — the lever is the caller's geometry.</para>
    /// </summary>
    public static Vector3 ContactImpulse(Vector3 pushDirection, float moverMassKg,
        float targetMassKg, float approachSpeedMps)
    {
        float len = pushDirection.Length();
        if (len <= 0f)
            return Vector3.Zero;
        float v = WakeSpeed(moverMassKg, targetMassKg, approachSpeedMps);
        if (v <= 0f)
            return Vector3.Zero;
        return pushDirection / len * (v * targetMassKg);
    }

    /// <summary>
    /// <b>The closing speed along the contact normal</b>, from two bodies' velocities. Positive
    /// means approaching. A graze returns near zero however fast the mover is going, which is the
    /// property <see cref="WakeSpeedThresholdMps"/> leans on.
    /// </summary>
    /// <param name="moverVelocity">The moving body's velocity.</param>
    /// <param name="targetVelocity">The other body's velocity — <c>Vector3.Zero</c> for a resting
    /// prop, which is every caller in this build, but taken as a parameter so the quantity is
    /// the real relative one rather than an assumption.</param>
    /// <param name="normalTowardTarget">Unit vector from the mover toward the target. Not
    /// required to be normalised.</param>
    public static float ApproachSpeed(Vector3 moverVelocity, Vector3 targetVelocity,
        Vector3 normalTowardTarget)
    {
        float len = normalTowardTarget.Length();
        if (len <= 0f)
            return 0f;
        return (moverVelocity - targetVelocity).Dot(normalTowardTarget / len);
    }

    /// <summary>
    /// <b>Where a box-shaped mover reaches furthest toward what it struck</b> -- the point the
    /// wake impulse is applied at (PHYS-2, 2026-09-21).
    ///
    /// <para><b>A domino is knocked over by its neighbour's TOP EDGE, and an impulse through a
    /// box's centre cannot topple anything.</b> <c>body_entered</c> carries no contact point, and
    /// the funnel applied every prop-on-prop wake at the struck prop's centre -- pure translation
    /// -- so a row of cereal boxes shoved at 2.8 m/s shuffled 3 cm each and stood (measured:
    /// <c>wake prop=2 at=2.77 -> 1.66</c>, <c>prop=3 1.65 -> 0.99</c>, <c>prop=4 0.57 -> 0.34</c>,
    /// not one past 60 degrees). The mover's support point along the line of centres is the
    /// honest approximation: a toppling box leads with its top-front edge, and that is high on the
    /// neighbour; a box sliding face-first ties on all four front corners and their mean is the
    /// face's centre, which is the flat contact it really is. <paramref name="tieM"/> is what
    /// "ties" means -- a centimetre, under any prop's half-depth.</para>
    /// </summary>
    public static Vector3 StrikePoint(Transform3D moverAt, Vector3 moverHalf, Vector3 toward,
        float tieM = 0.01f)
    {
        if (toward.LengthSquared() <= 1e-8f)
            return moverAt.Origin;
        Vector3 n = toward.Normalized();
        Span<Vector3> corners = stackalloc Vector3[8];
        Span<float> along = stackalloc float[8];
        float best = float.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var local = new Vector3(
                (i & 1) == 0 ? -moverHalf.X : moverHalf.X,
                (i & 2) == 0 ? -moverHalf.Y : moverHalf.Y,
                (i & 4) == 0 ? -moverHalf.Z : moverHalf.Z);
            corners[i] = moverAt * local;
            along[i] = corners[i].Dot(n);
            if (along[i] > best)
                best = along[i];
        }
        Vector3 sum = Vector3.Zero;
        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            if (along[i] >= best - tieM)
            {
                sum += corners[i];
                count++;
            }
        }
        return sum / count;
    }

    /// <summary>
    /// <b>Half-extents of an oriented box along the axes of another frame</b> -- the AABB of an
    /// OBB, <c>ext_i = sum_j |B_ij| half_j</c>. Identical to sweeping the eight corners, without
    /// the eight transforms.
    /// </summary>
    public static Vector3 BoxExtents(Basis basis, Vector3 half) => new(
        Mathf.Abs(basis.Row0.X) * half.X + Mathf.Abs(basis.Row0.Y) * half.Y + Mathf.Abs(basis.Row0.Z) * half.Z,
        Mathf.Abs(basis.Row1.X) * half.X + Mathf.Abs(basis.Row1.Y) * half.Y + Mathf.Abs(basis.Row1.Z) * half.Z,
        Mathf.Abs(basis.Row2.X) * half.X + Mathf.Abs(basis.Row2.Y) * half.Y + Mathf.Abs(basis.Row2.Z) * half.Z);

    /// <summary>
    /// <b>Half-extents of a round shape -- a cylinder, a capsule, a sphere -- along the axes of
    /// another frame.</b> <paramref name="axis"/> is the shape's own axis expressed in that
    /// frame (unit), <paramref name="halfLen"/> the half-length of the straight part (a cylinder's
    /// half-height; a capsule's half-height minus its radius; a sphere's zero), and the extent
    /// along any axis is <c>|a_i| halfLen + r sqrt(1 - a_i^2)</c> for FLAT ends (a cylinder: the
    /// rim of a tilted disc) and <c>|a_i| halfLen + r</c> for ROUND ends (a capsule, and a sphere
    /// is a capsule of zero length -- its extent is its radius whichever way it is turned).
    ///
    /// <para><b>PHYS-2 (2026-09-21): this is what stops a rolling can or a rolled orange being
    /// teleported home for lying on the floor.</b> REACH-1's bounds test swept the eight corners
    /// of the shape's LOCAL bounding box, which for anything round overstates the extent by up to
    /// sqrt(2) (a cylinder about its own axis) or sqrt(3) (a sphere) as the body rotates -- and a
    /// body that rolls rotates. A can lying on the floor sits 1.2 cm into it (the solver's rest
    /// penetration for a cylinder on its side, measured on every run) and its true bottom is
    /// 0.8 cm inside the bounds slack; its corner-swept bottom at 45 degrees is 2.6 cm outside it,
    /// and that read as OutOfBounds -> RestoredLastGood: the can jumped back to where it had
    /// been, mid-run, in front of the camera. <c>Run-PhysicsFeelTest</c> caught the can; the
    /// same run caught authored produce 1135 -- a sphere -- being restored 0.4 m for the same
    /// reason, which is the shipped game doing it to a player's orange.</para>
    /// </summary>
    public static Vector3 RoundExtents(Vector3 axis, float halfLen, float radius, bool roundEnds)
    {
        Vector3 e = Vector3.Zero;
        e.X = Mathf.Abs(axis.X) * halfLen + radius * (roundEnds ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - axis.X * axis.X)));
        e.Y = Mathf.Abs(axis.Y) * halfLen + radius * (roundEnds ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - axis.Y * axis.Y)));
        e.Z = Mathf.Abs(axis.Z) * halfLen + radius * (roundEnds ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - axis.Z * axis.Z)));
        return e;
    }
}
