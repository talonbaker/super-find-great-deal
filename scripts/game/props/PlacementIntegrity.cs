using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>Placement integrity, layer 1</b> — the server's answer to "may this prop be at that
/// transform?" (program doc §5b, a ruling from Talon on 2026-09-19).
///
/// <para><b>Why this is a deliberate, layered check and not a nicety.</b> Props clipping into
/// walls and into each other is the known failure mode of every prior playtest in this lineage.
/// In THIS game a seeker who cannot find the hidden object because of a physics glitch is the
/// worst outcome the design has — the round ends in a shrug rather than in a door bursting open
/// — so the check is explicit, it runs on the server, and every rejection says which of the two
/// tests failed and by how much. Silence is the defect class (INTERACTION-BIBLE §2).</para>
///
/// <para><b>Two tests, in this order.</b>
/// <list type="number">
/// <item>The prop's own collision shape, at the intended transform, is inside a room's authored
/// bounds volume (an <c>Area3D</c> in the <see cref="RoomBoundsGroup"/> group — one per room
/// section scene).</item>
/// <item>A shape overlap query at that transform, against static geometry and every other prop,
/// reports a penetration depth no deeper than <paramref name="overlapToleranceM"/> (default
/// <see cref="DefaultOverlapToleranceM"/>). Small overlaps are what the physics settle resolves;
/// a deep one is a prop inside a wall.</item>
/// </list>
/// Bounds first, because "you are trying to put it outside the room" is a more useful thing to be
/// told than "it overlaps the wall you are trying to put it through".</para>
///
/// <para><b>One method, two callers, on purpose.</b> CARRY-1 calls it inside the place RPC;
/// REACH-1 calls the identical method on every Resting latch (layer 2) and follows a failure with
/// depenetration and a last-good fallback. Two implementations of "is this prop somewhere legal"
/// would be two answers to one question, and the second one would be the one nobody
/// maintained.</para>
/// </summary>
public static class PlacementIntegrity
{
    /// <summary>Scene group every room's authored bounds <c>Area3D</c> belongs to. The group is
    /// declared in each room scene's <c>[node]</c> header (<c>.claude/rules/godot-scenes.md</c>:
    /// a group wired anywhere else fails silently and looks exactly like a feature that does
    /// nothing).</summary>
    public const string RoomBoundsGroup = "room_bounds";

    /// <summary>The node name every room's bounds volume carries, for the error text and for a
    /// level author looking for it in the scene tree. The GROUP is what the query uses; this is
    /// documentation that happens to be enforced by convention rather than by code.</summary>
    public const string RoomBoundsNodeName = "RoomBounds";

    /// <summary>How deep a shape may sit inside another before a placement is refused, metres.
    /// 0.02 m is roughly the depth ordinary resting contact reports once the solver has settled a
    /// box onto a slab; anything at that scale is resolved by the physics on the next tick, and
    /// refusing it would refuse setting a crate down on the floor.</summary>
    public const float DefaultOverlapToleranceM = 0.02f;

    /// <summary>Slack on the bounds test, metres. Same value as the overlap tolerance and for the
    /// same reason: a crate set down ON the floor has its bottom face exactly at the bounds
    /// volume's own floor, and an exact-equality test on a float would refuse it about half the
    /// time.</summary>
    public const float BoundsEpsilonM = DefaultOverlapToleranceM;

    /// <summary>Physics layers the overlap query looks at: layer 1, which is where this game puts
    /// static geometry AND released props (see <c>Carryable.Release</c>). A HELD prop sits on
    /// layer 0 and is therefore invisible here, which is correct — something in somebody's hand is
    /// not an obstacle, and it is about to move anyway.</summary>
    public const uint QueryMask = 1;

    /// <summary>Most contact pairs a single overlap query reports. The answer only needs the
    /// DEEPEST one, so the cap is a bound on work rather than on correctness; a prop buried in a
    /// dozen things is refused by the first pair as surely as by all twelve.</summary>
    private const int MaxContacts = 16;

    /// <summary>Which of the two tests refused, or <see cref="None"/>.</summary>
    public enum PlacementFault
    {
        /// <summary>Both tests passed.</summary>
        None = 0,

        /// <summary>The prop's shape, at the intended transform, is not fully inside any room's
        /// authored bounds volume.</summary>
        OutsideRoomBounds = 1,

