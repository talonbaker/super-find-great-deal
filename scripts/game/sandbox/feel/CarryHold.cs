using Godot;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// <b>THE HOLD, AS MATHS</b> (FEEL-1, 2026-09-20). Where a held prop's target is, how near the
/// holder it may ever come, how far the wheel may push it, how fast the spring has to chase it,
/// and when the hold breaks. <see cref="CarrySpring"/> is still the chase; this is everything
/// that decides what the chase is chasing.
///
/// <para><b>Why it exists.</b> Talon's first ride: <i>"There is still an extreme issue with how
/// items are picked up: if the player moves, the item clips into their body ... it shouldn't snap
/// to any location; that's why there's physics and collision on the objects. I'd like a scroll
/// wheel to move the object forward and back in space."</i> The carry before this packet snapped
/// the prop to a chest-height anchor with <c>CollisionMask = 0</c> — it collided with nothing at
/// all, and the spring's lag walked it straight through the holder. Every number below is
/// DERIVED from the holder's own capsule and the prop's own bulk, never typed, because a can and
/// a crate cannot share a safe hold distance and a constant would be wrong for one of them.</para>
///
/// <para><b>Engine-light on purpose</b>, exactly like <see cref="CarrySpring"/>: plain statics
/// over Godot's value types, no node, no physics server, so the Godot-free suite can pin every
/// derivation and a caller decides what to do with the answer.</para>
/// </summary>
public static class CarryHold
{
    /// <summary>The gap left between the prop's nearest face and the holder's capsule, metres.
    /// Small enough that the object still reads as being in your hands, big enough that a
    /// rounding error in either radius cannot close it.</summary>
    public const float HolderSkinM = 0.05f;

    /// <summary>How far out the wheel may push an ordinary prop, metres, measured from the eye
    /// along the view ray.
    ///
    /// <para><b>1.2 m, and it is a reach rather than a taste.</b> It is about an arm plus the
    /// forearm-length the hold already sits at, it keeps the object inside the frame at a 75°
    /// lens (<c>FirstPersonCamera.DefaultFovDeg</c>), and it is the number
    /// <c>PropManager</c>'s place reach is derived FROM rather than checked against — see the
    /// place-reach comment there. Past this a carry stops being a carry and becomes
    /// telekinesis, which is the thing the old 0.9 m place reach existed to refuse.</para></summary>
    public const float HoldMaxBaseM = 1.2f;

    /// <summary>The scroll band a prop is guaranteed, metres, however fat it is. A prop whose
    /// <see cref="HoldMinM"/> is already past <see cref="HoldMaxBaseM"/> still gets somewhere to
    /// roll the wheel to; without this the band inverts and the clamp silently pins it.</summary>
    public const float MinScrollRangeM = 0.30f;

    /// <summary>One wheel notch, metres.
    ///
    /// <para><b>0.10 m, and the binding constraint is the CRATE, not taste.</b> A notch has to be
    /// a nudge you can feel, and the band it divides is narrowest for the fattest prop: the
    /// 0.44 m crate's <see cref="HoldMinM"/> is 0.79 m against a 1.2 m ceiling, so it has 0.41 m
    /// to roll through. At 0.12 m that is three and a bit notches — a wheel with four positions,
    /// which reads as a toggle rather than as a distance. At 0.10 m it is four clear notches and
    /// the can gets seven. <c>Scroll_TheBandIsAtLeastFourNotchesWideForEveryShippedProp</c> is
    /// the bar and it is what moved this number; it went red at 0.12.</para></summary>
    public const float ScrollStepM = 0.10f;

    /// <summary>How far the world may hold a held prop off its target before the hold is given
    /// up, metres — the standard physics-carry rule: you shoved it into a shelf and kept walking,
    /// so it is no longer in your hands.</summary>
    public const float BreakHoldM = 0.6f;

    /// <summary>...and for how long, seconds. A doorway clips a crate for a frame or two on
    /// every walk; only a sustained block is a lost hold.</summary>
    public const float BreakHoldSec = 0.3f;

    // --------------------------------------------------------------------- the prop's own bulk

    /// <summary>The radius of the sphere that contains this prop whatever way it is turned: half
    /// the diagonal of its axis-aligned bounds.
    ///
    /// <para>A sphere rather than the box, deliberately, and it is the conservative direction:
    /// the sphere contains the box, so a clearance proved against this is a clearance the box
    /// also has, at every orientation the player can spin the thing to. Testing the box would
    /// have to be redone every tick the hold rotation changes.</para></summary>
    public static float BoundingRadiusM(Vector3 aabbSize) => aabbSize.Length() * 0.5f;

