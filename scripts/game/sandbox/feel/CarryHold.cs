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

    /// <summary>...and for how long, seconds.
    ///
    /// <para><b>0.8 s, not the packet's 0.3 s, and the reason is the DRESSED room.</b> F4 named
    /// 0.3 s and that is the usual number for this rule; measured in the search room STOCK-1
    /// filled, it is far too short. A bot carrying a crate eleven metres down the z = -2.1
    /// walkway -- an ordinary journey, not a shove -- had the hold broken THREE times by brushing
    /// floor bins and pallet stacks it was walking past, and arrived empty-handed
    /// (<c>Run-PlaceTest</c>'s bot D, `[carry] hold broken prop=1016` three times in one run).
    /// Losing the object you are carrying because you clipped a bin is worse than the defect this
    /// rule exists to prevent.</para>
    ///
    /// <para>The rule still fires for what it is for: a prop the world holds for most of a second
    /// while you keep walking is genuinely out of your hands, and the prop is swept rather than
    /// teleported, so a brush that clears itself catches back up inside the window instead of
    /// costing the player their object. <b>A value call, stated rather than smuggled</b> -- if
    /// Talon rides it and a crate still gets ripped out of his hands on an aisle, this is the
    /// number to move, and if a prop hangs on a shelf too long, it is the same one.</para></summary>
    public const float BreakHoldSec = 0.8f;

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

    /// <summary>
    /// <b>The grabbed point, kept ON the object.</b> Where the view ray passes through the prop
    /// this is the ray point itself; where it misses — over the top of a crate on the floor, which
    /// is the ordinary case for anything aimed at from standing height — it is the nearest point
    /// of the prop's bounding sphere instead.
    ///
    /// <para><b>Measured, on this suite's second run.</b> Without the clamp a bot with no pitch
    /// grabbed a crate at y = 0.22 along a ray at eye height and came away with a grab offset
    /// 1.2 m long. That offset is a LEVER: the hold point orbits the holder at the hold distance,
    /// so a 180-degree turn in half a second sweeps it through pi x 1.2 = 3.8 m — 7.5 m/s, past
    /// any honest speed cap — and the prop fell far enough behind its target, for long enough,
    /// that the break-hold rule gave it up 0.3 s after every grab. The log said
    /// <c>hold broken prop=1014 blocked=1.23m for 0.30s</c>, nine samples after the grab, every
    /// run.</para>
    ///
    /// <para>It is also simply what a grab IS: you take hold of the object, not of a point in the
    /// air a metre away from it.</para></summary>
    public static Vector3 GrabPointOnProp(Vector3 propCentre, Vector3 rayPoint, float propRadiusM)
    {
        Vector3 away = rayPoint - propCentre;
        float dist = away.Length();
        return dist <= propRadiusM || dist < 1e-6f
            ? rayPoint
            : propCentre + away * (propRadiusM / dist);
    }

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

    // --------------------------------------------- a held prop cannot shove the world hard

    /// <summary>
    /// <b>The fastest a held prop may be driven through the world</b>, m/s — twice the holder's
    /// own top speed.
    ///
    /// <para><b>Why there is a cap at all.</b> Talon, 2026-09-20: <i>"I want to know objects
    /// won't freak out and make other objects jump around randomly … if they accidentally hit a
    /// bunch of boxes those should fall over like dominoes, cans should roll."</i> A spring that
    /// is allowed to resolve a whole metre in one tick hands whatever it meets a 60 m/s impulse,
    /// and the shelf goes across the room. Capping the STEP caps the energy a carry can ever
    /// deposit, whatever the response dial is set to and whatever the frame time was.</para>
    ///
    /// <para><b>Twice the walk, and both halves of that matter.</b> Below the walk speed the hold
    /// could not keep up with its own holder — the prop would trail further every tick and the
    /// hold would break on a straight line. Much above it and the cap stops capping anything a
    /// player can do on purpose. Two is the smallest multiple that leaves the scroll wheel and a
    /// turn-on-the-spot room to resolve without the cap firing on ordinary play.</para>
    /// </summary>
    public static float MaxHoldSpeedMps(float topSpeedMps) => 2f * topSpeedMps;

    /// <summary>One tick of hold motion, with <see cref="MaxHoldSpeedMps"/> applied: the step
    /// keeps its direction and loses whatever length is past <c>maxSpeed × dt</c>. A SPEED rather
    /// than a distance, so the bound is the same on a 30 Hz machine and a 240 Hz one.</summary>
    public static Vector3 CapStep(Vector3 from, Vector3 to, float maxSpeedMps, float dt)
    {
        Vector3 step = to - from;
        float limit = Mathf.Max(0f, maxSpeedMps) * Mathf.Max(0f, dt);
        float len = step.Length();
        return len <= limit || len < 1e-9f ? to : from + step * (limit / len);
    }

    // ------------------------------------------------------------------------------- the break

    /// <summary>How long the prop has been held off its target, after this tick: the clock runs
    /// while the gap is past <see cref="BreakHoldM"/> and resets the instant it catches up.
    ///
    /// <para>Superseded by <see cref="StepBlocked"/> for the live carry, which adds the half this
    /// one cannot see — whether the prop is STUCK or merely scraping past. Kept because it is the
    /// honest statement of the simple rule and the unit suite pins both.</para></summary>
    public static float StepBlockedSeconds(float blockedSec, float blockedM, float dt) =>
        blockedM > BreakHoldM ? blockedSec + dt : 0f;

    /// <summary>What counts as the prop WORKING ITSELF FREE rather than jittering in place,
    /// metres. Below this a change in the gap is solver noise and the clock keeps running; at or
    /// above it the prop has made real progress and the clock starts again.
    ///
    /// <para>2 cm, which is <c>PlacementIntegrity.DefaultOverlapToleranceM</c> — the same figure
    /// this game already uses for "near enough to be the same place" — rather than a second
    /// opinion about what a small distance is.</para></summary>
    public const float BlockedProgressM = 0.02f;

    /// <summary>The break clock, and the best (smallest) gap seen so far in this blocked episode.
    /// A value type, so a caller keeps one field and no more.</summary>
    public readonly record struct BlockedClock(float Seconds, float BestBlockedM)
    {
        /// <summary>A clock that has never run. <c>BestBlockedM</c> starts at infinity so the
        /// first blocked tick of an episode always records its own gap as the best.</summary>
        public BlockedClock() : this(0f, float.PositiveInfinity) { }
    }

    /// <summary>
    /// <b>One tick of the break clock.</b> It runs only while the prop is STUCK — the world is in
    /// the way, the gap is past <see cref="BreakHoldM"/>, and the gap is <b>not closing</b>.
    ///
    /// <para><b>The ruling this implements (2026-09-20).</b> The break rule exists for a prop that
    /// is stuck, not one that is dragging along shelving and still clearing. Measured on
    /// <c>Run-PlaceTest</c>'s bot D: an eleven-metre walk down a dressed walkway with a crate
    /// brushes floor bins and pallet stacks the whole way, and under a plain elapsed-time clock
    /// the hold was given up mid-journey (<c>blocked=1.83m for 0.82s</c>) — costing the player the
    /// object for doing the most ordinary thing in the game. That walk is the first thing a human
    /// will do with this carry, so the rule had to learn the difference rather than the suite
    /// learning to avoid it.</para>
    ///
    /// <para><b>"Not closing" is measured against the BEST gap of the episode, not the previous
    /// tick.</b> A tick-to-tick comparison reads solver jitter as progress and a genuinely wedged
    /// prop would never break; carrying the episode's minimum means only real progress — at least
    /// <see cref="BlockedProgressM"/> — restarts the clock, and a prop that oscillates in place
    /// still runs out of time.</para>
    /// </summary>
    public static BlockedClock StepBlocked(BlockedClock clock, float blockedM, bool worldInTheWay, float dt)
    {
        if (!worldInTheWay || blockedM <= BreakHoldM)
            return new BlockedClock();
        // The FIRST blocked tick of an episode counts: there is no previous best to have made
        // progress against, and a rule that spent its first tick recording rather than counting
        // would under-report every episode by one tick.
        if (float.IsInfinity(clock.BestBlockedM))
            return new BlockedClock(dt, blockedM);
        if (blockedM <= clock.BestBlockedM - BlockedProgressM)
            return new BlockedClock(0f, blockedM);   // it is working itself free
        return new BlockedClock(clock.Seconds + dt, Mathf.Min(clock.BestBlockedM, blockedM));
    }

    /// <summary>Has the world held this prop off its target far enough, for long enough, that
    /// the holder has lost it?</summary>
    public static bool ShouldBreakHold(float blockedM, float blockedSec) =>
        blockedM > BreakHoldM && blockedSec >= BreakHoldSec;
}