        /// <summary>The prop's shape overlaps static geometry or another prop deeper than the
        /// tolerance. This is the "inside a wall" case.</summary>
        Overlapping = 2,

        /// <summary>The prop has no collision shape to test with. A level defect, not a player
        /// action — refused rather than waved through, because a prop with no shape is exactly the
        /// one that would end up inside geometry unnoticed.</summary>
        NoShape = 3,
    }

    /// <summary>
    /// One placement answer: which test refused, how deep the overlap was (0 for every other
    /// fault), and a line of text naming what it hit. The text is for the server log and the
    /// handoff — the player-facing reason is the <c>PlaceDenial</c> ordinal the RPC carries, so
    /// nothing here has to be localised or trusted across the wire.
    /// </summary>
    /// <param name="BlockerIsProp">REACH-1: true when the named blocker is another PROP rather
    /// than static geometry. Set only on the <see cref="PlacementFault.Overlapping"/> path,
    /// where the refusal already pays for a second query to name what it hit, so this costs
    /// nothing new. Layer 2's log line distinguishes <c>PropOverlap</c> from <c>StaticOverlap</c>
    /// with it, and the distinction is not cosmetic: a prop inside a WALL is the defect §5b
    /// exists to prevent, a prop inside another PROP is usually two crates the solver is about
    /// to push apart by itself.</param>
    public readonly record struct Verdict(PlacementFault Fault, float PenetrationM, string Detail,
        bool BlockerIsProp = false)
    {
        public static readonly Verdict Ok = new(PlacementFault.None, 0f, "");

        /// <summary>True when the placement may proceed.</summary>
        public bool Allowed => Fault == PlacementFault.None;

        public override string ToString() =>
            Allowed ? "ok" : $"{Fault} (penetration {PenetrationM:0.000} m) {Detail}".TrimEnd();
    }

    /// <summary>
    /// Run both tests on <paramref name="propBody"/> AS IF it were at <paramref name="at"/>. The
    /// body is never moved: the shape query is posed at the candidate transform, so this is safe
    /// to call on a prop that is currently in somebody's hand (which is exactly when CARRY-1 calls
    /// it) and on one that has already settled (which is when REACH-1 does).
    /// </summary>
    /// <param name="propBody">The prop's physical body. Its first <see cref="CollisionShape3D"/>
    /// child supplies the shape and its local offset.</param>
    /// <param name="at">The candidate world transform for the BODY.</param>
    /// <param name="excludeHolder">The holder's own physics Rid, excluded from the overlap query.
    /// Optional, and optional for a reason: a rest-time audit (layer 2) has no holder, and the
    /// packet's engine-facing signature is the two-argument one. Placing a crate at arm's length
    /// in front of you overlaps your own capsule, and refusing that would make the verb
    /// unusable.</param>
    /// <param name="overlapToleranceM">Penetration allowed before test 2 refuses.</param>
    public static Verdict Check(RigidBody3D propBody, Transform3D at, Rid? excludeHolder = null,
        float overlapToleranceM = DefaultOverlapToleranceM)
    {
        if (!GodotObject.IsInstanceValid(propBody) || !propBody.IsInsideTree())
            return new Verdict(PlacementFault.NoShape, 0f, "prop body is not in the tree");

        CollisionShape3D? shapeNode = FirstShapeOf(propBody);
        if (shapeNode?.Shape is not Shape3D shape)
            return new Verdict(PlacementFault.NoShape, 0f, $"'{propBody.Name}' has no CollisionShape3D");

        // The shape's own offset inside the body, carried through so a prop whose collider is not
        // centred on its origin is tested where the collider actually is.
        Transform3D shapeAt = at * shapeNode.Transform;

        Verdict bounds = CheckBounds(propBody, shape, shapeAt);
        if (!bounds.Allowed)
            return bounds;

        return CheckOverlap(propBody, shape, shapeAt, excludeHolder, overlapToleranceM);
    }

    // --- test 1: inside a room -------------------------------------------------------------