    // --------------------------------------------------------------- how near it may ever come

    /// <summary>
    /// <b>The closest a held prop's centre may come to the holder's capsule axis</b>, metres —
    /// the holder's own collision radius, plus the prop's own bounding radius, plus
    /// <see cref="HolderSkinM"/>.
    ///
    /// <para>It is used for two different jobs and they are the same number on purpose: it is
    /// the floor of the scroll band (so the wheel can never pull an object into your chest), and
    /// it is the clearance radius <see cref="PushOutOfSegment"/> projects the prop out to every
    /// tick (so the spring's lag can never do it either). One derivation, so the two guarantees
    /// cannot drift apart.</para>
    /// </summary>
    public static float HoldMinM(float capsuleRadiusM, float propRadiusM) =>
        capsuleRadiusM + propRadiusM + HolderSkinM;

    /// <summary>The far end of the scroll band, metres — <see cref="HoldMaxBaseM"/>, or far
    /// enough past <see cref="HoldMinM"/> that a very large prop still has a band.</summary>
    public static float HoldMaxM(float holdMinM) =>
        Mathf.Max(HoldMaxBaseM, holdMinM + MinScrollRangeM);

    /// <summary>A requested hold distance, brought into the band.</summary>
    public static float ClampHoldDistance(float requestedM, float holdMinM, float holdMaxM) =>
        Mathf.Clamp(requestedM, holdMinM, Mathf.Max(holdMinM, holdMaxM));

    /// <summary>The wheel: <paramref name="notches"/> steps out (positive) or in (negative),
    /// clamped to the band.</summary>
    public static float Scroll(float currentM, int notches, float holdMinM, float holdMaxM) =>
        ClampHoldDistance(currentM + notches * ScrollStepM, holdMinM, holdMaxM);

    // ------------------------------------------------------------------------------ the spring

    /// <summary>
    /// <b>The slowest spring that keeps a held prop out of the holder while they move</b>, in
    /// rad/s.
    ///
    /// <para>A critically damped spring following a target that moves at a constant
    /// <paramref name="topSpeedMps"/> settles a fixed distance behind it:
    /// <c>lag = 2v/ω</c> (<see cref="SteadyStateLagM"/>). Walking FORWARD is the dangerous
    /// direction — the hold point runs ahead and the prop is left behind, i.e. nearer the body —
    /// so the bar is that the lag stays under half of <see cref="HoldMinM"/>, which gives
    /// <c>ω ≥ 4v / HoldMin</c>. It is a comfort bound, not the guarantee: the guarantee is
    /// <see cref="PushOutOfSegment"/>, applied to the pose actually written. This is what stops
    /// the projection from having to fire on an ordinary walk, which would read as the object
    /// sticking to an invisible shell.</para>
    ///
    /// <para><b>Talon removed sprint on 2026-09-20</b> ("no sprint button; they won't be moving
    /// far enough for it to matter"), so the top speed this is evaluated at is the walk itself.
    /// That is what makes the floor affordable: at the browse pace it lands near the feel
    /// system's own light-item response instead of an order of magnitude above it, and a heavy
    /// crate still lags like a heavy crate.</para>
    /// </summary>
    public static float MinOmega(float holdMinM, float topSpeedMps) =>
        4f * topSpeedMps / Mathf.Max(0.0001f, holdMinM);

    /// <summary>The spring response actually used for a hold: whatever the feel system's heft
    /// dial asked for, or <see cref="MinOmega"/>, whichever is faster. Heft still shapes
    /// everything below the floor, which is where both shipped props live at a browse pace.</summary>
    public static float HoldOmega(float springOmega, float holdMinM, float topSpeedMps) =>
        Mathf.Max(springOmega, MinOmega(holdMinM, topSpeedMps));

    /// <summary>How far behind a target moving at <paramref name="speedMps"/> a critically damped
    /// spring of response <paramref name="omega"/> settles, metres.</summary>
    public static float SteadyStateLagM(float omega, float speedMps) =>
        omega <= 0f ? float.PositiveInfinity : 2f * speedMps / omega;

    // ------------------------------------------------------------------------- the grabbed point