    /// <summary>
    /// Every corner of the shape's axis-aligned extent, at the candidate transform, must fall
    /// inside ONE room's bounds box.
    ///
    /// <para><b>One room, not the union of all of them.</b> The rooms in this game are 40 m apart
    /// with solid walls, so a placement that is half in one room and half in another is not a
    /// legal placement that this test is being pedantic about — it is a transform nobody can
    /// reach, i.e. a symptom. If a later level ever has two adjoining bounds volumes, this is the
    /// line that has to change, and it should change to "inside the union", deliberately.</para>
    ///
    /// <para><b>No bounds volume anywhere is a PASS, loudly.</b> The CI worlds ("open",
    /// "propsync") author no rooms, and every carry suite runs in them. Refusing every placement
    /// there would turn a missing level feature into a broken verb; warning once per call site
    /// would flood the log. So it passes and says so at the log tier — the room scenes' own
    /// self-test is what proves the real level has its volumes.</para>
    /// </summary>
    private static Verdict CheckBounds(Node3D propBody, Shape3D shape, Transform3D shapeAt)
    {
        SceneTree? tree = propBody.GetTree();
        if (tree == null)
            return Verdict.Ok;

        Godot.Collections.Array<Node> volumes = tree.GetNodesInGroup(RoomBoundsGroup);
        if (volumes.Count == 0)
            return Verdict.Ok; // no authored rooms in this world (the CI testbeds) — see the doc above

        Vector3 half = HalfExtentsOf(shape);
        foreach (Node node in volumes)
        {
            if (node is not Area3D area || !GodotObject.IsInstanceValid(area) || !area.IsInsideTree())
                continue;
            if (FirstShapeOf(area)?.Shape is not BoxShape3D box)
                continue;
            Transform3D boxWorld = area.GlobalTransform * FirstShapeOf(area)!.Transform;
            if (ExtentsInside(shape, half, shapeAt, boxWorld, box.Size * 0.5f))
                return Verdict.Ok;
        }
        return new Verdict(PlacementFault.OutsideRoomBounds, 0f,
            $"({shapeAt.Origin.X:0.00}, {shapeAt.Origin.Y:0.00}, {shapeAt.Origin.Z:0.00}) "
            + $"is not inside any of the {volumes.Count} authored {RoomBoundsNodeName} volume(s)");
    }

    /// <summary>All eight corners of the candidate's local extent, expressed in the bounds box's
    /// own frame, within its half-size plus <see cref="BoundsEpsilonM"/>.</summary>
    private static bool ExtentsInside(Shape3D shape, Vector3 half, Transform3D shapeAt,
        Transform3D boxWorld, Vector3 boxHalf)
    {
        // The shape's frame expressed in the bounds volume's frame: its centre and its extents
        // along the volume's own axes. A box's extents are its corner sweep; a round shape's are
        // its TRUE extents, which do not grow as it rotates about its own axis -- PHYS-2, and
        // PropPhysics.RoundExtents says what it cost to sweep corners instead.
        Transform3D inBox = boxWorld.AffineInverse() * shapeAt;
        Vector3 c = inBox.Origin;
        Vector3 axis = inBox.Basis.Y.LengthSquared() > 0f ? inBox.Basis.Y.Normalized() : Vector3.Up;
        Vector3 ext = shape switch
        {
            SphereShape3D sphere => Vector3.One * sphere.Radius,
            CylinderShape3D cylinder => PropPhysics.RoundExtents(axis, cylinder.Height * 0.5f, cylinder.Radius, roundEnds: false),
            CapsuleShape3D capsule => PropPhysics.RoundExtents(axis,
                Mathf.Max(0f, capsule.Height * 0.5f - capsule.Radius), capsule.Radius, roundEnds: true),
            _ => PropPhysics.BoxExtents(inBox.Basis, half),
        };
        return Mathf.Abs(c.X) + ext.X <= boxHalf.X + BoundsEpsilonM
            && Mathf.Abs(c.Y) + ext.Y <= boxHalf.Y + BoundsEpsilonM
            && Mathf.Abs(c.Z) + ext.Z <= boxHalf.Z + BoundsEpsilonM;
    }