    /// <summary>Where the hand is this tick: along the view ray, at the hold distance. Not a
    /// chest socket — <i>"it shouldn't snap to any location"</i>.</summary>
    public static Vector3 HoldPoint(Vector3 eye, Vector3 direction, float distanceM) =>
        direction.LengthSquared() < 1e-12f ? eye : eye + direction.Normalized() * distanceM;

    /// <summary>The grabbed point expressed in the prop's own frame, so it survives every
    /// rotation the player then spins the object through — the object pivots about where you are
    /// holding it, which is the whole reason this is stored in local space.</summary>
    public static Vector3 GrabLocalFrom(Transform3D propAtGrab, Vector3 grabWorldPoint) =>
        propAtGrab.AffineInverse() * grabWorldPoint;

    /// <summary>Where the prop's origin has to be for its grabbed point to sit on the hold
    /// point, at this orientation.</summary>
    public static Vector3 PropOriginFor(Vector3 holdPoint, Basis poseBasis, Vector3 grabLocal) =>
        holdPoint - poseBasis * grabLocal;

    /// <summary>
    /// The prop's orientation at the grab, expressed relative to the holder's YAW — which is the
    /// only part of the holder's facing a hold follows.
    ///
    /// <para><b>Yaw only, and both halves of that are deliberate.</b> Following the yaw is what
    /// keeps the geometry of the grab intact as you turn: the vector from the grabbed point to
    /// the rest of the object stays in front of you instead of swinging round into you, which is
    /// the clip Talon rode into. NOT following the pitch is what stops the object tumbling when
    /// you look at the floor — a thing held out in front of you does not roll over because you
    /// looked down, and the hold point already moves with the pitch.</para>
    /// </summary>
    public static Basis HoldBasisLocal(float holderYaw, Basis propWorldBasis) =>
        new Basis(Vector3.Up, -holderYaw) * propWorldBasis.Orthonormalized();

    /// <summary>This tick's world orientation for the held prop: the holder's yaw, the rotation
    /// the player has spun it to since the grab, and the orientation it was grabbed at.</summary>
    public static Basis PoseBasis(float holderYaw, Basis heldRotation, Basis holdBasisLocal) =>
        (new Basis(Vector3.Up, holderYaw) * heldRotation.Orthonormalized() * holdBasisLocal.Orthonormalized())
            .Orthonormalized();

    // ------------------------------------------------------------------ never inside the holder

    /// <summary>
    /// <b>The guarantee</b>: a point pushed out to at least <paramref name="clearanceM"/> from
    /// the segment <paramref name="a"/>..<paramref name="b"/> — the holder's capsule axis —
    /// leaving it alone if it is already clear.
    ///
    /// <para>Applied to the pose that is actually written onto the body, every tick, on top of
    /// the distance clamp and the spring floor. Those two make it rare; this makes it certain.
    /// Idempotent, so applying it to its own output changes nothing.</para>
    ///
    /// <para><paramref name="fallbackDir"/> is used only in the degenerate case where the point
    /// is exactly on the axis and there is no direction to push along — the holder's own view
    /// direction, so the object comes back out in front of them rather than out of their
    /// back.</para>
    /// </summary>
    public static Vector3 PushOutOfSegment(Vector3 point, Vector3 a, Vector3 b, float clearanceM, Vector3 fallbackDir)
    {
        Vector3 closest = ClosestPointOnSegment(point, a, b);
        Vector3 away = point - closest;
        float dist = away.Length();
        if (dist >= clearanceM)
            return point;
        Vector3 dir = dist > 1e-5f
            ? away / dist
            : (fallbackDir.LengthSquared() > 1e-10f ? fallbackDir.Normalized() : Vector3.Forward);
        return closest + dir * clearanceM;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 1e-10f)
            return a;
        float t = Mathf.Clamp((p - a).Dot(ab) / lenSq, 0f, 1f);
        return a + ab * t;
    }

    // ------------------------------------------------------------------------------- the break

    /// <summary>How long the prop has been held off its target, after this tick: the clock runs
    /// while the gap is past <see cref="BreakHoldM"/> and resets the instant it catches up.</summary>
    public static float StepBlockedSeconds(float blockedSec, float blockedM, float dt) =>
        blockedM > BreakHoldM ? blockedSec + dt : 0f;

    /// <summary>Has the world held this prop off its target far enough, for long enough, that
    /// the holder has lost it?</summary>
    public static bool ShouldBreakHold(float blockedM, float blockedSec) =>
        blockedM > BreakHoldM && blockedSec >= BreakHoldSec;
}