    /// <summary>
    /// The shape's half-extents in its own frame.
    ///
    /// <para><b>An explicit switch rather than <c>Shape3D.GetDebugMesh().GetAabb()</c>.</b> The
    /// debug-mesh route allocates an ArrayMesh per call on a path that runs once per placement
    /// AND once per settle for every prop in the room; this game's props are boxes and spheres,
    /// and the four cases below cover every shape the engine's own primitives offer that anything
    /// here could plausibly use. An unknown shape falls back to a conservative 0.25 m cube and
    /// says so, which errs toward refusing a placement rather than toward waving a prop into a
    /// wall.</para>
    /// </summary>
    public static Vector3 HalfExtentsOf(Shape3D shape) => shape switch
    {
        BoxShape3D box => box.Size * 0.5f,
        SphereShape3D sphere => Vector3.One * sphere.Radius,
        CapsuleShape3D capsule => new Vector3(capsule.Radius, capsule.Height * 0.5f, capsule.Radius),
        CylinderShape3D cylinder => new Vector3(cylinder.Radius, cylinder.Height * 0.5f, cylinder.Radius),
        _ => Vector3.One * 0.25f,
    };

    // --- test 2: not inside anything -------------------------------------------------------

    /// <summary>
    /// A shape overlap query at the candidate transform, reporting the DEEPEST contact.
    ///
    /// <para><c>CollideShape</c> returns contact PAIRS — a point on the queried shape and the
    /// matching point on whatever it hit — so the distance between a pair IS the penetration
    /// depth. <c>IntersectShape</c> alone would only say "something is there", which cannot
    /// distinguish a crate resting on a shelf from a crate inside it, and that distinction is the
    /// whole point of the tolerance.</para>
    /// </summary>
    private static Verdict CheckOverlap(RigidBody3D propBody, Shape3D shape, Transform3D shapeAt,
        Rid? excludeHolder, float overlapToleranceM)
    {
        PhysicsDirectSpaceState3D? space = propBody.GetWorld3D()?.DirectSpaceState;
        if (space == null)
            return Verdict.Ok;

        var exclude = new Godot.Collections.Array<Rid> { propBody.GetRid() };
        if (excludeHolder is Rid holder)
            exclude.Add(holder);
        // EVERY PLAYER'S BODY IS EXCLUDED, not only the holder's — and §5b says so in as many
        // words: the test is "shape overlap against STATIC GEOMETRY and EVERY OTHER PROP".
        //
        // This was found the expensive way (CARRY-1, 2026-09-19). With only the holder excluded,
        // Run-CarryTest went red: two bots walked to the same crate, and when the holder pressed E
        // to put it down the placement was refused `DoesNotFitThere` because the crate — held out
        // in front of the holder's chest — penetrated the OTHER bot's capsule by 0.294 m. The prop
        // stayed in the hand and the suite's drop never happened.
        //
        // The refusal was correct code answering the wrong question. A wall traps a prop forever
        // and a shelf back hides it where nobody can reach; a player standing there walks away a
        // second later, and the physics settle that follows a placement resolves the contact by
        // itself. Refusing on a body that is about to move means "put this down" fails whenever a
        // teammate is standing close — which in a two-player co-op game is most of the time, and
        // is the same defect class as a single key whose constructive meaning can silently lose
        // (SandboxAvatar.FindNearestUnavailableCarryable's incident).
        foreach (MpFoundation.Game.Sandbox.SandboxAvatar avatar
                 in MpFoundation.Game.Sandbox.SandboxAvatar.Live)
        {
            if (GodotObject.IsInstanceValid(avatar) && avatar.IsInsideTree())
                exclude.Add(avatar.GetRid());
        }

        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = shapeAt,
            CollisionMask = QueryMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = exclude,
            // Margin 0: the tolerance below is the ONE place a "how close is too close" number
            // lives. A non-zero query margin would be a second, invisible one.
            Margin = 0f,
        };

        Godot.Collections.Array<Vector3> contacts = space.CollideShape(query, MaxContacts);
        float deepest = 0f;
        for (int i = 0; i + 1 < contacts.Count; i += 2)
        {
            float depth = contacts[i].DistanceTo(contacts[i + 1]);
            if (depth > deepest)
                deepest = depth;
        }
        if (deepest <= overlapToleranceM)
            return Verdict.Ok;

        // Name the blocker. A second, cheaper query, run ONLY on the refusal path — a placement
        // that passes never pays for it, and a refusal that cannot say what it hit is the kind of
        // bug report nobody can act on.
        string what = "something";
        bool blockerIsProp = false;
        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, 1);
        if (hits.Count > 0 && hits[0].TryGetValue("collider", out Variant collider)
            && collider.As<GodotObject>() is Node hitNode)
        {
            what = hitNode.GetPath().ToString();
            // REACH-1: static geometry or another prop? The prop bodies in this game are all
            // Carryable, and a prop's own NetworkedProp is its parent — checking the node itself
            // rather than the registry keeps this usable from a layer-2 audit that has no
            // PropManager in scope (the planted self-test is exactly that case).
            blockerIsProp = hitNode is MpFoundation.Game.Sandbox.Carryable
                            || hitNode.GetParent() is NetworkedProp;
        }

        return new Verdict(PlacementFault.Overlapping, deepest,
            $"penetrates {what} by {deepest:0.000} m (tolerance {overlapToleranceM:0.000} m)",
            blockerIsProp);
    }

    // --- REACH-1: the correction half of layer 2 -------------------------------------------

    /// <summary>
    /// Try to push <paramref name="propBody"/> OUT of whatever it is inside, from
    /// <paramref name="at"/>, by at most <paramref name="maxTranslationM"/> metres.
    ///
    /// <para><b>The minimum translation, from the engine's own rest info.</b>
    /// <see cref="PhysicsDirectSpaceState3D.GetRestInfo"/> reports the deepest contact's point
    /// and its NORMAL — the direction out of the thing the shape is inside — and
    /// <see cref="CollideShape"/> gives the depth. Moving along the normal by the depth (plus a
    /// hair, so the re-test is not deciding a float tie) is the minimum translation vector, and
    /// it is the only push that is guaranteed not to make some other overlap worse than the one
    /// it is fixing.</para>
    ///
    /// <para><b>It re-checks, and a push that does not clear is not applied.</b> 0.15 m out of a
    /// 1 m pillar leaves the crate still inside it; reporting that as corrected would be worse
    /// than reporting the failure, because the caller's fallback (the last good transform) is
    /// the thing that actually saves the round.</para>
    ///
    /// <para><b>Iterative, up to <see cref="DepenetrateSteps"/> pushes.</b> A crate wedged in the
    /// corner where a wall meets a shelf is inside TWO things, and one push along one normal
    /// clears one of them; the second pass sees the other. The total distance moved is still
    /// capped at <paramref name="maxTranslationM"/>, so this buys correctness in a corner
    /// without buying a prop that walks across the room.</para>
    ///
    /// <para><b>It judges by the CALLER's tolerance</b> (REVIEW-1 I5, 2026-09-20). All three
    /// decisions inside the loop — is the depth worth pushing out of, does the pushed candidate
    /// pass, does the un-pushed one — used to read
    /// <see cref="DefaultOverlapToleranceM"/> directly, while <see cref="RestAudit.Correct"/>
    /// hands the value down. Today they are the same number
    /// (<c>PropManager.PlaceOverlapToleranceM</c> is aliased to the default), so nothing was
    /// visibly wrong; the day anyone moves one, this loop starts reporting <c>Depenetrated</c>
    /// for a pose the audit that called it then refuses, or refuses a pose the audit would have
    /// accepted and sends a hidden object back to its last-good transform for no reason. That is
    /// precisely the drift <see cref="Check"/>'s own parameter doc says the argument exists to
    /// prevent.</para>
    /// </summary>
    /// <param name="overlapToleranceM">Penetration allowed before a pose is refused — the same
    /// bar the caller's own <see cref="Check"/> will apply. Not optional: it sits before the
    /// <c>out</c> parameters, and a default here is how the two layers drifted in the first
    /// place.</param>
    /// <returns>True with <paramref name="corrected"/> set to a transform that passes
    /// <see cref="Check"/> at <paramref name="overlapToleranceM"/>; false with
    /// <paramref name="corrected"/> left at <paramref name="at"/>.</returns>
    public static bool TryDepenetrate(RigidBody3D propBody, Transform3D at, float maxTranslationM,
        float overlapToleranceM, out Transform3D corrected, out float movedM, out int queries)
    {
        corrected = at;
        movedM = 0f;
        queries = 0;

        if (!GodotObject.IsInstanceValid(propBody) || !propBody.IsInsideTree())
            return false;
        CollisionShape3D? shapeNode = FirstShapeOf(propBody);
        if (shapeNode?.Shape is not Shape3D shape)
            return false;
        PhysicsDirectSpaceState3D? space = propBody.GetWorld3D()?.DirectSpaceState;
        if (space == null)
            return false;

        Godot.Collections.Array<Rid> exclude = ExclusionsFor(propBody);
        Transform3D candidate = at;

        for (int step = 0; step < DepenetrateSteps; step++)
        {
            Transform3D shapeAt = candidate * shapeNode.Transform;
            var query = new PhysicsShapeQueryParameters3D
            {
                Shape = shape,
                Transform = shapeAt,
                CollisionMask = QueryMask,
                CollideWithBodies = true,
                CollideWithAreas = false,
                Exclude = exclude,
                Margin = 0f,
            };

            queries++;
            Godot.Collections.Dictionary rest = space.GetRestInfo(query);
            if (rest.Count == 0 || !rest.TryGetValue("normal", out Variant n))
                break;   // nothing to push out of; the Check below is the authority either way
            Vector3 normal = n.AsVector3();
            if (normal.LengthSquared() < 0.0001f)
                break;

            queries++;
            float depth = DeepestContact(space, query);
            if (depth <= overlapToleranceM)
                break;

            float push = Mathf.Min(depth + DepenetrateSlackM, maxTranslationM - movedM);
            if (push <= 0f)
                break;
            candidate.Origin += normal.Normalized() * push;
            movedM += push;

            queries++;
            if (Check(propBody, candidate, null, overlapToleranceM).Allowed)
            {
                corrected = candidate;
                return true;
            }
            if (movedM >= maxTranslationM)
                break;
        }

        // One last honest answer: the loop may have broken out because the overlap was already
        // inside tolerance, in which case the candidate IS good and saying otherwise would send
        // a perfectly placed prop back to its last good transform for no reason.
        queries++;
        if (movedM > 0f && Check(propBody, candidate, null, overlapToleranceM).Allowed)
        {
            corrected = candidate;
            return true;
        }
        movedM = 0f;
        return false;
    }

    /// <summary>How many push-and-re-test passes <see cref="TryDepenetrate"/> makes. Three: one
    /// for the ordinary single contact, two more for a corner. Beyond that the answer is "it is
    /// stuck", and the last good transform is the better tool.</summary>
    public const int DepenetrateSteps = 3;

    /// <summary>Extra distance added to a push so the re-test is not deciding a float tie,
    /// metres.</summary>
    public const float DepenetrateSlackM = 0.005f;

    /// <summary>The deepest contact pair a shape query reports, metres. Split out of
    /// <see cref="CheckOverlap"/> so the depenetration loop measures depth with the same code
    /// the refusal does.</summary>
    private static float DeepestContact(PhysicsDirectSpaceState3D space,
        PhysicsShapeQueryParameters3D query)
    {
        Godot.Collections.Array<Vector3> contacts = space.CollideShape(query, MaxContacts);
        float deepest = 0f;
        for (int i = 0; i + 1 < contacts.Count; i += 2)
        {
            float depth = contacts[i].DistanceTo(contacts[i + 1]);
            if (depth > deepest)
                deepest = depth;
        }
        return deepest;
    }

    /// <summary>The prop itself plus every avatar — the same exclusion set
    /// <see cref="CheckOverlap"/> builds, for the same reason (see its comment). Shared so the
    /// depenetration loop can never be pushing out of a body the audit is about to ignore.
    /// </summary>
    private static Godot.Collections.Array<Rid> ExclusionsFor(RigidBody3D propBody)
    {
        var exclude = new Godot.Collections.Array<Rid> { propBody.GetRid() };
        foreach (MpFoundation.Game.Sandbox.SandboxAvatar avatar
                 in MpFoundation.Game.Sandbox.SandboxAvatar.Live)
        {
            if (GodotObject.IsInstanceValid(avatar) && avatar.IsInsideTree())
                exclude.Add(avatar.GetRid());
        }
        return exclude;
    }

    /// <summary>The first <see cref="CollisionShape3D"/> child of a body or area, by tree order.
    /// Found by TYPE rather than by the name "CollisionShape3D": the two prop scenes and the
    /// code-built fallback happen to agree on that name today, and a level author adding a prop
    /// with a differently-named collider should get a working prop rather than a silent
    /// <see cref="PlacementFault.NoShape"/>.</summary>
    private static CollisionShape3D? FirstShapeOf(Node node)
    {
        foreach (Node child in node.GetChildren())
            if (child is CollisionShape3D cs)
                return cs;
        return null;
    }
}
